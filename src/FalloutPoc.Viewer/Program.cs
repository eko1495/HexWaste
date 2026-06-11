using FalloutPoc.Viewer;

string? gameDir = null;
string mapName = "artemple.map";
string? screenshot = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
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

using var game = new ViewerGame(gameDir, mapName, screenshot);
game.Run();
return 0;
