# Re-record tier, sub-project 3: crit-failure damage and damage-proc fidelity — design spec (2026-08-15)

Close **F11, F12 and F13** from `docs/BACKLOG.md` — three small divergences that all live in the
crit-failure / accidental-hit neighbourhood of `CombatEngine`. They are grouped into one branch
because they share fixtures: recording each separately would re-record the same transcripts three
times and make the deltas harder, not easier, to attribute.

**F14 is explicitly not in scope.** It is documentation of an ordering divergence that the shipped
`_cf_table` makes unreachable (`DAM_CRIP_RANDOM` appears exactly once, paired with nothing), and the
backlog entry already records it. There is nothing to build.

## Grounding — verified against `e97087b` on 2026-08-15, not taken from the backlog prose

Every claim below was re-read in the reference before this spec was written. The 2026-08-11 batch
shipped nine spec errors that reviewers caught by doing exactly this; the checks are recorded here so
the implementer does not have to repeat them, and so a reviewer can falsify them cheaply.

### F11 — crit-failure self-damage is half vanilla

`attackComputeCriticalFailure` (`combat.cc:4228-4232`) resolves self-damage as:

```c
if ((attack->attackerFlags & DAM_HIT_SELF) != 0) {
    int ammoQuantity = attackType == ATTACK_TYPE_RANGED ? attack->ammoQuantity : 1;
    attackComputeDamage(attack, ammoQuantity, 2);
} else if ((attack->attackerFlags & DAM_EXPLODE) != 0) {
    attackComputeDamage(attack, 1, 2);
}
```

`DAM_RANDOM_HIT` takes the same shape (`combat.cc:4260`): `attackComputeDamage(attack, ammoQuantity, 2)`.

Inside `attackComputeDamage` the third argument is `bonusDamageMultiplier`, which reaches
`damageMultiplier = bonusDamageMultiplier * weaponGetAmmoDamageMultiplier(...)` (`:4586`) and is then
undone by `damage /= 2` (`:4601`). **The pair is net ×1: vanilla applies the full rolled damage.**

Hexwaste's `CritFailDamage` (`CombatEngine.cs:1233`) passes `critMultiplier: 1` into
`CombatMath.RollWeaponDamage` / `RollDamage`, whose bodies are `raw * critMultiplier / 2`
(`CombatMath.cs:36,46`) — so the rolled figure is **halved** before DT/DR. A 5–12 weapon self-hit
that should cost 12 costs 6.

The fix is the argument `1` → `2`, at the one call site that feeds both `DAM_HIT_SELF` and
`DAM_RANDOM_HIT`. This is pre-existing since `f77e37f` and unrelated to the fork harvest, which only
pinned it in a test.

### F12 — the collateral victim of a missed shot must NOT run `damage_p_proc`

`_check_ranged_miss` (`combat.cc`) reassigns `attack->defender = critter` — the bystander the shot
struck — while `attack->oops` still holds the *intended* target from attack init (`:3485`
`attack->oops = defender`). The defender's damage call is:

```c
_damage_object(defender, attack->defenderDamage, animated, attack->defender != attack->oops, attacker);
```

(`:4723`), so for a collateral hit the fourth argument is **true**. `_damage_object` gates the proc as
`if (!a4) { … scriptExecProc(a1->sid, SCRIPT_PROC_DAMAGE); }` (`:4848`) — therefore **no damage proc
runs**. The fork's PR #493 inverts a *different* call site's polarity and does not change this
branch's outcome.

Hexwaste's `ApplyAccidentalHit` (`CombatEngine.cs:729`) calls `RunDamageProc(acc.Victim, attacker, …)`
unconditionally for any scripted non-dude bystander. The fix is to stop calling it there.

### F13 — the `DAM_EXPLODE` crit-failure branch runs no `damage_p_proc` at all

The `#493` port wired the party-gated self-damage proc into `ApplyCritFailDamage`
(`CombatEngine.cs:1250-1266`), which the `DAM_HIT_SELF` branch reaches. The sibling `DAM_EXPLODE`
branch (`:1193`) routes to `Explode(...)` instead and never reaches `ApplyCritFailDamage`, so a
critter blown up by its own fumbling weapon runs no proc — where the reference's
`attackComputeDamage(attack, 1, 2)` self-damage feeds the same `_apply_damage` path that `#493`
corrects. The fix is to give the explode branch the same party-gated `RunDamageProc(self, self, …)`
tail, on the damage the blast actually dealt to the attacker.

## Scope — three changes, three commits

Each is committed separately with a `combat-golden.sh check` between them, so any fixture movement
attributes to a single item. Sharing a branch is an efficiency; sharing a commit would destroy the
attribution this tier exists to protect.

