# UI Scale Stage 6: Shell, Endgame, Cutscene Screens, and HUD Text Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the last six unscaled surfaces in the Viewer — death art/narration, the ending
slideshow, credits, the cutscene browser, the movie player/card, and the persistent gameplay HUD
text overlay — render at the same uniform scale as every other screen, filling non-4:3 windows.
This is the final UI Scale stage: after it, there are no more unscaled screens anywhere in the
Viewer outside documented, deliberately-unscaled art-absent fallbacks.

**Architecture:** Unlike every prior stage, these six surfaces are not each their own top-level
`Draw*()` method with one call site — most live inline as independent sequential `if` blocks
inside one method, `DrawTextOverlay()`, each currently reading `GraphicsDevice.Viewport` on its
own. The fix is per-block scoping, using that same method's own already-scaled main-menu-family
block (from an earlier stage) as the template: open a scoped, scaled `SpriteBatch` block around
just one `if` block, close it before the next. `MenuOriginDevice()` — a documented but
never-executed "later stages delete this" TODO, proven identical to the already-scaled
`MenuOrigin()` except for its viewport source — gets deleted once its three callers switch.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`SpriteBatch.Begin`/`End`), the already-shipped
`UiScale`/`VirtualViewport`/`UiMouse`/`UiScaleMatrix` infrastructure (Stage 1 through worldmap
chrome).

## Global Constraints

- Scale model: `scale = min(viewportWidth/640, viewportHeight/480)` (Stage 1, shipped,
  unit-tested — do not touch).
- `VirtualViewport()` always returns `new Rectangle(0, 0, w, h)` — offset always `(0,0)`.
- `_hudBarHeight` is an established **device-pixel** quantity (its own doc comment,
  `ViewerGame.Hud.cs:117-121`) — this project's precedent conversion for exactly this unit
  mismatch is `SkilldexOrigin` (`ViewerGame.Panels.cs:634-646`):
  ```csharp
  Rectangle vp = VirtualViewport();
  int hudBarVirtual = (int)(_hudBarHeight / UiScale());
  ```
  Task 2's `hudY` formula must use this identical substitution.
- Confirmed via direct grep this session: `ViewerGame.Harness.cs` has zero references to
  `MenuOriginDevice`, `DrawDeathArt`, `DrawDeathNarration`, `DrawEndgame`, `DrawCredits`,
  `DrawCutsceneMenu`, `CutsceneListLayout`, or `MenuOrigin` — no changes needed there. It does
  call `CutsceneNames()` (a plain string list with no viewport math), which this plan does not
  touch.
- The plain-text main-menu fallback (`ViewerGame.Hud.cs:617-661`) and the HUD-bar-hidden
  message-log fallback (`:664-678`-ish) are OUT OF SCOPE — both are explicitly documented as
  deliberately unscaled, matching every other screen's art-absent convention. Do not touch them.
- Every position read used for scaled content must come from `UiMouse()`/`VirtualViewport()`
  inside a scaled block — never a raw `GraphicsDevice.Viewport`/`Mouse.GetState()` mixed into
  scaled content.

## File Structure

- `src/Hexwaste.Viewer/ViewerGame.Shell.cs` — `MenuOriginDevice()` (deleted), `DrawDeathArt`,
  `DrawCredits`.
- `src/Hexwaste.Viewer/ViewerGame.Endgame.cs` — `DrawDeathNarration`, `DrawEndgame`.
- `src/Hexwaste.Viewer/ViewerGame.Hud.cs` — `DrawTextOverlay` (all six blocks),
  `DrawCutsceneMenu`, `CutsceneListLayout` is actually in `ViewerGame.cs` (confirmed below).
- `src/Hexwaste.Viewer/ViewerGame.cs` — `CutsceneListLayout`, the `Update()` cutscene hit-test
  block.
- `src/Hexwaste.Viewer/MviePlayer.cs` — `Draw`'s signature.

