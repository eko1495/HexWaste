# Vanilla font registry + Skilldex/Options/Preferences fonts — design

## Problem

Hexwaste loads one font (`font1.aaf`) into a single `_fontRenderer` and uses it for
nearly all text. Vanilla Fallout 2 selects a font per UI element with
`fontSetCurrent(id)`; ids 100–104 map to `font0.aaf`–`font4.aaf`
(`reference/fallout2-ce/src/font_manager.cc:54-56,122,214`, pinned base tree). A census
earlier today found several screens using the wrong font. The main menu was fixed
first (`d21c7b6`) by adding two named fields, `_menuCaptionFontRenderer` (font0) and
`_menuButtonFontRenderer` (font4). This follow-up covers the next three screens the
user picked — Skilldex, Options, Preferences — and, now that four fonts are in play,
replaces the per-font fields with a registry keyed by vanilla's own ids, as the last
final review recommended.

## Grounding (pinned base tree, exact)

- **Skilldex** (`src/skilldex.cc`): `fontSetCurrent(103)` at `:257`; the title
  `SKILLDEX` (`:260-265`), all 8 skill-name button labels (`:316-339`), and `CANCEL`
  (`:362-367`) are all drawn under **103**. The 8 skill *values* are not font text —
  they are `BIG_NUMBERS` FRM digit-strip blits (`:268-313`).
- **Options window** (`src/options.cc`, `optionsWindowInit`): `fontSetCurrent(103)` at
  `:254`; all 5 button labels (`:259-291`) are **103**. The `fontSetCurrent(104)` at
  `:411` belongs to `showPause()` (`:327-494`, the `PAUSED` overlay) — a separate
  window Hexwaste does not implement.
- **Preferences** (`src/preferences.cc`): `fontSetCurrent(104)` at `:1016` → title
  `options.msg {100}` "GAME PREFERENCES" at (74,10) (`:1018-1019`);
  `fontSetCurrent(103)` at `:1021` → the 19 section titles (`:1024-1038`) and
  DEFAULT/DONE/CANCEL at y=449 (`:1041-1050`); `fontSetCurrent(101)` at `:1053` →
  every per-setting value label (`_UpdateThing`, `:609/:616/:641/:767`).

Files confirmed present in this project's game data: `font0.aaf`–`font4.aaf`
(`font5.aaf` does not exist).

## Design

### 1. Font registry (`src/Hexwaste.Viewer/ViewerGame.cs`)

Remove the two named fields (`_menuCaptionFontRenderer`, `_menuButtonFontRenderer`,
`ViewerGame.cs:598-602`) and replace them with:

```csharp
    /// <summary>Vanilla's interface fonts keyed by their fontSetCurrent() id (100..104 →
    /// font{id-100}.aaf, font_manager.cc:122,214). font1.aaf (id 101) is NOT stored here —
    /// it stays in _fontRenderer, the fallback every other id degrades to when its file is
    /// missing. Loaded in LoadContent(), disposed in UnloadContent().</summary>
    private readonly Dictionary<int, AafFontRenderer> _fonts = new();

    /// <summary>The renderer for vanilla's fontSetCurrent(<paramref name="id"/>); falls back
    /// to font1.aaf when that id's file is missing. Callers must already have guarded
    /// `_fontRenderer is null` (every draw method does), so the fallback is non-null here.</summary>
    private AafFontRenderer Font(int id) => _fonts.TryGetValue(id, out var f) ? f : _fontRenderer!;
```

Replace the two named loads (`ViewerGame.cs:1533-1539`) with one loop, kept right after
the existing `font1.aaf` load (which is unchanged, including its stderr diagnostic):

```csharp
        // Vanilla's other interface fonts, keyed by fontSetCurrent() id (font_manager.cc:
        // id-100 → font{N}.aaf). font1.aaf (101) is _fontRenderer itself, the fallback.
        foreach (int id in new[] { 100, 102, 103, 104 })
        {
            string file = $"font{id - 100}.aaf";
            if (_vfs.Exists(file))
                _fonts[id] = new AafFontRenderer(GraphicsDevice, AafFont.Load(_vfs.ReadAllBytes(file)));
        }
```

Replace the two named disposes (`ViewerGame.cs:7433-7434`) with:

```csharp
        foreach (AafFontRenderer f in _fonts.Values)
            f.Dispose();
```

`_fontRenderer` and its 240 existing call sites are untouched. font2.aaf (id 102) is
loaded for uniformity with vanilla's fixed 0..4 set — nothing uses it yet (the
character editor's name field will, per the census); the cost is one small atlas.

### 2. Main menu (`src/Hexwaste.Viewer/ViewerGame.Shell.cs`) — no behavior change

`Shell.cs:119`: `_menuButtonFontRenderer ?? _fontRenderer` → `Font(104)`.
`Shell.cs:128`: `_menuCaptionFontRenderer ?? _fontRenderer` → `Font(100)`.
The `??` moves into `Font()`, so these are behavior-identical.

### 3. Skilldex (`src/Hexwaste.Viewer/ViewerGame.Panels.cs`, `DrawSkilldex()`)

