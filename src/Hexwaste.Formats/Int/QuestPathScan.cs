namespace Hexwaste.Formats.Int;

/// <summary>
/// The P128 quest-path finder's static per-procedure scan: for one compiled script,
/// attribute every const <c>set_global_var</c> write to its procedure and extract the
/// dialog graph — <c>gsay_option</c>/<c>giq_option</c> (0x811F/0x8121) edges whose target
/// procedure is a compile-time constant (an int proc index, or a static-string proc name
/// resolved via <see cref="IntProgram.FindProcedure"/>), plus direct <c>call</c> (0x8005)
/// edges. BFS over those edges from <c>talk_p_proc</c> turns "which script completes this
/// quest" (the P124 census) into "which option picks reach the write" (a fixture-authoring
/// guide). Option ordinals are STATIC — the position among the node's option calls in code
/// order — and can shift at runtime when a giq_option is IQ-filtered; the guide says where
/// to look, the harness run confirms the live index.
/// </summary>
public static class QuestPathScan
{
    /// <summary>An option edge: picking ordinal N (0-based, static code order) in
    /// <paramref name="FromProc"/> runs <paramref name="ToProc"/>.</summary>
    public sealed record OptionEdge(int FromProc, int Ordinal, int ToProc);

    public sealed record ConstWrite(int Proc, int Gvar, int Value);

    /// <summary>An <c>obj_carrying_pid_obj(pid)</c> (0x810D) check inside a procedure — the item
    /// gate a completing node tests. The quest-driver pre-gives these pids.</summary>
    public sealed record ItemCheck(int Proc, int Pid);

    /// <summary>A single-bit test <c>global_var(Gvar) &amp; Mask</c> inside a procedure — the
    /// pattern a dialog node uses to gate an option on another quest's task bit (e.g. Rebecca's
    /// turn-in reads <c>446 &amp; 0x100</c>). The quest-driver's bit-level prerequisite resolver
    /// (P137) matches these to <see cref="BitSet"/>s of the SAME (gvar,mask) — the fix for the
    /// shared-task-bitfield over-inclusion that sank the gvar-level attempt (plan §10).</summary>
    public sealed record BitCheck(int Proc, int Gvar, int Mask);

    /// <summary>A single-bit set <c>global_var(Gvar) |= Mask</c> (the read-modify-write
    /// <c>push G; push G; get_global; push Mask; bitwise_or; set_global</c>) — how one NPC's node
    /// records it did a sub-task in a shared task bitfield (e.g. Fred's demand-full sets
    /// <c>446 |= 0x8000</c>). Matched to a completer's <see cref="BitCheck"/> by exact mask.</summary>
    public sealed record BitSet(int Proc, int Gvar, int Mask);

    public sealed record Result(
        IntProgram Program,
        IReadOnlyList<ConstWrite> Writes,
        IReadOnlyList<OptionEdge> Options,
        IReadOnlyList<(int FromProc, int ToProc)> Calls,
        IReadOnlyList<ItemCheck> ItemChecks,
        IReadOnlyList<BitCheck> BitChecks,
        IReadOnlyList<BitSet> BitSets);

    public static Result Scan(byte[] data)
    {
        IntProgram program = IntProgram.Load(data);
        var writes = new List<ConstWrite>();
        var options = new List<OptionEdge>();
        var calls = new List<(int, int)>();
        var itemChecks = new List<ItemCheck>();

        // Per-procedure ranges: bodies are contiguous in body-offset order to EOF
        // (imported procs have no body and are skipped).
        var bodies = program.Procedures
            .Select((p, i) => (Proc: p, Index: i))
            .Where(t => t.Proc.BodyOffset > 0 && !t.Proc.IsImported)
            .OrderBy(t => t.Proc.BodyOffset)
            .ToList();

        var bitChecks = new List<BitCheck>();
        var bitSets = new List<BitSet>();

        for (int b = 0; b < bodies.Count; b++)
        {
            int procIndex = bodies[b].Index;
            int start = bodies[b].Proc.BodyOffset;
            int end = b + 1 < bodies.Count ? bodies[b + 1].Proc.BodyOffset : data.Length;
            ScanRange(program, data, procIndex, start, end, writes, options, calls, itemChecks);
            ScanBits(data, procIndex, start, end, bitChecks, bitSets);
        }

        return new Result(program, writes, options, calls, itemChecks, bitChecks, bitSets);
    }

