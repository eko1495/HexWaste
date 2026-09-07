# UI Scale Stage 5: Worldmap Chrome Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the worldmap chrome window (backdrop, town tabs, date/dial, car/globe monitor, and
the scissored 450×443 map/townmap view) render at the same uniform scale as every other screen in
the Viewer, filling non-4:3 windows instead of native 1:1 pixel size. This is the last unscaled
screen in the whole project.

**Architecture:** `WorldmapScreen`'s positioning methods already take `viewport` as a parameter
rather than reading `GraphicsDevice.Viewport` internally, so most of the fix is swapping what
`ViewerGame.cs` passes in. The one genuinely novel piece is the scissored inner map view:
`GraphicsDevice.ScissorRectangle` is a device-pixel API MonoGame does not run through the
`SpriteBatch` transform matrix, so `DrawChrome` needs a new `scale` parameter to convert its
virtual-unit clip rectangle into real device pixels, and its inner scissor `Begin()` needs its
own explicit transform. Two tasks land back-to-back in this one plan — not independently
shippable, since `DrawChrome` only renders correctly once both land (see Global Constraints).

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`SpriteBatch`, `GraphicsDevice.ScissorRectangle`,
`RasterizerState`), the already-shipped `UiScale`/`VirtualViewport`/`UiMouse`/`UiScaleMatrix`
infrastructure (Stage 1 through UI Scale Inventory Piece 2).

## Global Constraints

- Scale model: `scale = min(viewportWidth/640, viewportHeight/480)` (Stage 1, shipped,
  unit-tested — do not touch).
- `VirtualViewport()` always returns `new Rectangle(0, 0, w, h)` — offset always `(0,0)`, never a
  letterboxed/pillarboxed origin (confirmed via `ViewerGame.UiScale.cs:22-30` and
  `Hexwaste.Formats.Rendering.UiScale.ComputeVirtualViewport`/`TransformMouse`, whose own doc
  comment states no offset term is needed). This is why the device-pixel scissor rectangle is a
  **plain multiply by `scale`**, with no offset term.
- **Task 1 and Task 2 are not independently shippable.** `DrawChrome` draws both the frame and
  the scissored content in one continuous call — landing Task 1 alone would scale the frame
  while the scissored content stays native-size/position inside it, a visible mismatch. Both
  tasks land in this same plan, back-to-back, as one reviewable unit split into two
  review-focused diffs (frame math, then scissor math). Task 1's task review and manual
  verification therefore focus on frame math only (build + golden suites); the full manual
  visual check (world view + townmap sub-view scaling together with no mismatch) happens once,
  at the end of Task 2.
- The `else` legacy-fallback branch (`_worldmapScreen.Draw`/`HitTest`/`DrawPartyDot`/
  `DrawPartySprite`, `DrawWorldmapCarBox`) — taken only when `HasChrome` is false (no
  `worldmap.frm` loaded) — is OUT OF SCOPE and must stay completely untouched. It already
  computes its own independent aspect-fit scale (`Layout()`, `WorldmapScreen.cs:101-107`) and is
  not the 640×480-native letterboxing problem this project has been fixing.
- No `ViewerGame.Harness.cs` change is needed — confirmed by a whole-tree grep that it only sets
  the plain `_worldmapScreen.TownmapArea` property and reads `HasTownmap(...)` (no viewport
  parameter anywhere).
- No change to `ViewerGame.Worldmap.cs` (the travel/state logic file) — unrelated to rendering or
  input coordinate space.

## File Structure

- `src/Hexwaste.Viewer/ViewerGame.cs` — `Update()`'s worldmap input block (`:2633-2762`) and the
  `Draw()` call site (`:5791-5817`).
- `src/Hexwaste.Viewer/ViewerGame.Panels.cs` — `DrawEncounterPrompt()` (`:1221-1244`).
- `src/Hexwaste.Viewer/WorldmapScreen.cs` — `DrawChrome()` (`:194-259`) gains the new `scale`
  parameter; no other method in this file changes (every other positioning helper already takes
  `viewport` as a parameter and needs no internal changes).

## Task 1: Chrome frame

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2676`, `:2696`, `:2710-2721`, `:2725`, `:2728`,
  `:2741`, `:5791-5817`
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs:1221-1244`

**Interfaces:**
- Consumes: `UiScaleMatrix()`, `VirtualViewport()`, `UiMouse()` (Stage 1/2, already shipped), the
  `Point uiMouse` local already declared once in `Update()` (`ViewerGame.cs:1940` area) — reuse
  it, do not redeclare it.
- Produces: the `wms.DrawChrome(_spriteBatch, VirtualViewport(), ...)` call site at
  `ViewerGame.cs:5801`, which Task 2 will extend with one more argument (`UiScale()`) once it
  adds the `scale` parameter to `DrawChrome`'s signature.

