# AI usage log

A running log kept from M0. Each entry records the tool, what it was used for, any output rejected or fixed, and anything verified by hand. The README's AI usage note (M16) is written from this log.

## 2026-10-27 to 2026-10-28: planning and M0

**Tool:** Claude Code (Claude Opus 5.5), including multi-agent planning workflows (independent drafts, synthesis, adversarial critics, revision).

**Used for:**
- Drafting and revising `docs/DEVELOPMENT_PLAN.md` from the specification and a series of design decisions (UQ1–UQ19).
- Researching Washington sales-tax rounding (RCW 82.08.054).
- M0 scaffolding: central package management, build settings, marker types, smoke test.

**Rejected or fixed AI output:**
- *Tax truncation.* A tax-truncation rule was applied to the plan at the user's request. Research into RCW 82.08.054 showed that Washington requires half-up rounding on the combined state and local rate. The rule was withdrawn, and tax rounding became per-jurisdiction data seeded with the WA rule.
- *Topping cover algorithm.* The first proposal priced each count layer separately. A verification pass found that this is not globally optimal if more portions are ever added, and that it compared factors instead of rounded cents. It was replaced by a minimum-cost exact cover over count vectors, compared in rounded cents.
- *DST fall-back flicker.* A proposed "compare promo boundaries in local time" rule would have re-enabled an expired promo during the repeated 01:00–02:00 hour on a fall-back night. It was replaced by resolving each local boundary to its earliest UTC instant.
- *Mangled project references.* During M0, a generated shell command lost the backslashes in the test projects' `ProjectReference` paths. This was caught by inspecting the files and fixed before building.

**Verified by hand rather than trusted:**
- Worked pricing vectors, recomputed independently to the cent: P1–P29, PL4, PL8, PR1–PR8, the M5 allocation, the multi-level and Seattle tax vectors (T4, T7, T14–T16), and the BOGO B11 cases.
- 2026 America/Los_Angeles DST transition instants (2026-03-08T10:00Z, 2026-11-01T09:00Z) and the promo boundary instants derived from them.
- M0: NuGet was queried for the latest patch of each pinned package before bumping the pins.

## 2026-10-28: M1 money primitives

