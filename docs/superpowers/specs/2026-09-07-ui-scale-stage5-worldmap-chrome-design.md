# UI Scale — Stage 5: Worldmap Chrome

## Problem

Seventeen screens now render at a uniform scale filling non-4:3 windows (dialog, main menu,
Skilldex, perk picker, save/load, character sheet, Pip-Boy, options, automap, preferences, aim
dialog, tactics, elevator picker, action menu, HUD bar, and both Inventory pieces). The one
remaining unscaled surface in the whole project is worldmap chrome — the authentic FRM-136
window (P123/P125): backdrop, the scissored 450×443 map/townmap view, town tabs, date/dial, and
the globe/car monitor. This spec covers migrating it.

## Grounding (confirmed by a direct source-tree read this session)

- **`ChromeOrigin(Rectangle viewport)`** (`WorldmapScreen.cs:135-136`) is the single choke point,
  exactly the same shape as `InvBoxOrigin()`/`ItemWindowArt()`:
  ```csharp
  public Point ChromeOrigin(Rectangle viewport) =>
      new(viewport.X + (viewport.Width - ChromeW) / 2, viewport.Y + (viewport.Height - ChromeH) / 2);
  ```
  Unlike every prior stage's choke point, `WorldmapScreen` is a **separate class from
  `ViewerGame`** and every one of its positioning methods already takes `viewport` as a
  **parameter** rather than reading `GraphicsDevice.Viewport` internally: `ViewRect` (:139-143),
  `TabButtonRect` (:174-179), `TabArrowRects` (:182-188), `TownWorldSwitchRect` (:329-334),
  `TownmapEntranceAt` (:310-325), `HitTestChrome` (:437-455, via `ViewRect`), and `DrawChrome`
  itself (:198). **No internal `WorldmapScreen.cs` positioning code needs to change** — the whole
  fix is swapping what `ViewerGame.cs` passes in as `viewport` and as the mouse position.
