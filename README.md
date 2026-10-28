# Pizza Magic!

A pizza order builder with a separate order service. A customer builds an order, watches the total update as they change it, and sends it to the restaurant's order service. They can then check on the order and, until the kitchen starts it, change or cancel it.

| Piece | Project | What it is | URL when running |
|---|---|---|---|
| Order builder | `src/PizzaMagic.Web` | ASP.NET Core 8 MVC app for customers (no login) | http://localhost:5000 |
| Order service | `src/PizzaMagic.OrderManager` | ASP.NET Core 8 Web API; the only process that touches the database (SQLite) | http://localhost:5100 (Swagger at `/swagger`) |
| Pricing core | `src/PizzaMagic.Domain` | Pure C# library: menu, pricing, promotions, tax, local time | (library) |
| Wire contract | `src/PizzaMagic.Contracts` | Shared DTOs and the money / percent formatters | (library) |

The full design, milestone plan and decisions are in [docs/DEVELOPMENT_PLAN.md](docs/DEVELOPMENT_PLAN.md).

## Current status

The project is built test-first, milestone by milestone (plan section 8). **Done so far: M0-M12**: the whole pricing core, the order description, the order state machine, the database, and the complete order service API: menu, quotes, idempotent submission, reads, update and cancel while Not Started, and the kitchen advancing orders. The order builder's client for the order service is in place (M12); its screens come in M13-M14.

| Milestone | What it adds | Status |
|---|---|---|
| M0 | Solution, package pins, ports, smoke test | Done |
| M1 | Money in integer cents, the two named rounding rules, exact splitting, `$` formatting | Done |
| M2 | Menu with per-restaurant overrides, pizza line pricing, restaurant local time | Done |
| M3 | Promotion engine: ILOVEPIZZA / SAVE5 as data, conflicts, validity windows | Done |
| M4 | Tax: Washington 6.5%, rounding method from the jurisdiction's data | Done |
| M5 | Pricing engine: validation, discounts, tax, tip (with effective %), totals | Done |
| M6 | Order description: "2 x Large pizza with Pepperoni, Bacon", promos, tax, tip, total | Done |
| M7 | Order state machine: kitchen steps forward, customer cancels only while Not Started | Done |
| M8 | Database: full SQLite schema, seed scripts (menu, WA tax, promos, time zones), startup validation | Done |
| M9 | API: `GET /api/v1/menu` and `POST /api/v1/quotes` (live pricing with validation errors by field) | Done |
| M10 | Order API: idempotent submit (Idempotency-Key, price check against the confirmed total), order / status / description from the stored snapshot | Done |
| M11 | Update (If-Match) and cancel while Not Started, kitchen advance, and race-proof status changes (cancel vs kitchen start: exactly one wins) | Done |
| M12 | The order builder's typed client for the order service: safe retries (same Idempotency-Key and body), no retry on 4xx, problems parsed, outages reported instead of thrown | Done |
| M13-M15 | Order builder UI with running total, checkout, status pages, end-to-end tests | Next |
| M15F | Fantasy realm test database: invented zones, restaurants, tiered taxes 0-75%, a MYTHIC topping tier, themed crusts and sauces | Planned |
| M16 | README sections required by the spec: API reference, assumptions, design write-up, AI usage note | Planned |

**What you can look at today:**
- **The pricing core, through its tests.** Every worked example in the plan (for instance P9: 2 large pepperoni and bacon, ILOVEPIZZA, 15% tip = **$31.48**) is a test you can run (see below).
- **The order builder** at http://localhost:5000: the branded welcome page (logo, favicon). The builder screens arrive in M13.
- **The order service** at http://localhost:5100/swagger: on first start it creates and seeds `pizzamagic.db` and validates the catalog. Try the menu and price any order in Swagger, or with the ready-made requests in `src/PizzaMagic.OrderManager/PizzaMagic.OrderManager.http` (see "Try the API" below).

## Prerequisites

