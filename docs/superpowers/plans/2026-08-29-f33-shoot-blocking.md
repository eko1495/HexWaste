# F33 — Shoot-Blocking Two-Stage Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the reference's two-stage line-of-fire design — one coarse blocking predicate plus a per-consumer policy — and then give each consumer the policy its reference counterpart actually has.

**Architecture:** Split `ShootBlockerAt` into a faithful coarse predicate and explicit per-consumer policies, in a step that is provably behaviour-neutral, before changing any policy. Then adopt the reference policies one consumer group at a time, measuring which fixtures move at each step and explaining every movement before re-recording it.

**Tech Stack:** C# / .NET 10, xUnit, and the golden transcript suites (now ~2 minutes for all six).

**Design spec:** `docs/superpowers/specs/2026-08-29-f33-shoot-blocking-design.md`

## Global Constraints

- **Port, never guess.** Every behavioural change carries a comment naming its source, in this repo's form: `// ported from fallout2-ce src/x.cc f()`. If a detail cannot be confirmed from `reference/fallout2-ce`, **stop and ask**.
- **`alexbatalov e97087b` is authoritative for vanilla.** The `community/main` fork is a bug-fix candidate source only. The SFALL extension visible at `combat.cc:5906` is **out of scope** — target the vanilla behaviour at the pin.
- Target `net10.0`. **No new NuGet dependencies.** Never commit game assets.
- The hermetic suite (`dotnet test`) must be **0 failed** at the end of every task. Baseline: **1016 passed / 0 failed / 94 skipped**.
- Golden suites need `export DISPLAY=:0` and `FALLOUT2_DIR="$(pwd)/game-data"`. All six now run in about two minutes.
- **A moved fixture is never re-recorded because the suite went red.** Diff it, explain the movement in terms of the change just made, and only then re-record. **A fixture that moves for a reason the change does not explain is a stop condition** — report it.
- Conventional commits; commit at the end of every task.

---

## Background the implementer needs

The reference's `_obj_shoot_blocking_at` (`reference/fallout2-ce/src/object.cc:2440`) is a **coarse**
"is there something here" query. Each caller then applies its own filter to the object it gets back.
The five combat-side callers:

| caller | policy |
|---|---|
| `combat.cc:3584` — shot-blocked roll | requires `SHOOT_THRU == 0`, then type, then a to-hit roll |
| `combat.cc:3641` — burst / continuous walk | **type only; no `SHOOT_THRU` test** |
| `combat.cc:3956` — missed-shot collateral | requires `SHOOT_THRU == 0` |
| `combat.cc:5906` — `combat_is_shot_blocked` to-hit penalty | type and `!= targetObj`; **no `SHOOT_THRU` test** |
| `combat_ai.cc:2585` — friendly-fire check | no flag test; identity comparison |

Hexwaste collapsed this. **Two different callers' policies are currently fused into shared
infrastructure**, which is the thing this plan untangles:

- `ShootBlockerAt` (`src/Hexwaste.Viewer/ViewerGame.CombatHost.cs:226`) bakes in
  `NO_BLOCK == 0 && SHOOT_THRU == 0` and excludes **both** shooter and target.
- `LineOfFire.Trace` (`src/Hexwaste.Formats/Combat/LineOfFire.cs`) bakes in `combat.cc:5906`'s
  policy: living critters are counted and the walk resumes past them, non-critters block. It **also**
  excludes the target tile, so the target exclusion is applied twice.

The reference flag values (`reference/fallout2-ce/src/obj_types.h`): `OBJECT_HIDDEN = 0x01`,
`OBJECT_NO_BLOCK = 0x10`, `OBJECT_MULTIHEX = 0x800`, `OBJECT_LIGHT_THRU = 0x20000000`,
`OBJECT_SHOOT_THRU = 0x80000000`. Note `SHOOT_THRU` is the sign bit — in C# it must be handled as
`uint`, which the current code already does correctly.

**Do not confuse two different multihex things.** `LineOfFire`'s doc comment says the `+1 MULTIHEX`
crowd bump (`combat.cc:5921`) is deliberately unported — that is a *to-hit* term. This plan adds a
different thing: the predicate's own **six-neighbour adjacency scan** for multihex objects
(`object.cc`, the second loop of `_obj_shoot_blocking_at`). Leave the crowd bump alone.

## The ten consumers and their reference counterparts

