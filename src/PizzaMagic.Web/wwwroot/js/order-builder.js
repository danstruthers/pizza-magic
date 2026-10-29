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
      toppings: (l.toppings ?? []).map((t) => ({ code: t.code, multiplier: t.multiplier ?? 1 })),
      crustCode: l.crustCode ?? null,   // kept from an order being changed; the builder has no crust/sauce picker yet (X2)
      sauceCode: l.sauceCode ?? null,
    })),
    promoCodes: [...(saved.promoCodes ?? [])],
    tip: saved.tip ?? null,
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
    // Only the busy side: when the answer arrives, render() decides whether the total is current or stale.
    onBusyChange: (busy) => {
      if (busy) {
        totalPanel.classList.add('opacity-50');
        statusEl.textContent = 'updating…';
        reviewEl.disabled = true;   // the shown total is out of date until the next quote says otherwise
      }
    },
  });

  function draft() {
    return {
      revision: state.revision,
      lineItems: state.lines.map((l) => ({
        kind: 'Pizza',
        sizeCode: l.sizeCode,
        quantity: l.quantity,
        toppings: l.toppings.map((t) => ({ code: t.code, multiplier: t.multiplier })),
        crustCode: l.crustCode,
        sauceCode: l.sauceCode,
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
      node.querySelector('.line-title').textContent = `Pizza ${index + 1}`;
      const size = node.querySelector('.size');
      size.id = `size-${index}`;
      node.querySelector('.size-label').htmlFor = size.id;
      size.value = line.sizeCode;
      size.addEventListener('change', () => { line.sizeCode = size.value; updateToppingPrices(node, line); changed(); });
      const quantity = node.querySelector('.quantity');
      quantity.id = `quantity-${index}`;
      node.querySelector('.quantity-label').htmlFor = quantity.id;
      quantity.value = line.quantity;
      quantity.addEventListener('change', () => { line.quantity = Number.parseInt(quantity.value, 10) || 0; changed(); });
      node.querySelectorAll('.topping').forEach((box) => {
        // The amount choice (Regular / Extra 2x / Triple 3x), when the topping allows more than one (UQ29).
        const amount = node.querySelector(`.multiplier[data-for="${box.value}"]`);
        const chosen = line.toppings.find((t) => t.code === box.value);
        box.checked = chosen !== undefined;
        if (amount) {
          amount.value = String(chosen?.multiplier ?? 1);
          amount.disabled = !box.checked;
          amount.addEventListener('change', () => {
            const topping = line.toppings.find((t) => t.code === box.value);
            if (topping) { topping.multiplier = Number.parseInt(amount.value, 10); changed(); }
          });
        }
        box.addEventListener('change', () => {
          line.toppings = box.checked
            ? [...line.toppings, { code: box.value, multiplier: amount ? Number.parseInt(amount.value, 10) : 1 }]
            : line.toppings.filter((t) => t.code !== box.value);
          if (amount) amount.disabled = !box.checked;
          changed();
        });
      });
      node.querySelector('.remove-line').addEventListener('click', () => {
        state.lines.splice(index, 1);
        renderLines();
        // Keep keyboard focus nearby: the pizza that took this one's place, else the last one, else "Add a pizza".
        const next = linesEl.children[Math.min(index, state.lines.length - 1)];
        (next?.querySelector('.size') ?? document.getElementById('add-line')).focus();
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
    state.lines.push({ sizeCode: menu.sizes[0].code, quantity: 1, toppings: [], crustCode: null, sauceCode: null });
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

  // Every held code is listed with what it does (or why it doesn't apply yet) and a Remove button; each applied
  // code also gets its own line under Discounts, so the customer can see which discount is which.
  function renderChips(view) {
    const removeCode = (code) => {
      state.promoCodes = state.promoCodes.filter((c) => c.trim().toUpperCase() !== code);
      changed();
    };
    const chips = document.getElementById('promo-chips');
    if (view.chips.length === 0) {
      const none = document.createElement('li');
      none.className = 'small text-muted';
      none.textContent = 'No promo codes applied.';
      chips.replaceChildren(none);
    } else {
      chips.replaceChildren(...view.chips.map((chip) => {
        const item = document.createElement('li');
        item.className = 'd-flex flex-wrap align-items-center gap-2 py-1';
        const badge = document.createElement('span');
        badge.className = `badge ${chip.state === 'applied' ? 'bg-success' : 'bg-warning text-dark'}`;
        badge.textContent = chip.state === 'applied' ? `${chip.code} ✓` : `${chip.code} – not applied`;
        const message = document.createElement('span');
        message.className = 'small';
        message.textContent = chip.message ?? '';
        const remove = document.createElement('button');
        remove.type = 'button';
        remove.className = 'btn btn-sm btn-outline-danger py-0';
        remove.textContent = 'Remove';
        remove.setAttribute('aria-label', `Remove promo code ${chip.code}`);
        remove.addEventListener('click', () => removeCode(chip.code));
        item.append(badge, message, remove);
        return item;
      }));
    }
    document.getElementById('discount-lines').replaceChildren(...view.chips
      .filter((chip) => chip.state === 'applied')
      .map((chip) => {
        const row = document.createElement('div');
        row.className = 'd-flex justify-content-between';
        const code = document.createElement('span');
        code.textContent = chip.code;
        const amount = document.createElement('span');
        amount.textContent = chip.discountText;
        row.append(code, amount);
        return row;
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
    showPressedPreset();
    changed();
  }
  // The preset matching the current tip is shown (and announced) as pressed.
  function showPressedPreset() {
    document.querySelectorAll('.tip-preset').forEach((button) => {
      const pressed = state.tip?.mode === 'Percent' && Number(state.tip.value) === Number(button.dataset.percent);
      button.setAttribute('aria-pressed', String(pressed));
      button.classList.toggle('active', pressed);
    });
  }
  if (state.tip) {
    tipMode.value = state.tip.mode;
    tipValue.value = state.tip.value;
  }
  showPressedPreset();

  // ---- totals ----

  function render(view) {
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
    reviewEl.disabled = !view.reviewEnabled;
  }

  document.getElementById('describe').addEventListener('click', () => descriptionEl.classList.toggle('d-none'));

  renderLines();
  if (state.lines.length > 0) {
    sequencer.request(draft());
  }
}
