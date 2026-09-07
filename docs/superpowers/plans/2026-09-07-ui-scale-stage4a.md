# UI Scale — Stage 4a: Action Menu, Elevator Picker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the right-click action menu and the elevator level picker render at the same
uniform scale as the twelve screens Stage 2/3a/3b/3c already migrated, filling non-4:3 windows
like `fallout2-ce`'s stretch, while every other screen (including the still-unscaled HUD bar,
deliberately deferred) keeps its native size.

**Architecture:** The elevator picker gets the Skilldex/perk-picker/aim-dialog treatment (scope
only the art path; the true fallback, `DrawElevatorPickerFallback`, stays unscaled) plus the
Tactics-style `Point uiMouse` parameter addition for its `Update()`-side hit-test. The action menu
is structurally different from every screen tackled so far: its position (`_actionMenuPos`) is
computed ONCE, inside `OpenActionMenu` — a method called from `Update()` at right-click time, not
recomputed every `Draw()` call — so its fix lives in that `Update()`-side clamp math and its call
site, not in a `Draw`-side origin helper. `DrawActionMenu` itself has no separable fallback (its
missing-icon fallback is an inline per-row text substitution, not a whole-method dispatch like
Skilldex's), so the whole method scales, like save/load/tactics/preferences.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`SpriteBatch.Begin`/`End`, already-shipped
`UiScale`/`VirtualViewport`/`UiMouse`/`UiScaleMatrix` from Stage 1/2/3a/3b/3c).

## Global Constraints

- Scale model: `scale = min(viewportWidth/640, viewportHeight/480)` (Stage 1, shipped,
  unit-tested — do not touch).
- Only the action menu and the elevator picker scale in this plan. No other screen (including the
  HUD bar, deliberately deferred to its own later stage) changes.
- `DrawElevatorPickerFallback` (the elevator picker's art-missing fallback, drawn at a fixed,
  non-`VirtualViewport()`-relative position via the raw device viewport) stays completely
  untouched and unscaled — matching the established precedent for a degraded fallback path.
- `DrawActionMenu` has no separate fallback method to carve out — its per-row icon-missing
  fallback (a flat rect + text label) is drawn inline, using the same `_actionMenuPos`-derived
  `pos` as the icon-texture path — so the whole method scales, matching the save/load/Pip-Boy/
  options/automap/preferences/tactics shape.
- Every position read from a real device (mouse) or used to lay out scaled content must come
  from `UiMouse()`/`VirtualViewport()` inside a scaled block — never a raw
  `GraphicsDevice.Viewport`/`Mouse.GetState().X/Y` mixed into scaled content.
- `OpenActionMenu`'s clamp math must switch to `VirtualViewport()`, and its ONE caller in
  `Update()` must pass an already-`UiMouse()`-transformed point — this is the one place in this
  stage where a `Draw()`-only fix is insufficient; `_actionMenuPos` must be stored in
  virtual-canvas coordinates from the moment it's created, or everything downstream
  (`DrawActionMenu`, `ActionMenuRowAt`) would silently disagree with the cursor.
- `ViewerGame.Harness.cs`'s `ActionMenuProbe` (`OpenActionMenu(amObj, 100, 100)`, hardcoded
  literal coordinates) and `ElevatorOpen`/`ElevatorRide` (set `_elevatorPicker` directly, no pixel
  math) need NO code change — confirmed during planning that neither touches any helper this
  stage converts in a way that would break.
- Both `DrawActionMenu()` and `DrawElevatorPicker()` are called OUTSIDE the
  `if (_worldmapOpen) {...} else {...}` branch in `Draw()` (unlike every screen migrated so far,
  which are all inside the `else`) — each self-guards on its own state
  (`_actionMenuObj is null` / `_elevatorPicker is not { }`), so this is expected to be harmless,
  but the manual visual check should include a quick sanity pass confirming neither screen
  renders or misbehaves while the worldmap chrome is open.

## File Structure

- `src/Hexwaste.Viewer/ViewerGame.Elevator.cs` — `ElevatorWindowPos`, `UpdateElevatorPicker`,
  `DrawElevatorPicker`.
- `src/Hexwaste.Viewer/ViewerGame.ActionMenu.cs` — `OpenActionMenu`, `DrawActionMenu`.
- `src/Hexwaste.Viewer/ViewerGame.cs` — the `Update()` call sites for `UpdateElevatorPicker`,
  `OpenActionMenu`, and `ActionMenuRowAt`.

