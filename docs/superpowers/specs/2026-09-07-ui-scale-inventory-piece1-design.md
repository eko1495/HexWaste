# UI Scale — Inventory, Piece 1: the Dude's Own Inventory

## Problem

Fifteen screens (dialog, main-menu family, Skilldex, perk picker, save/load, character sheet,
Pip-Boy, options, automap, preferences, aim dialog, tactics, elevator picker, action menu, and the
HUD bar) now render at a uniform scale filling non-4:3 windows. Inventory — the dude's own
inventory screen plus the loot/barter/trade window variants — was deferred out of Stage 3a because
it has no clean single art/fallback split the way every other screen does: `InvBoxOrigin()`
returns `Point?`, and roughly six dependent helpers each independently branch on that null with
their own fixed, non-viewport-relative fallback positions, plus a drag-ghost icon drawn at a raw
mouse position. A fresh survey confirms the screen actually splits cleanly into two largely
decoupled subsystems — **the dude's own inventory** (one texture, one origin function, no strip
variants) and **the loot/barter/trade window** (three sub-variants sharing one resolver, three
times the origin-branch surface for the same migration mechanism). Per user decision, this spec
covers **Piece 1 only: the dude's own inventory** — `InvBoxOrigin()` and everything that depends
on it. The loot/barter/trade window (`ItemWindowArt()` and its dependents) is deferred to its own
later piece, the same way the HUD bar was deferred out of Stage 4a.

## Grounding (confirmed by a direct source-tree survey this session)

- `InvBoxOrigin()` (`ViewerGame.Panels.cs:1939-1942`) is the single choke point for this piece:
  ```csharp
  private Point? InvBoxOrigin() => _invBox is null
      ? null
      : new Point(Math.Max(0, (GraphicsDevice.Viewport.Width - InvBoxW) / 2),
                  Math.Max(0, (GraphicsDevice.Viewport.Height - InvBoxH) / 2));
  ```
  Every one of its six dependent helpers (`InventoryPanelX`, `WeaponSlotRect`, `ArmorSlotRect`,
  `LeftWeaponSlotRect`, `InvBoxDoneRect`, and the `InvBoxOrigin`-branches of the shared
  `ItemRowRect`/`DrawItemList`) calls `InvBoxOrigin()` itself rather than independently reading
  `GraphicsDevice.Viewport` — **this is the opposite risk shape from Stage 3b's duplicated-origin
  screens**: converting `InvBoxOrigin()` alone (to `VirtualViewport()`) automatically propagates
  correctly to every dependent, with no second copy to keep in lockstep. Their fixed fallbacks
  (`new Rectangle(420, 96, 90, 60)`, `x = 40`, etc., used only when `_invBox is null`) are already
  absolute, non-viewport-relative constants — matching the established "fallback stays as-is"
  precedent, they need no changes.
- **The shared item-list plumbing (`ItemRowRect`, `DrawItemList`, `TryClickItemPanel`) is used by
  BOTH families plus a third, fully-headless fallback path**, and already branches cleanly on
  which family is active:
  ```csharp
  // ItemRowRect (ViewerGame.Panels.cs:2289): InvBoxOrigin branch first, then ItemPanelRegion
  // (loot/barter/trade), then the fixed-box fallback.
  if (InvBoxOrigin() is { } o && x == o.X + InvBoxListLocalX) return ...;
  if (ItemPanelRegion(x) is { } reg) return ...;
  return ...; // fixed x+6/60+... box, headless-safe
  ```
  `DrawItemList` (`:2327`) has the identical three-way branch shape. Converting `InvBoxOrigin()`
  is sufficient to fix the first branch; the second (loot/barter/trade) and third (headless
  fallback) branches are untouched by this piece and must keep rendering exactly as today.
