# UI Scale — Stage 3b: Character Sheet, Pip-Boy, Options, Automap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the character sheet, Pip-Boy, options, and automap screens render at the same
uniform scale as every screen Stage 2/3a already migrated, filling non-4:3 windows like
`fallout2-ce`'s stretch, while every other screen keeps its native size.

**Architecture:** Character sheet gets the Skilldex/perk-picker treatment (scope the scaled batch
around the art path only, since it has a genuine separate fallback method). Pip-Boy, options, and
automap get the save/load treatment (scope the scaled batch around the whole method, since their
fallback is a same-function boolean gate sharing the same viewport-relative formula as the art
path — no separable branch to carve out). Character sheet additionally requires converting THREE
independent, textually-identical copies of the same origin formula in lockstep; options and
automap each have TWO.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`SpriteBatch.Begin`/`End`, already-shipped
`UiScale`/`VirtualViewport`/`UiMouse`/`UiScaleMatrix` from Stage 1/2/3a).

## Global Constraints

- Scale model: `scale = min(viewportWidth/640, viewportHeight/480)` (Stage 1, shipped,
  unit-tested — do not touch).
- Only the character sheet, Pip-Boy, options, and automap scale in this plan. No other screen
  (inventory, Skilldex, perk picker, save/load, preferences, aim dialog, tactics, HUD bar, action
  menu, elevator picker, worldmap chrome, Credits, Endgame, death screen, main-menu family,
  dialog) changes in this stage.
- `DrawSkillAllocatorFallback()` (character sheet's art-missing fallback, drawn at a fixed,
  non-viewport-relative position) stays completely untouched and unscaled — matching the Stage
  2/3a precedent for a degraded fallback path.
- Pip-Boy, options, and automap have NO separate fallback method to carve out — a single boolean
  (`_pipboyBg is not null` / `_optionsBg is not null` / `_automapBg is not null`) gates both
  branches inside the SAME function, and both branches already share the same viewport-relative
  formula (exactly like Stage 3a's save/load). Their entire `Draw*` method scales, not just an
  "art path" subset.
- Every position read from a real device (mouse) or used to lay out scaled content must come
  from `UiMouse()`/`VirtualViewport()` inside a scaled block — never a raw
  `GraphicsDevice.Viewport`/`Mouse.GetState().X/Y` mixed into scaled content.
- `ViewerGame.Harness.cs`'s two callers of `PipboyRowRect`/`OptionsRowRect` (the scripted
  `MenuClick` CLI test action, lines ~1907/~1897) need NO code change — the harness runs against
  the same live, real `GraphicsDevice.Viewport` (1280×720, the Viewer's actual default back
  buffer) that `Draw()` uses, not a headless stub, so converting the shared helper itself keeps
  both call paths automatically consistent.
- Character sheet has a pre-existing (not introduced by this stage) same-frame overlap with the
  already-scaled perk picker: pressing `G` while the sheet is open sets `_perkPickOpen = true`
  without clearing `_skillAllocOpen`, so both screens can render in the same frame. This stage's
  character-sheet migration incidentally makes both screens scale identically when that happens
  — no state-machine change is in scope, just be aware this is expected, not a new bug to
  chase down.

## File Structure

- `src/Hexwaste.Viewer/ViewerGame.Panels.cs` — `DrawSkillAllocator`, `CharSheetItemAt`,
  `PipboyContentOrigin`, `DrawPipboy`, `OptionsRowRect`, `DrawOptions`, `AutomapButtons`,
  `DrawAutomap`.
- `src/Hexwaste.Viewer/ViewerGame.cs` — four `Update()` blocks (character sheet, Pip-Boy,
  options, automap), all switched to read the already-existing `uiMouse` local (declared by
  Stage 2 Task 1, reused here — no new declaration).

## Task 1: Character sheet

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs:58-237` (`DrawSkillAllocator`),
  `:337-342` (`CharSheetItemAt`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2169`, `:2176-2177` (Update hit-tests)

**Interfaces:**
- Consumes: `UiScaleMatrix()`, `VirtualViewport()`, `UiMouse()` (Stage 1/2, already shipped),
  the `Point uiMouse` local already declared in `Update()` (Stage 2 Task 1).
