# Menu Backdrop Fill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every menu-family screen's backdrop art (title, character pick, character creation, death screen, endgame slides) stretches to fill the full virtual viewport instead of pillarboxing a fixed 640x480 centered image with black bars on wide windows.

**Architecture:** A new `DrawMenuBackdrop(Texture2D bg)` helper in `ViewerGame.Shell.cs` draws a backdrop texture stretched to `(0, 0, vp.Width, vp.Height)`, replacing the black-fill-then-centered-draw pair at 4 call sites. The 5th site (Endgame slides) has its own pre-existing source-rectangle logic and gets a narrower, inline destination-rectangle-only change. Every element drawn after a backdrop (buttons, labels, portraits, steppers, subtitles) keeps using `MenuOrigin()`'s centered `(ox, oy)` unchanged.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`src/Hexwaste.Viewer`). No new dependencies.

## Global Constraints

- Only backdrop art stretches (non-uniformly, if needed) — every button/label/portrait/stepper/subtitle keeps its exact current `MenuOrigin()`-relative position and size, undistorted (`docs/superpowers/specs/2026-09-08-menu-backdrop-fill-design.md`, Decision section).
- `MenuOrigin()` itself does not change.
- Endgame's existing source-rectangle logic (the dead "wide panning texture" path) is untouched — only its destination rectangle widens.
- Credits is not touched — it has no backdrop texture and is already correct.
- No other already-migrated screen (HUD, dialog, worldmap, inventory, etc.) is touched.
- `ViewerGame` has no unit test project (MonoGame dependency, same situation as the recent scroll-border-clamp and fullscreen-by-default work) — verify via build + manual screenshots, not automated tests.

---

## File Structure

- Modify: `src/Hexwaste.Viewer/ViewerGame.Shell.cs` — add `DrawMenuBackdrop(Texture2D bg)` near `MenuOrigin()`; apply it at the Title, Character Pick, Character Creation, and Death backdrop draw sites.
- Modify: `src/Hexwaste.Viewer/ViewerGame.Endgame.cs` — widen the Endgame slide backdrop's destination rectangle only.

Single cohesive change (one new helper + 5 call-site edits across two files) — one task.

---

### Task 1: Stretch menu-family backdrops to fill the viewport

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Shell.cs`
- Modify: `src/Hexwaste.Viewer/ViewerGame.Endgame.cs`

**Interfaces:**
- Produces: `ViewerGame.DrawMenuBackdrop(Texture2D bg) -> void` — private, called from the 4 simple backdrop-draw sites in `ViewerGame.Shell.cs`.
- Consumes (existing, unchanged): `ViewerGame.MenuOrigin() -> (int ox, int oy)` (`ViewerGame.Shell.cs:46-50`), `ViewerGame.VirtualViewport() -> Rectangle` (`ViewerGame.UiScale.cs:30-35`).

- [ ] **Step 1: Add the `DrawMenuBackdrop` helper**

In `src/Hexwaste.Viewer/ViewerGame.Shell.cs`, find `MenuOrigin()`:

```csharp
    private (int ox, int oy) MenuOrigin()
    {
        Rectangle vp = VirtualViewport();
        return ((vp.Width - 640) / 2, (vp.Height - 480) / 2);
    }
```

(currently `ViewerGame.Shell.cs:46-50`). Immediately after it, insert:

```csharp

    /// <summary>Draws a menu-family backdrop stretched to fill the full virtual viewport —
    /// matching fo2ce's own non-uniform stretch, but scoped to this decorative art only.
    /// Buttons/labels/portraits drawn afterward keep using MenuOrigin()'s centered,
    /// undistorted position — only the backdrop itself is stretched.</summary>
    private void DrawMenuBackdrop(Texture2D bg)
    {
        Rectangle vp = VirtualViewport();
        _spriteBatch.Draw(bg, new Rectangle(0, 0, vp.Width, vp.Height), Color.White);
    }
```

- [ ] **Step 2: Apply it at the Title (main menu) backdrop**

Find, in `DrawAuthenticMainMenu()`:

```csharp
        Rectangle vp = VirtualViewport();
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
        (int ox, int oy) = MenuOrigin();
        _spriteBatch.Draw(_mainMenuBg, new Rectangle(ox, oy, 640, 480), Color.White);
