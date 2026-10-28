// The order details page's DOM wiring: poll the status and keep the panel and its buttons in step with it.
// The polling logic lives in order-status.mjs.
import { createStatusPoller } from './order-status.mjs';

const panel = document.getElementById('order-status');
if (panel) {
  const display = panel.querySelector('[data-field="statusDisplay"]');
  const since = panel.querySelector('[data-field="statusChangedAt"]');

  const formatTime = (local, utc) => {
    const text = local?.localTime ?? utc;
    const match = /T(\d{2}):(\d{2})/.exec(text);
    if (!match) return text;
    const hours = Number(match[1]);
    const clock = `${hours % 12 === 0 ? 12 : hours % 12}:${match[2]} ${hours < 12 ? 'AM' : 'PM'}`;
    return `${clock} ${local ? local.abbreviation : 'UTC'}`;
  };

  createStatusPoller({
    initialStatus: panel.dataset.status,
    fetchStatus: async () => {
      const response = await fetch(panel.dataset.statusUrl, { headers: { Accept: 'application/json' } });
      if (!response.ok) throw new Error(`status ${response.status}`);
      return response.json();
    },
    onStatus: (status) => {
      panel.classList.remove('opacity-50');
      if (status.status === panel.dataset.status) return;
      panel.dataset.status = status.status;
      display.textContent = status.statusDisplay;
      since.textContent = formatTime(status.statusChangedAtLocal, status.statusChangedAt);
      // Buttons follow the server's allowed actions: once the kitchen starts, Change and Cancel disappear.
      panel.querySelectorAll('form[data-action]').forEach((form) => {
        if (!status.allowedActions.includes(form.dataset.action)) form.remove();
      });
    },
    onError: () => panel.classList.add('opacity-50'),
  }).start();
}
