# Main Menu Fonts Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The main menu's button labels and copyright/version text render with vanilla's actual fonts (`font4.aaf` and `font0.aaf`) instead of being substituted with the general-purpose `font1.aaf`, fixing the "stretched"/misaligned look reported when compared against fo2ce.

**Architecture:** Two more `AafFontRenderer` instances are loaded alongside the existing `font1.aaf` renderer, using the identical null-safe loading pattern. `DrawAuthenticMainMenu()`'s three draw calls (button labels, copyright, version) switch to the new renderers, each with a `??` fallback to the always-present `_fontRenderer` if the new font files are ever missing.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`src/Hexwaste.Viewer`). No new dependencies.

## Global Constraints

- Only the main menu's font selection changes — `MenuButtonRect`, `MenuOrigin`, and the button-label horizontal centering formula (`ox + 126 - width/2`) are already confirmed correct against vanilla and must not change (`docs/superpowers/specs/2026-09-09-main-menu-fonts-design.md`, Scope and Non-goals).
- No other screen's font usage changes — the broader multi-screen font census (Skilldex, Options, Preferences, dialogs, credits, load/save, inventory quantity window) is explicitly out of scope, tracked separately.
- `ViewerGame` has no unit test project (MonoGame dependency) — verify via build + manual screenshot comparison against the fo2ce reference already captured this session, not automated tests.

---

## File Structure

- Modify: `src/Hexwaste.Viewer/ViewerGame.cs` — 2 new nullable `AafFontRenderer?` fields, 2 new load calls next to the existing `font1.aaf` load.
- Modify: `src/Hexwaste.Viewer/ViewerGame.Shell.cs` — `DrawAuthenticMainMenu()`'s 3 draw calls and the button-label centering-height calculation.

Single cohesive change (2 new fields + their loading + 3 rewired draw calls across two files) — one task.

---

### Task 1: Load and wire the main menu's real fonts (font0.aaf, font4.aaf)

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs`
- Modify: `src/Hexwaste.Viewer/ViewerGame.Shell.cs`

**Interfaces:**
- Produces: `ViewerGame._menuCaptionFontRenderer` — private nullable `AafFontRenderer?` field (font0.aaf), consumed only by `DrawAuthenticMainMenu()` in `ViewerGame.Shell.cs`.
- Produces: `ViewerGame._menuButtonFontRenderer` — private nullable `AafFontRenderer?` field (font4.aaf), consumed only by `DrawAuthenticMainMenu()` in `ViewerGame.Shell.cs`.
- Consumes (existing, unchanged): `AafFontRenderer` class (`AafFontRenderer.cs`), `AafFont.Load(byte[]) -> AafFont` (`Hexwaste.Formats.Text`), `GameFileSystem.Exists(string) -> bool` / `ReadAllBytes(string) -> byte[]` (`_vfs`, existing field).

- [ ] **Step 1: Add the two new field declarations**

Find, in `src/Hexwaste.Viewer/ViewerGame.cs`:

```csharp
    /// <summary>Travel to this city.txt area index right after load (screenshot testing).</summary>
    public int? TravelToArea { get; set; }
    private AafFontRenderer? _fontRenderer;
```

(currently `ViewerGame.cs:594-596`). Replace it with:

```csharp
    /// <summary>Travel to this city.txt area index right after load (screenshot testing).</summary>
    public int? TravelToArea { get; set; }
    private AafFontRenderer? _fontRenderer;

    /// <summary>The main menu's distinct fonts (vanilla fontSetCurrent(100)/fontSetCurrent(104),
    /// mainmenu.cc:123,202) -- separate from _fontRenderer (font1.aaf), the general interface
    /// font used everywhere else. Loaded in LoadContent(); see DrawAuthenticMainMenu().</summary>
    private AafFontRenderer? _menuCaptionFontRenderer; // font0.aaf: copyright/version
    private AafFontRenderer? _menuButtonFontRenderer;  // font4.aaf: the six button labels
```

- [ ] **Step 2: Load the two new fonts**

Find:

```csharp
        // font1.aaf is the standard readable interface font.
        if (_vfs.Exists("font1.aaf"))
            _fontRenderer = new AafFontRenderer(GraphicsDevice, AafFont.Load(_vfs.ReadAllBytes("font1.aaf")));
        else
            Console.Error.WriteLine("font1.aaf not found — text overlay disabled");  // ascii-ok: stderr diagnostic, not font-rendered
