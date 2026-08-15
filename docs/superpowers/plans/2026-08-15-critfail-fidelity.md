# Crit-failure damage and damage-proc fidelity (F11–F13) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close backlog items F11, F12 and F13 — three divergences in `CombatEngine`'s crit-failure /
accidental-hit neighbourhood — with one deliberate golden re-record for F11 and none for F12/F13.

**Architecture:** Three independent single-file changes to
`src/Hexwaste.Formats/Combat/CombatEngine.cs`, each with hermetic xUnit coverage through the existing
`FakeCombatHost`. No new host seam, no new type, no signature change visible outside `CombatEngine`
except one optional parameter with a default that reproduces today's behaviour.

**Tech Stack:** C# / .NET 10 (`net10.0`), xUnit. Reference source: `reference/fallout2-ce` at
`e97087b` (gitignored clone).

## Global Constraints

- **Never copy, embed, or commit game assets.** `.gitignore` excludes `*.dat`, `*.map`, `*.frm`,
  `*.pal`, `game-data/` — keep it that way.
- **Port from `reference/fallout2-ce`, never guess.** Every behavioural change carries a comment
  citing its source file and function, e.g.
  `// ported from fallout2-ce src/combat.cc attackComputeDamage()`.
  If a detail cannot be confirmed from the reference, **stop and ask** — do not invent it.
- `alexbatalov e97087b` is authoritative for vanilla behaviour. `community/main` is a bug-fix
  candidate source only; cite ported fork fixes as `(community fix #NNN)`.
- **No new dependencies.** Ask before adding anything beyond MonoGame, xUnit, SixLabors.ImageSharp.
- `src/Hexwaste.Formats` stays free of MonoGame references.
- **Golden-net discipline:** the golden scripts run the *prebuilt* binary. Never run two nets
  concurrently, never background one, and **never build while one is running** — a mid-run rebuild
  silently invalidates the results.
- Golden nets need a real display and game data (`FALLOUT2_DIR`, default `./game-data`).
- Every regression test must be **confirmed to fail against the pre-change code**. A test that passes
  both before and after proves nothing — that is exactly how three bugs survived the 2026-08-11 batch.

---

## File Structure

| File | Responsibility | Tasks |
|---|---|---|
| `src/Hexwaste.Formats/Combat/CombatEngine.cs` | `CritFailDamage` (F11), `ApplyAccidentalHit` (F12), `Explode` + the `DAM_EXPLODE` crit-fail branch (F13) | 1, 2, 3 |
| `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs` | all hermetic coverage; `FakeCombatHost` already records `DamageProcCalls` | 1, 2, 3 |
| `tests/golden-combat/arcaves-crit-fail-day6.txt` | the one deliberately re-recorded fixture | 1 |
| `docs/BACKLOG.md` | reconcile F11–F13; add the carried `ammoQuantity` divergence | 4 |

---

## Reference facts every task depends on

Verified against `e97087b` on 2026-08-15. Do not re-derive; do falsify if something looks wrong.

- `attackComputeCriticalFailure` (`combat.cc:4228-4232`) resolves self-damage as
  `attackComputeDamage(attack, ammoQuantity, 2)` for `DAM_HIT_SELF` and
  `attackComputeDamage(attack, 1, 2)` for `DAM_EXPLODE`. `DAM_RANDOM_HIT` uses the same shape at
  `combat.cc:3486`.
- Inside `attackComputeDamage`, the third argument is `bonusDamageMultiplier`: it multiplies at
  `:4586` and is undone by `damage /= 2` at `:4601`. **The pair is net ×1 — vanilla applies the full
  rolled damage.**
- `_damage_object(a1, damage, animated, a4, a5)` (`combat.cc:4821`) gates the damage proc as
  `if (!a4) { … scriptExecProc(a1->sid, SCRIPT_PROC_DAMAGE); }` (`:4848`), and it does so **before**
  the `DAM_DEAD` / destroy-proc block at `:4855`.
