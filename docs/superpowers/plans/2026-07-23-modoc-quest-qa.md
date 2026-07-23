# Modoc Quest QA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land Modoc quests 110 (garden rats) and 693 (Jonny/Slag caves) as byte-stable goldens (or precise documented gate-findings), record the 108 writes=0 vanilla-gap verdict, optionally retry 105 timeboxed, and reconcile the Modoc docs.

**Architecture:** QA-sweep work on the established pattern (see the VC plan `2026-07-21-vc-crosstown-quest-qa.md` for the fully-worked example): per quest — static trace (`ProcAnalyze --quest-paths`, `int_disasm.py`), interactive drive (`probe()` harness runs), bank (scenario in `scripts/quest-golden.sh` + fixture + one commit). No engine/harness code changes.

**Tech Stack:** .NET 10 / MonoGame viewer harness, `tools/ProcAnalyze`, `tools/int_disasm.py`, `tools/DatDump`, `scripts/quest-golden.sh`.

## Global Constraints

- No `--set-global` faking of a quest gvar or its prerequisites. Allowed inputs: tiles, gvars (read), item pids via `--give` (sanctioned acquire shortcut), option indices, `--rng-seed`, `--set-hour`, `--teleport`/`--escort-pump`, `--pump-ms`, `--kill` (fires the real destroy_p_proc path).
- No copyrighted game dialogue text in any committed file OR report file — state/IDs only (tiles, gvars, pids, option indices, node/proc names, msg numbers).
- Every scenario: standard `CREATE="--create 5,5,5,5,5,5,5:0,4,5:0"` + trailing `--rng-seed 1`.
- Needs game data (`./game-data`) + `DISPLAY` (`:0` pattern).
- One conventional commit (`qa: ...`) per landed unit.
- Scratch (extracted `.int` etc.): `$S` = `/tmp/claude-1000/-home-eko-dev-FPOC/78d78d3d-c6c3-4eb5-b358-8abe8172315d/scratchpad` — never the repo tree.
- `record` mode rewrites ALL fixtures: after recording, `git status --porcelain tests/golden-quest/` must show ONLY the new fixture; anything else changed → STOP, investigate, do not commit.

## Ground truth already traced (2026-07-23; re-verify ordinals at runtime — static optN is 0-based, --talk-seq is 1-based, IQ/state filtering shifts them)

| Quest | Facts |
| --- | --- |
| 110 (display≥4, completed≥7) | All writes in `mcFarrel`: activation Node001 =opt7⇒ Node994 `:=4`; completers Node001 =opt5⇒ Node021 and =opt6⇒ Node029, each writing `:=3` OR `:=8` state-dependent — the discriminating condition (rat-death accounting) is THE thing to trace. Earlier sweep: killing all modgard rats moved neither 110 nor gvar 297 (110 was likely never at 4). Farrel modinn 25088; garden rat tiles (modgard): 14494 14696 16892 17098 17680 18684 21899 22894 23887; molerat modshit 9901. |
| 693 (display≥1, completed≥2) | 15 writes across three scripts: `mcJonny` Node016/Node017 `:=3` (via Node002 =opt0⇒ Node014 =opt3/opt5⇒); `mcVegeir` Node990 `:=4`/`:=2`; `mcBaltha` Node003 `:=4`/`:=2` (talk_p_proc direct — state-gated), Node024 `:=2`/Node025 `:=3` (via Node004), Node026 non-dialog trigger `:=4`/`:=2`. Balthas = Jonny's father (Modoc main); Jonny + Vegeir = Slag caves (ghost farm arc — the landed 631 golden `quest-modoc-ghostfarm` drives modmain 20143 and is the likely prereq context). |
| 108 | writes=0 across all 1263 scripts — P124-pinned vanilla content gap. Verdict-only. |
| 105 (display≥4, completed≥7) | Parked (same quest as 106). Retry targets if attempted: `mcCornel` Node024 (Node002 =opt2⇒, takes the watch) or Node025 (=opt3⇒); `mcFarrel` Node025 (Node001 =opt10⇒). Use `--talk-seq 13490 -` auto mode. Timebox strictly. |

The interactive probe helper (define once per shell; build first with `dotnet build src/Hexwaste.Viewer -c Debug`):

```bash
probe() { timeout 120 env DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="${FALLOUT2_DIR:-$(pwd)/game-data}" \
  dotnet run --project src/Hexwaste.Viewer -c Debug --no-build -- \
  --game-dir "${FALLOUT2_DIR:-$(pwd)/game-data}" --no-audio \
  --create 5,5,5,5,5,5,5:0,4,5:0 "$@" 2>&1; }
```

Script extraction for disasm:

```bash
dotnet run --project tools/DatDump -- --game-dir ./game-data extract "scripts\\<name>.int" "$S/int/<name>.int"
python3 tools/int_disasm.py "$S/int/<name>.int" <ProcName>          # operand-level
python3 tools/int_disasm.py "$S/int/<name>.int" --writes <gvar>     # writer procs
```

---

### Task 1: Golden `quest-modoc-rats` (gvar 110)

**Files:**
- Modify: `scripts/quest-golden.sh` (one SCENARIOS entry + state/ID comment)
- Create: `tests/golden-quest/quest-modoc-rats.txt`
- Modify (only on a gate-finding outcome): `docs/qa-sweep/modoc.md`

**Interfaces:**
- Produces: scenario name `quest-modoc-rats` (Task 4 references it), or a documented gate.