- [.NET SDK 8.0.427](https://dotnet.microsoft.com/download/dotnet/8.0) or a later 8.0 feature band (pinned in `global.json`).
- Optional: Node.js 18+ only to regenerate the logo and favicon (`tools/brand`).

Nothing else: there is no database server to install. SQLite runs inside the order service from M8 onwards.

## Build and test

From the repository root:

```bash
dotnet tool restore     # restores the pinned dotnet-ef tool
dotnet test             # builds everything and runs all tests
```

Expected result today: about 1,900 tests passing, with 0 warnings. Warnings are treated as errors, so a warning fails the build.

Useful variations:

```bash
# Run one area of the pricing core
dotnet test --filter "FullyQualifiedName~PizzaMagic.Domain.Tests.Pricing"
dotnet test --filter "FullyQualifiedName~PizzaMagic.Domain.Tests.Promotions"
dotnet test --filter "FullyQualifiedName~PizzaMagic.Domain.Tests.Tax"

# Run the spec's worked pricing examples (P1-P25) and list each one
dotnet test --filter "FullyQualifiedName~Golden_vectors_price_to_the_cent" --logger "console;verbosity=normal"
```

Test areas in `tests/PizzaMagic.Domain.Tests`:

| Folder | Covers |
|---|---|
| `Monetary` | Rounding (32.5 cents becomes 33, never banker's rounding), truncation, checked money arithmetic, exact splits, `$1,234.56` formatting |
| `Catalog` | Topping prices from tiers (8% / 11% of the size price), restaurant overrides and availability, defaults |
| `Promotions` | Code clean-up, duplicates, limits, conflicts ("SAVE5 can't be combined with ILOVEPIZZA"), validity windows across daylight-saving changes |
| `Tax` | 6.5% half-up per RCW 82.08.054, the TRUNCATE option, tax lines that sum exactly |
| `Time` | UTC to Los Angeles local time from stored offset periods, including both 2026 DST switches |
| `Pricing` | Full quotes: totals to the cent, validation errors with field paths, tips, and properties over 500 generated orders |
| `Ordering` | The order description and every allowed or refused status change (all 25 status pairs for each actor) |

And in `tests/PizzaMagic.OrderManager.Tests` (each test builds a real SQLite database in a temp file):

| Folder | Covers |
|---|---|
| `Api` | The menu, quote and order endpoints over HTTP against a real database: prices to the cent, promo outcomes, field errors by path, idempotent submission (including 50 parallel retries with one key), quote = submitted = stored total for every spec example, orders unchanged by later price, tax or promo edits, update/cancel/kitchen rules and their check order, and cancel-vs-kitchen and edit-vs-kitchen races |
| `Persistence` | All 33 tables and their CHECK / foreign-key rules, the seed matching the spec, the spec examples priced from the database, the seed-script runner, startup validation, UTC and local time columns, and the time zone seed matching tzdb |

And in `tests/PizzaMagic.Web.Tests`:

| Folder | Covers |
|---|---|
| `Clients` | The order builder's client behind its real retry pipeline: a 503 is retried with the same key and identical body, 4xx is never retried, problem types and fields are parsed, and outages come back as "unavailable" instead of exceptions |

## Run the apps

Each app runs in its own terminal, from the repository root:

```bash
dotnet run --project src/PizzaMagic.OrderManager    # order service: http://localhost:5100/swagger
dotnet run --project src/PizzaMagic.Web             # order builder: http://localhost:5000
```

Stop either one with Ctrl+C. The ports come from each project's `Properties/launchSettings.json`. To use a different port, add for example `--urls http://localhost:5200`.

On first start the order service creates `pizzamagic.db` next to the project, applies the seed scripts in `data/seed`, and validates the catalog (it refuses to start if the data is invalid). Delete `pizzamagic.db*` to reset it; the `.gitignore` already excludes it. The time zone seed is generated: after a NodaTime upgrade, run `dotnet run --project tools/PizzaMagic.TzSeedGen` from the repository root and commit the result.

Docker Compose is designed but not part of the built scope (plan M17).

## Try the API

With the order service running (bash or Git Bash; in PowerShell use the `.http` file or Swagger instead):

```bash
# The menu, with server-computed topping prices
curl http://localhost:5100/api/v1/menu

# Price an order (P9: 2 large pepperoni and bacon, ILOVEPIZZA, 15% tip = $31.48)
curl -X POST http://localhost:5100/api/v1/quotes -H 'Content-Type: application/json' -d '{"lineItems":[{"kind":"Pizza","sizeCode":"LARGE","quantity":2,"toppings":[{"code":"PEPPERONI"},{"code":"BACON"}]}],"promoCodes":["ILOVEPIZZA"],"tip":{"mode":"Percent","value":"15"}}'
```

The quote returns every intermediate value (line prices, discounts and where they landed, tax lines, tip, total), a written description, the order as the server will accept it (`normalizedDraft`) and whether it can be submitted (`canSubmit`). Promo code problems never fail a quote: each code gets its own status (Applied, NotEligible, Rejected, InvalidFormat, ...). Field errors return 422 with a message per JSON path. To place an order, POST the same draft to `/api/v1/orders` with an `Idempotency-Key` header and `expectedTotalCents` set to the quoted total: a retry with the same key returns the same order (`Idempotent-Replayed: true`), and a total that no longer matches returns 409 with the current quote. Once placed, an order can be changed with `PUT /api/v1/orders/{id}` (header `If-Match: "<version>"`) or cancelled with `POST /api/v1/orders/{id}/cancel`, but only while it is Not Started.

**Simulating the restaurant.** The kitchen moves an order forward one step at a time with

```bash
curl -X POST http://localhost:5100/api/v1/kitchen/orders/<orderId>/advance -H 'Content-Type: application/json' -d '{"expectedStatus":"NotStarted"}'
```

(`NotStarted` -> `Started` -> `ReadyForPickup` -> `Delivered`; send the status you expect the order to be in). Repeating the same call is safe: it returns `alreadyApplied: true` instead of advancing twice. Once the kitchen has started an order, update and cancel return 409 with the time it started. If a cancel and a kitchen start arrive at the same moment, exactly one wins (tested 50 times in a row). A timer-driven simulator is designed but not built (plan M18).

More examples, including a conflicting promo code, a dollar tip with its effective percentage, field errors and order submission, are in `PizzaMagic.OrderManager.http`, which Visual Studio and VS Code (REST Client) can run directly.

## Repository layout

```
src/PizzaMagic.Domain/        pricing core (Monetary, Catalog, Pricing, Promotions, Tax, Time)
src/PizzaMagic.Contracts/     shared contract types and formatters
src/PizzaMagic.OrderManager/  order service (Web API)
src/PizzaMagic.Web/           order builder (MVC)
tests/                        one test project per app plus end-to-end tests
tools/brand/                  logo and favicon generators
docs/DEVELOPMENT_PLAN.md      design, decisions (section 10), milestones (section 8)
docs/ai-log.md                running log of AI use, rejected output and hand verification
```

## Still to come in this README

The spec also asks for these sections. They are written in M16, when the pieces they describe exist:
- **Order service API:** endpoints with example requests. The designed API is in plan section 5.
- **Assumptions:** every ambiguity and the decision made. The current list is in plan section 3, and the user's decisions are in section 10.
- **Design write-up:** money and rounding, extensibility, the order lifecycle and retries, and what was cut.
- **AI usage note:** written from [docs/ai-log.md](docs/ai-log.md).
- **Tested invariants:** which are covered and which are not (plan section 7.2).