```

(currently `ViewerGame.cs:1521-1525`). Replace it with:

```csharp
        // font1.aaf is the standard readable interface font.
        if (_vfs.Exists("font1.aaf"))
            _fontRenderer = new AafFontRenderer(GraphicsDevice, AafFont.Load(_vfs.ReadAllBytes("font1.aaf")));
        else
            Console.Error.WriteLine("font1.aaf not found — text overlay disabled");  // ascii-ok: stderr diagnostic, not font-rendered

        // font0.aaf / font4.aaf: the main menu's caption and button-label fonts
        // (vanilla fontSetCurrent(100)/fontSetCurrent(104), mainmenu.cc:123,202) --
        // distinct from font1.aaf, the general interface font used everywhere else.
        if (_vfs.Exists("font0.aaf"))
            _menuCaptionFontRenderer = new AafFontRenderer(GraphicsDevice, AafFont.Load(_vfs.ReadAllBytes("font0.aaf")));
        if (_vfs.Exists("font4.aaf"))
            _menuButtonFontRenderer = new AafFontRenderer(GraphicsDevice, AafFont.Load(_vfs.ReadAllBytes("font4.aaf")));
```

- [ ] **Step 3: Build to confirm no compile errors**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors (pre-existing nullable warnings, if any, are unrelated and fine).

- [ ] **Step 4: Rewire `DrawAuthenticMainMenu()`'s button-label draw**

Find, in `src/Hexwaste.Viewer/ViewerGame.Shell.cs`, inside `DrawAuthenticMainMenu()`:

```csharp
            string label = MiscMsg(MainMenuButtons[i].MsgId);
            Color c = enabled ? gold : dimGold;
            // Vertically centre the label on the button (the engine's font 104 is taller than ours, so we
            // centre rather than pin to its baked y=41*i+20 — a small presentation divergence).
            float ly = r.Y + (26 - _fontRenderer.LineHeight) / 2f;
            _fontRenderer.Draw(_spriteBatch, label,
                new Vector2(ox + 126 - _fontRenderer.MeasureWidth(label) / 2f, ly), c);
```

Replace it with:

```csharp
            string label = MiscMsg(MainMenuButtons[i].MsgId);
            Color c = enabled ? gold : dimGold;
            AafFontRenderer buttonFont = _menuButtonFontRenderer ?? _fontRenderer;
            // Vertically centre the label on the button (vanilla pins to its baked
            // y=41*i+20; centring instead is a small presentation divergence).
            float ly = r.Y + (26 - buttonFont.LineHeight) / 2f;
            buttonFont.Draw(_spriteBatch, label,
                new Vector2(ox + 126 - buttonFont.MeasureWidth(label) / 2f, ly), c);
```

- [ ] **Step 5: Rewire the copyright/version draw**

Find:

```csharp
        // Copyright (misc.msg {20}) bottom-left + version bottom-right (mainmenu.cc:141-155).
        _fontRenderer.Draw(_spriteBatch, MiscMsg(20), new Vector2(ox + 15, oy + 459), tan);
        _fontRenderer.Draw(_spriteBatch, MenuVersionString,
            new Vector2(ox + 615 - _fontRenderer.MeasureWidth(MenuVersionString), oy + 459), tan);
        return true;
```

Replace it with:

```csharp
        // Copyright (misc.msg {20}) bottom-left + version bottom-right (mainmenu.cc:141-155).
        AafFontRenderer captionFont = _menuCaptionFontRenderer ?? _fontRenderer;
        captionFont.Draw(_spriteBatch, MiscMsg(20), new Vector2(ox + 15, oy + 459), tan);
        captionFont.Draw(_spriteBatch, MenuVersionString,
            new Vector2(ox + 615 - captionFont.MeasureWidth(MenuVersionString), oy + 459), tan);
        return true;
