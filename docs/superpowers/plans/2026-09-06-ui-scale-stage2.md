# UI Scale — Stage 2: Dialog + Main Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the dialog panel and the main-menu family (Title / character-pick / creation
editor) render at a uniform scale that fills a non-4:3 window, matching `fallout2-ce`'s
fullscreen stretch, without touching any other screen's size or hit-testing yet.

**Architecture:** Stage 1 (shipped) built `Hexwaste.Formats.Rendering.UiScale` (pure math) and
`ViewerGame`'s `UiScale()`/`VirtualViewport()`/`UiMouse()` wrappers — currently unused. This
stage wires them into exactly two call paths: `DrawDialogPanel`/`DrawConversationPanel`
(`ViewerGame.cs`) and the main-menu family dispatch inside `DrawTextOverlay`
(`ViewerGame.Hud.cs`) plus its shared `MenuOrigin()` helper (`ViewerGame.Shell.cs`). Both draw
paths open their own short-lived, scoped `SpriteBatch.Begin(transformMatrix: UiScaleMatrix())`
block — ending the shared unscaled batch just before, and reopening it just after — so only
these two screens scale; every other screen drawn in the shared batch (inventory, Skilldex,
Pip-Boy, HUD bar, etc.) is untouched and keeps rendering through the unscaled batch exactly as
today. This differs mechanically from the Stage 2 spec's illustrative pseudocode (which showed
one transform added at the single top-level `Begin` in `Draw()`) — direct code reading during
planning found that call wraps *every* screen, including ones not yet in scope, so a global
transform there would scale them too. Scoping the transform inside `DrawDialogPanel` and inside
`DrawTextOverlay`'s menu-dispatch branch achieves the spec's stated intent (only dialog + main
menu scale) with a smaller, more local diff and zero change to `Draw()`'s call sequence or the
draw order of anything else in the frame.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`SpriteBatch.Begin`/`End`, `Matrix.CreateScale`).

## Global Constraints

- Scale model: `scale = min(viewportWidth/640, viewportHeight/480)` (Stage 1's
  `UiScale.ComputeScale`, already shipped and unit-tested — do not reimplement).
- Only the dialog panel and the main-menu family (`MenuState.Title`, `.CharacterPick`,
  `.CreateStats`, `.CreateTraits`, `.CreateTags`) scale in this stage. `MenuState.Credits` and
  `.Endgame` (drawn by `DrawCredits`/`DrawEndgame`, neither of which uses `MenuOrigin()`) are
  explicitly **not** touched in this stage — they stay in the unscaled batch, unchanged.
- No other screen (inventory, character sheet, Skilldex, Pip-Boy, automap, options, preferences,
  save/load, aim dialog, tactics, HUD bar, action menu, elevator picker, worldmap chrome) changes
  in this stage.
- No golden-transcript regression: headless runs never call `Draw()`, so none of this is
  reachable in that path (same invariant as Stage 1 and the dialog-frame feature before it).
  Still run the existing golden suites after each task as a regression net.
- Every position read from a real device (mouse) or used to lay out scaled content must come
  from `UiMouse()`/`VirtualViewport()` (Stage 1, `ViewerGame.UiScale.cs`) inside a scaled block —
  never a raw `GraphicsDevice.Viewport`/`Mouse.GetState().X/Y` mixed into scaled content, and
  never `UiMouse()`/`VirtualViewport()` used for content staying in an unscaled block.

## File Structure

- `src/Hexwaste.Viewer/ViewerGame.UiScale.cs` — gains one new method, `UiScaleMatrix()`, reused
  by both tasks so the `Matrix.CreateScale(UiScale())` call appears in exactly one place.
- `src/Hexwaste.Viewer/ViewerGame.cs` — `DrawDialogPanel` (scoped scaled batch),
  `DrawConversationPanel` (viewport source), the two `HitTestDialogOption` call sites and the
  hover-highlight mouse read in `DrawConversationPanel` (mouse source), and `Update()` (the new
  `Point uiMouse` local, reused by both tasks).
