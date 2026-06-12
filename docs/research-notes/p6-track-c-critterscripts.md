# Phase 6 Track C — Critter scripts in combat: critter_p_proc / destroy_p_proc / damage_p_proc + spatial/timed map scripts

Reference tree: `/home/eko/dev/FPOC/reference/fallout2-ce/src` (cited as `file:line`).
Disassembly: real `.int` files extracted from the user's game data into `/tmp/ints/*.int`,
listed with `/tmp/int_disasm.py` (extends `tools/int_analyze.py`; push operands resolved).
Map script sections dumped with `/tmp/mapscan` (standalone C# tool referencing the built
`Hexwaste.Formats.dll`; repo untouched).

---

## 1. Engine semantics (line-cited)

### 1.1 critter_p_proc — heartbeat, cadence, gating

- Registration: `_scr_game_init()` does `tickersAdd(_doBkProcesses)` — `scripts.cc:1598`.
  Tickers run once per game-loop iteration: `inputGetInput()` → `tickersExecute()`
  (`input.cc:188`), i.e. **every frame**.
- `_doBkProcesses` (`scripts.cc:674`) calls `_script_chk_critters()` + `_script_chk_timed_events()`
  only when `gScriptsEnabled && _script_engine_run_critters && !_gdialogActive() && !gameMovieIsPlaying()`
  (`scripts.cc:694-699`; the movie check is an SFALL fix, comment at `scripts.cc:695`).
- `_script_chk_critters` (`scripts.cc:705`):
  - additional gate `if (!_gdialogActive() && !isInCombat())` (`scripts.cc:707`) —
    **critter_p_proc never runs during dialog or combat**;
  - **round-robin: exactly ONE critter script per frame**: a static counter `_count_` walks
    the `SCRIPT_TYPE_CRITTER` list, wraps at list size (`scripts.cc:721-726`), then
    `scriptExecProc(script->sid, proc)` (`scripts.cc:740`). The
    `proc = isInCombat() ? SCRIPT_PROC_COMBAT : SCRIPT_PROC_CRITTER` at `scripts.cc:726`
    is dead code for the COMBAT branch (outer gate already excluded combat).
  - **No distance gating, no per-critter timer** — with N scripted critters on the map,
    each one's critter_p_proc runs every N frames.
- combat_p_proc (for contrast) is run from combat itself: at a critter's turn start with
  fixedParam=4 (`combat.cc:3245-3247`), and on the attacker after a successful hit with
  fixedParam=2, target = victim (`combat.cc:4729-4732`, `combat.cc:4755-4758`), plus the
  map script via `_scr_end_combat()` (`scripts.cc:2864`).

### 1.2 damage_p_proc — callers and fixedParam

- Main path `_damage_object()` (`combat.cc:4821`, called from `_apply_damage`): after HP is
  subtracted, **fixedParam = damage dealt**, then
  `scriptExecProc(a1->sid, SCRIPT_PROC_DAMAGE)` (`combat.cc:4850-4851`).
  Skipped when attacker and victim are both party members (`combat.cc:4848`) and when the
  damage was "a4" (the oops/self path, `combat.cc:4847`).
- Non-critter defender path (walls/scenery shot at): fixedParam = attackerDamage,
  `scriptSetObjects(sid, attacker, weapon)` → source = attacker, target = weapon, then
  `SCRIPT_PROC_DAMAGE` (`combat.cc:4699-4701`).
- Explosions: `_scr_explode_scenery()` runs SCRIPT_PROC_DAMAGE with **fixedParam = 20**
  and target = initiator on every item/spatial script within radius (`scripts.cc:2904-2949`).

### 1.3 destroy_p_proc — call site and the XP path

