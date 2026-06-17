using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Hex;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// The CI-friendly net the phase-9 M0 extraction unlocks: drives the turn machine
/// through a fake <see cref="ICombatHost"/> with a scripted RNG — no GraphicsDevice,
/// no game data. Locks the §A AP-resets and the §B roll-now/apply-on-completion
/// split from docs/phase9-research-report.md.
/// </summary>
public class CombatEngineTests
{
    [Fact]
    public void DamageAppliesOnAnimationCompletionAndXpPaysAtCombatEnd()
    {
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        int enemyTile = HexGrid.TileInDirection(20100, 0);
        MapObject enemy = host.AddCritter(NewCritter(tile: enemyTile, hp: 1, exp: 50));
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(enemy));

        // §B: the outcome is rolled now (transcript out, AP spent) but damage is
        // NOT applied until the swing animation finishes.
        Assert.Equal(CombatPhase.PlayerTurn, engine.Phase);
        Assert.Equal(7, engine.DudeAp); // 10 max − 3 punch
        Assert.Equal(1, enemy.CurrentHp); // not applied yet
        Assert.Contains(host.Transcripts, t => t.StartsWith("attack "));
        Assert.DoesNotContain(host.Logs, l => l.Contains("dies"));

        // Swing finishes → damage lands, the critter dies, but XP is still pending.
        host.Animating.Clear();
        engine.ProcessAnimations();
        Assert.True(enemy.IsDead);
        Assert.Equal(0, host.XpAwarded);

