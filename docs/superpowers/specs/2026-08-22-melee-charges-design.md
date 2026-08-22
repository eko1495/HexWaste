# Sub-project: melee/unarmed weapons never spend charges (F34) — design spec (2026-08-22)

Close **F34**: any weapon with an ammo capacity spends one charge per attack in the reference;
Hexwaste spends one only when the weapon is a *gun*, so the five non-gun energy weapons in the game
have infinite charges.

F34 was filed while grounding F31 (2026-08-22, `736b87a`) — F31 turned out to be wrong on three
counts, and this is the real gap underneath it.

## Grounding — verified against `e97087b` on 2026-08-22

### The reference spends on capacity, never on weapon class

`_compute_attack`, after the ranged branch (`combat.cc:3897-3903`):

```c
if (attackType == ATTACK_TYPE_RANGED) {
    attack->ammoQuantity = v26;
    …
} else {
    if (ammoGetCapacity(attack->weapon) > 0) {
        attack->ammoQuantity = 1;
    }
}
```

`attack->ammoQuantity` starts at 0 (`:3476`), so a non-ranged attack with a capacity-less weapon
spends nothing and one with a capacity spends exactly 1.

The deduction happens in `_combat_anim_finished` (`combat.cc:5346-5356`) and is **attacker-agnostic**
— dude and NPC alike, gated only on capacity:

```c
Object* weapon = critterGetWeaponForHitMode(_main_ctd.attacker, _main_ctd.hitMode);
if (weapon != nullptr) {
    if (ammoGetCapacity(weapon) > 0) {
        int ammoQuantity = ammoGetQuantity(weapon);
        ammoSetQuantity(weapon, ammoQuantity - _main_ctd.ammoQuantity);
```

`ammoGetCapacity` (`item.cc:1358-1372`) reads `proto->item.data.weapon.ammoCapacity` — a proto field,
with no reference to the attack animation or weapon class anywhere in it.

### The refusal is capacity-gated too

`_combat_check_bad_shot` (`combat.cc:5678-5683`):

```c
if (ammoGetCapacity(weapon) > 0) {
    if (ammoGetQuantity(weapon) == 0) {
        return COMBAT_BAD_SHOT_NO_AMMO;
    }
}
```

This governs **both** sides: the dude through `_combat_attack_this` (`:5736`) and the AI through
`_combat_ai`'s attack loop (`combat_ai.cc:2731`). So in vanilla a drained cattle prod cannot swing at
all until it is reloaded. That is the other half of the model and it is why this spec touches two
sites rather than one — spending charges without gating on them would merely relocate the infinite
weapon rather than remove it.

### Which weapons this is actually about — a full proto census, not an estimate

Enumerated every item proto (`items.lst`, PIDs 1-600) with `AmmoCapacity > 0` and classified by
`IsGun`. **Exactly five are non-guns**, and all five draw Small Energy Cell (ammo PID 38):

| PID | Name | Anim | Capacity |
|-----|------|------|----------|
| 116 | Ripper | SWING | 30 |
| 160 | Cattle Prod | SWING | 20 |
| 235 | Power Fist | PUNCH | 25 |
| 399 | Super Cattle Prod | SWING | 20 |
| 407 | Mega Power Fist | PUNCH | 25 |

All five are ordinary obtainable weapons — this is reachable in normal play, not theoretical. The
census also settles F31's two hardcoded PIDs (399 / 407): both ship, both are in this set.

### The Hexwaste side

- **Three spend sites**, each gated on `isGun`: `CombatEngine.cs:381-382` (dude),
  `:3775` and `:3912` (NPC paths). `isGun` is `WeaponProtoStats.IsGun(ExtendedFlags)` —
  `(extendedFlags & 0xF) >= 6` (`ProtoDatabase.cs:91`), i.e. the attack animation, which is exactly
  the thing the reference never consults here.
- **The burst spend site** (`:953`) is guns-only by construction and is not in scope.
- **The NPC refusal is already correct**: `CheckBadShot` (`:2297`) gates on
  `AmmoCapacity > 0 && WeaponAmmo(...) <= 0` and its comment already says "gated on capacity, NOT on
  isGun … Hexwaste has no non-gun ammo-capacity weapon in practice". The census above shows there are
  five; that comment's parenthetical is stale and must be corrected as part of this work.
- **The dude refusal is wrong**: the whole empty-weapon block sits inside `if (isGun)` (`:317-333`).
- `WeaponAmmo` (`ViewerGame.CombatHost.cs:183-188`) resolves `-1` to capacity and is already
  weapon-class-agnostic, so a map-placed cattle prod reads as full without any change.
- `ReloadEquippedWeapon` (`:3929-3933`) is already capacity-gated, so the R key can refill these five
  today. The player is not left with a dead weapon.

## Scope

### 1. Spending is gated on capacity, not class

