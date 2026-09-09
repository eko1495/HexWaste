# Menu backdrop un-stretch (main-menu icon/plate alignment) — design

## Problem

On the main menu the six red-glow button plates, their labels, and the click bands sit
to the right of the painted button slots in `mainmenu.frm`. At the default 1280x720
window the plates are 96 virtual px (144 screen px) off horizontally. Vertical is
exact only because height happens to be the limiting axis at 16:9.

Root cause: the 2026-09-08 "menu backdrop fill" change
(`docs/superpowers/specs/2026-09-08-menu-backdrop-fill-design.md`, `60e3124` and
its predecessors) stretches the 640x480 backdrop art non-uniformly to the full virtual
viewport in `DrawMenuBackdrop()` (`src/Hexwaste.Viewer/ViewerGame.Shell.cs:56-60`),
while every overlay drawn afterwards — plates, labels, portraits, steppers, click bands —
stays at `MenuOrigin()`'s centred 640x480 box (`Shell.cs:46-50`). The painted slots move
with the stretch; the overlays do not. Worked example at 1280x720: UI scale 1.5,
virtual canvas 853x480, `MenuOrigin().ox = 106`; the slot painted at art x=30 lands at
30 × 853/640 ≈ 40, the plate is drawn at 106 + 30 = 136.

The same defect exists on every screen sharing the helper: character pick
(`Shell.cs:270`), character creation (`Shell.cs:438`), and the death screen
(`Shell.cs:840`). The endgame slideshow (`ViewerGame.Endgame.cs:165`) stretches the
same way but has only a centred subtitle to misplace, so there it is a consistency
issue rather than a visible bug.

## Grounding (pinned base tree `alexbatalov e97087b`)

Vanilla never stretches this art. Each of these screens creates a fixed 640x480 window
centred on the screen and blits its FRM 1:1 into it; the rest of the screen is
`_colorTable[0]` black:

- Main menu — `src/mainmenu.cc:97-105` (`(screenGetWidth() - 640) / 2`, …),
  backdrop blit `:119`, buttons at window-local `(30, 19 + index*42 - index)` `:183-200`.
- Character selector — `src/character_selector.cc:264-266`.
- Endgame slideshow — `src/endgame.cc:574-581` (with a full-screen black overlay window
  behind it, `:569`).
- Character creation — `src/character_editor.cc:1369-1374`.
- Death screen — `src/main.cc:373-378`.

So the vanilla-faithful presentation is exactly what Hexwaste drew before 2026-09-08:
the 640x480 art at the centred origin, black elsewhere. Overlays then align by
construction because the art and the overlays share one origin.

## Decision

Revert the backdrop stretch for all five menu-family sites. The 2026-09-08 fill decision
is superseded; its stated benefit (no black bars at 16:9) is outweighed by moving the
painted UI slots away from the fixed overlays, and its motivating case
(fullscreen-by-default) was itself reverted on 2026-09-09
(`docs/superpowers/specs/2026-09-09-revert-fullscreen-default-design.md`).

The project returns to zero non-uniform stretch anywhere, restoring the principle stated
in `Hexwaste.Formats.Rendering.UiScale`'s doc comment.

Rejected alternatives, recorded for the next reader:

- **Stretch the overlays with the art** (one per-axis `(W/640, H/480)` transform for the
  whole menu family plus mouse mapping). Aligns, but distorts plates and glyphs 1.33x wide
  at 16:9 — the "stretched buttons" look the user reported.
- **Stretched positions, 1:1 sizes.** Plates land in slots that are painted wider than
  the plates; text sits in a distorted frame. No vanilla precedent.

## Changes

### 1. `DrawMenuBackdrop` (`src/Hexwaste.Viewer/ViewerGame.Shell.cs:52-60`)

Replace the body and doc comment:

