# UI Scale — Stage 2: Dialog + Main Menu

## Problem

Stage 1 (shipped, `2dc11d0`/`3bb2c2a`) built `UiScale.ComputeScale`/`ComputeVirtualViewport`/`TransformMouse`
and the `ViewerGame` wrappers `UiScale()`/`VirtualViewport()`/`UiMouse()` — verified inert,
zero call sites. Stage 2 wires that infrastructure into the two screens already compared
against `fallout2-ce` this session: the **dialog panel** (`DrawConversationPanel`,
`ViewerGame.cs`) and the **main menu family** (`DrawAuthenticMainMenu` plus its sibling
screens `DrawAuthenticSelector`/`DrawAuthenticCreation` in `ViewerGame.Shell.cs`, which
share the same `MenuOrigin()` convention and the same input-routing block). No other
screen (inventory, Skilldex, Pip-Boy, HUD bar, worldmap chrome, etc.) changes in this
stage — those are later, separately-specced stages.

## Grounding (confirmed by direct code read this session)

- **One shared `SpriteBatch.Begin`/`End` pair wraps the entire UI frame.**
  `ViewerGame.cs:5756` opens `_spriteBatch.Begin(samplerState: SamplerState.PointClamp)`
  (no transform) and `ViewerGame.cs:5808` closes it. Between them, in order:
  `DrawInterfaceBar()`, `DrawTextOverlay()` (which internally dispatches to
  `DrawAuthenticMainMenu`/`DrawAuthenticSelector`/`DrawAuthenticCreation`/`DrawCredits`),
  `DrawDialogPanel()` (→ `DrawConversationPanel`), then every other modal
  (`DrawItemPanels`, `DrawSkillAllocator`, `DrawPerkPicker`, `DrawSkilldex`, `DrawPipboy`,
  `DrawAutomap`, `DrawOptions`, `DrawPreferences`, `DrawSaveLoad`, `DrawAimDialog`,
  `DrawTactics`), then `DrawMapFade`/`DrawActionMenu`/`DrawElevatorPicker`/`DrawMouseCursor`.
  A separate, unrelated `Begin(transformMatrix: WorldZoomMatrix())` at `ViewerGame.cs:5746`
  handles only the world/map layer and is untouched by this spec.
- **A `transformMatrix:` on that one shared `Begin` would scale every screen in the
  block, not just dialog and menu.** Since Stage 2 must not touch inventory/Skilldex/
  Pip-Boy/etc. yet (their own position math still assumes the real, unscaled viewport),
  the shared `Begin` cannot simply gain a scale matrix. Stage 2 therefore **splits this
  one `Begin`/`End` into three consecutive scoped blocks** for the duration of this
  stage (later stages will fold more screens into the scaled block, until eventually
  only one scaled block remains and this split is undone):
  1. Unscaled block: `DrawInterfaceBar()` (HUD bar — later stage).
  2. **Scaled block** (`transformMatrix: Matrix.CreateScale(UiScale())`): the main-menu
     family draw calls and `DrawDialogPanel()` — see "Isolating the scaled draws" below.
  3. Unscaled block: every remaining modal, in the same order as today.

  Multiple `Begin`/`End` pairs per frame are an ordinary, supported MonoGame pattern
  (already used in this file for the screenshot/thumbnail paths at `ViewerGame.cs:5814`,
  `:7207`, `:7225`) — this is not a new technique for this codebase, just a new use of it
  for the live frame.
- **Isolating the scaled draws inside `DrawTextOverlay()`**: `DrawTextOverlay()` is a
  larger dispatcher than just the main-menu family — the implementer must read its
  current body and confirm exactly which branches are `DrawAuthenticMainMenu`/
  `DrawAuthenticSelector`/`DrawAuthenticCreation`/`DrawCredits` (all in scope, all share
  `MenuOrigin()`) versus any other content it draws that is out of scope for this stage.
  If `DrawTextOverlay()` mixes in-scope and out-of-scope drawing in one method, extract
  the four in-scope calls into their own direct call from `Draw()`, inside the scaled
  block, rather than wrapping the whole `DrawTextOverlay()` call. This is a required
  verification step, not an assumption this spec makes.
