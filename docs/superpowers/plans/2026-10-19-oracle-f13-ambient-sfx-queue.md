# Oracle F13 ambient sfx game-time event Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ambient sfx runs as fo2ce's game-time event (`ambientSoundEffectEventProcess`), drawing from the shared RNG even without audio, so the oracle's artemple-idle moves past trace line 1297.

**Architecture:** A pure `AmbientSfx.RunEvent` holds the event's draw logic (Formats, unit-tested). `ViewerGame` keeps `_ambientNextTick`/`_ambientPendingIdx`, runs the event from `UpdateClock` after `PumpTimers`, starts the chain on map load, and the vanilla import takes over the saved type-13 event. The wall-time `TickAmbientSfx` is removed.

**Tech Stack:** C# / .NET 10, xUnit, MonoGame viewer. Spec: `docs/superpowers/specs/2026-10-19-oracle-f13-ambient-sfx-queue-design.md`.

## Global Constraints

- fo2ce order per event: index (stored, or rolled when no data and the list is non-empty, else -1), then `delay = 10 * randomBetween(15, 20)` (always drawn), then the next index and reschedule only if the list is non-empty; `if (inCombat) idx = -1`.
- `between(min, max)` is fo2ce's inclusive `randomBetween`; from `Random` it is `rng.Next(min, max + 1)`.
- Shortcut comments, verbatim:
  - `shortcut: an ambient event and a script timer due on the same tick run timers first; fo2ce's single queue orders them by insertion. Upgrade if the oracle shows a same-tick difference.`
  - `shortcut: a Hexwaste save does not store the pending ambient event; the chain restarts at map load. Upgrade if save/load round trips need the exact ambient schedule.`
- Port comments: `// ported from fallout2-ce src/x.cc f()`; alexbatalov e97087b (`reference/fallout2-ce`) is authoritative.
- No non-ASCII characters in C# string literals (CI runs `scripts/ascii-lint.sh`). No new dependencies. Never commit game assets.
- Commit trailers on every commit:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk
  ```

---

### Task 1: `AmbientSfx.RunEvent`

**Files:**
- Modify: `src/Hexwaste.Formats/Map/AmbientSfx.cs` (add after `RollIndex`)
- Create: `tests/Hexwaste.Formats.Tests/AmbientSfxEventTests.cs`

**Interfaces:**
- Produces: `public static (int PlayIdx, int? NextIdx, int DelayTicks) AmbientSfx.RunEvent(IReadOnlyList<(string Name, int Chance)> entries, int? storedIdx, bool inCombat, Func<int, int, int> between)`; `DelayTicks == -1` means not rescheduled.

- [ ] **Step 1: Write the failing tests**

```csharp
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>Oracle F13: fo2ce game_sound.cc ambientSoundEffectEventProcess draw order.</summary>
public class AmbientSfxEventTests
{
    private static readonly (string Name, int Chance)[] Two = [("a", 30), ("b", 70)];

    // Scripted fo2ce randomBetween: returns the queued values in order and records each (min, max).
    private static Func<int, int, int> Scripted(List<(int Min, int Max)> calls, params int[] values)
    {
        var queue = new Queue<int>(values);
        return (min, max) => { calls.Add((min, max)); return queue.Dequeue(); };
    }

    [Fact]
    public void NoDataDrawsIndexDelayNextIndex()
    {
        var calls = new List<(int, int)>();
        var r = AmbientSfx.RunEvent(Two, null, false, Scripted(calls, 0, 15, 40));
        Assert.Equal([(0, 100), (15, 20), (0, 100)], calls);
        Assert.Equal(0, r.PlayIdx);       // 0 < 30 -> "a"
        Assert.Equal(1, r.NextIdx);       // 40 -> past "a" (30), into "b"
        Assert.Equal(150, r.DelayTicks);  // 10 * 15
    }

    [Fact]
    public void StoredIndexDrawsDelayAndNextIndexOnly()
    {
        var calls = new List<(int, int)>();
        var r = AmbientSfx.RunEvent(Two, 1, false, Scripted(calls, 18, 82));
        Assert.Equal([(15, 20), (0, 100)], calls);
        Assert.Equal(1, r.PlayIdx);
        Assert.Equal(1, r.NextIdx);
        Assert.Equal(180, r.DelayTicks);
    }

