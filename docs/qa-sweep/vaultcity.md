
# Vault City (loc 1504) QA sweep — 5/10 done

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

**REMAIN (4):**
- **85** Deliver jet sample to Dr Troy (VCDrTroy vctyvlt 13084) — STORY-GATED, NOT a cold-boot
  delivery: Troy's greeting stays at the no-quest-available baseline even with jet (item 259) in
  hand. Needs prior jet/drug-problem context (from the Den/Redding storyline). Skip until that
  context is settable.
- **89** Deliver Lynette's holodisk to Westin in NCR — STORY-GATED, tier **B4** (campaign-state
  fixture track), NOT reachable from a cold boot. Gate map (Task 2): Westin's own accept option
  (`scwestin.int` `Node001`, msg 113, `=> Node017 => getDisk`) requires exactly `gvar89==1` AND
  `obj_carrying_pid_obj(dude,447)` — that half is fine (447 is the sanctioned `--give` item). The
  blocker is upstream, on Lynette's side: her hub (`vclynett.int` `Node053`, VCLynett vctycocl
  17100) only offers the safe-location reveal option (msg 394, `=> Node136 => Node116/Node119
  => Node119a/Node123`, writes gvar89:=1/2) when `gvar88==5` AND carrying 447. `gvar88` is set to
  5/6/7 only inside `vclynett.int` itself (`Node114`/`Node116`/`Node130` — no lower-stage writes
  anywhere in the script), so stages 1-4 are driven by another script entirely. Worse, even the
  *prior* raiders-intelligence options (msg 392/393, requiring `gvar88==4`) and the Gecko-powerplant
  topic that leads into them (msg 391, requiring `gvar82==2` or `gvar82>3`, `gvar490==0`) are
  gated on `gvar82` — the SAME gvar that tracks quest **82** (Gecko powerplant, below) already on
  this town's REMAIN list. So 89 is chained behind quest 82's own progress via a shared
  raiders/Bishop-conspiracy arc; none of gvar82/88/490 are settable by dialogue alone from a fresh
  character and none may be faked via `--set-global`. Confirmed empirically: with item 447 given,
  Lynette's hub still shows only the 3 baseline options (ask-questions / citizenship / nevermind)
  — no raiders/Gecko/Bishop-safe branch appears for a cold-boot character.
- **82** Solve the Gecko powerplant problem — big multi-step (also Gecko quest).
- **529** Scout 8 sectors around Gecko + enter NCR — worldmap/Stark recon. See **529 verdict**
  below: needs real campaign machinery (the citizenship/conspiracy story arc), not an engine gap.

**529 verdict (2026-07-21):** **Outcome 3 — needs real new machinery (a campaign prerequisite
chain, not an engine subsystem). No code shipped.**

Static trace (`vcstark.int`, all writers found via `--quest-paths 529`): every write lives in
Sgt. Stark's own script. `Node054a`/`Node055a`/`Node055b` set `529:=1` (accepted); `Node057`
sets `529:=2` (COMPLETES for display>=1/completed>=2, +300 caps +350 xp); `Node059a`/`Node059b`
set `529:=3`; `Node061` sets `529:=4` (COMPLETES for display>=3/completed>=4, +750 xp, an item
`create_object` pid 59 + 500 caps). `Node054`/`Node055`/`Node064` gate the "scouted enough"
option on an 7-term AND chain of `metarule3(rule=105, x, y, 0) > 1` calls — rule 105 is
`METARULE3_WM_SUBTILE_STATE` (`interpreter_extra.cc:1995`, `wmSubTileGetVisitedState`), i.e. the
worldmap per-subtile UNKNOWN(0)/KNOWN(1)/VISITED(2) fog state at 7 specific world-pixel
coordinates around Gecko/NCR ((1224,171),(1274,172),(1323,173),(1224,223),(1324,225),
(1224,274)/(1275,274)/(1325,273) — 3rd triple only in the display>=1 group). `Node055` also
reads a plain counter `gvar 82 <8 / ==8 / >8` for flavour text (not a gate).

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

**VC tiles (vctydwtn):** Lydia 26306, Valerie 21096, Moore 17485. **Other tiles:** Lynette
(vctycocl) 17100, Bishop (newr2 elev 2) 17678, Westin (ncr3) 17892. **Item pids:** beer 124,
booze 125, wrench 384, pliers 75, briefcase 336, Lynette holodisk 337, Bishop holodisk 447.
(Maps: VCTYCTYD courtyard, VCTYDWTN downtown, VCTYCOCL council, VCTYVLT.)

**THE DELIVERY PATTERN (reliable quick win):** navigate the NPC's chat chain to the "I'll get
it for you" accept (gvar:=1) → `--give <item pids>` → re-talk, the greeting/info menu gains a
"here's your delivery" option gated on obj_carrying → gvar:=complete. Same shape as mom-meal
(Den), modoc-watch, anna-locket. VC has ~4-5 of these.

Related: [[klamath-qa-sweep]], [[den-qa-sweep]], [[modoc-qa-sweep]], [[p128-quest-path-finder]].
