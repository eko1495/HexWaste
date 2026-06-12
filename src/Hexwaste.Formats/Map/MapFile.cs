using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Map;

public sealed record MapHeader(
    int Version,
    string Name,
    int EnteringTile,
    int EnteringElevation,
    int EnteringRotation,
    int LocalVariablesCount,
    int ScriptIndex,
    int Flags,
    int Darkness,
    int GlobalVariablesCount,
    int Index,
    uint LastVisitTime);

/// <summary>One map elevation: 100x100 square tile grid + object list.</summary>
public sealed class MapElevation
{
    public const int SquareGridWidth = 100;
    public const int SquareGridHeight = 100;
    public const int SquareGridSize = SquareGridWidth * SquareGridHeight;

    /// <summary>Raw square values: low 16 bits = floor tile id, high 16 bits = roof tile id.</summary>
    public required int[] Squares { get; init; }

    public List<MapObject> Objects { get; } = [];

    public int FloorTileId(int square) => Squares[square] & 0xFFFF;
    public int RoofTileId(int square) => (Squares[square] >> 16) & 0xFFFF;
}

/// <summary>A static object placed on the 200x200 hex grid.</summary>
public sealed class MapObject
{
    public required int Id { get; init; }

    /// <summary>Hex grid tile number (0..39999), or -1 for inventory items.</summary>
    public required int HexTile { get; set; }

    /// <summary>Pixel offset from the hex tile center (objects can be nudged off-grid).</summary>
    public required int X { get; init; }
    public required int Y { get; init; }

    public required int Frame { get; init; }
    public required int Rotation { get; set; }
    public required int Fid { get; set; }
    public required int Flags { get; set; }
    public required int Pid { get; init; }

    /// <summary>Light emission: radius in hexes (max 8) and intensity (0..65536).</summary>
    public int LightDistance { get; init; }
    public int LightIntensity { get; init; }

    /// <summary>Script id (type in the high byte), or -1; key into MapFile.ScriptsBySid.</summary>
    public int Sid { get; set; } // settable: engine removes a critter's script on death (combat.cc:4876)

    /// <summary>The serialized "updated flags" (items carry their lock bit here).</summary>
    public int UpdatedFlags { get; set; }

    /// <summary>Doors' openFlags (scenery subtype 0) — lock bit 0x02000000, jam 0x04000000.</summary>
    public int DoorOpenFlags { get; set; }

    /// <summary>
    /// Lock state, ported from fallout2-ce proto_instance.cc objectIsLocked():
    /// items check data.flags, scenery checks door.openFlags; OBJ_LOCKED = 0x02000000.
    /// </summary>
    public bool IsLockedState
    {
        get => Hexwaste.Formats.Fid.PidType(Pid) switch
        {
            0 => (UpdatedFlags & 0x02000000) != 0, // item/container
            2 => (DoorOpenFlags & 0x02000000) != 0, // scenery/door
            _ => false,
        };
        set
        {
            switch (Hexwaste.Formats.Fid.PidType(Pid))
            {
                case 0:
                    UpdatedFlags = value ? UpdatedFlags | 0x02000000 : UpdatedFlags & ~0x02000000;
                    break;
                case 2:
                    DoorOpenFlags = value ? DoorOpenFlags | 0x02000000 : DoorOpenFlags & ~0x02000000;
                    break;
            }
        }
    }
    public List<MapObject> Inventory { get; } = [];

    /// <summary>Stack size when this object sits in an inventory (MAP quantity field).</summary>
    public int StackCount { get; set; } = 1;

    // Critter instance data (obj_pud), meaningful only for critter pids.
    // ported from fallout2-ce src/obj_types.h CritterObjectData/CritterCombatData
    public int DamageLastTurn { get; set; }
    public int Maneuver { get; set; }
    public int ActionPoints { get; set; }

    /// <summary>DAM_* result flags; DAM_DEAD = 0x80.</summary>
    public int CombatResults { get; set; }
    public int AiPacket { get; set; }
    public int Team { get; set; }
    public int WhoHitMeCid { get; set; }

