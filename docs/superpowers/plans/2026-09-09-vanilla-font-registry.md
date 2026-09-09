# Vanilla Font Registry + Skilldex/Options/Preferences Fonts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the two named main-menu font fields with a registry keyed by vanilla's `fontSetCurrent()` ids, then use it to give Skilldex, Options, and Preferences their correct vanilla fonts (font3/font4) instead of the general font1.

**Architecture:** `ViewerGame.cs` gains `Dictionary<int, AafFontRenderer> _fonts` plus a `Font(int id)` accessor that falls back to `_fontRenderer` (font1.aaf, untouched with its 240 existing call sites). Fonts 100/102/103/104 load in one loop and dispose in one loop. Task 1 does the refactor with behavior-identical main-menu swaps; Task 2 applies `Font(103)`/`Font(104)` at the seven Skilldex/Options/Preferences sites.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`src/Hexwaste.Viewer`). No new dependencies.

## Global Constraints

- Font id → file: `100..104 → font{id-100}.aaf` (`font_manager.cc:122,214`). font1.aaf (101) stays in `_fontRenderer` and is the fallback; it is NOT stored in the registry (spec §1).
- `Font(id)` requires `_fontRenderer` non-null; every draw method already guards `_fontRenderer is null` before drawing (spec §1).
- Skilldex `{N}%` values, Preferences value labels, and `DrawSkilldexTextFallback()` stay on `_fontRenderer` (vanilla 101 / FRM digits) (spec §3, §5).
- `OptionsRowRect` must use the same renderer as `DrawOptions` or rows and hit-testing desync (spec §4).
- Non-goals: the pause overlay `PAUSED` string, Preferences button colors, Skilldex `CANCEL`, `{122} Affect player speed`, other census screens, the main-menu icon/plate alignment bug, the 240 `_fontRenderer` sites.
- No automated test covers `ViewerGame` — verify by build + screenshots (`--menu --screenshot`, `--prefs`, live `S`/`Esc`).

---

## File Structure

- Modify: `src/Hexwaste.Viewer/ViewerGame.cs` — registry field + `Font()` accessor (replacing 2 fields at `:598-602`), load loop (replacing `:1533-1539`), dispose loop (replacing `:7433-7434`).
- Modify: `src/Hexwaste.Viewer/ViewerGame.Shell.cs` — 2 accessor swaps (`:119`, `:128`).
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs` — `DrawSkilldex` (`:695`, `:708-710`), `OptionsRowRect` (`:1208`), `DrawOptions` (`:1291-1292`).
- Modify: `src/Hexwaste.Viewer/ViewerGame.Preferences.cs` — new title draw, `:173-174`, `:198`.

---

### Task 1: Font registry + behavior-identical main-menu swaps

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:598-602, 1533-1539, 7433-7434`
- Modify: `src/Hexwaste.Viewer/ViewerGame.Shell.cs:119, 128`

**Interfaces:**
- Produces: `private AafFontRenderer Font(int id)` on `ViewerGame` — returns the registry renderer for vanilla font id `id`, else `_fontRenderer!`. Task 2 calls `Font(103)` and `Font(104)`.
- Produces: `private readonly Dictionary<int, AafFontRenderer> _fonts` (internal to `ViewerGame.cs`).
- Removes: `_menuCaptionFontRenderer`, `_menuButtonFontRenderer` (no other file references them — verified by grep in Step 6).
- Consumes (unchanged): `_fontRenderer` (font1.aaf), `AafFontRenderer`, `AafFont.Load`, `_vfs.Exists/ReadAllBytes`.

- [ ] **Step 1: Replace the two named fields with the registry + accessor**

In `src/Hexwaste.Viewer/ViewerGame.cs` find:

```csharp
    /// <summary>The main menu's distinct fonts (vanilla fontSetCurrent(100)/fontSetCurrent(104),
    /// mainmenu.cc:123,202) -- separate from _fontRenderer (font1.aaf), the general interface
    /// font used everywhere else. Loaded in LoadContent(); see DrawAuthenticMainMenu().</summary>
    private AafFontRenderer? _menuCaptionFontRenderer; // font0.aaf: copyright/version
    private AafFontRenderer? _menuButtonFontRenderer;  // font4.aaf: the six button labels
```

Replace with:

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

- [ ] **Step 2: Replace the two named loads with one loop**

Find:

