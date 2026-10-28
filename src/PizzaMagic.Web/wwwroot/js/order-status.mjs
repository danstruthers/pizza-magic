// Order status polling (plan section 6.1): every 5 s until the order reaches a final status. A failed request is
// reported and polling continues; the server's status is shown as given, never inferred.

export const FINAL_STATUSES = ['Delivered', 'Cancelled'];

export function createStatusPoller({
  initialStatus,
  fetchStatus,
  onStatus,
  onError = () => {},
  intervalMs = 5000,
  setTimer = (fn, ms) => setTimeout(fn, ms),
}) {
  let stopped = false;

  const schedule = () => {
    if (!stopped) setTimer(poll, intervalMs);
  };

  async function poll() {
    if (stopped) return;
    try {
      const status = await fetchStatus();
      if (stopped) return;
      onStatus(status);
      if (FINAL_STATUSES.includes(status.status)) {
        stopped = true;
        return;
      }
    } catch (error) {
      onError(error);
    }
    schedule();
  }

  return {
    start() {
      if (FINAL_STATUSES.includes(initialStatus)) {
        stopped = true;
        return;
      }
      schedule();
    },
    stop() {
      stopped = true;
    },
  };
}
