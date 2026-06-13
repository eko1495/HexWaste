# Phase-9 Track B: AI packets / ai.txt

Scope: make `MapObject.AiPacket` actually drive enemy behavior. Today it is
parsed and ignored. This note resolves the `ai.txt` schema, the *real*
`combat_ai.cc` semantics of the fields that matter, the concrete packets the
slice critters carry, and a minimal M1 subset.

## Q0 — Confirm: AiPacket is read NOWHERE for AI decisions

Grep across `src/`, `tools/`, `tests/`:
- Parsed: `Map/MapFile.cs:432` (`obj.AiPacket = reader.ReadInt32()`), exposed at `Map/MapFile.cs:104`.
- Proto field: `Proto/ProtoDatabase.cs:83` (`CritterProtoStats.AiPacket`).
- Round-tripped only: `SaveState.cs:81` (party save), `ViewerGame.cs:4891`/`5023` (party save/restore).
- Script externals only: `Int/ScriptHost.cs:875` (`get_critter_stat` field 5 read), `:910` (`set_critter_stat` write).
- Debug print only: `ViewerGame.cs:548-549` (title-bar dump).
- One test asserts a parsed value: `tests/.../CritterStatsTests.cs:53` (`Assert.Equal(14, critter.AiPacket)`).

Nothing in `TryEnemyAction`/`TryAllyAction`/`EnemyAttack` (`ViewerGame.cs:2930/3008/3079`)
reads it. **Confirmed: AI is packet-blind.** Current enemy logic
(`ViewerGame.cs:2955-3003`): reload-if-dry → if in weapon range with clear
LoF, attack regardless of computed hit chance → else A* one step toward the
nearest of dude+allies, stop adjacent. There is **no min_to_hit gate** (the
prompt's "flat min_to_hit 30" is a characterization: enemies fire at any
chance; the only clamp is `CombatMath.ToHit` 0..95 at `CombatMath.cs:18`),
**no fleeing**, **no distance preference**, **no best-weapon selection**.

## Q1 — ai.txt schema

`data\ai.txt` (extracted `/tmp/ai.txt`): **8267 lines, 187 `[Section]`s**, one
section per AI packet, standard Fallout INI (`key=value`, `[Section]`
headers). The section *name* is cosmetic; the lookup key is the integer
`packet_num` field. The proto's `aiPacket` (and the per-instance MAP
`AiPacket`) is that `packet_num`. Parsed by `aiInit()`
(`combat_ai.cc:370-470`) into the `AiPacket` struct (`combat_ai.cc:59-88`).

Fields, defaults from `aiPacketInit` (`combat_ai.cc:350-366`) — string fields
absent → **-1**; ints absent → parse fails the packet (they are mandatory in
the shipped file). The fields that matter for behavior:

| key | type | meaning (cite) |
|---|---|---|
| `packet_num` | int | the lookup id; matches proto `aiPacket` |
| `aggression` | int | parsed (`combat_ai.cc:409`); **NOTE: read by no combat function — flavor/legacy** |
| `min_to_hit` | int | the walk-closer hit-chance floor; see Q2 |
| `min_hp` | int | RAW hp flee threshold; see Q3 |
| `max_dist` | int | leash: disengage if dist-to-target > max_dist with AP left (`combat_ai.cc:3101`) |
| `distance` | enum→0..4 | stay_close/charge/snipe/on_your_own/stay; see Q4 (default -1 = none) |
| `run_away_mode` | enum→0..6 | minus-1'd (`combat_ai.cc:455-459`); **only feeds a % table for the party-UI + a debug print — NOT the flee decision**; see Q3 |
| `disposition` | enum→0..4 | minus-1'd (`:481-482`); branches **only for party members** in `_ai_danger_source` (`:1542-1559`); see Q4 |
| `hurt_too_much` | bitmask | crippled/blind flee trigger (`combat_ai.cc:302-335`, `_rmatchHurtVals` `:242-248`) |
| `best_weapon` | enum→0..7 | weapon-preference order (`gBestWeaponKeys` `:180-189`, `_weapPrefOrderings` `:269-279`); see Q5 |
| `area_attack_mode` | enum→0..4 | burst/AoE safety gate (`gAreaAttackModeKeys` `:162-168`) — out of M1 scope (no burst yet) |
| `attack_who` | enum→0..4 | target selection: attacking_me/strongest/weakest/whomever/closest (`:171-177`); only consumed for party members + via `_ai_danger_source` |
| `chem_use` | enum→0..5 | drug usage (`gChemUseKeys` `:192-199`) — out of scope, NPCs in slice carry few chems |
| `secondary_freq`,`called_freq` | int | AI taunt-message + called-shot frequency knobs — cosmetic for us |
| `run/move/attack/miss/hit_*` | ranges | combatai taunt-message line ranges (`AiMessageRange[]`) — cosmetic |
| `font/color/outline_color/chance` | int | message rendering — cosmetic |
| `body_type/general_type` | str | message-list selection — cosmetic |

