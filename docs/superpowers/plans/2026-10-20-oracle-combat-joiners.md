# Oracle F4/F5 Combat Opening and Joiners Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Hexwaste's combat opening, round-end joiners, per-turn `combat_p_proc` and taunt random draws match fo2ce, so the arcaves-ambush oracle fixture moves past trace line 9.

**Architecture:** `CombatEngine` gains fo2ce's ordered combat list (`_combatList` with `_listCom`/`_listNoncom`), built at combat start with `_combat_sequence_init`'s swaps and updated at round end by a resumable port of `_combat_add_noncoms` plus `_combat_sequence`. `_hostiles` and `_order` keep their roles and are fed from the list. `SetWhoHitMe` writes the reaction LVAR through a new host call; taunts get the missing types and draw sites through another.

**Tech Stack:** C# / .NET 10, xUnit, the fo2ce trace oracle (`scripts/oracle-run.sh`), golden suites (`scripts/*-golden.sh`).

Spec: `docs/superpowers/specs/2026-10-20-oracle-combat-joiners-design.md` (read it for the fo2ce behavior and line references).

## Global Constraints

- Port from fallout2-ce with `// ported from fallout2-ce src/x.cc f()`; alexbatalov e97087b (`reference/fallout2-ce`) is authoritative.
- A known limit gets a code comment in the form `shortcut: <the limit>, <when to upgrade>`.
- CI ascii-lint: no non-ASCII characters in C# string literals (comments may use them, but prefer ASCII in new code).
- No new dependencies beyond MonoGame, xUnit, ImageSharp. Never copy or commit game assets.
- Commit trailers on every commit:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk
  ```
- Work stays on branch `feat/oracle-combat-joiners`.
- Unit tests: `dotnet test tests/Hexwaste.Formats.Tests -c Release` (tests needing game files skip without `FALLOUT2_DIR`).
- Do not run golden suites or the oracle in parallel with each other; run them one at a time. Kill leftovers with `pkill -9 -f build-oracle/fallout2-ce`.

---

### Task 1: Taunt types and the always-drawn chance roll

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatTaunt.cs`
- Modify: `src/Hexwaste.Formats/Combat/AiPackets.cs` (the `AiPacket` record params near line 42-45 and `Flush()` near line 92)
- Test: `tests/Hexwaste.Formats.Tests/AiPacketTests.cs` (the P72-M3 taunt section, lines 167-215)

**Interfaces:**
- Produces: `CombatTaunt.Type { Attack, Run, Move, Miss, Hit }`; `CombatTaunt.Pick(AiPacket pkt, CombatTaunt.Type type, ICombatRng rng, int hitLocation = 3)`; `AiPacket` params `MoveStart`, `MoveEnd`, `MissStart`, `MissEnd`, `HitRanges` (`(int Start, int End)[]?`, indexed head 0, left arm 1, right arm 2, torso 3, right leg 4, left leg 5, eyes 6, groin 7).

- [ ] **Step 1: Write the failing tests** — in `AiPacketTests.cs`, replace `TauntPickSkipsWhenChanceIsZero` and add the new tests:

```csharp
    private sealed class CountingRng(params int[] v) : ICombatRng
    {
        public int Draws;
        public int Next(int lo, int hi) => Math.Clamp(v[Math.Min(Draws++, v.Length - 1)], lo, hi - 1);
    }

    [Fact]
    public void TauntPickDrawsTheChanceRollEvenWhenChanceIsZero()
    {
        // fo2ce _combatai_msg (combat_ai.cc:3324) always draws randomBetween(1,100); chance 0 then skips.
        var p = new AiPacket(8, "Scorpion", 0, 0, 0, "", "", Chance: 0,
            AttackStart: 50140, AttackEnd: 50159);
        var rng = new CountingRng(1, 50150);
        Assert.Equal(-1, CombatTaunt.Pick(p, CombatTaunt.Type.Attack, rng));
        Assert.Equal(1, rng.Draws);
    }

    [Fact]
    public void TauntFieldsForMoveMissAndHitParseFromAiTxt()
    {
        AiPacketTable t = AiPacketTable.Parse(
            "[Addict]\npacket_num=179\nchance=15\nmove_start=2020\nmove_end=2029\nmiss_start=2060\nmiss_end=2069\n"
            + "hit_head_start=2100\nhit_head_end=2101\nhit_left_arm_start=2110\nhit_left_arm_end=2111\n"
            + "hit_right_arm_start=2120\nhit_right_arm_end=2121\nhit_torso_start=2130\nhit_torso_end=2131\n"
            + "hit_right_leg_start=2140\nhit_right_leg_end=2141\nhit_left_leg_start=2150\nhit_left_leg_end=2151\n"
            + "hit_eyes_start=2160\nhit_eyes_end=2161\nhit_groin_start=2170\nhit_groin_end=2171\n");
        AiPacket p = t.Get(179)!;
        Assert.Equal((2020, 2029), (p.MoveStart, p.MoveEnd));
        Assert.Equal((2060, 2069), (p.MissStart, p.MissEnd));
        Assert.Equal(8, p.HitRanges!.Length);
        Assert.Equal((2130, 2131), p.HitRanges[3]);
        Assert.Equal((2170, 2171), p.HitRanges[7]);
    }

    [Fact]
    public void TauntPickUsesTheMoveMissAndHitRanges()
    {
        var p = new AiPacket(179, "Addict", 0, 0, 0, "", "", Chance: 15,
            MoveStart: 2020, MoveEnd: 2029, MissStart: 2060, MissEnd: 2069,
            HitRanges: [(2100, 2101), (2110, 2111), (2120, 2121), (2130, 2131),
                        (2140, 2141), (2150, 2151), (2160, 2161), (2170, 2171)]);
        Assert.Equal(2025, CombatTaunt.Pick(p, CombatTaunt.Type.Move, new CountingRng(1, 2025)));
        Assert.Equal(2061, CombatTaunt.Pick(p, CombatTaunt.Type.Miss, new CountingRng(1, 2061)));
        Assert.Equal(2161, CombatTaunt.Pick(p, CombatTaunt.Type.Hit, new CountingRng(1, 2161), hitLocation: 6));
        // no hit table (HitRanges null) -> empty range -> no taunt, chance roll only
        var bare = new AiPacket(1, "P", 0, 0, 0, "", "", Chance: 100);
        var rng = new CountingRng(1);
        Assert.Equal(-1, CombatTaunt.Pick(bare, CombatTaunt.Type.Hit, rng));
        Assert.Equal(1, rng.Draws);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Hexwaste.Formats.Tests -c Release --filter "FullyQualifiedName~AiPacketTests"`
