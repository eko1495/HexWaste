# UI Scale — HUD Bar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the always-visible bottom HUD bar render at the same uniform scale as the fourteen
screens Stage 2/3a/3b/3c/4a already migrated, filling non-4:3 windows like `fallout2-ce`'s
stretch, while every other screen (still-unscaled inventory, worldmap chrome — both out of scope
elsewhere) keeps its native behavior and every existing `_hudBarHeight` consumer keeps working
unchanged.

**Architecture:** `_hudBarHeight` keeps meaning "how many real device pixels the bar occupies" —
it becomes `InterfaceBar.Height * UiScale()` instead of the bare native constant. Under that
convention, all eight existing consumers (including Stage 3a's `SkilldexOrigin`, which already
divides by `UiScale()` anticipating exactly this) need zero changes. `DrawInterfaceBar`'s content
(background art through indicator pills) scopes into one scoped, scaled `SpriteBatch` block, the
same technique as the fourteen already-migrated screens — with one wrinkle no prior screen had:
this method has an early return **inside** what becomes the scaled block (the `_dude is null`
guard), so that return must resume the unscaled batch before returning, or the batch would stay
scaled for the rest of the frame. `TryClickInterfaceBar`'s independent `bar.Origin(...)` call and
its one caller convert together, in the same task.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`SpriteBatch.Begin`/`End`, already-shipped
`UiScale`/`VirtualViewport`/`UiMouse`/`UiScaleMatrix` from Stage 1/2/3a/3b/3c/4a).

## Global Constraints

- Scale model: `scale = min(viewportWidth/640, viewportHeight/480)` (Stage 1, shipped,
  unit-tested — do not touch).
- Only the HUD bar scales in this plan. No other screen changes.
- `_hudBarHeight` (`ViewerGame.cs:395`) stays a DEVICE-PIXEL quantity —
  `InterfaceBar.Height * UiScale()` — not a virtual-canvas one. This is the single most important
  design decision this plan depends on: it is what lets all eight existing consumers
  (`ViewerGame.Hud.cs:88` `DrawMouseCursor`, `:498` `DrawTextOverlay`'s `hudY`, `:642` a boolean
  gate, `ViewerGame.Panels.cs:645` `SkilldexOrigin`, `:735` `DrawSkilldexTextFallback`) keep
  working with ZERO changes of their own. Do not touch any of those five call sites in this plan
  — confirmed during planning that all five already read `_hudBarHeight` in a way that stays
  correct under this convention.
- `HudButtons()`, `DrawIndicatorPills(Point o)`, and `ViewerGame.Harness.cs`'s `HudClick` action
  need NO code changes — confirmed during planning: `HudButtons()`'s rects are bar-local and
  offset by whatever `o` its three callers compute; `DrawIndicatorPills` takes `o` as a parameter
  and inherits its coordinate space automatically; `HudClick` dispatches by name only
  (`.OnClick()`), no coordinate math.
- Every position read from a real device (mouse) or used to lay out scaled content must come
  from `UiMouse()`/`VirtualViewport()` inside a scaled block — never a raw
  `GraphicsDevice.Viewport`/`Mouse.GetState().X/Y` mixed into scaled content.
- `DrawInterfaceBar`'s `if (_dude is null || GetCritterState(_dude.Dude) is not { } stats) return;`
  early return sits INSIDE the scoped scaled block once the wrap is applied — it must resume the
  unscaled batch (`_spriteBatch.End(); _spriteBatch.Begin(samplerState: SamplerState.PointClamp);`)
  before returning, or every screen drawn later that same frame would render at the wrong scale
  for the rest of the frame. This is the one place in this whole multi-stage project where an
  early return genuinely lives inside a scaled block — get it right.
- `DrawInterfaceBar` has two call sites (`ViewerGame.cs:5810` live per-frame, `:7257` the
  `CaptureThumbnail` offscreen path) — confirmed during planning that both render to
  backbuffer-sized targets, so both compute the identical scale factor; no special-casing needed
  for either.

