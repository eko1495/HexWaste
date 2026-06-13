# Phase-9 Track D: Combat physics — knockback, throwing, explosives, burst

Scope of this note: the **engine mechanics** for knockdown/knockback, throwing,
explosives, metarule(49), and burst. Content availability (does the slice carry
weapons that exercise these?) is Track E's job, but I re-ran the
`ContentAudit` tool against the eight raw slice maps so each feature carries a
hard "what fires it" census — see the boxes per section. All engine claims cite
`reference/fallout2-ce/src/<file>.cc:LINE`; all data numbers are quoted from the
real protos/maps via the audit tool, not from memory.

Enum values used throughout (real, from `obj_types.h:127-149`, `animation.h`,
`proto_types.h:60-67`):
`DAM_KNOCKED_OUT=0x01, DAM_KNOCKED_DOWN=0x02, DAM_DEAD=0x80, DAM_HIT=0x100,
DAM_CRITICAL=0x200, DAM_BYPASS=0x800, DAM_BACKWASH=0x400000`;
`ANIM_FALL_BACK=20, ANIM_FALL_FRONT=21, ANIM_PRONE_TO_STANDING=36,
ANIM_BACK_TO_STANDING=37, ANIM_WALK=1, ANIM_THROW_ANIM=18, ANIM_FIRE_BURST=46`;
`DAMAGE_TYPE_EXPLOSION=6` (NORMAL=0,LASER=1,FIRE=2,PLASMA=3,ELECTRICAL=4,EMP=5,EXPLOSION=6).

---

## (1) Knockdown / knockback

### When knockback fires

Computed in `attackComputeDamage` (combat.cc:4501) at the very end, lines
**4633-4659**. The gate (combat.cc:4633-4637):

```
knockbackDistancePtr != nullptr                                    // defender, not attacker (4506-4517)
&& (critter->flags & OBJECT_MULTIHEX) == 0                          // not a multihex critter
&& (damageType == DAMAGE_TYPE_EXPLOSION                             // explosion OR
    || attack->weapon == nullptr                                    // unarmed (no weapon) OR
    || weaponGetAttackTypeForHitMode(...) == ATTACK_TYPE_MELEE)     // melee
&& PID_TYPE(critter->pid) == OBJ_TYPE_CRITTER
&& !_critter_flag_check(critter->pid, CRITTER_NO_KNOCKBACK)
```

So knockback fires on **explosion damage, unarmed attacks, or melee attacks** —
NOT on ranged guns (a 10mm to the chest never shoves). Confirmed by the
`knockbackDistancePtr = nullptr` for the attacker branch (4517), i.e. attackers
never knock themselves back.

### Distance

combat.cc:4651-4653:
```
int knockbackDistanceDivisor = weaponGetPerk(weapon) == PERK_WEAPON_KNOCKBACK ? 5 : 10;
*knockbackDistancePtr = *damagePtr / knockbackDistanceDivisor;
```
**distance = damage / 10** (integer), or /5 with the Knockback weapon perk
(no slice weapon has it — skip). Stonewall perk on the *dude* halves it 50% of
the time (4640-4657) — dude-only perk, skip for our scope (we have no perks).
For explosions the same formula is duplicated in `_compute_explosion_damage`
(actions.cc:1822-1825): `*knockbackDistancePtr = damage / 10`.

### The shove geometry (the part to port carefully)

`actionKnockdown` (actions.cc:102-154). The shove direction is the
attacker→defender line: `knockbackRotation = tileGetRotationTo(attacker->tile,
defender->tile)` (actions.cc:553 for the main defender, :520 for blast extras).
Then it walks straight (actions.cc:122-137):

```
for (distance = 1; distance <= maxDistance; distance++) {
    tile = tileGetTileInDirection(obj->tile, rotation, distance);
    if (_obj_blocking_at(obj, tile, elevation) != nullptr) { distance--; break; }   // YES — blocked by occupied tiles
    if (isExitGridAt(tile, elevation)) { distance--; break; }                        // and stops before exit grids
}
distance--;   // step back one (started at 1)
```