- `_damage_object()`, when the victim ends up DAM_DEAD (`combat.cc:4855-4882`):
  1. `scriptSetObjects(sid, whoHitMe, nullptr)` — **source_obj = killer** (`combat.cc:4856`);
  2. `scriptExecProc(sid, SCRIPT_PROC_DESTROY)` (`combat.cc:4857`);
  3. **XP is engine-side**: if victim != dude and `whoHitMe == gDude || whoHitMe->team == gDude->team`,
     and the script did **not** call `script_overrides`,
     `_combat_exps += critterGetExp(a1); killsIncByType(...)` (`combat.cc:4860-4872`).
     `critterGetExp` returns `proto->critter.data.experience` (`critter.cc:920-925`).
     The accumulated `_combat_exps` is paid to the PC at **combat end**:
     `_combat_over()` → `_combat_give_exps(_combat_exps)` (`combat.cc:2816`) →
     `pcAddExperience` + "you earn %d exp." message (`combat.cc:2857+`).
     So: **engine awards proto XP; destroy_p_proc only adds scripted bonus XP via
     give_exp_points (= pcAddExperience directly, `interpreter_extra.cc:465-472`)**, and a
     script can veto the engine XP/kill-count with `script_overrides`.
  4. `scriptRemove(sid); a1->sid = -1` (`combat.cc:4876-4879`) — **the dead critter's script
     is removed immediately**; no proc of any kind ever runs again for it.
- Other destroy call sites:
  - `_combatKillCritterOutsideCombat()` runs SCRIPT_PROC_DESTROY then `critterKill`
    (`combat.cc:6014-6020`);
  - `critterKill()` itself does NOT run destroy, it only removes the script
    (`critter.cc:897-900`);
  - `_obj_remove()` (object deletion) runs SCRIPT_PROC_DESTROY before `scriptRemove`
    (`object.cc:3900-3906`).

### 1.4 Dead critters on map revisit

`scriptsExecMapUpdateScripts(SCRIPT_PROC_MAP_ENTER)` runs map_enter for **every** script of
**every type** that has the proc, with **fixedParam = first-run flag** `(gMapHeader.flags & 1)==0`
(`scripts.cc:2600-2670`, fixedParam at 2608-2613); there is **no aliveness check**. The engine
"skips" dead critters purely because death removed their script (`combat.cc:4876-4879`) and the
post-visit map (SAVE.DAT copy) stores the corpse with sid = -1. **Consequence for Hexwaste:
since we reload pristine maps + deltas, our delta must record "script destroyed" per critter,
or dead critters would re-run map_enter/critter procs on revisit.**

### 1.5 Spatial scripts — trigger model

- Trigger: inside the per-frame animation stepper `_object_animate()` (`animation.cc:2740`),
  whenever a moving object's tile actually changes during `_object_move`,
  `scriptsExecSpatialProc(object, object->tile, object->elevation)` (`animation.cc:2770-2775`).
  So **any non-flat, non-hidden object that walks onto a new tile** triggers spatials, not
  just the dude (geckos can step on traps).
- `scriptsExecSpatialProc` (`scripts.cc:2516-2566`):
  - rejects mouse cursors, hidden/flat objects, `tile < 10`;
  - re-entrancy guard `_scr_SpatialsEnabled` (set false while running, `scripts.cc:2538`);
  - iterates spatial scripts **of that elevation**; hit if `builtTile == script->sp.built_tile`
    or `radius != 0 && tileDistanceBetween(builtTile's tile, tile) <= radius`;
  - `scriptSetObjects(sid, object, nullptr)` — **source_obj = the stepping object** — then
    `scriptExecProc(sid, SCRIPT_PROC_SPATIAL)` (`scripts.cc:2560`).
- There is no "exit radius" event and no polling — strictly edge-triggered on tile change.

### 1.6 Timed map scripts (type 2)

`_script_chk_timed_events` (`scripts.cc:745+`) drives the generic queue (game-time advance +
`queueProcessEvents`), and map_update fires every 30 s of wall ticks (`scripts.cc:757-760`).
But see §4: **no retail FO2 map contains a single type-2 (timed) script record**, so the
records MapFile currently discards are dead weight in practice.

---

## 2. Empirical survey — 11 scripts disassembled

Scripts found via map script sections (`/tmp/mapscan` output) + `scripts\scripts.lst`
(1302 entries, 0-based). Map placements verified from the actual MAP files:

| map | scripted critters (examples) | spatial |
|---|---|---|
| artemple.map | 1 critter: ACKlint (idx 750); 2 items: Animfrvr | 0 |
| arcaves.map | 11×ZClRat(18)/ZClScorp(19) e0–e1; e2: ACTemVil(748), more scorpions | **18** (4×SprTrp5x r=10, 14×ATSrTrpX r=5, all elev 2) |
| klatrap.map | 9×ZCLGecko(270 golden), 4×ZCGecko(269) | 0 |
| denbus1.map | 70 critter records: DCAddict(35)×~20, DCThug(38), dcG2Grd(36), DCTubby, DCFlick, dcRebecc… | 0 |
| denbus2.map | 86: dcMetzge(45), DCSlaver(46), dcTyler, dcLara, DCMom… | 0 |

### 2.1 The dungeon-critter template (ZClScorp = ZClRat = ZCGecko = ZCLGecko)

`ZClScorp.int` critter_p_proc — **complete listing**:

```
0xa66 push_base
0xa68 push 0xAA4(else)        ; jump target for if
0xa6e self_obj
0xa70 dude_obj
0xa72 obj_can_see_obj          ; perception + straight-path LOS (interpreter_extra.cc:1783)
0xa74 if
0xa76 dude_obj                 ; attack(dude, 0,1,0,0,30000,0,0)
0xa78..0xaa2 push 0,1,0,0,30000,0,0 ; attack   <- op 0x80D0 opAttackComplex
0xaa4 ... ret
```

**Unprovoked aggro IS script-driven**: `if obj_can_see_obj(self_obj, dude_obj) then
attack(dude_obj)`. `opAttackComplex` (`interpreter_extra.cc:1813-1890`) outside combat
builds a CombatStartData (attacker=self, defender=target, maxDamage=30000) and calls
`scriptsRequestCombat()` (`scripts.cc:1100`) — i.e. the script *starts combat*; inside
combat it just sets `maneuver |= ENGAGING; whoHitMe = target`. The engine's own
team-data logic (`_combatai_check_retaliation`, same-team joining) only acts *after*
combat exists or after damage — **on-sight hostility comes only from critter_p_proc.**

- damage_p_proc (ZClScorp): `if source_obj == dude_obj then attack(dude)` — retaliation
  backup. ZCGecko/ZCLGecko attack `source_obj` unconditionally.
- map_enter_p_proc (ZClScorp): `critter_add_trait(self,1,6,4)` = set TEAM 4,
  `critter_add_trait(self,1,5,8)` = set AI packet 8 (kind/param semantics:
  `interpreter_extra.cc:2859-2920`, CRITTER_TRAIT_OBJECT_TEAM=6 / _AI_PACKET=5), then
  `add_timer_event(self, game_ticks(random(1,5)), 0)` (fidget loop; rat: team 3/AI 7,
  geckos: team 49/AI 26, **gecko also does `store_global` GVAR-ish init**).
  **Teams are assigned by script at map_enter, not stored in the MAP critter block** —
  relevant to our team-arithmetic combat.
- destroy_p_proc:
  - ZClScorp: `create_object(92,0,0,-1)` + `add_obj_to_inven(self_obj, it)` — drops loot
    item PID 92 into its own corpse. No give_exp_points, no GVARs.
  - ZCLGecko/ZCGecko: `if has_trait(0, dude_obj, 73)` (perk 73 = Gecko Skinning;
    CRITTER_TRAIT_PERK path `interpreter_extra.cc:2570-2576`) `then create_object(276/277…)
    + add_obj_to_inven(self)` — pelt drop gated on the perk.
  - ZClRat: empty (prologue/epilogue only).
- combat_p_proc (ZClScorp): `if fixed_param==2 and target_obj==dude and not
  success(do_check(dude, STAT_ENDURANCE=6? (arg 6), -1)) then poison(target_obj, random(1,6))`
  — on-hit poison. ZCGecko: same shape but `radiation_inc(target, random(...))` scaled by
  `get_pc_stat(1)` and metarule(46,0) (game difficulty).

### 2.2 Town NPCs (DCAddict, DCThug, dcG2Grd, dcMetzge — Den)

critter_p_proc template (quoted from DCThug, identical shape in DCAddict/dcG2Grd/dcMetzge):