## File Structure

- `src/Hexwaste.Viewer/ViewerGame.Hud.cs` — `DrawInterfaceBar`, `TryClickInterfaceBar`.
- `src/Hexwaste.Viewer/ViewerGame.cs` — the one `Update()` call site for `TryClickInterfaceBar`.

## Task 1: HUD bar

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Hud.cs:109-276` (`DrawInterfaceBar`),
  `:419-448` (`TryClickInterfaceBar`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2968` (Update call site)

**Interfaces:**
- Consumes: `UiScaleMatrix()`, `VirtualViewport()`, `UiMouse()`, `UiScale()` (Stage 1/2, already
  shipped), the `Point uiMouse` local already declared in `Update()` (Stage 2 Task 1).
- Produces: nothing new consumed by a later task — this is the only task in this plan.

- [ ] **Step 1: Switch `_hudBarHeight`'s assignment to a scale-aware device-pixel value, and scope the rest of the method into a scaled `SpriteBatch` block**

Find, in `src/Hexwaste.Viewer/ViewerGame.Hud.cs` (around line 109-124):

```csharp
    private void DrawInterfaceBar()
    {
        if (_interfaceBar is not { Loaded: true } bar || _worldmapOpen)
        {
            _hudBarHeight = 0;
            return;
        }

        Rectangle viewport = GraphicsDevice.Viewport.Bounds;
        _hudBarHeight = InterfaceBar.Height;
        bar.Draw(_spriteBatch, viewport);

        if (_dude is null || GetCritterState(_dude.Dude) is not { } stats)
            return;
        Point o = bar.Origin(viewport); // bar-local coords (interface.cc) -> screen = o + coord
```

Replace with:

```csharp
    private void DrawInterfaceBar()
    {
        if (_interfaceBar is not { Loaded: true } bar || _worldmapOpen)
        {
            _hudBarHeight = 0;
            return;
        }

        // UI Scale (HUD bar): _hudBarHeight stays a DEVICE-PIXEL quantity -- the bar's actual
        // rendered height on screen -- not a virtual-canvas one. This is what lets every existing
        // consumer (DrawMouseCursor, DrawTextOverlay's hudY, SkilldexOrigin, DrawSkilldexTextFallback)
        // keep reading it exactly as before; only this method's OWN drawing needs to change.
        _hudBarHeight = InterfaceBar.Height * UiScale();

        // Everything below draws into its own scoped, scaled SpriteBatch block -- same technique
        // as the other 14 migrated screens -- so the bar matches fo2ce's fullscreen stretch.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        Rectangle viewport = VirtualViewport();
        bar.Draw(_spriteBatch, viewport);

        if (_dude is null || GetCritterState(_dude.Dude) is not { } stats)
        {
            // UI Scale (HUD bar): this early return sits INSIDE the scoped block above -- it must
            // resume the unscaled batch before returning, or every screen drawn later this frame
            // would render at the wrong scale for the rest of the frame.
            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            return;
        }
        Point o = bar.Origin(viewport); // bar-local coords (interface.cc) -> screen = o + coord
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Convert the hover/press-feedback loop's mouse read**

Find, in the same file (around line 248-267):

```csharp
        _panelPixel ??= CreatePixel();
        MouseState hoverMouse = Mouse.GetState();
        string? forcePress = Environment.GetEnvironmentVariable("HEXWASTE_HUD_FORCE_PRESS");
        foreach (HudButton b in HudButtons())
        {
            if (b.CombatOnly && !inCombat)
                continue;
            var rect = new Rectangle(o.X + b.Local.X, o.Y + b.Local.Y, b.Local.Width, b.Local.Height);
            bool over = rect.Contains(hoverMouse.X, hoverMouse.Y);
            bool pressed = (over && hoverMouse.LeftButton == ButtonState.Pressed)
                || string.Equals(forcePress, b.Name, StringComparison.OrdinalIgnoreCase);
            if (pressed && bar.Pressed.TryGetValue(b.Name, out Texture2D? dn) && dn is not null)
                _spriteBatch.Draw(dn, new Vector2(rect.X, rect.Y), Color.White);
            else if (pressed)
                _spriteBatch.Draw(_panelPixel, rect, new Color(0, 0, 0, 90));
            else if (over)
                // PREMULTIPLIED-alpha white (the SpriteBatch is AlphaBlend): a raw Color(255,255,255,45)
                // has RGB > alpha and over-brightens to a SOLID white box; Color.White * 0.18 is correct.
                _spriteBatch.Draw(_panelPixel, rect, Color.White * 0.18f);
        }