- [ ] **Step 1: Convert the `Update()` worldmap input block's frame-level hit-tests**

Find, in `src/Hexwaste.Viewer/ViewerGame.cs` (inside the `if (_worldmapOpen)` block, around lines
2661-2684):

```csharp
                if (_worldmapScreen is { HasChrome: true, TownmapArea: { } town } tm)
                {
                    if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.T)
                        || IsKeyPressed(keyboard, Keys.W))
                    {
                        tm.TownmapArea = null;
                    }
                    else
                    {
                        int pick = -1;
                        for (int i = 0; i < town.Entrances.Count && i < 9 && pick < 0; i++)
                            if (IsKeyPressed(keyboard, Keys.D1 + i)
                                && town.Entrances[i] is { StartsOn: true, TownmapX: >= 0, TownmapY: >= 0 })
                                pick = i;
                        if (pick < 0 && click)
                            pick = tm.TownmapEntranceAt(mouse.X, mouse.Y, GraphicsDevice.Viewport.Bounds);
                        if (pick >= 0)
                            EnterTownmapEntrance(town, pick);
                    }
                    _previousMouse = mouse;
                    _previousKeyboard = keyboard;
                    base.Update(gameTime);
                    return;
                }
```

Replace with:

```csharp
                if (_worldmapScreen is { HasChrome: true, TownmapArea: { } town } tm)
                {
                    if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.T)
                        || IsKeyPressed(keyboard, Keys.W))
                    {
                        tm.TownmapArea = null;
                    }
                    else
                    {
                        int pick = -1;
                        for (int i = 0; i < town.Entrances.Count && i < 9 && pick < 0; i++)
                            if (IsKeyPressed(keyboard, Keys.D1 + i)
                                && town.Entrances[i] is { StartsOn: true, TownmapX: >= 0, TownmapY: >= 0 })
                                pick = i;
                        if (pick < 0 && click)
                            pick = tm.TownmapEntranceAt(uiMouse.X, uiMouse.Y, VirtualViewport());
                        if (pick >= 0)
                            EnterTownmapEntrance(town, pick);
                    }
                    _previousMouse = mouse;
                    _previousKeyboard = keyboard;
                    base.Update(gameTime);
                    return;
                }
```

- [ ] **Step 2: Convert the TOWN/WORLD switch and view-edge scroll hit-tests**

Find, in the same file (around lines 2689-2725):

```csharp
                if (_worldmapScreen is { HasChrome: true } wms)
                {
                    // P125: the TOWN/WORLD switch (T/W keys or the red button at 519,439)
                    // opens the current town's townmap (worldmap.cc:3144 gates on standing
                    // at a town whose townmap art exists).
                    WorldArea? here = _cities.Areas.FirstOrDefault(a => a.Index == _currentAreaId);
                    bool switchHit = IsKeyPressed(keyboard, Keys.T) || IsKeyPressed(keyboard, Keys.W)
                        || (click && wms.TownWorldSwitchRect(GraphicsDevice.Viewport.Bounds).Contains(mouse.X, mouse.Y));
                    if (switchHit && wms.HasTownmap(here))
                    {
                        _audio?.PlaySfx("ib1p1xx1");
                        wms.TownmapArea = here;
                        _previousMouse = mouse;
                        _previousKeyboard = keyboard;
                        base.Update(gameTime);
                        return;
                    }
                    // P123 chrome input: arrow keys + wheel + view-edge hover scroll the 1:1
                    // map view (wmInterfaceScroll); the town-tab red buttons quick-travel
                    // (KEY_CTRL_F1.. handler, worldmap.cc:3232); the tab arrows page the list.
                    const int scrollStep = 20; // fo2ce's wheel scroll step (:3260)
                    Rectangle wmView = wms.ViewRect(GraphicsDevice.Viewport.Bounds);
                    int dx = (keyboard.IsKeyDown(Keys.Right) ? scrollStep : 0) - (keyboard.IsKeyDown(Keys.Left) ? scrollStep : 0);
                    int dy = (keyboard.IsKeyDown(Keys.Down) ? scrollStep : 0) - (keyboard.IsKeyDown(Keys.Up) ? scrollStep : 0);
                    if (wmView.Contains(mouse.X, mouse.Y))
                    {
                        dy -= (mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue) / 120 * scrollStep;
                        const int edge = 8; // hover the view edge to scroll (wmMouseBkProc)
                        if (mouse.X < wmView.X + edge) dx -= scrollStep / 2;
                        if (mouse.X > wmView.Right - edge) dx += scrollStep / 2;
                        if (mouse.Y < wmView.Y + edge) dy -= scrollStep / 2;
                        if (mouse.Y > wmView.Bottom - edge) dy += scrollStep / 2;
                    }
                    if (dx != 0 || dy != 0)
                        wms.ScrollBy(dx, dy);

                    _hoveredArea = wms.HitTestChrome(mouse.X, mouse.Y, GraphicsDevice.Viewport.Bounds, WorldFog);
                    if (click)
                    {
                        (Rectangle up, Rectangle down) = wms.TabArrowRects(GraphicsDevice.Viewport.Bounds);
                        List<WorldArea> towns = wms.TabTowns(WorldFog);
                        if (_hoveredArea is not null && _hoveredArea.Index == _currentAreaId
                            && wms.HasTownmap(_hoveredArea))
                            wms.TownmapArea = _hoveredArea; // your own circle opens the townmap (:3144)
                        else if (_hoveredArea is not null)
                            TravelTo(_hoveredArea);
                        else if (up.Contains(mouse.X, mouse.Y))
                            wms.ScrollTabs(-1, WorldFog);
                        else if (down.Contains(mouse.X, mouse.Y))
                            wms.ScrollTabs(+1, WorldFog);
                        else
                            for (int row = 0; row < 7; row++)
                                if (wms.TabButtonRect(GraphicsDevice.Viewport.Bounds, row).Contains(mouse.X, mouse.Y)
                                    && wms.TabsOffset + row < towns.Count)
                                {
                                    _audio?.PlaySfx("ib1p1xx1");
                                    TravelTo(towns[wms.TabsOffset + row]);
                                    break;
                                }
                    }
                }
```

