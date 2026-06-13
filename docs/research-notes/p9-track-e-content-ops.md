# Phase-9 Track E: Content reality check + cross-cutting ops

> Method: built `tools/ContentAudit` (uses `Hexwaste.Formats` parsers) to scan
> the opening-hour slice maps, classify every weapon by attack mode (ported
> from fallout2-ce `src/item.cc` `_attack_anim`/`_attack_subtype`), tally
> critter AI packets + kill types, and resolve owners via `pro_*.msg`. Weapon
> protos parsed to the byte by the existing `ProtoDatabase`. AI fields read
> from the real `data\ai.txt` (extracted, 158 509 bytes, 187 sections). Engine
> claims cited file:line against `reference/fallout2-ce`.
>
> Maps dumped (all of the slice): artemple, denbus1, denbus2, denres1,
> kladwtwn, klamall, klagraz, klatoxcv, klatrap, klacanyn, klaratcv.
> (Prompt said "kldwtwn.map" — that file does NOT exist; the real Klamath
> downtown map is `kladwtwn.map`.)

This track GATES C/D: a combat feature with no content in the raw slice is
near-zero payoff. Headline: **the felt-depth content is THROWING (spears on
every Den guard) and AIMED/CRIT (every human/dog/gecko enemy). BURST and
DYNAMITE are real but live only in deep optional caves. Save needs NO V3 bump.**

---

## 1. THE CONTENT TABLE (feature -> present? -> PIDs/maps -> verdict)

Classification key: a weapon's attack mode = `extendedFlags & 0xF` (primary)
and `(extendedFlags & 0xF0) >> 4` (secondary), indexed into `_attack_subtype[9]`
(item.cc:128-141) / `_attack_anim[9]` (item.cc:116-125): 5=THROW, 6=SINGLE,
7=BURST, 8=CONTINUOUS. Verified against `ItemProtoTests.cs:21`
(`spear.ExtendedFlags & 0xF == 4`).

| Depth feature | Present in slice? | Concrete PIDs (map[elev,where]) | Verdict |
|---|---|---|---|
| **THROWING-class** (anim 5 prim or sec) | **YES — abundant, combat-lootable** | Spear `0x07` (artemple,denbus1,denbus2,kladwtwn,klamall — carried by Agile Guard/Agile Thug/Weak Gun Guard, secondary THROW range 8); Rock `0x13`; Throwing Knife `0x2D` (denbus2,kladwtwn,klamall); Sharpened Spear `0x118` (klamall); Flare `0x4F` | **BUILD** — highest content density; spear is on the actual Den fighters |
| **EXPLOSIVE thrown (AoE grenade)** | **YES, but narrow** | Grenade (Frag) `0x19` dmg=explosion 20-35 (kladwtwn ONLY — on Child(Female)+Loser+Bookcase); Molotov `0x9F` dmg=explosion 8-20 (kladwtwn — Child+Loser+Bookcase) | **BUILD WITH throwing** — reuses throw rung; but owners are kids/containers (pickpocket/loot, not combat drops) |
| **EXPLOSIVE misc (dynamite/plastic)** | **WEAK — 1 instance, deep cave** | Dynamite `0x33` (idx 51, MISC subtype 5) in a **Shelf** on `klaratcv.map[e1]` (rat caves). Plastic explosive `0x55`: **ABSENT**. Armed `206/209`: never in pristine maps. | **DEFER** — 1 shelf in an optional dungeon; the timer-queue + actionExplode port (actions.cc:1582) buys one shelf |
| **EXPLOSIVE single-shot (rocket)** | trace, deep cave | Robo Rocket Launcher `0x10E` dmg=explosion 10-30 (klatoxcv.map[e2], toxic caves) — on a robot enemy | **DEFER** — robot-only, deep optional map |
| **BURST-capable** (anim 7) | **NEAR-ABSENT** | **Bozar `0x15E`** rounds=15 (klatoxcv.map[e2] ONLY — toxic caves). No 10mm SMG, no Minigun, no burst pistol anywhere in the slice. | **DEFER** — one weapon, one deep map; `_compute_spray` cone (combat.cc:3702) is pure cost for ~zero felt use |
| **CONTINUOUS/flamer** (anim 8) | **ABSENT** | none | **DEFER** (out of scope confirmed) |
| **AIMED-benefit guns** (RANGED single, non-explosive) | **YES — core slice** | 10mm Pistol `0x08` (denbus1/2, klaratcv); Desert Eagle `0x12` (denbus2); Shotgun `0x5E` (denbus1/2); Pipe Rifle `0x12B` (kladwtwn, klamall); Laser `0x10`/Plasma Pistol `0x18` (klatoxcv deep) | **BUILD** — guns the player already fights with; aimed shots add depth to every Den/Klamath gunfight |
| **CRIT targets (kill types)** | **YES — 7 of 19** | core slice critters map to KILL_TYPE: MAN(110), WOMAN(68), CHILD(14), BRAHMIN(9), DOG(2), RAT(1), GECKO(1) | **BUILD** — crits land on every human enemy; only 7/19 crit-table blocks are reachable in the slice |
| **KNOCKDOWN/knockback** | **YES (mechanic, not item)** | fires on explosion / unarmed / melee (combat.cc:4635); spear-thrust (melee) + grenade (explosion) + fists all knock down — all present | **BUILD with throwing/crit** — geometry is cheap, content is everywhere |

