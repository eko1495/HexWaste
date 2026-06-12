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
Microsoft.Xna.Framework.Point? talk = null;
int[] choose = [];
int? talkHex = null;
int? rngSeed = null;
List<ViewerGame.StartupAction> actions = [];
int? gotoTile = null;
int? doorTile = null;
double ambient = 1.0;
bool ambientFixed = false;
string? savePath = null;
bool saveOnExit = false;
bool loadOnStart = false;
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
            ambientFixed = true;
            break;
        case "--save-to" when i + 1 < args.Length:
            savePath = args[++i];
            saveOnExit = true;
            break;
        case "--load-from" when i + 1 < args.Length:
            savePath = args[++i];
            loadOnStart = true;
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
        case "--talk" when i + 1 < args.Length:
        {
            string[] parts = args[++i].Split(',');
            talk = new Microsoft.Xna.Framework.Point(int.Parse(parts[0]), int.Parse(parts[1]));
            break;
        }
        case "--choose" when i + 1 < args.Length:
            choose = args[++i].Split(',').Select(int.Parse).ToArray();
            break;
        case "--talk-hex" when i + 1 < args.Length:
            talkHex = int.Parse(args[++i]);
            break;
        case "--use-hex" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.UseHex(int.Parse(args[++i]), Lockpick: false));
            break;
        case "--lockpick-hex" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.UseHex(int.Parse(args[++i]), Lockpick: true));
            break;
        case "--examine-critter" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.ExamineCritter(int.Parse(args[++i])));
            break;
        case "--attack" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.Attack(int.Parse(args[++i])));
            break;
        case "--fight" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.Fight(int.Parse(args[++i])));
            break;
        case "--rng-seed" when i + 1 < args.Length:
            rngSeed = int.Parse(args[++i]);
            break;
        case "--take-all":
            actions.Add(new ViewerGame.StartupAction.TakeAll());
            break;
        case "--goto-map" when i + 1 < args.Length:
        {
            // file[:tile[:elevation]] — tile omitted = map's entering position
            string[] parts = args[++i].Split(':');
            actions.Add(new ViewerGame.StartupAction.Transit(parts[0],
                parts.Length > 1 ? int.Parse(parts[1]) : -1,
                parts.Length > 2 ? int.Parse(parts[2]) : 0));
            break;
        }
        case "--save-now":
            actions.Add(new ViewerGame.StartupAction.SaveNow());
            break;
        case "--load-now":
            actions.Add(new ViewerGame.StartupAction.LoadNow());
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
    StartInWalkMode = walk,
    PickAt = pick,
    ExamineAt = examine,
    TalkAt = talk,
    TalkAtHex = talkHex,
    StartupActions = actions,
    RngSeed = rngSeed,
    AutoChoose = choose,
    WalkToTile = gotoTile,
    ToggleDoorAtTile = doorTile,
    InitialAmbient = ambient,
    AmbientFixed = ambientFixed,
    SaveOnExit = saveOnExit,
    LoadOnStart = loadOnStart,
    SavePath = savePath ?? "fpoc-save.json",
    StartOnWorldmap = worldmap,
    TravelToArea = travelArea,
    DisableAudio = noAudio,
    DisableAmbientLife = noAmbient,
};
game.Run();
return 0;