Expected: build errors (`MoveStart`, `HitRanges`, `Type.Move` not defined).

- [ ] **Step 3: Add the `AiPacket` fields** — in `AiPackets.cs`, after the `RunStart`/`RunEnd` param:

```csharp
    /// <summary>ai.txt <c>move_start</c>/<c>move_end</c>, <c>miss_start</c>/<c>miss_end</c> and the eight
    /// <c>hit_&lt;location&gt;_start/_end</c> pairs (combat_ai.cc:421-443): the combatai.msg ranges for the
    /// MOVE, MISS and HIT taunts. HitRanges is indexed by fo2ce HIT_LOCATION_* (head 0 .. groin 7).</summary>
    int MoveStart = 0, int MoveEnd = -1, int MissStart = 0, int MissEnd = -1,
    (int Start, int End)[]? HitRanges = null,
```

and in `Flush()` after `RunStart: I("run_start"), RunEnd: I("run_end"),`:

```csharp
                    MoveStart: I("move_start"), MoveEnd: I("move_end"),
                    MissStart: I("miss_start"), MissEnd: I("miss_end"),
                    HitRanges: [.. HitLocationKeys.Select(k => (I($"hit_{k}_start"), I($"hit_{k}_end")))],
```

with, in the same class as `Parse`:

```csharp
    // fo2ce HIT_LOCATION_* order (combat_ai.cc:429-443)
    private static readonly string[] HitLocationKeys =
        ["head", "left_arm", "right_arm", "torso", "right_leg", "left_leg", "eyes", "groin"];
```

- [ ] **Step 4: Rewrite `CombatTaunt`** — replace the class body and update the summary's "only ATTACK and RUN" sentence:

```csharp
public static class CombatTaunt
{
    public enum Type { Attack, Run, Move, Miss, Hit }

    /// <summary>Roll whether <paramref name="pkt"/> taunts now and pick a combatai.msg id from the
    /// type's range, ported from fallout2-ce src/combat_ai.cc _combatai_msg(): always draws
    /// <c>randomBetween(1,100)</c> and skips when it is <c>&gt; chance</c>; else <c>randomBetween(start,end)</c>
    /// (inclusive). <c>end &lt; start</c> (no range) -> no taunt. HIT picks the range of
    /// <paramref name="hitLocation"/> (fo2ce HIT_LOCATION_*, torso 3). Returns -1 = no taunt.</summary>
    public static int Pick(AiPacket pkt, Type type, ICombatRng rng, int hitLocation = 3)
    {
        if (rng.Next(1, 101) > pkt.Chance)
            return -1;
        (int start, int end) = type switch
        {
            Type.Attack => (pkt.AttackStart, pkt.AttackEnd),
            Type.Run => (pkt.RunStart, pkt.RunEnd),
            Type.Move => (pkt.MoveStart, pkt.MoveEnd),
            Type.Miss => (pkt.MissStart, pkt.MissEnd),
            Type.Hit when pkt.HitRanges is { } h && hitLocation >= 0 && hitLocation < h.Length => h[hitLocation],
            _ => (0, -1),
        };
        if (end < start)
            return -1;
        return rng.Next(start, end + 1); // randomBetween(start, end) is inclusive
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Hexwaste.Formats.Tests -c Release --filter "FullyQualifiedName~AiPacketTests"`
Expected: PASS. Then run the whole unit suite; the Harness probe in `ViewerGame.Harness.cs:2039` still compiles (it uses the 3-arg `Pick`).

- [ ] **Step 6: Commit**

```bash
git add src/Hexwaste.Formats/Combat/CombatTaunt.cs src/Hexwaste.Formats/Combat/AiPackets.cs tests/Hexwaste.Formats.Tests/AiPacketTests.cs
git commit -m "feat(combat): taunt MOVE/MISS/HIT types and the always-drawn chance roll (_combatai_msg)"
```
(plus the trailers)

---

### Task 2: The reaction write in `_critter_set_who_hit_me`

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/ICombatHost.cs` (add a default member next to `RunCombatProc`, line ~211)
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs:2073` (`SetWhoHitMe`)
- Modify: `src/Hexwaste.Formats/Int/ScriptHost.cs` (new method next to `ReactionValue`, line ~1500)
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs` (implement next to `RunCombatProc`, line ~1433)
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs` (new test + `FakeCombatHost` member)

