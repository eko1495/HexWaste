# Inventory summary panel fidelity fixes — design

## Problem

`DrawInventorySummary()` (`src/Hexwaste.Viewer/ViewerGame.Panels.cs:1861-1934`, the
SPECIAL/HP/AC/resistance panel beside the inventory paperdoll) has three related bugs,
all root-caused via direct comparison against fo2ce's `inventoryRenderSummary()`
(`reference/fallout2-ce/src/inventory.cc:2891-3178`, pinned base tree) during today's
real-game side-by-side review:

1. **Wrong color** — the whole panel renders in pale white instead of vanilla's pure
   green.
2. **Layout overlap** — the inventory box overlaps the HUD interface bar by ~47px at
   the reference resolution.
3. **Misplaced weight readout** — "Total Wt:" is drawn in the wrong panel (the
   item-list column, not the summary block), which combined with bug 2's overlap makes
   it visually spill into the HUD bar.

## Bug 1: color

`DrawInventorySummary()` draws every line of text — name, SPECIAL stats, HP, AC, and
all five resistance rows — using a single `pale` color:

```csharp
var pale = new Color(252, 252, 252); // the INVBOX readout's pale text (_colorTable[992])
```

(`Panels.cs:1874`.) The comment is simply wrong. Vanilla's `inventoryRenderSummary()`
uses `_colorTable[992]` throughout (`inventory.cc:2905-3176`), and `_colorTable[992]`
decodes (RGB555: `(r<<10)|(g<<5)|b` = `0b0_00000_11111_00000`) to pure green, not
white. Hexwaste's own code already documents this correctly elsewhere:
`ObjectType.Wall => new Color(0, 248, 0), // _colorTable[992]` (`Panels.cs:1052`).

**Fix**: change the `pale` variable's value to green, matching that existing correct
usage:

```csharp
var pale = new Color(0, 248, 0); // the INVBOX readout's green text (_colorTable[992])
```

Nothing else in the method changes — every text draw already routes through this one
variable.

## Bug 2: layout overlap with the HUD bar

`InvBoxOrigin()` (`Panels.cs:1976-1979`) is the single source of truth for the
inventory box's screen position — every dependent element (item list, armor/weapon
slots, paperdoll, DONE button) reads it:

```csharp
private Point? InvBoxOrigin() => _invBox is null
    ? null
    : new Point(Math.Max(0, (VirtualViewport().Width - InvBoxW) / 2),
                Math.Max(0, (VirtualViewport().Height - InvBoxH) / 2));
```

It centers vertically within the *full* virtual viewport height. At the reference
480-tall virtual canvas (`InvBoxH = 377`), this computes `Y = 51`, so the box's bottom
edge lands at `51 + 377 = 428`. The HUD interface bar's own top edge is at
`viewport.Height - InterfaceBar.Height = 480 - 99 = 381` (`InterfaceBar.cs:24,66-67`).
`428 > 381` — a **~47px overlap**.

