using System.Buffers.Binary;
using Hexwaste.Formats.Save.Vanilla;

namespace Hexwaste.Formats.Tests;

public class SaveDatReaderTests
{
    private sealed class Be
    {
        public readonly MemoryStream S = new();
        public Be I32(int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); S.Write(b); return this; }
        public Be I16(short v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteInt16BigEndian(b, v); S.Write(b); return this; }
        public Be U8(byte v) { S.WriteByte(v); return this; }
        public Be Str(string s, int len) { var b = new byte[len]; System.Text.Encoding.ASCII.GetBytes(s).CopyTo(b, 0); S.Write(b); return this; }
        public Be Zeros(int n) { S.Write(new byte[n]); return this; }
        public Be Many(int count, int v) { for (int i = 0; i < count; i++) I32(v); return this; }
    }

    private static readonly VanillaSaveContext Ctx = new(
        GlobalVarCount: 3, PartyDescriptionCount: 1, PartyDescriptionPids: [-1],
        HasCustomAiPacket: _ => false, Protos: null);

    private static Be Header(Be w) => w
        .Str("FALLOUT SAVE FILE", 24).I16(1).I16(2).U8((byte)'R')
        .Str("Narg", 32).Str("oracle test", 30)
        .I16(10).I16(10).I16(2026).I32(0)            // real date + meaningless fileTime
        .I16(7).I16(25).I16(2241).I32(302400)        // game date + game time
        .I16(0).I16(2).Str("ARTEMPLE.SAV", 16)
        .Zeros(29792 + 128);

    internal static byte[] MinimalSave()
    {
        var w = Header(new Be());
        w.I32(18000);                                // 2  dude cid
        w.I32(1).I32(0).I32(5);                      // 3  gvars
        w.I32(1).Str("ARTEMPLE.SAV", 13).I32(1234);  // 4  map list (NUL-terminated) + automap size
        w.I32(1).I32(0).I32(5);                      // 5  gvars copy
        // 6  dude object: 18 ints, then objectDataRead critter part (inv len, cap, ptr, field0, 7 combat, hp, rad, poison), then center tile
        w.I32(18000).I32(20100).I32(0).I32(0).I32(0).I32(0).I32(0).I32(3).I32(0x01000001).I32(0)
         .I32(0).I32(0x01000000).I32(-1).I32(4).I32(65536).I32(0).I32(-1).I32(-1);
        w.I32(0).I32(0).I32(0).I32(0).Many(7, 0).I32(30).I32(0).I32(0);
        w.I32(20100);
        w.I32(0).I32(0).Many(35, 5).Many(35, 0).Many(18, 10).I32(0).I32(0).I32(0).I32(0); // 7 critter: sneak, flags, 35+35+18, 4 (376 B)
        w.Many(19, 0);                               // 8  kills
        w.I32(4).I32(0).I32(5).I32(-1);              // 9  tagged skills
        //                                             10 random: 0 bytes
        w.Many(1 * 119, 0);                          // 11 perks (1 description)
        w.I32(0);                                    // 12 combat state: inactive
        //                                             13 ai: 0 custom packets
        w.I32(0).I32(1).I32(0).I32(0).I32(0);        // 14 pc stats
        //                                             15 items: 0 bytes
        w.I32(-1).I32(-1);                           // 16 traits
        w.I32(0);                                    // 17 automap flags
        w.Many(13, 1).I32(0).Many(4, 22).I32(0).I32(0); // 18 preferences (floats as raw bits)
        w.I32(1).U8(0);                              // 19 character editor
        w.I32(0).I32(0).I32(10).I32(20).I32(0).I32(-1).I32(-1).I32(-1).I32(0).I32(-1).I32(0); // 20 worldmap fixed
        w.I32(0);                                    //    0 cities
        w.I32(0).I32(0);                             //    0 tiles, 0 horizontal
        w.I32(0);                                    //    0 counters
        //                                             21 pipboy: 0 bytes
        w.Zeros(17);                                 // 22 movies
        w.Many(18 * 3, 0);                           // 23 skill usage
        w.I32(1).I32(0);                             // 24 party: length 1 (dude only), item count; 0 ids; 0 descriptions
        w.I32(0);                                    // 25 queue: 0 events
        w.I32(1).I32(0).I32(1).I32(0);               // 26 interface
        return w.S.ToArray();
    }

    [Fact]
    public void ReadsMinimalSaveEndToEnd()
    {
        VanillaSave save = SaveDatReader.Read(new MemoryStream(MinimalSave()), Ctx);
        Assert.Equal("Narg", save.Header.CharacterName);
        Assert.Equal(302400u, save.Header.GameTime);
        Assert.Equal("ARTEMPLE.SAV", save.Header.MapFileName);
        Assert.Equal(18000, save.DudeCid);
        Assert.Equal([1, 0, 5], save.GlobalVars);
        Assert.Equal(["ARTEMPLE.SAV"], save.MapFiles);
        Assert.Equal(20100, save.Dude.HexTile);
        Assert.Equal(30, save.Dude.CurrentHp);
        Assert.Equal(20100, save.DudeCenterTile);
        Assert.Equal(5, save.Critter.BaseStats[0]);
        Assert.Equal([4, 0, 5, -1], save.TaggedSkills);
        Assert.False(save.Combat.Active);
        Assert.Equal(1, save.PcStats.Level);
        Assert.Equal([-1, -1], save.Traits);
        Assert.Equal(10, save.Worldmap.PosX);
        Assert.Empty(save.PartyMemberIds);
        Assert.Empty(save.Queue);
        Assert.Equal(save.TotalLength, MinimalSave().Length);  // consumed exactly
    }

    [Fact]
    public void RejectsBadSignature()
    {
        byte[] bytes = MinimalSave();
        bytes[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => SaveDatReader.Read(new MemoryStream(bytes), Ctx));
    }

    [Fact]
    public void RejectsDivergentGvarCopies()
    {
        byte[] bytes = MinimalSave();
        // the second gvar copy's last int sits right before the dude object; corrupt it
        int dudeObjectStart = 30051 + 4 + 12 + (4 + 13 + 4) + 12;
        bytes[dudeObjectStart - 1] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => SaveDatReader.Read(new MemoryStream(bytes), Ctx));
    }

    [GameDataFact]
    public void ReadsEveryOracleFixtureSaveCompletely()
    {
        string root = Path.Combine(GameData.RequiredDir, "oracle-saves");
        if (!Directory.Exists(root))
            return; // fixtures not generated on this machine (scripts/oracle-make-save.sh)
        var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new Proto.ProtoDatabase(vfs);
        VanillaSaveContext ctx = VanillaSaveContext.FromGameData(vfs, protos);
        foreach (string dir in Directory.GetDirectories(root))
        {
            using Stream s = SlotFiles.OpenMaybeGzip(Path.Combine(dir, "SAVE.DAT"));
            VanillaSave save = SaveDatReader.Read(s, ctx);
            Assert.Equal(s.Length, save.TotalLength);
            Assert.Contains(save.Header.MapFileName, save.MapFiles, StringComparer.OrdinalIgnoreCase);
        }
    }
}
