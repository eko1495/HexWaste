namespace Hexwaste.Formats.Map;

/// <summary>
/// The vanilla elevator tables, ported VERBATIM from fallout2-ce src/elevator.cc (24 initialized
/// rows of the [ELEVATORS_MAX=50] arrays; ELEVATOR_COUNT = 24). An elevator scenery object carries a
/// TYPE that indexes these tables + a starting LEVEL; the picker shows the type's buttons and a
/// picked button resolves to a (map, elevation, tile) destination.
/// </summary>
public static class ElevatorTables
{
    public const int KlamathToxicCaves = 13; // elevator.h enum — the on-slice elevator

    /// <summary>gElevatorLevels (elevator.cc:95): button count per elevator type.</summary>
    public static readonly int[] Levels =
    [
        4, 2, 3, 2, 3, 2, 3, 3, 3, 3, 3, 2, // 0..11
        4, 2, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, // 12..23
    ];

    /// <summary>gElevatorDescriptions (elevator.cc:123): [type][button] → (map, elevation, tile).
    /// tile == −1 marks an unused button slot.</summary>
    public static readonly (int Map, int Elevation, int Tile)[][] Descriptions =
    [
        [(14, 0, 18940), (14, 1, 18936), (15, 0, 21340), (15, 1, 21340)], //  0 BoS main
        [(13, 0, 20502), (14, 0, 14912), (0, 0, -1), (0, 0, -1)],         //  1 BoS surface
        [(33, 0, 12498), (33, 1, 20094), (34, 0, 17312), (0, 0, -1)],     //  2 Master upper
        [(34, 0, 16140), (34, 1, 16140), (0, 0, -1), (0, 0, -1)],         //  3 Master lower
        [(49, 0, 14920), (49, 1, 15120), (50, 0, 12944), (0, 0, -1)],     //  4 Military base upper
        [(50, 0, 24520), (50, 1, 25316), (0, 0, -1), (0, 0, -1)],         //  5 Military base lower
        [(42, 0, 22526), (42, 1, 22526), (42, 2, 22526), (0, 0, -1)],     //  6 Glow upper
        [(42, 2, 14086), (43, 0, 14086), (43, 2, 14086), (0, 0, -1)],     //  7 Glow lower
        [(40, 0, 14104), (40, 1, 22504), (40, 2, 17312), (0, 0, -1)],     //  8 Vault 13
        [(9, 0, 13704), (9, 1, 23302), (9, 2, 17308), (0, 0, -1)],        //  9 Necropolis
        [(28, 0, 19300), (28, 1, 19300), (28, 2, 20110), (0, 0, -1)],     // 10 Sierra 1
        [(28, 2, 20118), (29, 0, 21710), (0, 0, -1), (0, 0, -1)],         // 11 Sierra 2
        [(28, 0, 20122), (28, 1, 20124), (28, 2, 20940), (29, 0, 22540)], // 12 Sierra service
        [(12, 1, 16052), (12, 2, 14480), (0, 0, -1), (0, 0, -1)],         // 13 Klamath toxic caves
        [(6, 0, 14104), (6, 1, 22504), (6, 2, 17312), (0, 0, -1)],        // 14 Elevator 14
        [(30, 0, 14104), (30, 1, 22504), (30, 2, 17312), (0, 0, -1)],     // 15 Vault City
        [(36, 0, 13704), (36, 1, 23302), (36, 2, 17308), (0, 0, -1)],     // 16 Vault 15 main
        [(39, 0, 17285), (36, 0, 19472), (0, 0, -1), (0, 0, -1)],         // 17 Vault 15 surface
        [(109, 2, 10701), (109, 1, 10705), (0, 0, -1), (0, 0, -1)],       // 18 Navarro northern
        [(109, 2, 14697), (109, 1, 15099), (0, 0, -1), (0, 0, -1)],       // 19 Navarro center
        [(109, 2, 23877), (109, 1, 25481), (0, 0, -1), (0, 0, -1)],       // 20 Navarro lab
        [(109, 2, 26130), (109, 1, 24721), (0, 0, -1), (0, 0, -1)],       // 21 Navarro canteen
        [(137, 0, 23953), (148, 1, 16526), (0, 0, -1), (0, 0, -1)],       // 22 SF Shi temple
        [(62, 0, 13901), (63, 1, 17923), (0, 0, -1), (0, 0, -1)],         // 23 Redding Wanamingo mine
    ];

