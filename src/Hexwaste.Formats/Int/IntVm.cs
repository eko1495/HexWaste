namespace Hexwaste.Formats.Int;

/// <summary>
/// Host services a script may call. Only the externals needed for the
/// examine/door paths are real; every other external opcode is arity-stubbed
/// by the VM (arguments popped, 0 pushed when the builtin returns a value).
/// </summary>
public interface IVmExternals
{
    /// <summary>display_msg (fallout2-ce interpreter_extra.cc opDisplayMsg).</summary>
    void DisplayMessage(string text);

    /// <summary>message_str (opGetMessageString): text for a line of a message list.</summary>
    string GetMessage(int messageListId, int id);

    /// <summary>script_overrides (opScriptOverrides).</summary>
    void SetScriptOverrides();

    /// <summary>self_obj (opGetSelf) as an opaque int handle.</summary>
    int SelfObjectId();

    /// <summary>obj_name (opGetObjectName).</summary>
    string ObjectName(int objectHandle);

    /// <summary>get_global_var (opGetGlobalVar); returning 0 is fine.</summary>
    int GetGlobalVar(int index);

    /// <summary>get_local_var (opGetLocalVar); returning 0 is fine.</summary>
    int GetLocalVar(int index);

    /// <summary>get_map_var (opGetMapVar).</summary>
    int GetMapVar(int index);

    // ---- script-context protocol (phase-4 M0). Defaults preserve the old
    // stub behavior so simple hosts (tests) keep working unchanged.

    /// <summary>set_local_var (opSetLocalVar) — writes mapLocalVars[script.localVarsOffset + index].</summary>
    void SetLocalVar(int index, int value) { }

    /// <summary>set_global_var (opSetGlobalVar).</summary>
    void SetGlobalVar(int index, int value) { }

    /// <summary>set_map_var (opSetMapVar).</summary>
    void SetMapVar(int index, int value) { }

    /// <summary>source_obj (opGetSource) — the object that triggered the proc (usually the dude).</summary>
    int SourceObjectId() => 0;

    /// <summary>target_obj (opGetTarget) — defaults to self per scriptExecProc (scripts.cc:1316).</summary>
    int TargetObjectId() => SelfObjectId();

    /// <summary>dude_obj (opGetDude).</summary>
    int DudeObjectId() => 0;

    /// <summary>obj_being_used_with (opGetObjectBeingUsedWith).</summary>
    int ObjectBeingUsedWithId() => 0;

    /// <summary>get_critter_stat (opGetCritterStat → stat.cc critterGetStat):
    /// effective stat (base+bonus; 35=current HP, 36=poison, 37=rad); -1 unknown.</summary>
    int GetCritterStat(int objectHandle, int stat) => 0;

    /// <summary>set_critter_stat (opSetCritterStat): despite the name it
    /// ADJUSTS the dude's base stat by amount; 0 ok, -1 non-dude.</summary>
    int AdjustCritterBaseStat(int objectHandle, int stat, int amount) => -1;

    /// <summary>has_trait (opHasTrait): type 0=perk rank, 1=object trait
    /// (5=aiPacket, 6=team, 10=rotation, 666=visible, 669=inv weight),
    /// 2=selected character trait.</summary>
    int HasTrait(int type, int objectHandle, int param) => 0;

    /// <summary>do_check (opDoCheck → stat.cc statRoll): d10 vs SPECIAL+mod →
    /// ROLL_SUCCESS(2)/ROLL_FAILURE(1).</summary>
    int DoCheck(int objectHandle, int stat, int modifier) => 2;

    /// <summary>get_pc_stat (opGetPcStat): 0=unspent skill pts, 1=level,
    /// 2=experience, 3=reputation, 4=karma.</summary>
    int GetPcStat(int stat) => 0;

    /// <summary>critter_add_trait (opCritterAddTrait): kind 1 sets object
    /// traits (5=aiPacket, 6=team); perks (kind 0) are out of PoC scope.</summary>
    void CritterAddTrait(int objectHandle, int kind, int param, int value) { }

    /// <summary>attack/attack_complex (opAttackComplex): self attacks the
    /// target — starts combat outside it, retargets inside it.</summary>
    void AttackComplex(int targetHandle) { }

    /// <summary>anim_busy (opAnimBusy): is the object mid-animation?</summary>
    bool AnimBusy(int objectHandle) => false;

    /// <summary>give_exp_points (opGiveExpPoints → pcAddExperience).</summary>
    void GiveExpPoints(int amount) { }

    /// <summary>override_map_start (opOverrideMapStart, interpreter_extra.cc:522):
    /// tile = 200·y + x; repositions the dude during map_enter.</summary>
    void OverrideMapStart(int x, int y, int elevation, int rotation) { }

    /// <summary>fixed_param (opGetFixedParam) — map_enter: first-run flag; timed: timer param.</summary>
    int FixedParam() => 0;

    /// <summary>action_being_used (opGetActionBeingUsed) — skill id during use_skill_on (lockpick = 9).</summary>
    int ActionBeingUsed() => -1;

    /// <summary>script_action (opGetScriptAction) — the proc id being executed.</summary>
    int ScriptAction() => 0;

    /// <summary>
    /// metarule (opMetarule). The host should answer rule 14 FIRST_RUN
    /// (pristine map → 1), 22 IS_LOADGAME (0) and 30 CAR_CURRENT_TOWN (0);
    /// the default matches those for pristine-map sessions.
    /// </summary>
    int Metarule(int rule, int argument) => rule == 14 ? 1 : 0;

    /// <summary>Game clock in ticks (10/second; engine boots at 302400). Drives game_time* and month.</summary>
    int GameTime() => 302400;

    // ---- dialog protocol (phase-4 M1). The VM resolves message ids to text
    // before calling; option procs arrive as procedure indices.

    /// <summary>gsay_start (_gdialogStart): clear the reply and option list.</summary>
    void DialogStart() { }

    /// <summary>gsay_reply / the reply part of gsay_message.</summary>
    void DialogReply(string text) { }

    /// <summary>gsay_option / giq_option (after the IQ filter): an option bound to a procedure index.</summary>
    void DialogOption(string text, int procedureIndex, int reaction) { }

    /// <summary>gsay_end (_gdialogGo): the collected reply+options are ready to present.</summary>
    void DialogEnd() { }

    /// <summary>start_gdialog — headId is -1 for head-less NPCs.</summary>
    void DialogSessionStart(int headId, int backgroundId) { }

    /// <summary>end_dialogue.</summary>
    void DialogSessionEnd() { }

    /// <summary>Player IQ (+ Smooth Talker) for the giq_option filter.</summary>
    int DialogIntelligence() => 5;

    /// <summary>using_skill(object, skill): true only for the dude's SNEAK flag (P29 A-M0).</summary>
    bool IsUsingSkill(int objectHandle, int skill) => false;

    /// <summary>critter_attempt_placement (interpreter_extra.cc:2812): relocate a critter to a tile
    /// (or a free tile near it) on an elevation. Returns true on success.</summary>
    bool CritterAttemptPlacement(int critterHandle, int tile, int elevation) => false;

    /// <summary>float_msg — floating head-text over an object.</summary>
    void FloatMessage(int objectHandle, string text, int type) { }

