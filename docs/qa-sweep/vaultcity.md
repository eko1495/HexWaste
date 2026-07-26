
# Vault City (loc 1504) QA sweep — 8/10 done (B4 arc CLOSED; 85 remains open, precisely gated)

Fourth campaign-QA town. VC is DELIVERY-heavy (many "bring X to Y" quests = the tractable
tier), so a good source of quick goldens. Same toolset.

**DONE (8):**
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
- **89** Deliver Lynette's holodisk to Westin in NCR — golden `quest-lynette-holodisk` (B4 Task
  2), 0→1→3→4 (COMPLETES). Cross-town chain through the Raiders2.map special encounter
  (`gvar88`) and NCR's SCWestin; full gate trace in the **89 detail** appendix below.
- **82** Solve the Gecko powerplant problem — golden `quest-gecko-powerplant` (B4 arc),
  0→2→5→6→7→9, cross-town VC/Gecko chain; also grants VC Citizenship (79:=4, 81:=1) via
  McClure; full recipe in [[gecko-qa-sweep]], VC-side summary in the **82 detail** appendix below.
- **529** Scout 8 sectors around Gecko + enter NCR — golden `quest-stark-scout`, 0→1→2 (row 1)
  and 0→3→4 (row 2), BOTH rows COMPLETE in one Sgt. Stark conversation. Closes the B4 arc's
  last open lead (the `lvar8>10` citizenship gate); full trace in the **529 verdict UPDATE**
  below. Note: `quests.txt` loc 1504 has 10 rows over 9 distinct gvars — 529 spans TWO rows
  (desc 508 = its 1/2 tier, desc 509 = its 3/4 tier), which is why this file enumerates 9
  quests against a /10 denominator; that mismatch is expected, not a miscount.

**REMAIN (1):**
- **85** Deliver jet sample to Dr Troy (VCDrTroy vctyvlt 13084) — STORY-GATED, gate now FULLY
  TRACED (B4 Task 4, no golden landed — see **85 verdict** below for the precise resume path).