| our call site | what it is | reference counterpart |
|---|---|---|
| `CombatEngine.cs:274` | dude gun attack, refuse if blocked | `combat.cc:3584` |
| `CombatEngine.cs:361` | dude gun attack, refuse + message | `combat.cc:3584` |
| `CombatEngine.cs:495` | a third refusal path | `combat.cc:3584` |
| `CombatEngine.cs:686` | burst line, collects critters | `combat.cc:3641` |
| `CombatEngine.cs:748` | missed-shot overshoot victim | `combat.cc:3956` |
| `CombatEngine.cs:1819` | explosion line-of-sight | identify in Task 7 |
| `CombatEngine.cs:2349` | friendly-on-fire-line | `combat_ai.cc:2585` |
| `CombatEngine.cs:3528` | enemy approach / crowd count | `combat.cc:5906` |
| `ViewerGame.cs:3758` | to-hit line-of-fire penalty | `combat.cc:5906` |
| `ViewerGame.Rendering.cs:369` | combat outline colour | identify in Task 7 |

Re-derive every one of these line numbers from the tree before you rely on it; this file's own edits
will move them.

---

### Task 1: Measure what is actually on the line — no behaviour changes

The spec's central open question is what sits between shooter and target in
`denbus2-burst-collateral`, the fixture the previous attempt broke. This task answers it and changes
nothing else.

**Files:**
- Modify: `src/Hexwaste.Viewer/Program.cs` (add the CLI flag)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs` (add the `StartupAction` record)
- Modify: `src/Hexwaste.Viewer/ViewerGame.Harness.cs` (implement the probe)

**Interfaces:**
- Produces: `--shot-blockers <shooterHex> <targetHex>`, printing one line per candidate object found
  along the line, plus a summary line. Later tasks use it to explain fixture movements.

**Follow the existing probe pattern.** `--walker-restart-probe` is a good model: a `StartupAction`
record on `ViewerGame`, a `case` in `Program.cs`, and a `case` in the harness switch.

- [ ] **Step 1: Add the probe**

The probe must, for every tile on the line between the two hexes, report **every** solid object on
that tile — not just the one the current predicate would return — because the whole point is to see
what the current predicate is filtering out. For each object print its tile, type, pid, and flags in
hex, then whether each of the five reference policies would treat it as an obstruction.

Add to `src/Hexwaste.Viewer/ViewerGame.cs`, beside the other `StartupAction` records:

```csharp
        /// <summary>F33: dump every solid object on the line between two hexes, with the flags
        /// and the verdict each reference caller policy would reach.</summary>
        public sealed record ShotBlockers(int ShooterHex, int TargetHex) : StartupAction;
```

Add to `src/Hexwaste.Viewer/Program.cs`, beside the other probe cases:

```csharp
        case "--shot-blockers" when i + 2 < args.Length: // F33: what is actually on the line
            actions.Add(new ViewerGame.StartupAction.ShotBlockers(int.Parse(args[++i]), int.Parse(args[++i])));
            break;
```

Add to the harness switch in `src/Hexwaste.Viewer/ViewerGame.Harness.cs`:

```csharp
                case StartupAction.ShotBlockers(var sbShooter, var sbTarget):
                {
                    // F33: the coarse question the reference's _obj_shoot_blocking_at answers, and
                    // the five different verdicts its callers reach from the same object.
                    const int hidden = 0x01, noBlock = 0x10, multiHex = 0x800;
                    const uint shootThru = 0x80000000;
                    Console.WriteLine($"shot-blockers: from={sbShooter} to={sbTarget} elev={_elevation}");
                    int seen = 0;
                    Formats.Combat.LineOfFire.Trace(sbShooter, sbTarget, tile =>
                    {
                        foreach (MapObject o in _solidObjects[_elevation].Where(o => o.HexTile == tile))
                        {
                            uint f = (uint)o.Flags;
                            bool isHidden = (f & hidden) != 0;
                            bool nb = (f & noBlock) != 0;
                            bool st = (f & shootThru) != 0;
                            bool mh = (f & multiHex) != 0;
                            ObjectType t = Fid.Type(o.Fid);
                            bool typeOk = t is ObjectType.Wall or ObjectType.Scenery
                                || (t is ObjectType.Critter && !o.IsDead);
                            // The reference predicate: !HIDDEN && (NO_BLOCK==0 || SHOOT_THRU==0) && type
                            bool refCoarse = !isHidden && (!nb || !st) && typeOk;
                            // Ours today: !HIDDEN && NO_BLOCK==0 && SHOOT_THRU==0 && type
                            bool ours = !isHidden && !nb && !st && typeOk;
                            Console.WriteLine(
                                $"  tile={tile} pid={o.Pid} type={t} flags=0x{f:X8}"
                                + $" hidden={(isHidden ? 1 : 0)} noBlock={(nb ? 1 : 0)}"
                                + $" shootThru={(st ? 1 : 0)} multiHex={(mh ? 1 : 0)}"
                                + $" refCoarse={(refCoarse ? 1 : 0)} ours={(ours ? 1 : 0)}"
                                + $" p3584={(refCoarse && !st ? 1 : 0)}"
                                + $" p3641={(refCoarse ? 1 : 0)}"
                                + $" p3956={(refCoarse && !st ? 1 : 0)}"
                                + $" p5906={(refCoarse && t is not ObjectType.Critter ? 1 : 0)}");
                            seen++;
                        }
                        return null; // never block: we want the whole line, not the first hit
                    });
                    Console.WriteLine($"shot-blockers: {seen} object(s) on the line");
                    break;
                }
