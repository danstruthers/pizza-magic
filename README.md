# Pizza Magic!

A pizza order builder with a separate order service. A customer builds an order, watches the total update as they change it, and sends it to the restaurant's order service. They can then check on the order and, until the kitchen starts it, change or cancel it.

```
 Browser ──HTML / fetch──▶ Order builder (MVC, :5000) ──HTTP + retries──▶ Order service (Web API, :5100) ──▶ SQLite
           (renders only)   cart in Session, review ticket                 ALL pricing, validation,
                            NO pricing, NO database                        orders, idempotency, status
```

| Piece | Project | What it is | URL when running |
|---|---|---|---|
| Order builder | `src/PizzaMagic.Web` | ASP.NET Core 8 MVC app for customers (no login) | http://localhost:5000 |
| Order service | `src/PizzaMagic.OrderManager` | ASP.NET Core 8 Web API; the only process that touches the database (SQLite) | http://localhost:5100 (Swagger at `/swagger`) |
| Pricing core | `src/PizzaMagic.Domain` | Pure C# library used only by the order service: menu, pricing, promotions, tax, local time | (library) |
| Wire contract | `src/PizzaMagic.Contracts` | Shared request/response types and the `$` / `%` formatters | (library) |

Sections: [Run it](#run-it) · [Using it](#using-it) · [Order service API](#order-service-api) · [Assumptions](#assumptions) · [Design write-up](#design-write-up) · [Tests and invariants](#tests-and-invariants) · [AI usage note](#ai-usage-note) · [Status and cut list](#status-and-cut-list)

The full design, every decision and the milestone plan are in [docs/DEVELOPMENT_PLAN.md](docs/DEVELOPMENT_PLAN.md). Things marked **designed, not built** there are specified (with test vectors) but deliberately not implemented.

## Run it

Prerequisites: [.NET SDK 8.0.427](https://dotnet.microsoft.com/download/dotnet/8.0) or a later 8.0 feature band (pinned in `global.json`). Optional: Node.js 18+ for the browser-script tests. Nothing else: SQLite runs inside the order service.

From the repository root, each app in its own terminal:

```bash
dotnet run --project src/PizzaMagic.OrderManager    # order service: http://localhost:5100/swagger
dotnet run --project src/PizzaMagic.Web             # order builder: http://localhost:5000
```

On first start the order service creates `pizzamagic.db` next to its project, applies the seed scripts in `data/seed` (menu, Washington tax, ILOVEPIZZA / SAVE5, time zones) and validates the catalog; it refuses to start on invalid data. To reset, stop it and delete `src/PizzaMagic.OrderManager/pizzamagic.db*`. Ports come from each project's `Properties/launchSettings.json` (override with `--urls`). Docker Compose is designed but not built.

**The fantasy realm test database (M15F).** A second, invented dataset (six restaurants in five made-up time zones, tax from 0% to 75%, a MYTHIC topping tier, realm promo codes) lives in its own file beside the normal one, and neither ever touches the other. Pick it with `--database`:

```bash
dotnet run --project src/PizzaMagic.OrderManager                        # default: pizzamagic.db (same as --database default)
dotnet run --project src/PizzaMagic.OrderManager -- --database fantasy  # pizzamagic-fantasy.db: data/seed/*.sql + data/test/fantasy-realm.sql
```

`--database` is the configuration key `Seed:Profile`, so `--Seed:Profile=Fantasy` or the environment variable `Seed__Profile=Fantasy` also work. Any other value stops startup with a message. The service logs the profile and the database file it opened. If `ConnectionStrings:PizzaMagic` is set explicitly, that file is used instead, and the service refuses a file that already holds the other dataset. To reset the realm, delete `src/PizzaMagic.OrderManager/pizzamagic-fantasy.db*`.

Pizza Magic (restaurant 1) is still the default restaurant there, with the spec menu. To shop at a realm restaurant, point the order builder at it with `OrderManager:RestaurantId`:

```bash
dotnet run --project src/PizzaMagic.Web -- --OrderManager:RestaurantId=1003   # Sunspire Ember Oven (75% tax, UTC+10:00)
```

Restaurants: 1001 Eldoria (8.875%, UTC−01:15), 1002 Dwarrowdeep (0%, UTC+04:15), 1003 Sunspire (75%, UTC+10:00), 1004 Mistmarsh (17.125%, UTC−07:45), 1005 Moonhollow (33.333%, shares Sunspire's zone), 1006 Frostholm (5%, UTC−02:15). Codes: MOONRISE, ARCHMAGE, HOARD, FORGE20 / DWARF20, FROSTFEAST (windows are in each restaurant's local time; see plan section 7.5). The file is generated: change `tools/PizzaMagic.FantasySeedGen`, then run `dotnet run --project tools/PizzaMagic.FantasySeedGen` from the repository root and delete the old fantasy database.

Tests:

```bash
dotnet test                          # about 2,200 .NET tests: pricing core, order service, order builder, end-to-end
node --test "tests/js/*.test.mjs"    # the two browser modules with logic (needs Node.js; keep the quotes)
```

The build treats warnings as errors. To list the spec's worked pricing examples as they run: `dotnet test --filter "FullyQualifiedName~Golden_vectors_price_to_the_cent" --logger "console;verbosity=normal"`.

## Using it

1. Open http://localhost:5000. Add pizzas (size, quantity, toppings, each as Regular, Extra 2x or Triple 3x), promo codes and a tip (preset %, custom % or dollars). The total updates as you go; each promo code is listed with what it saves (or why it doesn't apply yet) and a Remove button, and "Describe my order" shows the written description. Review stays disabled until the current total allows the order to be placed.
2. **Review order** prices the order again and shows exactly what will be charged. **Place order** sends it and opens the order's page (save the link). **Your orders** lists every order placed in this browser in the last 7 days, newest first, with its status, when it was placed (restaurant time), what's on it, its discounts and its total.
3. While the order is Not Started, **Change order** (back through the builder and Review) and **Cancel order** are available. The page polls the status every 5 seconds.
4. **Simulate the kitchen** (the restaurant side): open **Kitchen (staff)** at http://localhost:5000/Kitchen. It lists every active order, oldest first, with what to make, and a button for each order's next step (Start → Mark ready for pickup → Mark delivered); it refreshes every 15 seconds. Or use the API directly, sending the status you expect the order to be in:

   ```bash
   curl -X POST http://localhost:5100/api/v1/kitchen/orders/<orderId>/advance -H 'Content-Type: application/json' -d '{"expectedStatus":"NotStarted"}'
   ```

   `NotStarted` → `Started` → `ReadyForPickup` → `Delivered`. Repeating a call is safe (`alreadyApplied: true`). The same endpoint is in Swagger and in `src/PizzaMagic.OrderManager/PizzaMagic.OrderManager.http`. Once started, the order page drops its buttons and change/cancel are refused. (A timer-driven simulator is designed, not built.)

## Order service API

JSON over HTTP under `/api/v1`, money as integer cents, codes as catalog codes. Errors are RFC 9457 problem details with a stable `type` (for example `.../problems/price-changed`) and, for validation, messages keyed by JSON path (`lineItems[0].quantity`). Unknown JSON properties are rejected (400).

| Endpoint | Purpose | Notes |
|---|---|---|
| `GET /menu` | Sizes, toppings by tier with the server-computed price for each size, tip presets and limits | Promo codes are not listed |
| `POST /quotes` | Price a draft order: every intermediate value, a description, `normalizedDraft`, `canSubmit` | No writes. Promo problems never fail a quote (each code gets a status); invalid fields → 422 |
| `POST /orders` | Place an order → 201 with `orderId`, `ETag: "1"`, `Location` | **`Idempotency-Key` required.** `expectedTotalCents` = the total the customer confirmed; a different current total → 409 `price-changed` with the current quote |
| `GET /orders/{id}` | The stored order (from its snapshot, never re-priced), status, allowed actions | |
| `GET /orders/{id}/status` | Light status payload for polling | Includes the restaurant's local time of the last change |
| `GET /orders/{id}/description` | The written description | |
| `PUT /orders/{id}` | Replace the order's content while Not Started | **`If-Match: "<version>"` required** (412 if stale); `Idempotency-Key` optional; 409 once the kitchen has started |
| `POST /orders/{id}/cancel` | Cancel while Not Started | 409 once started; cancelling a cancelled order returns it again |
| `GET /kitchen/orders` | Restaurant side: active orders (Not Started, Started, Ready for Pickup), oldest first, with items, total and next status | Optional `restaurantId` (default restaurant otherwise); 404 for an unknown restaurant |
| `POST /kitchen/orders/{id}/advance` | Restaurant side: one step forward | `expectedStatus` required; repeats return `alreadyApplied` |

Examples (bash or Git Bash; on Windows PowerShell use `curl.exe` with the same arguments, the `.http` file, or Swagger):

```bash
# Price P9: 2 Large with pepperoni and bacon, ILOVEPIZZA, 15% tip -> totalCents 3148 ($31.48)
curl -X POST http://localhost:5100/api/v1/quotes -H 'Content-Type: application/json' \
  -d '{"lineItems":[{"kind":"Pizza","sizeCode":"LARGE","quantity":2,"toppings":[{"code":"PEPPERONI"},{"code":"BACON"}]}],"promoCodes":["ILOVEPIZZA"],"tip":{"mode":"Percent","value":"15"}}'

# Place it. Sending the same request again with the same key returns the same order (Idempotent-Replayed: true).
curl -i -X POST http://localhost:5100/api/v1/orders -H 'Content-Type: application/json' -H 'Idempotency-Key: readme-order-1' \
  -d '{"lineItems":[{"kind":"Pizza","sizeCode":"LARGE","quantity":2,"toppings":[{"code":"PEPPERONI"},{"code":"BACON"}]}],"promoCodes":["ILOVEPIZZA"],"tip":{"mode":"Percent","value":"15"},"expectedTotalCents":3148}'

# Status, then change it to one Medium pepperoni ($8.05) while Not Started, then cancel
curl http://localhost:5100/api/v1/orders/<orderId>/status
curl -i -X PUT http://localhost:5100/api/v1/orders/<orderId> -H 'Content-Type: application/json' -H 'If-Match: "1"' -H 'Idempotency-Key: readme-update-1' \
  -d '{"lineItems":[{"kind":"Pizza","sizeCode":"MEDIUM","quantity":1,"toppings":[{"code":"PEPPERONI"}]}],"promoCodes":[],"tip":null,"expectedTotalCents":805}'
curl -X POST http://localhost:5100/api/v1/orders/<orderId>/cancel
```

A conflicting second code is not an error on a quote: `"promoCodes":["ILOVEPIZZA","SAVE5"]` returns ILOVEPIZZA `Applied` and SAVE5 `Rejected` with "SAVE5 can't be combined with ILOVEPIZZA. Remove ILOVEPIZZA to use SAVE5." The `.http` file has more examples (dollar tip with its effective %, field errors, conflicts).

## Assumptions

Every place the rules were open, and what was decided. The full table (A1–A74, with rationale) is in plan section 3, and the decisions on the open questions and later requests (UQ1–UQ30) are in section 10.

**Prices and rounding**
- Money is integer cents. Rounding is half away from zero ($0.325 → $0.33), never banker's rounding, and every rounded amount is rounded **before** it is multiplied by a quantity.
- "8% / 11% of the base pizza price" is of the **size price** only, rounded per pizza (Medium + bacon: 11% of $7.00 = $0.77), then × quantity. Quantity multiplies the whole pizza, toppings included.
- A pizza with no toppings is valid. Quantity is 1–20 per line and 1–20 lines; 0 is an error, not a removal (removing a line means leaving it out).

**Tax**
- "6.5% of the total order" is charged on the merchandise subtotal **after discounts**, once per order (never per line), and never on the tip.
- 6.5% is Washington's state rate, so the restaurant is modeled in Washington (time zone America/Los_Angeles). Tax is rounded the way Washington law says (RCW 82.08.054: half-up on the combined tax, per invoice). The rounding method is per-jurisdiction data, so a jurisdiction that truncates can say so. Confirm with a tax professional before real use.

**Promo codes**
- Codes are trimmed and case-insensitive; duplicates collapse; at most 3 distinct codes.
- ILOVEPIZZA: 10% of the pre-tax subtotal, rounded once.
- SAVE5: "over $20" means **strictly more than $20.00**, on the subtotal before discounts, tax and tip. $20.00 doesn't qualify; $20.01 does.
- **SAVE5 + ILOVEPIZZA: the code entered first stays; the second is rejected** with a message naming the code to remove. A held code that isn't currently eligible (SAVE5 at $20.00) still blocks the other, and the order can't be placed until one of them is removed or the cart qualifies.
- On the running total, promo problems never break the total: each code gets a status and message. On submit and update, any code that would not apply is a 422, so an order is never stored with a code the customer may think applied.

**Tip**
- "Percentage of the total bill" = (subtotal − discounts + tax), not including the tip. A percent tip follows the bill; a dollar tip stays fixed and shows its effective rate, e.g. "$3.00 (10.96%)".
- Up to two decimals; 0 allowed; caps of $500 and 100% catch typos. Tips are never taxed or discounted.

**Orders and lifecycle**
- The order id is a random GUID and acts as the customer's key to the order (no accounts).
- "Cancelled" is added as a final status. Only the customer cancels, only while Not Started. The kitchen moves one step at a time and can't skip.
- "Delivered" means handed to the customer (the spec rules out delivery integration).
- What is submitted is exactly what Review showed. If prices changed in between, the order is refused with the new total (409) and nothing is created.
- An order keeps a snapshot of its prices, names and tax; later menu, promo or tax changes never alter it. Changing an order while Not Started re-prices it at current prices, which the customer confirms on Review.
- Retries: the same key replays the original response; the same key with a different body is refused (422). Keys last 24 hours.

**Scope**
- One restaurant is seeded in the normal database, though the schema supports several (global menu plus per-store overrides; the fantasy test database adds six). Prices are USD.
- Toppings cover the whole pizza, once each, as Regular, Extra (2x) or Triple (3x) (up to each topping's max, from the menu data). Extra is charged per layer: the rounded topping price × 2 or × 3, then × quantity (Large Pepperoni 2x = 2 × $0.96), and described as "Pepperoni 2x". Half and quarter toppings, crust/sauce upgrades and add-ons are designed, not built, and the API refuses them with a clear message. The default crust (hand tossed) and sauce (classic tomato) are free.
- The cart lives in an in-memory session (2 hours idle): fine for one server, lost on restart.

## Design write-up

**Money and where pricing runs.** Money is `long` cents and rates are integer parts-per-million (6.5% = 65 000): no floating point anywhere. Rounding happens at six named points (topping per pizza, placement, item promo, order promo, tax, percent tip) through two named functions: `RoundHalfAwayFromZero` everywhere, and `Truncate` only for a tax jurisdiction configured to truncate. Amounts are rounded *before* they are multiplied (82.5¢ → 83¢ × 10 = $83.30, not $83.25), and discounts and tax are split across lines by largest remainder, so parts always sum to the total. **Pricing exists once, in `PizzaMagic.Domain`, and runs only in the order service.** The builder's running total is a `POST /quotes` (debounced, one request in flight, newest wins), and the browser only formats the server's numbers. Review seals the server's normalized order and total into a tamper-evident ticket; Place submits exactly that with `expectedTotalCents`, and the service refuses (409, with the new total) if prices changed. The total seen is the total charged by construction, not by keeping two implementations in step. The cost is a round trip per edit.

**Adding a size, topping category or promo.** The menu is rows: a size is a `product` row, a topping category is a `topping_tier` row with a pricing rule, and stores override price or availability with sparse rows. A promo is a `promotion` row (effect kind + JSON parameters) with `promotion_condition`, `promotion_code` and conflict rows (`promotion_incompatibility` pairs, exclusion groups or "exclusive"). The engine looks kinds up by name and never mentions a product, tier or code. This script, as `data/seed/0002-*.sql`, adds a Party size and a "$10 off $50 or more" code that conflicts with both existing codes. After a restart, Pepperoni on a Party is $1.44 and SAVE5 after BIGTEN gets "SAVE5 can't be combined with BIGTEN…" (tried on a scratch database, no code changed):

```sql
INSERT INTO product (id, product_type_code, code, display_name, price_cents, tax_class_id, sort_order) VALUES (4,'PIZZA_SIZE','PARTY','Party',1800,1,4);
INSERT INTO promotion (id, name, customer_description, effect_type, effect_params, priority, valid_from_local, valid_to_local)
  VALUES (3,'Big Ten','$10 off orders of $50 or more','fixed_off_order','{"amountCents":1000}',100,NULL,NULL);
INSERT INTO promotion_code (code, promotion_id) VALUES ('BIGTEN',3);
INSERT INTO promotion_condition (id, promotion_id, condition_type, params) VALUES (2,3,'min_subtotal','{"amountCents":5000,"inclusive":true}');
INSERT INTO promotion_incompatibility (promotion_id_low, promotion_id_high) VALUES (1,3), (2,3);
```

A new *kind* of rule (say "2+ pizzas") is one new `IConditionEvaluator` class registered by name; the engine and other promos are untouched. Product *types* are deliberately code, since they decide a line's shape. Startup validation refuses data the code can't honor rather than ignoring it.

**The order lifecycle.** Not Started → Started → Ready for Pickup → Delivered, plus Cancelled from Not Started only; the allowed moves per actor are one table, tested for all 25 status pairs.
- *Retry after a timeout.* Creating an order requires an `Idempotency-Key`. The builder's key stays the same while the cart's content does, and its HTTP client retries with the same key and identical bytes. The service stores the response with the key in the same transaction as the order, re-checking the key inside the write lock, so a retry of a committed request replays the original response and a parallel duplicate loses. If every attempt fails, the customer sees "We couldn't confirm your order", the cart is locked, and **Try again** re-sends the same ticket and key. **Guarantee:** one key, at most one order, the same answer every time (50 parallel retries; end to end with a lost response).
- *Cancel as the kitchen starts.* Each is one conditional `UPDATE`: cancel `WHERE id = @id AND status = 'NotStarted'`, kitchen `WHERE id = @id AND status = @expected`. SQLite serializes writers, so exactly one changes the row; the other updates 0 rows, re-reads and says why (409 "the kitchen has started this order", with the time). Updates add `If-Match` on the version. **Guarantee:** exactly one wins and the loser is told accurately (50 runs, service and HTTP; a naive read-then-write version is shown to let both succeed).

**Cut, faked, and next.** I chose to build the core path only (decision UQ7). Designed, not built: half and quarter toppings (whole-pizza extra, 2x/3x, is built), crust/sauce upgrades and add-ons, item promos and BOGO, multi-level tax with tax classes, a kitchen timer, Docker Compose. Faked: the kitchen is an unauthenticated endpoint; the cart is an in-memory session; the browser DOM code is checked by a scripted headless-browser session rather than by the test suite. Next, in order: placement pricing (its test vectors are written), upgrades and add-ons, item promos and BOGO, multi-level tax, then Compose and the timer; longer-term directions are in plan section 9.4. **Timebox:** this went far past 3 hours: planning on 2026-10-27/28, then build commits from 07:25 to 15:42 on 10-28 (about 8 hours, AI-assisted), plus features and fixes requested after trying the app (extra toppings, a richer orders page, the kitchen page, the fantasy test database; to 17:13). In 3 hours I would keep the money model, quote-as-the-total, idempotent submit and conditional status updates, and drop the full schema, store overrides, time zones and most of the UI.

## Tests and invariants

**Chosen invariants, and why.** I tested the things a customer or the restaurant would lose money or trust over, and the things AI-written code tends to get subtly wrong:

| Invariant | Why | Where |
|---|---|---|
| The total seen is the total charged: running total = Review = submitted = stored | Requirement 2; the core promise | `Quote_total_equals_submitted_total_equals_stored_total` for every spec example, the Checkout tests, and the end-to-end `Flow_build_submit_status_edit_cancel` and `Web_quote_equals_direct_api_quote` |
| Exact money: spec examples to the cent, half away from zero, round before multiply | Rounding drift is the classic pricing bug | `Golden_vectors_price_to_the_cent` (P1–P26 and more), the `Monetary` tests, and property tests over 500 generated orders (discount and tax parts sum exactly, never negative) |
| One source of truth for rounding and pricing | Keeps the first invariant true as code grows | Architecture scans: no floating point, no `Math.Round` and no bare price numbers in the engine; truncation only in `TaxCalculator`; the web app can't reference the pricing core or the database |
| One key → at most one order | "Customers … may see timeouts and retry" | `Fifty_parallel_submissions_with_one_key_create_one_order`, replay, reused key with a different body, key expiry, `Transaction_is_immediate`, the client's retry-pipeline tests, and the end-to-end lost-response flow |
| Cancel/update only while Not Started, even under races | Requirement 5's guarantee | All 25 status pairs per actor; `Cancel_and_kitchen_start_racing_exactly_one_wins` (and over HTTP), `Edit_and_kitchen_start_racing_never_edit_a_started_order`, and `Naive_check_then_act_double_succeeds` as the counter-example |
| Promo rules as written: SAVE5 strictly over $20, the conflict message, codes re-evaluated on every edit | "We read this closely" | The `Promotions` tests (PR vectors) and quote/submit API tests |
| A placed order never changes when the menu, promos or tax change | Seen = charged after the fact | `Historical_order_unchanged_after_price_rate_label_and_promo_change` |
| Instants stored as UTC; promo windows in the restaurant's local time, correct across DST | Off-by-an-hour bugs | `Time` tests, DST promo-window vectors, `Every_time_column_is_utc_or_local_text` |
| Bad data fails at startup, not at checkout | Data-only changes need a safety net | Startup validation tests |

I checked that the important tests can fail by temporarily breaking the code (banker's rounding, swapped tax methods, a cancel without the status condition, a missing idempotency guard, a stray `Math.Round`). Each break failed the expected tests, and was then reverted. The log is in `docs/ai-log.md`.

**Not covered:** the browser DOM wiring (`order-builder.js`, `order-details.js`) has no automated browser test in the suite. Its logic modules have `node --test` tests, and the builder was driven end to end in headless Edge with a scripted session (add a pizza, extra toppings, apply and remove promos, Review enabling, review and place), but that script is not part of `dotnet test`. There is nothing for multi-process deployment (the database's unique key is the backstop there, but untested across processes), load, session scale-out, or accessibility. The designed-not-built features have test vectors written in the plan but no tests. The fantasy realm test database (M15F) covers only the core part: its multi-level tax, tax-class rates and themed crusts and sauces with upcharges need features that are designed, not built, and the startup validator still refuses that data.

**Fantasy realm (M15F).** `FantasyRealmTests` runs the built features far outside the spec's single restaurant: the generated SQL must equal the generator's output; the catalog validates; every realm restaurant's menu, prices, policy and tax resolve; nine golden orders (F1–F9) pinned to the cent, computed independently by a Node script, cover 0% and 75% tax, both rounding methods where they differ (75% of 777 = 582.75 → 582 truncated; 8.875% of 875 = 77.66 → 78 half-up) and the MYTHIC tier on every size; a promo window opens at local midnight at each fixed offset to the millisecond, is open in one zone and closed in another at the same instant, and gives the same answer for the two restaurants sharing a zone; and the invariants (subtotal − discount + tax + tip = total, allocations sum to each discount, tax lines sum to the tax) hold over 560 generated orders across all seven restaurants. `DatabaseProfileTests` covers `--database`.

Test projects: `tests/PizzaMagic.Domain.Tests` (pricing core), `tests/PizzaMagic.OrderManager.Tests` (API and persistence against a real temp-file SQLite), `tests/PizzaMagic.Web.Tests` (order builder with a scripted order service), `tests/PizzaMagic.EndToEnd.Tests` (both real apps in-process, plus the architecture rules) and `tests/js` (`node --test`).

## AI usage note

Claude Code (Claude Opus 5.5) wrote most of the plan, the tests and the code under my direction: I made the design decisions (plan section 10) and the agent worked milestone by milestone, test first, keeping [docs/ai-log.md](docs/ai-log.md), a running log of the output that had to be fixed and of what was checked by hand. It was also used to research Washington's tax-rounding law. **Rejected:** I first asked for tax to always be truncated. Research during the session into RCW 82.08.054 showed that Washington requires half-up rounding on the combined tax, so I withdrew that decision and the rounding method became per-jurisdiction data. **Fixed AI output:** the first `EffectiveMenu` read the default crust and sauce before they were assigned, so the database's defaults would have been silently ignored. No test caught it (in the seed the default also sorts first), so `Global_default_wins_over_a_lower_sort_order` was added. **Verified rather than trusted:** the AI's tests were not taken at their word. Each key invariant was broken on purpose to confirm a test would fail. For "cancel and a kitchen start can never both succeed", removing the status condition from the cancel `UPDATE` failed exactly the three tests that cover it, and the condition was restored. `Naive_check_then_act_double_succeeds` stays in the suite to show the bug the design prevents. The log has the rest, including mistakes in AI-written tests.

## Status and cut list

Built test-first, milestone by milestone (plan section 8; red test commits, then the implementation): **M0–M16 are done**, plus the core part of M15F and the requested additions below.

| Milestone | What it adds |
|---|---|
| M0–M1 | Solution and package pins; money in integer cents, the two named rounding rules, exact splitting, `$` formatting |
| M2–M6 | Menu with per-restaurant overrides and restaurant local time; promotion engine; Washington tax; pricing engine (validation, discounts, tax, tip with effective %); order description |
| M7–M8 | Order state machine; full SQLite schema (33 tables), seed scripts and startup validation |
| M9–M11 | Menu and quote API; idempotent submit with price check; update (If-Match), cancel, kitchen advance, race-proof status changes |
| M12–M14 | The order builder: typed client with safe retries, builder page with a server-computed running total, Review/Place with a sealed ticket, order page with live status, change, cancel, recent orders, lookup |
| M15 | End-to-end flows with both real apps, and architecture rules |
| M16 | This README: API, assumptions, design write-up, tests, AI note |
| After M16 | Requested after trying the app: whole-pizza extra toppings 2x/3x, a visible promo list with Remove, a Review button that is disabled until the order can be placed (UQ29); status, date and summary on "Your orders"; the Kitchen page and its queue endpoint (UQ30); Bootstrap 5.1 badge colours |
| M15F | Fantasy realm test database, core part: generator, `data/test/fantasy-realm.sql`, `--database fantasy` (side-by-side `pizzamagic-fantasy.db`). Extended part (multi-level tax, class rates, themed crusts and sauces) designed, not built |

The designed-not-built list and future directions are in [Design write-up](#design-write-up) above and in plan section 9.4.

### Repository layout

```
src/PizzaMagic.Domain/        pricing core (Monetary, Catalog, Pricing, Promotions, Tax, Time, Ordering)
src/PizzaMagic.Contracts/     shared request/response types and formatters
src/PizzaMagic.OrderManager/  order service (Web API, EF Core + SQLite, seed runner)
src/PizzaMagic.Web/           order builder (MVC, Session cart, typed client)
data/seed/                    the menu, tax, promos and time zones, as SQL
data/test/                    the generated fantasy realm test data (applied only with --database fantasy)
tests/                        one test project per app, end-to-end tests, JS tests
tools/                        time zone and fantasy realm seed generators, logo and favicon generators
docs/DEVELOPMENT_PLAN.md      design, assumptions (section 3), decisions (section 10), milestones (section 8)
docs/ai-log.md                running log of AI use, rejected output and hand verification
```
