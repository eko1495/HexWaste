# UI Scale — Inventory Piece 1: the Dude's Own Inventory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the dude's own inventory screen (the INVBOX window, equip slots, item list, and
inventory summary) render at the same uniform scale as the fifteen screens already migrated,
filling non-4:3 windows like `fallout2-ce`'s stretch, while the still-deferred loot/barter/trade
window keeps rendering exactly as it does today.

**Architecture:** `InvBoxOrigin()` is the single choke point every dependent helper already calls
— converting it to `VirtualViewport()` propagates correctly to all six dependents with nothing to
keep in lockstep (the opposite risk shape from Stage 3b's duplicated-origin screens).
`DrawInventoryWindow`, `DrawEquipSlots`, and `DrawInventorySummary` are each independently
self-guarding, single-purpose methods — exactly like Skilldex/perk-picker's art-path methods —
and each scopes its own whole body in a scoped, scaled `SpriteBatch` block. The umbrella
`DrawItemPanels()`'s loop over `CurrentItemPanels()` needs a narrower fix: only the
`ItemPanelKind.Inventory` case (guaranteed to be the only panel present when it appears) scopes
into a scaled block; every other panel kind keeps rendering unscaled, since the loot/barter/trade
family is Piece 2, not yet migrated.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`SpriteBatch.Begin`/`End`, already-shipped
`UiScale`/`VirtualViewport`/`UiMouse`/`UiScaleMatrix` from Stage 1/2/3a/3b/3c/4a/HUD-bar).

## Global Constraints

- Scale model: `scale = min(viewportWidth/640, viewportHeight/480)` (Stage 1, shipped,
  unit-tested — do not touch).
- Only the dude's own inventory scales in this plan. The loot/barter/trade window
  (`ItemWindowArt()` and everything downstream: `ItemPanelRegion`, `PanelPageRows`,
  `DrawItemWindow`, `LootDoneRect`) is Piece 2, explicitly deferred — do not touch any of it.
- `InvBoxOrigin()`'s six dependents (`InventoryPanelX`, `WeaponSlotRect`, `ArmorSlotRect`,
  `LeftWeaponSlotRect`, `InvBoxDoneRect`, and `ItemRowRect`/`DrawItemList`'s `InvBoxOrigin`
  branches) need NO code changes of their own — confirmed during planning that all six already
  call `InvBoxOrigin()` rather than independently reading the viewport.
- The fixed, non-viewport-relative fallback rects each of the six helpers returns when
  `_invBox is null` (`new Rectangle(420, 96, 90, 60)`, `x = 40`, etc.) stay exactly as they are —
  they're already headless-safe and viewport-independent, matching the established
  "fallback stays as-is" precedent.
- Every position read from a real device (mouse) or used to lay out scaled content must come
  from `UiMouse()`/`VirtualViewport()` inside a scaled block — never a raw
  `GraphicsDevice.Viewport`/`Mouse.GetState().X/Y` mixed into scaled content.
- In `DrawItemPanels()`'s loop, ONLY the `panel.Kind == ItemPanelKind.Inventory` case scopes into
  a scaled block. `CurrentItemPanels()`'s if/else-if structure guarantees this panel is never
  present alongside a barter/trade/loot panel in the same frame — confirmed during planning.
