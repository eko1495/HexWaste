# Oracle F6: critter_p_proc Cadence and Order Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run critter scripts the way fallout2-ce does: one `critter_p_proc` per frame, round-robin over an ordered critter script list that `ScriptHost` keeps in fo2ce's order.

**Architecture:** A new `CritterScriptList` (Formats) holds critter sids with fo2ce's append / swap-remove / round-robin semantics. `ScriptHost` owns one list plus a sid-to-object index for the whole session. It rebuilds both on each map load and keeps them in sync through `AllocateSid`, `BindDudeScript` and `RemoveScript`. The viewer's `PumpCritterProcs` takes one sid per frame from the list. Kill sites remove scripts at the moment of death; the combat engine does this through a new `ICombatHost` callback.

**Tech Stack:** C# / .NET 10, xUnit, bash golden suites, the fo2ce trace oracle (`scripts/oracle-run.sh`).

**Spec:** `docs/superpowers/specs/2026-10-11-oracle-f6-critter-cadence-design.md`

## Global Constraints

- fallout2-ce `alexbatalov e97087b` (`reference/fallout2-ce`) is authoritative. Ported code carries `// ported from fallout2-ce src/x.cc f()`.
- No new dependencies (MonoGame, xUnit, ImageSharp only).
- Never commit game assets. Tests needing game data use `[GameDataFact]` and `FALLOUT2_DIR`.
- No non-ASCII characters in C# string literals (`scripts/ascii-lint.sh` runs first in CI).
- Branch: `feat/oracle-f6-critter-cadence` (already created). Commit after each task; do not push.
- Unit tests: `FALLOUT2_DIR=$PWD/game-data dotnet test` (output is Polish: "Powodzenie" = pass, "niepowodzenie" = failures).
- The `_count_` counter is never reset (it survives map loads).
- Kept Hexwaste deviations, each with a code comment:
  - waiting companions are skipped (their slot is consumed, nothing runs);
  - a script without `critter_p_proc` runs nothing (fo2ce runs procedure 1, finding F8).
- Map-change list order (map critters, then dude, then party) is an unverified assumption and gets a code comment saying so.

## Refinement of the spec (decided while planning)

The spec says the rebuild skips sids "whose owner is dead or no longer on the map". The plan uses the
narrower and more faithful rule: **skip a sid when no object on the map currently holds it.**
- Every Hexwaste kill path already sets `Sid = -1`, and so does the revisit delta replay
  (`ViewerGame.SaveLoad.cs:164`). So killed critters drop out on their own.
- Corpses that the pristine `.MAP` itself places with a script keep it, exactly as fo2ce's `.MAP` load
  does.
- This is a small behavior change from today's `IsHeartbeatEligible`, which skipped every dead critter.

---

## File map

- Create:
  - `src/Hexwaste.Formats/Int/CritterScriptList.cs`: the ordered list plus the counter.
  - `tests/Hexwaste.Formats.Tests/CritterScriptListTests.cs`
  - `tests/Hexwaste.Formats.Tests/CritterScriptOrderTests.cs`: `ScriptHost` list/index tests (hermetic).
- Modify:
  - `src/Hexwaste.Formats/Map/MapFile.cs`: `CritterSids`, filled in `ReadScripts`.
  - `src/Hexwaste.Formats/Int/ScriptHost.cs`: list ownership, index, `AllocateSid(map, idx, owner)`, `RemoveScript`, `TryGetScriptOwner`, `RebuildCritterScripts`; `DestroyObject` removes.
  - `src/Hexwaste.Formats/Combat/ICombatHost.cs`: `RemoveCritterScript` default no-op.
  - `src/Hexwaste.Formats/Combat/CombatEngine.cs:2304`: call it before `critter.Sid = -1`.
  - `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`: fake host records it, plus a test.
  - `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs`: `RemoveCritterScript`, kill sites, `PumpCritterProcs`.
  - `src/Hexwaste.Viewer/ViewerGame.cs`: `LoadMap` rebuild, `:4286` and `:5570` `AllocateSid` callers, `:5518` kill site, sneak timer field.
  - `src/Hexwaste.Viewer/ViewerGame.CompanionHub.cs:127,148`: dismiss and rejoin.
  - `src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs`: copy `CritterSids`, append the second dude.
  - `docs/research-notes/oracle-m1-findings-2026-10.md`, `docs/PHASE-HISTORY.md`.
  - Golden fixtures under `tests/golden-*` (re-recorded in Task 7).

---

### Task 1: Baseline timing and the dude-duplicate cause

No production code. This records two facts the later tasks rely on.

**Files:**
- Modify: `docs/research-notes/oracle-m1-findings-2026-10.md` (F6 section)
- Temporary (not committed): `reference/fallout2-ce-oracle/src/scripts.cc`

**Interfaces:** Produces the baseline golden runtime (seconds) and the confirmed cause of the doubled dude entry. Task 7 uses both.

- [ ] **Step 1: Time the golden suites on the unchanged branch**

```bash
cd /home/eko/dev/FPOC
dotnet build src/Hexwaste.Viewer -v q -nologo
for s in combat opening endgame quest encounter; do
  /usr/bin/time -f "$s %e s" scripts/$s-golden.sh 2>&1 | grep -E "ALL PASS|FAILURES| s$"
done
```

Expected: every suite prints `ALL PASS` (a pre-existing flake may print NONDETERMINISTIC; rerun that suite once). Write down the five times and their sum.

- [ ] **Step 2: Dump fo2ce's critter list after the save load**

