# The stale walker — membership-vs-liveness in `_npcWalkers` (F21) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A critter whose walk has finished can walk again. Today a finished walker stays in
`_npcWalkers` and `StartNpcWalk`'s guard tests dictionary membership, so the critter is frozen for the
rest of the run while the engine keeps logging movement it never performs.

**Architecture:** One guard change in `ViewerGame.StartNpcWalk` (behavioural, moves fixtures), then one
decoupling of the walker prune from `UpdateAmbientLife` (must move nothing). Proof is a new harness
probe, because there is no viewer unit-test project.

**Tech Stack:** C# / .NET 10 (`net10.0`), MonoGame DesktopGL, xUnit. Reference:
`reference/fallout2-ce` at `e97087b` (gitignored clone).

## Global Constraints

- **Never copy, embed, or commit game assets.** `.gitignore` excludes `*.dat`, `*.map`, `*.frm`,
  `*.pal`, `game-data/`.
- **Port from `reference/fallout2-ce`, never guess.** Every behavioural change carries a comment
  citing its source. If a detail cannot be confirmed, **stop and ask**.
- **`alexbatalov e97087b` is authoritative for vanilla.** `community/main` is a bug-fix candidate
  source only, cited as `(community fix #NNN)`.
- **VERIFY EVERY LINE NUMBER YOU CITE**, in the reference *and* in Hexwaste, **on this branch**. Recent
  branches produced three wrong reference citations and one set of stale Hexwaste line numbers read on
  a different branch.
