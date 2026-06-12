using Hexwaste.Formats;
using Hexwaste.Formats.Frm;
using Hexwaste.Formats.Pal;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// FrmDump — decodes an FRM from the game's VFS to PNG(s), proving that FRM
// pixels and PAL colors are parsed correctly.
//
// usage: FrmDump --game-dir <dir> <virtual\path.frm> [out-prefix]

string? gameDir = null;
var rest = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--game-dir" && i + 1 < args.Length)
        gameDir = args[++i];
    else
        rest.Add(args[i]);
}

if (gameDir is null || rest.Count == 0)
{
    Console.Error.WriteLine("usage: FrmDump --game-dir <dir> <virtual\\path.frm> [out-prefix]");
    return 1;
}

string frmPath = rest[0];
string outPrefix = rest.Count > 1 ? rest[1] : Path.GetFileNameWithoutExtension(frmPath.Replace('\\', '/'));

using GameFileSystem vfs = GameFileSystem.Open(gameDir);
Palette palette = Palette.Load(vfs.ReadAllBytes("color.pal"));
FrmFile frm = FrmFile.Load(vfs.ReadAllBytes(frmPath));

Console.WriteLine($"{frmPath}: version {frm.Version}, {frm.FrameCount} frame(s), {frm.FramesPerSecond} fps, action frame {frm.ActionFrame}");

var uniqueDirections = new List<int>();
for (int rotation = 0; rotation < FrmFile.RotationCount; rotation++)
    if (rotation == 0 || !ReferenceEquals(frm.Directions[rotation], frm.Directions[rotation - 1]))
        uniqueDirections.Add(rotation);

foreach (int rotation in uniqueDirections)
{
    for (int frameIndex = 0; frameIndex < frm.FrameCount; frameIndex++)
    {
        FrmFrame frame = frm.GetFrame(frameIndex, rotation);
        using var image = new Image<Rgba32>(frame.Width, frame.Height);
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                byte index = frame.Pixels[y * frame.Width + x];
                (byte r, byte g, byte b) = palette.GetRgb(index);
                image[x, y] = new Rgba32(r, g, b, index == Palette.TransparentIndex ? (byte)0 : (byte)255);
            }
        }

        string suffix = uniqueDirections.Count > 1 || frm.FrameCount > 1
            ? $"_d{rotation}_f{frameIndex}"
            : "";
        string outPath = $"{outPrefix}{suffix}.png";
        image.SaveAsPng(outPath);
        Console.WriteLine($"  {frame.Width}x{frame.Height} offset ({frame.OffsetX},{frame.OffsetY}) -> {outPath}");
    }
}

return 0;