```

- [ ] **Step 6: Build to confirm no compile errors**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 7: Manual verification**

Launch Hexwaste windowed (the default now) and screenshot the main menu:

```bash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data &
sleep 8
WIN=$(xdotool search --name "Hexwaste" | head -1)
xdotool windowactivate "$WIN"
sleep 0.5
spectacle -b -n -o /tmp/menu-fonts-check.png
```

Read `/tmp/menu-fonts-check.png` with the Read tool. Expected: the six button labels
("INTRO", "NEW GAME", "LOAD GAME", "OPTIONS", "CREDITS", "EXIT") render in a visibly
bolder/larger font than before this fix, and the copyright/version text at the bottom
also renders in a distinct font from the buttons.

Compare against the fo2ce reference screenshot already captured this session:

```bash
ls scratch/compare-runs/menu-alignment-*/
```

Read the fo2ce reference image found there (e.g. `menu-align-fo2ce.png` or similar —
check the directory listing for the exact filename) and the Hexwaste screenshot
side by side. Expected: the button label font weight/size proportions now visually
match fo2ce's, rather than looking thinner/smaller as before this fix.

Then do a click-through smoke check to confirm hit-testing is unaffected (this fix
only changes rendering, not `MenuButtonBandLocal`/`MenuButtonRect`, but confirm live
since visual position feeds expectations):

```bash
xdotool mousemove 490 316
sleep 0.2
xdotool mousemove 540 316
sleep 0.3
xdotool mousedown 1
sleep 0.2
xdotool mouseup 1
sleep 1.5
spectacle -b -n -o /tmp/menu-fonts-newgame-check.png
```

(Coordinates are for a 1280x720 window at NEW GAME's position, per this session's
established click pattern — if the window isn't at the expected screen position,
screenshot first and adjust.) Read `/tmp/menu-fonts-newgame-check.png` and confirm it
shows the character-selection screen, i.e. the NEW GAME button was still clickable.

Kill the running instance when done:

```bash
kill %1 2>/dev/null || true
pkill -9 -f "Hexwaste.Viewer/bin" 2>/dev/null || true
```

- [ ] **Step 8: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/ViewerGame.Shell.cs
git commit -m "$(cat <<'EOF'
fix(viewer): use vanilla's actual main menu fonts

Hexwaste loaded only font1.aaf and used it everywhere, including the
main menu -- but vanilla uses font0.aaf (fontSetCurrent(100)) for the
copyright/version text and font4.aaf (fontSetCurrent(104)) for the
six button labels, both distinct from the general interface font.
Both files are already present in game data; this loads and wires
them in, matching the reported "stretched"/misaligned look against a
real fo2ce comparison. The button-label centering formula itself was
already correct (vanilla also centers around a fixed x=126 point,
mainmenu.cc:215) -- this is purely a font-selection fix.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

## Self-Review

**Spec coverage:**
- 2 new fields + loading → Steps 1-2. ✅
- Button-label draw rewiring (font + centering height) → Step 4. ✅
- Copyright/version draw rewiring → Step 5. ✅
- `??` fallback to `_fontRenderer` at both sites → Steps 4-5 (`_menuButtonFontRenderer ?? _fontRenderer`, `_menuCaptionFontRenderer ?? _fontRenderer`). ✅
- Non-goals (`MenuButtonRect`/`MenuOrigin`/horizontal centering formula unchanged) → nothing in this plan touches them; Step 4's replacement keeps `ox + 126 - buttonFont.MeasureWidth(label) / 2f` structurally identical, only the font instance changes. ✅
- Testing section's manual verification (launch, screenshot, compare against fo2ce reference, click-through) → Step 7. ✅

**Placeholder scan:** No TBD/TODO. Step 7's fo2ce-reference-file lookup uses `ls` to
find the exact filename rather than guessing it — a grounded lookup, not a vague
placeholder.

**Type consistency:** `_menuCaptionFontRenderer`/`_menuButtonFontRenderer` are both
`AafFontRenderer?` throughout (declaration in Step 1, load in Step 2, `??`-narrowed
local `AafFontRenderer` in Steps 4-5) — consistent with the existing `_fontRenderer`
field's type. `buttonFont`/`captionFont` locals are both non-nullable
`AafFontRenderer` (post-`??`), matching how `_fontRenderer` was already used
directly (non-null-checked) in the original code at this point in the method (guarded
by the method's own early-return at the top: `if (_mainMenuBg is null || _fontRenderer
is null) return false;`).
