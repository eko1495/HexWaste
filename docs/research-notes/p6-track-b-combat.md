# Phase-6 Track B research — combat depth: weapons, armor, XP, corpse persistence

Reference tree: `<repo>/reference/fallout2-ce/src` (all file:line cites below are into it).
Project tree: `~/dev/FPOC`. Empirical values parsed from the real GOG game data at
`<repo>/game-data` with a throwaway probe (`/tmp/p6b-probe`) built on
`Hexwaste.Formats` (GameFileSystem + BigEndianReader). Repo untouched.

---

## 1. Equipped items

### Where equipped state lives
Equipped state is **bit flags on the item Object itself**, not a slot structure:

- `obj_types.h:78-80,86-87`:
  - `OBJECT_IN_LEFT_HAND  = 0x01000000`
  - `OBJECT_IN_RIGHT_HAND = 0x02000000`
  - `OBJECT_WORN          = 0x04000000`
  - `OBJECT_IN_ANY_HAND = LEFT|RIGHT`, `OBJECT_EQUIPPED = ANY_HAND|WORN`

Getters scan the owner's inventory for the flag — `inventory.cc:2771 critterGetItem2()`
(right hand: `item->flags & OBJECT_IN_RIGHT_HAND`), `inventory.cc:2793 critterGetItem1()`
(left hand), `inventory.cc:2815 critterGetArmor()` (`OBJECT_WORN`).

### MAP files vs runtime
Identical representation. MAP inventory items are full nested Object records
(`object.cc:412 objectRead()` reads `obj->flags` verbatim at line 425; nested inventory
items read at `object.cc:576/581`), so the equip bits come straight off disk. Our
`MapFile.cs ReadObject()` (line 334 reads `flags`) already preserves them on
`MapObject.Flags` — **no parser change needed**.

**Empirical (probe over real maps, flag mask 0x07000000):**
- `artemple.map`: 1/1 critter inventory items equipped — Cameron (pid 0x01000003, tile 21101)
  holds the **Spear (pid 7) IN_RIGHT_HAND** (flags 0x02000008).
- `denbus1.map`: 57/89 items equipped (e.g. guards: 10mm Pistol pid 8 RIGHT).
- `klamall.map`: 7/34 (knife pid 4 LEFT, etc.).

### Active attack weapon
The engine maps a *hit mode* to a hand: `item.cc:1001 critterGetWeaponForHitMode()` —
`HIT_MODE_LEFT_WEAPON_PRIMARY/SECONDARY/RELOAD → critterGetItem1`,
`HIT_MODE_RIGHT_* → critterGetItem2`, anything else → nullptr (unarmed).
Hit modes: `combat_defs.h:22-27` (LEFT_PRIMARY=0, RIGHT_PRIMARY=2, PUNCH=4, KICK=5).
For the dude, the current hit mode comes from the interface bar's active hand:
`interface.cc:1015 interfaceGetCurrentHitMode()` reads
`gInterfaceItemStates[gInterfaceCurrentHand]` (`gInterfaceCurrentHand`, interface.cc:170).

### Minimal PoC model
One "hand item" reference on the dude (skip left/right duality): inventory panel "equip"
toggles `OBJECT_IN_RIGHT_HAND` on the item's MapObject in `_dudeInventory`; attack path asks
"equipped weapon != null ? weapon stats : unarmed". NPCs: scan `MapObject.Inventory` for the
0x02000000/0x01000000 bit at CritterState construction (data already parsed). Caution: our
dude inventory currently serializes as `SavedItem(Pid, Count)` only — save format needs an
`Equipped` bool (or hand enum) per item, or re-equip is lost on load.

---

## 2. Attack FID resolution

### Proto field → FID weapon code
- Weapon proto field #1 of the weapon block is `animationCode`
  (`proto.cc:1585`, first read of the `ITEM_TYPE_WEAPON` case of
  `protoItemDataRead()`, proto.cc:1553; accessor `item.cc:1770 weaponGetAnimationCode()`).
- FID composition: `art.cc:1009-1011 buildFidInternal()`:
  `fid = (rotation<<28)&0x70000000 | objectType<<24 | (animType<<16)&0xFF0000 |
  (weaponCode<<12)&0xF000 | frmId&0xFFF`. The proto's `animationCode` is placed directly
  into the FID weapon-code nibble — see `inventory.cc:2582 _adjust_fid()`:
  `animationCode = proto->item.data.weapon.animationCode; fid = buildFid(OBJ_TYPE_CRITTER, base, 0, animationCode, 0)`.
