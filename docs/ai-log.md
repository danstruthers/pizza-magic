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