### Ammo present (subtype 4) — supports the aimed-gun verdict
10mm JHP `0x1D` (x2/1 dmg, +25 DR), 10mm AP `0x1E` (x1/2, -25 DR), 12ga Shells
`0x5F` (AC-10), .44 FMJ `0x6F` in the core Den maps; .223/SEC/MFC/2mm-EC/4.7mm
only in klatoxcv (deep). The to-hit/damage ammo mods are already consumed by
`RangedMath`, so aimed guns have full ammo support today.

---

## 2. THE AI ANSWER (which ai.txt fields actually move the Den/Klamath fights)

`data\ai.txt` is **8267 lines / 187 sections** (one per `packet_num` 0-186).
Parsed as INI (`combat_ai.cc:406-484` `_aiPacketInit`). The actual Den/Klamath
NPCs (from `MapObject.AiPacket`, parsed at MapFile.cs:432, **grep-confirmed read
nowhere today**) carry these packets:

| pkt | section | who (Den/Klamath) | min_to_hit | min_hp(RAW) | max_dist | distance | best_weapon | attack_who | aggression |
|---|---|---|---|---|---|---|---|---|---|
| 12 | Generic Guards | denbus1/2 guards (x4/x22) | **20** | **4** | 20 | (default) | ranged_over_melee | closest | 80 |
| 13 | Thugs | denbus1 thugs (x3) | **40** | **10** | 10 | — | (default) | **weakest** | 80 |
| 14 | Peasants | denbus1/2/kladwtwn (x28/31/6) | **34** | **12** | 10 | — | — | closest | 30 |
| 22 | Tough Guard | denbus1/2 (x6/x2) | **15** | **1** | 19 | **on_your_own** | ranged_over_melee | closest | 95 |
| 25 | Wimpy Peasant | denbus1/2/kladwtwn (x21/14/6) | **0** | **0** | 7 | **charge** | no_pref | closest | 95 |
| 1 | Arroyo Warrior | artemple/denbus2/kladwtwn | 30 | 5 | 20 | on_your_own | — | **strongest** | 90 |
| 50 | Cyberdog | (merchants, x3/5) | 10 | 0 | 12 | stay_close | unarmed | whomever | 75 |

(Full per-packet dump preserved in tool output; sample names from pro_crit.msg.)

**Fields that ACTUALLY change the fights** (port these; ignore the message-range
+ color/font fields):

1. **`min_to_hit`** — replaces our flat 30. Drives the move-closer-or-flee loop
   in `_ai_try_attack` (combat_ai.cc:2705 `minToHit = aiGetPacket->min_to_hit`):
   - When OUT OF RANGE: `_determine_to_hit_no_range < minToHit` -> **FLEE**, else
     `_ai_move_steps_closer` (combat_ai.cc:2807-2820).
   - When IN RANGE but `accuracy < minToHit`: walk tile-by-tile toward target,
     re-rolling `_determine_to_hit_from_tile`, breaking as soon as `toHit >=
     minToHit`; if no tile qualifies -> FLEE (combat_ai.cc:2845-2891).
   Effect in-slice: a tough guard (15) closes and shoots almost always; a thug
   (40) is far more likely to reposition or flee. Flat-30 erases this spread.

2. **`min_hp` flee (RAW value, NOT the % table)** — at turn start
   `_combat_ai` (combat_ai.cc:3076-3081): flee if
   `STAT_CURRENT_HIT_POINTS < ai->min_hp` (OR fleeing maneuver OR
   `results & hurt_too_much`). **min_hp is the raw field** (4/10/12/1 above);
   parse does NOT recompute it (combat_ai.cc:407-408 reads it verbatim; the
   `_hp_run_away_value[]` 25/40/60/75/100 % table is only used by
   `_cai_get_min_hp` for the DYNAMIC run-away-mode setter at combat_ai.cc:830-833,
   NOT the turn-start flee). So: **port `min_hp` as an absolute HP threshold.**
   `run_away_mode` and `hurt_too_much` (bitmask of DAM_* from `_rmatchHurtVals`,
   combat_ai.cc:242-248) are secondary — defer until crippling status exists.

