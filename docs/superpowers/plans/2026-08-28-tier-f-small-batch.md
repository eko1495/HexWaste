# Tier F Small-Batch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close six independent, one-site fidelity gaps (F4, F5, F6, F7, F9, F43) against `reference/fallout2-ce` at `alexbatalov e97087b`.

**Architecture:** Six unrelated fixes, no shared state. Each ports one cited reference expression into one Hexwaste site, with a test that fails before the change. Where the reference reads a framebuffer or accumulates render-loop state, the port uses a pure equivalent (a painted-tile map; a prefix sum over frame offsets) chosen because it is *exact* for the cases that occur, not because it is convenient.

**Tech Stack:** C# / .NET 10, MonoGame DesktopGL, xUnit. No new dependencies.

**Design spec:** `docs/superpowers/specs/2026-08-28-tier-f-small-batch-design.md`

## Global Constraints

- **Port, never guess.** Every behavioural change carries a comment naming its source, in the project's existing form: `// ported from fallout2-ce src/x.cc f()`. If a detail cannot be confirmed from `reference/fallout2-ce`, **stop and ask** rather than inventing it.
- **`alexbatalov e97087b` is authoritative** for vanilla behaviour. `community/main` is a bug-fix candidate source only, and its non-vanilla QoL (often marked `// CE:`) is out of scope.
- **Target framework is `net10.0`.** No new NuGet dependencies — the allowed set is MonoGame, xUnit, and SixLabors.ImageSharp (dump tools only). **Ask before adding anything else.**
- **Never copy, embed, or commit game assets.** `.gitignore` excludes `*.dat`, `*.map`, `*.frm`, `*.pal`, `game-data/` — keep it that way. Tests needing real game files use `[GameDataFact]`/`[GameDataTheory]`, which skip when `FALLOUT2_DIR` is unset.
- **Hermetic suite command:** `dotnet test` from the repo root. It must stay green (0 failed) at the end of every task. The current baseline is **980 passed / 0 failed / 91 skipped**.
- **Golden suites need a display and real game data** (`DISPLAY` set, `FALLOUT2_DIR` pointing at the install). They are run at the end of Task 6 and Task 7 only, not per task.
- **Conventional commits.** Commit at the end of every task; do not batch tasks into one commit.
- **`Frame`/`Rotation` on `MapObject` are the engine's own mutable object state.** When making a field settable, follow the existing precedent on `Pid` (`src/Hexwaste.Formats/Map/MapFile.cs:52-55`): a doc comment naming the engine call that mutates it.

---

## File Structure

| File | Role in this plan |
|---|---|
| `tests/Hexwaste.Formats.Tests/AmmoProtoCensusTests.cs` | **Create.** Task 1: the F43 gate — a `[GameDataFact]` census over every ammo proto's damage multiplier/divisor. |
| `src/Hexwaste.Formats/Int/ScriptHost.cs` | **Modify.** Task 2: add the public static `ApplyDirectAnim` helper and call it from `ScriptContext.Anim`. |
| `src/Hexwaste.Formats/Map/MapFile.cs` | **Modify.** Task 2: make `MapObject.Frame` settable. |
| `tests/Hexwaste.Formats.Tests/AnimExternalTests.cs` | **Create.** Task 2: hermetic tests for anim 1000/1010. |
| `src/Hexwaste.Formats/Map/AutomapPaint.cs` | **Create.** Task 3: the pure wall-priority paint rule. |
| `tests/Hexwaste.Formats.Tests/AutomapPaintTests.cs` | **Create.** Task 3: its truth table. |
| `src/Hexwaste.Viewer/ViewerGame.Panels.cs` | **Modify.** Task 3: route `DrawAutomap`'s plot loop through the rule. |
| `src/Hexwaste.Viewer/ViewerGame.cs` | **Modify.** Task 4: bottom-anchor the talking head and apply the accumulated X offset. |
| `src/Hexwaste.Formats/Text/MonitorLayout.cs` | **Create.** Task 5: the monitor's wrap budget + knob rule as a pure function. |
| `tests/Hexwaste.Formats.Tests/MonitorLayoutTests.cs` | **Create.** Task 5: its arithmetic. |
| `src/Hexwaste.Viewer/ViewerGame.Hud.cs` | **Modify.** Task 5: use the budget and prefix the knob. |
| `src/Hexwaste.Formats/Combat/CombatMath.cs` | **Modify.** Task 6: drop the ammo multiplier clamp. |
| `tests/Hexwaste.Formats.Tests/CombatMathTests.cs` | **Modify.** Task 6: lock the unclamped multiplier. |
| `docs/BACKLOG.md` | **Modify.** Task 7: reconcile all six entries. |

---

### Task 1: The F43 gate — ammo damage-multiplier census

The design spec makes F43 conditional on a fact nobody has established: whether any shipped ammo proto carries `DamageMultiplier == 0`. This task answers it and leaves the answer in the repo as a regression guard. **No production code changes here.**

**Files:**
- Create: `tests/Hexwaste.Formats.Tests/AmmoProtoCensusTests.cs`

**Interfaces:**
- Consumes: `ProtoDatabase.Get(int pid)` (`src/Hexwaste.Formats/Proto/ProtoDatabase.cs:161`) returning `ProtoInfo` with an `Ammo` property of type `AmmoProtoStats?` (`:98-105`, fields `Caliber, Quantity, AcModifier, DrModifier, DamageMultiplier, DamageDivisor`); `Fid.PidType(int)` / `Fid.PidIndex(int)` (`src/Hexwaste.Formats/Fid.cs:29,31`); `GameFileSystem.Open(string)`; `GameData.RequiredDir` (`tests/Hexwaste.Formats.Tests/GameDataFactAttribute.cs:21`).
- Produces: the census result, reported to the runner's stdout, and the assertion that gates Task 6.

**Background you need:** item PIDs are `(0 << 24) | index` where `index` is the 1-based line number in `proto\items\items.lst`. `ProtoDatabase.Load` throws `InvalidDataException` for an out-of-range index and can throw for a short/truncated `.pro`, so the loop must tolerate both per-PID rather than aborting the census.

- [ ] **Step 1: Write the census test**

Create `tests/Hexwaste.Formats.Tests/AmmoProtoCensusTests.cs`:

```csharp
using Hexwaste.Formats;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// F43's gate: the reference multiplies by ammo's damageMultiplier unconditionally
/// (combat.cc:4586-4587) and guards only the divisor (:4594-4598), while
/// RangedMath.RollDamage clamps the multiplier to a minimum of 1. The clamp only
/// changes an outcome for ammo whose multiplier is 0, so this census establishes
/// whether the divergence is live on shipped data or inert.
/// </summary>
public class AmmoProtoCensusTests
{
    [GameDataFact]
    public void NoShippedAmmoProtoHasADamageMultiplierOfZero()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);

        var ammo = new List<(int Pid, int Mult, int Div)>();
        // Item PIDs are (type 0 << 24) | 1-based items.lst index. Walk until the
        // database reports the index is past the end of the list.
        for (int index = 1; index <= 1000; index++)
        {
            ProtoInfo info;
            try
            {
                info = protos.Get(index);
            }
            catch (InvalidDataException)
            {
                continue; // past the end of items.lst, or a short .pro — neither is ammo evidence
            }
            catch (FileNotFoundException)
            {
                continue;
            }

            if (info.Ammo is { } a)
                ammo.Add((index, a.DamageMultiplier, a.DamageDivisor));
        }

        Assert.NotEmpty(ammo); // a census that found no ammo at all proves nothing

        var zeroMultiplier = ammo.Where(a => a.Mult == 0).ToList();
        var zeroDivisor = ammo.Where(a => a.Div == 0).ToList();

        // Reported unconditionally so the numbers land in the run log, not only on failure.
        Console.WriteLine($"AMMO CENSUS: {ammo.Count} ammo protos; "
            + $"multiplier==0: {zeroMultiplier.Count}; divisor==0: {zeroDivisor.Count}");
        foreach ((int pid, int mult, int div) in ammo.Where(a => a.Mult != 1 || a.Div != 1))
            Console.WriteLine($"  pid {pid}: mult={mult} div={div}");

        Assert.True(zeroMultiplier.Count == 0,
            "Ammo protos with damageMultiplier == 0 exist: "
            + string.Join(", ", zeroMultiplier.Select(a => a.Pid))
            + " — F43 is a LIVE damage change, not an inert one. Stop and escalate before Task 6.");
    }
}
```

