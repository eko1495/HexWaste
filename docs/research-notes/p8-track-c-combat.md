# P8 Track C — Combat Depth II (research findings)

Date: 2026-06-13. Repo v0.7.0, denbus2 baseline.

## Q6a. Bench (done first)

`--map denbus2.map --bench 400` → **370 frames after warm-up: avg 2.90 ms, p95 5.32 ms, max 9.47 ms (~345 fps uncapped)**; palette uploads 66, cycling FRMs 1.
Verdict vs 8 ms canary: avg/p95 comfortably under; max 9.47 ms is a single-frame spike (GC/first-load), not a sustained breach. **Green — headroom for crits/burst math, which is per-attack, not per-frame.**

## Q1. ai.txt packets

**File**: `data\ai.txt` in master.dat — 187 packets, INI sections keyed by name, `packet_num=` gives the id our MAP critters reference (MapFile.cs:432 already parses it; currently UNUSED).

**Fields per packet** (parse: combat_ai.cc:355-470):
- Always present (187/187): packet_num, aggression, min_to_hit, min_hp, max_dist, called_freq, secondary_freq, chance, color/outline_color/font, body_type/general_type (mostly), + 24 hit_*/miss/attack/move/run taunt message-range ids.
- Optional: hurt_too_much (mask of "blind, crippled, crippled_legs, crippled_arms" → DAM_* flags, combat_ai.cc:240-249), run_away_mode (none/coward/finger_hurts/bleeding/not_feeling_good/tourniquet/never → _hp_run_away_value {0,25,40,60,75,100}%, line 253; parsed index is DECREMENTED, line 457), distance (stay_close/charge/snipe/on_your_own/stay, line 202), attack_who (whomever_attacking_me/strongest/weakest/closest/whomever), disposition, best_weapon, area_attack_mode, chem_use, chem_primary_desire.

**Packets used in our maps** (probe /tmp/p8c-probe, MAP critter AiPacket histogram):
- denbus1 (72 critters): 14 Peasants ×28, 25 Wimpy Peasant ×21, 22 Tough Guard ×6, 15 Child ×6, 12 Generic Guards ×4, 50 Cyberdog ×3, 13 Thugs ×3, 17 Store Owner ×1.
- denbus2 (86): 14 ×31, 12 ×22, 25 ×14, 1 Arroyo Warrior ×11, 17 ×2, 15 ×2, 22 ×2, 54 PARTY VIC ×1, 50 ×1.
- klamall (32): 12 ×11, 14 ×6, 7 Rat ×5, 15 ×4, 25 ×3, 13 ×2, 50 ×1.
- arcaves (22): 7 Rat ×12, 14 ×6, 8 Scorpion ×3, 1 ×1.
→ 12 distinct packets cover all four maps: {1,7,8,12,13,14,15,17,22,25,50,54}.

**Key values for those** (full table in probe output):
| pkt | name | min_to_hit | min_hp | distance | run_away | hurt_too_much |
| 1 Arroyo Warrior | 30 | 5 | on_your_own | - | blind |
| 7 Rat / 8 Scorpion | 0 | 0 | - | - | blind |
| 12 Generic Guards | 20 | 4 | - | tourniquet | crippled,blind |
| 13 Thugs | 40 | 10 | - | not_feeling_good | crippled,blind |
| 14 Peasants | 34 | 12 | - | bleeding | crippled,blind |
| 15 Child | 10 | 20 | - | - | crippled,blind |
| 22 Tough Guard | 15 | 1 | on_your_own | tourniquet | (none) |
| 25 Wimpy Peasant | 0 | 0 | charge | never | crippled,blind |
| 50 Cyberdog | 10 | 0 | stay_close | - | crippled,blind |

