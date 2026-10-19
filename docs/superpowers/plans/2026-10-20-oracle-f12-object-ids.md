# Oracle F12 object ids Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Synthetic and runtime objects get their ids from a port of fo2ce's object id counter, so the oracle trace labels them like fo2ce (artemple-idle moves past line 3011, `self=obj#16` vs `obj#-2`).

**Architecture:** A small `ObjectIds` class (port of `scriptsNewObjectId`, start value 12) lives in `Hexwaste.Formats.Map`; `ScriptHost` owns one and exposes `NewObjectId()` with a viewer-installed in-use check. The map-script owner is built once per map load (two ids), every SPAWN site takes its ids from the counter, and a `MapObject.Spawned` flag replaces the harness's `Id == -3` marker.

**Tech Stack:** C# / .NET 10, xUnit. Spec: `docs/superpowers/specs/2026-10-20-oracle-f12-object-ids-design.md`.

## Global Constraints

- `ObjectIds.Current` starts at 12; `Next(inUse)` = `do Current++; while (inUse(Current)); return ++Current;` (only the first increment is checked).
- In-use set: objects on a map tile (`HexTile >= 0`), all elevations, plus the dude. Inventory items are not in it.
- Ids per site: map-script owner 2 (keeps the second); `CreateObject` 1, +1 when `scriptIndex >= 0`; script money 1; encounter critter 1, +1 when scripted, then each item 1; stack split 1; ammo box 1; explosion marker 1. SPAWN prints the first id; the object keeps the last.
- `CreateObject`: SPAWN and MOVE show the first id (fo2ce places before `scriptAdd`). Encounter critter: the second id is taken before placement, so MOVE shows the final id and comes before the item SPAWN lines.
- One map-script owner per map load, stored with the map's name; a proc for another map never reuses it.
- Spatial stand-in `Id = -1`.
- Shortcut comments, verbatim:
  - `shortcut: the start value is observed, not derived; upgrade if a fixture's first map-script id is not 16`
  - `shortcut: fo2ce creation paths Hexwaste does not model do not advance the counter (projectiles actions.cc:780, explosion ring actions.cc:867/1608, hidden barter/loot boxes inventory.cc:4123/5060, the spatial owner scriptGetSelf creates on self_obj); upgrade when a fixture shows a SPAWN gap`
  - `shortcut: objects rebuilt from a Hexwaste save keep -4, fo2ce restores their saved ids; upgrade if save round trips need stable ids`
- Viewer sites with no script host keep their current constant id.
- Port comments: `// ported from fallout2-ce src/x.cc f()`; alexbatalov e97087b is authoritative.
- No non-ASCII characters in C# string literals (CI runs `scripts/ascii-lint.sh`).
- No new dependencies. Never commit game assets.
- Commit trailers on every commit:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk
  ```

---

### Task 1: ObjectIds counter + ScriptHost sites

**Files:**
- Create: `src/Hexwaste.Formats/Map/ObjectIds.cs`
- Modify: `src/Hexwaste.Formats/Map/MapFile.cs` (`MapObject`, add `Spawned`)
- Modify: `src/Hexwaste.Formats/Int/ScriptHost.cs` (after `DudeTraceSid` ~line 398; `GetSpatialSelf` ~line 606; money ~line 791; `SynthMapScriptOwner` ~line 1121; `RunStartProcs` ~1178; `RunMapEnter` ~1197; `RunMapUpdate` ~1243; `RunMapExit` ~1325; `RunMapCombatOver` ~1349; `ScriptContext.CreateObject` ~2650)
- Test: `tests/Hexwaste.Formats.Tests/ObjectIdsTests.cs` (new)

**Interfaces:**
- Produces: `Hexwaste.Formats.Map.ObjectIds` (`int Current`, `int Next(Func<int, bool> inUse)`); `ScriptHost.ObjectIds`, `ScriptHost.ObjectIdInUse` (`Func<int, bool>`, settable, default `_ => false`), `int ScriptHost.NewObjectId()`; `bool MapObject.Spawned { get; set; }`.

- [ ] **Step 1: Write the failing tests** (new file `tests/Hexwaste.Formats.Tests/ObjectIdsTests.cs`)

```csharp
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;
using Hexwaste.Formats.Trace;

namespace Hexwaste.Formats.Tests;

