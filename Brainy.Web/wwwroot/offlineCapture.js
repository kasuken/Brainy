/**
 * offlineCapture.js — Offline Lite (issue #302) queued-capture module.
 *
 * Loaded three places, all as a plain classic script so the exact same code runs
 * everywhere: `App.razor` (for the live app, including the /capture/share Blazor page),
 * `offline.html` (the static fallback page, no Blazor/circuit at all), and
 * `service-worker.js` via `importScripts()` (for Background Sync). It always attaches
 * itself to `self`, which resolves to `window` in the first two and to the worker's
 * global scope in the third — never `window` directly, since that does not exist there.
 *
 * ## Data lifecycle / security model (read before editing)
 * - The queue lives in IndexedDB ("brainy-offline" database, "captureQueue" store) —
 *   never sessionStorage or an in-memory array — so it survives a full browser restart.
 * - Items are created with status 'queued'. flushQueue() POSTs them (batched) to
 *   POST /api/offline/captures/sync. Each item carries a client-generated GUID
 *   idempotency key that the server checks against a database-unique ledger, so a batch
 *   retried after a dropped connection still creates each queued capture exactly once —
 *   the "syncs exactly once" acceptance criterion for issue #302.
 * - A capture is NEVER shown as "saved"/"synced" until the server has actually
 *   acknowledged it. Status transitions: queued -> syncing -> synced | failed.
 *   'failed' means the SERVER rejected the item (a real validation failure); a network
 *   error or still-offline attempt reverts the item back to 'queued' so it is retried,
 *   never silently marked failed.
 * - Sign-out (see MainLayout.razor's logout hook) calls clearSyncedRecords(), which
 *   removes only 'synced' records — capture content already safely stored on the
 *   server, so keeping it cached locally after logout would just be exposing another
 *   user's already-server-side content on a shared device for no benefit. 'queued' and
 *   'failed' records are deliberately LEFT ALONE at sign-out: they are real,
 *   not-yet-saved user work, and clearing them would silently destroy it.
 * - This queue only ever creates new Inbox notes (pure appends, never edits), so there
 *   is no server-side state a queued item could conflict with — see getQueueSnapshot's
 *   doc comment for why a 'conflicted' status is not implemented in this Lite scope.
 */
