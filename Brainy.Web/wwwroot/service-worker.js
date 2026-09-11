// Offline Lite (issue #302): a small, explicit app shell (this offline fallback page plus the
// two scripts it needs) is cached at install time. Every other request — every Blazor Server
// page, every API call, the SignalR/WebSocket circuit, all static assets — still goes straight
// to the network exactly as before. This deliberately does NOT try to make live Razor pages
// (Today, /capture/share, etc.) work offline: they need a live Interactive Server circuit,
// which cannot exist without connectivity. Instead:
//   - A failed *navigation* (the user opened/refreshed a page with no connectivity) falls back
//     to the cached OFFLINE_URL, a genuinely static, non-Blazor HTML page. That page reads its
//     own cached Today/current-focus/favorites snapshot (offlineSnapshot.js, localStorage) and
//     the offline capture queue (offlineCapture.js, IndexedDB) to render a read-only view plus
//     a manual capture form — see offline.html for the full data-lifecycle/security notes.
//   - Because the browser keeps the originally-requested URL (and its query string) even
//     though this fallback's markup is what renders, the same OFFLINE_URL also transparently
//     covers an offline PWA share-target invocation (GET /capture/share?title=...&text=...&
//     url=...) — offline.html detects the share query params itself and queues them.
//   - Captured content is queued client-side in IndexedDB (offlineCapture.js) independent of
//     any Razor/SignalR circuit, and synced via a plain POST /api/offline/captures/sync
//     endpoint — never a false "saved" claim before the server actually has it.
const CACHE_NAME = 'brainy-offline-v1';
const OFFLINE_URL = 'offline.html';
const APP_SHELL = [OFFLINE_URL, 'offlineCapture.js', 'offlineSnapshot.js'];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then((cache) => cache.addAll(APP_SHELL))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (event) => {
  const request = event.request;

  // Only top-level page navigations get the offline fallback. Everything else (API
  // calls, static assets, the SignalR hub) is untouched, same as before this file
  // existed — Offline Lite does not cache or intercept any of that.
  if (request.mode !== 'navigate') {
    event.respondWith(fetch(request));
    return;
  }

  event.respondWith(
    fetch(request).catch(() => caches.match(OFFLINE_URL))
  );
});

// Loaded as a classic script so it can attach to `self` here too (see its own header
// comment) — lets the capture queue's sync logic live in exactly one place instead of
// being duplicated between the page context and the service worker.
importScripts('offlineCapture.js');

self.addEventListener('sync', (event) => {
  if (event.tag === self.offlineCapture.SYNC_TAG) {
    event.waitUntil(self.offlineCapture.flushQueue());
  }
});
