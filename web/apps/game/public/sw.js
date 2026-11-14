/* NobodyFalls service worker (GAME_DESIGN §11): app shell offline, Web Push. */
const SHELL = 'nf-shell-v1';
const ASSETS = 'nf-assets-v1';
const SHELL_URLS = ['/fr/', '/en/', '/manifest.webmanifest'];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(SHELL)
      .then((cache) => cache.addAll(SHELL_URLS))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(keys.filter((k) => k !== SHELL && k !== ASSETS).map((k) => caches.delete(k))),
      )
      .then(() => self.clients.claim()),
  );
});

self.addEventListener('fetch', (event) => {
  const request = event.request;
  const url = new URL(request.url);
  if (request.method !== 'GET' || url.origin !== self.location.origin) {
    return;
  }
  // Live game data is never cached.
  if (/^\/(api|hubs|auth|dev)\//.test(url.pathname)) {
    return;
  }

  // Hashed build files never change: cache first.
  if (url.pathname.startsWith('/assets/')) {
    event.respondWith(
      caches.open(ASSETS).then(async (cache) => {
        const cached = await cache.match(request);
        if (cached) {
          return cached;
        }
        const response = await fetch(request);
        if (response.ok) {
          cache.put(request, response.clone());
        }
        return response;
      }),
    );
    return;
  }

  // Pages: the network first (fresh index), the cached shell when offline.
  if (request.mode === 'navigate') {
    event.respondWith(
      fetch(request)
        .then((response) => {
          const lang = url.pathname.startsWith('/en') ? '/en/' : '/fr/';
          if (response.ok) {
            caches.open(SHELL).then((cache) => cache.put(lang, response.clone()));
          }
          return response;
        })
        .catch(
          async () =>
            (await caches.match(url.pathname.startsWith('/en') ? '/en/' : '/fr/')) ?? Response.error(),
        ),
    );
  }
});

/* Payload sent by the server: { title, body, url, tag } (ADR 0009). */
self.addEventListener('push', (event) => {
  let message = { title: 'NobodyFalls', body: '', url: '/', tag: 'nobodyfalls' };
  try {
    message = { ...message, ...event.data.json() };
  } catch {
    // Keep the defaults.
  }
  event.waitUntil(
    self.registration.showNotification(message.title, {
      body: message.body,
      tag: message.tag,
      icon: '/icons/icon-192.png',
      badge: '/icons/badge-96.png',
      data: { url: message.url },
    }),
  );
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const target = new URL(event.notification.data?.url ?? '/', self.location.origin).href;
  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((windows) => {
      const open = windows.find((w) => new URL(w.url).origin === self.location.origin);
      if (open) {
        return open.navigate(target).then((w) => (w ?? open).focus());
      }
      return self.clients.openWindow(target);
    }),
  );
});