/// <summary>Oracle F12: object ids from fo2ce's scriptsNewObjectId counter. Hermetic: a 12-byte empty DAT2,
/// hand-assembled idtest.int (map_enter/map_update empty; critter_p_proc creates an unscripted then a
/// scripted misc object), and a 28-byte misc proto.</summary>
[Collection(nameof(OracleTraceTests))] // uses the static OracleTrace
public class ObjectIdsTests : IDisposable
{
    private const int MiscPid = 0x05000001;
    private readonly string _tempDir;

    public ObjectIdsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hexwaste-objectids-" + Guid.NewGuid().ToString("N"));
        string scriptsDir = Path.Combine(_tempDir, "data", "scripts");
        string miscDir = Path.Combine(_tempDir, "data", "proto", "misc");
        Directory.CreateDirectory(scriptsDir);
        Directory.CreateDirectory(miscDir);
        File.WriteAllBytes(Path.Combine(_tempDir, "master.dat"),
            [0, 0, 0, 0, 4, 0, 0, 0, 12, 0, 0, 0]); // the 12-byte empty DAT2
        File.WriteAllText(Path.Combine(scriptsDir, "scripts.lst"), "idtest.int ; # local_vars=0\n");
        File.WriteAllBytes(Path.Combine(scriptsDir, "idtest.int"), TestIntScript.Build(
            ("map_enter_p_proc", _ => { }),
            ("map_update_p_proc", _ => { }),
            ("critter_p_proc", c =>
            {
                foreach (int sid in new[] { -1, 1 }) // unscripted, then scripted (sid is 1-based)
                {
                    TestIntScript.PushInt(c, MiscPid);
                    TestIntScript.PushInt(c, 12345); // tile
                    TestIntScript.PushInt(c, 0);     // elevation
                    TestIntScript.PushInt(c, sid);
                    TestIntScript.Op(c, 0x80B7);     // create_object_sid
                    TestIntScript.Op(c, 0x801A);     // pop the handle
                }
            })));
        File.WriteAllText(Path.Combine(miscDir, "misc.lst"), "test.pro\n");
        var pro = new List<byte>();
        foreach (int v in new[] { MiscPid, 0, Fid.Build(ObjectType.Misc, 1), 0, 0, 0, 0 })
            pro.AddRange([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]); // big-endian
        File.WriteAllBytes(Path.Combine(miscDir, "test.pro"), [.. pro]);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private ScriptHost MakeHost()
    {
        GameFileSystem vfs = GameFileSystem.Open(_tempDir);
        return new ScriptHost(vfs, ScriptList.Load(vfs), new ProtoDatabase(vfs));
    }

    private static MapFile ScriptedMap() => new()
    {
        Header = new MapHeader(20, "idtest.map", 0, 0, 0, 0, 1, 0, 0, 0, 0, 0), // ScriptIndex 1 = idtest.int
        GlobalVariables = [],
        LocalVariables = [],
        Elevations = new MapElevation?[MapFile.ElevationCount],
    };

    private static string Traced(Action run)
    {
        var sw = new StringWriter();
        try
        {
            OracleTrace.Open(sw, "hdr");
            run();
        }
        finally
        {
            OracleTrace.Close();
        }
        return sw.ToString();
    }

    [Fact]
    public void NextReturnsEvenIdsFromTwelve()
    {
        var ids = new ObjectIds();
        Assert.Equal(14, ids.Next(_ => false));
        Assert.Equal(16, ids.Next(_ => false));
    }

    [Fact]
    public void OnlyTheFirstIncrementIsChecked() => // 13 in use: 14 is checked and free, 15 is returned
        Assert.Equal(15, new ObjectIds().Next(id => id == 13));

    [Fact]
    public void MapScriptOwnerIsObj16AcrossTheLoadPasses()
    {
        ScriptHost host = MakeHost();
        MapFile map = ScriptedMap();
        string trace = Traced(() =>
        {
            host.RunStartProcs(map, [], null);
            host.RunMapEnter(map, [], null);
            host.RunMapUpdate(map, [], null);
        });
        Assert.Contains("PROC idtest.int map_enter_p_proc self=obj#16", trace);
        Assert.Contains("PROC idtest.int map_update_p_proc self=obj#16", trace);
        Assert.DoesNotContain("obj#-2", trace);
    }

