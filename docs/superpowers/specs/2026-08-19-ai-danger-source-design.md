# Sub-project 7: `_ai_danger_source` — enemy target selection (the last big re-record item) — design spec (2026-08-19)

Port `_ai_danger_source` (`combat_ai.cc:1529-1705`) and the perception-based `PruneEscapedHostiles`
it unblocks. This is the largest remaining item in the re-record tier and the **only one still
expected to move fixtures broadly**, because it changes *ordinary* enemy target selection rather than
an exceptional branch.

Scope was chosen by the project owner: **one sub-project, full port**, over a staged alternative. The
attribution risk of a single large re-record was raised and accepted. This spec mitigates it by
requiring **separate commits with a fixture measurement between each**, inside the one branch — the
structure F11-F13 used successfully.

## Grounding — verified against `e97087b` on 2026-08-19

### The function's shape

```
_ai_danger_source(a1):
  if a1 is a PARTY MEMBER:
      ignoreFleeingCritters = disposition in {custom, coward, defensive, aggressive}
          ... but forced false when aiGetDistance(a1) == DISTANCE_CHARGE
      attackWho = aiGetAttackWho(a1)
      switch attackWho:
        WHOMEVER_ATTACKING_ME -> [candidate search; see "The CE block" below] ; return if found
        STRONGEST / WEAKEST / CLOSEST -> a1->whoHitMe = nullptr    (:1642 — CLEARS whoHitMe)
        default -> nothing
  else:
      attackWho = -1                                              (:1648)

  whoHitMe = a1->whoHitMe
  if whoHitMe is null or == a1:  targets[0] = null
  elif whoHitMe is ALIVE:
      if attackWho == WHOMEVER or attackWho == -1:  return whoHitMe   (:1657 — EARLY RETURN)
  else (whoHitMe dead):
      targets[0] = (different team) ? _ai_find_nearest_team(a1, whoHitMe, 1) : null

  aiFindAttackers(a1, &targets[1], &targets[2], &targets[3])        (:1668)
  if ignoreFleeingCritters: null out any target where critterIsFleeing
  sort targets[0..3] by STRENGTH / WEAKNESS / DISTANCE per attackWho
  for each of the 4 in order:
      if candidate != null AND isWithinPerception(a1, candidate):
          if pathfinderFindPath(a1, a1->tile, candidate->tile, nullptr, 0, _obj_blocking_at) != 0
             OR _combat_check_bad_shot(...) == COMBAT_BAD_SHOT_OK:
              return candidate
  return nullptr
```

**The single most important fact for scoping:** for an ordinary (non-party) enemy with a *living*
`whoHitMe`, the function returns it at `:1657` — **with no perception check and no reachability
check**. The perception and pathfinding gates apply only to the fallback path, i.e. when `whoHitMe`
is null or dead. Hexwaste's common case is therefore already close; the divergence concentrates in
the fallback.

This also explains, precisely, why the existing `PruneEscapedHostiles` deferral note
(`CombatEngine.cs:2353-2360`) is right that a naive perception prune is wrong: a fled hostile keeps
its `whoHitMe`, so it still has a danger source and still wants to fight.

### Two markers in this code, and what precedent says about each

`e97087b` is pinned as authoritative *for vanilla*, but this function contains two blocks that are
explicitly **not** vanilla. Both decisions below follow existing precedent in this repo rather than
new policy:

- **`// CE:` at `:1565`** — "Slightly improve 'Whomever is attacking me' targeting", which first tries
  to continue attacking the previous target before falling back. **EXCLUDED.** CLAUDE.md names CE QoL
  as out of scope, and no `// CE:` block has ever been ported here.
  **The vanilla path is recoverable without guessing:** the block is purely additive — it sets
  `candidate`, and is followed by `if (candidate == nullptr) { …fallback… }`. Omitting it leaves
  exactly the vanilla fallback. (The reference clone's history is truncated, so the pre-CE revision
  cannot be diffed; the structural argument is what makes this safe rather than a guess.)
- **`// SFALL:` at `:1483`** (inside `aiFindAttackers`) — adds `continue` so one candidate cannot be
  reported in more than one category. **PORTED.** Precedent: `EventQueue.cs` cites "queue.cc SFALL
  multi-event dedup" and `AiBestWeapon.cs` cites an SFALL midpoint, so SFALL-marked fixes inside
  `e97087b` are already treated as baseline here.

### What Hexwaste already has

- `PerceptionDetect.IsWithinPerception` — ported (`combat_ai.cc:3499`).
- `CompanionAi` — `AttackWho`/`Disposition` presets and `PickTarget`, with the rating-based
  Strongest/Weakest comparators and **vanilla's inverted-comparator quirk deliberately preserved**
  (shipped in the 2026-08-11 batch). Do not "correct" that quirk.