- [ ] **Step 2: Run the census**

Run:
```bash
cd /home/eko/dev/FPOC
FALLOUT2_DIR=./game-data dotnet test --filter FullyQualifiedName~AmmoProtoCensus -v n 2>&1 | grep -E 'AMMO CENSUS|pid |Passed!|Failed!|Assert'
```

Expected: a line `AMMO CENSUS: N ammo protos; multiplier==0: 0; divisor==0: 0` (N is whatever the data holds — record the real number), then `Passed!`.

**If `multiplier==0` is nonzero the test FAILS.** That is the gate firing, not a broken test: **stop, report the listed PIDs to the project owner, and do not start Task 6.** Complete Tasks 2–5 and Task 7 regardless; F43 then needs its own re-record decision.

- [ ] **Step 3: Confirm the test skips without game data**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet test --filter FullyQualifiedName~AmmoProtoCensus 2>&1 | tail -3
```

Expected: 0 passed, 1 skipped — CI must stay green without assets.

- [ ] **Step 4: Commit**

```bash
cd /home/eko/dev/FPOC
git add tests/Hexwaste.Formats.Tests/AmmoProtoCensusTests.cs
git commit -m "test(combat): census the ammo damage multiplier, F43's gate

F43 records as unverified whether any shipped ammo proto carries a
damageMultiplier of 0 — the only value for which RangedMath.RollDamage's
Math.Max(_, 1) clamp changes an outcome. Answers it as a GameDataFact so
the answer is a standing regression guard rather than a one-off run."
```

---

### Task 2: F9 — the `anim` external's 1000 and 1010 values

**Files:**
- Modify: `src/Hexwaste.Formats/Map/MapFile.cs:47` (make `Frame` settable)
- Modify: `src/Hexwaste.Formats/Int/ScriptHost.cs:1610-1614` (`ScriptContext.Anim`)
- Create: `tests/Hexwaste.Formats.Tests/AnimExternalTests.cs`

**Interfaces:**
- Produces: `public static bool ScriptHost.ApplyDirectAnim(MapObject obj, int anim, int frame)` — returns `true` when `anim` was one of the direct-manipulation values (1000 / 1010) and was therefore fully handled, `false` when the caller should fall through to its animation request. No other task consumes it.

**The reference** (`src/interpreter_extra.cc:3420-3428`):

```c
} else if (anim == 1000) {
    if (frame < ROTATION_COUNT) {
        Rect rect;
        objectSetRotation(obj, frame, &rect);
        tileWindowRefreshRect(&rect, gElevation);
    }
} else if (anim == 1010) {
    Rect rect;
    objectSetFrame(obj, frame, &rect);
    tileWindowRefreshRect(&rect, gElevation);
}
```

`ROTATION_COUNT` is 6. Note there is **no** guard on 1010.

**Named divergence to write into the comment:** the reference's guard is `frame < ROTATION_COUNT` with no lower bound, and `objectSetRotation` (`object.cc`) likewise only rejects `direction >= ROTATION_COUNT`, so vanilla will store a negative rotation. Hexwaste feeds `Rotation` into `Fid.Build` and into array indexing, where a negative value throws rather than rendering garbage. The port therefore uses `frame is >= 0 and < 6` — a deliberate lower bound beyond the reference, stated in the comment so nobody later "fixes" it back.

**Why this cannot move a fixture:** the current `AnimRequested` handler gates on `anim is >= 0 and < 40` (`src/Hexwaste.Viewer/ViewerGame.cs:1233-1237`), so 1000/1010 already fall through as a silent no-op. The change is purely additive.

- [ ] **Step 1: Write the failing tests**

Create `tests/Hexwaste.Formats.Tests/AnimExternalTests.cs`:

```csharp
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// F9: opAnim's direct-manipulation values, ported from
/// fallout2-ce src/interpreter_extra.cc opAnim() (:3420-3428). 1000 sets rotation
/// (guarded by ROTATION_COUNT), 1010 sets the frame (unguarded). Everything else
/// falls through to the ordinary animation request.
/// </summary>
public class AnimExternalTests
{
    private static MapObject Critter() => new()
    {
        Id = 1, HexTile = 100, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = 0x01000000, Flags = 0, Pid = 0x01000005,
    };

    [Fact]
    public void Anim1000SetsRotation()
    {
        MapObject obj = Critter();
        Assert.True(ScriptHost.ApplyDirectAnim(obj, 1000, 3));
        Assert.Equal(3, obj.Rotation);
    }

    [Fact]
    public void Anim1000IgnoresARotationAtOrAboveRotationCount()
    {
        // objectSetRotation rejects direction >= ROTATION_COUNT (6); opAnim guards the
        // same bound, which is what makes the CE animate_rotation pointer bug harmless.
        MapObject obj = Critter();
        obj.Rotation = 2;
        Assert.True(ScriptHost.ApplyDirectAnim(obj, 1000, 6));
        Assert.Equal(2, obj.Rotation);
    }

    [Fact]
    public void Anim1000IgnoresANegativeRotation()
    {
        // Documented divergence: vanilla stores a negative rotation, which would throw
        // in our Fid.Build/array-indexing path rather than render garbage.
        MapObject obj = Critter();
        obj.Rotation = 2;
        Assert.True(ScriptHost.ApplyDirectAnim(obj, 1000, -1));
        Assert.Equal(2, obj.Rotation);
    }

    [Fact]
    public void Anim1010SetsFrame()
    {
        MapObject obj = Critter();
        Assert.True(ScriptHost.ApplyDirectAnim(obj, 1010, 4));
        Assert.Equal(4, obj.Frame);
    }

