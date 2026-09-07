# UI Scale — HUD Bar

## Problem

Fourteen screens (dialog, main-menu family, Skilldex, perk picker, save/load, character sheet,
Pip-Boy, options, automap, preferences, aim dialog, tactics, elevator picker, action menu — Stage
2/3a/3b/3c/4a, all merged) now render at a uniform scale filling non-4:3 windows. The HUD bar —
the always-visible bottom interface bar — was deliberately deferred out of Stage 4 because it
looked like a different kind of risk: it draws unconditionally every frame (not a toggleable
modal like the other fourteen), from two call sites, and its `_hudBarHeight` field already has
eight dependent read sites across two files, one of which (`SkilldexOrigin`, Stage 3a) pre-emptively
divided by `UiScale()` anticipating a future scaled bar. A focused follow-up survey found the
actual blast radius is much smaller than that risk assessment implied, provided one convention is
adopted: **`_hudBarHeight` keeps meaning "how many real device pixels the bar occupies at the
screen bottom," even after the bar itself scales** — it becomes `InterfaceBar.Height * UiScale()`
instead of the bare constant `InterfaceBar.Height`. Under that convention, all eight existing
consumers keep working completely unchanged; this spec only needs to touch the bar's own drawing
and hit-testing.

## Grounding (confirmed by a direct source-tree survey this session)

- `_hudBarHeight` (`ViewerGame.cs:395`) is assigned in exactly two places, both inside
  `DrawInterfaceBar` (`ViewerGame.Hud.cs:109-276`):
  ```csharp
  private void DrawInterfaceBar()
  {
      if (_interfaceBar is not { Loaded: true } bar || _worldmapOpen)
      {
          _hudBarHeight = 0;
          return;
      }

      Rectangle viewport = GraphicsDevice.Viewport.Bounds;
      _hudBarHeight = InterfaceBar.Height;               // InterfaceBar.cs:24, const int Height = 99
      bar.Draw(_spriteBatch, viewport);

      if (_dude is null || GetCritterState(_dude.Dude) is not { } stats)
          return;
      Point o = bar.Origin(viewport); // bar-local coords (interface.cc) -> screen = o + coord
      ...
  ```
  Everything from `Point o = bar.Origin(viewport);` (line 123) through `DrawIndicatorPills(o);`
  (line 275) is **one linear body with no further early returns** — optional sub-blocks
  (`if`/`foreach` for weapon art, ammo counter, HP/AC fields, AP pips, the message monitor, combat-
  only END TURN/END COMBAT buttons, the hover/press-feedback loop, the debug-overlay loop) all key
  off the single `o`. This is a clean single boundary for the established scoped-batch technique
  — the wrap must start right after `Rectangle viewport = ...` (before `bar.Draw`, so the
  background art itself scales too) and run through `DrawIndicatorPills(o)`.
