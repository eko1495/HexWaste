# `max_dist` flee gate and engine maneuver flags (F1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port `_ai_run_away`'s `max_dist` predicate and its `FLEEING` / `DISENGAGING` maneuver
writes, so an engine-initiated flight can terminate instead of repeating every turn forever.

**Architecture:** One gate at the top of `CombatEngine.TryFlee`, reading the AI packet the engine
already fetches. No new type, no new host seam, no signature change. This is the first item in this
tier expected to move a committed fixture.

**Tech Stack:** C# / .NET 10 (`net10.0`), xUnit. Reference: `reference/fallout2-ce` at `e97087b`
(gitignored clone).

## Global Constraints

- **Never copy, embed, or commit game assets.** `.gitignore` excludes `*.dat`, `*.map`, `*.frm`,
  `*.pal`, `game-data/` — keep it that way.
- **Port from `reference/fallout2-ce`, never guess.** Every behavioural change carries a comment
  citing its source file and function. If a detail cannot be confirmed, **stop and ask**.
- **`alexbatalov e97087b` is authoritative for vanilla.** `community/main` is a bug-fix candidate
  source only; cite ported fork fixes as `(community fix #NNN)`.
- **VERIFY EVERY LINE NUMBER YOU CITE** by opening the reference. Three citations by this plan's
  author were proved wrong on the immediately preceding branch. Report and correct any that are off.
- **No new dependencies.** `src/Hexwaste.Formats` stays free of MonoGame references.
- **Golden-net discipline:** the scripts run the *prebuilt* binary. Never run two nets concurrently,
  never background one, **never build while one is running**. The scripts print no fixture count —
  report exactly what they print, never an inferred count.
- Golden nets need a real display and game data (`FALLOUT2_DIR`, default `./game-data`).
- Every regression test must be **confirmed to fail against the pre-change code**.

---

## THE TRAP IN THIS ITEM — read before writing any code

The comparison is **`<`**, matching `e97087b` (`combat_ai.cc:1183`):

```c
if (distance < ai->max_dist) { …flee… } else { maneuver |= CRITTER_MANEUVER_DISENGAGING; }
```

The maintained fork's PR #675 flips this to `<=`. **We classified that hunk as ungrounded and
rejected it** (no disassembly, no issue, no symptom). Porting the gate with the fork's operator would
import the exact change we declined. Task 1 Step 2 pins the boundary with a test for this reason.

---

## File Structure

| File | Responsibility | Tasks |
|---|---|---|
| `src/Hexwaste.Formats/Combat/CombatEngine.cs` | `TryFlee` — the gate and the two maneuver writes | 1 |
| `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs` | new hermetic tests + repair of existing flee tests | 1 |
| `tests/golden-combat/denbus2-fight-flee.txt` | the expected deliberate re-record | 1 |
| `docs/BACKLOG.md` | F1 → shipped; new entry for the out-of-scope second setter | 2 |

---

## Reference facts every task depends on

Verified against `e97087b` on 2026-08-15.

- `_ai_run_away(a1, a2)` (`combat_ai.cc:1173`), `a2` defaulting to `gDude` when null, computes
  `distance = objectGetDistanceBetween(a1, a2)` and branches on `distance < ai->max_dist` (`:1183`).
  The true branch sets `CRITTER_MANUEVER_FLEEING` (`:1184`) then runs; the `else` sets
  `CRITTER_MANEUVER_DISENGAGING` (`:1216`) and does **nothing else** — no movement, no AP spend.
- Flags: `CRITTER_MANEUVER_ENGAGING = 0x01`, `DISENGAGING = 0x02`, `MANUEVER_FLEEING = 0x04`
  (`obj_types.h:120-123`).
- `DISENGAGING` is what terminates a fight: `_combatai_want_to_fight` returns false on it (`:3195`),
  `_combatai_want_to_stop` returns true on it (`:3215`). `FLEEING` does the same at `:3199` / `:3223`.

### Hexwaste facts

