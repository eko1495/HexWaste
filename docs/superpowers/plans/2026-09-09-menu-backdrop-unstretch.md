# Menu Backdrop Un-stretch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put the menu-family backdrop art back at its centred, unstretched 640x480 position so the button plates, labels, and click bands land on the painted slots again.

**Architecture:** `DrawMenuBackdrop()` in `ViewerGame.Shell.cs` is the single helper the main menu, character pick, character creation, and death screen use; its body reverts to black-fill-then-1:1-draw at `MenuOrigin()`. The endgame slideshow has its own draw and gets the same destination rectangle. No overlay code changes — the overlays were already at `MenuOrigin()`.

**Tech Stack:** C# / .NET 10, MonoGame DesktopGL. No automated test covers `ViewerGame`; verification is by CLI screenshot probes and one live run.

Spec: `docs/superpowers/specs/2026-09-09-menu-backdrop-unstretch-design.md`.

## Global Constraints

- Vanilla reference is the pinned `alexbatalov e97087b` tree: `mainmenu.cc:97-118`, `character_selector.cc:264-266`, `endgame.cc:574-581` centre a 640x480 window unstretched, black (`_colorTable[0]`) elsewhere. Cite these in the code comment.
- The backdrop is drawn at exactly `new Rectangle(ox, oy, 640, 480)` with `(ox, oy) = MenuOrigin()`. No stretch, no scale, no crop.
- `MenuOrigin()`, `MenuButtonRect`, `MenuButtonBandLocal`, and every overlay draw/mouse handler are NOT modified.
- The Endgame source-rectangle line (`new Rectangle(0, 0, srcW, tex.Height)`) and its black fill at `ViewerGame.Endgame.cs:157` stay exactly as they are.
- Commits end with:
  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
  ```
- Screenshots go under `/home/eko/.claude/jobs/28039163/tmp/` (not `/tmp`). Kill any Hexwaste instance you launch (`pkill -x Hexwaste.Viewer`; `pgrep -x`, never `pgrep -f`, to check).

---

### Task 1: Revert the backdrop stretch (code + docs)

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Shell.cs:52-60` (`DrawMenuBackdrop`) and `:86-87` (doc comment)
- Modify: `src/Hexwaste.Viewer/ViewerGame.Endgame.cs:165-166`
- Modify: `docs/superpowers/specs/2026-09-08-menu-backdrop-fill-design.md:1-2`
- Modify: `docs/PHASE-HISTORY.md` (append at end of file)

**Interfaces:**
- Consumes: existing `MenuOrigin()` → `(int ox, int oy)`, `VirtualViewport()` → `Rectangle`, `_panelPixel` / `CreatePixel()` (1x1 white texture used by every panel), all in `ViewerGame`.
- Produces: nothing new; `DrawMenuBackdrop(Texture2D bg)` keeps its signature.

- [ ] **Step 1: Replace `DrawMenuBackdrop`**

In `src/Hexwaste.Viewer/ViewerGame.Shell.cs`, replace this exact block:

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

with:

```csharp
    /// <summary>Draws a menu-family backdrop the way vanilla does (mainmenu.cc:97-118,
    /// character_selector.cc:264-266): the 640x480 art 1:1 at MenuOrigin()'s centred box,
    /// black-filled elsewhere. Never stretched — the plates/labels/click bands drawn afterwards
    /// share MenuOrigin(), so art and overlays stay aligned only if the art is not moved.</summary>
    private void DrawMenuBackdrop(Texture2D bg)
    {
        Rectangle vp = VirtualViewport();
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
        (int ox, int oy) = MenuOrigin();
        _spriteBatch.Draw(bg, new Rectangle(ox, oy, 640, 480), Color.White);
    }
```

- [ ] **Step 2: Fix the `DrawAuthenticMainMenu` doc comment**

In the same file, replace these two lines:

```csharp
    /// <summary>Draw the authentic FO2 main menu: mainmenu.frm (FID 140, 640x480) stretched to fill
    /// the virtual viewport (see DrawMenuBackdrop), the six red-glow menuup/menudown buttons (FID
```

with:

```csharp
    /// <summary>Draw the authentic FO2 main menu: mainmenu.frm (FID 140, 640x480) centred 1:1
    /// (see DrawMenuBackdrop), the six red-glow menuup/menudown buttons (FID
```

- [ ] **Step 3: Endgame destination rectangle**

In `src/Hexwaste.Viewer/ViewerGame.Endgame.cs`, replace:

```csharp
            _spriteBatch.Draw(tex, new Rectangle(0, 0, vp.Width, vp.Height),
                new Rectangle(0, 0, srcW, tex.Height), Color.White);
```

with:

```csharp
            _spriteBatch.Draw(tex, new Rectangle(ox, oy, 640, 480),
                new Rectangle(0, 0, srcW, tex.Height), Color.White);
```

`ox`/`oy` are already declared a few lines above (`(int ox, int oy) = MenuOrigin();`). Do not touch the `vp` declaration or the black fill above it; `vp` is still used by the black fill.

- [ ] **Step 4: Build and grep**

Run:
```bash
dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj 2>&1 | tail -3
grep -n "stretched to fill" src/Hexwaste.Viewer/*.cs; echo "grep exit=$?"
grep -n -i backdrop src/Hexwaste.Formats/Rendering/UiScale.cs; echo "grep exit=$?"
```
Expected: 0 errors; both greps print nothing and `grep exit=1`.

- [ ] **Step 5: Screenshot probes at 1280x720**

