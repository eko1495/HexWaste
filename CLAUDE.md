# Fallout 2 Map Viewer PoC — Prime Directives

Proof-of-concept **Fallout 2 map viewer** in **C# / .NET + MonoGame (DesktopGL)**.

## Mission

1. Read original Fallout 2 game data from the user's legally owned copy (`--game-dir`, default `./game-data`). **Never copy, embed, or commit any game assets into this repository.**
2. Parse: **DAT2** archives (`master.dat`, `critter.dat`, `patch000.dat`), **FRM** sprites, **PAL** palettes (`color.pal`), **MAP** files, minimal **PRO** prototypes.
3. Render one complete map (default `artemple.map`) in a resizable window: floor tiles, toggleable roofs, static scenery/walls with correct z-sorting.
4. Palette color cycling (slime, fire, shoreline, monitors, alarm) at original speeds.
5. Camera: pan with mouse drag / arrow keys, optional integer zoom.

**Out of scope (do NOT build):** critters/NPCs, scripting, combat, pathfinding, UI, sound, worldmap, save/load.

## Authoritative reference

`reference/fallout2-ce` (cloned, gitignored) — port logic from there, never guess.
Key files: `src/dfile.cc` (DAT2), `src/db.cc` (VFS: loose files override DAT), `src/art.cc` (FRM),
`src/color.cc`/`src/palette.cc` (PAL + cycling), `src/map.cc` (MAP), `src/proto.cc` (PRO),
`src/tile.cc` (**hex/square grid ↔ screen math, draw order — most important for rendering**).

When porting, add a comment with the source: `// ported from fallout2-ce src/tile.cc tileToScreenXY()`.
If a format detail can't be confirmed from fallout2-ce sources, **stop and ask** instead of guessing.

## Layout

- `src/Hexwaste.Formats` — pure .NET class library, zero MonoGame deps, unit-testable.
- `src/Hexwaste.Viewer` — MonoGame DesktopGL app.
- `tools/DatDump`, `tools/FrmDump` — CLI demo/debug tools.
- `tests/Hexwaste.Formats.Tests` — xUnit; tests needing real game files are guarded by env var `FALLOUT2_DIR` (skip when unset) so CI passes without assets.
- `game-data/` — extracted GOG game data (gitignored). `master.dat`, `critter.dat`, `patch000.dat`, `data/` live at its root.

## Milestones (commit after each)

Phase 1 (DONE): M1 DAT2 reader, M2 PAL+FRM, M3 MAP parsing, M4 static floor
render, M5 objects + z-sorting + roofs, M6 palette cycling.

Phase 2 — "walking simulator" (per research report
`compass_artifact_…_text_markdown.md`; NO combat, NO script VM — hard scope line):

0. **P2-M0** — DONE. Benchmark on newr1.map (heaviest: 2841 objects):
   avg 3.6 ms / p95 6.2 ms / max 13.6 ms full frame with cycling active —
   far under the 16 ms threshold. **Decision: CPU palette conversion stays;
   no shader, no Wine.** Simulation is wall-time driven, fixed 60 Hz update
   kept; `--bench N` measures uncapped frame cost; FPS shown in title.
1. **P2-M1** — static critters: FID→FRM name via critters.lst + anim-code
   suffix (`src/art.cc` `artBuildFilePath()`/`_art_get_code()`), correct
   direction, z-sorted with solid objects.
2. **P2-M2** — idle/breath animation + walk cycle in place (`src/animation.cc`);
   FRM frame offsets accumulate across frames.
3. **P2-M3** — mouse picking: per-pixel alpha hit-test in reverse draw order,
   hover shows PID/FID (`src/object.cc`, `src/tile.cc` screen↔hex).
4. **P2-M4** — dude movement: A* on hex grid (`src/path.cc`), blocking objects,
   walk along path, camera follow.
5. **P2-M5** — hardcoded interactions, no VM: doors (open/close animation),
   exit grids (map/elevation transition), stairs/ladders.

