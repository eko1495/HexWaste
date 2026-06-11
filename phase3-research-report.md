# Phase 3 Research Report — After the Walking Simulator

*Researched 2026-06-11 in-repo: all engine claims verified against `reference/fallout2-ce/src` with file/line citations gathered by parallel research agents; script-VM numbers measured by byte-exact disassembly of six real game scripts; web findings carry URLs and explicit unverified flags.*

## TL;DR

- **Recommended path: "Make the world real" — text/examine → static lighting → worldmap travel → sound → ambient NPC life**, with an optional, now-evidence-backed stretch goal: a micro INT VM for script-driven examine/door behaviors.
- **The phase-2 "never touch scripts" rule is partially overturned by measurement**: six disassembled scripts use only **39 of 76 core opcodes** (push is the *only* opcode with an inline operand; floats are never used), and the examine path needs exactly **3 external builtins**. A VM is no longer a research swamp — it's a bounded port — *but* it stays a stretch goal because examine text works without it (proto messages cover defaults; scripts only override).
- **Drop the modder-tooling pivot**: an actively maintained cross-platform map editor now exists (Gecko, Qt6, commits as of today), and fallout2-ce's mapper restoration stalled in 2023. The niche the phase-2 report identified has been filled.
- **MonoGame still requires Wine for DesktopGL shaders (verified mid-2026)** — and it doesn't matter: lighting is feasible on the CPU path because the engine applies **uniform intensity per object** (a SpriteBatch tint is *exactly* faithful); only floor tiles use per-pixel gradients, which can be approximated per-tile first and upgraded later.

## Key findings per direction

### 1. Lighting — feasible on CPU, objects perfectly, floors approximately

- Light is stored per hex: `gTileIntensity[3][40000]`, values 16384..65536; ambient acts as a floor via `max(tileLight, ambient)` (light.cc:16-75, tile.cc:1681).
- Light sources: objects carry `lightDistance` (≤8 hexes) + `lightIntensity`; linear falloff `(intensity-655)/(distance+1)` spread over a 36-neighbor table with per-rotation occlusion flags (`_obj_adjust_light`, object.cc:3963-4606, ~660 LOC — the bulk of any port).
- **Decisive rendering fact:** objects are blitted with **one uniform intensity** (`intensityColorTable[color][intensity/512]`, object.cc:2757-2844) → a per-draw `Color` tint in SpriteBatch reproduces this *exactly*. Floors interpolate light across 10 vertex samples per tile into a per-pixel intensity map (tile.cc:1598-1832) → a per-tile uniform tint loses the corner gradient; per-vertex coloring of tile quads (custom quad batch instead of SpriteBatch for floors) would recover most of it without any shader.
- Day/night = scripts calling `set_light_level`; for the PoC a CLI/keybind ambient parameter gives the same effect.
- Port scope: light.cc (~145 LOC) + `_obj_adjust_light` (~660) + intensity application (~100 in our renderer). Static computation at map load (we have no moving light sources) avoids the per-frame cost that hurt DarkFO.

### 2. Ambient NPC life — fidget is free, wander is an honest fake

- The "fidget" system is **engine-native and script-free**: a ticker scans all visible, standing, living critters and replays their stand animation every 1–10 s (`_dude_fidget`, animation.cc:3019-3110). Direct port onto our existing animator: ~200-300 LOC.
- Wandering is 100% script-driven in the original — there is no engine wanderer. A faked version (random rotation + short A* walks within a home radius, which we already have the machinery for) is visually plausible; what can't be faked: guard routes, fleeing, reactions to the player.

### 3. Worldmap — well-bounded, completes the traversal loop

- Renders from 20 `art\intrface\WRLDMP00-19.FRM` tiles (350×300 px, 4-wide grid; confirmed in game data). Areas live in `data\city.txt` (`world_pos`, size, entrance list with **map name + elevation + tile + rotation** — exactly what our existing transition code consumes).
- "Exit to worldmap" resolves via reverse lookup: which area's entrance list contains the map you left (`wmMatchAreaContainingMapIdx`, worldmap.cc:6541). Entering an area can skip the town-map UI and load the first valid entrance (`wmAreaFindFirstValidMap`).
- **~2,200 lines of worldmap.cc are skippable** (encounters, travel time, car/fuel, fog) — the needed core is ~800-1,000 C# LOC, mostly one-time data loading. Show all areas as known; no fog.

### 4. Sound — decoder is self-contained, our events are already in place

