using Hexwaste.Formats;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;
using Hexwaste.Formats.Text;

// ContentAudit — phase-9 Track E content reality check.
// Scans maps for weapons/items/critters, classifies weapons by attack mode
// (ported from fallout2-ce src/item.cc _attack_anim/_attack_subtype), and
// reports per-PID + per-map.
// usage: ContentAudit --game-dir <dir> --map m1.map [--map m2.map ...]

string? gameDir = null;
var maps = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--game-dir" && i + 1 < args.Length) gameDir = args[++i];
    else if (args[i] == "--map" && i + 1 < args.Length) maps.Add(args[++i]);
}
if (gameDir is null || maps.Count == 0)
{
    Console.Error.WriteLine("usage: ContentAudit --game-dir <dir> --map m.map [--map ...]");
    return 1;
}

using GameFileSystem vfs = GameFileSystem.Open(gameDir);
var protos = new ProtoDatabase(vfs);
var names = new ProtoMessages(vfs, protos);

// _attack_subtype[9], item.cc:128-141
string[] attackTypeName = { "NONE", "UNARMED", "MELEE", "THROW", "RANGED" };
int[] attackSubtype = { 0, 1, 1, 2, 2, 3, 4, 4, 4 };
// _attack_anim[9] meaning, item.cc:116-125
string[] animName = { "STAND", "PUNCH", "KICK", "SWING", "THRUST", "THROW", "SINGLE", "BURST", "CONTINUOUS" };
string[] dmgTypeName = { "normal", "laser", "fire", "plasma", "electrical", "emp", "explosion" };

// dynamite/plastic explosive misc PIDs, proto_types.h:142,151,163,165
var explosivePids = new HashSet<int> { 51, 85, 206, 209 };

// Track which PIDs we have seen across all maps, with where.
var seen = new Dictionary<int, (ProtoInfo proto, string? name, SortedSet<string> where)>();

// Critter AI packet tally: (mapName, packetNum) -> count, and packetNum -> sample critter name.
var critterAi = new Dictionary<string, Dictionary<int, int>>();
var packetCritterName = new Dictionary<int, SortedSet<string>>();

void Note(int pid, string location)
{
    if (Fid.PidType(pid) != (int)ObjectType.Item && Fid.PidType(pid) != (int)ObjectType.Misc) return;
    if (!seen.TryGetValue(pid, out var rec))
    {
        ProtoInfo? p = null;
        try { p = protos.Get(pid); } catch { }
        if (p is null) return;
        rec = (p, SafeName(pid), new SortedSet<string>());
        seen[pid] = rec;
    }
    rec.where.Add(location);
}

string? SafeName(int pid)
{
    try { return names.GetName(pid); } catch { return null; }
}

var trackOwners = new HashSet<int> { 0x19, 0x9F, 0x33, 0x55, 0x07, 0x2D, 0x13 }; // grenade, molotov, dynamite, plastic, spear, throwknife, rock
var ownerLines = new List<string>();
void Walk(MapObject o, string mapName, int elev, bool inInventory, int ownerPid)
{
    Note(o.Pid, inInventory ? $"{mapName}[e{elev},inv]" : $"{mapName}[e{elev},ground]");
    if (inInventory && trackOwners.Contains(Fid.PidIndex(o.Pid)) && Fid.PidType(o.Pid) is 0 or 5)
    {
        string item = SafeName(o.Pid) ?? $"0x{o.Pid:X}";
        string owner = ownerPid == 0 ? "GROUND" : (SafeName(ownerPid) ?? $"0x{ownerPid:X}");
        ownerLines.Add($"{mapName} e{elev}: {item} x{o.StackCount} <- {owner} (0x{ownerPid:X})");
    }
    foreach (var inv in o.Inventory)
        Walk(inv, mapName, elev, true, o.Pid);
}