3. **`distance` prefs** (`_cai_perform_distance_prefs`, combat_ai.cc:2970-3034):
   `charge` (move 1 closer), `snipe` (back off to >=10 if outmatched),
   `stay_close` (close to <=5 from dude), `on_your_own` (no special move).
   Wimpy peasants CHARGE; tough guards are on_your_own. Cheap to port (one
   switch, reuses A*), changes the "feel" of who rushes you.

4. **`attack_who`** (closest/weakest/strongest/whomever, combat_ai.cc:471) —
   our AI already picks NEAREST. Thugs pick **weakest**, Arroyo warriors pick
   **strongest**. Small change to target selection; moderate felt impact.

5. **`best_weapon`** (combat_ai.cc:258-266 `_weapPrefOrderings`) — only matters
   once enemies carry >1 weapon class; most Den NPCs carry one. **Low priority.**

**Sizing:** ai.txt parser (~S, INI is `MessageFile`-like) + consume the
already-parsed `MapObject.AiPacket` keyed by packet_num + the min_to_hit
walk/flee + min_hp flee = **M**. distance prefs + attack_who = **S** add-on.
This is the single change that turns "approach and bonk" into recognizable
F2 enemy behavior, and the content (the packets) already ships on every fighter.

---

## 3. KNOCKDOWN / KNOCKBACK GEOMETRY (cited, for Track C)

- **When it fires** (combat.cc:4633-4637, `attackComputeDamage`): target not
  multihex AND `(damageType==EXPLOSION || weapon==null || attackType==MELEE)`
  AND target is a critter AND not `CRITTER_NO_KNOCKBACK`. **Ranged guns do NOT
  knock back** — only explosions, unarmed, and melee. (All three present in the
  slice: fists, spears/clubs, grenades.)
- **Distance** = `damage / 10` (`/5` with Weapon Knockback perk; halved again if
  the dude has Stonewall and a 50% roll fails) — combat.cc:4651-4656.
- **Direction + blocking** (`actionKnockdown`, actions.cc:101-136): rotation =
  `tileGetRotationTo(attacker->tile, defender->tile)` (straight away from
  attacker); walk `tileGetTileInDirection(tile, rotation, distance)` for
  distance 1..maxDist, **stopping (distance--) on a blocked tile
  (`_obj_blocking_at`) or an exit grid**. `MAX_KNOCKDOWN_DISTANCE = 20`
  (actions.cc:40). So: **YES, knockback is blocked by occupied tiles.**
- **Get-up** (`_combat_standup`, combat.cc:5391-5396): costs **3 AP** (1 with
  Quick Recovery). Anims: ANIM_FALL_FRONT/BACK (20/21-class fall) then standup.
- **+40 to-hit vs prone** (combat.cc:4474-4475): `if defender results &
  (DAM_KNOCKED_OUT|DAM_KNOCKED_DOWN): toHit += 40`.

---

## 4. AIMED SHOTS + CRITICALS — the table reality check

- **Crit table = `gCriticalHitTables[SFALL_KILL_TYPE_COUNT=38][HIT_LOCATION_COUNT=9][CRTICIAL_EFFECT_COUNT=6]`**
  (combat.cc:189-1786). Only the first **19** kill types (`KILL_TYPE_COUNT=19`)
  are populated; rows 19-37 are SFALL zero-padding. Actual data:
  **1026 rows** (`grep -c "{ [0-9-]"` over 189-1786 = 1026 = 19×9×6). Each row
  = `{damageMultiplier, damageFlags, statCheck, statCheckMod, statCheckFailResult,
  msgHit, msgMiss}` (struct CriticalHitDescription).
- **The slice only needs 7 kill-type blocks** = 7×9×6 = **378 rows** (MAN,
  WOMAN, CHILD, BRAHMIN, DOG, RAT, GECKO — the only kill types on slice
  critters). **Pivot threshold: transcribe the 7 reachable blocks first (378
  rows, checksum-verified against source); the other 12 blocks (super
  mutant/ghoul/deathclaw/...) are content the slice never produces — defer.**
- **Honor only `{damageMultiplier, KNOCKED_DOWN, DEAD, BYPASS}`**; mask
  `DAM_CRIP_*/DAM_BLIND/DAM_KNOCKED_OUT/DAM_LOSE_TURN` for now (no cripple/blind
  systems yet). The mask is a per-flag `&` — keep the full row data, gate the
  effect application.