Replace with:

```csharp
                if (_worldmapScreen is { HasChrome: true } wms)
                {
                    // P125: the TOWN/WORLD switch (T/W keys or the red button at 519,439)
                    // opens the current town's townmap (worldmap.cc:3144 gates on standing
                    // at a town whose townmap art exists).
                    WorldArea? here = _cities.Areas.FirstOrDefault(a => a.Index == _currentAreaId);
                    bool switchHit = IsKeyPressed(keyboard, Keys.T) || IsKeyPressed(keyboard, Keys.W)
                        || (click && wms.TownWorldSwitchRect(VirtualViewport()).Contains(uiMouse.X, uiMouse.Y));
                    if (switchHit && wms.HasTownmap(here))
                    {
                        _audio?.PlaySfx("ib1p1xx1");
                        wms.TownmapArea = here;
                        _previousMouse = mouse;
                        _previousKeyboard = keyboard;
                        base.Update(gameTime);
                        return;
                    }
                    // P123 chrome input: arrow keys + wheel + view-edge hover scroll the 1:1
                    // map view (wmInterfaceScroll); the town-tab red buttons quick-travel
                    // (KEY_CTRL_F1.. handler, worldmap.cc:3232); the tab arrows page the list.
                    // Stage: UI Scale Stage 5 -- uiMouse/VirtualViewport() throughout: the view
                    // rect and every hit-test below live in the same virtual-canvas coordinate
                    // space DrawChrome's scaled content will render in (Task 2).
                    const int scrollStep = 20; // fo2ce's wheel scroll step (:3260)
                    Rectangle wmView = wms.ViewRect(VirtualViewport());
                    int dx = (keyboard.IsKeyDown(Keys.Right) ? scrollStep : 0) - (keyboard.IsKeyDown(Keys.Left) ? scrollStep : 0);
                    int dy = (keyboard.IsKeyDown(Keys.Down) ? scrollStep : 0) - (keyboard.IsKeyDown(Keys.Up) ? scrollStep : 0);
                    if (wmView.Contains(uiMouse.X, uiMouse.Y))
                    {
                        dy -= (mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue) / 120 * scrollStep;
                        const int edge = 8; // hover the view edge to scroll (wmMouseBkProc)
                        if (uiMouse.X < wmView.X + edge) dx -= scrollStep / 2;
                        if (uiMouse.X > wmView.Right - edge) dx += scrollStep / 2;
                        if (uiMouse.Y < wmView.Y + edge) dy -= scrollStep / 2;
                        if (uiMouse.Y > wmView.Bottom - edge) dy += scrollStep / 2;
                    }
                    if (dx != 0 || dy != 0)
                        wms.ScrollBy(dx, dy);

                    _hoveredArea = wms.HitTestChrome(uiMouse.X, uiMouse.Y, VirtualViewport(), WorldFog);
                    if (click)
                    {
                        (Rectangle up, Rectangle down) = wms.TabArrowRects(VirtualViewport());
                        List<WorldArea> towns = wms.TabTowns(WorldFog);
                        if (_hoveredArea is not null && _hoveredArea.Index == _currentAreaId
                            && wms.HasTownmap(_hoveredArea))
                            wms.TownmapArea = _hoveredArea; // your own circle opens the townmap (:3144)
                        else if (_hoveredArea is not null)
                            TravelTo(_hoveredArea);
                        else if (up.Contains(uiMouse.X, uiMouse.Y))
                            wms.ScrollTabs(-1, WorldFog);
                        else if (down.Contains(uiMouse.X, uiMouse.Y))
                            wms.ScrollTabs(+1, WorldFog);
                        else
                            for (int row = 0; row < 7; row++)
                                if (wms.TabButtonRect(VirtualViewport(), row).Contains(uiMouse.X, uiMouse.Y)
                                    && wms.TabsOffset + row < towns.Count)
                                {
                                    _audio?.PlaySfx("ib1p1xx1");
                                    TravelTo(towns[wms.TabsOffset + row]);
                                    break;
                                }
                    }
                }
```