Enum key tables (transcribe verbatim for the parser):
- `gDistanceModeKeys` (`combat_ai.cc:202-208`): `stay_close,charge,snipe,on_your_own,stay` → 0,1,2,3,4.
- `gDispositionKeys` (`:222-229`): `none,custom,coward,defensive,aggressive,berserk`, then `disposition--` → none=-1,custom=0,coward=1,defensive=2,aggressive=3,berserk=4.
- `gRunAwayModeKeys` (`:211-219`): `none,coward,finger_hurts,bleeding,not_feeling_good,tourniquet,never`, then `run_away_mode--`.
- `gBestWeaponKeys` (`:180-189`): `no_pref,melee,melee_over_ranged,ranged_over_melee,ranged,unarmed,unarmed_over_thrown,random`.
- `gAttackWhoKeys` (`:171-177`): `whomever_attacking_me,strongest,weakest,whomever,closest`.

## Q2 — `min_to_hit`: walk closer until toHit ≥ min, else flee

This is the field that turns flat combat into Fallout combat. The logic lives
in `_ai_try_attack` (`combat_ai.cc:2692-2900`). `min_to_hit` is read once at
`:2705`. The bad-shot dispatcher loops up to 10 times (`:2726`) on
`_combat_check_bad_shot` codes (`combat_defs.h:164-171`):

- **OUT_OF_RANGE (2)** (`:2807-2830`): compute to-hit ignoring range
  (`_determine_to_hit_no_range`, `:2809`). If even at point-blank
  `toHitNoRange < minToHit` → **`_ai_run_away`** (`:2812-2814`, debug
  `"FLEEING: Can't possibly Hit Target!"`). Otherwise move closer with all AP
  (`_ai_move_steps_closer`, `:2818`).
- **OK (0)** (`:2837-2895`): real to-hit = `_determine_to_hit` (`:2838`). If
  `accuracy < minToHit` (`:2845`): re-check no-range; if still below min →
  flee (`:2848-2850`). Else **walk tile-by-tile toward the target, stopping at
  the first tile where the projected to-hit clears the floor**
  (`:2853-2879`): pathfind, then for each step tile call
  `_determine_to_hit_from_tile`; `if (toHit >= minToHit) break` (`:2869-2872`).
  Spend exactly `actionPointsToUse` steps, then attack (`:2881-2888`).
- AIM_BLOCKED (5) (`:2831-2836`): step closer to clear LoF.

So the engine **never fires below `min_to_hit`** when it could close the gap;
it closes to the cheapest tile that satisfies the floor, and **flees outright**
when no reachable tile (or point blank) can. Our current code fires at any
chance and never flees — this single field is the largest felt-depth change.

Port shape for Hexwaste (host already has A*, LoF, `CombatMath.ToHit`):
1. compute chance at current tile; if `>= minToHit` and in range → attack (as today);
2. else walk the A* path toward target one tile at a time, recompute
   `CombatMath.ToHit` projected from each candidate tile (we don't have
   `_determine_to_hit_from_tile`, but `ToHit` is position-independent except
   for distance falloff + LoF crowd — recompute distance/LoF from the tile),
   stop at the first tile clearing `minToHit` or when AP/path exhausted;
3. if even adjacent / point-blank cannot clear `minToHit` → set FLEEING and
   run away (Q3). Use the proto/instance `min_to_hit`, not a constant.

## Q3 — `min_hp` flee: RAW hp, NOT the run_away_mode % table

