/**
 * offlineSnapshot.js — Offline Lite (issue #302) read-only Today/current-focus/favorites
 * snapshot, cached client-side for the static offline fallback page (offline.html).
 *
 * ## What this stores and why
 * - A single small JSON blob in localStorage, refreshed opportunistically while the app is
 *   online (see refresh(), called from Home.razor's Today page on load). It is a point-in-
 *   time SNAPSHOT, not live data — it will not reflect anything changed since it was last
 *   fetched, and the offline page must always say so (see isStale()/STALE_AFTER_MS below).
 * - Cleared entirely at sign-out (clearSnapshot(), wired from MainLayout.razor's logout
 *   form) so a shared/public device does not keep another user's Today/current-focus/
 *   favorites data cached in the browser after logout. Unlike the offline capture queue,
 *   there is nothing here to preserve across a sign-out — this is read-only, already-on-
 *   the-server data, never unsynced user work.
 * - Treated as stale after STALE_AFTER_MS regardless of whether it is displayed, so a
 *   days-old "current focus" is never shown as if it might still be current.
 */
(function () {
  const STORAGE_KEY = 'brainy:offline-snapshot:v1';
  const ENDPOINT = '/api/offline/today-snapshot';
  // 48h: long enough to survive a weekend without a refresh, short enough that a stale
  // "current focus" reads as obviously outdated rather than plausible.
  const STALE_AFTER_MS = 48 * 60 * 60 * 1000;

  function isOnline() {
    return typeof navigator === 'undefined' ? true : navigator.onLine;
  }

  /** Fetches the latest snapshot and stores it. Returns null on any failure (including offline). */
  async function refresh() {
    if (!isOnline()) return null;
    try {
      const response = await fetch(ENDPOINT, { credentials: 'same-origin' });
      if (!response.ok) return null;
      const data = await response.json();
      const snapshot = { fetchedAtUtc: new Date().toISOString(), data };
      localStorage.setItem(STORAGE_KEY, JSON.stringify(snapshot));
      return snapshot;
    } catch {
      return null;
    }
  }

  /** Reads the last-cached snapshot, or null if none has ever been stored. */
  function read() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }

  /** True when the snapshot is missing or older than STALE_AFTER_MS. */
  function isStale(snapshot) {
    if (!snapshot || !snapshot.fetchedAtUtc) return true;
    return Date.now() - new Date(snapshot.fetchedAtUtc).getTime() > STALE_AFTER_MS;
  }

  /** Sign-out hook: removes the cached snapshot entirely (see file header). */
  function clearSnapshot() {
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      /* ignored: nothing to clear if storage is unavailable */
    }
  }

  self.offlineSnapshot = { refresh, read, isStale, clearSnapshot, STALE_AFTER_MS };
})();
