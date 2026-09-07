# UI Scale — Stage 4a: Action Menu, Elevator Picker

## Problem

Stage 2/3a/3b/3c (shipped) wired the shared `UiScale` infrastructure into twelve screens — all now
render at a uniform scale filling non-4:3 windows, matching `fallout2-ce`'s stretch. The
originally-planned Stage 4 covered the HUD bar, the right-click action menu, and the elevator
level picker together. A pre-implementation survey found the HUD bar is a materially different
kind of risk from the other two: it draws unconditionally every frame (not a toggleable modal,
unlike all twelve screens migrated so far), it has two separate draw call sites (the live frame
and the screenshot/off-screen render-target path), and its `_hudBarHeight` field already has
eight dependent read sites across two files — one of them (`SkilldexOrigin`, already shipped in
Stage 3a) pre-emptively divided by `UiScale()` in anticipation of a future scaled bar. Scaling the
HUD bar is therefore not an additive "wire up this one file" change like every prior stage — it
requires revisiting already-shipped code and settling an unresolved unit convention first. Per
user decision, the HUD bar is deferred to its own dedicated later stage. **This spec covers Stage
4a only: the action menu and the elevator picker**, both far more contained.

- **Action menu** (`DrawActionMenu`/`OpenActionMenu`/`ActionMenuRowAt`, `src/Hexwaste.Viewer/ViewerGame.ActionMenu.cs`)
- **Elevator picker** (`DrawElevatorPicker`/`UpdateElevatorPicker`, `src/Hexwaste.Viewer/ViewerGame.Elevator.cs`)

## Grounding (confirmed by a direct source-tree survey this session)

- Both screens are drawn from the same shared per-frame `SpriteBatch.Begin/End` block every
  other migrated screen uses (`ViewerGame.cs`, opened at `:5777`) — but, unlike all twelve prior
  screens, **both are called OUTSIDE the `if (_worldmapOpen) {...} else {...}` branch entirely**
  (`ViewerGame.cs:5826-5827`, `DrawActionMenu(); DrawElevatorPicker();`), running every frame
  unconditionally (each self-guards on its own state: `_actionMenuObj is null` /
  `_elevatorPicker is not { }`). This is a structural difference from the twelve already-migrated
  screens worth confirming stays harmless — in practice neither should ever be open while
  `_worldmapOpen` is true, but nothing in the code prevents it structurally, so a sanity check
  belongs in this stage's testing.
- **Elevator picker** is structurally the closest match to prior-stage work: a centered modal
  with a genuine art-vs-fallback dispatch (`DrawElevatorPicker()`, `ViewerGame.Elevator.cs:251-288`,
  falls back to `DrawElevatorPickerFallback(...)` at `:291-304` when any of 4 required FRMs is
  missing — same shape as Skilldex/perk-picker/aim-dialog). Two origin helpers, both currently
  reading the raw device viewport:
  ```csharp
  private Point ElevatorWindowPos(Texture2D bg) => new(                        // :167-169
      (GraphicsDevice.Viewport.Width - bg.Width) / 2,
      (GraphicsDevice.Viewport.Height - bg.Height) / 2);

  private Rectangle ElevatorButtonRect(Texture2D bg, Texture2D btn, int level) // :173-177
  {
      Point p = ElevatorWindowPos(bg);
      return new Rectangle(p.X + 13, p.Y + 40 + level * 60, btn.Width, btn.Height);
  }
  ```
  Neither has an external caller (confirmed by a whole-tree grep) — no `ViewerGame.Harness.cs`
  surprise. `UpdatePreferences`-style pattern applies directly: convert both to
  `VirtualViewport()`, scope only the art path (mirroring Skilldex/perk-picker/aim-dialog), leave
  `DrawElevatorPickerFallback` untouched. `UpdateElevatorPicker` (`:183-249`) hit-tests via
  `ElevatorButtonRect(bg, btn, i).Contains(mouse.X, mouse.Y)` (`:231`, raw `mouse` parameter) and
  `DrawElevatorPicker` has its own independent in-Draw hover read (`MouseState mouse =
  Mouse.GetState();` at `:281`) — both need the `UiMouse()`/`uiMouse` treatment established since
  Stage 2. `Harness.cs`'s `ElevatorOpen`/`ElevatorRide` actions set `_elevatorPicker` directly and
  never touch pixel math — confirmed unaffected.
