using Hexwaste.Formats;
using Hexwaste.Formats.Art;
using Hexwaste.Formats.Frm;
using Hexwaste.Formats.Pal;
using Microsoft.Xna.Framework.Graphics;

namespace Hexwaste.Viewer;

/// <summary>
/// Lazily loads FRMs by FID and converts frames to RGBA textures, with an LRU
/// cap so an entire DAT is never resident at once. Palette index data stays in
/// the FrmFile, so frames containing animated palette indices (229..255) can be
/// re-uploaded cheaply when the palette cycles — only those textures are touched,
/// never the whole scene (the mistake that killed jsFO's performance).
/// </summary>
public sealed class FrmCache(GameFileSystem vfs, ArtIndex artIndex, GraphicsDevice graphicsDevice, Palette palette)
    : IDisposable
{
    private const int Capacity = 4096;
    private const int FirstCyclingIndex = 229;

    private sealed class Entry
    {
        public required FrmFile Frm { get; init; }
        public required Texture2D?[][] Textures { get; init; }
        public required bool HasCyclingColors { get; init; }
        public LinkedListNode<int>? LruNode { get; set; }
    }

    private readonly Dictionary<int, Entry> _entries = [];
    private readonly LinkedList<int> _lru = [];
    private byte[] _paletteRgba = palette.ToRgba();

    public FrmFile GetFrm(int fid) => GetEntry(fid).Frm;

    /// <summary>Number of loaded FRMs containing animated palette indices.</summary>
    public int CyclingEntryCount => _entries.Values.Count(e => e.HasCyclingColors);

    private readonly Dictionary<(int Fid, int Frame, int Rotation), Texture2D> _outlines = [];

    /// <summary>
    /// 1px silhouette texture for hover/selection outlines (the original's
    /// objectDrawOutline traces index!=0 edge pixels): white edge pixels on
    /// transparent, tinted by the caller.
    /// </summary>
    public Texture2D GetOutlineTexture(int fid, int frame = 0, int rotation = 0)
    {
        var key = (fid, frame, rotation);
        if (_outlines.TryGetValue(key, out Texture2D? cached))
            return cached;

        FrmFrame frmFrame = GetFrm(fid).GetFrame(frame, rotation);
        int width = frmFrame.Width;
        int height = frmFrame.Height;
        byte[] pixels = frmFrame.Pixels;
        byte[] rgba = new byte[width * height * 4];

        bool Solid(int x, int y) =>
            x >= 0 && x < width && y >= 0 && y < height && pixels[y * width + x] != 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!Solid(x, y))
                    continue;
                bool edge = !Solid(x - 1, y) || !Solid(x + 1, y) || !Solid(x, y - 1) || !Solid(x, y + 1);
                if (!edge)
                    continue;
                int p = (y * width + x) * 4;
                rgba[p] = rgba[p + 1] = rgba[p + 2] = rgba[p + 3] = 255;
            }
        }

        var texture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
        texture.SetData(rgba);
        _outlines[key] = texture;
        return texture;
    }

    public Texture2D GetTexture(int fid, int frame = 0, int rotation = 0)
    {
        Entry entry = GetEntry(fid);
        FrmFrame frmFrame = entry.Frm.GetFrame(frame, rotation);
        return entry.Textures[rotation][frame] ??= CreateTexture(frmFrame);
    }

    /// <summary>Called by the palette cycler: re-uploads only textures that contain cycling indices.</summary>
    public void OnPaletteChanged(Palette updated)
    {
        _paletteRgba = updated.ToRgba();
        foreach (Entry entry in _entries.Values)
        {
            if (!entry.HasCyclingColors)
                continue;

            for (int rotation = 0; rotation < FrmFile.RotationCount; rotation++)
            {
                Texture2D?[] textures = entry.Textures[rotation];
                for (int frame = 0; frame < textures.Length; frame++)
                {
                    if (textures[frame] is { } texture)
                        UploadPixels(texture, entry.Frm.GetFrame(frame, rotation));
                }
            }
        }
    }

    private Entry GetEntry(int fid)
    {
        if (_entries.TryGetValue(fid, out Entry? cached))
        {
            _lru.Remove(cached.LruNode!);
            cached.LruNode = _lru.AddFirst(fid);
            return cached;
        }

        FrmFile frm = FrmFile.Load(vfs.ReadAllBytes(artIndex.GetFrmPath(fid)));

        bool hasCycling = false;
        for (int rotation = 0; rotation < FrmFile.RotationCount && !hasCycling; rotation++)
        {
            if (rotation > 0 && ReferenceEquals(frm.Directions[rotation], frm.Directions[rotation - 1]))
                continue;
            foreach (FrmFrame frame in frm.Directions[rotation])
                if (Array.FindIndex(frame.Pixels, p => p >= FirstCyclingIndex) >= 0)
                {
                    hasCycling = true;
                    break;
                }
        }

        var entry = new Entry
        {
            Frm = frm,
            Textures = [.. Enumerable.Range(0, FrmFile.RotationCount)
                .Select(r => new Texture2D?[frm.FrameCount])],
            HasCyclingColors = hasCycling,
        };

        if (_entries.Count >= Capacity)
            EvictOldest();

        _entries[fid] = entry;
        entry.LruNode = _lru.AddFirst(fid);
        return entry;
    }

    private void EvictOldest()
    {
        if (_lru.Last is not { } oldest)
            return;
        if (_entries.Remove(oldest.Value, out Entry? entry))
            DisposeTextures(entry);
        _lru.RemoveLast();
    }

    private Texture2D CreateTexture(FrmFrame frame)
    {
        var texture = new Texture2D(graphicsDevice, frame.Width, frame.Height, false, SurfaceFormat.Color);
        UploadPixels(texture, frame);
        return texture;
    }

    private void UploadPixels(Texture2D texture, FrmFrame frame)
    {
        byte[] rgba = new byte[frame.Pixels.Length * 4];
        for (int i = 0; i < frame.Pixels.Length; i++)
            Buffer.BlockCopy(_paletteRgba, frame.Pixels[i] * 4, rgba, i * 4, 4);
        texture.SetData(rgba);
    }

    private static void DisposeTextures(Entry entry)
    {
        for (int rotation = 0; rotation < FrmFile.RotationCount; rotation++)
            foreach (Texture2D? texture in entry.Textures[rotation])
                texture?.Dispose();
    }

    public void Dispose()
    {
        foreach (Entry entry in _entries.Values)
            DisposeTextures(entry);
        foreach (Texture2D outline in _outlines.Values)
            outline.Dispose();
        _outlines.Clear();
        _entries.Clear();
        _lru.Clear();
    }
}