    [Fact]
    public void AnOrdinaryAnimIsNotHandledDirectly()
    {
        MapObject obj = Critter();
        Assert.False(ScriptHost.ApplyDirectAnim(obj, 5, 0));
        Assert.Equal(0, obj.Rotation);
        Assert.Equal(0, obj.Frame);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet test --filter FullyQualifiedName~AnimExternalTests 2>&1 | tail -20
```

Expected: a **build error**, `error CS0117: 'ScriptHost' does not contain a definition for 'ApplyDirectAnim'`. That is the correct failure for a method that does not exist yet.

- [ ] **Step 3: Make `MapObject.Frame` settable**

In `src/Hexwaste.Formats/Map/MapFile.cs`, replace line 47:

```csharp
    public required int Frame { get; init; }
```

with:

```csharp
    /// <summary>Settable (not init-only): the engine mutates an object's frame in place for
    /// the script-side `anim(obj, 1010, frame)` external (interpreter_extra.cc opAnim →
    /// objectSetFrame). The renderer reads it at ViewerGame.Rendering.cs:275.</summary>
    public required int Frame { get; set; }
```

- [ ] **Step 4: Add the helper and call it**

In `src/Hexwaste.Formats/Int/ScriptHost.cs`, add this as a public static member of `ScriptHost` (place it beside the existing public static `CritterStateOf`, which is the pattern this follows):

```csharp
    /// <summary>F9: opAnim's two direct-manipulation anim values, which bypass the animation
    /// system entirely. Returns true when <paramref name="anim"/> was handled here.
    /// ported from fallout2-ce src/interpreter_extra.cc opAnim() (:3420-3428)</summary>
    public static bool ApplyDirectAnim(MapObject obj, int anim, int frame)
    {
        if (anim == 1000)
        {
            // The reference guards only `frame < ROTATION_COUNT` (6), and objectSetRotation
            // likewise rejects only `direction >= ROTATION_COUNT` — so vanilla will store a
            // NEGATIVE rotation. DIVERGENCE, deliberate: Hexwaste feeds Rotation into
            // Fid.Build and into per-rotation array indexing, where a negative throws rather
            // than rendering garbage, so the lower bound is enforced here.
            if (frame is >= 0 and < 6)
                obj.Rotation = frame;
            return true;
        }

        if (anim == 1010)
        {
            // Unguarded in the reference; the renderer clamps to the FRM's frame count
            // (ViewerGame.Rendering.cs:275), which is where objectSetFrame's own bound lives.
            obj.Frame = frame;
            return true;
        }

        return false;
    }
```

Then in `ScriptContext.Anim` (`ScriptHost.cs:1610`), replace:

```csharp
        public void Anim(int objectHandle, int anim, int frame)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.AnimRequested?.Invoke(obj, anim);
        }
```

with:

```csharp
        public void Anim(int objectHandle, int anim, int frame)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj)
                return;
            // F9: 1000/1010 manipulate the object directly and never reach the animation
            // system (interpreter_extra.cc opAnim :3420-3428).
            if (ApplyDirectAnim(obj, anim, frame))
                return;
            _host.AnimRequested?.Invoke(obj, anim);
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet test --filter FullyQualifiedName~AnimExternalTests 2>&1 | tail -5
```

Expected: `Passed!  - Failed: 0, Passed: 5`.

- [ ] **Step 6: Run the whole hermetic suite**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet test 2>&1 | tail -5
```

Expected: `Failed: 0`, passed count is the baseline **980 + 5 = 985** (plus 1 skipped from Task 1 → 92 skipped). Making `Frame` settable can break `required`-init call sites at compile time; if the build fails, fix the call sites — `required` still forces initialization, only `init`-only is being relaxed.

- [ ] **Step 7: Commit**

```bash
cd /home/eko/dev/FPOC
git add src/Hexwaste.Formats/Int/ScriptHost.cs src/Hexwaste.Formats/Map/MapFile.cs tests/Hexwaste.Formats.Tests/AnimExternalTests.cs
git commit -m "fix(script): honour anim values 1000 (rotation) and 1010 (frame)

opAnim handles both explicitly (interpreter_extra.cc:3420-3428); ScriptHost
forwarded every anim to AnimRequested and discarded the frame argument, so
a script calling anim(obj, 1000, rot) to face a critter did nothing.

Purely additive: AnimRequested already gates anim < 40, so these values were
a silent no-op rather than a bogus animation request. MapObject.Frame becomes
settable to back 1010, following the precedent already set on Pid.

Divergence, deliberate and commented: the reference guards only the upper
bound, so vanilla stores a negative rotation; ours would throw on array
indexing, so the lower bound is enforced."
```

---

### Task 3: F7 — automap wall-colour priority

**Files:**
- Create: `src/Hexwaste.Formats/Map/AutomapPaint.cs`
- Create: `tests/Hexwaste.Formats.Tests/AutomapPaintTests.cs`
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs:1066-1073` (`DrawAutomap`'s object loop)

**Interfaces:**
- Produces: `public enum AutomapMark { Other, Wall, Scenery }` and `public static bool AutomapPaint.Overpaints(AutomapMark existing, AutomapMark incoming)` — `false` exactly when scenery would overpaint a wall. No other task consumes it.

**The reference** (`src/automap.cc:573`):

```c
if (*v12 != _colorTable[992] || objectColor != _colorTable[480]) {
    v12[0] = objectColor;
    v12[1] = objectColor;
}
```

`_colorTable[992]` is the **wall** colour (`automap.cc:534`) and `_colorTable[480]` the high-detail **scenery** colour (`:537`). So the rule is narrow: *scenery may not overpaint wall.* The dude/scanner colour (`_colorTable[31744]`) still overpaints walls.

**Why `DrawAutomap` and not the mini-map.** The guard lives in the `AUTOMAP_IN_GAME` branch, whose semantics — the `OBJECT_SEEN` gate (`:530`), the `AUTOMAP_WTH_HIGH_DETAILS` scenery gate (`:537`), the scanner critter colour (`:526`) — are the ones `DrawAutomap` implements (`ViewerGame.Panels.cs:1066-1082`). `DrawPipboyMiniMap` has its own scaled projection and is *not* the counterpart, despite the backlog citing `Panels.cs:1015`.

**Why a dictionary is exact, not an approximation.** `DrawAutomap`'s projection is `ax = 449 - 2 * (tile % 200)`, `ay = 2 * (tile / 200) + 8` (`:1059`) — a bijection from tile to pixel with a 2 px step, so "the pixel already painted at this tile" and "the mark already recorded for this tile" are the same statement. Keep the existing `_flatObjects`-then-`_solidObjects` order: the reference's guard is order-dependent (a wall painted *after* scenery still wins), so a global priority table would be a different rule.

- [ ] **Step 1: Write the failing test**

Create `tests/Hexwaste.Formats.Tests/AutomapPaintTests.cs`:

```csharp
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// F7: the in-game automap's one colour-priority rule, ported from
/// fallout2-ce src/automap.cc:573 — scenery may not overpaint a wall pixel.
/// Everything else overpaints, including a wall arriving after scenery.
/// </summary>
public class AutomapPaintTests
{
    [Fact]
    public void SceneryDoesNotOverpaintAWall() =>
        Assert.False(AutomapPaint.Overpaints(AutomapMark.Wall, AutomapMark.Scenery));

    [Fact]
    public void AWallOverpaintsScenery() =>
        Assert.True(AutomapPaint.Overpaints(AutomapMark.Scenery, AutomapMark.Wall));

    [Fact]
    public void TheDudeMarkOverpaintsAWall() =>
        // _colorTable[31744] is not the scenery colour, so the guard's second term is false.
        Assert.True(AutomapPaint.Overpaints(AutomapMark.Wall, AutomapMark.Other));

    [Fact]
    public void SceneryOverpaintsAnythingThatIsNotAWall() =>
        Assert.True(AutomapPaint.Overpaints(AutomapMark.Other, AutomapMark.Scenery));

