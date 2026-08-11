# fo2ce Combat-AI Fidelity (byte-identical batch) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port six `combat_ai.cc` residuals from `reference/fallout2-ce` into Hexwaste without moving a single committed golden fixture. (The spec listed seven; companion armor auto-equip was cut during planning — see Task 5.)

**Architecture:** Pure decision logic lands in `src/Hexwaste.Formats/Combat/` (no MonoGame deps, unit-testable); anything needing world data reaches the viewer through a new `ICombatHost` seam whose **default implementation reproduces today's behavior**, so the fake test host and the fixtures stay inert by construction. The viewer implements the real seams in `ViewerGame.CombatHost.cs`.

**Tech Stack:** C# / .NET (net10.0), xUnit, MonoGame DesktopGL (viewer only).

**Spec:** `docs/superpowers/specs/2026-08-11-fo2ce-combat-ai-fidelity-design.md`

## Global Constraints

- **Port, never guess.** Every behavioral change carries a comment naming the reference source, e.g. `// ported from fallout2-ce src/combat_ai.cc _combatai_rating (:3449)`. If a detail cannot be confirmed in `reference/fallout2-ce`, stop and ask.
- **No game assets** enter the repo. Fixtures are transcripts only.
- **Goldens are the contract.** Every task ends by running `scripts/combat-golden.sh check`. A fixture that moves means the item is **escalated to the deferred re-record tier** — never "fixed" by weakening the port to preserve the fixture.
- **New `ICombatHost` members must have a default implementation** returning the empty/false value that reproduces current behavior. The combat unit tests use a fake host that implements only what it needs.
- Golden scripts need a real display and game data (`FALLOUT2_DIR`, default `./game-data`). `dotnet test` does not.
- Conventional commits; end each message with `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.
- Out of scope, do not implement: ring-spiral explosions, `_combat_safety_invalidate_weapon`, `_ai_danger_source` / perception-based `PruneEscapedHostiles`, the `_ai_best_weapon` explosive `×(extras+1)` factor.

## File Structure

| File | Responsibility |
|---|---|
| `src/Hexwaste.Formats/Combat/AiRating.cs` | **Create.** Pure `_combatai_rating` scoring. |
| `src/Hexwaste.Formats/Combat/CombatEngine.cs` | Modify. `Rating()` wrapper, `RegisterHit` retaliation rule, ally target ranking, `AiSwitchWeapon` ammo + ground-pickup wiring, `_aiLastItem` memory, the two extra switch triggers. |
| `src/Hexwaste.Formats/Combat/CompanionAi.cs` | Modify. `PickTarget` ranks by rating, with vanilla's inverted comparators. |
| `src/Hexwaste.Formats/Combat/AiBestWeapon.cs` | Modify. Add the weapon-perk ×2 damage-score helper. |
| `src/Hexwaste.Formats/Combat/ICombatHost.cs` | Modify. Three new defaulted seams. |
| `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs` | Modify. Real seam implementations (ammo calibers, ground items, retrieve) + NPC drug scheduling. |
| `src/Hexwaste.Viewer/ViewerGame.Chemistry.cs` | Modify. Owner-aware pending drug queue. |
| `src/Hexwaste.Viewer/ViewerGame.cs` | Modify. `_pendingDrugEvents` shape, drop the combat-idle `_npcDrugBonus.Clear()`, new `StartupAction`. |
| `src/Hexwaste.Viewer/ViewerGame.Harness.cs` | Modify. `--ai-pickup-probe` handler. |
| `src/Hexwaste.Viewer/Program.cs` | Modify. `--ai-pickup-probe` arg parse. |
| `tests/Hexwaste.Formats.Tests/AiRatingTests.cs` | **Create.** |
| `tests/Hexwaste.Formats.Tests/CompanionAiTests.cs` | Modify. Rating-based target tests. |
| `tests/Hexwaste.Formats.Tests/AiBestWeaponTests.cs` | Modify. Perk ×2 test. |

---

### Task 1: `_combatai_rating` and its two in-scope consumers

**Files:**
- Create: `src/Hexwaste.Formats/Combat/AiRating.cs`
- Create: `tests/Hexwaste.Formats.Tests/AiRatingTests.cs`
- Modify: `src/Hexwaste.Formats/Combat/CompanionAi.cs:108-131` (`PickTarget` + `Better`)
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs:1611-1618` (`RegisterHit`), `:2818-2821` (ally ranking)
- Test: `tests/Hexwaste.Formats.Tests/AiRatingTests.cs`, `tests/Hexwaste.Formats.Tests/CompanionAiTests.cs`