**Interfaces:**
- Produces: `ICombatHost.SetReaction(MapObject critter, int value)` (default no-op); `ScriptHost.SetReaction(MapFile map, MapObject critter, int value)`.

- [ ] **Step 1: Write the failing test** — add to `FakeCombatHost`:

```csharp
        public readonly List<(MapObject Critter, int Value)> Reactions = [];
        public void SetReaction(MapObject critter, int value) => Reactions.Add((critter, value));
```

and the test:

```csharp
    [Fact]
    public void OpeningCombatWritesTheReactionOfTheCritterTheDudeFaces()
    {
        // fo2ce critter.cc:1297 _critter_set_who_hit_me: when the stored attacker is gDude,
        // reactionSetValue(critter, -3) writes the critter's LVAR 0. _combat_sequence_init stamps
        // attacker<->defender, so a script aggro writes the AGGRESSOR's reaction (dude = its whoHitMe).
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject attacker = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        attacker.Team = 1;
        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(attacker, dude);
        Assert.Contains((attacker, -3), host.Reactions);
        Assert.DoesNotContain(host.Reactions, r => r.Critter == dude); // the dude's whoHitMe is a non-dude
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Hexwaste.Formats.Tests -c Release --filter "FullyQualifiedName~OpeningCombatWritesTheReaction"`
Expected: FAIL (`Reactions` empty).

- [ ] **Step 3: Implement**

`ICombatHost.cs`:

```csharp
    /// <summary>fo2ce reaction.cc reactionSetValue(): store <paramref name="value"/> in the critter's
    /// script LVAR 0 (no-op for an unscripted critter).</summary>
    void SetReaction(MapObject critter, int value) { }
```

`CombatEngine.cs` — `SetWhoHitMe` becomes an instance method (its callers at :2113, :2118, :2781, :2782 are instance code):

```csharp
    // ported from fallout2-ce src/critter.cc _critter_set_who_hit_me()
    // shortcut: fo2ce also stamps a same-team attacker when statRoll(INT, -1) < 2; that roll (a shared-stream
    // draw) is not modeled, upgrade when a same-team hit fixture exists
    private void SetWhoHitMe(MapObject target, MapObject? attacker)
    {
        if (attacker is null || attacker.Team != target.Team)
        {
            target.WhoHitMe = attacker;
            if (attacker is not null && attacker == _host.Dude)
                _host.SetReaction(target, -3); // reactionSetValue(a1, -3), critter.cc:1297
        }
    }
```

Keep the existing doc comment above it; fix any text that calls it static.

`ScriptHost.cs`, next to `ReactionValue`:

```csharp
    /// <summary>ported from fallout2-ce src/reaction.cc reactionSetValue(): scriptSetLocalVar(sid, 0, value),
    /// traced like a script's own store. No-op for an unscripted critter or a script without LVARs.</summary>
    public void SetReaction(MapFile map, MapObject critter, int value)
    {
        if (critter.Sid == -1 || !map.ScriptsBySid.TryGetValue(critter.Sid, out MapScriptRecord? record))
            return;
        int[] slice = GetLocalVarSlice(map, critter.Sid, record);
        if (slice.Length == 0)
            return;
        if (OracleTrace.Active)
            OracleTrace.Lvar(critter.Sid == DudeSid ? DudeTraceSid : critter.Sid, 0, slice[0], value);
        slice[0] = value;
    }
```

`ViewerGame.CombatHost.cs`:

```csharp
    public void SetReaction(MapObject critter, int value)
    {
        if (_map is not null)
            _scriptHost?.SetReaction(_map, critter, value);
    }
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Hexwaste.Formats.Tests -c Release`
Expected: the new test passes; the rest stay green.

- [ ] **Step 5: Commit** — `fix(combat): _critter_set_who_hit_me writes the reaction LVAR when the dude is the attacker` (plus trailers).

---

### Task 3: One `combat_p_proc` per turn

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` (`TryEnemyAction` :3564, `TryAllyAction` :3893, the two `TraceTurn` sites in `StepTurnOrder` :3207 and :3217, a new field)
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`

**Interfaces:**
- Produces: `private bool _turnProcRan;` (cleared when an NPC turn opens; Task 4 moves the clearing into `StartNpcTurn`).