    [Fact]
    public void AWallOverpaintsAWall() =>
        Assert.True(AutomapPaint.Overpaints(AutomapMark.Wall, AutomapMark.Wall));
}
```

- [ ] **Step 2: Run it to verify it fails**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet test --filter FullyQualifiedName~AutomapPaintTests 2>&1 | tail -10
```

Expected: build error `CS0246: The type or namespace name 'AutomapPaint' could not be found`.

- [ ] **Step 3: Write the rule**

Create `src/Hexwaste.Formats/Map/AutomapPaint.cs`:

```csharp
namespace Hexwaste.Formats.Map;

/// <summary>Which of the in-game automap's colours a mark carries. Only the wall and
/// scenery cases participate in the priority rule; every other colour is Other.</summary>
public enum AutomapMark
{
    Other,
    Wall,    // _colorTable[992],  automap.cc:534
    Scenery, // _colorTable[480],  automap.cc:537
}

/// <summary>The in-game automap's single colour-priority rule.</summary>
public static class AutomapPaint
{
    /// <summary>Whether <paramref name="incoming"/> may overpaint <paramref name="existing"/>.
    /// ported from fallout2-ce src/automap.cc:573:
    /// <c>if (*v12 != _colorTable[992] || objectColor != _colorTable[480])</c> — i.e. refuse
    /// ONLY scenery-over-wall. The dude and scanner colours still overpaint a wall.</summary>
    public static bool Overpaints(AutomapMark existing, AutomapMark incoming) =>
        existing != AutomapMark.Wall || incoming != AutomapMark.Scenery;
}
```

- [ ] **Step 4: Run it to verify it passes**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet test --filter FullyQualifiedName~AutomapPaintTests 2>&1 | tail -5
```

Expected: `Passed!  - Failed: 0, Passed: 5`.

- [ ] **Step 5: Wire it into `DrawAutomap`**

In `src/Hexwaste.Viewer/ViewerGame.Panels.cs`, the object loop currently reads:

```csharp
        foreach (MapObject obj in _flatObjects[_elevation].Concat(_solidObjects[_elevation]))
        {
            if (!_seenTiles.Contains(obj.HexTile) || AutomapColor(obj) is not { } col) // OBJECT_SEEN fog (P71)
                continue;
            // P82: LOW detail shows only walls (the engine's AUTOMAP_WITH_HIGH_DETAILS gate); HIGH = all.
            if (!_automapHighDetail && Fid.Type(obj.Fid) is not ObjectType.Wall)
                continue;
            Plot(obj.HexTile, col, 2);
        }
```

Replace it with:

```csharp
        // F7: the engine refuses to repaint a wall pixel with the scenery colour
        // (automap.cc:573). Our projection is a bijection from tile to pixel
        // (ax = 449 − 2·col, step 2), so tracking the mark per tile IS reading the pixel.
        var painted = new Dictionary<int, AutomapMark>();
        foreach (MapObject obj in _flatObjects[_elevation].Concat(_solidObjects[_elevation]))
        {
            if (!_seenTiles.Contains(obj.HexTile) || AutomapColor(obj) is not { } col) // OBJECT_SEEN fog (P71)
                continue;
            // P82: LOW detail shows only walls (the engine's AUTOMAP_WITH_HIGH_DETAILS gate); HIGH = all.
            if (!_automapHighDetail && Fid.Type(obj.Fid) is not ObjectType.Wall)
                continue;
            AutomapMark mark = Fid.Type(obj.Fid) switch
            {
                ObjectType.Wall => AutomapMark.Wall,
                ObjectType.Scenery => AutomapMark.Scenery,
                _ => AutomapMark.Other,
            };
            if (painted.TryGetValue(obj.HexTile, out AutomapMark existing)
                && !AutomapPaint.Overpaints(existing, mark))
                continue;
            painted[obj.HexTile] = mark;
            Plot(obj.HexTile, col, 2);
        }
```

Add `using Hexwaste.Formats.Map;` to the file's usings if it is not already present (check the top of the file first — `MapObject` is already referenced in this loop, so it almost certainly is).

- [ ] **Step 6: Build and run the whole hermetic suite**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet test 2>&1 | tail -5
```

Expected: `Failed: 0`, 990 passed (985 + 5).

- [ ] **Step 7: Commit**

```bash
cd /home/eko/dev/FPOC
git add src/Hexwaste.Formats/Map/AutomapPaint.cs tests/Hexwaste.Formats.Tests/AutomapPaintTests.cs src/Hexwaste.Viewer/ViewerGame.Panels.cs
git commit -m "fix(automap): refuse to overpaint a wall mark with scenery

automap.cc:573 guards exactly one pair — scenery may not repaint a wall
pixel; the dude and scanner colours still may. DrawAutomap overpainted
unconditionally, so the priority model was absent rather than incomplete.

Two corrections to the backlog entry, recorded in the spec: the rule is
narrower than 'any later mark can hide a wall', and it belongs to
DrawAutomap (which implements the AUTOMAP_IN_GAME semantics) rather than
the mini-map the entry cited."
```

---

### Task 4: F4 + F5 — talking-head anchoring and horizontal sway

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:6178-6195` (the tail of `DrawTalkingHead`)

**Interfaces:**
- Consumes: `FrmCache.GetFrm(int fid)` (`src/Hexwaste.Viewer/FrmCache.cs:36`) → `FrmFile`; `FrmFile.GetFrame(int frame, int rotation)` → `FrmFrame` with `OffsetX`/`OffsetY` (`src/Hexwaste.Formats/Frm/FrmFile.cs:12-13`); `FrmCache.FrameCount(int fid, int rotation = 0)` (`FrmCache.cs:119`).
- Produces: nothing consumed by later tasks.

**The reference** (`src/game_dialog.cc:4586-4590`):

```c
_totalHotx += a4;              // a4 = artGetFrameOffsets(...).x for THIS frame
a3 += _totalHotx;              // a3 = artGetRotationOffsets(...).x
int destOffset = destWidth * (200 - height) + a3 + (388 - width) / 2;
```

with `_totalHotx = 0` when `frame == 0` (`:4557`).

**Two design decisions, both deliberate:**

1. **Height comes from the texture, not a new accessor.** `FrmCache.GetTexture` builds the texture straight from the `FrmFrame` (`FrmCache.cs:111-115`), so `head.Height` *is* the frame height. F4 needs no new API.
2. **`_totalHotx` is computed as a prefix sum, not accumulated in a field.** The reference accumulates once per *animation* frame; `DrawTalkingHead` runs once per *render* frame, so a field would over-accumulate at high frame rates. Because the reference resets at frame 0 and adds one frame's offset per step, its value at frame N is exactly the sum of offsets 0..N. The prefix sum is therefore exact for sequential playback and immune to render rate. **Divergence to comment:** lip-sync playback jumps between frames non-sequentially (`LipData.FrameForPhoneme`), where the reference would accumulate the offsets of the frames it actually displayed. None of the 5 heads that use a nonzero X offset (`HRLD2BF3`, `HRLD2GF2`, `HRLD2NF3`, `TNDI2GF2`, `TNDI2NF3`) is a lip anim, so this is inert on shipped data — say so rather than leaving it unstated.

**The rotation-offset term.** The reference's `a3` starts as `artGetRotationOffsets(headFrm, 0).x`, and the `if (destOffset + width * v8 > 0)` guard uses its Y. Both are provably 0 on all 186 shipped heads — established when PR #675 hunk 20 was rejected. Do **not** add code for them; add a comment recording that they are inert and why.

- [ ] **Step 1: Apply the anchoring and the offset**

In `src/Hexwaste.Viewer/ViewerGame.cs`, replace the tail of `DrawTalkingHead`:

```csharp
        // The engine's head display area is window-local (126,14), ~388px wide; the heads sit centred in
        // the 640 frame. Centre this head's own width within that area and draw it at natural size.
        int x = frameX + 126 + (388 - head.Width) / 2;
        int y = frameY + 14;
        _spriteBatch.Draw(head, new Vector2(x, y), Color.White);