Note: the scroll-wheel delta (`mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue`) is left
on raw `mouse` — it's a cumulative device counter, not a screen position, and has no coordinate
space to convert.

- [ ] **Step 3: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 4: Scope `DrawEncounterPrompt()` into its own scaled `SpriteBatch` block**

Find, in `src/Hexwaste.Viewer/ViewerGame.Panels.cs` (around lines 1221-1244):

```csharp
    /// <summary>The detected-encounter avoid prompt (phase-16 M1): a centred Yes/No
    /// box over the worldmap mirroring the engine's showDialogBox (worldmap.cc:3510).</summary>
    private void DrawEncounterPrompt()
    {
        if (_encounterPrompt is not { } p || _fontRenderer is null)
            return;
        _panelPixel ??= CreatePixel();
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int w = 360, h = 96;
        int x = (vp.Width - w) / 2, y = (vp.Height - h) / 2;
        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, w, h), new Color(8, 16, 8, 240));
        var green = new Color(0, 252, 0);
        string[] lines =
        [
            "You detect something up ahead.",
            p.Name ?? "An encounter.",
            "Do you wish to encounter it?  (Y / N)",
        ];
        int ty = y + 14;
        foreach (string line in lines)
        {
            int tw = _fontRenderer.MeasureWidth(line);
            _fontRenderer.Draw(_spriteBatch, line, new Vector2(x + (w - tw) / 2, ty), green);
            ty += _fontRenderer.LineHeight + 6;
        }
    }
```

Replace with:

```csharp
    /// <summary>The detected-encounter avoid prompt (phase-16 M1): a centred Yes/No
    /// box over the worldmap mirroring the engine's showDialogBox (worldmap.cc:3510).</summary>
    private void DrawEncounterPrompt()
    {
        if (_encounterPrompt is not { } p || _fontRenderer is null)
            return;
        _panelPixel ??= CreatePixel();

        // Stage: UI Scale Stage 5 -- no separable fallback exists here (this box has no art,
        // just a synthetic dark panel + text), so the whole method scopes into its own scoped,
        // scaled SpriteBatch block, matching the save/load/Pip-Boy/options shape.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        Rectangle vp = VirtualViewport();
        int w = 360, h = 96;
        int x = (vp.Width - w) / 2, y = (vp.Height - h) / 2;
        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, w, h), new Color(8, 16, 8, 240));
        var green = new Color(0, 252, 0);
        string[] lines =
        [
            "You detect something up ahead.",
            p.Name ?? "An encounter.",
            "Do you wish to encounter it?  (Y / N)",
        ];
        int ty = y + 14;
        foreach (string line in lines)
        {
            int tw = _fontRenderer.MeasureWidth(line);
            _fontRenderer.Draw(_spriteBatch, line, new Vector2(x + (w - tw) / 2, ty), green);
            ty += _fontRenderer.LineHeight + 6;
        }

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 6: Scope the `DrawChrome` call site into its own scaled `SpriteBatch` block**

Find, in `src/Hexwaste.Viewer/ViewerGame.cs` (around lines 5791-5817):

```csharp
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        if (_worldmapOpen)
        {
            // P123: the authentic chrome window (worldmap.frm, the scrolling 450x443 view,
            // town tabs, date/dial, globe/car monitor); the fitted view + corner car box
            // stay as the missing-art residual.
            if (_worldmapScreen is { HasChrome: true } wms)
            {
                Formats.CarState? car = _scriptHost?.Car;
                (int m, int d, int y) = _clock.Date; // month 1-12; the months strip is 0-based
                wms.DrawChrome(_spriteBatch, GraphicsDevice.Viewport.Bounds, _hoveredArea, WorldFog,
                    _worldPosX, _worldPosY,
                    _activeTravel?.Dest.WorldX ?? -1, _activeTravel?.Dest.WorldY ?? -1,
                    _clock.Hour, d, m - 1, y,
                    car?.InCar == true, car?.Fuel ?? 0, Formats.CarState.FuelMax, _carDotFrame);
            }
            else
            {
                _worldmapScreen?.Draw(_spriteBatch, GraphicsDevice.Viewport.Bounds, _hoveredArea, WorldFog);
                // The party dot: "you are here" whenever a worldmap position is known, and the
                // moving marker mid-travel (phase-17 M2/M3 — one dot, the unified position).
                if (_worldPosX >= 0 && _worldPosY >= 0)
                    _worldmapScreen?.DrawPartyDot(_spriteBatch, GraphicsDevice.Viewport.Bounds, _worldPosX, _worldPosY);
                DrawWorldmapCarBox(); // P122 corner fallback (the chrome hosts the real one)
            }
            DrawEncounterPrompt();
        }
