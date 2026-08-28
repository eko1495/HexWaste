
# Klamath (loc 1502) QA sweep — COMPLETE 6/6

The first town in the campaign-QA arc ([[p134-quest-pilot-calibration]]). 6 quests total —
**ALL 6 DONE (182, 390, 198, 197, 102, 391). KLAMATH SWEEP COMPLETE.** 9 quest goldens (incl. the
pre-existing free-vic/smitty). 391 (Rescue Torr, commit d7d4e42) was the last: the "obscure join
trigger" was really QUEST ACTIVATION — 391 activates via Ardin (Torr's MOTHER, kladwtwn 22885,
1,1,1,1,1,1) but only after 71:=1 displaces Torr; the canyon Torr (KLACANYN 15287) then spawns
gated on BOTH 71:=1 AND 391:=1; talk 1,1 ("let's get out of here")→Node940 follow flag; escort-sim
(--teleport 19450 + --escort-pump)→391:=2. See docs/plan-scripted-map-event.md §11. The full
capability stack (set-hour, escort-sim, load_map scripted events, --critters) reused; no new code. Uses the P128 path finder + the new
--map-objects tile tool. The 198 golden proved the workflow end-to-end: `ProcAnalyze
--map-objects` for the tile, `tools/int_disasm.py <script> --writes <gvar>` for the node→value
writes + `<script> <proc>` for operand-level gates (item pid etc.), live --talk-seq nav (the
`round: opts=N` aid) for the option chain, then the cross-map drive. THE REUSABLE WORKFLOW.

**NEW TOOL (5258d36): `ProcAnalyze --map-objects --map <name>`** — lists every scripted
object as `elev=E tile=T pid=P script=NAME`. This is the tile-discovery aid the pilot said
was the recurring cost. kladwtwn.map tiles: **KCTorr 24291, KCBob 22687, KCSajag 17917,
KCDunton 17500 + 17699, KCArdin 22885** (KCTorr 24291 matches the existing torr-brahmin golden).

