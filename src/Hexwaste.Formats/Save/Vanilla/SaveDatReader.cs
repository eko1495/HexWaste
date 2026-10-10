using System.Text;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Save.Vanilla;

/// <summary>Read-only SAVE.DAT parser: header + the 27 handlers of fallout2-ce src/loadsave.cc
/// _master_load_list (:242-269), in order. The stream has no section lengths, so every section is
/// parsed (unmodelled ones kept raw) to reach the next.</summary>
public static class SaveDatReader
{
    private const int StatSaveableCount = 35;   // stat_defs.h:70 SAVEABLE_STAT_COUNT
    private const int SkillCount = 18;          // skill_defs.h:31
    private const int KillTypeCount = 19;       // proto_types.h:107-126
    private const int TaggedSkillCount = 4;     // skill_defs.h:7
    private const int PerkCount = 119;          // perk_defs.h:126
    private const int PcStatCount = 5;          // stat_defs.h:76-82
    private const int TraitsSelected = 2;       // trait_defs.h:7
    private const int MovieCount = 17;          // game_movie.h:16-33
    private const int SkillUsesPerDay = 3;      // skill.cc:32
    private const int AiPacketInts = 45;        // combat_ai.cc aiPacketRead (:615)
    private const int SubtilesPerTile = 6 * 7;  // worldmap.cc:64-65

    public static VanillaSave Read(Stream saveDat, VanillaSaveContext ctx)
    {
        var r = new BigEndianReader(saveDat);
        SaveHeader header = ReadHeader(r);                                  // lsgLoadHeaderInSlot (:1902)
        int dudeCid = r.ReadInt32();                                        // _LoadObjDudeCid (:2923)
        int[] gvars = r.ReadInt32Array(ctx.GlobalVarCount);                 // scriptsLoadGameGlobalVars (scripts.cc:1735)
        (string[] maps, int automapSize) = ReadMapList(r);                  // _SlotMap2Game (:2548)
        int[] gvars2 = r.ReadInt32Array(ctx.GlobalVarCount);                // scriptsSkipGameGlobalVars (scripts.cc:1746)
        if (!gvars.AsSpan().SequenceEqual(gvars2))
            throw new InvalidDataException("SAVE.DAT global-variable copies differ — wrong GlobalVarCount?");
        MapObject dude = ReadDude(r, ctx);                                   // _obj_load_dude (object.cc:3629)
        int centerTile = r.ReadInt32();
        CritterSection critter = ReadCritter(r);                            // critterLoad (critter.cc:203)
        int[] kills = r.ReadInt32Array(KillTypeCount);                      // killsLoad (critter.cc:723)
        int[] tagged = r.ReadInt32Array(TaggedSkillCount);                  // skillsLoad (skill.cc:185)
        // randomLoad (random.cc:77): 0 bytes
        int[][] perks = Enumerable.Range(0, ctx.PartyDescriptionCount)      // perksLoad (perk.cc:253)
            .Select(_ => r.ReadInt32Array(PerkCount)).ToArray();
        CombatSection combat = ReadCombat(r);                               // combatLoad (combat.cc:2093)
        int[][] ai = ctx.PartyDescriptionPids.Where(ctx.HasCustomAiPacket)  // aiLoad (combat_ai.cc:573)
            .Select(_ => r.ReadInt32Array(AiPacketInts)).ToArray();
        int[] pc = r.ReadInt32Array(PcStatCount);                           // statsLoad (stat.cc:158)
        // itemsLoad (item.cc:241): 0 bytes
        int[] traits = r.ReadInt32Array(TraitsSelected);                    // traitsLoad (trait.cc:111)
        int automapFlags = r.ReadInt32();                                   // automapLoad (automap.cc:280)
        byte[] prefs = r.ReadBytes(80);                                     // preferencesLoad (preferences.cc:860)
        int lastLevel = r.ReadInt32();                                      // characterEditorLoad (character_editor.cc:5661)
        bool freePerk = r.ReadByte() != 0;
        WorldmapSection wm = ReadWorldmap(r);                               // wmWorldMap_load (worldmap.cc:1149)
        // pipboyLoad (pipboy.cc:868): 0 bytes
        byte[] movies = r.ReadBytes(MovieCount);                            // gameMoviesLoad (game_movie.cc:119)
        int[] usage = r.ReadInt32Array(SkillCount * SkillUsesPerDay);       // skillsUsageLoad (skill.cc:1196)
        (int itemCount, int[] memberIds, PartyMemberLevel[] levels) = ReadParty(r, ctx); // partyMembersLoad (party_member.cc:713)
        QueueEventSave[] queue = ReadQueue(r);                              // queueLoad (queue.cc:84)
        int[] iface = r.ReadInt32Array(4);                                  // interfaceLoad (interface.cc:702)
        // _EndLoad (:2421): 0 bytes

        return new VanillaSave
        {
            Header = header, DudeCid = dudeCid, GlobalVars = gvars, MapFiles = maps, AutomapDbSize = automapSize,
            Dude = dude, DudeCenterTile = centerTile, Critter = critter, Kills = kills, TaggedSkills = tagged,
            PerksByDescription = perks, Combat = combat, CustomAiPackets = ai,
            PcStats = new PcStatsSection(pc[0], pc[1], pc[2], pc[3], pc[4]), Traits = traits,
            AutomapFlags = automapFlags, PreferencesRaw = prefs, CharacterEditorLastLevel = lastLevel,
            CharacterEditorHasFreePerk = freePerk, Worldmap = wm, MoviesSeen = movies, SkillUsage = usage,
            PartyItemCount = itemCount, PartyMemberIds = memberIds, PartyLevels = levels, Queue = queue,
            InterfaceRaw = iface, TotalLength = saveDat.Position,
        };
    }