In `reference/fallout2-ce-oracle/src/scripts.cc`, add this temporary function after `oracleResetBkMapUpdate()`:

```cpp
void oracleDumpCritterList()
{
    ScriptList* list = &(gScriptLists[SCRIPT_TYPE_CRITTER]);
    int slot = 0;
    for (ScriptListExtent* e = list->head; e != nullptr; e = e->next) {
        for (int i = 0; i < e->length; i++) {
            fprintf(stderr, "oracle-critter-list %d sid=0x%08X flags=0x%X\n", slot++, e->scripts[i].sid, e->scripts[i].flags);
        }
    }
}
```

Declare it in `src/scripts.h` next to `oracleResetBkMapUpdate`. Call it in `src/main.cc` right after `oracleResetBkMapUpdate();`.

Also add one `fprintf(stderr, "oracle-set-dude sid=0x%08X\n", gDude->sid);` line at the end of `scriptsSetDudeScript()`, before `return 0;`.

- [ ] **Step 3: Build and run the artemple fixture**

```bash
tools/fo2ce-trace/build.sh 2>&1 | tail -1
scripts/oracle-run.sh artemple-idle > /dev/null 2>&1
grep -E "oracle-critter-list|oracle-set-dude" scratch/oracle-runs/artemple-idle/fo2ce.log
```

Expected: three list entries, two of them with the dude's sid, and `oracle-set-dude` printed twice during the load (the two calls in `_obj_load_dude`, `object.cc:3649` and `:3660`). If the output differs, record what you actually see. Task 6's import step follows the observed list exactly: the number of dude entries and their positions.

- [ ] **Step 4: Revert the temporary fo2ce change and rebuild**

```bash
cd reference/fallout2-ce-oracle && git checkout -- src/scripts.cc src/scripts.h src/main.cc && cd ../..
git -C reference/fallout2-ce-oracle add -A
git -C reference/fallout2-ce-oracle diff --cached e97087b -- . ':!build-oracle' | cmp - tools/fo2ce-trace/trace.patch && echo PATCH-CLEAN
tools/fo2ce-trace/build.sh 2>&1 | tail -1
```

`git checkout --` restores the staged (patched) version, so `PATCH-CLEAN` must print.

- [ ] **Step 5: Record the findings**

Append to the F6 section of `docs/research-notes/oracle-m1-findings-2026-10.md`:

```markdown
- Checked (F6 Task 1): after `--load-vanilla-save` fo2ce's critter list is <paste the observed slots>.
  The second dude entry comes from <observed cause, e.g. `_obj_load_dude` calling `scriptsSetDudeScript`
  twice, `object.cc:3649/3660`>. Golden baseline before F6: <sum> s
  (combat <a>, opening <b>, endgame <c>, quest <d>, encounter <e>).
```

- [ ] **Step 6: Commit**

```bash
git add docs/research-notes/oracle-m1-findings-2026-10.md
git commit -m "docs(oracle): F6 baseline timing and the doubled dude critter entry"
```

---

### Task 2: `CritterScriptList`

**Files:**
- Create: `src/Hexwaste.Formats/Int/CritterScriptList.cs`
- Create: `tests/Hexwaste.Formats.Tests/CritterScriptListTests.cs`

**Interfaces:**
- Produces: `public sealed class CritterScriptList` in namespace `Hexwaste.Formats.Int` with
  - `int Count`
  - `void Add(int sid)`
  - `void Remove(int sid)`
  - `int? Next()`
  - `void Clear()`
  - `IReadOnlyList<int> Sids`

- [ ] **Step 1: Write the failing tests**

```csharp
using Hexwaste.Formats.Int;

namespace Hexwaste.Formats.Tests;

public class CritterScriptListTests
{
    private static CritterScriptList Of(params int[] sids)
    {
        var list = new CritterScriptList();
        foreach (int sid in sids)
            list.Add(sid);
        return list;
    }

    [Fact]
    public void FirstNextRunsSlotOneThenWraps()
    {
        CritterScriptList list = Of(10, 11, 12);
        Assert.Equal(11, list.Next()); // _count_ starts at 0 and is incremented before use
        Assert.Equal(12, list.Next());
        Assert.Equal(10, list.Next()); // wrap at Count
        Assert.Equal(11, list.Next());
    }

    [Fact]
    public void RemoveMovesTheTailIntoTheRemovedSlot()
    {
        CritterScriptList list = Of(10, 11, 12, 13);
        list.Remove(11);
        Assert.Equal([10, 13, 12], list.Sids);
        list.Remove(12); // removing the last entry just shrinks
        Assert.Equal([10, 13], list.Sids);
    }

    [Fact]
    public void RemovingAMissingSidIsANoOp()
    {
        CritterScriptList list = Of(10, 11);
        list.Remove(99);
        list.Remove(-1);
        Assert.Equal([10, 11], list.Sids);
    }

    [Fact]
    public void EmptyListYieldsNull()
    {
        Assert.Null(new CritterScriptList().Next());
    }

    [Fact]
    public void ClearKeepsTheCounter()
    {
        CritterScriptList list = Of(10, 11, 12);
        list.Next(); // counter 1
        list.Next(); // counter 2
        list.Clear();
        list.Add(20);
        list.Add(21);
        list.Add(22);
        list.Add(23);
        Assert.Equal(23, list.Next()); // counter 3, not 1
    }

    [Fact]
    public void DuplicatesAreAllowedAndRemoveTakesTheFirst()
    {
        CritterScriptList list = Of(10, 77, 11, 77);
        list.Remove(77);
        Assert.Equal([10, 77, 11], list.Sids);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~CritterScriptListTests"`