- [ ] **Step 1: Trace the discriminator.** Extract `mcfarrel.int`; disassemble `Node021`, `Node029`, `Node994`, and `Node001` (greeting gates). Identify exactly what separates the `:=3` from the `:=8` write — expected candidates: a rat-count check (LVAR/MVAR/gvar 297, `critter_count`-style external, or per-rat destroy_p_proc increments). Then find the rat script on modgard (`ProcAnalyze --map-objects --map modgard.map`, script column for the rat tiles) and disassemble its `destroy_p_proc` to see what it increments. Write down the full mechanism.
- [ ] **Step 2: Drive the activation.** `probe --goto-map modinn.map --get-global 110 --talk-seq 25088 <chain> --get-global 110 --rng-seed 1` — iterate to the Node994 accept until 110=4. Note: Farrel's greeting also hosts the 106-watch branch (landed golden uses `3,1,1`); the rats branch is a different route — read the option lists.
- [ ] **Step 3: Exterminate on the real path.** Hop to modgard, `--kill` each rat tile (real destroy_p_proc — the sanctioned combat shortcut; kill ALL, including molerat modshit 9901 if Step 1 shows it counts), then re-talk Farrel and take the report option; expect 110=8 (or whatever ≥7 value the honest chain yields). If Step 1 revealed a different mechanism (e.g. map-var checked on map_enter), adapt the verb order to the real path.
- [ ] **Step 4: Bank.** Scenario line (chains from Steps 2-3, `--get-global 110` checkpoints, `--quest-probe`, `--rng-seed 1`), then the standard cycle: `check` → MISSING FIXTURE, `record`, git-status-only-new-fixture guard, `check` → `ok` + ALL PASS, inspect fixture (state/ID lines only).
- [ ] **Step 5: Commit** `qa: Modoc garden-rats golden (gvar 110)` + Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>.
- [ ] **Fallback:** if activation or report proves gated beyond real reach, document the exact gate in `modoc.md` (110 entry) and commit that instead (`qa: Modoc 110 — <gate>, documented`).

### Task 2: Golden `quest-jonny-rescue` (gvar 693)

**Files:**
- Modify: `scripts/quest-golden.sh` (+ fixture `tests/golden-quest/quest-jonny-rescue.txt`), or `docs/qa-sweep/modoc.md` on the gate outcome.

**Interfaces:**
- Consumes: the landed `quest-modoc-ghostfarm` recipe (modmain 20143 chain, line ~143 of quest-golden.sh) as the likely 631-prereq context for reaching the Slag caves amicably.
- Produces: scenario `quest-jonny-rescue` or a documented gate.

- [ ] **Step 1: Trace.** Extract `mcjonny.int`, `mcbaltha.int`, `mcvegeir.int`; disassemble the completing nodes and their gates (`--writes 693` first, then operand-level on each gate proc). Map which completing value (2/3/4) corresponds to which route (e.g. Jonny-sent-home vs escort vs Vegeir-released) and what each requires (631 stage? LVARs? party state? Jonny's follow flag → escort-sim like Klamath's Smiley?). Locate Jonny/Vegeir tiles (`--map-objects` on the cave/ghost-farm maps) and Balthas on modmain.
- [ ] **Step 2: Drive the cheapest honest route end-to-end.** Likely shape: run the ghost-farm chain (reuse the landed 631 recipe's option chains) → enter the caves → Jonny dialogue (Node014 opt3/opt5 routes → :=3) or escort-sim to Balthas (Node003 state-gated :=4). Iterate with `--get-global 693` checkpoints.
- [ ] **Step 3: Bank + commit** as in Task 1 (`qa: Modoc Jonny-rescue golden (gvar 693)`), with the same record/git-status/check guards. **Fallback:** documented gate in `modoc.md` + commit.

### Task 3: 108 verdict + timeboxed 105 retry

**Files:**
- Modify: `docs/qa-sweep/modoc.md` (+ scenario/fixture only if 105 unexpectedly lands)

- [ ] **Step 1: 108.** Re-run `ProcAnalyze --quest-paths 108` (expect `writes=0`), cross-check `--quest-census` pins it as the P124 vanilla gap, and write the verdict into `modoc.md`: no script writes gvar 108 — vanilla content gap, no golden possible, cross-ref P124/census. Remove it from the town's landable pool (denominator note like VC's 529 dual-threshold note).
- [ ] **Step 2: 105 retry — HARD 30-minute timebox.** With the watch (--give 257:1) and the 106 accusation heard (the landed 3,1,1 chain), try `--talk-seq 13490 -` (driver auto mode) and, if that fails, at most a few hand iterations toward mcCornel Node024/Node025 (static opt2/opt3 on Node002). Lands → scenario `quest-cornelius-watch` + fixture + commit. Doesn't → one sentence in `modoc.md` confirming the park verdict with what was tried; no further time.
- [ ] **Step 3: Commit** the doc changes (`qa: Modoc 108 verdict (vanilla writes=0) + 105 retry outcome`).

### Task 4: Docs reconcile + final green

**Files:**
- Modify: `docs/qa-sweep/modoc.md`, `docs/qa-sweep/README.md`

- [ ] **Step 1:** `modoc.md` to truth: header count (2 pre-session + landings), move 631 to DONE with its recipe summary (from the `quest-modoc-ghostfarm` line: modmain 20143, --give 263:10 + 41:5000, chain 1,1,3,1,1,1,1,1,1,1,1,2,1 then re-talk 1, 631 lifecycle), fold in this session's outcomes (110/693 entries or gates, 108 verdict, 105 note), refresh tiles/pids footer.
- [ ] **Step 2:** README table (Modoc row) + total-goldens count from `ls tests/golden-quest | wc -l`.
- [ ] **Step 3:** `scripts/quest-golden.sh check` → ALL PASS; `git status --porcelain` clean of strays.
- [ ] **Step 4: Commit** `docs: Modoc sweep reconciled` + trailer.
