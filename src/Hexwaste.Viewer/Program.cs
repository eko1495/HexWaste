using Hexwaste.Viewer;

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
var difficulty = Hexwaste.Formats.Map.GameDifficulty.Normal;
int aimLocation = 8; // HIT_LOCATION_UNCALLED
string? characterName = null;
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
bool forceMenu = false;

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
        case "--menu": // force the front door (menu screenshots/testing)
            forceMenu = true;
            break;
        case "--travel" when i + 1 < args.Length:
            travelArea = int.Parse(args[++i]);
            break;
        case "--ambient" when i + 1 < args.Length:
            ambient = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            ambientFixed = true;
            break;
        case "--save-path" when i + 1 < args.Length: // set the file for in-process --save/--load
            savePath = args[++i];
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
        case "--burst" when i + 1 < args.Length:
            // --burst <hex>: fire a burst at the critter at hex with the equipped
            // burst weapon (e.g. --give 9 --use-item 9 --burst <hex> for the 10mm SMG).
            actions.Add(new ViewerGame.StartupAction.Burst(int.Parse(args[++i])));
            break;
        case "--party-count":
            actions.Add(new ViewerGame.StartupAction.PartyCount());
            break;
        case "--hud-click" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.HudClick(args[++i]));
            break;
        case "--panel-click" when i + 2 < args.Length:
            // --panel-click <side> <row>: click an item-panel row (side 0=left, 1=right).
            actions.Add(new ViewerGame.StartupAction.PanelClick(int.Parse(args[i + 1]), int.Parse(args[i + 2])));
            i += 2;
            break;
        case "--menu-click" when i + 2 < args.Length:
            // --menu-click <options|pipboy|pipboy-rest> <row>: click a menu row.
            actions.Add(new ViewerGame.StartupAction.MenuClick(args[i + 1], int.Parse(args[i + 2])));
            i += 2;
            break;
        case "--use-skill" when i + 2 < args.Length:
            // --use-skill <skillId> <targetHex> (hex<0 = self): apply a Skilldex skill.
            actions.Add(new ViewerGame.StartupAction.UseSkill(int.Parse(args[i + 1]), int.Parse(args[i + 2])));
            i += 2;
            break;
        case "--rest-for" when i + 1 < args.Length:
            // --rest-for <minutes> (or -1 healed / -2,-3 until morning,evening): Pip-Boy rest.
            actions.Add(new ViewerGame.StartupAction.RestFor(int.Parse(args[++i])));
            break;
        case "--automap":
            actions.Add(new ViewerGame.StartupAction.OpenAutomap());
            break;
        case "--set-global" when i + 2 < args.Length:
            // --set-global <id> <value>: force a session GVAR (probe gated dialog).
            actions.Add(new ViewerGame.StartupAction.SetGlobal(int.Parse(args[i + 1]), int.Parse(args[i + 2])));
            i += 2;
            break;
        case "--talk-seq" when i + 2 < args.Length:
            // --talk-seq <hex> <c1,c2,...>: talk to the critter at hex, auto-pick the
            // 1-based choices. Repeatable; multiple share the session GVAR dict so a
            // gated chain works (e.g. talk Vic, then Metzger offers to sell him).
            actions.Add(new ViewerGame.StartupAction.TalkChoose(
                int.Parse(args[i + 1]),
                args[i + 2] == "-" ? [] : args[i + 2].Split(',').Select(int.Parse).ToArray()));
            i += 2;
            break;
        case "--explode" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.Explode(int.Parse(args[++i])));
            break;
        case "--throw" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.Throw(int.Parse(args[++i])));
            break;
        case "--load-transient" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.LoadTransient(args[++i]));
            break;
        case "--encounter-walk" when i + 5 < args.Length:
            actions.Add(new ViewerGame.StartupAction.EncounterWalk(
                int.Parse(args[i + 1]), int.Parse(args[i + 2]), int.Parse(args[i + 3]),
                int.Parse(args[i + 4]), int.Parse(args[i + 5])));
            i += 5;
            break;
        case "--encounter" when i + 3 < args.Length:
            // --encounter <map> <group> <count>: spawn a worldmap.txt group on a
            // transient encounter map (e.g. --encounter desert1.map ARRO_Rats 3).
            actions.Add(new ViewerGame.StartupAction.EncounterSpawnAt(
                args[i + 1], args[i + 2], int.Parse(args[i + 3])));
            i += 3;
            break;
        case "--encounter-fight" when i + 5 < args.Length:
            // --encounter-fight <map> <groupA> <countA> <groupB> <countB>: spawn an
            // X-FIGHTING-Y encounter (two groups, distinct teams) and start the brawl.
            actions.Add(new ViewerGame.StartupAction.EncounterFight(
                args[i + 1], args[i + 2], int.Parse(args[i + 3]), args[i + 4], int.Parse(args[i + 5])));
            i += 5;
            break;
        case "--travel-from" when i + 3 < args.Length:
            // --travel-from <x> <y> <areaIndex>: travel from worldmap pixel (x,y)
            // toward a city.txt area, rolling encounters along the way.
            actions.Add(new ViewerGame.StartupAction.TravelFrom(
                int.Parse(args[i + 1]), int.Parse(args[i + 2]), int.Parse(args[i + 3])));
            i += 3;
            break;
        case "--encounter-answer" when i + 1 < args.Length:
            // --encounter-answer <yes|no>: pre-answer a detected encounter's avoid prompt.
            actions.Add(new ViewerGame.StartupAction.EncounterAnswer(
                args[++i] is "yes" or "y" or "engage" or "true" or "1"));
            break;
        case "--travel-resume" when i + 3 < args.Length:
            // --travel-resume <x> <y> <areaIndex>: leave an encounter map mid-leg and
            // confirm travel auto-resumes toward the destination (phase-16 M2).
            actions.Add(new ViewerGame.StartupAction.TravelResume(
                int.Parse(args[i + 1]), int.Parse(args[i + 2]), int.Parse(args[i + 3])));
            i += 3;
            break;
        case "--travel-step" when i + 3 < args.Length:
            // --travel-step <x> <y> <areaIndex>: drive the animated travel path headlessly
            // (cadence ticks vs pixel-steps + outcome) — phase-17 M2/M4.
            actions.Add(new ViewerGame.StartupAction.TravelStepDemo(
                int.Parse(args[i + 1]), int.Parse(args[i + 2]), int.Parse(args[i + 3])));
            i += 3;
            break;
        case "--force-outdoorsman" when i + 1 < args.Length:
            // --force-outdoorsman <n>: override the party's best Outdoorsman (test plumbing).
            actions.Add(new ViewerGame.StartupAction.ForceOutdoorsman(int.Parse(args[++i])));
            break;
        case "--fight" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.Fight(int.Parse(args[++i])));
            break;
        case "--character" when i + 1 < args.Length:
            characterName = args[++i];
            break;
        case "--rng-seed" when i + 1 < args.Length:
            rngSeed = int.Parse(args[++i]);
            break;
        case "--projectile" when i + 1 < args.Length:
            // --projectile <hex>: throw a spear from range and report the launched
            // flying projectile (phase-10 #11).
            actions.Add(new ViewerGame.StartupAction.ProjectileCheck(int.Parse(args[++i])));
            break;
        case "--difficulty" when i + 1 < args.Length:
            difficulty = args[++i].ToLowerInvariant() switch
            {
                "easy" => Hexwaste.Formats.Map.GameDifficulty.Easy,
                "hard" => Hexwaste.Formats.Map.GameDifficulty.Hard,
                _ => Hexwaste.Formats.Map.GameDifficulty.Normal,
            };
            break;
        case "--aim" when i + 1 < args.Length:
            aimLocation = args[++i].ToLowerInvariant() switch
            {
                "head" => 0, "left_arm" => 1, "right_arm" => 2, "torso" => 3,
                "right_leg" => 4, "left_leg" => 5, "eyes" => 6, "groin" => 7,
                _ => 8, // uncalled
            };
            break;
        case "--give" when i + 1 < args.Length:
        {
            // pid[:count] — test plumbing: drop an item into the dude's bag
            string[] parts = args[++i].Split(':');
            actions.Add(new ViewerGame.StartupAction.Give(int.Parse(parts[0]),
                parts.Length > 1 ? int.Parse(parts[1]) : 1));
            break;
        }
        case "--use-item" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.UseItemByPid(int.Parse(args[++i])));
            break;
        case "--companion" when i + 1 < args.Length:
            // --companion <hex>: recruit the critter at hex, then drive the full
            // control lifecycle (wait/follow/dismiss/rejoin) and print a transcript.
            actions.Add(new ViewerGame.StartupAction.CompanionLifecycle(int.Parse(args[++i])));
            break;
        case "--trade" when i + 2 < args.Length:
            // --trade <hex> <pid>: recruit the critter at hex, give the dude <pid>,
            // then trade it to the follower and back (asserts flat 1:1, no caps).
            actions.Add(new ViewerGame.StartupAction.TradeWith(int.Parse(args[i + 1]), int.Parse(args[i + 2])));
            i += 2;
            break;
        case "--companion-persist" when i + 1 < args.Length:
            // --companion-persist <hex>: recruit + wait, save/load round-trip, assert
            // the wait flag + original team survived (phase-10 #2).
            actions.Add(new ViewerGame.StartupAction.CompanionPersist(int.Parse(args[++i])));
            break;
        case "--dismiss-persist" when i + 1 < args.Length:
            // --dismiss-persist <hex>: recruit + dismiss, save/load round-trip, assert
            // the dismissed body persists on its map and is rejoinable (phase-10 #3).
            actions.Add(new ViewerGame.StartupAction.DismissPersist(int.Parse(args[++i])));
            break;
        case "--recruit" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.Recruit(int.Parse(args[++i])));
            break;
        case "--use-on" when i + 1 < args.Length:
        {
            string[] parts = args[++i].Split(':');
            actions.Add(new ViewerGame.StartupAction.UseOn(int.Parse(parts[0]), int.Parse(parts[1])));
            break;
        }
        case "--buy" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.Buy(int.Parse(args[++i])));
            break;
        case "--sell" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.Sell(int.Parse(args[++i])));
            break;
        case "--end-barter":
            actions.Add(new ViewerGame.StartupAction.EndBarter());
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
        case "--grant-xp" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.GrantXp(int.Parse(args[++i])));
            break;
        case "--spend-skill" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.SpendSkill(int.Parse(args[++i])));
            break;
        case "--show-skills":
            actions.Add(new ViewerGame.StartupAction.OpenSkills());
            break;
        case "--rest":
            actions.Add(new ViewerGame.StartupAction.Rest());
            break;
        case "--hurt" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.Hurt(int.Parse(args[++i])));
            break;
        case "--create" when i + 1 < args.Length:
        {
            // "S,P,E,C,I,A,L:t,t,t:g"
            string[] parts = args[++i].Split(':');
            int[] special = parts[0].Split(',').Select(int.Parse).ToArray();
            int[] tags = parts[1].Split(',').Select(int.Parse).ToArray();
            int gender = parts.Length > 2 ? int.Parse(parts[2]) : 0;
            actions.Add(new ViewerGame.StartupAction.CreateCharacter(special, tags, gender));
            break;
        }
        case "--show-create":
            actions.Add(new ViewerGame.StartupAction.ShowCreate());
            break;
        case "--advance-days" when i + 1 < args.Length:
            actions.Add(new ViewerGame.StartupAction.AdvanceDays(int.Parse(args[++i])));
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
            Console.Error.WriteLine("usage: Hexwaste.Viewer --game-dir <dir> [--map artemple.map] [--screenshot out.png]");
            return 1;
    }
}

