# Phase-10 Track D: Worldmap travel UI — the traveling dot, the encounter roll hook, return/resume

Scope of this note: the **travel feel + the return/resume seam**. Not the roll
math (Track A owns `wmRndEncounterOccurred`/pick) and not the spawn (Track A/the
transient-map question). What Track D owns: turn the current instant
click-to-teleport into a **Bresenham-walking party dot** that advances game-time,
looks up the current subtile, and offers Track A a per-step roll hook; then make
an encounter map's edge **return to the worldmap at the exact interrupted
position and auto-resume** travel. All engine claims cite
`reference/fallout2-ce/src/<file>.cc:LINE`; all dimensions are quoted from the
real `worldmap.txt`/`city.txt`/`maps.txt`, not from memory.

Coordinate-space note used throughout: city.txt `world_pos=x,y` are **absolute
worldmap pixels** (Arroyo `184,133`, Klamath `373,122`, Den `473,272`, city.txt:34
/ Klamath & Den blocks) — the *same* space as the engine's
`wmGenData.worldPosX/Y`. Our `WorldArea.WorldX/WorldY` (CityList.cs:20-21) already
hold these raw pixels, and `WorldmapScreen` already renders the
`WorldWidth=1400 × WorldHeight=1500` grid (WorldmapScreen.cs:23-24). So the dot
lives in pixel space directly — no new coordinate transform.

---

## (0) The grid dimensions — CONFIRMED, and a correction to the p7/p8 notes

The prior notes (p7-track-b-world.md:260, p8-track-a-encounters.md:144) said "6×6
subtiles" / "7×3 grid". Both are wrong. The real constants
(worldmap.cc:64-65,96-99):

```
#define SUBTILE_GRID_WIDTH  (7)
#define SUBTILE_GRID_HEIGHT (6)
#define WM_TILE_WIDTH  (350)
#define WM_TILE_HEIGHT (300)
#define WM_SUBTILE_SIZE (50)
```

and **`num_horizontal_tiles=4`** (worldmap.txt:3054) with **20 `[Tile N]` blocks**
(worldmap.txt grep: Tile 0..19) ⇒ a **4-wide × 5-tall tile grid**:
`4*350 = 1400` px wide × `5*300 = 1500` px tall. This is *exactly*
`WorldmapScreen.WorldWidth=1400 / WorldHeight=1500` (WorldmapScreen.cs:23-24,
`TileColumns=4`, `TileCount=20`). The existing renderer is already grid-correct;
Track D only adds the dot and the subtile math on top of it.

Each tile is `350/50 = 7` subtile-columns × `300/50 = 6` subtile-rows ⇒
**7×6 = 42 subtiles per tile**, 20 tiles ⇒ 840 subtiles total. (Not 36.)

### The subtile lookup math (worldmap.cc:3533-3543 `wmFindCurSubTileFromPos`)

```
tileIndex = y / 300 * 4 + x / 350 % 4;          // wmNumHorizontalTiles = 4
TileInfo* tile = &wmTileInfoList[tileIndex];
column = y % 300 / 50;                            // 0..5  (WM_TILE_HEIGHT/WM_SUBTILE_SIZE)
row    = x % 350 / 50;                            // 0..6  (WM_TILE_WIDTH /WM_SUBTILE_SIZE)
*subtilePtr = &tile->subtiles[column][row];      // NOTE [column][row]
```

### The data-key → [column][row] mapping (the load-bearing parse detail)

`wmConfigInit` builds the config key as **`snprintf(key, "%d_%d", row, column)`**
with `column` the OUTER loop (0..5) and `row` the INNER loop (0..6)
(worldmap.cc:1341-1352), then stores at `subtiles[column][row]`
(`wmParseSubTileInfo(tile, row, column, …)`, :1352). So a worldmap.txt line
`R_C=...` has:

- **first number `R` = `row` = the x-subtile** (`x%350/50`, range 0..6)
- **second number `C` = `column` = the y-subtile** (`y%300/50`, range 0..5)