So **yes, blocked by occupied tiles** — the critter stops on the last empty tile
before an obstacle (or exit grid). `MAX_KNOCKDOWN_DISTANCE = 20` caps it
(actions.cc:40,116-118). Our `Hex.HexGrid.TileInDirection` + `RotationTo`
(HexGrid.cs:35,83) are 1:1 ports of `tileGetTileInDirection`/`tileGetRotationTo`
— this geometry drops straight onto them; the only new host call is
"is tile occupied?" which the viewer already tracks (`_blockedTiles`).

### Two distinct knockdown paths — DO NOT conflate (the subtle one)

`_show_damage_to_object` (actions.cc:292) branches on whether a **crit flag**
(DAM_KNOCKED_OUT|DAM_KNOCKED_DOWN) is set vs a **pure shove**:

- **Crit-induced knockdown** (actions.cc:400-409): flag is set → fall anim,
  shove via `actionKnockdown`, and the critter **stays prone**. It gets up on
  its next turn (see below). DAM_KNOCKED_DOWN persists in `combat.results`.
- **Pure shove, no crit flag** (actions.cc:416-423): `knockbackDistance != 0`
  but no flag → fall + shove, then **immediately registers the get-up anim in
  the same sequence** (`animationRegisterAnimate(defender, ANIM_BACK_TO_STANDING
  / ANIM_PRONE_TO_STANDING, -1)`). The critter falls, slides, stands back up,
  no persisting prone status, **no AP cost**.

This matters for our model: a grenade or a melee hit that does ≥10 damage
*always* shoves and the victim *bounces back up* unless the hit was ALSO a
critical that set the knockdown flag. The flag only comes from the crit table
(combat.cc:195-460) — `_check_for_death` (combat.cc:4766-4777) sets ONLY
DAM_DEAD, never knockdown. So **knockdown-that-sticks requires the crit table**
(Track-D/-aimed work, M3), while **knockback-that-bounces** is standalone and
can ship without crits (M2).

### Get-up anims + AP cost

When a critter that *kept* the prone flag starts its turn, `_combat_turn`
(combat.cc:3225) at lines 3259-3262 calls `_combat_standup` if
`!a2 && _critter_is_prone(obj)`. `_critter_is_prone` (critter.cc:986-999) =
`results & (KNOCKED_OUT|KNOCKED_DOWN)` set OR anim in the fall range.

`_combat_standup` (combat.cc:5391-5414):
```
v2 = 3;
if (a1 == gDude && perkGetRank(a1, PERK_QUICK_RECOVERY)) v2 = 1;
ap = max(ap - v2, 0);
_dude_standup(a1);
```
**AP cost = 3** (1 with Quick Recovery — dude-only perk, skip). `_dude_standup`
(animation.cc:3182-3196): plays **ANIM_BACK_TO_STANDING (37)** if currently in
ANIM_FALL_BACK, else **ANIM_PRONE_TO_STANDING (36)**, then clears
DAM_KNOCKED_DOWN (animation.cc:3195). AP is reset to MaxAP at turn start
(combat.cc:3211-3217) BEFORE the standup deduction, so a knocked-down critter
effectively loses 3 AP off the top of its next turn.

### +40 to-hit vs prone

`attackDetermineToHit`, combat.cc:4474-4476:
```
if (targetIsCritter && defender != nullptr
    && (defender->data.critter.combat.results & (DAM_KNOCKED_OUT | DAM_KNOCKED_DOWN)) != 0) {
    toHit += 40;
}
```
**+40** when the target carries KNOCKED_OUT or KNOCKED_DOWN. Cap is +95
(combat.cc:4489-4491). This is one line in our `CombatMath.ToHitChance` once a
`KnockedDown` bit exists on `CritterState`.

> **CONTENT (audit, 8 maps):** knockback fires from melee (knives/clubs/
> sledges/spears/crowbars — present on every Den/Klamath map) and from the 2
> explosives (Frag Grenade, Molotov on kladwtwn) + Plastic Explosives on
> arcaves. Persisting knockdown needs the crit table (M3). So **knockback-bounce
> is exercisable today by any melee fight**; persisting-prone + the +40 only
> pays off once crits land.

### Sizing — knockback

- **Bounce-only knockback (no persisting flag): S, ~70 LoC.** `defenderKnockback
  = damage/10` in the damage path (gate: explosion|unarmed|melee), the straight-
  walk shove with occupancy/exit-grid stop, fall+stand anim pair. Needs a
  `TileChanged` host callback (the corpse/move plumbing M0 already routes).
