# Fullscreen By Default Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A plain interactive launch of Hexwaste opens borderless-fullscreen at the desktop's native resolution, matching vanilla Fallout 2/fo2ce's default presentation, while every CLI-driven screenshot/test/benchmark path keeps its existing fixed windowed size unaffected.

**Architecture:** `ViewerGame` gains a `StartFullscreen` property applied in `Initialize()` before `base.Initialize()`, a `ToggleFullscreen()` method wired to an Alt+Enter check early in `Update()`, and windowed-size memory tracked through the existing `ClientSizeChanged` handler. `Program.cs` gains a `--windowed` opt-out flag and wires `StartFullscreen` off the same `interactiveLaunch` condition that already gates the main menu.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`src/Hexwaste.Viewer`). No new dependencies.

## Global Constraints

- Scope: only `interactiveLaunch` (not `forceMenu`) gates the fullscreen default — `--menu` is a dev/testing flag per its own comment at `Program.cs:74` ("force the front door (menu screenshots/testing)"), not real play (`docs/superpowers/specs/2026-09-08-fullscreen-by-default-design.md`, Scope section).
- Borderless fullscreen only (`HardwareModeSwitch = false`), native desktop resolution via `GraphicsAdapter.DefaultAdapter.CurrentDisplayMode`. No exclusive-fullscreen mode.
- No new Preferences-screen entry, no per-monitor selection — both explicitly out of scope per the spec's Non-goals section.
- Every CLI-driven screenshot/test/benchmark invocation must keep its existing fixed windowed behavior, unchanged by this work.
- `ViewerGame` has no unit test project (MonoGame dependency, same situation as the recent scroll-border-clamp work) — verify via build + manual runs, not automated tests.

---

## File Structure

- Modify: `src/Hexwaste.Viewer/ViewerGame.cs` — add `StartFullscreen` property, `_windowedWidth`/`_windowedHeight` fields, fullscreen setup in `Initialize()`, `ToggleFullscreen()` method, Alt+Enter check in `Update()`, and update the `ClientSizeChanged` handler in the constructor.
- Modify: `src/Hexwaste.Viewer/Program.cs` — add `--windowed` flag parsing and wire `StartFullscreen` at the existing `StartInMenu` assignment site.

Single cohesive change, one task.

---

### Task 1: Fullscreen by default with windowed opt-out and Alt+Enter toggle

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs`
- Modify: `src/Hexwaste.Viewer/Program.cs`

**Interfaces:**
- Produces: `ViewerGame.StartFullscreen` — public `bool` settable property (object-initializer pattern, same as `StartInMenu`/`StartInWalkMode`), consumed by `Program.cs`'s `new ViewerGame(...) { ... }` initializer.
- Produces: `ViewerGame.ToggleFullscreen()` — private `void` method, called only from the Alt+Enter check inside `Update()`.
- Consumes (from existing code, unchanged): `ViewerGame.IsKeyPressed(KeyboardState, Keys) -> bool` (`ViewerGame.cs:3030`), `_graphics` (the `GraphicsDeviceManager` field set up in the constructor), `_camera.SetWindowSize(int, int)` (already called from the constructor's `ClientSizeChanged` handler).

- [ ] **Step 1: Add `StartFullscreen` property to `ViewerGame`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find:

```csharp
    /// <summary>Starts with critters in the walk cycle (screenshot testing of the T toggle).</summary>
    public bool StartInWalkMode { get; set; }
```

(currently `ViewerGame.cs:35-36`). Immediately after it, insert:

```csharp

    /// <summary>Borderless fullscreen at the desktop's native resolution for a plain
    /// interactive launch, matching vanilla Fallout 2/fo2ce's default presentation.
    /// Set from Program.cs off the same interactiveLaunch condition that gates
    /// StartInMenu. See Initialize() and ToggleFullscreen().</summary>
    public bool StartFullscreen { get; set; }
```

- [ ] **Step 2: Add windowed-size memory fields and fullscreen setup in `Initialize()`**

Find the constructor's `GraphicsDeviceManager` setup and `ClientSizeChanged` handler:

```csharp
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += (_, _) =>
            _camera.SetWindowSize(Window.ClientBounds.Width, Window.ClientBounds.Height);
    }
