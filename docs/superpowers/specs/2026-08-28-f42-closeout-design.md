# Sub-project: closing out F42 — the fixture re-record (2026-08-28)

> **As-of note:** line citations in this spec describe the tree as of `df36ac5`, the commit this
> spec's own work merged at (and the commit `feat/tier-f-small-batch` later started from), and
> are deliberately not maintained past it — that branch's `CombatMath.cs`/`CombatMathTests.cs`
> edits shifted lines past ~153/~111 respectively. For current locations, see `docs/BACKLOG.md`.

F42's *fix* shipped in `f0b4fcd`: `CombatMath.ReduceByArmor` now reduces post-threshold damage in
the reference's subtract-form, with five point tests. What did not ship is Task 2 of
`docs/superpowers/plans/2026-08-24-melee-dr-form.md` — the **golden-fixture measurement and
re-record**, and the backlog reconciliation that depends on its result.

This spec covers only that closeout. The arithmetic, its derivation and its acceptance rule live in
`docs/superpowers/specs/2026-08-24-melee-dr-form-design.md` and are not restated here.

## Starting state — verified 2026-08-28

- `HEAD` is `ea956b9` on `feat/melee-dr-form`; `f0b4fcd` carries the fix, `ea956b9` the corrected
  acceptance rule.
- The working tree holds **one uncommitted line**: a debug probe at `CombatMath.cs:99` (that line
  number describes the probe-instrumented tree; the probe was removed in Task 3 and the subtract-form
  `return` now sits at `:100`),
  `Console.Error.WriteLine($"F42PROBE d={afterThreshold} r={resistance} moved=…")`. The previous
  session added it and stopped there. It is the seed of this spec's measurement, not stray debris —
  but it must not reach a commit.
- `DISPLAY=:0` and `game-data/` are both present, so every suite runs here. `FALLOUT2_DIR` is unset;
  the scripts default to `./game-data`.
- Six suites, 279 committed fixtures: combat 18, encounter 188, quest 39, census 16, opening 13,
  endgame 5.

## The problem this spec exists to solve

The acceptance rule is stated per *damage computation*: each moves 0 or +1. **It cannot be checked
against the transcripts.** One moved damage value kills a critter a round early, which shifts the
RNG stream, and the rest of that transcript diverges wholesale — the diff shows dozens of unrelated
lines, none of them recognisably "+1". Reviewing re-records that way would rubber-stamp them.

So the proof is moved off the transcripts entirely, onto two independent legs.

## Leg 1 — the arithmetic, proved exhaustively and hermetically

A sixth test in `CombatMathTests.cs` sweeping the whole reachable domain, asserting for every
`(d, r)` both halves of the derivation:

- `subtractForm(d, r) - multiplyForm(d, r) ∈ {0, 1}` — never negative, never more than 1;
- the difference is `1` **iff** `d*r % 100 != 0` — the exact predicate, not just its bound.

`multiplyForm` is written inline in the test as the reference expression `d * (100 - r) / 100`; the
old form is gone from the source and must not be reintroduced there to support a test.

**Reaching the domain through the public API.** `ReduceByArmor` is `private`
(`CombatMath.cs:66`), so the sweep goes through `RollWeaponDamage` exactly as the five existing
tests do (`CombatMathTests.cs:220-273`):

- `d` — pass `minDamage == maxDamage == d` with a `CountingCombatRng(d)`, `meleeDmg: 0`, `dt: 0`,
  and the default `critMultiplier: 2` / divisor 1 / multiplier 1, so the `×2 … /2` wrapper is the
  identity and `afterThreshold == d` exactly.
- `r` — **drive it entirely through the `ammoDrModifier` parameter with `dr: 0`**, not through the
  DR stat. `CritterState` caps DR at 90, so the stat cannot express `91..100`, whereas
  `Math.Clamp(dr + ammoDrModifier, 0, 100)` (`CombatMath.cs:93`) reaches the full range. This also
  keeps one `CritterState` hoisted outside both loops instead of allocating per iteration.

`r` runs `0..100` and `d` runs `0..999`. That upper `d` bound is chosen to sit far above any
reachable single-hit damage while keeping the sweep ~101k trivial calls.

This leg needs no game data and is the only part of this work that stays in the repository.

## Leg 2 — the A/B probe, and the stop condition that actually matters

The probe is extended to report `d`, `r`, the old form's value, the new form's value, and whether
they differ — enough to attribute any individual move without archaeology afterwards.

It stays **uncommitted** and is deleted before the record. `Hexwaste.Formats` is a pure library and
does not acquire a permanent debug hook; this follows the established precedent in
`docs/research-notes/fork-fix-ledger-2026-08.md` (hunk 20), where a throwaway probe over all 186
head FRMs settled a verdict and was then removed.

It writes to **stderr**, and every suite diffs **stdout**, so the probe cannot contaminate a
fixture. That is what makes it safe to leave running during a full `check` pass.

### Per-fixture attribution

