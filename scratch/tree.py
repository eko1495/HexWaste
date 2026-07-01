#!/usr/bin/env python3
"""Per-script dialogue tree: node -> giq option targets, node -> quest-gvar writes,
and talk_p_proc -> start node calls."""
import sys, re
QG={102:"RUSTLE_BRAHMIN?102",182:"TORR_GUARD_STATUS",197:"SMILEY_STATUS",
    198:"STILL_STATUS",390:"QUEST_RAT_GOD",391:"QUEST_RESCUE_TORR",
    100:"QUEST_VIC_DEVICE",101:"QUEST_MAGGIE_STILL",103:"QUEST_RUSTLE_CATTLE",
    179:"RUSTLE_ACCEPT",180:"RUSTLE_REFUSE",181:"RUSTLE_REWARD",184:"RUSTLE_OVER",
    203:"?203"}
dis=open(sys.argv[1]).read().splitlines()
proc=None; procs={}
for ln in dis:
    m=re.match(r"--- proc \d+ (\S+)",ln)
    if m: proc=m.group(1); continue
    if proc is None: continue
    m=re.search(r"giq_option  msg=(\S+) -> target proc\[(\S+)\]=(\S+) react=(\S+)",ln)
    if m:
        procs.setdefault(proc,{"giq":[],"gvar":[],"call":[]})
        procs[proc]["giq"].append((m.group(1),m.group(3),m.group(4)))
        continue
    m=re.search(r"set_global_var   <== \[int:(\d+) \| int:(-?\d+)\]",ln)
    if m:
        g=int(m.group(1))
        if g in QG:
            procs.setdefault(proc,{"giq":[],"gvar":[],"call":[]})
            procs[proc]["gvar"].append((g,QG[g],int(m.group(2))))
        continue
for p,d in procs.items():
    if not d["giq"] and not d["gvar"]: continue
    print(f"[{p}]")
    for g,n,v in d["gvar"]:
        print(f"    SET gvar {g} ({n}) = {v}")
    for msg,tn,r in d["giq"]:
        print(f"    opt msg={msg} -> {tn} (react {r})")