## Task 1: Static screens (death art, endgame, credits, movie player/card)

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Shell.cs:46-59` (delete `MenuOriginDevice`, keep
  `MenuOrigin`), `:836-850` (`DrawDeathArt`), `:778-804` (`DrawCredits`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.Endgame.cs:150-171` (`DrawEndgame`), `:286-300`
  (`DrawDeathNarration`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.Hud.cs:526-599` (the death-screen block, the
  movie-player block, the `_movieCard` block, and the `DrawCredits`/`DrawEndgame` call sites)
- Modify: `src/Hexwaste.Viewer/MviePlayer.cs:141-149` (`Draw`'s signature)

**Interfaces:**
- Consumes: `MenuOrigin()` (already shipped, `ViewerGame.Shell.cs:46-50`), `VirtualViewport()`,
  `UiScaleMatrix()` (Stage 1, already shipped).
- Produces: `MviePlayer.Draw(SpriteBatch sb, Texture2D pixel, Rectangle viewport)` — the new
  signature Task 1's one call site (`ViewerGame.Hud.cs:558`) uses; no other file calls this
  method (confirmed via whole-tree grep).

- [ ] **Step 1: Delete `MenuOriginDevice()` and convert its 3 callers to `MenuOrigin()`**

Find, in `src/Hexwaste.Viewer/ViewerGame.Shell.cs` (lines 52-59):

```csharp
    /// <summary>The device-pixel twin of MenuOrigin(), for the 640x480 shell screens that are NOT
    /// yet drawn inside a scaled SpriteBatch block (death art/narration, the endgame slides).
    /// Later stages delete this once those screens fold into their own scaled batch.</summary>
    private (int ox, int oy) MenuOriginDevice()
    {
        Viewport vp = GraphicsDevice.Viewport;
        return ((vp.Width - 640) / 2, (vp.Height - 480) / 2);
    }

```

Delete this block entirely (keep `MenuOrigin()` immediately above it, lines 46-50, unchanged).

Find, in `src/Hexwaste.Viewer/ViewerGame.Shell.cs` (`DrawDeathArt`, around line 848):

```csharp
        (int ox, int oy) = MenuOriginDevice();
        _spriteBatch.Draw(_deathBg, new Rectangle(ox, oy, 640, 480), Color.White);
```

Replace with:

```csharp
        (int ox, int oy) = MenuOrigin();
        _spriteBatch.Draw(_deathBg, new Rectangle(ox, oy, 640, 480), Color.White);
```

Find, in `src/Hexwaste.Viewer/ViewerGame.Endgame.cs` (`DrawEndgame`, around line 159):

```csharp
        (int ox, int oy) = MenuOriginDevice();
        if (GetEndgameTexture(slide.FrmPath) is { } tex)
```

Replace with:

```csharp
        (int ox, int oy) = MenuOrigin();
        if (GetEndgameTexture(slide.FrmPath) is { } tex)
```

Find, in `src/Hexwaste.Viewer/ViewerGame.Endgame.cs` (`DrawDeathNarration`, around line 291):

```csharp
        (int ox, int oy) = MenuOriginDevice();
        int lh = _fontRenderer.LineHeight;
```

Replace with:

```csharp
        (int ox, int oy) = MenuOrigin();
        int lh = _fontRenderer.LineHeight;
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Convert `DrawDeathArt`'s own internal viewport read**

Find, in `src/Hexwaste.Viewer/ViewerGame.Shell.cs` (`DrawDeathArt`, around lines 843-849):

```csharp
        if (_deathBg is null)
            return false;
        Viewport vp = GraphicsDevice.Viewport;
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
        (int ox, int oy) = MenuOrigin();
        _spriteBatch.Draw(_deathBg, new Rectangle(ox, oy, 640, 480), Color.White);
        return true;
```

Replace with:

```csharp
        if (_deathBg is null)
            return false;
        Rectangle vp = VirtualViewport();
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
        (int ox, int oy) = MenuOrigin();
        _spriteBatch.Draw(_deathBg, new Rectangle(ox, oy, 640, 480), Color.White);
        return true;
```

- [ ] **Step 4: Scope the death-screen block in `DrawTextOverlay()`**

Find, in `src/Hexwaste.Viewer/ViewerGame.Hud.cs` (around lines 526-553):

```csharp
        if (_combat.IsGameOver || _debugDeathScreen)
        {
            _panelPixel ??= CreatePixel();
            // P83-M4: the authentic death.frm scene behind the options (text-only fallback if the art absent).
            if (!DrawDeathArt())
                _spriteBatch.Draw(_panelPixel,
                    new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                    new Color(0, 0, 0, 170));
            DrawDeathNarration(); // P100 M8: the enddeath.txt-selected narration subtitle
            var center = new Vector2(GraphicsDevice.Viewport.Width / 2f, GraphicsDevice.Viewport.Height / 2f);
            string[] lines =
            [
                "YOU HAVE DIED",
                $"Level {_dudeLevel}  -  {_dudeXp} XP  -  Day {_clock.Day}",
                "",
                "F9  Load last save",
                "N   New game",
                "Esc Quit",
            ];
            float lineY = center.Y - lines.Length * _fontRenderer.LineHeight;
            foreach (string line in lines)
            {
                Color color = line == lines[0] ? new Color(252, 0, 0) : new Color(252, 252, 84);
                _fontRenderer.Draw(_spriteBatch, line,
                    new Vector2(center.X - _fontRenderer.MeasureWidth(line) / 2f, lineY), color);
                lineY += _fontRenderer.LineHeight * 1.6f;
            }
        }
```

Replace with:

```csharp
        if (_combat.IsGameOver || _debugDeathScreen)
        {
            // Stage: UI Scale Stage 6 -- the fallback dark overlay and "YOU HAVE DIED" text live
            // inline here, not in a separable method, so the whole block scopes as one unit,
            // matching the main-menu-family block later in this same method.
            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

            _panelPixel ??= CreatePixel();
            Rectangle vp = VirtualViewport();
            // P83-M4: the authentic death.frm scene behind the options (text-only fallback if the art absent).
            if (!DrawDeathArt())
                _spriteBatch.Draw(_panelPixel,
                    new Rectangle(0, 0, vp.Width, vp.Height),
                    new Color(0, 0, 0, 170));
            DrawDeathNarration(); // P100 M8: the enddeath.txt-selected narration subtitle
            var center = new Vector2(vp.Width / 2f, vp.Height / 2f);
            string[] lines =
            [
                "YOU HAVE DIED",
                $"Level {_dudeLevel}  -  {_dudeXp} XP  -  Day {_clock.Day}",
                "",
                "F9  Load last save",
                "N   New game",
                "Esc Quit",
            ];
            float lineY = center.Y - lines.Length * _fontRenderer.LineHeight;
            foreach (string line in lines)
            {
                Color color = line == lines[0] ? new Color(252, 0, 0) : new Color(252, 252, 84);
                _fontRenderer.Draw(_spriteBatch, line,
                    new Vector2(center.X - _fontRenderer.MeasureWidth(line) / 2f, lineY), color);
                lineY += _fontRenderer.LineHeight * 1.6f;
            }

            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        }
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 6: Change `MviePlayer.Draw`'s signature from `Viewport` to `Rectangle`**

Find, in `src/Hexwaste.Viewer/MviePlayer.cs` (around lines 139-149):

```csharp
    /// <summary>Draw the current frame centred over a full-viewport black backdrop (native
    /// 640x320 size, letterboxed — no aspect distortion).</summary>
    public void Draw(SpriteBatch sb, Texture2D pixel, Viewport vp)
    {
        sb.Draw(pixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
        if (_texture is null)
            return;
        int ox = (vp.Width - _texture.Width) / 2;
        int oy = (vp.Height - _texture.Height) / 2;
        sb.Draw(_texture, new Rectangle(ox, oy, _texture.Width, _texture.Height), Color.White);
    }
```

Replace with:

```csharp
    /// <summary>Draw the current frame centred over a full-viewport black backdrop (native
    /// 640x320 size, letterboxed — no aspect distortion). Stage: UI Scale Stage 6 -- takes a
    /// Rectangle (only .Width/.Height are ever read) rather than a Viewport, matching
    /// WorldmapScreen's convention, so the caller can pass VirtualViewport() and draw this
    /// through a scaled SpriteBatch block.</summary>
    public void Draw(SpriteBatch sb, Texture2D pixel, Rectangle viewport)
    {
        sb.Draw(pixel, new Rectangle(0, 0, viewport.Width, viewport.Height), Color.Black);
        if (_texture is null)
            return;
        int ox = (viewport.Width - _texture.Width) / 2;
        int oy = (viewport.Height - _texture.Height) / 2;
        sb.Draw(_texture, new Rectangle(ox, oy, _texture.Width, _texture.Height), Color.White);
    }
```

- [ ] **Step 7: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: FAIL — the one call site in `ViewerGame.Hud.cs` still passes `GraphicsDevice.Viewport`
(a `Viewport`, not a `Rectangle`). This confirms the signature change took effect; the next step
fixes the caller.

- [ ] **Step 8: Scope the movie-player block and the `_movieCard` block**

Find, in `src/Hexwaste.Viewer/ViewerGame.Hud.cs` (around lines 555-582):

```csharp
        if (_moviePlayer is not null)
        {
            _panelPixel ??= CreatePixel();
            _moviePlayer.Draw(_spriteBatch, _panelPixel, GraphicsDevice.Viewport);
        }

        if (_cutsceneMenuOpen)
            DrawCutsceneMenu();

        if (_movieCard is { } card)
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel,
                new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                new Color(0, 0, 0, 235));
            float cardY = GraphicsDevice.Viewport.Height / 2f - card.Count * _fontRenderer.LineHeight;
            foreach (string line in card)
            {
                _fontRenderer.Draw(_spriteBatch, line,
                    new Vector2(GraphicsDevice.Viewport.Width / 2f - _fontRenderer.MeasureWidth(line) / 2f, cardY),
                    line == card[0] ? new Color(252, 252, 84) : new Color(0, 252, 0));
                cardY += _fontRenderer.LineHeight * 1.5f;
            }
            const string hint = "click or press any key to continue";
            _fontRenderer.Draw(_spriteBatch, hint,
                new Vector2(GraphicsDevice.Viewport.Width / 2f - _fontRenderer.MeasureWidth(hint) / 2f, cardY + _fontRenderer.LineHeight),
                new Color(140, 140, 140));
        }
