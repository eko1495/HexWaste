# P7 Track A — Ranged Combat + Ammo (implementation-grade research)

All line refs against <repo>/reference/fallout2-ce/src.

## Q1. determineToHit (combat.cc `attackDetermineToHit`, combat.cc:4314–4498, engine 0x4243A8)

Order of terms exactly as the engine applies them:

1. **Base skill** (combat.cc:4325–4329): unarmed/no-weapon → `skillGetValue(attacker, SKILL_UNARMED)`; otherwise `weaponGetSkillValue` (item.cc:1203–1220) = `skillGetValue` of the weapon's skill for the hit mode (`weaponGetSkillForHitMode` item.cc:1168 maps proto `perk`/attack-type → SMALL_GUNS / BIG_GUNS / ENERGY / THROWING / MELEE / UNARMED).
2. **Ranged distance/perception block** (only if attack type RANGED or THROW, combat.cc:4331–4402):
   - `perceptionBonusMult` = 2 normally, 4 for PERK_WEAPON_LONG_RANGE, 5 (+`minEffectiveDist=8`) for PERK_WEAPON_SCOPE_RANGE (4337–4349).
   - `distanceMod = distance(attacker tile, defender tile)` (4357–4365).
   - If `distanceMod >= minEffectiveDist`: subtract perception bonus = `mult*(PE-2)` for dude, `mult*PE` for NPCs (4367–4374). Else (inside scope min range): `distanceMod += minEffectiveDist` (penalty).
   - Clamp: `distanceMod = max(distanceMod, -2*PE)` (4377–4379).
   - Scale: `distanceMod *= -4` (×-12 instead if attacker BLIND and mod>=0) (4381–4389). Net effect: **-4 per hex beyond your "free" PE-derived range; up to +4 per hex of unused perception range, capped at +8*PE.**
   - Add to toHit (4391–4393).
   - **Crowd penalty**: `_combat_is_shot_blocked(..., &numCrittersInLof)` counts friendly-ish critters standing on the line of fire; `toHit -= 10 * numCrittersInLof` (4395–4400).
3. One Hander trait: -40 two-handed / +20 one-handed (dude only, 4404–4410).
4. **Min strength**: `minStrengthMod = weapon.minStrength - ST` (−3 with Weapon Handling perk); if >0, `toHit -= 20 * minStrengthMod` (4412–4420).
5. PERK_WEAPON_ACCURATE: +20 (4422–4424).
6. **AC**: if target is critter: `armorClass = critterGetStat(defender, STAT_ARMOR_CLASS) + weaponGetAmmoArmorClassModifier(weapon)`, clamped to >=0, `toHit -= armorClass` (4427–4434). **This is where the ammo AC modifier lands — on to-hit, not damage.**
7. **Called shot**: `hit_location_penalty[hitLocation]` full for ranged, /2 for melee (4436–4440). Table combat.cc:172–183: head -40, eyes -60, arms -30, legs -20, groin -30, torso/uncalled 0.
8. Multihex target: +15 (4442–4444).
9. **Lighting (dude attacker only)**: target's `objectGetLightIntensity`; <=26214 → -40, <=39321 → -25, <=52428 → -10 (Night Sight perk forces 65536) (4446–4463). 65536 = full light, so thresholds ≈ 40%/60%/80% lit.
10. `_gcsd->accuracyBonus` (scripted attack override, 4465–4467).
11. Attacker blind: -25 (4469–4471). Target knocked out/down: +40 (4473–4475).
12. Combat-difficulty pref: enemies of dude's team get -20 (easy) / +20 (hard) (4477–4487).
13. Clamp: `toHit = min(toHit, 95)`; **no lower clamp** (warning only if < -100) (4489–4495).

**Range gate is NOT in to-hit** — it's in `_combat_check_bad_shot` (combat.cc:5643–5694): refuses the attack when `weaponGetRange(attacker, hitMode) < distance` (5673), when magazine empty (`ammoGetCapacity>0 && ammoGetQuantity==0`, 5679–5683), or when LoF blocked for ranged/throw (5685–5691). `weaponGetRange` (item.cc:1594–1635): primary→proto `maxRange1`, secondary→`maxRange2`; THROW type instead caps at `min(3*ST, maxRange)`.

The hit roll itself (attackComputeToHit caller, combat.cc:3829–3860): `roll = randomRoll(accuracy, 1, &randomValue)`; critical chance handling separate.