**Verified rather than trusted:** after the money tests passed, `RoundHalfAwayFromZero` was temporarily mutated so that exact halves round down (banker's-style), to confirm that the tests can fail. 5 tests failed (the midpoint and 6.5%-of-$5.00 cases), and the mutation was then reverted. The tie-break rules of the largest-remainder allocator were also hand-checked against plan vectors M5–M9.

## 2026-10-28: M2 catalog, line pricing, local time

**Fixed AI output:** the first draft of `EffectiveMenu` computed the default crust and sauce in its constructor from a set of default ids that was only assigned by an `init` property afterwards. The global `is_default` flags would therefore have been silently ignored, and the fallback (lowest sort order) would have hidden the bug with the seed data. It was caught on review before the first build, and the ids became a constructor argument. A follow-up review found that no test would have caught it: in the seed, `HAND_TOSSED` is both the default and the lowest-sorted crust. `Global_default_wins_over_a_lower_sort_order` was added, and reintroducing the bug makes exactly that test fail (93 pass, 1 fails).

## 2026-10-28: M3 promotion engine

**Verified rather than trusted:** two deliberate mutations, each reverted afterwards.
- Making exclusivity one-sided (an exclusive code only blocks codes entered after it) fails exactly 1 test (PR15, `[ILOVEPIZZA, EXCL]`).
- Comparing the promo end against the local wall clock instead of the resolved UTC boundary fails exactly 2 tests: the 09:00Z and 09:15Z cases on the fall-back night, where the naive comparison re-opens an expired promo. This is the flicker the design exists to prevent (PR13b).

**Fixed AI output:** two of the first-draft tests were wrong before the first run. One zero-discount test assumed the wrong application order: equal priorities fall back to promotion id, so ILOVEPIZZA (id 1) applied first. The other test, described as "dropped codes block nothing", actually tested the code limit. Both were rewritten before committing the red tests.

## 2026-10-28: M4 single-level tax

**Verified rather than trusted:**
- Swapping the HALF_UP and TRUNCATE branches in `TaxCalculator` fails 6 tests (T1-T3 and T13).
- A source scan confirms that `Rounding.Truncate` is called only from `TaxCalculator` (outside its own definition).

**Process note:** for the second time (also in M1), the automatic restore after a mutation check used `git checkout` on a not-yet-committed file, which silently did nothing. The mutation was restored by hand and verified. From now on, mutations are made and reverted with explicit edits.

## 2026-10-28: M5-M6 pricing engine and description

**Fixed AI output:**
- `Tip_is_not_taxed` first compared two `TaxResult` records with `ShouldBe`. Record equality compares the `Lines` list by reference, so identical tax results were reported as different. It now uses `ShouldBeEquivalentTo`.
- The first `PercentFormatter.FormatBasisPointsCompact` trimmed trailing zeros with a chain of string replacements that was hard to verify and risky for values like 10%, where removing "0%" breaks the number. It was replaced before any commit with integer-based formatting, and 10% and 10.05% test cases were added.

**Verified rather than trusted:** the effective-tip-rate examples (10.96%, 21.91% and the 0.05% half-up case) were recomputed independently with a script before being written into the tests.

## 2026-10-28: M7 state machine

**Process slip found and corrected:** plan section 7.1 says red commits must compile: tests plus `NotImplementedException` stubs, failing for the right reason. The red commits for M1-M6 instead failed to compile on missing types. The M7 red commit also contained a real syntax error (`from from in ...` in a query). It was caught before the implementation, fixed, given stubs, and amended (it had not been pushed). It now fails with 83 `NotImplementedException` failures and nothing else. The earlier red commits (M1-M6) are not pushed either, but rewriting six milestones of history to add stubs was judged not worth it, so they stay as they are and this note records the deviation. From M8 on, red commits compile.

## 2026-10-28: M8 persistence

**Design decision:** the schema is the plan's DDL verbatim, as hand-written SQL run by the migration. It is not regenerated from EF fluent configuration, which avoids maintaining a second definition of 33 tables and dozens of CHECKs that could drift. The EF model maps only the tables the code uses.

**Fixed AI output:** the test helper first ran raw SQL through `ExecuteSqlRawAsync`, which calls `string.Format` even with no parameters, so statements containing JSON (`{"amountCents":...}`) failed with `FormatException` (8 tests). The seed runner already used raw ADO.NET for exactly this reason; the helper was switched too.

**Verified rather than trusted:**
- The DDL and both seeds were first loaded into SQLite with Node's built-in `node:sqlite`, with foreign keys on: 17 products, 217 offset periods, and no foreign-key violations.
- The generated time zone seed matches the plan's hand-checked 2026 Los Angeles switches.
- The service was started on a fresh database and the data was read back: both scripts were logged, and `journal_mode` was `wal`.

## 2026-10-28: M9 menu and quote API

**Fixed AI output:**
- `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes` was first set on `ApiBehaviorOptions`, where it does not exist (it is on `MvcOptions`). The build caught it.
- Two tests compared records that hold lists with `ShouldBe` / `ShouldBeEquivalentTo`. That compares the list by reference, and the equivalence check also compares collection types (array vs List). This is the same trap as in M5. Those assertions now compare the scalar fields and the list separately.

**Verified rather than trusted:** the running service was exercised with curl: the menu grid, and the P24 quote (both codes) returning 3148 with ILOVEPIZZA Applied, SAVE5 Rejected and the expected description. The README's curl command was also run verbatim and returned 3148.

## 2026-10-28: M10 order submission

**Fixed AI output:**
- The stored-order view first looked allocations up by database id. The submit path builds the response before saving (so the replay body can be stored in the same transaction), when every id is still 0, so the lookups collided. It now uses the navigation properties, which are set in memory and `Include`d on reads.
- The first `Transaction_is_immediate` test used `Default Timeout=0` for the second writer, expecting "fail immediately". In ADO.NET, 0 means wait forever, so the test hung. It now uses 1 second.
- Two tests read the database directly before the in-process host had started (and created the schema). The helpers now start the host first.
- `DescriptionSource` referred to `Promotions.PromoStatus`, which C# resolved to the record's own `Promotions` property instead of the namespace.

**Verified rather than trusted:**
- Disabling both the re-check inside the IMMEDIATE transaction and the primary-key backstop makes `Fifty_parallel_submissions_with_one_key_create_one_order` fail, while the sequential replay test still passes. The race test therefore really exercises the in-lock guard. Both guards were restored, and a search confirms no `false &&` remains.
- On the running service: submit returned 201 with ETag and Location, a retry with the same key returned the same order with `Idempotent-Replayed: true`, status showed the local PDT time, the description came from the snapshot, and a stale total returned 409.

## 2026-10-28: M11 update, cancel, kitchen, races

**Verified rather than trusted:**
- Removing the status check from cancel's conditional UPDATE (so a cancel overrides a started order) makes exactly three tests fail: both cancel-vs-start race tests (service level and HTTP) and `Cancel_when_started_is_409_and_the_row_is_unchanged`. The condition was then restored.
- `Naive_check_then_act_double_succeeds` demonstrates the bug the conditional UPDATE prevents: a read-then-write cancel and start both report success.
- Live run on the service: update returned 200 with ETag "2"; kitchen advance moved the order to Started (v3); cancel then returned 409 "The kitchen has started this order; it can no longer be cancelled."

**Refactor:** M10's submit-only result type became one `OrderResult` with a single outcome-to-HTTP mapping (`OrderResults.ToHttp`) shared by submit, update, cancel and advance.

## 2026-10-28: M12 typed client

**Verified rather than trusted:** `Submit_retry_reuses_key_and_body` goes through the real `AddStandardResilienceHandler` pipeline with a stub as the network. It records three requests with one key and identical body bytes, so POST retries really happen (they are not silently disabled) and really are byte-identical. A scan confirms that `PizzaMagic.Web` references no Domain, EF Core or SQLite code.

## 2026-10-28: M13 order builder page

**Fixed AI output:**
- The plan's JS test command `node --test tests/js` fails on Node 24, which treats a directory argument as a file. It is now `node --test "tests/js/*.test.mjs"`.
- The template's `HomeController.Index` also claimed `/`, alongside the builder's `[HttpGet("/")]`. The home action and view were removed, Home keeps only the error page, and the nav points at the builder.

**Verified rather than trusted:**
- All 11 quote-sequencer tests fail against the `NotImplementedException` stub, and pass against the real module.
- On the running pair: the page rendered the seed menu; the quote proxy returned the priced quote for 2 Large Pepperoni with ILOVEPIZZA; quantity 0 came back as 422 at `lineItems[0].quantity`; a post without the antiforgery header got 400; the session cart survived a reload; a stale draft (revision 1 after revision 2) did not overwrite it; `.mjs` is served as `text/javascript`.
- Not covered by an automated test yet: the DOM wiring in `order-builder.js` (syntax-checked only). The M15 end-to-end tests are where it gets exercised in a browser.

## 2026-10-28: M14 checkout and order pages

**Fixed AI output:**
- The first checkout test helper called `EnsureSuccessStatusCode()` on the builder quote, but two tests deliberately make that quote fail (so the cart keeps un-normalized input). The helper now only rejects a refused (400) post. The app was right; the test was wrong.
- A C# string literal containing `
`, written through a shell script, became real line breaks and broke the build. It was fixed with an exact edit.
- The plan had `GET /Orders/{id}/Edit`, which changes the Session. It is built as an antiforgery POST, and the plan row was updated.

**Verified rather than trusted:**
- Removing the cart lock (saving builder drafts while an order is waiting for confirmation) fails exactly `Place_unavailable_keeps_ticket_and_locks_cart`. Removing the key rotation on content change fails exactly `Key_reused_until_content_changes`. Both were reverted, and a search confirms no `false &&` remains.
- All 28 new C# tests and 4 new JS tests failed against the stubs before the implementation.
- On the running pair: the P9 order (2 Large Pepperoni + Bacon, ILOVEPIZZA, 15% tip) reviewed and placed at $31.48; it appeared under "Your orders"; Change loaded it into the builder, and updating it to one Medium Pepperoni gave $8.05 (version 2); after a kitchen advance the status endpoint showed Started, the page dropped its buttons, and cancel showed the kitchen message; lookup by number redirected to the order.
- Not covered by an automated test: the DOM wiring in `order-details.js` and `order-builder.js` (syntax-checked only); M15's end-to-end tests exercise them.

## 2026-10-28: M15 end-to-end and architecture tests

**Found by the new tests:** the engine literal scan flagged `PromotionDefinition`'s default priority, a bare `100` (the schema's column default). It is now the named constant `PromotionDefinition.DefaultPriority` (committed separately as `fix:` after the test).

**Fixed AI output:**
- The first version of the scan's self-test put C# with three quotes in a row inside a `"""` raw string, which didn't compile; it now uses a four-quote delimiter.
- A first attempt to plant a forbidden project reference (to check the dependency test) used shell `sed`/`node` escaping that silently didn't change the file, so the test "passed" against an unchanged project. The plant was redone with an exact edit and confirmed in the file before trusting the result.

**Verified rather than trusted (each plant reverted, tree confirmed clean):**
- A planted `Rounding.Truncate(x, 7) + (long)Math.Round(1.5)` in `TipCalculator` fails all three rounding scans (Truncate outside TaxCalculator; `Math.Round` and the literals 7 and 1.5 in the engine; library rounding in `src/`).
- A planted `ProjectReference` from Web to Domain fails `Web_does_not_reference_Domain_or_EF`.
- The scanner's own test checks that numbers and `Math.Round` inside comments, regular, verbatim and character literals are ignored while real code is not, and that line numbers survive.
- The lost-response flow checks the database directly: one order after the lost response, still one after Try again.

## 2026-10-28: M16 README

**Fixed AI output (before commit):**
- The first draft attributed the open-question decisions to a "product owner" and wrote the AI note as if the author had personally run the mutation checks. Both were reworded to say who actually did what: the decisions are the author's (plan section 10), and the checks were run by the agent in the session.
- It cited "conflict tests driven by `promotion_incompatibility` rows" from the plan, but no test by that description could be found. The claim was replaced by tests that exist.
- The design write-up first came to about 1,060 words; it was cut to about 800 (with the SQL example) for the spec's "~1 page".

**Verified rather than trusted:**
- The README's data-only example (a PARTY size and a BIGTEN promo conflicting with both codes) was run as a seed script on a scratch database: Pepperoni on a Party priced at $1.44, 3 Party Pepperoni with BIGTEN totalled $51.46, SAVE5 was rejected with the conflict message, and below $50 BIGTEN explained why. The script and database were deleted afterwards.
- Every API example in the README was run verbatim on a fresh database: the quote returned 3148; the same submit twice returned 201 then a replay (`Idempotent-Replayed: true`, same id); the update returned 200 with ETag "2"; cancel returned 200 Cancelled twice; the conflict message matched; the description endpoint returned 200.
- The conditional `UPDATE` predicates quoted in the write-up were checked against `OrderService` (cancel: `status = NotStarted`; kitchen: `status = expected`).

## 2026-10-28: UQ29 UI fixes (extra toppings, promo list, Review button)

**Found by using the app, not by the tests:** three UI problems reported by the user. The page had no way to ask for 2x/3x of a topping (the server refused multipliers by design, A69); the promo chips worked but showed what each code did only in a hover tooltip, with a tiny × to remove it and no per-code discount line; and Review was a link styled as disabled, so it could be reached by keyboard, and stayed enabled for the debounce window after an edit (e.g. right after removing the last pizza).

**Diagnosed rather than guessed:** before changing anything, the builder was driven in headless Edge over the DevTools protocol (a small Node script, no packages): the promo chip did render and Remove did work, so the promo fix is visibility; the Review link had `pointer-events: none` but `tabIndex 0`.

**Fixed AI output:**
- A test regex used `"s"` in a regular C# string (an invalid escape) and was rewritten as a verbatim string.
- The browser-check script passed `
` inside a template literal into the page, where it became a real line break inside a regex literal; it was rewritten with `String.fromCharCode(10)`.

**Verified rather than trusted:** red first (11 Domain, 5 OrderManager, 3 Web and 2 JS tests failing for the right reason), then green; the stored row satisfies the schema's `placement_charge_cents = placement_unit_charge_cents * multiplier` CHECK; and in headless Edge: Review disabled and unfocusable on an empty page, immediately after an edit and after removing the last pizza; Large Pepperoni 3x = $15.85 (1488 + 97); ILOVEPIZZA listed with "saves $1.49", a Remove button and its own discount line, total $14.26; SAVE5 shows the conflict message; Remove restores $15.85; Medium Pepperoni 2x reviews and places at $8.65 and the order page says "Pepperoni 2x"; `/Checkout` with an empty cart lands on the builder; no console errors.

## 2026-10-28: "Your orders" shows status, date and summary

**Requested by the user:** the recent-orders page listed only order numbers. Each order is now read from the service (in parallel, at most 10) and shown with its status, when it was placed (restaurant time), its lines, discounts and total; an unreadable order stays listed with a short reason.

**Process note:** a running copy of the web app (not started by the agent) locked `bin/`, so the tests were built with `--artifacts-path` into a temp folder instead of stopping it. That made the architecture tests fail (they found the repository by walking up from their output folder); `Repository` now falls back to its own source file's location, which is a real robustness fix, not a test change to make things pass.

**Verified rather than trusted:** red first (2 tests), then all 2,082 .NET tests green; in headless Edge against the new build on spare ports with a scratch database: two orders placed, one started by the kitchen, and the page showed "Started | Placed Oct 28, 2026, 4:37 PM PDT | $13.34 | 1 x Large pizza with Pepperoni 2x | ILOVEPIZZA −$1.39" and "Not Started | … | $5.91 | 1 x Small pizza with Bacon".

## 2026-10-28: simulated kitchen page (UQ30)

**Requested by the user:** a restaurant page in the web app that shows all pending orders with a button to advance each. The order service had no way to list orders, so `GET /api/v1/kitchen/orders` (a stretch item in the plan) was built for it; the web app still reaches orders only through the service.

**Fixed AI output:** `git add -A` picked up the background agent's worktree folder (`.claude/worktrees/...`) and committed it as an embedded repository in the red commit. It was removed from the (unpushed) commit with `git rm --cached` and an amend, and `.claude/worktrees/` was added to `.gitignore`; the agent's files were not touched.

**Verified rather than trusted:** red first (13 tests across the API, client, page and end-to-end), then all 2,095 .NET tests green. Removing the active-status filter from the queue query fails the queue test (a delivered order has no next step), and was reverted. In headless Edge against the new build on spare ports with a scratch database: two orders placed, both listed oldest first with items and totals; pressing the first order's button three times took it to Delivered ("Order 0569f436 is now Delivered.") and off the list.
