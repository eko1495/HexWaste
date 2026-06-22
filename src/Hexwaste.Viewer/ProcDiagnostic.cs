using System;
using System.Collections.Generic;
using Hexwaste.Formats;
using Hexwaste.Formats.Int;

namespace Hexwaste.Viewer;

public static class ProcDiagnostic
{
    public static void EnumerateModocProcs(GameFileSystem vfs)
    {
        var scriptList = ScriptList.Load(vfs);
        int[] indices = { 98, 100, 101, 102, 103, 104, 105, 194, 203, 553, 560, 572, 575, 577, 580, 815, 816, 825, 985, 96, 1023 };

        foreach (int idx in indices)
        {
            string? name = scriptList.GetName(idx);
            if (name == null) continue;
            
            try
            {
                using var stream = vfs.OpenRead($@"scripts\{name}.int");
                var prog = IntProgram.Load(stream);
                
                var procNames = new List<string>();
                foreach (var proc in prog.Procedures)
                {
                    procNames.Add(proc.Name);
                }
                
                Console.WriteLine($"{idx:3} {name:30} => {prog.Procedures.Count:2} procs: {string.Join(", ", procNames)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{idx:3} {name:30} => ERROR: {ex.Message}");
            }
        }
    }
}
