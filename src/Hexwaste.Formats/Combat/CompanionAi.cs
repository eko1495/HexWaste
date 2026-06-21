namespace Hexwaste.Formats.Combat;

/// <summary>Overall combat disposition (game_dialog.cc:385 the 5 radio buttons). Not "Custom"
/// PRESETS the detailed knobs; Custom uses the explicit AttackWho/Distance/RunAway.</summary>
public enum Disposition { Berserk, Aggressive, Defensive, Coward, Custom }

/// <summary>Target-selection priority (game_dialog.cc custom.msg 500-504; combat_ai.cc _ai_find_target).</summary>
public enum AttackWho { WhoeverAttackingMe, Strongest, Weakest, Whomever, Closest }

/// <summary>Movement preference vs the party leader / the target (custom.msg 400-404;
/// combat_ai.cc _cai_perform_distance_prefs).</summary>
public enum Distance { StayClose, Charge, Snipe, OnYourOwn, Stay }

/// <summary>The HP threshold at which to flee (custom.msg 200-205; combat_ai.cc run_away_mode → min_hp).
/// Hexwaste maps the engine's min_hp levels to HP fractions (a documented approximation).</summary>
public enum RunAway { AbjectCoward, FingerHurts, Bleeding, NotFeelingGood, Tourniquet, Never }

/// <summary>When to quaff a healing chem (custom.msg 600-604; combat_ai.cc _ai_check_drugs).</summary>
public enum ChemUse { Clean, WhenHurtLittle, WhenHurtLots, Sometimes, Anytime }

/// <summary>When to use a burst/secondary attack (custom.msg 100-104; combat_ai.cc:2287 _ai_pick_hit_mode —
/// the to-hit thresholds). Hexwaste adds NEVER (the default = the pre-P51 single-only ally) since the engine's
/// unset behaviour (-1) is INT/distance-gated and doesn't map to a window row.</summary>
public enum AreaAttack { Never, Sometimes, BeCareful, BeSure, BeAbsolutelySure, Always }

/// <summary>Best-weapon preference (custom.msg 300-307; combat_ai.cc:269 _weapPrefOrderings). The values MATCH
/// the engine's best_weapon enum so the int feeds AiBestWeapon's [best_weapon+1] table directly.</summary>
public enum WeaponPref { NoPref = 0, Melee = 1, MeleeOverRanged = 2, RangedOverMelee = 3, Ranged = 4, Unarmed = 5, UnarmedOverThrow = 6, Random = 7 }

