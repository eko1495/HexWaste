# Rating-Gated Retaliation (re-record tier, sub-project 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore `_combatai_check_retaliation`'s rating gate in `RegisterHit`, prove it with hermetic tests, and deliberately re-record the single golden fixture it moves.

**Architecture:** A two-line change in `CombatEngine.RegisterHit` plus its doc comment. All proof lives in unit tests driven through the existing `FakeCombatHost`; the re-recorded fixture is a record of a consequence, never the evidence.

**Tech Stack:** C# / .NET (net10.0), xUnit, MonoGame DesktopGL (viewer only).

**Spec:** `docs/superpowers/specs/2026-08-12-retaliation-rerecord-design.md`

## Global Constraints

- **Port, never guess.** The change carries a comment naming the reference source (`// ported from fallout2-ce src/combat_ai.cc _combatai_check_retaliation (:3484)`). If a detail cannot be confirmed in `reference/fallout2-ce`, stop and ask.
- **The byte-identical contract is LIFTED for this item only.** Exactly one fixture — `tests/golden-encounter/brawl-watch.txt` — is expected to move. **Any other fixture moving is a STOP-AND-REPORT**, not something to re-record.
- **A re-recorded fixture proves nothing.** Every test written here must be confirmed to FAIL against the pre-change code, except the explicitly-labelled preservation guard in Step 5.
- **If the fixture delta cannot be traced** to specific retargeting decisions, do NOT re-record — report it and the item returns to deferred.
- No game assets may enter the repository.
- Golden scripts need a real display and game data (`FALLOUT2_DIR`, default `./game-data`). **Never run two golden scripts concurrently and never background one** — they drive a real graphics window and contend for the display. `quest-golden.sh` and `encounter-golden.sh` take a long time; run each in the foreground and wait.
- Conventional commit ending with `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.

## File Structure

| File | Responsibility |
|---|---|
| `src/Hexwaste.Formats/Combat/CombatEngine.cs` | Modify `RegisterHit` (~:1632-1644): restore the rating gate, rewrite the doc comment, return the method to an instance method. |
| `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs` | Add three tests through `FakeCombatHost`. |
| `tests/golden-encounter/brawl-watch.txt` | Re-recorded — the only fixture permitted to change. |
| `docs/BACKLOG.md` | Move retaliation from the re-record tier into shipped. |

Delivered as **one task** — the change is two lines plus its verification.

---

### Task 1: Restore the rating gate, prove it, re-record the one fixture

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` (`RegisterHit`, ~:1632-1644)
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`
- Re-record: `tests/golden-encounter/brawl-watch.txt`
- Modify: `docs/BACKLOG.md`

**Interfaces:**
- Consumes: `CombatEngine.Rating(MapObject?)` (private, already present) and `AiRating.Score` — both shipped in the previous batch, unchanged here.
- Produces: nothing new. `RegisterHit` stays private; its signature changes only by dropping `static`.

**Existing helpers you will use** (already in `CombatEngineTests.cs`):
- `NewCritter(int tile, int hp, int ap = 10, int seq = 1, int exp = 0, int betterCrit = 0, int meleeDmg = 0, int skill = 0, int endurance = 0, int dr = 0, int killType = 0, int perception = 5)` — returns `(MapObject Obj, CritterProtoStats Proto)`.
- `FakeCombatHost.SetDude(...)`, `.AddCritter(...)`, `engine.BeginScriptAggro(attacker, target)`, `engine.Step()`, `MinRng`.
- `Step(int tile, int dir, int count)` — the tile-walk helper used as `Step(20100, 0, 5)`.

**Why `meleeDmg` is the rating dial:** `Rating` = `AiRating.Score(state.MeleeDamage, state.ArmorClass, equippedWeaponMaxDamage)` = `max(meleeDamage, weaponMax) + armorClass`. `NewCritter` has no armor-class parameter (AC is 0 for all test critters) and these critters carry no weapon, so `meleeDmg` alone sets the rating.

- [ ] **Step 1: Write the failing retargeting test**

Add to `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`:

```csharp
    [Fact]
    public void AHigherRatedAttackerKeepsWhoHitMeAgainstALaterWeakerHit()
    {
        // ported from fallout2-ce src/combat_ai.cc _combatai_check_retaliation (:3484): whoHitMe is only
        // REPLACED when the new attacker's _combatai_rating is strictly greater, so a critter keeps
        // hunting the scarier enemy instead of whoever last scratched it. Pre-change (unconditional
        // last-hitter-wins) the dude's whoHitMe would end up as the WEAK attacker that struck last.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 200, ap: 10));
        // seq orders the turn: the STRONG one acts first, the WEAK one strikes last.
        MapObject strong = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, seq: 20, meleeDmg: 9, skill: 100));
        MapObject weak = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 3), hp: 30, ap: 10, seq: 1, meleeDmg: 1, skill: 100));

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(strong, dude);
        for (int i = 0; i < 12 && !ReferenceEquals(dude.WhoHitMe, weak); i++)
            engine.Step();

        Assert.NotNull(dude.WhoHitMe);
        Assert.Same(strong, dude.WhoHitMe); // the weak last-hitter must NOT have stolen it
    }
