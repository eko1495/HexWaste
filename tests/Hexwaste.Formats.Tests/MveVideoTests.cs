using Hexwaste.Formats.Movie;

namespace Hexwaste.Formats.Tests;

/// <summary>P133 (gap batch D, session 2): the MVE video codec (_nfPkDecomp block opcodes
/// 0-15 + palette). Validated pixel-exact against ffmpeg's interplay_video decoder across
/// all 13 game movies (~9,500 frames); here we lock the pipeline + determinism against a
/// real art\cuts\*.mve without embedding any game pixels.</summary>
public class MveVideoTests
{
    [GameDataFact]
    public void DecodesRealMovieFramesDeterministically()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        byte[] bytes = vfs.ReadAllBytes(@"art\cuts\intro.mve");
        MveFile mve = MveFile.Parse(bytes);

        (int frames, byte[] firstFrame, byte[] lastFrame, int width, int height) Run()
        {
            var video = new MveVideo();
            int frames = 0;
            byte[] first = [], last = [];
            foreach (MveOpcode op in mve.Opcodes)
            {
                video.Step(op);
                if (video.FramePresented)
                {
                    byte[] f = video.CurrentIndexed;
                    if (frames == 0) first = f;
                    last = f;
                    frames++;
                }
            }
            return (frames, first, last, video.Width, video.Height);
        }

        var a = Run();

        // Fallout movies are 640x320, 8-bit paletted.
        Assert.Equal(640, a.width);
        Assert.Equal(320, a.height);
        Assert.True(a.frames > 100, $"expected a long cutscene, got {a.frames} frames");
        Assert.Equal(a.width * a.height, a.firstFrame.Length);

        // Real content: the first frame is not a uniform fill.
        Assert.True(a.firstFrame.Distinct().Count() > 8, "first frame looks uniform");
        // Animation: the last frame differs from the first (the intro is not static).
        Assert.NotEqual(a.firstFrame, a.lastFrame);

        // Determinism: a second independent decode is byte-identical (no cross-frame state
        // leak, no RNG, no uninitialised buffers).
        var b = Run();
        Assert.Equal(a.frames, b.frames);
        Assert.Equal(a.firstFrame, b.firstFrame);
        Assert.Equal(a.lastFrame, b.lastFrame);
    }

    [GameDataFact]
    public void PaletteAndBlitProduceOpaqueRgba()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        MveFile mve = MveFile.Parse(vfs.ReadAllBytes(@"art\cuts\iplogo.mve"));
        var video = new MveVideo();
        foreach (MveOpcode op in mve.Opcodes)
        {
            video.Step(op);
            if (video.FramePresented)
                break;
        }
        Assert.True(video.Width > 0);
        byte[] rgba = video.BlitRgba();
        Assert.Equal(video.Width * video.Height * 4, rgba.Length);
        // Every pixel is fully opaque (alpha = 255) and the frame carries real colour.
        bool anyColour = false;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            Assert.Equal(255, rgba[i + 3]);
            if (rgba[i] != 0 || rgba[i + 1] != 0 || rgba[i + 2] != 0) anyColour = true;
        }
        Assert.True(anyColour, "blitted frame is entirely black");
    }
}