    /// <summary>Per-instance current HP (denbus1 critters carry individual values).</summary>
    public int CurrentHp { get; set; }
    public int Radiation { get; set; }
    public int Poison { get; set; }

    /// <summary>ported from fallout2-ce src/critter.cc critterIsDead(): DAM_DEAD.</summary>
    public bool IsDead => (CombatResults & 0x80) != 0;

    /// <summary>Loaded rounds (weapons) or rounds in the box (ammo items);
    /// -1 = derive from the prototype (fresh item / pre-V2 save).</summary>
    public int AmmoQuantity { get; set; } = -1;

    /// <summary>Pid of the loaded ammo (weapons); -1 = proto default.</summary>
    public int AmmoTypePid { get; set; } = -1;

    // ported from fallout2-ce src/obj_types.h
    public bool IsHidden => (Flags & 0x01) != 0;
    public bool IsFlat => (Flags & 0x08) != 0;

    // Equip state lives as flags on the ITEM object (obj_types.h:78-87);
    // MAP files store them verbatim.
    public const int FlagInLeftHand = 0x01000000;
    public const int FlagInRightHand = 0x02000000;
    public const int FlagWorn = 0x04000000;

    public bool IsInHand => (Flags & (FlagInLeftHand | FlagInRightHand)) != 0;
    public bool IsWorn => (Flags & FlagWorn) != 0;

    /// <summary>Travel destination of exit grids, stairs and ladders; null otherwise.</summary>
    public MapDestination? Destination { get; set; }
}

/// <summary>
/// A map script-section record: which scripts.lst script a sid runs, and the
/// script's slice of the map's local-variables array (get/set_local_var(v)
/// addresses mapLocalVars[LocalVarsOffset + v] — fallout2-ce scripts.cc:2808).
/// Pristine maps store a valid mapper-assigned offset; LocalVarsCount is
/// zeroed there and re-derived from scripts.lst's "# local_vars=N" comment.
/// </summary>
public sealed record MapScriptRecord(int ScriptListIndex, int LocalVarsOffset, int LocalVarsCount);

/// <summary>
/// Where an exit grid / stairs / ladder leads. Map &gt; 0 means another map
/// (index into data\maps.txt); otherwise the same map. ported from
/// fallout2-ce src/proto_instance.cc useStairs()/useLadder*() and the exit
/// grid fields in objectDataRead().
/// </summary>
public sealed record MapDestination(int Map, int Tile, int Elevation, int Rotation)
{
    /// <summary>Decodes a built tile: bits 0..25 tile, 26..28 rotation, 29..31 elevation
    /// (fallout2-ce src/obj_types.h builtTileGet*).</summary>
    public static MapDestination? FromBuiltTile(int map, int builtTile)
    {
        if (builtTile == -1)
            return null;
        return new MapDestination(
            map,
            builtTile & 0x3FFFFFF,
            (builtTile & unchecked((int)0xE0000000)) >>> 29,
            (builtTile & 0x1C000000) >> 26);
    }
}

/// <summary>
/// Fallout 2 MAP file. Sections ported from fallout2-ce:
/// header — src/map.cc mapHeaderRead(); global/local vars — mapGlobalVariablesLoad()/
/// mapLocalVariablesLoad(); square grids — _square_load(); scripts — src/scripts.cc
/// scriptLoadAll() (parsed only to advance the stream); objects — src/object.cc
/// objectLoadAllInternal()/objectRead() + src/proto.cc objectDataRead().
/// All values are big-endian.
/// </summary>
public sealed class MapFile
{
    public const int ElevationCount = 3;

    public required MapHeader Header { get; init; }
    public required int[] GlobalVariables { get; init; }
    public required int[] LocalVariables { get; init; }

    /// <summary>Script id → record from the scripts section (scripts.lst index + LVAR slice).</summary>
    public Dictionary<int, MapScriptRecord> ScriptsBySid { get; } = [];

    /// <summary>Spatial trigger scripts (type-1 sids): trap corridors etc.</summary>
    public List<SpatialScript> SpatialScripts { get; } = [];

