# B4 Gecko-Powerplant Arc Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Drive the Gecko-powerplant / VC-citizenship arc end-to-end and land the goldens it gates: `quest-gecko-powerplant` (82), `quest-lynette-holodisk` (89), `quest-stark-scout` (529, with the sanctioned rule-105 hook), plus an 85 probe and docs.

**Architecture:** Established QA-sweep pattern (worked examples: the VC/Modoc/Den plans in docs/superpowers/plans/). One new wrinkle: Task 3 lands a tiny engine hook (metarule3 rule 105) together with its golden, per the P138 probe's outcome-2 rule. Task 2/3/4 recipes REUSE Task 1's landed chain as their prefix (long recipes are fine — quest-torr-rescue precedent).

**Tech Stack:** viewer harness, ProcAnalyze (incl. --bit-scan), int_disasm.py, DatDump, quest-golden.sh; IntVm.cs for the one hook.

## Global Constraints

- No `--set-global` on quest gvars or prereqs — 82/88/79 move only via real dialogue/object use. `--give` (items/caps) and real chem use sanctioned. `--kill` fires real destroy_p_proc (avoid here: 79:=6 damage_p_proc = town hostility — do NOT hit VC citizens).
- No dialogue text incl. close paraphrase in committed files or reports — state/IDs only. Check giq_option iq operands when an expected option is missing (Den/Modoc lesson). Run `--bit-scan` before declaring any bit "has no setter" (Den lesson).
- Scenarios: `$CREATE` + trailing `--rng-seed 1`. After `record`: only intended fixtures changed. One conventional commit per landed unit. Scratch under `$S` = `/tmp/claude-1000/-home-eko-dev-FPOC/78d78d3d-c6c3-4eb5-b358-8abe8172315d/scratchpad` (all 142 vc/vi/gc/gs .int files are ALREADY extracted at `$S/vcint/`).
- Engine code: ONLY Task 3's rule-105 branch in IntVm's 0x80E1 handler, following the preserved sketch in `docs/qa-sweep/vaultcity.md`'s 529 verdict block (~8 lines, KillCountProvider-style provider pattern); its test = the 529 golden + the full suite staying green. Nothing else.
- Poll long suite checks to completion in-turn (blocking loop) — never end a turn waiting on a Monitor.

## Ground truth (swept 2026-07-24; ordinals 0-based static / 1-based --talk-seq, drift expected)

| Gvar | Writers (script Node := value @ trigger) |
| --- | --- |
| 82 (display≥2, completed≥8) | VCLynett Node047f/049a/049b :=3 (via Node053 opt6→Node034→Node044→Node047); GCHarold Node048 :=5; GCGordon Node912/Node936 :=5; GCBrain Node030 :=5; VCMClure Node045/045a :=6 (via Node006 opt2→Node008 opt7→Node042→Node043); VCRandal Node024 :=7 (via Node014 opt5→Node023); GeckPwpl map_exit :=8/:=9; GSValve repair_it :=9; GCRobot checkdone :=9; GCFestus Node30a :=9, Node992 :=12; VCMClure Node059a :=13 (via Node006 opt2→Node008 opt11→Node059); VICenCom Node032 :=13; GsTerm use_obj_on :=15. |
| 88 | VCLynett Node114 :=5, Node116 :=6, Node130 :=7; VCRandal Node026 :=8. NO other writers exist (all-script sweep) — the old "stages 1-4" hypothesis is dead. |
| 79 | vcskeeve Node024 :=1; vcgatgrd Node023/027 :=3, Node024/026 :=2; :=4 tier: VCLynett Node077/076a/076b, vcmclure Node046, vcgreg Node030, VCChet Node001c; **:=5: VCLynett Node132 or VCChet Node001b**; :=6 = hostile (damage_p_proc in ~50 scripts — never trigger). |
| 89 (display≥1, completed≥3) | VCLynett Node123/126 :=1, Node119a :=2, Node129 :=4; SCWestin getDisk :=3 gated obj_carrying pid 447 (ncr3 tile 17892). Lynette reveal option: msg 394, gated 88==5 && carrying 447. |
| 529 (two quests.txt rows) | VCStark (vctycocl; find tile via --map-objects) Node054/055/064 each AND-chain the same 8 metarule3(105,x,y,0) subtile checks — coords (1224,171),(1274,172),(1323,173),(1224,223),(1324,225),(1224,274),(1275,274),(1325,273); citizenship gate gvar79==5. Rule-105 hook currently UNWIRED in IntVm case 0x80E1 (has 100/103/110); sketch + fo2ce refs in vaultcity.md's 529 verdict block; WorldmapFog.StateAt / MarkRadiusVisited exist engine-side. |
| 85 | VCDrTroy (vctyvlt 13084); jet pid 259. Prior finding: cold-boot inert. Gate unknown — trace fresh (vcdrtroy.int is in $S/vcint/). |