    // ported from fallout2-ce src/loadsave.cc lsgLoadHeaderInSlot() (:1902) / writer (:1796-1898)
    private static SaveHeader ReadHeader(BigEndianReader r)
    {
        string signature = Ascii(r.ReadBytes(24));
        if (!signature.StartsWith("FALLOUT SAVE FILE", StringComparison.Ordinal))
            throw new InvalidDataException("not a Fallout 2 SAVE.DAT (signature)");
        short v1 = r.ReadInt16(), v2 = r.ReadInt16();
        byte release = r.ReadByte();
        if (v1 != 1 || v2 != 2 || release != (byte)'R')
            throw new InvalidDataException($"unsupported SAVE.DAT version {v1}.{v2}{(char)release}");
        string name = Ascii(r.ReadBytes(32));
        string description = Ascii(r.ReadBytes(30));
        r.Skip(3 * 2 + 4);                         // real-world date + meaningless fileTime
        int month = r.ReadInt16(), day = r.ReadInt16(), year = r.ReadInt16();
        uint gameTime = r.ReadUInt32();
        int elevation = r.ReadInt16(), mapIndex = r.ReadInt16();
        string mapFile = Ascii(r.ReadBytes(16));
        r.Skip(29792 + 128);                       // thumbnail (LS_PREVIEW_SIZE) + padding
        return new SaveHeader(name, description, month, day, year, gameTime, elevation, mapIndex, mapFile);
    }