// A game directory must contain master.dat. When --game-dir is omitted, probe
// the usual install locations (GOG, Steam) plus a game-data folder next to
// the executable / working directory.
static bool LooksLikeGameDir(string? dir) =>
    dir is not null && Directory.Exists(dir)
    && Directory.EnumerateFiles(dir)
        .Any(f => Path.GetFileName(f).Equals("master.dat", StringComparison.OrdinalIgnoreCase));

if (gameDir is null)
{
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string[] probes =
    [
        Path.Combine(AppContext.BaseDirectory, "game-data"),
        Path.Combine(Environment.CurrentDirectory, "game-data"),
        @"C:\GOG Games\Fallout 2",
        @"C:\Program Files (x86)\GOG Galaxy\Games\Fallout 2",
        @"C:\Program Files (x86)\Steam\steamapps\common\Fallout 2",
        Path.Combine(home, "GOG Games", "Fallout 2"),
        Path.Combine(home, ".steam", "steam", "steamapps", "common", "Fallout 2"),
        Path.Combine(home, ".local", "share", "Steam", "steamapps", "common", "Fallout 2"),
        Path.Combine(home, "Games", "Fallout 2"),
    ];
    gameDir = probes.FirstOrDefault(LooksLikeGameDir);
}

if (!LooksLikeGameDir(gameDir))
{
    Console.Error.WriteLine(gameDir is null
        ? "No Fallout 2 game data found."
        : $"'{gameDir}' does not contain master.dat.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Hexwaste needs the data files from an original copy of Fallout 2");
    Console.Error.WriteLine("(GOG or Steam — no game assets ship with Hexwaste). Point it at the");
    Console.Error.WriteLine("install folder containing master.dat, critter.dat and patch000.dat:");
    Console.Error.WriteLine();
    Console.Error.WriteLine("    Hexwaste.Viewer --game-dir \"C:\\GOG Games\\Fallout 2\"");
    Console.Error.WriteLine();
    Console.Error.WriteLine("or copy/symlink that folder to ./game-data next to the executable.");
    return 1;
}