```

(currently `ViewerGame.Shell.cs:86-90`, inside the method starting at line 80). Replace it with:

```csharp
        (int ox, int oy) = MenuOrigin();
        DrawMenuBackdrop(_mainMenuBg);
```

- [ ] **Step 3: Apply it at the Character Pick backdrop**

Find, in `DrawAuthenticSelector()`:

```csharp
        Rectangle vp = VirtualViewport();
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
        (int ox, int oy) = MenuOrigin();
        _spriteBatch.Draw(_pickCharBg, new Rectangle(ox, oy, 640, 480), Color.White);
```

(currently `ViewerGame.Shell.cs:259-263`, inside the method starting at line 253). Replace it with:

```csharp
        (int ox, int oy) = MenuOrigin();
        DrawMenuBackdrop(_pickCharBg);
```

- [ ] **Step 4: Apply it at the Character Creation backdrop**

Find, in `DrawAuthenticCreation()`:

```csharp
        Rectangle vp = VirtualViewport();
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
        (int ox, int oy) = MenuOrigin();
        _spriteBatch.Draw(_createBg, new Rectangle(ox, oy, 640, 480), Color.White);
```

(currently `ViewerGame.Shell.cs:430-434`, inside the method starting at line 424). Replace it with:

```csharp
        (int ox, int oy) = MenuOrigin();
        DrawMenuBackdrop(_createBg);
```

- [ ] **Step 5: Apply it at the Death screen backdrop**

Find (the method has no name shown in this snippet, but the pattern is identical to the other 3):

```csharp
        Rectangle vp = VirtualViewport();
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
        (int ox, int oy) = MenuOrigin();
        _spriteBatch.Draw(_deathBg, new Rectangle(ox, oy, 640, 480), Color.White);
        return true;
```

(currently `ViewerGame.Shell.cs:836-841`). Replace it with:

```csharp
        (int ox, int oy) = MenuOrigin();
        DrawMenuBackdrop(_deathBg);
        return true;
```

- [ ] **Step 6: Build to confirm no compile errors**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors (pre-existing nullable warnings, if any, are unrelated and fine).

- [ ] **Step 7: Widen the Endgame slide backdrop's destination rectangle**

Find, in `DrawEndgame()`:

```csharp
    private void DrawEndgame()
    {
        if (_endgameSlides is null || _endgameIndex >= _endgameSlides.Count)
            return;
        EndgameSlide slide = _endgameSlides[_endgameIndex];
        Rectangle vp = VirtualViewport();
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);

        (int ox, int oy) = MenuOrigin();
        if (GetEndgameTexture(slide.FrmPath) is { } tex)
        {
            // Art 327 (DP.FRM) is the wide panning-desert scene, referenced only by commented endgame.txt
            // rows → dead in vanilla; we blit its left 640 px statically (a full pan is a deferred layer).
            int srcW = slide.Panning ? Math.Min(tex.Width, 640) : Math.Min(tex.Width, 640);
            _spriteBatch.Draw(tex, new Rectangle(ox, oy, 640, 480),
                new Rectangle(0, 0, srcW, tex.Height), Color.White);
        }
```

(currently `ViewerGame.Endgame.cs:150-167`). Replace only the final `_spriteBatch.Draw(tex, ...)` call — leave the black fill, the `srcW` computation, and everything else in this snippet exactly as-is:

```csharp
    private void DrawEndgame()
    {
        if (_endgameSlides is null || _endgameIndex >= _endgameSlides.Count)
            return;
        EndgameSlide slide = _endgameSlides[_endgameIndex];
        Rectangle vp = VirtualViewport();
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);

        (int ox, int oy) = MenuOrigin();
        if (GetEndgameTexture(slide.FrmPath) is { } tex)
        {
            // Art 327 (DP.FRM) is the wide panning-desert scene, referenced only by commented endgame.txt
            // rows → dead in vanilla; we blit its left 640 px statically (a full pan is a deferred layer).
            int srcW = slide.Panning ? Math.Min(tex.Width, 640) : Math.Min(tex.Width, 640);
            _spriteBatch.Draw(tex, new Rectangle(0, 0, vp.Width, vp.Height),
                new Rectangle(0, 0, srcW, tex.Height), Color.White);
        }
