using Hexwaste.Formats.Hex;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Combat;

/// <summary>The engine's blocking _combat_turn loop, flattened into a state
/// machine stepped from the host's Update (combat.cc:3121 _combat_turn_run).</summary>
public enum CombatPhase
{
    Idle,
    PlayerTurn,
    EnemyTurn,
    GameOver,
}

/// <summary>
/// The turn machine, lifted out of the MonoGame viewer into engine-free code
/// (phase-9 M0, "extract first"). It owns the combat STATE and DECISIONS and
/// reaches everything external — animation, draw lists, blocking, proto/art VFS,
/// audio, scripts, dude progression — through <see cref="ICombatHost"/>. Outcomes
/// are rolled up front (<see cref="PendingAttack"/>) and applied when the host
/// reports the animation finished, mirroring _apply_damage in
/// _combat_anim_finished (combat.cc:5363). Every method is a 1:1 port of the
/// former ViewerGame.cs combat method; see docs/phase9-research-report.md §M0.
/// </summary>
public sealed class CombatEngine
{
    private readonly ICombatHost _host;
    private readonly ICombatRng _rng;

    /// <summary>The rolled-but-not-applied attack: damage lands when the punch
    /// animation completes (engine: _apply_damage in _combat_anim_finished).</summary>
    private sealed record PendingAttack(MapObject Attacker, MapObject Target, int Chance, bool Hit, int Damage);
    private PendingAttack? _pendingAttack;

    /// <summary>Critters playing their death fall; value = death anim (20/21).</summary>
    private readonly Dictionary<MapObject, int> _fallingCritters = [];

    private CombatPhase _phase = CombatPhase.Idle;
    private readonly HashSet<MapObject> _hostiles = [];
    private readonly Queue<MapObject> _enemyQueue = new();
    private MapObject? _actingEnemy;
    private int _actingEnemyAp;
    private readonly Queue<MapObject> _allyQueue = new();
    private MapObject? _actingAlly;
    private int _actingAllyAp;
    private int _round;
    private int _dudeAp;
    private bool _gameOver;

    /// <summary>Kill XP accrued this combat, paid at combat end like the engine's
    /// _combat_exps → _combat_give_exps (combat.cc:2816).</summary>
    private int _xpPending;

    public CombatEngine(ICombatHost host, ICombatRng rng)
    {
        _host = host;
        _rng = rng;
    }

    // --- Public surface the viewer/harness drive --------------------------
    public CombatPhase Phase => _phase;
    public int Round => _round;
    public int DudeAp => _dudeAp;
    public bool IsGameOver => _gameOver;
    public bool HasPendingAttack => _pendingAttack is not null;
    /// <summary>An attack or death-fall is resolving (independent of walkers).</summary>
    public bool IsResolving => _pendingAttack is not null || _fallingCritters.Count > 0;
    /// <summary>Resolving OR an NPC walker is mid-move — the engine is "busy".</summary>
    public bool IsBusy => IsResolving || _host.IsAnyWalkerMoving();
    public IReadOnlyCollection<MapObject> Hostiles => _hostiles;

    /// <summary>Load path: seed the dude's AP outside combat (SpawnDude).</summary>
    public void SetDudeAp(int ap) => _dudeAp = ap;

    // ====================================================================
    //  Attacks
    // ====================================================================

