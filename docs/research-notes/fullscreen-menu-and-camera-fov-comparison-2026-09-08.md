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

**Root-caused later the same session** (two grounding passes against
`reference/fallout2-ce/src`, pinned base tree): fo2ce's `src/` code never reads
`fallout2.cfg`'s `[screen] resolution_x`/`resolution_y` at all. Its actual game
resolution — world AND UI, one shared 8-bit surface — comes from a fixed low value:
1024x768 by default in this repo's shipped `f2_res.ini` (`[MAIN] SCR_WIDTH`/
`SCR_HEIGHT`, `reference/fallout2-ce/run/__support/app/f2_res.ini:28-29`), or 640x480
truly vanilla. That fixed surface is uploaded to a texture and presented via
`SDL_RenderSetLogicalSize` + `SDL_RenderCopy(renderer, texture, nullptr, nullptr)`
(`src/svga.cc:364,410-416`) every frame — architecturally a **uniform**, aspect-ratio
-preserving scale (confirmed against the vendored SDL2 source fo2ce actually builds
against: `UpdateLogicalSize()` always computes one `scale` factor for both axes and,
with no `RENDER_LOGICAL_SIZE_MODE` hint set anywhere in fo2ce's own code, always takes
the letterbox path). SDL2 is architecturally incapable of the non-uniform per-axis
stretch this note originally assumed.

So this session's "fills 1920x1080 edge-to-edge, no black bars" fo2ce screenshots were
never fo2ce's own rendering logic doing that — `_GNW95_init_window()` requests
`SDL_WINDOW_FULLSCREEN` (exclusive/legacy fullscreen, `src/svga.cc:181`) at the game's
own small logical size (1024x768), and on this session's Linux/KWin test machine the
*compositor* silently scanned-out-scaled that small framebuffer to fill the real
1920x1080 panel — a step that happens entirely after `SDL_RenderPresent`, outside
fo2ce's code, and would behave differently on a different OS/compositor/monitor. The
2.3x "zoom" gap this note originally measured is consistent with a 1024/1920 ≈ 1.9x or
640/1920 = 3.0x upscale of that small frame, not a deliberate fo2ce camera FOV.

**Decision, once this was understood**: keep Hexwaste's current behavior unchanged —
Hexwaste renders directly at the window's true resolution (crisp, more world visible,
no upscale blur), which is arguably a straightforward improvement over fo2ce's
DOS-era-heritage fixed-low-resolution-then-blur limitation, not a bug to fix. No code
change to the camera. `Hexwaste.Formats.Rendering.UiScale.cs`'s doc comment — which had
claimed fo2ce does a non-uniform per-axis stretch, the mistaken premise this whole
investigation started from — was corrected to record the grounded finding, since
Hexwaste's own no-letterbox design remains a deliberate choice independent of what
fo2ce actually does (not a replication of it, as the old comment implied).

## Conclusion

Both of today's fixes (fullscreen-by-default, menu-backdrop-fill) hold up against a
real side-by-side run of both engines at native fullscreen resolution — menus and
character selection now visually match fo2ce's own presentation closely. The apparent
gameplay-camera "zoom" gap turned out not to be a Hexwaste bug at all: fo2ce's real
in-application behavior is a small, fixed-resolution, letterboxed image, and the
full-bleed fo2ce screenshots this session captured were an OS-compositor artifact of
this specific test machine, not reproducible or faithful application behavior worth
matching. Closed with no further change, after correcting the mistaken "fo2ce
non-uniform stretch" premise that had been recorded in `UiScale.cs` since earlier
UI Scale work.
