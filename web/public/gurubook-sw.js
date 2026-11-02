/*
 * GuruBook's service worker: Angular's own (offline pages, updates) plus phone notifications.
 * The server sends only what happened; push-text.js writes it in the person's language, using the same translation files as the app.
 */
importScripts('./ngsw-worker.js', './push-text.js');

const LANGUAGES = ['en', 'si', 'ta'];

async function translations(language) {
  try {
    const response = await fetch(`i18n/${language}.json`);
    return response.ok ? await response.json() : null;
  } catch {
    return null;
  }
}

async function show(data) {
  const language = LANGUAGES.includes(data.lang) ? data.lang : 'en';
  // The person's language, or English if that file cannot be reached.
  let messages = await translations(language);
  let used = language;
  if (!messages && language !== 'en') {
    messages = await translations('en');
    used = 'en';
  }

  const words = messages ? self.GuruBookPushText.compose(data, messages, used) : { title: 'GuruBook', body: '' };
  await self.registration.showNotification(words.title, {
    body: words.body,
    icon: 'icons/icon-192x192.png',
    badge: 'icons/icon-96x96.png',
    // The same notification shown twice replaces itself instead of piling up.
    tag: data.id,
    data: { url: typeof data.link === 'string' && data.link.startsWith('/') && !data.link.startsWith('//') ? data.link : '/' },
  });
}

self.addEventListener('push', (event) => {
  let data = null;
  try {
    data = event.data ? event.data.json() : null;
  } catch {
    return;
  }
  // A message with a ready-made "notification" is shown by Angular's own handler; ours carry a kind.
  if (!data || data.notification || typeof data.kind !== 'string') return;
  event.waitUntil(show(data));
});

async function open(path) {
  const target = new URL(path.replace(/^\//, ''), self.registration.scope).href;
  const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
  for (const window of windows) {
    await window.focus();
    if ('navigate' in window) {
      try {
        await window.navigate(target);
      } catch {
        // A window that cannot be moved stays where it is.
      }
    }
    return;
  }
  await self.clients.openWindow(target);
}

self.addEventListener('notificationclick', (event) => {
  const url = event.notification.data && event.notification.data.url;
  // Ones Angular showed carry their own action and are handled by it.
  if (typeof url !== 'string') return;
  event.notification.close();
  event.waitUntil(open(url));
});
