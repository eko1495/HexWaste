using Hexwaste.Formats;
using Hexwaste.Formats.Movie;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;

namespace Hexwaste.Viewer;

/// <summary>
/// P133: a full-screen Interplay MVE cutscene player. Drives <see cref="MveVideo"/> over the
/// demuxed opcode stream at the movie's own frame rate (the 0x02 CreateTimer quanta), uploads
/// each decoded 8-bit frame to a persistent RGBA <see cref="Texture2D"/> (via the palette the
/// stream maintains inline), plays the decoded DPCM soundtrack once through the
/// <see cref="AudioManager"/>, and blits the frame centred/letterboxed over a black backdrop.
///
/// The player is a self-contained modal — the caller ticks <see cref="Update"/> with the frame
/// delta, draws with <see cref="Draw"/>, and watches <see cref="Finished"/> (also settable via
/// <see cref="Skip"/> on a key/click). It never touches the game simulation.
/// </summary>
public sealed class MviePlayer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly MveFile _mve;
    private readonly MveVideo _video = new();
    private readonly IReadOnlyList<MveOpcode> _ops;
    private int _opIndex;

    // Frame period in microseconds. Defaults to ~15 fps until the stream's CreateTimer sets it.
    private double _frameMicros = 66_667;
    private double _accumMicros;

    private Texture2D? _texture;
    private SoundEffectInstance? _audio;

    public bool Finished { get; private set; }
    public int Width => _video.Width;
    public int Height => _video.Height;
    public int FramesPresented { get; private set; }

    public MviePlayer(GraphicsDevice gd, byte[] mveBytes, AudioManager? audio)
    {
        _gd = gd;
        _mve = MveFile.Parse(mveBytes);
        _ops = _mve.Opcodes;

        // The soundtrack is decoded up front and submitted once (the whole movie's audio); the
        // video clock then paces the frames independently. Missing/undecodable audio is fine.
        MveAudio.Track? track = MveAudio.Decode(_mve);
        if (track is not null && audio is not null)
            _audio = audio.PlayMovieAudio(track.Pcm16, track.Format.SampleRate, track.Format.Stereo);

        // Present the first frame immediately so there's something on screen at t=0.
        if (!AdvanceToNextFrame())
            Finished = true;
    }

    /// <summary>Load an MVE by cutscene name (art\cuts\&lt;name&gt;.mve). Returns null if absent
    /// or unparseable, so the caller can fall back to a caption card.</summary>
    public static MviePlayer? TryOpen(GraphicsDevice gd, GameFileSystem vfs, string name, AudioManager? audio)
    {
        string path = $@"art\cuts\{name}.mve";
        if (!vfs.Exists(path))
            return null;
        try
        {
            byte[] bytes = vfs.ReadAllBytes(path);
            if (!MveFile.HasMagic(bytes))
                return null;
            return new MviePlayer(gd, bytes, audio);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            Console.Error.WriteLine($"movie {name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Advance the clock by <paramref name="elapsedMs"/> and step whole frames as the
    /// movie's period elapses. Catch-up is capped so a long stall can't spin the whole movie
    /// in one tick.</summary>
    public void Update(double elapsedMs)
    {
        if (Finished)
            return;
        _accumMicros += elapsedMs * 1000.0;
        int guard = 0;
        while (_accumMicros >= _frameMicros && guard++ < 4)
        {
            _accumMicros -= _frameMicros;
            if (!AdvanceToNextFrame())
            {
                Finished = true;
                _audio?.Stop();
                break;
            }
        }
    }

    /// <summary>Step opcodes until the next frame is presented (updating the timer + palette as
    /// they appear). Returns false at end of stream.</summary>
    private bool AdvanceToNextFrame()
    {
        while (_opIndex < _ops.Count)
        {
            MveOpcode op = _ops[_opIndex++];
            if (op.Type == (byte)MveOp.CreateTimer && op.Data.Length >= 6)
            {
                ReadOnlySpan<byte> d = op.Data.Span;
                // ported from fallout2-ce src/movie_lib.cc syncInit(): quanta = rate*resolution µs.
                uint rate = (uint)(d[0] | (d[1] << 8) | (d[2] << 16)) | ((uint)d[3] << 24);
                int resolution = d[4] | (d[5] << 8);
                if (rate > 0 && resolution > 0)
                    _frameMicros = rate * (double)resolution;
            }
            _video.Step(op);
            if (_video.FramePresented)
            {
                UploadFrame();
                FramesPresented++;
                return true;
            }
        }
        return false;
    }

    private void UploadFrame()
    {
        if (_video.Width == 0)
            return;
        byte[] rgba = _video.BlitRgba(); // R,G,B,A — matches SurfaceFormat.Color byte order
        if (_texture is null || _texture.Width != _video.Width || _texture.Height != _video.Height)
        {
            _texture?.Dispose();
            _texture = new Texture2D(_gd, _video.Width, _video.Height, false, SurfaceFormat.Color);
        }
        _texture.SetData(rgba);
    }

    /// <summary>Draw the current frame centred over a full-viewport black backdrop (native
    /// 640x320 size, letterboxed — no aspect distortion).</summary>
    public void Draw(SpriteBatch sb, Texture2D pixel, Viewport vp)
    {
        sb.Draw(pixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
        if (_texture is null)
            return;
        int ox = (vp.Width - _texture.Width) / 2;
        int oy = (vp.Height - _texture.Height) / 2;
        sb.Draw(_texture, new Rectangle(ox, oy, _texture.Width, _texture.Height), Color.White);
    }

    public void Skip()
    {
        Finished = true;
        _audio?.Stop();
    }

    public void Dispose()
    {
        _audio?.Stop();
        _audio?.Dispose();
        _texture?.Dispose();
    }
}