- [ ] **Step 1: Write the failing test** — an enemy that must walk before it can attack (its turn spans frames because the fake host's `StartWalk` marks it `Animating`):

```csharp
    [Fact]
    public void AMultiFrameAiTurnRunsItsPerTurnCombatProcOnce()
    {
        // fo2ce _combat_turn (combat.cc:3247) runs combat_p_proc once; the walk and attack then run inside
        // _combat_turn_run. Hexwaste re-enters TryEnemyAction after the walk and must not re-run fp=4.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0, 4), hp: 30, ap: 10));
        enemy.Team = 1;
        enemy.Sid = 5;
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "");
        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(enemy, dude); // the enemy opens -> its round-1 turn starts now
        for (int i = 0; i < 20 && engine.Phase == CombatPhase.EnemyTurn; i++)
        {
            host.Animating.Clear(); // finish the walk/attack animation
            engine.Step();
        }
        Assert.Equal(1, host.CombatProcCalls.Count(c => c.Critter == enemy && c.FixedParam == 4));
    }
```

If the helper `HexGrid.TileInDirection(tile, dir, distance)` overload does not exist, use the 3-arg form used elsewhere in this file (e.g. line ~1612 `HexGrid.TileInDirection(20100, 0, 10)`).

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Hexwaste.Formats.Tests -c Release --filter "FullyQualifiedName~AMultiFrameAiTurnRunsItsPerTurnCombatProcOnce"`
Expected: FAIL (count 2 or more).

- [ ] **Step 3: Implement** — add the field next to `_actingEnemyAp`:

```csharp
    // fo2ce _combat_turn runs the per-turn combat_p_proc once; a turn that spans frames re-enters
    // TryEnemyAction/TryAllyAction, which must not run it again.
    private bool _turnProcRan;
```

In `StepTurnOrder`, set `_turnProcRan = false;` right before each of the two `TraceTurn(actor, ...)` calls (ally and enemy). In `TryEnemyAction` replace

```csharp
        if (RunCombatProcOverridesTurn(enemy))
            return false;
```

with

```csharp
        if (!_turnProcRan)
        {
            _turnProcRan = true;
            if (RunCombatProcOverridesTurn(enemy))
                return false;
        }
```

and the same in `TryAllyAction` for `ally`. (A turn the proc overrode returns false at once, so it is never re-entered.)

- [ ] **Step 4: Run the full unit suite** — `dotnet test tests/Hexwaste.Formats.Tests -c Release`. Expected: PASS. A test that asserted fp=4 calls per re-entry is asserting the bug: fix its expectation and say so in the commit message.

- [ ] **Step 5: Commit** — `fix(combat): run the per-turn combat_p_proc once per turn (_combat_turn)` (plus trailers).

---

### Task 4: The fo2ce combat list, round 1, and the round-end join pass

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs`: fields; `BeginCombat` (:2323); `StartBrawl` (:2348); `AddJoiners` (:2682, kept for brawls); `BuildTurnOrder` (:2725); `BeginScriptAggro` (:2810); `StepTurnOrder` (:3105); `StartNewRound` (:3230); `Reset` (:4215); `EndCombat` (the clear block near :2924)
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs` (new tests; update `JoinSetup` :1559 and every test the round-1 change breaks)

**Interfaces:**
- Consumes: `_turnProcRan` (Task 3); `SetWhoHitMe` (Task 2); existing `WantToJoin(MapObject, MapObject)`, `WithinPerception(MapObject, MapObject)`, `IsKnockedOut(MapObject)`, `ManeuverEngaging/Disengaging`, `TryEnemyAction`, `TryAllyAction`, `TraceTurn`.
- Produces: `private bool StartNpcTurn(MapObject actor)` (true while the turn spans frames; Task 5 adds the move-taunt flag there); `_combatList`, `_listCom`, `_listNoncom`, `_joinIndex`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void RoundOneIsTheAttackerAndTheDudeOnly_OthersJoinAtRoundEndWithAnImmediateTurn()
    {
        // fo2ce _combat_sequence_init: round 1 = attacker, defender (dude); no join check before it.
        // _combat_add_noncoms at round end: a joiner gets max AP and takes its turn at once.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject attacker = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        attacker.Team = 1;
        MapObject other = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 2), hp: 30, ap: 10));
        other.Team = 1;
        other.Sid = 9;
        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(attacker, dude);
        Assert.DoesNotContain(host.CombatProcCalls, c => c.Critter == other); // no join check at combat start
        for (int i = 0; i < 20 && engine.Phase != CombatPhase.PlayerTurn; i++) { host.Animating.Clear(); engine.Step(); }
        Assert.Equal(1, engine.Round);
        Assert.DoesNotContain(host.CombatProcCalls, c => c.Critter == other);
        engine.EndPlayerTurn(); // round 1 ends -> join pass
        for (int i = 0; i < 20 && engine.Phase != CombatPhase.PlayerTurn; i++) { host.Animating.Clear(); engine.Step(); }
        int join = host.CombatProcCalls.FindIndex(c => c.Critter == other && c.FixedParam == 5);
        int turn = host.CombatProcCalls.FindIndex(c => c.Critter == other && c.FixedParam == 4);
        Assert.True(join >= 0 && turn > join, "the joiner is asked (fp=5), then takes its turn (fp=4)");
        Assert.Contains(other, engine.Hostiles);
    }

    [Fact]
    public void TheJoinPassAsksNonCombatantsInHexTileOrder()
    {
        // objectListCreate walks tiles 0..39999 (object.cc:2652); _combat_add_noncoms visits in list order.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject attacker = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        attacker.Team = 1;
        MapObject high = host.AddCritter(NewCritter(tile: 20300, hp: 30, ap: 10)); // added first, higher tile
        MapObject low = host.AddCritter(NewCritter(tile: 19900, hp: 30, ap: 10));
        high.Sid = 11; low.Sid = 12;
        host.OnCombatProc = (c, fp) => { if (fp == 5) c.Maneuver = 0x02; }; // DISENGAGING: asked, never joins
        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(attacker, dude);
        for (int i = 0; i < 20 && engine.Phase != CombatPhase.PlayerTurn; i++) { host.Animating.Clear(); engine.Step(); }
        engine.EndPlayerTurn();
        for (int i = 0; i < 20 && engine.Phase != CombatPhase.PlayerTurn; i++) { host.Animating.Clear(); engine.Step(); }
        var asked = host.CombatProcCalls.Where(c => c.FixedParam == 5).Select(c => c.Critter).ToList();
        Assert.Equal([low, high], asked);
    }
```

Check `FakeCombatHost.OnCombatProc` exists with signature `Action<MapObject, int>?` (it is invoked at line ~4623); if the fake host does not let `NewCritter` tiles 20300/19900 sit within the map, pick any two interior tiles with `low < high`.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/Hexwaste.Formats.Tests -c Release --filter "FullyQualifiedName~RoundOneIsTheAttacker|FullyQualifiedName~TheJoinPassAsks"`
Expected: FAIL (today `AddJoiners` runs fp=5 at combat start).

- [ ] **Step 3: Add the list state** (near `_hostiles`):

```csharp
    // fo2ce combat.cc _combat_list / _list_com / _list_noncom: every critter on the elevation (dude included),
    // combatants first. Empty for a StartBrawl fight, which keeps the AddJoiners path.
    private readonly List<MapObject> _combatList = [];
    private int _listCom, _listNoncom;
    private int _joinIndex = -1; // position of the running round-end join pass (-1 = none)
```

Clear them in `Reset()` and in `EndCombat` next to `_hostiles.Clear();`:

```csharp
        _combatList.Clear();
        _listCom = _listNoncom = 0;
        _joinIndex = -1;
```

and add `_combatList.Clear();` at the start of `StartBrawl` (it keeps the old path).

- [ ] **Step 4: Build the list at combat start**

```csharp
    // ported from fallout2-ce src/combat.cc _combat_begin() + _combat_sequence_init(): the list in hex-tile
    // order, then the attacker, the defender and (if neither) the dude swapped to the front as round 1.
    // shortcut: critters created during combat never enter the list, upgrade if a fixture spawns one mid-fight
    private void InitCombatList(MapObject? attacker, MapObject? defender)
    {
        MapObject? dude = _host.Dude;
        _combatList.Clear();
        IEnumerable<MapObject> all = _host.CombatCritters.Concat(_host.PartyMembers);
        if (dude is not null)
            all = all.Append(dude);
        _combatList.AddRange(all.Where(c => !c.IsHidden).Distinct().OrderBy(c => c.HexTile)); // stable
        foreach (MapObject c in _combatList)
        {
            c.Maneuver &= ManeuverEngaging;
            c.DamageLastTurn = 0;
        }
        int next = 0;
        void Place(MapObject? o)
        {
            int i = o is null ? -1 : _combatList.IndexOf(o);
            if (i < 0)
                return;
            (_combatList[i], _combatList[next]) = (_combatList[next], _combatList[i]);
            next++;
        }
        Place(attacker);
        Place(defender);
        if (attacker != dude && defender != dude)
            Place(dude);
        _listCom = next;
        _listNoncom = _combatList.Count - next;
    }
```

In `BeginCombat(target)`: remove `AddJoiners();` and call `InitCombatList(_host.Dude, target);` right before `BuildTurnOrder(firstRound: true, _host.Dude, target);`.
In `BeginScriptAggro` (the `_phase == Idle` branch): remove `AddJoiners();` and call `InitCombatList(attacker, dude);` right before `BuildTurnOrder(firstRound: true, attacker, dude);`. Keep `attacker.WhoHitMe = dude;` after `InitCombatList` is not needed (BuildTurnOrder's `SetWhoHitMe(attacker, defender)` stamps it), but leave the existing line in place: it runs before `InitCombatList`, which does not touch `WhoHitMe`.

- [ ] **Step 5: Feed `BuildTurnOrder` from the list** — at the top of `BuildTurnOrder`, after `_actingAlly = null;`:

```csharp
        if (_combatList.Count > 0)
        {
            _order.AddRange(_combatList.Take(_listCom));
            if (firstRound && attacker is not null && defender is not null)
            {
                SetWhoHitMe(attacker, defender); // _combat_sequence_init, combat.cc:3011-3017
                SetWhoHitMe(defender, attacker);
            }
            _orderIndex = 0;
            ResetAllCombatAp();
            return;
        }
```

(`dude` is declared later in the method; declare the early block before it or move `MapObject? dude = _host.Dude;` above it.) The rest of the method stays for `StartBrawl` fights.

- [ ] **Step 6: Extract `StartNpcTurn`** — in `StepTurnOrder`, the NPC-slot block (from `_phase = CombatPhase.EnemyTurn;` through the ally/enemy `if/else`) moves into:

```csharp
    /// <summary>fo2ce _combat_turn for a non-dude critter: AP to max, TURN, the per-turn combat_p_proc and the AI.
    /// Returns true while the turn spans frames (StepTurnOrder resumes it).</summary>
    private bool StartNpcTurn(MapObject actor)
    {
        _phase = CombatPhase.EnemyTurn;
        _turnProcRan = false;
        if (_host.PartyMembers.Contains(actor))
        {
            _actingAlly = actor;
            _actingAllyAp = _host.GetCritterState(actor)?.MaxActionPoints ?? 5;
            TraceTurn(actor, _actingAllyAp); // fo2ce _combat_turn entry, before combat_p_proc
            if (TryAllyAction(actor))
                return true;
            _currentAp[actor] = _actingAllyAp; // P77
            _actingAlly = null;
        }
        else
        {
            _actingEnemy = actor;
            _actingEnemyAp = _host.GetCritterState(actor)?.MaxActionPoints ?? 5;
            TraceTurn(actor, _actingEnemyAp); // fo2ce _combat_turn entry, before combat_p_proc
            if (TryEnemyAction(actor))
                return true;
            _currentAp[actor] = _actingEnemyAp; // P77
            _actingEnemy = null;
        }
        return false;
    }
```

and `StepTurnOrder`'s slot becomes:

```csharp
            if (StartNpcTurn(actor))
                return;
            _orderIndex++;
```

(Task 3's `_turnProcRan = false;` lines at the old `TraceTurn` sites go away with the block.)

- [ ] **Step 7: The join pass and the round transition**

```csharp
    // ported from fallout2-ce src/combat_ai.cc _combatai_notify_friends(gDude)
    private void NotifyFriends(MapObject dude)
    {
        foreach (MapObject c in _combatList)
            if ((c.Maneuver & ManeuverEngaging) == 0 && c.Team == dude.Team && WithinPerception(c, dude))
                c.Maneuver |= ManeuverEngaging;
    }

    // ported from fallout2-ce src/combat.cc _combat_add_noncoms(): visit the non-combatants in list order; a
    // joiner moves into the combatant part and takes its turn at once. Returns true while a joiner's turn
    // spans frames (StepTurnOrder finishes it and advances _joinIndex).
    // shortcut: fo2ce runs one background frame (_process_bk) before each join check, which can fire due queue
    // events; not modeled, upgrade if a trace shows a timer firing between join checks
    private bool RunJoinPass()
    {
        if (_host.Dude is not { } dude)
            return false;
        if (_joinIndex < 0)
        {
            NotifyFriends(dude);
            _joinIndex = _listCom;
        }
        var onMap = _host.CombatCritters.Concat(_host.PartyMembers).ToHashSet();
        for (; _joinIndex < _listCom + _listNoncom; _joinIndex++)
        {
            if (_gameOver || _phase == CombatPhase.Idle)
                return false;
            MapObject c = _combatList[_joinIndex];
            if (c == dude || !onMap.Contains(c) || !WantToJoin(c, dude))
                continue;
            c.Maneuver = 0; // CRITTER_MANEUVER_NONE (combat.cc:2907)
            (_combatList[_joinIndex], _combatList[_listCom]) = (_combatList[_listCom], c);
            _listCom++;
            _listNoncom--;
            if (!_host.PartyMembers.Contains(c))
                _hostiles.Add(c);
            c.WhoHitMeCid = -1; // marks the dude as the aggressor
            _host.Log($"The {_host.ObjectName(c)} joins the fight!");
            _host.Transcript($"joins: {_host.ObjectName(c)}@{c.HexTile} (team {c.Team})");
            if (StartNpcTurn(c))
                return true;
        }
        return false;
    }

    // ported from fallout2-ce src/combat.cc _combat_sequence() (after _combat_add_noncoms): the dead leave the
    // list's live part, knocked-out and disengaging critters become non-combatants, the rest sort by
    // Sequence then Luck (stable; glibc qsort is a merge sort for these sizes).
    private void SequenceCombatList()
    {
        MapObject? dude = _host.Dude;
        int count = _listCom;
        for (int i = 0; i < count; i++)
        {
            MapObject c = _combatList[i];
            if (!c.IsDead)
                continue;
            _combatList[i] = _combatList[count - 1];
            _combatList[count - 1] = _combatList[_listNoncom + count - 1];
            _combatList[_listNoncom + count - 1] = c;
            i--;
            count--;
        }
        for (int i = 0; i < count; i++)
        {
            MapObject c = _combatList[i];
            if (c == dude || (!IsKnockedOut(c) && c.Maneuver != ManeuverDisengaging))
                continue;
            c.Maneuver &= ~ManeuverEngaging;
            _listNoncom++;
            _combatList[i] = _combatList[count - 1];
            _combatList[count - 1] = c;
            i--;
            count--;
        }
        List<MapObject> sorted = [.. _combatList.Take(count)
            .OrderByDescending(c => _host.GetCritterState(c)?.Sequence ?? 0)
            .ThenByDescending(c => _host.GetCritterState(c)?.Stat(CritterStat.Luck) ?? 0)];
        for (int i = 0; i < count; i++)
            _combatList[i] = sorted[i];
        _listCom = count;
    }
```

`StartNewRound` becomes:

```csharp
    private void StartNewRound()
    {
        _round++;
        _combatTick += TicksPerRound;
        // shortcut: fo2ce fires knockout wakes after _combat_sequence (queue events in a later frame); Hexwaste
        // processes them before it, upgrade if a trace shows a KO'd critter rejoining a round early
        _events.Process(_combatTick, OnCombatEvent);
        if (_combatList.Count > 0)
            SequenceCombatList();
        else
            AddJoiners(); // StartBrawl fights
        BuildTurnOrder(firstRound: false, null, null);
    }
```

In `StepTurnOrder`:
- the two continuation branches at the top (`_actingEnemy` / `_actingAlly`) replace `_orderIndex++;` with `if (_joinIndex >= 0) _joinIndex++; else _orderIndex++;`;
- inside `if (_orderIndex >= _order.Count)`, before the spectator cap, add:

```csharp
                if (_combatList.Count > 0)
                {
                    if (RunJoinPass())
                        return; // a joiner's turn spans frames
                    _joinIndex = -1;
                }
```

Update `AddJoiners`' summary to say it serves `StartBrawl` fights only, and `BuildTurnOrder`'s summary to say it reads the fo2ce list when one exists.

- [ ] **Step 8: Run the new tests**

Run: `dotnet test tests/Hexwaste.Formats.Tests -c Release --filter "FullyQualifiedName~RoundOneIsTheAttacker|FullyQualifiedName~TheJoinPassAsks|FullyQualifiedName~AMultiFrameAiTurn|FullyQualifiedName~OpeningCombatWritesTheReaction"`
Expected: PASS.

- [ ] **Step 9: Update the tests the round-1 change breaks** — run the full suite. `JoinSetup` (:1559) must drive round 1 to its end, and apply `setup(candidate)` AFTER combat starts (`InitCombatList` resets `Maneuver &= ENGAGING` and `DamageLastTurn`, as fo2ce's `_combat_begin` does):

```csharp
        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(attacker, dude);
        setup(candidate); // after _combat_begin's maneuver/damageLastTurn reset
        for (int i = 0; i < 20 && engine.Phase != CombatPhase.PlayerTurn; i++) { host.Animating.Clear(); engine.Step(); }
        engine.EndPlayerTurn(); // round 1 ends -> _combat_add_noncoms
        for (int i = 0; i < 20 && engine.Phase is not (CombatPhase.PlayerTurn or CombatPhase.Idle); i++) { host.Animating.Clear(); engine.Step(); }
        return (engine, host, dude, candidate);
```

For every other failing test: decide whether it encoded the old round-1 composition (all hostiles/allies act in round 1, joiners at combat start) or a real regression. For the former, drive the fight to the round end the same way and keep the assertion's intent; never delete an assertion to make a test pass. List each changed test and the reason in the commit message.

- [ ] **Step 10: Commit** — `fix(combat): fo2ce combat list - round 1 is attacker/defender/dude, joiners act at round end (F4/F5)` (plus trailers, plus the changed-tests list).

---

### Task 5: Taunt draw sites

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/ICombatHost.cs`
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs`: new field + helper; `StartNpcTurn`; the enemy approach `StartWalk` (end of the enemy AI method, ~:3802); the ally approach `StartWalk` (~:4054); the attack sites after each `_host.OnAttackStarted(...)` (:450, :587, :4025, :4104, :4137, :4167)
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs` (`TryTaunt` :970, `OnAttackStarted` :1216, new `Taunt`)
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`

**Interfaces:**
- Consumes: `CombatTaunt.Type.Move/Miss/Hit`, `CombatTaunt.Pick(..., hitLocation)` (Task 1); `StartNpcTurn` (Task 4).
- Produces: `ICombatHost.Taunt(MapObject critter, CombatTaunt.Type type, int hitLocation = 3)` (default no-op).

- [ ] **Step 1: Write the failing tests** — add to `FakeCombatHost`:

```csharp
        public readonly List<(MapObject Critter, CombatTaunt.Type Type)> Taunts = [];
        public void Taunt(MapObject critter, CombatTaunt.Type type, int hitLocation = 3) => Taunts.Add((critter, type));
```

and:

```csharp
    [Fact]
    public void AnAiTurnThatWalksThenSwingsTauntsMoveOnceThenTheMeleeOutcome()
    {
        // fo2ce: _ai_move_steps_closer MOVE taunt on the first approach (combat_ai.cc:2382), then _action_melee's
        // attacker HIT/MISS taunt (actions.cc:674-680). The ATTACK taunt stays in the host's OnAttackStarted.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0, 4), hp: 30, ap: 10));
        enemy.Team = 1;
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "");
        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(enemy, dude);
        for (int i = 0; i < 20 && engine.Phase == CombatPhase.EnemyTurn; i++) { host.Animating.Clear(); engine.Step(); }
        var mine = host.Taunts.Where(t => t.Critter == enemy).Select(t => t.Type).ToList();
        Assert.Equal(CombatTaunt.Type.Move, mine[0]);
        Assert.Equal(1, mine.Count(t => t == CombatTaunt.Type.Move));
        Assert.Contains(mine, t => t is CombatTaunt.Type.Hit or CombatTaunt.Type.Miss);
    }
