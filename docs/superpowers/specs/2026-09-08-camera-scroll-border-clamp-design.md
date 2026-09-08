# Camera scroll border clamp — design

## Problem

`artemple.map` (and likely other maps) only define 7,456 of the 10,000 possible
floor tiles on the 100x100 square grid — undefined tiles render as a black
checkerboard void. Vanilla Fallout 2 never exposes this: fo2ce's camera is
clamped near the edge of the 200x200 hex grid so the view can never scroll far
enough to reveal undefined tiles. Hexwaste has no equivalent clamp, so panning
(mouse drag or arrow keys) near a map edge can expose the void.

Confirmed live: holding the Left arrow key for 2 seconds in fo2ce produced a
pixel-identical screenshot at the Temple of Trials entrance — proof the camera
was already at its scroll limit. Hexwaste's camera has no such limit.

A related but distinct fo2ce feature — a scroll clamp driven by `.EDG` files —
was investigated and ruled out: it comes from the HRP (High Resolution Patch),
a third-party mod, ported into the `community` fork only (not the project's
pinned `alexbatalov e97087b` base tree, and not vanilla Fallout 2 data). Per
CLAUDE.md, a fork addition is only portable when it corrects a misreading of
vanilla behavior — HRP's `.EDG` clamp is a deliberate mod feature, not that.

This spec instead ports the border clamp that **is** present in the pinned
base tree, unconditionally, in vanilla fo2ce.

## Vanilla mechanism (ported from)

`reference/fallout2-ce/src/tile.cc`, pinned base tree (`e97087b`):

- `tileSetBorder()` (`tile.cc:462-484`) computes four bounds —
  `gTileBorderMinX/MaxX/MinY/MaxY` — **once**, at map-init time
  (`tile.cc:437-444`), while deliberately pretending the window is the
  original 640x380 iso view size, regardless of the actual window size (the
  grid is always 200x200, so the margin is resolution-independent by design;
  see the comment at `tile.cc:437-439,464-467`). It probes two screen points,
  `(-320, -240)` and `(-320, ORIGINAL_ISO_WINDOW_HEIGHT + 240)` i.e.
  `(-320, 620)`, via `tileFromScreenXY`, and derives the margins from the
  resulting tiles' diamond coordinates relative to the grid-center tile's own
  diamond coordinates, with an odd/even adjustment on X.