- Filename letters: `art.cc:544 _art_get_code()` — first suffix letter = `'d' + (weaponCode-1)`
  (art.h:91-102: 1 knife='d', 2 club='e', 3 hammer='f', 4 spear='g', 5 pistol='h', 6 SMG='i',
  7 shotgun/rifle='j', 8 laser rifle='k', 9 minigun='l', 10 launcher='m');
  second letter = `'a'+anim` for anims ≤ ANIM_WALK, and `'c' + (anim-ANIM_TAKE_OUT)` for
  anims 38-47 (take-out='c', put-away='d', dodge='e', **thrust(41)='f'**, **swing(42)='g'**,
  point='h', unpoint='i', **fire-single(45)='j'**, fire-burst='k', fire-continuous='l').

### Which attack anim a weapon uses
`item.cc:1334 weaponGetAnimationForHitMode()`: index = `extendedFlags & 0xF` (primary) or
`(extendedFlags>>4)&0xF` (secondary) into `_attack_anim[9]` (item.cc:116):
{STAND, THROW_PUNCH, KICK_LEG, SWING, THRUST, THROW, FIRE_SINGLE, FIRE_BURST, FIRE_CONTINUOUS}.

### Empirical weapon protos (parsed from game-data; names from text\english\game\pro_item.msg)
| Weapon | PID | animationCode (FID letter) | dmg | dmgType | range1/2 | AP1/AP2 | minST | ammo | attackIdx pri/sec |
|---|---|---|---|---|---|---|---|---|---|
| Knife | 4 | 1 ('d') | 1-6 | normal | 1/1 | 3/3 | 2 | none | 3 swing / 4 thrust |
| Spear | 7 | 4 ('g') | 3-10 | normal | 2/8 | 4/6 | 4 | none | 4 **thrust** / 5 throw |
| Sharpened Spear | 280 | 4 ('g') | 4-12 | normal | 2/8 | 4/6 | 4 | none | 4 thrust / 5 throw |
| Combat Knife | 236 | 1 ('d') | 3-10 | normal | 1/1 | 3/3 | 2 | none | 3 swing / 4 thrust |
| 10mm Pistol | 8 | 5 ('h') | 5-12 | normal | 25/0 | 5/0 | 3 | cal 8, cap 12, 1 rd/shot, ammoTypePid 0x1D | 6 fire-single / 0 |
| 10mm JHP (ammo) | 29 | — | — | — | — | — | — | box 24, drMod +25, dmgMult 2/1 | — |
| 10mm AP (ammo) | 30 | — | — | — | — | — | — | box 24, drMod −25, dmgMult 1/2 | — |

10mm Pistol `projectilePid = -1` (hitscan — see §3); Spear throw projectilePid 0x05000007.

### Art that actually ships (DatDump list over critter.dat)
- **hmwarr** (our dude art): `AA AB AE AK AL AN AO AP AQ AR AS AT  BA BB BO BP  CH CJ
  GA GB GC GD GE GF GM  RA RB RO RP` — i.e. unarmed set + **complete spear ('g') set**:
  GA stand, GB walk, GC take-out, GD put-away, GE dodge, **GF thrust**, GM throw.
  **NO knife ('d'), NO pistol ('h'), and no GG (spear swing).** Spear is fine: its primary
  attack idx is 4 = THRUST = GF (parsed proto above), so no missing-art problem.
- **hmjmps**: full set for every weapon code — D* (knife incl. DG swing, DM throw),
  E* club, F* hammer, G* spear (incl. GG), H* pistol (incl. HJ fire-single), I* SMG,
  J* rifle, K* laser, L* minigun, M* launcher, plus the full B*-knockdown set per rotation.

**Consequence:** with the dude on hmwarr, the only equippable weapon whose attack art exists
is the spear class (animationCode 4). To demo knife or 10mm pistol on the dude, switch the
dude base art to hmjmps (or keep the existing `LoadMap` fallback: probe `artExists`, fall back
to weapon code 0 — but then the attack reads as a punch). NPCs in den/klamall already carry
pistols and their own art sets cover it (e.g. guard FIDs come with weapon code baked in).

---

## 3. Melee/thrown vs ranged — implementation cost