```

Note the delegate returns `null` deliberately — returning a blocker would stop the walk at the first
object and hide everything beyond it, which is exactly what must not happen here.

- [ ] **Step 2: Build and confirm the hermetic suite is untouched**

```bash
cd /home/eko/dev/FPOC
dotnet build 2>&1 | tail -3
dotnet test 2>&1 | tail -2
```

Expected: build succeeds, `Failed: 0`, 1016 passed / 94 skipped — the probe adds no test and changes
no behaviour.

- [ ] **Step 3: Find the fixture's shooter and target hexes**

```bash
cd /home/eko/dev/FPOC
grep -n 'denbus2-burst-collateral' scripts/encounter-golden.sh
```

Read the scenario's arguments to identify the map and the hexes involved. Report both.

- [ ] **Step 4: Run the probe on that scenario's geometry**

Use the map and hexes from Step 3:

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
src/Hexwaste.Viewer/bin/Debug/net10.0/Hexwaste.Viewer \
  --game-dir ./game-data --no-audio --no-ambient --map <MAP> \
  --shot-blockers <SHOOTER_HEX> <TARGET_HEX> 2>/dev/null
```

- [ ] **Step 5: Classify the result against the pre-registered outcomes**

The spec registered four outcomes **in advance** so the result cannot be rationalised afterwards.
State plainly which one the data shows:

- **Objects with `shootThru=1` and type Scenery on the line** → the reference genuinely ends the
  burst walk there; our fixture encodes our own behaviour and will need a deliberate re-record.
- **Objects with `noBlock=1, shootThru=0`** → the 5,368-object population is the live one; our
  predicate blocks too little and the fix is small and opposite in direction to the old framing.
- **The only candidate is the shooter or the target** → the exclusion divergence dominates and must
  be settled before anything is concluded about flags.
- **No object on the line under any policy** → the burst stopped for a reason outside this predicate;
  F33 as scoped is not the cause, and that is a finding worth reporting on its own.

**Do not adjust the plan to fit the result in this task.** Record which outcome holds and stop.

- [ ] **Step 6: Commit**

```bash
cd /home/eko/dev/FPOC
git add src/Hexwaste.Viewer/Program.cs src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/ViewerGame.Harness.cs
git commit -m "test(combat): add --shot-blockers, the F33 line-of-fire probe

Dumps every solid object between two hexes with its flags and the verdict
each of the five reference caller policies would reach. The delegate
deliberately never blocks, so the whole line is reported rather than the
first hit.

No behaviour change: the probe reads state and prints."
```

---

### Task 2: Split the predicate into two stages, provably without changing behaviour

This is the enabling move, and it is the one task in the plan that must move **nothing**. Separating
the restructure from the behaviour change is what makes every later task's fixture movement
attributable.

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs` (`ShootBlockerAt`)
- Modify: `src/Hexwaste.Formats/Combat/LineOfFire.cs`
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` (the eight call sites)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs`, `src/Hexwaste.Viewer/ViewerGame.Rendering.cs` (two call sites)
- Test: `tests/Hexwaste.Formats.Tests/ShootBlockerPolicyTests.cs` (create)

**Interfaces:**
- Produces:
  - `public enum ShotPolicy { RefusesOnShootThru, TypeOnly, NonCritterOnly }` in
    `src/Hexwaste.Formats/Combat/` — the three distinct filters the reference callers apply.
  - `public static bool ShotPolicyRules.Obstructs(ShotPolicy policy, MapObject candidate, bool isTarget)`
    — the per-consumer filter, pure and hermetically testable.
  - `ShootBlockerAt` keeps its signature for now; its *body* becomes the coarse predicate with the
    old terms moved into the policies.

**The behaviour-neutrality argument you must preserve.** Today's effective test at every consumer is
`!HIDDEN && NO_BLOCK == 0 && SHOOT_THRU == 0 && type`. After this task the coarse predicate is
`!HIDDEN && (NO_BLOCK == 0 || SHOOT_THRU == 0) && type` and each consumer applies
`NO_BLOCK == 0 && SHOOT_THRU == 0` as its policy. Composed, those are identical — the coarse
predicate's disjunction is subsumed. **Every consumer gets the same policy in this task**; the
policies only start to differ in Task 5.

- [ ] **Step 1: Write the failing test**

Create `tests/Hexwaste.Formats.Tests/ShootBlockerPolicyTests.cs`:

```csharp
using Hexwaste.Formats;
using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// F33: the reference splits line-of-fire into a coarse predicate
/// (_obj_shoot_blocking_at, object.cc:2440) and a per-caller filter. These pin the
/// three distinct filters its callers apply, so a later task can give each consumer
/// the one its counterpart has without guessing.
/// </summary>
public class ShootBlockerPolicyTests
{
    private const int Hidden = 0x01, NoBlock = 0x10;
    private const int ShootThru = unchecked((int)0x80000000);