**Quest ledger (quests.txt loc 1502 rows: `1502, msg, gvar, display, complete`):**
- gvar **182** msg302 (Guard Torr's brahmin) — DONE golden `quest-torr-brahmin` (--talk-seq 24291 1,1)
- gvar **390** msg304 (Kill the Rat God) — DONE golden `quest-kill-ratgod` (--kill 25486 on klaratcv)
- gvar **198** msg300 complete>=3 (Refuel Whiskey Bob's still) — **DONE golden `quest-bob-still`
  (f3f991a), FULL lifecycle 0→1→2→5.** Recipe: `--give 41:500 --give 286:2` (caps + 2 firewood),
  Bob (kladwtwn 22687) `--talk-seq 22687 2,1,2,1,1,1` (chat→buy drink→"how can I help"→"tell me
  more"→"OK I'll do it") → 198:=1; `--goto-map klatrap.map --use-on 286:20131` (firewood pid 286
  on the Still, klatrap hex 20131) → 198:=2; back to Bob `--talk-seq 22687 1` → Node950 fires on
  greeting → 198:=5 (completed). Wood=pid 286="Firewood"; refuel gated on global(198)==1 + a
  game_time/global458 "gone bad after ~24" branch (harmless on a fresh game). Bob's drink needs
  caps (buying loosens him to offer the job — msg 170).
- gvar **197** msg301 (Rescue Smiley) — **LANDED golden `quest-smiley-rescue`, the FIRST escort
  golden, full lifecycle 0→2→3.** Smiley = klatoxcv elev1 tile 18651; join dialog `1,1,1,1,1`
  (Node970) sets his LVAR follow flag + team. Completion: his `critter_p_proc` opCritterAttempt-
  Placement's him across floors to the dude, then fires leave_player (proc 14) when
  `tile_distance(smiley,18335)<7 AND smiley.elev==0` → 197:=2; kladwtwn map_enter (197==2 →
  197:=3). Recipe: `--teleport 18335 0` (dude to the elev-0 delivery tile) `--escort-pump 18651 8`
  (run Smiley's own heartbeat 8× across elevations) → 197:=2, then `--goto-map kladwtwn.map` →
  197:=3. **THE ESCORT-SIM CAPABILITY (built this session):** `--teleport <tile> <elev>` (dude
  in-place move via SwitchElevationInPlace) + `--escort-pump <followerTile> <beats>` (run one
  critter's critter_p_proc directly, ANY elevation — bypasses the _elevation-scoped
  PumpCritterProcs). **KEY ENGINE FIX:** ElevationOfObject read the STATIC _map.Elevations lists,
  not the LIVE _solidObjects/_flatObjects that PlaceObject/SwitchElevationInPlace update — so a
  runtime-moved critter reported its OLD elevation and the escort's `elev==0` gate never passed.
  Fixed to read the live lists first (no-op for never-moved objects; all goldens byte-identical).
- gvar **391** (Rescue Torr) — escort DELIVERY solved by the escort-sim, but the JOIN is
  EVENT-gated (blocked). Torr's escort is IDENTICAL to Smiley's: LVAR10 follow flag (join =
  Node940), critter_p_proc → leave_player (proc, 391:=2 + 71:=0) when near delivery tile 19450.
  So --teleport+--escort-pump WOULD complete it. BUT you can't reach the join: canyon Torr
  (KLACANYN 15287) is "no critter" on a cold boot AND with --set-global 71 1 (71 = the "Torr
  displaced to canyon" flag Node930 sets). Torr only becomes present/joinable after the klagraz
  grazing-fields EVENT + the Dunton confrontation (Node930, which also completes 102). Agreeing
  to guard at kladwtwn (talk 24291 1,1,1 → 182:=1) does NOT activate 391 or make Torr follow.
- gvar **102** (Duntons) — EVENT-gated, same wall. Completes via Torr Node930 (which also sets
  71:=1, displacing Torr to the canyon for 391). Node930 reachable only post-event. Torr at
  klagraz 24572 not walk-up talkable; the brahmin-guard is a worldmap-arrival map_enter event.

**BOTH 391 + 102 gate on the KLAGRAZ grazing-fields event — REFRAMED + PLANNED in
`docs/plan-scripted-map-event.md`.** It is NOT a worldmap special encounter (earlier wrong
guess). It's a SCRIPTED load_map transition: **KCTorr Node020** does `load_map(14=klagraz,
param=13)`; the param becomes GVAR_LOAD_MAP_INDEX = **gvar 27**, which klagraz map_enter checks
(`global(27)==13`) to set up the confrontation. **load_map (0x80E4) is ALREADY IMPLEMENTED**
(ScriptHost.cs:1760 sets gvar 27 + fires LoadMapRequested → viewer _pendingTransition,
ViewerGame.cs:1233). So the phase is SMALL (S-M, mostly verification): (W1) reach Node020's
dialogue path; (W2) apply the dialogue-deferred _pendingTransition after --talk-seq (the likely
gap — talk-seq closes dialog without applying it, cf. the --pump-ms loop Harness.cs:594); (W3)
drive the klagraz KCTorr/KCDunton confrontation to 71:=1 (the risk — may need combat/--kill);
(W4) 102 via Torr Node018, 391 via the ALREADY-PROVEN escort-sim to the now-present canyon Torr.
Broader payoff: the load_map path unblocks quest climaxes across 5+ regions + the tanker spine
(per docs/CAMPAIGN-PORT-REVIEW.md).

**W1+W2 SPIKE DONE — GO (commit 56c307a).** `--talk-seq 24291 1,1,1` (accept guarding Torr's
brahmin) IS the load_map node — gvar 27 reads 13 right after; `--pump-ms` then applies the
deferred `_pendingTransition` and loads klagraz (--map-update-probe: kladwtwn→klagraz,
newStubbedExternals=0). **W2 needs NO code** (--pump-ms already applies pending transitions,
Harness.cs:594). Transition half is FREE; estimate revised to S. Only W3 remains: on klagraz
post-arrival, 391/102/71 all still 0 and klagraz Torr tile 24572 = "no critter" (map_enter
repositioned/deactivated him), so W3 = drive the KCTorr/KCDunton confrontation to 71:=1 (the
risk — may need combat/--kill or a Dunton dialogue branch). Next: W3 spike (dump klagraz objects
post-arrival, locate Torr+Duntons, drive the scene), then W4 (102 via Torr Node018, 391 via the
proven escort-sim), W5 (goldens).

**SESSION FINDING:** the 3 remaining are NOT walk-up-dialog quests like the still — they're the
interconnected brahmin-drive line (escort Smiley out of the caves; the klagraz grazing-fields
event + escort Torr). Each needs escort-join and/or event firing that isn't currently
harness-drivable — a deeper engine/harness investigation (verify/enable dialog party-add +
map_enter event resolution), not a quick fixture. This is the real "months of QA" tier.

**LIVE FINDING (reconfirms p134):** on a fresh kladwtwn boot, `--kill 17500 --kill 17699`
(both Duntons) leaves 102 & 391 at 0; `--talk-seq 24291 1|2` (Torr) and `--talk-seq 22687 1`
(Bob) leave 391/102/198 at 0. So NONE of the 4 are clean triggers — each dialog branch is
conditional on prereq GVAR/LVAR/party state that a cold boot lacks. Authoring each fixture =
activate the quest via its start branch, satisfy the intermediate state (--set-global/--give/
--set-local as the disassembly dictates), THEN drive the completion + --get-global before/after
+ --quest-probe, and record into scripts/quest-golden.sh. Budget ~20-40 min EACH; genuine
per-quest archaeology, not a fan-out. Easiest next target likely gvar 198 via the KCSajag
Node013 option chain, or 197 once Smiley's toxic-caves map+tile are dumped with --map-objects.

Related: [[p134-quest-pilot-calibration]], [[p128-quest-path-finder]], [[p124-quest-census]],
[[p103-108-quest-e2e-escort-review]] (escort = critter_p_proc heartbeat).

## Walk-up-dialog quest survey (campaign-wide) — the "cheap fixture" reality

Pivoted to find still-like self-contained quests elsewhere. `ProcAnalyze --quest-paths`
(no arg) + `grep "COMPLETES.*=opt"` lists all dialog-option-reachable completions (Den dc*,
Modoc mc*, etc.). BUT surveying them showed the still (198) was UNUSUALLY self-contained.
Every candidate examined has a real prereq gating offer AND/OR completion:
- **Den Anna's locket (551, DCAnna denbus1 28105)**: **LANDED golden `quest-anna-locket`
  (f4ee75f), 0→2 completed.** Was blocked on the night-ghost spawn; FIXED by the new
  `--set-hour <hh>` verb (direct clock jump to hh:00, re-runs map_update — no loop, unlike the
  hanging --advance-ms). Anna visible when game_time_hour <= 400 (her map_update_p_proc). Recipe:
  `--goto-map denbus1.map --give 252:1 --set-hour 2 --talk-seq 28105 2` (give locket pid 252 →
  her obj_carrying check → Node007 → 551:=2). `--set-hour` is the reusable unblock for every
  time-gated NPC/quest.
- **Den Frankie (101, DCFranki denbus2 14716)**: "find where Rebecca gets her booze" (= Bob's
  still). Completion Node012 gated on `global(445) & 16` (still-knowledge bit) — set by
  **Rebecca's dialog** (dcRebecc denbus1 17662, Node004/014/022 bitwise-OR 445). Landable in
  principle (2 walk-up NPCs, no items/escort) but a multi-step gated chain: Frankie's quest
  OFFER itself is hidden (greeting "Info" = dead end; accept branch not found in a few tries).

**CONCLUSION (evidence-backed):** self-contained walk-up quests like the still are rare. Most
gate offer+completion behind knowledge bits / item possession / spawn conditions / cross-NPC
chains, so each is a genuine ~30-60min dialog-navigation investigation — NOT a cheap batch.
The tooling (--map-objects, int_disasm, round-nav) makes each tractable; there's no shortcut
to many fixtures fast. To SCALE the sweep, the real lever is an automated quest-completion
DRIVER (harness mode: given a gvar, use the static path + live dialog nav to auto-attempt),
or a night-jump + party-add/escort-sim to unblock the item/ghost/escort categories.
