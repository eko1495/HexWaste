namespace Hexwaste.Formats.Proto;

/// <summary>
/// Minimal prototype info needed by the PoC: enough to size variable-length
/// object data while parsing MAP files (item/scenery subtype) and to resolve
/// rendering flags/FRM ids. Header layout ported from fallout2-ce
/// src/proto.cc protoRead(); values are big-endian.
/// </summary>
public sealed record ProtoInfo(
    int Pid,
    int MessageId,
    int Fid,
    int Flags,
    int ExtendedFlags,
    /// <summary>Item type or scenery type; -1 for other object types.</summary>
    int SubType,
    /// <summary>Sound id char for sfx names (scenery field_34 / item field_80); 0 when absent.</summary>
    byte SoundId = 0,
    /// <summary>Inventory-list icon FID (items only); -1 otherwise.</summary>
    int InventoryFid = -1,
    /// <summary>The critter stat block; null for non-critter protos.</summary>
    CritterProtoStats? Critter = null,
    /// <summary>Base price (items only; proto.cc protoRead cost @ byte 48).</summary>
    int Cost = 0,
    WeaponProtoStats? Weapon = null,
    ArmorProtoStats? Armor = null,
    DrugProtoStats? Drug = null);

/// <summary>Weapon payload, ported from fallout2-ce src/proto.cc
/// protoItemDataRead() ITEM_TYPE_WEAPON. The attack animation comes from
/// extendedFlags &amp; 0xF via item.cc _attack_anim[].</summary>
public sealed record WeaponProtoStats(
    int AnimationCode,
    int MinDamage,
    int MaxDamage,
    int DamageType,
    int MaxRange1,
    int MaxRange2,
    int ApCost);

/// <summary>Armor payload (protoItemDataRead ITEM_TYPE_ARMOR): AC then
/// DR[7] then DT[7], by damage type (0 = normal).</summary>
public sealed record ArmorProtoStats(int ArmorClass, int[] DamageResistance, int[] DamageThreshold);

/// <summary>Drug payload (protoItemDataRead ITEM_TYPE_DRUG): three affected
/// stats + immediate amounts. Stat -1 = unused; stats[0] == -2 means
/// amounts[0..1] are a random range applied to stats[1] (item.cc
/// _perform_drug_effect — the stimpak heal roll).</summary>
public sealed record DrugProtoStats(int[] Stats, int[] Amounts);

/// <summary>
/// Critter prototype combat data, ported from fallout2-ce src/proto.cc
/// protoRead() (headFid/aiPacket/team after sid) + src/critter.cc
/// protoCritterDataRead(). Stat indices follow src/stat_defs.h (see
/// <see cref="CritterStat"/>); skills follow src/skill_defs.h (unarmed = 3).
/// </summary>
public sealed record CritterProtoStats(
    int AiPacket,
    int Team,
    int CritterFlags,
    int[] BaseStats,
    int[] BonusStats,
    int[] Skills,
    int BodyType,
    int Experience,
    int KillType,
    /// <summary>Natural unarmed damage type; absent in two 412-byte protos → 0 (normal).</summary>
    int DamageType);

/// <summary>
/// Lazily loads .pro prototypes via the VFS, following fallout2-ce
/// src/proto.cc _proto_list_str(): the file name comes from line
/// (pid &amp; 0xFFFFFF), 1-based, of proto\&lt;type&gt;\&lt;type&gt;.lst.
/// </summary>
public sealed class ProtoDatabase(GameFileSystem vfs)
{
    private static readonly string[] TypeDirs =
        ["items", "critters", "scenery", "walls", "tiles", "misc"];

    private readonly Dictionary<int, ProtoInfo> _cache = [];
    private readonly Dictionary<int, string[]> _lists = [];

    public ProtoInfo Get(int pid)
    {
        if (_cache.TryGetValue(pid, out ProtoInfo? cached))
            return cached;

        ProtoInfo info;
        try
        {
            info = Load(pid);
        }
        catch (EndOfStreamException ex)
        {
            // Truncated/short .pro — surface as the exception type every
            // caller already soft-handles.
            throw new InvalidDataException($"PID 0x{pid:X8}: proto file too short.", ex);
        }

        _cache[pid] = info;
        return info;
    }

