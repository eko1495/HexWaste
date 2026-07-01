using System.Text;
using Hexwaste.Formats;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Point-4 optional-authenticity: the Highwayman car fuel model (worldmap.cc port) + the holodisk.txt
/// parser (pipboy.cc port).
/// </summary>
public class CarAndHolodiskTests
{
    private static int NoGlobals(int _) => 0;

    [Fact]
    public void CarStartsFueledButNotOwned()
    {
        var car = new CarState();
        Assert.Equal(CarState.FuelMax, car.Fuel); // fo2ce inits carFuel = CAR_FUEL_MAX
        Assert.False(car.InCar);
        Assert.False(car.IsOutOfGas);
        Assert.Equal(-1, car.CurrentAreaId);
    }

    [Fact]
    public void UseGasClampsAtZeroAndFlipsOutOfGas()
    {
        var car = new CarState { Fuel = 5000 };
        car.UseGas(30000, NoGlobals);
        Assert.Equal(0, car.Fuel);
        Assert.True(car.IsOutOfGas);
    }

    [Fact]
    public void UseGasHonoursUpgradeDiscounts()
    {
        // Super-car (−90%) + Reno upgrade (−10%) + fuel-cell regulator (÷2), applied in that order to 1000:
        // 1000 → 100 → 90 → 45.
        int Globals(int g) => g == CarState.GvarSuperCar || g == CarState.GvarRenoUpgrade
            || g == CarState.GvarFuelCellRegulator ? 1 : 0;
        var car = new CarState { Fuel = CarState.FuelMax };
        car.UseGas(1000, Globals);
        Assert.Equal(CarState.FuelMax - 45, car.Fuel);
    }

    [Fact]
    public void FillGasTopsUpAndReportsAmountAdded()
    {
        var car = new CarState { Fuel = 0 };
        Assert.Equal(0, car.FillGas(20000));       // fit entirely → 0 (engine: no top-up needed)
        Assert.Equal(20000, car.Fuel);
        int added = car.FillGas(CarState.FuelMax);  // overshoots → returns the amount added to reach max
        Assert.Equal(CarState.FuelMax, car.Fuel);
        Assert.Equal(CarState.FuelMax - 20000, added);
    }

    [Fact]
    public void GiveToPartyRequiresFuel()
    {
        var empty = new CarState { Fuel = 0 };
        Assert.False(empty.GiveToParty());
        Assert.False(empty.InCar);

        var fueled = new CarState { Fuel = 100 };
        Assert.True(fueled.GiveToParty());
        Assert.True(fueled.InCar);
    }

    [Fact]
    public void HolodiskParserReadsGvarNameDescriptionSkippingComments()
    {
        const string txt = "# header\n\n2001, 100, 101\n2002 , 200 , 201\ngarbage line\n";
        var disks = HolodiskLog.Parse(new MemoryStream(Encoding.ASCII.GetBytes(txt)));
        Assert.Equal(2, disks.Count);
        Assert.Equal(new Holodisk(2001, 100, 101), disks[0]);
        Assert.Equal(new Holodisk(2002, 200, 201), disks[1]);
    }

    [GameDataFact]
    public void RealHolodiskTxtParses()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        if (!vfs.Exists(@"data\holodisk.txt"))
            return; // some extractions omit it; the parser is covered by the inline fixture
        var disks = HolodiskLog.Parse(vfs.OpenRead(@"data\holodisk.txt"));
        Assert.NotEmpty(disks);
        Assert.All(disks, d => Assert.True(d.Gvar > 0));
    }
}