At the top of the art path (after the existing `_fontRenderer is null` guard and the
`SkilldexOrigin(...)` call), take `AafFontRenderer font = Font(103);`, then:

- `:695` title `"SKILLDEX"` → `font.Draw(...)`.
- `:708-710` the 8 skill-name labels: `MeasureWidth`, `LineHeight`, and `Draw` all
  through `font` (same renderer for measure and draw, so centering matches).
- `:718-720` the `{N}%` values stay on `_fontRenderer` — vanilla blits FRM digits here,
  a pre-existing documented divergence, not a font-id question.
- `DrawSkilldexTextFallback()` (art-missing path) stays on `_fontRenderer`.

### 4. Options (`src/Hexwaste.Viewer/ViewerGame.Panels.cs`)

- `OptionsRowRect(int index)` `:1208`: `(_fontRenderer?.LineHeight ?? 16) + 10` →
  `(_fontRenderer is null ? 16 : Font(103).LineHeight) + 10`. This helper is used for
  hit-testing as well as drawing, so it must use the same renderer as the draw or rows
  and clicks desync. (`Font()` requires `_fontRenderer` non-null, hence the explicit
  guard preserving the existing `?? 16` headless default.)
- `DrawOptions()` `:1291-1292`: `MeasureWidth` and `Draw` through `Font(103)`.

### 5. Preferences (`src/Hexwaste.Viewer/ViewerGame.Preferences.cs`, `DrawPreferences()`)

- **Add the missing title.** Vanilla draws it as text; Hexwaste currently draws nothing.
  After the backdrop draw and before the settings loop:

  ```csharp
        // Title (options.msg {100}, preferences.cc:1016-1019 -- font 104 at (74,10)).
        Font(104).Draw(_spriteBatch, PrefMsg(100), new Vector2(o.X + 74, o.Y + 10), value);
  ```

  `PrefMsg` already reads `options.msg` (`Preferences.cs:209-212`); `value` is the
  existing `(0,108,0)` = `_colorTable[18979]` that vanilla uses for this window.
- `:173-174` the 19 section titles: `MeasureWidth` and `Draw` through `Font(103)`
  (col1 titles are centered on x=99, so measuring with the same renderer matters).
- `:198` `Btn(...)` DEFAULT/DONE/CANCEL: `Font(103).Draw(...)`.
- `:186-187` value labels stay on `_fontRenderer` (vanilla 101).

## Scope

- `ViewerGame.cs`: registry field + accessor, load loop, dispose loop (replacing the two
  named fields and their load/dispose lines).
- `ViewerGame.Shell.cs`: 2 accessor swaps.
- `ViewerGame.Panels.cs`: `DrawSkilldex` (2 sites), `OptionsRowRect` (1), `DrawOptions` (1).
- `ViewerGame.Preferences.cs`: 1 new title draw, 2 rewired sites.

## Non-goals

- The pause overlay's `PAUSED` string (font 104) — Hexwaste has no pause overlay.
- Preferences colors: vanilla draws everything here, including DEFAULT/DONE/CANCEL, in
  `_colorTable[18979]`; Hexwaste draws the buttons in gold. A separate pre-existing
  divergence — flagged, not changed.
- Skilldex `CANCEL` label and Preferences `{122} Affect player speed` — neither is drawn
  by Hexwaste today; adding content is out of scope for a font fix.
- The remaining census screens (message/confirmation dialog boxes, credits, load/save
  titles, inventory quantity window, character editor) — the registry makes each a
  one-line `Font(id)` change later.
- The open main-menu icon/plate alignment bug (backdrop stretch vs. fixed-box overlays)
  — tracked separately.
- The 240 `_fontRenderer` call sites that are already correct (vanilla 101).

## Testing

No automated test covers `ViewerGame` (MonoGame dependency, as throughout this
session). Verify with the deterministic CLI probes, which reach each screen without
menu navigation:

1. `--show-skills` is the character sheet, not Skilldex — check `Program.cs` for a
   Skilldex probe (`grep -n '"--skilldex\|"--show-skilldex' src/Hexwaste.Viewer/Program.cs`);
   if none exists, open it live (`S` key in gameplay) and screenshot. Expected: `SKILLDEX`
   and the 8 skill names in the bolder font3, the `%` values unchanged.
2. `--prefs` opens Preferences (per this project's playbook). Expected: a new
   `GAME PREFERENCES` title at the top-left in the large font4; section titles and
   DEFAULT/DONE/CANCEL in font3; value labels unchanged.
3. Options: open live (`Esc` in gameplay), screenshot. Expected: all rows in font3, and
   hovering/clicking a row still highlights/activates the right one (the `OptionsRowRect`
   pitch change is what keeps hit-testing aligned).
4. `--menu --screenshot` for the main menu. Expected: pixel-identical to before this
   change (the two `Font()` swaps are behavior-preserving).
5. `dotnet build` clean; a `grep` that `_menuCaptionFontRenderer`/`_menuButtonFontRenderer`
   no longer appear anywhere.
