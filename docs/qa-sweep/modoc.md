
# Modoc (loc 1503) QA sweep — started, 1/6 done

Third campaign-QA town (after [[klamath-qa-sweep]] 6/6, [[den-qa-sweep]] 5/7). Same toolset
(--map-objects, int_disasm, --critters, round-nav, escort-sim, set-hour).

**DONE (1):**
- **106** Find Cornelius's gold watch for Farrel — golden `quest-modoc-watch` (b9bea33), 0→4→8.
  Farrel (modinn 25088), accused of the theft, hooks it via his watch-defense greeting
  `3,1,1` ("Watch?…Would you help?" → 106:=4); carry the gold watch (item **257**) → his greeting
  gains opt4 "Is this the watch?" → "Yes, this is it!" → 106:=8. Watch = --give shortcut (found in
  the outhouse). 106 = Farrel-side; 105 = Cornelius-side (advances to 3 when 106 done).

**REMAIN (5, task #58 tracks Modoc):**
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
- **110** Farrel's garden rats. GOTCHA: agreeing to help Farrel (2,1,1 "I'll help anyway") does
  NOT set 110; killing all modgard rats did NOT move 110 or gvar 297. The report opts are Farrel
  opt6 (msg166 "cleared the vermin") / opt7 (msg169 "All furries dead") but they need 110 active
  first. The activation + rat-death tracking need proper tracing (not the obvious kill-quest).
- **693** Jonny missing → Slag caves (rescue/escort — likely escort-sim).
- **108** Tell Karl in the Den it's OK to come back (check: was 108 a P124 vanilla gap? verify).
- **631** Ghost farm / Slag investigation (multi-stage 1→2→3→4→5).

**Tiles/items:** Farrel modinn 25088, Cornelius modinn 13490, garden rats modgard (14494 14696
16892 17098 17680 18684 21899 22894 23887), molerat modshit 9901. Watch = item 257.

**PATTERN (3rd town):** confirmed again — a couple clean item-return/delivery wins per town, the
rest multi-step. 13 quest goldens total across Klamath(6)+Den(5→ has extras)+Modoc(1)+opening.

Related: [[klamath-qa-sweep]], [[den-qa-sweep]], [[p128-quest-path-finder]].