- In `Update()`'s click-handling cascade, ONLY the branches gated on
  `_inventoryOpen && _lootContainer is null && _tradePartner is null` (the `InvBoxDoneRect` check
  and the `HandleInventoryDrag` call) convert to the scaled mouse. The `LootDoneRect` branch and
  the final fallback `TryClickItemPanel(mouse.X, mouse.Y, shiftHeld)` branch (which handles
  barter/trade/loot's click-to-use) stay on the raw `mouse` — Piece 2 territory.
- `HandleInventoryDrag(MouseState mouse, bool shift)`'s own body needs NO changes — it reads both
  button state and position from the same `mouse` parameter throughout (including the
  `_dragStart` press-position field and its later use in `TryClickItemPanel(_dragStart.X,
  _dragStart.Y, shift)`), so the fix is a synthetic, UI-scaled `MouseState` built once at the
  `Update()` call site (the exact technique already used for Preferences in Stage 3c), not
  changes inside the method.
- No `ViewerGame.Harness.cs` caller exists for any helper this plan touches — confirmed by a
  whole-tree grep during planning.

## File Structure

- `src/Hexwaste.Viewer/ViewerGame.Panels.cs` — `InvBoxOrigin`, `DrawInventoryWindow`,
  `DrawEquipSlots`, `DrawInventorySummary`, `DrawItemPanels`.
- `src/Hexwaste.Viewer/ViewerGame.cs` — the `Update()` click-handling cascade (the
  `InvBoxDoneRect` check and the `HandleInventoryDrag` call site only).

## Task 1: Inventory Piece 1

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs:1939-1942` (`InvBoxOrigin`),
  `:1812-1827` (`DrawItemPanels`), `:1837-1902` (`DrawInventorySummary`),
  `:2188-2204` (`DrawEquipSlots`), `:2208-2236` (`DrawInventoryWindow`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2458-2476` (Update click-handling cascade)

**Interfaces:**
- Consumes: `UiScaleMatrix()`, `VirtualViewport()`, `UiMouse()` (Stage 1/2, already shipped),
  the `Point uiMouse` local already declared in `Update()` (Stage 2 Task 1).
- Produces: nothing new consumed by a later task — this is the only task in this plan.

- [ ] **Step 1: Switch `InvBoxOrigin()` to the virtual viewport**

Find, in `src/Hexwaste.Viewer/ViewerGame.Panels.cs` (around line 1937-1942):

```csharp
    /// <summary>Top-left of the centred INVBOX window when its art is loaded; null = the fallback
    /// boxes-beside-the-list layout (headless / art absent).</summary>
    private Point? InvBoxOrigin() => _invBox is null
        ? null
        : new Point(Math.Max(0, (GraphicsDevice.Viewport.Width - InvBoxW) / 2),
                    Math.Max(0, (GraphicsDevice.Viewport.Height - InvBoxH) / 2));
```

Replace with:

```csharp
    /// <summary>Top-left of the centred INVBOX window when its art is loaded; null = the fallback
    /// boxes-beside-the-list layout (headless / art absent).
    /// Stage: Inventory Piece 1 (UI Scale) — reads the virtual-canvas viewport. Every dependent
    /// helper (InventoryPanelX, WeaponSlotRect, ArmorSlotRect, LeftWeaponSlotRect, InvBoxDoneRect,
    /// and ItemRowRect/DrawItemList's InvBoxOrigin branches) calls this method rather than reading
    /// the viewport independently, so converting it here is sufficient — there is no second copy
    /// to keep in lockstep.</summary>
    private Point? InvBoxOrigin() => _invBox is null
        ? null
        : new Point(Math.Max(0, (VirtualViewport().Width - InvBoxW) / 2),
                    Math.Max(0, (VirtualViewport().Height - InvBoxH) / 2));
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Scope `DrawInventoryWindow` into its own scaled `SpriteBatch` block**

Find, in the same file (around line 2206-2236):

```csharp
    /// <summary>P67: the authentic INVBOX.frm window + the dude paperdoll, drawn behind the item list
    /// when the inventory is open. Lazy-loads the art once; if absent, leaves the fallback layout.</summary>
    private void DrawInventoryWindow()
    {
        if (_fontRenderer is null || !_inventoryOpen || _lootContainer is not null
            || _tradePartner is not null || _barterNpc is not null)
            return;
        if (!_invBoxTried)
        {
            _invBoxTried = true;
            _invBox = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\INVBOX.frm");
        }
        if (InvBoxOrigin() is not { } o)
            return; // art absent -> the fallback boxes layout (DrawEquipSlots at x=420)
        _spriteBatch.Draw(_invBox, new Rectangle(o.X, o.Y, InvBoxW, InvBoxH), Color.White);
        // The dude paperdoll (its current art reflects worn armor), scaled into the body view (176,37,60,100).
        if (_dude?.Dude is { } dude)
        {
            try
            {
                Texture2D doll = _frmCache.GetTexture(dude.Fid, 0, 1); // frame 0, a forward-facing rotation
                var view = new Rectangle(o.X + InvBoxBodyLocal.X, o.Y + InvBoxBodyLocal.Y, InvBoxBodyLocal.Width, InvBoxBodyLocal.Height);
                float scale = Math.Min((float)view.Width / doll.Width, (float)view.Height / doll.Height);
                var size = new Point((int)(doll.Width * scale), (int)(doll.Height * scale));
                _spriteBatch.Draw(doll, new Rectangle(view.X + (view.Width - size.X) / 2, view.Y + (view.Height - size.Y) / 2, size.X, size.Y), Color.White);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
            }
        }
    }
```

Replace with:

```csharp
    /// <summary>P67: the authentic INVBOX.frm window + the dude paperdoll, drawn behind the item list
    /// when the inventory is open. Lazy-loads the art once; if absent, leaves the fallback layout.</summary>
    private void DrawInventoryWindow()
    {
        if (_fontRenderer is null || !_inventoryOpen || _lootContainer is not null
            || _tradePartner is not null || _barterNpc is not null)
            return;
        if (!_invBoxTried)
        {
            _invBoxTried = true;
            _invBox = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\INVBOX.frm");
        }
        if (InvBoxOrigin() is not { } o)
            return; // art absent -> the fallback boxes layout (DrawEquipSlots at x=420)

        // Stage: Inventory Piece 1 (UI Scale) — the whole method scopes into its own scoped,
        // scaled SpriteBatch block, matching the fifteen already-migrated screens. No separable
        // fallback method exists here (the fallback is the fixed-position rects InvBoxOrigin's
        // dependents already return when art is absent, drawn by other methods, not this one).
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        _spriteBatch.Draw(_invBox, new Rectangle(o.X, o.Y, InvBoxW, InvBoxH), Color.White);
        // The dude paperdoll (its current art reflects worn armor), scaled into the body view (176,37,60,100).
        if (_dude?.Dude is { } dude)
        {
            try
            {
                Texture2D doll = _frmCache.GetTexture(dude.Fid, 0, 1); // frame 0, a forward-facing rotation
                var view = new Rectangle(o.X + InvBoxBodyLocal.X, o.Y + InvBoxBodyLocal.Y, InvBoxBodyLocal.Width, InvBoxBodyLocal.Height);
                float scale = Math.Min((float)view.Width / doll.Width, (float)view.Height / doll.Height);
                var size = new Point((int)(doll.Width * scale), (int)(doll.Height * scale));
                _spriteBatch.Draw(doll, new Rectangle(view.X + (view.Width - size.X) / 2, view.Y + (view.Height - size.Y) / 2, size.X, size.Y), Color.White);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
            }
        }

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Scope `DrawEquipSlots` into its own scaled `SpriteBatch` block, and fix the drag-ghost icon**

Find, in the same file (around line 2188-2204):

```csharp
    private void DrawEquipSlots()
    {
        if (_fontRenderer is null || !_inventoryOpen || _lootContainer is not null
            || _tradePartner is not null || _barterNpc is not null)
            return;
        _panelPixel ??= CreatePixel();
        bool onWindow = InvBoxOrigin() is not null;
        bool rightActive = _activeHand == MapObject.FlagInRightHand;
        // P81: two ready weapon hands; the ACTIVE one (which fires) is marked '*'. Armor below them.
        DrawEquipSlot(WeaponSlotRect(), rightActive ? "R-HAND*" : "R-HAND", EquippedInSlot(Formats.Combat.EquipSlot.Weapon), onWindow);
        DrawEquipSlot(LeftWeaponSlotRect(), rightActive ? "L-HAND" : "L-HAND*", EquippedInSlot(Formats.Combat.EquipSlot.WeaponLeft), onWindow);
        DrawEquipSlot(ArmorSlotRect(), "ARMOR", EquippedInSlot(Formats.Combat.EquipSlot.Armor), onWindow);
        // A bright border round the active hand so it's clear which weapon fires.
        DrawRectOutline(rightActive ? WeaponSlotRect() : LeftWeaponSlotRect(), new Color(252, 252, 84));
        if (_dragItem is { } dragged) // the ghost icon follows the cursor (from the last Update mouse)
            DrawItemIcon(dragged, new Rectangle(_previousMouse.X - 14, _previousMouse.Y - 11, 28, 22));
    }
```

Replace with:

```csharp
    private void DrawEquipSlots()
    {
        if (_fontRenderer is null || !_inventoryOpen || _lootContainer is not null
            || _tradePartner is not null || _barterNpc is not null)
            return;

        // Stage: Inventory Piece 1 (UI Scale) — the whole method scopes into its own scoped,
        // scaled SpriteBatch block, matching the fifteen already-migrated screens.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        _panelPixel ??= CreatePixel();
        bool onWindow = InvBoxOrigin() is not null;
        bool rightActive = _activeHand == MapObject.FlagInRightHand;
        // P81: two ready weapon hands; the ACTIVE one (which fires) is marked '*'. Armor below them.
        DrawEquipSlot(WeaponSlotRect(), rightActive ? "R-HAND*" : "R-HAND", EquippedInSlot(Formats.Combat.EquipSlot.Weapon), onWindow);
        DrawEquipSlot(LeftWeaponSlotRect(), rightActive ? "L-HAND" : "L-HAND*", EquippedInSlot(Formats.Combat.EquipSlot.WeaponLeft), onWindow);
        DrawEquipSlot(ArmorSlotRect(), "ARMOR", EquippedInSlot(Formats.Combat.EquipSlot.Armor), onWindow);
        // A bright border round the active hand so it's clear which weapon fires.
        DrawRectOutline(rightActive ? WeaponSlotRect() : LeftWeaponSlotRect(), new Color(252, 252, 84));
        // Stage: Inventory Piece 1 (UI Scale) — reads a fresh UiMouse() instead of the stored,
        // raw-device-pixel _previousMouse, matching every other screen's in-Draw position-read
        // convention (and tracking the CURRENT frame's cursor rather than lagging one frame).
        Point ghostMouse = UiMouse();
        if (_dragItem is { } dragged) // the ghost icon follows the cursor
            DrawItemIcon(dragged, new Rectangle(ghostMouse.X - 14, ghostMouse.Y - 11, 28, 22));

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Scope `DrawInventorySummary` into its own scaled `SpriteBatch` block**

Find, in the same file (around line 1837-1841):

```csharp
    private void DrawInventorySummary()
    {
        if (_fontRenderer is null || _dude is null || InvBoxOrigin() is not { } o)
            return;
        Formats.Combat.CritterState? stats = GetCritterState(_dude.Dude);
        if (stats is null)
            return;
```

Replace with:

```csharp
    private void DrawInventorySummary()
    {
        if (_fontRenderer is null || _dude is null || InvBoxOrigin() is not { } o)
            return;
        Formats.Combat.CritterState? stats = GetCritterState(_dude.Dude);
        if (stats is null)
            return;

        // Stage: Inventory Piece 1 (UI Scale) — the whole method scopes into its own scoped,
        // scaled SpriteBatch block, matching the fifteen already-migrated screens.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());
```

Then find the method's closing (around line 1900-1902):

```csharp
            lineY += lh * 3;
        }
    }
```

Replace with:

```csharp
            lineY += lh * 3;
        }

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 8: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 9: Scope only the `ItemPanelKind.Inventory` case in `DrawItemPanels`'s loop**

Find, in the same file (around line 1812-1827):

```csharp
    private void DrawItemPanels()
    {
        if (_fontRenderer is null)
            return;
        DrawInventoryWindow(); // P67: the INVBOX paperdoll backdrop (behind the list); no-op if the art is absent
        DrawItemWindow();      // P86: the loot/barter/trade FRM backdrop; no-op if the art is absent
        foreach (ItemPanel panel in CurrentItemPanels())
        {
            int bottom = DrawItemList(panel.Title, panel.Items, panel.X, panel.Price);
            if (ReferenceEquals(panel.Items, _dudeInventory)) // the dude's side carries the weight readout (P24)
                DrawWeightReadout(panel.X, bottom);
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

- [ ] **Step 10: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 11: Route the pure-inventory `Update()` hit-tests through `uiMouse`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2458-2476):

```csharp
            bool clickPress = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            // The INVBOX DONE button closes the pure inventory (the baked-in art button, inventory.cc).
            if (clickPress && _inventoryOpen && _lootContainer is null && _tradePartner is null
                && InvBoxDoneRect() is { } done && done.Contains(mouse.X, mouse.Y))
            {
                _inventoryOpen = false;
                _stealTarget = null;
            }
            // P111: the LOOT window's DONE button (fo2ce inventory.cc:1052-1066 — fires KEY_ESCAPE).
            else if (clickPress && _lootContainer is not null
                && LootDoneRect() is { } lootDone && lootDone.Contains(mouse.X, mouse.Y))
            {
                _lootContainer = null;
                _stealTarget = null;
            }
            else if (_inventoryOpen && _lootContainer is null && _tradePartner is null)
                HandleInventoryDrag(mouse, shiftHeld);
            else if (clickPress)
                TryClickItemPanel(mouse.X, mouse.Y, shiftHeld);
```

Replace with:

```csharp
            bool clickPress = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            // The INVBOX DONE button closes the pure inventory (the baked-in art button, inventory.cc).
            // Stage: Inventory Piece 1 (UI Scale) — uiMouse.X/Y here, since InvBoxDoneRect() now
            // reads VirtualViewport() via InvBoxOrigin(). LootDoneRect (below) and the final
            // TryClickItemPanel fallback stay on the raw mouse -- Piece 2 (loot/barter/trade)
            // territory, not yet migrated.
            if (clickPress && _inventoryOpen && _lootContainer is null && _tradePartner is null
                && InvBoxDoneRect() is { } done && done.Contains(uiMouse.X, uiMouse.Y))
            {
                _inventoryOpen = false;
                _stealTarget = null;
            }
            // P111: the LOOT window's DONE button (fo2ce inventory.cc:1052-1066 — fires KEY_ESCAPE).
            else if (clickPress && _lootContainer is not null
                && LootDoneRect() is { } lootDone && lootDone.Contains(mouse.X, mouse.Y))
            {
                _lootContainer = null;
                _stealTarget = null;
            }
            else if (_inventoryOpen && _lootContainer is null && _tradePartner is null)
            {
                // Stage: Inventory Piece 1 (UI Scale) — HandleInventoryDrag reads both button
                // state and position from the SAME MouseState parameter throughout its body
                // (including the drag-start Point it stores and later compares against the
                // now-virtual-canvas ItemRowRect), so a synthetic, UI-scaled MouseState carries
                // uiMouse's position through unchanged, exactly like Stage 3c's Preferences fix —
                // no changes needed inside HandleInventoryDrag itself.
                MouseState uiInvMouse = new(uiMouse.X, uiMouse.Y, mouse.ScrollWheelValue,
                    mouse.LeftButton, mouse.MiddleButton, mouse.RightButton,
                    mouse.XButton1, mouse.XButton2, mouse.HorizontalScrollWheelValue);
                HandleInventoryDrag(uiInvMouse, shiftHeld);
            }
            else if (clickPress)
                TryClickItemPanel(mouse.X, mouse.Y, shiftHeld);
```

- [ ] **Step 12: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 13: Run the golden suites**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Run: `FALLOUT2_DIR=./game-data scripts/encounter-golden.sh check`
Expected: both report all scenarios passing, byte-identical — headless runs never call `Draw()`,
and `InvBoxOrigin()`/`ItemWindowArt()`'s null-branches (which every headless/no-game-data
scenario hits) are completely untouched by this task. The `--panel-click`/`--drag-equip` goldens
specifically exercise `TryClickItemPanel`/`HandleInventoryDrag`'s underlying dispatch logic
directly via CLI actions (not a simulated screen click), so they're unaffected by this task's
coordinate-space changes; a pass confirms no regression in that dispatch logic sitting next to
this diff.

- [ ] **Step 14: Manual visual check — inventory at a non-4:3 window, and a Piece 2 non-regression check**

Run:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/inventory-piece1.png \
  -- --create 5,5,5,5,5,5,5:0,4,5:0 --hud-click INV
```
Read the resulting PNG. Expected: the INVBOX window, equip slots (weapon/left-hand/armor), item
list, and paperdoll all render at 1.5× baseline, centered, undistorted. If a live/scripted drag
can be simulated, drag an item from the list onto an equip slot and confirm the ghost icon
visibly tracks the cursor at the correct (scaled) position, and that the drop actually equips the
item. Confirm clicking the DONE button (scaled position) closes the inventory.

Then confirm Piece 2 is unaffected: open loot, barter, or trade (via whatever CLI/live path
reaches them) and confirm those windows still render at native, unscaled size exactly as before
this task — this is the check that proves this task's changes didn't leak into the still-deferred
loot/barter/trade family.

- [ ] **Step 15: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Panels.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the dude's own inventory to fill non-4:3 windows

Wires Stage 1/2/3a/3b/3c/4a/HUD-bar's UiScale infrastructure into the
dude's own inventory screen (INVBOX window, equip slots, item list,
inventory summary). InvBoxOrigin() -- the single choke point every
dependent helper (InventoryPanelX, WeaponSlotRect, ArmorSlotRect,
LeftWeaponSlotRect, InvBoxDoneRect) already calls rather than reading
the viewport independently -- now reads VirtualViewport(), propagating
correctly to all six dependents with nothing to keep in lockstep.
DrawInventoryWindow/DrawEquipSlots/DrawInventorySummary each scope
their whole body in a scoped, scaled SpriteBatch block; the drag-ghost
icon switches from the stale, raw-device _previousMouse to a fresh
UiMouse() call, matching every other screen's convention.
DrawItemPanels()'s per-panel loop scopes ONLY the ItemPanelKind.
Inventory case, since CurrentItemPanels()'s if/else-if structure
guarantees that panel never coexists with barter/trade/loot in the
same frame -- every other panel kind keeps rendering unscaled.
HandleInventoryDrag's Update() call site now passes a synthetic,
UI-scaled MouseState (the Stage 3c Preferences technique) rather than
changing the method's own body.

This is Piece 1 of Inventory (deferred since Stage 3a as the
highest-risk remaining screen). The loot/barter/trade window
(ItemWindowArt() and its dependents) remains its own deferred piece.
Worldmap chrome (Stage 5) is the only other unscaled screen left.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```