- **Crit trigger**: `randomRoll(accuracy, criticalChance, ...)` returns
  ROLL_CRITICAL_SUCCESS/FAILURE (combat.cc:3853-3895); on success
  `damageMultiplier = attackComputeCriticalHit(attack)` (combat.cc:3911,
  table lookup at 4125). CriticalChance = LK base (already a CritterState
  derived stat).
- **Called-shot menu** (8 locations, HitLocation enum combat_defs.h:74-86):
  `hit_location_penalty[]` = head -40, L/R arm -30, torso 0, R/L leg -20,
  eyes -60, groin -30, uncalled 0 (combat.cc:172-182). Applied FULL for ranged,
  **HALVED for melee** (combat.cc:4437-4441). Aimed shot costs **+1 AP**
  (item.cc:1705-1707 `if (aiming) actionPoints += 1`). BYPASS = ignore armor
  DT/DR.
- **Sizing**: crit-table transcription (7 blocks) = **M** (mechanical but
  verify-heavy). Crit trigger + multiplier application = **S**. 8-way called-shot
  UI + penalty/AP = **S-M**. Total **M-L** if all three; the table is the long
  pole.

---

## 5. THROWING + EXPLOSIVES — animator rung & content payoff

- **Throwing reuses ranged to-hit + ONE new animator mode**: move the weapon
  object along a straight hex line to the target tile (the projectile IS the
  weapon, recoverable). `weaponGetAttackTypeForHitMode == ATTACK_TYPE_THROW`
  (item.cc:1611). Sizing **S (~60-80 LoC)**: a `MoveObjectAlongLine` rung +
  reuse `RangedMath.ToHitChance` (throw uses Throwing skill, not Small Guns —
  one skill swap). **Content: spears on every Den guard/thug make this the
  highest-payoff new attack mode.**
- **Grenade/Molotov AoE**: `weaponIsGrenade` = damage type EXPLOSION/PLASMA/EMP
  (item.cc:1968-1971); both slice throwables (`0x19`,`0x9F`) report dmg=explosion.
  Radius via `weaponGetGrenadeExplosionRadius` (item.cc:1986, default
  `gGrenadeExplosionRadius`). On impact -> `actionExplode(tile, elev, min, max,
  source, animate)` (actions.cc:1582) -> `_compute_explosion_on_extras` ->
  `damage_p_proc` to critters/items/spatials in radius. **Content caveat:
  grenade/molotov are on a kid + a Loser + bookcases on kladwtwn ONLY — loot/
  pickpocket, not combat drops. Build the AoE for completeness, but the
  felt-payoff is the spear-throw, not the grenade.**
- **Dynamite/plastic (misc)**: Dynamite `0x33` (idx 51) -> timer queue (phase-5
  timer class) -> armed `206` -> spawn explosion. Only **1 dynamite** in the
  whole slice (a shelf in klaratcv rat caves). **DEFER the misc-explosive +
  timer-arm path** — one shelf in an optional dungeon.
- **`metarule(49)` = METARULE_WEAPON_DAMAGE_TYPE** (interpreter_extra.cc:78,
  handler 3297): returns `weaponGetDamageType(weapon)` for an ITEM/WEAPON. Wire
  it so the artemple temple door's `damage_p_proc` can test
  `metarule(49,source)==DAMAGE_TYPE_EXPLOSION(6)` and open by blast.
  **Content caveat: there is NO grenade IN artemple** (the temple has 1 critter
  + the locked door + 124 misc/exit objects; no thrown explosive). A player can
  only blast the door if they CARRY a grenade from Klamath. So `metarule(49)`
  is correct to wire (cheap, S) but the blast-the-temple-door fantasy is not a
  shippable demo from the slice's start — the lockpick bypass remains the real
  path. Flag honestly.

---

## 6. BURST — DEFER (content evidence)

`_compute_spray` (combat.cc:3702): `burstRounds = weaponGetBurstRounds`, splits
into center/left/right cones (`ammoQuantity/3` each), three LoF walls, per-round
accounting. **Exactly ONE burst weapon in the entire slice**: the Bozar `0x15E`
(rounds=15) in klatoxcv.map elevation 2 (toxic caves, a deep optional map, on
a robot/guard). No SMG, no minigun, no burst pistol on any Den/Klamath surface
map. **DEFER**: a three-cone spray engine for a single weapon nobody fires in
the opening hour is pure cost. One-line rationale for the report:
"burst = 1 weapon, 1 deep cave -> deferred until a burst weapon reaches the
player's hands in normal play."

