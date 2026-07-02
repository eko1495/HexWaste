using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Xunit;

namespace Hexwaste.Formats.Tests;

/// <summary>P114 engine-gap batch: the pure tables/math (Master Trader barter price, .gam map-var parse,
/// Smooth Talker via the dialogue INT gate). Enhanced Knockout + opDisplay are covered by combat/VM tests.</summary>
public class P114FidelityTests
{
    // --- Item 4: Master Trader (-25% merchant buy price), verbatim eval order ---
    [Fact]
    public void MasterTraderCutsMerchantBuyPrice()
    {
        // Regression: perk-less price unchanged (guards the FP reorder).
        Assert.Equal(466, BarterMath.BuyPrice(175, 0, 80, 20));
        Assert.Equal(430, BarterMath.BuyPrice(175, 0, 80, 35));
        // Master Trader: mult 1.0 -> 0.75, so 466.67*0.75 = 350.
        Assert.Equal(350, BarterMath.BuyPrice(175, 0, 80, 20, masterTrader: true));
        Assert.True(BarterMath.BuyPrice(175, 0, 80, 20, true) < BarterMath.BuyPrice(175, 0, 80, 20));
    }

    [Fact]
    public void BarterModifierClampsAtTinyPositiveNotZero()
    {
        // modifier -100 → mult 0 (or -0.25 with Master Trader) → clamped to 0.0099999998, not 0 (inventory.cc:4697).
        Assert.True(BarterMath.BuyPrice(100, -100, 80, 20, masterTrader: true) >= 0);
        Assert.True(BarterMath.BuyPrice(100, -100, 80, 20) >= 0);
    }

    // --- Item 4: Smooth Talker raises the effective INT for a giq-gated option ---
    [Fact]
    public void SmoothTalkerRaisesEffectiveIntForDialogueGate()
    {
        // An INT-6 option is hidden at INT 5, visible at INT 6 (Smooth Talker rank 1 → 5+1).
        Assert.False(DialogGate.IqOptionVisible(6, 5));
        Assert.True(DialogGate.IqOptionVisible(6, 5 + 1));
    }

    // --- Item 2: per-map .gam MAP_GLOBAL_VARS parse (section selection + skip rules) ---
    [Fact]
    public void MapGlobalVarsParseSelectsTheMapSectionAndSkipsNoise()
    {
        // A decoy //MAP_GLOBAL_VARS comment must NOT trigger the section; a blank + a real // comment skip.
        const string gam =
            "//MAP_GLOBAL_VARS: this is a decoy comment\n" +
            "MAP_GLOBAL_VARS:\n" +
            "// a real comment\n" +
            "\n" +
            "MVAR_A := 0;\n" +
            "MVAR_B := 5;\n" +
            "MVAR_C := -3;\n";
        var vals = GameGlobalVars.Parse(gam, "MAP_GLOBAL_VARS:");
        Assert.Equal(3, vals.Count);          // 3 vars; decoy/comment/blank skipped
        Assert.Equal(new[] { 0, 5, -3 }, vals);
    }

    [Fact]
    public void GameGlobalVarsSectionStillDefaultsToGameSection()
    {
        var vals = GameGlobalVars.Parse("GAME_GLOBAL_VARS:\nGVAR_X := 50;\n");
        Assert.Equal(new[] { 50 }, vals);
    }

    // --- Item 6: reg_anim batch dispatches sequentially, honoring the per-action delay ---
    static MapObject Dummy() => new()
    {
        Id = 1, HexTile = 100, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0x01000000, Flags = 0, Pid = 0, Sid = -1,
    };

    [Fact]
    public void RegAnimBatchDispatchesHeadThenWaitsForBlockerAndDelay()
    {
        MapObject o = Dummy();
        var actions = new[]
        {
            new RegAnimAction(RegAnimKind.MoveToTile, o, 200, null, 0, 0), // head, no delay
            new RegAnimAction(RegAnimKind.MoveToTile, o, 300, null, 0, 5), // 5-frame delay
            new RegAnimAction(RegAnimKind.Animate,    o, 0,   null, 3, 0),
        };
        var seq = new RegAnimSequencer(actions, frameMs: 100.0);

        // Head dispatches immediately.
        Assert.Same(actions[0], seq.Begin());

        // While the blocker (head's walk) is still moving, nothing dispatches.
        Assert.Null(seq.Advance(blockerActive: true, elapsedMs: 1000));

        // Blocker done, but action[1]'s 5-frame delay (500 ms) hasn't elapsed → still waits.
        Assert.Null(seq.Advance(blockerActive: false, elapsedMs: 300));
        // Delay now satisfied → action[1] dispatches.
        Assert.Same(actions[1], seq.Advance(blockerActive: false, elapsedMs: 300));

        // action[2] has 0 delay → dispatches as soon as its blocker (action[1]) finishes.
        Assert.Null(seq.Advance(blockerActive: true, elapsedMs: 50));   // action[1] still walking
        Assert.Same(actions[2], seq.Advance(blockerActive: false, elapsedMs: 50));
        Assert.True(seq.Done);
        Assert.Null(seq.Advance(blockerActive: false, elapsedMs: 999));
    }

    [Fact]
    public void RegAnimSingleActionBatchDispatchesImmediately()
    {
        // The N=1 case (the only reg_anim golden fixture) — Begin returns the sole action, then Done.
        MapObject o = Dummy();
        var seq = new RegAnimSequencer(new[] { new RegAnimAction(RegAnimKind.MoveToTile, o, 200, null, 0, 0) });
        Assert.NotNull(seq.Begin());
        Assert.True(seq.Done);
    }
}
