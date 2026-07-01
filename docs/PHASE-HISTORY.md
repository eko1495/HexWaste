# Hexwaste — Phase History

This is the append-only milestone changelog for the Fallout 2 map-viewer PoC,
moved out of CLAUDE.md (which has a 150k-char context limit). It is reference
history — the durable build guidance lives in CLAUDE.md. Phases are
cross-referenced as `P<phase>-M<milestone>` throughout the codebase and docs.

## Milestones

Phase 1 (DONE): M1 DAT2 reader, M2 PAL+FRM, M3 MAP parsing, M4 static floor render, M5 objects + z-sorting + roofs, M6 palette cycling.

Phase 2 — "walking simulator" (NO combat, NO script VM — hard scope line):
- **P2-M0** (DONE): benchmark newr1.map (2841 objects, heaviest): avg 3.6 / p95 6.2 / max 13.6 ms full frame with cycling — under 16 ms. **DECISION: CPU palette conversion stays; no shader, no Wine.** Wall-time sim, fixed 60 Hz update; `--bench N` measures uncapped cost.
- **P2-M1**: static critters — FID→FRM via critters.lst + anim-code suffix (art.cc artBuildFilePath/_art_get_code), correct direction, z-sorted with solids.
- **P2-M2**: idle/breath + walk-in-place (animation.cc); FRM frame offsets accumulate across frames.
- **P2-M3**: mouse picking — per-pixel alpha hit-test in reverse draw order (object.cc, tile.cc screen↔hex).
- **P2-M4**: dude movement — A* on hex grid (path.cc), blocking objects, camera follow.
- **P2-M5**: hardcoded interactions, no VM — doors, exit grids (map/elevation transition), stairs/ladders.

Phase 3 (DONE): M0 AAF fonts + MSG + examine; M1 static lighting (LightGrid port incl. the 36-case occlusion switch; CPU tints — per-object exact, per-square floor approximation); M2 worldmap travel (city.txt/maps.txt name lookup); M3 sound (full ACM decoder port, door sfx names, footstep approximation, maps.txt music — music is LOOSE files under <game>/sound/music); M4 ambient life (fidget per _dude_fidget; **wander is a documented fake**); M5 micro INT VM (39 core ops + 181 arity-mapped externals; **examine override path only — use_p_proc/map_enter NOT wired**). KEY FACTS: scripts.lst is 0-based; message_str list ids = scripts.lst index + 1.

Phase 4 (DONE): M0 VM foundations (real rolls — **stub-0 = critical-failure trap**; LVARs are LAZY slices, pristine maps store offset -1); M1 text dialog (gsay loop, options bind by procedure index); M2 locked doors + lockpick + RunMapEnter (**map script = header.ScriptIndex-1**); M3 world-mutation externals + loot/inventory panels (RunMapEnter snapshots its list — stocking scripts mutate it); M4 GameClock (**engine has NO day/night curve; ours is custom**) + JSON delta save/load (containers restock by design); M5 polish (outlines, roof fade, egg-fade approximation, scroll clamp). GOTCHA: GPU backbuffer readback races — screenshots must render via a RenderTarget2D (_screenshotTarget). DEFERRED: per-vertex floor lighting (BasicEffect quads).

Phase 5 (DONE): M0 foundations (real caps/timer/tile externals — **pay-caps stub gave goods away**; timers dialog-gated, cleared on map exit, 1:1 tick source); M1 multi-map persistence (per-map deltas keyed by LOAD-ORDER ORDINALS — **MAP object Ids collide**; LVAR slices keyed by map NAME, imported before map_enter on revisits, firstRun=0; container snapshots overwrite restock; fixes ~590 KB/transition ScriptHost leak); M2 critter stats (proto stat block + 11 MAP combat ints; CritterState = base+bonus); M3 player combat (roll before animate, damage on completion; **corpse = anim+28, NO_BLOCK + flat → loot panel works unchanged**); M4 AI turns (AP-budgeted approach, same-team joiners within 20 hexes, game over → F9); M5 ship-prep (FalloutPoc→Hexwaste rename, SUL license + NOTICE).

Phase 6 (DONE — "The Opening Hour"): M0 hygiene (**OnStubbedExternal finally hooked — it never was**; SaveState Version=1 refuse-mismatch; DeadOrdinals — kills persist, **sid=-1 BEFORE map_enter like the engine**); M1 real dude (premade\player.gcd = critter proto stat-block layout + name/tags/traits; real get_critter_stat/has_trait/do_check/get_pc_stat — fixes stat-gated dialog); M2 critter_p_proc heartbeat (1 script/10 Hz tick round-robin, gated; **unprovoked aggro IS script-driven**); M3 kills matter (destroy/damage procs; XP engine-side from proto exp, paid at combat END, forfeited on death; level-up EN/2+2 HP); M4 winnable combat (weapon/armor/drug proto payloads; equip = item flags 0x1/0x2/0x4000000 — MAP NPC weapons just work; armor mutates bonus stats; stimpak = -2-marker random heal); M5 barter (**export.cc vars session-scoped on ScriptHost — per-VM before, never connected**; gdialog_barter flag-only, **arg OVERWRITES set_barter_mod**; stock lives in the shop BOX at trade time because our dialog model runs the talk epilogue early; price = cost×2×(mod+100)/100×(160+npcB)/(160+dudeB), sells at face). GOTCHAS: map_enter must run HIDDEN scripted objects (shop boxes); the dude's bag is ALIASED to dude.Inventory (caps externals); --attack is a free-swing primitive (resets combat), --fight runs real turns.

Phase 7 (DONE — "Ship It, Then Arm the Wasteland"): M0 v0.6 front door (menu + gcd picker + death screen, v0.6.0 tag; publish = user's git push); M1 V2 saves (MovedOrdinals NPC positions replayed BEFORE map_enter; SavedItem ammo fields, -1 = derive from proto; override_map_start; V1 refuses); M2 guns (10mm-class = HITSCAN, muzzle flash baked in FRM 'j'; to-hit combat.cc:4314 subset; **LoF = greedy hex walk, DEVIATION from the engine's screen Bresenham [SUPERSEDED by P13-M1: now faithful screen-Bresenham]**; dude art hmjmps — **hmwarr has no gun sets, engine has NO weapon-art fallback**; R=reload, roofs moved to F4); M3 traps (spatial records in MapFile; RunSpatialsAt gated like _scr_SpatialsEnabled; create_object_sid BINDS scripts via AllocateSid; use_obj_on item-then-target precedence; gmovie = caption card from .sve); M4 party minimum (**followers travel OUTSIDE map deltas**, follow script re-bound per map — follow logic 100% script-side critter_p_proc; allies act after hostiles; enemies target nearest of dude+allies; team kills pay XP); M5 per-vertex floors (BasicEffect quads, corner light from NW/NE/SW/SE neighbor hexes; newr1 3.34 ms — faster than sprite path).

Phase 8 (DONE — "The Character Comes Alive"): M0 bug fixes (CritterState now tag-aware — gcd TaggedSkills add +20 + double-rate per skill.cc:251; female dude art hfjmps + female death scream when gcd gender baseStats[34]==1; SCOPE.md); M1 skill growth (SkillSet = gSkillDescriptions + skillGetValue + cost ramp + 5+2*IN pts/level cap 99); M2 character sheet (C/K — SPECIAL+derived+skills, allocator enriched not a 2nd panel); M3 rest-to-heal (Z; pipboy.cc:2113 need/rate*3 truncation, HEALING_RATE=max(EN/3,1); **gates on local safety not the engine's can_rest_here flag — documented divergence**); M4 character creation (GcdFile.Create recomputes derived stats per stat.cc:554; **BUG FIX: SpawnDude took the generic proto's 30 HP for ALL gcd characters because GetCritterState keyed on the unset _dude; now reads the gcd directly**); M5 merchant restock (MapDelta.SnapshotDay; _stockedOrdinals container with stale snapshot keeps fresh map_enter stock after RestockDays=3; world loot stays looted). **GOTCHA: premade SPECIAL ordered S/P/E/C/I/A/L = baseStats[0..6] (Agility is index 5, NOT 4).**