```

with:

```csharp
        // The engine's head display area is window-local (126,14), 388x200.
        // ported from fallout2-ce src/game_dialog.cc gameDialogRenderTalkingHead() (:4590):
        //   destOffset = destWidth * (200 - height) + a3 + (388 - width) / 2
        // so the head is BOTTOM-anchored in the 200px area (F4) and shifted by the
        // accumulated per-frame X offset (F5, `_totalHotx`, :4585).
        //
        // `a3` also carries artGetRotationOffsets(...).x, and the reference's
        // `if (destOffset + width * v8 > 0)` guard carries its Y. Both are 0 on all 186
        // shipped art\heads\*.FRM (established when PR #675 hunk 20 was rejected), so
        // neither term is ported — they would be identity.
        int drawnFrame = HeadFrameIndex(headId, animType, requestedFrame);
        int hotX = HeadAccumulatedHotX(headId, animType, drawnFrame);
        int x = frameX + 126 + (388 - head.Width) / 2 + hotX;
        int y = frameY + 14 + (200 - head.Height);
        _spriteBatch.Draw(head, new Vector2(x, y), Color.White);
```

- [ ] **Step 2: Add the two helpers**

Add these immediately after `HeadTexture` in `src/Hexwaste.Viewer/ViewerGame.cs`:

```csharp
    /// <summary>The frame index <see cref="HeadTexture"/> actually resolved for this request —
    /// the same clamp/modulo, so the offset sum below is taken over the frame really drawn.</summary>
    private int HeadFrameIndex(int headId, int animType, int frame)
    {
        int fid = Formats.Fid.Build(Formats.ObjectType.Head, headId, animType, weaponCode: 1);
        try
        {
            int frames = _frmCache.FrameCount(fid);
            return frames > 0 ? Math.Min(frame, frames - 1) % frames : 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            return 0;
        }
    }

    /// <summary>`_totalHotx` (game_dialog.cc:4557,4585): reset at frame 0, then one frame's
    /// artGetFrameOffsets X added per step — so at frame N it equals the sum over frames 0..N.
    /// Computed as that prefix sum rather than accumulated in a field, because DrawTalkingHead
    /// runs once per RENDER frame while the reference runs once per ANIMATION frame, and a field
    /// would over-accumulate at high frame rates.
    /// DIVERGENCE: lip-sync playback visits frames non-sequentially (LipData.FrameForPhoneme),
    /// where the reference would sum the frames it actually showed. Inert on shipped data — all
    /// 5 heads with a nonzero X offset (HRLD2BF3, HRLD2GF2, HRLD2NF3, TNDI2GF2, TNDI2NF3) are
    /// fidget anims, which do play sequentially.</summary>
    private int HeadAccumulatedHotX(int headId, int animType, int drawnFrame)
    {
        int fid = Formats.Fid.Build(Formats.ObjectType.Head, headId, animType, weaponCode: 1);
        try
        {
            Formats.Frm.FrmFile frm = _frmCache.GetFrm(fid);
            int total = 0;
            for (int i = 0; i <= drawnFrame; i++)
                total += frm.GetFrame(i, 0).OffsetX;
            return total;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException
            or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return 0; // a head whose offsets can't be read simply doesn't sway
        }
    }
```

If `Formats.Frm.FrmFile` or `Formats.Frm` is not the correct namespace path from this file, use whatever `FrmCache.GetFrm`'s return type resolves to — check `src/Hexwaste.Viewer/FrmCache.cs:36` and its usings rather than guessing.

- [ ] **Step 3: Build**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet build 2>&1 | tail -5
```

Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Verify against the real head art**

There is no golden covering dialog pixels, so the proof is a probe over the shipped art. Run:

```bash
cd /home/eko/dev/FPOC
FALLOUT2_DIR=./game-data dotnet test 2>&1 | tail -5
```

Expected: `Failed: 0`. Then confirm the two claims this task rests on, using the existing `FrmDump` tool against `art\heads\`:

```bash
cd /home/eko/dev/FPOC
timeout 300 dotnet run -c Release --project tools/DatDump -- --game-dir ./game-data list 'art\heads' | wc -l
```

Expected: 186 head FRMs — the population both claims are quantified over. Record the number you actually get in the commit message; **if it is not 186, say so** rather than repeating the figure from the backlog.

- [ ] **Step 5: Commit**

```bash
cd /home/eko/dev/FPOC
git add src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "fix(dialog): bottom-anchor talking heads and apply their X sway

game_dialog.cc:4590 computes destOffset = destWidth * (200 - height) + a3 +
(388 - width) / 2. Hexwaste pinned y = frameY + 14, so the 14 heads with
frames shorter than 200px sat high and shifted between frames (F4); and it
applied none of _totalHotx, the accumulated per-frame X offset 5 heads use
(F5). One expression, both entries.

_totalHotx is a prefix sum over frames 0..N rather than a field: the
reference accumulates per animation frame, we draw per render frame, and a
field would over-accumulate. Exact for sequential playback, which is all
5 affected heads.

