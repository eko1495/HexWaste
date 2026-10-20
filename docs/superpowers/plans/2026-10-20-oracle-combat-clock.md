# Oracle F19 Combat Clock Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Combat adds 5 s (50 ticks) of game time per round, as fo2ce's `_combat_sequence` does, and the game-time events that come due fire mid-fight, so the arcaves-ambush oracle fixture moves past trace line 41.

**Architecture:** `CombatEngine.StartNewRound` calls a new default-no-op host hook `ICombatHost.AddGameTime(int ticks)` after the round-end re-sequence and before `BuildTurnOrder`. The viewer bumps `_clock.Ticks`; the same frame's `UpdateClock` (which runs after `_combat.Step()` in every pump) fires due timers, poison, drugs, the map-update event and the rest. `ProcessMapUpdateEvent` drops its combat gate.

**Tech Stack:** C# / .NET 10, xUnit, the fo2ce trace oracle (`scripts/oracle-run.sh`), golden suites (`scripts/*-golden.sh`).

Spec: `docs/superpowers/specs/2026-10-20-oracle-combat-clock-design.md` (fo2ce behavior and line references).

## Global Constraints

- Port from fallout2-ce with `// ported from fallout2-ce src/x.cc f()`; alexbatalov e97087b (`reference/fallout2-ce`) is authoritative.
- A known limit gets a code comment in the form `shortcut: <the limit>, <when to upgrade>`.
- CI ascii-lint: no non-ASCII characters in C# string literals (prefer ASCII in new comments too).
- No new dependencies beyond MonoGame, xUnit, ImageSharp. Never copy or commit game assets.
- Commit trailers on every commit, exactly:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk
  ```
- Work stays on branch `feat/oracle-combat-clock`.
- Unit tests: `dotnet test tests/Hexwaste.Formats.Tests -c Release` (tests needing game files skip without `FALLOUT2_DIR`).
- Run golden suites and the oracle one at a time, never in parallel. Kill leftovers with `pkill -9 -f build-oracle/fallout2-ce` as its own command (inside a compound command it kills the shell).
- Per round: exactly 50 ticks (`TicksPerRound`). No call at combat start (round 1 begins without it, as in `_combat_sequence_init`).

---

### Task 1: The engine hook and its unit tests

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/ICombatHost.cs` (next to `SetReaction`, line ~219)
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` (`StartNewRound` ~3456; the `_events`/`_combatTick` doc comment ~80-89)
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs` (new tests next to `KillingTheLastHostileMidRoundStillRunsTheJoinPass_AJoinerKeepsCombatGoing` ~1711; the `FakeCombatHost` ~4795)

**Interfaces:**
- Produces: `void ICombatHost.AddGameTime(int ticks) { }` (default no-op). Called once per round transition from `StartNewRound` with `ticks = 50`.

- [ ] **Step 1: Record the calls in the fake host** — in `FakeCombatHost`, right after the `SetReaction` line (`public void SetReaction(MapObject critter, int value) => Reactions.Add((critter, value));`), add:

```csharp
        // F19: each AddGameTime call, with how many combat_p_proc calls had run by then (orders it vs the joins).
        public readonly List<(int Ticks, int ProcCallsBefore)> GameTimeAdds = [];
        public Action? OnAddGameTime { get; set; }
        public void AddGameTime(int ticks)
        {
            GameTimeAdds.Add((ticks, CombatProcCalls.Count));
            OnAddGameTime?.Invoke();
        }
```

- [ ] **Step 2: Write the failing tests** — after `KillingTheLastHostileMidRoundStillRunsTheJoinPass_AJoinerKeepsCombatGoing`, add:

