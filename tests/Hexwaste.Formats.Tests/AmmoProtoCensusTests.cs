using Hexwaste.Formats;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// F43's gate: the reference multiplies by ammo's damageMultiplier unconditionally
/// (combat.cc:4586-4587) and guards only the divisor (:4594-4598), while
/// RangedMath.RollDamage clamps the multiplier to a minimum of 1. The clamp only
/// changes an outcome for ammo whose multiplier is 0, so this census establishes
/// whether the divergence is live on shipped data or inert.
/// </summary>
public class AmmoProtoCensusTests
{
    [GameDataFact]
    public void NoShippedAmmoProtoHasADamageMultiplierOfZero()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);

        var ammo = new List<(int Pid, int Mult, int Div)>();
        // Item PIDs are (type 0 << 24) | 1-based items.lst index. Walk until the
        // database reports the index is past the end of the list.
        for (int index = 1; index <= 1000; index++)
        {
            ProtoInfo info;
            try
            {
                info = protos.Get(index);
            }
            catch (InvalidDataException)
            {
                continue; // past the end of items.lst, or a short .pro — neither is ammo evidence
            }
            catch (FileNotFoundException)
            {
                continue;
            }

            if (info.Ammo is { } a)
                ammo.Add((index, a.DamageMultiplier, a.DamageDivisor));
        }

        Assert.NotEmpty(ammo); // a census that found no ammo at all proves nothing

        var zeroMultiplier = ammo.Where(a => a.Mult == 0).ToList();
        var zeroDivisor = ammo.Where(a => a.Div == 0).ToList();

        // Reported unconditionally so the numbers land in the run log, not only on failure.
        Console.WriteLine($"AMMO CENSUS: {ammo.Count} ammo protos; "
            + $"multiplier==0: {zeroMultiplier.Count}; divisor==0: {zeroDivisor.Count}");
        foreach ((int pid, int mult, int div) in ammo.Where(a => a.Mult != 1 || a.Div != 1))
            Console.WriteLine($"  pid {pid}: mult={mult} div={div}");

        Assert.True(zeroMultiplier.Count == 0,
            "Ammo protos with damageMultiplier == 0 exist: "
            + string.Join(", ", zeroMultiplier.Select(a => a.Pid))
            + " — F43 is a LIVE damage change, not an inert one. Stop and escalate before Task 6.");
    }
}