- **No new dependencies.** `src/Hexwaste.Formats` stays free of MonoGame references.
- **Golden-net discipline:** the scripts run the *prebuilt* binary. Never run two nets concurrently,
  never background one, **never build while one runs**. The scripts print no fixture count — report
  exactly what they print. (The controller violated this once already this session; two overlapping
  runs collide on the encounter suite's fixed `/tmp` save paths.)
- Golden nets and the probe need a real display and game data (`FALLOUT2_DIR`, default `./game-data`).

---

## Why the proof is a probe, not a unit test

**There is no viewer test project** — `tests/` contains only `Hexwaste.Formats.Tests`, and
`StartNpcWalk` is a private member of `ViewerGame` in the MonoGame project. Adding a viewer test
project would drag MonoGame and a `GraphicsDevice` into the test rig; that is a structural change, not
a step in this fix.

The codebase's established idiom for exactly this situation is a **headless harness probe** — there are
57 `probe` references in `Program.cs` (`--blocked-probe`, `--npc-walk`, `--light-probe`, …), and the
project's own P114 lesson is that *a byte-identical golden can hide an inert feature*, so viewer-side
behaviour is proven live against real game data.

The existing `--npc-walk <hex> <target>` (`ViewerGame.Harness.cs:855-872`) is close but insufficient:
it starts one walk and prints `started=0|1`. Two consecutive `--npc-walk` actions would **not**
demonstrate this bug, because the first walk has not finished when the second is attempted, so a
refusal is legitimate. The bug needs a walk **pumped to completion** and then a second attempt.

---

## Reference and Hexwaste facts

- `animationIsBusy` (`animation.cc:581`) iterates only sequences actually in use
  (`animationSequence->field_0 != -1000`) and reports busy only for a live, non-callback,
  non-idle-stand animation. **The reference's busy test is liveness-based.** That is the citation this
  fix carries; `_npcWalkers` itself is Hexwaste's own structure with no reference counterpart.
- `StartNpcWalk` is at `ViewerGame.cs:3326`; the offending guard is at `:3328`; the walker is stored by
  indexer assignment at `:3383` (`_npcWalkers[npc] = walker`), so replacing a stale entry is clean.
- `DudeController.Moving => _rotations is not null` (`DudeController.cs:33`) — a finished walker is
  present but not `Moving`.
- The prune is `ViewerGame.cs:3262-3272`, inside `UpdateAmbientLife` (`:3257`), *after* its early
  return `if (DisableAmbientLife || _worldmapOpen) return;` (`:3259-3260`).
- `UpdateAmbientLife` is called from `ViewerGame.cs:2817` (the real `Update`) and
  `ViewerGame.Harness.cs:590` and `:2440` — **not** from the `--fight` autoplay loop
  (`ViewerGame.Harness.cs:2035-2038`) nor the brawl-watch loop (`:205-208`), both of which pump
  `walker.Update(...)` over `_npcWalkers.Values` directly.

---

## Task 1: The probe, then the guard

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs` (the guard), `src/Hexwaste.Viewer/Program.cs` (flag
  parsing), `src/Hexwaste.Viewer/ViewerGame.Harness.cs` (probe handler), and the `StartupAction` record
  list (`ViewerGame.cs:873` area)
- Re-record (expected, set unknown): `tests/golden-combat/` and possibly `tests/golden-encounter/`

**Interfaces:**
- Produces: a new startup action and CLI flag, `--walker-restart-probe <hex> <target1> <target2>`.

- [ ] **Step 1: Add the probe — BEFORE changing the guard**

Add a `StartupAction.WalkerRestartProbe(int Hex, int Target1, int Target2)` record beside the existing
ones (`ViewerGame.cs:873` area), a `case "--walker-restart-probe" when i + 3 < args.Length:` in
`Program.cs` beside `--npc-walk` (`Program.cs:525`), and a handler in `ViewerGame.Harness.cs` modelled
on the `StartupAction.NpcWalk` case (`:855-872`).

The handler must, in order:
1. find the critter at `Hex` (`CritterAt(hex, aliveOnly: true)`), erroring out like `--npc-walk` does
   if absent;
2. `StartNpcWalk(npc, target1)` and print `started1=`;
3. **pump the walker to completion** — loop `walker.Update(...)` over `_npcWalkers.Values` with a large
   dt until the walker reports `!Moving` (bounded by an iteration cap so a stuck walk cannot hang the
   harness), exactly as the autoplay loops pump (`ViewerGame.Harness.cs:2035-2038`);
4. print whether the walker is still `Moving` and whether it is still present in `_npcWalkers` — these
   two values *are* the bug, so print both;
5. `StartNpcWalk(npc, target2)` and print `started2=`;
6. print the critter's tile at the end.

One line, in the style of the other probes, e.g.:
```
walker-restart-probe: from <hex> t1=<t1> started1=<0|1> movingAfterPump=<0|1> inDict=<0|1> t2=<t2> started2=<0|1> tile=<tile>
```

- [ ] **Step 2: Run the probe against the UNFIXED engine and capture the failure**

Build, then pick a map and a critter with walk art and open space around it — `--map denbus2.map` is
the fixture map where the bug is observed, and `ProcAnalyze --map-objects` or an existing
`--blocked-probe` can identify a suitable critter and two free destinations. Choose targets that are
genuinely reachable and unblocked so nothing else can explain a refusal.

Expected on the unfixed engine: **`started1=1`, `movingAfterPump=0`, `inDict=1`, `started2=0`** — the
walker finished, was never removed, and blocked the second walk.

**If `started2=1` before the fix, stop and report.** The bug is not reproducing in your setup and
nothing below is justified.

Record the exact command and its exact output in your report — this is the mutation evidence for a
change that has no unit test.

- [ ] **Step 3: Fix the guard**

In `ViewerGame.cs`, replace the membership test at `:3328`:

```csharp
        // ported from fallout2-ce src/animation.cc animationIsBusy (:581): the reference's busy test
        // is LIVENESS-based — it walks only sequences actually in use (field_0 != -1000) and reports
        // busy only for a live animation. Ours asked whether the critter had EVER walked: a finished
        // walker stays in _npcWalkers (it is pruned only inside UpdateAmbientLife, :3262-3272, which
        // the autoplay harness loops never call and which returns early under DisableAmbientLife /
        // _worldmapOpen), so the critter was frozen for the rest of the run while callers kept logging
        // movement it never performed (F21). A stale idle entry is replaced by the assignment at :3383.
        if (npc == _dude?.Dude
            || (_npcWalkers.TryGetValue(npc, out DudeController? active) && active.Moving)
            || Fid.Type(npc.Fid) is not ObjectType.Critter)
            return false;
```

Verify `:3383` is still the assignment line on this branch before citing it.

- [ ] **Step 4: Re-run the probe and confirm the flip**

Same command as Step 2. Expected: **`started2=1`** and a final tile at or toward `target2`. Record the
exact output.

- [ ] **Step 5: Run the unit suite**

Run: `dotnet test`
Expected: **0 failed.** Nothing in `Hexwaste.Formats` should be affected; if something is, report it.

- [ ] **Step 6: Measure the fixture blast radius — and expect a large one**

Run: `scripts/combat-golden.sh check`, then **separately**, after it completes,
`scripts/encounter-golden.sh check`.

**This item's failing set genuinely cannot be predicted.** Any fixture where an NPC walker finishes and
that critter later attempts to move may change. `denbus2-fight-flee`'s `Healthy Slave@10270 -> 8870`
repeat is the one certainty.

**Enumerate every failing fixture** and, for each, classify the delta:
- **Expected class:** a critter that previously froze now moves — new/changed movement lines, and any
  downstream consequence of the critter being elsewhere.
- **Stop-and-investigate class:** anything else — changed damage with no movement change, a changed
  winner that no freed critter explains, a changed round count with no movement delta.

A large failing set is **not** alarming here; the analysis predicts the bug is widespread. An
unexplainable delta is.

- [ ] **Step 7: Justify before recording**

Write the trace: the shared mechanism once, then per-fixture evidence naming the critter that was
frozen and now moves. **If any fixture's delta cannot be explained, do not record** — stop and report
with that fixture's diff.

- [ ] **Step 8: Record and verify**

```bash
scripts/combat-golden.sh record
git status --short tests/golden-combat/
```
then, separately, the encounter suite if it moved. `git status` must list exactly the fixtures your
Step 6 enumeration predicted. Re-run both `check`s afterwards — expected ALL PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ tests/golden-combat/ tests/golden-encounter/
git commit -m "fix: a finished walker no longer freezes its critter (F21)

<the Step 7 trace: the mechanism, then every re-recorded fixture with the
critter that was frozen and now moves; plus the probe's before/after output>

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Decouple the prune from ambient life — and prove it inert

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs` (`UpdateAmbientLife`), `src/Hexwaste.Viewer/ViewerGame.Harness.cs`
  (the two autoplay loops)

- [ ] **Step 1: Hoist the prune**

Move the finished-walker prune (`ViewerGame.cs:3262-3272`) out of `UpdateAmbientLife` into its own
private method — e.g. `PruneFinishedWalkers(double elapsedMs)` — that advances the walkers and drops
the idle ones, and call it from `Update` (`ViewerGame.cs:2817` area) **before**, and independently of,
the `DisableAmbientLife || _worldmapOpen` early return. Walker lifecycle must not be a side effect of
ambient fidgeting.

Keep `UpdateAmbientLife`'s remaining behaviour unchanged, and make sure walkers are advanced exactly
once per frame — **not twice**. Double-advancing would change movement speed and is the most likely way
this task goes wrong.

- [ ] **Step 2: Make the autoplay loops use it**

The `--fight` loop (`ViewerGame.Harness.cs:2035-2038`) and the brawl-watch loop (`:205-208`) pump
`walker.Update(...)` directly. Replace those inline loops with the new method so finished walkers drain
there too. Note the two loops use different dt values (`10` and `100000`) — preserve each one's dt;
do not unify them.

- [ ] **Step 3: Run everything and expect NOTHING to move**

Run `dotnet test`, then `scripts/combat-golden.sh check`, then `scripts/encounter-golden.sh check`,
each to completion, one at a time.

**Expected: all green, no fixture moved.** Given Task 1, the guard no longer consults staleness, so
draining the dictionary must be behaviour-neutral.

**If a fixture moves, that is a stop condition and a finding**, not something to record: it means
either the walkers are being advanced a different number of times than before, or something else reads
`_npcWalkers` membership in a way this plan did not account for. Report it with the diff rather than
re-recording.

- [ ] **Step 4: Commit**

```bash
git add src/Hexwaste.Viewer/
git commit -m "refactor: walker lifecycle is no longer a side effect of ambient life (F21)

Finished-walker pruning lived inside UpdateAmbientLife, after its
'if (DisableAmbientLife || _worldmapOpen) return;' — so --no-ambient and an
open worldmap leaked walkers in the real game, and the autoplay harness
loops, which never call it, leaked them always. Hoisted into its own method
called independently, and used by both autoplay loops.

Behaviour-neutral by construction after the guard fix: no fixture moved.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Backlog reconciliation

**Files:** `docs/BACKLOG.md`

- [ ] **Step 1: F21 → shipped** with both SHAs, what was fixed, and **the full list of re-recorded
  fixtures**. State the consequence plainly: transcripts recorded through the autoplay paths before
  this fix could contain frozen-critter artefacts, and name which ones did.

- [ ] **Step 2: Answer F21's open question about the brawl-watch loop.** The entry says it "should be
  checked for the same defect before this is called fixed." Task 2 touches it — record whether it was
  in fact affected, and what changed (or did not).

- [ ] **Step 3: Record the rejected alternative** (again, since it recurs): reordering `TryFlee`'s
  transcript line after a successful `StartWalk` was not done, for the same reason as in F18 — it
  treats the symptom, and the correct fix makes the line truthful.

- [ ] **Step 4: Check numbering against the sibling branches.** `feat/critfail-fidelity` uses F15-F17;
  `feat/flee-maxdist` uses F18-F19; this lineage already added F20-F21. Any new entry must collide with
  none of them. State the numbers you chose.

- [ ] **Step 5: Commit**

```bash
git add docs/BACKLOG.md
git commit -m "docs: reconcile F21 as shipped

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:** the guard → Task 1 Step 3; the prune decoupling → Task 2; the three proof
  obligations → the probe (Steps 2/4) covers the guard's rule and the engine-level pairing, and Task 2
  Step 3 covers replacement/inertness; the fixture protocol → Task 1 Steps 6-8; docs → Task 3.
- **The spec's escape clause was triggered and is resolved here, not waved away:** there is no viewer
  unit-test seam, so the proof is a live probe in the codebase's own idiom, with explicit before/after
  output required in the report.
- **Highest risk is Task 2 Step 1** — double-advancing walkers would silently change movement speed.
  Called out inline; Step 3's "nothing moves" expectation is what would catch it.