- Produces: nothing new consumed by a later task in this plan.

- [ ] **Step 1: Switch `DrawSkillAllocator`'s origin to the virtual viewport and scope its art path**

Find, in `src/Hexwaste.Viewer/ViewerGame.Panels.cs` (around line 58-81):

```csharp
        if (!_skillAllocOpen || _fontRenderer is null || _dudeGcd is null)
            return;

        if (!_charBgTried)
        {
            _charBgTried = true;
            // EDTREDT.FRM (interface FID 177 = the in-game character-editor backdrop) + BIGNUM.FRM
            // (FID 170, the SPECIAL big-digit strip). Loaded into dedicated fields — NOT the LRU
            // FrmCache, which evicts + disposes its textures during play — the PERKWIN/OPBASE
            // text-then-art pattern. ported from fallout2-ce src/character_editor.cc:1282/307.
            _charBg = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\EDTREDT.FRM");
            _bigNum = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\BIGNUM.FRM");
        }
        if (_charBg is null) { DrawSkillAllocatorFallback(); return; }

        var green = new Color(0, 252, 0);
        var gold = new Color(252, 252, 84);
        var tan = new Color(180, 156, 96);

        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int ox = (vp.Width - 640) / 2, oy = (vp.Height - 480) / 2;
        _spriteBatch.Draw(_charBg, new Rectangle(ox, oy, 640, 480), Color.White);
```

Replace with:

```csharp
        if (!_skillAllocOpen || _fontRenderer is null || _dudeGcd is null)
            return;

        if (!_charBgTried)
        {
            _charBgTried = true;
            // EDTREDT.FRM (interface FID 177 = the in-game character-editor backdrop) + BIGNUM.FRM
            // (FID 170, the SPECIAL big-digit strip). Loaded into dedicated fields — NOT the LRU
            // FrmCache, which evicts + disposes its textures during play — the PERKWIN/OPBASE
            // text-then-art pattern. ported from fallout2-ce src/character_editor.cc:1282/307.
            _charBg = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\EDTREDT.FRM");
            _bigNum = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\BIGNUM.FRM");
        }
        if (_charBg is null) { DrawSkillAllocatorFallback(); return; }

        // Stage 3b (UI Scale): the art path draws into its own scoped, scaled SpriteBatch block —
        // same technique as Stage 3a's DrawSkilldex/DrawPerkPicker — so the character sheet
        // matches fo2ce's fullscreen stretch while DrawSkillAllocatorFallback (above) stays
        // unscaled, matching the established fallback precedent.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        var green = new Color(0, 252, 0);
        var gold = new Color(252, 252, 84);
        var tan = new Color(180, 156, 96);

        Rectangle vp = VirtualViewport();
        int ox = (vp.Width - 640) / 2, oy = (vp.Height - 480) / 2;
        _spriteBatch.Draw(_charBg, new Rectangle(ox, oy, 640, 480), Color.White);
```

Then find the method's closing (around line 234-237):

```csharp
        T(383, 455, EditorMsg(103), tan);   // Print To File (inert)
        T(492, 455, EditorMsg(100), gold);  // Done
        T(585, 455, EditorMsg(102), gold);  // Cancel
    }
```

Replace with:

```csharp
        T(383, 455, EditorMsg(103), tan);   // Print To File (inert)
        T(492, 455, EditorMsg(100), gold);  // Done
        T(585, 455, EditorMsg(102), gold);  // Cancel

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Switch `CharSheetItemAt`'s origin to the virtual viewport**

Find, in the same file (around line 337-343):

```csharp
    private int CharSheetItemAt(int mx, int my)
    {
        if (_charBg is null || _fontRenderer is null)
            return -1;
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int ox = (vp.Width - 640) / 2, oy = (vp.Height - 480) / 2;
        int lx = mx - ox, ly = my - oy;
```

Replace with:

```csharp
    private int CharSheetItemAt(int mx, int my)
    {
        if (_charBg is null || _fontRenderer is null)
            return -1;
        // Stage 3b (UI Scale): this hit-test must agree with DrawSkillAllocator's scaled art
        // path — both read the same virtual-canvas origin, and callers pass the already
        // UiMouse()-transformed point (see Update()'s uiMouse usage below).
        Rectangle vp = VirtualViewport();
        int ox = (vp.Width - 640) / 2, oy = (vp.Height - 480) / 2;
        int lx = mx - ox, ly = my - oy;
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Route the `Update()` hit-tests (item selection AND DONE/CANCEL) through `uiMouse`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2165-2180):

```csharp
            // P82-M2: click a stat/skill/derived/condition info area -> select it (the description
            // card updates); clicking a skill also arms it for an Enter-raise.
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
            {
                if (CharSheetItemAt(mouse.X, mouse.Y) is var sel && sel >= 0)
                {
                    _charSelId = sel;
                    if (sel is >= 61 and < 79)
                        _skillAllocIndex = sel - 61;
                }
                // P82-M4: the DONE / CANCEL buttons (the baked red buttons, y~454) close the sheet.
                Rectangle cvp = GraphicsDevice.Viewport.Bounds;
                int cbx = mouse.X - (cvp.Width - 640) / 2, cby = mouse.Y - (cvp.Height - 480) / 2;
                if (cby is >= 448 and < 476 && cbx is >= 462 and < 640)
                    _skillAllocOpen = false;
            }
```

Replace with:

```csharp
            // P82-M2: click a stat/skill/derived/condition info area -> select it (the description
            // card updates); clicking a skill also arms it for an Enter-raise.
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
            {
                if (CharSheetItemAt(uiMouse.X, uiMouse.Y) is var sel && sel >= 0)
                {
                    _charSelId = sel;
                    if (sel is >= 61 and < 79)
                        _skillAllocIndex = sel - 61;
                }
                // P82-M4: the DONE / CANCEL buttons (the baked red buttons, y~454) close the sheet.
                // Stage 3b (UI Scale): matches CharSheetItemAt's virtual-canvas origin above.
                Rectangle cvp = VirtualViewport();
                int cbx = uiMouse.X - (cvp.Width - 640) / 2, cby = uiMouse.Y - (cvp.Height - 480) / 2;
                if (cby is >= 448 and < 476 && cbx is >= 462 and < 640)
                    _skillAllocOpen = false;
            }
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Run the opening golden suite**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Expected: all scenarios pass, unaffected (headless never calls `Draw()`, and `_skillAllocOpen`
is never true along the opening spine).

- [ ] **Step 8: Manual visual check — character sheet at a non-4:3 window**

Run:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage3b-charsheet.png \
  -- --hud-click CHA
```
(check `src/Hexwaste.Viewer/Program.cs`'s `--hud-click` token list if `CHA` doesn't open the
character sheet — the HUD button that opens it is the same one the `K`/`C` keyboard shortcuts
map to; search for how `_skillAllocOpen` gets set to find the exact token, or use `--menu-click`
if the character sheet has an equivalent forced-open path.)

Read the resulting PNG. Expected: the character sheet renders at 1.5× Stage-1-baseline size,
centered, undistorted. Then, still using the CLI or a live run, click a visible stat or skill
row and confirm `_charSelId`/the description card updates to that SPECIFIC row (not an adjacent
one) — this is the check that proves all three origin copies (Steps 1, 3, 5) stayed in lockstep.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Panels.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the character sheet to fill non-4:3 windows

Wires Stage 1/2/3a's UiScale infrastructure into DrawSkillAllocator's
art path: it now draws inside its own scoped, scaled SpriteBatch
block. Converts all three independent, previously-identical copies of
this screen's origin formula in lockstep -- DrawSkillAllocator itself,
CharSheetItemAt (the hit-test), and Update()'s DONE/CANCEL button
check -- to VirtualViewport(), so clicks keep landing on the row the
cursor visually sits over. DrawSkillAllocatorFallback (art missing)
stays untouched and unscaled.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

## Task 2: Pip-Boy

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs:744-750` (`PipboyContentOrigin`),
  `:819-892` (`DrawPipboy`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2227`, `:2231` (Update hit-tests)

**Interfaces:**
- Consumes: same as Task 1.
- Produces: nothing new consumed by a later task.

- [ ] **Step 1: Switch `PipboyContentOrigin()` to the virtual viewport**

Find, in `src/Hexwaste.Viewer/ViewerGame.Panels.cs` (around line 742-750):

```csharp
    // Pip-Boy content origin + line height — shared by DrawPipboy (render) and the
    // PipboyRow* helpers (hit-test) so a row click always lands where it's drawn.
    private void PipboyContentOrigin(out Point po, out int lh)
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int pw = _pipboyBg?.Width ?? 640, ph = _pipboyBg?.Height ?? 480;
        po = new Point(Math.Max(0, (vp.Width - pw) / 2), Math.Max(0, (vp.Height - ph) / 2));
        lh = (_fontRenderer?.LineHeight ?? 16) + 4;
    }
```

Replace with:

```csharp
    // Pip-Boy content origin + line height — shared by DrawPipboy (render) and the
    // PipboyRow* helpers (hit-test) so a row click always lands where it's drawn.
    // Stage 3b (UI Scale): reads the virtual-canvas viewport — DrawPipboy draws inside its own
    // scoped, scaled SpriteBatch block, and ViewerGame.Harness.cs's scripted MenuClick action
    // (a CLI test path) calls PipboyRowRect (which calls this) against the same real,
    // GraphicsDevice-backed viewport Draw uses, so both stay consistent automatically.
    private void PipboyContentOrigin(out Point po, out int lh)
    {
        Rectangle vp = VirtualViewport();
        int pw = _pipboyBg?.Width ?? 640, ph = _pipboyBg?.Height ?? 480;
        po = new Point(Math.Max(0, (vp.Width - pw) / 2), Math.Max(0, (vp.Height - ph) / 2));
        lh = (_fontRenderer?.LineHeight ?? 16) + 4;
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Scope `DrawPipboy` into its own scaled `SpriteBatch` block**

Find, in the same file (around line 819-825):

```csharp
    private void DrawPipboy()
    {
        if (!_pipboyOpen || _fontRenderer is null)
            return;
        _pipboyBg ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\PIP.frm");

        PipboyContentOrigin(out Point po, out int lh);
```

Replace with:

```csharp
    private void DrawPipboy()
    {
        if (!_pipboyOpen || _fontRenderer is null)
            return;
        _pipboyBg ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\PIP.frm");

        // Stage 3b (UI Scale): unlike Skilldex/perk-picker/character-sheet, Pip-Boy has no
        // separate fallback method — the art-present and art-absent branches below share this
        // one function's viewport-relative math (like Stage 3a's save/load), so the whole method
        // scales inside one scoped, scaled SpriteBatch block.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        PipboyContentOrigin(out Point po, out int lh);
```

Then find, later in the same method (around line 882-883):

```csharp
        // The clickable action rows (click or the keyboard shortcut). The hovered row lights.
        int hovered = PipboyRowAt(Mouse.GetState().X, Mouse.GetState().Y);
```

Replace with:

```csharp
        // The clickable action rows (click or the keyboard shortcut). The hovered row lights.
        Point pm = UiMouse();
        int hovered = PipboyRowAt(pm.X, pm.Y);
```

Finally find the method's closing (around line 890-892):

```csharp
        _fontRenderer.Draw(_spriteBatch, _pipboyRestMenu ? "click a duration, Esc back" : "click a row, P / Esc close",
            new Vector2(cx, po.Y + ph - 30), dim);
    }
```

Replace with:

```csharp
        _fontRenderer.Draw(_spriteBatch, _pipboyRestMenu ? "click a duration, Esc back" : "click a row, P / Esc close",
            new Vector2(cx, po.Y + ph - 30), dim);

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Route the `Update()` hit-tests through `uiMouse`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2226-2231):

```csharp
            bool pipPress = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            if (pipPress && PipboyTabAt(mouse.X, mouse.Y) is { } tabAction)
            {
                tabAction();
            }
            else if (pipPress && PipboyRowAt(mouse.X, mouse.Y) is var prow && prow >= 0)
            {
                PipboyRows()[prow].OnClick();
            }
```

Replace with:

```csharp
            bool pipPress = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            if (pipPress && PipboyTabAt(uiMouse.X, uiMouse.Y) is { } tabAction)
            {
                tabAction();
            }
            else if (pipPress && PipboyRowAt(uiMouse.X, uiMouse.Y) is var prow && prow >= 0)
            {
                PipboyRows()[prow].OnClick();
            }
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Run the opening golden suite**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Expected: all scenarios pass, unaffected.

- [ ] **Step 8: Manual visual check — Pip-Boy at a non-4:3 window, plus the harness-call sanity check**

Run:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage3b-pipboy.png \
  -- --hud-click PIP
```
(check `Program.cs` for the exact `--hud-click` token if `PIP` doesn't match).

Read the resulting PNG. Expected: PIP.frm renders at 1.5× baseline, centered, undistorted, with
STATUS text and the clickable rows all legible.

Then confirm the `ViewerGame.Harness.cs` `MenuClick("pipboy", row)` CLI path still resolves to
the visually-correct row post-conversion — run whatever startup action exercises it (check
`Program.cs` for the flag that maps to `StartupAction.MenuClick`) and confirm the printed
row/action matches what a human click at that same screen position would trigger.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Panels.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the Pip-Boy panel to fill non-4:3 windows

Wires Stage 1/2/3a's UiScale infrastructure into DrawPipboy: unlike
the art/fallback-split screens (Skilldex, perk picker, character
sheet), Pip-Boy's art-present and art-absent branches share one
function's viewport-relative math (like save/load), so the whole
method now draws inside one scoped, scaled SpriteBatch block.
PipboyContentOrigin() reads VirtualViewport() instead of the raw
device viewport -- both DrawPipboy and ViewerGame.Harness.cs's
scripted MenuClick CLI action share this one helper, so converting it
keeps the real render and the scripted-test click automatically
consistent.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

## Task 3: Options

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs:1156-1164` (`OptionsRowRect`),
  `:1201-1230` (`DrawOptions`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2356-2357` (Update hit-test)

**Interfaces:**
- Consumes: same as Task 1/2.
- Produces: nothing new consumed by a later task.

- [ ] **Step 1: Switch `OptionsRowRect()` to the virtual viewport**

Find, in `src/Hexwaste.Viewer/ViewerGame.Panels.cs` (around line 1154-1164):

```csharp
    // The clickable rect for the index-th options row — origin + spacing mirror DrawOptions
    // exactly (the FRM-dim fallback keeps it valid before the art loads).
    private Rectangle OptionsRowRect(int index)
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int ow = _optionsBg?.Width ?? 164, oh = _optionsBg?.Height ?? 217;
        int ox = Math.Max(0, (vp.Width - ow) / 2), oy = Math.Max(0, (vp.Height - oh) / 2);
        int lh = (_fontRenderer?.LineHeight ?? 16) + 10;
        int ty0 = oy + (oh - OptionsItems.Length * lh) / 2;
        return new Rectangle(ox, ty0 + index * lh - 2, ow, lh);
    }
```

Replace with:

```csharp
    // The clickable rect for the index-th options row — origin + spacing mirror DrawOptions
    // exactly (the FRM-dim fallback keeps it valid before the art loads).
    // Stage 3b (UI Scale): reads the virtual-canvas viewport — DrawOptions draws inside its own
    // scoped, scaled SpriteBatch block, and this must stay in lockstep with DrawOptions'
    // OWN, separate inline origin copy (see that method). ViewerGame.Harness.cs's scripted
    // MenuClick action calls this same helper against the same real, live viewport Draw uses.
    private Rectangle OptionsRowRect(int index)
    {
        Rectangle vp = VirtualViewport();
        int ow = _optionsBg?.Width ?? 164, oh = _optionsBg?.Height ?? 217;
        int ox = Math.Max(0, (vp.Width - ow) / 2), oy = Math.Max(0, (vp.Height - oh) / 2);
        int lh = (_fontRenderer?.LineHeight ?? 16) + 10;
        int ty0 = oy + (oh - OptionsItems.Length * lh) / 2;
        return new Rectangle(ox, ty0 + index * lh - 2, ow, lh);
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Scope `DrawOptions` into its own scaled `SpriteBatch` block, converting its second origin copy too**

Find, in the same file (around line 1201-1223):

```csharp
    private void DrawOptions()
    {
        if (!_optionsOpen || _fontRenderer is null)
            return;
        _optionsBg ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\OPBASE.frm");

        int ow = _optionsBg?.Width ?? 164, oh = _optionsBg?.Height ?? 217;
        var green = new Color(0, 252, 0);
        var hot = new Color(252, 252, 84);

        // Top-left of the panel (recompute the same way OptionsRowRect does).
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int px = Math.Max(0, (vp.Width - ow) / 2), py = Math.Max(0, (vp.Height - oh) / 2);

        if (_optionsBg is not null)
            _spriteBatch.Draw(_optionsBg, new Vector2(px, py), Color.White);
        else
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel, new Rectangle(px, py, ow, oh), new Color(8, 16, 8, 240));
        }

        int hovered = OptionsRowAt(Mouse.GetState().X, Mouse.GetState().Y);
```

Replace with:

```csharp
    private void DrawOptions()
    {
        if (!_optionsOpen || _fontRenderer is null)
            return;
        _optionsBg ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\OPBASE.frm");

        // Stage 3b (UI Scale): like Pip-Boy, Options has no separate fallback method — the
        // art-present and art-absent branches below share this function's viewport-relative
        // math, so the whole method scales inside one scoped, scaled SpriteBatch block.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        int ow = _optionsBg?.Width ?? 164, oh = _optionsBg?.Height ?? 217;
        var green = new Color(0, 252, 0);
        var hot = new Color(252, 252, 84);

        // Top-left of the panel (recompute the same way OptionsRowRect does).
        Rectangle vp = VirtualViewport();
        int px = Math.Max(0, (vp.Width - ow) / 2), py = Math.Max(0, (vp.Height - oh) / 2);

        if (_optionsBg is not null)
            _spriteBatch.Draw(_optionsBg, new Vector2(px, py), Color.White);
        else
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel, new Rectangle(px, py, ow, oh), new Color(8, 16, 8, 240));
        }

        Point om = UiMouse();
        int hovered = OptionsRowAt(om.X, om.Y);
