using Hexwaste.Formats.Dat2;

namespace Hexwaste.Formats;

/// <summary>
/// Virtual file system over a Fallout 2 installation, modeled on fallout2-ce
/// src/xfile.cc xfileOpen(): mounted bases are searched in order and the first
/// hit wins, so loose files in <c>data/</c> override patch000.dat, which
/// overrides critter.dat, which overrides master.dat.
/// Virtual paths use '\' separators and are case-insensitive.
/// </summary>
public sealed class GameFileSystem : IDisposable
{
    private readonly string _looseRoot;
    private readonly List<Dat2Archive> _archives;

    public string GameDir { get; }
    public IReadOnlyList<Dat2Archive> Archives => _archives;

    private GameFileSystem(string gameDir, string looseRoot, List<Dat2Archive> archives)
    {
        GameDir = gameDir;
        _looseRoot = looseRoot;
        _archives = archives;
    }

    /// <summary>
    /// Mounts <c>data/</c> (loose files), then patch000.dat, critter.dat, master.dat
    /// — the same precedence the original engine gets from fallout2.cfg.
    /// </summary>
    public static GameFileSystem Open(string gameDir)
    {
        if (!Directory.Exists(gameDir))
            throw new DirectoryNotFoundException($"Game directory '{gameDir}' does not exist.");

        var archives = new List<Dat2Archive>();
        foreach (string datName in new[] { "patch000.dat", "critter.dat", "master.dat" })
        {
            string? datPath = ResolveCaseInsensitive(gameDir, datName);
            if (datPath is not null)
                archives.Add(Dat2Archive.Open(datPath));
        }

        if (archives.Count == 0)
            throw new FileNotFoundException($"No DAT2 archives (master.dat etc.) found in '{gameDir}'.");

        string looseRoot = ResolveCaseInsensitive(gameDir, "data") ?? Path.Combine(gameDir, "data");
        return new GameFileSystem(gameDir, looseRoot, archives);
    }

    public bool Exists(string virtualPath) =>
        ResolveLoosePath(virtualPath) is not null || _archives.Any(a => a.Contains(virtualPath));

    public Stream OpenRead(string virtualPath)
    {
        string? loosePath = ResolveLoosePath(virtualPath);
        if (loosePath is not null)
            return File.OpenRead(loosePath);

        foreach (Dat2Archive archive in _archives)
        {
            Dat2Entry? entry = archive.FindEntry(virtualPath);
            if (entry is not null)
                return archive.OpenRead(entry);
        }

        throw new FileNotFoundException($"'{virtualPath}' not found in any mounted base of '{GameDir}'.", virtualPath);
    }

    public byte[] ReadAllBytes(string virtualPath)
    {
        using Stream stream = OpenRead(virtualPath);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Finds a loose file under data/ matching the virtual path case-insensitively
    /// (required on Linux where the on-disk case of extracted GOG files varies).
    /// </summary>
    private string? ResolveLoosePath(string virtualPath)
    {
        if (!Directory.Exists(_looseRoot))
            return null;

        string current = _looseRoot;
        string[] segments = virtualPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            string? next = ResolveCaseInsensitive(current, segments[i]);
            if (next is null)
                return null;
            bool isLast = i == segments.Length - 1;
            if (isLast != File.Exists(next))
                return null;
            current = next;
        }

        return segments.Length > 0 ? current : null;
    }

    private static string? ResolveCaseInsensitive(string directory, string name)
    {
        string exact = Path.Combine(directory, name);
        if (File.Exists(exact) || Directory.Exists(exact))
            return exact;

        return Directory.EnumerateFileSystemEntries(directory)
            .FirstOrDefault(e => string.Equals(Path.GetFileName(e), name, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        foreach (Dat2Archive archive in _archives)
            archive.Dispose();
    }
}
