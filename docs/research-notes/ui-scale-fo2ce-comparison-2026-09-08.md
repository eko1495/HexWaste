# fo2ce ↔ Hexwaste comparison — post UI Scale (2026-09-08)

Piloted per `docs/fo2ce-comparison-playbook.md`. Screenshots live in the (gitignored) run dir
`scratch/compare-runs/ui-scale-post-20260908-000340/` — this note is the durable summary.

fo2ce: 1920x1080 fullscreen (community continuous build). Hexwaste: 1280x720 windowed.
Both are non-4:3 — the exact case the whole UI Scale project targeted.

## 01 — main menu
fo2ce: button rail top-left, power-armor helmet art filling the right/most of the frame,
buttons legible at native scale (this screen was always full-window in fo2ce, nothing to
compare against a "before" state). Hexwaste: same composition (button rail left, helmet art
right) scaled ~2x to fill 1280x720 — matches fo2ce's proportions well. Confirms Stage 2's
main-menu-family scaling still holds.

## 02 — gameplay HUD (Temple of Trials entrance)
Both engines: same camera framing of the temple entrance, dude standing on the steps. HUD bar
spans the full window width in both, INV/Skilldex buttons at the same relative corners, item
slots/AC/HP readout centered. fo2ce's readout differs only in game-state text (real inventory
vs Hexwaste's fresh --create dude) and camera zoom level (fo2ce's default zoom shows the dude
slightly larger relative to the temple) — expected, not a scaling defect. The HP/AP/Level/XP
text line above the bar (Stage 6 Task 2's target) sits flush above the bar in both.

## 03 — inventory
Both: paperdoll top-center, ARMOR/ITEM slots below-left, stat/resistance readout top-right,
DONE button bottom-right. Window fill and internal proportions match. Confirms Inventory
Piece 1's INVBOX scaling.

## 04 — message box (fo2ce only, no Hexwaste equivalent captured)
fo2ce: Narg (premade tribal) has no Pip-Boy yet, correctly triggering "You aren't wearing the
pipboy!" — confirmed vanilla behavior, not a bug. No direct Hexwaste checkpoint taken for this
(no CLI flag readily maps to "no pipboy" state); the encounter-prompt-style message box was
already covered by Stage 6's own manual verification in-session.

## 05 — Skilldex
Both: right-anchored panel, 8 skill rows + CANCEL/close button, scaled to the window height.
Layout and relative proportions match. Confirms Stage 3a's Skilldex scaling.

## Conclusion

Across all 4 directly comparable checkpoints (main menu, gameplay HUD, inventory, Skilldex),
Hexwaste's post-UI-Scale layout proportions and window-fill behavior track fo2ce's own
fullscreen-stretch presentation at a different non-4:3 resolution (1280x720 vs 1920x1080),
confirming the project's core goal — filling non-4:3 windows uniformly, matching fo2ce's
presentation model without its axis-distorting stretch — holds up against the real reference
engine, not just Hexwaste's own screenshots taken in isolation throughout this project.
