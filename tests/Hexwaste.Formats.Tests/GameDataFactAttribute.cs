namespace Hexwaste.Formats.Tests;

/// <summary>
/// A fact that requires a real Fallout 2 installation. Skipped unless the
/// FALLOUT2_DIR environment variable points at a directory with master.dat,
/// so CI passes without game assets.
/// </summary>
public sealed class GameDataFactAttribute : FactAttribute
{
    public GameDataFactAttribute()
    {
        if (string.IsNullOrEmpty(GameData.Dir))
            Skip = "FALLOUT2_DIR is not set; skipping test that needs real game data.";
    }
}

public static class GameData
{
    public static string? Dir => Environment.GetEnvironmentVariable("FALLOUT2_DIR");

    public static string RequiredDir => Dir
        ?? throw new InvalidOperationException("FALLOUT2_DIR is not set.");
}

/// <summary>Theory variant of <see cref="GameDataFactAttribute"/>.</summary>
public sealed class GameDataTheoryAttribute : TheoryAttribute
{
    public GameDataTheoryAttribute()
    {
        if (string.IsNullOrEmpty(GameData.Dir))
            Skip = "FALLOUT2_DIR is not set; skipping test that needs real game data.";
    }
}