```

**If both attackers do not land a hit within the loop**, adjust `hp`/`ap`/`skill`/adjacency so they do — a test where the weak critter never strikes would pass for the wrong reason. Step 2 is the check that catches exactly that.

- [ ] **Step 2: Run it and confirm it fails against the pre-change code**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~AHigherRatedAttackerKeepsWhoHitMe`
Expected: **FAIL** — `Assert.Same` reports the weak critter, because `RegisterHit` is still unconditional last-hitter-wins.

If it PASSES here, the test is not exercising what it claims (most likely the weak critter never actually hit). Fix the setup until it genuinely fails, and say so in your report. Do not proceed on a test that passes before the change.

- [ ] **Step 3: Write the failing equal-rating test**

```csharp
    [Fact]
    public void AnEqualRatedAttackerDoesNotStealWhoHitMe()
    {
        // The boundary the reference's STRICT `>` defines (combat_ai.cc:3488): an equally-rated attacker
        // leaves the existing whoHitMe alone. Pre-change this returned the later attacker.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 200, ap: 10));
        MapObject first = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, seq: 20, meleeDmg: 5, skill: 100));
        MapObject second = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 3), hp: 30, ap: 10, seq: 1, meleeDmg: 5, skill: 100));

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(first, dude);
        for (int i = 0; i < 12 && !ReferenceEquals(dude.WhoHitMe, second); i++)
            engine.Step();

        Assert.Same(first, dude.WhoHitMe); // equal rating → keep the incumbent
    }
```

- [ ] **Step 4: Run it and confirm it fails**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~AnEqualRatedAttackerDoesNotSteal`
Expected: **FAIL** — reports `second`. Same rule as Step 2: if it passes pre-change, the setup is wrong.

- [ ] **Step 5: Write the gate-preservation guard**

**This one is expected to pass BEFORE and AFTER — it is a preservation guard, not a regression test.** Its job is to prove that restoring the gate did not disturb the two pre-existing filters. Label it as such so no future reader mistakes it for proof of the change.

```csharp
    [Fact]
    public void SameTeamAndDeadTargetHitsStillNeverRegisterWhoHitMe()
    {
        // PRESERVATION GUARD (passes both before and after the rating gate): RegisterHit's team gate and
        // dead-target gate are unchanged by the gate restore. Hexwaste keeps these; the reference's
        // equivalents live in _combatai_check_retaliation's callers (combat.cc:4717/4745).
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 200, ap: 10));
        MapObject enemy = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, meleeDmg: 5, skill: 100));

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        // The enemy hit the dude, never itself, and never a same-team critter.
        Assert.Null(enemy.WhoHitMe);
        Assert.True(dude.WhoHitMe is null || ReferenceEquals(dude.WhoHitMe, enemy));
    }
```

- [ ] **Step 6: Restore the rating gate**

In `src/Hexwaste.Formats/Combat/CombatEngine.cs`, replace the `RegisterHit` doc comment and method (~:1632-1644) with:

```csharp
    /// <summary>Record who last hit a critter (whoHitMe) — ported from fallout2-ce combat.cc:4707 +
    /// combat_ai.cc _combatai_check_retaliation (:3484): an unset whoHitMe is taken unconditionally, but
    /// an existing one is REPLACED only by a strictly higher-rated attacker (_combatai_rating), so a
    /// critter keeps hunting the scarier enemy rather than whoever last scratched it. An equally-rated
    /// attacker does not steal aggro. Hexwaste's team gate is retained — the engine's equivalent gate
    /// lives in the callers. This deliberately re-records the brawl-watch encounter fixture (see
    /// docs/superpowers/specs/2026-08-12-retaliation-rerecord-design.md).</summary>
    private void RegisterHit(MapObject target, MapObject attacker)
    {
        if (target.IsDead || attacker == target || attacker.Team == target.Team)
            return;
        if (target.WhoHitMe is { } current && Rating(attacker) <= Rating(current))
            return; // combat_ai.cc:3488 — only a STRICTLY greater rating retargets
        target.WhoHitMe = attacker;
    }