```csharp
    /// <summary>Draws a menu-family backdrop the way vanilla does (mainmenu.cc:97-105,119,
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

The four call sites (`:98`, `:270`, `:438`, `:840`) are unchanged. The black fill returns
here because the art no longer covers the viewport; `_panelPixel`/`CreatePixel()` is the
existing 1x1 texture used by every other panel.

### 2. Endgame slides (`src/Hexwaste.Viewer/ViewerGame.Endgame.cs:165-166`)

Destination rectangle back to the centred box; the source rectangle line and the
existing black fill at `:157` stay exactly as they are:

```csharp
            _spriteBatch.Draw(tex, new Rectangle(ox, oy, 640, 480),
                new Rectangle(0, 0, srcW, tex.Height), Color.White);
```

`ox`/`oy` are already in scope (`:159`). The subtitle (`DrawEndgameSubtitle`) is
untouched — it was always `MenuOrigin()`-relative.

### 3. Doc comments

- `DrawAuthenticMainMenu` summary (`Shell.cs:86-90`): "stretched to fill the virtual
  viewport (see DrawMenuBackdrop)" → "centred 1:1 (see DrawMenuBackdrop)".
- `Hexwaste.Formats.Rendering.UiScale` class comment: no text change needed — it already
  states the no-non-uniform-stretch principle; verify nothing there references the
  backdrop exception (a `grep -n backdrop` on the file must return nothing).

### 4. Docs

- Prepend to `docs/superpowers/specs/2026-09-08-menu-backdrop-fill-design.md`, right
  under the title:

  > **Superseded 2026-09-09** by
  > `2026-09-09-menu-backdrop-unstretch-design.md`: stretching the art moved the painted
  > button slots away from the `MenuOrigin()`-anchored overlays (96 virtual px at
  > 1280x720). Vanilla centres the 640x480 window unstretched; Hexwaste does again.

- `docs/PHASE-HISTORY.md`: one MAINTENANCE paragraph in the existing format at the end of
  the file (after the 2026-09-08 scroll-clamp entry), naming the root cause, the worked
  1280x720 numbers, the vanilla citations, and this spec + plan.

## Scope

- `src/Hexwaste.Viewer/ViewerGame.Shell.cs`: `DrawMenuBackdrop` body + 2 doc comments.
- `src/Hexwaste.Viewer/ViewerGame.Endgame.cs`: 1 destination rectangle.
- `docs/superpowers/specs/2026-09-08-menu-backdrop-fill-design.md`: superseded note.
- `docs/PHASE-HISTORY.md`: maintenance entry.

## Non-goals

- `MenuOrigin()`, `MenuButtonRect`, `MenuButtonBandLocal`, and every overlay draw — they
  were already vanilla-correct; the art moves back to them.
- Uniform-scale "cover" fitting of the art (crop instead of bars): it would crop the
  top rows where the buttons live at 16:9 and has no vanilla precedent.
- The ~4 px button label vertical-centring divergence (`Shell.cs:120-122`), the
  "Laser/Loser" glyph residual, and the remaining font-census screens — tracked
  separately.
- Credits — has no backdrop; already positions on `VirtualViewport()`.
- Endgame's dead "wide panning texture" source-rect path — untouched.

## Testing

No automated test covers `ViewerGame`. Verify by screenshot at the default windowed
1280x720 and once in fullscreen:

1. `dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --menu --screenshot <png>`.
   Expected: the six plates sit inside the painted slots on the left column (plate
   left edge at window-local x=30 of the art, i.e. 160 + 30×1.5 = 205 screen px), black
   bars 160 screen px wide on each side, copyright bottom-left and version bottom-right
   inside the art's bottom band. Compare against the last pre-2026-09-08 main-menu
   capture if one is on disk; otherwise against the fo2ce main menu captured this week.
2. `--menu pick --screenshot` and `--menu create --screenshot` (the `--menu` probe accepts
   an optional `pick`/`create` state, `Program.cs:77-80`). Expected: portrait, stat block,
   and TAKE/CREATE/BACK plates centred on the art; black bars each side.
3. Live: launch windowed, NEW GAME → hover each of the six buttons — the menudown swap
   must happen exactly when the cursor is over the painted slot (this proves the click
   bands and the art agree again). Click TAKE on character pick to confirm the band
   still fires.
4. Alt+Enter to fullscreen (1920x1080): same alignment, bars 240 screen px each side.
5. `dotnet build` clean; `grep -n "stretched to fill" src/Hexwaste.Viewer/*.cs` returns
   nothing.
