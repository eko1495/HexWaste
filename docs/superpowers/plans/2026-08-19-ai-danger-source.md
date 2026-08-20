# `_ai_danger_source` — enemy target selection (F-tier, last big re-record) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Hexwaste's hand-rolled enemy target selection with a port of `_ai_danger_source`
(`combat_ai.cc:1529-1705`), and switch `PruneEscapedHostiles` to the perception model it unblocks.

**Architecture:** New pure helpers in `Hexwaste.Formats.Combat` (inert on arrival), then `TryEnemyAction`'s
target-selection prologue is replaced by a single `DangerSource(enemy)` call, then the prune. One branch,
three sequenced behavioural commits, a fixture measurement between each.

**Tech Stack:** C# / .NET 10 (`net10.0`), xUnit. Reference: `reference/fallout2-ce` at `e97087b`.

## Global Constraints

- **Never copy, embed, or commit game assets.** `.gitignore` excludes `*.dat`, `*.map`, `*.frm`, `*.pal`, `game-data/`.
- **Port from `reference/fallout2-ce`, never guess.** Every behavioural change carries a comment citing its
  source file and function. If a detail cannot be confirmed, **stop and ask**.
- **`alexbatalov e97087b` is authoritative for vanilla.** `community/main` is a bug-fix candidate source only,
  cited as `(community fix #NNN)`.
- **VERIFY EVERY LINE NUMBER YOU CITE**, in the reference *and* in Hexwaste, **on this branch**. Recent
  branches produced three wrong reference citations and one set of stale Hexwaste numbers read elsewhere.
- **No new dependencies.** `src/Hexwaste.Formats` stays free of MonoGame references.
- **Golden-net discipline:** scripts run the *prebuilt* binary. Never run two nets concurrently, never
  background one, **never build while one runs**. Scripts print no fixture count — report exactly what they
  print. The encounter suite writes to fixed `/tmp` paths, so overlapping runs corrupt it.
- **`scripts/combat-golden.sh` is the implementer's; `quest-golden.sh` and `encounter-golden.sh` are the
  controller's.** Three implementers on recent branches stalled or doubled-up on the slow suites.
- Every regression test must be **confirmed failing pre-change**, and failing for the *right reason*.

---

## The two non-vanilla markers — decided, do not re-litigate