    private static MapObject Obj(int flags, ObjectType type, bool dead = false) => new()
    {
        Id = 1, HexTile = 100, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = ((int)type << 24), Flags = flags, Pid = ((int)type << 24) | 5,
        CombatResults = dead ? 1 : 0,
    };

    [Fact]
    public void RefusesOnShootThruSkipsAShootThruObject() =>
        // combat.cc:3585 re-tests the flag the coarse predicate let through.
        Assert.False(ShotPolicyRules.Obstructs(
            ShotPolicy.RefusesOnShootThru, Obj(ShootThru, ObjectType.Scenery), isTarget: false));

    [Fact]
    public void RefusesOnShootThruBlocksAPlainScenery() =>
        Assert.True(ShotPolicyRules.Obstructs(
            ShotPolicy.RefusesOnShootThru, Obj(0, ObjectType.Scenery), isTarget: false));

    [Fact]
    public void TypeOnlyBlocksAShootThruObject() =>
        // combat.cc:3641 applies no flag test at all — this is the burst walk.
        Assert.True(ShotPolicyRules.Obstructs(
            ShotPolicy.TypeOnly, Obj(ShootThru, ObjectType.Scenery), isTarget: false));

    [Fact]
    public void NonCritterOnlyIgnoresALivingCritter() =>
        // combat.cc:5906 breaks only on non-critters; critters are counted and passed.
        Assert.False(ShotPolicyRules.Obstructs(
            ShotPolicy.NonCritterOnly, Obj(0, ObjectType.Critter), isTarget: false));

    [Fact]
    public void NonCritterOnlyIgnoresTheTarget() =>
        // combat.cc:5909's `obstacle != targetObj`.
        Assert.False(ShotPolicyRules.Obstructs(
            ShotPolicy.NonCritterOnly, Obj(0, ObjectType.Wall), isTarget: true));

    [Fact]
    public void NonCritterOnlyBlocksAWallThatIsNotTheTarget() =>
        Assert.True(ShotPolicyRules.Obstructs(
            ShotPolicy.NonCritterOnly, Obj(0, ObjectType.Wall), isTarget: false));
}
```

Before running it, check `MapObject`'s required members and `CombatResults`' dead bit against
`src/Hexwaste.Formats/Map/MapFile.cs` and adapt the helper if the shape differs — do not invent
members.

- [ ] **Step 2: Run it and confirm it fails for the right reason**

```bash
dotnet test --filter FullyQualifiedName~ShootBlockerPolicyTests 2>&1 | tail -10
```

Expected: a build error naming `ShotPolicy` / `ShotPolicyRules` as undefined. A different failure
means the test helper is wrong, not the production code.

- [ ] **Step 3: Add the policy type**

Create `src/Hexwaste.Formats/Combat/ShotPolicy.cs`:

```csharp
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Combat;

/// <summary>The filters the reference's line-of-fire callers apply to the object
/// _obj_shoot_blocking_at hands back. The predicate is deliberately coarse; the policy
/// is where each caller decides what the object means for it.</summary>
public enum ShotPolicy
{
    /// <summary>ported from fallout2-ce src/combat.cc:3585 (the shot-blocked roll) and
    /// :3963 (the missed-shot collateral target): a SHOOT_THRU object is not an obstruction.</summary>
    RefusesOnShootThru,

    /// <summary>ported from fallout2-ce src/combat.cc:3644 (the burst / continuous walk):
    /// no flag test at all — anything the coarse predicate returns ends the walk.</summary>
    TypeOnly,

    /// <summary>ported from fallout2-ce src/combat.cc:5908-5909 (combat_is_shot_blocked):
    /// only a non-critter that is not the target obstructs; critters are counted and passed.</summary>
    NonCritterOnly,

    /// <summary>TEMPORARY. Reproduces the pre-F33 collapsed behaviour — NO_BLOCK == 0 and
    /// SHOOT_THRU == 0 — so the predicate can be made faithful without changing what any
    /// consumer sees. Every consumer moves off this in Task 5, and it is deleted in Task 7.
    /// It has no reference counterpart and must never be the answer for a shipped consumer.</summary>
    LegacyCollapsed,
}