    /// <summary>A spatial script record: exact tile (radius 0) or a radius
    /// circle on one elevation (built_tile decoded like MapDestination).</summary>
    public sealed record SpatialScript(int Sid, int ScriptListIndex, int Tile, int Elevation, int Radius);

    /// <summary>Indexed by elevation; null when the map has no data for that elevation.</summary>
    public required MapElevation?[] Elevations { get; init; }

    public static MapFile Load(Stream stream, ProtoDatabase protos)
    {
        var reader = new BigEndianReader(stream);

        MapHeader header = ReadHeader(reader);
        if (header.Version is not (19 or 20))
            throw new InvalidDataException($"Unsupported map version {header.Version} (Fallout 2 maps are version 20).");

        int[] globalVars = reader.ReadInt32Array(Math.Max(header.GlobalVariablesCount, 0));
        int[] localVars = reader.ReadInt32Array(Math.Max(header.LocalVariablesCount, 0));

        var elevations = new MapElevation?[ElevationCount];
        for (int elevation = 0; elevation < ElevationCount; elevation++)
        {
            // ported from fallout2-ce src/map.cc _map_data_elev_flags:
            // flag bits {2, 4, 8} mark ABSENT elevations.
            if ((header.Flags & (2 << elevation)) != 0)
                continue;

            int[] squares = reader.ReadInt32Array(MapElevation.SquareGridSize);
            for (int i = 0; i < squares.Length; i++)
            {
                // ported from fallout2-ce src/map.cc _square_load(): clear bit 12
                // of the roof word.
                int high = (squares[i] >> 16) & 0xFFFF;
                high &= ~0x1000;
                squares[i] = (high << 16) | (squares[i] & 0xFFFF);
            }

            elevations[elevation] = new MapElevation { Squares = squares };
        }

        var map = new MapFile
        {
            Header = header,
            GlobalVariables = globalVars,
            LocalVariables = localVars,
            Elevations = elevations,
        };

        ReadScripts(reader, map.ScriptsBySid, map.SpatialScripts);
        ReadObjects(reader, elevations, protos, header.Version);

        return map;
    }

    private static MapHeader ReadHeader(BigEndianReader reader)
    {
        int version = reader.ReadInt32();
        byte[] nameBytes = reader.ReadBytes(16);
        int nul = Array.IndexOf(nameBytes, (byte)0);
        string name = System.Text.Encoding.ASCII.GetString(nameBytes, 0, nul >= 0 ? nul : 16);

        int enteringTile = reader.ReadInt32();
        int enteringElevation = reader.ReadInt32();
        int enteringRotation = reader.ReadInt32();
        int localVarsCount = reader.ReadInt32();
        int scriptIndex = reader.ReadInt32();
        int flags = reader.ReadInt32();
        int darkness = reader.ReadInt32();
        int globalVarsCount = reader.ReadInt32();
        int index = reader.ReadInt32();
        uint lastVisitTime = reader.ReadUInt32();
        reader.Skip(44 * 4); // reserved field_3C

        return new MapHeader(version, name, enteringTile, enteringElevation, enteringRotation,
            localVarsCount, scriptIndex, flags, darkness, globalVarsCount, index, lastVisitTime);
    }