- **Persisting knockdown + get-up + +40: M, ~120 LoC** but **only meaningful
  with the crit table** (the flag's sole non-explosion source). `KnockedDown`
  bool on `CritterState`; turn-start `_combat_standup` (−3 AP + anim 36/37);
  +40 in to-hit; **additive-V2 save delta** (1 bool per critter ordinal — fits
  the existing per-map delta machinery, no Version bump). Maps to **M3**
  alongside crits; the bounce-only S can land earlier (M2).

---

## (2) Throwing

### The attack path — it reuses the ranged math, plus ONE animator mode

To-hit/damage: `attackCompute` (combat.cc:3819) is the single attack entry for
all hit modes; throwing differs only in `anim == ANIM_THROW_ANIM` (=18) and
`attackType == ATTACK_TYPE_THROW` (=3). To-hit is `attackDetermineToHit` exactly
as for guns — our `CombatMath.ToHitChance(skill, distance, …)` already does this;
the skill is **Throwing** (already in `SkillSet.cs:25`). The one math wrinkle is
**range**: `weaponGetRange` (item.cc:1611-1627) for a throw weapon =
`min(maxRange1, 3 * effectiveStrength)` where effectiveStrength = ST (+2/Heave-Ho
for the dude — skip the perk). So a ST-6 dude throws a spear (maxRange1=8) at
range min(8,18)=8; a rock (maxRange=15) at min(15,18)=15.

### The flight — the new animator rung

`_action_ranged` (actions.cc:692). For throws (actions.cc:753-778):
```
projectile = weapon;                         // the thrown weapon IS the projectile
weaponFid = weapon->fid;
itemRemove(attacker, weapon, 1);             // removed from hand
replacedWeapon = itemReplace(...);           // auto-rewield next of same kind
objectSetFid(projectile, projectileProto->fid, nullptr);   // swap to in-flight art
_obj_connect(weapon, attacker->tile, ...);   // place at attacker's tile on the map
```
Then it flies (actions.cc:792-806):
```
projectileOrigin = _combat_bullet_start(attacker, defender);
objectSetLocation(projectile, projectileOrigin, ...);
objectSetRotation(projectile, tileGetRotationTo(attacker->tile, defender->tile));
animationRegisterMoveToTileStraight(projectile, defender->tile, elev, ANIM_WALK, 0);  // <-- THE NEW MODE
delay = _make_straight_path(projectile, origin, defender->tile, ...) - 1;             // (or attack->tile on a miss)
```
`animationRegisterMoveToTileStraight` is "move an object along a straight hex
line at walk speed". This is **the one missing animator mode** — Hexwaste's
`ObjectAnimator` already plays per-frame FRM offsets; what's new is tweening an
object between the *screen positions* of two hexes over N frames, then firing a
completion callback. ~60-80 LoC (consistent with the phase-7 and phase-8 sizings
in `p7-track-a-ranged.md:153` and `p8-track-c-combat.md:85`).

### Recoverable?

For a **non-explosive thrown weapon** (rock/spear/throwing-knife) the projectile
FID is restored to the weapon FID on landing (actions.cc:937-938:
`animationRegisterSetFid(projectile, weaponFid, -1)`), and it was `_obj_connect`-
ed onto the map — so **yes, it lands on the ground at the target tile and is
recoverable** (rocks/spears pick back up). For **explosive throws** (grenade/
molotov) the projectile is hidden/destroyed after the blast (actions.cc:926-936,
the `hideProjectile` callback) — not recoverable, by design.

> **CONTENT (audit, 8 maps): THROWING-class = 7 weapons, abundant.**
> Spear (0x07, range 2/8, on artemple/arcaves/denbus1/denbus2/kladwtwn/KLAMALL),
> Rock (0x13, range 15, denbus1/denbus2/kladwtwn — incl. on-ground copies),
> Throwing Knife (0x2D, range 16, denbus2/kladwtwn/KLAMALL), Flare (0x4F),
> Sharpened Spear (0x118, KLAMALL), plus the 2 explosives below. **This is
> immediately visible, fightable content** — unlike burst.

### Sizing — throwing

- **Animator mode (move-along-straight-line + callback): S/M, ~60-80 LoC.**
  Shared by guns-with-projectiles later, but guns in our slice are hitscan so
  this is throwing's cost.
- **Throw attack wiring: S, ~50 LoC.** `range = min(maxRange1, 3*ST)` in the
  range calc, Throwing skill into the existing to-hit, item-remove-from-hand +
  land-on-ground (recoverable for non-explosive), auto-rewield.
- **Total throwing (non-explosive): M.** Maps to **M4** ("ranged depth: throwing
  + aimed"). Explosive throws fold into M3's explosion path (below) — a grenade
  is "throw + AoE at landing tile".

---

## (3) Explosives

### actionExplode (actions.cc:1582)

The canonical blast. Steps (actions.cc:1582-1724):
1. Spawn a **misc-10 explosion object**: `fid = buildFid(OBJ_TYPE_MISC, 10, 0,
   0, 0)` (actions.cc:1594), hidden, NO_SAVE, placed at the blast tile; plus 6
   adjacent visual copies (actions.cc:1605-1623) — those are purely cosmetic
   FRM rings.
2. `critter = _obj_blocking_at(nullptr, tile, elevation)` — the critter standing
   on the center tile is the primary defender (actions.cc:1625-1630).
3. `attackInit(attack, explosion, critter, …)`; `attack->attackerFlags = DAM_HIT`
   — **the explosion object is the attacker** (actions.cc:1632-1635). This is
   what makes metarule(49) work (see §4).
4. Center damage: `_compute_explosion_damage(min, max, critter, &knockback)`
   (actions.cc:1643).
5. AoE: `_compute_explosion_on_extras(attack, 0, 0, 1)` (actions.cc:1646) →
   each extra critter takes `_compute_explosion_damage` (actions.cc:1648-1655).
6. `attackComputeDeathFlags` + (animated) `_show_damage` / (non-animated)
   `critterKill` (actions.cc:1657-1710).
7. **`_report_explosion`** (actions.cc:1727) applies damage and **broadcasts to
   scenery scripts** via `_combat_explode_scenery` → `_scr_explode_scenery`.

### _compute_explosion_on_extras (combat.cc:3987)

Ring-by-ring spiral around the blast tile (combat.cc:4013-4048): radius starts
at 1, walks each ring, until `weaponGetGrenadeExplosionRadius`/`Rocket…` is
exceeded. **Default radii (item.cc:3376-3382): grenade = 2, rocket = 3.** Plain
`actionExplode` calls with `isGrenade=0`, so it uses the **rocket radius path
(3)** (combat.cc:4035). For each tile, `_obj_blocking_at` finds a live, non-
SHOOT_THRU critter with unblocked LoS to center (`_combat_is_shot_blocked`,
combat.cc:4050-4055) and adds it to `attack->extras` (cap = `explosionGetMaxTargets`
= 6, combat.cc:4020,4022; item.cc:3534). Self-hit is possible: the attacker in
radius gets DAM_BACKWASH (combat.cc:4056-4060). Our `LineOfFire.Trace`
(LineOfFire.cs:18) is the LoS check; the ring spiral is `TileInDirection` walks.

### _compute_explosion_damage (actions.cc:1811)

```
damage = randomBetween(min, max) - DT_explosion;
if (damage > 0) damage -= DR_explosion * damage / 100;
if (damage < 0) damage = 0;
knockback = (multihex ? 0 : damage / 10);
```
Simple roll minus explosion DT/DR — our `CritterState` already exposes
DT/DR per damage type.

### damage_p_proc to ITEM + SPATIAL objects in radius

`_scr_explode_scenery` (scripts.cc:2879-2950): collects every ITEM-type and
SPATIAL-type script whose owner is on the same elevation and within `radius`
(= rocket radius 3) `tileDistanceBetween` of the blast tile, then for each:
```
script->fixedParam = 20;                       // scripts.cc:2940
script->source = nullptr; script->target = a1; // a1 = the explosion object
scriptExecProc(sid, SCRIPT_PROC_DAMAGE);       // scripts.cc:2949
```
So a scenery/spatial script's `damage_p_proc` runs with **fixed_param == 20**
and **target == the misc-10 explosion object** — this is exactly the temple-door
trigger (see §4). Hexwaste already dispatches `damage_p_proc` (the phase-4
container/door path) and runs spatial scripts (`RunSpatialsAt`, phase-7 M3), so
this is a radius-gated broadcast over existing plumbing.

### Dynamite countdown → reuses Hexwaste's phase-5 timer queue

Arming (`_obj_use_explosive`, proto_instance.cc:868-926):
```
seconds = _inven_set_timer(explosive);          // player-set countdown
explosiveActivate(&explosive->pid);             // DYNAMITE_I(51)->DYNAMITE_II(206)
delay = 10 * seconds;
roll = perkHasRank(DEMOLITION_EXPERT) ? SUCCESS : skillRoll(SKILL_TRAPS, 0);
switch (roll) {
  CRITICAL_FAILURE: delay = 0;  eventType = EXPLOSION_FAILURE;  // blows up now
  FAILURE:          delay /= 2; eventType = EXPLOSION_FAILURE;
  default:                      eventType = EXPLOSION;
}
queueAddEvent(delay, explosive, nullptr, eventType);
```
Detonation (`queue.cc:451-493`, `_queue_do_explosion_`):
```
explosiveGetDamage(pid, &min, &max);            // dynamite 30-50, plastic 40-80 (item.cc:3379-3382)
if (DEMOLITION_EXPERT) { min += 10; max += 10; }
actionExplode(tile, elevation, min, max, gDude, animate);
_obj_destroy(explosive);
```

This maps **directly** onto Hexwaste's existing timer queue
(`ScriptHost.AddTimer(map, owner, delayTicks, param)`, ScriptHost.cs:241; 1 tick
= 100 ms): the engine's `delay = 10 * seconds` is in 1/10-sec engine ticks, i.e.
identical tick granularity. So a dynamite arm = `AddTimer(map, explosive,
10*seconds, EXPLOSION_TAG)`, and the due callback runs our `ActionExplode`. The
TRAPS skill roll (we have skills now) decides premature/half/normal — our
`ICombatRng`/skill roll covers it.

> **CONTENT (audit, 8 maps):**
> - **EXPLOSIVE weapons: 2** — Grenade (Frag) 0x19 (dmg 20-35 explosion, range
>   15, kladwtwn) and Molotov Cocktail 0x9F (dmg 8-20 explosion, range 12,
>   kladwtwn). Both are throw-class (anim THROW) → "throw + AoE at landing".
> - **Plastic Explosives 0x55 (idx 85)** on **arcaves.map[e1, in a container]**
>   — real content for the timed-explosive/temple-door beat.
> - **No dynamite (PID 51) placed** in any slice map; plastic explosives are the
>   only timed explosive present (arcaves). The temple door itself is on
>   artemple — so the canonical "blow the temple door" beat needs the player to
>   *carry* explosives there (none on artemple); arcaves plastic-ex is the
>   provable timed-explosive demo. Flag: the temple-door beat is engine-real but
>   the player must bring explosives across maps — honest about that.

### Sizing — explosives

- **`ActionExplode` core (spawn misc-10, ring AoE via `_compute_explosion_on_
  extras`, `_compute_explosion_damage`, death flags, multi-victim apply): M,
  ~150 LoC Formats.** Ring spiral + LoS-gated extras (cap 6) + per-victim roll.
- **`_scr_explode_scenery` broadcast (radius-3 ITEM/SPATIAL `damage_p_proc`,
  fixed_param=20, target=explosion obj): S, ~40 LoC** over existing
  damage_p_proc + spatial dispatch.
- **Dynamite/plastic timer arm + detonate: S, ~50 LoC** — reuses phase-5 timer
  queue verbatim; TRAPS roll for premature.
- **Grenade/molotov throw = throwing (M4) + the explosion core.** Knockback from
  the blast is the explosion branch of §1 (free once §1 ships).
- **Total: M.** Maps to **M3** (explosives + crits + persisting knockdown — they
  share the damage-results plumbing and the crit-table flag). The metarule(49)
  + door beat is the marquee demo.

---

## (4) metarule(49) — confirmed rule number + return

The rule number the prompt asked to confirm: **METARULE_WEAPON_DAMAGE_TYPE = 49**
(interpreter_extra.cc:78). Note the name is *weapon damage type*, not literally
"explosion". Handler (interpreter_extra.cc:3297-3315, inside `opMetarule`,
opcode 0x810B per interpreter_extra.cc:4980):
```
case METARULE_WEAPON_DAMAGE_TYPE:
    Object* object = param.pointerValue;
    if (PID_TYPE(object->pid) == OBJ_TYPE_ITEM) {
        if (itemGetType(object) == ITEM_TYPE_WEAPON)
            result = weaponGetDamageType(nullptr, object);   // a real weapon: its dmg type
    } else {
        if (buildFid(OBJ_TYPE_MISC, 10, 0, 0, 0) == object->fid) {
            result = DAMAGE_TYPE_EXPLOSION;                   // <-- THE special case
        }
    }
```
So **metarule(49) on the misc-10 explosion object returns DAMAGE_TYPE_EXPLOSION
(=6)** (interpreter_extra.cc:3306-3308). The temple door's `damage_p_proc` is
invoked by `_scr_explode_scenery` with `target = explosion object`; the script
calls `metarule(49, target)`, gets EXPLOSION, and opens. (Cross-checked against
our own phase-7 report, which independently disassembled this:
docs/phase7-research-report.md:11,27.) For a regular weapon item it returns that
weapon's `proto.item.data.weapon.damageType` (item.cc:1294-1309). To wire it,
Hexwaste's IntVm needs the `metarule` opcode to handle subrule 49: if the target
object is our spawned explosion marker → return 6; if it's a weapon item →
return the proto damage type. **S, ~25 LoC** (one case in the metarule switch).
This is the only piece that makes "open the temple door legitimately by blast"
real, and it has no dependency beyond §3's explosion object existing.

---

## (5) Burst — `_compute_spray` (combat.cc:3703)

### The cone via 3 LoF walls + per-round accounting

`_compute_spray` (combat.cc:3703-3795):
```
ammoQuantity = min(ammoGetQuantity(weapon), weaponGetBurstRounds(weapon));   // 3707-3711
roll = randomRoll(accuracy, criticalChance, NULL);                            // 3716
if (CRITICAL_FAILURE) return roll;                                            // 3718-3720
if (CRITICAL_SUCCESS) accuracy += 20;                                         // 3722-3724
// split rounds (no burst-mod): combat.cc:3735-3746
centerRounds  = max(ammoQuantity/3, 1);
leftRounds    = ammoQuantity/3;
rightRounds   = ammoQuantity - centerRounds - leftRounds;
mainTargetRounds = max(centerRounds/2, 1);  // (decrements centerRounds if it was 0)
```
Then it rolls each round at the main target (combat.cc:3755-3759), and fires
along **three straight hex walls** (combat.cc:3765-3784):
- **center**: `mainTargetEndTile = _tile_num_beyond(attacker, defender, range)`,
  `_shoot_along_path(centerRounds - hits, …)` (3766-3767).
- **left**: center tile rotated `(rotation+1)%6`, beyond to range (3778-3780).
- **right**: center tile rotated `(rotation+5)%6` (i.e. −1), beyond to range
  (3782-3784).

`_shoot_along_path` (combat.cc:3629-3699) walks each wall with
`_make_straight_path_func(…, _obj_shoot_blocking_at)` (3641), and for each
critter on the line rolls `randomBetween(1,100) <= toHit` per remaining round
(3654-3657), accumulating into `extras[]` (cap 6, 3637) with summed damage/flags/
knockback for burst (3682-3686). Our `LineOfFire` already walks a straight hex
wall and counts critters on it — burst is "do that three times, with the two
side walls offset by `RotationTo(center, attacker)` ±1, and account per-round".

`_tile_num_beyond` = extend the attacker→target line out to `range` tiles past
the target (so strays continue past the victim). Hexwaste would need this small
helper (it's a `TileInDirection` walk to a distance, ~15 LoC).

### Sizing — burst

- **`_compute_spray` + `_shoot_along_path` + `_tile_num_beyond`: M, ~140 LoC**
  (three-wall cone, per-round accounting, extras list with summed damage). Plus
  burst FRM 'k' fire anim and per-round ammo deduction (the ammo math we already
  have).

> **CONTENT (audit, 8 maps): BURST-capable weapons = 0.**
> Census of every weapon placed across artemple/denbus1/denbus2/kladwtwn/
> KLAMALL/arcaves/klatrap/denres1: the only multi-shot guns are 10mm Pistol
> (rounds=1, single), Desert Eagle (single), Shotgun (rounds=2 but anim=SINGLE,
> not burst), Pipe Rifle (single). **No weapon in the raw slice has a BURST
> primary or secondary mode.** (The 10mm SMG — the canonical burst weapon — is
> PID 9 and appears in NO slice map; it could only enter via merchant stock the
> player buys.)
>
> **Recommendation: DEFER burst.** Building a 3-wall cone that nothing in the
> shippable content fires is the textbook "engine with no payoff." If a later
> phase adds the SMG to Tubby/Flick's restock (player-bought toy), revisit then.

---

## Per-feature sizing table → milestone map

| Feature | Effort / LoC | Felt-depth | Content in slice | Milestone |
|---|---|---|---|---|
| Knockback (bounce-only, no persist) | **S** ~70 | Med (every melee/blast shoves) | Yes — all melee maps | **M2** |
| Persisting knockdown + get-up(36/37, −3AP) + **+40** | **M** ~120 | High (the prone combo) | Needs crit flag → M3 | **M3** (with crits) |
| Knockback save delta (KnockedDown bool) | S ~20, **additive-V2** | — | — | M3 |
| Throwing animator mode (move-along-line) | **S/M** ~60-80 | High (rocks/spears fly) | Yes — 7 throw weapons | **M4** |
| Throwing attack wiring (range=min(r1,3·ST), Throwing skill, recoverable land) | **S** ~50 | High | Yes | **M4** |
| ActionExplode core (misc-10, ring AoE r=3, cap 6, per-victim roll) | **M** ~150 | High (AoE!) | grenade/molotov/plastic-ex | **M3** |
| `_scr_explode_scenery` broadcast (damage_p_proc, fixed_param=20) | **S** ~40 | — (enables door) | artemple door + scenery | **M3** |
| Dynamite/plastic timer arm+detonate (reuse phase-5 queue) | **S** ~50 | Med (the fuse beat) | Plastic-ex on arcaves | **M3** |
| metarule(49) → EXPLOSION for misc-10 | **S** ~25 | High (legit temple door) | artemple door | **M3** |
| Burst (`_compute_spray` 3-wall cone) | **M** ~140 | Med | **ZERO** weapons | **DEFER** |

### The M2-M5 read for Track D

- **M2** can take the **bounce-only knockback (S)** as a felt win that needs no
  crit table and no save change — every melee fight visibly shoves now.
- **M3** is the explosives + crits cluster: ActionExplode (M) + scenery
  broadcast (S) + dynamite timer (S) + **metarule(49) (S)** = the temple-door
  beat and grenade/molotov AoE; persisting knockdown + +40 (M) rides on the crit
  flag the crit table introduces. All share the damage-results / `combat.results`
  plumbing — do them together. **One additive-V2 save delta** (KnockedDown bit)
  covers it; no Version-3 bump needed.
- **M4** is throwing: the one new animator rung (S/M) + throw wiring (S);
  grenade/molotov reuse M3's explosion core at the landing tile.
- **DEFER burst** to a content-driven future phase (no burst weapon ships).

### Unverified / honest flags

- **No dynamite (PID 51) in any slice map** — plastic explosives (arcaves, e1,
  container) is the only timed explosive with real placement. The "blow the
  artemple door" beat is engine-correct but requires the player to *carry*
  explosives onto artemple (artemple itself places none); the provable
  timed-explosive demo is arcaves. Stated, not assumed.
- **Heave-Ho / Stonewall / Quick-Recovery / Knockback / Demolition-Expert perks
  intentionally omitted** — we have no perk system; the base formulas (range
  3·ST, distance dmg/10, get-up −3AP) are the no-perk paths and are what every
  slice fight uses.
- **`_inven_set_timer` exact UI** (the player picks the countdown seconds) not
  ported here — for our scope a fixed countdown (e.g. the original default) is
  fine; the *math* (`delay = 10*seconds`, TRAPS roll) is what's load-bearing and
  is cited. UNVERIFIED: the exact default seconds value (set interactively in the
  engine's timer dialog, not a constant I could cite from source).
- Molotov is `dmgType=explosion` per the audit (so `isGrenade=true`, actions.cc:
  727) but `_pick_death` re-labels it FIRE for the *death animation only*
  (actions.cc:190-194) — cosmetic, no effect on damage/AoE.
- Burst-mod (sfall `burstModComputeRounds`, combat.cc:3733) is OFF by default
  (`gBurstModEnabled`) — the vanilla third-split (3735-3746) is the path to port
  if burst is ever un-deferred.