```csharp
        // font0.aaf / font4.aaf: the main menu's caption and button-label fonts
        // (vanilla fontSetCurrent(100)/fontSetCurrent(104), mainmenu.cc:123,202) --
        // distinct from font1.aaf, the general interface font used everywhere else.
        if (_vfs.Exists("font0.aaf"))
            _menuCaptionFontRenderer = new AafFontRenderer(GraphicsDevice, AafFont.Load(_vfs.ReadAllBytes("font0.aaf")));
        if (_vfs.Exists("font4.aaf"))
            _menuButtonFontRenderer = new AafFontRenderer(GraphicsDevice, AafFont.Load(_vfs.ReadAllBytes("font4.aaf")));
```

Replace with:

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

- [ ] **Step 3: Replace the two named disposes with one loop**

Find:

```csharp
        _fontRenderer?.Dispose();
        _menuCaptionFontRenderer?.Dispose();
        _menuButtonFontRenderer?.Dispose();
        _frmCache.Dispose();
```

Replace with:

```csharp
        _fontRenderer?.Dispose();
        foreach (AafFontRenderer f in _fonts.Values)
            f.Dispose();
        _frmCache.Dispose();
```

- [ ] **Step 4: Swap the two main-menu sites (behavior-identical)**

In `src/Hexwaste.Viewer/ViewerGame.Shell.cs` find:

```csharp
            AafFontRenderer buttonFont = _menuButtonFontRenderer ?? _fontRenderer;
```

Replace with:

```csharp
            AafFontRenderer buttonFont = Font(104);
```

Find:

```csharp
        AafFontRenderer captionFont = _menuCaptionFontRenderer ?? _fontRenderer;
```

Replace with:

```csharp
        AafFontRenderer captionFont = Font(100);
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors (pre-existing nullable warnings are fine).

- [ ] **Step 6: Verify the old names are gone and the main menu is pixel-identical**

```bash
grep -rn "_menuCaptionFontRenderer\|_menuButtonFontRenderer" src/ && echo "STALE REFERENCES FOUND" || echo "clean"
```

Expected: `clean`.

Capture the main menu before/after (the "before" is the previous commit):

```bash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --menu --screenshot /tmp/reg-menu-after.png
git stash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --menu --screenshot /tmp/reg-menu-before.png
git stash pop
cmp /tmp/reg-menu-before.png /tmp/reg-menu-after.png && echo IDENTICAL
```

Expected: `IDENTICAL`. (If `cmp` differs only because of a blinking cursor or a wander-timer critter in the background, Read both images and confirm the button labels and copyright line render identically; the `--menu` screenshot is a static shell screen so it should be byte-identical.) Then `git stash list` must be empty.

- [ ] **Step 7: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/ViewerGame.Shell.cs
git commit -m "$(cat <<'EOF'
refactor(viewer): registry of vanilla fonts keyed by fontSetCurrent id

Replaces the two named main-menu font fields with a
Dictionary<int, AafFontRenderer> keyed by vanilla's fontSetCurrent()
ids (100..104 -> font{N}.aaf) and a Font(id) accessor that falls back
to font1.aaf. Loads fonts 100/102/103/104 in one loop and disposes
them in one loop, so the forgotten-Dispose slip from d0b31f8 cannot
recur per font. The two main-menu sites become Font(104)/Font(100),
behavior-identical (--menu screenshot byte-identical). Groundwork for
the Skilldex/Options/Preferences font fixes that follow.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

### Task 2: Skilldex, Options, Preferences fonts

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs:695, 708-710, 1208, 1291-1292`
- Modify: `src/Hexwaste.Viewer/ViewerGame.Preferences.cs` (title draw after the backdrop; `:173-174`; `:198`)

**Interfaces:**
- Consumes (from Task 1): `AafFontRenderer Font(int id)` — non-null after the caller's `_fontRenderer is null` guard.
- Consumes (unchanged): `PrefMsg(int) -> string` (reads `options.msg`), `_fontRenderer`.

- [ ] **Step 1: Skilldex title and skill-name labels → font 103**

In `src/Hexwaste.Viewer/ViewerGame.Panels.cs`, inside `DrawSkilldex()`, find:

```csharp
        _spriteBatch.Draw(_skilldexBox, new Vector2(o.X, o.Y), Color.White);
        _fontRenderer.Draw(_spriteBatch, "SKILLDEX", new Vector2(o.X + 55, o.Y + 14), titleColor);
```