    /// <summary>
    /// Attacks an adjacent/in-range critter. The outcome is rolled HERE, before
    /// any animation — damage waits for the swing to finish (ported from
    /// fallout2-ce src/combat.cc _combat_attack() / combatAttemptAttack()).
    /// </summary>
    public bool TryAttack(MapObject target)
    {
        MapObject? dude = _host.Dude;
        if (dude is null || _pendingAttack is not null || target == dude)
            return false;
        if (Fid.Type(target.Fid) is not ObjectType.Critter || target.IsDead)
            return false;
        if (_host.GetCritterState(dude) is not { } attacker || _host.GetCritterState(target) is not { } defender)
            return false;

        (ProtoInfo? weaponProto, MapObject? weaponItem) = _host.EquippedWeapon(dude);
        bool isGun = weaponProto?.Weapon is { } wstats && wstats.IsGun(weaponProto.ExtendedFlags);
        int range = isGun ? weaponProto!.Weapon!.MaxRange1
            : Math.Min(weaponProto?.Weapon?.MaxRange1 ?? 1, 2); // throwers melee-capped until rung (a)
        int apCost = weaponProto?.Weapon?.ApCost ?? CombatMath.PunchApCost;
        int distance = HexGrid.Distance(dude.HexTile, target.HexTile);
        if (distance > range)
        {
            _host.Log("Too far away.");
            return false;
        }

        int crittersInPath = 0;
        if (isGun)
        {
            // _combat_check_bad_shot gates: empty mag, then line of fire.
            if (_host.WeaponAmmo(weaponProto!, weaponItem!) <= 0)
            {
                if (_phase == CombatPhase.PlayerTurn
                    && _dudeAp >= RangedMath.ReloadApCost
                    && _host.TryReload(dude, weaponProto!, weaponItem!))
                {
                    _dudeAp -= RangedMath.ReloadApCost;
                    return true; // reloading is its own action
                }
                if (_phase != CombatPhase.PlayerTurn && _host.TryReload(dude, weaponProto!, weaponItem!))
                    return true;
                _host.Log("Out of ammo.");
                return false;
            }

            (MapObject? blocker, crittersInPath) = LineOfFire.Trace(
                dude.HexTile, target.HexTile, tile => _host.ShootBlockerAt(tile, dude, target));
            if (blocker is not null)
            {
                _host.Log($"Your shot is blocked by the {_host.ObjectName(blocker)}.");
                return false;
            }
        }

        // AP: in combat the round budget rules; the first swing opens combat
        // with a fresh budget.
        switch (_phase)
        {
            case CombatPhase.PlayerTurn when _dudeAp < apCost:
                _host.Log("Not enough action points.");
                return false;
            case CombatPhase.EnemyTurn or CombatPhase.GameOver:
                return false;
            case CombatPhase.Idle:
                _dudeAp = attacker.MaxActionPoints;
                break;
        }
        _dudeAp -= apCost;

        // The engine reg_anim_clear()s both parties before choreographing.
        _host.ClearAnimation(target);
        dude.Rotation = HexGrid.RotationTo(dude.HexTile, target.HexTile);

        (int chance, bool hit, int damage) = RollAttack(attacker, defender, weaponProto, weaponItem,
            distance, crittersInPath, attackerIsDude: true);
        if (isGun)
            weaponItem!.AmmoQuantity = _host.WeaponAmmo(weaponProto!, weaponItem) - 1;
        _pendingAttack = new PendingAttack(dude, target, chance, hit, damage);
        _host.Transcript($"attack {_host.ObjectName(target)}@{target.HexTile}"
            + $"{(weaponProto is null ? "" : $" [{_host.ObjectNameByPid(weaponProto.Pid)}{(isGun ? $" {weaponItem!.AmmoQuantity}rnd d{distance}" : "")}]")}: chance={chance}% hit={hit} damage={damage}");

        _host.OnAttackStarted(dude, weaponProto);

        if (_phase == CombatPhase.Idle)
            BeginCombat(target);
        return true;
    }

    /// <summary>Roll an attack with the equipped weapon (or fists). Guns use the
    /// ranged to-hit (distance/PE, ammo AC mod, min-ST, crowd) and ammo damage
    /// mods; melee keeps the phase-6 path.</summary>
    private (int Chance, bool Hit, int Damage) RollAttack(
        CritterState attacker, CritterState defender,
        ProtoInfo? weaponProto, MapObject? weaponItem, int distance, int crittersInPath,
        bool attackerIsDude)
    {
        int chance;
        bool isGun = weaponProto?.Weapon is { } w && w.IsGun(weaponProto.ExtendedFlags);
        if (isGun)
        {
            AmmoProtoStats? ammo = weaponItem is null ? null : _host.LoadedAmmo(weaponProto!, weaponItem);
            chance = RangedMath.ToHitChance(
                attacker.SmallGunsSkill, distance,
                attacker.Stat(CritterStat.Perception), attackerIsDude,
                defender.ArmorClass, ammo?.AcModifier ?? 0,
                weaponProto!.Weapon!.MinStrength, attacker.Stat(CritterStat.Strength),
                crittersInPath);
        }
        else
        {
            int skill = weaponProto is null ? attacker.UnarmedSkill : attacker.MeleeWeaponsSkill;
            chance = CombatMath.ToHitChance(skill, defender);
        }

        bool hit = CombatMath.RollHit(_rng, chance);
        int damage = 0;
        if (hit)
        {
            if (isGun)
            {
                AmmoProtoStats? ammo = weaponItem is null ? null : _host.LoadedAmmo(weaponProto!, weaponItem);
                damage = RangedMath.RollDamage(_rng,
                    weaponProto!.Weapon!.MinDamage, weaponProto.Weapon.MaxDamage, defender,
                    ammo?.DrModifier ?? 0, ammo?.DamageMultiplier ?? 1, ammo?.DamageDivisor ?? 1);
            }
            else
            {
                damage = weaponProto?.Weapon is { } weapon
                    ? CombatMath.RollWeaponDamage(_rng, attacker, defender, weapon.MinDamage, weapon.MaxDamage)
                    : CombatMath.RollDamage(_rng, attacker, defender);
            }
        }

        return (chance, hit, damage);
    }

