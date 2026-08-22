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
        // P74-M2: a Knockback-perk weapon halves the shove divisor (10→5 = double distance).
        bool KnockbackPerk = false,
        // P26 gore: the killing blow's damage type + attacker animation feed DeathAnims.Pick.
        int DamageType = 0, int AttackerAnim = DeathAnims.FallBack,
        // P114: a MISSED ranged/thrown shot that struck a bystander in the overshoot line (combat.cc:3937).
        AccidentalHit? Accidental = null);
    private PendingAttack? _pendingAttack;

    /// <summary>P114: the bystander a missed single ranged/thrown shot overshoots into (combat.cc:3937-3969).</summary>
    private sealed record AccidentalHit(MapObject Victim, int Damage);

    /// <summary>A thrown weapon in flight: lands when the throw animation finishes —
    /// an explosive detonates (AoE), a spear/rock damages the target and drops
    /// recoverable on the ground.</summary>
    private sealed record PendingThrow(MapObject Thrower, MapObject? Target, int TargetTile,
        bool Hit, int Damage, bool Explosive, int MinDamage, int MaxDamage, ProtoInfo Proto, MapObject Item,
        int CritFlags = 0, AccidentalHit? Accidental = null); // P114: overshoot bystander on a thrown-solid miss
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
    // ported from fallout2-ce src/obj_types.h:99
    private const int CRITTER_INVULNERABLE = 0x400;
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
    // The round's INTERLEAVED turn order (dude + hostiles + allies), ported from combat.cc
    // _combat() iterating the sorted _combat_list: round 1 is attacker/defender/dude-first
    // (_combat_sequence_init), rounds 2+ are sorted by Sequence (Luck tiebreak) via _compare_faster
    // in _combat_sequence. _orderIndex is the current slot; the dude's slot pauses for input.
    private readonly List<MapObject> _order = [];
    private int _orderIndex;
    private MapObject? _actingEnemy;
    private int _actingEnemyAp;
    private MapObject? _actingAlly;
    private int _actingAllyAp;
    // P77: every combatant's CURRENT combat AP, mirroring obj->data.critter.combat.ap. Reset to maxAp
    // at every round start (_combat_set_move_all, combat.cc:3206/3425), then captured as the leftover
    // when each critter's turn ends. READ in M2 as a temporary AC bonus when it is NOT this critter's
    // turn (the remaining-AP dodge, stat.cc:239). A not-yet-acted critter carries full maxAp dodge;
    // an already-acted one its leftover. The dude is captured at EndPlayerTurn.
    private readonly Dictionary<MapObject, int> _currentAp = [];
    /// <summary>ported from fallout2-ce src/combat_ai.cc aiInfoSetLastItem (:2258): the ground item a
    /// critter is walking toward but could not reach this turn, so it resumes next turn instead of
    /// re-deciding. Cleared when the item is retrieved and when combat ends.</summary>
    private readonly Dictionary<MapObject, (ProtoInfo Proto, MapObject Item)> _aiLastItem = [];
    private int _round;
    private int _dudeAp;
    private bool _gameOver;
    /// <summary>P73: a dude-ABSENT NPC-vs-NPC brawl — the dude isn't in the turn order or a target,
    /// every combatant fights cross-team, and the fight ends when one team remains. Default false
    /// (the dude-involved combat/brawl path is untouched → byte-identical).</summary>
    private bool _dudeSpectator;
    private const int MaxSpectatorBrawlRounds = 100; // P73: stalemate/slow-fight bound for a dude-absent brawl
    private const int SnipeRange = 5; // P78-M3: the distance a DISTANCE_SNIPE enemy backs away to reopen

    /// <summary>Kill XP accrued this combat, paid at combat end like the engine's
    /// _combat_exps → _combat_give_exps (combat.cc:2816).</summary>
    private int _xpPending;

    public CombatEngine(ICombatHost host, ICombatRng rng, ICombatRng? calledShotRng = null)
    {
        _host = host;
        _rng = rng;
        _calledShotRng = calledShotRng; // P75-M4: isolated AI called-shot stream; null = no called shots
    }

    /// <summary>P75-M4: the isolated RNG for the AI called-shot decision. Kept OFF the combat to-hit/
    /// damage stream so an NPC rolling its 1/called_freq aim chance (which fires ≈never for the golden
    /// packets, called_freq=10000) doesn't perturb the combat goldens. Null → no AI called shots.</summary>
    private readonly ICombatRng? _calledShotRng;

    /// <summary>Pick an NPC attacker's hit location (AI called shot, AiCalledShot.Pick). Uncalled unless
    /// the isolated roll fires + the at-location to-hit clears the packet's min_to_hit. P75-M4.</summary>
    private int AiHitLocation(MapObject attacker, CritterState attackerState, CritterState defenderState,
        ProtoInfo? weaponProto, MapObject? weaponItem, int distance, int crittersInPath)
    {
        AiPacket? ai = _host.GetAiPacket(attacker);
        if (_calledShotRng is null || ai is null || ai.CalledFreq < 1)
            return CriticalTables.LocationUncalled;
        // A single-shot attack can aim (the engine's critterCanAim is false for burst; this path is
        // never burst). To-hit at a location = base to-hit + the (halved-for-melee) location penalty.
        bool isGun = weaponProto?.Weapon is { } w && w.IsGun(weaponProto.ExtendedFlags);
        int baseToHit = ComputeToHit(attackerState, defenderState, weaponProto, weaponItem, distance, crittersInPath, attackerIsDude: false);
        int ToHitAt(int loc)
        {
            int pen = CriticalTables.LocationPenalty[Math.Clamp(loc, 0, CriticalTables.LocationCount - 1)];
            if (!isGun)
                pen /= 2;
            return Math.Clamp(baseToHit + pen, 0, 95);
        }
        return AiCalledShot.Pick(ai.CalledFreq, attackerState.Stat(CritterStat.Intelligence), canAim: true,
            ai.MinToHit, _calledShotRng, ToHitAt);
    }

    // --- Public surface the viewer/harness drive --------------------------
    public CombatPhase Phase => _phase;
    public int Round => _round;
    public int DudeAp => _dudeAp;
    /// <summary>P74-M4: the dude's free-move pool (Bonus Move) — drained by movement BEFORE AP.</summary>
    public int DudeFreeMove => _dudeFreeMove;
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
    private void ResetDudeAp(CritterState dude)
    {
        _dudeAp = Math.Max(0, dude.MaxActionPoints - _host.DudeEncumbranceApPenalty());
        // P74-M4: refresh the Bonus Move free-move pool (2 AP/rank, combat.cc:3237). Rank 0 → 0, so a
        // perk-less dude has no free move → SpendDudeAp behaves exactly as before → byte-identical.
        _dudeFreeMove = 2 * _host.DudePerkRank(Perks.PerkId.BonusMove);
    }

    private int _dudeFreeMove;

    /// <summary>Charge the dude's turn AP for movement (or any non-attack action), clamped at 0
    /// (phase-18 M0: combat movement costs MovePointCost per hex). P74-M4: the Bonus Move free-move
    /// pool is spent FIRST, then real AP (animation.cc:2610 — the engine drains free move before ap).</summary>
    public void SpendDudeAp(int amount)
    {
        if (amount <= _dudeFreeMove)
        {
            _dudeFreeMove -= amount;
            return;
        }
        amount -= _dudeFreeMove;
        _dudeFreeMove = 0;
        _dudeAp = Math.Max(0, _dudeAp - amount);
    }

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

    /// <summary>The reference gates ammo spending on the weapon's ammo capacity, never on its
    /// attack animation — a Cattle Prod or Power Fist drains Small Energy Cells exactly like a
    /// gun drains its magazine.
    /// ported from fallout2-ce src/combat.cc attackCompute() (:3900-3902) and
    /// _combat_anim_finished() (:5348-5350), both gated on ammoGetCapacity(weapon) > 0.</summary>
    private static bool UsesCharges(ProtoInfo? weaponProto) => (weaponProto?.Weapon?.AmmoCapacity ?? 0) > 0;

    // ====================================================================
    //  Attacks
    // ====================================================================

    /// <summary>P52-M4: the live to-hit % for the dude attacking <paramref name="target"/> at a hit
    /// location — for the called-shot dialog's per-bodypart readout. Mirrors <see cref="RollAttack"/>'s
    /// accuracy (ComputeToHit + the location penalty, clamped 0..95) WITHOUT rolling or any side effect.
    /// Returns null when no attack is possible (no dude/weapon, dead/non-critter target, out of range,
    /// or a blocked line of fire).</summary>
    public int? PreviewToHit(MapObject target, int hitLocation)
    {
        MapObject? dude = _host.Dude;
        if (dude is null || target == dude || Fid.Type(target.Fid) is not ObjectType.Critter || target.IsDead)
            return null;
        if (_host.GetCritterState(dude) is not { } attacker || _host.GetCritterState(target) is not { } defender)
            return null;

        (ProtoInfo? weaponProto, MapObject? weaponItem) = _host.EquippedWeapon(dude);
        bool isGun = weaponProto?.Weapon is { } w && w.IsGun(weaponProto.ExtendedFlags);
        int range = isGun ? weaponProto!.Weapon!.MaxRange1 : Math.Min(weaponProto?.Weapon?.MaxRange1 ?? 1, 2);
        int distance = HexGrid.Distance(dude.HexTile, target.HexTile);
        if (distance > range)
            return null;

        int crittersInPath = 0;
        if (isGun)
        {
            (MapObject? blocker, crittersInPath) = LineOfFire.Trace(
                dude.HexTile, target.HexTile, tile => _host.ShootBlockerAt(tile, dude, target));
            if (blocker is not null)
                return null;
        }

        int locPenalty = CriticalTables.LocationPenalty[Math.Clamp(hitLocation, 0, CriticalTables.LocationCount - 1)];
        if (!isGun)
            locPenalty /= 2;
        return Math.Clamp(
            ComputeToHit(attacker, defender, weaponProto, weaponItem, distance, crittersInPath, attackerIsDude: true) + locPenalty,
            0, 95);
    }

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
        // ported from fallout2-ce src/combat.cc _combat_check_bad_shot() (:5678-5683): the empty-weapon
        // refusal is gated on ammoGetCapacity(weapon) > 0, NOT on weapon class — the same gate
        // CheckBadShot already uses on the NPC side. Hexwaste's dude-side auto-reload here is a
        // pre-existing deviation from _combat_attack_this (:5738-5747) and is left as-is.
        if (UsesCharges(weaponProto))
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
        }

        if (isGun)
        {
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

        (int chance, bool hit, int damage, int critFlags, int delta) = RollAttack(attacker, defender, weaponProto, weaponItem,
            distance, crittersInPath, attackerIsDude: true, defenderIsDude: target == dude, hitLocation, DiffDmgMod(dude));

        // P41: a missed attack can fumble into a critical failure (the full _cf_table — drop/destroy/
        // explode/hurt-self/cripple/random-hit/lose-turn), replacing the P29 lose-turn-only Jinxed stub.
        // The dude's EFFECT is gated to day 6 (the trigger draws from day 2). On lose-turn, end the turn.
        if (!hit && TriggerCritFailure(attacker, attackerIsDude: true, weaponProto, weaponItem, delta))
            _dudeAp = 0;

        // P114: a missed gun shot can overshoot into a bystander (combat.cc:3937). Computed up-front so the
        // damage RNG draw is ordered here; only a critter in the overshoot line draws (else byte-identical).
        AccidentalHit? accidental = null;
        if (!hit && isGun && weaponProto?.Weapon is not null)
            accidental = ComputeAccidentalMiss(dude, target, target.HexTile, weaponProto.Weapon.MaxRange1,
                weaponProto, _host.LoadedAmmo(weaponProto, weaponItem!), DiffDmgMod(dude));

        if (UsesCharges(weaponProto))
            weaponItem!.AmmoQuantity = _host.WeaponAmmo(weaponProto!, weaponItem) - 1;
        _pendingAttack = new PendingAttack(dude, target, chance, hit, damage, critFlags, CanKnockback: !isGun,
            KnockbackPerk: weaponProto?.Weapon is { WeaponPerk: WeaponProtoStats.PerkKnockback }, // P74-M2
            DamageType: weaponProto?.Weapon?.DamageType ?? 0, // P26 gore context
            AttackerAnim: DeathAnims.AttackAnimFor(isGun, weaponProto?.Weapon is not null),
            Accidental: accidental);
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
        (int accuracy, int roundsFired, int roundsHit, int totalDamage, List<BurstExtra> extras, bool loseTurn) =
            RollBurst(dude, target, attacker, defender, weaponProto, weaponItem, distance, crittersInPath, ammoBefore);
        // F26: a burst that critical-fails on its inception roll aborts with no hits; the fumble can also
        // cost the dude the rest of its turn (matches the single-shot pattern at :369-370).
        if (loseTurn)
            _dudeAp = 0;

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
    private (int Accuracy, int RoundsFired, int RoundsHit, int TotalDamage, List<BurstExtra> Extras, bool LoseTurn) RollBurst(
        MapObject dudeObj, MapObject targetObj, CritterState attacker, CritterState defender,
        ProtoInfo weaponProto, MapObject weaponItem, int distance, int crittersInPath, int loadedAmmo,
        bool attackerIsDude = true) // P51: an ally burst (area-attack) passes false
    {
        int accuracy = Math.Clamp(
            ComputeToHit(attacker, defender, weaponProto, weaponItem, distance, crittersInPath, attackerIsDude),
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
                {
                    // CRITICAL_FAILURE: burst aborts, bullets still spent. The detection above is the
                    // existing, already-faithful port of _compute_spray's inception roll (combat.cc:3703,
                    // early-returns at :3718-3719); this call is what was missing — the shared dispatch
                    // every attack shape reaches (combat.cc:3933-3934 case ROLL_CRITICAL_FAILURE) applies
                    // attackComputeCriticalFailure's effects. No second trigger roll: the roll just above
                    // IS the trigger, so this calls the effects-only half directly (see
                    // ApplyCritFailureEffects). Folding it in here — rather than at each of the three call
                    // sites — makes it structurally impossible for a burst to abort without effects.
                    // combat.cc:3713 — *roundsSpentPtr = ammoQuantity is assigned BEFORE the inception
                    // roll, so `n` (rounds spent, not rounds hit — a fumbled burst connects with none)
                    // is exactly the ammoQuantity a DAM_HIT_SELF/DAM_RANDOM_HIT branch rolls damage for
                    // (combat.cc:4229/:4259 ternary, ATTACK_TYPE_RANGED always true for a burst).
                    bool loseTurn = ApplyCritFailureEffects(attacker, attackerIsDude, weaponProto, weaponItem, n);
                    return (accuracy, n, 0, 0, [], loseTurn);
                }
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
        int diffMod = DiffDmgMod(dudeObj); // P84: the shooter's Easy/Hard modifier (100 for dude/ally bursts)
        int roundsHit = 0, totalDamage = 0;
        for (int i = 0; i < mainTargetExposure; i++)
        {
            if (_rng.Next(1, 101) <= accuracy) // plain per-round hit (combat.cc:3654)
            {
                roundsHit++;
                totalDamage += RangedMath.RollDamage(_rng,
                    weaponProto.Weapon.MinDamage, weaponProto.Weapon.MaxDamage, defender,
                    ammo?.DrModifier ?? 0, ammo?.DamageMultiplier ?? 1, ammo?.DamageDivisor ?? 1,
                    difficultyDamageModifier: diffMod);
            }
        }

        // M2: the collateral cone. The center/left/right lines (combat.cc:3766-3784)
        // spray any OTHER critter standing in the way (the defender's own hits stay the
        // main model above). In a 1-on-1 the lines are empty → no _rng draws → the
        // existing burst fixtures stay byte-identical. The line sweep reuses the
        // Bresenham Trace (the single named LoF divergence); only the end-tiles use the
        // exact TileNumBeyond. Cap 6 extras (combat.cc:3637).
        List<BurstExtra> extras = ConeCollateral(dudeObj, targetObj, attacker, weaponProto,
            weaponItem, ammo, centerRounds - roundsHit, leftRounds, rightRounds, accuracy, diffMod);

        return (accuracy, n, roundsHit, totalDamage, extras, false);
    }

    /// <summary>Walk the burst cone's three lines (center/left/right) and roll collateral
    /// hits on every critter other than the main target — combat.cc _compute_spray's
    /// _shoot_along_path passes. Returns the accumulated collateral victims (cap 6).</summary>
    private List<BurstExtra> ConeCollateral(MapObject dudeObj, MapObject targetObj, CritterState attacker,
        ProtoInfo weaponProto, MapObject weaponItem, AmmoProtoStats? ammo,
        int centerBudget, int leftRounds, int rightRounds, int accuracy, int difficultyDamageModifier = 100)
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
            dudeObj, targetObj, attacker, weaponProto, weaponItem, ammo, accuracy, extras, difficultyDamageModifier);
        ShootCollateral(from, HexGrid.TileNumBeyond(from, leftTile, range), leftRounds,
            dudeObj, targetObj, attacker, weaponProto, weaponItem, ammo, accuracy, extras, difficultyDamageModifier);
        ShootCollateral(from, HexGrid.TileNumBeyond(from, rightTile, range), rightRounds,
            dudeObj, targetObj, attacker, weaponProto, weaponItem, ammo, accuracy, extras, difficultyDamageModifier);
        return extras;
    }

    /// <summary>One cone line: collect the critters along it (Trace walks the Bresenham,
    /// counts critters, resumes past them, stops at a wall), then spend the round budget
    /// hitting each in turn (per-round d100 ≤ its own to-hit). Excludes the shooter and
    /// the main target; accumulates on a repeat victim across lines.</summary>
    private void ShootCollateral(int from, int endTile, int budget,
        MapObject dudeObj, MapObject targetObj, CritterState attacker,
        ProtoInfo weaponProto, MapObject weaponItem, AmmoProtoStats? ammo, int accuracy, List<BurstExtra> extras,
        int difficultyDamageModifier = 100)
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
                    vstate, ammo?.DrModifier ?? 0, ammo?.DamageMultiplier ?? 1, ammo?.DamageDivisor ?? 1,
                    difficultyDamageModifier: difficultyDamageModifier);

            int idx = extras.FindIndex(e => e.Victim == victim);
            if (idx >= 0)
                extras[idx] = extras[idx] with { RoundsHit = extras[idx].RoundsHit + hits, Damage = extras[idx].Damage + dmg };
            else
                extras.Add(new BurstExtra(victim, hits, dmg));
        }
    }

    /// <summary>P114: a MISSED single ranged/thrown shot overshoots the target and can strike the first
    /// critter in the line beyond it (combat.cc attackCompute:3937-3969). fo2ce picks the accidental target
    /// DETERMINISTICALLY (no to-hit roll) — the first shoot-blocker on the straight path from the target's
    /// tile to the overshoot endpoint (target excluded), else whatever blocks the endpoint — then rolls one
    /// plain (non-crit) damage packet. Draws RNG ONLY when a living critter is in the way, so a clear
    /// overshoot line is byte-identical. NOT _check_ranged_miss (that is vestigial). Grenade scatter (the
    /// RNG-drawing explosive branch, combat.cc:3941) is deferred.</summary>
    private AccidentalHit? ComputeAccidentalMiss(MapObject attackerObj, MapObject? targetObj, int targetTile,
        int range, ProtoInfo weaponProto, AmmoProtoStats? ammo, int difficultyDamageModifier)
    {
        int endpoint = HexGrid.TileNumBeyond(attackerObj.HexTile, targetTile, range);
        if (endpoint == targetTile)
            return null;

        // Exclude the shooter + the intended target from blocking (ShootBlockerAt takes both).
        MapObject excludeTarget = targetObj ?? attackerObj;
        MapObject? victim = null;
        LineOfFire.Trace(targetTile, endpoint, tile =>
        {
            MapObject? obj = _host.ShootBlockerAt(tile, attackerObj, excludeTarget);
            if (victim is null && obj is not null && tile != targetTile && Fid.Type(obj.Fid) is ObjectType.Critter)
                victim = obj;
            return obj; // a wall stops the line
        });
        victim ??= _host.ShootBlockerAt(endpoint, attackerObj, excludeTarget); // endpoint fallback

        if (victim is null || Fid.Type(victim.Fid) is not ObjectType.Critter || victim.IsDead
            || _host.GetCritterState(victim) is not { } vs)
            return null;

        int dmg = RangedMath.RollDamage(_rng, weaponProto.Weapon!.MinDamage, weaponProto.Weapon.MaxDamage, vs,
            ammo?.DrModifier ?? 0, ammo?.DamageMultiplier ?? 1, ammo?.DamageDivisor ?? 1,
            difficultyDamageModifier: difficultyDamageModifier);
        return new AccidentalHit(victim, dmg);
    }

    /// <summary>Apply a missed shot's accidental bystander hit (mirrors ApplyBurstExtras — HP, kill /
    /// on-hit proc; the dude routes to GameOver). NO damage_p_proc: see below.</summary>
    // ported from fallout2-ce src/combat.cc _damage_object() (:4821) + _check_ranged_miss(): the miss
    // reassigns attack->defender to the bystander while attack->oops keeps the INTENDED target
    // (:3485), so the defender damage call at :4723 passes `defender != oops` = true and the proc gate
    // `if (!a4)` (:4847) skips SCRIPT_PROC_DAMAGE entirely. The collateral victim takes the HP loss and
    // the on-hit path, but never its damage proc (F12, fixed 2026-08-15). The fork's PR #493 inverts a
    // DIFFERENT call site's polarity and does not change this branch's outcome.
    private void ApplyAccidentalHit(AccidentalHit acc, MapObject attacker)
    {
        if (acc.Damage <= 0 || acc.Victim.IsDead)
            return;
        MapObject? dude = _host.Dude;
        acc.Victim.CurrentHp -= acc.Damage;
        _host.Log($"The shot goes wide and hits the {_host.ObjectName(acc.Victim)} for {acc.Damage} damage.");
        if (acc.Victim.CurrentHp <= 0)
        {
            if (acc.Victim == dude)
                GameOver();
            else
                KillCritter(acc.Victim, attacker, acc.Damage, 0, DeathAnims.FallBack);
        }
        else if (acc.Victim != dude)
        {
            _host.OnTargetHit(acc.Victim, attacker, knockedDown: false);
            RunOnHitCombatProc(attacker, acc.Victim);
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

        // P113 (4.2): throws bypass ComputeToHit — apply the same darkness penalty; a ground throw
        // (no target critter) sees light 0 → −40 (combat.cc:4451).
        chance += DarknessToHit(weaponProto, targetCritter is null ? 0 : _host.LightIntensityAt(targetCritter));

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
                0, 1, 1, critMultiplier, (critFlags & CriticalTables.DamBypass) != 0,
                difficultyDamageModifier: DiffDmgMod(dude)) // P84 (dude-only throw → 100, byte-identical)
            : 0;

        // P114: a missed NON-explosive throw overshoots into a bystander (combat.cc:3937). Grenade scatter
        // (the explosive branch) is deferred. Draws damage RNG only when a critter is in the overshoot line.
        AccidentalHit? accidental = !hit && !explosive
            ? ComputeAccidentalMiss(dude, targetCritter, targetTile, range, weaponProto, ammo: null, DiffDmgMod(dude))
            : null;

        dude.Rotation = HexGrid.RotationTo(dude.HexTile, targetTile);
        _host.RemoveFromHand(dude, weaponItem); // leaves the hand at throw time
        _pendingThrow = new PendingThrow(dude, targetCritter, targetTile, hit, damage, explosive,
            weaponProto.Weapon.MinDamage, weaponProto.Weapon.MaxDamage, weaponProto, weaponItem, critFlags, accidental);
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
            // F16: a thrown grenade resolves through the in-attack _compute_explosion_on_extras path
            // (combat.cc:3973-3976), where attack->attacker is the real thrower — attackSourced: true.
            Explode(t.TargetTile, t.Thrower, t.MinDamage, t.MaxDamage, radius: 3, attackSourced: true);
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
                ApplyCritStatus(t.Target, t.CritFlags, t.Thrower); // P14
            }
        }
        else
        {
            _host.Log($"The {_host.ObjectNameByPid(t.Proto.Pid)} misses.");
            if (t.Accidental is { } acc) // P114: the overshoot bystander hit
                ApplyAccidentalHit(acc, t.Thrower);
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

        if (ShouldRunDamageProc(b.Target, b.Attacker))
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
            _host.OnTargetHit(b.Target, b.Attacker, knockedDown: false); // bursts/guns never knock back (combat.cc:4633)
            RunOnHitCombatProc(b.Attacker, b.Target); // P35: fp=2 per struck victim (combat.cc:4754)
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

            if (ShouldRunDamageProc(ex.Victim, b.Attacker))
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
                _host.OnTargetHit(ex.Victim, b.Attacker, knockedDown: false);
                RunOnHitCombatProc(b.Attacker, ex.Victim); // P35: fp=2 per struck victim (combat.cc:4754)
            }
        }
    }

    /// <summary>P84: the combat-difficulty damage modifier (75/100/125) to apply to THIS attacker's
    /// damage. The engine gates it on <c>attacker.team != gDude.team</c> (combat.cc:4554) — i.e. only
    /// attackers NOT on the dude's team are scaled. Hexwaste's dude-team = the dude + party members, so
    /// the dude and allies deal 100% (identity) and only hostiles feel Easy/Hard. A null attacker
    /// (environmental blast) is treated as off-team but Normal still returns 100 → byte-identical.</summary>
    private int DiffDmgMod(MapObject? attacker) =>
        attacker == _host.Dude || (attacker is not null && _host.PartyMembers.Contains(attacker))
            ? 100
            : _host.CombatDifficultyDamageModifier;

    /// <summary>Roll an attack with the equipped weapon (or fists). Guns use the
    /// ranged to-hit (distance/PE, ammo AC mod, min-ST, crowd) and ammo damage
    /// mods; melee keeps the phase-6 path.</summary>
    private (int Chance, bool Hit, int Damage, int CritFlags, int Delta) RollAttack(
        CritterState attacker, CritterState defender,
        ProtoInfo? weaponProto, MapObject? weaponItem, int distance, int crittersInPath,
        bool attackerIsDude, bool defenderIsDude, int hitLocation, int difficultyDamageModifier = 100)
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

        // P114: Enhanced Knockout weapon perk (combat.cc:3798 attackComputeEnhancedKnockout, called :3925;
        // + the unconditional forced-KO on a crit, :4146). On a perk-117 hit: a crit forces KO; a normal hit
        // rolls d100 and KOs if <= STRENGTH-8. Draws exactly ONE d100 per perk hit, ZERO for other weapons
        // (WeaponPerk -1) — inert in vanilla (no shipped weapon carries perk 117), faithful for a modded one.
        if (hit && weaponProto?.Weapon is { WeaponPerk: WeaponProtoStats.PerkEnhancedKnockout })
        {
            int koRoll = _rng.Next(1, 101); // drawn on EVERY perk hit (combat.cc:3802)
            if ((critFlags & CriticalTables.DamCritical) != 0
                || koRoll <= attacker.Stat(CritterStat.Strength) - 8)
                critFlags |= CriticalTables.DamKnockedOut;
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
            // P74-M2: the Penetrate weapon perk cuts the defender's DT to 20% (DT only). Any attacker.
            bool penetrate = weaponProto?.Weapon is { WeaponPerk: WeaponProtoStats.PerkPenetrate };
            if (isGun)
            {
                AmmoProtoStats? ammo = weaponItem is null ? null : _host.LoadedAmmo(weaponProto!, weaponItem);
                // P29-M4: Bonus Ranged Damage (+2/rank) is ranged-only and dude-only (combat.cc:4547).
                int rangedBonus = attackerIsDude ? 2 * _host.DudePerkRank(Perks.PerkId.BonusRangedDamage) : 0;
                damage = RangedMath.RollDamage(_rng,
                    weaponProto!.Weapon!.MinDamage, weaponProto.Weapon.MaxDamage, defender,
                    ammo?.DrModifier ?? 0, ammo?.DamageMultiplier ?? 1, ammo?.DamageDivisor ?? 1,
                    critMultiplier, bypass, extraDr, rangedBonus, penetrate, difficultyDamageModifier);
            }
            else
            {
                damage = weaponProto?.Weapon is { } weapon
                    ? CombatMath.RollWeaponDamage(_rng, attacker, defender, weapon.MinDamage, weapon.MaxDamage, critMultiplier, bypass, extraDr, penetrate, difficultyDamageModifier)
                    : CombatMath.RollDamage(_rng, attacker, defender, critMultiplier, bypass, extraDr, penetrate: false, difficultyDamageModifier);
            }

            // P29-M4 flat post-armor damage perks (combat.cc:4618-4630), dude-only, inert at rank 0.
            if (attackerIsDude)
                damage += DudeFlatDamageBonus(weaponProto, defender);
        }

        return (accuracy, hit, damage, critFlags, delta);
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

    /// <summary>
    /// Critical FAILURE on a MISS (P41) — random.cc randomTranslateRoll (the trigger) + combat.cc:4178
    /// attackComputeCriticalFailure (the effects). Call AFTER RollAttack on a miss; returns true if the
    /// fumble costs the attacker its turn (the caller zeroes the right AP pool). RNG ORDERING mirrors the
    /// engine: the day≥1 natural-upgrade draw fires immediately after the (miss) hit-roll, then the Jinxed
    /// force, then severity + per-effect draws — so a day-1, non-Jinxed attacker draws NOTHING (goldens
    /// byte-identical). The DUDE's effect is suppressed before day 6 (the trigger still drew). Effects are
    /// applied to the ATTACKER immediately (the miss has no deferred swing-damage).
    /// </summary>
    private bool TriggerCritFailure(CritterState attacker, bool attackerIsDude,
        ProtoInfo? weaponProto, MapObject? weaponItem, int delta)
    {
        bool critFail = false;
        if (_host.CriticalsEnabled)                                  // random.cc:113 — day≥1 natural upgrade
            critFail = _rng.Next(1, 101) <= -delta / 10;
        if (!critFail && _host.DudeHasTrait(TraitModifiers.Jinxed))  // combat.cc:3857 — Jinxed force, no day gate
            critFail = _rng.Next(0, 2) == 1;
        if (!critFail)
            return false;

        return ApplyCritFailureEffects(attacker, attackerIsDude, weaponProto, weaponItem);
    }

    /// <summary>The critical-failure EFFECTS (combat.cc:4178 attackComputeCriticalFailure), split out of
    /// <see cref="TriggerCritFailure"/> so a caller that already knows the roll landed on a fumble — without
    /// having to re-draw the trigger — can apply them directly. Used by TriggerCritFailure itself (single
    /// shot / melee / thrown misses, whose OWN day-gated + Jinxed roll decides the trigger above), and by
    /// <see cref="RollBurst"/> (combat.cc:3703-3720 _compute_spray's inception roll IS the trigger — its
    /// ROLL_CRITICAL_FAILURE return dispatches straight into attackComputeCriticalFailure at the shared
    /// switch, combat.cc:3933-3934, with no second roll). Do not call this without first confirming the
    /// fumble landed; it does not re-check.</summary>
    /// <param name="roundCount">Rounds spent this attack (combat.cc:3713 `*roundsSpentPtr`), used by the
    /// DAM_HIT_SELF/DAM_RANDOM_HIT damage rolls below (combat.cc:4229/:4259 `ammoQuantity`). Defaults to 1
    /// so single-shot/melee/thrown callers — none of which know a burst's round count — are unchanged.</param>
    private bool ApplyCritFailureEffects(CritterState attacker, bool attackerIsDude,
        ProtoInfo? weaponProto, MapObject? weaponItem, int roundCount = 1)
    {
        // ported from fallout2-ce src/combat.cc attackComputeCriticalFailure() :4182-4184: an invulnerable
        // attacker is exempt outright — checked BEFORE the dude's day-6 gate (:4186) and before any
        // _cf_table lookup, so it draws no severity roll at all (unlike the day<6 dude case below, which
        // still draws the trigger and is only gated on its EFFECT). Must stay first for that reason.
        if ((attacker.Proto.CritterFlags & CRITTER_INVULNERABLE) != 0)
            return false;

        // combat.cc:4190 — the dude's fumble has no EFFECT before day 6 (the trigger above still drew).
        if (attackerIsDude && !_host.DudeCritFailuresEnabled)
            return false;

        MapObject self = attacker.Critter;
        int failureType = weaponProto?.Weapon?.CriticalFailureType ?? 0;
        int flags = CriticalFailure.Resolve(failureType, attacker.Stat(CritterStat.Luck), _rng);
        // _attackFindInvalidFlags (combat.cc:4225): an unarmed attacker can't drop/destroy/lose a weapon.
        if (weaponItem is null)
            flags &= ~(CriticalTables.DamDrop | CriticalTables.DamDestroy | CriticalTables.DamLoseAmmo);
        if (flags == 0)
            return false;

        _host.Log($"The {_host.ObjectName(self)} fumbles!");
        _host.Transcript($"crit-fail: {_host.ObjectName(self)}@{self.HexTile} flags=0x{flags:X}");

        if ((flags & CriticalTables.DamCripRandom) != 0) // _do_random_cripple → one random limb bit
        {
            int[] limbs = { CriticalTables.DamCripLegLeft, CriticalTables.DamCripLegRight,
                CriticalTables.DamCripArmLeft, CriticalTables.DamCripArmRight };
            self.CombatResults |= limbs[_rng.Next(0, 4)];
            _host.Transcript($"crippled: {_host.ObjectName(self)}@{self.HexTile} flags=0x{self.CombatResults & CriticalTables.DamCripLimbs:X}");
        }
        if ((flags & CriticalTables.DamKnockedDown) != 0 && _knockedDown.Add(self))
            _host.Transcript($"knockdown: {_host.ObjectName(self)}@{self.HexTile}");

        // Self-damage, in the reference's shape (combat.cc:4228-4232 at our pinned e97087b — the fork adds
        // the HURT_SELF branch straight after it, community/main combat.cc:4343-4345): HIT_SELF takes the weapon's rolled
        // damage as a direct HP hit (no on-hit hooks / ammo mods, not a re-attack); else EXPLODE detonates
        // the fumbling weapon at the attacker's tile (its own damage as the blast, radius 1 — a documented
        // simplification). HURT_SELF is a SEPARATE, much milder branch: a flat 1-5, with no damage roll at
        // all. _cf_table never pairs HURT_SELF with HIT_SELF, so the two never stack.
        if ((flags & CriticalTables.DamHitSelf) != 0)
            CritFailDamage(attacker, attacker, weaponProto, "crit-fail-self", roundCount);
        else if ((flags & CriticalTables.DamExplode) != 0)
            // F16: the crit-fail explode also resolves through _compute_explosion_on_extras
            // (combat.cc:3976, isFromAttacker=1) — attackSourced: true so the OTHER victims of the
            // fumbler's own blast also run their damage_p_proc (source = the fumbler), not just the
            // fumbler itself (selfDamageProcFor, F13).
            Explode(self.HexTile, self, weaponProto?.Weapon?.MinDamage ?? 1, weaponProto?.Weapon?.MaxDamage ?? 6, 1,
                selfDamageProcFor: self, attackSourced: true);

        if ((flags & CriticalTables.DamHurtSelf) != 0)
            ApplyCritFailDamage(attacker, attacker, _rng.Next(1, 6), weaponProto, "crit-fail-self");

        if ((flags & CriticalTables.DamDrop) != 0 && weaponItem is not null)
        {
            _host.RemoveFromHand(self, weaponItem);
            _host.DropThrownWeapon(weaponItem, self.HexTile); // spills to the ground, recoverable
        }
        else if ((flags & CriticalTables.DamDestroy) != 0 && weaponItem is not null)
            _host.RemoveFromHand(self, weaponItem);          // destroyed — gone, not dropped
        else if ((flags & CriticalTables.DamLoseAmmo) != 0 && weaponItem is not null)
            weaponItem.AmmoQuantity = 0;                      // the magazine spills

        // DAM_RANDOM_HIT: the wild shot strikes a random nearby living critter (can catch a companion) —
        // a documented direct-damage approximation, not a full re-attack (combat.cc _combat_ai_random_target).
        if ((flags & CriticalTables.DamRandomHit) != 0)
        {
            MapObject? victim = RandomNearbyCritter(self);
            if (victim is not null && _host.GetCritterState(victim) is { } vd)
                CritFailDamage(attacker, vd, weaponProto, "crit-fail-random-hit", roundCount);
        }

        // DAM_DUD / DAM_ON_FIRE are cosmetic on this slice (no jam-state / fire model) — documented.
        return (flags & CriticalTables.DamLoseTurn) != 0;
    }

    /// <summary>Direct crit-failure damage to a victim (DAM_HIT_SELF or the wild RANDOM_HIT): the weapon's
    /// rolled damage (no ammo mods — a documented simplification), applied straight to HP with a kill
    /// check.</summary>
    // ported from fallout2-ce src/combat.cc attackComputeCriticalFailure() (community fix #675).
    // The reference rolls weapon damage (attackComputeDamage) ONLY for DAM_HIT_SELF and DAM_EXPLODE;
    // DAM_HURT_SELF is a separate branch that just adds randomBetween(1, 5) to attackerDamage — which
    // starts at 0 — so a HURT_SELF fumble is worth exactly 1-5 and takes no damage roll. This method is
    // the HIT_SELF/RANDOM_HIT half; the HURT_SELF half calls ApplyCritFailDamage directly with the 1-5.
    // ported from fallout2-ce src/combat.cc attackComputeDamage(): the reference passes
    // bonusDamageMultiplier = 2 (combat.cc:4230 for HIT_SELF, :4260 for RANDOM_HIT), which multiplies at
    // :4586 and is undone by the `damage /= 2` at :4601 — a net x1, i.e. the FULL rolled figure. Our
    // critMultiplier feeds the same `raw * critMultiplier / 2` shape, so 2 is what reproduces vanilla;
    // passing 1 halved every crit-failure hit (F11, fixed 2026-08-15). Confirmed byte-identical
    // against tests/golden-combat: no committed fixture's fumble sets DAM_HIT_SELF/DAM_RANDOM_HIT
    // (the one crit-failure fixture, arcaves-crit-fail-day6, fumbles to flags=0x8000, LOSE_TURN
    // only), so this branch has zero golden-fixture blast radius today — proven only by the two
    // mutation-verified unit tests below.
    // F15: for a RANGED fumble the reference rolls attack->ammoQuantity times (combat.cc:4229/:4589) —
    // a burst self-hits once per round SPENT (combat.cc:3713 assigns *roundsSpentPtr before the
    // inception roll, so this holds even though the aborted burst connects with nothing).
    // <paramref name="roundCount"/> defaults to 1 so single-shot/melee callers are unchanged by
    // construction (melee is doubly inert: the reference's own ternary collapses ammoQuantity to 1 off
    // ATTACK_TYPE_RANGED). RollBurst passes its rounds-spent count through ApplyCritFailureEffects.
    private void CritFailDamage(CritterState attacker, CritterState victimState, ProtoInfo? weaponProto,
        string tag, int roundCount = 1)
    {
        int dmg = 0;
        for (int i = 0; i < roundCount; i++)
        {
            dmg += weaponProto?.Weapon is { } w
                ? CombatMath.RollWeaponDamage(_rng, attacker, victimState, w.MinDamage, w.MaxDamage, 2, false, 0)
                : CombatMath.RollDamage(_rng, attacker, victimState, 2, false, 0);
        }
        ApplyCritFailDamage(attacker, victimState, dmg, weaponProto, tag);
    }

    /// <summary>Apply an already-computed crit-failure damage figure to a victim: HP, log, transcript and
    /// the kill check. A self-kill / companion-kill via the attacker; a dude victim → game over.</summary>
    private void ApplyCritFailDamage(CritterState attacker, CritterState victimState, int dmg,
        ProtoInfo? weaponProto, string tag)
    {
        MapObject victim = victimState.Critter;
        victim.CurrentHp -= dmg;
        _host.Log($"The {_host.ObjectName(victim)} takes {dmg} damage.");
        _host.Transcript($"{tag}: {_host.ObjectName(victim)}@{victim.HexTile} damage={dmg}");

        // ported from fallout2-ce src/combat.cc _apply_damage() (community fix #493): the attacker's
        // self-damage _damage_object call passes the "hit an UNINTENDED target" flag (the pre-image at
        // e97087b passes its inverse and carries the author's own `// TODO: Not sure about
        // "attack->defender == attack->oops"` on that very expression). With the corrected polarity the
        // ordinary fumble — defender == the intended target — DOES run the self-damaged attacker's
        // damage_p_proc, with itself as both damaged object and source. _damage_object still skips the
        // proc when target and source are both party members, which for self-damage means EVERY party
        // member (the dude included, gPartyMembers[0]) is skipped: only an unaffiliated critter runs it.
        // The DAM_RANDOM_HIT victim is NOT this call site — it becomes attack->defender with oops left
        // at the original target, so its flag is true and it takes no damage_p_proc either way.
        // One deliberate difference on this branch: the reference's self-damage _damage_object (:4683) is
        // NOT preceded by a scriptSetObjects — unlike the defender (:4722) and extras (:4749) call sites —
        // so source_obj keeps whatever the previous call left there and a script reading it here reads a
        // stale handle. We pass
        // the victim itself — a well-defined choice, since "the attacker damaged itself" is what happened.
        if (victim == attacker.Critter && dmg > 0 && ShouldRunDamageProc(victim, victim))
            foreach (string line in _host.RunDamageProc(victim, victim, dmg))
                _host.Log(line);

        if (victim.CurrentHp > 0)
            return;
        if (victim == _host.Dude)
            _host.GameOver();
        else
            KillCritter(victim, attacker.Critter, dmg, weaponProto?.Weapon?.DamageType ?? 0);
    }

    /// <summary>A random LIVING combatant (incl. the dude) within 5 tiles of <paramref name="self"/>,
    /// or null. Used for the crit-failure DAM_RANDOM_HIT wild shot.</summary>
    private MapObject? RandomNearbyCritter(MapObject self)
    {
        var pool = new List<MapObject>();
        if (_host.Dude is { IsDead: false } dude && dude != self && HexGrid.Distance(self.HexTile, dude.HexTile) <= 5)
            pool.Add(dude);
        foreach (MapObject c in _host.CombatCritters)
            if (c != self && !c.IsDead && HexGrid.Distance(self.HexTile, c.HexTile) <= 5)
                pool.Add(c);
        return pool.Count == 0 ? null : pool[_rng.Next(0, pool.Count)];
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
    /// <summary>The combatant whose turn it currently is (the engine's _combat_whose_turn()): the acting
    /// enemy/ally mid-turn, the dude during PlayerTurn, else the order's current slot; null out of combat.</summary>
    private MapObject? CurrentActor()
    {
        if (_phase == CombatPhase.Idle)
            return null;
        if (_actingEnemy is { } e) return e;
        if (_actingAlly is { } a) return a;
        if (_phase == CombatPhase.PlayerTurn) return _host.Dude;
        return _orderIndex >= 0 && _orderIndex < _order.Count ? _order[_orderIndex] : null;
    }

    /// <summary>The remaining-AP dodge AC bonus (stat.cc:215-242): during combat, a critter whose turn it
    /// is NOT gains its current combat AP as temporary AC. ×1 here; M3 folds in the dude's HtH-Evade ×2 +
    /// Unarmed/12. 0 out of combat, on the actor's own turn, or for a critter with no stored AP.</summary>
    private int ApDodgeAc(MapObject? defender)
    {
        if (defender is null || _phase == CombatPhase.Idle || ReferenceEquals(CurrentActor(), defender))
            return 0;
        int ap = _currentAp.GetValueOrDefault(defender, 0);
        int multiplier = 1;
        int hthEvadeBonus = 0;
        if (ReferenceEquals(defender, _host.Dude) && _host.DudePerkRank(Perks.PerkId.HthEvade) > 0 && DudeUnarmed())
        {
            // stat.cc:233 — an unarmed dude with HtH Evade doubles the AP→AC and adds Unarmed/12.
            multiplier = 2;
            hthEvadeBonus = (_host.GetCritterState(defender)?.UnarmedSkill ?? 0) / 12;
        }
        return ap * multiplier + hthEvadeBonus;
    }

    /// <summary>The dude wields no weapon (stat.cc:208 critterGetItem2/1 == no WEAPON). Hexwaste equips
    /// ONE weapon, so "unarmed" = no equipped weapon proto.</summary>
    private bool DudeUnarmed() => _host.Dude is { } d && _host.EquippedWeapon(d).Proto is null;

    /// <summary>P77: the critter's current remaining-AP dodge AC bonus (the value M2 folds into to-hit),
    /// for the HUD/Pip-Boy combat-AC display + the --ac-dodge-probe. 0 out of combat / on its own turn.</summary>
    public int RemainingApDodge(MapObject critter) => ApDodgeAc(critter);

    private int ComputeToHit(CritterState attacker, CritterState defender,
        ProtoInfo? weaponProto, MapObject? weaponItem, int distance, int crittersInPath, bool attackerIsDude)
    {
        // P77: the defender's remaining-AP dodge folds into its AC (before the ammo modifier + 0-clamp,
        // exactly like statGetValue adds it into STAT_ARMOR_CLASS that combat.cc:4428 then reads).
        int apDodge = ApDodgeAc(defender.Critter);
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
            // P113 (item 3C): LONG_RANGE/SCOPE_RANGE weapon perks change the PE range term
            // (combat.cc:4359-4372) — a weapon property, NOT dude-gated. Perk-less = (2,0), identical.
            (int rangeMult, int minRange) = w.WeaponPerk switch
            {
                WeaponProtoStats.PerkLongRange => (4, 0),
                WeaponProtoStats.PerkScopeRange => (5, 8),
                _ => (2, 0),
            };
            toHit = RangedMath.ToHitChance(
                attacker.SmallGunsSkill, distance,
                perception, attackerIsDude,                          // PE-5 when blind (stat.cc:191)
                defender.ArmorClass + apDodge, ammo?.AcModifier ?? 0,
                w.MinStrength, effectiveStrength, crittersInPath,
                attackerBlind: attacker.Blind,                       // ×12 distance penalty (combat.cc:4383)
                perkRangeMult: rangeMult, perkMinRange: minRange);
        }
        else
        {
            int skill = weaponProto is null ? attacker.UnarmedSkill : attacker.MeleeWeaponsSkill;
            toHit = CombatMath.ToHitChance(skill, defender, apDodge);
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

        // +15 to hit a MULTIHEX defender — a big target (e.g. a Large Radscorpion) is easier to hit
        // (combat.cc:4443). Requires BuildSpawn to propagate the proto's OBJECT_MULTIHEX onto the spawn.
        if ((defender.Critter.Flags & OBJECT_MULTIHEX) != 0)
            toHit += 15;

        // P113 (4.2): darkness to-hit penalty (combat.cc:4446-4463) — DUDE only, all attack types, keyed
        // on the DEFENDER's tile light (0..65536). A Night Sight-perk weapon treats it as full bright.
        if (attackerIsDude)
            toHit += DarknessToHit(weaponProto, _host.LightIntensityAt(defender.Critter));

        // P74-M2: the Accurate weapon perk adds +20 to hit, for ANY attacker (combat.cc:4423 — no dude
        // gate, it's a weapon property). Inert for a perk-less weapon (WeaponPerk -1) → byte-identical.
        if (weaponProto?.Weapon is { WeaponPerk: WeaponProtoStats.PerkAccurate })
            toHit += 20;
        return toHit; // callers clamp to [0,95]
    }

    /// <summary>P113 (4.2): the darkness to-hit modifier (combat.cc:4448-4463). Night Sight-perk weapons
    /// see full brightness; otherwise the defender's tile light picks a penalty band. lightIntensity is
    /// 0..65536 (a null/ground target passes 0 → −40).</summary>
    private static int DarknessToHit(ProtoInfo? weaponProto, int lightIntensity)
    {
        int li = weaponProto?.Weapon is { WeaponPerk: WeaponProtoStats.PerkNightSight } ? 65536 : lightIntensity;
        return li <= 26214 ? -40 : li <= 39321 ? -25 : li <= 52428 ? -10 : 0;
    }

    /// <summary>Damage-on-completion + corpse conversion, polled every frame
    /// (the engine's _combat_anim_finished callback chain).</summary>
    public void ProcessAnimations()
    {
        if (_pendingAttack is { } attack && !_host.IsAnimating(attack.Attacker))
        {
            _pendingAttack = null;
            // Task-2 port: aiInfoSetLastTarget(attacker, defender) (combat.cc:3558) — stamped
            // unconditionally (hit or miss), same as the reference.
            attack.Attacker.LastAttackTarget = attack.Target;
            ResolveAttack(attack);
        }

        if (_pendingThrow is { } thrown && !_host.IsAnimating(thrown.Thrower))
        {
            _pendingThrow = null;
            if (thrown.Target is not null)
                thrown.Thrower.LastAttackTarget = thrown.Target;
            ResolveThrow(thrown);
        }

        if (_pendingBurst is { } burst && !_host.IsAnimating(burst.Attacker))
        {
            _pendingBurst = null;
            burst.Attacker.LastAttackTarget = burst.Target;
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
            // Dodge reaction (actions.cc:906) — only a non-prone, non-KO'd defender dodges (P34-M6).
            if (attack.Target != dude && !_knockedDown.Contains(attack.Target) && !IsKnockedOut(attack.Target))
                _host.OnTargetDodge(attack.Target);
            if (attack.Accidental is { } acc) // P114: the overshoot bystander hit
                ApplyAccidentalHit(acc, attack.Attacker);
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
        if (ShouldRunDamageProc(attack.Target, attack.Attacker))
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
            // P34-M6: pass the attacker (for facing) + whether THIS blow knocks the target down (a FALL
            // instead of a hit-react). DamKnockedDown is read before ApplyKnockback consumes it below.
            _host.OnTargetHit(attack.Target, attack.Attacker, (attack.CritFlags & CriticalTables.DamKnockedDown) != 0);

        ApplyCritStatus(attack.Target, attack.CritFlags, attack.Attacker); // P14: knockout / lose-turn / crippled / blind
        ApplyKnockback(attack);
        RunOnHitCombatProc(attack.Attacker, attack.Target); // P35: fp=2 on-hit hook (e.g. scorpion poison)
        RegisterHit(attack.Target, attack.Attacker); // P101 (bucket 3): the struck critter remembers its attacker
    }

    /// <summary>
    /// The on-hit combat_p_proc hook, ported from fallout2-ce src/combat.cc:4729-4733: after a landed
    /// hit (defenderDamage >= 0 && DAM_HIT) the ATTACKER's combat_p_proc runs with fixedParam=2 and
    /// target = the struck defender (e.g. a scorpion poisons whom it stung). DIVERGENCE: a lethal hit
    /// returns early in Hexwaste (KillCritter), so the hook fires only on a non-lethal hit — moot for
    /// poison (a kill needs no poison). The dude attacker is a no-op (no gcd combat_p_proc / Sid -1).
    /// </summary>
    private void RunOnHitCombatProc(MapObject attacker, MapObject defender)
    {
        if (attacker.Sid == -1)
            return;
        foreach (string line in _host.RunCombatProc(attacker, 2, defender).Lines)
            _host.Log(line);
    }

    /// <summary>
    /// Task 2 (F27 unification): the shared damage_p_proc precondition every RunDamageProc call site
    /// needs, ported from fallout2-ce src/combat.cc _damage_object() (:4847/4849-4851):
    /// <code>
    /// if (!a4) {
    ///     if (!objectIsPartyMember(a1) || !objectIsPartyMember(a5)) {
    ///         scriptSetFixedParam(a1->sid, damage);
    ///         scriptExecProc(a1->sid, SCRIPT_PROC_DAMAGE);
    ///     }
    /// }
    /// </code>
    /// (the `sid`-bound object-has-a-script check that gates scriptExecProc itself is Sid == -1, the
    /// same precondition RunObjectProc/RunCombatProc use elsewhere in this port). This helper carries
    /// ONLY that shared pair: `target.Sid != -1`, and skip when target and source are BOTH party
    /// members — <c>_host.Dude</c> counts as a party member (gPartyMembers[0],
    /// The dude counts as a party member: object.cc:347 calls `partyMemberAdd(gDude)` at object load
    /// (which stamps his id at party_member.cc:398); party_member.cc:725's `gPartyMembers->object = gDude`
    /// is the save-load path of the same fact., set on party init/load). It does NOT
    /// carry the `!a4` term itself (fallout2-ce's "hit an unintended target" flag) — each call site's
    /// own `hitUnintendedTarget`/`attackSourced`/`victim == selfDamageProcFor`/`victim == attacker`/
    /// `dmg > 0` conditions stay AT the site (F12, F13, F16 each established theirs deliberately;
    /// folding them in here would silently recreate F12, where a missed shot's collateral victim would
    /// run a proc the reference suppresses).
    ///
    /// Per Task 1's investigation: the old per-site `target != _host.Dude` term has NO reference
    /// counterpart (:4849 is a pair gate only — vanilla DOES run the dude's damage_p_proc against an
    /// enemy-sourced hit) and is dropped here. It was behaviourally inert against every shipped map
    /// (the dude's Sid never resolves to a real object script — see BACKLOG F29), so dropping it changes
    /// no observable output; it only lets a hypothetical live-sid dude (as the reference genuinely wires
    /// up via scriptsSetDudeScript, scripts.cc:1460-1489) reach the same pair gate as everyone else.
    /// </summary>
    private bool ShouldRunDamageProc(MapObject target, MapObject? source)
    {
        if (target.Sid == -1)
            return false;
        bool targetIsPartyMember = target == _host.Dude || _host.PartyMembers.Contains(target);
        bool sourceIsPartyMember = source is not null && (source == _host.Dude || _host.PartyMembers.Contains(source));
        return !(targetIsPartyMember && sourceIsPartyMember);
    }

    /// <summary>F32 harness: exercises the exact ShouldRunDamageProc pair gate (:4849) shared by all six
    /// production RunDamageProc call sites, without any of the proc's own output reaching a
    /// golden-visible sink (RunDamageProc routes through _host.Log, never _host.Transcript — see
    /// BACKLOG F32). Returns whether the gate let the proc run, so a golden fixture can pin the gate's
    /// outcome directly instead of an unobservable script side effect.</summary>
    public bool ProbePartyDamageProc(MapObject victim, MapObject? source, int damage)
    {
        if (!ShouldRunDamageProc(victim, source))
            return false;
        _host.RunDamageProc(victim, source, damage);
        return true;
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

        // P70: Stonewall — the dude has a 50% chance to resist a knockback/knockdown entirely
        // (combat.cc:4641, randomBetween(0,100) < 50). DUDE-only; rank 0 short-circuits BEFORE the roll
        // so no RNG is drawn -> byte-identical (no golden dude carries the perk).
        if (target == _host.Dude && _host.DudePerkRank(Perks.PerkId.Stonewall) > 0 && _rng.Next(0, 101) < 50)
            return;

        // P74-M2: the Knockback weapon perk halves the divisor (5 vs 10) → double the shove (combat.cc:4651).
        Shove(attack.Attacker.HexTile, target, attack.Damage / (attack.KnockbackPerk ? 5 : 10));

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
    /// _compute_explosion_*; victim discovery now walks the engine's own ring-spiral
    /// (<see cref="ExplosionSpiral"/>, ported from _compute_explosion_on_extras), with LoS applied per
    /// victim and a cap of 6 hits (combat.cc explosionGetMaxTargets). NOTE the cap here counts the
    /// centre critter too, unlike the reference where explosionGetMaxTargets (6) bounds only the
    /// EXTRAS array and the primary defender is hit outside/before that cap — so the reference can
    /// damage up to 7 critters from one blast where this port caps at 6. NOTE ALSO: when two critters
    /// share a tile, only the first one enumerated into <c>byTile</c> can ever be a victim of that
    /// tile — a second critter on the same tile takes zero blast damage where the reference (whose
    /// _obj_blocking_at also yields a single object per tile, and can itself pick a wall over a
    /// critter) would have processed whichever object it found there. Not changed: judged more
    /// faithful than less, but gameplay-visible, so documented here.</summary>
    public void Explode(int centerTile, MapObject? killer, int minDamage, int maxDamage, int radius,
        MapObject? selfDamageProcFor = null, bool attackSourced = false)
    {
        const int maxTargets = 6;
        const int explosionDt = CritterStat.DamageThreshold + 6; // STAT_DAMAGE_THRESHOLD_EXPLOSION
        const int explosionDr = CritterStat.DamageResistance + 6; // STAT_DAMAGE_RESISTANCE_EXPLOSION

        var victims = _host.CombatCritters.Where(c => !c.IsDead).ToList();
        if (_host.Dude is { } dude && !victims.Contains(dude))
            victims.Add(dude);

        int diffMod = DiffDmgMod(killer); // P84: an enemy blast scales by Easy/Hard; a dude/null blast = 100
        int hits = 0;

        // ported from fallout2-ce src/combat.cc _compute_explosion_on_extras (:4022): victims are
        // found ring-by-ring in rotation order, not nearest-first — the order decides which victim
        // draws its damage first. DOCUMENTED DIVERGENCE: the reference never examines the blast tile
        // (its occupant is the primary defender, damaged by the main attack path); Hexwaste's Explode
        // has no separate primary path, so the centre critter is damaged FIRST and the spiral orders
        // the rest.
        var byTile = new Dictionary<int, MapObject>();
        foreach (MapObject c in victims)
            byTile.TryAdd(c.HexTile, c);

        var ordered = new List<MapObject>();
        var seen = new HashSet<MapObject>();
        if (byTile.TryGetValue(centerTile, out MapObject? atCenter) && seen.Add(atCenter))
            ordered.Add(atCenter);
        foreach (int tile in ExplosionSpiral.Tiles(centerTile, radius))
            if (byTile.TryGetValue(tile, out MapObject? victimAtTile) && seen.Add(victimAtTile))
                ordered.Add(victimAtTile); // combat.cc:4063-4070 — the reference scans `extras` for the
                                           // obstacle before adding it, so no victim is hit twice.

        foreach (MapObject victim in ordered)
        {
            if (hits >= maxTargets)
                break;
            // ported from fallout2-ce src/combat.cc _apply_damage() (:4738): the extras loop re-checks
            // `(obj->data.critter.combat.results & DAM_DEAD) == 0` for every entry before processing it,
            // because an earlier entry's damage_p_proc can kill a later one. `ordered` is a snapshot
            // list built before any script runs, and now that the self-damage proc (F13) can run a
            // script mid-loop, a proc that kills a not-yet-processed victim would otherwise reach
            // `KillCritter` a second time — a double destroy_p_proc and double XP award, since
            // KillCritter itself has no IsDead early-return.
            if (victim.IsDead)
                continue;
            // Line-of-sight from the blast centre (walls shield).
            (MapObject? blocker, _) = LineOfFire.Trace(centerTile, victim.HexTile,
                t => _host.ShootBlockerAt(t, victim, victim));
            if (blocker is not null && victim.HexTile != centerTile)
                continue;
            if (_host.GetCritterState(victim) is not { } state)
                continue;

            hits++;
            // P84: the difficulty modifier scales the raw blast before DT (engine order), like every
            // other attackComputeDamage path. 100 (Normal / dude / environmental) = byte-identical.
            int raw = _rng.Next(minDamage, maxDamage + 1) * diffMod / 100;
            int damage = Math.Max(raw - state.Stat(explosionDt), 0);
            damage -= state.Stat(explosionDr) * damage / 100;
            if (damage <= 0)
                continue;

            victim.CurrentHp -= damage;
            _host.Log($"The blast hits the {_host.ObjectName(victim)} for {damage} damage.");
            _host.Transcript($"explosion-hit: {_host.ObjectName(victim)}@{victim.HexTile} damage={damage}");

            // ported from fallout2-ce src/combat.cc _damage_object() (:4847, community fix #493): the
            // DAM_EXPLODE crit-failure branch self-damages through attackComputeDamage(attack, 1, 2)
            // (:4231-4232) and lands in the same _apply_damage path as DAM_HIT_SELF, so the fumbling
            // critter runs its own damage_p_proc — with itself as both damaged object and source. The
            // proc is skipped when object and source are both party members, which for self-damage means
            // every party member including the dude, so only an unaffiliated critter runs it. It fires
            // BEFORE the kill check because the reference's proc gate (:4847) precedes its DAM_DEAD
            // destroy block (:4855). selfDamageProcFor is null for every other caller — an ordinary
            // blast has no self-damaged attacker — so this is inert by construction (F13, fixed 2026-08-15).
            // Firing the proc before the Shove call below is NOT an arbitrary choice: for this exact
            // event, attackComputeCriticalFailure() clears DAM_HIT (combat.cc:4180) before calling
            // attackComputeDamage(), which then takes the attacker-damage branch and sets
            // knockbackDistancePtr = nullptr unconditionally (combat.cc:4517) — the reference computes
            // NO knockback for the fumbling attacker's own self-damage, so there is no reference
            // knockback for this proc to precede or follow. Explode()'s Shove call below is inherited
            // from the shared grenade-blast path (`actionExplode` / `_compute_explosion_*`, see the
            // class-level doc comment above), not from this crit-failure event; F17 (below) suppresses
            // it for this victim specifically, so it has no reference ordering to match here either way.
            if (victim == selfDamageProcFor && ShouldRunDamageProc(victim, victim))
                foreach (string line in _host.RunDamageProc(victim, victim, damage))
                    _host.Log(line);

            // F16: the sibling of the block above. ported from fallout2-ce src/combat.cc _apply_damage()
            // extras loop (:4751, community fix #493): every OTHER critter caught in an attack-sourced
            // blast (a thrown grenade, or a crit-fail explode's other victims) also runs its
            // damage_p_proc, with the SOURCE being the blast's attacker (attack->attacker at :4751) —
            // never the victim itself; that shape is F13's self-damage block above.
            //
            // At bare e97087b the flag passed to _damage_object() here is `attack->defender ==
            // attack->oops` (:4751 pre-#493); for this event defender == oops (Explode() never diverges a
            // victim from what it targeted, so the two are always equal here), making the flag TRUE —
            // _damage_object's `if (!a4)` gate (:4847) then suppresses the proc entirely, so AT BARE
            // E97087B THIS PROC WOULD NOT RUN. Hexwaste does not carry that polarity: F13 already adopted
            // #493's rewrite, which collapses all three site-specific oops/defender expressions into one
            // `hitUnintendedTarget = attack->defender != attack->intendedTarget` — always false here for
            // the same reason — so under the polarity Hexwaste carries, the proc DOES run. Not porting
            // this half while F13 ported the self-damage half is exactly the asymmetry this closes.
            //
            // `attackSourced` is a Hexwaste-only gate, not a reference concept — the reference's
            // _apply_damage extras loop is reached from two different callers: the in-attack
            // _compute_explosion_on_extras (grenades, crit-fail explode — attack->attacker is the real
            // attacking critter, i.e. our `killer`) and actionExplode (the scripted `explosion` opcode +
            // queue.cc's planted-charge detonation), where attackInit(attack, explosion, critter, ...)
            // (actions.cc:1631) makes attack->attacker the transient misc-10 explosion marker object —
            // NEVER the placer (queue.cc passes gDude only as a bookkeeping `sourceObj` to
            // _report_explosion, not as attack->attacker). Hexwaste's Explode() conflates both reference
            // shapes behind one `killer` parameter and does not model the marker object, so `killer`
            // being non-null cannot be used to detect "this was a real attack" — the planted-charge caller
            // (ViewerGame.cs ProcessArmedCharges) passes killer=dude even though its reference source is
            // never a critter. attackSourced is the explicit opt-in that ambiguity needs; only the two
            // callers that resolve a genuine Attack (the grenade throw and the crit-fail explode) set it.
            //
            // The party half of the gate is ShouldRunDamageProc (:4849's pair gate, shared by all six
            // RunDamageProc call sites as of Task 2/F27 — see its doc comment for the full port note and
            // the F29 resolution of the dude-exclusion question this block used to carry inline).
            if (attackSourced && victim != killer && ShouldRunDamageProc(victim, killer))
                foreach (string line in _host.RunDamageProc(victim, killer, damage))
                    _host.Log(line);

            // F17: ported from fallout2-ce src/combat.cc attackComputeCriticalFailure (:4180), which
            // clears DAM_HIT as its very first statement, before calling attackComputeDamage
            // (:4513-4517): with DAM_HIT cleared, attackComputeDamage takes the attacker-damage (else)
            // branch and sets knockbackDistancePtr = nullptr UNCONDITIONALLY — the reference computes
            // ZERO knockback for a fumbler's own self-damage. This is a suppression of a Hexwaste-only
            // side effect (Explode()'s Shove call, inherited from the shared grenade-blast path), not a
            // tuning choice: without it, HexGrid.RotationTo(centerTile, centerTile) is degenerate for
            // the fumbler standing on the blast tile and can push it in an arbitrary direction.
            if ((victim.Flags & OBJECT_MULTIHEX) == 0 && victim != selfDamageProcFor)
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

    /// <summary>ported from fallout2-ce src/combat_ai.cc _combatai_rating (:3449-3479): this critter's
    /// threat rating, 0 for a null/dead/knocked-out critter (the engine's DAM_DEAD | DAM_KNOCKED_OUT and
    /// non-critter guards). The reference takes max(meleeDamage, item2 maxDamage, item1 maxDamage) + AC —
    /// a MAX over melee and both hands, not a sum (AiRating.Score already implements the max correctly;
    /// only this comment was wrong). Hexwaste models one wielded slot rather than two hands, so it
    /// considers only the actively-wielded weapon (EquippedWeapon) — for the dude, a weapon carried in a
    /// non-active hand is not considered. Currently unreachable in practice: Rating is only ever called
    /// on hostiles, which don't have a second-hand concept here either.</summary>
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

    /// <summary>Ports fallout2-ce src/critter.cc `_critter_set_who_hit_me` (:1285-1301) — the single gate
    /// the reference uses everywhere it writes a critter's whoHitMe. The full reference condition is
    /// `a2 == nullptr || a1.team != a2.team || (statRoll(a1, STAT_INTELLIGENCE, -1) < 2 &&
    /// !(partyMember(a1) && partyMember(a2)))` (critter.cc:1296, in _critter_set_who_hit_me at :1285): a null attacker or a cross-team attacker
    /// always writes; a same-team attacker writes only on a failed INT roll (INT 5 → 60% chance) and never
    /// between two party members.
    ///
    /// DELIBERATE DIVERGENCE: Hexwaste simplifies the same-team INT-roll branch to an unconditional
    /// REFUSAL — same team never writes, full stop. This keeps combat setup free of RNG for a reason
    /// unrelated to this helper (drawing on the roll here would move golden fixtures). The cross-team
    /// path — the only one the reference reaches without drawing RNG, since the `||` short-circuits on
    /// `team != team` before the roll — is modelled EXACTLY. A null attacker also writes exactly (clears
    /// whoHitMe), matching the reference's `a2 == nullptr` arm.
    ///
    /// Both call sites that stamp whoHitMe in the reference (`_combat_sequence_init`, combat.cc:3012/3016,
    /// and the KO/DEAD bypass, combat.cc:4711-4716) route through this one helper here, so Hexwaste no
    /// longer carries two contradictory models of the same reference gate.</summary>
    private static void SetWhoHitMe(MapObject target, MapObject? attacker)
    {
        if (attacker is null || attacker.Team != target.Team)
            target.WhoHitMe = attacker;
    }

    /// <summary>Record who last hit a critter (whoHitMe) — ported from fallout2-ce combat.cc:4707 +
    /// combat_ai.cc _combatai_check_retaliation (:3484): an unset whoHitMe is taken unconditionally, but
    /// an existing one is REPLACED only by a strictly higher-rated attacker (_combatai_rating), so a
    /// critter keeps hunting the scarier enemy rather than whoever last scratched it. An equally-rated
    /// attacker does not steal aggro. Hexwaste's team gate is retained as an early exit here AND inside
    /// `SetWhoHitMe` (the single helper now modelling `_critter_set_who_hit_me`) — the early exit is a
    /// fast path only, both checks agree. This moved the brawl-watch fixture
    /// (deliberately re-recorded — see
    /// docs/superpowers/specs/2026-08-12-retaliation-rerecord-design.md).
    ///
    /// KO/DEAD BYPASS (combat.cc:4711-4716): the reference branches on the JUST-APPLIED hit's outcome —
    /// if it leaves the defender DAM_DEAD or DAM_KNOCKED_OUT, `_critter_set_who_hit_me` (critter.cc:1285-
    /// 1301) is called instead of `_combatai_check_retaliation`. That callee is NOT unconditional: it
    /// stamps whoHitMe only when the attacker is null, OR the attacker's team differs from the defender's,
    /// OR a `statRoll(defender, STAT_INTELLIGENCE, -1) < 2` check passes (and even then only when the
    /// defender/attacker aren't BOTH party members). So it carries its own team filter — a same-team
    /// knockout does not stamp whoHitMe in the reference except via that INT-roll exception. Hexwaste's
    /// bypass therefore only lifts the RATING gate, not the team gate: since ApplyCritStatus (which can
    /// set DamKnockedOut) runs immediately before this call, IsKnockedOut(target) reflects THIS hit's
    /// outcome, and a KO'd target takes the attacker unconditionally once past the team check, bypassing
    /// only the rating gate below.
    ///
    /// Documented simplifications (not modelled): (1) the `statRoll(INT) < 2` exception, which can still
    /// stamp whoHitMe on a same-team KO in the reference; (2) combat.cc:4713's extra condition that skips
    /// the stamp entirely when the KO'd defender is the dude and the hit wasn't an "oops" (friendly-fire)
    /// hit — Hexwaste always stamps once past the team gate.</summary>
    private void RegisterHit(MapObject target, MapObject attacker)
    {
        if (target.IsDead || attacker == target)
            return;
        if (attacker.Team == target.Team)
            return;
        if (IsKnockedOut(target))
        {
            SetWhoHitMe(target, attacker); // critter.cc:1285-1301 — bypasses only the rating gate below
            return;
        }
        if (target.WhoHitMe is { } current && Rating(attacker) <= Rating(current))
            return; // combat_ai.cc:3488 — only a STRICTLY greater rating retargets
        SetWhoHitMe(target, attacker);
    }

    /// <summary>True if the critter may take its turn (not knocked out, not on a
    /// lose-turn, not dead) — ports critterIsActive (critter.cc:942).</summary>
    private static bool CanAct(MapObject c) =>
        (c.CombatResults & (CriticalTables.DamKnockedOut | CriticalTables.DamLoseTurn | CriticalTables.DamDead)) == 0;

    /// <summary>Knock a critter unconscious + queue its wake (combat.cc:4799-4805) —
    /// public so the crit path, a script external, or a test can drive it.</summary>
    public void KnockOut(MapObject critter, MapObject? knockedOutBy = null)
    {
        if (critter.IsDead || IsKnockedOut(critter))
            return;
        critter.CombatResults |= CriticalTables.DamKnockedOut;
        int en = _host.GetCritterState(critter)?.Stat(CritterStat.Endurance) ?? 5;
        _events.Schedule(_combatTick, 10 * (35 - 3 * en), critter, EventQueue.EventType.Knockout);
        _host.Log($"The {_host.ObjectName(critter)} is knocked out!");
        _host.Transcript($"knockout: {_host.ObjectName(critter)}@{critter.HexTile}");

        // P100 (Point 3): fo2ce combat.cc:5370 _scr_end_combat — a dude knocked out (not killed) hands the
        // MAP script's combat_p_proc first refusal (fixedParam = KO'er team, combat.cc:5944
        // _combat_player_knocked_out_by). If it script_overrides, the ring "caught" the KO → end the fight at
        // the turn boundary (no game-over), instead of leaving the dude unconscious among enemies. No slice
        // map's map-script overrides here, so this is inert outside a prizefight arena (proven by fake host).
        if (critter == _host.Dude && knockedOutBy is not null && _host.RunMapCombatOver(knockedOutBy.Team))
            RequestTerminateCombat();
    }

    /// <summary>Apply a crit's honored status flags to the target (P14-M2/M3): knockout
    /// queues a wake; lose-turn/crippled-limb/blind are recorded on CombatResults
    /// (consumed by the turn loop / CritterState).</summary>
    private void ApplyCritStatus(MapObject target, int critFlags, MapObject? attacker = null)
    {
        int status = critFlags & StatusFlags;
        if (status == 0 || target.IsDead)
            return;
        target.CombatResults |= status & (CriticalTables.DamLoseTurn | CriticalTables.DamCripLimbs | CriticalTables.DamBlind);
        if ((status & CriticalTables.DamCripLimbs) != 0 || (status & CriticalTables.DamBlind) != 0)
            _host.Transcript($"crippled: {_host.ObjectName(target)}@{target.HexTile} flags=0x{status:X}");
        if ((status & CriticalTables.DamKnockedOut) != 0)
            KnockOut(target, attacker); // P100 (Point 3): the KO'er drives the map-script "combat over" hook
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
    /// <summary>
    /// The per-turn combat_p_proc hook, ported from fallout2-ce src/combat.cc:3243-3258 (_combat_turn):
    /// for a scripted combatant (sid != -1) run combat_p_proc (fixedParam=4, source+target null); if the
    /// script called script_overrides() the engine skips the standup + default-AI block entirely
    /// (combat.cc:3259) — we mirror that by returning true so the caller forfeits the rest of the turn.
    /// Runs INSIDE the !incapacitated branch (caller checks SkipTurnIfIncapacitated first, :3231).
    /// </summary>
    private bool RunCombatProcOverridesTurn(MapObject critter)
    {
        if (critter.Sid == -1)
            return false;
        (IReadOnlyList<string> lines, bool overridden) = _host.RunCombatProc(critter, 4);
        foreach (string line in lines)
            _host.Log(line);
        return overridden;
    }

    private int StandUpIfProne(MapObject critter, int ap)
    {
        if (!_knockedDown.Remove(critter))
            return -1;
        // P70: Quick Recovery — the dude stands in 1 AP instead of 3 (combat.cc:5396 _combat_standup,
        // a1 == gDude). Rank 0 -> StandUpApCost (3), the transcript prints the same -> byte-identical.
        int cost = critter == _host.Dude && _host.DudePerkRank(Perks.PerkId.QuickRecovery) > 0 ? 1 : StandUpApCost;
        _host.Transcript($"getup: {_host.ObjectName(critter)} (-{cost} AP)");
        _host.OnGetUp(critter); // P34-M6: the visible stand-up sprite (the prone flag is already cleared)
        return Math.Max(ap - cost, 0);
    }

    private void KillCritter(MapObject critter, MapObject? killer = null,
        int damage = 0, int damageType = 0, int attackerAnim = DeathAnims.FallBack)
    {
        // The victim is already flagged DAM_DEAD when destroy_p_proc runs in the engine
        // (_set_new_results precedes _damage_object's destroy block, combat.cc:~4790) — set it first
        // so a destroy_p_proc that tests critter_is_dead(self) sees "dead" (P108).
        critter.CombatResults |= 0x80; // DAM_DEAD
        critter.CombatResults &= ~(CriticalTables.DamKnockedOut | CriticalTables.DamLoseTurn); // dead, not unconscious

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

        // P113 (item 7d): itemDestroyAllHidden (combat.cc:4858, right after destroy_p_proc) — natural-
        // weapon items (ITEM_HIDDEN proto flag: claws, flame breath) vanish from the corpse's loot.
        for (int i = critter.Inventory.Count - 1; i >= 0; i--)
            if (_host.ItemIsHidden(critter.Inventory[i]))
                critter.Inventory.RemoveAt(i);

        // Engine: kills by the dude OR his team accrue XP — never for the dude's own death
        // (combat.cc:4860 gates on victim != gDude).
        bool dudeTeamKill = killer == _host.Dude || (killer is not null && killer.Team == 0);
        if (!xpOverridden && dudeTeamKill && critter != _host.Dude
            && _host.GetCritterState(critter) is { } stats)
        {
            _xpPending += stats.Proto.Experience;
            _host.RecordKill(critter); // killsIncByType(critterGetKillType(victim)), combat.cc:4870
        }

        _host.RemovePartyMember(critter);

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
        _hostiles.Add(target);
        AddJoiners();
        // The dude opened combat → round-1 order is dude (attacker) first, target (defender) second
        // (_combat_sequence_init). The dude's slot is index 0, so the turn stays his — he attacks now.
        BuildTurnOrder(firstRound: true, _host.Dude, target);
        _host.Log($"Combat begins — round 1, your turn (AP {_dudeAp}).");
    }

    /// <summary>Start a multi-team brawl (phase-16 M3, X-FIGHTING-Y): every supplied
    /// non-dude critter joins as a hostile, and cross-team targeting makes the opposing
    /// groups fight each other as well as the dude. Opens on the dude's turn so the
    /// player can watch the factions thin each other out, or wade in. A NEW entry point
    /// — it does not touch BeginCombat/AddJoiners, so single-team combat is untouched.
    /// <para>P73: with <paramref name="dudeSpectator"/>, the dude is NOT a combatant — he's left
    /// out of the turn order and is never targeted, the brawl auto-runs every NPC slot, and it ends
    /// when one team remains. Default false → the dude-involved path is byte-identical.</para></summary>
    public void StartBrawl(IEnumerable<MapObject> combatants, bool dudeSpectator = false)
    {
        MapObject? dude = _host.Dude;
        if (dude is null || _gameOver || _phase != CombatPhase.Idle)
            return;
        _host.StopDude();
        _dudeSpectator = dudeSpectator;
        // Spectator: open in EnemyTurn so UpdateCombat auto-steps the NPC order (no dude slot to pause on).
        _phase = dudeSpectator ? CombatPhase.EnemyTurn : CombatPhase.PlayerTurn;
        _round = 1;
        _combatTick = 0;
        _events.ClearAll();
        _hostiles.Clear();
        foreach (MapObject c in combatants)
            if (!c.IsDead && c != dude && c.Team != dude.Team)
            {
                _hostiles.Add(c);
                c.WhoHitMeCid = -1;
                c.WhoHitMe = null; // P101 (bucket 3): fresh retaliation state per fight
            }
        // Task-2 port: seed every combatant's initial danger source — ported from fallout2-ce
        // src/combat_ai.cc _caiTeamCombatInit (:1725-1755), generalized past its fixed
        // attacker/defender-TEAM-pair shape (StartBrawl supports N teams, not just two): each hostile's
        // whoHitMe starts as the nearest critter on ANY other team — AiTargets.FindNearestTeam(self, self,
        // sameTeam: false, ...), the "different team" branch Task 1 documented as unused by any SHIPPED
        // reference call site (every existing caller passes flags=1/sameTeam); StartBrawl is a new,
        // Hexwaste-specific caller that legitimately needs it. Without this, DangerSource's entirely
        // whoHitMe/aiFindAttackers-driven acquisition finds nothing on round 1 (StartBrawl nulled every
        // combatant's whoHitMe above, and nobody has been attacked yet) and every combatant passes forever
        // — the reference avoids this because a scripted team fight always runs _caiTeamCombatInit first.
        // Minor-5 note (Task-2 review): the reference's _caiTeamCombatInit (:1725-1755) loops the WHOLE
        // combat list and seeds every member of either team, and also stamps whoHitMeCid there. This
        // seeds only _hostiles — the dude and party members are left unseeded, and WhoHitMeCid is not
        // touched at all. Left as-is: every shipped StartBrawl caller is a spectator/NPC-vs-NPC brawl
        // (dude/party never participate as targets in that scenario, and DangerSource is only ever
        // invoked for non-dude, non-party critters here), so the gap is immaterial to any observed
        // behavior; widening it would touch dude/party WhoHitMe with no shipped caller to validate
        // against.
        foreach (MapObject c in _hostiles)
            c.WhoHitMe = AiTargets.FindNearestTeam(c, c, sameTeam: false, CombatRoster(c));

        if (!dudeSpectator && _host.GetCritterState(dude) is { } stats)
            ResetDudeAp(stats);
        BuildTurnOrder(firstRound: true, dudeSpectator ? null : dude, null);
        var teams = _hostiles.Select(h => h.Team).Distinct().OrderBy(t => t).ToList();
        if (!dudeSpectator)
            _host.Log($"You stumble into a battle! ({_hostiles.Count} combatants).");
        _host.Transcript($"brawl: combatants={_hostiles.Count} teams=[{string.Join(",", teams)}]"
            + (dudeSpectator ? " spectator" : ""));
    }

    // CRITTER_MANEUVER_* flags (obj_types.h:120-123) — MapObject.Maneuver carries them.
    private const int ManeuverEngaging = 0x01, ManeuverDisengaging = 0x02, ManeuverFleeing = 0x04;

    /// <summary>P113 (4.3): isWithinPerception (combat_ai.cc:3499) for in-combat target acquisition —
    /// the observer's Perception, facing arc, and distance vs the target's detection range (PE×2
    /// hearing in combat), with the dude-sneak reduction when the target is the dude. The AI uses this
    /// to decide whether a critter has a valid danger source, mirroring _ai_danger_source (:1693).</summary>
    private bool WithinPerception(MapObject observer, MapObject target)
    {
        if (_host.GetCritterState(observer) is not { } os)
            return false;
        bool canSee = PerceptionDetect.CanSee(observer.Rotation, observer.HexTile, target.HexTile);
        int distance = HexGrid.Distance(observer.HexTile, target.HexTile);
        return PerceptionDetect.IsWithinPerception(distance, os.Perception, _host.DudeSneakSkill,
            canSee, (target.Flags & 0x20000) != 0, target == _host.Dude,
            _host.DudeIsActivelySneaking, _host.DudeHasSneakFlag, inCombat: true);
    }

    /// <summary>Hexwaste's stand-in for the reference's <c>_curr_crit_list</c> in a
    /// <see cref="DangerSource"/> call: the current combatants (hostiles + party + dude),
    /// <paramref name="self"/> included (the helpers below skip it by identity, matching the reference's
    /// in-place self-skip), sorted nearest-first from <paramref name="self"/> —
    /// <c>_ai_sort_list_distance(_curr_crit_list, _curr_crit_num, a1)</c>, run once and shared by both
    /// the whoHitMe fallback and aiFindAttackers (both re-sort from the same origin in the reference, so
    /// one sort here is equivalent).
    ///
    /// DIVERGENCE 1 — MEMBERSHIP IS NARROWER, and this is the material one. The reference's list is NOT
    /// the combatant list: <c>_combat_ai_begin(_list_total, _combat_list)</c> (combat.cc:2649, its only
    /// caller) snapshots — once, at combat start, fixed for the whole fight — the list built at
    /// combat.cc:2574 by <c>objectListCreate(-1, _combat_elev, OBJ_TYPE_CRITTER, &amp;_combat_list)</c>:
    /// EVERY critter on the combat elevation, combatants and non-combatants alike (<c>_list_total</c>,
    /// not <c>_list_com</c>). So vanilla's <c>aiFindAttackers</c> and <c>_ai_find_nearest_team</c> can
    /// legitimately return a bystander who never joined the fight — reachable through the
    /// <c>whoHitByFriend</c> slot and through the dead-whoHitMe <c>FindNearestTeam</c> fallback. Hexwaste's
    /// roster cannot: it is only the live combatant set, and it is recomputed per call rather than frozen
    /// at combat start. Consequence: in a multi-faction fight Hexwaste's AI will pick a different target
    /// than vanilla wherever vanilla would have picked a bystander. Widening this to the elevation's
    /// critters is the faithful fix; it is deliberately NOT done here because it moves target decisions
    /// (and therefore goldens) across every fixture, which is its own change.
    ///
    /// DIVERGENCE 2 — <c>_dudeSpectator</c> (P73) drops the dude from the roster when he isn't part of
    /// THIS brawl. The reference always includes gDude; Hexwaste supports dude-absent brawls the
    /// reference has no counterpart for, so this one is a carried design divergence, not a gap.</summary>
    private List<MapObject> CombatRoster(MapObject self)
    {
        IEnumerable<MapObject> all = _hostiles.Concat(_host.PartyMembers);
        if (!_dudeSpectator && _host.Dude is { } dude)
            all = all.Concat([dude]);
        return all.Distinct().OrderBy(c => HexGrid.Distance(self.HexTile, c.HexTile)).ToList();
    }

    /// <summary>ported from fallout2-ce src/combat.cc _combat_check_bad_shot (:5643-5694), the single
    /// mutually-exclusive bad-shot reason for <paramref name="attacker"/> firing its CURRENTLY EQUIPPED
    /// weapon (the reference's HIT_MODE_RIGHT_WEAPON_PRIMARY, aiming=false) at <paramref name="defender"/>.
    /// Checked in the reference's own order so each guard's status is the first one that applies. AP is
    /// read from <see cref="_actingEnemyAp"/> — the reference reads the attacker's own live combat.ap,
    /// which for the critter whose turn DangerSource runs on IS <see cref="_actingEnemyAp"/> (set at
    /// TryEnemyAction's turn start, decremented as AP is spent) — so this is only meaningful for the
    /// critter currently acting; a caller invoking DangerSource off-turn (e.g. a unit test) must seed
    /// <see cref="_actingEnemyAp"/> itself, exactly as the reference implicitly requires a live combat.ap.
    /// NOT ported: throw-type weapons (Hexwaste's thrown items don't route through EquippedWeapon here)
    /// and the friendly-fire safety gate (a Hexwaste addition elsewhere, not part of vanilla's bad-shot
    /// reasons) — see the DangerSource doc for the full soft-spot note.
    ///
    /// Minor-3 note (Task-2 review): both shipped call sites pass <c>self</c> (DangerSource's own
    /// parameter, the acting critter) as <paramref name="attacker"/>, so `_actingEnemyAp` is always that
    /// critter's own AP in practice — left unparameterized rather than threading AP through the call, to
    /// avoid widening a signature with no second caller to validate against yet (a future ally-AI
    /// integration is the natural point to revisit this).</summary>
    private enum ShotStatus { Ok, AlreadyDead, ArmsCrippled, NotEnoughAp, OutOfRange, NoAmmo, AimBlocked }

    private ShotStatus CheckBadShot(MapObject attacker, MapObject defender)
    {
        if (defender.IsDead)
            return ShotStatus.AlreadyDead;

        (ProtoInfo? weaponProto, MapObject? weaponItem) = _host.EquippedWeapon(attacker);
        if (WeaponBlockedByCrippledArms(attacker, weaponProto) is not null)
            return ShotStatus.ArmsCrippled;

        int apCost = weaponProto?.Weapon?.ApCost ?? CombatMath.PunchApCost;
        if (apCost > _actingEnemyAp)
            return ShotStatus.NotEnoughAp;

        bool isGun = weaponProto?.Weapon is { } w && w.IsGun(weaponProto.ExtendedFlags);
        int range = isGun ? weaponProto!.Weapon!.MaxRange1 : Math.Min(weaponProto?.Weapon?.MaxRange1 ?? 1, 2);
        if (HexGrid.Distance(attacker.HexTile, defender.HexTile) > range)
            return ShotStatus.OutOfRange;

        // ported from fallout2-ce src/combat.cc _combat_check_bad_shot (:5678-5680): gated on
        // `ammoGetCapacity(weapon) > 0`, NOT on isGun — any weapon with an ammo slot draws this
        // check. Matches the reference exactly; five non-gun ammo-capacity weapons ship in
        // Fallout 2 — Ripper (116), Cattle Prod (160), Power Fist (235), Super Cattle Prod (399)
        // and Mega Power Fist (407), all drawing Small Energy Cell — so this is a real fidelity fix.
        if ((weaponProto?.Weapon?.AmmoCapacity ?? 0) > 0 && _host.WeaponAmmo(weaponProto!, weaponItem!) <= 0)
            return ShotStatus.NoAmmo;

        // ported from fallout2-ce src/combat.cc _combat_check_bad_shot (:5682-5687): gated on
        // `attackType == RANGED || THROW || weaponGetRange(hitMode) > 1`, NOT on isGun — a range-2
        // (or longer) melee weapon (e.g. a spear) also draws the blocked-shot check. Hexwaste has no
        // distinct THROW attack type here (see the NOT-ported note above), but `range > 1` already
        // covers the melee-reach case the isGun-only gate was missing.
        if (isGun || range > 1)
        {
            (MapObject? blocker, _) = LineOfFire.Trace(attacker.HexTile, defender.HexTile,
                tile => _host.ShootBlockerAt(tile, attacker, defender));
            if (blocker is not null)
                return ShotStatus.AimBlocked;
        }

        return ShotStatus.Ok;
    }

    /// <summary>ported from fallout2-ce src/combat_ai.cc _ai_danger_source (:1529-1705): the critter's
    /// single most urgent target this turn. Order matches the reference exactly: the party-only
    /// disposition/attack_who read → the (party-only) ATTACK_WHO_WHOMEVER_ATTACKING_ME short-circuit and
    /// the STRONGEST/WEAKEST/CLOSEST whoHitMe clear → the LIVING-whoHitMe early return (:1657, ungated —
    /// no perception/reachability check; this applies to every critter, party or not) → the
    /// dead-whoHitMe FindNearestTeam fallback → aiFindAttackers' three candidates → the fleeing filter →
    /// the strength/weakness/distance sort → the perception + (reachability OR legal-shot) scan.
    ///
    /// EXCLUDED (decided, CLAUDE.md out-of-scope): the "// CE:" previous-target-continuation improvement
    /// wrapping the ATTACK_WHO_WHOMEVER_ATTACKING_ME case (the `if (1)` block, :1564-1637, whose CE arm is
    /// :1565-1590) — non-vanilla QoL. The vanilla
    /// fallback loop it wraps (nearest critter, cross-team, alive, currently attacking the dude, reachable,
    /// and not a definitively-bad shot) IS ported, using <see cref="MapObject.LastAttackTarget"/> for
    /// "currently attacking the dude" (aiInfoGetLastTarget).
    ///
    /// SOFT SPOT (established, not assumed): before this port, Hexwaste had NO _combat_check_bad_shot
    /// counterpart at all — only scattered inline range/ammo/LoF/crippled-arm checks duplicated at each
    /// attack call site (TryAttack, TryEnemyAction, TryAllyAction, ...). <see cref="CheckBadShot"/> is the
    /// first unified port, built for this function; it covers dead/crippled-arms/AP/range/ammo/LoF (the
    /// reference's 6 gates for a non-throw weapon) but does not model throw-type weapons or the
    /// ATTACK_TYPE_THROW range>1 branch distinctly — Hexwaste's range formula already folds thrown-item
    /// range into MaxRange1/2 elsewhere, so this is a documented narrowing, not a silent gap.
    ///
    /// PARTY GATING (:1541/:1648): the whole disposition/attack_who apparatus is gated on
    /// <c>_host.PartyMembers.Contains(self)</c> — a non-party critter takes attackWho = -1 (never
    /// consults AttackWho; the STRONGEST/WEAKEST/CLOSEST whoHitMe-clear is party-only). Hexwaste's only
    /// current call site (TryEnemyAction) never passes a party member, so this branch is presently
    /// reachable only via a direct call (e.g. a unit test) — kept faithful/generic rather than narrowed
    /// to match the one caller, since a future ally-AI integration should be able to reuse it unchanged.
    ///
    /// ROSTER (read <see cref="CombatRoster"/>'s note in full): the candidate list this function scans is
    /// narrower than the reference's <c>_curr_crit_list</c> — live combatants only, where vanilla snapshots
    /// every critter on the elevation including non-combatant bystanders — and, under
    /// <c>_dudeSpectator</c> (P73), excludes the dude entirely.
    /// </summary>
    private MapObject? DangerSource(MapObject self)
    {
        bool ignoreFleeingCritters = false;
        AttackWho? attackWho = null; // null == the reference's attackWho = -1 (non-party, :1648)
        List<MapObject> roster = CombatRoster(self);

        if (_host.PartyMembers.Contains(self)) // :1541
        {
            CompanionAi ai = _host.CompanionSettings(self).Effective();

            // :1543-1556 — Hexwaste's Disposition enum has no DISPOSITION_NONE/BERKSERK=false-only
            // counterpart split; every disposition ignores fleeing critters except Berserk (matching the
            // reference's case list: Custom/Coward/Defensive/Aggressive -> true, None/Berserk -> false).
            ignoreFleeingCritters = ai.Disposition != Disposition.Berserk;
            if (ignoreFleeingCritters && ai.Distance == Distance.Charge) // :1557-1559
                ignoreFleeingCritters = false;

            attackWho = ai.AttackWho; // :1561
            switch (ai.AttackWho)
            {
                case AttackWho.WhoeverAttackingMe: // case :1563, `if (1)` block :1564-1637; vanilla fallback loop :1597-1631
                    foreach (MapObject critter in roster)
                    {
                        if (critter == self)
                            continue;
                        if (critter.IsDead || IsKnockedOut(critter)
                            || critter.Team == self.Team
                            || critter.LastAttackTarget != _host.Dude)
                            continue;
                        if (Pathfinder.FindPath(self.HexTile, critter.HexTile, tile => _host.IsBlocked(tile),
                                requireFreeDestination: false) is null)
                            continue;
                        ShotStatus shot = CheckBadShot(self, critter);
                        if (shot != ShotStatus.Ok && shot != ShotStatus.NoAmmo && shot != ShotStatus.OutOfRange)
                            continue;
                        if (ignoreFleeingCritters && (critter.Maneuver & ManeuverFleeing) != 0)
                            continue;
                        return critter;
                    }
                    break;
                case AttackWho.Strongest:
                case AttackWho.Weakest:
                case AttackWho.Closest:
                    self.WhoHitMe = null; // :1642 — party-only whoHitMe clear
                    break;
            }
        }

        MapObject? target0 = null;
        MapObject? whoHitMe = self.WhoHitMe;
        if (whoHitMe is not null && whoHitMe != self)
        {
            if (!whoHitMe.IsDead)
            {
                // :1657 — the ungated early return: NO perception check, NO reachability check, applies
                // to every non-party critter (attackWho == null) and to a party member set to WHOMEVER.
                if (attackWho is null or AttackWho.Whomever)
                    return whoHitMe;
            }
            else if (whoHitMe.Team != self.Team)
            {
                target0 = AiTargets.FindNearestTeam(self, whoHitMe, sameTeam: true, roster); // :1661
            }
        }

        (MapObject? t1, MapObject? t2, MapObject? t3) = AiTargets.FindAttackers(self, roster); // :1668
        MapObject?[] targets = [target0, t1, t2, t3];

        if (ignoreFleeingCritters) // :1670-1676
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] is { } c && (c.Maneuver & ManeuverFleeing) != 0)
                    targets[i] = null;

        // :1678-1691 — non-null candidates only (the reference's qsort pushes nulls to the tail, so
        // filtering first and sorting the rest is equivalent for this scan-first-hit loop).
        IEnumerable<MapObject> live = targets.OfType<MapObject>();
        List<MapObject> sorted = attackWho switch
        {
            // VANILLA QUIRK (see CompanionAi.Better's comment): _compare_strength sorts ASCENDING, so
            // STRONGEST targets the LOWEST-rated candidate and WEAKEST (_compare_weakness, descending)
            // targets the HIGHEST-rated one. Deliberate — do not "correct" it.
            AttackWho.Strongest => live.OrderBy(c => Rating(c)).ToList(),
            AttackWho.Weakest => live.OrderByDescending(c => Rating(c)).ToList(),
            _ => live.OrderBy(c => HexGrid.Distance(self.HexTile, c.HexTile)).ToList(),
        };

        foreach (MapObject candidate in sorted) // :1693-1702
        {
            if (!WithinPerception(self, candidate))
                continue;
            byte[]? path = Pathfinder.FindPath(self.HexTile, candidate.HexTile,
                tile => _host.IsBlocked(tile), requireFreeDestination: false);
            if (path is not null || CheckBadShot(self, candidate) == ShotStatus.Ok)
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Does a candidate critter join the fight, ported from fallout2-ce src/combat_ai.cc
    /// _combatai_want_to_join() (:3165): a dead/knocked-out critter never joins; one hurt this turn
    /// (damageLastTurn > 0) always does; otherwise its combat_p_proc runs with fixedParam=5 (P35-M4 —
    /// the script may set its maneuver, e.g. by attacking → ENGAGING), and the maneuver decides
    /// (ENGAGING → join, DISENGAGING/FLEEING → don't); else the danger-source/team-sight heuristic
    /// (CombatRules.ShouldJoin). The hidden/elevation guards are covered by the CombatCritters set.
    /// </summary>
    private bool WantToJoin(MapObject c, MapObject dude)
    {
        if (c.IsDead || IsKnockedOut(c))
            return false;
        if (c.DamageLastTurn > 0)
            return true;
        if (c.Sid != -1)
            _host.RunCombatProc(c, 5); // fp=5: the script's want-to-join decision (may set maneuver)
        if ((c.Maneuver & ManeuverEngaging) != 0)
            return true;
        if ((c.Maneuver & (ManeuverDisengaging | ManeuverFleeing)) != 0)
            return false;
        // P113 (4.3): join if on a team with an active hostile AND the candidate PERCEIVES the dude —
        // _combatai_notify_friends + the perception-gated _ai_danger_source (combat_ai.cc:3632/1695),
        // replacing the flat 20-hex sight radius. A critter facing away or out of range holds off.
        return !c.IsDead && _hostiles.Any(h => h.Team == c.Team) && WithinPerception(c, dude);
    }

    /// <summary>Same-team / hurt / script-willing critters join the fight at round start
    /// (combat.cc:2905 _combat_add_noncoms → _combatai_want_to_join per candidate).</summary>
    private void AddJoiners()
    {
        MapObject? dude = _host.Dude;
        if (dude is null)
            return;
        foreach (MapObject critter in _host.CombatCritters.Where(o => !_hostiles.Contains(o)).ToList())
        {
            if (!WantToJoin(critter, dude))
                continue;
            critter.Maneuver = 0; // CRITTER_MANEUVER_NONE once joined (combat.cc:2907)
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

        // The dude's slot in the interleaved order is done — advance to the next
        // combatant (the engine's _combat() loop moving past gDude's _combat_turn).
        if (_host.Dude is { } d) // P77: stash the dude's leftover AP for his AC dodge until his next turn
            _currentAp[d] = _dudeAp;
        _orderIndex++;
        _phase = CombatPhase.EnemyTurn;
    }

    /// <summary>
    /// Build the round's interleaved turn order (dude + living hostiles + living party members),
    /// ported from combat.cc. ROUND 1 (<paramref name="firstRound"/>) places the attacker first, the
    /// defender second and the dude third (_combat_sequence_init) — the one who opened combat acts
    /// first, NOT by initiative. ROUNDS 2+ sort by Sequence descending, Luck as the tiebreak
    /// (_compare_faster in _combat_sequence). Knocked-out / disengaging critters are dropped (the
    /// engine moves them to the non-combatant list). The sort is STABLE for fully-tied critters (a
    /// documented divergence from the engine's unstable qsort — keeps the goldens reproducible).
    /// </summary>
    private void BuildTurnOrder(bool firstRound, MapObject? attacker, MapObject? defender)
    {
        _order.Clear();
        _actingEnemy = null;
        _actingAlly = null;
        MapObject? dude = _host.Dude;

        var combatants = new List<MapObject>();
        if (!_dudeSpectator && dude is not null && !dude.IsDead) // P73: a spectator dude isn't in the order
            combatants.Add(dude);
        foreach (MapObject h in _hostiles)
            if (!h.IsDead && !combatants.Contains(h))
                combatants.Add(h);
        foreach (MapObject a in _host.PartyMembers)
            if (!a.IsDead && !combatants.Contains(a))
                combatants.Add(a);

        if (firstRound)
        {
            void PlaceFirst(MapObject? o)
            {
                if (o is not null && combatants.Remove(o))
                    _order.Add(o);
            }
            PlaceFirst(attacker);
            PlaceFirst(defender);
            if (attacker != dude && defender != dude)
                PlaceFirst(dude);
            _order.AddRange(combatants); // the rest, in collection order

            // Task-2 port: ported from fallout2-ce src/combat.cc _combat_sequence_init (:3011-3017) — the
            // attacker/defender that opened this round stamp each other's whoHitMe via
            // `_critter_set_who_hit_me` (critter.cc:1285-1301), before the opening attack even resolves,
            // and regardless of whether either side is gDude — the `attacker != gDude && defender != gDude`
            // guard at :2995 gates ONLY the "place dude third in the combat list" block above (closes at
            // :3006); it does not reach the whoHitMe stamp at :3011-3017. That stamp is NOT a raw
            // assignment in the reference, so it is routed through `SetWhoHitMe` here — the same gated
            // helper `RegisterHit` uses — rather than writing `WhoHitMe` directly. DangerSource's target
            // ACQUISITION is now entirely whoHitMe/aiFindAttackers-driven (no "just pick nearest" fallback,
            // matching the reference); without this stamp a combat opened by a MISSED first attack would
            // leave the defender's whoHitMe unset (RegisterHit only fires on an actual hit) and it would
            // never find a target on its first turn.
            //
            // Routed through SetWhoHitMe even when a side is gDude (byte-faithful to the reference's own
            // unconditional call — `SetWhoHitMe` decides whether to WRITE) even though nothing in
            // Hexwaste currently reads the dude's own WhoHitMe for an AI decision (DangerSource only runs
            // for non-dude critters). A cross-team pair (every golden, every real fight) writes exactly as
            // before — SetWhoHitMe's gate only changes same-team behavior, where it now correctly REFUSES
            // to write (matching RegisterHit's existing same-team simplification) instead of writing
            // unconditionally. See ASameTeamHitNeverRegistersWhoHitMe.
            //
            // Kept behind the pre-existing "both non-null" guard (not the reference's two independent
            // null checks at :3011/:3015) — this task's scope is the same-team gate, not the null-arg
            // handling for the defender-less StartBrawl call (:2033); widening that is a separate change.
            if (attacker is not null && defender is not null)
            {
                SetWhoHitMe(attacker, defender);
                SetWhoHitMe(defender, attacker);
            }
        }
        else
        {
            // KO/disengaging critters don't act this round (combat_ai DISENGAGING maneuver / DAM_KNOCKED_OUT).
            _order.AddRange(combatants
                .Where(c => c == dude || (c.CombatResults & CriticalTables.DamKnockedOut) == 0
                    && (c.Maneuver & ManeuverDisengaging) == 0)
                .OrderByDescending(c => _host.GetCritterState(c)?.Sequence ?? 0)
                .ThenByDescending(c => _host.GetCritterState(c)?.Stat(CritterStat.Luck) ?? 0));
        }
        _orderIndex = 0;
        ResetAllCombatAp();
    }

    /// <summary>_combat_set_move_all (combat.cc:3206, called at the top of every round, :3425): every
    /// committed combatant's combat.ap → maxAp. So at round start a not-yet-acted critter carries full
    /// maxAp dodge (M2); as each acts, the leftover is captured at turn end. P77.</summary>
    private void ResetAllCombatAp()
    {
        foreach (MapObject c in _order)
            _currentAp[c] = _host.GetCritterState(c)?.MaxActionPoints ?? 5;
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
            // P113 (4.3): a script that aggroes the dude (opAttackComplex) has DECIDED to attack — the
            // dude is its danger source ungated by perception, so a blind/rear-facing ambusher still
            // engages. Byte-identical for goldens: the ambusher opens combat adjacent to the dude anyway.
            attacker.WhoHitMe = dude;
            AddJoiners();
            if (_host.GetCritterState(dude) is { } stats)
                ResetDudeAp(stats);
            // The ambusher opened combat → it acts first (round-1 attacker, dude as defender).
            BuildTurnOrder(firstRound: true, attacker, dude);
            _phase = CombatPhase.EnemyTurn;
            _host.Log($"The {_host.ObjectName(attacker)} attacks you!");
            _host.Transcript($"scripted-aggro: {_host.ObjectName(attacker)}@{attacker.HexTile} starts combat");
        }
        else if (_hostiles.Add(attacker))
        {
            attacker.WhoHitMeCid = -1;
            _host.Log($"The {_host.ObjectName(attacker)} joins the fight!");
        }
    }

    /// <summary>ported from fallout2-ce src/combat.cc _combat_should_end (:3339-3376): the AUTOMATIC
    /// end-of-combat test, run once per round from <c>_combat()</c>'s <c>} while (!_combat_should_end());</c>
    /// (:3446). It reads <c>_list_com</c> — the combatant list AFTER <c>_combat_sequence()</c> (:3023) has
    /// already evicted, that same round, every critter that is dead (:3030-3042) or is knocked out /
    /// <c>CRITTER_MANEUVER_DISENGAGING</c> (:3044-3060, moved to the non-combatant list, from which
    /// <c>_combat_add_noncoms()</c> (:2899) may re-admit it later). So the predicate that decides who
    /// still counts as a live participant is <b>dead / KO / DISENGAGING</b> — and that is what this
    /// method applies to <c>_hostiles</c>. It is the SAME predicate <see cref="BuildTurnOrder"/> already
    /// applies when building <c>_order</c>, which is Hexwaste's stand-in for the post-eviction
    /// <c>_list_com</c>.
    ///
    /// NOT ported here: <c>_combatai_want_to_stop</c>. Its sole caller in the reference is
    /// <c>combatAttemptEnd</c> (combat.cc:3087) — the PLAYER's manual "leave combat" gate — and
    /// <c>_combat_should_end</c> never calls it. Folding it in here would add two terms vanilla never
    /// applies automatically: <c>ManeuverFleeing</c> (a fleer still inside <c>ai->max_dist</c> keeps
    /// FLEEING, not DISENGAGING, stays in <c>_list_com</c>, and the fight continues) and the perception
    /// term (vanilla keeps fighting an enemy that momentarily cannot see the dude). See
    /// <see cref="WantsToStopFighting"/>, which stays where the reference puts it:
    /// <see cref="TryEndCombat"/> alone.
    ///
    /// Hexwaste shape: the reference's team scan over the full <c>_list_com</c> ("end unless someone is
    /// on a team other than the dude's, or has a whoHitMe on the dude's team") is expressed here as
    /// "no live, non-evicted hostile remains" — <c>_hostiles</c> IS the dude-hostile set by
    /// construction, so the cross-team test is already satisfied by membership.</summary>
    private bool CombatShouldEnd() => _dudeSpectator
        // P73: a dude-absent brawl ends when one team (or none) is left standing. No reference
        // counterpart (vanilla's _list_com always contains gDude); a carried Hexwaste divergence.
        ? _hostiles.Where(h => !h.IsDead).Select(h => h.Team).Distinct().Count() <= 1
        // A knocked-out (but alive) hostile keeps blocking automatic end even though the reference
        // would have evicted it at :3044-3060 — pre-existing Hexwaste design (P14-M2): a KO critter
        // stays a live combat participant through its wake timer rather than letting the fight close
        // under it. That is the one deliberate departure from _combat_sequence's predicate here.
        : !_hostiles.Any(h => !h.IsDead && (IsKnockedOut(h) || (h.Maneuver & ManeuverDisengaging) == 0));

    private bool _terminateRequested;

    /// <summary>A script called terminate_combat (combat_p_proc) — end the fight at the next turn
    /// boundary, ported from fallout2-ce src/interpreter_extra.cc opTerminateCombat
    /// (_game_user_wants_to_quit = 1). No-op outside combat (P35-M5).</summary>
    public void RequestTerminateCombat()
    {
        if (_phase != CombatPhase.Idle)
            _terminateRequested = true;
    }

    private void EndCombat()
    {
        // Idempotent: StepTurnOrder() can itself end the fight (the MaxSpectatorBrawlRounds cap and the
        // per-round CombatShouldEnd() check) and return, after which UpdateCombat's own post-Step
        // CombatShouldEnd() is trivially true (_hostiles is empty) and would tear down a SECOND time —
        // duplicate wake/clear passes, a second ResetDudeAp, a second "Combat ends." log. The goldens
        // never caught it because Log is not transcripted. One teardown per fight.
        if (_phase == CombatPhase.Idle)
            return;

        // Force-wake every combatant so knockout never leaks past the fight
        // (combat.cc:2840 _combat_over → knockoutEventProcess); crippled/blind bits
        // persist on CombatResults (a Doctor clears them).
        _events.ClearAll();
        foreach (MapObject c in _hostiles.Concat(_host.PartyMembers).Append(_host.Dude!).Where(c => c is not null).Distinct())
        {
            c.CombatResults &= ~(CriticalTables.DamKnockedOut | CriticalTables.DamLoseTurn);
            // Minor-4 fix (Task-2 review): LastAttackTarget is a live MapObject reference stamped on
            // every attack/throw/burst resolve (aiInfoSetLastTarget, combat.cc:3558) but, unlike
            // WhoHitMe (:1642 party-only clear, plus the fresh-per-fight seed at StartBrawl), it was
            // never cleared — a stale-handle shape this project has fixed before (cf. P126). Currently
            // feeds only the unreached WhoeverAttackingMe branch, so this is a latent-bug close, not an
            // observed one; no reference counterpart to cite for "clear at combat end" since the
            // reference re-derives its own combat-list-index lookup (combat.cc:2101/2125-2133) rather
            // than holding a raw pointer across fights.
            c.LastAttackTarget = null;
        }
        _knockedDown.Clear();
        _aiLastItem.Clear();
        _terminateRequested = false; // P35-M5

        _phase = CombatPhase.Idle;
        _hostiles.Clear();
        _order.Clear();
        _currentAp.Clear();
        _actingEnemy = null;
        _actingAlly = null;
        if (_host.Dude is { } dude && _host.GetCritterState(dude) is { } stats)
            ResetDudeAp(stats);
        _host.Log("Combat ends.");

        // P73: the dude earns no XP from a brawl he wasn't part of (faithful — he didn't fight).
        bool spectator = _dudeSpectator;
        _dudeSpectator = false;
        if (_xpPending > 0)
        {
            if (!spectator)
                _host.AwardXp(_xpPending);
            _xpPending = 0;
        }
    }

    /// <summary>Player-initiated end-combat gate — ported from fallout2-ce src/combat.cc combatAttemptEnd
    /// (:3075): refuse to leave while any live hostile still WANTS TO FIGHT. Returns true iff combat
    /// actually ended (the caller shows combat.msg #103 on false). Distinct from Reset() (the debug/map-load
    /// hard teardown) — this is what the ENDCOMBAT button must call.</summary>
    public bool TryEndCombat()
    {
        if (_phase == CombatPhase.Idle)
            return true;
        if (_phase == CombatPhase.GameOver)
            return false;
        if (_hostiles.Any(h => !WantsToStopFighting(h)))
            return false; // an enemy still engaged — combat.cc:3086 message #103, no end
        EndCombat();
        return true;
    }

    /// <summary>ported from fallout2-ce src/combat_ai.cc _combatai_want_to_stop (:3211): a critter stops
    /// fighting (does NOT block the player leaving combat) if it is DISENGAGING (:3215), dead/KO (:3219),
    /// FLEEING (:3223), or it has no danger source it can still perceive (:3227-3228,
    /// <c>enemy == nullptr || !isWithinPerception(a1, enemy)</c>).
    ///
    /// The last term is a <see cref="DangerSource"/> call in the reference — NOT a hardcoded "can it see
    /// the dude or his party". That distinction matters: with a hardcoded dude-side, two hostile teams
    /// brawling out of the dude's perception would all report "wants to stop". Now that
    /// <c>_ai_danger_source</c> is ported, this is the real thing.
    ///
    /// The ONE port of this reference function in the codebase, and — matching the reference, whose only
    /// caller is <c>combatAttemptEnd</c> (combat.cc:3087) — its only consumer is
    /// <see cref="TryEndCombat"/>, the player's manual exit gate. The AUTOMATIC end check
    /// (<see cref="CombatShouldEnd"/>) deliberately does NOT use it; see that method's note.
    ///
    /// Caveat carried from <see cref="CheckBadShot"/>: DangerSource reads <see cref="_actingEnemyAp"/>
    /// for its AP gate, which is only meaningful for the critter currently acting — the reference has the
    /// same property (it reads the critter's live <c>combat.ap</c>) and <c>combatAttemptEnd</c> calls it
    /// off-turn just the same, so this is faithful rather than a Hexwaste soft spot.</summary>
    private bool WantsToStopFighting(MapObject h)
    {
        if (h.IsDead || IsKnockedOut(h))
            return true;
        if ((h.Maneuver & (ManeuverDisengaging | ManeuverFleeing)) != 0)
            return true;
        // :3227-3228 — the danger source is whatever _ai_danger_source finds for THIS critter, and the
        // perception check is against THAT, not against the dude.
        return DangerSource(h) is not { } enemy || !WithinPerception(h, enemy);
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
    public void Kill(MapObject critter, MapObject? killer = null)
    {
        KillCritter(critter, killer);
        // P108: an out-of-combat scripted/harness kill pays its XP now — otherwise _xpPending
        // strands until an unrelated combat ends and pays a windfall (mirrors the blast path).
        if (_xpPending > 0 && _phase == CombatPhase.Idle)
        {
            _host.AwardXp(_xpPending);
            _xpPending = 0;
        }
    }

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

        // A combat_p_proc called terminate_combat → end the fight now (P35-M5).
        if (_terminateRequested)
        {
            EndCombat();
            return;
        }

        // Task 3 (correction): let the currently-active combatant's own turn run BEFORE asking
        // CombatShouldEnd() whether anyone still wants to fight. WantsToStopFighting reads live
        // Maneuver/KO/perception state that a critter's OWN turn this round is often what sets or
        // resolves (TryFlee stamping FLEEING/DISENGAGING, the KO-forfeit branch, a script presetting
        // the flee maneuver before combat even opens) — asking first would judge that state before
        // the actor ever got to act on it. The original PruneEscapedHostiles ran its distance-only
        // test before StepTurnOrder() safely, because raw position never changes except through an
        // already-resolved move; WantsToStopFighting's richer state does not have that property, so
        // the two calls swap places here.
        if (_phase == CombatPhase.EnemyTurn)
            StepTurnOrder();

        // _phase guard: StepTurnOrder() may already have ended the fight (see EndCombat's idempotency
        // note) — don't re-evaluate a trivially-true CombatShouldEnd() over an emptied _hostiles.
        if (_phase != CombatPhase.Idle && CombatShouldEnd())
        {
            EndCombat();
            return;
        }
    }

    // HISTORY (Task 3, then corrected twice): this used to be PruneEscapedHostiles(), which ran a
    // FLAT ~20-hex sight-distance test over _hostiles every Step and physically evicted whoever was
    // past it. THAT GATE was the invention — nothing in e97087b decides participation by a fixed
    // hex radius. The eviction itself is NOT an invention, and an earlier revision of this comment
    // was wrong to say the reference "never removes anyone from the fight": _combat_sequence()
    // (combat.cc:3023, called once per round from _combat()'s loop at :3443) removes dead critters
    // (:3030-3042) and moves knocked-out / CRITTER_MANEUVER_DISENGAGING critters to the
    // non-combatant list (:3044-3060), from which _combat_add_noncoms() (:2899) can re-admit them
    // via _combatai_want_to_join. Evict-and-re-add every round IS the reference's architecture, and
    // _ai_run_away (combat_ai.cc:1183/1216) is what sets DISENGAGING — precisely when a fleeing
    // critter is at or past its packet's ai->max_dist, i.e. the reference's own "a hostile that
    // escaped leaves the fight" mechanism, which the flat prune was crudely approximating.
    //
    // So the evict/rejoin oscillation traced live on denbus2-fight-flee ("@9274 ... enemy=null" on
    // every round, from AddJoiners() adding a fresh hostile without stamping its WhoHitMe) was
    // evidence that the GATE was wrong, not that mutation was wrong.
    //
    // The shipped shape: Hexwaste does not maintain a second, mutable combatant list — _order (built
    // by BuildTurnOrder) already applies _combat_sequence's own dead/KO/DISENGAGING predicate, and
    // CombatShouldEnd() applies the same predicate to _hostiles, which is what _combat_should_end
    // reads the post-eviction _list_com for. _combatai_want_to_stop stays where the reference puts
    // it — TryEndCombat(), the player's manual exit gate — and is NOT part of the automatic check.

    /// <summary>
    /// Step the INTERLEAVED round order one combatant at a time (ported from combat.cc _combat()'s
    /// <c>for (; curIndex &lt; _list_com; curIndex++) _combat_turn(_combat_list[curIndex])</c>): finish
    /// an in-flight NPC action, then advance to the next actor. The dude's slot pauses the machine in
    /// PlayerTurn (the engine's blocking _combat_turn(gDude) → _combat_input); an NPC slot auto-resolves
    /// via TryEnemyAction/TryAllyAction. When the order is exhausted, start the next round and re-sort.
    /// </summary>
    private void StepTurnOrder()
    {
        MapObject? dude = _host.Dude;

        // Continue an NPC action that spans Step calls (a walk/attack mid-animation).
        if (_actingEnemy is { } ae)
        {
            if (!ae.IsDead && TryEnemyAction(ae))
                return;
            _currentAp[ae] = _actingEnemyAp; // P77: leftover AP boosts its AC until its next turn
            _actingEnemy = null;
            _orderIndex++;
        }
        else if (_actingAlly is { } aa)
        {
            if (!aa.IsDead && TryAllyAction(aa))
                return;
            _currentAp[aa] = _actingAllyAp;
            _actingAlly = null;
            _orderIndex++;
        }

        // Walk the order to the next actor that can act.
        while (true)
        {
            if (_orderIndex >= _order.Count)
            {
                // P73: a dude-absent brawl has no dude slot to pause this loop, so a STALEMATE
                // (two factions that can't reach each other → every actor passes) would spin
                // StartNewRound forever. Cap it: end the brawl once it runs this long (a draw if
                // both teams still stand). Also bounds a pathologically slow fight. Dude-involved
                // combat is unaffected (the dude slot always returns) → byte-identical.
                if (_dudeSpectator && _round >= MaxSpectatorBrawlRounds)
                {
                    EndCombat();
                    return;
                }
                StartNewRound();
                if (_order.Count == 0)
                    return; // nothing left (CombatShouldEnd guards the real end)
                // Task 3 (correction, round 2): ported from fallout2-ce src/combat.cc _combat()'s
                // own round loop — `} while (!_combat_should_end());` (:3446) — checked once per
                // round, right after the round transition (_combat_sequence()/StartNewRound here),
                // BEFORE processing the new round's actors. Without this, a round with nothing left
                // to do (every remaining actor's TryEnemyAction/TryAllyAction returns false — e.g.
                // the opposing team is already fully eliminated) falls straight through every actor,
                // back to the top of this while(true), and into ANOTHER StartNewRound() — all inside
                // this single StepTurnOrder() call, with no return to the caller in between. The
                // caller-side CombatShouldEnd() check below UpdateCombat's StepTurnOrder() call never
                // gets a chance to run until this loop itself gives up — which, for a dude-absent
                // brawl, is only the unrelated MaxSpectatorBrawlRounds stalemate cap. Traced live: an
                // already-decided fight (team 1 fully dead) spun from round 7 to round 100 with the
                // EXACT SAME sequence of prior attack transcripts, byte-identical up to that point —
                // confirming this was a control-flow gap, not a fidelity/perception issue.
                if (CombatShouldEnd())
                {
                    EndCombat();
                    return;
                }
            }

            MapObject actor = _order[_orderIndex];
            if (actor.IsDead)
            {
                _orderIndex++;
                continue;
            }

            if (actor == dude)
            {
                // The dude's slot — incapacitated dudes forfeit it (the wake fires as rounds advance),
                // otherwise pause in PlayerTurn for input (the engine's blocking _combat_turn(gDude)).
                if (!CanAct(dude!))
                {
                    SkipTurnIfIncapacitated(dude!);
                    _host.Transcript($"dude-skip: round {_round}");
                    _orderIndex++;
                    continue;
                }
                if (_host.GetCritterState(dude!) is { } stats)
                {
                    ResetDudeAp(stats);
                    if (StandUpIfProne(dude!, _dudeAp) is var afterStand && afterStand >= 0)
                        _dudeAp = afterStand; // the dude stands at the cost of 3 AP
                }
                _phase = CombatPhase.PlayerTurn;
                _host.Log($"Round {_round} — your turn (AP {_dudeAp}).");
                return;
            }

            // An NPC slot: a party ally or a hostile (different targeting logic).
            _phase = CombatPhase.EnemyTurn;
            if (_host.PartyMembers.Contains(actor))
            {
                _actingAlly = actor;
                _actingAllyAp = _host.GetCritterState(actor)?.MaxActionPoints ?? 5;
                if (TryAllyAction(actor))
                    return;
                _currentAp[actor] = _actingAllyAp; // P77
                _actingAlly = null;
            }
            else
            {
                _actingEnemy = actor;
                _actingEnemyAp = _host.GetCritterState(actor)?.MaxActionPoints ?? 5;
                if (TryEnemyAction(actor))
                    return;
                _currentAp[actor] = _actingEnemyAp; // P77
                _actingEnemy = null;
            }
            _orderIndex++;
        }
    }

    /// <summary>Advance to the next round (combat.cc _combat_sequence at the bottom of the round loop):
    /// tick the combat clock + fire due knockout wakes (P14-M2), let joiners in, then rebuild the
    /// Sequence-sorted turn order.</summary>
    private void StartNewRound()
    {
        _round++;
        _combatTick += TicksPerRound;
        _events.Process(_combatTick, OnCombatEvent);
        AddJoiners();
        BuildTurnOrder(firstRound: false, null, null);
    }

    /// <summary>One AI action: punch when adjacent, else an AP-budgeted approach
    /// at 1 AP per hex (the engine's combat_ai movement budget).</summary>
    /// <summary>The AI heal loop (_ai_check_drugs healing branch, combat_ai.cc:999-1027): a BIPED enemy
    /// below its chem_use HP ratio quaffs healing items (host-side) while it has the AP (2 each), until
    /// healthy or out of items/AP. BODY_TYPE_BIPED == 0 (proto_types.h) — quadruped scorpions never heal,
    /// so the arcaves combat goldens are unaffected. Enemies only (the dude/allies heal via the UI).</summary>
    private void TryAiHeal(MapObject enemy, AiPacket ai, CritterState st)
    {
        if (st.Proto.BodyType != 0) // 0 = BODY_TYPE_BIPED — only bipeds chem up
            return;
        // Healing branch (P42): below the chem_use HP ratio, quaff healing items while AP lasts.
        bool healed = false;
        int ratio = AiHealing.HealHpRatio(ai.ChemUse);
        if (ratio > 0)
        {
            int minHp = st.MaxHp * ratio / 100;
            while (enemy.CurrentHp < minHp && _actingEnemyAp >= 2 && _host.TryNpcHeal(enemy))
            {
                _actingEnemyAp -= 2;
                healed = true;
            }
        }
        // Non-healing combat-drug branch (P78-M2, combat_ai.cc:1028): only when it didn't just heal, roll
        // the per-mode chem_use chance and quaff chem_primary_desire buff drugs (2 AP each), capped per mode.
        // ShouldUse short-circuits without drawing for a clean enemy → the golden fights are byte-identical.
        if (!healed && AiCombatDrug.ShouldUse(ai.ChemUse, _round, _rng))
        {
            int used = 0, max = AiCombatDrug.MaxPerTurn(ai.ChemUse);
            while (_actingEnemyAp >= 2 && used < max && _host.TryNpcUseCombatDrug(enemy, ai.ChemPrimaryDesire))
            {
                _actingEnemyAp -= 2;
                used++;
            }
        }
    }

    /// <summary>P78-M3: is a LIVING same-team critter exactly on the hex line between the shooter and the
    /// target (so a ranged shot would pass through it)? An exact-collinear approximation of the engine's
    /// _combat_safety_invalidate_weapon LoF-tile scan (combat.cc:2249). Only consulted for gun shots, so
    /// melee golden enemies never reach it → byte-identical.</summary>
    private bool FriendlyOnFireLine(MapObject shooter, int targetTile) =>
        _hostiles.Any(h => h != shooter && !h.IsDead && h.Team == shooter.Team
            && HexGrid.IsOnSegment(shooter.HexTile, h.HexTile, targetTile));

    /// <summary>
    /// _ai_switch_weapons → _ai_search_inven_weap (combat_ai.cc:2596/2002): the wielded weapon is
    /// unusable (here: a dry gun with no reload), so scan the critter's CARRIED weapons for the best
    /// one its ai.txt <c>best_weapon</c> preference allows and wield it. Returns the new weapon, or
    /// (null, null) for fists when nothing qualifies (the engine's punch fallback). Only BIPED/ROBOTIC
    /// bodies search inventory (combat_ai.cc:2004); others keep fists. The ground-retrieval fallback
    /// (_ai_search_environ, combat_ai.cc:2178) is stricter still — BIPED only.
    ///
    /// DOCUMENTED SIMPLIFICATIONS vs the engine: the avg-damage score applies the explosive-radius
    /// ×(extras+1) factor (ExplosionExtrasAt) only when a <paramref name="defender"/> is supplied —
    /// wired for the ENEMY path (TryEnemyAction/ProbeAiWeaponSwitch pass the dude as defender); the
    /// ALLY path (TryAllyAction/ProbeAllyWeaponSwitch) passes no defender, so companions never get the
    /// blast-radius boost even though the reference runs the same _ai_best_weapon for any AI-controlled
    /// combatant (combat_ai.cc:3060-3150 → _ai_try_attack → _ai_switch_weapons → _ai_best_weapon) —
    /// see docs/BACKLOG.md. The weapon-perk ×2 factor IS applied (AiBestWeapon.AvgDamage);
    /// _combat_safety_invalidate_weapon (ally-in-line-of-fire / over-range "ignore") is not applied
    /// (Ignore stays false); ranged ammo availability now searches the carried inventory's calibers
    /// (CarriedAmmoCalibers), matching aiHaveAmmo; art-exists is assumed. Wired at three triggers: dry
    /// gun with no reload, a crippled arm making the wielded weapon unusable, and
    /// already-unarmed-and-out-of-range (combat_ai.cc:2800/2823 — the enemy-attack path in TryEnemyAction).
    /// </summary>
    // Enemy entry: reads best_weapon + min_to_hit from the ai.txt packet.
    private (ProtoInfo?, MapObject?) AiSwitchWeapon(MapObject enemy, AiPacket? ai, int distance, MapObject? currentItem,
        MapObject? defender = null) =>
        AiSwitchWeapon(enemy, ai?.BestWeapon ?? -1, ai?.MinToHit ?? 0, distance, currentItem, defender);

    /// <summary>ported from fallout2-ce src/item.cc weaponGetDamageRadius (:1975-1995): a ranged
    /// single-shot explosion weapon uses the rocket radius (3), a thrown grenade the grenade radius
    /// (2), everything else 0 (item.cc:3376-3377 — engine globals, not proto fields). weaponIsGrenade
    /// is damage type EXPLOSION / PLASMA / EMP (item.cc:1968-1972, proto_types.h:59-67: NORMAL 0,
    /// LASER 1, FIRE 2, PLASMA 3, ELECTRICAL 4, EMP 5, EXPLOSION 6).
    ///
    /// The "fire single" test is NOT <c>AnimationCode</c> — that field is the held-weapon-sprite
    /// selector (weaponGetAnimationCode, WEAPON_ANIMATION_* in art.h:91-101; 1 = WEAPON_ANIMATION_KNIFE),
    /// unrelated to attack animation. The reference compares weaponGetAnimationForHitMode(...) against
    /// ANIM_FIRE_SINGLE, which _attack_anim[extendedFlags &amp; 0xF] (item.cc:116-126) maps from nibble
    /// index 6 — the same nibble WeaponClass.AttackType already reads to get RANGED for that index.</summary>
    private static int WeaponDamageRadius(ProtoInfo proto, int attackType)
    {
        if (proto.Weapon is not { } w)
            return 0;
        bool blastDamage = w.DamageType is 6 /* EXPLOSION */ or 3 /* PLASMA */ or 5 /* EMP */;
        bool fireSingle = (proto.ExtendedFlags & 0xF) == 6; // ANIM_FIRE_SINGLE (item.cc:116-126)
        if (attackType == WeaponClass.AttackRanged && fireSingle && w.DamageType == 6 /* EXPLOSION */)
            return 3; // gRocketExplosionRadius
        if (attackType == WeaponClass.AttackThrow && blastDamage)
            return 2; // gGrenadeExplosionRadius
        return 0;
    }

    /// <summary>ported from fallout2-ce src/combat_ai.cc _ai_best_weapon (:1859-1862): how many EXTRA
    /// victims a blast at the defender's tile would catch — the engine calls
    /// _compute_explosion_on_extras with noDamage = 1 purely to read extrasLength. Counting only; no
    /// damage, no RNG. Returns 0 for a non-blast weapon or a null defender.
    ///
    /// The spiral itself is ALWAYS bounded by the grenade radius (2), never the rocket radius (3),
    /// regardless of which one <see cref="WeaponDamageRadius"/> returns as its non-zero gate. Traced
    /// the full call chain: <c>_ai_best_weapon</c> only checks <c>weaponGetDamageRadius(...) &gt; 0</c>
    /// (combat_ai.cc:1860) to decide whether to run the count at all; the radius that actually bounds
    /// the spiral is the <c>isGrenade</c> argument threaded into <c>_compute_explosion_on_extras</c>
    /// (combat.cc:4033-4039), and the AI passes <c>isGrenade = weaponIsGrenade(weapon1)</c> — a
    /// damage-TYPE-only test (EXPLOSION / PLASMA / EMP, item.cc:1968-1972) with no animation gate, so
    /// it is true for a fire-single rocket launcher exactly as much as for a thrown grenade. Every
    /// weapon that clears the <c>&gt; 0</c> gate is therefore "a grenade" for this AI-scoring purpose,
    /// and the walk is bounded by <c>gGrenadeExplosionRadius = 2</c> (item.cc:3376), never 3. The
    /// genuine 2-vs-3 split lives only in the real damage path (<see cref="Explode"/>,
    /// combat.cc:3831-3836), which additionally requires <c>ANIM_THROW_ANIM</c> and is out of scope
    /// here.
    ///
    /// The attacker's own tile is excluded from the count: the reference's spiral scan special-cases
    /// <c>obstacle == attack->attacker</c> and routes it to the backwash branch instead of
    /// <c>attack->extras[]</c> (combat.cc:4056-4060), so a self-adjacent attacker never inflates its
    /// own extrasLength.
    ///
    /// AI-SCORING-ONLY DIVERGENCES from the reference (combat.cc:4053-4055), none applied here: the
    /// <c>_combat_is_shot_blocked</c> line-of-sight test (which <see cref="Explode"/> DOES apply to its
    /// damage victims), the <c>OBJECT_SHOOT_THRU</c> flag test, and the attacker's own elevation as a
    /// filter on candidate tiles. Also note <c>_host.CombatCritters</c> excludes the dude (see
    /// <c>ViewerGame.CombatHost.cs</c> around :984), so when an enemy scores a blast against a
    /// companion, the player standing nearby is never counted as an extra victim.</summary>
    private int ExplosionExtrasAt(ProtoInfo proto, int attackType, MapObject? defender, MapObject attacker)
    {
        int gate = WeaponDamageRadius(proto, attackType); // combat_ai.cc:1860 — a > 0 gate only
        if (gate <= 0 || defender is null)
            return 0;
        const int spiralRadius = 2; // gGrenadeExplosionRadius (item.cc:3376) — see doc comment above
        var occupied = new HashSet<int>();
        foreach (MapObject c in _host.CombatCritters)
            if (!c.IsDead && c != attacker)
                occupied.Add(c.HexTile);
        int extras = 0;
        foreach (int tile in ExplosionSpiral.Tiles(defender.HexTile, spiralRadius))
            if (occupied.Contains(tile) && ++extras == 6) // explosionGetMaxTargets (item.cc:3574)
                break;
        return extras;
    }

    // P51: the core, callable for an ALLY with a best_weapon VALUE (from CompanionAi.WeaponPref) instead
    // of an ai.txt packet — the same _ai_best_weapon switch the enemies run (combat_ai.cc:1894).
    private (ProtoInfo?, MapObject?) AiSwitchWeapon(MapObject enemy, int bestWeapon, int minToHit, int distance,
        MapObject? currentItem, MapObject? defender = null)
    {
        CritterState? self = _host.GetCritterState(enemy);
        int bodyType = self?.Proto.BodyType ?? -1;
        if (bodyType is not (0 or 2)) // BODY_TYPE_BIPED / BODY_TYPE_ROBOTIC
            return (null, null);

        int results = enemy.CombatResults;
        bool bothArmsCrippled =
            (results & CriticalTables.DamCripArmLeft) != 0 && (results & CriticalTables.DamCripArmRight) != 0;
        bool anyArmCrippled = (results & (CriticalTables.DamCripArmLeft | CriticalTables.DamCripArmRight)) != 0;

        // Fold seed = the unarmed "punch" option: UNARMED if punch reaches (dist ≤ 1) else NONE
        // (order 999); avg damage 0 (the engine leaves avgDamage1 = 0 for weapon1 == null).
        var best = new AiBestWeapon.Choice(
            distance <= 1 ? WeaponClass.AttackUnarmed : WeaponClass.AttackNone, AvgDamage: 0, Cost: 0);
        (ProtoInfo Proto, MapObject Item)? winner = null;

        if (!bothArmsCrippled)
        {
            foreach ((ProtoInfo proto, MapObject item) in _host.CritterInventoryWeapons(enemy))
            {
                if (item == currentItem || proto.Weapon is not { } weapon)
                    continue;
                int attackType = WeaponClass.AttackType(proto.ExtendedFlags);

                // _ai_can_use_weapon (combat_ai.cc:1972): two-handed gate, skill ≥ min_to_hit, pref match.
                if (anyArmCrippled && WeaponProtoStats.IsTwoHanded(proto.ExtendedFlags))
                    continue;
                if (self is not null
                    && self.SkillValue(WeaponClass.Skill(proto.ExtendedFlags, weapon.DamageType)) < minToHit)
                    continue;
                if (!AiBestWeapon.HasWeapPrefType(bestWeapon, attackType))
                    continue;
                // _ai_can_use_weapon's ammo gate (combat_ai.cc:2036 → aiHaveAmmo, :1765): a ranged weapon
                // qualifies with rounds loaded OR matching ammo in the bag (the engine searches inventory;
                // the pre-port approximation was loaded-rounds only).
                if (attackType == WeaponClass.AttackRanged && _host.WeaponAmmo(proto, item) <= 0
                    && !_host.CarriedAmmoCalibers(enemy).Contains(weapon.Caliber))
                    continue;

                var cand = new AiBestWeapon.Choice(attackType,
                    AiBestWeapon.AvgDamage(weapon.MinDamage, weapon.MaxDamage, weapon.WeaponPerk,
                        ExplosionExtrasAt(proto, attackType, defender, enemy)),
                    proto.Cost, IsFlare: proto.Pid == 79);
                bool favorB = bestWeapon == 7 && _rng.Next(1, 101) <= 50; // RANDOM coin (inert on slice)
                if (AiBestWeapon.Prefers(bestWeapon, best, cand, favorB))
                {
                    best = cand;
                    winner = (proto, item);
                }
            }
        }

        if (winner is { } w)
        {
            _host.EquipWeapon(enemy, w.Item);
            _aiLastItem.Remove(enemy);
            return (w.Proto, w.Item);
        }

        // _ai_switch_weapons (combat_ai.cc:2606) → _ai_search_environ (combat_ai.cc:2178): nothing usable
        // in the bag → look for a weapon lying on the ground within PE+5 hexes, walk to it, pick it up and
        // wield it. _ai_search_environ opens with its OWN stricter gate — BIPED only (unlike the bag search
        // above, which also admits ROBOTIC per _ai_search_inven_weap, combat_ai.cc:2004-2006). Both-arms-
        // crippled is ALSO a hard no here: _ai_can_use_weapon's FIRST check (combat_ai.cc:1974-1977) rejects
        // every weapon before the two-handed gate even runs, so a both-arms-crippled critter can never wield
        // a weapon picked up off the ground either, not just one already in its bag.
        if (bodyType != 0 || bothArmsCrippled) // BODY_TYPE_BIPED
            return (null, null);

        // A remembered item may have been claimed by someone else since last turn — the closest reference
        // analogue is _ai_retrieve_object's item->owner check (combat_ai.cc:2250), which drops the item
        // rather than retrying it once someone else has it. Hexwaste has no MapObject.Owner, so re-verify
        // the item is still lying on the ground before trusting the memory; TryRetrieveItem is also
        // hardened against this independently (belt-and-suspenders — see ViewerGame.CombatHost.cs).
        (ProtoInfo Proto, MapObject Item)? wanted =
            _aiLastItem.TryGetValue(enemy, out (ProtoInfo Proto, MapObject Item) remembered) ? remembered : null;
        if (wanted is { } stale && !_host.GroundItemsNear(enemy, int.MaxValue).Any(gi => gi.Item == stale.Item))
        {
            _aiLastItem.Remove(enemy);
            wanted = null;
        }
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
    }

    /// <summary>Important 2 (final review): the AiSwitchWeapon candidate gate admits a ranged weapon
    /// with an empty magazine when a matching caliber is carried (:2451-2453), but that weapon still
    /// cannot fire until reloaded. ported from fallout2-ce src/combat_ai.cc _ai_try_attack (:2731-2757):
    /// the reference loops _combat_check_bad_shot after every _ai_switch_weapons call and, on
    /// COMBAT_BAD_SHOT_NO_AMMO, reloads BEFORE ever attempting to fire — never a free/negative-ammo shot.
    /// Call this right after any AiSwitchWeapon result. Returns true when the turn was spent reloading
    /// (the caller should `return true` for this action); otherwise the weapon/item/isGun out params are
    /// cleared to fists when the switched-to gun could not be reloaded, so the caller never fires it dry.</summary>
    private bool TryReloadSwitchedGun(MapObject critter, ref ProtoInfo? weapon, ref MapObject? weaponItem, ref bool isGun, ref int actorAp)
    {
        if (!isGun || _host.WeaponAmmo(weapon!, weaponItem!) > 0)
            return false;
        if (actorAp >= RangedMath.ReloadApCost && _host.TryReload(critter, weapon!, weaponItem!))
        {
            actorAp -= RangedMath.ReloadApCost;
            return true;
        }
        // Can't reload (no AP, or no matching ammo actually retrievable) — not a usable weapon this turn.
        weapon = null;
        weaponItem = null;
        isGun = false;
        return false;
    }

    /// <summary>P43 harness: run the AI inventory weapon switch for <paramref name="enemy"/> as if its
    /// wielded weapon went dry, equipping + returning the chosen weapon's pid (-1 = fists fallback).
    /// Drives the real <see cref="AiSwitchWeapon"/> path (CritterInventoryWeapons → best_weapon fold →
    /// EquipWeapon) against <paramref name="target"/> for the distance term.</summary>
    public int ProbeAiWeaponSwitch(MapObject enemy, MapObject target)
    {
        AiPacket? ai = _host.GetAiPacket(enemy);
        int distance = HexGrid.Distance(enemy.HexTile, target.HexTile);
        (_, MapObject? curItem) = _host.EquippedWeapon(enemy);
        (ProtoInfo? proto, _) = AiSwitchWeapon(enemy, ai, distance, curItem, target);
        return proto?.Pid ?? -1;
    }

    /// <summary>P51: the ALLY best-weapon switch (CompanionAi.WeaponPref → the int AiSwitchWeapon overload),
    /// equipping + returning the chosen pid (-1 = fists). The companion analogue of ProbeAiWeaponSwitch.</summary>
    public int ProbeAllyWeaponSwitch(MapObject ally, int bestWeapon, int distance)
    {
        (_, MapObject? curItem) = _host.EquippedWeapon(ally);
        (ProtoInfo? proto, _) = AiSwitchWeapon(ally, bestWeapon, minToHit: 0, distance, curItem);
        return proto?.Pid ?? -1;
    }

    private bool TryEnemyAction(MapObject enemy)
    {
        MapObject? dude = _host.Dude;
        if (dude is null)
            return false;

        // Knocked out or losing the turn → forfeit it (combat.cc:3231).
        if (SkipTurnIfIncapacitated(enemy))
            return false;

        // Per-turn combat_p_proc hook (combat.cc:3243) — before standup + default AI; an override
        // cancels the whole turn (P35). Inside the !incapacitated branch like the engine.
        if (RunCombatProcOverridesTurn(enemy))
            return false;

        // Stand up first if prone (3 AP), then act with what's left.
        if (StandUpIfProne(enemy, _actingEnemyAp) is var stood && stood >= 0)
        {
            _actingEnemyAp = stood;
            if (_actingEnemyAp < 1)
                return false; // standing used the whole turn
        }

        // Task-2 port: the whole nearest-dude/party/cross-team + perception + help-shout + retaliation
        // prologue that used to live here is now a single DangerSource call, ported from fallout2-ce
        // src/combat_ai.cc _ai_danger_source (:1529-1705) — see its doc comment for the full order and
        // the documented exclusions/divergences (the CE previous-target block, the bad-shot soft spot,
        // the _dudeSpectator carry-over).
        MapObject? defenderObj = DangerSource(enemy);
        if (defenderObj is null) // P73: a spectator-brawl critter with no cross-team target left → pass
            return false;
        int dudeTile = defenderObj.HexTile;
        AiPacket? ai = _host.GetAiPacket(enemy);

        // P70: script-set flee — a critter whose script flagged the FLEEING maneuver
        // (critter_set_flee_state, 0x8152, wired P58) runs on its own turn. This is the FIRST
        // OR-clause of _combat_ai's flee gate (combat_ai.cc:3074, before min_hp/hurt_too_much —
        // order immaterial, all three OR into _ai_run_away). INERT by default: only a quest script
        // sets the bit and no slice golden critter does.
        // ported from fallout2-ce src/combat_ai.cc _combat_ai()
        if ((enemy.Maneuver & ManeuverFleeing) != 0)
            return TryFlee(enemy, dudeTile, ref _actingEnemyAp);

        // min_hp flee (RAW current HP, combat_ai.cc:3077): too wounded to fight.
        if (ai is { MinHp: > 0 } && (_host.GetCritterState(enemy)?.CurrentHp ?? int.MaxValue) < ai.MinHp)
            return TryFlee(enemy, dudeTile, ref _actingEnemyAp);

        // hurt_too_much flee (combat_ai.cc:3076): a crippled/blinded critter whose AI packet lists
        // that damage flag flees. INERT by default — HurtTooMuch defaults 0 and no slice golden enemy
        // carries a crip/blind bit on a turn it takes. (Order vs min_hp is immaterial — both OR into TryFlee.)
        // ported from fallout2-ce src/combat_ai.cc _combat_ai()
        if (ai is { HurtTooMuch: not 0 } && (enemy.CombatResults & ai.HurtTooMuch) != 0)
            return TryFlee(enemy, dudeTile, ref _actingEnemyAp);

        // chem_use: a hurt BIPED quaffs healing items before attacking (combat_ai.cc _ai_check_drugs,
        // after the flee gate, before _ai_try_attack). P42.
        if (ai is not null && _host.GetCritterState(enemy) is { } healSt)
            TryAiHeal(enemy, ai, healSt);

        (ProtoInfo? enemyWeapon, MapObject? enemyWeaponItem) = _host.EquippedWeapon(enemy);
        bool enemyGun = enemyWeapon?.Weapon is { } ew && ew.IsGun(enemyWeapon.ExtendedFlags);
        int enemyDistance = HexGrid.Distance(enemy.HexTile, dudeTile);

        // _ai_try_attack shape: reload-if-empty, approach if blocked/far, else
        // stand and shoot; switch to a carried backup (best_weapon) when dry, fists otherwise.
        bool drySwitched = false; // did the branch below already run AiSwitchWeapon this turn?
        if (enemyGun && _host.WeaponAmmo(enemyWeapon!, enemyWeaponItem!) <= 0)
        {
            if (_actingEnemyAp >= RangedMath.ReloadApCost
                && _host.TryReload(enemy, enemyWeapon!, enemyWeaponItem!))
            {
                _actingEnemyAp -= RangedMath.ReloadApCost;
                return true;
            }
            // Dry with no ammo: scan the inventory for the packet-preferred backup weapon and wield
            // it (_ai_switch_weapons → _ai_search_inven_weap, combat_ai.cc:2596). None → fists.
            (enemyWeapon, enemyWeaponItem) = AiSwitchWeapon(enemy, ai, enemyDistance, enemyWeaponItem, defenderObj);
            drySwitched = true;
            enemyGun = enemyWeapon?.Weapon is { } ew2 && ew2.IsGun(enemyWeapon.ExtendedFlags);
            // Important 2: the switch may have landed on ANOTHER gun that is itself empty — reload it
            // (or drop to fists) before ever computing an attack range/firing with it.
            if (TryReloadSwitchedGun(enemy, ref enemyWeapon, ref enemyWeaponItem, ref enemyGun, ref _actingEnemyAp))
                return true;
        }

        // P78-M4: an NPC with crippled arms can't wield its weapon (both arms → any weapon, one arm →
        // a two-handed weapon, combat.cc:5655) — drop to fists first, the symmetric counterpart of the
        // dude gate (P18-M2). Inert unless the dude has crippled an enemy's arm with an aimed shot.
        bool crippledBlock = enemyWeapon is not null && WeaponBlockedByCrippledArms(enemy, enemyWeapon) is not null;
        if (crippledBlock)
        {
            enemyWeapon = null;
            enemyWeaponItem = null;
            enemyGun = false;
        }

        int attackRange = enemyGun ? enemyWeapon!.Weapon!.MaxRange1
            : Math.Min(enemyWeapon?.Weapon?.MaxRange1 ?? 1, 2);
        int attackCost = enemyWeapon?.Weapon?.ApCost ?? CombatMath.PunchApCost;

        // _ai_try_attack (combat_ai.cc:2800): a crippled arm just made the wielded weapon unusable →
        // switch to whatever the critter can still use (one-handed / fists). Else (combat_ai.cc:2823):
        // already unarmed and out of range with the current weapon → try to arm ourselves before falling
        // back to moving closer. _combat_check_bad_shot (combat.cc:5643) returns ONE mutually exclusive
        // bad-shot reason per attempt — arm-crippled outranks out-of-range in that ordering — so this is
        // an if/else-if (mirroring the reason dispatch), not two independent triggers that could both
        // fire and double up the switch's RNG draw (best_weapon == 7 coin flip).
        bool switched = false;
        if (crippledBlock)
        {
            (enemyWeapon, enemyWeaponItem) = AiSwitchWeapon(enemy, ai, enemyDistance, enemyWeaponItem, defenderObj);
            switched = true;
        }
        // Minor (final review): when the dry-gun branch above already switched (and TryReloadSwitchedGun
        // cleared the result to fists), enemyWeapon is null here too — without the !drySwitched guard this
        // trigger would re-run the IDENTICAL AiSwitchWeapon call (re-selecting + re-EquipWeapon-ing the same
        // unusable gun before dropping it again), which is inert for combat math but makes the viewer play
        // a spurious draw animation (EquipWeapon → SetWieldedWeaponArt(animate: true)) for a weapon the NPC
        // immediately discards. Skipping it here is a no-op for every other path (drySwitched is false
        // whenever the branch above didn't run).
        else if (!drySwitched && enemyWeapon is null && enemyDistance > attackRange)
        {
            (enemyWeapon, enemyWeaponItem) = AiSwitchWeapon(enemy, ai, enemyDistance, enemyWeaponItem, defenderObj);
            switched = true;
        }
        if (switched)
        {
            enemyGun = enemyWeapon?.Weapon is { } ew3 && ew3.IsGun(enemyWeapon.ExtendedFlags);
            // Important 2: same re-check as the dry-gun switch above — don't let this switch land on
            // an unloaded gun and fire it unreloaded.
            if (TryReloadSwitchedGun(enemy, ref enemyWeapon, ref enemyWeaponItem, ref enemyGun, ref _actingEnemyAp))
                return true;
            attackRange = enemyGun ? enemyWeapon!.Weapon!.MaxRange1
                : Math.Min(enemyWeapon?.Weapon?.MaxRange1 ?? 1, 2);
            attackCost = enemyWeapon?.Weapon?.ApCost ?? CombatMath.PunchApCost;
        }

        int minToHit = ai?.MinToHit ?? 0;
        CritterState? self = _host.GetCritterState(enemy);
        CritterState? def = _host.GetCritterState(defenderObj);

        // Can it EVER clear min_to_hit (best case: point-blank, no crowd)? If not,
        // it can never land a shot — flee rather than flail (combat_ai.cc:2812).
        if (minToHit > 0 && self is not null && def is not null
            && ComputeToHit(self, def, enemyWeapon, enemyWeaponItem, 1, 0, false) < minToHit)
            return TryFlee(enemy, dudeTile, ref _actingEnemyAp);

        int enemyCritters = 0;
        bool shotBlocked = false;
        if (enemyGun && enemyDistance <= attackRange)
        {
            (MapObject? blocker, enemyCritters) = LineOfFire.Trace(
                enemy.HexTile, dudeTile, tile => _host.ShootBlockerAt(tile, enemy, defenderObj));
            // P78-M3: friendly-fire safety (_combat_safety_invalidate_weapon, combat.cc:2249) — don't take a
            // RANGED shot that passes through a living teammate. SIMPLIFICATION: an exact-collinear hex test
            // (the friend lies between us and the target) rather than the engine's full LoF-tile scan +
            // retarget; the enemy holds (approaches) instead. Inert on the slice (no enemy shoots past an ally).
            if (!shotBlocked && FriendlyOnFireLine(enemy, dudeTile))
                shotBlocked = true;
            shotBlocked = shotBlocked || blocker is not null;
        }

        // P68/P78-M3: the enemy honours its ai.txt distance preference (was parsed but never consumed for
        // enemies). SNIPE — a ranged sniper closed inside its preferred range backs away to reopen distance
        // instead of shooting point-blank (combat_ai.cc:3001 _cai_perform_distance_prefs). P78-M3 makes it a
        // MULTI-step retreat toward SnipeRange (was a one-step kite), AP-limited, stopping at a blocked hex.
        // No golden enemy is a sniper -> byte-identical.
        Distance distMode = AiDistanceMode.Parse(ai?.Distance);
        if (distMode == Distance.Snipe && enemyGun && enemyDistance < SnipeRange && _actingEnemyAp >= 1)
        {
            int awayRot = (HexGrid.RotationTo(enemy.HexTile, dudeTile) + 3) % 6;
            int dest = enemy.HexTile, taken = 0, budget = Math.Min(_actingEnemyAp, SnipeRange - enemyDistance);
            for (int s = 0; s < budget; s++)
            {
                int next = HexGrid.TileInDirection(dest, awayRot);
                if (next == dest || _host.IsBlocked(next))
                    break;
                dest = next;
                taken++;
            }
            if (taken > 0)
            {
                _actingEnemyAp -= taken;
                return _host.StartWalk(enemy, dest);
            }
        }

        if (enemyDistance <= attackRange && !shotBlocked)
        {
            int toHit = self is not null && def is not null
                ? ComputeToHit(self, def, enemyWeapon, enemyWeaponItem, enemyDistance, enemyCritters, false)
                : 0;
            if (toHit >= minToHit)
            {
                // P76-M1: try a BURST first (ai.txt area_attack_mode/secondary_freq, _ai_pick_hit_mode) —
                // it manages its own ApCost2; else a single shot. IsBurstWeapon short-circuits a single-mode
                // enemy BEFORE the decision roll, so the golden enemies (no burst weapon) are byte-identical.
                if (IsBurstWeapon(enemyWeapon) && ai is not null && self is not null && def is not null
                    && TryEnemyBurst(enemy, defenderObj, self, def, enemyWeapon!, enemyWeaponItem!, enemyDistance, enemyCritters, ai))
                    return true;
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

        // P68: DISTANCE_STAY holds position — it attacks if already in range (above) but never closes the
        // gap (combat_ai.cc:1223/2361, _ai_move_away/_ai_move_steps_closer return -1 for DISTANCE_STAY).
        // The golden enemies (scorpion pkt8 / peasant pkt14) carry NO distance field -> the engine default
        // -1 -> OnYourOwn here -> they approach as before -> byte-identical.
        if (distMode == Distance.Stay)
            return false;

        if (_actingEnemyAp < 1)
            return false;
        // P113 (Stage 4.1): combat approach paths route through closed usable doors and open them on
        // contact, like fo2ce (canUseDoor exempts them in _make_path for AI moves too, animation.cc:1802).
        byte[]? path = Pathfinder.FindPath(enemy.HexTile, dudeTile,
            tile => _host.IsBlocked(tile), t => _host.IsPassableClosedDoor(enemy, t));
        if (path is null || path.Length <= 1)
            return false;

        // Crippled legs cost 4×/8× AP per hex (critter.cc:1349); 1× otherwise → an
        // intact enemy's budget is unchanged (byte-identical).
        int costPerHex = CritterState.MovePointCost(enemy.CombatResults);
        // P117: the approach RUNS when the mover still has half its AP and its art says so —
        // ported from fallout2-ce combat_ai.cc:2424 _ai_move_steps_closer (actionPoints >=
        // maxAp/2 && artCritterFidShouldRun); gated on the PRE-move AP like the engine.
        bool approachRun = self is not null && _actingEnemyAp >= self.MaxActionPoints / 2
            && _host.CritterShouldRun(enemy);
        int steps = Math.Min(path.Length - 1, _actingEnemyAp / costPerHex); // stop adjacent
        _actingEnemyAp -= steps * costPerHex;
        int targetTile = enemy.HexTile;
        for (int i = 0; i < steps; i++)
            targetTile = HexGrid.TileInDirection(targetTile, path[i]);
        return _host.StartWalk(enemy, targetTile, approachRun);
    }

    /// <summary>Run away from a threat tile: greedily step to the unblocked
    /// neighbour that most increases distance, up to the AP budget (the engine's
    /// _ai_run_away, combat_ai.cc:2812 — our greedy hex-distance approximation).
    /// Returns false (and takes no turn) if hemmed in.</summary>
    // P50: takes the actor's AP by ref so BOTH the enemy turn (_actingEnemyAp) and an ally's
    // run-away (CompanionAi.ShouldFlee → _actingAllyAp) can flee through the same path.
    private bool TryFlee(MapObject critter, int threatTile, ref int actorAp)
    {
        if (actorAp < 1)
            return false;

        // ported from fallout2-ce src/combat_ai.cc _ai_run_away (:1183-1217): a critter already at or
        // beyond max_dist from its threat does NOT run — it sets CRITTER_MANEUVER_DISENGAGING (:1216)
        // and takes no movement and no AP, which is exactly what lets a flight terminate
        // (_combatai_want_to_stop returns true on the flag, :3215). Inside the threshold it is marked
        // CRITTER_MANUEVER_FLEEING (:1184) and runs. Before this gate the engine set NEITHER flag, so
        // every consumer of them was starved on an engine-initiated flight and a fleeing critter would
        // re-flee every turn forever with nothing to ever mark it done. No committed golden fixture
        // exercises this: in denbus2-fight-flee every fleeing critter's distance from its threat stays
        // at or below 8, never reaching its ai.txt packet's max_dist of 10 (measured directly). That
        // fixture's own repeated "flee: Cute Slave@11272 -> 10480" line (same tile every round) has a
        // different, pre-existing cause: TryFlee logs the flee line and calls StartWalk unconditionally,
        // but StartWalk's destination tile is itself occupied/blocked every round, so the walk never
        // actually starts and the critter never moves — a separate bug, not this gate's absence.
        // The comparison is '<', matching e97087b. The maintained fork's PR #675 flips it to '<=';
        // that hunk was rejected as ungrounded, so '<' is deliberate — do not "correct" it.
        // A null AI packet is a Hexwaste-only state (the reference always has one): keep the pre-gate
        // behaviour and flee, rather than inventing a default max_dist.
        AiPacket? ai = _host.GetAiPacket(critter);
        if (ai is not null && !(HexGrid.Distance(critter.HexTile, threatTile) < ai.MaxDist))
        {
            critter.Maneuver |= ManeuverDisengaging;
            _host.Transcript($"disengage: {_host.ObjectName(critter)}@{critter.HexTile}");
            return false; // the reference's empty else — no move, no AP, and the caller ends the turn
        }
        critter.Maneuver |= ManeuverFleeing;

        int fromTile = critter.HexTile;
        // ported from fallout2-ce combat_ai.cc _ai_run_away: head directly AWAY from the
        // threat (the rotation from threat→self), or ±1 rotation, as far as AP allows, via
        // a REAL path (_make_path) — not greedy neighbour-stepping that snags on obstacles.
        // Try the full-AP distance first, shrinking until a reachable retreat tile is found.
        // F18: the reference calls _make_path(a1, a1->tile, destination, nullptr, 1) — a5 = 1 requires
        // the DESTINATION itself to be free (combat_ai.cc:1192), not just the path leading to it. A
        // candidate that is itself occupied/blocked must be rejected so the loop shrinks to a nearer
        // free tile, rather than being accepted and then silently refused by the walker.
        int rotation = HexGrid.RotationTo(threatTile, fromTile);
        int target = -1;
        for (int dist = actorAp; dist > 0 && target < 0; dist--)
        {
            foreach (int dir in (ReadOnlySpan<int>)[rotation, (rotation + 1) % 6, (rotation + 5) % 6])
            {
                int dest = HexGrid.TileInDirection(fromTile, dir, dist);
                if (dest != fromTile && Pathfinder.FindPath(fromTile, dest, _host.IsBlocked,
                        t => _host.IsPassableClosedDoor(critter, t), requireFreeDestination: true) is not null) // P113 (4.1): flee through doors
                {
                    target = dest;
                    break;
                }
            }
        }

        if (target < 0)
            return false; // hemmed in — no reachable retreat

        actorAp = 0; // the run uses the whole turn (animationRegisterRunToTile, full ap)
        _host.Log($"The {_host.ObjectName(critter)} flees!");
        _host.Transcript($"flee: {_host.ObjectName(critter)}@{fromTile} -> {target}");
        _host.OnCritterFlee(critter); // P72-M3: flee taunt (Draw-only, isolated rng → byte-identical)
        // P117: _ai_run_away registers a RUN unconditionally (combat_ai.cc:1210) — no
        // shouldRun gate; the host falls back to walk when the run art is missing.
        return _host.StartWalk(critter, target, run: true);
    }

    /// <summary>How close a "stay close to me" companion keeps to the dude before regrouping (P50;
    /// combat_ai.cc _cai_perform_distance_prefs ~5 hexes).</summary>
    private const int AllyStayCloseHexes = 5;

    /// <summary>A companion's action, honouring its P50 combat-control settings (disposition / attack-who
    /// / run-away / distance / chem-use). The DEFAULT settings (Aggressive) resolve to the pre-P50
    /// behaviour — attack the nearest hostile, never flee, no distance constraint — so an un-configured
    /// ally is byte-identical to the old AI.</summary>
    private bool TryAllyAction(MapObject ally)
    {
        if (SkipTurnIfIncapacitated(ally))
            return false;

        // Per-turn combat_p_proc hook — the engine runs it for EVERY combatant (no party exclusion). P35.
        if (RunCombatProcOverridesTurn(ally))
            return false;

        if (StandUpIfProne(ally, _actingAllyAp) is var stood && stood >= 0)
        {
            _actingAllyAp = stood;
            if (_actingAllyAp < 1)
                return false;
        }

        CompanionAi ai = _host.CompanionSettings(ally).Effective(); // P50 disposition → effective knobs
        List<MapObject> hostiles = _hostiles.Where(h => !h.IsDead).ToList();
        if (hostiles.Count == 0)
            return false;

        CritterState? selfState = _host.GetCritterState(ally);

        // P70: script-set flee — the FLEEING maneuver bit (critter_set_flee_state) makes the ally run
        // too (combat_ai.cc:3074, _combat_ai runs for EVERY combatant). Checked before the disposition
        // run-away so a script override wins. Inert by default — no slice ally sets the bit.
        if ((ally.Maneuver & ManeuverFleeing) != 0 && _actingAllyAp >= 1)
        {
            int fleeTile = hostiles.OrderBy(h => HexGrid.Distance(ally.HexTile, h.HexTile)).First().HexTile;
            return TryFlee(ally, fleeTile, ref _actingAllyAp);
        }

        // P50 run-away: too wounded for this disposition → flee (combat_ai.cc:3077, the ally path).
        if (selfState is not null && _actingAllyAp >= 1
            && CompanionAi.ShouldFlee(ai.RunAway, selfState.CurrentHp, selfState.MaxHp))
        {
            int threatTile = hostiles.OrderBy(h => HexGrid.Distance(ally.HexTile, h.HexTile)).First().HexTile;
            return TryFlee(ally, threatTile, ref _actingAllyAp);
        }

        // P50 chem-use: quaff a healing item when hurt past the threshold (reuses the P42 host heal).
        if (selfState is not null && _actingAllyAp >= 2 && AllyShouldHeal(ai.ChemUse, selfState) && _host.TryNpcHeal(ally))
        {
            _actingAllyAp -= 2;
            return true;
        }

        // P50 attack-who: pick the target by priority. Closest (the default) == the old nearest-hostile.
        // WhoeverAttackingMe prefers the hostile that last hit this ally (ally.WhoHitMe, the per-critter
        // whoHitMe tracker added P101) — combat_ai.cc _ai_find_target's whoHitMe preference — and falls back
        // to Closest when nobody has (PickTarget). Ignored for the other modes, so the default is unchanged.
        List<(int Rating, int Distance, bool HitMe)> ranked = hostiles
            .Select(h => (Rating(h), HexGrid.Distance(ally.HexTile, h.HexTile),
                ReferenceEquals(ally.WhoHitMe, h)))
            .ToList();
        MapObject target = hostiles[CompanionAi.PickTarget(ai.AttackWho, ranked)];

        (ProtoInfo? weaponProto, MapObject? weaponItem) = _host.EquippedWeapon(ally);
        bool isGun = weaponProto?.Weapon is { } w && w.IsGun(weaponProto.ExtendedFlags);
        int distance = HexGrid.Distance(ally.HexTile, target.HexTile);

        // ported from fallout2-ce src/combat.cc _combat_check_bad_shot() (:5678-5683): the empty-weapon
        // refusal is gated on ammoGetCapacity(weapon) > 0, NOT on weapon class — the same gate
        // CheckBadShot already uses on the NPC side. Hexwaste's dude-side auto-reload here is a
        // pre-existing deviation from _combat_attack_this (:5738-5747) and is left as-is.
        if (UsesCharges(weaponProto) && _host.WeaponAmmo(weaponProto!, weaponItem!) <= 0)
        {
            if (_actingAllyAp >= RangedMath.ReloadApCost
                && _host.TryReload(ally, weaponProto!, weaponItem!))
            {
                _actingAllyAp -= RangedMath.ReloadApCost;
                return true;
            }
            // P51 best-weapon: a dry gun switches to the best CARRIED weapon for the companion's
            // preference (the enemies' _ai_best_weapon, P43, now reachable for an ally via WeaponPref) —
            // or fists when nothing else is carried (the pre-P51 slice behaviour → byte-identical).
            (weaponProto, weaponItem) = AiSwitchWeapon(ally, (int)ai.WeaponPref, minToHit: 0, distance, weaponItem);
            isGun = weaponProto?.Weapon is { } gw && gw.IsGun(weaponProto.ExtendedFlags);
            // Final review (companion-path parity): the ally-side counterpart of the enemy's Important-2
            // re-check above — CandidateGate/AiSwitchWeapon can land the ally on ANOTHER gun that is
            // itself empty (a matching caliber in CarriedAmmoCalibers is enough to qualify as a
            // candidate), so reload it here too before ever computing range/firing with it. Without this
            // an ally could fire a gun it never loaded and drive its AmmoQuantity negative, mirroring the
            // defect 4227b75 fixed on the enemy path only. ported from fallout2-ce src/combat_ai.cc
            // _ai_try_attack (:2731-2757).
            if (TryReloadSwitchedGun(ally, ref weaponProto, ref weaponItem, ref isGun, ref _actingAllyAp))
                return true;
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
            if (_host.GetCritterState(ally) is not { } attacker || _host.GetCritterState(target) is not { } defender)
                return false;

            // P51 area-attack: a burst-capable gun + a non-Never area-attack mode + the to-hit threshold →
            // BURST (the same _compute_spray + cone the dude runs). Default Never → skipped → byte-identical.
            if (isGun && IsBurstWeapon(weaponProto) && ai.AreaAttack != AreaAttack.Never
                && TryAllyBurst(ally, target, attacker, defender, weaponProto!, weaponItem!, distance, crittersInPath, ai.AreaAttack))
                return true;

            if (_actingAllyAp < apCost)
                return false;
            _actingAllyAp -= apCost;
            ally.Rotation = HexGrid.RotationTo(ally.HexTile, target.HexTile);
            (int chance, bool hit, int damage, int critFlags, int delta) = RollAttack(attacker, defender, weaponProto, weaponItem,
                distance, crittersInPath, attackerIsDude: false, defenderIsDude: false,
                AiHitLocation(ally, attacker, defender, weaponProto, weaponItem, distance, crittersInPath), DiffDmgMod(ally)); // P75-M4 + P84
            if (!hit && TriggerCritFailure(attacker, attackerIsDude: false, weaponProto, weaponItem, delta))
                _actingAllyAp = 0; // P41: a fumble can cost the ally its turn
            if (UsesCharges(weaponProto) && weaponItem is not null)
                weaponItem.AmmoQuantity = _host.WeaponAmmo(weaponProto!, weaponItem) - 1;
            _pendingAttack = new PendingAttack(ally, target, chance, hit, damage, critFlags, CanKnockback: !isGun,
                KnockbackPerk: weaponProto?.Weapon is { WeaponPerk: WeaponProtoStats.PerkKnockback }, // P74-M2
                DamageType: weaponProto?.Weapon?.DamageType ?? 0,
                AttackerAnim: DeathAnims.AttackAnimFor(isGun, weaponProto?.Weapon is not null));
            _host.Transcript($"ally-attack {_host.ObjectName(ally)} -> {_host.ObjectName(target)}@{target.HexTile}"
                + $"{(weaponProto is null ? "" : $" [{_host.ObjectNameByPid(weaponProto.Pid)}]")}: chance={chance}% hit={hit} damage={damage}{CritTag(critFlags)}");
            _host.OnAttackStarted(ally, target, weaponProto);
            return true;
        }

        // P50 distance preference for the approach: Stay holds position; StayClose regroups with the
        // dude when too far; Charge/OnYourOwn/Snipe close on the target (OnYourOwn = the old default;
        // Snipe's keep-distance back-away is a documented residual).
        if (ai.Distance == Distance.Stay)
            return false;
        int moveTo = target.HexTile;
        if (ai.Distance == Distance.StayClose && _host.Dude is { } leader
            && HexGrid.Distance(ally.HexTile, leader.HexTile) > AllyStayCloseHexes)
            moveTo = leader.HexTile;

        if (_actingAllyAp < 1)
            return false;
        byte[]? path = Pathfinder.FindPath(ally.HexTile, moveTo,
            tile => _host.IsBlocked(tile), t => _host.IsPassableClosedDoor(ally, t)); // P113 (4.1)
        if (path is null || path.Length <= 1)
            return false;
        // P117: allies approach through the same _ai_move_steps_closer, so the same
        // half-AP + shouldRun run gate applies (combat_ai.cc:2424).
        bool allyRun = _host.GetCritterState(ally) is { } allyState
            && _actingAllyAp >= allyState.MaxActionPoints / 2 && _host.CritterShouldRun(ally);
        int steps = Math.Min(path.Length - 1, _actingAllyAp);
        _actingAllyAp -= steps;
        int walkTarget = ally.HexTile;
        for (int i = 0; i < steps; i++)
            walkTarget = HexGrid.TileInDirection(walkTarget, path[i]);
        return _host.StartWalk(ally, walkTarget, allyRun);
    }

    /// <summary>P50: an ally heals when hurt past its chem-use threshold (combat_ai.cc _ai_check_drugs
    /// HealHpRatio mapping). Clean never heals (the default → byte-identical).</summary>
    private static bool AllyShouldHeal(ChemUse mode, CritterState st)
    {
        if (st.MaxHp <= 0 || mode == ChemUse.Clean)
            return false;
        int pct = st.CurrentHp * 100 / st.MaxHp;
        return mode switch
        {
            ChemUse.WhenHurtLittle => pct < 60,
            ChemUse.WhenHurtLots => pct < 30,
            ChemUse.Sometimes => pct < 50,
            ChemUse.Anytime => pct < 100,
            _ => false,
        };
    }

    /// <summary>P51: an ally fires a burst (area-attack), the same _compute_spray + cone the dude runs
    /// (TryBurst), but with the ally's AP + attackerIsDude:false. The area-attack mode gates whether the
    /// burst fires (the to-hit thresholds, _ai_pick_hit_mode); SOMETIMES rolls a 1/3 here (allies have no
    /// ai.txt secondary_freq — a documented fixed value). Returns false to fall back to the single shot.</summary>
    private bool TryAllyBurst(MapObject ally, MapObject target, CritterState attacker, CritterState defender,
        ProtoInfo weaponProto, MapObject weaponItem, int distance, int crittersInPath, AreaAttack mode)
    {
        int apCost = weaponProto.Weapon!.ApCost2 > 0 ? weaponProto.Weapon.ApCost2 : weaponProto.Weapon.ApCost;
        if (_actingAllyAp < apCost || _host.WeaponAmmo(weaponProto, weaponItem) <= 0)
            return false;
        int toHit = Math.Clamp(
            ComputeToHit(attacker, defender, weaponProto, weaponItem, distance, crittersInPath, attackerIsDude: false), 0, 95);
        bool fire = mode == AreaAttack.Sometimes ? _rng.Next(1, 4) == 1 : CompanionAi.ShouldAreaAttack(mode, toHit);
        if (!fire)
            return false;

        _actingAllyAp -= apCost;
        ally.Rotation = HexGrid.RotationTo(ally.HexTile, target.HexTile);
        int ammoBefore = _host.WeaponAmmo(weaponProto, weaponItem);
        (int acc, int fired, int hits, int total, List<BurstExtra> extras, bool loseTurn) = RollBurst(
            ally, target, attacker, defender, weaponProto, weaponItem, distance, crittersInPath, ammoBefore, attackerIsDude: false);
        // F26: a burst fumble can cost the ally the rest of its turn.
        if (loseTurn)
            _actingAllyAp = 0;
        _pendingBurst = new PendingBurst(ally, target, weaponProto, weaponItem, ammoBefore, fired, hits, total, extras);
        _host.Transcript($"ally-burst {_host.ObjectName(ally)} -> {_host.ObjectName(target)}@{target.HexTile}"
            + $" [{_host.ObjectNameByPid(weaponProto.Pid)} {ammoBefore}rnd d{distance}]: chance={acc}% rounds={fired} hit={hits} damage={total}");
        foreach (BurstExtra ex in extras)
            _host.Transcript($"burst-extra: {_host.ObjectName(ex.Victim)}@{ex.Victim.HexTile} hit={ex.RoundsHit} damage={ex.Damage}");
        _host.OnAttackStarted(ally, target, weaponProto);
        return true;
    }

    /// <summary>P76-M1: an enemy fires a burst (mirrors TryAllyBurst but ai.txt-driven via AiBurstMode +
    /// the enemy AP). Returns false → the caller falls through to the single shot. The decision rng draw
    /// only happens for a burst-capable weapon (the EnemyAttack short-circuit), so it never touches a
    /// no-burst golden enemy.</summary>
    private bool TryEnemyBurst(MapObject enemy, MapObject target, CritterState attacker, CritterState defender,
        ProtoInfo weaponProto, MapObject weaponItem, int distance, int crittersInPath, AiPacket ai)
    {
        int apCost = weaponProto.Weapon!.ApCost2 > 0 ? weaponProto.Weapon.ApCost2 : weaponProto.Weapon.ApCost;
        if (_actingEnemyAp < apCost || _host.WeaponAmmo(weaponProto, weaponItem) <= 0)
            return false;
        int toHit = Math.Clamp(
            ComputeToHit(attacker, defender, weaponProto, weaponItem, distance, crittersInPath, attackerIsDude: false), 0, 95);
        if (!AiBurstMode.ShouldBurst(ai, attacker.Stat(CritterStat.Intelligence), distance, toHit, _rng))
            return false;

        _actingEnemyAp -= apCost;
        enemy.Rotation = HexGrid.RotationTo(enemy.HexTile, target.HexTile);
        int ammoBefore = _host.WeaponAmmo(weaponProto, weaponItem);
        (int acc, int fired, int hits, int total, List<BurstExtra> extras, bool loseTurn) = RollBurst(
            enemy, target, attacker, defender, weaponProto, weaponItem, distance, crittersInPath, ammoBefore, attackerIsDude: false);
        // F26: a burst fumble can cost the enemy the rest of its turn.
        if (loseTurn)
            _actingEnemyAp = 0;
        _pendingBurst = new PendingBurst(enemy, target, weaponProto, weaponItem, ammoBefore, fired, hits, total, extras);
        _host.Transcript($"enemy-burst {_host.ObjectName(enemy)} -> {_host.ObjectName(target)}@{target.HexTile}"
            + $" [{_host.ObjectNameByPid(weaponProto.Pid)} {ammoBefore}rnd d{distance}]: chance={acc}% rounds={fired} hit={hits} damage={total}");
        foreach (BurstExtra ex in extras)
            _host.Transcript($"burst-extra: {_host.ObjectName(ex.Victim)}@{ex.Victim.HexTile} hit={ex.RoundsHit} damage={ex.Damage}");
        _host.OnAttackStarted(enemy, target, weaponProto);
        return true;
    }

    private void EnemyAttack(MapObject enemy, MapObject defenderObj, ProtoInfo? weaponProto,
        MapObject? weaponItem, int distance, int crittersInPath)
    {
        if (_host.Dude is null || _host.GetCritterState(enemy) is not { } attacker
            || _host.GetCritterState(defenderObj) is not { } defender)
            return;

        enemy.Rotation = HexGrid.RotationTo(enemy.HexTile, defenderObj.HexTile);

        bool isGun = weaponProto?.Weapon is { } w && w.IsGun(weaponProto.ExtendedFlags);
        (int chance, bool hit, int damage, int critFlags, int delta) = RollAttack(attacker, defender, weaponProto, weaponItem,
            distance, crittersInPath, attackerIsDude: false, defenderIsDude: defenderObj == _host.Dude,
            AiHitLocation(enemy, attacker, defender, weaponProto, weaponItem, distance, crittersInPath), DiffDmgMod(enemy)); // P75-M4 + P84
        if (!hit && TriggerCritFailure(attacker, attackerIsDude: false, weaponProto, weaponItem, delta))
            _actingEnemyAp = 0; // P41: a fumble can cost the enemy the rest of its turn
        if (UsesCharges(weaponProto) && weaponItem is not null)
            weaponItem.AmmoQuantity = _host.WeaponAmmo(weaponProto!, weaponItem) - 1;
        _pendingAttack = new PendingAttack(enemy, defenderObj, chance, hit, damage, critFlags, CanKnockback: !isGun,
            KnockbackPerk: weaponProto?.Weapon is { WeaponPerk: WeaponProtoStats.PerkKnockback }, // P74-M2
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
        _terminateRequested = false; // P35-M5
        _dudeSpectator = false; // P73
        _dudeFreeMove = 0; // P74-M4
        _hostiles.Clear();
        _order.Clear();
        _actingEnemy = null;
        _actingAlly = null;
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