- `src/Hexwaste.Viewer/ViewerGame.Hud.cs` — `DrawTextOverlay`'s menu-dispatch chain (scoped
  scaled batch + merged art/fallback dispatch).
- `src/Hexwaste.Viewer/ViewerGame.Shell.cs` — `MenuOrigin()` (viewport source).

## Task 1: Dialog panel

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.UiScale.cs`
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:1934` (Update, new local), `:2519-2548` (scripted
  dialog input), `:2551-2573` (companion-hub input), `:6171-6184` (`DrawDialogPanel`),
  `:6472-6579` (`DrawConversationPanel`)

**Interfaces:**
- Consumes: `UiScale()`, `VirtualViewport()`, `UiMouse()` (Stage 1, already shipped in
  `ViewerGame.UiScale.cs`).
- Produces: `UiScaleMatrix()` — `private Matrix UiScaleMatrix()` in `ViewerGame.UiScale.cs`,
  reused by Task 2. A `Point uiMouse` local declared in `Update()` immediately after
  `MouseState mouse = Mouse.GetState();` (line 1934), reused by Task 2 to build a UI-scaled
  `MouseState` for the main-menu family's mouse handlers.

- [ ] **Step 1: Add `UiScaleMatrix()` and refresh the "not yet used" doc comments**

Open `src/Hexwaste.Viewer/ViewerGame.UiScale.cs`. It currently reads exactly:

```csharp
using Hexwaste.Formats.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

public sealed partial class ViewerGame
{
    /// <summary>The current UI scale factor for this window size — see
    /// Hexwaste.Formats.Rendering.UiScale.ComputeScale. Not yet used by any draw call; later,
    /// separately-specced stages wire it into SpriteBatch.Begin's transformMatrix.</summary>
    private float UiScale()
    {
        Viewport vp = GraphicsDevice.Viewport;
        return Formats.Rendering.UiScale.ComputeScale(vp.Width, vp.Height);
    }

    /// <summary>The "virtual" (pre-scale) canvas for this window size — see
    /// Hexwaste.Formats.Rendering.UiScale.ComputeVirtualViewport. Not yet used by any layout
    /// code; later stages replace existing GraphicsDevice.Viewport.Bounds references with this
    /// wherever content is drawn through the scaled batch.</summary>
    private Rectangle VirtualViewport()
    {
        Viewport vp = GraphicsDevice.Viewport;
        (int w, int h) = Formats.Rendering.UiScale.ComputeVirtualViewport(vp.Width, vp.Height, UiScale());
        return new Rectangle(0, 0, w, h);
    }

    /// <summary>The real mouse position transformed into the same virtual-canvas coordinate
    /// space as VirtualViewport — see Hexwaste.Formats.Rendering.UiScale.TransformMouse. Not yet
    /// used by any hit-test; later stages replace existing Mouse.GetState().X/Y position reads
    /// with this wherever a click is tested against scaled content. Callers that need button-
    /// press state (e.g. Mouse.GetState().LeftButton) keep calling Mouse.GetState() directly for
    /// that — only the position needs transforming.</summary>
    private Point UiMouse()
    {
        MouseState mouse = Mouse.GetState();
        (int x, int y) = Formats.Rendering.UiScale.TransformMouse(mouse.X, mouse.Y, UiScale());
        return new Point(x, y);
    }
}
```

Replace the whole file body with:

```csharp
using Hexwaste.Formats.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

public sealed partial class ViewerGame
{
    /// <summary>The current UI scale factor for this window size — see
    /// Hexwaste.Formats.Rendering.UiScale.ComputeScale. Wired in starting Stage 2 (the dialog
    /// panel and the main-menu family); later stages fold in the remaining screens.</summary>
    private float UiScale()
    {
        Viewport vp = GraphicsDevice.Viewport;
        return Formats.Rendering.UiScale.ComputeScale(vp.Width, vp.Height);
    }

    /// <summary>A SpriteBatch.Begin(transformMatrix:) value that scales virtual-canvas content
    /// (see VirtualViewport) up to real screen pixels. Every scaled SpriteBatch block in the
    /// Viewer builds its transform from this one method, so UiScale() is computed once per
    /// scoped Begin rather than separately at each call site.</summary>
    private Matrix UiScaleMatrix() => Matrix.CreateScale(UiScale());

    /// <summary>The "virtual" (pre-scale) canvas for this window size — see
    /// Hexwaste.Formats.Rendering.UiScale.ComputeVirtualViewport. Wired in starting Stage 2 (the
    /// dialog panel and the main-menu family); later stages replace the remaining
    /// GraphicsDevice.Viewport(.Bounds) references with this wherever content is drawn through a
    /// scaled batch.</summary>
    private Rectangle VirtualViewport()
    {
        Viewport vp = GraphicsDevice.Viewport;
        (int w, int h) = Formats.Rendering.UiScale.ComputeVirtualViewport(vp.Width, vp.Height, UiScale());
        return new Rectangle(0, 0, w, h);
    }

    /// <summary>The real mouse position transformed into the same virtual-canvas coordinate
    /// space as VirtualViewport — see Hexwaste.Formats.Rendering.UiScale.TransformMouse. Wired in
    /// starting Stage 2 (the dialog panel and the main-menu family); later stages replace the
    /// remaining Mouse.GetState().X/Y position reads with this wherever a click is tested against
    /// scaled content. Callers that need button-press state (e.g. Mouse.GetState().LeftButton)
    /// keep calling Mouse.GetState() directly for that — only the position needs transforming.</summary>
    private Point UiMouse()
    {
        MouseState mouse = Mouse.GetState();
        (int x, int y) = Formats.Rendering.UiScale.TransformMouse(mouse.X, mouse.Y, UiScale());
        return new Point(x, y);
    }
}
```

- [ ] **Step 2: Build to confirm the new method compiles**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Add the shared `uiMouse` local in `Update()`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 1932-1934):

```csharp
        _frameClock.Restart();
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();
```

Replace with:

```csharp
        _frameClock.Restart();
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();
        // Stage 2 (UI Scale): the dialog panel and the main-menu family hit-test against
        // VirtualViewport()-derived rectangles, so their click position must be the same
        // transformed point, not the raw device mouse. Every other Update() mouse hit-test in
        // this method (worldmap, character sheet, inventory, etc.) keeps using `mouse` directly —
        // only in-scope screens use `uiMouse`.
        Point uiMouse = UiMouse();
```

- [ ] **Step 4: Route dialog/companion-hub hit-testing through `uiMouse`**

In the same file, find (around line 2518-2536):

```csharp
        // Dialog mode swallows all input.
        if (_dialog is not null)
        {
            for (int i = 0; i < 9; i++)
            {
                if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
                {
                    ChooseDialogOption(i);
                    break;
                }
            }

            if (_dialog is not null && mouse.LeftButton == ButtonState.Pressed
                && _previousMouse.LeftButton == ButtonState.Released)
            {
                int hit = HitTestDialogOption(mouse.X, mouse.Y);
                if (hit >= 0)
                    ChooseDialogOption(hit);
            }
```

Replace the `HitTestDialogOption` line with:

```csharp
                int hit = HitTestDialogOption(uiMouse.X, uiMouse.Y);
```

Then find (around line 2551-2565), the matching companion-hub block:

```csharp
        // Companion-control hub swallows input (phase-10 M4).
        if (_companionHub is not null)
        {
            for (int i = 0; i < _hubOptions.Count; i++)
                if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
                {
                    ChooseCompanionOption(i);
                    break;
                }
            if (_companionHub is not null && mouse.LeftButton == ButtonState.Pressed
                && _previousMouse.LeftButton == ButtonState.Released)
            {
                int hit = HitTestDialogOption(mouse.X, mouse.Y);
                if (hit >= 0)
                    ChooseCompanionOption(hit);
            }
```

Replace that `HitTestDialogOption` line the same way:

```csharp
                int hit = HitTestDialogOption(uiMouse.X, uiMouse.Y);
```

(`.LeftButton`/`.Released` checks stay on the raw `mouse` — only the position argument changes.)