### Minimal-honest PoC subset (we already have skill−AC clamp95)
Keep: base weapon skill, distance term **simplified to `-4 * max(0, dist − 2*PE)`** with the `max(distanceMod, -2*PE)` clamp dropped... actually keep the faithful 4-line version, it is tiny:
```
d = dist - 2*(PE)            // NPC form; dude form uses (PE-2). mult=2, no weapon perks
d = max(d, -2*PE); toHit += -4*d   (apply only the dude-form/NPC-form distinction if cheap)
```
Keep: AC subtraction incl. ammo AC mod (we need ammo protos anyway); range gate + no-ammo gate + LoF gate from `_combat_check_bad_shot`; clamp 95.
Drop and what it costs:
- Weapon perks (long range/scope/accurate/night sight): cosmetic at PoC weapon set (10mm pistol = no perk) — zero cost for early-game guns, scoped rifles will feel mildly off.
- One Hander / Sharpshooter / Weapon Handling perks & traits: no perk system in Hexwaste → free.
- Min strength: cheap (field is in the proto payload we must read anyway, see Q3) — recommend KEEP, it's 2 lines and makes big guns honest.
- Lighting tiers: we have LightGrid; reading `objectGetLightIntensity` equivalent ≈ light at defender tile. Medium effort; dropping it makes night combat too easy for the player only (NPCs unaffected per engine). Defer.
- Crowd `-10*numCrittersInLof`: comes nearly free once LoF walk exists (same walk returns the count). KEEP if Q2 walk is ported.
- Called shots: only matters if we build the aimed-shot UI (scope rung c). Drop until then.
- Knockdown +40, blind, difficulty pref: no such states in Hexwaste → free.

## Q2. Line of fire

Entry point: `_combat_is_shot_blocked` (combat.cc:5897–5942). Loop: repeatedly call
`_make_straight_path_func(sourceObj, current, to, nullptr, &obstacle, 32, _obj_shoot_blocking_at)` (5906);
- non-critter obstacle that isn't the target → **blocked** (5908–5910);
- critter obstacle → not blocking, but counted into `numCrittersOnLof` (+1, +2 if MULTIHEX; skipped if dead/KO/down — SFALL fix) (5912–5925), then the walk **resumes from the critter's tile** (5927–5938) until reaching `to`.

The walk `_make_straight_path_func` (animation.cc:1951–2150, engine 0x4163C8) is a **Bresenham line in SCREEN space**, not a hex walk: take `tileToScreenXY(from)+(16,8)` and same for `to` (tile centers), step 1 px at a time along the major axis, convert each pixel back with `tileFromScreenXY`, and whenever the tile changes, call the blocking callback. `a6=32` means "shoot mode": objects with `OBJECT_SHOOT_THRU` (0x80000000) are ignored (animation.cc:1956, 2050, 2103); a6 also doubles as the node-sampling stride when building projectile paths (every 32nd step records a StraightPathNode — used for animation, not blocking).

What blocks — `_obj_shoot_blocking_at` (object.cc:2440–2494): objects on the tile (and MULTIHEX objects on the 6 adjacent tiles) with elevation match, not HIDDEN, not the excluded shooter, where NOT(NO_BLOCK && SHOOT_THRU), and FID type is **CRITTER (alive only — SFALL corpse fix) / SCENERY / WALL**. Note: closed doors are SCENERY and block unless their proto sets ShootThru → the engine sets `OBJECT_SHOOT_THRU` from proto flag 0x80000000; open doors have NO_BLOCK set so they don't block. Items and misc objects never block shots.

**Is our LightGrid walk reusable? No.** LightGrid (src/Hexwaste.Formats/Light/LightGrid.cs) is the radial light-propagation table (precomputed per-rotation offset rings + the 36-case occlusion switch) — geometry is baked for outward cones from a light source, not arbitrary point-to-point lines. Reuse the *blocking predicate* idea only.

**Cheapest faithful port** (~70 lines): we already have tile↔screen math ported from tile.cc (used for picking, P2-M3). Port `_make_straight_path_func` verbatim with `straightPathNodeList=null` (drop the node-recording branch entirely for LoF; keep it later if we want projectile waypoints), plus an `ObjShootBlockingAt(excl, tile, elev)` over the viewer's per-tile object index. Implement `_combat_is_shot_blocked`'s outer loop including the critter-resume + `numCrittersInLof` counter (feeds Q1's −10/critter term for free). Honesty shortcuts that stay faithful for a PoC: skip the MULTIHEX adjacent-tile scan (no multihex critters in our target maps' combat) and the MULTIHEX resume fix.