Vanilla avoids this differently: at/near its native 640×480 resolution it doesn't
center at all — it pins `Y=0` (`inventory.cc:748-756`). That works for vanilla because
of its own architecture (a fixed low logical resolution, GPU-stretched to the real
display — confirmed in this session's earlier fullscreen investigation), which
Hexwaste's resolution-independent virtual-canvas model doesn't share, so literally
porting that special case wouldn't fit Hexwaste's actual rendering approach.

**Fix**: center the box within the space *above* the HUD bar instead of the full
viewport:

```csharp
private Point? InvBoxOrigin() => _invBox is null
    ? null
    : new Point(Math.Max(0, (VirtualViewport().Width - InvBoxW) / 2),
                Math.Max(0, (VirtualViewport().Height - InterfaceBar.Height - InvBoxH) / 2));
```

At the reference canvas this computes `Y = (480 - 99 - 377) / 2 = 2`, so the box's
bottom lands at `2 + 377 = 379 < 381` — clear of the bar, with almost exactly vanilla's
own ~3px clearance, but derived from Hexwaste's own geometry rather than vanilla's
native-resolution special case. Every dependent element moves consistently since they
all read this one method.

## Bug 3: "Total Wt:" drawn in the wrong panel

Vanilla draws the weight readout as the *final row inside*
`inventoryRenderSummary()` itself (`inventory.cc:3160-3178`) — the same panel as HP/AC/
resistances, using that panel's own running coordinate accumulator.

Hexwaste instead draws it via a separate call, anchored to the bottom of the
*item-list* panel:

```csharp
if (ReferenceEquals(panel.Items, _dudeInventory)) // the dude's side carries the weight readout (P24)
    DrawWeightReadout(panel.X, bottom);
```

(`Panels.cs:1841-1842`, inside `DrawItemPanels()`'s loop over `CurrentItemPanels()`.)
This condition fires whenever the dude's own inventory list is one of the currently
open panels — which includes both the plain INV screen *and* loot/trade/barter
sessions (where the dude's inventory list panel sits alongside a container/NPC panel).
`DrawInventorySummary()`, by contrast, is gated to the plain INV screen only:

```csharp
if (_inventoryOpen && _lootContainer is null && _tradePartner is null && _barterNpc is null)
    DrawInventorySummary(); // the dude's own plain INV screen only, matching DrawEquipSlots' gate
```

(`Panels.cs:1849-1850`.) So moving the weight draw unconditionally into
`DrawInventorySummary()` would silently remove it from loot/trade sessions, which is
out of scope for this fix (untested against vanilla's own loot/trade weight-display
behavior, and not part of today's finding).

**Fix**: keep `DrawWeightReadout`'s existing behavior for loot/trade completely
untouched by skipping *only* the old call site's draw in the specific case where
`DrawInventorySummary()` will handle it instead — reusing that exact same gate
condition so the two can never both fire or both skip:

```csharp
if (ReferenceEquals(panel.Items, _dudeInventory)
    && !(_inventoryOpen && _lootContainer is null && _tradePartner is null && _barterNpc is null))
    DrawWeightReadout(panel.X, bottom);
```

Then draw the weight line as the final row inside `DrawInventorySummary()`, after the
two hand-weapon blocks, using that method's own `x`/`lineY` accumulator (matching
vanilla's row position exactly). `DrawWeightReadout`'s existing green/red
encumbrance-color logic is correct and already matches vanilla
(`_colorTable[992]`/`_colorTable[31744]`) — extract it into a small shared helper so
both call sites use identical logic rather than duplicating it:

```csharp
/// <summary>The carried-weight color: green within capacity, red when encumbered
/// (critterIsEncumbered) — matches vanilla's _colorTable[992]/_colorTable[31744].</summary>
private static Color WeightReadoutColor(int carried, int cap) =>
    Formats.Map.InventoryWeight.IsEncumbered(carried, cap)
        ? new Color(255, 64, 64) : new Color(0, 252, 0);
```

`DrawWeightReadout` (`Panels.cs:2334-2342`) becomes:

```csharp
private void DrawWeightReadout(int panelX, int panelBottom)
{
    if (_fontRenderer is null || _dude is null)
        return;
    int carried = DudeCarriedWeight(), cap = DudeCarryCapacity();
    _fontRenderer.Draw(_spriteBatch, $"Total Wt: {carried}/{cap}",
        new Vector2(panelX + 10, panelBottom + 4), WeightReadoutColor(carried, cap));
}
```

And `DrawInventorySummary()` gets one more block appended after the hand-weapon loop
(before its closing `_spriteBatch.End()`):

```csharp
int carried = DudeCarriedWeight(), cap = DudeCarryCapacity();
_fontRenderer.Draw(_spriteBatch, $"Total Wt: {carried}/{cap}",
    new Vector2(x, lineY), WeightReadoutColor(carried, cap));
```

## Scope

All three fixes are in `src/Hexwaste.Viewer/ViewerGame.Panels.cs`, in the same small
cluster of code (`DrawInventorySummary`, `InvBoxOrigin`, `DrawWeightReadout`, and the
item-panel loop's call site) — one cohesive change, not three separate ones, since bug
2 is what causes bug 3's visible symptom and both are naturally fixed together.

## Non-goals

- No change to loot/trade/barter weight-readout behavior — completely untouched,
  guarded by reusing `DrawInventorySummary()`'s own existing gate condition.
- No change to any other panel's color, layout, or positioning.
- No change to the item-list panel, equip slots, paperdoll rendering, or drag-and-drop
  logic — only the summary block's text color, the box's vertical origin, and the
  weight-readout's position/ownership change.
- Does not address the separate, still-open "Laser"/"Loser" residual glyph-rendering
  issue from the AAF font premultiplied-alpha fix (tracked separately in
  `.superpowers/sdd/progress.md`) — that's an unrelated rendering-pipeline bug, not a
  color/layout/text-content issue.

## Testing

No automated test project covers `ViewerGame` (MonoGame dependency, same situation as
every fix this session) — verify manually via the deterministic `--show-inventory` CLI
probe (`Program.cs:795-796`), which reaches the plain INV screen without menu-click
flakiness:

```bash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --show-inventory --screenshot /tmp/check.png
```

1. Confirm the whole SPECIAL/HP/AC/resistance panel renders in green, not white.
2. Confirm the inventory box no longer visually overlaps the HUD interface bar — the
   box's bottom edge should sit clearly above the bar's top edge.
3. Confirm "Total Wt: carried/cap" appears as the last line inside the SPECIAL/
   resistance summary panel (right side), not below the item list (left side), and
   that its color is green when not encumbered.
4. If reachable without excessive setup, open a loot/trade/barter session and confirm
   the item-list-anchored weight readout there is completely unaffected (still
   present, same position as before this fix) — this is the regression check for the
   Bug 3 gate condition.