### Melee weapon (spear/knife class)
- To-hit: same shape as today — `combat.cc:4314 attackDetermineToHit()`: non-ranged path is
  just `toHit = weaponGetSkillValue(...)` (melee skill instead of unarmed; skill map
  `item.cc:100 _attack_skill[]`, melee = `30 + 2*(AG+ST) + ...` per skill.cc — actually
  SKILL_MELEE_WEAPONS base differs: 55 + (AG+ST)/2 per skill.cc table; verify when porting),
  then −AC (combat.cc:4427-4434), hit-location/2, etc. No distance terms.
- Damage: `item.cc:1244 weaponGetDamage()` =
  `randomBetween(minDamage, meleeDamage + maxDamage)` for melee (melee damage stat added,
  item.cc:1262-1265) → drop-in replacement for the `rand(1, 2+meleeDmg)` unarmed roll.
- AP: `item.cc:1643 weaponGetActionPointCost()` → proto `actionPointCost1` (spear 4, knife 3).
- Adjacency: `item.cc:1599 weaponGetRange()` → `maxRange1` (knife 1 = same adjacency as
  punch; spear 2 = one extra hex — trivially `HexGrid.Distance <= range`).
- Animation: one new action FRM per attack (hmwarr GF exists). Hit/death flow unchanged.

### Ranged (10mm pistol)
Extra machinery on top of all of the above:
- To-hit distance/perception term (`combat.cc:4331-4402`): for ATTACK_TYPE_RANGED,
  `distanceMod = distance(attacker, target)`; perception bonus
  `perceptionBonusMult * (perception - 2)` for the dude (mult 2 default, 4 long-range, 5 scope,
  combat.cc:4337-4349,4367-4371); clamp `distanceMod >= -2*perception` (4377);
  `distanceMod *= -4` (−12 if blind) (4381-4389); `toHit -= 10 * numCrittersInLof`
  (line-of-fire blockers, 4396-4402, needs `_combat_is_shot_blocked`); plus min-ST penalty
  `-20 per point` (4412-4421) and ammo AC modifier (4429).
- Ammo: per-shot decrement happens at animation end —
  `combat.cc:5332 _combat_anim_finished()` line 5350:
  `ammoSetQuantity(weapon, ammoQuantity - _main_ctd.ammoQuantity)` (single shot spends 1
  round, weapon proto `rounds`=1). Reload: `item.cc:1437 weaponAttemptReload()` (find matching
  ammo by `ammoTypePid` then by caliber) + `item.cc:1553 weaponReload()` (caliber check,
  partial-box math, sets `weapon.ammoTypePid`) + 2 AP (`item.cc:1650-1663`). Requires
  per-instance `ammoQuantity`/`ammoTypePid` — **currently Skip()ed in
  MapFile.ReadObjectData (line 418) and absent from SaveState.SavedItem** → parser + save
  format change.
- Projectile: cheap for the pistol — `actions.cc:86 _action_ranged()` only creates a
  projectile object when the weapon's `projectilePid` proto has a real fid
  (actions.cc:750-752); 10mm pistol projectilePid = −1 → pure hitscan, just play HJ
  fire-single on the attacker and the hit anim on the target.
- LoF check for targeting (straight-line blocker walk) is new code we don't have.

### Recommendation
**Melee weapon (spear) first.** It reuses the entire existing punch pipeline (roll → animate
→ damage-on-completion → corpse), needs zero parser changes beyond the weapon proto block
(animationCode/min/max/AP/range), zero save-format changes, and the dude's existing hmwarr
art ships the complete spear set — artemple's own Cameron already holds a spear in a real map.
Ranged is a fast follow if desired, but it drags in ammo state (MAP parse + save format),
reload UI, LoF, and the distance to-hit term: 3-4x the surface area.

---

## 4. Armor

### Proto fields — `proto.cc:1556-1564` (`protoItemDataRead` ITEM_TYPE_ARMOR case)
In file order (all big-endian int32 after the common item header):
`armorClass`, `damageResistance[7]`, `damageThreshold[7]`, `perk`, `maleFid`, `femaleFid`.
**Note the order: DR array comes BEFORE DT.** Damage types indexed 0-6:
normal, laser, fire, plasma, electrical, EMP, explosion (proto.h DamageType).