    /// <summary>Damage-on-completion + corpse conversion, polled every frame
    /// (the engine's _combat_anim_finished callback chain).</summary>
    public void ProcessAnimations()
    {
        if (_pendingAttack is { } attack && !_host.IsAnimating(attack.Attacker))
        {
            _pendingAttack = null;
            ResolveAttack(attack);
        }

        if (_fallingCritters.Count > 0)
        {
            foreach ((MapObject critter, int deathAnim) in _fallingCritters.ToArray())
            {
                if (_host.IsFallInProgress(critter))
                    continue;
                _fallingCritters.Remove(critter);
                _host.ConvertToCorpse(critter, deathAnim);
            }
        }
    }

    private void ResolveAttack(PendingAttack attack)
    {
        MapObject? dude = _host.Dude;
        bool byDude = attack.Attacker == dude;
        string targetName = _host.ObjectName(attack.Target);
        string attackerName = _host.ObjectName(attack.Attacker);

        if (!attack.Hit)
        {
            _host.Log(byDude ? $"You missed the {targetName}." : $"The {attackerName} misses you.");
            return;
        }

        attack.Target.CurrentHp -= attack.Damage;
        _host.Log(byDude
            ? $"You hit the {targetName} for {attack.Damage} damage."
            : $"The {attackerName} hits you for {attack.Damage} damage.");

        // damage_p_proc runs as damage applies, fixedParam = amount, source =
        // attacker (combat.cc:4850-4851; party-on-party skip is moot here).
        if (attack.Target != dude && attack.Target.Sid != -1)
            foreach (string line in _host.RunDamageProc(attack.Target, attack.Attacker, attack.Damage))
                _host.Log(line);

        if (attack.Target.CurrentHp <= 0)
        {
            if (attack.Target == dude)
                GameOver();
            else
                KillCritter(attack.Target, attack.Attacker);
            return;
        }

        if (attack.Target != dude)
            _host.OnTargetHit(attack.Target);
    }

    private void KillCritter(MapObject critter, MapObject? killer = null)
    {
        // Engine death order (combat.cc:4850-4876): destroy_p_proc with source =
        // killer, then proto XP accrues for the dude's kills unless the script
        // called script_overrides, then the script is removed.
        bool xpOverridden = false;
        if (critter.Sid != -1)
        {
            (IReadOnlyList<string> lines, bool overridden) = _host.RunDestroyProc(critter, killer ?? _host.Dude);
            foreach (string line in lines)
                _host.Log(line);
            xpOverridden = overridden;
        }

        // Engine: kills by the dude OR his team accrue XP (combat.cc:4860).
        bool dudeTeamKill = killer == _host.Dude || (killer is not null && killer.Team == 0);
        if (!xpOverridden && dudeTeamKill && _host.GetCritterState(critter) is { } stats)
            _xpPending += stats.Proto.Experience;

        _host.RemovePartyMember(critter);

        critter.CombatResults |= 0x80; // DAM_DEAD
        critter.Sid = -1; // the engine removes the script on death (combat.cc:4876)
        _host.OnCritterRemoved(critter);
        _host.Log($"The {_host.ObjectName(critter)} dies.");

        int deathAnim = _host.PickDeathAnim(critter);
        if (_host.StartDeathFall(critter, deathAnim))
            _fallingCritters[critter] = deathAnim;
    }

    // ====================================================================
    //  Combat lifecycle
    // ====================================================================

