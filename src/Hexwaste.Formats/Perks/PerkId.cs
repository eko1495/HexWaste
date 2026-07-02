namespace Hexwaste.Formats.Perks;

/// <summary>
/// Named indices (perk_defs.h enum order) for the perks whose effects are wired beyond the
/// data-driven stat table (P28-M3). The stat-modifier perks (Toughness, Action Boy, Lifegiver,
/// More/Better Criticals, Faster Healing, Bonus HtH Damage, Strong Back, Dodger, +SPECIAL, …)
/// need no constant here — they apply automatically via <see cref="PerkRules.StatModifier"/>.
/// </summary>
public static class PerkId
{
    public const int Awareness = 0;         // examine a critter reveals its HP/condition + wielded weapon (proto_instance.cc:294)
    public const int BonusHthAttacks = 1;   // −1 AP per melee/unarmed attack (item.cc:1693)
    public const int BonusRangedDamage = 4; // +2 damage per rank on a ranged hit (combat.cc:4547)
    public const int BonusRateOfFire = 5;   // −1 AP per ranged attack (item.cc:1699)
    public const int Sharpshooter = 14;     // +2 effective Perception per rank for ranged to-hit (combat.cc:4355)
    public const int Educated = 18;         // +2 skill points per level-up (character_editor.cc:5689)
    public const int Empathy = 22;          // tints dialogue options by NPC reaction (game_dialog.cc:2118)
    public const int FlowerChild = 42;      // halves drug addiction chance + withdrawal duration (item.cc:2834/3060)
    public const int Comprehension = 81;    // +50% skill-book gain (proto_instance.cc:780)
    public const int JetAddiction = 70;     // the Jet withdrawal "perk" — PERMANENT until the Jet antidote (item.cc:2984)
    public const int Slayer = 23;           // every melee/unarmed hit is a critical (combat.cc:3866)
    public const int Sniper = 24;           // a ranged hit crits on a d10 ≤ Luck roll (combat.cc:3891)
    public const int SilentDeath = 25;      // melee/unarmed backstab while sneaking: 4x dmg / x2 on a crit (combat.cc:3870)
    public const int HthEvade = 93;         // unarmed dude: AP→AC dodge ×2 + Unarmed/12 when off-turn (stat.cc:233)
    public const int Pickpocket = 37;       // Steal waives the item-size + face-to-face penalties (skill.cc:1039)
    public const int FortuneFinder = 20;    // 2× caps (pid 41) found in random encounters (worldmap.cc:3880)
    public const int Scrounger = 40;        // (no engine impl) — data-present only, not wired
    public const int Pathfinder = 43;       // worldmap travel time −25%/rank (worldmap.cc:4179)
    public const int CautiousNature = 80;   // +3 to the surrounding-encounter spawn distance (worldmap.cc:3985)
    public const int HeaveHo = 35;          // +2 effective Strength per rank for throw range (item.cc:1613)
    public const int QuickPockets = 48;     // −2 inventory-access AP/rank (inventory.cc:572) — NOT modeled (no in-combat inventory AP)
    public const int SwiftLearner = 50;     // +5% experience per rank (stat.cc:737)
    public const int LivingAnatomy = 97;    // +5 damage vs a living (non-robot/alien) target (combat.cc:4619)
    public const int Pyromaniac = 101;      // +5 damage with a fire weapon (combat.cc:4626)
    public const int WeaponHandling = 106;  // +3 effective Strength vs the weapon min-ST to-hit penalty (combat.cc:4414)

    // P70 batch: combat/stat/heal perks (hardcoded engine effects, Stat=-1 so not auto-folded).
    public const int Healer = 19;           // First Aid/Doctor heal +4*rank min / +10*rank max (skill.cc:561)
    public const int AdrenalineRush = 79;   // +1 ST while current HP < max/2 (stat.cc:256)
    public const int QuickRecovery = 102;   // stand up from prone in 1 AP instead of 3 (combat.cc:5396)
    public const int Stonewall = 104;       // 50% chance to resist a knockdown (combat.cc:4641)

    // P70 batch: the skill-modifier perk family (perk.cc perkGetSkillModifier:628).
    public const int MrFixit = 31;          // +10 Science & Repair
    public const int Medic = 32;            // +10 First Aid & Doctor
    public const int MasterThief = 33;      // +15 Lockpick & Steal
    public const int Speaker = 34;          // +20 Speech
    public const int Ghost = 38;            // +20 Sneak in darkness (light term CUT — no CritterState light model)
    public const int Survivalist = 16;      // +25 Outdoorsman
    public const int Ranger = 47;           // +15 Outdoorsman
    public const int Harmless = 91;         // +20 Steal
    public const int Negotiator = 99;       // +10 Speech & Barter
    public const int Salesman = 103;        // +20 Barter
    public const int Thief = 105;           // +10 Sneak/Lockpick/Steal/Traps
    public const int VaultCityTraining = 107; // +5 First Aid & Doctor
    public const int ExpertExcrementExpeditor = 116; // +5 Speech
    public const int Gambler = 83;          // +20 Gambling

    // P74 batch: the 7 Gain-X SPECIAL perks (+1 to the primary stat, stat.cc:252-309). CONTIGUOUS in
    // perk_defs.h (84..90 == SPECIAL 0..6), so GainStrength + statIndex addresses each. Hardcoded in the
    // engine's critterGetStat switch (NOT data-driven, so the Stat=-1 table doesn't cover them).
    public const int GainStrength = 84;     // +1 ST
    public const int GainPerception = 85;   // +1 PE
    public const int GainEndurance = 86;    // +1 EN
    public const int GainCharisma = 87;     // +1 CH
    public const int GainIntelligence = 88; // +1 IN
    public const int GainAgility = 89;      // +1 AG
    public const int GainLuck = 90;         // +1 LK
    public const int BonusMove = 3;         // 2 free movement AP/rank, drained before combat AP (combat.cc:3237)
    public const int Lifegiver = 28;        // +4 max HP per rank, per level-up (stat.cc:771)
    public const int MasterTrader = 17;     // -25% merchant buy price (inventory.cc:4685)
    public const int SmoothTalker = 49;     // +1 effective INT/rank for giq dialogue gates (interpreter_extra.cc:3867)
}