---

## CROSS-CUTTING CALLS

### (a) Save format — additive-V2 SUFFICES, NO V3 bump

`SaveState.cs` is a JSON snapshot, `CurrentVersion=2`, refuse-on-mismatch
(SaveState.cs:20-22). The phase-8 pattern: new nullable/defaulted fields are
additive (round-trip safe) and do NOT bump; only a shape change to the
ordinal-keyed deltas bumps. **Knockdown/aimed-target/crit-status are TRANSIENT
combat state that never crosses a save boundary:**

- The engine **cannot save during combat** — saving is via the pipboy, and the
  pipboy is blocked in combat (game.cc:652-666 "Pipboy not available in
  combat!"; same guard on rest/Z at game.cc:722-736). Load mid-combat force-ends
  combat (`_combat_over_from_load`, loadsave.cc:1703-1705). So DAM_KNOCKED_DOWN,
  aimed targeting, and crit flags are by-design ephemeral.
- **Verdict: additive-V2 is enough** for the whole of Combat Depth II. No new
  persistent fields are required by crits/aimed/knockdown/throwing/burst.
- **One divergence to fix (S):** Hexwaste's **F5 save is NOT gated by combat**
  (ViewerGame.cs:1102, no `_combatPhase` check), unlike the engine. If we leave
  transient knockdown unsaved (correct), an F5-during-combat then F9 silently
  drops a knocked-down state. Cleanest = **gate F5/F9/Z on `_combatPhase ==
  Idle`** (matches game.cc), which also removes the only path by which transient
  state could leak into a save. This is a correctness alignment, not a format
  change.

### (b) Bench — ample headroom; depth math is OFF the frame budget

Re-ran `--bench 400` on denbus2 this session: **avg 3.89 ms / p95 7.37 ms /
max 10.46 ms** (~257 fps uncapped) — ~12 ms under the 16 ms threshold,
consistent with the cited ~2.9/5.3/9.5. The bench measures the
render+palette-cycling steady state (PrintBenchReport, ViewerGame.cs:3531-3540);
it does NOT run combat. Per-attack crit-table lookups (a 3-D array index) and
AI tile-walks (A* already used for movement, bounded by AP <= ~10 tiles/turn)
fire only on a turn event, not per frame. **No frame-budget risk.** Optional:
re-run `--bench` after M1/M3 to confirm; cheap, but no concern expected.

### (c) Anything we're not seeing — pushback

1. **Extract-first ordering is RIGHT, keep it.** `ICombatRng`/`SystemCombatRng`
   already exist (Combat/ICombatRng.cs) — the seam is half-built; M0 is the
   right gate and the determinism test it unblocks is the regression net for
   everything below.
2. **Re-order the depth features BY CONTENT, not by glamour:** the data says
   the payoff ranking is **(1) AI packets [content on every fighter] -> (2)
   aimed shots + crits [every human/dog/gecko enemy, 7 kill-type blocks] ->
   (3) throwing [spears on every Den guard] + knockdown -> (4) grenade AoE
   [kladwtwn only, kid/container loot] -> DEFER burst (1 weapon, 1 cave) /
   misc-dynamite (1 shelf) / flamer (absent).** The standing milestone plan
   should front-load AI + aimed/crit because that is where the slice has the
   most enemies to apply it to. Throwing is a close third (spears) and pairs
   naturally with knockdown (melee/throw both knock down).
3. **`metarule(49)` and the temple-door-blast are a trap-demo:** wire the
   external (cheap), but do NOT advertise "blow the temple door" — there is no
   thrown explosive in artemple, so the demo can't be reproduced from the
   slice's opening without first looting Klamath. The lockpick path stays the
   shippable one.
4. **Honest non-combat alternative exists but is weaker:** the only
   higher-leverage non-combat item would be Vic's legitimate rescue +
   companion trade/dismiss (phase-8 spillover), but that's narrative plumbing
   with no new mechanic; Combat Depth II applies to far more of the existing
   content. **Stay the course on combat depth.**

---

## Tool note

`tools/ContentAudit` (new, this track) is reusable for future content audits:
`dotnet run --project tools/ContentAudit -c Release -- --game-dir <dir>
--map a.map --map b.map ...` prints the weapon table (with attack modes +
damage type + extendedFlags), the capability classification, throwable/explosive
ownership, kill-type tally, and per-map AI-packet histogram. It builds on the
existing `ProtoDatabase`/`MapFile`/`ProtoMessages` (zero new deps).
