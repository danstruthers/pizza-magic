// The order builder's quote logic, kept pure so it can be tested with `node --test "tests/js/*.test.mjs"` (plan section 6.3).
// The JS never computes money: every amount is server cents, only formatted here.

/**
 * Sends quote requests for the latest draft:
 * - debounced (250 ms by default),
 * - at most one request in flight per tab; edits made meanwhile coalesce into the latest draft,
 * - only the response for the newest draft is applied (an older one is never shown),
 * - an unavailable total (no connection, or a 5xx) is retried with the same draft after retryMs (5 s by default).
 */
export function createQuoteSequencer({
  send, onResponse, debounceMs = 250, retryMs = 5000, setTimer = setTimeout, clearTimer = clearTimeout, onBusyChange = () => {},
}) {
  let latest = 0;          // sequence number of the newest draft requested
  let pending = null;      // the newest draft not yet sent
  let inFlight = false;
  let timer = null;

  function request(draft) {
    latest += 1;
    pending = { draft, seq: latest };
    if (timer !== null) clearTimer(timer);
    timer = setTimer(flush, debounceMs);
    onBusyChange(true);
  }

  async function flush() {
    timer = null;
    if (inFlight || pending === null) return;
    const job = pending;
    pending = null;
    inFlight = true;
    let response;
    try {
      response = await send(job.draft);
    } catch {
      response = { status: 0, body: { message: 'Total unavailable — retrying' } };
    } finally {
      inFlight = false;
    }
    if (job.seq === latest) {
      onBusyChange(false);   // before drawing, so the drawing (e.g. a greyed stale total) has the last word
      onResponse(response, job.draft);
      if (response.status === 0 || response.status >= 500) {
        // Retry the same draft after retryMs, unless a newer edit replaces it first.
        timer = setTimer(() => request(job.draft), retryMs);
      }
    } else if (pending !== null) {
      flush();   // a newer draft arrived while this one was in flight: send it now
    }
  }

  return { request, flush };
}

/** Turns a quote proxy result ({ status, body }) into everything the page shows. */
export function reduceQuote({ status, body }) {
  const view = {
    stale: true,
    reviewEnabled: false,
    fieldErrors: {},
    reasons: [],
    chips: [],
    notices: [],
    promoCodes: null,
    unavailable: null,
    totalText: null,
    subtotalText: null,
    discountText: null,
    taxText: null,
    tipText: null,
    description: null,
  };

  if (status === 422) {
    for (const [path, errors] of Object.entries(body?.errors ?? {})) {
      view.fieldErrors[path] = errors.map((e) => e.message);
    }
    return view;
  }
  if (status !== 200) {
    view.unavailable = body?.message ?? 'Total unavailable — retrying';
    return view;
  }

  view.stale = false;
  view.totalText = formatCents(body.totalCents);
  view.subtotalText = formatCents(body.subtotalCents);
  view.discountText = formatCents(-body.discountCents);
  view.taxText = formatCents(body.taxCents);
  view.tipText = tipText(body.tip);
  view.description = body.description;
  view.promoCodes = body.normalizedPromoCodes;

  for (const promo of body.promotions ?? []) {
    if (promo.status === 'Applied') {
      view.chips.push({ code: promo.code, state: 'applied', message: promo.message, discountText: formatCents(-promo.discountCents) });
    } else if (promo.status === 'NotEligible') {
      view.chips.push({ code: promo.code, state: 'warning', message: promo.message, discountText: null });
    } else if (promo.status !== 'Duplicate' && promo.message) {
      view.notices.push(promo.message);   // removed from the cart: Rejected, InvalidFormat, Unknown, ...
    }
  }

  view.reviewEnabled = body.canSubmit === true;
  if (!view.reviewEnabled) {
    view.reasons = [
      ...(body.issues ?? []).map((i) => i.message),
      ...view.chips.filter((c) => c.state === 'warning').map((c) => c.message),
    ];
  }
  return view;
}

function tipText(tip) {
  const amount = formatCents(tip?.tipCents ?? 0);
  if (tip?.mode === 'Percent' && tip.ratePpm != null) {
    return `${amount} (${formatBasisPointsCompact(Math.round(tip.ratePpm / 100))})`;
  }
  if (tip?.mode === 'Amount' && tip.effectiveRateBasisPoints != null) {
    return `${amount} (${formatBasisPoints(tip.effectiveRateBasisPoints)})`;
  }
  return amount;
}

/** Mirrors Contracts.MoneyFormatter: "$1,234.56", "-$2.90". */
export function formatCents(cents) {
  const negative = cents < 0;
  const magnitude = Math.abs(cents);
  const dollars = Math.floor(magnitude / 100).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  const remainder = String(magnitude % 100).padStart(2, '0');
  return `${negative ? '-' : ''}$${dollars}.${remainder}`;
}

/** Mirrors Contracts.PercentFormatter.FormatBasisPoints: 1096 -> "10.96%". */
export function formatBasisPoints(basisPoints) {
  const magnitude = Math.abs(basisPoints);
  return `${basisPoints < 0 ? '-' : ''}${Math.floor(magnitude / 100)}.${String(magnitude % 100).padStart(2, '0')}%`;
}

/** Mirrors Contracts.PercentFormatter.FormatBasisPointsCompact: 1500 -> "15%", 1750 -> "17.5%". */
export function formatBasisPointsCompact(basisPoints) {
  const magnitude = Math.abs(basisPoints);
  const fraction = String(magnitude % 100).padStart(2, '0').replace(/0+$/, '');
  return `${basisPoints < 0 ? '-' : ''}${Math.floor(magnitude / 100)}${fraction ? '.' + fraction : ''}%`;
}
