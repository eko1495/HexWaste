# fo2ce ↔ Hexwaste Comparison Automation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the operational tooling (control scripts + a playbook doc) that lets a fresh subagent pilot fo2ce and Hexwaste through the same scenario and produce comparable screenshot checkpoints, per `docs/superpowers/specs/2026-09-05-fo2ce-comparison-automation-design.md`.

**Architecture:** Three small, focused shell scripts (`scripts/fo2ce-control.sh`, `scripts/hexwaste-checkpoint.sh`, `scripts/compare-run-init.sh`) that wrap the exact commands already verified working in the design session, plus `docs/fo2ce-comparison-playbook.md` that a fresh subagent reads to learn the whole workflow (it has zero context otherwise). The actual "pilot fo2ce/Hexwaste through a real scenario" work is *not* part of this plan — it's post-implementation usage, validated here only by one smoke-test checkpoint each.

**Tech Stack:** bash (existing `scripts/*.sh` conventions), `xdotool`, `spectacle`, `dotnet run --project src/Hexwaste.Viewer`.

## Global Constraints

- `SDL_VIDEODRIVER=x11` is required to launch fo2ce with an inputtable/screenshottable window on this machine (Wayland/KWin session) — every launch path must set it.
- Screenshots must use `spectacle -b -n -f -o <path>.png` — `ffmpeg -f x11grab` and ImageMagick `import` both silently return solid-black frames on this compositor and must not be used.
- fo2ce binary lives at `reference/fallout2-ce/run/fallout2-ce` (community continuous build); its window title is `"FALLOUT II"`.
- Hexwaste's CLI (`src/Hexwaste.Viewer/Program.cs`) applies a queued action list once per process invocation and takes exactly one `--screenshot <path>` at the end — there is no mid-run screenshot, so N checkpoints means N invocations, each replaying the action prefix up to that point.
- `FALLOUT2_DIR` (default `./game-data`) and `DISPLAY` (default `:0`) are the existing env-var conventions used by `scripts/*-golden.sh` and `scripts/quest-harvest.sh` — follow them.
- No sudo, no new dependencies beyond what's already installed (`xdotool`, `spectacle`, `dotnet`).
- This is ad hoc tooling, not a CI suite — no golden-file assertions, no `scripts/*-golden.sh` integration.
- `*.png` is already globally gitignored (`.gitignore:18`); only `scratch/compare-runs/` itself (the notes.md files and directory structure) needs a new gitignore entry.

---

### Task 1: fo2ce control script

**Files:**
- Create: `scripts/fo2ce-control.sh`

**Interfaces:**
- Produces: a CLI with subcommands `launch`, `shot <file>`, `key <xdotool-key-spec>`, `click <x> <y>`, `status`, `kill` — this is the only way later tasks (and the playbook doc, and future pilot subagents) touch fo2ce.

- [ ] **Step 1: Write the script**

