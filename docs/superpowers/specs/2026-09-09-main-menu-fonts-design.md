# Main menu fonts fix — design

## Problem

Reported: the windowed main menu's buttons "look stretched" and controls aren't
aligned inside the buttons, and the copyright caption looks misaligned compared to
fo2ce. Compared directly against fo2ce side by side.

The button-label centering math itself is correct — vanilla's `mainmenu.cc:215`
centers each label around a fixed `x=126` point too
(`126 - (len / 2)`, matching Hexwaste's ported
`ox + 126 - _fontRenderer.MeasureWidth(label) / 2f`), so longer labels like
"NEW GAME" legitimately extending further both left and right than "INTRO" around
that same center point is expected in both engines, not a bug.

## Root cause

Hexwaste loads exactly one font (`font1.aaf`, `ViewerGame.cs:1521-1523`) and uses it
for all text everywhere via a single `_fontRenderer` field. Vanilla uses *different*
font IDs for different UI elements, set via `fontSetCurrent(id)` before each draw
call. For the main menu specifically (`reference/fallout2-ce/src/mainmenu.cc`, pinned
base tree):

- `fontSetCurrent(100)` (`mainmenu.cc:123`) before drawing the copyright/version text.
- `fontSetCurrent(104)` (`mainmenu.cc:202`) before drawing the six button labels.

Font IDs map to files via `font_manager.cc`'s `interfaceFontSetCurrentImpl` (`font -=
100` then `font{index}.aaf`): 100 → `font0.aaf`, 104 → `font4.aaf`. Both confirmed
present in this project's `game-data/master.dat` (loose-file check via a throwaway
diagnostic against `GameFileSystem`) — Hexwaste already has the correct assets, it
just never loads or uses them for these two font IDs.

`DrawAuthenticMainMenu()`'s own existing doc comment (`ViewerGame.Shell.cs`, near the
label-centering code) already flags this indirectly: *"the engine's font 104 is
taller than ours, so we centre rather than pin to its baked y=41*i+20 — a small
presentation divergence."* That's the visible symptom: font1.aaf's shorter line
height, substituted for font4.aaf's real height, produces text that's proportioned
differently against the button plate than vanilla's actual bold menu font — which is
what reads as "stretched"/"not aligned."

## Fix

Load two more fonts alongside the existing `font1.aaf`, same pattern, in
`ViewerGame.cs` right after the existing load (`ViewerGame.cs:1521-1525`):

```csharp
private AafFontRenderer? _menuCaptionFontRenderer;  // font0.aaf — vanilla fontSetCurrent(100)
private AafFontRenderer? _menuButtonFontRenderer;   // font4.aaf — vanilla fontSetCurrent(104)
```

```csharp
// font0.aaf / font4.aaf: the main menu's caption and button-label fonts
// (vanilla fontSetCurrent(100)/fontSetCurrent(104), mainmenu.cc:123,202) —
// distinct from font1.aaf, the general interface font used everywhere else.
if (_vfs.Exists("font0.aaf"))
    _menuCaptionFontRenderer = new AafFontRenderer(GraphicsDevice, AafFont.Load(_vfs.ReadAllBytes("font0.aaf")));
if (_vfs.Exists("font4.aaf"))
    _menuButtonFontRenderer = new AafFontRenderer(GraphicsDevice, AafFont.Load(_vfs.ReadAllBytes("font4.aaf")));
```

Rewire `DrawAuthenticMainMenu()` (`ViewerGame.Shell.cs:91-131`) to use them, with a
`??` fallback to the always-present `_fontRenderer` in case either loose file is ever
missing (matching this file's existing defensive style — never throws, just degrades):

- Button-label vertical centering (currently `(26 - _fontRenderer.LineHeight) / 2f`)
  and the `Draw` call both switch to `(_menuButtonFontRenderer ?? _fontRenderer)`.
- Copyright and version `Draw` calls both switch to
  `(_menuCaptionFontRenderer ?? _fontRenderer)`.

This also removes the existing "font 104 is taller than ours" divergence — once we're
actually using font4.aaf, the centering is against its real height, not an
approximation.

## Scope

- `src/Hexwaste.Viewer/ViewerGame.cs`: 2 new nullable fields, 2 new load calls (same
  null-safe pattern as the existing `font1.aaf` load).
- `src/Hexwaste.Viewer/ViewerGame.Shell.cs`: `DrawAuthenticMainMenu()`'s 3 draw calls
  (button labels, copyright, version) plus the centering-height calculation.

Nothing else changes. Button *positions* (`MenuButtonRect`, `MenuOrigin`, the
centered-around-x=126 formula) are already confirmed correct and untouched.

## Non-goals

- The broader multi-screen font census (Skilldex, Options/pause, Preferences,
  message/confirmation dialog boxes, credits, load/save titles, inventory
  quantity/move-items sub-window — all confirmed via the same grounding pass to also
  use a font other than font1.aaf in vanilla) is explicitly out of scope for this fix,
  tracked as a separate follow-up.
- No change to any screen confirmed already correct (HUD, automap, combat log,
  Pip-Boy, worldmap, dialog body text, endgame, character selector, inventory item
  list/summary — all vanilla font 101/font1.aaf, matching Hexwaste's current single
  font already).
- No change to `MenuButtonRect`, `MenuOrigin`, or the button-label horizontal
  centering formula — all already correct.

## Testing

No automated test project covers `ViewerGame` (MonoGame dependency, same situation as
every fix this session). Verify manually:

1. Launch Hexwaste windowed (`dotnet run --project src/Hexwaste.Viewer --
   --game-dir game-data`), screenshot the main menu.
2. Compare button label rendering against the fo2ce reference already captured this
   session (`scratch/compare-runs/menu-alignment-*/`) — confirm the labels now use a
   visibly bolder/larger font matching fo2ce's proportions, not font1.aaf's smaller
   interface font.
3. Confirm the copyright/version line at the bottom also switches to the distinct
   caption font, matching fo2ce's proportions there too.
4. Confirm button click bands (`MenuButtonBandLocal`) still work correctly — this fix
   doesn't touch hit-testing, but worth a live click-through smoke check since label
   rendering position feeds visual expectations even if the underlying rects are
   unchanged.
