# Phase plan — automated quest-completion driver

> **Goal:** a harness mode that drives an original-game quest to completion automatically,
> so the campaign-QA sweep stops being a per-quest manual `--talk-seq` trace. The force
> multiplier for the "months of QA" backlog.
> **Status:** PLAN. Grounded in the dialogue internals verified this session.

## 1. The enabling insight (why this is now tractable)

Manual sweeping was slow for two reasons, both now solved by existing machinery:

1. **Ordinal drift** — static option ordinals (`--quest-paths`) ≠ live option indices, because
   `giq_option` IQ-filtering removes options at runtime, shifting the numbering. So you can't
   replay a static `opt2,opt0` chain blindly.
   **Solution:** the live dialogue already exposes each option's **target procedure index** —
   `DialogSession.OptionProcedures` (ScriptHost.cs:1309; the comment notes the dynamic census
   already DFS's these). And `QuestPathScan` builds the option/call graph by **procedure index**
   (`OptionEdge.FromProc/ToProc`), with `FindPath` computing the route. So we match live options
   to the static route **by target proc index**, not ordinal — drift-proof.

2. **Item gates** — completions gate on `obj_carrying_pid_obj(pid)`.
   **Solution:** scan the completing node's bytecode for those pids and `--give` them up front
   (the acquisition shortcut we've done by hand — locket 252, meal 468, watch 257, etc.).

## 2. The algorithm

`--quest-drive <gvar> [<npcTile>]` on a loaded map:

1. **Resolve the completing script + node.** Reuse `ProcAnalyze --quest-paths` logic
   (`QuestPathScan`): for the quest gvar, find the script whose node writes `gvar >= completed`,
   and that node's proc index. Prefer a script present on the current map.
2. **Find the NPC.** If `npcTile` given, use it; else map the completing script name → a critter
   tile via the map's scripted objects (the `--map-objects` resolution, in-process).
3. **Pre-give items.** Scan the completing node (+ its callers on the route) for
   `obj_carrying_pid_obj` operands → `--give <pid>:N` (N from any nearby count constant, else 1).
   Also give a caps float (`41`) for buy/bribe branches.
4. **Compute the route.** `FindPath(talk_p_proc → completingNode)` → an ordered node-index list.
   Build the set of "route nodes" + each node's remaining-distance-to-completion.
5. **Drive the dialogue (guided greedy).** Start dialogue. Each round:
   - if `gvar >= completed` → **done**;
   - else among live options, pick the one whose `OptionProcedure` is a route node with the
     smallest remaining distance (steps toward completion; `=call=>` edges advance with no pick
     and are handled naturally — they change the reply without an option);
   - if no option is on the route, fall back: pick the lowest-index non-terminating option
     (progress heuristic), bounded by a max-rounds guard;
   - record each pick.
6. **Report** (state-only, golden-safe): `quest-drive: gvar=<n> start=<v0> end=<v> completed=<b>
   picks=[i,j,...] npc=<tile>`.

## 3. Scope — what the first cut does and does not

**In (MVP):** single-NPC dialogue-completable quests with item/caps auto-give — the bulk of the
delivery/return/dialogue tier (watch 106, mom-meal 450, lydia 497, valerie 493, torr-brahmin
182, …). The driver should **reproduce the known goldens automatically**.

**Deferred (follow-ups, documented):**
- **Cross-NPC / cross-map chains** (activate at A, complete at B: fred-money, smith-plow,
  rescue-joshua). Handle later by running the driver per-script in the route's NPC order.
- **Haggling / value branches** (Fred demand-full, Harry $800, Barkus $1000) — the completing
  option may not be the greedy-nearest; add a "try each terminal option, keep the one that
  advances the gvar" fallback.
- **Non-dialog completions** (`--kill`, `use_obj_on`) — orthogonal; keep the existing verbs.

## 4. Implementation steps (each golden-verified)

1. **Formats: route helper.** Add `QuestPathScan.FindPathProcs(...)` returning the node-index
   list (the graph already has it; `FindPath` renders names — expose the ints). Unit-test on a
   hermetic .int or a GameDataFact.
2. **Formats: completion resolver.** A helper that, for a gvar, returns
   `(scriptName, completingProcIndex, requiredItemPids[])` from `QuestPathScan` +
   an `obj_carrying` operand scan. Unit-test.
3. **Harness: `--quest-drive`.** Wire steps 2–6 in `ViewerGame.Harness.cs` using the existing
   dialogue session (`TalkTo` + `ChooseDialogOption` + `OptionProcedures`) and `--give`. STATE-
   only output line (new `quest-drive:` prefix — filtered out of every golden).
4. **Validate.** Run `--quest-drive` for each single-NPC golden gvar; confirm it reaches the same
   completed value. Run all 5 golden suites — must stay byte-identical (additive mode).

## 5. Risks

- **Greedy dead-ends** (a route needs a non-nearest pick, e.g. buy-a-drink-first). Mitigation:
  the max-rounds guard + reporting where it stuck; add the terminal-try fallback (step §3) if a
  known golden fails to auto-complete.
- **Multi-write gvars** (quest shares a gvar across sub-tasks, e.g. 371 desc 204/205): resolve to
  the node reaching the wanted `completed` threshold, not just any writer.
- **Dialogue re-entrancy / state** — the driver runs one forward pass (no backtracking), so no
  snapshot/restore needed; side-effect-free by construction.

## 6. Payoff

Converts each landable quest from ~15–60 min manual tracing to a single `--quest-drive <gvar>`
call, and makes the untouched towns (New Reno 15, NCR 12, …) a batch sweep. Even partial success
(the driver auto-completes the easy tier, flags the hard ones with where they stuck) is a large
multiplier — and the "where it stuck" report is itself the scoping the manual traces produce.

## 7. First action

Implement step 1 (`FindPathProcs`) + step 2 (the completion resolver) in Formats with unit
tests, then the harness verb, validating against `--quest-drive 106` (the watch — the simplest
single-NPC item-return golden) first.

## 8. Implementation results — WORKS for the single-NPC tier

Built: `QuestPathScan.FindPathProcs` (proc-index route) + `ItemChecks` (obj_carrying pids); the
`--quest-drive <gvar>` harness (`DriveQuest`/`DriveOne`). 953 Formats tests pass. Final algorithm
generalised beyond the plan: **multi-pass over ALL map-NPCs that write the gvar** (not just
completers — quests activate at A, complete at B), each pass routing toward the **nearest write
that advances the gvar** (value > current) and matching live options to that route by target
proc index. Item pids pre-given from every gvar-writer's route; caps floated.

**Validated (auto-completes, matching the manual goldens):**
- `--quest-drive 106` → mcFarrel, item 257, picks 3,1,1,4 → **completed** (= quest-modoc-watch)
- `--quest-drive 497` → VCDwnBar, items 124/125 → **completed** (= quest-lydia-booze)
- `--quest-drive 493` → VCMainWk, items 384/75 → **completed** (= quest-valerie-tools)
- `--quest-drive 551` → DCAnna, item 252 → **completed** (= quest-anna-locket)
- `--quest-drive 182` → correctly **activates** 0→1 (full completion is the klagraz event, not
  dialogue); 371/550 likewise activate (completion is cross-NPC / item-gated). Correct behaviour.

Notably it auto-picks the *reachable* completer — e.g. 106 completes at Farrel and skips
Cornelius's dementia-maze (no resolvable route) on its own.

**Known limits (the honest edge — follow-ups):**
- **Single-map only** — cross-map chains (mom-meal Mom@denbus2→Smitty@denbus1, smith-plow,
  rescue-joshua) need the driver to hop maps; today it drives NPCs on the loaded map.
- **Value-branch negotiations** — Fred demand-full / Harry $800 / Barkus $1000 aren't the
  greedy-nearest option; needs a "try each terminal option, keep the advancing one" fallback.
- **Off-route/computed-dispatch dialogue** — mazes whose options resolve via non-const targets
  (Cornelius) can't be routed statically; the driver bails cleanly rather than loop.

Net: the driver **auto-clears the delivery/item-return tier** (the bulk of the easy quests) and
correctly activates the rest — exactly the force-multiplier intended. It reports `start/end/
completed/at/items/steps` so a non-completing run scopes the remaining manual work for free.

## 9. Batch census (#1), golden emitter (#2), cross-map (#3) — DONE

Three follow-ups landed, turning the driver into a pipeline:

- **`--quest-drive-all` (#1 batch census):** runs the driver for every quest a current-map NPC
  writes, printing a `completed / activated / stuck` matrix + per-quest recipe. Example on
  vctydwtn: `completed=2 activated=1 stuck=3 of 6` — auto-lands 493/497, flags 82 (powerplant),
  321 (cross-town), 459 (cross-map) as the work. One pass per map = a census.
- **`quest-drive-cmd:` (#2 golden emitter):** every completing/advancing run prints a runnable
  harness line — `--goto-map … --give … --talk-seq tile picks …`. Validated: the emitted 497 and
  450 recipes reproduce fresh (gvar reaches completion). Closes driver → recipe → golden; the
  operator verifies + commits, no hand-tracing.
- **`--quest-drive <gvar> <mapCsv>` (#3 cross-map):** hops the town's maps in rounds, driving
  gvar-writers on each until completion — handles activate-at-A-complete-at-B chains. Validated:
  **450 (mom-meal) completes across denbus2→denbus1** (Mom activates, Smitty delivers); the
  emitted cross-map recipe hops `--goto-map` and reproduces fresh. (459 hops correctly but stalls
  on Barkus's $1000 bribe — the value-branch limit below, not a cross-map failure.)

**#4 value-branch tie-breaking (partial):** `DriveOne` now records rounds with a TIE (≥2 options
to the same best route node — the haggle/bribe menus) and, when the greedy pass doesn't advance,
retries picking each tied option, keeping the one that moves the gvar. Sound + additive
(regression-free: 106/497/493/551 and cross-map 450 still auto-complete). BUT it doesn't crack
the marquee negotiations (371 Fred / 80 Harry / 459 Barkus) for two deeper reasons found in
testing: (a) some value-branches advance a PREREQUISITE gvar (Fred's demand-full sets the 446
task bit, not the quest gvar 371 — the driver watches 371 and sees no movement), and (b) the
menus sit behind DEEP navigation the greedy doesn't always reach (Barkus: building→center→
Joshua→negotiate→tier). Cracking these needs the driver to (a) track the completing node's
prerequisite gvars as progress signals, and (b) route through multi-level sub-menus — a larger
generalization, deferred. The census still flags these correctly as activated/stuck.

Additive throughout — all 16 quest goldens byte-identical, 953 Formats tests pass.