    /// <summary>gdialog_barter (gameDialogBarter, game_dialog.cc:3163): sets
    /// the trade flag; its argument OVERWRITES gdialog_set_barter_mod (:3169).</summary>
    void Barter(int modifier) { }

    /// <summary>gdialog_set_barter_mod.</summary>
    void GdialogSetBarterMod(int modifier) { }

    /// <summary>move_obj_inven_to_obj (opMoveObjectInventoryToObject): move the
    /// whole inventory from source to target.</summary>
    void MoveAllInventory(int sourceHandle, int targetHandle) { }

    /// <summary>play_gmovie (opPlayGameMovie): the host shows a caption card.</summary>
    void PlayMovie(int movieId) { }

    /// <summary>critter_damage (opCritterDamage → actionDamage): flag 0x100 =
    /// bypass armor, 0x200 = no animation; low bits = damage type.</summary>
    void CritterDamage(int objectHandle, int amount, int damageTypeWithFlags) { }

    /// <summary>party_add / party_remove (opPartyAdd/opPartyRemove).</summary>
    void PartyAdd(int objectHandle) { }

    void PartyRemove(int objectHandle) { }

    /// <summary>party_member_obj (opGetPartyMember): handle of the party
    /// member with this pid, or 0.</summary>
    int PartyMemberByPid(int pid) => 0;

    // ---- door/container state (phase-4 M2); handle 0 must no-op like the
    // engine's scriptPredefinedError paths.

    /// <summary>obj_is_locked (objectIsLocked).</summary>
    bool ObjIsLocked(int objectHandle) => false;

    /// <summary>obj_lock / obj_unlock / jam_lock (jam treated as lock).</summary>
    void ObjSetLocked(int objectHandle, bool locked) { }

    /// <summary>obj_is_open (door frame != 0).</summary>
    bool ObjIsOpen(int objectHandle) => false;

    /// <summary>obj_open / obj_close — the host animates and re-blocks.</summary>
    void ObjSetOpen(int objectHandle, bool open) { }

    // ---- world mutation (phase-4 M3); handle 0 must no-op.

    /// <summary>create_object_sid; scriptIndex (scripts.lst, -1 none) binds a
    /// fresh script to the object. Returns the new handle or 0.</summary>
    int CreateObject(int pid, int tile, int elevation, int scriptIndex = -1) => 0;

    /// <summary>destroy_object.</summary>
    void DestroyObject(int objectHandle) { }

    /// <summary>add_obj_to_inven / add_mult_objs_to_inven.</summary>
    void AddToInventory(int targetHandle, int itemHandle, int quantity) { }

    /// <summary>rm_obj_from_inven / rm_mult_objs_from_inven; returns the removed count.</summary>
    int RemoveFromInventory(int targetHandle, int itemHandle, int quantity) => 0;

    /// <summary>move_to; returns the object's new tile (engine pushes a result).</summary>
    int MoveTo(int objectHandle, int tile, int elevation) => -1;

    /// <summary>set_obj_visibility (true = hidden).</summary>
    void SetObjectVisibility(int objectHandle, bool hidden) { }

    /// <summary>obj_pid.</summary>
    int ObjPid(int objectHandle) => -1;

    /// <summary>obj_is_carrying_obj_pid (interpreter_extra.cc:1040): the quantity of
    /// <paramref name="pid"/> the critter carries (recursive into nested containers).</summary>
    int ObjIsCarryingPid(int objectHandle, int pid) => 0;

    /// <summary>obj_carrying_pid_obj (interpreter_extra.cc:3438): the handle of the
    /// FIRST carried item with <paramref name="pid"/> (depth-first), or 0 if none.</summary>
    int ObjCarryingPidObj(int objectHandle, int pid) => 0;

    /// <summary>tile_contains_pid_obj — handle of a matching object, or 0.</summary>
    int TileContainsPidObj(int tile, int elevation, int pid) => 0;

    // ---- timers + geometry + caps (phase-5 M0)

    /// <summary>add_timer_event — delay is in game ticks (10/second, 1:1 real time).</summary>
    void AddTimerEvent(int objectHandle, int delayTicks, int param) { }

    /// <summary>rm_timer_event — removes all timers owned by the object.</summary>
    void RemoveTimerEvents(int objectHandle) { }

    /// <summary>metarule3 rule 100 — removes timers matching (object, param).</summary>
    void RemoveTimerEventsWithParam(int objectHandle, int param) { }

    /// <summary>tile_num — the object's hex tile, or -1.</summary>
    int ObjTile(int objectHandle) => -1;

    /// <summary>cur_map_index.</summary>
    int CurrentMapIndex() => 0;

    /// <summary>item_caps_total — caps (pid 41) in the object's inventory.</summary>
    int CapsTotal(int objectHandle) => 0;

    /// <summary>item_caps_adjust — mutates the caps stack; 0 on success, -1 when insufficient.</summary>
    int CapsAdjust(int objectHandle, int amount) => -1;

    /// <summary>obj_can_see_obj — PoC: distance-based sight (no line-of-sight walls).</summary>
    bool ObjCanSee(int objectHandle, int targetHandle) => false;

    /// <summary>animate_move_obj_to_tile — the host may start a walk animation.</summary>
    void AnimateMoveToTile(int objectHandle, int tile, int speed) { }

    /// <summary>set_light_level — set the global ambient light (0-100%, 50 = cavern).</summary>
    void SetLightLevel(int level) { }

    /// <summary>obj_set_light_level — set a per-object light pool (intensity 0-100%, radius).</summary>
    void SetObjectLightLevel(int objectHandle, int intensity, int distance) { }

    /// <summary>reg_anim_animate_forever — loop animation code <paramref name="anim"/> on the
    /// object forever (the host's animator).</summary>
    void RegAnimAnimateForever(int objectHandle, int anim) { }

    /// <summary>reg_anim_func BEGIN (interpreter_extra.cc:3462 reg_anim_begin): open an
    /// animation batch; subsequent reg_anim register ops accumulate into it.</summary>
    void RegAnimBegin() { }

    /// <summary>reg_anim_func END (interpreter_extra.cc:3469 reg_anim_end): flush the batch
    /// to the host (it plays the queued moves/animations).</summary>
    void RegAnimEnd() { }

    /// <summary>reg_anim_func CLEAR (interpreter_extra.cc:3466 reg_anim_clear): cancel the
    /// object's running/queued animations.</summary>
    void RegAnimClear(int objectHandle) { }

    /// <summary>reg_anim_obj_move_to_tile / reg_anim_obj_run_to_tile (interpreter_extra.cc:
    /// 3547/3564): queue a walk (<paramref name="run"/> = run variant) to a hex tile.</summary>
    void RegAnimMoveToTile(int objectHandle, int tile, int delay, bool run) { }

    /// <summary>reg_anim_obj_move_to_obj / reg_anim_obj_run_to_obj (interpreter_extra.cc:
    /// 3513/3530): queue a walk to another object's tile.</summary>
    void RegAnimMoveToObject(int objectHandle, int destHandle, int delay, bool run) { }

    /// <summary>reg_anim_animate / reg_anim_animate_reverse (interpreter_extra.cc:3477/3496):
    /// queue an animation by code (<paramref name="reverse"/> plays it backwards).</summary>
    void RegAnimAnimate(int objectHandle, int anim, int delay, bool reverse) { }
}