Phase 9 (DONE — "Combat Depth II"): M0 extract-first (~700-line turn machine lifted into Formats.Combat.CombatEngine behind ICombatHost + ICombatRng, NO behavior change; **viewer keeps sole ownership of animator/walkers/draw-lists/_blockedTiles so the walker TileChanged closure stays correct without an engine callback; adversarial audit caught two missing side-effects — NPC-walker TileChanged + script damage/destroy procs**); M1 AI packets (parses ai.txt 187 packets; **MapObject.AiPacket was parsed since phase-5 but read NOWHERE**; min_to_hit close-or-flee + RAW min_hp flee — combat_ai.cc:3077, **run_away_mode is party-UI/debug only, NOT the combat flee**; PruneEscapedHostiles disengages fled critters so combat ends. **GOTCHA: arcaves radscorpions are script-spawned at runtime with pkt-8 min-0 — the static map's pkt-14 never applies; Den slaves pkt-33 min_hp-30 actually flee**); M2 aimed shots + criticals (gen_critical_tables.py → 1080-row CriticalTables.g.cs from combat.cc, FNV-1a checksum-guarded; to-hit upgrades SUCCESS→CRITICAL via 2nd d100 ≤ delta/10 + (critChance − hit_location_penalty); honor mult + flags {CRITICAL,DEAD,KNOCKED_DOWN,BYPASS}, mask the rest; aimed shot +1 AP + penalty full-ranged/half-melee. **GOTCHA: criticals gate on day≥2 (random.cc randomTranslateRoll, gameTime/TICKS_PER_DAY≥1) — so day-1 fixtures take ZERO extra RNG draws; the called-shot UI was a V-cycle, SUPERSEDED by P49-M1: V now opens a click dialog**); M3 knockback + persisting knockdown + explosions (shove dmg/10 along hex line for melee/explosion, **NEVER guns — combat.cc:4633, !MULTIHEX/!NO_KNOCKBACK**; crit DAM_KNOCKED_DOWN persists prone, +40 to hit combat.cc:4474, 3 AP to stand; Explode = radius+LoS AoE, explosion DT/DR 23/30 + knockback cap 6 — **ring-spiral simplified to radius+LoS**); M4 throwing (TryThrow reuses ranged to-hit with Throwing skill, range min(maxRange, 3×ST); explosives detonate at landing tile via Explode + misc-10 marker + metarule(49)==EXPLOSION + radius-3 damage_p_proc = the temple-door path; non-explosives drop recoverable. **GOTCHA: projectile flies via the throw anim, not a tweened sprite [UPDATE: screen-tween landed in #11, throws crit as of P13-M3 — both p9 notes superseded]; the artemple door-blast beat is WIRED but unverified in-game — lockpick stays the advertised opener**). Spillover: burst (**DEFERRED — claimed ZERO burst weapons in the slice**), random encounters, Vic's rescue, projectile tween.

Post-phase-10 backlog (GitHub issues):

**#9 burst fire (DONE)** — the phase-9 "ZERO burst weapons" claim was WRONG: newr1.map carries 3 lootable burst guns (10mm SMG/Tommy Gun/Combat Shotgun, via --give). Ported combat.cc _compute_spray: rounds = min(loaded, weapon.Rounds); ONE day-gated inception crit roll (crit-FAIL aborts, crit-SUCCESS +20, bullets still spent); per-round hit = plain d100≤acc, **rounds never crit**; fresh damage roll per hit summed; ammo decremented in ONE batch at resolve (combat.cc:5349, NOT eagerly like single-shot); **AP = secondary ApCost2; burst can't be aimed**. Main target exposure = max(centerRounds, mainTargetRounds), ~3 of 10 for an SMG in a duel. (Left/right cone lines + up-to-6 collateral "extras" = the deferred upgrade, DONE in P13-M2; the main-target exposure model is RETAINED as the documented approximation so 1-on-1 bursts stay byte-identical.) GOTCHA (review-caught): EndPlayerTurn/UpdateCombat gated only _pendingAttack — a pending burst/throw could flip to the enemy turn mid-animation (the B+Space race; **the throw half was a latent p9 bug**); both now block on all three pending actions.

**#13 companion depth (PARTIAL — level-up FOUNDATION only; banter closed-with-docs)** — party.txt EXISTS (data\party.txt; Sulik pid 16777313 section 4, level_minimum 6, level_up_every 3, 6 stage pids). Ported PURE logic into Formats.Party: PartyTable + PartyLevelUp.IncLevel (party_member.cc:1487-1539 _partyMemberIncLevels: level_up_every==0 never; pcLevel<level_minimum gate; cap at level_pids_num; numLevelUps%every levelMod; isEarly skip-until-cycle-boundary; **the INVERTED roll randomBetween(0,100) > 100*levelMod/every = DO NOT advance**). DIVERGENCE: engine indexes level_pids[level] AFTER level++ (skips [0], reads OOB on the last stage — a real quirk, copyLevelInfo only runs here); we apply level_pids in order capped at the count. NO viewer wiring/save/harness — **no shippable map recruits a party.txt companion (the Radscorpion test critter pid 0x1000005 is NOT in party.txt), so wiring would be inert**; lights up free when a real recruitment lands. Banter = ZERO engine work (talk_p_proc already runs all dialog externals; 100% companion-script content gated on the out-of-scope Sulik/Vic recruit quests).

**#10 Vic rescue (M0+M1+M-radio+M2+M3 — COMPLETE)** — M0 fixed the real multi-round dialog blocker: a non-blocking gsay_end means talk_p_proc's trailing end_dialogue set a STICKY SessionEnded that killed the first Choose (every option ended the convo); fix = clear SessionEnded in ResetDialogRound (a real goodbye node re-sets it). **The prior "debunk" was WRONG — DialogRealGameDataTests only asserted TERMINATION (1 round passes), never continuation.** M1 spine proven end-to-end on denbus2 (BOTH Metzger hex 15278 script 45 AND Vic hex 17070 script 49 live there): talk Vic → pay Metzger 1000 caps (item_caps_adjust, 2000→1000) → free-bit GVAR445|0x8000000 handshake → Vic recruit via the REAL talk_p_proc VM → party members=2. **GOTCHA (contradicts the p8 note): the cash buy is RADIO-GATED — Metzger only offers it after GVAR446|0x400000 ("radio fixed")**; the radio externals are a PREREQUISITE, not optional. M-radio (DONE): the 3 inventory externals — obj_is_carrying_obj (0x80BA = quantity-by-pid, **recursive into nested containers** per inventory.cc objectGetCarriedQuantityByPid), obj_carrying_pid_obj (0x810D = handle of first carried item by pid), rm_obj_from_inven (0x80D9 already wired); **pop order pid-then-critter (top-first); object handles are ScriptHost ints (HandleOf/ObjectOf), not engine void***. With pid 266 "Vic's Radio" in the bag → dcVic Node004→Node005 rm radio + set_global_var(446,|0x400000) → Metzger's $1000 buy unlocks → plumbing-free recruit. **GOTCHA: pid 266 has no in-slice source (multi-step Klamath quest item) — the recruit needs ONE item-give, the documented residual content gap.** M2: PartyLevelUp.IncLevel wired into AwardXp (once per PC level-up, stat.cc:789), party.txt parsed lazily, advanced stage proto applied as a per-companion CritterProtoStats OVERRIDE (NOT a shared-cache mutation — anti-aliasing), HP reset to new max (party_member.cc:1605); Vic advances 0x1000175→0x1000176 as the dude levels; dedicated seeded _partyRng keeps the roll off other streams. M3 (#10 COMPLETE): recruit + proto level-up survive save/load (**duplication trap already handled — CaptureMapDelta marks PartyMembers' ordinals TAKEN**); level-up persistence via 3 additive-V2 ints (Level/NumLevelUps/IsEarly, party_member.cc:520-538), stage proto re-applied on load. Lone residual: the radio ITEM has no in-slice source (one --give, content not engine).

**P11 authentic HUD bar (#15, DONE M0-M5)** — real art\intrface\iface.frm (640x99) pinned bottom-centre at native 1:1 (camera has no zoom). M0 bar + log relocation (green monitor is the log home; bottom-left = bar-hidden fallback). M1 HP/AC via the real NUMBERS.FRM digit blitter (3 colour bands; HP white/yellow/red by <50%/<25%) over a field-blank to (32,32,32) — **GOTCHA: iface.frm ships BAKED placeholder digits "036"/"-258" + SINGLE/BURST labels + the AP socket row, so AAF text won't do; AP = lit green pips on sockets**. M2 weapon slot + ammo. M3 green monitor (font1.aaf == engine font 101, tinted green, wrapped, top-anchored). M4 clickable INV/OPT/MAP/CHA/PIP/SKILLDEX (TryClickInterfaceBar consumes the click before world-interaction, additive). M5 active mode-label (SWING/SINGLE/BURST), hover highlight, ENDTURNU/ENDCMBTU combat buttons. **Bar is Draw-only so every fixture stayed byte-identical.** POLISH (DONE): button press-art (DN FRMs overlay the baked UP button while held; HudButton rects re-derived verbatim from interface.cc buttonCreate(x,y,w,h) with gInterfaceBarContentOffset=0) + HP/AC digit-roll (counters step 1/~25 ms toward live stat; cosmetic, never printed).

Phase 12 (IN PROGRESS — "Operate the Panels", HUD/UI wiring; the SKILLDEX/PIP/OPT buttons that only Log'd "not wired"): M0 Skilldex use-skill picker (DONE) — S opens an 8-skill flyout (skilldex.cc gSkilldexSkills order); picking arms _pendingUseSkill, applied on the next click via the generalised use_skill_on_p_proc path (TryLockpick is now just TryUseSkillOn(9)). Targeted skills run the target's script (a scripted door HONOURS use_skill_on_p_proc — stays locked, NOT blindly unlocked) + lockpick-unlock fallback; First Aid/Doctor port skill.cc:546 skillUse (d100 vs dude-skill% → 1-5 HP capped at MaxHp, can't-heal-dead/healthy guards, 30/60 game-min cost, 3-uses-per-day skillGetFreeUsageSlot cap). DOCUMENTED SIMPLIFICATIONS: no Healer perk (min/max heal=0 → 1-5), no crippled-limb model (Doctor limb-fix skipped), Sneak is a logged stance toggle with no stealth effect. Heal uses a dedicated seeded _skillRng. M1 Pip-Boy status + rest (DONE) — P opens PIP.FRM (640x480) centred, date/time top-left (pipboy.cc 20,17 / 155,17; our game-day+clock, NO full calendar since GameClock tracks only ticks — documented), STATUS page + REST sub-page. Rest options map to game-minutes; timed heal Progression.HpHealedResting = minutes*rate/180 (exact inverse of RestHoursToHeal); "until healed" reuses RestToHeal; "until morning/evening" → next 06:00/18:00. Automaps/archives/holodisks/alarm stay out (content-gated). M2 options/pause menu (DONE — P12 COMPLETE) — Esc CHANGED from quit→pause; OPT/Esc opens OPBASE.FRM (164x217) with options.cc showOptions actions minus Preferences (no preferences system): Save/Load/Main Menu (QuitToMainMenu)/Quit/Resume. RIDER: SCOPE.md+README reconciled (burst fire, Vic's rescue, companion level-ups, HUD panels moved "out"→"in"; only the burst collateral cone remained out — SUPERSEDED, shipped P13-M2). GOTCHA: FrmDump's --info is NOT a flag — it dumps a rendered PNG named "--info.png" into cwd (legal-guardrail trap; *.png gitignored but delete anyway).

Phase 13 (IN PROGRESS — "Combat Presentation + Burst Fidelity"; projectile screen-tween already DONE as #11): M0 HexGrid.FromScreenEmbedding (DONE) — the tileFromScreenXY inverse + the 512-byte _tile_mask corner LUT, camera offsets zeroed; the shared primitive the Bresenham LoF and cone end-tile walk both need. M1 screen-Bresenham line-of-fire (DONE) — LineOfFire.Trace rewritten from greedy-hex to the pixel-Bresenham of animation.cc:1951 _make_straight_path_func wrapped by combat.cc:5897 _combat_is_shot_blocked: walk the screen line between tile centres (+16,+8), FromScreenEmbedding per pixel, blocker-check on tile changes; signature unchanged. GOTCHA: the pixel cursor maps to the TARGET tile for a few steps before the exact-centre break, so Trace must skip BOTH endpoints (tile != fromTile && tile != toTile) — engine equivalent is the outer "obstacle != targetObj" guard; without it a wall/critter on the target's own tile false-blocks/counts. Retained simplifications: host-side NO_BLOCK/SHOOT_THRU/dead-critter filter + dropped +1 MULTIHEX crowd bump (combat.cc:5921). M2 burst collateral cone (DONE) — RollBurst fires center/left/right lines (_compute_spray combat.cc:3766-3784): leftRounds=n/3, rightRounds=n-center-left (engine statement order, BEFORE centerRounds-=1); ConeCollateral pivot (dist<=3 ? TileNumBeyond(att,def,3) : def) + rotation + left/right tiles (rot±1) + end-tiles via HexGrid.TileNumBeyond (_tile_num_beyond port); each line walked REUSING LineOfFire.Trace, per-round d100 ≤ each victim's own to-hit, non-target critters → PendingBurst.Extras (cap 6, dedup+accumulate). DOCUMENTED APPROXIMATIONS: the MAIN target keeps the v1 centre-exposure model (so a 1-on-1 stays byte-identical — empty cone lines → zero extra RNG); line sweep reuses greedy/Bresenham Trace (only end-tiles use exact TileNumBeyond); _check_ranged_miss not ported. Collateral emitted as separate "burst-extra:" lines. M3 thrown weapons can crit (DONE — P13 COMPLETE) — TryThrow runs the same day-gated 2nd-d100 crit upgrade as single-shot (combat.cc randomRoll); hit roll became the delta form (chance - d100, identical single draw so day-1 byte-identical); on a day-2 hit a 2nd d100 ≤ delta/10 + critChance upgrades (severity → CriticalTables.Lookup at LocationUncalled (8, penalty 0)). Throws are uncalled (torso) and never knock back (projectiles), so no called-shot UI / no knockback — documented. GOTCHA: a burst's CENTRE-line collateral budget = centerRounds minus the defender's hits (0 in a MinRng all-hit duel) — centre-line collateral only fires when the defender doesn't absorb the whole centre budget; left/right budgets independent. No --burst harness reaches a multi-critter cone on shippable maps (narrow cone + fixed teleport approach), so collateral has no real-data golden — fake-host test is the proof. P13 FOLLOW-UP (Skilldex authentic art, DONE) — DrawSkilldex renders the real SKLDXBOX.FRM + SKLDXOFF/SKLDXON button art (skilldex.cc: title 55,14; buttons bar-local 15,45+i*36), text flyout kept as fallback. GOTCHA (same as iface.frm): SKLDXBOX ships BAKED "223 %%" placeholder digits — field-blank to recess colour (32,32,32) and overwrite right-aligned.

Phase 14 (IN PROGRESS — "Combat Consequences": honor crit flags the tables emit but CombatEngine masked — lose-turn/crippled/blind/knockout — + timed-event queue + crippled-limb model): M0 crit-table 5-tuple + mask widen (DONE) — gen_critical_tables.py emits full rows (mult, flags, massiveStat, statMod, massiveFlags; only the 2 message-id cols dropped); Lookup stride 2→5; HonoredFlags widened from the p9 set (knockdown/dead/bypass/critical) to add KnockedOut/LoseTurn/CripLimbs/Blind (engine _set_new_results mask, combat.cc:4809). KEY FINDING: M0 byte-identical across ALL 14 combat fixtures incl. the 3 day-2 crit ones — because the new effects (CRIP/BLIND/KNOCKED_OUT) live in the MASSIVE-critical column (a secondary stat-roll, wired M4), NOT the base-row flags the day-2 crits hit; so widening the mask+tag is pure inert plumbing. M1 EventQueue (DONE) — pure queue.cc port (Schedule/Process/Remove, SFALL dedup, snapshot-on-process), not wired yet. M2 knockout-wake + turn-skip (DONE) — a combat-owned _combatTick advances 50/round (NOT ICombatHost.ClockTicks — decided against, no clock dep; headless --fight advances rounds so wakes fire); ApplyCritStatus writes the 4 flags from a crit (+ schedules the 10*(35-3*EN)-tick wake); SkipTurnIfIncapacitated forfeits a KO/lose-turn turn (lose-turn one-shot, KO persists); OnCombatEvent wakes (clear KO, leave prone → stands next turn); +40 to hit a KO'd target. M3 crippled-leg move cost + blind (DONE) — CritterState.MovePointCost (leg crip → 4×/8× per-hex AP, critter.cc:1349, on the AI approach budget). DOCUMENTED CUT: the crippled-leg slowdown is NPCs ONLY — the dude's in-combat movement is NOT AP-gated per hex (pre-existing PoC simplification: dude free-walks via WalkTo, ViewerGame.cs ~1974); AP-gating dude combat movement is its own feature (→ P18). Blind: Perception → PE-5 (stat.cc:191); blind attacker -25 to-hit (combat.cc:4470) + RangedMath ×12 distance penalty (combat.cc:4383, positive-penalty branch only). DEFERRED: crippled-ARM weapon-gate (niche, needs two-handed proto flag) — bit set + Doctor-healable. M4 massive-crit secondary stat-roll (DONE) — MassiveUpgrade: a FAILED d10 stat roll (rng.Next(1,11) > defender.Stat(massiveStat)+statMod; combat.cc:4134) ORs in the massive flags; in BOTH RollAttack + TryThrow crit blocks. One new RNG draw, only on an actual crit with massiveStat != -1, so day-1 byte-identical. Re-recorded 2 day-2 fixtures: aim-eyes-day2 now CRITICAL(blind) → scorpion enemy-attack 67→42 = the -25; the all-aimed-eyes run loses on the shifted stream (RNG-divergence, not a bug). M5 Skilldex Doctor limb-fix (DONE — P14 COMPLETE) — SkillHealing.HealLimbs rolls Doctor skill% (d100) per crippled limb/blindness in gHealableDamageFlags order (blind, L-arm, R-arm, R-leg, L-leg; skill.cc:69-75), clearing the bit on success. Skill 7 ONLY. CORRECTION: First-Aid does NOT heal limbs (skill.cc:574 = HP only) — the task premise was engine-inaccurate; only Doctor mends limbs (Repair does on robots — none in slice, inert).

Phase 15 (DONE — "Make the Chrome Click", UI completeness): M0 Pip-Boy full-window automap (DONE) — DrawAutomap renders AUTOMAP.FRM (519x480), every current-elevation object a colored dot (automap.cc automapRenderInMapWindow: ax = 449 - 2*(tile%200), ay = 2*(tile/200) + 8 — the flat-buffer v10 decomposed), colored by FID type (wall grey/scenery green/critter red/item yellow/misc cyan; dead skipped); opened from Pip-Boy (A). DOCUMENTED SIMPLIFICATIONS: fog-of-war faked all-visible (no OBJECT_SEEN); per-type colors are readable approximations of the engine's _colorTable; embedded mini-automap stays out (needs automap.db RLE). M1 weapon-slot interactive (DONE) — the HUD weapon slot (interface.cc:505 rect 267,26,188,67) is a HudButton; clicking (or N) cycles single↔burst for a burst gun; the mode label goes LIVE (was faked from the proto nibble). DOCUMENTED DIVERGENCE: the engine's weapon-slot left-click FIRES at a held target; we have no held-target model (combat targets the hovered critter via F/B), so the slot left-click CYCLES the mode instead (engine right-click semantics). M2 item-row clicking + overflow paging (DONE) — the four panels (inventory/loot/barter/trade) share one ItemPanel model tagged by ItemPanelKind; a row click routes to the same action its number key fires (Shift+click drops in inventory). ItemRowRect is the ONE geometry helper render + hit-test share (no Draw dependency, so headless --panel-click drives the real path). A shared _panelPage window (reset on open; PgUp/PgDn while a panel is open — those keys do elevation only when NO panel up) reaches the 10th+ item. M3 clickable Options/Pip-Boy rows (DONE — Skilldex parity) — geometry-recompute helpers shared by render+hit-test; Pip-Boy action rows render in a FIXED band below the variable status text (reserve 9 lines status / 2 rest) so geometry is computable; Rest rows call DoRest WITHOUT closing (matching the number keys). Spillover: per-member companion priced-barter, embedded mini-automap (automap.db RLE), worldmap-tab wiring. (Inventory drag-and-drop equip shipped P47.)

Phase 16 (DONE — "The Road Watches Back", worldmap + encounter authenticity): M0 encounter-name banner (DONE) — EncounterTable.Index (0-based load order = the subtile's encounterType, worldmap.cc:1384/1962) + EncounterEntry.EntryIndex (the enc_NN number, parsed from the key so a gap can't shift it, :1404) + MessageId = 3000 + 50*tableId + entryId (:3511); worldmap.msg lazy → names the encounter. Reconciled the stale EncounterSpawner docstring (per-member If()/Distance/Tile ARE honored — the old "not parsed" note was WRONG). M1 Outdoorsman detect + Yes/No avoid (DONE) — the detect roll now FLAGS the result (Detected + AvoidXp = max(0,100-detectValue), worldmap.cc:3475-3477) instead of silently nulling — SAME single rng draw, byte-identical stream; only the detection OUTCOME changed. Awards XP then pops a Y/N; N resumes the leg (re-detect or undetected ambush). Headless resolves synchronously (TravelFrom defaults engage so it never hangs). GOTCHA: the Arroyo→Den leg now DETECTS ARRO_Rats (the silent-avoid previously skipped it, running on to spore plants). M2 auto-resume travel (DONE) — _travelDestination remembers an engaged-encounter-interrupted leg's target; leaving the transient map (ApplyTransition Map<0) sets a deferred _resumeTravelDest. DEFERRED: terrain-difficulty step cadence (worldmap.cc:4318) presupposes an ANIMATED dot — travel is instantaneous (whole-leg compute then load), no per-step dot to slow. M3 X-FIGHTING-Y team brawl (DONE) — SpawnInstruction.Team: a FIGHTING situation puts each sub-group on a DISTINCT team (group 0→1, 1→2…; engine uses per-group team_num/proto, only one shipping group sets it, so we assign sequential teams — documented divergence). Cross-team targeting: a critter also targets the nearest HOSTILE on a different team, appended AFTER the dude+party loop, skipping the actor's own team, so a single-enemy-team fight (EVERY combat golden) is byte-identical. New StartBrawl entry (does NOT touch BeginCombat/AddJoiners). GOTCHA: ENCOUNTER_SITUATION_FIGHTING is only in the engine's enum+parse — never used behaviorally; the fight is emergent from proto teams + AI, so we realize it via team assignment. DOCUMENTED LIMITATION: the brawl runs within dude-involved combat (he watches by passing); a fully dude-absent NPC-vs-NPC fight needs the non-dude-centric turn loop (deferred → P73). M4 per-member If()/Distance + the case-bug (DONE) — locking the now-honored fidelity surfaced a real bug: CondRx matched "If(" case-SENSITIVELY, but ARRO_Spore_Plants' Dead member gates behind the ONLY lowercase "if (Rand(5%))" in worldmap.txt, so its condition was dropped and the corpse spawned 100% (10/10 seeds) instead of 5%. Fix: RegexOptions.IgnoreCase (engine keyword match is case-insensitive); +1 rng draw ONLY for that member. Spillover: animated worldmap travel, dude-absent brawl, special-encounter circle pin.

Phase 17 (DONE — "The World Visibly Moves", animated worldmap traversal): M0 stepwise TravelLeg iterator (DONE) — refactored ResolveLeg from whole-leg-compute into a pure per-pixel TravelLeg.Step() (holds the Bresenham cursor + WorldEncounters Δ3 anchor across calls); ResolveLeg is now a DRAIN-loop over Step() — one Step() == one old iteration, same RNG draws in the same order, so all 5 callers + goldens byte-identical (the de-risk checkpoint, the P13-M1 pattern). M1 terrain cadence (DONE, pure) — WorldmapFile parses [Data] terrain_types; TerrainCadence ports wmPartyWalkingStep's _terrainCounter (cycles 1..4, steps a pixel only when counter/difficulty>=1, so difficulty 1/2/3/4 → 4/3/2/1 of every 4 ticks advance — mountains slow the dot). PURE pacing — does NOT touch the game clock or encounter rolls, so animation speed is independent of encounter fidelity. M2 animated dot (DONE) — live play drains Step() over wall-time (TravelTickMs=30), terrain-paced; clock advances per pixel (same total as sync); an encounter pauses the dot. Headless keeps the SYNCHRONOUS whole-leg resolve (byte-identical) via _animateTravel=false. M3 unified travel surface (DONE) — the survey's "second travel surface" was a PHANTOM; every click already routes through the ONE TravelTo (encounters since P10/16, the dot since M2); reconciled the stale WorldmapScreen docstring. The dot now also renders as a persistent "you are here". Lone remaining worldmap simplification: no subtile fog-of-war reveal (→ P22). M4 save/restore mid-travel (DONE) — SaveState.TravelDestinationAreaId (additive-V2, -1=none); LoadGame drops the stale leg+prompt (cursors meaningless post-reload) and queues an auto-resume via the P16-M2 machinery. DIVERGENCE: the engine drops you STOPPED on a mid-walk reload; we resume (consistent with P16-M2 post-encounter auto-resume).

Phase 18 (DONE — "Combat Movement Symmetry"): the dude free-walked the whole map for free on its combat turn while NPCs paid AP, and the P14-M3 crippled-leg slowdown never touched the player. M0+M1 AP-gated dude combat movement (DONE) — the _dude.TileChanged closure deducts MovePointCost per hex from _combat.DudeAp DURING COMBAT (SpendDudeAp, clamped 0) and HALTS the walk when the next hex is unaffordable; click-to-walk refused when AP can't afford a hex. Out of combat, movement is free (no AP model). The 4x/8x crippled-leg cost (P14-M3, NPC-only) now charges the dude — closing the SCOPE asymmetry; Doctor-heal restores it. FIX: DudeController.Update touched _rotations AFTER TileChanged, NPE-ing when a handler Stop()s the walk (the survey-flagged "AP-truncation desync") — guarded. INERT on combat goldens (--fight teleports adjacent + attacks, NEVER walks the dude). M2 crippled-arm weapon gate (DONE) — WeaponProtoStats.IsTwoHanded (extendedFlags 0x200); WeaponBlockedByCrippledArms (combat.cc:5655) — both arms crippled blocks ANY weapon attack, one arm blocks a TWO-HANDED weapon, unarmed never gated; dude only. DOCUMENTED CUT: the NPC AI attack is NOT gated (NPCs rarely lose an arm + it'd churn the sensitive day-2 crit goldens). M3 faithful AI flee (DONE) — TryFlee ported from combat_ai.cc _ai_run_away: head directly AWAY (threat→self rotation, or ±1), as far as AP allows, to a tile reached by a REAL A* path (was greedy neighbour-stepping that snags on walls); uses the whole turn. Flee draws NO RNG → attack rolls byte-identical; denbus2-fight-flee re-recorded for the faithful retreat tiles only (SAME outcome: dude dies in 5 rounds). Spillover: AP-gating the dude's OUT-of-combat move, NPC crippled-arm gate.

Phase 20 (DONE — "Doc Truth + Presentation Polish", a breather phase): M0 stale-doc reconciliation (DONE, pure docs) — SCOPE.md dropped "AP-gated player movement is OUT" (P18 shipped it); CLAUDE.md marked the P10 "v1 cuts" list SUPERSEDED (wait/dismiss persistence #2/#3, per-member If()/Distance P10+P16-M4, X-FIGHTING-Y P16-M3, projectile tween #11 all shipped — only Vic's radio item source remains). M1 embedded Pip-Boy mini-map (DONE) — INVESTIGATION found automap.db is a GENERATED save artifact (engine writes MAPS\AUTOMAP.DB as you explore — not in game data, our PoC never writes it), so the specified RLE-decode has nothing to decode. DIVERGENCE: render the mini-map from LIVE objects (P15-M0 source) instead. M3 Pip-Boy real calendar (DONE) — GameClock.DateAt/DateString port scripts.cc gameTimeGetDate (walk months from FO2 start July 25 2241 — sfall_config.cc start year 2241/month 6/day 24, output +1; the old GameClock comment WRONGLY said "June 24"); Pip-Boy shows "July 25, 2241" not "Day 1". M2 automap fog + colors (DONE) — colors aligned to the in-game _colorTable (walls→pure green [992], scenery→dark green [480]; DIVERGENCE: we still show critters/items + a WHITE dude, which the in-game map hides/paints-red, for a more useful map). Fog: _seenObjects accumulates objects within AutomapSightRadius (14 hexes) of the dude — revealed at spawn + per hex, cleared per map (SIMPLIFICATION: proximity not LoS, not save-persisted → P71). M4 burst collateral real-data golden (DONE — NOT inert) — the standard --burst's fixed dir-3 approach never aligned the narrow cone with a bystander, so --burst-at <fromHex> <targetHex> aims it; denbus2-burst-collateral sweeps TWO real bystanders onto the left/right cone lines — first real-data proof of the P13-M2 cone. Spillover: true-LoS + save-persisted automap fog, automap.db write side, in-Pip-Boy date calendar page.

Phase 21 (DONE — "Script-driven map effects", from the fo2ce gap analysis): wire two arity-stubbed external families the slice ACTUALLY fires (verified via the OnStubbedExternal log on artemple/arcaves map_enter). M0 lighting (DONE) — set_light_level (0x80E9) + obj_set_light_level (0x8107) were stubbed though LightGrid existed since P3-M1. set_light_level → LightGrid.AmbientFromLightLevel (opSetLightLevel's two-segment lerp, 0→MIN/50→MID/100→MAX), sets Ambient + PINS it (AmbientFixed, so the day/night clock stops overriding). artemple's map_enter calls set_light_level(100) → max+pinned (CONFIRMED live). obj_set_light_level sets the object's light fields (intensity*65636/100, engine literal) + OBJECT_LIGHTING flag + rebuilds (no slice map uses it but shares the wiring). Callbacks SILENT (set_light_level fires on EVERY map_enter → would spam every golden). M1 reg_anim (DONE, with honest scope finding) — the slice fires reg_anim ONLY as reg_anim_animate_forever (artemple+arcaves; denbus2 none), and EVERY target is SCENERY (firepits, waterfall) our multi-frame FRMs already auto-loop → visually redundant on the slice. Wired anyway (0x8126: critters get an anim-coded looping FID; scenery loops its FRM). DEFERRED (no slice content, would be dead code): reg_anim_func begin/end queue + the MOVEMENT ops (obj_move/run_to_tile/obj) — the substantive "scripted on-entry NPC movement" reg_anim feature (→ P33-M1). Spillover: reg_anim movement ops + begin/end sequencing.

Phase 22 (DONE — "The Map Remembers Where You've Been", worldmap subtile fog-of-war): M0 reveal model + persistence (DONE) — pure WorldmapFog = the per-subtile UNKNOWN/KNOWN/VISITED grid (840 cells = 20 worldmap tiles × 7×6 subtiles, engine wmTileInfoList[].subtiles[][].state). Ported wmSubTileMarkRadiusVisited (radius 1 — the PERK_SCOUT radius-2 branch is OUT, no perks): the 3×3 ring → KNOWN (never downgrading a VISITED cell), centre → VISITED, + the SUBTILE_FILL_S/W strip spread (real worldmap.txt uses ONLY Fill_W, the western ocean columns — so W-spread is the only one that fires; ported both anyway). Subtile.Fill parsed from worldmap.txt field f[1] (was dropped before). The reveal rides INSIDE the pure TravelLeg (ctor reveals start, Step() reveals each Bresenham pixel) so the sync drain AND the animated dot reveal the SAME subtiles. CRITICAL: the fog draws ZERO RNG (pure position math), so it never perturbs the encounter stream — every travel golden byte-identical. Persistence: SaveState.RevealedSubtiles (sparse flat-index→state dict, additive within V2). M1 render + marker gate (DONE) — per-subtile veils (UNKNOWN=opaque black, KNOWN=alpha-120 black ~ engine intensityColorTable[..][75] dim, VISITED=clear), under markers/dot. Markers + HitTest gated on IsDiscovered = city.txt start_state=On (the 14 major cities, visible from start) OR the location subtile revealed (the 35 Off sub-areas appear once explored near). DOCUMENTED APPROXIMATION: marker discovery tied to subtile reveal rather than the engine's separate circle-hotspot detect (worldmap.cc:3068) — a clean derive-from-fog choice, no second city-state subsystem/save field.

Phase 23 (DONE — "See-Through", object translucency): DE-RISK FINDING (the headline): the whole shippable slice has EXACTLY ONE genuinely-translucent object — a TRANS_STEAM at denbus1 hex 28105 (pid 0x100001D). The hundreds of "TRANS_NONE" objects across every map are OPAQUE — TRANS_NONE (0x8000) is the engine's "render solid, never fade near the dude" flag, NOT a translucent effect (object.cc:5067 switch has no NONE case → default opaque blit). Glass/energy/red/wall: ZERO slice objects. User opted for the full faithful impl anyway. M0 (DONE) — pure TransType {None,Wall,Glass,Steam,Energy,Red} + Translucency.FromFlags (object.cc:943 priority: TRANS_NONE wins→opaque, else wall/glass/steam/energy/red). M1 (DONE) — DrawObjects folds a per-type (tint,alpha) into the object's light tint before the existing premultiplied-AlphaBlend Draw (the same Color*float path the egg-fade uses). The 5 tints are the engine's _colorTable blend SEEDS (object.cc:3467-3471 RGB555→RGB8) softened halfway to white. DOCUMENTED DIVERGENCE: the per-pixel luminance weighting + exact 8-bit palette composite COLLAPSE to one uniform alpha per type (SpriteBatch over RGBA has no 8-bit destination buffer to blend into; the real _dark_translucent_trans_buf_to_buf reads grayTable[src]<<8 + dst through the blend table). Remaining backlog: encumbrance, dialog IQ-gating, blood/gore.

Phase 24 (DONE — "Every Pound Counts", carry weight + encumbrance): item weight was PARSED-THEN-SKIPPED and CARRY_WEIGHT computed-but-never-enforced. RESEARCH (5 readers + critic) ground-truthed the enforcement: over-encumbered does THREE things — (1) max-AP penalty (stat.cc:198 — 1 AP per 40 lbs over, +1), (2) run→walk downgrade (animation.cc:646 — N/A: Hexwaste has only WalkTo, no run — documented inapplicable), (3) pickup/loot/barter BLOCKING (item.cc:313 / inventory.cc:4706/4360). NO movement-speed or worldmap penalty (confirmed absent). M1 pure (BYTE-IDENTICAL — proto read position unchanged, skip-8+read-4 == the old skip-12, so weapon/cost parsing stays aligned): ProtoInfo.Weight; CarryWeight = 25*ST+25 (stat.cc:571 — no perks [STRONG_BACK/PACK_RAT out], no SMALL_FRAME trait); InventoryWeight ports itemGetWeight (base + power-armor/2 [pids 3/232/348/349] + container recursion + weapon loaded-ammo boxWeight*ceil(rounds/boxSize)) + objectGetInventoryWeight (equipped items stay IN the list so they count once, matching the engine's primary loop — the separate-slot block is an engine artifact). M2 enforcement+display — the AP penalty rides a new ICombatHost.DudeEncumbranceApPenalty() DEFAULT method (0 → fake-host tests need no inventory model) through the one ResetDudeAp chokepoint (replaces all 7 `_dudeAp = MaxActionPoints` sites); DUDE-ONLY (player overloads; keeps sensitive combat goldens stable + NPCs have sane loadouts). Pickup/loot-single/barter-buy gated; --give BYPASSES by design (god-mode); take-all is all-or-nothing (inventory.cc:4360, avoids the per-item gate spinning the loop). VERIFIED: 60 SMGs=420lbs/cap 250 → AP penalty (420-250)/40+1=5 EXACT.

Phase 25 (DONE — "Speak Your Mind", dialogue IQ-gating): giq_option's dumb/smart options were gated against a HARDCODED intelligence of 5; now they read the dude's real STAT_INTELLIGENCE. The comparison logic (interpreter_extra.cc _op_giq_option: positive iq = min INT smart option, negative iq = max INT dumb/stupid option, skip otherwise) was ALREADY a faithful port (0x8121) — only the IN SOURCE was stubbed. M0 research confirmed the engine reads critterGetStat(gDude, STAT_INTELLIGENCE) (+ Smooth Talker perk rank, OUT — no perk system) and that the slice FIRES it heavily (denbus2: iq=4 ×33 smart + iq=-3 ×9 dumb), so it's live content. M1: DialogIntelligence() → host.CritterStatValue(_dude, 4) (null dude → 5, the neutral default); gate decision extracted to pure DialogGate.IqOptionVisible(iq, intelligence). KEY: the DEFAULT dude's real IN is 5 — IDENTICAL to the old hardcode — so the vic-recruit/levelup/save goldens are byte-identical, and no --character-combat golden navigates giq dialogue. Probe reports the greeting's OPTION COUNT (an int — NEVER the copyrighted option text): Vic offers 1 option at IN 2 vs 4 at IN 9.

Phase 26 (DONE — "Messy Deaths", gory death animations; the last in-scope gap-analysis item): RESEARCH finding: "blood splat" in FO2 is NOT a separate ground object — it's the death ANIMATION (the corpse FID), chosen by actions.cc _pick_death from damage type + damage + attacker animation, art-checked by _check_death, gated by the violence-level preference. M1: pure DeathAnims.Pick (the _pick_death port: gNormalDeathAnimations/gMaximumBloodDeathAnimations tables by DAMAGE_TYPE; single normal shots + melee stay FALL_BACK [no gibbing], bursts/lasers/explosions/thrown-explosives use the table at damage>=15; the BLOODY_MESS trait + Pyro/Flameboy perks + Molotov + CRITTER_SPECIAL_DEATH are OUT — no trait/perk system). Gore context (DamageType + AttackerAnim) threaded into PendingAttack/Burst/Throw + KillCritter (defaults FALL_BACK for script kills) at all 3 attack sites + 5 KillCritter callers. The host's PickDeathAnim generalised to the _check_death art-resolve (desired gore anim if it ships, else FALL_BACK/FRONT); corpse SF art still deathAnim+28 (FALL_BACK 20 → SF 48, holds for the gore anims). VIOLENCE fixed at NORMAL (no preferences screen — documented; shows gNormalDeathAnimations gore without MAX_BLOOD obliteration). SCOPE finding (translucency-style de-risk): denbus2 humans (pid 0x1000003/4) SHIP the gore art (burst→DancingAutofire, laser→SlicedInHalf, explode→BigHole) so it's LIVE; arcaves scorpions (0x1000005) lack it → faithfully fall back to FALL_BACK — which is WHY the combat goldens (scorpion kills) stayed byte-identical (plus the corpse FID is cosmetic, never in a transcript). GORE is the LAST feasible in-scope gap-analysis item; what remains is out-by-design (perks/karma/quests/content) or a scope expansion past the Arroyo→Klamath→Den slice.

Phase 28 (IN PROGRESS — "The Character Sheet Grows Teeth", traits + perks; first big scope-expansion past the gap-analysis backlog): the marquee character-progression layer. M0 research. M1 trait effects (DONE): ported trait.cc traitGetStatModifier + traitGetSkillModifier verbatim (16 traits; Chem Reliant/Resistant OUT — no addiction system; Sex Appeal has no engine impl). Applied LIVE in CritterState.Stat/SkillValue (engine's per-read critterGetStat behaviour). The SPECIAL→derived propagation (Gifted/Bruiser raising HP/melee) is baked at character-creation in the engine, NOT at stat-read — a future GcdFile.Create concern (no trait picker yet, became M3). NPCs/no-traits pass null → 0 modifier (the INERT-BY-DEFAULT invariant). has_trait was already wired (type 2). KEY FINDING: combat premade Narg (combat.gcd) carries traits [6,15] = HeavyHanded + Gifted, silently ignored until now; M1 makes them apply, so 6 --character-combat COMBAT goldens shifted (Gifted −10 all skills → 57%→47% to-hit, RNG cascades) and were re-recorded. M2 perk infrastructure + selection (DONE): tools/gen_perk_table.py parses perk.cc gPerkDescriptions → PerkTable.g.cs (119 perks, FNV-1a checksum-guarded). PerkRules ports perkCanAdd (maxRank cap, minLevel, skill/gvar param gates with FIRST_ONLY/OR/AND modes + negative-value "at most", per-SPECIAL reqs positive=min/negative=max) + cadence (3 levels/perk, 4 with Skilled, cap 37 — character_editor.cc:5713) + DATA-DRIVEN stat perks via StatModifier (each perk's stat/statModifier × rank: Toughness→DR, Action Boy→AP, More Criticals→crit) folded into CritterState.Stat alongside traits (so M3's stat perks are FREE). DudePerkRanks (int[119]), persisted sparse. has_trait(type 0) returns perkGetRank. Traits + perks STACK. [P75-M3 DOC-TRUTH CORRECTION: Lifegiver was listed here as a folded stat perk, but its PerkTable Stat=−1 (the [0,0,4,...] is the EN>=4 REQUIREMENT, not an effect) — it was INERT until P75-M3 wired its +4-HP/level at the AwardXp level-up site, NOT a CritterState.Stat fold.] M3 high-impact combat/skill-path perk effects (DONE): data-driven STAT perks (Toughness/Action Boy/More+Better Crits/Faster Healing/Bonus HtH Damage/Strong Back/Dodger/+SPECIAL/rad+poison) already work from M2's fold; M3 adds NON-stat perks via ICombatHost.DudePerkRank(int) (default 0): Swift Learner (+5%/rank XP, stat.cc:737), Bonus Rate of Fire / Bonus HtH Attacks (−1 AP ranged/melee, item.cc:1693), Sharpshooter (+2 PE/rank ranged to-hit, combat.cc:4355), Slayer (every melee/unarmed hit crits) + Sniper (ranged hit crits on d10≤Luck) in RollAttack crit block (combat.cc:3866/3891) — all DUDE-ONLY + rank-0 short-circuited. DEFERRED: Jinxed's crit-FAILURE (no single-shot crit-fail model — landed P29-M1/P41); Educated (+skill points/level — P29-M2); rest of 119 data-present. M4 perk-pick UI + char sheet (DONE — P28 COMPLETE): char sheet (C/K) shows Traits + Perks (trait.msg id 100+i / perk.msg id 101+i; trait.cc:74 / perk.cc:218) + effective trait/perk-modified SPECIAL; G opens a modal perk picker (PerkRules.CanAdd-filtered). AvailablePerkPicks = PicksEarned − ranks-taken. Text-panel picker (authentic PERKWIN.FRM art deferred to M5). Spillover: PERKWIN.FRM art, combat-crit trait spillover (One Hander/Fast Shot/Finesse-DR/Jinxed), Educated skill points, companion perks.

Phase 29 (IN PROGRESS — "Finish the Character", the P28 traits + perks spillover): complete the six deferred items. Everything keeps INERT-BY-DEFAULT. M1 combat-path trait leftovers (DONE): new ICombatHost.DudeHasTrait(int). One Hander (combat.cc:4404 — ComputeToHit, dude + any WIELDED weapon: two-handed −40 else +20; skipped unarmed/NPC). Fast Shot (item.cc:1679/1825 — −1 AP for range>2 weapon, floored at 1; AND can't aim: a called shot is coerced to uncalled, mirroring critterCanAim). Finesse (combat.cc:4540 — a dude attacker raises DEFENDER's DR +30 on the non-bypass path, via extraDr param; the +10 crit-chance UPSIDE was already live via TraitModifiers). Jinxed (combat.cc:3857 — on a dude MISS, d2==1 → lost turn, _dudeAp=0). SIMPLIFIED + DOCUMENTED: Jinxed honours only DAM_LOSE_TURN (not the 7×5 _cf_table), is dude-only (engine fumbles EVERY combatant when the dude is Jinxed), gates on CriticalsEnabled (day-2 proxy) vs the engine's day-6 crit-failure gate. Finesse-DR is single-attack RollAttack path only (burst/throw/explosion Finesse is a residual). M2 Educated + Skilled/Gifted skill points (DONE): ported the FULL per-level skill-point grant (character_editor.cc:5686) → SkillSet.PointsPerLevel = 5 + 2·IN(trait-mod) + 2·rank(Educated) + 5·Skilled − (Gifted?5), floored 0; banked cap 99 in AwardXp. KEY: IN is the TRAIT-modified Intelligence (Gifted's +1 IN, NOT drug/perk — critterGetBaseStatWithTraitModifier), so Gifted hits skill points TWICE (+1 IN → +2 SP, then explicit −5 = net −3/level). The P28 note's "−10" was the skill-VALUE penalty, DISTINCT from this −5 skill-POINT penalty. M3 trait picker in char creation (DONE): new CreateTraits step (Stats → TRAITS → Tags), 2-column grid, Space toggles (cap 2, OPTIONAL). GcdFile.Create gained a traits param: BAKES the SPECIAL→derived propagation from TRAIT-modified primaries (Gifted/Bruiser/Small Frame → HP/AP/AC/melee/carry/sequence at creation, mirroring critterUpdateDerivedStats), while the base primary SPECIAL stays UNMODIFIED so CritterState.Stat adds the modifier LIVE (no double count); DIRECT derived modifiers (Kamikaze AC, Heavy Handed melee, Fast Metabolism heal/rad/poison, Finesse crit) added live too. Verified: 5/5/5/5/5/5/5 dude HP30/AP7; Gifted → HP33/AP8; Bruiser+Small Frame → HP32/AP8. DOCUMENTED RESIDUAL: Gifted +1 primary→SKILL-VALUE propagation isn't applied for created chars (SkillSet.Value reads unmodified base — pre-existing P28 skill model); premades load pre-baked skills. M4 curated perk-effects batch (DONE): combat/skill perks via DudePerkRank, dude-only, inert at rank 0. Bonus Ranged Damage (+2/rank ranged, combat.cc:4547; added to raw roll BEFORE the ×2/÷2 wrapper in RangedMath so it nets +2/rank). Living Anatomy (+5 vs living non-robot/alien, combat.cc:4619) + Pyromaniac (+5 fire weapon, combat.cc:4626): flat post-armor adds. Weapon Handling (+3 effective ST vs gun min-ST penalty, combat.cc:4414). Heave Ho (+2 effective ST/rank for THROW RANGE only, cap 10, item.cc:1613). PerkId indices: BonusRangedDamage=4, HeaveHo=35, QuickPockets=48, LivingAnatomy=97, Pyromaniac=101, WeaponHandling=106. DOCUMENTED CUTS: Quick Pockets (−2 inventory-access AP) inert — no in-combat inventory-access AP model; the flat +5/Bonus-Ranged perks are SINGLE-attack path only (burst/throw flat-bonus residual); ~80 perks data-present. M5 PERKWIN.FRM art picker (DONE): authentic PERKWIN.FRM (573x230); eligible list + hovered perk name/wrapped description (perk.msg 1101+i), falling back to text flyout when absent (Skilldex pattern). M6 companion perks infrastructure (DONE — inert on the slice by design): PartyMemberState gained int[]? PerkRanks (additive within V2, null on old/shippable saves); GetCritterState's companion branch passes ranks as the same 5th arg the dude uses. FLAGGED: no shippable companion gains perks (party.txt level-ups advance proto STAGES, not perks — like #13), forward-looking infra with NO UI. P29 COMPLETE.

Phase 30 (IN PROGRESS — "Walk Softly", stealth/sneak). USER DECISION: full faithful detection, LIVE (periodic Sneak roll + Perception/distance isWithinPerception gate so active sneaking slips past script aggro). M0 two-layer sneak state (DONE): pure SneakState mirrors critter.cc — the FLAG (dudeHasState, the Skilldex/S toggle) + Working (_sneak_working, set by the M2 periodic roll); IsSneaking = FlagSet && Working (critter.cc:1236); RescheduleTicks ports sneakEventProcess ladder (success→600; failure retries sooner the higher the skill: >250→100 … >80→400, else 600). using_skill(dude, SKILL_SNEAK=8) returns the FLAG, not Working (interpreter_extra.cc:589 opUsingSkill); was the 0x80AB arity stub, no slice script branches on it. M1 Silent Death backstab + facing (DONE): SneakAttack.IsHitFromFront ports actions.cc:1512 _is_hit_from_front (diff=abs(attRot−defRot); front = diff ∉ {0,1,5} → behind/side hit is the backstab). Silent Death multiplier into RollAttack melee block (combat.cc:3870/3913): melee/unarmed DUDE hit + DudePerkRank(SilentDeath)>0 + sneak FLAG (ICombatHost.DudeSneakFlag, the engine checks the FLAG not Working) + from behind + target not yet engaged (defender.WhoHitMeCid != −1, our proxy for whoHitMe != gDude since Hexwaste doesn't track live whoHitMe — combat sets −1 at engage, so the bonus fires once on the surprise strike) → critMultiplier = 4 plain hit, ×2 on crit. PerkId.SilentDeath=25. M2 periodic sneak roll + persistence (DONE): a SKILL_SNEAK roll (d100 ≤ Sneak, skill.cc:479) sets Working — on flag-enable (immediate) and on the 100 ms critter heartbeat (one reschedule "tick" = one heartbeat, a documented approximation of the engine's game-time EVENT_TYPE_SNEAK queue). DEDICATED seeded _sneakRng (the isolation pattern) → enabling sneak draws ZERO from combat/worldmap/party/script streams. Persisted: SneakFlag/SneakWorking (sparse). M3 NPC detection gate — LIVE (DONE; the behavioral milestone): PerceptionDetect ports isWithinPerception (combat_ai.cc:3499) — two-tier range (with-LoS PE×5 / halved through glass; without-LoS PE×2 in combat else PE) + CanSee (actions.cc:1523 frontal-arc {0,1,5}) + dude-sneak reduction (actively sneaking ÷4, −1 if Sneak>120; flag-but-not-working ×2/3). Wired into the scripted-aggro path: a scripted attacker that can't perceive the dude does NOT engage. KEY de-risk: gated on the SNEAK FLAG (target==dude && FlagSet && !DudePerceivedBy), so a non-sneaking dude short-circuits PAST it → byte-identical (the P13-M1 pattern). Zero RNG. DOCUMENTED CUTS: NPC-vs-NPC sneak OUT (only dude→NPC gated — the engine's target==gDude branch); no lighting/PERK_GHOST term; forced-walk-while-sneaking animation N/A (WalkTo only). P30 COMPLETE.

Phase 31 (IN PROGRESS — "Reputation Precedes You", karma/reputation). USER DECISION: faithful PC-STAT model (engine stores karma=gPcStatValues[4], reputation=[3], NOT GVARs; reputation GVARs are the display layer). KEY engine truth: karma is READ-ONLY — no engine code auto-awards it on kills/quests, NO karma-gated dialog opcode (giq is IQ only) — so karma/town/generic-rep are 100% script-driven (set_global_var) or harness-set; the whole feature is display + script-read, never a combat/dialog behaviour change. M0 get_pc_stat seam (DONE): PcStat index map (stat_defs.h: 0 unspent / 1 level / 2 xp / 3 rep / 4 karma); _dudeKarma/_dudeReputation (default 0); PcStatProvider routes 3→rep, 4→karma, 0→unspent (were stubbed to 0; 1/2 already wired). Inert (default 0 == old stub). M1 generic reputation titles (DONE): GenericReputation.Parse + TitleFor port character_editor.cc genericReputationInit (7077) + lookup (5509): genrep.txt "threshold msgId" rows, sorted DESCENDING; title = highest-threshold row the value meets (−1 below all). The value reads _dudeReputation (engine reads GVAR_PLAYER_REPUTATION — a documented unification). M2 town reputation + karma-title GVARs (DONE): TownReputation.LevelFor ports the 7-band thresholds (character_editor.cc:5574 — <−30 Vilified … ==0 Neutral … >=30 Idolized; note the asymmetry at 0); KarmaTitles.Parse ports karmaInit (karmavar.txt) + Active (rows whose GVAR is non-zero, character_editor.cc:5537, excluding the gvar-0 generic-rep row). KEY FINDING: karmavar.txt binds the REAL vault13.gam GVAR ids (0,3,2,1,11,…), NOT the game_vars.h enum order — KarmaTitles reads the gvar FROM the file so it's robust. M3 karma display + save persistence (DONE — P31 COMPLETE): char sheet (C/K) + Pip-Boy STATUS show "Karma: N", "Reputation: <GVAR_PLAYER_REPUTATION> (<genrep title>)", earned karma titles, non-Neutral slice-town standings. Title STRINGS from editor.msg. MODEL CLARIFIED (supersedes the M1 "unify" note): get_pc_stat(3)=PC_STAT_REPUTATION (_dudeReputation, −20..20) and get_pc_stat(4)=PC_STAT_KARMA (_dudeKarma, ≥0) are the PC-stats, but the DISPLAYED reputation + genrep title read GVAR_PLAYER_REPUTATION = GlobalVars[0] (faithful source, VM-maintained) — distinct ranges, kept separate. Persisted: DudeKarma/DudeReputation (sparse null at 0). pcSetStat clamps: karma≥0, rep −20..20.

Phase 32 (IN PROGRESS — "Broaden Compatibility", from the audit). The audit (155-map dynamic sweep) found: all 155 original maps LOAD cleanly (the DAT/MAP/FRM/proto/render pipeline is general); the gap is scripted BEHAVIOUR — 13/28 procs wired, 93/181 externals wired, vault13.gam GVARs UNSEEDED. M0 proto-read guard (DONE): MapFile's two uncaught protos.Get(pid).SubType reads (Item/Scenery trailer switch) were a latent SIGABRT — a missing/corrupt .pro threw out of LoadMap → MonoGame Update → hard abort. Wrapped in MapFile.SubTypeOf (→ −1 on bad proto, best-effort) + a top-level LoadMap try/catch (FileNotFound/InvalidData/NotSupported/EndOfStream): a transition keeps the prior map, a failed INITIAL load falls back to the title menu (Draw guards on _map != null). NOTE: no shippable map actually trips this (DAT carries every proto); latent hardening, not an active-crash fix — the audit's "Klamath crash" was a measurement artifact (a typo'd filename, not a real incompatibility). M1 vault13.gam GVAR seeding (DONE): GameGlobalVars.Parse ports game.cc globalVarsRead (GAME_GLOBAL_VARS: section, positional index = the i-th non-blank/non-// line, value = sscanf %d after '='). SeedGlobalVars writes non-zero seeds at StartNewGame (after Clear, before first map_enter), sparse + SILENT. KEY FINDING (shrinks the gap): base vault13.gam seeds 684/696 globals to 0; only 12 non-zero, and just TWO touch the slice — GVAR_TOWN_REP_ARROYO[47]:=50 (Arroyo starts Idolized → feeds P31 display) and GVAR_FIND_VIC[619]:=1. GOLDEN SAFETY: seeding fires on StartNewGame; the harness --map debug path goes LoadContent→LoadMap directly (no StartNewGame → no seed), so the vic/dialog goldens are untouched. DOCUMENTED: the bare --map load stays unseeded (synthetic debug jump); real play (menu → New Game) seeds. P32 COMPLETE.

Phase 33 (IN PROGRESS — "Scripted Map Externals", wiring high-leverage stubs the slice fires). KEY GROUNDING (per-map stub census): the slice fires its high-leverage stubs in INERT ways — artemple/arcaves reg_anim_func wraps scenery animation; denbus2's critter_attempt_placement is a SAME-TILE placement (pid 0x0100003A 14716→14716, a no-op; NOT Vic@17070/Metzger@15278) — so wiring is forward-looking infra. M0 critter_attempt_placement (DONE): 0x80FF (interpreter_extra.cc:2812); relocates the critter via Placement.FreeTileNear (the tile or a free neighbour, _obj_attempt_placement SIMPLIFIED to radius-1 vs engine's wider spiral; uses the current elevation's blocking — approximate off-screen). M1 reg_anim movement (DONE — the audit's #1 high-leverage stub): wired reg_anim_func begin/end/clear queue (0x810E) + the 6 register ops the P21 note deferred — reg_anim_animate/_reverse (0x810F/0x8110), reg_anim_obj_move_to_obj/_run_to_obj (0x8111/0x8112), reg_anim_obj_move_to_tile/_run_to_tile (0x8113/0x8114). ScriptContext accumulates a resolved action list between begin and end, flushes on END; viewer plays the batch (move→StartNpcWalk; move-to-obj→walk to FreeTileNear; animate→animator). DOCUMENTED SIMPLIFICATIONS: engine plays a batch SEQUENTIALLY over time — we execute in PARALLEL on END and ignore the per-action delay; run==walk (no separate run speed/anim); Animate LOOPS rather than playing once (no one-shot primitive); animate_forever (0x8126) stays the P21 immediate path, NOT queued. Engine gates every op on !isInCombat() (interpreter_extra.cc:3460) — ExecuteRegAnim mirrors that. INERT: no shippable map fires the move/animate ops at map_enter (only reg_anim_animate_forever for scenery, P21), and reg_anim_func BEGIN/END wrap an empty batch there. SPILLOVER closed (from P21): "reg_anim movement ops + begin/end sequencing".

Phase 34 (IN PROGRESS — "Make It React", fo2ce-portability audit: Tier-1 breadth/feedback + Tier-2 sfx/reaction polish). M0 design specs. M1 combat-introspection externals (DONE): is_in_combat (0x8128, opCombatIsInitialized → CombatEngine.Phase != Idle) + critter_state (0x80FB, opGetCritterState → the CRITTER_STATE bitfield: DEAD(1) for null/non-critter/dead, else NORMAL(0)|PRONE(2 if FID anim 48-49)|DAM_CRIP bits for an active critter, or PRONE(2) for an inactive-but-alive one). DAM_CRIP == CriticalTables.DamHealable (0x7C); the inactive-death test uses MapObject.IsDead (DAM_DEAD bit; HP<=0-without-DAM_DEAD is unreachable for a polled live critter — documented). Stack shape UNCHANGED from the arity stubs (0x8128 = 0-pop/1-push, 0x80FB = 1-pop/1-push) — only the returned VALUE changed, and no slice script branches on it golden-visibly. M2 hurt_too_much flee gate (DONE): engine flees on a SECOND condition besides min_hp — (CombatResults & ai.hurt_too_much) != 0 (combat_ai.cc:3076). AiPacket.HurtTooMuch parsed from ai.txt's hurt_too_much column (_parse_hurt_str port: "blind"→DamBlind, "crippled"→DamCripLimbs[0x3C, legs+arms NOT blind], "crippled_legs"→DamCripLegAny, "crippled_arms"→DamCripArmAny). INERT by default (HurtTooMuch defaults 0 + AND-gate short-circuits); no slice golden enemy carries the bit on a turn it takes (the dude only blinds via a MASSIVE eye/uncalled crit, never landed on a scorpion mid-fight). Real ai.txt: packet 8 (scorpion) "blind"=0x40, packet 14 (Peasants) "crippled, blind"=0x7C, packet 33 (Den slave coward) "blind"=0x40. DOCUMENTED CUT: the engine's third OR-clause (CRITTER_MANUEVER_FLEEING) unported — Hexwaste has no maneuver model. M3 run animation (DONE): dude now RUNS by default. RunGuard.MovementAnimCode ports the 3 guards of animationRegisterRunToTile() — walk if a crippled leg (DAM_CRIP_LEG_ANY), or sneaking without Silent Running (PERK 15), or the run art (ANIM_RUNNING=19) is missing; else run. Dude computes runArtExists from the dude's ACTUAL weapon-code FID — a DOCUMENTED DIVERGENCE from the engine's weaponCode-0 check, so the existence test matches the FRM that loads. Per-rotation offsets + FRM-driven speed are anim-code-INDEPENDENT (already correct); CurrentFid is Draw/anim-only. DECISIONS: run applies in combat too (faithful, AP-cost unchanged by anim-code); the _dude_run sneak-disable side-effect (animation.cc:3007) DEFERRED (golden-risk to the P30 sneak suite). M4 typed combat outlines (DONE): during combat every visible LIVING critter is outlined by team (red hostile / green friendly / dim perception-only), LoS-gated — ports combat.cc _combat_update_critter_outline_for_los + object.cc _obj_outline_object. CombatOutline.TypeFor: clear LoF to the dude → same-team FRIENDLY else HOSTILE; LoF blocked → PERCEPTION if within dude PE×5 (÷2 through glass) else none. The 5-band gradient COLLAPSES to the engine's base palette index (243 red / 229 green / 61 dim — DOCUMENTED flat for the gradient). Reuses the faithful P13-M1 LineOfFire.Trace. Green hover outline suppressed during combat. DOCUMENTED: fog-of-war not modeled (all-visible). M5 combat sfx (DONE): SfxName.CharName (sfxBuildCharName port: FRM base + _art_get_code(weapon,anim) pair + death/knockout/contact override on the WEAPON char — FALL+Die→'Z', punch/kick+Contact→'Z'; null when base unresolvable) + WeaponName (sfxBuildWeaponName: W{R|A|O|F|H}{soundCode}{variant}{material}XX1; variant 1 for ready/oota/primary else 2). Wired: got-hit grunt (anim 14), death scream, unarmed-swing grunt, weapon-ready, out-of-ammo. DECISION: faithful CharName everywhere (scorpions→MASCP2*/MASCRP* SHIP; humans→HMWARR* DON'T = engine-faithful silence) EXCEPT the dude death keeps the P8 HumanDeath HM/HFXXXX fallback. Weapon-HIT material defers to 'F' flesh (combat never shoots scenery/walls — proto-material parse deferred). Ambient: MapList parses ambient_sfx= (malformed "animal:15 animal:10" token drops gracefully via first-':' split); AmbientSfx.RollIndex (wmSfxRollNextIdx weighted pick) + RemapBirdForNight (brdchir1/brdchirp→cricket/cricket1 at hour ≤600/≥1800); wall-time tick, combat-gated, DEDICATED seeded _ambientRng, ~17s cadence. ALL sfx via _audio?.PlaySfx → --no-audio headless-inert; the ambient timer is wall-time (harness never pumps it). M6 reaction animations (DONE — P34 COMPLETE): defender visibly REACTS on attack resolve. ReactionAnims ports actions.cc _show_damage_to_object + animation.cc _dude_standup: HitReaction (front→ANIM_HIT_FROM_FRONT 14; behind→HIT_FROM_BACK 15 only if the critter ships that art, else front), KnockdownFall (front→FALL_BACK 20 else FALL_FRONT 21), StandUp (fell-back→BACK_TO_STANDING 37 else PRONE_TO_STANDING 36), Dodge (ANIM_DODGE_ANIM 13). OnTargetHit WIDENED to (target, attacker, knockedDown); new OnTargetDodge (miss on a non-prone/non-KO defender) + OnGetUp hooks. Guarded by art existence + an already-mid-fall check (don't override a held FALL with a hit-react, actions.cc:438). DOCUMENTED: the DUDE is EXCLUDED from reactions (engine reacts him too — Hexwaste's camera-anchor dude historically doesn't; a spillover); the _pick_fall blocked-tile flip out of scope. Reaction goldens: human (ships back art → 14/fall-20 front, 15/fall-21 behind) vs scorpion (lacks back art → stays 14 even from behind, the fallback). P34 COMPLETE.

Phase 35 (IN PROGRESS — "The Script Takes Its Turn", combat_p_proc; the audit's hardest backlog item).
KEY FINDING: combat_p_proc (SCRIPT_PROC_COMBAT=13) is LIVE on the slice but liveness splits by HOOK — the
engine fires FIVE differently: per-turn fp=4, on-hit fp=2, want-to-join fp=5, the end-of-combat map hook,
the dead round-robin.
M1 fp=4 per-turn hook (DONE): for each scripted (sid!=-1) combatant, at the TOP of its turn (after skip-if-
incapacitated, before standup/AI), run combat_p_proc with scriptSetObjects(sid,NULL,NULL)+fixedParam=4
(combat.cc:3243-3258); if it called script_overrides() the engine skips standup+AI (combat.cc:3259) — we
mirror it by forfeiting the rest of the turn. Run for EVERY combatant, no party exclusion. GOTCHA: RunProc
couples source==dude_obj, so source=null yields dude_obj=0 in combat_p_proc — a documented divergence from
the engine's persistent gDude, INERT on the slice (fixed in M2). INERT because: the only --fight critter
defining combat_p_proc is the arcaves scorpion (ZClScorp, script 19), whose body gates on fixed_param==2
(the on-hit hook), so the fp=4 call short-circuits → no RNG/message. DOCUMENTED CUTS: the dude's own per-
turn proc (engine runs it for gDude too; inert, no slice dude gcd defines it); the other 4 hooks unported.
The fp=4 scripts that WOULD act — ACTemVil (script 748, terminate_combat at ≤half HP) + dcG2Grd (36) —
aren't in any --fight golden; their override externals (terminate_combat/critter_add_trait) stay arity-stubbed.
M2 fp=2 ON-HIT hook (DONE — scorpion poison sting): after a landed hit (combat.cc:4729-4733, defenderDamage
>=0 && DAM_HIT) the ATTACKER's combat_p_proc runs source=NULL, target=struck defender, fp=2. A DECOUPLED
runner (source always NULL, target+dude separate) — also fixes the M1 dude_obj=0 divergence (proc now sees
the real dude_obj). poison(0x8122, opPoison→critterAdjustPoison) → ApplyPoison (DUDE-ONLY, poison-resistance
reduced, sets Poison counter). Scorpion proc calls do_check + random + poison(target), deterministic under
--rng-seed. SURPRISE (BYTE-IDENTICAL despite the scorpion stinging the dude in --fight): (a) ApplyPoison is
SILENT — the engine's misc.msg "You have been poisoned!" is a copyrighted string, deliberately NOT emitted;
(b) the poison counter doesn't tick HP during the fight (EVENT_TYPE_POISON delayed-damage = documented
simplification, M3); (c) do_check draws from _scriptHost.Rng, NOT the combat stream. DOCUMENTED CUT: a
lethal hit returns early so fp=2 fires only on a non-lethal hit (moot for poison).
M3 poison-over-time tick (DONE — "poison actually hurts"): the dude's poison counter deals periodic HP
damage on the game clock. KEY: the ported EventQueue is COMBAT-SCOPED (cleared on combat end), so WRONG for
poison (must outlast combat); instead a viewer game-time schedule (_dudePoisonNextTick off GameClock.Ticks)
models the single EVENT_TYPE_POISON entry. SchedulePoison times the next tick to 10*(505-5*poison) ticks
(critter.cc:350-351); ProcessPoison (poisonEventProcess, critter.cc:378, DUDE-ONLY) fires every due tick in
a drain-loop (so a rest/travel clock JUMP deals the right count): poison -= 2, HP -= 1, GameOver if HP<=0,
re-queue from its own fire instant until poison<=0; driven from UpdateClock. "You take damage from poison."
misc.msg omitted (copyrighted, silent — the P35 pattern). Persisted: DudePoison (additive-V2 sparse; schedule
re-derived on load). DOCUMENTED CUT: a headless harness that jumps the clock without pumping a frame relies
on the explicit ProcessPoison in the probe.
M4 fp=5 want-to-join hook (DONE): the join decision runs each candidate's combat_p_proc fp=5 + honors its
maneuver (_combatai_want_to_join, combat_ai.cc:3165): a dead/KO critter never joins; one hurt this turn
(DamageLastTurn>0) always does; else fp=5 runs (script may set its maneuver, e.g. by attacking) and the
maneuver decides — ENGAGING(0x01)→join, DISENGAGING(0x02)/FLEEING(0x04)→don't; else the existing ShouldJoin
heuristic. Join clears maneuver to NONE (combat.cc:2907). The attack external sets the attacker ENGAGING
(interpreter_extra.cc:1860) — the primary maneuver source. INERT because: no slice critter handles fp==5
(scorpion fp==2, rat none) so fp=5 is a no-op VM run → maneuver stays NONE → ShouldJoin decides; no non-
hostile candidate is damaged (anything the dude hit is already hostile). DOCUMENTED RESIDUAL: the FLEEING/
DISENGAGING maneuver SOURCES (flee/terminate_combat externals, interpreter_extra.cc:4763/4781) stay arity-
stubbed — only ENGAGING-via-attack is wired (force-join, but not script-refuse).
M5 terminate_combat + DISENGAGING source (DONE): terminate_combat (0x8153, opTerminateCombat) — the combat-
control external a yield script (e.g. a temple challenger fp=4 at ≤half HP) calls to END the fight. Sets self
DISENGAGING (completing M4's residual maneuver source) + a _terminateRequested flag (set only in combat, the
engine's isInCombat guard) honored at the next turn boundary → EndCombat (combat.cc _game_user_wants_to_quit
=1). FORWARD-LOOKING INFRA: the grounding workflow's claim that ACTemVil (748)/dcG2Grd (36) are slice critters
is WRONG — MapDump finds NO script-748 critter in any of the 4 slice maps (arcaves' lone static critter is
script 750), so no shippable critter calls terminate_combat; proven by fake-host test only. DOCUMENTED
RESIDUAL: the FLEEING source (the flee external) still arity-stubbed (only ENGAGING-via-attack + DISENGAGING-
via-terminate wired). P35 COMPLETE; the end-of-combat map hook + the dead round-robin stay unported (no slice
driver).

Phase 36 (DONE — "Big Targets", MULTIHEX; Phase-34 audit's top Tier-2 combat item). Two gaps: (1) ComputeToHit
gained +15-to-hit-vs-a-multihex-defender (combat.cc:4443 — a big target is easier to hit; reads OBJECT_MULTIHEX,
the const already used for P9 knockback immunity); (2) the encounter-spawn path PROPAGATED the proto's
OBJECT_MULTIHEX (0x800) onto the spawn (it hardcoded Flags=0, so a spawned Large Radscorpion was never multihex
→ +15 + knockback immunity silently never applied). SLICE DRIVER (verified, NOT dead code): the Large Radscorpion
(pid 0x1000006, flags 0x20000800) spawns in KLAD_Scorpions (Klamath-Den route, 30% ratio + Dead variant); the
SMALL Radscorpion (0x1000005, the arcaves --fight critter) is NOT multihex (0x20000000) — so +15 is INERT on
current combat goldens (they fight the small one). Verbatim 1-line port.

Phase 37 (DONE — "Better Living Through Chemistry", non-HP drug stat effects; Phase-34 audit's last slice-driven
item — UseDrug previously applied ONLY the HP heal and Log'd "Nothing happens" for SPECIAL chems). Ported
item.cc _item_d_take_drug (:2809) + _perform_drug_effect (:2639) + the EVENT_TYPE_DRUG wear-off queue.
M1 proto (the drug weight int was already skipped, so the 9 trailing ints read in place): DrugProtoStats widened
to carry Duration1/Amount1, Duration2/Amount2, AddictionChance, WithdrawalEffect, WithdrawalOnset (proto.cc:1570-
1581). KEY GROUNDING FINDING: the duration1/duration2 amount tiers are NOT a residual to skip — they ARE the
wear-off; the three tiers per stat NET TO ZERO (Buffout ST +2 now / −4 at 360min / +2 at 1080min = 0; Jet ST/PE/AP
+1/+1/+2 / −4 at 5min / restore at 1440min = 0 — the comedown is the down-then-up ramp).
M2 effects: ApplyDrugEffect (stat 35 = current HP heal/clamp/GameOver; stats 0..34 = a BonusStats bonus; the
stats[0]==-2 sentinel = the stimpak random-range heal, REUSING _combatRng so the existing draw is byte-identical;
stats ≥36 [poison/rad counters] out of scope bar Mentats' minor rad bump), then schedules the two delayed kicks
(skips all-zero). ProcessDrugs drains due kicks in fire-time order from UpdateClock — the P35-M3 game-time pattern
(a rest/travel JUMP fires several at once). PERSISTENCE (critical risk): BonusStats is REBUILT from base+armor on
load (the drug bonus is NOT in the base block), so a mid-drug save would lose the immediate bonus while pending
reversals still fire → negative stats; FIX = track the drug's contribution in _drugBonus[35], persist it (sparse)
+ the pending kicks, and RE-APPLY _drugBonus to BonusStats AFTER the sheet rebuild on load. INERT: no golden gives/
uses a stat drug (golden --give pids are weapons/caps/radio; the -2 stimpak RNG unchanged).

Phase 38 (DONE — "Vices and Tallies", the user's "perks/karma auto-award, addiction/withdrawal, VO" ask —
RESHAPED by a grounded + adversarially-verified workflow against the prime directive). USER DECISIONS: drop
karma auto-award + add kill counters; full addiction/withdrawal; DEFER VO.
KARMA AUTO-AWARD — REJECTED as non-faithful: the engine has NO kill/quest/combat→karma hook (pcSetStat
stat.cc:611 is the sole gPcStatValues writer, never called with KARMA/REPUTATION; no set_pc_stat external;
combat.cc:4855 kill path runs destroy_p_proc + XP + the kill counter, ZERO karma) — karma is 100% script-
driven (set_global_var), wired in P31; inventing an auto-award would violate "port, never guess".
VO — faithful but FULLY INERT on the slice (every shippable Den NPC MSG has empty audio fields; no slice NPC
has a head/speech dir; the only voiced NPCs Elder/Hakunin/Sulik are content-gated out) — DEFERRED.
LESSON RE-CONFIRMED: the grounding synthesis mis-decoded the addiction perks AND the Tragic/Jet GVAR indices
— I verified every load-bearing value against the actual source / the checksum-guarded PerkTable.g.cs.
M0 DrugAddiction: drugPid→addiction-GVAR map (item.cc:144; verified game_vars.h NUKA=21/BUFFOUT=22/MENTATS=23/
PSYCHO=24/RADAWAY=25/ALCOHOL=26/TRAGIC=293/JET=294) + Roll (item.cc:2823 chance ×2 ChemReliant /÷2 ChemResistant
/÷2 FlowerChild, roll(1..100)≤chance inclusive); PerkRules.MaxRankPerkEffect = the perkAddEffect maxRank==-1
fold ((Stat,StatModifier) + StatReqs[0..6] SPECIAL array as the EFFECT) decoded from PerkTable.g.cs: Buffout(54)=
ST-2/EN-2/AG-3, Mentats(55)=IN-3/AG-2, Psycho(56)=IN-2, RadAway(57)=RadResist-20, Jet(70)=MaxAP-1/ST-1/PE-1,
Tragic(71)=PE-2/IN-1/LK-1, Nuka(53)=none.
M1+M2: TryAddict rolls on a dedicated isolated _addictionRng (→ byte-identical even though Buffout is now
addictive), sets the GVAR, schedules onset (600*withdrawalOnset ticks); ProcessWithdrawals onset→apply the perk
fold into _withdrawalBonus[35] + schedule recovery 7 game-days out FROM THE ONSET'S FIRE INSTANT (the clock-jump-
correct rule, like ProcessPoison — caught + fixed a recovery-scheduling bug); recovery→reverse + clear GVAR,
EXCEPT Jet (PERK_JET_ADDICTION=70 returns early → PERMANENT until pid-260 antidote, one-give residual). NEVER
touches _dudePerkRanks.
M3 persistence (WithdrawalBonus + PendingWithdrawals, re-applied AFTER the load sheet-rebuild — the DrugBonus
trap) + "Addictions:" display (character_editor.cc:4611 gAddictionReputationVars + editor.msg 1004+index).
KILL COUNTERS (the faithful karma adjacency): KillCritter tallies the victim's KILL_TYPE beside the XP accrual
(gated identically, combat.cc:4870) → _killsByType[19] (critter.cc:152), reset on new-game, persisted (sparse);
metarule3 rule 103 GET_KILL_COUNT wired (inert — no slice script reads it); "Kills:" display (proto.msg 1450+
killType). Golden verified seed-7 arcaves fight = 2 Radscorpions (each 60 XP, +120 = 2 kills NOT one).

Phase 39 (DONE — "Required Reading", skill books; chosen by a next-feature-grounding workflow ranking 4
candidates by value/effort/faithfulness/LIVENESS — books won [82] as the only one faithful, small, a NEW
player-facing capability, AND verified-live [vs selectable-ammo whose effect is already live, crit-FAILURE
gated to day≥6, map_update_p_proc subtle/inert]). A recursive inventory-walk found exactly 2 lootable books:
Guns and Bullets (pid 102→Small Guns) in a denbus1 container, Scout Handbook (pid 86→Outdoorsman) on a KLAMALL
critter (the slice's Klamath map is KLAMALL.map, NOT klamath.map).
M1 SkillBooks: BookTable (booksInitVanilla item.cc:3283 — 73→Science/802, 76→Repair/803, 80→FirstAid/804,
86→Outdoorsman/806, 102→SmallGuns/805) + BookRaise.Increase = (100−effective)/10, ≤0→0 (the de-facto cap at
effective 100 — NOT skillAddForce's 300 guard), ×150/100 with Comprehension (proto_instance.cc:776); ReadSeconds
= 3600*(11−INT). PerkId.Comprehension=81 (enum index, verified — NOT the line-88 the synthesis cited).
M2: refuse in combat (proto.msg-902, no copyright); read the EFFECTIVE skill but WRITE the BASE points
(_dudeGcd.Stats.Skills[skill]+=increase — the engine's skillGetValue/skillAddForce split, so a TAGGED skill
gains 2%/point: Narg's Small Guns 43→53 from +5 pts; untagged Outdoorsman 16→24 from +8); advance the clock.
DOCUMENTED OUT-OF-SCOPE (both in _obj_use_book): the paletteFadeTo screen fade (no palette fade) +
scriptsExecMapUpdateProc (map_update_p_proc unwired at the time). DECISION: ship all 5 book rows (3 have no
slice instance — forward-looking infra).

Phase 40 (DONE — "Pick Your Round", selectable ammo type; the next-feature-grounding RUNNER-UP [71]). The
combat CONSEQUENCE was already live (AP vs JHP shifts to-hit + damage via the wired ammo AC/DR/mult/div math);
P40 adds player CONTROL. UnloadEquippedWeapon (weaponUnload item.cc:1880 — eject min(loaded,boxCapacity) into a
DISCRETE bag box [a partial count must NOT merge into a full stack], leave the remainder, empty the weapon).
TryReload forwards to TryReloadWith(preferredAmmoPid): -1 = byte-identical (R-key/AI auto-reload), ≥0 restricts
the bag scan to a chosen pid. The no-mixed-mags rule stands, so a type SWAP needs an empty weapon → unload first.
SLICE-LIVE (recursive inventory walk): the 10mm pistol (pid 8) + Klamath pipe rifle (299) both fire 10mm, with
10mm AP (pid 30: ac0/dr-25/mult1/div2 = armor-piercing) + 10mm JHP (29: ac0/dr+25/mult2/div1 = anti-unarmored)
lootable across denbus1/2/kladwtwn, and Den NPCs wear DR-armor so AP genuinely matters. DOCUMENTED: the unloaded
box is discrete (engine creates a fresh object too); the engine's prefer-last-loaded-type auto-reload nuance
(item.cc:1455) is approximated by a bag-order scan on an empty weapon (pre-existing, unchanged).

Phase 41 (DONE — "Fumble", the critical-FAILURE table; highest-value remaining backlog — completes the crit
system [crit-SUCCESS landed P9; failures only honored Jinxed's lose-turn since P29]).
M0 data (proto un-skip is read4+skip4 == the old skip8): reads weapon criticalFailureType; gen_critical_tables.py
emits _cf_table[7][5] (combat.cc:1875, verified e.g. row6col4=Explode|LoseTurn|OnFire=37888); FNV-1a checksum
folds the new table; CriticalFailure.Severity (Luck-bucketed d100−5·(LK−5), combat.cc:4203) + Resolve.
M1 trigger + effects: the natural ROLL_CRITICAL_FAILURE upgrade (random.cc randomTranslateRoll — symmetric mirror
of crit-success: a MISS at day≥1 [CriticalsEnabled] draws a d100 ≤ −delta/10) + the Jinxed force (combat.cc:3857,
any combatant when the dude is Jinxed, no day gate). The upgrade draw is the very next after the miss hit-roll,
so day-1 non-Jinxed draws NOTHING → byte-identical. KEY DIVERGENCE FIX: the DUDE's crit-fail EFFECT is now
correctly gated to day≥6 (combat.cc:4190 — the trigger still draws from day 2); non-dude fumbles ungated (P29's
day-2 lose-turn was the documented divergence, now faithful). Effects (attackComputeCriticalFailure): LOSE_TURN/
KNOCKED_DOWN/CRIP_RANDOM/DROP/DESTROY/LOSE_AMMO/HIT_SELF/HURT_SELF/EXPLODE/RANDOM_HIT (the wild shot can catch a
companion); the _attackFindInvalidFlags mask clears DROP/DESTROY/LOSE_AMMO for an unarmed attacker. DOCUMENTED
SIMPLIFICATIONS: self/collateral damage is a direct HP hit (no on-hit hooks/ammo mods, not a re-attack); DAM_DUD/
DAM_ON_FIRE are cosmetic (no jam/fire model); crit-fail is on the SINGLE-attack path ONLY (burst already aborts
on its inception crit-fail; thrown is a residual — both day-1 goldens). RE-RECORD: the 2nd-d100-on-miss shifted
the 3 day-2 crit goldens (the P14-M4 precedent; a clean RNG shift, fights resolve sanely).

Phase 42 (DONE — "Field Medicine", enemy chem_use stimpak healing; AI-depth runner-up of the next-feature-
grounding-2 workflow). DELIBERATELY CHOSE THE RUNNER-UP over the synthesis's #1 (map_update_p_proc lighting,
8.8): I verified the #1's "every map wrongly full-bright → wire the day/night curve" premise was UNCONFIRMED
(contradicts the established P4 "engine has NO day/night curve; ours is custom"; the opcode scan showed arcaves
has ZERO game_time_hour/month refs [a cave] and the town-map counts were false-positive-inflated; couldn't
confirm map_update drives set_light_level without building it) + its AmbientFixed rework risks the P21 goldens.
Per the prime directive (don't build on an unverified premise), the solidly-verified runner-up won: ~30 slice
human NPCs CARRY stimpaks (pid 40, SubType=2) and chem_use is a live ai.txt field.
Ported combat_ai.cc _ai_check_drugs healing branch (:999-1027): chem_use (clean=0/hurt_little=1/hurt_lots=2/
sometimes=3/anytime=4/always=5); IsHealingItem (pids 40/144/273, item.cc:3592) + HealHpRatio (clean→0/little→60/
lots→30/else→50, :971). TryAiHeal runs after the flee gate (engine's flee→drugs→attack order): a BIPED
(BODY_TYPE_BIPED==0 → quadruped scorpions never heal) below MaxHp*ratio/100 quaffs a healing item while AP≥2,
2 AP each; rolls the stimpak heal on _combatRng. ENEMIES ONLY (dude/allies heal via the UI); the non-healing
combat-drug branch (Jet/Psycho) is a documented residual. INERT because: the golden-fight enemies — arcaves
scorpion (pkt8 clean + quadruped) and denbus2 peasant (pkt14 clean) — never heal (real ai.txt: pkt8/pkt14
chem_use absent=clean). DOCUMENTED: the slice's stimpak NPCs live in SWARM Den maps where the dude can't win a
clean 1-on-1, so the live proof is --ai-heal-probe + a fake-host test, not a winnable real fight.

Phase 43 (DONE — "Draw Your Backup", AI best_weapon inventory switch; the user's "Full combat AI" ask, M2 —
chem_use M1 shipped in P42). _ai_switch_weapons (combat_ai.cc:2596) → _ai_search_inven_weap (:2002) → _ai_best_
weapon (:1817): when a critter's wielded weapon becomes unusable it scans its CARRIED weapons and wields the best
one its ai.txt best_weapon preference allows. GROUNDING: multi-weapon NPCs DO exist on the slice — denbus1 17261
(Tough Guard pkt22 = ranged_over_melee) with a backup; kladwtwn/denbus2 pkt12/24/34 NPCs with backups — while
golden-fight scorpions are non-biped + carry NO weapons (inert).
Ported: WeaponClass (item.cc _attack_subtype/_attack_skill[9] → ATTACK_TYPE+SKILL from extFlags&0xF, with the
SMALL_GUNS→ENERGY/BIG_GUNS refinement); AiBestWeapon (_weapPrefOrderings[9][5] indexed [best_weapon+1] + pairwise
Prefers: order term, ±5-damage cost tiebreak, flare deprioritise, best_weapon==-1/≥UNARMED_OVER_THROW damage
override, RANDOM coin); BestWeapon parsed (-1 default = engine pre-parse, same as no_pref). AiSwitchWeapon folds
candidates with _ai_can_use_weapon (both-arms-crippled/one-arm+two-handed gate, skill≥min_to_hit, pref-type match,
ranged-needs-ammo) over an unarmed punch seed (UNARMED if dist≤1 else NONE), gated on BIPED/ROBOTIC (combat_ai.cc:
2004); wired into TryEnemyAction's dry-gun branch BEFORE the fists fallback. DOCUMENTED SIMPLIFICATIONS: the avg-
damage score omits the weapon-perk ×2 + explosive ×(extras+1) factors (Hexwaste tracks neither); _combat_safety_
invalidate_weapon (ally-in-LoF/over-range) not applied; ranged ammo approximated; only the dry-gun switch trigger
is wired (engine also switches on arm-crippled / out-of-range-no-weapon — same helper, no slice driver). KEY
FINDING (verified, not guessed): the kladwtwn multi-weapon NPCs' RUNTIME packets (24/34, no best_weapon) differ
from MapDump's STATIC read because kladwtwn map_enter spawns/replaces them — both reads are correct; denbus1
17261 (pkt22, consistent) is the clean demonstrator.

Phase 44 (DONE — "Initiative", interleaved combat turn order by Sequence; the biggest remaining combat-fidelity
gap). Combat ran in FIXED BLOCKS (dude → all hostiles → all allies); the engine interleaves EVERY combatant by
Sequence. Ported combat.cc: _combat_sequence (rounds 2+ qsort by _compare_faster = Sequence desc, Luck tiebreak;
drops dead + KO/disengaging to noncom) + _combat_sequence_init (ROUND 1 is attacker-first / defender-second /
dude-third — initiative does NOT apply the opening round; the one who started combat goes first) + the round loop.
Replaced the two-block model with ONE interleaved _order list + _orderIndex; BuildTurnOrder uses OrderByDescending
Sequence/Luck — STABLE for ties, a documented divergence from the engine's UNSTABLE qsort, for golden reproducibility.
StepTurnOrder walks one actor per Step (an NPC slot auto-resolves; the DUDE's slot pauses in PlayerTurn for input
— the engine's blocking _combat_turn(gDude)). The phase enum is unchanged (the viewer only reads Idle/PlayerTurn).
KEY OUTCOME: 15 of 16 combat goldens BYTE-IDENTICAL — the arcaves scorpions have Sequence ≤ the dude (Narg), so
dude-first order is unchanged; the reorder only bites when an enemy OUT-sequences the dude. The lone re-record
(denbus2-fight-flee, where Den humans out-sequence Narg) is SANE: same outcome (dude dies to the 24-slave swarm)
in 5 rounds instead of 9 — the faster death is the faithful order (humans now correctly act before him).
DOCUMENTED: round-per-round game-clock advance (gameTimeAddSeconds(5)) NOT wired (combat stays wall-time;
_combatTick is the knockout-wake source only).

Phase 45 (DONE — "Numbers in the Air", floating/overhead combat text; outcomes only reached the monitor log
before). KEY FINDING (the headline, verified): Fallout 2 does NOT float combat outcomes — _combat_display routes
every hit/miss/crit/damage line to the scrolling MONITOR LOG (displayMonitorAddMessage), ONE colour, no float;
the text_object.cc float layer is real but used only for AI taunts (combat_ai.cc, colour from ai.txt), skill-use
responses (actions.cc, YELLOW), level-up (party_member.cc, WHITE), and the script float_msg external. So
"floating damage numbers" is a DOCUMENTED PRESENTATION DIVERGENCE built on the engine's real float MECHANISM +
its real float_msg/_colorTable colour vocabulary (interpreter_extra.cc:3150-3190; color.cc RGB555→idx) — NOT an
invented _combat_display colour scheme.
M1 FloatText ports text_object.cc timing/placement: MAX_COUNT=20, baseDelay 3500 + lineDelay 1399 → LifetimeMs =
lineDelay*lines + base (:337, 4899 ms/line); AnchorOffset (16 − w/2, −(h+60)) = textObjectFindPlacement's primary
placement (:379-383; the 8-position off-screen cascade :386-454 simplified to the primary anchor — the camera
clamps the world); + a rise + alpha fade (presentation — the engine's floats are STATIC + NON-fading :338).
M2 viewer: a float is spawned from Log() by parsing the damage int out of the Hexwaste-AUTHORED "...for N damage."
line (NOT a combat.msg game string — Log is the in-memory monitor buffer, never stdout), placed over the defender
tracked at OnAttackStarted/OnThrowStarted. WHY the tracked object, not the Log wording: ResolveAttack keys the
hit/miss text on byDude, so an NPC-vs-NPC blow still reads "...hits you..." (untrustworthy); the tracked object is
also the ONLY signal for the dude AS defender, which OnTargetHit/OnTargetDodge deliberately skip (the camera-anchor
dude doesn't visibly react — P34-M6) — so CombatEngine is UNTOUCHED. One float per tile (textObjectsRemoveByOwner
one-per-owner :276/460) + the global cap; colours = the real float_msg constants: damage RED [31744] over an NPC /
LIGHT_RED [32074] over the dude (a readability shade — the engine distinguishes by message-id, NOT colour, so
documented), crit YELLOW [32747], miss WHITE [32767], black fading outline (idx 0). KEY DE-RISK: Draw-only + wall-
time-ticked → the headless harness pumps neither, so byte-identical (the float spawns DO run headless in Log but
only mutate an in-memory list). DOCUMENTED CUTS: burst collateral bystander floats omitted (the "also catches"
line names a bystander, not the tracked defender — main target still floats); a non-lethal NPC thrown-hit and a
prone/KO miss have no tracked-defender callback → their float is dropped (rare, cosmetic); off-screen cascade =
primary anchor only.

Phase 46 (DONE — "Let There Be (Less) Light", map_update_p_proc wiring + a latent lighting-clobber fix; the P42-
backlogged item, its M0 diagnostic finally run via a STATIC census [IntProgram.FindProcedure, a pure proc-table
read] + a RUNTIME trace). FINDINGS: SCRIPT_PROC_MAP_UPDATE=23 fires once on load AFTER map_enter (map.cc:1010-1011)
then every 600 game ticks (mapUpdateEventProcess), on the map script + every object script defining it; no combat
gate. The census found map_update_p_proc is LIVE on EVERY slice map (map script + many object scripts incl. dcVic/
Kcsulik/KCTorr/dcG2Grd) — NOT dead code (overturns the P42 "might be inert" worry). The runtime trace found it
drives lighting via set_light_level (1 call/map): 5 of 6 re-set level 100 (max, INERT — map_enter already pinned
max); ARCAVES sets level 50 (the P21 "cavern" level) → dims ambient 65536→40960 (62.5%). No unhandled externals
(only debug_msg, a no-op). So the P42 skepticism is RESOLVED: map_update DOES drive lighting (confirmed, not
assumed), but the "day/night curve" framing was wrong — it's a one-shot static cavern set_light_level.
M1 wiring (the user's "full, all scripts" choice): RunMapUpdate (the faithful scriptsExecMapUpdateScripts — map
script + ALL object scripts) right after RunMapEnter; the periodic 600-tick re-run is DEFERRED (no time-varying
map_update content on the slice). TWO faithful prerequisite fixes the diagnostic surfaced: (1) reg_anim_animate_
forever is now IDEMPOTENT per object (the engine has ONE anim slot/object) — artemple's Animfrvr map_update re-
registers the firepits map_enter already did, doubling the record; deduping restores forever=2. (2) A LATENT P21
BUG: RebuildLighting clobbered the script-pinned ambient back to InitialAmbient (it IGNORED AmbientFixed, unlike
the day/night clock at ViewerGame.cs:8606) — so set_light_level only ever "worked" because every shipped value
coincided with max. Fixed: RebuildLighting now PRESERVES the pinned ambient, and AmbientFixed RESETS per map load
so each map re-pins via its own scripts. Net: arcaves' cavern dim (40960) now actually renders (a real, modest
fidelity fix — the cave was lit at 100%).

Phase 47 (DONE — "Drag the Gear", inventory drag-and-drop equip; the P15-M2 spillover). M0 grounding: fo2ce
inventory.cc — the window (499x377) + armor/left/right equip-slot rects + the press→drag→release state machine +
_switch_hand equip/swap (:2386-2537); Hexwaste's inventory is a TEXT-LIST panel (no authentic INVBOX.frm window)
with a flag-toggle equip model.
M1 EquipRules: CanEquip (weapon→weapon slot, armor→armor slot, wrong-type drop rejected — the _switch_hand type
guards) + the EquipSlot enum (Weapon/Armor ONLY — Hexwaste equips ONE weapon, so the engine's LEFT-hand/dual-wield
item2 slot is OUT, a documented simplification: needs the two-handed/item2 proto model + no shippable content
dual-wields).
M2 viewer: two equip SLOTS (weapon + armor) as boxes beside the list; HandleInventoryDrag — drag a row onto a slot
= EQUIP, drag a slot item off = UNEQUIP, a row TAP (no real drag) falls back to click-to-use so click-to-equip is
preserved; reuses the existing flag + ApplyArmorBonus mutations. Loot/barter/trade keep click-on-press (transfer,
not equip). DOCUMENTED DIVERGENCE: slots are boxes beside the text list, not the authentic INVBOX.frm paperdoll
window — an art residual (the Skilldex text-then-art pattern).

Phase 48 (DONE — "Ten Slots", multi-slot save UI; the P5/P7 single-slot residual). Ported the fo2ce
loadsave.cc 10-slot LSGAME screen: pure Formats.SaveSlots (Count=10 LOAD_SAVE_SLOT_COUNT, per-slot
metadata occupied/version-mismatch/character/level/map/date) + a 10-slot picker modal opened from the
Options Save(S)/Load(L) rows; one JSON file per slot (hexwaste-slotN.json), 0-9/click to save into or
load from, load refuses an empty/mismatched slot. F5/F9 stay a SEPARATE quicksave on the default
SavePath. DOCUMENTED DIVERGENCES: a dark text panel, not the authentic LSGAME.frm art (art residual,
the Skilldex text-then-art pattern); no overwrite-confirm dialog; no thumbnail (added P80); and the
Title-screen cold-start "continue/load" is a residual — the in-game picker only loads MID-SESSION
(F9/Options-Load), Hexwaste's title goes Title->New Game.

Phase 49 (IN PROGRESS — "Aim Small", the called-shot click dialog + [P50] the AI-disposition window).
M1 called-shot click dialog (DONE): replaces the P9-M2 V-CYCLE with a click modal (engine's CALLED.frm
picker, combat.cc:5476). V opens a list of the 8 hit locations in engine button order (head/eyes/right-arm/
right-leg/torso/groin/left-arm/left-leg, combat.cc:1894-1907) + uncalled, each showing its to-hit PENALTY
(combat.cc:172: head -40/eyes -60/torso 0/arms -30/legs -20/groin -30); pick feeds the UNCHANGED
TryAttack(target, AimLocation) path. DOCUMENTED DIVERGENCES: single-column text list, not CALLED.frm
critter-pic overlay (art residual, Skilldex text-then-art pattern); live per-part to-hit % is a residual
(static penalty shown). M2-M4 (AI-disposition combat-control window + ally-AI wiring) ship as P50.

Phase 50 (DONE — "Tactics", the AI-disposition combat-control window + ally-AI wiring).
KEY FINDING: the engine's party combat-control window (game_dialog.cc:3354) has 7 LIVE settings, but
TryAllyAction was a 2-line "attack nearest hostile" with ZERO knobs — porting the WINDOW alone would be
cosmetic, so per the prime directive (no inert features, cf. P38's karma rejection) wired REAL ally-AI.
M1 pure Formats.Combat.CompanionAi: enums (Disposition/AttackWho/Distance/RunAway/ChemUse) + Effective()
(non-Custom disposition PRESETS the knobs — Aggressive/Berserk/Defensive/Coward) + ShouldFlee HP-fraction
thresholds + PickTarget priority. GOTCHA (record-struct trap): `new()` zero-inits a record struct (IGNORING
primary-ctor defaults → Berserk/AbjectCoward), so CompanionAi.Default is built EXPLICITLY as Aggressive/
Closest/OnYourOwn/Never/Clean = pre-P50 behaviour. M2 TryAllyAction via ICombatHost.CompanionSettings:
attack-who priority (Closest = old nearest; Strongest/Weakest by HP; WhoeverAttackingMe DEGRADES to Closest
— no per-ally whoHitMe tracker), run-away flee (TryFlee parameterised to take actor AP by ref so allies +
enemies share the one _ai_run_away path), distance (StayClose regroups past 5 hexes / Stay holds / Charge+
OnYourOwn close on target; Snipe back-away is a residual), chem-use heal (reuses P42 TryNpcHeal). Default
(Aggressive) = EXACT pre-P50 behaviour → byte-identical. M3 viewer: combat-control window (OptionsRowRect
modal) from the companion hub ("Set your tactics."); 5 cycle rows + Done, a detail-row cycle flips
disposition to Custom. Persistence: PartyMemberState +5 additive-V2 ints (default = CompanionAi.Default).
RESIDUALS (area-attack + best-weapon rows) CLOSED in P51; lone remaining is a dark text panel, not
control.frm art. Behaviour PROVEN by fake-host tests (slice allies never fight a configured disposition).

Phase 51 (DONE — "Full Tactics", closing the P50 ally area-attack + best-weapon residuals).
Engine: _ai_pick_hit_mode area-attack thresholds (combat_ai.cc:2287 — ALWAYS / SOMETIMES [1/secondary_freq]
/ BE_CAREFUL ≥50% / BE_SURE ≥85% / BE_ABSOLUTELY_SURE ≥95%) + 8 best_weapon options (_weapPrefOrderings,
indexed [best_weapon+1]). KEY FINDING: AiBestWeapon + IsBurstWeapon are zero-dude-coupled (reusable),
AiSwitchWeapon reads ai.BestWeapon (needs a value-overload for allies), the burst path is dude-coupled only
at RollBurst's one attackerIsDude:true. M1 CompanionAi +2 enums: AreaAttack {Never[default]/Sometimes/
BeCareful/BeSure/BeAbsolutelySure/Always} + WeaponPref {NoPref..Random, values MATCH the engine enum so the
int feeds AiBestWeapon directly} + ShouldAreaAttack (deterministic; SOMETIMES is engine rng). M2: best-weapon
— AiSwitchWeapon refactored to a (actor, bestWeapon:int, minToHit:int) overload, called in TryAllyAction's
dry-gun branch with WeaponPref (the P43 enemy switch now reaches allies); area-attack — RollBurst
parameterised by attackerIsDude (default true → dude byte-identical) + TryAllyBurst. M3 viewer: tactics
window 6→8 rows. DOCUMENTED: SOMETIMES uses a fixed 1/3 (allies have no ai.txt secondary_freq); area-attack
to-hit uses single ComputeToHit (not HIT_MODE secondary). Both P50 residuals CLOSED — the window is now the
full engine set bar control.frm art.

Phase 52 (DONE — "Dress the Chrome", a presentation-polish cluster; every milestone Draw-only / wall-time-
only → both golden suites byte-identical, the headless harness pumping neither Draw nor the wall clock).
M0 verified facts FrmDump-FIRST: CONTROL.frm 640x190 / LSGAME.frm 640x480 / LSGBOX 290x85 [the reader's
224x133 was WRONG — dump, don't trust]; PerkId.Empathy=22 cross-checked vs verified Educated=18/Slayer=23/
Sniper=24 in PerkTable, NOT the perk_defs.h line.
M1 Empathy dialogue-reaction colouring (game_dialog.cc:2118): DialogReaction.Classify (GAME_DIALOG_REACTION_
GOOD/NEUTRAL/BAD 49/50/51 → level) tints each option when DudePerkRank(Empathy)>0. DOCUMENTED DIVERGENCE:
uses the raw RGB555 the engine's _colorTable indices encode, no palette-nearest remap (no 8-bit dialogue
palette). Inert by default.
M2 CONTROL.frm tactics-window art (text panel fallback, Skilldex pattern). KEY FINDING: CONTROL.frm is a real
radio/checkbox layout (game_dialog.cc:3389 — TALK@593,41, disposition radios, USE BEST WEAPON/ARMOR
checkboxes), NOT Hexwaste's flat 8-row cycle model — so the readable cycle-rows are overlaid on the authentic
chrome (DOCUMENTED STRUCTURAL DIVERGENCE: they don't bind the engine's individual widgets).
M3 LSGAME.frm + LSGBOX save/load-picker art: authentic 640x480 window (slot-list frame + info box baked into
LSGAME per loadsave.cc); 10 slot rows at window-local (55,87), hovered metadata in info box at (396,254).
M4 called-shot LIVE per-bodypart to-hit %: CombatEngine.PreviewToHit (side-effect-free; mirrors ComputeToHit
+ the halved-for-melee location penalty, clamped 0..95, no roll) feeds the aim dialog.
M5 message-log scrollback: pure MonitorScrollback (display_monitor.cc ring math, Capacity 100) replaces the
5-line cap; scrolls via the engine's two invisible click-halves (display_monitor.cc:382/391 — top older /
bottom newer); a new message jumps to newest.
M6 screen-fade on map transitions: wall-time black-quad fade-IN over MapFadeSeconds 0.35 after each LoadMap.
DOCUMENTED DIVERGENCE: the engine's paletteFadeTo is a modal palette lerp + fade-OUT-then-in; Hexwaste has no
palette texture and a synchronous load → GPU quad fade-IN only, gated out while screenshotting.
EXCLUDED (critic, documented): inventory INVBOX.frm (FID-48 filename unconfirmed in fo2ce source + a
2-slot-vs-3-slot-paperdoll mismatch); egg-mask wall transparency (large 8-bit blend-table kernel — the P4
no-shader decision stands); Snipe back-away (combat-logic, wrong theme for a polish phase).

Phase 53 (DONE — "Lend a Voice", dialogue voiceover). FAITHFUL FORWARD-LOOKING INFRA — the engine's VO path
is real + fully specified but VERIFIED INERT on the slice: every slice dialogue line carries an EMPTY audio
field ({id}{}{text}) — Metzger's 240 lines, Vic's 266, all 17 Den NPCs — AND the GOG game-data ships NO
sound\speech\ directory (only sound\music\). DOUBLY inert (no field + no asset). This is NOT karma-auto-
award-style invention (P38) — VO has a real engine hook (scripts.cc _scr_get_msg_str_speech), just empty on
this content slice. KEY CORRECTION (readers conflicted): the PLAYED audio is FLAT sound\speech\<audio>.acm
(game_sound.cc:1871 _sound_speech_path); the per-head sound\speech\<head>\<audio>.lip path is the LIP-SYNC
file (lips.cc) — OUT OF SCOPE (no talking head, no .lip assets).
M1 MessageFile audio retention: the parser READ then DISCARDED the audio field (2nd of {id}{audio}{text}) —
now a parallel _audio dict + GetAudio(id) keeps non-empty values; purely additive.
M2 pure Formats.Sound.SpeechName: Path(audio) => sound\speech\<audio>.acm (lowercased) + ShouldSpeak(isReply,
headIsValid, audio, msgFlags) — the scripts.cc:2757 gate: REPLY-only (a3==1; game_dialog.cc:2239 reply vs
:2282 option a3=0), head FID is a HEAD (else a3 forced 0, :2746), audio non-empty, 0x01 message flag clear
(set → censor beep, not speech).
M3 wiring: PlayDialogVoice fired on 0x811E gsay_reply + 0x8120 gsay_message ONLY (reply opcodes), and only
for a message-list ref (msg.Tag==TypeInt, never a literal string) — NOT gsay_option (0x811F/0x8121).
AudioManager.PlaySpeech does a one-shot LOOSE read under <gameDir>\sound\speech\ (like music, not the DAT);
headIsValid assumed true since Hexwaste renders no head.
RESIDUAL: lip-sync (.lip + the talking head) stays out — no assets, no head model; VO lights up free when
voiced content installs loose sound\speech\*.acm.

MAINTENANCE (2026-06-21, "tend the god-object" — a quality pass ahead of adding more cities; grounded by a
4-reader + lead-engineer workflow): two changes, both proven SAFE.
(1) --smoke <map> coverage harness: a headless StartupAction (ViewerGame.Harness.cs) that censuses a map
(critters/containers/doors/scripted objects) + reports the FULL set of UNWIRED externals its scripts fire
(map_enter on load + a map_update pass) — the "silent quest gap" detector for a NEW city: run one command,
see what it needs that isn't wired. Deterministic + headless (no walk/UI/RNG), state-only output. 5 per-map
smoke goldens (artemple/arcaves/denbus1/denbus2/kladwtwn) are the cross-map regression net. Example: denbus2
fires use_obj_on_obj + tile_in_tile_rect; KLAMALL fires elevation.
(2) ViewerGame.cs god-object split: 10,279-line file → 4,734 (−54%), concern partials:
- ViewerGame.Harness.cs (1,642 — the 100+ --probe StartupAction dispatch, extracted from LoadContent as RunStartupActions())
- .Panels.cs (1,345 — char sheet/perk picker/Skilldex/Pip-Boy/automap/options/saveload picker/aim dialog/item panels)
- .CombatHost.cs (852 — ICombatHost impl + combat glue: weapon/ammo/reload/corpse/heal/heartbeat/poison/sfx/animation+throw callbacks/reactions/destroy+combat procs)
- .SaveLoad.cs (599 — per-map delta snapshot/replay + JSON SaveGame/LoadGame)
- .Hud.cs (412 — iface.frm bar + monitor + digit roll)
- .Rendering.cs (285 — floor/object sprite draw + outline/translucency)
- .Worldmap.cs (283 — travel/transitions/encounter-engage/Outdoorsman)
- .Chemistry.cs (224 — drugs/addiction)
- plus pre-existing .CompanionHub/.Party/.Tactics
KEY SAFETY INVARIANT: every move is a PURE same-class method move (fields stay CENTRAL in ViewerGame.cs) →
identical IL → goldens byte-identical; build is the fast inner-loop gate, full golden suite the final gate.
The harness extract was the one extract-method (a call-site change, not a pure move) — its golden gate was
mandatory.
STILL IN THE CORE (deliberately not split — too welded / interleaved / small): LoadContent (local functions
close over fields), Update (input distributed, not a method group), LoadMap, the dialog panel, char-creation,
the StartupAction record tree (nested types can't move), and the kills/XP/party-level/skill-points/rest
helpers (interleaved between the two CombatHost clusters).
Method to extract a concern: new ViewerGame.<Concern>.cs with `namespace Hexwaste.Viewer;
public sealed partial class ViewerGame { <methods> }` + the file's 9 usings (ImplicitUsings covers System.*);
cut a contiguous method block (a class member's close is the first `^    }$` — inner braces are deeper),
build, then golden-gate.

## The 13-city run (P54–P66): original-game locations

**Recipe:** a city is mostly CONTENT — the data-driven engine makes most of it free. Per-city steps: M0 reachability (`--travel N` via existing ArriveAt → wmAreaFindFirstValidMap → first-ON entrance → maps.txt → LoadMap at entrance tile; inter-map = STATIC exit grids, P2-M5 ApplyTransition, no code) / M1 new externals / M2 GVAR + proc census / dialogue drive. `--smoke <map>` censuses unwired externals; stubs=0 = all wired. **KEY LESSON: a city's cost = its genuinely-new externals + content; everything else is free reuse, and each city pre-clears the next (shared externals accumulate).**

**Externals-per-city tally:** VC needs the foundation set / Gecko 0 / Modoc 4 / Broken Hills 2 / New Reno 5 / NCR 0 / SF 0 / Redding 0 / Vault 15 0 / Sierra 2 / Military Base 0 / Navarro 0 / Oil Rig 0. **The 13-city run needed engine code on only 4 cities = 13 externals total.**

**Companion towns (real data\party.txt member → proven Vic-pattern party_add machinery, NOT custom content):** Gecko=Lenny (138, pid 0x100006B, member=1 levelMin=10), New Reno=Myron (436, pid 0x10000A0, member=1 levelMin=6), Broken Hills=Marcus (599 @18284, member=1 levelMin=12), Vault 15=Doc/pMDoc (556 @12684, pid 0x10000A2, member=7 levelMin=0), Sierra=Skynet/pMCyberdog (pid 0x1000088, member=1 levelMin=9), Navarro=K-9 (SAME pMCyberdog body 0x1000088, shared). Custom-content recruit (OUT, needs custom companion content): VC=Cassidy (script 571, NOT in party.txt). The member=0 control critter = radscorpion 0x1000005.

Phase 54 (DONE — "Vault City", FIRST new location): 4 maps (vctyctyd/vctydwtn/vctycocl/vctyvlt). M1 two SHARED externals: **day** (0x8119, opGetDay — DayFromEpochDay, mirrors month 0x8118) + **debug_msg** (0x8154 — dev no-op, pop+discard). M2 four seam externals → all 4 maps stubs=0 (also clears arcaves/denbus1/2/KLAMALL stubs): **elevation** (0x80EC, ElevationProvider), **critter_injure** (0x8127 — OR/clear DAM_CRIP 0x7C into CombatResults, honoring DAM_PERFORM_REVERSE 0x800000, P14 flag model), **anim** (0x810C — PlayActionOnce, Draw-only), **obj_on_screen** (0x8150 — return 1, no camera headless, DOCUMENTED DIVERGENCE). GVARs all 0 fresh (TownRep 50/Citizenship 81/Quest 91/Enemy 137). Reader errors corrected: GVAR indices were off by ~7; critter_p_proc already wired. Citizenship-quest machinery wired+proven; navigating Lynette (script 127 @17100) to flip GVAR 81 = content residual. OUT: Cassidy, McClure computer-parts quest (chains to Gecko).

Phase 55 (DONE — "Gecko", SECOND): 4 maps (gecksetl/geckpwpl/geckjunk/gecktunl), stubs=0 — VC wiring already covered everything. **HALLUCINATION CAUGHT: synthesis's "gecksetl stubs=1[debug_msg]" was false — verified stubs=0.** M2 ONE real change: **scenery use_p_proc** — InteractWith fired use_p_proc for containers+doors but a scripted SCENERY object with no exit-grid Destination fell to a no-op (reactor terminal GsTerm 515 @18677, reactor gsReactr 529 @12666, valve GSValve 846 @16264 were inert); added the scenery branch (engine's _obj_use dispatches SCRIPT_PROC_USE for any usable object). DOCUMENTED LIVE-PLAY GAIN: denbus2 graves (diggable in FO2), NR slot machines, wall switches now usable. Tooling: `--party-probe <pid>` + MapDump scripted-scenery listing. Reactor OPTIMIZE + VC-McClure bridge (GVAR VAULT_GECKO_PLANT=82) = content residual.

Phase 56 (DONE — "Modoc", THIRD): 4 maps, [Area 03], entrance modmain. M1 two PURE-QUERY: **tile_in_tile_rect** (0x80CF, interpreter_extra.cc:1447 — HexGrid.TileInTileRect port; engine's ASYMMETRIC corners c1=(minX,maxY)/c4=(maxX,minY), args c2/c3 popped-but-IGNORED) + **critter_inven_obj** (0x8106 — type 3 = Inventory.Count, else handle of Worn/RightHand/LeftHand). M2 two MUTATING: **set_map_start** (0x80A8 — reposition dude/camera to 200*y+x; no-op headless) + **kill_critter_type** (0x80EE, opKillCritterType — deathFrame 0 = silent remove, nonzero = corpse via ConvertToCorpse; count>200 guard; dude excluded). Engine's **_isLoadingGame() guard (:2384) wired FAITHFULLY** — flag wraps LoadGame's LoadMap (the only window restored scripts replay map_enter/map_update) so save-restore never re-destroys critters. M3 CORRECTS a pre-verification note: **use_obj_on_p_proc + timed_event_p_proc are BOTH wired, not OUT**. Only unwired procs Modoc defines = map_exit_p_proc + push_p_proc (PRE-EXISTING engine-wide residuals, denbus2 already defines them, never fired across whole slice). GVARs all 0 (TOWN_REP 52, JONNY_STATE 114, JONNY_TILE 115, TOOL_FLAG 118, ROSE_FLAG 123, JONNY_HOME 129). Quest: Balthas (96 @12323) "Jonny in the Well"; well miWell 572 @17520 fires scenery use_p_proc.

Phase 57 (DONE — "Broken Hills", FOURTH): town proper (BROKEN1/2) ALREADY stubs=0 (ZERO new town externals). Two new externals only on random-encounter SUB-maps (bhrnddst/bhrndmtn): **set_exit_grids** (0x80E6, opSetExitGrids:2180 — pops rotation[DISCARDED by engine]/tile/destElev/map/elevation; rewrites every exit-grid-pid [0x5000010..17] object's Destination, preserving parsed rotation) + **wield_obj_critter** (0x80DA → opWieldItem:1689, SAME handler as wield_obj — pops item THEN critter; weapon→right hand via P43 EquipWeapon, armor→worn+dude-only AC; NPC-armor AC = forward-looking infra, slice wields weapons only). entrance_1→BROKEN2 = static exit grid. GVARs 0 (TOWN_REP 54, FRAUD 147, ENEMY 309, READ_FRANCIS_NOTE 524, MARCUS_DEAD 526, CARAVAN 562). **GOTCHA: .map file is bhrndmtn (maps.txt has a typo "bhrndmnt"); --smoke loads by filename.** **MISREAD CORRECTED: scout's "Marcus = @11689 script 588" was a generic-mutant misread — real Marcus @18284 script 599.**

Phase 58 (DONE — "New Reno", FIFTH + BIGGEST: 11 maps, mob-family city): [Area 07], Newr1 entrance. M1 FIVE new externals (the MOST of any city): **obj_art_fid** (0x8149, opGetObjectFid:4643 — query, pushes Fid; **arity was ALREADY (1,true), stub pushed placeholder 0, so VALUE-fix on Newr2 not stack-desync — do NOT touch ExternalArity**), **critter_is_fleeing** (0x8151:4740 — pushes Maneuver & 0x04) + **critter_set_flee_state** (0x8152:4756 — pops fleeing THEN critter, sets/clears bit), **mark_area_known** (0x80B2:737 — pops markType/areaId/mode → WorldFog.MarkRadiusVisited; INERT, every NR area starts On; mode-1 map-mark + INVISIBLE-hide = documented no-ops), **game_time_advance** (0x80FC:2761 — pops ticks [1:1, TicksPerDay==864000], bumps clock THEN runs ProcessPoison/Drugs/Withdrawals = engine's queueProcessEvents catch-up, NOT just a bump). **KEY CORRECTION — the "all 0 fresh" premise is FALSE for NR: the FOUR crime-family counters SALVATORE 134 / BISHOP 135 / MORDINO 136 / WRIGHT 216 seed to 100 in vault13.gam (P32 SeedGlobalVars), they count DOWN as you wrong a family;** TOWN_REP_NR 55 / MADE_MAN 230 / PRIZEFIGHTER 231 / PORN_STAR 232 / MYRON 284 = 0. **GOTCHA: NR is the first city where a fresh-game GVAR is NON-zero — always check the actual vault13.gam seed, don't assume 0.** PATCH NOTE: Newr2/Newrst in patch000.dat, VFS resolves it.

Phase 59 (DONE — "NCR", SIXTH + CHEAPEST: ZERO engine code): 5 maps (NCR1-4, NCRENT), stubs=0. **KEY FINDING — the one apparent "new proc" is a NON-ISSUE: NCR1 (script 447 SCCop) DEFINES combat_is_over_p_proc, but SCRIPT_PROC_COMBAT_IS_OVER (=27) + _IS_STARTING (=26) are VESTIGIAL enum slots the engine NEVER scriptExecProc's anywhere (scripts.h:76-77 are the ONLY refs in all fo2ce). Hexwaste NOT firing them is CORRECT — wiring it would DIVERGE (the prime-directive trap: a defined-but-engine-dead proc is not a residual). LESSON: a map DEFINING a proc isn't proof the engine FIRES it — check scriptExecProc, not just the proc table.** GVARs 0 (TOWN_REP_NCR 57 + 168/170/172/196). No companion. **(P66 CORRECTION embedded: this entry originally listed a 6th map "ENCRCTR" — a false grep-match on "eNCRctr"; ENCRCTR is the ENCLAVE REACTOR [maps.txt Map 133], moved to P66. NCR proper is 5 maps.)**

Phase 60 (DONE — "San Francisco", SEVENTH): 7 maps (SFChina/SFChina2 Shi Chinatown, SFDock, SFElronb Hubologist, SFTanker PMV Valdez, +2 shuttle), ZERO engine code, stubs=0. No engine-dead-proc trap (NCR lesson held). GVARs 0 (TOWN_REP_SF 61 + 361/363/365/366/444). No companion. (Shi STREET NPCs 743/746 = 0-option guards; named talkers = scripts 813/819.)

Phase 61 (DONE — "Redding", EIGHTH): 6 maps (REDDOWN/REDDTUN/REDMENT/REDMTUN/REDWAME/redwan1), ZERO engine code, stubs=0. **NON-ZERO-SEED TRAP STRUCK AGAIN (caught): GVAR_TOTAL_WANAMINGOS (461) seeds to 20 (Wanamingo Mine initial creature count, you exterminate them), NOT 0;** other Redding GVARs 0 (TOWN_REP 56 / QUEST_REDDING_PROBLEM 94 / MAYOR 334 / SHERIFF 387 / WANAMINGO_OCCUPADO 389). No companion. **LESSON: the non-zero GVAR seed is NOT a New-Reno one-off (a creature/quest tally from vault13.gam); ALWAYS run the fresh-game --get-global check, never assume 0.**

Phase 62 (DONE — "Vault 15", NINTH + first zero-code city WITH a companion): 4 maps (VAULT15/V15ENT/V15SENT/V15_ORIG), ZERO engine code, stubs=0. GVARs all 0 (TOWN_REP_VAULT_15 294 / V15_SEED_STATUS 293 / V15_DARION_DEAD 172 / V15_KILL_DARION 474 — seed trap is real but NOT universal). Companion = Doc/pMDoc.

Phase 63 (DONE — "Sierra Army Depot", TENTH + breaks the zero-code streak): 3 maps (depolv1/depolva/depolvb), [Area 08] **start_state=Off** (DISCOVERED-VIA-QUEST, not worldmap-visible from start; maps load/walk directly, worldmap discovery via mark_area_known [P58] = content-gated, documented divergence). depolva fired TWO new externals: **tile_contains_obj_pid** (0x80BB, opTileContainsObjectWithPid:1057 — query, scans solid+flat objects for pid at tile/elev) + **animate_stand_reverse_obj** (0x80CD, opAnimateStandReverse:1363 — !combat-gated, plays ANIM_STAND[=0]; **DOCUMENTED SIMPLIFICATION: engine plays it REVERSED [lie/sit-down], we play forward via P54 Anim path — cosmetic**). **GOTCHA: tile_contains_obj_pid is ALSO fired by artemple's map_enter, so wiring it dropped artemple's stub → smoke-artemple re-recorded (stubs=1→0); the query's return doesn't gate a golden-visible branch.** GVARs 0 (TOWN_REP 53 / 149/150/152/153/157). Robot/combat DUNGEON (no dialogue talkers, Skynet dialogue content-gated behind assembling the body). Companion = Skynet (assembled at runtime, not a static map critter; party-probe confirms membership).

Phase 64 (DONE — "Military Base / Mariposa", ELEVENTH): 3 maps (mbclose/mbase12/mbase34, maps.txt Map 049-051), ZERO engine code, stubs=0. Pure super-mutant COMBAT DUNGEON (no dialogue talkers, no companion). Single GVAR MILITARY_BASE_FLAGS 215 = 0.

Phase 65 (DONE — "Navarro", TWELFTH — Enclave coastal base): 1 big map (NAVARRO, patch000.dat override). ZERO engine code (the "Enclave external-risk" prediction over-estimated), stubs=0, [Area 15] start_state=Off. GVARs 0 (TOWN_REP_ENCLAVE 62 / ENCLAVE_TIMER 434 / 431/432/441). Companion = K-9 (shared pMCyberdog body 0x1000088; in-world K-9 swaps to party body on recruit = content).

Phase 66 (DONE — "Enclave Oil Rig", THIRTEENTH + FINAL endgame): 7 maps (encdock/encdet/encgd/encpres[Richardson, patch000 override]/encrctr[Reactor]/enctrp/encfite[Frank Horrigan]). ZERO engine code (the "Enclave = external-risk" prediction was wrong — the wired set covers the WHOLE original-game map set), stubs=0, [Area 16] start_state=Off. GVARs 0 (ENCLAVE_ALARM 433 / REACTOR 435 / COMPUTER 440 / MARTIN 441). No companion. **RIDER (P59 correction): smoke-encrctr was a false grep-match mis-grouped under NCR; ENCRCTR is the Enclave Reactor, moved here (golden was always valid).** **MILESTONE: the ENTIRE original-game map set — every town, dungeon, special site, endgame — now LOADS, WALKS, transitions, and runs its scripts (every external wired); remaining gaps are all CONTENT (quest navigation, content-gated recruits), not engine.**

---

## Late polish (P67–P81)

Phase 67 (DONE — "Paperdoll inventory window", authentic INVBOX.frm): renders real art\intrface\INVBOX.frm (**interface FID 48, 499x377 — the P52 "FID-48 unconfirmed" blocker RESOLVED via intrface.lst[48]=INVBOX.FRM**) centred with dude PAPERDOLL (local 176,37,60,100, reflects worn armor) + two equip slots (armor 154,183; weapon→right-hand 245,286; left-hand 154,286 decorative). GOLDEN-SAFETY: all window-relative positions gated on _invBox LOADED (live Draw only); headless falls back to the original x=40 list + x=420 boxes. DOCUMENTED DIVERGENCES: text rows wider than engine's icon column; no left-hand/dual-wield slot (P47 single-weapon model). `--show-inventory`.

Phase 68 (DONE — "AI-packet enemy distance"): ai.txt distance= (parsed since P9, never CONSUMED for enemies) now honored in TryEnemyAction via AiDistanceMode.Parse (stay_close/charge/snipe/on_your_own/stay; absent/"random"/unknown → OnYourOwn = engine's pre-parse -1). Wired: **DISTANCE_STAY** → hold (combat_ai.cc:1223/2361 _ai_move_away/_ai_move_steps_closer return -1) + **DISTANCE_SNIPE** → ranged sniper at melee range (≤2) steps AWAY one hex (combat_ai.cc:3001, simplified one-step kite). RESIDUALS: CHARGE/STAY_CLOSE/ON_YOUR_OWN map to current approach; SNIPE kite is one-step+ungated (no _combatai_rating, no multi-step retreat to ~10). Golden enemies (scorpion pkt8, peasant pkt14) carry NO distance field → byte-identical.

Phase 69 (DONE — "S-tier trio" → turned out a DUO + one verified non-issue): **the audit synthesizer HALLUCINATED 3 facts, inline verification caught all (single-agent audit because the 4 readers got rate-limited → solo synthesis hallucinates; VERIFY every load-bearing claim inline).** M1 **Awareness perk** — examine showed HP unconditionally (over-generous); now HP/AC + wielded weapon gated behind PERK_AWARENESS (proto_instance.cc:294). **PERK_AWARENESS = index 0 (synth's "27" was a HALLUCINATION; verified vs perk_defs.h/PerkId).** M2 dude reaction sprites — removed the three OnTargetHit/OnTargetDodge/OnGetUp early-returns skipping the dude (P34-M6 spillover); dude now flinches/dodges/falls/stands. M3 run-vs-walk speed — **VERIFIED NON-ISSUE (synth's 3rd hallucination): running ALREADY moves faster (FrmDump: HMJMPSAT run=20 fps vs HMJMPSAB walk=10 fps); no code change.**

Phase 70 (DONE — "Finish the Perk Sheet + Script-Set Flee"): M1 curated perk batch (Stat=-1 hardcoded perks the data-driven fold can't express, dude-gated, rank-0 short-circuit). **PerkRules.SkillModifier = perk.cc perkGetSkillModifier verbatim (~14 skill perks: Medic/Mr.Fixit/Thief/Master Thief/Harmless/Speaker/Negotiator/Salesman/Gambler/Ranger/Survivalist/Vault City Training/Expert Excrement Expeditor/Living-Anatomy-Doctor).** CUT: Ghost's Sneak bonus is light-gated (no light model). Adrenaline Rush (stat.cc:256, +1 ST while HP<max/2, conditional) in CritterState.Stat; Quick Recovery (combat.cc:5396, stand in 1 AP); Stonewall (combat.cc:4641, 50% knockdown resist, RNG gated on rank>0); Healer (skill.cc:561, +4*rank min/+10*rank max). **Verified PerkId: Healer=19, AdrenalineRush=79, QuickRecovery=102, Stonewall=104.** M2 script-set AI flee — a critter with CRITTER_MANEUVER_FLEEING (via critter_set_flee_state 0x8152, wired P58) now RUNS on its turn (combat_ai.cc:3074 first OR-clause), in BOTH TryEnemyAction + TryAllyAction; closes the P35 residual.

Phase 71 (DONE — "The Map Remembers Where You've Been II", faithful automap fog): **M0 grounding HEADLINE — the proposed "true-LoS" reveal is NOT what the engine does: object.cc obj_set_seen() (:1443) marks the TILE under each mover, _obj_process_seen() (:3054) flags objects + neighbor spread as OBJECT_SEEN (object.cc:3099 is the ONLY writer). "Seen" is WALKED-TILE accumulation — NO line-of-sight, NO sight radius; Hexwaste's old radius-14 was MORE generous than the engine; porting "true-LoS" would VIOLATE the prime directive (guessing a mechanism).** M1 _seenObjects (refs, can't persist) → _seenTiles (HashSet<int>, persistable); RevealAround marks disc of AutomapSeenRadius=4 (DOCUMENTED APPROXIMATION of _obj_process_seen's ±row/±tile byte-spread, doesn't map cleanly onto the hex grid). M2 SeenTiles folded into MapDelta (rides VisitedMaps → survives save/load AND map revisit). `--reveal <hex>`. Closes the P20 "proximity not LoS, not save-persisted" simplification.

Phase 72 (DONE — "Speak Up", float messages onto P45 CombatTextLayer): M0 the 3 engine float_msg sites — level-up (party_member.cc:1554, white 0x7FFF, font 101), skill-use response (actions.cc:1461, yellow 32747), AI taunt (_combatai_msg combat_ai.cc:3302, per-packet chance/color + message ranges into combatai.msg, TWO randomBetween draws). M3 AiPacket extended with chance/color + attack/run msg ranges; CombatTaunt.Pick; wired ATTACK taunt (attacker) + RUN taunt (new ICombatHost.OnCritterFlee from TryFlee). GOLDEN-SAFETY: dedicated ISOLATED _tauntRng keeps rolls OFF the combat stream (Den humans pkt33 chance=25 taunt on flee; scorpion pkt8 chance=0 short-circuits before any draw). RESIDUALS: MISS/HIT taunts (attacker-vs-defender perspective + per-hit-location ranges) + MOVE deferred. `--taunt-probe` (verified combatai.msg GetText(2009) returns a real string).

Phase 73 (DONE — "Let Them Fight", dude-absent NPC-vs-NPC brawl loop): X-FIGHTING-Y (P16-M3) ran only WITH the dude; now a fully independent faction fight he watches. **M0 HEADLINE RISK: StepTurnOrder's while-loop only RETURNS at the dude's slot (PlayerTurn) or when an NPC acts — a dude-absent brawl with NO dude slot would spin StartNewRound forever on a stalemate.** M1 StartBrawl(dudeSpectator) + _dudeSpectator gates 6 branches (dude EXCLUDED from BuildTurnOrder, never TARGETED, brawl auto-runs EnemyTurn, ends when ≤1 living TEAM remains, PruneEscapedHostiles skipped, dude gets NO XP). **MaxSpectatorBrawlRounds=100 cap breaks the stalemate spin.** `--brawl-watch <map> <gA> <cA> <gB> <cB>` (rings factions adjacent, large pump-dt). **DOCUMENTED FINDING: a real brawl is a faithful flee-DRAW — hurt critters flee (min_hp/hurt_too_much) and scatter rather than fight to death, hitting the round cap with survivors on both teams; the fake-host test (hp:1 critters dying before they can flee) is the deterministic clean-WIN proof.**

Phase 74 (DONE — "Perk/Stat Fidelity", Tier-1 batch from a 25-agent adversarial gap-reaudit): **adversarial verify caught the Penetrate≠bypassArmor distinction.** M1 **Gain-X SPECIAL perks (84..90, CONTIGUOUS over SPECIAL 0..6, stat.cc:252-309)** +1 to primary, hardcoded per-case in critterGetStat → CritterState.Stat; + the gStatDescriptions effective-stat CLAMP (stat.cc:369, a Gain-X/Gifted stack can't exceed 10; 0 clamps up to engine min 1). **GOTCHA: clamp exposed a fake-host test relying on IMPOSSIBLE PE=0 → fixed to PE=1.** M2 weapon perks (proto perk field was Skip(4)'d, byte-safe un-skip): **Accurate** +20 to-hit (combat.cc:4423); **Penetrate** cuts defender DT to 20% — **DT ONLY via a NEW `penetrate` param, NOT bypassArmor (which cuts DT+DR, combat.cc:4535)**; **Knockback** halves shove divisor 10→5 (combat.cc:4651). LIVE: Combat Shotgun (pid 242) carries Accurate, so arcaves-burst-shotgun is correctly 65% not 45% (FAITHFUL re-record — previous golden was WRONG). M3 **has_skill** (0x80AA, was an arity stub failing every skill-gated dialogue branch) ported opHasSkill (interpreter_extra.cc:560 → skillGetValue, returns the VALUE not a bool). M4 **Bonus Move free-move AP pool** = 2*rank (combat.cc:3237), drained by movement BEFORE real AP (animation.cc:2610). **GOTCHA: SpendDudeAp is movement-ONLY, so attacks correctly don't drain the pool.**

Phase 75 (DONE — "Correct the Defects", Tier-2: 2 bugs + 2 perk/AI): M1 **time_of_day encounter BUG** — worldmap.cc:4135 compares the HOUR (gameTimeGetHour HHMM/100); Hexwaste passed RAW HHMM, so If(time_of_day>19) was 1930>19 = always true → Den_D night-only rave encounters (enc_23/24) fired at ANY hour. One-line fix (hhmm/100). M2 **ammo-clip consolidation BUG** — a stack is (StackCount-1) FULL boxes + 1 PARTIAL top; the merge bumped StackCount but IGNORED the incoming partial → two 12-round/24-cap boxes read as 36 not 24 (phantom rounds). New AmmoStack (item.cc:371 itemAdd port: TotalRounds=(StackCount-1)*cap+AmmoQuantity). RESIDUAL: static TransferOne (barter-sell) keeps the box-bump. M3 **Lifegiver +4 HP/rank/level (stat.cc:771) — was INERT (PerkTable Stat=-1; the [0,0,4,...] is the EN>=4 take REQUIREMENT, NOT an effect). The P28 CLAUDE.md notes claiming "Lifegiver→HP folded into CritterState.Stat" were WRONG (DOC-TRUTH FIX).** Progression.HpPerLevel(en, rank) at AwardXp + the SaveLoad recompute (the recompute IS the level-up-HP persistence path, MUST include Lifegiver or reload drops it; perk-rank restore reordered BEFORE it). M4 **AI called shots** (_ai_called_shot combat_ai.cc:2634 — 1/called_freq chance, INT≥5, random body part, revert if to-hit < min_to_hit) on ISOLATED _calledShotRng. LIVE — Arroyo/Den packets have low called_freq → denbus2-fight-flee re-recorded for one Villager head shot (62→42% = -40/2 melee head penalty); scorpion pkt8 called_freq=10000 never fires.

Phase 76 (DONE — "Round Two of Tier-2"): M1 **enemy burst selection** (combat_ai.cc:2285 _ai_pick_hit_mode) via AiBurstMode (ai.txt area_attack_mode → shared AreaAttack enum + secondary_freq; absent → INT<6/dist<10 default spray). **GOTCHA (AP-ordering bug, debugged via transcript-dump assert): the burst check MUST sit BEFORE the single-attack AP deduction — old placement deducted single-AP first, leaving _actingEnemyAp < burst ApCost2 → burst silently never fired.** M2 **faithful item pricing (itemGetCost, item.cc:813)** — barter used raw proto Cost, mispricing looted loaded guns + partial ammo; ItemCost.For adds loaded-weapon rounds×ammoCost/boxCapacity (:831), partial ammo fill-fraction (:847), recursive container (:828). M3 **difficulty spawn-count skew (worldmap.cc:3692)** — EncounterSpawner.Plan skews group size after the count roll: EASY −2 floored at entry min (fixed Enc:(N-N) unchanged), HARD +2 (the encounter-RATE skew at :3406/:3604 was already live). Golden encounter-rats-hard (ARRO_Rats seed 1: 5→6 at HARD).

Phase 77 (DONE — "Dodge the Difference", remaining-AP dodge, the AP-model phase): **M0 — STAT_ARMOR_CLASS gains a combat term (stat.cc:215-242): a critter whose turn it is NOT adds its CURRENT combat AP as temporary AC (×1; ×2 + Unarmed/12 for an unarmed dude with HtH Evade). AP is reset to maxAp for EVERY combatant at round top (_combat_set_move_all, combat.cc:3206/3425), spent down as it acts — so a not-yet-acted defender dodges at FULL maxAp, an already-acted one at leftover, and spending all your AP attacking leaves you easy to hit.** M1 _currentAp dict (write-only de-risk: reset at BuildTurnOrder end, leftover captured at each turn end). M2 the fold — ApDodgeAc into the AC INSIDE ComputeToHit BEFORE the ammo modifier + 0-clamp (combat.cc:4428). FAITHFUL re-records: opening PRE-combat swing keeps no dodge (like engine's pre-_combat_begin attack), then dude attacks on maxAp-5 scorpions drop 47→42%, enemy attacks on dude drop 70→69% (his 1 leftover AP); all outcomes preserved. **brawl-watch's NPC-vs-NPC fight now RESOLVES in 10 rounds (team 1 wins) instead of the 100-round stalemate cap — an RNG-cascade from shifted to-hit, deterministic+sane.** **PerkId.HthEvade=93 (verified by enum-enumeration, anchored on Awareness=0 + SilentDeath=25).** M3 HtH Evade ×2+Unarmed/12 (stat.cc:233). `--ac-dodge-probe`.

Phase 78 (DONE — "Tier 1: Combat AI & player skills"): M1 **Steal/pickpocket (NEW player capability)** — opens the mark's inventory as a Steal screen, each lift runs the real check. ProtoInfo.Size (proto.item.size, byte-safe un-skip skip8→skip4+read4); RandomRoll (random.cc randomRoll+randomTranslateRoll, 4-state day-gated); StealCheck (skill.cc skillsPerformStealing — stealModifier from session count, −4%/item-size and −25 face-to-face [both waived by Pickpocket=37], +20 vs KO/knocked-down, cap 95; steal roll then separate CATCH roll, a steal crit forces the catch). Steal XP 10/20/30… capped at 300−skill; caught → hostile via BeginScriptAggro. Isolated _stealRng. M2 **enemy combat-drug use (completes P42's chem_use)** — a BIPED enemy whose chem_use is sometimes/anytime/always rolls per-mode chance (combat_ai.cc:1028) + quaffs a chem_primary_desire buff (Jet/Psycho/Buffout); per-NPC _npcDrugBonus folded into BonusStats, cleared on combat end (no timed wear-off for NPCs — documented). M3 smarter AI positioning — SNIPE now MULTI-step retreat toward SnipeRange=5 (was one-step, P68); **_combat_safety (combat.cc:2249) — a gun enemy holds a shot whose hex line passes through a living teammate (new HexGrid.IsOnSegment collinear test) and approaches instead.** M4 NPC crippled-arm gate (symmetric to P18 dude gate; reuses WeaponBlockedByCrippledArms).

Phase 79 (DONE — "Tier 2: curated perk batch"): **KEY FINDING (verify-don't-trust-the-notes): the two MARQUEE Tier-2 items were ALREADY SHIPPED — Educated (+2 skill pts/level) wired in P29-M2, Jinxed-in-full landed in P41 (the complete _cf_table, replacing the P29 lose-turn-only stub). My Tier-2 proposal rested on STALE P28 spillover notes.** So reduced to 3 worldmap/encounter survival perks: **Fortune Finder** (PerkId 20, worldmap.cc:3880 — 2× a MONEY item pid 41 caps in a spawn), **Cautious Nature** (80, :3985 — +3 SURROUNDING ring radius), **Pathfinder** (43, :4179 wmGameTimeIncrement — shaves rank×25% travel time; PathfinderTicks = ticks − ticks·rank/4, integer form, sub-tick remainder dropped). **Scrounger (40) is data-present only — NO engine impl (no PERK_SCROUNGER ref in the .cc), left unwired.** RESIDUAL: Tag! (4th tag skill at level 12, needs a tag-picker UI) deferred.

Phase 80 (DONE — "Tier 3: save-slot thumbnails"): each occupied slot carries a screenshot — CaptureThumbnail renders WORLD ONLY (floors→objects→roofs→HUD, no menu panels) to _screenshotTarget, downscales to 224×133, writes a sidecar PNG (hexwaste-slotN.png, race-free GetData, P4 readback pattern). Capture DEFERRED to next Draw (render thread, picker not in shot). *.png gitignored. **KEY FINDINGS that reshaped Tier 3: "worldmap special-encounter pins" was a MISREAD — FO2 special encounters are a blinking travel ICON (ENCOUNTER_ENTRY_SPECIAL → wmBlinkRndEncounterIcon), not fixed map pins; save thumbnails are inherently Draw-only/visual, not golden-friendly.** Remaining Tier-3 items deferred (CONTROL.frm widget-binding already functional via P52 overlay; INVBOX dual-wield needs item2 model).

Phase 81 (DONE — "INVBOX dual-wield slot"): M0 grounding (invbox-dualwield-ground workflow). **KEY ENGINE FINDINGS (corrected my assumptions): FO2 has TWO independent READY weapon slots (left=item1, right=item2) + an ACTIVE hand (gInterfaceCurrentHand), NOT simultaneous dual-fire — only the active hand's weapon fires; you SWITCH hands (interfaceBarSwapHands = 1-currentHand). Equip = OBJECT-instance flags (OBJECT_IN_LEFT_HAND 0x1000000 / RIGHT 0x2000000 / WORN 0x4000000). critterGetItem1 = LEFT, item2 = RIGHT (counter-intuitive). A TWO-HANDED weapon is NOT special-cased at wield (neither _invenWieldFunc nor _switch_hand clears/occupies the other hand) — weaponIsTwoHanded only affects combat. So Hexwaste's prior single-weapon model was a SIMPLIFICATION, and adding the left hand REMOVES a divergence (no off-hand exclusivity to enforce).** IMPL: _activeHand field (default RIGHT) is the lynchpin; equip paths clear ONLY the target hand's bit (default right + no left-hand dude weapon in any golden reduces EXACTLY to old clear-both/set-right). EquipSlot.WeaponLeft added (Weapon stays the right-hand alias for prior callers); INVBOX left-hand slot (154,286) promoted to real; '.' key = SwapActiveHand. Persisted: SaveState.DudeActiveHand (sparse null = right; hand BITS ride DudeInventory.Flags). CUTS: no simultaneous dual-fire (faithful — FO2 switches hands); no two-handed off-hand exclusivity (faithful — engine doesn't enforce); NPC dual-hand not modeled (NPCs force right hand).

Phase 83 (DONE — "Game Shell": the authentic front-door lifecycle, replacing the plain-text tech-demo framing
with the real FO2 art front-to-back). Scoped by a 23-agent survey→adversarial-verify workflow that picked the
game shell as the highest faithful+visible+buildable payoff continuing P82's UI-authenticity push; grounded by a
dump-the-real-FRM pass (the recurring "dump, don't trust" rule). All shell code is in the new concern partial
**ViewerGame.Shell.cs**, gated on _menu != None (pre-game) or game-over, with the prior plain-text screens kept
as the headless/no-art fallback (the proven INVBOX/LSGAME text-then-art pattern) → BOTH golden suites BYTE-
IDENTICAL (16 combat + the full encounter net; the shell is Draw-only and StartInMenu defaults false for every
game golden). The shell STATE MACHINE already existed (MenuState enum; EnterCreation/PickPremade/FinishCreation)
— the work was swapping text-over-black-quad for FRM art + mouse hit-test.
- **M1 main menu** — mainmenu.frm (FID 140, 640×480 Enclave-soldier backdrop) + the six menuup/menudown buttons
  (FID 299/300, 26×26 at the engine rects x=30,y=19+i*41) with misc.msg labels {9..14} INTRO/NEW GAME/LOAD GAME/
  OPTIONS/CREDITS/EXIT + copyright {20}, mouse hover/click + keyboard hotkeys (i/n/l/o/c/e), nmselec0 click sfx,
  looping 07desert menu music (loose ACM, PlayMusic de-dups). Button→reality map: NEW GAME→selector, LOAD GAME→
  the 10-slot picker, CREDITS→M4, EXIT→quit; **INTRO + OPTIONS are greyed-disabled (documented divergences — no
  intro .mve movie, no preferences screen)**. `--menu-probe` dumps the button rects+labels+hit round-trip (window-
  local, deterministic golden); `--menu <pick|create|credits|death>` boots a sub-screen for screenshots.
- **M2 premade selector** — pickchar.frm (FID 174) with the highlighted premade's portrait FRM filling the
  display panel (combat/stealth/diplomat.frm 592×260 — the face on the left, the dark right half taking the stat/
  bio overlay) + SPECIAL/HP/tags + the .bio backstory, and the TAKE/MODIFY/CREATE/BACK buttons (lilredup/lilreddn
  FID 8/9 over the baked labels) + ◄─► cycle arrows. **GROUNDING CORRECTION: the scope candidate's "CUSSEL.frm"
  was WRONG — CUSSEL (FID 420) is the party-member custom UI; the selector buttons are lilredup/dn + slu/sld.**
  **BUG FOUND+FIXED (Linux trap): Path.GetFileNameWithoutExtension won't split on '\' on Linux, so "premade\combat
  .gcd" yielded the key "premade\combat" → the portrait/bio paths were wrong (stats showed, portrait/bio blank);
  PremadeBase now splits on '\' by hand.** MODIFY seeds the editor from the premade's SPECIAL/tags.
- **M3 creation editor** — edtrcrte.frm (FID 169) as a UNIFIED screen (the 3 create sub-states stats/traits/tags
  render it; the active sub-state drives the highlight): SPECIAL bignum digits (reuses the EDTREDT sheet's BIGNUM
  FID 170 at x=58, the value recess) + char-points counter, a live derived readout, the 18-skill tag picker, the
  16 optional traits in two columns, a stat.msg/skill.msg/trait.msg description card, Done/Cancel. The +/- steppers
  are BAKED into edtrcrte.frm (click-bands only, no overlay). Reuses AdjustCreateStat/ToggleCreateTag/ToggleCreate
  Trait/FinishCreation; mouse on steppers/rows/buttons + the unchanged keyboard flow.
- **M4 bookend** — credits scroll (credits.txt: '#' section gold / '@' role tan / name green, bottom-to-top, ESC/
  click exits) from the CREDITS button; the death screen now renders death.frm (FID 310) behind the game-over
  options. **KEY FINDING: death.frm + the ending slides do NOT use color.pal — rendering them with the game palette
  GARBLES them (confirmed via FrmDump too); endgame.cc endgameEndingLoadPalette loads art\intrface\<name>.pal per
  scene. New LoadFrmWithSiblingPalette (death.frm→death.pal) fixes it — and is the foundation the victory endgame.txt
  slideshow will reuse (each EG_*.frm has its own EG_*.pal).** The victory slideshow itself stays content-gated
  (no quest completes on the slice) — forward-looking, like the P53 VO infra. Adversarially reviewed.

Phase 84 (DONE — "Easy/Hard combat damage modifier"): closed a real latent gap surfaced by the P83 re-verify
workflow — the `--difficulty easy|normal|hard` setting drove only the WORLDMAP (encounter rate/group size) and
changed NOTHING in combat. Wired the engine's combat-difficulty damage modifier (Easy 75% / Normal 100% / Hard
125%), **ported from fallout2-ce src/combat.cc attackComputeDamage()**: the modifier is applied to the post-÷2
damage BEFORE the DT subtraction (combat.cc:4602), and gated on `attacker.team != gDude.team` (combat.cc:4554) —
i.e. ONLY attackers NOT on the dude's team are scaled, so the dude's and the party's own blows are untouched.
IMPL: a pure `CombatDifficulty.DamageModifier(GameDifficulty)→75/100/125`; a new `ICombatHost.Combat
DifficultyDamageModifier` (default 100 → the fake test host + any Normal game is identity) overridden in the
viewer off the same `Difficulty` the worldmap reads; a `CombatEngine.DiffDmgMod(attacker)` helper (dude or a
PartyMembers entry → 100, else the host modifier) threaded into every damage path — RollAttack (the 3 call
sites: dude/ally pass 100, EnemyAttack passes the modifier), the burst main + cone-collateral, the throw pre-roll
(dude-only → 100), and the inline Explode raw. The modifier rides as a trailing optional param (default 100) on
CombatMath.RollDamage/RollWeaponDamage/ReduceByArmor + RangedMath.RollDamage, so every existing caller is
unchanged. GOLDEN-SAFE: all golden scenarios run at the Normal default (modifier 100 = identity) → 16 combat +
the full encounter net BYTE-IDENTICAL. Proven live by 6 new tests: the 75/100/125 mapping (theory), the post-÷2/
before-DT scaling on the ranged + melee formulas, and two fake-host end-to-end checks — an enemy punch hits the
dude harder on Hard / softer on Easy, while the dude's own punch deals identical damage regardless of the setting
(the team gate). 769 Formats tests.

Phase 85 (DONE — "Integer zoom camera"): shipped the "optional integer zoom" the CLAUDE.md mission has
promised since day one but never built — Camera.cs had only PanX/PanY and the world drew at 1:1 pixels.
DESIGN (chosen to keep the faithful src/tile.cc projection PRISTINE): the camera math stays in logical
(un-zoomed) pixels; zoom is a VIEWER-layer transform. The world renders in its own SpriteBatch under a
`WorldZoomMatrix()` (scale by _zoom about the screen centre, identity at 1×) and the FloorRenderer's
BasicEffect.World gets the same matrix; the HUD/UI + worldmap draw in a SECOND, native batch on top, so
the 640×480 chrome never scales. Input inverts the same transform: `ToWorldPoint(screen)` →
`(p−centre)/zoom + centre` feeds every mouse→world pick (hover/attack/examine via PickObject, click-to-move
+ the cursor hit-test via a new `PickHex` helper); the world-anchored hex-ring cursor (drawn in the native
batch) is forward-transformed by `ToScreenPoint` + scaled. Mouse WHEEL zooms 1×..4× (the wheel was unused);
`--zoom N` sets it for screenshots. Thumbnails force 1× (the canonical world view). GOLDEN-SAFE: the golden
suites are headless TEXT transcripts that never render and never read the wheel, and 1× is identity — 16
combat + 178 encounter BYTE-IDENTICAL. Verified VISUALLY: artemple screenshots at 1×/2×/3× show the world
magnifying about centre (the dude stays centred) while the HUD bar + HP/AP text hold native size/position.
The cull in DrawFloors is conservative under zoom-in (logical viewport ⊇ the visible region) so no tile is
wrongly dropped. CUTS (documented): magnify-only (no <1× zoom-out, which would need a wider floor cull +
show map borders); zoom anchors on the screen centre, not the cursor.

Phase 86 (DONE — "Authentic Loot/Barter/Trade windows"): replaced the dark text-box item panels with the real
FO2 interface chrome, following the proven P67 INVBOX pattern (lazy-load on first live Draw; headless the
texture stays null → the panels fall back to the boxes layout → every golden byte-identical). FIDs + dims
DUMPED from master.dat, NOT guessed (the recurring "dump, don't trust" rule — and it CORRECTED my own P83-
derived doubt: I suspected FID 420 was CUSSEL, but intrface.lst line 420 = trade.frm, so the original survey
was right): **loot.frm (FID 114) 537×376** centred container window; **barter.frm (FID 111) 640×191** and
**trade.frm (FID 420) 640×190** bottom strips (your list left, their list right, the offer-table divider +
OFFER/TALK between). IMPL (all in ViewerGame.Panels.cs): `ItemWindowArt()` picks the active backdrop by mode
(_lootContainer→loot, _barterNpc→barter, _tradePartner→trade) + its placement; `DrawItemWindow()` draws it
behind the lists; `ItemPanelRegion(logicalX)` maps each panel's logical X (40=their list→right slot, 420=your
list→left slot; loot's single list→the central scrollers) to the window-relative row origin, and BOTH the
renderer (DrawItemList) and the hit-test (ItemRowRect) route through it so a click always lands on the row it
draws. GOLDEN-SAFE: the only --panel-click golden is the INVENTORY (no loot/barter/trade open → ItemWindowArt
null → the new branch never fires); the use-hex goldens hit scenery not containers → 16 combat + 178 encounter
BYTE-IDENTICAL. VERIFIED VISUALLY: a denbus1 footlocker renders the loot.frm chrome with its items as text+icon
rows; --open-barter (a new screenshot aid) on Metzger renders the barter.frm strip with his stock in the right
slot. DOCUMENTED DIVERGENCE (the P67 one): Hexwaste is a TEXT list, not the engine's 64×48 icon grid, so a long
item name + price can overrun the narrow art slot; and the offer-table mechanic isn't modelled (barter stays
direct click-to-buy/sell). trade.frm shares the barter strip path (verified via barter). New harness:
--open-barter <hex>.

Phase 87 (DONE — "Talking-head dialog screen"): the iconic FO2 animated head now renders above the
conversation panel — the marquee visible feature, replacing a dark box of green text. The "no head model"
premise was FALSE (the P82 survey's correction held): 187 head FRMs ship in master.dat, and the script DOES
hand a head to start_gdialog. **KEY GROUNDING (dump, don't trust): the script passes a BARE head INDEX, not
a FID — the engine builds it via buildFid(OBJ_TYPE_HEAD, headId, …) (interpreter_extra.cc:1919); -1 = head-
less.** PIPELINE (5 steps, end-to-end): (1) ScriptContext.DialogSessionStart(headId,bg) captures the index
(IntVm already extracted + passed it to the no-op stub — the memory's "IntVm drops it" was inaccurate; the
loss was the un-overridden stub); (2) DialogSession.HeadId exposes it; (3) ArtIndex resolves OBJ_TYPE_HEAD
FIDs — **ported art.cc artBuildFilePath() head branch: name = heads.lst-base + _head1[anim] + _head2[anim]
(+ a 1-based fidget number for the 'f' kind)**, e.g. anim 4 → ELDERNF1 (neutral fidget), anim 10 → ELDERNP
(neutral talk); TypeDirs extended with "heads"/"backgrnd"/"skilldex" (was 8 entries, head=8 threw); (4)
FrmCache loads it (heads use color.pal — confirmed via FrmDump, UNLIKE P83's death/ending sibling-palette
art); (5) DrawTalkingHead draws the neutral-fidget pose centred above the panel, frames CYCLING on a wall-
time tick for an idle living head. GOLDEN-SAFE: heads are Draw-only and the new HEAD: diagnostic line sits
OUTSIDE the encounter FILTER (verified: no fixture contains REPLY/HEAD) + combat scenarios never dialog → 16
combat + 178 encounter BYTE-IDENTICAL. VERIFIED: the Elder head (ELDERNF1 388×200) renders correctly with
color.pal (FrmDump); --force-head (new debug aid) shows a perfect talking head above a real Metzger dialog
in-game; 836 Formats tests incl. 2 new head-FID-resolution tests (fidget + talk naming). DOCUMENTED CUT: no
.lip phoneme lip-sync (the .lip timing files aren't shipped) — the fidget idle is the authentic core; the
emotion is fixed neutral (reaction-driven good/bad fidgets are a forward step). LIVENESS: the pipeline fires
for ANY dialog that supplies a head; the immediate-slice entry NPCs (Metzger et al.) are head-less text NPCs
and Arroyo's head NPCs (Elder/Hakunin) sit behind the vsuit.mve intro cutscene, so a clean headless real-NPC
capture wasn't scripted — but the render path is identical to the --force-head proof. New harness:
--force-head <index>, plus a HEAD: <id> dialog diagnostic.

Phase 88 (DONE — "Pip-Boy Archives quest log"): the long-inert ARCHIVES tab (it logged "No archives.") now
shows the real Fallout 2 quest log. **It is NOT content-gated-empty: some quests have displayThreshold 0
(e.g. "Retrieve the GECK for Arroyo"), so the log is populated from a fresh game** — the verifier's "nothing
to populate" worry was wrong, caught by reading the data. PIPELINE: pure Formats.QuestLog.Parse(data\
quests.txt → Quest{location, description, gvar, displayThreshold, completedThreshold}, '#'/'//' comments
skipped) — **ported from fallout2-ce src/pipboy.cc questInit()/pipboyRenderQuestList()**; the viewer's
DrawPipboyArchives iterates the quests, shows each whose live GVAR (_scriptHost.GlobalVars.GetValueOrDefault)
≥ displayThreshold, groups them under the town name (map.msg <location>), numbers them per-town, pulls the
line from quests.msg <description>, and dims it + appends "(done)" once the GVAR ≥ completedThreshold.
ARCHIVES/STATUS tabs toggle _pipboyArchives; the page's nav rows are Status/Close. **KEY GROUNDING (dumped):
the script passes ids into THREE message lists — location→map.msg (1500=Arroyo/1501=Den), description→
quests.msg, the engine's "(quests)" suffix→pipboy.msg; all CRLF {id}{}{text}, parsed by the existing
MessageFile.** GOLDEN-SAFE bar ONE deterministic re-record: the quest rows are Draw-only, but I added
`archives=` to the --menu-click state probe → menu-click-pipboy re-recorded (gains "archives=False"); new
menu-click-archives golden asserts the page opens. VERIFIED VISUALLY: a fresh artemple Pip-Boy shows
"Arroyo / 1. Retrieve the GECK for Arroyo."; forcing GVARs 191=1 + 619=2 adds "Rescue Nagor's dog" and
"Find Vic the Trader. (done)" (the dimmed completed marking). 838 Formats tests incl. 2 new QuestLog parser
tests. DOCUMENTED CUT: holodisks + the alarm-clock stay out (separate subsystems); quest PROGRESSION to
completion is content (the GVARs advance via quest scripts), but the log + thresholds + the from-fresh
displayThreshold-0 quests are all live. New harness: --menu-click pipboy-archives.

Phase 89 (DONE — "FO2 dialog screen", a P87 follow-up after user feedback that the head floated over the
bright live world). The engine's dialog is a SCREEN TAKEOVER: the captured scene is DARKENED, the head sits
in the UPPER area, and the reply/options fill the lower panel — not a head hovering over lit gameplay. Fixed
to match, grounded in src/game_dialog.cc constants: a full-viewport dim quad (black α175 ≈ the engine's
darken-blend) drawn first; then the dialog is laid out in a 640x480 frame centred on screen — the head at the
display anchor (126,14) at natural size, centred within the ~388px head area; the reply/options panel anchored
in the lower-frame reply region (frameY+219, ≈ the engine's REPLY_WINDOW 135,225 / OPTIONS_WINDOW 127,335)
instead of pinned to the screen bottom. Head-less dialog + the companion hub also dim now (the engine dims all
dialog) but keep their bottom panel. GOLDEN-SAFE: pure Draw-only — the --talk-seq dialog goldens print REPLY/
OPTION (filtered) and never render, so 16 combat + 179 encounter BYTE-IDENTICAL. Screenshot-verified: the
dimmed scene + top head + lower panel read as the FO2 dialog screen.

---

Phase 10 (DONE — "The Wasteland Bites Back"; trailing duplicate in this range, full text in CLAUDE.md): M0-M5 worldmap encounters + companion lifecycle. **Durable CORRECTION: the phase-10 research notes' "partymbr.msg list-14" claim was UNVERIFIED and WRONG — partymbr.msg does not exist in the game data, "partymbr" appears nowhere in fallout2-ce, and message list 14 resolves to Generic.int/generic.msg.** The engine has NO dedicated wait/follow/dismiss UI: recruit/dismiss are plain party_add/party_remove (interpreter_extra.cc:3943/3956 → party_member.cc:375/426) called from a companion's own talk_p_proc reply procedure (game_dialog.cc:2080); the hub reproduces those side effects. The only dedicated party UI is the AI-disposition combat-control window (game_dialog.cc:3354) — SUPERSEDED, shipped in P50. **GOTCHA: companions travel OUTSIDE map deltas — exclude PartyMembers from EVERY CaptureMapDelta loop or an F5 duplicates them on load.** v1 cuts now SUPERSEDED (wait/dismiss persistence #2/#3, per-member If()/Distance [P10 + P16-M4 lowercase-if fix], X-FIGHTING-Y [P16-M3], projectile tween [#11] all shipped); lone residual = Vic's radio ITEM has no in-slice source (one --give).

---

Phase 100 (DONE — "Finish the Game"): the four-point closeout, each grounded + adversarially verified
against fallout2-ce before a line was written, all committed to main with byte-identical golden suites.

**Point 1 — Victory endgame slideshow + death-ending narration (the win condition).** Ported from
src/endgame.cc. KARMA DECISION (grounded + verified): the engine NEVER auto-awards karma/town-rep/ending
GVARs — scripts set them via set_global_var (already in the VM) — so this is purely a SELECTION CONSUMER,
not an invented karma system (combat untouched). Formats parsers EndgameEnding (endgame.txt, 52 active
rows, C-atoi field parse incl. the inline-# → direction-0 quirk) + EndgameDeathEnding (enddeath.txt +
faithful %-weighted selector, Modoc-shitty-death forces record 12). Wired endgame_slideshow (0x8146) +
endgame_movie (0x8148) externals. MenuState.Endgame slideshow: keeps every slide whose GVAR == value,
renders each FRM with its sibling palette over black + narrator ACM voice-over (from the DAT, subtitle
timing scaled to the real speech duration) + word-wrapped subtitles; hands off to the credits scroll. Death
screen shows the enddeath.txt-selected narration (display-only → combat game-over transcripts unchanged).
--endgame-probe / --death-ending-probe + scripts/endgame-golden.sh. DP.FRM desert-pan (art 327) is
dead-in-vanilla (commented rows only) → static blit, full pan deferred.

**Point 2 — Silent quest-gap census + opening-spine golden (QA tooling).** Per-quest QA across ~150 quests
is a manual marathon; this makes it tractable without editing --smoke (golden-locked). IntProgram.
ReferencedExternals() statically linear-decodes a script's bytecode (mirrors IntVm.Execute: push consumes
4 operand bytes; external = 0x8000|(op&0x3FF) in ExternalArity.Table) to surface externals in dialog/use
branches --smoke can't see. IntVm.WiredExternals (140 opcodes, kept adjacent to ExecuteExternal). tools/
ProcAnalyze emits "procanalyze: map=… externals=… wired=… stubbed=[…]" — on the opening spine it finds
gfade_out/gfade_in + tile_is_visible, invisible to --smoke (invariant: smoke stubs ⊆ census stubbed).
scripts/opening-golden.sh locks artemple→arcaves→arvillag→argarden→arbridge (census + map-update + the
--goto-map chain). Dynamic --census (drive talk/use procs) deferred — the static scan is the authoritative
superset.

**Point 3 — Map-script combat-over hook (New Reno prizefight).** GROUNDING CORRECTION: combat_is_starting/
combat_is_over (SCRIPT_PROC 26/27) are VESTIGIAL — never scriptExecProc'd; porting them would DIVERGE from
vanilla. The real mechanism is _scr_end_combat (scripts.cc:2848): on a NON-LETHAL dude knockout, run the MAP
script's combat_p_proc with fixedParam = the KO'er team, and if it script_overrides, end the bout gracefully
(the ring caught the KO) instead of a game-over. ScriptHost.RunMapCombatOver + ICombatHost seam + CombatEngine.
KnockOut(critter, knockedOutBy) → RequestTerminateCombat on override. --combat-over probe + combat-over-newrba
golden (the Boxing Arena map script defines no combat_p_proc → hasProc=false, documenting that a LIVE fight
needs the dynamically-spawned-boxer content — content-gated, not guessed). Inert by default → byte-identical.

**Point 4 — Optional authenticity (car, holodisks, quest-log).** CarState ports worldmap.cc fuel math
(wmCarUseGas discount tiers / wmCarFillGas / wmCarIsOutOfGas), wiring the previously-silent-no-op metarule
car externals give_car_to_party(31)/give_car_gas(32)/car_current_town(30) + metarule3 110; Fuel defaults to
CAR_FUEL_MAX (fo2ce) so metarule3(110) stays 0 → byte-identical (acquisition + worldmap travel-speed/UI
deferred as content/presentation). HolodiskLog (holodisk.txt) + a Pip-Boy Archives holodisk-list section
(gvar != 0 gate). Quest log confirmed to populate from set_global_var (GECK quest, displayThreshold 0).
--quest-probe / --holodisk-probe / --car-probe. Lip-sync verdict: BUILDABLE (the narrator ACMs ARE in the
DAT — the "no speech assets" premise was loose-files-only) but CONTENT-GATED on the Arroyo→Den slice (no
voiced lines) → deferred; the P87/P89 head fidget already ships, only .lip phoneme timing + reaching a voiced
map remain.
---

Phase 101 (DONE — "The Last Mile"): three buckets past the P100 completion — content wiring that P100's
hooks unblocked, QA tooling to make the ~150-quest manual QA tractable, and discrete engine gap-fills. Each
grounded + adversarially verified against fallout2-ce before coding; all committed with byte-identical golden
suites (bar two reviewed, sane re-records). See [[p100-endgame-qa-prizefight-car]].

**BUCKET 1 — content wiring (car / prizefight / lip-sync):**
- CAR gameplay payoff: WorldmapTravel.TravelLeg drives _carStride pixels/step (4 + blower + Reno + 3×super,
  worldmap.cc:3025) but rolls/ticks/burns once; CarState.UseGas drains 100/step; out-of-gas strands the party
  on cardesrt (worldmap.cc:3054). ArriveAt parks the car (car_current_town). Persisted (additive V2). Foot
  travel stride 1 → byte-identical. --car-acquire harness + car-travel/car-outofgas goldens.
- PRIZEFIGHT: the FSM is content fo2ce can't source (no .ssl); its engine hooks already exist (P35 critter
  combat_p_proc + P100 combat-over). Wired game_ui_disable/enable(0x8133/0x8134) — the round-cutscene input
  lock used New-Reno-wide (census: Newr2 11→9, arcaves 7→5, sane re-records). Full ladder deferred as content.
- LIP-SYNC: the "no speech assets" premise was FALSE — 1029 per-head .lip + ACMs are in master.dat. LipData
  parses the v2 .lip (big-endian, verified vs ELDER\AELD1.LIP: 64 phonemes/65 markers) + the 42-int
  PhonemeFrame table (game_dialog.cc:320) + anim ids 9/10/11 (art.h, correcting the brief's 10/11/12).
  PlayDialogVoice loads the per-head ACM+.lip from the DAT + DrawTalkingHead animates the mouth to the
  phonemes over playback position. Live on the Arroyo Elder (voiced + reachable). --lip-probe golden.

**BUCKET 2 — QA tooling:**
- census-sweep.sh: a VM-free ProcAnalyze census over 16 maps (one per story region) locking each map's
  wired/stubbed external counts — a game-wide "silent quest-gap" regression net. Zero load failures.
- dynamic --census: drives the arg-free interactive procs (use_p_proc/description/critter_p_proc + DFS every
  talk_p_proc dialog branch) → the CONFIRMED-EXECUTED stub set. Invariant verified: dynamic ⊆ static. Arg-
  taking procs (use_obj_on/use_skill_on) left to the static superset (documented scope cut). Normalizes the
  descriptive _stubbedExternals keys + filters the fetch_external/store_external forms.

**BUCKET 3 — engine gap-fills (byte-identical/sane-rerecord wins; HIGH-risk deferred):**
- SPECIAL-ENCOUNTER FIX (byte-identical): dropped the IsTransient filter that wrongly rejected the 6 saved=Yes
  special maps (crashed whale, Cafe of Broken Dreams, …), silently degrading them to random desert
  (worldmap.cc:3640 wmRndEncounterPick — the map is chosen unconditionally, saved= never gates it).
- whoHitMe RETALIATION: a struck critter remembers its attacker (MapObject.WhoHitMe, transient, team-gated)
  and TryEnemyAction prefers that avenger over nearest when it's a live cross-team combatant (combat_ai.cc
  _ai_danger_source). Combat goldens byte-identical (lone-dude duels: whoHitMe == nearest). Lone re-record:
  brawl-watch (2v2) now retaliation-targets → team 2 wins (was 1) — a reviewed, faithful behavioral change.
- DEFERRED: combat hit/damage HIGH tier (darkness/LONG_RANGE/SCOPE/stray-shot — forces ~all combat re-records),
  METARULE_ELEVATOR(15) (additive, needs a table port + probe), egg-mask per-pixel wall transparency (cosmetic,
  re-records rendering goldens). The combat LOW tier (perk range mults) is inert on the slice (no slice weapon
  carries perk 58/64) → pure forward-looking infra, not built.
---

Phase 102 (DONE — "Zero Stubs"): a full-game census sweep (ProcAnalyze over all 155 maps) found 87 maps
referencing ≥1 unwired external, dominated by cosmetics (gfade ×58, item_subtype ×43, play_sfx ×16). Wired
ALL ~16 remaining externals; result: 0 of 155 maps reference an unwired external. Arities/returns
adversarially verified against fo2ce interpreter_extra.cc + ExternalArity.cs (zero mismatches — a wrong pop
corrupts the VM data stack). ALL 16 combat + 185 encounter goldens BYTE-IDENTICAL — the wired externals fire
only in dialogue/use/timer branches the goldens don't drive, and the map_enter paths that run don't query
them; only the static-census fixtures re-record (stub counts → 0). Tiers: A cosmetic (play_sfx,
animate_stand_obj, gfade_out/in, reg_anim_play_sfx, art_anim bit-op, sfx_build_char/weapon_name→PushString);
B queries (item_subtype→ITEM_TYPE, proto_data→universal+item members, tile_is_visible→camera-proximity);
C object/inv (inven_cmds cmd13, inven_unwield, use_obj→use_p_proc, drop_obj, scr_return store-only); D
radiation counter (dude-only, resistance, clamp — mirrors the P35 poison model). Deferred layers: proto_data
NAME/DESC strings, the scr_return use_obj_on gate flip, the radiation delayed-damage band model. KEY: the
campaign's entire STATIC external-demand surface is now covered — what remains to "finish the campaign" is
per-quest playtest QA + the deferred content (prizefight ladder, car acquisition, rad band effects), not
engine wiring.
---

Phase 103 (DONE — "Quest E2E PoC"): a proof-of-concept end-to-end QUEST test driver, proving the ~150-quest
manual QA can be turned into repeatable goldens. scripts/quest-golden.sh drives the "Free Vic" quest (Arroyo
GVAR 619 FIND_VIC) to completion through the REAL game logic — a fresh game seeds 619=1 (active); buying Vic's
freedom from Metzger (give 2000 caps + his radio, then the 3-NPC --talk-seq dialogue) drives 619→2 via the
dialogue VM's set_global_var (NOT --set-global faking); asserts the lifecycle via --get-global (1→2) +
--quest-probe (the Pip-Boy "Find Vic" quest flips completed=1) + --party-count (Vic joins). Captured lines are
STATE/ID only (get-global/quest-item/quest-probe/party) — never the copyrighted dialogue text. Deterministic
(--rng-seed). Additive (new script + tests/golden-quest/) → all goldens byte-identical. TEMPLATE: each further
quest = author its scenario (discover its GVAR + dialogue option path, assert the lifecycle) — the per-quest
QA is now automatable, not purely manual. LIMITS: dialogue/combat quests are the sweet spot; fetch/timing/
multi-map quests need more harness plumbing or stay manual.
---

Phase 104 (DONE — "Klamath + Den quest suite"): extended the P103 e2e PoC into a real Klamath + Den quest
regression net. A headless discovery workflow (MapDump/ProcAnalyze/DatDump + decompile, adversarially
verified) mapped each region's quests to GVAR + giver hex + script (location codes are INVERTED vs the
brief: 1501=Den, 1502=Klamath). The dialogue OPTION paths (bytecode emission order != keypress order) were
nailed dynamically with --talk-seq. scripts/quest-golden.sh now drives 3 quests via the real dialogue VM
(set_global_var, not --set-global): quest-free-vic (Den, FULL lifecycle — 619 FIND_VIC 1→2 + 100
QUEST_VIC_DEVICE 0→2, both completed, Vic joins); quest-smitty-carpart (Den, GVAR 550 accept 0→1 via
--talk-seq 22137 1,1,1,1,1); quest-torr-brahmin (Klamath, GVAR 182 accept 0→1 via --talk-seq 24291 1,1).
Asserts each lifecycle via --get-global (before/after) + --quest-probe (the Pip-Boy quest flips
hidden→active→completed). Captured lines are STATE/ID only — never the copyrighted dialogue text.
Additive → all combat/encounter/opening goldens byte-identical. The remaining Klamath+Den quests are
authorable on the same template (kill/escort/item-gated need stronger chars / more harness plumbing —
documented): Rat God 390 (hard kill), Rescue Torr/Smiley 391/197 (escort), Refuel Still 198 (item-on-object).
---

Phase 105 (DONE — "Kill quest + escort finding"): added a KILL quest to the e2e suite + a --kill harness.
scripts/quest-golden.sh gains quest-kill-ratgod (Klamath GVAR 390): killing Keeng Ra'at (klaratcv elev 2,
hex 25486) fires its destroy_p_proc which unconditionally sets 390=2 (FULL 0→2 lifecycle). New --kill <hex>
harness drives the REAL death path (CombatEngine.Kill → KillCritter → RunDestroyProc → destroy_p_proc)
deterministically — a fresh test char can't win the boss fight, so the debug kill is the cause of death but
the QUEST logic (destroy_p_proc → set_global_var) is real. Additive (new StartupAction + flag) → all 16
combat goldens byte-identical. The suite is now 4 quests (Free Vic full / Smitty accept / Torr-brahmin accept
/ Rat God kill). ESCORT quests (Rescue Torr 391, Rescue Smiley 197) — CONFIRMED BLOCKED: their completion
fires via leave_player when the escorted follower reaches an exit grid, and that mechanism is UNWIRED in
Hexwaste (grep found no leave_player / escort-exit handling anywhere; no talk path flips 391/197). This is a
genuine engine gap, not a test gap — a faithful escort e2e needs the follower-on-exit-grid → leave_player
mechanism wired first (a real feature: temporary escort follower + exit-grid crossing detection + the
leave_player proc). Not faked.