- `_check_ranged_miss` reassigns `attack->defender = critter` (the bystander) while `attack->oops`
  keeps the intended target from `:3485`. The defender call at `:4723` passes
  `attack->defender != attack->oops` → **true** for a collateral hit → **no damage proc**.

### Hexwaste facts the tests depend on

- `NewCritter(...)` gives **Luck 0**, so `CriticalFailure.Resolve`'s
  `chance = d100 − 5·(Luck − 5)` is `raw + 25`.
- `CriticalFailure.Severity`: `≤20→0, ≤50→1, ≤75→2, ≤95→3, else 4`.
- `CriticalTables.CritFailTable` is 7 rows × 5 columns, row-major
  (`CriticalTables.g.cs:401`). The entries this plan uses:
  - **row 1, col 4 = 65536 = `DAM_HIT_SELF`** → raw ≥ 71 (e.g. 80 → chance 105 → col 4)
  - **row 1, col 3 = 1048576 = `DAM_RANDOM_HIT`** → raw 51–70 (e.g. 60 → chance 85 → col 3)
  - **row 4, col 4 = 4096 = `DAM_EXPLODE`** → raw ≥ 71 (e.g. 80 → chance 105 → col 4)
- `SequenceRng` returns its listed values in order, then **repeats the last value, clamped** into each
  subsequent call's range. So `SequenceRng(100, 1, 80)` gives to-hit 100 (miss), upgrade 1
  (crit-fail), severity 80, and every later draw is 80 clamped — a `Next(5, 13)` weapon roll yields
  its max, 12.
- `MakeGun(ap: 5, critFailType: 0)` builds a **5–12 damage** gun; `critFailType` selects the
  `_cf_table` row.
- `FakeCombatHost.DamageProcCalls` is a `List<(MapObject Target, MapObject? Source, int Damage)>`
  appended by `RunDamageProc` — assert against it directly.
- `RollWeaponDamage` adds `attacker.MeleeDamage`, which is **0** for these fake critters.

---

## Task 1: F11 — crit-failure self-damage at full vanilla strength

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` — `CritFailDamage`, ~`:1224-1238`
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs` — rewrite
  `HitSelfFumbleStillRollsWeaponDamage` (`:2476`), add `RandomHitFumbleAppliesFullWeaponDamage`
- Re-record: `tests/golden-combat/arcaves-crit-fail-day6.txt`

**Interfaces:**
- Consumes: `CombatMath.RollWeaponDamage(rng, attacker, target, minDamage, maxDamage, critMultiplier, bypassArmor, extraDr)`
  and `CombatMath.RollDamage(rng, attacker, target, critMultiplier, bypassArmor, extraDr)` — unchanged signatures.
- Produces: nothing new. `CritFailDamage` keeps its signature
  `void CritFailDamage(CritterState attacker, CritterState victimState, ProtoInfo? weaponProto, string tag)`.

- [ ] **Step 1: Update the existing pin to the vanilla figure**

In `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`, replace the body comment and the final
assertion of `HitSelfFumbleStillRollsWeaponDamage`. The comment currently explains the halving as a
known bug; it now explains the port. Replace the comment block (the lines from
`// The other half of the self-damage branch` through `// test pins TODAY's behaviour; see docs/BACKLOG.md F11 for the fix, which moves fixtures.`)
with:

```csharp
        // The other half of the self-damage branch (combat.cc:4228-4232 at our pinned e97087b):
        // DAM_HIT_SELF keeps the full weapon-damage roll (and takes NO 1-5 roll). _cf_table row 1
        // col 4 = 65536 = DAM_HIT_SELF exactly, so a gun whose criticalFailureType is 1 fumbling at
        // max severity self-hits. SequenceRng: to-hit 100 (miss), upgrade 1 (crit-fail), severity raw
        // 80 → chance = 80 + 25 = 105 → col 4; later draws repeat 80 clamped, so the 5-12 weapon roll
        // yields its max, 12 — and all 12 land, because attackComputeDamage(attack, n, 2) multiplies
        // by bonusDamageMultiplier 2 (combat.cc:4586) and then divides by 2 (:4601), i.e. x1: vanilla
        // self-damage is the FULL rolled figure. (F11: this asserted 30 − 6 until 2026-08-15, when
        // CritFailDamage stopped passing critMultiplier: 1 into `raw * critMultiplier / 2`.)
```

