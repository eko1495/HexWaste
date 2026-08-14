# Maintained-Fork Fidelity-Fix Harvest — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harvest engine-fidelity bug fixes from the maintained `fallout2-ce/fallout2-ce` fork into Hexwaste, and publish a written review of both maintained forks named in upstream issue #522.

**Architecture:** The fork continues the same C++ tree our reference clone is pinned to (`e97087b`), so its fixes read as diffs against our exact port source. A three-stage triage (mechanical file filter → PR-rationale read → full-diff sweep of the four highest-fidelity files) produces a ledger of candidates; each candidate then runs a fixed protocol — locate the C# call site, prove Hexwaste exhibits the bug, justify against *original-game* behavior, fix under TDD, and check blast radius against the golden suites. Every candidate ends in one of five terminal states with a written reason, so rejections are deliverables too.

**Tech Stack:** C# / .NET 10, xUnit, `gh` CLI + `jq` for the GitHub compare API, git remotes for diffing, the existing `scripts/*-golden.sh` regression nets.

**Spec:** `docs/superpowers/specs/2026-08-14-fork-fix-harvest-design.md`

## Global Constraints

- **The reference clone is never mutated beyond adding a remote.** `reference/fallout2-ce` stays checked out at `e97087b`. Never merge, never check out `community/main`.
- **`alexbatalov e97087b` remains authoritative for vanilla behavior.** The fork is a *bug-fix candidate source only*.
- **A fix is justified by original-game behavior, never by "the fork did it."** Changes the fork made as deliberate deviations (often marked `// CE:` in their source) are rejected.
- **Every ported routine keeps its provenance comment**, extended with the fork PR: `// ported from fallout2-ce src/combat.cc attackComputeCriticalFailure() (community fix #675)`.
- **TDD, always.** Failing test first, then the fix. A candidate with no failing test is not a candidate — it is `not-a-gap`.
- **Golden suites prove containment, not correctness.** Byte-identical is the expected result. A moved golden is stop-and-investigate.
- **One commit per fix**, each carrying its own regression test.
- Out of scope: QoL toggles, sfall opcodes, the mapper, `.png`/`.zip` assets, hi-res/widescreen, emscripten/mobile, re-pinning the reference clone.

---

## File Structure

**Created:**
- `docs/research-notes/fork-fix-ledger-2026-08.md` — the append-only candidate ledger (one row per fork change considered).
- `docs/research-notes/fork-survey-2026-08.md` — the written review of both forks; the C-deliverable.
- `scripts/fork-triage.sh` — the Stage-1 mechanical filter, committed so the candidate list is reproducible.

**Modified:**
- `CLAUDE.md:15-24` — the "Authoritative reference" section gains the fork-precedence rule.
- `docs/BACKLOG.md` — parked-QoL pointer.
- `src/Hexwaste.Formats/**` — one file per ported fix (unknown set until Task 3; Task 5 does the first).
- `tests/Hexwaste.Formats.Tests/**` — one regression test per ported fix.

---

## Task 1: Reference remote and documentation scaffold

Establishes the diffing capability and the precedence rule, so no later task has to decide which tree wins.

**Files:**
- Modify: `CLAUDE.md:15-24`
- Create: `docs/research-notes/fork-fix-ledger-2026-08.md`
- Create: `docs/research-notes/fork-survey-2026-08.md`

**Interfaces:**
- Consumes: nothing.
- Produces: the git ref `community/main` inside `reference/fallout2-ce`, diffable as `git -C reference/fallout2-ce diff e97087b..community/main -- src/<file>.cc`; the ledger table schema used by Tasks 3–7.

- [ ] **Step 1: Add the fork as a remote in the reference clone**

```bash
git -C reference/fallout2-ce remote add community https://github.com/fallout2-ce/fallout2-ce
git -C reference/fallout2-ce fetch community
```

- [ ] **Step 2: Verify the diff capability and that our tree did not move**

```bash
git -C reference/fallout2-ce log -1 --format='%h %s'
git -C reference/fallout2-ce diff --stat e97087b..community/main -- src/combat.cc
```

Expected: the first command still prints `e97087b Remove pause window` (the working tree has NOT moved). The second prints a non-empty diffstat for `src/combat.cc`.

- [ ] **Step 3: Add the precedence rule to CLAUDE.md**

In `CLAUDE.md`, immediately after the line
`If a format detail can't be confirmed from fallout2-ce sources, **stop and ask** instead of guessing.`
insert:

```markdown

Upstream is unmaintained ([issue #522](https://github.com/alexbatalov/fallout2-ce/issues/522)). The
maintained fork `fallout2-ce/fallout2-ce` is fetched into the same clone as the `community` remote
(`git diff e97087b..community/main -- src/x.cc`). **`alexbatalov e97087b` remains authoritative for
vanilla behavior**; `community/main` is a **bug-fix candidate source only**. Port a fork change only
when it corrects a misreading of the original game — never because the fork made it. The fork also
carries deliberate non-vanilla QoL (often marked `// CE:`); that is out of scope. Cite ported fork
fixes as `// ported from fallout2-ce src/x.cc f() (community fix #NNN)`.
```

- [ ] **Step 4: Create the ledger with its schema and no rows**

`docs/research-notes/fork-fix-ledger-2026-08.md`:

```markdown
# Fork fix ledger — `fallout2-ce/fallout2-ce` vs `e97087b` (2026-08)

Append-only. Every fork change considered gets exactly one row and one terminal status.

Statuses: `ported` · `not-applicable` (no such subsystem in Hexwaste) · `not-a-gap` (we are already
correct) · `rejected-non-vanilla` (fork deviates from the original on purpose) · `parked-QoL`.

Design: `docs/superpowers/specs/2026-08-14-fork-fix-harvest-design.md`

| PR / commit | Reference file | Claimed symptom | Our C# call site | Status | Notes |
| --- | --- | --- | --- | --- | --- |
```

- [ ] **Step 5: Create the survey doc skeleton**

`docs/research-notes/fork-survey-2026-08.md`:

```markdown
# Maintained-fork survey (2026-08)

