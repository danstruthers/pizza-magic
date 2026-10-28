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