and change the final assertion:

```csharp
        Assert.Equal(18, dude.CurrentHp);           // 30 − 12, the FULL weapon-damage roll
```

- [ ] **Step 2: Add the `DAM_RANDOM_HIT` sibling test**

Immediately after `HitSelfFumbleStillRollsWeaponDamage`, add:

```csharp
    [Fact]
    public void RandomHitFumbleAppliesFullWeaponDamageToTheWildVictim()
    {
        // The OTHER caller of CritFailDamage. DAM_RANDOM_HIT takes the same shape as DAM_HIT_SELF in
        // the reference — attackComputeDamage(attack, ammoQuantity, 2) at combat.cc:3486 — so its
        // victim also takes the full rolled figure, not half. _cf_table row 1 col 3 = 1048576 =
        // DAM_RANDOM_HIT exactly; raw 60 → chance = 60 + 25 = 85 → col 3. Later draws repeat 60
        // clamped: the pool index Next(0, 1) → 0, and the 5-12 weapon roll → 12.
        var host = new FakeCombatHost
        {
            CriticalsEnabled = true,
            DudeCritFailuresEnabled = true,
            LoadedAmmoCount = 10,
            Equipped = MakeGun(critFailType: 1),
        };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        var engine = new CombatEngine(host, new SequenceRng(100, 1, 60));

        Assert.True(engine.TryAttack(enemy));

        Assert.Contains(host.Transcripts, t => t.StartsWith("crit-fail-random-hit:"));
        Assert.Equal(88, enemy.CurrentHp); // 100 − 12, the FULL weapon-damage roll (was 100 − 6)
    }
```

- [ ] **Step 3: Run both tests to verify they FAIL against the pre-change code**

Run:
```bash
dotnet test --filter "FullyQualifiedName~HitSelfFumbleStillRollsWeaponDamage|FullyQualifiedName~RandomHitFumbleAppliesFullWeaponDamage"
```
Expected: **both FAIL** — `Assert.Equal() Failure: Expected 18, Actual 24` and
`Expected 88, Actual 94`. If either passes here, **stop**: the test is not exercising the branch and
proves nothing. Report which one and why before continuing.

- [ ] **Step 4: Make the change**

In `src/Hexwaste.Formats/Combat/CombatEngine.cs`, `CritFailDamage`. Replace the two
`CombatMath` calls' `1` with `2`, and replace the summary/comment above the method to state the port
rather than the simplification. The method becomes:

```csharp
    /// <summary>Direct crit-failure damage to a victim (DAM_HIT_SELF or the wild RANDOM_HIT): the weapon's
    /// rolled damage (no ammo mods — a documented simplification), applied straight to HP with a kill
    /// check.</summary>
    // ported from fallout2-ce src/combat.cc attackComputeCriticalFailure() (community fix #675).
    // The reference rolls weapon damage (attackComputeDamage) ONLY for DAM_HIT_SELF and DAM_EXPLODE;
    // DAM_HURT_SELF is a separate branch that just adds randomBetween(1, 5) to attackerDamage — which
    // starts at 0 — so a HURT_SELF fumble is worth exactly 1-5 and takes no damage roll. This method is
    // the HIT_SELF/RANDOM_HIT half; the HURT_SELF half calls ApplyCritFailDamage directly with the 1-5.
    // ported from fallout2-ce src/combat.cc attackComputeDamage(): the reference passes
    // bonusDamageMultiplier = 2 (combat.cc:4230 for HIT_SELF, :3486 for RANDOM_HIT), which multiplies at
    // :4586 and is undone by the `damage /= 2` at :4601 — a net x1, i.e. the FULL rolled figure. Our
    // critMultiplier feeds the same `raw * critMultiplier / 2` shape, so 2 is what reproduces vanilla;
    // passing 1 halved every crit-failure hit (F11, fixed 2026-08-15 with a deliberate re-record).
    // CARRIED DIVERGENCE: for a RANGED fumble the reference rolls attack->ammoQuantity times
    // (a burst self-hits once per round); we roll once. Changing the roll COUNT changes the RNG draw
    // count, so it is its own cycle — see docs/BACKLOG.md.
    private void CritFailDamage(CritterState attacker, CritterState victimState, ProtoInfo? weaponProto, string tag)
    {
        int dmg = weaponProto?.Weapon is { } w
            ? CombatMath.RollWeaponDamage(_rng, attacker, victimState, w.MinDamage, w.MaxDamage, 2, false, 0)
            : CombatMath.RollDamage(_rng, attacker, victimState, 2, false, 0);
        ApplyCritFailDamage(attacker, victimState, dmg, weaponProto, tag);
    }
```

- [ ] **Step 5: Run the full unit suite**

Run: `dotnet test`
Expected: **0 failed.** If a test other than the two above changed outcome, that is new information —
report it before proceeding rather than adjusting the test to match.

- [ ] **Step 6: Measure the fixture blast radius BEFORE recording**

Run: `scripts/combat-golden.sh check`
Expected: `arcaves-crit-fail-day6` fails; **everything else passes.**

Two stop conditions:
- If **nothing** fails, the change is not reaching any fixture. Do not record. Report it — F11 was
  predicted to move this fixture, and a no-op means the prediction was wrong somewhere.
- If a fixture **other than** `arcaves-crit-fail-day6` fails, stop and report the list. The blast
  radius was predicted as exactly one; more means the change does something beyond the multiplier.

- [ ] **Step 7: Construct the justification BEFORE recording**

Diff the failing fixture against the run output and write down, in prose:
- which crit-failure hit changed, and from what damage to what damage;
- whether the larger figure changed a kill, and therefore a round count or a survivor;
- why the new number is what `attackComputeDamage(attack, n, 2)` produces.

**If that trace cannot be constructed from the transcript, do not record.** Revert the change, report
that the delta is unexplained, and F11 returns to deferred. An unexplained delta accepted because
"the port looks faithful" is how a bug gets laundered into the baseline.

- [ ] **Step 8: Re-record, then verify exactly one fixture moved**

Run:
```bash
scripts/combat-golden.sh record
git status --short tests/golden-combat/
```
Expected: **exactly one modified file**, `tests/golden-combat/arcaves-crit-fail-day6.txt`. More than
one is a stop condition, not something to accept — `record` rewrites every fixture, so a second
modified file means a second behavioural change slipped in.

Then re-run `scripts/combat-golden.sh check` — expected: **ALL PASS**.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Formats/Combat/CombatEngine.cs \
        tests/Hexwaste.Formats.Tests/CombatEngineTests.cs \
        tests/golden-combat/arcaves-crit-fail-day6.txt
git commit -m "fix: crit-failure self-damage at full vanilla strength (F11)

<the Step 7 trace goes in this body — which hit changed, by how much, and
whether it changed a kill or a round count>

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: F12 — the collateral victim of a missed shot runs no `damage_p_proc`

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` — `ApplyAccidentalHit`, `:721-743`
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs` — add two tests after
  `MissedSingleShotWithClearOvershootHitsNobody` (`:2039`)

**Interfaces:**
- Consumes: `AccidentalHit(MapObject Victim, int Damage)` (private record, `:44`),
  `ICombatHost.RunDamageProc(MapObject target, MapObject? source, int damage)`.
- Produces: nothing new — `ApplyAccidentalHit` keeps its signature.

