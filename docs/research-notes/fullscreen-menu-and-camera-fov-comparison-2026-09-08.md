# fo2ce ↔ Hexwaste real comparison — post fullscreen/menu-backdrop fixes (2026-09-08)

Piloted per `docs/fo2ce-comparison-playbook.md`, both engines fullscreen at native
1920x1080. Screenshots in the (gitignored) run dir
`scratch/compare-runs/real-comparison-20260908-194342/` — this note is the durable
summary. Verifies the day's two shipped fixes (`904be23`/fullscreen-by-default,
`60e3124`/menu-backdrop-fill) and surfaces one new, real, unfixed finding.

## Setup

fo2ce: `scripts/fo2ce-control.sh launch`, skipped intro movies (`key Return` per
movie), `key n` (NEW GAME) → `key t` (TAKE the premade) → `key Return` (skip
character-intro cutscene) — the documented keyboard-mnemonic path, no mouse-click
flakiness this time.

Hexwaste: plain `dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data`
(no flags — the new fullscreen-by-default path), mouse-clicked NEW GAME → TAKE (main
menu hotkeys are still unwired, confirmed again today — see the menu-backdrop-fill
session's finding).

## 01 — main menu: both fixes confirmed working

Both engines now fill the full 1920x1080 screen edge-to-edge with no window chrome and
no black bars. Side-by-side the composition is close to identical: button rail
top-left, power-armor helmet art filling the right two-thirds, copyright/version text
at the bottom corners. This confirms both of today's fixes
(`fo2ce/01-title.png` vs `hexwaste/01-title.png` in the run dir):

- **Fullscreen-by-default** — Hexwaste's plain launch now genuinely fullscreens at
  desktop resolution, matching fo2ce's own default presentation.
- **Menu-backdrop-fill** — the black pillarbox bars that used to flank Hexwaste's menu
  art at this resolution are gone; the backdrop now stretches exactly like fo2ce's own
  buffer-stretch does for this same art.

## 02 — character selection: also fills correctly

Both show the same character-selection frame (portrait, SPECIAL stats, tagged skills,
bio text, TAKE/MODIFY/CREATE CHARACTER/BACK) filling the screen with no gaps
(`fo2ce/02-...`/`hexwaste/02-charselect.png`). Confirms the menu-backdrop-fill work
covers this screen correctly too, matching the per-task review's own live verification
from earlier today.

## 03 — Temple of Trials entrance, in gameplay: a real, unfixed camera FOV difference

This is the substantive new finding. At the *identical* 1920x1080 fullscreen
resolution, right after spawning at the Temple of Trials entrance:

- **fo2ce** (`fo2ce/02-temple-entrance-gameplay.png`): the temple facade fills nearly
  the entire screen — the twin beehive towers span roughly x=100 to x=1750 (~1650px
  of the 1920px width).
- **Hexwaste** (`hexwaste/03-temple-entrance-gameplay.png`): the same temple is a
  small, distant structure occupying only the upper-center of the screen — the
  towers span roughly x=580 to x=1300 (~720px) — surrounded by a large expanse of
  empty desert that fo2ce never shows at this camera position.

That's roughly a **2.3x difference in effective zoom** between the two engines at the
exact same output resolution, right after character creation, with no camera input
from either engine's default state. Cross-checked against an earlier same-day Hexwaste
screenshot at 1280x720 windowed
(`scratch/compare-runs/temple-scenery-20260908-144951/hexwaste/entrance.png`): the
temple occupies a visually similar *absolute* pixel footprint there too — i.e.
Hexwaste's world camera appears to hold a roughly constant pixels-per-hex-tile ratio
across window sizes (showing proportionally *more* world at a larger window, same
per-tile size), while fo2ce's camera at 1920x1080 keeps the temple large regardless of
window size, implying a different scaling relationship between window resolution and
world-camera zoom in the two engines.

**This was not investigated further or fixed this session** — root-causing it would
mean comparing `tile.cc`'s `gTileWindowWidth`/`gTileWindowHeight` handling (which reads
the *configured* resolution, per this session's earlier scroll-border-clamp research)
against `Camera.cs`'s equivalent `_windowWidth`/`_windowHeight` usage, to find where
the two diverge. Flagging it here as a genuine, verified, reproducible difference
worth a dedicated investigation — separate from today's two shipped fixes, both of
which are confirmed working correctly.

## Conclusion

Both of today's fixes (fullscreen-by-default, menu-backdrop-fill) hold up against a
real side-by-side run of both engines at native fullscreen resolution — menus and
character selection now visually match fo2ce's own presentation closely. Gameplay
camera zoom does not match, however: Hexwaste shows substantially more of the game
world than fo2ce does at the same resolution and camera position, a real discrepancy
uncovered by this comparison and left open for a future session.