### Runtime combination
`stat.cc:182 critterGetStat()` does **NOT** consult worn armor. Instead, equipping mutates
the critter's **bonus stats**: `inventory.cc:2544 _adjust_ac()` —
`bonusAC += newArmor.AC − oldArmor.AC`, and for each of the 7 damage types
`bonusDR[type] += new−old`, `bonusDT[type] += new−old` (accessors `item.cc:2088
armorGetArmorClass`, 2101 armorGetDamageResistance, 2114 armorGetDamageThreshold).
So `critterGetStat = base + bonus` keeps working untouched — which maps 1:1 onto our
`CritterState.Stat() = BaseStats + BonusStats`: the PoC can either mutate a per-dude bonus
array on equip/unequip (engine-faithful) or compute `Stat() + armorDelta` on the fly
(stateless, save-friendlier). Armor also swaps the dude's base art: `maleFid`/`femaleFid`
(critter FRM index) via `_adjust_fid()` (inventory.cc:2594-2605) — optional for the PoC
(leather jacket maleFid 0x0100000D, metal 0x0100000E; both base critter arts ship with
weapon-code sets in critter.dat).

### Empirical armor protos (parsed)
| | PID | AC | DT normal | DR normal | DT/DR laser | DT/DR fire | DT/DR plasma | DT/DR electr | DT/DR explo |
|---|---|---|---|---|---|---|---|---|---|
| Leather Jacket | 74 | 8 | 0 | 20% | 0/20 | 0/10 | 0/10 | 0/30 | 0/20 |
| Leather Armor | 1 | 15 | 2 | 25% | 0/20 | 0/20 | 0/10 | 0/30 | 0/20 |
| Metal Armor | 2 | 10 | 4 | 30% | 6/75 | 4/10 | 4/20 | 0/0 | 4/25 |

(EMP DR=500% on all — engine clamps; ignore for PoC normal-damage-only combat.)
Our `CombatMath.RollDamage` already applies DT-then-DR in the right order
(matches `attackComputeDamage`); armor only changes where the numbers come from.

---

## 5. Kill XP + progression

### Where kill XP comes from
- Source value: the critter proto's `experience` field — `critter.cc:920 critterGetExp()`
  returns `proto->critter.data.experience`. **We already parse this**
  (`ProtoDatabase.cs:151`, `CritterProtoStats.Experience`).
- Award path: on death inside `combat.cc:4674 _apply_damage()` — when `DAM_DEAD` set and the
  killer is the dude or dude's team, and the critter's script doesn't override:
  `_combat_exps += critterGetExp(a1); killsIncByType(critterGetKillType(a1))`
  (combat.cc:4870-4871). The accumulated XP is paid out **at combat end**:
  `combat.cc:2816 _combat_give_exps(_combat_exps)` → `combat.cc:2857 _combat_give_exps()`
  → `pcAddExperience()` (+ the "you earn %d exp." message, proto msg 621-626).
- Kill tally: `critter.cc:702 killsIncByType()` bumps `gKillsByType[killType]`
  (`critterGetKillType` critter.cc:745 reads the proto killType we also already parse).
- Scripts use externals `give_exp_points` (op 0x80b8 → pcAddExperience) — same sink.

### Level-up model — `stat.cc:731 pcAddExperienceWithOptions()`
- XP table is a closed formula, `stat.cc:662 pcGetExperienceForLevel(level)`:
  `v1 = level/2; odd level → 1000*v1*level; even → 1000*v1*(level-1)`
  (→ 1000, 3000, 6000, 10000, 15000 ... = 1000 * level*(level-1)/2).
- On level: `hpPerLevel = endurance/2 + 2` (stat.cc:771, +4/Lifegiver rank), added to
  **bonus** STAT_MAXIMUM_HIT_POINTS (stat.cc:775) and current HP adjusted by the same delta
  (stat.cc:777-778). Skill points/perks happen in the character editor — out of scope.
- **Yes, "kill XP + HP-only level-up" is coherent without a character sheet**: XP, level,
  and max-HP-bonus are three integers; the engine itself applies HP at level-up time and
  defers everything else to a UI we're not building. Display "Level N, XP x/y" in the
  existing message area.

### Dude base stats — gcd file
`critter.cc:1022 gcdLoad()` reads `premade\player.gcd` (via `proto.cc:907/1368
_proto_dude_init("premade\\player.gcd")`) with **the exact same record as the critter proto
stat block**: `protoCritterDataRead()` (critter.cc:1064) = flags, baseStats[35],
bonusStats[35], skills[18], bodyType, experience, killType[, damageType], followed by
name[32], skillsLoad (tagged skills), traitsLoad, remaining char points. After load it
zeroes experience/killType/bodyType and forces EMP DR=100 (critter.cc:1054-1057).
**Recommendation: parse it** — `premade\combat.gcd / stealth.gcd / diplomat.gcd / player.gcd`
ship in master.dat, the parser is ~10 lines on top of our existing
`protoCritterDataRead` port (same big-endian layout), and it instantly replaces the
synthetic "proto 0x01000001, 30 HP" dude with real SPECIAL stats that feed the unarmed/melee
skill formula, AC, sequence, and the END-driven HP-per-level. Synthesize only if we want a
fixed demo build; parsing is strictly cheaper than maintaining a hand-rolled stat block.

