
# Vault City (loc 1504) QA sweep — 7/10 done

Fourth campaign-QA town. VC is DELIVERY-heavy (many "bring X to Y" quests = the tractable
tier), so a good source of quick goldens. Same toolset.

**DONE (5):**
- **497** Deliver beer & booze to Lydia — golden `quest-lydia-booze` (1a67282), 0→1→2. Lydia
  (VCDwnBar, vctydwtn 26306); drink-menu → real-alcohol-request chain `1,1,1,1,1,1` → she wants
  10 each → 497:=1; carry 10 beer (124) + 10 booze (125), info menu opt6 (delivery-ready reply)
  (`2,6` → Node032 obj_carrying 124+125) → 497:=2.
- **493** Deliver tools to Valerie — golden `quest-valerie-tools` (5bb69f6), 0→1→2. Valerie
  (VCMainWk, vctydwtn 21096, grumpy maintenance); repair chain `1,1,1,1,1,1,1` → 493:=1; carry
  wrench (384) + pliers (75), greeting `1,1` (tools-delivery prompt, Node023) → 493:=2.
- **80** Get a plow for Mr Smith — golden `quest-smith-plow` (7518c6b), 0→3→6. 2-NPC purchase
  chain: Smith (vctyctyd 14078) accept `2,1,1` + commit-to-pay on re-talk `4,1,1` (payment
  accepted → 80:=3, unlocks Harry); Harry (VCHarry 12513) offers the plow sale ONLY at 80>=3,
  buy for $800 `2,2,1` → delivery-to-Smiths line → 80:=6. GOTCHA: the cross-NPC option is
  gvar-gated (Harry's plow line hidden until Smith sets 80:=3) — needed the Smith re-talk to
  advance past :=1. Caps via --give 41:1000.
- **459** Rescue Amanda's husband Joshua — golden `quest-rescue-joshua`, 0→1→3. Amanda (vctyctyd
  22673) accept chain `1,1,1,1,1,1` → 459:=1; Barkus (vctydwtn 14896) bribe chain
  `1,1,4,1,1,1,1` with `--give 41:5000` (caps bribe, no dialogue text) → return to Amanda `1` →
  459:=3.
- **321** Deliver Moore's briefcase to Bishop in New Reno — golden `quest-moore-briefcase`,
  0→1→2. Cross-town, 3-stop chain: Moore (VCMoore, vctydwtn 17485) accept chain
  `1,2,1,1,2,2,2` (script-grants briefcase pid 336) → 321:=1; guard-vetting hop at New Reno
  elev 1, tile 17075 (carrying-336-gated accept, chain `3,1`); Bishop (ncBishop, newr2 elev 2,
  tile 17678) chain `1` → 321:=2. GOTCHA (runtime discovery, not visible in the static
  quest-path trace): Bishop is hostile toward a dude who approaches cold — the guard-vetting hop
  at 17075 is required first to clear that.

**REMAIN (2):**
- **85** Deliver jet sample to Dr Troy (VCDrTroy vctyvlt 13084) — STORY-GATED, NOT a cold-boot
  delivery: Troy's greeting stays at the no-quest-available baseline even with jet (item 259) in
  hand. Needs prior jet/drug-problem context (from the Den/Redding storyline). Skip until that
  context is settable.
- **529** Scout 8 sectors around Gecko + enter NCR — worldmap/Stark recon. See **529 verdict**
  below: needs real campaign machinery (the citizenship/conspiracy story arc), not an engine gap.

**DONE (+1): 89** Deliver Lynette's holodisk to Westin in NCR — golden `quest-lynette-holodisk`
(B4 Task 2), 0→1→3→4 (COMPLETES). Task 1's trace above correctly found no vc/vi/gc/gs writer for
`gvar88` values 1-4, but stopped short of the real setter: a full 1885-script sweep (not just the
VC/Gecko set) found it in `raiders2.int`'s `map_update_p_proc` — the **Raiders2.map** special-
encounter map (a real, loadable 3-elevation map, not just a stub). Unconditional on
`dude_elevation==2 && gvar88<4`, it fires on the very first map-update tick, i.e. a plain
`--goto-map Raiders2.map:<tile>:2` sets `88:=4` with zero dialogue.