```

Replace with:

```csharp
        if (_moviePlayer is not null)
        {
            // Stage: UI Scale Stage 6 -- no separable fallback exists (the movie always draws a
            // black backdrop + the current frame, or just black while _texture is still null),
            // so the whole block scopes as one unit.
            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());
            _panelPixel ??= CreatePixel();
            _moviePlayer.Draw(_spriteBatch, _panelPixel, VirtualViewport());
            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        }

        if (_cutsceneMenuOpen)
            DrawCutsceneMenu();

        if (_movieCard is { } card)
        {
            // Stage: UI Scale Stage 6 -- plain text-over-black card, no art, no hit-test beyond
            // button/key state (checked in Update()), so the whole block scopes as one unit.
            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());
            Rectangle vp = VirtualViewport();
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel,
                new Rectangle(0, 0, vp.Width, vp.Height),
                new Color(0, 0, 0, 235));
            float cardY = vp.Height / 2f - card.Count * _fontRenderer.LineHeight;
            foreach (string line in card)
            {
                _fontRenderer.Draw(_spriteBatch, line,
                    new Vector2(vp.Width / 2f - _fontRenderer.MeasureWidth(line) / 2f, cardY),
                    line == card[0] ? new Color(252, 252, 84) : new Color(0, 252, 0));
                cardY += _fontRenderer.LineHeight * 1.5f;
            }
            const string hint = "click or press any key to continue";
            _fontRenderer.Draw(_spriteBatch, hint,
                new Vector2(vp.Width / 2f - _fontRenderer.MeasureWidth(hint) / 2f, cardY + _fontRenderer.LineHeight),
                new Color(140, 140, 140));
            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        }
