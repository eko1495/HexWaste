# Oracle F17 dude sid trace label Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The oracle trace labels the dude's LVAR stores with the sid fo2ce would print, so artemple-idle and klamath-hour move past their F17 lines (6 and 69).

**Architecture:** `ScriptHost.DudeSid` (0x04800000) stays the real sid. A trace-only `ScriptHost.DudeTraceSid` is set by `BindDudeScript` (0x04004650, the pid-derived sid after a map change) and overwritten by `ReplaceDudeScriptAfterVanillaLoad` (the first critter id >= 5 unused in the `.SAV` critter list). `ScriptContext.SetLocalVar` prints the label for `DudeSid`.

**Tech Stack:** C# / .NET 10, xUnit. Spec: `docs/superpowers/specs/2026-10-12-oracle-f17-dude-sid-label-design.md`.

## Global Constraints

- Keep `DudeSid` as the real sid. Add a trace-only label.
- `BindDudeScript` sets it to 0x04004650; `ReplaceDudeScriptAfterVanillaLoad` sets it to `FirstFreeCritterSid(map.CritterSids, 5)`, `| 0x04000000`.
- Known-limit comment, verbatim: `shortcut: assumes no critter script is created during the load-time passes (fo2ce's counter would move past 5); upgrade if an oracle fixture spawns one at load`.
- Port comments: `// ported from fallout2-ce src/x.cc f()`; alexbatalov e97087b is authoritative.
- No non-ASCII characters in C# string literals (CI runs `scripts/ascii-lint.sh`).
- No new dependencies. Never commit game assets.
- Commit trailers on every commit:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk
  ```

---

### Task 1: DudeTraceSid label + trace emit

**Files:**
- Modify: `src/Hexwaste.Formats/Int/ScriptHost.cs` (`BindDudeScript` ~line 398, `ReplaceDudeScriptAfterVanillaLoad` ~line 413, `ScriptContext.SetLocalVar` ~line 1763)
- Test: `tests/Hexwaste.Formats.Tests/StaleDudeScriptTests.cs`

**Interfaces:**
- Produces: `public int ScriptHost.DudeTraceSid { get; private set; }`; `internal static int ScriptHost.FirstFreeCritterSid(IEnumerable<int> critterSids, int start)`.
- Check `tests/Hexwaste.Formats.Tests/Hexwaste.Formats.Tests.csproj` / `src/Hexwaste.Formats` for `InternalsVisibleTo("Hexwaste.Formats.Tests")`; if absent, make the helper `public static` instead (do not add the attribute).

- [ ] **Step 1: Write the failing tests** (append to `StaleDudeScriptTests`)

```csharp
    [Theory]
    [InlineData(new[] { 0x04000001, 0x04004650 }, 0x04000005)]                    // artemple-idle
    [InlineData(new[] { 0x04000002, 0x04000003, 0x04000004, 0x04000005, 0x04000007, 0x04004650 }, 0x04000006)] // arcaves-ambush (subset)
    [InlineData(new[] { 0x04000001, 0x04000005, 0x04000006, 0x04000027, 0x04004650 }, 0x04000007)]
    public void FirstFreeCritterSidSkipsUsedIdsFromFive(int[] critterSids, int expected) =>
        Assert.Equal(expected, ScriptHost.FirstFreeCritterSid(critterSids, 5));

    [Fact]
    public void FirstFreeCritterSidKlamath() => // ids 1-39 used
        Assert.Equal(0x04000028, ScriptHost.FirstFreeCritterSid(
            Enumerable.Range(1, 39).Select(i => 0x04000000 | i).Append(0x04004650), 5));

    [Fact]
    public void DudeLvarTracesUnderDudeTraceSid()
    {
        ScriptHost host = MakeHost();
        MapFile map = EmptyMap([7]);
        map.Elevations[0] = new MapElevation { Squares = new int[MapElevation.SquareGridSize] };
        MapObject critter = Obj(24, 0x04000001, ObjectType.Critter);
        MapObject dude = Obj(18000, -1, ObjectType.Critter);
        map.Elevations[0]!.Objects.Add(critter);
        map.ScriptsBySid[critter.Sid] = new MapScriptRecord(0, -1, 0);
        map.ScriptsBySid[StaleSid] = new MapScriptRecord(0, 0, 1);
        map.CritterSids.AddRange([critter.Sid, StaleSid]);
        host.RebuildCritterScripts(map);
        host.BindDudeScript(map, dude);
        Assert.Equal(0x04004650, host.DudeTraceSid); // map change: pid-derived

        host.ReplaceDudeScriptAfterVanillaLoad(map, dude, StaleSid);
        Assert.Equal(0x04000005, host.DudeTraceSid); // save load: counter-allocated

        var sw = new StringWriter();
        try
        {
            OracleTrace.Open(sw, "hdr");
            host.RunScriptProc(ScriptHost.DudeSid, dude, map, dude, 0, -1, "critter_p_proc"); // LVAR0 := 99
        }
        finally
        {
            OracleTrace.Close();
        }
        Assert.Contains("LVAR sid=0x04000005 0 0->99", sw.ToString());
        Assert.DoesNotContain("sid=0x04800000", sw.ToString());
    }
