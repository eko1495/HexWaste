# Fullscreen by default for interactive launches — design

## Problem

Hexwaste always opens as a 1280x720 window, regardless of how it's launched. Vanilla
Fallout 2 (and fo2ce, its reference port) runs fullscreen by default
(`fallout2.cfg`'s `[screen] windowed=0`), filling the whole screen. A plain interactive
launch of Hexwaste — the same launch mode that already opens the main menu instead of
jumping straight into a map — should feel the same way: fullscreen, no window chrome,
filling the screen like the original game.

## Scope

Only genuine interactive play is affected. `Program.cs:888-891` already computes
`interactiveLaunch` (true only for a plain launch with none of the CLI test/headless
flags: `--screenshot`, `--menu`, `--walk`, `--goto`, benchmarks, etc. set) to decide
whether to show the main menu instead of jumping straight into a map. This spec reuses
exactly that same condition — not `forceMenu` (the `--menu` flag exists specifically
for "menu screenshots/testing" per its own comment at `Program.cs:74`, i.e. it's a dev
flag, not real play). Every CLI-driven screenshot, golden-image test, and benchmark
already sets `interactiveLaunch = false` today and is completely unaffected by this
change — they keep the fixed 1280x720 window they rely on for deterministic pixel
comparisons.

## Design

### 1. Fullscreen mode: borderless, native desktop resolution

MonoGame's `GraphicsDeviceManager` supports two fullscreen modes: exclusive
(a real display-mode switch) and borderless (`HardwareModeSwitch = false`, fills the
screen at its current desktop resolution without switching modes). Borderless is more
robust across Linux/KDE multi-monitor setups (no mode-switch flicker or failure risk)
and needs no new scaling code — Hexwaste's existing UI Scale system already renders
correctly at any window size, including whatever the desktop's native resolution is.

### 2. `ViewerGame` changes

New public property, following the existing object-initializer pattern used for
`StartInMenu`, `StartInWalkMode`, etc. (`ViewerGame.cs:36,155`):

```csharp
public bool StartFullscreen { get; set; }
```

New private fields to remember the last windowed size (so toggling back from
fullscreen restores it rather than resetting to 1280x720 every time):

```csharp
private int _windowedWidth = 1280;
private int _windowedHeight = 720;
```

In `Initialize()` (`ViewerGame.cs:1128`), before `base.Initialize()`:

```csharp
if (StartFullscreen)
{
    DisplayMode mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
    _graphics.HardwareModeSwitch = false;
    _graphics.IsFullScreen = true;
    _graphics.PreferredBackBufferWidth = mode.Width;
    _graphics.PreferredBackBufferHeight = mode.Height;
    _graphics.ApplyChanges();
}
```

This mirrors the existing `BenchFrames > 0` branch immediately below it, which already
calls `_graphics.ApplyChanges()` before `base.Initialize()` — so this ordering is a
proven-safe pattern in this codebase, not a new risk.

### 3. Runtime toggle: Alt+Enter

Checked once, early in `Update()` (`ViewerGame.cs:1922`, immediately after `keyboard`/
`mouse`/`uiMouse` are computed at line 1941 — before any state-specific handling), so it
takes effect regardless of what screen/dialog/menu is currently open:

```csharp
if ((keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt))
    && IsKeyPressed(keyboard, Keys.Enter))
{
    ToggleFullscreen();
    _previousMouse = mouse;
    _previousKeyboard = keyboard;
    base.Update(gameTime);
    return; // consume the Enter press: don't also fall through to the many
            // plain-Enter handlers elsewhere in this method (dialogs, character
            // creation, menu confirm, etc.)
}
```

```csharp
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

`_windowedWidth`/`_windowedHeight` are updated whenever the window is resized while
**not** fullscreen, inside the existing `Window.ClientSizeChanged` handler
(`ViewerGame.cs:1124-1125`):

```csharp
Window.ClientSizeChanged += (_, _) =>
{
    if (!_graphics.IsFullScreen)
    {
        _windowedWidth = Window.ClientBounds.Width;
        _windowedHeight = Window.ClientBounds.Height;
    }
    _camera.SetWindowSize(Window.ClientBounds.Width, Window.ClientBounds.Height);
};
```

Applying `ApplyChanges()` itself fires `ClientSizeChanged`, so `_camera.SetWindowSize`
is already called correctly after a fullscreen toggle with no extra wiring — this is
the same mechanism that already keeps the camera in sync when a user manually resizes
the windowed mode today.

### 4. CLI: `--windowed` opt-out flag

`Program.cs`: new flag parsed alongside the other boolean flags (near `--no-roofs`,
`Program.cs:43-45`):

```csharp
case "--windowed":
    windowed = true;
    break;
```

And the launch wiring (`Program.cs:894`, alongside the existing `StartInMenu =
interactiveLaunch || forceMenu`):

```csharp
StartFullscreen = interactiveLaunch && !windowed,
```

`windowed` defaults to `false`; a plain launch with no flags gets `interactiveLaunch =
true, windowed = false` → fullscreen. `--windowed` forces the old windowed behavior for
dev/testing convenience even on an otherwise-plain launch. Every existing CLI/headless
flag combination already forces `interactiveLaunch = false`, so `--windowed` is a no-op
in those cases (they were never going to be fullscreen anyway) — it exists purely for
the "I want to manually play/test in a window" case.

## Non-goals

- No new Preferences-screen entry. Vanilla Fallout 2 doesn't expose windowed/fullscreen
  as an in-game preference either — `[screen] windowed` is a `fallout2.cfg`-only
  setting there, edited outside the game. Hexwaste has no config file (documented in
  `GamePreferences.cs`'s own doc comment: "Hexwaste has no config file, so preferences
  reset each launch"), so a CLI flag is the natural equivalent, not a new persisted
  setting.
- No per-monitor selection (uses `GraphicsAdapter.DefaultAdapter`, matching how the
  rest of the engine already talks to the graphics device).
- No exclusive-fullscreen mode.
- No change to any CLI-driven screenshot/test/benchmark path — all of them already set
  `interactiveLaunch = false` and are untouched by this spec.

## Testing

`ViewerGame` has MonoGame dependencies at the project level, outside
`Hexwaste.Formats.Tests`'s scope (same situation as the recent scroll-border-clamp
work) — no automated unit test. Verify manually:

1. Launch Hexwaste with no flags (`dotnet run --project src/Hexwaste.Viewer --
   --game-dir game-data`) and confirm it opens fullscreen at the desktop's resolution,
   landing on the main menu with no window chrome/title bar visible.
2. Launch with `--windowed` added and confirm it opens as a 1280x720 window instead.
3. From a fullscreen launch, press Alt+Enter and confirm it drops to a 1280x720 window
   (the untouched default, since no prior windowed size was recorded this session);
   resize that window, press Alt+Enter twice more, and confirm it returns to the
   resized dimensions rather than resetting to 1280x720.
4. Confirm a `--screenshot`-driven invocation (e.g. `--map artemple.map --screenshot
   /tmp/check.png`) still produces the existing fixed-size output, unaffected by this
   change.
