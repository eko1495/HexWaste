# Phase 6 Track A — Barter, implementation-grade research

All engine citations are from `<repo>/reference/fallout2-ce/src` (file:line of the
checked-out tree). All game-content values were parsed from real files extracted out of
`master.dat` via DatDump; parse scripts and outputs live in `/tmp/p6a` and `/tmp/p6a-probe`.

---

## 1. The deferred-node flow — CONFIRMED, with one correction

### Opcode side

- `_op_gdialog_barter` — interpreter_extra.cc:4026-4033 (registered for 0x8129 at :5010).
  Pops one int (the modifier!) and calls `gameDialogBarter(data)`. **The SSL argument of
  `gdialog_barter` IS a barter modifier**, not a dummy.
- `opGameDialogSetBarterMod` — interpreter_extra.cc:4697-4702 (0x814E at :5047). Pops one int,
  calls `gameDialogSetBarterModifier(data)` = game_dialog.cc:3156-3159, which just stores
  `gGameDialogBarterModifier = modifier` (declared game_dialog.cc:227, reset to 0 in
  `gameDialogInit` path at :726).

### Engine flow (exact sequence)

1. Player clicks a dialog option → `_gdProcess` (game_dialog.cc:1856) → `_gdProcessChoice`
   (game_dialog.cc:2024) → `_executeProcedure(gDialogReplyProgram, dialogOptionEntry->proc)`
   at game_dialog.cc:2080-2082 runs the bound script proc (e.g. dcTubby `Node996`).
2. Inside that proc, `gdialog_barter(mod)` → `gameDialogBarter(modifier)`
   (game_dialog.cc:3163-3175): sets `gGameDialogBarterModifier = modifier` (:3169 —
   **unconditionally overwrites whatever `gdialog_set_barter_mod` set earlier**), calls
   `gameDialogBarterButtonUpMouseUp(-1, -1)` (:3170), then sets `_dialogue_state = 4;
   _dialogue_switch_mode = 2` (:3171-3172) and returns. **Nothing opens here — it only sets
   flags.** The script proc keeps running and builds the post-barter node (`gsay_reply` +
   `giq_option`s) — in real scripts the `gdialog_barter` call comes FIRST and the node-building
   AFTER (see §4), but since the opcode is flag-only the order inside the proc is irrelevant.
3. `gameDialogBarterButtonUpMouseUp` (game_dialog.cc:4272-4312) is the gatekeeper: it requires
   the speaker's critter proto to have `CRITTER_BARTER` (0x02, obj_types.h:93) in
   `proto->critter.data.flags` (:4285-4286); if missing it renders proto.msg line 903
   ("This person will not barter with you." — verified text\english\game\proto.msg:266) /
   913 for party members (:4299-4305) and does NOT switch modes.
4. Back in `_gdProcessChoice`, `_gdProcessUpdate()` (game_dialog.cc:2091, def :2223) renders
   the queued node — so the post-barter node is fully built (and even rendered) **before** the
   trade window exists.
5. `gameDialogTicker` (game_dialog.cc:2797, runs from the input pump) sees
   `_dialogue_switch_mode == 2` (:2799-2807): destroys the dialog window, calls
   `_gdialog_barter_create_win()` (def :3189), sets `_dialogue_switch_mode = 3` (:2802).
6. The `_gdProcess` loop sees `switch_mode == 3` (game_dialog.cc:1899-1915) and calls the
   **modal** `inventoryOpenTrade(gGameDialogWindow, gGameDialogSpeaker, _peon_table_obj,
   _barterer_table_obj, gGameDialogBarterModifier)` (:1904). This is who consumes the modifier.
7. When the trade window closes: `_gdialog_barter_cleanup_tables()` (:1905, def :3321-3351)
   force-returns any items left on the player table → `gDude` and on the barterer table (+ the
   `_barterer_temp_obj` that holds the weapon/armor stripped off the NPC at trade start,
   inventory.cc:5042-5057) → speaker. Then `_gdialog_barter_destroy_win()` and
   `_dialogue_switch_mode = 1; _dialogue_state = 1` (:1908-1914).
