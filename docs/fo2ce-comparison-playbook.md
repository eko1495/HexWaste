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

**The fo2ce pilot has exclusive use of the display while it runs.** `fo2ce-control.sh shot`
screenshots the whole screen, and the fo2ce window runs fullscreen — if a Hexwaste pilot opens
its own MonoGame window on the same display at the same time, it will pop over fo2ce mid-scenario
(corrupting its checkpoint screenshots) or steal `xdotool`'s input focus. Run the two pilots
**sequentially, one after the other — never concurrently.**

Your run directory was created for you by `scripts/compare-run-init.sh <slug>` (prints
`scratch/compare-runs/<slug>-<timestamp>/`). Write your checkpoints and notes under your
engine's subdirectory of that path: `<run-dir>/fo2ce/` or `<run-dir>/hexwaste/`.

## If you are piloting fo2ce

fo2ce has no semantic API. You play it like a human tester would: look at a screenshot,
decide what to press or click, act, look again.

Commands (all via `scripts/fo2ce-control.sh`, run from the repo root):
- `scripts/fo2ce-control.sh launch` — starts fo2ce, prints its PID. Run this first. If it
  errors "already running", something from a previous run is still up — run `kill` first.
- `scripts/fo2ce-control.sh shot <path>.png` — screenshots the whole screen. fo2ce runs
  **fullscreen at the desktop's actual resolution** (1920x1080 on this machine, as of this
  writing) — not 640x480 — because there's no `f2_res.ini` in `reference/fallout2-ce/run/`
  (only `EXAMPLE_fallout2.cfg`), so it falls back to the desktop resolution with its classic
  4:3 game content stretched to fill the screen. Confirm with `file <shot>.png` on your first
  checkpoint if running on a different machine. Use this for every checkpoint.
- `scripts/fo2ce-control.sh key "<xdotool key spec>"` — sends a key, e.g. `key Return`,
  `key Down`, or a sequence like `key "Down Down Return"`. **Arrow keys pan the camera, not the
  dude** (`src/game.cc`'s arrow-key handling calls `mapScroll()`) — movement in gameplay is
  mouse-only, click-to-walk.
- `scripts/fo2ce-control.sh move <dx> <dy>` then `scripts/fo2ce-control.sh click` — jogs the
  in-game cursor by a *relative* offset, then clicks at wherever it now is. fo2ce runs SDL in
  relative-mouse mode, so there is no way to warp the cursor to an absolute (x, y) and have the
  game see it — a combined absolute `mousemove ... click` silently no-ops in-game even though
  the OS-level cursor does move. Workflow: `shot`, look at the highlighted hex under the cursor
  (plain outline = walkable, red X = blocked), `move` by an estimated delta, `shot` again to
  confirm you're over the right tile, then `click`. Getting somewhere non-adjacent (e.g. through
  a doorway) often takes several small `move`+`shot` corrections rather than one big jump, and a
  destination close to a wall/threshold may need two shorter click-to-walk hops instead of one.
- **Right-click ("look"/examine)**: `fo2ce-control.sh` has no built-in right-click command —
  after `move`ing the cursor over a target, run the same down/sleep/up pattern directly with
  button 3: `DISPLAY=:0 xdotool mousedown 3; sleep 0.15; DISPLAY=:0 xdotool mouseup 3`. This
  performs the default "look" action and prints `You see: <Name>.` to the monitor box — no
  cursor-mode switch needed first. Once in look mode, simply hovering over a *different* object
  (no new right-click) also prints a fresh `You see: <Name>.` line for it. `Escape` while in
  this state opens the pause/options menu, not a look-cursor cancel — press it again or click
  Done to get back to gameplay.
- **Pause menu / Preferences**: `key Escape` from gameplay reliably opens the pause menu
  (Save Game/Load Game/Preferences/Help/Exit Game/Done). On this machine's 1920x1080 output the
  cursor lands near EXIT GAME (~(980, 555)) when the menu opens; PREFERENCES sits at roughly
  (940, 362) — `move -40 -193` then `click` gets there without a `shot`-and-recheck loop.
- `scripts/fo2ce-control.sh status` — exits 0 if fo2ce is currently running, 1 if not. Useful
  to sanity-check state between steps.
- `scripts/fo2ce-control.sh kill` — stop fo2ce. **Always run this when your scenario is done**,
  even if something went wrong — a stray process will block the next pilot's `launch`.

`launch` writes fo2ce's own stdout/stderr to `/tmp/fo2ce-control.log` — check there if `launch`
fails or the game seems stuck.

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
- The intro plays two movies (`iplogo.mve`, `intro.mve`) before the main menu. **Any keypress or
  click aborts a playing movie immediately** (`reference/fallout2-ce/src/movie.cc:764`,
  `inputGetInput() != -1`) — send a `key Return` (or similar) the moment a movie starts rather
  than waiting for it to finish; this took launch-to-menu from ~2 minutes down to ~20 seconds in
  practice. Send one key per movie in the sequence.
- Reaching the character-selection/creation screen and picking the premade character has worked
  via keyboard mnemonics: `key n` at the main menu (NEW GAME), `key t` at character selection
  (TAKE the premade), `key Return` to skip the character-intro cutscene straight into gameplay.
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
`--menu-click <panel> <n>`, `--attack <hex>`, `--door <hex>`, `--create <stats string>` (see
existing `scripts/*-golden.sh` for real examples of these flags in use). Note `--talk` takes a
screen point (`x,y`) while `--attack` and `--door` each take a single integer hex tile number,
not a coordinate pair.

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
- `--hud-click OPT` opens Hexwaste's pause menu (Save/Load/Preferences/Main Menu/Quit/Resume),
  matching fo2ce's Escape pause menu — it does NOT go straight to Preferences. Reaching the
  Preferences screen itself headlessly needs the separate `--prefs` flag; `--menu-click options 2`
  only hit-tests the row without dispatching to `OpenPreferences()`.
- `--no-build` is passed by the wrapper — if you've changed engine code, `dotnet build
  src/Hexwaste.Viewer -c Debug` first or your changes won't be reflected.
- If an action flag needs a tile/hex coordinate you don't know yet, it's fine to run a
  throwaway checkpoint first just to look at the map and figure out where things are.
  `tools/MapDump --map <name>.map` dumps a map's exit grids (per elevation) with their
  destination map and tile — useful for finding a tile that triggers a map transition.
- `--goto <tile>` onto a map-exit-grid tile queues the transition but does **not** by itself
  advance simulation time far enough for it to apply — pass `--advance-ms <n>` alongside it to
  pump the update loop until the walk-and-transition actually completes. Without it, `--goto`
  leaves you still on the origin map. A `STOPPED` line in stdout after arriving on the new map
  is expected/benign (it just means "no more path to the old target"). `--goto` alone teleports
  instantly (`8000`ms was enough just for the transition check); adding `--walk` makes the dude
  actually path there tile-by-tile first, which needs more simulated time (`15000`ms was needed
  for the artemple→arcaves distance) — tune upward if the dude hasn't arrived yet.
- A screenshot with `--walk` is pixel-identical to the equivalent instant `--goto` (verified on
  the artemple→arcaves transition) once both actually complete — `--walk` only matters if a
  checkpoint needs to show the dude mid-transit, not for where it ends up.

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