/// <summary>
/// Micro interpreter for compiled Fallout 2 scripts, ported from fallout2-ce
/// src/interpreter.cc. It is a two-stack machine: a data stack holding tagged
/// values (program globals live at its bottom, below <c>basePointer</c>;
/// procedure locals are addressed off <c>framePointer</c>) and a return stack
/// for saved instruction pointers and frame pointers. Execution starts at
/// file offset 0: the 42-byte header stub jumps to the global-init prologue
/// and ends in exit_program (runScript/_interpret). Procedures are invoked
/// the way _executeProcedure() does: _setupCall() pushes the current IP and
/// the magic return address 24 on the return stack, then flags, a
/// checkWaitFunc placeholder, the window id and a 0 return-value slot on the
/// data stack; the compiled epilogue jumps to 24 where the header stub pops
/// the return value and runs pop_flags_exit, unwinding everything and
/// breaking out of the interpreter loop.
///
/// Scope per the phase-3 report (M5): the 39 core opcodes measured across six
/// real scripts plus the handful the call convention itself needs. Floats
/// never occur and are not supported. Externals not in <see cref="IVmExternals"/>
/// are arity-stubbed via <see cref="ExternalArity"/> so the stack never
/// desyncs; stubbed calls are reported through an optional callback.
/// </summary>
/// <summary>
/// Cross-script external variables (the engine's export.cc): one store per
/// host session, shared by every VM it spawns. Values are ints (numbers and
/// object handles); the rare string store is logged and dropped.
/// </summary>
public sealed class ExternalVariables
{
    internal Dictionary<string, int> Ints { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => Ints.Clear();
    public int Count => Ints.Count;
}

public sealed class IntVm
{
    // Value tags, ported from interpreter.h VALUE_TYPE_*.
    private const ushort TypeInt = 0xC001;
    private const ushort TypeFloat = 0xA001;
    private const ushort TypeStaticString = 0x9001;
    private const ushort TypeDynamicString = 0x9801;

    // Program flags, ported from interpreter.h ProgramFlags (0x40 is the
    // "returned from an interrupt-context procedure" break flag set by
    // pop_flags_exit).
    private const int FlagExited = 0x01;
    private const int FlagStopped = 0x08;
    private const int FlagCriticalSection = 0x80;
    private const int FlagProcReturned = 0x40;

    // _interpret() break mask: EXITED | 0x04 | STOPPED | 0x20 | 0x40 | 0x0100.
    private const int BreakMask = 0x016D;

    /// <summary>Hard safety budget; real procs run a few thousand ops at most.</summary>
    private const int InstructionBudget = 100_000;

    private readonly struct Value(ushort tag, int raw)
    {
        public ushort Tag { get; } = tag;
        public int Raw { get; } = raw;

        public bool IsString => Tag is TypeStaticString or TypeDynamicString;

        public static Value Int(int value) => new(TypeInt, value);
    }

    private readonly IntProgram _program;
    private readonly IVmExternals _externals;
    private readonly Action<string>? _onStubbedExternal;

    // ported from fallout2-ce src/random.h ROLL_* enum
    private const int RollCriticalFailure = 0;
    private const int RollSuccess = 2;
    private const int RollCriticalSuccess = 3;

    /// <summary>Deterministic RNG (scripts only use it for stock quantities and flavor).</summary>
    private readonly Random _random = new(20260612);

    /// <summary>Dialog text: literal string, or a message-list lookup for int ids.</summary>
    private string ResolveDialogText(int messageListId, Value msg) =>
        msg.Tag == TypeInt ? _externals.GetMessage(messageListId, msg.Raw) : AsString(msg);

    /// <summary>Calendar month (1..12) for a day count since June 24, 2241 (non-leap years).</summary>
    private static int MonthFromEpochDay(int day)
    {
        ReadOnlySpan<int> daysPerMonth = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        int month = 5; // June (0-based)
        int dayOfMonth = 23 + day; // June 24 is day 0
        while (dayOfMonth >= daysPerMonth[month])
        {
            dayOfMonth -= daysPerMonth[month];
            month = (month + 1) % 12;
        }
        return month + 1;
    }

    // Data stack (stackValues) and return stack (returnStackValues). Plain
    // lists because store/fetch/fetch_global index into the data stack.
    private readonly List<Value> _stack = [];
    private readonly List<Value> _returnStack = [];

    // Dynamic string heap: programPushString()'s block allocator reduced to a
    // list; a 0x9801-tagged Raw is an index into it.
    private readonly List<string> _dynamicStrings = [];

    // export_variable / fetch_external / store_external backing store. The
    // engine shares these across programs (export.cc); a per-VM dictionary is
    // enough for single-script runs, with absent imports defaulting to 0.
    private readonly Dictionary<string, Value> _externalVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly ExternalVariables? _sharedExternalVariables;

    private int _instructionPointer;
    private int _framePointer = -1;
    private int _basePointer = -1;
    private int _flags;
    private int _windowId;
    private bool _initialized;

    public IntVm(IntProgram program, IVmExternals externals, Action<string>? onStubbedExternal = null,
        ExternalVariables? externalVariables = null)
    {
        _program = program;
        _externals = externals;
        _onStubbedExternal = onStubbedExternal;
        _sharedExternalVariables = externalVariables;
    }

    /// <summary>
    /// Data stack depth — after a balanced procedure run this is back to the
    /// global count established by the init prologue (diagnostic).
    /// </summary>
    public int DataStackDepth => _stack.Count;

    /// <summary>Return stack depth — 0 between procedure runs (diagnostic).</summary>
    public int ReturnStackDepth => _returnStack.Count;

    /// <summary>Runs a procedure by name (e.g. "description_p_proc"); false when absent.</summary>
    public bool TryRunProcedure(string name)
    {
        int index = _program.FindProcedure(name);
        return index >= 0 && TryRunProcedureByIndex(index);
    }

    /// <summary>
    /// Runs a procedure by its table index — how the dialog system binds
    /// options (game_dialog.cc _gdProcessChoice → _executeProcedure(proc)).
    /// </summary>
    public bool TryRunProcedureByIndex(int index)
    {
        if (index < 0 || index >= _program.Procedures.Count)
            return false;

        IntProcedure procedure = _program.Procedures[index];
        if (procedure.IsImported || procedure.BodyOffset <= 0)
            return false;

        EnsureInitialized();

        // ported from _executeProcedure(): _setupCall(program, address, 24)
        // followed by _interpret(program, -1).
        SetupCall(procedure.BodyOffset, returnAddress: 24);
        if (procedure.IsCritical)
            _flags |= FlagCriticalSection;
        Interpret();
        _flags &= ~FlagProcReturned;
        return true;
    }

    /// <summary>
    /// Runs the global-init prologue, ported from runScript(): a fresh
    /// program is interpreted from offset 0; the header stub jumps to the
    /// prologue, which pushes the program globals, runs set_global and
    /// returns to the stub's exit_program. The globals stay on the data
    /// stack below basePointer for all later procedure runs.
    /// </summary>
    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _instructionPointer = 0;
        _flags = 0;
        Interpret();
        _initialized = true;
    }