```

Then find the method's closing (around line 1224-1230):

```csharp
        for (int i = 0; i < OptionsItems.Length; i++)
        {
            Rectangle r = OptionsRowRect(i);
            int tw = _fontRenderer.MeasureWidth(OptionsItems[i]);
            _fontRenderer.Draw(_spriteBatch, OptionsItems[i], new Vector2(px + (ow - tw) / 2, r.Y + 2), i == hovered ? hot : green);
        }
    }
```

Replace with:

```csharp
        for (int i = 0; i < OptionsItems.Length; i++)
        {
            Rectangle r = OptionsRowRect(i);
            int tw = _fontRenderer.MeasureWidth(OptionsItems[i]);
            _fontRenderer.Draw(_spriteBatch, OptionsItems[i], new Vector2(px + (ow - tw) / 2, r.Y + 2), i == hovered ? hot : green);
        }

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Route the `Update()` hit-test through `uiMouse`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2356-2357):

```csharp
            int orow = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                ? OptionsRowAt(mouse.X, mouse.Y) : -1;
```

Replace with:

```csharp
            int orow = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                ? OptionsRowAt(uiMouse.X, uiMouse.Y) : -1;
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Run the opening golden suite**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Expected: all scenarios pass, unaffected.

- [ ] **Step 8: Manual visual check — options at a non-4:3 window, plus the harness-call sanity check**

Run:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage3b-options.png \
  -- --hud-click OPT
```
Read the resulting PNG. Expected: OPBASE.frm (or its dark-panel fallback) renders at 1.5×
baseline, centered, undistorted, all six rows legible. Then confirm
`ViewerGame.Harness.cs`'s `MenuClick("options", row)` path still resolves to the visually-correct
row post-conversion (same check as Task 2's Pip-Boy harness confirmation).

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Panels.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the options panel to fill non-4:3 windows

Wires Stage 1/2/3a's UiScale infrastructure into DrawOptions: like
Pip-Boy, its art-present and art-absent branches share one function's
viewport-relative math, so the whole method now draws inside one
scoped, scaled SpriteBatch block. Converts BOTH of this screen's
independent origin copies in lockstep -- OptionsRowRect() and
DrawOptions' own separate inline recomputation of the same formula --
to VirtualViewport(), so clicks keep landing on the row the cursor
visually sits over. ViewerGame.Harness.cs's scripted MenuClick action
shares OptionsRowRect with the real render, so it stays consistent
automatically.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

## Task 4: Automap

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs:1069-1077` (`AutomapButtons`),
  `:1079-1144` (`DrawAutomap`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2267-2281` (Update hit-tests)

**Interfaces:**
- Consumes: same as Task 1/2/3.
- Produces: nothing new consumed by a later task.

- [ ] **Step 1: Switch `AutomapButtons()` to the virtual viewport**

Find, in `src/Hexwaste.Viewer/ViewerGame.Panels.cs` (around line 1067-1077):

```csharp
    // The AUTOMAP.frm baked-in button screen rects (automap.cc): the SCANNER (111,454), CANCEL (277,454)
    // and the hi/lo-detail SWITCH (457,340) — shared by DrawAutomap (label hint) + the input hit-test.
    private (Rectangle Scanner, Rectangle Cancel, Rectangle Detail) AutomapButtons()
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int w = _automapBg?.Width ?? 519, h = _automapBg?.Height ?? 480;
        var o = new Point(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2));
        return (new Rectangle(o.X + 105, o.Y + 450, 24, 22),
                new Rectangle(o.X + 271, o.Y + 450, 24, 22),
                new Rectangle(o.X + 457, o.Y + 340, 42, 74));
    }
```

Replace with:

```csharp
    // The AUTOMAP.frm baked-in button screen rects (automap.cc): the SCANNER (111,454), CANCEL (277,454)
    // and the hi/lo-detail SWITCH (457,340) — shared by DrawAutomap (label hint) + the input hit-test.
    // Stage 3b (UI Scale): reads the virtual-canvas viewport — this must stay in lockstep with
    // DrawAutomap's OWN, separate inline copy of the identical formula (see that method); neither
    // delegates to the other, so both were converted together in this task.
    private (Rectangle Scanner, Rectangle Cancel, Rectangle Detail) AutomapButtons()
    {
        Rectangle vp = VirtualViewport();
        int w = _automapBg?.Width ?? 519, h = _automapBg?.Height ?? 480;
        var o = new Point(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2));
        return (new Rectangle(o.X + 105, o.Y + 450, 24, 22),
                new Rectangle(o.X + 271, o.Y + 450, 24, 22),
                new Rectangle(o.X + 457, o.Y + 340, 42, 74));
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Scope `DrawAutomap` into its own scaled `SpriteBatch` block, converting its own origin copy too**

Find, in the same file (around line 1079-1093):

```csharp
    private void DrawAutomap()
    {
        if (!_automapOpen || _fontRenderer is null)
            return;
        _automapBg ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\AUTOMAP.frm");

        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int w = _automapBg?.Width ?? 519, h = _automapBg?.Height ?? 480;
        var o = new Point(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2));
        _panelPixel ??= CreatePixel();
        if (_automapBg is not null)
            _spriteBatch.Draw(_automapBg, new Vector2(o.X, o.Y), Color.White);
        else
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X, o.Y, w, h), new Color(8, 16, 8, 240));
```

Replace with:

```csharp
    private void DrawAutomap()
    {
        if (!_automapOpen || _fontRenderer is null)
            return;
        _automapBg ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\AUTOMAP.frm");

        // Stage 3b (UI Scale): like Pip-Boy/Options, Automap has no separate fallback method — the
        // art-present and art-absent branches share this function's viewport-relative math, so the
        // whole method scales inside one scoped, scaled SpriteBatch block.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        Rectangle vp = VirtualViewport();
        int w = _automapBg?.Width ?? 519, h = _automapBg?.Height ?? 480;
        var o = new Point(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2));
        _panelPixel ??= CreatePixel();
        if (_automapBg is not null)
            _spriteBatch.Draw(_automapBg, new Vector2(o.X, o.Y), Color.White);
        else
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X, o.Y, w, h), new Color(8, 16, 8, 240));
```

Then find the method's closing (around line 1139-1144):

```csharp
        var labelGreen = new Color(0, 252, 0);
        _fontRenderer.Draw(_spriteBatch, $"AUTOMAP - {_currentMapName} (elev {_elevation}, {(_automapHighDetail ? "hi" : "lo")} detail{(_automapScanner ? ", scanner" : "")})",
            new Vector2(o.X + 20, o.Y + 12), labelGreen);
        _fontRenderer.Draw(_spriteBatch, "SCANNER / CANCEL / hi-lo switch - or Esc/A close, H/L detail, PgUp/Dn elev",
            new Vector2(o.X + 20, o.Y + h - 24), new Color(0, 168, 0));
    }
