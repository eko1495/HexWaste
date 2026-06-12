using Hexwaste.Formats;
using Hexwaste.Formats.Sound;
using Microsoft.Xna.Framework.Audio;

namespace Hexwaste.Viewer;

/// <summary>
/// Decodes ACM audio to PCM16 and plays it through MonoGame: one-shot sfx via
/// cached SoundEffect instances, music as a looping instance. Music tracks are
/// loose files under &lt;gameDir&gt;\sound\music\ (the fallout2.cfg music_path
/// default — GOG ships them outside the DATs); sfx come from the VFS
/// (sound\sfx\*.acm in master.dat). Audio failures (no device, missing file)
/// disable themselves quietly — sound is an enhancement, never a crash.
/// </summary>
public sealed class AudioManager : IDisposable
{
    private readonly GameFileSystem _vfs;
    private readonly string _musicDir;
    private readonly Dictionary<string, SoundEffect?> _sfxCache = new(StringComparer.OrdinalIgnoreCase);
    private SoundEffectInstance? _music;
    private string? _musicTrack;
    private bool _enabled = true;

    public AudioManager(GameFileSystem vfs, string gameDir)
    {
        _vfs = vfs;
        _musicDir = Path.Combine(gameDir, "sound", "music");
    }

    public void PlaySfx(string name)
    {
        if (!_enabled)
            return;

        try
        {
            if (!_sfxCache.TryGetValue(name, out SoundEffect? effect))
            {
                string path = SfxName.Path(name);
                effect = _vfs.Exists(path) ? CreateEffect(_vfs.ReadAllBytes(path)) : null;
                if (effect is null)
                    Console.Error.WriteLine($"sfx not found/decodable: {name}");
                _sfxCache[name] = effect;
            }

            effect?.Play(0.5f, 0f, 0f);
        }
        catch (Exception ex) when (ex is NoAudioHardwareException or InvalidOperationException)
        {
            Console.Error.WriteLine($"audio disabled: {ex.Message}");
            _enabled = false;
        }
    }

    /// <summary>Switches the looping music track (maps.txt music= name, e.g. "07desert").</summary>
    public void PlayMusic(string? track)
    {
        if (!_enabled || string.Equals(track, _musicTrack, StringComparison.OrdinalIgnoreCase))
            return;

        _music?.Stop();
        _music?.Dispose();
        _music = null;
        _musicTrack = track;
        if (track is null)
            return;

        try
        {
            string? file = Directory.Exists(_musicDir)
                ? Directory.EnumerateFiles(_musicDir)
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                        .Equals(track, StringComparison.OrdinalIgnoreCase))
                : null;
            if (file is null)
            {
                Console.Error.WriteLine($"music track not found: {track}");
                return;
            }

            SoundEffect? effect = CreateEffect(File.ReadAllBytes(file));
            if (effect is null)
                return;
            _music = effect.CreateInstance();
            _music.IsLooped = true;
            _music.Volume = 0.35f;
            _music.Play();
        }
        catch (Exception ex) when (ex is NoAudioHardwareException or InvalidOperationException)
        {
            Console.Error.WriteLine($"audio disabled: {ex.Message}");
            _enabled = false;
        }
    }

    private static SoundEffect? CreateEffect(byte[] acmData)
    {
        AcmAudio audio;
        try
        {
            audio = AcmDecoder.Decode(acmData);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        // Some stereo ACMs declare an odd total sample count (e.g. FOOTSTEP),
        // which violates SoundEffect's block alignment (channels * 2 bytes) —
        // truncate the dangling sample.
        int channels = audio.Channels >= 2 ? 2 : 1;
        int alignedSamples = audio.Samples.Length / channels * channels;
        byte[] pcm = new byte[alignedSamples * 2];
        Buffer.BlockCopy(audio.Samples, 0, pcm, 0, pcm.Length);

        try
        {
            return new SoundEffect(pcm, audio.SampleRate,
                channels == 2 ? AudioChannels.Stereo : AudioChannels.Mono);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"sfx rejected by audio backend: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _music?.Stop();
        _music?.Dispose();
        foreach (SoundEffect? effect in _sfxCache.Values)
            effect?.Dispose();
    }
}