Phase 3 (DONE, per docs/phase3-research-report.md): M0 AAF fonts + MSG + examine,
M1 static lighting (LightGrid port incl. the 36-case occlusion switch;
CPU tints — per-object exact, per-square floor approximation), M2 worldmap
travel (city.txt/maps.txt lookup names), M3 sound (full ACM decoder port,
door sfx names, footstep approximation, maps.txt music; music is LOOSE files
under <game>/sound/music), M4 ambient life (fidget per _dude_fidget; wander
is a documented fake), M5 micro INT VM (39 core ops + 181 arity-mapped
externals; examine override path only — use_p_proc/map_enter NOT wired).
Scripts.lst is 0-based; message_str list ids are scripts.lst index + 1.

Phase 4 (DONE, per docs/phase4-research-report.md): M0 VM foundations (real
rolls — stub-0 = critical-failure trap; script context; LVARs are LAZY
slices, pristine maps store offset -1), M1 text dialog (gsay loop, options
bind by procedure index), M2 locked doors + lockpick + RunMapEnter (map
script = header.ScriptIndex-1), M3 world-mutation externals + loot/
inventory panels (inventoryFid icons; RunMapEnter snapshots its list — 
stocking scripts mutate it), M4 GameClock (engine has NO day/night curve;
ours is custom) + JSON delta save/load (containers restock by design),
M5 polish (outlines, roof fade, egg-fade approximation, scroll clamp).
GOTCHA: GPU backbuffer readback races — screenshots must render via a
RenderTarget2D (ViewerGame._screenshotTarget). Per-vertex floor lighting
(BasicEffect quads) remains the known deferred upgrade.

Phase 5 (DONE, per docs/phase5-research-report.md): M0 foundations (real
caps/timer/tile externals — pay-caps stub gave goods away; timers are
dialog-gated, cleared on map exit, 1:1 tick source), M1 multi-map
persistence (per-map deltas keyed by LOAD-ORDER ORDINALS — MAP object Ids
collide; LVAR slices keyed by map NAME import before map_enter on revisits,
firstRun=0; container snapshots overwrite restock; fixes the ~590 KB/
transition ScriptHost leak), M2 critter stats (proto stat block + the 11
MAP combat ints; CritterState = base+bonus), M3 player combat (roll before
animate, damage on completion; corpse = anim+28, NO_BLOCK + flat → loot
panel works unchanged), M4 AI turns (AP-budgeted approach, same-team
joiners within 20 hexes, game over → F9), M5 ship-prep (renamed
FalloutPoc→Hexwaste, SUL license + NOTICE, docs/ provenance,
scripts/release.sh, game-dir probing).

Phase 6 (DONE, per docs/phase6-research-report.md — "The Opening Hour"):
M0 hygiene (OnStubbedExternal finally hooked — it never was; SaveState
Version=1 refuse-mismatch; DeadOrdinals — kills persist, sid=-1 BEFORE
map_enter like the engine), M1 real dude (premade\player.gcd = the
critter proto stat-block layout + name/tags/traits; real get_critter_-
stat/has_trait/do_check/get_pc_stat — fixes every stat-gated dialog),
M2 critter_p_proc heartbeat (1 script per 10 Hz tick round-robin,
gated; real critter_add_trait/attack/anim_busy/rotation_to_tile —
unprovoked aggro IS script-driven), M3 kills matter (destroy/damage
procs; XP engine-side from proto exp, paid at combat END, forfeited on
death; level-up EN/2+2 HP), M4 winnable combat (weapon/armor/drug proto
payloads; equip = item flags 0x1/0x2/0x4000000 — MAP NPC weapons just
work; armor mutates bonus stats; stimpak = -2-marker random heal), M5
barter (export.cc vars session-scoped on ScriptHost — per-VM before,
never connected; gdialog_barter flag-only, arg OVERWRITES set_barter_-
mod; stock lives in the shop BOX at trade time because our dialog model
runs the talk epilogue early — session tracks the box; price =
cost×2×(mod+100)/100×(160+npcB)/(160+dudeB), sells at face).
GOTCHAS: map_enter must run HIDDEN scripted objects (shop boxes);
the dude's bag is ALIASED to dude.Inventory (caps externals); --attack
is a free-swing primitive (resets combat), --fight runs real turns.

