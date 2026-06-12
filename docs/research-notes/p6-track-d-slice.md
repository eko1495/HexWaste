# Phase-6 Track D — Playability Vertical Slice audit (opening hour: Arroyo → Temple → Klamath/Den)

Date: 2026-06-12. Method: empirical, headless `Hexwaste.Viewer` runs against real GOG data
(`/home/eko/dev/FPOC/game-data`), script disassembly via `tools/int_analyze.py`, stub histograms via a
C# probe (`/tmp/p6d-probe`) on `ScriptHost.OnStubbedExternal`. Engine claims cite
`reference/fallout2-ce/src`. Artifacts: `/tmp/p6d/` (screenshots, logs, disassemblies, histograms).

## 0. Meta-finding: the viewer does NOT log stubbed externals

The task brief said "stderr logs every stubbed script external" — **false today**.
`ViewerGame.LoadContent` (src/Hexwaste.Viewer/ViewerGame.cs:257-268) never sets
`ScriptHost.OnStubbedExternal`; all 10 opening maps produced **0 stderr lines**. The histograms below
came from a probe project that wires the callback. Wiring it to `Console.Error` in the viewer is a
one-line diagnostic win.

## 1. Map-by-map sweep

All 10 maps load, render and screenshot cleanly, exit 0: artemple, arvillag, arbridge, argarden,
arcaves, klamall, kladwtwn, klatrap, denbus1, denbus2. No parse errors, no visual breaks observed
(screenshots /tmp/p6d/*.png; arcaves interior, Klamath mall pens, Den streets all correct).

Flow confirmed from data: new game = `artemple.map` (fallout2-ce src/main.cc:62 `_mainMap`),
exit grids → `arcaves.map` (maps.txt index 3 = Temple of Trials interior, 3 elevations) → elev 2 exit
→ `arvillag.map` (index 4). arbridge exits to worldmap (dest map −2); kladwtwn/denbus1/denbus2 all
have worldmap exit grids.

Stub histograms (probe: map_enter + one pass of critter_p_proc + talk_p_proc per critter;
full data /tmp/p6d/stub-histograms.txt). Top stubs per map:

| map | map_enter top stubs | talk/critter top stubs |
|---|---|---|
| artemple | reg_anim_func×4, critter_add_trait×2, set_light_level, override_map_start, tile_contains_obj_pid | get_critter_stat, has_trait, obj_art_fid |
| arvillag | **critter_add_trait×46**, play_gmovie, give_exp_points | get_critter_stat×42, has_trait×28, get_poison, has_skill |
| arbridge | critter_add_trait×6, mark_area_known | get_critter_stat×9, has_trait×6 |
| argarden | critter_attempt_placement×6, critter_add_trait×2 | has_trait×2 |
| arcaves | critter_add_trait×44, reg_anim_func×4 | **attack×2** (critter_p_proc wants to attack!), get_critter_stat |
| klamall | critter_add_trait×62, elevation | get_critter_stat×15, has_trait×14 |
| kladwtwn | critter_add_trait×77, party_member_obj | get_critter_stat×32, move_obj_inven_to_obj×4, **fetch_external: klam_sajag_box_obj / klam_bucknr_box_obj** |
| klatrap | critter_add_trait×26 | (none) |
| denbus1 | **rotation_to_tile×395**, critter_add_trait×133, has_trait×43, use_obj_on_obj×27, tile_in_tile_rect×20, override_map_start | fetch_external: becky_guard_obj / becky_door_obj / den_tubby_box_obj, critter_inven_obj, move_obj_inven_to_obj |
| denbus2 | critter_add_trait×156, has_trait×85, use_obj_on_obj×22, fetch_external: gang_2_member_2 | get_critter_stat×27, move_obj_inven_to_obj×8, party_member_obj, do_check |

Recurring offenders across the slice: **critter_add_trait** (sets team/AI packet —
interpreter_extra.cc opCritterAddTrait; stubbing it means scripted team assignments are lost),
**get_critter_stat / has_trait / has_skill / do_check** (all dialog stat-gating),
**fetch_external** (imported/exported cross-script variables, fallout2-ce src/export.cc),
**move_obj_inven_to_obj** (NPC gives/takes items in dialog), **give_exp_points**, **play_gmovie**,
**override_map_start**, **party_member_obj**.

## 2. Scripted progression blockers — empirical transcripts

Scripted-object intel (probe `scripts` command, /tmp/p6d/scripts-listing.txt):
- artemple: ACKlint (Klint) hex 21101; map script ARTemple (uses stubbed override_map_start).
- arcaves elev 0: DoorL100 locked door hex 11108; 5×ZClScorp + 6×ZClRat. Elev 1: **AIBkDor** hex
  14322 (the "blow it open" door — its ONLY proc is `damage_p_proc`: create_object+destroy_object,
  i.e. rubble swap; no use_p_proc at all). Elev 2: 2×AITemDor final doors (13528/19928),
  **ACTemVil = Cameron** hex 13728.
- arvillag: AHElder 18915, AHHakun 14109. arbridge: ACMynoc 19704.
- denbus1: DCTubby 16910, DCFlick 27083, dcRebecc (Becky) 17662 + dcRebGrd/diRebDor web.
  denbus2: DCVic 17070 behind diVicDor + dcVicGrd, dcMetzge 15278.

Engine fact (read from src): the viewer only ever runs `map_enter / talk / use / use_skill_on /
pickup / timed_event / description / look_at` procs. **Never run: critter_p_proc, destroy_p_proc,
damage_p_proc, use_obj_on_p_proc, combat_p_proc, map_update_p_proc, spatial.** (grep of
ViewerGame.cs + ScriptHost.cs; fallout2-ce runs critter procs round-robin in
`_script_chk_critters`, scripts.cc:705, gated off dialog/combat; destroy/damage from combat.cc:4701/4857/6018.)

### Temple of Trials
- **Fight scorpions/rats — WORKS (combat) but unwinnable and optional.**
  `--map arcaves.map --fight 20529 --rng-seed 42` → real to-hit/damage rolls, a second scorpion
  joins (team rule), `fight-result: rounds=6 dudeHp=0 gameOver=True targetDead=True hostilesLeft=1`.
  The level-1 hmwarr dude loses to 2 scorpions. **And critters never aggro on their own** (no
  critter_p_proc heartbeat — probe shows arcaves critter procs immediately call the stubbed
  `attack` external), so the temple is a peaceful stroll unless the player starts a fight.
- **Locked mid-temple door (DoorL100, elev 0 hex 11108) — WORKS TODAY.**
  `--use-hex 11108` → `locked=True open=False`; `--lockpick-hex 11108` → `locked=False`. 
- **Explosives door (AIBkDor, elev 1 hex 14322) — PASSABLE via sequence break.**
  With the door untouched, `--goto 13918` (exit grid to elev 2) = "no path". After `--use-hex 14322`
  the same walk **succeeds** (transition to elev 2 fired) — the viewer's native door toggle opens a
  door whose script provides no use_p_proc. Canonical mechanism (plant plastic explosives → timed
  explosion → damage_p_proc swaps door for rubble) NEEDS: usable explosive item + timer +
  `explosion`/`damage_p_proc` (fallout2-ce action.cc `actionExplode`, queue.cc EVENT_TYPE_EXPLOSION).
- **Final door + Cameron — dialog WORKS, fight path BROKEN, lockpick bypass WORKS.**
  Disassembly: Cameron opens the door in `critter_p_proc` (obj_unlock+obj_open after dialog LVAR) and
  in `destroy_p_proc` (sets map var read by AITemDor.map_enter) — neither proc is ever run.
  Empirical: `--talk-hex 13728` → correct reply ("…final challenge… unarmed combat"), smart options
  shown; `--choose 2` ("Sure, let's party") → dialog ends, **no combat starts**. `--fight 13728` →
  dude dies (rounds=7). Even when the target dies, no destroy_p_proc → door stays locked.
  BUT `--lockpick-hex 13528` → unlocked, and the full chain
  `lockpick → --goto 13326 → "travelling to arvillag.map"` **completes the temple end-to-end today**.
- **Klint dialog — WORKS** (`--talk-hex 21101` → greeting + option, clean exit).

### Arroyo
- **Elder — WORKS but serves the WRONG (low-INT) script branch**: "I am proud of you, Chosen…
  Here is a shiny bottle. Vic. He is a trader in Klamath." Root cause: `get_critter_stat` is
  arity-stubbed → returns 0 → every `get_critter_stat(dude, INT)` branch takes the dumb path.
  (Option-list IQ filtering separately uses `DialogIntelligence()=5`, ScriptHost.cs:652, so options
  look smart while replies are dumb.) Same symptom: Mynoc "Greetings, dull one!", Tubby "Please nod
  if you would like to trade". The Elder's flask handover relies on stubbed inventory externals.
- **Hakunin — talk WORKS** (correct mystic greeting). His unprompted greeting/healing
  (`dialogue_system_enter` from critter_p_proc) NEEDS critter heartbeat.
- **Bridge guardian Mynoc — talk WORKS** (dumb branch). Bridge crossing: walkable to hex 22905;
  beyond that `--goto` fails — Pathfinder.cs caps at 2000 expanded nodes (comparable to engine
  limits; a human clicking in increments crosses fine). Worldmap exit grid x31 (map −2) exists.
- **Getting the worldmap — WORKS.** Exit grids: artemple→arcaves transition verified by walking
  (`--goto 15686` → "travelling to arcaves.map"). `ApplyTransition` maps dest<0 → worldmap.
  `--worldmap --travel 2` → "Klamath → kladwtwn.map"; `--travel 1` → "Den → denbus1.map".

### The Den (Vic chain entry)
- **Becky — WORKS**: real dialog incl. "Do you have any work?" quest entry. Her door/guard web uses
  `fetch_external(becky_door_obj/becky_guard_obj)` — silently 0 (export.cc mechanism missing).
- **Tubby — talk WORKS (dumb branch); trading NEEDS barter.**
- **Flick — produces NOTHING**: talk_p_proc (59 instrs) runs, zero reply/options, no error. His
  conversation is effectively barter-gated (`Barter()` is a notice stub: "[Barter is not part of
  this PoC.]", ScriptHost.cs:660).
- **Vic — dialog WORKS** ("Who are you? You're not a slaver…", 4 correct options). Completing the
  chain NEEDS: caps payment to Metzger via dialog (caps externals exist per phase-5, but Metzger is
  barter/dialog-money heavy), `move_obj_inven_to_obj`, and party-member join (`party_member_obj`
  stubbed) for the payoff.

## 3. Ranked break-list (top gaps for the opening hour)

| # | Gap | Mechanism to build | fallout2-ce ref | Effort | Payoff |
|---|---|---|---|---|---|
| 1 | Hostiles never aggro; Cameron can't start/settle his fight; Hakunin never greets | critter_p_proc heartbeat: round-robin one critter script per tick, gated off dialog/combat | scripts.cc `_script_chk_critters`:705 | M | blocks progression + the whole "alive world" feel |
| 2 | Kills give nothing; Cameron's death never unlocks door; no XP anywhere | destroy_p_proc on kill + real `give_exp_points` + minimal XP/level counter | combat.cc:4857/6018; stat.cc `pcAddExperience` | M | blocks progression |
| 3 | Every NPC talks to you like you're INT 1 | real `get_critter_stat/get_pc_stat/has_trait/has_skill/do_check` against a dude character sheet | interpreter_extra.cc `opGetCritterStat`; stat.cc `critterGetStat` | S | blocks dialog correctness everywhere |
| 4 | No player character sheet (dude = raw hmwarr proto) | fixed default SPECIAL/skills block feeding #3 + combat | proto.cc critter defaults | S | enabler for #3/#5 |
| 5 | Mandatory unarmed fights unwinnable (died to scorpions AND Cameron) | combat tuning + usable healing items (healing powder/stimpak from inventory) | item.cc `_item_d_take_drug` | M | blocks (lockpick bypass exists but is a break) |
| 6 | critter_add_trait stubbed (top stub by volume, 46–156/map) — scripted team/AI assignments lost | implement TEAM/AI-packet branch of critter_add_trait | interpreter_extra.cc `opCritterAddTrait` | S | combat sides correctness |
| 7 | Barter absent: Flick mute, Tubby/Becky/Metzger commerce dead | gdialog_barter → trade UI + transaction | gdialog.cc `gdialogOpenBarter`; inven.cc barter funcs | L | blocks Den/Klamath economy (already designated phase-6 spillover) |
| 8 | fetch_external returns 0 (Becky's door/guard, Klamath boxes, Den gangs) | imported/exported variable table shared across scripts | export.cc `externalVariable*` | S | quest webs silently misbehave |
| 9 | Explosives path: AIBkDor damage_p_proc, item-on-object | use_obj_on_p_proc + timed explosion + scenery damage | action.cc `_action_use_an_item_on_object`, `actionExplode`; queue.cc | M | canonical route (native door-toggle bypass works today) |
| 10 | move_obj_inven_to_obj stubbed — dialog gifting (Elder's flask, Klint's spear…) | implement inventory-transfer external | interpreter_extra.cc `opMoveObjectInventoryToObject` | S | quest items never arrive |
| 11 | override_map_start stubbed (artemple, denbus1/2) | set dude tile/elev/rotation from script | interpreter_extra.cc `opOverrideMapStart`; map.cc `mapSetEnteringLocation` | S | correct spawn points |
| 12 | party_member_obj stubbed; no companions (Vic payoff, Sulik) | minimal party join/follow | party_member.cc | L | hour-edge; defer |
| 13 | play_gmovie stubbed (Elder's cutscene) | no-op with caption/log line | gmovie.cc `gameMoviePlay` | S | flavor |

(Also: wire `OnStubbedExternal` → stderr in the viewer; trivial, pure diagnosis.)

## 4. Cross-cutting

**a. Save versioning** (src/Hexwaste.Formats/SaveState.cs, 71 lines): plain System.Text.Json DTO,
**no Version field** (verified in emitted JSON: keys = Map, DudeTile, DudeRotation, Elevation,
ClockTicks, GlobalVars, DudeInventory, VisitedMaps, LocalVars). Risk is real because map deltas are
keyed by **load-order ordinals** — any change to MAP parsing or object filtering silently corrupts
old saves. Minimal future-proofing before saves circulate: (1) add `public int Version { get; set; } = 1;`
serialized first; (2) in `Load/FromJson`, treat absent (0) as v1, refuse versions > current with a
clear message, and route older versions through a `Migrate(SaveState, fromVersion)` hook (identity
today); (3) keep default STJ leniency (unknown fields ignored) so additive changes don't need bumps —
bump only on semantic changes (ordinal scheme, LVAR keying). Effort: S (≈20 lines + 2 tests).
Save/load round-trip verified working headless.

**b. Combat-controller testability** (ViewerGame.cs ~lines 1400–1750): the phase machine
(`CombatPhase`, `TryAttack`, `BeginCombat`, `AddJoiners`, `EndPlayerTurn`, `UpdateCombat`,
`StepEnemyTurn`, `TryEnemyAction`, `CombatShouldEnd`) contains **no MonoGame types** — math is
already in Formats (CombatMath, CritterState, Pathfinder, HexGrid). Couplings: `_animator`
(GPU-backed FrmCache textures; damage resolves on animation completion), `_npcWalkers`
(DudeController), `_vfs/_artIndex` art-exists probes, draw-list mutation in `FinishCorpse`,
`Log`. Smallest extraction: a `CombatEngine` in Hexwaste.Formats.Combat owning phase/round/AP/
hostiles/queue + resolution, with a small host interface (`Log`, `PlayAttack/PlayFall` → events,
`ArtExists`, `IsBlocked`, `OnCorpse`) and an explicit `Tick(animationsIdle: bool)` so tests drive
turns without an animator. Verdict: **M** (about a day) for the full machine; **S** if you only lift
the turn/AP/joiner arithmetic and leave choreography sequencing in the viewer. Worth doing before
combat-depth work (#1/#2/#5 above all land in this code).

**c. Ecosystem delta (June 2026):**
- fallout2-ce: still active (new issues through Feb 2026); still **Sustainable Use License**;
  the licensing-clarity threads ([#428](https://github.com/alexbatalov/fallout2-ce/issues/428),
  [#476 "Is this a decompilation?"](https://github.com/alexbatalov/fallout2-ce/issues/476)) remain
  open discussion, no relicensing — Hexwaste's SUL+NOTICE posture stays correct.
- MonoGame: 3.8.5 previews shipped ([preview 2, Jan 2026](https://monogame.net/blog/2026-01-02-MonoGame385.preview.2-release/));
  3.8.5 final discussed at the [May 2026 AMA](https://monogame.net/blog/2026-05-19-open-hours-may-2026/);
  3.9 planned as the LTS of the 3.x line ([roadmap](https://docs.monogame.net/roadmap/)). No urgency;
  DesktopGL on 3.8.4 stays fine.
- .NET 10: LTS, supported to Nov 2028; current servicing 10.0.9
  ([June 2026 servicing](https://devblogs.microsoft.com/dotnet/dotnet-and-dotnet-framework-june-2026-servicing-updates/),
  2 CVEs). Routine SDK bump only.

## 5. Verdict input

**"Make the opening hour playable" is a coherent phase-6** — more coherent than a single-system
phase — with one carve-out: keep barter (#7) as its own milestone inside it, and defer party
members (#12). Three strongest measured facts:

1. **The skeleton already closes end-to-end.** Measured today: new game → temple →
   lockpick final door → exit grid → Arroyo → worldmap → Klamath/Den; every map loads, every key
   NPC dialog runs with real text, save/load round-trips. What's missing is not a system, it's a
   short list of script *hooks* on existing systems.
2. **The top blockers concentrate in ONE mechanism family**: running three more script procs
   (critter/destroy/damage + a handful of real externals over the existing CritterState/inventory).
   Items #1–#3, #6, #8, #10, #11 are all ScriptHost/IntVm work with engine references already in
   hand — high payoff-per-effort, naturally test-driven by transcripts like the ones in §2.
3. **Single-system alternatives score worse empirically**: combat depth alone leaves a world where
   nothing ever attacks you (no critter_p_proc) and kills yield nothing (no destroy_p_proc/XP) —
   measured: a peaceful Temple of Trials and an unwinnable-but-skippable Cameron. Barter alone
   unblocks Flick/Tubby but none of the temple/Arroyo arc. The grab-bag is what converts existing
   systems into a game loop.