    private void BeginCombat(MapObject target)
    {
        _phase = CombatPhase.PlayerTurn;
        _round = 1;
        _hostiles.Clear();
        _enemyQueue.Clear();
        _actingEnemy = null;
        _hostiles.Add(target);
        AddJoiners();
        _host.Log($"Combat begins — round 1, your turn (AP {_dudeAp}).");
    }

    /// <summary>Scriptless hostility, the engine's combat_ai team rule: living
    /// same-team critters within sight range join the fight at round start.</summary>
    private void AddJoiners()
    {
        MapObject? dude = _host.Dude;
        if (dude is null)
            return;
        foreach (MapObject critter in _host.CombatCritters.Where(o =>
            !_hostiles.Contains(o) && CombatRules.ShouldJoin(o, _hostiles, dude.HexTile)).ToList())
        {
            _hostiles.Add(critter);
            critter.WhoHitMeCid = -1; // marks the dude as the aggressor
            _host.Log($"The {_host.ObjectName(critter)} joins the fight!");
            _host.Transcript($"joins: {_host.ObjectName(critter)}@{critter.HexTile} (team {critter.Team})");
        }
    }

    public void EndPlayerTurn()
    {
        if (_phase != CombatPhase.PlayerTurn || _pendingAttack is not null)
            return;

        _phase = CombatPhase.EnemyTurn;
        BuildEnemyQueue();
    }

    private void BuildEnemyQueue()
    {
        _enemyQueue.Clear();
        _actingEnemy = null;
        foreach (MapObject hostile in _hostiles.Where(h => !h.IsDead)
            .OrderByDescending(h => _host.GetCritterState(h)?.Sequence ?? 0))
            _enemyQueue.Enqueue(hostile);

        _allyQueue.Clear();
        _actingAlly = null;
        foreach (MapObject ally in _host.PartyMembers.Where(m => !m.IsDead))
            _allyQueue.Enqueue(ally);
    }

    /// <summary>A script's attack external fired (scripted aggro). The aggressor
    /// gets the opening turn, like scriptsRequestCombat starting combat with the
    /// script's self as attacker.</summary>
    public void BeginScriptAggro(MapObject attacker, MapObject target)
    {
        MapObject? dude = _host.Dude;
        if (dude is null || _gameOver)
            return;
        if (target != dude || attacker == dude)
            return; // NPC-vs-NPC fights are out of PoC scope

        if (_phase == CombatPhase.Idle)
        {
            _host.StopDude(); // ambush interrupts the walk
            _round = 1;
            _hostiles.Clear();
            _hostiles.Add(attacker);
            attacker.WhoHitMeCid = -1;
            AddJoiners();
            if (_host.GetCritterState(dude) is { } stats)
                _dudeAp = stats.MaxActionPoints;
            _phase = CombatPhase.EnemyTurn;
            BuildEnemyQueue();
            _host.Log($"The {_host.ObjectName(attacker)} attacks you!");
            _host.Transcript($"scripted-aggro: {_host.ObjectName(attacker)}@{attacker.HexTile} starts combat");
        }
        else if (_hostiles.Add(attacker))
        {
            attacker.WhoHitMeCid = -1;
            _host.Log($"The {_host.ObjectName(attacker)} joins the fight!");
        }
    }

    /// <summary>ported from fallout2-ce src/combat.cc _combat_should_end():
    /// combat is over when nothing hostile is left standing.</summary>
    private bool CombatShouldEnd() => !_hostiles.Any(h => !h.IsDead);

    private void EndCombat()
    {
        _phase = CombatPhase.Idle;
        _hostiles.Clear();
        _enemyQueue.Clear();
        _actingEnemy = null;
        if (_host.Dude is { } dude && _host.GetCritterState(dude) is { } stats)
            _dudeAp = stats.MaxActionPoints;
        _host.Log("Combat ends.");

        if (_xpPending > 0)
        {
            _host.AwardXp(_xpPending);
            _xpPending = 0;
        }
    }

    /// <summary>Public so a non-combat death (script/trap damage) can trigger the
    /// full game-over (state + transcript + host death screen).</summary>
    public void GameOver()
    {
        _phase = CombatPhase.GameOver;
        _gameOver = true;
        _host.GameOver();
        _host.Transcript("GAME OVER");
    }