Expected: build error, `CritterScriptList` not found.

- [ ] **Step 3: Implement**

```csharp
namespace Hexwaste.Formats.Int;

/// <summary>The critter script list (gScriptLists[SCRIPT_TYPE_CRITTER]) as the critter heartbeat sees it:
/// ordered sids, appended by scriptAdd, swap-removed by scriptRemove, walked round-robin by
/// _script_chk_critters. The counter (_count_) is never reset, not even by <see cref="Clear"/>.</summary>
public sealed class CritterScriptList
{
    private readonly List<int> _sids = [];
    private int _count;

    public int Count => _sids.Count;

    public IReadOnlyList<int> Sids => _sids;

    // ported from fallout2-ce src/scripts.cc scriptAdd(): new scripts go to the tail.
    public void Add(int sid) => _sids.Add(sid);

    // ported from fallout2-ce src/scripts.cc scriptRemove(): the tail entry moves into the removed slot.
    public void Remove(int sid)
    {
        int index = _sids.IndexOf(sid);
        if (index < 0)
            return;
        int last = _sids.Count - 1;
        _sids[index] = _sids[last];
        _sids.RemoveAt(last);
    }

    // ported from fallout2-ce src/scripts.cc _script_chk_critters(): increment, wrap, then run that slot.
    public int? Next()
    {
        _count++;
        if (_count >= _sids.Count)
            _count = 0;
        return _count < _sids.Count ? _sids[_count] : null;
    }

    public void Clear() => _sids.Clear();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~CritterScriptListTests"`
Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Hexwaste.Formats/Int/CritterScriptList.cs tests/Hexwaste.Formats.Tests/CritterScriptListTests.cs
git commit -m "feat(scripts): CritterScriptList with fo2ce append/swap-remove/round-robin (oracle F6)"
```

---

### Task 3: `MapFile.CritterSids`

**Files:**
- Modify: `src/Hexwaste.Formats/Map/MapFile.cs` (`ReadScripts` at ~line 335; the properties at ~line 244)
- Test: `tests/Hexwaste.Formats.Tests/CritterScriptOrderTests.cs` (created here, extended in Task 4)

**Interfaces:**
- Produces: `public List<int> CritterSids { get; } = [];` on `MapFile`. It holds type-4 sids in script-section file order and is filled by `MapFile.Load`.

- [ ] **Step 1: Write the failing test**

```csharp
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

public class CritterScriptOrderTests
{
    [GameDataFact]
    public void MapCritterSidsAreTheCritterSectionInFileOrder()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        MapFile map;
        using (Stream stream = vfs.OpenRead(@"maps\kladwtwn.map"))
            map = MapFile.Load(stream, protos);

        Assert.NotEmpty(map.CritterSids);
        Assert.All(map.CritterSids, sid => Assert.Equal(4, sid >> 24));
        Assert.Equal(map.ScriptsBySid.Keys.Where(sid => sid >> 24 == 4).Order(), map.CritterSids.Order());
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `FALLOUT2_DIR=$PWD/game-data dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~CritterScriptOrderTests"`
Expected: build error, `CritterSids` not found.

- [ ] **Step 3: Implement**

In `MapFile`, under `SpatialScripts`:

```csharp
    /// <summary>Critter-type (type 4) sids in script-section file order: fo2ce's
    /// gScriptLists[SCRIPT_TYPE_CRITTER] order after scriptLoadAll, which the critter heartbeat walks.</summary>
    public List<int> CritterSids { get; } = [];
```

Change the `ReadScripts` signature and its one call (`ReadScripts(reader, map.ScriptsBySid, map.SpatialScripts);` at ~line 296) to pass `map.CritterSids`:

```csharp
    private static void ReadScripts(BigEndianReader reader, Dictionary<int, MapScriptRecord> scriptsBySid,
        List<SpatialScript> spatialScripts, List<int> critterSids)
```

```csharp
        ReadScripts(reader, map.ScriptsBySid, map.SpatialScripts, map.CritterSids);
```

Add `const int typeCritter = 4;` beside `typeTimed`. Inside the `if (!isPadding && sid != -1)` block, after the `scriptsBySid[sid] = ...` line:

```csharp
                        if (sid >> 24 == typeCritter)
                            critterSids.Add(sid);
```

- [ ] **Step 4: Run it to verify it passes**

Run: `FALLOUT2_DIR=$PWD/game-data dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~CritterScriptOrderTests"`
Expected: 1 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Hexwaste.Formats/Map/MapFile.cs tests/Hexwaste.Formats.Tests/CritterScriptOrderTests.cs
git commit -m "feat(map): MapFile.CritterSids keeps the critter script section order (oracle F6)"
```

---

### Task 4: `ScriptHost` owns the list and the sid index

**Files:**
- Modify: `src/Hexwaste.Formats/Int/ScriptHost.cs` (`BindDudeScript` ~397, `AllocateSid` ~409, `BindPartyScript` ~442, `CreateObject` ~2540, `DestroyObject` ~2553)
- Modify (callers of the changed `AllocateSid` signature):
  - `src/Hexwaste.Viewer/ViewerGame.CompanionHub.cs:148`
  - `src/Hexwaste.Viewer/ViewerGame.cs:4286` and `:5570`
- Test: `tests/Hexwaste.Formats.Tests/CritterScriptOrderTests.cs`

**Interfaces:**
- Consumes: `CritterScriptList` (Task 2), `MapFile.CritterSids` (Task 3).
- Produces, on `ScriptHost`:
  - `public CritterScriptList CritterScripts { get; }`
  - `public void RebuildCritterScripts(MapFile map)`
  - `public bool TryGetScriptOwner(int sid, out MapObject owner)`
  - `public void RemoveScript(int sid)`
  - `public int AllocateSid(MapFile map, int scriptIndex, MapObject owner)` (replaces the 2-argument form)
  - `BindDudeScript` now also appends `DudeSid` and indexes the dude.

- [ ] **Step 1: Write the failing hermetic tests**

Replace the class header of `CritterScriptOrderTests` with the scaffold below (it now implements `IDisposable`, with the temp-dir setup copied from `StaleHandleGuardTests`). Keep Task 3's `MapCritterSidsAreTheCritterSectionInFileOrder` test inside the class:

```csharp
public class CritterScriptOrderTests : IDisposable
{
    private readonly string _tempDir;