        // Next step ends combat (nothing hostile standing) and pays XP.
        engine.Step();
        Assert.Equal(CombatPhase.Idle, engine.Phase);
        Assert.Equal(10, engine.DudeAp); // §A: EndCombat resets AP to max
        Assert.Equal(50, host.XpAwarded);
    }

    [Fact]
    public void BonusHthAttacksPerkLowersUnarmedApCost()
    {
        // P28-M3: Bonus HtH Attacks → −1 AP per melee/unarmed swing (item.cc:1693). A punch is
        // 3 AP, so 10 − 2 = 8 left (vs 7 without the perk — the first test's baseline).
        var host = new FakeCombatHost();
        host.PerkRanks[Hexwaste.Formats.Perks.PerkId.BonusHthAttacks] = 1;
        host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 1));
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(enemy));
        Assert.Equal(8, engine.DudeAp);
    }

    [Fact]
    public void SlayerPerkMakesEveryMeleeHitCritical()
    {
        // P28-M3: Slayer turns a melee/unarmed SUCCESS into a critical (combat.cc:3866). RNG:
        // to-hit(1)=hit, normal crit-roll(100)=miss, Slayer forces crit, severity(30), massive(10).
        var host = new FakeCombatHost { CriticalsEnabled = true };
        host.PerkRanks[Hexwaste.Formats.Perks.PerkId.Slayer] = 1;
        host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 500));
        var engine = new CombatEngine(host, new SequenceRng(1, 100, 30, 10));

        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();
        Assert.Contains(host.Logs, l => l.Contains("Critical hit!"));
    }

    [Fact]
    public void WithoutSlayerTheSameRollIsNotCritical()
    {
        // Control: the identical RNG without the perk leaves the failed crit-roll a plain hit.
        var host = new FakeCombatHost { CriticalsEnabled = true };
        host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 500));
        var engine = new CombatEngine(host, new SequenceRng(1, 100, 30, 10));

        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();
        Assert.DoesNotContain(host.Logs, l => l.Contains("Critical hit!"));
    }

    [Fact]
    public void ScriptedAmbushResetsDudeApAndOpensOnEnemyTurn()
    {
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject attacker = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30));
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(attacker, dude);

        Assert.Equal(CombatPhase.EnemyTurn, engine.Phase);
        Assert.Equal(10, engine.DudeAp); // §A: ambush hands control back at full AP
        Assert.Contains(host.Transcripts, t => t.StartsWith("scripted-aggro:"));
    }

    [Fact]
    public void RoundRolloverResetsDudeApAndEnemyRetaliates()
    {
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(enemy));     // open combat
        host.Animating.Clear();
        engine.ProcessAnimations();               // resolve the dude's swing
        engine.EndPlayerTurn();
        Assert.Equal(CombatPhase.EnemyTurn, engine.Phase);

        // Pump the enemy turn to completion (clear animations each step).
        for (int i = 0; i < 200 && engine.Phase == CombatPhase.EnemyTurn; i++)
        {
            host.Animating.Clear();
            engine.Step();
        }

        Assert.Equal(CombatPhase.PlayerTurn, engine.Phase);
        Assert.Equal(2, engine.Round);
        Assert.Equal(10, engine.DudeAp);          // §A: new round resets AP to max
        Assert.True(dude.CurrentHp < 30);         // the enemy hit back
        Assert.False(dude.IsDead);
    }

    [Fact]
    public void MassiveCriticalAppliesTheStatusOnAFailedDefenderRoll()
    {
        // P14-M4: a day-2 aimed HEAD crit (MAN sev1 = {4, BYPASS, EN, 0, KNOCKED_OUT}).
        // The defender's EN is 0, so any d10 massive roll fails → KNOCKED_OUT applies.
        // RNG order: to-hit(1) → crit-roll(1) → severity(30→sev1) → massive d10(10>0 fail).
        var host = new FakeCombatHost { CriticalsEnabled = true };
        host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 500, endurance: 0));
        var engine = new CombatEngine(host, new SequenceRng(1, 1, 30, 10));

        Assert.True(engine.TryAttack(enemy, hitLocation: 0)); // aim HEAD
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.True((enemy.CombatResults & CriticalTables.DamKnockedOut) != 0);
        Assert.Contains(host.Transcripts, t => t.StartsWith("knockout:"));
        Assert.False(enemy.IsDead); // KO'd, not killed (500 HP)
    }

    [Fact]
    public void MassiveCriticalIsResistedByAHighStatDefender()
    {
        // Same crit, but EN 10 → the massive d10 (1) <= 10 → SUCCESS (resisted), no KO.
        var host = new FakeCombatHost { CriticalsEnabled = true };
        host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 500, endurance: 10));
        var engine = new CombatEngine(host, new SequenceRng(1, 1, 30, 1)); // massive d10 = 1

        Assert.True(engine.TryAttack(enemy, hitLocation: 0));
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.True((enemy.CombatResults & CriticalTables.DamKnockedOut) == 0); // resisted
    }

    [Fact]
    public void KnockedOutEnemyForfeitsItsTurn()
    {
        // P14-M2: a knocked-out enemy skips its turn (combat.cc:3231) and stays
        // unconscious (EN 0 → 350-tick wake = 7 rounds), so the dude is unharmed.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(enemy)); // open combat
        host.Animating.Clear();
        engine.ProcessAnimations();
        engine.KnockOut(enemy);
        Assert.True((enemy.CombatResults & CriticalTables.DamKnockedOut) != 0);

        engine.EndPlayerTurn();
        for (int i = 0; i < 200 && engine.Phase == CombatPhase.EnemyTurn; i++)
        {
            host.Animating.Clear();
            engine.Step();
        }

        Assert.Equal(2, engine.Round);
        Assert.Equal(30, dude.CurrentHp);                                       // KO'd → never swung
        Assert.True((enemy.CombatResults & CriticalTables.DamKnockedOut) != 0); // still out (350 > 50)
        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("enemy-attack"));
    }

    [Fact]
    public void KnockedOutEnemyWakesAfterTheDelayAndFightsAgain()
    {
        // EN 10 → 10*(35-30) = 50-tick wake = exactly one round; then it attacks.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, endurance: 10));
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();
        engine.KnockOut(enemy);

        engine.EndPlayerTurn();
        for (int i = 0; i < 400 && !dude.IsDead && dude.CurrentHp == 30; i++)
        {
            host.Animating.Clear();
            engine.Step();
            if (engine.Phase == CombatPhase.PlayerTurn) engine.EndPlayerTurn(); // skip the player turn
        }

        Assert.Contains(host.Transcripts, t => t.StartsWith("wake:"));            // it came to
        Assert.True((enemy.CombatResults & CriticalTables.DamKnockedOut) == 0);   // no longer out
        Assert.True(dude.CurrentHp < 30);                                         // and resumed attacking
    }

    [Fact]
    public void LoseTurnEnemySkipsOneTurnThenActs()
    {
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();
        enemy.CombatResults |= CriticalTables.DamLoseTurn; // a crit dazed it

        engine.EndPlayerTurn();                            // round 1 enemy turn: skipped
        for (int i = 0; i < 200 && engine.Phase == CombatPhase.EnemyTurn; i++)
        {
            host.Animating.Clear();
            engine.Step();
        }
        Assert.Equal(30, dude.CurrentHp);                                  // skipped this round
        Assert.True((enemy.CombatResults & CriticalTables.DamLoseTurn) == 0); // one-shot cleared
        Assert.Contains(host.Transcripts, t => t.StartsWith("skip-turn:"));
    }

    [Fact]
    public void WoundedEnemyBelowMinHpFleesInsteadOfAttacking()
    {
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        int eTile = HexGrid.TileInDirection(20100, 0);
        MapObject enemy = host.AddCritter(NewCritter(tile: eTile, hp: 5, ap: 10));
        host.AiPackets[enemy] = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 10, 0, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude); // opens on the enemy's turn
        engine.Step();

        Assert.Contains(host.Transcripts, t => t.StartsWith("flee:"));
        Assert.True(HexGrid.Distance(enemy.HexTile, dude.HexTile) > 1); // backed away
        Assert.Equal(30, dude.CurrentHp);                               // did not attack
    }

    [Fact]
    public void EnemyThatCanNeverClearMinToHitFlees()
    {
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(99, "Hopeless", MinToHit: 99, MinHp: 0, 0, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        // unarmed to-hit (50) can never reach 99 → flee, never swing.
        Assert.Contains(host.Transcripts, t => t.StartsWith("flee:"));
        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("enemy-attack"));
        Assert.Equal(30, dude.CurrentHp);
    }

    [Fact]
    public void EnemyWithAchievableMinToHitStillAttacks()
    {
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 40, MinHp: 0, 0, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        // unarmed 50 ≥ 40 → attacks (rolled, not yet applied), no flee.
        Assert.Contains(host.Transcripts, t => t.StartsWith("enemy-attack"));
        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("flee:"));
    }

    [Fact]
    public void CriticalsStayOffUntilEnabled()
    {
        // Same MinRng, criticals disabled: a plain swing, no crit tag, base damage.
        var host = new FakeCombatHost { CriticalsEnabled = false };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.DoesNotContain(host.Transcripts, t => t.Contains("CRITICAL"));
        Assert.Equal(99, enemy.CurrentHp); // unarmed floor damage 1, no multiplier
    }

    [Fact]
    public void CriticalHitAppliesTheTableMultiplier()
    {
        var host = new FakeCombatHost { CriticalsEnabled = true };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        var engine = new CombatEngine(host, new MinRng()); // forces hit → crit, severity 0

        Assert.True(engine.TryAttack(enemy)); // UNCALLED
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.Contains(host.Transcripts, t => t.Contains("CRITICAL"));
        // damage = floor-roll 1 × multiplier / 2 (no armor); multiplier from the table.
        int mult = CriticalTables.Lookup(0, CriticalTables.LocationUncalled, 0, false).DamageMultiplier;
        Assert.Equal(100 - 1 * mult / 2, enemy.CurrentHp);
    }

    [Fact]
    public void DeadCriticalKillsRegardlessOfHp()
    {
        var host = new FakeCombatHost { CriticalsEnabled = true };
        // betterCrit 100 forces severity 5; aimed HEAD → MAN/HEAD/5 = DAM_DEAD.
        host.SetDude(NewCritter(20100, hp: 30, ap: 10, betterCrit: 100));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 1000));
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(enemy, hitLocation: 0)); // HEAD
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.True(enemy.IsDead); // instant kill despite 1000 HP
        Assert.Contains(host.Transcripts, t => t.Contains("CRITICAL(kill)"));
    }

    [Fact]
    public void AimedShotIsHarderAndCostsAnExtraAp()
    {
        var host = new FakeCombatHost { CriticalsEnabled = false };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        var engine = new CombatEngine(host, new MinRng());

        engine.TryAttack(enemy, hitLocation: 6); // EYES: -60 ranged / -30 melee
        Assert.Equal(6, engine.DudeAp);          // punch 3 + aimed 1 → 10 − 4
        Assert.Contains(host.Transcripts, t => t.Contains("chance=20%")); // 50 − 30
    }

    [Fact]
    public void BigMeleeHitShovesTheTargetAlongTheHexLine()
    {
        var host = new FakeCombatHost { CriticalsEnabled = false };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10, meleeDmg: 30));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 200));
        int start = enemy.HexTile;
        // SequenceRng: roll1=1 (hit), damage roll high → ~32 dmg → shove 3 tiles.
        var engine = new CombatEngine(host, new SequenceRng(1, 99));

        Assert.True(engine.TryAttack(enemy)); // unarmed (melee) → knockback eligible
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.Contains(host.Transcripts, t => t.StartsWith("knockback:"));
        Assert.True(HexGrid.Distance(enemy.HexTile, start) >= 2); // shoved away
        Assert.False(enemy.IsDead);
    }

    [Fact]
    public void GunsDoNotKnockBack()
    {
        // CanKnockback is false for guns — but the fake host gives fists, so this
        // documents the gate via the engine: an unarmed small hit (dmg < 10) never
        // shoves regardless. (Gun-specific path is covered by the !isGun flag.)
        var host = new FakeCombatHost { CriticalsEnabled = false };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        int start = enemy.HexTile;
        var engine = new CombatEngine(host, new MinRng()); // 1 dmg → shove 0

        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("knockback:"));
        Assert.Equal(start, enemy.HexTile);
    }

    [Fact]
    public void CritKnockdownPersistsIsEasierToHitAndStandsUp()
    {
        var host = new FakeCombatHost { CriticalsEnabled = true };
        host.SetDude(NewCritter(20100, hp: 30, ap: 12));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100, ap: 10));
        var engine = new CombatEngine(host, new MinRng());

        // Aimed RIGHT_LEG: MAN/RIGHT_LEG/sev0 = { 3, DAM_KNOCKED_DOWN } → prone.
        Assert.True(engine.TryAttack(enemy, hitLocation: 4));
        host.Animating.Clear();
        engine.ProcessAnimations();
        Assert.Contains(host.Transcripts, t => t.StartsWith("knockdown:"));

        // +40 vs a prone target: an uncalled follow-up reads chance 90 (50 + 40).
        host.Transcripts.Clear();
        Assert.True(engine.TryAttack(enemy));
        Assert.Contains(host.Transcripts, t => t.Contains("chance=90%"));

        // The enemy stands up at its turn (costs AP).
        host.Animating.Clear();
        engine.ProcessAnimations();
        host.Transcripts.Clear();
        engine.EndPlayerTurn();
        for (int i = 0; i < 50 && engine.Phase == CombatPhase.EnemyTurn; i++)
        {
            host.Animating.Clear();
            engine.Step();
        }
        Assert.Contains(host.Transcripts, t => t.StartsWith("getup:"));
    }

    [Fact]
    public void ExplosionDamagesCrittersInRadiusButNotBeyond()
    {
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(20100, hp: 50));
        int center = Step(20100, 0, 10); // 10 hexes from the dude (he's clear)
        MapObject a = host.AddCritter(NewCritter(center, hp: 100));
        MapObject b = host.AddCritter(NewCritter(Step(center, 1, 1), hp: 100));   // 1 away — in radius
        MapObject far = host.AddCritter(NewCritter(Step(center, 0, 4), hp: 100));  // 4 away — clear
        var engine = new CombatEngine(host, new MinRng()); // 20 dmg each (no explosion DT/DR)

        engine.Explode(center, killer: null, minDamage: 20, maxDamage: 35, radius: 2);

        Assert.True(a.CurrentHp < 100);
        Assert.True(b.CurrentHp < 100);
        Assert.Equal(100, far.CurrentHp);
        Assert.Equal(50, host.Dude!.CurrentHp);
    }

    [Fact]
    public void LethalExplosionKillsAndPaysXp()
    {
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(20100, hp: 50));
        int center = Step(20100, 0, 10);
        MapObject victim = host.AddCritter(NewCritter(center, hp: 5, exp: 75));
        var engine = new CombatEngine(host, new MinRng());

        engine.Explode(center, killer: host.Dude, minDamage: 20, maxDamage: 35, radius: 2);

        Assert.True(victim.IsDead);
        Assert.Equal(75, host.XpAwarded); // out-of-combat blast pays immediately
    }

    private static int Step(int tile, int dir, int n)
    {
        for (int i = 0; i < n; i++)
            tile = HexGrid.TileInDirection(tile, dir);
        return tile;
    }

    /// <summary>A throwable weapon proto+item: ext 0x50 = secondary THROW (spear),
    /// 0x05 = primary THROW (grenade); dmgType 6 = explosion.</summary>
    private static (ProtoInfo Proto, MapObject Item) MakeThrowWeapon(
        int pid, int ext, int dmgType, int r1, int r2, int min, int max)
    {
        var w = new WeaponProtoStats(0, min, max, dmgType, r1, r2, 0, 0, 4, 4, 0, 0, -1, 0, 0);
        var proto = new ProtoInfo(pid, 0, 0x06000000, 0, ext, 3, Weapon: w); // SubType 3 = weapon
        var item = new MapObject
        {
            Id = 7, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0,
            Fid = 0x06000000, Flags = 0, Pid = pid, Sid = -1,
        };
        return (proto, item);
    }

    [Fact]
    public void ThrownSpearLandsRecoverable()
    {
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        int targetTile = Step(20100, 0, 3); // within throw range 8
        host.AddCritter(NewCritter(targetTile, hp: 100));
        host.Equipped = MakeThrowWeapon(pid: 0x07, ext: 0x50, dmgType: 0, r1: 2, r2: 8, min: 3, max: 10);
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryThrow(targetTile));
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.Contains(host.Transcripts, t => t.StartsWith("throw "));
        Assert.Contains(host.Dropped, d => d.Pid == 0x07 && d.Tile == targetTile); // recoverable
        Assert.Equal(0, host.ExplosionMarkers);
    }

    [Fact]
    public void ThrownGrenadeExplodesAtLandingTileWithAoe()
    {
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        int targetTile = Step(20100, 0, 6);
        MapObject a = host.AddCritter(NewCritter(targetTile, hp: 100));
        MapObject b = host.AddCritter(NewCritter(Step(targetTile, 1, 1), hp: 100));
        host.Equipped = MakeThrowWeapon(pid: 0x19, ext: 0x05, dmgType: 6, r1: 15, r2: 0, min: 20, max: 35);
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryThrow(targetTile));
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.Equal(1, host.ExplosionMarkers);   // misc-10 marker (metarule(49) path)
        Assert.True(a.CurrentHp < 100);            // AoE at landing
        Assert.True(b.CurrentHp < 100);            // AoE radius
        Assert.DoesNotContain(host.Dropped, d => d.Pid == 0x19); // explosive consumed
    }

    [Fact]
    public void ThrowBeyondRangeIsRefused()
    {
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        int targetTile = Step(20100, 0, 12); // spear range is 8
        host.Equipped = MakeThrowWeapon(pid: 0x07, ext: 0x50, dmgType: 0, r1: 2, r2: 8, min: 3, max: 10);
        var engine = new CombatEngine(host, new MinRng());

        Assert.False(engine.TryThrow(targetTile));
    }

    [Fact]
    public void ThrownWeaponCanCritFromDay2()
    {
        // P13-M3: throws now run the day-gated 2nd-d100 crit upgrade (combat.cc randomRoll).
        var host = new FakeCombatHost { CriticalsEnabled = true };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 100)); // Throwing skill -> high chance
        int targetTile = Step(20100, 0, 3);
        host.AddCritter(NewCritter(targetTile, hp: 500));
        host.Equipped = MakeThrowWeapon(pid: 0x07, ext: 0x50, dmgType: 0, r1: 2, r2: 8, min: 3, max: 10);
        var engine = new CombatEngine(host, new MinRng()); // hit, then the crit roll succeeds (delta/10 large)

        Assert.True(engine.TryThrow(targetTile));
        Assert.Contains(host.Transcripts, t => t.StartsWith("throw ") && t.Contains("CRITICAL"));
    }

    [Fact]
    public void ThrownWeaponDoesNotCritOnDay1()
    {
        // The day-1 invariant the throw goldens rely on: no crit roll, no extra RNG.
        var host = new FakeCombatHost { CriticalsEnabled = false };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 100));
        int targetTile = Step(20100, 0, 3);
        host.AddCritter(NewCritter(targetTile, hp: 500));
        host.Equipped = MakeThrowWeapon(pid: 0x07, ext: 0x50, dmgType: 0, r1: 2, r2: 8, min: 3, max: 10);
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryThrow(targetTile));
        Assert.DoesNotContain(host.Transcripts, t => t.Contains("CRITICAL"));
    }

    // --- helpers ---------------------------------------------------------

    /// <summary>STR/AG 5 ⇒ unarmed skill 50, so a MinRng roll always connects.</summary>
    // ====================================================================
    //  Burst fire (#9) — _compute_spray port. ext 0x76 = primary SINGLE(6),
    //  secondary BURST(7); IsGun true (nibble >= 6), IsBurstWeapon true.
    // ====================================================================
    private static (ProtoInfo Proto, MapObject Item) MakeBurstWeapon(
        int rounds, int apCost2, int minDmg = 10, int maxDmg = 10, int minStr = 0, int maxRange = 40)
    {
        var w = new WeaponProtoStats(0, minDmg, maxDmg, 0, maxRange, maxRange, 0, minStr, 5, apCost2, rounds, 0, -1, 30, 0);
        var proto = new ProtoInfo(0x09, 0, 0x06000000, 0, 0x76, 3, Weapon: w);
        var item = new MapObject
        {
            Id = 9, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0,
            Fid = 0x06000000, Flags = 0, Pid = 0x09, Sid = -1, AmmoQuantity = -1,
        };
        return (proto, item);
    }

    [Fact]
    public void BurstFiresAtMostTheLoadedAmmoOrWeaponRounds()
    {
        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 6 };
        host.SetDude(NewCritter(20100, hp: 30, ap: 12));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
        host.Equipped = MakeBurstWeapon(rounds: 10, apCost2: 6); // mag (6) < burst rounds (10)
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryBurst(enemy));
        // rounds=6: capped by the loaded magazine, not the weapon's 10-round burst.
        Assert.Contains(host.Transcripts, t => t.StartsWith("burst ") && t.Contains("rounds=6"));
    }

    [Fact]
    public void BurstConeCatchesACollateralBystanderOnALine()
    {
        // M2: a critter standing on the left cone line takes collateral fire, while the
        // main target keeps its own hits. (leftTile is on the from->leftEnd line by
        // construction, so the bystander is guaranteed to be in the cone.)
        int from = 20100;
        int target = HexGrid.TileInDirection(from, 0, 3);

        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(from, hp: 30, ap: 12, skill: 100)); // gun skill so ranged shots connect
        MapObject enemy = host.AddCritter(NewCritter(target, hp: 500));
        (ProtoInfo proto, MapObject item) = MakeBurstWeapon(rounds: 10, apCost2: 6, minDmg: 10, maxDmg: 10);
        host.Equipped = (proto, item);

        // Reproduce the engine's left-line geometry, then DISCOVER a tile actually on
        // the from->leftEnd Bresenham (the far-endpoint path need not pass through
        // leftTile), and stand the bystander there.
        int pivot = HexGrid.Distance(from, target) <= 3 ? HexGrid.TileNumBeyond(from, target, 3) : target;
        int rotation = HexGrid.RotationTo(pivot, from);
        int leftTile = HexGrid.TileInDirection(pivot, (rotation + 1) % 6, 1);
        int leftEnd = HexGrid.TileNumBeyond(from, leftTile, 40);
        var leftLine = new List<int>();
        LineOfFire.Trace(from, leftEnd, t => { leftLine.Add(t); return null; });
        Assert.NotEmpty(leftLine);
        MapObject bystander = host.AddCritter(NewCritter(leftLine[Math.Min(2, leftLine.Count - 1)], hp: 500));

        host.BlockerOverride = tile => host.CombatCritters.FirstOrDefault(c => c.HexTile == tile && !c.IsDead);

        var engine = new CombatEngine(host, new MinRng()); // every round connects
        Assert.True(engine.TryBurst(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.True(bystander.CurrentHp < 500, "the cone's left line should have caught the bystander");
        Assert.True(enemy.CurrentHp < 500, "the main target still takes its own hits");
        Assert.Contains(host.Transcripts, t => t.StartsWith("burst-extra:"));
    }

    [Fact]
    public void BurstWithNoBystandersHasNoCollateral()
    {
        // The 1-on-1 invariant the burst goldens rely on: empty cone lines -> no extras.
        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(20100, hp: 30, ap: 12));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0, 3), hp: 500));
        host.Equipped = MakeBurstWeapon(rounds: 10, apCost2: 6, minDmg: 10, maxDmg: 10);
        host.BlockerOverride = tile => host.CombatCritters.FirstOrDefault(c => c.HexTile == tile && !c.IsDead);

        var engine = new CombatEngine(host, new MinRng());
        Assert.True(engine.TryBurst(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("burst-extra:"));
    }

    [Fact]
    public void BurstConsumesTheWholeMagazineOnResolveNotOnRoll()
    {
        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(20100, hp: 30, ap: 12));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
        (ProtoInfo proto, MapObject item) = MakeBurstWeapon(rounds: 10, apCost2: 6);
        item.AmmoQuantity = 10;
        host.Equipped = (proto, item);
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryBurst(enemy));
        Assert.Equal(10, item.AmmoQuantity);     // deferred: not decremented at roll time
        host.Animating.Clear();
        engine.ProcessAnimations();
        Assert.Equal(0, item.AmmoQuantity);       // single batch decrement at resolve (10 − 10)
    }

    [Fact]
    public void BurstCostsTheSecondaryApOnceRegardlessOfRoundCount()
    {
        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(20100, hp: 30, ap: 12));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
        host.Equipped = MakeBurstWeapon(rounds: 10, apCost2: 6);
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryBurst(enemy));
        Assert.Equal(6, engine.DudeAp); // 12 MaxAP − ApCost2(6), paid once for the whole burst
    }

    [Fact]
    public void BurstAccumulatesDamageAcrossEveryHitRound()
    {
        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(20100, hp: 30, ap: 12));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
        host.Equipped = MakeBurstWeapon(rounds: 10, apCost2: 6, minDmg: 10, maxDmg: 10);
        var engine = new CombatEngine(host, new MinRng()); // every exposed round hits for 10

        Assert.True(engine.TryBurst(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();

        // n=10 → centerRounds=3, mainTargetRounds=1 → center-line exposure = 3.
        // 3 hits × 10 dmg = 30 accumulated; left/right cone rounds are collateral (dropped v1).
        Assert.Contains(host.Transcripts, t => t.Contains("hit=3 damage=30"));
        Assert.Equal(470, enemy.CurrentHp);
    }

    [Fact]
    public void BurstCriticalFailureAbortsAllRoundsButStillSpendsApAndAmmo()
    {
        var host = new FakeCombatHost { CriticalsEnabled = true, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(20100, hp: 30, ap: 12));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
        (ProtoInfo proto, MapObject item) = MakeBurstWeapon(rounds: 10, apCost2: 6);
        item.AmmoQuantity = 10;
        host.Equipped = (proto, item);
        // Inception roll: d100=100 (a miss vs ~5% accuracy), then 1 ≤ -delta/10 → CRITICAL_FAILURE.
        var engine = new CombatEngine(host, new SequenceRng(100, 1));

        Assert.True(engine.TryBurst(enemy));        // the action still happened
        Assert.Equal(6, engine.DudeAp);             // AP spent
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.Contains(host.Transcripts, t => t.Contains("hit=0 damage=0"));
        Assert.Equal(500, enemy.CurrentHp);          // no rounds connected
        Assert.Equal(0, item.AmmoQuantity);          // bullets still left the barrel (10 − 10)
    }

    [Fact]
    public void EndPlayerTurnWaitsForAPendingBurstToResolve()
    {
        // #9 review (HIGH): the turn must not hand over to the enemy while the dude's
        // burst animation is still in flight (the B-key + Space race).
        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(20100, hp: 30, ap: 12));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
        host.Equipped = MakeBurstWeapon(rounds: 10, apCost2: 6);
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryBurst(enemy));                  // opens combat, _pendingBurst in flight
        Assert.Equal(CombatPhase.PlayerTurn, engine.Phase);
        engine.EndPlayerTurn();                               // must be a no-op while the burst resolves
        Assert.Equal(CombatPhase.PlayerTurn, engine.Phase);

        host.Animating.Clear();
        engine.ProcessAnimations();                           // burst lands
        engine.EndPlayerTurn();                               // now the turn can end
        Assert.Equal(CombatPhase.EnemyTurn, engine.Phase);
    }

    [Fact]
    public void NonBurstWeaponRefusesToBurst()
    {
        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(20100, hp: 30, ap: 12));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
        // ext 0x06 = primary SINGLE only (a pistol): a gun, but not burst-capable.
        var w = new WeaponProtoStats(0, 5, 12, 0, 40, 0, 0, 0, 5, 0, 0, 0, -1, 12, 0);
        host.Equipped = (new ProtoInfo(0x08, 0, 0x06000000, 0, 0x06, 3, Weapon: w),
            new MapObject
            {
                Id = 8, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0,
                Fid = 0x06000000, Flags = 0, Pid = 0x08, Sid = -1, AmmoQuantity = -1,
            });
        var engine = new CombatEngine(host, new MinRng());

        Assert.False(engine.TryBurst(enemy));
        Assert.Equal(CombatPhase.Idle, engine.Phase); // refused before combat opened
        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("burst "));
        Assert.Contains(host.Logs, l => l.Contains("can't fire a burst"));
    }

    [Fact]
    public void BrawlMakesOpposingTeamsFightEachOtherNotJustTheDude()
    {
        // Phase-16 M3 (X-FIGHTING-Y): two enemy teams spawned next to each other but far
        // from the dude. StartBrawl puts both in combat; cross-team targeting makes each
        // attack the NEARER enemy (the other faction), sparing the distant dude.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        int aTile = HexGrid.TileInDirection(20100, 0, 8);          // ~8 hexes from the dude
        MapObject teamA = host.AddCritter(NewCritter(aTile, hp: 30, ap: 10));
        MapObject teamB = host.AddCritter(NewCritter(HexGrid.TileInDirection(aTile, 0), hp: 30, ap: 10));
        teamA.Team = 1;
        teamB.Team = 2;                                            // distinct teams → they brawl
        var engine = new CombatEngine(host, new MinRng());

        engine.StartBrawl([teamA, teamB]);
        Assert.Equal(CombatPhase.PlayerTurn, engine.Phase);
        Assert.Contains(teamA, engine.Hostiles);
        Assert.Contains(teamB, engine.Hostiles);

        engine.EndPlayerTurn();                                   // hand over to the factions
        for (int i = 0; i < 200 && engine.Phase == CombatPhase.EnemyTurn; i++)
        {
            host.Animating.Clear();
            engine.Step();
        }

        Assert.True(teamA.CurrentHp < 30, "team A should have been hit by team B");
        Assert.True(teamB.CurrentHp < 30, "team B should have been hit by team A");
        Assert.Equal(30, dude.CurrentHp); // the distant dude is ignored — the factions fight each other
    }

    [Fact]
    public void CrippledArmsGateWeaponAttacks()
    {
        // Phase-18 M2 (combat.cc:5655): both arms crippled blocks ANY weapon attack; one
        // crippled arm blocks a TWO-HANDED weapon only; unarmed (no weapon) is never gated.
        bool CanAttack(int combatResults, (ProtoInfo?, MapObject?) equipped)
        {
            var host = new FakeCombatHost { Equipped = equipped };
            MapObject dude = host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 100));
            dude.CombatResults = combatResults;
            MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 30));
            return new CombatEngine(host, new MinRng()).TryAttack(enemy);
        }

        var wstats = new WeaponProtoStats(1, 1, 6, 0, 1, 0, 0, 1, 3, 0, 0, 0, -1, 0, 0);
        MapObject item = new() { Id = 8, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0, Flags = 0, Pid = 8, Sid = -1 };
        (ProtoInfo?, MapObject?) twoHanded = (new ProtoInfo(8, 0, 0x01000000, 0, 0x201, 3, Weapon: wstats), item);
        (ProtoInfo?, MapObject?) oneHanded = (new ProtoInfo(8, 0, 0x01000000, 0, 0x001, 3, Weapon: wstats), item);
        (ProtoInfo?, MapObject?) unarmed = (null, null);

        Assert.True(CanAttack(0, twoHanded));                                       // no crip → fine
        Assert.False(CanAttack(CriticalTables.DamCripArmLeft, twoHanded));          // one arm + 2H → blocked
        Assert.True(CanAttack(CriticalTables.DamCripArmLeft, oneHanded));           // one arm + 1H → fine
        Assert.False(CanAttack(CriticalTables.DamCripArmAny, oneHanded));           // both arms + weapon → blocked
        Assert.True(CanAttack(CriticalTables.DamCripArmAny, unarmed));              // both arms but unarmed → punch
    }

    // ====================================================================
    //  Combat-path traits (P29-M1): One Hander, Fast Shot, Finesse, Jinxed.
    //  All are dude-only and inert without the trait → goldens unchanged.
    // ====================================================================

    /// <summary>A melee weapon proto+item with the given extended flags (0x001 one-handed,
    /// 0x201 two-handed; low nibble 1 = a melee swing, not a gun).</summary>
    private static (ProtoInfo Proto, MapObject Item) MakeMeleeWeapon(int ext, int minDmg = 1, int maxDmg = 6, int ap = 3, int dmgType = 0)
    {
        var w = new WeaponProtoStats(1, minDmg, maxDmg, dmgType, 1, 0, 0, 1, ap, 0, 0, 0, -1, 0, 0);
        var proto = new ProtoInfo(8, 0, 0x01000000, 0, ext, 3, Weapon: w);
        var item = new MapObject { Id = 8, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0, Flags = 0, Pid = 8, Sid = -1 };
        return (proto, item);
    }

    /// <summary>A single-shot gun (ext 0x06 = primary SINGLE) with range 40 and AP cost 5.</summary>
    private static (ProtoInfo Proto, MapObject Item) MakeGun(int ap = 5)
    {
        var w = new WeaponProtoStats(0, 5, 12, 0, 40, 0, 0, 0, ap, 0, 0, 0, -1, 12, 0);
        var proto = new ProtoInfo(8, 0, 0x06000000, 0, 0x06, 3, Weapon: w);
        var item = new MapObject { Id = 8, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0x06000000, Flags = 0, Pid = 8, Sid = -1, AmmoQuantity = -1 };
        return (proto, item);
    }

    private static int AttackChance(FakeCombatHost host)
    {
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        new CombatEngine(host, new MinRng()).TryAttack(enemy);
        string line = host.Transcripts.First(t => t.StartsWith("attack "));
        return int.Parse(line.Split("chance=")[1].Split('%')[0]);
    }

    [Fact]
    public void OneHanderHelpsOneHandedAndHurtsTwoHandedToHit()
    {
        // P29-M1 (combat.cc:4404): a One Hander dude gets +20 to hit with a one-handed weapon
        // and −40 with a two-handed one. Base melee to-hit here is 50 (melee skill 50, AC 0).
        int Chance(bool oneHander, int ext)
        {
            var host = new FakeCombatHost { Equipped = MakeMeleeWeapon(ext) };
            if (oneHander) host.Traits.Add(TraitModifiers.OneHander);
            host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 10)); // melee skill = 20 + 2*(5+5) + 10 = 50
            return AttackChance(host);
        }

        Assert.Equal(50, Chance(false, 0x001)); // baseline, no trait
        Assert.Equal(70, Chance(true, 0x001));  // +20 one-handed
        Assert.Equal(10, Chance(true, 0x201));  // −40 two-handed
    }

    [Fact]
    public void FastShotLowersRangedApAndCannotAim()
    {
        // P29-M1 (item.cc:1679/1825): Fast Shot trims 1 AP off a long-range (>2) shot and forbids
        // aiming. A 5-AP gun → 4 AP; an aimed Fast Shot is coerced to uncalled (no +1 AP).
        int Ap(bool fastShot, int hitLocation)
        {
            var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10, Equipped = MakeGun() };
            if (fastShot) host.Traits.Add(TraitModifiers.FastShot);
            host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 50));
            MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
            var engine = new CombatEngine(host, new MinRng());
            Assert.True(engine.TryAttack(enemy, hitLocation));
            return engine.DudeAp;
        }

        Assert.Equal(5, Ap(false, CriticalTables.LocationUncalled)); // 10 − 5
        Assert.Equal(6, Ap(true, CriticalTables.LocationUncalled));  // 10 − (5 − 1 Fast Shot)
        Assert.Equal(4, Ap(false, 6));                              // aimed EYES: 10 − (5 + 1)
        Assert.Equal(6, Ap(true, 6));                               // aim blocked → same as uncalled Fast Shot
    }

    [Fact]
    public void FinesseRaisesDefenderDamageResistanceForADudeAttack()
    {
        // P29-M1 (combat.cc:4540): a Finesse dude raises the defender's DR +30. With a fixed-100
        // melee weapon and DR 50, damage = 100*(100−50)/100 = 50 normally, 100*(100−80)/100 = 20 finessed.
        int Damage(bool finesse)
        {
            var host = new FakeCombatHost { CriticalsEnabled = false, Equipped = MakeMeleeWeapon(0x001, minDmg: 100, maxDmg: 100) };
            if (finesse) host.Traits.Add(TraitModifiers.Finesse);
            host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 50));
            MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500, dr: 50));
            var engine = new CombatEngine(host, new MinRng());
            Assert.True(engine.TryAttack(enemy));
            host.Animating.Clear();
            engine.ProcessAnimations();
            return 500 - enemy.CurrentHp;
        }

        Assert.Equal(50, Damage(false));
        Assert.Equal(20, Damage(true));
    }

    [Fact]
    public void JinxedDudeFumblesAMissIntoALostTurn()
    {
        // P29-M1 (combat.cc:3857, simplified): on a MISS a Jinxed dude rolls d2 — a 1 fumbles into a
        // lost turn (AP → 0). Gated on CriticalsEnabled (our day-2 proxy for the engine's day-6 gate).
        int ApAfterMiss(bool jinxed)
        {
            var host = new FakeCombatHost { CriticalsEnabled = true }; // unarmed
            if (jinxed) host.Traits.Add(TraitModifiers.Jinxed);
            host.SetDude(NewCritter(20100, hp: 30, ap: 10));
            MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
            // SequenceRng: to-hit roll 100 (a miss), then the Jinxed d2 = 1 (fumble).
            var engine = new CombatEngine(host, new SequenceRng(100, 1));
            Assert.True(engine.TryAttack(enemy));
            return engine.DudeAp;
        }

        Assert.Equal(0, ApAfterMiss(true));  // fumbled → turn lost
        Assert.Equal(7, ApAfterMiss(false)); // no trait: 10 − 3 punch, no d2 drawn
    }

    // ====================================================================
    //  Curated perk effects (P29-M4): Bonus Ranged Damage, Living Anatomy,
    //  Pyromaniac, Weapon Handling, Heave Ho. All dude-only, inert at rank 0.
    // ====================================================================

    private int GunDamage(int bonusRangedRank)
    {
        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10, Equipped = MakeGun() };
        host.PerkRanks[Perks.PerkId.BonusRangedDamage] = bonusRangedRank;
        host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
        var engine = new CombatEngine(host, new MinRng()); // min gun damage = 5
        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();
        return 500 - enemy.CurrentHp;
    }

    [Fact]
    public void BonusRangedDamageAddsTwoPerRankToAGunHit()
    {
        // P29-M4 (combat.cc:4547): +2 damage/rank, ranged only. Gun min damage 5 → 5; +2/rank final.
        int baseDmg = GunDamage(0);
        Assert.Equal(5, baseDmg);
        Assert.Equal(baseDmg + 4, GunDamage(2)); // 2 ranks → +4
    }

    private int UnarmedDamage(int livingAnatomyRank, int killType)
    {
        var host = new FakeCombatHost { CriticalsEnabled = false };
        host.PerkRanks[Perks.PerkId.LivingAnatomy] = livingAnatomyRank;
        host.SetDude(NewCritter(20100, hp: 30, ap: 10)); // unarmed, floor damage 1
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500, killType: killType));
        var engine = new CombatEngine(host, new MinRng());
        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();
        return 500 - enemy.CurrentHp;
    }

    [Fact]
    public void LivingAnatomyAddsFiveVsLivingButNotRobotsOrAliens()
    {
        // P29-M4 (combat.cc:4619): +5 to the final damage vs a living target, skipped for robot/alien.
        Assert.Equal(1, UnarmedDamage(0, killType: 0));   // KILL_TYPE_MAN, no perk → floor 1
        Assert.Equal(6, UnarmedDamage(1, killType: 0));   // living → +5
        Assert.Equal(1, UnarmedDamage(1, killType: 10));  // KILL_TYPE_ROBOT → no bonus
        Assert.Equal(1, UnarmedDamage(1, killType: 16));  // KILL_TYPE_ALIEN → no bonus
    }

    [Fact]
    public void PyromaniacAddsFiveWithAFireWeaponOnly()
    {
        int Damage(int rank, int dmgType)
        {
            var host = new FakeCombatHost { CriticalsEnabled = false, Equipped = MakeMeleeWeapon(0x001, minDmg: 5, maxDmg: 5, dmgType: dmgType) };
            host.PerkRanks[Perks.PerkId.Pyromaniac] = rank;
            host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 50));
            MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
            var engine = new CombatEngine(host, new MinRng());
            Assert.True(engine.TryAttack(enemy));
            host.Animating.Clear();
            engine.ProcessAnimations();
            return 500 - enemy.CurrentHp;
        }
        // melee weapon damage 5 (min=max=5). Pyromaniac only fires for a fire weapon (dmgType 2).
        Assert.Equal(5, Damage(0, 2));      // fire weapon, no perk
        Assert.Equal(10, Damage(1, 2));     // fire + perk → +5
        Assert.Equal(5, Damage(1, 0));      // perk but normal damage → no bonus
    }

    [Fact]
    public void WeaponHandlingCancelsTheMinStrengthToHitPenalty()
    {
        // P29-M4 (combat.cc:4414): +3 effective ST vs the min-ST penalty. A min-ST 8 gun on a ST-5 dude
        // takes −20*(8−5) = −60 to hit; Weapon Handling makes the effective ST 8 → no penalty (+60).
        int Chance(int rank)
        {
            var w = new WeaponProtoStats(0, 5, 12, 0, 40, 0, 0, 8, 5, 0, 0, 0, -1, 12, 0); // min-ST 8
            var item = new MapObject { Id = 8, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0x06000000, Flags = 0, Pid = 8, Sid = -1, AmmoQuantity = -1 };
            var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10, Equipped = (new ProtoInfo(8, 0, 0x06000000, 0, 0x06, 3, Weapon: w), item) };
            host.PerkRanks[Perks.PerkId.WeaponHandling] = rank;
            host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 75)); // Small Guns = 5 + 4*5 + 75 = 100
            return AttackChance(host);
        }
        // PE 0 → the distance term is −20: 100−20−60 = 20 without; 100−20 = 80 with (penalty cancelled).
        Assert.Equal(20, Chance(0));
        Assert.Equal(80, Chance(1));
    }

    [Fact]
    public void HeaveHoExtendsThrowRange()
    {
        // P29-M4 (item.cc:1613): +2 effective ST/rank for the throw range. ST 5 → 3*5 = 15; a target at
        // 16 is out of range, but Heave Ho rank 1 → ST 7 → 3*7 = 21 reaches it.
        bool CanThrow(int rank, int dist)
        {
            var host = new FakeCombatHost();
            host.PerkRanks[Perks.PerkId.HeaveHo] = rank;
            host.SetDude(NewCritter(20100, hp: 30, ap: 10));
            int target = Step(20100, 0, dist);
            host.AddCritter(NewCritter(target, hp: 100));
            host.Equipped = MakeThrowWeapon(pid: 0x07, ext: 0x50, dmgType: 0, r1: 2, r2: 40, min: 3, max: 10);
            return new CombatEngine(host, new MinRng()).TryThrow(target);
        }
        Assert.False(CanThrow(0, 16)); // ST 5 → range 15, out of reach
        Assert.True(CanThrow(1, 16));  // Heave Ho → range 21, reaches
    }

    // ====================================================================
    //  Silent Death backstab (P30 A-M1): melee/unarmed, sneaking, from behind,
    //  target not yet engaged → 4x dmg / x2 on a crit. Dude-only, perk + flag gated.
    // ====================================================================

    private int SilentDeathMelee(bool perk, bool flag, int enemyRotation, int whoHitMe, bool criticals = false)
    {
        var host = new FakeCombatHost { CriticalsEnabled = criticals, SneakFlag = flag,
            Equipped = MakeMeleeWeapon(0x001, minDmg: 10, maxDmg: 10) };
        if (perk) host.PerkRanks[Perks.PerkId.SilentDeath] = 1;
        host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 100)); // melee skill -> always hits
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
        enemy.Rotation = enemyRotation;   // the dude faces dir 0 (toward the enemy); rot 0 = a backstab
        enemy.WhoHitMeCid = whoHitMe;     // != -1 → not yet engaged by the dude → surprise allowed
        var engine = new CombatEngine(host, new MinRng());
        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();
        return 500 - enemy.CurrentHp;
    }

    [Fact]
    public void SilentDeathQuadruplesAMeleeBackstab()
    {
        // From behind (enemy faces the same dir 0 the dude does → diff 0 → not from front): 10*4/2 = 20
        // vs the base 10*2/2 = 10.
        Assert.Equal(20, SilentDeathMelee(perk: true, flag: true, enemyRotation: 0, whoHitMe: 0));
        Assert.Equal(10, SilentDeathMelee(perk: false, flag: true, enemyRotation: 0, whoHitMe: 0)); // no perk
    }

    [Fact]
    public void SilentDeathRequiresBehindSneakingAndAFreshTarget()
    {
        Assert.Equal(10, SilentDeathMelee(perk: true, flag: true, enemyRotation: 3, whoHitMe: 0));  // from front
        Assert.Equal(10, SilentDeathMelee(perk: true, flag: false, enemyRotation: 0, whoHitMe: 0)); // not sneaking
        Assert.Equal(10, SilentDeathMelee(perk: true, flag: true, enemyRotation: 0, whoHitMe: -1)); // already engaged
    }

    [Fact]
    public void SilentDeathDoublesACriticalBackstab()
    {
        // On a crit, Silent Death doubles the crit multiplier (combat.cc:3919), so a backstab crit deals
        // exactly twice a normal crit (the perk draws no extra RNG → identical crit/severity rolls).
        int normalCrit = SilentDeathMelee(perk: false, flag: true, enemyRotation: 0, whoHitMe: 0, criticals: true);
        int silentCrit = SilentDeathMelee(perk: true, flag: true, enemyRotation: 0, whoHitMe: 0, criticals: true);
        Assert.True(normalCrit > 0);
        Assert.Equal(2 * normalCrit, silentCrit);
    }

    [Fact]
    public void CompanionPerkRanksApplyToItsStats()
    {
        // P29-M6: a companion's perk ranks feed CritterState's 5th arg (the same path the dude uses),
        // so the stat modifier applies to a non-dude critter. Inert when null — the slice default.
        (MapObject obj, CritterProtoStats proto) = NewCritter(20100, hp: 30, dr: 10); // base DR 10
        int[] ranks = new int[Hexwaste.Formats.Perks.PerkTable.Count];
        ranks[12] = 2; // Toughness → +10 DR/rank

        Assert.Equal(10, new CritterState(obj, proto).DamageResistance);                   // no ranks → base
        Assert.Equal(30, new CritterState(obj, proto, perkRanks: ranks).DamageResistance); // +20 from Toughness
    }

    private static (MapObject Obj, CritterProtoStats Proto) NewCritter(
        int tile, int hp, int ap = 10, int seq = 1, int exp = 0, int betterCrit = 0, int meleeDmg = 0, int skill = 0, int endurance = 0, int dr = 0, int killType = 0)
    {
        int[] b = new int[35];
        b[CritterStat.Strength] = 5;
        b[CritterStat.Agility] = 5;
        b[CritterStat.Endurance] = endurance;
        b[CritterStat.MaximumHitPoints] = hp;
        b[CritterStat.MaximumActionPoints] = ap;
        b[CritterStat.Sequence] = seq;
        b[CritterStat.BetterCriticals] = betterCrit;
        b[CritterStat.MeleeDamage] = meleeDmg;
        b[CritterStat.DamageResistance] = dr;
        int[] sk = new int[18];
        for (int i = 0; i < sk.Length; i++) sk[i] = skill; // ranged tests need a usable gun skill
        var proto = new CritterProtoStats(0, 0, 0, b, new int[35], sk, 0, exp, killType, 0);
        var obj = new MapObject
        {
            Id = tile, HexTile = tile, X = 0, Y = 0, Frame = 0, Rotation = 0,
            Fid = 0x01000000, Flags = 0, Pid = 0x01000001, Sid = -1,
        };
        obj.CurrentHp = hp;
        return (obj, proto);
    }

    /// <summary>Returns minInclusive for every draw: RollHit always connects (1 ≤
    /// chance) and unarmed damage is the floor (1).</summary>
    private sealed class MinRng : ICombatRng
    {
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
    }

    /// <summary>Returns queued values (clamped into range), repeating the last —
    /// lets a test force a hit then a big damage roll.</summary>
    private sealed class SequenceRng(params int[] values) : ICombatRng
    {
        private int _i;
        public int Next(int minInclusive, int maxExclusive) =>
            Math.Clamp(values[Math.Min(_i++, values.Length - 1)], minInclusive, maxExclusive - 1);
    }

    private sealed class FakeCombatHost : ICombatHost
    {
        private readonly Dictionary<MapObject, CritterState> _states = [];
        private readonly List<MapObject> _critters = [];
        public readonly HashSet<MapObject> Animating = [];
        public readonly List<string> Transcripts = [];
        public readonly List<string> Logs = [];
        public int XpAwarded;
        public bool GameOverCalled;
        public MapObject? Dude { get; private set; }

        public MapObject SetDude((MapObject Obj, CritterProtoStats Proto) c)
        {
            Dude = c.Obj;
            _states[c.Obj] = new CritterState(c.Obj, c.Proto);
            return c.Obj;
        }

        public MapObject AddCritter((MapObject Obj, CritterProtoStats Proto) c)
        {
            _states[c.Obj] = new CritterState(c.Obj, c.Proto);
            _critters.Add(c.Obj);
            return c.Obj;
        }

        public readonly Dictionary<MapObject, AiPacket> AiPackets = [];
        public bool CriticalsEnabled { get; set; }
        public readonly Dictionary<int, int> PerkRanks = []; // P28-M3 combat perk effects
        public int DudePerkRank(int perk) => PerkRanks.GetValueOrDefault(perk);
        public readonly HashSet<int> Traits = []; // P29-M1 combat-path trait effects
        public bool DudeHasTrait(int trait) => Traits.Contains(trait);
        public bool SneakFlag; // P30 A-M1 Silent Death gate
        public bool DudeSneakFlag => SneakFlag;
        public CritterState? GetCritterState(MapObject critter) => _states.GetValueOrDefault(critter);
        public AiPacket? GetAiPacket(MapObject critter) => AiPackets.GetValueOrDefault(critter);
        public (ProtoInfo? Proto, MapObject? Item) Equipped = (null, null);
        public (ProtoInfo? Proto, MapObject? Item) EquippedWeapon(MapObject critter) => Equipped;
        public int LoadedAmmoCount; // settable magazine for burst/gun tests
        public int WeaponAmmo(ProtoInfo weaponProto, MapObject item) => LoadedAmmoCount;
        public AmmoProtoStats? LoadedAmmo(ProtoInfo weaponProto, MapObject item) => null;
        public bool TryReload(MapObject holder, ProtoInfo weaponProto, MapObject item) => false;
        public Func<int, MapObject?>? BlockerOverride; // tests that need critters/walls on the line
        public MapObject? ShootBlockerAt(int tile, MapObject shooter, MapObject target) => BlockerOverride?.Invoke(tile);
        public bool IsBlocked(int tile) => false;
        public bool IsAnimating(MapObject critter) => Animating.Contains(critter);
        public bool IsFallInProgress(MapObject critter) => false;
        public bool IsAnyWalkerMoving() => false;
        public bool IsWalkerMoving(MapObject critter) => false;
        public bool StartWalk(MapObject critter, int targetTile) { critter.HexTile = targetTile; return true; }
        public void PlaceCritter(MapObject critter, int tile) => critter.HexTile = tile;
        public void StopDude() { }
        public void ClearAnimation(MapObject critter) => Animating.Remove(critter);
        public void OnAttackStarted(MapObject attacker, MapObject target, ProtoInfo? weaponProto) => Animating.Add(attacker);
        public void OnThrowStarted(MapObject thrower, int targetTile, ProtoInfo weaponProto) => Animating.Add(thrower);
        public void RemoveFromHand(MapObject thrower, MapObject item) { }
        public readonly List<(int Pid, int Tile)> Dropped = [];
        public void DropThrownWeapon(MapObject item, int tile) => Dropped.Add((item.Pid, tile));
        public int ExplosionMarkers;
        public void SpawnExplosionMarker(int tile) => ExplosionMarkers++;
        public void OnTargetHit(MapObject target) { }
        public int PickDeathAnim(MapObject critter, int desiredAnim) => 20;
        public bool StartDeathFall(MapObject critter, int deathAnim) => false; // no fall art → corpse now
        public void ConvertToCorpse(MapObject critter, int deathAnim) { }
        public void OnCritterRemoved(MapObject critter) { }
        public IReadOnlyList<string> RunDamageProc(MapObject target, MapObject? source, int damage) => [];
        public (IReadOnlyList<string> Lines, bool Overridden) RunDestroyProc(MapObject critter, MapObject? killer) => ([], false);
        public void RemovePartyMember(MapObject critter) { }
        public IReadOnlyCollection<MapObject> PartyMembers => [];
        public IEnumerable<MapObject> CombatCritters => _critters;
        public void AwardXp(int amount) => XpAwarded += amount;
        public void GameOver() => GameOverCalled = true;
        public void Log(string line) => Logs.Add(line);
        public void Transcript(string line) => Transcripts.Add(line);
        public string ObjectName(MapObject obj) => "Critter";
        public string ObjectNameByPid(int pid) => "Item";
    }
}