- **Every `ViewerGame.cs` call site** currently passes `GraphicsDevice.Viewport.Bounds` and raw
  `mouse.X/Y` (confirmed via direct read of `ViewerGame.cs:2632-2762` and `:5791-5817`):
  - `:2676` — `tm.TownmapEntranceAt(mouse.X, mouse.Y, GraphicsDevice.Viewport.Bounds)` (hit-test,
    inside the townmap-sub-view input branch)
  - `:2696` — `wms.TownWorldSwitchRect(GraphicsDevice.Viewport.Bounds).Contains(mouse.X, mouse.Y)`
    (hit-test)
  - `:2710` — `Rectangle wmView = wms.ViewRect(GraphicsDevice.Viewport.Bounds);` then used at
    `:2713-2720` for the view-edge hover-scroll math against raw `mouse.X/Y`
  - `:2725` — `wms.HitTestChrome(mouse.X, mouse.Y, GraphicsDevice.Viewport.Bounds, WorldFog)`
    (hit-test)
  - `:2728` — `wms.TabArrowRects(GraphicsDevice.Viewport.Bounds)` then `.Contains(mouse.X,
    mouse.Y)` at `:2735/2737`
  - `:2741` — `wms.TabButtonRect(GraphicsDevice.Viewport.Bounds, row).Contains(mouse.X, mouse.Y)`
    inside the `for (int row = 0; row < 7; row++)` loop
  - `:5801` — `wms.DrawChrome(_spriteBatch, GraphicsDevice.Viewport.Bounds, ...)` (draw-time)
  - `ViewerGame.Panels.cs:1227` — `DrawEncounterPrompt()`'s own `Rectangle vp =
    GraphicsDevice.Viewport.Bounds;` — a plain synthetic dark box + green text (no art, no mouse
    hit-test; Y/N answered by keyboard only, `ViewerGame.cs:2607-2625`). Trivially in-scope.
  - `:2752`, `:5809`, `:5813` — `_worldmapScreen?.HitTest/Draw/DrawPartyDot(...,
    GraphicsDevice.Viewport.Bounds, ...)` — the **`else` branch** taken only when
    `HasChrome` is false (no `worldmap.frm`). **OUT OF SCOPE** — see next point.
- **The art-present/fallback split is real and method-level**, confirmed at
  `ViewerGame.cs:5791-5817`:
  ```csharp
  _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
  if (_worldmapOpen)
  {
      if (_worldmapScreen is { HasChrome: true } wms)       // ART PRESENT — the real FRM-136 chrome
      {
          wms.DrawChrome(_spriteBatch, GraphicsDevice.Viewport.Bounds, _hoveredArea, WorldFog,
              _worldPosX, _worldPosY, ..., car?.InCar == true, car?.Fuel ?? 0, ..., _carDotFrame);
      }
      else                                                     // ART ABSENT — pre-chrome fitted fallback
      {
          _worldmapScreen?.Draw(_spriteBatch, GraphicsDevice.Viewport.Bounds, _hoveredArea, WorldFog);
          if (_worldPosX >= 0 && _worldPosY >= 0)
              _worldmapScreen?.DrawPartyDot(_spriteBatch, GraphicsDevice.Viewport.Bounds, _worldPosX, _worldPosY);
          DrawWorldmapCarBox();
      }
      DrawEncounterPrompt();
  }
  ```
  `HasChrome => Frm(BgFrm) is not null` (`WorldmapScreen.cs:131`) — true whenever `worldmap.frm`
  (interface 136) loads, i.e. always true with real game-data; false only headless/no-art. The
  `else` branch's own `Layout()` (`WorldmapScreen.cs:101-107`,
  `scale = min(viewport.Width/1400, viewport.Height/1500)`) is a **second, independent,
  pre-existing aspect-fit scaling scheme** that already fills whatever viewport it's given — it
  is not the 640×480-native letterboxing problem this whole project has been fixing, and it is
  not broken. **Leave the entire `else` branch (`Draw`, `HitTest`, `DrawPartyDot`,
  `DrawWorldmapCarBox`, `DrawPartySprite`) untouched.**
- **`DrawChrome`'s sub-components** (`WorldmapScreen.cs:194-259`) are all single-function, no
  further art/fallback split inside them (matching the Pip-Boy/options/save-load
  whole-method-scaling shape): the backdrop (`Frm(BgFrm)`, :200), the scissored inner view
  (townmap sub-view OR `DrawWorldView`, mutually exclusive on `TownmapArea`, :211-232), town tabs
  (`DrawTabs`, :238), date/dial (`DrawDate` + dial frame, :239-241), and the car monitor OR globe
  stamp (mutually exclusive on `inCar`, :243-258).
- **The scissor problem — the most novel code in the whole UI Scale project.** `DrawChrome`
  (`WorldmapScreen.cs:202-236`) reopens the `SpriteBatch` mid-method to scissor the inner view:
  ```csharp
  Point o = ChromeOrigin(viewport);
  Rectangle view = ViewRect(viewport);
  spriteBatch.Draw(Frm(BgFrm)!, new Vector2(o.X, o.Y), Color.White);

  spriteBatch.End();
  Rectangle oldScissor = _graphicsDevice.ScissorRectangle;
  spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: _scissor);
  _graphicsDevice.ScissorRectangle = Rectangle.Intersect(view, viewport);

  if (TownmapArea is { } town && Frm(town.TownmapArtIdx) is { } townArt) { /* draw town art + hotspots */ }
  else { DrawWorldView(spriteBatch, view, hovered, fog, partyX, partyY, destX, destY); }

  spriteBatch.End();
  _graphicsDevice.ScissorRectangle = oldScissor;
  spriteBatch.Begin(samplerState: SamplerState.PointClamp);
  ```
  `_scissor` is `new RasterizerState { ScissorTestEnable = true }` (`WorldmapScreen.cs:37`).
  `GraphicsDevice.ScissorRectangle` is a **device-pixel** rectangle — MonoGame does not run it
  through the `SpriteBatch` transform matrix. Two things must both be handled correctly:
  1. If the caller wraps the whole `DrawChrome` call in a scaled outer block
     (`transformMatrix: UiScaleMatrix()`), this inner reopened `Begin()` — which currently passes
     no `transformMatrix` — silently **drops the scale** for everything inside the clip (world
     tiles, fog, city circles, party dot, target marker, and the townmap sub-view's art +
     hotspots + labels) while the surrounding chrome (backdrop, tabs, date/dial, monitor) stays
     scaled. The inner `Begin()` must explicitly re-pass the same scale transform.
  2. The scissor rectangle itself must be computed in **real device pixels**, not virtual-canvas
     units — setting `ScissorRectangle` straight from a virtual-unit `view` rect would clip the
     wrong region of the real (larger) window.
- **Confirmed simplification found this session**: `VirtualViewport()` (`ViewerGame.UiScale.cs:
  22-30`) **always returns `new Rectangle(0, 0, w, h)`** — offset always `(0,0)`, never a
  letterboxed/pillarboxed origin. This falls directly out of `UiScale.ComputeVirtualViewport`
  (`UiScale.cs:29-30`) and `TransformMouse` (`UiScale.cs:37-38`), whose own doc comment states:
  *"No offset term is needed: the virtual viewport is DERIVED from the real one divided by scale,
  so scaling it back up exactly tiles the real screen with no letterbox gap to account for."*
  This means the device-pixel scissor rectangle is a **plain multiply by `UiScale()`** — no
  offset term, no dependency on `VirtualViewport()`'s own `X`/`Y` (which are always zero anyway).
- **`ViewerGame.Harness.cs`** only sets `_worldmapScreen.TownmapArea = tmHere;` (a plain property,
  no viewport math) and reads `_worldmapScreen?.HasTownmap(tmHere)` (no viewport parameter) —
  confirmed via whole-tree grep, matching every prior stage: **no change needed there.**
- **Co-activation**: `_worldmapOpen` gates a dedicated top-level early-return block in both
  `Update()` (`ViewerGame.cs:2633-2762`, "Worldmap mode swallows map input") and `Draw()`
  (`ViewerGame.cs:5792-5817`, inside the single unscaled outer `_spriteBatch.Begin()`). No other
  migrated screen's input or draw code runs while the worldmap is open — the same early-return
  shape already proven for Inventory Piece 1's dialogue/inventory mutual exclusion.
- **CLI verification aids** (`Program.cs`): `--worldmap` opens the world view directly (bare
  flag, no argument, :70-72); `--townmap [entrance]` opens the current town's townmap sub-view
  (optional entrance index to also enter it, :476-479).

## Design

Two tasks in one plan, landed back-to-back — **not independently shippable** (see Non-goals).

1. **Task 1 — the chrome frame.** In `ViewerGame.cs`, convert the frame-level call sites:
   `TownmapEntranceAt`, `TownWorldSwitchRect`, `HitTestChrome`, `TabArrowRects`, `TabButtonRect`
   (the `for row` loop), and the view-edge hover-scroll math (`wmView.Contains`/edge comparisons)
   — `GraphicsDevice.Viewport.Bounds` → `VirtualViewport()`, `mouse.X/Y` → `uiMouse.X/Y`. Convert
   `ViewerGame.Panels.cs`'s `DrawEncounterPrompt()` the same way it and every prior
   no-separate-fallback screen were converted: scope its whole body in a scaled block. Scope the
   `wms.DrawChrome(...)` call site (`ViewerGame.cs:5801`) in a scaled block too, passing
   `VirtualViewport()` as its `viewport` argument. Leave the `else` legacy-fallback branch
   entirely untouched (still passing `GraphicsDevice.Viewport.Bounds` — that branch's own
   `Layout()` already scales independently and correctly).
2. **Task 2 — the scissored map view.** Give `DrawChrome` one new parameter, `float scale`
   (the caller passes `UiScale()`). Inside the scissor block:
   - Keep computing `view` and every drawn position (tiles, fog, circles, party dot, target
     marker, townmap art, hotspot markers, labels) in virtual units exactly as today — they
     render correctly because the transform matrix maps virtual → device for anything drawn
     through a batch using it.
   - Derive the actual `GraphicsDevice.ScissorRectangle` from the same virtual `view`/`viewport`
     intersection, scaled up to device pixels: `new Rectangle((int)(r.X*scale), (int)(r.Y*scale),
     (int)(r.Width*scale), (int)(r.Height*scale))` applied to `Rectangle.Intersect(view,
     viewport)` — valid with **no offset term** specifically because `VirtualViewport()` always
     starts at `(0,0)` (confirmed above).
   - Re-pass `transformMatrix: Matrix.CreateScale(scale)` on the scissor block's inner `Begin()`,
     so content drawn inside the clip scales identically to the frame around it. `WorldmapScreen`
     has no dependency on `ViewerGame`'s `UiScaleMatrix()` helper — it builds its own
     `Matrix.CreateScale(scale)` from the passed-in `scale`, keeping `WorldmapScreen` free of any
     `ViewerGame`-specific coupling.
   - The post-scissor `Begin()` that restores the caller's state (`WorldmapScreen.cs:236`) must
     also resume with `transformMatrix: Matrix.CreateScale(scale)` (not a bare `Begin()`), since
     `DrawChrome` itself keeps drawing scaled content after the scissor block closes — the tabs,
     date/dial, and car/globe monitor (`WorldmapScreen.cs:238-258`) all execute after this
     `Begin()`, inside the same call, and must render at the same scale as the frame.
     (Confirmed via direct read of `ViewerGame.cs:5814-5839`: whatever batch state `DrawChrome`
     leaves behind on return does not leak into anything else — `DrawEncounterPrompt()`, the only
     call after it in the `_worldmapOpen` branch, manages its own scoped `End()`/`Begin()` cycle
     independently per Task 1, and every draw call after the whole `if (_worldmapOpen) {...} else
     {...}` block — `DrawMapFade`, `DrawActionMenu`, etc. — already manages its own scoping from
     prior stages. So the outer scaled block Task 1 wraps around the `DrawChrome` call site only
     needs to be closed and reopened unscaled once, after `DrawChrome` returns, exactly like every
     other whole-method-scoped screen in this project.)

## Non-goals

- **Independent shippability of the two tasks.** `DrawChrome` is one method that draws both the
  frame and the scissored content in one continuous call. Landing Task 1 alone would scale the
  frame while the scissored content stayed native-size/position inside it — a visible mismatch.
  Both tasks land in the same plan, back-to-back, as one reviewable unit split into two
  review-focused diffs (frame math, then scissor math) — this was explicitly approved by the user
  over the alternative of one single combined task.
- The legacy fallback rendering path (`_worldmapScreen.Draw/HitTest/DrawPartyDot/DrawPartySprite`,
  `DrawWorldmapCarBox`) and its own independent `Layout()` aspect-fit scale — not broken, not
  touched.
- `ViewerGame.Harness.cs` — confirmed no coordinate-math caller of anything this stage touches.
- Any change to the underlying worldmap travel/state logic in `ViewerGame.Worldmap.cs` (396
  lines of pure travel-state code, unrelated to rendering/input coordinate space).

## Testing

Same as every prior stage: no new pure-math logic (the scissor-rectangle scale multiply is a
direct, already-proven application of the existing `UiScale()`/`VirtualViewport()` primitives),
so verification is the existing golden suites (headless, unaffected — `_worldmapOpen` never
becomes true without simulated worldmap actions the goldens don't currently drive through
`DrawChrome`'s scaled paths, and the legacy fallback stays byte-identical) plus a manual
visual/screenshot check at a non-4:3 window: `--worldmap` to confirm the frame, tabs, date/dial,
and scissored world view (tiles, fog, city circles, party dot) all scale together with no bleed
outside the clip rectangle and no mismatch between the frame and its contents; `--townmap` to
confirm the townmap sub-view (town art + hotspot markers + labels) scales identically inside the
same clip. This closes out UI Scale entirely — after this stage, all screens in the Viewer scale
uniformly to fill non-4:3 windows.