    /// <summary>ported from fallout2-ce src/interpreter.cc _setupCall()/_setupCallWithReturnVal().</summary>
    private void SetupCall(int address, int returnAddress)
    {
        _returnStack.Add(Value.Int(_instructionPointer));
        _returnStack.Add(Value.Int(returnAddress));
        _stack.Add(Value.Int(_flags & 0xFFFF));
        _stack.Add(Value.Int(0)); // checkWaitFunc placeholder
        _stack.Add(Value.Int(_windowId));
        _flags &= ~0xFFFF;
        _instructionPointer = address;
        _stack.Add(Value.Int(0)); // return value slot
    }

    /// <summary>The _interpret() dispatch loop, with a hard instruction budget.</summary>
    private void Interpret()
    {
        int budget = InstructionBudget;
        while ((_flags & BreakMask) == 0)
        {
            if (--budget < 0)
                throw new InvalidDataException(
                    $"Script exceeded the {InstructionBudget} instruction budget (runaway loop?).");

            ushort opcode = (ushort)_program.ReadCode16(_instructionPointer);
            _instructionPointer += 2;

            if (((opcode >> 8) & 0x80) == 0)
                throw new InvalidDataException($"Bad opcode word 0x{opcode:X4} at 0x{_instructionPointer - 2:X}.");

            Execute(opcode);
        }
    }

    private void Execute(ushort opcode)
    {
        // Dispatch on the low 10 bits like _interpret(); the full word is
        // only meaningful for push, whose high bits carry the value type.
        switch (0x8000 | (opcode & 0x3FF))
        {
            case 0x8000: // noop
                break;
            case 0x8001: // push (the only opcode with an inline operand)
                ExecutePush(opcode);
                break;
            case 0x8002: // enter_critical_section
            case 0x804A: // start_critical
                _flags |= FlagCriticalSection;
                break;
            case 0x8003: // leave_critical_section
            case 0x804B: // end_critical
                _flags &= ~FlagCriticalSection;
                break;
            case 0x8004: // jump
                _instructionPointer = PopInt();
                break;
            case 0x8005: // call
                ExecuteCall();
                break;
            case 0x800C: // a_to_d
                Push(ReturnPop());
                break;
            case 0x800D: // d_to_a
                ReturnPush(Pop());
                break;
            case 0x8010: // exit_program
                _flags |= FlagExited;
                break;
            case 0x8011: // stop_program
                _flags |= FlagStopped;
                break;
            case 0x8012: // fetch_global
                Push(StackAt(_basePointer + PopInt()));
                break;
            case 0x8013: // store_global
            {
                int address = PopInt();
                StackSet(_basePointer + address, Pop());
                break;
            }
            case 0x8014: // fetch_external
                ExecuteFetchExternal();
                break;
            case 0x8015: // store_external
                ExecuteStoreExternal();
                break;
            case 0x8016: // export_variable
            {
                string identifier = _program.GetIdentifier(Pop().Raw);
                if (_sharedExternalVariables is { } shared)
                    shared.Ints.TryAdd(identifier, 0);
                else
                    _externalVariables.TryAdd(identifier, Value.Int(0));
                break;
            }
            case 0x8018: // swap
            {
                Value a = Pop();
                Value b = Pop();
                Push(a);
                Push(b);
                break;
            }
            case 0x8019: // swapa
            {
                Value a = ReturnPop();
                Value b = ReturnPop();
                ReturnPush(a);
                ReturnPush(b);
                break;
            }
            case 0x801A: // pop
                Pop();
                break;
            case 0x801B: // dup
            {
                Value value = Pop();
                Push(value);
                Push(value);
                break;
            }
            case 0x801C: // pop_return
                _instructionPointer = ReturnPopInt();
                break;
            case 0x801D: // pop_exit
                _instructionPointer = ReturnPopInt();
                _flags |= FlagProcReturned;
                break;
            case 0x801E: // pop_address
                ReturnPop();
                break;
            case 0x801F: // pop_flags
                ExecutePopFlags();
                break;
            case 0x8020: // pop_flags_return
                ExecutePopFlags();
                _instructionPointer = ReturnPopInt();
                break;
            case 0x8021: // pop_flags_exit
                ExecutePopFlags();
                _instructionPointer = ReturnPopInt();
                _flags |= FlagProcReturned;
                break;
            case 0x8025: // pop_flags_return_val_exit
            {
                Value value = Pop();
                ExecutePopFlags();
                _instructionPointer = ReturnPopInt();
                _flags |= FlagProcReturned;
                Push(value);
                break;
            }
            case 0x8027: // check_procedure_argument_count
            {
                int expected = PopInt();
                int procedureIndex = PopInt();
                if (ProcedureAt(procedureIndex).ArgumentCount != expected)
                    throw new InvalidDataException(
                        $"Wrong number of args to procedure {ProcedureAt(procedureIndex).Name}.");
                break;
            }
            case 0x8028: // lookup_procedure_by_name
            {
                int found = _program.FindProcedure(PopString());
                if (found < 1)
                    throw new InvalidDataException("lookup_procedure_by_name: procedure not found.");
                PushInt(found);
                break;
            }
            case 0x8029: // pop_base
                _framePointer = ReturnPopInt();
                break;
            case 0x802A: // pop_to_base
                if (_stack.Count < _framePointer)
                    throw new InvalidDataException("pop_to_base below the frame pointer (stack desync).");
                _stack.RemoveRange(_framePointer, _stack.Count - _framePointer);
                break;
            case 0x802B: // push_base
            {
                int argumentCount = PopInt();
                ReturnPush(Value.Int(_framePointer));
                _framePointer = _stack.Count - argumentCount;
                break;
            }
            case 0x802C: // set_global
                _basePointer = _stack.Count;
                break;
            case 0x802D: // fetch_procedure_address
                PushInt(ProcedureAt(PopInt()).BodyOffset);
                break;
            case 0x802E: // dump
            {
                int count = PopInt();
                for (int i = 0; i < count; i++)
                    Pop();
                break;
            }
            case 0x802F: // if
            {
                Value value = Pop();
                if (!IsEmpty(value))
                    Pop();
                else
                    _instructionPointer = PopInt();
                break;
            }
            case 0x8030: // while
                if (IsEmpty(Pop()))
                    _instructionPointer = PopInt();
                break;
            case 0x8031: // store
            {
                int address = PopInt();
                StackSet(_framePointer + address, Pop());
                break;
            }
            case 0x8032: // fetch
                Push(StackAt(_framePointer + PopInt()));
                break;
            case 0x8033: // equal
                ExecuteComparison(static c => c == 0);
                break;
            case 0x8034: // not_equal
                ExecuteComparison(static c => c != 0);
                break;
            case 0x8035: // less_than_equal
                ExecuteComparison(static c => c <= 0);
                break;
            case 0x8036: // greater_than_equal
                ExecuteComparison(static c => c >= 0);
                break;
            case 0x8037: // less_than
                ExecuteComparison(static c => c < 0);
                break;
            case 0x8038: // greater_than
                ExecuteComparison(static c => c > 0);
                break;
            case 0x8039: // add
                ExecuteAdd();
                break;
            case 0x803A: // sub
                ExecuteIntArithmetic(static (a, b) => unchecked(a - b));
                break;
            case 0x803B: // mul
                ExecuteIntArithmetic(static (a, b) => unchecked(a * b));
                break;
            case 0x803C: // div
                ExecuteIntArithmetic(static (a, b) =>
                    b != 0 ? a / b : throw new InvalidDataException("Division (DIV) by zero."));
                break;
            case 0x803D: // mod
                ExecuteIntArithmetic(static (a, b) =>
                    b != 0 ? a % b : throw new InvalidDataException("Division (MOD) by zero."));
                break;
            case 0x803E: // and
            {
                bool right = IsTruthy(Pop());
                bool left = IsTruthy(Pop());
                PushInt(left && right ? 1 : 0);
                break;
            }
            case 0x803F: // or
            {
                bool right = IsTruthy(Pop());
                bool left = IsTruthy(Pop());
                PushInt(left || right ? 1 : 0);
                break;
            }
            case 0x8040: // bitwise_and
                ExecuteIntArithmetic(static (a, b) => a & b);
                break;
            case 0x8041: // bitwise_or
                ExecuteIntArithmetic(static (a, b) => a | b);
                break;
            case 0x8042: // bitwise_xor
                ExecuteIntArithmetic(static (a, b) => a ^ b);
                break;
            case 0x8043: // bitwise_not
                PushInt(~PopInt());
                break;
            case 0x8044: // floor (no floats supported: ints pass through)
            {
                Value value = Pop();
                if (value.Tag != TypeInt)
                    throw new InvalidDataException("Invalid arg given to floor().");
                Push(value);
                break;
            }
            case 0x8045: // not (CE tests integerValue == 0 regardless of tag)
                PushInt(Pop().Raw == 0 ? 1 : 0);
                break;
            case 0x8046: // negate
            {
                Value value = Pop();
                if (value.Tag != TypeInt)
                    throw new InvalidDataException("Invalid arg given to NEG.");
                PushInt(-value.Raw);
                break;
            }
            case >= 0x8000 and <= 0x804B: // remaining core ops (timers, child programs, floats)
                throw new InvalidDataException(
                    $"Unsupported core opcode 0x{opcode:X4} at 0x{_instructionPointer - 2:X}.");
            default:
                ExecuteExternal(0x8000 | (opcode & 0x3FF));
                break;
        }
    }