```csharp
    [Fact]
    public void RoundTransitionAddsFiveSecondsAfterTheJoinPassAndBeforeTheNextRound()
    {
        // ported from fallout2-ce src/combat.cc _combat_sequence(): gameTimeAddSeconds(5) after _combat_add_noncoms
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject attacker = host.AddCritter(NewCritter(tile: 20102, hp: 30, ap: 10));
        attacker.Team = 1;
        MapObject candidate = host.AddCritter(NewCritter(tile: 20050, hp: 30, ap: 10));
        candidate.Team = 1;
        candidate.Sid = 7;
        candidate.DamageLastTurn = 4; // _combatai_want_to_join: damaged -> joins at the round-1 end
        var engine = new CombatEngine(host, new MinRng());
        CombatPhase phaseAtAdd = CombatPhase.Idle;
        int roundAtAdd = 0;
        host.OnAddGameTime = () => { phaseAtAdd = engine.Phase; roundAtAdd = engine.Round; };

        engine.BeginScriptAggro(attacker, dude);
        for (int i = 0; i < 20 && engine.Phase != CombatPhase.PlayerTurn; i++) { host.Animating.Clear(); engine.Step(); }
        Assert.Equal(CombatPhase.PlayerTurn, engine.Phase);
        Assert.Empty(host.GameTimeAdds); // combat start (_combat_sequence_init) adds no time

        engine.EndPlayerTurn();
        for (int i = 0; i < 20 && engine.Phase is not (CombatPhase.PlayerTurn or CombatPhase.Idle); i++) { host.Animating.Clear(); engine.Step(); }

        Assert.Equal(2, engine.Round);
        var add = Assert.Single(host.GameTimeAdds);
        Assert.Equal(50, add.Ticks);
        Assert.Equal(2, roundAtAdd);                          // inside the round transition
        Assert.NotEqual(CombatPhase.PlayerTurn, phaseAtAdd);  // before the dude's round-2 turn opens
        int lastJoinCheck = host.CombatProcCalls.FindLastIndex(c => c.Critter == candidate && c.FixedParam == 5);
        Assert.True(lastJoinCheck >= 0 && lastJoinCheck < add.ProcCallsBefore); // the join pass ran first
    }

    [Fact]
    public void TheFinalRoundTransitionAddsTimeBeforeTheEndCheck()
    {
        // _combat_sequence (and its gameTimeAddSeconds) runs before _combat_should_end ends the fight
        var (engine, host, _) = KillOnlyHostileOnDudeTurn(candidateJoins: false);
        Assert.Equal(CombatPhase.Idle, engine.Phase);
        Assert.Equal(new[] { 50 }, host.GameTimeAdds.Select(a => a.Ticks));
    }

    [Fact]
    public void BrawlRoundsAddFiveSecondsEach()
    {
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        int aTile = HexGrid.TileInDirection(20100, 0, 8);
        MapObject teamA = host.AddCritter(NewCritter(aTile, hp: 30, ap: 10));
        MapObject teamB = host.AddCritter(NewCritter(HexGrid.TileInDirection(aTile, 0), hp: 30, ap: 10));
        teamA.Team = 1;
        teamB.Team = 2;
        var engine = new CombatEngine(host, new MinRng());

        engine.StartBrawl([teamA, teamB]);
        Assert.Empty(host.GameTimeAdds);
        engine.EndPlayerTurn();
        for (int i = 0; i < 200 && engine.Phase == CombatPhase.EnemyTurn; i++) { host.Animating.Clear(); engine.Step(); }

        Assert.True(engine.Round >= 2);
        Assert.Equal(engine.Round - 1, host.GameTimeAdds.Count);
        Assert.All(host.GameTimeAdds, a => Assert.Equal(50, a.Ticks));
    }
```

`KillOnlyHostileOnDudeTurn` already exists (~line 1683) and returns `(engine, host, candidate)`. If the brawl test's fight ends at a transition (`Phase` becomes `Idle`), the count is still `Round - 1`, because every transition, including the final one, runs `StartNewRound`.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Hexwaste.Formats.Tests -c Release --filter "FullyQualifiedName~RoundTransitionAddsFiveSeconds|FullyQualifiedName~TheFinalRoundTransitionAddsTime|FullyQualifiedName~BrawlRoundsAddFiveSeconds"`
Expected: FAIL. `GameTimeAdds` stays empty, because the engine never calls `AddGameTime`. The fake's method compiles even before the interface has the member.