- `CombatEngine` already defines `ManeuverEngaging = 0x01, ManeuverDisengaging = 0x02,
  ManeuverFleeing = 0x04` (`:1999`) and already **consumes** all of them: want-to-join (`:2044-2046`),
  the turn-order filter (`:2134`), `WantsToStopFighting` (`:2266`), flee-continuation (`:2889`).
- The engine never **sets** any of them; the only engine write is `critter.Maneuver = 0` (`:2065`).
  Scripts do set them (`ScriptHost.cs:1805`, `:2113`, `:2282`).
- `_host.GetAiPacket(critter)` returns `AiPacket?` — already called at `:147`, `:2784`, `:2881`.
- `AiPacket`'s 5th positional parameter is `MaxDist` (`AiPackets.cs:18`).
- `TryFlee` returning `false` means "no action taken": `TryEnemyAction` returns it straight through,
  and the caller (`:2346`, `:2425`) ends that critter's turn — it does **not** fall through to an
  attack. The disengage branch is therefore correctly modelled as returning `false`.
- **All 41 `new AiPacket(...)` constructions in the test file pass `MaxDist: 0`.** With the gate in
  place, `distance < 0` is never true, so every one of them disengages. See Task 1 Step 4.

---

## Task 1: The gate, the maneuver writes, and the re-record

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` — `TryFlee` (`:3098`)
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`
- Re-record (expected): `tests/golden-combat/denbus2-fight-flee.txt`

**Interfaces:**
- Consumes: `ICombatHost.GetAiPacket(MapObject) -> AiPacket?`, `AiPacket.MaxDist`,
  `HexGrid.Distance(int, int)`, the `Maneuver*` constants at `CombatEngine.cs:1999`.
- Produces: no new public surface. `TryFlee` keeps its signature
  `bool TryFlee(MapObject critter, int threatTile, ref int actorAp)`.

- [ ] **Step 1: Write the "below the threshold still flees, and is marked" test**

Add near the existing flee tests (after `WithoutTheFleeManeuverTheSameHealthyEnemyAttacks`, ~`:475`):

```csharp
    [Fact]
    public void EnemyInsideMaxDistFleesAndIsMarkedFleeing()
    {
        // ported from fallout2-ce src/combat_ai.cc _ai_run_away (:1183-1184): inside max_dist the
        // critter is marked CRITTER_MANUEVER_FLEEING and runs. Adjacent (distance 1) with max_dist 10.
        const int ManeuverFleeing = 0x04;
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 5, ap: 10));
        host.AiPackets[enemy] = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 10, 10, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Contains(host.Transcripts, t => t.StartsWith("flee:"));
        Assert.True((enemy.Maneuver & ManeuverFleeing) != 0, "the engine must mark an actual flight FLEEING");
    }
```

- [ ] **Step 2: Write the boundary test — this is the one that guards the `<`**

```csharp
    [Theory]
    [InlineData(9, true)]    // distance 9 < max_dist 10 -> flees
    [InlineData(10, false)]  // distance 10 is NOT < 10 -> disengages. The fork's PR #675 '<=' would flee here.
    public void MaxDistBoundaryDecidesFleeingVersusDisengaging(int distance, bool expectFlee)
    {
        // ported from fallout2-ce src/combat_ai.cc _ai_run_away (:1183). The comparison is '<' at our
        // pinned e97087b. The maintained fork's PR #675 flips it to '<=', a hunk we rejected as
        // ungrounded — so distance == max_dist MUST disengage. Do not "fix" this to '<='.
        const int ManeuverFleeing = 0x04, ManeuverDisengaging = 0x02;
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0, distance), hp: 5, ap: 10));
        host.AiPackets[enemy] = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 10, 10, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Equal(expectFlee, (enemy.Maneuver & ManeuverFleeing) != 0);
        Assert.Equal(!expectFlee, (enemy.Maneuver & ManeuverDisengaging) != 0);
    }
```