    // ---------------------------------------------------------------- core ops

    /// <summary>ported from opPush(): the value type comes from the opcode word's high bits.</summary>
    private void ExecutePush(ushort opcode)
    {
        int operand = _program.ReadCode32(_instructionPointer);
        _instructionPointer += 4;

        if (opcode == TypeFloat)
            throw new NotSupportedException("Float push: floats never occur in game scripts and are not supported.");

        Push(new Value(opcode, operand));
    }

    /// <summary>ported from opCall(): jump to the procedure body; the compiled call site did the stack setup.</summary>
    private void ExecuteCall()
    {
        IntProcedure procedure = ProcedureAt(PopInt());
        if (procedure.IsImported)
            throw new InvalidDataException($"call of imported procedure {procedure.Name} is not supported.");

        _instructionPointer = procedure.BodyOffset;
        if (procedure.IsCritical)
            _flags |= FlagCriticalSection;
    }

    /// <summary>ported from opPopFlags(): windowId, checkWaitFunc, then flags off the data stack.</summary>
    private void ExecutePopFlags()
    {
        _windowId = PopInt();
        Pop(); // checkWaitFunc
        _flags = Pop().Raw & 0xFFFF;
    }

    private void ExecuteFetchExternal()
    {
        string identifier = _program.GetIdentifier(Pop().Raw);

        if (_sharedExternalVariables is { } shared)
        {
            if (!shared.Ints.TryGetValue(identifier, out int raw))
            {
                _onStubbedExternal?.Invoke($"fetch_external of undefined variable '{identifier}' -> 0");
                raw = 0;
            }
            Push(Value.Int(raw));
            return;
        }

        if (!_externalVariables.TryGetValue(identifier, out Value value))
        {
            // The engine fatals here; an import owned by another (unloaded)
            // script defaulting to 0 keeps single-program runs soft.
            _onStubbedExternal?.Invoke($"fetch_external of undefined variable '{identifier}' -> 0");
            value = Value.Int(0);
        }

        Push(value);
    }

    private void ExecuteStoreExternal()
    {
        string identifier = _program.GetIdentifier(Pop().Raw);
        Value value = Pop();

        if (_sharedExternalVariables is { } shared)
        {
            // Cross-program string offsets don't transfer; the shop scripts
            // only store ints and object handles.
            if (value.Tag == TypeInt)
                shared.Ints[identifier] = value.Raw;
            else
                _onStubbedExternal?.Invoke($"store_external of non-int '{identifier}' dropped");
            return;
        }

        _externalVariables[identifier] = value;
    }

    /// <summary>
    /// Comparison ops, ported from opConditionalOperator*(): mixed string/int
    /// operands compare as strings with the int formatted "%d"; value[1] is
    /// the left operand (popped second).
    /// </summary>
    private void ExecuteComparison(Func<int, bool> interpretation)
    {
        Value right = Pop();
        Value left = Pop();

        int comparison;
        if (left.IsString || right.IsString)
            comparison = string.CompareOrdinal(AsString(left), AsString(right));
        else
            comparison = left.Raw.CompareTo(right.Raw);

        PushInt(interpretation(comparison) ? 1 : 0);
    }

    /// <summary>ported from opAdd(): a string operand turns + into concatenation.</summary>
    private void ExecuteAdd()
    {
        Value right = Pop();
        Value left = Pop();

        if (left.IsString || right.IsString)
            PushString(AsString(left) + AsString(right));
        else
            PushInt(unchecked(left.Raw + right.Raw));
    }

    private void ExecuteIntArithmetic(Func<int, int, int> operation)
    {
        int right = PopInt();
        int left = PopInt();
        PushInt(operation(left, right));
    }

    // ------------------------------------------------------------- externals

