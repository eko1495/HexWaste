# The phantom flee — porting `pathfinderFindPath`'s `a5` destination check (F18) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `TryFlee` proposing retreat tiles the walker will refuse, by porting the reference's
`a5` "destination must be unblocked" mode — so a logged flee is a flee that happens.

**Architecture:** One optional parameter on `Pathfinder.FindPath` (default reproduces today's
behaviour, so all eight call sites stay inert) plus one opt-in at `TryFlee`. No new type, no host
seam, no signature change elsewhere.

**Tech Stack:** C# / .NET 10 (`net10.0`), xUnit. Reference: `reference/fallout2-ce` at `e97087b`
(gitignored clone).

## Global Constraints

- **Never copy, embed, or commit game assets.** `.gitignore` excludes `*.dat`, `*.map`, `*.frm`,
  `*.pal`, `game-data/`.
- **Port from `reference/fallout2-ce`, never guess.** Every behavioural change carries a comment
  citing its source file and function. If a detail cannot be confirmed, **stop and ask**.
- **`alexbatalov e97087b` is authoritative for vanilla.** `community/main` is a bug-fix candidate
  source only; cite ported fork fixes as `(community fix #NNN)`.
- **VERIFY EVERY LINE NUMBER YOU CITE**, in the reference *and* in Hexwaste. On a recent branch three
  reference citations were wrong, and on another the Hexwaste line numbers were stale because they had
  been read on a different branch. A line number read elsewhere is not a line number.
- **No new dependencies.** `src/Hexwaste.Formats` stays free of MonoGame references.
- **Golden-net discipline:** the scripts run the *prebuilt* binary. Never run two nets concurrently,
  never background one, **never build while one is running**. The scripts print no fixture count —
  report exactly what they print.
- Every regression test must be **confirmed to fail against the pre-change code**.

---

## Reference facts

Verified against `e97087b` on 2026-08-15.

- `_ai_run_away`'s retreat search calls `_make_path(a1, a1->tile, destination, nullptr, 1)`
  (`combat_ai.cc:1192`) — note the final argument **1**.
- `_make_path` is `pathfinderFindPath(object, from, to, rotations, a5, _obj_blocking_at)`
  (`animation.cc:1711`).
- `pathfinderFindPath` opens (`animation.cc:1716-1722`):
  ```c
  if (a5) {
      if (callback(object, to, object->elevation) != nullptr) {
          return 0;
      }
  }
  ```
  So with `a5` set, a **blocked destination yields no path at all**, before any search — which is why
  vanilla's retreat loop shrinks its distance until it finds a free tile.
- `_ai_move_away` also passes `a5 = 1` (`combat_ai.cc:1239`). It is **out of scope** here; Task 3
  records it.

## Hexwaste facts (read on THIS branch — re-verify before relying on them)

- `Pathfinder.FindPath(int from, int to, Func<int,bool> isBlocked, Func<int,bool>? isPassableDoor = null)`
  lives at `src/Hexwaste.Formats/Hex/Pathfinder.cs:22`.
- The goal exemption is the `neighbor != to` term at `Pathfinder.cs:48`.
- **The class doc comment (`Pathfinder.cs:3-10`) asserts "The goal tile itself is never
  blocking-checked … matching the original."** That claim is half wrong — it matches the original only
  when `a5 = 0` — and correcting it is part of this task.
- `CombatEngine` call sites on this branch: `:3022` (enemy approach), **`:3092` (TryFlee — the one to
  change)**, `:3262` (ally move). Others live in `DudeController.cs` (`:62`, `:83`, `:161`) and
  `ViewerGame.cs:5236`.
- Existing pathfinder tests are in `tests/Hexwaste.Formats.Tests/HexGridTests.cs` (`FindsStraightPath`,
  `WalksAroundObstacles`, `ReturnsNullWhenWalledIn`, and a passable-door test at ~`:137`). They use a
  local helper `private static int Tile(int x, int y) => y * HexGrid.Width + x;` at `:86`. Put the new
  unit tests beside them and use that helper.

---

## Task 1: Port the `a5` destination check into `Pathfinder`

**Files:**
- Modify: `src/Hexwaste.Formats/Hex/Pathfinder.cs`
- Test: `tests/Hexwaste.Formats.Tests/HexGridTests.cs`

**Interfaces:**
- Produces: `Pathfinder.FindPath(int from, int to, Func<int,bool> isBlocked, Func<int,bool>? isPassableDoor = null, bool requireFreeDestination = false)`.
  The new parameter is **last** and defaults to `false`, so every existing call site compiles and
  behaves exactly as before. Task 2 consumes it.

