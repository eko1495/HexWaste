# UI Scale — Stage 3a: Skilldex, Perk Picker, Save/Load Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Skilldex, perk-picker, and save/load screens render at the same uniform scale
as the dialog panel and main-menu family (Stage 2), filling non-4:3 windows like `fallout2-ce`'s
stretch, while every other screen keeps its native size.

**Architecture:** Each screen already isolates its origin math in one or two small private
helpers (`SkilldexOrigin`, `PerkWindowOrigin`, `SaveLoadPanelRect`/`SaveLoadSlotRect`) whose
callers are entirely local to `ViewerGame.Panels.cs` (confirmed by a whole-tree `grep` during
planning — no `ViewerGame.Harness.cs` caller, unlike Pip-Boy/Options which are deferred to Stage
3b for exactly that reason). Each screen's `Draw*` method opens its own scoped, scaled
`SpriteBatch` block — the same `End()`/`Begin(transformMatrix: UiScaleMatrix())`/…/`End()`/
`Begin()` technique Stage 2 established for the dialog panel and main-menu family — around just
its own content.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`SpriteBatch.Begin`/`End`, `Matrix.CreateScale`,
already-shipped `UiScale`/`VirtualViewport`/`UiMouse`/`UiScaleMatrix` from Stage 1/Stage 2).

## Global Constraints

- Scale model: `scale = min(viewportWidth/640, viewportHeight/480)` (Stage 1's
  `UiScale.ComputeScale`, shipped and unit-tested — do not touch).
- Only Skilldex, the perk picker, and save/load scale in this stage. No other screen (inventory,
  character sheet, Pip-Boy, automap, options, preferences, aim dialog, tactics, HUD bar, action
  menu, elevator picker, worldmap chrome, Credits, Endgame, the death screen, the main-menu
  family) changes in this stage.
- **Inventory is explicitly excluded from Stage 3a**, despite being named in the original spec.
  A closer read during planning found its "fallback" is not one separable branch the way
  Skilldex/perk-picker's is: `InvBoxOrigin()` returns `Point?` (null = art absent), and roughly
  six different helper methods (`WeaponSlotRect`, `ArmorSlotRect`, `LeftWeaponSlotRect`,
  `InvBoxDoneRect`, `LootDoneRect`, `InventoryPanelX`) each independently branch on that null with
  their own **fixed, viewport-INdependent** fallback positions (e.g. `new Rectangle(420, 96, 90,
  60)`, explicitly documented as "the boxes layout the harness/goldens use"), plus a drag-ghost
  icon drawn at the raw, untransformed `_previousMouse` position (`DrawEquipSlots`,
  `ViewerGame.Panels.cs:2083`). This is a meaningfully different, higher-risk shape than the other
  three screens and deserves its own carefully-scoped follow-up plan, not a rushed fit into this
  one.
- **Skilldex/perk-picker's art-missing text fallback stays unscaled**, matching the precedent
  Stage 2 set for the main-menu family's fallback: only the `SKLDXBOX.frm`/`PERKWIN.frm` art path
  scales; `DrawSkilldexTextFallback`/`DrawPerkPickerTextFallback` are untouched.