**Resolved: the combat flee check compares RAW current HP to `ai->min_hp`
directly.** `_combat_ai` (`combat_ai.cc:3053-3162`), the per-critter turn
entry, at **`:3075-3081`**:

```c
if ((combatData->maneuver & CRITTER_MANUEVER_FLEEING) != 0
    || (combatData->results & ai->hurt_too_much) != 0
    || critterGetStat(a1, STAT_CURRENT_HIT_POINTS) < ai->min_hp) {   // :3077
    debugPrint("%s: FLEEING: I'm Hurt!", ...);
    _ai_run_away(a1, a2);
    return;
}
```

The flee fires if **current HP < `min_hp`** (raw), OR a cripple/blind in
`hurt_too_much` is set, OR the FLEEING maneuver is already latched.

The `run_away_mode` → `_hp_run_away_value` percentage table
(`combat_ai.cc:253-260`: `{0,25,40,60,75,100}`) is **NOT** used by the combat
flee decision. Its only consumers:
- `_cai_get_min_hp` (`:3036-3050`) → returns the % when `run_away_mode != -1`,
  else `ai->min_hp` — but its single caller `_combat_ai:3065` uses the result
  **only for a `debugPrint`** at `:3066-3072` (`"minHp = %d; curHp = %d"`).
  It does not gate the flee.
- `aiGetRunAwayMode` (`:763-780`) → consumed **only** in `game_dialog.cc:3891`
  (the party-member "Run Away Mode" customization combobox). Not combat.

So for our scope: **use `min_hp` raw**. Ignore `run_away_mode` (it is a
party-UI / debug concept). `_ai_run_away` itself (`:1173`) flips the FLEEING
maneuver and moves away from the danger source; the minimal port is "set a
KnockedDown-style `Fleeing` flag and `_ai_move_away` from the attacker" — but
even cheaper for M1, see the recommendation: just **disengage** (stop
attacking, walk away from the dude) when below `min_hp`.

## Q4 — distance modes & disposition

`distance` (the enemy combat-range preference) is consumed by
`_cai_perform_distance_prefs` (`combat_ai.cc:2970-3033`), called from
`_combat_ai:3091` before `_ai_try_attack`, and again from `:3159` with spare
AP. `switch (aiGetPacket(a1)->distance)`:

- **STAY_CLOSE (0)** (`:2985-2992`): if not currently being hit by the dude,
  and dist-to-dude > 5, move to within 5 of the **dude** (companion leashing).
- **CHARGE (1)** (`:2993-2998`): `_ai_move_closer(a1, a2, 1)` — close all the
  way to the target with taunt.
- **SNIPE (2)** (`:2999-3019`): if dist-to-target < 10, and after paying the
  attack AP there are movement points, kite: if too close (`movementPoints +
  distance - 1 < 5`) and the attacker out-rates the defender, `_ai_move_away`
  10; if no movement points left, `_ai_move_away` 10. Keeps distance.
- **ON_YOUR_OWN (3)**: no case in the switch → falls through to the
  friendly-fire retarget tail (`:3022-3030`) only. Effectively "no special
  positioning, just `_ai_try_attack` decides" — i.e. the default
  walk-until-can-hit behavior.
- **STAY (4)**: also no switch case → same fall-through; BUT
  `_ai_move_steps_closer` early-returns -1 for `DISTANCE_STAY` (`:2360-2363`),
  so a STAY critter that `_ai_try_attack` wants to move will instead **flee**
  (the move returns -1 → `_ai_run_away` at `:2818-2819`/`:2881-2885`). STAY =
  "shoot from here or give up."