```bash
#!/usr/bin/env bash
# Control fo2ce (reference/fallout2-ce/run/fallout2-ce) for the comparison-automation pilot.
# See docs/fo2ce-comparison-playbook.md for the full workflow this supports.
#
# Usage:
#   scripts/fo2ce-control.sh launch                 # start fo2ce, print its PID
#   scripts/fo2ce-control.sh shot <output.png>       # screenshot the whole screen
#   scripts/fo2ce-control.sh key <xdotool-key-spec>  # e.g. "Return", "Down Down Return"
#   scripts/fo2ce-control.sh click <x> <y>           # click inside the fo2ce window at (x,y)
#   scripts/fo2ce-control.sh status                  # exit 0 if fo2ce is running, else 1
#   scripts/fo2ce-control.sh kill                    # stop fo2ce
set -uo pipefail
cd "$(dirname "$0")/.."

RUN_DIR="reference/fallout2-ce/run"
BIN="$(cd "$RUN_DIR" && pwd)/fallout2-ce"
LOG="/tmp/fo2ce-control.log"
DISP="${DISPLAY:-:0}"

cmd="${1:-}"
shift || true

case "$cmd" in
  launch)
    if pgrep -f "$BIN" >/dev/null 2>&1; then
      echo "already running: $(pgrep -f "$BIN")" >&2
      exit 1
    fi
    ( cd "$RUN_DIR" && SDL_VIDEODRIVER=x11 DISPLAY="$DISP" "$BIN" >"$LOG" 2>&1 & )
    sleep 3
    pid="$(pgrep -f "$BIN" | head -1)"
    if [ -z "$pid" ]; then
      echo "fo2ce failed to start, see $LOG" >&2
      exit 1
    fi
    echo "$pid"
    ;;
  shot)
    out="${1:?usage: shot <output.png>}"
    mkdir -p "$(dirname "$out")"
    spectacle -b -n -f -o "$out"
    ;;
  key)
    spec="${1:?usage: key <xdotool-key-spec>}"
    wid="$(DISPLAY="$DISP" xdotool search --name "FALLOUT II" | head -1)"
    [ -n "$wid" ] || { echo "fo2ce window not found" >&2; exit 1; }
    DISPLAY="$DISP" xdotool windowactivate "$wid"
    DISPLAY="$DISP" xdotool key --window "$wid" $spec
    ;;
  click)
    x="${1:?usage: click <x> <y>}"; y="${2:?usage: click <x> <y>}"
    wid="$(DISPLAY="$DISP" xdotool search --name "FALLOUT II" | head -1)"
    [ -n "$wid" ] || { echo "fo2ce window not found" >&2; exit 1; }
    DISPLAY="$DISP" xdotool windowactivate "$wid"
    DISPLAY="$DISP" xdotool mousemove --window "$wid" "$x" "$y" click 1
    ;;
  status)
    pgrep -f "$BIN" >/dev/null 2>&1
    ;;
  kill)
    pkill -f "$BIN" 2>/dev/null || true
    ;;
  *)
    echo "usage: $0 {launch|shot <file>|key <spec>|click <x> <y>|status|kill}" >&2
    exit 2
    ;;
esac
```

- [ ] **Step 2: Make it executable**

Run: `chmod +x scripts/fo2ce-control.sh`

- [ ] **Step 3: Verify `launch` and `status` work**

Run:
```bash
scripts/fo2ce-control.sh launch
scripts/fo2ce-control.sh status; echo "status exit=$?"
```
Expected: first command prints a numeric PID; second line prints `status exit=0`.

- [ ] **Step 4: Verify `shot` produces a real screenshot**

Run:
```bash
scripts/fo2ce-control.sh shot /tmp/fo2ce-control-test.png
file /tmp/fo2ce-control-test.png
stat -c '%s' /tmp/fo2ce-control-test.png
```
Expected: `file` reports `PNG image data` with nonzero width/height; size is well over 1000 bytes (a solid-black or empty capture from a broken tool is typically tiny or reports an error instead — a real frame of the fo2ce window is tens of KB or more).

- [ ] **Step 5: Verify `kill` and `status` agree afterward**

Run:
```bash
scripts/fo2ce-control.sh kill
sleep 1
scripts/fo2ce-control.sh status; echo "status exit=$?"
```
Expected: `status exit=1` (no longer running).

- [ ] **Step 6: Commit**

```bash
git add scripts/fo2ce-control.sh
git commit -m "$(cat <<'EOF'
feat(scripts): add fo2ce-control.sh for scripted fo2ce piloting

Wraps the launch/screenshot/input/kill commands verified during the
fo2ce runnable-baseline session (SDL_VIDEODRIVER=x11, spectacle for
screenshots — ffmpeg/import return black on this compositor) into one
CLI a comparison-automation pilot subagent can call directly.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

### Task 2: Hexwaste checkpoint runner script

**Files:**
- Create: `scripts/hexwaste-checkpoint.sh`

**Interfaces:**
- Consumes: nothing from Task 1 (independent).
- Produces: a CLI `scripts/hexwaste-checkpoint.sh <output.png> [-- <hexwaste CLI action flags...>]` — the only way later tasks (and pilot subagents) capture a Hexwaste checkpoint.

- [ ] **Step 1: Write the script**

```bash
#!/usr/bin/env bash
# Run one Hexwaste checkpoint: replay a prefix of --goto/--walk/--talk/... actions and
# capture a single screenshot at the end. Hexwaste's CLI applies its action list once per
# process, ending in one --screenshot — so each checkpoint of a scenario is its own invocation
# replaying the full action prefix up to that point. See docs/fo2ce-comparison-playbook.md.
#
# Usage: scripts/hexwaste-checkpoint.sh <output.png> [-- <action flags...>]
#   scripts/hexwaste-checkpoint.sh scratch/compare-runs/x/hexwaste/01-menu.png
#   scripts/hexwaste-checkpoint.sh scratch/compare-runs/x/hexwaste/02-vault.png -- --goto 12345
set -uo pipefail
cd "$(dirname "$0")/.."

