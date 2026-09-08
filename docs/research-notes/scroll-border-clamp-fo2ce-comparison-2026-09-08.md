# fo2ce ↔ Hexwaste comparison — scroll border clamp (2026-09-08)

Piloted per `docs/fo2ce-comparison-playbook.md`. Screenshots live in the (gitignored)
run dir `scratch/compare-runs/scroll-clamp-verify-20260908-152639/` — this note is the
durable summary. Verifies the fix landed in commit `615e6bd` (spec
`docs/superpowers/specs/2026-09-08-camera-scroll-border-clamp-design.md`).

## Setup

fo2ce reference screenshot reused from the same-day `temple-scenery-20260908-144951` run
(the vanilla clamp itself doesn't change — no need to re-record it): camera held Left for
2s from the Temple of Trials entrance, producing a pixel-identical shot to the un-panned
one, i.e. proof fo2ce's own camera was already sitting at its scroll limit.

Hexwaste this session: played **through actual gameplay**, not a CLI probe — launched
windowed, clicked NEW GAME → TAKE (Norg, the premade tribal) at the character-selection
screen, landed at the Temple of Trials entrance exactly like a real player would, then
drove the camera with the arrow keys (`xdotool keydown/keyup`), the same input path a
real player uses.

## 01 — camera held at the scroll limit

fo2ce (`fo2ce/clamped-at-edge-reference.png`): full temple facade in view, dude on the
entrance steps, cauldrons flanking the stairs, dry brush at the frame edges — the camera
has already hit its border and stopped; no undefined tiles anywhere in frame.

Hexwaste (`hexwaste/clamped-at-edge.png`): after holding Left for 30 seconds total from
the temple entrance (far longer than needed to reach the border — done deliberately to
stress-test the clamp), the camera settled on open desert terrain, well short of the map's
undefined region. No black-void checkerboard tiles anywhere in frame — the fix's border
margin is conservative by construction (it's a fixed geometric margin from the 200x200 hex
grid's edge, not aware of which tiles are actually defined), so it clamps the camera on
always-defined terrain before it can ever near the undefined area. Confirmed the position
was genuinely settled, not still creeping: a second 15-second hold produced a screenshot
whose only pixel differences from the first were the blinking HP-bar LED and cursor
sprite — the terrain itself was static.

This is the core fix at work: before this session's change, `ViewerGame.cs`'s only clamp
was "is the screen-center tile off the raw 200x200 grid at all" — coarse enough that
panning could reach the map's genuinely-undefined tiles and expose the void. The new
`Camera.IsWithinScrollBorder` check (ported from fo2ce's `tileSetBorder`/`tileSetCenter`
border math) stops the camera well inside safe territory instead.

## 02 — normal panning still works after the clamp engages

`hexwaste/panned-back.png`: holding Right for 3 seconds from the clamped position visibly
moved the camera back toward the map interior (a different mottled dirt pattern than the
clamped shot) — confirming the clamp is directional and local to the border, not a global
freeze. This matches fo2ce's own behavior, where the border rejection is per-candidate-tile
(`tileSetCenter` returns `-1` only for tiles beyond the margin) rather than a general input
lock.

## Conclusion

The scroll border clamp lands as designed: Hexwaste's camera, driven through the same
arrow-key input path a real player uses, now stops at a conservative distance from the
map's undefined region — consistent with, and in practice more conservative than, fo2ce's
own vanilla border behavior — while ordinary panning elsewhere on the map is unaffected.

## Note on this session's fo2ce repro attempt

A fresh attempt to drive fo2ce all the way through its own New Game → character-take flow
(to record a brand new matched "held Left" shot alongside the fresh Hexwaste run) got stuck
at the main menu: `NEW GAME` clicks were not registering even after relocating the in-game
cursor with small (`≤150px`) relative moves per the playbook's own guidance, and the
session's built-in main-menu idle timeout (`"Main menu timed-out"` in fo2ce's own log)
fired at least once mid-attempt, looping back into the intro movies. Rather than keep
burning time on a menu-input flake unrelated to the fix under test, this note reuses the
same-day `temple-scenery-20260908-144951` fo2ce reference (the vanilla clamp itself is
static, not something this fix changes) and focuses the fresh piloting effort on Hexwaste,
which is what the fix actually touches.