**Interfaces:**
- Produces: `AiRating.Score(int meleeDamage, int armorClass, params int[] weaponMaxDamages) -> int`; `CombatEngine.Rating(MapObject?) -> int` (private); `CompanionAi.PickTarget(AttackWho, IReadOnlyList<(int Rating, int Distance, bool HitMe)>) -> int` (the tuple's first field is **renamed from `Hp` to `Rating`** — this is a breaking signature change for existing callers and tests).
- Consumes: nothing from earlier tasks.

**⚠️ The inverted-comparator quirk.** `_compare_strength` (`combat_ai.cc:1330-1350`) returns `-1` when `rating1 < rating2`, and `_ai_sort_list_strength` qsorts with it, so `targets[0]` is the **lowest**-rated critter — and `_ai_danger_source` (`:1691`) takes the first valid entry. So in vanilla, `ATTACK_WHO_STRONGEST` targets the **weakest** critter and `ATTACK_WHO_WEAKEST` targets the strongest. This looks like a bug because it is one — it is vanilla's bug, and the project directive is to port it, not correct it. Do not "fix" this; the code comment must call it out.

- [ ] **Step 1: Write the failing test for the pure score**

Create `tests/Hexwaste.Formats.Tests/AiRatingTests.cs`:

```csharp
using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>_combatai_rating (combat_ai.cc:3449): max(melee damage, best wielded weapon max
/// damage) + armor class. The dead/KO and non-critter guards live in the engine wrapper.</summary>
public class AiRatingTests
{
    [Fact]
    public void UnarmedCritterScoresMeleeDamagePlusAc()
    {
        Assert.Equal(11, AiRating.Score(meleeDamage: 8, armorClass: 3));
    }

    [Fact]
    public void WeaponMaxDamageWinsWhenHigherThanMelee()
    {
        // A 10mm pistol (max 12) on a melee-damage-5 critter with AC 4 → 12 + 4.
        Assert.Equal(16, AiRating.Score(meleeDamage: 5, armorClass: 4, 12));
    }

    [Fact]
    public void MeleeDamageWinsWhenWeaponIsWeaker()
    {
        // combat_ai.cc only replaces melee_damage when weapon max EXCEEDS it.
        Assert.Equal(13, AiRating.Score(meleeDamage: 9, armorClass: 4, 3));
    }

    [Fact]
    public void BestOfSeveralWeaponsIsUsed()
    {
        Assert.Equal(20, AiRating.Score(meleeDamage: 5, armorClass: 5, 8, 15, 2));
    }
}
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~AiRatingTests`
Expected: build error — `AiRating` does not exist.

- [ ] **Step 3: Create `AiRating`**

Create `src/Hexwaste.Formats/Combat/AiRating.cs`:

```csharp
namespace Hexwaste.Formats.Combat;

/// <summary>
/// ported from fallout2-ce src/combat_ai.cc _combatai_rating (:3449): a critter's threat rating —
/// the best of its melee damage and its wielded weapons' MAX damage, plus its armor class. Drives
/// retaliation (_combatai_check_retaliation, :3484) and the strength/weakness target comparators
/// (_compare_strength/_compare_weakness, :1330/:1366). PURE: the caller resolves the stats.
/// </summary>
public static class AiRating
{
    /// <summary>rating = max(meleeDamage, best weaponMaxDamage) + armorClass. The engine only
    /// replaces melee_damage when a weapon's max damage EXCEEDS it, so a weaker weapon is ignored.
    /// The dead/KO and non-critter → 0 guards belong to the caller (see CombatEngine.Rating).</summary>
    public static int Score(int meleeDamage, int armorClass, params int[] weaponMaxDamages)
    {
        int best = meleeDamage;
        foreach (int max in weaponMaxDamages)
            if (max > best)
                best = max;
        return best + armorClass;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~AiRatingTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Write the failing test for the inverted comparators**

In `tests/Hexwaste.Formats.Tests/CompanionAiTests.cs`, replace any existing `PickTarget` tests that use the `Hp` field with these (keep the rest of the file untouched):

```csharp
    // The candidate tuple is (Rating, Distance, HitMe) — see AiRating / CompanionAi.PickTarget.

    [Fact]
    public void StrongestPicksTheLOWESTRatedTarget()
    {
        // VANILLA QUIRK (combat_ai.cc:1330 + :1691): _compare_strength sorts ASCENDING by rating and
        // the picker takes targets[0], so "Strongest" targets the weakest critter. Ported as-is.
        var candidates = new (int Rating, int Distance, bool HitMe)[]
        {
            (30, 5, false),
            (7, 9, false),
            (18, 2, false),
        };
        Assert.Equal(1, CompanionAi.PickTarget(AttackWho.Strongest, candidates));
    }

    [Fact]
    public void WeakestPicksTheHIGHESTRatedTarget()
    {
        var candidates = new (int Rating, int Distance, bool HitMe)[]
        {
            (30, 5, false),
            (7, 9, false),
            (18, 2, false),
        };
        Assert.Equal(0, CompanionAi.PickTarget(AttackWho.Weakest, candidates));
    }

    [Fact]
    public void EqualRatingsBreakByDistance()
    {
        var candidates = new (int Rating, int Distance, bool HitMe)[]
        {
            (12, 6, false),
            (12, 2, false),
        };
        Assert.Equal(1, CompanionAi.PickTarget(AttackWho.Strongest, candidates));
    }

    [Fact]
    public void ClosestIgnoresRating()
    {
        var candidates = new (int Rating, int Distance, bool HitMe)[]
        {
            (99, 1, false),
            (1, 4, false),
        };
        Assert.Equal(0, CompanionAi.PickTarget(AttackWho.Closest, candidates));
    }
```

- [ ] **Step 6: Run it and confirm it fails**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~CompanionAiTests`
Expected: FAIL — `StrongestPicksTheLOWESTRatedTarget` returns 0 (the current HP-based comparator picks the highest).

- [ ] **Step 7: Rework `PickTarget` to rank by rating**

In `src/Hexwaste.Formats/Combat/CompanionAi.cs`, replace the `PickTarget` and `Better` members with:

```csharp
    /// <summary>Pick a target by priority among the candidates (combat_ai.cc _ai_danger_source's
    /// attack_who switch, :1561). Each candidate carries its _combatai_rating, hex-distance from the
    /// actor, and whether it last hit the actor. Closest is the default (the pre-P50 behaviour);
    /// ties break by distance (Hexwaste's stable substitute for the engine's unstable qsort).</summary>
    public static int PickTarget(AttackWho mode, IReadOnlyList<(int Rating, int Distance, bool HitMe)> candidates)
    {
        if (candidates.Count == 0)
            return -1;
        int best = 0;
        for (int i = 1; i < candidates.Count; i++)
            if (Better(mode, candidates[i], candidates[best]))
                best = i;
        // WhoeverAttackingMe falls back to closest when nobody has hit the actor.
        if (mode == AttackWho.WhoeverAttackingMe && !candidates[best].HitMe)
            return PickTarget(AttackWho.Closest, candidates);
        return best;
    }

    // VANILLA QUIRK — ported deliberately, do NOT "correct" it: _compare_strength (combat_ai.cc:1330)
    // returns -1 when rating1 < rating2, so _ai_sort_list_strength qsorts ASCENDING and the picker
    // (:1691) takes targets[0] — the LOWEST-rated critter. STRONGEST therefore targets the weakest,
    // and WEAKEST (_compare_weakness, :1366, the mirrored comparator) targets the strongest.
    private static bool Better(AttackWho mode, (int Rating, int Distance, bool HitMe) a, (int Rating, int Distance, bool HitMe) b) => mode switch
    {
        AttackWho.Strongest => a.Rating != b.Rating ? a.Rating < b.Rating : a.Distance < b.Distance,
        AttackWho.Weakest => a.Rating != b.Rating ? a.Rating > b.Rating : a.Distance < b.Distance,
        AttackWho.WhoeverAttackingMe => a.HitMe != b.HitMe ? a.HitMe : a.Distance < b.Distance,
        _ => a.Distance < b.Distance, // Closest / Whomever
    };
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~CompanionAiTests`
Expected: PASS. If other tests in the file still reference the old `Hp` tuple field, rename that field to `Rating` in them — the values are positional, so only the name changes.

- [ ] **Step 9: Add the engine `Rating` wrapper and the retaliation rule**

In `src/Hexwaste.Formats/Combat/CombatEngine.cs`, replace the existing `RegisterHit` (at `:1611-1618`, the one whose doc-comment says "DOCUMENTED DIVERGENCE: last-hitter-wins") with:

```csharp
    /// <summary>ported from fallout2-ce src/combat_ai.cc _combatai_rating (:3449): this critter's threat
    /// rating, 0 for a null/dead/knocked-out critter (the engine's DAM_DEAD | DAM_KNOCKED_OUT and
    /// non-critter guards). NOTE: the engine sums over BOTH hands (critterGetItem1/critterGetItem2);
    /// Hexwaste models one wielded slot, so the equipped weapon is the only candidate.</summary>
    private int Rating(MapObject? critter)
    {
        if (critter is null || critter.IsDead || IsKnockedOut(critter))
            return 0;
        CritterState? state = _host.GetCritterState(critter);
        if (state is null)
            return 0;
        (ProtoInfo? proto, _) = _host.EquippedWeapon(critter);
        return AiRating.Score(state.MeleeDamage, state.ArmorClass, proto?.Weapon?.MaxDamage ?? 0);
    }

    /// <summary>Record who last hit a critter (whoHitMe) — ported from fallout2-ce combat.cc:4707 +
    /// combat_ai.cc _combatai_check_retaliation (:3484): an unset whoHitMe is taken unconditionally, but
    /// an existing one is only REPLACED by a strictly higher-rated attacker (so a critter keeps hunting
    /// the scarier enemy instead of whoever last scratched it). Hexwaste's team gate is retained — the
    /// engine's equivalent gate lives in the callers.</summary>
    private void RegisterHit(MapObject target, MapObject attacker)
    {
        if (target.IsDead || attacker == target || attacker.Team == target.Team)
            return;
        if (target.WhoHitMe is { } current && Rating(attacker) <= Rating(current))
            return; // combat_ai.cc:3488 — only a STRICTLY greater rating retargets
        target.WhoHitMe = attacker;
    }
```

- [ ] **Step 10: Feed ratings into the ally target ranking**

In `src/Hexwaste.Formats/Combat/CombatEngine.cs` (the ally block around `:2818`), replace the `ranked` list construction and the `PickTarget` call with:

```csharp
        List<(int Rating, int Distance, bool HitMe)> ranked = hostiles
            .Select(h => (Rating(h), HexGrid.Distance(ally.HexTile, h.HexTile),
                ReferenceEquals(ally.WhoHitMe, h)))
            .ToList();
        MapObject target = hostiles[CompanionAi.PickTarget(ai.AttackWho, ranked)];
```

- [ ] **Step 11: Build and run the full unit suite**

Run: `dotnet build && dotnet test tests/Hexwaste.Formats.Tests`
Expected: build clean, all tests PASS.

- [ ] **Step 12: Run the combat and encounter goldens**

Run: `scripts/combat-golden.sh check && scripts/encounter-golden.sh check`
Expected: every fixture byte-identical. The fixtures are single-attacker fights at disposition Closest, so neither consumer is reachable. **If a fixture moves, stop and report** — do not adjust the port; the item escalates to the deferred re-record tier.

- [ ] **Step 13: Commit**

```bash
git add src/Hexwaste.Formats/Combat/AiRating.cs src/Hexwaste.Formats/Combat/CompanionAi.cs \
        src/Hexwaste.Formats/Combat/CombatEngine.cs tests/Hexwaste.Formats.Tests/AiRatingTests.cs \
        tests/Hexwaste.Formats.Tests/CompanionAiTests.cs
git commit -m "feat: port _combatai_rating — rating-based retaliation + strength/weakness targeting

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: `_ai_best_weapon` perk factor + `aiHaveAmmo` bag search

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/AiBestWeapon.cs` (add `AvgDamage`)
- Modify: `src/Hexwaste.Formats/Combat/ICombatHost.cs` (add `CarriedAmmoCalibers`)
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs:2415-2429` (the `AiSwitchWeapon` candidate loop)
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs` (implement the seam)
- Test: `tests/Hexwaste.Formats.Tests/AiBestWeaponTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `AiBestWeapon.AvgDamage(int minDamage, int maxDamage, int weaponPerk) -> int`; `ICombatHost.CarriedAmmoCalibers(MapObject critter) -> IReadOnlyList<int>` (default `[]`).

- [ ] **Step 1: Write the failing test for the perk factor**

Append to `tests/Hexwaste.Formats.Tests/AiBestWeaponTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void AvgDamageIsTheMidpointWithoutAPerk()
    {
        // weaponPerk -1 = no perk (WeaponProtoStats.WeaponPerk's default).
        Assert.Equal(7, AiBestWeapon.AvgDamage(minDamage: 4, maxDamage: 10, weaponPerk: -1));
    }

    [Fact]
    public void AvgDamageDoublesWhenTheWeaponHasAPerk()
    {
        // combat_ai.cc:1866 — SFALL "Lower weapon score multiplier for having perk": avgDamage *= 2.
        // PerkAccurate == 59.
        Assert.Equal(14, AiBestWeapon.AvgDamage(minDamage: 4, maxDamage: 10, weaponPerk: 59));
    }

    [Fact]
    public void AvgDamageUsesIntegerDivisionLikeTheEngine()
    {
        Assert.Equal(7, AiBestWeapon.AvgDamage(minDamage: 5, maxDamage: 10, weaponPerk: -1));
    }
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~AiBestWeaponTests`
Expected: build error — `AiBestWeapon.AvgDamage` does not exist.

- [ ] **Step 3: Add `AvgDamage` to `AiBestWeapon`**

In `src/Hexwaste.Formats/Combat/AiBestWeapon.cs`, add this method to the class (immediately above `HasWeapPrefType`):

```csharp
    /// <summary>ported from fallout2-ce src/combat_ai.cc _ai_best_weapon (:1857-1870): the candidate's
    /// damage score — the (min+max)/2 midpoint (the SFALL avg-damage fix), DOUBLED when the weapon
    /// carries a weapon perk (:1866). The explosive ×(extrasLength+1) factor (:1861) is NOT applied —
    /// it needs _compute_explosion_on_extras, deferred with the ring-spiral explosion port.</summary>
    public static int AvgDamage(int minDamage, int maxDamage, int weaponPerk)
    {
        int avg = (minDamage + maxDamage) / 2;
        return weaponPerk != -1 ? avg * 2 : avg;
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~AiBestWeaponTests`
Expected: PASS.

- [ ] **Step 5: Add the ammo seam to `ICombatHost`**

In `src/Hexwaste.Formats/Combat/ICombatHost.cs`, next to `CritterInventoryWeapons`, add:

```csharp
    /// <summary>ported from fallout2-ce src/combat_ai.cc aiHaveAmmo (:1765): the CALIBERS of every ammo
    /// item the critter carries, so a ranged weapon with an empty magazine still counts as usable when
    /// matching ammo is in the bag. Default empty — with no carried ammo the caller falls back to the
    /// loaded-round count, which is exactly the pre-port behaviour (so the fixtures stay inert).</summary>
    IReadOnlyList<int> CarriedAmmoCalibers(MapObject critter) => [];
```

- [ ] **Step 6: Use both the perk factor and the bag search in `AiSwitchWeapon`**

In `src/Hexwaste.Formats/Combat/CombatEngine.cs`, inside the `foreach ((ProtoInfo proto, MapObject item) in _host.CritterInventoryWeapons(enemy))` loop, replace the ranged-ammo guard and the `cand` construction with:

```csharp
                // _ai_can_use_weapon's ammo gate (combat_ai.cc:2036 → aiHaveAmmo, :1765): a ranged weapon
                // qualifies with rounds loaded OR matching ammo in the bag (the engine searches inventory;
                // the pre-port approximation was loaded-rounds only).
                if (attackType == WeaponClass.AttackRanged && _host.WeaponAmmo(proto, item) <= 0
                    && !_host.CarriedAmmoCalibers(enemy).Contains(weapon.Caliber))
                    continue;

                var cand = new AiBestWeapon.Choice(attackType,
                    AiBestWeapon.AvgDamage(weapon.MinDamage, weapon.MaxDamage, proto.Weapon!.WeaponPerk),
                    proto.Cost, IsFlare: proto.Pid == 79);
```

Note: `weapon` is already the non-null `proto.Weapon` from the loop's pattern match, so `weapon.WeaponPerk` may be used instead of `proto.Weapon!.WeaponPerk` if the local is in scope — prefer the local.

- [ ] **Step 7: Implement the seam in the viewer**

In `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs`, add:

```csharp
    /// <summary>aiHaveAmmo (combat_ai.cc:1765): every ammo caliber in this critter's inventory.</summary>
    public IReadOnlyList<int> CarriedAmmoCalibers(MapObject critter) =>
        [.. critter.Inventory.Select(it => SafeProto(it.Pid)?.Ammo?.Caliber ?? -1).Where(c => c >= 0).Distinct()];
```

- [ ] **Step 8: Build and run the unit suite**

Run: `dotnet build && dotnet test tests/Hexwaste.Formats.Tests`
Expected: build clean, all tests PASS.

- [ ] **Step 9: Run the goldens**

Run: `scripts/combat-golden.sh check && scripts/encounter-golden.sh check`
Expected: byte-identical — both changes sit behind the dry-gun inventory-switch path, which the melee fixtures never enter. **If a fixture moves, stop and report.**

- [ ] **Step 10: Commit**

```bash
git add src/Hexwaste.Formats/Combat/AiBestWeapon.cs src/Hexwaste.Formats/Combat/ICombatHost.cs \
        src/Hexwaste.Formats/Combat/CombatEngine.cs src/Hexwaste.Viewer/ViewerGame.CombatHost.cs \
        tests/Hexwaste.Formats.Tests/AiBestWeaponTests.cs
git commit -m "feat: _ai_best_weapon perk factor + aiHaveAmmo inventory search

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: NPC combat-drug timed wear-off

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Chemistry.cs:63` (`_pendingDrugEvents` shape), `:122-128` (`ScheduleDrugEvent`), `ProcessDrugs`
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs:524-550` (`TryNpcUseCombatDrug`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2807-2808` (drop the combat-idle clear)
- Modify: `src/Hexwaste.Viewer/ViewerGame.SaveLoad.cs:300-301, 590-592` (persist dude entries only)

**Interfaces:**
- Consumes: nothing from Tasks 1-2.
- Produces: `_pendingDrugEvents` becomes `List<(long FireTick, MapObject? Owner, int[] Stats, int[] Amounts)>` — `null` owner = the dude.

**Background:** the dude's pipeline is already faithful (`ViewerGame.Chemistry.cs:43` — immediate effect, then `ScheduleDrugEvent(Duration1/Duration2)` down-ramps on the game clock, per `item.cc _item_d_take_drug` / `_insert_drug_effect`). NPCs bypass it: `TryNpcUseCombatDrug` applies the bonus with no duration and `ViewerGame.cs:2807` wipes `_npcDrugBonus` when combat goes idle. This task routes NPCs through the same queue.

**Save-format note:** `_pendingDrugEvents` is persisted (`SaveState.PendingDrug`). NPC-owned entries reference a live `MapObject` and are **not** serializable, so only dude entries (`Owner is null`) are saved. That keeps the save format unchanged and is no worse than today, where NPC bonuses do not survive combat at all. Say so in a code comment.

- [ ] **Step 1: Widen the pending-event tuple**

In `src/Hexwaste.Viewer/ViewerGame.Chemistry.cs`, replace the `_pendingDrugEvents` declaration with:

```csharp
    /// <summary>Pending delayed drug kicks (the down-ramp / wear-off), keyed by the game-tick they fire at.
    /// ported from item.cc's EVENT_TYPE_DRUG queue; driven from UpdateClock like the poison tick. (P37.)
    /// Owner null = the dude; a non-null Owner is an NPC that chem'd up in combat (its bonus lives in
    /// _npcDrugBonus and now decays on the clock instead of being wiped at combat end).</summary>
    private readonly List<(long FireTick, MapObject? Owner, int[] Stats, int[] Amounts)> _pendingDrugEvents = [];
```

- [ ] **Step 2: Make scheduling owner-aware**

In the same file, replace `ScheduleDrugEvent` with:

```csharp
    /// <summary>Schedule a delayed drug kick durationMin game-minutes out (item.cc _insert_drug_effect:
    /// skip an all-zero kick; delay = 600 ticks/game-minute, the GameClock basis). (P37.)
    /// <paramref name="owner"/> null = the dude; otherwise the NPC whose _npcDrugBonus ramps down.</summary>
    private void ScheduleDrugEvent(int durationMin, int[] stats, int[] amounts, MapObject? owner = null)
    {
        if (amounts[0] == 0 && amounts[1] == 0 && amounts[2] == 0)
            return; // item.cc:2601 — an unused kick schedules nothing
        _pendingDrugEvents.Add((_clock.Ticks + 600L * durationMin, owner, stats, amounts));
    }
```

- [ ] **Step 3: Apply due kicks to the right owner**

In `ProcessDrugs`, replace these three lines (`ViewerGame.Chemistry.cs:144-146`):

```csharp
            (long _, int[] stats, int[] amounts) = _pendingDrugEvents[next];
            _pendingDrugEvents.RemoveAt(next);
            ApplyDrugEffect(stats, amounts, immediate: false);
```

with:

```csharp
            (long _, MapObject? owner, int[] stats, int[] amounts) = _pendingDrugEvents[next];
            _pendingDrugEvents.RemoveAt(next);
            if (owner is null)
                ApplyDrugEffect(stats, amounts, immediate: false);
            else
                ApplyNpcDrugEffect(owner, stats, amounts);
```

Keep the surrounding earliest-first `while` loop exactly as it is — only the dispatch changes.

- [ ] **Step 4: Add the NPC applier**

In `src/Hexwaste.Viewer/ViewerGame.Chemistry.cs`, add:

```csharp
    /// <summary>The NPC analogue of ApplyDrugEffect's 0..34 branch (item.cc _perform_drug_effect, :2639):
    /// fold a kick into the critter's _npcDrugBonus. NPCs have no character sheet, so only the SPECIAL /
    /// derived bonus band and current HP apply — poison/radiation (36/37) are dude-only in Hexwaste.
    /// No RNG: the -2 random-range roll is immediate-only, and every scheduled kick is a fixed delta.</summary>
    private void ApplyNpcDrugEffect(MapObject critter, int[] stats, int[] amounts)
    {
        int[] bonus = _npcDrugBonus.TryGetValue(critter, out int[]? b) ? b : _npcDrugBonus[critter] = new int[35];
        for (int i = 0; i < 3; i++)
        {
            int stat = stats[i];
            if (stat == 35)
            {
                int max = GetCritterState(critter)?.MaxHp ?? critter.CurrentHp;
                critter.CurrentHp = Math.Clamp(critter.CurrentHp + amounts[i], 0, max);
            }
            else if (stat >= 0 && stat < 35)
            {
                bonus[stat] += amounts[i];
            }
        }
    }
```

- [ ] **Step 5: Schedule the two down-ramps when an NPC chems up**

In `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs`, in `TryNpcUseCombatDrug`, immediately after the immediate-effect loop that fills `bonus` and `hpHeal` (and before the `item.StackCount--` line), add:

```csharp
        // item.cc _item_d_take_drug (:2809): the immediate effect is followed by two delayed kicks that
        // ramp it back down — the same wear-off the dude gets (P37). Before this, an NPC's buff was
        // permanent until the blanket combat-end wipe, i.e. it had no duration at all.
        ScheduleDrugEvent(drug.Duration1, drug.Stats, drug.Amount1, critter);
        ScheduleDrugEvent(drug.Duration2, drug.Stats, drug.Amount2, critter);
```

Do **not** call `TryAddict` here — the `_item_d_take_drug` addiction tail is dude-gated at Hexwaste's call site and stays that way.

- [ ] **Step 6: Drop the blanket combat-end wipe**

In `src/Hexwaste.Viewer/ViewerGame.cs`, delete these two lines (at `:2807-2808`):

```csharp
        if (_npcDrugBonus.Count > 0 && _combat.Phase == Formats.Combat.CombatPhase.Idle)
            _npcDrugBonus.Clear();
```

Replace them with a comment so the removal is legible:

```csharp
        // P37/fidelity: NPC drug bonuses are NO LONGER wiped when combat ends — they ramp down on the
        // game clock through _pendingDrugEvents, like the dude's (item.cc _insert_drug_effect).
```

- [ ] **Step 7: Persist dude entries only**

In `src/Hexwaste.Viewer/ViewerGame.SaveLoad.cs`, replace the save projection at `:300-301` with:

```csharp
            // Only the DUDE's pending kicks persist: an NPC-owned entry references a live MapObject that
            // no save format can name. NPC buffs therefore do not survive a save/load — no worse than the
            // pre-port behaviour, where they did not survive the end of combat.
            PendingDrugs = _pendingDrugEvents.Any(e => e.Owner is null)
                ? [.. _pendingDrugEvents.Where(e => e.Owner is null)
                      .Select(e => new SaveState.PendingDrug(e.FireTick, e.Stats, e.Amounts))]
```

and the load at `:592` with:

```csharp
                _pendingDrugEvents.Add((e.FireTick, null, e.Stats, e.Amounts));
```

- [ ] **Step 8: Build and run the unit suite**

Run: `dotnet build && dotnet test tests/Hexwaste.Formats.Tests`
Expected: build clean, all tests PASS (including the existing `AiCombatDrugTests` and any save/load tests).

- [ ] **Step 9: Run the goldens**

Run: `scripts/combat-golden.sh check && scripts/encounter-golden.sh check && scripts/quest-golden.sh check`
Expected: byte-identical — every fixture enemy is `chem_use = clean`, so `AiCombatDrug.ShouldUse` short-circuits without drawing RNG. **If a fixture moves, stop and report.**

- [ ] **Step 10: Prove the wear-off is live (not inert)**

A byte-identical golden can hide a feature that never runs. Verify against real data: start a fight where an NPC with a `chem_use` packet and a carried Jet/Psycho takes its turn, then step the clock past `Duration1` and confirm the bonus ramps down.

Run (adjust the map/hex to a chem-using NPC found via `--map-objects`):
`dotnet run --project src/Hexwaste.Viewer -- --map denbus2.map --fight 11670 --rng-seed 3 --advance-ms 60000`
Expected: the stdout drug lines show the NPC's bonus applied and later reduced, rather than the bonus simply disappearing when combat ends. Record what you observed in the commit body.

- [ ] **Step 11: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Chemistry.cs src/Hexwaste.Viewer/ViewerGame.CombatHost.cs \
        src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/ViewerGame.SaveLoad.cs
git commit -m "feat: NPC combat drugs ramp down on the clock instead of clearing at combat end

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Ground pickup (`_ai_search_environ` + `_ai_retrieve_object`)

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/ICombatHost.cs` (two new seams)
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` (`_aiLastItem`, the `AiSwitchWeapon` fallback)
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs` (real seams)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs` (new `StartupAction`), `ViewerGame.Harness.cs` (probe), `Program.cs` (arg)

**Interfaces:**
- Consumes: `AiBestWeapon.HasWeapPrefType` (existing), `ICombatHost.EquipWeapon` (existing).
- Produces: `ICombatHost.GroundItemsNear(MapObject critter, int maxDistance) -> IReadOnlyList<(ProtoInfo Proto, MapObject Item)>` (default `[]`); `ICombatHost.TryRetrieveItem(MapObject critter, MapObject item) -> bool` (default `false`); `ViewerGame.StartupAction.AiPickupProbe(int NpcHex, int TargetHex)`.

- [ ] **Step 1: Add the two seams to `ICombatHost`**

In `src/Hexwaste.Formats/Combat/ICombatHost.cs`, near `CritterInventoryWeapons`, add:

```csharp
    /// <summary>ported from fallout2-ce src/combat_ai.cc _ai_search_environ (:2178): the ITEM objects
    /// lying on the critter's elevation within <paramref name="maxDistance"/> hexes, nearest first
    /// (the engine's _ai_sort_list_distance + the PE+5 cutoff at :2193). Default empty — nothing on the
    /// ground means the AI behaves exactly as before the port.</summary>
    IReadOnlyList<(ProtoInfo Proto, MapObject Item)> GroundItemsNear(MapObject critter, int maxDistance) => [];

    /// <summary>ported from fallout2-ce src/combat_ai.cc _ai_retrieve_object (:2237): try to pick the item
    /// up. Returns true when it is now in the critter's inventory; false when the critter is not adjacent
    /// yet (a walk toward it was started — the engine's actionPickUp + _combat_turn_run, resumed next turn
    /// via the caller's lastItem memory). Default false = no pickup ability (pre-port behaviour).</summary>
    bool TryRetrieveItem(MapObject critter, MapObject item) => false;
```

- [ ] **Step 2: Add the cross-turn memory field to `CombatEngine`**

In `src/Hexwaste.Formats/Combat/CombatEngine.cs`, beside the other per-critter AI dictionaries, add:

```csharp
    /// <summary>ported from fallout2-ce src/combat_ai.cc aiInfoSetLastItem (:2258): the ground item a
    /// critter is walking toward but could not reach this turn, so it resumes next turn instead of
    /// re-deciding. Cleared when the item is retrieved and when combat ends.</summary>
    private readonly Dictionary<MapObject, (ProtoInfo Proto, MapObject Item)> _aiLastItem = [];
```

In `EndCombat()`, add `_aiLastItem.Clear();` alongside the other per-fight state resets.

- [ ] **Step 3: Wire the ground search into `AiSwitchWeapon`**

In `src/Hexwaste.Formats/Combat/CombatEngine.cs`, replace the tail of `AiSwitchWeapon` (currently `if (winner is { } w) { … } return (null, null); // fists`) with:

```csharp
        if (winner is { } w)
        {
            _host.EquipWeapon(enemy, w.Item);
            _aiLastItem.Remove(enemy);
            return (w.Proto, w.Item);
        }

        // _ai_switch_weapons (combat_ai.cc:2606): nothing usable in the bag → look for a weapon lying on
        // the ground within PE+5 hexes, walk to it, pick it up and wield it. BIPED/ROBOTIC only (the
        // body-type gate above already returned for other bodies).
        (ProtoInfo Proto, MapObject Item)? wanted =
            _aiLastItem.TryGetValue(enemy, out (ProtoInfo Proto, MapObject Item) remembered) ? remembered : null;
        if (wanted is null)
        {
            int perception = self?.Stat(CritterStat.Perception) ?? 0;
            foreach ((ProtoInfo p, MapObject it) in _host.GroundItemsNear(enemy, perception + 5))
            {
                if (p.Weapon is null)
                    continue;
                int groundType = WeaponClass.AttackType(p.ExtendedFlags);
                if (!AiBestWeapon.HasWeapPrefType(bestWeapon, groundType))
                    continue;
                if (self is not null
                    && self.SkillValue(WeaponClass.Skill(p.ExtendedFlags, p.Weapon.DamageType)) < minToHit)
                    continue;
                if (anyArmCrippled && WeaponProtoStats.IsTwoHanded(p.ExtendedFlags))
                    continue;
                wanted = (p, it);
                break;
            }
        }
        if (wanted is { } g)
        {
            if (_host.TryRetrieveItem(enemy, g.Item))
            {
                _aiLastItem.Remove(enemy);
                _host.EquipWeapon(enemy, g.Item);
                return (g.Proto, g.Item);
            }
            _aiLastItem[enemy] = g; // not adjacent yet — resume next turn (aiInfoSetLastItem)
        }
        return (null, null); // fists
```

- [ ] **Step 4: Implement the seams in the viewer**

In `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs`, add:

```csharp
    /// <summary>_ai_search_environ (combat_ai.cc:2178): item objects on the current elevation within
    /// maxDistance, nearest first. PID type 0 == OBJ_TYPE_ITEM (Fid.PidType).</summary>
    public IReadOnlyList<(ProtoInfo Proto, MapObject Item)> GroundItemsNear(MapObject critter, int maxDistance)
    {
        List<(ProtoInfo Proto, MapObject Item, int Distance)> found = [];
        foreach (MapObject o in _flatObjects[_elevation].Concat(_solidObjects[_elevation]))
        {
            if (Formats.Fid.PidType(o.Pid) != 0)
                continue; // OBJ_TYPE_ITEM only
            int d = Formats.Hex.HexGrid.Distance(critter.HexTile, o.HexTile);
            if (d > maxDistance)
                continue;
            if (SafeProto(o.Pid) is { } proto)
                found.Add((proto, o, d));
        }
        return [.. found.OrderBy(f => f.Distance).Select(f => (f.Proto, f.Item))];
    }

    /// <summary>_ai_retrieve_object (combat_ai.cc:2237): adjacent → transfer to inventory; otherwise start
    /// a walk toward it and report "not yet" so the caller remembers it for next turn.</summary>
    public bool TryRetrieveItem(MapObject critter, MapObject item)
    {
        if (Formats.Hex.HexGrid.Distance(critter.HexTile, item.HexTile) > 1)
        {
            StartWalk(critter, item.HexTile);
            return false;
        }
        OnScriptObjectRemoved(item);
        foreach (MapElevation? elev in _map.Elevations)
            elev?.Objects.Remove(item);
        _flatObjects[_elevation].Remove(item);
        _solidObjects[_elevation].Remove(item);
        critter.Inventory.Add(item);
        _audio?.PlaySfx("ipickup1", SfxGain(critter)); // P117 sfx (inventory.cc:2364)
        Log($"The {ObjectName(critter)} picks up: {ObjectName(item)}.");
        return true;
    }
```

Adjust the namespace prefixes (`Formats.Hex.HexGrid`, `Formats.Fid`) to match the file's existing `using` set.

- [ ] **Step 5: Add the probe `StartupAction`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, in the `StartupAction` block (beside `NpcWalk` at `:872`), add:

```csharp
        /// <summary>Fidelity probe: strip the critter at NpcHex of its wielded weapon, then run the AI
        /// weapon switch against the critter at TargetHex — exercising the ground-pickup fallback
        /// (_ai_search_environ → _ai_retrieve_object). Reports what it walked to and wielded.</summary>
        public sealed record AiPickupProbe(int NpcHex, int TargetHex) : StartupAction;
```

- [ ] **Step 6: Parse the flag**

In `src/Hexwaste.Viewer/Program.cs`, beside the `--kill` case, add:

```csharp
        case "--ai-pickup-probe" when i + 2 < args.Length: // NPC weapon-switch ground-pickup fallback
            actions.Add(new ViewerGame.StartupAction.AiPickupProbe(
                int.Parse(args[++i]), int.Parse(args[++i])));
            break;
```

- [ ] **Step 7: Handle the probe**

In `src/Hexwaste.Viewer/ViewerGame.Harness.cs`, beside the `KillCritterAt` case, add:

```csharp
                case StartupAction.AiPickupProbe(var npcHex, var targetHex):
                {
                    MapObject? npc = CritterAt(npcHex, includeFlat: true);
                    MapObject? target = CritterAt(targetHex, includeFlat: true);
                    if (npc is null || target is null)
                    {
                        Console.Error.WriteLine($"ai-pickup-probe: no critter at {npcHex} or {targetHex}");
                        break;
                    }
                    IReadOnlyList<(ProtoInfo Proto, MapObject Item)> ground =
                        GroundItemsNear(npc, (GetCritterState(npc)?.Stat(Formats.Combat.CritterStat.Perception) ?? 0) + 5);
                    Console.WriteLine($"ai-pickup-probe: ground items in range = {ground.Count}");
                    foreach ((ProtoInfo p, MapObject it) in ground)
                        Console.WriteLine($"  pid={p.Pid} tile={it.HexTile} weapon={p.Weapon is not null}");
                    int pid = _combat.ProbeAiWeaponSwitch(npc, target);
                    Console.WriteLine($"ai-pickup-probe: wielded pid={pid} tile={npc.HexTile}");
                    break;
                }
```

- [ ] **Step 8: Build and run the unit suite**

Run: `dotnet build && dotnet test tests/Hexwaste.Formats.Tests`
Expected: build clean, all tests PASS. The fake combat host inherits both defaults, so no test host changes are needed.

- [ ] **Step 9: Run the goldens**

Run: `scripts/combat-golden.sh check && scripts/encounter-golden.sh check && scripts/quest-golden.sh check`
Expected: byte-identical — the fallback only runs when the inventory fold yields nothing, which the melee fixtures never reach. **If a fixture moves, stop and report.**

- [ ] **Step 10: Prove the pickup works on real data**

Find a map with a critter and a nearby ground weapon:
`dotnet run --project src/Hexwaste.Viewer -- --map arcaves.map --map-objects`

Then run the probe with an NPC hex and a target hex from that dump, repeating with `--advance-ms 4000` so the walk completes and the second turn retrieves the item:
`dotnet run --project src/Hexwaste.Viewer -- --map arcaves.map --ai-pickup-probe <npcHex> <targetHex> --advance-ms 4000`
Expected: the probe lists ground items in range and reports a non-`-1` wielded pid (or, on the first pass, a walk toward the item followed by the pickup after the advance). Record the observed output in the commit body. If no vanilla map places a loose weapon near a critter, say so explicitly rather than claiming the path was demonstrated.

- [ ] **Step 11: Commit**

```bash
git add src/Hexwaste.Formats/Combat/ICombatHost.cs src/Hexwaste.Formats/Combat/CombatEngine.cs \
        src/Hexwaste.Viewer/ViewerGame.CombatHost.cs src/Hexwaste.Viewer/ViewerGame.cs \
        src/Hexwaste.Viewer/ViewerGame.Harness.cs src/Hexwaste.Viewer/Program.cs
git commit -m "feat: AI picks up a usable ground weapon when its bag is empty (_ai_search_environ)

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: The extra `AiSwitchWeapon` triggers

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` (the two extra `AiSwitchWeapon` triggers)
- Modify: `docs/BACKLOG.md`

**Interfaces:**
- Consumes: `AiSwitchWeapon` (Tasks 2 and 4 versions).
- Produces: nothing new.

**⚠️ Companion armor auto-equip (spec item 7) is CUT from this batch — do not implement it.**
Grounding during planning found that **Hexwaste has no worn-armor model for any critter but the dude**:
`ApplyArmorBonus` (`ViewerGame.cs:4318`) folds armor into the dude's character sheet, the sprite comes
from `ArmorProtoStats.Male/FemaleFid` on the dude only, and `CritterState` has no worn-armor slot at
all. `_ai_search_inven_armor` (`combat_ai.cc:2051`) therefore has nothing to equip into — landing it
would first require a per-NPC worn-armor model (stat bonuses, DR/DT in the damage path, sprite fid),
which is its own task, not a step. Step 4 below records this in the backlog. Do not invent a
`WornArmorProto`/`EquipArmor` pair to make the item fit.

- [ ] **Step 1: Wire the two extra `AiSwitchWeapon` triggers**

In `src/Hexwaste.Formats/Combat/CombatEngine.cs`, in the enemy attack path where a shot is rejected, call the switch for the crippled-arm and out-of-range-with-no-weapon cases (`combat_ai.cc:2800` and `:2823`). Where the code currently only handles the dry-gun case, add:

```csharp
            // _ai_try_attack (combat_ai.cc:2800): a crippled arm makes the wielded weapon unusable →
            // switch to whatever the critter can still use (one-handed / fists).
            if ((enemy.CombatResults & (CriticalTables.DamCripArmLeft | CriticalTables.DamCripArmRight)) != 0)
                (weaponProto, weaponItem) = AiSwitchWeapon(enemy, ai, distance, weaponItem);

            // _ai_try_attack (combat_ai.cc:2823): out of range with NO weapon in hand → try to arm
            // ourselves before falling back to moving closer.
            else if (weaponProto is null && distance > range)
                (weaponProto, weaponItem) = AiSwitchWeapon(enemy, ai, distance, weaponItem);
```

Place these where `weaponProto`/`weaponItem`/`distance`/`range` are already in scope, before the range/AP decision that follows. If the local names differ, adapt them — do not restructure the surrounding turn logic.

- [ ] **Step 2: Build and run the full unit suite**

Run: `dotnet build && dotnet test tests/Hexwaste.Formats.Tests`
Expected: build clean, all tests PASS.

- [ ] **Step 3: Run every golden net**

Run: `scripts/combat-golden.sh check && scripts/encounter-golden.sh check && scripts/quest-golden.sh check && scripts/opening-golden.sh check && scripts/census-sweep.sh check && scripts/endgame-golden.sh check`
Expected: all byte-identical. Note the risk here: `arcaves-knockdown-day2` aims at a leg, not an arm, so no fixture should cripple an arm — but if one does and the fixture moves, **stop and report**; the crippled-arm trigger escalates to the re-record tier while the rest of the task stands.

- [ ] **Step 4: Update the backlog**

In `docs/BACKLOG.md`, edit the **A2** entry to record:

- **Ported:** rating-based retaliation, strength/weakness targeting, the `_ai_best_weapon` perk factor, `aiHaveAmmo` bag search, NPC drug timed wear-off, ground pickup, the crippled-arm and out-of-range weapon switches.
- **Grounding corrections:** `attack_who` is party-member-only (`combat_ai.cc:1544`) so Hexwaste's companion-only application is faithful, not a gap; `_combatai_rating` also keys `_compare_strength`/`_compare_weakness` (the HP-based companion ranking was an undocumented divergence); perception disengage was already ported in `WantsToStopFighting` — the deferred piece is `PruneEscapedHostiles`.
- **Newly grounded, NOT done:** `_ai_search_inven_armor` (companion armor auto-equip) needs a per-NPC worn-armor model first — Hexwaste models worn armor for the dude only. Size it as its own task.
- **Still in the re-record tier:** ring-spiral explosion damage, `_combat_safety_invalidate_weapon` + `_cai_retargetTileFromFriendlyFire`, `_ai_danger_source` + perception-based `PruneEscapedHostiles`, the explosive `×(extras+1)` best-weapon factor.

- [ ] **Step 5: Commit**

```bash
git add src/Hexwaste.Formats/Combat/CombatEngine.cs docs/BACKLOG.md
git commit -m "feat: crippled-arm and out-of-range AI weapon switches (_ai_try_attack)

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Done when

Six items landed across five commits (companion armor cut — see Task 5); `dotnet test` green; all six golden nets byte-identical (or any mover explicitly escalated to the re-record tier and reported, not papered over); the two live probes (NPC drug decay, ground pickup) demonstrated on real game data with their output recorded; the app boots (`dotnet run --project src/Hexwaste.Viewer`, needs a display and `FALLOUT2_DIR`); `docs/BACKLOG.md` A2 reconciled.