OUT="${1:?usage: $0 <output.png> [-- <action flags...>]}"
shift
if [ "${1:-}" = "--" ]; then shift; fi

GAME="${FALLOUT2_DIR:-$(pwd)/game-data}"
mkdir -p "$(dirname "$OUT")"

DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="$GAME" \
  dotnet run --project src/Hexwaste.Viewer -c Debug --no-build -- \
  --game-dir "$GAME" --no-audio "$@" --screenshot "$OUT"
```

- [ ] **Step 2: Make it executable**

Run: `chmod +x scripts/hexwaste-checkpoint.sh`

- [ ] **Step 3: Build the Viewer project (prerequisite for `--no-build`)**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 4: Verify a checkpoint with no extra actions produces a real screenshot**

Run:
```bash
scripts/hexwaste-checkpoint.sh /tmp/hexwaste-checkpoint-test.png
file /tmp/hexwaste-checkpoint-test.png
stat -c '%s' /tmp/hexwaste-checkpoint-test.png
```
Expected: `file` reports `PNG image data`; size well over 1000 bytes (the default `artemple.map` view, not a blank/error image).

- [ ] **Step 5: Verify an action flag is actually applied (sanity check the passthrough)**

Run:
```bash
scripts/hexwaste-checkpoint.sh /tmp/hexwaste-checkpoint-zoom.png -- --zoom 2
file /tmp/hexwaste-checkpoint-zoom.png
```
Expected: `file` reports `PNG image data`; command exits 0 (confirms flags after `--` reach the Viewer instead of being swallowed by the wrapper).

- [ ] **Step 6: Commit**

```bash
git add scripts/hexwaste-checkpoint.sh
git commit -m "$(cat <<'EOF'
feat(scripts): add hexwaste-checkpoint.sh for scripted checkpoint capture

Thin wrapper standardizing the dotnet run + --game-dir/--no-audio/
--screenshot boilerplate a comparison-automation pilot subagent needs
to replay one action-list prefix per checkpoint.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

### Task 3: Run directory scaffolding script

**Files:**
- Create: `scripts/compare-run-init.sh`
- Modify: `.gitignore` (add `scratch/compare-runs/`)

**Interfaces:**
- Consumes: nothing from Tasks 1-2 (independent).
- Produces: `scripts/compare-run-init.sh <scenario-slug>` which prints (on its last stdout line) the created run directory path, of the form `scratch/compare-runs/<slug>-<timestamp>/`, containing `fo2ce/notes.md` and `hexwaste/notes.md`. Task 5 relies on this exact output contract (last line = the path).

- [ ] **Step 1: Write the script**

```bash
#!/usr/bin/env bash
# Scaffold a fresh comparison run's artifact directory:
#   scratch/compare-runs/<slug>-<timestamp>/{fo2ce,hexwaste}/notes.md
# Prints the run directory path as the LAST line of stdout, so callers can capture it with
# e.g. `RUN_DIR="$(scripts/compare-run-init.sh my-scenario | tail -1)"`.
#
# Usage: scripts/compare-run-init.sh <scenario-slug>
set -uo pipefail
cd "$(dirname "$0")/.."

SLUG="${1:?usage: $0 <scenario-slug>}"
TS="$(date +%Y%m%d-%H%M%S)"
RUN_DIR="scratch/compare-runs/${SLUG}-${TS}"

mkdir -p "$RUN_DIR/fo2ce" "$RUN_DIR/hexwaste"
: > "$RUN_DIR/fo2ce/notes.md"
: > "$RUN_DIR/hexwaste/notes.md"

echo "$RUN_DIR"
```