Introduce one predicate used by all three sites — `(weaponProto?.Weapon?.AmmoCapacity ?? 0) > 0` —
and replace `isGun` at `:382`, `:3775` and `:3912`, citing `combat.cc:3900-3902` and `:5347-5350`.
`isGun` stays in use at those sites for everything else it decides (range, knockback, anim,
transcript), so this is a narrowing of one condition, not a removal of the flag.

### 2. The dude's empty-weapon refusal is gated on capacity

The block at `:318-333` moves from `if (isGun)` to the capacity predicate, citing
`combat.cc:5678-5683`. The line-of-fire trace that follows it inside the same block **stays
`isGun`-gated** — the reference gates that on `RANGED || THROW || range > 1` (`:5685-5687`), which is
a different condition, and `CheckBadShot` already models it correctly on the NPC side. Do not
conflate the two while moving the brace.

**The auto-reload inside that block is kept as-is and extended with it.** Vanilla does not
auto-reload the dude at all — `_combat_attack_this` prints "Out of ammo." and returns (`:5738-5747`);
only the AI reloads (`combat_ai.cc:2732-2740`). Hexwaste's dude-side auto-reload predates the
CombatEngine extraction (`53c1df4`) and is a pre-existing deviation. Extending the gate without
extending the reload would make melee energy weapons the *only* weapons that do not auto-reload — an
arbitrary second divergence. **Record the auto-reload deviation itself as a new backlog entry**; do
not fix it here, where it would move fixtures for a reason unrelated to F34. Leaving it as prose in
this spec is how F13 was lost for a release cycle.

### 3. Explicitly out of scope

- **F31's ×2 cost for PIDs 399 / 407.** It sits on top of this and is a separate, now-unblocked item.
- **The transcript's `Nrnd` suffix**, which is `isGun`-gated (`:389`). Extending it would change
  transcript text for a display reason, not a fidelity one, and would move any fixture that later
  wields one of the five. Leave it; note the choice.
- **The burst path** (`:953`).
- **Ammo damage modifiers on non-gun weapons — a third divergence of the same shape, found while
  grounding this one.** `attackComputeDamage` applies the loaded ammo's DR modifier, damage
  multiplier and divisor unconditionally (`combat.cc:4579-4586`), reading them from
  `attack->weapon` with no attack-type gate; Hexwaste applies them only inside `if (isGun)`
  (`CombatEngine.cs:1109-1123`), so the melee branch passes no ammo mods at all. The five weapons
  above all load Small Energy Cells, so their damage is currently computed as if unloaded. This is
  damage-affecting rather than resource-affecting, carries its own fixture risk, and is a different
  claim from "charges are spent" — **file it as a new backlog entry** rather than widening this
  item. It is the natural successor to F34, as F31 is.
- **Any change to `IsGun` itself.** Its `>= 6` definition is correct for what it means.

## What carries the proof

Hermetic tests through `FakeCombatHost`, each **confirmed failing pre-change and for the right
reason**:

1. **A non-gun weapon with capacity spends a charge per attack** — the whole point of the item.
2. **A non-gun weapon with no capacity spends nothing**, and its `AmmoQuantity` is untouched. This is
   the guard against "decrement everything", which would drive knives and spears negative.
3. **Guns are unchanged** — the inertness guarantee for the 13 gun fixtures.
4. **A drained non-gun weapon cannot attack.** The dude-side refusal, which is what stops the fix
   from relocating the infinite weapon rather than removing it.
5. **An NPC path spends too** — at least one of `:3775` / `:3912`, since the reference's deduction
   site is attacker-agnostic and a test that only covers the dude would pass with two thirds of the
   fix missing.

## Fixture expectations — stated before the run

**Expected: byte-identical, all four suites.** The basis is direct, not hopeful: the combat fixtures
log a weapon name only when one is equipped, and across all 18 the only weapon attacks recorded are
`Combat Shotgun`, `10mm SMG` (×2) — three lines, all guns. None of the five weapons appears.

So a fixture moving here is **a stop condition, not a re-record.** If one moves, the likely cause is
that a critter wields one of the five without the transcript naming it — investigate and report
before touching a baseline. This is the inverse of the usual re-record contract and it is deliberate:
the item's value is a live-play fix, and the fixtures' silence is the evidence that it is contained.

## Docs

`docs/BACKLOG.md`: F34 → shipped with its SHA, naming the five weapons. **Unblock F31** — its
"blocked behind F34" note becomes actionable, with the census confirming both PIDs ship. Correct the
stale "Hexwaste has no non-gun ammo-capacity weapon in practice" claim wherever it appears (it is in
`CombatEngine.cs:2295` as well as the ledger). Add the new entry for the dude-side auto-reload
deviation, citing `combat.cc:5738-5747` against `CombatEngine.cs:320-330`.

## Definition of done

Capacity-gated spending at all three sites and capacity-gated refusal on the dude side, each with its
citation; five hermetic tests green and confirmed failing pre-change; all four suites byte-identical;
`docs/BACKLOG.md` reconciled with F34 shipped, F31 unblocked, the auto-reload entry filed, and the
stale in-code comment corrected.

**Or:** a fixture moved, and the work stopped for investigation rather than re-recording.
