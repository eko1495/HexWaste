# UI Scale — Stage 3a: Simple Single-Helper Panels

## Problem

Stage 2 (shipped: `2e4f8d2`, `d01fba9`, `3bc59b6`) wired the shared `UiScale` infrastructure into
the dialog panel and the main-menu family — both now render at a uniform scale that fills a
non-4:3 window, matching `fallout2-ce`'s fullscreen stretch. Every other screen still renders at
native, unscaled size. A pre-implementation survey of the eleven remaining modal screens
(inventory, character sheet, Skilldex, Pip-Boy, automap, options, preferences, save/load, aim
dialog, tactics, perk picker) found ~20 separate origin/viewport helpers across them — several
duplicated inline two or three times, two called from the CLI test harness in a third file — too
large and too varied to wire in one pass without repeating Stage 2's near-miss (a shared helper
switched on a false "every caller is in-scope" assumption, caught only by the final whole-branch
review). The eleven screens are being split into three smaller, independently-shippable specs by
risk/complexity. **This spec covers Stage 3a only: the four screens whose geometry is
self-contained — one screen, one small helper family, no callers outside that screen's own
file(s), no CLI-harness dependency:**

- **Skilldex** (`DrawSkilldex`, `ViewerGame.Panels.cs`)
- **Perk picker** (`DrawPerkPicker`, `ViewerGame.Panels.cs`)
- **Save/Load** (`DrawSaveLoad`, `ViewerGame.Panels.cs`)

Stage 3b (character sheet, Pip-Boy, options, automap — screens with duplicated inline origin math
or a CLI-harness caller) and Stage 3c (preferences, aim dialog, tactics — screens with same-frame
overlap concerns) are separate, later specs.

**Post-planning correction:** Inventory was originally scoped into this spec as a fourth
"self-contained" screen, but writing the implementation plan found its fallback is not one
separable branch the way Skilldex/perk-picker's is — `InvBoxOrigin()` returns `Point?` and roughly
six different helper methods each independently branch on that null with their own **fixed,
viewport-independent** fallback positions (e.g. `new Rectangle(420, 96, 90, 60)`), plus a
drag-ghost icon drawn at the raw, untransformed mouse position. That is a meaningfully different
and higher-risk shape than the other three screens' single-helper, single-fallback pattern.
Inventory is deferred to its own follow-up plan; this spec's remaining three screens (Skilldex,
perk picker, save/load) are unaffected and are what
`docs/superpowers/plans/2026-09-06-ui-scale-stage3a.md` implements.

## Grounding (confirmed by a direct source-tree survey this session)

- All eleven modal screens' `Draw*` methods — including this spec's four — are invoked from the
  **same shared per-frame `SpriteBatch.Begin/End` block** already established in Stage 2
  (`ViewerGame.cs:5769`–`5821`, the `else` branch of `if (_worldmapOpen)`), sequentially with
  `DrawDialogPanel()` and `DrawTextOverlay()`. None of them has its own separate `Begin`/`End`
  today, and none is gated by `_worldmapOpen`.
- **None of this spec's four screens shares an origin helper with any other screen** (confirmed
  by a `grep -rn` of each helper's exact name across the whole `src/Hexwaste.Viewer/` tree — the
  full caller list for each is below). This is the opposite risk shape from Stage 2's
  `MenuOrigin()` miss: here the risk isn't "a hidden caller in another screen," it's "the same
  screen recomputes its origin twice, inline, in two different methods, and only one copy gets
  converted" — Inventory/Skilldex/Perk-picker/Save-Load do NOT have this problem (each has a
  single named helper, single definition, all callers local) — Stage 3b's screens are exactly
  where that duplicate-inline-copy risk lives, which is part of why they're split out.
