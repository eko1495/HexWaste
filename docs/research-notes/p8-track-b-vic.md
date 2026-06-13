# P8 Track B — Vic's rescue vertical slice

## Q1: vault13.gam GVAR foundation
- Format: text, `GVAR_NAME :=value; // (index)` lines under section header `GAME_GLOBAL_VARS:` (also a commented-out MAP_GLOBAL_VARS section). 696 `:=` entries (indices 0..695).
- Engine loads it at init AND reset: game.cc:266 `gameLoadGlobalVars()` in gameInitWithOptions, game.cc:412 in gameReset (new game/load). Parser = `globalVarsRead` game.cc:1044 (skips //-comments, splits at `;`, value after `:=`).
- NONZERO initials (full list, 12 of 696): idx47 GVAR_TOWN_REP_ARROYO=50, 134/135/136 SALVATORE/BISHOP/MORDINO_FAMILY_COUNTER=100, 144 GVAR_VAULT_MONSTER_COUNT=10, 216 WRIGHT_FAMILY_COUNTER=100, 257/258 NEW_RENO_CARLSON/WESTIN_PRICE=-1, 461 GVAR_TOTAL_WANAMINGOS=20, 464 GVAR_FRED_MONEY=200, 580 GVAR_MORTON_GANG=10, **619 GVAR_FIND_VIC=1**.
- **Breakage risk for Vic chain: GVAR_FIND_VIC (619) := 1** — our default-0 GVARs would start the "Find Vic" quest state wrong if any script tests it (verify in Q2 disassembly). All other Vic/Sulik/Metzger GVARs (19 DEN_VIC_STATUS, 29 VIC_DEVICE, 30 SLAVE_RUN, 100 QUEST_VIC_DEVICE, 235 SULIK_FREE, 287 DEN_SLAVER_WARNINGS, 452 DEN_VIC_KNOWN, 457 DEN_SEE_VIC) start at 0 — safe.
- Loader cost: trivial (S) — parse text lines, seed Dictionary.

## Q2(a): Sulik chain (kcmaida.int / kcsulik.int) — operand-resolved
Tooling: /tmp/p8/disasm.py (linear listing, push operands + call-target proc names + static strings resolved). Maps: Maida hex 22678, Sulik hex 23478, Torr 24291 on kladwtwn.map (NOT klamall); KCMaida=scripts.lst line 80 (msg list 80), Kcsulik=line 383.

### Maida payment path (kcmaida.int)
Dialog walk: Node004 (greeting; reply 230/231 picked by GVAR48 TOWN_REP_KLAMATH<5) → opt235 Node005 (questions hub; "about Sulik" opt243 shown only when GVAR235 SULIK_FREE==0) → Node013→014→015→016→017→018 (msg 370: "His bill comes to $350") → opt371 Node945 / opt372 Node955 (reaction-wrapper procs, LVAR0/LVAR3 reaction math) → both call Node019.
- Node019 (0x5f92): gsay_reply(80,380); giq_option(4,80,381,proc16,50) → **barter_for_sulik**; option 382 → Node999 exit.
- barter_for_sulik (0x2da0): `if item_caps_total(dude) > 349 { item_caps_adjust(dude,-350); call node020 } else call Node033`.
- node020 (0x5ffa): LVAR11=1, **set_global_var(235,1)** (GVAR_SULIK_FREE), **GVAR48 += 10** (TOWN_REP_KLAMATH), reply 390/391 (391 if program-global 27 i.e. Torr-rescued flag). NO XP here.
- talk_p_proc gating: hostile if `GVAR68 ENEMY_KLAMATH==1 || LVAR6` → Node998 (attack). Reaction boilerplate touches GVAR0 (karma), GVAR37–45 (reaction flags), GVAR48; uses has_trait(dude,13=Sex Appeal?) + get_critter_stat — all REAL on our host.
- Pre/post dialog: `move_obj_inven_to_obj(fetch_external("klam_bucknr_box_obj"), self)` and back — shop-box choreography via cross-script ExternalVariables (we have these).

### Sulik side (kcsulik.int)
talk_p_proc dispatch (0x3b0a..): hostile if `metarule(46 CURRENT_TOWN,0)==2 && GVAR68==1 || LVAR6`; LVAR16>0 → float; first-talk (LVAR4==0) gsay: IQ<4 → Node001 dumb, GVAR11 REPUTATION_SLAVER==1 → Node002, **GVAR235==0 → Node054 (still-in-debt branch)**, else Node008. Later: `party_member_obj(0x01000061)!=0 || LVAR11` → Node1000 (in-party hub). GVAR1 CHILDKILLER>=2→Node074; GVAR0<-100→Node70a; GVAR182/184 (Torr quest state) → Node076/077.
- **Pay Sulik directly: Node915** (0x5664): `caps>=350` → reaction+50, item_caps_adjust(-350), GVAR48+=10, `if GVAR235==0 → give_exp_points(500)` + display "+500 xp" (message_str list 14), set GVAR235=1 → Node043; else Node042 (no cash). THE 500 XP for "Free Sulik" lives ONLY here (paying Maida gives none).
- Join: Node1100 (0xb036) / Node1000 family — join code (0x8e66/0xb5e8): critter_add_trait(self,1,6,0) [team→player], **party_add(self)**, add_timer_event(self,1*game_ticks? (1s),1); option binding giq_option(...,proc 103=Node1100,...).

### What works on our host TODAY (headless transcripts, kladwtwn.map, --give 41:2000 --talk-hex 22678)
- talk_p_proc runs clean; Node004 greeting + 5 options print; reaction boilerplate executes (stubs hit: debug_msg, set_light_level, obj_is_carrying_obj — all benign here).
- **BLOCKER: choosing ANY option ends the conversation.** Cause: kcmaida talk_p_proc reaches `_op_gsay_end; end_gdialog` at TALK time (engine: gsay_end is the blocking dialog loop; end_gdialog runs only after the loop exits). Our DialogSessionEnd (ScriptHost.cs:1001) sets sticky SessionEnded; DialogSession.Choose (ScriptHost.cs:654) then kills the session after round 1 because ResetDialogRound (ScriptHost.cs:976) never clears it. Verified: `--choose 1` → no second REPLY; `--choose 1,2` → CHOOSE:2 never printed. Fix size XS: clear SessionEnded in ResetDialogRound (or ignore end_gdialog during the initial talk run). Everything downstream (caps>349 test, item_caps_adjust, set_global_var 235) is already-real once rounds continue.

## Q2(b): Vic chain (dcVic / dcMetzge)

The Metzger/Vic cash route is the **shortest legitimate Vic rescue** and runs almost entirely on machinery we already have. Two cooperating scripts in the same session GVAR dict:

### Radio gating (dcVic — NOT the rescue gate, but a dialog gate on the way)
The radio is handed over **through the dialog tree, not `use_obj_on_p_proc`** (that handler `[0x2f64..0x3092]` is a "don't point weapons at me" bark gated on `item_subtype==2` + weapon PIDs {47,48,49,40,144,53,71,273}).

- Two radio PIDs: **PID 266** (Vic's Klamath-shack radio) and **PID 100** (generic Radio).
- `Node004 [0x3514..0x35f8]` enables "Do you mean this radio?" (msg 174) when `obj_is_carrying_obj(dude,266)>0` (0x352c) OR `obj_carrying_pid_obj(dude,100)!=0` (0x356e).
- `Node005 [0x35f8..0x36ae]` give-radio: `obj_carrying_pid_obj(dude,266)` (0x3608) → `rm_obj_from_inven(dude,radio)` (0x362c) → **`GVAR446 DEN_FLAG_2 |= 0x400000`** (0x362e-0x3644) = "radio delivered", reply msg 177.
- That GVAR446 bit routes `talk_p_proc` 0x1c3a → `Node002 [0x3244]` (msg 163 "...even though we fixed his radio. He's threatening to sell me off") which sets GVAR100=1.

**Host verdict:** needs **new externals** `obj_carrying_pid_obj`, `obj_is_carrying_obj`, `rm_obj_from_inven` (HOST has `move_obj_inven_to_obj` but NOT these). Without them the radio sub-quest stubs out — but it is *flavor on the path*, not the rescue gate.

### The $1000 Metzger purchase (dcMetzge — the actual rescue)
Flow: `talk_p_proc` (line 1018) → Node002 → Node005 job hub → Vic sub-tree Node020 → Node023 → **Node024 [0x46fa] price node** → **Node025 [0x489a] payment node**.

- Price = `1000 / (1 + ((GVAR446 & 0x20000)!=0))` → **GVAR29** (0x46fc-0x472a). Bit 0x20000 = slaver-jacket/guild discount → 500; **never written in dcmetzge** (set by another script). Fresh player default GVAR446=0 → full **$1000**.
- Affordability gate (0x47de-0x4812): buy option (msg 498, proc-index 39 → Node025) offered ONLY if `item_caps_total(dude) >= GVAR29` (0x4806/0x4810).
- **Node025 payment/freed** (msg 510 "Okay, he's yours"): `item_caps_adjust(dude,-GVAR29)` (0x489c), `set_global_var(457,2)` → **GVAR457 DEN_SEE_VIC := 2** (the bought marker, 0x48ac), `GVAR100 := 2` if <2 (0x48ba), and **`GVAR445 DEN_FLAG_1 |= 0x8000000`** (0x48e0) = "already paid for Vic" (locks the re-offer).

**Premise correction:** dcmetzge does NOT touch GVAR19 DEN_VIC_STATUS — every `push_int 19` there is a proc-index/reaction node, not the GVAR. DEN_VIC_STATUS is owned by dcvic (read at 6 sites: 0x211a, 0x33a2, 0x33c2, 0x34ac, 0x44ca, 0x4674). Metzger sets DEN_SEE_VIC(457)+DEN_FLAG_1 bit; Vic flips DEN_VIC_STATUS himself.

### The GVAR that frees Vic
**GVAR445 GVAR_DEN_FLAG_1 bit 0x8000000 (134217728) = "Vic is free".** Set by dcMetzge Node025 (cash) or dcMetzge PROC 0x1d04 (other release paths: `GVAR100:=2` then `GVAR445|=134217728` at 0x1d0c-0x1d22). **NOT** GVAR19/29/100/619.
dcVic reads `GVAR445 & 0x8000000` as the join gate at `critter_p_proc` 0x17f2 (L712), `talk_p_proc` 0x1ada (L919), `Node995` 0x87b4 (L8028): if free → Node010 join offer (msg 192); else → Node007 still-captive brush-off.

### The party_add JOIN node
Path: `Node008 [0x3740]` (msg 183) → "Come with me" (msg 186) gated `(LVAR4 & 8192)==0` at 0x378e (not already joined) → proc index 59 = **Node995** → gate `GVAR445 & 0x8000000` → `Node010 [0x39b0]` (msg 192) → msg 193/194 bound to proc index 60 = **Node994**, after a **party-size gate** 0x39b8-0x3a1e: `metarule(16 PARTY_COUNT)-1 >= floor(dude.CHA/2)+has_trait(98)` OR `partyCount-1 >= 5` → diverts to msg 835 "party full".
`Node994 [0x8818..0x8914]` join code: `LVAR4 |= 8192` (0x881a), `critter_add_trait(self,1,6,0)` team 0 (0x88ba), **`party_add(self)`** (0x88d2), `call Node110` greeting (0x88e4). **No add_timer / follow-timer** — following is 100% `critter_p_proc` (0x14ac-0x15e2, gated `party_member_obj && LVAR5!=0 && GVAR398 PARTY_NO_FOLLOW==0`), matching p7-m4.
Per-map re-bind in `map_enter_p_proc [0x210a]`: `critter_add_trait(self,1,6,25)` (0x21c8) resets team to 25, gated `metarule(22 IS_LOADGAME)==0` + `metarule(14 TESTFIRSTRUN)`; schedules cosmetic fidget via `add_timer_event` (0x2238).

### GVAR619 FIND_VIC — does vault13.gam's init=1 change dcVic?
**No.** GVAR619 appears once in dcVic: `talk_p_proc` 0x19d6 `set_global_var(619,2)` — **write-only, never read** in dcVic. Session-default 0 (vs vault13.gam's 1) has zero effect on any dcVic branch; it only matters to OTHER scripts (V13/worldmap/Elder "found Vic" gates). vault13.gam preload is a separate phase-8 task, not required for the rescue.

### Which gates work on our host vs need new code
| Gate | Status |
|------|--------|
| Metzger cash price `1000/(1+(GVAR446&0x20000))` | **works** — GVAR dict defaults to full $1000 |
| `item_caps_total >= price`, `item_caps_adjust(-price)` | **works** — IntVm.cs:1284/1287, bag aliased to dude.Inventory (p6) |
| `set_global_var(457/100/445)` freed-markers | **works** — session GVAR dict |
| gsay tree Node002→005→020→023→024→025 + proc-index routing | **works** |
| ST<=5 / gender charm half-off sub-branch | **works** — get_critter_stat real (stat 34 gender, stat 3 ST) |
| dcVic reads GVAR445 bit → join offer | **works** — cross-script via shared session dict |
| `party_add` / `critter_add_trait` team set / follow loop | **works** — all real (p6/p7) |
| **`metarule(16) PARTY_COUNT`** party-size gate | **broken-cosmetic** — host returns 0 (ScriptHost.cs:797, IntVm.cs:111); join still offered, but "party full" refusal never fires |
| **`obj_carrying_pid_obj` / `obj_is_carrying_obj` / `rm_obj_from_inven`** (radio give Node004/005) | **NEW externals** — radio sub-quest + GVAR446 0x400000 stub out without them |
| `obj_being_used_with` / `item_subtype` (weapon bark) | stub to 0 — cosmetic |
| GVAR445 0x8000000 free bit set by Metzger | **must verify** our dcMetzge slice writes it (it does, Node025 0x48e0) |

**Bottom line:** the **cash purchase path needs ZERO new externals**; the **join needs only metarule rule 16** to refuse-when-full correctly (and even default-0 still presents the join). The **radio flavor branch** is the only part needing 3 new inventory-query externals, and it is optional to the rescue. No nonzero GVAR initials are required (all Den flags init :=0 correctly; only Metzger sets the free bit). Buying Vic awards **no give_exp_points in dcmetzge**; rescue XP/quest credit is Vic-side / V13-worldmap (out of this slice).

## Q3: Ranked break-list

Minimal set to walk the WHOLE chain (Maida/Sulik $350 **OR** Metzger/Vic $1000) legitimately. Ranked by leverage. Items 1-2 unblock the most for the least effort.

| # | Item | Effort | Unblocks |
|---|------|--------|----------|
| 1 | **Dialog-end XS fix from Q2a — DO NOT DO IT.** Reader VERIFIED the "SessionEnded sticky-after-talk" blocker is **NOT real**: `SessionEnded` is set ONLY by `end_dialogue` (0x80DF→DialogSessionEnd, ScriptHost.cs:1001), NOT by `gsay_end` (0x811D→DialogEnd, IntVm.cs:1060, which never touches it). Multi-round real-game dialog tests pass 2/2 (`DialogRealGameDataTests`). The two opcodes are correctly separated exactly as fallout2-ce (`interpreter_extra.cc` _op_gsay_end:3763 presents; opEndGameDialog:1948 ends). | **XS / 0 LOC** | Nothing to fix. **If kcmaida/kcsulik misbehave in-app, the cause is a missing external or an unanswered `metarule` rule (capture via OnStubbedExternal), NOT dialog round logic.** This re-points the entire Q2a investigation. |
| 2 | **vault13.gam GVAR loader (Q1)** — parse the 696-global init table, seed the session GVAR dict at new-game (GVAR619 FIND_VIC:=1, etc.). | **S** | Correct global initial state for ALL scripts. Irrelevant to *this* Vic slice (every Den flag inits :=0; GVAR619 is write-only in dcVic) but foundational for V13/worldmap "found Vic" gates and any other GVAR-read script. Highest leverage *outside* this slice. |
| 3 | **`metarule` rule 16 METARULE_PARTY_COUNT** — return live `party_add` member count (currently 0; ScriptHost.cs:797, IntVm.cs:111). Port `_getPartyMemberCount` (interpreter_extra.cc:3219). | **S** | Vic Node010 0x39b8 + Node1100 + dcMetzge Node003 party-size gates: correct refuse-when-full + correct greeting plural + companion-sell sub-branch. NOT strictly blocking the join (default-0 still offers it), but required to match engine and to refuse a full party. |
| 4 | **Radio inventory-query externals: `obj_carrying_pid_obj(critter,pid)`, `obj_is_carrying_obj(critter,obj)`, `rm_obj_from_inven(critter,obj)`** — for dcVic Node004 0x352c/0x356e + Node005 0x3608/0x362c (PID 266 / PID 100). | **S-M** | The radio repair sub-quest (GVAR446 0x400000) and its gated dialog branches (msg 163/174/177). **Optional to the rescue** — the cash purchase frees Vic without it — but needed to walk the radio flavor leg and likely reused by other inventory-gated scripts. |
| 5 | **Verify dcMetzge slice writes GVAR445 |= 0x8000000** (Node025 0x48e0) and that dcVic runs map_enter on Vic's map in the same session dict. | **XS** | The cross-script free-Vic handshake. No new code if both scripts share the session GVAR dict (they do); pure verification. |
| 6 | **`obj_being_used_with` / `item_subtype`** for dcVic use_obj_on_p_proc weapon bark — **stub to 0**. | **XS** | Cosmetic "don't point that at me" bark only. Safe to leave stubbed. |

**Note on the $350 Maida/Sulik route:** per Q2a it shares the same dialog plumbing and `party_add` join; its confirmed blocker was the (now-debunked) SessionEnded issue. With item #1 reclassified, the Sulik route's real remaining risk is the same as Vic's: a missing external or unanswered metarule surfaced via OnStubbedExternal. Both routes converge on items #3 (party count) as the only genuinely-needed join fix.

## Q4: Companion management minimum

**Architecture:** follow is 100% script-side in each critter's `map_update_p_proc`/heartbeat (we already drive it via `critter_p_proc`, p7-m4). Follow distance is a per-script LVAR (**Sulik LVAR[12], Vic LVAR[6]**, default 6). Loop gated on `LVAR[11]==0` (Sulik wait flag) AND `GVAR[398]==0` (global stop-follow) AND `party_member_obj!=0`; threshold `tile_distance_objs > 3*LVAR/2` then `rotation_to_tile`+`tile_num_in_direction`+`opAnimateMoveObjectToTile`. Stack order confirmed vs engine: `opSetLocalVar` pops value-then-index (interpreter_extra.cc:1166), `opGetLocalVar` pops index only (:1150).

- **WAIT / FOLLOW toggle:** no separate "wait" boolean — the FOLLOW-OPTIONS menu (Sulik Node1007 @0xa746; Vic ends @0x508c) just writes the distance LVAR to different values; "wait here" = script ceases chasing because `GVAR[398]`/`LVAR[11]` is set. Our heartbeat **must consult GVAR398 + the per-script wait LVAR** (both default 0 = follow). Menu strings load via `message_str(file=14, id=10004..10013)` = the shared **partymbr.msg-class** file, NOT kcsulik.msg.
- **DISMISS / leave:** real dialog node. Vic Node1002 @0x50ac: `set_local_var(5, game_time())` → `party_remove(self)` (0x50ba) → `critter_add_trait(self,1,6,25)` team 25 → `LVAR[4] |= 8192`. Sulik same (@0x9958 etc.) plus involuntary leave in damage_p_proc @0x4734 (`LVAR[13]=-1`).
- **REJOIN:** join node reused (Sulik Node800 @0x8ddc): checks `get_critter_state & 1 == 0` (alive), resets `LVAR[12]=6`, `LVAR[11]=0`, `critter_add_trait(self,1,6,0)`, **`party_add(self)`** (0x8e80), `add_timer_event(...,+1)`.
- **TRADE-with-companion:** **engine-side, NOT a script gdialog_barter** — neither kcsulik nor dcvic contains any barter opcode. Engine opens `partyMemberControlWindow` (FID 390) on talk-to-member; TRADE button ('d') routes to `inventoryOpenTrade(...gGameDialogBarterModifier)` with **modifier 0 (flat 1:1)** for companions (game_dialog.cc:1904, modifier defaults 0 @227/726). **Our priced barter box would NOT work** — it trades against a shop stock box with a `cost×2×(mod+100)/100×…` formula. The right primitive is a **loot/swap panel pointed at the follower's own Inventory** (like our corpse-loot panel), 1:1, reusing `move_obj_inven_to_obj` (already real). `_gdCanBarter` requires the proto CRITTER_BARTER flag — **our panel should bypass that check.**
- **Level proto swaps (partyMemberIncLevels):** engine-side, needs party.txt (level_minimum/level_up_every/level_pids), not referenced by these scripts. **Skip — cosmetic/balance.**

**Two highest-friction-removal items:**

| Pick | Item | Size | Why |
|------|------|------|-----|
| **A** | **DISMISS / REJOIN dialog nodes** | **S** | `party_add`/`party_remove`/`party_member_obj` already real (ScriptHost.cs:936-952); dialog trees already run; `set_local_var`/`critter_add_trait`/`GameTime()` (ScriptHost:799) all real. ONLY new work is the wait/distance LVARs round-tripping through our LVAR persistence (already handled by p5 LVAR slices) and honoring GVAR398/LVAR[11] in the follow loop. Unlocks the whole companion lifecycle with near-zero new engine code. |
| **B** | **TRADE-with-companion as a follower-Inventory swap panel** | **M** | Reuse the loot panel + `move_obj_inven_to_obj`, pointed at the follower's own Inventory with flat 1:1 moves — NOT the priced barter box. New work: a TRADE entry point on the in-party gsay hub (or a key) + a two-pane move panel; bypass `_gdCanBarter`/CRITTER_BARTER. USE-BEST-WEAPON/ARMOR ('w'/'a') is optional polish (we equip via item flags 0x1/0x2, p6-m4). |

No quest XP/karma in this slice (team kills already pay XP, p7-m4, unaffected). Soft blocker for the follow-options menu text: resolve **message_str file id 14** (partymbr.msg-class) or option strings render blank.

## Q5: Verdict

**Yes — "Vic's rescue legitimately + 2 companion-management items" is a coherent, well-scoped M0..M5 phase**, and it is the recommended Track B direction. It is unusually low-risk because the heavy machinery (dialog tree + proc-index routing, `party_add`/`party_remove`/`party_member_obj`, `critter_add_trait` team set, `item_caps_total`/`item_caps_adjust`, `get_critter_stat`, script-side follow via `critter_p_proc`, GVAR/LVAR session persistence, give_exp_points) **already shipped in phases 5-7**. The cash Vic path needs **zero** new externals; the only genuinely-new engine work is small and well-bounded.

**What it pulls in (and the opcodes involved):**
- **metarule rules** — `metarule(16) METARULE_PARTY_COUNT` (the one real join/refuse gate; port `_getPartyMemberCount` interpreter_extra.cc:3219). Rules 14 (TESTFIRSTRUN) and 22 (IS_LOADGAME) already answered; this adds 16.
- **Inventory-query externals** — `obj_carrying_pid_obj`, `obj_is_carrying_obj`, `rm_obj_from_inven` (radio leg only; optional to the rescue). These are the closest this phase gets to "**steal**"-class opcodes, but they are read/remove-self, NOT `steal_*`/skill-contested theft — no `do_check`-on-target, no `is_success`/`is_critical` rolls beyond what we already have.
- **Skill checks:** the cash path uses NONE. The Vic join party-size formula uses `get_critter_stat` (CHA, already real) + `has_trait` (already real), not a skill roll. No `roll_vs_skill`/`do_check`/`skill_contest` is introduced by this slice. (The Sulik $350 route is likewise stat/caps-gated, not skill-gated.)
- **Reputation / karma metarules:** NONE are touched by the cash Vic buy or the companion slice — `give_exp_points` is real and fires only on the slave-run/gangwar branches we are deliberately skipping (200/200/1000/1100/500 at the dcmetzge addrs in the reader notes), which also rewrite GVAR0 PLAYER_REPUTATION + reputation-badge GVARs 37-45. By choosing the cash path we **avoid** reputation mechanics entirely. (REPUTATION_SLAVER GVAR11 is not a gate on buying Vic.)
- **Companion lifecycle opcodes:** `party_add`, `party_remove`, `critter_add_trait` (TRAIT_OBJECT/OBJECT_TEAM), `set_local_var`/`get_local_var`, `game_time`, `get_critter_state`, `add_timer_event` — all already real.

**Honest M0..M5 sketch (recommended):**
- **M0 — Audit & rule 16 (S).** Implement `metarule(16) PARTY_COUNT` = live party member count. Hook OnStubbedExternal logging so kcmaida/kcsulik/dcvic/dcmetzge surface any *other* missing external/metarule at runtime (this is the real Q2a follow-up, replacing the debunked SessionEnded fix). **Do NOT touch ResetDialogRound/Choose.** Verify cross-script GVAR445 handshake.
- **M1 — Metzger $1000 cash purchase (S).** Walk dcMetzge Node002→005→020→023→024→025 end-to-end: caps gate, `item_caps_adjust(-1000)`, set GVAR457=2/GVAR100=2/GVAR445|=0x8000000. No new externals. This is the rescue's spine.
- **M2 — Vic join + per-map follow (S).** dcVic Node008→995→010→994: gate on GVAR445 bit, `party_add`, team-0, re-bind per map_enter (team 25), follow via existing `critter_p_proc`. Honor GVAR398/wait-LVAR in the loop.
- **M3 — Radio sub-quest leg (S-M).** Add `obj_carrying_pid_obj`/`obj_is_carrying_obj`/`rm_obj_from_inven`; walk dcVic Node004/005 (PID 266/100), set GVAR446 0x400000. Makes the radio dialog branches and msg 163/174/177 real. (Could also light up the Sulik $350 route if those scripts share these externals.)
- **M4 — Companion management Pick A: DISMISS / REJOIN (S).** Vic Node1002 / Sulik Node800 dialog nodes; wait/distance LVAR round-trip; resolve message_str file id 14 for follow-option text; honor GVAR398/LVAR[11] wait.
- **M5 — Companion management Pick B: follower-Inventory TRADE panel (M).** Reuse loot panel + `move_obj_inven_to_obj`, 1:1, follower's own Inventory, bypass CRITTER_BARTER; add a TRADE entry on the in-party gsay hub. Optional: USE-BEST-WEAPON/ARMOR polish.

**Deliberately out of scope (named so they aren't accidentally pulled in):** the slave-run alternative (needs a worldmap slave-roundup encounter map + GVAR31 SLAVES_COUNT we don't have), reputation/karma badge recompute (GVAR0/37-45), `partyMemberIncLevels` proto swaps (needs party.txt), the slaver-jacket 0x20000 discount bit (set by another script — we always charge full $1000), and vault13.gam GVAR preload (Q3 #2 — foundational but a separate task; not needed for this slice since every Den flag inits :=0).