**Verify the geometry before trusting it:** confirm that
`HexGrid.Distance(20100, HexGrid.TileInDirection(20100, 0, n)) == n` for `n = 9` and `n = 10`. Hex
grids do not always make that identity hold at the map edge or across row parity. If it does not hold,
pick tiles that give the exact distances you need and say so in a comment — the test's whole value is
that the distances are exactly 9 and 10.

- [ ] **Step 3: Write the "disengaging is inert and terminates the fight" tests**

```csharp
    [Fact]
    public void DisengagingEnemyNeitherMovesNorAttacks()
    {
        // combat_ai.cc:1215-1217 — the else branch sets the flag and does NOTHING else: no movement,
        // no AP spend, and (because TryEnemyAction returns false) no attack either.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0, 10), hp: 5, ap: 10));
        host.AiPackets[enemy] = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 10, 10, "", "");
        int startTile = enemy.HexTile;
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("flee:"));
        Assert.Equal(startTile, enemy.HexTile);   // did not move
        Assert.Equal(30, dude.CurrentHp);         // did not attack
    }

    [Fact]
    public void ADisengagedHostileNoLongerKeepsTheFightOpen()
    {
        // The POINT of the item, end-to-end: DISENGAGING makes _combatai_want_to_stop return true
        // (combat_ai.cc:3215), which is what lets a fight terminate. Asserting the flag alone would
        // pass even if nothing consumed it, so drive it through the engine's own exit path.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0, 10), hp: 5, ap: 10));
        host.AiPackets[enemy] = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 10, 10, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.True(engine.CanEndCombat(), "a disengaged sole hostile must not block leaving combat");
    }
```

