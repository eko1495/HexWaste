using System.IO.Compression;

namespace Hexwaste.Formats.Save.Vanilla;

/// <summary>Slot files (*.SAV, AUTOMAP.SAV, PROTO copies) are gzip-compressed by
/// fileCopyCompressed (file_utils.cc:66-104); the engine's xfileOpen gunzips on the 1F 8B magic
/// (xfile.cc:140-155). Mirror that: decompress when the magic is present, else read plain.</summary>
public static class SlotFiles
{
    public static Stream OpenMaybeGzip(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
        {
            using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            var plain = new MemoryStream();
            gz.CopyTo(plain);
            plain.Position = 0;
            return plain;
        }
        return new MemoryStream(bytes);
    }
}
