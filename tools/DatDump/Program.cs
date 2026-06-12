using Hexwaste.Formats;
using Hexwaste.Formats.Dat2;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

string? gameDir = null;
string? datFile = null;
var rest = new List<string>();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--game-dir":
            gameDir = RequireValue(args, ref i);
            break;
        case "--dat":
            datFile = RequireValue(args, ref i);
            break;
        default:
            rest.Add(args[i]);
            break;
    }
}

if (rest.Count == 0)
{
    PrintUsage();
    return 1;
}

string command = rest[0];

try
{
    switch (command)
    {
        case "list":
        {
            string? filter = rest.Count > 1 ? rest[1] : null;
            foreach ((Dat2Entry entry, string source) in EnumerateEntries(gameDir, datFile))
            {
                if (filter is not null && !entry.Path.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                string flag = entry.Compressed ? "z" : "-";
                Console.WriteLine($"{flag} {entry.UncompressedSize,10} {entry.Path}  [{source}]");
            }
            return 0;
        }

        case "extract":
        {
            if (rest.Count < 2)
            {
                Console.Error.WriteLine("extract requires a virtual path, e.g. extract color.pal");
                return 1;
            }
            string virtualPath = rest[1];
            string outPath = rest.Count > 2 ? rest[2] : Path.GetFileName(virtualPath.Replace('\\', '/'));

            byte[] data = datFile is not null
                ? OpenSingle(datFile).ReadAllBytes(virtualPath)
                : OpenVfs(gameDir).ReadAllBytes(virtualPath);

            File.WriteAllBytes(outPath, data);
            Console.WriteLine($"Extracted '{virtualPath}' -> '{outPath}' ({data.Length} bytes)");
            return 0;
        }

        case "info":
        {
            foreach (var archive in OpenArchives(gameDir, datFile))
            {
                long compressed = archive.Entries.Count(e => e.Compressed);
                Console.WriteLine($"{archive.Path}:");
                Console.WriteLine($"  entries: {archive.Entries.Count} ({compressed} compressed)");
                Console.WriteLine($"  data section offset: 0x{archive.DataSectionOffset:X}");
            }
            return 0;
        }

        default:
            Console.Error.WriteLine($"Unknown command '{command}'.");
            PrintUsage();
            return 1;
    }
}
catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidDataException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static string RequireValue(string[] args, ref int i)
{
    if (i + 1 >= args.Length)
        throw new ArgumentException($"{args[i]} requires a value");
    return args[++i];
}

static GameFileSystem OpenVfs(string? gameDir)
{
    if (gameDir is null)
        throw new DirectoryNotFoundException("Provide --game-dir <dir> (or --dat <file>).");
    return GameFileSystem.Open(gameDir);
}

static Dat2Archive OpenSingle(string datFile) => Dat2Archive.Open(datFile);

static IEnumerable<Dat2Archive> OpenArchives(string? gameDir, string? datFile)
{
    if (datFile is not null)
        return [OpenSingle(datFile)];
    return OpenVfs(gameDir).Archives;
}

static IEnumerable<(Dat2Entry, string)> EnumerateEntries(string? gameDir, string? datFile)
{
    foreach (var archive in OpenArchives(gameDir, datFile))
    {
        string source = Path.GetFileName(archive.Path);
        foreach (var entry in archive.Entries)
            yield return (entry, source);
    }
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        DatDump — Fallout 2 DAT2 archive inspector

        usage:
          DatDump (--game-dir <dir> | --dat <file>) list [substring-filter]
          DatDump (--game-dir <dir> | --dat <file>) extract <virtual\path> [out-file]
          DatDump (--game-dir <dir> | --dat <file>) info
        """);
}