    public CritterScriptOrderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hexwaste-critterorder-" + Guid.NewGuid().ToString("N"));
        string scriptsDir = Path.Combine(_tempDir, "data", "scripts");
        Directory.CreateDirectory(scriptsDir);
        File.WriteAllBytes(Path.Combine(_tempDir, "master.dat"),
            [0, 0, 0, 0, 4, 0, 0, 0, 12, 0, 0, 0]); // the 12-byte empty DAT2
        File.WriteAllText(Path.Combine(scriptsDir, "scripts.lst"), "obj_dude.int ; # local_vars=0\n");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private ScriptHost MakeHost()
    {
        GameFileSystem vfs = GameFileSystem.Open(_tempDir);
        return new ScriptHost(vfs, ScriptList.Load(vfs), new ProtoDatabase(vfs));
    }

    private static MapObject Critter(int id, int sid) => new()
    {
        Id = id, HexTile = 100 + id, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = Fid.Build(ObjectType.Critter, 1), Flags = 0, Pid = 0x01000002, Sid = sid,
    };

    private static MapFile MapWith(string name, params MapObject[] critters)
    {
        var map = new MapFile
        {
            Header = new MapHeader(20, name, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            GlobalVariables = [],
            LocalVariables = [],
            Elevations = new MapElevation?[MapFile.ElevationCount],
        };
        map.Elevations[0] = new MapElevation { Squares = new int[MapElevation.SquareGridSize] };
        foreach (MapObject c in critters)
        {
            map.Elevations[0]!.Objects.Add(c);
            if (c.Sid != -1)
            {
                map.ScriptsBySid[c.Sid] = new MapScriptRecord(0, -1, 0);
                map.CritterSids.Add(c.Sid);
            }
        }
        return map;
    }

    [Fact]
    public void MapChangeOrderIsMapCrittersThenDudeThenParty()
    {
        ScriptHost host = MakeHost();
        MapObject dude = Critter(18000, -1);
        MapObject companion = Critter(50, -1);
        MapFile map = MapWith("b.map", Critter(1, 0x04000001), Critter(2, 0x04000002));

        host.RebuildCritterScripts(map);
        host.BindDudeScript(map, dude);
        companion.Sid = host.AllocateSid(map, 0, companion); // party re-bind after map_enter

        Assert.Equal([0x04000001, 0x04000002, ScriptHost.DudeSid, companion.Sid], host.CritterScripts.Sids);
        Assert.Equal(4, companion.Sid >> 24);
        Assert.NotEqual(ScriptHost.DudeSid, companion.Sid);
        Assert.True(host.TryGetScriptOwner(companion.Sid, out MapObject owner));
        Assert.Same(companion, owner);
    }

    [Fact]
    public void RemoveScriptKeepsListAndIndexInSync()
    {
        ScriptHost host = MakeHost();
        MapObject a = Critter(1, 0x04000001), b = Critter(2, 0x04000002), c = Critter(3, 0x04000003);
        MapFile map = MapWith("a.map", a, b, c);
        host.RebuildCritterScripts(map);

        host.RemoveScript(0x04000001);

        Assert.Equal([0x04000003, 0x04000002], host.CritterScripts.Sids);
        Assert.False(host.TryGetScriptOwner(0x04000001, out _));
        Assert.True(host.TryGetScriptOwner(0x04000003, out MapObject owner));
        Assert.Same(c, owner);
    }

    [Fact]
    public void RebuildSkipsSidsNoObjectHolds()
    {
        ScriptHost host = MakeHost();
        MapObject killed = Critter(1, 0x04000001);
        MapFile map = MapWith("c.map", killed, Critter(2, 0x04000002));
        killed.Sid = -1; // the revisit delta replay clears a dead critter's sid (ViewerGame.SaveLoad.cs:164)

        host.RebuildCritterScripts(map);

        Assert.Equal([0x04000002], host.CritterScripts.Sids);
    }

    [Fact]
    public void ItemsGetItemTypeSidsAndStayOffTheList()
    {
        ScriptHost host = MakeHost();
        MapFile map = MapWith("d.map");
        host.RebuildCritterScripts(map);
        var item = new MapObject
        {
            Id = 7, HexTile = 7, X = 0, Y = 0, Frame = 0, Rotation = 0,
            Fid = Fid.Build(ObjectType.Item, 1), Flags = 0, Pid = 0x00000001, Sid = -1,
        };

        int sid = host.AllocateSid(map, 0, item);

        Assert.Equal(3, sid >> 24);
        Assert.Empty(host.CritterScripts.Sids);
    }
}
```

Add `using Hexwaste.Formats.Int;` and `using Hexwaste.Formats.Map;` if they are missing (Task 3 already added them). Check that `Fid.Build` and `ObjectType` resolve; they live in the `Hexwaste.Formats` namespace.

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~CritterScriptOrderTests"`
Expected: build errors, `RebuildCritterScripts` / `CritterScripts` / the 3-argument `AllocateSid` not found.

- [ ] **Step 3: Implement in `ScriptHost`**

Next to `BindDudeScript`:

```csharp
    /// <summary>The critter heartbeat's list (oracle F6). Session-scoped like fo2ce's _count_.</summary>
    public CritterScriptList CritterScripts { get; } = new();

    // sid -> owner for the critter heartbeat (all elevations); kept in step with CritterScripts.
    private readonly Dictionary<int, MapObject> _scriptOwners = [];

    /// <summary>Map load: the list becomes the map's critter section in file order (fo2ce scriptLoadAll),
    /// minus sids no object on the map still holds (killed critters clear their sid; a .MAP corpse that
    /// still carries a script keeps it, as in fo2ce).</summary>
    public void RebuildCritterScripts(MapFile map)
    {
        CritterScripts.Clear();
        _scriptOwners.Clear();
        foreach (MapElevation? elev in map.Elevations)
            foreach (MapObject obj in elev?.Objects ?? [])
                if (obj.Sid != -1)
                    _scriptOwners[obj.Sid] = obj;
        foreach (int sid in map.CritterSids)
            if (_scriptOwners.ContainsKey(sid))
                CritterScripts.Add(sid);
    }

    public bool TryGetScriptOwner(int sid, out MapObject owner) => _scriptOwners.TryGetValue(sid, out owner!);

    /// <summary>ported from fallout2-ce src/scripts.cc scriptRemove(): drop a script from the critter list
    /// (tail moves into its slot) and the owner index. -1 and unknown sids are no-ops.</summary>
    public void RemoveScript(int sid)
    {
        if (sid == -1)
            return;
        CritterScripts.Remove(sid);
        _scriptOwners.Remove(sid);
    }
```

Change `BindDudeScript` to:

```csharp
    public void BindDudeScript(MapFile map, MapObject dude)
    {
        map.ScriptsBySid[DudeSid] = new MapScriptRecord(0, -1, 0);
        dude.Sid = DudeSid;
        // fo2ce scriptsSetDudeScript -> _obj_new_sid -> scriptAdd: appended after the map's critters.
        // Unverified for a map change (scriptLoadAll replaces the list head; no oracle fixture changes
        // maps yet), verified for a save load by the oracle traces.
        CritterScripts.Add(DudeSid);
        _scriptOwners[DudeSid] = dude;
    }
```

Replace `AllocateSid` with:

```csharp
    /// <summary>Runtime sid for a script-created object (engine scr_new): a fresh sid of the owner's
    /// script type, registered into the map's script table. Critters (type 4) join the critter list.</summary>
    public int AllocateSid(MapFile map, int scriptIndex, MapObject owner)
    {
        bool critter = Fid.Type(owner.Fid) is ObjectType.Critter;
        int sid = critter ? DudeSid + 1 : 0x03000000 | 0x00800000; // synthetic ranges, clear of map sids
        while (map.ScriptsBySid.ContainsKey(sid))
            sid++;
        map.ScriptsBySid[sid] = new MapScriptRecord(scriptIndex, -1, 0);
        // P108: the MapFile (and its ScriptsBySid) is re-parsed on every map load, so this scan
        // restarts and RECYCLES synthetic sids across visits — but _localVarSlices persists per map
        // NAME. Clear any stale slice a previous visit left under this key, or the new holder
        // inherits another object's local vars (fo2ce frees LVAR blocks on scriptRemove).
        _localVarSlices.Remove((map.Header.Name, sid));
        if (critter)
        {
            CritterScripts.Add(sid);
            _scriptOwners[sid] = owner;
        }
        return sid;
    }
```


Update the in-file callers:
- `BindPartyScript` (~line 444): `int sid = AllocateSid(map, scriptIndex, member);`
- `CreateObject` (~line 2540): `int sid = _host.AllocateSid(_map, scriptIndex, obj);`
- `DestroyObject` (~line 2553): before `_host.ObjectRemoved?.Invoke(obj);` add
  `_host.RemoveScript(obj.Sid); // fo2ce _obj_remove -> scriptRemove (object.cc:3905)`

- [ ] **Step 4: Update the viewer callers**

- `src/Hexwaste.Viewer/ViewerGame.CompanionHub.cs:148`:
  `member.Sid = _scriptHost.AllocateSid(_map, scriptIndex, member);`
- `src/Hexwaste.Viewer/ViewerGame.cs:4286`:
  `item.Sid = _scriptHost.AllocateSid(_map, protoIndex, item);`
- `src/Hexwaste.Viewer/ViewerGame.cs:5570`: the encounter spawn allocates inside an object initializer, before `obj` exists. Change the initializer's `Sid = ...` to `Sid = -1,` and add right after the initializer closes:

```csharp
        if (si.ScriptIndex >= 0 && _scriptHost is not null)
            obj.Sid = _scriptHost.AllocateSid(_map, si.ScriptIndex, obj);
```

- [ ] **Step 5: Build and run the tests**

```bash
dotnet build src/Hexwaste.Viewer -v q -nologo 2>&1 | grep -E " error |Błąd" ; \
FALLOUT2_DIR=$PWD/game-data dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~CritterScriptOrderTests|FullyQualifiedName~CritterScriptListTests" 2>&1 | grep -E "Powodzenie|niepowodzenie:"
```

Expected: no build errors; 11 passed.

- [ ] **Step 6: Commit**

```bash
git add src/Hexwaste.Formats/Int/ScriptHost.cs src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/ViewerGame.CompanionHub.cs tests/Hexwaste.Formats.Tests/CritterScriptOrderTests.cs
git commit -m "feat(scripts): ScriptHost owns the critter list and sid index (oracle F6)"
```

---

### Task 5: Remove the script at every kill site

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/ICombatHost.cs` (next to `RecordKill`, ~line 231)
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs:2304`
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs:496, :508, :528`
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:5518`
- Modify: `src/Hexwaste.Viewer/ViewerGame.CompanionHub.cs:127`
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs` (the `FakeCombatHost` at ~line 4472 and a new test)

**Interfaces:**
- Consumes: `ScriptHost.RemoveScript(int)` (Task 4).
- Produces:
  - `void RemoveCritterScript(MapObject critter) { }` on `ICombatHost` (default no-op);
  - `public void RemoveCritterScript(MapObject critter)` on `ViewerGame`.

Sites deliberately left alone, because the list is rebuilt after them or they never held a live sid:
- `ViewerGame.SaveLoad.cs:164` (revisit delta replay, before the rebuild);
- `ViewerGame.Party.cs:154` (a freshly rebuilt dismissed body);
- `ViewerGame.Party.cs:66` and `:132` (map exit).

- [ ] **Step 1: Write the failing test**

In `FakeCombatHost` add:

```csharp
        public List<int> RemovedScripts { get; } = [];
        public void RemoveCritterScript(MapObject critter) => RemovedScripts.Add(critter.Sid);
```

Add the test near `DamageAppliesOnAnimationCompletionAndXpPaysAtCombatEnd`:

```csharp
    [Fact]
    public void KillRemovesTheVictimsScriptBeforeClearingTheSid()
    {
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 1));
        enemy.Sid = 0x04000005;
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.True(enemy.IsDead);
        Assert.Equal([0x04000005], host.RemovedScripts); // combat.cc:4876 scriptRemove, sid still set
        Assert.Equal(-1, enemy.Sid);
    }