- [ ] **Step 4: Add the interface member** — in `ICombatHost.cs`, right after `void SetReaction(MapObject critter, int value) { }`:

```csharp
    /// <summary>fo2ce gameTimeAddSeconds(): advance the game clock by <paramref name="ticks"/> (10 per second).
    /// Called once per combat round transition. Default no-op so the fake test host needs no override.</summary>
    void AddGameTime(int ticks) { }
```

- [ ] **Step 5: Call it from `StartNewRound`** — replace the body's tail so it reads:

```csharp
        if (_combatList.Count > 0)
            SequenceCombatList(); // the join pass already ran (RunJoinPass, from StepTurnOrder)
        else
            AddJoiners(); // StartBrawl fights
        // ported from fallout2-ce src/combat.cc _combat_sequence(): gameTimeAddSeconds(5), last, before _combat_should_end
        // shortcut: in combat fo2ce drains the queue only on a 100 ms background tick, so a due event can fire a few frames later than here, and for an NPC that opens a round fo2ce's first background pass may come before or after its first action; upgrade if a trace shows due events landing elsewhere in a round
        _host.AddGameTime(TicksPerRound);
        BuildTurnOrder(firstRound: false, null, null);
```

Also update the `StartNewRound` summary: append "then advance the game clock 5 s (F19)" after "re-sequence the fo2ce list (StartBrawl: let joiners in)".

- [ ] **Step 6: Fix the stale `_combatTick` doc comment** (~line 80-87). It says "mutating the saved clock mid-fight would churn day-boundary state"; that is no longer the design. Replace that summary with:

```csharp
    /// <summary>The knockout-wake event queue + a combat-owned monotonic tick that advances
    /// <see cref="TicksPerRound"/> per round (P14-M2), in step with the game clock (F19,
    /// <see cref="ICombatHost.AddGameTime"/>). The KO delay (10*(35-3*EN)) and EN scaling stay exact.</summary>
    // shortcut: knockout wakes run on the engine's own combat tick, not as fo2ce's EVENT_TYPE_KNOCKOUT queue event on the game clock; both advance 50 per round, upgrade if a trace orders a wake against a timer
```

- [ ] **Step 7: Run the tests to verify they pass, then the whole unit suite**

Run: `dotnet test tests/Hexwaste.Formats.Tests -c Release --filter "FullyQualifiedName~RoundTransitionAddsFiveSeconds|FullyQualifiedName~TheFinalRoundTransitionAddsTime|FullyQualifiedName~BrawlRoundsAddFiveSeconds"`
Expected: 3 passed.
Run: `dotnet test tests/Hexwaste.Formats.Tests -c Release`
Expected: all pass (1125 + 3 pass, 98 skip without `FALLOUT2_DIR`).

- [ ] **Step 8: Commit**

```bash
git add src/Hexwaste.Formats/Combat/ICombatHost.cs src/Hexwaste.Formats/Combat/CombatEngine.cs tests/Hexwaste.Formats.Tests/CombatEngineTests.cs
git commit -m "feat(combat): add 5 s of game time per round via ICombatHost.AddGameTime" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk"
```

---

### Task 2: Viewer glue, the map-update gate, oracle, goldens, findings doc

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs` (`SetReaction` ~1445; `ProcessMapUpdateEvent` and its doc comment ~603-621)
- Modify: `docs/research-notes/oracle-m1-findings-2026-10.md` (F19 ~429; the update lines near the top ~28; "Recommended next steps" ~496)
- Modify (re-record only): fixtures under `tests/golden-*/` that move

**Interfaces:**
- Consumes: `ICombatHost.AddGameTime(int ticks)` from Task 1.

- [ ] **Step 1: Implement the host hook** — in `ViewerGame.CombatHost.cs`, right after the `SetReaction` method:

```csharp
    // ported from fallout2-ce src/scripts.cc gameTimeAddSeconds(); the same frame's UpdateClock (after Step) fires what came due
    public void AddGameTime(int ticks) => _clock.Ticks += ticks;
