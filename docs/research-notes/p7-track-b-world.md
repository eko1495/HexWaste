# P7 Track B — Wider World research

## Q1. Spatial traps

### Engine mechanics (fallout2-ce, verified)
- **Trigger site**: `animation.cc:2761-2776` `_object_animate()` — only inside the
  per-frame move step: when `_object_move(index)` changes `object->tile`
  (`savedTile != object->tile`), it calls `scriptsExecSpatialProc(object,
  object->tile, object->elevation)` (animation.cc:2774). This is the ONLY runtime
  call site (grep-confirmed). So: triggered by **any object that walks tile-to-tile
  via the animation system** (dude + critters; NOT straight-line/knockback moves —
  `_object_straight_move` branch skips it, NOT teleports via objectSetLocation).
- **Filters** (`scripts.cc:2516-2566 scriptsExecSpatialProc`): skips mouse-cursor
  objects; skips objects with `OBJECT_HIDDEN` or `OBJECT_FLAT` flags; skips
  `tile < 10`; gate `_scr_SpatialsEnabled` — set false during execution (no
  recursive re-entry), also script-controllable via externals
  `scr_spatials_disable/enable` (used by map.cc:973-975 around the first-run
  map_enter so spawning doesn't fire traps).
- **Match**: `builtTile = builtTileCreate(tile, elevation)` (obj_types.h:316 —
  `tile | elevation<<29`); for every spatial script on this elevation
  (`scriptGetFirstSpatialScript(elevation)` filters by
  `builtTileGetElevation(script->sp.built_tile) == elevation`, scripts.cc:2453):
  fire if **exact built_tile match** OR (`sp.radius != 0` AND
  `tileDistanceBetween(builtTileGetTile(built_tile), tile) <= radius`). So
  elevation gating is exact; radius=0 means exact-tile only.
- **On fire**: `scriptSetObjects(sid, triggeringObject, nullptr)` then
  `scriptExecProc(sid, SCRIPT_PROC_SPATIAL)` (proc name `spatial_p_proc`,
  scripts.cc:152). source_obj = walker; **self_obj for spatial scripts**: owner is
  null → `scriptGetSelf` (scripts.cc:~600) lazily creates a HIDDEN+FLAT interface
  object placed at the script's built_tile/elevation — so `tile_num(self_obj)`,
  `elevation(self_obj)`, `tile_distance_objs(self_obj, source_obj)` all work.
- **Re-trigger suppression**: NONE in the engine beyond the during-exec flag —
  scripts self-suppress via LVAR/MVAR (see below).

### SprTrp51.int disassembly (Arroyo caves spear trap, scripts.lst line 31)
`spatial_p_proc` (operand-resolved, /tmp/int_disasm.py):
- Only reacts if `obj_type(source_obj) == 1` (critter).
- Computes perception-based detect distance: `dist_check = PE(source) -
  2*tile_distance_objs(self,source)` (get_critter_stat stat 1 = PERCEPTION).
- Block A (notice the trap, at radius): if `tile_distance_objs <= local0` …
  actually: if dist within range AND LVAR0==0 AND LVAR1==0 → `do_check(source_obj,
  skill 1?  — pushes 1, traps check via do_check)`; on `success` and dude not
  already in death anim: set LVAR0=1, `reg_anim_func(2, source_obj)` (stop),
  `create_object(0x20003B7 /* scenery pid 951 "visible spear trap" */,
  tile_num(self), elevation(self), SID 32)` — i.e. PERCEPTION reveal spawns a
  visible trap scenery object. critical-success/failure branches print
  message_str(30, 100..103) lines.
- Block B (spring it, exact tile): if `tile_distance_objs(self,source)==0 AND
  LVAR1==0 AND MVAR1==0` → game_ui_disable; create_object(0x5000007 = misc pid 7
  spear projectile?, tile 26090, elev) stored in GVAR19 slot used as temp;
  `anim(obj, 1000, rotation_to_tile(...))` = missile flight; if a visible-trap
  scenery (pid 951) is on self tile → destroy it; create sprung-trap scenery
  pid 0x20003B9 (953); `call proc 9` (= Missile_Fired → Check_Hit: roll damage via
  random + critter_damage, give_exp_points); set LVAR1=1, MVAR1=1. **MVAR1=1 is
  the persistent "this trap fired" suppression** (and LVAR1 in-session).
- `use_skill_on_p_proc`: gated on LVAR1==0 AND MVAR1==0 AND
  `action_being_used == 11` (SKILL_TRAPS); `script_overrides`;
  `roll_vs_skill(source_obj, 11, 0)`; success → create disarmed-trap scenery
  pid 0x20003B8 (952), LVAR1=1, MVAR1=1, msg 14:102, +25 xp once (LVAR2 guard);
  critical failure → trap springs at the disarmer (same Missile_Fired path);
  plain failure → msg 14:103 "you fail". Dude path and party-member path are
  duplicated blocks (source_obj == dude vs party member).
- `timed_event_p_proc` exists (re-enables game UI after the missile anim);
  `start` registers nothing notable.

### Wiring plan for Hexwaste
1. **Formats (S)**: `MapFile.ReadScripts` (src/Hexwaste.Formats/Map/MapFile.cs:265)
   already walks spatial records — stop `reader.Skip(8)` for type 1 and store
   `BuiltTile`(=>Tile, Elevation) + `Radius` on `MapScriptRecord` (add two
   nullable ints). SID keys already preserved.
2. **ScriptHost (S)**: build per-elevation spatial list at map load:
   `(sid, tile, elevation, radius)`. Implement `spatial_p_proc` exec: set
   source=walker, self=synthetic hidden object at built_tile (we can fake with a
   lightweight MapObject so tile_num/elevation/tile_distance externals work).
   Add `scr_spatials_disable/enable` externals (opcode 0x80de/0x80df-ish — they
   exist in interpreter_extra) + suppress during map_enter first-run like
   map.cc:973.
3. **Viewer (S)**: hook existing `DudeController.TileChanged` (and the AI-walk
   tile stepper for critters if we want parity; dude-only covers the trap use
   case): on tile change → for each spatial script on this elevation: exact match
   or hex distance <= radius → run proc. With <10 spatial scripts per map a flat
   list beats an index; per-map spatial index unnecessary.
4. **Behavior deps (M)**: script needs externals already real (create_object,
   roll_vs_skill, do_check, critter_damage, give_exp_points, message_str,
   reg_anim_func, game_ui_disable/enable, add_timer_event, tile_contains_pid_obj,
   rotation_to_tile, anim) — most exist per project state. Missing/risky:
   `anim(obj,1000,rot)` missile-flight animation (can degrade to instant),
   `action_being_used` (must return skill id during use_skill_on — verify wired),
   spatial self_obj synthesis, and the **Traps-skill UI path**: player needs a way
   to USE the Traps skill on the revealed trap object (we have use_skill_on
   plumbing for lockpick; needs a skill-picker or hotkey).
- **Estimate refined**: S plumbing holds (parse+host list+TileChanged hook ≈
  half-day). M behavior confirmed but smaller than feared: the trap script runs on
  existing externals; the real M items are spatial-self synthesis, Traps-skill
  invocation UI, and the missile anim degradation choice. Net: **S+M stands,
  closer to M-minus overall**.

## Q2. use_obj_on_p_proc

### Engine call site
`proto_instance.cc:1245 _protinst_use_item_on(critter, targetObj, item)`:
1. Hardcoded medical items FIRST (pid switch: doctor's bag / first aid kit /
   paramedic bag / field medic kit → `skillUse()` with crit modifier; scripts
   never see these; blocked in combat with msg 902).
2. Else if item has NO sid: run **target's** `use_obj_on_p_proc`
   (`scriptSetObjects(target->sid, critter, item)` — source=user,
   **target slot = the item**, which is what `obj_being_used_with` reads);
   if script didn't `script_overrides` → `_protinst_default_use_item`.
3. Else run the **item's** `use_obj_on_p_proc` first (self=item, target=target);
   if its `script->returnValue == 0`, fall through to target's proc as in (2).
Caller chain: `_obj_use_item_on` → action.cc `_action_use_an_object` /
`actionUseAnItemOnAnObject` (the inventory "USE ON" cursor path).

### Temple door explosive — NOT use_obj_on!
The plastic-explosive door in the Temple of Trials is `AIBkDor.int`
("Door w/out a handle In Arroyo Caves", scripts.lst line 35). It has **no
use_obj_on_p_proc at all** — only `damage_p_proc`: if
`metarule(49 /*METARULE_WEAPON_DAMAGE_TYPE*/, target_obj) == 6 /*explosion*/`
→ create broken-door scenery pid 0x20003A5 (933) at self tile, destroy self
(disasm confirmed). The explosive item itself is engine-side: armed timer →
explosion damages nearby objects → damage_p_proc fires with target_obj = the
explosion weapon (metarule 49 handler interpreter_extra.cc:3297 returns
DAMAGE_TYPE_EXPLOSION for the misc explosion FID). So this flow needs the
**explosives/timer/area-damage engine path**, not use_obj_on.
The OTHER temple door `AITemDor.int` DOES use use_obj_on: checks
`obj_being_used_with` pid (the temple key?) → `obj_unlock(self)` + msg.

### Survey of non-stub consumers in opening-hour scripts (Arroyo/Klamath/Den; instr counts)
use_obj_on_p_proc (>16 instrs): diMomGrv/diDadGrv/diAnnGrv 315 (shovel →
grave digging: roll_vs_skill, game_time_advance, gfade in/out), kstill 227
(Klamath still: give parts, rm_obj_from_inven), Door/AICavDor/ASCrlDr 169 +
all K*/D* boxes 113 (generic **crowbar pry** path: obj_being_used_with +
obj_pid + LVAR), AITemDor 190 (key unlock), di*Dor 167 (Den doors:
obj_is_locked check), DCVic 93 (give Vic the radio part — timer based),
Kcbrahmn/ACBrahmn 67 (use knife on brahmin), diStill 75, kcLvatr 103,
sishelf1 102.
use_skill_on_p_proc beyond lockpick: all 18 spear traps 252 (TRAPS disarm,
Q1); ACVillgr 159 + KChild/kcggcust/kcbhcust 23 (**STEAL reaction** —
roll_vs_skill on dude steal attempts), midoor 137, sishelf1 117, ASWell 44
(repair the Arroyo well), Kcbrahmn/ACSporPl/ACBrahmn 42 (skill on critters),
AIChest 37, kc/ZC critters 17 (script_overrides to block skill use on
scorpions/rats/geckos — likely blocks doctor/steal silliness).
NOTE: doctor/first-aid on critters mostly does NOT go through scripts —
engine `skillUse` handles healing (skill.cc), scripts only intercept edge cases.

### What's missing in Hexwaste without use_obj_on
`obj_being_used_with` already implemented (IntVm.cs:902, ExternalArity 0x80C0).
Missing: (a) a viewer "use inventory item on world object" action, (b)
ScriptHost.RunUseObjOn implementing the proto_instance.cc precedence
(item-script → returnValue → target-script → script_overrides → default), (c)
default fallback `_protinst_default_use_item` (keys, crowbar default msg).
Without it: no crowbar prying, no Vic radio quest, no grave digging, no key
unlock of AITemDor — but lockpick/regular open paths still work. Estimate: S
for plumbing (proc id 7, objects already in scriptSetObjects shape) + S for a
minimal "Use on" UI = **M total with UI**, S if triggered via existing
inventory panel.

## Q3. Party member MINIMUM (headline)

### Recruitment paths (disassembled)
**Kcsulik.int Node800** (and DCVic.int Node994 — byte-for-byte same pattern):
1. guard: `critter_state(self) & DAM_DEAD == 0`
2. LVAR12 (follow-distance pref) defaulted to 6; LVAR11 (wait flag) = 0
3. save original team: `LVAR13 = has_trait(TRAIT_OBJECT, self, 6/*OBJECT_TEAM_NUM*/)`
4. `critter_add_trait(self, 1/*TRAIT_OBJECT*/, 6/*TEAM_NUM*/, 0)` → **team 0 = dude's**
5. `party_add(self_obj)` (external 0x8124)
6. `add_timer_event(self, game_ticks(1), 1)`
Leave-party nodes (Node70a/071/072/074, Node1100) do the inverse:
`party_remove(self)` + `critter_add_trait(...team... LVAR13)`.
GVAR side: status GVARs are set in surrounding dialog nodes (not in the join
node itself). **No metarule involved in joining.**

### What party_member_obj must return
interpreter_extra.cc:4671 `opPartyMemberObj` → `partyMemberFindByPid(pid)`
(party_member.cc): scans gPartyMembers, returns the **Object\*** whose pid
matches, else 0. Scripts use it constantly as "is X in my party":
e.g. Kcsulik map_enter_p_proc: `if party_member_obj(0x1000061/*PID_SULIK*/) == 0
→ reset team to 43`; critter_p_proc gates the whole follow loop on it.

### Engine behavior (party_member.cc / combat_ai)
`partyMemberAdd` (party_member.cc:375): dedup by pid; **rewrites object->id =
(pid&0xFFFFFF)+18000** and sid number likewise (stable cross-map identity);
sets `OBJECT_NO_REMOVE|OBJECT_NO_SAVE` (excluded from per-map saves — carried
in party state instead); script flags 0x08|0x10; `critterSetTeam(object, 0)`;
clears queued script events. `partyMemberRemove` (party_member.cc:426)
reverses flags. `_partyMemberSyncPosition` (party_member.cc:796): on map
enter, places members at `tileGetTileInDirection(dude->tile, (dude->rotation
+2 or +4)%6, distance/2)` with distance starting at 2 — i.e. ring around dude.
**Combat**: no special party AI — members fight via normal combat_ai because
team==0; engine only adds them to combat via team matching.

### THE BIG FINDING — follow logic is SCRIPT-side, not engine-side
Kcsulik critter_p_proc (disassembled end-to-end): if in party → betrayal/karma
checks (attack dude if karma < -100 etc.) → if LVAR11 (wait)==0:
`if tile_distance_objs(self,dude) > 3*LVAR12/2 && !anim_busy(self)` → target =
`tile_num_in_direction(tile_num_in_direction(dude_tile,
rotation_to_tile(dude_tile,self_tile), LVAR12), random(0,5), random(0,2))`;
**run** (`animate_move_obj_to_tile(self, t, 1)`) if dist > 2× threshold else
walk; stop (`reg_anim_func(2,self)`) when closer to dude than to target.
Since ScriptHost already runs critter_p_proc at 10 Hz, **following comes free**
once these externals are real: `party_add`/`party_remove` (maintain a list +
set team 0), `party_member_obj` (pid lookup), `animate_move_obj_to_tile`
(A* walk — we have dude pathing; reuse for critters), `anim_busy`,
`reg_anim_func(2)` (stop). "Wait here"/"stay close" dialog commands also work
free (they only set LVAR11/LVAR12).

### Evaluating the cut ("team critter that walks toward dude, no inv/level/dialog mgmt")
WORKS: join/leave dialog nodes (pure party_add/remove + LVARs); follow/run/
wait/distance prefs (script-side); combat on player side (team 0 + existing
same-team AI joiner logic); betrayal checks; Sulik map_enter elevation-follow.
BREAKS / DEGRADES:
- **party.cc level proto swaps** (_partyMemberCopyLevelInfo, party.txt
  level_pids): skipped → followers never gain levels. Cosmetic; safe to cut.
- **Inventory mgmt**: barter-based equip is phase-6 spillover anyway; Vic's
  radio handover is use_obj_on (Q2), not barter — partially recoverable.
- **Map-transition carryover** vs per-map object model — the real exception.
  SIZE: (a) global PartyState {pid, fid, sourceMap+ordinal, CritterState,
  inventory list, script list-index, LVAR array} living beside (not inside)
  per-map deltas; (b) on exit: drop member from current map & record
  "departed-with-player" in that map's delta so revisit doesn't dupe (we
  already have taken/hidden machinery — reuse); (c) on enter: spawn as
  created-style runtime object near dude (sync-position formula), bind script
  with the CARRIED LVAR array (engine does exactly this: _partyMemberPrep*
  copies script->localVars into the party list) — bypasses our map-NAME-keyed
  LVAR import for these sids; (d) on death: party_remove fires from script;
  convert member to a normal created-object corpse delta on the map where it
  died. Estimate for carryover alone: **M (2-4 days)** — touches SaveState
  (shares the Version=2 bump), map load/unload, ScriptHost sid binding.
- Risk: partyMemberAdd's id/sid rewrite — scripts compare via
  party_member_obj, not raw ids, so we can skip the rewrite IF our
  party_member_obj uses the PartyState list. Keep ordinals out of it.
**Honest total: M** (externals S: ~5 simple ones; carryover M; combat reuse S).
NOT L — no engine follow AI to write, no level system needed. Load-bearing
risks: LVAR carry across maps (firstRun semantics on revisit must NOT re-init
member scripts), corpse conversion, and dude-pathing reuse for NPC walk
(animate_move_obj_to_tile must path around blockers or followers wedge in
doorways — accept straight-line fallback + teleport-if-stuck like the engine's
_objPMAttemptPlacement fallback).

## Q4. Random encounters

### worldmap.txt structure (decoded empirically; data\worldmap.txt, 4047 lines)
- `[Data]`: frequency name→% map: Forced=100, Frequent=38, Common=22,
  Uncommon=12, Rare=4, None=0.
- `[Random Maps: Desert|Mountain|City|Ocean]`: per-terrain pool of encounter
  map lookup-names (Desert has 12).
- `[Encounter: NAME]` blocks = critter group compositions:
  `type_NN=ratio:95%, pid:16777227, Script:617` (+ optional `Dead`,
  `Item:NNN`, `Distance:N`, `If (Rand(5%))`), plus
  `position=huddle|straight_line|double_line|wedge|cone, spacing:N`.
- `[Encounter Table N]` (e.g. Table 6 `lookup_name=Arro_M`):
  `maps=Mountain Encounter 1, ...` (overrides terrain pool) and `enc_NN=
  Chance:9%,Enc:(2-4) ARRO_War_Party AMBUSH Player[, If(Global(g) op v) And
  If(Player(Level) op v)]`; specials add `Counter:1,Special,Map:...` (one-shot,
  pin a location on the map, e.g. enc_25 bridge keeper requires Level>9 and
  Global(605)<1).
- `[Tile N]` (21 world tiles, 7x3 grid of 350px): `art_idx`,
  `walk_mask_name`, and 6x6 subtile lines
  `x_y=Terrain,Fill,morningFreq,afternoonFreq,nightFreq,tableLookupName`
  (e.g. Tile 0 `2_4=Desert,No_Fill,Uncommon,Uncommon,Uncommon,Arro_D`).

### Engine roll cadence (worldmap.cc)
Walking loop (`wmTownMapFunc` area ~3026): each UI frame = 1
`wmPartyWalkingStep()` (4-9 steps with car/upgrades); each frame
`wmGameTimeIncrement(18000)` = 18000 ticks = **30 game-minutes per step**.
`wmRndEncounterOccurred()` (worldmap.cc:3322):
- throttled: < 1500 ms REAL time since last check → no roll;
- requires |Δx|>=3 **AND** |Δy|>=3 worldpos movement since last encounter pos
  (engine quirk: AND, so axis-aligned travel rolls less);
- standing on a known area circle (`wmMatchWorldPosToArea` != -1) → never;
- special: Horrigan forced after day 35; sfall forced-encounter hook;
- daypart from hour (>=1800 or <600 night, >=1200 afternoon, else morning);
  `frequency = wmFreqValues[subtile.encounterChance[dayPart]]`, ±frequency/15
  by difficulty; `randomBetween(0,100) < frequency` → encounter.
`wmRndEncounterPick()` (worldmap.cc:3557): filter entries by conditional
(Global/Level/etc.) and counter!=0; weighted pick over Chance values with
luck-5 + Explorer(2)/Ranger(1)/Scout(1) + difficulty ±5 shifting the roll
DOWN the list (later entries are rarer); decrement Counter for one-shots.
Map choice: entry's `Map:` if special, else random from table `maps=`, else
terrain pool. Then `mapLoadById`, and **after map_enter** map.cc:978 calls
`wmSetupRandomEncounter()` (worldmap.cc:3657) which spawns the critter groups
via `wmSetupCritterObjs` at a random start point with the formation/spacing,
sets AMBUSH/FIGHTING hostility, and starts combat if ambushed.

### Minimal model for our worldmap screen
We already do city.txt/maps.txt travel. Add: parse [Data] freqs, [Tile N]
subtile grid (terrain+3 freqs+table name), [Encounter Table] entries
(chance, group spec, If-conditions limited to Global() and Player(Level) —
covers the opening tables), [Encounter:] compositions (ratio/pid/Script/
position). Model: while travel-line advances 1 step/tick (30 game-min), every
N real ms if moved enough: roll vs subtile freq; on hit pick entry (uniform-
weighted is acceptable, luck/perks optional), pick map from pool, load it,
spawn groups at a `random_start_point_N` with simple ring placement around
the point, mark hostile teams, drop dude at the start point. Skip: car, Horrigan,
specials with Counter pins, formations beyond "cluster with spacing".

### What encounter maps need that town maps don't (desert1)
maps.txt `[Map 19] desert1`: `saved=No` (**per-map deltas must NOT persist —
regenerate each visit; also don't allocate a load-order ordinal delta slot**),
`dead_bodies_age=No`, `can_rest_here=No,No,No`, and 5x
`random_start_point_N=elev:0, tile_num:NNNNN` (read by worldmap.cc:2724-2748
into map->startPoints; engine picks one for the party + spawn anchor).
Otherwise desert1.map is a perfectly normal MAP file (parses with existing
MapFile; few/no scripts; exit back to worldmap = walk off edge → exit grids
exist at map borders). NEW for us: honoring `saved=No`, reading
random_start_point list, and spawning script-bound critters NOT present in
the MAP file (create_object-style runtime spawn with Script:NNN binding —
we already create objects at runtime; binding a scripts.lst index to a
spawned critter is the same path RunMapEnter uses for created objects).
Estimate: M for the whole minimal model (parser S, roll loop S, spawn M-).

## Q5. Small correctness items

### (a) NPC position persistence — S, yes
Current MapDelta (src/Hexwaste.Formats/SaveState.cs:50) has Doors/TakenOrdinals/
DeadOrdinals/Created/ContainerInventories/MapVars — no positions. Add
`Dictionary<int, MovedCritter> MovedOrdinals` with
`record MovedCritter(int HexTile, int Elevation, int Rotation)`. Capture on map
exit: any live critter whose (tile,elevation,rotation) differs from its pristine
MAP values (we have the pristine map in hand at capture time, or store original
on the runtime object at load). Replay: **AFTER map_enter runs** — map_enter
walkers (e.g. Kcsulik map_enter_p_proc does `move_to(self, tile, dude_elev)`
on elevation mismatch) would otherwise fight the delta; applying moved-ordinals
last matches our existing "container snapshot overwrites restock" policy and the
engine's behavior (engine saves full object state, so saved position always
wins; map_enter moves only happen when scripts decide to, and they operate on
the restored position — minor divergence, acceptable: scripts that ALWAYS
reposition (rare; gated on LVARs/firstRun) will be overridden by our delta —
mitigate by replaying deltas first and letting map_enter move afterwards for
critters whose script actually called move_to this run... simplest correct
order: replay delta positions BEFORE map_enter (it sees real positions, may
re-move — engine-faithful). Choose BEFORE.) Cost: capture ~15 lines, replay
~10, ordinal mapping already exists for taken/dead. **S.**

### (b) override_map_start — S, yes
`opOverrideMapStart` (interpreter_extra.cc:522): pops rotation, elevation, y, x;
`tile = 200*y + x`; sets dude rotation (objectSetRotation), moves dude
(objectSetLocation — on failure restores previous tile), recenters camera
(tileSetCenter + refresh). Called from map scripts' map_enter_p_proc to place
the dude at the correct entrance. Our wiring: ExternalArity already declares
0x80A9/4 args (ExternalArity.cs:27). Implement in the VM host callback: during
RunMapEnter, convert (x,y,elev,rot) → tile=200*y+x, then OVERWRITE the pending
MapDestination/dude placement and recenter camera. Since RunMapEnter executes
after initial dude placement in LoadMap (ViewerGame.LoadMap/ApplyTransition,
ViewerGame.cs:672/2655), just set dude tile+elevation+rotation directly and
recentre; no save format impact. **S.**

### Shared Version=2 bump
One bump covers: MovedOrdinals dict (a) + ranged-ammo fields (other track).
override_map_start needs no save change. Loader: Version==1 saves load with
empty MovedOrdinals (JSON default) — backward compatible; bump is mostly
declarative (CurrentVersion const, SaveState.cs:19).
