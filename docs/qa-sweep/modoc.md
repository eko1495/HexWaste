
# Modoc (loc 1503) QA sweep — 4/6 done (106, 110, 631, 693)

Third campaign-QA town (after [[klamath-qa-sweep]] 6/6, [[den-qa-sweep]] 5/7). Same toolset
(--map-objects, int_disasm, --critters, round-nav, escort-sim, set-hour).

**DONE (4):**
- **106** Find Cornelius's gold watch for Farrel — golden `quest-modoc-watch` (b9bea33), 0→4→8.
  Farrel (modinn 25088), accused of the theft, hooks it via his watch-defense greeting
  option chain (opt3,1,1 → 106:=4); carry the gold watch (item **257**) → his greeting gains an
  opt4 confirm route → 106:=8. Watch = --give shortcut (found in the outhouse). 106 = Farrel-side;
  105 = Cornelius-side (advances to 3 when 106 done).
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
  --set-global). Completion is the found-BB-gun branch: item pid 261 (bottom of the old well)
  carried back to Balthas, reported on his follow-up greeting → 693:=2, completed. The real Jonny/
  Vegeir NPCs live on the Ghost Town maps (gstfarm/gstcav1/gstcav2 — NOT any "mod*"-named map;
  the modmain mcJonny/mcBaltha placements are decoy/hidden objects destroyed on map entry) and
  offer an alternate (unbanked) direct-rescue route reaching 693:=3 via Jonny's father-name quiz
  (Node014, Balthas answer, no skill roll) — not needed once the BB-gun report already completes
  the quest; left as a documented alternate, not a second golden.

**REMAIN (task #58 tracks Modoc):**
- **105** Cornelius's watch = SAME QUEST AS 106 (mcCornel Node024 sets BOTH 105:=8 + 106:=8).
  Already covered by the 106 golden. Tried hard to land 105 separately (~15 nav attempts):
  Cornelius (modinn 13490) is a deliberately-scatterbrained dementia-LOOP. With the watch (257)
  carried AND Farrel's accusation heard (Farrel 3,1,1 → 106:=4), Cornelius `2,1,1` reaches an
  8-option greeting with opt6 "found it at Farrel's" (frame) + opt7 "industrious rat made off"
  (truth). BUT both only set 106:=8 and leave 105 at 3 (below its display>=4!) — Cornelius stays
  suspicious ("what are you doing with that?"), you keep the watch. The real 105-completing node
  (Node024, where he TAKES the watch happy) is behind a different option whose LIVE index ≠ the
  static-graph opt2 (IQ-filter ordinal drift, P128 caveat) — not landed. VERDICT: watch quest is
  covered by 106; 105 is the alternate same-quest side, finicky, low marginal value. Parked.
- **108** Tell Karl in the Den it's OK to come back (check: was 108 a P124 vanilla gap? verify).

**Tiles/items:** Farrel modinn 25088, Cornelius modinn 13490, garden rats modgard (14494 14696
16892 17098 17680 18684 21899 22894 23887), molerat modshit 9901. Watch = item 257. Balthas
modmain 12323, BB gun = item 261, Mentats = item 53. Real Jonny gstcav2 24517, real Vegeir
gstcav1 26502 (Ghost Town maps, not the "mod*" set).

**PATTERN (3rd town):** confirmed again — a couple clean item-return/delivery wins per town, the
rest multi-step. Quest goldens now span Klamath(6)+Den(5→ has extras)+Modoc(106/110/631/693)+opening.

Related: [[klamath-qa-sweep]], [[den-qa-sweep]], [[p128-quest-path-finder]].