```

Replace with:

```csharp
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        if (_worldmapOpen)
        {
            // P123: the authentic chrome window (worldmap.frm, the scrolling 450x443 view,
            // town tabs, date/dial, globe/car monitor); the fitted view + corner car box
            // stay as the missing-art residual.
            if (_worldmapScreen is { HasChrome: true } wms)
            {
                Formats.CarState? car = _scriptHost?.Car;
                (int m, int d, int y) = _clock.Date; // month 1-12; the months strip is 0-based

                // Stage: UI Scale Stage 5 -- DrawChrome has no separable fallback (the whole
                // method draws real chrome art), so it scopes into its own scoped, scaled
                // SpriteBatch block, matching the save/load/Pip-Boy/options shape. The scissored
                // inner map view (Task 2) is handled INSIDE DrawChrome itself -- it reopens the
                // batch mid-method for the scissor and must re-pass this same scale, which is
                // why DrawChrome takes scale as its own parameter rather than only a viewport.
                _spriteBatch.End();
                _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

                wms.DrawChrome(_spriteBatch, VirtualViewport(), _hoveredArea, WorldFog,
                    _worldPosX, _worldPosY,
                    _activeTravel?.Dest.WorldX ?? -1, _activeTravel?.Dest.WorldY ?? -1,
                    _clock.Hour, d, m - 1, y,
                    car?.InCar == true, car?.Fuel ?? 0, Formats.CarState.FuelMax, _carDotFrame);

                _spriteBatch.End();
                _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            }
            else
            {
                _worldmapScreen?.Draw(_spriteBatch, GraphicsDevice.Viewport.Bounds, _hoveredArea, WorldFog);
                // The party dot: "you are here" whenever a worldmap position is known, and the
                // moving marker mid-travel (phase-17 M2/M3 — one dot, the unified position).
                if (_worldPosX >= 0 && _worldPosY >= 0)
                    _worldmapScreen?.DrawPartyDot(_spriteBatch, GraphicsDevice.Viewport.Bounds, _worldPosX, _worldPosY);
                DrawWorldmapCarBox(); // P122 corner fallback (the chrome hosts the real one)
            }
            DrawEncounterPrompt();
        }
```

(This step deliberately does NOT add a `scale`/`UiScale()` argument to the `DrawChrome(...)` call
yet — `DrawChrome`'s signature does not accept one until Task 2. Building after this step
compiles cleanly, but the scissored content inside `DrawChrome` will render unscaled until Task 2
lands; this is the expected, temporary intermediate state described in Global Constraints, not a
regression to fix here.)

- [ ] **Step 7: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 8: Run the golden suites**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Run: `FALLOUT2_DIR=./game-data scripts/encounter-golden.sh check`
Expected: both report all scenarios passing, byte-identical — headless runs never call `Draw()`,
and none of the existing goldens open the worldmap through `DrawChrome`'s scaled paths, so this
task cannot change any golden output.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/ViewerGame.Panels.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the worldmap chrome frame to fill non-4:3 windows

Converts every ViewerGame.cs call site that feeds WorldmapScreen's
positioning helpers (TownmapEntranceAt, TownWorldSwitchRect, ViewRect,
HitTestChrome, TabArrowRects, TabButtonRect) from
GraphicsDevice.Viewport.Bounds/raw mouse to VirtualViewport()/uiMouse
-- none of WorldmapScreen's own methods need to change, since they
already take viewport as a parameter rather than reading
GraphicsDevice.Viewport internally. Scopes DrawEncounterPrompt() and
the DrawChrome() call site into scaled SpriteBatch blocks, matching
the whole-method-scaling shape used for Pip-Boy/options/save-load.

This is Task 1 of 2 for worldmap chrome (UI Scale Stage 5). The
scissored inner map view (world tiles/fog/circles + the townmap
sub-view) still renders unscaled inside the now-scaled frame until
Task 2 lands -- an expected, temporary intermediate state, not a
regression.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

## Task 2: Scissored map view

**Files:**
- Modify: `src/Hexwaste.Viewer/WorldmapScreen.cs:194-259` (`DrawChrome`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:5801` (the `DrawChrome(...)` call site Task 1 added)