- **default / -1 (none)**: no positioning pref; `_ai_move_steps_closer`
  proceeds normally → plain approach. **Most slice critters are here** (they
  don't set `distance`).

`disposition` (`combat_ai.cc:892-900`) is consumed **only inside
`_ai_danger_source`** (`:1529-...`) and **only for party members**
(`if (objectIsPartyMember(a1))` at `:1541`): it sets `ignoreFleeingCritters`
(`:1544-1559`) — coward/defensive/aggressive/custom ignore fleeing targets,
none/berserk do not; charge-distance forces not-ignore (`:1557-1558`). For
**enemy** (non-party) AI, disposition does **not** branch behavior; targeting
for enemies uses `whoHitMe`/nearest. Also used in `game_dialog.cc:3487`
(party-customization UI). **Out of M1 scope** (we have no NPC party AI driven
by ai.txt; companions are 100% script-side per phase-7 M4). The slice's
**non-party** packets almost all leave `disposition` unset (-1).

## Q5 — best_weapon (brief)

`aiGetBestWeapon` (`:783-787`) returns the enum. `_ai_best_weapon`
(`:1817-...`) ranks the critter's two equipped weapons: for each, compute avg
damage (`(min+max)/2`, `:1858`), ×(extras+1) for AoE (`:1859-1863`), ×2 if the
weapon has a perk (`:1866-1869`), invalidate unsafe weapons
(`_combat_safety_invalidate_weapon`, `:1872`); then `_weapPrefOrderings`
(`:269-279`) gives the attack-type priority list per `best_weapon` value
(e.g. `ranged_over_melee` = {RANGED, MELEE}, `melee` = {MELEE} only). The
chosen weapon's hit mode comes from `_ai_pick_hit_mode` (`:2262`).
**Low M1 value**: slice NPCs carry at most one weapon each (MAP-equipped, see
phase-6 M4 "MAP NPC weapons just work"); with one weapon there is nothing to
rank. Defer until burst/throwing add a second weapon slot worth choosing.

## Q6 — Which packets the slice critters actually carry

Method: temp dump program over `Hexwaste.Formats` (enumerate all
`OBJ_TYPE_CRITTER` objects per map, group by PID, read proto
`Critter.AiPacket` + per-instance `MapObject.AiPacket`), names resolved via
`pro_crit.msg` `MessageId`. Cross-referenced to `/tmp/ai.txt` by `packet_num`.
Tools restored after (MapDump working tree clean).

**Per-instance vs proto note:** the MAP file can override the proto's
`aiPacket` per object. Several Den critters do: e.g. denbus1 PID 54 "Loser"
has proto packet **25** but many instances carry **14** (`instAi=[14,25]`).
Consume the **instance** `MapObject.AiPacket`; fall back to the proto when the
instance value is the engine's "unset" sentinel. (In the parsed data the
instance value is always a valid packet, so just use `MapObject.AiPacket`.)

The packets present in the slice and their key fields (from `/tmp/ai.txt`):

| pkt | ai.txt name | aggr | min_to_hit | min_hp | max_dist | distance | run_away_mode | best_weapon | hurt_too_much |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Arroyo Warrior | 90 | 30 | 5 | 20 | on_your_own | - | - | blind |
| 6 | Brahmin | 10 | 0 | 0 | 15 | - | - | - | crippled,blind |
| 7 | Rat | 45 | 0 | 0 | 15 | - | - | - | blind |
| 8 | Scorpion | 60 | 0 | 0 | 15 | - | - | - | blind |
| 12 | Generic Guards | 80 | 20 | 4 | 20 | - | tourniquet | ranged_over_melee | crippled,blind |
| 13 | Thugs | 80 | 40 | 10 | 10 | - | not_feeling_good | - | crippled,blind |
| 14 | Peasants | 30 | 34 | 12 | 10 | - | bleeding | - | crippled,blind |
| 15 | Child | 0 | 10 | 20 | 8 | - | - | - | crippled,blind |
| 17 | Store Owner | 33 | 30 | 8 | 8 | - | tourniquet | - | crippled,blind |
| 21 | Generic Dog | 80 | 0 | 0 | 18 | - | - | - | blind |
| 22 | Tough Guard | 95 | 15 | 1 | 19 | on_your_own | tourniquet | ranged_over_melee | (none) |
| 24 | Tough Citizen | 60 | 30 | 10 | 12 | - | tourniquet | - | crippled,blind |
| 25 | Wimpy Peasant | 95 | 0 | 0 | 7 | charge | never | no_pref | crippled,blind |
| 26 | Gecko | 50 | 0 | 0 | 10 | - | - | - | crippled,blind |
| 30 | Repair Bot | 50 | 30 | 10 | 10 | - | - | - | blind |
| 32 | Tough Bot | 90 | 0 | 0 | 10 | on_your_own | never | no_pref | blind |
| 39 | Wimpy Gecko | 20 | 25 | 0 | 10 | - | - | - | crippled,blind |
| 50 | Cyberdog | 75 | 10 | 0 | 12 | stay_close | - | unarmed | crippled,blind |
| 57 | PARTY VIC AGGRESSIVE | 45 | 10 | 10 | 12 | on_your_own | not_feeling_good | ranged_over_melee | crippled,blind |
| 77 | PARTY SULIK AGGRESSIVE | 45 | 10 | 10 | 12 | on_your_own | tourniquet | melee_over_ranged | crippled,blind |
| 134 | Enclave Patrol | 90 | 10 | 1 | 10 | - | - | - | (none) |

