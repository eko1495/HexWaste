
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
- **89** Deliver Lynette's holodisk to Westin in NCR — STORY-GATED, tier **B4** (campaign-state
  fixture track), NOT reachable from a cold boot. Gate map (Task 2): Westin's own accept option
  (`scwestin.int` `Node001`, msg 113, `=> Node017 => getDisk`) requires exactly `gvar89==1` AND
  `obj_carrying_pid_obj(dude,447)` — that half is fine (447 is the sanctioned `--give` item). The
  blocker is upstream, on Lynette's side: her hub (`vclynett.int` `Node053`, VCLynett vctycocl
  17100) only offers the "Bishop's safe" reveal option (msg 394, `=> Node136 => Node116/Node119
  => Node119a/Node123`, writes gvar89:=1/2) when `gvar88==5` AND carrying 447. `gvar88` is set to
  5/6/7 only inside `vclynett.int` itself (`Node114`/`Node116`/`Node130` — no lower-stage writes
  anywhere in the script), so stages 1-4 are driven by another script entirely. Worse, even the
  *prior* "raiders info" options (msg 392/393, requiring `gvar88==4`) and the Gecko-powerplant
  topic that leads into them (msg 391, requiring `gvar82==2` or `gvar82>3`, `gvar490==0`) are
  gated on `gvar82` — the SAME gvar that tracks quest **82** (Gecko powerplant, below) already on
  this town's REMAIN list. So 89 is chained behind quest 82's own progress via a shared
  raiders/Bishop-conspiracy arc; none of gvar82/88/490 are settable by dialogue alone from a fresh
  character and none may be faked via `--set-global`. Confirmed empirically: with item 447 given,
  Lynette's hub still shows only the 3 baseline options (ask-questions / citizenship / nevermind)
  — no raiders/Gecko/Bishop-safe branch appears for a cold-boot character.
- **321** Deliver Moore's briefcase to Bishop in NEW RENO — cross-town (harder).
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
