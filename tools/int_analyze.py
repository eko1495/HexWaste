#!/usr/bin/env python3
"""Fallout 2 .int bytecode analyzer.

File layout (derived from fallout2-ce src/interpreter.cc, all big-endian):
  0x00          42-byte header (programCreateByPath: procedures = data + 42)
  0x2A          int32 procedureCount
  0x2E          procedureCount * 24-byte Procedure records:
                  int32 nameOffset       (offset into identifiers block, incl. its 4-byte size field)
                  int32 flags            (1=timed 2=conditional 4=imported 8=exported 16=critical)
                  int32 time
                  int32 conditionOffset
                  int32 bodyOffset       (ABSOLUTE file offset; instructionPointer indexes file data)
                  int32 argCount
  idents        int32 byteSize, then blob of [u16 len][NUL-terminated name] entries,
                then u32 0xFFFFFFFF terminator
  staticStrings int32 byteSize (0xFFFFFFFF if none), blob in same entry format,
                then u32 0xFFFFFFFF terminator (only when blob present)
                (programGetString: staticStrings+4+offset; the fallout2-ce
                 'staticStrings' pointer aims at the identifiers terminator)
  code          immediately after, to EOF (begins with global-var init prologue:
                0x802C set_global ...)

Instruction encoding (from _interpret/_getOp/opPush):
  - Each instruction is a u16 BE word; valid iff (word >> 8) & 0x80 (high byte has 0x80 bit).
  - Handler dispatch uses (word & 0x3FF).
  - ONLY opcode index 0x001 (OPCODE_PUSH) carries an inline operand: 4 bytes following the word.
    The push word's upper bits encode the value type:
      0xC001 int, 0xA001 float, 0x9001 static-string offset, 0x9801 dynamic-string offset.
  - Every other opcode is a bare 16-bit word; a linear scan is therefore exact.
"""
import re
import struct
import sys
from collections import Counter

import os
SRC = os.environ.get(
    "FALLOUT2_CE_SRC",
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "reference", "fallout2-ce", "src"))

# ---------------------------------------------------------------- opcode names
def load_opcode_names():
    names = {}
    # Core opcodes from interpreter.h enum
    text = open(f"{SRC}/interpreter.h").read()
    for m in re.finditer(r"OPCODE_(\w+) = (0x[0-9A-Fa-f]+)", text):
        names[int(m.group(2), 16)] = ("core", m.group(1).lower())
    # External registrations (interpreter_extra.cc has '// op_xxx' SSL names)
    for f in ("interpreter_extra.cc", "interpreter_lib.cc", "scripts.cc",
              "sfall_opcodes.cc"):
        try:
            text = open(f"{SRC}/{f}").read()
        except OSError:
            continue
        for m in re.finditer(
                r"interpreterRegisterOpcode\((0x[0-9A-Fa-f]+),\s*(\w+)\);(?:\s*//\s*(op_\w+))?",
                text):
            num = int(m.group(1), 16)
            ssl = m.group(3)[3:] if m.group(3) else m.group(2)
            if num not in names:
                names[num] = ("ext", ssl)
    return names

OPNAMES = load_opcode_names()
PUSH_TYPES = {0xC001: "int", 0xA001: "float", 0x9001: "sstr", 0x9801: "dstr"}

# ---------------------------------------------------------------- int parsing
def u32(b, o): return struct.unpack_from(">i", b, o)[0]
def u16(b, o): return struct.unpack_from(">H", b, o)[0]

def parse(path):
    data = open(path, "rb").read()
    proc_base = 42
    count = u32(data, proc_base)
    procs = []
    ident_base = proc_base + 4 + 24 * count
    for i in range(count):
        o = proc_base + 4 + 24 * i
        name_off, flags, time, cond_off, body_off, argc = struct.unpack_from(">6i", data, o)
        # identifier name: nameOffset is relative to identifiers block start
        end = data.index(b"\0", ident_base + name_off)
        name = data[ident_base + name_off:end].decode("ascii", "replace")
        procs.append(dict(idx=i, name=name, flags=flags, body=body_off,
                          cond=cond_off, argc=argc))
    ident_size = u32(data, ident_base)
    term1 = ident_base + 4 + ident_size
    assert u32(data, term1) == -1, "identifier table terminator missing"
    sstr_base = term1 + 4
    sstr_size = u32(data, sstr_base)
    if sstr_size < 0:           # 0xFFFFFFFF == no static strings
        code_start = sstr_base + 4
    else:
        code_start = sstr_base + 4 + sstr_size + 4  # blob + 0xFFFFFFFF terminator
        assert u32(data, code_start - 4) == -1, "string table terminator missing"
    return data, procs, code_start

def scan(data, start, end):
    """Linear-scan disassemble [start, end); returns Counter of opcode words
    (push words normalized to their typed form) and a list of problems."""
    ops = Counter()
    problems = []
    pc = start
    while pc + 2 <= end:
        w = u16(data, pc)
        if (w >> 8) & 0x80 == 0:
            problems.append(f"bad opcode word {w:#06x} @ {pc:#x}")
            pc += 2
            continue
        pc += 2
        if (w & 0x3FF) == 0x001:        # OPCODE_PUSH: 4-byte inline operand
            ops[w] += 1                 # keep typed word (0xC001/0xA001/0x9001/0x9801)
            pc += 4
        else:
            ops[0x8000 | (w & 0x3FF)] += 1
    return ops, problems