8. `gameDialogTicker` case 1 (game_dialog.cc:2809-2823) recreates the dialog window
   (`_gdialog_window_create` + `gdUnhide`, :2812-2816) — **the node queued in step 2 now
   presents.** (SFALL fix at :2818-2822 re-renders the caps counter.)

**Verdict: model confirmed**, with two corrections: (a) the script calls the opcode first and
builds the node after (not "node first"), which is equivalent because the opcode is flag-only;
(b) `gdialog_barter`'s own argument is a modifier that clobbers `gdialog_set_barter_mod`'s value
(game_dialog.cc:3169) — `set_barter_mod` only survives when barter is started via the dialog
window's "Barter" button (key `b`, game_dialog.cc:1928-1930 → `gameDialogBarterButtonUpMouseUp`,
which does NOT touch the modifier).

---

## 2. Price math, exactly

### Cost of one item — `itemGetCost`, item.cc:813-859

```
cost = proto->item.cost                              // item.cc:824
ITEM_TYPE_CONTAINER: cost += objectGetCost(obj)      // :828   (contents)
ITEM_TYPE_WEAPON:    cost += ammoQty * ammoProto->item.cost
                              / ammoProto->item.data.ammo.quantity   // :831-844 (loaded ammo)
ITEM_TYPE_AMMO:      cost = cost * ammoQty / ammoCapacity            // :846-854 (partial clip)
```
`objectGetCost` (item.cc:864-915) sums `itemGetCost * quantity` over an inventory, with a
special case for ammo stacks (full clips at proto cost + current clip pro-rated, :874-890) and
adds the critter's wielded/worn gear if not flagged in-hand/worn (:896-910).

### The barter valuation — `_barter_compute_value`, inventory.cc:4673-4703

Computes what the NPC demands for the contents of HIS table `_btable` (everything you take,
including caps you take as payment):

```
if speaker is party member: return objectGetInventoryWeight(_btable)        // :4675-4677
cost            = objectGetCost(_btable)                                    // :4679
caps            = itemGetTotalCaps(_btable)                                 // :4680
costWithoutCaps = cost - caps                                               // :4681
perkBonus       = 25.0  if dude==gDude && perkHasRank(PERK_MASTER_TRADER)   // :4683-4688
partyBarter     = partyGetBestSkillValue(SKILL_BARTER)                      // :4690
npcBarter       = skillGetValue(npc, SKILL_BARTER)                          // :4691
barterModMult   = (_barter_mod + 100.0 - perkBonus) * 0.01                  // :4694
balancedCost    = (160.0 + npcBarter) / (160.0 + partyBarter)
                  * (costWithoutCaps * 2.0)                                 // :4695
if barterModMult < 0: barterModMult = 0.0099999998                          // :4696-4699
return (int)(barterModMult * balancedCost + caps)                           // :4701
```

**Caps (pid 41) special-casing**: `itemGetTotalCaps` (item.cc:3153-3186) counts the *quantity*
of `PROTO_ID_MONEY = 41` (proto_types.h:139) stacks (recursing into containers). Caps are
subtracted before the multiplier and re-added at face value (:4681, :4701) — money always
trades 1:1 in both directions, so the "money" proto's cost field (1) never enters the formula.

**The player's side is NOT skill-adjusted**: the acceptance test in
`_barter_attempt_transaction` (inventory.cc:4706-4760) is
`objectGetCost(offerTable) >= _barter_compute_value(dude, npc)` (:4742-4746) — your goods are
credited at full base cost; the whole spread lives in the 2x-and-skill term on the buy side.
The two on-screen numbers match: left/player table shows plain `objectGetCost(leftTable)`
(inventory.cc:4973-4974), right/NPC table shows `_barter_compute_value` (:5012-5013).

**`_barter_mod`** (declared inventory.cc:445) = `barterMod` param at trade open (:5035), then
refreshed every loop frame as `barterMod + reactionModifier` (:5124) where reactionModifier
comes from `reactionGetValue/reactionTranslateValue` (:5090-5105): BAD +25, NEUTRAL 0,
GOOD -15. Positive modifier ⇒ more expensive. (`modifier <= -30` at :5126 also force-ends
barter, unreachable via reactions alone.)

### Worked numbers — 5 real items, real shopkeeper

