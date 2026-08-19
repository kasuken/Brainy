// Install-only service worker. No offline cache or app shell.
// A pass-through fetch handler satisfies installability checks on browsers
// that still require one, while every request continues to hit the network.

self.addEventListener('install', (event) => {
  event.waitUntil(self.skipWaiting());
});

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', (event) => {
  event.respondWith(fetch(event.request));
});