- [ ] **Step 1: Write the failing test**

Add after `MissedSingleShotWithClearOvershootHitsNobody`:

```csharp
    [Fact]
    public void MissedShotsCollateralVictimRunsNoDamageProc()
    {
        // F12, ported from fallout2-ce src/combat.cc _damage_object() (:4821): _check_ranged_miss
        // reassigns attack->defender to the bystander it struck, while attack->oops keeps the INTENDED
        // target (set at :3485). The defender's damage call at :4723 therefore passes
        // `attack->defender != attack->oops` = TRUE, and _damage_object gates the proc as `if (!a4)`
        // (:4848) — so a collateral victim runs NO damage_p_proc. It still takes the HP loss and still
        // runs the on-hit path; only the damage proc is suppressed.
        int from = 20100;
        int target = HexGrid.TileInDirection(from, 0, 3);

        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(from, hp: 30, ap: 12, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(target, hp: 500));
        (ProtoInfo proto, MapObject item) = MakeBurstWeapon(rounds: 10, apCost2: 6, minDmg: 10, maxDmg: 10, maxRange: 40);
        host.Equipped = (proto, item);

        int endpoint = HexGrid.TileNumBeyond(from, target, 40);
        var line = new List<int>();
        LineOfFire.Trace(target, endpoint, t => { line.Add(t); return null; });
        Assert.True(line.Count > 1, "there must be an overshoot tile beyond the target");
        MapObject bystander = host.AddCritter(NewCritter(line[1], hp: 500));
        bystander.Sid = 7; // scripted: a damage_p_proc COULD run — the point is that it must not

        host.BlockerOverride = tile => host.CombatCritters.FirstOrDefault(c => c.HexTile == tile && !c.IsDead);

        var engine = new CombatEngine(host, new SequenceRng(100));
        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.True(bystander.CurrentHp < 500, "the overshoot should still have struck the bystander");
        Assert.DoesNotContain(host.DamageProcCalls, c => c.Target == bystander);
    }
```

- [ ] **Step 2: Run it to verify it FAILS**

Run:
```bash
dotnet test --filter "FullyQualifiedName~MissedShotsCollateralVictimRunsNoDamageProc"
```
Expected: **FAIL** on the `Assert.DoesNotContain` — the collateral victim currently runs the proc.
If it passes, stop: the test is not reaching `ApplyAccidentalHit`.

- [ ] **Step 3: Make the change**

In `ApplyAccidentalHit`, delete the three lines

```csharp
        if (acc.Victim != dude && acc.Victim.Sid != -1)
            foreach (string line in _host.RunDamageProc(acc.Victim, attacker, acc.Damage))
                _host.Log(line);
```

and replace the method's summary comment with one that records why:

```csharp
    /// <summary>Apply a missed shot's accidental bystander hit (mirrors ApplyBurstExtras — HP, kill /
    /// on-hit proc; the dude routes to GameOver). NO damage_p_proc: see below.</summary>
    // ported from fallout2-ce src/combat.cc _damage_object() (:4821) + _check_ranged_miss(): the miss
    // reassigns attack->defender to the bystander while attack->oops keeps the INTENDED target
    // (:3485), so the defender damage call at :4723 passes `defender != oops` = true and the proc gate
    // `if (!a4)` (:4848) skips SCRIPT_PROC_DAMAGE entirely. The collateral victim takes the HP loss and
    // the on-hit path, but never its damage proc (F12, fixed 2026-08-15). The fork's PR #493 inverts a
    // DIFFERENT call site's polarity and does not change this branch's outcome.
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test`
Expected: **0 failed** — including `MissedSingleShotHitsABystanderInTheOvershootLine`, which asserts
only HP and must be unaffected.

- [ ] **Step 5: Add the boundary test — the ordinary defender still runs its proc**

The suppression must not leak to the normal hit path. `NpcSelfDamageFumbleRunsItsOwnDamageProc`
(`:2510`) already proves the self-damage proc still fires. Confirm an ordinary *defender* proc still
fires by running the existing coverage:

```bash
dotnet test --filter "FullyQualifiedName~DamageProc"
```
Expected: **0 failed.** If no existing test covers an ordinary defender's `damage_p_proc`, write one
in the same shape as the test above — an enemy with `Sid = 7` hit by a *landed* shot
(`SequenceRng(1)` for a to-hit success), asserting `host.DamageProcCalls` **does** contain it — and
confirm it passes both before and after this change (it is a boundary pin, not a regression test, so
passing on both sides is correct here).

- [ ] **Step 6: Run the combat golden net**

Run: `scripts/combat-golden.sh check`
Expected: **ALL PASS, 16/16 — no fixture moves.** F12 only removes a script proc, and the fixture
critters would have to define `damage_p_proc` for anything to differ.

**If a fixture moves, stop and report.** It means a `damage_p_proc` with observable side effects was
running on a collateral victim, which is worth understanding before it is recorded away.

- [ ] **Step 7: Commit**

```bash
git add src/Hexwaste.Formats/Combat/CombatEngine.cs tests/Hexwaste.Formats.Tests/CombatEngineTests.cs
git commit -m "fix: a missed shot's collateral victim runs no damage_p_proc (F12)

_check_ranged_miss reassigns attack->defender to the bystander while
attack->oops keeps the intended target (combat.cc:3485), so the defender
damage call at :4723 passes defender != oops and _damage_object's \`if (!a4)\`
gate (:4848) skips SCRIPT_PROC_DAMAGE. ApplyAccidentalHit ran it
unconditionally. HP loss, the on-hit path and the kill path are unchanged.

No fixture moved: combat-golden 16/16.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: F13 — the `DAM_EXPLODE` crit-failure branch runs its self-damage proc

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` — `Explode` (`:1598`) and the `DAM_EXPLODE`
  crit-fail branch (`:1193`)
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs` — add after
  `NpcSelfDamageFumbleRunsItsOwnDamageProc` (`:2534`)

**Interfaces:**
- Consumes: `ICombatHost.RunDamageProc`, `ICombatHost.PartyMembers`, `ICombatHost.Dude`.
- Produces: `Explode` gains a trailing optional parameter —
  `public void Explode(int centerTile, MapObject? killer, int minDamage, int maxDamage, int radius, MapObject? selfDamageProcFor = null)`.
  The default `null` reproduces today's behaviour exactly, so every existing caller and the viewer
  are inert by construction.

**Why a parameter and not a return value:** the reference runs the damage proc *inside*
`_damage_object`, i.e. **before** the `DAM_DEAD` destroy-proc block (`:4848` precedes `:4855`).
Running it after `Explode` returns would fire it after `KillCritter` had already run the destroy
proc — the wrong order. The proc must fire inside the victim loop.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void ExplodeFumbleRunsTheSelfDamagedAttackersDamageProc()
    {
        // F13: PR #493's self-damage proc was wired into ApplyCritFailDamage, which only the
        // DAM_HIT_SELF branch reaches. The sibling DAM_EXPLODE branch routes to Explode() and reached
        // no proc at all — where the reference's attackComputeDamage(attack, 1, 2) self-damage feeds
        // the same _apply_damage path (combat.cc:4230). _cf_table row 4 col 4 = 4096 = DAM_EXPLODE
        // exactly, so a critFailType-4 weapon fumbling at max severity detonates.
        var host = new FakeCombatHost
        {
            CriticalsEnabled = true,
            LoadedAmmoCount = 10,
            Equipped = MakeGun(critFailType: 4),
        };
        host.SetDude(NewCritter(20100, hp: 100, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 60, ap: 10));
        enemy.Sid = 7; // a scripted, unaffiliated NPC: its damage_p_proc can run
        var rng = new RecordingRng(new SequenceRng(100, 1, 100, 1, 80));
        var engine = new CombatEngine(host, rng);

        Assert.True(engine.TryAttack(enemy)); // open combat
        host.Animating.Clear();
        engine.ProcessAnimations();
        engine.EndPlayerTurn();
        for (int i = 0; i < 200 && engine.Phase == CombatPhase.EnemyTurn; i++)
        {
            host.Animating.Clear();
            engine.Step();
        }

        Assert.Contains(host.Transcripts, t => t.StartsWith("crit-fail: ") && t.Contains("flags=0x1000"));
        Assert.Contains(host.DamageProcCalls, c => c.Target == enemy && c.Source == enemy);
    }
```