**Interfaces:**
- Consumes: `UiScale()` (Stage 1, already shipped) — the caller passes this as `DrawChrome`'s new
  `scale` parameter.
- Produces: `DrawChrome`'s new signature —
  `public void DrawChrome(SpriteBatch spriteBatch, Rectangle viewport, float scale, WorldArea?
  hovered, Formats.Map.WorldmapFog? fog, int partyX, int partyY, int destX, int destY, int
  hourHhmm, int day, int month, int year, bool inCar, int fuel, int fuelMax, int carFrame)` — the
  `scale` parameter is inserted immediately after `viewport`. No other public method on
  `WorldmapScreen` changes signature.

- [ ] **Step 1: Add the `scale` parameter to `DrawChrome` and fix the scissor math**

Find, in `src/Hexwaste.Viewer/WorldmapScreen.cs` (around lines 190-236):

```csharp
    /// <summary>Draw the full chrome window: background, the scissored 1:1 map view (tiles,
    /// fog, city circles + names, destination + hotspot markers), the town tabs, date/time,
    /// the day/night dial, and the globe/car monitor. The sprite batch is restarted around
    /// the scissored section (the caller's batch must be a plain PointClamp Begin).</summary>
    public void DrawChrome(SpriteBatch spriteBatch, Rectangle viewport, WorldArea? hovered,
        Formats.Map.WorldmapFog? fog, int partyX, int partyY, int destX, int destY,
        int hourHhmm, int day, int month, int year, bool inCar, int fuel, int fuelMax, int carFrame)
    {
        Point o = ChromeOrigin(viewport);
        Rectangle view = ViewRect(viewport);
        spriteBatch.Draw(Frm(BgFrm)!, new Vector2(o.X, o.Y), Color.White);

        // ---- the scissored map view (everything positioned by world − scroll) ----
        spriteBatch.End();
        Rectangle oldScissor = _graphicsDevice.ScissorRectangle;
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: _scissor);
        _graphicsDevice.ScissorRectangle = Rectangle.Intersect(view, viewport);

        // P125: the townmap sub-view replaces the world content inside the same chrome
        // (wmTownMapRefresh :5915 blits the town art at the view spot; hotspot buttons at
        // the entrances' window coords; labels from worldmap.msg under each hotspot).
        if (TownmapArea is { } town && Frm(town.TownmapArtIdx) is { } townArt)
        {
            spriteBatch.Draw(townArt, new Vector2(view.X, view.Y), Color.White);
            Texture2D? spot = Frm(HotspotFrm);
            for (int i = 0; i < town.Entrances.Count; i++)
            {
                AreaEntrance e = town.Entrances[i];
                if (!e.StartsOn || e.TownmapX < 0 || e.TownmapY < 0)
                    continue;
                if (spot is not null)
                    spriteBatch.Draw(spot, new Vector2(o.X + e.TownmapX, o.Y + e.TownmapY), Color.White);
                string? label = TownmapMsg?.Invoke(200 + 10 * town.Index + i);
                if (label is not null)
                    _font?.Draw(spriteBatch, label, new Vector2(
                        o.X + e.TownmapX + (spot?.Width ?? 24) / 2 - (_font?.MeasureWidth(label) ?? 0) / 2,
                        o.Y + e.TownmapY + (spot?.Height ?? 26) + 4), new Color(0, 252, 0));
            }
        }
        else
        {
            DrawWorldView(spriteBatch, view, hovered, fog, partyX, partyY, destX, destY);
        }

        spriteBatch.End();
        _graphicsDevice.ScissorRectangle = oldScissor;
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        DrawTabs(spriteBatch, o, fog);
        DrawDate(spriteBatch, o, hourHhmm, day, month, year);
        if (Frm(DialFrm, (hourHhmm / 100 + 12) % Math.Max(1, DialFrameCount())) is { } dial)
            spriteBatch.Draw(dial, new Vector2(o.X + 532, o.Y + 48), Color.White); // WM_WINDOW_DIAL

        if (inCar)
        {
            // The car monitor at its real chrome spot (worldmap.cc:6179-6199).
            if (Frm(433, carFrame) is { } movie)
                spriteBatch.Draw(movie, new Vector2(o.X + 514, o.Y + 336), Color.White);
            if (Frm(CarOverlayFrm) is { } overlay)
                spriteBatch.Draw(overlay, new Vector2(o.X + 499, o.Y + 330), Color.White);
            int barH = (int)(70L * Math.Clamp(fuel, 0, fuelMax) / Math.Max(1, fuelMax));
            if (barH > 0)
                spriteBatch.Draw(_marker, new Rectangle(o.X + 500, o.Y + 339 + (70 - barH), 2, barH),
                    new Color(0, 196, 0));
        }
        else if (Frm(GlobeFrm) is { } globe)
        {
            spriteBatch.Draw(globe, new Vector2(o.X + 495, o.Y + 330), Color.White); // wmglobe stamp
        }
    }
```