foreach (var mapName in maps)
{
    MapFile map;
    try
    {
        using Stream s = vfs.OpenRead($@"maps\{mapName}");
        map = MapFile.Load(s, protos);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"MAP {mapName}: FAILED to load: {ex.Message}");
        continue;
    }
    int totalObjs = 0, critters = 0;
    for (int e = 0; e < MapFile.ElevationCount; e++)
    {
        var el = map.Elevations[e];
        if (el is null) continue;
        foreach (var o in el.Objects)
        {
            totalObjs++;
            if (Fid.PidType(o.Pid) == (int)ObjectType.Critter)
            {
                critters++;
                // AiPacket field is the instance value; fall back to proto.
                int pkt = o.AiPacket;
                if (pkt == 0)
                {
                    try { pkt = protos.Get(o.Pid).Critter?.AiPacket ?? 0; } catch { }
                }
                if (!critterAi.TryGetValue(mapName, out var tally)) { tally = new(); critterAi[mapName] = tally; }
                tally[pkt] = tally.GetValueOrDefault(pkt) + 1;
                if (!packetCritterName.TryGetValue(pkt, out var nm)) { nm = new(); packetCritterName[pkt] = nm; }
                string? cn = null;
                try { cn = names.GetName(o.Pid); } catch { }
                if (cn != null) nm.Add(cn);
            }
            Walk(o, mapName, e, false, 0);
        }
    }
    Console.WriteLine($"MAP {mapName}: {totalObjs} objects, {critters} critters");
}

Console.WriteLine();
Console.WriteLine("=== WEAPONS (item subtype 3) ===");
Console.WriteLine($"{"PID",-12} {"name",-22} {"primary",-10} {"secondary",-10} {"dmg",-9} {"rng1/2",-9} {"rounds",-6} {"calib",-5} where");
foreach (var (pid, rec) in seen.OrderBy(k => k.Key))
{
    var p = rec.proto;
    if (p.SubType != 3 || p.Weapon is null) continue;
    var w = p.Weapon;
    int prim = p.ExtendedFlags & 0xF;
    int sec = (p.ExtendedFlags & 0xF0) >> 4;
    string primT = $"{animName[Math.Clamp(prim,0,8)]}/{attackTypeName[attackSubtype[Math.Clamp(prim,0,8)]]}";
    string secT = sec == 0 ? "-" : $"{animName[Math.Clamp(sec,0,8)]}/{attackTypeName[attackSubtype[Math.Clamp(sec,0,8)]]}";
    string dt = dmgTypeName[Math.Clamp(w.DamageType,0,6)];
    Console.WriteLine($"0x{pid:X8} {Trunc(rec.name,22),-22} {primT,-12} {secT,-12} {w.MinDamage}-{w.MaxDamage,-5} {w.MaxRange1}/{w.MaxRange2,-6} {w.Rounds,-6} {w.Caliber,-5} dmg={dt,-9} ext=0x{p.ExtendedFlags:X} {string.Join(",", rec.where)}");
}

Console.WriteLine();
Console.WriteLine("=== WEAPON CAPABILITY CLASSIFICATION ===");
ReportClass("BURST-capable (prim or sec anim==BURST/7)", p => HasAnim(p, 7));
ReportClass("THROWING-class (prim or sec attackType==THROW/3, anim 5)", p => HasAnim(p, 5));
ReportClass("EXPLOSIVE weapons (dmgType==explosion)", p => p.Weapon is not null && p.Weapon.DamageType == 6);
ReportClass("CONTINUOUS/flamer (anim==8)", p => HasAnim(p, 8));
ReportClass("AIMED-benefit guns (RANGED single, not explosive)",
    p => HasAnim(p, 6) && p.Weapon is not null && p.Weapon.DamageType != 6);

Console.WriteLine();
Console.WriteLine("=== EXPLOSIVE MISC ITEMS (dynamite/plastic, PIDs 51/85/206/209) ===");
bool anyExp = false;
foreach (var (pid, rec) in seen.OrderBy(k => k.Key))
{
    int idx = Fid.PidIndex(pid);
    if ((Fid.PidType(pid) == (int)ObjectType.Item || Fid.PidType(pid) == (int)ObjectType.Misc) && explosivePids.Contains(idx))
    {
        anyExp = true;
        Console.WriteLine($"0x{pid:X8} idx={idx} {Trunc(rec.name,24),-24} subtype={rec.proto.SubType} {string.Join(",", rec.where)}");
    }
}
if (!anyExp) Console.WriteLine("ABSENT in scanned maps.");