- **Every one of the eight existing `_hudBarHeight` consumers keeps its current unit expectation
  unchanged** under the "device pixels" convention:
  - `ViewerGame.Hud.cs:88` (`DrawMouseCursor`) and `:498` (`DrawTextOverlay`'s `hudY`) — raw
    device-pixel comparisons against `GraphicsDevice.Viewport.Height` — unaffected.
  - `ViewerGame.Hud.cs:642` — a `_hudBarHeight == 0` boolean gate — unaffected by units entirely.
  - `ViewerGame.Panels.cs:645` (`SkilldexOrigin`, Stage 3a) — already computes
    `int hudBarVirtual = (int)(_hudBarHeight / UiScale());`, with a doc comment (`:634-637`)
    stating exactly this convention as the reason. **Needs zero changes** — this is the call site
    that motivated deferring the HUD bar in the first place, and it turns out to already be
    correct for the chosen convention.
  - `ViewerGame.Panels.cs:735` (`DrawSkilldexTextFallback`) — a deliberately-unscaled fallback
    reading raw device pixels — unaffected.
- **`TryClickInterfaceBar(int mouseX, int mouseY)`** (`Hud.cs:419-448`) independently recomputes
  its own `Point o = bar.Origin(GraphicsDevice.Viewport.Bounds);` — a **second, separate** call to
  `bar.Origin(...)` from `DrawInterfaceBar`'s. Both currently agree because both read the raw
  device viewport; once `DrawInterfaceBar`'s `o` becomes virtual-canvas-based, this second call
  site must convert too, in the same commit, or hit-testing would silently disagree with what's
  drawn. Its caller, `ViewerGame.cs:2968` (`TryClickInterfaceBar(mouse.X, mouse.Y)`), reads the
  raw device mouse — needs `UiMouse()`.
- **Two independent raw-mouse reads live inside `DrawInterfaceBar` itself**: the hover/press-
  feedback loop (`:249-267`, `MouseState hoverMouse = Mouse.GetState();`, used for both position
  `.Contains(...)` and `.LeftButton`) and a debug-overlay loop (`:271-273`, draws only, no mouse
  read, but shares the same `o.X + b.Local.X` rect math and must move in lockstep). The feedback
  loop needs the same `rawMouse`/`mouse` split established for the elevator picker and aim dialog
  (Stage 3c/4a): a fresh `Mouse.GetState()` for `.LeftButton`, `UiMouse()` for position.
- **`HudButtons()`** (`Hud.cs:400-415`, returns `HudButton[]`, a `record struct(string Name,
  Rectangle Local, Action OnClick, bool CombatOnly = false)`) needs no changes itself — every
  `Local` rect is bar-local, offset against whatever `o` its three callers (`DrawInterfaceBar`'s
  two loops, `TryClickInterfaceBar`'s hit-test) compute. Its one external caller,
  `ViewerGame.Harness.cs:1857` (`StartupAction.HudClick`), dispatches by **name** only
  (`.FirstOrDefault(b => b.Name == hudName); btn.OnClick();`) — no coordinate math, confirmed
  unaffected.
- **`DrawIndicatorPills(Point o)`** (`Hud.cs:287-321`) takes `o` as a parameter and does no
  independent viewport/mouse read — it needs zero changes, automatically inheriting whatever
  coordinate space `DrawInterfaceBar` computes `o` in.
- **`DrawInterfaceBar` has two call sites**, both already inside the same shared, plain
  (identity-transform) `SpriteBatch.Begin(samplerState: PointClamp)`/`End()` block the other
  fourteen screens use:
  - `ViewerGame.cs:5810` — the live per-frame path, inside `if (_map is not null)`.
  - `ViewerGame.cs:7257` — `CaptureThumbnail`'s offscreen path, rendering into `_screenshotTarget`.
    **Confirmed this target is allocated at `GraphicsDevice.PresentationParameters
    .BackBufferWidth/Height` — i.e. exactly the live window size, not a fixed thumbnail
    resolution** (the actual small 224×133 thumbnail is a *separate*, later blit that never calls
    `DrawInterfaceBar` or reads `UiScale()`). Since `UiScale()`/`VirtualViewport()` read
    `GraphicsDevice.Viewport` fresh on every call, this call site computes the identical scale
    factor as the live path — no special-casing needed; `DrawInterfaceBar` can just do what every
    other migrated screen already does; provably safe, not merely assumed safe.
  - On a thumbnail-capture frame, `DrawInterfaceBar()` runs twice in one `Draw()` call (once for
    the offscreen target, once for the live frame) — both against same-sized targets, so both
    computations agree; this is expected, pre-existing double-invocation behavior, not a new bug.

## Design

1. **Keep `_hudBarHeight` as a device-pixel quantity.** Change its assignment from
   `_hudBarHeight = InterfaceBar.Height;` to `_hudBarHeight = InterfaceBar.Height * UiScale();` —
   this one-line change is what lets every one of the eight existing consumers, including Stage
   3a's `SkilldexOrigin`, keep working with zero changes of their own.
2. **Scope `DrawInterfaceBar`'s content (background art through indicator pills) in one scoped,
   scaled `SpriteBatch` block**, starting right after `Rectangle viewport = ...` (so `bar.Draw`
   itself scales) and ending after `DrawIndicatorPills(o);` — matching the exact
   `End()`/`Begin(transformMatrix: UiScaleMatrix())`/…/`End()`/`Begin()` technique used by all
   fourteen migrated screens. Inside the block, `viewport` becomes `VirtualViewport()` and
   `Point o = bar.Origin(viewport)` is computed from that virtual-canvas rectangle.
3. **Convert the feedback loop's mouse read** to the established `rawMouse`/`mouse` split: a fresh
   `Mouse.GetState()` for `.LeftButton`, `UiMouse()` for the position used in `.Contains(...)`.
   The debug-overlay loop needs no mouse change (it doesn't read the mouse), just inherits the new
   `o`.
4. **Convert `TryClickInterfaceBar`'s independent `bar.Origin(...)` call** to
   `bar.Origin(VirtualViewport())`, and its one caller (`ViewerGame.cs:2968`) to pass
   `uiMouse.X, uiMouse.Y` instead of the raw device mouse.
5. No changes needed to `HudButtons()`, `DrawIndicatorPills`, `DrawMouseCursor`,
   `DrawTextOverlay`'s `hudY`, `SkilldexOrigin`, `DrawSkilldexTextFallback`, or
   `ViewerGame.Harness.cs`'s `HudClick` action — all confirmed to already work correctly under the
   device-pixel convention.

## Non-goals

- Any change to the eight `_hudBarHeight` consumers listed above — confirmed unaffected by the
  chosen convention; touching any of them would be unnecessary scope creep.
- Inventory (its own, separately-scoped future plan).
- Worldmap chrome (Stage 5).
- Special-casing `CaptureThumbnail`'s offscreen path — confirmed to naturally compute the correct,
  matching scale factor with no changes.

## Testing

Same as every prior stage: no new pure-math logic, so verification is the existing golden suites
(headless, unaffected — `DrawInterfaceBar` is Draw-only) plus a manual visual/screenshot check at
the Viewer's default 1280x720 window, confirming: the bar renders at the same 1.5× scale as the
fourteen already-scaled screens, still bottom-anchored and centered, undistorted, with every
button (INV/OPT/MAP/CHA/PIP/SKILLDEX/WEAPON/END TURN/END COMBAT) clickable at its visually
scaled position — this is the check that proves `TryClickInterfaceBar`'s conversion agrees with
the scaled draw. Also verify the Skilldex box (Stage 3a, reads `_hudBarHeight`) still sits flush
above the now-larger bar with no gap or overlap, confirming the device-pixel convention holds
end-to-end. Also verify a `--screenshot`/thumbnail-capture path (`CaptureThumbnail`) still
produces a correctly-scaled bar in its output, confirming the offscreen-path safety argument
holds in practice, not just on paper.