```

- [ ] **Step 2: Drop the combat gate in `ProcessMapUpdateEvent`** — the condition becomes:

```csharp
        if (_mapUpdateNextTick < 0 || _clock.Ticks < _mapUpdateNextTick || !MapUpdateAllowed()
            || _lootContainer is not null)
            return;
```

In its doc comment, replace "Not in combat (Hexwaste freezes the clock there; fo2ce adds 5 s per combat round, combat.cc:3071, and its queue keeps running - see the findings doc) or with a loot container open; a gated due event waits." with "Runs in combat too (mapUpdateEventProcess has no combat check; the clock moves 5 s per round, combat.cc:3071). Not with a loot container open; a gated due event waits." In the fourth shortcut comment above it, change "the dialog/combat/loot gates" to "the dialog/loot gates".

- [ ] **Step 3: CI-equivalent checks**

```bash
./scripts/ascii-lint.sh
dotnet build src/Hexwaste.Viewer -c Release
dotnet test tests/Hexwaste.Formats.Tests -c Release
```
Expected: clean lint, 0 build errors, all tests pass.

- [ ] **Step 4: Oracle** — one at a time:

```bash
scripts/oracle-run.sh arcaves-ambush > scratch/oracle-runs/arcaves-ambush-run.out 2>&1; tail -15 scratch/oracle-runs/arcaves-ambush-run.out
scripts/oracle-run.sh artemple-idle > scratch/oracle-runs/artemple-idle-run.out 2>&1; tail -5 scratch/oracle-runs/artemple-idle-run.out
scripts/oracle-run.sh klamath-hour > scratch/oracle-runs/klamath-hour-run.out 2>&1; tail -12 scratch/oracle-runs/klamath-hour-run.out
```
Expected: arcaves moves past line 41 (`t=302451 TURN obj#18000 ap=9` matches, and so do the round-2 `timed_event_p_proc` timers with their `RNG 3..5` draws); artemple IDENTICAL; klamath still at line 1664. Take the first divergence from the "DIVERGED at fo2ce line N" block, not a plain diff. If arcaves now diverges in where the round-2 timers fall relative to TURN/`combat_p_proc` lines, diagnose against `scratch/oracle-runs/arcaves-ambush/{fo2ce,hexwaste}.trace` and report it. Do not restructure the frame order; that is the recorded 100 ms shortcut.

- [ ] **Step 5: Goldens** — sequentially, saving outputs:

```bash
for s in combat quest endgame opening encounter; do scripts/$s-golden.sh > scratch/f19-golden-$s.out 2>&1; tail -3 scratch/f19-golden-$s.out; done
```
For each moved fixture: run the suite a second time to rule out the known encounter crash flake, then review the diff. The move must be explained by the clock advancing 50 ticks per round: a later clock or day in the output, a timer/poison/drug/withdrawal/radiation/map-update/charge/ambient event firing mid-fight, or RNG shifts downstream of one. Then `scripts/<suite>-golden.sh record`, re-check, and list each re-recorded fixture with its reason in the commit message. If a diff is not explained by that, stop and report it instead of recording.

- [ ] **Step 6: Findings doc** — F19: heading gets "— FIXED", plus a one-paragraph summary: `StartNewRound` calls `ICombatHost.AddGameTime(50)` after the re-sequence, before the end check; due queue events fire in the same frame's `UpdateClock`; the map-update event runs in combat; the two shortcuts (100 ms drain gate, KO wakes on the engine tick). Keep the original text under "Original finding:" as F20 does. Add an "Update after F19" line near the top and a numbered "After F19" entry under "Recommended next steps" with the three new first divergences.

- [ ] **Step 7: Commit** — the code + doc as `fix(oracle): F19 combat clock runs game-time events mid-fight; new first divergences`, and separately, if any, `test(golden): re-record <fixtures> for the 5 s combat round clock`, each with the trailers:

```bash
git commit -m "<subject>" -m "<body listing fixtures and reasons, if any>" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk"
```
