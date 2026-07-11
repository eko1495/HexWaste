#!/usr/bin/env python3
"""Operand-level disassembler for one procedure of a Fallout 2 .int script.

int_analyze.py summarises opcode *usage*; this dumps the linear instruction stream of a named
procedure WITH push operands — the missing piece for quest-fixture archaeology (finding the
item pid an use_obj_on_p_proc checks, the exact gvar := value a node writes, the option chain
a dialog takes). Reuses int_analyze's big-endian parser.

Usage:  python3 tools/int_disasm.py <script.int> <proc_name>
        python3 tools/int_disasm.py <script.int> --writes <gvar>   # procs that set_global_var <gvar>
"""
import sys
import struct
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import int_analyze as ia


def decode(data, start, end):
    out = []
    pc = start
    while pc + 2 <= end:
        w = ia.u16(data, pc)
        if (w >> 8) & 0x80 == 0:
            pc += 2
            continue
        off = pc
        pc += 2
        if (w & 0x3FF) == 0x001:  # OPCODE_PUSH — 4-byte inline operand
            val = struct.unpack_from(">i", data, pc)[0]
            pc += 4
            typ = ia.PUSH_TYPES.get(w, hex(w))
            extra = f"  (0x{val & 0xFFFFFFFF:X})" if typ == "int" else ""
            out.append((off, f"push_{typ} {val}{extra}"))
        else:
            out.append((off, ia.opname(w)))
    return out


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1
    path = sys.argv[1]
    data, procs, code_start = ia.parse(path)
    bodies = sorted(p["body"] for p in procs if p["body"] > 0)

    def proc_range(p):
        start = p["body"]
        return start, min([b for b in bodies if b > start], default=len(data))

    if sys.argv[2] == "--writes":
        gvar = sys.argv[3]
        for p in sorted(procs, key=lambda x: x["body"]):
            if p["body"] <= 0:
                continue
            lines = decode(data, *proc_range(p))
            for i, (off, t) in enumerate(lines):
                if t.startswith(f"push_int {gvar} ") or t == f"push_int {gvar}":
                    # look ahead for a set_global_var within a few instrs
                    tail = " ".join(x[1] for x in lines[i:i + 4])
                    if "set_global_var" in tail:
                        val = next((x[1] for x in lines[i + 1:i + 3]
                                    if x[1].startswith("push_int")), "?")
                        print(f"{p['name']}: gvar {gvar} := {val.split()[1]}  @{off:#x}")
        return 0

    tgt = next((p for p in procs if p["name"] == sys.argv[2]), None)
    if tgt is None:
        print(f"no procedure {sys.argv[2]!r}; procs: {', '.join(p['name'] for p in procs)}")
        return 1
    for off, t in decode(data, *proc_range(tgt)):
        print(f"  {off:#06x}: {t}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