    [Fact]
    public void CreateObjectTakesASecondIdOnlyWhenScripted()
    {
        ScriptHost host = MakeHost();
        MapFile map = ScriptedMap();
        map.Elevations[0] = new MapElevation { Squares = new int[MapElevation.SquareGridSize] };
        const int OwnerSid = 0x03000001;
        map.ScriptsBySid[OwnerSid] = new MapScriptRecord(0, -1, 0);
        var owner = new MapObject
        {
            Id = 100, HexTile = 200, X = 0, Y = 0, Frame = 0, Rotation = 0,
            Fid = Fid.Build(ObjectType.Scenery, 1), Flags = 0, Pid = 0x02000001, Sid = OwnerSid,
        };
        string trace = Traced(() =>
        {
            host.RunStartProcs(map, [], null);                                        // owner: 14, 16
            host.RunScriptProc(OwnerSid, owner, map, null, 0, -1, "critter_p_proc"); // 18; 20 then 22
        });
        Assert.Contains("SPAWN obj#18 pid=0x05000001", trace);
        Assert.Contains("SPAWN obj#20 pid=0x05000001", trace);
        List<MapObject> created = map.Elevations[0]!.Objects.Where(o => o.Pid == MiscPid).ToList();
        Assert.Equal([18, 22], created.Select(o => o.Id));
        Assert.All(created, o => Assert.True(o.Spawned));
    }
}
```

Notes: if `RunScriptProc` does not accept a null dude, pass `owner`. If the map-script `start` pass or `RunMapEnter` requires `map.Elevations[0]`, set it in `MapScriptOwnerIsObj16AcrossTheLoadPasses` the same way as the last test. If the PROC line spells the program differently (check `ScriptHost.TraceProc`, ~line 964), match its exact spelling; the assertions that matter are `self=obj#16` and the absence of `obj#-2`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Hexwaste.Formats.Tests -v q --nologo --filter "FullyQualifiedName~ObjectIdsTests"`
Expected: build FAIL (`ObjectIds`, `MapObject.Spawned` not defined).

- [ ] **Step 3: Create `src/Hexwaste.Formats/Map/ObjectIds.cs`**

```csharp
namespace Hexwaste.Formats.Map;

/// <summary>fo2ce's object id counter (oracle F12): every created object takes its id from here. The
/// in-use check is fo2ce's objectFindFirst/objectFindNext walk: objects on a map tile, all elevations.</summary>
// shortcut: fo2ce creation paths Hexwaste does not model do not advance the counter (projectiles actions.cc:780, explosion ring actions.cc:867/1608, hidden barter/loot boxes inventory.cc:4123/5060, the spatial owner scriptGetSelf creates on self_obj); upgrade when a fixture shows a SPAWN gap
public sealed class ObjectIds
{
    // _cur_id starts at 4 (scripts.cc:189) and is never reset; 12 is its value before the map load on the
    // oracle's launch path (main menu, load a slot), observed in both fixtures with a map script (obj#16).
    // shortcut: the start value is observed, not derived; upgrade if a fixture's first map-script id is not 16
    public int Current { get; private set; } = 12;

    /// <summary>ported from fallout2-ce src/scripts.cc scriptsNewObjectId(): only the first increment is
    /// checked against ids in use.</summary>
    public int Next(Func<int, bool> inUse)
    {
        do
            Current++;
        while (inUse(Current));
        return ++Current;
    }
}
```

- [ ] **Step 4: Add `MapObject.Spawned`** (`src/Hexwaste.Formats/Map/MapFile.cs`, next to `Team` ~line 142)

```csharp
    /// <summary>Created at runtime by a script (create_object_sid) or an encounter; the harness counts these.
    /// Not saved. Replaces the old Id == -3 marker (oracle F12: ids now come from the counter).</summary>
    public bool Spawned { get; set; }
```

- [ ] **Step 5: `ScriptHost` counter** (after the `DudeTraceSid` property, ~line 398)

```csharp
    /// <summary>Oracle F12: fo2ce's object id counter, shared by every object this session creates.</summary>
    public ObjectIds ObjectIds { get; } = new();

    /// <summary>True when an object on a map tile (any elevation) has the id; installed by the viewer.</summary>
    public Func<int, bool> ObjectIdInUse { get; set; } = _ => false;

    public int NewObjectId() => ObjectIds.Next(ObjectIdInUse);
```

- [ ] **Step 6: Map-script owner, one per map load**