- `tileSetCenter()` (`tile.cc:537-608`) rejects any candidate center tile
  whose diamond coordinates satisfy
  `tile_x <= MinX || tile_x >= MaxX || tile_y <= MinY || tile_y >= MaxY`
  (`tile.cc:574-578`), returning `-1` (no camera movement happens at all —
  it's all-or-nothing, not a slide-to-boundary).
- `mapScroll()` (`map.cc:603-654`) applies no clamp of its own; it computes a
  candidate center tile from the pan delta and defers entirely to
  `tileSetCenter`.

This spec ports only the border clamp — not the distance-from-dude cap or the
scroll-blocker object mechanism (also present in vanilla `tileSetCenter`, out
of scope here per explicit decision).

## Hexwaste's current camera model

`src/Hexwaste.Viewer/Camera.cs`:

- `SetCenter(int hexTile)` (`Camera.cs:42-70`) is already a direct, faithful
  port of `tileSetCenter`'s coordinate math (minus any bound checks) — it
  computes `_tileX`/`_tileY` (the same diamond coordinates vanilla calls
  `tile_x`/`tile_y`) from a hex tile index using the identical formula.
- `PanX`/`PanY` (`Camera.cs:31-32`) are a separate screen-pixel offset layered
  on top of the centered view; panning (mouse drag, arrow keys) only ever
  changes `PanX`/`PanY`, never `CenterHexTile`.
- `ScreenToHex(int screenX, int screenY)` (`Camera.cs:169-220`) is a faithful
  port of `tileFromScreenXY`; internally it computes the same diamond
  coordinates (`v10`, `v11` in the current code) before packing them into a
  single hex tile index (`v12 = HexGridWidth - 1 - v11`, final tile
  `= HexGridWidth * v10 + v12`) and returns `-1` if the packed tile falls
  outside the raw 0..199 grid bounds.
- The only existing clamp today is at `ViewerGame.cs:2797-2804`: after
  applying a candidate pan, it checks `ScreenToHex` of the viewport center;
  if that returns `< 0` (i.e. off the raw grid entirely), the pan is
  reverted. This is coarser than vanilla's border margin — it only rejects
  panning fully off the 200x200 grid, not panning into the border zone
  vanilla protects (which is what exposes undefined tiles near a map edge).

## Design

### 1. Compute the border once, in `Camera`

Add a private method to `Camera.cs`, ported from `tileSetBorder`, and four
new private fields: `_borderMinX`, `_borderMaxX`, `_borderMinY`, `_borderMaxY`.

Compute it once, the first time `SetWindowSize` is called (guarded by a
`_borderInitialized` bool so it never recomputes on subsequent resizes —
matching vanilla's own "calculated only once at start" comment, since the
margin is resolution-independent by construction). Implementation:

```csharp
private bool _borderInitialized;
private int _borderMinX;
private int _borderMaxX;
private int _borderMinY;
private int _borderMaxY;

/// <summary>ported from fallout2-ce src/tile.cc tileSetBorder(): computed
/// once, using the original 640x380 iso view size regardless of actual
/// window size (comment at tile.cc:437-439,464-467 — border margins are
/// resolution-independent since the grid is always 200x200).</summary>
private void InitializeBorder()
{
    // Temporarily pretend we're at the original resolution, center on
    // the grid-middle tile (mirrors tile.cc:437-444), probe two points,
    // then restore real window size via the caller's own SetCenter call.
    int savedWidth = _windowWidth;
    int savedHeight = _windowHeight;
    _windowWidth = 640;
    _windowHeight = 380;

    SetCenter(HexGridWidth * (HexGridHeight / 2) + HexGridWidth / 2);

    int v1 = ScreenToHex(-320, -240);
    int v2 = ScreenToHex(-320, 380 + 240);

    int v1TileY = v1 / HexGridWidth;
    int v2TileX = HexGridWidth - 1 - v2 % HexGridWidth;

    _borderMinX = System.Math.Abs(HexGridWidth - 1 - v2TileX - _tileX) + 6;
    _borderMinY = System.Math.Abs(_tileY - v1TileY) + 7;
    _borderMaxX = HexGridWidth - _borderMinX - 1;
    _borderMaxY = HexGridHeight - _borderMinY - 1;

    if ((_borderMinX & 1) == 0)
    {
        _borderMinX++;
    }

    if ((_borderMaxX & 1) == 0)
    {
        _borderMinX--;
    }

    _windowWidth = savedWidth;
    _windowHeight = savedHeight;
    _borderInitialized = true;
}
```

**Note on the port**: `v2TileX` above is computed via
`HexGridWidth - 1 - v2 % HexGridWidth` because `ScreenToHex` returns a packed
hex tile index, not the raw diamond `tile_x`/`tile_y` — this is the exact
inverse of the packing `ScreenToHex` itself performs
(`v12 = HexGridWidth - 1 - v11`, `tile = HexGridWidth * v10 + v12`; see
`Camera.cs:215-217`), so `HexGridWidth - 1 - (packedTile % HexGridWidth)`
recovers `v11` (vanilla's `tile_x`) exactly. `v1TileY` is recovered the same
way `SetCenter` derives `tile_y` from a packed tile
(`hexTile / HexGridWidth`, `Camera.cs:47`). Vanilla's own `v1`/`v2` computed
via `tileFromScreenXY` are used only partially in `tileSetBorder`
(`tile.cc:471-472`: `v2 % hexGridWidth` and `v1 / hexGridWidth`) — the port
above reproduces exactly those two partial reads, nothing more, since
`ScreenToHex`'s early-return-`-1`-on-out-of-grid behavior is irrelevant here
(the two probe points are always within the grid for a 200x200 grid centered
correctly, matching vanilla, which performs no bounds check at this call site
either).

`SetWindowSize` calls `InitializeBorder()` once, guarded. **Care needed**:
`InitializeBorder`'s internal `SetCenter` call (to the grid-middle tile, for
probing) overwrites `CenterHexTile` as a side effect — so the caller's real
center tile must be captured *before* calling `InitializeBorder` and
restored after, mirroring vanilla's own explicit second `tileSetCenter` call
after `tileSetBorder` (`tile.cc:452`):

```csharp
public void SetWindowSize(int width, int height)
{
    int realCenterHexTile = CenterHexTile;

    _windowWidth = width;
    _windowHeight = height;
    if (!_borderInitialized)
    {
        InitializeBorder();
    }

    SetCenter(realCenterHexTile);
}
```

### 2. Expose a border check on `Camera`

Add a public method other code (the pan-clamp call site) can use without
reaching into private fields:

```csharp
/// <summary>ported from fallout2-ce src/tile.cc tileSetCenter()'s border
/// check (tile.cc:574-578): true if the given hex tile falls within the
/// scroll border margin (i.e. panning to center on it is allowed).</summary>
public bool IsWithinScrollBorder(int hexTile)
{
    if (hexTile < 0)
    {
        return false;
    }

    int tileX = HexGridWidth - 1 - hexTile % HexGridWidth;
    int tileY = hexTile / HexGridWidth;

    return tileX > _borderMinX && tileX < _borderMaxX
        && tileY > _borderMinY && tileY < _borderMaxY;
}
```

### 3. Replace the existing clamp call site

`ViewerGame.cs:2797-2804` currently reverts a pan when
`_camera.ScreenToHex(viewport center) < 0`. Replace that condition with
`!_camera.IsWithinScrollBorder(_camera.ScreenToHex(viewport center))` —
`IsWithinScrollBorder` already treats a `-1` (off-grid) tile as
out-of-bounds, so this is a strict superset of the current check: every pan
the old code rejected is still rejected, and panning into the border margin
(which is what exposed the void) is now rejected too. The revert mechanism
itself (restoring `PanX`/`PanY` to their pre-pan values) is unchanged.

## Non-goals

- Distance-from-dude scroll cap (`tile.cc:543-562`) — not ported, per
  explicit scope decision.
- Scroll-blocker map objects (`tile.cc:564-568`, pid `0x500000C`) — not
  ported; no confirmed shipped map relies on this, per explicit scope
  decision.
- HRP's `.EDG`-file-driven per-map scroll boundaries — out of scope
  entirely; third-party-mod-derived, not present in the pinned base tree or
  in vanilla Fallout 2 data.
- No change to `SetCenter`, `HexToScreen`, `SquareToScreen`, or
  `ScreenToHex`'s existing projection math — this only adds a new border
  check consulted at the one pan-clamp call site.

## Testing

`Camera` has no existing unit tests (it lives in `Hexwaste.Viewer`, which has
MonoGame dependencies at the project level, outside the
`Hexwaste.Formats.Tests` project scope per the layout rule in CLAUDE.md).
Verify manually, the same way the original bug was found:

1. Launch Hexwaste on `artemple.map`, pan toward the Temple of Trials
   entrance using arrow keys (matching the fo2ce repro technique) until the
   pan stops advancing.
2. Screenshot and confirm no black-void checkerboard tiles are visible —
   compare against the fo2ce reference screenshot already captured in
   `scratch/compare-runs/temple-scenery-20260908-144951/fo2ce/entrance.png`
   (gitignored scratch, informational only).
3. Confirm normal panning elsewhere on the map (away from any edge) is
   unaffected — the camera should still move freely well within the border.
4. Confirm mouse-drag panning is clamped identically to arrow-key panning
   (both go through the same `ViewerGame.cs:2797` call site).