    // Opcodes for the bit-level patterns (interpreter.h): a normalized op word is 0x8000 | (word & 0x3FF).
    private const int OP_GET_GLOBAL = 0x80C5, OP_SET_GLOBAL = 0x80C6;
    private const int OP_BITWISE_AND = 0x8040, OP_BITWISE_OR = 0x8041;

    /// <summary>Second pass over one proc body: decode to a flat (isPush, value) token stream and
    /// match the two single-bit patterns exactly (P137 bit-level prerequisite analysis).
    /// <para>BitCheck  <c>global(G) &amp; MASK</c>: push G, get_global, push MASK, bitwise_and.</para>
    /// <para>BitSet <c>global(G) |= MASK</c>: push G, push G, get_global, push MASK, bitwise_or,
    /// set_global (the RMW confirms the same G on both the get and the set).</para></summary>
    private static void ScanBits(byte[] data, int procIndex, int start, int end,
        List<BitCheck> bitChecks, List<BitSet> bitSets)
    {
        // Decode the body into tokens: (IsPush, Value) — Value = operand for a push, else the
        // normalized opcode word. Non-push non-op words (locals/inline) become op tokens too, which
        // simply never complete a pattern.
        var toks = new List<(bool IsPush, int Value)>();
        int pc = start;
        while (pc + 2 <= end)
        {
            ushort word = (ushort)((data[pc] << 8) | data[pc + 1]);
            pc += 2;
            if ((word & 0x8000) == 0) { toks.Add((false, word)); continue; }
            if ((word & 0x3FF) == 0x001) // push: 4-byte operand follows
            {
                if (pc + 4 > end) break;
                int operand = (data[pc] << 24) | (data[pc + 1] << 16) | (data[pc + 2] << 8) | data[pc + 3];
                pc += 4;
                toks.Add((true, operand));
                continue;
            }
            toks.Add((false, 0x8000 | (word & 0x3FF)));
        }

        for (int i = 0; i < toks.Count; i++)
        {
            // BitCheck: push G, get_global, push MASK, bitwise_and
            if (i + 3 < toks.Count
                && toks[i].IsPush
                && !toks[i + 1].IsPush && toks[i + 1].Value == OP_GET_GLOBAL
                && toks[i + 2].IsPush
                && !toks[i + 3].IsPush && toks[i + 3].Value == OP_BITWISE_AND)
            {
                bitChecks.Add(new BitCheck(procIndex, toks[i].Value, toks[i + 2].Value));
            }
            // BitSet: push G, push G, get_global, push MASK, bitwise_or, set_global
            if (i + 5 < toks.Count
                && toks[i].IsPush && toks[i + 1].IsPush && toks[i].Value == toks[i + 1].Value
                && !toks[i + 2].IsPush && toks[i + 2].Value == OP_GET_GLOBAL
                && toks[i + 3].IsPush
                && !toks[i + 4].IsPush && toks[i + 4].Value == OP_BITWISE_OR
                && !toks[i + 5].IsPush && toks[i + 5].Value == OP_SET_GLOBAL)
            {
                bitSets.Add(new BitSet(procIndex, toks[i].Value, toks[i + 3].Value));
            }
        }
    }

    private static void ScanRange(IntProgram program, byte[] data, int procIndex, int start, int end,
        List<ConstWrite> writes, List<OptionEdge> options, List<(int, int)> calls, List<ItemCheck> itemChecks)
    {
        // The rolling last-two-push window (like GlobalWriteScan): Tag 0 = not a push.
        (ushort Tag, int Value) prev1 = default, prev2 = default;
        int optionOrdinal = 0;
        int pc = start;
        while (pc + 2 <= end)
        {
            ushort word = (ushort)((data[pc] << 8) | data[pc + 1]);
            pc += 2;
            if ((word & 0x8000) == 0)
            {
                prev2 = prev1 = default;
                continue;
            }
            if ((word & 0x3FF) == 0x001) // push: 4-byte operand
            {
                int operand = (data[pc] << 24) | (data[pc + 1] << 16) | (data[pc + 2] << 8) | data[pc + 3];
                pc += 4;
                prev2 = prev1;
                prev1 = (word, operand);
                continue;
            }

            switch (0x8000 | (word & 0x3FF))
            {
                case 0x80C6: // set_global_var: [push gvar][push value][op]
                    if (prev1.Tag == 0xC001 && prev2.Tag == 0xC001)
                        writes.Add(new ConstWrite(procIndex, prev2.Value, prev1.Value));
                    break;

                case 0x811F: // gsay_option: [..][push proc][push reaction][op]
                case 0x8121: // giq_option: same tail (iq is pushed first, far behind)
                {
                    int target = ResolveProc(program, prev2);
                    if (target >= 0)
                        options.Add(new OptionEdge(procIndex, optionOrdinal, target));
                    optionOrdinal++; // unresolved targets still occupy an option slot
                    break;
                }

                case 0x8005: // call: pops the proc index pushed immediately before
                    if (prev1.Tag == 0xC001 && prev1.Value >= 0 && prev1.Value < program.Procedures.Count)
                        calls.Add((procIndex, prev1.Value));
                    break;

                case 0x810D: // obj_carrying_pid_obj(obj, pid): pid is the const pushed immediately before
                    if (prev1.Tag == 0xC001 && prev1.Value > 0)
                        itemChecks.Add(new ItemCheck(procIndex, prev1.Value));
                    break;
            }
            prev2 = prev1;
            prev1 = default;
        }
    }

