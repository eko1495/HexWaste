# P10 Track C — transient-map group spawn + formations + the EC*.int missing-external CENSUS

Scope: how a worldmap encounter group is instantiated in the engine
(`wmSetupRandomEncounter` / `wmSetupCritterObjs`), the formation geometry,
how that maps onto Hexwaste's `create_object_sid` + MAP-NPC equip path
(phases 5/7), and — the headline deliverable — the **missing-external census**
for the early-loop EC\*.int encounter scripts. All engine claims cite
`reference/fallout2-ce/src/<file>.cc:LINE`; all data is quoted from the real
`worldmap.txt` (repo root), `scripts.lst`, `maps.txt` (from the DAT) and the
EC\*.int disassemblies (`python3 tools/int_analyze.py` + `/tmp/int_disasm.py`),
never from memory.

---

## (1) Group spawn — the engine path (worldmap.cc) → Hexwaste mapping

### Entry: `wmSetupRandomEncounter` (worldmap.cc:3657)
Runs **after** the map's `map_enter_p_proc` (map.cc:978 calls it post-enter).
Per sub-entry (`X AMBUSH Player`, `X AND Y`, `X FIGHTING Y`):
- `critterCount = randomBetween(min,max)` (worldmap.cc:3692); difficulty
  Easy −2 / Hard +2 (worldmap.cc:3696-3706); **`_getPartyMemberCount() > 2` ⇒
  +2** (worldmap.cc:3708-3711) — the same `_getPartyMemberCount` the Vic/Sulik
  party-size gate needs (metarule 16); ties Track C's spawn to the companion
  fold-in.
- `wmSetupCritterObjs(encounterIndex, &critter, count)` (worldmap.cc:3714).
- Group-vs-group autostart: only for `index > 0` with
  `subEntiesLength == 2 && !isInCombat()` — sets `whoHitMe` on both leaders,
  `_caiSetupTeamCombat` + `_scripts_request_combat_locked`
  (worldmap.cc:3718-3741). **`X AMBUSH Player` does NOT auto-start here** — the
  `Player` sub-entry returns without writing the out-param
  (`wmSetupCritterObjs` early-returns at :3719 when encounterIndex==-1), so
  `critter`==`prevCritter` and the combat block is skipped. **Ambush hostility
  comes from the spawned critters' SCRIPTS** (the `obj_can_see_obj → attack`
  heartbeat — see §3), confirming the p8-track-a finding.

### Per-critter spawn: `wmSetupCritterObjs` (worldmap.cc:3771-3909)
For each `type_NN` (skip if `pid==-1` or its `If()` fails — `wmEvalConditional`
gets the group count as the `enctr()` operand, worldmap.cc:3794):
1. count = `ratio*groupCount/100` (USE_RATIO) else **1** (SINGLE — no `ratio:`
   key, e.g. leaders), min-clamped to 1 (worldmap.cc:3796-3812).
2. **`objectCreateWithPid(&object, pid)`** (worldmap.cc:3827) — proto defaults.
3. team override only `if (encounterEntry->team != -1)` (worldmap.cc:3841-3845)
   — and `team_num=` is **engine-UB garbage** (only present in a commented
   example block; `configGetInt` doesn't write on a missing key while the local
   `team` is read uninitialized). **Net: team comes from the proto + the bound
   SCRIPT's `critter_add_trait`, NOT worldmap.txt** (re-confirmed; p8-track-a Q3).
