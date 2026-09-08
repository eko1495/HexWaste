# Inventory Summary Panel Fidelity Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix three related bugs in Hexwaste's inventory summary panel (`DrawInventorySummary()`): wrong text color, a layout overlap with the HUD bar, and a misplaced weight readout — bringing it in line with vanilla fo2ce's `inventoryRenderSummary()`.

**Architecture:** All three fixes land in `src/Hexwaste.Viewer/ViewerGame.Panels.cs`, in the same small cluster of code. The color fix changes one variable. The layout fix changes `InvBoxOrigin()`'s Y computation, the single source of truth every dependent panel element reads. The weight-readout fix extracts a shared color helper and moves the readout's draw call from the item-list panel into the summary panel, gated so loot/trade behavior is untouched.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`src/Hexwaste.Viewer`). No new dependencies.

## Global Constraints

- Green color must be `Color(0, 248, 0)`, matching vanilla's `_colorTable[992]` and the existing correct usage at `Panels.cs:1052` (`docs/superpowers/specs/2026-09-08-inventory-summary-panel-fidelity-design.md`, Bug 1).
- `InvBoxOrigin()`'s Y computation must center within `VirtualViewport().Height - InterfaceBar.Height`, not the full viewport height (spec, Bug 2).
- The weight-readout gate change must only affect the plain INV screen; loot/trade/barter behavior must be completely unaffected (spec, Bug 3).
- `ViewerGame` has no unit test project (MonoGame dependency) — verify via build + manual screenshots using the deterministic `--show-inventory` CLI probe, not automated tests.

---

## File Structure

- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs` — `DrawInventorySummary()`'s color, `InvBoxOrigin()`'s Y computation, a new `WeightReadoutColor()` helper, `DrawWeightReadout()`'s body, its call site's gate condition, and a new weight-readout block appended to `DrawInventorySummary()`.

Single cohesive change (all three bugs, one file, one tightly-coupled code cluster) — one task.

---

### Task 1: Fix the inventory summary panel's color, layout, and weight-readout position

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs`

**Interfaces:**
- Produces: `ViewerGame.WeightReadoutColor(int carried, int cap) -> Color` — private static, called from both `DrawWeightReadout()` and the new block inside `DrawInventorySummary()`.
- Consumes (existing, unchanged): `ViewerGame.DudeCarriedWeight() -> int`, `ViewerGame.DudeCarryCapacity() -> int` (both already used by `DrawWeightReadout`), `Formats.Map.InventoryWeight.IsEncumbered(int, int) -> bool`, `InterfaceBar.Height` (constant, `InterfaceBar.cs:24`).

- [ ] **Step 1: Fix the text color (Bug 1)**

Find, in `src/Hexwaste.Viewer/ViewerGame.Panels.cs`, inside `DrawInventorySummary()`:

```csharp
        var pale = new Color(252, 252, 252); // the INVBOX readout's pale text (_colorTable[992])
```

(currently `Panels.cs:1874`). Replace it with:

```csharp
        var pale = new Color(0, 248, 0); // the INVBOX readout's green text (_colorTable[992])
```

- [ ] **Step 2: Fix the layout overlap (Bug 2)**

Find:

```csharp
    private Point? InvBoxOrigin() => _invBox is null
        ? null
        : new Point(Math.Max(0, (VirtualViewport().Width - InvBoxW) / 2),
                    Math.Max(0, (VirtualViewport().Height - InvBoxH) / 2));
```

(currently `Panels.cs:1976-1979`). Replace it with:

```csharp
    private Point? InvBoxOrigin() => _invBox is null
        ? null
        : new Point(Math.Max(0, (VirtualViewport().Width - InvBoxW) / 2),
                    Math.Max(0, (VirtualViewport().Height - InterfaceBar.Height - InvBoxH) / 2));
```

- [ ] **Step 3: Build to confirm no compile errors**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors (pre-existing nullable warnings, if any, are unrelated and fine).

- [ ] **Step 4: Extract the shared weight-readout color helper**

Find, in `src/Hexwaste.Viewer/ViewerGame.Panels.cs`:

```csharp
    /// <summary>The carried-weight readout, drawn just below the dude's inventory panel (P24;
    /// inventory.cc:3164 "Total Wt: N/M") — green within capacity, red when over
    /// (critterIsEncumbered). Below the panel so it never collides with the title/rows.</summary>
    private void DrawWeightReadout(int panelX, int panelBottom)
    {
        if (_fontRenderer is null || _dude is null)
            return;
        int carried = DudeCarriedWeight(), cap = DudeCarryCapacity();
        Color color = Formats.Map.InventoryWeight.IsEncumbered(carried, cap)
            ? new Color(255, 64, 64) : new Color(0, 252, 0);
        _fontRenderer.Draw(_spriteBatch, $"Total Wt: {carried}/{cap}", new Vector2(panelX + 10, panelBottom + 4), color);
    }
```

(currently `Panels.cs:2331-2342`, including its doc comment). Replace it with:

```csharp
    /// <summary>The carried-weight color: green within capacity, red when encumbered
    /// (critterIsEncumbered) — matches vanilla's _colorTable[992]/_colorTable[31744].</summary>
    private static Color WeightReadoutColor(int carried, int cap) =>
        Formats.Map.InventoryWeight.IsEncumbered(carried, cap)
            ? new Color(255, 64, 64) : new Color(0, 252, 0);

    /// <summary>The carried-weight readout, drawn just below the dude's inventory panel during
    /// loot/trade/barter (P24; inventory.cc:3164 "Total Wt: N/M"). On the plain INV screen this
    /// is drawn instead as the final row of DrawInventorySummary() itself (matching vanilla's own
    /// position, inventory.cc:3160-3178) — see that method's gate condition and this one's call
    /// site in DrawItemPanels().</summary>
    private void DrawWeightReadout(int panelX, int panelBottom)
    {
        if (_fontRenderer is null || _dude is null)
            return;
        int carried = DudeCarriedWeight(), cap = DudeCarryCapacity();
        _fontRenderer.Draw(_spriteBatch, $"Total Wt: {carried}/{cap}",
            new Vector2(panelX + 10, panelBottom + 4), WeightReadoutColor(carried, cap));
    }
```

- [ ] **Step 5: Gate the old call site so it skips the plain-INV case (Bug 3, part 1)**

Find:

```csharp
                int bottom = DrawItemList(panel.Title, panel.Items, panel.X, panel.Price);
                if (ReferenceEquals(panel.Items, _dudeInventory)) // the dude's side carries the weight readout (P24)
                    DrawWeightReadout(panel.X, bottom);
```

(currently `Panels.cs:1840-1842`, inside `DrawItemPanels()`'s loop over `CurrentItemPanels()`). Replace it with:

```csharp
                int bottom = DrawItemList(panel.Title, panel.Items, panel.X, panel.Price);
                // The plain INV screen draws its own weight readout inside DrawInventorySummary()
                // instead (matching vanilla's row position) -- this call only fires during
                // loot/trade/barter, where DrawInventorySummary() never runs.
                if (ReferenceEquals(panel.Items, _dudeInventory)
                    && !(_inventoryOpen && _lootContainer is null && _tradePartner is null && _barterNpc is null))
                    DrawWeightReadout(panel.X, bottom);
```

- [ ] **Step 6: Add the weight-readout row inside `DrawInventorySummary()` (Bug 3, part 2)**

Find the end of `DrawInventorySummary()`'s hand-weapon loop:

```csharp
                lineY += lh;
                if (SafeProto(hand.Pid)?.Weapon is { } weapon)
                    _fontRenderer.Draw(_spriteBatch, $"Dmg: {weapon.MinDamage}-{weapon.MaxDamage}", new Vector2(x, lineY), pale);
            }
            lineY += lh * 3;
        }

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

(currently `Panels.cs:1926-1934`). Replace it with:

```csharp
                lineY += lh;
                if (SafeProto(hand.Pid)?.Weapon is { } weapon)
                    _fontRenderer.Draw(_spriteBatch, $"Dmg: {weapon.MinDamage}-{weapon.MaxDamage}", new Vector2(x, lineY), pale);
            }
            lineY += lh * 3;
        }

        int carried = DudeCarriedWeight(), cap = DudeCarryCapacity();
        _fontRenderer.Draw(_spriteBatch, $"Total Wt: {carried}/{cap}",
            new Vector2(x, lineY), WeightReadoutColor(carried, cap));

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 7: Build to confirm no compile errors**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 8: Manual verification**

