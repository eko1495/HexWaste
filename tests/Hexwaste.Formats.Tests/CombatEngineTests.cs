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
    public void KillsAreTalliedOnlyForDudeOrTeamKills()
    {
        // P38: killsIncByType — the engine tallies a kill (beside the XP award) only for a dude/team
        // kill the destroy script didn't override (combat.cc:4860-4870). A killer-less death isn't.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        var engine = new CombatEngine(host, new MinRng());
        MapObject a = host.AddCritter(NewCritter(tile: 20200, hp: 1, killType: 6));
        MapObject b = host.AddCritter(NewCritter(tile: 20300, hp: 1, killType: 6));

        engine.Kill(a, dude); // dude kill → tallied
        engine.Kill(b);       // no killer → not tallied (the dude/team gate fails)

        Assert.Single(host.RecordedKills);
        Assert.Same(a, host.RecordedKills[0]);
    }

    [Fact]
    public void PreviewToHitAppliesTheLocationPenaltyAndGatesOutOfRange()
    {
        // P52-M4: the called-shot dialog's live per-bodypart to-hit %. Mirrors RollAttack's accuracy
        // (ComputeToHit + the location penalty, halved for melee, clamped 0..95) with no roll/side effect.
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10, skill: 80)); // unarmed (default), usable skill
        var engine = new CombatEngine(host, new MinRng());
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30));

        int? uncalled = engine.PreviewToHit(enemy, CriticalTables.LocationUncalled);
        int? head = engine.PreviewToHit(enemy, 0); // HEAD: -40 penalty, halved to -20 unarmed
        Assert.NotNull(uncalled);
        Assert.NotNull(head);
        Assert.InRange(uncalled!.Value, 0, 95);
        Assert.True(head < uncalled, "the head penalty must lower the to-hit"); // proves the penalty is applied

        // Gating: a target beyond unarmed range (> 2 hexes) yields no preview.
        int far = HexGrid.TileInDirection(HexGrid.TileInDirection(HexGrid.TileInDirection(20100, 0), 0), 0);
        MapObject distant = host.AddCritter(NewCritter(tile: far, hp: 30));
        Assert.Null(engine.PreviewToHit(distant, CriticalTables.LocationUncalled));
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
    public void BonusMoveFreeMovePoolIsSpentBeforeAp()
    {
        // P74-M4: Bonus Move grants 2 free-move AP/rank (combat.cc:3237), drained by movement BEFORE
        // real AP (animation.cc:2610). Rank 2 → a 4-AP free-move pool.
        var host = new FakeCombatHost();
        host.PerkRanks[Hexwaste.Formats.Perks.PerkId.BonusMove] = 2;
        host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 1));
        var engine = new CombatEngine(host, new MinRng());

        engine.TryAttack(enemy);                 // opens combat on the dude's turn → ResetDudeAp seeds free move
        Assert.Equal(4, engine.DudeFreeMove);
        int apAfterAttack = engine.DudeAp;

        engine.SpendDudeAp(3);                    // ≤ pool → only the pool shrinks, AP untouched
        Assert.Equal(1, engine.DudeFreeMove);
        Assert.Equal(apAfterAttack, engine.DudeAp);

        engine.SpendDudeAp(3);                    // > pool (1) → pool to 0, AP pays the excess (2)
        Assert.Equal(0, engine.DudeFreeMove);
        Assert.Equal(apAfterAttack - 2, engine.DudeAp);
    }

    [Fact]
    public void WithoutBonusMoveThePoolIsZeroAndSpendHitsApDirectly()
    {
        // Control: a perk-less dude has no free move → SpendDudeAp behaves exactly as pre-P74 (byte-identical).
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 1));
        var engine = new CombatEngine(host, new MinRng());

        engine.TryAttack(enemy);
        Assert.Equal(0, engine.DudeFreeMove);
        int ap = engine.DudeAp;
        engine.SpendDudeAp(2);
        Assert.Equal(ap - 2, engine.DudeAp);
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
        enemy.Team = 1; // Important-fix side effect (Task-2 review): the combat-open whoHitMe stamp is
                         // now team-gated (SetWhoHitMe), so a same-team pair (the old default-team-0
                         // fixture) no longer gives DangerSource a target to retaliate against — matches
                         // real vanilla data, where a hostile is never on the dude's team.
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
    public void EasyHardDifficultyScalesEnemyDamageDealtToTheDude()
    {
        // P84: an off-team (hostile) attacker's damage is scaled by the combat-difficulty modifier
        // (Easy 75 / Normal 100 / Hard 125). One scripted enemy punch (raw 8 → 8/6/10 after the wrapper).
        int EnemyPunch(int modifier)
        {
            var host = new FakeCombatHost { CombatDifficultyDamageModifier = modifier };
            MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 100, ap: 10));
            MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 3, meleeDmg: 10));
            var engine = new CombatEngine(host, new SequenceRng(1, 8)); // to-hit d100=1 (connects), damage raw=8
            engine.BeginScriptAggro(enemy, dude);
            for (int i = 0; i < 200 && engine.Phase == CombatPhase.EnemyTurn; i++)
            {
                host.Animating.Clear();
                engine.Step();
            }
            return 100 - dude.CurrentHp; // damage the enemy dealt to the dude
        }

        int normal = EnemyPunch(100);
        Assert.True(normal > 0);
        Assert.True(EnemyPunch(125) > normal); // Hard: the enemy hits harder
        Assert.True(EnemyPunch(75) < normal);  // Easy: the enemy hits softer
    }

    [Fact]
    public void DifficultyDoesNotScaleTheDudesOwnDamage()
    {
        // P84: the modifier gates on attacker.team != dude.team — so the DUDE's damage is unchanged by
        // the difficulty setting (DiffDmgMod returns 100 for the dude). The enemy ends with identical HP
        // whether the host reports Normal (100) or Hard (125).
        int DudePunch(int modifier)
        {
            var host = new FakeCombatHost { CombatDifficultyDamageModifier = modifier };
            host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10, meleeDmg: 10));
            MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 100));
            var engine = new CombatEngine(host, new SequenceRng(1, 8)); // identical rolls for both runs
            Assert.True(engine.TryAttack(enemy));
            host.Animating.Clear();
            engine.ProcessAnimations();
            return 100 - enemy.CurrentHp;
        }

        Assert.Equal(DudePunch(100), DudePunch(125)); // the dude is immune to the difficulty modifier
        Assert.True(DudePunch(100) > 0);
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
        enemy.Team = 1; // Task 3: cross-team, like real game data — SetWhoHitMe/RegisterHit only stamp whoHitMe across teams
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
        enemy.Team = 1; // Important-fix side effect (Task-2 review): see RoundRolloverResetsDudeApAndEnemyRetaliates.
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
    public void HigherSequenceEnemyActsBeforeTheDudeInRoundTwo()
    {
        // P44 (combat.cc _combat_sequence): rounds 2+ are interleaved by Sequence, so an enemy that
        // out-sequences the dude takes its turn BEFORE the dude's. Round 1 still favours the attacker
        // (the dude here), but by the time the dude reaches its round-2 turn, the faster enemy has
        // already acted twice (round 1 + round 2). The old fixed-block model would show only once.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 100, ap: 10, seq: 1)); // SLOW
        // ap 4 = exactly one 3-AP punch per turn (it's adjacent, so no move), making the count exact.
        MapObject fast = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 4, seq: 20));
        fast.Team = 1; // Important-fix side effect (Task-2 review): see RoundRolloverResetsDudeApAndEnemyRetaliates.
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(fast)); // round 1: the dude (attacker) opens
        host.Animating.Clear();
        engine.ProcessAnimations();
        host.AttackOrder.Clear();            // ignore the dude's round-1 swing
        engine.EndPlayerTurn();              // hand round 1 over to the enemy

        for (int i = 0; i < 400 && !dude.IsDead; i++)
        {
            host.Animating.Clear();
            engine.Step();
            if (engine.Phase == CombatPhase.PlayerTurn)
            {
                if (engine.Round >= 2)
                    break;                   // the dude's round-2 turn has arrived
                engine.EndPlayerTurn();
            }
        }

        // The faster enemy acted in round 1 AND round 2 before the dude's round-2 slot.
        Assert.Equal(2, host.AttackOrder.Count(a => a == fast));
        Assert.Equal(2, engine.Round);
    }

    [Fact]
    public void LoseTurnEnemySkipsOneTurnThenActs()
    {
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        enemy.Team = 1; // Task 3: cross-team, like real game data — SetWhoHitMe/RegisterHit only stamp whoHitMe across teams
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
        // MaxDist was a placeholder 0 here until F1 (max_dist gate) made the field live; distance
        // 1 < max_dist 10 so it flees, matching the pre-gate behaviour this test locks.
        host.AiPackets[enemy] = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 10, 10, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude); // opens on the enemy's turn
        engine.Step();

        Assert.Contains(host.Transcripts, t => t.StartsWith("flee:"));
        Assert.True(HexGrid.Distance(enemy.HexTile, dude.HexTile) > 1); // backed away
        Assert.Equal(30, dude.CurrentHp);                               // did not attack
    }

    [Fact]
    public void ScriptSetFleeManeuverMakesAHealthyEnemyRunInsteadOfAttacking()
    {
        // P70: critter_set_flee_state(critter, 1) sets CRITTER_MANEUVER_FLEEING (0x04); _combat_ai's
        // first flee clause (combat_ai.cc:3074) runs the critter away even at full HP with no min_hp gate.
        const int ManeuverFleeing = 0x04;
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        int eTile = HexGrid.TileInDirection(20100, 0);
        MapObject enemy = host.AddCritter(NewCritter(tile: eTile, hp: 30, ap: 10)); // healthy, would normally attack
        host.AiPackets[enemy] = new AiPacket(13, "Coward", MinToHit: 0, MinHp: 0, 10, "", "");
        enemy.Maneuver |= ManeuverFleeing; // a script flagged it to flee
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Contains(host.Transcripts, t => t.StartsWith("flee:"));
        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("enemy-attack"));
        Assert.True(HexGrid.Distance(enemy.HexTile, dude.HexTile) > 1); // backed away
        Assert.Equal(30, dude.CurrentHp);                               // did not attack
    }

    [Fact]
    public void WithoutTheFleeManeuverTheSameHealthyEnemyAttacks()
    {
        // Control for the maneuver-flee gate: identical setup minus the bit → the enemy closes + attacks
        // (the byte-identical default — the FLEEING clause is inert unless a script sets it).
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(13, "Coward", MinToHit: 0, MinHp: 0, 0, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("flee:"));
    }

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

    [Theory]
    [InlineData(9, true)]    // distance 9 < max_dist 10 -> flees
    [InlineData(10, false)]  // distance 10 is NOT < 10 -> disengages. The fork's PR #675 '<=' would flee here.
    public void MaxDistBoundaryDecidesFleeingVersusDisengaging(int distance, bool expectFlee)
    {
        // ported from fallout2-ce src/combat_ai.cc _ai_run_away (:1183). The comparison is '<' at our
        // pinned e97087b. The maintained fork's PR #675 flips it to '<=', a hunk we rejected as
        // ungrounded — so distance == max_dist MUST disengage. Do not "fix" this to '<='.
        // Geometry check (distcheck scratch program): HexGrid.Distance(20100, TileInDirection(20100,0,n))
        // == n exactly for n=9 and n=10 (no edge wraparound at this tile), so the identity assumed by
        // this test holds verbatim.
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
        // CanEndCombat() from the brief is a placeholder name; the real public predicate fed by
        // WantsToStopFighting (CombatEngine.cs:2213, exit gate at :2203) is TryEndCombat().
        // Perception is bumped to 20 (fallback range PE*2=40 in combat) so that, pre-gate, the
        // enemy's un-gated 10-tile flee to distance ~20 does NOT accidentally drop it out of
        // WithinPerception and end the fight via the unrelated perception fallback in
        // WantsToStopFighting — that would make this pass pre-change for the wrong reason. With PE 20
        // the pre-gate combat correctly stays open (still perceived), and only the post-gate
        // DISENGAGING flag (checked before the perception fallback) can close it.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0, 10), hp: 5, ap: 10, perception: 20));
        host.AiPackets[enemy] = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 10, 10, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.True(engine.TryEndCombat(), "a disengaged sole hostile must not block leaving combat");
    }

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

    [Fact]
    public void EnemyThatCanNeverClearMinToHitFlees()
    {
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(99, "Hopeless", MinToHit: 99, MinHp: 0, 10, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        // unarmed to-hit (50) can never reach 99 → flee, never swing.
        Assert.Contains(host.Transcripts, t => t.StartsWith("flee:"));
        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("enemy-attack"));
        Assert.Equal(30, dude.CurrentHp);
    }

    [Fact]
    public void AFleeingCritterNeverLogsAFleeItDoesNotPerform()
    {
        // F18: TryFlee used to pick its retreat tile with a pathfinder that exempts the GOAL from the
        // blocked test, so it could propose an occupied tile; the walker then refused the move and the
        // transcript recorded a flight that never happened (denbus2-fight-flee logged the identical
        // 'flee: Cute Slave@11272 -> 10480' four times without the critter ever moving).
        // ported from fallout2-ce src/combat_ai.cc _ai_run_away (:1192): the retreat search passes
        // _make_path(..., a5 = 1), so a blocked candidate produces no path and the loop shrinks.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 5, ap: 10));
        host.AiPackets[enemy] = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 10, 10, "", "");

        // Occupy the full-AP retreat tile the search would otherwise choose, leaving nearer tiles free.
        int startTile = enemy.HexTile;
        int rotation = HexGrid.RotationTo(dude.HexTile, startTile);
        int fullDistanceTile = HexGrid.TileInDirection(startTile, rotation, 10);
        host.IsBlockedOverride = tile => tile == fullDistanceTile;

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        // With requireFreeDestination honoured, the blocked full-AP tile is rejected and the loop
        // shrinks to the next-nearer (dist=9) candidate, which IS free — the critter flees there for
        // real. A logged flee must therefore correspond to an actual move to a tile other than the
        // blocked one.
        Assert.Contains(host.Transcripts, t => t.StartsWith("flee:"));
        Assert.NotEqual(startTile, enemy.HexTile);
        Assert.NotEqual(fullDistanceTile, enemy.HexTile);
    }

    [Fact]
    public void HurtBipedEnemyHealsBeforeAttackingWithChemUse()
    {
        // P42 (_ai_check_drugs): a hurt BIPED enemy with chem_use + a healing item quaffs it (2 AP) on
        // its turn before attacking — stims_when_hurt_lots heals below 30% of max HP.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 100, ap: 10));
        enemy.CurrentHp = 20; // 20/100 = below the 30% hurt-lots ratio
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "", 0, ChemUse: 2);
        host.CarriesStimpak.Add(enemy);
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.True(enemy.CurrentHp > 20, "the enemy healed before attacking");
        Assert.DoesNotContain(enemy, host.CarriesStimpak); // the stimpak was consumed
    }

    [Fact]
    public void CleanEnemyNeverHealsEvenWhenHurtAndCarryingAStimpak()
    {
        // chem_use=clean → no heal, even hurt with a stimpak in the bag (the inert-by-default gate;
        // the slice's scorpion/peasant packets are clean → byte-identical goldens).
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 100, ap: 10));
        enemy.CurrentHp = 5;
        host.AiPackets[enemy] = new AiPacket(8, "Animal", MinToHit: 0, MinHp: 0, 0, "", "", 0, ChemUse: 0);
        host.CarriesStimpak.Add(enemy);
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Equal(5, enemy.CurrentHp);              // never healed
        Assert.Contains(enemy, host.CarriesStimpak);   // stimpak untouched
    }

    [Fact]
    public void AlwaysChemUseEnemyDrinksACombatDrugOnItsTurn()
    {
        // P78-M2 (_ai_check_drugs non-heal branch): a healthy ALWAYS-chem_use BIPED quaffs a buff drug
        // (100% chance) before attacking — it isn't hurt, so the heal branch passes and the drug branch fires.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 100, ap: 10));
        host.AiPackets[enemy] = new AiPacket(50, "Junkie", MinToHit: 0, MinHp: 0, 0, "", "", 0, ChemUse: 5);
        host.CombatDrugs[enemy] = 1;
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Contains(enemy, host.DrankCombatDrug);
        Assert.Equal(0, host.CombatDrugs[enemy]); // consumed
    }

    [Fact]
    public void CleanEnemyNeverDrinksACombatDrug()
    {
        // chem_use=clean → the chance is 0, so ShouldUse short-circuits WITHOUT drawing → no drink (the
        // inert-by-default invariant that keeps the clean-packet golden fights byte-identical).
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 100, ap: 10));
        host.AiPackets[enemy] = new AiPacket(8, "Animal", MinToHit: 0, MinHp: 0, 0, "", "", 0, ChemUse: 0);
        host.CombatDrugs[enemy] = 1;
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.DoesNotContain(enemy, host.DrankCombatDrug);
        Assert.Equal(1, host.CombatDrugs[enemy]); // untouched
    }

    [Fact]
    public void NpcWithCrippledArmsStillAttacksWithFistsInsteadOfSoftlocking()
    {
        // P78-M4: an enemy whose arms are both crippled can't wield its weapon — but unlike the DUDE
        // (blocked outright), an NPC drops to fists and still attacks the adjacent dude. The pure gate
        // (WeaponBlockedByCrippledArms) is covered by CrippledArmsGateWeaponAttacks; this proves the NPC
        // path calls it and falls back rather than stalling the turn.
        bool EnemyAttacks(int combatResults)
        {
            var wstats = new WeaponProtoStats(1, 6, 10, 0, 1, 0, 0, 1, 4, 0, 0, 0, -1, 0, 0); // a melee weapon
            MapObject item = new() { Id = 8, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0, Flags = 0, Pid = 8, Sid = -1 };
            var host = new FakeCombatHost { Equipped = (new ProtoInfo(8, 0, 0, 0, 0x001, 3, Weapon: wstats), item) };
            MapObject dude = host.SetDude(NewCritter(20100, hp: 30, ap: 10));
            MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, skill: 100));
            enemy.CombatResults = combatResults;
            host.AiPackets[enemy] = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 0, 0, "", "");
            var engine = new CombatEngine(host, new MinRng());
            engine.BeginScriptAggro(enemy, dude);
            for (int i = 0; i < 50 && engine.Phase == CombatPhase.EnemyTurn; i++) { host.Animating.Clear(); engine.Step(); }
            return dude.CurrentHp < 30;
        }

        Assert.True(EnemyAttacks(0));                              // armed, no crip → attacks
        Assert.True(EnemyAttacks(CriticalTables.DamCripArmAny));   // both arms crippled → fists, still attacks
    }

    [Fact]
    public void EnemyWithACrippledArmSwitchesToACarriedOneHandedBackupInsteadOfFists()
    {
        // ported from fallout2-ce src/combat_ai.cc _ai_try_attack (:2800): when the wielded weapon is
        // blocked by a crippled arm (WeaponBlockedByCrippledArms), _ai_switch_weapons is called — it isn't
        // an automatic drop to fists. With a one-handed backup in the bag matching the ai.txt preference,
        // the enemy should re-arm with it (and still land the attack this same turn), not punch.
        var host = new FakeCombatHost { LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, skill: 100));
        enemy.CombatResults = CriticalTables.DamCripArmLeft; // one arm crippled
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: 2 /* melee */);
        host.Equipped = (TestWeapon(0x100, 0x206, 5, 10), TestItem(0x100)); // two-handed → blocked by the crip

        MapObject backupItem = TestItem(0x201);
        host.InventoryWeapons[enemy] = [(TestWeapon(0x201, 0x03, 4, 10), backupItem)]; // one-handed melee (swing)

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(enemy, dude);
        for (int i = 0; i < 50 && engine.Phase == CombatPhase.EnemyTurn; i++) { host.Animating.Clear(); engine.Step(); }

        Assert.Contains((enemy, backupItem), host.Equips); // switched to the backup, not just dropped to fists
        Assert.True(dude.CurrentHp < 30);                  // and attacked with it this turn
    }

    [Fact]
    public void UnarmedEnemyOutOfRangeSwitchesToACarriedWeaponBeforeClosingIn()
    {
        // ported from fallout2-ce src/combat_ai.cc _ai_try_attack (:2823): COMBAT_BAD_SHOT_OUT_OF_RANGE
        // with weapon == null calls _ai_switch_weapons before falling back to _ai_move_closer. An unarmed
        // enemy standing 3 hexes off with a ranged backup in its bag should arm itself and shoot, rather
        // than walking in to throw punches.
        var host = new FakeCombatHost { LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        int eTile = Step(20100, 0, 3); // 3 hexes away — out of fist range (1), never armed to begin with
        MapObject enemy = host.AddCritter(NewCritter(tile: eTile, hp: 30, ap: 10, skill: 100));
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: 4 /* ranged */);
        // no host.Equipped — genuinely unarmed

        MapObject rangedItem = TestItem(0x201);
        host.InventoryWeapons[enemy] = [(TestWeapon(0x201, 0x06, 4, 10), rangedItem)]; // a ranged pistol

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Contains((enemy, rangedItem), host.Equips); // armed itself instead of walking in unarmed
        Assert.Equal(eTile, enemy.HexTile);                // in range with the new weapon → no approach needed
    }

    // --- P68: AI-packet enemy distance (stay / snipe) ---------------------------------

    [Fact]
    public void StayDistanceEnemyHoldsPositionInsteadOfApproaching()
    {
        // P68 (DISTANCE_STAY, combat_ai.cc:1223/2361): an enemy with distance=stay never closes the gap —
        // it only attacks if already in range. Out of range here → it holds (a turret/stationary guard).
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        int eTile = HexGrid.TileInDirection(HexGrid.TileInDirection(HexGrid.TileInDirection(20100, 0), 0), 0);
        MapObject enemy = host.AddCritter(NewCritter(tile: eTile, hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(99, "Turret", MinToHit: 0, MinHp: 0, 0, "stay", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Equal(eTile, enemy.HexTile);  // held position — never approached
        Assert.Equal(30, dude.CurrentHp);    // out of range, so no attack either
    }

    [Fact]
    public void AbsentDistanceEnemyStillApproaches()
    {
        // The byte-identical control: the golden scorpion (pkt8) / peasant (pkt14) carry NO distance field
        // → the engine default -1 → OnYourOwn here → they approach exactly as before. Only stay/snipe differ.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        int eTile = HexGrid.TileInDirection(HexGrid.TileInDirection(HexGrid.TileInDirection(20100, 0), 0), 0);
        MapObject enemy = host.AddCritter(NewCritter(tile: eTile, hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(8, "Scorpion", MinToHit: 0, MinHp: 0, 0, "", ""); // no distance
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.True(HexGrid.Distance(enemy.HexTile, dude.HexTile) < HexGrid.Distance(eTile, dude.HexTile));
    }

    [Fact]
    public void SnipeEnemyBacksAwayMultipleHexesTowardItsPreferredRange()
    {
        // P68/P78-M3 (DISTANCE_SNIPE, combat_ai.cc:3001): a ranged sniper inside its preferred range kites —
        // P78-M3 makes it a MULTI-step retreat toward SnipeRange (5), AP-limited, not the old one-step.
        var host = new FakeCombatHost { LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        int eTile = HexGrid.TileInDirection(20100, 0); // adjacent (distance 1)
        MapObject enemy = host.AddCritter(NewCritter(tile: eTile, hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(13, "Sniper", MinToHit: 0, MinHp: 0, 0, "snipe", "");
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100)); // a loaded ranged gun
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Equal(5, HexGrid.Distance(enemy.HexTile, dude.HexTile)); // backed off to SnipeRange (1 → 5)
        Assert.Equal(30, dude.CurrentHp);                              // did not shoot this turn
    }

    [Fact]
    public void EnemyHoldsAGunShotThatWouldPassThroughATeammate()
    {
        // P78-M3 (_combat_safety_invalidate_weapon, combat.cc:2249): a gun enemy won't fire a shot whose hex
        // line passes through a living teammate — it holds and APPROACHES instead of friendly-firing the ally.
        // Observable = whether the shooter had to MOVE: in range with a clear line it shoots in place; with
        // an ally on the line it can't shoot, so it closes the gap (moves off its tile).
        bool ApproachedInsteadOfShooting(bool placeAllyOnLine)
        {
            var host = new FakeCombatHost { LoadedAmmoCount = 10 };
            MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
            int eTile = Step(20100, 0, 4); // the shooter, 4 hexes out, gun range 40 → in range
            MapObject enemy = host.AddCritter(NewCritter(tile: eTile, hp: 30, ap: 10, skill: 120));
            enemy.Team = 3;
            host.AiPackets[enemy] = new AiPacket(13, "Gunner", MinToHit: 0, MinHp: 0, 0, "", "");
            host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100));
            // an ally either exactly on the shooter→dude line (distance 2 from the dude) or well off it.
            MapObject ally = host.AddCritter(NewCritter(tile: placeAllyOnLine ? Step(20100, 0, 2) : Step(20100, 2, 6), hp: 30));
            ally.Team = 3;
            var engine = new CombatEngine(host, new MinRng());
            engine.BeginScriptAggro(enemy, dude);
            for (int i = 0; i < 50 && engine.Phase == CombatPhase.EnemyTurn; i++) { host.Animating.Clear(); engine.Step(); }
            return enemy.HexTile != eTile; // did it have to move off its firing position?
        }

        Assert.False(ApproachedInsteadOfShooting(placeAllyOnLine: false)); // clear line → shoots in place
        Assert.True(ApproachedInsteadOfShooting(placeAllyOnLine: true));   // ally on the line → holds, approaches
    }

    // --- P43: AI inventory weapon switch (best_weapon) --------------------------------

    private static ProtoInfo TestWeapon(int pid, int ext, int min, int max, int cost = 0, int dmgType = 0)
    {
        // ext low nibble = the attack-anim code (0x06 single-fire ranged, 0x03 swing melee, 0x01 punch).
        var w = new WeaponProtoStats(0, min, max, dmgType, 40, 0, 0, 0, 4, 0, 0, 0, -1, 12, 0);
        return new ProtoInfo(pid, 0, 0x06000000, 0, ext, 3, Cost: cost, Weapon: w);
    }

    private static MapObject TestItem(int pid) => new()
    {
        Id = pid, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0x06000000, Flags = 0, Pid = pid, Sid = -1,
    };

    [Fact]
    public void AiSwitchesToTheCarriedBackupMatchingItsWeaponPreference()
    {
        // _ai_switch_weapons → _ai_search_inven_weap → _ai_best_weapon: a biped enemy whose gun went dry
        // draws the carried backup its best_weapon preference favours. ranged_over_melee → the pistol.
        var host = new FakeCombatHost { LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: 3);
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100)); // the dry equipped gun

        MapObject rangedItem = TestItem(0x201);
        host.InventoryWeapons[enemy] =
        [
            (TestWeapon(0x200, 0x03, 1, 6), TestItem(0x200)),    // a melee club (swing)
            (TestWeapon(0x201, 0x06, 4, 10), rangedItem),        // a ranged pistol (single)
        ];
        var engine = new CombatEngine(host, new MinRng());

        int chosen = engine.ProbeAiWeaponSwitch(enemy, dude);

        Assert.Equal(0x201, chosen);                       // ranged_over_melee → the pistol
        Assert.Contains((enemy, rangedItem), host.Equips); // and it was actually wielded
    }

    [Fact]
    public void AiPrefersABlastWeaponWhenExtraVictimsPushItsScoreAhead()
    {
        // LIVENESS PROOF (task 3, step 5): the ×(extras+1) factor is not just wired, it changes a real
        // decision. best_weapon = -1 (default) → the RANGED/THROW preference orders differ, so the
        // choice hinges on the |Δavg| > 5 damage override (combat_ai.cc:1963). Weapon A (a ranged rifle,
        // avg 10) alone beats weapon B (a thrown frag grenade, base avg 7 — never > 5 ahead of A) — but
        // with 2 extra critters standing around the defender's tile within the grenade's radius-2 blast,
        // B's score becomes 7 * (2+1) = 21, now 11 ahead of A, clears the override and wins.
        var host = new FakeCombatHost { LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        // The enemy stands well outside the grenade's own radius-2 blast (distance 5) so its own tile
        // never counts itself as an "extra" — keeps the extras count exactly the 2 critters seeded below.
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0, distance: 5), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(8, "Grunt", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: -1);
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100)); // some other equipped gun

        MapObject rifleItem = TestItem(0x201);
        MapObject grenadeItem = TestItem(0x202);
        host.InventoryWeapons[enemy] =
        [
            (TestWeapon(0x201, 0x06, 10, 10, dmgType: 0), rifleItem),               // ranged rifle, avg 10
            (TestWeapon(0x202, 0x05, 4, 10, dmgType: 6 /* EXPLOSION */), grenadeItem), // thrown grenade, base avg 7
        ];

        // Two extra critters standing adjacent to the dude (well within the grenade's radius-2 spiral).
        host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(dude.HexTile, 1), hp: 30, ap: 10));
        host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(dude.HexTile, 3), hp: 30, ap: 10));

        var engine = new CombatEngine(host, new MinRng());

        int chosen = engine.ProbeAiWeaponSwitch(enemy, dude);

        Assert.Equal(0x202, chosen); // the grenade, boosted by its 2 extras, beats the raw-damage rifle
    }

    [Fact]
    public void AiPrefersARocketLauncherWhenExtraVictimsPushItsScoreAhead()
    {
        // Review finding "Important 2": WeaponDamageRadius's ranged/fire-single test used
        // w.AnimationCode (the held-weapon-sprite selector, art.h WEAPON_ANIMATION_*) instead of the
        // extendedFlags nibble (item.cc _attack_anim[6] == ANIM_FIRE_SINGLE). TestWeapon always leaves
        // AnimationCode == 0, so under the bug this branch NEVER returns a nonzero radius for ANY ranged
        // weapon — a rocket launcher's own extras are always counted as 0 and it can never win a
        // close-call vs a plain rifle.
        //
        // Final-review correction (2026-08-14): the AI's own extras spiral is ALWAYS bounded by the
        // grenade radius (2), never the rocket radius (3) — traced combat_ai.cc:1860 (a > 0 gate
        // only) → combat.cc:4033-4039 (isGrenade bounds the walk) → item.cc:1968-1972
        // (weaponIsGrenade is damage-TYPE only, no animation gate) → item.cc:3376
        // (gGrenadeExplosionRadius = 2). So the two extra critters below sit at hex-distance EXACTLY 2
        // (ring 2), inside the AI-scoring spiral's actual radius-2 bound — see the companion negative
        // test below, which places them at ring 3 instead and asserts they are NOT counted.
        var host = new FakeCombatHost { LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        // Attacker stands well outside its own rocket's radius-2 (AI-scoring) blast around the defender.
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0, distance: 8), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(8, "Grunt", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: -1);
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100)); // some other equipped gun

        MapObject rifleItem = TestItem(0x201);
        MapObject rocketItem = TestItem(0x202);
        host.InventoryWeapons[enemy] =
        [
            (TestWeapon(0x201, 0x06, 10, 10, dmgType: 0), rifleItem), // ranged rifle, avg 10, NORMAL damage
            (TestWeapon(0x202, 0x06, 4, 10, dmgType: 6 /* EXPLOSION */), rocketItem), // ranged rocket launcher, base avg 7
        ];

        // Two extra critters at exactly hex-distance 2 from the dude — ring 2, inside the radius-2 walk.
        host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(dude.HexTile, 0, distance: 2), hp: 30, ap: 10));
        host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(dude.HexTile, 2, distance: 2), hp: 30, ap: 10));

        var engine = new CombatEngine(host, new MinRng());

        int chosen = engine.ProbeAiWeaponSwitch(enemy, dude);

        Assert.Equal(0x202, chosen); // the rocket, boosted by its 2 extras (7*3=21 vs rifle's 10), wins
    }

    [Fact]
    public void AiRocketLauncherScoringIgnoresVictimsAtRingThree()
    {
        // Final-review negative counterpart (2026-08-14) to AiPrefersARocketLauncherWhenExtraVictims-
        // PushItsScoreAhead: proves the AI-scoring spiral radius is pinned at 2, not 3. Same setup as
        // that test, EXCEPT the two extra critters sit at hex-distance EXACTLY 3 (ring 3) — outside the
        // AI-scoring spiral's radius-2 bound (item.cc:3376 gGrenadeExplosionRadius; see the chain cited
        // in ExplosionExtrasAt's doc comment). With them correctly excluded, the rocket's score stays
        // at its unboosted 7 (extras = 0 → 7*(0+1) = 7), which is within 5 of the rifle's 10, so the
        // tiebreak falls to item cost — both weapons cost 0 here, so the incumbent (rifle) keeps the
        // win. If the spiral radius were wrongly reverted to 3 (the pre-fix bug this whole batch
        // corrects), these same ring-3 critters WOULD be counted, the rocket's score would jump to
        // 7*3=21, clear the rifle's 10 by more than 5, and `chosen` would flip to the rocket
        // (0x202) — so this assertion fails under the radius-3 regression. Verified manually by
        // temporarily reverting ExplosionExtrasAt's spiralRadius to 3: this test then fails with
        // Assert.Equal(0x201, chosen) expecting 0x201 but getting 0x202.
        var host = new FakeCombatHost { LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0, distance: 8), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(8, "Grunt", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: -1);
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100)); // some other equipped gun

        MapObject rifleItem = TestItem(0x201);
        MapObject rocketItem = TestItem(0x202);
        host.InventoryWeapons[enemy] =
        [
            (TestWeapon(0x201, 0x06, 10, 10, dmgType: 0), rifleItem), // ranged rifle, avg 10, NORMAL damage
            (TestWeapon(0x202, 0x06, 4, 10, dmgType: 6 /* EXPLOSION */), rocketItem), // ranged rocket launcher, base avg 7
        ];

        // Two extra critters at exactly hex-distance 3 from the dude — ring 3 only, OUTSIDE radius 2.
        host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(dude.HexTile, 0, distance: 3), hp: 30, ap: 10));
        host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(dude.HexTile, 2, distance: 3), hp: 30, ap: 10));

        var engine = new CombatEngine(host, new MinRng());

        int chosen = engine.ProbeAiWeaponSwitch(enemy, dude);

        Assert.Equal(0x201, chosen); // ring-3 victims NOT counted → rocket stays unboosted → rifle wins
    }

    [Fact]
    public void AiRecognizesAThrownPlasmaGrenadeAsABlastWeapon()
    {
        // Review finding "Important 1": the blastDamage test used damage-type constants 6/7/8
        // (EXPLOSION/PLASMA/EMP per the task brief's WRONG numbering) instead of the reference's
        // 6/3/5 (proto_types.h:59-67). Under the bug, a PLASMA(=3) grenade's blastDamage is false, so
        // it is never treated as a grenade and never gets the ×(extras+1) boost.
        var host = new FakeCombatHost { LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0, distance: 5), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(8, "Grunt", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: -1);
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100));

        MapObject rifleItem = TestItem(0x201);
        MapObject grenadeItem = TestItem(0x202);
        host.InventoryWeapons[enemy] =
        [
            (TestWeapon(0x201, 0x06, 10, 10, dmgType: 0), rifleItem),                // ranged rifle, avg 10
            (TestWeapon(0x202, 0x05, 4, 10, dmgType: 3 /* PLASMA */), grenadeItem),  // thrown plasma grenade, base avg 7
        ];

        host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(dude.HexTile, 1), hp: 30, ap: 10));
        host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(dude.HexTile, 3), hp: 30, ap: 10));

        var engine = new CombatEngine(host, new MinRng());

        int chosen = engine.ProbeAiWeaponSwitch(enemy, dude);

        Assert.Equal(0x202, chosen); // the plasma grenade, boosted by its 2 extras, beats the rifle
    }

    [Fact]
    public void AiRecognizesAThrownEmpGrenadeAsABlastWeapon()
    {
        // Same as the plasma case, but for EMP (=5), also miscoded as 8 by the bug.
        var host = new FakeCombatHost { LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0, distance: 5), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(8, "Grunt", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: -1);
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100));

        MapObject rifleItem = TestItem(0x201);
        MapObject grenadeItem = TestItem(0x202);
        host.InventoryWeapons[enemy] =
        [
            (TestWeapon(0x201, 0x06, 10, 10, dmgType: 0), rifleItem),              // ranged rifle, avg 10
            (TestWeapon(0x202, 0x05, 4, 10, dmgType: 5 /* EMP */), grenadeItem),  // thrown EMP grenade, base avg 7
        ];

        host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(dude.HexTile, 1), hp: 30, ap: 10));
        host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(dude.HexTile, 3), hp: 30, ap: 10));

        var engine = new CombatEngine(host, new MinRng());

        int chosen = engine.ProbeAiWeaponSwitch(enemy, dude);

        Assert.Equal(0x202, chosen); // the EMP grenade, boosted by its 2 extras, beats the rifle
    }

    [Fact]
    public void AiGivesNoBlastRadiusToANonBlastWeapon()
    {
        // Sanity check: a thrown weapon with ordinary (non-blast) damage never gets the ×(extras+1)
        // boost even with extra critters standing right next to the defender — WeaponDamageRadius
        // returns 0, so ExplosionExtrasAt never fires (radius <= 0 short-circuit).
        var host = new FakeCombatHost { LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0, distance: 5), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(8, "Grunt", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: -1);
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100));

        MapObject rifleItem = TestItem(0x201);
        MapObject throwingKnifeItem = TestItem(0x202);
        host.InventoryWeapons[enemy] =
        [
            (TestWeapon(0x201, 0x06, 10, 10, dmgType: 0), rifleItem),                // ranged rifle, avg 10
            (TestWeapon(0x202, 0x05, 4, 10, dmgType: 0 /* NORMAL */), throwingKnifeItem), // thrown knife, base avg 7
        ];

        host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(dude.HexTile, 1), hp: 30, ap: 10));
        host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(dude.HexTile, 3), hp: 30, ap: 10));

        var engine = new CombatEngine(host, new MinRng());

        int chosen = engine.ProbeAiWeaponSwitch(enemy, dude);

        Assert.Equal(0x201, chosen); // no blast boost for the knife → the rifle's raw damage still wins
    }

    [Fact]
    public void AiKeepsFistsWhenNoCarriedWeaponQualifies()
    {
        // The inert-by-default invariant: an empty inventory → no candidate → fists (-1), nothing wielded.
        // The golden-fight critters carry no weapons → the switch never fires → byte-identical goldens.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(8, "Animal", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: -1);
        var engine = new CombatEngine(host, new MinRng());

        Assert.Equal(-1, engine.ProbeAiWeaponSwitch(enemy, dude));
        Assert.Empty(host.Equips);
    }

    [Fact]
    public void NonBipedCritterNeverSearchesInventory()
    {
        // _ai_search_inven_weap gates on BIPED/ROBOTIC (combat_ai.cc:2004): a QUADRUPED scorpion never
        // switches even with a backup in the bag — why the arcaves scorpion goldens are byte-identical.
        var host = new FakeCombatHost { LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        int[] b = new int[35];
        b[CritterStat.MaximumHitPoints] = 30;
        b[CritterStat.MaximumActionPoints] = 10;
        var quadProto = new CritterProtoStats(0, 0, 0, b, new int[35], new int[18], 1 /* BODY_TYPE_QUADRUPED */, 0, 0, 0);
        var enemy = new MapObject
        {
            Id = 2, HexTile = HexGrid.TileInDirection(20100, 0), X = 0, Y = 0, Frame = 0, Rotation = 0,
            Fid = 0x01000000, Flags = 0, Pid = 0x01000005, Sid = -1,
        };
        enemy.CurrentHp = 30;
        host.AddCritter((enemy, quadProto));
        host.AiPackets[enemy] = new AiPacket(8, "Scorpion", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: -1);
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100));
        host.InventoryWeapons[enemy] = [(TestWeapon(0x201, 0x06, 4, 10), TestItem(0x201))];
        var engine = new CombatEngine(host, new MinRng());

        Assert.Equal(-1, engine.ProbeAiWeaponSwitch(enemy, dude)); // non-biped → no inventory search
        Assert.Empty(host.Equips);
    }

    [Fact]
    public void AiRejectsACarriedWeaponBelowItsMinToHitSkill()
    {
        // _ai_can_use_weapon: skillGetValue(critter, weaponSkill) < min_to_hit → not a candidate. With
        // all skills 0 and min_to_hit 50, the carried pistol is rejected → fists.
        var host = new FakeCombatHost { LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, skill: 0));
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 50, MinHp: 0, 0, "", "", 0, 0, BestWeapon: 3);
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100));
        host.InventoryWeapons[enemy] = [(TestWeapon(0x201, 0x06, 4, 10), TestItem(0x201))];
        var engine = new CombatEngine(host, new MinRng());

        Assert.Equal(-1, engine.ProbeAiWeaponSwitch(enemy, dude)); // skill 0 < 50 → no candidate
        Assert.Empty(host.Equips);
    }

    // --- Ground-pickup fallback (_ai_search_environ / _ai_retrieve_object) -----------------------

    [Fact]
    public void AiRemembersAGroundWeaponAcrossTurnsAndRetrievesItLater()
    {
        // Issue 3 (review): the non-adjacent "start a walk, return not-yet, remember the item, retrieve
        // it on a later turn" path had no test. First call: TryRetrieveItem reports "not adjacent yet" →
        // fists this turn, but the item is remembered. Second call: the memory resumes and the item is
        // actually retrieved + wielded — proving the cross-turn state in CombatEngine._aiLastItem works.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: Step(20100, 0, 1), hp: 30, ap: 10, skill: 120));
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: 3);

        MapObject groundItem = TestItem(0x300);
        groundItem.HexTile = Step(20100, 0, 4); // within perception(5)+5 of the enemy, not adjacent
        host.Ground.Add((TestWeapon(0x300, 0x06, 4, 10), groundItem));
        host.RetrieveResults[groundItem] = new Queue<bool>([false]); // first attempt: not adjacent yet

        var engine = new CombatEngine(host, new MinRng());

        Assert.Equal(-1, engine.ProbeAiWeaponSwitch(enemy, dude));    // remembered, not retrieved yet
        Assert.Empty(host.Equips);
        Assert.Equal(0x300, engine.ProbeAiWeaponSwitch(enemy, dude)); // resumed + retrieved this turn
        Assert.Contains((enemy, groundItem), host.Equips);
        Assert.Contains(groundItem, enemy.Inventory);
    }

    [Fact]
    public void TwoCrittersRememberingTheSameGroundItemDoNotBothRetrieveIt()
    {
        // Issue 2 (review): the reference's _ai_retrieve_object re-checks item->owner (combat_ai.cc:2250)
        // before trusting a remembered item — someone else may have already claimed it. Two unarmed
        // critters that both remember the same ground weapon must NOT both end up with it in their
        // inventory: whichever critter's turn resolves first takes it; the other's stale memory is
        // dropped and it gets nothing (fists), not a duplicate MapObject reference.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy1 = host.AddCritter(NewCritter(tile: Step(20100, 0, 1), hp: 30, ap: 10, skill: 120));
        MapObject enemy2 = host.AddCritter(NewCritter(tile: Step(20100, 1, 1), hp: 30, ap: 10, skill: 120));
        host.AiPackets[enemy1] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: 3);
        host.AiPackets[enemy2] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: 3);

        MapObject groundItem = TestItem(0x300);
        groundItem.HexTile = Step(20100, 0, 4);
        host.Ground.Add((TestWeapon(0x300, 0x06, 4, 10), groundItem));
        // Both critters' first attempt reports "not adjacent yet" so both end up remembering the item.
        host.RetrieveResults[groundItem] = new Queue<bool>([false, false]);

        var engine = new CombatEngine(host, new MinRng());
        Assert.Equal(-1, engine.ProbeAiWeaponSwitch(enemy1, dude)); // enemy1 remembers it
        Assert.Equal(-1, engine.ProbeAiWeaponSwitch(enemy2, dude)); // enemy2 remembers the SAME item

        // Round 2: enemy1 resolves first (queue drained → default "still on ground" success), claiming it.
        Assert.Equal(0x300, engine.ProbeAiWeaponSwitch(enemy1, dude));
        Assert.Contains(groundItem, enemy1.Inventory);

        // enemy2's remembered item is now stale (no longer on the ground) — must NOT be retrieved too.
        Assert.Equal(-1, engine.ProbeAiWeaponSwitch(enemy2, dude));
        Assert.DoesNotContain(groundItem, enemy2.Inventory);
        Assert.Single(host.Equips); // only ever wielded once, by enemy1
        Assert.DoesNotContain((enemy2, groundItem), host.Equips);
    }

    [Fact]
    public void RoboticCritterNeverRetrievesAGroundWeapon()
    {
        // Issue 1 (review): _ai_search_environ (combat_ai.cc:2178/2180) gates on BODY_TYPE_BIPED alone —
        // stricter than the bag-search gate (_ai_search_inven_weap), which also admits ROBOTIC. A robotic
        // critter with an empty bag must NOT walk over and pick up a ground weapon.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        int[] b = new int[35];
        b[CritterStat.MaximumHitPoints] = 30;
        b[CritterStat.MaximumActionPoints] = 10;
        b[CritterStat.Perception] = 5;
        var roboticProto = new CritterProtoStats(0, 0, 0, b, new int[35], new int[18], 2 /* BODY_TYPE_ROBOTIC */, 0, 0, 0);
        var enemy = new MapObject
        {
            Id = 2, HexTile = Step(20100, 0, 1), X = 0, Y = 0, Frame = 0, Rotation = 0,
            Fid = 0x01000000, Flags = 0, Pid = 0x01000005, Sid = -1,
        };
        enemy.CurrentHp = 30;
        host.AddCritter((enemy, roboticProto));
        host.AiPackets[enemy] = new AiPacket(12, "Robot", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: 3);

        MapObject groundItem = TestItem(0x300);
        groundItem.HexTile = Step(20100, 0, 4);
        host.Ground.Add((TestWeapon(0x300, 0x06, 4, 10), groundItem));

        var engine = new CombatEngine(host, new MinRng());

        Assert.Equal(-1, engine.ProbeAiWeaponSwitch(enemy, dude));
        Assert.Empty(host.Equips);
        Assert.Empty(host.RetrieveAttempts); // never even tried — the BIPED-only gate returns first
        Assert.DoesNotContain(groundItem, enemy.Inventory);
    }

    [Fact]
    public void BothArmsCrippledNeverRetrievesOrWieldsAGroundWeapon()
    {
        // Important 1 (final review): _ai_can_use_weapon's FIRST check is both-arms-crippled → false
        // (combat_ai.cc:1974-1977), BEFORE the two-handed gate — a both-arms-crippled critter can never
        // wield ANY weapon, ground or bag. The bag-search loop already gated on bothArmsCrippled (:2432);
        // the ground-pickup branch that follows it did not, so a both-arms-crippled BIPED with an empty
        // bag would walk to a ground weapon, retrieve it, and wield it anyway.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: Step(20100, 0, 1), hp: 30, ap: 10, skill: 120));
        enemy.CombatResults = CriticalTables.DamCripArmAny; // both arms crippled
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: 3);

        MapObject groundItem = TestItem(0x300);
        groundItem.HexTile = Step(20100, 0, 4); // within perception+5, on the way it would otherwise take
        host.Ground.Add((TestWeapon(0x300, 0x06, 4, 10), groundItem));

        var engine = new CombatEngine(host, new MinRng());

        Assert.Equal(-1, engine.ProbeAiWeaponSwitch(enemy, dude));
        Assert.Empty(host.Equips);
        Assert.Empty(host.RetrieveAttempts); // never even tried — the both-arms-crippled gate returns first
        Assert.DoesNotContain(groundItem, enemy.Inventory);
    }

    [Fact]
    public void EnemySwitchedToAnEmptyGunDoesNotFireUnloaded()
    {
        // Important 2 (final review): the candidate gate (:2451-2453) now admits a ranged weapon with
        // WeaponAmmo <= 0 when a matching caliber is carried, but the reload attempt (:2661-2673) only
        // ever re-checks the PRE-switch weapon — a switched-to gun with an empty magazine used to fire
        // straight through EnemyAttack (:3113-3114), taking AmmoQuantity to -1 for a free unloaded shot.
        // ported from fallout2-ce src/combat_ai.cc _ai_try_attack (:2731-2757): the reference loops
        // _combat_check_bad_shot after every _ai_switch_weapons call and reloads on NO_AMMO before ever
        // firing, rather than shooting an unloaded weapon.
        var host = new FakeCombatHost { LoadedAmmoCount = 0 }; // every gun in play reports an empty magazine
        host.CarriedCalibersOverride = [0];                    // matches TestWeapon's default caliber
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        int eTile = Step(20100, 0, 5); // out of fist range, well inside a gun's MaxRange1
        MapObject enemy = host.AddCritter(NewCritter(tile: eTile, hp: 30, ap: 10, skill: 100));
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "", 0, 0, BestWeapon: 4 /* ranged */);
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100)); // dry equipped gun, can't reload (fake host)

        MapObject backupItem = TestItem(0x201);
        backupItem.AmmoQuantity = 0; // sentinel: an unfired weapon's ammo must never go negative from here
        host.InventoryWeapons[enemy] = [(TestWeapon(0x201, 0x06, 4, 10), backupItem)]; // a second gun, ALSO dry

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Contains((enemy, backupItem), host.Equips); // it DID switch to the backup...
        Assert.Equal(30, dude.CurrentHp);                  // ...but never fired it unloaded
        Assert.True(backupItem.AmmoQuantity >= 0);         // ...and never decremented into negative "phantom ammo"
    }

    [Fact]
    public void AllySwitchedToAnEmptyGunDoesNotFireUnloaded()
    {
        // Companion-path counterpart of EnemySwitchedToAnEmptyGunDoesNotFireUnloaded — 4227b75 only
        // patched TryEnemyAction; TryAllyAction (:2974-3022) has the identical shape (dry-gun-switch
        // → AiSwitchWeapon admits an empty-magazine gun on a matching carried caliber → fire at ~:3022
        // with no post-switch reload check), so a companion could fire a gun it never loaded and drive
        // its ammo negative. ported from fallout2-ce src/combat_ai.cc _ai_try_attack (:2731-2757): the
        // reference reloads on NO_AMMO before ever firing, for any combatant (friendly or hostile).
        var host = new FakeCombatHost { LoadedAmmoCount = 0 }; // every gun in play reports an empty magazine
        host.CarriedCalibersOverride = [0];                    // matches TestWeapon's default caliber
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject ally = host.AddAlly(
            NewCritter(tile: HexGrid.TileInDirection(20100, 2), hp: 30, ap: 10, skill: 100), CompanionAi.Default);
        // ap: 1 (the CritterState MaximumActionPoints stat floors at 1, so 0 still clamps to 1) — the
        // enemy shares the same global host.Equipped dry gun (the fake host has no per-critter wield
        // map), so with a real AP budget it would throw harmless fist-swings at the dude every turn
        // while we wait for the ally's slot. Placed 5 hexes out (well outside fist range but inside a
        // gun's MaxRange1, like the enemy-path fixture) so even its dry-switch-to-fists fallback can't
        // reach the dude with 1 AP/round (a single hex of approach) — it just plods closer, never
        // attacking, letting the loop below reach the dude's (pass-through) and then the ally's slot.
        MapObject enemy = host.AddCritter(NewCritter(tile: Step(20100, 0, 5), hp: 30, ap: 1));
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100)); // dry equipped gun, can't reload (fake host)

        MapObject backupItem = TestItem(0x201);
        backupItem.AmmoQuantity = 0; // sentinel: an unfired weapon's ammo must never go negative from here
        host.InventoryWeapons[ally] = [(TestWeapon(0x201, 0x06, 4, 10), backupItem)]; // a second gun, ALSO dry

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(enemy, dude); // the enemy opens combat (order: enemy, dude, ally)
        for (int i = 0; i < 200 && engine.Phase != CombatPhase.PlayerTurn; i++)
        {
            host.Animating.Clear();
            engine.Step(); // the AP-starved enemy plods one hex closer, never in range to attack
        }
        host.Transcripts.Clear();
        engine.EndPlayerTurn(); // hand off to the ally's slot
        host.Animating.Clear();
        // ONE Step() only — the ally's dry-gun check fires on its own equipped weapon regardless of
        // target distance (the gun's fake MaxRange1 is 40, so "in range" isn't the gate here), so this
        // single call already exercises the switch-then-reload-check. Looping further would let the
        // ally spend several ROUNDS closing the 5-6 hex gap and land a legitimate fists punch once
        // adjacent — a real (non-buggy) melee fallback that would give a false "still fires" reading.
        engine.Step();

        Assert.Contains((ally, backupItem), host.Equips); // it DID switch to the backup...
        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("ally-attack")); // ...but never fired it unloaded
        Assert.True(backupItem.AmmoQuantity >= 0);         // ...and never decremented into negative "phantom ammo"
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
    public void BlindEnemyWithBlindHurtFlagFleesInsteadOfAttacking()
    {
        // P34-M2: a critter at full HP but carrying a DAM_BLIND result bit flees when its
        // AI packet's hurt_too_much mask matches (combat_ai.cc:3076) — independent of min_hp.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        // Task 3: PE 8, not the fixture-wide default of 5 — CritterState.Perception subtracts 5 flat
        // when DamBlind is set (stat.cc:191 critterGetStat), and PruneEscapedHostiles now runs
        // isWithinPerception(enemy, danger-source) every Step (combat_ai.cc _combatai_want_to_stop,
        // :3227-3228) BEFORE the enemy's own turn/hurt_too_much decision executes. At the default PE 5,
        // blind zeroes perception outright (5 − 5 = 0), so even an adjacent, living whoHitMe is never
        // "perceived" and the enemy gets pruned from combat before it can take the very turn this test
        // means to exercise. PE 8 leaves 3 after the blind malus, still enough to pass the fallback
        // isWithinPerception check at distance 1 (PE×2 in combat = 6) — a critter that fights half-blind
        // rather than one with literally zero perception, which is the scenario this test probes.
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, perception: 8));
        host.AiPackets[enemy] = new AiPacket(8, "Scorpion", MinToHit: 0, MinHp: 0, 10, "", "",
            HurtTooMuch: CriticalTables.DamBlind);
        enemy.CombatResults |= CriticalTables.DamBlind;
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Contains(host.Transcripts, t => t.StartsWith("flee:"));
        Assert.Equal(30, dude.CurrentHp); // did not attack
    }

    [Fact]
    public void EnemyWithHurtFlagButNoMatchingResultBitStillAttacks()
    {
        // The AND-gate: hurt_too_much is set but no matching crip/blind bit on CombatResults,
        // so the enemy still attacks (the inert-by-default invariant — goldens stay byte-identical).
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(8, "Scorpion", MinToHit: 0, MinHp: 0, 0, "", "",
            HurtTooMuch: CriticalTables.DamBlind);
        // CombatResults left 0 → the gate short-circuits.
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Contains(host.Transcripts, t => t.StartsWith("enemy-attack"));
        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("flee:"));
    }

    [Fact]
    public void PerTurnCombatProcRunsForAScriptedEnemyThenDefaultAi()
    {
        // P35: a scripted (Sid != -1) enemy runs combat_p_proc (fp=4) at the top of its turn; with no
        // override it falls through to the default AI and still attacks.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        enemy.Sid = 5; // scripted
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Contains(host.CombatProcCalls, c => c.Critter == enemy && c.FixedParam == 4);
        Assert.Contains(host.Transcripts, t => t.StartsWith("enemy-attack")); // default AI still ran
    }

    [Fact]
    public void PerTurnCombatProcOverrideForfeitsTheTurn()
    {
        // script_overrides() in combat_p_proc cancels the default turn (combat.cc:3259) — no attack.
        var host = new FakeCombatHost { CombatProcOverride = true };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        enemy.Sid = 5;
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Contains(host.CombatProcCalls, c => c.Critter == enemy);
        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("enemy-attack")); // turn forfeited
        Assert.Equal(30, dude.CurrentHp);
    }

    [Fact]
    public void UnscriptedEnemyDoesNotRunCombatProc()
    {
        // Sid == -1 (the NewCritter default) → the proc is skipped (combat.cc:3244 sid!=-1 gate).
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Empty(host.CombatProcCalls);
    }

    [Fact]
    public void KnockedOutEnemyDoesNotRunCombatProc()
    {
        // The proc is inside the !incapacitated branch (combat.cc:3231) — a KO'd critter forfeits both.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        enemy.Sid = 5;
        enemy.CombatResults |= CriticalTables.DamKnockedOut;
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Empty(host.CombatProcCalls);
    }

    // P35-M4 want-to-join (combat_ai.cc:3165): AddJoiners runs at combat start; a candidate's maneuver /
    // damage / fp=5 decides whether it joins. A far attacker opens combat; the candidate is the subject.
    private static (CombatEngine, FakeCombatHost, MapObject Dude, MapObject Candidate) JoinSetup(
        int candidateTile, Action<MapObject> setup)
    {
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject attacker = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        attacker.Team = 1;
        MapObject candidate = host.AddCritter(NewCritter(tile: candidateTile, hp: 30, ap: 10));
        candidate.Team = 1; // same team as the attacker (so the ShouldJoin fallback would consider it)
        setup(candidate);
        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(attacker, dude); // adds the attacker + runs AddJoiners over the candidate
        return (engine, host, dude, candidate);
    }

    private static bool Joined(FakeCombatHost host, MapObject c) =>
        host.Transcripts.Any(t => t.StartsWith("joins:") && t.Contains($"@{c.HexTile} "));

    [Fact]
    public void EngagingManeuverCritterJoinsEvenWhenFar()
    {
        // ENGAGING (0x01) short-circuits before the distance heuristic — a far critter still joins.
        // (20050 = x50,y100: a far INTERIOR tile — an edge tile would break the step-walked Distance.)
        var (_, host, _, candidate) = JoinSetup(candidateTile: 20050, c => c.Maneuver = 0x01);
        Assert.True(HexGrid.Distance(candidate.HexTile, 20100) > CombatRules.SightRangeHexes); // ShouldJoin would reject
        Assert.True(Joined(host, candidate));
    }

    [Fact]
    public void FleeingManeuverCritterDoesNotJoinEvenWhenNearby()
    {
        // A near, same-team critter ShouldJoin would accept, but FLEEING (0x04) blocks the join.
        var (_, host, _, candidate) = JoinSetup(HexGrid.TileInDirection(20100, 2), c => c.Maneuver = 0x04);
        Assert.True(HexGrid.Distance(candidate.HexTile, 20100) <= CombatRules.SightRangeHexes);
        Assert.False(Joined(host, candidate));
    }

    [Fact]
    public void DamagedCandidateJoinsRegardlessOfDistance()
    {
        // damageLastTurn > 0 → joins before any maneuver/heuristic check (combat_ai.cc:3183).
        var (_, host, _, candidate) = JoinSetup(candidateTile: 20050, c => c.DamageLastTurn = 4);
        Assert.True(Joined(host, candidate));
    }

    [Fact]
    public void Fp5HookRunsForAScriptedJoinCandidate()
    {
        // A scripted (Sid != -1) candidate gets its combat_p_proc run with fixedParam=5.
        var (_, host, _, candidate) = JoinSetup(HexGrid.TileInDirection(20100, 2), c => c.Sid = 7);
        Assert.Contains(host.CombatProcCalls, p => p.Critter == candidate && p.FixedParam == 5);
    }

    [Fact]
    public void TerminateCombatFromACombatProcEndsTheFight()
    {
        // P35-M5: a combat_p_proc that calls terminate_combat (→ engine.RequestTerminateCombat) ends the
        // fight at the next turn boundary (combat.cc _game_user_wants_to_quit).
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        enemy.Sid = 5; // scripted → its per-turn fp=4 runs
        host.AiPackets[enemy] = new AiPacket(12, "Guard", MinToHit: 0, MinHp: 0, 0, "", "");
        // ACTemVil's fp=4 calls terminate_combat AND script_overrides (it yields), so the enemy forfeits
        // its turn — no pending attack to stall the turn loop before the terminate check.
        host.CombatProcOverride = true;
        var engine = new CombatEngine(host, new MinRng());
        host.OnCombatProc = (c, fp) => { if (fp == 4 && c == enemy) engine.RequestTerminateCombat(); };

        engine.BeginScriptAggro(enemy, dude); // opens combat; the enemy's turn runs fp=4 → terminate
        for (int i = 0; i < 4 && engine.Phase != CombatPhase.Idle; i++)
            engine.Step();

        Assert.Equal(CombatPhase.Idle, engine.Phase); // combat ended
        Assert.Equal(30, dude.CurrentHp);             // the enemy never landed an attack
    }

    [Fact]
    public void DudeKnockedOutRunsTheMapCombatOverHookWithTheKoerTeam()
    {
        // P100 (Point 3): when the dude is knocked out (not killed), the engine runs the MAP script's
        // combat_p_proc "combat over" hook with fixedParam = the KO'er's team (fo2ce _scr_end_combat).
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject boxer = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        boxer.Team = 3;
        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(boxer, dude); // open combat

        engine.KnockOut(dude, boxer); // the boxer knocks the dude out

        Assert.Contains(3, host.MapCombatOverTeams); // the hook ran with the boxer's team
    }

    [Fact]
    public void CaughtKnockoutEndsCombatInsteadOfLeavingTheDudeDown()
    {
        // The ring "catches" the KO: the map-script combat_p_proc script_overrides → combat ends at the
        // turn boundary (no game-over), the faithful prizefight-defeat path.
        var host = new FakeCombatHost { MapCombatOverReturns = true };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject boxer = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        boxer.Team = 3;
        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(boxer, dude);

        engine.KnockOut(dude, boxer);      // caught KO → RequestTerminateCombat
        for (int i = 0; i < 4 && engine.Phase != CombatPhase.Idle; i++)
            engine.Step();

        Assert.Equal(CombatPhase.Idle, engine.Phase); // the bout ended, dude alive
        Assert.False(dude.IsDead);
    }

    [Fact]
    public void DudeKnockoutWithoutAnOverridingMapScriptDoesNotEndCombat()
    {
        // The default (no map-script combat_p_proc / no override): a dude KO does NOT end combat — the
        // hook is inert, so ordinary fights are unaffected (byte-identical to pre-P100 behaviour).
        var host = new FakeCombatHost { MapCombatOverReturns = false };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(enemy, dude);

        engine.KnockOut(dude, enemy);

        Assert.NotEqual(CombatPhase.Idle, engine.Phase); // combat did NOT terminate from the KO
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
        enemy.Team = 1; // Task 3: cross-team, like real game data — SetWhoHitMe/RegisterHit only stamp whoHitMe across teams
        var engine = new CombatEngine(host, new MinRng());

        // Aimed RIGHT_LEG: MAN/RIGHT_LEG/sev0 = { 3, DAM_KNOCKED_DOWN } → prone.
        Assert.True(engine.TryAttack(enemy, hitLocation: 4));
        host.Animating.Clear();
        engine.ProcessAnimations();
        Assert.Contains(host.Transcripts, t => t.StartsWith("knockdown:"));

        // +40 vs a prone target, −10 for the enemy's remaining-AP dodge (P77: it hasn't acted, so its
        // full maxAp 10 boosts its AC): an uncalled follow-up reads chance 80 (50 + 40 − 10).
        host.Transcripts.Clear();
        Assert.True(engine.TryAttack(enemy));
        Assert.Contains(host.Transcripts, t => t.Contains("chance=80%"));

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

    // ---- P77 remaining-AP dodge (stat.cc:215-242) -----------------------

    [Fact]
    public void NotYetActedEnemyDodgesAtItsFullMaxApAndTheDudeIsZeroOnHisTurn()
    {
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 30, ap: 7));
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(enemy));                  // opens combat — the dude's turn
        Assert.Equal(7, engine.RemainingApDodge(enemy));       // the enemy hasn't acted → full maxAp dodge
        Assert.Equal(0, engine.RemainingApDodge(host.Dude!));  // it IS the dude's turn → no dodge for him
    }

    // SetDudeAp pins a known leftover so the off-turn dodge is exact (independent of the swing's AP cost).
    private static CombatEngine OffTurnDudeWithApFive(FakeCombatHost host, out MapObject dude)
    {
        dude = host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 72)); // unarmed; Unarmed/12 = 6
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 30, ap: 7));
        var engine = new CombatEngine(host, new MinRng());
        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();
        engine.SetDudeAp(5);          // pin the leftover
        engine.EndPlayerTurn();       // captures _currentAp[dude] = 5, off-turn (the enemy's slot)
        return engine;
    }

    [Fact]
    public void HtHEvadeDoublesTheUnarmedDudeOffTurnDodgeAndAddsUnarmedOver12()
    {
        var host = new FakeCombatHost();
        host.PerkRanks[Perks.PerkId.HthEvade] = 1;
        CombatEngine engine = OffTurnDudeWithApFive(host, out MapObject dude);
        int unarmed = host.GetCritterState(dude)!.UnarmedSkill;
        Assert.Equal(5 * 2 + unarmed / 12, engine.RemainingApDodge(dude)); // leftover ×2 + Unarmed/12
    }

    [Fact]
    public void WithoutHtHEvadeTheDudeOffTurnDodgeIsHisRawLeftoverAp()
    {
        var host = new FakeCombatHost(); // no perk
        CombatEngine engine = OffTurnDudeWithApFive(host, out MapObject dude);
        Assert.Equal(5, engine.RemainingApDodge(dude)); // ×1, no Unarmed/12 bonus
    }

    [Fact]
    public void HtHEvadeIsInertWhenTheDudeIsArmed()
    {
        var host = new FakeCombatHost();
        host.PerkRanks[Perks.PerkId.HthEvade] = 1;
        CombatEngine engine = OffTurnDudeWithApFive(host, out MapObject dude);
        host.Equipped = (new Proto.ProtoInfo(1, 0, 0, 0, 0, -1), null); // now wielding a weapon
        Assert.Equal(5, engine.RemainingApDodge(dude)); // the unarmed gate fails → ×1, no bonus
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

    [Fact]
    public void ExplosionDamagesNonCentreVictimsInSpiralOrderNotDistanceOrder()
    {
        // ported from fallout2-ce src/combat.cc _compute_explosion_on_extras (:4022): victims are
        // collected ring-by-ring in rotation order, NOT nearest-first. Both victims here sit at
        // distance 1, so a distance sort keeps list order (west, then north-east) while the spiral
        // opens at the NE neighbour — so the order flips, and with it which victim draws first.
        const int center = 20100;
        const int NE = 0, W = 4;
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(tile: 20900, hp: 30, ap: 10)); // far away, not a victim

        int westTile = HexGrid.TileInDirection(center, W);
        int northEastTile = HexGrid.TileInDirection(center, NE);
        host.AddCritter(NewCritter(tile: westTile, hp: 100));
        host.AddCritter(NewCritter(tile: northEastTile, hp: 100));

        var engine = new CombatEngine(host, new MinRng());
        engine.Explode(center, killer: null, minDamage: 10, maxDamage: 10, radius: 2);

        // The transcript records victims in the order they were damaged, at the tile they occupied AT
        // THAT MOMENT — captured above, since a nonzero-damage hit also knocks the victim back
        // (Explode -> Shove), which would otherwise mutate MapObject.HexTile out from under a
        // post-Explode read.
        var hitOrder = host.Transcripts
            .Where(t => t.StartsWith("explosion-hit:"))
            .ToList();
        Assert.Equal(2, hitOrder.Count);
        Assert.Contains($"@{northEastTile}", hitOrder[0]); // spiral opens NE
        Assert.Contains($"@{westTile}", hitOrder[1]);
    }

    [Fact]
    public void ACritterOnTheBlastTileIsDamagedBeforeAnySpiralVictim()
    {
        // DOCUMENTED DIVERGENCE (combat.cc:4033): the reference never enumerates the blast tile — its
        // occupant is the primary defender, damaged by the main attack path. Hexwaste's Explode has no
        // separate primary path, so the centre critter is damaged FIRST and the spiral orders the rest.
        // Without this, a strict spiral port would leave a critter standing on the blast tile unharmed.
        //
        // DEVIATION FROM BRIEF: the brief's 2-victim version (centre + one neighbour) can never fail
        // under the OLD distance sort either — the centre tile is always distance 0, the unbeatable
        // minimum, so OrderBy(Distance) already puts it first regardless of insertion order or spiral
        // logic. Confirmed empirically: that 2-victim setup PASSED against the pre-change code (see
        // task-2-report.md). A third distance-1 victim is added here (west, same trick as the sibling
        // spiral-order test) so the assertion also pins the spiral tie-break among the non-centre
        // victims — which DOES fail pre-change (distance sort's stable tie keeps insertion order:
        // west before north-east).
        const int center = 20100;
        const int NE = 0, W = 4;
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(tile: 20900, hp: 30, ap: 10)); // far away, not a victim

        int westTile = HexGrid.TileInDirection(center, W);
        int neighbourTile = HexGrid.TileInDirection(center, NE);
        host.AddCritter(NewCritter(tile: westTile, hp: 100));
        host.AddCritter(NewCritter(tile: neighbourTile, hp: 100));
        host.AddCritter(NewCritter(tile: center, hp: 100));

        var engine = new CombatEngine(host, new MinRng());
        engine.Explode(center, killer: null, minDamage: 10, maxDamage: 10, radius: 2);

        // Tiles captured above (not read live off MapObject.HexTile after Explode), since a nonzero
        // hit also knocks the victim back (Explode -> Shove), which would otherwise mutate HexTile.
        var hitOrder = host.Transcripts.Where(t => t.StartsWith("explosion-hit:")).ToList();
        Assert.Equal(3, hitOrder.Count);
        Assert.Contains($"@{center}", hitOrder[0]); // centre victim first...
        Assert.Contains($"@{neighbourTile}", hitOrder[1]); // ...then the spiral opens NE...
        Assert.Contains($"@{westTile}", hitOrder[2]); // ...then west
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
    public void EnemyWithABurstWeaponAndAlwaysModeFiresABurst()
    {
        // P76-M1: an enemy whose weapon has a burst secondary + area_attack_mode=always sprays.
        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, skill: 80));
        host.Equipped = MakeBurstWeapon(rounds: 6, apCost2: 6);
        host.AiPackets[enemy] = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 0, 0, "", "", AreaAttackMode: "always");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.Contains(host.Transcripts, t => t.StartsWith("enemy-burst"));
    }

    [Fact]
    public void EnemyWithoutABurstWeaponShootsSingleEvenWithAnAreaMode()
    {
        // Control: IsBurstWeapon false (a single-mode gun) → the short-circuit skips the burst path → single
        // shot, no decision rng draw (the source of the slice goldens' byte-identical-ness).
        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, skill: 80));
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100)); // ext 0x06 = single only
        host.AiPackets[enemy] = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 0, 0, "", "", AreaAttackMode: "always");
        var engine = new CombatEngine(host, new MinRng());

        engine.BeginScriptAggro(enemy, dude);
        engine.Step();

        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("enemy-burst"));
        Assert.Contains(host.Transcripts, t => t.StartsWith("enemy-attack"));
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
    public void CannotEndCombatWhileAnAdjacentHostileStillFights()
    {
        // Bugfix: the ENDCOMBAT button was ungated (a hard Reset). fo2ce combatAttemptEnd (combat.cc:3075)
        // refuses to leave while a live enemy still wants to fight (_combatai_want_to_stop).
        var host = new FakeCombatHost { CriticalsEnabled = false };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500, perception: 5));
        // A REAL team split (NewCritter defaults everyone to team 0). WantsToStopFighting now derives its
        // danger source from DangerSource (_combatai_want_to_stop's :3227 `_ai_danger_source(a1)`) rather
        // than hardcoding "the dude + party", and every reference path into a danger source — the
        // whoHitMe stamp via _critter_set_who_hit_me's cross-team gate (critter.cc:1296) and
        // aiFindAttackers' own cross-team filter — is cross-team. A same-team "enemy" has no danger
        // source in vanilla either, so leaving both on team 0 would have been testing an impossible fight.
        enemy.Team = 1;
        var engine = new CombatEngine(host, new MinRng());
        Assert.True(engine.TryAttack(enemy)); // opens combat; enemy is a live, adjacent, engaging hostile
        host.Animating.Clear();
        engine.ProcessAnimations();
        Assert.NotEqual(CombatPhase.Idle, engine.Phase);

        // The adjacent angry animal blocks the exit.
        Assert.False(engine.TryEndCombat());
        Assert.NotEqual(CombatPhase.Idle, engine.Phase);

        // A dead (or KO'd/fled) hostile "wants to stop" → the fight can now be left.
        enemy.CombatResults |= CriticalTables.DamDead;
        Assert.True(engine.TryEndCombat());
        Assert.Equal(CombatPhase.Idle, engine.Phase);
    }

    [Fact]
    public void MissedSingleShotHitsABystanderInTheOvershootLine()
    {
        // P114: a MISSED gun shot overshoots into the first critter beyond the target (combat.cc:3937).
        int from = 20100;
        int target = HexGrid.TileInDirection(from, 0, 3);

        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(from, hp: 30, ap: 12, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(target, hp: 500));
        (ProtoInfo proto, MapObject item) = MakeBurstWeapon(rounds: 10, apCost2: 6, minDmg: 10, maxDmg: 10, maxRange: 40);
        host.Equipped = (proto, item);

        // Stand a bystander on the from->target overshoot line, beyond the target.
        int endpoint = HexGrid.TileNumBeyond(from, target, 40);
        var line = new List<int>();
        LineOfFire.Trace(target, endpoint, t => { line.Add(t); return null; });
        Assert.True(line.Count > 1, "there must be an overshoot tile beyond the target");
        MapObject bystander = host.AddCritter(NewCritter(line[1], hp: 500));

        host.BlockerOverride = tile => host.CombatCritters.FirstOrDefault(c => c.HexTile == tile && !c.IsDead);

        // SequenceRng(100…) → the to-hit d100 is 100 > any clamped chance → MISS; damage rolls are fixed (10).
        var engine = new CombatEngine(host, new SequenceRng(100));
        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.Equal(500, enemy.CurrentHp);           // the primary target was missed
        Assert.True(bystander.CurrentHp < 500, "the overshoot should have struck the bystander");
    }

    [Fact]
    public void MissedSingleShotWithClearOvershootHitsNobody()
    {
        // The 1-on-1 invariant the ranged goldens rely on: an empty overshoot line → no accidental hit,
        // no extra RNG drawn. (Same miss setup, but no bystander behind the target.)
        int from = 20100;
        int target = HexGrid.TileInDirection(from, 0, 3);
        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(from, hp: 30, ap: 12, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(target, hp: 500));
        host.Equipped = MakeBurstWeapon(rounds: 10, apCost2: 6, minDmg: 10, maxDmg: 10, maxRange: 40);
        host.BlockerOverride = tile => host.CombatCritters.FirstOrDefault(c => c.HexTile == tile && !c.IsDead);

        var engine = new CombatEngine(host, new SequenceRng(100));
        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.Equal(500, enemy.CurrentHp); // missed, nobody else on the line → no accidental hit
    }

    [Fact]
    public void MissedShotsCollateralVictimRunsNoDamageProc()
    {
        // F12, ported from fallout2-ce src/combat.cc _damage_object() (:4821): _check_ranged_miss
        // reassigns attack->defender to the bystander it struck, while attack->oops keeps the INTENDED
        // target (set at :3485). The defender's damage call at :4723 therefore passes
        // `attack->defender != attack->oops` = TRUE, and _damage_object gates the proc as `if (!a4)`
        // (:4847) — so a collateral victim runs NO damage_p_proc. It still takes the HP loss and still
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

    [Fact]
    public void OrdinaryDefenderStillRunsItsDamageProc()
    {
        // F12 boundary pin: the damage-proc suppression is specific to ApplyAccidentalHit's collateral
        // victim (defender != oops). It must not leak to the ordinary defender path — a landed shot on
        // the INTENDED target (defender == oops) still runs SCRIPT_PROC_DAMAGE (combat.cc:4723-4850-4851).
        // This is a boundary pin, not a regression test: it is expected to pass both before and after F12.
        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(20100, hp: 30, ap: 12, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        enemy.Sid = 7; // scripted: a damage_p_proc can run
        host.Equipped = MakeGun();
        var engine = new CombatEngine(host, new MinRng()); // guaranteed to-hit success

        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.True(enemy.CurrentHp < 100, "the shot must land for the proc to matter");
        Assert.Contains(host.DamageProcCalls, c => c.Target == enemy);
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
    public void BurstExtraOnAPartyMemberByAPartyMemberAttackerRunsNoDamageProc()
    {
        // F27 (Task 2, B — this is the bug being fixed): ported from fallout2-ce src/combat.cc
        // _apply_damage() (:4849, `if (!objectIsPartyMember(a1) || !objectIsPartyMember(a5))`) — the
        // proc is skipped whenever BOTH the damaged object and its source are party members, and the
        // dude counts as a party member (gPartyMembers[0], party_member.cc:725). ApplyBurstExtras's old
        // gate (`ex.Victim != dude`) only ever excluded the dude himself — it never looked at whether
        // the ATTACKER was a party member, so a dude-fired burst catching a companion in its cone ran
        // that companion's damage_p_proc, which the reference suppresses (dude+companion are both
        // party members). MUST FAIL before the shared pair-gate helper lands.
        int from = 20100;
        int target = HexGrid.TileInDirection(from, 0, 3);

        var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(from, hp: 30, ap: 12, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(target, hp: 500));
        (ProtoInfo proto, MapObject item) = MakeBurstWeapon(rounds: 10, apCost2: 6, minDmg: 10, maxDmg: 10);
        host.Equipped = (proto, item);

        // Same cone geometry as BurstConeCatchesACollateralBystanderOnALine — a companion standing on
        // the left cone line takes the collateral hit.
        int pivot = HexGrid.Distance(from, target) <= 3 ? HexGrid.TileNumBeyond(from, target, 3) : target;
        int rotation = HexGrid.RotationTo(pivot, from);
        int leftTile = HexGrid.TileInDirection(pivot, (rotation + 1) % 6, 1);
        int leftEnd = HexGrid.TileNumBeyond(from, leftTile, 40);
        var leftLine = new List<int>();
        LineOfFire.Trace(from, leftEnd, t => { leftLine.Add(t); return null; });
        Assert.NotEmpty(leftLine);
        MapObject companion = host.AddAlly(NewCritter(leftLine[Math.Min(2, leftLine.Count - 1)], hp: 500), CompanionAi.Default);
        companion.Sid = 11; // scripted: a damage_p_proc COULD run — the point is that it must not

        host.BlockerOverride = tile => host.CombatCritters.Concat(host.Allies)
            .FirstOrDefault(c => c.HexTile == tile && !c.IsDead);

        var engine = new CombatEngine(host, new MinRng()); // every round connects
        Assert.True(engine.TryBurst(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();

        Assert.True(companion.CurrentHp < 500, "the cone's left line should still have caught the companion");
        Assert.DoesNotContain(host.DamageProcCalls, c => c.Target == companion);
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
    public void DudeBurstCritFailureAppliesEffectsAndLosesTheTurn()
    {
        // F26 (Test A): the burst's own inception roll (RollBurst, ported from combat.cc:3703-3720
        // _compute_spray — the ALREADY-EXISTING detection, unchanged here) aborts on a
        // CRITICAL_FAILURE. This pins that the abort now reaches the shared crit-fail effects
        // dispatch every attack shape reaches (combat.cc:3933-3934 case ROLL_CRITICAL_FAILURE ->
        // attackComputeCriticalFailure), instead of silently discarding the fumble.
        // SequenceRng: skill 0 keeps accuracy at/near the floor so the inception d100=100 lands well
        // below it — delta = accuracy-100 is a large negative, so -delta/10 is large too, and the
        // trigger roll=1 always lands the fumble (a high-accuracy shooter would make -delta/10 too
        // small for a fixed roll=1 to reliably clear — this is why skill is pinned low here rather
        // than left at a "normal" value). severity=30 (with Luck 0 the Luck-shifted chance is
        // 30+25=55 -> CriticalFailure.Severity bucket 2; row 0 (unarmed/default weapon
        // criticalFailureType) cols 1 AND 2 are both 32768 = DamLoseTurn only, matching the existing
        // single-shot CriticalFailureFiresOnAMissAndHonorsTheDudeDay6Gate recipe).
        var host = new FakeCombatHost { CriticalsEnabled = true, DudeCritFailuresEnabled = true, LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 0));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
        host.Equipped = MakeBurstWeapon(rounds: 10, apCost2: 6, minDmg: 10, maxDmg: 10);
        var engine = new CombatEngine(host, new SequenceRng(100, 1, 30));

        Assert.True(engine.TryBurst(enemy));

        Assert.Contains(host.Transcripts, t => t.StartsWith("crit-fail: ") && t.Contains("flags=0x8000"));
        Assert.Contains(host.Transcripts, t => t.StartsWith("burst ") && t.Contains("hit=0 damage=0"));
        Assert.Equal(500, enemy.CurrentHp);   // the burst aborted — nothing connected
        Assert.Equal(0, engine.DudeAp);       // DamLoseTurn zeroes the pool that matters for the dude (:369-370)
    }

    [Fact]
    public void BurstHitSelfFumbleRollsDamageOncePerRoundSpent()
    {
        // F15 (combat.cc:4229 ternary + :4589 loop): a burst that fumbles into DAM_HIT_SELF rolls
        // weapon damage once per round SPENT (combat.cc:3713 assigns *roundsSpentPtr = ammoQuantity
        // BEFORE the inception roll, so the count holds even though the aborted burst connects with
        // nothing — "rounds spent", not "rounds hit"). PRIMARY assertion is the DRAW COUNT, not the
        // damage total: a total-only assertion would still pass if the roll count silently became 1
        // and RollWeaponDamage's fixed 10-per-round happened to match some other total — this is
        // exactly the shape of bug that hid F11 for months.
        // Weapon: burst, 9 rounds, CriticalFailureType 1 (row 1 in _cf_table). skill 0 keeps accuracy
        // at the floor so the d100=100 inception roll is a guaranteed miss (mirrors
        // DudeBurstCritFailureAppliesEffectsAndLosesTheTurn's recipe). SequenceRng(100, 1, 80): 100 =
        // inception d100 (miss), 1 <= -delta/10 (CRITICAL_FAILURE), severity raw 80 -> with Luck 0 the
        // chance is 80+25=105 -> CriticalFailure.Severity bucket 4 -> _cf_table row 1 col 4 = 65536 =
        // DAM_HIT_SELF exactly (same severity recipe as HitSelfFumbleStillRollsWeaponDamage). minDmg =
        // maxDmg = 10 makes the per-round damage draw deterministic regardless of its returned value.
        var w = new WeaponProtoStats(0, 10, 10, 0, 40, 40, 0, 0, 5, 6, 9, 0, -1, 30, 0, CriticalFailureType: 1);
        var proto = new ProtoInfo(0x09, 0, 0x06000000, 0, 0x76, 3, Weapon: w);
        var item = new MapObject
        {
            Id = 9, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0,
            Fid = 0x06000000, Flags = 0, Pid = 0x09, Sid = -1, AmmoQuantity = 10,
        };
        var host = new FakeCombatHost { CriticalsEnabled = true, DudeCritFailuresEnabled = true, LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(20100, hp: 200, ap: 12, skill: 0));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
        host.Equipped = (proto, item);
        var rng = new RecordingRng(new SequenceRng(100, 1, 80));
        var engine = new CombatEngine(host, rng);

        Assert.True(engine.TryBurst(enemy));

        Assert.Contains(host.Transcripts, t => t.StartsWith("crit-fail: ") && t.Contains("flags=0x10000"));
        Assert.Contains(host.Transcripts, t => t.StartsWith("crit-fail-self:"));
        // n = min(loadedAmmo 10, weapon.Rounds 9) = 9 rounds spent -> 9 weapon-damage draws (10, 11),
        // not the 1 a single-shot/pre-F15 path would take.
        Assert.Equal(9, rng.Draws.Count(d => d == (10, 11)));
        Assert.Equal(500, enemy.CurrentHp);  // the fumble hits the ATTACKER, not the intended target
        Assert.Equal(110, dude.CurrentHp);   // 200 − 9×10, the full per-round-spent damage
    }

    [Fact]
    public void SingleShotHitSelfFumbleStillRollsExactlyOnce()
    {
        // Non-regression B: a single-shot ranged fumble takes the default roundCount=1 path unchanged.
        // Identical recipe/assertions to HitSelfFumbleStillRollsWeaponDamage, plus the explicit draw-count
        // check this task adds.
        var host = new FakeCombatHost
        {
            CriticalsEnabled = true,
            DudeCritFailuresEnabled = true,
            LoadedAmmoCount = 10,
            Equipped = MakeGun(critFailType: 1),
        };
        MapObject dude = host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        var rng = new RecordingRng(new SequenceRng(100, 1, 80));
        var engine = new CombatEngine(host, rng);

        Assert.True(engine.TryAttack(enemy));

        Assert.Equal(1, rng.Draws.Count(d => d == (5, 13))); // MakeGun's min 5 .. max 12 inclusive
        Assert.Equal(18, dude.CurrentHp); // 30 − 12, the FULL weapon-damage roll, exactly one draw
    }

    [Fact]
    public void MeleeHitSelfFumbleStillRollsExactlyOnce()
    {
        // Non-regression C: an unarmed/melee fumble also takes the default roundCount=1 path, doubly
        // inert per the reference — attackType != ATTACK_TYPE_RANGED collapses ammoQuantity to 1 at
        // combat.cc:4229 regardless of anything this task plumbs. Uses CriticalFailureAppliesTheTableEffect's
        // recipe (unarmed, row 0) but at a severity that lands DAM_HIT_SELF instead of CRIP_RANDOM: row 0
        // col 4 doesn't carry HIT_SELF in the table, so we exercise this through a melee WEAPON
        // (criticalFailureType 1, row 1) with 0 rounds/no burst — RollAttack's ordinary single-hit path.
        var host = new FakeCombatHost { CriticalsEnabled = true, DudeCritFailuresEnabled = true };
        MapObject dude = host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        var w = new WeaponProtoStats(0, 3, 8, 0, 1, 1, 0, 0, 3, 0, 0, 0, -1, 0, 0, CriticalFailureType: 1);
        var proto = new ProtoInfo(0x0A, 0, 0x06000000, 0, 0x03, 3, Weapon: w); // ext 0x03 = swing melee anim
        var item = new MapObject { Id = 10, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0x06000000, Flags = 0, Pid = 0x0A, Sid = -1, AmmoQuantity = -1 };
        host.Equipped = (proto, item);
        var rng = new RecordingRng(new SequenceRng(100, 1, 80));
        var engine = new CombatEngine(host, rng);

        Assert.True(engine.TryAttack(enemy));

        Assert.Equal(1, rng.Draws.Count(d => d == (3, 9))); // min 3 .. max 8 inclusive, exactly one draw
        Assert.Equal(22, dude.CurrentHp); // 30 − 8, the FULL weapon-damage roll
    }

    private static System.Reflection.MethodInfo TryAllyBurstMethod() => typeof(CombatEngine).GetMethod(
        "TryAllyBurst", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    private static System.Reflection.MethodInfo TryEnemyBurstMethod() => typeof(CombatEngine).GetMethod(
        "TryEnemyBurst", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    private static void SetActingAllyAp(CombatEngine engine, int ap) => typeof(CombatEngine)
        .GetField("_actingAllyAp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
        .SetValue(engine, ap);

    private static void SetActingEnemyAp2(CombatEngine engine, int ap) => typeof(CombatEngine)
        .GetField("_actingEnemyAp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
        .SetValue(engine, ap);

    [Fact]
    public void AllyBurstCritFailureAppliesEffectsAndLosesTheTurn()
    {
        // F26 (Test B, ally half): the coverage brief flags THIS as the test that catches "wired
        // one of three call sites" — driven directly via reflection (like RegisterHit/DangerSource
        // above) since routing this exact RNG sequence through the full AI turn loop would be
        // RNG-fragile (unrelated AI decisions share the same rng instance). Same recipe as the dude
        // test: inception=100, trigger=1, severity=30 -> row0 col2 = DamLoseTurn (32768).
        var host = new FakeCombatHost { CriticalsEnabled = true, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject ally = host.AddAlly(NewCritter(HexGrid.TileInDirection(20100, 2), hp: 30, ap: 10, skill: 0),
            CompanionAi.Default with { AreaAttack = AreaAttack.Always });
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
        (ProtoInfo proto, MapObject item) = MakeBurstWeapon(rounds: 10, apCost2: 6, minDmg: 10, maxDmg: 10);
        var engine = new CombatEngine(host, new SequenceRng(100, 1, 30));
        SetActingAllyAp(engine, 10);

        CritterState attacker = host.GetCritterState(ally)!;
        CritterState defender = host.GetCritterState(enemy)!;
        int distance = HexGrid.Distance(ally.HexTile, enemy.HexTile);
        object? result = TryAllyBurstMethod().Invoke(engine,
            [ally, enemy, attacker, defender, proto, item, distance, 0, AreaAttack.Always]);

        Assert.True((bool)result!);
        Assert.Contains(host.Transcripts, t => t.StartsWith("crit-fail: ") && t.Contains("flags=0x8000"));
        Assert.Contains(host.Transcripts, t => t.StartsWith("ally-burst") && t.Contains("hit=0 damage=0"));
        Assert.Equal(500, enemy.CurrentHp);
        Assert.Equal(0, (int)typeof(CombatEngine)
            .GetField("_actingAllyAp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(engine)!);
    }

    [Fact]
    public void EnemyBurstCritFailureAppliesEffectsAndLosesTheTurn()
    {
        // F26 (Test B, enemy half). Same shape as the ally test above.
        var host = new FakeCombatHost { CriticalsEnabled = true, LoadedAmmoCount = 10 };
        MapObject dude = host.SetDude(NewCritter(20100, hp: 500, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, skill: 0));
        (ProtoInfo proto, MapObject item) = MakeBurstWeapon(rounds: 10, apCost2: 6, minDmg: 10, maxDmg: 10);
        var engine = new CombatEngine(host, new SequenceRng(100, 1, 30));
        SetActingEnemyAp2(engine, 10);

        CritterState attacker = host.GetCritterState(enemy)!;
        CritterState defender = host.GetCritterState(dude)!;
        int distance = HexGrid.Distance(enemy.HexTile, dude.HexTile);
        var ai = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 0, 0, "", "", AreaAttackMode: "always");
        object? result = TryEnemyBurstMethod().Invoke(engine,
            [enemy, dude, attacker, defender, proto, item, distance, 0, ai]);

        Assert.True((bool)result!);
        Assert.Contains(host.Transcripts, t => t.StartsWith("crit-fail: ") && t.Contains("flags=0x8000"));
        Assert.Contains(host.Transcripts, t => t.StartsWith("enemy-burst") && t.Contains("hit=0 damage=0"));
        Assert.Equal(500, dude.CurrentHp);
        Assert.Equal(0, (int)typeof(CombatEngine)
            .GetField("_actingEnemyAp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(engine)!);
    }

    [Fact]
    public void BurstThatDoesNotCritFailIsUnchanged_Pin()
    {
        // F26 (Test C — a PIN, not a regression test): a burst whose inception roll does NOT land on
        // a CRITICAL_FAILURE must be entirely untouched by this change — same rounds fired, same
        // hits, same damage, and no extra RNG draw beyond what the pre-existing detection already
        // drew. SequenceRng: inception=1 (delta = accuracy-1, positive for any accuracy > 1, so the
        // ROLL_SUCCESS/ROLL_CRITICAL_SUCCESS branch runs, never the abort), crit-success check=100
        // (fails for any realistic accuracy/criticalChance, so this stays a PLAIN success — no +20),
        // then 1 repeating for every per-round hit check (1 <= accuracy always hits) and every damage
        // roll (min==max==10 makes the damage roll deterministic regardless of the draw). Same
        // weapon/ammo shape (rounds:10, apCost2:6) as the abort tests above, so n=10, centerRounds=3,
        // mainTargetExposure=3 -> 3 hits * 10 damage = 30, exactly matching the pre-existing
        // CriticalsEnabled:false burst-hit fixtures' numbers (e.g. BurstFiresAtMostTheLoadedAmmoOrWeaponRounds's
        // sibling tests), which is the "unchanged" being pinned.
        var host = new FakeCombatHost { CriticalsEnabled = true, LoadedAmmoCount = 10 };
        host.SetDude(NewCritter(20100, hp: 30, ap: 12, skill: 80));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 500));
        host.Equipped = MakeBurstWeapon(rounds: 10, apCost2: 6, minDmg: 10, maxDmg: 10);
        var rng = new RecordingRng(new SequenceRng(1, 100, 1));
        var engine = new CombatEngine(host, rng);

        Assert.True(engine.TryBurst(enemy));

        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("crit-fail:"));
        Assert.Contains(host.Transcripts, t => t.StartsWith("burst ") && t.Contains("hit=3 damage=30"));
        Assert.Equal(6, engine.DudeAp);       // ordinary AP spend, no lose-turn zeroing
        // Exactly the pre-existing draw count: inception + crit-success-check + 3×(hit-check + damage-roll).
        Assert.Equal(8, rng.Draws.Count);
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
    public void DudeAbsentBrawlAutoResolvesToOneTeamWithoutTheDude()
    {
        // P73: StartBrawl(dudeSpectator: true) — the dude is NOT a combatant. The brawl opens
        // auto-running (EnemyTurn, no PlayerTurn pause), the factions fight cross-team, and it ends
        // when one team remains — all without the dude in the order or as a target.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        int aTile = HexGrid.TileInDirection(20100, 0, 8);
        MapObject teamA = host.AddCritter(NewCritter(aTile, hp: 1, ap: 10, skill: 80));
        MapObject teamB = host.AddCritter(NewCritter(HexGrid.TileInDirection(aTile, 0), hp: 1, ap: 10, skill: 80));
        teamA.Team = 1;
        teamB.Team = 2;
        var engine = new CombatEngine(host, new MinRng());

        engine.StartBrawl([teamA, teamB], dudeSpectator: true);
        Assert.Equal(CombatPhase.EnemyTurn, engine.Phase); // auto-runs — no dude slot to pause on

        for (int i = 0; i < 500 && engine.Phase != CombatPhase.Idle; i++)
        {
            host.Animating.Clear();
            engine.Step();
        }

        Assert.Equal(CombatPhase.Idle, engine.Phase);  // the brawl ended (one team left standing)
        Assert.True(teamA.IsDead ^ teamB.IsDead);       // exactly one faction survives
        Assert.Equal(30, dude.CurrentHp);               // the spectator dude never took damage / acted
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
    private static (ProtoInfo Proto, MapObject Item) MakeGun(int ap = 5, int critFailType = 0)
    {
        var w = new WeaponProtoStats(0, 5, 12, 0, 40, 0, 0, 0, ap, 0, 0, 0, -1, 12, 0, critFailType);
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
    public void CriticalFailureFiresOnAMissAndHonorsTheDudeDay6Gate()
    {
        // P41 (random.cc randomTranslateRoll + combat.cc:4178): a MISS at day ≥ 2 (CriticalsEnabled)
        // draws the natural crit-failure upgrade; on a crit-failure the dude's EFFECT is suppressed
        // until day 6 (DudeCritFailuresEnabled). SeqRng: to-hit 100 (guaranteed miss), upgrade 1
        // (≤ -delta/10 → crit-fail), severity 30 (row 0 → LOSE_TURN). At day < 6 the severity is never
        // drawn (gated before Resolve), so the punch just costs its 3 AP.
        int ApAfterMiss(bool day6)
        {
            var host = new FakeCombatHost { CriticalsEnabled = true, DudeCritFailuresEnabled = day6 };
            host.SetDude(NewCritter(20100, hp: 30, ap: 10));
            MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
            var engine = new CombatEngine(host, new SequenceRng(100, 1, 30));
            Assert.True(engine.TryAttack(enemy));
            return engine.DudeAp;
        }

        Assert.Equal(0, ApAfterMiss(day6: true));  // day ≥ 6: the fumble lands → turn lost
        Assert.Equal(7, ApAfterMiss(day6: false)); // day < 6: trigger drew but the effect is gated → 10 − 3 punch
    }

    [Fact]
    public void CriticalFailureAppliesTheTableEffect()
    {
        // A day-6 dude fumble at MAX severity → _cf_table row 0 (unarmed) col 4 = CRIP_RANDOM → one
        // crippled limb. SeqRng: to-hit 100 (miss), upgrade 1 (crit-fail), severity 100 (col 4), limb 0.
        var host = new FakeCombatHost { CriticalsEnabled = true, DudeCritFailuresEnabled = true };
        MapObject dude = host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        var engine = new CombatEngine(host, new SequenceRng(100, 1, 100, 0));
        Assert.True(engine.TryAttack(enemy));
        Assert.NotEqual(0, dude.CombatResults & CriticalTables.DamCripLimbs); // a limb is crippled
    }

    [Fact]
    public void HurtSelfFumbleRollsTheExtraOneToFiveDamage()
    {
        // community fix #675 (combat.cc:4336-4345): DAM_HURT_SELF is its OWN branch — the reference only
        // rolls weapon damage for DAM_HIT_SELF / DAM_EXPLODE, and _cf_table never pairs HURT_SELF with
        // HIT_SELF, so a HURT_SELF fumble is worth EXACTLY randomBetween(1, 5) and nothing else.
        // _cf_table row 0 (unarmed) col 3 = 524290 = HURT_SELF | KNOCKED_DOWN, so a day-6 dude fumble
        // at severity 3 takes that path. SequenceRng: to-hit 100 (miss), upgrade 1 (crit-fail),
        // severity raw 60 → chance = 60 − 5*(LUCK 0 − 5) = 85, i.e. the 76..95 bucket = col 3;
        // every later draw repeats 60 clamped into range → the 1-5 roll yields 5.
        var host = new FakeCombatHost { CriticalsEnabled = true, DudeCritFailuresEnabled = true };
        MapObject dude = host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        var rng = new RecordingRng(new SequenceRng(100, 1, 60));
        var engine = new CombatEngine(host, rng);

        Assert.True(engine.TryAttack(enemy));

        // The exact draw stream: to-hit, crit-fail upgrade, severity, then the 1-5 self-hurt roll.
        // The reference's randomBetween(1, 5) is inclusive → Next(1, 6) here. No weapon/base damage
        // draw may precede it — that was the lumped-with-HIT_SELF bug.
        Assert.Equal([(1, 101), (1, 101), (1, 101), (1, 6)], rng.Draws);
        Assert.Equal(25, dude.CurrentHp); // 30 − 5, the clamped 1-5 roll, no weapon damage on top
    }

    [Fact]
    public void InvulnerableAttackerIsExemptFromCriticalFailureEffects()
    {
        // F30 (combat.cc:4178-4184 attackComputeCriticalFailure, e97087b): the CRITTER_INVULNERABLE
        // check (obj_types.h:99, 0x400) sits BEFORE the dude's day-6 gate (:4186) and BEFORE any
        // _cf_table lookup — so an invulnerable attacker draws NO severity roll at all, not merely
        // "draws it and then discards the effect" the way the day<6 dude case does. DudeCritFailuresEnabled
        // is true here specifically so a failure to guard correctly (e.g. guarding after the day-6 check)
        // would still show through as a lost turn — the day-6 gate must not be the thing hiding this.
        // SequenceRng: to-hit 100 (miss), upgrade 1 (crit-fail), severity 30 (would be row0 → LOSE_TURN
        // if drawn). RecordingRng proves that third draw never happens: only 2 draws total.
        var host = new FakeCombatHost { CriticalsEnabled = true, DudeCritFailuresEnabled = true };
        (MapObject dudeObj, CritterProtoStats dudeProto) = NewCritter(tile: 20100, hp: 30, ap: 10);
        MapObject dude = host.SetDude((dudeObj, dudeProto with { CritterFlags = 0x400 })); // CRITTER_INVULNERABLE
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 100));
        var rng = new RecordingRng(new SequenceRng(100, 1, 30));
        var engine = new CombatEngine(host, rng);

        Assert.True(engine.TryAttack(enemy));

        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("crit-fail:"));
        Assert.Equal(0, dude.CombatResults & CriticalTables.DamCripLimbs); // no crippled limb
        Assert.Equal(7, engine.DudeAp); // 10 − 3 punch AP only; no lose-turn zeroing
        // The draw-count proof: to-hit + the crit-fail upgrade roll, and NOTHING past it — no severity
        // roll was drawn. A guard placed after CriticalFailure.Resolve would still show 3 draws here.
        Assert.Equal(2, rng.Draws.Count);
    }

    [Fact]
    public void NonInvulnerableAttackerStillFumblesNormally_Pin()
    {
        // F30 (Test B — a PIN, not a regression test): without this, "always return false from the
        // invulnerability guard" would trivially pass the primary test above. A critter with
        // CritterFlags left at 0 (not invulnerable) must still take the full day-6 crit-fail effect,
        // exactly as CriticalFailureFiresOnAMissAndHonorsTheDudeDay6Gate's day6:true case already
        // demonstrates — this is expected to pass unchanged both before and after the guard is added.
        var host = new FakeCombatHost { CriticalsEnabled = true, DudeCritFailuresEnabled = true };
        host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 100));
        var rng = new RecordingRng(new SequenceRng(100, 1, 30));
        var engine = new CombatEngine(host, rng);

        Assert.True(engine.TryAttack(enemy));

        Assert.Contains(host.Transcripts, t => t.StartsWith("crit-fail: ") && t.Contains("flags=0x8000"));
        Assert.Equal(0, engine.DudeAp); // lose-turn zeroed the remaining AP
        Assert.Equal(3, rng.Draws.Count); // to-hit + upgrade + severity — the severity roll DOES happen
    }

    [Fact]
    public void HitSelfFumbleStillRollsWeaponDamage()
    {
        // The other half of the self-damage branch (combat.cc:4228-4232 at our pinned e97087b):
        // DAM_HIT_SELF keeps the full weapon-damage roll (and takes NO 1-5 roll). _cf_table row 1
        // col 4 = 65536 = DAM_HIT_SELF exactly, so a gun whose criticalFailureType is 1 fumbling at
        // max severity self-hits. SequenceRng: to-hit 100 (miss), upgrade 1 (crit-fail), severity raw
        // 80 → chance = 80 + 25 = 105 → col 4; later draws repeat 80 clamped, so the 5-12 weapon roll
        // yields its max, 12 — and all 12 land, because attackComputeDamage(attack, n, 2) multiplies
        // by bonusDamageMultiplier 2 (combat.cc:4586) and then divides by 2 (:4601), i.e. x1: vanilla
        // self-damage is the FULL rolled figure. (F11: this asserted 30 − 6 until 2026-08-15, when
        // CritFailDamage stopped passing critMultiplier: 1 into `raw * critMultiplier / 2`.)
        var host = new FakeCombatHost
        {
            CriticalsEnabled = true,
            DudeCritFailuresEnabled = true,
            LoadedAmmoCount = 10,
            Equipped = MakeGun(critFailType: 1),
        };
        MapObject dude = host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        var rng = new RecordingRng(new SequenceRng(100, 1, 80));
        var engine = new CombatEngine(host, rng);

        Assert.True(engine.TryAttack(enemy));

        Assert.Contains(host.Transcripts, t => t.StartsWith("crit-fail-self:"));
        Assert.DoesNotContain((1, 6), rng.Draws);   // the 1-5 HURT_SELF roll is NOT part of this path
        Assert.Equal(18, dude.CurrentHp);           // 30 − 12, the FULL weapon-damage roll
    }

    [Fact]
    public void RandomHitFumbleAppliesFullWeaponDamageToTheWildVictim()
    {
        // The OTHER caller of CritFailDamage. DAM_RANDOM_HIT takes the same shape as DAM_HIT_SELF in
        // the reference — attackComputeDamage(attack, ammoQuantity, 2) at combat.cc:4260 — so its
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

    [Fact]
    public void NpcSelfDamageFumbleRunsItsOwnDamageProc()
    {
        // community fix #493 (combat.cc _apply_damage): the attacker's self-damage _damage_object call
        // passes the "hit an unintended target" flag, so in the ordinary case (defender == intendedTarget)
        // the SELF-damaged attacker runs its own damage_p_proc.
        var host = new FakeCombatHost { CriticalsEnabled = true };
        MapObject dude = host.SetDude(NewCritter(20100, hp: 100, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        enemy.Sid = 7; // a scripted NPC: damage_p_proc can run
        enemy.Team = 1; // Important-fix side effect (Task-2 review): see RoundRolloverResetsDudeApAndEnemyRetaliates.
        var rng = new RecordingRng(new SequenceRng(100, 1, 100, 1, 60));
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

        Assert.Contains(host.Transcripts, t => t.StartsWith("crit-fail-self: "));
        Assert.Contains(host.DamageProcCalls, c => c.Target == enemy && c.Source == enemy);
    }

    [Fact]
    public void ExplodeFumbleRunsTheSelfDamagedAttackersDamageProc()
    {
        // F13: PR #493's self-damage proc was wired into ApplyCritFailDamage, which only the
        // DAM_HIT_SELF branch reaches. The sibling DAM_EXPLODE branch routes to Explode() and reached
        // no proc at all — where the reference's attackComputeDamage(attack, 1, 2) self-damage feeds
        // the same _apply_damage path (combat.cc:4231-4232). _cf_table row 4 col 4 = 4096 = DAM_EXPLODE
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
        enemy.Team = 1; // Important-fix side effect (Task-2 review): see RoundRolloverResetsDudeApAndEnemyRetaliates.
        // Derived empirically with RecordingRng (rng.Draws printed against the armed NPC's AI/attack
        // path) — the sibling test's sequence is for an unarmed NPC and does not apply here. Five draws
        // land the fumble; a 6th (clamped to the 5th's value) is Explode()'s own blast-damage roll:
        //   100 -> the DUDE's own to-hit roll (opens combat with a guaranteed miss)
        //   1   -> the DUDE's TriggerCritFailure natural-upgrade roll: drawn (CriticalsEnabled), but
        //          DudeCritFailuresEnabled defaults false so the dude's fumble has no further effect —
        //          this draw is consumed and inert, exactly like NpcSelfDamageFumble's shape
        //   100 -> the ENEMY's own to-hit roll (guaranteed miss, opens ITS crit-failure check)
        //   1   -> the ENEMY's TriggerCritFailure natural-upgrade roll (1 <= -delta/10, upgrades)
        //   80  -> CriticalFailure.Resolve's severity roll. Luck is 0 (NewCritter's default, unset),
        //          so chance = 80 − 5·(0−5) = 105 (CriticalFailure.cs:25) → Severity(105) buckets to
        //          column 4 (>95, CriticalFailure.cs:18) → _cf_table row 4 col 4 = 4096 = DAM_EXPLODE
        //          (CriticalTables.g.cs row index 20-24). This is the Luck-0 trap: Luck 0 shifts
        //          severity UP a column versus a naive "chance == raw roll" assumption — the same trap
        //          an earlier plan's SequenceRng(100,1,80) fell into.
        //   (80 again, clamped) -> Explode()'s rand(minDamage, maxDamage+1) blast roll, drawn once per
        //          victim in range (here: both the enemy itself and the dude, radius 1)
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

    [Fact]
    public void ExplodeSkipsAVictimKilledByAnEarlierVictimsDamageProc()
    {
        // ported from fallout2-ce src/combat.cc _apply_damage() (:4738): the extras loop re-checks
        // DAM_DEAD for every entry before processing it, because an earlier entry's damage_p_proc can
        // kill a later one. F13 made Explode()'s victim loop able to run a script (the self-damaged
        // attacker's damage_p_proc) for the first time; before that, `ordered` was a pure data snapshot
        // and no iteration could observe another entry's death mid-loop. Without the IsDead guard this
        // adds, a proc that kills a not-yet-processed victim would not stop Explode from still applying
        // blast damage (and, if lethal, a second KillCritter) to that already-dead victim.
        const int center = 20100;
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(Step(center, 0, 20), hp: 100)); // far away, not a victim
        MapObject fumbler = host.AddCritter(NewCritter(center, hp: 100)); // processed first: ordered puts the centre tile first
        fumbler.Sid = 5; // unaffiliated scripted critter: its damage_p_proc can run
        int otherTile = Step(center, 0, 1);
        MapObject other = host.AddCritter(NewCritter(otherTile, hp: 100)); // one hex away: processed after the centre

        // Simulate the fumbler's damage_p_proc killing `other` via some unrelated script effect —
        // exactly the hazard the reference's DAM_DEAD re-check guards against.
        host.OnDamageProc = (target, _, _) =>
        {
            if (target == fumbler)
                other.CombatResults |= CriticalTables.DamDead;
        };

        var engine = new CombatEngine(host, new MinRng()); // MinRng: damage == minDamage exactly, no DT/DR
        engine.Explode(center, killer: null, minDamage: 10, maxDamage: 10, radius: 2, selfDamageProcFor: fumbler);

        Assert.Contains(host.DamageProcCalls, c => c.Target == fumbler); // the proc did run
        // The guard must stop Explode from touching `other` once the proc has killed it: no blast
        // damage applied, no "explosion-hit" transcript, no second KillCritter/XP for an already-dead
        // victim.
        Assert.Equal(100, other.CurrentHp);
        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("explosion-hit:") && t.Contains($"@{otherTile}"));
        Assert.DoesNotContain(other, host.RecordedKills);
    }

    [Fact]
    public void ExplodeDoesNotShoveTheSelfDamagedAttacker()
    {
        // F17: ported from fallout2-ce src/combat.cc attackComputeCriticalFailure (:4180), which clears
        // DAM_HIT as its very first statement, before calling attackComputeDamage (:4513-4517): with
        // DAM_HIT cleared, attackComputeDamage takes the attacker-damage (else) branch and sets
        // knockbackDistancePtr = nullptr UNCONDITIONALLY. The reference therefore computes ZERO
        // knockback for a fumbler's own self-damage. Explode()'s per-victim tail previously called
        // Shove() for every non-multihex victim including the fumbler standing on the blast tile —
        // where HexGrid.RotationTo(centerTile, centerTile) is degenerate and can push it in an
        // arbitrary direction. Assert BOTH the tile is unchanged AND no knockback: line names it: a
        // tile-only assertion would pass even with the bug present, since a degenerate rotation can
        // resolve back to the starting tile.
        const int center = 20100;
        var host = new FakeCombatHost();
        MapObject fumbler = host.AddCritter(NewCritter(center, hp: 100));
        int start = fumbler.HexTile;

        var engine = new CombatEngine(host, new MinRng()); // MinRng: damage == minDamage exactly, no DT/DR
        engine.Explode(center, killer: null, minDamage: 50, maxDamage: 50, radius: 1, selfDamageProcFor: fumbler);

        Assert.Equal(start, fumbler.HexTile);
        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("knockback:") && t.Contains($"@{start}"));
    }

    [Fact]
    public void ExplodeStillShovesOtherBlastVictims()
    {
        // Boundary pin (F17): the self-damage suppression must be scoped to selfDamageProcFor only.
        // "Delete the Shove() call" would also make ExplodeDoesNotShoveTheSelfDamagedAttacker above
        // pass, so this confirms an ordinary blast victim (not the fumbler) is still knocked back.
        // This is expected to pass BOTH before and after the fix — it is a boundary pin, not a
        // regression test.
        const int center = 20100;
        var host = new FakeCombatHost();
        MapObject fumbler = host.AddCritter(NewCritter(center, hp: 100));
        int otherTile = Step(center, 0, 1);
        MapObject other = host.AddCritter(NewCritter(otherTile, hp: 100));
        int otherStart = other.HexTile;

        var engine = new CombatEngine(host, new MinRng());
        engine.Explode(center, killer: null, minDamage: 50, maxDamage: 50, radius: 2, selfDamageProcFor: fumbler);

        Assert.NotEqual(otherStart, other.HexTile);
        Assert.Contains(host.Transcripts, t => t.StartsWith("knockback:") && t.Contains($"@{otherStart}"));
    }

    [Fact]
    public void ExplodeRunsAnAttackSourcedVictimsDamageProc()
    {
        // F16: the sibling of F13/#493's self-damage proc. ported from fallout2-ce src/combat.cc
        // _apply_damage() extras loop (:4751, community fix #493): every OTHER critter caught in an
        // attack-sourced blast (a thrown grenade, or a crit-fail explode's other victims) also runs its
        // damage_p_proc — source = the blast's ATTACKER (attack->attacker at :4751), never the victim
        // itself (that shape is F13's self-damage branch, and getting it backwards is the easy mistake
        // here).
        //
        // At bare e97087b, the flag passed to _damage_object() at this site is `attack->defender ==
        // attack->oops` (:4751 pre-#493) — for this event `defender == oops` (Explode() never diverges a
        // victim from what it targeted), so the flag is TRUE and _damage_object's `if (!a4)` gate (:4847)
        // suppresses the proc entirely: at bare e97087b this proc would NOT run. Hexwaste has adopted
        // #493's polarity throughout (see F13's comment above), which replaces all three site-specific
        // oops/defender expressions with one `hitUnintendedTarget = attack->defender != attack->
        // intendedTarget` (:4751 post-#493) — always false here for the same reason — so under the
        // polarity Hexwaste carries, the proc DOES run. Hexwaste took the attacker-self half of this (F13)
        // and not the extras half; this is the other half.
        const int center = 20100;
        var host = new FakeCombatHost();
        MapObject attacker = host.AddCritter(NewCritter(tile: Step(center, 0, 5), hp: 100)); // the thrower/fumbler; outside radius 1, not itself a victim
        MapObject bystander = host.AddCritter(NewCritter(tile: center, hp: 100));
        bystander.Sid = 9; // unaffiliated scripted critter: its damage_p_proc can run

        var engine = new CombatEngine(host, new MinRng()); // MinRng: damage == minDamage exactly, no DT/DR
        engine.Explode(center, killer: attacker, minDamage: 10, maxDamage: 10, radius: 1, attackSourced: true);

        Assert.Contains(host.DamageProcCalls, c => c.Target == bystander && c.Source == attacker);
    }

    // ====================================================================
    //  Task 2 (F27 unification): ShouldRunDamageProc's four quadrants.
    //  ported from fallout2-ce src/combat.cc _damage_object() (:4849, `if (!objectIsPartyMember(a1) ||
    //  !objectIsPartyMember(a5))`) — skip only when BOTH the damaged object (a1) and its source (a5)
    //  are party members. gDude counts as a party member (gPartyMembers[0], party_member.cc:725).
    //  Explode() is used as the vehicle because it accepts an arbitrary killer/victim pair directly;
    //  this is the shared predicate's OWN content, not any one site's extra conditions.
    // ====================================================================

    [Fact]
    public void APartyMembersDamageProcRunsWhenTheAttackerIsNotAPartyMember()
    {
        // Quadrant 1: enemy -> party member. Not both party members -> the pair gate does not fire.
        const int center = 20100;
        var host = new FakeCombatHost();
        MapObject enemy = host.AddCritter(NewCritter(tile: Step(center, 0, 5), hp: 100)); // NOT a party member
        MapObject companion = host.AddCritter(NewCritter(tile: center, hp: 100));
        host.Allies.Add(companion); // party-member victim (Explode reads CombatCritters for its victim pool)
        companion.Sid = 21;

        var engine = new CombatEngine(host, new MinRng());
        engine.Explode(center, killer: enemy, minDamage: 10, maxDamage: 10, radius: 1, attackSourced: true);

        Assert.Contains(host.DamageProcCalls, c => c.Target == companion && c.Source == enemy);
    }

    [Fact]
    public void APartyMembersDamageProcIsSkippedWhenTheAttackerIsAlsoAPartyMember()
    {
        // Quadrant 2: party member -> party member. Both party members -> the pair gate fires.
        // (Same shape as ExplodeSkipsAPartyMemberVictimOfAPartyMemberAttacker below, which pins this
        // for the F16 site specifically; this one exercises the shared helper in isolation.)
        const int center = 20100;
        var host = new FakeCombatHost();
        MapObject attacker = host.AddCritter(NewCritter(tile: Step(center, 0, 5), hp: 100));
        host.Allies.Add(attacker); // party-member attacker
        MapObject victim = host.AddCritter(NewCritter(tile: center, hp: 100));
        host.Allies.Add(victim); // party-member victim
        victim.Sid = 22;

        var engine = new CombatEngine(host, new MinRng());
        engine.Explode(center, killer: attacker, minDamage: 10, maxDamage: 10, radius: 1, attackSourced: true);

        Assert.DoesNotContain(host.DamageProcCalls, c => c.Target == victim);
    }

    [Fact]
    public void TheDudesDamageProcRunsWhenAnEnemyAttacks()
    {
        // Quadrant 3 (Task 1's dude finding): the dude counts as gPartyMembers[0], so the pair gate
        // fires only when BOTH sides are party members — an enemy-sourced hit on the dude is NOT both,
        // so vanilla runs it (combat.cc:4849 is a pair gate only; there is no reference dude exclusion).
        // The old `!= dude` term at every site suppressed this unconditionally; this proves the shared
        // helper does not. The dude's Sid is forced to a live value here (simulating
        // scriptsSetDudeScript's real in-game sid, scripts.cc:1460-1489) purely to isolate the pair-gate
        // logic from the separate Sid==-1 precondition — SpawnDude leaves the real dude's Sid at -1
        // (Task 1's hardening), so in production this quadrant is masked by that precondition and never
        // observably fires; see BACKLOG F29.
        const int center = 20100;
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: center, hp: 100));
        dude.Sid = 5; // simulate a live dude script sid (Task 1, scripts.cc:1460-1489)
        MapObject enemy = host.AddCritter(NewCritter(tile: Step(center, 0, 5), hp: 100)); // NOT a party member

        var engine = new CombatEngine(host, new MinRng());
        engine.Explode(center, killer: enemy, minDamage: 10, maxDamage: 10, radius: 1, attackSourced: true);

        Assert.Contains(host.DamageProcCalls, c => c.Target == dude && c.Source == enemy);
    }

    [Fact]
    public void TheDudesDamageProcIsSkippedWhenAPartyMemberAttacks()
    {
        // Quadrant 4: the dude and a companion are both party members -> the pair gate fires exactly
        // like any other both-party-members pairing. Same Sid-forcing note as the quadrant-3 test above.
        const int center = 20100;
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: center, hp: 100));
        dude.Sid = 6; // simulate a live dude script sid
        MapObject companion = host.AddAlly(NewCritter(tile: Step(center, 0, 5), hp: 100), CompanionAi.Default);

        var engine = new CombatEngine(host, new MinRng());
        engine.Explode(center, killer: companion, minDamage: 10, maxDamage: 10, radius: 1, attackSourced: true);

        Assert.DoesNotContain(host.DamageProcCalls, c => c.Target == dude);
    }

    [Fact]
    public void ExplodeSkipsAPartyMemberVictimOfAPartyMemberAttacker()
    {
        // Boundary pin (F16): mirrors _damage_object's party gate exactly (combat.cc:4849,
        // `if (!objectIsPartyMember(a1) || !objectIsPartyMember(a5))`) — skip only when BOTH the victim
        // (a1) and the blast's attacker (a5) are party members. May pass both before and after the fix
        // (before: no extras proc ran for anyone; after: this specific pairing is still gated off) — this
        // pins the gate, it does not by itself prove the fix landed.
        const int center = 20100;
        var host = new FakeCombatHost();
        MapObject attacker = host.AddCritter(NewCritter(tile: Step(center, 0, 5), hp: 100));
        host.Allies.Add(attacker); // party-member attacker
        MapObject victim = host.AddCritter(NewCritter(tile: center, hp: 100));
        victim.Sid = 3;
        host.Allies.Add(victim); // party-member victim

        var engine = new CombatEngine(host, new MinRng());
        engine.Explode(center, killer: attacker, minDamage: 10, maxDamage: 10, radius: 1, attackSourced: true);

        Assert.DoesNotContain(host.DamageProcCalls, c => c.Target == victim);
    }

    [Fact]
    public void ExplodeRunsNoVictimProcsForANonAttackSourcedBlast()
    {
        // Boundary pin (F16): an environmental blast (a scripted `explosion` opcode, or a planted-charge
        // detonation) is NOT attack-sourced in Hexwaste's model — Explode() defaults attackSourced to
        // false, and every real environmental caller leaves it at that default. May pass both before and
        // after the fix (before: no extras proc existed at all; after: this call opts out) — this pins the
        // scope of the fix, it does not by itself prove the fix landed.
        const int center = 20100;
        var host = new FakeCombatHost();
        MapObject victim = host.AddCritter(NewCritter(tile: center, hp: 100));
        victim.Sid = 4;

        var engine = new CombatEngine(host, new MinRng());
        engine.Explode(center, killer: null, minDamage: 10, maxDamage: 10, radius: 1); // attackSourced defaults false

        Assert.DoesNotContain(host.DamageProcCalls, c => c.Target == victim);
    }

    [Fact]
    public void NoCriticalFailureWithoutCriticalsOrJinxed()
    {
        // The inert invariant: a non-Jinxed dude before day 2 (CriticalsEnabled false) draws NOTHING
        // extra on a miss → byte-identical. SeqRng has only the to-hit roll; any extra draw would throw.
        var host = new FakeCombatHost { CriticalsEnabled = false };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));
        var engine = new CombatEngine(host, new SequenceRng(100));
        Assert.True(engine.TryAttack(enemy));
        Assert.Equal(7, engine.DudeAp); // 10 − 3 punch, no fumble draw
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
            host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 75, perception: 0)); // Small Guns = 5 + 4*5 + 75 = 100
            return AttackChance(host);
        }
        // P74-M1: the dude's PE (NewCritter leaves it 0) clamps to the engine min 1, so the distance
        // term is −12 not −20: 100−12−60 = 28 without; 100−12 = 88 with (the −60 penalty still cancelled,
        // delta = 60).
        Assert.Equal(28, Chance(0));
        Assert.Equal(88, Chance(1));
    }

    [Fact]
    public void AccurateWeaponPerkAddsTwentyToHit()
    {
        // P74-M2 (combat.cc:4423): an Accurate-perk weapon is +20 to hit, any attacker.
        int Chance(int perk)
        {
            var w = new WeaponProtoStats(0, 5, 12, 0, 40, 0, 0, 1, 5, 0, 0, 0, -1, 12, 0, WeaponPerk: perk); // min-ST 1
            var item = new MapObject { Id = 8, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0x06000000, Flags = 0, Pid = 8, Sid = -1, AmmoQuantity = -1 };
            var host = new FakeCombatHost { CriticalsEnabled = false, LoadedAmmoCount = 10, Equipped = (new ProtoInfo(8, 0, 0x06000000, 0, 0x06, 3, Weapon: w), item) };
            host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 50, perception: 0));
            return AttackChance(host);
        }
        Assert.Equal(Chance(-1) + 20, Chance(WeaponProtoStats.PerkAccurate)); // no-perk baseline + 20
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

    // P50: drive a wounded ally's turn — a Coward disposition flees (RunAway), the default (Aggressive →
    // RunAway.Never) does not. Proves the CompanionAi.ShouldFlee wiring is connected to TryAllyAction.
    private static List<string> RunWoundedAllyTurn(CompanionAi ai)
    {
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(tile: 20100, hp: 100, ap: 10));
        MapObject ally = host.AddAlly(NewCritter(tile: HexGrid.TileInDirection(20100, 2), hp: 30, ap: 10), ai);
        ally.CurrentHp = 5; // badly wounded (5 / 30)
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        enemy.Team = 1; // Task 3: cross-team, like real game data — SetWhoHitMe/RegisterHit only stamp whoHitMe across teams
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(enemy)); // the dude opens combat (adjacent, unarmed) — the ally joins
        host.Animating.Clear();
        engine.ProcessAnimations();
        host.Transcripts.Clear();
        engine.EndPlayerTurn();
        for (int i = 0; i < 200 && engine.Phase != CombatPhase.PlayerTurn; i++)
        {
            host.Animating.Clear();
            engine.Step();
        }
        return host.Transcripts;
    }

    [Fact]
    public void WoundedCowardAllyFlees() =>
        Assert.Contains(RunWoundedAllyTurn(CompanionAi.Default with { Disposition = Disposition.Coward }),
            t => t.StartsWith("flee:"));

    [Fact]
    public void WoundedDefaultAllyDoesNotFlee() => // Aggressive → RunAway.Never (the byte-identical default)
        Assert.DoesNotContain(RunWoundedAllyTurn(CompanionAi.Default), t => t.StartsWith("flee:"));

    [Fact]
    public void AllyBurstsWhenAreaAttackAllows() // P51 area-attack: a burst gun + AreaAttack.Always → ally-burst
    {
        var host = new FakeCombatHost { LoadedAmmoCount = 20 };
        host.SetDude(NewCritter(tile: 20100, hp: 100, ap: 10));
        host.AddAlly(NewCritter(tile: HexGrid.TileInDirection(20100, 2), hp: 100, ap: 10, skill: 100),
            CompanionAi.Default with { AreaAttack = AreaAttack.Always });
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 100, ap: 10));
        enemy.Team = 1; // Task 3: cross-team, like real game data — SetWhoHitMe/RegisterHit only stamp whoHitMe across teams
        host.Equipped = MakeBurstWeapon(rounds: 10, apCost2: 4); // shared: the dude single-shots, the ally bursts
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(enemy)); // the dude opens combat (a single shot)
        host.Animating.Clear();
        engine.ProcessAnimations();
        host.Transcripts.Clear();
        engine.EndPlayerTurn();
        for (int i = 0; i < 200 && engine.Phase != CombatPhase.PlayerTurn; i++)
        {
            host.Animating.Clear();
            engine.Step();
        }

        Assert.Contains(host.Transcripts, t => t.StartsWith("ally-burst"));
    }

    [Fact]
    public void DefaultAllyNeverBursts() // AreaAttack.Never (the default) → single shots only, byte-identical
    {
        var host = new FakeCombatHost { LoadedAmmoCount = 20 };
        host.SetDude(NewCritter(tile: 20100, hp: 100, ap: 10));
        host.AddAlly(NewCritter(tile: HexGrid.TileInDirection(20100, 2), hp: 100, ap: 10, skill: 100), CompanionAi.Default);
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 100, ap: 10));
        host.Equipped = MakeBurstWeapon(rounds: 10, apCost2: 4);
        var engine = new CombatEngine(host, new MinRng());

        Assert.True(engine.TryAttack(enemy));
        host.Animating.Clear();
        engine.ProcessAnimations();
        host.Transcripts.Clear();
        engine.EndPlayerTurn();
        for (int i = 0; i < 200 && engine.Phase != CombatPhase.PlayerTurn; i++)
        {
            host.Animating.Clear();
            engine.Step();
        }

        Assert.DoesNotContain(host.Transcripts, t => t.StartsWith("ally-burst"));
    }

    [Fact]
    public void AllyDryGunSwitchesToBackupPerWeaponPref() // P51 best-weapon (the ally AiSwitchWeapon path)
    {
        var host = new FakeCombatHost { LoadedAmmoCount = 0 }; // the equipped gun is dry
        host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject ally = host.AddAlly(NewCritter(tile: 20200, hp: 30, ap: 10, skill: 100), CompanionAi.Default);
        host.AddCritter(NewCritter(tile: 20201, hp: 30, ap: 10));
        host.Equipped = (TestWeapon(0x100, 0x06, 5, 12), TestItem(0x100));      // a dry ranged gun
        host.InventoryWeapons[ally] = [(TestWeapon(0x201, 0x03, 1, 6), TestItem(0x201))]; // a carried melee club

        int chosen = new CombatEngine(host, new MinRng()).ProbeAllyWeaponSwitch(ally, (int)WeaponPref.NoPref, distance: 1);

        Assert.Equal(0x201, chosen);                                            // switched to the club (vs fists)
        Assert.Contains(host.Equips, e => e.Critter == ally && e.Item.Pid == 0x201);
    }

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
        strong.Team = 1; // distinct from the dude's default team 0 — RegisterHit's team gate must pass
        weak.Team = 1;

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(strong, dude);
        for (int i = 0; i < 20 && !ReferenceEquals(dude.WhoHitMe, weak); i++)
        {
            host.Animating.Clear(); // unblock the pending-attack resolve so each Step lands a hit
            if (engine.Phase == CombatPhase.PlayerTurn)
                engine.EndPlayerTurn(); // the dude takes no action — pass so weak's slot comes up
            engine.Step();
        }

        Assert.NotNull(dude.WhoHitMe);
        Assert.Same(strong, dude.WhoHitMe); // the weak last-hitter must NOT have stolen it
        // Finding 4: the failure mode hit on the first attempt was WEAK never actually landing a hit —
        // asserting only the incumbent left that unproven. `enemy-attack` transcript lines print the
        // ATTACKER's own tile, so a hit=True line at `weak`'s spawn tile proves it struck at least once.
        Assert.Contains(host.Transcripts,
            t => t.StartsWith($"enemy-attack Critter@{weak.HexTile}") && t.Contains("hit=True"));
    }

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
        first.Team = 1; // distinct from the dude's default team 0 — RegisterHit's team gate must pass
        second.Team = 1;

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(first, dude);
        for (int i = 0; i < 20 && !ReferenceEquals(dude.WhoHitMe, second); i++)
        {
            host.Animating.Clear(); // unblock the pending-attack resolve so each Step lands a hit
            if (engine.Phase == CombatPhase.PlayerTurn)
                engine.EndPlayerTurn(); // the dude takes no action — pass so second's slot comes up
            engine.Step();
        }

        Assert.Same(first, dude.WhoHitMe); // equal rating → keep the incumbent
        // Finding 4: prove `second` actually struck at least once (the earlier failure mode was
        // asserting only the incumbent, never that the challenger landed a hit at all).
        Assert.Contains(host.Transcripts,
            t => t.StartsWith($"enemy-attack Critter@{second.HexTile}") && t.Contains("hit=True"));
    }

    [Fact]
    public void ASameTeamHitNeverRegistersWhoHitMe()
    {
        // Finding 2 fix: the original "preservation guard" (SameTeamAndDeadTargetHitsStillNeverRegister-
        // WhoHitMe) mutation-tested as vacuous — with `RegisterHit` reduced to `if (attacker == target)
        // return;` (BOTH the team gate and the dead-target gate deleted) it still passed, because no hit
        // ever landed (single Step(), no host.Animating.Clear()) and its assertion was a tautology in a
        // two-critter host. This test genuinely exercises RegisterHit's team gate
        // (attacker.Team == target.Team, CombatEngine.cs:1658): `enemy` is left on the DEFAULT team (0),
        // same as the dude's default team, so BeginScriptAggro still opens combat and enemy still attacks
        // the dude (team is not consulted anywhere in target selection — only in RegisterHit itself).
        //
        // Task-2 history (renamed back after the Important-fix review): BuildTurnOrder ports
        // fallout2-ce src/combat.cc _combat_sequence_init's whoHitMe stamp (:3011-3017), which is NOT a
        // raw assignment in the reference — it's `_critter_set_who_hit_me` (critter.cc:1285-1301), gated
        // on team (same-team writes only on a failed INT roll, which Hexwaste simplifies to "never" —
        // see `SetWhoHitMe`). An earlier version of this port wrote WhoHitMe unconditionally at combat
        // open, which briefly made this test assert `Assert.Same(enemy, dude.WhoHitMe)` — the UNFAITHFUL
        // half of that bug, since `enemy` and `dude` are same-team here. Now that both the combat-open
        // stamp (BuildTurnOrder) and RegisterHit route through the one gated `SetWhoHitMe` helper, neither
        // writes for a same-team pair, and the original `Assert.Null` assertion is correct again — for the
        // right reason this time (a real team gate, not an absent stamp).
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 200, ap: 10));
        MapObject enemy = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, meleeDmg: 5, skill: 100));
        // enemy.Team left at its default (0) — identical to dude's default team.

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(enemy, dude);
        // The combat-open stamp already ran inside BeginScriptAggro/BuildTurnOrder, before any Step() —
        // same-team, so SetWhoHitMe refuses to write.
        Assert.Null(dude.WhoHitMe);

        // Run a FIXED number of steps (not "until a hit-dispatch line appears" — that transcript line
        // is written at DISPATCH time, one Step() before ResolveAttack/RegisterHit actually run on the
        // following Step(), so stopping on it would exit before the gate under test is ever reached).
        for (int i = 0; i < 20; i++)
        {
            host.Animating.Clear(); // unblock the pending-attack resolve so each Step lands a hit
            if (engine.Phase == CombatPhase.PlayerTurn)
                engine.EndPlayerTurn(); // the dude takes no action — pass so enemy's slot comes up
            engine.Step();
        }

        // Proves a hit actually RESOLVED (host.Logs carries the post-resolution "hits you" line, written
        // from ResolveAttack right before RegisterHit — otherwise the gate below would be untested, same
        // failure mode as before), then proves RegisterHit's own same-team gate (via SetWhoHitMe) still
        // refuses to write after that hit lands.
        Assert.Contains(host.Logs, l => l.Contains("hits you"));
        Assert.Null(dude.WhoHitMe);
    }

    [Fact]
    public void ARegisterHitCallOnAnAlreadyDeadTargetIsANoOp()
    {
        // Finding 2 fix, dead-target half: RegisterHit's `target.IsDead` guard is unreachable through
        // the public engine surface by design — TryAttack (CombatEngine.cs:279) already refuses a dead
        // target before any attack is dispatched, and every AI target-selection path (the dude+party
        // pick, the cross-team hostiles loop at :2645, FriendAttacker) skips dead critters before they're
        // ever considered — mirroring the reference, where a corpse is never offered as attack->defender
        // in the first place. So RegisterHit's own `target.IsDead` check is a defensive redundant guard
        // that real gameplay can never exercise; the only honest way to pin it is to call the private
        // method directly and confirm it is a no-op on an already-dead target.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 200, ap: 10));
        MapObject attacker = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, meleeDmg: 5, skill: 100));
        MapObject corpse = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 3), hp: 30, ap: 10, meleeDmg: 5, skill: 100));
        attacker.Team = 1; // cross-team from both dude and corpse, so only IsDead can be gating this
        corpse.CombatResults |= 0x80; // DAM_DEAD (MapObject.IsDead, MapFile.cs:129)

        var engine = new CombatEngine(host, new MinRng());
        var registerHit = typeof(CombatEngine).GetMethod("RegisterHit",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        registerHit.Invoke(engine, [corpse, attacker]);

        Assert.Null(corpse.WhoHitMe);
    }

    [Fact]
    public void AKnockedOutTargetBypassesTheRatingGate()
    {
        // ported from fallout2-ce src/combat.cc:4711-4716: a hit that leaves the defender knocked out
        // takes the JUST-LANDED attacker unconditionally (via _critter_set_who_hit_me, critter.cc:1285-
        // 1301) rather than going through _combatai_check_retaliation's rating comparison. So a lower-
        // rated attacker CAN steal whoHitMe from a higher-rated incumbent when the hit knocks the
        // target out — the opposite of the ordinary (non-KO) rule pinned by
        // AHigherRatedAttackerKeepsWhoHitMeAgainstALaterWeakerHit above. Invoked via reflection like
        // ARegisterHitCallOnAnAlreadyDeadTargetIsANoOp: the KO branch is reachable through the public
        // engine surface (ApplyCritStatus can set DamKnockedOut immediately before RegisterHit runs),
        // but driving that combination through Step()/BeginScriptAggro would be RNG-fragile, so a direct
        // call is the honest way to pin the branch in isolation.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 200, ap: 10));
        MapObject strongIncumbent = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10, meleeDmg: 9, skill: 100));
        MapObject weakAttacker = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 3), hp: 30, ap: 10, meleeDmg: 1, skill: 100));
        MapObject target = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 1), hp: 30, ap: 10));
        strongIncumbent.Team = 1;
        weakAttacker.Team = 1;
        target.Team = 0; // cross-team from both attackers — only the KO bypass is under test here

        var engine = new CombatEngine(host, new MinRng());
        var registerHit = typeof(CombatEngine).GetMethod("RegisterHit",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        target.WhoHitMe = strongIncumbent; // a higher-rated incumbent already holds whoHitMe
        target.CombatResults |= CriticalTables.DamKnockedOut; // THIS hit just knocked the target out

        registerHit.Invoke(engine, [target, weakAttacker]);

        // Without the KO branch, the rating gate (weakAttacker's rating <= strongIncumbent's) would
        // keep the incumbent — this assertion is what a deleted KO branch fails.
        Assert.Same(weakAttacker, target.WhoHitMe);
    }

    [Fact]
    public void ASameTeamKnockedOutCompanionStillBlocksWhoHitMe()
    {
        // Pins the branch ORDERING required by combat.cc:4711-4716 + critter.cc:1285-1301: the reference
        // routes a KO'd defender to `_critter_set_who_hit_me`, which carries its OWN team filter (skips
        // the stamp when attacker.Team == defender.Team, absent the unmodelled INT-roll exception — see
        // the RegisterHit doc comment above). So Hexwaste's team gate (attacker.Team == target.Team) must
        // run BEFORE the KO bypass, not after — otherwise a same-team knockout (e.g. friendly fire on a
        // companion) would wrongly stamp whoHitMe. Invoked via reflection for the same reason as
        // AKnockedOutTargetBypassesTheRatingGate: isolating the KO+same-team combination directly rather
        // than fishing for it through Step()/RNG.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 200, ap: 10));
        MapObject companion = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        MapObject friendlyAttacker = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 3), hp: 30, ap: 10, meleeDmg: 9, skill: 100));
        companion.Team = 1;
        friendlyAttacker.Team = 1; // same team as the companion — the team gate must reject this hit

        var engine = new CombatEngine(host, new MinRng());
        var registerHit = typeof(CombatEngine).GetMethod("RegisterHit",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        companion.CombatResults |= CriticalTables.DamKnockedOut; // THIS hit just knocked the companion out

        registerHit.Invoke(engine, [companion, friendlyAttacker]);

        // If the KO branch ran before the team gate, this would be stamped unconditionally instead.
        Assert.Null(companion.WhoHitMe);
    }

    // ====================================================================
    //  Task 2: DangerSource (fallout2-ce src/combat_ai.cc _ai_danger_source, :1529-1705) — the six
    //  proof obligations from the task-2 brief. Each drives the private DangerSource method directly
    //  via reflection (like RegisterHit above), seeding CombatEngine's private _hostiles/_actingEnemyAp
    //  fields where a scenario needs the roster or a bad-shot AP check, since these tests exercise
    //  DangerSource in isolation rather than through a full Step()-driven turn.
    // ====================================================================

    private static System.Reflection.MethodInfo DangerSourceMethod() => typeof(CombatEngine).GetMethod(
        "DangerSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    private static HashSet<MapObject> HostilesOf(CombatEngine engine) => (HashSet<MapObject>)typeof(CombatEngine)
        .GetField("_hostiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
        .GetValue(engine)!;

    private static void SetActingEnemyAp(CombatEngine engine, int ap) => typeof(CombatEngine)
        .GetField("_actingEnemyAp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
        .SetValue(engine, ap);

    [Fact]
    public void ADangerSourceLivingWhoHitMeShortCircuitsPerceptionAndReachability()
    {
        // ported from fallout2-ce src/combat_ai.cc _ai_danger_source (:1651-1660): a LIVING whoHitMe is
        // returned IMMEDIATELY for a non-party critter (attackWho == -1, :1648) — NO perception check,
        // NO reachability check. Only the fallback scan (targets[0..3]) is gated by either. Proven by
        // placing the avenger far outside perception range (30 hexes, beyond both the PE*5=25 wide-cone
        // sighted tier and the PE*2=10 hearing fallback) AND behind a universally-blocked path: applying
        // the perception/reachability gate here (the natural-looking mistake the task brief calls out)
        // would return null instead.
        var host = new FakeCombatHost { IsBlockedOverride = _ => true }; // every tile blocked → path always fails
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject self = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        MapObject avenger = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(20100, 0, 30), hp: 30, ap: 10)); // far outside perception (beyond PE*5=25 wide-cone AND PE*2=10 fallback)
        self.Team = 1;
        avenger.Team = 0;
        self.WhoHitMe = avenger;

        var engine = new CombatEngine(host, new MinRng());
        Assert.Same(avenger, DangerSourceMethod().Invoke(engine, [self]));
    }

    [Fact]
    public void ADangerSourceDeadWhoHitMeFallsThroughToNearestTeam()
    {
        // ported from fallout2-ce src/combat_ai.cc _ai_danger_source (:1660-1665): a DEAD whoHitMe does
        // NOT short-circuit — it falls to _ai_find_nearest_team(self, whoHitMe, sameTeam=1) for a live
        // replacement on the SAME team as the dead attacker.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject self = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        MapObject deadAttacker = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 1), hp: 30, ap: 10));
        MapObject replacement = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 2), hp: 30, ap: 10));
        self.Team = 1;
        // team 2, NOT the dude's default team 0 — otherwise FindNearestTeam(sameTeam) would match the
        // (closer) dude instead of `replacement`, since both are living team-0 critters in the roster.
        deadAttacker.Team = 2;
        replacement.Team = 2; // same team as the dead attacker → the valid replacement
        deadAttacker.CombatResults |= 0x80; // DAM_DEAD
        self.WhoHitMe = deadAttacker;

        var engine = new CombatEngine(host, new MinRng());
        HostilesOf(engine).Add(self);
        HostilesOf(engine).Add(deadAttacker);
        HostilesOf(engine).Add(replacement);
        SetActingEnemyAp(engine, 10);

        Assert.Same(replacement, DangerSourceMethod().Invoke(engine, [self]));
    }

    [Fact]
    public void ADangerSourceWithNoWhoHitMeFindsACritterAttackingMe()
    {
        // ported from fallout2-ce src/combat_ai.cc _ai_danger_source (:1666) → aiFindAttackers
        // (:1487-1493, ported Task 1): with self.whoHitMe null, the aiFindAttackers "WhoHitMe" candidate
        // (a critter whose OWN whoHitMe points back at self) is wired into targets[1] and — being the
        // only candidate — picked by the perception+reachability scan.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject self = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        MapObject attacker = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 1), hp: 30, ap: 10));
        self.Team = 1;
        attacker.Team = 0;
        attacker.WhoHitMe = self; // attacker is currently attacking ME (self); self.WhoHitMe stays null

        var engine = new CombatEngine(host, new MinRng());
        HostilesOf(engine).Add(self);
        HostilesOf(engine).Add(attacker);
        SetActingEnemyAp(engine, 10);

        Assert.Same(attacker, DangerSourceMethod().Invoke(engine, [self]));
    }

    [Fact]
    public void ADangerSourcePerceptionGatesTheFallbackOnly()
    {
        // ported from fallout2-ce src/combat_ai.cc _ai_danger_source (:1691-1703): the perception check
        // gates the targets[0..3] scan — proven here by sorting an OUT-of-perception candidate FIRST
        // (AttackWho.Strongest sorts ASCENDING by rating, the documented vanilla quirk — a low-rated
        // candidate sorts before a high-rated one) and confirming the scan moves on to the next,
        // in-perception candidate rather than stopping (or wrongly returning the unperceived one).
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject self = host.AddAlly(
            NewCritter(tile: HexGrid.TileInDirection(20100, 3, 5), hp: 30, ap: 10),
            new CompanionAi(Disposition.Custom, AttackWho.Strongest));
        MapObject teammate = host.AddAlly(
            NewCritter(tile: HexGrid.TileInDirection(self.HexTile, 1), hp: 30, ap: 10),
            new CompanionAi(Disposition.Custom, AttackWho.Closest));
        self.Team = 1;
        teammate.Team = 1;

        MapObject candidateFar = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(self.HexTile, 0, 30), hp: 30, ap: 10, meleeDmg: 1)); // lowest rating → sorts FIRST; 30 hexes (beyond PE*5=25 AND PE*2=10) → out of perception
        MapObject candidateNear = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(self.HexTile, 2), hp: 30, ap: 10, meleeDmg: 5)); // higher rating → sorts second; adjacent → perceivable + reachable
        candidateFar.Team = 0;
        candidateNear.Team = 0;
        candidateFar.WhoHitMe = self;       // fills the "WhoHitMe" slot (targets[1])
        teammate.WhoHitMe = candidateNear;  // fills the "WhoHitFriend" slot (targets[2]) as candidateNear

        var engine = new CombatEngine(host, new MinRng());
        HostilesOf(engine).Add(candidateFar);
        HostilesOf(engine).Add(candidateNear);
        SetActingEnemyAp(engine, 10);

        Assert.Same(candidateNear, DangerSourceMethod().Invoke(engine, [self]));
    }

    [Fact]
    public void ADangerSourceReachabilityIsADisjunctionWithLegalShot()
    {
        // ported from fallout2-ce src/combat_ai.cc _ai_danger_source (:1698-1699): a candidate is taken
        // when EITHER a path exists OR the shot is legal (OR, not AND). The candidate here is totally
        // UNREACHABLE (every tile reports blocked) while the shot itself is legal (adjacent, unarmed
        // melee, ample AP) — an AND-bug would refuse it and this test would see null instead.
        var host = new FakeCombatHost { IsBlockedOverride = _ => true };
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject self = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        MapObject candidate = host.AddCritter(
            NewCritter(tile: HexGrid.TileInDirection(self.HexTile, 1), hp: 30, ap: 10));
        self.Team = 1;
        candidate.Team = 0;
        candidate.WhoHitMe = self;

        var engine = new CombatEngine(host, new MinRng());
        HostilesOf(engine).Add(candidate);
        SetActingEnemyAp(engine, 10); // ample AP for an unarmed (fists) swing

        Assert.Same(candidate, DangerSourceMethod().Invoke(engine, [self]));
    }

    [Fact]
    public void ADangerSourcePartyGatingAppliesOnlyToPartyMembers()
    {
        // ported from fallout2-ce src/combat_ai.cc _ai_danger_source (:1541/:1648): the whole
        // disposition/attack_who apparatus — including the STRONGEST/WEAKEST/CLOSEST whoHitMe-clear at
        // :1642 — is gated on objectIsPartyMember(self); a non-party critter takes attackWho = -1 and
        // never reaches the switch, no matter what AI settings a host might report for it.

        // Half 1: a NON-party critter with a disposition on file (as if the party gate were missing)
        // still takes the whoHitMe early return untouched — the settings are never consulted.
        var host1 = new FakeCombatHost();
        MapObject dude1 = host1.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject self1 = host1.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        MapObject avenger1 = host1.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 1), hp: 30, ap: 10));
        self1.Team = 1;
        avenger1.Team = 0;
        self1.WhoHitMe = avenger1;
        // NOT added via AddAlly (self1 is not a party member) — only its Dispositions entry is set, to
        // prove the gate checks PartyMembers membership, not whether AI settings merely exist.
        host1.Dispositions[self1] = new CompanionAi(Disposition.Custom, AttackWho.Strongest);

        var engine1 = new CombatEngine(host1, new MinRng());
        System.Reflection.MethodInfo dangerSource = DangerSourceMethod();
        Assert.Same(avenger1, dangerSource.Invoke(engine1, [self1]));
        Assert.Same(avenger1, self1.WhoHitMe); // untouched — the STRONGEST clear never ran

        // Half 2: an ACTUAL party member with AttackWho.Strongest DOES run the switch — its whoHitMe is
        // cleared (:1642), even though it had a living one.
        var host2 = new FakeCombatHost();
        MapObject dude2 = host2.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject self2 = host2.AddAlly(
            NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10),
            new CompanionAi(Disposition.Custom, AttackWho.Strongest));
        MapObject avenger2 = host2.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 1), hp: 30, ap: 10));
        self2.Team = 1;
        avenger2.Team = 0;
        self2.WhoHitMe = avenger2;

        var engine2 = new CombatEngine(host2, new MinRng());
        dangerSource.Invoke(engine2, [self2]);

        Assert.Null(self2.WhoHitMe); // cleared — the party-gated branch ran
    }

    // ====================================================================
    //  Task 3 (correction): CombatShouldEnd folds WantsToStopFighting's query in directly instead
    //  of a mutating PruneEscapedHostiles (see the HISTORY comment above StepTurnOrder in
    //  CombatEngine.cs for what that replaced and why). The original two Task-3 tests
    //  (FledHostileWithLivingWhoHitMeIsNotPruned / HostileWithNoDangerSourceIsPruned) exercised
    //  PruneEscapedHostiles's DangerSource-based asymmetry directly via reflection; that method no
    //  longer exists and nothing replaced its exact semantics (WantsToStopFighting tests
    //  perception of the dude/party, not a DangerSource whoHitMe chain), so they were dropped
    //  rather than adapted — keeping them alive under a different meaning would be worse than
    //  losing them. In their place: a regression test for the actual bug (a freshly-joined
    //  hostile must not be evicted from _hostiles before its first turn) and one proving the
    //  automatic end-of-combat path this whole mechanism exists for still works.
    // ====================================================================

    [Fact]
    public void JoiningHostileWithNoWhoHitMeYetIsNeverEvictedFromHostiles()
    {
        // The regression net for the bug this correction fixed: AddJoiners() adds a fresh hostile
        // to _hostiles WITHOUT stamping its WhoHitMe (traced live on denbus2-fight-flee — a
        // Villager was evicted with DangerSource==null on round 1, then re-joined next round,
        // then evicted again, every round). Under the old DangerSource-mutating prune this hostile
        // would vanish on the very next Step(). Under the corrected design _hostiles is never
        // mutated by the want-to-stop check at all, so it must still be there after several Steps.
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject attacker = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0), hp: 30, ap: 10));
        attacker.Team = 1;
        // A same-team candidate within perception of the dude — WantToJoin's exact condition
        // (_hostiles.Any(h => h.Team == c.Team) && WithinPerception(c, dude)) — but with NO
        // whoHitMe of its own: nobody has fought it yet, matching a freshly recruited joiner.
        MapObject candidate = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 1), hp: 30, ap: 10));
        candidate.Team = 1;
        Assert.Null(candidate.WhoHitMe);

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(attacker, dude); // opens combat, runs AddJoiners over the candidate
        Assert.Contains(candidate, engine.Hostiles); // sanity: it did join

        for (int i = 0; i < 5; i++)
        {
            host.Animating.Clear();
            engine.Step();
            if (engine.Phase == CombatPhase.Idle)
                break; // combat resolved (dude/attacker traded blows) — fine, just not an eviction
        }

        Assert.Null(candidate.WhoHitMe); // still never acquired one...
        Assert.True(candidate.IsDead || engine.Hostiles.Contains(candidate),
            "a hostile must stay in the fight (or die in it) — never silently vanish for lack of a danger source");
    }

    [Fact]
    public void CombatAutomaticallyEndsWhenTheSoleHostileDisengages()
    {
        // The ORIGINAL purpose PruneEscapedHostiles existed for, now served by CombatShouldEnd's
        // own WantsToStopFighting check: "without this, an M1 flee that the dude doesn't chase
        // never resolves" (the old deleted doc comment). A wounded, low-HP hostile flees (TryFlee
        // sets CRITTER_MANEUVER_FLEEING/DISENGAGING); once it's the sole hostile and no longer
        // wants to fight, combat must end on its own — nobody has to manually chase it down or
        // call TryEndCombat().
        var host = new FakeCombatHost();
        MapObject dude = host.SetDude(NewCritter(tile: 20100, hp: 30, ap: 10));
        MapObject enemy = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(20100, 0, 10), hp: 5, ap: 10));
        host.AiPackets[enemy] = new AiPacket(13, "Thug", MinToHit: 0, MinHp: 10, 10, "", "");

        var engine = new CombatEngine(host, new MinRng());
        engine.BeginScriptAggro(enemy, dude);
        for (int i = 0; i < 20 && engine.Phase != CombatPhase.Idle; i++)
        {
            host.Animating.Clear();
            engine.Step();
        }

        Assert.Equal(CombatPhase.Idle, engine.Phase); // combat ended on its own
        Assert.Contains(host.Transcripts, t => t.StartsWith("disengage:") || t.StartsWith("flee:"));
    }

    private static (MapObject Obj, CritterProtoStats Proto) NewCritter(
        int tile, int hp, int ap = 10, int seq = 1, int exp = 0, int betterCrit = 0, int meleeDmg = 0, int skill = 0, int endurance = 0, int dr = 0, int killType = 0, int perception = 5)
    {
        int[] b = new int[35];
        b[CritterStat.Strength] = 5;
        b[CritterStat.Agility] = 5;
        // P113 (4.3): a real perception so the AI's isWithinPerception target/join gate sees nearby
        // targets (PE 5 → PE*2 = 10-hex hearing in combat). The combat/encounter GOLDENS use the real
        // host's proto PE, so this only affects these fake-host unit tests.
        b[CritterStat.Perception] = perception;
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

    /// <summary>Wraps another RNG and records the (min, maxExclusive) bounds of every draw —
    /// lets a test assert that a specific roll happened, independent of damage-formula internals.</summary>
    private sealed class RecordingRng(ICombatRng inner) : ICombatRng
    {
        public readonly List<(int Min, int MaxExclusive)> Draws = [];
        public int Next(int minInclusive, int maxExclusive)
        {
            Draws.Add((minInclusive, maxExclusive));
            return inner.Next(minInclusive, maxExclusive);
        }
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

        // P50: a party-member ally (NOT a hostile) + its combat-control disposition.
        public readonly List<MapObject> Allies = [];
        public readonly Dictionary<MapObject, CompanionAi> Dispositions = [];
        public MapObject AddAlly((MapObject Obj, CritterProtoStats Proto) c, CompanionAi ai)
        {
            _states[c.Obj] = new CritterState(c.Obj, c.Proto);
            Allies.Add(c.Obj);
            Dispositions[c.Obj] = ai;
            return c.Obj;
        }
        public CompanionAi CompanionSettings(MapObject ally) => Dispositions.GetValueOrDefault(ally, CompanionAi.Default);

        public readonly Dictionary<MapObject, AiPacket> AiPackets = [];
        public bool CriticalsEnabled { get; set; }
        public bool DudeCritFailuresEnabled { get; set; } // P41: the dude's day≥6 crit-failure-effect gate
        public int CombatDifficultyDamageModifier { get; set; } = 100; // P84: Easy 75 / Normal 100 / Hard 125
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
        public readonly Dictionary<MapObject, List<(ProtoInfo Proto, MapObject Item)>> InventoryWeapons = []; // P43
        public IReadOnlyList<(ProtoInfo Proto, MapObject Item)> CritterInventoryWeapons(MapObject critter) =>
            InventoryWeapons.GetValueOrDefault(critter, []);
        public readonly List<(MapObject Critter, MapObject Item)> Equips = []; // P43 EquipWeapon
        public void EquipWeapon(MapObject critter, MapObject weaponItem) => Equips.Add((critter, weaponItem));

        // Ground-pickup fallback (_ai_search_environ / _ai_retrieve_object, combat_ai.cc:2178/2237).
        // Ground is a mutable "world" list: TryRetrieveItem removes the item from it on a genuine pickup,
        // so a stale/already-claimed item naturally disappears from later GroundItemsNear scans — same
        // observable contract as the real ViewerGame.CombatHost.TryRetrieveItem hardening.
        public readonly List<(ProtoInfo Proto, MapObject Item)> Ground = [];
        public IReadOnlyList<(ProtoInfo Proto, MapObject Item)> GroundItemsNear(MapObject critter, int maxDistance) =>
            [.. Ground.Where(g => HexGrid.Distance(critter.HexTile, g.Item.HexTile) <= maxDistance)];
        public readonly List<(MapObject Critter, MapObject Item)> RetrieveAttempts = [];
        // Per-item queue of results to return in order; falls back to "adjacent ⇒ succeed" default logic
        // (removes from Ground + adds to the critter's inventory) once the queue is drained.
        public readonly Dictionary<MapObject, Queue<bool>> RetrieveResults = [];
        // Deliberately NAIVE by default (no self-protecting "still on the ground?" check) — this
        // mirrors the pre-fix TryRetrieveItem bug the review flagged (unconditional Inventory.Add
        // regardless of whether the world-list removal actually found anything), so the "two critters
        // remember the same item" test exercises CombatEngine's OWN re-verification (Issue 2's fix in
        // CombatEngine.cs), not a self-protecting test double.
        public bool TryRetrieveItem(MapObject critter, MapObject item)
        {
            RetrieveAttempts.Add((critter, item));
            bool ok = RetrieveResults.TryGetValue(item, out Queue<bool>? q) && q.Count > 0
                ? q.Dequeue()
                : true; // "adjacent" default — always reports success unless a test configures otherwise
            if (ok)
            {
                Ground.RemoveAll(g => g.Item == item);
                critter.Inventory.Add(item);
            }
            return ok;
        }
        public int LoadedAmmoCount; // settable magazine for burst/gun tests
        public int WeaponAmmo(ProtoInfo weaponProto, MapObject item) => LoadedAmmoCount;
        public AmmoProtoStats? LoadedAmmo(ProtoInfo weaponProto, MapObject item) => null;
        public bool TryReload(MapObject holder, ProtoInfo weaponProto, MapObject item) => false;
        // Important 2 (final review): the calibers CarriedAmmoCalibers reports as "in the bag" — override
        // per test so a candidate gun with WeaponAmmo <= 0 can still qualify (aiHaveAmmo's caliber match).
        public IReadOnlyList<int> CarriedCalibersOverride = [];
        public IReadOnlyList<int> CarriedAmmoCalibers(MapObject critter) => CarriedCalibersOverride;
        public Func<int, MapObject?>? BlockerOverride; // tests that need critters/walls on the line
        public MapObject? ShootBlockerAt(int tile, MapObject shooter, MapObject target) => BlockerOverride?.Invoke(tile);
        public Func<int, bool>? IsBlockedOverride; // tests that need a specific tile reported blocked (e.g. Pathfinder callers like TryFlee)
        public bool IsBlocked(int tile) => IsBlockedOverride?.Invoke(tile) ?? false;
        public bool IsAnimating(MapObject critter) => Animating.Contains(critter);
        public bool IsFallInProgress(MapObject critter) => false;
        public bool IsAnyWalkerMoving() => false;
        public bool IsWalkerMoving(MapObject critter) => false;
        public bool StartWalk(MapObject critter, int targetTile, bool run = false) { critter.HexTile = targetTile; return true; }
        public bool CritterShouldRun(MapObject critter) => true; // instant-teleport fake — anim code is moot
        public void PlaceCritter(MapObject critter, int tile) => critter.HexTile = tile;
        public void StopDude() { }
        public void ClearAnimation(MapObject critter) => Animating.Remove(critter);
        public readonly List<MapObject> AttackOrder = []; // P44: records attacker order for turn-order tests
        public void OnAttackStarted(MapObject attacker, MapObject target, ProtoInfo? weaponProto)
        {
            Animating.Add(attacker);
            AttackOrder.Add(attacker);
        }
        public void OnThrowStarted(MapObject thrower, int targetTile, ProtoInfo weaponProto) => Animating.Add(thrower);
        public void RemoveFromHand(MapObject thrower, MapObject item) { }
        public readonly List<(int Pid, int Tile)> Dropped = [];
        public void DropThrownWeapon(MapObject item, int tile) => Dropped.Add((item.Pid, tile));
        public int ExplosionMarkers;
        public void SpawnExplosionMarker(int tile) => ExplosionMarkers++;
        public List<(MapObject Target, bool KnockedDown)> Hits { get; } = [];
        public List<MapObject> Dodges { get; } = [];
        public List<MapObject> GetUps { get; } = [];
        public void OnTargetHit(MapObject target, MapObject attacker, bool knockedDown) => Hits.Add((target, knockedDown));
        public void OnTargetDodge(MapObject target) => Dodges.Add(target);
        public void OnGetUp(MapObject critter) => GetUps.Add(critter);
        // P35 combat_p_proc: record each call (per-turn fp=4 / on-hit fp=2 + its target); CombatProcOverride
        // toggles script_overrides (the per-turn turn-cancel).
        public List<(MapObject Critter, int FixedParam, MapObject? Target)> CombatProcCalls { get; } = [];
        public bool CombatProcOverride { get; set; }
        public Action<MapObject, int>? OnCombatProc { get; set; } // a test side-effect (e.g. terminate)
        public (IReadOnlyList<string> Lines, bool Overridden) RunCombatProc(MapObject critter, int fixedParam, MapObject? target = null)
        {
            CombatProcCalls.Add((critter, fixedParam, target));
            OnCombatProc?.Invoke(critter, fixedParam);
            return ([], CombatProcOverride);
        }
        // P100 (Point 3): the map-script "combat over / dude KO'd" hook. MapCombatOverReturns toggles
        // script_overrides; MapCombatOverTeams records the KO'er team the engine passed.
        public bool MapCombatOverReturns { get; set; }
        public List<int> MapCombatOverTeams { get; } = [];
        public bool RunMapCombatOver(int knockedOutByTeam)
        {
            MapCombatOverTeams.Add(knockedOutByTeam);
            return MapCombatOverReturns;
        }
        public int PickDeathAnim(MapObject critter, int desiredAnim) => 20;
        public bool StartDeathFall(MapObject critter, int deathAnim) => false; // no fall art → corpse now
        public void ConvertToCorpse(MapObject critter, int deathAnim) { }
        public void OnCritterRemoved(MapObject critter) { }
        public readonly List<(MapObject Target, MapObject? Source, int Damage)> DamageProcCalls = [];
        public Action<MapObject, MapObject?, int>? OnDamageProc { get; set; } // a test side-effect (e.g. kill another victim)
        public IReadOnlyList<string> RunDamageProc(MapObject target, MapObject? source, int damage)
        {
            DamageProcCalls.Add((target, source, damage));
            OnDamageProc?.Invoke(target, source, damage);
            return [];
        }
        public (IReadOnlyList<string> Lines, bool Overridden) RunDestroyProc(MapObject critter, MapObject? killer) => ([], false);
        public void RemovePartyMember(MapObject critter) { }
        public IReadOnlyCollection<MapObject> PartyMembers => Allies;
        public IEnumerable<MapObject> CombatCritters => _critters;
        public void AwardXp(int amount) => XpAwarded += amount;
        public readonly List<MapObject> RecordedKills = []; // P38: killsIncByType
        public void RecordKill(MapObject victim) => RecordedKills.Add(victim);
        public readonly HashSet<MapObject> CarriesStimpak = []; // P42: AI chem_use heal
        public int NpcHealAmount = 10;
        public bool TryNpcHeal(MapObject critter)
        {
            if (!CarriesStimpak.Remove(critter)) // one stimpak per heal
                return false;
            int max = GetCritterState(critter)?.MaxHp ?? critter.CurrentHp;
            critter.CurrentHp = Math.Min(critter.CurrentHp + NpcHealAmount, max);
            return true;
        }
        public readonly Dictionary<MapObject, int> CombatDrugs = []; // P78-M2: count of buff drugs carried
        public readonly List<MapObject> DrankCombatDrug = [];
        public bool TryNpcUseCombatDrug(MapObject critter, int[]? primaryDesire)
        {
            if (CombatDrugs.GetValueOrDefault(critter) <= 0)
                return false;
            CombatDrugs[critter]--;
            DrankCombatDrug.Add(critter);
            return true;
        }
        public void GameOver() => GameOverCalled = true;
        public void Log(string line) => Logs.Add(line);
        public void Transcript(string line) => Transcripts.Add(line);
        public string ObjectName(MapObject obj) => "Critter";
        public string ObjectNameByPid(int pid) => "Item";
    }
}