```

Note the method loses `static` (it now calls the instance method `Rating`). This intentionally reverses final-review Minor 6 from the previous batch, which only applied while the gate was absent.

- [ ] **Step 7: Run the full unit suite**

Run: `dotnet build && dotnet test tests/Hexwaste.Formats.Tests`
Expected: build clean; all tests PASS, including the three added here. Record the pass/fail/skip counts.

- [ ] **Step 8: Measure the fixture blast radius BEFORE recording anything**

Run each in the FOREGROUND, one at a time, waiting for each to finish:

```bash
scripts/combat-golden.sh check
scripts/quest-golden.sh check
scripts/encounter-golden.sh check
```

Expected: `combat-golden` 16/16 PASS, `quest-golden` 39/39 PASS, and `encounter-golden` reporting **exactly one** REGRESSION — `brawl-watch`, changing `rounds=11 → 9`, `survivors=1 → 2`, `winTeam=[2] → [1]`, with `dudeHp=30` unchanged.

**If any fixture other than `brawl-watch` fails, STOP and report.** The blast radius was measured as exactly one fixture; a second means the change does more than the design believes, and the design is wrong — not the fixture. Do not re-record in that case.

- [ ] **Step 9: Construct the justification for the delta**

Before recording, explain the three changed values. Re-run the brawl scenario directly to read its transcript:

```bash
DISPLAY=:0 FALLOUT2_DIR=/home/eko/dev/FPOC/game-data dotnet run --project src/Hexwaste.Viewer -c Debug -- \
  --game-dir game-data --no-audio --brawl-watch desert1.map ARRO_War_Party 2 ARRO_Cannibals 2 --rng-seed 3
```

Compare that transcript against the committed `tests/golden-encounter/brawl-watch.txt` and identify: which critter changed target, on which round, and which rating comparison caused it — then why the resulting `rounds`/`survivors`/`winTeam` follow from that. The ratings are `max(melee damage, wielded weapon max damage) + armor class` per combatant.

**If you cannot construct that trace from the transcripts, STOP — do not re-record.** Report what you tried and what remained unexplained; the item returns to deferred rather than being re-recorded on the strength of "the port looks faithful."

- [ ] **Step 10: Re-record, and verify exactly one fixture changed**

Run in the foreground: `scripts/encounter-golden.sh record`

This rewrites all 188 fixtures, so the guard is the diff:

```bash
git status --short tests/golden-encounter/
```

Expected: **exactly one** modified file — `tests/golden-encounter/brawl-watch.txt`. Confirm its diff shows only the three expected value changes.

**If more than one file is modified, STOP and report** — restore with `git checkout -- tests/golden-encounter/` and do not commit. More than one moved fixture contradicts Step 8's measurement and means something is non-deterministic or broader than believed.

- [ ] **Step 11: Re-run every suite green**

Foreground, one at a time:

```bash
scripts/combat-golden.sh check
scripts/quest-golden.sh check
scripts/encounter-golden.sh check
```

Expected: all PASS — 16/16, 39/39, 188/188.

- [ ] **Step 12: Update the backlog**

In `docs/BACKLOG.md`, move rating-gated retaliation out of the re-record tier into the shipped list. State that its `brawl-watch` fixture was **deliberately re-recorded** (cite the commit SHA once you have it, or reference this plan), so a future reader knows the baseline moved by intent rather than drift. The re-record tier should now list four remaining items: ring-spiral explosion damage, `_combat_safety_invalidate_weapon` + `_cai_retargetTileFromFriendlyFire`, `_ai_danger_source` + perception-based `PruneEscapedHostiles`, and the explosive `×(extras+1)` best-weapon factor.

- [ ] **Step 13: Commit**

The commit body must carry the Step 9 justification — this is the permanent record of why the baseline changed.

```bash
git add src/Hexwaste.Formats/Combat/CombatEngine.cs \
        tests/Hexwaste.Formats.Tests/CombatEngineTests.cs \
        tests/golden-encounter/brawl-watch.txt docs/BACKLOG.md
git commit -m "feat: rating-gated retaliation (_combatai_check_retaliation) + brawl-watch re-record

<the Step 9 trace: which critter retargeted, on which round, from which rating
comparison, and how rounds 11->9 / survivors 1->2 / winTeam [2]->[1] follow>

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Done when

The gate restored; the two regression tests confirmed failing pre-change and passing after; the preservation guard green; exactly one fixture re-recorded with its delta traced in the commit body; all four suites green; `docs/BACKLOG.md` showing four remaining re-record-tier items.

**Or:** the item is honestly returned to deferred because the delta could not be traced (Step 9) or the blast radius exceeded one fixture (Step 8 / Step 10) — with nothing re-recorded and the finding reported. That is a legitimate outcome of this plan, not a failure of it.