The probe's value depends on knowing *which fixture* each line came from, and the scripts capture
only stdout per scenario (`out=$(timeout 90 env …)`); stderr is discarded outright today — every
`run()` already redirects it with `2>/dev/null` (`combat-golden.sh:62`, `encounter-golden.sh:637`,
`quest-golden.sh:352`, `endgame-golden.sh:33`, `opening-golden.sh:56` and `:61`,
`census-sweep.sh:43`) — which is exactly why the probe's stderr output needs its own redirection
edit to survive.

Rather than reimplement six scripts' invocation logic in a throwaway runner — each has its own
argument shape, and a reimplementation that drifts would silently measure something other than what
`check` runs — add **one uncommitted line inside each script's `run()`** redirecting stderr to
`$PROBEDIR/$name.log`. All six share the same `SCENARIOS=(` / `FIX=` idiom, so the edit is the same
shape in each. Reverted along with the probe.

### The cross-tabulation

One `check` pass over all six suites with the probe live yields, together:

- **(a)** which fixtures now differ from their committed (pre-fix) baseline — the suites' own
  failure list;
- **(b)** how many damage computations actually moved inside each fixture.

The rule is **one-directional, and stating it correctly is the point**:

> `differs ⟹ moved > 0`

The converse does not hold and must not be enforced. A fixture may show `moved > 0` and still be
byte-identical — the moved value may never be printed, or may be absorbed downstream (a critter that
dies either way, a hit point count off-screen). Those are expected and are not findings.

**A fixture that differs while `moved == 0` moved for some other reason.** That is the stop
condition: it is exactly the class of error a re-record bakes in permanently, and it is invisible to
any amount of transcript reading. Work stops and the cause is investigated before anything is
recorded.

## Sequence

1. Leg 1's test, confirmed passing — and **mutation-verified**, which it genuinely is: against the
   pre-fix multiply-form `RollWeaponDamage` returns `multiplyForm(d, r)` itself, so the difference is
   uniformly 0 and the `iff d*r % 100 != 0 → difference is 1` half fails on every non-multiple case.
   Verify it by reverting `CombatMath.cs:100` locally, not by assuming.
2. Extend the probe; add the six `run()` redirections. All uncommitted.
3. Full `check` across the six suites; build the cross-tabulation.
4. Evaluate the rule. Any `differs && moved == 0` → **stop and report**, record nothing.
5. Revert the probe and the six redirections. Confirm `git diff` is clean of them before proceeding —
   a probe line surviving into a recorded fixture's run would be caught by the stdout/stderr split,
   but a probe line surviving into a *commit* would not.
6. `record` the affected suites; review the diffs; commit the re-recorded fixtures.
7. Backlog and the traced example.

## What carries the proof into the commit

> **Superseded by measurement (2026-08-28).** Zero fixtures moved, so there is no re-record commit
> and no fixture value to trace. `docs/BACKLOG.md`'s F42 entry carries the real outcome and cites the
> *hermetic* worked example instead. The clauses below describe the outcome this spec predicted, not
> the one that occurred; they are kept as the record of that prediction.

The commit body states the count of re-recorded fixtures and gives **at least one traced example**:
which attack, the defender's effective `r`, the value of `d`, and the arithmetic for both forms
showing why +1 is right. The probe supplies `d` and `r` directly; this is the reason it reports them
rather than just a moved flag.

Note when writing it that `r` is the **clamped effective** resistance — Finesse's +30 and F36's ammo
modifier fold in before the clamp — so a traced example against a DR-0 defender can be legitimate,
and citing the defender's raw DR stat as `r` would make the arithmetic look wrong.

## Docs

`docs/BACKLOG.md`: F42 → shipped, in its neighbours' format, with both commit SHAs, the suite
results, the re-recorded count and the traced example.

The `ReduceByArmor` / `RangedMath.RollDamage` unification is filed as **its own backlog entry**.
Both now perform identical arithmetic in identical shape (`CombatMath.cs:90-100` and `:164-181`),
which makes merging them tempting and is precisely why it needs a durable record instead of a
remark inside a shipped spec — F13 was lost for a release cycle that way.

## Out of scope

- The unification itself (filed, not done).
- **F43** — the gun path's `Math.Max(ammoDamageMultiplier, 1)` clamp (`CombatMath.cs:156`).
- **Fork-fix harvest round 2.** `community/main` advanced 88 commits (`19a2ad84 → c35e1f69`) while
  `alexbatalov/fallout2-ce` stayed at `e97087b`. Six candidates warrant ledger rows — PRs #692,
  #707, #690, #687, #710, #694 — and the ledger currently ends at #675. Its own session.

## Definition of done

> **Superseded in part.** The two fixture-dependent clauses below — "every re-recorded fixture's
> delta conforming to the `+1` rule" and "the traced example in the commit body" — were vacated by
> the measurement: zero fixtures moved. Everything else was met.

The exhaustive test green; the cross-tabulation built and its one-directional rule satisfied for
every fixture; probe and redirections provably gone from the tree; the affected fixtures re-recorded
with a reviewed diff; the traced example in the commit body; all six suites green afterwards;
`docs/BACKLOG.md` showing F42 shipped and the unification filed.

**Or:** a fixture differed with `moved == 0`, nothing was recorded, and the work stopped for
investigation.
