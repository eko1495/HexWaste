# P10 Track E — Companion-lifecycle fold-in (M4–M5, cheap + reusable only)

Scope: the *small, broadly-useful* companion pieces that ride alongside the
random-encounter spine — **NOT a quest VM, NOT Vic's full radio rescue**. Three
deliverables: (1) `metarule(16)` PARTY_COUNT, (2) dismiss/rejoin dialog nodes +
a follow-loop audit, (3) a flat 1:1 companion-inventory trade panel. Every engine
claim cites `reference/fallout2-ce/src/<file>.cc:LINE`; every script claim cites
an operand offset from the disassembled `.int` (tools/int_analyze.py +
/tmp/p8/disasm.py operand-resolved linear listing). Scripts disassembled:
`scripts\dcVic.int` (35164 B, 62 procs) and `scripts\kcsulik.int` (47806 B, 106
procs). **`scripts\scsulik.int` does NOT exist in the DAT** — the recruitable
Klamath Sulik is **kcsulik.int** (the prompt's "scsulik" is a typo; verified by
DatDump: "scripts\scsulik.int not found in any mounted base").

---

## (1) metarule(16) PARTY_COUNT — port `_getPartyMemberCount` (S, ~12 LoC)

### The engine function (the whole thing)
`_getPartyMemberCount` (party_member.cc:900-913):
```c
int count = gPartyMembersLength;                       // includes slot 0 = gDude
for (int index = 1; index < gPartyMembersLength; index++) {
    Object* object = gPartyMembers[index].object;
    if (PID_TYPE(object->pid) != OBJ_TYPE_CRITTER
        || critterIsDead(object)
        || (object->flags & OBJECT_HIDDEN) != 0) {
        count--;
    }
}
return count;
```
- **Slot 0 is the dude**: `gPartyMembers->object = gDude` (party_member.cc:725).
  So the returned count is **`1 + (live, visible, critter recruited members)`**.
- The loop starts at index 1 (skips the dude) and decrements for each recruited
  member that is non-critter / dead / hidden.
- Dispatch: `case METARULE_PARTY_COUNT: result = _getPartyMemberCount();`
  (interpreter_extra.cc:3219-3221); `METARULE_PARTY_COUNT = 16`
  (interpreter_extra.cc:62); opcode `metarule` = 0x810B (interpreter_extra.cc
  registration, confirmed by our IntVm dispatch at IntVm.cs:935).

### Where it is currently stubbed in Hexwaste
`ScriptHost.Metarule` (ScriptHost.cs:798-804) handles **only** rule 14
(FIRST_RUN), rule 49 (WEAPON_DAMAGE_TYPE / the explosion marker); everything else
`_ => 0`. So **metarule(16) returns 0 today.** (IntVm.cs:935-939 pops param then
rule, calls `_externals.Metarule(rule, param)`; the default `Metarule` in the
interface, IntVm.cs:111, is `rule == 14 ? 1 : 0`.)

### The port (the load-bearing detail: our roster does NOT contain the dude)
`ScriptHost.PartyMembers` (ScriptHost.cs:107) holds **only recruited members** —
`party_add` does `PartyMembers.Add(obj)` (ScriptHost.cs:944-946), `party_member_obj`
looks up by pid in that list (`PartyMemberByPid`, ScriptHost.cs:957-958). The dude
is NOT a member of it. Therefore the faithful port adds the implicit dude as +1:
```csharp
16 => 1 + _host.PartyMembers.Count(m =>
        Fid.PidType(m.Pid) == (int)ObjectType.Critter && !m.IsDead && !m.IsHidden),
```
`MapObject` already exposes `IsDead` (MapFile.cs:114, `DAM_DEAD` bit), `IsHidden`
(MapFile.cs:124, flag 0x01), and `Fid.PidType(Pid)` (MapFile.cs:72) — no new
plumbing. **~3 LoC in the switch.**

### Why it is load-bearing (the dialog gates that subtract 1)
dcVic uses `metarule(16) PARTY_COUNT` at **four** sites, always as `metarule(16)
- 1` to recover the *recruited* count:
- 0x18a2-0x18b0 (debug), then 0x18ba/0x190a/0x1948: `push_int 16; push_int 0;
  metarule; push_int 1; sub` — the `partyCount - 1` value feeding the party-size
  gate (the Node010 `metarule(16)-1 >= floor(dude.CHA/2) + has_trait(98)` /
  `>= 5` test from p8-track-b.md:58).

