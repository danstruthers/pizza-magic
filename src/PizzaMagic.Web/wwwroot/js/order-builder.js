// The order builder's DOM wiring. All logic worth testing lives in quote-sequencer.mjs; this file only reads
// the form, posts drafts and renders what the server returns. It never computes money.
import { createQuoteSequencer, reduceQuote, formatCents } from './quote-sequencer.mjs';

const root = document.getElementById('builder');
if (root) {
  const menu = JSON.parse(root.dataset.menu);
  const saved = JSON.parse(root.dataset.cart);
  const token = root.querySelector('input[name="__RequestVerificationToken"]').value;
  const template = document.getElementById('line-template');
  const linesEl = document.getElementById('lines');
  const totalPanel = document.getElementById('total-panel');
  const statusEl = document.getElementById('total-status');
  const reviewEl = document.getElementById('review');
  const descriptionEl = document.getElementById('description');

  const state = {
    revision: saved.revision ?? 0,
    lines: (saved.lineItems ?? []).map((l) => ({
      sizeCode: l.sizeCode,
      quantity: l.quantity,
      toppings: (l.toppings ?? []).map((t) => t.code),
    })),
    promoCodes: [...(saved.promoCodes ?? [])],
    tip: saved.tip ?? null,
    lastView: null,
  };

  const sequencer = createQuoteSequencer({
    send: async (draft) => {
      const response = await fetch('/Builder/Quote', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
        body: JSON.stringify(draft),
      });
      let body = null;
      try { body = await response.json(); } catch { body = null; }
      return { status: response.status, body };
    },
    onResponse: (response) => render(reduceQuote(response)),
    onBusyChange: (busy) => {
      totalPanel.classList.toggle('opacity-50', busy);
      if (busy) statusEl.textContent = 'updating…';
    },
  });

  function draft() {
    return {
      revision: state.revision,
      lineItems: state.lines.map((l) => ({
        kind: 'Pizza',
        sizeCode: l.sizeCode,
        quantity: l.quantity,
        toppings: l.toppings.map((code) => ({ code })),
      })),
      promoCodes: state.promoCodes,
      tip: state.tip,
    };
  }

  function changed() {
    state.revision += 1;
    sequencer.request(draft());
  }

  // ---- lines ----

  function renderLines() {
    linesEl.replaceChildren();
    state.lines.forEach((line, index) => {
      const node = template.content.firstElementChild.cloneNode(true);
      node.dataset.index = index;
      const size = node.querySelector('.size');
      size.value = line.sizeCode;
      size.addEventListener('change', () => { line.sizeCode = size.value; updateToppingPrices(node, line); changed(); });
      const quantity = node.querySelector('.quantity');
      quantity.value = line.quantity;
      quantity.addEventListener('change', () => { line.quantity = Number.parseInt(quantity.value, 10) || 0; changed(); });
      node.querySelectorAll('.topping').forEach((box) => {
        box.checked = line.toppings.includes(box.value);
        box.addEventListener('change', () => {
          line.toppings = box.checked ? [...line.toppings, box.value] : line.toppings.filter((c) => c !== box.value);
          changed();
        });
      });
      node.querySelector('.remove-line').addEventListener('click', () => {
        state.lines.splice(index, 1);
        renderLines();
        changed();
      });
      updateToppingPrices(node, line);
      linesEl.append(node);
    });
  }

  // Display only: the server's price grid for the selected size.
  function updateToppingPrices(node, line) {
    node.querySelectorAll('.topping').forEach((box) => {
      const prices = JSON.parse(box.dataset.prices);
      box.closest('label').querySelector('.topping-price').textContent =
        prices[line.sizeCode] != null ? `+${formatCents(prices[line.sizeCode])}` : '';
    });
  }

  document.getElementById('add-line').addEventListener('click', () => {
    state.lines.push({ sizeCode: menu.sizes[0].code, quantity: 1, toppings: [] });
    renderLines();
    changed();
  });

  // ---- promos ----

  const promoInput = document.getElementById('promo-input');
  function applyPromo() {
    const code = promoInput.value.trim();
    if (!code) return;
    state.promoCodes = [...state.promoCodes, code];
    promoInput.value = '';
    changed();
  }
  document.getElementById('promo-apply').addEventListener('click', applyPromo);
  promoInput.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); applyPromo(); } });

  function renderChips(view) {
    const chips = document.getElementById('promo-chips');
    chips.replaceChildren(...view.chips.map((chip) => {
      const el = document.createElement('span');
      el.className = `badge rounded-pill ${chip.state === 'applied' ? 'text-bg-success' : 'text-bg-warning'}`;
      el.title = chip.message ?? '';
      el.textContent = `${chip.code} `;
      const remove = document.createElement('button');
      remove.type = 'button';
      remove.className = 'btn-close btn-close-white ms-1';
      remove.style.fontSize = '0.6rem';
      remove.setAttribute('aria-label', `Remove ${chip.code}`);
      remove.addEventListener('click', () => {
        state.promoCodes = state.promoCodes.filter((c) => c.toUpperCase() !== chip.code);
        changed();
      });
      el.append(remove);
      return el;
    }));
    document.getElementById('promo-notices').replaceChildren(...view.notices.map((text) => {
      const div = document.createElement('div');
      div.textContent = text;
      return div;
    }));
  }

  // ---- tip ----

  const tipMode = document.getElementById('tip-mode');
  const tipValue = document.getElementById('tip-value');
  document.querySelectorAll('.tip-preset').forEach((button) => button.addEventListener('click', () => {
    tipMode.value = 'Percent';
    tipValue.value = button.dataset.percent;
    setTip();
  }));
  document.querySelector('.tip-none').addEventListener('click', () => { tipValue.value = ''; setTip(); });
  tipMode.addEventListener('change', setTip);
  tipValue.addEventListener('input', setTip);
  function setTip() {
    const value = tipValue.value.trim();
    state.tip = value === '' ? null : { mode: tipMode.value, value };
    changed();
  }
  if (state.tip) {
    tipMode.value = state.tip.mode;
    tipValue.value = state.tip.value;
  }

  // ---- totals ----

  function render(view) {
    state.lastView = view;
    if (view.promoCodes) state.promoCodes = view.promoCodes;   // the server's normalized list replaces the cart's
    totalPanel.classList.toggle('opacity-50', view.stale);

    if (view.unavailable) {
      statusEl.textContent = view.unavailable;
    } else if (Object.keys(view.fieldErrors).length > 0) {
      statusEl.textContent = 'Fix the highlighted fields to see your total.';
    } else {
      statusEl.textContent = '';
    }

    if (!view.stale) {
      const set = (name, text) => { root.querySelector(`[data-total="${name}"]`).textContent = text; };
      set('subtotal', view.subtotalText);
      set('discount', view.discountText);
      set('tax', view.taxText);
      set('tip', view.tipText);
      set('total', view.totalText);
      descriptionEl.textContent = view.description;
      renderChips(view);
    }

    // Field errors by JSON path: lineItems[i].* go under that line, tip.value under the tip.
    root.querySelectorAll('.line-errors, [data-error-for]').forEach((el) => { el.textContent = ''; });
    for (const [path, messages] of Object.entries(view.fieldErrors)) {
      const line = /^lineItems\[(\d+)\]/.exec(path);
      const target = line
        ? linesEl.children[Number(line[1])]?.querySelector('.line-errors')
        : root.querySelector(`[data-error-for="${path}"]`) ?? statusEl;
      if (target) target.textContent += messages.join(' ') + ' ';
    }

    document.getElementById('reasons').textContent = view.reasons.join(' ');
    reviewEl.classList.toggle('disabled', !view.reviewEnabled);
    reviewEl.setAttribute('aria-disabled', String(!view.reviewEnabled));
    if (view.reviewEnabled) reviewEl.removeAttribute('disabled'); else reviewEl.setAttribute('disabled', '');
  }

  document.getElementById('describe').addEventListener('click', () => descriptionEl.classList.toggle('d-none'));

  renderLines();
  if (state.lines.length > 0) {
    sequencer.request(draft());
  }
}
