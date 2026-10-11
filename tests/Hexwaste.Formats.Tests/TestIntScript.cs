namespace Hexwaste.Formats.Tests;

/// <summary>Hand-assembles a minimal compiled .int for hermetic ScriptHost tests. Layout copied from
/// VmGlobalsPersistenceTests (the compiler's 42-byte stub, one module global, void-proc epilogue).</summary>
internal static class TestIntScript
{
    public static void Op(List<byte> code, ushort opcode)
    {
        code.Add((byte)(opcode >> 8));
        code.Add((byte)opcode);
    }

    public static void PushInt(List<byte> code, int value)
    {
        Op(code, 0xC001); // push, int type bits
        Int32(code, value);
    }

    private static void Int32(List<byte> bytes, int value)
    {
        bytes.Add((byte)(value >> 24));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static void Epilogue(List<byte> code)
    {
        PushInt(code, 0);   // return value
        Op(code, 0x800D);   // d_to_a
        Op(code, 0x8019);   // swapa
        Op(code, 0x802A);   // pop_to_base
        Op(code, 0x8029);   // pop_base
        Op(code, 0x800C);   // a_to_d
        Op(code, 0x801C);   // pop_return
    }

    public static byte[] Build(params (string Name, Action<List<byte>> Body)[] procs)
    {
        var idBytes = new List<byte>();
        var nameOffsets = new int[procs.Length];
        for (int i = 0; i < procs.Length; i++)
        {
            string name = procs[i].Name;
            idBytes.Add((byte)((name.Length + 1) >> 8));
            idBytes.Add((byte)(name.Length + 1));
            nameOffsets[i] = 4 + idBytes.Count;
            idBytes.AddRange(System.Text.Encoding.Latin1.GetBytes(name));
            idBytes.Add(0);
        }

        int codeStart = 42 + 4 + procs.Length * 24 + 4 + idBytes.Count + 4;

        var prologue = new List<byte>();
        Op(prologue, 0x802C);      // set_global
        PushInt(prologue, 0);      // global[0] := 0
        Op(prologue, 0x8003);      // leave_critical_section
        Op(prologue, 0x801C);      // pop_return -> 18

        var bodies = new List<List<byte>>();
        foreach ((string _, Action<List<byte>> body) in procs)
        {
            var code = new List<byte>();
            Op(code, 0x802B);      // push_base
            body(code);
            Epilogue(code);
            bodies.Add(code);
        }

        var file = new List<byte>();
        Op(file, 0x8002);          //  0: enter_critical_section
        PushInt(file, 18);         //  2: push 18
        Op(file, 0x800D);          //  8: d_to_a
        PushInt(file, codeStart);  // 10: push prologue address
        Op(file, 0x8004);          // 16: jump
        Op(file, 0x8010);          // 18: exit_program
        Op(file, 0x801A);          // 20: pop
        Op(file, 0x8020);          // 22: pop_flags_return
        Op(file, 0x801A);          // 24: pop
        Op(file, 0x8021);          // 26: pop_flags_exit
        Op(file, 0x801A);          // 28: pop
        Op(file, 0x8022);          // 30
        Op(file, 0x801A);          // 32: pop
        Op(file, 0x8023);          // 34
        Op(file, 0x8024);          // 36
        Op(file, 0x8025);          // 38
        Op(file, 0x8026);          // 40

        Int32(file, procs.Length);
        int at = codeStart + prologue.Count;
        for (int i = 0; i < procs.Length; i++)
        {
            Int32(file, nameOffsets[i]);
            Int32(file, 0);
            Int32(file, 0);
            Int32(file, 0);
            Int32(file, at);
            Int32(file, 0);
            at += bodies[i].Count;
        }
        Int32(file, idBytes.Count);
        file.AddRange(idBytes);
        Int32(file, unchecked((int)0xFFFFFFFF)); // static strings absent
        file.AddRange(prologue);
        foreach (List<byte> body in bodies)
            file.AddRange(body);
        return file.ToArray();
    }
}