- **Skilldex** — origin helper `SkilldexOrigin(out boxW, out boxH, out btnW, out btnH)`
  (`ViewerGame.Panels.cs:604`); every caller (`SkilldexRowAt` at `:618`, `DrawSkilldex` itself at
  `:647`) is local to Skilldex. Skilldex DOES have a separate text-only fallback,
  `DrawSkilldexTextFallback` (`:683`), with its own **independent** inline viewport read at `:688`
  that does not reuse `SkilldexOrigin()` — matching Stage 2's precedent (the main-menu family's
  art-missing fallback stayed unscaled), this fallback is explicitly out of scope: only the
  art-path (`SkilldexOrigin()` and everything derived from it) scales. `DrawSkilldex` also reads
  the mouse directly, inside Draw, for hover highlighting: `Panels.cs:649`
  (`MouseState m = Mouse.GetState(); int hovered = SkilldexRowAt(m.X, m.Y);`) — this is a second,
  independent raw-mouse read beyond the `Update()`-side hit-test at `ViewerGame.cs:2209`
  (`SkilldexRowAt(mouse.X, mouse.Y)`), and both need converting.
- **Perk picker** — origin helper `PerkWindowOrigin(out rowH, out rowsShown, eligCount)`
  (`Panels.cs:505`); every caller (`PerkPickerRowAt` at `:520`, `DrawPerkPicker` at `:545`) is
  local. Also has a text fallback, `DrawPerkPickerTextFallback` (`:577`), out of scope for the
  same reason as Skilldex's. Draw-side hover read: `Panels.cs:551`
  (`Mouse.GetState()` → `PerkPickerRowAt`). Update-side hit-test: `ViewerGame.cs:2140`
  (`PerkPickerRowAt(mouse.X, mouse.Y)`, inside `if (_perkPickOpen)`). The CLI test harness
  (`ViewerGame.Harness.cs:1389-1398`) has a scripted perk-pick action but selects **by index, not
  by simulated screen-coordinate click** — it does not call `PerkWindowOrigin`/`PerkPickerRowAt`
  and is unaffected by this stage.
- **Save/Load** — origin helpers `SaveLoadPanelRect()` (`Panels.cs:1291`) and
  `SaveLoadSlotRect(slot)` (`:1306`); every caller (`SaveLoadSlotAt` hit-test at `:1318`,
  `DrawSaveLoad`'s render loop at `:1332`/`:1351`) is local. No text-fallback branch — the panel
  is always drawn (a flat/tinted background, not baked art gated behind a null check), so the
  whole `DrawSaveLoad()` call scales. Draw-side hover read: `Panels.cs:1347`
  (`Mouse.GetState()` → `SaveLoadSlotAt`). Update-side hit-test: `ViewerGame.cs:2317`
  (`SaveLoadSlotAt(mouse.X, mouse.Y)`, inside `if (_saveLoadOpen)`). Note: Save/Load can be
  reached from the main-menu's LOAD GAME button (`ViewerGame.cs:2026-2028`, a pre-existing guard
  ensures Save/Load's own `Update()` handler runs instead of the menu's when both are
  technically open) — this is a *state* overlap the code already resolves; it is not a *drawing*
  overlap this stage needs to handle specially, since `DrawSaveLoad()` and `DrawTextOverlay()` are
  sequential, non-nested calls in `Draw()` exactly like every other pair of screens.
- **Reused, not re-declared:** Stage 2 (Task 1) already added a `Point uiMouse = UiMouse();` local
  in `Update()`, recomputed once per frame right after `MouseState mouse = Mouse.GetState();`
  (`ViewerGame.cs:1935`-ish). This stage's four `Update()`-side hit-test call sites read from that
  same existing local — no new per-frame mouse sampling is introduced.

## Design

For each of the three screens, apply the same technique Stage 2 established:

1. **Scope a `SpriteBatch` block around the screen's own `Draw*` call.** Following the
   `DrawDialogPanel` precedent (`ViewerGame.cs:6191`-ish): guard with an early return when the
   screen isn't open (no wasted `End`/`Begin` churn on the common closed-panel frame), then
   `_spriteBatch.End(); _spriteBatch.Begin(samplerState: SamplerState.PointClamp,
   transformMatrix: UiScaleMatrix()); …draw…; _spriteBatch.End(); _spriteBatch.Begin(samplerState:
   SamplerState.PointClamp);` around the content. For Skilldex and Perk picker, the scoped block
   must enclose ONLY the art-path draw (mirroring Stage 2's `DrawTextOverlay` restructure for the
   main-menu family): call the existing art-vs-fallback dispatch, and if it reports "no art,"
   fall through to the text-fallback call in the resumed, unscaled batch — exactly as
   `DrawAuthenticMainMenu`'s `bool` return already gates today. Save/Load has no separate fallback
   method to carve out this way (see below), so its whole `DrawSaveLoad()` call scales.
2. **Switch every named origin helper's viewport read from `GraphicsDevice.Viewport(.Bounds)` to
   `VirtualViewport()`** (Stage 1, `ViewerGame.UiScale.cs`): `SkilldexOrigin()`,
   `PerkWindowOrigin()`, `SaveLoadPanelRect()`/`SaveLoadSlotRect()`. Do NOT touch
   `SkilldexOrigin()`'s or `PerkWindowOrigin()`'s fallback siblings
   (`DrawSkilldexTextFallback`/`DrawPerkPickerTextFallback`'s own independent inline viewport
   reads) — those stay on the raw device viewport, unscaled, per point 1. `SkilldexOrigin()`
   additionally anchors to `_hudBarHeight`, a fixed device-pixel constant (the HUD bar's native,
   still-unscaled texture height) — that value must be converted to virtual-canvas units
   (divided by the current scale) before use inside the scaled block, or the transform would
   double-apply to it.
3. **Switch every mouse read used for position (not button state) inside a scaled block to
   `UiMouse()`**: the three Draw-side hover reads (Skilldex `:649`, Perk picker `:551`, Save/Load
   `:1347`) and the `Update()`-side hit-tests (Skilldex's one, Perk picker's one, Save/Load's one)
   — reusing the existing `uiMouse` local for the `Update()` sites, and a fresh `UiMouse()` call
   for the Draw-side hover reads (matching how `DrawConversationPanel`'s hover loop already does
   this in Stage 2).
4. **Leave every raw viewport/mouse reference inside a fallback path exactly as it is today** —
   except Save/Load's, which has no separable fallback path (see point 1) and scales uniformly.

## Non-goals

- Inventory — deferred to its own follow-up plan (see "Post-planning correction" above).
- Character sheet, Pip-Boy, options, automap (Stage 3b — duplicated inline origin math, and two
  helpers with a `ViewerGame.Harness.cs` caller: `PipboyRowRect` and `OptionsRowRect`).
- Preferences, aim dialog, tactics (Stage 3c — preferences is reachable from the main-menu state
  and its `Draw`/`Update` sequencing with `DrawTextOverlay` needs closer attention; aim dialog has
  two independent origin families (fallback vs. called-shot art window); tactics has no unusual
  risk but is grouped with this batch for a manageable spec size).
- The HUD bar, action menu, elevator picker (Stage 4) and worldmap chrome (Stage 5).
- `MenuOrigin()`/`MenuOriginDevice()` (Stage 2, already shipped and out of scope for every later
  stage — none of Stage 3a's screens use either).
- Any change to `ViewerGame.Harness.cs` — none of Stage 3a's three screens has a harness caller of
  its geometry helpers (confirmed: the harness's perk-pick action selects by index, not by
  simulated click).

## Testing

Same as Stage 2: no new pure-math logic (Stage 1's `UiScale` is unit-tested and untouched), so
verification is the existing golden suites (headless, unaffected — none of this is reachable
without `Draw()`) plus a manual visual/screenshot check per screen at the Viewer's default
1280x720 window, confirming: the screen renders at the same 1.5× scale as the dialog panel and
main-menu family, centered, undistorted, with hover/click still landing under the cursor, and (for
Skilldex specifically) still sitting flush above the HUD bar's own native-sized top edge. For
Skilldex and Perk picker specifically, also confirm the (live-reachable-only-when-art-missing)
text fallback still renders at native size, unscaled — matching the Stage 2 precedent this spec's
design section calls for.
