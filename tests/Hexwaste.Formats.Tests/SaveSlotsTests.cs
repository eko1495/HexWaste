using Hexwaste.Formats;

namespace Hexwaste.Formats.Tests;

/// <summary>P48: the pure multi-slot save bookkeeping (SaveSlots) — slot count,
/// per-slot file names, and the SaveState→SlotInfo display reduction.</summary>
public class SaveSlotsTests
{
    [Fact]
    public void TenSlots() => Assert.Equal(10, SaveSlots.Count);

    [Theory]
    [InlineData(0, "hexwaste-slot0.json")]
    [InlineData(9, "hexwaste-slot9.json")]
    public void SlotFileNamePerSlot(int slot, string expected) =>
        Assert.Equal(expected, SaveSlots.SlotFileName(slot));

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void SlotFileNameRejectsOutOfRange(int slot) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SaveSlots.SlotFileName(slot));

    [Fact]
    public void DescribeNullIsEmpty() => Assert.Equal(SlotInfo.Empty, SaveSlots.Describe(null));

    [Fact]
    public void DescribeOccupiedCarriesTheMetadata()
    {
        var state = new SaveState
        {
            Version = SaveState.CurrentVersion,
            Character = "combat",
            DudeLevel = 3,
            Map = "denbus2.map",
            ClockTicks = 302400,
        };
        SlotInfo info = SaveSlots.Describe(state);
        Assert.True(info.Occupied);
        Assert.False(info.VersionMismatch);
        Assert.Equal("combat", info.Character);
        Assert.Equal(3, info.Level);
        Assert.Equal("denbus2.map", info.Map);
        Assert.Equal(GameClock.DateStringAt(302400), info.Date);
    }

    [Fact]
    public void DescribeVersionMismatchIsFlagged()
    {
        var state = new SaveState { Version = SaveState.CurrentVersion - 1 };
        SlotInfo info = SaveSlots.Describe(state);
        Assert.True(info.Occupied);
        Assert.True(info.VersionMismatch);
    }
}