Concrete slice mappings (proto packet shown; instances may override):

- **artemple.map** — 1 placed critter: PID 3 "Villager" → packet **1** (Arroyo
  Warrior: min_to_hit 30, min_hp 5, distance on_your_own). NOTE: the temple's
  radscorpions are **script-spawned** (not in the MAP object list), so they
  are not parseable from the map file; the placed critter is the villager.
- **Den (denbus1/denbus2/denres1)** — the meaty fights:
  - Thugs / slavers: PID 34/35/36 "Agile Thug" → packet **13** (Thugs:
    min_to_hit 40, **min_hp 10**, max_dist 10).
  - Slaver guards: PID 42/45/46/47/71 "Melee Guard"/"Weak Gun Guard" → packet
    **12** (Generic Guards: min_to_hit 20, min_hp 4, ranged_over_melee).
  - PID 40/41 "Tough Guard" → packet **22** (min_to_hit 15, min_hp 1,
    on_your_own).
  - Metzger (slave boss) PID 290 → packet **12**.
  - Generic slaves / homesteaders / Anna / Karl: PID 28/29/65/66/67/196/220 →
    packet **14** (Peasants: min_to_hit 34, **min_hp 12**, bleeding).
  - "Loser" street crowd PID 54/55 → packet **25** (Wimpy Peasant: min_to_hit
    0, min_hp 0, **charge**, never flees) — these are the cannon-fodder crowd.
  - Merchants PID 56/58/60 → packet **17** (Store Owner). Vic PID 62 → packet
    **57** (party).
- **Klamath (kladwtwn/KLAMALL/klatoxcv/KLARATCV/klatrap/klagraz/KLACANYN)** —
  mostly critters, not humanoids:
  - Rats/mole rats/pig rats: PID 11/110/112/113/309 → packet **7** (Rat:
    min_to_hit 0, no flee). KLARATCV is 34× PID 11.
  - Geckos: PID 80/81/83 → packet **26** (Gecko: min_to_hit 0). klatrap +
    klatoxcv are gecko packs.
  - Radscorpions: PID 5 (klagraz ×7) → packet **8** (Scorpion).
  - Dogs PID 9 → packet **21**; Brahmin PID 10/114 → packet **6** (aggr 10,
    near-harmless).
  - Whiskey Bob's thugs / Klamath toughs PID 67/68 → packet **14**; guards PID
    69/71 → **12**; Sajag/store PID 56-61 → **17**.
  - Robots: KLACANYN PID 172 / KLARATCV PID 73 "Mr. Handy/Repair Bot" → packet
    **30**; klatoxcv PID 78 sentry bot → packet **32**.
  - Sulik PID 97 → packet **77** (party). Enclave Guards PID 291/292 → packet
    **134** (KLACANYN — the toxic-caves ambush set piece).

## Recommended M1 subset — the 2–3 fields that move the fights

The slice's *humanoid* fights (Den slavers/thugs/guards, Klamath toughs) are
exactly the ones with non-zero `min_to_hit` / `min_hp`. The animal packs
(rats/geckos/scorpions) are min_to_hit 0 / min_hp 0 — they already behave
correctly under the current "rush and bite" AI, so M1 changes nothing for
them (good: no regression risk on the most numerous critters).

Ship, in priority order:

1. **`min_to_hit` (the headline).** Walk-closer-until-hittable + flee-if-
   impossible, per Q2. This is what makes Den slavers with pistols close the
   gap and what stops a melee thug from flailing at a chance it can never
   make. Reuse existing A* + `CombatMath.ToHit`. **This is ~80% of the felt
   change.** Slice packets affected: 12 (min 20), 13 (40), 14 (34), 17/24/30
   (30), 22 (15), 39 (25), 50/57/77/134 (10), 1 (30).

2. **`min_hp` (raw) flee.** Per Q3: when `CurrentHp < packet.min_hp`, the
   critter disengages (stop attacking + step away from the attacker) instead
   of fighting to 1 HP. Minimal version: set a transient `Fleeing` state on
   the critter for the rest of combat; on its turn, walk away from the nearest
   hostile rather than toward. Affects Thugs (min_hp 10), Peasants/Karl/Anna
   (12), Store Owner (8), Generic Guards (4). Makes wounded slavers break —
   the single biggest "this feels like Fallout" beat after #1. **Use raw
   `min_hp`; do NOT implement the run_away_mode % table** (it is party-UI /
   debug only).

3. **`distance` (cheap, only 4 slice packets use it).** Implement only the two
   that appear on real fighters: **charge (1)** = packet 25 "Loser" crowd
   (rush all-out — they already do, so this is a no-op confirmation) and
   **on_your_own (3)** = packets 1/22/32 (default approach — also effectively
   current behavior). `snipe`/`stay_close` appear on no hostile enemy worth
   the kiting code (stay_close 50 = Cyberdog, not in slice maps;
   snipe = none). **Verdict: parse `distance` for completeness but the only
   behavior to add is the STAY (4) "shoot-or-flee" early-out — and STAY
   appears on zero slice critters, so distance is effectively a no-op for our
   content. Wire the field, skip the positioning code, document it.**

**Defer:** `best_weapon` (NPCs carry one weapon — Q5), `disposition` (party
only — Q4), `area_attack_mode`/`chem_use` (no burst, few chems),
`hurt_too_much` cripple-flee (we have no crippling yet — phase-9 track C; once
knockdown/cripple lands, the `hurt_too_much` bit at `_combat_ai:3076` becomes
free to add since the flee path already exists from #2), `aggression` (read by
no combat function — `combat_ai.cc` never references it; it is legacy/flavor).

**Data work:** a small `AiPacketTable` parser in `Hexwaste.Formats.Combat`
reading `data\ai.txt` (INI; reuse the existing MSG/config reading style),
keyed by `packet_num`, exposing `{MinToHit, MinHp, MaxDist, Distance}` for the
packets in the slice. ~80 packets needed at most; parse all 187 cheaply.
Resolve a critter's packet via `MapObject.AiPacket`.

**Save impact:** none required for M1. `min_to_hit`/`min_hp` are derived from
the (already-saved) `AiPacket` + live `CurrentHp`. A transient `Fleeing` state
is combat-scoped (cleared on combat end), so it need not persist — no
Version-3 bump. (If a future track makes flee survive a save mid-combat, fold
a `Fleeing` bool into the additive-V2 critter delta like `KnockedDown`.)

**Headless test:** extend the `--fight` harness with a low-HP enemy seed and
assert (a) a slaver with min_to_hit 20 closes the gap before firing when
started out of range, (b) a thug at HP < 10 emits a flee/disengage transcript
line rather than attacking. Both are deterministic given `--rng-seed`.

## Unverified / caveats

- `aggression` is parsed (`combat_ai.cc:409`) but I found no combat-path
  reference; flagging it "read by no combat function" from grep of
  `combat_ai.cc` — UNVERIFIED whether sfall or another TU reads it. Safe to
  ignore for M1 either way.
- Per-instance `AiPacket` overrides (e.g. denbus1 PID 54 inst 14 vs proto 25)
  are real in the parsed data; I did not trace *why* the MAP overrides — just
  that we should consume the instance value. UNVERIFIED: whether any slice
  instance carries an "unset" sentinel needing proto fallback (none observed).
- artemple radscorpions are script-spawned, so their packet (8, Scorpion) is
  inferred from the proto, not from a map object — they will get the right
  packet at spawn time as long as the spawn path copies proto `aiPacket` into
  `MapObject.AiPacket` (verify when wiring create_object spawns).
