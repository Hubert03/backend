self.addEventListener('push', (event) => {
  const data = event.data?.json() ?? {};
  event.waitUntil(
    self.registration.showNotification('Reminder', {
      body: data.title || 'You have a reminder',
      tag: data.reminderId,
      requireInteraction: true,
      data,
      actions: [
        { action: 'snooze', title: 'Snooze' },
        { action: 'stop', title: 'Stop' },
      ],
    }),
  );
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const { reminderId } = event.notification.data || {};
  const action = event.action || 'open';
  const url = `/reminders?triggered=${reminderId}&action=${action}`;
  event.waitUntil(
    clients.matchAll({ type: 'window', includeUncontrolled: true }).then((list) => {
      for (const c of list) {
        if ('focus' in c) { c.navigate(url); return c.focus(); }
      }
      return clients.openWindow(url);
    }),
  );
});