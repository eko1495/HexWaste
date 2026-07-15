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

    public sealed record Result(
        IntProgram Program,
        IReadOnlyList<ConstWrite> Writes,
        IReadOnlyList<OptionEdge> Options,
        IReadOnlyList<(int FromProc, int ToProc)> Calls,
        IReadOnlyList<ItemCheck> ItemChecks);

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

        for (int b = 0; b < bodies.Count; b++)
        {
            int procIndex = bodies[b].Index;
            int start = bodies[b].Proc.BodyOffset;
            int end = b + 1 < bodies.Count ? bodies[b + 1].Proc.BodyOffset : data.Length;
            ScanRange(program, data, procIndex, start, end, writes, options, calls, itemChecks);
        }

        return new Result(program, writes, options, calls, itemChecks);
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