def opname(w):
    if (w & 0x3FF) == 1:
        return f"push_{PUSH_TYPES.get(w, hex(w))}"
    n = 0x8000 | (w & 0x3FF)
    kind, name = OPNAMES.get(n, ("?", f"UNKNOWN_{n:#06x}"))
    return name

def is_core(w):
    return 0x8000 <= (0x8000 | (w & 0x3FF)) <= 0x804B

# ---------------------------------------------------------------- analysis
TARGET_PROCS = {"look_at_p_proc", "description_p_proc", "use_p_proc",
                "map_enter_p_proc"}

def analyze(path, targets=TARGET_PROCS, verbose=True):
    data, procs, code_start = parse(path)
    whole, problems = scan(data, code_start, len(data))
    # per-procedure ranges: sort by body offset; range = [body, next body)
    bodies = sorted([p for p in procs if p["body"] > 0 and not p["flags"] & 4],
                    key=lambda p: p["body"])
    per_proc = {}
    for i, p in enumerate(bodies):
        end = bodies[i + 1]["body"] if i + 1 < len(bodies) else len(data)
        per_proc[p["name"]], _ = scan(data, p["body"], end)
    if verbose:
        print(f"\n=== {path}  ({len(data)} bytes, code @ {code_start:#x}, "
              f"{len(procs)} procs) ===")
        if problems:
            print(f"  scan problems: {problems[:5]}{'...' if len(problems)>5 else ''}")
        print("  procedures:", ", ".join(p["name"] for p in procs))
        core = sorted({0x8000 | (w & 0x3FF) for w in whole if is_core(w)})
        ext = sorted({0x8000 | (w & 0x3FF) for w in whole if not is_core(w)})
        print(f"  WHOLE SCRIPT: {len(core)} distinct core, {len(ext)} distinct external")
        print("   core:", " ".join(f"{o:#06x}:{opname(o)}" for o in core))
        print("   ext :", " ".join(f"{o:#06x}:{opname(o)}" for o in ext))
        for t in sorted(targets):
            if t in per_proc:
                c = per_proc[t]
                tc = sorted({0x8000 | (w & 0x3FF) for w in c if is_core(w)})
                te = sorted({0x8000 | (w & 0x3FF) for w in c if not is_core(w)})
                print(f"  [{t}] {sum(c.values())} instrs | core({len(tc)}): "
                      + " ".join(opname(o) for o in tc))
                print(f"      ext({len(te)}): "
                      + " ".join(f"{opname(o)}" for o in te))
    return whole, per_proc

if __name__ == "__main__":
    files = sys.argv[1:] or ["/tmp/artemple.int", "/tmp/miDoor.int",
                             "/tmp/gsrdoor.int", "/tmp/diMomBox.int",
                             "/tmp/sishelf1.int", "/tmp/DenBus1.int"]
    union_whole = Counter()
    union_targets = Counter()
    target_hits = {}
    for f in files:
        whole, per_proc = analyze(f)
        union_whole.update(whole)
        for t in TARGET_PROCS:
            if t in per_proc:
                union_targets.update(per_proc[t])
                for w in per_proc[t]:
                    target_hits.setdefault(opname(w), set()).add(
                        f.rsplit('/', 1)[-1] + ":" + t)

    print("\n" + "=" * 72)
    core = sorted({0x8000 | (w & 0x3FF) for w in union_whole if is_core(w)})
    ext = sorted({0x8000 | (w & 0x3FF) for w in union_whole if not is_core(w)})
    print(f"UNION across {len(files)} scripts (whole files): "
          f"{len(core)} core + {len(ext)} external distinct opcodes")
    print(" core:", " ".join(f"{o:#06x}:{opname(o)}" for o in core))
    print(" ext :")
    for o in ext:
        print(f"   {o:#06x} {opname(o)}")
    print("\nTop 15 externals by frequency (whole files):")
    extfreq = Counter()
    for w, n in union_whole.items():
        if not is_core(w):
            extfreq[0x8000 | (w & 0x3FF)] += n
    for o, n in extfreq.most_common(15):
        print(f"   {n:5d}  {o:#06x} {opname(o)}")

    tcore = sorted({0x8000 | (w & 0x3FF) for w in union_targets if is_core(w)})
    text_ = sorted({0x8000 | (w & 0x3FF) for w in union_targets if not is_core(w)})
    print(f"\nUNION restricted to target procs "
          f"(look_at/description/use/map_enter): {len(tcore)} core + {len(text_)} ext")
    print(" core:", " ".join(f"{opname(o)}" for o in tcore))
    print(" ext :")
    for o in text_:
        print(f"   {o:#06x} {opname(o)}  <- {sorted(target_hits.get(opname(o), []))}")
    print("\nPush-type usage:", {PUSH_TYPES[w]: n for w, n in union_whole.items()
                                 if (w & 0x3FF) == 1})