- `sound_decoder.cc` (ACM) is **1,296 LOC with zero external dependencies**, outputs PCM16 with header-declared rate/channels → feeds MonoGame `SoundEffect.FromStream`/`DynamicSoundEffectInstance` directly. No C# ACM decoder exists anywhere (verified negative) — this would be the first, with BSD/ISC libacm and MIT `alexbatalov/adecode` as cross-references.
- Event sfx names compose mechanically (game_sound.cc): scenery/doors = `s{a|p}{O/C/L/N/U}` + 4-char sound name from the proto; footsteps via the same `_art_get_code` we already ported. Files confirmed in `master.dat` under `sound\sfx\`, music under `sound\music\`.
- Phase-2 deferred sound for lack of events; we now *have* the events (steps, door open/close, map transitions).

### 5. Script VM — measured, not guessed (the headline result)

Byte-exact disassembly of `artemple.int`, `DenBus1.int`, `miDoor.int`, `gsrdoor.int`, `diMomBox.int`, `sishelf1.int` (analyzer script preserved at `/tmp/int_analyze.py`; zero unknown opcodes across ~100 KB of bytecode):

- **Core opcodes used: 39 of 76.** Only `push` carries an inline operand (4 bytes; type in the opcode's high bits). **0 float pushes** in all six scripts — float support can be skipped.
- **Examine (`look_at_p_proc`/`description_p_proc`) needs exactly 3 externals**: `script_overrides`, `message_str`, `display_msg`. Door `use_p_proc` + container stocking grow the set to ~25 externals (lockpick rolls, inventory ops, local vars, `reg_anim_func` stubbable to instant state change).
- **"No-op unknown externals" is viable ONLY arity-aware**: externals pop fixed args and usually push a return; a blind no-op desyncs the stack. The arity table is mechanically liftable from `interpreter_extra.cc`. With arity-aware stubs, the long tail (combat, XP, floats) no-ops safely.
- Caveat: procedures call helper procedures, so the practical target is "39 core ops + arity-stubbed externals," not a per-proc minimum.
- **Important deflation of the VM's value:** default examine text needs *no VM at all* — `pro_item.msg`/`pro_scen.msg` etc. map our already-parsed proto `MessageId` to name (`+0`) and description (`+1`) (proto.cc:335-369; confirmed against game data). Scripts only *override* defaults. So the VM is a quality upgrade, not a prerequisite.

### 6. Containers & items — mostly free, UI is the work

- Container inventories are **static MAP data we already parse**; scripts only conditionally modify them. Locked = a flag check (`CONTAINER_FLAG_LOCKED`); open/close = the same one-shot FRM animation we built for doors; pickup = remove-from-map + add-to-list (proto_instance.cc:571-618, 1789-1869).
- The original loot UI is script/interface-driven → a small custom panel is needed (item names from `pro_item.msg`, icons via `inventoryFid` — inven FRMs parse with our existing FRM code).

### 7. Modder tooling — drop it

- **Gecko** (JanSimek/geck-map-editor, C++20/Qt6/SFML3) is an actively developed cross-platform Fallout 2 map *editor* — 386+ commits, latest **2026-06-11**. fallout2-ce's `src/mapper/` has had no commits since Sep 2023 and issue #421 confirms it's unfinished. A read-only inspector would compete with a live editor; the niche is gone. (NMA thread details partially unverified — the forum 403-blocks fetches; "sfall-rs" does not exist.)

### Cross-cutting

- **Fonts: use native AAF** — format is trivial (256 glyphs, width/height/offset + 1 byte/pixel opacity, big-endian; font_manager.cc:117-205), ~250 LOC, 5 fonts ship in the game data, and it's authentic. FontStashSharp (NuGet 1.5.6, June 2026, maintained) remains the TTF fallback if ever needed.
- **MSG parser**: `{id}{audio}{text}`, newlines stripped, ~100 LOC.
- **MonoGame**: 3.8.4.1 stable; 3.8.5 previews ship a Vulkan backend with a SPIR-V pipeline, but DesktopGL effect compilation **still needs Wine** (official docs + preview release notes). CPU rendering path remains the right call.

## Comparison table

| Direction | Effort | Payoff (visible) | Risk | Fun | Verdict |
|---|---|---|---|---|---|
| Text/examine (AAF+MSG, no VM) | Low (~500 LOC) | High — names/descriptions on click | Minimal | Medium | **Do first (M0)** |
| Lighting (static, CPU) | Medium (~900 LOC) | Very high — the game's mood | Low-medium (floor gradient fidelity) | High | **Do (M1)** |
| Worldmap travel | Medium (~1000 LOC) | Very high — whole world traversable | Low (formats confirmed) | Very high | **Do (M2)** |
| Sound (ACM + events + music) | Medium (~1600 LOC, mechanical) | High — immersion | Low (decoder self-contained) | High | **Do (M3)** |
| Ambient NPC life | Low (~300 LOC) | Medium | Low | Medium | **Do (M4)** |
| Containers/loot UI | Medium | Medium-high | Low | Medium-high | Fold into M5 or phase 4 |
| Micro script VM | Medium-high (bounded: 39 ops + ~25 arity-stubbed externals) | Medium (examine overrides, real door/lock behavior) | Medium — measured, no longer unknown | High (it's the engine's soul) | **Stretch (M5)** |
| Modder tooling pivot | Medium | Low (Gecko exists) | Low | Low | **Dropped** |

## Recommended roadmap

**M0 — Text foundation + examine (no VM).** AAF font renderer (`font_manager.cc`), MSG parser (`message.cc`), load `pro_*.msg`; clicking/hovering an object shows its real name, examine (e.g. right-click or E) shows the description from proto MessageId+1. Display via a simple message log strip. *Files: font_manager.cc, message.cc, proto.cc:335-369. Demo: click the Temple door → "Door / A sturdy door blocks your way…". Headless: `--pick` already prints; extend to print resolved names.*

**M1 — Static lighting + day/night ambient.** Port light.cc + `_obj_adjust_light` (36-neighbor falloff with occlusion), compute `gTileIntensity` once at map load from light-emitting objects; apply as per-object SpriteBatch tint (exact) and per-tile floor tint (approx); ambient slider/keybind + `--ambient` flag for screenshots. Acceptance: torch pools of light in the dark Temple. *Files: light.cc, object.cc:3963-4606, color.cc:168-191 (intensity semantics). Upgrade path if floors look flat: per-vertex colored quads for floors (no shader).*

**M2 — Worldmap travel.** Parse `city.txt` + `[Tile Data]` of `worldmap.txt` (areas/entrances only), render the 20-tile world with area circles, click-to-travel loads the area's first entrance via the existing transition code; exit grids marked "worldmap" open this screen at the source area. All areas visible (no fog, no encounters, no time). *Files: worldmap.cc:2386-2534 (areas), 5158-5307 (render), 6460-6557 (lookups). Demo: leave Arroyo, click The Den, arrive at denbus1.*

**M3 — Sound.** Port the ACM decoder into Formats (unit-test: decode a known sfx, assert sample count/rate; it's fully self-contained). Hook existing events: footsteps (surface code via `_art_get_code`), door open/close sfx, map-transition + worldmap music (`sound\music\*.acm` streamed via `DynamicSoundEffectInstance`). *Files: sound_decoder.cc (1.3k LOC), game_sound.cc:1318-1517 (naming).*

**M4 — Ambient life.** Fidget ticker (1–10 s stand-anim replays for visible critters), random facing changes, short radius-bounded A* wander walks with per-critter home tile. Accept the honest limits (no routines/reactions). *Files: animation.cc:3019-3110.*

**M5 (stretch) — Micro INT VM.** 39 core opcodes (skip floats), arity-aware external dispatch table lifted from `interpreter_extra.cc`, the 3 examine externals implemented for real, then the ~25 door/container externals; everything else arity-stubbed with a warning log. Run only `look_at/description/use/map_enter` procs. Reuse `/tmp/int_analyze.py` (copy into `tools/IntDump`) for regression: disassemble before running. *Pivot threshold: if stack desyncs persist after arity-stubbing in 2-3 test scripts, stop at M4 — the world is already alive without it.*

## Pivot thresholds

- **M1**: if per-tile floor tint shows ugly banding at light-pool edges → switch floors to per-vertex colored quads (~2 days), NOT to shaders (Wine still required, verified).
- **M3**: if the ACM port fights for >2 sessions, defer music (streaming) and ship event sfx only (small files, whole-buffer decode).
- **M5**: see above — VM is strictly optional; everything before it must not depend on it.

## Caveats / unverified

- NMA forum thread contents summarized from search excerpts (the site 403-blocks direct fetches).
- jsFO's "Jan 2026 update" date unverified; "sfall-rs" confirmed nonexistent.
- Whether MonoGame 3.8.5 previews allow *any* fully Wine-free effect build on Linux for the **Vulkan** target is unconfirmed (release notes never state it); irrelevant to the chosen CPU path.
- The opcode study covers 6 scripts (2 maps, 2 doors, 2 containers); the 39-core/69-external union will grow somewhat with dialog/critter scripts — but those are out of scope for M5.
- Per-proc opcode counts are non-transitive (helper-proc calls); the report's M5 scope already uses the whole-script union, which is the safe number.
