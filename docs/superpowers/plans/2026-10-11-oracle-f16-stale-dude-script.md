# Oracle F16 (stale obj_dude script) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After a vanilla save load, the stale obj_dude critter slot (sid `0x04004650`) runs its own script
state (own LVARs, own VM), and map_enter / map_update run object scripts in fo2ce list order (critter list
last, by sid), so the oracle traces move past F16.

**Architecture:** One new `ScriptHost.RunScriptProc(sid, self, ...)` (run a sid's script acting as `self`);
`RunObjectProc` becomes a call to it. The critter heartbeat runs the sid it takes from the list. A private
`ScriptHost.RunObjectScriptsInListOrder` replaces the object loops of `RunMapEnter` / `RunMapUpdate`:
non-critter-list objects first (caller order), then `CritterScripts` in list order, limited to owners the
caller passed. Spec: `docs/superpowers/specs/2026-10-11-oracle-f16-stale-dude-script-design.md`.

**Tech Stack:** C# / .NET 10, xUnit, the fo2ce trace oracle (`scripts/oracle-run.sh`).

## Global Constraints

- Port from fallout2-ce with `// ported from fallout2-ce src/x.cc f()`; alexbatalov e97087b is authoritative.
- Never copy, embed or commit game assets; no game text quotes. Tests needing game data use `[GameDataFact]`.
- No new dependencies.
- CI runs `scripts/ascii-lint.sh`: no non-ASCII characters in C# string literals.
- Commit messages end with:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk
  ```
- Work on branch `feat/oracle-f16-stale-dude-script` (already checked out).
- Goldens: run suites sequentially, never concurrently with engine runs. A moved fixture is run twice for
  determinism, diff-reviewed, re-recorded, and listed in the commit message.
- Never leave hung fo2ce processes: `pkill -9 -f build-oracle/fallout2-ce` after oracle runs if any remain.

---

### Task 1: `RunScriptProc` and the heartbeat runs the slot's sid

**Files:**
- Create: `tests/Hexwaste.Formats.Tests/TestIntScript.cs`
- Create: `tests/Hexwaste.Formats.Tests/StaleDudeScriptTests.cs`
- Modify: `src/Hexwaste.Formats/Int/ScriptHost.cs` (`RunObjectProc` at ~line 876)
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs` (`PumpCritterProcs`, ~line 570)
- Modify: `src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs` (comment at lines 77-79)

**Interfaces:**
- Produces: `public ScriptRunResult? ScriptHost.RunScriptProc(int sid, MapObject self, MapFile map,
  MapObject? dude, int fixedParam, int actionBeingUsed, params string[] procedureNames)`;
  test helper `TestIntScript.Build(params (string Name, Action<List<byte>> Body)[] procs)` with
  `TestIntScript.Op(List<byte>, ushort)` and `TestIntScript.PushInt(List<byte>, int)`.

- [ ] **Step 1: Write the test helper** (hand-assembled `.int`, same layout as
  `VmGlobalsPersistenceTests.BuildTwoProcScript`, generalized to N procs; that test stays unchanged)

`tests/Hexwaste.Formats.Tests/TestIntScript.cs`:
```csharp
namespace Hexwaste.Formats.Tests;

/// <summary>Hand-assembles a minimal compiled .int for hermetic ScriptHost tests. Layout copied from
/// VmGlobalsPersistenceTests (the compiler's 42-byte stub, one module global, void-proc epilogue).</summary>
internal static class TestIntScript
{
    public static void Op(List<byte> code, ushort opcode)
    {
        code.Add((byte)(opcode >> 8));
        code.Add((byte)opcode);
    }

    public static void PushInt(List<byte> code, int value)
    {
        Op(code, 0xC001); // push, int type bits
        Int32(code, value);
    }

    private static void Int32(List<byte> bytes, int value)
    {
        bytes.Add((byte)(value >> 24));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static void Epilogue(List<byte> code)
    {
        PushInt(code, 0);   // return value
        Op(code, 0x800D);   // d_to_a
        Op(code, 0x8019);   // swapa
        Op(code, 0x802A);   // pop_to_base
        Op(code, 0x8029);   // pop_base
        Op(code, 0x800C);   // a_to_d
        Op(code, 0x801C);   // pop_return
    }

    public static byte[] Build(params (string Name, Action<List<byte>> Body)[] procs)
    {
        var idBytes = new List<byte>();
        var nameOffsets = new int[procs.Length];
        for (int i = 0; i < procs.Length; i++)
        {
            string name = procs[i].Name;
            idBytes.Add((byte)((name.Length + 1) >> 8));
            idBytes.Add((byte)(name.Length + 1));
            nameOffsets[i] = 4 + idBytes.Count;
            idBytes.AddRange(System.Text.Encoding.Latin1.GetBytes(name));
            idBytes.Add(0);
        }

        int codeStart = 42 + 4 + procs.Length * 24 + 4 + idBytes.Count + 4;

        var prologue = new List<byte>();
        Op(prologue, 0x802C);      // set_global
        PushInt(prologue, 0);      // global[0] := 0
        Op(prologue, 0x8003);      // leave_critical_section
        Op(prologue, 0x801C);      // pop_return -> 18

        var bodies = new List<List<byte>>();
        foreach ((string _, Action<List<byte>> body) in procs)
        {
            var code = new List<byte>();
            Op(code, 0x802B);      // push_base
            body(code);
            Epilogue(code);
            bodies.Add(code);
        }

        var file = new List<byte>();
        Op(file, 0x8002);          //  0: enter_critical_section
        PushInt(file, 18);         //  2: push 18
        Op(file, 0x800D);          //  8: d_to_a
        PushInt(file, codeStart);  // 10: push prologue address
        Op(file, 0x8004);          // 16: jump
        Op(file, 0x8010);          // 18: exit_program
        Op(file, 0x801A);          // 20: pop
        Op(file, 0x8020);          // 22: pop_flags_return
        Op(file, 0x801A);          // 24: pop
        Op(file, 0x8021);          // 26: pop_flags_exit
        Op(file, 0x801A);          // 28: pop
        Op(file, 0x8022);          // 30
        Op(file, 0x801A);          // 32: pop
        Op(file, 0x8023);          // 34
        Op(file, 0x8024);          // 36
        Op(file, 0x8025);          // 38
        Op(file, 0x8026);          // 40

        Int32(file, procs.Length);
        int at = codeStart + prologue.Count;
        for (int i = 0; i < procs.Length; i++)
        {
            Int32(file, nameOffsets[i]);
            Int32(file, 0);
            Int32(file, 0);
            Int32(file, 0);
            Int32(file, at);
            Int32(file, 0);
            at += bodies[i].Count;
        }
        Int32(file, idBytes.Count);
        file.AddRange(idBytes);
        Int32(file, unchecked((int)0xFFFFFFFF)); // static strings absent
        file.AddRange(prologue);
        foreach (List<byte> body in bodies)
            file.AddRange(body);
        return file.ToArray();
    }
}
```

- [ ] **Step 2: Write the failing test**

`tests/Hexwaste.Formats.Tests/StaleDudeScriptTests.cs`:
```csharp
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

/// <summary>Oracle F16: a sid runs its own script state even when another sid shares its owner (the
/// vanilla import's stale obj_dude slot). Hermetic: hand-assembled lvartest.int, 1 LVAR:
///   critter_p_proc:    LVAR[0] := 99
///   map_update_p_proc: GVAR[777] := LVAR[0]
///   map_enter_p_proc:  (empty)</summary>
[Collection(nameof(OracleTraceTests))] // Task 2's order test uses the static OracleTrace
public class StaleDudeScriptTests : IDisposable
{
    private const int StaleSid = 0x04004650;
    private readonly string _tempDir;

    public StaleDudeScriptTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hexwaste-staledude-" + Guid.NewGuid().ToString("N"));
        string scriptsDir = Path.Combine(_tempDir, "data", "scripts");
        Directory.CreateDirectory(scriptsDir);
        File.WriteAllBytes(Path.Combine(_tempDir, "master.dat"),
            [0, 0, 0, 0, 4, 0, 0, 0, 12, 0, 0, 0]); // the 12-byte empty DAT2
        File.WriteAllText(Path.Combine(scriptsDir, "scripts.lst"), "lvartest.int ; # local_vars=1\n");
        File.WriteAllBytes(Path.Combine(scriptsDir, "lvartest.int"), TestIntScript.Build(
            ("critter_p_proc", c =>
            {
                TestIntScript.PushInt(c, 0);           // LVAR index
                TestIntScript.PushInt(c, 99);          // value
                TestIntScript.Op(c, 0x80C2);           // set_local_var
            }),
            ("map_update_p_proc", c =>
            {
                TestIntScript.PushInt(c, 777);         // GVAR index
                TestIntScript.PushInt(c, 0);           // LVAR index
                TestIntScript.Op(c, 0x80C1);           // get_local_var
                TestIntScript.Op(c, 0x80C6);           // set_global_var
            }),
            ("map_enter_p_proc", _ => { })));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private ScriptHost MakeHost()
    {
        GameFileSystem vfs = GameFileSystem.Open(_tempDir);
        return new ScriptHost(vfs, ScriptList.Load(vfs), new ProtoDatabase(vfs));
    }

    private static MapObject Obj(int id, int sid, ObjectType type) => new()
    {
        Id = id, HexTile = 100 + id, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = Fid.Build(type, 1), Flags = 0, Pid = 0x01000002, Sid = sid,
    };

    private static MapFile EmptyMap(int[] localVars) => new()
    {
        Header = new MapHeader(20, "stale.map", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        GlobalVariables = [],
        LocalVariables = localVars,
        Elevations = new MapElevation?[MapFile.ElevationCount],
    };

    [Fact]
    public void TwoSidsSharingAnOwnerKeepSeparateLocalVars()
    {
        ScriptHost host = MakeHost();
        const int OwnSid = 0x04000001;
        MapFile map = EmptyMap([10, 20]);
        map.ScriptsBySid[OwnSid] = new MapScriptRecord(0, 0, 1);  // LVAR seeded from slot 0 (10)
        map.ScriptsBySid[StaleSid] = new MapScriptRecord(0, 1, 1); // LVAR seeded from slot 1 (20)
        MapObject owner = Obj(18000, OwnSid, ObjectType.Critter);

        host.RunScriptProc(OwnSid, owner, map, owner, 0, -1, "critter_p_proc");
        host.RunScriptProc(StaleSid, owner, map, owner, 0, -1, "map_update_p_proc");

        Assert.Equal(20, host.GlobalVars[777]); // the stale sid read its own seeded LVAR, not 99
        Dictionary<int, int[]> lvars = host.ExportLocalVars("stale.map");
        Assert.Equal([99], lvars[OwnSid]);
        Assert.Equal([20], lvars[StaleSid]);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~StaleDudeScriptTests`
Expected: build error, `'ScriptHost' does not contain a definition for 'RunScriptProc'`.

- [ ] **Step 4: Implement `RunScriptProc`**

In `src/Hexwaste.Formats/Int/ScriptHost.cs`, replace the second `RunObjectProc` overload body:
```csharp
    public ScriptRunResult? RunObjectProc(MapObject obj, MapFile map, MapObject? dude,
        int fixedParam, int actionBeingUsed, params string[] procedureNames) =>
        RunScriptProc(obj.Sid, obj, map, dude, fixedParam, actionBeingUsed, procedureNames);

    /// <summary>Runs <paramref name="sid"/>'s script (its LVARs, its VM) with <paramref name="self"/> as
    /// self. Differs from <see cref="RunObjectProc(MapObject, MapFile, MapObject?, int, int, string[])"/>
    /// only when self does not hold the sid: the vanilla import's stale obj_dude slot, which fo2ce runs as
    /// its own Script with self = the dude (oracle F16).</summary>
    public ScriptRunResult? RunScriptProc(int sid, MapObject self, MapFile map, MapObject? dude,
        int fixedParam, int actionBeingUsed, params string[] procedureNames)
    {
        if (sid == -1 || !map.ScriptsBySid.TryGetValue(sid, out MapScriptRecord? record))
            return null;

        return RunProc(record.ScriptListIndex, map, sid, record, self, dude,
            fixedParam, actionBeingUsed, procedureNames);
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~StaleDudeScriptTests`
Expected: PASS (1 test).

- [ ] **Step 6: Heartbeat runs the slot's sid**

In `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs` `PumpCritterProcs`, replace
```csharp
        var result = _scriptHost.RunObjectProc(critter, _map, _dude?.Dude, "critter_p_proc");
```
with
```csharp
        // The slot's sid, not critter.Sid: the stale obj_dude slot runs its own state (oracle F16).
        var result = _scriptHost.RunScriptProc(sid, critter, _map, _dude?.Dude, 0, -1, "critter_p_proc");
```

In `src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs`, replace the comment's last line
`// fo2ce runs it as its own Script struct; Hexwaste runs the dude's obj_dude (owner.Sid = DudeSid).` with:
```csharp
        // fo2ce runs it as its own Script (own LVARs, self = the dude); so does Hexwaste, by sid (F16).
        // Deviation: fo2ce saves it back into the map's .SAV, so it returns on a revisit; Hexwaste drops it
        // on a map change and after a Hexwaste save/reload (no oracle fixture covers either).
```

- [ ] **Step 7: Build and run the full unit suite**

Run: `dotnet build src/Hexwaste.Viewer -v q -nologo && dotnet test tests/Hexwaste.Formats.Tests -v q --nologo`
Expected: build succeeds; all tests pass (1196 = 1195 + 1).

- [ ] **Step 8: Commit**

```bash
git add tests/Hexwaste.Formats.Tests/TestIntScript.cs tests/Hexwaste.Formats.Tests/StaleDudeScriptTests.cs \
  src/Hexwaste.Formats/Int/ScriptHost.cs src/Hexwaste.Viewer/ViewerGame.CombatHost.cs \
  src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs
git commit -m "feat(scripts): run critter slots by sid so the stale obj_dude slot keeps its own state (oracle F16)"
```
(plus the two trailer lines from Global Constraints)

---

### Task 2: map_enter / map_update run object scripts in fo2ce list order

**Files:**
- Modify: `src/Hexwaste.Formats/Int/ScriptHost.cs` (`RunMapEnter` object loop ~line 1159; `RunMapUpdate`
  object loop ~line 1202)
- Test: `tests/Hexwaste.Formats.Tests/StaleDudeScriptTests.cs` (add one test)

**Interfaces:**
- Consumes: `RunScriptProc` (Task 1), `CritterScripts`, `_scriptOwners`, `DefinesProc` (existing).
- Produces: private `RunObjectScriptsInListOrder(MapFile map, IEnumerable<MapObject> objects,
  MapObject? dude, int fixedParam, string procName)`.

- [ ] **Step 1: Write the failing test** (append to `StaleDudeScriptTests`)

```csharp
    [Fact]
    public void MapUpdateRunsCritterListLastInListOrder()
    {
        ScriptHost host = MakeHost();
        MapFile map = EmptyMap([5]);
        map.Elevations[0] = new MapElevation { Squares = new int[MapElevation.SquareGridSize] };
        MapObject scenery = Obj(14, 0x03000001, ObjectType.Scenery);
        MapObject critter = Obj(24, 0x04000001, ObjectType.Critter);
        MapObject dude = Obj(18000, -1, ObjectType.Critter);
        map.Elevations[0]!.Objects.AddRange([scenery, critter]);
        map.ScriptsBySid[scenery.Sid] = new MapScriptRecord(0, -1, 0);
        map.ScriptsBySid[critter.Sid] = new MapScriptRecord(0, -1, 0);
        map.ScriptsBySid[StaleSid] = new MapScriptRecord(0, 0, 1); // LVAR seeded 5
        map.CritterSids.AddRange([critter.Sid, StaleSid]);
        host.RebuildCritterScripts(map, StaleSid, dude);
        host.BindDudeScript(map, dude); // list: critter, stale, dude

        var sw = new StringWriter();
        try
        {
            OracleTrace.Open(sw, "t");
            OracleTrace.Clock = () => 0u;
            host.RunMapUpdate(map, [dude, scenery, critter], dude); // the viewer prepends the dude
        }
        finally
        {
            OracleTrace.Clock = null;
            OracleTrace.Close();
        }

        string[] selves = sw.ToString().Split('\n')
            .Where(l => l.Contains(" PROC ")).Select(l => l[(l.LastIndexOf('#') + 1)..].Trim()).ToArray();
        Assert.Equal(["14", "24", "18000", "18000"], selves);
        Assert.Equal(0, host.GlobalVars[777]); // gDude (LVAR 0) ran after the stale slot (LVAR 5)
    }
```
Add `using Hexwaste.Formats.Trace;` at the top of the file.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~MapUpdateRunsCritterListLastInListOrder`
Expected: FAIL: selves is `["18000", "14", "24"]` (dude first, stale never runs).

- [ ] **Step 3: Implement the ordered loop**

In `src/Hexwaste.Formats/Int/ScriptHost.cs`, add near `RunMapUpdate`:
```csharp
    /// <summary>ported from fallout2-ce src/scripts.cc scriptsExecMapUpdateScripts(): object scripts run
    /// by type list, the critter list last and in list order, by sid (so the stale obj_dude slot runs its
    /// own script, oracle F16); only scripts that define the proc (procs[proc] &gt; 0). Critter-list
    /// entries run only when the caller passed their owner, so probe callers keep their object sets.</summary>
    // shortcut: non-critter scripts run in the caller's object order, fo2ce uses the item list's file order;
    // upgrade if the oracle shows a divergence there.
    private void RunObjectScriptsInListOrder(MapFile map, IEnumerable<MapObject> objects, MapObject? dude,
        int fixedParam, string procName)
    {
        List<MapObject> snapshot = objects.ToList(); // procs create objects (container stocking)
        var passed = new HashSet<MapObject>(snapshot, ReferenceEqualityComparer.Instance);
        var listed = new HashSet<int>(CritterScripts.Sids);

        foreach (MapObject obj in snapshot)
            if (!listed.Contains(obj.Sid) && map.ScriptsBySid.TryGetValue(obj.Sid, out MapScriptRecord? record)
                && DefinesProc(record.ScriptListIndex, procName))
                RunObjectProc(obj, map, dude, fixedParam, -1, procName);

        foreach (int sid in CritterScripts.Sids.ToList())
            if (_scriptOwners.TryGetValue(sid, out MapObject? owner) && passed.Contains(owner)
                && map.ScriptsBySid.TryGetValue(sid, out MapScriptRecord? record)
                && DefinesProc(record.ScriptListIndex, procName))
                RunScriptProc(sid, owner, map, dude, fixedParam, -1, procName);
    }
```

In `RunMapUpdate`, replace
```csharp
        foreach (MapObject obj in objects.ToList())
            if (map.ScriptsBySid.TryGetValue(obj.Sid, out MapScriptRecord? record)
                && DefinesProc(record.ScriptListIndex, "map_update_p_proc"))
                RunObjectProc(obj, map, dude, 0, -1, "map_update_p_proc");
```
with
```csharp
        RunObjectScriptsInListOrder(map, objects, dude, 0, "map_update_p_proc");
```

In `RunMapEnter`, replace
```csharp
        foreach (MapObject obj in objects.ToList())
            RunObjectProc(obj, map, dude, firstRun, -1, "map_enter_p_proc");
```
with
```csharp
        RunObjectScriptsInListOrder(map, objects, dude, firstRun, "map_enter_p_proc");
```
Keep the snapshot comment above that loop (it now lives in the helper; delete the duplicate line only).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Hexwaste.Formats.Tests -v q --nologo`
Expected: all pass (1197).

- [ ] **Step 5: Commit**

```bash
git add src/Hexwaste.Formats/Int/ScriptHost.cs tests/Hexwaste.Formats.Tests/StaleDudeScriptTests.cs
git commit -m "feat(scripts): map_enter/map_update run the critter list last, by sid (oracle F16)"
```
(plus the two trailer lines)

---

### Task 3: Oracle, goldens, findings doc

**Files:**
- Modify: `docs/research-notes/oracle-m1-findings-2026-10.md` (F7 section ~line 197, F15 ~279, F16 ~289,
  "Recommended next steps" ~306)
- Modify (only if a fixture moves): files under `tests/golden*/`

- [ ] **Step 1: Run the oracle on all three fixtures**

```bash
for f in artemple-idle klamath-hour arcaves-ambush; do scripts/oracle-run.sh $f > scratch/oracle-runs/$f-run.out 2>&1; tail -5 scratch/oracle-runs/$f-run.out; done
pgrep -f build-oracle/fallout2-ce && pkill -9 -f build-oracle/fallout2-ce
```
Expected: artemple first divergence after line 3, klamath after line 66. Check the map_update round:
```bash
grep -n -m6 "map_update" scratch/oracle-runs/artemple-idle/hexwaste.trace
```
Expected: `artemple, animfrvr obj#14, animfrvr obj#18, obj_dude obj#18000, obj_dude obj#18000` at t=302720,
matching `scratch/oracle-runs/artemple-idle/fo2ce.trace` lines 3011-3015. If the first divergence did not
move, stop and report (do not change code beyond this plan without the controller).

- [ ] **Step 2: Run the golden suites sequentially**

```bash
for s in combat quest endgame opening encounter; do scripts/$s-golden.sh || echo "SUITE $s FAILED"; done
```
Expected: all pass unchanged (they never load a vanilla save; only the dude's map_enter/map_update order
can move a fixture). For any moved fixture: run it twice and require identical output, review the diff
(plausible: ordering of script side effects only), re-record with the suite's `record` mode, and list it
in the commit. A suite failure that is the pre-existing encounter flake (native SIGSEGV/SIGABRT, empty
output) is re-run once; report it if it repeats.

- [ ] **Step 3: Update the findings doc**

- F16 heading: append ` — FIXED`; add a `- **Fixed:**` bullet: the heartbeat and map_enter/map_update run
  critter slots by sid (`ScriptHost.RunScriptProc`), the critter list last in list order; the stale slot
  has its own LVARs and VM; deviations: dropped on map change / Hexwaste reload, start code (F8).
- F7: replace the open "obj_dude map_update twice" note with: explained by F16 (the stale slot), fixed.
- F15 first bullet: add "Fixed with F16."
- Recommended next steps item 3: F16 done; record the new first divergences (fixture, line, the two
  differing lines) from Step 1.

- [ ] **Step 4: Full checks and commit**

```bash
scripts/ascii-lint.sh && dotnet test tests/Hexwaste.Formats.Tests -v q --nologo
git add docs/research-notes/oracle-m1-findings-2026-10.md
git commit -m "docs(oracle): F16 fixed; new first divergences"
```
(plus the two trailer lines; add any re-recorded fixtures to the same commit and name them in the body)