Use the deterministic `--show-inventory` CLI probe (no menu-click navigation needed):

```bash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --show-inventory --screenshot /tmp/inv-fix-check.png
```

Read `/tmp/inv-fix-check.png` with the Read tool. Expected:

1. The whole SPECIAL/HP/AC/resistance panel renders in green, not white.
2. The inventory box's bottom edge sits clearly above the HUD interface bar's top
   edge — no visual overlap.
3. "Total Wt: carried/cap" appears as the last line inside the SPECIAL/resistance
   summary panel (right side, same column as "Hit Points"/"Armor Class"/the
   resistance rows), not below the item list (left side). Its color should be green
   (a fresh character is not encumbered).

If any of these don't look right, crop and upscale the relevant region for a closer
look before concluding, e.g.:

```bash
convert /tmp/inv-fix-check.png -crop 300x400+700+50 -resize 900x1200 /tmp/inv-fix-zoom.png
```

(Adjust the crop region based on what the full screenshot actually shows.)

- [ ] **Step 9: Regression check — loot/trade weight readout unaffected**

Check whether a CLI probe exists for a loot or barter screen:

```bash
grep -n '"--show-loot\|"--open-barter\|"--loot' src/Hexwaste.Viewer/Program.cs
```

If a suitable probe exists, use it to screenshot a loot/trade session and confirm the
item-list-anchored weight readout is still present, in its original position (bottom
of the dude's item-list column), unaffected by this change. If no such probe exists or
reaching that state requires excessive setup, it's acceptable to skip this live check —
the gate condition added in Step 5 is a pure boolean-logic change that can be verified
by inspection: the new condition is `ReferenceEquals(...) && !(the exact same boolean
expression DrawInventorySummary() uses to gate itself)`, so the two draw paths are
provably mutually exclusive and every state that isn't the plain INV screen still
reaches the original `DrawWeightReadout` call exactly as before.

- [ ] **Step 10: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Panels.cs
git commit -m "$(cat <<'EOF'
fix(viewer): inventory summary panel color, layout, and weight readout

Three related bugs found via a real-game comparison against fo2ce:
the SPECIAL/HP/AC/resistance panel rendered in pale white instead of
vanilla's green (_colorTable[992]); InvBoxOrigin() centered the box
within the full viewport instead of the space above the HUD bar,
causing a ~47px overlap; and the weight readout was drawn in the
item-list panel instead of as the summary panel's own final row
(matching inventory.cc:3160-3178), which combined with the overlap
made it visually spill past the frame. Fixes land together since the
layout bug is what caused the weight-readout's visible symptom.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

## Self-Review

**Spec coverage:**
- Bug 1 (color) → Step 1. ✅
- Bug 2 (layout) → Step 2. ✅
- Bug 3 (weight readout, both the shared helper extraction and the two call sites) →
  Steps 4-6. ✅
- Non-goals (loot/trade untouched, no other panel touched) → Step 5's gate condition
  is the exact mechanism that guarantees this; Step 9 verifies/documents it. ✅
- Testing section's manual verification → Step 8; regression check → Step 9. ✅

**Placeholder scan:** No TBD/TODO. Step 9 has an explicit, justified fallback ("if no
such probe exists... it's acceptable to skip this live check") backed by a concrete
logical argument (the gate conditions are provably mutually exclusive), not a vague
placeholder.

**Type consistency:** `WeightReadoutColor(int carried, int cap) -> Color` is declared
in Step 4 and consumed identically in Step 4's own `DrawWeightReadout` body and Step
6's new block in `DrawInventorySummary()` — same parameter order, same return type,
same call shape (`WeightReadoutColor(carried, cap)`) at both call sites.