Replace with:

```csharp
    /// <summary>Draw the full chrome window: background, the scissored 1:1 map view (tiles,
    /// fog, city circles + names, destination + hotspot markers), the town tabs, date/time,
    /// the day/night dial, and the globe/car monitor. The sprite batch is restarted around
    /// the scissored section (the caller's batch must be a plain PointClamp Begin).
    /// Stage: UI Scale Stage 5 -- `scale` is the same factor the caller's own scaled batch was
    /// opened with (UiScale()). Everything drawn here (backdrop, tabs, date/dial, monitor, and
    /// everything inside the scissor) is positioned in VIRTUAL units exactly as before -- the
    /// caller's transform matrix maps that to real screen pixels. GraphicsDevice.ScissorRectangle
    /// is the one exception: it is a DEVICE-PIXEL API that MonoGame does not run through the
    /// SpriteBatch transform, so its rectangle must be computed separately by multiplying the
    /// virtual clip rect by `scale` -- valid with no offset term because VirtualViewport()
    /// (the `viewport` this method receives) always starts at (0,0).</summary>
    public void DrawChrome(SpriteBatch spriteBatch, Rectangle viewport, float scale, WorldArea? hovered,
        Formats.Map.WorldmapFog? fog, int partyX, int partyY, int destX, int destY,
        int hourHhmm, int day, int month, int year, bool inCar, int fuel, int fuelMax, int carFrame)
    {
        Point o = ChromeOrigin(viewport);
        Rectangle view = ViewRect(viewport);
        spriteBatch.Draw(Frm(BgFrm)!, new Vector2(o.X, o.Y), Color.White);

        // ---- the scissored map view (everything positioned by world − scroll) ----
        spriteBatch.End();
        Rectangle oldScissor = _graphicsDevice.ScissorRectangle;
        Matrix scaleMatrix = Matrix.CreateScale(scale);
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: _scissor, transformMatrix: scaleMatrix);
        Rectangle clip = Rectangle.Intersect(view, viewport);
        _graphicsDevice.ScissorRectangle = new Rectangle(
            (int)(clip.X * scale), (int)(clip.Y * scale), (int)(clip.Width * scale), (int)(clip.Height * scale));

        // P125: the townmap sub-view replaces the world content inside the same chrome
        // (wmTownMapRefresh :5915 blits the town art at the view spot; hotspot buttons at
        // the entrances' window coords; labels from worldmap.msg under each hotspot).
        if (TownmapArea is { } town && Frm(town.TownmapArtIdx) is { } townArt)
        {
            spriteBatch.Draw(townArt, new Vector2(view.X, view.Y), Color.White);
            Texture2D? spot = Frm(HotspotFrm);
            for (int i = 0; i < town.Entrances.Count; i++)
            {
                AreaEntrance e = town.Entrances[i];
                if (!e.StartsOn || e.TownmapX < 0 || e.TownmapY < 0)
                    continue;
                if (spot is not null)
                    spriteBatch.Draw(spot, new Vector2(o.X + e.TownmapX, o.Y + e.TownmapY), Color.White);
                string? label = TownmapMsg?.Invoke(200 + 10 * town.Index + i);
                if (label is not null)
                    _font?.Draw(spriteBatch, label, new Vector2(
                        o.X + e.TownmapX + (spot?.Width ?? 24) / 2 - (_font?.MeasureWidth(label) ?? 0) / 2,
                        o.Y + e.TownmapY + (spot?.Height ?? 26) + 4), new Color(0, 252, 0));
            }
        }
        else
        {
            DrawWorldView(spriteBatch, view, hovered, fog, partyX, partyY, destX, destY);
        }

        spriteBatch.End();
        _graphicsDevice.ScissorRectangle = oldScissor;
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: scaleMatrix);

        DrawTabs(spriteBatch, o, fog);
        DrawDate(spriteBatch, o, hourHhmm, day, month, year);
        if (Frm(DialFrm, (hourHhmm / 100 + 12) % Math.Max(1, DialFrameCount())) is { } dial)
            spriteBatch.Draw(dial, new Vector2(o.X + 532, o.Y + 48), Color.White); // WM_WINDOW_DIAL

        if (inCar)
        {
            // The car monitor at its real chrome spot (worldmap.cc:6179-6199).
            if (Frm(433, carFrame) is { } movie)
                spriteBatch.Draw(movie, new Vector2(o.X + 514, o.Y + 336), Color.White);
            if (Frm(CarOverlayFrm) is { } overlay)
                spriteBatch.Draw(overlay, new Vector2(o.X + 499, o.Y + 330), Color.White);
            int barH = (int)(70L * Math.Clamp(fuel, 0, fuelMax) / Math.Max(1, fuelMax));
            if (barH > 0)
                spriteBatch.Draw(_marker, new Rectangle(o.X + 500, o.Y + 339 + (70 - barH), 2, barH),
                    new Color(0, 196, 0));
        }
        else if (Frm(GlobeFrm) is { } globe)
        {
            spriteBatch.Draw(globe, new Vector2(o.X + 495, o.Y + 330), Color.White); // wmglobe stamp
        }
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: FAIL — `DrawChrome` now requires a `scale` argument the call site in `ViewerGame.cs`
does not yet pass. This confirms the signature change took effect; the next step fixes the caller.

- [ ] **Step 3: Pass `UiScale()` at the `DrawChrome` call site**

Find, in `src/Hexwaste.Viewer/ViewerGame.cs` (the call site Task 1 added, around line 5804):

```csharp
                wms.DrawChrome(_spriteBatch, VirtualViewport(), _hoveredArea, WorldFog,
                    _worldPosX, _worldPosY,
                    _activeTravel?.Dest.WorldX ?? -1, _activeTravel?.Dest.WorldY ?? -1,
                    _clock.Hour, d, m - 1, y,
                    car?.InCar == true, car?.Fuel ?? 0, Formats.CarState.FuelMax, _carDotFrame);