With our stubbed `0`, every gate computes `0 - 1 = -1`, which is `>= floor(CHA/2)`
**never** (CHA≥1 ⇒ floor≥0 > -1) → **the join is always offered and the "party
full" refusal NEVER fires.** Default-0 does not block the join (matches
p8-track-b.md:75 "broken-cosmetic"), but it is wrong and trivial to fix.
**Recommend M4 implements this first** — it is the one genuinely-needed companion
VM change and is shared by the encounter spawn-count side too (any future
party-size-gated content).

---

## (2) DISMISS / REJOIN + follow-loop audit (S, ~0 new VM LoC — all externals real)

### The follow loop IS the disassembled `critter_p_proc` bytecode (no host follow AI)
Hexwaste runs one `critter_p_proc` per game tick round-robin
(ViewerGame.cs:2326-2348, `_scriptHost.RunObjectProc(critter,…,"critter_p_proc")`)
— following is **100% script-side**, exactly as the engine. So the "audit" is:
does our VM implement every external the loop calls? It does. Operand-level walk
of **kcsulik critter_p_proc [0x25a4..0x29b8]**:

- **Aggro short-circuit** (0x25b2-0x2614): `if (LVAR[5]==2 OR LVAR[6]==1) AND
  obj_can_see_obj(self,dude)` → `LVAR[5]=1`, `attack(dude,…)`. (LVAR[5] = betrayal
  state, LVAR[6] = a second aggro flag.)
- **Karma/rep betrayal checks** (0x2622-0x27c4): gated on `party_member_obj
  (16777313)!=0`; tests `GVAR[0] PLAYER_REPUTATION < -100`, `GVAR[1] CHILDKILLER
  >= 2`, `GVAR[4]+GVAR[5] >= 25 AND (GVAR[5] > 2*GVAR[4] OR GVAR[3]==1)`,
  `GVAR[11] REPUTATION_SLAVER == 1`, `metarule(46 CURRENT_TOWN)==2 AND GVAR[68]
  ENEMY_KLAMATH == 1` → various leave/attack nodes. All read via real
  externals (get_global_var, metarule rule 46 already answered by p8).
- **THE FOLLOW GATE** (0x27c6-0x281c):
  `if (LVAR[11] != 0) == 0   // i.e. LVAR[11]==0 → wait flag clear → follow`
  `  AND (GVAR[398] != 0) == 0  // i.e. GVAR[398]==0 → global stop-follow clear`
- **Distance default** (0x2826-0x2844): `if LVAR[12]==0 { LVAR[12]=6 }`.
- **Move decision** (0x284c-0x291c): `dist = tile_distance_objs(self,dude);
  if (dist > 3*LVAR[12]/2)` and `anim_busy(self)==0` →
  target = `tile_num_in_direction(tile_num_in_direction(dude.tile,
  rotation_to_tile(dude.tile,self.tile), LVAR[12]), random(0,5), random(0,2))`
  stored in GVAR[11] (scratch); then if `dist > 2*(3*LVAR[12]/2)` →
  `opAnimateMoveObjectToTile(self, GVAR[11], 1)` **(run)** else `…, 0)` **(walk)**.
- **Stop** (0x2924-0x2950): `if tile_distance(self,dude) < tile_distance(self,
  GVAR[11])` → `reg_anim_func(2, self)` (halt).

**Sulik LVAR map (verified):** **distance = LVAR[12]** (default 6),
**wait = LVAR[11]**, betrayal-state = LVAR[5], saved-original-team = LVAR[13]
(damage_p_proc 0x4782 sets `LVAR[13] = -1` on involuntary leave; rejoin reads it).

**Vic LVAR map (verified, dcVic critter_p_proc [0x1478..0x1840]):** gate on
`party_member_obj`, `LVAR[5]` (wait/state), `GVAR[398]`; **distance = LVAR[6]**
(default 6, set at 0x14fe-0x150a), same `tile_distance_objs` / `opAnimateMove
ObjectToTile` / GVAR[11]-scratch pattern. So the prompt's open question
"(LVAR[11]/[6/12]?)" resolves as: **Sulik wait=LVAR[11] dist=LVAR[12]; Vic
wait=LVAR[5] dist=LVAR[6]** — different indices per script, but our VM reads them
generically through `get_local_var`/`set_local_var` so no per-script knowledge is
needed.

