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
   `DialogSession.OptionProcedures` (ScriptHost.cs:1342; the comment notes the dynamic census
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

## 10. Prerequisite-gvar generalization — ATTEMPTED, reverted (bit-level is the real fix)

Tried to crack the marquee negotiations (371/80/459) by broadening the driver's progress signal
from just the quest gvar to its PREREQUISITE gvars — a gvar the completing node READS that another
quest-NPC WRITES (Fred sets the 446 task bit; Rebecca's turn-in reads it). Built the supporting
static analysis: `QuestPathScan.GvarReads` (0x80C5 reads) and precise `RmwWrites` (the
`get_global(G) → bitwise(0x8040/41) → set_global` read-modify-write a task bit compiles to — a
const-VALUE-less write `ConstWrite` can't see). Threaded a `progress` set through the driver.

**Result: reverted.** It works mechanically (with the correct bitwise opcodes it DID detect 446
and make Fred a candidate) but hits a FUNDAMENTAL over-inclusion: the task-bit gvars (445/446/452)
are **shared bitfields touched by dozens of NPCs** across many quests, so "gvar the completer
reads that another NPC writes" pulls in ~37 candidates (every Den addict) and regresses 371 to
end=0. The delivery-tier completers stayed green, but the net was a regression on the target
quests, so it's not shippable.

**The real fix is BIT-LEVEL, not gvar-level:** track WHICH bit of 446 the completer checks
(`446 & 0x8000`) vs which bit Fred sets (`446 |= 0x8000`), and treat as a prerequisite only the
NPC that sets the SAME bit. That needs mask-tracking through the RMW/AND sequences and matching
masks across scripts — a materially larger analysis. Deferred as its own task; the driver remains
at the known-good #1–#4 (auto-completes the delivery/item-return tier, correctly activates the
rest, flags negotiations as the work). The census (§9) still scopes these precisely.

## 11. Full-map harvest — DONE (4 new goldens, +the false-positive guard hardened)

Ran the driver across **all 155 maps** (`scripts/quest-harvest.sh`), not just the 22 town hubs.
Two phases: DISCOVER (`--quest-drive-all` per map → every gvar a map's NPCs write + status) then
VERIFY (fresh single `--quest-drive <gvar>` per non-golden candidate). Results in
`docs/qa-sweep/harvest.md`: 56 quest-bearing maps, 233 driver runs, batch completed=20/
activated=30/stuck=183.

**The key hardening:** the driver's OWN `completed=1` is not trustworthy on value-branch quests —
tie-breaking (#4) mutates persistent gvar state while EXPLORING terminal options, so the driver
sees completion but the recorded picks don't reproduce it. So VERIFY doesn't trust the driver: it
extracts the emitted recipe and **replays it standalone** (exactly what a golden does), accepting
only if the gvar advances to the driver-reported value in a clean process. This caught 380
(reddown) again — driver end=3, replay 0->0 — plus two degenerate empty-pick recipes.

**4 new recipe-verified goldens** (surfaces the hub census never reached), added to
`scripts/quest-golden.sh` (suite now 24, all byte-identical):
- **195** GVAR_NCR_VORTIS_QUEST_STATE (ncrent), **332** GVAR_REDDING_EXCAVATOR_CHIP (redment),
  **485** GVAR_NCR_ENLONE_LETTER_QST (sfelronb), **367** GVAR_SAN_FRAN_SPLEEN (sftanker+dnslvrun).

**Flagged for manual review (not forced):** 481 GVAR_NCR_BRAHMN_QST + 488 GVAR_V13_GORIS_QST are
real quests whose completer writes the gvar on node-entry with zero option picks — the recipe
emitter produced an empty `--talk-seq <tile>` that doesn't replay. They need a first-talk trigger
or a valid pick sequence; correctly excluded by the replay guard rather than shipped broken.

### §11a — the two flagged quests RESOLVED (prereq-gated, not goldenable; emitter hardened)

Disassembled both completers (`tools/int_disasm.py`). Both write via `talk_p_proc` (fires on
dialogue OPEN, hence "zero picks"), and both are **prerequisite-gated**, not emitter bugs:
- **488 (Goris):** `ocGoris.talk_p_proc` writes `488:=2` only inside the *Goris-joins-the-party*
  branch (right after `party_add` + the join cutscene) and only `if global(488)==1`. Recruiting
  Goris requires the Vault 13 deathclaw storyline state — deep prior campaign state.
- **481 (NCR brahmin):** `scdrvpay.Node006` writes `481:=2` unconditionally *once reached*, but
  `talk_p_proc` only calls Node006 after a reputation / prior brahmin-drive gate; a clean talk
  never reaches it (verified 0->0).

Neither completes from a clean start, and the standing rule forbids `--set-global`-faking the
prerequisites in a golden — so **no new golden**; the replay guard's exclusion was correct. The
one code fix: `DriveCommand` now emits the `-` sentinel for a zero-pick step (was an empty pick
arg → `int.Parse("")` throws → bogus `?->?`); the recipe is now well-formed and replays to a
clean `0->0`, correctly classified as a false-positive rather than an ambiguous crash. These two
belong to the future "real campaign-state fixtures" track, not the clean-start golden suite.

## 12. Bit-level prerequisite driver — DONE (cracks the negotiation-prereq tier; §10's real fix)

The bit-level analysis §10 called for, now built and proven end-to-end on 371 (Fred→Rebecca).

**Static analysis** (`QuestPathScan`): two new captures, hermetic + real-data tested.
- `BitCheck(proc, gvar, mask)` — `global(G) & MASK` (push G, get_global, push MASK, bitwise_and).
- `BitSet(proc, gvar, mask)` — `global(G) |= MASK` RMW (push G, push G, get_global, push MASK,
  bitwise_or, set_global). The mask is captured exactly.
- New `ProcAnalyze --bit-scan [gvar]`: dumps every (gvar,mask) with a CHECK and a SET across all
  scripts. The ground truth: `gvar=446 mask=0x8000 set-by=[dcFred] checked-by=[dcRebecc]` — a clean
  1:1, where gvar-level detection saw 37 NPCs on the shared 446 bitfield (§10). The mask disambiguates.

**Driver integration** (`RunDriver`): route-generic via a `DriveGoal` (gvar goal | bit goal — the
gvar path stays byte-identical). For each completer, collect the foreign `(gvar,mask)` bit-CHECKS
on the route to its write, find map-NPCs that SET that exact bit → prerequisites, drive them.
Three things made it reproducible (each found by a concrete failure):
1. **Activation cap.** While a prereq bit is unset, cap the writer goal at the DISPLAY (activation)
   threshold. Without it the completer jumps to an UNGATED refuse/abort branch (Rebecca's Node988
   := 2) that "completes" the gvar via tie-retry exploration but doesn't replay — a false positive.
   Capping lets the completer only activate, opening the prereq's own gate (Fred's demand-full needs
   the quest live); once bits are set we uncap and complete via the real gated branch (Node011).
2. **Dialogue-reachability filter.** Drop prereqs whose bit-set is in a non-dialogue proc
   (`destroy_p_proc` etc. — Fred's 445 & 0x400 fires on his death). An undriveable prereq would
   jam prereqsPending() forever, keeping the completer capped at activation.
3. **Order = writers then prereqs**, matching the manual Rebecca→Fred→Rebecca sequence.

**Result:** `--quest-drive 371` now auto-drives `17662(activate) → 25479(Fred) → 17662(complete)`,
REPLAY-VERIFIED 0→2 (the harvest guard). Locked as golden `quest-rebecca-prereq` (suite = 25, all
byte-identical). No regression: 106/497/493/551/393 still auto-complete; 953 Formats tests pass
(+3 new bit-level tests). The negotiation-prerequisite tier the gvar-level attempt couldn't reach
is now driveable. Remaining stuck-tier (New Reno deep sub-menus, non-bit prereqs) is separate work.

## 13. Investigation-chain driver (New Reno mysteries) — GROUNDED, checkpoint (not built)

Grounded 286 (GVAR_NEW_RENO_WRIGHT_MYSTERY) — the archetype "investigation" quest — to size the
work. The mechanism IS the P137 bit-prerequisite pattern, but at a scale that needs several new
subsystems, with no incremental payoff.

**What 286 actually is:** a 37-write murder mystery. The completer (ncOrvill) accuses the murderer
via `talk_p_proc → Node018 → Node988 := 2`, and each accuse option is GATED on evidence bits in the
shared New Reno flag fields — `314` (GVAR_NEW_RENO_FLAG_1) and `345` (FLAG_2). Each bit has a
specific setter (`314 & 0x20` ← ncRenesc, `345 & 0x2` ← ncJules, `345 & 0x4` ← ncJimmyJ, …) — a
clean P137-style relationship, so the shared-bitfield §10 wall does NOT apply (the mask disambiguates).

**Why the current P137 driver can't do it (measured with a QDTRACE):**
- On newr2, prereq gathering found `neededBits=[314&0x4000, 314&0x40]` but `prereqsOnMap=[]` — **the
  evidence-bit setters are on OTHER New Reno maps** (the prereq loop only searches the current map's
  critters). Cross-map prereq resolution is missing.
- Only 2 of the many accuse-gate bits were found: `FindPathProcs` returns ONE shortest path to ONE
  completing write, so BitChecks on the rest of the gate (and other completers) are missed. Complete
  (full-subtree) bit gathering is missing.
- Reaching `286 := 1` (accept the investigation) is a 7-hop `Node004 → … → Node015` dialogue path.
- Which suspect to accuse is quest-specific (depends on which evidence you gathered) — not a clean
  mechanical driver decision.

**Honest scope:** completing an investigation quest needs the confluence of (1) full-subtree
multi-bit prereq gathering, (2) cross-map prereq resolution (drive bit-setters on other maps, then
return), (3) deep-activation navigation, and (4) quest-specific accusation logic. Crucially there is
**no incremental payoff** — all four are required before ANY investigation quest completes, so it
can't be delivered in validated slices. That makes it a large, multi-session build with real
convergence risk, for a handful of quests — several of which are ALSO combat-completable
(`damage_p_proc := 2` on the suspects, i.e. the B3/kill path once the culprit is known).

**Recommendation / checkpoint:** do NOT sink into a speculative build. The quest-driver is at a
strong, honest state — it auto-completes the delivery/item-return tier, the single-bit-prerequisite
negotiations (P137), and the unconditional-kill quests (P138/B3), and correctly flags the rest. The
investigation tier is the remaining frontier and is genuinely large; take it on only as a dedicated
multi-session effort with eyes open, or bank here.