    /// <summary>gElevatorBackgrounds (elevator.cc:65): per-type (background, panel) art\intrface
    /// list indices; panel −1 = the background already carries the button column. The shared
    /// button/gauge art is <see cref="ButtonDownFrmId"/>/<see cref="ButtonUpFrmId"/>/<see cref="GaugeFrmId"/>
    /// (gElevatorFrmIds, elevator.cc:58).</summary>
    public static readonly (int BackgroundFrmId, int PanelFrmId)[] Backgrounds =
    [
        (143, -1), (143, 150), (144, -1), (144, 145), (146, -1), (146, 147), //  0..5
        (146, -1), (146, 151), (148, -1), (146, -1), (146, -1), (146, 147),  //  6..11
        (388, -1), (143, 150), (148, -1), (148, -1), (148, -1), (143, 150),  // 12..17
        (143, 150), (143, 150), (143, 150), (143, 150), (143, 150), (143, 150), // 18..23
    ];

    public const int ButtonDownFrmId = 141; // ebut_in.frm
    public const int ButtonUpFrmId = 142;   // ebut_out.frm
    public const int GaugeFrmId = 149;      // gaj000.frm — 13 vertically stacked slices

    /// <summary>The gauge strip has 13 slices; a level maps to slice (int)(level * 12/(levels−1))
    /// (elevatorSelectLevel :384-392). The full sweep runs at 276.92307 ms per slice unit
    /// (:425-427: delay = v43·276.92307 per v43-sized step → constant slice rate).</summary>
    public const int GaugeSlices = 13;
    public const double GaugeMsPerSlice = 276.92307;

    /// <summary>gElevatorLevelLabels (elevator.cc:273): the printed label char per button ('\0' =
    /// slot unused). These double as the keyboard shortcuts in the live picker.</summary>
    public static readonly char[][] LevelLabels =
    [
        ['1', '2', '3', '4'], ['G', '1', '\0', '\0'], ['1', '2', '3', '\0'], ['3', '4', '\0', '\0'],
        ['1', '2', '3', '\0'], ['3', '4', '\0', '\0'], ['1', '2', '3', '\0'], ['3', '4', '6', '\0'],
        ['1', '2', '3', '\0'], ['1', '2', '3', '\0'], ['1', '2', '3', '\0'], ['3', '4', '\0', '\0'],
        ['1', '2', '3', '4'], ['1', '2', '\0', '\0'], ['1', '2', '3', '\0'], ['1', '2', '3', '\0'],
        ['1', '2', '3', '\0'], ['1', '2', '\0', '\0'], ['1', '2', '\0', '\0'], ['1', '2', '\0', '\0'],
        ['1', '2', '\0', '\0'], ['1', '2', '\0', '\0'], ['1', '2', '\0', '\0'], ['1', '2', '\0', '\0'],
    ];

    /// <summary>The start-level fixup + button resolution, ported from fallout2-ce elevator.cc
    /// elevatorSelectLevel (:349-380). Given the elevator TYPE, the CURRENT map, and the stub's start
    /// LEVEL, returns the 0-based index of the "current" (highlighted) button. Preserves the original's
    /// un-bounds-checked description read via a guard (the OOB is a real upstream bug; we clamp).</summary>
    public static int CurrentButton(int elevatorType, int currentMap, int startLevel)
    {
        (int Map, int Elevation, int Tile)[] desc = Descriptions[elevatorType];
        int level = startLevel;

        int index = 0;
        for (; index < 4; index++)
            if (desc[index].Map == currentMap)
                break;

        if (index < 4)
        {
            int probe = level + index;
            if (probe >= 0 && probe < 4 && desc[probe].Tile != -1) // clamp the fo2ce OOB read (elevator.cc:359)
                level += index;
        }

        // per-elevator special cases (elevator.cc:364-376)
        if (elevatorType == 11) // SIERRA_2
            level -= level <= 2 ? 2 : 3;
        else if (elevatorType == 5 && level >= 2) // MILITARY_BASE_LOWER
            level -= 2;
        else if (elevatorType == 4 && level == 4) // MILITARY_BASE_UPPER
            level -= 2;

        if (level > 3) // final clamp (elevator.cc:378-380)
            level -= 3;
        return Math.Clamp(level, 0, Levels[elevatorType] - 1);
    }
}
