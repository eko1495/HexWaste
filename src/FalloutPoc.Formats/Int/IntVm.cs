namespace FalloutPoc.Formats.Int;

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

    private int _instructionPointer;
    private int _framePointer = -1;
    private int _basePointer = -1;
    private int _flags;
    private int _windowId;
    private bool _initialized;

    public IntVm(IntProgram program, IVmExternals externals, Action<string>? onStubbedExternal = null)
    {
        _program = program;
        _externals = externals;
        _onStubbedExternal = onStubbedExternal;
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
        if (index < 0)
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
        _externalVariables[identifier] = Pop();
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