- [ ] **Step 1: Write the three unit tests**

Add to `HexGridTests.cs`, beside the existing pathfinder tests:

```csharp
    [Fact]
    public void BlockedDestinationStillPathsWithoutTheFlag()
    {
        // Inertness guarantee for the seven call sites that do NOT opt in: the goal tile stays exempt
        // from the blocked test by default (Pathfinder.cs `neighbor != to`), which is what lets a
        // melee approach path onto an occupied tile. This is the reference's a5 = 0 behaviour.
        int from = Tile(100, 100);
        int to = HexGrid.TileInDirection(HexGrid.TileInDirection(from, 2), 2);

        Assert.NotNull(Pathfinder.FindPath(from, to, tile => tile == to));
    }

    [Fact]
    public void BlockedDestinationYieldsNoPathWhenTheDestinationMustBeFree()
    {
        // ported from fallout2-ce src/animation.cc pathfinderFindPath (:1716-1722): with a5 set, a
        // blocked destination returns 0 BEFORE any search. _ai_run_away's retreat search passes
        // a5 = 1 (combat_ai.cc:1192), which is how vanilla shrinks its retreat distance until it
        // finds a genuinely free tile.
        int from = Tile(100, 100);
        int to = HexGrid.TileInDirection(HexGrid.TileInDirection(from, 2), 2);

        Assert.Null(Pathfinder.FindPath(from, to, tile => tile == to, null, requireFreeDestination: true));
    }

    [Fact]
    public void APassableClosedDoorAtTheDestinationIsNotABlocker()
    {
        // P109: a closed door the walker may open is not a blocker. The new destination check must
        // honour the same exemption the intermediate-tile check does, or flee-through-doors regresses.
        int from = Tile(100, 100);
        int to = HexGrid.TileInDirection(HexGrid.TileInDirection(from, 2), 2);

        Assert.NotNull(Pathfinder.FindPath(from, to, tile => tile == to, tile => tile == to,
            requireFreeDestination: true));
    }
```

**Verify the geometry** rather than assuming it: confirm `to` is reachable from `from` in the empty
case (the existing `FindsStraightPath` uses the same construction, so it should be) and that the only
blocked tile in each test is the destination itself. If `to` is adjacent to `from` in a way that makes
the test vacuous, pick a longer path and say why.

- [ ] **Step 2: Run them to verify the first passes and the other two FAIL**

Run:
```bash
dotnet test --filter "FullyQualifiedName~BlockedDestination|FullyQualifiedName~APassableClosedDoorAtTheDestination"
```
Expected: `BlockedDestinationStillPathsWithoutTheFlag` **passes** (it pins today's behaviour and is
correctly green on both sides — it is an inertness pin, not a regression test). The other two **fail
to compile or fail outright**, because the parameter does not exist yet. A compile failure is an
acceptable "fails first" here — but once it compiles, confirm
`BlockedDestinationYieldsNoPathWhenTheDestinationMustBeFree` genuinely fails against the unmodified
logic before you make it pass.

- [ ] **Step 3: Implement the parameter**

In `Pathfinder.cs`, add the parameter and the pre-search check. The check goes **before** the search
loop, mirroring the reference's early return rather than being folded into the loop condition:

```csharp
    /// <param name="requireFreeDestination">ported from fallout2-ce src/animation.cc
    /// pathfinderFindPath (:1716-1722): the reference's `a5` argument. When set, a BLOCKED
    /// destination yields no path at all — the function returns 0 before searching. Callers that
    /// mirror `_make_path(..., 1)` pass true; the default false reproduces `a5 = 0`, under which the
    /// goal tile is exempt from the blocked test so a path can end on an occupied tile (a melee
    /// approach). F18: TryFlee needed this, because a retreat onto a blocked tile is refused by the
    /// walker and the flight silently never happens.</param>
    public static byte[]? FindPath(int from, int to, Func<int, bool> isBlocked,
        Func<int, bool>? isPassableDoor = null, bool requireFreeDestination = false)
    {
        if (!HexGrid.IsValid(from) || !HexGrid.IsValid(to) || from == to)
            return null;

        // ported from fallout2-ce src/animation.cc pathfinderFindPath (:1718-1722): `if (a5) { if
        // (callback(object, to, elevation) != nullptr) return 0; }` — the destination's own blocker is
        // tested up front. The door exemption applies here exactly as it does to intermediate tiles.
        if (requireFreeDestination && isBlocked(to) && !(isPassableDoor?.Invoke(to) ?? false))
            return null;
```

Then correct the class doc comment (`Pathfinder.cs:3-10`), whose current sentence — "The goal tile
itself is never blocking-checked (so paths can end next to/at an occupied target), matching the
original" — is only true of `a5 = 0`. State that the reference takes an `a5` flag, that the default
reproduces `a5 = 0`, and that `requireFreeDestination` reproduces `a5 = 1`.

- [ ] **Step 4: Run the tests**

Run: `dotnet test`
Expected: **0 failed.** The three new tests pass and no existing pathfinder test changes outcome — if
one does, the default is not inert and that is a stop condition, not something to adjust the test for.

- [ ] **Step 5: Commit**

```bash
git add src/Hexwaste.Formats/Hex/Pathfinder.cs tests/Hexwaste.Formats.Tests/HexGridTests.cs
git commit -m "feat: port pathfinderFindPath's a5 destination check (F18, part 1)

Pathfinder.FindPath gains requireFreeDestination, the reference's a5
argument (animation.cc:1716-1722): with it set, a blocked destination
yields no path before any search. Defaults to false = a5 = 0, the goal
exemption we have always had, so all eight call sites are inert.

Also corrects the class doc, which claimed the unconditional goal
exemption matched the original — it matches only a5 = 0.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: `TryFlee` opts in, and the fixture is re-recorded

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` — the `TryFlee` retreat search (`:3092`)
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`
- Re-record (expected): `tests/golden-combat/denbus2-fight-flee.txt`

**Interfaces:**
- Consumes: `Pathfinder.FindPath(..., requireFreeDestination: true)` from Task 1.

- [ ] **Step 1: Write the engine-level test**

Add near the other flee tests in `CombatEngineTests.cs`:

```csharp
    [Fact]
    public void AFleeingCritterNeverLogsAFleeItDoesNotPerform()
    {
        // F18: TryFlee used to pick its retreat tile with a pathfinder that exempts the GOAL from the
        // blocked test, so it could propose an occupied tile; the walker then refused the move and the
        // transcript recorded a flight that never happened (denbus2-fight-flee logged the identical
        // 'flee: Cute Slave@11272 -> 10480' four times without the critter ever moving).
        // ported from fallout2-ce src/combat_ai.cc _ai_run_away (:1192): the retreat search passes
        // _make_path(..., a5 = 1), so a blocked candidate produces no path and the loop shrinks.
        // The invariant asserted here is the pairing that was broken: a 'flee:' line implies movement.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 5, ap: 10));
        host.AiPackets[enemy] = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 10, 10, "", "");

        // Occupy the full-AP retreat tile the search would otherwise choose, leaving nearer tiles free.
        int startTile = enemy.HexTile;
        int rotation = HexGrid.RotationTo(dude.HexTile, startTile);
        int fullDistanceTile = HexGrid.TileInDirection(startTile, rotation, 10);
        host.BlockedOverride = tile => tile == fullDistanceTile;

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        bool loggedFlee = host.Transcripts.Any(t => t.StartsWith("flee:"));
        bool actuallyMoved = enemy.HexTile != startTile;
        Assert.False(loggedFlee && !actuallyMoved, "a logged flee must correspond to an actual move");
    }
```

**Two things to resolve rather than assume:**
- `host.BlockedOverride` is a **guessed member name**. Find the real way `FakeCombatHost` reports a
  blocked tile (`IsBlocked`) and use it; if no hook exists, add one in the same style as the existing
  `BlockerOverride` used by the overshoot tests.
- The `Assert.False(loggedFlee && !actuallyMoved, …)` form passes if the critter neither logs nor
  moves, which would make it weak. **Strengthen it** once you know what the fixed engine actually does
  in this setup: if it retreats to a nearer tile, assert that positively (`Assert.True(actuallyMoved)`
  plus the flee line). Only fall back to the implication form if the correct behaviour genuinely is
  "no turn taken". Say in your report which case holds and why.

- [ ] **Step 2: Run it to verify it FAILS pre-change**

Run:
```bash
dotnet test --filter "FullyQualifiedName~AFleeingCritterNeverLogsAFleeItDoesNotPerform"
```
Expected: **FAIL** — today the engine logs the flee and does not move. If it passes, the setup is not
reproducing the bug; fix the setup before touching the engine, and report what you changed.

- [ ] **Step 3: Make `TryFlee` opt in**

At `CombatEngine.cs:3092`, add the argument to the existing call:

```csharp
                if (dest != fromTile && Pathfinder.FindPath(fromTile, dest, _host.IsBlocked,
                        t => _host.IsPassableClosedDoor(critter, t), requireFreeDestination: true) is not null)
```

and extend the comment immediately above the retreat search to cite
`_make_path(a1, a1->tile, destination, nullptr, 1)` (`combat_ai.cc:1192`) as the reason the
destination must be free — the retreat loop is meant to shrink until it finds one.

- [ ] **Step 4: Run the tests**

Run: `dotnet test`
Expected: **0 failed.** Pay attention to the other flee tests — if one now fails, the change reaches
further than intended and that is worth reporting before proceeding.

- [ ] **Step 5: Measure the fixture blast radius BEFORE recording**

Run: `scripts/combat-golden.sh check`

Expected: **`denbus2-fight-flee` fails; everything else passes.**

Stop conditions:
- **Nothing fails** → the fix is not reaching the fixture. Do not record. Report it — this has already
  happened twice in this arc, and both times the prediction rather than the code was wrong.
- **Anything else fails** → report the full list first.

- [ ] **Step 6: Construct the justification BEFORE recording**

Diff the failing fixture against the new output and write down:
- that the four identical `flee: Cute Slave@11272 -> 10480` lines (fixture lines 25, 39, 57, 75) are
  gone, and what replaced them — a retreat to a nearer free tile, or no flee line at all;
- which candidate tile was rejected as blocked, and what the loop chose instead;
- any knock-on change (rounds, survivors, winner) and why it follows.

**If that trace cannot be constructed, do not record.** Revert and report; F18 returns to deferred.

- [ ] **Step 7: Record and verify**

```bash
scripts/combat-golden.sh record
git status --short tests/golden-combat/
```
Only the fixtures your Step 5 measurement predicted may appear. Then re-run
`scripts/combat-golden.sh check` — expected ALL PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Hexwaste.Formats/Combat/CombatEngine.cs \
        tests/Hexwaste.Formats.Tests/CombatEngineTests.cs \
        tests/golden-combat/
git commit -m "fix: a logged flee is now a flee that happens (F18)

<the Step 6 trace goes in this body>

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Backlog reconciliation and the full nets

**Files:**
- Modify: `docs/BACKLOG.md`

- [ ] **Step 1: Move F18 to shipped**

With the commit SHAs from Tasks 1 and 2, what was ported (the `a5` destination check and `TryFlee`'s
opt-in), and the fixtures that were **deliberately re-recorded**, listing exactly which.

- [ ] **Step 2: Add the new entry for the unaudited `a5` call sites**

Hexwaste's `FindPath` modelled only `a5 = 0` until now, and the reference passes `a5 = 1` at other
sites — **`_ai_move_away` (`combat_ai.cc:1239`) is the known next case**. The other seven Hexwaste
call sites (`CombatEngine.cs:3022` enemy approach and `:3262` ally move; `DudeController.cs:62/83/161`;
`ViewerGame.cs:5236`) have never been checked against their reference counterparts. Mark it re-record
tier — changing any of them moves movement transcripts.

- [ ] **Step 3: Record the rejected alternative**

Note in the F18 entry that moving the `flee:` transcript line to after a successful `StartWalk` was
considered and deliberately **not** done: it treats the symptom rather than the cause, and doing both
would make the fixture delta impossible to attribute. This matters because it is the obvious fix and
the next reader will wonder why it was not taken.

- [ ] **Step 4: Run the remaining nets**

**One at a time. Nothing else may build while these run.**

```bash
scripts/quest-golden.sh check
scripts/encounter-golden.sh check
```
Quest: expected ALL PASS. Encounter: `brawl-watch` is a plausible mover. If it moves, **do not record
it here** — stop and report with the delta so it is justified on the same terms as Task 2's.

- [ ] **Step 5: Commit**

```bash
git add docs/BACKLOG.md
git commit -m "docs: reconcile F18 as shipped and record the unaudited a5 call sites

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:** the `a5` port → Task 1; `TryFlee` opt-in → Task 2 Step 3; the four proof
  obligations → Task 1 Steps 1 (unit rule, door, inertness) and Task 2 Step 1 (engine-level pairing);
  the fixture protocol → Task 2 Steps 5-7; docs, the rejected alternative and the follow-up entry →
  Task 3.
- **Deliberate task split:** Task 1 is pure and testable without any combat harness, and its default
  is inert — so it can be reviewed on its own merits before anything behavioural depends on it.
- **Known soft spots, flagged inline:** `BlockedOverride` is a guessed member name, and Task 2's
  assertion is written in a weak implication form that the implementer is instructed to strengthen
  once the correct post-fix behaviour is known. Both are named rather than hidden.
