using Hexwaste.Formats.Movie;

namespace Hexwaste.Formats.Tests;

/// <summary>P132 (gap batch D, session 1): the MVE container demuxer + Interplay DPCM audio.
/// The video codec (block opcodes) is session 2 — here we lock the container parse + the
/// audio path against a real art\cuts\*.mve.</summary>
public class MveAudioTests
{
    [Fact]
    public void DpcmStereoFrameDecodesInitPlusDeltas()
    {
        // A hand-built stereo frame: init L=1000, R=-1000, then two delta pairs of byte 0
        // (delta 0 → unchanged) and byte 1 (delta +1). 2+2 pairs → 6 stereo samples * 2ch.
        byte[] frame =
        [
            0xE8, 0x03, // L init 1000
            0x18, 0xFC, // R init -1000 (0xFC18)
            0x00, 0x00, // pair 1: +0, +0
            0x01, 0x01, // pair 2: +1, +1
        ];
        byte[] pcm = InterplayDpcm.DecodeFrame(frame, stereo: true);
        short S(int i) => (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));

        Assert.Equal((2 + 2 * 2) * 2, pcm.Length); // (init pair + 2 pairs) * 2ch * 2 bytes
        Assert.Equal(1000, S(0));   // L init
        Assert.Equal(-1000, S(1));  // R init
        Assert.Equal(1000, S(2));   // L +0
        Assert.Equal(-1000, S(3));  // R +0
        Assert.Equal(1001, S(4));   // L +1
        Assert.Equal(-999, S(5));   // R +1
    }

    [GameDataFact]
    public void ParsesRealMveContainerAndDecodesItsAudio()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        byte[] bytes = vfs.ReadAllBytes(@"art\cuts\afailed.mve");
        Assert.True(MveFile.HasMagic(bytes));

        MveFile mve = MveFile.Parse(bytes);
        Assert.NotEmpty(mve.Opcodes);
        // The stream must end each chunk (0x01) and carry an audio init (0x03).
        Assert.Contains(mve.Opcodes, o => o.Type == (byte)MveOp.InitAudio);
        Assert.Contains(mve.Opcodes, o => o.Type == (byte)MveOp.EndOfChunk);

        MveAudio.Track? track = MveAudio.Decode(mve);
        Assert.NotNull(track);
        // afailed.mve: stereo 16-bit DPCM at 22050 Hz (the Fallout movie audio profile).
        Assert.Equal(22050, track.Format.SampleRate);
        Assert.True(track.Format.Stereo);
        Assert.True(track.Format.Bits16);
        Assert.True(track.Format.Compressed);

        // A non-trivial PCM16 buffer, aligned to stereo 16-bit frames, within int16 range.
        Assert.True(track.Pcm16.Length > 10000, $"only {track.Pcm16.Length} PCM bytes");
        Assert.Equal(0, track.Pcm16.Length % 4); // 2 channels * 2 bytes
        int peak = 0;
        for (int i = 0; i + 1 < track.Pcm16.Length; i += 2)
            peak = Math.Max(peak, Math.Abs((short)(track.Pcm16[i] | (track.Pcm16[i + 1] << 8))));
        Assert.InRange(peak, 1, 32767); // real signal, not silence, not garbage-saturated
    }

    [GameDataFact]
    public void DecodesTheEldersTalkingHeadAudio()
    {
        // A second, larger movie (the Elder briefing) — confirms the demux scales past the
        // tiny afailed clip to a multi-megabyte talking-head with many audio frames.
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        byte[] bytes = vfs.ReadAllBytes(@"art\cuts\ELDER.mve");
        MveAudio.Track? track = MveAudio.Decode(MveFile.Parse(bytes));
        Assert.NotNull(track);
        Assert.Equal(22050, track.Format.SampleRate);
        Assert.True(track.Pcm16.Length > 1_000_000); // seconds of talking-head audio
    }
}