## Q3. Ammo: proto fields, damage hooks, reload, MAP data, SaveState V2

### Weapon proto payload — full read order (proto.cc:1585–1602, `protoItemDataRead` ITEM_TYPE_WEAPON)
int32 each unless noted: animationCode, minDamage, maxDamage, damageType, maxRange1, maxRange2, **projectilePid**, **minStrength**, actionPointCost1, actionPointCost2, criticalFailureType, perk, **rounds** (burst count), **caliber**, **ammoTypePid** (default loaded ammo PID), **ammoCapacity**, **soundCode (uint8!)** (proto.cc:1601 — one byte, not int). Our ProtoDatabase stops at apCost1, so it must add: apCost2, criticalFailureType, perk, rounds, caliber, ammoTypePid, ammoCapacity, soundCode — and stop discarding projectilePid/minStrength.

### Ammo proto payload (proto.cc:1604–1611, ITEM_TYPE_AMMO) — 6×int32
caliber, quantity (mag/box size — `ammoGetCapacity` returns this for ammo items, item.cc:1358–1373), armorClassModifier, damageResistanceModifier, damageMultiplier, damageDivisor.

### Where ammo mods are applied
- **AC modifier → to-hit**: `weaponGetAmmoArmorClassModifier` (item.cc:2020–2034, looks up the proto of the weapon's *currently loaded* `ammoTypePid`) added to defender AC in attackDetermineToHit (combat.cc:4428–4430).
- **DR modifier and mult/div → damage**: `attackComputeDamage` (combat.cc:4501–4624). Default (non-Glovz/YAAM) path, combat.cc:4581–4615:
  - `damageResistance += weaponGetAmmoDamageResistanceModifier(weapon)` (item.cc:2037), clamp 0..100. (AP ammo has DR mod ≈ negative? No — AP 5mm has DRmod -... it's per proto; modifier may be negative = armor resists less, or positive.)
  - `damageMultiplier = bonusDamageMultiplier * weaponGetAmmoDamageMultiplier(weapon)`; per-round loop: `damage = rand(minDmg,maxDmg) (+ melee bonus inside weaponGetDamage) + rangedDamageBonusPerk; damage *= damageMultiplier; damage /= ammoDamageDivisor; damage /= 2; damage = damage*difficultyMod/100; damage -= DT; damage -= damage*DR/100; if>0 accumulate` (4589–4614).
  - **The mysterious `/2` cancels the default `bonusDamageMultiplier = 2`** set in attackCompute (combat.cc:3843-ish `int damageMultiplier = 2;`); criticals replace it via attackComputeCriticalHit. So our melee CombatMath stays numerically identical if we adopt this path with ammoMult/Div=1.
  - `ammoQuantity` param = rounds that hit (1 for single shot; from `_compute_spray` for bursts) — damage loop runs once per hitting round.
- **DT/DR bypass** on armor-bypass criticals: DAM_BYPASS → DT,DR ×0.2 (combat.cc:4530–4534).

### Ammo consumption & reload
- Per attack, `attack->ammoQuantity` = rounds spent: ranged single shot v26=1; burst = up to proto `rounds` (capped at remaining mag, `_compute_spray`); melee with capacity>0 (e.g. cattle prod) = 1 (combat.cc:3888–3903). Deducted after the attack animation finishes: `_combat_anim_finished` → `ammoSetQuantity(weapon, qty - _main_ctd.ammoQuantity)` (combat.cc:5346–5352).
- Empty-mag gate: `_combat_check_bad_shot` returns NO_AMMO when capacity>0 && quantity==0 (combat.cc:5679–5683).
- **Reload AP cost**: `weaponGetActionPointCost` with HIT_MODE_*_WEAPON_RELOAD → **2 AP** (1 with PERK_WEAPON_FAST_RELOAD, 0 for Solar Scorcher) (item.cc:1650–1663).
- **Reload mechanics** `weaponReload` (item.cc:1553–1592): gated by `weaponCanBeReloadedWith` (item.cc:1503–1551) — calibers must match; if the mag isn't empty, the new ammo's PID must equal the loaded `ammoTypePid` (no mixed mags). Partial fill: moves `min(capacity - current, ammoItem.quantity)` rounds from the ammo item stack into the weapon, sets `weapon.data.item.weapon.ammoTypePid = ammo->pid`, returns rounds left in the ammo item (caller destroys it at 0). Ammo items themselves are stacks whose top item carries `ammo.quantity` rounds (partial boxes).

### MAP per-object extra fields we currently skip (proto.cc:579–597, `objectDataRead`)
After the inventory/flags block, items by proto subtype: **WEAPON → int32 ammoQuantity (loaded) + int32 ammoTypePid**; **AMMO → int32 quantity**; MISC → int32 charges; KEY → int32 keyCode. MapFile.ReadObjectData must read weapon+ammo variants (we likely already skip the right byte counts; now surface them).

### SaveState Version 2 proposal (shared bump with NPC-position track)
- `Version = 2`.
- `SavedItem(Pid, Count, Flags)` → add two nullable/int fields: `AmmoQuantity` (loaded rounds; meaningful for weapons and for ammo stacks where it = rounds in the *top* item of the stack, engine convention) and `AmmoTypePid` (weapons only, -1 when empty/never loaded). Serialize as `int AmmoQuantity = -1; int AmmoTypePid = -1` so V1 JSON (fields absent) deserializes to -1 = "use proto defaults" (proto.cc:756–763: fresh weapon gets ammoQuantity=ammoCapacity, ammoTypePid=proto default; fresh ammo gets quantity=proto ammo.quantity).
- On load: -1 sentinel → re-derive from proto, matching `protoItemDataDefaults`.
- V1 compatibility: keep reader accepting Version 1 and filling defaults; bump written version once both tracks land.

## Q4. What the engine draws for a 10mm shot (actions.cc `_action_ranged`, actions.cc:691–980)

**Verified from game data**: 10mm Pistol = PID 8 (pro_item.msg {800}); items.lst line 8 → `proto\items\00000004.pro`; parsed payload: animCode=5 (pistol), dmg 5–12 type 0, range1=25/range2=0, **projectilePid = -1**, minST=3, apCost1=5/apCost2=0, critFail=2, perk=-1, rounds=1, caliber=8, ammoTypePid=29 (10mm JHP), capacity=12, soundCode='A'. (NB: PRO files are **big-endian**; items.lst line order ≠ filename order — pid N maps to the Nth line of items.lst, e.g. pid 8 → 00000004.pro. Also the pid stored inside the file matches the msgId, not the filename.)

Sequence (`_action_ranged`):
1. `animationRegisterRotateToTile(attacker, defender->tile)` (actions.cc:721).
2. Non-throw: `animationRegisterAnimate(attacker, ANIM_POINT, -1)` — raise weapon (actions.cc:731).
3. Weapon attack sound (`WEAPON_SOUND_EFFECT_ATTACK`, actions.cc:736–742), then `animationRegisterAnimate(attacker, anim, 0)` where anim = fire animation from hit mode (ANIM_FIRE_SINGLE = FRM letter 'j', burst 'k') (actions.cc:744).
4. **Projectile object only if** `weaponGetProjectilePid` resolves to a proto with `fid != -1` (actions.cc:750–752). For the 10mm pistol (and most guns) projectilePid = -1 → **no projectile, pure hitscan**: the muzzle flash is baked into the attacker's fire FRM frames; nothing travels. Projectile path exists for rockets/grenades/flare-type weapons: create object at `_combat_bullet_start` tile, `animationRegisterMoveToTileStraight` to the defender (or miss tile), plus explosion FRM ring for explosives (actions.cc:780–889).
5. On miss without projectile: defender plays `ANIM_DODGE_ANIM` if not knocked out/down (actions.cc:903–909).
6. `_show_damage(attack, anim, delay)` (actions.cc:914) → `_show_damage_to_object` (actions.cc:292) plays defender hit/knockback/death animation (death anim chosen by damage type/amount elsewhere).
7. Ammo deduction happens in `_combat_anim_finished` after the chain completes (see Q3).

**Cheapest honest visual for our PlayActionOnce animator**: rotate attacker to face target → PlayActionOnce(fire-single anim 'j' with weapon code in FID bits 12–15) + gunshot sfx at anim start → on completion apply damage and PlayActionOnce on defender (hit anim, or dodge on miss, corpse anim+28 on kill — existing melee plumbing reused verbatim). No projectile object, no muzzle-flash sprite, no tracer — that IS what the original shows for the 10mm pistol. Skip projectile rendering entirely until/unless we do throwing or rockets (rung (a)/(c) in Q7).

## Q5. Dude art: hmjmps vs hmwarr

Suffix codes (art.cc `_art_get_code`, art.cc:544–606): combat anims TAKE_OUT..FIRE_CONTINUOUS → first letter = `'d' + (weaponCode-1)` (d=knife e=club f=sledge g=spear h=pistol i=smg j=rifle k=big gun l=minigun m=rocket), second letter = `'c' + (anim − ANIM_TAKE_OUT)` (c=take out, d=put away, e=dodge, h=point, i=unpoint, **j=fire single, k=fire burst, l=fire continuous**); knockdown/death = 'b'+x, single-frame deaths = 'r'+x, throw = dm/gm/as.

DatDump inventory (critter.dat):
- **hmwarr** (critters.lst index 63): A* (unarmed), BA BB BO BP (minimal knockdown/death), CH CJ, G* spear (GA–GF, GM throw), RA RB RO RP. **No d/e/f/h/i/j/k/l/m sets — cannot hold any gun.**
- **hmjmps** (critters.lst index 12, the engine's vault-suit `_art_vault_guy_num` default): full set — D E F G sets, **H pistol (HA HB HC HD HE HH HI HJ — fire-single 'HJ' present, no HK burst: pistols never burst)**, I smg (incl. IK burst), J rifle (JJ+JK), K big gun, L minigun, M rocket, complete B and R death sets, NA called-shot pic.

**Engine fallback: there is none for weapon codes.** `_inven_wield` (inventory.cc:3313–3319) builds the attack-anim FID and if `artExists` fails it **refuses the wield** ("inven_wield failed! ERROR"). `buildFid` (art.cc:1015–1031) only falls back across *rotations*, never across weapon codes. So the original engine solves this by giving the player a body that has all weapon sets.

**Recommendation**: switch the dude's base FRM from hmwarr to **hmjmps** (one-line base-name change; idle/walk/unarmed/spear behavior identical in structure), and keep the engine rule: refuse to equip a weapon whose `buildFid(base, animCode, fireAnim)` art doesn't exist (cheap artExists check via our DAT index). Do NOT invent a punch-fallback for missing gun art — the engine never renders that.

## Q6. Enemy ranged use

### Map probe (C# probe over our MapFile + ProtoDatabase, /tmp/p7a-probe)
Equip flags survive our parser; findings (elevation 0):
- **denbus1**: ~10 critters with EQUIPPED **10mm Pistol** (pid 8), several also carrying a 10mm AP/JHP box in inventory; 1 guard with EQUIPPED **Shotgun** (pid 94, animCode 7 = rifle art, range 14) + shells (pid 95); melee rest (knives/clubs/spears); 3 rock-throwers (Rock pid 19, throw range 15).
- **denbus2**: ~18 critters with EQUIPPED 10mm Pistol (most with an ammo box, mix of JHP pid 29 / AP pid 30), 1 EQUIPPED Shotgun + shells, 1 EQUIPPED **Desert Eagle .44** (pid 18) + .44 FMJ (pid 111), 1 Throwing Knife carrier.
- **kladwtwn**: 2 critters with EQUIPPED **Pipe Rifle** (pid 299, animCode 7, range 20, caliber 10mm — uses 10mm JHP they carry), molotovs (pid 159) + frag grenade (pid 25) carriers, many rock-throwers, cattle prod (melee with charges).
- Note: some armed critters' map FIDs already carry the weapon code in bits 12–15 (0x100500E = pistol pose), others have code 0 despite an EQUIPPED gun — the engine recomputes the FID on wield; we should derive pose from the equipped weapon's animationCode, not trust the MAP FID.
- Ammo boxes appear as StackCount=1 items; the *rounds inside* are the MAP ammo `quantity` field we currently skip (Q3) — without reading it, NPC reload supply is unknown (engine default would be full box).

### Engine AI shape (combat_ai.cc `_ai_try_attack`, combat_ai.cc:2686–2904)
Loop up to 10 attempts per turn; each iteration consults `_combat_check_bad_shot` and dispatches:
- NO_AMMO → reload from inventory (`aiHaveAmmo`+`weaponReload`, AP cost = reload cost, combat_ai.cc:2733–2758), else scavenge ammo from ground, else unwield and switch weapon (2759–2801).
- NOT_ENOUGH_AP / crippled → `_ai_switch_weapons` (2802–2808).
- OUT_OF_RANGE → if `to-hit-no-range < ai->min_to_hit` flee; else `_ai_move_steps_closer` (2809–2830).
- AIM_BLOCKED → `_ai_move_steps_closer` with all AP (2831–2837).
- OK → compute to-hit; if `< min_to_hit`, walk the A* path tile by tile recomputing `_determine_to_hit_from_tile` until it reaches min_to_hit, spend just that many AP, then attack (2838–2894); else attack immediately.
Distance preferences (`_cai_perform_distance_prefs`, combat_ai.cc:2970–3030, enum combat_ai_defs.h:39–44): stay_close (≤5 hexes of dude — party members), charge (move adjacent first), snipe (back off to 10 if weaker and close), on_your_own, stay (never move, gated at 1223/2361). Weapon choice: `_ai_search_inven_weap` ranks by `best_weapon` pref table `_weapPrefOrderings` (1805–1812). min_to_hit and distance come from the per-critter AI packet (ai.txt, MAP AiPacket field — we already parse AiPacket).

### Minimal model for Hexwaste (faithful to the common case)
Per AI turn: weapon = equipped item if WeaponProtoStats present (no switching, no scavenging); then:
1. if mag empty: if a matching-caliber ammo item is in inventory → reload (2 AP, consume box rounds), else fall back to melee/punch (skip the magic-hands anim).
2. if dist > weapon range OR LoF blocked → A* approach (existing approach code), recheck.
3. else stand + shoot using Q1 to-hit with a flat min_to_hit ≈ 30% gate (engine value lives in ai.txt; hardcoding 30 covers Den guards whose packets use ~30%).
This is exactly "stand+shoot if range+LoF else approach" plus the empty-mag branch, and reuses the AP-budgeted approach we already have.

## Q7. Scope ladder

**(a) Throwing only** — effort S, risk low-medium.
No ammo system, no reload, no proto additions beyond minStrength. But: needs ANIM_THROW_ANIM art (hmwarr has AS/GM so even the current dude can throw rocks/spears), a projectile object flying (`animationRegisterMoveToTileStraight` — rocks DO render as a thrown item via projectile=weapon, actions.cc:753–765), item removal from hand + landing on the ground, range = min(3*ST, maxRange). Grenades drag in explosion AoE (combat.cc `_compute_explosion_on_extras`) — exclude them. Verdict: cheap but a dead end — it builds the projectile animator that guns don't even need, and skips ammo/LoF which everything else needs.

**(b) Single-shot pistols/rifles: ammo + reload + LoF, no burst/aimed** — effort M, risk low. RECOMMENDED.
- Formats: extend WeaponProtoStats (+projectilePid, minStrength, apCost2, critFail, perk, rounds, caliber, ammoTypePid, ammoCapacity, soundCode) + new AmmoProtoStats (6 ints); MapFile: read weapon ammoQuantity/ammoTypePid + ammo quantity (Q3). All verified byte layouts above.
- CombatMath: add the Q1 subset (distance term, ammo AC mod, min-ST, range/no-ammo/LoF gates); damage = existing formula + ammo DR mod + mult/div + the ×2 default-mult-then-/2 wrapper (numerically identity for melee).
- Viewer: LoF walk (~70 lines, Q2); fire visual = existing PlayActionOnce path with anim 'j' (Q4, zero new animator features); dude art swap to hmjmps (Q5); reload action (2 AP, R key / button); AI branch per Q6.
- SaveState V2 per Q3.
Covers every gun observed in denbus1/denbus2/kladwtwn except SMG burst mode (10mm SMG appears only as loot, and its primary mode is single anyway) — the Den plays honestly on this rung.
- Risk: the to-hit distance term and LoF interact with PE/AC stats we already store; main unknown is per-tile object index perf for the Bresenham callback (denbus2 ≈ 2.8k objects — fine, we already bucket by tile for rendering/picking).

**(c) + burst and aimed shots** — effort L, risk medium-high.
Burst = `_compute_spray` (combat.cc:3680–3779): rounds split into center/left/right thirds, per-round to-hit, stray rounds hit critters along three `_make_straight_path_func` cones, extras-damage lists, multi-victim display — easily 2–3× the rung-(b) combat code, plus burst FRM 'k' handling and per-round ammo accounting. Aimed shots = hit-location menu UI + `hit_location_penalty` + critical-hit table (`attackComputeCriticalHit` + crit_tables per body part) — the crit table port alone is big. Defer both; nothing in the Den maps requires them.

**Recommendation: rung (b).** It is the smallest slice that makes ranged combat real (ammo scarcity, reload decisions, cover via LoF), reuses the melee animator unchanged because 10mm-class guns are visually hitscan, and leaves (a)'s projectile animator and (c)'s spray/crit systems as clean later increments.