    /// <summary>Kill a critter outside the combat loop (script/trap damage),
    /// running the same death path (destroy proc, XP accrual, corpse).</summary>
    public void Kill(MapObject critter, MapObject? killer = null) => KillCritter(critter, killer);

    // ====================================================================
    //  Turn stepping
    // ====================================================================

    /// <summary>ProcessAnimations + the flattened _combat_turn_run loop. Steps the
    /// turn machine once nothing is animating (the engine's blocking
    /// while(_combat_turn_running > 0) becomes four early-return guards).</summary>
    public void Step()
    {
        ProcessAnimations();
        UpdateCombat();
    }

    private void UpdateCombat()
    {
        if (_phase is CombatPhase.Idle or CombatPhase.GameOver)
            return;
        if (_pendingAttack is not null || _fallingCritters.Count > 0)
            return;
        if (_actingEnemy is { } moving && _host.IsWalkerMoving(moving))
            return;
        if (_actingAlly is { } movingAlly && _host.IsWalkerMoving(movingAlly))
            return;

        if (_host.Dude is { } dude && dude.CurrentHp <= 0)
        {
            GameOver();
            return;
        }

        if (CombatShouldEnd())
        {
            EndCombat();
            return;
        }

        if (_phase == CombatPhase.EnemyTurn)
            StepEnemyTurn();
    }

    private void StepEnemyTurn()
    {
        if (_actingEnemy is { } acting && !acting.IsDead)
        {
            if (TryEnemyAction(acting))
                return;
            _actingEnemy = null;
        }

        while (_enemyQueue.Count > 0)
        {
            MapObject enemy = _enemyQueue.Dequeue();
            if (enemy.IsDead)
                continue;
            _actingEnemy = enemy;
            _actingEnemyAp = _host.GetCritterState(enemy)?.MaxActionPoints ?? 5;
            if (TryEnemyAction(enemy))
                return;
            _actingEnemy = null;
        }

        // Companions take their swings after the hostiles.
        if (_actingAlly is { } actingAlly && !actingAlly.IsDead)
        {
            if (TryAllyAction(actingAlly))
                return;
            _actingAlly = null;
        }

        while (_allyQueue.Count > 0)
        {
            MapObject ally = _allyQueue.Dequeue();
            if (ally.IsDead)
                continue;
            _actingAlly = ally;
            _actingAllyAp = _host.GetCritterState(ally)?.MaxActionPoints ?? 5;
            if (TryAllyAction(ally))
                return;
            _actingAlly = null;
        }

        // Everyone acted: next round.
        _round++;
        AddJoiners();
        if (_host.Dude is { } dude && _host.GetCritterState(dude) is { } stats)
            _dudeAp = stats.MaxActionPoints;
        _phase = CombatPhase.PlayerTurn;
        _host.Log($"Round {_round} — your turn (AP {_dudeAp}).");
    }