    /// <summary>
    /// Reads the scripts section, keeping only the sid → scripts.lst index
    /// mapping needed to bind objects to script files.
    /// ported from fallout2-ce src/scripts.cc scriptLoadAll()/scriptListExtentRead()/
    /// scriptRead(): 5 script-type groups; each non-empty group is ceil(count/16)
    /// extents of 16 records + 2 trailing ints; every record is read in full even
    /// beyond the extent's logical length (the writer writes them symmetrically) —
    /// padding records beyond each extent's logical length carry garbage and are
    /// discarded.
    /// </summary>
    private static void ReadScripts(BigEndianReader reader, Dictionary<int, MapScriptRecord> scriptsBySid,
        List<SpatialScript> spatialScripts)
    {
        const int scriptTypeCount = 5;
        const int extentSize = 16;
        const int typeSpatial = 1;
        const int typeTimed = 2;

        for (int type = 0; type < scriptTypeCount; type++)
        {
            int count = reader.ReadInt32();
            if (count == 0)
                continue;

            int remaining = count;
            int extents = (count + extentSize - 1) / extentSize;
            for (int extent = 0; extent < extents; extent++)
            {
                for (int record = 0; record < extentSize; record++)
                {
                    int sid = reader.ReadInt32();
                    reader.Skip(4); // field_4

                    // SID_TYPE is an arithmetic shift, matching C++ (value) >> 24.
                    int builtTile = -1;
                    int radius = 0;
                    switch (sid >> 24)
                    {
                        case typeSpatial:
                            builtTile = reader.ReadInt32();
                            radius = reader.ReadInt32();
                            break;
                        case typeTimed:
                            reader.Skip(4); // time
                            break;
                    }

                    reader.Skip(4); // flags
                    int scriptListIndex = reader.ReadInt32();
                    reader.Skip(2 * 4); // prg, ownerId
                    int localVarsOffset = reader.ReadInt32();
                    int localVarsCount = reader.ReadInt32();
                    reader.Skip(8 * 4); // returnValue .. field_50

                    bool isPadding = record >= Math.Min(remaining, extentSize);
                    if (!isPadding && sid != -1)
                    {
                        scriptsBySid[sid] = new MapScriptRecord(scriptListIndex, localVarsOffset, localVarsCount);
                        if (sid >> 24 == typeSpatial && builtTile != -1)
                            spatialScripts.Add(new SpatialScript(sid, scriptListIndex,
                                builtTile & 0x3FFFFFF,
                                (builtTile & unchecked((int)0xE0000000)) >>> 29,
                                radius));
                    }
                }

                remaining -= extentSize;
                reader.Skip(8); // extent length + next pointer
            }
        }
    }

    private static void ReadObjects(BigEndianReader reader, MapElevation?[] elevations,
        ProtoDatabase protos, int mapVersion)
    {
        reader.ReadInt32(); // total object count

        for (int elevation = 0; elevation < ElevationCount; elevation++)
        {
            int countAtElevation = reader.ReadInt32();
            for (int i = 0; i < countAtElevation; i++)
            {
                MapObject obj = ReadObject(reader, protos, mapVersion);
                elevations[elevation]?.Objects.Add(obj);
            }
        }
    }

    /// <summary>ported from fallout2-ce src/object.cc objectRead().</summary>
    private static MapObject ReadObject(BigEndianReader reader, ProtoDatabase protos, int mapVersion)
    {
        int id = reader.ReadInt32();
        int tile = reader.ReadInt32();
        int x = reader.ReadInt32();
        int y = reader.ReadInt32();
        reader.Skip(2 * 4); // sx, sy — screen coords, recomputed by the renderer
        int frame = reader.ReadInt32();
        int rotation = reader.ReadInt32();
        int fid = reader.ReadInt32();
        int flags = reader.ReadInt32();
        reader.Skip(4); // elevation — implied by the section being read
        int pid = reader.ReadInt32();
        reader.Skip(4); // cid
        int lightDistance = reader.ReadInt32();
        int lightIntensity = reader.ReadInt32();
        reader.Skip(4); // field_74
        int sid = reader.ReadInt32();
        reader.Skip(4); // scriptIndex — resolved at runtime via the scripts section

        var obj = new MapObject
        {
            Id = id,
            HexTile = tile,
            X = x,
            Y = y,
            Frame = frame,
            Rotation = rotation,
            Fid = fid,
            Flags = flags,
            Pid = pid,
            LightDistance = lightDistance,
            LightIntensity = lightIntensity,
            Sid = sid,
        };

        int inventoryLength = ReadObjectData(reader, obj, protos, mapVersion);

        // ported from fallout2-ce src/object.cc objectRead(): remap legacy exit
        // grid art to the green/red variants.
        if (Fid.IsExitGridPid(pid) && obj.Destination?.Map <= 0 && (obj.Fid & 0xFFF) < 33)
            obj.Fid = Fid.Build(ObjectType.Misc, (obj.Fid & 0xFFF) + 16, Fid.AnimType(obj.Fid));

        for (int i = 0; i < inventoryLength; i++)
        {
            int quantity = reader.ReadInt32();
            MapObject item = ReadObject(reader, protos, mapVersion);
            item.StackCount = Math.Max(quantity, 1);
            obj.Inventory.Add(item);
        }

        return obj;
    }

