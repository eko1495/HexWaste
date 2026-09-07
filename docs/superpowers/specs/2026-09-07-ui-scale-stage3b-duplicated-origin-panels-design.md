# UI Scale — Stage 3b: Character Sheet, Pip-Boy, Options, Automap

## Problem

Stage 2 and Stage 3a (shipped) wired the shared `UiScale` infrastructure into the dialog panel,
the main-menu family, Skilldex, the perk picker, and save/load — all five now render at a uniform
scale filling non-4:3 windows, matching `fallout2-ce`'s fullscreen stretch. This spec covers the
next batch, split out during Stage 3's original pre-implementation survey specifically because
these four screens carry **duplicated inline origin math** (the risk Stage 3a's screens didn't
have) and **two cross-file callers in the CLI test harness** (the risk Stage 2's `MenuOrigin()`
regression taught this project to grep for before ever switching a shared helper):

- **Character sheet** (`DrawSkillAllocator`, `src/Hexwaste.Viewer/ViewerGame.Panels.cs`)
- **Pip-Boy** (`DrawPipboy`/`DrawPipboyArchives`, same file)
- **Options** (`DrawOptions`, same file)
- **Automap** (`DrawAutomap`, same file)

Inventory (deferred from Stage 3a for an unrelated, more severe reason — six independent
fixed-position fallback branches plus a drag-ghost icon) remains its own future plan. Preferences,
aim dialog, and tactics remain Stage 3c.

## Grounding (confirmed by a direct source-tree survey this session)

- All four screens' `Draw*` methods are invoked from the same shared per-frame
  `SpriteBatch.Begin/End` block established in Stage 2 (`ViewerGame.cs`, the `else` branch of
  `if (_worldmapOpen)`) — confirmed structurally unchanged by Stage 3a's commits.
- **Character sheet — three independent, byte-for-byte-identical copies of the same origin
  formula**, none delegating to the others:
  ```csharp
  Rectangle vp = GraphicsDevice.Viewport.Bounds;
  int ox = (vp.Width - 640) / 2, oy = (vp.Height - 480) / 2;
  ```
  at `DrawSkillAllocator` (`ViewerGame.Panels.cs:79-80`), `CharSheetItemAt` (`:341-342`, the
  hit-test), and a differently-named-but-identical variant inside `Update()`'s DONE/CANCEL button
  check (`ViewerGame.cs:2176-2177`, `cvp`/`cbx`/`cby`). All three must convert together or the
  three will disagree about where the sheet sits. `DrawSkillAllocator` has a genuine, separate
  fallback method, `DrawSkillAllocatorFallback()` (`Panels.cs:368`), gated by
  `if (_charBg is null) { DrawSkillAllocatorFallback(); return; }` (`Panels.cs:73`) — the fallback
  draws at a **fixed, non-viewport-relative** position (`x=48, y=28`) and reads no viewport at
  all, so (matching the Skilldex/perk-picker precedent) only the art path scales; the fallback is
  untouched. No cross-file caller exists for any character-sheet helper.
- **Pip-Boy — one real choke point**, `PipboyContentOrigin(out Point po, out int lh)`
  (`Panels.cs:744-750`), consumed by `PipboyRowRect(int index)` (`:784-789`) and directly by
  `DrawPipboy`/`PipboyTabAt`/`DrawPipboyArchives` — no second, independent copy exists. **No
  separate fallback method**: `DrawPipboy()` gates art-vs-plain-rect with `if (_pipboyBg is not
  null) {...} else {...}` inline in the same function (`Panels.cs:831-837`), both branches already
  viewport-relative through the same `PipboyContentOrigin()` call — same shape as Save/Load in
  Stage 3a, so the whole method scales, no split needed. `PipboyRowRect` has a caller in
  `ViewerGame.Harness.cs:1907` (a scripted `MenuClick` CLI test action) — confirmed this call site
  runs against a real, live `GraphicsDevice.Viewport` (1280×720, the Viewer's actual default back
  buffer size, not a headless stub — `Harness.cs`'s `RunStartupActions()` runs from the real
  `LoadContent()` after `Initialize()` has already stood up the real `GraphicsDeviceManager`), so
  converting `PipboyRowRect` to `VirtualViewport()` keeps the harness's simulated click and the
  real Draw call automatically consistent — both read the same helper against the same live
  viewport. `DrawPipboy`'s hover-highlight reads `Mouse.GetState()` directly inside Draw
  (`Panels.cs:883`) rather than through the shared `mouse`/`uiMouse` locals — this needs converting
  to `UiMouse()` just like the in-Draw hover reads in Stage 3a's Skilldex/perk-picker. Note:
  `DrawPipboyMiniMap` (`Panels.cs:1035`) is confirmed dead code (zero callers, superseded per its
  own doc comment) — out of scope, not touched by this stage.
- **Options — two independent copies**: the named helper `OptionsRowRect(int index)`
  (`Panels.cs:1156-1164`, its own `Rectangle vp = GraphicsDevice.Viewport.Bounds;` at `:1158`) and
  a second, separate inline computation directly inside `DrawOptions()` (`Panels.cs:1212-1213`,
  `Rectangle vp = GraphicsDevice.Viewport.Bounds; int px = ..., py = ...;`) that duplicates only
  the origin half of `OptionsRowRect`'s math from its own locally-declared `ow`/`oh`. Both must
  convert together. **No separate fallback method** — same same-function gate shape as Pip-Boy
  (`Panels.cs:1215-1221`), so the whole method scales. `OptionsRowRect` has a caller in
  `ViewerGame.Harness.cs:1897` (another scripted `MenuClick` action) — same live-viewport
  confirmation as Pip-Boy applies. `DrawOptions`'s hover read is also a direct
  `Mouse.GetState()` call inside Draw (`Panels.cs:1223`), needing the same `UiMouse()` conversion.