Replace `SynthMapScriptOwner()` (~line 1119-1134, including its doc comment) with:

```csharp
    private (string Map, MapObject Owner)? _mapScriptOwner;

    /// <summary>The map script's owner object, one per map load: fo2ce map.cc:957-972 creates it with
    /// objectCreateWithFidPid (one id), then map.cc:970 takes the id it keeps. <paramref name="fresh"/> = a new
    /// map load (RunStartProcs); other passes reuse it, building one if none exists for this map.</summary>
    private MapObject MapScriptOwner(MapFile map, bool fresh = false)
    {
        if (!fresh && _mapScriptOwner is { } cached && cached.Map == map.Header.Name)
            return cached.Owner;
        NewObjectId(); // objectCreateWithFidPid
        var owner = new MapObject
        {
            Id = NewObjectId(), // map.cc:970
            HexTile = 1,
            X = 0,
            Y = 0,
            Frame = 0,
            Rotation = 0,
            Fid = Fid.Build(ObjectType.Misc, 12),
            Flags = 0,
            Pid = 0x05000010,
            Sid = -1,
        };
        _mapScriptOwner = (map.Header.Name, owner);
        return owner;
    }
```

Then:
- `RunStartProcs`: `SynthMapScriptOwner()` -> `MapScriptOwner(map, fresh: true)`.
- `RunMapEnter`: delete the `// The map script has no real owner object; synthesize one.` comment and the `var owner = new MapObject { ... };` block; pass `MapScriptOwner(map)` instead of `owner` to `RunProc`.
- `RunMapUpdate`: same as `RunMapEnter`.
- `RunMapExit` and `RunMapCombatOver`: `SynthMapScriptOwner()` -> `MapScriptOwner(map)`.

Check with `grep -n "Id = -2\|SynthMapScriptOwner" src/Hexwaste.Formats/Int/ScriptHost.cs` (expect no output).

- [ ] **Step 7: Spatial stand-in and money**

In `GetSpatialSelf` (~line 606) change `Id = -5,` to:

```csharp
                Id = -1, // fo2ce: a spatial script has no owner until self_obj (scriptGetSelf), so it traces obj#-1
```

In the money block of `CapsAdjust` (~line 791) change `Id = -5,` to `Id = NewObjectId(),` (the existing `// fo2ce item.cc:3236 objectCreateWithPid(MONEY)` comment on the SPAWN line stays).

- [ ] **Step 8: `ScriptContext.CreateObject`** (~line 2663)

Change `Id = -3,` to `Id = _host.NewObjectId(), // objectCreateWithFidPid` and add `Spawned = true,` to the initializer. In the existing `if (scriptIndex >= 0)` block, after `obj.Sid = sid;`, add:

```csharp
                obj.Id = _host.NewObjectId(); // interpreter_extra.cc:938, after objectSetLocation: SPAWN/MOVE showed the first id
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `FALLOUT2_DIR=/home/eko/dev/FPOC/game-data dotnet test tests/Hexwaste.Formats.Tests -v q --nologo`
Expected: all pass (report pass/skip/fail counts). Then `scripts/ascii-lint.sh` (exit 0). The viewer still compiles (`dotnet build src/Hexwaste.Viewer -v q -nologo`, 0 errors); its `-3` checks are fixed in Task 2.

- [ ] **Step 10: Commit**

```bash
git add src/Hexwaste.Formats/Map/ObjectIds.cs src/Hexwaste.Formats/Map/MapFile.cs src/Hexwaste.Formats/Int/ScriptHost.cs tests/Hexwaste.Formats.Tests/ObjectIdsTests.cs
git commit -m "feat(trace): object ids from fo2ce's counter for the map-script owner, script spawns and money (oracle F12)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk"
```

---

### Task 2: Viewer sites + harness marker + goldens

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs` (`_scriptHost` initializer ~line 1233; stack split ~line 4952; encounter spawn ~lines 5575-5618)
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs` (ammo box ~line 390; explosion marker ~line 1050)
- Modify: `src/Hexwaste.Viewer/ViewerGame.SaveLoad.cs` (`RebuildObject` ~line 249, comment only)
- Modify: `src/Hexwaste.Viewer/ViewerGame.Harness.cs` (lines ~186, 193, 219, 370, 398)

**Interfaces:**
- Consumes: `ScriptHost.ObjectIdInUse`, `ScriptHost.NewObjectId()`, `MapObject.Spawned` (Task 1).

- [ ] **Step 1: Install the in-use check** in the `_scriptHost = new Formats.Int.ScriptHost(...) { ... }` initializer (~line 1233), next to `ObjectPlaced`:

```csharp
                // Oracle F12: fo2ce's scriptsNewObjectId skips ids used by objects on a map tile, all elevations.
                ObjectIdInUse = id => _dude?.Dude.Id == id || _map.Elevations.Any(e => e is not null
                    && e.Objects.Any(o => o.Id == id && o.HexTile >= 0)),