```

Replace with:

```csharp
        _panelPixel ??= CreatePixel();
        MouseState rawMouse = Mouse.GetState();
        Point hoverMouse = UiMouse();
        string? forcePress = Environment.GetEnvironmentVariable("HEXWASTE_HUD_FORCE_PRESS");
        foreach (HudButton b in HudButtons())
        {
            if (b.CombatOnly && !inCombat)
                continue;
            var rect = new Rectangle(o.X + b.Local.X, o.Y + b.Local.Y, b.Local.Width, b.Local.Height);
            bool over = rect.Contains(hoverMouse.X, hoverMouse.Y);
            bool pressed = (over && rawMouse.LeftButton == ButtonState.Pressed)
                || string.Equals(forcePress, b.Name, StringComparison.OrdinalIgnoreCase);
            if (pressed && bar.Pressed.TryGetValue(b.Name, out Texture2D? dn) && dn is not null)
                _spriteBatch.Draw(dn, new Vector2(rect.X, rect.Y), Color.White);
            else if (pressed)
                _spriteBatch.Draw(_panelPixel, rect, new Color(0, 0, 0, 90));
            else if (over)
                // PREMULTIPLIED-alpha white (the SpriteBatch is AlphaBlend): a raw Color(255,255,255,45)
                // has RGB > alpha and over-brightens to a SOLID white box; Color.White * 0.18 is correct.
                _spriteBatch.Draw(_panelPixel, rect, Color.White * 0.18f);
        }
```

(`rawMouse.LeftButton` — button state — is the fresh raw read; `hoverMouse.X/Y` — position — is
the scaled `Point` from `UiMouse()`. The debug-overlay loop right after this one draws only, reads
no mouse, and needs no change — it already keys off the same `o`.)

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Close the scoped batch at the method's end**

Find, at the very end of `DrawInterfaceBar` (around line 271-276):

```csharp
        if (Environment.GetEnvironmentVariable("HEXWASTE_HUD_DEBUG") == "1")
            foreach (HudButton b in HudButtons())
                _spriteBatch.Draw(_panelPixel, new Rectangle(o.X + b.Local.X, o.Y + b.Local.Y, b.Local.Width, b.Local.Height), new Color(255, 0, 0, 90));

        DrawIndicatorPills(o);
    }
```

Replace with:

```csharp
        if (Environment.GetEnvironmentVariable("HEXWASTE_HUD_DEBUG") == "1")
            foreach (HudButton b in HudButtons())
                _spriteBatch.Draw(_panelPixel, new Rectangle(o.X + b.Local.X, o.Y + b.Local.Y, b.Local.Width, b.Local.Height), new Color(255, 0, 0, 90));

        DrawIndicatorPills(o);

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Switch `TryClickInterfaceBar`'s independent origin call to the virtual viewport**

Find, in the same file (around line 419-423):

```csharp
    private bool TryClickInterfaceBar(int mouseX, int mouseY)
    {
        if (_interfaceBar is not { Loaded: true } bar || _worldmapOpen)
            return false;
        Point o = bar.Origin(GraphicsDevice.Viewport.Bounds);
```

