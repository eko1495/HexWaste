namespace Hexwaste.Formats;

/// <summary>
/// The Highwayman car state, ported from fallout2-ce src/worldmap.cc (wmGenData.isInCar / carFuel /
/// currentCarAreaId + wmCarUseGas/wmCarFillGas/wmCarGiveToParty/wmCarIsOutOfGas). Fuel is 0..<see
/// cref="FuelMax"/>. The car's ACQUISITION (New Reno T-Ray) + the worldmap travel-speed boost and fuel-bar
/// UI are content/presentation and deferred; this is the faithful engine model the metarule car externals
/// (give_car_to_party / give_car_gas / car_current_town) drive — previously silent no-ops.
/// </summary>
public sealed class CarState
{
    public const int FuelMax = 80000; // worldmap.h:8 CAR_FUEL_MAX

    // game_vars.h upgrade GVARs (gap-free enum; index = source line − 7, cross-checked vs GVAR 491). These
    // gate the wmCarUseGas fuel discounts; all 0 on the playable slice, so the tiers are inert there.
    public const int GvarSuperCar = 456;          // GVAR_NEW_RENO_SUPER_CAR
    public const int GvarRenoUpgrade = 455;       // GVAR_NEW_RENO_CAR_UPGRADE
    public const int GvarFuelCellRegulator = 453; // GVAR_CAR_UPGRADE_FUEL_CELL_REGULATOR

    public bool InCar { get; set; }
    // fo2ce inits carFuel = CAR_FUEL_MAX even before the car is owned (worldmap.cc:919); isInCar gates
    // ownership. Defaulting Fuel to max keeps metarule3 110 (out-of-gas) false → 0, matching pre-P100.
    public int Fuel { get; set; } = FuelMax;
    public int CurrentAreaId { get; set; } = -1;

    /// <summary>wmCarIsOutOfGas (worldmap.cc:6031).</summary>
    public bool IsOutOfGas => Fuel <= 0;

    /// <summary>wmCarUseGas (worldmap.cc:5984): burn fuel, honoring the upgrade discounts (super-car −90%,
    /// Reno upgrade −10%, fuel-cell regulator halves), clamped at 0.</summary>
    public void UseGas(int amount, Func<int, int> getGlobal)
    {
        if (getGlobal(GvarSuperCar) != 0) amount -= amount * 90 / 100;
        if (getGlobal(GvarRenoUpgrade) != 0) amount -= amount * 10 / 100;
        if (getGlobal(GvarFuelCellRegulator) != 0) amount /= 2;
        Fuel = Math.Max(0, Fuel - amount);
    }

    /// <summary>wmCarFillGas (worldmap.cc:6010): top up toward <see cref="FuelMax"/>, returning the
    /// overflow that did not fit (0 when it all fit).</summary>
    public int FillGas(int amount)
    {
        if (Fuel + amount <= FuelMax)
        {
            Fuel += amount;
            return 0;
        }
        int remaining = FuelMax - Fuel;
        Fuel = FuelMax;
        return remaining;
    }

    /// <summary>wmCarGiveToParty (worldmap.cc:6..): board the car — requires fuel. Returns false (the
    /// engine's −1 "out of power") when empty. The map transition to the car map is a deferred concern.</summary>
    public bool GiveToParty()
    {
        if (Fuel <= 0)
            return false;
        InCar = true;
        return true;
    }
}