Inputs (all parsed, see §3/§4 for method): dude barter = 35 (given; party best = 35), NPC =
Tubby/Flick, both critter pid 58 "Average Merchant" (pro_crit.msg:69 `{5800}`), parsed barter
skill = **80** (CH 6 → 4×6=24, +56 proto skill points; formula `defaultValue 0 +
4×CH + 1×points` from gSkillDescriptions[SKILL_BARTER=15] = `{..., 43, 0, 4, STAT_CHARISMA,
STAT_INVALID, 1, 0, 0}`, skill.cc:93 row 16 of :78-98, applied in skillGetValue skill.cc; NPC
branch has no dude-only bonuses). Modifier = 0 (both shops call `gdialog_barter(0)`, §4),
neutral reaction, no Master Trader.

Buy multiplier = `1.0 × (160+80)/(160+35) × 2 = 2.46154`; sell = face value.

| item (pid, type) | parsed base cost | BUY from Tubby | SELL to Tubby |
|---|---|---|---|
| Stimpak (40, drug) | 175 | int(430.77) = **430** | **175** |
| 10mm Pistol (8, weapon, unloaded) | 250 | **615** | **250** |
| Leather Jacket (74, armor) | 250 | **615** | **250** |
| Fruit (71, drug) | 10 | **24** | **10** |
| Money (41, misc) | 1 (unused) | 100 caps cost **100** | **100** |

