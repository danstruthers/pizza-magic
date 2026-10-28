// node --test tests/js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  createQuoteSequencer,
  reduceQuote,
  formatCents,
  formatBasisPoints,
  formatBasisPointsCompact,
} from '../../src/PizzaMagic.Web/wwwroot/js/quote-sequencer.mjs';

// A manual clock and a controllable "network", so ordering is deterministic.
function harness() {
  const timers = [];
  const sent = [];
  const resolvers = [];
  const applied = [];
  const sequencer = createQuoteSequencer({
    debounceMs: 250,
    setTimer: (fn) => { timers.push(fn); return timers.length; },
    clearTimer: () => { timers.length = 0; },
    send: (draft) => {
      sent.push(draft);
      return new Promise((resolve) => resolvers.push(resolve));
    },
    onResponse: (response, draft) => applied.push({ response, draft }),
  });
  const tick = () => { const pending = timers.splice(0); pending.forEach((fn) => fn()); };
  const settle = () => new Promise((resolve) => setImmediate(resolve));
  return { sequencer, sent, resolvers, applied, tick, settle };
}

test('debounces edits into one request', () => {
  const h = harness();
  h.sequencer.request('a');
  h.sequencer.request('b');
  h.sequencer.request('c');
  assert.deepEqual(h.sent, []);
  h.tick();
  assert.deepEqual(h.sent, ['c']);
});

test('one_request_in_flight_latest_coalesced', async () => {
  const h = harness();
  h.sequencer.request('a');
  h.tick();                       // 'a' in flight
  h.sequencer.request('b');
  h.tick();
  h.sequencer.request('c');
  h.tick();
  assert.deepEqual(h.sent, ['a']);  // never two in flight
  h.resolvers[0]({ ok: true, quote: 'A' });
  await h.settle();
  assert.deepEqual(h.sent, ['a', 'c']);  // 'b' coalesced into the latest draft
});

test('only_newest_response_applied', async () => {
  const h = harness();
  h.sequencer.request('a');
  h.tick();
  h.sequencer.request('b');       // newer edit while 'a' is in flight
  h.resolvers[0]({ ok: true, quote: 'A' });
  await h.settle();
  assert.deepEqual(h.applied, []); // 'a' is stale: never shown
  h.tick();
  h.resolvers[1]({ ok: true, quote: 'B' });
  await h.settle();
  assert.deepEqual(h.applied.map((x) => x.draft), ['b']);
});

const quote = (overrides = {}) => ({
  totalCents: 3148, subtotalCents: 2856, discountCents: 286, taxCents: 167, billCents: 2737,
  tip: { mode: 'Percent', input: '15', ratePpm: 150000, tipCents: 411, effectiveRateBasisPoints: null },
  canSubmit: true, normalizedPromoCodes: ['ILOVEPIZZA'], issues: [], promotions: [],
  description: '2 x Large pizza with Pepperoni, Bacon — $28.56', ...overrides,
});

test('reduces a quote to display text and enables Review only when canSubmit', () => {
  const view = reduceQuote({ status: 200, body: quote() });
  assert.equal(view.totalText, '$31.48');
  assert.equal(view.tipText, '$4.11 (15%)');
  assert.equal(view.reviewEnabled, true);
  assert.deepEqual(view.promoCodes, ['ILOVEPIZZA']);
  assert.equal(view.stale, false);

  const blocked = reduceQuote({ status: 200, body: quote({ canSubmit: false, issues: [{ code: 'ORDER_EMPTY', message: 'Add at least one pizza to your order.' }] }) });
  assert.equal(blocked.reviewEnabled, false);
  assert.deepEqual(blocked.reasons, ['Add at least one pizza to your order.']);
});

test('a dollar tip shows its effective rate with two decimals', () => {
  const view = reduceQuote({ status: 200, body: quote({ tip: { mode: 'Amount', input: '3.00', ratePpm: null, tipCents: 300, effectiveRateBasisPoints: 1096 } }) });
  assert.equal(view.tipText, '$3.00 (10.96%)');
});

