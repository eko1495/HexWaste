using FalloutPoc.Viewer;

string? gameDir = null;
string mapName = "artemple.map";
string? screenshot = null;
bool roofs = true;
double advanceMs = 0;
int benchFrames = 0;
bool walk = false;
Microsoft.Xna.Framework.Point? pick = null;
Microsoft.Xna.Framework.Point? examine = null;
int? gotoTile = null;
int? doorTile = null;
double ambient = 1.0;
bool worldmap = false;
int? travelArea = null;
bool noAudio = false;
bool noAmbient = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--no-roofs":
            roofs = false;
            break;
        case "--no-audio":
            noAudio = true;
            break;
        case "--no-ambient":
            noAmbient = true;
            break;
        case "--advance-ms" when i + 1 < args.Length:
            advanceMs = double.Parse(args[++i]);
            break;
        case "--bench" when i + 1 < args.Length:
            benchFrames = int.Parse(args[++i]);
            break;
        case "--walk":
            walk = true;
            break;
        case "--goto" when i + 1 < args.Length:
            gotoTile = int.Parse(args[++i]);
            break;
        case "--door" when i + 1 < args.Length:
            doorTile = int.Parse(args[++i]);
            break;
        case "--worldmap":
            worldmap = true;
            break;
        case "--travel" when i + 1 < args.Length:
            travelArea = int.Parse(args[++i]);
            break;
        case "--ambient" when i + 1 < args.Length:
            ambient = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--pick" when i + 1 < args.Length:
        {
            string[] parts = args[++i].Split(',');
            pick = new Microsoft.Xna.Framework.Point(int.Parse(parts[0]), int.Parse(parts[1]));
            break;
        }
        case "--examine" when i + 1 < args.Length:
        {
            string[] parts = args[++i].Split(',');
            examine = new Microsoft.Xna.Framework.Point(int.Parse(parts[0]), int.Parse(parts[1]));
            break;
        }
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
    StartInWalkMode = walk,
    PickAt = pick,
    ExamineAt = examine,
    WalkToTile = gotoTile,
    ToggleDoorAtTile = doorTile,
    InitialAmbient = ambient,
    StartOnWorldmap = worldmap,
    TravelToArea = travelArea,
    DisableAudio = noAudio,
    DisableAmbientLife = noAmbient,
};
game.Run();
return 0;