/// <summary>
/// A companion's combat-control settings (P50) — Hexwaste's port of the engine's party-member
/// AI-disposition window (game_dialog.cc:3354 partyMemberControlWindowInit + the combat_ai.cc reads).
/// Pure: the enums + the preset resolution (<see cref="Effective"/>) + the decision helpers
/// (<see cref="ShouldFlee"/>, <see cref="PickTarget"/>). The <see cref="Default"/> resolves to
/// Hexwaste's pre-P50 ally behaviour (attack the nearest hostile, never flee, no distance constraint),
/// so a companion with the default settings is BYTE-IDENTICAL to the old TryAllyAction.
/// </summary>
public readonly record struct CompanionAi(
    Disposition Disposition = Disposition.Aggressive,
    AttackWho AttackWho = AttackWho.Closest,
    Distance Distance = Distance.OnYourOwn,
    RunAway RunAway = RunAway.Never,
    ChemUse ChemUse = ChemUse.Clean,
    AreaAttack AreaAttack = AreaAttack.Never,
    WeaponPref WeaponPref = WeaponPref.NoPref)
{
    /// <summary>The pre-P50 ally behaviour (Aggressive → closest / no-distance / never-flee / no burst /
    /// no-pref weapon). NOTE: built EXPLICITLY, not <c>new()</c> — a record struct's parameterless ctor
    /// zero-inits (ignoring the primary-ctor defaults), which would wrongly give Berserk/AbjectCoward.</summary>
    public static readonly CompanionAi Default =
        new(Disposition.Aggressive, AttackWho.Closest, Distance.OnYourOwn, RunAway.Never, ChemUse.Clean,
            AreaAttack.Never, WeaponPref.NoPref);

    /// <summary>Whether an ally should fire a burst this hit (P51; _ai_pick_hit_mode, combat_ai.cc:2287). The
    /// SOMETIMES random roll is engine-side (a 1/freq draw); this resolves the deterministic modes.</summary>
    public static bool ShouldAreaAttack(AreaAttack mode, int secondaryToHit) => mode switch
    {
        AreaAttack.Always => true,
        AreaAttack.BeCareful => secondaryToHit >= 50,
        AreaAttack.BeSure => secondaryToHit >= 85,
        AreaAttack.BeAbsolutelySure => secondaryToHit >= 95,
        _ => false, // Never (off); Sometimes is resolved with the rng in the engine
    };

    /// <summary>Resolve the disposition PRESET into the effective AttackWho/Distance/RunAway. A non-Custom
    /// disposition overrides the explicit knobs (game_dialog.cc: the radio selects a strategy; Custom
    /// enables the 6 detail rows). Aggressive == the Default == the old behaviour.</summary>
    public CompanionAi Effective() => Disposition switch
    {
        Disposition.Berserk => this with { AttackWho = AttackWho.Closest, Distance = Distance.Charge, RunAway = RunAway.Never },
        Disposition.Aggressive => this with { AttackWho = AttackWho.Closest, Distance = Distance.OnYourOwn, RunAway = RunAway.Never },
        Disposition.Defensive => this with { AttackWho = AttackWho.WhoeverAttackingMe, Distance = Distance.StayClose, RunAway = RunAway.Bleeding },
        Disposition.Coward => this with { AttackWho = AttackWho.Weakest, Distance = Distance.StayClose, RunAway = RunAway.FingerHurts },
        _ => this, // Custom: use the explicit knobs
    };

    /// <summary>Flee when current HP falls to the run-away threshold (combat_ai.cc:3077 min_hp gate),
    /// mapped to an HP fraction of max.</summary>
    public static bool ShouldFlee(RunAway mode, int currentHp, int maxHp) => mode switch
    {
        RunAway.Never => false,
        RunAway.Tourniquet => currentHp * 5 <= maxHp,       // ≤ 20%
        RunAway.NotFeelingGood => currentHp * 5 <= maxHp * 2, // ≤ 40%
        RunAway.Bleeding => currentHp * 5 <= maxHp * 3,       // ≤ 60%
        RunAway.FingerHurts => currentHp * 5 <= maxHp * 4,    // ≤ 80%
        RunAway.AbjectCoward => currentHp < maxHp,            // any damage
        _ => false,
    };

    /// <summary>Pick a target by priority among the candidates (combat_ai.cc _ai_find_target). Each
    /// candidate carries its HP, hex-distance from the actor, and whether it last hit the actor. Closest
    /// is the default (the pre-P50 behaviour); ties break by distance.</summary>
    public static int PickTarget(AttackWho mode, IReadOnlyList<(int Hp, int Distance, bool HitMe)> candidates)
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

    private static bool Better(AttackWho mode, (int Hp, int Distance, bool HitMe) a, (int Hp, int Distance, bool HitMe) b) => mode switch
    {
        AttackWho.Strongest => a.Hp != b.Hp ? a.Hp > b.Hp : a.Distance < b.Distance,
        AttackWho.Weakest => a.Hp != b.Hp ? a.Hp < b.Hp : a.Distance < b.Distance,
        AttackWho.WhoeverAttackingMe => a.HitMe != b.HitMe ? a.HitMe : a.Distance < b.Distance,
        _ => a.Distance < b.Distance, // Closest / Whomever
    };
}