Replace with:

```csharp
    private bool TryClickInterfaceBar(int mouseX, int mouseY)
    {
        if (_interfaceBar is not { Loaded: true } bar || _worldmapOpen)
            return false;
        // UI Scale (HUD bar): matches DrawInterfaceBar's scaled-block origin — this hit-test's
        // caller (ViewerGame.cs) passes an already-UiMouse()-transformed point, and this rect
        // family must agree with the same virtual-canvas origin the draw uses.
        Point o = bar.Origin(VirtualViewport());
```

- [ ] **Step 8: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 9: Route `TryClickInterfaceBar`'s one caller through `uiMouse`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2966-2971):

```csharp
            // A click on a HUD bar button (INV/OPT/MAP/CHA/PIP/SKILLDEX) is consumed
            // there and does not also walk/interact with the map underneath (#15 M4).
            else if (TryClickInterfaceBar(mouse.X, mouse.Y))
            {
                // handled by the bar
            }
```

Replace with:

```csharp
            // A click on a HUD bar button (INV/OPT/MAP/CHA/PIP/SKILLDEX) is consumed
            // there and does not also walk/interact with the map underneath (#15 M4).
            else if (TryClickInterfaceBar(uiMouse.X, uiMouse.Y))
            {
                // handled by the bar
            }
```

- [ ] **Step 10: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 11: Run the golden suites**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Run: `FALLOUT2_DIR=./game-data scripts/encounter-golden.sh check`
Expected: both report all scenarios passing, byte-identical — `DrawInterfaceBar`/
`TryClickInterfaceBar` are Draw/click-only, unreachable from a headless run; these are a broad
regression net, not targeted coverage of this change.

- [ ] **Step 12: Manual visual check — HUD bar at a non-4:3 window, including a Skilldex agreement check**

Run:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage-hudbar.png \
  -- --create 5,5,5,5,5,5,5:0,4,5:0
```
Read the resulting PNG. Expected: the HUD bar renders at 1.5× baseline (bigger than every prior
screenshot in this session that showed it at native size), still bottom-centered, undistorted,
every element (weapon slot, HP/AC digit boxes, AP pips, message monitor, weapon-mode label)
legible and correctly positioned within the bar.

Then run:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage-hudbar-skilldex.png \
  -- --create 5,5,5,5,5,5,5:0,4,5:0 --hud-click SKILLDEX
```
Read the resulting PNG. Expected: the Skilldex box (Stage 3a) still sits flush just above the
now-larger HUD bar's own top edge — not overlapping it, not floating with a gap — confirming
`_hudBarHeight`'s device-pixel convention holds end-to-end with its one pre-existing dependent
consumer.

If a live/scripted click is possible, click a specific HUD button (e.g. the visually-scaled MAP
or CHA button) and confirm it opens the correct screen — this is the check that proves
`TryClickInterfaceBar`'s conversion agrees with the scaled draw.

- [ ] **Step 13: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Hud.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the HUD bar to fill non-4:3 windows

Wires Stage 1/2/3a/3b/3c/4a's UiScale infrastructure into
DrawInterfaceBar: its content now draws inside its own scoped, scaled
SpriteBatch block, matching the fourteen already-migrated screens.
_hudBarHeight keeps meaning "device pixels the bar occupies" --
InterfaceBar.Height * UiScale() instead of the bare constant -- so
every one of its eight existing consumers (including Stage 3a's
SkilldexOrigin, which already divided by UiScale() anticipating this)
keeps working unchanged. The method's one internal early return (art
missing a live dude/critter state) now resumes the unscaled batch
before returning, since it sits inside the scoped block.
TryClickInterfaceBar's independent bar.Origin() call and its one
caller convert together so hit-testing agrees with the scaled draw.

This closes the HUD bar out of the deferred Stage 4 scope. Inventory
and worldmap chrome (Stage 5) remain the only screens left unscaled.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```