```

(currently `ViewerGame.cs:1117-1126`). Replace it with:

```csharp
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += (_, _) =>
        {
            if (!_graphics.IsFullScreen)
            {
                _windowedWidth = Window.ClientBounds.Width;
                _windowedHeight = Window.ClientBounds.Height;
            }
            _camera.SetWindowSize(Window.ClientBounds.Width, Window.ClientBounds.Height);
        };
    }
```

Then find the private field declarations near the top of the class — the same block Step 1
touched — and add the two size-memory fields right after the `StartFullscreen` property
you just added:

```csharp
    public bool StartFullscreen { get; set; }

    private int _windowedWidth = 1280;
    private int _windowedHeight = 720;
```

Now find `Initialize()`:

```csharp
    protected override void Initialize()
    {
        // The simulation is wall-time driven (palette cycling, soon animations),
        // so rendering speed never affects game speed. MonoGame's default fixed
        // 60 Hz update is kept for interactive use; benchmarks unlock both the
        // timestep and vsync to measure raw frame cost.
        if (BenchFrames > 0)
        {
            IsFixedTimeStep = false;
            _graphics.SynchronizeWithVerticalRetrace = false;
            _graphics.ApplyChanges();
        }

        base.Initialize();
```

(currently `ViewerGame.cs:1128-1141`). Replace it with:

```csharp
    protected override void Initialize()
    {
        // A plain interactive launch (Program.cs: interactiveLaunch && !windowed)
        // opens borderless-fullscreen at the desktop's native resolution, matching
        // vanilla Fallout 2/fo2ce's default presentation. Every CLI-driven
        // screenshot/test/benchmark path leaves StartFullscreen false.
        if (StartFullscreen)
        {
            DisplayMode mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            _graphics.HardwareModeSwitch = false;
            _graphics.IsFullScreen = true;
            _graphics.PreferredBackBufferWidth = mode.Width;
            _graphics.PreferredBackBufferHeight = mode.Height;
            _graphics.ApplyChanges();
        }

        // The simulation is wall-time driven (palette cycling, soon animations),
        // so rendering speed never affects game speed. MonoGame's default fixed
        // 60 Hz update is kept for interactive use; benchmarks unlock both the
        // timestep and vsync to measure raw frame cost.
        if (BenchFrames > 0)
        {
            IsFixedTimeStep = false;
            _graphics.SynchronizeWithVerticalRetrace = false;
            _graphics.ApplyChanges();
        }

        base.Initialize();
```

- [ ] **Step 3: Build to confirm no compile errors**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors (pre-existing nullable warnings, if any, are unrelated and fine).

- [ ] **Step 4: Add `ToggleFullscreen()` and the Alt+Enter check in `Update()`**

Find the top of `Update()`:

```csharp
    protected override void Update(GameTime gameTime)
    {
        if (_exitAfterStartupActions)
        {
            if (SaveOnExit)
                SaveGame();
            Exit();
            return;
        }

        _frameClock.Restart();
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();
        // Stage 2 (UI Scale): the dialog panel and the main-menu family hit-test against
        // VirtualViewport()-derived rectangles, so their click position must be the same
        // transformed point, not the raw device mouse. As of UI Scale Stage 5 (worldmap chrome,
        // the last screen migrated), every screen-space hit-test in this method uses `uiMouse`;
        // raw `mouse` remains only for button/wheel state and the worldmap's legacy
        // no-chrome-art fallback (which has its own independent, correct scaling).
        Point uiMouse = UiMouse();
```

(currently `ViewerGame.cs:1922-1941`). Replace it with:

```csharp
    protected override void Update(GameTime gameTime)
    {
        if (_exitAfterStartupActions)
        {
            if (SaveOnExit)
                SaveGame();
            Exit();
            return;
        }

        _frameClock.Restart();
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();

        // Global fullscreen toggle, checked before any state-specific handling so it
        // works regardless of what screen/dialog/menu is open. Consumes the Enter
        // press (returns early) so it doesn't also fall through to the many plain-Enter
        // handlers elsewhere in this method (dialogs, character creation, menu confirm).
        if ((keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt))
            && IsKeyPressed(keyboard, Keys.Enter))
        {
            ToggleFullscreen();
            return;
        }

        // Stage 2 (UI Scale): the dialog panel and the main-menu family hit-test against
        // VirtualViewport()-derived rectangles, so their click position must be the same
        // transformed point, not the raw device mouse. As of UI Scale Stage 5 (worldmap chrome,
        // the last screen migrated), every screen-space hit-test in this method uses `uiMouse`;
        // raw `mouse` remains only for button/wheel state and the worldmap's legacy
        // no-chrome-art fallback (which has its own independent, correct scaling).
        Point uiMouse = UiMouse();
```

Then add the `ToggleFullscreen()` method itself. Find `IsKeyPressed`:

```csharp
    private bool IsKeyPressed(KeyboardState keyboard, Keys key) =>
```

(`ViewerGame.cs:3030`). Immediately before that line, insert:

```csharp
    /// <summary>Alt+Enter: flips between borderless fullscreen (desktop resolution) and
    /// windowed, remembering the last windowed size across the toggle.</summary>
    private void ToggleFullscreen()
    {
        if (_graphics.IsFullScreen)
        {
            _graphics.IsFullScreen = false;
            _graphics.PreferredBackBufferWidth = _windowedWidth;
            _graphics.PreferredBackBufferHeight = _windowedHeight;
        }
        else
        {
            DisplayMode mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            _graphics.HardwareModeSwitch = false;
            _graphics.IsFullScreen = true;
            _graphics.PreferredBackBufferWidth = mode.Width;
            _graphics.PreferredBackBufferHeight = mode.Height;
        }
        _graphics.ApplyChanges();
    }

```

- [ ] **Step 5: Build to confirm no compile errors**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors. If `DisplayMode` or `GraphicsAdapter` are
unresolved, check the top of `ViewerGame.cs` for `using Microsoft.Xna.Framework.Graphics;`
— it should already be present (the file already uses `GraphicsDevice`, `Texture2D`, etc.
from that namespace); if for some reason it's missing, add it.

- [ ] **Step 6: Add the `--windowed` CLI flag in `Program.cs`**

Near the top of `Program.cs`, find the other boolean flag variables:

```csharp
bool noAudio = false;
bool noAmbient = false;
bool forceMenu = false;
string? menuStartState = null;
```

Add `windowed` alongside them:

```csharp
bool noAudio = false;
bool noAmbient = false;
bool forceMenu = false;
string? menuStartState = null;
bool windowed = false;
```

Then find the flag-parsing switch:

```csharp
        case "--no-roofs":
            roofs = false;
            break;
        case "--no-audio":
            noAudio = true;
            break;
```

(currently `Program.cs:43-47`). Add a new case immediately after `--no-roofs`:

```csharp
        case "--no-roofs":
            roofs = false;
            break;
        case "--windowed":
            windowed = true;
            break;
        case "--no-audio":
            noAudio = true;
            break;
```

- [ ] **Step 7: Wire `StartFullscreen` at the launch site**

Find:

```csharp
using var game = new ViewerGame(gameDir!, mapName, screenshot, roofs)
{
    StartInMenu = interactiveLaunch || forceMenu,
    MenuStartState = menuStartState,
```

(currently `Program.cs:892-895`). Replace it with:

```csharp
using var game = new ViewerGame(gameDir!, mapName, screenshot, roofs)
{
    StartInMenu = interactiveLaunch || forceMenu,
    StartFullscreen = interactiveLaunch && !windowed,
    MenuStartState = menuStartState,
```

- [ ] **Step 8: Build to confirm no compile errors**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 9: Manual verification**

All four checks from the spec's Testing section. Use `xdotool`/`spectacle` the same way
established in `docs/fo2ce-comparison-playbook.md` for windowed-state screenshots (an
actual fullscreen window has no titlebar to search by name with `xdotool search --name`,
so use `xdotool getactivewindow` after launch instead, or simply eyeball via a full-screen
`spectacle -b -n -o <file>` capture).

1. **Fullscreen by default:**

```bash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data &
sleep 5
spectacle -b -n -o /tmp/fullscreen-check.png
kill %1 2>/dev/null || true
```

Read `/tmp/fullscreen-check.png` with the Read tool. Expected: the Hexwaste main menu
fills the entire screen with no window titlebar/border visible, at the desktop's native
resolution (compare against `xdotool getdisplaygeometry` or the earlier session's known
desktop size, 1920x1032-ish on this machine).

2. **`--windowed` opt-out:**

```bash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --windowed &
sleep 5
spectacle -b -n -o /tmp/windowed-check.png
```

Expected: a 1280x720 window with a visible titlebar ("Hexwaste.Viewer"), same as before
this change. Leave this instance running for the next check.

3. **Alt+Enter toggle + windowed-size memory:**

```bash
WIN=$(xdotool search --name "Hexwaste" | head -1)
xdotool windowactivate "$WIN"
sleep 0.3
xdotool key alt+Return
sleep 1
spectacle -b -n -o /tmp/alt-enter-fullscreen.png
```

Expected: `/tmp/alt-enter-fullscreen.png` shows fullscreen (titlebar gone) — the toggle
worked from a windowed launch. Then toggle back and confirm the 1280x720 size returns
(not some other default):

```bash
xdotool key alt+Return
sleep 1
spectacle -b -n -o /tmp/alt-enter-windowed-again.png
kill %1 2>/dev/null || true
```

Expected: back to a 1280x720 window.

4. **Screenshot path unaffected:**

```bash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --map artemple.map --screenshot /tmp/probe-check.png
```

Expected: this command runs to completion and exits (it's a one-shot headless
screenshot action, not an interactive launch — no window is left running), producing
`/tmp/probe-check.png` at the same fixed dimensions this flag has always produced,
confirming `interactiveLaunch = false` for this invocation kept `StartFullscreen` off.

- [ ] **Step 10: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/Program.cs
git commit -m "$(cat <<'EOF'
feat(viewer): default to fullscreen on interactive launches

A plain launch now opens borderless-fullscreen at the desktop's
native resolution, matching vanilla Fallout 2/fo2ce's default
presentation, instead of always windowing at 1280x720. --windowed
opts back out for dev/testing, and Alt+Enter toggles at runtime
(remembering the last windowed size). Every CLI-driven
screenshot/test/benchmark path is untouched -- they already disable
interactiveLaunch, which now also gates StartFullscreen.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

## Self-Review

**Spec coverage:**
- Borderless fullscreen, native desktop resolution → Steps 2, 4 (`Initialize()`, `ToggleFullscreen()`). ✅
- `StartFullscreen` property, object-initializer pattern → Step 1. ✅
- `--windowed` opt-out flag and `interactiveLaunch && !windowed` wiring → Steps 6-7. ✅
- Alt+Enter runtime toggle, consuming the Enter press → Step 4. ✅
- Windowed-size memory across toggles → Steps 2 (`ClientSizeChanged` handler), 4 (`ToggleFullscreen`). ✅
- Non-goals (no Preferences entry, no per-monitor selection, no exclusive fullscreen, no change to CLI-driven paths) → nothing in this plan touches any of them. ✅
- Testing section's four manual checks → Step 9. ✅

**Placeholder scan:** No TBD/TODO. Step 5 flags a real but minor uncertainty (whether
`using Microsoft.Xna.Framework.Graphics;` is already present) with a concrete fallback
instruction rather than silently assuming — not a vague placeholder, a grounded
contingency.

**Type consistency:** `StartFullscreen` is `bool` everywhere it's declared/set/read
(Steps 1, 2, 7). `ToggleFullscreen()` returns `void`, matches its call site in Step 4.
`_windowedWidth`/`_windowedHeight` are `int` throughout, consistent with
`Window.ClientBounds.Width/Height` (also `int`) and `_graphics.PreferredBackBufferWidth/
Height` (also `int`).
