# Sub-project: the melee branch never consults the loaded ammo (F36) — design spec (2026-08-23)

Close **F36**: the reference reads the loaded ammo's damage-resistance modifier, damage multiplier,
damage divisor and armor-class modifier with **no attack-type gate**; Hexwaste reads them only on the
gun path, so the melee/unarmed branch computes as if the weapon were unloaded.

## Grounding — verified against `e97087b` on 2026-08-23

Four reads, all ungated:

- `attackComputeDamage` (`combat.cc:4579-4587`): `damageResistance += weaponGetAmmoDamageResistanceModifier(attack->weapon)`,
  clamped to `[0, 100]`, then `damageMultiplier = bonusDamageMultiplier * weaponGetAmmoDamageMultiplier(...)`
  and `damageDivisor = weaponGetAmmoDamageDivisor(...)`.
- `attackDetermineToHit` (`combat.cc:4428-4432`): `armorClass += weaponGetAmmoArmorClassModifier(weapon)`,
  clamped at `>= 0`.

The accessors (`item.cc:2037-2085`) resolve the ammo proto through the weapon's `ammoTypePid` and
return the neutral value when it is `-1` or unloadable — `0` for the DR modifier, `1` for the
multiplier and divisor. So on the reference's own melee path they are usually a no-op *by data*, not
by a gate.

## The measurement that reframes this item

F36's entry says these five weapons' "damage is still computed as if unloaded", implying a damage
bug. **There is no damage bug.** A census of every item proto shows:

- **Exactly five non-gun weapons have a real `ammoTypePid` at all** — the F34 five (Ripper 116,
  Cattle Prod 160, Power Fist 235, Super Cattle Prod 399, Mega Power Fist 407). No throwing weapon,
  no other melee weapon references ammo.
- All five load **Small Energy Cell (38)**, whose modifiers are **AC 0, DR 0, multiplier 1,
  divisor 1** — every one the neutral value.

So "computed as if unloaded" is *numerically identical* to "computed as loaded" for every weapon that
ships. **This item is a structural fidelity gap with provably zero behavioural effect on shipped
data**, not the damage-affecting change the entry advertises, and its "re-record tier" label is wrong.

That is a reason to do it carefully and cheaply, not a reason to skip it: the reference has no gate,
the 17 ammo protos that *do* carry modifiers prove the mechanism is real, and a future weapon or a
corrected proto would silently diverge. It is the same class as the `CheckBadShot` capacity gate,
which shipped as an inert fidelity fix.

## Scope

### In — the melee branch consults the loaded ammo, exactly as the gun branch does

1. **Damage.** `CombatMath.RollDamage` (unarmed) and `RollWeaponDamage` (melee weapon) gain the three
   ammo parameters, defaulting to the neutral values so every existing call site is unchanged by
   construction. Apply them in the reference's order (`combat.cc:4589-4600`): multiply by
   `critMultiplier * ammoMultiplier`, divide by the divisor **only when non-zero**, then the engine's
   `/ 2`, then the difficulty modifier, then DT, then DR. The DR modifier is added to the defender's
   DR before the existing `Math.Clamp(dr, 0, 100)`, which already matches the reference's clamp.
2. **To-hit.** The melee to-hit expression (`CombatMath.cs:23`) subtracts `target.ArmorClass + extraAc`;
   it gains the ammo AC modifier with the reference's own clamp shape — `Math.Max(ac + ammoAc, 0)`,
   the same form the ranged path already uses (`CombatMath.cs:140`).
3. The `CombatEngine` melee branch passes `_host.LoadedAmmo(...)`'s values, the way the gun branch
   does immediately above it.

### The RNG constraint — the thing that could actually break

Both melee damage helpers take **exactly one** `rng.Next` draw, before any of this arithmetic. The
change must not add, remove or reorder a draw. If it does, every combat fixture moves, and that would
be a regression rather than a re-record. Arithmetic only, after the draw.

### Out

- **Any change to the gun branch**, which already does all four correctly.
- **`ReduceByArmor`'s existing clamp semantics** beyond adding the ammo DR modifier to its input.
- **F41's `-1` sentinel.** Unrelated; separately filed.

## What carries the proof

Hermetic tests through `CombatMath` and `FakeCombatHost`. Because shipped data makes this inert, the
tests must use **synthetic ammo values** — that is the only way to prove the wiring exists at all:

1. **A melee weapon with a damage multiplier deals more damage**; with a divisor, less. Assert exact
   values against the reference's operation order, not just "greater than".
2. **A melee weapon with a DR modifier is reduced differently**, including the `[0, 100]` clamp at
   both ends.
3. **A divisor of 0 does not divide** (the reference's `if (damageDivisor != 0)` guard) and does not
   throw.
4. **The ammo AC modifier shifts melee to-hit**, with the `>= 0` clamp.
5. **Neutral values change nothing** — the guarantee that every existing call site and all five
   shipped weapons are unaffected.

Each must be confirmed failing before the change, for the right reason.

## Fixture expectations

**Byte-identical, all suites** — and here that is a strong claim rather than a hope, because the
census proves no shipped weapon carries a non-neutral modifier on this path. **A moving fixture means
the RNG draw order changed or the arithmetic is wrong; it is a stop condition, not a re-record.**

## Docs

`docs/BACKLOG.md`: F36 → shipped, **with its framing corrected** — record that it was filed as
damage-affecting and re-record tier, and that the census showed it is provably inert on shipped data
(five weapons, all Small Energy Cell, all modifiers neutral). State that the value is structural. Note
that 17 ammo protos do carry modifiers, so the mechanism is real for guns and would be for any future
non-gun weapon.

## Definition of done

All four reads ungated on the melee path with their citations; five hermetic tests green and
mutation-verified; the single RNG draw per helper unchanged; suites byte-identical; F36 shipped with
the corrected framing.