Replace with:

```csharp
        _spriteBatch.Draw(_skilldexBox, new Vector2(o.X, o.Y), Color.White);
        // skilldex.cc:257 fontSetCurrent(103): the title and the 8 skill-name labels. The
        // {N}% values below stay on _fontRenderer -- vanilla blits FRM digit strips there.
        AafFontRenderer font = Font(103);
        font.Draw(_spriteBatch, "SKILLDEX", new Vector2(o.X + 55, o.Y + 14), titleColor);
```

Find:

```csharp
            string name = SkillName(skill);
            int nameX = Math.Max(0, (btnW - _fontRenderer.MeasureWidth(name)) / 2);
            int nameY = Math.Max(0, (btnH - _fontRenderer.LineHeight) / 2);
            _fontRenderer.Draw(_spriteBatch, name, new Vector2(btnPos.X + nameX, btnPos.Y + nameY), nameColor);
```

Replace with:

```csharp
            string name = SkillName(skill);
            int nameX = Math.Max(0, (btnW - font.MeasureWidth(name)) / 2);
            int nameY = Math.Max(0, (btnH - font.LineHeight) / 2);
            font.Draw(_spriteBatch, name, new Vector2(btnPos.X + nameX, btnPos.Y + nameY), nameColor);
```

Leave the `{N}%` value draw (`:718-720`) and `DrawSkilldexTextFallback()` on `_fontRenderer`.

- [ ] **Step 2: Options row pitch and labels → font 103**

Find, in `OptionsRowRect`:

```csharp
        int lh = (_fontRenderer?.LineHeight ?? 16) + 10;
```

Replace with:

```csharp
        // options.cc:254 fontSetCurrent(103). Same renderer as DrawOptions, or rows and
        // hit-testing desync; the ?? 16 keeps the headless (no font1.aaf) default.
        int lh = (_fontRenderer is null ? 16 : Font(103).LineHeight) + 10;
```

Find, in `DrawOptions()`:

```csharp
            Rectangle r = OptionsRowRect(i);
            int tw = _fontRenderer.MeasureWidth(OptionsItems[i]);
            _fontRenderer.Draw(_spriteBatch, OptionsItems[i], new Vector2(px + (ow - tw) / 2, r.Y + 2), i == hovered ? hot : green);
```

Replace with:

```csharp
            Rectangle r = OptionsRowRect(i);
            AafFontRenderer font = Font(103); // options.cc:254 fontSetCurrent(103)
            int tw = font.MeasureWidth(OptionsItems[i]);
            font.Draw(_spriteBatch, OptionsItems[i], new Vector2(px + (ow - tw) / 2, r.Y + 2), i == hovered ? hot : green);
```

- [ ] **Step 3: Preferences title (new), section titles, buttons**

In `src/Hexwaste.Viewer/ViewerGame.Preferences.cs`, inside `DrawPreferences()`, find:

```csharp
        var value = new Color(0, 108, 0); // _colorTable[18979] — the baked dark-green label ink
        var knob = new Color(0, 252, 0);
        Texture2D? knobArt = InterfaceFrm(PrefKnobOffFrm);
```

Replace with:

```csharp
        var value = new Color(0, 108, 0); // _colorTable[18979] — the baked dark-green label ink
        var knob = new Color(0, 252, 0);
        Texture2D? knobArt = InterfaceFrm(PrefKnobOffFrm);

        // Title (options.msg {100}, preferences.cc:1016-1019 -- fontSetCurrent(104) at (74,10)).
        Font(104).Draw(_spriteBatch, PrefMsg(100), new Vector2(o.X + 74, o.Y + 10), value);
        // preferences.cc:1021 fontSetCurrent(103): the 19 section titles + DEFAULT/DONE/CANCEL.
        // Value labels stay on _fontRenderer (preferences.cc:1053, font 101).
        AafFontRenderer titleFont = Font(103);
```

Find:

```csharp
            int titleX = slot.TitleCentered ? slot.TitleX - _fontRenderer.MeasureWidth(title) / 2 : slot.TitleX;
            _fontRenderer.Draw(_spriteBatch, title, new Vector2(o.X + titleX, o.Y + slot.TitleY), value);
```

Replace with:

```csharp
            int titleX = slot.TitleCentered ? slot.TitleX - titleFont.MeasureWidth(title) / 2 : slot.TitleX;
            titleFont.Draw(_spriteBatch, title, new Vector2(o.X + titleX, o.Y + slot.TitleY), value);
```

Find:

```csharp
        void Btn(int lx, int msg) => _fontRenderer.Draw(_spriteBatch, PrefMsg(msg), new Vector2(o.X + lx, o.Y + 449), gold);
```

Replace with:

```csharp
        void Btn(int lx, int msg) => titleFont.Draw(_spriteBatch, PrefMsg(msg), new Vector2(o.X + lx, o.Y + 449), gold);
```

Leave the value-label draw (`_fontRenderer.Draw(_spriteBatch, label, ...)`, `:186-187`) unchanged.

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 5: Verify Preferences via the probe**

```bash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --prefs --screenshot /tmp/reg-prefs.png
```

Read `/tmp/reg-prefs.png`. Expected: a `GAME PREFERENCES` title now appears at the top-left of the window in the large bold font4 (it was absent before); the section titles (GAME DIFFICULTY, COMBAT DIFFICULTY, …) and DEFAULT/DONE/CANCEL are in the bolder font3; the value labels (Normal, Brief, On/Off, …) are unchanged. Crop/upscale a title region if needed:

```bash
convert /tmp/reg-prefs.png -crop 640x120+0+0 -resize 1920x360 /tmp/reg-prefs-zoom.png
```

- [ ] **Step 6: Verify Skilldex and Options live**

No CLI probe opens these; use a live windowed run. Launch, click through NEW GAME → TAKE (the small-double-move-then-click pattern from `docs/fo2ce-comparison-playbook.md`), then:

```bash
WIN=$(xdotool search --name "Hexwaste" | head -1); xdotool windowactivate "$WIN"; sleep 0.3
xdotool key s; sleep 1; spectacle -b -n -o /tmp/reg-skilldex.png
xdotool key Escape; sleep 0.5
xdotool key Escape; sleep 1; spectacle -b -n -o /tmp/reg-options.png
```

Read both. Expected — Skilldex: `SKILLDEX` and the 8 skill names in the bolder font3, the `%` values unchanged. Options: all rows in font3, evenly spaced, and (move the mouse over a row, screenshot) the hovered row highlights — proving `OptionsRowRect` and the draw still agree. If the menu clicks don't land, screenshot first and re-locate the buttons (known flake).

```bash
pkill -9 -f "Hexwaste.Viewer/bin" 2>/dev/null || true
```

- [ ] **Step 7: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Panels.cs src/Hexwaste.Viewer/ViewerGame.Preferences.cs
git commit -m "$(cat <<'EOF'
fix(viewer): vanilla fonts for Skilldex, Options, and Preferences

Skilldex title + 8 skill-name labels, all Options rows (and the row
pitch OptionsRowRect uses for hit-testing), Preferences section
titles + DEFAULT/DONE/CANCEL -> font3.aaf (fontSetCurrent(103):
skilldex.cc:257, options.cc:254, preferences.cc:1021). Adds the
Preferences title "GAME PREFERENCES" (options.msg {100}) at (74,10)
in font4.aaf (preferences.cc:1016) -- vanilla draws it as text and
Hexwaste never drew it. Skilldex % values and Preferences value
labels stay on font1 (vanilla FRM digits / font 101).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

## Self-Review

**Spec coverage:** §1 registry/accessor/load/dispose → T1 Steps 1-3. §2 main-menu swaps → T1 Step 4. §3 Skilldex → T2 Step 1. §4 Options incl. `OptionsRowRect` → T2 Step 2. §5 Preferences title + titles + buttons → T2 Step 3. Testing §1-5 → T1 Step 6, T2 Steps 5-6. Non-goals: no step touches them.

**Placeholder scan:** none. Every code step shows full replacement text; every verify step has commands and expected output.

**Type consistency:** `Font(int) -> AafFontRenderer` (non-null) defined in T1 Step 1; T2 uses it as `AafFontRenderer font = Font(103)` / `titleFont` / `Font(104).Draw` — matches. `_fonts` is `Dictionary<int, AafFontRenderer>` in Step 1, iterated as `AafFontRenderer f in _fonts.Values` in Step 3 — matches. `OptionsRowRect`'s ternary keeps `int lh`.