```

(`DrawCutsceneMenu()`'s own call site in between is untouched here — Task 2 converts its internal
body.)

- [ ] **Step 9: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 10: Scope `DrawCredits()`'s own internal viewport read and its call site**

Find, in `src/Hexwaste.Viewer/ViewerGame.Shell.cs` (`DrawCredits`, around lines 778-804):

```csharp
    private void DrawCredits()
    {
        EnsureCredits();
        if (_fontRenderer is null || _creditsLines is null)
            return;
        Viewport vp = GraphicsDevice.Viewport;
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);

        var section = new Color(252, 252, 84);
        var role = new Color(180, 156, 96);
        var name = new Color(0, 252, 0);
        int lh = _fontRenderer.LineHeight + 5;
        float y = vp.Height - _creditsScroll;
        foreach ((string text, char kind) in _creditsLines)
        {
            if (text.Length > 0 && y > -lh && y < vp.Height)
            {
                Color c = kind == '#' ? section : kind == '@' ? role : name;
                _fontRenderer.Draw(_spriteBatch, text,
                    new Vector2(vp.Width / 2f - _fontRenderer.MeasureWidth(text) / 2f, y), c);
            }
            y += lh;
        }
        if (y < 0) // fully scrolled past the top → loop
            _creditsScroll = 0;
    }
```

Replace with:

```csharp
    private void DrawCredits()
    {
        EnsureCredits();
        if (_fontRenderer is null || _creditsLines is null)
            return;
        Rectangle vp = VirtualViewport();
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);

        var section = new Color(252, 252, 84);
        var role = new Color(180, 156, 96);
        var name = new Color(0, 252, 0);
        int lh = _fontRenderer.LineHeight + 5;
        float y = vp.Height - _creditsScroll;
        foreach ((string text, char kind) in _creditsLines)
        {
            if (text.Length > 0 && y > -lh && y < vp.Height)
            {
                Color c = kind == '#' ? section : kind == '@' ? role : name;
                _fontRenderer.Draw(_spriteBatch, text,
                    new Vector2(vp.Width / 2f - _fontRenderer.MeasureWidth(text) / 2f, y), c);
            }
            y += lh;
        }
        if (y < 0) // fully scrolled past the top → loop
            _creditsScroll = 0;
    }