Console.WriteLine();
Console.WriteLine("=== ALL AMMO (subtype 4) ===");
foreach (var (pid, rec) in seen.OrderBy(k => k.Key))
{
    if (rec.proto.SubType != 4 || rec.proto.Ammo is null) continue;
    var a = rec.proto.Ammo;
    Console.WriteLine($"0x{pid:X8} {Trunc(rec.name,22),-22} calib={a.Caliber} qty={a.Quantity} AC{a.AcModifier:+0;-0;+0} DR{a.DrModifier:+0;-0;+0} x{a.DamageMultiplier}/{a.DamageDivisor} {string.Join(",", rec.where)}");
}

Console.WriteLine();
Console.WriteLine("=== THROWABLE/EXPLOSIVE OWNERSHIP (who carries it) ===");
foreach (var line in ownerLines.Distinct().OrderBy(s => s))
    Console.WriteLine("     " + line);

Console.WriteLine();
Console.WriteLine("=== CRITTER KILL TYPES (proto KillType -> which crit-table block) ===");
string[] killTypeName = { "MAN","WOMAN","CHILD","SUPER_MUTANT","GHOUL","BRAHMIN","RADSCORPION","RAT","FLOATER","CENTAUR","ROBOT","DOG","MANTIS","DEATH_CLAW","PLANT","GECKO","ALIEN","GIANT_ANT","BIG_BAD_BOSS" };
var killTypeCount = new Dictionary<int, (int count, SortedSet<string> names)>();
foreach (var mapName in maps)
{
    MapFile m;
    try { using Stream s = vfs.OpenRead($@"maps\{mapName}"); m = MapFile.Load(s, protos); } catch { continue; }
    for (int e = 0; e < MapFile.ElevationCount; e++)
    {
        var el = m.Elevations[e]; if (el is null) continue;
        foreach (var o in el.Objects)
        {
            if (Fid.PidType(o.Pid) != (int)ObjectType.Critter) continue;
            int kt = -1; string? cn = null;
            try { kt = protos.Get(o.Pid).Critter?.KillType ?? -1; cn = names.GetName(o.Pid); } catch { }
            if (!killTypeCount.TryGetValue(kt, out var rec)) { rec = (0, new()); }
            rec.count++; if (cn != null) rec.names.Add(cn); killTypeCount[kt] = rec;
        }
    }
}
foreach (var (kt, rec) in killTypeCount.OrderByDescending(k => k.Value.count))
{
    string ktn = kt >= 0 && kt < killTypeName.Length ? killTypeName[kt] : $"#{kt}";
    Console.WriteLine($"     killType {kt,2} {ktn,-13} x{rec.count,-4} {string.Join("/", rec.names.Take(4))}");
}

Console.WriteLine();
Console.WriteLine("=== CRITTER AI PACKETS (instance AiPacket, fallback proto) ===");
foreach (var (mapName, tally) in critterAi)
{
    Console.WriteLine($"-- {mapName}");
    foreach (var (pkt, cnt) in tally.OrderByDescending(k => k.Value))
    {
        string sample = packetCritterName.TryGetValue(pkt, out var nm) ? string.Join("/", nm.Take(3)) : "?";
        Console.WriteLine($"     packet {pkt,4} x{cnt,-3}  {sample}");
    }
}

return 0;

bool HasAnim(ProtoInfo p, int anim)
{
    if (p.SubType != 3) return false;
    int prim = p.ExtendedFlags & 0xF;
    int sec = (p.ExtendedFlags & 0xF0) >> 4;
    return prim == anim || sec == anim;
}

void ReportClass(string title, Func<ProtoInfo, bool> pred)
{
    var hits = seen.Where(k => pred(k.Value.proto)).OrderBy(k => k.Key).ToList();
    Console.WriteLine($"-- {title}: {hits.Count}");
    foreach (var h in hits)
        Console.WriteLine($"     0x{h.Key:X8} {Trunc(h.Value.name,24),-24} {string.Join(",", h.Value.where)}");
}

static string Trunc(string? s, int n) => s is null ? "?" : (s.Length <= n ? s : s[..n]);
