# F33 — the shoot-blocking predicate: a two-stage design collapsed into one

**Date:** 2026-08-29
**Supersedes the diagnosis in `docs/BACKLOG.md`'s F33 entry**, which framed this as one wrong
operator. It is three divergences, and the operator is the least of them.

## What the entry got right, and where it went wrong

Right: `ShootBlockerAt` (`src/Hexwaste.Viewer/ViewerGame.CombatHost.cs:226`) and
`_obj_shoot_blocking_at` (`reference/fallout2-ce/src/object.cc:2440`) disagree on the flag test, and
the disagreement is not inert — a survey over all 155 maps found 48% of 209,413 solid objects
classified differently.

Wrong: it concluded that adopting the reference's reading must be incorrect, because doing so broke
`denbus2-burst-collateral` and "if `SHOOT_THRU`-only objects truly blocked shots, ranged combat would
be frequently impossible in vanilla". That inference assumed the predicate's answer *is* the
engine's answer. **It is not.**

## The actual shape: one coarse predicate, five caller policies

`_obj_shoot_blocking_at` is a *coarse* "is there something here" query. Every caller then decides for
itself what the returned object means. The clearest instance is `combat.cc:3584-3586`, which asks the
predicate and then immediately re-tests the flag the predicate deliberately let through:

```c
_make_straight_path_func(attack->attacker, curr, to, nullptr, &critter, 32, _obj_shoot_blocking_at);
if (critter != nullptr) {
    if ((critter->flags & OBJECT_SHOOT_THRU) == 0) {
```

The five combat-side callers filter differently, and the differences are the design:

Each policy is a combination of at most three independent terms — *does a `SHOOT_THRU` object
count*, *does a living critter count*, *does the target count*. Verified against each call site:

| caller | excludes `SHOOT_THRU` | excludes critters | excludes the target |
|---|---|---|---|
| `combat.cc:3584` — the shot-blocked roll | yes | yes | no |
| `combat.cc:3641` — the burst / continuous walk | **no** | yes | no |
| `combat.cc:3956` — the missed-shot collateral target | yes | **no** | (via `excludeObj`) |
| `combat.cc:5906` — `combat_is_shot_blocked`'s penalty | **no** | yes | yes |
| `combat_ai.cc:2585` — the friendly-fire check | no | no | no |

Two of these were got wrong in an earlier draft and corrected only after reading the call sites
directly: `3641` and `5906` do **not** treat a living critter as a hard obstruction — it is a hit
candidate and the walk continues — and `3956` applies **no type test at all**, so a critter does
count there. Deriving a policy from the caller's prose summary rather than its code is exactly how
this entry went wrong the first time.

Hexwaste has the same *shape* — `LineOfFire.Trace(from, to, blockerFunc)`
(`src/Hexwaste.Formats/Combat/LineOfFire.cs:20`) with ten consumers — but **every consumer passes the
same `ShootBlockerAt`**, and that one predicate has the caller-side filters baked into it. The
two-stage design is collapsed into one stage, so no consumer can have its own policy.

## Why the previous attempt broke, and why its conclusion does not follow

Swapping `&&` for `||` adopted the reference's *predicate* while keeping our collapsed *consumers* —
so every consumer inherited a policy that belongs to only some of them. The fixture that broke,
`denbus2-burst-collateral`, exercises the burst walk, which is **caller `combat.cc:3641` — the one
with no `SHOOT_THRU` filter**. Under the reference, a `SHOOT_THRU` scenery on that line really does
end the walk.

So "the burst stopped, therefore the change was wrong" does not follow. It is equally consistent with
the fixture encoding *our* behaviour as correct — the same mechanism that recorded the melee
damage-resistance bug (F42) into the transcript baseline and kept it there for months. **This spec
does not assume either way. Task 1 measures it.**

## Composing the two stages, for the one caller we can check on paper

For `combat.cc:3584`, predicate and filter compose to:

```
!HIDDEN && (NO_BLOCK == 0 || SHOOT_THRU == 0) && SHOOT_THRU == 0
  ==  !HIDDEN && SHOOT_THRU == 0
```

Ours is `!HIDDEN && NO_BLOCK == 0 && SHOOT_THRU == 0`. The difference is the extra `NO_BLOCK == 0`
term, so the population that is classified differently **for this caller** is the objects carrying
`NO_BLOCK` and not `SHOOT_THRU`: **5,368 objects, not 95,463** — two orders of magnitude smaller than
the entry's headline, and in the opposite direction (we block *less* where the reference blocks).

That is the entry's "48%" properly attributed: it is a property of the coarse predicate, not of what
any consumer actually sees.

## Two further divergences the entry never recorded

**The exclusion set.** `_make_straight_path_func` (`animation.cc:1951`) invokes
`callback(obj, from, obj->elevation)`, so the predicate's `excludeObj` is the walker's first
argument — and **it differs per call site**, which an earlier draft of this spec got wrong by
asserting it is always the attacker:

| call site | `excludeObj` |
|---|---|
| `combat_ai.cc:2585` | `attacker` |
| `combat.cc:3584` | `attack->attacker` |
| `combat.cc:3641` | `attack->attacker` |
| `combat.cc:3956` | `accidentalTarget`, initialised to `attack->defender` — the **defender** |
| `combat.cc:5906` | `sourceObj` |

