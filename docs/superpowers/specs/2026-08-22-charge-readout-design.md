# Sub-project: the charge readout (F38) — design spec (2026-08-22)

Close **F38**: F34 made the five non-gun capacity weapons genuinely drain and genuinely refuse to
attack when empty, but both on-screen readouts are gated on weapon *class*, so the player sees no
charge count and gets no explanation when the weapon stops working.

## Grounding — verified against `e97087b` on 2026-08-22

F38 was filed from a review finding without being grounded first — the same mistake that produced
F37's wrong diagnosis. Grounding it first changed two of its three claims. **Treat the entry as a
lead, not a specification.**

### The two gates are different, and that difference is load-bearing

The HUD bar and the examine text do **not** use the same condition in the reference:

- `_intface_update_ammo_lights` (`interface.cc:1346-1375`) gates on **capacity**:
  `if (p->isWeapon != 0) { int maximum = ammoGetCapacity(p->item); if (maximum > 0) { … } }`.
- The Awareness examine line (`proto_instance.cc:318-322`) gates on **caliber**:
  `if (ammoGetCaliber(item2) != 0)` picks message 547 (`"and is wielding a %s with %d/%d shots of
  %s."`) and otherwise message 546 (`"and is wielding a %s."`).

`ammoGetCaliber` (`item.cc:1395-1412`) resolves the *ammo* proto through the weapon's `ammoTypePid`
and returns **0 when that proto cannot be loaded** — i.e. when `ammoTypePid` is `-1`.

So the two conditions genuinely diverge, and a "just use capacity for both" fix would be wrong.
Verified against real proto data:

| PID | Weapon | Capacity | Caliber | HUD bar | examine "shots" |
|-----|--------|----------|---------|---------|-----------------|
| 160 | Cattle Prod | 20 | 3 | yes | yes |
| 390 | Solar Scorcher | 6 | 0 (`ammoTypePid` −1) | yes | **no** |
| 161 | Red Ryder BB Gun | 100 | 0 | yes | **no** |
| 427 | Flame Breath | 4 | 0 | yes | **no** |

**`WeaponProtoStats.Caliber != 0` is a faithful stand-in for `ammoGetCaliber(weapon) != 0`** — checked
across the weapon set, the weapon proto's own caliber field equals the referenced ammo proto's caliber
for every weapon with a real `ammoTypePid`, and both are 0 when it is −1. Reloading cannot break the
equivalence: `weaponAttemptReload` only accepts ammo of a matching caliber. Use the proto field and
say why in a comment, rather than adding an ammo-proto lookup that would always agree.

### What Hexwaste has

- `ViewerGame.Hud.cs:145-147` draws a **numeric** ammo count with `NUMBERS.FRM` at
  `(o.X + 458, o.Y + 76)`, gated on `w.IsGun(weaponProto.ExtendedFlags)`.
- `ViewerGame.cs:5961-5963` prints `" (n/cap shots)"` in the Awareness examine line, gated on
  `w.IsGun(...)`.

Both changes are one condition each. `WeaponAmmo` is already weapon-class-agnostic, so the values are
correct as soon as the gates let them through.

### F38's third claim is wrong, and it matters — the reference draws no digits at all

F38 says the HUD site is a "counter" to be re-gated. It is, in Hexwaste — but that counter is **not
what vanilla shows.** `interfaceUpdateAmmoBar` (`interface.cc:1985-2007`) paints a **70-pixel vertical
dithered gauge**, one pixel wide, at `x = 463 + gInterfaceBarContentOffset` from `y = 26` downward:
colour 14 for the empty span, then alternating 196/14 for the filled span, with the ratio forced even.
There is no numeric ammo readout in the vanilla interface bar.

Hexwaste's digits date from the original HUD work (`1a7d27a`, P11-M1/M2) and carry no citation. That
is a **display-shape divergence**, separate from this item's gating question, and changing it would
alter the HUD for every gun in the game — a visible change deserving its own decision and its own
measurement. **It is out of scope here and must be filed**, not fixed in passing and not left as prose
in this spec.

## Scope

### In

1. `ViewerGame.Hud.cs:146` — gate on `AmmoCapacity > 0` instead of `IsGun`, citing
   `interface.cc:1357-1359`.
2. `ViewerGame.cs:5962` — gate on `Caliber != 0` instead of `IsGun`, citing
   `proto_instance.cc:318-322` and `item.cc:1395-1412`, with the note above on why the proto's own
   caliber field is used.

### Out — each to be filed as its own backlog entry

- **Digits vs the vanilla dithered gauge** (`interface.cc:1985-2007`), as argued above.
- **The MISC-charges branch.** `_intface_update_ammo_lights`'s `else` shows the same gauge for a
  non-weapon MISC item in hand from `miscItemGetMaxCharges` / `miscItemGetCharges`
  (`interface.cc:1363-1370`). Hexwaste parses `MiscCharges` (`ProtoDatabase.cs:46`, stamped on
  instances per P116) so the data exists, but its HUD slot is weapon-only, so this needs a wider
  change than a gate.

## What carries the proof

Both sites are viewer-side, so **no golden covers either**, and the project's probe idiom is the
substitute. Do not claim this works because it compiles.

- Extend or add a harness probe that prints what each readout would show for a given equipped weapon,
  and exercise it against **a capacity melee weapon (Cattle Prod 160), a normal gun, a
  caliber-0 gun (Solar Scorcher 390) and a capacity-less melee weapon** — the four cases the table
  above distinguishes. The Solar Scorcher case is the one that proves the two gates were kept
  distinct rather than collapsed into one.
- If an existing probe already exposes the HUD or examine strings, extend it rather than adding a
  second one; look before building.

## Fixture expectations

**Byte-identical, all suites.** Both sites are draw/log paths not reached by any fixture — the
examine readout is additionally gated behind the Awareness perk, and its own source comment already
records that no golden examines a critter. A moving fixture is a stop condition.

## Docs

`docs/BACKLOG.md`: F38 → shipped, **with its two corrected claims stated** — that the HUD and examine
gates differ (capacity vs caliber) rather than both being capacity, and that vanilla's HUD readout is
a gauge rather than digits. File the two out-of-scope entries. Note F38's own provenance lesson: it
was filed from a review finding without grounding, and two of its three claims did not survive
contact with the reference.

## Definition of done

Both gates re-based with their citations; probe evidence covering the four weapon cases including
Solar Scorcher; suites byte-identical; `docs/BACKLOG.md` reconciled with F38 shipped, its claims
corrected, and the two successors filed.
