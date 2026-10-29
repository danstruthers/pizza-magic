# Pizza Magic!

A pizza order builder with a separate order service. A customer builds an order, watches the total update as they change it, and sends it to the restaurant's order service. They can then check on the order and, until the kitchen starts it, change or cancel it.

```
 Browser ──HTML / fetch──▶ Order builder (MVC, :5000) ──HTTP + retries──▶ Order service (Web API, :5100) ──▶ SQLite
           (renders only)   cart in Session, review ticket                 ALL pricing, validation,
                            NO pricing, NO database                        orders, idempotency, status
```

| Piece | Project | What it is |
|---|---|---|
| Order builder | `src/PizzaMagic.Web` | ASP.NET Core 8 MVC app for customers (no login), plus a simulated kitchen page |
| Order service | `src/PizzaMagic.OrderManager` | ASP.NET Core 8 Web API; the only process that touches the database (SQLite) |
| Pricing core | `src/PizzaMagic.Domain` | Pure C# library used only by the order service: menu, pricing, promotions, tax, local time |
| Wire contract | `src/PizzaMagic.Contracts` | Shared request/response types and the `$` / `%` formatters |

Sections: [Run it](#run-it) · [Using it](#using-it) · [Order service API](#order-service-api) · [Assumptions](#assumptions) · [Design write-up](#design-write-up) · [Tests and invariants](#tests-and-invariants) · [AI usage note](#ai-usage-note)

The full design and every decision are in [docs/DEVELOPMENT_PLAN.md](docs/DEVELOPMENT_PLAN.md); this README is self-contained.

## Run it

Prerequisites: [.NET SDK 8.0.427](https://dotnet.microsoft.com/download/dotnet/8.0) or a later 8.0 feature band (pinned in `global.json`). Optional: Node.js 18+ for the browser-script tests. SQLite runs inside the order service; nothing else to install.

From the repository root, each app in its own terminal:

```bash
dotnet run --project src/PizzaMagic.OrderManager    # order service: http://localhost:5100/swagger
dotnet run --project src/PizzaMagic.Web             # order builder: http://localhost:5000
```

On first start the order service creates `pizzamagic.db` in its project folder, applies the seed scripts in `data/seed` (menu, Washington tax, ILOVEPIZZA / SAVE5, time zones) and validates the catalog; it refuses to start on invalid data. To reset, stop it and delete `src/PizzaMagic.OrderManager/pizzamagic.db*`.

Tests:

```bash
dotnet test                          # pricing core, order service, order builder, end-to-end and architecture tests
node --test "tests/js/*.test.mjs"    # the two browser modules with logic (keep the quotes)
```

A second, invented dataset for testing (six restaurants in made-up time zones, tax from 0% to 75%, a MYTHIC topping tier) lives in its own file: `dotnet run --project src/PizzaMagic.OrderManager -- --database fantasy` uses `pizzamagic-fantasy.db` beside the normal one, and the two never mix. Shop at one of its restaurants with `dotnet run --project src/PizzaMagic.Web -- --OrderManager:RestaurantId=1003`.

## Using it

1. Open http://localhost:5000. Add pizzas (size, quantity, toppings, each Regular, Extra 2x or Triple 3x), promo codes and a tip (preset, custom % or dollars). The total updates as you go; each promo code shows what it saves (or why it doesn't apply yet) with a Remove button. **Review order** stays disabled until the current total allows the order to be placed.
2. **Review order** prices the order again and shows exactly what will be charged; **Place order** sends it and opens the order's page. **Your orders** lists your 10 most recent orders from the last 7 days with status, time placed, contents and total.
3. While the order is Not Started, **Change order** (back through the builder and Review) and **Cancel order** are available. The order page polls the status every 5 seconds.
4. **Kitchen (staff)**, at http://localhost:5000/Kitchen, simulates the restaurant: it lists active orders oldest first, each with a button for its next step (Start → Mark ready for pickup → Mark delivered), and refreshes every 15 seconds. The same step is `POST /api/v1/kitchen/orders/{id}/advance` with `{"expectedStatus":"NotStarted"}` (Swagger, or `src/PizzaMagic.OrderManager/PizzaMagic.OrderManager.http`).

## Order service API

JSON under `/api/v1`, money in integer cents. Every error is an RFC 9457 problem document with a stable `type` (e.g. `.../problems/price-changed`); validation errors are keyed by JSON path (`lineItems[0].quantity`). Unknown JSON properties are refused (400); request bodies are capped at 1 MB.

| Endpoint | Purpose | Notes |
|---|---|---|
| `GET /menu` | Sizes, toppings by tier with their price per size and maximum amount (1x–3x), tip presets and limits | Promo codes are not listed |
| `POST /quotes` | Price a draft: every intermediate value, a description, the normalized draft the server will accept, `canSubmit` | No writes. Promo problems never fail a quote (each code gets a status); invalid fields → 422 |
| `POST /orders` | Place an order → 201, `ETag: "1"`, `Location` | **`Idempotency-Key` required.** `expectedTotalCents` = the total the customer confirmed; if prices changed → 409 with the current quote |
| `GET /orders/{id}` · `/status` · `/description` | The stored order (never re-priced), the light polling payload, or the written description | |
| `PUT /orders/{id}` | Replace the order while Not Started | **`If-Match: "<version>"` required** (412 if stale); optional `Idempotency-Key`; 409 once started |
| `POST /orders/{id}/cancel` | Cancel while Not Started | Optional `If-Match`; 409 once started; repeating it returns the cancelled order |
| `GET /kitchen/orders` | Active orders, oldest first, with items and next status | Optional `restaurantId` |
| `POST /kitchen/orders/{id}/advance` | One step forward | `expectedStatus` required; a repeat returns `alreadyApplied` |

```bash
# Price P9: 2 Large with pepperoni and bacon, ILOVEPIZZA, 15% tip -> totalCents 3148 ($31.48)
curl -X POST http://localhost:5100/api/v1/quotes -H 'Content-Type: application/json' \
  -d '{"lineItems":[{"kind":"Pizza","sizeCode":"LARGE","quantity":2,"toppings":[{"code":"PEPPERONI"},{"code":"BACON"}]}],"promoCodes":["ILOVEPIZZA"],"tip":{"mode":"Percent","value":"15"}}'

# Place it; sending it again with the same key returns the same order (Idempotent-Replayed: true)
curl -i -X POST http://localhost:5100/api/v1/orders -H 'Content-Type: application/json' -H 'Idempotency-Key: readme-order-1' \
  -d '{"lineItems":[{"kind":"Pizza","sizeCode":"LARGE","quantity":2,"toppings":[{"code":"PEPPERONI"},{"code":"BACON"}]}],"promoCodes":["ILOVEPIZZA"],"tip":{"mode":"Percent","value":"15"},"expectedTotalCents":3148}'

# Change it to one Medium pepperoni ($8.05) while Not Started, then cancel
curl -i -X PUT http://localhost:5100/api/v1/orders/<orderId> -H 'Content-Type: application/json' -H 'If-Match: "1"' -H 'Idempotency-Key: readme-update-1' \
  -d '{"lineItems":[{"kind":"Pizza","sizeCode":"MEDIUM","quantity":1,"toppings":[{"code":"PEPPERONI"}]}],"promoCodes":[],"tip":null,"expectedTotalCents":805}'
curl -X POST http://localhost:5100/api/v1/orders/<orderId>/cancel
```

On Windows PowerShell use `curl.exe` with the same arguments, or the `.http` file or Swagger.

## Assumptions

Where the rules were open, and what I decided (all of them, with reasons, are in plan section 3).

**Prices.** Money is integer cents, rounded half away from zero ($0.325 → $0.33, never banker's rounding), and every amount is rounded **before** it is multiplied. "8% / 11% of the base pizza price" is of the size price only, per pizza (Medium + bacon: 11% of $7.00 = $0.77), then × quantity. Extra and triple toppings are 2× or 3× that rounded price (Large pepperoni 2x = 2 × $0.96). A pizza with no toppings is valid; quantity is 1–20 per line, 1–20 lines; each topping is listed once per pizza (more of it means choosing 2x or 3x).

**Tax.** 6.5% of the merchandise total **after discounts**, once per order, never on the tip. 6.5% is Washington's state rate, so the restaurant is modeled in Washington (Pacific time), and tax is rounded as Washington law says: half-up on the combined tax, per invoice (RCW 82.08.054). The rounding method is data per jurisdiction. Confirm with a tax professional before real use.

**Promo codes.** Trimmed and case-insensitive; repeats collapse; at most 3 distinct codes (10 entries). ILOVEPIZZA is 10% of the pre-tax subtotal, rounded once. SAVE5's "over $20" means **strictly more than $20.00** before discounts, tax and tip. **SAVE5 + ILOVEPIZZA: the code entered first holds; the second is rejected** with a message naming the code to remove. A held code that isn't currently eligible still blocks the other, and the order can't be placed until one is removed or the cart qualifies. On the running total a bad code never breaks the total; on submit any code that wouldn't apply is a 422, so an order is never stored with a code the customer thinks applied.

**Tip.** A percentage is of the bill: subtotal − discounts + tax. A dollar tip stays fixed and shows its effective rate, e.g. "$3.00 (10.96%)". Up to two decimals, 0 allowed, at most $500 or 100%; never taxed or discounted.

**Orders.** The order id is a random GUID that acts as the customer's key to it (no accounts). "Cancelled" is an added final status: only the customer cancels, only while Not Started; the kitchen moves one step at a time. "Delivered" means handed over (the spec rules out delivery). What is submitted is exactly what Review showed; if prices changed in between, nothing is created and Review shows the new total. A placed order keeps a snapshot of its prices, names and tax, so later menu, promo or tax changes never alter it; changing it while Not Started re-prices at current prices, confirmed on Review.

**Scope.** One restaurant in the normal database (the schema supports several, with per-store overrides). USD. Half and quarter toppings, crust and sauce upgrades and add-ons are designed but not built; the API refuses them with a clear message. The cart lives in an in-memory session (2 hours idle): one server, lost on restart.

## Design write-up

**Money and where pricing runs.** Money is `long` cents and rates are integer parts per million (6.5% = 65 000); there is no floating point in pricing. Amounts are rounded at four named points (topping per pizza, order promo, tax, percent tip) by one function, `RoundHalfAwayFromZero`; only a tax jurisdiction configured to truncate uses `Truncate`. Rounding happens before multiplying (82.5¢ → 83¢ × 10 = $8.30, not $8.25), and discounts and tax are split across lines by largest remainder, so the parts always add up. **Pricing exists once, in `PizzaMagic.Domain`, and runs only in the order service**; architecture tests stop the web app referencing it. The builder's running total is a `POST /quotes` (debounced, one request in flight, newest wins, retried after an outage), and the browser only formats the server's numbers. Review seals the server's normalized order and total into a tamper-evident ticket; Place submits exactly that ticket with `expectedTotalCents`, and the service refuses with the new total if prices moved. So the total seen is the total charged by construction, not by keeping two implementations in step.

**Adding a size, topping category or promo.** The menu is rows: a size is a `product` row, a topping category is a `topping_tier` row with a pricing rule, and stores override price or availability with sparse rows. A promo is a `promotion` row (an effect kind with JSON parameters) plus `promotion_condition`, `promotion_code` and conflict rows (`promotion_incompatibility` pairs, an exclusion group, or "exclusive"). The engine looks kinds up by name and never mentions a product, tier or code. For example, this script in `data/seed/` adds a Party size and a "$10 off $50 or more" code that can't be combined with either existing code; after a restart, pepperoni on a Party is $1.44 and SAVE5 after BIGTEN gets "SAVE5 can't be combined with BIGTEN…":

```sql
INSERT INTO product (id, product_type_code, code, display_name, price_cents, tax_class_id, sort_order) VALUES (4,'PIZZA_SIZE','PARTY','Party',1800,1,4);
INSERT INTO promotion (id, name, customer_description, effect_type, effect_params, priority, valid_from_local, valid_to_local)
  VALUES (3,'Big Ten','$10 off orders of $50 or more','fixed_off_order','{"amountCents":1000}',100,NULL,NULL);
INSERT INTO promotion_code (code, promotion_id) VALUES ('BIGTEN',3);
INSERT INTO promotion_condition (id, promotion_id, condition_type, params) VALUES (2,3,'min_subtotal','{"amountCents":5000,"inclusive":true}');
INSERT INTO promotion_incompatibility (promotion_id_low, promotion_id_high) VALUES (1,3), (2,3);
```

The fantasy test database proves the same thing automatically: a new size, a new topping tier, new zones and promo codes with exclusion groups load as data and price correctly, with no code change. A genuinely new kind of rule is one new class registered by name: "Tuesdays only" is an `IConditionEvaluator` that reads the restaurant's local day from the time and zone it is given. A rule that needs to see the pizzas themselves ("2+ pizzas") would also need the lines passed to conditions, a small change to the engine. Startup refuses data the code can't honor rather than ignoring it.

**The order lifecycle.** Not Started → Started → Ready for Pickup → Delivered, plus Cancelled from Not Started; the allowed moves per actor are one table.
- *Retry after a timeout.* Placing an order needs an `Idempotency-Key`; the builder keeps the same key until the cart changes, and its HTTP client retries with the same key and identical bytes. The service stores the response with the key in the same transaction as the order, re-checking the key inside the write lock, so a retry of a committed request gets the original response. The builder saves a "waiting for confirmation" lock before it calls the service, so if every attempt fails the customer sees "We couldn't confirm your order" and **Try again** re-sends the same ticket and key; no other order can be placed meanwhile. **Guarantee:** for 24 hours, one key gives at most one order and one answer. Refusals (a price change, a validation error) are not stored, so the same key can succeed once the problem is fixed.
- *Cancel as the kitchen starts.* Each is one conditional `UPDATE`: cancel `WHERE id = @id AND status = 'NotStarted'`, kitchen `WHERE status = @expected`. SQLite serializes writers, so exactly one changes the row; the other updates nothing, re-reads, and says why (409 "the kitchen has started this order", with the time). Edits add the version (`If-Match`). **Guarantee:** exactly one wins, and the loser is told accurately.

**Cut, faked, and next.** I designed more than I built. Built: the spec's whole path, plus whole-pizza extra toppings, a kitchen page and the fantasy test data. Designed but not built: half and quarter toppings, crust and sauce upgrades and add-ons, item-level promos and BOGO, multi-level tax with tax classes, a timer-driven kitchen, Docker Compose. Faked: the kitchen has no login, the cart is an in-memory session, and the browser code is checked by a scripted headless-browser session rather than the test suite. Next: half and quarter toppings (their test vectors are written), upgrades and add-ons, item promos, multi-level tax, then Compose. **Timebox:** this went well past 3 hours. The work was not done in one sitting: the commit timestamps span two days with long breaks away from it, so the actual working time is much shorter than they suggest. In a strict 3 hours I would keep the money model, quote-as-the-total, idempotent submit and the conditional status updates, and drop the full schema, store overrides, time zones and the extras.

## Tests and invariants

I tested what a customer or the restaurant would lose money or trust over, and what AI-written code tends to get subtly wrong:

| Invariant | Tests |
|---|---|
| The total seen is the total charged: running total = Review = submitted = stored | `Quote_total_equals_submitted_total_equals_stored_total`, the Checkout tests, end-to-end `Flow_build_submit_status_edit_cancel` and `Web_quote_equals_direct_api_quote` |
| The spec's rules, to the cent | Hand-computed examples P1–P26 (mostly `Golden_vectors_price_to_the_cent`); 500 generated orders checked against an independent calculation written straight from the spec's table (`Matches_an_independent_calculation_of_the_spec_rules`), plus parts-sum-to-total properties |
| One place for pricing and rounding | Architecture scans: no floating point, `Math.Round` or bare price numbers in the engine; truncation only in `TaxCalculator`; the web app can't reference the pricing core or the database |
| One key → at most one order, one answer | 50 parallel submits with one key; 10 parallel copies of one edit; replay; key reuse with another body; expiry; end to end, a lost response then Try again, and a double tap on Place |
| Cancel and edit only while Not Started, even in a race | All 25 status pairs per actor; cancel vs kitchen start, edit vs kitchen start, two edits from one version, cancel vs edit (50 runs each); a naive read-then-write version shown to double-succeed |
| A placed order never changes when the menu, promos or tax change | `Historical_order_unchanged_after_price_rate_label_and_promo_change` |
| Bad input is a clear 4xx, never a 500 | Null lines and toppings, oversized lists and tips, unknown routes, unexpected exceptions (`MalformedRequestTests`) |
| Instants stored as UTC; promo windows in the restaurant's local time across DST | `Time` tests, DST promo-window vectors, `Every_time_column_is_utc_or_local_text` |

The important tests were checked by breaking the code on purpose (banker's rounding, swapped tax methods, a cancel without its status condition, a missing idempotency guard, ILOVEPIZZA at 9%, Place ignoring the lock) and confirming the right tests fail.

**Not covered:** no browser automation in the suite (the builder was exercised in headless Edge by a script kept outside it); nothing across multiple processes or under load; accessibility only basic (labels, live totals, focus). Designed-but-unbuilt features have test vectors in the plan but no tests.

Test projects: `tests/PizzaMagic.Domain.Tests`, `tests/PizzaMagic.OrderManager.Tests` (against real temp-file SQLite), `tests/PizzaMagic.Web.Tests` (against a scripted order service), `tests/PizzaMagic.EndToEnd.Tests` (both real apps in-process, plus the architecture rules) and `tests/js`.

## AI usage note

I used Claude Code (Claude Opus 5.5) to draft and critique the design, research Washington's tax-rounding law, and write the tests and code test-first, keeping [docs/ai-log.md](docs/ai-log.md) as a running record of its mistakes and of what was checked by hand. **Rejected output:** its rule for promo validity windows compared local wall-clock times, which would have re-opened an expired promo during the repeated hour on a daylight-saving fall-back night; it became "resolve each local boundary to its earliest UTC instant", pinned by tests at 09:00Z and 09:15Z on that night. **Verified myself:** using the app, I found that order statuses and promo codes were invisible, even though every test passed. The agent's markup used Bootstrap 5.2 badge classes, but the app ships Bootstrap 5.1. That led to a fix and a test requiring every colour class to exist in the shipped stylesheet. A code review I commissioned then found a retried edit could get "changed elsewhere" for its own change; it was reproduced by a race test before being fixed.