Upstream `alexbatalov/fallout2-ce` is unmaintained; issue
[#522](https://github.com/alexbatalov/fallout2-ce/issues/522) (cambragol, 2026-07-13) names two
maintained forks. This is the review of both, and the record of what we took from them.

## 1. The two forks
## 2. Why one of them is a diff source
## 3. Triage results
## 4. Ported fixes
## 5. Rejected — and why
## 6. Parked: QoL catalogue for a future phase
```

- [ ] **Step 6: Commit**

```bash
git add CLAUDE.md docs/research-notes/fork-fix-ledger-2026-08.md docs/research-notes/fork-survey-2026-08.md
git commit -m "docs: add the community fork as a bug-fix candidate source

Upstream fallout2-ce is unmaintained (issue #522). Fetch the maintained
fork as the 'community' remote in the reference clone (never merged, tree
stays on e97087b) and state the precedence rule: alexbatalov e97087b is
authoritative for vanilla, the fork is a candidate source only."
```

---

## Task 2: Stage-1 mechanical candidate filter

Reduces 1090 commits to a machine-generated candidate list with zero judgment applied, so the list is reproducible and re-runnable when the fork moves.

**Files:**
- Create: `scripts/fork-triage.sh`

**Interfaces:**
- Consumes: `community/main` from Task 1 (the script uses the GitHub API, not the local clone, so it also works without a fetch).
- Produces: `scripts/fork-triage.sh` writing TSV rows `SHA<TAB>PR<TAB>SUBJECT<TAB>FILES` to stdout for commits that touch ported engine files.

- [ ] **Step 1: Write the script**

`scripts/fork-triage.sh`:

```bash
#!/usr/bin/env bash
# Stage-1 triage for the maintained-fork fix harvest.
#   docs/superpowers/specs/2026-08-14-fork-fix-harvest-design.md
#
# Lists fork commits between our pinned reference (e97087b) and community/main that touch C++
# sources we actually ported, dropping build/CI/platform/mapper-only churn. NO judgment is applied
# here — every surviving row still needs the Stage-2 rationale read.
#
# Usage: scripts/fork-triage.sh [base] [head]   (defaults: e97087b main)
set -euo pipefail

BASE="${1:-e97087b}"
HEAD_REF="${2:-main}"
REPO="fallout2-ce/fallout2-ce"

# Files we ported logic from. Anything outside this list is not our subsystem. Applied per-candidate
# in step 3 (the compare API does not carry per-commit file lists), exported for that use.
export PORTED='^src/(tile|combat|combat_ai|critter|object|map|proto|item|skill|perk|interpreter|interpreter_extra|scripts|worldmap|animation|art|color|palette|dfile|db|game_dialog|automap|inventory|party_member|queue|stat|trait|display_monitor|light|actions|pipboy|elevator|endgame|random)\.cc$'

for page in 1 2 3 4 5; do
  gh api "repos/$REPO/compare/$BASE...$HEAD_REF?per_page=250&page=$page" 2>/dev/null || true
done | jq -r '
  .commits // [] | .[] |
  . as $c |
  ($c.commit.message | split("\n")[0]) as $subj |
  ($subj | capture("#(?<n>[0-9]+)").n // "") as $pr |
  [$c.sha[0:9], $pr, $subj] | @tsv
' | sort -u
```

Note: the compare API returns commits but not their per-commit file lists, so the file filter is
applied in Step 3 per candidate. Keeping the two steps separate avoids 1090 extra API calls on
every re-run.

- [ ] **Step 2: Make it executable and run it**

```bash
chmod +x scripts/fork-triage.sh
scripts/fork-triage.sh | wc -l
```

Expected: roughly 985 unique subject rows (the exact count may drift if the fork advances).

- [ ] **Step 3: Produce the fix-shortlist with file filtering**

```bash
scripts/fork-triage.sh | grep -iE 'fix|bug|crash|correct|revert' \
  | tee /tmp/fork-fix-shortlist.tsv | wc -l
```

Expected: on the order of 240 rows. For each row carrying a PR number, its touched files come from:

```bash
gh api repos/fallout2-ce/fallout2-ce/pulls/<PR>/files --paginate --jq '.[].filename'
```

Rows whose files are entirely outside the `PORTED` pattern above are bucket 2/3 discards.

- [ ] **Step 4: Commit the script**

```bash
git add scripts/fork-triage.sh
git commit -m "chore: add scripts/fork-triage.sh for the fork fix harvest

Stage-1 mechanical filter over the GitHub compare API: fork commits
between e97087b and community/main, reproducible and re-runnable."
```

---

## Task 3: Stage-2 rationale read — classify every shortlisted commit

Turns the shortlist into ledger rows. This is the judgment-heavy pass; it produces the work list for Tasks 5–7.

**Files:**
- Modify: `docs/research-notes/fork-fix-ledger-2026-08.md`

**Interfaces:**
- Consumes: `/tmp/fork-fix-shortlist.tsv` from Task 2.
- Produces: a ledger where every shortlisted row has a bucket; bucket-1 rows carry a **candidate** status (blank Status cell means "not yet run through Task 5's protocol").

- [ ] **Step 1: For each shortlisted row, read the diff and the linked issue**

```bash
gh api repos/fallout2-ce/fallout2-ce/pulls/<PR> --jq '.title, .body'
gh api repos/fallout2-ce/fallout2-ce/pulls/<PR>/files --paginate --jq '.[] | .filename, .patch'
```

Assign a bucket:

| Bucket | Signal | Ledger action |
| --- | --- | --- |
| 1 engine-logic | Changes behavior of a routine we ported | Add a row, Status blank (candidate) |
| 2 C++-only | Memory/UB/build/formatting/platform; no behavior change in a managed port | Row with `not-applicable`, one-line reason |
| 3 not-our-subsystem | mapper, `.png`/`.zip`, sfall opcodes, emscripten, audio backend | Row with `not-applicable`, one-line reason |
| 4 non-vanilla QoL | Marked `// CE:`, or a README enhancement | Row with `parked-QoL`; also list it in survey §6 |

- [ ] **Step 2: Prioritize PR #675 explicitly**

`Fix a variety of decompilation errors (#675)` is the highest-value single PR: a broad
decompilation-correctness audit spanning `actions.cc art.cc automap.cc cache.cc combat.cc
combat_ai.cc critter.cc dbox.cc dialog.cc display_monitor.cc endgame.cc game.cc game_dialog.cc
interface.cc interpreter_extra.cc inventory.cc item.cc lips.cc map.cc mouse_manager.cc` and more.
Every hunk in it is by definition a place where upstream misread the original binary and we ported
the misreading. **Give each hunk its own ledger row**, not one row for the PR:

```bash
gh api repos/fallout2-ce/fallout2-ce/pulls/675/files --paginate --jq '.[] | .filename, .patch' \
  > /tmp/pr675.patch
```

- [ ] **Step 3: Write the rows**

Row format (real examples, both already verified while writing this plan):

```markdown
| [#675](https://github.com/fallout2-ce/fallout2-ce/pull/675) | `combat.cc attackComputeCriticalFailure()` | DAM_HURT_SELF must add a further `randomBetween(1,5)` to attacker damage; upstream omits it | `CombatEngine.cs:1190` — lumps HitSelf/HurtSelf into one `CritFailDamage` call | | Confirmed present in our code; Task 5 |
| [#476](https://github.com/fallout2-ce/fallout2-ce/pull/476) | `interpreter_extra.cc opCritterModifySkill()` | Opcode pushes a return value the ssl compiler never pops → scripts loop forever | `IntVm.cs:1932` — we push it too | `rejected-non-vanilla` | Fork comment says `// CE: remove returnval`; sfall patches it too. The *original* engine pushes, so we match vanilla. Revisit only if a vanilla script actually hangs. See Task 6 |
```

- [ ] **Step 4: Commit the ledger**

```bash
git add docs/research-notes/fork-fix-ledger-2026-08.md
git commit -m "docs: classify the fork fix shortlist into the harvest ledger"
```

---

## Task 4: Stage-3 completeness sweep of the four fidelity-critical files

Catches silent behavior deltas buried inside refactor or feature commits, which Stage 2 cannot see.

**Files:**
- Modify: `docs/research-notes/fork-fix-ledger-2026-08.md`

**Interfaces:**
- Consumes: the `community` remote (Task 1) and the existing ledger rows (Task 3), used to skip anything already recorded.
- Produces: additional bucket-1 candidate rows tagged `stage-3` in Notes.

- [ ] **Step 1: Dump the four diffs**

```bash
for f in tile combat interpreter object; do
  git -C reference/fallout2-ce diff e97087b..community/main -- "src/$f.cc" > "/tmp/sweep-$f.diff"
  wc -l "/tmp/sweep-$f.diff"
done
```

- [ ] **Step 2: Read each diff hunk by hunk**

For every hunk, decide: **behavior delta** (add a ledger row, Notes `stage-3`), **pure refactor**
(rename, extraction, formatting, `const`-ness, container swap — skip), or **already in the ledger**
(skip). Refactor-heavy files will be mostly the middle category; that is expected.

- [ ] **Step 3: Append the new rows and commit**

```bash
git add docs/research-notes/fork-fix-ledger-2026-08.md
git commit -m "docs: stage-3 sweep of tile/combat/interpreter/object into the ledger"
```

---

## Task 5: First port end-to-end — DAM_HURT_SELF self-damage (#675)

The worked example of the full protocol. This candidate is **already verified as a real gap**:
`reference/fallout2-ce src/combat.cc attackComputeCriticalFailure()` in the fork adds
`attack->attackerDamage += randomBetween(1, 5)` when `DAM_HURT_SELF` is set — a decompilation
omission upstream — and `src/Hexwaste.Formats/Combat/CombatEngine.cs:1190` handles `DamHitSelf` and
`DamHurtSelf` identically, so we are missing it.

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs:1189-1191` (call site) and `:1219-1234` (`CritFailDamage`)
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`
- Modify: `docs/research-notes/fork-fix-ledger-2026-08.md`

**Interfaces:**
- Consumes: `CritFailDamage(CritterState attacker, CritterState victimState, ProtoInfo? weaponProto, string tag)`; `ICombatRng.Next(int minInclusive, int maxExclusive)`; the existing private test helpers `FakeCombatHost`, `SequenceRng`, `NewCritter(int tile, int hp, ...)` in `CombatEngineTests.cs`.
- Produces: `CritFailDamage(CritterState attacker, CritterState victimState, ProtoInfo? weaponProto, string tag, bool hurtSelf = false)` — the `hurtSelf` flag rolls the extra damage *inside* the method, after the base damage roll, to preserve RNG stream order.

- [ ] **Step 1: Write the failing test**

Append to `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`, next to
`CriticalFailureAppliesTheTableEffect`:

```csharp
    [Fact]
    public void HurtSelfFumbleRollsTheExtraOneToFiveDamage()
    {
        // community fix #675 (combat.cc attackComputeCriticalFailure): DAM_HURT_SELF adds a further
        // randomBetween(1, 5) on top of the rolled self-damage. Upstream omitted it, so we did too.
        // _cf_table row 0 (unarmed) col 3 = 524290 = HURT_SELF | KNOCKED_DOWN, so a day-6 dude fumble
        // at severity 3 takes that path. SequenceRng: to-hit 100 (miss), upgrade 1 (crit-fail),
        // severity 80 (col 3); every later draw repeats 80 clamped into range.
        var host = new FakeCombatHost { CriticalsEnabled = true, DudeCritFailuresEnabled = true };
        MapObject dude = host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        var rng = new RecordingRng(new SequenceRng(100, 1, 80));
        var engine = new CombatEngine(host, rng);

        Assert.True(engine.TryAttack(enemy));

        // The reference's randomBetween(1, 5) is inclusive → Next(1, 6) here.
        Assert.Contains((1, 6), rng.Draws);
        Assert.True(dude.CurrentHp < 30, "the fumble must cost the dude HP");
    }
```

And add the recording helper beside `MinRng` / `SequenceRng` in the same file:

```csharp
    /// <summary>Wraps another RNG and records the (min, maxExclusive) bounds of every draw —
    /// lets a test assert that a specific roll happened, independent of damage-formula internals.</summary>
    private sealed class RecordingRng(ICombatRng inner) : ICombatRng
    {
        public readonly List<(int Min, int MaxExclusive)> Draws = [];
        public int Next(int minInclusive, int maxExclusive)
        {
            Draws.Add((minInclusive, maxExclusive));
            return inner.Next(minInclusive, maxExclusive);
        }
    }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/Hexwaste.Formats.Tests --filter HurtSelfFumbleRollsTheExtraOneToFiveDamage
```

Expected: FAIL — `Assert.Contains` reports no `(1, 6)` draw in `rng.Draws`, because the extra roll
does not exist yet.

- [ ] **Step 3: Implement the fix**

In `src/Hexwaste.Formats/Combat/CombatEngine.cs`, change the call site (currently line 1190) from:

```csharp
        else if ((flags & (CriticalTables.DamHitSelf | CriticalTables.DamHurtSelf)) != 0)
            CritFailDamage(attacker, attacker, weaponProto, "crit-fail-self");
```

to:

```csharp
        else if ((flags & (CriticalTables.DamHitSelf | CriticalTables.DamHurtSelf)) != 0)
            CritFailDamage(attacker, attacker, weaponProto, "crit-fail-self",
                hurtSelf: (flags & CriticalTables.DamHurtSelf) != 0);
```

and extend `CritFailDamage` (currently line 1219):

```csharp
    // ported from fallout2-ce src/combat.cc attackComputeCriticalFailure() (community fix #675):
    // DAM_HURT_SELF adds a further randomBetween(1, 5) on top of the rolled damage. The extra roll is
    // taken HERE, after the damage roll, to keep the RNG stream in reference order.
    private void CritFailDamage(CritterState attacker, CritterState victimState, ProtoInfo? weaponProto,
        string tag, bool hurtSelf = false)
    {
        MapObject victim = victimState.Critter;
        int dmg = weaponProto?.Weapon is { } w
            ? CombatMath.RollWeaponDamage(_rng, attacker, victimState, w.MinDamage, w.MaxDamage, 1, false, 0)
            : CombatMath.RollDamage(_rng, attacker, victimState, 1, false, 0);
        if (hurtSelf)
            dmg += _rng.Next(1, 6);
        victim.CurrentHp -= dmg;
```

The rest of the method body is unchanged.

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/Hexwaste.Formats.Tests --filter HurtSelfFumbleRollsTheExtraOneToFiveDamage
```

Expected: PASS.

- [ ] **Step 5: Run the full unit suite**

```bash
dotnet test
```

Expected: all tests pass. `CriticalFailureTests` and the other `CombatEngineTests` crit-fail cases
must be unaffected — none of them take the HURT_SELF branch.

- [ ] **Step 6: Check blast radius against the golden suites**

```bash
scripts/combat-golden.sh
scripts/encounter-golden.sh
scripts/quest-golden.sh
```

Expected: byte-identical (`check` mode reports no diff). **This fix consumes one extra RNG draw
whenever a HURT_SELF fumble occurs**, which would shift every subsequent roll in that transcript —
so if a combat golden moves, confirm the moved fixture actually contains a HURT_SELF fumble
(`grep 'crit-fail-self' <fixture>`) before treating it as expected. If it does, re-record it and
diff-review the new bytes per the P120 precedent; if it does not, the fix is wrong — stop.

- [ ] **Step 7: Update the ledger row to `ported` and commit**

```bash
git add src/Hexwaste.Formats/Combat/CombatEngine.cs \
        tests/Hexwaste.Formats.Tests/CombatEngineTests.cs \
        docs/research-notes/fork-fix-ledger-2026-08.md
git commit -m "fix: DAM_HURT_SELF fumbles add the extra 1-5 self-damage

attackComputeCriticalFailure applies randomBetween(1, 5) on top of the
rolled damage for DAM_HURT_SELF; upstream fallout2-ce omitted it in
decompilation and we ported the omission. Ported from community fix #675."
```

---

## Task 6: Record the first rejection — `critter_mod_skill` return value (#476)

The worked example of the *rejection* path, which is half the deliverable. No code changes.

**Files:**
- Modify: `docs/research-notes/fork-fix-ledger-2026-08.md`

**Interfaces:**
- Consumes: the ledger schema from Task 1.
- Produces: the precedent for how `rejected-non-vanilla` rows are written.

- [ ] **Step 1: Confirm the behavior matches upstream in our code**

```bash
grep -n "0x813C" -A 6 src/Hexwaste.Formats/Int/IntVm.cs
```

Expected: `PushInt(_externals.CritterModSkill(PopInt(), skill, points)); // last pop = critter; pushes 0`
— we push the return value, exactly as upstream `opCritterModifySkill` does.

- [ ] **Step 2: Confirm the fork's change is a deliberate deviation, not a decompilation fix**

```bash
gh api repos/fallout2-ce/fallout2-ce/pulls/476/files --paginate --jq '.[].patch' | grep -A 2 'CE:'
gh api repos/fallout2-ce/fallout2-ce/issues/474 --jq '.body'
```

Expected: the patch replaces `programStackPushInteger(program, 0);` with the comment
`// CE: remove returnval, which ssl compiler doesn't expect`, and issue #474 reports a *mod* script
(`gl_addskill.int`) hanging. The `CE:` marker and the sfall parallel both say the original engine
does push — so the fork is knowingly deviating from vanilla to make mod scripts work.

- [ ] **Step 3: Write the ledger row**

Status `rejected-non-vanilla`, Notes: *"Fork marks it `// CE:`; sfall patches it the same way. The
original engine pushes, and Hexwaste matches the original. The reported hang needs a mod script — no
vanilla script trips it. Revisit only if a vanilla script is observed hanging here."*

- [ ] **Step 4: Commit**

```bash
git add docs/research-notes/fork-fix-ledger-2026-08.md
git commit -m "docs: reject community fix #476 (critter_mod_skill returnval) as non-vanilla"
```

---

## Task 7: Work the remaining bucket-1 candidates

Repeat Task 5's protocol per candidate. Each candidate is its own commit and its own review gate.

**Files:**
- Modify: per candidate — the C# file holding the call site, its test file, and the ledger.

**Interfaces:**
- Consumes: the candidate rows from Tasks 3 and 4.
- Produces: one terminal ledger status per candidate.

For each ledger row with a blank Status, in ledger order:

- [ ] **Step 1: Locate.** Find the C# call site corresponding to the fork's changed routine. If the subsystem does not exist in Hexwaste, set `not-applicable` with the reason, commit the ledger, and move to the next candidate.

- [ ] **Step 2: Confirm.** Write a failing test that reproduces the bug in our code, modelled on Task 5 Step 1 — an xUnit test in the matching `tests/Hexwaste.Formats.Tests/*Tests.cs` file, using the fake host / scripted-RNG pattern already in that file. For anything needing real game data, reproduce with the existing probe tooling instead:

```bash
dotnet run --project tools/ProcAnalyze -- --help    # census/quest/path probes
dotnet run --project src/Hexwaste.Viewer -- --help  # the --*-probe headless probes
```

  If the bug cannot be reproduced, set `not-a-gap`, record **the code path that shows we are already correct**, commit the ledger, and move on. This is an expected and valuable outcome — most candidates will land here.

- [ ] **Step 3: Run the test to verify it fails.**

```bash
dotnet test tests/Hexwaste.Formats.Tests --filter <TestName>
```

Expected: FAIL, for the reason the fork's PR describes.

- [ ] **Step 4: Justify.** Confirm the fix reflects the *original game*, using the decompiled routine in `reference/fallout2-ce` at `e97087b` plus the fork's linked issue. If the fork's change is marked `// CE:` or is otherwise a deliberate deviation, set `rejected-non-vanilla` per Task 6, delete the test, commit the ledger, and move on.

- [ ] **Step 5: Implement the minimal fix**, with the provenance comment:

```csharp
// ported from fallout2-ce src/<file>.cc <function>() (community fix #NNN)
```

- [ ] **Step 6: Run the test, then the full unit suite.**

```bash
dotnet test tests/Hexwaste.Formats.Tests --filter <TestName>
dotnet test
```

Expected: the new test PASSES and nothing else regresses.

- [ ] **Step 7: Check blast radius.** Run the golden suites touching the changed subsystem — combat/AI changes need `scripts/combat-golden.sh`; worldmap/encounter/companion changes need `scripts/encounter-golden.sh`; script-VM or quest-state changes need `scripts/quest-golden.sh`. When unsure, run all three plus `scripts/census-sweep.sh`. Byte-identical is expected; a moved golden is stop-and-investigate, and becomes a deliberate re-record only after diff review shows the new bytes are more faithful.

- [ ] **Step 8: Set the row to `ported` and commit** — one commit per fix, message `fix: <behavior> (community fix #NNN)`.

---

## Task 8: Publish the survey and park the QoL catalogue

The C-deliverable: the written review, plus the backlog pointer so the out-of-scope features are not re-discovered later.

**Files:**
- Modify: `docs/research-notes/fork-survey-2026-08.md`
- Modify: `docs/BACKLOG.md`

**Interfaces:**
- Consumes: the completed ledger from Tasks 3–7.
- Produces: the final review document.

- [ ] **Step 1: Fill in survey sections 1–2**

Section 1 characterizes both forks:
[`fallout2-ce/fallout2-ce`](https://github.com/fallout2-ce/fallout2-ce) — continuation of the same
codebase, "dozens upon dozens of bug fixes", pathfinding and graphics-glitch work, plus deliberate
non-vanilla QoL; SUL licensed. [`cambragol/fission-ce`](https://github.com/cambragol/fission-ce) —
rebrand plus a modular `[enhancements]` toggle block over a `StrictVanilla` baseline.

Section 2 states the material fact: our reference is pinned at `e97087b` (2025-02-16), the
Community fork is the same tree **1090 commits / 300 files** later, so its fixes are diffs against
our exact port source. `fission-ce` is a *survey* source only — it is a feature fork, not a
fidelity fork, so it contributes to §6 and nothing else.

- [ ] **Step 2: Fill in sections 3–5 from the ledger**

Section 3: counts per terminal status. Section 4: each `ported` row with its commit SHA and the
behavior it corrects. Section 5: each `not-a-gap` / `rejected-non-vanilla` row with its reason —
explicitly framed so a future session does not re-litigate the same fork commit.

- [ ] **Step 3: Write section 6, the parked QoL catalogue**

Copy the catalogue verbatim from the spec's §6 (both fork lists), keeping its caveat: **sourced from
fork READMEs, not individually verified against our code; several may already exist in Hexwaste**.
Add the adoption rule: vanilla by default, opt-in toggle, because the golden suites encode vanilla.

- [ ] **Step 4: Add the BACKLOG pointer**

Append to `docs/BACKLOG.md` an entry pointing at `docs/research-notes/fork-survey-2026-08.md` §6 as
the surveyed-but-unbuilt QoL source, noting that each item needs its own confirm pass first.

- [ ] **Step 5: Final full verification**

```bash
dotnet build
dotnet test
scripts/combat-golden.sh
scripts/encounter-golden.sh
scripts/quest-golden.sh
```

Expected: build clean, all unit tests pass, every golden suite byte-identical except any fixtures
deliberately re-recorded and diff-reviewed during Task 5 or 7.

- [ ] **Step 6: Commit**

```bash
git add docs/research-notes/fork-survey-2026-08.md docs/BACKLOG.md
git commit -m "docs: publish the maintained-fork survey and park the QoL catalogue"
```

---

## Notes for the reviewer

- **Yield is unknown by design.** The ~240 fix-ish subjects may produce 30 ports or 3; most will end
  `not-a-gap` or `not-applicable`. The survey is the guaranteed deliverable.
- **PR #675 is the centre of mass.** A decompilation-correctness audit across ~20 engine files is
  exactly the class where Hexwaste faithfully ported a wrong behavior. If time is short, work #675's
  hunks before anything else in the shortlist.
- **Rejections are output, not waste.** A `not-a-gap` row with the code path that proves we are
  already correct is what stops the next session re-opening the same fork commit.