    /// <summary>
    /// External (engine builtin) dispatch: the few examine/door builtins call
    /// into <see cref="IVmExternals"/>; everything else known to
    /// <see cref="ExternalArity"/> is arity-stubbed (pop Args, push 0 when it
    /// returns) so the stack stays balanced.
    /// </summary>
    private void ExecuteExternal(int opcode)
    {
        switch (opcode)
        {
            case 0x80A4: // obj_name
                PushString(_externals.ObjectName(PopInt()));
                break;
            case 0x80B8: // display_msg
                _externals.DisplayMessage(PopString());
                break;
            case 0x80B9: // script_overrides
                _externals.SetScriptOverrides();
                break;
            case 0x80BC: // self_obj
                PushInt(_externals.SelfObjectId());
                break;
            case 0x80C1: // get_local_var
                PushInt(_externals.GetLocalVar(PopInt()));
                break;
            case 0x80C3: // get_map_var
                PushInt(_externals.GetMapVar(PopInt()));
                break;
            case 0x80C5: // get_global_var
                PushInt(_externals.GetGlobalVar(PopInt()));
                break;
            case 0x8105: // message_str (opGetMessageString pops index, then list)
            {
                int messageIndex = PopInt();
                int messageListIndex = PopInt();
                PushString(_externals.GetMessage(messageListIndex, messageIndex));
                break;
            }

            // ---- variable setters (opSetLocalVar pops value, then index)
            case 0x80C2: // set_local_var
            {
                int value = PopInt();
                _externals.SetLocalVar(PopInt(), value);
                break;
            }
            case 0x80C4: // set_map_var
            {
                int value = PopInt();
                _externals.SetMapVar(PopInt(), value);
                break;
            }
            case 0x80C6: // set_global_var
            {
                int value = PopInt();
                _externals.SetGlobalVar(PopInt(), value);
                break;
            }

            // ---- script context
            case 0x80BD: // source_obj
                PushInt(_externals.SourceObjectId());
                break;
            case 0x80BE: // target_obj
                PushInt(_externals.TargetObjectId());
                break;
            case 0x80BF: // dude_obj
                PushInt(_externals.DudeObjectId());
                break;
            case 0x80C0: // obj_being_used_with
                PushInt(_externals.ObjectBeingUsedWithId());
                break;
            case 0x80F7: // fixed_param
                PushInt(_externals.FixedParam());
                break;
            case 0x80FA: // action_being_used
                PushInt(_externals.ActionBeingUsed());
                break;
            case 0x80C7: // script_action
                PushInt(_externals.ScriptAction());
                break;
            case 0x810B: // metarule (opMetarule pops param, then rule)
            {
                Value param = Pop();
                int rule = PopInt();
                PushInt(_externals.Metarule(rule, param.Tag == TypeInt ? param.Raw : 0));
                break;
            }

            // ---- pure functions (phase-4 report M0: stub-0 rolls are a trap —
            // critical(0) would fire jam/explosion branches)
            case 0x80B4: // random (opRandom pops max, then min)
            {
                int max = PopInt();
                int min = PopInt();
                PushInt(min >= max ? min : _random.Next(min, max + 1));
                break;
            }
            case 0x80F2: // game_ticks: seconds * 10
                PushInt(PopInt() * 10);
                break;
            case 0x80AC: // roll_vs_skill (pops modifier, skill, obj) — PoC: plain success
                Pop();
                Pop();
                Pop();
                PushInt(RollSuccess);
                break;
            case 0x80AE: // do_check (opDoCheck pops modifier, stat, obj)
            {
                int modifier = PopInt();
                int stat = PopInt();
                PushInt(_externals.DoCheck(PopInt(), stat, modifier));
                break;
            }
            case 0x80CA: // get_critter_stat (pops stat, obj)
            {
                int stat = PopInt();
                PushInt(_externals.GetCritterStat(PopInt(), stat));
                break;
            }
            case 0x80CB: // set_critter_stat — ADJUSTS base stat (pops amount, stat, obj)
            {
                int amount = PopInt();
                int stat = PopInt();
                PushInt(_externals.AdjustCritterBaseStat(PopInt(), stat, amount));
                break;
            }
            case 0x80F3: // has_trait (pops param, obj, type)
            {
                int traitParam = PopInt();
                int traitObj = PopInt();
                PushInt(_externals.HasTrait(PopInt(), traitObj, traitParam));
                break;
            }
            case 0x80A6: // get_pc_stat
                PushInt(_externals.GetPcStat(PopInt()));
                break;
            case 0x8102: // critter_add_trait (pops value, param, kind, obj; pushes -1)
            {
                int traitValue = PopInt();
                int traitParam = PopInt();
                int traitKind = PopInt();
                _externals.CritterAddTrait(PopInt(), traitKind, traitParam, traitValue);
                PushInt(-1);
                break;
            }
            case 0x80D0: // attack (opAttackComplex pops 7 args, then target)
            case 0x80DD: // attack_complex — same engine handler
            {
                for (int i = 0; i < 7; i++)
                    Pop();
                _externals.AttackComplex(PopInt());
                break;
            }
            case 0x80E7: // anim_busy
                PushInt(_externals.AnimBusy(PopInt()) ? 1 : 0);
                break;
            case 0x80A1: // give_exp_points
                _externals.GiveExpPoints(PopInt());
                break;
            case 0x80A9: // override_map_start (pops rotation, elevation, y, x)
            {
                int omsRotation = PopInt();
                int omsElevation = PopInt();
                int omsY = PopInt();
                _externals.OverrideMapStart(PopInt(), omsY, omsElevation, omsRotation);
                break;
            }
            case 0x814C: // rotation_to_tile (pops destTile, srcTile)
            {
                int destTile = PopInt();
                PushInt(Hex.HexGrid.RotationTo(PopInt(), destTile));
                break;
            }
            case 0x80AF: // success: ROLL_SUCCESS or ROLL_CRITICAL_SUCCESS
            {
                int roll = PopInt();
                PushInt(roll is RollSuccess or RollCriticalSuccess ? 1 : 0);
                break;
            }
            case 0x80B0: // critical: ROLL_CRITICAL_FAILURE or ROLL_CRITICAL_SUCCESS
            {
                int roll = PopInt();
                PushInt(roll is RollCriticalFailure or RollCriticalSuccess ? 1 : 0);
                break;
            }

            // ---- dialog (handlers ported from interpreter_extra.cc
            // _op_gsay_* / opStartGameDialog; text resolved here so the host
            // only sees strings + procedure indices)
            case 0x80DE: // start_gdialog (pops background, head, reaction, obj, msgList)
            {
                int backgroundId = PopInt();
                int headId = PopInt();
                Pop(); // reactionLevel
                Pop(); // obj
                Pop(); // msgListId — discarded by the engine too
                _externals.DialogSessionStart(headId, backgroundId);
                break;
            }
            case 0x80DF: // end_dialogue
                _externals.DialogSessionEnd();
                break;
            case 0x811C: // gsay_start
                _externals.DialogStart();
                break;
            case 0x811D: // gsay_end
                _externals.DialogEnd();
                break;
            case 0x811E: // gsay_reply (pops msg-or-string, then msgList)
            {
                Value msg = Pop();
                int listId = PopInt();
                _externals.DialogReply(ResolveDialogText(listId, msg));
                break;
            }
            case 0x811F: // gsay_option (pops reaction, proc, msg, msgList)
            case 0x8121: // giq_option (additionally pops iq LAST — i.e. first pushed)
            {
                int reaction = PopInt();
                Value proc = Pop();
                Value msg = Pop();
                int listId = PopInt();
                if (opcode == 0x8121)
                {
                    int iq = PopInt();
                    // ported from _op_giq_option: the dude's real IN gates dumb/smart options
                    // (positive iq = min, negative = dumb-only max). P25 feeds the real INT.
                    if (!DialogGate.IqOptionVisible(iq, _externals.DialogIntelligence()))
                        break;
                }

                int procedureIndex = proc.Tag == TypeInt
                    ? proc.Raw
                    : _program.FindProcedure(AsString(proc)); // name variant: resolve (engine drops it)
                if (procedureIndex >= 0)
                    _externals.DialogOption(ResolveDialogText(listId, msg), procedureIndex, reaction);
                break;
            }
            case 0x8120: // gsay_message (pops reaction, msg, msgList): reply + auto-done + present
            {
                Pop(); // reaction
                Value msg = Pop();
                int listId = PopInt();
                _externals.DialogReply(ResolveDialogText(listId, msg));
                _externals.DialogEnd();
                break;
            }
            case 0x810A: // float_msg (pops type, msg, obj)
            {
                int type = PopInt();
                Value msg = Pop();
                int objectHandle = PopInt();
                _externals.FloatMessage(objectHandle, AsString(msg), type);
                break;
            }
            case 0x8129: // gdialog_barter — flag-only; arg OVERWRITES the modifier
                _externals.Barter(PopInt());
                break;
            case 0x814E: // gdialog_set_barter_mod
                _externals.GdialogSetBarterMod(PopInt());
                break;
            case 0x8115: // play_gmovie
                _externals.PlayMovie(PopInt());
                break;
            case 0x8124: // party_add
                _externals.PartyAdd(PopInt());
                break;
            case 0x8125: // party_remove
                _externals.PartyRemove(PopInt());
                break;
            case 0x814B: // party_member_obj (pops pid, pushes handle)
                PushInt(_externals.PartyMemberByPid(PopInt()));
                break;
            case 0x80EF: // critter_damage (pops typeWithFlags, amount, obj)
            {
                int damageTypeWithFlags = PopInt();
                int damageAmount = PopInt();
                _externals.CritterDamage(PopInt(), damageAmount, damageTypeWithFlags);
                break;
            }
            case 0x8147: // move_obj_inven_to_obj (pops dest, then source)
            {
                int moveDest = PopInt();
                _externals.MoveAllInventory(PopInt(), moveDest);
                break;
            }

            // ---- door/container state
            case 0x812D: // obj_is_locked
                PushInt(_externals.ObjIsLocked(PopInt()) ? 1 : 0);
                break;
            case 0x812E: // obj_lock
            case 0x814D: // jam_lock (PoC: a jammed lock is just a locked lock)
                _externals.ObjSetLocked(PopInt(), true);
                break;
            case 0x812F: // obj_unlock
                _externals.ObjSetLocked(PopInt(), false);
                break;
            case 0x8130: // obj_is_open
                PushInt(_externals.ObjIsOpen(PopInt()) ? 1 : 0);
                break;
            case 0x8131: // obj_open
                _externals.ObjSetOpen(PopInt(), true);
                break;
            case 0x8132: // obj_close
                _externals.ObjSetOpen(PopInt(), false);
                break;

            // ---- world mutation (pop orders from interpreter_extra.cc handlers)
            case 0x80B7: // create_object_sid (pops sid, elevation, tile, pid)
            {
                int scriptIndex = PopInt(); // scripts.lst index, -1 = unscripted
                int elevation = PopInt();
                int tile = PopInt();
                int pid = PopInt();
                PushInt(_externals.CreateObject(pid, tile, elevation, scriptIndex));
                break;
            }
            case 0x80F4: // destroy_object
                _externals.DestroyObject(PopInt());
                break;
            case 0x80D8: // add_obj_to_inven (pops item, target)
            {
                int item = PopInt();
                _externals.AddToInventory(PopInt(), item, 1);
                break;
            }
            case 0x8116: // add_mult_objs_to_inven (pops quantity, item, target)
            {
                int quantity = PopInt();
                int item = PopInt();
                _externals.AddToInventory(PopInt(), item, quantity);
                break;
            }
            case 0x80D9: // rm_obj_from_inven (pops item, target)
            {
                int item = PopInt();
                _externals.RemoveFromInventory(PopInt(), item, 1);
                break;
            }
            case 0x8117: // rm_mult_objs_from_inven (pops quantity, item, target) -> removed count
            {
                int quantity = PopInt();
                int item = PopInt();
                PushInt(_externals.RemoveFromInventory(PopInt(), item, quantity));
                break;
            }
            case 0x80BA: // obj_is_carrying_obj (pops pid, critter) -> quantity carried
            {                // ported from fallout2-ce src/interpreter_extra.cc:1040
                int pid = PopInt();
                PushInt(_externals.ObjIsCarryingPid(PopInt(), pid));
                break;
            }
            case 0x810D: // obj_carrying_pid_obj (pops pid, critter) -> item handle (or 0)
            {                // ported from fallout2-ce src/interpreter_extra.cc:3438
                int pid = PopInt();
                PushInt(_externals.ObjCarryingPidObj(PopInt(), pid));
                break;
            }
            case 0x80B6: // move_to (pops elevation, tile, obj) -> new tile
            {
                int elevation = PopInt();
                int tile = PopInt();
                PushInt(_externals.MoveTo(PopInt(), tile, elevation));
                break;
            }
            case 0x80E3: // set_obj_visibility (pops hidden, obj)
            {
                int hidden = PopInt();
                _externals.SetObjectVisibility(PopInt(), hidden != 0);
                break;
            }
            case 0x8100: // obj_pid
                PushInt(_externals.ObjPid(PopInt()));
                break;
            case 0x80C8: // obj_type — PID_TYPE of the object's pid
            {
                int pid = _externals.ObjPid(PopInt());
                PushInt(pid == -1 ? -1 : pid >> 24);
                break;
            }
            case 0x80A7: // tile_contains_pid_obj (pops pid, elevation, tile)
            {
                int pid = PopInt();
                int elevation = PopInt();
                PushInt(_externals.TileContainsPidObj(PopInt(), elevation, pid));
                break;
            }

            // ---- timers + geometry + caps (phase-5 M0)
            case 0x80F0: // add_timer_event (pops param, delay, obj)
            {
                int param = PopInt();
                int delay = PopInt();
                _externals.AddTimerEvent(PopInt(), delay, param);
                break;
            }
            case 0x80F1: // rm_timer_event
                _externals.RemoveTimerEvents(PopInt());
                break;
            case 0x80E1: // metarule3 (pops p3, p2, p1, rule); rule 100 clears (obj, param) timers
            {
                Value p3 = Pop();
                Value p2 = Pop();
                Value p1 = Pop();
                int rule = PopInt();
                _ = p3;
                if (rule == 100 && p1.Tag == TypeInt && p2.Tag == TypeInt)
                    _externals.RemoveTimerEventsWithParam(p1.Raw, p2.Raw);
                PushInt(0);
                break;
            }
            case 0x80D4: // tile_num
                PushInt(_externals.ObjTile(PopInt()));
                break;
            case 0x80D2: // tile_distance (pops tile2, tile1)
            {
                int tile2 = PopInt();
                PushInt(Hex.HexGrid.Distance(PopInt(), tile2));
                break;
            }
            case 0x80D3: // tile_distance_objs (pops obj2, obj1)
            {
                int tileB = _externals.ObjTile(PopInt());
                int tileA = _externals.ObjTile(PopInt());
                PushInt(Hex.HexGrid.Distance(tileA, tileB));
                break;
            }
            case 0x80D5: // tile_num_in_direction (pops distance, rotation, tile)
            {
                int distance = PopInt();
                int rotation = PopInt();
                int tile = PopInt();
                PushInt(rotation is >= 0 and < 6
                    ? Hex.HexGrid.TileInDirection(tile, rotation, Math.Max(distance, 0))
                    : tile);
                break;
            }
            case 0x8101: // cur_map_index
                PushInt(_externals.CurrentMapIndex());
                break;
            case 0x8138: // item_caps_total
                PushInt(_externals.CapsTotal(PopInt()));
                break;
            case 0x8139: // item_caps_adjust (pops amount, obj)
            {
                int amount = PopInt();
                PushInt(_externals.CapsAdjust(PopInt(), amount));
                break;
            }
            case 0x80DC: // obj_can_see_obj (pops target, source)
            case 0x80F5: // obj_can_hear_obj — same PoC distance check
            {
                int target = PopInt();
                PushInt(_externals.ObjCanSee(PopInt(), target) ? 1 : 0);
                break;
            }
            case 0x80AB: // using_skill (pops skill, object) — interpreter_extra.cc:579
            {
                int skill = PopInt();
                PushInt(_externals.IsUsingSkill(PopInt(), skill) ? 1 : 0);
                break;
            }
            case 0x80FF: // critter_attempt_placement (pops elevation, tile, critter) — interpreter_extra.cc:2812
            {
                int elevation = PopInt();
                int tile = PopInt();
                PushInt(_externals.CritterAttemptPlacement(PopInt(), tile, elevation) ? 1 : 0);
                break;
            }
            case 0x80CE: // animate_move_obj_to_tile (pops speed, tile, obj)
            {
                int speed = PopInt();
                int tile = PopInt();
                _externals.AnimateMoveToTile(PopInt(), tile, speed);
                break;
            }
            case 0x80E9: // set_light_level (pops level)
                _externals.SetLightLevel(PopInt());
                break;
            case 0x8107: // obj_set_light_level (pops distance, intensity, obj)
            {
                int distance = PopInt();
                int intensity = PopInt();
                _externals.SetObjectLightLevel(PopInt(), intensity, distance);
                break;
            }
            case 0x8126: // reg_anim_animate_forever (pops anim, obj)
            {
                int anim = PopInt();
                _externals.RegAnimAnimateForever(PopInt(), anim);
                break;
            }

            // ---- reg_anim batch (interpreter_extra.cc opRegAnim*). The engine gates
            // these on !isInCombat(); the args are popped first either way, so we always
            // pop here and let the host skip execution mid-combat (it keeps the stack
            // balanced). reg_anim_func pops (param, cmd); the move/animate ops pop
            // (delay, target, obj) like their opcode handlers.
            case 0x810E: // reg_anim_func
            {
                Value param = Pop();
                int cmd = PopInt();
                switch (cmd)
                {
                    case 1: _externals.RegAnimBegin(); break;          // OP_REG_ANIM_FUNC_BEGIN
                    case 2: _externals.RegAnimClear(param.Raw); break; // OP_REG_ANIM_FUNC_CLEAR (param = object)
                    case 3: _externals.RegAnimEnd(); break;            // OP_REG_ANIM_FUNC_END
                }
                break;
            }
            case 0x810F: // reg_anim_animate (pops delay, anim, obj)
            case 0x8110: // reg_anim_animate_reverse
            {
                int delay = PopInt();
                int anim = PopInt();
                _externals.RegAnimAnimate(PopInt(), anim, delay, opcode == 0x8110);
                break;
            }
            case 0x8111: // reg_anim_obj_move_to_obj (pops delay, dest, obj)
            case 0x8112: // reg_anim_obj_run_to_obj
            {
                int delay = PopInt();
                int dest = PopInt();
                _externals.RegAnimMoveToObject(PopInt(), dest, delay, opcode == 0x8112);
                break;
            }
            case 0x8113: // reg_anim_obj_move_to_tile (pops delay, tile, obj)
            case 0x8114: // reg_anim_obj_run_to_tile
            {
                int delay = PopInt();
                int tile = PopInt();
                _externals.RegAnimMoveToTile(PopInt(), tile, delay, opcode == 0x8114);
                break;
            }

            // ---- clock (ported from fallout2-ce scripts.cc gameTimeGetHour())
            case 0x80EA: // game_time (ticks)
                PushInt(_externals.GameTime());
                break;
            case 0x80EB: // game_time_in_seconds
                PushInt(_externals.GameTime() / 10);
                break;
            case 0x80F6: // game_time_hour (hhmm)
            {
                int time = _externals.GameTime();
                PushInt(100 * (time / 600 / 60 % 24) + time / 600 % 60);
                break;
            }
            case 0x8118: // month (epoch June 24, 2241; 10 ticks/s)
            {
                int day = _externals.GameTime() / 864000;
                PushInt(MonthFromEpochDay(day));
                break;
            }
            default:
            {
                if (!ExternalArity.Table.TryGetValue(opcode, out (string Name, int Args, bool Returns) arity))
                    throw new InvalidDataException($"Unknown external opcode 0x{opcode:X4}.");

                for (int i = 0; i < arity.Args; i++)
                    Pop();
                if (arity.Returns)
                    PushInt(0);
                _onStubbedExternal?.Invoke(
                    $"stubbed external {arity.Name} (0x{opcode:X4}): popped {arity.Args}"
                    + (arity.Returns ? ", pushed 0" : ""));
                break;
            }
        }
    }