**Note for the implementer:** the RNG sequence above mirrors
`NpcSelfDamageFumbleRunsItsOwnDamageProc`'s shape (to-hit miss, upgrade, to-hit miss, upgrade,
severity), with the severity value raised to 80 so it buckets to column 4. The enemy now carries a
gun rather than fists, so the AI may draw a different number of values before the fumble. **Derive
the real sequence empirically** — the `RecordingRng` wrapper is already in place, so print
`rng.Draws` and adjust — and then replace this note with a comment naming what each listed draw is.
The required outcome is fixed and non-negotiable: `_cf_table` row 4, column 4, i.e. flags `0x1000`.
Assert the `crit-fail:` transcript flags so the test cannot silently drift onto a different branch.

- [ ] **Step 2: Run it to verify it FAILS**

Run:
```bash
dotnet test --filter "FullyQualifiedName~ExplodeFumbleRunsTheSelfDamagedAttackersDamageProc"
```
Expected: **FAIL** on the `DamageProcCalls` assertion, with the `crit-fail: … flags=0x1000`
assertion **passing** — that ordering matters. If the flags assertion is what fails, the setup is not
reaching the explode branch and the sequence needs fixing first; a test that fails for the wrong
reason proves nothing.

- [ ] **Step 3: Give `Explode` the optional self-proc parameter**

Change the signature:

```csharp
    public void Explode(int centerTile, MapObject? killer, int minDamage, int maxDamage, int radius,
        MapObject? selfDamageProcFor = null)
```

and add the proc inside the victim loop, immediately after the HP loss and its log/transcript lines
and **before** the `Shove` and the kill check:

```csharp
            victim.CurrentHp -= damage;
            _host.Log($"The blast hits the {_host.ObjectName(victim)} for {damage} damage.");
            _host.Transcript($"explosion-hit: {_host.ObjectName(victim)}@{victim.HexTile} damage={damage}");

            // ported from fallout2-ce src/combat.cc _damage_object() (:4848, community fix #493): the
            // DAM_EXPLODE crit-failure branch self-damages through attackComputeDamage(attack, 1, 2)
            // (:4230) and lands in the same _apply_damage path as DAM_HIT_SELF, so the fumbling critter
            // runs its own damage_p_proc — with itself as both damaged object and source. The proc is
            // skipped when object and source are both party members, which for self-damage means every
            // party member including the dude, so only an unaffiliated critter runs it. It fires BEFORE
            // the kill check because the reference's proc gate (:4848) precedes its DAM_DEAD destroy
            // block (:4855). selfDamageProcFor is null for every other caller — an ordinary blast has no
            // self-damaged attacker — so this is inert by construction (F13, fixed 2026-08-15).
            if (victim == selfDamageProcFor && victim.Sid != -1
                && victim != _host.Dude && !_host.PartyMembers.Contains(victim))
                foreach (string line in _host.RunDamageProc(victim, victim, damage))
                    _host.Log(line);
```

- [ ] **Step 4: Pass the attacker at the crit-failure call site**

At `:1193`, change:

```csharp
        else if ((flags & CriticalTables.DamExplode) != 0)
            Explode(self.HexTile, self, weaponProto?.Weapon?.MinDamage ?? 1, weaponProto?.Weapon?.MaxDamage ?? 6, 1,
                selfDamageProcFor: self);
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test`
Expected: **0 failed**, and the new test now passes.