```

If `RunDestroyProc` on the fake host needs a script for sid `0x04000005`, check its fake implementation: it must return empty lines without throwing. Adjust only the fake, not the engine.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~KillRemovesTheVictimsScript"`
Expected: FAIL. `RemovedScripts` is empty, because the engine never calls the new hook.

- [ ] **Step 3: Implement the engine hook**

`ICombatHost.cs`, after `RecordKill`:

```csharp
    /// <summary>The victim's script leaves the critter heartbeat list (scriptRemove, combat.cc:4876), called
    /// while critter.Sid still holds the sid. Default no-op; the viewer forwards to ScriptHost.RemoveScript.</summary>
    void RemoveCritterScript(MapObject critter) { }
```

`CombatEngine.cs:2304`:

```csharp
        _host.RemoveCritterScript(critter);
        critter.Sid = -1; // the engine removes the script on death (combat.cc:4876)
```

- [ ] **Step 4: Implement the viewer side**

In `ViewerGame.CombatHost.cs`, next to `OnCritterRemoved`:

```csharp
    public void RemoveCritterScript(MapObject critter) => _scriptHost?.RemoveScript(critter.Sid);
```

Insert `RemoveCritterScript(obj);` (for the companion hub: `RemoveCritterScript(member);`) on the line before each of these assignments:
- `ViewerGame.CombatHost.cs:496`: `obj.Sid = -1;` inside the `deathFrame == 0` destroy branch
- `ViewerGame.CombatHost.cs:508`: `obj.Sid = -1;` (kill_critter_type corpse branch)
- `ViewerGame.CombatHost.cs:528`: `obj.Sid = -1;` (`KillCritterObject`)
- `ViewerGame.cs:5518`: `obj.Sid = -1;` (an encounter spawn placed dead)
- `ViewerGame.CompanionHub.cs:127`: `member.Sid = -1;` (dismiss)

