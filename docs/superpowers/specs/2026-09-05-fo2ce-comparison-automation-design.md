# fo2ce ↔ Hexwaste comparison automation

## Problem

The user regularly notices glitches and behavioral differences between
Hexwaste and vanilla Fallout 2 while playing, but has no systematic way to
pin down *what* differs without manually running both games side by side.
[Prior session](2026-09-05) got a real `fallout2-ce` binary running on this
machine (`reference/fallout2-ce/run/`, community continuous build) as a
comparison baseline. This spec covers the actual comparison workflow built
on top of that baseline.

## Goal

Given a natural-language scenario brief, pilot both fo2ce and Hexwaste
through the same sequence of events, capture matching screenshots, and
produce a comparison report (meaningful differences + proposed fixes,
grounded in `reference/fallout2-ce/src/*.cc`) — on demand, when investigating
a suspected glitch. Not a CI regression suite; not fully unattended.

## Non-goals

- No pixel-diffing or image-hashing pipeline — comparison is done by reading
  screenshots directly (an LLM vision review), which tolerates harmless
  rendering noise (anti-aliasing, scaling, exact offsets) that a strict diff
  would flag constantly.
- No standalone API-calling pipeline (no Anthropic API key, no unattended
  report generation) — the comparison step happens inside this Claude Code
  conversation.
- No scheduled/CI-triggered runs. Purely ad hoc, run when needed.
- No literal, hand-written per-engine input scripts. Scenarios are briefs;
  each pilot improvises the concrete inputs.

## Architecture

### Scenario brief

A short natural-language description of what to do and what checkpoints
matter, e.g.:

> "Start a new game with default character creation. Walk out of Vault 13.
> Find the first NPC encountered and talk to them. Pick the first dialogue
> option. Checkpoints: after character creation, after exiting the vault,
> on first seeing the NPC, mid-dialogue."

The same brief drives both pilots. Checkpoints are narrative beats, not
fixed time intervals — the two engines have very different pacing (fo2ce
runs in real time under human-speed input; Hexwaste's harness applies
actions instantly), so lining up by wall-clock time would misalign the
comparison.

### fo2ce pilot (fresh subagent)

fo2ce has no semantic API — it's a normal interactive SDL game. Its pilot
subagent must play it the way a human tester would:

- Launch: `cd reference/fallout2-ce/run && SDL_VIDEODRIVER=x11 DISPLAY=:0 ./fallout2-ce`
  (backgrounded; `SDL_VIDEODRIVER=x11` is required — without it the window
  is a native Wayland surface invisible to input/screenshot tooling on this
  KDE/KWin session).
- Observe: `spectacle -b -n -f -o <path>.png` (verified working; `ffmpeg
  -f x11grab` and ImageMagick `import` both return solid black on this
  compositor even against the XWayland-backed window — do not use them).
- Act: `xdotool key`/`xdotool click`/`xdotool mousemove`, using
  `xdotool search --name "FALLOUT II" getwindowgeometry` to locate the
  window before computing click coordinates.
- Loop: screenshot → look at it → decide next input → act → repeat, using
  its own reading of the screen to navigate menus, walk, and talk — no
  pre-scripted coordinates, since those would be brittle and can't be
  authored reliably in advance.
- At each checkpoint named in the brief, save a numbered screenshot and a
  one-line note of what just happened.
- Kill the process when done (`pkill -f "reference/fallout2-ce/run/fallout2-ce"`
  or the specific PID).

### Hexwaste pilot (fresh subagent)

Hexwaste's CLI harness (`src/Hexwaste.Viewer/Program.cs`) is already
semantic: flags like `--goto`, `--walk`, `--talk`, `--choose`, `--menu-click`
queue a list of `StartupAction`s applied in order within a single process
run, ending in one `--screenshot` capture. There is no "screenshot mid-run,
then keep going" — so to get N checkpoints, the pilot subagent runs the
harness N times, each time replaying the action-list prefix up to that
checkpoint and capturing a screenshot at the end of that run.

This makes the Hexwaste side deterministic and cheap: the pilot's job is to
translate the scenario brief into the right sequence of flags (consulting
map/tile data or existing probes as needed to find coordinates equivalent
to what the fo2ce pilot did), not to visually improvise. No vision-guided
trial and error is needed here.

### Artifacts

Per run, under `scratch/compare-runs/<scenario-slug>-<timestamp>/`:

```
fo2ce/
  01-<label>.png
  02-<label>.png
  ...
  notes.md          # one line per checkpoint: what happened, any launch quirks
hexwaste/
  01-<label>.png
  02-<label>.png
  ...
  notes.md          # one line per checkpoint: exact CLI invocation used
```

`*.png` is already globally gitignored (`.gitignore:18`), so screenshots
never risk being committed. `scratch/` already exists as an informal
debug workspace in this repo.

Each subagent reports back a short summary and the artifact paths — not the
raw images — keeping the main conversation's context light.

### Comparison step (done by the main conversation, not a third subagent)

After both subagents finish, the main conversation (me) reads matching
checkpoint pairs side by side and:

- Describes meaningful discrepancies, explicitly filtering out harmless
  rendering noise (anti-aliasing, minor scaling/offset, font hinting).
- For anything that looks like a real Hexwaste bug, cross-checks against
  `reference/fallout2-ce/src/*.cc` and proposes a concrete fix, citing the
  source function per this repo's porting convention (`// ported from
  fallout2-ce src/x.cc f()`).
- Flags — but does not automatically dismiss or automatically accept — any
  difference that might instead be a `fallout2-ce/fallout2-ce` community
  fork QoL change (marked `// CE:` in that source) rather than a vanilla
  bug in Hexwaste, per CLAUDE.md's authority rules (`alexbatalov e97087b`
  is authoritative; `community/main` is a bug-fix-candidate source only).

This step is not delegated because proposing a grounded fix requires
reading Hexwaste's own source and `reference/fallout2-ce/src/*.cc` — work
that belongs in the same context as the codebase knowledge already built up
in this conversation, not a subagent starting fresh.

## Known constraints (carried over from getting fo2ce running)

- `fallout2-ce/fallout2-ce` community continuous build (rolling release tag
  `continious`), not `alexbatalov`'s stale `v1.3.0` release — see
  `reference/fallout2-ce/run/`. No sudo, no build required.
- `SDL_VIDEODRIVER=x11` is mandatory for an inputtable/screenshottable
  window on this machine's Wayland/KWin session.
- `spectacle -b -n -f -o <path>.png` is the only screenshot method verified
  to work here; `ffmpeg`/`import` silently produce black frames.
- The community build may carry non-vanilla QoL changes; any diff found
  against it should be sanity-checked against `alexbatalov`'s source before
  being treated as confirmed.

## Open items for the first real run

- No scenario has been piloted end-to-end yet with this workflow — the
  first invocation will validate the design in practice (whether the
  fo2ce pilot can reliably navigate by vision alone, whether Hexwaste
  checkpoint flags are easy to derive from a brief, whether checkpoint
  granularity needs adjusting).
- Character-creation flow and exact main-menu layout in the community
  build haven't been mapped by coordinates yet — the fo2ce pilot will need
  to discover them live from its own screenshots each run.