```

Find, in `src/Hexwaste.Viewer/ViewerGame.Hud.cs` (around lines 592-599):

```csharp
        else if (_menu == MenuState.Credits)
        {
            DrawCredits();
        }
        else if (_menu == MenuState.Endgame)
        {
            DrawEndgame();
        }
```

Replace with:

```csharp
        else if (_menu == MenuState.Credits)
        {
            // Stage: UI Scale Stage 6 -- DrawCredits/DrawEndgame each scope their own call site,
            // matching the main-menu-family block below.
            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());
            DrawCredits();
            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        }
        else if (_menu == MenuState.Endgame)
        {
            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());
            DrawEndgame();
            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        }
```

Also update the stale comment immediately below (around what is now further down, originally
lines 602-607) that documents this exact gap:

Find:

```csharp
            // Stage 2 (UI Scale): the main-menu family (Title/CharacterPick/CreateStats-Traits-
            // Tags — the only remaining MenuState values reachable here) draws into its own
            // scoped, scaled SpriteBatch block — same technique as DrawDialogPanel — so these
            // three screens match fo2ce's fullscreen stretch. Credits/Endgame above don't use
            // MenuOrigin() and stay unscaled; the plain-text fallback below (art missing) also
            // stays unscaled, matching its pre-existing, already-degraded presentation.
```

Replace with:

```csharp
            // Stage 2 (UI Scale): the main-menu family (Title/CharacterPick/CreateStats-Traits-
            // Tags — the only remaining MenuState values reachable here) draws into its own
            // scoped, scaled SpriteBatch block — same technique as DrawDialogPanel — so these
            // three screens match fo2ce's fullscreen stretch. Stage 6 gave Credits/Endgame their
            // own matching scoped blocks above; the plain-text fallback below (art missing) is
            // the one surface here that stays unscaled by design, matching its pre-existing,
            // already-degraded presentation.