- [ ] **Step 2: Make it executable**

Run: `chmod +x scripts/compare-run-init.sh`

- [ ] **Step 3: Add the gitignore entry**

In `.gitignore`, add a new line (near the other `scratch`-adjacent or generated-output entries; appending at the end of the file is fine):

```
scratch/compare-runs/
```

- [ ] **Step 4: Verify the script's directory contract**

Run:
```bash
RUN_DIR="$(scripts/compare-run-init.sh smoke-test | tail -1)"
echo "RUN_DIR=$RUN_DIR"
ls -la "$RUN_DIR/fo2ce" "$RUN_DIR/hexwaste"
git status --short "$RUN_DIR"
```
Expected: `RUN_DIR` matches `scratch/compare-runs/smoke-test-<14-digit-timestamp>`; both subdirectories contain an empty `notes.md`; `git status --short` shows nothing for `$RUN_DIR` (proves the gitignore entry from Step 3 is working — an untracked, non-ignored file would show as `??`).

- [ ] **Step 5: Commit**

```bash
git add scripts/compare-run-init.sh .gitignore
git commit -m "$(cat <<'EOF'
feat(scripts): add compare-run-init.sh to scaffold comparison-run artifacts

Creates scratch/compare-runs/<slug>-<timestamp>/{fo2ce,hexwaste}/notes.md
and gitignores the whole scratch/compare-runs/ tree so ad hoc comparison
runs never risk landing in a commit.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

### Task 4: Comparison playbook doc

**Files:**
- Create: `docs/fo2ce-comparison-playbook.md`

**Interfaces:**
- Consumes: the exact CLIs produced by Tasks 1-3 (`scripts/fo2ce-control.sh`, `scripts/hexwaste-checkpoint.sh`, `scripts/compare-run-init.sh`) — must document their real usage, not paraphrase it.
- Produces: the document a fresh pilot subagent is pointed at (via the Agent tool prompt) when a future comparison run is kicked off — this is the plan's only consumer-facing deliverable besides the scripts themselves.

- [ ] **Step 1: Write the playbook**

```markdown
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
```

- [ ] **Step 2: Verify the doc references only real, existing script paths and flags**

Run:
```bash
grep -oE 'scripts/[a-z0-9-]+\.sh' docs/fo2ce-comparison-playbook.md | sort -u | while read -r f; do
  [ -x "$f" ] && echo "OK  $f" || echo "MISSING/NOT EXECUTABLE  $f"
done
```
Expected: every line printed is `OK` (all three scripts from Tasks 1-3 exist and are
executable). If anything prints `MISSING`, fix the path in the doc before continuing.

- [ ] **Step 3: Verify no placeholder text slipped in**

Run: `grep -inE 'TBD|TODO|FIXME|xxx' docs/fo2ce-comparison-playbook.md`
Expected: no output (empty grep result, exit code 1).

- [ ] **Step 4: Commit**

```bash
git add docs/fo2ce-comparison-playbook.md
git commit -m "$(cat <<'EOF'
docs: add fo2ce-comparison-playbook.md for pilot subagents

The concrete, zero-prior-context reference a fresh subagent reads before
piloting fo2ce or Hexwaste through a comparison scenario: exact script
usage, workflow steps, checkpoint/notes format, and gotchas from the
fo2ce runnable-baseline session (SDL_VIDEODRIVER=x11, spectacle,
community-fork QoL caveat).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

### Task 5: End-to-end smoke validation

**Files:**
- None created/modified — this task only runs the scripts from Tasks 1-3 together and checks the result. If any check fails, the fix belongs in whichever Task 1-4 script/doc is wrong (reopen that task; do not patch around it here).

