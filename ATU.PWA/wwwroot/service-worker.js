// ATU Service Worker - Soporte offline y caché estratégico
const CACHE_NAME = 'atu-v1';
const STATIC_ASSETS = [
    '/',
    '/index.html',
    '/manifest.json',
];

// ── Install: Pre-caché de assets estáticos ─────────────────────────────────────
self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME).then(cache => cache.addAll(STATIC_ASSETS))
    );
    self.skipWaiting();
});

// ── Activate: Limpiar cachés anteriores ───────────────────────────────────────
self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys().then(keys =>
            Promise.all(keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k)))
        )
    );
    self.clients.claim();
});

// ── Fetch: Estrategia Network-First para API, Cache-First para estáticos ───────
self.addEventListener('fetch', (event) => {
    const url = new URL(event.request.url);

    // API calls: siempre network (datos críticos, nunca usar caché)
    if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/hubs/')) {
        event.respondWith(fetch(event.request));
        return;
    }

    // Assets estáticos: cache-first con fallback a network
    event.respondWith(
        caches.match(event.request).then(cached =>
            cached ?? fetch(event.request).then(response => {
                const clone = response.clone();
                caches.open(CACHE_NAME).then(cache => cache.put(event.request, clone));
                return response;
            })
        )
    );
});

// ── Push Notifications: Alertas de fraude críticas ────────────────────────────
self.addEventListener('push', (event) => {
    const data = event.data?.json() ?? {};

    const options = {
        body: data.message ?? 'Alerta de seguridad ATU',
        icon: '/icons/atu-192.png',
        badge: '/icons/badge-72.png',
        vibrate: [200, 100, 200, 100, 200], // Patrón SOS
        tag: data.eventId ?? 'atu-alert',
        requireInteraction: data.isCritical ?? false,
        data: { url: data.dashboardUrl ?? '/dashboard' },
        actions: [
            { action: 'view', title: '🔍 Ver Dashboard' },
            { action: 'dismiss', title: 'Descartar' }
        ]
    };

    event.waitUntil(
        self.registration.showNotification(
            data.isCritical ? '🚨 ALERTA DE FRAUDE - ATU' : '🔐 ATU Sistema',
            options
        )
    );
});

self.addEventListener('notificationclick', (event) => {
    event.notification.close();

    if (event.action === 'view' || !event.action) {
        event.waitUntil(
            clients.matchAll({ type: 'window' }).then(clientList => {
                const url = event.notification.data?.url ?? '/';
                const existing = clientList.find(c => c.url.includes('/dashboard'));
                return existing ? existing.focus() : clients.openWindow(url);
            })
        );
    }
});
