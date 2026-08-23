# Sub-project: the two-PID ammo-cost doubling (F31) — design spec (2026-08-23)

Close **F31**: `_item_w_compute_ammo_cost` doubles the ammo cost of an attack for exactly two
hardcoded PIDs. Unblocked by F34 (`3e27240`'s predecessor `0645d09`), which gave those weapons a
per-attack charge to double.

## Grounding — verified against `e97087b` on 2026-08-23

`_item_w_compute_ammo_cost` (`item.cc:1947-1965`) in full:

```c
int _item_w_compute_ammo_cost(Object* obj, int* inout_a2)
{
    if (inout_a2 == nullptr) return -1;
    if (obj == nullptr) return 0;
    pid = obj->pid;
    if (pid == PROTO_ID_SUPER_CATTLE_PROD || pid == PROTO_ID_MEGA_POWER_FIST) {
        *inout_a2 *= 2;
    }
    return 0;
}
```

- The two PIDs are 399 and 407 (`proto_types.h:177-178`). F34's census confirms both ship and are
  among the five non-gun ammo-capacity weapons; both have capacity 20 and 25 respectively and draw
  Small Energy Cell.
- The single call site is `attackCompute` (`combat.cc:3905`), which runs **after both branches** — the
  ranged one that sets `ammoQuantity` from the spray and the non-ranged one at `:3900-3902` that sets
  it to 1. So the doubling is not melee-specific in the code, only in effect: both PIDs are melee
  (SWING / PUNCH), so the ranged and burst paths can never reach it.
- The `-1` return is unreachable from that call site — `attackCompute` passes
  `&(attack->ammoQuantity)`, never null.

### The quirk this creates, which we must port rather than fix

The refusal tests `ammoGetQuantity(weapon) == 0` (`_combat_check_bad_shot`, `combat.cc:5679-5683`),
and the deduction (`_combat_anim_finished`, `:5348-5350`) clamps only at the **top**
(`ammoSetQuantity`, `item.cc:1421-1426`: `if (quantity > capacity) quantity = capacity`) — there is no
floor.

So for these two weapons at an **odd** charge count, vanilla spends 2 from 1 and lands on −1. `−1 != 0`,
so the refusal never fires and the weapon keeps attacking, drifting −1, −3, −5… Reloading resets it
(`weaponReload` fills to capacity or adds the cell's quantity, `item.cc:1566-1588`), and both
capacities are even, so this is reachable only from an odd starting count that is never reloaded — a
map-placed instance, most plausibly.

**Port it as-is.** Do not add a floor, and do not extend the refusal to "fewer charges than the cost".
Neither exists in the reference, and inventing either would be a deviation dressed as a bug fix.
**Record the quirk in the backlog entry** so it is a known-and-chosen behaviour rather than a latent
surprise. Whether any shipped map places PID 399 or 407 with an odd `AmmoQuantity` is **not surveyed**
— say so rather than implying it cannot happen.

## The Hexwaste side

F34 left three spend sites, all of the shape
`weaponItem.AmmoQuantity = _host.WeaponAmmo(weaponProto, weaponItem) - 1`, gated on
`UsesCharges(weaponProto)` — the dude, ally and enemy attack paths in
`src/Hexwaste.Formats/Combat/CombatEngine.cs`. There is a fourth, the burst path, of the shape
`Math.Max(0, b.AmmoBefore - b.RoundsFired)`.

**Verify all four locations as the file stands now** — F34 shifted this file substantially and every
line number in this spec is a hint, not a fact.

## Scope

### In

One helper beside `UsesCharges`, used at every site that spends charges:

```csharp
private const int PidSuperCattleProd = 399, PidMegaPowerFist = 407; // proto_types.h:177-178
private static int AmmoCost(ProtoInfo? weaponProto, int quantity) => …
```

— doubling `quantity` for those two PIDs and returning it unchanged otherwise, cited to
`item.cc:1947-1965`. The three single-shot sites then subtract `AmmoCost(weaponProto, 1)` instead of
`1`.

The **burst site is included for faithfulness and is inert**: the reference applies the doubling after
both branches, so a burst-capable weapon with one of these PIDs would double its rounds — but neither
PID is burst-capable (`SWING` / `PUNCH`), so nothing changes. Wire it and say why in a comment, rather
than leaving a site that silently disagrees with the reference's structure.

### Out

- **The burst site's `Math.Max(0, …)` floor.** Vanilla has no floor. It is pre-existing, unrelated to
  this item, and unreachable for these two PIDs. Leave it; it is not worth a fixture risk here. If it
  bothers a reviewer, it is a separate finding.
- **Any change to the refusal.** See the quirk above.

## What carries the proof

Hermetic tests through `FakeCombatHost`, each confirmed failing pre-change **and for the right
reason**:

1. **PID 399 spends 2 per attack.** The item.
2. **PID 407 spends 2 per attack.** The second hardcoded PID — a test for only one would pass with
   half the constant list.
3. **A different capacity weapon still spends 1** — e.g. the Cattle Prod (160), whose near-identical
   name and behaviour make it exactly the weapon a wrong implementation would catch by accident.
4. **A gun still spends 1**, the inertness guard for the 13 gun fixtures.

## Fixture expectations

**Byte-identical, all suites.** F34 established that no fixture wields any of the five capacity
weapons; this item narrows to two of those five. A moving fixture is a stop condition.

## Docs

`docs/BACKLOG.md`: F31 → shipped, stating the odd-count drift quirk as ported-deliberately and noting
it is unsurveyed in map data. While in the file, fix the **duplicated measurement clause** in F34's
header — the suite list appears twice, the second time with the parenthetical about `census`,
`endgame` and `opening`; keep one.

## Definition of done

The helper ported with its citation and used at all four spend sites; four hermetic tests green and
mutation-verified; suites byte-identical; F31 shipped with the quirk recorded; F34's duplicated clause
cleaned.