```

- [ ] **Step 2: Stack split** (~line 4952): `Id = -4,` -> `Id = _scriptHost?.NewObjectId() ?? -4, // fo2ce item.cc:435 _obj_copy`.

- [ ] **Step 3: Ammo box and explosion marker** (`ViewerGame.CombatHost.cs`)
- Ammo box (~line 390), inside `if (RebuildObject(typePid, 1) is { } box)`, first statement:
  ```csharp
              box.Id = _scriptHost?.NewObjectId() ?? box.Id; // fo2ce item.cc:1893 objectCreateWithPid(ammo)
  ```
- Explosion marker (~line 1050): `Id = -7,` -> `Id = _scriptHost?.NewObjectId() ?? -7,`.

- [ ] **Step 4: Encounter spawn** (`ViewerGame.cs` ~lines 5575-5618). Change the initializer's `Id = -3, // script-created marker, like ScriptContext.CreateObject` to:

```csharp
            Id = _scriptHost?.NewObjectId() ?? -3, // objectCreateWithPid
            Spawned = true,
```

Then replace the trace/sid/item/MOVE sequence so it runs in fo2ce's order (`worldmap.cc:3825-3870`: create, `_obj_new_sid_inst`, place, then items). The block from `if (si.ScriptIndex >= 0 && _scriptHost is not null)` through the final `Move` becomes:

```csharp
        if (si.ScriptIndex >= 0 && _scriptHost is not null)
            obj.Sid = _scriptHost.AllocateSid(_map, si.ScriptIndex, obj);
        // Trace oracle: fo2ce worldmap.cc wmSetupCritterObjs - objectCreateWithPid (SPAWN, tile -1),
        // _obj_new_sid_inst (a second id when scripted), objectSetLocation (critter MOVE), then each
        // inventory item's objectCreateWithPid (SPAWN).
        if (Formats.Trace.OracleTrace.Active)
            Formats.Trace.OracleTrace.Spawn(obj.Id, obj.Pid, -1);
        if (si.ScriptIndex >= 0 && _scriptHost is not null)
            obj.Id = _scriptHost.NewObjectId(); // proto_instance.cc:158 _obj_new_sid_inst
        if (Formats.Trace.OracleTrace.Active && Fid.PidType(obj.Pid) == (int)ObjectType.Critter)
            Formats.Trace.OracleTrace.Move(obj.Id, -1, obj.HexTile);
```

followed by the unchanged `if (Fid.Type(obj.Fid) is ObjectType.Critter) { ... }` team/HP block, then the item loop with its first statements becoming:

```csharp
            if (RebuildObject(it.Pid, it.Count) is not { } item)
                continue;
            item.Id = _scriptHost?.NewObjectId() ?? item.Id; // objectCreateWithPid
```

and the old `Move` call after the item loop deleted. Keep `return obj;`.

- [ ] **Step 5: `RebuildObject` comment** (`ViewerGame.SaveLoad.cs` ~line 249): on the `Id = -4,` line add

```csharp
                Id = -4, // shortcut: objects rebuilt from a Hexwaste save keep -4, fo2ce restores their saved ids; upgrade if save round trips need stable ids
```

- [ ] **Step 6: Harness marker** (`ViewerGame.Harness.cs`): replace each `o.Id == -3` with `o.Spawned` (5 places). Check with `grep -n "Id == -3" src/Hexwaste.Viewer/*.cs` (expect no output).

- [ ] **Step 7: Build, test, goldens**

```bash
FALLOUT2_DIR=/home/eko/dev/FPOC/game-data dotnet test tests/Hexwaste.Formats.Tests -v q --nologo
scripts/ascii-lint.sh
dotnet build src/Hexwaste.Viewer -c Release -v q -nologo
```