    // ported from fallout2-ce src/loadsave.cc _SlotMap2Game() (:2548) + _mygets() (:2644)
    private static (string[], int) ReadMapList(BigEndianReader r)
    {
        int count = r.ReadInt32();
        if (count <= 0)
            throw new InvalidDataException("SAVE.DAT map list is empty");
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            var sb = new StringBuilder();
            for (byte b = r.ReadByte(); b != 0; b = r.ReadByte())
                sb.Append((char)b);
            names[i] = sb.ToString();
        }
        return (names, r.ReadInt32());
    }

    private static MapObject ReadDude(BigEndianReader r, VanillaSaveContext ctx) =>
        // _obj_load_dude → _obj_load_obj (object.cc:3536) — same record as a map object, map version 20.
        MapFile.ReadObject(r, ctx.Protos!, mapVersion: 20);

    // ported from fallout2-ce src/critter.cc critterLoad() (:203) + protoCritterDataRead() (:1064)
    private static CritterSection ReadCritter(BigEndianReader r)
    {
        int sneak = r.ReadInt32();
        int flags = r.ReadInt32();
        int[] baseStats = r.ReadInt32Array(StatSaveableCount);
        int[] bonusStats = r.ReadInt32Array(StatSaveableCount);
        int[] skills = r.ReadInt32Array(SkillCount);
        return new CritterSection(sneak, flags, baseStats, bonusStats, skills,
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
    }

    // ported from fallout2-ce src/combat.cc combatLoad() (:2093) / combatSave() (:2214-2243)
    private static CombatSection ReadCombat(BigEndianReader r)
    {
        uint state = r.ReadUInt32();
        if ((state & 1) == 0)
            return new CombatSection(false, state, 0, 0, 0, 0, 0, -1, [], []);
        int turnRunning = r.ReadInt32(), freeMove = r.ReadInt32(), exps = r.ReadInt32();
        int listCom = r.ReadInt32(), listNoncom = r.ReadInt32(), listTotal = r.ReadInt32();
        int dudeCid = r.ReadInt32();
        int[] cids = r.ReadInt32Array(listTotal);
        var data = new CombatantSaveData[listTotal];
        for (int i = 0; i < listTotal; i++)
            data[i] = new CombatantSaveData(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        return new CombatSection(true, state, turnRunning, freeMove, exps, listCom, listNoncom, dudeCid, cids, data);
    }

    // ported from fallout2-ce src/worldmap.cc wmWorldMap_load() (:1149) / wmWorldMap_save() (:1074-1140)
    private static WorldmapSection ReadWorldmap(BigEndianReader r)
    {
        bool frank = r.ReadInt32() != 0;
        int area = r.ReadInt32(), x = r.ReadInt32(), y = r.ReadInt32();
        bool icon = r.ReadInt32() != 0;
        int encMap = r.ReadInt32(), encTable = r.ReadInt32(), encEntry = r.ReadInt32();
        bool inCar = r.ReadInt32() != 0;
        int carArea = r.ReadInt32(), carFuel = r.ReadInt32();
        int cityCount = r.ReadInt32();
        var cities = new CitySave[cityCount];
        for (int i = 0; i < cityCount; i++)
        {
            int cx = r.ReadInt32(), cy = r.ReadInt32(), state = r.ReadInt32(), visited = r.ReadInt32();
            int entrances = r.ReadInt32();
            cities[i] = new CitySave(cx, cy, state, visited, r.ReadInt32Array(entrances));
        }
        int tiles = r.ReadInt32(), horizontal = r.ReadInt32();
        int[] subtiles = r.ReadInt32Array(tiles * SubtilesPerTile);
        int counterCount = r.ReadInt32();
        var counters = new (int, int, int)[counterCount];
        for (int i = 0; i < counterCount; i++)
            counters[i] = (r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        return new WorldmapSection(frank, area, x, y, icon, encMap, encTable, encEntry, inCar, carArea, carFuel,
            cities, horizontal, subtiles, counters);
    }

    // ported from fallout2-ce src/party_member.cc partyMembersLoad() (:713) / writer (:520-538)
    private static (int, int[], PartyMemberLevel[]) ReadParty(BigEndianReader r, VanillaSaveContext ctx)
    {
        int length = r.ReadInt32();
        int itemCount = r.ReadInt32();
        int[] ids = length > 0 ? r.ReadInt32Array(length - 1) : [];
        var levels = new PartyMemberLevel[Math.Max(ctx.PartyDescriptionCount - 1, 0)];
        for (int i = 0; i < levels.Length; i++)
            levels[i] = new PartyMemberLevel(r.ReadInt32(), r.ReadInt32(), r.ReadInt32() != 0);
        return (itemCount, ids, levels);
    }

    // ported from fallout2-ce src/queue.cc queueLoad() (:84) + gEventTypeDescriptions readProcs (:52-67)
    private static QueueEventSave[] ReadQueue(BigEndianReader r)
    {
        int count = r.ReadInt32();
        var events = new QueueEventSave[count];
        for (int i = 0; i < count; i++)
        {
            uint time = r.ReadUInt32();
            int type = r.ReadInt32();
            int owner = r.ReadInt32();
            // Valid types are 0..13 (queue.h EventType, EVENT_TYPE_COUNT=14); only drug/withdrawal/script/
            // radiation carry a readProc (queue.cc gEventTypeDescriptions :54-67), all others serialize 0 bytes.
            if (type < 0 || type > 13)
                throw new InvalidDataException($"SAVE.DAT queue event {i} has unknown type {type}");
            int dataInts = type switch
            {
                0 => 6, // drug: drugEffectEventRead (item.cc:2886)
                2 => 3, // withdrawal: withdrawalEventRead (item.cc:3005)
                3 => 2, // script: scriptEventRead (scripts.cc:836)
                6 => 2, // radiation: radiationEventRead (critter.cc:647)
                _ => 0,
            };
            events[i] = new QueueEventSave(time, type, owner, r.ReadInt32Array(dataInts));
        }
        return events;
    }

    private static string Ascii(byte[] bytes)
    {
        int nul = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, nul >= 0 ? nul : bytes.Length);
    }
}
