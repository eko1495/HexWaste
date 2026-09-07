# UI Scale — Inventory Piece 2: Loot, Barter, and Trade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the loot, barter, and trade window (`ItemWindowArt()` and everything downstream)
render at the same uniform scale as the sixteen screens already migrated, filling non-4:3 windows
like `fallout2-ce`'s stretch. This closes out Inventory entirely — after this plan, only worldmap
chrome (Stage 5) remains unscaled.

**Architecture:** `ItemWindowArt()` is the single choke point every dependent
(`ItemPanelRegion`, `PanelPageRows`, `LootDoneRect`) already calls — exactly the same low-risk
shape Piece 1's `InvBoxOrigin()` already proved out. Converting it alone propagates correctly to
every dependent with nothing to keep in lockstep. `DrawItemWindow()` scopes its whole body (no
separable fallback exists). Once this piece scales the loot/barter/trade family too,
`DrawItemPanels()`'s per-panel `if (panel.Kind == ItemPanelKind.Inventory) {...scaled...} else
{...unscaled...}` split — which Piece 1 introduced specifically to keep the two families
independent — collapses back into one unconditional scoped block, since every panel kind now
scales identically.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`SpriteBatch.Begin`/`End`, already-shipped
`UiScale`/`VirtualViewport`/`UiMouse`/`UiScaleMatrix` from Stage 1 through Inventory Piece 1).

## Global Constraints

- Scale model: `scale = min(viewportWidth/640, viewportHeight/480)` (Stage 1, shipped,
  unit-tested — do not touch).
- Only the loot/barter/trade window scales in this plan — it's the last unscaled piece of
  Inventory. Worldmap chrome (Stage 5) is unrelated and out of scope.
- `ItemWindowArt()`'s dependents (`ItemPanelRegion`, `PanelPageRows`, `LootDoneRect`, and the
  `ItemPanelRegion`-branches of `ItemRowRect`/`DrawItemList`) need NO code changes of their own —
  confirmed during planning that all of them already call `ItemWindowArt()` rather than
  independently reading the viewport.
- `DrawItemPanels()`'s `if (panel.Kind == ItemPanelKind.Inventory)` split (from Piece 1) collapses
  into one unconditional scoped block in this plan — do not leave the `if`/`else` in place once
  both branches would be identical.
- Every position read from a real device (mouse) or used to lay out scaled content must come
  from `UiMouse()`/`VirtualViewport()` inside a scaled block — never a raw
  `GraphicsDevice.Viewport`/`Mouse.GetState().X/Y` mixed into scaled content.
- A pre-existing, unrelated quirk (`OpenTrade()` sets both `_tradePartner` and `_lootContainer`,
  so `ItemWindowArt()`'s `_lootContainer`-first branch order may make the `_tradePartner` branch
  effectively unreachable for trade) is confirmed out of scope — do not investigate or fix it in
  this plan; converting `ItemWindowArt()`'s viewport read is correct regardless of which internal
  branch executes.
- No `ViewerGame.Harness.cs` caller exists for any helper this plan touches — confirmed by a
  whole-tree grep during planning (unchanged since Piece 1's equivalent grep).

## File Structure

- `src/Hexwaste.Viewer/ViewerGame.Panels.cs` — `ItemWindowArt`, `DrawItemWindow`,
  `DrawItemPanels`.
- `src/Hexwaste.Viewer/ViewerGame.cs` — three `Update()` hit-test call sites (the barter click,
  `LootDoneRect`, and the final loot/trade fallback click).

## Task 1: Loot, barter, and trade

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs:2028-2050` (`ItemWindowArt`),
  `:2358-2379` (`DrawItemWindow`), `:1812-1847` (`DrawItemPanels`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2398` (barter click), `:2472` (`LootDoneRect`),
  `:2491` (final fallback click)

**Interfaces:**
- Consumes: `UiScaleMatrix()`, `VirtualViewport()`, `UiMouse()` (Stage 1/2, already shipped),
  the `Point uiMouse` local already declared in `Update()` (Stage 2 Task 1).
- Produces: nothing new consumed by a later task — this is the only task in this plan, and the
  last task in the whole Inventory effort.

- [ ] **Step 1: Switch `ItemWindowArt()`'s viewport read to the virtual viewport**

Find, in `src/Hexwaste.Viewer/ViewerGame.Panels.cs` (around line 2025-2050):

```csharp
    /// <summary>The active loot/barter/trade FRM backdrop + its top-left screen placement (and whether it
    /// is a bottom strip), or null when no such panel is up OR the art is absent (headless) — in which case
    /// the item panels fall back to the dark text boxes and every existing golden stays byte-identical.</summary>
    private (Texture2D Tex, Point Origin, bool Strip)? ItemWindowArt()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        if (_lootContainer is not null)
        {
            if (!_lootBoxTried) { _lootBoxTried = true; _lootBox = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\loot.frm"); }
            return _lootBox is null ? null
                : (_lootBox, new Point(Math.Max(0, (vw - LootBoxW) / 2), Math.Max(0, (vh - LootBoxH) / 2)), false);
        }
        if (_barterNpc is not null)
        {
            if (!_barterBoxTried) { _barterBoxTried = true; _barterBox = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\barter.frm"); }
            return _barterBox is null ? null
                : (_barterBox, new Point((vw - TradeStripW) / 2, Math.Max(0, vh - TradeStripH)), true);
        }
        if (_tradePartner is not null)
        {
            if (!_tradeBoxTried) { _tradeBoxTried = true; _tradeBox = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\trade.frm"); }
            return _tradeBox is null ? null
                : (_tradeBox, new Point((vw - TradeStripW) / 2, Math.Max(0, vh - TradeStripH)), true);
        }
        return null;
    }
```

Replace with:

```csharp
    /// <summary>The active loot/barter/trade FRM backdrop + its top-left screen placement (and whether it
    /// is a bottom strip), or null when no such panel is up OR the art is absent (headless) — in which case
    /// the item panels fall back to the dark text boxes and every existing golden stays byte-identical.
    /// Stage: Inventory Piece 2 (UI Scale) — reads the virtual-canvas viewport. Every dependent
    /// (ItemPanelRegion, PanelPageRows, LootDoneRect, and ItemRowRect/DrawItemList's
    /// ItemPanelRegion branches) calls this method rather than reading the viewport independently,
    /// so converting it here is sufficient — there is no second copy to keep in lockstep.</summary>
    private (Texture2D Tex, Point Origin, bool Strip)? ItemWindowArt()
    {
        int vw = VirtualViewport().Width, vh = VirtualViewport().Height;
        if (_lootContainer is not null)
        {
            if (!_lootBoxTried) { _lootBoxTried = true; _lootBox = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\loot.frm"); }
            return _lootBox is null ? null
                : (_lootBox, new Point(Math.Max(0, (vw - LootBoxW) / 2), Math.Max(0, (vh - LootBoxH) / 2)), false);
        }
        if (_barterNpc is not null)
        {
            if (!_barterBoxTried) { _barterBoxTried = true; _barterBox = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\barter.frm"); }
            return _barterBox is null ? null
                : (_barterBox, new Point((vw - TradeStripW) / 2, Math.Max(0, vh - TradeStripH)), true);
        }
        if (_tradePartner is not null)
        {
            if (!_tradeBoxTried) { _tradeBoxTried = true; _tradeBox = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\trade.frm"); }
            return _tradeBox is null ? null
                : (_tradeBox, new Point((vw - TradeStripW) / 2, Math.Max(0, vh - TradeStripH)), true);
        }
        return null;
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Scope `DrawItemWindow()` into its own scaled `SpriteBatch` block**

Find, in the same file (around line 2358-2379):

```csharp
    private void DrawItemWindow()
    {
        if (ItemWindowArt() is not { } w)
            return;
        int wide = w.Strip ? TradeStripW : LootBoxW;
        int high = w.Strip ? TradeStripH : LootBoxH;
        _spriteBatch.Draw(w.Tex, new Rectangle(w.Origin.X, w.Origin.Y, wide, high), Color.White);

        // P111: the LOOT window's DONE button is a separate little-red-button FRM the engine overlays
        // at (476,331) next to the baked-in DONE plate (inventory.cc:1052-1066 with interface FID 8) —
        // loot.frm itself ships without the button, which is why it looked missing.
        if (!w.Strip && _lootContainer is not null)
        {
            if (!_lilRedTried)
            {
                _lilRedTried = true;
                _lilRedUp = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\lilredup.frm");
            }
            if (_lilRedUp is not null)
                _spriteBatch.Draw(_lilRedUp, new Vector2(w.Origin.X + 476, w.Origin.Y + 331), Color.White);
        }
    }
```

Replace with:

```csharp
    private void DrawItemWindow()
    {
        if (ItemWindowArt() is not { } w)
            return;

        // Stage: Inventory Piece 2 (UI Scale) — no separable fallback exists here (the "dark box"
        // fallback is drawn elsewhere, by DrawItemList's own third branch, not this method), so the
        // whole method scopes into its own scoped, scaled SpriteBatch block.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        int wide = w.Strip ? TradeStripW : LootBoxW;
        int high = w.Strip ? TradeStripH : LootBoxH;
        _spriteBatch.Draw(w.Tex, new Rectangle(w.Origin.X, w.Origin.Y, wide, high), Color.White);

        // P111: the LOOT window's DONE button is a separate little-red-button FRM the engine overlays
        // at (476,331) next to the baked-in DONE plate (inventory.cc:1052-1066 with interface FID 8) —
        // loot.frm itself ships without the button, which is why it looked missing.
        if (!w.Strip && _lootContainer is not null)
        {
            if (!_lilRedTried)
            {
                _lilRedTried = true;
                _lilRedUp = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\lilredup.frm");
            }
            if (_lilRedUp is not null)
                _spriteBatch.Draw(_lilRedUp, new Vector2(w.Origin.X + 476, w.Origin.Y + 331), Color.White);
        }

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Collapse `DrawItemPanels()`'s per-panel `if`/`else` back to one unconditional scoped block**

Find, in the same file (around line 1812-1847):

```csharp
    private void DrawItemPanels()
    {
        if (_fontRenderer is null)
            return;
        DrawInventoryWindow(); // P67: the INVBOX paperdoll backdrop (behind the list); no-op if the art is absent
        DrawItemWindow();      // P86: the loot/barter/trade FRM backdrop; no-op if the art is absent
        foreach (ItemPanel panel in CurrentItemPanels())
        {
            // Stage: Inventory Piece 1 (UI Scale) — ONLY the dude's-own-inventory panel scopes
            // into a scaled SpriteBatch block; every other panel kind (barter/trade/loot, Piece 2,
            // not yet migrated) keeps rendering unscaled. CurrentItemPanels()'s if/else-if
            // structure guarantees ItemPanelKind.Inventory is never present alongside another
            // panel kind in the same frame, so this scoping never needs to nest.
            if (panel.Kind == ItemPanelKind.Inventory)
            {
                _spriteBatch.End();
                _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

                int scaledBottom = DrawItemList(panel.Title, panel.Items, panel.X, panel.Price);
                if (ReferenceEquals(panel.Items, _dudeInventory))
                    DrawWeightReadout(panel.X, scaledBottom);

                _spriteBatch.End();
                _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            }
            else
            {
                int bottom = DrawItemList(panel.Title, panel.Items, panel.X, panel.Price);
                if (ReferenceEquals(panel.Items, _dudeInventory)) // the dude's side carries the weight readout (P24)
                    DrawWeightReadout(panel.X, bottom);
            }
        }
        DrawEquipSlots(); // P47: the weapon/armor equip slots + the dragged-item ghost
        if (_inventoryOpen && _lootContainer is null && _tradePartner is null && _barterNpc is null)
            DrawInventorySummary(); // the dude's own plain INV screen only, matching DrawEquipSlots' gate
    }
```

Replace with:

```csharp
    private void DrawItemPanels()
    {
        if (_fontRenderer is null)
            return;
        DrawInventoryWindow(); // P67: the INVBOX paperdoll backdrop (behind the list); no-op if the art is absent
        DrawItemWindow();      // P86: the loot/barter/trade FRM backdrop; no-op if the art is absent
        // Stage: Inventory Piece 2 (UI Scale) — every ItemPanel kind now scales the same way (the
        // Piece 1 if/else split between ItemPanelKind.Inventory and everything else is no longer
        // needed), so the whole loop body scopes into one scoped, scaled SpriteBatch block.
        // CurrentItemPanels() returns at most one panel kind's worth of panels per frame (it's an
        // if/else-if chain), so this loop's iterations never mix scaled and unscaled content.
        if (CurrentItemPanels() is { Count: > 0 } panels)
        {
            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

            foreach (ItemPanel panel in panels)
            {
                int bottom = DrawItemList(panel.Title, panel.Items, panel.X, panel.Price);
                if (ReferenceEquals(panel.Items, _dudeInventory)) // the dude's side carries the weight readout (P24)
                    DrawWeightReadout(panel.X, bottom);
            }

            _spriteBatch.End();
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        }
        DrawEquipSlots(); // P47: the weapon/armor equip slots + the dragged-item ghost
        if (_inventoryOpen && _lootContainer is null && _tradePartner is null && _barterNpc is null)
            DrawInventorySummary(); // the dude's own plain INV screen only, matching DrawEquipSlots' gate
    }
```

(Note: `CurrentItemPanels()` is now called twice per frame — once for the `is { Count: > 0 }` check
and once implicitly via the `foreach`'s own evaluation of `panels`, which is the SAME list
instance captured by the pattern match, not a second call. `panels` is bound once by the
`is { Count: > 0 } panels` pattern and reused by the `foreach` — confirm this compiles as written;
if the compiler complains, declare `List<ItemPanel> panels = CurrentItemPanels();` on its own line
above the `if (panels.Count > 0)` instead, and use `panels` in the `foreach` — either form calls
`CurrentItemPanels()` exactly once.)

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Route the three `Update()` hit-tests through `uiMouse`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2397-2398, the barter branch):

```csharp
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
                TryClickItemPanel(mouse.X, mouse.Y, shift);
```

Replace with:

```csharp
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
                TryClickItemPanel(uiMouse.X, uiMouse.Y, shift);
```

Then find (around line 2470-2476, the loot DONE-button check):

```csharp
            // P111: the LOOT window's DONE button (fo2ce inventory.cc:1052-1066 — fires KEY_ESCAPE).
            else if (clickPress && _lootContainer is not null
                && LootDoneRect() is { } lootDone && lootDone.Contains(mouse.X, mouse.Y))
            {
                _lootContainer = null;
                _stealTarget = null;
            }
```

Replace with:

```csharp
            // P111: the LOOT window's DONE button (fo2ce inventory.cc:1052-1066 — fires KEY_ESCAPE).
            else if (clickPress && _lootContainer is not null
                && LootDoneRect() is { } lootDone && lootDone.Contains(uiMouse.X, uiMouse.Y))
            {
                _lootContainer = null;
                _stealTarget = null;
            }
```

Then find (around line 2490-2491, the final loot/trade fallback click):

```csharp
            else if (clickPress)
                TryClickItemPanel(mouse.X, mouse.Y, shiftHeld);
```

Replace with:

```csharp
            else if (clickPress)
                TryClickItemPanel(uiMouse.X, uiMouse.Y, shiftHeld);
```

(This is the third and last raw-`mouse` position read in this cascade — the comment above Piece
1's `InvBoxDoneRect` conversion, which said "LootDoneRect and the final TryClickItemPanel fallback
stay on the raw mouse — Piece 2 territory, not yet migrated," is now stale; feel free to remove
or update it as part of this step since Piece 2 is what's landing now, though this is not
required for correctness.)

- [ ] **Step 8: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 9: Run the golden suites**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Run: `FALLOUT2_DIR=./game-data scripts/encounter-golden.sh check`
Expected: both report all scenarios passing, byte-identical — headless runs never call `Draw()`,
and `ItemWindowArt()`'s null-branch (which every headless/no-game-data scenario hits) is
completely untouched by this task. The `--panel-click`/barter/loot goldens specifically exercise
`TryClickItemPanel`'s underlying dispatch logic directly via CLI actions (not a simulated screen
click), so they're unaffected by this task's coordinate-space changes; a pass confirms no
regression in that dispatch logic sitting next to this diff.

- [ ] **Step 10: Manual visual check — loot, barter, and trade at a non-4:3 window**

Run, for loot (check `src/Hexwaste.Viewer/Program.cs`/`ViewerGame.Harness.cs` for the exact
flag/action if this doesn't match — search for how `_lootContainer` gets set from the CLI):
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/inventory-piece2-loot.png \
  -- --goto <a-tile-near-a-container> --use-hex <container-hex>
```
Read the resulting PNG. Expected: `loot.frm` renders at 1.5× baseline, centered, undistorted, the
DONE-button overlay correctly positioned, item lists in both columns legible.

Repeat similarly for barter (`--open-barter`-style flag, check `Program.cs`/`docs/
fo2ce-comparison-playbook.md` for the exact working invocation used earlier in this project's
history) and confirm `barter.frm`'s bottom strip renders at 1.5× baseline, bottom-anchored,
undistorted. If a live/scripted trade with a companion can be reached, confirm the same for
`trade.frm` (or whichever art `ItemWindowArt()` actually resolves for trade, per this plan's
Global Constraints note about the `_lootContainer`-first branch order).

For every window reached, confirm clicking a row and clicking DONE/closing lands where the
cursor visually sits — this is the check that proves the three `Update()` conversions (Step 7)
agree with the scaled draw.

- [ ] **Step 11: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Panels.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the loot/barter/trade window to fill non-4:3 windows

Wires the UiScale infrastructure into ItemWindowArt() -- the single
choke point every dependent (ItemPanelRegion, PanelPageRows,
LootDoneRect) already calls rather than reading the viewport
independently, the same low-risk shape Inventory Piece 1's
InvBoxOrigin() already proved out. DrawItemWindow() scopes its whole
body (no separable fallback exists). DrawItemPanels()'s per-panel
if/else split (introduced by Piece 1 to keep the dude's-own-inventory
and loot/barter/trade families scaling independently) collapses back
into one unconditional scoped block, since every panel kind now
scales identically. The three remaining raw-mouse Update() hit-tests
(the barter click, LootDoneRect, and the final loot/trade fallback
click) switch to the existing uiMouse local.

This closes out Inventory entirely (Piece 1 + Piece 2). Only worldmap
chrome (Stage 5) remains unscaled.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```