Getting from `88==4` to `Node114` (`88:=5`) turned out to hide a SECOND, unrelated gate: the
hub's raiders-intel topic (`Node107`) branches on `bitwise_and(gvar373, 1)` — `GVAR_RAIDERS_FLAGS`
per `vault13.gam` (confirmed via `ProcAnalyze --bit-scan 373`, since a literal
`push/push/set_global_var` sweep across all 1885 scripts found zero writers — the true writer is
a computed `|=` only `--bit-scan` catches). Bit 0 of 373 is set only inside the Raiders2.map
mercenary critters' (`icMerc`/`icMrcCpt`/`icScout`) own `destroy_p_proc`, itself gated on
`GVAR_RAIDERS_COUNT` (377, seeded to 18 by the map's roster) dropping to `<=5` — i.e. the raiders
encounter must be substantially fought through (`source_obj==dude` on each kill), not merely
visited. `--kill` drives this the same sanctioned way as the existing kill-quest goldens (real
`destroy_p_proc`, only the cause-of-death is a debug shortcut); 14 of the 17 raiders on the map
are killed to cross the threshold.

With `gvar373` bit 0 set, Lynette's hub's raiders-intel branch now runs
`Node107→Node072→Node110→Node111→Node114` (`88:=5`). `Node114` itself checks
`obj_is_carrying_obj(dude,447)` (Bishop's Holodisk) — not yet held on the first pass, so it falls
to an informational side-branch. `--give 447:1` (combat/steal-gated acquisition in the intended
design, sanctioned the same way as quest 321's briefcase) then unlocks the hub's msg-394 reveal
(`gvar88==5 && carrying 447`) → `Node136→Node116` (`88:=6`) → `Node119→Node123` (`89:=1`).
SCWestin (`ncr3` 17892, displays in-game as "McGee") offers his own accept option gated
`gvar89==1 && carrying 447`; picking it calls `getDisk` directly (`89:=3`, consumes the item).
Returning to Lynette with `89==3` unlocks `Node125`'s `gvar89==3`-gated option → `Node129`:
`89:=4` (COMPLETES; also sets `gvar484:=2`). End state: `82=9, 79=4, 81=1, 88=6, 89=4`. No
`--set-global` anywhere; the raiders are a legitimate hostile faction (distinct from the VC-citizen
`79:=6` hostility guard, never triggered). `Node132` (`79:=5`, Lynette's own citizenship-rank-5
grant) remains untraced/unlanded — it needs `Node130a`'s `CHA>7` roll on top of everything above,
out of this task's scope; 529's Stark-recon gate (needing `gvar79==5`) is therefore still open.

**DONE (+1): 82** Solve the Gecko powerplant problem — golden `quest-gecko-powerplant` (B4 arc),
0→2→5→6→7→9, cross-town VC/Gecko chain; also grants VC Citizenship (79:=4, 81:=1) via McClure.
Full recipe in [[gecko-qa-sweep]] (this is filed as a Gecko-town quest; VC's McClure/Lynette/
Randal legs are the VC side of the same cross-town chain).

**529 verdict (2026-07-21):** **Outcome 3 — needs real new machinery (a campaign prerequisite
chain, not an engine subsystem). No code shipped.**

Static trace (`vcstark.int`, all writers found via `--quest-paths 529`): every write lives in
Sgt. Stark's own script. `Node054a`/`Node055a`/`Node055b` set `529:=1` (accepted); `Node057`
sets `529:=2` (COMPLETES for display>=1/completed>=2, +300 caps +350 xp); `Node059a`/`Node059b`
set `529:=3`; `Node061` sets `529:=4` (COMPLETES for display>=3/completed>=4, +750 xp, an item
`create_object` pid 59 + 500 caps). Disassembly (`int_disasm.py`) confirms all three gate nodes —
`Node054` (accept-gate check), `Node055` (re-check, same option), and `Node064` (the
general-hub "scouted enough" report option) — each independently AND-chain the *same* 8-term
`metarule3(rule=105, x, y, 0) > 1` sequence, one term per node, all 8 terms present in each of
the three: `(1224,171),(1274,172),(1323,173),(1224,223),(1324,225),(1224,274),(1275,274),
(1325,273)`. Rule 105 is `METARULE3_WM_SUBTILE_STATE` (`interpreter_extra.cc:1995`,
`wmSubTileGetVisitedState`), i.e. the worldmap per-subtile UNKNOWN(0)/KNOWN(1)/VISITED(2) fog
state at those 8 world-pixel coordinates around Gecko/NCR. `Node055` also reads a plain counter
`gvar 82 <8 / ==8 / >8` for flavour text (not a gate) — see the gvar-82 caveat below.

CAVEAT (gvar 82 double duty): `get_global_var` operand confirmed as **82**, the SAME global
that tracks quest **82** (Gecko powerplant, this town's REMAIN list) — Stark's `Node055` reuses
it purely for flavour-text branching (`<8`/`==8`/`>8`), unrelated to 529's own completion gate.
Harmless in practice (quest-82's tracked stage values top out at 3-4 per the 89-entry above, so
Stark's `>8` branch is unreachable at any *documented* stage value and the `<8` branch always
fires), but a future reader chasing "gvar 82" must not confuse Stark's flavour read with the
powerplant quest-stage writes — they are the same variable, two unrelated consumers.

Harness capability for THAT mechanism: already there in spirit. `Hexwaste.Formats.Map.WorldmapFog`
(`src/Hexwaste.Formats/Map/WorldmapFog.cs`) already ports `wmSubTileGetVisitedState`
(`StateAt(worldX,worldY)`) and `wmSubTileMarkRadiusVisited` (`MarkRadiusVisited`, called from
real `WorldmapTravel.Step`, `src/Hexwaste.Formats/Map/WorldmapTravel.cs:200,251` — driven by the
actual `--travel-from`/`--travel-step` verbs, not a poke). The ONLY missing wire is
`metarule3` rule 105 itself: `IntVm.cs`'s `case 0x80E1` handles rules 100/103/110 but has no
`rule == 105` branch (`src/Hexwaste.Formats/Int/IntVm.cs:1663-1678`), so any script's
`metarule3(105,...)` call always returns 0 — the AND chain is always false regardless of real
travel. This alone would be a textbook "one trivial flag" fix (a `SubtileStateProvider` on
`ScriptHost`, mirroring the existing `KillCountProvider`/`CarIsOutOfGas` pattern, ~8 lines
across `ScriptHost.cs`/`IntVm.cs`/`ViewerGame.cs`) — drafted and build-verified during this
session, then **reverted** once the blocker below was found, per the "land only if a golden
actually lands" rule.

The real blocker is upstream of gvar 529 entirely: Stark's `talk_p_proc` only reaches ANY of the
recon-job branches (including the plain fallback greeting we're used to) through a router that
requires `gvar79` (a VC-wide reputation/citizenship-rank stat, values 0-6 seen across every VC
NPC script) to already be `>=4` — the specific scouting-job branch needs `gvar79==5` exactly
(`vcstark.int` `talk_p_proc` @0x273a `gvar79==5 && lvar10==0`). A cold-boot character has
`gvar79==0`, so Stark shows only the generic 2-option greeting (verified empirically:
`--goto-map vctydwtn.map --talk-seq 12674` with a fresh char). Grepped every VC script for
writers of `gvar79`: the value 5 is set in exactly two places — `vcchet.int` `Node001b`
(Illicit Allocations Officer Chet's bribery/black-market citizenship route) and `vclynett.int`
`Node132` (the citizenship/conspiracy storyline's culmination reward: +2500 xp, `gvar81:=1`,
`gvar50 += 10`). Neither is reachable from a cold boot: Chet's critter is **not placed on any of
Vault City's 4 maps** (`vctydwtn`/`vctyctyd`/`vctycocl`/`vctyvlt` — confirmed via
`--map-objects` on all four, all elevations; he must be `create_object`-spawned by some other
hidden-passage trigger not yet found — **open lead: Chet's activation/spawn trigger is
unresearched**, no map or script yet found that `create_object`s him; that's the next thread to
pull if this arc gets revisited), and Lynette's `Node132` sits at the END of the same
Bishop-conspiracy arc already documented above (quest 89) as gated on `gvar88`/`gvar82`/`gvar490`
with no lower-stage writer reachable from a fresh character.

**Bottom line:** 529's own "8 sectors scouted" check is a thin, already-mostly-built engine gap
(1 unwired metarule3 rule) riding on top of a genuine, deep campaign prerequisite (VC
citizenship rank 5, granted only by the same story arc blocking quest 89). Filed as the B4
campaign-state track alongside 89 — re-derive when/if that arc becomes drivable; at that point
the metarule3-105 wiring (sketch above) is the only remaining engine work, and it is trivial.

**B4 Task 3 update (2026-07-24): the gate got closer, but NOT reached — hook drafted, verified,
then reverted (outcome-2: no golden lands without `gvar79==5`, so nothing may commit).**

Starting from Task 1+2's end-state (`82=9, 79=4, 81=1, 88=6, 89=4`), re-traced `Node132`'s gate
(`Node130a`: `lvar8>10 && CHA>7`) with the disassembler used for Tasks 1-2 (`scratch/disasm.py`
plus a hand proc-table walk matching `giq_option`'s stack order — `iq, msgListId, msg, proc,
reaction`, popped in that reverse order — against `vclynett.int`'s procedure table to resolve
each option's numeric `proc` target to a Node name). Three sub-findings, in order of how far each
got:

1. **`Node130` (88:=7) IS drivable — new route found.** `Node129` (the same node that completes
   quest 89, `89:=4`) conditionally queues an extra option — `giq_option` gated on
   `obj_is_carrying_obj(dude, 338)` ("Westin Holodisk") — leading to `Node130`, which
   unconditionally destroys the held pid-338 object and sets `88:=7`/`gvar50+=5`. Item 338 is
   NOT created by `SCWestin.getDisk` (verified by disassembly: `getDisk` only sets `89:=3` and
   consumes pid 447, no `create_object`). It comes from a **separate** `SCWestin.int` subroutine,
   `giveDisk` (`create_object(338, ...)` + `add_obj_to_inven`, sets `gvar484:=1`), reachable via
   `Node017`→`Node018`, itself reachable from `Node001`'s dialogue-option list ONLY in the single
   turn where `gvar89==1 && obj_is_carrying_obj(dude,447)` is still true (i.e. the SAME visit
   where `getDisk` fires — one extra dialogue click continues from the getDisk reply into the
   disk hand-back). Driven end-to-end: after the existing 447-delivery click, one more pick
   reaches `Node018`, granting pid 338; returning to Lynette and completing `Node129` then shows
   the extra `Node130` option, which was picked successfully (`88` observed `6→7`).
2. **CHA>7 IS achievable via the real drug pipeline — no engine change needed.** Confirmed via
   the actual game data: Mentats (item pid 53, verified via `pro_item.msg` id 5300) has REAL
   `DrugProtoStats` stats `[4,1,3]`/amounts `[2,2,1]` — i.e. it gives an immediate Charisma
   (stat index 3) +1, alongside INT+2/PER+2. `ApplyDrugEffect`'s per-stat `+=` has no stacking
   cap, so 3 doses (real `--give`/`--use-item`, the sanctioned test-plumbing for a legitimately
   obtainable consumable) raise CHA from the template's 5 to 8 — verified live (a temporary
   diagnostic print, not committed, showed CHA 6→7→8 across the three doses). "Mirrored Shades"
   (pid 433, also checked per the sketch) carry NO drug/armor stat payload in the real data — the
   item's own description ("makes you feel cool") is confirmed flavor-only, not a real CHA route.
3. **`lvar8>10` is the one gate NOT closed.** `lvar8` on Lynette's own script instance is
   incremented (unconditionally, `+1`, no cap) by a scattered set of side-nodes
   (`Node011a/b/c`, `012c`, `018a`, `032a`, `038a`, `052a`, `076b`, `081b`, `082a`, `089a`,
   `103a`). A live instrumented trace (temporary `set_local_var` logging, not committed) proved
   these do NOT fire from the standard 4-topic Q&A hub (`Node011`/`012`/`018`/`032`'s PARENTS —
   asking about the GECK / Vault 13 / "why not live in a vault" / slavery, repeated 15x each,
   zero `lvar8` writes observed) — the hub's 4 topics are a red herring for this gate. One real
   trigger WAS found: `Node123` (part of the raiders/Bishop-holodisk reveal, sets `89:=1`) queues
   a THIRD option, msg 733, gated `has_skill(dude,14)>=75 OR CHA>7`, targeting `Node103a`
   (an `lvar8++` node) — confirmed by disassembly, but NOT confirmed reachable in practice: the
   live "reveal" dialogue actually taken during Task 2's driven route does not appear to pass
   through this exact `Node123` context (the equivalent screen offered only 2 options with no
   CHA-gated 3rd, even with CHA=8 live) — either a different node entirely presents that reveal
   in the real flow, or the option's visibility depends on additional state not yet identified.
   With the effort already spent, the true trigger sequence for accumulating `lvar8` past 10 (11+
   real increments needed) was NOT located. **Open lead for a future session:** map the FULL set
   of callers into `Node103a`/`Node089a`/etc. precisely (six different parent nodes were found
   for `Node103a` alone — `103,105,106,113,115,123` — any one might be the real one under
   slightly different preconditions than tried here) rather than guessing from the static Q&A
   hub.

Per the outcome-2 rule (hook lands only with a working golden), the drafted metarule3 rule-105
wiring (`ScriptHost.SubtileStateProvider` + `IntVm case 0x80E1 rule==105` +
`ViewerGame`'s `WorldFog.StateAt` binding — ~8 lines, build-verified clean, full 37/37 quest
suite confirmed byte-identical with it in place) was **reverted, not committed** — `gvar79`
never reaches 5, so Stark's scouting branch is still never entered and rule 105 is still never
called by any script. No `--set-global` was used on 79/88/89/81 at any point; the only debug
shortcuts were the sanctioned `--give`/`--use-item` (real Mentats, real proto data) and one extra
real dialogue click (no state was poked).

**VC tiles (vctydwtn):** Lydia 26306, Valerie 21096, Moore 17485. **Other tiles:** Lynette
(vctycocl) 17100, Bishop (newr2 elev 2) 17678, Westin (ncr3) 17892. **Item pids:** beer 124,
booze 125, wrench 384, pliers 75, briefcase 336, Lynette holodisk 337, Bishop holodisk 447.
(Maps: VCTYCTYD courtyard, VCTYDWTN downtown, VCTYCOCL council, VCTYVLT.)

**THE DELIVERY PATTERN (reliable quick win):** navigate the NPC's chat chain to the "I'll get
it for you" accept (gvar:=1) → `--give <item pids>` → re-talk, the greeting/info menu gains a
"here's your delivery" option gated on obj_carrying → gvar:=complete. Same shape as mom-meal
(Den), modoc-watch, anna-locket. VC has ~4-5 of these.

Related: [[klamath-qa-sweep]], [[den-qa-sweep]], [[modoc-qa-sweep]], [[p128-quest-path-finder]].
