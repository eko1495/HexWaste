namespace Hexwaste.Formats.Combat;

/// <summary>
/// Trade pricing, ported from fallout2-ce src/inventory.cc
/// _barter_compute_value(): the NPC demands a marked-up price for its goods
/// while the player's goods always credit at face value (:4742) — the whole
/// spread sits on the buy side. Caps (pid 41) trade 1:1 by quantity.
/// </summary>
public static class BarterMath
{
    /// <summary>Barter skill = 4 × CH + points (+ tag bonus for the dude);
    /// delegates to <see cref="CritterState.BarterSkill"/>.</summary>
    public static int BarterSkill(CritterState critter) => critter.BarterSkill;

    /// <summary>What the NPC demands for an item:
    /// cost × 2 × (mod+100)/100 × (160+npcBarter)/(160+dudeBarter).
    /// (Master Trader and the reaction modifier are out of PoC scope.)</summary>
    public static int BuyPrice(int cost, int modifier, int npcBarter, int dudeBarter, bool masterTrader = false)
    {
        // ported from fallout2-ce src/inventory.cc _barter_compute_value (:4683-4701) — eval order kept exact.
        double perkBonus = masterTrader ? 25.0 : 0.0;                 // Master Trader: -25% merchant buy price
        double barterModMult = (modifier + 100.0 - perkBonus) * 0.01;
        double balancedCost = (160.0 + npcBarter) / (160.0 + dudeBarter) * (cost * 2.0);
        if (barterModMult < 0)
            barterModMult = 0.0099999998;                             // inventory.cc:4697 (NOT 0)
        return (int)(barterModMult * balancedCost);
    }

    /// <summary>Player goods credit at plain cost (inventory.cc:4742).</summary>
    public static int SellPrice(int cost) => Math.Max(cost, 0);
}
