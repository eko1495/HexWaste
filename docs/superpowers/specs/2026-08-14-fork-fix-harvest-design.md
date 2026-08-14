# Maintained-fork fix harvest — design spec (2026-08-14)

Upstream `alexbatalov/fallout2-ce` — our sole authoritative reference — has stopped receiving
updates. Issue [#522](https://github.com/alexbatalov/fallout2-ce/issues/522) (cambragol,
2026-07-13) names two maintained forks. One of them continues the *same C++ tree* we ported from,
which makes its 18 months of bug fixes readable as diffs against our exact pinned commit.

This spec covers harvesting **engine-fidelity fixes only** from that fork, and producing a written
review of both forks. Quality-of-life features are surveyed and parked, not built.

## Context

Our reference clone `reference/fallout2-ce` sits at `e97087b` (2025-02-16, "Remove pause window").
Against `fallout2-ce/fallout2-ce@main` that is **1090 commits / 300 files changed**; ~240 unique
commit subjects mention fix/bug/crash/correct.

The two forks differ in character, and the difference decides how we treat them:

| Fork | Character | Our use |
| --- | --- | --- |
| [`fallout2-ce/fallout2-ce`](https://github.com/fallout2-ce/fallout2-ce) | Continuation of the same codebase. "Dozens upon dozens of bug fixes", pathfinding, graphics glitches, weapon stacking. Also carries deliberate non-vanilla QoL. SUL licensed. | **Bug-fix candidate source** — this spec |
| [`cambragol/fission-ce`](https://github.com/cambragol/fission-ce) | Rebrand + modularity focus; an `[enhancements]` toggle block over a `StrictVanilla` baseline. | **Surveyed only** — parked backlog, §6 |

Sampling the fix subjects confirms the value is real but diluted. Genuine finds include
`Fix a variety of decompilation errors (#675)` — the highest-value class, where upstream misread
the original binary and we then ported the misreading faithfully — plus
`Fix returnval of critter_mod_skill (#476)`,
`fix removing next object after hidden object when critter dies (#653)`,
`Fix crash when trying to run missing combat_p_proc and start is not the first in the script (#310)`,
`Fix OOM memory access with alternate damage calculation (#584)`, and
`Fix bug that stopped music when reloading on the same map (#168)`.

### Candidate buckets

1. **Engine-logic** — bugs we inherited by porting faithfully. **The target.**
2. **C++-only** — memory, UB, platform, build, formatting. Nonexistent in C#. Discard.
3. **Not-our-subsystem** — mapper, `.png`/`.zip` assets, sfall opcodes, emscripten/mobile. Discard.
4. **Non-vanilla QoL** — deliberate deviations from the original. Park (§6).

## 1. Reference setup

In `reference/fallout2-ce` (already gitignored):

```
git remote add community https://github.com/fallout2-ce/fallout2-ce
git fetch community
```

Never merged, never checked out. The working tree stays on `e97087b`, so every existing
`// ported from fallout2-ce src/tile.cc tileToScreenXY()` citation keeps meaning exactly what it
says. Diffs are read via `git diff e97087b..community/main -- src/<file>.cc`.

`CLAUDE.md`'s "Authoritative reference" section gains this rule:

> `alexbatalov e97087b` remains authoritative for vanilla behavior. `community/main` (fork
> `fallout2-ce/fallout2-ce`) is a **bug-fix candidate source only**. A change is ported only when it
> corrects a misreading of the original game — never because the fork made it. The fork also
> contains deliberate non-vanilla QoL; that is out of scope.

## 2. Triage pipeline

Three stages feed one ledger.

### Stage 1 — subject filter (mechanical, no judgment)

Drop any commit whose changed files are confined to `.github/`, `CMakeLists*`, `os/`,
`.clang-format`, emscripten/Android/iOS paths, or the mapper. Of the remainder, keep those touching
files we ported. Scripted against the GitHub compare API; output is a candidate list carrying
commit SHA, PR number, and touched files.

### Stage 2 — rationale read

For each survivor, read the diff plus its linked PR/issue and assign a bucket (§Context). Linked
issues usually state the original-game symptom, which is precisely the justification bar §3 sets.
Bucket 1 becomes a ledger row; buckets 2–3 are recorded as one-line discards; bucket 4 goes to §6.

### Stage 3 — completeness sweep

Full `git diff e97087b..community/main` over `src/tile.cc`, `src/combat.cc`, `src/interpreter.cc`,
`src/object.cc` **only**, reading hunks for behavior deltas Stages 1–2 missed — silent fixes buried
inside refactor or feature commits. These four are where a silent delta would be most damaging and
where the golden suites would actually notice. Refactor-only hunks are noted and skipped.

Stage 3 is a completeness check on Stage 2, not a replacement: a full 300-file diff read would give
hunks with no way to distinguish a fix from a restructure, and would discard the *why* that the
fork's PRs already document.

### The ledger

A single markdown table in `docs/research-notes/fork-fix-ledger-2026-08.md`, append-only across
sessions so a later run resumes rather than restarts. One row per candidate:

| Fork commit / PR | Reference file | Claimed symptom | Our C# call site | Status | Notes |
| --- | --- | --- | --- | --- | --- |

## 3. Per-candidate protocol

Every row ends in exactly one terminal state, each with a written reason.

1. **Locate** — find the corresponding C# call site. No call site (subsystem absent) → terminal
   `not-applicable`.
2. **Confirm** — prove Hexwaste exhibits the bug: a failing unit test, or a probe run through the
   existing `ProcAnalyze` / `--*-probe` tooling for anything needing real game data. Cannot
   reproduce → terminal `not-a-gap`, recording the code path that shows we are already correct.
3. **Justify** — the fix must correct a misreading of original behavior, grounded in the decompiled
   source or in a linked issue describing an in-game symptom. Fork opinion without that grounding →
   terminal `rejected-non-vanilla`.
4. **Fix** — TDD. The Stage-2 failing test lands first, then the change, cited as
   `// ported from fallout2-ce src/<file>.cc <func>() (community fix #NNN)` so provenance stays
   legible against a reference tree that does not contain the fix.
5. **Blast radius** — run every golden suite (`scripts/combat-golden.sh`,
   `scripts/encounter-golden.sh`, `scripts/quest-golden.sh`, plus the census / endgame / opening
   suites). Byte-identical is the expected outcome and proves **containment, not correctness** — the
   unit test from step 2 proves correctness. A moved golden is a stop-and-investigate; it becomes a
   deliberate re-record only after diff review establishes the new bytes are more faithful,
   following the P120 precedent.

Terminal states: `ported` · `not-applicable` · `not-a-gap` · `rejected-non-vanilla` · `parked-QoL`.

`not-a-gap` and `not-applicable` rows are deliverables in their own right — they stop a future
session re-litigating the same fork commit. Prior experience (`backlog-gaps-over-reported`) says
external gap reports over-report, and a fork's PR title is exactly such a second-hand claim.

## 4. Deliverables

- `docs/research-notes/fork-survey-2026-08.md` — the review: issue-522 context, both forks
  characterized, bucket analysis, per-candidate findings **including everything rejected and why**,
  and the parked catalogue (§6).
- `docs/research-notes/fork-fix-ledger-2026-08.md` — the ledger table (§2), kept as its own file so
  it stays append-friendly across sessions.
- Ported fixes: **one commit per fix, each with its regression test** — individually reviewable and
  revertible.
- `CLAUDE.md` reference rule (§1).
- `docs/BACKLOG.md` entries pointing at §6.

## 5. Out of scope (this spec)

QoL toggles / `[enhancements]`, sfall opcode compatibility, the BIS mapper, `.png` / `.zip` asset
loading, hi-res / widescreen work, emscripten and mobile targets, and any wholesale re-pin or
replacement of the reference clone.

## 6. Parked catalogue — surveyed, not built

Recorded so nothing needs re-discovering. **Sourced from fork READMEs and not individually verified
against our code**; each would need its own confirm pass before it becomes work. Several may
already exist in Hexwaste.

### From `fallout2-ce/fallout2-ce` (QoL)

- Party members loot and barter in place of the PC; directly equip party members
- Expanded 2-column inventory and loot screens; expanded 4-row barter screen; expanded AP bar
- Ctrl-click to move items when bartering / looting / stealing, with auto-balanced caps
- Music continues between maps; auto-open doors
- Integrated HELP menu; last used save slot remembered
- Item / corpse / container / critter highlighting
- 44.1 kHz stereo audio, `.ogg` / `.wav`

### From `cambragol/fission-ce` (`[enhancements]` toggles over a `StrictVanilla` baseline)

- AutoOpenDoors, AutoPush, AutoQuickSave
- DisplayBonusDamage, DisplayKarmaChanges
- Enhanced barter; explosions emit light; gapless music; minimap
- Mass highlight; NPC armor; numbered dialogue
- Configurable game speed and inventory column count

A future phase adopting any of these should follow the fission-ce shape: **vanilla by default,
opt-in toggle**, since every one of them is a deliberate deviation and our golden suites encode
vanilla behavior.

## 7. Risks

- **Unknown yield.** 240 fix-ish subjects may produce 30 real ports or 3; most will land in
  `not-a-gap` / `not-applicable`. No fix count is promised up front. The survey doc is the
  guaranteed deliverable; ported fixes are the variable one.
- **Licensing.** The fork ships under the same Sustainable Use License as upstream, and we already
  port from that codebase under those terms. No new condition is introduced — noted only for the
  record.
- **Reference drift.** Citations in our code name a tree that does not contain the ported fixes.
  Mitigated by the `(community fix #NNN)` citation suffix (§3.4) making the provenance explicit.
