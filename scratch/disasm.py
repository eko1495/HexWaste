#!/usr/bin/env python3
"""Linear disassembler for Fallout2 .int, focused on dialogue quest tracing.
Prints, per procedure, the sequence of pushes + external/core ops, resolving:
  - push int/float/string
  - fetch_procedure_address (annotated with proc index -> name from the just-pushed int)
  - set_global_var / giq_option / gsay_option / gsay_reply / gsay_message args
"""
import struct, sys, os
SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                   "reference", "fallout2-ce", "src")
import re
def load_opcode_names():
    names = {}
    text = open(f"{SRC}/interpreter.h").read()
    for m in re.finditer(r"OPCODE_(\w+) = (0x[0-9A-Fa-f]+)", text):
        names[int(m.group(2),16)] = m.group(1).lower()
    for f in ("interpreter_extra.cc","interpreter_lib.cc","scripts.cc"):
        try: text=open(f"{SRC}/{f}").read()
        except OSError: continue
        for m in re.finditer(r"interpreterRegisterOpcode\((0x[0-9A-Fa-f]+),\s*(\w+)\);(?:\s*//\s*(op_\w+))?",text):
            num=int(m.group(1),16); ssl=m.group(3)[3:] if m.group(3) else m.group(2)
            names.setdefault(num,ssl)
    return names
OPN=load_opcode_names()
def u32(b,o): return struct.unpack_from(">i",b,o)[0]
def u16(b,o): return struct.unpack_from(">H",b,o)[0]

def parse(path):
    data=open(path,"rb").read()
    pb=42; count=u32(data,pb); procs=[]
    ident_base=pb+4+24*count
    for i in range(count):
        o=pb+4+24*i
        name_off,flags,time,cond_off,body_off,argc=struct.unpack_from(">6i",data,o)
        end=data.index(b"\0",ident_base+name_off)
        name=data[ident_base+name_off:end].decode("ascii","replace")
        procs.append(dict(idx=i,name=name,flags=flags,body=body_off,argc=argc))
    ident_size=u32(data,ident_base); term1=ident_base+4+ident_size
    sstr_base=term1+4; sstr_size=u32(data,sstr_base)
    # build static string table: offset(rel to sstr_base+4) -> string
    sstr={}
    if sstr_size<0:
        code_start=sstr_base+4
    else:
        blob=sstr_base+4; p=blob; endb=blob+sstr_size
        while p<endb:
            ln=u16(data,p); s=data[p+2:p+2+ln].split(b"\0")[0].decode("ascii","replace")
            sstr[p-blob]=s; p+=2+ln
        code_start=blob+sstr_size+4
    return data,procs,code_start,sstr

def opname(w):
    full=0x8000|(w&0x3FF)
    return OPN.get(full,f"op_{full:#06x}")

def disasm(path, want=None):
    data,procs,code_start,sstr=parse(path)
    by_body={p["body"]:p for p in procs}
    bodies=sorted([p for p in procs if p["body"]>0 and not p["flags"]&4],key=lambda p:p["body"])
    print(f"### {os.path.basename(path)}  {len(data)}b code@{code_start:#x} procs={len(procs)}")
    print("PROCS:", ", ".join(f"{p['idx']}:{p['name']}" for p in procs))
    for i,p in enumerate(bodies):
        end=bodies[i+1]["body"] if i+1<len(bodies) else len(data)
        if want and p["name"] not in want: continue
        print(f"\n--- proc {p['idx']} {p['name']}  [{p['body']:#x},{end:#x}) ---")
        pc=p["body"]; recent=[]  # stack of (kind,value)
        while pc+2<=end:
            w=u16(data,pc)
            if (w>>8)&0x80==0: pc+=2; continue
            op=w&0x3FF
            if op==0x001:
                typ=w; val=u32(data,pc+2); pc+=6
                if typ==0xC001: recent.append(("int",val)); disp=f"push int {val}"
                elif typ==0xA001: f=struct.unpack_from(">f",data,pc-4)[0]; recent.append(("flt",f)); disp=f"push float {f}"
                elif typ==0x9001: s=sstr.get(val,f"@{val}"); recent.append(("str",s)); disp=f'push sstr "{s}"'
                elif typ==0x9801: recent.append(("dstr",val)); disp=f"push dstr @{val}"
                else: recent.append(("?",val)); disp=f"push? {typ:#x} {val}"
                print(f"  {pc-6:#06x}: {disp}")
                continue
            pc+=2
            name=opname(w)
            note=""
            if name=="fetch_procedure_address":
                if recent and recent[-1][0]=="int":
                    pidx=recent[-1][1]
                    pn=procs[pidx]["name"] if 0<=pidx<len(procs) else "?"
                    recent[-1]=("proc",f"{pidx}:{pn}")
                    note=f"  => proc[{pidx}]={pn}"
                print(f"  {pc-2:#06x}: {name}{note}")
                continue
            INTERESTING={"set_global_var","giq_option","gsay_option","gsay_message",
                "gsay_reply","gsay_end","start_gdialog","end_dialogue","override_map_start",
                "give_exp_points","set_local_var","reg_anim_func","float_msg","add_mult_objs_to_inven"}
            if name=="giq_option":
                # pushes: iq, msgfile, msg, target(procindex), reaction
                tail=recent[-5:]
                tgt=None; msg=None; react=None
                if len(recent)>=2 and recent[-1][0]=="int":
                    react=recent[-1][1]
                    if recent[-2][0]=="int":
                        tgt=recent[-2][1]
                    elif recent[-2][0]=="proc":
                        tgt=int(recent[-2][1].split(":")[0])
                    elif recent[-2][0]=="str":
                        tgt=recent[-2][1]
                if len(recent)>=3 and recent[-3][0]=="int": msg=recent[-3][1]
                tn=procs[tgt]["name"] if (tgt is not None and 0<=tgt<len(procs)) else "?"
                raw=" | ".join(f"{k}:{v}" for k,v in tail)
                print(f"  {pc-2:#06x}: giq_option  msg={msg} -> target proc[{tgt}]={tn} react={react}   [{raw}]")
                recent=[]
                continue
            if name in INTERESTING:
                args=" | ".join(f"{k}:{v}" for k,v in recent[-6:])
                print(f"  {pc-2:#06x}: {name}   <== [{args}]")
                recent=[]
                continue
            # control flow / consumers: reset tracking to avoid stale args
            if name in ("if","while","jump","call","dup","pop","store","fetch",
                        "equal","not_equal","less_than","greater_than","less_than_equal",
                        "greater_than_equal","and","or","add","sub","mul","div","negate","not"):
                recent=[]
            print(f"  {pc-2:#06x}: {name}")
            if len(recent)>12: recent=recent[-12:]

if __name__=="__main__":
    path=sys.argv[1]
    want=set(sys.argv[2:]) if len(sys.argv)>2 else None
    disasm(path,want)