**`CanEndCombat()` is a placeholder name.** Find the real public predicate that `WantsToStopFighting`
(`:2266`) feeds — the exit gate at `:2252` is `if (_hostiles.Any(h => !WantsToStopFighting(h)))`. Use
whatever public member exposes that decision. If none is public, assert through the observable
behaviour instead (e.g. the engine's phase after the player attempts to end combat) rather than
making a private member public just for the test.

- [ ] **Step 4: Write the null-packet inertness test**

```csharp
    [Fact]
    public void ACritterWithNoAiPacketStillFlees()
    {
        // Hexwaste-only state: the reference always has a packet, so there is no vanilla behaviour to
        // port for a null one. Keep the pre-gate behaviour rather than inventing a default max_dist —
        // this is what keeps packet-less fixture critters and the ally flee path inert.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        enemy.Maneuver |= 0x04; // script-set FLEEING, the path that does not need a packet
        // deliberately NO host.AiPackets[enemy] entry
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Contains(host.Transcripts, t => t.StartsWith("flee:"));
    }
```

- [ ] **Step 5: Run the new tests to verify they FAIL against the pre-change code**

Run:
```bash
dotnet test --filter "FullyQualifiedName~MaxDistBoundary|FullyQualifiedName~EnemyInsideMaxDist|FullyQualifiedName~Disengaging|FullyQualifiedName~ADisengagedHostile"
```
Expected: the `FLEEING`-marking assertions and every disengage assertion **FAIL** (nothing sets the
flags today, and a distance-10 enemy currently flees). `ACritterWithNoAiPacketStillFlees` is a pin and
correctly passes on both sides — it is not a regression test.

If a test passes here that should not, **stop and report** — it means it is not reaching `TryFlee`.

- [ ] **Step 6: Implement the gate**

In `TryFlee` (`CombatEngine.cs:3098`), insert immediately after the `if (actorAp < 1) return false;`
guard, before `int fromTile = critter.HexTile;`:

```csharp
        // ported from fallout2-ce src/combat_ai.cc _ai_run_away (:1183-1217): a critter already at or
        // beyond max_dist from its threat does NOT run — it sets CRITTER_MANEUVER_DISENGAGING (:1216)
        // and takes no movement and no AP, which is exactly what lets a flight terminate
        // (_combatai_want_to_stop returns true on the flag, :3215). Inside the threshold it is marked
        // CRITTER_MANUEVER_FLEEING (:1184) and runs. Before this gate the engine set NEITHER flag, so
        // every consumer of them was starved on an engine-initiated flight and a fleeing critter
        // re-fled every turn forever (visible in denbus2-fight-flee as the same flee: line repeating).
        // The comparison is '<', matching e97087b. The maintained fork's PR #675 flips it to '<=';
        // that hunk was rejected as ungrounded, so '<' is deliberate — do not "correct" it.
        // A null AI packet is a Hexwaste-only state (the reference always has one): keep the pre-gate
        // behaviour and flee, rather than inventing a default max_dist.
        AiPacket? ai = _host.GetAiPacket(critter);
        if (ai is not null && HexGrid.Distance(critter.HexTile, threatTile) >= ai.MaxDist)
        {
            critter.Maneuver |= ManeuverDisengaging;
            _host.Transcript($"disengage: {_host.ObjectName(critter)}@{critter.HexTile}");
            return false; // the reference's empty else — no move, no AP, and the caller ends the turn
        }
        critter.Maneuver |= ManeuverFleeing;
```

**Note the new `disengage:` transcript line.** It is deliberate: it makes the re-recorded fixture
delta legible and traceable, which the justification step depends on. It also means the fixture change
includes added lines, not only removed ones.

- [ ] **Step 7: Run the new tests to verify they pass**

Run the same filter as Step 5. Expected: all pass.

- [ ] **Step 8: Repair the existing flee tests — deliberately, not by weakening the gate**

Run: `dotnet test`

Expect failures in the pre-existing tests that assert a flee happens, because **all 41 `new AiPacket`
constructions in the file pass `MaxDist: 0`** and `distance < 0` is never true. The known flee-asserting
tests are at roughly `:432`, `:454`, `:490`, `:1282` (and the ally path at `:2764`, which should be
unaffected if companions have no packet — verify rather than assume).

For each failure, set that packet's `MaxDist` to a realistic value — **10**, matching the `Guard`
packet already pinned in `AiPacketTests.cs:40` — and add a short comment on the first one you fix
explaining that `MaxDist: 0` was a placeholder for a field that was dead until this change.

**Do NOT** fix these by changing the comparison, by defaulting `MaxDist` when zero, or by skipping
the gate when `MaxDist == 0`. A zero `max_dist` meaning "never flee" is a real, faithful outcome of
the reference's own arithmetic; the tests were simply written against a field nothing read. If you
believe a production code path genuinely depends on `MaxDist == 0` behaving as "always flee", **stop
and report** rather than encoding it.

Then re-run `dotnet test` to green.

- [ ] **Step 9: Measure the fixture blast radius BEFORE recording**

Run: `scripts/combat-golden.sh check`

Expected: **`denbus2-fight-flee` fails; everything else passes.**

Stop conditions:
- **Nothing fails** → the gate is not reaching any fixture. Do not record. Report it: the spec
  predicted this fixture moves, and a no-op means the prediction is wrong (this already happened once
  on the preceding branch, with F11).
- **Anything other than `denbus2-fight-flee` fails** → report the full list before going further.
  A second mover is plausible here but was not predicted, so it must be understood, not absorbed.

- [ ] **Step 10: Construct the justification BEFORE recording**

Diff the failing fixture against the current output and write down:
- which critter disengaged, at what distance from its threat, on which round;
- that the repeated identical `flee:` lines (the committed fixture has
  `flee: Cute Slave@11272 -> 10480` at lines 25, 39, 57, 75) are reduced or gone;
- whether the fight now ends earlier, and why `distance < max_dist` produces exactly that.

**If that trace cannot be constructed from the transcript, do not record.** Revert and report; F1
returns to deferred. An unexplained delta accepted because "the port looks faithful" is how a bug gets
laundered into the baseline.

- [ ] **Step 11: Record, then verify exactly the predicted fixtures moved**

Run:
```bash
scripts/combat-golden.sh record
git status --short tests/golden-combat/
```
`record` rewrites every fixture, so `git status` must show **only the fixtures your Step 9 measurement
predicted**. Any additional modified file is a stop condition.

Then re-run `scripts/combat-golden.sh check` — expected: ALL PASS.

- [ ] **Step 12: Commit**

```bash
git add src/Hexwaste.Formats/Combat/CombatEngine.cs \
        tests/Hexwaste.Formats.Tests/CombatEngineTests.cs \
        tests/golden-combat/
git commit -m "fix: port _ai_run_away's max_dist gate and the engine's maneuver flags (F1)

<the Step 10 trace goes in this body: which critter disengaged, at what
distance, on which round, and how the repeated flee: lines changed>

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Backlog reconciliation and the full nets

**Files:**
- Modify: `docs/BACKLOG.md`

- [ ] **Step 1: Move F1 to shipped**

Record the commit SHA from Task 1, what was ported (`<` gate, `FLEEING` on the flee path,
`DISENGAGING` on the else), and that `tests/golden-combat/denbus2-fight-flee.txt` was **deliberately
re-recorded**, listing every fixture that actually moved.

- [ ] **Step 2: Correct F1's framing while you are there**

The entry says Hexwaste "has no distance predicate at all". Replace with the sharper finding: the
maneuver flags, all four consumers (want-to-join, turn-order filter, `WantsToStopFighting`,
flee-continuation) and the script setters already existed and were correct — the gap was that the
**engine's own AI never set the flags**, so an engine-initiated flight could never be concluded.

- [ ] **Step 3: Add the new entry for the out-of-scope second setter**

The reference sets `DISENGAGING` in a second place — `_combat_ai`'s tail (`combat_ai.cc:3098-3112`):
when the target is alive, AP remains and `distance > max_dist`, it backs away from a friendly corpse
(`aiInfoGetFriendlyDead` + `_ai_move_away`) or, failing that, tries `_ai_find_friend(a1, PE * 2, 5)`
and sets `DISENGAGING` only if no friend is found. **Neither `aiInfoGetFriendlyDead`/`aiInfoSetFriendlyDead`
nor `_ai_find_friend` exists anywhere in this repo** — a repo-wide search finds no equivalent — so
this needs friendly-corpse tracking and a friend search built first. Mark it re-record tier and note
that it makes disengagement *harder* (a critter with a nearby friend keeps fighting), so porting it
will move fixtures again.

Follow the numbering and style of the neighbouring Tier F entries.

- [ ] **Step 4: Run the remaining nets**

**One at a time. Nothing else may build while these run.**

```bash
scripts/quest-golden.sh check
scripts/encounter-golden.sh check
```

The quest suite is expected **ALL PASS** — nothing there should reach an engine-initiated flee. The
encounter suite contains `brawl-watch`, which involves fleeing critters, so it is a **plausible
mover**. If it moves, do not record it here: stop and report, with the delta, so the movement is
justified on the same terms as Task 1's before anything is rewritten.

- [ ] **Step 5: Commit**

```bash
git add docs/BACKLOG.md
git commit -m "docs: reconcile F1 as shipped and record the second DISENGAGING setter

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:** the gate → Task 1 Steps 6; the five proof obligations → Steps 1-4 (flee+marked,
  boundary both sides, disengage inert, termination end-to-end, null packet); the fixture protocol
  (measure → justify → record → verify) → Steps 9-11; docs and the out-of-scope entry → Task 2.
- **Known soft spots, both flagged inline rather than hidden:** the hex geometry assumption in Step 2
  (`TileInDirection(t, 0, n)` being exactly `n` away) and the placeholder predicate name in Step 3
  (`CanEndCombat()`), which the implementer must resolve against the real API.
- **The largest risk is Step 8** — an implementer under pressure to make 41 packets' worth of tests
  pass may reach for `<=` or a `MaxDist == 0` special case. Step 8 names both and forbids both, and
  Step 2's boundary test fails loudly if either is attempted.
