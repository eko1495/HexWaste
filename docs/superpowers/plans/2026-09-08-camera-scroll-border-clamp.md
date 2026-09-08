# Camera Scroll Border Clamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port fo2ce's vanilla scroll border clamp into Hexwaste's `Camera` so the camera can never pan far enough to expose undefined (black-void) map tiles near a map edge, matching vanilla Fallout 2 behavior.

**Architecture:** `Camera.cs` gains a one-time border-margin computation (`InitializeBorder`, ported from `tileSetBorder`) and a public `IsWithinScrollBorder(int hexTile)` check (ported from `tileSetCenter`'s border check). `ViewerGame.cs`'s existing pan-revert clamp swaps its coarse "off the raw 200x200 grid" test for this real margin check.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`src/Hexwaste.Viewer`). No new dependencies.

## Global Constraints

- Never copy, embed, or commit any game assets into the repository (CLAUDE.md) — this task touches no assets, only C# source.
- Port fidelity: every formula in this plan is a literal port of `reference/fallout2-ce/src/tile.cc` (pinned base tree `e97087b`) — do not "simplify" or "improve" the arithmetic; match it exactly, including C-style truncating-division semantics where present.
- Scope: only the border clamp (`tileSetBorder` + `tileSetCenter`'s border check) is ported. Do NOT add the distance-from-dude scroll cap, the scroll-blocker object mechanism, or anything HRP/`.EDG`-related — all three are explicitly out of scope per `docs/superpowers/specs/2026-09-08-camera-scroll-border-clamp-design.md`'s Non-goals section.
- `Camera` has no existing unit test project (it lives in `Hexwaste.Viewer`, outside `tests/Hexwaste.Formats.Tests`'s scope) — this plan verifies via build + a manual in-app check, not automated tests.

---

## File Structure

- Modify: `src/Hexwaste.Viewer/Camera.cs` — add border fields, `InitializeBorder()`, `IsWithinScrollBorder(int)`, and fix `SetWindowSize` to preserve `CenterHexTile` across the border-init call.
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2797-2804` — swap the pan-revert condition to use the new `IsWithinScrollBorder` check.

This is a single cohesive change (one file's new method + one call-site edit) — one task.

---

### Task 1: Port the scroll border clamp

**Files:**
- Modify: `src/Hexwaste.Viewer/Camera.cs`
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2797-2804`

**Interfaces:**
- Produces: `Camera.IsWithinScrollBorder(int hexTile) -> bool` — public, called from `ViewerGame.cs`'s input-handling block. Returns `true` when panning to center on `hexTile` is allowed (within the vanilla-derived border margin), `false` otherwise (including when `hexTile < 0`, i.e. off the raw grid).
- Consumes (from existing `Camera.cs` code, unchanged): `Camera.HexGridWidth`/`HexGridHeight` (both `200`, `Camera.cs:13-14`), `Camera.ScreenToHex(int, int) -> int` (`Camera.cs:169-220`), `Camera.SetCenter(int hexTile)` (`Camera.cs:42-70`), private fields `_tileX`, `_tileY`, `_windowWidth`, `_windowHeight` (all already declared in `Camera.cs:16-26`).

- [ ] **Step 1: Add the four border fields and the initialized flag**

In `src/Hexwaste.Viewer/Camera.cs`, find the existing private field block:

```csharp
    private int _windowWidth;
    private int _windowHeight;
```

(currently at `Camera.cs:25-26`, immediately after the `_squareOffX`/`_squareOffY` fields). Replace it with:

```csharp
    private int _windowWidth;
    private int _windowHeight;

    private bool _borderInitialized;
    private int _borderMinX;
    private int _borderMaxX;
    private int _borderMinY;
    private int _borderMaxY;
```

- [ ] **Step 2: Add `InitializeBorder()`**

Immediately after the `SetCenter` method (which ends at `Camera.cs:70` with its closing `}`), insert:

```csharp
    /// <summary>ported from fallout2-ce src/tile.cc tileSetBorder(): computed
    /// once, using the original 640x380 iso view size regardless of actual
    /// window size (comment at tile.cc:437-439,464-467 — border margins are
    /// resolution-independent since the grid is always 200x200).</summary>
    private void InitializeBorder()
    {
        // Temporarily pretend we're at the original resolution and center
        // on the grid-middle tile (mirrors tile.cc:437-444), probe two
        // points the same way tileSetBorder does, then restore the real
        // window size. The caller restores the real center tile afterward.
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

- [ ] **Step 3: Add `IsWithinScrollBorder(int hexTile)`**

Immediately after `InitializeBorder()`, insert:

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

- [ ] **Step 4: Fix `SetWindowSize` to preserve `CenterHexTile` across border init**

Find the existing `SetWindowSize` method:

```csharp
    public void SetWindowSize(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;
        SetCenter(CenterHexTile);
    }
```

(currently `Camera.cs:34-39`). Replace it with:

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

This matters because `InitializeBorder()` calls `SetCenter` internally (to
probe from the grid-middle tile), which overwrites `CenterHexTile` as a side
effect — without capturing the real value first, the very first
`SetWindowSize` call would leave the camera centered on the grid middle
instead of the caller's intended tile.

- [ ] **Step 5: Build to confirm no compile errors**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors (pre-existing warnings, if any, are unrelated and fine).

- [ ] **Step 6: Update the pan-clamp call site in `ViewerGame.cs`**

Find, at `src/Hexwaste.Viewer/ViewerGame.cs:2797-2804`:

```csharp
        // Scroll clamp (the engine's border check in tileSetCenter): revert
        // pans that push the view center off the hex grid.
        if ((_camera.PanX != panBeforeX || _camera.PanY != panBeforeY)
            && _camera.ScreenToHex(GraphicsDevice.Viewport.Width / 2, GraphicsDevice.Viewport.Height / 2) < 0)
        {
            _camera.PanX = panBeforeX;
            _camera.PanY = panBeforeY;
        }
```

Replace it with:

```csharp
        // Scroll clamp (the engine's border check in tileSetCenter): revert
        // pans that push the view center outside the vanilla scroll border
        // margin (this is what keeps undefined/black-void tiles near a map
        // edge from ever coming into view — see Camera.IsWithinScrollBorder).
        if ((_camera.PanX != panBeforeX || _camera.PanY != panBeforeY)
            && !_camera.IsWithinScrollBorder(_camera.ScreenToHex(GraphicsDevice.Viewport.Width / 2, GraphicsDevice.Viewport.Height / 2)))
        {
            _camera.PanX = panBeforeX;
            _camera.PanY = panBeforeY;
        }
```

- [ ] **Step 7: Build again to confirm no compile errors**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 8: Manual verification — confirm the void is no longer reachable**

`Program.cs`'s CLI has no flag that simulates held arrow-key input over time
(checked: no `--pan`/`--hold-key`/similar exists, and
`scripts/hexwaste-checkpoint.sh` only wraps one-shot `--screenshot` runs with
whatever action flags already exist). Panning is only driven through real
keyboard/mouse input in `ViewerGame.cs`'s `Update()`, so verify with a live
windowed run driven by `xdotool`, the same technique already established
this session for fo2ce in `docs/fo2ce-comparison-playbook.md`.

Run Hexwaste windowed on `artemple.map`:

```bash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --map artemple.map &
sleep 5
```

Find its window and focus it:

```bash
WIN=$(xdotool search --name "Hexwaste" | head -1)
xdotool windowactivate "$WIN"
```

Hold Left for ~2 seconds (matching the fo2ce repro that first found this
bug), the direction that exposed the void toward the Temple of Trials
entrance:

```bash
xdotool keydown Left
sleep 2
xdotool keyup Left
```

Screenshot with `spectacle` (the tool already confirmed to work on this
KWin session — `ffmpeg`/`import` do not):

```bash
spectacle -b -n -o /tmp/border-clamp-check-left.png
```

Expected: `/tmp/border-clamp-check-left.png` shows the Temple of Trials
entrance with no black checkerboard void tiles visible, consistent with the
existing fo2ce reference screenshot at
`scratch/compare-runs/temple-scenery-20260908-144951/fo2ce/entrance.png`
(gitignored scratch, informational comparison only — not asserted
byte-equal, just visually consistent: no void exposed). Read both images
with the Read tool to compare.

Then confirm panning still works normally elsewhere: hold Right for ~2
seconds (back toward the map interior, away from the edge), screenshot again
(`spectacle -b -n -o /tmp/border-clamp-check-right.png`), and confirm the
view actually moved from the left-clamped position — i.e. the clamp
engages only near the edge, not everywhere.

Kill the running instance when done:

```bash
kill %1 2>/dev/null || true
```

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/Camera.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
fix(viewer): clamp camera pan to the vanilla scroll border

Ports fo2ce's tileSetBorder/tileSetCenter border check (pinned base
tree tile.cc:462-484,574-578) so the camera can no longer pan far
enough to expose undefined black-void tiles near a map edge (observed
at artemple.map's Temple of Trials entrance). Replaces the previous
coarse "off the raw 200x200 grid" pan-revert check with the real
vanilla margin.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

## Self-Review

**Spec coverage:**
- Border computation (`tileSetBorder` port) → Step 2 (`InitializeBorder`). ✅
- Border check (`tileSetCenter`'s border check port) → Step 3 (`IsWithinScrollBorder`). ✅
- `SetWindowSize` fix for `CenterHexTile` preservation → Step 4. ✅
- Call-site swap at `ViewerGame.cs:2797-2804` → Step 6. ✅
- Non-goals (distance-from-dude cap, scroll-blocker objects, `.EDG`) → explicitly excluded, nothing in this plan touches them. ✅
- Testing section's manual verification procedure → Step 8. ✅

**Placeholder scan:** No TBD/TODO; Step 8's CLI flag has a documented fallback (check the existing script's `--help` rather than guessing) since I could not confirm the exact flag name from the spec — this is the one spot without a fully pinned-down command, called out explicitly rather than left silently vague.

**Type consistency:** `IsWithinScrollBorder(int hexTile) -> bool` used identically in Step 3's definition and Step 6's call site. `InitializeBorder()` is private, called only from `SetWindowSize` (Step 4) and never elsewhere — consistent with the spec.