---

## 6. Corpse persistence (bug-fix-grade proposal)

### Engine behavior to mirror
- `critter.cc:818 critterKill()`: corpse FID = `buildFid(CRITTER, frmIdx, deathAnim+? , ...)`
  (the SF death frame), `flags |= OBJECT_NO_BLOCK` + flat toggle (critter.cc:882-885),
  `hp = 0; results |= DAM_DEAD` (895-896), and crucially
  **`scriptRemove(critter->sid); critter->sid = -1`** (critter.cc:897-900; duplicated in
  `_apply_damage`, combat.cc:4875-4878). The map_enter loop
  (`scripts.cc:2601 scriptsExecMapUpdateScripts`, sid collection at 2635-2647) iterates the
  registered script list only — a dead critter's script was removed, so its map_enter /
  critter_p_proc simply never exists on revisit. The original persists this because revisits
  load the mutated `.SAV` object dump (sid already −1, FID already corpse), not the pristine MAP.
- Dead critters as loot: the engine keeps the corpse object with its inventory; our viewer
  already treats `DAM_DEAD` critters as containers (ViewerGame.cs:1765).

### Recommended extension: `DeadOrdinals` replay (not per-ordinal Fid/Flags capture)
Add to `SaveState.MapDelta` (SaveState.cs:38):
```csharp
/// <summary>Pristine critters killed by the player, by load-order ordinal.</summary>
public List<int> DeadOrdinals { get; set; } = [];
```
Replaying the *conversion* beats capturing Fid/Flags/results per ordinal because
(a) `FinishCorpse`'s art choice is already deterministic (FALL_BACK if the FRM ships, else
FALL_FRONT — ViewerGame.cs:1532-1543), so storing the FID is redundant; (b) raw flag
snapshots would also freeze unrelated bits (TRANS_*, SEEN) and rot if corpse logic changes;
(c) it keeps the delta JSON-small and human-readable, consistent with the existing
door/taken/created records.

**Capture** (in `CaptureMapDelta`, ViewerGame.cs:2727): alongside the TakenOrdinals loop —
`if (present.Contains(obj) && obj.IsDead && Fid.PidType(obj.Pid) == critter) delta.DeadOrdinals.Add(ordinal)`.
Also widen the container-snapshot condition (ViewerGame.cs:2760) to
`obj.Inventory.Count > 0 || _stockedOrdinals.Contains(ordinal) || obj.IsDead` — today a
**fully looted corpse gets no inventory snapshot, so its pristine loot resurrects** even once
the corpse itself persists. An empty snapshot is exactly the existing "keeps looted ones
looted" mechanism; no interaction with TakenOrdinals at all (the corpse is never "taken" —
it stays in the world as a flat container, mirroring the engine).