```

Replace with:

```csharp
        var labelGreen = new Color(0, 252, 0);
        _fontRenderer.Draw(_spriteBatch, $"AUTOMAP - {_currentMapName} (elev {_elevation}, {(_automapHighDetail ? "hi" : "lo")} detail{(_automapScanner ? ", scanner" : "")})",
            new Vector2(o.X + 20, o.Y + 12), labelGreen);
        _fontRenderer.Draw(_spriteBatch, "SCANNER / CANCEL / hi-lo switch - or Esc/A close, H/L detail, PgUp/Dn elev",
            new Vector2(o.X + 20, o.Y + h - 24), new Color(0, 168, 0));

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

(Note: `DrawAutomap` has no in-Draw `Mouse.GetState()` hover read to convert — confirmed during
planning; the SCANNER/CANCEL/switch hint text is static, not hover-colored.)

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Route the `Update()` hit-tests through `uiMouse`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2267-2281):

```csharp
            (Rectangle scanner, Rectangle cancel, Rectangle detail) = AutomapButtons();
            bool apress = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.A)
                || (apress && cancel.Contains(mouse.X, mouse.Y)))
            {
                _automapOpen = false;
                _automapScanner = false; // the scanner view lasts one automap session (automapShow resets flags)
            }
            else if (IsKeyPressed(keyboard, Keys.H) || IsKeyPressed(keyboard, Keys.L)
                || (apress && detail.Contains(mouse.X, mouse.Y)))
            {
                _automapHighDetail = !_automapHighDetail;
                Log($"Automap detail: {(_automapHighDetail ? "high" : "low")}.");
            }
            else if (IsKeyPressed(keyboard, Keys.S) || (apress && scanner.Contains(mouse.X, mouse.Y)))
                TryAutomapScanner(); // P116 (review H): spend a Motion Sensor charge for the scanner view
```