### Audit result: every follow external is implemented and non-stub
| external | opcode | Hexwaste site | status |
|---|---|---|---|
| get_local_var / set_local_var | 0x80C1 / 0x80C2 | IntVm.cs:876 / 894 | real |
| get_global_var | 0x80C5 | (dispatched) | real |
| tile_distance_objs | 0x80D3 | (dispatched) | real |
| tile_num_in_direction | 0x80D5 | IntVm.cs:1271 | real |
| rotation_to_tile | 0x814C | IntVm.cs:1022 | real |
| anim_busy | 0x80E7 | IntVm.cs:1008 → AnimBusyResolver (ScriptHost.cs:935) | real |
| opAnimateMoveObjectToTile | 0x80CE | IntVm.cs:1300 → AnimateMoveToTile → MoveRequested (ScriptHost.cs:848-852) | real |
| reg_anim_func | 0x810E | (dispatched) | real |
| party_member_obj | 0x814B | IntVm.cs:1125 → PartyMemberByPid (ScriptHost.cs:957) | real |

**The wait/stop semantics come for free**: the loop reads `LVAR[11]`/`LVAR[5]`
and `GVAR[398]` from real backing storage — IF our LVAR slices and GVAR dict
persist correctly (they do, per phase-5 M1 LVAR slices keyed by map name +
phase-7 party LVAR carry). The one thing to verify on the bench: that the wait
LVAR a dismiss node writes (game_time(), nonzero) actually survives into the next
critter_p_proc tick and across a map transition for a party member (party LVAR
carry, p7-track-b.md:223). **No new VM code; this is a test, not a feature.**

### DISMISS / REJOIN are pure dialog nodes (all externals already real)
**Sulik REJOIN — Node800 [0x8ddc..0x8ed2]:**
```
if (critter_state(self) & 1 == 0)        // 0x8de6-0x8df8  alive (DAM_DEAD bit clear)
  if (LVAR[12] == 0) LVAR[12] = 6        // 0x8e00-0x8e1e  reset follow distance
  LVAR[11] = 0                            // 0x8e20-0x8e2c  clear wait flag
  if (has_trait(OBJECT,self,6) != 0)      // 0x8e34-0x8e4c  save orig team
     LVAR[13] = has_trait(OBJECT,self,6)  // 0x8e4e-0x8e64
  critter_add_trait(self, 1, 6, 0)        // 0x8e66-0x8e7a  TEAM_NUM → 0 (player)
  party_add(self)                          // 0x8e7e-0x8e80
  add_timer_event(self, game_ticks(1), 1) // 0x8e90-0x8ea0
  critter_add_trait(self, 1, 6, 0)        // 0x8ea2-0x8eb6  (re-set team 0)
```

**Sulik DISMISS — Node1002 [0x994a..0x9c18]** (this is a "wait here", not a hard
leave): `LVAR[11] = game_time()` (0x994c-0x9954, sets the wait flag nonzero),
`party_remove(self)` (0x9956-0x9958), then re-offers a follow-options menu via
`message_str(14, 10001/10008/10009)` + `giq_option`. The hard involuntary leave
(team restored to LVAR[13], `LVAR[5]=2` aggro) is in **damage_p_proc** 0x468a-
0x47b4: `party_remove(self)`, `critter_add_trait(self,1,6,LVAR[13])`,
`LVAR[13]=-1`, `LVAR[5]=2`.

**Vic JOIN — Node994 [0x8818..0x8914]:** `LVAR[4] |= 8192` (0x8820-0x8830,
already-joined flag), `LVAR[6] = 6` (0x886c-0x8872, follow distance),
`LVAR[5] = 0` (0x8874-0x8880, wait clear), `critter_add_trait(self,1,6,0)`
(0x88c2-0x88ce, team 0), `party_add(self)` (0x88d4).

**Vic DISMISS — Node1002 [0x50ac..0x5422]:** `LVAR[5] = game_time()`
(0x50ae-0x50b6, wait marker), `party_remove(self)` (0x50ba),
**`critter_add_trait(self,1,6,25)`** (0x50ca-0x50d0, **team 25 = Vic's original
DEN team** — this is the literal "team 25" the prompt asked about; it is
Vic-specific, hardcoded, vs Sulik which restores the *saved* LVAR[13] team),
`LVAR[4] |= 8192` then `|= 4096`.