4. **script binding** (worldmap.cc:3843-3848): `scriptRemove(object->sid)` then
   `_obj_new_sid_inst(object, SCRIPT_TYPE_CRITTER, scriptIdx - 1)`
   (worldmap.cc:3848) —
   **worldmap.txt `Script:N` is 1-based; engine binds the 0-based scripts.lst
   index `N-1`** (matches CLAUDE.md's "scripts.lst is 0-based"). E.g. `Script:617`
   → scripts.lst index 616 → `ECRat.int`.
5. placement: non-Surrounding ⇒ `objectSetLocation(object, tile, gElevation)`;
   **Surrounding ⇒ `_obj_attempt_placement(object, tile, 0, 0)`**
   (worldmap.cc:3854). Then face the dude:
   `tileGetRotationTo(tile, gDude->tile)` (worldmap.cc:3857).
6. items (worldmap.cc:3860-3905): `objectCreateWithPid(&item, itemPid)`,
   `itemAdd(object, item, qty)`, `_obj_disconnect`; **`(wielded)` ⇒
   `_inven_wield(object, item, HAND_RIGHT)`** (worldmap.cc:3892).

### Hexwaste mapping (verified against the codebase)
- **`objectCreateWithPid` + `_obj_new_sid_inst(scriptIdx-1)`** = Hexwaste's
  `create_object_sid` external (IntVm.cs:1164, `CreateObject(pid, tile, elev,
  scriptIndex)`), which already allocates a fresh sid via
  `ScriptHost.AllocateSid` (ScriptHost.cs:114 — synthetic type-3 sid range) and
  binds the scripts.lst index — the exact party-member init path
  (ViewerGame.cs:4312). So spawning a script-bound critter is the create-object
  path the slice already runs; the only new code is iterating worldmap.txt
  group entries and calling it per critter at the chosen start point.
- **team**: set `critter.Team = <nonzero>` at spawn (or let the bound script's
  `critter_add_trait(self,1,6,team)` do it on its first `map_enter`/heartbeat).
  In Hexwaste **team 0 = the dude's team; any nonzero team is a potential
  hostile** (ViewerGame.cs:4257; CombatEngine.cs:613 dudeTeamKill = `Team==0`).
  ECRat's `critter_add_trait(self,1,6,124)` (team 124) → non-zero → hostile-eligible.
- **wielded equip**: a wielded weapon is just an inventory item carrying the
  **in-hand flag `0x01000000`/`0x02000000`** (MapFile.cs:129-133 `IsInHand`),
  and `EquippedWeapon` (ICombatHost.cs:39) reads exactly that. So `Item:280
  (wielded)` ⇒ add proto-280 to the spawned critter's inventory with the
  in-hand flag — the existing CombatEngine picks it up unchanged ("MAP NPC
  weapons just work", phase-6 M4). No `_inven_wield` port needed for hostility.

## (2) Formations — `wmSetupRndNextTileNumInit` (:3911) / `wmSetupRndNextTileNum` (:3972)

`wmFormationStrs` (worldmap.cc) enum: `surrounding, straight_line, double_line,
wedge, cone, huddle`.
- **Surrounding** (the bounty-hunter / spore-plant / slave ambush ring):
  center = `gDude->tile` (worldmap.cc:3921-3922), random initial direction.
  Per spawn: `distance = entry.Distance` if set, else
  **`randomBetween(-2,2) + critterGetStat(gDude, STAT_PERCEPTION)`** (+3 Cautious
  Nature — skip) (worldmap.cc:3979-3989, clamped ≥0); origin =
  `tileGetTileInDirection(gDude->tile, dir, distance)` with `dir` rotating
  through all 6 (worldmap.cc:3992-3998), then jitter
  `randomBetween(0, distance/2)` hexes in a random direction (worldmap.cc:
  4000-4004). ⇒ **a loose RING around the dude at Perception±2 hexes.**
- **straight_line / double_line / wedge / cone / huddle**: anchor = a random
  `random_start_point_N` from maps.txt (`map->startPoints`, worldmap.cc:
  3933-3945; if none → dude tile); two alternating arms grow from the anchor by
  `spacing` hexes per spawn, oriented by `tileGetRotationTo(anchor, dudeTile)`;
  huddle spirals one center (worldmap.cc:4007-4070). First critter sits ON the
  anchor.
- **Dead** entries (`type_NN=Dead,pid:...`): flag `ENCOUNTER_SUBINFO_DEAD` →
  spawn as corpse dressing (Hexwaste: the anim+28 corpse path from P5-M3).
- Placement validity `wmEvalTileNumForPlacement` (worldmap.cc:4082): tile must
  be unblocked (`_obj_blocking_at`) AND **reachable from the dude**
  (`pathfinderFindPath` with `_obj_shoot_blocking_at`, worldmap.cc:4088); 25
  retries / 25-hex drift cap (worldmap.cc:4068-4076 in `wmSetupRndNextTileNum`),
  else skip that critter.

Both `tileGetTileInDirection` and `tileGetRotationTo` are 1:1 in Hexwaste
(`Hex.HexGrid.TileInDirection` / `RotationTo`), so the geometry drops straight
on. **v1 minimal**: Surrounding = ring at `Perception±2` around the dude;
everything else = cluster-with-spacing around a random `random_start_point_N`;
Dead = corpse. The reachability gate maps onto our existing A* (the phase-4/9
pathing host). NOTE: a busy `Surrounding` with no reachable ring tile feeds the
**unreachable-joiner non-termination** (phase-9 spillover) — M3 should bound it.

`maps.txt desert1` (real): `saved=No`, `random_start_point_0=elev:0,
tile_num:19086`, `_1=…17302`, `_2=…21315` (3 anchors) — parses with the
existing MapList once `saved` + `random_start_point_N` are read (Track A/B).

---

## (3) THE EC*.int MISSING-EXTERNAL CENSUS (the deliverable)

### Method
1. From `worldmap.txt` parsed the Arroyo/Klamath/Den `[Encounter: GROUP]` blocks
   (lines 272-534, 1023) and collected every `Script:N`.
2. Mapped each `Script:N` → scripts.lst line N (1-based file line; engine binds
   index N-1) → EC\*.int name. Extracted each from the DAT
   (`DatDump … extract scripts\<name>.int`).
3. Disassembled each (`python3 tools/int_analyze.py` for the external union;
   `/tmp/int_disasm.py` for per-proc bodies).
4. Cross-referenced the external opcode union against Hexwaste's **real**
   `IntVm` switch cases (a `case 0x80XX:` with a body) — NOT just
   `ExternalArity.cs` (which declares arity for ALL of them so an unimplemented
   external falls to the **default arity-stub**: pop `Args`, push 0 if it
   returns, fire `_onStubbedExternal`; IntVm.cs:1327-1340). A "missing" external
   = one in the EC union with no real `case`, i.e. it silently no-ops / returns 0.

### Script roster (Arroyo/Klamath/Den early loop)
| worldmap Script:N | scripts.lst idx (N-1) | EC*.int | role |
|---|---|---|---|
| 617 | 616 | ECRat.int | rats / pig-rats / ants / molerats (dominant Arroyo creature) |
| 616 | 615 | ECScorp.int | small/large radscorpions |
| 615 | 614 | ECGecko.int | silver/golden geckos |
| 614 | 613 | ECPlant.int | spore plants |
| 618 | 617 | ECWarPty.int | war party (hostile humans) |
| 619 | 618 | ECCanibl.int | cannibals (hostile humans) |
| 484 | 483 | ECHunter.int | hunting party |
| 622 | 621 | ECNomad.int | nomads (neutral) |
| 620 | 619 | ECOutCst.int | outcasts |
| 621 | 620 | ECHlyPpl.int | holy people |
| 836 | 835 | ECBHuntr.int | bounty hunters (karma/childkiller-gated; not fresh-player) |
| 624 | 623 | ECBandit.int | Klamath bandits |
| 623 | 622 | ECHomles.int | Klamath homeless |
| 509 | 508 | ECTrappr.int | Klamath trappers |
| 493 | 492 | ECFarmer.int | Klamath farmers |
| 508 | 507 | ECSlaver.int | Den slavers |
| 627/628/629 | 626/627/628 | ECSlvRun/ECSlave/ECRavPty.int | Den slave-run/slaves/rave |
| 625/626 | 624/625 | ECRobber/ECHiwymn.int | Den robbers/highwaymen |
| 258/259/776 | 257/258/775 | ECMrchnt/ECGuard/ECMstDen.int | caravans |
| 1129 | 1128 | ECChild.int | children |

### Census tables

**Implemented externals**: Hexwaste has real `IntVm` cases for **88** externals.
The EC scripts use **70** distinct externals across all 24, **41** in the
fresh-player creature subset.

#### TABLE A — FIRST-LOOP CREATURE SCRIPTS (the canonical fresh-Arroyo loop:
ECRat, ECScorp, ECGecko, ECPlant, ECWarPty, ECCanibl, ECHunter, ECNomad).
These are what a level-1 player actually hits (Arro_D enc_03/13/14/15/16/17/18/19,
ants→ECRat; bounty hunters/Morton are gated OUT for karma=50/childkiller=0).
**Of 41 externals used, 5 are missing — ALL cosmetic/degradable:**

| opcode | name | used by (first-loop) | proc | effect if stubbed-to-0 | verdict |
|---|---|---|---|---|---|
| 0x810C | anim | ECRat/ECScorp/ECGecko | map_enter | `anim(self,1000,random(0,5))` = set spawn rotation; anim 1000 is just `objectSetRotation` (interpreter_extra.cc:3421-3424). | **cosmetic** — critters face a fixed dir instead of random |
| 0x810E | reg_anim_func | ECRat/ECScorp/ECGecko + critter_p_proc | map_enter + wander | sequence-bracket (BEGIN/CLEAR/END, 1/2/3) for the **`!isInCombat()`-gated wander** (interpreter_extra.cc:3455-3473). | **cosmetic** — only the idle-wander loop; no combat effect |
| 0x8122 | poison | ECScorp | combat_p_proc | radscorpion sting: on HIT + failed EN check → `poison(dude, random(3,15))`. Sting DAMAGE still applies; only poison-over-time lost. | **degradable** — no poison system in slice anyway |
| 0x8151 | critter_is_fleeing | (ECNomad here; ECBHuntr/Slaver elsewhere) | combat_p_proc | flee-bark branch; returns 0 = "not fleeing" ⇒ bark skipped. | **cosmetic** |
| 0x8154 | debug_msg | most | various | developer console spew; no game effect. | **no-op** (safe; already silently stubbed) |

**The load-bearing externals are ALL already real:** aggro =
`obj_can_see_obj(self,dude)` (0x80DC) → `attack(dude,…)` (0x80D0); team =
`critter_add_trait(self,1,6,team)` (0x8102); wander move =
`animate_move_obj_to_tile` (0x80CE) + `tile_num_in_direction` (0x80D5) +
`tile_distance_objs` (0x80D3) + `party_member_obj` (0x814B) + `anim_busy`
(0x80E7) + `self_obj`/`dude_obj`/`random`. **⇒ The canonical Arroyo encounter
loop spawns, aggros, and fights with ZERO new externals; the 5 misses are
spawn-rotation, idle-wander, sting-poison, flee-bark, and debug spew.**

Disassembly evidence (ECRat critter_p_proc @0x988):
`obj_can_see_obj(self,dude); if → attack(dude,0,1,0,0,30000,0,0)`. map_enter
@0xa42: `critter_add_trait(self,1,6,124)` then `anim(self,1000,random(0,5))`.

#### TABLE B — ALL 24 EC SCRIPTS (the full early loop incl. human caravan /
trapper / robber / bounty-hunter groups). 70 externals used, **16 missing:**

