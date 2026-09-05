# fo2ce ↔ Hexwaste comparison playbook

Read this in full before piloting either engine. It exists because a pilot subagent starts
with zero context — this doc is that context. Full design rationale:
`docs/superpowers/specs/2026-09-05-fo2ce-comparison-automation-design.md`.

## What you're doing

You've been given a scenario brief (a natural-language description of what to do and which
moments are "checkpoints") and told to pilot ONE of the two engines through it, saving a
screenshot + a one-line note at each checkpoint. You are not comparing anything yourself —
that happens later, back in the main conversation, once both pilots (fo2ce and Hexwaste) have
reported their artifacts.

Your run directory was created for you by `scripts/compare-run-init.sh <slug>` (prints
`scratch/compare-runs/<slug>-<timestamp>/`). Write your checkpoints and notes under your
engine's subdirectory of that path: `<run-dir>/fo2ce/` or `<run-dir>/hexwaste/`.

## If you are piloting fo2ce

fo2ce has no semantic API. You play it like a human tester would: look at a screenshot,
decide what to press or click, act, look again.

Commands (all via `scripts/fo2ce-control.sh`, run from the repo root):
- `scripts/fo2ce-control.sh launch` — starts fo2ce, prints its PID. Run this first. If it
  errors "already running", something from a previous run is still up — run `kill` first.
- `scripts/fo2ce-control.sh shot <path>.png` — screenshots the whole screen (the game fills
  it at 640x480). Use this for every checkpoint.
- `scripts/fo2ce-control.sh key "<xdotool key spec>"` — sends a key, e.g. `key Return`,
  `key Down`, or a sequence like `key "Down Down Return"`.
- `scripts/fo2ce-control.sh click <x> <y>` — clicks inside the fo2ce window at window-relative
  pixel (x, y). The window is 640x480; read your last screenshot to figure out where to click.
- `scripts/fo2ce-control.sh kill` — stop fo2ce. **Always run this when your scenario is done**,
  even if something went wrong — a stray process will block the next pilot's `launch`.

Workflow:
1. `scripts/fo2ce-control.sh launch`, wait a couple seconds for the intro/menu.
2. Loop: `shot` → look at the image (use the Read tool) → decide the next `key`/`click` →
   act → repeat, until you reach a checkpoint named in your brief.
3. At each checkpoint: `shot <run-dir>/fo2ce/NN-<short-label>.png` (zero-padded, in order),
   then append one line to `<run-dir>/fo2ce/notes.md`: `NN | <short-label> | <what happened>`.
4. When the scenario brief is complete (or you get stuck), `scripts/fo2ce-control.sh kill`.
5. Report back: the run directory path, the list of checkpoint files you produced, and a
   short summary — not the images themselves.

Gotchas:
- The intro plays two movies (`iplogo.mve`, `intro.mve`) before the main menu — expect to
  wait and/or press a key to skip them if your brief starts past the intro.
- This binary is the `fallout2-ce/fallout2-ce` **community** continuous build, not strictly
  vanilla — it may carry `// CE:` quality-of-life changes. If something looks different from
  what you'd expect of vanilla Fallout 2, note it, but don't assume it's a Hexwaste bug — that
  judgment happens later, against `reference/fallout2-ce/src/*.cc`.
- `fo2ce-control.sh` already handles two fragile bits of this machine's setup: it launches
  with `SDL_VIDEODRIVER=x11` (without it, fo2ce's window is invisible to input/screenshot
  tools) and screenshots via `spectacle` (ffmpeg/ImageMagick's `import` silently return black
  frames here). If either symptom shows up, that's a sign something else broke — not a reason
  to change the launch/screenshot method.

## If you are piloting Hexwaste

Hexwaste's CLI is already semantic — you are not visually guessing. Each checkpoint is one
full process invocation via `scripts/hexwaste-checkpoint.sh`, replaying every action from the
start of the scenario up to that checkpoint (there is no "keep the same process running and
screenshot again" — the harness takes exactly one `--screenshot` per run).

Command: `scripts/hexwaste-checkpoint.sh <output.png> [-- <action flags...>]`

Available action flags (see `src/Hexwaste.Viewer/Program.cs` for the full authoritative list —
this is not exhaustive): `--goto <tile>`, `--walk`, `--talk <x,y>`, `--choose <n>`,
`--menu-click <panel> <n>`, `--attack <x,y>`, `--door <x,y>`, `--create <stats string>` (see
existing `scripts/*-golden.sh` for real examples of these flags in use).

Workflow:
1. Translate your scenario brief's checkpoints into a growing list of action flags — e.g. if
   checkpoint 2 is "after exiting the vault", figure out (by reading map data, existing probes,
   or the Formats library) the `--goto`/`--walk` flags that get the dude there.
2. For checkpoint N, run `scripts/hexwaste-checkpoint.sh <run-dir>/hexwaste/NN-<label>.png --
   <all action flags for checkpoints 1..N, in order>`.
3. After each run, append one line to `<run-dir>/hexwaste/notes.md`:
   `NN | <short-label> | <exact action flags used>`.
4. Report back: the run directory path, the list of checkpoint files, and a short summary.

Gotchas:
- `--no-build` is passed by the wrapper — if you've changed engine code, `dotnet build
  src/Hexwaste.Viewer -c Debug` first or your changes won't be reflected.
- If an action flag needs a tile/hex coordinate you don't know yet, it's fine to run a
  throwaway checkpoint first just to look at the map and figure out where things are.

## Format recap

```
<run-dir>/
  fo2ce/
    01-<label>.png
    02-<label>.png
    notes.md        # one line per checkpoint: "NN | label | what happened"
  hexwaste/
    01-<label>.png
    02-<label>.png
    notes.md        # one line per checkpoint: "NN | label | exact flags used"
```

Checkpoint numbers and labels must match between `fo2ce/` and `hexwaste/` — the comparison
step pairs them by filename.