Tiles known: Lynette vctycocl 17100; McClure/Randal/Stark/Chet: locate via `ProcAnalyze --map-objects` (VC maps vctydwtn/vctyctyd/vctycocl/vctyvlt; Chet's placement was previously unfound on the 4 static maps — but his script writes 79, so check ALL maps' object lists and runtime, incl. courtyard/village maps vcvillge if present). Gecko maps: geckset/geckpwpl/geckjunk/gecktunl (Harold/Gordon/Festus/Brain/valve/robot/terminal via --map-objects).

Helpers: probe() + extraction as in prior plans (viewer build first; DatDump Release dll at tools/DatDump/bin/Release/net10.0/DatDump.dll for fast batch extraction).

---

### Task 1: Golden `quest-gecko-powerplant` (82) + the citizenship grants

**Files:** Modify `scripts/quest-golden.sh`; Create `tests/golden-quest/quest-gecko-powerplant.txt`; (gate outcome: docs/qa-sweep/gecko.md).
**Interfaces:** Produces scenario `quest-gecko-powerplant` AND its recipe's option chains (documented in the report) as the PREFIX Tasks 2-4 reuse. Checkpoint `--get-global 82/88/79` values at each stage go in the report for downstream tasks.

- [ ] **Step 1: Trace the gates.** Disassemble (all files in $S/vcint/): VCLynett Node053's option builder + Node034/044/047 (the 82:=3 route — what unlocks opt6? day pass? 79 tier?), Node077/Node132 gates (what grants :=4/:=5), Node114's gate (the 88:=5 write — which 82 value does it require?); vcgatgrd Node023-027 (the gate day-pass/citizenship-test routes); GCHarold Node048 / GCGordon / GCBrain (the :=5 knowledge tier); VCMClure Node042-045 + Node059 region (the :=6 and :=13 routes + gates); VCRandal Node014/023/024 (:=7) + Node026 (88:=8 — avoid or note); GSValve repair_it + GCRobot checkdone (what item/state the repair needs — expected: super repair kit 308? parts?); GsTerm use_obj_on (what item → :=15). Map every gate to a driveable action. Locate all NPC tiles (--map-objects on the VC + Gecko maps).
- [ ] **Step 2: Drive the ladder** with dense `--get-global 82` (+88/79 at the VC ends) checkpoints: VC intro (gate guard day-pass tier if Lynette's audience needs it) → Lynette 82:=3 → Gecko (Harold/Gordon/Brain) :=5 → McClure :=6 → Randal :=7 → the plant fix (:=8/9 via the traced mechanism — item use, robot, valve) → IF tractable, the optimization tier (:=12/13) → back to Lynette: capture 88:=5 (Node114) and the 79 grants (Node077 :=4, Node132 :=5) in the same run. If the optimization tier is what Node114 needs, prefer it; if repair (:=8/9) suffices, the shorter route wins.
- [ ] **Step 3: Bank** `quest-gecko-powerplant` (the recipe through 82-completion + the Lynette/citizenship grants, with 88/79 checkpoints in the fixture) via the standard fail→record→check cycle (36/36 ALL PASS; git-status guard; fixture state/ID).
- [ ] **Step 4: Commit** `qa: B4 arc golden — Gecko powerplant (gvar 82) + VC citizenship grants` + Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>. **Fallback:** if a stage is genuinely gated beyond reach, document the exact gate in gecko.md + vaultcity.md and commit — but exhaust giq_option/--bit-scan/order-dependence checks first (three towns' worth of lessons say the gate is findable).

### Task 2: Golden `quest-lynette-holodisk` (89)

**Files:** Modify `scripts/quest-golden.sh`; Create `tests/golden-quest/quest-lynette-holodisk.txt`.
**Interfaces:** Consumes Task 1's chain (from its report) as the recipe prefix. Produces scenario `quest-lynette-holodisk`.

- [ ] **Step 1:** From Task 1's end-state (88==5), disassemble VCLynett's msg-394 route ordinals fresh (options shift with the new state), and SCWestin Node001 opt7→Node017→getDisk (ncr3 17892).
- [ ] **Step 2:** Drive: Task-1 prefix → Lynette reveal branch (89:=1/2 en route if her errand nodes fire) → --give 447:1 (+337:1 if her disk isn't script-granted) → ncr3 Westin → 89:=3; then Lynette return (Node129 :=4) if reachable.
- [ ] **Step 3:** Bank + cycle (37/37) + commit `qa: B4 arc golden — Lynette holodisk to Westin (gvar 89)` + trailer. Fallback: precise gate doc.

### Task 3: Rule-105 hook + golden `quest-stark-scout` (529)

**Files:** Modify `src/Hexwaste.Viewer/.../IntVm.cs` (the 0x80E1 metarule3 case ONLY — follow the sketch in vaultcity.md's 529 block; find the exact file/lines by grepping `case 0x80E1` / rules 100/103/110); Modify `scripts/quest-golden.sh`; Create `tests/golden-quest/quest-stark-scout.txt`.
**Interfaces:** Consumes Task 1's chain through 79==5 (Lynette Node132 route). Produces the hook + scenario `quest-stark-scout`.

- [ ] **Step 1:** Wire rule 105 (WM_SUBTILE_STATE) per the sketch: metarule3(105, x, y, 0) → the worldmap subtile visited-state via the existing WorldmapFog query (match the provider pattern of rules 100/103/110 in the same switch). Build clean.
- [ ] **Step 2:** Regression guard BEFORE the new golden: `scripts/quest-golden.sh check` must stay ALL PASS (the hook must not perturb any existing fixture — rule 105 was previously unreachable, so byte-identical is expected; any diff = STOP and investigate).
- [ ] **Step 3:** Drive 529: Task-1 prefix (79==5) → Stark accept (find his tile via --map-objects vctycocl) → visit the 8 subtiles by worldmap travel (the harness's travel verbs — check what exists: --travel? --goto-map to NCR entrance maps marks visited radius via MarkRadiusVisited; consult WorldmapTravel call sites) + enter NCR → Stark report → 529 completed per its quests.txt thresholds.
- [ ] **Step 4:** Bank + cycle (38/38) + commit `feat: wire metarule3 rule 105 (WM_SUBTILE_STATE) + the 529 Stark-scout golden` + trailer. Fallback: if the travel verbs can't legitimately mark the subtiles, STOP the golden, keep the hook UNCOMMITTED (outcome-2 discipline: no golden → no hook), document in vaultcity.md.

### Task 4: 85 probe (timeboxed 30 min) + docs reconcile + final green

**Files:** `docs/qa-sweep/vaultcity.md`, `docs/qa-sweep/gecko.md`, `docs/qa-sweep/README.md`; (+ scenario/fixture if 85 lands).

- [ ] **Step 1:** With Task 1's arc state, disassemble vcdrtroy.int's jet-branch gates (in $S/vcint/); one timeboxed drive attempt if the gate is now satisfiable; land `quest-troy-jet` or write the precise gate into vaultcity.md.
- [ ] **Step 2:** Docs: vaultcity.md — 82/89/529/85 entries to their outcomes, the B4-arc section updated (the arc is now a recipe, not a blocker; keep the Chet/alternate notes current per Task 1 findings); gecko.md — 82 landed (Gecko's first golden) + header; README — VC/Gecko rows + total from `ls tests/golden-quest | wc -l`.
- [ ] **Step 3:** Full `scripts/quest-golden.sh check` polled to completion → ALL PASS; clean tree.
- [ ] **Step 4:** Commit `docs: B4 arc landed — VC/Gecko sweep reconciled` + trailer.