```
if obj_can_see_obj(self_obj, dude_obj) then
  if (local_var(4) bwand 2) != 0 or (global_var(447) bwand 0x4000) != 0 then  ; "I am hostile" LVAR bit, or town-hostile GVAR bit
    if critter_is_fleeing(self_obj) then
      if not anim_busy(self) and (tile_distance_objs(self,dude) < 8 or obj_can_see_obj(self,dude)) then
        loop: animate_move_obj_to_tile(self, tile_num_in_direction(tile_num(self), away-rotation, random(3,10)), RUN)
    else
      attack(dude_obj, 0,1,0,0,30000,0,0)
```

(DCAddict additionally gates on `has_trait(1, self_obj, 666)` =
CRITTER_TRAIT_OBJECT_IS_INVISIBLE → "self is visible" — `interpreter_extra.cc:2596-2598`,
scripts.h:113; Metzger's hostile bit is `global_var(446) bwand 0x1000000`.)
**Town aggro = LVAR/GVAR-gated script attack; fleeing = script-driven wander-away.**

- destroy_p_proc (DCThug/dcG2Grd/ACKlint/ACTemVil): the standard reputation macro block —
  `if source_obj == dude_obj`: increment "evil/good kills" GVARs (4 or 5), and when
  `metarule(51, self) == 2` (METARULE_CRITTER_KILL_TYPE == KILL_TYPE_MAN,
  `interpreter_extra.cc:3328`): GVAR_1++ (men killed), GVAR_0 -= 15/-10 (reputation),
  GVAR_47 -= 5..8 (karma-ish), recompute rep-flag GVARs 37–45 via threshold ladder.
  **No XP anywhere** — kill XP for ordinary NPCs is pure engine/proto.
- destroy_p_proc (dcMetzge, boss-ish): quest mutations + bonus XP:
  `if global_var(100) < 2 then set_global_var(100, 2)` (Metzger dead);
  `set_global_var(445, global_var(445) bwor 0x8000000)` (town flag);
  rep ladder; `display_msg(message_str(...,800))`; **`give_exp_points(1500)`** + "you gain
  1500 exp" message; sets GVAR_470 from `game_time + 2*24*…` (timer for slave-run state);
  also `move_obj_inven_to_obj` (loot transfer).
- damage_p_proc (ACKlint): `if source_obj == dude_obj then set_local_var(6,1);
  set_global_var(7,1)` — pure quest/hostility bookkeeping. dcMetzge damage:
  `is_in_combat()`-gated variant. **For opening-area gameplay, damage_p_proc is mostly a
  retaliation/bookkeeping backup — our whoHitMe arithmetic already approximates the
  retaliation half.**
- ACKlint critter_p_proc: `if local_var(5) == 2 and obj_can_see_obj(self,dude) then
  set_local_var(5,1); attack(dude)` — only hostile if dialog set LVAR_5=2.
- ACTemVil (Cameron) critter_p_proc: `if local_var(13)==1 → attack(dude)`; second branch
  LVAR_5==2 + can_see → call Node032 then attack; third branch starts auto-dialog
  (`dialogue_system_enter`) when dude within 6 hexes on map index 3. Its combat_p_proc
  ends the sparring match via `terminate_combat` when HP low — boss-fight-ish logic that
  exists only in procs we don't run.

### 2.3 Spatial trap scripts (ATSrTrp0 ≙ SprTrp50; opcode streams are byte-identical apart from push constants)

`ATSrTrp0.int` (Temple of Trials spear trap, arcaves elev 2, radius 5) — spatial_p_proc:

```
if obj_type(source_obj) == 1 (critter) then
  detectDist := get_critter_stat(source_obj, 1=PE) + 0
  mod        := detectDist - tile_distance_objs(self, source) * 2
  if tile_distance_objs(self,source) <= detectDist and LVAR_0==0 and LVAR_1==0 then
    roll := do_check(source_obj, 1, mod)          ; PE check to notice the trap
    if success(roll) → display "you notice a trap" (msg 30:100/101, only if source==dude or visible)
    else: set LVAR_0=1; reg_anim_func(2, source_obj)            ; stop the walker
          spear := create_object(PID 0x20003B7, tile_num(self), elevation(self), sid 173)
          → Missile_Fired/Check_Hit procs: anim() the spear, then
            critter_damage(target, damage, dmgflags 0x100|0x200)  ; op 0x80EF
```

use_skill_on_p_proc (TRAPS skill): disarm roll; on first success `set_local_var(2,1)` +
**`give_exp_points(25)`** + "+25 exp" float (`0xea0-0xea6`, second copy `0x1020-0x1026`).
So traps award scripted XP on disarm, damage via `critter_damage`, and are *one-shot*
(LVAR latch).

---

## 3. What's missing as stubs / what lights up if wired

Static gap analysis (`/tmp/gap.py`): per-proc external usage of all 11 scripts vs the 71
externals actually implemented in `IntVm.ExecuteExternal` (`src/Hexwaste.Formats/Int/IntVm.cs`).
Note: **ViewerGame never assigns `ScriptHost.OnStubbedExternal`** (`ViewerGame.cs:257-269`),
so the headless run (`arcaves.map`, exit 0, screenshot OK) printed no stub hits — the log
sink exists but is unhooked; evidence above is from disassembly, which is exhaustive anyway.

(a) **Missing because critter_p_proc never runs**
  - On-sight aggro for ALL dungeon critters (rats/scorpions/geckos: unconditional
    `can_see → attack`) — currently they never start combat first.
  - Conditional town hostility (LVAR-bit / town-GVAR-bit triggered mobs: Den thugs after
    you anger a faction, Metzger after GVAR_446 bit set).
  - Script-driven fleeing (critter_is_fleeing branch: run away from dude).
  - Random wander for geckos (1-in-200 / 1-in-2000 `animate_move_obj_to_tile`) — partly
    faked today by the documented phase-3 wander.
  - Auto-dialog ambush (ACTemVil `dialogue_system_enter` within 6 hexes).
  - No floats in these particular critter procs (floats live mostly in timed_event_p_proc,
    which we already run).

(b) **What destroy_p_proc adds**
  - Quest/reputation GVARs on kills (kill counters GVAR 4/5, rep GVAR 0/47, flags 37–45;
    Metzger: GVAR_100=2 + town bit 0x8000000 in GVAR_445).
  - Scripted loot drops (scorpion part PID 92; gecko pelts 276/277 gated on perk 73).
  - Boss bonus XP via give_exp_points (Metzger 1500) — **base kill XP must stay
    engine-side from proto exp** (combat.cc:4869-4872), accumulated and paid at combat end,
    suppressed when the script called script_overrides (we already track Overridden).

(c) **damage_p_proc in opening areas**: low value — dungeon-critter versions just
  re-attack the attacker (our whoHitMe/team arithmetic already produces that), town
  versions write 1-2 quest vars (ACKlint LVAR_6/GVAR_7). Wire it cheaply or defer.

**New externals needed (union across surveyed procs, opcode + #procs using it):**
attack 0x80D0 (14) · critter_add_trait 0x8102 (14) · has_trait 0x80F3 (12) ·
debug_msg 0x8154 (17, no-op) · rotation_to_tile 0x814C (9) · elevation 0x80EC (6) ·
is_in_combat 0x8128 (6) · anim_busy 0x80E7 (5) · anim 0x810C (5) · critter_is_fleeing 0x8151 (4) ·
do_check 0x80AE (4) · give_exp_points 0x80A1 (4) · get_critter_stat 0x80CA (3) ·
reg_anim_func 0x810E (3) · tile_contains_obj_pid 0x80BB (3) · use_obj_on_obj 0x8145 (3) ·
opGetCritterState 0x80FB (2) · dialogue_system_enter 0x80F9 (2) · then 1-use each:
critter_damage 0x80EF, poison 0x8122, radiation_inc 0x80FD, get_pc_stat 0x80A6,
proto_data 0x8104, obj_art_fid 0x8149, art_anim 0x814A, play_sfx 0x80A3,
game_ui_disable/enable 0x8133/0x8134, move_obj_inven_to_obj 0x8147, terminate_combat 0x8153.

---

## 4. Spatial + timed records in the data

- **artemple.map: 0 spatial, 0 timed. denbus1.map: 0/0. denbus2.map: 0/0.**
  The Temple traps live in **arcaves.map elevation 2: 18 spatial scripts**
  (SprTrp50–53 radius 10, ATSrTrp0–E radius 5 — the spear traps disassembled in §2.3).
- Full sweep of every `.map` in the VFS (`/tmp/spatial-sweep.txt`): only **28 of ~160 maps
  have spatial scripts** (max: Raiders2 91, Navarro 38, depolvA 36, klatoxcv 20, arcaves 18),
  and **zero maps contain a type-2 timed script record** — discarding timed records is
  empirically harmless; spatial records are needed for arcaves/klatoxcv-class content.
- Trigger model to port (S): on any walker's tile change (dude included), match
  same-elevation spatial records by exact built-tile or radius, set source = mover, run
  spatial_p_proc once with a re-entrancy latch (`scripts.cc:2516-2566`, `animation.cc:2770-2775`).
  Trigger plumbing itself is **S**; making the *trap scripts* fully behave (critter_damage,
  do_check, PE stat, spear animation) pushes the milestone to **M**.

## 5. Wiring cost in our architecture (ScriptHost.RunObjectProc pattern)

| item | size | plan + new externals |
|---|---|---|
| critter_p_proc heartbeat | **M** | Pump like `PumpTimers` (ScriptHost.cs:177): round-robin ONE scripted critter per tick/frame (engine: scripts.cc:705-740), gated `!dialog && !combat` and skip dead/script-removed. Needs REAL: `attack` (→ new callback `AttackRequested(self,target)` into ViewerGame hostility, mirroring opAttackComplex semantics incl. "no-op if target fleeing/dead/in-dialog"), `critter_add_trait` (write CritterState team/AI — supersedes MAP team ints), `has_trait` (perk rank + OBJECT team/AI/visible), `anim_busy` (walker-busy query), `critter_is_fleeing` (false initially), `rotation_to_tile`/`elevation` (pure geometry), `is_in_combat`, `debug_msg` (no-op). |
| destroy_p_proc on KillCritter | **S** | In `ViewerGame.KillCritter` (ViewerGame.cs:1523) before corpse conversion: set source=killer (combat.cc:4856) and run `destroy_p_proc`; if `!result.Overridden`, award proto XP (engine path combat.cc:4860-4872 — accumulate, pay at combat end); then mark the script dead in the per-map delta (engine removes sid, combat.cc:4876-4879). Needs: `give_exp_points`, `has_trait`, `move_obj_inven_to_obj`; create_object/add_obj_to_inven already real. |
| damage_p_proc on ResolveAttack | **S** | In `ResolveAttack` (ViewerGame.cs:1490) after HP subtraction, non-party-pair only: fixedParam=damage, source=attacker, target=weapon (combat.cc:4845-4851). Reuses the heartbeat external set (attack, is_in_combat). |
| spatial triggers | **S** (plumbing) / **M** (trap behavior) | Stop discarding `built_tile`/`radius` in `MapFile.ReadScripts` (MapFile.cs:281-287); hook every `TileChanged` (dude ViewerGame.cs:983, walkers 1193/1235). Trap behavior additionally needs: `do_check`/`success`/`critical` real rolls, `critter_damage`, `get_critter_stat` (stats exist in CritterState, just not exposed to the VM), `proto_data`, `obj_art_fid`/`art_anim`, `reg_anim_func`/`anim` (visual stubs OK), `game_ui_disable/enable` (no-op), `play_sfx` (audio exists). |
| timed map scripts | **skip** | Zero records in all retail maps (sweep §4). |

Also recommended: hook `OnStubbedExternal` in ViewerGame (one line) so future runs produce
the stub-hit telemetry the prompt expected.

### Unverified / flagged
- Loot PID names (92 = radscorpion part, 276/277 = gecko pelts) inferred from context, not
  cross-checked against proto msg files.
- ZCGecko combat_p_proc stat argument decoded as "6" for do_check — stat id not name-resolved.
- The `create_object(..., sid 173)` in ATSrTrp0 (spear projectile inherits script index 173)
  read directly from the listing; runtime effect not traced further than Missile_Fired/Check_Hit.
- artemple.map's relation to the Temple of Trials interior (the actual ant/scorpion/trap
  content is in arcaves.map per the data; map-name lore not re-verified against maps.txt).