```

Note: `vp` and `ox`/`oy` are already computed above this point in the method and remain in scope — `ox`/`oy` are still used by `DrawEndgameSubtitle(slide.Subtitles[_endgameSubLine], ox, oy)` a few lines below (outside this snippet), which is unaffected by this change and needs no edit.

- [ ] **Step 8: Build to confirm no compile errors**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 9: Manual verification**

Launch Hexwaste fullscreen and drive it through the screenshot workflow already
established this session (`xdotool` + `spectacle`, per
`docs/fo2ce-comparison-playbook.md`'s conventions — prefer the documented keyboard
mnemonics over mouse clicks at the main menu, per that playbook's own 2026-09-08 gotcha
entry about unreliable menu clicks).

```bash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data &
sleep 6
spectacle -b -n -o /tmp/menu-backdrop-title.png
```

Read `/tmp/menu-backdrop-title.png` with the Read tool. Expected: the main menu
backdrop (blue gradient + power-armor helmet art) fills edge-to-edge with no black bars
on either side; the six buttons, their labels, and the copyright/version text remain in
the same centered position and size as before this fix (compare against any earlier
1280x720 or fullscreen main-menu screenshot from this session — button positions
relative to the window center should be visually unchanged, only the backdrop differs).

Then navigate to the character-pick screen using the keyboard mnemonic (`n` for NEW
GAME):

```bash
WIN=$(xdotool search --name "Hexwaste" | head -1)
xdotool windowactivate "$WIN"
xdotool key n
sleep 1
spectacle -b -n -o /tmp/menu-backdrop-charpick.png
```

Read `/tmp/menu-backdrop-charpick.png`. Expected: this screen's backdrop also fills
edge-to-edge; the character portrait, stat block, and TAKE/CREATE/BACK buttons stay
centered. Click TAKE (use the small-step `move`-then-`click` pattern from
`docs/fo2ce-comparison-playbook.md` if a direct `xdotool key` mnemonic isn't available
for this button, or just move on if `t` works as a mnemonic) to confirm the click bands
are still correctly positioned — this exercises that `MenuOrigin()`-relative coordinates
were unaffected by the backdrop-only change.

Kill the running instance when done:

```bash
kill %1 2>/dev/null || true
```

If reaching the character-creation or death screens is straightforward without
extensive setup, screenshot those too for the same edge-to-edge check; if not, the
Title and Character Pick checks above are sufficient evidence that the shared
`DrawMenuBackdrop` helper works correctly, since Character Creation and Death use the
exact same helper call.

- [ ] **Step 10: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Shell.cs src/Hexwaste.Viewer/ViewerGame.Endgame.cs
git commit -m "$(cat <<'EOF'
fix(viewer): stretch menu backdrops to fill the viewport

The main menu, character pick, character creation, death screen, and
endgame slides all drew their 640x480 backdrop art centered in a
black letterbox -- barely visible at 4:3-ish windows but a clear
~240px black bar per side at 1920x1080 fullscreen. Stretches the
backdrop art itself (only) to fill the viewport, matching fo2ce's own
non-uniform stretch for this decorative art; every button/label/
portrait/stepper/subtitle keeps its existing MenuOrigin()-centered,
undistorted position.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

## Self-Review

**Spec coverage:**
- `DrawMenuBackdrop` helper → Step 1. ✅
- Title, Character Pick, Character Creation, Death call sites → Steps 2-5. ✅
- Endgame's destination-rectangle-only change, source-rect logic untouched → Step 7. ✅
- Credits and `MenuOrigin()` unchanged → nothing in this plan touches either. ✅
- Testing section's manual verification → Step 9. ✅

**Placeholder scan:** No TBD/TODO. Step 9's death/character-creation coverage has an
explicit fallback ("if reaching ... is straightforward ... if not, the Title and
Character Pick checks above are sufficient") rather than a vague instruction — this is
a deliberate, justified scope reduction (same shared helper, same guarantee), not a
placeholder.

**Type consistency:** `DrawMenuBackdrop(Texture2D bg) -> void` used identically at all
4 call sites in Steps 2-5. `_mainMenuBg`/`_pickCharBg`/`_createBg`/`_deathBg` are all
`Texture2D?` fields (per their declarations already in the codebase); each call site is
already guarded by an early-return null check before reaching the backdrop draw, so
passing them as the non-nullable `Texture2D bg` parameter is safe without an additional
null-forgiving operator or check inside the helper.