- **`FLEEING` / `DISENGAGING` maneuver flags actually set by the engine** — shipped by F1
  (`57e6ce6`). `critterIsFleeing` therefore has a real Hexwaste counterpart for the first time; before
  F1 the flags were script-only, which is part of why this item was deferred.
- Applying `AttackWho` to companions only is **faithful, not a gap** — the reference gates the whole
  switch on `objectIsPartyMember` (`:1541`) and takes `attackWho = -1` otherwise (`:1648`). Do not
  "fix" it.
- `PruneEscapedHostiles` (`CombatEngine.cs:2361`) currently drops living hostiles beyond a flat
  `CombatRules.SightRangeHexes` from the dude's team.

### Helpers that must be ported

- `aiFindAttackers` (`:1457`) — fills three target slots by distance-sorted scan: who is attacking me,
  who is attacking a friend, who was hit by a friend. Includes the SFALL `continue`.
- `_ai_find_nearest_team` (`:1397`) — nearest living critter on (flag 0x01) the same or (flag 0x02) a
  different team as `a2`, distance-sorted from `a1`.
- The three sort orders — distance, strength, weakness. Strength/weakness key on `_combatai_rating`,
  which Hexwaste already has as `AiRating`.
- `_combat_check_bad_shot` — Hexwaste has partial bad-shot logic; the implementer must establish what
  exists and what is missing rather than assuming either.

**The implementer must read `combat_ai.cc:1529-1705`, `:1397-1425` and `:1457-1528` directly.** This
spec deliberately does not reproduce 176 lines of reference; it states the decisions, not the code.

## Scope — one branch, sequenced commits

Attribution is preserved by ordering, since a single combined re-record was chosen:

1. **The helpers, inert.** `aiFindAttackers`, `_ai_find_nearest_team`, the sort orders — pure units,
   unit-tested, called by nothing yet. Must move no fixture.
2. **`_ai_danger_source` itself**, wired into enemy target selection. The behavioural change. Takes
   the bulk of the re-record.
3. **`PruneEscapedHostiles`** switched to the perception model. Separate commit, separate measurement,
   because its delta is conceptually distinct (who *leaves* combat, not who is *targeted*).

A fixture measurement runs between each. If commit 1 moves anything, the "inert" claim is false and
that is a stop condition.

### Out of scope

- The `// CE:` targeting improvement (above).
- `_ai_danger_source`'s callers beyond target selection — the function is also consulted by
  `_combatai_want_to_fight` / `_combatai_want_to_stop`; wiring those is commit 3's business only
  insofar as `PruneEscapedHostiles` needs it. Do not refactor the exit gate wholesale.
- Anything in the remaining tier (F15, F16, F17, F19, F20, `_combat_safety_invalidate_weapon`).

## What carries the proof

Fixtures are records of consequences. Hermetic tests through `FakeCombatHost` are the proof, and each
must be **confirmed failing pre-change**:

1. **Living `whoHitMe` short-circuits.** A non-party enemy with a living `whoHitMe` targets it even
   when out of perception and unreachable — the `:1657` early return. This is the test that stops
   someone "helpfully" applying the perception gate everywhere.
2. **Dead `whoHitMe` falls through** to `_ai_find_nearest_team` on the other team.
3. **`aiFindAttackers` categorisation**, including the SFALL rule that one candidate cannot occupy two
   slots.
4. **The perception gate applies to the fallback only** — a fallback candidate outside perception is
   skipped, and the next viable one is taken.
5. **The reachability disjunction** — an unreachable candidate is still selected if the shot is legal
   (`OR`, not `AND`; getting this backwards silently narrows targeting).
6. **Party-member gating** — a non-party critter never consults `AttackWho`, and the
   STRONGEST/WEAKEST/CLOSEST branch clearing `whoHitMe` (`:1642`) applies to party members only.
7. **`PruneEscapedHostiles`** — a hostile that fled but retains a living `whoHitMe` is *not* pruned;
   one that genuinely lost its danger source is.

## Fixture expectations

**Expect a large failing set, across both suites.** This changes ordinary target selection. Unlike the
four items that were predicted to move fixtures and did not, this one has no narrow branch to hide in.

Protocol, per commit:
1. `check` first, enumerate every failure.
2. Classify each: a changed *target choice* and its consequences is the expected class. A delta with
   no target change behind it is stop-and-investigate.
3. Justify, then record, then confirm `git status` matches the enumeration.

**If the failing set is so large that per-fixture classification is impractical, stop and report
rather than recording in bulk.** That is the point at which the staged alternative should be
reconsidered — a re-record nobody can explain is worth less than the port.

## Definition of done

The three commits landed with a measurement between each; the CE block excluded and the SFALL fix
included, both with their reasoning in comments; seven hermetic tests green and confirmed failing
pre-change; every re-recorded fixture enumerated and classified; all four suites green;
`docs/BACKLOG.md` reconciled, closing the A2 re-record-tier entry for this item.

**Or:** the failing set proved too large to classify honestly, and the work stopped for re-scoping.