    private ProtoInfo Load(int pid)
    {
        int type = Fid.PidType(pid);
        if (type < 0 || type >= TypeDirs.Length)
            throw new InvalidDataException($"PID 0x{pid:X8} has unsupported type {type}.");

        string[] list = GetList(type);
        int index = Fid.PidIndex(pid) - 1; // .lst lines are 1-based
        if (index < 0 || index >= list.Length)
            throw new InvalidDataException($"PID 0x{pid:X8} is out of range of {TypeDirs[type]}.lst ({list.Length} lines).");

        string virtualPath = $@"proto\{TypeDirs[type]}\{list[index]}";
        using Stream stream = vfs.OpenRead(virtualPath);
        var reader = new BigEndianReader(stream);

        // ported from fallout2-ce src/proto.cc protoRead()
        int filePid = reader.ReadInt32();
        int messageId = reader.ReadInt32();
        int fid = reader.ReadInt32();

        int flags;
        int extendedFlags;
        int subType = -1;
        byte soundId = 0;
        int inventoryFid = -1;
        int cost = 0;
        CritterProtoStats? critter = null;
        WeaponProtoStats? weapon = null;
        ArmorProtoStats? armor = null;
        DrugProtoStats? drug = null;
        switch ((ObjectType)type)
        {
            // ported from fallout2-ce src/proto.cc protoRead(): misc protos
            // end after extendedFlags — they have NO sid field (exit grids'
            // .pro files are exactly that short).
            case ObjectType.Misc:
                reader.Skip(8); // lightDistance, lightIntensity
                flags = reader.ReadInt32();
                extendedFlags = reader.ReadInt32();
                break;

            case ObjectType.Item:
            case ObjectType.Critter:
            case ObjectType.Scenery:
            case ObjectType.Wall:
                reader.Skip(8); // lightDistance, lightIntensity
                flags = reader.ReadInt32();
                extendedFlags = reader.ReadInt32();
                reader.Skip(4); // sid
                if ((ObjectType)type is ObjectType.Item or ObjectType.Scenery)
                    subType = reader.ReadInt32();

                // ported from fallout2-ce src/proto.cc protoRead(): scenery's
                // sound char is field_34 (after field_2C); items' is field_80
                // (after material/size/weight/cost/inventoryFid).
                if ((ObjectType)type is ObjectType.Scenery)
                {
                    reader.Skip(4); // field_2C
                    soundId = reader.ReadByte();
                }
                else if ((ObjectType)type is ObjectType.Item)
                {
                    reader.Skip(3 * 4); // material, size, weight
                    cost = reader.ReadInt32();
                    inventoryFid = reader.ReadInt32();
                    soundId = reader.ReadByte();

                    // ported from fallout2-ce src/proto.cc protoItemDataRead()
                    switch (subType)
                    {
                        case 0: // ITEM_TYPE_ARMOR: AC, DR[7], DT[7]
                            armor = new ArmorProtoStats(reader.ReadInt32(),
                                reader.ReadInt32Array(7), reader.ReadInt32Array(7));
                            break;
                        case 2: // ITEM_TYPE_DRUG: stat[3], amount[3]
                            drug = new DrugProtoStats(reader.ReadInt32Array(3), reader.ReadInt32Array(3));
                            break;
                        case 3: // ITEM_TYPE_WEAPON
                        {
                            int animationCode = reader.ReadInt32();
                            int minDamage = reader.ReadInt32();
                            int maxDamage = reader.ReadInt32();
                            int damageType = reader.ReadInt32();
                            int maxRange1 = reader.ReadInt32();
                            int maxRange2 = reader.ReadInt32();
                            reader.Skip(2 * 4); // projectilePid, minStrength
                            int apCost = reader.ReadInt32(); // actionPointCost1
                            weapon = new WeaponProtoStats(animationCode, minDamage, maxDamage,
                                damageType, maxRange1, maxRange2, apCost);
                            break;
                        }
                    }
                }
                else if ((ObjectType)type is ObjectType.Critter)
                {
                    // ported from fallout2-ce src/proto.cc protoRead() (critter
                    // case) + src/critter.cc protoCritterDataRead()
                    reader.Skip(4); // headFid
                    int aiPacket = reader.ReadInt32();
                    int team = reader.ReadInt32();
                    int critterFlags = reader.ReadInt32();
                    int[] baseStats = reader.ReadInt32Array(35);
                    int[] bonusStats = reader.ReadInt32Array(35);
                    int[] skills = reader.ReadInt32Array(18);
                    int bodyType = reader.ReadInt32();
                    int experience = reader.ReadInt32();
                    int killType = reader.ReadInt32();

                    // Two 412-byte protos (Sentry Bot, Weak Brahmin) end here;
                    // the engine defaults their damage type to normal.
                    int damageType = 0;
                    try
                    {
                        damageType = reader.ReadInt32();
                    }
                    catch (EndOfStreamException)
                    {
                    }

                    critter = new CritterProtoStats(aiPacket, team, critterFlags,
                        baseStats, bonusStats, skills, bodyType, experience, killType, damageType);
                }
                break;

            case ObjectType.Tile:
                flags = reader.ReadInt32();
                extendedFlags = reader.ReadInt32();
                break;

            default:
                throw new InvalidDataException($"PID 0x{pid:X8}: unexpected type {type}.");
        }

        return new ProtoInfo(filePid, messageId, fid, flags, extendedFlags, subType, soundId, inventoryFid, critter, cost, weapon, armor, drug);
    }

    private string[] GetList(int type)
    {
        if (_lists.TryGetValue(type, out string[]? cached))
            return cached;

        string dir = TypeDirs[type];
        using Stream stream = vfs.OpenRead($@"proto\{dir}\{dir}.lst");
        using var reader = new StreamReader(stream);

        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            // ported from fallout2-ce src/proto.cc _proto_list_str():
            // the file name ends at the first space.
            int space = line.IndexOf(' ');
            lines.Add((space >= 0 ? line[..space] : line).Trim());
        }

        string[] result = [.. lines];
        _lists[type] = result;
        return result;
    }
}
