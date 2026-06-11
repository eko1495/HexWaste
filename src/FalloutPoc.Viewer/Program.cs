using FalloutPoc.Viewer;

string? gameDir = null;
string mapName = "artemple.map";
string? screenshot = null;
bool roofs = true;
double advanceMs = 0;
int benchFrames = 0;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--no-roofs":
            roofs = false;
            break;
        case "--advance-ms" when i + 1 < args.Length:
            advanceMs = double.Parse(args[++i]);
            break;
        case "--bench" when i + 1 < args.Length:
            benchFrames = int.Parse(args[++i]);
            break;
        case "--game-dir" when i + 1 < args.Length:
            gameDir = args[++i];
            break;
        case "--map" when i + 1 < args.Length:
            mapName = args[++i];
            break;
        case "--screenshot" when i + 1 < args.Length:
            screenshot = args[++i];
            break;
        default:
            Console.Error.WriteLine($"unknown argument '{args[i]}'");
            Console.Error.WriteLine("usage: FalloutPoc.Viewer --game-dir <dir> [--map artemple.map] [--screenshot out.png]");
            return 1;
    }
}

gameDir ??= Path.Combine(AppContext.BaseDirectory, "game-data");
if (!Directory.Exists(gameDir))
{
    Console.Error.WriteLine($"game directory '{gameDir}' not found — pass --game-dir <dir>");
    return 1;
}

using var game = new ViewerGame(gameDir, mapName, screenshot, roofs)
{
    AdvanceCyclingMs = advanceMs,
    BenchFrames = benchFrames,
};
game.Run();
return 0;