Run (each writes a PNG and exits on its own):
```bash
T=/home/eko/.claude/jobs/28039163/tmp
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --menu --screenshot $T/unstretch-menu.png
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --menu pick --screenshot $T/unstretch-pick.png
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --menu create --screenshot $T/unstretch-create.png
```
Read each PNG (the Read tool renders images). Expected on `unstretch-menu.png`: a 640x480 art block centred with black bars on both sides; six red-glow plates sitting inside the painted slots of the left column (not floating to the right of them); copyright bottom-left and version bottom-right inside the art's bottom band. Expected on pick/create: portrait/stat block/TAKE-CREATE-BACK plates (pick) and the stat steppers (create) sitting on their painted art, black bars each side. Record what you saw in the report — "matches" is not enough; name the elements you checked.

- [ ] **Step 6: Live hover check**

Launch windowed in the background and drive it with xdotool:
```bash
T=/home/eko/.claude/jobs/28039163/tmp
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data > $T/live.log 2>&1 &
sleep 8
WIN=$(DISPLAY=:0 xdotool search --name "Hexwaste" | head -1)
eval $(DISPLAY=:0 xdotool getwindowgeometry --shell "$WIN")   # sets X, Y, WIDTH, HEIGHT
# Plate 0 (INTRO) centre: art-local (43, 32) → screen = (X + 160 + 43*1.5, Y + 32*1.5)
DISPLAY=:0 xdotool mousemove $((X + 160 + 64)) $((Y + 48)); sleep 0.3
DISPLAY=:0 xdotool mousemove $((X + 160 + 65)) $((Y + 48)); sleep 0.8
spectacle -b -n -o $T/unstretch-hover0.png
# Plate 1 (NEW GAME) centre: art-local (43, 73) → +41 rows of art
DISPLAY=:0 xdotool mousemove $((X + 160 + 65)) $((Y + 110)); sleep 0.8
spectacle -b -n -o $T/unstretch-hover1.png
# Fullscreen aspect check (Alt+Enter toggles; 1920x1080 → scale 2.25, bars 240 px each side)
DISPLAY=:0 xdotool key alt+Return; sleep 2
spectacle -b -n -o $T/unstretch-fullscreen.png
DISPLAY=:0 xdotool key alt+Return; sleep 1
pkill -x Hexwaste.Viewer; sleep 1; pgrep -x Hexwaste.Viewer >/dev/null && echo "STILL RUNNING" || echo killed
```

Read `unstretch-fullscreen.png` too. Expected: same centred art with plates on the slots, black bars ~240 px each side.
Read both PNGs. Expected: the cursor is over the painted slot AND the plate under it shows the pressed/hover art swap (menudown) for that row only, proving the click bands and the painted slots agree. If the window's Y offset makes the maths land off a plate, adjust by the geometry printed and re-take; note what you did. Window geometry from `getwindowgeometry --shell` is the client area on this KWin session; if the shot shows the cursor above the art, add the title-bar height it reports.

- [ ] **Step 7: Superseded note on the old spec**

In `docs/superpowers/specs/2026-09-08-menu-backdrop-fill-design.md`, replace the first two lines:

```markdown
# Menu backdrop fill — design

```

with:

```markdown
# Menu backdrop fill — design

> **Superseded 2026-09-09** by `2026-09-09-menu-backdrop-unstretch-design.md`: stretching
> the art moved the painted button slots away from the `MenuOrigin()`-anchored overlays
> (96 virtual px at 1280x720). Vanilla centres the 640x480 window unstretched; Hexwaste
> does again.

```

- [ ] **Step 8: PHASE-HISTORY maintenance entry**

Append to the very end of `docs/PHASE-HISTORY.md` (after the existing scroll-clamp MAINTENANCE paragraph, preceded by one blank line):

```markdown

MAINTENANCE (2026-09-09, "menu backdrop un-stretch" — the main-menu icon/plate misalignment): the 2026-09-08
"menu backdrop fill" change stretched mainmenu.frm (and the pick/create/death/endgame backdrops) non-uniformly
to the full virtual viewport in `DrawMenuBackdrop()`, while the plates, labels, and click bands stayed at
`MenuOrigin()`'s centred 640x480 box. The painted button slots moved with the stretch; the overlays did not.
At the default 1280x720 window (UI scale 1.5, virtual 853x480, ox=106) the slot painted at art x=30 lands
at virtual x≈40 while the plate is drawn at 136 — 96 virtual px (144 screen px) off. Vanilla never stretches
this art: `mainmenu.cc:97-118`, `character_selector.cc:264-266`, `endgame.cc:574-581` each centre a fixed
640x480 window and blit the FRM 1:1, black elsewhere. Fix = `DrawMenuBackdrop()` back to black-fill + 1:1 draw
at `MenuOrigin()` (`ViewerGame.Shell.cs`), same destination rect for the endgame slide
(`ViewerGame.Endgame.cs`); overlays untouched. The project is back to zero non-uniform stretch anywhere.
Verified with `--menu`/`--menu pick`/`--menu create` screenshots and a live hover pass (menudown swap fires
exactly over the painted slot). Spec/plan: `docs/superpowers/specs/2026-09-09-menu-backdrop-unstretch-design.md`,
`docs/superpowers/plans/2026-09-09-menu-backdrop-unstretch.md`. The 2026-09-08 spec carries a superseded note.
```

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Shell.cs src/Hexwaste.Viewer/ViewerGame.Endgame.cs \
        docs/superpowers/specs/2026-09-08-menu-backdrop-fill-design.md docs/PHASE-HISTORY.md
git commit -m "fix(viewer): centre menu backdrops 1:1 so plates land on the painted slots

Reverts the 2026-09-08 non-uniform backdrop stretch. mainmenu.cc:97-118,
character_selector.cc:264-266 and endgame.cc:574-581 centre an unstretched
640x480 window; the plates/labels/click bands were already at that origin.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr"
```

Confirm `git status --short` is clean afterwards.
