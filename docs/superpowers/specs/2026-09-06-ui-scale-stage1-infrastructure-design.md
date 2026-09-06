# UI Scale — Stage 1: Shared Infrastructure

## Problem

Hexwaste renders every UI screen (HUD bar, dialog, inventory, character sheet,
Pip-Boy, automap, menus, worldmap chrome, etc.) at native 1:1 pixel size,
centered in whatever window size the player has — unlike `fallout2-ce`, which
renders its whole game into a fixed 640×480 buffer and lets SDL stretch that
buffer to fill the physical screen. Comparing the two side by side (this
session's ongoing fo2ce-vs-Hexwaste work) made the size difference obvious:
in a typical window, Hexwaste's UI looks small and letterboxed where fo2ce's
fills the screen.

Matching fo2ce's presentation means introducing a uniform render-time scale
factor across the whole UI, and — critically — transforming real mouse input
by the same factor everywhere a click is hit-tested, or clicks will land on
the wrong pixels the moment scaling changes what's drawn where. An inventory
of the current codebase found **60+ call sites reading `GraphicsDevice.Viewport`
dimensions and 17 call sites reading raw `Mouse.GetState()` position**, spread
across nearly every file in `src/Hexwaste.Viewer` (dialog, inventory,
character sheet, Pip-Boy, automap, Skilldex, options, preferences, save/load,
perk picker, aim dialog, tactics, the HUD bar, the action menu, the elevator
picker, and the worldmap chrome's own separate class). That is too large and
too risky (many of these are gameplay-critical hit-tests) to change in one
pass, so the work is staged. **This spec covers Stage 1 only: the shared math
and helpers every later stage will use — no existing draw call, hit-test, or
visible rendering changes at all.** Stages 2+ (dialog+menu, the remaining
modal screens, the HUD bar/action menu/elevator picker, worldmap chrome) are
separate specs, written after this stage ships and is verified inert.

## Design

### The scale model

`fallout2-ce` stretches its fixed 640×480 buffer to an arbitrary window
shape, which distorts (non-uniform stretch) unless the window happens to be
4:3. Hexwaste will instead compute a **uniform** scale factor —
`scale = min(viewportWidth / 640, viewportHeight / 480)` — and let the
"virtual" canvas extend past 640×480 on whichever axis has slack, rather than
letterboxing with black bars. Concretely: if the width axis is the limiting
one, the virtual canvas is exactly `640 × (viewportHeight / scale)` — 640
wide (matching real screen width exactly once scaled) and taller than 480,
so a screen anchored to the true bottom/top edge (like the HUD bar) can still
reach them, while 640×480-sized content (menus, dialog, most modal screens)
centers within that taller canvas exactly as it does today via
`(virtualWidth - 640) / 2`. This is a deliberate, minor improvement over
fo2ce's own non-uniform stretch — avoiding pixel distortion — not a fidelity
gap; it will be called out plainly in this project's docs once later stages
make it visible.

### Where the pure math lives

Per this project's existing architecture
(`Hexwaste.Formats` — pure .NET, zero MonoGame deps, unit-testable via
`tests/Hexwaste.Formats.Tests`; `Hexwaste.Viewer` — the MonoGame app), the
scale/virtual-viewport/mouse-transform arithmetic is plain float/int math
with no MonoGame dependency, so it belongs in a new
`src/Hexwaste.Formats/Rendering/UiScale.cs` — a small static class,
unit-tested directly, matching the pattern already used for e.g.
`Hexwaste.Formats/Hex`, `Hexwaste.Formats/Light`. `Hexwaste.Viewer` gets a
thin wrapper (new partial file `ViewerGame.UiScale.cs`, following this
project's one-partial-file-per-concern convention like `ViewerGame.Hud.cs`)
that calls into it using real `GraphicsDevice.Viewport`/`Mouse.GetState()`
values and returns MonoGame types (`float`, `Rectangle`, `Point`).

### The three operations

1. **`ComputeScale(viewportWidth, viewportHeight, baseWidth = 640, baseHeight = 480)`**
   → `float`. `Math.Min(viewportWidth / (float)baseWidth, viewportHeight / (float)baseHeight)`,
   clamped to a minimum of `0.1f` (defends against a degenerate near-zero
   window size; MonoGame doesn't allow literally zero, but this is cheap
   insurance against a divide producing `Infinity`/`NaN` downstream).
2. **`ComputeVirtualViewport(viewportWidth, viewportHeight, scale)`** →
   `(int Width, int Height)`. `((int)(viewportWidth / scale), (int)(viewportHeight / scale))`
   — truncating, matching this codebase's existing tolerance for sub-pixel
   imprecision in similar centering math (e.g. `(vp.Width - 640) / 2` already
   truncates).
3. **`TransformMouse(rawX, rawY, scale)`** → `(int X, int Y)`.
   `((int)(rawX / scale), (int)(rawY / scale))`. No offset term is needed:
   because the virtual viewport is *derived from* the real viewport divided
   by the same scale (not a fixed 640×480 with letterbox bars), scaling the
   whole virtual canvas back up by `scale` exactly tiles the real screen with
   no gap — there is nothing to offset.

### Viewer-side wrappers (`ViewerGame.UiScale.cs`)

- `float UiScale()` — calls `UiScale.ComputeScale` with the current
  `GraphicsDevice.Viewport.Width/Height`.
- `Rectangle VirtualViewport()` — `new Rectangle(0, 0, w, h)` from
  `UiScale.ComputeVirtualViewport` using the same viewport and the scale
  from `UiScale()`.
- `Point UiMouse()` — `UiScale.TransformMouse` applied to
  `Mouse.GetState().X/Y` and the current `UiScale()`. Callers that need
  button-press state (`Mouse.GetState().LeftButton`) keep calling
  `Mouse.GetState()` directly for that — only the position needs transforming.

### Explicitly NOT in Stage 1

- No existing `_spriteBatch.Begin(...)` call gets the new transform matrix.
- No existing `GraphicsDevice.Viewport` reference is replaced with
  `VirtualViewport()`.
- No existing `Mouse.GetState().X/Y` hit-test is replaced with `UiMouse()`.
- No visible rendering change, no behavior change, no golden fixture impact.

Stage 1's only deliverable is correct, unit-tested, currently-unused
infrastructure that Stage 2 (dialog + main menu, the two screens already
verified against fo2ce this session) will be the first to actually wire in.

## Testing

Pure math in `Hexwaste.Formats` is directly unit-testable with xUnit,
matching this project's existing test conventions (no `FALLOUT2_DIR` guard
needed — this logic touches no game data). Required coverage:

- `ComputeScale`: width-limiting case (e.g. 1280×720 → 1.5, since
  720/480 = 1.5 < 1280/640 = 2.0), height-limiting case (e.g. 1920×1080 →
  3.0 exactly, since both axes agree, and a case where width is stricter —
  e.g. 640×1000 → 1.0, limited by width), the exact-4:3 case (640×480 → 1.0,
  1280×960 → 2.0), and the degenerate-viewport clamp (e.g. 1×1 → 0.1, not 0
  or a negative/NaN value).
- `ComputeVirtualViewport`: at a width-limiting scale, virtual width comes
  back as exactly the base width (640) and virtual height is `>= 480`; at a
  height-limiting scale, the reverse. A round-trip check
  (`virtualWidth * scale` is within 1px of the original `viewportWidth`, same
  for height) for a handful of representative sizes.
- `TransformMouse`: a point at the real viewport's exact center maps to the
  virtual viewport's exact center (within integer-truncation tolerance) for
  at least one width-limiting and one height-limiting scale; a point at
  `(0,0)` maps to `(0,0)` exactly (no offset term).

## Non-goals (deferred to later, separately-specced stages)

- Wiring the scale transform into any actual `SpriteBatch.Begin` call.
- Replacing any `GraphicsDevice.Viewport` reference in existing draw/layout
  code with `VirtualViewport()`.
- Replacing any `Mouse.GetState()` hit-test call with `UiMouse()`.
- The HUD bar, action menu, elevator picker, and worldmap chrome — all
  explicitly later stages per the staging plan agreed before this spec.
- Any decision about whether the eventual scale should be integer-only
  (matching this project's existing, separate "optional integer zoom" world
  camera language in CLAUDE.md) versus fractional (matching fo2ce's own
  arbitrary-window-size stretch) for the *world/gameplay* camera — that
  camera is untouched by this whole effort. This spec's scale is for UI
  presentation only, and is deliberately fractional, matching fo2ce's own
  arbitrary stretch ratio at arbitrary window sizes.