| opcode | name | used by | where | effect if stubbed | verdict |
|---|---|---|---|---|---|
| 0x810C | anim | ECRat/Scorp/Gecko/Robber | map_enter | spawn rotation / idle | cosmetic |
| 0x810E | reg_anim_func | 7 scripts | map_enter + wander | wander brackets (`!isInCombat`) | cosmetic |
| 0x8122 | poison | ECScorp/ECTrappr | combat_p_proc | poison-on-hit | degradable |
| 0x8151 | critter_is_fleeing | 7 scripts | combat_p_proc | flee bark | cosmetic |
| 0x8154 | debug_msg | 14 scripts | all | console spew | no-op |
| 0x80EC | elevation | ECRobber | map_enter | `elevation(self)==0` gate; all start points are elev:0 so 0 is correct | degradable |
| 0x80BA | obj_is_carrying_obj | ECRobber/ECTrappr | map_enter | spawn inventory-dressing (decides extra loot based on what's carried) | degradable — loot variety only |
| 0x810D | obj_carrying_pid_obj | ECRobber/ECTrappr | map_enter | same dressing path | degradable |
| 0x80C9 | item_subtype | ECGuard/ECRobber/ECTrappr | map_enter | dressing: pick item by subtype | degradable |
| 0x8106 | critter_inven_obj | ECGuard/ECRobber/ECTrappr | map_enter | dressing: inspect own inventory | degradable |
| 0x80DA | wield_obj_critter | ECRobber | map_enter | script self-equip; REDUNDANT — `(wielded)` items are equipped by `wmSetupCritterObjs` via the in-hand flag before the script runs | redundant for hostility |
| 0x80AA | has_skill | ECTrappr | map_enter | dressing branch | degradable |
| 0x80AB | using_skill | ECGuard | map_enter | dressing branch | degradable |
| 0x8136 | gfade_out | ECTrappr | map_enter | screen fade (cinematic) | cosmetic |
| 0x8137 | gfade_in | ECTrappr | map_enter | screen fade | cosmetic |
| 0x8150 | obj_on_screen | ECTrappr | map_enter | visibility query for the fade | cosmetic |

**Structural finding:** ECTrappr (728-instr map_enter) and ECRobber (4563-instr
map_enter) have **NO critter_p_proc / combat_p_proc at all** — their entire logic
is spawn-time **inventory dressing + examine flavor**. All 11 of the "human-group"
missing externals live in `map_enter_p_proc` (or look_at/description), NOT on any
combat path. So even the full early loop spawns and fights with no new combat
externals; the misses only reduce loot-variety and cosmetics.

### The OnStubbedExternal audit recommendation
Hexwaste's `OnStubbedExternal` hook (IntVm.cs:1336, wired phase-6 M0) already
captures every one of these at runtime. **Recommendation: ship M3 spawn WITHOUT
implementing any of the 16; let `OnStubbedExternal` log them during the
golden-transcript encounter fixtures, and confirm none desync the stack** (they
can't — the default path pops `Args` and pushes 0, IntVm.cs:1327-1340; all 16
arities are correct in `ExternalArity.cs`). Then, if any prove load-bearing in a
specific fixture, implement just that one. The likely candidates to implement
opportunistically (all S, all already mappable to existing host state):
- **`elevation`** (0x80EC) — trivial: `PushInt(objectElevation)`; 1 line; would
  also help other scripts. **Do it (S).**
- **`obj_is_carrying_obj` / `obj_carrying_pid_obj` / `critter_inven_obj`** —
  the Vic radio sub-quest (p8-track-b Q4) wants these too; implementing them
  for the companion fold-in lights up encounter loot-dressing for free. **Fold
  into M4/M5 (S-M).**
- **`anim` / `reg_anim_func`** — only worth it if the idle-wander/spawn-facing
  visibly matters; the engine gates them `!isInCombat()`, so in an
  ambush-into-combat encounter they often never fire. **Defer** unless the demo
  looks static.

---

## Cross-cutting / honest flags

- **UNVERIFIED**: I disassembled the externals USED (opcode census) for all 24
  scripts and the full body of the dominant creature scripts (ECRat/ECScorp +
  ECBHuntr/ECWarPty aggro). I did NOT step every instruction of the 728/4563-
  instr ECTrappr/ECRobber map_enter bodies — the missing-external CLASSIFICATION
  for those 11 human-group externals (all in map_enter dressing) is inferred from
  their proc location + arity + the disassembled call sites I sampled, not a full
  operand-by-operand trace. The load-bearing claim (no combat-path miss) is solid
  because those scripts have no combat/critter proc at all.
- **`Bounty_Hunter_*` and `Morton_Brother` groups** (Arro_D enc_04-12) bind
  ECBHuntr (Script:836) but are **gated out for a fresh player** (require
  `Global(1)>1` childkiller OR `Global(0)<-500` karma; defaults are
  childkiller=0, karma=50 per vault13.gam). They are NOT first-loop. Their
  scripts add no missing external beyond Table B (critter_is_fleeing/debug_msg).
- **team_num UB**: re-confirmed teams are NOT from worldmap.txt; bind from
  proto + script `critter_add_trait`. Don't parse `team_num=`.
- **`_getPartyMemberCount`** is read at spawn (worldmap.cc:3708) AND is the
  metarule-16 the companion fold-in needs — implement it once, both tracks use it.
- The Surrounding-formation reachability gate (worldmap.cc:4058) interacts with
  the phase-9 **unreachable-joiner non-termination**: a cornered ambush ring is
  exactly the case that bites. M3 should cap/relax combat-end when a hostile is
  unreachable (the phase-9 spillover note).