- [ ] **Step 5: Scope `DrawDialogPanel` into its own scaled `SpriteBatch` block**

Find (around line 6171-6184):

```csharp
    /// <summary>Text dialog panel: reply on top, numbered options below (keys 1-9 or click).</summary>
    private void DrawDialogPanel()
    {
        if (_companionHub is not null)
        {
            DrawConversationPanel(ObjectName(_companionHub), "What do you need?",
                [.. _hubOptions.Select(o => o.Label)], isPartyMember: true);
            return;
        }
        if (_dialog is not null)
            DrawConversationPanel(_dialog.NpcName, _dialog.Reply, _dialog.Options, _dialog.OptionReactions,
                EffectiveHeadId(),
                isPartyMember: _dialogNpc is not null && (_scriptHost?.PartyMembers.Contains(_dialogNpc) ?? false));
    }
```

Replace with:

```csharp
    /// <summary>Text dialog panel: reply on top, numbered options below (keys 1-9 or click).
    /// Stage 2 (UI Scale): draws into its own scoped, scaled SpriteBatch block — the shared
    /// unscaled batch (opened once per frame in Draw()) is suspended for the duration and resumed
    /// immediately after, so only the dialog panel scales; every other screen drawn later in the
    /// same frame is unaffected.</summary>
    private void DrawDialogPanel()
    {
        if (_companionHub is null && _dialog is null)
            return;

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        if (_companionHub is not null)
        {
            DrawConversationPanel(ObjectName(_companionHub), "What do you need?",
                [.. _hubOptions.Select(o => o.Label)], isPartyMember: true);
        }
        else if (_dialog is not null)
        {
            DrawConversationPanel(_dialog.NpcName, _dialog.Reply, _dialog.Options, _dialog.OptionReactions,
                EffectiveHeadId(),
                isPartyMember: _dialogNpc is not null && (_scriptHost?.PartyMembers.Contains(_dialogNpc) ?? false));
        }

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 6: Switch `DrawConversationPanel`'s viewport and mouse sources**

Find, in `DrawConversationPanel` (around line 6485):

```csharp
        Rectangle viewport = GraphicsDevice.Viewport.Bounds;
```

Replace with:

```csharp
        // Stage 2 (UI Scale): DrawDialogPanel already opened a scaled SpriteBatch block around
        // this call, so every rectangle built from `viewport` below (the frame origin, the dim
        // overlay, the reply/options panel) must be expressed in virtual-canvas coordinates, not
        // real device pixels — VirtualViewport() (not GraphicsDevice.Viewport.Bounds) is what the
        // scaled batch's transform expects.
        Rectangle viewport = VirtualViewport();
