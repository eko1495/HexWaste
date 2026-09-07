# UI Scale — Inventory, Piece 2: Loot, Barter, and Trade

## Problem

Sixteen screens now render at a uniform scale filling non-4:3 windows. Piece 1 of Inventory (the
dude's own inventory) shipped, deliberately leaving the loot/barter/trade window family deferred
as its own piece — three sub-variants sharing one resolver, roughly three times the
origin-branch surface of Piece 1 for the same migration mechanism. This spec covers that
remaining piece.

## Grounding (confirmed by a direct source-tree read this session, post-Piece-1)

- **`ItemWindowArt()`** (`ViewerGame.Panels.cs:2028-2050`) is the single choke point for this
  piece — exactly the same shape as Piece 1's `InvBoxOrigin()`:
  ```csharp
  private (Texture2D Tex, Point Origin, bool Strip)? ItemWindowArt()
  {
      int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
      if (_lootContainer is not null) { ... return (_lootBox, new Point(...), false); }
      if (_barterNpc is not null) { ... return (_barterBox, new Point(...), true); }
      if (_tradePartner is not null) { ... return (_tradeBox, new Point(...), true); }
      return null;
  }
  ```
  Its dependents — `ItemPanelRegion(int logicalX)` (`:2056-2065`), `PanelPageRows()` (`:2071`),
  `LootDoneRect()` (`:2001-2004`), and the `ItemPanelRegion`-branches of the shared
  `ItemRowRect`/`DrawItemList` — all call `ItemWindowArt()` rather than independently reading the
  viewport. Converting `ItemWindowArt()` alone (to `VirtualViewport()`) propagates correctly to
  every dependent, with nothing to keep in lockstep — the same low-risk shape Piece 1 already
  proved out.
- **`DrawItemWindow()`** (`:2358-2379`) is self-contained and self-guarding
  (`ItemWindowArt() is not { } w → return`), drawing the window backdrop plus (for loot only) a
  separate little DONE-button overlay (`lilredup.frm`). No separate fallback method exists — when
  art is absent, this method simply draws nothing (the "dark box" fallback is drawn elsewhere, by
  `DrawItemList`'s third branch — see below). The whole method scopes as one scaled block.
- **A pre-existing quirk, noted but out of scope to fix**: `OpenTrade()` (`ViewerGame.
  CompanionHub.cs:19-29`) sets BOTH `_tradePartner` and `_lootContainer = follower`.
  `ItemWindowArt()` checks `_lootContainer is not null` before `_tradePartner is not null`, so
  trade currently resolves to the LOOT art branch (`loot.frm`, `Strip: false`), not the
  `trade.frm` strip branch — meaning the `_tradePartner` branch and `TradeStripW`/`TradeStripH`
  for trade specifically may be effectively unreachable today. This is a pre-existing behavior
  question unrelated to UI scaling — converting `ItemWindowArt()`'s viewport read is correct
  regardless of which internal branch ends up executing, so this piece does not need to
  investigate or fix it.
- **`DrawItemPanels()`'s per-panel loop** (`ViewerGame.Panels.cs:1812-1847`, as Piece 1 left it)
  currently reads:
  ```csharp
  foreach (ItemPanel panel in CurrentItemPanels())
  {
      if (panel.Kind == ItemPanelKind.Inventory) { /* scaled, Piece 1 */ }
      else { /* unscaled — barter/trade/loot, Piece 2 */ }
  }
  ```
  Once this piece scales the `else` branch too, BOTH branches become identical scoped-batch code
  — the if/else distinction Piece 1 introduced can collapse back into a single unconditional
  scoped block wrapping the whole loop body, since every panel kind now scales the same way.
  (Piece 1 scoped by `panel.Kind`, not by "did the art actually load" — a deliberate simplification
  already accepted without objection in Piece 1's review; this piece follows the same convention
  for consistency, rather than introducing a new art-presence check here.)
- **`Update()`'s hit-test call sites needing conversion**, all currently on raw `mouse`:
  - `ViewerGame.cs:2398` — the barter branch's `TryClickItemPanel(mouse.X, mouse.Y, shift)`.
  - `ViewerGame.cs:2472` — `LootDoneRect().Contains(mouse.X, mouse.Y)` (gated on
    `_lootContainer is not null`, which covers both loot AND trade per the quirk above).
  - `ViewerGame.cs:2491` — the final fallback `TryClickItemPanel(mouse.X, mouse.Y, shiftHeld)`
    (handles loot/trade's click-to-use, now that Piece 1's `HandleInventoryDrag` branch above it
    only fires for pure inventory).
  None of these three sites has a draw-time hover-highlight counterpart to convert — confirmed
  `DrawItemList`'s `ItemPanelRegion` branch draws every row in a single static color, with no
  `Mouse.GetState()` hover read (unlike Skilldex/perk-picker's row highlighting).
- No `ViewerGame.Harness.cs` caller exists for `ItemWindowArt`, `ItemPanelRegion`,
  `PanelPageRows`, `DrawItemWindow`, or `LootDoneRect` — confirmed by the same whole-tree grep
  Piece 1's planning already ran (unchanged since then).

## Design

1. **Convert `ItemWindowArt()`'s internal viewport read to `VirtualViewport()`.** This is the one
   necessary change — `ItemPanelRegion`, `PanelPageRows`, `LootDoneRect`, and `ItemRowRect`/
   `DrawItemList`'s `ItemPanelRegion` branches inherit it automatically.
2. **Scope `DrawItemWindow()`'s whole body** in a scoped, scaled `SpriteBatch` block (no
   separable fallback exists, matching the save/load/Pip-Boy/options shape).
3. **Collapse `DrawItemPanels()`'s per-panel `if/else` back to one unconditional scoped block**
   around the whole `foreach` loop body, now that every `ItemPanel` kind scales the same way —
   removing the `if (panel.Kind == ItemPanelKind.Inventory)` distinction Piece 1 introduced, since
   it's no longer needed once both branches are identical.
4. **Convert the three `Update()` hit-test call sites** (the barter click, `LootDoneRect`, and the
   final loot/trade fallback click) from raw `mouse` to the existing `uiMouse` local.

## Non-goals

- Investigating or fixing the `OpenTrade`/`ItemWindowArt()` branch-ordering quirk noted above —
  unrelated to UI scaling, and correct to leave as-is for this piece.
- Worldmap chrome (Stage 5, unrelated).
- Any change to `ViewerGame.Harness.cs` — confirmed no coordinate-math caller of any helper this
  piece touches.

## Testing

Same as every prior piece: no new pure-math logic, so verification is the existing golden suites
(headless, unaffected — confirmed `ItemWindowArt()`'s null-branch, which every headless/
no-game-data scenario hits, is untouched by this piece) plus a manual visual/screenshot check at
the Viewer's default 1280x720 window: open loot, then barter, then trade, and confirm each
window's art, item list, and (for loot) the DONE-button overlay all render at the same 1.5× scale
as the sixteen already-scaled screens, centered/bottom-anchored per their existing layout,
undistorted, with clicking a row and the DONE/close controls landing under the cursor. This also
completes Inventory as a whole — after this piece, confirm the dude's own inventory (Piece 1) and
loot/barter/trade (this piece) both scale identically and can be reached from one another (e.g.
opening a container while the dude's inventory is closed, or vice versa) without any visual
mismatch.