```

- [ ] **Step 11: Convert `DrawEndgame`'s own internal viewport read**

Find, in `src/Hexwaste.Viewer/ViewerGame.Endgame.cs` (`DrawEndgame`, around lines 150-157):

```csharp
    private void DrawEndgame()
    {
        if (_endgameSlides is null || _endgameIndex >= _endgameSlides.Count)
            return;
        EndgameSlide slide = _endgameSlides[_endgameIndex];
        Viewport vp = GraphicsDevice.Viewport;
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
```

Replace with:

```csharp
    private void DrawEndgame()
    {
        if (_endgameSlides is null || _endgameIndex >= _endgameSlides.Count)
            return;
        EndgameSlide slide = _endgameSlides[_endgameIndex];
        Rectangle vp = VirtualViewport();
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
```

- [ ] **Step 12: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 13: Run the golden suites**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Run: `FALLOUT2_DIR=./game-data scripts/encounter-golden.sh check`
Expected: both report all scenarios passing, byte-identical — none of these six surfaces render
in a headless run without one of the specific `--menu death/credits/endgame`/`--cutscene-menu`
triggers, none of which any existing golden scenario drives.

- [ ] **Step 14: Manual visual check — death screen, credits, endgame at a non-4:3 window**

Run, for the death screen:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage6-death.png \
  -- --menu death
```
Read the resulting PNG. Expected: the death.frm backdrop (or, if the art is absent, the dark
overlay) and the "YOU HAVE DIED" text render centered at ~1.5× scale (for a 1280×720 window),
matching every other already-scaled screen.

Run, for credits:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage6-credits.png \
  -- --menu credits
```
Read the resulting PNG. Expected: the scrolling credits text renders centered and scaled to fill
the window, not pillarboxed at native 640×480 size.

Run, for the endgame slideshow:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage6-endgame.png \
  -- --menu endgame
```
Read the resulting PNG. Expected: the ending slide art (or its fallback) and subtitle render
scaled to fill the window.

- [ ] **Step 15: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Shell.cs src/Hexwaste.Viewer/ViewerGame.Endgame.cs \
        src/Hexwaste.Viewer/ViewerGame.Hud.cs src/Hexwaste.Viewer/MviePlayer.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the death screen, endgame, credits, and movie player

Deletes MenuOriginDevice() -- a documented but never-executed "later
stages delete this" TODO -- now that its 3 callers (DrawDeathArt,
DrawEndgame, DrawDeathNarration) switch to the already-scaled
MenuOrigin(). Scopes the death-screen block, the movie-player block,
the _movieCard block, and the DrawCredits()/DrawEndgame() call sites
each into their own scoped, scaled SpriteBatch block -- these six
surfaces live as independent sequential if-blocks inside one method
(DrawTextOverlay) rather than each having their own top-level Draw()
call site, so each block scopes individually using that method's own
already-scaled main-menu-family block as the template. MviePlayer.Draw
takes a Rectangle instead of a Viewport (matching WorldmapScreen's
convention) so its caller can pass VirtualViewport().

This is Task 1 of 2 for UI Scale Stage 6 (shell/endgame/cutscene
screens + the HUD text overlay). The cutscene browser and the
persistent gameplay HUD text (Task 2) still render unscaled.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

## Task 2: Cutscene browser + persistent HUD text overlay

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:7081-7085` (`CutsceneListLayout`), `:2096-2109`
  (the `Update()` cutscene hit-test)
- Modify: `src/Hexwaste.Viewer/ViewerGame.Hud.cs:505-524` (the hover-name tooltip and the AP/HP
  HUD line), `:684-713` (`DrawCutsceneMenu`)

**Interfaces:**
- Consumes: `UiScaleMatrix()`, `VirtualViewport()`, `UiMouse()` (already shipped), the `Point
  uiMouse` local already declared once in `Update()` (`ViewerGame.cs:1940` area) — reuse it, do
  not redeclare it.
- Produces: nothing new consumed by a later task — this is the last task in the whole UI Scale
  project.

- [ ] **Step 1: Convert `CutsceneListLayout`'s viewport read**

Find, in `src/Hexwaste.Viewer/ViewerGame.cs` (around lines 7079-7085):

```csharp
    /// <summary>Shared list geometry for the cutscene browser so Update hit-testing and the HUD
    /// draw agree: the Y of row 0 and the per-row height, centred in the viewport.</summary>
    private (float firstRowY, float rowH) CutsceneListLayout(int count)
    {
        float rowH = _fontRenderer.LineHeight * 1.35f;
        float firstRowY = GraphicsDevice.Viewport.Height / 2f - count * rowH / 2f;
        return (firstRowY, rowH);
    }
```

Replace with:

```csharp
    /// <summary>Shared list geometry for the cutscene browser so Update hit-testing and the HUD
    /// draw agree: the Y of row 0 and the per-row height, centred in the viewport.
    /// Stage: UI Scale Stage 6 -- reads VirtualViewport(), the same choke-point shape
    /// ChromeOrigin()/ItemWindowArt() already proved out for worldmap chrome/inventory.</summary>
    private (float firstRowY, float rowH) CutsceneListLayout(int count)
    {
        float rowH = _fontRenderer.LineHeight * 1.35f;
        float firstRowY = VirtualViewport().Height / 2f - count * rowH / 2f;
        return (firstRowY, rowH);
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Convert the `Update()` cutscene hit-test**

Find, in `src/Hexwaste.Viewer/ViewerGame.cs` (around lines 2095-2109):

```csharp
                // Mouse hover selects a row; a click on it plays it.
                (float firstRowY, float rowH) = CutsceneListLayout(names.Count);
                int cx = GraphicsDevice.Viewport.Width / 2;
                int hover = -1;
                if (Math.Abs(mouse.X - cx) < 220)
                {
                    int row = (int)((mouse.Y - firstRowY) / rowH);
                    if (row >= 0 && row < names.Count)
                        hover = row;
                }
                if (hover >= 0)
                    _cutsceneMenuIndex = hover;

                bool clickPlay = hover >= 0 && mouse.LeftButton == ButtonState.Pressed
                    && _previousMouse.LeftButton == ButtonState.Released;
```

Replace with:

```csharp
                // Mouse hover selects a row; a click on it plays it.
                (float firstRowY, float rowH) = CutsceneListLayout(names.Count);
                int cx = VirtualViewport().Width / 2;
                int hover = -1;
                if (Math.Abs(uiMouse.X - cx) < 220)
                {
                    int row = (int)((uiMouse.Y - firstRowY) / rowH);
                    if (row >= 0 && row < names.Count)
                        hover = row;
                }
                if (hover >= 0)
                    _cutsceneMenuIndex = hover;

                bool clickPlay = hover >= 0 && mouse.LeftButton == ButtonState.Pressed
                    && _previousMouse.LeftButton == ButtonState.Released;
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Scope `DrawCutsceneMenu()`'s whole body**

Find, in `src/Hexwaste.Viewer/ViewerGame.Hud.cs` (around lines 684-713):

```csharp
    private void DrawCutsceneMenu()
    {
        _panelPixel ??= CreatePixel();
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 235));

        List<string> names = CutsceneNames();
        (float firstRowY, float rowH) = CutsceneListLayout(names.Count);
        var green = new Color(0, 252, 0);
        var yellow = new Color(252, 252, 84);
        var dim = new Color(120, 120, 120);

        const string title = "CUTSCENE ARCHIVE";
        _fontRenderer.Draw(_spriteBatch, title,
            new Vector2(vw / 2f - _fontRenderer.MeasureWidth(title) / 2f, firstRowY - rowH * 2f), yellow);

        for (int i = 0; i < names.Count; i++)
        {
            string label = names[i].ToLowerInvariant();
            bool sel = i == _cutsceneMenuIndex;
            string text = sel ? $"> {label}" : label;
            _fontRenderer.Draw(_spriteBatch, text,
                new Vector2(vw / 2f - _fontRenderer.MeasureWidth(text) / 2f, firstRowY + i * rowH),
                sel ? yellow : green);
        }

        const string hint = "up/down or hover  -  enter/click to play  -  esc to close";
        _fontRenderer.Draw(_spriteBatch, hint,
            new Vector2(vw / 2f - _fontRenderer.MeasureWidth(hint) / 2f, firstRowY + names.Count * rowH + rowH), dim);
    }