    /// <summary>An option's target proc operand: an int proc index, or a static-string
    /// proc name (the compiler emits either — IntVm resolves names the same way).</summary>
    private static int ResolveProc(IntProgram program, (ushort Tag, int Value) push)
    {
        if (push.Tag == 0xC001)
            return push.Value >= 0 && push.Value < program.Procedures.Count ? push.Value : -1;
        if (push.Tag == 0x9001)
        {
            try { return program.FindProcedure(program.GetStaticString(push.Value)); }
            catch (InvalidDataException) { return -1; }
        }
        return -1;
    }

    /// <summary>The shortest node-index route from <paramref name="fromProc"/> to
    /// <paramref name="toProc"/> over the option + call graph — the sequence of target proc
    /// indices to reach (excluding the start), or null if unreachable. The quest-driver matches
    /// LIVE dialogue options to these by <c>OptionProcedure</c> (drift-proof vs. static ordinals).</summary>
    public static List<int>? FindPathProcs(Result scan, int fromProc, int toProc)
    {
        if (fromProc == toProc)
            return [];
        var edges = new Dictionary<int, List<int>>();
        foreach (OptionEdge e in scan.Options)
            (edges.TryGetValue(e.FromProc, out var l) ? l : edges[e.FromProc] = []).Add(e.ToProc);
        foreach ((int from, int to) in scan.Calls)
            (edges.TryGetValue(from, out var l) ? l : edges[from] = []).Add(to);

        var previous = new Dictionary<int, int>();
        var queue = new Queue<int>();
        queue.Enqueue(fromProc);
        var seen = new HashSet<int> { fromProc };
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            foreach (int to in edges.GetValueOrDefault(at, []))
            {
                if (!seen.Add(to))
                    continue;
                previous[to] = at;
                if (to == toProc)
                {
                    var path = new List<int>();
                    for (int p = toProc; p != fromProc; p = previous[p])
                        path.Insert(0, p);
                    return path;
                }
                queue.Enqueue(to);
            }
        }
        return null;
    }

    /// <summary>Shortest edge path from <paramref name="fromProc"/> to
    /// <paramref name="toProc"/> over the option + call graph, or null. Each step is
    /// rendered "=optN=&gt;" (pick static option ordinal N) or "=call=&gt;".</summary>
    public static List<string>? FindPath(Result scan, int fromProc, int toProc)
    {
        if (fromProc == toProc)
            return [];
        var edges = new Dictionary<int, List<(string Label, int To)>>();
        foreach (OptionEdge e in scan.Options)
            (edges.TryGetValue(e.FromProc, out var l) ? l : edges[e.FromProc] = [])
                .Add(($"=opt{e.Ordinal}=>", e.ToProc));
        foreach ((int from, int to) in scan.Calls)
            (edges.TryGetValue(from, out var l) ? l : edges[from] = []).Add(("=call=>", to));

        var previous = new Dictionary<int, (int From, string Label)>();
        var queue = new Queue<int>();
        queue.Enqueue(fromProc);
        var seen = new HashSet<int> { fromProc };
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            foreach ((string label, int to) in edges.GetValueOrDefault(at, []))
            {
                if (!seen.Add(to))
                    continue;
                previous[to] = (at, label);
                if (to == toProc)
                {
                    var path = new List<string>();
                    for (int p = toProc; p != fromProc; p = previous[p].From)
                        path.Insert(0, $"{previous[p].Label} {scan.Program.Procedures[p].Name}");
                    return path;
                }
                queue.Enqueue(to);
            }
        }
        return null;
    }
}