- **Save/load has no separate fallback method** — `_saveLoadArt` gates a boolean inside
  `SaveLoadPanelRect`/`SaveLoadSlotRect`/`DrawSaveLoad` themselves, and both the art and non-art
  branches already compute their position from the *same* viewport-relative formula (unlike
  Skilldex/perk-picker's fallback, which uses independent, disconnected constants). There is no
  clean seam to split scaled-from-unscaled, so Stage 3a scales **both** branches uniformly — a
  deliberate, narrower deviation from the "fallback stays unscaled" precedent, justified because
  there is no separate function to carve the boundary at.
- Every position read from a real device (mouse) or used to lay out scaled content must come
  from `UiMouse()`/`VirtualViewport()` inside a scaled block — never a raw
  `GraphicsDevice.Viewport`/`Mouse.GetState().X/Y` mixed into scaled content, and never
  `UiMouse()`/`VirtualViewport()` used for content staying in an unscaled block.
- `_hudBarHeight` (`ViewerGame.cs:395`) is a **device-pixel constant** — `InterfaceBar.Height`,
  the HUD bar's native texture height, entirely independent of window size and NOT itself scaled
  until a later stage. Any scaled-block calculation that anchors to it (only `SkilldexOrigin()`
  does, in this stage) must first convert it to virtual-canvas units (`_hudBarHeight / UiScale()`)
  — using it raw inside virtual-space math would anchor Skilldex to the wrong height once the
  scale transform is applied. This is the kind of unit-mixing bug Stage 2's final review caught
  for `MenuOrigin()`; it is called out here up front instead.

## File Structure

- `src/Hexwaste.Viewer/ViewerGame.Panels.cs` — `SkilldexOrigin`, `DrawSkilldex`, `SkilldexRowAt`
  (unchanged), `PerkWindowOrigin`, `DrawPerkPicker`, `PerkPickerRowAt` (unchanged),
  `SaveLoadPanelRect`, `SaveLoadSlotRect`, `DrawSaveLoad`.
- `src/Hexwaste.Viewer/ViewerGame.cs` — three `Update()` hit-test call sites (Skilldex, perk
  picker, save/load), all switched to read the already-existing `uiMouse` local (declared by
  Stage 2 Task 1, reused here — no new declaration).

## Task 1: Skilldex

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs:604-680` (`SkilldexOrigin`, `DrawSkilldex`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2208-2209` (Update hit-test)

**Interfaces:**
- Consumes: `UiScaleMatrix()`, `VirtualViewport()`, `UiMouse()` (Stage 1/Stage 2, already shipped),
  the `Point uiMouse` local already declared in `Update()` (Stage 2 Task 1).
- Produces: nothing new consumed by a later task in this plan.

- [ ] **Step 1: Switch `SkilldexOrigin()` to the virtual viewport, with the `_hudBarHeight` unit fix**

Find, in `src/Hexwaste.Viewer/ViewerGame.Panels.cs` (around line 604-612):

```csharp
    /// <summary>Top-left of the Skilldex box: bottom-right, just above the HUD bar
    /// (skilldex.cc:225-226 — right margin 4, bottom margin 6). btnW/btnH = the SKLDXOFF
    /// button size; row i sits at bar-local (15, 45 + i*36).</summary>
    private Point SkilldexOrigin(out int boxW, out int boxH, out int btnW, out int btnH)
    {
        boxW = _skilldexBox?.Width ?? 185;
        boxH = _skilldexBox?.Height ?? 368;
        btnW = _skilldexBtnOff?.Width ?? 88;
        btnH = _skilldexBtnOff?.Height ?? 33;
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        return new Point(Math.Max(0, vp.Width - boxW - 4), Math.Max(0, vp.Height - _hudBarHeight - boxH - 6));
    }
```

Replace with:

```csharp
    /// <summary>Top-left of the Skilldex box: bottom-right, just above the HUD bar
    /// (skilldex.cc:225-226 — right margin 4, bottom margin 6). btnW/btnH = the SKLDXOFF
    /// button size; row i sits at bar-local (15, 45 + i*36).
    /// Stage 3a (UI Scale): reads the virtual-canvas viewport, since this is drawn inside
    /// DrawSkilldex's scoped, scaled SpriteBatch block. _hudBarHeight is a fixed DEVICE-pixel
    /// constant (the HUD bar's native texture height, unscaled until a later stage) — it must be
    /// converted to virtual-canvas units here, or the scale transform would double-apply to it and
    /// anchor the box at the wrong height above the (still native-sized) bar.</summary>
    private Point SkilldexOrigin(out int boxW, out int boxH, out int btnW, out int btnH)
    {
        boxW = _skilldexBox?.Width ?? 185;
        boxH = _skilldexBox?.Height ?? 368;
        btnW = _skilldexBtnOff?.Width ?? 88;
        btnH = _skilldexBtnOff?.Height ?? 33;
        Rectangle vp = VirtualViewport();
        int hudBarVirtual = (int)(_hudBarHeight / UiScale());
        return new Point(Math.Max(0, vp.Width - boxW - 4), Math.Max(0, vp.Height - hudBarVirtual - boxH - 6));
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Scope `DrawSkilldex`'s art path into its own scaled `SpriteBatch` block**

Find, in the same file (around line 633-680):

```csharp
    private void DrawSkilldex()
    {
        if (!_skilldexOpen || _fontRenderer is null)
            return;

        _skilldexBox ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\SKLDXBOX.frm");
        _skilldexBtnOff ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\SKLDXOFF.frm");
        _skilldexBtnOn ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\SKLDXON.frm");
        if (_skilldexBox is null)
        {
            DrawSkilldexTextFallback();
            return;
        }

        Point o = SkilldexOrigin(out _, out _, out int btnW, out int btnH);
        var titleColor = new Color(252, 252, 84);
        var nameColor = new Color(0, 252, 0);
        var dim = new Color(0, 168, 0);

        _spriteBatch.Draw(_skilldexBox, new Vector2(o.X, o.Y), Color.White);
        _fontRenderer.Draw(_spriteBatch, "SKILLDEX", new Vector2(o.X + 55, o.Y + 14), titleColor);

        MouseState m = Mouse.GetState();
        int hovered = SkilldexRowAt(m.X, m.Y);
        for (int i = 0; i < SkilldexSkills.Length; i++)
        {
            int skill = SkilldexSkills[i];
            var btnPos = new Vector2(o.X + 15, o.Y + 45 + i * 36);
            Texture2D? btn = i == hovered ? _skilldexBtnOn : _skilldexBtnOff;
            if (btn is not null)
                _spriteBatch.Draw(btn, btnPos, Color.White);

            string name = SkillName(skill);
            int nameX = Math.Max(0, (btnW - _fontRenderer.MeasureWidth(name)) / 2);
            int nameY = Math.Max(0, (btnH - _fontRenderer.LineHeight) / 2);
            _fontRenderer.Draw(_spriteBatch, name, new Vector2(btnPos.X + nameX, btnPos.Y + nameY), nameColor);

            // The box bakes placeholder "223 %%" digits in each readout (like iface.frm);
            // field-blank them to the recess colour (32,32,32) and draw the real value
            // right-aligned (skilldex.cc blits BIG_NUMBERS here at x=110).
            _panelPixel ??= CreatePixel();
            int fieldX = o.X + 100, fieldW = 72, fieldY = o.Y + 46 + i * 36;
            _spriteBatch.Draw(_panelPixel, new Rectangle(fieldX, fieldY, fieldW, 26), new Color(32, 32, 32));
            string val = $"{DudeSkillValue(skill)}%";
            _fontRenderer.Draw(_spriteBatch, val,
                new Vector2(fieldX + fieldW - _fontRenderer.MeasureWidth(val) - 4, fieldY + (26 - _fontRenderer.LineHeight) / 2), dim);
        }
    }
```

Replace with:

```csharp
    private void DrawSkilldex()
    {
        if (!_skilldexOpen || _fontRenderer is null)
            return;

        _skilldexBox ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\SKLDXBOX.frm");
        _skilldexBtnOff ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\SKLDXOFF.frm");
        _skilldexBtnOn ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\SKLDXON.frm");
        if (_skilldexBox is null)
        {
            DrawSkilldexTextFallback();
            return;
        }

        // Stage 3a (UI Scale): the art path draws into its own scoped, scaled SpriteBatch block —
        // same technique as Stage 2's DrawDialogPanel — so Skilldex matches fo2ce's fullscreen
        // stretch while DrawSkilldexTextFallback (above) stays unscaled, matching Stage 2's
        // main-menu-family precedent for an art-missing fallback.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        Point o = SkilldexOrigin(out _, out _, out int btnW, out int btnH);
        var titleColor = new Color(252, 252, 84);
        var nameColor = new Color(0, 252, 0);
        var dim = new Color(0, 168, 0);

        _spriteBatch.Draw(_skilldexBox, new Vector2(o.X, o.Y), Color.White);
        _fontRenderer.Draw(_spriteBatch, "SKILLDEX", new Vector2(o.X + 55, o.Y + 14), titleColor);

        Point m = UiMouse();
        int hovered = SkilldexRowAt(m.X, m.Y);
        for (int i = 0; i < SkilldexSkills.Length; i++)
        {
            int skill = SkilldexSkills[i];
            var btnPos = new Vector2(o.X + 15, o.Y + 45 + i * 36);
            Texture2D? btn = i == hovered ? _skilldexBtnOn : _skilldexBtnOff;
            if (btn is not null)
                _spriteBatch.Draw(btn, btnPos, Color.White);

            string name = SkillName(skill);
            int nameX = Math.Max(0, (btnW - _fontRenderer.MeasureWidth(name)) / 2);
            int nameY = Math.Max(0, (btnH - _fontRenderer.LineHeight) / 2);
            _fontRenderer.Draw(_spriteBatch, name, new Vector2(btnPos.X + nameX, btnPos.Y + nameY), nameColor);

            // The box bakes placeholder "223 %%" digits in each readout (like iface.frm);
            // field-blank them to the recess colour (32,32,32) and draw the real value
            // right-aligned (skilldex.cc blits BIG_NUMBERS here at x=110).
            _panelPixel ??= CreatePixel();
            int fieldX = o.X + 100, fieldW = 72, fieldY = o.Y + 46 + i * 36;
            _spriteBatch.Draw(_panelPixel, new Rectangle(fieldX, fieldY, fieldW, 26), new Color(32, 32, 32));
            string val = $"{DudeSkillValue(skill)}%";
            _fontRenderer.Draw(_spriteBatch, val,
                new Vector2(fieldX + fieldW - _fontRenderer.MeasureWidth(val) - 4, fieldY + (26 - _fontRenderer.LineHeight) / 2), dim);
        }

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Route the `Update()` hit-test through `uiMouse`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2208-2210):

```csharp
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                && SkilldexRowAt(mouse.X, mouse.Y) is var row && row >= 0)
                ArmSkill(SkilldexSkills[row]);
```

Replace with:

```csharp
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                && SkilldexRowAt(uiMouse.X, uiMouse.Y) is var row && row >= 0)
                ArmSkill(SkilldexSkills[row]);
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Run the opening golden suite (headless regression net)**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Expected: all scenarios pass, byte-identical to `tests/golden-opening/` — none of this task's
changes are reachable from a headless run (no `Draw()`).

- [ ] **Step 8: Manual visual check — Skilldex at a non-4:3 window**

The Viewer's default window is 1280x720 (16:9), already non-4:3. Run:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage3a-skilldex.png \
  -- --create 5,5,5,5,5,5,5:0,4,5:0 --talk-hex 21101 --choose 1 --hud-click SKILLDEX
```
(`--talk-hex 21101 --choose 1` exits the Klint dialog by picking option 1 — "go back to the
village" — first, so the world is idle before opening Skilldex; adjust if the option numbering
has changed. Check `src/Hexwaste.Viewer/Program.cs` for the exact `--hud-click` token Skilldex
uses if `SKILLDEX` doesn't match — search for how the HUD's Skilldex button is identified.)

Read the resulting PNG. Expected: the Skilldex box renders larger (1.5×) than Stage 1's baseline,
its bottom-right corner sitting just above the HUD bar's own (native-sized, unscaled) top edge —
not overlapping it and not floating with a large gap above it, confirming the `_hudBarHeight`
unit conversion in Step 1 is correct.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Panels.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the Skilldex panel to fill non-4:3 windows

Wires Stage 1/2's UiScale infrastructure into DrawSkilldex's art path:
it now draws inside its own scoped, scaled SpriteBatch block, with
SkilldexOrigin() reading VirtualViewport() instead of the raw device
viewport. _hudBarHeight (the HUD bar's fixed, still-unscaled native
texture height) is converted to virtual-canvas units before use, so
the box continues to sit flush above the bar at any window size.
DrawSkilldexTextFallback (art missing) stays unscaled, matching the
Stage 2 precedent for a degraded fallback path.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

## Task 2: Perk picker

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs:505-574` (`PerkWindowOrigin`, `DrawPerkPicker`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2139-2140` (Update hit-test)

**Interfaces:**
- Consumes: same as Task 1.
- Produces: nothing new consumed by a later task in this plan.

- [ ] **Step 1: Switch `PerkWindowOrigin()` to the virtual viewport**

Find, in `src/Hexwaste.Viewer/ViewerGame.Panels.cs` (around line 503-511):

```csharp
    /// <summary>Top-left of the centred PERKWIN window + the per-row height (the list area divided so
    /// up to ~11 perks fit). One source the render + hit-test share (the SkilldexRowAt pattern).</summary>
    private Point PerkWindowOrigin(out int rowH, out int rowsShown, int eligCount)
    {
        rowH = Math.Max(_fontRenderer!.LineHeight + 1, 11);
        rowsShown = Math.Min(eligCount, PerkWinListH / rowH);
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        return new Point(Math.Max(0, (vp.Width - PerkWinW) / 2), Math.Max(0, (vp.Height - PerkWinH) / 2));
    }
```

Replace with:

```csharp
    /// <summary>Top-left of the centred PERKWIN window + the per-row height (the list area divided so
    /// up to ~11 perks fit). One source the render + hit-test share (the SkilldexRowAt pattern).
    /// Stage 3a (UI Scale): reads the virtual-canvas viewport — this is drawn inside DrawPerkPicker's
    /// scoped, scaled SpriteBatch block.</summary>
    private Point PerkWindowOrigin(out int rowH, out int rowsShown, int eligCount)
    {
        rowH = Math.Max(_fontRenderer!.LineHeight + 1, 11);
        rowsShown = Math.Min(eligCount, PerkWinListH / rowH);
        Rectangle vp = VirtualViewport();
        return new Point(Math.Max(0, (vp.Width - PerkWinW) / 2), Math.Max(0, (vp.Height - PerkWinH) / 2));
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Scope `DrawPerkPicker`'s art path into its own scaled `SpriteBatch` block**

Find, in the same file (around line 533-574):

```csharp
    private void DrawPerkPicker()
    {
        if (!_perkPickOpen || _fontRenderer is null)
            return;
        if (!_perkWinTried) { _perkWinTried = true; _perkWin = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\PERKWIN.frm"); }
        if (_perkWin is null)
        {
            DrawPerkPickerTextFallback();
            return;
        }

        List<int> elig = EligiblePerks();
        Point o = PerkWindowOrigin(out int rowH, out int rowsShown, elig.Count);
        _spriteBatch.Draw(_perkWin, new Vector2(o.X, o.Y), Color.White);

        var green = new Color(0, 252, 0);
        var hi = new Color(252, 252, 84);
        var cardColor = new Color(0, 0, 0); // the card area is parchment — dark text reads on it
        int hovered = PerkPickerRowAt(Mouse.GetState().X, Mouse.GetState().Y);
        for (int i = 0; i < rowsShown; i++)
        {
            int pi = elig[i];
            string rank = _dudePerkRanks[pi] > 0 ? $" ({_dudePerkRanks[pi]}/{Formats.Perks.PerkTable.Get(pi).MaxRank})" : "";
            _fontRenderer.Draw(_spriteBatch, PerkName(pi) + rank,
                new Vector2(o.X + PerkWinListX + 4, o.Y + PerkWinListY + i * rowH), i == hovered ? hi : green);
        }

        // The perk card for the hovered (or first) eligible perk: name + wrapped description.
        int card = hovered >= 0 ? elig[hovered] : (elig.Count > 0 ? elig[0] : -1);
        if (card >= 0)
        {
            _fontRenderer.Draw(_spriteBatch, PerkName(card), new Vector2(o.X + PerkWinCardX, o.Y + 27), cardColor);
            float dy = o.Y + 70;
            foreach (string line in _fontRenderer.WrapText(PerkDescription(card), PerkWinW - PerkWinCardX - 24))
            {
                _fontRenderer.Draw(_spriteBatch, line, new Vector2(o.X + PerkWinCardX, dy), cardColor);
                dy += _fontRenderer.LineHeight;
            }
        }
        if (elig.Count == 0)
            _fontRenderer.Draw(_spriteBatch, "(none qualify)", new Vector2(o.X + PerkWinListX + 4, o.Y + PerkWinListY), green);
    }
```

Replace with:

```csharp
    private void DrawPerkPicker()
    {
        if (!_perkPickOpen || _fontRenderer is null)
            return;
        if (!_perkWinTried) { _perkWinTried = true; _perkWin = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\PERKWIN.frm"); }
        if (_perkWin is null)
        {
            DrawPerkPickerTextFallback();
            return;
        }

        // Stage 3a (UI Scale): the art path draws into its own scoped, scaled SpriteBatch block —
        // same technique as Stage 2's DrawDialogPanel — so the perk picker matches fo2ce's
        // fullscreen stretch while DrawPerkPickerTextFallback (above) stays unscaled, matching
        // Stage 2's main-menu-family precedent for an art-missing fallback.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        List<int> elig = EligiblePerks();
        Point o = PerkWindowOrigin(out int rowH, out int rowsShown, elig.Count);
        _spriteBatch.Draw(_perkWin, new Vector2(o.X, o.Y), Color.White);

        var green = new Color(0, 252, 0);
        var hi = new Color(252, 252, 84);
        var cardColor = new Color(0, 0, 0); // the card area is parchment — dark text reads on it
        Point mp = UiMouse();
        int hovered = PerkPickerRowAt(mp.X, mp.Y);
        for (int i = 0; i < rowsShown; i++)
        {
            int pi = elig[i];
            string rank = _dudePerkRanks[pi] > 0 ? $" ({_dudePerkRanks[pi]}/{Formats.Perks.PerkTable.Get(pi).MaxRank})" : "";
            _fontRenderer.Draw(_spriteBatch, PerkName(pi) + rank,
                new Vector2(o.X + PerkWinListX + 4, o.Y + PerkWinListY + i * rowH), i == hovered ? hi : green);
        }

        // The perk card for the hovered (or first) eligible perk: name + wrapped description.
        int card = hovered >= 0 ? elig[hovered] : (elig.Count > 0 ? elig[0] : -1);
        if (card >= 0)
        {
            _fontRenderer.Draw(_spriteBatch, PerkName(card), new Vector2(o.X + PerkWinCardX, o.Y + 27), cardColor);
            float dy = o.Y + 70;
            foreach (string line in _fontRenderer.WrapText(PerkDescription(card), PerkWinW - PerkWinCardX - 24))
            {
                _fontRenderer.Draw(_spriteBatch, line, new Vector2(o.X + PerkWinCardX, dy), cardColor);
                dy += _fontRenderer.LineHeight;
            }
        }
        if (elig.Count == 0)
            _fontRenderer.Draw(_spriteBatch, "(none qualify)", new Vector2(o.X + PerkWinListX + 4, o.Y + PerkWinListY), green);

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Route the `Update()` hit-test through `uiMouse`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2138-2141):

```csharp
            // P29-M5: click a row in the PERKWIN list to take that perk (additive to 1-9).
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                && PerkPickerRowAt(mouse.X, mouse.Y) is var prow && prow >= 0 && prow < elig.Count)
                ChoosePerk(elig[prow]);
```

Replace with:

```csharp
            // P29-M5: click a row in the PERKWIN list to take that perk (additive to 1-9).
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                && PerkPickerRowAt(uiMouse.X, uiMouse.Y) is var prow && prow >= 0 && prow < elig.Count)
                ChoosePerk(elig[prow]);
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Run the opening golden suite**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Expected: all scenarios pass, unaffected (headless never calls `Draw()`, and `_perkPickOpen` is
never true along the opening spine).

- [ ] **Step 8: Manual visual check — perk picker at a non-4:3 window**

Reaching the perk picker requires a character old enough to have a pending perk pick — not
reachable from a fresh character in one CLI invocation. If a `--force-head`-style debug flag or
similar exists to force `_perkPickOpen = true` directly, check `src/Hexwaste.Viewer/Program.cs`
for it; otherwise, this manual check may need `--advance-ms`-driven leveling or can be skipped
with a clear note in the task report (the build + golden suite are the hard requirements — do not
spend more than ~10 minutes searching for a CLI path before reporting the limitation).

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Panels.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the perk-picker panel to fill non-4:3 windows

Wires Stage 1/2's UiScale infrastructure into DrawPerkPicker's art
path: it now draws inside its own scoped, scaled SpriteBatch block,
with PerkWindowOrigin() reading VirtualViewport() instead of the raw
device viewport. DrawPerkPickerTextFallback (art missing) stays
unscaled, matching the Stage 2 precedent for a degraded fallback path.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

## Task 3: Save/Load

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs:1291-1382`-ish (`SaveLoadPanelRect`,
  `SaveLoadSlotRect`, `DrawSaveLoad`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2316-2317` (Update hit-test)

**Interfaces:**
- Consumes: same as Task 1/2.
- Produces: nothing new consumed by a later task.

- [ ] **Step 1: Switch `SaveLoadPanelRect()` to the virtual viewport**

Find, in `src/Hexwaste.Viewer/ViewerGame.Panels.cs` (around line 1291-1304):

```csharp
    private Rectangle SaveLoadPanelRect()
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        if (_saveLoadArt)
        {
            const int w = 640, h = 480; // LSGAME.frm
            return new Rectangle(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2), w, h);
        }
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        int th = (Formats.SaveSlots.Count + 2) * lh + 16;
        int x = Math.Max(0, (vp.Width - SaveLoadPanelWidth) / 2);
        int y = Math.Max(0, (vp.Height - th) / 2);
        return new Rectangle(x, y, SaveLoadPanelWidth, th);
    }
```

Replace with:

```csharp
    /// <summary>Stage 3a (UI Scale): reads the virtual-canvas viewport in BOTH the art and
    /// text-only branches — unlike Skilldex/perk-picker, save/load's fallback is not a separate
    /// method with independent, disconnected positioning; it shares this exact function with the
    /// art path, so there is no seam to leave one branch unscaled. Both branches are drawn inside
    /// DrawSaveLoad's scoped, scaled SpriteBatch block.</summary>
    private Rectangle SaveLoadPanelRect()
    {
        Rectangle vp = VirtualViewport();
        if (_saveLoadArt)
        {
            const int w = 640, h = 480; // LSGAME.frm
            return new Rectangle(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2), w, h);
        }
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        int th = (Formats.SaveSlots.Count + 2) * lh + 16;
        int x = Math.Max(0, (vp.Width - SaveLoadPanelWidth) / 2);
        int y = Math.Max(0, (vp.Height - th) / 2);
        return new Rectangle(x, y, SaveLoadPanelWidth, th);
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Scope `DrawSaveLoad` into its own scaled `SpriteBatch` block**

Find, in the same file (around line 1323-1382 — read the file to confirm the exact end of the
method, since it continues past what's shown here with the rest of the per-slot render loop):

```csharp
    private void DrawSaveLoad()
    {
        if (!_saveLoadOpen || _fontRenderer is null)
            return;
        // P52-M3: render the authentic LSGAME.frm window when present; the slot-list frame + info box
        // are baked into the art (loadsave.cc). Fall back to the dark text panel when the asset is absent.
        _lsgameFrm ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\LSGAME.frm");
        _saveLoadArt = _lsgameFrm is not null;
        _panelPixel ??= CreatePixel();
        Rectangle p = SaveLoadPanelRect();
```

Replace the method's opening with:

```csharp
    private void DrawSaveLoad()
    {
        if (!_saveLoadOpen || _fontRenderer is null)
            return;
        // P52-M3: render the authentic LSGAME.frm window when present; the slot-list frame + info box
        // are baked into the art (loadsave.cc). Fall back to the dark text panel when the asset is absent.
        _lsgameFrm ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\LSGAME.frm");
        _saveLoadArt = _lsgameFrm is not null;
        _panelPixel ??= CreatePixel();

        // Stage 3a (UI Scale): both the art and text-only rendering below scale together (see
        // SaveLoadPanelRect's doc comment) inside their own scoped SpriteBatch block, the same
        // technique as Stage 2's DrawDialogPanel.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        Rectangle p = SaveLoadPanelRect();
```

Then find every remaining `Mouse.GetState()` read inside this same method (there is exactly one,
used for hover):

```csharp
        int hovered = SaveLoadSlotAt(Mouse.GetState().X, Mouse.GetState().Y);
```

Replace with:

```csharp
        Point m = UiMouse();
        int hovered = SaveLoadSlotAt(m.X, m.Y);
```

Finally, find the end of the method — the last statement `DrawSaveLoad` executes (read the method
in full first to locate it exactly; it is the per-slot render loop's closing brace) — and add,
immediately after that closing brace but still inside `DrawSaveLoad`'s own body:

```csharp
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

(i.e. the method's final closing `}` moves down two lines to follow this resume-batch pair,
exactly mirroring how Task 1/2 closed `DrawSkilldex`/`DrawPerkPicker`.)

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Route the `Update()` hit-test through `uiMouse`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2314-2317):

```csharp
        if (_saveLoadOpen)
        {
            int slrow = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                ? SaveLoadSlotAt(mouse.X, mouse.Y) : -1;
```

Replace with:

```csharp
        if (_saveLoadOpen)
        {
            int slrow = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                ? SaveLoadSlotAt(uiMouse.X, uiMouse.Y) : -1;
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Run the opening golden suite**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Expected: all scenarios pass, unaffected.

- [ ] **Step 8: Manual visual check — save/load at a non-4:3 window**

Run:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage3a-saveload.png \
  -- --menu --menu-click title 2
```
(check `src/Hexwaste.Viewer/Program.cs`'s `--menu-click` handling for the exact row-index
convention if `2` doesn't land on LOAD GAME — the six main-menu buttons are INTRO/NEW GAME/LOAD
GAME/OPTIONS/CREDITS/EXIT in that order per `MainMenuButtons`, `ViewerGame.Shell.cs:29-37`, so
LOAD GAME is index 2 if 0-based.)

Read the resulting PNG. Expected: the save/load panel (art or text, whichever `LSGAME.frm`'s
presence resolves to) renders at 1.5× Stage 1's baseline, centered, undistorted, with slot rows
still legible.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Panels.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the save/load panel to fill non-4:3 windows

Wires Stage 1/2's UiScale infrastructure into DrawSaveLoad: it now
draws inside its own scoped, scaled SpriteBatch block, with
SaveLoadPanelRect() reading VirtualViewport() instead of the raw
device viewport. Unlike Skilldex/perk-picker, both the LSGAME.frm art
path and the text-only fallback scale together here, since they share
one function's viewport-relative math rather than being two
independent, disconnected layouts.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```