```

Replace with:

```csharp
    private void DrawCutsceneMenu()
    {
        // Stage: UI Scale Stage 6 -- no separable fallback exists (this dark backdrop + text
        // list has no art at all), so the whole method scopes into its own scoped, scaled
        // SpriteBatch block.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        _panelPixel ??= CreatePixel();
        Rectangle vp = VirtualViewport();
        int vw = vp.Width, vh = vp.Height;
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 235));

        List<string> names = CutsceneNames();
        (float firstRowY, float rowH) = CutsceneListLayout(names.Count);
        var green = new Color(0, 252, 0);
        var yellow = new Color(252, 252, 84);
        var dim = new Color(120, 120, 120);

        const string title = "CUTSCENE ARCHIVE";
        _fontRenderer.Draw(_spriteBatch, title,
            new Vector2(vw / 2f - _fontRenderer.MeasureWidth(title) / 2f, firstRowY - rowH * 2f), yellow);

        for (int i = 0; i < names.Count; i++)
        {
            string label = names[i].ToLowerInvariant();
            bool sel = i == _cutsceneMenuIndex;
            string text = sel ? $"> {label}" : label;
            _fontRenderer.Draw(_spriteBatch, text,
                new Vector2(vw / 2f - _fontRenderer.MeasureWidth(text) / 2f, firstRowY + i * rowH),
                sel ? yellow : green);
        }

        const string hint = "up/down or hover  -  enter/click to play  -  esc to close";
        _fontRenderer.Draw(_spriteBatch, hint,
            new Vector2(vw / 2f - _fontRenderer.MeasureWidth(hint) / 2f, firstRowY + names.Count * rowH + rowH), dim);

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Scope the persistent HUD text overlay's two top blocks in `DrawTextOverlay()`**

Find, in `src/Hexwaste.Viewer/ViewerGame.Hud.cs` (around lines 505-524):

```csharp
        if (_hoveredObject is not null && _hoveredObject != _dude?.Dude)
        {
            MouseState mouse = Mouse.GetState();
            _fontRenderer.Draw(_spriteBatch, ObjectName(_hoveredObject),
                new Vector2(mouse.X + 14, mouse.Y + 6), green);
        }

        // AP/HP text HUD above the message log.
        if (_dude is not null && GetCritterState(_dude.Dude) is { } dudeStats)
        {
            string hud = $"HP {dudeStats.CurrentHp}/{dudeStats.MaxHp}  AP {_combat.DudeAp}/{dudeStats.MaxActionPoints}"
                + $"  L{_dudeLevel} XP {_dudeXp}";
            if (AimLocation != Formats.Combat.CriticalTables.LocationUncalled)
                hud += $"  |  aim: {AimName(AimLocation)} (V)";
            if (_combat.Phase != Formats.Combat.CombatPhase.Idle)
                hud += $"  |  round {_combat.Round}: "
                    + (_combat.Phase == Formats.Combat.CombatPhase.PlayerTurn ? "your turn (F attack, Space end turn)" : "enemy turn");
            int hudY = GraphicsDevice.Viewport.Height - _hudBarHeight - 8 - (Math.Min(_messageLog.Count, MessageLogFallbackLines) + 1) * _fontRenderer.LineHeight - 4;
            _fontRenderer.Draw(_spriteBatch, hud, new Vector2(8, hudY), new Color(252, 252, 84));
        }
```

