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
