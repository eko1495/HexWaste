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
    private sealed record PendingAttack(MapObject Attacker, MapObject Target, int Chance, bool Hit, int Damage, int CritFlags, bool CanKnockback,
        // P26 gore: the killing blow's damage type + attacker animation feed DeathAnims.Pick.
        int DamageType = 0, int AttackerAnim = DeathAnims.FallBack);
    private PendingAttack? _pendingAttack;

    /// <summary>A thrown weapon in flight: lands when the throw animation finishes —
    /// an explosive detonates (AoE), a spear/rock damages the target and drops
    /// recoverable on the ground.</summary>
    private sealed record PendingThrow(MapObject Thrower, MapObject? Target, int TargetTile,
        bool Hit, int Damage, bool Explosive, int MinDamage, int MaxDamage, ProtoInfo Proto, MapObject Item,
        int CritFlags = 0);
    private PendingThrow? _pendingThrow;

    /// <summary>A burst in flight: every round is rolled up front; the accumulated
    /// damage lands and the magazine is decremented (in one batch, combat.cc:5349)
    /// when the single muzzle-flash animation completes. AmmoBefore − RoundsFired is
    /// the post-burst magazine; RoundsHit of the center-line rounds connected.</summary>
    private sealed record PendingBurst(MapObject Attacker, MapObject Target, ProtoInfo WeaponProto,
        MapObject WeaponItem, int AmmoBefore, int RoundsFired, int RoundsHit, int TotalDamage,
        IReadOnlyList<BurstExtra> Extras);
    private PendingBurst? _pendingBurst;

    /// <summary>A collateral burst victim — a critter other than the main target struck
    /// by the cone's center/left/right lines (combat.cc _shoot_along_path "extras").</summary>
    private sealed record BurstExtra(MapObject Victim, int RoundsHit, int Damage);

    /// <summary>Critters playing their death fall; value = death anim (20/21).</summary>
    private readonly Dictionary<MapObject, int> _fallingCritters = [];

    /// <summary>Critters knocked prone by a crit (DAM_KNOCKED_DOWN) — they stand up
    /// (3 AP) at their next turn and are +40 to hit while down. Combat-scoped
    /// (cleared on Reset); never saved (the engine can't save mid-combat).</summary>
    private readonly HashSet<MapObject> _knockedDown = [];
    private const int OBJECT_MULTIHEX = 0x800;
    private const int CRITTER_NO_KNOCKBACK = 0x4000;
    private const int StandUpApCost = 3; // _combat_standup (combat.cc:5391)

    /// <summary>The knockout-wake event queue + a combat-owned monotonic tick that
    /// advances <see cref="TicksPerRound"/> per round (P14-M2). The wake fires off this
    /// tick, NOT the saved GameClock — headless --fight loops don't advance wall-time,
    /// and mutating the saved clock mid-fight would churn day-boundary state. The KO
    /// delay (10*(35-3*EN)) and EN scaling stay exact; the round cadence is the
    /// documented divergence (same class as LoF greedy-hex).</summary>
    private readonly EventQueue _events = new();
    private long _combatTick;
    private const int TicksPerRound = 50; // _combat_sequence: 5 game-seconds × 10 ticks/s

    /// <summary>The honored status flags written to MapObject.CombatResults from a crit
    /// (P14): knockout + lose-turn (combat-transient, cleared on wake/skip/end) and the
    /// crippled limbs + blind (persist via CombatResults until a Doctor clears them).</summary>
    private const int StatusFlags = CriticalTables.DamKnockedOut | CriticalTables.DamLoseTurn
        | CriticalTables.DamCripLimbs | CriticalTables.DamBlind;

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
    public bool IsResolving => _pendingAttack is not null || _pendingThrow is not null || _pendingBurst is not null || _fallingCritters.Count > 0;
    /// <summary>Resolving OR an NPC walker is mid-move — the engine is "busy".</summary>
    public bool IsBusy => IsResolving || _host.IsAnyWalkerMoving();
    public IReadOnlyCollection<MapObject> Hostiles => _hostiles;

    /// <summary>Load path: seed the dude's AP outside combat (SpawnDude).</summary>
    public void SetDudeAp(int ap) => _dudeAp = ap;

    /// <summary>Set the dude's turn budget to max AP minus the over-encumbrance penalty (P24;
    /// stat.cc:198 — the engine bakes it into STAT_MAXIMUM_ACTION_POINTS). 0 penalty when within
    /// capacity, so an un-overloaded dude (every combat golden) is unchanged.</summary>
    private void ResetDudeAp(CritterState dude) =>
        _dudeAp = Math.Max(0, dude.MaxActionPoints - _host.DudeEncumbranceApPenalty());

    /// <summary>Charge the dude's turn AP for movement (or any non-attack action), clamped
    /// at 0 (phase-18 M0: combat movement costs MovePointCost per hex).</summary>
    public void SpendDudeAp(int amount) => _dudeAp = Math.Max(0, _dudeAp - amount);

    /// <summary>combat.cc:5655 crippled-arm gate: with a WEAPON equipped, both arms crippled
    /// blocks any weapon attack and one crippled arm blocks a TWO-HANDED weapon. Unarmed
    /// (no weapon) is never gated here — you can still punch. Returns the block reason, or
    /// null if the attack is allowed (phase-18 M2). Doctor heals the crip-arm bit (P14-M5).</summary>
    private static string? WeaponBlockedByCrippledArms(MapObject attacker, ProtoInfo? weaponProto)
    {
        if (weaponProto?.Weapon is null)
            return null;
        int cr = attacker.CombatResults;
        bool left = (cr & CriticalTables.DamCripArmLeft) != 0;
        bool right = (cr & CriticalTables.DamCripArmRight) != 0;
        if (left && right)
            return "both arms are crippled";
        if ((left || right) && WeaponProtoStats.IsTwoHanded(weaponProto.ExtendedFlags))
            return "your crippled arm can't handle a two-handed weapon";
        return null;
    }

    // ====================================================================
    //  Attacks
    // ====================================================================

    /// <summary>
    /// Attacks an adjacent/in-range critter. The outcome is rolled HERE, before
    /// any animation — damage waits for the swing to finish (ported from
    /// fallout2-ce src/combat.cc _combat_attack() / combatAttemptAttack()).
    /// </summary>
    public bool TryAttack(MapObject target, int hitLocation = CriticalTables.LocationUncalled)
    {
        MapObject? dude = _host.Dude;
        if (dude is null || _pendingAttack is not null || _pendingBurst is not null || target == dude)
            return false;
        if (Fid.Type(target.Fid) is not ObjectType.Critter || target.IsDead)
            return false;
        if (_host.GetCritterState(dude) is not { } attacker || _host.GetCritterState(target) is not { } defender)
            return false;

        // P29-M1: Fast Shot can't aim (item.cc:1825 critterCanAim) — the engine never offers the
        // aim dialog to a Fast Shot dude, so we coerce any called shot back to uncalled (no +1 AP,
        // no aimed crit bonus). Inert without the trait.
        if (hitLocation != CriticalTables.LocationUncalled && _host.DudeHasTrait(TraitModifiers.FastShot))
            hitLocation = CriticalTables.LocationUncalled;

        (ProtoInfo? weaponProto, MapObject? weaponItem) = _host.EquippedWeapon(dude);
        if (WeaponBlockedByCrippledArms(dude, weaponProto) is { } crippleReason)
        {
            _host.Log($"You can't attack — {crippleReason}.");
            return false;
        }
        bool isGun = weaponProto?.Weapon is { } wstats && wstats.IsGun(weaponProto.ExtendedFlags);
        int range = isGun ? weaponProto!.Weapon!.MaxRange1
            : Math.Min(weaponProto?.Weapon?.MaxRange1 ?? 1, 2); // throwers melee-capped until rung (a)
        int apCost = Math.Max(1, (weaponProto?.Weapon?.ApCost ?? CombatMath.PunchApCost)
            + (hitLocation != CriticalTables.LocationUncalled ? 1 : 0) // aimed shot +1 AP (item.cc:1706)
            // P28-M3: Bonus Rate of Fire (−1 ranged) / Bonus HtH Attacks (−1 melee/unarmed) — item.cc:1693.
            - (isGun
                ? (_host.DudePerkRank(Perks.PerkId.BonusRateOfFire) > 0 ? 1 : 0)
                : (_host.DudePerkRank(Perks.PerkId.BonusHthAttacks) > 0 ? 1 : 0))
            // P29-M1: Fast Shot − 1 AP for a long-range weapon (range > 2; item.cc:1679). Inert
            // without the trait; the Math.Max(1) floor mirrors the engine's "actionPoints < 1 → 1".
            - (range > 2 && _host.DudeHasTrait(TraitModifiers.FastShot) ? 1 : 0));
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
                _host.OnWeaponOutOfAmmo(weaponProto!);
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
                ResetDudeAp(attacker);
                break;
        }
        _dudeAp -= apCost;

        // The engine reg_anim_clear()s both parties before choreographing.
        _host.ClearAnimation(target);
        dude.Rotation = HexGrid.RotationTo(dude.HexTile, target.HexTile);

        (int chance, bool hit, int damage, int critFlags) = RollAttack(attacker, defender, weaponProto, weaponItem,
            distance, crittersInPath, attackerIsDude: true, defenderIsDude: target == dude, hitLocation);

        // P29-M1: Jinxed — a missed dude attack can fumble into a critical failure (combat.cc:3857
        // rolls randomBetween(0,1) on every ROLL_FAILURE when the dude has the trait). SIMPLIFIED:
        // we honour only DAM_LOSE_TURN (the most common _cf_table low-severity effect) by zeroing the
        // dude's AP — not the full 7×5 table (drop/explode/cripple/on-fire), and dude-only (the engine
        // fumbles EVERY combatant when the dude is Jinxed). Gated on CriticalsEnabled (our day-2 signal;
        // the engine gates the dude's crit-failures at day 6 — a documented divergence). The d2 draw is
        // taken ONLY when the trait is set, so a trait-less dude draws nothing → goldens byte-identical.
        if (!hit && _host.CriticalsEnabled && _host.DudeHasTrait(TraitModifiers.Jinxed) && _rng.Next(0, 2) == 1)
        {
            _dudeAp = 0;
            _host.Log("Jinxed! You fumble and lose your turn.");
        }

        if (isGun)
            weaponItem!.AmmoQuantity = _host.WeaponAmmo(weaponProto!, weaponItem) - 1;
        _pendingAttack = new PendingAttack(dude, target, chance, hit, damage, critFlags, CanKnockback: !isGun,
            DamageType: weaponProto?.Weapon?.DamageType ?? 0, // P26 gore context
            AttackerAnim: DeathAnims.AttackAnimFor(isGun, weaponProto?.Weapon is not null));
        _host.Transcript($"attack {_host.ObjectName(target)}@{target.HexTile}"
            + $"{(weaponProto is null ? "" : $" [{_host.ObjectNameByPid(weaponProto.Pid)}{(isGun ? $" {weaponItem!.AmmoQuantity}rnd d{distance}" : "")}]")}: chance={chance}% hit={hit} damage={damage}{CritTag(critFlags)}");

        _host.OnAttackStarted(dude, target, weaponProto);

        if (_phase == CombatPhase.Idle)
            BeginCombat(target);
        return true;
    }

    /// <summary>ANIM_FIRE_BURST attack-mode nibble (item.cc _attack_subtype[7]).</summary>
    private const int BurstAnim = 7;

    /// <summary>The equipped weapon can fire a burst: its primary or secondary attack
    /// mode is BURST (extendedFlags nibble == 7; item.cc:131-141 + 1148-1165). The 3
    /// shippable burst guns are SINGLE/BURST (10mm SMG, Tommy Gun, Combat Shotgun).</summary>
    public static bool IsBurstWeapon(ProtoInfo? proto) =>
        proto?.Weapon is not null
        && (((proto.ExtendedFlags >> 4) & 0xF) == BurstAnim || (proto.ExtendedFlags & 0xF) == BurstAnim);

    /// <summary>
    /// Fire a burst at a critter (combat.cc _compute_spray, ANIM_FIRE_BURST). The
    /// round count is min(loaded ammo, weapon burst-rounds); the engine splits those
    /// rounds across a center/left/right cone. The outcome is rolled HERE; damage and
    /// the magazine decrement land when the muzzle-flash animation finishes.
    ///
    /// P13-M2: the collateral cone is now modelled — the left/right lines (rotation±1,
    /// combat.cc:3769-3784) and any non-target critter on the three lines take
    /// collateral fire (the up-to-6 "extras" of _shoot_along_path). DOCUMENTED
    /// APPROXIMATION: the main target's own hit count keeps the v1 centre-exposure
    /// model (so a 1-on-1 burst is byte-identical), the line sweep reuses the Bresenham
    /// Trace (only the end-tiles use the exact _tile_num_beyond), and _check_ranged_miss
    /// is not ported. In a duel the cone lines are empty → no collateral, no extra RNG.
    /// </summary>
    public bool TryBurst(MapObject target)
    {
        MapObject? dude = _host.Dude;
        if (dude is null || _pendingAttack is not null || _pendingBurst is not null || _pendingThrow is not null || target == dude)
            return false;
        if (Fid.Type(target.Fid) is not ObjectType.Critter || target.IsDead)
            return false;
        if (_host.GetCritterState(dude) is not { } attacker || _host.GetCritterState(target) is not { } defender)
            return false;

        (ProtoInfo? weaponProto, MapObject? weaponItem) = _host.EquippedWeapon(dude);
        if (WeaponBlockedByCrippledArms(dude, weaponProto) is { } crippleReason)
        {
            _host.Log($"You can't fire — {crippleReason}.");
            return false;
        }
        if (!IsBurstWeapon(weaponProto) || weaponItem is null)
        {
            _host.Log("This weapon can't fire a burst.");
            return false;
        }

        int distance = HexGrid.Distance(dude.HexTile, target.HexTile);
        if (distance > weaponProto!.Weapon!.MaxRange1)
        {
            _host.Log("Too far away.");
            return false;
        }

        // _combat_check_bad_shot: empty mag (auto-reload, its own action), then LoF.
        if (_host.WeaponAmmo(weaponProto, weaponItem) <= 0)
        {
            if (_phase == CombatPhase.PlayerTurn
                && _dudeAp >= RangedMath.ReloadApCost
                && _host.TryReload(dude, weaponProto, weaponItem))
            {
                _dudeAp -= RangedMath.ReloadApCost;
                return true;
            }
            if (_phase != CombatPhase.PlayerTurn && _host.TryReload(dude, weaponProto, weaponItem))
                return true;
            _host.Log("Out of ammo.");
            _host.OnWeaponOutOfAmmo(weaponProto);
            return false;
        }

        (MapObject? blocker, int crittersInPath) = LineOfFire.Trace(
            dude.HexTile, target.HexTile, tile => _host.ShootBlockerAt(tile, dude, target));
        if (blocker is not null)
        {
            _host.Log($"Your shot is blocked by the {_host.ObjectName(blocker)}.");
            return false;
        }

        // Burst is the weapon's SECONDARY action-point cost (item.cc:1943); burst
        // can't be aimed (item.cc:1830), so there is no aimed +1.
        int apCost = weaponProto.Weapon.ApCost2 > 0 ? weaponProto.Weapon.ApCost2 : weaponProto.Weapon.ApCost;
        switch (_phase)
        {
            case CombatPhase.PlayerTurn when _dudeAp < apCost:
                _host.Log("Not enough action points.");
                return false;
            case CombatPhase.EnemyTurn or CombatPhase.GameOver:
                return false;
            case CombatPhase.Idle:
                ResetDudeAp(attacker);
                break;
        }
        _dudeAp -= apCost;

        _host.ClearAnimation(target);
        dude.Rotation = HexGrid.RotationTo(dude.HexTile, target.HexTile);

        int ammoBefore = _host.WeaponAmmo(weaponProto, weaponItem);
        (int accuracy, int roundsFired, int roundsHit, int totalDamage, List<BurstExtra> extras) =
            RollBurst(dude, target, attacker, defender, weaponProto, weaponItem, distance, crittersInPath, ammoBefore);

        // Ammo is consumed in a single batch AT RESOLVE (after damage, combat.cc:5349)
        // — burst deliberately differs from the single-shot eager decrement in TryAttack.
        _pendingBurst = new PendingBurst(dude, target, weaponProto, weaponItem,
            ammoBefore, roundsFired, roundsHit, totalDamage, extras);
        _host.Transcript($"burst {_host.ObjectName(target)}@{target.HexTile}"
            + $" [{_host.ObjectNameByPid(weaponProto.Pid)} {ammoBefore}rnd d{distance}]:"
            + $" chance={accuracy}% rounds={roundsFired} hit={roundsHit} damage={totalDamage}");
        // Collateral is emitted as its own lines (only when present) so a 1-on-1 burst's
        // transcript stays byte-identical to the pre-cone fixtures.
        foreach (BurstExtra ex in extras)
            _host.Transcript($"burst-extra: {_host.ObjectName(ex.Victim)}@{ex.Victim.HexTile}"
                + $" hit={ex.RoundsHit} damage={ex.Damage}");

        _host.OnAttackStarted(dude, target, weaponProto);

        if (_phase == CombatPhase.Idle)
            BeginCombat(target);
        return true;
    }

    /// <summary>Roll a whole burst (combat.cc:3703 _compute_spray). One inception
    /// critical roll (day-gated): a critical FAILURE aborts the burst (no hits), a
    /// critical SUCCESS adds +20 accuracy to every round. Individual rounds never
    /// crit (combat.cc:3654-3657) — each is a plain d100 ≤ accuracy hit. Damage is a
    /// fresh roll per hit round, summed (combat.cc:4589-4615). Returns the bullets
    /// fired (always n — they leave the barrel even on an abort), the rounds that
    /// connected, and the accumulated damage.</summary>
    private (int Accuracy, int RoundsFired, int RoundsHit, int TotalDamage, List<BurstExtra> Extras) RollBurst(
        MapObject dudeObj, MapObject targetObj, CritterState attacker, CritterState defender,
        ProtoInfo weaponProto, MapObject weaponItem, int distance, int crittersInPath, int loadedAmmo)
    {
        int accuracy = Math.Clamp(
            ComputeToHit(attacker, defender, weaponProto, weaponItem, distance, crittersInPath, attackerIsDude: true),
            0, 95);

        int n = Math.Min(loadedAmmo, weaponProto.Weapon!.Rounds);

        // Inception roll = randomRoll(accuracy, critChance) (random.cc:85-131). The
        // d100 is ALWAYS drawn; the crit upgrade/abort only fires from day 2
        // (_host.CriticalsEnabled), so day-1 fixtures take no extra draws and never abort.
        int delta = accuracy - _rng.Next(1, 101);
        if (_host.CriticalsEnabled)
        {
            if (delta < 0)
            {
                if (_rng.Next(1, 101) <= -delta / 10)
                    return (accuracy, n, 0, 0, []); // CRITICAL_FAILURE: burst aborts, bullets still spent
            }
            else if (_rng.Next(1, 101) <= delta / 10 + attacker.Stat(CritterStat.CriticalChance))
            {
                accuracy = Math.Min(accuracy + 20, 95); // CRITICAL_SUCCESS
            }
        }

        // Cone split (combat.cc:3735-3746), exact integer truncation and statement
        // order: leftRounds + rightRounds are taken from centerRounds BEFORE the
        // mainTargetRounds adjustment decrements it.
        int centerRounds = n / 3;
        if (centerRounds == 0)
            centerRounds = 1;
        int leftRounds = n / 3;
        int rightRounds = n - centerRounds - leftRounds;
        int mainTargetRounds = centerRounds / 2;
        if (mainTargetRounds == 0)
        {
            mainTargetRounds = 1;
            centerRounds -= 1;
        }
        // The center line passes through the target; its full budget can hit it
        // (direct mainTargetRounds + the _shoot_along_path remainder).
        int mainTargetExposure = Math.Max(centerRounds, mainTargetRounds);

        AmmoProtoStats? ammo = _host.LoadedAmmo(weaponProto, weaponItem);
        int roundsHit = 0, totalDamage = 0;
        for (int i = 0; i < mainTargetExposure; i++)
        {
            if (_rng.Next(1, 101) <= accuracy) // plain per-round hit (combat.cc:3654)
            {
                roundsHit++;
                totalDamage += RangedMath.RollDamage(_rng,
                    weaponProto.Weapon.MinDamage, weaponProto.Weapon.MaxDamage, defender,
                    ammo?.DrModifier ?? 0, ammo?.DamageMultiplier ?? 1, ammo?.DamageDivisor ?? 1);
            }
        }

        // M2: the collateral cone. The center/left/right lines (combat.cc:3766-3784)
        // spray any OTHER critter standing in the way (the defender's own hits stay the
        // main model above). In a 1-on-1 the lines are empty → no _rng draws → the
        // existing burst fixtures stay byte-identical. The line sweep reuses the
        // Bresenham Trace (the single named LoF divergence); only the end-tiles use the
        // exact TileNumBeyond. Cap 6 extras (combat.cc:3637).
        List<BurstExtra> extras = ConeCollateral(dudeObj, targetObj, attacker, weaponProto,
            weaponItem, ammo, centerRounds - roundsHit, leftRounds, rightRounds, accuracy);

        return (accuracy, n, roundsHit, totalDamage, extras);
    }

    /// <summary>Walk the burst cone's three lines (center/left/right) and roll collateral
    /// hits on every critter other than the main target — combat.cc _compute_spray's
    /// _shoot_along_path passes. Returns the accumulated collateral victims (cap 6).</summary>
    private List<BurstExtra> ConeCollateral(MapObject dudeObj, MapObject targetObj, CritterState attacker,
        ProtoInfo weaponProto, MapObject weaponItem, AmmoProtoStats? ammo,
        int centerBudget, int leftRounds, int rightRounds, int accuracy)
    {
        var extras = new List<BurstExtra>();
        int from = dudeObj.HexTile;
        int range = weaponProto.Weapon!.MaxRange1;

        // Cone pivot + rotation (combat.cc:3769-3776; note the (pivot, attacker) arg order).
        int pivot = HexGrid.Distance(from, targetObj.HexTile) <= 3
            ? HexGrid.TileNumBeyond(from, targetObj.HexTile, 3)
            : targetObj.HexTile;
        int rotation = HexGrid.RotationTo(pivot, from);
        int leftTile = HexGrid.TileInDirection(pivot, (rotation + 1) % 6, 1);
        int rightTile = HexGrid.TileInDirection(pivot, (rotation + 5) % 6, 1);

        ShootCollateral(from, HexGrid.TileNumBeyond(from, targetObj.HexTile, range), centerBudget,
            dudeObj, targetObj, attacker, weaponProto, weaponItem, ammo, accuracy, extras);
        ShootCollateral(from, HexGrid.TileNumBeyond(from, leftTile, range), leftRounds,
            dudeObj, targetObj, attacker, weaponProto, weaponItem, ammo, accuracy, extras);
        ShootCollateral(from, HexGrid.TileNumBeyond(from, rightTile, range), rightRounds,
            dudeObj, targetObj, attacker, weaponProto, weaponItem, ammo, accuracy, extras);
        return extras;
    }

    /// <summary>One cone line: collect the critters along it (Trace walks the Bresenham,
    /// counts critters, resumes past them, stops at a wall), then spend the round budget
    /// hitting each in turn (per-round d100 ≤ its own to-hit). Excludes the shooter and
    /// the main target; accumulates on a repeat victim across lines.</summary>
    private void ShootCollateral(int from, int endTile, int budget,
        MapObject dudeObj, MapObject targetObj, CritterState attacker,
        ProtoInfo weaponProto, MapObject weaponItem, AmmoProtoStats? ammo, int accuracy, List<BurstExtra> extras)
    {
        if (budget <= 0 || extras.Count >= 6 || endTile == from)
            return;

        var line = new List<MapObject>();
        LineOfFire.Trace(from, endTile, tile =>
        {
            MapObject? obj = _host.ShootBlockerAt(tile, dudeObj, targetObj);
            if (obj is not null && Fid.Type(obj.Fid) is ObjectType.Critter
                && obj != targetObj && obj != dudeObj && !line.Contains(obj))
                line.Add(obj);
            return obj; // critters are counted + walked-past; a wall stops the line
        });

        int remaining = budget;
        foreach (MapObject victim in line)
        {
            if (remaining <= 0 || extras.Count >= 6)
                break;
            if (_host.GetCritterState(victim) is not { } vstate)
                continue;

            int dist = HexGrid.Distance(from, victim.HexTile);
            int acc = Math.Clamp(
                ComputeToHit(attacker, vstate, weaponProto, weaponItem, dist, 0, attackerIsDude: dudeObj == _host.Dude),
                0, 95);

            int hits = 0;
            while (remaining > 0 && _rng.Next(1, 101) <= acc)
            {
                remaining--;
                hits++;
            }
            if (hits == 0)
                continue;

            int dmg = 0;
            for (int h = 0; h < hits; h++)
                dmg += RangedMath.RollDamage(_rng, weaponProto.Weapon!.MinDamage, weaponProto.Weapon.MaxDamage,
                    vstate, ammo?.DrModifier ?? 0, ammo?.DamageMultiplier ?? 1, ammo?.DamageDivisor ?? 1);

            int idx = extras.FindIndex(e => e.Victim == victim);
            if (idx >= 0)
                extras[idx] = extras[idx] with { RoundsHit = extras[idx].RoundsHit + hits, Damage = extras[idx].Damage + dmg };
            else
                extras.Add(new BurstExtra(victim, hits, dmg));
        }
    }

    /// <summary>HIT_LOCATION nibble for a THROW attack mode (item.cc _attack_anim).</summary>
    private const int ThrowAnim = 5;

    /// <summary>The equipped weapon can be thrown (primary or secondary attack mode
    /// is THROW), with the throw range (primary→MaxRange1, secondary→MaxRange2).</summary>
    private static bool IsThrowable(ProtoInfo proto, out bool primaryThrow, out int rangeMax, out int apCost)
    {
        primaryThrow = (proto.ExtendedFlags & 0xF) == ThrowAnim;
        bool secondaryThrow = ((proto.ExtendedFlags >> 4) & 0xF) == ThrowAnim;
        var w = proto.Weapon!;
        rangeMax = primaryThrow ? w.MaxRange1 : w.MaxRange2;
        apCost = primaryThrow ? w.ApCost : w.ApCost2;
        return primaryThrow || secondaryThrow;
    }

    /// <summary>Throw the equipped weapon at a tile (item.cc weaponGetRange = min(
    /// maxRange, 3×ST); Throwing skill). Explosives detonate at the landing tile;
    /// other thrown weapons damage the critter there and drop recoverable. The
    /// outcome lands when the throw animation finishes (like a melee swing).</summary>
    public bool TryThrow(int targetTile)
    {
        MapObject? dude = _host.Dude;
        if (dude is null || _pendingAttack is not null || _pendingThrow is not null || _pendingBurst is not null)
            return false;
        if (_host.GetCritterState(dude) is not { } attacker)
            return false;

        (ProtoInfo? weaponProto, MapObject? weaponItem) = _host.EquippedWeapon(dude);
        if (WeaponBlockedByCrippledArms(dude, weaponProto) is { } crippleReason)
        {
            _host.Log($"You can't throw — {crippleReason}.");
            return false;
        }
        if (weaponProto?.Weapon is null || weaponItem is null
            || !IsThrowable(weaponProto, out _, out int rangeMax, out int apCost))
        {
            _host.Log("You have nothing to throw.");
            return false;
        }

        int strength = attacker.Stat(CritterStat.Strength);
        // P29-M4: Heave Ho raises effective Strength for the THROW RANGE only (item.cc:1613), +2/rank,
        // capped at 10 (PRIMARY_STAT_MAX). The to-hit min-ST penalty below still uses the raw ST.
        int throwStrength = Math.Min(10, strength + 2 * _host.DudePerkRank(Perks.PerkId.HeaveHo));
        int range = Math.Min(rangeMax, 3 * throwStrength); // item.cc:1611 weaponGetRange
        int distance = HexGrid.Distance(dude.HexTile, targetTile);
        if (distance > range)
        {
            _host.Log("Too far to throw.");
            return false;
        }

        if (apCost <= 0)
            apCost = 4; // throw default
        switch (_phase)
        {
            case CombatPhase.PlayerTurn when _dudeAp < apCost:
                _host.Log("Not enough action points.");
                return false;
            case CombatPhase.EnemyTurn or CombatPhase.GameOver:
                return false;
            case CombatPhase.Idle:
                ResetDudeAp(attacker);
                break;
        }
        _dudeAp -= apCost;

        MapObject? targetCritter = _host.CombatCritters
            .FirstOrDefault(c => !c.IsDead && c.HexTile == targetTile);
        bool explosive = weaponProto.Weapon.DamageType == 6; // DAMAGE_TYPE_EXPLOSION

        // Ranged-style to-hit with the Throwing skill, then the SAME day-gated critical
        // upgrade as single-shot (combat.cc randomRoll — throws crit too; P13-M3). The
        // hit draw is the identical single d100, so day-1 throws stay byte-identical.
        // Throws are uncalled (torso, penalty 0) and never knock back (projectiles).
        int defenderAc = targetCritter is not null && _host.GetCritterState(targetCritter) is { } ds ? ds.ArmorClass : 0;
        int chance = RangedMath.ToHitChance(attacker.ThrowingSkill, distance,
            attacker.Stat(CritterStat.Perception), attackerIsDude: true,
            defenderAc, 0, weaponProto.Weapon.MinStrength, strength, crittersInPath: 0);

        int delta = chance - _rng.Next(1, 101);
        bool hit = delta >= 0;

        int critMultiplier = 2;
        int critFlags = 0;
        if (hit && targetCritter is not null && _host.CriticalsEnabled
            && _host.GetCritterState(targetCritter) is { } critDef
            && _rng.Next(1, 101) <= delta / 10 + attacker.Stat(CritterStat.CriticalChance))
        {
            int severity = CriticalTables.Severity(_rng.Next(1, 101) + attacker.Stat(CritterStat.BetterCriticals));
            CriticalEffect eff = CriticalTables.Lookup(critDef.Proto.KillType,
                CriticalTables.LocationUncalled, severity, targetCritter == dude);
            critMultiplier = eff.DamageMultiplier;
            critFlags = (MassiveUpgrade(eff, critDef) & CriticalTables.HonoredFlags) | CriticalTables.DamCritical;
        }

        int damage = hit && targetCritter is not null && _host.GetCritterState(targetCritter) is { } td
            ? RangedMath.RollDamage(_rng, weaponProto.Weapon.MinDamage, weaponProto.Weapon.MaxDamage, td,
                0, 1, 1, critMultiplier, (critFlags & CriticalTables.DamBypass) != 0)
            : 0;

        dude.Rotation = HexGrid.RotationTo(dude.HexTile, targetTile);
        _host.RemoveFromHand(dude, weaponItem); // leaves the hand at throw time
        _pendingThrow = new PendingThrow(dude, targetCritter, targetTile, hit, damage, explosive,
            weaponProto.Weapon.MinDamage, weaponProto.Weapon.MaxDamage, weaponProto, weaponItem, critFlags);
        _host.Transcript($"throw {_host.ObjectNameByPid(weaponProto.Pid)} -> @{targetTile}"
            + $": chance={chance}% hit={hit}{(explosive ? " explosive" : $" damage={damage}")}{CritTag(critFlags)}");
        _host.OnThrowStarted(dude, targetTile, weaponProto);

        if (_phase == CombatPhase.Idle && targetCritter is not null)
            BeginCombat(targetCritter);
        return true;
    }

    private void ResolveThrow(PendingThrow t)
    {
        if (t.Explosive)
        {
            // Grenade/molotov: detonate at the landing tile (wires the M3 AoE +,
            // via the misc-10 marker, the metarule(49) door path).
            _host.Log($"The {_host.ObjectNameByPid(t.Proto.Pid)} explodes!");
            _host.SpawnExplosionMarker(t.TargetTile);
            Explode(t.TargetTile, t.Thrower, t.MinDamage, t.MaxDamage, radius: 3);
            return;
        }

        if (t.Hit && t.Target is { IsDead: false })
        {
            bool critical = (t.CritFlags & CriticalTables.DamCritical) != 0;
            t.Target.CurrentHp -= t.Damage;
            bool byDude = t.Thrower == _host.Dude;
            _host.Log((byDude
                ? $"You hit the {_host.ObjectName(t.Target)} for {t.Damage} damage."
                : $"The {_host.ObjectName(t.Thrower)} hits you for {t.Damage} damage.")
                + (critical ? " Critical hit!" : ""));
            // A DEAD critical kills outright regardless of remaining HP (combat.cc DAM_DEAD).
            if (t.Target.CurrentHp <= 0 || (t.CritFlags & CriticalTables.DamDead) != 0)
            {
                if (t.Target == _host.Dude)
                    GameOver();
                else // P26: a thrown weapon's death uses THROW_ANIM (gibs only if explosive)
                    KillCritter(t.Target, t.Thrower, t.Damage, t.Proto.Weapon?.DamageType ?? 0, DeathAnims.ThrowAnim);
            }
            else
            {
                ApplyCritStatus(t.Target, t.CritFlags); // P14
            }
        }
        else
        {
            _host.Log($"The {_host.ObjectNameByPid(t.Proto.Pid)} misses.");
        }

        // Recoverable: the weapon drops on the ground at the landing tile.
        _host.DropThrownWeapon(t.Item, t.TargetTile);
    }

    /// <summary>Damage + magazine decrement when the burst's muzzle-flash animation
    /// finishes. Ammo is consumed in one batch here (after damage, combat.cc:5349),
    /// unlike the single-shot eager decrement. Guns never knock back, so there is no
    /// ApplyKnockback (mirrors ResolveAttack otherwise).</summary>
    private void ResolveBurst(PendingBurst b)
    {
        MapObject? dude = _host.Dude;
        bool byDude = b.Attacker == dude;
        string targetName = _host.ObjectName(b.Target);
        string attackerName = _host.ObjectName(b.Attacker);

        // Single-batch magazine decrement (the bullets left the barrel regardless of hits).
        b.WeaponItem.AmmoQuantity = Math.Max(0, b.AmmoBefore - b.RoundsFired);

        if (b.RoundsHit == 0 || b.TotalDamage <= 0)
        {
            _host.Log(byDude ? $"Your burst misses the {targetName}." : $"The {attackerName}'s burst misses you.");
            return;
        }

        b.Target.CurrentHp -= b.TotalDamage;
        _host.Log(byDude
            ? $"You riddle the {targetName} with {b.RoundsHit} rounds for {b.TotalDamage} damage."
            : $"The {attackerName} riddles you with {b.RoundsHit} rounds for {b.TotalDamage} damage.");

        if (b.Target != dude && b.Target.Sid != -1)
            foreach (string line in _host.RunDamageProc(b.Target, b.Attacker, b.TotalDamage))
                _host.Log(line);

        if (b.Target.CurrentHp <= 0)
        {
            if (b.Target == dude)
                GameOver();
            else // P26: a burst death uses FIRE_BURST (gibs on a solid hit, unlike a single shot)
                KillCritter(b.Target, b.Attacker, b.TotalDamage, b.WeaponProto.Weapon?.DamageType ?? 0, DeathAnims.FireBurst);
        }
        else if (b.Target != dude)
        {
            _host.OnTargetHit(b.Target);
        }

        ApplyBurstExtras(b);
    }

    /// <summary>Apply the collateral cone victims (M2): subtract HP, run the damage proc,
    /// kill or hit-react — the same path as the main target, for each "extra".</summary>
    private void ApplyBurstExtras(PendingBurst b)
    {
        MapObject? dude = _host.Dude;
        foreach (BurstExtra ex in b.Extras)
        {
            if (ex.Damage <= 0 || ex.Victim.IsDead)
                continue;
            ex.Victim.CurrentHp -= ex.Damage;
            _host.Log($"The burst also catches the {_host.ObjectName(ex.Victim)} for {ex.Damage} damage.");

            if (ex.Victim != dude && ex.Victim.Sid != -1)
                foreach (string line in _host.RunDamageProc(ex.Victim, b.Attacker, ex.Damage))
                    _host.Log(line);

            if (ex.Victim.CurrentHp <= 0)
            {
                if (ex.Victim == dude)
                    GameOver();
                else
                    KillCritter(ex.Victim, b.Attacker, ex.Damage, b.WeaponProto.Weapon?.DamageType ?? 0, DeathAnims.FireBurst);
            }
            else if (ex.Victim != dude)
            {
                _host.OnTargetHit(ex.Victim);
            }
        }
    }

    /// <summary>Roll an attack with the equipped weapon (or fists). Guns use the
    /// ranged to-hit (distance/PE, ammo AC mod, min-ST, crowd) and ammo damage
    /// mods; melee keeps the phase-6 path.</summary>
    private (int Chance, bool Hit, int Damage, int CritFlags) RollAttack(
        CritterState attacker, CritterState defender,
        ProtoInfo? weaponProto, MapObject? weaponItem, int distance, int crittersInPath,
        bool attackerIsDude, bool defenderIsDude, int hitLocation)
    {
        bool isGun = weaponProto?.Weapon is { } w && w.IsGun(weaponProto.ExtendedFlags);

        // Aimed-shot location penalty: full for ranged, halved for melee. Lowers
        // the to-hit but (being negative) raises the crit modifier below.
        int locPenalty = CriticalTables.LocationPenalty[Math.Clamp(hitLocation, 0, CriticalTables.LocationCount - 1)];
        if (!isGun)
            locPenalty /= 2;
        int accuracy = Math.Clamp(
            ComputeToHit(attacker, defender, weaponProto, weaponItem, distance, crittersInPath, attackerIsDude) + locPenalty,
            0, 95);

        // randomRoll: delta >= 0 is a hit; on a hit (criticals enabled from day 2)
        // a second d100 <= delta/10 + (critChance - locPenalty) upgrades to a crit.
        int roll = _rng.Next(1, 101);
        int delta = accuracy - roll;
        bool hit = delta >= 0;

        int critMultiplier = 2;
        int critFlags = 0;
        if (hit && _host.CriticalsEnabled)
        {
            int critModifier = attacker.Stat(CritterStat.CriticalChance) - locPenalty;
            bool crit = _rng.Next(1, 101) <= delta / 10 + critModifier;
            // P28-M3: Slayer (every melee/unarmed hit) / Sniper (ranged hit, d10 <= Luck) force a
            // crit for the dude when the normal roll didn't. The short-circuit means a perk-less
            // dude draws no extra RNG — so the combat goldens stay byte-identical.
            if (!crit && attackerIsDude)
                crit = isGun
                    ? _host.DudePerkRank(Perks.PerkId.Sniper) > 0 && _rng.Next(1, 11) <= attacker.Stat(CritterStat.Luck)
                    : _host.DudePerkRank(Perks.PerkId.Slayer) > 0;
            if (crit)
            {
                int severity = CriticalTables.Severity(_rng.Next(1, 101) + attacker.Stat(CritterStat.BetterCriticals));
                CriticalEffect eff = CriticalTables.Lookup(defender.Proto.KillType, hitLocation, severity, defenderIsDude);
                critMultiplier = eff.DamageMultiplier;
                critFlags = (MassiveUpgrade(eff, defender) & CriticalTables.HonoredFlags) | CriticalTables.DamCritical;
            }
        }

        // P30 A-M1: Silent Death backstab (combat.cc:3870-3875 on-hit / 3913-3921 on-crit). A melee/
        // unarmed dude striking from BEHIND while the sneaking FLAG is set, against a target it hasn't
        // engaged yet (WhoHitMeCid != -1 — our proxy for the engine's whoHitMe != gDude, since Hexwaste
        // doesn't track live whoHitMe), deals 4x on a plain hit / doubles the crit multiplier. Dude-only +
        // perk-gated + sneak-flag-gated, drawing NO extra RNG, so a perk-less/non-sneaking dude is inert.
        if (hit && attackerIsDude && !isGun
            && _host.DudePerkRank(Perks.PerkId.SilentDeath) > 0 && _host.DudeSneakFlag
            && defender.Critter.WhoHitMeCid != -1
            && !SneakAttack.IsHitFromFront(attacker.Critter.Rotation, defender.Critter.Rotation))
        {
            critMultiplier = (critFlags & CriticalTables.DamCritical) != 0 ? critMultiplier * 2 : 4;
        }

        int damage = 0;
        if (hit)
        {
            bool bypass = (critFlags & CriticalTables.DamBypass) != 0;
            // P29-M1 Finesse: a dude attacker raises the defender's DR by +30 (combat.cc:4540), but
            // only on the non-bypass path (the engine skips it under DAM_BYPASS). Inert for NPC
            // attackers and a trait-less dude, so the combat goldens stay byte-identical.
            int extraDr = !bypass && attackerIsDude && _host.DudeHasTrait(TraitModifiers.Finesse) ? 30 : 0;
            if (isGun)
            {
                AmmoProtoStats? ammo = weaponItem is null ? null : _host.LoadedAmmo(weaponProto!, weaponItem);
                // P29-M4: Bonus Ranged Damage (+2/rank) is ranged-only and dude-only (combat.cc:4547).
                int rangedBonus = attackerIsDude ? 2 * _host.DudePerkRank(Perks.PerkId.BonusRangedDamage) : 0;
                damage = RangedMath.RollDamage(_rng,
                    weaponProto!.Weapon!.MinDamage, weaponProto.Weapon.MaxDamage, defender,
                    ammo?.DrModifier ?? 0, ammo?.DamageMultiplier ?? 1, ammo?.DamageDivisor ?? 1,
                    critMultiplier, bypass, extraDr, rangedBonus);
            }
            else
            {
                damage = weaponProto?.Weapon is { } weapon
                    ? CombatMath.RollWeaponDamage(_rng, attacker, defender, weapon.MinDamage, weapon.MaxDamage, critMultiplier, bypass, extraDr)
                    : CombatMath.RollDamage(_rng, attacker, defender, critMultiplier, bypass, extraDr);
            }

            // P29-M4 flat post-armor damage perks (combat.cc:4618-4630), dude-only, inert at rank 0.
            if (attackerIsDude)
                damage += DudeFlatDamageBonus(weaponProto, defender);
        }

        return (accuracy, hit, damage, critFlags);
    }

    /// <summary>The crit's flags, plus the "massive critical" flags when the defender
    /// FAILS a stat roll (d10 &gt; stat + mod; combat.cc:4134 statRoll, stat.cc:708) —
    /// this is the source of most KNOCKED_OUT / BLIND / CRIP effects and the one new RNG
    /// draw (P14-M4). It sits inside the crit branch, after the severity roll, so day-1
    /// (no crit) takes no draw; a day-2 crit on a row with a massive stat takes one d10.</summary>
    private int MassiveUpgrade(CriticalEffect eff, CritterState defender)
    {
        if (eff.MassiveStat != -1 && _rng.Next(1, 11) > defender.Stat(eff.MassiveStat) + eff.StatMod)
            return eff.Flags | eff.MassiveFlags;
        return eff.Flags;
    }

    // KILL_TYPE_ROBOT / KILL_TYPE_ALIEN (proto_types.h) — Living Anatomy excludes these.
    private const int KillTypeRobot = 10, KillTypeAlien = 16;

    /// <summary>P29-M4 flat post-armor damage perks for a dude attack (combat.cc:4618-4630), added to
    /// the final damage: Living Anatomy +5 vs a living (non-robot/alien) target, Pyromaniac +5 with a
    /// fire weapon. Returns 0 for a perk-less dude — inert by default. Single-attack path only; burst/
    /// throw flat-bonus is a documented residual (the perks rarely apply on the shippable slice).</summary>
    private int DudeFlatDamageBonus(ProtoInfo? weaponProto, CritterState defender)
    {
        int bonus = 0;
        if (_host.DudePerkRank(Perks.PerkId.LivingAnatomy) > 0
            && defender.Proto.KillType is not (KillTypeRobot or KillTypeAlien))
            bonus += 5;
        if (_host.DudePerkRank(Perks.PerkId.Pyromaniac) > 0
            && weaponProto?.Weapon?.DamageType == 2) // DAMAGE_TYPE_FIRE
            bonus += 5;
        return bonus;
    }

    /// <summary>Transcript suffix marking a critical (and its honoured effects, P14).</summary>
    private static string CritTag(int critFlags)
    {
        if ((critFlags & CriticalTables.DamCritical) == 0)
            return "";
        if ((critFlags & CriticalTables.DamDead) != 0)
            return " CRITICAL(kill)";
        var fx = new List<string>();
        if ((critFlags & CriticalTables.DamKnockedDown) != 0) fx.Add("knockdown");
        if ((critFlags & CriticalTables.DamKnockedOut) != 0) fx.Add("knockout");
        if ((critFlags & CriticalTables.DamLoseTurn) != 0) fx.Add("loseturn");
        if ((critFlags & CriticalTables.DamCripArmAny) != 0) fx.Add("crip-arm");
        if ((critFlags & CriticalTables.DamCripLegAny) != 0) fx.Add("crip-leg");
        if ((critFlags & CriticalTables.DamBlind) != 0) fx.Add("blind");
        return fx.Count > 0 ? $" CRITICAL({string.Join(",", fx)})" : " CRITICAL";
    }

    /// <summary>The to-hit % only (no roll) — for AI min_to_hit decisions and the
    /// RollAttack chance. Guns fall off with distance + crowd; melee is
    /// position-independent (skill − AC).</summary>
    private int ComputeToHit(CritterState attacker, CritterState defender,
        ProtoInfo? weaponProto, MapObject? weaponItem, int distance, int crittersInPath, bool attackerIsDude)
    {
        int toHit;
        if (weaponProto?.Weapon is { } w && w.IsGun(weaponProto.ExtendedFlags))
        {
            AmmoProtoStats? ammo = weaponItem is null ? null : _host.LoadedAmmo(weaponProto, weaponItem);
            // P28-M3: Sharpshooter adds +2 effective Perception per rank to the ranged to-hit
            // (combat.cc:4355) — dude only, 0 ranks = no change.
            int perception = attacker.Perception
                + (attackerIsDude ? 2 * _host.DudePerkRank(Perks.PerkId.Sharpshooter) : 0);
            // P29-M4: Weapon Handling adds +3 effective Strength vs the weapon min-ST to-hit penalty
            // (combat.cc:4414 — minStrengthMod -= 3). Dude only, 0 ranks = no change.
            int effectiveStrength = attacker.Stat(CritterStat.Strength)
                + (attackerIsDude && _host.DudePerkRank(Perks.PerkId.WeaponHandling) > 0 ? 3 : 0);
            toHit = RangedMath.ToHitChance(
                attacker.SmallGunsSkill, distance,
                perception, attackerIsDude,                          // PE-5 when blind (stat.cc:191)
                defender.ArmorClass, ammo?.AcModifier ?? 0,
                w.MinStrength, effectiveStrength, crittersInPath,
                attackerBlind: attacker.Blind);                      // ×12 distance penalty (combat.cc:4383)
        }
        else
        {
            int skill = weaponProto is null ? attacker.UnarmedSkill : attacker.MeleeWeaponsSkill;
            toHit = CombatMath.ToHitChance(skill, defender);
        }

        // P29-M1: One Hander (dude, any wielded weapon) — a two-handed weapon costs −40 to hit,
        // anything one-handed gains +20 (combat.cc:4404). Skipped when unarmed (no weapon) and for
        // NPCs; a trait-less dude is inert, so the combat goldens stay byte-identical.
        if (attackerIsDude && weaponProto is not null && _host.DudeHasTrait(TraitModifiers.OneHander))
            toHit += WeaponProtoStats.IsTwoHanded(weaponProto.ExtendedFlags) ? -40 : 20;

        // A blind attacker: -25 to hit, melee or ranged (combat.cc:4470).
        if (attacker.Blind)
            toHit -= 25;

        // +40 to hit a prone OR knocked-out target (combat.cc:4474).
        if (_knockedDown.Contains(defender.Critter) || IsKnockedOut(defender.Critter))
            toHit = Math.Min(toHit + 40, 95);
        return toHit; // callers clamp to [0,95]
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

        if (_pendingThrow is { } thrown && !_host.IsAnimating(thrown.Thrower))
        {
            _pendingThrow = null;
            ResolveThrow(thrown);
        }

        if (_pendingBurst is { } burst && !_host.IsAnimating(burst.Attacker))
        {
            _pendingBurst = null;
            ResolveBurst(burst);
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

        bool critical = (attack.CritFlags & CriticalTables.DamCritical) != 0;
        attack.Target.CurrentHp -= attack.Damage;
        _host.Log((byDude
            ? $"You hit the {targetName} for {attack.Damage} damage."
            : $"The {attackerName} hits you for {attack.Damage} damage.")
            + (critical ? " Critical hit!" : ""));

        // damage_p_proc runs as damage applies, fixedParam = amount, source =
        // attacker (combat.cc:4850-4851; party-on-party skip is moot here).
        if (attack.Target != dude && attack.Target.Sid != -1)
            foreach (string line in _host.RunDamageProc(attack.Target, attack.Attacker, attack.Damage))
                _host.Log(line);

        // A DEAD critical kills outright regardless of remaining HP (combat.cc DAM_DEAD).
        if (attack.Target.CurrentHp <= 0 || (attack.CritFlags & CriticalTables.DamDead) != 0)
        {
            if (attack.Target == dude)
                GameOver();
            else
                KillCritter(attack.Target, attack.Attacker, attack.Damage, attack.DamageType, attack.AttackerAnim);
            return;
        }

        if (attack.Target != dude)
            _host.OnTargetHit(attack.Target);

        ApplyCritStatus(attack.Target, attack.CritFlags); // P14: knockout / lose-turn / crippled / blind
        ApplyKnockback(attack);
    }

    /// <summary>Knockback shove (melee/unarmed/explosion, never guns) + persisting
    /// prone from a crit. The shove is damage/10 tiles along the hex line, stopping
    /// before a blocked tile (combat.cc:4633 gate + actions.cc:102 geometry); a pure
    /// shove just moves, only a crit DAM_KNOCKED_DOWN leaves the target prone.</summary>
    private void ApplyKnockback(PendingAttack attack)
    {
        MapObject target = attack.Target;
        bool eligible = attack.CanKnockback
            && (target.Flags & OBJECT_MULTIHEX) == 0
            && (_host.GetCritterState(target)?.Proto.CritterFlags & CRITTER_NO_KNOCKBACK) == 0;
        if (!eligible)
            return;

        Shove(attack.Attacker.HexTile, target, attack.Damage / 10);

        // Persisting prone only from a crit (a pure shove bounces back up).
        if ((attack.CritFlags & CriticalTables.DamKnockedDown) != 0 && _knockedDown.Add(target))
        {
            _host.Log($"The {_host.ObjectName(target)} is knocked down!");
            _host.Transcript($"knockdown: {_host.ObjectName(target)}@{target.HexTile}");
        }
    }

    /// <summary>Push a critter away from a source tile, distance tiles (capped at
    /// MAX_KNOCKDOWN_DISTANCE 20), stopping before the first blocked tile.</summary>
    private void Shove(int fromTile, MapObject target, int distance)
    {
        distance = Math.Min(distance, 20);
        if (distance <= 0)
            return;
        int rotation = HexGrid.RotationTo(fromTile, target.HexTile);
        int tile = target.HexTile;
        for (int i = 0; i < distance; i++)
        {
            int next = HexGrid.TileInDirection(tile, rotation);
            if (_host.IsBlocked(next)) // blocked by an occupied tile — stop short
                break;
            tile = next;
        }
        if (tile != target.HexTile)
        {
            int from = target.HexTile;
            _host.PlaceCritter(target, tile);
            _host.Transcript($"knockback: {_host.ObjectName(target)}@{from} -> {tile}");
        }
    }

    /// <summary>An area explosion at <paramref name="centerTile"/> (a thrown grenade
    /// or the misc-10 marker): every critter within radius with clear line-of-sight
    /// takes rand(min,max) − DT_explosion − DR_explosion (stats 23/30), plus
    /// knockback dmg/10 away from the blast. Ported from actions.cc actionExplode /
    /// _compute_explosion_*; the engine's ring-spiral is simplified to radius + LoS,
    /// capped at 6 targets (combat.cc explosionGetMaxTargets).</summary>
    public void Explode(int centerTile, MapObject? killer, int minDamage, int maxDamage, int radius)
    {
        const int maxTargets = 6;
        const int explosionDt = CritterStat.DamageThreshold + 6; // STAT_DAMAGE_THRESHOLD_EXPLOSION
        const int explosionDr = CritterStat.DamageResistance + 6; // STAT_DAMAGE_RESISTANCE_EXPLOSION

        var victims = _host.CombatCritters.Where(c => !c.IsDead).ToList();
        if (_host.Dude is { } dude && !victims.Contains(dude))
            victims.Add(dude);

        int hits = 0;
        foreach (MapObject victim in victims
            .Where(c => HexGrid.Distance(c.HexTile, centerTile) <= radius)
            .OrderBy(c => HexGrid.Distance(c.HexTile, centerTile)))
        {
            if (hits >= maxTargets)
                break;
            // Line-of-sight from the blast centre (walls shield).
            (MapObject? blocker, _) = LineOfFire.Trace(centerTile, victim.HexTile,
                t => _host.ShootBlockerAt(t, victim, victim));
            if (blocker is not null && victim.HexTile != centerTile)
                continue;
            if (_host.GetCritterState(victim) is not { } state)
                continue;

            hits++;
            int raw = _rng.Next(minDamage, maxDamage + 1);
            int damage = Math.Max(raw - state.Stat(explosionDt), 0);
            damage -= state.Stat(explosionDr) * damage / 100;
            if (damage <= 0)
                continue;

            victim.CurrentHp -= damage;
            _host.Log($"The blast hits the {_host.ObjectName(victim)} for {damage} damage.");
            _host.Transcript($"explosion-hit: {_host.ObjectName(victim)}@{victim.HexTile} damage={damage}");

            if ((victim.Flags & OBJECT_MULTIHEX) == 0)
                Shove(centerTile, victim, damage / 10);

            if (victim.CurrentHp <= 0)
            {
                if (victim == _host.Dude)
                    GameOver();
                else // P26: blast deaths are EXPLOSION-typed -> BIG_HOLE/exploded gore
                    KillCritter(victim, killer, damage, 6 /* DAMAGE_TYPE_EXPLOSION */, DeathAnims.FireSingle);
            }
        }

        if (_xpPending > 0 && _phase == CombatPhase.Idle) // out-of-combat blast pays now
        {
            _host.AwardXp(_xpPending);
            _xpPending = 0;
        }
    }

    // ====================================================================
    //  P14 combat-status: knockout / lose-turn (turn-skip + timed wake)
    // ====================================================================

    private static bool IsKnockedOut(MapObject c) => (c.CombatResults & CriticalTables.DamKnockedOut) != 0;

    /// <summary>True if the critter may take its turn (not knocked out, not on a
    /// lose-turn, not dead) — ports critterIsActive (critter.cc:942).</summary>
    private static bool CanAct(MapObject c) =>
        (c.CombatResults & (CriticalTables.DamKnockedOut | CriticalTables.DamLoseTurn | CriticalTables.DamDead)) == 0;

    /// <summary>Knock a critter unconscious + queue its wake (combat.cc:4799-4805) —
    /// public so the crit path, a script external, or a test can drive it.</summary>
    public void KnockOut(MapObject critter)
    {
        if (critter.IsDead || IsKnockedOut(critter))
            return;
        critter.CombatResults |= CriticalTables.DamKnockedOut;
        int en = _host.GetCritterState(critter)?.Stat(CritterStat.Endurance) ?? 5;
        _events.Schedule(_combatTick, 10 * (35 - 3 * en), critter, EventQueue.EventType.Knockout);
        _host.Log($"The {_host.ObjectName(critter)} is knocked out!");
        _host.Transcript($"knockout: {_host.ObjectName(critter)}@{critter.HexTile}");
    }

    /// <summary>Apply a crit's honored status flags to the target (P14-M2/M3): knockout
    /// queues a wake; lose-turn/crippled-limb/blind are recorded on CombatResults
    /// (consumed by the turn loop / CritterState).</summary>
    private void ApplyCritStatus(MapObject target, int critFlags)
    {
        int status = critFlags & StatusFlags;
        if (status == 0 || target.IsDead)
            return;
        target.CombatResults |= status & (CriticalTables.DamLoseTurn | CriticalTables.DamCripLimbs | CriticalTables.DamBlind);
        if ((status & CriticalTables.DamCripLimbs) != 0 || (status & CriticalTables.DamBlind) != 0)
            _host.Transcript($"crippled: {_host.ObjectName(target)}@{target.HexTile} flags=0x{status:X}");
        if ((status & CriticalTables.DamKnockedOut) != 0)
            KnockOut(target);
    }

    /// <summary>Knockout-wake handler (queue.cc → critter.cc:1247 knockoutEventProcess):
    /// clear the KO and leave the critter prone, so it stands (3 AP) at its next turn.</summary>
    private void OnCombatEvent(MapObject owner, EventQueue.EventType type)
    {
        if (type != EventQueue.EventType.Knockout || owner.IsDead)
            return;
        owner.CombatResults &= ~CriticalTables.DamKnockedOut;
        _knockedDown.Add(owner); // wakes prone
        _host.Log($"The {_host.ObjectName(owner)} comes to.");
        _host.Transcript($"wake: {_host.ObjectName(owner)}@{owner.HexTile}");
    }

    /// <summary>True (and consumes a one-shot lose-turn) if the critter must skip its
    /// turn this round (combat.cc:3231 lose-turn / KO skip). KO persists until the wake.</summary>
    private bool SkipTurnIfIncapacitated(MapObject c)
    {
        if (IsKnockedOut(c))
            return true;
        if ((c.CombatResults & CriticalTables.DamLoseTurn) != 0)
        {
            c.CombatResults &= ~CriticalTables.DamLoseTurn; // one-shot
            _host.Transcript($"skip-turn: {_host.ObjectName(c)}@{c.HexTile}");
            return true;
        }
        return false;
    }

    /// <summary>A prone critter stands at its turn (3 AP); returns the AP left, or
    /// -1 if it wasn't prone. Removes the flag.</summary>
    private int StandUpIfProne(MapObject critter, int ap)
    {
        if (!_knockedDown.Remove(critter))
            return -1;
        _host.Transcript($"getup: {_host.ObjectName(critter)} (-{StandUpApCost} AP)");
        return Math.Max(ap - StandUpApCost, 0);
    }

    private void KillCritter(MapObject critter, MapObject? killer = null,
        int damage = 0, int damageType = 0, int attackerAnim = DeathAnims.FallBack)
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
        critter.CombatResults &= ~(CriticalTables.DamKnockedOut | CriticalTables.DamLoseTurn); // dead, not unconscious
        critter.Sid = -1; // the engine removes the script on death (combat.cc:4876)
        _knockedDown.Remove(critter);
        _events.Remove(critter); // no pending wake for the dead (queue.cc:271)
        _host.OnCritterRemoved(critter);
        _host.Log($"The {_host.ObjectName(critter)} dies.");

        // P26 gore: pick the gory death anim from the killing blow (violence fixed at NORMAL —
        // no preferences screen), then let the host fall back if the critter lacks that art.
        int desired = DeathAnims.Pick(damageType, damage, attackerAnim, DeathAnims.ViolenceNormal);
        int deathAnim = _host.PickDeathAnim(critter, desired);
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
        _combatTick = 0;
        _events.ClearAll();
        _hostiles.Clear();
        _enemyQueue.Clear();
        _actingEnemy = null;
        _hostiles.Add(target);
        AddJoiners();
        _host.Log($"Combat begins — round 1, your turn (AP {_dudeAp}).");
    }

    /// <summary>Start a multi-team brawl (phase-16 M3, X-FIGHTING-Y): every supplied
    /// non-dude critter joins as a hostile, and cross-team targeting makes the opposing
    /// groups fight each other as well as the dude. Opens on the dude's turn so the
    /// player can watch the factions thin each other out, or wade in. A NEW entry point
    /// — it does not touch BeginCombat/AddJoiners, so single-team combat is untouched.</summary>
    public void StartBrawl(IEnumerable<MapObject> combatants)
    {
        MapObject? dude = _host.Dude;
        if (dude is null || _gameOver || _phase != CombatPhase.Idle)
            return;
        _host.StopDude();
        _phase = CombatPhase.PlayerTurn;
        _round = 1;
        _combatTick = 0;
        _events.ClearAll();
        _hostiles.Clear();
        _enemyQueue.Clear();
        _actingEnemy = null;
        foreach (MapObject c in combatants)
            if (!c.IsDead && c != dude && c.Team != dude.Team)
            {
                _hostiles.Add(c);
                c.WhoHitMeCid = -1;
            }
        if (_host.GetCritterState(dude) is { } stats)
            ResetDudeAp(stats);
        var teams = _hostiles.Select(h => h.Team).Distinct().OrderBy(t => t).ToList();
        _host.Log($"You stumble into a battle! ({_hostiles.Count} combatants).");
        _host.Transcript($"brawl: combatants={_hostiles.Count} teams=[{string.Join(",", teams)}]");
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
        // Don't hand the turn over while ANY of the dude's actions is still in
        // flight — a burst or a throw blocks the turn-end just like a melee/shot
        // swing (the engine's blocking _combat_turn_run; #9 review).
        if (_phase != CombatPhase.PlayerTurn
            || _pendingAttack is not null || _pendingBurst is not null || _pendingThrow is not null)
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
            _combatTick = 0;
            _events.ClearAll();
            _hostiles.Clear();
            _hostiles.Add(attacker);
            attacker.WhoHitMeCid = -1;
            AddJoiners();
            if (_host.GetCritterState(dude) is { } stats)
                ResetDudeAp(stats);
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
        // Force-wake every combatant so knockout never leaks past the fight
        // (combat.cc:2840 _combat_over → knockoutEventProcess); crippled/blind bits
        // persist on CombatResults (a Doctor clears them).
        _events.ClearAll();
        foreach (MapObject c in _hostiles.Concat(_host.PartyMembers).Append(_host.Dude!).Where(c => c is not null).Distinct())
            c.CombatResults &= ~(CriticalTables.DamKnockedOut | CriticalTables.DamLoseTurn);
        _knockedDown.Clear();

        _phase = CombatPhase.Idle;
        _hostiles.Clear();
        _enemyQueue.Clear();
        _actingEnemy = null;
        if (_host.Dude is { } dude && _host.GetCritterState(dude) is { } stats)
            ResetDudeAp(stats);
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
        // Stall the turn machine while any of the dude's actions resolves — a pending
        // burst/throw must not let the enemy step mid-animation (#9 review).
        if (_pendingAttack is not null || _pendingBurst is not null || _pendingThrow is not null
            || _fallingCritters.Count > 0)
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

        // Disengage hostiles that have fled beyond sight of the whole team — they
        // have escaped, so combat can end (the engine's flee/should-end behaviour;
        // without this, an M1 flee that the dude doesn't chase never resolves).
        PruneEscapedHostiles();

        if (CombatShouldEnd())
        {
            EndCombat();
            return;
        }

        if (_phase == CombatPhase.EnemyTurn)
            StepEnemyTurn();
    }

    /// <summary>Remove living hostiles farther than sight range from every member
    /// of the dude's team (they have disengaged). All hostiles START within sight
    /// (AddJoiners), so this only drops critters that actually fled away.</summary>
    private void PruneEscapedHostiles()
    {
        MapObject? dude = _host.Dude;
        if (dude is null)
            return;
        _hostiles.RemoveWhere(h => !h.IsDead && DistanceToTeam(h, dude) > CombatRules.SightRangeHexes);
    }

    private int DistanceToTeam(MapObject from, MapObject dude)
    {
        int best = HexGrid.Distance(from.HexTile, dude.HexTile);
        foreach (MapObject ally in _host.PartyMembers)
            if (!ally.IsDead)
                best = Math.Min(best, HexGrid.Distance(from.HexTile, ally.HexTile));
        return best;
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

        // Everyone acted: next round. Advance the combat tick and fire any due
        // knockout wakes (P14-M2) before the new round's turns are taken.
        _round++;
        _combatTick += TicksPerRound;
        _events.Process(_combatTick, OnCombatEvent);
        AddJoiners();
        if (_host.Dude is { } dude && _host.GetCritterState(dude) is { } stats)
        {
            // The dude is unconscious / loses this turn: skip straight back to the
            // enemy turn (the wake fires as rounds advance). Avoids a dead player turn.
            if (!CanAct(dude))
            {
                SkipTurnIfIncapacitated(dude);
                _host.Transcript($"dude-skip: round {_round}");
                _phase = CombatPhase.EnemyTurn;
                BuildEnemyQueue();
                return;
            }
            ResetDudeAp(stats);
            if (StandUpIfProne(dude, _dudeAp) is var afterStand && afterStand >= 0)
                _dudeAp = afterStand; // the dude stands at the cost of 3 AP
        }
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

        // Knocked out or losing the turn → forfeit it (combat.cc:3231).
        if (SkipTurnIfIncapacitated(enemy))
            return false;

        // Stand up first if prone (3 AP), then act with what's left.
        if (StandUpIfProne(enemy, _actingEnemyAp) is var stood && stood >= 0)
        {
            _actingEnemyAp = stood;
            if (_actingEnemyAp < 1)
                return false; // standing used the whole turn
        }

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

        // Cross-team targeting (phase-16 M3, X-FIGHTING-Y): a critter also targets the
        // nearest HOSTILE on a DIFFERENT team — so two spawned enemy groups brawl each
        // other, not just the dude. Considered AFTER the dude+party loop and skipping the
        // enemy's own team, so a single-enemy-team fight (every golden) is byte-identical:
        // its only other-team critters are the dude+party, already chosen above.
        foreach (MapObject other in _hostiles)
        {
            if (other.IsDead || other == enemy || other.Team == enemy.Team)
                continue;
            int d = HexGrid.Distance(enemy.HexTile, other.HexTile);
            if (d < bestDistance)
            {
                bestDistance = d;
                defenderObj = other;
            }
        }

        int dudeTile = defenderObj.HexTile;
        AiPacket? ai = _host.GetAiPacket(enemy);

        // min_hp flee (RAW current HP, combat_ai.cc:3077): too wounded to fight.
        if (ai is { MinHp: > 0 } && (_host.GetCritterState(enemy)?.CurrentHp ?? int.MaxValue) < ai.MinHp)
            return TryFlee(enemy, dudeTile);

        // hurt_too_much flee (combat_ai.cc:3076): a crippled/blinded critter whose AI packet lists
        // that damage flag flees. INERT by default — HurtTooMuch defaults 0 and no slice golden enemy
        // carries a crip/blind bit on a turn it takes. (Order vs min_hp is immaterial — both OR into TryFlee.)
        // ported from fallout2-ce src/combat_ai.cc _combat_ai()
        if (ai is { HurtTooMuch: not 0 } && (enemy.CombatResults & ai.HurtTooMuch) != 0)
            return TryFlee(enemy, dudeTile);

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
        int minToHit = ai?.MinToHit ?? 0;
        CritterState? self = _host.GetCritterState(enemy);
        CritterState? def = _host.GetCritterState(defenderObj);

        // Can it EVER clear min_to_hit (best case: point-blank, no crowd)? If not,
        // it can never land a shot — flee rather than flail (combat_ai.cc:2812).
        if (minToHit > 0 && self is not null && def is not null
            && ComputeToHit(self, def, enemyWeapon, enemyWeaponItem, 1, 0, false) < minToHit)
            return TryFlee(enemy, dudeTile);

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
            int toHit = self is not null && def is not null
                ? ComputeToHit(self, def, enemyWeapon, enemyWeaponItem, enemyDistance, enemyCritters, false)
                : 0;
            if (toHit >= minToHit)
            {
                if (_actingEnemyAp < attackCost)
                    return false;
                _actingEnemyAp -= attackCost;
                EnemyAttack(enemy, defenderObj, enemyWeapon, enemyWeaponItem, enemyDistance, enemyCritters);
                return true;
            }
            // In range but accuracy below min_to_hit → close the gap (fall through
            // to the approach, re-evaluated next turn). The slice has no snipers,
            // so closing toward is the right move (combat_ai.cc:2845 simplified).
        }

        if (_actingEnemyAp < 1)
            return false;
        byte[]? path = Pathfinder.FindPath(enemy.HexTile, dudeTile, tile => _host.IsBlocked(tile));
        if (path is null || path.Length <= 1)
            return false;

        // Crippled legs cost 4×/8× AP per hex (critter.cc:1349); 1× otherwise → an
        // intact enemy's budget is unchanged (byte-identical).
        int costPerHex = CritterState.MovePointCost(enemy.CombatResults);
        int steps = Math.Min(path.Length - 1, _actingEnemyAp / costPerHex); // stop adjacent
        _actingEnemyAp -= steps * costPerHex;
        int targetTile = enemy.HexTile;
        for (int i = 0; i < steps; i++)
            targetTile = HexGrid.TileInDirection(targetTile, path[i]);
        return _host.StartWalk(enemy, targetTile);
    }

    /// <summary>Run away from a threat tile: greedily step to the unblocked
    /// neighbour that most increases distance, up to the AP budget (the engine's
    /// _ai_run_away, combat_ai.cc:2812 — our greedy hex-distance approximation).
    /// Returns false (and takes no turn) if hemmed in.</summary>
    private bool TryFlee(MapObject critter, int threatTile)
    {
        if (_actingEnemyAp < 1)
            return false;

        int fromTile = critter.HexTile;
        // ported from fallout2-ce combat_ai.cc _ai_run_away: head directly AWAY from the
        // threat (the rotation from threat→self), or ±1 rotation, as far as AP allows, via
        // a REAL path (_make_path) — not greedy neighbour-stepping that snags on obstacles.
        // Try the full-AP distance first, shrinking until a reachable retreat tile is found.
        int rotation = HexGrid.RotationTo(threatTile, fromTile);
        int target = -1;
        for (int dist = _actingEnemyAp; dist > 0 && target < 0; dist--)
        {
            foreach (int dir in (ReadOnlySpan<int>)[rotation, (rotation + 1) % 6, (rotation + 5) % 6])
            {
                int dest = HexGrid.TileInDirection(fromTile, dir, dist);
                if (dest != fromTile && Pathfinder.FindPath(fromTile, dest, _host.IsBlocked) is not null)
                {
                    target = dest;
                    break;
                }
            }
        }

        if (target < 0)
            return false; // hemmed in — no reachable retreat

        _actingEnemyAp = 0; // the run uses the whole turn (animationRegisterRunToTile, full ap)
        _host.Log($"The {_host.ObjectName(critter)} flees!");
        _host.Transcript($"flee: {_host.ObjectName(critter)}@{fromTile} -> {target}");
        return _host.StartWalk(critter, target);
    }

    /// <summary>A companion's action: punch/shoot the nearest living hostile, else
    /// approach it — the same minimal AI the enemies run.</summary>
    private bool TryAllyAction(MapObject ally)
    {
        if (SkipTurnIfIncapacitated(ally))
            return false;

        if (StandUpIfProne(ally, _actingAllyAp) is var stood && stood >= 0)
        {
            _actingAllyAp = stood;
            if (_actingAllyAp < 1)
                return false;
        }

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
            (int chance, bool hit, int damage, int critFlags) = RollAttack(attacker, defender, weaponProto, weaponItem,
                distance, crittersInPath, attackerIsDude: false, defenderIsDude: false, CriticalTables.LocationUncalled);
            if (isGun && weaponItem is not null)
                weaponItem.AmmoQuantity = _host.WeaponAmmo(weaponProto!, weaponItem) - 1;
            _pendingAttack = new PendingAttack(ally, target, chance, hit, damage, critFlags, CanKnockback: !isGun,
                DamageType: weaponProto?.Weapon?.DamageType ?? 0,
                AttackerAnim: DeathAnims.AttackAnimFor(isGun, weaponProto?.Weapon is not null));
            _host.Transcript($"ally-attack {_host.ObjectName(ally)} -> {_host.ObjectName(target)}@{target.HexTile}"
                + $"{(weaponProto is null ? "" : $" [{_host.ObjectNameByPid(weaponProto.Pid)}]")}: chance={chance}% hit={hit} damage={damage}{CritTag(critFlags)}");
            _host.OnAttackStarted(ally, target, weaponProto);
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
        (int chance, bool hit, int damage, int critFlags) = RollAttack(attacker, defender, weaponProto, weaponItem,
            distance, crittersInPath, attackerIsDude: false, defenderIsDude: defenderObj == _host.Dude,
            CriticalTables.LocationUncalled);
        if (isGun && weaponItem is not null)
            weaponItem.AmmoQuantity = _host.WeaponAmmo(weaponProto!, weaponItem) - 1;
        _pendingAttack = new PendingAttack(enemy, defenderObj, chance, hit, damage, critFlags, CanKnockback: !isGun,
            DamageType: weaponProto?.Weapon?.DamageType ?? 0,
            AttackerAnim: DeathAnims.AttackAnimFor(isGun, weaponProto?.Weapon is not null));
        _host.Transcript($"enemy-attack {_host.ObjectName(enemy)}@{enemy.HexTile}"
            + $"{(weaponProto is null ? "" : $" [{_host.ObjectNameByPid(weaponProto.Pid)}{(isGun ? $" d{distance}" : "")}]")}: chance={chance}% hit={hit} damage={damage}{CritTag(critFlags)}");

        _host.OnAttackStarted(enemy, defenderObj, weaponProto);
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
        _pendingThrow = null;
        _pendingBurst = null;
        _fallingCritters.Clear();
        _knockedDown.Clear();
        _events.ClearAll();
        _combatTick = 0;
        _gameOver = false;
    }
}
