# VC Cross-Town Quest QA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the two Vault City cross-town delivery quests (gvar 321 Moore→Bishop, gvar 89 Lynette→Westin) as byte-stable goldens in the quest-e2e suite, produce a written feasibility verdict on gvar 529 (Stark's scout-recon), and reconcile the VC sweep docs.

**Architecture:** This is QA-sweep work, not feature code: each quest is driven through the real script logic (dialogue VM + `set_global_var`) using the existing headless harness verbs (`--goto-map`, `--talk-seq`, `--give`, `--get-global`, `--quest-probe`), then banked as a scenario line in `scripts/quest-golden.sh` with a recorded fixture. No engine or harness code changes (except, at most, a trivial flag if the 529 probe finds one is all that's missing — and then only with a documented finding, not speculatively).

**Tech Stack:** .NET 10 / MonoGame viewer harness (`src/Hexwaste.Viewer`), `tools/ProcAnalyze` (C#), `tools/int_disasm.py` (Python 3), `tools/DatDump`, bash golden runner `scripts/quest-golden.sh`.

## Global Constraints

- **No `--set-global` faking** of a quest gvar or its prerequisites. Allowed state/ID inputs only: tiles, gvars (read), item pids via `--give` (the sanctioned item-acquire shortcut — precedent: `quest-anna-locket`, `quest-modoc-watch`), option indices, `--rng-seed`.
- **No copyrighted game dialogue text** may appear in any committed file (recipes, comments, docs, fixtures). STATE/ID only. You will *see* dialogue text in local harness output — never paste it into the repo.
- Every scenario starts with the standard chargen line already defined in `scripts/quest-golden.sh`: `CREATE="--create 5,5,5,5,5,5,5:0,4,5:0"` and ends with `--rng-seed 1`.
- Requires real game data + a display: `FALLOUT2_DIR` (default `./game-data`) and `DISPLAY` must be set. If `dotnet run` on the viewer exits instantly with no output, the display is the first suspect.
- One conventional commit per landed quest (`qa: ...`), so an aborted later quest loses nothing.
- Scratch space (extracted `.int` files etc.): use the session scratchpad dir, NEVER the repo tree. Below, `$S` = `/tmp/claude-1000/-home-eko-dev-FPOC/78d78d3d-c6c3-4eb5-b358-8abe8172315d/scratchpad`.
- `git status` must stay clean of scratch artifacts; `game-data/` and `*.int` are gitignored — keep it that way.

## Ground truth already traced (verified 2026-07-21, bake-in values)

These were derived with the exact commands shown in each task's re-derive step; treat them as starting values, but **re-verify at runtime** — static option ordinals shift under runtime IQ/state filtering.

| Quest | Giver | Completer | Items | Writes |
| --- | --- | --- | --- | --- |
| 321 (display≥1, completed≥2) | `VCMoore` — vctydwtn, elev 0, tile **17485** | `ncBishop` — newr2, elev **2**, tile **17678** | briefcase pid **336** (script-granted: Moore's Node008 does `create_object` 336; Bishop's gate accepts 336 OR 335) | Moore Node008/Node016c `:=1`; Bishop Node004 `:=2` or `:=3` (both ≥ completed) |
| 89 (display≥1, completed≥3) | `VCLynett` — vctycocl, elev 0, tile **17100** | `SCWestin` — ncr3, elev 0, tile **17892** | Lynette Holodisk pid **337**; **Bishop's Holodisk pid 447** — Westin's completing proc `getDisk` checks `obj_carrying_pid_obj(dude, 447)`, then sets 89:=3 and removes/destroys the disk | Lynette Node123/Node126 `:=1`, Node119a `:=2`, Node129 `:=4` (also ≥ completed); Westin `talk_p_proc → Node001 =opt7=> Node017 =call=> getDisk` `:=3` |

⚠ 89 is NOT a plain delivery: the completing item is pid 447 (Bishop's Holodisk), not Lynette's 337. Task 2 first maps the gates, then decides between `--give 447:1` (sanctioned acquire shortcut) and a story-gate finding.

⚠ `--quest-paths` prints 0-based `optN` labels; `--talk-seq` takes **1-based** option indices (`opt0` → talk-seq `1`). All existing recipes are 1-based.

---

### Task 1: Golden `quest-moore-briefcase` (gvar 321)

**Files:**
- Modify: `scripts/quest-golden.sh` (add one SCENARIOS entry + comment)
- Create: `tests/golden-quest/quest-moore-briefcase.txt` (recorded fixture)

**Interfaces:**
- Consumes: existing harness verbs and the `run()` helper in `scripts/quest-golden.sh` (unchanged).
- Produces: scenario name `quest-moore-briefcase` — Task 4's doc update and suite-green check refer to it by this exact name.

- [ ] **Step 1: Build the viewer once and define the interactive run helper**

```bash
cd /home/eko/dev/FPOC
dotnet build src/Hexwaste.Viewer -c Debug
S=/tmp/claude-1000/-home-eko-dev-FPOC/78d78d3d-c6c3-4eb5-b358-8abe8172315d/scratchpad; mkdir -p "$S/int"
# Interactive probe: same invocation as quest-golden.sh's run(), but WITHOUT the output
# filter, so you see the dialogue option lists needed to nail --talk-seq indices.
probe() { timeout 120 env DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="${FALLOUT2_DIR:-$(pwd)/game-data}" \
  dotnet run --project src/Hexwaste.Viewer -c Debug --no-build -- \
  --game-dir "${FALLOUT2_DIR:-$(pwd)/game-data}" --no-audio \
  --create 5,5,5,5,5,5,5:0,4,5:0 "$@" 2>&1; }
```

Expected: build succeeds; `probe` defined in your shell.

- [ ] **Step 2: Re-derive the statics (verifies the bake-in table)**

```bash
dotnet run --project tools/ProcAnalyze -- --game-dir ./game-data --quest-paths 321
dotnet run --project tools/DatDump -- --game-dir ./game-data extract "scripts\\vcmoore.int" "$S/int/vcmoore.int"
dotnet run --project tools/DatDump -- --game-dir ./game-data extract "scripts\\ncbishop.int" "$S/int/ncbishop.int"
python3 tools/int_disasm.py "$S/int/vcmoore.int" --writes 321
python3 tools/int_disasm.py "$S/int/ncbishop.int" Node003 | head -60
```

Expected: `--quest-paths 321` prints `COMPLETES ncBishop: Node004 := 3` / `:= 2` via `talk_p_proc =call=> Node003 =opt0=> Node004`, and `advances VCMoore: Node008 := 1` via `talk_p_proc =call=> Node018 =opt1=> Node018a =call=> Node007 =opt1=> Node008`. The Node003 disasm shows `obj_is_carrying_obj` on pids 336 and 335 OR-ed before the `giq_option` block (the delivery options are carry-gated).

- [ ] **Step 3: Drive the Moore accept interactively (321: 0→1)**

Static hint: the accept path is Node018 opt1 → Node007 opt1 → Node008, i.e. first guess `--talk-seq 17485 2,2` (1-based). Iterate: start with one option and read the printed option list at each level until the run shows `get-global: 321=1`.

```bash
probe --goto-map vctydwtn.map --get-global 321 --talk-seq 17485 2,2 --get-global 321 --rng-seed 1 \
  | grep -E "^(get-global:|option|reply|talk)" 
# If 321 stays 0: re-run printing everything (drop the grep), read the option lists,
# adjust the index chain, repeat. Do NOT paste dialogue text anywhere in the repo.
```

Expected: final `get-global: 321=1`. Moore's Node008 also `create_object`s briefcase 336 into the dude's inventory — no `--give` needed for 321.

- [ ] **Step 4: Drive the Bishop delivery (321: 1→2 or 1→3)**

Append the cross-town hop to the working accept chain. Bishop is on elevation 2, so use the `map:tile:elev` goto form (precedent: `quest-kill-ratgod`).

```bash
probe --goto-map vctydwtn.map --get-global 321 --talk-seq 17485 2,2 --get-global 321 \
  --goto-map newr2.map:17678:2 --talk-seq 17678 1 --get-global 321 --quest-probe --rng-seed 1 \
  | grep -E "^(get-global:|quest-probe:)"
# Iterate the Bishop --talk-seq indices the same way: his greeting may route through guard/intro
# nodes before Node003; the briefcase-delivery option only appears while carrying pid 336.
```

Expected: final `get-global: 321=2` (or `=3` — Node004 has both writes; either is ≥ the completed threshold 2) and `quest-probe:` showing quest 321 completed. Whichever value your deterministic chain produces is the value the fixture locks.

- [ ] **Step 5: Add the scenario to `scripts/quest-golden.sh`**

Append inside the `SCENARIOS=(` array, before the closing `)`, substituting the exact option chains nailed in steps 3–4 (the chains below are the static first-guess — use what actually ran):

```bash
  # Deliver Moore's briefcase to Bishop (Vault City→New Reno, GVAR 321) — the first CROSS-TOWN
  # delivery golden, full lifecycle 0→1→2 via the real dialogue (no --set-global). Moore
  # (vctydwtn 17485) hands over the locked briefcase on accepting (Node008 create_object 336 →
  # 321:=1); Bishop (newr2 elev2 17678) takes it while carried (Node003 obj_is_carrying 336/335 →
  # Node004 → 321:=2, completed). Crosses vctydwtn→newr2, proving the delivery pattern spans towns.
  "quest-moore-briefcase|$CREATE --goto-map vctydwtn.map --get-global 321 --talk-seq 17485 2,2 --get-global 321 --goto-map newr2.map:17678:2 --talk-seq 17678 1 --get-global 321 --quest-probe --rng-seed 1"
```

- [ ] **Step 6: Verify the suite reports the missing fixture (the failing state)**

```bash
scripts/quest-golden.sh check 2>&1 | grep -E "quest-moore-briefcase|ALL PASS|FAIL"
```

Expected: `MISSING FIXTURE: quest-moore-briefcase (run 'record' first)` and overall `quest e2e: FAIL`. (Every other scenario must still say `ok` — if any pre-existing golden diffs, STOP: something else broke.)

- [ ] **Step 7: Record the fixture, then verify byte-stable replay**

`record` mode re-records EVERY fixture — the git diff is the guard that nothing else moved.

```bash
scripts/quest-golden.sh record
git status --porcelain tests/golden-quest/
```

Expected: `record` prints `recorded quest-moore-briefcase (N lines)`; git shows ONLY `?? tests/golden-quest/quest-moore-briefcase.txt` (no modified pre-existing fixtures — if any existing fixture changed, STOP and investigate before proceeding).

```bash
scripts/quest-golden.sh check
```

Expected: `ok  quest-moore-briefcase` (check runs each scenario twice and diffs, so this also proves replay determinism) and `quest e2e: ALL PASS`.

- [ ] **Step 8: Inspect the new fixture for the lifecycle + no dialogue text**

```bash
cat tests/golden-quest/quest-moore-briefcase.txt
```

Expected: only `get-global:` / `quest-probe:` lines; 321 visible as 0 → 1 → 2 (or 3); no prose.

- [ ] **Step 9: Commit**

```bash
git add scripts/quest-golden.sh tests/golden-quest/quest-moore-briefcase.txt
git commit -m "qa: VC cross-town golden — Moore's briefcase to Bishop (gvar 321)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Golden `quest-lynette-holodisk` (gvar 89)

**Files:**
- Modify: `scripts/quest-golden.sh` (add one SCENARIOS entry + comment)
- Create: `tests/golden-quest/quest-lynette-holodisk.txt` (recorded fixture)
- Modify (only on the story-gated outcome): `docs/qa-sweep/vaultcity.md` (gate-finding instead of a golden)

**Interfaces:**
- Consumes: `probe()` helper from Task 1 Step 1 (re-define it if running in a fresh shell).
- Produces: scenario name `quest-lynette-holodisk` (or a documented gate-finding) — referenced by Task 4.

- [ ] **Step 1: Map the gates on both ends (this quest is NOT a plain delivery)**

```bash
S=/tmp/claude-1000/-home-eko-dev-FPOC/78d78d3d-c6c3-4eb5-b358-8abe8172315d/scratchpad; mkdir -p "$S/int"
dotnet run --project tools/ProcAnalyze -- --game-dir ./game-data --quest-paths 89
dotnet run --project tools/DatDump -- --game-dir ./game-data extract "scripts\\vclynett.int" "$S/int/vclynett.int"
dotnet run --project tools/DatDump -- --game-dir ./game-data extract "scripts\\scwestin.int" "$S/int/scwestin.int"
python3 tools/int_disasm.py "$S/int/vclynett.int" --writes 89
python3 tools/int_disasm.py "$S/int/scwestin.int" Node001 | grep -nE "get_global_var|obj_carrying|push_int|giq_option" | head -60
python3 tools/int_disasm.py "$S/int/scwestin.int" Node017 | head -40
python3 tools/int_disasm.py "$S/int/scwestin.int" getDisk | head -20
```

Expected from the already-done trace: `getDisk` checks `obj_carrying_pid_obj(dude, 447)` → sets `89:=3` → removes/destroys the disk. The NEW information to extract here: what gates Node001's opt7 (the route to Node017) and Node017 itself — specifically any `get_global_var` checks (89 stage? 321? an NCR/VC story gvar?) that decide whether the Westin conversation offers the disk exchange at all. Write down every gvar+threshold you find.

- [ ] **Step 2: Drive the Lynette accept (89: 0→1/2)**

Static hint: the `:=1` writes sit deep in Node053 (opt12/opt14/opt15 0-based) — these WILL be filtered/reordered at runtime; expect to iterate. Lynette is in the Council building map.

```bash
probe --goto-map vctycocl.map --get-global 89 --talk-seq 17100 1 --get-global 89 --rng-seed 1
# Iterate the option chain from the printed lists toward the holodisk-errand branch until
# the final line shows 89=1 (Node123/Node126) or 89=2 (Node119a). Note whether the run
# grants item 337 (Lynette's disk) — visible as an inventory/create line in the full output.
```

Expected: `get-global: 89=1` (or `=2`). If Lynette's citizenship/audience gating blocks the branch entirely for a fresh character, note the blocking gvar from Step 1 and jump to Step 6 (gate-finding outcome).

- [ ] **Step 3: Drive the Westin exchange (89 → 3)**

Two sub-cases, decided by Step 1's gate map:

*(a) Reachable with items in hand* — chain the hop; `--give 447:1` is the sanctioned acquire shortcut for Bishop's Holodisk (same rule that `--give`s Anna's locket / the Modoc watch), and `--give 337:1` covers Lynette's disk if her accept node didn't script-grant it:

```bash
probe --goto-map vctycocl.map --get-global 89 --talk-seq 17100 <accept-chain> --get-global 89 \
  --give 337:1 --give 447:1 \
  --goto-map ncr3.map --talk-seq 17892 <westin-chain> --get-global 89 --quest-probe --rng-seed 1
# <accept-chain> = Step 2's nailed indices; <westin-chain> starts from the printed option
# list, aiming at the opt7→Node017 route (1-based first guess: 8).
```

Expected: final `get-global: 89=3`, `quest-probe:` shows quest 89 completed.

*(b) Story-gated* — Step 1 revealed a prereq gvar (e.g. Westin only talks disks after the 321/Bishop arc or an NCR story stage) that cannot be reached by real dialogue from a cold boot. Then per the spec this quest's deliverable is the documented gate, not a golden → go to Step 6.

- [ ] **Step 4: (reachable case) Add the scenario, verify fail → record → check, exactly as Task 1 Steps 5–8**

Append inside `SCENARIOS=(`, with the nailed chains substituted:

```bash
  # Deliver Lynette's holodisk to Westin (Vault City→NCR, GVAR 89) — cross-town, and NOT a plain
  # delivery: Westin's completing proc (getDisk) consumes BISHOP's holodisk (pid 447, the
  # incriminating disk; --give = the sanctioned acquire shortcut, like Anna's locket), removing
  # it and setting 89:=3 (completed). Lynette (vctycocl 17100) activates the errand (89:=1);
  # Lynette's own disk is pid 337. Crosses vctycocl→ncr3.
  "quest-lynette-holodisk|$CREATE --goto-map vctycocl.map --get-global 89 --talk-seq 17100 <accept-chain> --get-global 89 --give 337:1 --give 447:1 --goto-map ncr3.map --talk-seq 17892 <westin-chain> --get-global 89 --quest-probe --rng-seed 1"
```

Then:

```bash
scripts/quest-golden.sh check 2>&1 | grep quest-lynette-holodisk   # expect: MISSING FIXTURE
scripts/quest-golden.sh record                                      # expect: recorded quest-lynette-holodisk
git status --porcelain tests/golden-quest/                          # expect: ONLY ?? quest-lynette-holodisk.txt
scripts/quest-golden.sh check                                       # expect: ok + ALL PASS
cat tests/golden-quest/quest-lynette-holodisk.txt                   # expect: 89 lifecycle, IDs only, no prose
```

- [ ] **Step 5: (reachable case) Commit**

```bash
git add scripts/quest-golden.sh tests/golden-quest/quest-lynette-holodisk.txt
git commit -m "qa: VC cross-town golden — Lynette's holodisk to Westin (gvar 89)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 6: (story-gated case only) Document the gate instead**

In `docs/qa-sweep/vaultcity.md`, move quest 89 into the REMAIN section's story-gated tier with the exact finding: the gating gvar(s) + thresholds from Step 1, which NPC/arc sets them, and the tier label `B4` (campaign-state fixture track). Model the entry on the existing 85/Dr-Troy note. Commit:

```bash
git add docs/qa-sweep/vaultcity.md
git commit -m "qa: VC 89 (Lynette holodisk) — story-gated, documented for B4 (no golden)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Feasibility verdict on gvar 529 (Stark's Gecko recon) — investigate-only

**Files:**
- Modify: `docs/qa-sweep/vaultcity.md` (the verdict — this is the deliverable)

**Interfaces:**
- Consumes: nothing from Tasks 1–2.
- Produces: a "529 verdict" subsection in `vaultcity.md`; Task 4's README update reflects it. **No engine/harness code in this task** unless the probe proves ONE trivial flag is the only missing piece — and then land it with its own commit + golden, not speculatively.

- [ ] **Step 1: Static trace — who writes 529 and on what trigger**

```bash
S=/tmp/claude-1000/-home-eko-dev-FPOC/78d78d3d-c6c3-4eb5-b358-8abe8172315d/scratchpad; mkdir -p "$S/int"
dotnet run --project tools/ProcAnalyze -- --game-dir ./game-data --quest-paths 529
# For each writer script the trace names (expected: VC's Stark + possibly a worldmap/NCR-entry hook):
dotnet run --project tools/DatDump -- --game-dir ./game-data extract "scripts\\<writer>.int" "$S/int/<writer>.int"
python3 tools/int_disasm.py "$S/int/<writer>.int" --writes 529
python3 tools/int_disasm.py "$S/int/<writer>.int" <writer-proc>   # operand level: what state it reads
```

Record: which proc writes each 529 value, and what it reads — specifically whether the "8 sectors scouted" condition is a set of gvars, LVARs, or a `metarule`/worldmap-visited query.

- [ ] **Step 2: Check what the harness can already drive**

```bash
grep -n '"--' src/Hexwaste.Viewer/*.cs | grep -iE "travel|world|visit|goto|teleport" | head -20
grep -rn "case .*visited\|Visited" src/Hexwaste.Viewer --include=*.cs -l | head
```

Question to answer: can existing verbs (worldmap travel, `--goto-map` to the NCR entrance map, `--pump-ms`) legitimately flip the state Step 1 identified? "Legitimately" = through the same code path the game uses (visiting maps/sectors), not by poking the state store directly — the no-`--set-global` rule applies in spirit to whatever store the scout-state lives in.

- [ ] **Step 3: Verdict — one of exactly three outcomes, written into `vaultcity.md`**

Add a `**529 verdict (2026-07-21):**` block with one of:
1. **Drivable now** — the recipe sketch (verbs + order). Then actually land it as golden `quest-stark-scout` following the Task 1 Step 5–9 shape (scenario + fixture + commit).
2. **One trivial flag short** — name the missing flag, what it must do, and why it is trivial (≤ ~20 lines, no new subsystem). Land flag + golden + commit only if it truly meets that bar; otherwise treat as outcome 3.
3. **Needs real new machinery** — name the subsystem (e.g. per-subtile visited tracking exposed to scripts) and file it as the B-tier follow-up; no code.

- [ ] **Step 4: Commit the verdict (with whatever it shipped)**

```bash
git add docs/qa-sweep/vaultcity.md   # plus scripts/quest-golden.sh + fixture if outcome 1/2 landed a golden
git commit -m "qa: VC 529 (Stark scout-recon) — feasibility verdict

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Reconcile the sweep docs + final suite green

**Files:**
- Modify: `docs/qa-sweep/vaultcity.md`
- Modify: `docs/qa-sweep/README.md`

**Interfaces:**
- Consumes: outcomes of Tasks 1–3 (which goldens landed, which findings were documented).

- [ ] **Step 1: Update `docs/qa-sweep/vaultcity.md`**

It is stale ahead of this session's results: the header says "3/10 done" while 459 (rescue Joshua, golden `quest-rescue-joshua`) is already landed but still listed under REMAIN. Bring it to truth:
- Header count = 4 pre-session + this session's landings.
- Move 459 to DONE with its recipe summary (from the `quest-rescue-joshua` line in `scripts/quest-golden.sh`: Amanda vctyctyd 22673 accept `1,1,1,1,1,1` → Barkus vctydwtn 14896 bribe chain `1,1,4,1,1,1,1` with `--give 41:5000` → return to Amanda `1` → 459:=3).
- Move 321 / 89 to DONE with tiles/pids/chains (or 89's gate-finding), fold in the 529 verdict, and refresh the tiles/pids footer (add: Moore vctydwtn 17485, Lynette vctycocl 17100, Bishop newr2:2 17678, Westin ncr3 17892; briefcase 336, Lynette disk 337, Bishop disk 447).

- [ ] **Step 2: Update `docs/qa-sweep/README.md`**

Update the VC row of the status table and the total golden count line (was "16 quest goldens total", already stale vs the 29 pre-session fixtures — set it to the actual post-session `ls tests/golden-quest | wc -l`).

- [ ] **Step 3: Full-suite final check**

```bash
scripts/quest-golden.sh check
```

Expected: `ok` for every scenario, `quest e2e: ALL PASS`. Also confirm no scratch files leaked into the tree: `git status --porcelain` shows only the intended doc changes.

- [ ] **Step 4: Commit**

```bash
git add docs/qa-sweep/vaultcity.md docs/qa-sweep/README.md
git commit -m "docs: VC sweep reconciled — cross-town goldens landed, 529 verdict, counts fixed

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```