**What visibly changes our fights**:
1. **min_to_hit** (combat_ai.cc:2840-2880): replaces our flat 30. If accuracy < min_to_hit, AI walks tile-by-tile along the path until a tile where toHit ≥ min_to_hit (line 2870), and if even point-blank no-range toHit < min_to_hit it FLEES (2845-2852). Thugs (40) reposition aggressively; rats (0) always bite; Tough Guards (15) shoot from far.
2. **Flee threshold** (_combat_ai, 3074-3081): flee when curHP < min_hp (absolute file value) OR (damage flags & hurt_too_much). Peasants (min_hp 12) and Children (20) visibly run when wounded — big felt change in Den streets. Note: the run_away_mode % table only feeds party-member UI/debug; the actual combat check is the raw min_hp field — port just min_hp + (later) hurt flags.
3. **distance prefs** (_cai_perform_distance_prefs, 2970-3032): stay_close = stay within 5 hexes of dude (party only, gates Vic), charge = close to melee first, snipe = retreat to 10 if weaker/out of AP, stay = never move, on_your_own = no-op. Wimpy Peasant=charge, Cyberdog=stay_close.

**Sizing "honor min_to_hit + distance pref + flee"**: ai.txt parser (INI-ish, ~100 LoC in Formats + unit test with a fixture string) = S. AI-loop changes: min_to_hit gate + walk-until-hittable reuses our approach path = S; flee = new "move away" helper (straight-line retreat using our greedy hex walk, max AP per turn) = S; distance prefs = gates on existing approach (charge/stay/on_your_own trivial; snipe reuses move-away; stay_close only matters for party NPCs) = S. **Total: M (~300-400 LoC incl. tests), one sitting. Highest felt-depth per line of any item in this track.**

## Q2. Criticals + aimed shots — the real row count

**Trigger path** (combat.cc:3715, random.cc randomRoll/randomTranslateRoll): to-hit roll delta = accuracy − d100; on success, d100 ≤ delta/10 + STAT_CRITICAL_CHANCE → ROLL_CRITICAL_SUCCESS (aimed shots pass `critChance − hit_location_penalty[loc]` as the modifier, combat.cc:3852 — eyes shots get +60 crit chance). Vanilla gates crits behind game-day ≥1 (failures day ≥6) — sfall removes; we should ignore the gate. Melee Slayer / ranged Sniper perks force crits (3865-3898) — skip, we have no perks.

**Effect-level pick** (attackComputeCriticalHit, combat.cc:4089-4161): d100 + Better Criticals → ≤20/45/70/90/100/else → effect 0-5. Entry = `gCriticalHitTables[killType][hitLocation][effect]`.

**Row count, REALLY**: table declared `[SFALL_KILL_TYPE_COUNT=38][9][6]` but initialized rows counted = **1026 = 19 kill types × 9 hit locations × 6 effects** (combat.cc:189-1790). Plus `gPlayerCriticalHitTable[9][6]` = **54** entries used when defender == dude (combat.cc:1791). Total 1080 7-int entries.

**Entry struct** (combat_defs.h:138-161): {damageMultiplier (halves — fed as bonusDamageMultiplier into the ×ammoMult-then-÷2 wrapper we already ported; normal hit = 2), flags (DAM_*: BYPASS/KNOCKED_DOWN/KNOCKED_OUT/CRIP_xxx/BLIND/DEAD/LOSE_TURN/DROP/ON_FIRE/CRIP_RANDOM), massiveCriticalStat (−1 or STAT_ to roll; failure ⇒ adds massiveCriticalFlags + swaps message), statModifier, massiveCriticalFlags, messageId, massiveMessageId (combat.msg 5000+)}. DAM_BYPASS effect in attackComputeDamage (combat.cc:4530-4532): DT and DR cut to 20% (not zero). Crit FAILURES are a separate small table `_cf_table` (item.cc:1875, weapon-failure-type × 5, flags only: drop/explode/hit-self/lose-turn/lose-ammo), luck-shifted bucket.

**Aimed shots**: hit_location_penalty (combat.cc:172): head −40, arms −30/−30, torso 0, legs −20/−20, eyes −60, groin −30, uncalled 0. Applied to to-hit FULL for ranged, HALF for melee (combat.cc:4438-4441). AP cost: +1 (item.cc weaponGetActionPointCost, `if (aiming) actionPoints += 1`). UI = 8-location pick menu. Also relevant to-hit bits we currently lack: +40 vs knocked-down target, −25 when attacker blind, +15 multihex.

