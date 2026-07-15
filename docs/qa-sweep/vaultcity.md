
# Vault City (loc 1504) QA sweep — 3/10 done

Fourth campaign-QA town. VC is DELIVERY-heavy (many "bring X to Y" quests = the tractable
tier), so a good source of quick goldens. Same toolset.

**DONE (2):**
- **497** Deliver beer & booze to Lydia — golden `quest-lydia-booze` (1a67282), 0→1→2. Lydia
  (VCDwnBar, vctydwtn 26306); "what's on tap→real alcohol" chain `1,1,1,1,1,1` → she wants 10
  each → 497:=1; carry 10 beer (124) + 10 booze (125), info menu opt6 "I have that shipment"
  (`2,6` → Node032 obj_carrying 124+125) → 497:=2.
- **493** Deliver tools to Valerie — golden `quest-valerie-tools` (5bb69f6), 0→1→2. Valerie
  (VCMainWk, vctydwtn 21096, grumpy maintenance); repair chain `1,1,1,1,1,1,1` → 493:=1; carry
  wrench (384) + pliers (75), greeting `1,1` "You have tools?" (Node023) → 493:=2.

- **80** Get a plow for Mr Smith — golden `quest-smith-plow` (7518c6b), 0→3→6. 2-NPC purchase
  chain: Smith (vctyctyd 14078) accept `2,1,1` + commit on re-talk `4,1,1` ("I'll take the money"
  → 80:=3, unlocks Harry); Harry (VCHarry 12513) offers "still selling that plow?" ONLY at 80>=3,
  buy for $800 `2,2,1` → "Drop it off with the Smiths" → 80:=6. GOTCHA: the cross-NPC option is
  gvar-gated (Harry's plow line hidden until Smith sets 80:=3) — needed the Smith re-talk to
  advance past :=1. Caps via --give 41:1000.

**REMAIN (7):**
- **85** Deliver jet sample to Dr Troy (VCDrTroy vctyvlt 13084) — STORY-GATED, NOT a cold-boot
  delivery: Troy only offers "Nothing today" even with jet (item 259) in hand. Needs prior jet/
  drug-problem context (from the Den/Redding storyline). Skip until that context is settable.
- **321** Deliver Moore's briefcase to Bishop in NEW RENO — cross-town (harder).
- **89** Deliver Lynette's holodisk to Westin in NCR — cross-town.
- **82** Solve the Gecko powerplant problem — big multi-step (also Gecko quest).
- **459** Rescue Amanda's husband Joshua — escort (use the escort-sim).
- **529** Scout 8 sectors around Gecko + enter NCR — worldmap/Stark recon.

**VC tiles (vctydwtn):** Lydia 26306, Valerie 21096. **Item pids:** beer 124, booze 125,
wrench 384, pliers 75. (Maps: VCTYCTYD courtyard, VCTYDWTN downtown, VCTYCOCL council, VCTYVLT.)

**THE DELIVERY PATTERN (reliable quick win):** navigate the NPC's chat chain to the "I'll get
it for you" accept (gvar:=1) → `--give <item pids>` → re-talk, the greeting/info menu gains a
"here's your delivery" option gated on obj_carrying → gvar:=complete. Same shape as mom-meal
(Den), modoc-watch, anna-locket. VC has ~4-5 of these.

Related: [[klamath-qa-sweep]], [[den-qa-sweep]], [[modoc-qa-sweep]], [[p128-quest-path-finder]].
