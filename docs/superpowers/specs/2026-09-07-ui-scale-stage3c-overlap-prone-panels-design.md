# UI Scale — Stage 3c: Preferences, Aim Dialog, Tactics

## Problem

Stage 2/3a/3b (shipped) wired the shared `UiScale` infrastructure into the dialog panel, the
main-menu family, Skilldex, the perk picker, save/load, the character sheet, Pip-Boy, options,
and automap — all nine now render at a uniform scale filling non-4:3 windows, matching
`fallout2-ce`'s stretch. This spec covers the last batch of the originally-planned Stage 3 split:
**preferences, the aim/called-shot dialog, and the tactics window** — grouped together because
each carries a same-frame co-activation risk with an already-scaled screen, or an internal
dual-coordinate-system hazard, that the earlier batches didn't have.

- **Preferences** (`DrawPreferences`/`UpdatePreferences`, `src/Hexwaste.Viewer/ViewerGame.Preferences.cs`)
- **Aim dialog** (`DrawAimDialog`/`DrawAimDialogFallback`, `src/Hexwaste.Viewer/ViewerGame.Panels.cs`)
- **Tactics** (`DrawTactics`, `src/Hexwaste.Viewer/ViewerGame.Tactics.cs`)

Inventory remains its own deferred plan. Stage 4 (HUD bar/action menu/elevator picker) and Stage
5 (worldmap chrome) are next after this.

## Grounding (confirmed by a direct source-tree survey this session)

- All three screens' `Draw*` methods are invoked from the same shared per-frame
  `SpriteBatch.Begin/End` block (`ViewerGame.cs`, the `else` branch of `if (_worldmapOpen)`),
  unconditionally and self-guarding, exactly like every screen migrated so far.
- **Preferences** has a single origin helper, `PrefWindowPos()` (`Preferences.cs:45-50`,
  `Viewport vp = GraphicsDevice.Viewport; ... return new Point((vp.Width - (bg?.Width ?? 640)) /
  2, (vp.Height - (bg?.Height ?? 480)) / 2);`), with exactly two callers — `UpdatePreferences`
  (`:80`) and `DrawPreferences` (`:141`) — both local to this file. No separate fallback method:
  `DrawPreferences()` (`:137-190`) gates art-vs-plain-rect inline (`:143-149`), both branches
  already sharing `PrefWindowPos()`'s viewport-relative math (same shape as Stage 3a's save/load),
  so the whole method scales.
  - **The real complication**: `_preferencesOpen`'s `Update()` handler is *hoisted* above the
    normal per-screen dispatch (`ViewerGame.cs:2015-2022`, "runs in ANY state, incl. the main
    menu's OPTIONS button") and calls `UpdatePreferences(keyboard, mouse)` with the **raw**
    `MouseState mouse` — not position ints, a whole `MouseState`. Since `OpenPreferences()` is
    reachable from the Title screen's OPTIONS button, **the already-scaled main-menu family and
    the (currently unscaled) Preferences window routinely render in the same frame**
    (`DrawTextOverlay()` at `ViewerGame.cs:5804` paints the scaled Title screen; `DrawPreferences()`
    at `:5813` paints Preferences on top). This is not a new risk Stage 3c introduces — it already
    exists today, with Preferences unscaled and the Title screen scaled since Stage 2; Stage 3c's
    job is to make Preferences scale identically so the two agree.
- **Aim dialog** has TWO independent origin families for the same logical screen — this is a
  genuine art-vs-fallback dispatch (not a same-function boolean gate):
  ```csharp
  // The art-mode ("called-shot") family:
  private Point CalledShotWindowPos() => new(
      (GraphicsDevice.Viewport.Width - CalledShotW) / 2,
      Math.Max(0, (GraphicsDevice.Viewport.Height - CalledShotH) / 2));          // Panels.cs:1605-1607
  private Rectangle CalledShotButtonRect(int row) { ... }                        // Panels.cs:1611-1615
  private Rectangle CalledShotCancelRect() { ... }                               // Panels.cs:1617-1621

  // The text-fallback family:
  private Rectangle AimDialogPanelRect() {
      Rectangle vp = GraphicsDevice.Viewport.Bounds; ...
  }                                                                              // Panels.cs:1568-1574
  private Rectangle AimDialogRowRect(int row) { ... }                           // Panels.cs:1576-1581
  ```
  `DrawAimDialog()` (`Panels.cs:1676-1686`) dispatches exactly like Skilldex/perk-picker: `if (bg
  is null || digits is null) { DrawAimDialogFallback(); return; }`. Per the established
  precedent, only the art-path family (`CalledShotWindowPos`/`ButtonRect`/`CancelRect`) scales;
  `AimDialogPanelRect`/`AimDialogRowRect`/`DrawAimDialogFallback()` stay unscaled — even though
  the fallback rects also happen to read the viewport, the same "degraded path stays native"
  rule Stage 2/3a already established for Skilldex's/the perk picker's fallback applies uniformly
  here regardless of whether the fallback's own math is viewport-relative.
  - **The real complication**: `CalledShotHitAt(int mx, int my)` (`Panels.cs:1625-1633`) itself
    branches on art presence and tests the SAME `(mx, my)` against either the (to-be-scaled)
    `CalledShotButtonRect`/`CancelRect` or the (staying-unscaled) `AimDialogRowRect`, depending on
    which is live: `if (InterfaceFrm(CalledShotBgFrmId) is null) return AimDialogRowAt(mx, my); ...`.
    Its one caller, `HandleAimDialogInput` (`Panels.cs:1635-1657`), already independently computes
    `bool artMode = InterfaceFrm(CalledShotBgFrmId) is not null;` right after calling
    `CalledShotHitAt` (`:1651`) — for a different purpose (deciding whether row 8 means "cancel").
    That same check must move earlier and additionally decide **which mouse coordinate space to
    pass into `CalledShotHitAt`**: `UiMouse()`'s point when art is live (art branch expects
    virtual-canvas coordinates), the raw `mouse` point when it isn't (fallback branch expects
    device coordinates) — see Design point 3.
