# Phase-9 Track C: Criticals + Aimed Shots — the table reality check

All line numbers are `reference/fallout2-ce/src/<file>` as cloned. The crit
tables are big but the LOGIC around them is small; the whole feature splits
cleanly into "transcribe a checksum-verifiable data blob" and "wire ~40 lines
of trigger + a 8-button menu". Quoted values are real.

## Q1 — The critical-hit effect tables: exact dimensions + total entry count

There are **two** tables, both `CriticalHitDescription` arrays.

**(A) `gCriticalHitTables`** — the per-killtype table. Declaration
(combat.cc:189):

```c
static CriticalHitDescription gCriticalHitTables[SFALL_KILL_TYPE_COUNT][HIT_LOCATION_COUNT][CRTICIAL_EFFECT_COUNT] = {
```

Dimensions resolve to:
- `SFALL_KILL_TYPE_COUNT` = `KILL_TYPE_COUNT * 2` = **38** (proto_types.h:131; `KILL_TYPE_COUNT` = 19, the enum MAN..BIG_BAD_BOSS at proto_types.h:107-126). The array is *dimensioned* 38 but only **19** kill-type blocks are literally initialized in source — I counted `grep -c "// KILL_TYPE_"` over combat.cc:189-1786 = **19**, in exact enum order (MAN=0 at :190 … BIG_BAD_BOSS=18 at :1702). The other 19 slots are implicit zero-fill (sfall's short-killtype headroom; never reached by base content).
- `HIT_LOCATION_COUNT` = **9** (combat_defs.h:75-87: HEAD, LEFT_ARM, RIGHT_ARM, TORSO, RIGHT_LEG, LEFT_LEG, EYES, GROIN, UNCALLED).
- `CRTICIAL_EFFECT_COUNT` = **6** (combat_defs.h:8) — the six severity rows per (killtype, location).

**Literal rows actually written: 19 × 9 × 6 = 1026.** Verified empirically:
`grep -cE "^\s*\{ [0-9]"` over combat.cc:189-1786 = **1026**, and
`grep -c "// HIT_LOCATION_"` = **171** = 19×9. ✔

**(B) `gPlayerCriticalHitTable`** — used when the *defender is the dude*
(combat.cc:4121-4122). Declaration (combat.cc:1791):

```c
static CriticalHitDescription gPlayerCriticalHitTable[HIT_LOCATION_COUNT][CRTICIAL_EFFECT_COUNT] = {
```

= 9 × 6 = **54** rows. Verified: `grep -cE "^\s*\{ [0-9]"` over
combat.cc:1791-1864 = **54**. ✔

**TOTAL literal rows to transcribe for full fidelity = 1026 + 54 = 1080**,
each row = **7 ints** (the `CriticalHitDescription` struct, combat_defs.h:138-161):
`{ damageMultiplier, flags, massiveCriticalStat, massiveCriticalStatModifier,
massiveCriticalFlags, messageId, massiveCriticalMessageId }`.

Two mirror copies exist at runtime (`gBaseCriticalHitTables` /
`gBasePlayerCriticalHitTable`, combat.cc:1967-1968) — those are just
sfall's "pristine backup" so `c_*` config overrides can be reset; **not a
second data source**, ignore for the port.

Message-id range across both tables: **1000 .. 7106** (verified by
`grep -oE "[0-9]{4,5}"` min/max over combat.cc:189-1864). These index
combat.msg; the `messageId` is the normal-crit line, `massiveCriticalMessageId`
the massive-crit line. `5000`/`5100`/`7100` etc. are the per-killtype "nothing
extra happened" fallbacks.

## Q2 — The crit trigger (how a crit is decided)

A crit is decided by the **same to-hit roll** that decides whether the attack
lands — `randomRoll` upgrades a SUCCESS to a CRITICAL_SUCCESS. There is no
separate "did I crit" dice; the crit chance is folded into the to-hit roll as
the `criticalSuccessModifier` argument.

**The call (single-shot path), combat.cc:3852-3853:**
```c
int chance = critterGetStat(attack->attacker, STAT_CRITICAL_CHANCE);
roll = randomRoll(accuracy, chance - hit_location_penalty[attack->defenderHitLocation], nullptr);
```
i.e. `criticalSuccessModifier = critical_chance − hit_location_penalty[loc]`.
Because `hit_location_penalty` values are **negative** (Q3), subtracting them
ADDS to the crit modifier — an aimed shot is both harder to hit AND more likely
to crit when it does.

**`randomRoll` itself (random.cc:85-95, 101-131):**
```c
int delta = difficulty - randomBetween(1, 100);        // difficulty = accuracy
... roll = ROLL_SUCCESS;                                 // when delta >= 0
if (... gameTime/GAME_TIME_TICKS_PER_DAY >= 1) {        // criticals enabled from day 2
    if (randomBetween(1, 100) <= delta / 10 + criticalSuccessModifier)
        roll = ROLL_CRITICAL_SUCCESS;
}
```
So: roll d100; `delta = accuracy − d100`. If `delta ≥ 0` the hit lands; then a
**second** d100 ≤ `delta/10 + (crit_chance − loc_penalty)` upgrades to a crit.
(Symmetric path for misses upgrading to critical-failure at d100 ≤ −delta/10,
random.cc:113-117 — out of scope for this track; we keep flat misses.)

`STAT_CRITICAL_CHANCE` = stat index **15** (stat_defs.h:38) and equals LK by
default (`CriticalChance = LK`, stat.cc:574; already in Hexwaste
CritterState.cs:22). Hexwaste's `RollHit` is `rng.Next(1,101) <= chance`
(CombatMath.cs:20) — that's `d100 <= accuracy`, the exact `delta ≥ 0` test
(equivalent since `accuracy − d100 ≥ 0` ⇔ `d100 ≤ accuracy`). To add crits we
keep the same first roll and, on success, do the second roll using the captured
`delta = accuracy − d100`.

**Severity selection — `attackComputeCriticalHit` (combat.cc:4089-4159):**
```c
attack->attackerFlags |= DAM_CRITICAL;                  // :4100
int chance = randomBetween(1, 100);                     // :4102
chance += critterGetStat(attack->attacker, STAT_BETTER_CRITICALS);  // :4104
int effect;
if (chance <= 20) effect = 0;                           // :4107-4118
else if (chance <= 45) effect = 1;
else if (chance <= 70) effect = 2;
else if (chance <= 90) effect = 3;
else if (chance <= 100) effect = 4;
else effect = 5;                                        // only reachable via Better Criticals > 0
```
`STAT_BETTER_CRITICALS` = stat index **16** (stat_defs.h:39 — counting the
enum from STAT_STRENGTH=0: CRITICAL_CHANCE=15, BETTER_CRITICALS=16); 0 for a
base character (it's a perk stat), so `effect=5` (the worst row) is unreachable
without the Better Criticals perk — but the rows still must be transcribed
because Better-Crit-bearing critters and the dude-with-perk can hit them, and
checksum verification wants the whole blob.

Then the table lookup (combat.cc:4120-4126):
```c
if (defender == gDude)
    crit = &gPlayerCriticalHitTable[hitLocation][effect];
else
    crit = &gCriticalHitTables[critterGetKillType(defender)][hitLocation][effect];
attack->defenderFlags |= crit->flags;                   // :4128
attack->criticalMessageId = crit->messageId;            // :4132
```
`critterGetKillType` (critter.cc:745-763): dude ⇒ MAN or WOMAN by
`STAT_GENDER`; else `proto->critter.data.killType`. Hexwaste already parses
`KillType` from the PRO critter block (ProtoDatabase.cs:249,263) and gender
from the gcd (baseStats[34]) — both available.

**Massive-crit upgrade (combat.cc:4134-4139):**
```c
if (crit->massiveCriticalStat != -1) {
    if (statRoll(defender, crit->massiveCriticalStat,
                 crit->massiveCriticalStatModifier, nullptr) <= ROLL_FAILURE) {
        attack->defenderFlags |= crit->massiveCriticalFlags;
        attack->criticalMessageId = crit->massiveCriticalMessageId;
    }
}
```
`statRoll` (stat.cc:708-722): `value = stat + modifier; chance = rand(1,10);
return chance <= value ? SUCCESS : FAILURE`. So the upgrade fires when the
defender **fails** the stat check (e.g. EN−3 on a head crit) — i.e. a tough
target resists the massive effect. `damageMultiplier` returned is the
table cell's first int (combat.cc:4158).

`attackComputeCriticalHit` returns early with **2** (no crit applied, normal
2× multiplier) if the defender is INVULNERABLE or not a critter
(combat.cc:4092-4098) — keep that guard.

## Q3 — hit_location_penalty, aimed-shot AP cost, aimable locations

**The 8-location to-hit penalty array (combat.cc:172-182):**
```c
static int hit_location_penalty_default[HIT_LOCATION_COUNT] = {
    -40,   // HEAD
    -30,   // LEFT_ARM
    -30,   // RIGHT_ARM
      0,   // TORSO
    -20,   // RIGHT_LEG
    -20,   // LEFT_LEG
    -60,   // EYES
    -30,   // GROIN
      0,   // UNCALLED
};
```
(`hit_location_penalty[]` is the live copy, combat.cc:184; identical to default
unless sfall overrides.)

**Application in to-hit (combat.cc:4437-4441):**
```c
if (isRangedWeapon)
    toHit += hit_location_penalty[hitLocation];        // full
else
    toHit += hit_location_penalty[hitLocation] / 2;    // melee = half
```
So ranged eats the full penalty, melee/unarmed half. (Same `hit_location_penalty`
appears subtracted in the crit modifier, Q2 — full magnitude there regardless.)

**Aimed-shot AP cost = +1 (item.cc:1706-1712):**
```c
if (aiming) actionPoints += 1;
if (actionPoints < 1) actionPoints = 1;
```
applied at the very end of `weaponGetActionPointCost(critter, hitMode, aiming)`.

**When is `aiming` true** (combat.cc:3524-3533): if the chosen hit location is
anything other than TORSO or UNCALLED, `aiming = true` unconditionally; for
TORSO/UNCALLED it follows the player's aim toggle. So picking any of the 8
specific body parts always costs the +1.

**Which locations are aimable**: all 8 specific locations. The called-shot
menu (`calledShotSelectHitLocation`, combat.cc:5476-5586) draws exactly 4 left +
4 right buttons over these fixed arrays (combat.cc:1894-1907):
```c
_hit_loc_left[4]  = { HEAD, EYES, RIGHT_ARM, RIGHT_LEG };
_hit_loc_right[4] = { TORSO, GROIN, LEFT_ARM, LEFT_LEG };
```
The 9th, `HIT_LOCATION_UNCALLED`, is the default non-aimed shot (resolved to
TORSO for crit-table indexing at combat.cc:3838-3840). Aiming is *gated*: with
ONE arm crippled you can't aim a two-handed weapon, with BOTH arms crippled you
can't use weapons at all (`_combat_check_bad_shot`, combat.cc:5655-5667). For
our scope (the dude rarely crippled in the opening hour) this gate is optional
polish.

## Q4 — Crit EFFECT flags each table entry can carry (with values)

The `flags` and `massiveCriticalFlags` ints are bitmasks over `Dam`
(obj_types.h:126-151). Full set with hex values:

| Flag | Value | Meaning |
|---|---|---|
| DAM_KNOCKED_OUT | 0x01 | unconscious (lose multiple turns) |
| DAM_KNOCKED_DOWN | 0x02 | prone (get-up costs AP; +40 to-hit vs it) |
| DAM_CRIP_LEG_LEFT | 0x04 | cripple left leg |
| DAM_CRIP_LEG_RIGHT | 0x08 | cripple right leg |
| DAM_CRIP_ARM_LEFT | 0x10 | cripple left arm |
| DAM_CRIP_ARM_RIGHT | 0x20 | cripple right arm |
| DAM_BLIND | 0x40 | blinded (−25 to-hit while attacker, combat.cc:4470-4472) |
| DAM_DEAD | 0x80 | instant death |
| DAM_HIT | 0x100 | the hit landed (attacker flag, set by engine not table) |
| DAM_CRITICAL | 0x200 | this was a critical (set at combat.cc:4100) |
| DAM_ON_FIRE | 0x400 | burning (DoT) |
| DAM_BYPASS | 0x800 | armor bypass — DT/DR → 20% |
| DAM_LOSE_TURN | 0x8000 | lose next turn |
| DAM_CRIP_RANDOM | 0x200000 | resolve to a random limb (`_do_random_cripple`, combat.cc:4141-4143) |
| DAM_EXPLODE / DAM_DESTROY / DAM_DROP / DAM_RANDOM_HIT … | 0x1000.. | appear only in `massiveCriticalFlags` of a few exotic creature rows (e.g. combat.cc:400 "DAM_CRIP_ARM_RIGHT \| DAM_BLIND \| DAM_ON_FIRE \| DAM_EXPLODE") |

**damageMultiplier** is the first int (NOT a flag) — values seen are 2..8. It
feeds `attackComputeDamage`'s `bonusDamageMultiplier`; in the default damage
path the raw damage is `× damageMultiplier × ammoMult / ammoDiv / 2` (combat.cc:
4586-4601). Because of the trailing `/2`, a table value of `4` = effective 2×,
`6` = 3×, `8` = 4×. A *normal* (non-crit) hit passes multiplier **2**
(combat.cc:3844), so `/2` makes it identity — exactly what Hexwaste's
`RollDamage` already encodes (`damage = raw * 2 * ammoMult; /= ammoDiv; /= 2`,
CombatMath.cs:85-87). **The crit multiplier slots straight in where that
hardcoded `2` is.**

**DAM_BYPASS effect (combat.cc:4530-4532):**
```c
if ((*flagsPtr & DAM_BYPASS) != 0 && damageType != DAMAGE_TYPE_EMP) {
    damageThreshold = 20 * damageThreshold / 100;       // DT → 20%
    damageResistance = 20 * damageResistance / 100;     // DR → 20%
}
```
i.e. armor bypass reduces DT and DR to one-fifth before the subtraction in
`RollDamage`. Trivial to add (multiply the two by 20/100 when the BYPASS flag is
present).

## Q5 — Recommended minimal honest cut + pivot threshold

**Honor only {damageMultiplier, KNOCKED_DOWN (0x02), DEAD (0x80), BYPASS
(0x800)}; mask everything else** at apply-time. Rationale:
- `damageMultiplier` + `BYPASS` are pure number math already living in
  `RollDamage` — near-free, and they ARE the felt punch of a crit.
- `DAM_KNOCKED_DOWN` is Track-D's deliverable (the `KnockedDown` flag + +40
  vs-prone + get-up AP); honoring it here means crits feed that system for free.
- `DAM_DEAD` (and via massive-crit too) gives the instant-kill "crit to the
  eyes" moment that players remember; routes straight into the existing
  `KillCritter` path (ViewerGame.cs:2501).
- **Mask** DAM_KNOCKED_OUT/BLIND/LOSE_TURN/CRIP_*/ON_FIRE/CRIP_RANDOM/EXPLODE:
  each needs a status-tick system (unconscious turn-skip, blind to-hit penalty
  upkeep, limb-state on the critter + art, DoT scheduler) we don't have. Apply
  a mask: `defenderFlags &= (DAM_DEAD | DAM_KNOCKED_DOWN | DAM_BYPASS | DAM_CRITICAL)`
  after the table lookup. The damage multiplier and message text still apply, so
  the player still sees "was hit for critical damage" — only the secondary
  status is dropped. Document the mask exactly like the LoF deviation.

**TRANSCRIBE THE FULL TABLE ANYWAY** (all 1080 rows, both arrays, verbatim with
the real flag bitmasks). Reasons: (1) it's a mechanical copy of a contiguous
block, machine-checkable — a script can diff the generated C# initializer's
integer values against `combat.cc` token-for-token, giving a checksum-grade
regression net; (2) masking is an apply-time decision, so honoring more flags
later is a one-line mask change, not a re-transcription; (3) a *subset* table
(only the killtypes/locations present in the slice) is MORE work to justify and
verify than a straight copy and bakes in a content assumption.

**Transcription mechanics**: do NOT hand-type 1080 rows. Write a one-off
generator (`tools/` python or a `dotnet run` snippet) that reads
combat.cc:189-1864, resolves the `DAM_*`/`STAT_*` symbols to their integer
values (from the enum tables above), and emits a C# `static readonly int[,,,]`
(or a flat `int[]` + index helper) plus a checksum line. Keep the generated
file checked in; keep the generator + a unit test that re-derives the checksum.

**PIVOT THRESHOLD**: the table is **1080 rows × 7 ints = 7560 integers**. That
is far past any sane hand-transcription budget, so the rule fires immediately:
> If the table exceeds ~50 rows of hand-transcription (it's 1080), DO NOT hand-
> type it — generate it with a parser and a checksum test. If even the generator
> is deemed too much for one milestone, ship **multiplier-only with a single
> shared table** (transcribe just KILL_TYPE_MAN + KILL_TYPE_WOMAN + the player
> table = 3 × 54 = 162 rows, fall back to MAN for every other killtype) and
> defer the full per-creature table. The slice is humans + a few rats/geckos
> (denbus1/2 + Klamath, ~28 firearm critters per phase-7 notes), so MAN/WOMAN
> cover the overwhelming majority of felt crits; RAT (killtype 7) / GECKO (15)
> rows differ but are a small payoff. **UNVERIFIED: exact killtype distribution
> of slice critters — I did not parse the raw denbus/klamath protos for their
> `killType` byte; phase-7 confirms ~28 firearm HUMANS, so MAN/WOMAN dominate,
> but the rat/gecko count is unconfirmed. Parse `data/proto/critters/*.pro`
> killType (offset in the critter data block, ProtoDatabase.cs:249) to confirm
> before choosing subset-vs-full.**

## Sizing (separate the table from the logic)

- **Table transcription (generator + checksum test): S–M.** The generator is
  ~80-120 LoC (symbol map + regex over the literal block + C# emitter); the
  generated data file is large but machine-made; the checksum unit test is ~20
  LoC. Risk: low (it either matches the source token-for-token or it doesn't).
  This is "S in effort, M in line count", and the lines are not reviewed by
  hand.
- **Crit trigger logic: S (~40-60 LoC).** Capture `delta = accuracy − d100` in
  `RollHit`/`RangedMath` (or add a `RollHitDetailed` returning the delta), add
  the second `rng.Next(1,101) <= delta/10 + critChance − locPenalty` roll, the
  `effect` bucket (combat.cc:4107-4118), the table lookup keyed by killType/
  gender, the massive-crit `statRoll`, the mask, and feed `damageMultiplier` +
  `BYPASS` into `RollDamage`. All inputs already exist on `CritterState`
  (CriticalChance idx 15; BetterCriticals idx 16 = 0 at our scope; KillType in
  proto; DT/DR present).
- **Aimed-shot menu + AP: S–M (~80-120 LoC viewer + ~10 LoC Formats).** A
  hit-location picker (reuse the existing list/loot panel render), the
  `hit_location_penalty[]` const, full-vs-half application (CombatMath
  one-liner), `aiming ⇒ +1 AP` in the AP-cost call, default UNCALLED→TORSO. The
  crippled-arm gate (combat.cc:5655-5667) is optional.
- **Better Criticals stat: free** — already index 16; default 0, so the effect
  bucket behaves correctly with zero work; the perk itself stays out of scope.

## Headless test hooks

- **Table checksum**: a `[Fact]` that re-runs the generator's checksum over the
  committed data and asserts equality with the value derived from `combat.cc`
  (kept as a constant the generator prints). Catches any drift.
- **Crit trigger determinism**: extend the seeded `--fight` harness with a flag
  forcing the crit-upgrade roll to succeed; assert a specific (killtype, loc,
  effect) row yields the expected damageMultiplier + flags. Builds on the M0
  `ICombatRng` determinism gate (already present, ICombatRng.cs:19).
- **Aimed shot**: `--attack --aim eyes` style flag → assert to-hit drops by 60
  for ranged / 30 for melee and AP cost rises by 1; transcript-diffable.

## Save / cross-cutting

- Crit-induced status reduces to **DAM_KNOCKED_DOWN** at our cut, which is
  Track-D's `KnockedDown` and already representable in `MapObject.CombatResults`
  (an int bitmask, MapFile.cs:103; DAM_DEAD=0x80 already stored there). No new
  save field needed for crits beyond Track-D's additive-V2 knockdown delta.
- The crit tables are **static code**, not save state — nothing to serialize.

## Bottom line

- Trigger logic: **one extra d100** on a successful to-hit, modifier =
  `critChance − hit_location_penalty[loc]`, severity by `rand(1,100) +
  BetterCriticals` bucketed 20/45/70/90/100/>100 → rows 0-5, lookup by
  killType/gender, optional massive-crit `statRoll`. ~40-60 LoC.
- Tables: **1080 rows × 7 ints = 7560 integers**; generate + checksum, never
  hand-type; if even that's too big, ship MAN/WOMAN/player (162 rows) with
  MAN-fallback.
- Honor only {damageMultiplier, KNOCKED_DOWN, DEAD, BYPASS}, mask the rest;
  multiplier + BYPASS drop straight into the existing `RollDamage` ×2/÷2 wrapper.
- Aimed shots: 8 fixed locations, full ranged / half melee to-hit penalty, +1 AP.