**Apply order** (two halves, matching the engine's sid-removal semantics):
1. In `ApplyDeltaBeforeScripts` (ViewerGame.cs:2769), for each dead ordinal set
   `obj.Sid = -1` (and `CombatResults |= 0x80`, `CurrentHp = 0`). `LoadMap` builds its
   `scripted` set *after* this call (ViewerGame.cs:638-645, filter `o.Sid != -1`), so the
   dead critter's map_enter/critter scripts never run — the engine-faithful outcome of
   `scriptRemove()`. This must NOT be done after scripts: a den guard's map_enter could
   otherwise re-arm hostility or restock the corpse.
2. In `ApplyDeltaAfterScripts` (ViewerGame.cs:2788), run the existing corpse conversion on
   each dead ordinal: `FinishCorpse(obj, deathAnim)` with the same deterministic
   FALL_BACK→FALL_FRONT probe (factor the probe out of `KillCritter` so kill-time and
   replay share it). FinishCorpse already does corpse FID (+28), NO_BLOCK|flat, solid→flat
   list move, and `RebuildBlockedTiles` — inventory untouched, so the loot panel works.
   Container snapshots in the same method then overwrite the corpse inventory with the
   saved one (or empty). Note FinishCorpse uses `_elevation` — generalize to the corpse's
   elevation when replaying.
3. Save/load is free: `MapDelta` round-trips through the same JSON; the current map's delta
   is captured in `SaveGame` (ViewerGame.cs:2859) already.

Edge cases: script-created critters that die aren't pristine ordinals — currently
`Created` only stores pid/count, so they vanish on revisit either way (acceptable,
document it); AI/`AddJoiners` already gate on `IsDead`, which the replayed
`CombatResults` satisfies, so no resurrected aggro.

**Size: S** (~40-60 lines: 1 field, capture loop addition, two apply hooks, FinishCorpse
elevation/probe refactor). The only M-risk is if we also want wounded-but-alive HP to
persist — that's a separate `Dictionary<int,int> CritterHp` and can wait.

---

## 7. Effort table

| Item | Size | Notes / concrete risks |
|---|---|---|
| Equipped melee weapon (spear class) | **S-M** | Parse weapon block in ProtoDatabase (animationCode/min/max/dmgType/range1/AP1 — order from proto.cc:1585-1604); equip flag toggle in the inventory panel; CombatMath: melee-skill to-hit + `rand(min, max+meleeDmg)`; attacker FID gains weapon code (hmwarr G-set ships, GF thrust verified). Risks: melee-skill base formula must be ported from skill.cc (differs from unarmed); SavedItem needs an Equipped field or equips are lost on save/load; NPC equipped-weapon damage suddenly applies to the dude — balance check vs 30 HP dude (Den pistols would hit hard once NPCs also use weapon stats; can scope to dude-only first). |
| Armor (leather jacket/metal) | **S** | Armor block parse (AC + DR[7] + DT[7] — **DR before DT**, proto.cc:1557-1559); equip = WORN bit + bonus-stat delta per inventory.cc:2544 _adjust_ac; our DT/DR damage math is already correct. Risks: double-equipping (must clear old armor's delta — copy _adjust_ac's old/new pattern exactly); dude art swap via maleFid is optional but inconsistency (leather-armor stats on jumpsuit art) is visible; save must persist the worn flag or recompute deltas on load. |
| Ranged + ammo (10mm pistol) | **M-L** | Everything melee needs PLUS: stop Skip()ing weapon ammoQuantity/ammoTypePid in MapFile.cs:418 and carry them on MapObject + SavedItem; reload action (item.cc:1437/1553 semantics, 2 AP); distance/perception to-hit term (combat.cc:4331-4402) and at least a crude line-of-fire check; ammo decrement at anim end (combat.cc:5350); JHP/AP drMod/dmgMult if fidelity matters. Risks: save-format migration (existing saves lack ammo fields); hmwarr has no 'h' art — dude must move to hmjmps or pistol stays NPC-only; LoF check is new geometry code with corner cases. |
| Kill XP + HP-only level-up | **S** | Proto Experience already parsed; on KillCritter accumulate, award at combat end (combat.cc:2816 pattern); XP table = closed formula stat.cc:662; level-up adds END/2+2 to max-HP-bonus and heals the delta (stat.cc:771-778). Needs the dude's real END → parse premade\player.gcd via existing protoCritterDataRead port (~10 lines). Risks: persisting XP/level/hpBonus = 3 new SaveState fields (trivial but a format bump); script-killed critters (kill via scripts, not combat) won't award XP unless that path also hooks in. |
| Corpse persistence | **S** | `DeadOrdinals` list + sid=-1 before scripts + FinishCorpse replay after scripts + widen container-snapshot condition to dead critters (full plan in §6). Risks: apply-order regression if conversion runs before map_enter (scripts could re-target the corpse); FinishCorpse's `_elevation` assumption must be generalized; fully-looted-corpse loot resurrection unless the snapshot condition is widened in the same commit. |

Suggested order: corpse persistence (bug) → melee weapon → armor → kill XP/level-up → ranged.

## Unverified / flagged items
- SKILL_MELEE_WEAPONS base formula cited from memory of skill.cc's table — **verify the
  exact constants in skill.cc before porting** (unarmed's 30+2*(AG+ST) is already verified
  in our code).
- `_attack_subtype` comment in item.cc:126 says spear primary is "Swing"; the **parsed proto
  says primary idx 4 = Thrust** — trust the parsed data (and hmwarr ships GF thrust, not GG
  swing, which corroborates it).
- Whether map scripts ever resurrect critters (which would fight the DeadOrdinals replay)
  was not audited; no such external is wired in our VM, so it's moot for the PoC.
- EMP DR 500% clamping behavior not traced (irrelevant to normal-damage PoC combat).