- **Viewport-derived positioning to change**, all in the in-scope screens only:
  - `ViewerGame.Shell.cs:42-46` `MenuOrigin()` — currently
    `Viewport vp = GraphicsDevice.Viewport; return ((vp.Width - 640) / 2, (vp.Height - 480) / 2);`
    → replace `vp` with `VirtualViewport()`.
  - `ViewerGame.cs:6485-6491` inside `DrawConversationPanel` — currently
    `Rectangle viewport = GraphicsDevice.Viewport.Bounds; ... frameX = (viewport.Width - 640) / 2; frameY = (viewport.Height - 480) / 2;`
    and the full-viewport dim rectangle at `:6498` and `panelWidth`'s
    `Math.Min(720, viewport.Width - 40)` clamp at `:6503` → all four uses of
    `viewport` in this method switch to `VirtualViewport()`.
  - Nothing else in this stage (Panels.cs's two other `(vp.Width-640)/2` sites and the
    character-sheet click mapping at `ViewerGame.cs:2164` are out of scope — later stage).
- **Mouse hit-testing to change**, all reading the raw `MouseState mouse = Mouse.GetState();`
  local declared once in `Update()` at `ViewerGame.cs:1934` and threaded through:
  - `ViewerGame.cs:2533` and `:2562` — `HitTestDialogOption(mouse.X, mouse.Y)` for
    scripted-dialog and companion-hub option picking.
  - `ViewerGame.cs:6557` inside `DrawConversationPanel`'s hover-highlight loop — a
    *second*, independent `Mouse.GetState()` read used only for hover, same rectangles.
  - `ViewerGame.Shell.cs:129` inside `HandleMenuMouse(MouseState mouse)` —
    `_menuHover = MenuButtonAtLocal(mouse.X - ox, mouse.Y - oy);` (called from
    `ViewerGame.cs:2032` with the same raw `mouse` local) and the two sibling
    handlers `HandleSelectorMouse` (`Shell.cs:321-336`) and `HandleCreationMouse`
    (`Shell.cs:688-737`), called at `ViewerGame.cs:2033-2034` — all three share
    `MenuOrigin()` and are in scope together as "the main menu family."
  - `ViewerGame.Shell.cs:88` inside `DrawAuthenticMainMenu()` reads `Mouse.GetState()`
    a third time, but only for `.LeftButton` (pressed-art swap) — no `.X`/`.Y` used,
    so **no change needed** there; call this out explicitly so the implementer doesn't
    "fix" a call site that isn't broken.
  - In every case above, replace `mouse.X, mouse.Y` (or `mouse.X - ox, mouse.Y - oy`)
    with the transformed equivalent: call `UiMouse()` once per `Update()` tick (not
    once per call site) and pass its `.X`/`.Y` into the existing hit-test functions
    unchanged. `HitTestDialogOption`, `MenuButtonAtLocal`, and the option-rect/button-rect
    construction in `DrawConversationPanel`/`Shell.cs` all keep comparing against
    rectangles built from `VirtualViewport()` — only the point being tested changes from
    raw device pixels to `UiMouse()`'s virtual-canvas pixels.

## Design

1. **Scaled `SpriteBatch` block**: after `DrawInterfaceBar()` and before the rest of
   the modal list, add:
   ```csharp
   _spriteBatch.End();
   _spriteBatch.Begin(samplerState: SamplerState.PointClamp,
       transformMatrix: Matrix.CreateScale(UiScale()));
   // main-menu family + DrawDialogPanel() calls go here
   _spriteBatch.End();
   _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
   // remaining modal draw calls continue unchanged
   ```
   This is a pure reordering/splitting of existing draw calls — no call is added,
   removed, or reordered relative to its neighbors within its own screen family.
