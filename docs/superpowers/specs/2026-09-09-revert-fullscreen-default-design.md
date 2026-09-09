# Revert fullscreen-by-default's default — design

## Problem

Yesterday's fullscreen-by-default feature (`7a48e74`, bugfixed in `904be23`) made a
plain interactive launch open at native fullscreen resolution instead of the previous
fixed 1280x720 window. Reported today: the game feels "less crispy" than before.

Investigated and root-caused with concrete evidence, not a broad rollback:

- **The game world is provably unaffected.** `WorldZoomMatrix()`
  (`src/Hexwaste.Viewer/ViewerGame.Rendering.cs:20-27`) returns `Matrix.Identity` at
  the default zoom level, and every world-layer draw call (floors, objects, roofs) uses
  it — rendering has always been native, unscaled 1:1 pixels, unrelated to any UI Scale
  work. Side-by-side crops (same map, same camera position) confirm this: the temple
  facade sprite is pixel-for-pixel identical between a 1280x720 windowed capture and a
  1920x1080 fullscreen capture. What changed is that at native fullscreen on a large
  monitor, the same-sized sprites occupy a much smaller fraction of the screen than in
  the old small window — the FOV/apparent-size effect already investigated and
  deliberately accepted during yesterday's fo2ce-comparison work, now felt directly
  during real play rather than in a side-by-side screenshot.
- **UI/menu text is not measurably blurrier.** Cropped and upscaled the same main-menu
  button-rail text from both a windowed (1.5x UI scale) and fullscreen (2.25x UI scale)
  capture — no clear sharpness regression; if anything, text legibility looks at least
  as good, plausibly helped by the AAF font premultiplied-alpha fix from earlier this
  session.

So the "less crispy" perception traces specifically to yesterday's fullscreen-by-default
change — not the broader 3-day UI Scale project, which is otherwise unaffected and
stays exactly as it is (menu-backdrop-fill, the font fix, and the inventory panel
color/layout/weight-readout fixes are all unrelated to this and untouched).

## Decision

Revert the *default* only. Keep the fullscreen capability itself — the `StartFullscreen`
property, `Initialize()`'s fullscreen setup, `ToggleFullscreen()`, and the Alt+Enter
runtime toggle in `Update()` are all already correct and independently reviewed
(final whole-branch review: "Ready to merge? Yes"). No reason to remove working,
validated code. Only the launch-time default flips back.

## Change

`src/Hexwaste.Viewer/Program.cs` — invert the opt-in/opt-out flag from `windowed`
(default off, `--windowed` opts OUT of fullscreen) to `fullscreen` (default off,
`--fullscreen` opts IN to fullscreen):

Find:

```csharp
bool windowed = false;
```

Replace with:

```csharp
bool fullscreen = false;
```

Find:

```csharp
        case "--windowed":
            windowed = true;
            break;
```

Replace with:

```csharp
        case "--fullscreen":
            fullscreen = true;
            break;
```

Find:

```csharp
    StartFullscreen = interactiveLaunch && !windowed,
```

Replace with:

```csharp
    StartFullscreen = interactiveLaunch && fullscreen,
```

A plain interactive launch now has `fullscreen = false` (the new variable's default),
so `StartFullscreen` evaluates to `false` — windowed at 1280x720, exactly matching
every launch before yesterday's change. `--fullscreen` opts in for anyone who wants
native fullscreen. Alt+Enter still works identically from either starting state (its
own code, `ViewerGame.cs`, is untouched).

## Scope

`src/Hexwaste.Viewer/Program.cs` only, 3 corresponding edits (variable declaration,
flag-parsing case, `StartFullscreen` wiring). No other file changes. Every CLI-driven
screenshot/test/benchmark path is unaffected either way — they already force
`interactiveLaunch = false`, which makes `StartFullscreen` false regardless of this
flag's value.

## Non-goals

- No change to `ViewerGame.cs` (`StartFullscreen` property, `Initialize()`,
  `ToggleFullscreen()`, the Alt+Enter check, the `ClientSizeChanged` handler) — all
  already correct.
- No change to any other file from yesterday's or today's work (menu backdrop fill,
  AAF font premultiplied-alpha fix, inventory summary panel color/layout/weight-readout
  fixes) — none of these are implicated by the investigation above.
- No change to the game world's rendering — confirmed unaffected by any recent work.

## Testing

No automated test project covers `ViewerGame`/`Program.cs`'s launch wiring (MonoGame
dependency, same situation as every fix this session). Verify manually:

1. Launch with no flags (`dotnet run --project src/Hexwaste.Viewer -- --game-dir
   game-data`) and confirm it opens as a 1280x720 window (titlebar visible), not
   fullscreen — the primary fix.
2. Launch with `--fullscreen` added and confirm it opens fullscreen at desktop
   resolution, exactly as a plain launch did yesterday.
3. From the new windowed default, press Alt+Enter and confirm it still toggles to
   fullscreen correctly (this exercises that `ToggleFullscreen()`'s own code, untouched
   by this change, still works from the new starting state).
4. Confirm a `--screenshot`-driven invocation still produces its existing fixed-size
   output, unaffected either way.