### Commit 1 — F11: `critMultiplier: 2`

`CritFailDamage` passes `2` to both `CombatMath` overloads. The doc comment stops describing the
halving and cites `attackComputeDamage`'s ×2/÷2 pair. `CombatEngineTests.HitSelfFumbleStillRollsWeaponDamage`
currently asserts `30 - 6` with a comment stating the deviation; that assertion becomes the full
figure and the comment goes.

**Not in scope on this commit, recorded as a carried divergence:** the reference rolls a ranged
`DAM_HIT_SELF` `attack->ammoQuantity` times (a burst fumble self-hits once per round); Hexwaste rolls
once. Changing the roll *count* changes the RNG draw count, which is a materially larger blast radius
than changing a multiplier, and it belongs in its own cycle. Note it in `docs/BACKLOG.md` rather than
folding it in silently.

### Commit 2 — F12: drop the collateral `damage_p_proc`

Remove the `RunDamageProc` call from `ApplyAccidentalHit`, with a comment porting the
`defender != oops` reasoning above. `OnTargetHit` / `RunOnHitCombatProc` / the kill path are
**unchanged** — only the damage proc is suppressed, because only the damage proc is what `a4` gates.

### Commit 3 — F13: the explode branch's self-damage proc

`Explode` must report the damage it dealt to the attacker so the branch can run the same
party-gated proc `ApplyCritFailDamage` runs. Keep the gate identical — non-dude, `Sid != -1`, not a
party member — since it is the same `_damage_object` predicate.

If the damage dealt to the attacker cannot be recovered from `Explode` without restructuring its
victim loop, **say so and stop** rather than inventing a second damage figure: a proc fired with a
number that is not the number applied is worse than no proc.

## What carries the proof

Same contract as sub-projects 1 and 2. **The fixture is a record of a consequence, never the
evidence.** Hermetic tests through `FakeCombatHost` are the proof, and every one must be confirmed to
**fail against the pre-change code** — a test green on both sides proves nothing.

1. **F11** — a `DAM_HIT_SELF` fumble with a known weapon range and a seeded RNG applies the full
   rolled figure, not half. Assert the exact number, not an inequality.
2. **F11 boundary** — the `DAM_RANDOM_HIT` victim takes the same full figure (same call site).
3. **F11 non-regression** — `DAM_HURT_SELF`'s flat 1–5 is untouched: it never enters `CritFailDamage`.
4. **F12** — a scripted bystander struck by a missed shot runs **no** damage proc, while still taking
   the HP loss and still running `OnTargetHit` / the on-hit proc.
5. **F12 boundary** — the ordinary *intended* defender still runs its damage proc, so the suppression
   did not leak to the normal path.
6. **F13** — an unaffiliated scripted critter that fumbles into `DAM_EXPLODE` runs its damage proc
   with the blast damage; a party member and the dude do not.

## Fixture expectations — stated in advance, so a surprise is detectable

- **F11 will move fixtures.** `arcaves-crit-fail-day6` is the live crit-failure fixture. Damage
  numbers change, and a changed damage number can change a kill, which changes rounds. This is the
  deliberate, diff-reviewed re-record, on the P120 precedent.
- **F12 and F13 are expected to move nothing** — both only add or remove a script proc, and the
  fixture critters' scripts would have to define `damage_p_proc` to differ. If either moves a
  fixture, **stop and investigate**: it means a proc with side effects is running where the analysis
  says none should.
- Any fixture outside the combat suite moving is a stop condition for all three.

Record order, as in sub-project 1: check first and enumerate the failures, confirm they are the
predicted set, only then record, then confirm exactly the expected files changed under
`git status --short`.

## The justification

The commit body for F11 must trace the re-recorded fixture's changed values to the doubled damage —
which hit changed by how much, and whether that changed a kill or a round count. **If the trace
cannot be constructed from the transcript, do not re-record**; the item returns to deferred. An
unexplained delta accepted because "the port looks faithful" is how a bug gets laundered into the
baseline.

## Docs

`docs/BACKLOG.md`: F11, F12, F13 move to the shipped list with their commit SHAs, noting which
fixtures were deliberately re-recorded. Add the ranged `ammoQuantity` self-hit roll count as a new
carried divergence. F14 stays as-is — still an unreachable, documented ordering divergence.

## Definition of done

Three commits; every hermetic test green and each confirmed failing pre-change; the F11 fixture
delta traced in its commit body; F12 and F13 confirmed to have moved nothing; all four suites green
afterwards; `docs/BACKLOG.md` reconciled.

**Or:** something moved that this spec did not predict, and the work stopped for investigation — a
legitimate outcome, and the reason the expectations above are written down before the run.