    /// <summary>
    /// ported from fallout2-ce src/proto.cc objectDataRead(). Returns the
    /// inventory length; the items themselves follow as nested object records.
    /// </summary>
    private static int ReadObjectData(BigEndianReader reader, MapObject obj,
        ProtoDatabase protos, int mapVersion)
    {
        int inventoryLength = reader.ReadInt32();
        reader.Skip(8); // capacity + meaningless serialized pointer

        int pidType = Fid.PidType(obj.Pid);
        if (pidType == (int)ObjectType.Critter)
        {
            // ported from fallout2-ce src/proto.cc objectDataRead() +
            // objectCritterCombatDataRead()
            reader.Skip(4); // field_0 (reaction_to_pc)
            obj.DamageLastTurn = reader.ReadInt32();
            obj.Maneuver = reader.ReadInt32();
            obj.ActionPoints = reader.ReadInt32();
            obj.CombatResults = reader.ReadInt32();
            obj.AiPacket = reader.ReadInt32();
            obj.Team = reader.ReadInt32();
            obj.WhoHitMeCid = reader.ReadInt32();
            obj.CurrentHp = reader.ReadInt32();
            obj.Radiation = reader.ReadInt32();
            obj.Poison = reader.ReadInt32();
            return inventoryLength;
        }

        int updatedFlags = reader.ReadInt32();
        if (updatedFlags == unchecked((int)0xCCCCCCCC))
            updatedFlags = 0; // engine: "Reading pud: updated_flags was un-Set!"
        obj.UpdatedFlags = updatedFlags;

        switch ((ObjectType)pidType)
        {
            case ObjectType.Item:
                switch (protos.Get(obj.Pid).SubType)
                {
                    case 3: // ITEM_TYPE_WEAPON: loaded rounds + loaded ammo pid
                        obj.AmmoQuantity = reader.ReadInt32();
                        obj.AmmoTypePid = reader.ReadInt32();
                        break;
                    case 4: // ITEM_TYPE_AMMO: rounds in the box
                        obj.AmmoQuantity = reader.ReadInt32();
                        break;
                    case 5 or 6: // MISC charges / KEY keyCode
                        reader.Skip(4);
                        break;
                }
                break;

            case ObjectType.Scenery:
                switch (protos.Get(obj.Pid).SubType)
                {
                    case 0: // SCENERY_TYPE_DOOR
                        obj.DoorOpenFlags = reader.ReadInt32();
                        break;
                    case 1: // SCENERY_TYPE_STAIRS: builtTile then map
                    {
                        int builtTile = reader.ReadInt32();
                        int map = reader.ReadInt32();
                        obj.Destination = MapDestination.FromBuiltTile(map, builtTile);
                        break;
                    }
                    case 2: // SCENERY_TYPE_ELEVATOR: type+level (hardcoded tables; out of scope)
                        reader.Skip(8);
                        break;
                    case 3 or 4: // ladders: v19 has builtTile only; v20 adds map first
                    {
                        int map = 0;
                        if (mapVersion != 19)
                            map = reader.ReadInt32();
                        int builtTile = reader.ReadInt32();
                        obj.Destination = MapDestination.FromBuiltTile(map, builtTile);
                        break;
                    }
                }
                break;

            case ObjectType.Misc:
                if (Fid.IsExitGridPid(obj.Pid))
                {
                    int map = reader.ReadInt32();
                    int tile = reader.ReadInt32();
                    int elevation = reader.ReadInt32();
                    int rotation = reader.ReadInt32();
                    obj.Destination = new MapDestination(map, tile, elevation, rotation);
                }
                break;
        }

        return inventoryLength;
    }
}
