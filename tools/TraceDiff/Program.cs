using Hexwaste.Formats.Trace;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: TraceDiff <fo2ce.trace> <hexwaste.trace>");
    return 2;
}
string[] expected = File.ReadAllLines(args[0]);
string[] actual = File.ReadAllLines(args[1]);
TraceDivergence? d = TraceComparer.FirstDivergence(expected, actual);
if (d is null)
{
    Console.WriteLine($"IDENTICAL ({expected.Count(l => !l.StartsWith('#'))} lines)");
    return 0;
}
Console.WriteLine($"DIVERGED at fo2ce line {d.LineNumber}");
foreach (string c in d.Context)
    Console.WriteLine($"  {c}");
Console.WriteLine($"- fo2ce:    {d.Expected ?? "<end of trace>"}");
Console.WriteLine($"+ hexwaste: {d.Actual ?? "<end of trace>"}");
return 1;