Phase 7 (DONE, per docs/phase7-research-report.md — "Ship It, Then Arm
the Wasteland"): M0 v0.6 front door (menu + gcd picker + death screen,
README screenshots, CHANGELOG, v0.6.0 tag; publish = user's git push
per docs/RELEASING.md), M1 V2 saves (MovedOrdinals NPC positions
replayed BEFORE map_enter; SavedItem ammo fields, -1 = derive from
proto; override_map_start; V1 refuses), M2 guns (10mm-class = HITSCAN,
muzzle flash baked in FRM 'j' — zero animator features; to-hit
combat.cc:4314 subset; LoF = greedy hex walk DEVIATION from the
engine's screen Bresenham; dude art hmjmps — hmwarr has no gun sets,
engine has NO weapon-art fallback; R=reload, roofs moved to F4),
M3 traps (spatial records kept in MapFile; RunSpatialsAt gated like
_scr_SpatialsEnabled; create_object_sid BINDS scripts via AllocateSid;
critter_damage real; use_obj_on item-then-target precedence; gmovie =
caption card from .sve), M4 party minimum (followers travel OUTSIDE
map deltas, follow script re-bound per map — follow logic is 100%
script-side critter_p_proc; allies act after hostiles; enemies target
nearest of dude+allies; team kills pay XP), M5 per-vertex floors
(BasicEffect quads, corner light from NW/NE/SW/SE neighbor hexes;
newr1 3.34 ms avg — faster than the sprite path).
Spillover to phase 8: random encounters (worldmap.txt decoded, maps
need saved=No delta-skip), burst/aimed shots, companion management.

After each milestone: run tests, run the app if possible, update README progress checklist, conventional commit.

## Critical gotchas

- **Two grids**: floor/roof = 100×100 *square* grid; objects = 200×200 *hex* grid. Different coord→screen formulas; port both from `tile.cc`. Fallout's projection is oblique/trimetric, NOT standard 2:1 isometric.
- **Draw order**: floor → flat objects → non-flat objects in hex tile order → roofs.
- **PAL values are 0–63**: multiply by 4 and clamp for 8-bit RGB.
- **Roofs render shifted up 96 px** relative to their square tile.
- **FRM frame offsets accumulate** across frames; orientations may share the same data offset.
- **Transparent color = palette index 0.**
- Palette cycling must NOT re-decode whole textures per frame (killed jsFO). Keep 8-bit index data; prefer a palette-lookup shader with a 256×1 palette texture updated each cycle tick.
- DAT2 vs DAT1: Fallout 2 only (little-endian DAT2, zlib). Fallout 1 (DAT1, LZSS) is out of scope.

## Legal guardrails

- `.gitignore` excludes `*.dat`, `*.map`, `*.frm`, `*.pal`, `game-data/` — keep it that way.
- README must state: requires original Fallout 2 copy, no assets included, not affiliated with Bethesda Softworks.
- No "Fallout" in any distributable/package ID — DONE: the project is `Hexwaste` everywhere; LICENSE.md (SUL v1.0) + NOTICE.md ship with every artifact (see docs/RELEASING.md).

## Working style

- Small, reviewed steps over big-bang generation.
- Dependencies allowed: MonoGame, xUnit, SixLabors.ImageSharp (dump tools only). **Ask before adding anything else.**
- Streaming reads from DAT2 (`DeflateStream` at the right offsets); lazy-load FRMs with an LRU cache. Do not extract everything to memory.