So the exclusion is a **caller-supplied parameter of the coarse query**, not a property of the
predicate. `ShootBlockerAt` hardcodes `o != shooter && o != target`, which is neither: it excludes
two objects where the reference excludes one, and it cannot express `3956`'s defender-exclusion at
all.

**The missing multihex phase.** After finding nothing on the tile itself, `_obj_shoot_blocking_at`
walks the six adjacent tiles looking for `OBJECT_MULTIHEX` objects and blocks on those — with a
*different*, stricter flag test (`(flags & OBJECT_NO_BLOCK) == 0` alone, no `SHOOT_THRU`
disjunction). `ShootBlockerAt` has no multihex handling whatsoever. Roughly 25 lines of the reference
function were never ported. This too makes us block less.

## One hypothesis the entry offered, now closed

The entry asked whether the flag word we survey is the flag word the engine uses. It is:
`objectRead` (`object.cc:412`) reads `obj->flags` verbatim from the map file with a single
`fileReadInt32`, and the post-read fixup in `objectLoadAllInternal` only clears `OBJECT_NO_REMOVE` on
certain critters. **No proto flags are merged into a loaded map object's flag word.**

Stated precisely, because the sweeping version would be wrong: there *is* a runtime `flags |=` during
map load (`map.cc:961`, setting `LIGHT_THRU | NO_SAVE | HIDDEN`), but it applies to a synthetic
misc-12 object created to carry the map script — not to any object read from the map, and hidden in
any case. So **the survey measured the right field**, and the "our parser reads a different flag word
than the engine uses" hypothesis is dead without needing an experiment.

---

## Task 1 — the decisive measurement, before any behaviour changes

Everything above is derived from reading. One fact is not, and it decides the rest: **what actually
sits on the line in `denbus2-burst-collateral`.**

Add a harness probe that, for a shooter and a target, walks the line and reports every candidate
object per tile with its type, pid, and flags in hex — and, for each of the five reference caller
policies, whether that policy would treat it as an obstruction. The project has dozens of such
probes; this one is worth keeping.

**Pre-register what each outcome means, so the result cannot be rationalised afterwards:**

- **The blockers carry `SHOOT_THRU` and are scenery.** Then the reference genuinely ends the burst
  walk there, our fixture encodes our own behaviour, and the fixture must be re-recorded — deliberately
  and with the diff reviewed, on the F42 precedent.
- **The blockers carry `NO_BLOCK` only.** Then the 5,368-object population is the live one, our
  predicate blocks too little, and the fix is small and in the opposite direction from the entry's
  framing.
- **The blocker is the target or the shooter.** Then the exclusion divergence dominates and must be
  fixed first, before anything is concluded about flags.
- **No object is found under any policy.** Then the burst stopped for a reason outside this predicate
  entirely, and F33 as scoped is not the cause of what the previous attempt observed.

## Then: port the two stages

The shape to port is one coarse predicate plus per-consumer policies:

1. `ShootBlockerAt` becomes faithful to `_obj_shoot_blocking_at`, which means three specific things,
   spelled out because "be faithful" is where a port quietly keeps one of its old terms:
   - the **tile phase** gates on `!HIDDEN && (NO_BLOCK == 0 || SHOOT_THRU == 0)`, then the type test
     (live critter, scenery, or wall) that we already have correct;
   - the exclusion becomes a **caller-supplied parameter** — one object, whatever that consumer's
     reference counterpart passes (usually the attacker, but the defender at `combat.cc:3956`);
   - the **multihex phase** runs when the tile phase finds nothing: the six adjacent tiles are
     scanned for `OBJECT_MULTIHEX` objects under a *stricter* gate — `!HIDDEN && NO_BLOCK == 0`, with
     **no `SHOOT_THRU` disjunction** — plus the same exclusion and type test.
2. Each of the ten `LineOfFire.Trace` consumers gets the policy its reference counterpart has. Five
   have a counterpart in the table above; the others (rendering's outline check, explosion
   line-of-sight, `DangerSource` reachability, approach) must have their counterpart identified, and
   where none exists, the policy chosen must be stated and justified rather than inherited by default.

**Do not do step 1 without step 2.** Step 1 alone is exactly the change that was tried and reverted.

## Blast radius and the re-record policy

The predicate reaches to-hit line-of-fire penalties, missed-shot overshoot, explosion line-of-sight,
`DangerSource` reachability, enemy and ally approach, and rendering. Fixtures **will** move.

Every moved fixture must be diffed and explained before it is re-recorded — not re-recorded because
the suite went red. A fixture that moves for a reason the change does not explain is a stop condition.
The golden suites now run in under two minutes, which is what makes an
iterate-measure-explain loop practical here; it was 24 minutes when this entry was written, and that
cost is part of why the previous attempt stopped at "reverted".

## Non-goals

- **F25 stays blocked** until this lands. It asks the worldmap start-point probe to adopt
  shoot-blocking semantics; adopting them while they are unresolved is what this entry has always
  warned against.
- No change to `_obj_blocking_at`'s movement-blocking sibling, which is a different predicate with
  different callers.
- No attempt to port the SFALL line-of-fire hit-chance extension visible at `combat.cc:5906`; the
  vanilla behaviour at pin `e97087b` is what this targets.