- **Automap — two independent copies, textually identical, neither delegating to the other**:
  `AutomapButtons()` (`Panels.cs:1069-1077`) and `DrawAutomap()`'s own inline block
  (`Panels.cs:1085-1087`) both compute
  `Rectangle vp = GraphicsDevice.Viewport.Bounds; int w = _automapBg?.Width ?? 519, h =
  _automapBg?.Height ?? 480; var o = new Point(Math.Max(0, (vp.Width - w) / 2), Math.Max(0,
  (vp.Height - h) / 2));` — both must convert together. **No separate fallback method** — same
  same-function gate shape (`Panels.cs:1089-1092`). No cross-file caller exists for
  `AutomapButtons`. Automap has no in-Draw hover read (no `Mouse.GetState()` call anywhere inside
  `DrawAutomap()` — the button hints are static text, not hover-colored).
- **A real, pre-existing same-frame overlap this stage's character-sheet migration incidentally
  fixes**: pressing `G` while the character sheet is open sets `_perkPickOpen = true`
  (`ViewerGame.cs:2153-2154`) **without** clearing `_skillAllocOpen`, and `Draw()` calls
  `DrawSkillAllocator(); DrawPerkPicker();` unconditionally (each self-gates on its own flag) — so
  for the one frame the key is pressed, both screens render in the same frame. Since the perk
  picker is already scaled (Stage 3a) and the character sheet currently is not, this is *today* a
  visible one-frame flash of mismatched scale; once this stage lands, both screens scale
  identically and the flash disappears as a side effect. This is a pre-existing app behavior, not
  something this stage needs to actively "fix" beyond migrating the character sheet — call it out
  so nobody mistakes the pre-existing overlap for a new bug introduced by this stage.
- No SpriteBatch-nesting risk from this overlap either way: `DrawSkillAllocator()` and
  `DrawPerkPicker()` are sequential, non-nested calls (`ViewerGame.cs:5806-5807`), each opening and
  fully closing its own scoped batch before the next call — the same pattern already proven safe
  across every pair of screens in Stage 2/3a.

## Design

Apply the established technique per screen, same as Stage 2/3a:

1. **Character sheet**: scope a `SpriteBatch` block around `DrawSkillAllocator`'s art path only
   (after the `_charBg is null` fallback check, mirroring `DrawSkilldex`/`DrawPerkPicker`).
   Convert all three origin copies (`DrawSkillAllocator`, `CharSheetItemAt`,
   `Update()`'s DONE/CANCEL check) to `VirtualViewport()` in the same change — a partial
   conversion (e.g. converting the render but not the hit-test) would make clicks land on the
   wrong pixels. Leave `DrawSkillAllocatorFallback()` untouched.
2. **Pip-Boy, Options, Automap**: scope a `SpriteBatch` block around the **entire** `Draw*` method
   (no fallback to carve out, per the grounding above) — same shape as Stage 3a's Save/Load.
   Convert every origin computation (`PipboyContentOrigin`; `OptionsRowRect` AND `DrawOptions`'s
   own duplicate; `AutomapButtons` AND `DrawAutomap`'s own duplicate) to `VirtualViewport()`.
3. **Mouse reads**: every position read (not button-state) inside a scaled block switches to
   `UiMouse()` — including the three screens' in-Draw hover reads that currently call
   `Mouse.GetState()` directly (Pip-Boy, Options) — and every `Update()`-side hit-test reuses the
   existing `uiMouse` local (Character sheet's `CharSheetItemAt`/DONE-CANCEL, Pip-Boy's
   `PipboyTabAt`/`PipboyRowAt`, Options' `OptionsRowAt`, Automap's three button `.Contains` checks).
4. **`ViewerGame.Harness.cs` needs no code change** — `PipboyRowRect`/`OptionsRowRect` are called
   there, but since the harness runs against the same live `GraphicsDevice.Viewport` Draw does,
   converting the helper itself (not the call site) keeps both consistent automatically. Confirm
   this holds with a manual check (a harness-driven `--menu-click pipboy N` / `--menu-click
   options N` invocation still selects the visually-correct row post-conversion).

## Non-goals

- Inventory (its own, separately-scoped future plan — see Stage 3a's spec for why).
- Preferences, aim dialog, tactics (Stage 3c).
- The HUD bar, action menu, elevator picker (Stage 4) and worldmap chrome (Stage 5).
- `DrawPipboyMiniMap` — confirmed dead code, zero callers; not touched.
- Actively "fixing" the character-sheet/perk-picker same-frame overlap beyond what migrating the
  character sheet naturally resolves (both render at the same scale afterward) — no state-machine
  change to `_skillAllocOpen`/`_perkPickOpen` is in scope here.

## Testing

Same as Stage 2/3a: no new pure-math logic, so verification is the existing golden suites
(headless, unaffected) plus a manual visual/screenshot check per screen at the Viewer's default
1280x720 window — confirming each screen renders at the same 1.5× scale as the already-scaled
screens, centered, undistorted, with hover/click landing under the cursor. For Pip-Boy and
Options specifically, additionally verify a `--menu-click`-style harness invocation still
resolves to the visually-correct row after the `VirtualViewport()` conversion (per point 4 above).
For the character sheet specifically, verify all three origin copies stayed in lockstep by
confirming a click on a visible stat/skill row actually selects that row (not an adjacent one) at
a non-native window size.