    [Fact]
    public void CombatDrawsTheSameButPlaysNothing()
    {
        var calls = new List<(int, int)>();
        var r = AmbientSfx.RunEvent(Two, null, true, Scripted(calls, 0, 15, 40));
        Assert.Equal(3, calls.Count);
        Assert.Equal(-1, r.PlayIdx);
        Assert.Equal(1, r.NextIdx);
        Assert.Equal(150, r.DelayTicks);
    }

    [Fact]
    public void EmptyListDrawsDelayOnlyAndStopsTheChain()
    {
        var calls = new List<(int, int)>();
        var r = AmbientSfx.RunEvent([], null, false, Scripted(calls, 17));
        Assert.Equal([(15, 20)], calls);
        Assert.Equal(-1, r.PlayIdx);
        Assert.Null(r.NextIdx);
        Assert.Equal(-1, r.DelayTicks);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Hexwaste.Formats.Tests -v q --nologo --filter "FullyQualifiedName~AmbientSfxEventTests"`
Expected: build FAIL (`RunEvent` not defined).

- [ ] **Step 3: Implement** (in `AmbientSfx`, after `RollIndex`)

```csharp
    /// <summary>
    /// ported from fallout2-ce src/game_sound.cc ambientSoundEffectEventProcess(): the draws of one ambient
    /// event, in fo2ce's order. <paramref name="storedIdx"/> is the event's data (null = no data, as for the
    /// map-load start and a loaded .SAV event: roll now). The delay is always drawn; the next index and the
    /// reschedule only for a non-empty list. In combat nothing plays (draws unchanged).
    /// <paramref name="between"/> is the engine's inclusive randomBetween(min, max).
    /// Returns DelayTicks -1 when the chain stops.
    /// </summary>
    public static (int PlayIdx, int? NextIdx, int DelayTicks) RunEvent(
        IReadOnlyList<(string Name, int Chance)> entries, int? storedIdx, bool inCombat, Func<int, int, int> between)
    {
        int idx = storedIdx ?? (entries.Count > 0 ? RollIndex(entries, total => between(0, total)) : -1);
        int delay = 10 * between(15, 20);
        int? next = null;
        int delayTicks = -1;
        if (entries.Count > 0)
        {
            next = RollIndex(entries, total => between(0, total));
            delayTicks = delay;
        }
        if (inCombat)
            idx = -1;
        return (idx, next, delayTicks);
    }
```

Note: `RollIndex` returns -1 without drawing when the summed chance is <= 0; fo2ce's `wmSfxRollNextIdx` would still draw `randomBetween(0, 0)`. maps.txt chances are positive, so leave it (no test needed).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Hexwaste.Formats.Tests -v q --nologo --filter "FullyQualifiedName~AmbientSfxEventTests"`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Hexwaste.Formats/Map/AmbientSfx.cs tests/Hexwaste.Formats.Tests/AmbientSfxEventTests.cs
git commit -m "feat(sfx): AmbientSfx.RunEvent ports the ambient event's draw order (oracle F13)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk"
```

---

### Task 2: Game-time ambient event in the viewer + vanilla import

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs` (fields ~395-400; map load ~1945-1946; `TickAmbientSfx` ~1976-1995; `Update` call ~2993; `UpdateClock` ~6880-6889)
- Modify: `src/Hexwaste.Viewer/ViewerGame.Oracle.cs` (`OracleFrame` call ~202; the `_ambientRng` audit comment ~25-30)
- Modify: `src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs` (`ImportVanillaQueue` ~203-247)

**Interfaces:**
- Consumes: `AmbientSfx.RunEvent` (Task 1).

- [ ] **Step 1: Fields.** Replace

```csharp
    // Per-map ambient sfx (P34-M5): the weighted maps.txt list, a wall-time countdown, and a DEDICATED
    // seeded RNG kept off the combat/worldmap/skill/sneak streams so a future wall-time golden can't shift.
    private IReadOnlyList<(string Name, int Chance)> _mapAmbient = [];
    private double _ambientTimerMs;
    private Random? _ambientRng;
    private const double AmbientIntervalMs = 17000; // ~the engine's 10*randomBetween(15,20) game-ticks
```

with

```csharp
    // Per-map ambient sfx (P34-M5, oracle F13): the weighted maps.txt list, fo2ce's game-time ambient event
    // (next due tick, -1 = none queued; the stored index, null = no data), and a DEDICATED seeded RNG kept
    // off the combat/worldmap/skill/sneak streams (the oracle points it at the shared stream).
    private IReadOnlyList<(string Name, int Chance)> _mapAmbient = [];
    private long _ambientNextTick = -1;
    private int? _ambientPendingIdx;
    private Random? _ambientRng;
```

- [ ] **Step 2: Replace `TickAmbientSfx`** (the whole method and its doc comment) with

```csharp
    /// <summary>
    /// ported from fallout2-ce src/game_sound.cc ambientSoundEffectEventProcess(): one ambient event on the
    /// game-time queue. Draws even without audio (the shared stream must advance as in fo2ce); plays the
    /// index's sound (birds -> crickets at night) only with audio and out of combat. fo2ce's 5000 ms
    /// bk-time gate between plays is not ported (audio only, no draws).
    /// </summary>
    private void RunAmbientSfxEvent(int? storedIdx)
    {
        _ambientRng ??= new Random(RngSeed ?? Environment.TickCount);
        (int playIdx, int? nextIdx, int delayTicks) = Formats.Map.AmbientSfx.RunEvent(_mapAmbient, storedIdx,
            _combat.Phase != Formats.Combat.CombatPhase.Idle, (min, max) => _ambientRng.Next(min, max + 1));
        _ambientNextTick = delayTicks >= 0 ? _clock.Ticks + delayTicks : -1;
        _ambientPendingIdx = nextIdx;
        if (playIdx != -1 && _audio is not null)
            _audio.PlaySfx(Formats.Map.AmbientSfx.RemapBirdForNight(_mapAmbient[playIdx].Name, _clock.Hour));
    }

    /// <summary>Runs the due ambient event (scripts.cc _doBkProcesses -> queueProcessEvents, right after the
    /// clock tick); loops to catch up after clock jumps like the other game-time events.
    /// shortcut: an ambient event and a script timer due on the same tick run timers first; fo2ce's single queue orders them by insertion. Upgrade if the oracle shows a same-tick difference.</summary>
    private void ProcessAmbientSfx()
    {
        while (_ambientNextTick >= 0 && _ambientNextTick <= _clock.Ticks)
            RunAmbientSfxEvent(_ambientPendingIdx);
    }
```

- [ ] **Step 3: Remove both old calls.** Delete in `Update`:

```csharp
        TickAmbientSfx(gameTime.ElapsedGameTime.TotalMilliseconds); // P34-M5 ambient sfx
```

and in `OracleFrame` (`ViewerGame.Oracle.cs`):

```csharp
        TickAmbientSfx(ms);
```

Also update the `OracleFrame` doc comment's list ("ambient life/sfx") to "ambient life" and the `_ambientRng` audit comment in `ViewerGame.Oracle.cs` (the lines starting `//   _ambientRng       ViewerGame.cs:1982`) to:

```
//   _ambientRng       ViewerGame.cs RunAmbientSfxEvent
//                                                  logic     ambient sfx event (game_sound.cc ambientSoundEffectEventProcess,
//                                                            wmSfxRollNextIdx) on the game-time clock, drawing without audio
//                                                            and in combat like fo2ce (oracle F13).
```

(keep the column alignment of the surrounding entries).

- [ ] **Step 4: Call from `UpdateClock`.** After `_scriptHost?.PumpTimers(_dude?.Dude); // P114: ...` add:

```csharp
        ProcessAmbientSfx(); // oracle F13: the ambient sfx event fires on game time
```

- [ ] **Step 5: Map load.** Replace

```csharp
        _mapAmbient = _mapList.GetAmbientSfx(mapName); // P34-M5: per-map ambient sfx list
        _ambientTimerMs = AmbientIntervalMs;
```

with

```csharp
        _mapAmbient = _mapList.GetAmbientSfx(mapName); // P34-M5: per-map ambient sfx list
        // ported from fallout2-ce src/map.cc mapLoad() -> _gsound_sfx_q_start(): start the chain (no data).
        // shortcut: a Hexwaste save does not store the pending ambient event; the chain restarts at map load. Upgrade if save/load round trips need the exact ambient schedule.
        RunAmbientSfxEvent(null);
```

Check with `grep -n "_ambientTimerMs\|AmbientIntervalMs\|TickAmbientSfx" src/Hexwaste.Viewer/*.cs` that nothing else references the removed names (expect no output).

- [ ] **Step 6: Vanilla import.** In `ImportVanillaQueue`, before `foreach (QueueEventSave e in queue)` add:

```csharp
        // queueLoad (queue.cc:84) replaces the whole queue: the ambient event map load queued is dropped.
        _ambientNextTick = -1;
        _ambientPendingIdx = null;
```

and add a case before `default:`:

```csharp
                case 13: // EVENT_TYPE_GSOUND_SFX_EVENT: no readProc (queue.cc:67), so it fires with no data
                    _ambientNextTick = e.Time;
                    _ambientPendingIdx = null;
                    break;
```

- [ ] **Step 7: Verify.**

```bash
FALLOUT2_DIR=/home/eko/dev/FPOC/game-data dotnet test tests/Hexwaste.Formats.Tests -v q --nologo
scripts/ascii-lint.sh
dotnet build src/Hexwaste.Viewer -c Release -v q -nologo
```

Expected: all tests pass (report counts), lint clean, 0 errors. Then run the golden suites SEQUENTIALLY (never two at once, never alongside an oracle run), each in check mode:

```bash
for s in combat quest endgame opening encounter; do scripts/$s-golden.sh > scratch/f13-golden-$s.out 2>&1; echo "$s rc=$?"; done
```

Expected: all rc=0 (no fixture moved). The encounter suite has a known PRE-EXISTING native crash flake (SIGSEGV/SIGABRT, empty fixture output); if only encounter fails, rerun it once and report both runs. If any fixture's output changed (not a crash), STOP and report the fixture names and the diff; do not re-record.

- [ ] **Step 8: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/ViewerGame.Oracle.cs src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs
git commit -m "fix(sfx): ambient sfx runs as fo2ce's game-time event, drawing without audio (oracle F13)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk"
```

---

### Task 3: Oracle rerun + findings doc

**Files:**
- Modify: `docs/research-notes/oracle-m1-findings-2026-10.md` (F13 section; "Run results"; "Recommended next steps")

- [ ] **Step 1: Run the oracle, sequentially** (each a few minutes; timeout 600000 ms)

```bash
scripts/oracle-run.sh artemple-idle  > scratch/oracle-runs/artemple-idle-run.out 2>&1
scripts/oracle-run.sh klamath-hour   > scratch/oracle-runs/klamath-hour-run.out 2>&1
scripts/oracle-run.sh arcaves-ambush > scratch/oracle-runs/arcaves-ambush-run.out 2>&1
pgrep -f build-oracle/fallout2-ce && pkill -9 -f build-oracle/fallout2-ce
```

Take each first divergence from the run output's TraceDiff section (not a plain `diff` of the trace files).
Expected: artemple past line 1297; klamath at 1664 or later; arcaves at 9 or later. Check that the
`vanilla-import: skipped queue event type 13` line no longer appears in `scratch/oracle-runs/<fixture>/hexwaste.log`.
If artemple still diverges on a `wmSfxRollNextIdx` / `ambientSoundEffectEventProcess` RNG line, STOP and
report the two differing lines and their neighbours (do not change code).

- [ ] **Step 2: Update the findings doc**

- F13: `Status: FIXED`, one paragraph: the event is ported (`AmbientSfx.RunEvent` + `ViewerGame.RunAmbientSfxEvent`), runs on game time after script timers, starts on map load, takes over the saved type-13 event; the two shortcut limits.
- Run results / Recommended next steps: the new first divergence per fixture (line number + the two differing lines + a one-line characterisation).

- [ ] **Step 3: Commit**

```bash
git add docs/research-notes/oracle-m1-findings-2026-10.md
git commit -m "docs(oracle): F13 fixed; new first divergences

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk"
```
