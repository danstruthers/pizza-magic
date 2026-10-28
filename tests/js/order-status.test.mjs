// node --test "tests/js/*.test.mjs"
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createStatusPoller } from '../../src/PizzaMagic.Web/wwwroot/js/order-status.mjs';

// A manual timer and a scripted status endpoint, so polling is deterministic.
function harness(responses, initialStatus = 'NotStarted') {
  const timers = [];
  const seen = [];
  const failures = [];
  let calls = 0;
  const poller = createStatusPoller({
    initialStatus,
    intervalMs: 5000,
    setTimer: (fn, ms) => { timers.push({ fn, ms }); },
    fetchStatus: async () => {
      const next = responses[calls++];
      if (next instanceof Error) throw next;
      return next;
    },
    onStatus: (status) => seen.push(status.status),
    onError: () => failures.push(calls),
  });
  const tick = async () => {
    const pending = timers.splice(0);
    for (const t of pending) await t.fn();
  };
  return { poller, timers, seen, failures, calls: () => calls, tick };
}

const status = (s) => ({ status: s, statusDisplay: s, allowedActions: s === 'NotStarted' ? ['update', 'cancel'] : [] });

test('polls every 5 s and stops at a final status', async () => {
  const h = harness([status('Started'), status('ReadyForPickup'), status('Delivered'), status('Delivered')]);
  h.poller.start();
  assert.equal(h.timers[0].ms, 5000);
  await h.tick();
  await h.tick();
  await h.tick();
  assert.deepEqual(h.seen, ['Started', 'ReadyForPickup', 'Delivered']);
  assert.equal(h.timers.length, 0);
  assert.equal(h.calls(), 3);
});

test('does not poll an order that is already final', () => {
  const h = harness([], 'Cancelled');
  h.poller.start();
  assert.equal(h.timers.length, 0);
});

test('keeps polling after a failed request', async () => {
  const h = harness([new Error('offline'), status('Started')]);
  h.poller.start();
  await h.tick();
  assert.deepEqual(h.failures, [1]);
  assert.equal(h.timers.length, 1);
  await h.tick();
  assert.deepEqual(h.seen, ['Started']);
});

test('stop() ends polling', async () => {
  const h = harness([status('Started'), status('Started')]);
  h.poller.start();
  h.poller.stop();
  await h.tick();
  assert.equal(h.calls(), 0);
});
