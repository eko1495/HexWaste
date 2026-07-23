
# Modoc (loc 1503) QA sweep — 5/5 landable done (105, 106, 110, 631, 693); 108 is a pinned vanilla
# gap excluded from the denominator

Third campaign-QA town (after [[klamath-qa-sweep]] 6/6, [[den-qa-sweep]] 5/7). Same toolset
(--map-objects, int_disasm, --critters, round-nav, escort-sim, set-hour).

**DONE (5):**
- **106** Find Cornelius's gold watch for Farrel — golden `quest-modoc-watch` (b9bea33), 0→4→8.
  Farrel (modinn 25088), accused of the theft, hooks it via his watch-defense greeting
  option chain (opt3,1,1 → 106:=4); carry the gold watch (item **257**) → his greeting gains an
  opt4 confirm route → 106:=8. Watch = --give shortcut (found in the outhouse). 106 = Farrel-side;
  105 = Cornelius-side.
- **105** Cornelius's side of the same watch quest — golden `quest-cornelius-watch`, 0→4→8. The
  activation write (105:=4) sits behind a dedicated Cornelius sub-branch (mcCornel Node010→Node001
  "ask more questions" loop→Node017→Node018→Node019, its own accusation-acceptance chain). The real
  Node001 guard (0x35a2–0x35c2 in mccornel.int) is `105==0 OR 106==0` (inclusive-or), not "both 105
  and 106 are still 0" — the branch stays reachable while 105==0 regardless of Farrel's (106) state.
  In practice the LANDED scenario talks Cornelius first (that ordering is sufficient and
  replay-proven), but it is not script-required by the guard itself. Once 105:=4, accusing Farrel
  (106:=4, same chain as 106's golden) and returning to Cornelius carrying the watch reaches his
  post-first-visit greeting (mcCornel Node002, entered because a per-critter visit counter is now
  nonzero); its report option (mcCornel Node024/Node025, the same node Farrel's completion shares)
  reads 105's CURRENT value before writing — 105<4 stalls at :=3 (undone), 105 in [4,7) completes to
  :=8. The >15 prior attempts always did the Farrel/watch steps first, so 105 was still 0 and every
  completion attempt landed on the :=3 stall — this is what unblocked it. A parallel activation
  branch, mcCornel Node016→Node020 (and Node021→Node022), writes 105:=4 under the same Node001 guard
  as the documented Node017→Node018→Node019 chain — two flavor routes to the same effect. Full
  lifecycle 0→4→8, real dialogue only; disasm-driven (mccornel.int giq_option operand trace, no
  --set-global).
- **110** Farrel's garden rats — golden `quest-modoc-rats` (788f8b7), 0→4→8. Same greeting as 106,
  other branch (2,2,1). Completion discriminator is a shared per-map rat counter (mcRat, 10
  instances on modgard) decremented in destroy_p_proc; hitting zero sets GVAR 297 bit 0x80, which
  Farrel's report option branches on for the confirmed (:=8) vs undone (:=3) write.
- **631** Ghost farm / Slag investigation — golden `quest-modoc-ghostfarm` (see quest-golden.sh),
  drives Jo (mcJo, modmain 20143) with item pid 263.
- **693** Jonny missing — golden `quest-jonny-rescue`, 0→1→2 (display≥1, completed≥1). Balthas
  (mcBaltha, modmain 12323) has a personal-topic greeting branch (surfaces the missing-son thread,
  writes 693:=1) gated on live Perception ≥6 — a real script stat check, not IQ. The mandated
  chargen SPECIAL is 5/5/5/5/5/5/5 (PE=5), so the branch is hidden by default; cleared honestly by
  giving + using a Mentats dose (pid 53, +1 PE while active — a real in-game mechanic, not
  --set-global). Completion is the found-BB-gun branch: item pid 261 (origin: mcbaltha msg 172)
  carried back to Balthas, reported on his follow-up greeting → 693:=2, completed. The real Jonny/
  Vegeir NPCs live on the Ghost Town maps (gstfarm/gstcav1/gstcav2 — NOT any "mod*"-named map;
  the modmain mcJonny/mcBaltha placements are decoy/hidden objects destroyed on map entry) and
  offer an alternate (unbanked) direct-rescue route reaching 693:=3 via Jonny's father-name quiz
  (Node014, Balthas answer, no skill roll) — not needed once the BB-gun report already completes
  the quest; left as a documented alternate, not a second golden.

**VANILLA GAP (excluded from the landable denominator):**
- **108** Tell Karl in the Den it's OK to come back — `ProcAnalyze --quest-paths 108` reports
  `writes=0` across all 1263 scripts (re-verified 2026-07-23); the P124 quest-census
  (`ProcAnalyze --quest-census`) independently pins gvar 108 as one of its 3 vanilla content gaps
  (alongside 396 power-plant and the capped 370 Jet-source), with a spot-check line confirming
  `constWrites=0 values=[] scripts=[]`. No script in the shipped game ever sets this gvar — the
  quest is unfinishable by design, not by an engine limitation. No golden is possible; counted 6
  quests for Modoc; 5 landable — all landed — plus 108, a pinned vanilla gap no script can complete.
  See [[p124-quest-census]] (P124 quest census — pinned vanilla gaps 108/396/370).

**Tiles/items:** Farrel modinn 25088, Cornelius modinn 13490, garden rats modgard (14494 14696
16892 17098 17680 18684 21899 22894 23887), molerat modshit 9901. Watch = item 257. Balthas
modmain 12323, BB gun = item 261, Mentats = item 53. Real Jonny gstcav2 24517, real Vegeir
gstcav1 26502 (Ghost Town maps, not the "mod*" set).

**PATTERN (3rd town):** confirmed again — a couple clean item-return/delivery wins per town, the
rest multi-step. Quest goldens now span Klamath(6)+Den(5→ has extras)+Modoc(105/106/110/631/693)
+opening; Modoc closes at 5/5 landable + 1 pinned vanilla gap (108).

Related: [[klamath-qa-sweep]], [[den-qa-sweep]], [[p128-quest-path-finder]].
