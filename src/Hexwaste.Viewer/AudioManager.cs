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
    private readonly string _speechDir;
    private readonly Dictionary<string, SoundEffect?> _sfxCache = new(StringComparer.OrdinalIgnoreCase);
    private SoundEffectInstance? _music;
    private SoundEffectInstance? _speech;
    private string? _musicTrack;
    private bool _enabled = true;

    // P130: the master volume, on fo2ce's 0..32767 scale (VOLUME_MAX). Scales every sfx +
    // the music instance; the Preferences slider drives it. Music re-applies live.
    private const int VolumeMax = 32767;
    private int _masterVolume = VolumeMax;
    public int MasterVolume => _masterVolume;
    private float MasterFactor => Math.Clamp(_masterVolume / (float)VolumeMax, 0f, 1f);

    public void SetMasterVolume(int fo2ceScale)
    {
        _masterVolume = Math.Clamp(fo2ceScale, 0, VolumeMax);
        if (_music is not null)
            _music.Volume = MasterFactor;
    }

    public AudioManager(GameFileSystem vfs, string gameDir)
    {
        _vfs = vfs;
        _musicDir = Path.Combine(gameDir, "sound", "music");
        _speechDir = Path.Combine(gameDir, "sound", "speech");
    }

    /// <summary>Play a one-shot sfx. <paramref name="gain"/> scales the base volume 0..1 —
    /// the positional attenuation factor (SfxVolume.RelativeGain, P121); default 1 = the
    /// pre-P121 full volume for unanchored/UI sounds.</summary>
    public void PlaySfx(string name, float gain = 1f)
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

            effect?.Play(0.5f * Math.Clamp(gain, 0f, 1f) * MasterFactor, 0f, 0f);
        }
        catch (Exception ex) when (ex is NoAudioHardwareException or InvalidOperationException)
        {
            Console.Error.WriteLine($"audio disabled: {ex.Message}");
            _enabled = false;
        }
    }

    /// <summary>P53: play a one-shot dialogue speech file. Speech ACMs are LOOSE under
    /// &lt;gameDir&gt;\sound\speech\ (like music — the VO is not in the DATs); the slice ships none, so this
    /// is inert until voiced content is installed. <paramref name="name"/> is the MSG audio basename.</summary>
    public void PlaySpeech(string name)
    {
        if (!_enabled)
            return;
        try
        {
            string file = Path.Combine(_speechDir, name + ".acm");
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"speech not found: {name}");
                return;
            }
            SoundEffect? effect = CreateEffect(File.ReadAllBytes(file));
            if (effect is null)
                return;
            _speech?.Stop();
            _speech?.Dispose();
            _speech = effect.CreateInstance();
            _speech.Play();
        }
        catch (Exception ex) when (ex is NoAudioHardwareException or InvalidOperationException)
        {
            Console.Error.WriteLine($"audio disabled: {ex.Message}");
            _enabled = false;
        }
    }

    /// <summary>P100 (Point 1): play endgame narrator speech supplied as raw ACM bytes (the narrator ACMs
    /// live INSIDE the DAT under sound\speech\narrator\, not loose), and return its duration in ms (or 0 if
    /// audio is off / the decode failed → the caller falls back to the 0.08 s/char subtitle timing, exactly
    /// as endgame.cc does when speechLoad fails).</summary>
    public double PlaySpeechData(byte[] acmData)
    {
        if (!_enabled)
            return 0;
        try
        {
            AcmAudio audio;
            try { audio = AcmDecoder.Decode(acmData); }
            catch (InvalidDataException) { return 0; }
            SoundEffect? effect = CreateEffect(acmData);
            if (effect is null)
                return 0;
            _speech?.Stop();
            _speech?.Dispose();
            _speech = effect.CreateInstance();
            _speech.Play();
            int channels = audio.Channels >= 2 ? 2 : 1;
            return (double)(audio.Samples.Length / channels) / audio.SampleRate * 1000.0;
        }
        catch (Exception ex) when (ex is NoAudioHardwareException or InvalidOperationException)
        {
            Console.Error.WriteLine($"audio disabled: {ex.Message}");
            _enabled = false;
            return 0;
        }
    }

    /// <summary>Stop any playing speech (e.g. when a slide is skipped early).</summary>
    public void StopSpeech()
    {
        _speech?.Stop();
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
            _music.Volume = 0.35f * MasterFactor; // P130: music honors the master slider
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
        _speech?.Stop();
        _speech?.Dispose();
        foreach (SoundEffect? effect in _sfxCache.Values)
            effect?.Dispose();
    }
}