**Verdict**:
- **Full port**: 1080-entry table transcription is mechanical (it's literal int arrays — transcribe gCriticalHitTables into a C# static, ~1700 lines of data, scriptable to verify by checksum against the cc source). Effects then demand crippled-limb state, blind, lose-turn, knocked-out handling in CritterState + AI = the expensive part (M-L), NOT the table.
- **Minimal cut (RECOMMENDED)**: keep the real table shape but honor only {damageMultiplier, DAM_KNOCKED_DOWN, DAM_DEAD, DAM_BYPASS} and the massive-crit stat roll (we have real rolls); ignore cripple/blind/KO flags (mask them off like _attackFindInvalidFlags does). Visible result: "critical for 24 damage, X is knocked down / killed outright" + bypass making low-DR shots scary — 90% of the felt drama. Size: table transcription M-mechanical + ~60 LoC logic in CombatMath + knockdown plumbing from Q3 = **M overall**.
- **Skip**: leaves aimed shots pointless (their whole payoff is the crit-modifier coupling) — if we want the called-shot UI at all, the minimal cut is the floor. Skip only if Q3/Q4 take priority.

## Q3. Knockdown / knockback

**Where it comes from**: knockback distance is set in attackComputeDamage (combat.cc:4634-4659): only when damageType==EXPLOSION, weapon==null (unarmed), or attack type MELEE — **guns never knock back, they only knock DOWN via crit flags**. distance = totalDamage / 10 (÷5 with Knockback weapon perk; CRITTER_NO_KNOCKBACK and MULTIHEX immune).
**Shove** (actionKnockdown, actions.cc:~410468): walk straight tiles in `rotation = tileGetRotationTo(attacker, defender)` from the victim, 1..maxDistance, stop one short of the first blocking object or exit grid (we can reuse our greedy straight hex walk + blocking test verbatim); animationRegisterKnockdown moves the object along that line while playing FALL anim. Without a move-along-line animator mode, an acceptable v1: teleport the victim hex-by-hex over the fall animation's duration, or even snap at anim end (engine snaps logically too — combat math uses final tile).
**Knockdown state**: DAM_KNOCKED_DOWN flag → victim lies prone (keep last FALL frame — PlayFall already ends there). **Get-up**: NOT a reversed fall — dedicated anims exist: ANIM_BACK_TO_STANDING=37 (after FALL_BACK=20) / ANIM_PRONE_TO_STANDING=36 (after FALL_FRONT=21) (animation.h:43-60, _dude_standup animation.cc:3182). So get-up = PlayActionOnce with anim code 36/37 — zero new animator features. **AP cost**: standing up at start of victim's turn costs 3 AP (1 with Quick Recovery perk), floors at 0 (_combat_standup combat.cc:5395-5410); clears DAM_KNOCKED_DOWN.
**To-hit synergy**: +40 to hit a knocked-down target (combat.cc:4475) — makes knockdown tactically real.
**What we need**: KnockedDown flag in CritterState (+ save delta), turn-machine hook "if knocked down: spend 3 AP, play 36/37, clear flag", PlayFall reuse, straight-line shove walk (have), +40 to-hit term. **Size: S** (if snap-shove) / **S-M** (if animated slide). Knockback applies mostly to melee/unarmed and future explosions; pairs naturally with Q2's minimal crit cut (KNOCKED_DOWN is the most common crit flag in the MAN table).

## Q4. Burst

**Mechanics** (_compute_spray combat.cc:3688/0x423488 + _shoot_along_path 0x423284):
1. rounds = min(magazine, weapon.proto.rounds) — we ALREADY parse `Rounds` in WeaponProtoStats.
2. One master roll vs accuracy (with crit chance): crit-fail → bail to crit-failure handling; crit-success → accuracy +20 for the per-round rolls (no crit table on burst).
3. Split: center = rounds/3 (min 1), left = rounds/3, right = remainder; mainTarget = center/2 (min 1, taken out of center).
4. mainTarget rounds each roll d100 vs accuracy → hits on the chosen target.
5. THREE cone walks via straight paths: centerEnd = _tile_num_beyond(attacker, defender, range); cone edges: centerTile = defender tile (or 3-beyond if distance ≤ 3), rot = rotationTo(centerTile→attacker), leftTile = neighbor (rot+1)%6, rightTile = (rot+5)%6, each extended to range with _tile_num_beyond. _shoot_along_path then repeatedly runs the straight-path walk (same primitive as our LineOfFire.Trace), and at each intercepted critter rolls `while d100 ≤ toHit(critter) && roundsLeft` — that critter soaks roundsHit, damage = the normal wrapper with ammoQuantity=roundsHit; extras capped at 6 targets.
6. Ammo spent = full rounds count; AP cost = ApCost2 (secondary mode); burst flag = extendedFlags secondary nibble ((flags>>4)&0xF)==7 (item.cc _attack_anim, ANIM_FIRE_BURST).

**Availability probe (raw MAP inventories, 5 maps)**: NOTE — prompt's "10mm SMG pid 5" is wrong: **pid 5 = Club; 10mm SMG = pid 9** (pro_item.msg, proto 9: rounds=10, cap=30, secondary=burst). Census of all weapons placed on denbus1/denbus2/klamall/arcaves/klatrap: knives(4), clubs(5), sledge(6), spears(7), 10mm Pistols (8: ×12 denbus1, ×21 denbus2), Rocks (19: ×15 denbus2!), crowbar(20), Desert Eagle(18), throwing knives(45), Shotgun(94 ×1) — **ZERO burst-capable weapons in the raw map data of our whole slice**. Caveat: merchant stocking scripts (RunMapEnter list mutation) may add SMGs to Tubby/Flick stock, and we sell via barter — burst would be a player-bought toy only.
**Size**: math is fully Formats-testable, plumbing exists (LineOfFire, per-round damage wrapper, magazines). New: _tile_num_beyond port (S), spray routine (~120 LoC, M-small), extras list in attack result + multi-target damage presentation in viewer (S/M), attack-mode toggle UI (S). **Total M — but felt-depth payoff in OUR maps is near zero. Recommend: defer behind Q1/Q2/Q3 unless the barter/SMG loop ships in the same phase.**

## Q5. Throwing + explosives

**Throwing (rocks/spears/knives/grenades)**: maps are FULL of throwables (Rocks ×15 on denbus2, spears, throwing knives) — unlike burst, this is immediately visible content. Attack math is the ranged path we already have (range formula differs: ST-based — RangedMath handles? verify; throw anim = ANIM_THROW_ANIM, extendedFlags nibble 5). Missing piece is ONLY the projectile visual: _action_ranged (actions.cc:720-815) — for throws the projectile IS the weapon object (itemRemove → objectSetFid(projectileProto->fid) → place at attacker, animationRegisterMoveToTileStraight to defender tile (or miss tile), then restore/land: weapon lands on the ground at target tile (`_obj_connect`), i.e. rocks are recoverable. Guns would also benefit (bullet streak uses same registerMoveToTileStraight but with created-then-hidden projectile — we currently skip it fine).
   **Animator gap**: one new mode "move object along straight hex line at fixed velocity, then callback" — our ObjectAnimator has per-frame offset playback already; straight-line tween between screen positions of two hexes is ~60-80 LoC. **Size S/M. IN — recommended**, it unlocks rocks/spears/knives with the math we already ship.
**Grenades**: isGrenade when damageType is explosion/plasma/EMP (actions.cc:727). Pulls in _compute_explosion_on_extras (combat.cc:3987): ring-by-ring spiral around impact tile up to weaponGetGrenadeExplosionRadius (2 for grenades), each live critter with unblocked LoS to center (our LineOfFire) takes a full damage roll (ammoQty=1, mult=2); attacker can self-hit (DAM_BACKWASH). Knockback distance also computed (damage/10, EXPLOSION type) → ties into Q3. Size: ring-walk helper + AoE loop ~100 LoC Formats + viewer multi-hit presentation. **M. IN if Q3 ships (shared knockback), else defer.** No grenades in raw Den/Klamath maps though (frag grenade pid 25 absent) — same availability caveat as burst.
**Dynamite/timed + the temple door**: chain is: arm explosive → queue EVENT_TYPE_EXPLOSION → _queue_explode_exit (queue.cc:451) → **actionExplode** (actions.cc:1582): creates EXPLOSION MISC OBJECT fid=buildFid(MISC,10) (+6 adjacent visual copies), builds an Attack with it, AoE via _compute_explosion_on_extras, then _combat_explode_scenery → _scr_explode_scenery (scripts.cc:2879): broadcasts SCRIPT_PROC_DAMAGE to every ITEM+SPATIAL script within rocket radius, with fixedParam=20 and target=the explosion object. **metarule(49)=METARULE_WEAPON_DAMAGE_TYPE** (interpreter_extra.cc:78,3297): returns weaponGetDamageType(obj), with the special case `fid == buildFid(MISC,10,0,0,0) → DAMAGE_TYPE_EXPLOSION` — THAT is what the door script sees. Also direct path: when the Attack's defender is the non-critter door, combat.cc:4690-4704 fires damage_p_proc with weapon=explosion object. So "legitimate temple door" needs: timed-explosive item use → our existing timer queue → a tiny actionExplode (spawn misc-10 object, AoE damage, broadcast damage_p_proc to scripts in radius with fixedParam=20) + metarule 49 external returning EXPLOSION for misc-10/weapon damage type. We already have the script timer machinery (phase 5) and damage_p_proc dispatch. **Size M; high charm (it's THE classic artemple beat) — IN, but artemple needs dynamite placed (player has none in Den raw maps — Arroyo temple has it? plastic explosives are quest-given there in original; flag as content-availability TODO).** Recommendation order: throwing IN, dynamite/metarule49 IN (small, reuses timers), grenade AoE conditional on Q3.

## Q6b. Extraction go/no-go

Bench (above): denbus2 400 frames → avg 2.90 / p95 5.32 / max 9.47 ms. Canary green; depth work is per-attack CPU, no frame-budget concern.

**Current machine (verified in ViewerGame.cs today)**: CombatPhase enum :83, attack path ~:1752-2150, StartCombat :2500, EndTurn :2530, UpdateCombat :2669, StepEnemyTurn :2698, TryEnemyAction/EnemyAttack below; flat min-to-hit and damage-on-completion all live in the viewer. The p7 ICombatPresenter sketch (p7-track-c-fidelity.md Q6a) still matches the code 1:1 — IsBusy/PlayAttack/StartWalk/Log/OnCombatEnded covers every host dependency I see.

**Verdict: GO — extract FIRST, before any Q1-Q5 feature.** Reasons:
1. Every recommended feature is orchestration-heavy, not just math: AI min_to_hit walk-until-hittable + flee (Q1), knockdown get-up AP at turn start (Q3), crit flag application at damage-on-completion (Q2). Built in the viewer they are playtest-only; built in an extracted engine they get seeded deterministic tests (fake presenter, fixed Random) — the exact regression net the 1080-row crit table and flee thresholds need.
2. The move is mechanical NOW (sketch confirms ~5-method surface); every viewer-side feature added first makes it strictly harder. Extraction cost M (2-3 days) is paid once; the alternative is paying playtest-verification on each of 4 features.
3. It directly unlocks p7 regression test #3 (determinism trace) and gives crits/burst a permanent harness.

**Order of work (recommended phase-8 track C plan)**:
1. **CombatEngine extraction** (M): move phase/AP/turn/AI loop to Formats behind ICombatPresenter; no behavior change; lock with (a) seeded determinism test on a synthetic 3-critter map, (b) denbus2 [GameDataFact] smoke.
2. **Q1 AI packets** (M): ai.txt parser + min_to_hit gate + flee(min_hp) + distance prefs — pure engine work, fully testable. Biggest felt-depth win.
3. **Q3 knockdown** (S) then **Q2 minimal crit cut** (M): table transcription + {mult, KNOCKED_DOWN, DEAD, BYPASS} + massive-crit roll; presenter additions are just PlayFall reuse + anim 36/37 get-up. Aimed-shot menu rides on this (+1 AP, location penalties, crit-mod coupling).
4. **Q5 throwing** (S/M): straight-line projectile animator mode → rocks/spears/knives (abundant in our maps); then **dynamite/actionExplode + metarule 49** (M) for the temple-door beat.
5. **Defer**: Q4 burst (zero burst weapons in our slice's raw maps), grenade AoE (no grenades placed; revisit with merchant stock), full crit effect flags (cripple/blind/KO/lose-turn).