**Interfaces:**
- Consumes: `scripts/compare-run-init.sh` (Task 3), `scripts/fo2ce-control.sh` (Task 1), `scripts/hexwaste-checkpoint.sh` (Task 2).
- Produces: nothing for later tasks — this is the plan's final acceptance check, proving the three scripts compose into one working pipeline the way the playbook (Task 4) describes.

- [ ] **Step 1: Scaffold a smoke-test run directory**

Run:
```bash
RUN_DIR="$(scripts/compare-run-init.sh e2e-smoke | tail -1)"
echo "RUN_DIR=$RUN_DIR"
```
Expected: prints a `scratch/compare-runs/e2e-smoke-<timestamp>` path.

- [ ] **Step 2: Capture one fo2ce checkpoint**

Run:
```bash
scripts/fo2ce-control.sh launch
sleep 3
scripts/fo2ce-control.sh shot "$RUN_DIR/fo2ce/01-boot.png"
printf '01 | boot | intro movie / menu on screen\n' >> "$RUN_DIR/fo2ce/notes.md"
scripts/fo2ce-control.sh kill
```
Expected: no errors; `$RUN_DIR/fo2ce/01-boot.png` exists.

- [ ] **Step 3: Capture one Hexwaste checkpoint**

Run:
```bash
scripts/hexwaste-checkpoint.sh "$RUN_DIR/hexwaste/01-boot.png"
printf '01 | boot | default artemple.map view, no action flags\n' >> "$RUN_DIR/hexwaste/notes.md"
```
Expected: no errors; `$RUN_DIR/hexwaste/01-boot.png` exists.

- [ ] **Step 4: Verify both checkpoint pairs are real, viewable images**

Run:
```bash
file "$RUN_DIR/fo2ce/01-boot.png" "$RUN_DIR/hexwaste/01-boot.png"
stat -c '%n %s bytes' "$RUN_DIR/fo2ce/01-boot.png" "$RUN_DIR/hexwaste/01-boot.png"
cat "$RUN_DIR/fo2ce/notes.md" "$RUN_DIR/hexwaste/notes.md"
```
Expected: both `file` lines report `PNG image data` with real (nonzero) dimensions; both
sizes are well over 1000 bytes; both notes files show their one recorded line. Then actually
view both images with the Read tool to confirm they show real game content (fo2ce: intro
movie or menu; Hexwaste: the default map) — this is the check that catches a silently-black
screenshot slipping through a `file`/size check.

- [ ] **Step 5: Confirm nothing from the smoke run risks being committed**

Run: `git status --short "$RUN_DIR"`
Expected: no output (the `scratch/compare-runs/` gitignore entry from Task 3 covers it).

- [ ] **Step 6: No commit for this task**

This task produces no tracked files — Tasks 1-4 are already committed individually. Leave
the smoke-test run directory in place (or delete it with `rm -rf "$RUN_DIR"` — it's gitignored
either way, so either choice is safe) and report the plan complete.

---

## Self-Review Notes

- **Spec coverage:** fo2ce piloting (Task 1 + Task 4's fo2ce section), Hexwaste checkpoint
  replay (Task 2 + Task 4's Hexwaste section), run-artifact layout with `notes.md` format
  (Task 3 + Task 4's format recap), the gitignore safety net (Task 3), and the community-fork
  QoL caveat (documented in Task 4's fo2ce gotchas) are all covered. The spec's "comparison
  step" itself (reading checkpoint pairs and proposing fixes) is explicitly out of scope for
  this plan — it happens in the main conversation using the artifacts this plan produces, not
  as a scripted/coded step.
- **Placeholder scan:** none found; every step has literal commands and expected output.
- **Type/interface consistency:** `compare-run-init.sh`'s "last line = run dir path" contract
  is used identically in Task 3 Step 4 and Task 5 Step 1. `fo2ce-control.sh`'s subcommand
  names (`launch`, `shot`, `key`, `click`, `status`, `kill`) match between Task 1's script,
  Task 4's playbook, and Task 5's smoke test. `hexwaste-checkpoint.sh`'s `<output.png> [--
  <flags>]` signature matches between Task 2 and Task 5.