    /// <summary>One AI action: punch when adjacent, else an AP-budgeted approach
    /// at 1 AP per hex (the engine's combat_ai movement budget).</summary>
    private bool TryEnemyAction(MapObject enemy)
    {
        MapObject? dude = _host.Dude;
        if (dude is null)
            return false;

        // Enemies pick the nearest of the dude and his living companions.
        MapObject defenderObj = dude;
        int bestDistance = HexGrid.Distance(enemy.HexTile, dude.HexTile);
        foreach (MapObject ally in _host.PartyMembers)
        {
            if (ally.IsDead)
                continue;
            int d = HexGrid.Distance(enemy.HexTile, ally.HexTile);
            if (d < bestDistance)
            {
                bestDistance = d;
                defenderObj = ally;
            }
        }

        int dudeTile = defenderObj.HexTile;
        (ProtoInfo? enemyWeapon, MapObject? enemyWeaponItem) = _host.EquippedWeapon(enemy);
        bool enemyGun = enemyWeapon?.Weapon is { } ew && ew.IsGun(enemyWeapon.ExtendedFlags);
        int enemyDistance = HexGrid.Distance(enemy.HexTile, dudeTile);

        // _ai_try_attack shape: reload-if-empty, approach if blocked/far, else
        // stand and shoot; melee fallback when dry.
        if (enemyGun && _host.WeaponAmmo(enemyWeapon!, enemyWeaponItem!) <= 0)
        {
            if (_actingEnemyAp >= RangedMath.ReloadApCost
                && _host.TryReload(enemy, enemyWeapon!, enemyWeaponItem!))
            {
                _actingEnemyAp -= RangedMath.ReloadApCost;
                return true;
            }
            enemyWeapon = null; // dry and no ammo: fists
            enemyWeaponItem = null;
            enemyGun = false;
        }

        int attackRange = enemyGun ? enemyWeapon!.Weapon!.MaxRange1
            : Math.Min(enemyWeapon?.Weapon?.MaxRange1 ?? 1, 2);
        int attackCost = enemyWeapon?.Weapon?.ApCost ?? CombatMath.PunchApCost;
        int enemyCritters = 0;
        bool shotBlocked = false;
        if (enemyGun && enemyDistance <= attackRange)
        {
            (MapObject? blocker, enemyCritters) = LineOfFire.Trace(
                enemy.HexTile, dudeTile, tile => _host.ShootBlockerAt(tile, enemy, defenderObj));
            shotBlocked = blocker is not null;
        }

        if (enemyDistance <= attackRange && !shotBlocked)
        {
            if (_actingEnemyAp < attackCost)
                return false;
            _actingEnemyAp -= attackCost;
            EnemyAttack(enemy, defenderObj, enemyWeapon, enemyWeaponItem, enemyDistance, enemyCritters);
            return true;
        }

        if (_actingEnemyAp < 1)
            return false;
        byte[]? path = Pathfinder.FindPath(enemy.HexTile, dudeTile, tile => _host.IsBlocked(tile));
        if (path is null || path.Length <= 1)
            return false;

        int steps = Math.Min(path.Length - 1, _actingEnemyAp); // stop adjacent
        _actingEnemyAp -= steps;
        int targetTile = enemy.HexTile;
        for (int i = 0; i < steps; i++)
            targetTile = HexGrid.TileInDirection(targetTile, path[i]);
        return _host.StartWalk(enemy, targetTile);
    }

    /// <summary>A companion's action: punch/shoot the nearest living hostile, else
    /// approach it — the same minimal AI the enemies run.</summary>
    private bool TryAllyAction(MapObject ally)
    {
        MapObject? target = _hostiles.Where(h => !h.IsDead)
            .OrderBy(h => HexGrid.Distance(ally.HexTile, h.HexTile))
            .FirstOrDefault();
        if (target is null)
            return false;

        (ProtoInfo? weaponProto, MapObject? weaponItem) = _host.EquippedWeapon(ally);
        bool isGun = weaponProto?.Weapon is { } w && w.IsGun(weaponProto.ExtendedFlags);
        int distance = HexGrid.Distance(ally.HexTile, target.HexTile);

        if (isGun && _host.WeaponAmmo(weaponProto!, weaponItem!) <= 0)
        {
            if (_actingAllyAp >= RangedMath.ReloadApCost
                && _host.TryReload(ally, weaponProto!, weaponItem!))
            {
                _actingAllyAp -= RangedMath.ReloadApCost;
                return true;
            }
            weaponProto = null;
            weaponItem = null;
            isGun = false;
        }

        int range = isGun ? weaponProto!.Weapon!.MaxRange1
            : Math.Min(weaponProto?.Weapon?.MaxRange1 ?? 1, 2);
        int apCost = weaponProto?.Weapon?.ApCost ?? CombatMath.PunchApCost;
        int crittersInPath = 0;
        bool blocked = false;
        if (isGun && distance <= range)
        {
            (MapObject? blocker, crittersInPath) = LineOfFire.Trace(
                ally.HexTile, target.HexTile, tile => _host.ShootBlockerAt(tile, ally, target));
            blocked = blocker is not null;
        }

        if (distance <= range && !blocked)
        {
            if (_actingAllyAp < apCost)
                return false;
            _actingAllyAp -= apCost;
            if (_host.GetCritterState(ally) is not { } attacker || _host.GetCritterState(target) is not { } defender)
                return false;
            ally.Rotation = HexGrid.RotationTo(ally.HexTile, target.HexTile);
            (int chance, bool hit, int damage) = RollAttack(attacker, defender, weaponProto, weaponItem,
                distance, crittersInPath, attackerIsDude: false);
            if (isGun && weaponItem is not null)
                weaponItem.AmmoQuantity = _host.WeaponAmmo(weaponProto!, weaponItem) - 1;
            _pendingAttack = new PendingAttack(ally, target, chance, hit, damage);
            _host.Transcript($"ally-attack {_host.ObjectName(ally)} -> {_host.ObjectName(target)}@{target.HexTile}"
                + $"{(weaponProto is null ? "" : $" [{_host.ObjectNameByPid(weaponProto.Pid)}]")}: chance={chance}% hit={hit} damage={damage}");
            _host.OnAttackStarted(ally, weaponProto);
            return true;
        }

        if (_actingAllyAp < 1)
            return false;
        byte[]? path = Pathfinder.FindPath(ally.HexTile, target.HexTile, tile => _host.IsBlocked(tile));
        if (path is null || path.Length <= 1)
            return false;
        int steps = Math.Min(path.Length - 1, _actingAllyAp);
        _actingAllyAp -= steps;
        int walkTarget = ally.HexTile;
        for (int i = 0; i < steps; i++)
            walkTarget = HexGrid.TileInDirection(walkTarget, path[i]);
        return _host.StartWalk(ally, walkTarget);
    }