(function () {
  const DB_NAME = 'brainy-offline';
  const DB_VERSION = 1;
  const STORE = 'captureQueue';
  const SYNC_ENDPOINT = '/api/offline/captures/sync';
  const SYNC_TAG = 'offline-capture-sync';
  // A synced item is kept around briefly after success purely so a status badge can
  // show "Synced" for a moment instead of the item just vanishing; not a retention policy.
  const SYNCED_RETENTION_MS = 5 * 60 * 1000;

  function openDb() {
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(DB_NAME, DB_VERSION);
      request.onupgradeneeded = () => {
        const db = request.result;
        if (!db.objectStoreNames.contains(STORE)) {
          db.createObjectStore(STORE, { keyPath: 'id' });
        }
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }

  function requestToPromise(request) {
    return new Promise((resolve, reject) => {
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }

  function txDone(tx) {
    return new Promise((resolve, reject) => {
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error);
    });
  }

  async function getAllRecords() {
    const db = await openDb();
    const tx = db.transaction(STORE, 'readonly');
    return await requestToPromise(tx.objectStore(STORE).getAll());
  }

  async function putRecord(record) {
    const db = await openDb();
    const tx = db.transaction(STORE, 'readwrite');
    tx.objectStore(STORE).put(record);
    await txDone(tx);
  }

  async function deleteRecord(id) {
    const db = await openDb();
    const tx = db.transaction(STORE, 'readwrite');
    tx.objectStore(STORE).delete(id);
    await txDone(tx);
  }

  function newId() {
    if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID();
    // Fallback for very old browsers without crypto.randomUUID: still random enough
    // for a client-side idempotency key (the server also de-dupes by content/user/time).
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = (Math.random() * 16) | 0;
      const v = c === 'x' ? r : (r & 0x3) | 0x8;
      return v.toString(16);
    });
  }

  function isOnline() {
    return typeof navigator === 'undefined' ? true : navigator.onLine;
  }

  async function registerBackgroundSync() {
    try {
      if (typeof navigator === 'undefined' || !('serviceWorker' in navigator)) return false;
      const registration = await navigator.serviceWorker.ready;
      if (!('sync' in registration)) return false; // e.g. Safari/Firefox: no Background Sync API
      await registration.sync.register(SYNC_TAG);
      return true;
    } catch {
      return false;
    }
  }

  /**
   * Queues one capture for offline sync. Returns the generated id immediately; the
   * caller should treat the item as "queued on this device", never as "saved" — only a
   * later 'synced' status (see getQueueSnapshot) means the server has it.
   * @param {{title?: string, text?: string, url?: string}} input
   */
  async function enqueue(input) {
    const record = {
      id: newId(),
      title: (input && input.title) || null,
      text: (input && input.text) || '',
      url: (input && input.url) || null,
      status: 'queued',
      createdAtUtc: new Date().toISOString(),
      syncedAtUtc: null,
      error: null
    };
    await putRecord(record);

    // Best-effort: try a background-sync registration (works even if the tab closes
    // before connectivity returns) and an immediate foreground attempt (covers browsers
    // without Background Sync, and the common case of already being online right now).
    registerBackgroundSync().catch(() => {});
    flushQueue().catch(() => {});

    return record.id;
  }

  /** Attempts to sync every queued/failed item. Safe to call repeatedly/concurrently. */
  async function flushQueue() {
    if (!isOnline()) return { attempted: false };

    const all = await getAllRecords();
    const pending = all.filter((r) => r.status === 'queued' || r.status === 'failed');
    if (pending.length === 0) {
      await pruneSyncedRecords(all);
      return { attempted: false };
    }

    // Mark as syncing first so a concurrent flush (foreground load + a background-sync
    // event firing around the same time) does not post the same items twice.
    for (const record of pending) {
      record.status = 'syncing';
      await putRecord(record);
    }

    let response;
    try {
      response = await fetch(SYNC_ENDPOINT, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          items: pending.map((r) => ({ idempotencyKey: r.id, title: r.title, text: r.text, url: r.url }))
        })
      });
    } catch {
      await revertToQueued(pending);
      return { attempted: true, ok: false };
    }

    if (!response.ok) {
      await revertToQueued(pending);
      return { attempted: true, ok: false };
    }

    let body;
    try {
      body = await response.json();
    } catch {
      await revertToQueued(pending);
      return { attempted: true, ok: false };
    }

    const nowIso = new Date().toISOString();
    for (const item of (body && body.items) || []) {
      const record = pending.find((r) => r.id === item.idempotencyKey);
      if (!record) continue;

      if (item.outcome === 'rejected') {
        record.status = 'failed';
        record.error = item.error || 'Brainy could not save this capture.';
      } else {
        record.status = 'synced';
        record.syncedAtUtc = nowIso;
        record.error = null;
      }
      await putRecord(record);
    }

    await pruneSyncedRecords(await getAllRecords());
    return { attempted: true, ok: true };
  }

  async function revertToQueued(records) {
    for (const record of records) {
      record.status = 'queued';
      await putRecord(record);
    }
  }

  async function pruneSyncedRecords(all) {
    const cutoff = Date.now() - SYNCED_RETENTION_MS;
    for (const record of all) {
      if (record.status === 'synced' && record.syncedAtUtc && new Date(record.syncedAtUtc).getTime() < cutoff) {
        await deleteRecord(record.id);
      }
    }
  }

  /**
   * Returns every queued item, newest first, for status-badge UI. Only ever reports
   * queued/syncing/synced/failed — never "conflicted": this Lite-scope queue only ever
   * creates brand-new Inbox notes (no offline edits of existing data), so there is no
   * server-side state a queued item could actually conflict with. A speculative
   * "conflicted" UI for a state that cannot occur was deliberately not built.
   */
  async function getQueueSnapshot() {
    const all = await getAllRecords();
    all.sort((a, b) => (a.createdAtUtc < b.createdAtUtc ? 1 : -1));
    return all;
  }

  /** Removes only successfully-synced records — see the sign-out note in the file header. */
  async function clearSyncedRecords() {
    const all = await getAllRecords();
    for (const record of all) {
      if (record.status === 'synced') await deleteRecord(record.id);
    }
  }

  function initForegroundSync() {
    if (typeof window === 'undefined') return; // Running inside the service worker itself.
    window.addEventListener('online', () => { flushQueue().catch(() => {}); });
    // Also try once on load: covers "connectivity had already returned while this tab/app
    // was closed", which no 'online' event will ever fire for.
    flushQueue().catch(() => {});
  }

  self.offlineCapture = {
    SYNC_TAG,
    enqueue,
    flushQueue,
    getQueueSnapshot,
    clearSyncedRecords
  };

  initForegroundSync();
})();