```

(If the enemy's walk does not end adjacent with AP left to swing in this setup, give it more AP; the MOVE assertions are the point.)

- [ ] **Step 2: Run it to verify it fails** — `--filter "FullyQualifiedName~TauntsMoveOnceThenTheMeleeOutcome"`; expected FAIL (no `Taunts`).

- [ ] **Step 3: Implement**

`ICombatHost.cs`:

```csharp
    /// <summary>fo2ce combat_ai.cc _combatai_msg(): a taunt chance roll (and message) for the critter.</summary>
    void Taunt(MapObject critter, CombatTaunt.Type type, int hitLocation = 3) { }
```

`CombatEngine.cs` — field next to `_turnProcRan`, set in `StartNpcTurn` next to `_turnProcRan = false;`:

```csharp
    // fo2ce _ai_attack's `taunt` (combat_ai.cc:2697): only the turn's first approach walk taunts.
    private bool _moveTauntPending;
```

```csharp
        _moveTauntPending = true;
```

Before the enemy approach `return _host.StartWalk(enemy, targetTile, approachRun);` and the ally approach `return _host.StartWalk(ally, walkTarget, allyRun);`:

```csharp
        if (_moveTauntPending)
        {
            _moveTauntPending = false;
            _host.Taunt(enemy, CombatTaunt.Type.Move); // _ai_move_steps_closer, combat_ai.cc:2382
        }