2. **`MenuOrigin()` and `DrawConversationPanel`'s local `viewport`** switch their
   viewport source from `GraphicsDevice.Viewport`/`.Bounds` to `VirtualViewport()`
   (Stage 1's wrapper), per the grounding section above. Every rectangle built from
   that origin (menu button rects, dialog frame/panel rects, the dim overlay, the
   `panelWidth` clamp) is therefore expressed in virtual-canvas coordinates — which is
   exactly what the scaled `SpriteBatch` block expects, since `Matrix.CreateScale(UiScale())`
   maps virtual-canvas coordinates back onto the real screen.
3. **Mouse position transform**: call `UiMouse()` once in `Update()` alongside the
   existing raw `Mouse.GetState()` call at `ViewerGame.cs:1934`, and pass its `.X`/`.Y`
   into the four in-scope hit-test call sites (`HitTestDialogOption` ×2,
   `HandleMenuMouse`/`HandleSelectorMouse`/`HandleCreationMouse`'s `mouse.X`/`mouse.Y`
   uses) instead of the raw `mouse.X`/`mouse.Y`. Leave every other use of the raw
   `mouse` local (button-state checks, out-of-scope screens' hit-tests) untouched.
   `DrawConversationPanel`'s internal hover-loop `Mouse.GetState()` read (`:6557`)
   gets the same treatment — replace with `UiMouse()`.
4. **`DrawAuthenticMainMenu()`'s own `Mouse.GetState()` read** (`Shell.cs:88`) is
   explicitly left alone (see grounding) — it never reads `.X`/`.Y`.
5. **Fallback preserved**: headless/golden runs never call `Draw()`, so none of this
   is reachable in that path — no golden-transcript impact, matching Stage 1 and the
   dialog-frame feature before it.

## Non-goals (deferred to later stages)

- Inventory, character sheet, Skilldex, Pip-Boy, automap, options, preferences,
  save/load, aim dialog, tactics — all still compute positions against the real,
  unscaled `GraphicsDevice.Viewport` and read raw `Mouse.GetState()` directly; they
  stay in the third (unscaled) `SpriteBatch` block from this spec's design.
- The HUD bar, action menu, elevator picker, worldmap chrome — explicitly later
  stages per the staging plan (unchanged from the Stage 1 spec's non-goals).
- Undoing the three-block `SpriteBatch` split — later stages will fold more screens
  into the scaled block until the split collapses back to one block; that collapse is
  not part of this stage's deliverable.
- `ViewerGame.cs:2164`'s character-sheet click mapping and `ViewerGame.Panels.cs`'s
  two other `(vp.Width-640)/2` sites — same pattern, different screen, later stage.

## Testing

- No new pure-math logic is introduced (Stage 1 already covers `UiScale`/`TransformMouse`
  with unit tests) — Stage 2 is wiring only, verified by manual/visual check plus the
  existing golden suites (dialog and menu goldens are text-transcript based and
  untouched by any of the above, confirmed during the dialog-frame feature work).
- Manual verification: launch Hexwaste at a non-4:3 window size (matching this
  session's earlier fo2ce-comparison window size), confirm the main menu and an
  in-game dialog now render at a larger, fo2ce-comparable size, and confirm clicking
  a main-menu button / dialog option under the mouse cursor still selects the item the
  cursor visually sits over (not a stale, unscaled position).
- Confirm the character-select and character-creation screens (`DrawAuthenticSelector`/
  `DrawAuthenticCreation`, sharing `MenuOrigin()`) also scale and remain clickable,
  since they are pulled into this stage's scope alongside the main menu itself.
- Confirm every screen NOT in scope (inventory, Skilldex, etc.) is visually unchanged
  — still native 1:1 size, still correctly hit-tested by the raw mouse position — since
  it remains in the unscaled third `SpriteBatch` block.