// The main-menu front door appears only for a plain interactive launch;
// any test/headless flag boots straight into the world like before.
bool interactiveLaunch = screenshot is null && actions.Count == 0 && talkHex is null
    && talk is null && pick is null && examine is null && benchFrames == 0
    && !loadOnStart && !worldmap && travelArea is null && gotoTile is null
    && doorTile is null && advanceMs == 0 && !walk;

using var game = new ViewerGame(gameDir!, mapName, screenshot, roofs)
{
    StartInMenu = interactiveLaunch || forceMenu,
    AdvanceCyclingMs = advanceMs,
    BenchFrames = benchFrames,
    StartInWalkMode = walk,
    PickAt = pick,
    ExamineAt = examine,
    TalkAt = talk,
    TalkAtHex = talkHex,
    StartupActions = actions,
    RngSeed = rngSeed,
    Difficulty = difficulty,
    AimLocation = aimLocation,
    CharacterName = characterName,
    AutoChoose = choose,
    WalkToTile = gotoTile,
    ToggleDoorAtTile = doorTile,
    InitialAmbient = ambient,
    AmbientFixed = ambientFixed,
    SaveOnExit = saveOnExit,
    LoadOnStart = loadOnStart,
    SavePath = savePath ?? "hexwaste-save.json",
    StartOnWorldmap = worldmap,
    TravelToArea = travelArea,
    DisableAudio = noAudio,
    DisableAmbientLife = noAmbient,
};
game.Run();
return 0;