- [ ] **Step 6: Run the combat golden net**

Run: `scripts/combat-golden.sh check`
Expected: **ALL PASS, 16/16.** F13 adds a script proc on a branch the fixtures reach only if a
fixture critter fumbles into `DAM_EXPLODE` *and* has a `damage_p_proc`. If a fixture moves, stop and
report which — it is real information about what the fixtures cover.

- [ ] **Step 7: Commit**

```bash
git add src/Hexwaste.Formats/Combat/CombatEngine.cs tests/Hexwaste.Formats.Tests/CombatEngineTests.cs
git commit -m "fix: the DAM_EXPLODE crit-failure branch runs its self-damage proc (F13)

PR #493's port wired the party-gated self-damage damage_p_proc into
ApplyCritFailDamage, which only DAM_HIT_SELF reaches; the sibling
DAM_EXPLODE branch routes to Explode() and ran no proc at all. Explode
gains an optional selfDamageProcFor (null for every other caller, so
inert by construction) and fires the proc inside the victim loop, before
the kill check — matching the reference, whose proc gate (combat.cc:4848)
precedes its DAM_DEAD destroy block (:4855).

No fixture moved: combat-golden 16/16.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Reconcile the backlog and run the full nets

**Files:**
- Modify: `docs/BACKLOG.md` — the F11, F12, F13 entries

- [ ] **Step 1: Move F11–F13 to shipped**

Rewrite each of the three entries to state what was ported, with the commit SHA from its task, and —
for F11 only — that `tests/golden-combat/arcaves-crit-fail-day6.txt` was **deliberately re-recorded**,
so a future reader knows the baseline changed by intent rather than drift. State plainly that F12 and
F13 moved no fixture.

- [ ] **Step 2: Add the carried divergence**

Add a new entry recording what Task 1 deliberately did not do: the reference rolls a **ranged**
`DAM_HIT_SELF` `attack->ammoQuantity` times (`combat.cc:4229` — a burst fumble self-hits once per
round) and `DAM_RANDOM_HIT` likewise (`:3486`); Hexwaste rolls once. Mark it **re-record tier**:
changing the roll count changes the RNG draw count, which is a materially larger blast radius than
changing a multiplier.

- [ ] **Step 3: Leave F14 alone**

F14 stays as written — an ordering divergence the shipped `_cf_table` makes unreachable
(`DAM_CRIP_RANDOM` appears exactly once, paired with nothing). It is documentation, not work.

- [ ] **Step 4: Run the remaining nets**

**One at a time. Nothing else may build while these run.**

```bash
scripts/quest-golden.sh check
scripts/encounter-golden.sh check
```
Expected: **ALL PASS** for both. Any movement outside the combat suite is a stop condition for all
three items — none of these changes should reach a quest or encounter transcript.

- [ ] **Step 5: Commit**

```bash
git add docs/BACKLOG.md
git commit -m "docs: reconcile F11-F13 and record the ammoQuantity self-hit divergence

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:** F11 → Task 1; F12 → Task 2; F13 → Task 3; the docs/backlog section and the
  four-suite run → Task 4; F14's explicit exclusion → Task 4 Step 3. The spec's "what carries the
  proof" list of six tests maps to: (1)(3) Task 1 Step 1, (2) Task 1 Step 2, (4) Task 2 Step 1,
  (5) Task 2 Step 5, (6) Task 3 Step 1.
- **Escape hatch:** the spec's "if the damage dealt cannot be recovered from `Explode` without
  restructuring, stop" is resolved in the plan — the optional parameter recovers it inside the loop,
  which is also the only placement that matches the reference's proc-before-destroy order.
- **Known soft spot:** Task 3's RNG sequence is the one value in this plan not verified by execution.
  It is flagged inline with the invariant that must hold (`flags=0x1000`) and an assertion that fails
  loudly if the setup drifts onto another branch.
