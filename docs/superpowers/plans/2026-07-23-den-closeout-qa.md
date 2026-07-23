# Den Closeout QA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the Den at 7/7 — goldens `quest-becky-still` (gvar 101) and `quest-lara-war` (gvar 454), then docs reconcile.

**Architecture:** Established QA-sweep pattern (fully-worked examples: VC plan 2026-07-21, Modoc plan 2026-07-23): static trace → interactive probe drive → bank scenario+fixture → commit. No engine/harness changes.

**Tech Stack:** viewer harness, ProcAnalyze, int_disasm.py, DatDump, quest-golden.sh.

## Global Constraints

- No `--set-global` faking of quest gvars OR prerequisites — gvar 445's bits (0x20000 drink, 16 reveal) must be set by the real Rebecca/Frankie dialogue; stage advances of 454 by real dcLara/dcMetzge dialogue.
- `--give` sanctioned for item/caps acquire; `--kill` fires the target's real destroy_p_proc (sanctioned); `--use-on <pid>:<tile>` fires use_obj_on_p_proc; other verbs: --set-hour, --pump-ms, --teleport/--escort-pump, --goto-map map[:tile:elev].
- No copyrighted dialogue text (including close paraphrase) in committed files or report files — state/IDs only (tiles, gvars, bits, pids, option indices, node/proc names, msg numbers).
- Scenarios: standard `$CREATE` + trailing `--rng-seed 1`. Suite check must end ALL PASS; after `record`, `git status --porcelain tests/golden-quest/` shows ONLY new fixtures, else STOP.
- One conventional commit per landed unit. Scratch under `$S` = `/tmp/claude-1000/-home-eko-dev-FPOC/78d78d3d-c6c3-4eb5-b358-8abe8172315d/scratchpad` only.
- Fixture note: `quest-item completed=1` is a derived boolean; real thresholds live in quests.txt.

## Ground truth (traced 2026-07-23 + prior session; ordinals 0-based static vs 1-based --talk-seq, drift expected)

| Quest | Facts |
| --- | --- |
| 101 (display≥2, completed≥3) | Writers: DCFranki Node994 `:=1` (via Node002→Node004 opt2→Node010→Node011), Node013/993 `:=2`, Node012/014/015/019/992/988 `:=4` (post-destruction reports); diStill `use_obj_on_p_proc`/`damage_p_proc` `:=3` (the completion). Chain: Rebecca (denbus1 17662) $5-drink → 445|=0x20000; Frankie (denbus2 14716) price branch gated `(445&0x20000)&&!(445&16)&&101==0` → 101:=1; Rebecca reveal → 445|=16; Frankie report → 101:=2; explosive (pid 384 or 20 or 75 — verify which pids diStill's use_obj_on accepts) on diStill denbus1 ELEV1 tile 17062 at 101==2 → :=3. SNAG to resolve first: prior live drive saw Frankie opts 171/172/174, not the gated 173, despite 445=0x20000 — disassemble DCFranki's option builder (Node010/Node011 region) at operand level to find the missed condition (candidate: the drink must be bought BEFORE first talking to Frankie, an LVAR first-visit flag, or a time/map gate). Caps via --give 41:N. |
| 454 (display≥1, completed≥2) | 54 writes. Stage dialogue: dcLara (denbus2 — find tile via --map-objects) intel/accept chain; dcMetzge Node019 `:=3` (permission); further dcLara scout/prep stages (disassemble dcLara --writes 454 for the full ladder); completion `:=11` via destroy_p_proc on dcTyler/DCG1Grd/dcG2Grd or dcLara, with map_enter/map_exit fallbacks (church map = denbus2 area; gang fight at the church). Drive: advance stages honestly through dialogue, then `--kill` the opposing gang members (their real death procs fire the war-outcome writes). Expect the fight on a specific map (locate dcTyler/guards via --map-objects across den maps incl. denbus2/dnchurch if present). |

Helpers: probe() + DatDump/int_disasm commands as in the Modoc plan's Ground-truth section (same $S).

---

### Task 1: Golden `quest-becky-still` (gvar 101)

**Files:** Modify `scripts/quest-golden.sh`; Create `tests/golden-quest/quest-becky-still.txt`; (gate outcome only: `docs/qa-sweep/den.md`).
**Interfaces:** Produces scenario `quest-becky-still` (Task 3 references it).

- [ ] **Step 1:** Extract dcfranki.int (+ dcrebecc.int, distill.int); disassemble DCFranki Node010/Node011/Node994 and the greeting option-builder to resolve the 171/172/174-vs-173 snag — name the exact missing condition. Disassemble diStill use_obj_on_p_proc for the accepted item pids + the 101==2 gate.
- [ ] **Step 2:** Drive the chain with probe(): Rebecca drink (need caps: --give 41:100) → check 445 via the harness output if exposed, else infer from Frankie's options → Frankie 101:=1 → Rebecca reveal → Frankie 101:=2 → --give the accepted explosive pid → --goto-map denbus1.map + --use-on <pid>:17062 (elev 1 — if --use-on needs same-elevation, --teleport to elev 1 first) → 101=3 → optional Frankie report (:=4) for the full lifecycle.
- [ ] **Step 3:** Bank: scenario + standard fail→record→check cycle (ALL PASS 34/34; git-status guard) + fixture inspect (state/ID only, 101 lifecycle 0→1→2→3(→4)).
- [ ] **Step 4:** Commit `qa: Den Becky-still golden (gvar 101)` + Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>. **Fallback:** exact gate documented in den.md + commit.

### Task 2: Golden `quest-lara-war` (gvar 454)

**Files:** Modify `scripts/quest-golden.sh`; Create `tests/golden-quest/quest-lara-war.txt`; (gate outcome only: den.md).
**Interfaces:** Produces scenario `quest-lara-war`.

- [ ] **Step 1:** Extract dclara.int/dcmetzge.int/dctyler.int (+ guard scripts); `--writes 454` each; disassemble the stage ladder (which dialogue nodes advance 454 through 1/2/3/…; what each stage gates) and the destroy_p_proc/map_enter completion writes (what state they require — e.g. war-started stage). Locate all combatant tiles (--map-objects across den maps).
- [ ] **Step 2:** Drive stages honestly: Lara intel/accept chains → Metzger permission (his tile 15278 known from free-vic) → Lara scout/prep → the war trigger; then `--kill` the opposing gang (order per the trace; the last death or map_enter fires :=11). --pump-ms where scripted transitions/timers apply.
- [ ] **Step 3:** Bank: scenario + cycle (ALL PASS 35/35; git-status guard) + fixture inspect (454 ladder → 11).
- [ ] **Step 4:** Commit `qa: Den Lara gang-war golden (gvar 454)` + trailer. **Fallback:** documented gate + commit.

### Task 3: Docs reconcile + final green

**Files:** `docs/qa-sweep/den.md`, `docs/qa-sweep/README.md`.

- [ ] **Step 1:** den.md → 7/7 truth: move 101+454 to DONE with recipe summaries (incl. the resolved Frankie-gate condition and the 454 stage ladder); REMAIN section emptied/replaced with a town-complete note; refresh tiles/pids footer; scrub any quoted dialogue fragments encountered (the older entries predate the scrub standard — e.g. the 371 entry's quoted phrases); fix the stale "DONE (4/7)" header structure.
- [ ] **Step 2:** README: Den row → complete; totals from `ls tests/golden-quest | wc -l`.
- [ ] **Step 3:** `scripts/quest-golden.sh check` → ALL PASS; `git status --porcelain` clean.
- [ ] **Step 4:** Commit `docs: Den sweep reconciled — town complete (7/7)` + trailer.