- [ ] **Step 5: Run the tests**

```bash
dotnet build src/Hexwaste.Viewer -v q -nologo 2>&1 | grep -E " error " ; \
FALLOUT2_DIR=$PWD/game-data dotnet test 2>&1 | grep -E "Powodzenie|niepowodzenie:"
```

Expected: no build errors; the full suite passes (1179 + 11 + 1 = 1191 at this point).

- [ ] **Step 6: Commit**

```bash
git add src/Hexwaste.Formats/Combat/ICombatHost.cs src/Hexwaste.Formats/Combat/CombatEngine.cs src/Hexwaste.Viewer/ViewerGame.CombatHost.cs src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/ViewerGame.CompanionHub.cs tests/Hexwaste.Formats.Tests/CombatEngineTests.cs
git commit -m "feat(combat): remove a critter's script from the heartbeat list at death (oracle F6)"
```

---

### Task 6: The per-frame pump, the map-load rebuild and the vanilla-import dude

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs`
  - `LoadMap`: `_scriptHost.BindDudeScript(...)` at ~line 1883
  - the field `_critterProcTimerMs`
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs:544` (`PumpCritterProcs`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs` (the `new MapFile { ... }` copy at ~line 66, after `LoadMap(...)` at ~line 82)

**Interfaces:**
- Consumes:
  - `ScriptHost.RebuildCritterScripts`
  - `ScriptHost.CritterScripts.Next()` / `.Add()`
  - `ScriptHost.TryGetScriptOwner`
  - `MapFile.CritterSids`

- [ ] **Step 1: Rebuild the list on map load**

In `LoadMap`, replace

```csharp
            if (_dude is not null)
                _scriptHost.BindDudeScript(_map, _dude.Dude);
```

with

```csharp
            // Oracle F6: the critter heartbeat list is the map's critter section, then the dude (and, after
            // map_enter, the party via AllocateSid in InjectPartyMembers).
            _scriptHost.RebuildCritterScripts(_map);
            if (_dude is not null)
                _scriptHost.BindDudeScript(_map, _dude.Dude);
```

- [ ] **Step 2: One critter script per frame**

Find the field declaration with `grep -n "_critterProcTimerMs" src/Hexwaste.Viewer/*.cs`. Rename it to `_sneakTimerMs` and keep its type. Then replace the body of `PumpCritterProcs` (keep its gate line unchanged) so it reads:

```csharp
    /// <summary>One critter_p_proc per frame, round-robin over ScriptHost.CritterScripts — ported from
    /// fallout2-ce src/scripts.cc _script_chk_critters(), a _doBkProcesses ticker that runs once per
    /// main-loop pass (60 Hz live, 100 Hz on the 10 ms harness/oracle frames), gated like the engine's
    /// !dialog && !combat && !movie check.</summary>
    private void PumpCritterProcs(double elapsedMs)
    {
        if (_scriptHost is null || _combat.Phase != Formats.Combat.CombatPhase.Idle || _combat.IsGameOver
            || _dialog is not null || _companionHub is not null || _lootContainer is not null || _worldmapOpen)
            return;

        // P30 A-M2: the periodic sneak re-check (sneakEventProcess) is game-time driven: one reschedule
        // "tick" per 100 ms, independent of the per-frame critter heartbeat. Isolated _sneakRng.
        _sneakTimerMs += elapsedMs;
        if (_sneakTimerMs >= 100)
        {
            _sneakTimerMs = 0;
            if (_sneak.FlagSet && --_sneakTicksRemaining <= 0)
                RollSneak();
        }

        if (_scriptHost.CritterScripts.Next() is not { } sid || !_scriptHost.TryGetScriptOwner(sid, out MapObject critter))
            return; // empty list, or an owner gone this frame: the slot is consumed, as in fo2ce
        // Hexwaste deviation: a "wait here" companion's slot is consumed but its follow proc does not run
        // (phase-10 M4; vanilla handles waiting inside the party scripts).
        if (_waitingCompanions.Contains(critter))
            return;
        // Hexwaste deviation: a script without critter_p_proc runs nothing (fo2ce runs procedure 1, F8).
        var result = _scriptHost.RunObjectProc(critter, _map, _dude?.Dude, "critter_p_proc");
        if (result is not null)
            foreach (string line in result.Messages)
                Log($"{ObjectName(critter)}: {line}");
    }
```

Remove the now-unused `_critterProcIndex` field (`grep -n "_critterProcIndex" src/Hexwaste.Viewer/*.cs` must show only its declaration before you delete it). Leave `IsHeartbeatEligible` in place: the `companion-lifecycle` probe at `ViewerGame.Harness.cs:2313` still uses it.

- [ ] **Step 3: Vanilla import keeps the save's order and the doubled dude**

In `ViewerGame.VanillaImport.cs`, after `map.SpatialScripts.AddRange(saved.SpatialScripts);` add:

```csharp
        map.CritterSids.AddRange(saved.CritterSids);
```

After the `try { ... LoadMap(...) } finally { _isLoadingGame = false; }` block add the following. Match the entry count to what Task 1 observed. The code below assumes the two-entry case Task 1 expects; if Task 1 saw something else, change it to match and update the comment:

```csharp
        // fo2ce _obj_load_dude calls scriptsSetDudeScript twice (object.cc:3649/3660); the oracle traces
        // show the dude in the critter list twice after a save load (oracle F6, Task 1).
        _scriptHost?.CritterScripts.Add(Formats.Int.ScriptHost.DudeSid);
```

- [ ] **Step 4: Build, unit tests, CI steps**

```bash
dotnet build src/Hexwaste.Viewer -v q -nologo 2>&1 | grep -E " error " ; \
FALLOUT2_DIR=$PWD/game-data dotnet test 2>&1 | grep -E "Powodzenie|niepowodzenie:" ; \
./scripts/ascii-lint.sh
```

Expected: no build errors, all tests pass, `ascii-lint: clean`.

- [ ] **Step 5: Run the oracle**

```bash
for f in artemple-idle klamath-hour arcaves-ambush; do echo "== $f"; scripts/oracle-run.sh $f 2>&1 | tail -3; done
for f in artemple-idle klamath-hour; do
  for e in fo2ce hexwaste; do
    grep "critter_p_proc" scratch/oracle-runs/$f/$e.trace | head -100 | awk '{print $3,$5}' > /tmp/claude-1000/$e-$f.txt
  done
  diff /tmp/claude-1000/fo2ce-$f.txt /tmp/claude-1000/hexwaste-$f.txt > /dev/null && echo "$f critter order MATCH" || echo "$f critter order DIFF"
done
```

Expected: both fixtures print `critter order MATCH` (script name and `self=`, first 100 critter procs), and every fixture's first divergence is past line 2.
- If the order differs, compare the first differing slot against Task 1's list dump before changing anything.
- Object ids can differ for runtime-created critters (finding F12). If only `self=` ids of runtime spawns differ, that is F12, not an F6 failure. Note it and continue.

Record each fixture's new first divergence (the `DIVERGED at` block from `oracle-run.sh`) for Task 7.

- [ ] **Step 6: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/ViewerGame.CombatHost.cs src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs
git commit -m "feat(scripts): one critter_p_proc per frame over the fo2ce-ordered list (oracle F6)"
```

---

### Task 7: Goldens, runtime, docs

**Files:**
- Modify: golden fixtures under `tests/golden-combat/`, `tests/golden-opening/`, `tests/golden-endgame/`, `tests/golden-quest/`, `tests/golden-encounter/` (only the ones that move)
- Modify: `docs/research-notes/oracle-m1-findings-2026-10.md` (F6 marked FIXED, the new first divergences)
- Modify: `docs/PHASE-HISTORY.md` (one MAINTENANCE paragraph at the end)

- [ ] **Step 1: Verify each suite and list what moved**

```bash
for s in combat opening endgame quest encounter; do
  echo "== $s"; /usr/bin/time -f "$s %e s" scripts/$s-golden.sh 2>&1 | grep -E "REGRESSION|NONDETERMINISTIC|ALL PASS|FAILURES| s$"
done
```

Expected: REGRESSION lines for fixtures the per-frame heartbeat changes. Compare the runtimes with Task 1's baseline. If the total is more than 2x the baseline, stop and profile `PumpCritterProcs` / `RunObjectProc` before recording.

- [ ] **Step 2: Rule out nondeterminism**

For each fixture flagged NONDETERMINISTIC, rerun its suite. A fixture that is NONDETERMINISTIC on two consecutive runs is a real F6 problem (for example, an order depending on dictionary iteration). Fix it before recording. A one-off flag that passes on the rerun is the pre-existing flake noted in the findings doc.

- [ ] **Step 3: Record suite by suite and review each diff**

```bash
scripts/<suite>-golden.sh record
git diff --stat tests/golden-<suite>/
git diff tests/golden-<suite>/ | head -200
```

Review rule for each changed fixture:
- Accept: more or different NPC movement, float text or log lines, different RNG-dependent outcomes, and shifted later state.
- Reject (investigate instead): missing sections, a crash or exception line, an empty fixture, or a fixture whose scenario no longer reaches its end marker.

Revert a rejected fixture with `git checkout -- <file>` and investigate it before continuing.

- [ ] **Step 4: Confirm the recorded state is stable**

```bash
for s in combat opening endgame quest encounter; do scripts/$s-golden.sh 2>&1 | grep -E "golden|e2e"; done
```

Run this twice. Expected: every suite reports ALL PASS both times (a single pre-existing flake that passes on rerun is acceptable; name it in the commit message).

- [ ] **Step 5: Commit the goldens**

```bash
git add tests/golden-*
git commit -m "test(golden): re-record for the per-frame critter heartbeat (oracle F6)

<list each moved fixture and a one-line reason, e.g. 'denbus1-walk: NPC wander moves earlier'>
Golden runtime: <baseline> s -> <after> s."
```

- [ ] **Step 6: Update the docs**

In `docs/research-notes/oracle-m1-findings-2026-10.md`, change the F6 heading to `### F6 — critter_p_proc cadence and order — FIXED` and add under it:

```markdown
**FIXED.** `ScriptHost.CritterScripts` (`CritterScriptList`) holds the critter sids in fo2ce's order
(file order on load, append on add, tail-into-slot on remove, dude twice after a vanilla save load) and
`PumpCritterProcs` runs one per frame. The critter_p_proc order now matches fo2ce in artemple-idle and
klamath-hour. New first divergences: <artemple: line N, what>; <klamath: line N, what>;
<arcaves: line N, what>. Still unverified: the list order after a map change (no oracle fixture
changes maps).
```

Append to `docs/PHASE-HISTORY.md`, after the trace-oracle MAINTENANCE paragraph:

```markdown
MAINTENANCE (2026-10-11, "oracle F6"): the critter heartbeat is now fo2ce's `_script_chk_critters`:
one `critter_p_proc` per frame (60 Hz live, 100 Hz on harness/oracle 10 ms frames), round-robin over
`ScriptHost.CritterScripts` in fo2ce's list order, all elevations, scripts removed at the moment of death
(`ICombatHost.RemoveCritterScript`). Golden fixtures re-recorded (<N> moved, diff-reviewed); golden
runtime <baseline> s -> <after> s. Spec/plan: `docs/superpowers/specs/2026-10-11-oracle-f6-critter-cadence-design.md`,
`docs/superpowers/plans/2026-10-11-oracle-f6-critter-cadence.md`.
```

- [ ] **Step 7: Full check and commit**

```bash
FALLOUT2_DIR=$PWD/game-data dotnet test 2>&1 | grep -E "Powodzenie|niepowodzenie:"
./scripts/ascii-lint.sh && ./scripts/harness-selftest.sh 2>&1 | tail -1
git add docs/research-notes/oracle-m1-findings-2026-10.md docs/PHASE-HISTORY.md
git commit -m "docs(oracle): F6 fixed; new first divergences"
```

Expected: all tests pass, `ascii-lint: clean`, `harness self-test: 10 passed, 0 failed`.