Example from real data (worldmap.txt:3091, Tile 0): `2_4=Desert,No_Fill,Uncommon,
Uncommon,Uncommon,Arro_D` → row=2 (x-subtile 2), column=4 (y-subtile 4),
terrain=Desert, fill=No_Fill, freqs morning/afternoon/night = Uncommon ×3, table
lookup = `Arro_D`. Our parser MUST index `subtiles[C][R]` and look up with
`C=y%300/50, R=x%350/50` or it reads the wrong frequency. (The p8 parser sketch
at p8-track-a-encounters.md:144 declared a 6-col×6-row `WmSubtile[,]` — fix to
**7 rows × 6 cols** addressed as `[column=0..5][row=0..6]`.)

### Subtile line grammar (worldmap.cc `wmParseSubTileInfo`)

`terrain , fill , morningChance , afternoonChance , nightChance , encounterTypeLookupName`
The 3 chances are frequency-name strings (`None/Rare/Uncommon/Common/Frequent/
Forced`) resolved through `[Data]` to the percentage (worldmap.txt:33-39:
Forced=100, Frequent=38, Common=22, Uncommon=12, Rare=4, None=0). The last field
is an `[Encounter Table N]` `lookup_name` (Track A's roll table). `Fill_W`/
`No_Fill` is a render-only adjacency hint — **skip** (we render the full FRM tile,
not procedural fill).

### Which tables the early loop actually uses (named, from Tile 0)

The Arroyo region (Tile 0, art_idx=339, worldmap.txt:3064) carries:
`Arro_M` (Mountain, e.g. `2_0`,`3_1`), `Arro_D` (Desert, `2_4`,`3_4`,`4_4`),
`Arro_O`/`Fish_O` (Ocean), `Arrok_D`/`Arrok_M` (Arroyo-Klamath border, `5_2`,
`6_2`), and the Klamath tables `Kla_M`/`Kla_D` along the east edge (`6_0`..`6_5`).
So the canonical Arroyo→Klamath→Den loop's *first* tables are **`Arro_M`,
`Arro_D`, `Kla_M`, `Kla_D`** — all on Tile 0 alone. (Den's `Den_D` table sits in
the tiles around `world_pos=473,272` ⇒ tileIndex `272/300*4 + 473/350%4 = 0*4 + 1
= 1` ⇒ **Tile 1**.) Track A decoded `Arro_M` (Table 6) and `Den_D` (Table 16)
in p8-track-a-encounters.md:75-77 — those are the right ones.

Most early subtiles are `Uncommon` (12%) or `Rare` (4%); the dense-encounter
Desert/Mountain frequencies kick in further out. With a 30-game-min step (below)
and the Δ3 gate, the early loop sees encounters every few steps, not constantly —
faithful.

---

## (1) The traveling dot — Bresenham over the existing UI

### The engine's walk is a fixed-point Bresenham in worldPos pixel space

`wmPartyInitWalking(x, y)` (worldmap.cc:4266-4309) sets up a classic integer
Bresenham line from `worldPosX/Y` to the clicked `(x,y)`:

```
dx = abs(x - worldPosX);  dy = abs(y - worldPosY);
if (dx < dy) {                                   // y-major
    walkDistance = dy;
    walkLineDeltaMainAxisStep  = 2*dx;
    walkLineDelta              = 2*dx - dy;
    walkLineDeltaCrossAxisStep = 2*(dx - dy);
    mainAxisStep  = (0, 1);  crossAxisStep = (1, 1);   // (x,y)
} else {                                          // x-major
    walkDistance = dx;
    walkLineDeltaMainAxisStep  = 2*dy;
    walkLineDelta              = 2*dy - dx;
    walkLineDeltaCrossAxisStep = 2*(dy - dx);
    mainAxisStep  = (1, 0);  crossAxisStep = (1, 1);
}
if (destX < worldPosX) negate the X components;   // :4296-4299
if (destY < worldPosY) negate the Y components;   // :4301-4304
```

`wmPartyWalkingStep()` (worldmap.cc:4312-4383) is one Bresenham increment per
call, gated by a terrain-difficulty divisor:

```
_terrainCounter = _terrainCounter % 4 + 1;                  // 1..4 cycle, :4318-4321
wmPartyFindCurSubTile();                                     // :4324
terrainDifficulty = max(1, wmTerrainTypeList[subtile.terrain].difficulty);
if (_terrainCounter / terrainDifficulty >= 1) {             // mountains step less often
    if (walkLineDelta >= 0) {                                // cross-axis step
        if (wmWorldPosInvalid(pos + crossAxisStep)) { stop; } // off the walk mask → stop
        walkLineDelta += walkLineDeltaCrossAxisStep;
        worldPos += crossAxisStep;
    } else {                                                 // main-axis step
        if (wmWorldPosInvalid(pos + mainAxisStep)) { stop; }
        walkLineDelta += walkLineDeltaMainAxisStep;
        worldPos += mainAxisStep;
    }
    walkDistance -= 1;
    if (walkDistance == 0) { isWalking = false; }            // arrived, :4377-4381
}
```

So **one step = one pixel along the line** (Δ3 measured in those pixels), and
each pixel-step is suppressed when `_terrainCounter / difficulty < 1` (Mountain
difficulty>1 ⇒ fewer pixel advances per frame — slower travel). `wmWorldPosInvalid`
(worldmap.cc:4244-4263) reads a per-tile 44-byte-stride walk-mask bitmap;
stepping onto a masked-off pixel halts travel.

### The time tick — 30 game-minutes per pixel-step

In the loop body (worldmap.cc:3103): after each `wmPartyWalkingStep()`, every
frame calls `wmGameTimeIncrement(18000)` (worldmap.cc:4172-4200, modulo the SFALL Pathfinder
0.25× bonus we skip). **18000 ticks = 30 game-minutes**: `GAME_TIME_TICKS_PER_HOUR
= 36000` (scripts.h:18), so `18000 = 36000/2 = 0.5 h`. Our
`GameClock.TicksPerHour = 60*60*10 = 36000` (GameClock.cs:12) is **identical to
the engine's**, so **+18000 GameClock ticks per dot-step == +30 game-min exactly**,
via `_clock.Ticks += 18000` (we don't even need a new helper; `AdvanceHours` is too
coarse — add `_clock.Ticks += 18000` inline or a small `Advance(long ticks)`).

### Minimal deterministic dot for Hexwaste (the design)

Current `WorldmapScreen` is render-only; the click handler in ViewerGame
(`OnLeftClick` → `TravelTo(area)`, ViewerGame.cs:1174,2937) does an **instant**
`LoadMap` + `_clock.AdvanceHours(8)`. Replace with a dot state machine. Keep ALL
of it in `WorldmapScreen` (it already owns the layout/scale math) + a thin
ViewerGame driver:

1. **Dot state** (new on WorldmapScreen): `Vector2 _pos` (init from
   `CurrentAreaId`'s `WorldX/WorldY`, or the area the dude last entered/exited
   from), `Vector2? _dest`, `bool _isWalking`, plus the Bresenham accumulator
   fields (port `wmPartyInitWalking` 1:1 — integer math, ~25 LoC). On click of an
   empty map point → `InitWalking(worldX, worldY)`; on click of a circle while
   stopped → existing `TravelTo`/enter-town.
2. **Step cadence**: drive from the fixed update, NOT real-time. The engine's
   1500 ms real throttle (worldmap.cc:3325) is a UI-frame-rate artifact; for a
   deterministic slice, do **N pixel-steps per fixed update** at a chosen
   `StepsPerSecond` (e.g. 1 step per fixed tick at 60 Hz looks like the engine's
   ~zoom). Each step: `WalkingStep()` (port the Bresenham increment; terrain
   divisor optional v1 — start at difficulty=1 everywhere, add the
   `wmTerrainTypeList` divisor later for Mountain slowdown) + `_clock.Ticks +=
   18000`.
3. **Per-step hook** (Track A's): after `WalkingStep()` while `_isWalking`, call
   `Func<int worldX, int worldY, EncounterResult?> _encounterRoll` (injected from
   ViewerGame). Track A implements the Δ3 / circle-suppression / daypart / pick
   chain (worldmap.cc:3322-3527) behind it and returns either null (keep walking)
   or a chosen encounter map index + start point. Track D just calls it and acts
   on the result (fade out, `LoadMap(transient)`, stash resume state).
4. **Arrival**: `walkDistance == 0` → `_isWalking=false`, match the dot pos to a
   circle (`HitTest`-style nearest within the size radius); if on a circle, the
   existing enter-town path runs. If the click was *at* a circle, that's the
   normal `TravelTo`.
5. **Render**: draw the dot (reuse the 1×1 `_marker` texture, ViewerGame already
   passes it) at `Layout()`-scaled `_pos`; optionally a faint line to `_dest`.

**Determinism**: the dot walk is pure integer Bresenham — already deterministic.
The only randomness is Track A's roll, which already seeds off `--rng-seed`
(ViewerGame.cs:317-319 `SystemCombatRng(seed)`; reuse the same `ICombatRng` for
the worldmap RNG so `--rng-seed` makes the whole travel+encounter sequence a
golden-transcript fixture). A new `--travel-to <x> <y>` (or reuse `--travel
<areaIndex>` + a new `--walk-steps N`) headless action makes it scriptable.

### Sizing — the dot

- **Bresenham port (`InitWalking` + `WalkingStep`): S, ~50 LoC** — 1:1 from
  worldmap.cc:4266-4383, integer math, no MonoGame deps (could even live in
  Formats and be unit-tested headless against a hand-computed line).
- **Subtile lookup (`FindSubtile(x,y)`): S, ~10 LoC** — worldmap.cc:3533-3543,
  `[column=y%300/50][row=x%350/50]`.
- **Dot state machine + render + click rework in WorldmapScreen/ViewerGame: S/M,
  ~80 LoC** — the `_isWalking`/`_pos`/`_dest` plumbing, the per-step roll hook
  callback, the `_clock.Ticks += 18000` tick, the dot marker draw.
- **Total dot: M.** Maps to **M1**. Terrain-difficulty slowdown and the
  walk-mask `wmWorldPosInvalid` halt are **DEFER**-able polish (start
  difficulty=1, no mask — the dot can cross "ocean" pixels; cosmetic only since
  encounters come from the subtile table, not the mask).

---

## (2) Return / resume — the seam, and why it's nearly free

### How the engine leaves an encounter map (exit grids, map == -2)

Encounter maps are **ringed on every edge with exit grids whose `Destination.Map
== -2`** (p8-track-a-encounters.md:20-24 audited this: mountn1 38×, city1 434×,
desert1 374× exit grids with dest map=-2 around the perimeter). Walking off any
edge steps onto one. `mapHandleTransition` (map.cc:1233-1254):

```
if (gMapTransition.map == -1) { wmTownMap();  memset(&gMapTransition,0,…); }   // :1243
else if (gMapTransition.map == -2) { wmWorldMap(); memset(&gMapTransition,0,…); } // :1249
```

So **map == -2 → `wmWorldMap()`**, map == -1 → `wmTownMap()` (the town-map
sub-screen, which we don't have — treat -1 as -2 for v1). `wmWorldMap()`
(worldmap.cc:2971) just calls `wmWorldMapFunc(0)` (:2971) — it **re-enters the
worldmap loop**.

### Why the dot reappears exactly where it stopped, and resumes

`wmGenData.worldPosX/Y`, `isWalking`, `walkDestinationX/Y`, and the Bresenham
accumulators are **module-global state that the encounter detour never touches**.
`wmWorldMapFunc` on re-entry (worldmap.cc:2975-3026): it reads
`wmGenData.isWalking` at the top of the loop (worldmap.cc:3025) and, if still
true, immediately calls `wmPartyWalkingStep()` again. The encounter trigger path
(worldmap.cc:3109-3120) does `wmFadeOut(); mapLoadById(encounterMapId); break;` —
it **breaks out of the loop but leaves `isWalking` true and walkDestination set**.
So when the map's exit grid brings you back via `wmWorldMap()`, the loop resumes
the SAME walk toward the SAME destination from the SAME position. **Resume is
automatic because nothing cleared the state.** This is the whole trick — there is
no explicit "save/restore worldmap position" around the encounter; the state is
ambient and survives the round-trip.

(One subtlety: `wmRndEncounterOccurred` sets `oldWorldPosX/Y = worldPosX/Y` at
the end of a successful roll, worldmap.cc:3502-3503 per p8 — the Δ3 anchor — so
the *next* roll after resuming needs another 3-pixel move. That's Track A's
state; Track D just preserves `oldWorldPos*` across the detour like everything
else.)

### Our integration — `_worldmapOpen` already does 80% of it

Hexwaste's exit-grid plumbing is already wired for negative dest maps:
- `MapFile` reads the exit-grid `map` field as a raw `Int32` (MapFile.cs:495), so
  `-1`/`-2` flow straight into `Destination.Map`.
- `CheckExitGridAt` (ViewerGame.cs:2959-2965) queues `_pendingTransition =
  destination` when the dude steps on an exit grid.
- `ApplyTransition` (ViewerGame.cs:2894-2915) already branches `Map < 0` →
  `_worldmapOpen = true; Log("You head out to the wasteland.")` (:2910-2914).

So walking off an encounter-map edge ALREADY returns to the worldmap screen
today. What's missing for **resume** is only:
1. **Stash the resume state** when an encounter is *triggered* (not when entered
   by click): on the per-step roll hit, before `LoadMap(transient)`, save the dot
   `(_pos, _dest, _isWalking, oldWorldPos)` on `WorldmapScreen` (it's
   module-state-equivalent — just don't reset it). Since the dot lives on
   `WorldmapScreen` and that instance persists across `LoadMap` (it's created once
   at startup, ViewerGame.cs:403, and only `Dispose`d at shutdown :4638), the
   state is *already* ambient across the detour — same as the engine. **No
   explicit save/restore needed**; just don't clear `_isWalking`/`_dest` on the
   transient `LoadMap`.
2. **Auto-resume on return**: when `ApplyTransition` sets `_worldmapOpen = true`
   from a `Map < 0` exit, the dot is still `_isWalking` ⇒ the next worldmap
   update keeps stepping. Today `ApplyTransition(Map<0)` is the manual "head to
   wasteland" (dude leaves a town on foot). For encounter return, the SAME branch
   fires and the dot just resumes — **identical code path, zero new branches**.
   The only care: distinguish "left a town on foot" (start a fresh walk / stop at
   the town circle) from "returned from an encounter mid-walk" (resume). The dot's
   own `_isWalking` flag is that discriminator — if it's already walking, resume;
   if not (left town), stop at the circle.

### The minimal deterministic version

- Dot Bresenham (M1) + the per-step roll hook (Track A) + transient `LoadMap`
  (Track A's persistence) + the existing `Map<0 → _worldmapOpen` return. Resume
  falls out for free from WorldmapScreen-persisted state.
- Headless: `--rng-seed S --travel-to X Y --walk-steps N` walks N pixel-steps,
  rolls each, and on an encounter prints the chosen map + group; a follow-up
  `--exit-edge` (step the dude onto a perimeter exit grid) returns to worldmap and
  resumes. Golden-transcript the (seed → encounter sequence) like combat.
- **DEFER**: town-map sub-screen for map==-1 (treat as -2); the walk-mask halt;
  terrain slowdown; the 1500 ms real-time throttle (we step on fixed ticks).

### Sizing — return/resume

- **S, ~30 LoC.** Stash-nothing (state already ambient on the persistent
  WorldmapScreen) + the "is the dot mid-walk?" discriminator in
  `ApplyTransition(Map<0)` + treating -1 as -2. The exit-grid → worldmap path is
  already built. This is the cheapest milestone in the phase.

---

## (3) The phase-9 projectile-tween loose end — does it fit M1's Bresenham work?

The prompt asks whether the thrown-projectile tween (phase-9 loose end: thrown
weapons fly via the throw *anim* with no tweened sprite, p9-track-d-physics.md:69,
:173-195) folds into M1's Bresenham/tween work.

**Partial fit, but it's a different tween axis — be honest about the overlap.**

- The worldmap dot tweens in **worldmap pixel space** (a 2-D Bresenham over a flat
  bitmap). It is integer-exact and needs no screen interpolation — the dot just
  draws at `Layout()`-scaled `_pos`.
- The projectile tween is `animationRegisterMoveToTileStraight` (actions.cc:792-806
  per p9-track-d:187): move an object along a straight HEX line, interpolating its
  **screen position** between two hexes' `Camera.SquareTileToScreenXY`/hex-screen
  positions over N frames, then fire a completion callback. That's a *hex-grid*
  straight walk + per-frame screen lerp in the combat viewport — a different
  coordinate system and a different host (ObjectAnimator, not WorldmapScreen).

**Shared concept, not shared code.** Both are "advance a point along a straight
line a fixed amount per tick", and `Hex.HexGrid.TileInDirection`/`RotationTo`
already give the projectile its hex line (p9-track-d:75-77). But the worldmap dot
reuses NONE of the ObjectAnimator screen-lerp plumbing, and the projectile reuses
none of the worldmap pixel-Bresenham. So:

- **It does NOT naturally ride M1.** M1 is worldmap-only; the projectile tween is
  a `Hexwaste.Viewer/ObjectAnimator` feature (currently `ObjectAnimator` only
  plays per-frame FRM offsets, ObjectAnimator.cs:102 — no position interpolation,
  grep confirms no `MoveToTile`/`Lerp`/`Tween`).
- **Recommendation: keep the projectile tween OUT of Track D / M1.** It belongs
  with whatever phase-10 milestone touches combat presentation (or stays
  deferred). Folding it into M1 would mean building a hex-screen interpolator that
  M1 doesn't otherwise need — scope creep. If a later milestone DOES build the
  hex-screen straight-move tween, the worldmap dot's Bresenham is a useful
  reference implementation for the "fixed advance per tick" pattern, but they
  share ~0 lines. **UNVERIFIED-as-a-fit; flagged as a deliberate non-fold.**

---

## (4) Cross-cutting: save format + quicksave-on-transient

### Worldmap position in the save

The engine persists, in `wmWorldMap_save` (worldmap.cc:1066-1145):
`worldPosX` (:1076), `worldPosY` (:1077), `currentAreaId` (:1075),
`encounterMapId/TableId/EntryId` (:1079-1081), per-area state/visited/entrance
states (:1087-1099), per-subtile fog `state` (:1104-1114), and the remaining
**one-shot Counters** (:1116-1143). It does **NOT** save `isWalking` or
`walkDestinationX/Y` (grep: absent from the save block). **Loading a mid-travel
save leaves you STOPPED at the saved `worldPos`.** So our additive-V2 needs only
`WorldPosX`, `WorldPosY`, `CurrentAreaId`, and Track A's per-table `Counter` dict
— matching the engine. Mid-walk destination is intentionally lost on load. (One
additive-V2 bump shared with Track A's encounter-counter persistence — argue it
fits; no Version-3.)

### Quicksave on a transient encounter map

The engine does NOT special-case "you're on an encounter map" for the *save UI* —
but `saved=No` means the map's `.SAV` is never written (map.cc:1456-1461 "Not
saving RANDOM encounter map" + erase, per p8-track-a:11). The savegame itself
records `worldPos`, so a save taken on an encounter map, when loaded, restores
**worldmap coordinates** and re-enters the worldmap (the live encounter map is
gone). For Hexwaste the simplest faithful rule (p8-track-a:175 already recommends
this): **saving while on a transient encounter map writes `WorldPosX/Y` +
`CurrentAreaId` and returns the loaded game to the worldmap**, not to the
encounter map. Document it as a divergence (the engine technically keeps the live
map in the slot via the `_map_save_in_game(false)` else-branch, map.cc:1456 — but
replicating that means serializing a full transient runtime map, not worth it).
Disallow/redirect quicksave on transient maps. **S, fits Track A's save work.**

---

## (5) Milestone fit + sizing summary (Track D's slice)

| Piece | Effort / LoC | Felt-depth | Milestone | DEFER-if |
|---|---|---|---|---|
| Bresenham `InitWalking`+`WalkingStep` port (worldmap.cc:4266-4383) | **S** ~50 | High (the dot moves!) | **M1** | — |
| Subtile lookup `[col=y%300/50][row=x%350/50]` (worldmap.cc:3533-3543) | **S** ~10 | — (feeds Track A roll) | **M1** | — |
| Dot state machine + render + click rework + `_clock.Ticks+=18000` | **S/M** ~80 | High | **M1** | — |
| Per-step encounter roll **hook** (Track A owns the body) | **S** ~15 | — | **M1** seam | — |
| Return/resume (exit grid Map==-2 → `_worldmapOpen`, ambient dot state) | **S** ~30 | High (seamless) | **M1/M4** | town-map for -1 → treat as -2 |
| Worldmap pos in additive-V2 save (WorldPosX/Y, CurrentAreaId) | **S** ~15 | — | (shared w/ Track A) | — |
| Quicksave-on-transient → return to worldmap coords | **S** ~10 | — | (shared w/ Track A) | — |
| Terrain-difficulty slowdown + walk-mask halt | S ~30 | Low (cosmetic) | **DEFER** | always (start diff=1, no mask) |
| Projectile screen-tween (phase-9 loose end) | M ~70 | High | **NOT M1** — different tween axis | — |

**Net Track D: M1 is the whole job** — a Bresenham-walking dot that ticks game
time, looks up the subtile, offers Track A a per-step roll hook, and returns/
resumes through the already-built `Map<0` exit path. The return/resume seam is the
phase's cheapest win because WorldmapScreen persists across `LoadMap`, so the dot
state is ambient exactly like the engine's `wmGenData`. Track D's only hard
dependency is Track A (the roll + transient-map load); Track D delivers the dot,
the hook, and the seam.

---

## Unverified / honest flags

- **Terrain `difficulty` values** (`wmTerrainTypeList[].difficulty`) come from
  worldmap.txt's terrain section, not yet parsed in our slice — the Mountain
  slowdown is real engine behavior (worldmap.cc:4326-4333) but I did not transcribe
  the per-terrain difficulty numbers here (DEFER'd anyway; start at 1). UNVERIFIED:
  exact difficulty integers per terrain.
- **`StepsPerSecond` dot speed** is a design choice, not an engine constant — the
  engine's speed is "1 pixel-step per UI frame, throttled by a 1500 ms encounter
  check and terrain divisor" (worldmap.cc:3025-3122,4318-4333). Our fixed-tick
  speed must be *chosen* to feel right; it does not affect game-time (always +30
  min/step) or determinism. UNVERIFIED: the wall-clock feel until tuned in-app.
- **`oldWorldPosX/Y` Δ3 anchor reset** (worldmap.cc:3502-3503) is Track A's roll
  state; I cite it only to note Track D must preserve it across the encounter
  detour (it's ambient like everything else). Its exact set-site I took from
  p8-track-a-encounters.md:114 rather than re-reading worldmap.cc:3502 line-by-line
  — cross-check if the Δ3 behavior surprises during M1 testing.
- **map == -1 (town-map) handling**: I recommend treating -1 like -2 (return to
  worldmap) since we have no town-map sub-screen. The engine calls `wmTownMap()`
  (map.cc:1246) which is a distinct screen — our divergence is intentional and
  documented. The p8 audit noted desert1's 56 map=-1 grids are "likely editor
  leftovers" (p8-track-a:24) — confirm during M3 that the dominant perimeter grids
  are -2, not -1, on the early-loop encounter maps.
- **Projectile-tween fit**: assessed as a NON-fold (different coordinate system /
  host). UNVERIFIED that a future combat-presentation milestone wouldn't want to
  share a generic "advance-along-line-per-tick" helper — possible, but they share
  ~0 lines today.
