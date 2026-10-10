using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Save.Vanilla;

public sealed record SaveHeader(string CharacterName, string Description, int GameMonth, int GameDay, int GameYear,
    uint GameTime, int Elevation, int MapIndex, string MapFileName);

public sealed record CritterSection(int SneakWorking, int Flags, int[] BaseStats, int[] BonusStats, int[] Skills,
    int BodyType, int Experience, int KillType, int DamageType);

public sealed record CombatantSaveData(int FriendlyDeadId, int LastTargetId, int LastItemId, int LastMove);

public sealed record CombatSection(bool Active, uint State, int TurnRunning, int FreeMove, int Exps,
    int ListCom, int ListNoncom, int DudeCid, int[] CombatantCids, CombatantSaveData[] CombatantData);

public sealed record PcStatsSection(int UnspentSkillPoints, int Level, int Experience, int Reputation, int Karma);

public sealed record CitySave(int X, int Y, int State, int VisitedState, int[] EntranceStates);

public sealed record WorldmapSection(bool DidMeetFrank, int CurrentArea, int PosX, int PosY, bool EncounterIconVisible,
    int EncMapId, int EncTableId, int EncEntryId, bool IsInCar, int CarArea, int CarFuel,
    CitySave[] Cities, int NumHorizontalTiles, int[] SubtileStates, (int Table, int Entry, int Counter)[] Counters);

public sealed record PartyMemberLevel(int Level, int NumLevelUps, bool IsEarly);

public sealed record QueueEventSave(uint Time, int Type, int OwnerId, int[] Data);

public sealed class VanillaSave
{
    public required SaveHeader Header { get; init; }
    public required int DudeCid { get; init; }
    public required int[] GlobalVars { get; init; }
    public required string[] MapFiles { get; init; }
    public required int AutomapDbSize { get; init; }
    public required MapObject Dude { get; init; }
    public required int DudeCenterTile { get; init; }
    public required CritterSection Critter { get; init; }
    public required int[] Kills { get; init; }
    public required int[] TaggedSkills { get; init; }
    public required int[][] PerksByDescription { get; init; }
    public required CombatSection Combat { get; init; }
    public required int[][] CustomAiPackets { get; init; }
    public required PcStatsSection PcStats { get; init; }
    public required int[] Traits { get; init; }
    public required int AutomapFlags { get; init; }
    public required byte[] PreferencesRaw { get; init; }
    public required int CharacterEditorLastLevel { get; init; }
    public required bool CharacterEditorHasFreePerk { get; init; }
    public required WorldmapSection Worldmap { get; init; }
    public required byte[] MoviesSeen { get; init; }
    public required int[] SkillUsage { get; init; }
    public required int PartyItemCount { get; init; }
    public required int[] PartyMemberIds { get; init; }
    public required PartyMemberLevel[] PartyLevels { get; init; }
    public required QueueEventSave[] Queue { get; init; }
    public required int[] InterfaceRaw { get; init; }
    public required long TotalLength { get; init; }
}
