# Re-record tier, sub-project 2: explosion ring-spiral ordering — design spec (2026-08-13)

Port `_compute_explosion_on_extras`'s ring-spiral victim ordering (`combat.cc:3987`) and expose it
in a damage-free counting mode, which unblocks the explosive `×(extras+1)` best-weapon factor
deferred from the 2026-08-11 batch.

## Scope reframing — this is probably NOT a re-record item

The tier listed this as fixture-moving. Grounding says otherwise, for two reasons:

1. **The reference spiral never examines the centre tile.** It starts at `radius = 1` (the NE
   neighbour of the blast tile, `combat.cc:4034`) and walks outward, so a critter standing *on* the
   blast tile never enters `extras` — in the reference that critter is the primary defender, damaged
   by the main attack path. Hexwaste's `CombatEngine.Explode` has no separate primary path and hits
   the centre critter inside the same loop.
2. **Both explosion fixtures are centred on their only-or-first victim.** `arcaves-explode` has one
   victim, the Radscorpion at tile `20529`, which is the blast centre. `arcaves-throw-grenade` has
   two — the same Radscorpion at the centre, plus the dude (`Hero Male@20530`) as the *only*
   non-centre victim. Ordering is unobservable with a single non-centre victim.

A naive strict-spiral port would therefore delete `arcaves-explode`'s only victim, which is wrong.
This design keeps the centre critter as the primary victim and applies spiral ordering only to the
rest — under which **no committed fixture is expected to move**.

So the divergence being fixed is real but currently unobserved by any fixture: it needs **two or
more non-centre victims**. The proof is therefore entirely hermetic tests, and the golden suites
serve only as a no-regression check. **Any fixture movement is a stop-and-investigate**, not an
expected outcome — the inverse of sub-project 1's contract.

## Scope

### 1. `ExplosionSpiral` — a new pure unit

New `src/Hexwaste.Formats/Combat/ExplosionSpiral.cs`. Given a centre tile and a maximum radius, it
enumerates tiles in the reference's exact order (`combat.cc:4022-4045`):

- `radius++` opens each ring, whose first tile is the NE neighbour of the previous ring's first tile
  (`tileGetTileInDirection(ringFirstTile, ROTATION_NE, 1)`), with `rotation = ROTATION_SE` and
  `ringTileIdx = 0`.
- Each subsequent step advances one tile in the current `rotation`, increments `ringTileIdx`, and
  rotates one step further whenever `ringTileIdx % radius == 0` (the reference's "the larger the
  radius, the slower we rotate", `:4026`), wrapping `ROTATION_COUNT` back to `ROTATION_NE`.
- A ring closes when the advanced tile equals that ring's first tile; the walk then opens the next
  ring, and stops once `radius` exceeds the maximum.

Pure tile arithmetic — no host, no RNG, no damage — so it is directly unit-testable against
hand-computed ring sequences.

### 2. `CombatEngine.Explode` consumes it

The centre critter, if any, is damaged first exactly as today. Remaining victims are then taken in
**spiral order** instead of the current `OrderBy(distance)`. Everything else is unchanged: the
line-of-sight check, the `raw − DT − DR%` damage formula, knockback, the difficulty modifier, and
the 6-target cap (`explosionGetMaxTargets`, `item.cc:3574`).

This ordering change is the entire behavioural delta of the sub-project.

### 3. Counting mode unblocks the explosive best-weapon factor

`_ai_best_weapon` calls `_compute_explosion_on_extras(..., noDamage = 1)` purely to read
`extrasLength` (`combat_ai.cc:1861`). The same enumerator, run without applying damage, yields that
count, so `AiBestWeapon.AvgDamage` gains the explosive `×(extrasLength + 1)` factor it has been
missing since the previous batch deferred it for this dependency.

## Documented divergences — recorded, not silently carried

- **Attacker backwash.** The reference's `DAM_BACKWASH` branch (`combat.cc:4056-4060`) clears
  `DAM_HIT`, recomputes damage down a separate path and flags the attacker. Not ported, by decision.
  Note explicitly that `arcaves-throw-grenade` **exercises this branch** — the dude is caught in his
  own grenade blast and takes ordinary blast damage where the reference would compute backwash.
- **Centre critter as primary.** The reference damages the centre critter through the main attack
  path; Hexwaste's `Explode` folds it into the same loop, hit first. This substitution is what keeps
  `arcaves-explode` correct.
- **Victim discovery.** The reference finds victims per-tile via `_obj_blocking_at`; Hexwaste
  iterates its critter list. These differ for multihex critters occupying several tiles.
- **Radius accessors.** `weaponGetGrenadeExplosionRadius` vs `weaponGetRocketExplosionRadius`
  (`combat.cc:4035-4039`) bound the spiral differently per weapon class; Hexwaste passes a single
  radius.
- **Damage computation.** The reference calls `attackComputeDamage`; Hexwaste keeps its simplified
  explosion formula. Pre-existing, unchanged here.

## Verification

Hermetic tests carry the entire proof, since no fixture exercises the divergence:

1. **Ring order** for radii 1–3, each asserted against a hand-computed tile sequence derived from the
   reference's rules — not from the implementation's own output.
2. **Rotation cadence** — that radius 2 rotates every 2 steps and radius 3 every 3, per
   `ringTileIdx % radius`.
3. **Ring closure and radius bound** — each ring ends on return to its first tile, and enumeration
   stops beyond the maximum radius.
4. **Centre-first rule** — a critter on the blast tile is damaged first and is not enumerated by the
   spiral.
5. **Multi-victim ordering** — the test that would have caught this divergence: two or more
   non-centre victims arranged so spiral order differs from distance order, asserting spiral order.
   This must fail against the pre-change distance sort.
6. **Counting mode** — the victim count feeding the best-weapon factor, and `AvgDamage`'s
   `×(extras+1)` multiplication.

Then all four suites (`dotnet test`, `combat-golden.sh`, `quest-golden.sh`, `encounter-golden.sh`)
with **no fixture expected to move**. If one does, stop and report: it means the victim set or
ordering changed somewhere the analysis above did not predict, and that must be understood before
anything is re-recorded.

## Definition of done

`ExplosionSpiral` landed and unit-tested against hand-computed orders; `Explode` ordering ported with
the centre-first rule intact; the explosive `×(extras+1)` factor wired via counting mode; the five
documented divergences recorded in `docs/BACKLOG.md`; all four suites green with no fixture moved.

**Or:** a fixture moved and the work stopped for investigation — a legitimate outcome, since it would
mean this analysis was wrong in a way worth understanding before proceeding.