Replace with:

```csharp
        // Stage: UI Scale Stage 6 -- these two blocks are unconditional (run every frame during
        // normal gameplay, not gated by any special-screen state), so they scope as one unit at
        // the very top of this method. _hudBarHeight stays a device-pixel quantity by convention
        // (see its own doc comment) -- hudBarVirtual is the same SkilldexOrigin-precedent
        // conversion (ViewerGame.Panels.cs) applied here.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        if (_hoveredObject is not null && _hoveredObject != _dude?.Dude)
        {
            Point tipMouse = UiMouse();
            _fontRenderer.Draw(_spriteBatch, ObjectName(_hoveredObject),
                new Vector2(tipMouse.X + 14, tipMouse.Y + 6), green);
        }

        // AP/HP text HUD above the message log.
        if (_dude is not null && GetCritterState(_dude.Dude) is { } dudeStats)
        {
            string hud = $"HP {dudeStats.CurrentHp}/{dudeStats.MaxHp}  AP {_combat.DudeAp}/{dudeStats.MaxActionPoints}"
                + $"  L{_dudeLevel} XP {_dudeXp}";
            if (AimLocation != Formats.Combat.CriticalTables.LocationUncalled)
                hud += $"  |  aim: {AimName(AimLocation)} (V)";
            if (_combat.Phase != Formats.Combat.CombatPhase.Idle)
                hud += $"  |  round {_combat.Round}: "
                    + (_combat.Phase == Formats.Combat.CombatPhase.PlayerTurn ? "your turn (F attack, Space end turn)" : "enemy turn");
            int hudBarVirtual = (int)(_hudBarHeight / UiScale());
            int hudY = VirtualViewport().Height - hudBarVirtual - 8 - (Math.Min(_messageLog.Count, MessageLogFallbackLines) + 1) * _fontRenderer.LineHeight - 4;
            _fontRenderer.Draw(_spriteBatch, hud, new Vector2(8, hudY), new Color(252, 252, 84));
        }

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
```

- [ ] **Step 8: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 9: Run the golden suites**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Run: `FALLOUT2_DIR=./game-data scripts/encounter-golden.sh check`
Expected: both report all scenarios passing, byte-identical — headless runs never call `Draw()`.

- [ ] **Step 10: Manual visual check — cutscene browser and gameplay HUD text at a non-4:3 window**

Run, for the cutscene browser:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage6-cutscene.png \
  -- --cutscene-menu
```
Read the resulting PNG. Expected: the "CUTSCENE ARCHIVE" title, the movie-name list, and the hint
line all render centered and scaled to fill the window (not native/small).

Run, for the persistent gameplay HUD text (use any normal in-game screenshot; `--goto` starts on
a loaded map with a dude present):
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage6-hud-text.png \
  -- --create 5,5,5,5,5,5,5:0,4,5:0
```
Read the resulting PNG. Expected: the "HP .../... AP .../..." readout renders at the same scale
as the HUD bar directly below it (both should look proportionate to each other and to the rest of
the scaled UI), sitting flush above the bar with no gap or overlap — the same check Stage 3a's
`SkilldexOrigin` and the HUD bar task originally used to prove the `_hudBarHeight` convention.

If either screenshot shows content at native/mismatched scale relative to the rest of the window,
do not commit — the `hudBarVirtual`/`VirtualViewport()` math needs revisiting first.

- [ ] **Step 11: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/ViewerGame.Hud.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the cutscene browser and the persistent gameplay HUD text

CutsceneListLayout() -- the shared choke point Update()'s hit-test and
DrawCutsceneMenu() both already used -- now reads VirtualViewport(),
propagating to both; Update()'s cx/mouse.X/Y hover math converts to
uiMouse. The persistent HP/AP/hover-name overlay (drawn every frame
during normal gameplay, not gated by any special-screen state) scopes
into its own block at the top of DrawTextOverlay(); its hudY formula
gets the same _hudBarHeight/UiScale() device-to-virtual conversion
already established by SkilldexOrigin.

This is Task 2 of 2 for UI Scale Stage 6 and completes UI Scale for
the entire Viewer -- every screen and overlay now scales uniformly to
fill non-4:3 windows, with zero remaining GraphicsDevice.Viewport or
bare Mouse.GetState() reads feeding rendering or hit-test math outside
the documented, deliberately-unscaled art-absent fallbacks.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```
