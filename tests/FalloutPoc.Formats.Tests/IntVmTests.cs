using FalloutPoc.Formats.Int;

namespace FalloutPoc.Formats.Tests;

/// <summary>
/// Records every host call the VM makes; GetMessage always returns a marker
/// string so display_msg output can be traced back to message_str.
/// </summary>
internal sealed class FakeExternals : IVmExternals
{
    public List<string> DisplayedMessages { get; } = [];
    public int ScriptOverridesCalls { get; private set; }

    public void DisplayMessage(string text) => DisplayedMessages.Add(text);

    public string GetMessage(int messageListId, int id) => $"TEST-DESC({messageListId},{id})";

    public void SetScriptOverrides() => ScriptOverridesCalls++;

    public int SelfObjectId() => 1;

    public string ObjectName(int objectHandle) => $"obj{objectHandle}";

    public int GetGlobalVar(int index) => 0;

    public int GetLocalVar(int index) => 0;

    public int GetMapVar(int index) => 0;
}

public class IntVmTests
{
    private static IntProgram LoadScript(string virtualPath)
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        using Stream stream = vfs.OpenRead(virtualPath);
        return IntProgram.Load(stream);
    }

    [GameDataFact]
    public void ParsesMiDoorProcedureTable()
    {
        IntProgram program = LoadScript(@"scripts\midoor.int");

        Assert.True(program.Procedures.Count > 5, $"only {program.Procedures.Count} procedures");
        Assert.True(program.FindProcedure("look_at_p_proc") >= 0, "look_at_p_proc missing");
        Assert.True(program.FindProcedure("description_p_proc") >= 0, "description_p_proc missing");

        // Body offsets must land inside the file, past the 42-byte stub.
        foreach (IntProcedure procedure in program.Procedures)
            Assert.InRange(procedure.BodyOffset, 42, program.Data.Length - 2);
    }

    [GameDataFact]
    public void RunsMiDoorDescription()
    {
        IntProgram program = LoadScript(@"scripts\midoor.int");
        var externals = new FakeExternals();
        var vm = new IntVm(program, externals);

        Assert.True(vm.TryRunProcedure("description_p_proc"));

        // The door script overrides the default examine text: it must call
        // script_overrides and display a message_str-derived string.
        Assert.True(externals.ScriptOverridesCalls > 0, "script_overrides was not called");
        Assert.Contains(externals.DisplayedMessages, m => m.Contains("TEST-DESC"));

        // The call convention must leave both stacks balanced across runs.
        Assert.Equal(0, vm.ReturnStackDepth);
        int depth = vm.DataStackDepth;
        Assert.True(vm.TryRunProcedure("description_p_proc"));
        Assert.Equal(depth, vm.DataStackDepth);
        Assert.Equal(0, vm.ReturnStackDepth);
    }

    [GameDataFact]
    public void RunsMiDoorLookAt()
    {
        IntProgram program = LoadScript(@"scripts\midoor.int");
        var externals = new FakeExternals();
        var vm = new IntVm(program, externals);

        Assert.True(vm.TryRunProcedure("look_at_p_proc"));

        Assert.True(externals.ScriptOverridesCalls > 0, "script_overrides was not called");
        Assert.Contains(externals.DisplayedMessages, m => m.Contains("TEST-DESC"));
        Assert.Equal(0, vm.ReturnStackDepth);
    }

    [GameDataFact]
    public void RunsSiShelf1Description()
    {
        // A second, unrelated script (SAD container) proves generality.
        IntProgram program = LoadScript(@"scripts\sishelf1.int");
        var externals = new FakeExternals();
        var vm = new IntVm(program, externals);

        Assert.True(vm.TryRunProcedure("description_p_proc"));

        Assert.True(externals.ScriptOverridesCalls > 0, "script_overrides was not called");
        Assert.Contains(externals.DisplayedMessages, m => m.Contains("TEST-DESC"));
        Assert.Equal(0, vm.ReturnStackDepth);
    }

    [GameDataFact]
    public void StubsUnknownExternalsWithoutThrowing()
    {
        // map_enter_p_proc leans on externals outside IVmExternals
        // (metarule, obj_lock, set_local_var, cur_map_index, ...); all of
        // them must be arity-stubbed, reported, and leave the stack intact.
        IntProgram program = LoadScript(@"scripts\midoor.int");
        var externals = new FakeExternals();
        int stubbed = 0;
        var vm = new IntVm(program, externals, _ => stubbed++);

        Assert.True(vm.TryRunProcedure("map_enter_p_proc"));
        Assert.True(stubbed > 0, "expected at least one arity-stubbed external call");
        Assert.Equal(0, vm.ReturnStackDepth);

        Assert.False(vm.TryRunProcedure("no_such_procedure"));
    }
}
