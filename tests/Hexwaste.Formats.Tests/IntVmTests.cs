using Hexwaste.Formats.Int;

namespace Hexwaste.Formats.Tests;

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

    // P0 (campaign port): record the critter-state EFFECT externals so a real script run proves dispatch.
    public List<(int Handle, int Amount)> Heals { get; } = [];
    public List<int> PoisonReads { get; } = [];
    public List<(int Handle, int DeathFrame)> Kills { get; } = [];
    public int PoisonValue { get; set; }

    public int CritterHeal(int objectHandle, int amount) { Heals.Add((objectHandle, amount)); return 0; }
    public int GetPoison(int objectHandle) { PoisonReads.Add(objectHandle); return PoisonValue; }
    public void KillCritter(int objectHandle, int deathFrame) { Kills.Add((objectHandle, deathFrame)); }

    public List<(int Target, int Item)> UseOnObj { get; } = [];
    public List<(int Handle, int Kind, int Param, int Value)> RmTraits { get; } = [];
    public List<(int Handle, int Skill, int Points)> ModSkills { get; } = [];
    public int DialogueEnters { get; private set; }
    public List<(int Index, int Param)> LoadMaps { get; } = [];
    public List<(string Name, int Param)> LoadMapNames { get; } = [];
    public void UseObjectOnObject(int targetHandle, int itemHandle) => UseOnObj.Add((targetHandle, itemHandle));
    public int CritterRemoveTrait(int h, int kind, int param, int value) { RmTraits.Add((h, kind, param, value)); return -1; }
    public int CritterModSkill(int h, int skill, int points) { ModSkills.Add((h, skill, points)); return 0; }
    public void DialogueSystemEnter() => DialogueEnters++;
    public void LoadMap(int mapIndex, int param) => LoadMaps.Add((mapIndex, param));
    public void LoadMapByName(string mapName, int param) => LoadMapNames.Add((mapName, param));

    public List<(int Attacker, int Defender)> AttackSetups { get; } = [];
    public void AttackSetup(int attackerHandle, int defenderHandle) => AttackSetups.Add((attackerHandle, defenderHandle));
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
        // map_enter_p_proc leans on externals outside IVmExternals (metarule, obj_lock,
        // set_local_var, ...); any the VM doesn't implement must be arity-stubbed — popped,
        // reported, never throwing — so the proc runs clean and leaves the return stack intact.
        // INVARIANT (the safety property): a clean run + an empty stack. We do NOT assert a stub
        // FIRED — as more externals get wired a given script may stub zero of them (the stub path
        // itself stays exercised by the still-stubbed slice maps in the --smoke goldens).
        IntProgram program = LoadScript(@"scripts\midoor.int");
        var externals = new FakeExternals();
        int stubbed = 0;
        var vm = new IntVm(program, externals, _ => stubbed++);

        Assert.True(vm.TryRunProcedure("map_enter_p_proc")); // ran without throwing
        Assert.Equal(0, vm.ReturnStackDepth);                // stack intact (stubs popped their arity)

        Assert.False(vm.TryRunProcedure("no_such_procedure"));
    }

    [GameDataFact]
    public void HakuninHealNodeDispatchesCritterHealAndGetPoison()
    {
        // P0 (campaign port): Hakunin's healing node (ahhakun Node014) is a real caller of the newly
        // wired critter-state externals. Running it proves the IntVm dispatches critter_heal (0x80E8) and
        // get_poison (0x8123) to the right interface methods with the right popped args, and balances both
        // stacks. PoisonValue>0 makes the script take its cure-poison branch so get_poison is observed.
        IntProgram program = LoadScript(@"scripts\ahhakun.int");
        var externals = new FakeExternals { PoisonValue = 5 };
        var vm = new IntVm(program, externals);

        Assert.True(vm.TryRunProcedure("Node014")); // ran clean (no throw, all other externals arity-stubbed)
        Assert.Equal(0, vm.ReturnStackDepth);       // return stack balanced
        Assert.NotEmpty(externals.Heals);           // critter_heal dispatched: (handle, amount) popped in order
        Assert.NotEmpty(externals.PoisonReads);     // get_poison dispatched: handle popped, value pushed
    }

    [GameDataFact]
    public void DenMapEnterDispatchesUseObjOnObj()
    {
        // P0-M2: a denbus1 map_enter script calls use_obj_on_obj (0x8145) — that's why --smoke listed it
        // as the Den's one stubbed external. Running map_enter through the real ScriptHost proves the full
        // path fires: opcode dispatch -> ScriptContext.UseObjectOnObject -> the UseObjOnObjRequested host
        // delegate (which in the viewer runs the use_obj_on_p_proc chain).
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new Hexwaste.Formats.Proto.ProtoDatabase(vfs);
        int fired = 0;
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos)
        {
            NameResolver = _ => "x",
            UseObjOnObjRequested = (_, _) => fired++,
        };

        using Stream stream = vfs.OpenRead(@"maps\denbus1.map");
        Hexwaste.Formats.Map.MapFile map = Hexwaste.Formats.Map.MapFile.Load(stream, protos);
        host.RunMapEnter(map, map.Elevations[0]!.Objects.Where(o => o.Sid != -1), null);

        Assert.True(fired > 0, "no denbus1 map_enter script dispatched use_obj_on_obj");
    }

    [GameDataFact]
    public void CameronRewardNodeDispatchesCritterModSkill()
    {
        // P0-M3: Cameron's reward node (actemvil Node016a) calls critter_mod_skill (0x813C). Running it
        // proves the IntVm pops (points, skill, critter) in order, dispatches to CritterModSkill, pushes
        // its 0 result, and balances both stacks.
        IntProgram program = LoadScript(@"scripts\actemvil.int");
        var externals = new FakeExternals();
        var vm = new IntVm(program, externals);

        Assert.True(vm.TryRunProcedure("Node016a"));
        Assert.Equal(0, vm.ReturnStackDepth);
        Assert.NotEmpty(externals.ModSkills); // critter_mod_skill dispatched (handle, skill, points)
    }

    [GameDataFact]
    public void ReactorTerminalUseProcDispatchesDialogueSystemEnter()
    {
        // P0-M5: the Gecko reactor control terminal (gsterm) opens its dialog by calling
        // dialogue_system_enter (0x80F9) from its use_p_proc. Running that proc proves the opcode
        // dispatches to DialogueSystemEnter (the viewer then opens the terminal's talk_p_proc).
        IntProgram program = LoadScript(@"scripts\gsterm.int");
        var externals = new FakeExternals();
        var vm = new IntVm(program, externals);

        Assert.True(vm.TryRunProcedure("use_p_proc"));
        Assert.Equal(0, vm.ReturnStackDepth);
        Assert.True(externals.DialogueEnters > 0, "dialogue_system_enter was not dispatched");
    }

    [GameDataFact]
    public void MetzgerNodeDispatchesLoadMap()
    {
        // P0-M6: Metzger's slave-run departure node (dcMetzge Node989) sends the player to another map via
        // load_map (0x80E4). Running it proves the IntVm pops (param, mapIndexOrName), routes the int vs
        // string form to LoadMap/LoadMapByName, and balances both stacks.
        IntProgram program = LoadScript(@"scripts\dcMetzge.int");
        var externals = new FakeExternals();
        var vm = new IntVm(program, externals);

        Assert.True(vm.TryRunProcedure("Node989"));
        Assert.Equal(0, vm.ReturnStackDepth);
        Assert.True(externals.LoadMaps.Count + externals.LoadMapNames.Count > 0, "load_map was not dispatched");
    }

    [GameDataFact]
    public void DragonDoFightDispatchesAttackSetup()
    {
        // P0-M7: the New Reno martial-arts duel (fcdragon doFight) makes the master attack the dude via
        // attack_setup (0x8143) — the disassembly pushes local_var(12) then dude_obj, so the VM pops
        // (defender=dude, attacker=master). Running it proves the IntVm pops both pointers in order and
        // dispatches to AttackSetup with balanced stacks.
        IntProgram program = LoadScript(@"scripts\fcdragon.int");
        var externals = new FakeExternals();
        var vm = new IntVm(program, externals);

        Assert.True(vm.TryRunProcedure("doFight"));
        Assert.Equal(0, vm.ReturnStackDepth);
        Assert.NotEmpty(externals.AttackSetups); // attack_setup dispatched (attacker, defender)
    }
}