    // ------------------------------------------------------------ stack/values

    private void Push(Value value) => _stack.Add(value);

    private void PushInt(int value) => _stack.Add(Value.Int(value));

    /// <summary>programPushString() reduced to a list of dynamic strings.</summary>
    private void PushString(string? value)
    {
        _dynamicStrings.Add(value ?? "Error");
        _stack.Add(new Value(TypeDynamicString, _dynamicStrings.Count - 1));
    }

    private Value Pop()
    {
        if (_stack.Count == 0)
            throw new InvalidDataException("Data stack underflow.");
        Value value = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return value;
    }

    private int PopInt()
    {
        Value value = Pop();
        if (value.Tag != TypeInt)
            throw new InvalidDataException($"Expected an int on the stack, got tag 0x{value.Tag:X4}.");
        return value.Raw;
    }

    private string PopString() => AsString(Pop());

    private Value ReturnPop()
    {
        if (_returnStack.Count == 0)
            throw new InvalidDataException("Return stack underflow.");
        Value value = _returnStack[^1];
        _returnStack.RemoveAt(_returnStack.Count - 1);
        return value;
    }

    private int ReturnPopInt()
    {
        Value value = ReturnPop();
        if (value.Tag != TypeInt)
            throw new InvalidDataException($"Expected an int on the return stack, got tag 0x{value.Tag:X4}.");
        return value.Raw;
    }