- **`// CE:` at `combat_ai.cc:1565`** ("Slightly improve 'Whomever is attacking me' targeting"): **EXCLUDED.**
  CLAUDE.md puts CE QoL out of scope. The block is purely additive — it sets `candidate`, then
  `if (candidate == nullptr) { …fallback… }` runs. **Omit the block; implement the fallback.** That IS the
  vanilla path. (The clone's history is truncated, so it cannot be diffed — the structure is the evidence.)
- **`// SFALL:` at `combat_ai.cc:1483`** (the `continue` in `aiFindAttackers`): **PORTED.** Precedent:
  `EventQueue.cs` and `AiBestWeapon.cs` already cite SFALL-marked fixes as baseline.

---

## Reference facts

Read `combat_ai.cc:1529-1705`, `:1397-1425` and `:1457-1528` yourself before writing code. Summary:

**`_ai_danger_source(a1)`**
- Party members only: resolve `ignoreFleeingCritters` from disposition (custom/coward/defensive/aggressive
  → true; none/berserk → false), forced **false** when `aiGetDistance(a1) == DISTANCE_CHARGE`; then switch on
  `aiGetAttackWho`. STRONGEST/WEAKEST/CLOSEST **clear `a1->whoHitMe`** (`:1642`).
- Non-party: `attackWho = -1` (`:1648`).
- `whoHitMe` null or self → `targets[0] = null`. **Alive → `return whoHitMe` immediately when
  `attackWho == WHOMEVER || attackWho == -1` (`:1657`) — no perception, no reachability.** Dead → 
  `targets[0] = _ai_find_nearest_team(a1, whoHitMe, 1)` if on a different team, else null.
- `aiFindAttackers` fills `targets[1..3]` (`:1668`).
- If `ignoreFleeingCritters`, null out any fleeing target.
- Sort the 4 by strength / weakness / distance per `attackWho`.
- Return the first candidate that is non-null **and** `isWithinPerception`, **and** for which
  `pathfinderFindPath(a1, a1->tile, candidate->tile, nullptr, 0, _obj_blocking_at) != 0`
  **OR** `_combat_check_bad_shot(...) == COMBAT_BAD_SHOT_OK`. Note `a5 = 0` — the goal-tile exemption is
  correct here (contrast F18/F20). Note the **OR**: inverting it to AND silently narrows targeting.
- Else `nullptr`.

**`aiFindAttackers(critter, &whoHitMe, &whoHitFriend, &whoHitByFriend)`** (`:1457`) — scan distance-sorted
critters, skipping self, until 3 slots filled; each match `continue`s (SFALL) so one candidate fills at most
one slot:
- slot 1: candidate alive **and** `candidate->whoHitMe == critter` (someone attacking me);
- slot 2: candidate on **my** team, its `whoHitMe` non-null, `!= critter`, on another team, alive → store
  **that attacker** (not the friend);
- slot 3: candidate on **another** team, alive, and its `whoHitMe` is on **my** team.

**`_ai_find_nearest_team(a1, a2, flags)`** (`:1397`) — nearest living critter, distance-sorted from `a1`,
excluding `a1`, where flag `0x01` means *same* team as `a2` and `0x02` means *different*.

## Hexwaste facts (verify on this branch)

- Target selection today is the prologue of `TryEnemyAction` (~`:2823-2880`): nearest of dude+companions
  (cross-team loop when `_dudeSpectator`), then a perception gate that **exempts `whoHitMe`**, then
  `FriendAttacker` (a partial slot 2), then a retaliation override that prefers a live cross-team
  `WhoHitMe`. It is an ad-hoc approximation of the same logic in a different order.
- `PerceptionDetect.IsWithinPerception` and `CombatEngine.WithinPerception` exist.
- `AiRating.Score` exists; `CompanionAi.PickTarget` carries the **inverted** Strongest/Weakest comparators
  deliberately (vanilla quirk) — reuse, do not "correct".
- `MapObject` exposes `Team`, `WhoHitMe`, `IsDead`, `HexTile`; `CombatEngine` has `_hostiles`,
  `_host.PartyMembers`, `_host.CombatCritters`, `_host.Dude`.
- `Pathfinder.FindPath(from, to, isBlocked, isPassableDoor = null, requireFreeDestination = false)` — the
  default `false` is `a5 = 0`, which is what this site needs.
- Applying `AttackWho` to companions only is **faithful** (`:1541` / `:1648`). Do not widen it.
- F1 shipped engine-set `FLEEING`/`DISENGAGING` maneuver flags — `ManeuverFleeing` is `0x04` on
  `MapObject.Maneuver`. That is `critterIsFleeing`.

---

## Task 1: The pure helpers (must move nothing)

**Files:**
- Create: `src/Hexwaste.Formats/Combat/AiTargets.cs`
- Test: `tests/Hexwaste.Formats.Tests/AiTargetsTests.cs`

**Interfaces — later tasks depend on these exact signatures:**
```csharp
public static class AiTargets
{
    public static (MapObject? WhoHitMe, MapObject? WhoHitFriend, MapObject? WhoHitByFriend)
        FindAttackers(MapObject self, IReadOnlyList<MapObject> distanceSorted);

    public static MapObject? FindNearestTeam(MapObject self, MapObject reference,
        bool sameTeam, IReadOnlyList<MapObject> distanceSorted);
}
```
Both take a **pre-distance-sorted** list, which keeps them pure (no distance function, no host) and exactly
mirrors the reference, where both helpers call `_ai_sort_list_distance` before scanning.

- [ ] **Step 1: Write the tests first**

Cover, at minimum: each of the three `FindAttackers` slots in isolation; the **SFALL rule** that one
candidate cannot fill two slots (construct a critter that qualifies for slots 1 and 3 and assert it lands
in exactly one); dead candidates skipped; self skipped; `FindNearestTeam` for `sameTeam: true` and `false`,
returning the *first* in the supplied order (i.e. nearest) and `null` when none qualifies.

Use the existing test-file conventions — check how `CombatEngineTests.cs` builds `MapObject`s
(`NewCritter(...)`) and reuse that helper rather than inventing one.

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test --filter "FullyQualifiedName~AiTargets"`
Expected: compile failure, then genuine assertion failures once the class exists as a stub. Report the
real failures, not just "does not compile".

- [ ] **Step 3: Implement `AiTargets`**

Port both helpers from `:1457-1528` and `:1397-1425`, each with a citation comment. Include the SFALL
`continue` semantics and cite it as `(SFALL fix, combat_ai.cc:1483)`.

- [ ] **Step 4: Green, then prove inertness**

Run `dotnet test` (expect 0 failed), then `scripts/combat-golden.sh check`.
**Expected: ALL PASS — nothing calls these yet.** If a fixture moves, something is wired that should not be;
stop and report.

- [ ] **Step 5: Commit**

```bash
git add src/Hexwaste.Formats/Combat/AiTargets.cs tests/Hexwaste.Formats.Tests/AiTargetsTests.cs
git commit -m "feat: port aiFindAttackers and _ai_find_nearest_team as pure helpers (inert)

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: `_ai_danger_source` replaces the ad-hoc prologue (the big re-record)

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` — new `DangerSource(MapObject)`; `TryEnemyAction`'s
  prologue calls it
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`
- Re-record (expected, large): `tests/golden-combat/`, `tests/golden-encounter/`

- [ ] **Step 1: Write the hermetic tests first**

The spec's proof obligations 1-6. Each must fail pre-change **for the right reason**:
1. **Living `whoHitMe` short-circuits** — a non-party enemy targets it even when out of perception AND
   unreachable. This is the guard against applying the perception gate everywhere.
2. **Dead `whoHitMe`** falls through to `FindNearestTeam` on the other team.
3. **`aiFindAttackers` wiring** — with no `whoHitMe`, a critter attacking me is chosen.
4. **Perception gates the fallback only** — an out-of-perception fallback candidate is skipped and the next
   viable one taken.
5. **Reachability is a disjunction** — an unreachable candidate is still selected when the shot is legal.
   Write this so it fails if someone uses `AND`.
6. **Party gating** — a non-party critter never consults `AttackWho`; the STRONGEST/WEAKEST/CLOSEST branch
   clearing `whoHitMe` applies to party members only.

- [ ] **Step 2: Confirm each fails, and why**

Run the filter; record each failure message. A test that passes here is not exercising the branch — report
it rather than proceeding.

- [ ] **Step 3: Implement `DangerSource`**

Add a private `MapObject? DangerSource(MapObject self)` to `CombatEngine` following the reference order
exactly (party branch → `attackWho` → whoHitMe early return → targets[0..3] → fleeing filter → sort →
perception + reachability loop). Reuse `AiTargets`, `AiRating`, `CompanionAi`'s comparators,
`WithinPerception`, and `Pathfinder.FindPath(..., requireFreeDestination: false)`.

Then replace `TryEnemyAction`'s prologue (~`:2823-2880`) with a `DangerSource(enemy)` call. **Preserve the
`_dudeSpectator` behaviour** — a brawl the dude is not in must still work; if the reference has no
counterpart for that concept, keep Hexwaste's handling and document it as a carried divergence.

Delete `FriendAttacker` only if `DangerSource` genuinely subsumes it; if anything still calls it, say so.

- [ ] **Step 4: Green**

`dotnet test` — expect 0 failed. Existing combat tests may need updating where they asserted the old
ad-hoc ordering; for each, state whether the new expectation is the reference's behaviour or a test that
was pinning the approximation.

- [ ] **Step 5: Measure — expect a LARGE failing set**

Run `scripts/combat-golden.sh check`, then report. **Do not run the encounter suite** — the controller
runs it and will hand you the enumeration.

Classify every failure: a changed *target choice* and its downstream consequences is the expected class; a
delta with no target change behind it is stop-and-investigate.

**If the set is too large to classify honestly, STOP and report.** Recording in bulk what nobody can explain
is worse than not porting the item — the spec names this as the point to reconsider staging.

- [ ] **Step 6: Justify, record, verify**

Only after classification: `scripts/combat-golden.sh record`, then `git status --short tests/golden-combat/`
must match your enumeration exactly. Re-run `check`.

- [ ] **Step 7: Commit** with the per-class justification in the body (shared mechanism once, then
      per-fixture evidence), ending:
```
Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
```

---

## Task 3: `PruneEscapedHostiles` on the perception model

**Files:** `src/Hexwaste.Formats/Combat/CombatEngine.cs` (~`:2361`), plus tests

- [ ] **Step 1: Test first.** A hostile that fled but retains a **living** `whoHitMe` is **not** pruned (it
  still has a danger source — this is exactly what the existing deferral note at `:2353-2360` warns about);
  one whose danger source is gone **is** pruned. Confirm both fail pre-change.

- [ ] **Step 2: Implement.** Replace the flat `SightRangeHexes` test with "no danger source", i.e.
  `DangerSource(h) is null`, matching `_combatai_want_to_stop`'s `enemy == nullptr || !isWithinPerception`
  (`combat_ai.cc:3227-3228`). Verify that line number. **Delete the deferral note** — it describes a
  deferral that no longer exists — and replace it with the port citation.

- [ ] **Step 3: Measure, classify, justify, record** exactly as Task 2. This delta is conceptually distinct
  (who *leaves* combat, not who is *targeted*), which is why it is a separate commit — keep it that way even
  if both suites move.

- [ ] **Step 4: Commit** with its own justification.

---

## Task 4: Backlog reconciliation

**Files:** `docs/BACKLOG.md`

- [ ] **Step 1:** Close the A2 re-record-tier entry for `_ai_danger_source` + `PruneEscapedHostiles`, with
  all commit SHAs and the complete list of re-recorded fixtures.
- [ ] **Step 2:** Record both marker decisions — CE excluded, SFALL ported — and their precedent, so the
  next reader does not re-litigate them.
- [ ] **Step 3:** Record any carried divergence the implementer flagged (`_dudeSpectator`, `_combat_check_bad_shot`
  coverage, anything else).
- [ ] **Step 4:** Check numbering against existing entries (F1-F21 are taken) and state what you chose.
- [ ] **Step 5:** Commit.

---

## Self-review notes

- **Attribution inside a single sub-project** is the whole reason for the three-commit split: the owner chose
  one sub-project over staging, and the measurement between commits is what keeps each delta explainable.
- **Task 1 is inert by construction** — new file, no callers — so it is reviewable on its own and its
  "nothing moves" check is meaningful rather than ceremonial.
- **Highest risk is Task 2 Step 5.** This is the first item in the whole arc genuinely expected to move many
  fixtures, and bulk-recording an unclassified set would undo the discipline the previous six sub-projects
  established. The stop condition is stated twice, deliberately.
- **Known soft spot:** `_combat_check_bad_shot`'s Hexwaste coverage is unknown at plan time; Task 2 Step 3
  requires the implementer to establish it rather than assume, and to report what is missing.