```

Replace with:

```csharp
                wms.DrawChrome(_spriteBatch, VirtualViewport(), UiScale(), _hoveredArea, WorldFog,
                    _worldPosX, _worldPosY,
                    _activeTravel?.Dest.WorldX ?? -1, _activeTravel?.Dest.WorldY ?? -1,
                    _clock.Hour, d, m - 1, y,
                    car?.InCar == true, car?.Fuel ?? 0, Formats.CarState.FuelMax, _carDotFrame);
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Run the golden suites**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Run: `FALLOUT2_DIR=./game-data scripts/encounter-golden.sh check`
Expected: both report all scenarios passing, byte-identical, for the same reason as Task 1 (no
existing golden calls `DrawChrome`'s scaled path).

- [ ] **Step 6: Manual visual check — world view and townmap sub-view at a non-4:3 window**

Run, for the world view:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/worldmap-world.png \
  -- --worldmap
```
Read the resulting PNG. Expected: the whole chrome window (backdrop, town tabs, date strip,
day/night dial, globe or car monitor) fills the window at ~1.5× scale (for a 1280×720 window);
the scissored map view inside shows the world tiles, fog, and city circles at the SAME scale as
the frame around them — no native-size/mismatched content inside the clip, and no tile/marker
bleeding outside the 450×443 view's edges.

Run, for the townmap sub-view:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/worldmap-townmap.png \
  -- --townmap
```
Read the resulting PNG. Expected: the townmap art, hotspot entrance markers, and their labels
render inside the same scissored view at the same 1.5× scale as the surrounding chrome frame,
matching the world view's scaling exactly.

If either screenshot shows a scale mismatch between the frame and the scissored content, or shows
content clipped incorrectly (too small a clip region, or content bleeding past the frame edges),
that means the device-pixel scissor math in Step 1 needs revisiting before proceeding — do not
commit until both screenshots show correct, matching scaling.

- [ ] **Step 7: Commit**

```bash
git add src/Hexwaste.Viewer/WorldmapScreen.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the worldmap's scissored map view to match its frame

DrawChrome() gains a `scale` parameter (the caller's UiScale()) so its
scissored inner map view (world tiles/fog/circles/markers, or the
townmap sub-view) renders at the same scale as the chrome frame around
it. Two distinct fixes were needed: (1) the scissor block's inner
SpriteBatch.Begin() now re-passes the scale transform explicitly --
reopening a batch mid-method previously dropped any prior transform
silently; (2) GraphicsDevice.ScissorRectangle is a device-pixel API
MonoGame does not run through the SpriteBatch transform, so its
rectangle is now computed by multiplying the virtual-unit clip rect by
`scale` directly -- valid with no offset term because VirtualViewport()
always starts at (0,0).

This is Task 2 of 2 for worldmap chrome (UI Scale Stage 5) and
completes UI Scale for the entire Viewer -- every screen now scales
uniformly to fill non-4:3 windows.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```