/// <summary>Applies a <see cref="ShotPolicy"/> to a candidate the coarse predicate returned.</summary>
public static class ShotPolicyRules
{
    private const int NoBlock = 0x10;
    private const int ShootThru = unchecked((int)0x80000000);

    public static bool Obstructs(ShotPolicy policy, MapObject candidate, bool isTarget) => policy switch
    {
        ShotPolicy.RefusesOnShootThru => (candidate.Flags & ShootThru) == 0,
        ShotPolicy.TypeOnly => true,
        ShotPolicy.NonCritterOnly => Fid.Type(candidate.Fid) is not ObjectType.Critter && !isTarget,
        ShotPolicy.LegacyCollapsed => (candidate.Flags & NoBlock) == 0 && (candidate.Flags & ShootThru) == 0,
        _ => true,
    };
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd /home/eko/dev/FPOC && dotnet test --filter FullyQualifiedName~ShootBlockerPolicyTests 2>&1 | tail -4
```

Expected: `Passed!  - Failed: 0, Passed: 6`.

- [ ] **Step 5: Move the old terms out of the predicate and into a policy at every consumer**

In `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs`, change only the flag term — the `&&` between the
two flag tests becomes the reference's `||`:

```csharp
    /// <summary>The COARSE line-of-fire query. ported from fallout2-ce
    /// src/object.cc _obj_shoot_blocking_at() (:2440), tile phase: !HIDDEN &&
    /// (NO_BLOCK == 0 || SHOOT_THRU == 0), then the type test. The disjunction is deliberate —
    /// each caller decides what SHOOT_THRU means for it, via ShotPolicyRules.Obstructs.
    /// Do NOT re-add a flag term here; that is what collapsed the two stages originally.</summary>
    public MapObject? ShootBlockerAt(int tile, MapObject shooter, MapObject target)
    {
        const int noBlock = 0x10;
        const uint shootThru = 0x80000000;
        return _solidObjects[_elevation].FirstOrDefault(o =>
            o.HexTile == tile && o != shooter && o != target && !o.IsHidden
            && ((o.Flags & noBlock) == 0 || ((uint)o.Flags & shootThru) == 0)
            && (Fid.Type(o.Fid) is ObjectType.Wall or ObjectType.Scenery
                || (Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead)));
    }
```

Then wrap **every** `LineOfFire.Trace` delegate so the removed terms are reapplied as a policy. The
shape, using `CombatEngine.cs:274` as the worked example — apply the same shape at all ten sites:

```csharp
            (MapObject? blocker, crittersInPath) = LineOfFire.Trace(
                dude.HexTile, target.HexTile,
                tile => _host.ShootBlockerAt(tile, dude, target) is { } o
                        && ShotPolicyRules.Obstructs(ShotPolicy.LegacyCollapsed, o, isTarget: o == target)
                    ? o : null);
```

Leave `LineOfFire.Trace`'s own critter-counting and target-tile exclusion **unchanged** in this task —
they become policies in Task 5, and changing them here would break this task's neutrality.

- [ ] **Step 6: Prove behaviour-neutrality**

```bash
cd /home/eko/dev/FPOC
dotnet test 2>&1 | tail -2
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
for s in combat quest endgame opening encounter; do ./scripts/$s-golden.sh check 2>&1 | tail -1; done
./scripts/census-sweep.sh check 2>&1 | tail -1
```

Expected: `Failed: 0` with 1022 passed (1016 + 6), and **six `ALL PASS` verdicts with nothing
re-recorded**. A moved fixture here means the split was not behaviour-neutral — that is a stop
condition, not something to record.

- [ ] **Step 7: Commit**

```bash
cd /home/eko/dev/FPOC
git add src/Hexwaste.Formats/Combat/ShotPolicy.cs tests/Hexwaste.Formats.Tests/ShootBlockerPolicyTests.cs src/Hexwaste.Viewer/ViewerGame.CombatHost.cs src/Hexwaste.Formats/Combat/CombatEngine.cs src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/ViewerGame.Rendering.cs
git commit -m "refactor(combat): split line-of-fire into a coarse predicate and a policy

The reference's _obj_shoot_blocking_at (object.cc:2440) is a coarse query and
each caller filters what it returns; Hexwaste had the filters baked into the
predicate, so no consumer could have its own policy. This separates the two
without changing any behaviour: the coarse predicate gains the reference's
disjunction, and every consumer gets a LegacyCollapsed policy that composes
back to exactly the old test.

All six golden suites byte-identical, nothing re-recorded — which is the
point of doing the restructure alone."
```

---

### A note on ordering, because the spec warns about exactly this

The spec says **"Do not do step 1 without step 2"** — do not make the predicate faithful while the
consumers still carry the old policies, because that is the change that was tried and reverted.

Tasks 3 and 4 do change the predicate while every consumer is still on `LegacyCollapsed`, so read why
that is not the thing the spec forbids. The reverted attempt adopted the reference's **flag
operator**, whose whole meaning depends on the caller filters — the disjunction exists precisely so
callers can decide what `SHOOT_THRU` means. Task 2 lands that operator **behaviour-neutrally**, with
the filter moved rather than dropped. What Tasks 3 and 4 change is different: the multihex phase and
the exclusion set are terms no caller filter interacts with, so each can be landed and measured on
its own. If either turns out to interact with a policy after all, that surfaces as an unexplained
fixture movement — which is a stop condition in both tasks.

---

### Task 3: Port the missing multihex adjacency phase

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs` (`ShootBlockerAt`)
- Test: `tests/Hexwaste.Formats.Tests/ShootBlockerPolicyTests.cs` (extend)

**Interfaces:**
- Consumes: `ShotPolicy` / `ShotPolicyRules` from Task 2.

`_obj_shoot_blocking_at`'s second loop, which Hexwaste never ported: when the tile itself yields
nothing, the six adjacent tiles are scanned for objects carrying `OBJECT_MULTIHEX` (`0x800`), under a
**stricter** gate — `!HIDDEN && NO_BLOCK == 0`, with **no `SHOOT_THRU` disjunction** — plus the same
exclusion and type test. Read the reference's second loop yourself before writing this; the gate
differing from the first loop's is the detail most likely to be copied wrong.

This makes us block **more**, so fixtures may move.

- [ ] **Step 1: Find the neighbour helper this repo already has**

```bash
cd /home/eko/dev/FPOC
grep -rn 'TileInDirection\|Neighbors\|Neighbours' src/Hexwaste.Formats/Hex/*.cs | head
```

Use the existing hex-neighbour function; do not write a new one.

- [ ] **Step 2: Add the adjacency phase to `ShootBlockerAt`**

Rewrite `ShootBlockerAt` so the tile phase's result is captured, and the adjacency scan runs only
when it is null. Substitute the real neighbour helper name from Step 1 for `TileInDirection`:

```csharp
        MapObject? onTile = _solidObjects[_elevation].FirstOrDefault(o =>
            o.HexTile == tile && o != shooter && o != target && !o.IsHidden
            && ((o.Flags & noBlock) == 0 || ((uint)o.Flags & shootThru) == 0)
            && (Fid.Type(o.Fid) is ObjectType.Wall or ObjectType.Scenery
                || (Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead)));
        if (onTile is not null)
            return onTile;

        // ported from fallout2-ce src/object.cc _obj_shoot_blocking_at()'s SECOND loop (:2440):
        // with nothing on the tile itself, the six neighbours are scanned for MULTIHEX objects
        // under a STRICTER gate — !HIDDEN && NO_BLOCK == 0, with NO SHOOT_THRU disjunction. The
        // asymmetry with the tile phase above is the reference's own; do not "harmonise" it.
        const int multiHex = 0x800;
        for (int dir = 0; dir < 6; dir++)
        {
            int adj = Formats.Hex.HexGrid.TileInDirection(tile, dir, 1);
            if (adj < 0)
                continue;
            MapObject? mh = _solidObjects[_elevation].FirstOrDefault(o =>
                o.HexTile == adj && (o.Flags & multiHex) != 0
                && o != shooter && o != target && !o.IsHidden
                && (o.Flags & noBlock) == 0
                && (Fid.Type(o.Fid) is ObjectType.Wall or ObjectType.Scenery
                    || (Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead)));
            if (mh is not null)
                return mh;
        }
        return null;
```

If Task 4 has already run, the `o != target` terms above are gone — reconcile with whatever the file
actually contains rather than pasting over it.

- [ ] **Step 3: Run the hermetic suite and all six golden suites**

```bash
cd /home/eko/dev/FPOC
dotnet test 2>&1 | tail -2
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
for s in combat quest endgame opening encounter; do echo "=== $s ==="; ./scripts/$s-golden.sh check 2>&1 | tail -1; done
./scripts/census-sweep.sh check 2>&1 | tail -1
```

- [ ] **Step 4: Explain every moved fixture before re-recording it**

For each fixture that differs, use `--shot-blockers` from Task 1 on that scenario's geometry and show
the multihex object now blocking. **Only then** re-record, and quote the diff in your report. A
fixture that moves without a multihex object on its line is a stop condition — report it and stop.

- [ ] **Step 5: Commit**

```bash
cd /home/eko/dev/FPOC
git add src/Hexwaste.Viewer/ViewerGame.CombatHost.cs tests/Hexwaste.Formats.Tests/ShootBlockerPolicyTests.cs
git commit -m "fix(combat): port _obj_shoot_blocking_at's multihex adjacency phase

The reference scans the six neighbouring tiles for OBJECT_MULTIHEX objects
when the tile itself yields nothing, under a stricter gate than the first
loop's — NO_BLOCK only, with no SHOOT_THRU disjunction. Hexwaste had none of
this, so it blocked too little around multihex critters."
```

---

### Task 4: Stop excluding the target from the coarse predicate

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs` (`ShootBlockerAt`)

`_make_straight_path_func` (`animation.cc:1951`) calls `callback(obj, from, obj->elevation)`, so the
predicate's `excludeObj` is the walker's first argument — the **attacker** at every combat call site.
`ShootBlockerAt` excludes shooter *and* target. The target exclusion is ours alone.

Note the target is *also* excluded inside `LineOfFire.Trace` (its `tile != toTile` guard, which is
`combat.cc:5909`'s `obstacle != targetObj` — a **caller policy**, and one that `NonCritterOnly`
now expresses). Removing the predicate's copy is what this task does; the walker's copy is dealt with
in Task 5, where it becomes a policy rather than a hard-coded rule.

- [ ] **Step 1: Remove the target exclusion from the predicate**

Drop `o != target` from `ShootBlockerAt`'s filter. Keep the shooter exclusion. Update the doc comment
to state that the reference excludes only the attacker and cite `animation.cc:1951`.

- [ ] **Step 2: Run everything**

```bash
cd /home/eko/dev/FPOC
dotnet test 2>&1 | tail -2
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
for s in combat quest endgame opening encounter; do echo "=== $s ==="; ./scripts/$s-golden.sh check 2>&1 | tail -1; done
./scripts/census-sweep.sh check 2>&1 | tail -1
```

Fixtures may not move at all, because `LineOfFire.Trace` still skips the target tile independently.
If nothing moves, say so — that is the expected outcome and it is worth stating rather than implying
the change did something.

- [ ] **Step 3: Explain any movement, then commit**

Same rule as Task 3: diff, explain, then re-record. Then:

```bash
cd /home/eko/dev/FPOC
git add src/Hexwaste.Viewer/ViewerGame.CombatHost.cs
git commit -m "fix(combat): exclude only the attacker from the shoot-blocking predicate

_make_straight_path_func passes its first argument as excludeObj, which is the
attacker at every combat call site (animation.cc:1951). Excluding the target
as well was ours alone. LineOfFire.Trace still skips the target tile, so this
may be inert on its own; Task 5 turns that into a policy."
```

---

### Task 5: Give each consumer its reference policy

The behaviour change this whole plan exists for. Every consumer moves off `LegacyCollapsed` onto the
policy its reference counterpart applies.

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` (eight call sites)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs`, `src/Hexwaste.Viewer/ViewerGame.Rendering.cs`
- Modify: `src/Hexwaste.Formats/Combat/LineOfFire.cs`
- Modify: `src/Hexwaste.Formats/Combat/ShotPolicy.cs` (remove `LegacyCollapsed`)

Assign from the consumer table in this plan's Background section: the three refusal paths take
`RefusesOnShootThru`; the burst line takes `TypeOnly`; the overshoot victim takes
`RefusesOnShootThru`; the friendly-fire check takes `TypeOnly` (`combat_ai.cc:2585` applies no flag
test); the crowd-count and to-hit-penalty consumers take `NonCritterOnly`. The explosion
line-of-sight and rendering-outline consumers are settled in Task 7 — leave them on `LegacyCollapsed`
until then and say so in the commit.

`LineOfFire.Trace`'s hard-coded critter counting is `NonCritterOnly`'s behaviour. Consumers that take
that policy keep it; the others must not silently inherit it. Decide how the walker expresses this —
a policy parameter is the obvious route — and state the choice in your report.

- [ ] **Step 1: Assign the policies**

Change each call site, one policy at a time rather than all at once, so a fixture movement is
attributable to a single consumer.

- [ ] **Step 2: After each policy group, run everything**

```bash
cd /home/eko/dev/FPOC
dotnet test 2>&1 | tail -2
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
for s in combat quest endgame opening encounter; do echo "=== $s ==="; ./scripts/$s-golden.sh check 2>&1 | tail -1; done
./scripts/census-sweep.sh check 2>&1 | tail -1
```

- [ ] **Step 3: `denbus2-burst-collateral` specifically**

This fixture exercises the burst walk, the consumer that takes `TypeOnly` — the policy with no
`SHOOT_THRU` test. Task 1 pre-registered what its movement means. If it moves, quote Task 1's
measurement and say which registered outcome this confirms. **If it moves in a way Task 1's data does
not explain, stop and report** rather than re-recording.

- [ ] **Step 4: Commit**

```bash
cd /home/eko/dev/FPOC
git add -A src/
git commit -m "fix(combat): give each line-of-fire consumer its reference policy

The five reference callers filter the coarse predicate's result differently;
this assigns each Hexwaste consumer the policy its counterpart applies and
removes the LegacyCollapsed placeholder from the ones that are settled."
```

---

### Task 6: Re-record what moved, deliberately

Only if Tasks 3-5 moved fixtures. If nothing moved, skip to Task 7 and say so.

**Files:**
- Modify: fixtures under `tests/golden-*/` — **only** those explained in Tasks 3-5.

- [ ] **Step 1: Re-record only the suites with explained movements**

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
./scripts/<suite>-golden.sh record
git status --short tests/
```

- [ ] **Step 2: Review every changed fixture line against its explanation**

```bash
cd /home/eko/dev/FPOC
git diff tests/
```

Each changed line must correspond to an explanation from Tasks 3-5. **A changed line with no
explanation is a stop condition** — revert the re-record and report it.

- [ ] **Step 3: Verify and commit**

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
for s in combat quest endgame opening encounter; do echo "=== $s ==="; ./scripts/$s-golden.sh check 2>&1 | tail -1; done
./scripts/census-sweep.sh check 2>&1 | tail -1
git add tests/
git commit -m "test(golden): re-record the fixtures the shoot-blocking port moved"
```

The commit body must list every changed fixture with the one-sentence explanation of why it moved.

---

### Task 7: Settle the two unmapped consumers, and reconcile the docs

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` (explosion line-of-sight)
- Modify: `src/Hexwaste.Viewer/ViewerGame.Rendering.cs` (combat outline)
- Modify: `docs/BACKLOG.md`

- [ ] **Step 1: Identify each one's reference counterpart**

The explosion line-of-sight consumer and the combat-outline consumer have no counterpart assigned.
Find what the reference does for each — search `reference/fallout2-ce/src/` for the explosion victim
walk and for the outline/highlight path. **If a consumer has no reference counterpart at all**,
because it is a Hexwaste-side feature, say so, choose a policy, and justify the choice in a comment.
Do not let it keep `LegacyCollapsed` by default without a stated reason.

- [ ] **Step 2: Remove `LegacyCollapsed` entirely**

Once every consumer has a real policy, delete the placeholder so nothing can silently regress onto
the collapsed behaviour.

- [ ] **Step 3: Run everything**

```bash
cd /home/eko/dev/FPOC
dotnet test 2>&1 | tail -2
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
for s in combat quest endgame opening encounter; do echo "=== $s ==="; ./scripts/$s-golden.sh check 2>&1 | tail -1; done
./scripts/census-sweep.sh check 2>&1 | tail -1
```

- [ ] **Step 4: Reconcile `docs/BACKLOG.md`**

Rewrite F33 to record what was actually found, not what was predicted: that this was a two-stage
design collapsed into one, that the "48%" figure is a property of the coarse predicate rather than of
what any consumer sees, the composed-population figure for the refusal path, the two divergences the
old entry never recorded, and the outcome of Task 1's measurement.

**F25 is blocked behind F33.** If this work settles the predicate, say whether F25 is now unblocked
and why. If it does not, say that too.

Re-derive every line citation from the tree after the last code edit — this branch moves lines in
`CombatEngine.cs`, `LineOfFire.cs`, `ViewerGame.CombatHost.cs` and `ViewerGame.cs`, so citations into
those files elsewhere in the docs may be stale.

- [ ] **Step 5: Commit**

```bash
cd /home/eko/dev/FPOC
git add -A src/ docs/
git commit -m "docs: reconcile F33 with what the two-stage port actually established"
```

---

## Verification Summary

| Task | How it is proven |
|---|---|
| 1 | Probe added; hermetic suite unchanged; the pre-registered outcome is named |
| 2 | 1022 hermetic tests; **all six golden suites byte-identical, nothing re-recorded** |
| 3 | Every moved fixture shown to have a multihex object on its line |
| 4 | Movement explained, or explicitly reported as none |
| 5 | Each policy group's movement attributed to that group; `denbus2-burst-collateral` matched against Task 1's registered outcomes |
| 6 | Every re-recorded line has an explanation; unexplained lines revert |
| 7 | No consumer left on the placeholder; F25's status stated either way |

## What this plan deliberately does not do

- **No SFALL behaviour.** The line-of-fire hit-chance extension at `combat.cc:5906` is out of scope;
  the target is vanilla at `e97087b`.
- **No change to the `+1 MULTIHEX` crowd bump** (`combat.cc:5921`), which `LineOfFire`'s doc comment
  records as deliberately unported. It is a to-hit term, not part of the predicate.
- **No change to `_obj_blocking_at`**, the movement-blocking sibling with different callers.
- **No unblocking of F25 by assumption.** Task 7 states its status based on what was established.