- **Tactics** has a single, same-function boolean gate (`_tacticsArt`) inside both
  `TacticsPanelRect()`/`TacticsRowRect()` (`Tactics.cs:95-118`) and `DrawTactics()`
  (`:159-193`, `if (_controlFrm is not null) {...} else {...}` at `:173-182`) — exactly the
  Stage 3a save/load shape. All three helpers' callers (`TacticsRowAt`, `DrawTactics`) are local
  to `ViewerGame.Tactics.cs`; no cross-file caller anywhere.
- No cross-file (`ViewerGame.Harness.cs`/`ViewerGame.Endgame.cs`-style) surprise caller exists for
  any helper touched in this stage — confirmed by a fresh whole-tree `grep` per screen.
- **Aim dialog and tactics can co-render in the same frame** — `_aimDialogOpen` and
  `_tacticsMember is not null` are independent flags with no mutual exclusion; both
  `DrawAimDialog()` (`ViewerGame.cs:5815`) and `DrawTactics()` (`:5816`) are unconditional,
  self-guarding calls back-to-back. This is a pre-existing (not new) overlap — both windows
  already center independently via the raw device viewport today and can already visually
  overlap; converting both to `VirtualViewport()` preserves that exact relationship (both center
  in the virtual canvas instead), introducing no new positional conflict. Stage 3c does not need
  to resolve this overlap, only preserve it.

## Design

1. **Preferences**: scope the entire `DrawPreferences()` method in one scoped, scaled `SpriteBatch`
   block (no fallback to carve out, matching save/load's shape). Convert `PrefWindowPos()` to
   `VirtualViewport()`. At the `Update()` call site (`ViewerGame.cs:2017`), build a UI-scaled
   synthetic `MouseState` — the same technique Stage 2's main-menu family used
   (`new MouseState(uiMouse.X, uiMouse.Y, mouse.ScrollWheelValue, mouse.LeftButton,
   mouse.MiddleButton, mouse.RightButton, mouse.XButton1, mouse.XButton2,
   mouse.HorizontalScrollWheelValue)`) — and pass that into `UpdatePreferences` instead of the
   raw `mouse`, since `UpdatePreferences` takes a whole `MouseState`, not bare ints.
2. **Aim dialog**: scope `DrawAimDialog`'s art path (after its `bg is null || digits is null`
   check) in its own scoped, scaled block, converting `CalledShotWindowPos`/`ButtonRect`/
   `CancelRect` to `VirtualViewport()`. Leave `AimDialogPanelRect`/`AimDialogRowRect`/
   `DrawAimDialogFallback()` completely untouched and unscaled. In `HandleAimDialogInput`,
   reorder so `bool artMode = InterfaceFrm(CalledShotBgFrmId) is not null;` is computed BEFORE
   calling `CalledShotHitAt`, and pass `artMode ? uiMouse.X : mouse.X, artMode ? uiMouse.Y :
   mouse.Y` into it — this is the one place in this stage where the mouse coordinate space itself
   is conditional on which internal branch will fire, and it must be resolved by the caller, not
   inside `CalledShotHitAt` (which has no way to know which space its caller intends without this
   reordering). `DrawAimDialog`'s in-Draw hover reads (`Mouse.GetState()`, used for hover and for
   `cancelPressed`) switch unconditionally to `UiMouse()` since by that point in the method the
   art branch has already been confirmed live.
3. **Tactics**: scope the entire `DrawTactics()` method in one scoped, scaled block (no fallback
   to carve out, matching save/load's and preferences' shape). Convert `TacticsPanelRect()`/
   `TacticsRowRect()` to `VirtualViewport()`. Route `HandleTacticsInput`'s hit-test and
   `DrawTactics`'s in-Draw hover read through `uiMouse`/`UiMouse()`.
4. In all three cases, every `Update()`-side hit-test reuses the existing `uiMouse` local
   (declared once, Stage 2 Task 1); no new per-frame mouse sampling is introduced.

## Non-goals

- Inventory (its own, separately-scoped future plan).
- The HUD bar, action menu, elevator picker (Stage 4) and worldmap chrome (Stage 5).
- Resolving the pre-existing aim-dialog/tactics same-frame overlap, or the pre-existing
  Preferences/main-menu same-frame overlap — both are preserved exactly as they already behave,
  not fixed.
- Any change to `ViewerGame.Harness.cs` — no helper touched in this stage has a caller there.

## Testing

Same as every prior stage: no new pure-math logic, so verification is the existing golden suites
(headless, unaffected) — **run `scripts/encounter-golden.sh`, not just `scripts/opening-golden.sh`**,
per the lesson Stage 3b's final review surfaced (the opening suite doesn't exercise every
touched screen's scripted CLI paths; check which suite actually covers aim-dialog/tactics/
preferences scenarios before treating a pass as complete evidence) — plus a manual
visual/screenshot check per screen at the Viewer's default 1280x720 window. For the aim dialog
specifically, verify BOTH the art-mode called-shot window (scaled) and, if reachable, the
text-fallback rows (unscaled) render and hit-test correctly — and specifically verify a click
still selects the correct body-location row when the art mode is live, proving the
`HandleAimDialogInput` coordinate-space reordering (Design point 2) works. For preferences,
verify a screenshot taken from the Title-menu OPTIONS path shows both the (scaled) main menu
and the (now also scaled) preferences window agreeing in scale, not one native-sized against the
other.