    private void ReturnPush(Value value) => _returnStack.Add(value);

    private IntProcedure ProcedureAt(int index)
    {
        if (index < 0 || index >= _program.Procedures.Count)
            throw new InvalidDataException($"Procedure index {index} is out of range.");
        return _program.Procedures[index];
    }

    private Value StackAt(int index)
    {
        if (index < 0 || index >= _stack.Count)
            throw new InvalidDataException($"Stack access at {index} is out of range (stack desync).");
        return _stack[index];
    }

    private void StackSet(int index, Value value)
    {
        if (index < 0 || index >= _stack.Count)
            throw new InvalidDataException($"Stack store at {index} is out of range (stack desync).");
        _stack[index] = value;
    }

    private string AsString(Value value) => value.Tag switch
    {
        TypeStaticString => _program.GetStaticString(value.Raw),
        TypeDynamicString when value.Raw >= 0 && value.Raw < _dynamicStrings.Count => _dynamicStrings[value.Raw],
        TypeDynamicString => throw new InvalidDataException($"Bad dynamic string handle {value.Raw}."),
        TypeInt => value.Raw.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new InvalidDataException($"Cannot read tag 0x{value.Tag:X4} as a string."),
    };

    /// <summary>ported from ProgramValue::isEmpty() (dynamic strings fall through to empty).</summary>
    private static bool IsEmpty(Value value) => value.Tag switch
    {
        TypeInt or TypeStaticString => value.Raw == 0,
        _ => true,
    };

    /// <summary>Truthiness for and/or, ported from opLogicalOperatorAnd/Or: strings are always true.</summary>
    private static bool IsTruthy(Value value) => value.IsString || value.Raw != 0;
}