Replace with:

```csharp
            (Rectangle scanner, Rectangle cancel, Rectangle detail) = AutomapButtons();
            bool apress = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.A)
                || (apress && cancel.Contains(uiMouse.X, uiMouse.Y)))
            {
                _automapOpen = false;
                _automapScanner = false; // the scanner view lasts one automap session (automapShow resets flags)
            }
            else if (IsKeyPressed(keyboard, Keys.H) || IsKeyPressed(keyboard, Keys.L)
                || (apress && detail.Contains(uiMouse.X, uiMouse.Y)))
            {
                _automapHighDetail = !_automapHighDetail;
                Log($"Automap detail: {(_automapHighDetail ? "high" : "low")}.");
            }
            else if (IsKeyPressed(keyboard, Keys.S) || (apress && scanner.Contains(uiMouse.X, uiMouse.Y)))
                TryAutomapScanner(); // P116 (review H): spend a Motion Sensor charge for the scanner view
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Run the opening golden suite**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Expected: all scenarios pass, unaffected.

- [ ] **Step 8: Manual visual check — automap at a non-4:3 window**

Run:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage3b-automap.png \
  -- --automap
```
(check `Program.cs` for the exact flag that forces `_automapOpen = true` — search for
`--automap` or how the automap is opened from the CLI; the plan's earlier stages' `--menu`
precedent suggests a direct flag exists).

Read the resulting PNG. Expected: AUTOMAP.frm renders at 1.5× baseline, centered, undistorted,
with plotted dots/markers still aligned to the map art beneath them (not offset).

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Panels.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the automap to fill non-4:3 windows

Wires Stage 1/2/3a's UiScale infrastructure into DrawAutomap: like
Pip-Boy/Options, its art-present and art-absent branches share one
function's viewport-relative math, so the whole method now draws
inside one scoped, scaled SpriteBatch block. Converts BOTH of this
screen's independent, textually-identical origin copies in lockstep --
AutomapButtons() and DrawAutomap's own separate inline recomputation
of the same formula -- to VirtualViewport(), so the button hit-tests
keep agreeing with what's drawn.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```