Expected: all tests pass (report counts), lint clean, 0 errors. Then run the golden suites SEQUENTIALLY (never two at once, never alongside an oracle run), each in check mode:

```bash
for s in combat quest endgame opening encounter; do scripts/$s-golden.sh > scratch/f12-golden-$s.out 2>&1; echo "$s rc=$?"; done
```

Expected: all rc=0 (scripts never read ids, so no fixture should move). The encounter suite has a known PRE-EXISTING native crash flake (SIGSEGV/SIGABRT, empty fixture output); if only encounter fails, rerun it once and report both runs. If any fixture's output changed (not a crash), STOP and report the fixture names and the diff; do not re-record.

- [ ] **Step 8: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/ViewerGame.CombatHost.cs src/Hexwaste.Viewer/ViewerGame.SaveLoad.cs src/Hexwaste.Viewer/ViewerGame.Harness.cs
git commit -m "feat(trace): viewer spawn sites take fo2ce counter ids; harness counts MapObject.Spawned (oracle F12)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk"
```

---

### Task 3: Oracle rerun + findings doc

**Files:**
- Modify: `docs/research-notes/oracle-m1-findings-2026-10.md` (F12 section ~line 259, "Run results" ~line 9, "Recommended next steps" ~line 378)

**Interfaces:**
- Consumes: Tasks 1-2 (no code changes in this task).

- [ ] **Step 1: Run the oracle, sequentially** (each a few minutes; timeout 600000 ms)

```bash
scripts/oracle-run.sh artemple-idle  > scratch/oracle-runs/artemple-idle-run.out 2>&1
scripts/oracle-run.sh klamath-hour   > scratch/oracle-runs/klamath-hour-run.out 2>&1
scripts/oracle-run.sh arcaves-ambush > scratch/oracle-runs/arcaves-ambush-run.out 2>&1
pgrep -f build-oracle/fallout2-ce && pkill -9 -f build-oracle/fallout2-ce
```

Take each first divergence from the TraceDiff output in the `*-run.out` file (a plain diff of the trace files gives different line numbers). Expected: artemple past line 3011; klamath still 1664 (`MOVE obj#26 17500->17701` vs `kcitizen critter_p_proc obj#156`); arcaves still 9. If artemple still differs on a `self=obj#` label for the map script, STOP and report (do not change code). Also count the map-script and SPAWN ids in both klamath traces:

```bash
grep -o "kladwtwn.int map_update_p_proc self=obj#[0-9-]*" scratch/oracle-runs/klamath-hour/hexwaste.trace | sort | uniq -c
grep "SPAWN" scratch/oracle-runs/klamath-hour/fo2ce.trace | head -5
grep "SPAWN" scratch/oracle-runs/klamath-hour/hexwaste.trace | head -5
```

Record whether the SPAWN ids match (they come after klamath's line 1664, so they are evidence only).

- [ ] **Step 2: Update the findings doc**

- F12 section: `Status: FIXED` with the counter rule (fo2ce `scriptsNewObjectId`, `scripts.cc:526`: only the first increment is checked; `_cur_id` starts at 4, never reset; observed 12 before the map load on the oracle's launch path), this site table:

  ```
  | site | fo2ce | ids |
  |---|---|---|
  | map-script owner | map.cc:957-972 | 2 |
  | create_object_sid | opCreateObject, interpreter_extra.cc:938 | 1, +1 when scripted |
  | script money | item.cc:3236 | 1 |
  | encounter critter | wmSetupCritterObjs, _obj_new_sid_inst | 1, +1 when scripted; each item 1 |
  | stack split | item.cc:435 _obj_copy | 1 |
  | ammo box | item.cc:1893 | 1 |
  | explosion marker | actions.cc:1595 | 1 |
  ```

  the unmodeled creation paths (projectiles `actions.cc:780`, explosion ring `actions.cc:867/1608`, hidden barter/loot boxes `inventory.cc:4123/5060`, the spatial owner created on `self_obj`), the spatial label (-1), the klamath SPAWN comparison from Step 1, and the encounter trace-order fix (MOVE before the item SPAWN lines).
- Run results / Recommended next steps: the new first divergences from Step 1.

- [ ] **Step 3: Commit**

```bash
git add docs/research-notes/oracle-m1-findings-2026-10.md
git commit -m "docs(oracle): F12 fixed; new first divergences

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk"
```