- **Action menu is smaller by line count (`DrawActionMenu`, `ActionMenu.cs:171-193`, 23 lines)
  but structurally the most novel of any screen tackled in this whole project**: it has no
  reusable "origin helper" recomputed every frame the way every other screen (including the
  elevator picker) does. Its anchor, `_actionMenuPos`, is a `Point` field computed ONCE — at
  right-click time, inside `OpenActionMenu` (`ActionMenu.cs:55-75`), a method called from
  `Update()`, not from `Draw()` — and then simply read, unchanged, by every subsequent
  `DrawActionMenu`/`ActionMenuRowAt` call until the menu closes:
  ```csharp
  Rectangle vp = GraphicsDevice.Viewport.Bounds;               // :70
  int h = _actionMenuItems.Count * ActionIconSize;
  _actionMenuPos = new Point(
      Math.Clamp(mx, 0, Math.Max(0, vp.Width - ActionIconSize)),
      Math.Clamp(my, 0, Math.Max(0, vp.Height - h)));           // :72-74
  ```
  `mx, my` are the raw right-click screen coordinates, passed in from `Update()`
  (`ViewerGame.cs:2947`, `OpenActionMenu(_hoveredObject, mouse.X, mouse.Y)`, raw `mouse`). This
  means the fix is NOT "convert one helper in `ActionMenu.cs`" — it is: (1) `OpenActionMenu`'s
  clamp switches from `GraphicsDevice.Viewport.Bounds` to `VirtualViewport()`, and (2) its
  caller in `Update()` (`ViewerGame.cs:2947`) must pass an already-`UiMouse()`-transformed point
  instead of the raw `mouse.X, mouse.Y`, so that `_actionMenuPos` is stored in virtual-canvas
  coordinates from the moment it's created — everything downstream (`DrawActionMenu`,
  `ActionMenuRowAt`) then just works unchanged, needing no origin-helper conversion of its own.
  `ActionMenuRowAt(int mx, int my)` (`:80-86`) does pure arithmetic against `_actionMenuPos`
  already, no viewport read — its Update-path caller (`ViewerGame.cs:2960`, raw `mouse.X, mouse.Y`)
  needs the same `uiMouse` swap. `DrawActionMenu`'s own in-Draw hover read (`MouseState m =
  Mouse.GetState();`, `ActionMenu.cs:175`) needs the `UiMouse()` treatment.
  `Harness.cs:392-404`'s `ActionMenuProbe` calls `OpenActionMenu(amObj, 100, 100)` with hardcoded
  literal coordinates — confirmed harmless: `(100, 100)` sits comfortably inside both the real
  device viewport and any `VirtualViewport()` at every resolution this project runs at (the
  virtual canvas is always ≥ 640×480, wider or taller than the base depending on which axis is
  scale-limiting), so no change is needed there, but note it explicitly since it's a literal that
  silently assumes this.
- No `ViewerGame.Harness.cs` caller of `ElevatorWindowPos`/`ElevatorButtonRect` exists.
  `HudButtons()` (a HUD-bar helper, out of scope for this stage) has a `Harness.cs:1857` caller
  but it dispatches by name (`.OnClick()`) with no coordinate math — irrelevant to this stage,
  noted only so it isn't mistaken for an action-menu/elevator-picker concern.

## Design

1. **Elevator picker**: scope `DrawElevatorPicker`'s art path (after its 4-FRM-missing fallback
   check) in its own scoped, scaled `SpriteBatch` block, converting `ElevatorWindowPos`/
   `ElevatorButtonRect` to `VirtualViewport()`. Leave `DrawElevatorPickerFallback` untouched and
   unscaled. Route `UpdateElevatorPicker`'s hit-test and `DrawElevatorPicker`'s in-Draw hover read
   through `uiMouse`/`UiMouse()`.
2. **Action menu**: convert `OpenActionMenu`'s clamp math (`ActionMenu.cs:70-74`) to
   `VirtualViewport()`, and change its `Update()` call site (`ViewerGame.cs:2947`) to pass
   `uiMouse.X, uiMouse.Y` instead of the raw `mouse.X, mouse.Y` — this is the one place in this
   stage where a `Draw()`-side conversion alone is insufficient; the fix lives in `Update()`'s
   click-open path. `ActionMenuRowAt`'s Update-path caller (`:2960`) switches the same way.
   `DrawActionMenu`'s in-Draw hover read switches to `UiMouse()`. No origin helper needs touching
   in `ActionMenu.cs` itself — `_actionMenuPos` is stored correctly once its two writers/readers
   agree on the coordinate space at creation time.
3. In both cases, every `Update()`-side hit-test reuses the existing `uiMouse` local (declared
   once, Stage 2 Task 1); no new per-frame mouse sampling is introduced.

## Non-goals

- The HUD bar (`DrawInterfaceBar` + `DrawIndicatorPills` + `HudButtons`/`TryClickInterfaceBar`) —
  deferred to its own dedicated later stage, which must open by settling whether `_hudBarHeight`
  becomes a virtual-canvas quantity (removing `SkilldexOrigin`'s existing `/UiScale()` division,
  but requiring `DrawMouseCursor`/`DrawTextOverlay`'s two raw-device reads and
  `DrawSkilldexTextFallback`'s read to convert the other way) or stays a device-pixel quantity
  storing `InterfaceBar.Height * UiScale()` (touching fewer already-shipped call sites) — and must
  account for its second draw call site (`ViewerGame.cs:7257`, the screenshot/off-screen path).
- Inventory (its own, separately-scoped future plan).
- Worldmap chrome (Stage 5).
- Any change to `ViewerGame.Harness.cs` — confirmed no coordinate-math caller of any helper this
  stage touches; `HudClick`/`ActionMenuProbe`/`ElevatorOpen`/`ElevatorRide` are all unaffected as
  detailed above.

## Testing

Same as every prior stage: no new pure-math logic, so verification is the existing golden suites
(headless, unaffected) plus a manual visual/screenshot check per screen at the Viewer's default
1280x720 window, confirming: each screen renders at the same 1.5× scale as the eleven other
already-scaled modal screens, centered (elevator picker) or correctly click-anchored (action
menu), undistorted, with hover/click landing under the cursor. For the action menu specifically,
verify a right-click at a specific screen position opens the menu at the cursor (not offset), and
that clicking a specific menu row fires that row's action — this is the check that proves the
`Update()`-side `OpenActionMenu`/`uiMouse` plumbing is correct, since there's no origin helper to
inspect statically the way there is for every other screen. Also do a quick sanity check that
neither screen renders/misbehaves while `_worldmapOpen` is true (per the Grounding section's note
that both draw outside the `if (_worldmapOpen)` branch structurally, even though neither should
realistically be open there).