```

Note: the existing `EmptyMap`/`Obj`/`MakeHost` helpers are in this file; if `EmptyMap` already sets `Elevations[0]`, drop that line (mirror the existing `ReplaceDudeScriptLeavesBothScriptsFresh` test). If running `critter_p_proc` on a fresh dude script first runs other code that also writes LVAR0, keep the `Contains` on the `->99` line only and adjust the old value to what the trace shows (the assertion that matters is the `sid=0x04000005` label and the absence of `0x04800000`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Hexwaste.Formats.Tests -v q --nologo --filter "FullyQualifiedName~StaleDudeScriptTests"`
Expected: build FAIL (`FirstFreeCritterSid` / `DudeTraceSid` not defined).

- [ ] **Step 3: Implement**

In `ScriptHost.cs`, after the `DudeSid` const:

```csharp
    /// <summary>Oracle trace label for the dude's script: the sid fo2ce would print (F17). Trace-only;
    /// the real sid stays <see cref="DudeSid"/>.</summary>
    public int DudeTraceSid { get; private set; } = 0x04004650;

    /// <summary>ported from fallout2-ce src/scripts.cc scriptGetNewId(): from the counter value, step up past
    /// every critter id already in use.</summary>
    internal static int FirstFreeCritterSid(IEnumerable<int> critterSids, int start)
    {
        var used = critterSids.Select(sid => sid & 0xFFFFFF).ToHashSet();
        int id = start;
        while (used.Contains(id))
            id++;
        return 0x04000000 | id;
    }
```

In `BindDudeScript`, add as the last statement:

```csharp
        // fo2ce party_member.cc:692 _partyMemberRecoverLoadInstance: after a map change the dude's sid is
        // pid-derived, (4 << 24) | (pid 0x01000000 & 0xFFFFFF) + 18000.
        DudeTraceSid = 0x04004650;
```

In `ReplaceDudeScriptAfterVanillaLoad`, after the final `BindDudeScript(map, dude);`:

```csharp
        // fo2ce _obj_load_dude -> scriptAdd -> scriptGetNewId: nextScriptId is never reset by a load; it is 5 on
        // the oracle's launch path (main menu, load a slot), the only value matching all three fixtures.
        // shortcut: assumes no critter script is created during the load-time passes (fo2ce's counter would move past 5); upgrade if an oracle fixture spawns one at load
        DudeTraceSid = FirstFreeCritterSid(map.CritterSids, 5);
```

In `ScriptContext.SetLocalVar`, change the trace call to:

```csharp
                    OracleTrace.Lvar(_sid == DudeSid ? _host.DudeTraceSid : _sid, index, slice[index], value);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `FALLOUT2_DIR=/home/eko/dev/FPOC/game-data dotnet test tests/Hexwaste.Formats.Tests -v q --nologo`
Expected: all pass (report pass/skip/fail counts). Then `scripts/ascii-lint.sh` (exit 0) and `dotnet build src/Hexwaste.Viewer -c Release -v q -nologo` (0 errors).

- [ ] **Step 5: Commit**

```bash
git add src/Hexwaste.Formats/Int/ScriptHost.cs tests/Hexwaste.Formats.Tests/StaleDudeScriptTests.cs
git commit -m "fix(trace): label the dude's LVAR stores with fo2ce's dude sid (oracle F17)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk"
```

---

### Task 2: Oracle rerun + findings doc

**Files:**
- Modify: `docs/research-notes/oracle-m1-findings-2026-10.md` (F17 section ~line 331, "Run results" ~line 9, "Recommended next steps" ~line 344)

**Interfaces:**
- Consumes: Task 1's label (no code changes in this task).

- [ ] **Step 1: Run the oracle, sequentially** (each a few minutes; timeout 600000 ms)

```bash
scripts/oracle-run.sh artemple-idle  > scratch/oracle-runs/artemple-idle-run.out 2>&1
scripts/oracle-run.sh klamath-hour   > scratch/oracle-runs/klamath-hour-run.out 2>&1
scripts/oracle-run.sh arcaves-ambush > scratch/oracle-runs/arcaves-ambush-run.out 2>&1
pgrep -f build-oracle/fallout2-ce && pkill -9 -f build-oracle/fallout2-ce
```

Expected: artemple first divergence after line 6, klamath after line 69, arcaves still at line 9 (`LVAR sid=0x04000008 0 0->-3` vs `PROC zclscorp.int combat_p_proc self=obj#102`). Record each new first-divergence line number and the two differing lines. If artemple or klamath still differ on an LVAR sid label, STOP and report (do not change code).

- [ ] **Step 2: Update the findings doc**

- F17 section: set `Status: FIXED`, add the counter rule (fo2ce `scriptGetNewId`, `scripts.cc:2138`; `nextScriptId` reset only in `_scr_header_load`, `scripts.cc:1797`; observed value 5) and this table:

  ```
  | fixture | critter ids used | fo2ce dude sid |
  |---|---|---|
  | artemple-idle | 1, 18000 | 0x04000005 |
  | arcaves-ambush | 2-5, 7-24, 18000 | 0x04000006 |
  | klamath-hour | 1-39, 18000 | 0x04000028 |
  ```
  and the known limit (a critter script created during the load-time passes would move fo2ce's counter).
- Run results / Recommended next steps: the new first divergences from Step 1; state that arcaves line 9
  (`sid=0x04000008`, a scorpion's combat LVAR; fo2ce starts obj#70's combat turn first) belongs to F4/F5, not F17.

- [ ] **Step 3: Commit**

```bash
git add docs/research-notes/oracle-m1-findings-2026-10.md
git commit -m "docs(oracle): F17 fixed; new first divergences

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk"
```