```

Then find, later in the same method (around line 6556-6570):

```csharp
        y += lineHeight / 2;
        MouseState mouse = Mouse.GetState();
        _dialogOptionRects.Clear();
        Rectangle currentRect = Rectangle.Empty;
        int currentOption = -1;
        foreach ((int option, string line, bool first) in optionLines)
        {
            var lineRect = new Rectangle(panelX + 16, y, textWidth, lineHeight);
            if (first && currentOption >= 0)
                _dialogOptionRects.Add(currentRect);
            currentRect = first ? lineRect : Rectangle.Union(currentRect, lineRect);
            currentOption = option;

            bool hovered = lineRect.Contains(mouse.X, mouse.Y);
```

Replace the `MouseState mouse = Mouse.GetState();` line with:

```csharp
        Point mouse = UiMouse(); // Stage 2 (UI Scale): hover-highlight against the same virtual-canvas point Update() hit-tests with
```

(`lineRect.Contains(mouse.X, mouse.Y)` below needs no further change — `Rectangle.Contains(int, int)` accepts the `Point`'s fields exactly as it did the `MouseState`'s.)

- [ ] **Step 7: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.` (no leftover `MouseState`/`Mouse` reference errors in `DrawConversationPanel`)

- [ ] **Step 8: Run the opening + quest golden suites (headless regression net)**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Run: `FALLOUT2_DIR=./game-data scripts/quest-golden.sh check`
Expected: both report all scenarios passing, byte-identical to `tests/golden-opening/` /
`tests/golden-quest/` — dialog goldens are text-transcript based and never call `Draw()`, so
this step is unaffected by anything in this task; a failure here means something outside the
intended scope broke.

- [ ] **Step 9: Manual visual check — dialog at a non-4:3 window**

The Viewer's default window is 1280x720 (`ViewerGame.cs:1120-1121`), which is 16:9 — already a
non-4:3 size, so no extra flag is needed to exercise scaling.

Run:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage2-dialog.png \
  -- --goto 19680 --talk 19684
```
(adjust the `--goto`/`--talk` hex/target to a known NPC near the player's start tile if this
exact pair doesn't resolve — any live, non-headless conversation works; the dialog-frame feature
earlier this session used a guard NPC near Temple of Trials for the same kind of check.)

Read the resulting PNG. Expected: the dialog frame, head/portrait area, and reply/options text
are all visibly larger than Stage 1's screenshot baseline (1.5× the old size, matching
`min(1280/640, 720/480) = 1.5`), centered in the window, with no stretching/distortion and no
double-scaled or cut-off text.

- [ ] **Step 10: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.UiScale.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the dialog panel to fill non-4:3 windows

Wires Stage 1's UiScale infrastructure into DrawDialogPanel and
DrawConversationPanel: the dialog draws inside its own scoped, scaled
SpriteBatch block (UiScaleMatrix()) rather than the shared unscaled
one, and its viewport/mouse math switches from the raw device values
to VirtualViewport()/UiMouse() so option hit-testing still lands under
the cursor at any window size. Every other screen keeps its native,
unscaled size.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

## Task 2: Main-menu family (Title / character-pick / creation editor)

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Shell.cs:42-46` (`MenuOrigin`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.Hud.cs:560-633` (`DrawTextOverlay`'s menu dispatch)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2022-2039` (`Update`'s menu-state block)

**Interfaces:**
- Consumes: `UiScaleMatrix()` (Task 1, `ViewerGame.UiScale.cs`), `VirtualViewport()` (Stage 1),
  the `Point uiMouse` local declared in `Update()` by Task 1 (immediately after
  `MouseState mouse = Mouse.GetState();`).
- Produces: nothing new consumed by a later task in this stage — this is the last task.

- [ ] **Step 1: Switch `MenuOrigin()` to the virtual viewport**

In `src/Hexwaste.Viewer/ViewerGame.Shell.cs`, find (around line 41-46):

```csharp
    /// <summary>The window-centred origin of the 640x480 shell backdrop (the panels' ox/oy convention).</summary>
    private (int ox, int oy) MenuOrigin()
    {
        Viewport vp = GraphicsDevice.Viewport;
        return ((vp.Width - 640) / 2, (vp.Height - 480) / 2);
    }
```

Replace with:

```csharp
    /// <summary>The window-centred origin of the 640x480 shell backdrop (the panels' ox/oy
    /// convention). Stage 2 (UI Scale): callers (DrawAuthenticMainMenu/Selector/Creation and
    /// their mouse handlers) always run inside the scaled SpriteBatch block DrawTextOverlay opens
    /// around the menu-family dispatch, so this must return a virtual-canvas origin, not a real
    /// device one.</summary>
    private (int ox, int oy) MenuOrigin()
    {
        Rectangle vp = VirtualViewport();
        return ((vp.Width - 640) / 2, (vp.Height - 480) / 2);
    }
```

- [ ] **Step 2: Switch the three art-screens' full-screen black backdrop to the virtual viewport too**

`DrawAuthenticMainMenu`, `DrawAuthenticSelector`, and `DrawAuthenticCreation` (all in
`ViewerGame.Shell.cs`) each open with the same pattern — a `Viewport vp = GraphicsDevice.Viewport;`
used only to size a full-black backdrop rectangle before drawing the 640x480 art at
`MenuOrigin()`. Left as `GraphicsDevice.Viewport`, that backdrop rectangle is expressed in real
device pixels while everything else in these methods (drawn inside the scaled batch Step 3
below opens) is in virtual-canvas pixels — harmless at the common `scale >= 1` case (the
oversized backdrop still fully covers the real screen once the transform scales it up further),
but not at `scale < 1` (a window smaller than 640x480), where it would leave a gap. Fix all
three for consistency with the rest of this stage's rule (scaled content only ever reads
`VirtualViewport()`).

In `src/Hexwaste.Viewer/ViewerGame.Shell.cs`, find (three separate, near-identical occurrences,
at the top of `DrawAuthenticMainMenu`, `DrawAuthenticSelector`, and `DrawAuthenticCreation`):

```csharp
        Viewport vp = GraphicsDevice.Viewport;
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
```

Replace each occurrence with:

```csharp
        Rectangle vp = VirtualViewport();
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
```

(`vp.Width`/`vp.Height` are read identically whether `vp` is a MonoGame `Viewport` or a
`Rectangle` — no other line in any of the three methods reads `vp`.)

- [ ] **Step 2b: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Scope the menu-family dispatch into its own scaled `SpriteBatch` block**

In `src/Hexwaste.Viewer/ViewerGame.Hud.cs`, find the entire block (around line 560-633):

```csharp
        // P83-M1/M2/M4: the authentic mainmenu.frm / pickchar.frm / credits.txt screens (each with its own
        // black background). Falls through to the plain-text path when the art is absent.
        // P139: a full-screen movie (drawn above) suppresses the menu chrome so an INTRO started from the
        // Title screen plays over black, not behind the menu buttons.
        if (_moviePlayer is not null)
        {
            // the movie owns the frame — no menu chrome
        }
        else if (_menu == MenuState.Credits)
        {
            DrawCredits();
        }
        else if (_menu == MenuState.Endgame)
        {
            DrawEndgame();
        }
        else if (_menu == MenuState.Title && DrawAuthenticMainMenu())
        {
            // handled by the art path
        }
        else if (_menu == MenuState.CharacterPick && DrawAuthenticSelector())
        {
            // handled by the art path
        }
        else if (_menu is MenuState.CreateStats or MenuState.CreateTraits or MenuState.CreateTags
                 && DrawAuthenticCreation())
        {
            // handled by the art path (the unified edtrcrte.frm creation screen)
        }
        else if (_menu != MenuState.None)
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel,
                new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                new Color(0, 0, 0, 200));
            var center = new Vector2(GraphicsDevice.Viewport.Width / 2f, GraphicsDevice.Viewport.Height / 2f);
            var gold = new Color(252, 252, 84);
            var menuGreen = new Color(0, 252, 0);
            var gray = new Color(140, 140, 140);

            const string title = "H E X W A S T E";
            _fontRenderer.Draw(_spriteBatch, title,
                new Vector2(center.X - _fontRenderer.MeasureWidth(title) / 2f, center.Y - 120), gold);
            const string subtitle = "a Fallout 2 engine slice - needs your own game data";
            _fontRenderer.Draw(_spriteBatch, subtitle,
                new Vector2(center.X - _fontRenderer.MeasureWidth(subtitle) / 2f, center.Y - 120 + _fontRenderer.LineHeight * 1.4f), gray);

            if (_menu is MenuState.Title or MenuState.CharacterPick)
            {
                // The Title fallback must list the 6 buttons in MainMenuButtons order so the row index maps
                // to the same ActivateMainMenuButton action the art path uses (P83-M1 review fix).
                string[] items = _menu == MenuState.Title
                    ? ["Intro", "New game", "Load game", "Options", "Credits", "Exit"]
                    : ["Create your own", .. _premadeGcds.Select(g => g.Label)];
                float itemY = center.Y - 20;
                for (int i = 0; i < items.Length; i++)
                {
                    string line = (i == _menuIndex ? "> " : "  ") + items[i];
                    _fontRenderer.Draw(_spriteBatch, line,
                        new Vector2(center.X - _fontRenderer.MeasureWidth(line) / 2f, itemY),
                        i == _menuIndex ? menuGreen : gray);
                    itemY += _fontRenderer.LineHeight * 1.6f;
                }
                string hint = _menu == MenuState.Title
                    ? "arrows + Enter; Esc quits"
                    : "create or pick a character - arrows + Enter; Esc back";
                _fontRenderer.Draw(_spriteBatch, hint,
                    new Vector2(center.X - _fontRenderer.MeasureWidth(hint) / 2f, itemY + _fontRenderer.LineHeight), gray);
            }
            else
            {
                DrawCreationScreen(center, gold, menuGreen, gray);
            }
        }
```

Replace with:

```csharp
        // P83-M1/M2/M4: the authentic mainmenu.frm / pickchar.frm / credits.txt screens (each with its own
        // black background). Falls through to the plain-text path when the art is absent.
        // P139: a full-screen movie (drawn above) suppresses the menu chrome so an INTRO started from the
        // Title screen plays over black, not behind the menu buttons.
        if (_moviePlayer is not null)
        {
            // the movie owns the frame — no menu chrome
        }
        else if (_menu == MenuState.Credits)
        {
            DrawCredits();
        }
        else if (_menu == MenuState.Endgame)
        {
            DrawEndgame();
        }
        else if (_menu != MenuState.None)
        {
            // Stage 2 (UI Scale): the main-menu family (Title/CharacterPick/CreateStats-Traits-
            // Tags — the only remaining MenuState values reachable here) draws into its own
            // scoped, scaled SpriteBatch block — same technique as DrawDialogPanel — so these
            // three screens match fo2ce's fullscreen stretch. Credits/Endgame above don't use
            // MenuOrigin() and stay unscaled; the plain-text fallback below (art missing) also
            // stays unscaled, matching its pre-existing, already-degraded presentation.
            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());
            bool handled = _menu == MenuState.Title ? DrawAuthenticMainMenu()
                : _menu == MenuState.CharacterPick ? DrawAuthenticSelector()
                : _menu is MenuState.CreateStats or MenuState.CreateTraits or MenuState.CreateTags
                  && DrawAuthenticCreation();
            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

            if (!handled)
            {
                _panelPixel ??= CreatePixel();
                _spriteBatch.Draw(_panelPixel,
                    new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                    new Color(0, 0, 0, 200));
                var center = new Vector2(GraphicsDevice.Viewport.Width / 2f, GraphicsDevice.Viewport.Height / 2f);
                var gold = new Color(252, 252, 84);
                var menuGreen = new Color(0, 252, 0);
                var gray = new Color(140, 140, 140);

                const string title = "H E X W A S T E";
                _fontRenderer.Draw(_spriteBatch, title,
                    new Vector2(center.X - _fontRenderer.MeasureWidth(title) / 2f, center.Y - 120), gold);
                const string subtitle = "a Fallout 2 engine slice - needs your own game data";
                _fontRenderer.Draw(_spriteBatch, subtitle,
                    new Vector2(center.X - _fontRenderer.MeasureWidth(subtitle) / 2f, center.Y - 120 + _fontRenderer.LineHeight * 1.4f), gray);

                if (_menu is MenuState.Title or MenuState.CharacterPick)
                {
                    // The Title fallback must list the 6 buttons in MainMenuButtons order so the row index maps
                    // to the same ActivateMainMenuButton action the art path uses (P83-M1 review fix).
                    string[] items = _menu == MenuState.Title
                        ? ["Intro", "New game", "Load game", "Options", "Credits", "Exit"]
                        : ["Create your own", .. _premadeGcds.Select(g => g.Label)];
                    float itemY = center.Y - 20;
                    for (int i = 0; i < items.Length; i++)
                    {
                        string line = (i == _menuIndex ? "> " : "  ") + items[i];
                        _fontRenderer.Draw(_spriteBatch, line,
                            new Vector2(center.X - _fontRenderer.MeasureWidth(line) / 2f, itemY),
                            i == _menuIndex ? menuGreen : gray);
                        itemY += _fontRenderer.LineHeight * 1.6f;
                    }
                    string hint = _menu == MenuState.Title
                        ? "arrows + Enter; Esc quits"
                        : "create or pick a character - arrows + Enter; Esc back";
                    _fontRenderer.Draw(_spriteBatch, hint,
                        new Vector2(center.X - _fontRenderer.MeasureWidth(hint) / 2f, itemY + _fontRenderer.LineHeight), gray);
                }
                else
                {
                    DrawCreationScreen(center, gold, menuGreen, gray);
                }
            }
        }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Route the menu-family mouse handlers through `uiMouse`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2018-2039):

```csharp
        if (_menu != MenuState.None && !_saveLoadOpen)
        {
            _audio?.PlayMusic("07desert");
            if (_menu == MenuState.Credits)
                UpdateCredits(gameTime.ElapsedGameTime.TotalMilliseconds, keyboard, mouse);
            else if (_menu == MenuState.Endgame)
                UpdateEndgame(gameTime.ElapsedGameTime.TotalMilliseconds, keyboard, mouse);
            else
            {
                HandleMenuInput(keyboard);
                HandleMenuMouse(mouse);
                HandleSelectorMouse(mouse);
                HandleCreationMouse(mouse);
            }
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }
```

Replace the `else` branch with:

```csharp
            else
            {
                // Stage 2 (UI Scale): these three handlers hit-test against MenuOrigin()'s
                // VirtualViewport()-derived rectangles, so they need the same scaled position as
                // the draw path (DrawTextOverlay's scoped batch) — a synthetic MouseState carries
                // uiMouse's position through unchanged, since only its X/Y differ from `mouse`.
                HandleMenuInput(keyboard);
                MouseState uiMouseState = new(uiMouse.X, uiMouse.Y, mouse.ScrollWheelValue,
                    mouse.LeftButton, mouse.MiddleButton, mouse.RightButton,
                    mouse.XButton1, mouse.XButton2, mouse.HorizontalScrollWheelValue);
                HandleMenuMouse(uiMouseState);
                HandleSelectorMouse(uiMouseState);
                HandleCreationMouse(uiMouseState);
            }
```

(`UpdateCredits`/`UpdateEndgame` keep the raw `mouse` — Credits/Endgame are explicitly out of
scope for this stage, per the Global Constraints.)

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Run the opening golden suite (headless regression net)**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Expected: all scenarios pass, byte-identical to `tests/golden-opening/` — none of this task's
changes are reachable from a headless run (no `Draw()`, and `Update()`'s menu-state branch is
`_menu != MenuState.None`, which the opening-spine scenarios never enter).

- [ ] **Step 8: Manual visual check — main menu, character pick, and creation editor**

Run, for each of the three screens:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage2-mainmenu.png -- --menu

FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage2-pick.png -- --menu pick

FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage2-create.png -- --menu create
```
(check `src/Hexwaste.Viewer/Program.cs`'s `--menu` case around line 73 for the exact accepted
sub-state tokens if `pick`/`create` don't match — the flag exists specifically to force these
screens for screenshot/testing per its own comment.)

Read each resulting PNG. Expected: `mainmenu.frm`/`pickchar.frm`/`edtrcrte.frm` and their text
all render at 1.5× Stage 1's baseline size (matching `min(1280/720 scale) = 1.5` on the default
1280x720 window), centered, un-distorted. Then, with the main-menu screenshot still open,
manually move the mouse in a live run and click each of the six main-menu buttons (INTRO/NEW
GAME/LOAD GAME/OPTIONS/CREDITS/EXIT) to confirm the click lands where the cursor visually sits,
not at the old, pre-scale button position.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Shell.cs src/Hexwaste.Viewer/ViewerGame.Hud.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the main-menu family to fill non-4:3 windows

Wires Stage 1's UiScale infrastructure into MenuOrigin() and the
Title/CharacterPick/CreateStats-Traits-Tags dispatch inside
DrawTextOverlay: those three screens draw inside their own scoped,
scaled SpriteBatch block, and their mouse handlers hit-test against a
UI-scaled synthetic MouseState instead of the raw device one. Credits,
Endgame, and the plain-text art-missing fallback are explicitly left
unscaled, matching their pre-existing presentation. Every other screen
keeps its native, unscaled size.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```