**89 detail (B4 Task 2):** Task 1's trace above correctly found no vc/vi/gc/gs writer for
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
`destroy_p_proc`, only the cause-of-death is a debug shortcut); the golden's driven kill list is
14 tiles (task-2-report.md's "downstream contract"), i.e. 14 of the 17 raider critter objects
actually found placed on the map — one more than the 13 kills strictly needed to cross `377<=5`
from its seed of 18 (18−13=5), a margin kill rather than a miscount. The 18-seed-vs-17-placed-
objects gap itself is not reconciled by any B4 report on hand — noted here as an observed,
unexplained delta (possibly a roster entry that isn't materialized as a killable object) rather
than invented away.

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

**82 detail (B4 arc):** filed as a Gecko-town quest ([[gecko-qa-sweep]] has the full recipe);
VC's McClure/Lynette/Randal legs are the VC side of the same cross-town chain.

**85 verdict (B4 Task 4, 2026-07-24): NOT landed within the 30-min drive timebox — gate fully
disassembled, precise resume path recorded (state/IDs only).**

`VCDrTroy` (`vctyvlt` 13084) `talk_p_proc` on a cold-boot character (no party members matching
its two special-case PIDs, `gvar85==0`, `lvar4==0`, `lvar7==0`) falls through to its `Node044`
hub unconditionally (the earlier "cold-boot inert" finding was correct for the *default* greeting
content, but the hub DOES conditionally queue extra options — it isn't a dead end). Among
`Node044`'s `giq_option` list, msg-291 targets `Node026`, gated
**`gvar85 < 4 AND gvar370 == 3`** — this is the quest's real accept branch (`Node026` →
`Node027` → `Node028` → … eventually reaching `Node019`, confirmed via `--writes 85` as the
node that sets `85:=1`). The other hub options are carrying/stage-gated follow-ons: msg-292 →
`Node044a` needs `85==2 && obj_is_carrying_obj(dude,259)` (jet pid 259, the actual sample
hand-off); msg-293/294 are revisit branches for `85==1`/`85==1||85==2`.

`gvar370` (quests.txt loc 1507, the previously-pinned "Jet-source caps at 3 vs 4" vanilla gap
from `p124-quest-census`) is written **only** by `nhMyron.int` — Myron, New Reno's Jet chemist —
via a Science-skill-gated dialogue chain: `Node239` sets `370:=1` (unconditional once reached,
gated only on `370==0`); its own follow-up option to `Node240` (`370:=2`) requires
`has_skill(dude,12/*Science*/) > 50`; deeper in the same script, `Node131`→`Node132`→`Node133`
(`370:=3`, unconditional at entry once `370<3`) is reached only through options gated
`has_skill(dude,12) > 75` (one branch) or `> 80` (an alternate, better-reward branch) earlier in
the chain. **370==3 is exactly what `Node026` needs — no vanilla gap blocks this specific
threshold**, only the science-skill climb to reach it.

Checked whether a single fresh `--create` character can clear the `>75` threshold: per
`SkillSet.Value` (`src/Hexwaste.Formats/Combat/SkillSet.cs`), Science is `def=0, statMod=4,
stat1=INT(4)`; untagged, `value = 4×INT` (max 40 at INT 10); tagged, `value = 4×INT + basePts + 20`
where `basePts` is skill points spent post-creation (0 at creation) — so even an INT-10,
Science-tagged character caps at **60** at the moment of character creation, short of the `>75`
node-access gate. Closing it needs real skill-point investment from leveling (XP/level-ups spent
on Science) before visiting Myron, not just a stat/tag choice at `--create` time — out of scope
for a single timeboxed drive attempt. No `--set-global` was used anywhere in this trace; the gate
was established purely by static disassembly (`scratch/disasm.py`, `tools/int_disasm.py --writes`).

**Resume path for a future session:** (1) `--create` a Science-tagged build with INT 10 (or plan
to level once before engaging Myron); (2) locate Myron via `ProcAnalyze --map-objects` on New
Reno's maps (script `nhMyron.int`, not yet tile-located this session); (3) drive his dialogue to
`Node133` (`370:=3`) — the `Node239`→`Node240` low-skill leg is easy, the `Node131`/`132` leg is
the real skill wall; (4) confirm jet (pid 259) is obtainable without `--give`-as-a-crutch (Myron
himself is the in-fiction source — not yet confirmed which of his nodes hands it over, or whether
it must be bought/found elsewhere); (5) return to `vctyvlt` 13084, take `Node026`'s option
(`85:=1`), continue `Node027`→`Node028`→…→`Node019`, then the carrying-259-gated `Node044a`
branch (`85:=2`) and onward to the `quests.txt` completed threshold (`85>=3`).

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
   diagnostic print, not committed, showed CHA 6→7→8 across the three doses). Mirrored Shades
   (pid 433, also checked per the sketch) carry NO drug/armor stat payload in the real data — the
   item's own description (`pro_item.msg` 43301) is confirmed flavor-only, not a real CHA route.
3. **`lvar8>10` is the one gate NOT closed.** `lvar8` on Lynette's own script instance is
   incremented (unconditionally, `+1`, no cap) by a scattered set of side-nodes
   (`Node011a/b/c`, `012c`, `018a`, `032a`, `038a`, `052a`, `076b`, `081b`, `082a`, `089a`,
   `103a`). A live instrumented trace (temporary `set_local_var` logging, not committed) proved
   these do NOT fire from the standard 4-topic Q&A hub (`Node011`/`012`/`018`/`032`'s PARENTS,
   repeated 15x each, zero `lvar8` writes observed) — the hub's 4 topics are a red herring for
   this gate. One real
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

**529 verdict UPDATE (2026-07-26): CLOSED — golden `quest-stark-scout` lands both 529 rows,
0→1→2 and 0→3→4, in one session. The `lvar8>10` gate (this arc's last open lead) was a plain
farm loop, not new machinery.**

Resuming the Task 3 end-state (`82=9,79=4,81=1,88=6,89=4`, CHA=8 via 3 real Mentats doses):
`lvar8` on Lynette's script instance turned out to be incremented by the SAME node family
reached through her Q&A hub's normal option 1 branch — `Node087→Node099→Node103→Node103a`,
each visit `+1`, no cap — repeating the identical option sequence (`--talk-seq 17100
1,3,2,2,1`) 11 times crosses `lvar8>10` (the Task 3 report's search of the 4 Q&A-hub topics
under `Node011`/`012`/`018`/`032` was the wrong branch; the real incrementer sits one level
into option 1's OWN sub-branch, not a sibling topic). With `lvar8>10 && CHA>7` both true,
`Node130a`'s gate passes on the next council-hub visit (`--talk-seq 17100 3,4,1,2`) → `Node132`
fires: `79:=5` (+2500 xp, `81:=1`, `50+=10`).

With `gvar79==5`, Sgt. Stark (`vctydwtn` 12674) now routes past the generic 2-option greeting
into his full job hub. Static disassembly of `vcstark.int` (`scratch/disasm.py`, every node
from the greeting through the completion chain, cross-checked against `VCSTARK.MSG` for
option-target identification — never for committed text) mapped the exact click path BEFORE
any live run: greeting → `Node015`(opt1) → `Node016` (the job hub; opt2 = the recon-job topic,
gated `gvar529==0`) → `Node050`(opt1) → `Node051`(opt1) → `Node052`(opt1) → `Node053`(opt1) →
`Node054` — the accept node, which unconditionally does `mark_area_known(area 5)` (this is
what makes the 8 sector tiles reachable via `--travel-from ... 5`) and independently AND-chains
the same 8-term `metarule3(105,x,y,0)>1` check documented above. Because this session's
`--travel-from` legs for all 8 coords run BEFORE the FIRST-EVER Stark visit (reusing
`WorldmapTravel.Step`'s real leg-start `MarkRadiusVisited`, no engine change beyond the rule-105
wire itself), the chain is already true on accept, so `Node054`'s own success branch
(opt2 → `Node056`) fires immediately: `Node056`(opt1) → `Node056a`(auto, fade+call) →
`Node057` — `529:=2` (row 1 COMPLETES, +300 caps +350 xp) and offers to continue (opt1) →
`Node058` — row 2's setup node, unconditionally `mark_area_known(area 10)`, then checks
`gvar540`.

`gvar540` needed one more real find, since neither `ncr3.map` nor Westin's own script
(`SCWestin.int`) ever write it: a full 1448-script `set_global_var` sweep (every `.int` in
`master.dat`/`patch000.dat`) found exactly one writer, `NCRENT.int` — a DEDICATED NCR-entrance
transition map (`NCRENT.MAP`, not one of the four town submaps `ncr1-4.map`), whose
`map_update_p_proc` sets `540:=1` unconditionally on `dude_elevation==0 && 540==0`. This
engine's map-enter path already fires one `map_update_p_proc` pass immediately after
`map_enter_p_proc` (the existing `RunMapEnter`→`RunMapUpdate` order in `ScriptHost.cs`), so a
plain `--goto-map NCRENT.map` sets `540:=1` with zero dialogue, zero pump needed. With `540==1`
already true, `Node058`'s row-2 branch goes straight to its success option (opt1 → `Node060`,
skipping the `Node059` incomplete-task detour entirely) → `Node060`(opt1) → `Node060a`(auto,
fade+call) → `Node061` — `529:=4` (row 2 COMPLETES, +500 caps +750 xp +item pid 59, `50+=3`).

Full Stark click sequence, one continuous conversation: `1,1,2,1,1,1,1,2,1,1,1,1` (12 clicks).
No `--set-global` anywhere on 79/529/540 or any intermediate gvar; the only debug shortcuts are
the sanctioned `--give`/`--use-item` (real Mentats) already used in Task 3, plus `--travel-from`
(real `WorldmapTravel` legs) and `--goto-map NCRENT.map` (a real, loadable map, its
`map_update_p_proc` doing exactly what the original engine would do on a real border crossing).

**VC tiles (vctydwtn):** Lydia 26306, Valerie 21096, Moore 17485. **Other tiles:** Lynette
(vctycocl) 17100, Bishop (newr2 elev 2) 17678, Westin (ncr3) 17892. **Item pids:** beer 124,
booze 125, wrench 384, pliers 75, briefcase 336, Lynette holodisk 337, Bishop holodisk 447.
(Maps: VCTYCTYD courtyard, VCTYDWTN downtown, VCTYCOCL council, VCTYVLT.)

**B4 arc closeout (final, 2026-07-26):** the Gecko-powerplant/Bishop-conspiracy/citizenship arc
is now a FULLY drivable recipe end to end — three goldens (`quest-gecko-powerplant` 82,
`quest-lynette-holodisk` 89, `quest-stark-scout` 529) land it with zero `--set-global`, ending
at `82=9, 79=5, 81=1, 88=6, 89=4, 484=2, 529=4, 540=1`. The metarule3 rule-105
(`WM_SUBTILE_STATE`) hook drafted-then-reverted in Task 3 is now wired for real (37/37 existing
goldens stayed byte-identical with it in place before this golden was added). `85` (Dr Troy's
jet sample) is a SEPARATE, unrelated gate — not part of this conspiracy arc at all — traced to a
cross-town New Reno (Myron/`nhMyron.int`) Science-skill dialogue chain; see the 85 verdict above
for its resume path. It is the one remaining VC quest, and it is not an engine gap — a real,
precisely-scoped campaign-content investigation for a future session.

**THE DELIVERY PATTERN (reliable quick win):** navigate the NPC's chat chain to the "I'll get
it for you" accept (gvar:=1) → `--give <item pids>` → re-talk, the greeting/info menu gains a
"here's your delivery" option gated on obj_carrying → gvar:=complete. Same shape as mom-meal
(Den), modoc-watch, anna-locket. VC has ~4-5 of these.

Related: [[klamath-qa-sweep]], [[den-qa-sweep]], [[modoc-qa-sweep]], [[p128-quest-path-finder]].
