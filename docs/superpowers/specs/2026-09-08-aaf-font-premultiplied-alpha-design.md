# AAF font premultiplied-alpha fix — design

## Problem

Hexwaste's AAF interface-font rendering (`font1.aaf`, used pervasively across the app —
HUD text, menus, panels) washes out partial-opacity glyph pixels toward full
brightness, destroying fine letterform detail. Concretely: lowercase `'a'` renders
visually indistinguishable from `'o'` — discovered via a real side-by-side comparison
against fo2ce, where the inventory summary panel's "Laser"/"Plasma" labels read as
"Loser"/"Plosmo" in Hexwaste.

This isn't a font-selection bug (font1.aaf is confirmed the correct vanilla file —
`reference/fallout2-ce/src/font_manager.cc:101,117-122,208-220` traces fo2ce's font ID
101 to exactly `font1.aaf`) and it isn't corrupted source data (the raw glyph bytes for
`'a'` and `'o'` were extracted and compared directly: `'a'`'s right edge stays at full
opacity (level 7) in every row — a flat, squared-off wall — while `'o'` tapers to
partial opacity (level 3 or 6) at all four corners — a rounded ring. That taper vs.
flat-edge contrast is the entire visual cue distinguishing the two glyphs at 6×7px, and
it survives correctly all the way through Hexwaste's own AAF parsing).

## Root cause

Confirmed against `reference/fallout2-ce/src/font_manager.cc:373-380` (pinned base
tree): fo2ce's `interfaceFontDrawImpl()` composites each raw opacity byte through a
per-pixel blend-table lookup (`_getColorBlendTable`, `color.cc:376-462`) that correctly
weights *both* the foreground glyph color and the background pixel by the same
`level/7` fraction — proper alpha compositing done in indexed-RGB space.

Hexwaste's `AafFontRenderer` (`src/Hexwaste.Viewer/AafFontRenderer.cs:22-58`) instead
bakes every glyph into one shared 16×16-cell atlas texture at construction time, as
**straight (non-premultiplied) alpha**: RGB is always written as flat white
(`255,255,255`), and only the alpha channel carries the opacity-derived value
(`AafFontRenderer.cs:47-49`):

```csharp
byte alpha = (byte)Math.Min(level * 255 / font.MaxLevel, 255);
rgba[pixel] = rgba[pixel + 1] = rgba[pixel + 2] = 255;
rgba[pixel + 3] = alpha;
```

Every `SpriteBatch.Begin()` call site that later draws this atlas — dozens of them
across the project — uses MonoGame's default `blendState`, which is
`BlendState.AlphaBlend`: `ColorSourceBlend = One, ColorDestinationBlend =
InverseSourceAlpha`, i.e. `result = src.rgb + dst.rgb * (1 - src.a)`. This blend
equation expects the source RGB to already be **premultiplied** by its own alpha. Since
Hexwaste's atlas never does that (source RGB is flat `255` regardless of opacity),
every partially-transparent pixel — even one at only 43% opacity (`level=3` of 7,
`alpha≈109`) — contributes its *full, unscaled* white RGB to the blend result, with
only the background's bleed-through term reduced by `(1-a)`. Visually, a
43%-opacity pixel ends up looking nearly as bright as a 100%-opacity one. That is
exactly the feature destroyed: the corner-taper vs. flat-edge distinction between
`'o'` and `'a'` lives entirely in those partial-opacity pixels.

## Fix

Premultiply the atlas texture's RGB channels by alpha at construction time, in
`AafFontRenderer`'s constructor (`AafFontRenderer.cs:31-57`):

```csharp
byte alpha = (byte)Math.Min(level * 255 / font.MaxLevel, 255);
rgba[pixel] = rgba[pixel + 1] = rgba[pixel + 2] = alpha;
rgba[pixel + 3] = alpha;
```

(Only the RGB-fill line changes — `alpha` instead of the literal `255` — the alpha
channel itself is already correct and unchanged.)

Every draw call site keeps its existing `SpriteBatch.Begin()` call exactly as-is —
none of them need a `blendState` argument, since the default `BlendState.AlphaBlend`
is the *correct* blend mode for a properly premultiplied-alpha texture; the bug was
purely a mismatch between that (correct, unchanged) blend mode and the (incorrectly
straight-alpha) texture data. This is a single-file, single-method fix.

`Draw()`'s per-call color tint (`AafFontRenderer.cs:86-91`, the `color` parameter used
for both the drop-shadow pass and the main colored-glyph pass) continues to work
correctly against a premultiplied source: `SpriteBatch` multiplies the tint into the
sampled texture color per-channel before the blend equation runs, which is
well-defined and correct regardless of whether the source texture's own alpha was
baked into its RGB — this is the standard, textbook-correct MonoGame pattern for
tinted premultiplied-alpha sprites (drop shadows, colored UI glyphs, etc.).

## Scope

Single file: `src/Hexwaste.Viewer/AafFontRenderer.cs`, one 3-line change inside the
constructor's glyph-unpacking loop. `AafFont.cs` (pure `.aaf` byte parsing — confirmed
correct against fo2ce's own unpacking loop, same header size, same row-major
byte-per-pixel layout, no stride/padding mismatch) is untouched. No call site outside
this file changes.

## Non-goals

- No change to `AafFont.cs`'s parsing logic — already confirmed correct.
- No change to any `SpriteBatch.Begin()` call site's `blendState` argument anywhere in
  the project — the existing default (`BlendState.AlphaBlend`) is correct once the
  atlas texture itself is fixed.
- Does not address the inventory summary panel's separate color/layout/weight-position
  bugs (tracked as a follow-up fix, scoped independently since those are unrelated to
  glyph rendering fidelity).

## Testing

No automated test currently covers font rendering pixel output — same situation as
prior fixes this session (MonoGame dependency, no test project covers `ViewerGame` or
its rendering). Verify manually:

1. Launch Hexwaste, reach the inventory screen (the exact repro from today's
   comparison review), screenshot, and confirm the resistance-list labels read
   "Laser"/"Plasma" (not "Loser"/"Plosmo").
2. Spot-check a few other screens with lowercase text using `font1.aaf` (e.g. the
   Skilldex or Pip-Boy) to confirm general text legibility improved and nothing
   regressed (no washed-out or overly dark glyphs, no color-tint regressions on
   drop-shadowed text).