The rotation-offset X and Y terms are not ported — both are 0 on every
shipped head, established when PR #675 hunk 20 was rejected."
```

---

### Task 5: F6 — monitor bullet knob and wrap budget

**Files:**
- Create: `src/Hexwaste.Formats/Text/MonitorLayout.cs`
- Create: `tests/Hexwaste.Formats.Tests/MonitorLayoutTests.cs`
- Modify: `src/Hexwaste.Viewer/ViewerGame.Hud.cs:193-212`

**Interfaces:**
- Produces: `public static class MonitorLayout` with
  - `public const char Knob = '\x95';`
  - `public const int Width = 167;`
  - `public const int Height = 60;`
  - `public const int X = 23;`
  - `public const int Y = 24;`
  - `public static int MaxDisplayLines(int lineHeight)` → `Height / lineHeight`
  - `public static int WrapBudget(int lineHeight, int knobWidth)` → `Width - MaxDisplayLines(lineHeight) - knobWidth`

  No other task consumes these.

**The reference** (`src/display_monitor.cc:262`):

```c
while (fontGetStringWidth(str) < DISPLAY_MONITOR_WIDTH - _max_disp - knobWidth) {
```

with `DISPLAY_MONITOR_WIDTH` = `167 + gInterfaceBarContentOffset` (`:33`; the offset is 0 for the vanilla 640-wide bar) and `_max_disp` = `DISPLAY_MONITOR_HEIGHT / fontGetLineHeight()` (`:115`), `DISPLAY_MONITOR_HEIGHT` = 60 (`:34`).

**Port `_max_disp` as written.** It is a *line count* subtracted from a *pixel width*. That is an oddity of the original, not a typo to correct. A comment must say so, or a future reader will "fix" it.

**The knob** is `'\x95'`, prefixed to the **first** line of each message only; the continuation arm sets `knob = '\0'; knobWidth = 0;` (`:266-272`), so continuation lines get the full budget.

**The 80-character line cap, deliberately omitted.** The same loop copies at most
`DISPLAY_MONITOR_LINE_LENGTH - 1` (79) characters per line, or `- 2` (78) on the knob line
(`display_monitor.cc:267-274`) — a fixed-size `char[80]` buffer bound, not a display rule. The
pixel budget is always the binding constraint at this font size, so the cap can never fire for
text the width test already accepted. It is **not ported**; record that in the commit message so a
future reader knows it was considered rather than missed.

**The font question, already resolved — do not re-investigate.** `DISPLAY_MONITOR_FONT` is 101 (`:38`), and `interfaceFontLoad` builds its path as `snprintf(path, sizeof(path), "font%d.aaf", font_index)` over an index of `id - 100` (`font_manager.cc:117-122`). Font 101 is therefore `font1.aaf`, which `ViewerGame` already loads (`ViewerGame.cs:1487`). No new asset, no new loader.

**The geometry decision, made here so the implementer does not have to.** The reference rect is `X=23, Y=24, W=167, H=60` (`display_monitor.cc:31-34`); Hexwaste uses `24, 26, 162x56` (`ViewerGame.Hud.cs:198`). **Adopt all four.** The width and height are load-bearing — they *are* the budget and `_max_disp` — and leaving X and Y at hand-tuned values while adopting the reference's width would produce a rect that matches neither. This is a visible 1–2 px shift of the monitor text and must be called out in the commit message as a deliberate, unverified-by-screenshot change if no screenshot is taken.

- [ ] **Step 1: Write the failing test**

Create `tests/Hexwaste.Formats.Tests/MonitorLayoutTests.cs`:

```csharp
using Hexwaste.Formats.Text;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// F6: the message monitor's wrap budget, ported from
/// fallout2-ce src/display_monitor.cc:262 —
///   DISPLAY_MONITOR_WIDTH - _max_disp - knobWidth
/// where _max_disp is a LINE COUNT (height / lineHeight) subtracted from a PIXEL
/// width. That is the original's own arithmetic and is reproduced, not corrected.
/// </summary>
public class MonitorLayoutTests
{
    [Fact]
    public void MaxDisplayLinesIsTheHeightOverTheLineHeight() =>
        // DISPLAY_MONITOR_HEIGHT (60) / fontGetLineHeight(), display_monitor.cc:115.
        Assert.Equal(6, MonitorLayout.MaxDisplayLines(10));

    [Fact]
    public void TheFirstLineBudgetSubtractsBothTheLineCountAndTheKnob() =>
        // 167 - 6 - 5
        Assert.Equal(156, MonitorLayout.WrapBudget(lineHeight: 10, knobWidth: 5));

    [Fact]
    public void ContinuationLinesGetTheFullBudgetBecauseKnobWidthIsZeroed() =>
        // display_monitor.cc:270 sets knob = '\0' and knobWidth = 0 after the first line.
        Assert.Equal(161, MonitorLayout.WrapBudget(lineHeight: 10, knobWidth: 0));

    [Fact]
    public void TheKnobIsTheBulletCharacter() =>
        Assert.Equal('\x95', MonitorLayout.Knob);

    [Fact]
    public void TheRectMatchesTheReference()
    {
        // display_monitor.cc:31-34
        Assert.Equal(23, MonitorLayout.X);
        Assert.Equal(24, MonitorLayout.Y);
        Assert.Equal(167, MonitorLayout.Width);
        Assert.Equal(60, MonitorLayout.Height);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet test --filter FullyQualifiedName~MonitorLayoutTests 2>&1 | tail -10
```

Expected: build error `CS0246: The type or namespace name 'MonitorLayout' could not be found`.

- [ ] **Step 3: Write the layout rules**

Create `src/Hexwaste.Formats/Text/MonitorLayout.cs`:

```csharp
namespace Hexwaste.Formats.Text;

/// <summary>The green message monitor's geometry and wrap budget.
/// ported from fallout2-ce src/display_monitor.cc (:31-34 geometry, :115 _max_disp,
/// :262 the wrap condition, :266-272 the knob).</summary>
public static class MonitorLayout
{
    /// <summary>The bullet knob prefixed to the FIRST line of every message
    /// (display_monitor.cc:244).</summary>
    public const char Knob = '\x95';

    // DISPLAY_MONITOR_X/Y/WIDTH/HEIGHT, display_monitor.cc:31-34. WIDTH is
    // `167 + gInterfaceBarContentOffset`; the offset is 0 for the vanilla 640-wide bar.
    public const int X = 23;
    public const int Y = 24;
    public const int Width = 167;
    public const int Height = 60;

    /// <summary>`_max_disp` — how many lines fit in the monitor (display_monitor.cc:115).</summary>
    public static int MaxDisplayLines(int lineHeight) =>
        lineHeight > 0 ? Height / lineHeight : 0;

    /// <summary>The wrap budget: `DISPLAY_MONITOR_WIDTH - _max_disp - knobWidth`
    /// (display_monitor.cc:262). NOTE the original subtracts `_max_disp`, a LINE COUNT,
    /// from a PIXEL width. That is what the shipped engine does; it is reproduced here
    /// verbatim and is NOT a unit error to be corrected.
    /// Pass the knob's pixel width for the first line of a message and 0 for every
    /// continuation line, matching the `knobWidth = 0` arm at :270.</summary>
    public static int WrapBudget(int lineHeight, int knobWidth) =>
        Width - MaxDisplayLines(lineHeight) - knobWidth;
}
```

- [ ] **Step 4: Run it to verify it passes**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet test --filter FullyQualifiedName~MonitorLayoutTests 2>&1 | tail -5
```

Expected: `Passed!  - Failed: 0, Passed: 5`.

- [ ] **Step 5: Wire it into the HUD**

In `src/Hexwaste.Viewer/ViewerGame.Hud.cs`, the monitor block currently reads:

```csharp
        if (_fontRenderer is not null && _messageLog.Count > 0)
        {
            const int mx = 24, my = 26, mw = 162, mh = 56;
            int maxLines = Math.Max(1, mh / _fontRenderer.LineHeight);
            var lines = new List<string>();
            foreach (string msg in _messageLog)
                lines.AddRange(_fontRenderer.WrapText(msg, mw));
```

Replace those six lines with:

```csharp
        if (_fontRenderer is not null && _messageLog.Count > 0)
        {
            // F6: the engine's own rect and wrap budget (display_monitor.cc:31-34, :262),
            // and the '\x95' bullet knob on each message's first line (:244, :266-272).
            int mx = Formats.Text.MonitorLayout.X, my = Formats.Text.MonitorLayout.Y;
            int mh = Formats.Text.MonitorLayout.Height;
            int knobWidth = _fontRenderer.MeasureWidth(Formats.Text.MonitorLayout.Knob.ToString());
            int maxLines = Math.Max(1, Formats.Text.MonitorLayout.MaxDisplayLines(_fontRenderer.LineHeight));
            var lines = new List<string>();
            foreach (string msg in _messageLog)
            {
                // The knob is prefixed to the message text, so it occupies real width on the
                // first line; the budget for that line is reduced by exactly that width, and
                // continuation lines get the full budget (:270 zeroes knobWidth).
                List<string> wrapped = _fontRenderer.WrapText(
                    Formats.Text.MonitorLayout.Knob + msg,
                    Formats.Text.MonitorLayout.WrapBudget(_fontRenderer.LineHeight, knobWidth));
                lines.AddRange(wrapped);
            }
```

Leave the rest of the block (the `MonitorScrollback.Window` call and the draw loop) untouched — it already uses `mx`, `my`, `mh` and `maxLines`.

**Note on fidelity, to record rather than silently accept:** the reference re-widens the budget for continuation lines *within* one message, and `AafFontRenderer.WrapText` takes a single width for the whole string. This wiring therefore applies the narrower first-line budget to every line of a message. `WrapBudget(lineHeight, knobWidth)` and `WrapBudget(lineHeight, 0)` differ by the width of one character, so the effect is at most one word moving to the next line on a long message. If that is not acceptable, the fix is a `WrapText` overload taking a first-line width — **but do not build it in this task**; record it as a follow-up in the commit message and in `docs/BACKLOG.md` during Task 7.

- [ ] **Step 6: Build and run the whole hermetic suite**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet test 2>&1 | tail -5
```

Expected: `Failed: 0`, 995 passed.

- [ ] **Step 7: Commit**

```bash
cd /home/eko/dev/FPOC
git add src/Hexwaste.Formats/Text/MonitorLayout.cs tests/Hexwaste.Formats.Tests/MonitorLayoutTests.cs src/Hexwaste.Viewer/ViewerGame.Hud.cs
git commit -m "fix(hud): monitor bullet knob and the engine's wrap budget

display_monitor.cc:262 wraps to DISPLAY_MONITOR_WIDTH - _max_disp -
knobWidth and prefixes '\\x95' to each message's first line (:244). The HUD
wrapped to a flat 162px with no knob.

_max_disp is a line count subtracted from a pixel width. That is the
shipped engine's own arithmetic, reproduced verbatim and commented so it is
not later 'corrected'.

Resolves the entry's open font question: DISPLAY_MONITOR_FONT 101 routes
through interfaceFontLoad's \"font%d.aaf\" over id-100, so it is font1.aaf,
already loaded. No new asset.

Also adopts the reference rect 23,24 167x60 in place of the hand-tuned
24,26 162x56 — the width and height are the budget itself, so a partial
adoption would match neither. A visible 1-2px shift of the monitor text.

The 80-char line cap in the same loop is a char[80] buffer bound, not a
display rule, and can never bind before the pixel budget at this font
size. Considered and not ported.

Follow-up, not done here: the reference re-widens the budget for
continuation lines within a message; WrapText takes one width, so the
narrower first-line budget applies throughout. Bounded by one character's
width."
```

---

### Task 6: F43 — drop the ammo damage-multiplier clamp

**GATE: do not start this task if Task 1's census reported any ammo proto with `DamageMultiplier == 0`.** In that case F43 is a live damage change needing its own re-record decision; report and stop.

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatMath.cs:156`
- Modify: `tests/Hexwaste.Formats.Tests/CombatMathTests.cs`

**Interfaces:**
- Consumes: `RangedMath.RollDamage(...)` as it exists — its signature does not change.
- Produces: nothing consumed by later tasks.

**The reference** multiplies by `damageMultiplier` unconditionally (`combat.cc:4586-4587`) and guards only the divisor with `if (damageDivisor != 0)` (`:4594-4598`). Hexwaste's melee path already passes the multiplier through unclamped (`CombatMath.cs:44`, `:59`); only `RangedMath.RollDamage` clamps it (`:156`).

The divisor forms stay as they are: `Math.Max(divisor, 1)` and "skip the divide when it would be 0" give the same result, so that half is already equivalent. **Do not touch the divisor.**

- [ ] **Step 1: Write the failing test**

Add to `tests/Hexwaste.Formats.Tests/CombatMathTests.cs` (place it beside the other `RangedMath.RollDamage` tests; if you cannot find them, put it at the end of the class):

```csharp
    [Fact]
    public void RangedAmmoWithAZeroDamageMultiplierDealsNoDamage()
    {
        // combat.cc:4586-4587 multiplies by damageMultiplier unconditionally — only the
        // DIVISOR is guarded (:4594-4598). A clamp to a minimum of 1 would make zero-
        // multiplier ammo deal full damage on the gun path while the melee path
        // (CombatMath.cs:44, :59, both unclamped) and the reference both deal none.
        CritterState attacker = NewState();
        CritterState target = NewState(dr: 0);
        var rng = new CountingCombatRng(10);

        int damage = RangedMath.RollDamage(rng, attacker, target, 10, 10,
            critMultiplier: 2, rangedDamageBonus: 0, ammoDrModifier: 0,
            ammoDamageMultiplier: 0, ammoDamageDivisor: 1);

        Assert.Equal(0, damage);
    }
```

**Before writing it, read `RangedMath.RollDamage`'s actual signature at `src/Hexwaste.Formats/Combat/CombatMath.cs:147-152` and match the parameter names and order exactly.** The call above is written from the parameters named in the entry; if the real signature differs (extra parameters such as `bypassArmor`, `penetrate`, `difficultyDamageModifier`), pass their defaults explicitly rather than reordering. Likewise confirm the helper names `NewState` (`CombatMathTests.cs:9`) and `CountingCombatRng` (`:313`) are still at those lines — this branch has not touched that file, but the file moved once already this month.

- [ ] **Step 2: Run it to verify it fails**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet test --filter FullyQualifiedName~RangedAmmoWithAZeroDamageMultiplier 2>&1 | tail -10
```

Expected: FAIL — `Assert.Equal() Failure: Expected: 0, Actual: 10` (the clamp turns the 0 multiplier into 1, so the full roll survives).

- [ ] **Step 3: Drop the clamp**

In `src/Hexwaste.Formats/Combat/CombatMath.cs`, replace line 156:

```csharp
        int damage = raw * critMultiplier * Math.Max(ammoDamageMultiplier, 1);
```

with:

```csharp
        // F43: the reference multiplies by damageMultiplier UNCONDITIONALLY
        // (combat.cc:4586-4587) and guards only the divisor below (:4594-4598). A
        // Math.Max(_, 1) here would make zero-multiplier ammo deal full damage on the gun
        // path while the reference and our own melee path (CombatMath.cs:44, :59) deal none.
        int damage = raw * critMultiplier * ammoDamageMultiplier;
```

- [ ] **Step 4: Run it to verify it passes**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet test --filter FullyQualifiedName~RangedAmmoWithAZeroDamageMultiplier 2>&1 | tail -5
```

Expected: `Passed!  - Failed: 0, Passed: 1`.

- [ ] **Step 5: Run the whole hermetic suite**

Run:
```bash
cd /home/eko/dev/FPOC && dotnet test 2>&1 | tail -5
```

Expected: `Failed: 0`, 996 passed.

- [ ] **Step 6: Run every golden suite**

This is the one task that touches the damage path. Run all six suites:

```bash
cd /home/eko/dev/FPOC
export FALLOUT2_DIR=./game-data
for s in combat encounter quest endgame opening; do
  echo "=== $s ==="; ./scripts/$s-golden.sh check 2>&1 | tail -3
done
echo "=== census ==="; ./scripts/census-sweep.sh check 2>&1 | tail -3
```

Expected: all pass, **279 fixtures, 0 differing**. Task 1's census established that no shipped ammo carries a 0 multiplier, so byte-identical is the prediction. **If any fixture differs, stop** — the census was wrong or incomplete, and that is a finding to report, not to re-record around.

These suites need `DISPLAY` set and real game data. If they cannot run in this environment, say so explicitly in the commit message rather than implying they passed.

- [ ] **Step 7: Commit**

```bash
cd /home/eko/dev/FPOC
git add src/Hexwaste.Formats/Combat/CombatMath.cs tests/Hexwaste.Formats.Tests/CombatMathTests.cs
git commit -m "fix(combat): stop clamping the ammo damage multiplier on the gun path

combat.cc:4586-4587 multiplies by damageMultiplier unconditionally and
guards only the divisor (:4594-4598). RangedMath.RollDamage clamped the
multiplier to a minimum of 1, so zero-multiplier ammo would have dealt
full damage on the gun path while the reference and our own melee path
(CombatMath.cs:44, :59) deal none.

Inert on shipped data: the ammo census added in this branch found no proto
with a multiplier of 0, and all 279 golden fixtures are byte-identical."
```

---

### Task 7: Citation sweep and backlog reconciliation

Every earlier task changed the line count of a file that other documents cite. In this repo that has repeatedly shipped wrong citations — the predecessor branch needed three separate fix-up commits for exactly this. **Re-derive from the tree, after the last code edit; never transcribe a line number from a document, a review, or this plan.**

**Files:**
- Modify: `docs/BACKLOG.md`
- Modify: any document whose citations into the touched files have drifted

- [ ] **Step 1: Find every inbound citation into the touched files**

Run:
```bash
cd /home/eko/dev/FPOC
grep -rn 'ViewerGame\.cs:[0-9]\|ViewerGame\.Hud\.cs:[0-9]\|ViewerGame\.Panels\.cs:[0-9]\|ScriptHost\.cs:[0-9]\|MapFile\.cs:[0-9]\|CombatMath\.cs:[0-9]\|CombatMathTests\.cs:[0-9]' docs/ --include='*.md' | sort
```

This lists every citation that *may* have moved. Each one must be checked against the tree, not assumed.

- [ ] **Step 2: Re-derive the anchors this branch actually moved**

Run:
```bash
cd /home/eko/dev/FPOC
grep -n 'public required int Frame' src/Hexwaste.Formats/Map/MapFile.cs
grep -n 'public static bool ApplyDirectAnim\|public void Anim(int objectHandle' src/Hexwaste.Formats/Int/ScriptHost.cs
grep -n 'private void DrawTalkingHead\|private int HeadAccumulatedHotX\|private int HeadFrameIndex' src/Hexwaste.Viewer/ViewerGame.cs
grep -n 'private void DrawAutomap\|AutomapPaint.Overpaints' src/Hexwaste.Viewer/ViewerGame.Panels.cs
grep -n 'MonitorLayout' src/Hexwaste.Viewer/ViewerGame.Hud.cs
grep -n 'raw \* critMultiplier \* ammoDamageMultiplier' src/Hexwaste.Formats/Combat/CombatMath.cs
```

Update every stale citation found in Step 1 to the numbers this step reports. For a citation that describes code no longer present, add an explicit as-of note (e.g. "as of `df36ac5`") rather than silently repointing it at unrelated code.

- [ ] **Step 3: Reconcile the six backlog entries**

In `docs/BACKLOG.md`:

- **F4, F5** → mark SHIPPED with the Task 4 commit SHA. Record that both were one expression, that the rotation-offset terms were deliberately not ported because they are 0 on all shipped heads, and the prefix-sum-vs-field decision with its lip-sync divergence.
- **F6** → mark SHIPPED with the Task 5 commit SHA. Record the resolved font question (101 = `font1.aaf` via `interfaceFontLoad`), that `_max_disp`'s unit mismatch is faithful and intentional, the adoption of the reference rect, and **file the continuation-line budget as a new follow-up entry** — it is a real residual, and F13 was lost for a release cycle by leaving exactly this kind of remark inside a shipped entry.
- **F7** → mark SHIPPED with the Task 3 commit SHA, and **correct the entry's two errors**: the guard is scenery-over-wall only, not "any later object mark"; and it belongs to `DrawAutomap`, not the mini-map's `Plot` call the entry cited.
- **F9** → mark SHIPPED with the Task 2 commit SHA. Record that the change was purely additive (`AnimRequested` already gated `anim < 40`), that `MapObject.Frame` became settable, and the deliberate negative-rotation lower bound.
- **F43** → if Task 6 ran: mark SHIPPED with its SHA, and replace "unverified whether any shipped ammo proto carries a multiplier of 0" with the census's actual number and the name of the test that now guards it. If Task 6 was gated off: record the census result, that the entry is now **verified live**, and that it awaits a re-record decision.

- [ ] **Step 4: Verify the sweep found everything**

Re-run Step 1's grep and spot-check three citations at random against the tree. Also confirm no document still repeats a number this branch invalidated:

```bash
cd /home/eko/dev/FPOC
grep -rn 'CombatMath\.cs:156\|MapFile\.cs:47\|ScriptHost\.cs:1610\|ViewerGame\.cs:6194\|ViewerGame\.Hud\.cs:198\|Panels\.cs:1015' docs/ --include='*.md'
```

Any hit is either correct-by-coincidence (verify it) or stale (fix it). `Panels.cs:1015` specifically is the wrong-path citation F7's entry carried — it should no longer appear as the fix site.

- [ ] **Step 5: Final full verification**

Run:
```bash
cd /home/eko/dev/FPOC
dotnet test 2>&1 | tail -5
git status --short
```

Expected: `Failed: 0`, and a clean tree apart from the documentation edits about to be committed.

- [ ] **Step 6: Commit**

```bash
cd /home/eko/dev/FPOC
git add docs/
git commit -m "docs: reconcile the six Tier F entries and sweep their citations

F4, F5, F6, F7, F9 and F43 marked shipped with their SHAs, each carrying
what the implementation actually established rather than what the entry
predicted. F7's entry corrected on two counts (the guard is narrower than
recorded; it cites the mini-map instead of DrawAutomap). F6's continuation-
line wrap budget filed as its own follow-up rather than left as a remark
inside a shipped entry.

Citations into the six touched files re-derived from the tree after the
last code edit, not transcribed."
```

---

## Verification Summary

| Item | How it is proven |
|---|---|
| F43 census | `[GameDataFact]`, reports counts to stdout, fails if any multiplier is 0 |
| F9 | 5 hermetic tests: rotation set, upper bound, lower bound, frame set, fall-through |
| F7 | 5 hermetic tests over the truth table, including wall-after-scenery |
| F4 / F5 | Build + full suite; the 186-head population re-counted from the DAT. **No golden covers dialog pixels** — say so, do not imply coverage |
| F6 | 5 hermetic tests on the budget arithmetic. **No golden covers HUD pixels** — the rect change is visible and unscreenshotted unless one is taken |
| F43 | 1 hermetic test + all 279 golden fixtures byte-identical |

## What this plan deliberately does not do

- **F44** (unifying `ReduceByArmor` with `RangedMath.RollDamage`) — same file as F43, explicitly out of scope; it needs its own measurement pass.
- **F42a** (a golden fixture with nonzero melee DR) — separate entry, separate work.
- **The `WrapText` first-line-width overload** — filed in Task 7, not built.
- **`DrawPipboyMiniMap`** — not the counterpart of the reference branch being ported; left alone.