## Task 1: Elevator picker

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Elevator.cs:167-169` (`ElevatorWindowPos`),
  `:183-249` (`UpdateElevatorPicker`), `:251-288` (`DrawElevatorPicker`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2200` (Update call site)

**Interfaces:**
- Consumes: `UiScaleMatrix()`, `VirtualViewport()`, `UiMouse()` (Stage 1/2, already shipped),
  the `Point uiMouse` local already declared in `Update()` (Stage 2 Task 1).
- Produces: `UpdateElevatorPicker(KeyboardState keyboard, MouseState mouse, Point uiMouse,
  GameTime gameTime)` — a new parameter, following the exact shape Stage 3c's
  `HandleTacticsInput(MouseState mouse, Point uiMouse, KeyboardState keyboard)` established.
  Nothing else in this plan consumes it.

- [ ] **Step 1: Switch `ElevatorWindowPos()` to the virtual viewport**

Find, in `src/Hexwaste.Viewer/ViewerGame.Elevator.cs` (around line 165-169):

```csharp
    /// <summary>The picker window's top-left screen position (the art centered like
    /// elevatorWindowInit :545, or the text-fallback box).</summary>
    private Point ElevatorWindowPos(Texture2D bg) => new(
        (GraphicsDevice.Viewport.Width - bg.Width) / 2,
        (GraphicsDevice.Viewport.Height - bg.Height) / 2);
```

Replace with:

```csharp
    /// <summary>The picker window's top-left screen position (the art centered like
    /// elevatorWindowInit :545, or the text-fallback box).
    /// Stage 4a (UI Scale): reads the virtual-canvas viewport — DrawElevatorPicker's art path
    /// draws inside its own scoped, scaled SpriteBatch block. DrawElevatorPickerFallback
    /// deliberately does NOT read VirtualViewport() — that fallback stays unscaled.</summary>
    private Point ElevatorWindowPos(Texture2D bg) => new(
        (VirtualViewport().Width - bg.Width) / 2,
        (VirtualViewport().Height - bg.Height) / 2);
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Add a `Point uiMouse` parameter to `UpdateElevatorPicker` and route its hit-test through it**

Find, in the same file (around line 183, the method signature, and line 226-233, the hit-test):

```csharp
    private void UpdateElevatorPicker(KeyboardState keyboard, MouseState mouse, GameTime gameTime)
    {
```

Replace with:

```csharp
    private void UpdateElevatorPicker(KeyboardState keyboard, MouseState mouse, Point uiMouse, GameTime gameTime)
    {
```

Then find (around line 226-233):

```csharp
        if (pick < 0 && mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
            && InterfaceFrm(ElevatorTables.Backgrounds[picker.Type].BackgroundFrmId) is { } bg
            && InterfaceFrm(ElevatorTables.ButtonUpFrmId) is { } btn)
        {
            for (int i = 0; i < picker.Levels && pick < 0; i++)
                if (ElevatorButtonRect(bg, btn, i).Contains(mouse.X, mouse.Y))
                    pick = i;
        }
```

Replace with:

```csharp
        if (pick < 0 && mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
            && InterfaceFrm(ElevatorTables.Backgrounds[picker.Type].BackgroundFrmId) is { } bg
            && InterfaceFrm(ElevatorTables.ButtonUpFrmId) is { } btn)
        {
            for (int i = 0; i < picker.Levels && pick < 0; i++)
                if (ElevatorButtonRect(bg, btn, i).Contains(uiMouse.X, uiMouse.Y))
                    pick = i;
        }
```

(`mouse.LeftButton`/`_previousMouse.LeftButton` — button state, not position — correctly stay on
the original `MouseState mouse` parameter.)

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Scope `DrawElevatorPicker`'s art path into its own scaled `SpriteBatch` block**

Find, in the same file (around line 251-265):

```csharp
    private void DrawElevatorPicker()
    {
        if (_elevatorPicker is not { } picker || _fontRenderer is null)
            return;

        (int bgId, int panelId) = ElevatorTables.Backgrounds[picker.Type];
        Texture2D? bg = InterfaceFrm(bgId);
        Texture2D? btnUp = InterfaceFrm(ElevatorTables.ButtonUpFrmId);
        Texture2D? btnDown = InterfaceFrm(ElevatorTables.ButtonDownFrmId);
        Texture2D? gauge = InterfaceFrm(ElevatorTables.GaugeFrmId);
        if (bg is null || btnUp is null || btnDown is null || gauge is null)
        {
            DrawElevatorPickerFallback(picker);
            return;
        }

        Point p = ElevatorWindowPos(bg);
```

Replace with:

```csharp
    private void DrawElevatorPicker()
    {
        if (_elevatorPicker is not { } picker || _fontRenderer is null)
            return;

        (int bgId, int panelId) = ElevatorTables.Backgrounds[picker.Type];
        Texture2D? bg = InterfaceFrm(bgId);
        Texture2D? btnUp = InterfaceFrm(ElevatorTables.ButtonUpFrmId);
        Texture2D? btnDown = InterfaceFrm(ElevatorTables.ButtonDownFrmId);
        Texture2D? gauge = InterfaceFrm(ElevatorTables.GaugeFrmId);
        if (bg is null || btnUp is null || btnDown is null || gauge is null)
        {
            DrawElevatorPickerFallback(picker);
            return;
        }

        // Stage 4a (UI Scale): the art path draws into its own scoped, scaled SpriteBatch block —
        // same technique as Stage 3a's DrawSkilldex/DrawPerkPicker — so the elevator picker
        // matches fo2ce's fullscreen stretch while DrawElevatorPickerFallback (above) stays
        // unscaled, matching the established fallback precedent.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        Point p = ElevatorWindowPos(bg);
```

Then find, later in the same method (around line 281-287):

```csharp
        MouseState mouse = Mouse.GetState();
        for (int i = 0; i < picker.Levels; i++)
        {
            Rectangle r = ElevatorButtonRect(bg, btnUp, i);
            bool pressed = mouse.LeftButton == ButtonState.Pressed && r.Contains(mouse.X, mouse.Y);
            _spriteBatch.Draw(pressed ? btnDown : btnUp, new Vector2(r.X, r.Y), Color.White);
        }
    }
```

Replace with:

```csharp
        MouseState rawMouse = Mouse.GetState();
        Point mouse = UiMouse();
        for (int i = 0; i < picker.Levels; i++)
        {
            Rectangle r = ElevatorButtonRect(bg, btnUp, i);
            bool pressed = rawMouse.LeftButton == ButtonState.Pressed && r.Contains(mouse.X, mouse.Y);
            _spriteBatch.Draw(pressed ? btnDown : btnUp, new Vector2(r.X, r.Y), Color.White);
        }

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

(`rawMouse.LeftButton` — button state — needs its own fresh `Mouse.GetState()` read since `mouse`
is now the scaled `Point` from `UiMouse()`, matching the pattern Stage 3c's aim-dialog task used
for its own `cancelPressed` check.)

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Update `UpdateElevatorPicker`'s one caller in `Update()`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2196-2204):

```csharp
        // P113 (item 5): the elevator level picker is modal — it owns input until a level or Esc.
        // P119: it also owns the in-flight gauge ride, which ignores input until the teleport.
        if (_elevatorPicker is not null)
        {
            UpdateElevatorPicker(keyboard, mouse, gameTime);
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }
```

Replace with:

```csharp
        // P113 (item 5): the elevator level picker is modal — it owns input until a level or Esc.
        // P119: it also owns the in-flight gauge ride, which ignores input until the teleport.
        if (_elevatorPicker is not null)
        {
            UpdateElevatorPicker(keyboard, mouse, uiMouse, gameTime);
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }
```

- [ ] **Step 8: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 9: Run the golden suite covering the elevator picker**

Run: `FALLOUT2_DIR=./game-data scripts/encounter-golden.sh check`
Expected: all scenarios pass — none of them drive the elevator picker's simulated-click path (any
elevator-related scenarios use direct state-setting CLI actions per `ViewerGame.Harness.cs`'s
`ElevatorOpen`/`ElevatorRide`, confirmed during planning to bypass this task's pixel math
entirely), so a pass here is a broad regression net, not coverage evidence for this specific
change. Also run `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check` for the same reason.

- [ ] **Step 10: Manual visual check — elevator picker at a non-4:3 window**

Run (`--elevator-open <type>[:<level>]` maps to `Program.cs:453-459`'s
`StartupAction.ElevatorOpen`, which sets `_elevatorPicker` directly — no live click needed to
reach the open state):
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage4a-elevator.png \
  -- --elevator-open 0
```
(if type `0` doesn't resolve to a valid elevator background, check `ElevatorTables.Backgrounds`
in the Formats project for a valid index — the exact type number is cosmetic to this check, any
valid one that resolves real art is sufficient.)
Read the resulting PNG. Expected: the elevator panel art renders at 1.5× baseline, centered,
undistorted, the gauge and level buttons all legible and correctly aligned with the background
art. If a click can be simulated, confirm clicking a specific level button selects that level (not
an adjacent one). Also confirm the elevator picker does not appear/misbehave if triggered while
the worldmap chrome happens to be open (a quick sanity check, not expected to reveal anything —
see this plan's Global Constraints).

- [ ] **Step 11: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Elevator.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the elevator picker to fill non-4:3 windows

Wires Stage 1/2/3a/3b/3c's UiScale infrastructure into
DrawElevatorPicker's art path: it now draws inside its own scoped,
scaled SpriteBatch block, with ElevatorWindowPos() reading
VirtualViewport() instead of the raw device viewport.
DrawElevatorPickerFallback (art missing) stays untouched and
unscaled. UpdateElevatorPicker gains a Point uiMouse parameter
(alongside its existing MouseState mouse, kept for button-state
reads) -- the same shape Stage 3c's HandleTacticsInput established --
so its hit-test agrees with the scaled draw.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

## Task 2: Action menu

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.ActionMenu.cs:55-75` (`OpenActionMenu`),
  `:171-193` (`DrawActionMenu`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2947`, `:2960` (Update call sites)

**Interfaces:**
- Consumes: same as Task 1.
- Produces: nothing new consumed by a later task.

- [ ] **Step 1: Switch `OpenActionMenu`'s clamp math to the virtual viewport**

Find, in `src/Hexwaste.Viewer/ViewerGame.ActionMenu.cs` (around line 53-75):

```csharp
    /// <summary>Open the action menu for <paramref name="obj"/> at the cursor — the item list is the
    /// engine's per-object-type build (Formats.Map.ActionMenu), clamped on screen.</summary>
    private void OpenActionMenu(MapObject obj, int mx, int my)
    {
        if (_dude is null)
            return;
        ObjectType type = Fid.Type(obj.Fid);
        bool isDude = obj == _dude.Dude;
        bool activeCritter = type == ObjectType.Critter && !obj.IsDead;
        bool canTalk = activeCritter && !isDude;                          // alive non-dude critter (assumed talkable)
        bool inCombat = _combat.Phase != Formats.Combat.CombatPhase.Idle;
        bool isContainer = IsContainer(obj) || (type == ObjectType.Item && obj.Inventory.Count > 0);
        bool sceneryCanUse = type == ObjectType.Scenery;                  // InteractWith routes (door/use_p_proc/no-op)
        bool canPush = activeCritter && !isDude && CanPushCritter(obj);   // P113 (item 6)
        _actionMenuItems = ActionMenu.Build(type, isDude, activeCritter, canTalk, inCombat, sceneryCanUse, isContainer, canPush);
        _actionMenuObj = obj;

        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int h = _actionMenuItems.Count * ActionIconSize;
        _actionMenuPos = new Point(
            Math.Clamp(mx, 0, Math.Max(0, vp.Width - ActionIconSize)),
            Math.Clamp(my, 0, Math.Max(0, vp.Height - h)));
    }
```

Replace with:

```csharp
    /// <summary>Open the action menu for <paramref name="obj"/> at the cursor — the item list is the
    /// engine's per-object-type build (Formats.Map.ActionMenu), clamped on screen.
    /// Stage 4a (UI Scale): (mx, my) must already be in virtual-canvas coordinates (the caller
    /// passes UiMouse()'s point, not the raw device mouse) — _actionMenuPos is stored once here and
    /// simply read, unchanged, by every later DrawActionMenu/ActionMenuRowAt call until the menu
    /// closes, so there is no later origin-helper conversion point the way every other screen has.</summary>
    private void OpenActionMenu(MapObject obj, int mx, int my)
    {
        if (_dude is null)
            return;
        ObjectType type = Fid.Type(obj.Fid);
        bool isDude = obj == _dude.Dude;
        bool activeCritter = type == ObjectType.Critter && !obj.IsDead;
        bool canTalk = activeCritter && !isDude;                          // alive non-dude critter (assumed talkable)
        bool inCombat = _combat.Phase != Formats.Combat.CombatPhase.Idle;
        bool isContainer = IsContainer(obj) || (type == ObjectType.Item && obj.Inventory.Count > 0);
        bool sceneryCanUse = type == ObjectType.Scenery;                  // InteractWith routes (door/use_p_proc/no-op)
        bool canPush = activeCritter && !isDude && CanPushCritter(obj);   // P113 (item 6)
        _actionMenuItems = ActionMenu.Build(type, isDude, activeCritter, canTalk, inCombat, sceneryCanUse, isContainer, canPush);
        _actionMenuObj = obj;

        Rectangle vp = VirtualViewport();
        int h = _actionMenuItems.Count * ActionIconSize;
        _actionMenuPos = new Point(
            Math.Clamp(mx, 0, Math.Max(0, vp.Width - ActionIconSize)),
            Math.Clamp(my, 0, Math.Max(0, vp.Height - h)));
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Scope `DrawActionMenu` into its own scaled `SpriteBatch` block**

Find, in the same file (around line 169-193):

```csharp
    /// <summary>Render the action-menu icon stack (the hovered row uses the H/highlight art), falling
    /// back to text labels when the icon FRMs are absent (the Skilldex text-then-art pattern).</summary>
    private void DrawActionMenu()
    {
        if (_actionMenuObj is null)
            return;
        MouseState m = Mouse.GetState();
        int hover = ActionMenuRowAt(m.X, m.Y);
        for (int i = 0; i < _actionMenuItems.Count; i++)
        {
            (Texture2D? n, Texture2D? h) = ActionIcon(_actionMenuItems[i]);
            var pos = new Vector2(_actionMenuPos.X, _actionMenuPos.Y + i * ActionIconSize);
            Texture2D? tex = (i == hover ? h : n) ?? n;
            if (tex is not null)
                _spriteBatch.Draw(tex, pos, Color.White);
            else if (_fontRenderer is not null)
            {
                _panelPixel ??= CreatePixel();
                _spriteBatch.Draw(_panelPixel, new Rectangle((int)pos.X, (int)pos.Y, 90, ActionIconSize),
                    new Color(8, 8, 8, 230));
                _fontRenderer.Draw(_spriteBatch, _actionMenuItems[i].ToString(), pos + new Vector2(4, 14),
                    i == hover ? new Color(252, 252, 84) : new Color(0, 252, 0));
            }
        }
    }
```

Replace with:

```csharp
    /// <summary>Render the action-menu icon stack (the hovered row uses the H/highlight art), falling
    /// back to text labels when the icon FRMs are absent (the Skilldex text-then-art pattern).
    /// Stage 4a (UI Scale): unlike Skilldex/perk-picker, the icon-missing fallback here is an
    /// inline per-row substitution (not a separate whole-method dispatch) that shares the same
    /// _actionMenuPos-derived pos as the icon-texture path, so the whole method scales inside one
    /// scoped, scaled SpriteBatch block — there is no separable unscaled branch to carve out.</summary>
    private void DrawActionMenu()
    {
        if (_actionMenuObj is null)
            return;

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        Point m = UiMouse();
        int hover = ActionMenuRowAt(m.X, m.Y);
        for (int i = 0; i < _actionMenuItems.Count; i++)
        {
            (Texture2D? n, Texture2D? h) = ActionIcon(_actionMenuItems[i]);
            var pos = new Vector2(_actionMenuPos.X, _actionMenuPos.Y + i * ActionIconSize);
            Texture2D? tex = (i == hover ? h : n) ?? n;
            if (tex is not null)
                _spriteBatch.Draw(tex, pos, Color.White);
            else if (_fontRenderer is not null)
            {
                _panelPixel ??= CreatePixel();
                _spriteBatch.Draw(_panelPixel, new Rectangle((int)pos.X, (int)pos.Y, 90, ActionIconSize),
                    new Color(8, 8, 8, 230));
                _fontRenderer.Draw(_spriteBatch, _actionMenuItems[i].ToString(), pos + new Vector2(4, 14),
                    i == hover ? new Color(252, 252, 84) : new Color(0, 252, 0));
            }
        }

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Route the `Update()` call sites through `uiMouse`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2942-2948):

```csharp
        if (!_debugForceActionMenu && mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
        {
            if (_actionMenuObj is not null)
                CloseActionMenu();
            else if (_hoveredObject is not null)
                OpenActionMenu(_hoveredObject, mouse.X, mouse.Y);
        }
```

Replace with:

```csharp
        if (!_debugForceActionMenu && mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
        {
            if (_actionMenuObj is not null)
                CloseActionMenu();
            else if (_hoveredObject is not null)
                OpenActionMenu(_hoveredObject, uiMouse.X, uiMouse.Y);
        }
```

Then find (around line 2958-2965):

```csharp
            if (_actionMenuObj is not null)
            {
                int amRow = ActionMenuRowAt(mouse.X, mouse.Y);
                if (amRow >= 0)
                    DispatchActionMenu(amRow);
                else
                    CloseActionMenu();
            }
```

Replace with:

```csharp
            if (_actionMenuObj is not null)
            {
                int amRow = ActionMenuRowAt(uiMouse.X, uiMouse.Y);
                if (amRow >= 0)
                    DispatchActionMenu(amRow);
                else
                    CloseActionMenu();
            }
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Run the golden suites**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Run: `FALLOUT2_DIR=./game-data scripts/encounter-golden.sh check`
Expected: all scenarios pass. `ViewerGame.Harness.cs`'s `ActionMenuProbe` calls `OpenActionMenu`
with hardcoded literal coordinates `(100, 100)` (not derived from a simulated click), confirmed
during planning to sit comfortably inside both the real device viewport and any
`VirtualViewport()` at every resolution this project runs at — so this is a regression net, not
targeted coverage of the `Update()`-side `uiMouse` plumbing this task changes; Step 8 is the real
verification.

- [ ] **Step 8: Manual visual check — action menu opened at a specific screen position**

Run (`--action-menu <hex>` maps to `Program.cs:346-347`'s `StartupAction.ActionMenuProbe`,
which calls `OpenActionMenu(amObj, 100, 100)` — a hardcoded position, confirmed harmless in the
Global Constraints — and sets `_debugForceActionMenu = true` to hold the menu open for the
screenshot, per `ViewerGame.Harness.cs:392-407`; `21101` is a known-good hex with an object on it,
reused from this session's earlier dialog-frame/Skilldex screenshots):
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage4a-actionmenu.png \
  -- --action-menu 21101
```
(if `21101` has no object on the default starting map, use `--goto <tile>` first to move near a
known NPC/object, or pick any hex confirmed to hold an object via `--map-objects`-style tooling
from earlier sessions.)
Read the resulting PNG. Expected: the action-menu icon stack renders at 1.5× baseline, undistorted,
positioned near where the triggering click/probe point was (accounting for the clamp). If a live
right-click can be simulated at a specific screen position, confirm the menu opens at the cursor
(not offset), and that clicking a specific row fires that row's action — this is the check that
proves the `Update()`-side `OpenActionMenu`/`uiMouse` plumbing (this task's only non-mechanical
change) is correct, since there's no origin helper to inspect statically the way every other
screen has. Also confirm the action menu does not appear/misbehave if triggered while the
worldmap chrome happens to be open (a quick sanity check, not expected to reveal anything).

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.ActionMenu.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the action menu to fill non-4:3 windows

Wires Stage 1/2/3a/3b/3c's UiScale infrastructure into the action
menu -- structurally different from every screen migrated so far,
since its position (_actionMenuPos) is computed once, inside
OpenActionMenu (called from Update() at right-click time), rather
than recomputed every Draw() call. OpenActionMenu's clamp now reads
VirtualViewport(), and its Update() call site passes an
already-UiMouse()-transformed point instead of the raw device mouse,
so _actionMenuPos is stored in virtual-canvas coordinates from
creation -- DrawActionMenu/ActionMenuRowAt then need no origin-helper
conversion of their own. DrawActionMenu (no separable fallback: its
missing-icon substitution is inline, not a whole-method dispatch) now
draws its entire body inside one scoped, scaled SpriteBatch block.

This closes UI Scale Stage 4a. The HUD bar remains deferred to its own
dedicated stage (its _hudBarHeight field has 8 dependent read sites
across already-shipped code with an unresolved unit convention).
Inventory and worldmap chrome (Stage 5) also remain deferred.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```