test('a not-eligible promo stays as a chip with its reason; Review stays disabled', () => {
  const view = reduceQuote({ status: 200, body: quote({
    canSubmit: false,
    normalizedPromoCodes: ['SAVE5'],
    promotions: [{ code: 'SAVE5', status: 'NotEligible', discountCents: 0, message: 'SAVE5 requires an order subtotal over $20.00 (yours is $20.00).', conflictsWith: [], allocations: [] }],
  }) });
  assert.deepEqual(view.chips, [{ code: 'SAVE5', state: 'warning', message: 'SAVE5 requires an order subtotal over $20.00 (yours is $20.00).' }]);
  assert.equal(view.reviewEnabled, false);
  assert.deepEqual(view.reasons, ['SAVE5 requires an order subtotal over $20.00 (yours is $20.00).']);
});

test('a rejected code is removed and its message shown once', () => {
  const view = reduceQuote({ status: 200, body: quote({
    promotions: [
      { code: 'ILOVEPIZZA', status: 'Applied', discountCents: 286, message: 'ILOVEPIZZA saves $2.86.', conflictsWith: [], allocations: [] },
      { code: 'SAVE5', status: 'Rejected', discountCents: 0, message: "SAVE5 can't be combined with ILOVEPIZZA. Remove ILOVEPIZZA to use SAVE5.", conflictsWith: ['ILOVEPIZZA'], allocations: [] },
    ],
  }) });
  assert.deepEqual(view.chips, [{ code: 'ILOVEPIZZA', state: 'applied', message: 'ILOVEPIZZA saves $2.86.' }]);
  assert.deepEqual(view.notices, ["SAVE5 can't be combined with ILOVEPIZZA. Remove ILOVEPIZZA to use SAVE5."]);
  assert.deepEqual(view.promoCodes, ['ILOVEPIZZA']);
});

test('422_maps_errors_by_path', () => {
  const view = reduceQuote({ status: 422, body: { errors: { 'lineItems[0].quantity': [{ code: 'QUANTITY_OUT_OF_RANGE', message: 'Quantity must be between 1 and 20.' }] } } });
  assert.deepEqual(view.fieldErrors, { 'lineItems[0].quantity': ['Quantity must be between 1 and 20.'] });
  assert.equal(view.reviewEnabled, false);
  assert.equal(view.stale, true);
});

test('an outage keeps the last total greyed out and says so', () => {
  const view = reduceQuote({ status: 503, body: { message: 'Total unavailable — retrying' } });
  assert.equal(view.stale, true);
  assert.equal(view.reviewEnabled, false);
  assert.equal(view.unavailable, 'Total unavailable — retrying');
});

// Pinned by the same cases as the C# MoneyFormatter and PercentFormatter tests.
test('formatCents_matches_csharp_cases', () => {
  assert.equal(formatCents(-290), '-$2.90');
  assert.equal(formatCents(123456), '$1,234.56');
  assert.equal(formatCents(0), '$0.00');
  assert.equal(formatCents(5), '$0.05');
  assert.equal(formatCents(533), '$5.33');
  assert.equal(formatCents(100000000), '$1,000,000.00');
});

test('formatBasisPoints_matches_csharp_cases', () => {
  assert.equal(formatBasisPoints(1096), '10.96%');
  assert.equal(formatBasisPoints(633), '6.33%');
  assert.equal(formatBasisPoints(5), '0.05%');
  assert.equal(formatBasisPoints(0), '0.00%');
  assert.equal(formatBasisPointsCompact(1500), '15%');
  assert.equal(formatBasisPointsCompact(1750), '17.5%');
  assert.equal(formatBasisPointsCompact(1825), '18.25%');
  assert.equal(formatBasisPointsCompact(1000), '10%');
});