```

(`ally` at the ally site.) The snipe/flee walks (`_ai_move_away`, `TryFlee`) do not taunt MOVE.

The outcome helper:

```csharp
    // ported from fallout2-ce src/actions.cc _action_melee() (:674-680) and _action_ranged() (:917-923): after the
    // ATTACK taunt, melee taunts the attacker's HIT (hit, defender alive) or MISS; ranged taunts the defender's.
    // shortcut: "defender alive" compares damage with current HP, ignoring instant-death crit flags; upgrade if
    // a trace shows a taunt draw after a crit kill
    private void TauntAttackOutcome(MapObject attacker, MapObject defender, bool ranged, bool hit, int damage,
        int hitLocation)
    {
        MapObject taunter = ranged ? defender : attacker;
        if (!hit)
            _host.Taunt(taunter, CombatTaunt.Type.Miss);
        else if (damage < defender.CurrentHp)
            _host.Taunt(taunter, CombatTaunt.Type.Hit,
                hitLocation == CriticalTables.LocationUncalled ? 3 : hitLocation); // uncalled = torso
    }
```

Call it right after each `_host.OnAttackStarted(...)`:
- `EnemyAttack` (:4167): hoist the `AiHitLocation(...)` argument of `RollAttack` into `int hitLoc` and pass it; `TauntAttackOutcome(enemy, defenderObj, isGun, hit, damage, hitLoc);`
- the dude's single attack (:450): `TauntAttackOutcome(dude, target, isGun, hit, damage, hitLocation);`
- the dude's burst (:587): `TauntAttackOutcome(dude, target, ranged: true, roundsHit > 0, totalDamage, CriticalTables.LocationUncalled);`
- the ally's single attack (:4025): `TauntAttackOutcome(ally, target, isGun, hit, damage, CriticalTables.LocationUncalled);`
- the ally burst (:4104) and enemy burst (:4137): `TauntAttackOutcome(<attacker>, target, ranged: true, hits > 0, total, CriticalTables.LocationUncalled);`

Use the local names actually in scope at each site (read 15 lines above each call).

`ViewerGame.CombatHost.cs` — `TryTaunt` gains `int hitLocation = 3` (passed to `Pick`) and the dude return; `OnAttackStarted`'s own dude check can stay:

```csharp
    private void TryTaunt(MapObject critter, Formats.Combat.CombatTaunt.Type type, int hitLocation = 3)
    {
        if (critter == _dude?.Dude) // combat_ai.cc:3314: the dude never taunts (no draw)
            return;
        if (critter.IsDead || (critter.CombatResults & Formats.Combat.CriticalTables.DamKnockedOut) != 0)
            return;
        ...
        int msgId = Formats.Combat.CombatTaunt.Pick(pkt, type, _tauntRng, hitLocation);
```

```csharp
    public void Taunt(MapObject critter, Formats.Combat.CombatTaunt.Type type, int hitLocation = 3) =>
        TryTaunt(critter, type, hitLocation);
```

- [ ] **Step 4: Run the full unit suite** — expected PASS.

- [ ] **Step 5: Commit** — `fix(combat): MOVE/HIT/MISS taunt draws at fo2ce's sites (_combatai_msg)` (plus trailers).

---

### Task 6: Oracle, goldens, findings doc

**Files:**
- Modify: `docs/research-notes/oracle-m1-findings-2026-10.md` (F4, F5, "Recommended next steps")
- Modify (re-record only): fixtures under `tests/golden-*/` that move

- [ ] **Step 1: CI-equivalent checks**

```bash
./scripts/ascii-lint.sh
dotnet build src/Hexwaste.Viewer -c Release
dotnet test tests/Hexwaste.Formats.Tests -c Release
```
Expected: clean lint, 0 build errors, all tests pass.

- [ ] **Step 2: Oracle** — one at a time:

```bash
scripts/oracle-run.sh arcaves-ambush > scratch/oracle-runs/arcaves-ambush-run.out 2>&1; tail -15 scratch/oracle-runs/arcaves-ambush-run.out
scripts/oracle-run.sh artemple-idle > scratch/oracle-runs/artemple-idle-run.out 2>&1; tail -5 scratch/oracle-runs/artemple-idle-run.out
scripts/oracle-run.sh klamath-hour > scratch/oracle-runs/klamath-hour-run.out 2>&1; tail -12 scratch/oracle-runs/klamath-hour-run.out
```
Expected: arcaves moves past line 9 and through the opening round (next gap expected near t=302451, F19); artemple IDENTICAL; klamath still at line 1664. Take the first divergence from the TraceDiff output, not a plain diff. If arcaves diverges inside the opening round, diagnose against `scratch/oracle-runs/arcaves-ambush/{fo2ce,hexwaste}.trace`, fix within this plan's scope, and report what you changed.

- [ ] **Step 3: Goldens** — sequentially, saving outputs:

```bash
for s in combat quest endgame opening encounter; do scripts/$s-golden.sh > scratch/cj-golden-$s.out 2>&1; tail -3 scratch/cj-golden-$s.out; done
```
For each moved fixture: run the suite a second time to rule out the known encounter flake, review the diff (the move must be explained by round-1 composition, joiner timing, one combat_p_proc per turn, the reaction LVAR, or taunt draws), then `scripts/<suite>-golden.sh record`, re-check, and list each re-recorded fixture with its reason in the commit message.

- [ ] **Step 4: Findings doc** — F4 and F5: status FIXED with a one-paragraph summary (round 1 = attacker/defender/dude; round-end `_combat_add_noncoms` with an immediate turn; reaction LVAR; one combat_p_proc per turn; taunt draws), replace F4's stale evidence, and add the new first divergences under "Recommended next steps".

- [ ] **Step 5: Commit** — `docs(oracle): F4/F5 fixed; new first divergences` and, separately if any, `test(golden): re-record <fixtures> for the fo2ce combat opening` (plus trailers).
