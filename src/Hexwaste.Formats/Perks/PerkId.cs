namespace Hexwaste.Formats.Perks;

/// <summary>
/// Named indices (perk_defs.h enum order) for the perks whose effects are wired beyond the
/// data-driven stat table (P28-M3). The stat-modifier perks (Toughness, Action Boy, Lifegiver,
/// More/Better Criticals, Faster Healing, Bonus HtH Damage, Strong Back, Dodger, +SPECIAL, …)
/// need no constant here — they apply automatically via <see cref="PerkRules.StatModifier"/>.
/// </summary>
public static class PerkId
{
    public const int BonusHthAttacks = 1;   // −1 AP per melee/unarmed attack (item.cc:1693)
    public const int BonusRangedDamage = 4; // +2 damage per rank on a ranged hit (combat.cc:4547)
    public const int BonusRateOfFire = 5;   // −1 AP per ranged attack (item.cc:1699)
    public const int Sharpshooter = 14;     // +2 effective Perception per rank for ranged to-hit (combat.cc:4355)
    public const int Educated = 18;         // +2 skill points per level-up (character_editor.cc:5689)
    public const int Slayer = 23;           // every melee/unarmed hit is a critical (combat.cc:3866)
    public const int Sniper = 24;           // a ranged hit crits on a d10 ≤ Luck roll (combat.cc:3891)
    public const int HeaveHo = 35;          // +2 effective Strength per rank for throw range (item.cc:1613)
    public const int QuickPockets = 48;     // −2 inventory-access AP/rank (inventory.cc:572) — NOT modeled (no in-combat inventory AP)
    public const int SwiftLearner = 50;     // +5% experience per rank (stat.cc:737)
    public const int LivingAnatomy = 97;    // +5 damage vs a living (non-robot/alien) target (combat.cc:4619)
    public const int Pyromaniac = 101;      // +5 damage with a fire weapon (combat.cc:4626)
    public const int WeaponHandling = 106;  // +3 effective Strength vs the weapon min-ST to-hit penalty (combat.cc:4414)
}