    private void EnemyAttack(MapObject enemy, MapObject defenderObj, ProtoInfo? weaponProto,
        MapObject? weaponItem, int distance, int crittersInPath)
    {
        if (_host.Dude is null || _host.GetCritterState(enemy) is not { } attacker
            || _host.GetCritterState(defenderObj) is not { } defender)
            return;

        enemy.Rotation = HexGrid.RotationTo(enemy.HexTile, defenderObj.HexTile);

        bool isGun = weaponProto?.Weapon is { } w && w.IsGun(weaponProto.ExtendedFlags);
        (int chance, bool hit, int damage) = RollAttack(attacker, defender, weaponProto, weaponItem,
            distance, crittersInPath, attackerIsDude: false);
        if (isGun && weaponItem is not null)
            weaponItem.AmmoQuantity = _host.WeaponAmmo(weaponProto!, weaponItem) - 1;
        _pendingAttack = new PendingAttack(enemy, defenderObj, chance, hit, damage);
        _host.Transcript($"enemy-attack {_host.ObjectName(enemy)}@{enemy.HexTile}"
            + $"{(weaponProto is null ? "" : $" [{_host.ObjectNameByPid(weaponProto.Pid)}{(isGun ? $" d{distance}" : "")}]")}: chance={chance}% hit={hit} damage={damage}");

        _host.OnAttackStarted(enemy, weaponProto);
    }

    // ====================================================================
    //  Viewer-driven actions that cost combat AP / mutate combat state
    // ====================================================================

    /// <summary>Manual reload (R key): 2 AP during the player's turn, free out of
    /// combat. Ported from the former ViewerGame R-handler.</summary>
    public void ReloadEquippedWeapon()
    {
        MapObject? dude = _host.Dude;
        if (dude is null || _host.EquippedWeapon(dude) is not (not null, not null) equipped
            || equipped.Proto!.Weapon is not { AmmoCapacity: > 0 })
            return;

        if (_phase == CombatPhase.PlayerTurn)
        {
            if (_dudeAp >= RangedMath.ReloadApCost && _host.TryReload(dude, equipped.Proto, equipped.Item!))
                _dudeAp -= RangedMath.ReloadApCost;
        }
        else if (_phase == CombatPhase.Idle)
        {
            _host.TryReload(dude, equipped.Proto, equipped.Item!);
        }
    }

    /// <summary>Gate + charge an item-use action (drug, etc.): PlayerTurn spends
    /// AP (false + log if short), Idle is free, enemy turn/game-over blocks.
    /// Returns true if the action may proceed.</summary>
    public bool TryUseActionPoints(int apCost)
    {
        if (_phase == CombatPhase.PlayerTurn)
        {
            if (_dudeAp < apCost)
            {
                _host.Log("Not enough action points.");
                return false;
            }
            _dudeAp -= apCost;
            return true;
        }
        return _phase == CombatPhase.Idle;
    }

    /// <summary>A critter left the hostile set (recruited into the party).</summary>
    public void RemoveHostile(MapObject critter) => _hostiles.Remove(critter);

    public void Reset()
    {
        _phase = CombatPhase.Idle;
        _hostiles.Clear();
        _enemyQueue.Clear();
        _actingEnemy = null;
        _pendingAttack = null;
        _fallingCritters.Clear();
        _gameOver = false;
    }
}