- **`CurrentItemPanels()`** (`:1782-1810`) is an if/else-if chain keyed on `_barterNpc`/
  `_tradePartner`/`_lootContainer`/`_inventoryOpen`, in that priority order — **the
  `ItemPanelKind.Inventory` panel (the dude's own list) is only ever added in the final
  `else if (_inventoryOpen)` branch, and is always the ONLY panel in the returned list when it
  appears** (mutually exclusive with barter/trade/loot by construction). This is the hook Piece 1
  needs: `DrawItemPanels()`'s loop over `CurrentItemPanels()` can check `panel.Kind ==
  ItemPanelKind.Inventory` to scope just that panel's rendering, with no risk of a barter/trade/
  loot panel ever sharing a frame with it.
- **`DrawItemPanels()`** (`:1812-1827`) is the umbrella, called from `Update()`... no — called from
  `Draw()`'s shared per-frame batch (same block as all fifteen migrated screens), unconditionally
  except for a bare `_fontRenderer is null` guard. Unlike a typical migrated screen, it has no
  single top-level open-flag return — but each of its five callees (`DrawInventoryWindow`,
  `DrawItemWindow`, `DrawEquipSlots`, `DrawInventorySummary`) already self-guards on its own
  open-flag combination, and `CurrentItemPanels()` returns an empty list when nothing is open — so
  there's no actual correctness gap here, just a style difference from the other fifteen screens
  that this piece does not need to fix.
- **`DrawInventoryWindow`** (self-guards: `_fontRenderer is null || !_inventoryOpen ||
  _lootContainer is not null || _tradePartner is not null || _barterNpc is not null`),
  **`DrawEquipSlots`** (identical guard), and **`DrawInventorySummary`** (self-guards on
  `_dude is null || InvBoxOrigin() is not { } o`, reached from `DrawItemPanels()` only when
  `_inventoryOpen && _lootContainer is null && _tradePartner is null && _barterNpc is null`) are
  each independently self-contained, single-purpose Draw methods — structurally exactly like
  Skilldex/perk-picker/aim-dialog's art-path methods, just without a separate fallback method
  (there is none for these three; the "fallback" is the fixed-position rects the six helpers
  already return when `_invBox is null`, which these methods draw at directly with no code
  change needed).
- **The drag-ghost icon** (`DrawEquipSlots`, `ViewerGame.Panels.cs:2202-2203`):
  ```csharp
  if (_dragItem is { } dragged) // the ghost icon follows the cursor (from the last Update mouse)
      DrawItemIcon(dragged, new Rectangle(_previousMouse.X - 14, _previousMouse.Y - 11, 28, 22));
  ```
  `_previousMouse` is a raw, unscaled `MouseState` captured every frame from `Mouse.GetState()`
  — it is not virtual-canvas-transformable as stored. Since every other in-Draw hover/position
  read across all fifteen migrated screens already calls `UiMouse()` fresh at the point of use
  (rather than reading a stored prior-frame value), the natural fix is to replace
  `_previousMouse.X/Y` with a fresh `UiMouse()` call here too — consistent with every other
  screen's convention, and arguably more correct (the ghost would track the CURRENT frame's mouse
  rather than lagging one frame behind).
- **`Update()`'s hit-test chain** (`ViewerGame.cs`, in the click-handling cascade) has this shape,
  confirmed by direct reading:
  ```csharp
  if (clickPress && _inventoryOpen && _lootContainer is null && _tradePartner is null
      && InvBoxDoneRect() is { } done && done.Contains(mouse.X, mouse.Y)) { ... }
  else if (clickPress && _lootContainer is not null
      && LootDoneRect() is { } lootDone && lootDone.Contains(mouse.X, mouse.Y)) { ... }
  else if (_inventoryOpen && _lootContainer is null && _tradePartner is null)
      HandleInventoryDrag(mouse, shiftHeld);
  else if (clickPress)
      TryClickItemPanel(mouse.X, mouse.Y, shiftHeld);
  ```
  The first branch (`InvBoxDoneRect`) and the third (`HandleInventoryDrag`) are gated on
  `_inventoryOpen && _lootContainer is null && _tradePartner is null` — **exclusively the pure-
  inventory case**, safe for this piece to convert to `uiMouse`. The second branch
  (`LootDoneRect`) and the final fallback `TryClickItemPanel` (which handles barter/trade/loot's
  click-to-use, per the existing comment "loot/barter/trade keep click-on-press in the caller")
  belong to Piece 2 and must stay on raw `mouse` until that piece lands. A separate, earlier
  barter-specific branch (number-key buy/sell plus a click, also calling `TryClickItemPanel` with
  raw `mouse`) is likewise Piece 2 territory.
  `HandleInventoryDrag(MouseState mouse, bool shift)` reads BOTH button state (`.LeftButton`) and
  position (`.X`/`.Y`) extensively throughout its body (press branch, release branch, and the
  tap-vs-drag distance threshold against a stored `_dragStart` field) — unlike the single-position-
  read pattern Tactics/aim-dialog/elevator-picker used, this needs the Preferences-style synthetic
  `MouseState` built once at the `Update()` call site (carrying `uiMouse.X/Y` with every other
  field copied from the real `mouse`), so `HandleInventoryDrag`'s own body needs no internal
  changes — `_dragStart` then naturally ends up stored in virtual-canvas coordinates too, which is
  exactly what it needs to be to stay consistent with `TryClickItemPanel(_dragStart.X,
  _dragStart.Y, shift)`'s later comparison against the now-virtual-canvas `ItemRowRect`.
- No `ViewerGame.Harness.cs` caller exists for any of the ten helpers this piece or Piece 2
  touches — confirmed by a fresh whole-tree grep; the only two cross-file call sites at all are
  `InvBoxDoneRect`/`LootDoneRect`'s callers in `ViewerGame.cs`'s `Update()`, already accounted for
  above.

## Design

1. **Convert `InvBoxOrigin()`'s internal viewport read to `VirtualViewport()`.** This is the one
   necessary change — every dependent helper (`InventoryPanelX`, `WeaponSlotRect`, `ArmorSlotRect`,
   `LeftWeaponSlotRect`, `InvBoxDoneRect`, and `ItemRowRect`/`DrawItemList`'s `InvBoxOrigin`
   branches) inherits it automatically with no further code changes to those helpers themselves.
2. **Scope `DrawInventoryWindow()`, `DrawEquipSlots()`, and `DrawInventorySummary()`** each in
   their own scoped, scaled `SpriteBatch` block (the whole method, since each is already
   self-contained and self-guarding — no separable fallback exists for any of the three).
3. **In `DrawItemPanels()`'s loop over `CurrentItemPanels()`**, scope ONLY the
   `panel.Kind == ItemPanelKind.Inventory` case's `DrawItemList`/`DrawWeightReadout` calls in a
   scoped, scaled block — every other panel kind (barter/trade/loot) keeps rendering unscaled,
   exactly as today, since `ItemPanelKind.Inventory` is guaranteed to be the only panel present
   when it appears (per `CurrentItemPanels()`'s if/else-if structure).
4. **Fix the drag-ghost icon**: replace `_previousMouse.X/Y` with a fresh `UiMouse()` call inside
   `DrawEquipSlots()`'s scoped block, matching every other screen's in-Draw position-read
   convention.
5. **In `Update()`**: convert the `InvBoxDoneRect().Contains(mouse.X, mouse.Y)` check (the
   pure-inventory-gated branch only) to `uiMouse.X, uiMouse.Y`. Build a synthetic, UI-scaled
   `MouseState` (the same technique already used for Preferences: `uiMouse.X/Y` plus every other
   field copied from the real `mouse`) at the call site that invokes `HandleInventoryDrag`, and
   pass that instead of the raw `mouse` — `HandleInventoryDrag`'s own body needs no changes.
6. **Leave untouched**: `ItemWindowArt()` and everything that depends on it (`ItemPanelRegion`,
   `PanelPageRows`, `DrawItemWindow`, `LootDoneRect`), the `LootDoneRect`/final-fallback
   `TryClickItemPanel` branches in `Update()`, the barter-specific click branch, and the
   fully-headless fixed-box fallback branches inside `ItemRowRect`/`DrawItemList` — all Piece 2
   territory or pre-existing headless-safe fallback, out of scope here.

## Non-goals

- The loot/barter/trade window (`ItemWindowArt()` and everything downstream) — its own future
  piece, deferred for the same reason the HUD bar was deferred out of Stage 4a: three sub-variants
  sharing one resolver is roughly three times the origin-branch surface for the same migration
  mechanism, and is cleanly separable from this piece at the code level (confirmed above).
- Adding a top-level open-flag guard to `DrawItemPanels()` — confirmed there's no actual
  correctness gap today (every callee self-guards, `CurrentItemPanels()` returns empty when
  closed), so this would be cosmetic scope creep, not something this piece's scaling work needs.
- Worldmap chrome (Stage 5, unrelated).

## Testing

Same as every prior stage: no new pure-math logic, so verification is the existing golden suites
(headless, unaffected — confirmed the `InvBoxOrigin`/`ItemWindowArt` null-branches, which are what
headless/no-game-data runs always hit, are completely untouched by this piece) plus a manual
visual/screenshot check at the Viewer's default 1280x720 window: open the dude's own inventory and
confirm the INVBOX window, equip slots, item list, and (if reachable) the inventory summary panel
all render at the same 1.5× scale as the fifteen already-scaled screens, centered, undistorted,
with hover/click landing under the cursor. Specifically verify: (a) dragging an item from the list
onto an equip slot still works and the ghost icon visibly tracks the cursor at the correct
(scaled) position; (b) clicking the DONE button closes the inventory; (c) opening loot, barter, or
trade afterward still renders those windows at native, unscaled size exactly as before — this is
the check that proves Piece 1's changes didn't leak into Piece 2's still-deferred territory.