With Master Trader: mult 0.75 ⇒ Stimpak buys at int(0.75×430.77) = **323**. If Tubby's
`gdialog_set_barter_mod(-30)` actually survived (it doesn't via the dialog option, see §1/§4):
mult 0.7 ⇒ **301**. A loaded 10mm Pistol adds `ammoQty × cost(10mm ammo)/clipQty` (item.cc:838).

Pid verification: every proto's `messageId` parsed as `pid×100` and matched against
text\english\game\pro_item.msg — {800} 10mm Pistol, {4000} Stimpak, {4100} Money, {7100} Fruit,
{7400} Leather Jacket.

---

## 3. Proto cost field offset

From `protoRead` (proto.cc:1663-1685), items only — field order with absolute file offsets
(all **big-endian** int32):

| offset | field |
|---|---|
| 0 | pid |
| 4 | messageId |
| 8 | fid |
| 12 | lightDistance |
| 16 | lightIntensity |
| 20 | flags |
| 24 | extendedFlags |
| 28 | sid |
| 32 | item.type |
| 36 | material |
| 40 | size |
| 44 | weight |
| **48** | **cost** (proto.cc:1680) |
| 52 | inventoryFid |
| 56 | field_80 (1 byte) |
| 57 | type-specific data (`protoItemDataRead`, proto.cc:1553) |

**Non-items have no cost field**: the critter branch (proto.cc:1687-1698), scenery
(:1700-1710), wall (:1711-1719), tile, misc branches read no cost; `protoGetDataMember`
serves `proto->item.cost` only for items (proto.cc:1151), and item init zeroes it (:392).

Empirical verification on real protos (parsed with `/tmp/p6a` python, big-endian @48), across
four different item types: pid 8 weapon (file 00000004.pro!) cost=250; pid 40 drug cost=175;
pid 74 armor cost=250; pid 41 misc cost=1; pid 71 drug cost=10 — all match the known in-game
prices and each file's `messageId == pid*100` cross-check.

**Filename gotcha (verified)**: proto file names are NOT the pid — `_proto_list_str`
(proto.cc:201-247) maps `pid & 0xFFFFFF` to line N of `proto\items\items.lst`. Line 8 is
`00000004.pro` (which contains pid 8/10mm Pistol), while the file literally named
`00000008.pro` contains pid 14 (Explosive Rocket, msgId 1400). Our ProtoDatabase already
indexes via items.lst, but any direct-by-name probing must go through the .lst.

**Fix for our reader**: src/Hexwaste.Formats/Proto/ProtoDatabase.cs:135 currently does
`reader.Skip(4 * 4); // material, size, weight, cost` — change to `reader.Skip(4 * 3)` +
`cost = reader.ReadInt32();` and surface `Cost` (and ideally `SubType`-specific ammo
`quantity` for clip pricing) on `ProtoInfo`.

(Critter proto offsets used for the skill parse, from protoRead :1687-1698 +
`protoCritterDataRead` critter.cc:1064-1091: data.flags @44 — `CRITTER_BARTER`=0x02 confirmed
set on pid 58 — baseStats[35] @48, bonusStats[35] @188, skills[18] @328 ⇒ barter = @328+15×4 =
@388; CH base @60, bonus @200.)

---

## 4. Shopkeeper scripts: Tubby & Flick (the Den)

scripts.lst (extracted `Scripts\SCRIPTS.LST` from patch000.dat — note: it lives under
`Scripts\`, not `data\`): line 42 `DCFlick.int` ⇒ 0-based index 41; line 48 `DCTubby.int` ⇒ 47.
Their stock boxes: line 170 `DITubBox.int`, line 171 `DIFlkBox.int`. Both critters located in
`maps\denbus1.map` via our own MapFile loader (`/tmp/p6a-probe`): both are critter pid 58,
sids 0x04000002 (Tubby) / 0x04000001 (Flick).

### Dialog/barter shape (disassembled with /tmp/p6a/disasm.py, built on tools/int_analyze.py)

**dcTubby.int** — `talk_p_proc` @0x11d0:
- Prologue: `move_obj_inven_to_obj(self, generic_temp_box)` then
  `move_obj_inven_to_obj(den_tubby_box_obj, self)` (identifiers verified at idents+912/+892) —
  personal gear parked in a temp box, store stock loaded ONTO the critter for the trade.
  (`opMoveObjectInventoryToObject` = itemMoveAll src→dest, interpreter_extra.cc:4582-4619.)
- `Node001` @0x234a: `set_local_var(9,1)`, **`gdialog_set_barter_mod(-30)`**, then a normal
  reply/options node (no barter yet). LVAR9 is only ever read to pick the post-barter option
  text (170 vs 168).
- `Node996` @0x28a0 and `Node995` @0x2952 (the "let's trade" option procs):
  **`gdialog_barter(0)` FIRST** (0x28a2-0x28a8), **then** `gsay_reply(48, sstr " ")` (a blank
  reply) + `giq_option`s pointing at Node005/Node999. This is the deferred node, built after
  the opcode — and the literal `0` argument clobbers Node001's -30 (game_dialog.cc:3169).
- Epilogue of `talk_p_proc` (after `gsay_end`/`end_gdialog` @0x1fb6): mirror moves —
  `move_obj_inven_to_obj(self, den_tubby_box_obj)` then `(generic_temp_box, self)`; LVAR4 |= 1.
- `Node993`/`Node992` use `item_caps_total`/`item_caps_adjust` (100/50 caps) but these are
  separate "pay for info" dialog nodes, **not** post-barter checks.

**DCFlick.int**: same pattern with `den_flick_box_obj`/`generic_temp_box` (idents +928/+948);
single barter node `Node990` @0x1f44: `gdialog_barter(0)` then `call Node003` which builds the
post-barter reply. **No `gdialog_set_barter_mod` at all** ⇒ mod 0.

**Post-barter checks: none.** Neither script inspects inventory, caps, or LVARs after the
trade window; the queued node presents unconditionally. **A no-op or cancelled trade window
cannot break their dialog state.** The only correctness requirement on our side is that the
`talk_p_proc` epilogue (the two inventory moves after the gsay loop) eventually runs when the
dialog ends, otherwise the stock stays on the critter and the personal gear stays in
generic_temp_box (mostly self-healing on next talk, but persisted deltas would drift).

### Stock replenishment (DITubBox.int, `map_enter_p_proc` @0x8d0, ~15 KB)

- `start` and `map_enter` both `store_external("den_tubby_box_obj", self)` (ident +426
  verified) — the box exports itself; the shopkeeper script imports it.
- Restock is gated by `metarule(22,0) == 0` (METARULE_IS_LOADGAME, scripts.h:66 — skip on
  savegame load) **and** `LVAR0 < game_time`.
- Caps: `item_caps_adjust(self, random(151,161) - item_caps_total(self))` — sets the till to
  ~151-161 caps absolute.
- Goods: per item pid, if `obj_is_carrying_obj(self, pid) < random(lo,hi)` → `create_object` +
  `add_mult_objs_to_inven` to top up (26 create_object sites). Stocked pids parsed from the
  bytecode: 6 (Sledgehammer), 8 (10mm Pistol), 9 (10mm SMG), 18 (Desert Eagle), 21 (Brass
  Knuckles), 29/30 (10mm JHP/AP), 40 (Stimpak), 48 (RadAway), 74 (Leather Jacket), 87
  (Buffout), 110 (Psycho), 259 (Jet) — names cross-checked in pro_item.msg.
- Timer reset: `LVAR0 = game_time + random(1,2)*24*60*60*10` (1-2 game days, 10 ticks/s)
  @0x47b8-0x47f0.

So stock lives in an invisible container critter-adjacent, replenished in the **box's**
map_enter, and is moved onto the shopkeeper only for the duration of talk_p_proc.

---

## 5. Minimal trade-loop state machine for Hexwaste

Engine model to copy (inventoryOpenTrade, inventory.cc:5031-5230): four piles — player
inventory, player offer table, NPC inventory(= the critter, holding stock), NPC offer table.
Modal loop; two actions: **T**alk = cancel-and-return (itemMoveAll both tables back,
:5126-5131, then `_barter_end_to_talk_to` game_dialog.cc:3178-3186) and **M** = attempt
transaction (:5132-5147).

`_barter_attempt_transaction` (inventory.cc:4706-4760) in order:
1. Weight check: dude carry weight < weight of NPC table ⇒ inventry.msg {31} (:4710-4718).
2. (party-member branch: reverse weight check ⇒ {32}, :4720-4729.)
3. `badOffer` if player offer table is empty (:4732-4734) or contains a queued (timer-active)
   item — exception: Geiger counter that can be switched off (:4735-4739).
4. Value check: `objectGetCost(offerTable) < _barter_compute_value(...)` ⇒ badOffer (:4742-4746).
5. badOffer ⇒ inventry.msg **{28} "No, your offer is not good enough."** (:4748-4754), abort.
6. Commit: `itemMoveAll(barterTable, dude); itemMoveAll(offerTable, npc);` (:4757-4758) —
   caps move as ordinary money-item stacks. Success toast: inventry.msg **{27} "OK, that's a
   good trade."** (inventory.cc:5137-5145).

Messages: there is **no `barter.msg`** in master.dat (extraction attempt failed; flagged) —
all barter strings are in `text\english\game\inventry.msg`, verified quotes:
`{27}{}{OK, that's a good trade.}`, `{28}{}{No, your offer is not good enough.}`,
`{30}{}{Wt.}`, `{31}{}{Sorry, you cannot carry that much.}`,
`{32}{}{Sorry, that's too much to carry.}` — plus proto.msg `{903}{}{This person will not
barter with you.}` for the CRITTER_BARTER gate.

### Proposed mapping onto our architecture

State on `DialogSession`:
- `int BarterModifier` — set by 0x814E handler (replace arity stub).
- `bool PendingBarter` + `int PendingBarterModifier` — set by 0x8129 handler, which must:
  check speaker proto `CritterState`/proto critter data flags for CRITTER_BARTER (0x02); if
  absent, append proto.msg 903 to the session's out-of-band lines and NOT set the flag;
  if present, set `PendingBarter = true; BarterModifier = popped arg` (overwrite semantics).
- Viewer: after `Choose(option)` returns and the next node is captured, if `PendingBarter`
  → open the barter panel **before** rendering the node; render the node when the panel closes.
  This reproduces the engine's window swap with zero VM changes.

Barter panel (reuse the loot panel's two-pane plumbing, but with 4 logical lists; simplest UI:
keep two panes and a Tab/arrow toggle between inventory-row and offer-row per side):
- Open: snapshot nothing; offers start empty. (Engine also strips the NPC's armed weapon/armor
  into a temp object, inventory.cc:5041-5057 — we can skip this cosmetic detail, but then the
  wielded weapon is purchasable; either way is consistent because cleanup returns it.)
- Each frame/redraw: leftValue = Σ itemGetCost(playerOffer); rightValue = caps(npcOffer) +
  (int)((mod + 100 − perk)/100 × (160+npcBarter)/(160+dudeBarter) × 2 × (cost(npcOffer) −
  caps(npcOffer))), clamping the multiplier at 0.01 — straight port of inventory.cc:4673-4701.
  mod = BarterModifier (+ reaction term only if we ever model reaction; 0 is faithful for a
  neutral NPC).
- Offer/retract: move item (or N of stack) between inventory and offer list — pure list ops,
  same as loot panel take/drop.
- Commit: empty-offer / value check → show {28}; else move npcOffer→dude, playerOffer→npc,
  show {27}. (Weight check optional; we already skip carry weight elsewhere — if skipped, skip
  {31} too. Queued-item check can be skipped: we don't run item timers.)
- Cancel/close (Esc): return both offer lists to their owners unconditionally — this is the
  engine's `_gdialog_barter_cleanup_tables` guarantee (game_dialog.cc:3321-3351), and it's what
  makes a partial implementation safe.
- After close: deliver the queued node; dialog continues. No script re-entry, no extra procs.

NPC barter skill: needs proto critter stat read — we already parse the critter stat block for
P5-M2 (CritterState = base+bonus); add skills[18] (file offset 328) or at minimum skills[15].
Caps: reuse the existing real `CapsTotal`/`CapsAdjust` machinery (money pid 41); in the panel,
caps should display as a quantity-stack like the engine does.

---

## 6. Effort estimate

**(a) Flat-price barter reusing the loot panel — M (2-4 sessions).**
Wire 0x8129/0x814E (small), PendingBarter handoff in the viewer dialog loop (small), the
4-list panel UI + commit/cancel (the bulk), item cost = `ProtoInfo.Cost` after the
ProtoDatabase one-liner fix plus the items.lst-indexed read. Value rule: leftCost >= rightCost
at base cost both sides.

**(b) Real formula with barter skill — S on top of (a).**
The formula is ~10 lines (inventory.cc:4673-4701); inputs needed: dude barter 35 (we already
have a stat block; tagged/trait/difficulty bonuses optional), NPC `skills[15]` from the critter
proto (one extra array in ProtoDatabase), caps face-value carve-out (we already know pid 41),
Master Trader = constant-off unless we have perks. Ammo/contents adjustments in itemGetCost
(item.cc:826-855) are a small follow-up; flag partial clips as approximate if skipped.

**Concrete risks**
1. **talk_p_proc epilogue must run at dialog end** — Tubby/Flick move stock back to their box
   AFTER the gsay loop. If our DialogSession tears down the VM at `gdialog_end` without letting
   `talk_p_proc` finish, store stock leaks onto the critter and into `generic_temp_box`
   (persisted-delta drift across saves). Verify our session end path resumes the proc.
2. **Imported/exported externals** (`den_tubby_box_obj`, `generic_temp_box`) must work across
   scripts (box's `start`/`map_enter` store_external before shopkeeper fetch_external) — if our
   VM's external-variable table is per-program rather than global, talk_p_proc's inventory
   moves will null-deref (engine shows "Script Error: ... obj is NULL" path,
   interpreter_extra.cc:4587-4596 — we should no-op on null, not throw).
3. **Restock timer in box map_enter** uses game_time and METARULE_IS_LOADGAME(22) — our
   RunMapEnter snapshot semantics + container-restock-by-design (Phase 4) interact here;
   make sure LVAR0 of the box persists or stock resets every visit (acceptable, but caps would
   also reset — matches our documented restock-by-design).
4. **Modifier overwrite semantics** — implement `gdialog_barter(arg)` as overwriting the mod;
   copying `set_barter_mod` into the trade without the overwrite gives Tubby a wrong 0.7x.
5. **Money is an item stack** — the panel must let the player offer N caps (partial stack),
   i.e. quantity-split UI, or selling becomes all-or-nothing. The loot panel's by-number-key
   model needs a count prompt for stacks.
6. UI fidelity scope creep: the engine's per-frame reaction-modifier refresh, weight checks,
   queued-item/Geiger rule and armed-NPC stripping are all safely skippable (documented fakes),
   because cancel-returns-everything makes any partial subset consistent.