**External census for all four nodes — all real in Hexwaste:** `party_add`
(0x8124, IntVm.cs:1119), `party_remove` (0x8125, IntVm.cs:1122), `critter_add_trait`
(0x8102, real — p6/p7), `set_local_var`/`get_local_var` (real),
`opGetCritterState` (0x80FB, real), `game_time` (0x80EA, real),
`add_timer_event`/`game_ticks` (0x80F0/0x80F2, real, phase-5),
`has_trait` (0x80F3, real). **Zero new externals.** The only new work is a
TRADE/dismiss/wait entry point on the in-party gsay hub (the dialog tree already
runs; these nodes are reachable once the hub options are bound) and resolving
the **partymbr.msg file id 14** strings (ids 10001-10010 — "wait here", "stay
close", "follow at medium/long range", etc.) or the follow-option text renders
blank (soft blocker, p8-track-b.md:114).
**CORRECTED (#8, verified 2026-06):** the partymbr.msg "soft blocker" was a MYTH.
`partymbr.msg` does not exist in the game data, "partymbr" is absent from the
fallout2-ce source, and list 14 = `generic.msg`. The shipped hub uses English
fallback labels and performs no list-14 lookup, so there is no blank-text risk.
See the "UNVERIFIED / honest flags" section below for the resolution.

---

## (3) 1:1 companion-inventory TRADE panel (M-, ~120 LoC viewer — reuses loot panel)

### Engine: party-member TRADE is a flat move, NOT priced barter
The TRADE button ('d') on the party-member control window
(`partyMemberControlWindowHandleEvents`, game_dialog.cc:3757-3762):
```c
} else if (keyCode == KEY_LOWERCASE_D) {
    if (_gdCanBarter()) { _dialogue_switch_mode = 2; _dialogue_state = 4; return; }
}
```
which dispatches (game_dialog.cc:1904) to:
```c
inventoryOpenTrade(gGameDialogWindow, gGameDialogSpeaker,
                   _peon_table_obj, _barterer_table_obj, gGameDialogBarterModifier);
```
The crucial fact: **`gGameDialogBarterModifier` is reset to 0** at dialog window
init (game_dialog.cc:726) and is **only** changed by `gameDialogSetBarterModifier`
(game_dialog.cc:3156-3158) / `gameDialogBarter` (game_dialog.cc:3163-3169) — the
**priced shop-barter** path (the `gdialog_barter` opcode). The party-member
control TRADE path never calls those, so it trades at **modifier 0**. In
inventory.cc the modifier folds into the markup (inventory.cc:5090-5124,
`_barter_mod = barterMod + modifier`); modifier 0 + no caps exchange ⇒ items move
**1:1, no caps**. This is exactly p8-track-b.md:104's finding, now confirmed at the
operand level.

### `_gdCanBarter` would block followers without the CRITTER_BARTER flag — bypass it
`_gdCanBarter` (game_dialog.cc:3662-3675): returns 1 only if non-critter, or no
proto, or `proto->critter.data.flags & CRITTER_BARTER`. Most companions (Sulik,
Vic) do **not** carry CRITTER_BARTER → vanilla shows "This person will not barter
with you" (msg 903) and the engine routes the party TRADE through a different
control window anyway. For our flat panel we **bypass the `_gdCanBarter` /
CRITTER_BARTER check entirely** (it only matters for the priced-barter speaker,
which we are not building).

### Reuse Hexwaste's loot panel — point `_lootContainer` at the follower
The corpse/container loot UI already exists and is generic:
- `_lootContainer` (MapObject, ViewerGame.cs:171); loot mode keyboard handler
  (ViewerGame.cs:1066-1104): number keys take, A take-all, Esc/I close,
  Shift+number drop.
- `TakeFromContainer(index)` (ViewerGame.cs:1748-1756) is **already generic** —
  it moves `_lootContainer.Inventory[index]` into the dude bag via
  `AddToDudeInventory`. Pointing `_lootContainer` at the follower's
  `MapObject.Inventory` makes the **take-from-companion** side work unchanged.
- The **give-to-companion** side needs one new variant: `DropFromInventory`
  (ViewerGame.cs:1758-1768) currently drops to the *map floor*
  (`_map.Elevations[…].Objects.Add(item)`). A companion-trade variant must
  instead `follower.Inventory.Add(item)` (the inverse of TakeFromContainer). This
  is the only genuinely new transfer logic (~15 LoC) — everything else (the
  two-pane render, the take side, take-all, close) is reused. Optionally wire the
  VM `move_obj_inven_to_obj` (0x8147, **already implemented** IntVm.cs:1135,
  ExternalArity.cs:185) as the move primitive so a script could drive it too, but
  the panel doesn't need it — direct list moves suffice (same as the corpse loot).

**Net: M- (~120 LoC viewer), zero new engine externals.** New = a follower-trade
entry point (a key while talking to / hovering a party member, or a gsay hub
option) + the give-to-follower drop variant + a "trading with NAME" panel header.

### Equip-best ('w'/'a') is optional polish — skip for the cut
The engine's USE-BEST-WEAPON/ARMOR buttons auto-equip; we equip via item flags
0x1/0x2 (p6-m4) so this is trivial-but-optional balance polish. **Out of the M5
cut** unless cheap at the time.

---

## Vic's radio rescue (0x810D / 0x80BA) — DEFER to a later phase. Here's why.

The prompt asks whether to wire Vic's *actual* radio rescue now. **No — the
cash/dismiss/trade lifecycle is the right cut; the radio is a later phase.**
Empirical basis (cross-checked against p8-track-b.md:34-42):
- Vic's **legitimate cash rescue needs ZERO new externals** (Metzger $1000 path,
  dcMetzge Node025 sets `GVAR445 |= 0x8000000`; dcVic reads that bit as the join
  gate). p8-track-b designed this whole vertical slice (M1–M2) on existing
  machinery. The radio leg is **flavor on the path, not the rescue gate**
  (p8-track-b.md:42).
- The radio leg needs **two new inventory-query externals** that are currently
  **stubbed** (they have arity entries — `obj_is_carrying_obj` 0x80BA
  ExternalArity.cs:44, `obj_carrying_pid_obj` 0x810D ExternalArity.cs:127 — but
  **NO handler** in the IntVm switch, so both hit `OnStubbedExternal` and return
  0). `rm_obj_from_inven` (0x80D9) *is* implemented (IntVm.cs:1189). dcVic
  Node004 [0x3514-0x35f8] / Node005 [0x35f8-0x36ae] gate the "give Vic the radio"
  branch on `obj_carrying_pid_obj(dude,266)` / `obj_is_carrying_obj(dude,100)` and
  set `GVAR446 |= 0x400000`.
- The two externals are each ~10 LoC (scan a MapObject.Inventory for a pid /
  handle), so they are **cheap** — but the radio sub-quest they unlock is a
  multi-node dialog leg (msg 163/174/177) that is *content*, not lifecycle, and is
  Vic-specific. Folding the whole Vic rescue (cash + radio + join + per-map
  follow) into Track E's M4–M5 would blow the "cheap + reusable only" scope line
  the phase set (it is a 5-milestone vertical slice in its own right per
  p8-track-b.md:127-133).

**Recommendation:** wire the *reusable* pieces now (metarule 16, dismiss/rejoin,
trade panel — they benefit ANY companion, including encounter-spawned allies and
the existing `--recruit` test plumbing), and **leave Vic's radio rescue for a
dedicated phase**. If a future "Den vertical slice" phase lands, add the two
inventory-query externals there. (The cash-purchase rescue could even be wired
cheaply at that point since it needs no new externals — but it is content, not
lifecycle, so it does not belong in this fold-in.)

---

## M4–M5 split (recommended)

- **M4 — metarule(16) + dismiss/rejoin + follow audit (S).**
  - Port `_getPartyMemberCount` into `ScriptHost.Metarule` rule 16 (`1 + live
    recruited`), ~3 LoC. Headless test: spawn N members via `--recruit`, assert
    `metarule(16)` reports N+1 and the dcVic party-size gate
    (`metarule(16)-1 >= floor(CHA/2)`) refuses at full party.
  - Bind the dismiss/rejoin/wait/follow-distance gsay hub options (Node1002 /
    Node800 / Node1007 family for Sulik; Node1002 / Node994 / Node1007 for Vic).
    Resolve **partymbr.msg id 14** (ids 10001-10010) for option text.
    <!-- CORRECTED (#8): partymbr.msg / list 14 does not exist; hub uses English
    fallback labels, no message_str(14,…) lookup. See the UNVERIFIED-flags resolution. -->

  - **Audit (test, not code):** verify the wait LVAR (Sulik [11], Vic [5]) and
    `GVAR[398]` halt/resume the follow loop across a critter_p_proc tick AND a map
    transition (party LVAR carry). All follow externals are already real — this is
    a golden-transcript fixture (`--rng-seed` deterministic), not a feature.
  - Demoable: recruit → "wait here" (companion stops) → "follow me" (resumes) →
    dismiss (party_remove, team restored) → rejoin (alive-gated party_add).

- **M5 — flat 1:1 companion-trade panel (M-).**
  - Point the existing loot panel's `_lootContainer` at the follower's
    `MapObject.Inventory`; add the **give-to-follower** drop variant (inverse of
    `TakeFromContainer`); add a trade entry point (key while hovering/talking to a
    party member) + a "Trading with NAME" header. Bypass `_gdCanBarter` /
    CRITTER_BARTER. Optionally route `move_obj_inven_to_obj` (already real) as the
    move primitive.
  - Headless test: open companion trade, move an item each way, assert both
    inventories and that NO caps changed (flat 1:1). Verify the moved item
    survives save/load + a map transition (the follower travels outside map
    deltas, p7-track-b.md:223).
  - Demoable: give the companion a weapon (it then auto-equips via item flags),
    take their starting loot.

**Sizing summary:** M4 = **S** (one ~3-LoC metarule port + dialog-hub wiring +
msg-file resolve + audit fixtures); M5 = **M-** (~120 LoC viewer, give-to-follower
variant is the only new transfer logic). **Zero new externals** for the whole
fold-in (radio's two are explicitly deferred with the radio rescue). This fits
"cheap + reusable, not a quest VM."

---

## UNVERIFIED / honest flags

- **RESOLVED (#8, 2026-06) — the partymbr.msg id-14 premise was WRONG.** Empirically
  verified against this game data + the fallout2-ce source:
  - `dotnet run --project tools/DatDump -- extract "text\english\dialog\partymbr.msg"`
    → "not found in any mounted base" (also not under `text\english\game\`). The file
    does not exist in the slice.
  - `grep -rin partymbr reference/fallout2-ce` → ZERO matches. The engine has no
    `partymbr` symbol or file at all.
  - `scripts.lst` line 14 = `Generic.int` → message list 14 resolves (via the
    project's index+1 rule) to `generic.msg`, the generic-dialog file, which carries
    no 10001-10010 party strings. The engine's party-control window text comes from
    `gProtoMessageList`/`gMiscMessageList` (proto.msg / misc.msg @ 9000+), not a
    partymbr file.
  Conclusion: the `message_str(14, 1000N)` reading was an unconfirmed guess (flagged
  honest at the time). The engine's real wait/follow/dismiss path is plain
  `party_add`/`party_remove` from a companion's own `talk_p_proc` reply procedure —
  no shared partymbr message file. The shipped viewer hub uses English fallback
  labels and reproduces those exact party side effects, so there is no blank-text
  risk and no list-14 lookup. Issue #8 ("partymbr.msg list-14 routing") was closed as
  a documentation correction.
- **UNVERIFIED: that our party LVAR carry preserves the wait/distance LVARs across
  a map transition for a recruited member.** p7-track-b.md:223 designed party-LVAR
  carry (firstRun semantics must NOT re-init member scripts on revisit), but I did
  not run a save/transition probe of a *waiting* companion's LVAR[11]/[5] here.
  This is the M4 audit fixture and the single real follow-loop risk.
- **UNVERIFIED: the exact team id Sulik restores on hard leave.** damage_p_proc
  saves `LVAR[13] = has_trait(OBJECT,self,6)` and restores it (0x4782 sets it to
  -1 after); the *value* (Sulik's pristine Klamath team) was not read from the
  proto here. Vic's dismiss hardcodes **team 25** (dcVic Node1002 0x50ca-0x50d0),
  confirmed.
- The encounter-spawned allies (if any FIGHTING-each-other group is friendly to
  the player) would also benefit from metarule(16), but no early-loop encounter
  group recruits the player — so M4's metarule fix is purely for the
  Sulik/Vic dialog gates in this slice. (Encounter hostility is Track A/B's
  scripted-aggro heartbeat, p8-track-a.md:101.)
