# P10 Track A — worldmap.txt table semantics + the random-encounter roll/pick chain

Scope: the **spine** of random worldmap encounters — every `worldmap.txt`
section's exact grammar (parsed from the real 4047-line file in the repo) and
the fallout2-ce roll/pick/condition chain (cited `src/worldmap.cc:LINE`). All
table values are quoted from the local `worldmap.txt`; all engine claims
cite `reference/fallout2-ce/src/<file>.cc:LINE`; the EC*.int external census was
run against the **real extracted scripts** (DatDump + the int_analyze
disassembler), not from memory. Where a detail could not be confirmed it is
flagged `UNVERIFIED`.

This note corrects two errors carried in the prior phase-7/phase-8 notes:
**(1)** each `[Tile N]` is a **7×6 = 42-subtile** grid, not 6×6 (p7-track-b said
"6×6 subtiles", p8 said "21 tiles, 6×6"); **(2)** `Script:N` in worldmap.txt is a
**1-based scripts.lst line number** (engine binds index `N-1`), verified against
the extracted scripts.lst.

---

## 1. The data file — section-by-section grammar (real values)

### `[Data]` — frequency name → percentage (worldmap.txt:32-39)
```
Forced=100%  Frequent=38%  Common=22%  Uncommon=12%  Rare=4%  None=0%
```
Engine loads these into `wmFreqValues[6]` (worldmap.cc:790) indexed by an
`ENCOUNTER_FREQUENCY_TYPE` enum; a subtile's daypart cell stores the **index**,
and the roll reads `wmFreqValues[index]`. So the six values above ARE the
percentages a subtile can carry. `terrain_types=Desert:1, Mountain:2, City:1,
Ocean:1` (line 44) is a per-terrain draw-pixel-skip count (cosmetic, skip).

### `[Random Maps: Desert|Mountain|City|Ocean]` (worldmap.txt:50-100)
Per-terrain fallback pools of map **lookup-names** (resolved to map indices via
maps.txt). Desert=12 (`Desert Encounter 1..12`), Mountain=12 (mixes
`Cavern Encounter 0..5` + `Mountain Encounter 1..6`), City=8, Ocean=12
(`Coast Encounter 1..12`). Used only when an entry has no `Map:` AND its table
has no `maps=` (worldmap.cc:3640-3648).

### `[Encounter: GROUP]` — critter-group composition (worldmap.txt:156-1034)
Parsed lazily on first reference by `wmReadEncBaseType` (worldmap.cc:1611).
Per-block keys:
- `type_NN=` — one critter spec. Keys (`wmParseEncBaseSubTypeStr`,
  worldmap.cc:1681; defaults :1745-1757):
  - `ratio:N%` → `ENCOUNTER_RATIO_MODE_USE_RATIO`; **omitted ratio = SINGLE**
    (exactly one spawn — the leader; e.g. `ARRO_Hunting_Party type_00` w/ the
    Sharp Spear has no ratio).
  - `Dead` prefix → `ENCOUNTER_SUBINFO_DEAD` (corpse dressing).
  - `pid:N` → proto id (`0`→-1).
  - `Item:[(min[-max])]PID[(wielded)|{wielded}|(worn)|{worn}]` — up to **10**
    (`wmParseEncounterItemType` :2005). `(wielded)` → equip hand; `(worn)` →
    armor; bare quantity range = ammo/caps (e.g. `Item:(3-6)320` = 3-6 ×
    7.62mm; `Item:(0-10)41` = 0-10 caps).
  - `Distance:N` / `TileNum:N` placement offsets.
  - `Script:N` → scripts.lst line N (binds index **N-1**, see §4).
  - trailing `If(...)` → same condition grammar as table entries (§ below),
    e.g. `If (Rand(5%))` optional members.
- `position=FORMATION[, Spacing:N][, Distance:N]` (`wmParseEncBaseType`
  :1666-1671; defaults :1729 = surrounding/spacing 1/distance -1).
  `wmFormationStrs` (:716-723) = `surrounding, straight_line, double_line,
  wedge, cone, huddle`.
- `team_num=` — appears **once** in the whole file, inside the commented
  `[Encounter: Raiders]` WIP block (line 174). For every REAL group the key is
  absent; `configGetInt` does not write on a missing key while the local
  `int team` is assigned unconditionally → engine reads uninitialized/leftover
  team. **Teams therefore come from the critter PROTO + its bound SCRIPT, not
  from worldmap.txt** (cross-checked: every spawned critter's hostility is
  script-driven — see §4).

Real early-game groups (verified):
- `ARRO_Rats` (272): `ratio:95% pid 16777227 Script:617` + `ratio:5% Pig Rats` +
  `pid:271 Xander Root If(Rand(5%))`; `position=huddle, spacing:3`.
- `ARRO_Sm_Scorpions` (289): `ratio:100% pid 16777221 Script:616` + two `Dead`
  primitives `If(Rand(10%))`; `huddle, spacing:4`.
- `ARRO_Silver_Geckos` (301): 90% pid 16777296 / 10% pid 16777297, `Script:615`.
- `ARRO_War_Party` (325): 30% / 20% hunters w/ Sharp Spear (pid 280, wielded),
  `Item:(3-6)320` ammo, `Item:(0-10)41` caps, `Script:618`; `wedge, spacing:2`.
- `ARRO_Hunting_Party` (318): SINGLE leader (no ratio) + 60%/40% hunters,
  Dead gecko; `Script:484`; `wedge, spacing:2`.
- `DEN_Slavers` (497): slavers w/ Desert Eagle (pid 18) / Springer (pid 9) +
  slaves; `Script:508`/`Script:628`; `wedge, spacing:2`.
- `Bounty_Hunter_Low` (982): 2 types, `Script:836`,
  `position=Surrounding, Spacing:2, distance:4` (the classic ring ambush).
- `Special1` (978): `type_00=ratio:0%, pid:19` — spawns **nothing**; the special
  MAP supplies the content. Every special table entry points its `Enc:` here.

### `[Encounter Table N]` — weighted entry tables (worldmap.txt:1036-…; 76 tables)
`wmReadEncounterType` (worldmap.cc:1367): reads `lookup_name=` (the subtile
pointer key), `maps=` (≤ **6** map lookup-names, :1390), then `enc_00..enc_NN`
(≤ **40** entries, the candidate array is `int candidates[41]`,
worldmap.cc:3568). Per-entry (`wmParseEncounterTableIndex` :1429; defaults
`wmEncounterTypeSlotInit` :1777 = chance 0 / counter -1 / map -1 /
scenery Normal):
- `Chance:N%` (:1437) — weight, NOT normalized; the pick is a roll over the SUM
  of candidate chances (§3).
- `Counter:N` (:1438) — one-shot budget; decremented on selection; entries with
  `counter == 0` are filtered out (worldmap.cc:3579-3581). Default -1 = unlimited.
- `Special` (:1440) — bare token → `ENCOUNTER_ENTRY_SPECIAL`; pairs with `Map:`,
  pins a "!" city circle at the party position on hit.
- `Map:LookupName` (:1456) — per-entry map override (specials always have it;
  non-specials may too, e.g. Den_D `enc_23/24` force `Desert Encounter 7` for the
  night Rave).
- `Enc:` (:1482, `wmParseEncounterSubEncStr`) — composition:
  `[(min-max)] GROUP_NAME [SITUATION]`, SITUATION ∈ `{Nothing, AMBUSH, FIGHTING,
  AND}` (enum :137-142). `Player` parses to sub-index -1
  (`wmParseFindSubEncTypeMatch` :1586). Grammar seen in the real Arroyo tables:
  - `Enc:(2-4) ARRO_War_Party AMBUSH Player` — one group, ambush.
  - `Enc:(2-4) ARRO_Spore_Plants AND (1-2) ARRO_Silver_Geckos FIGHTING Player`
    — two groups, both hostile to player.
  - `Enc:(2-4) ARRO_Hunting_Party FIGHTING (3-4) ARRO_Cannibals` — two groups
    fight EACH OTHER (player is a bystander).
  - `Enc:(3-5) ARRO_Nomads` — neutral group, no situation.
  - `Enc:Special1` — the no-spawn placeholder.
- `Scenery:None|Light|Normal|Heavy` (:1467) — debug-print only
  (worldmap.cc:3682), **no gameplay effect — skip**.
- `If(...) [And|Or If(...)]` — `wmParseConditional` (:2110): up to **3**
  sub-conditions + 2 logical links (`wmConditionalQualifierStrs` "and"/"or").

### `[Tile N]` — the world grid (worldmap.txt:3064-4040; 20 tiles)
Per-tile keys: `art_idx`, `encounter_difficulty` (default 0; real values seen:
0, -15..-70 — an Outdoorsman detection bonus, NOT the encounter rate),
`walk_mask_name`. Then a **42-line subtile grid**.

**CONFIRMED dimensions** (worldmap.cc:64-65, 372): `SUBTILE_GRID_WIDTH=7`,
`SUBTILE_GRID_HEIGHT=6`, `subtiles[HEIGHT][WIDTH] = subtiles[6][7]`. The parse
loop (worldmap.cc:1341-1356) keys `"%d_%d", row, column` with `row` in 0..6
(WIDTH) and `column` in 0..5 (HEIGHT), so each `[Tile N]` carries lines
`0_0 … 6_5` = **42 subtiles** (I counted: `awk` over Tile 0 = 42 lines, x∈0..6,
y∈0..5). The prior notes' "6×6" was wrong.

Subtile line format (`wmParseSubTileInfo` worldmap.cc:1943-1967), field order
EXACT:
```
x_y = Terrain , Fill , morningChance , afternoonChance , nightChance , TableLookup
```
- `Terrain` → terrain index (Desert/Mountain/City/Ocean) — feeds the fallback
  map pool.
- `Fill` → a `SUBTILE_FILL_*` enum (`No_Fill/Fill_W/…`, worldmap.cc:614-623) —
  art edge-blending only, **gameplay-irrelevant**.
- 3× daypart chance → a `wmFreqStrs` index (None/Rare/Uncommon/Common/
  Frequent/Forced); the roll picks `encounterChance[dayPart]`.
- `TableLookup` → resolves to the encounter-table index
  (`wmParseFindEncounterTypeMatch` :1970).

Real Tile 0 (the Arroyo home tile, worldmap.txt:3064-3110): the playable land
subtiles point at **`Arro_M`** (Mountain, table 6), **`Arro_D`** (Desert,
table 7), **`Arro_O`** (Ocean, table 8), with a few `Arrok_D/Arrok_M` and
`Kla_M/Kla_D` toward the SE corner (the road to Klamath). Example rows:
`2_4=Desert,No_Fill,Uncommon,Uncommon,Uncommon,Arro_D` (=12% all dayparts),
`3_1=Mountain,No_Fill,Rare,Rare,Rare,Arro_M` (=4%),
`6_3=Desert,No_Fill,Uncommon,Uncommon,Uncommon,Kla_D`.
Tile 1 (worldmap.txt:3114-3159) carries `Kla_M/Kla_D/Klad_D/Den_D/DMRV_D/Den_M`
— the Klamath→Den corridor — with `2_5=Desert,…,Frequent,Frequent,Frequent,
Den_D` (=38%) as a hot cell.

**The exact early-game sections to parse first:** Tiles 0 and 1; tables
**6 Arro_M, 7 Arro_D, 8 Arro_O, 9 Arrok_D, 10 Arrok_M, 11 Kla_D, 12 Kla_M,
13 Klad_D, 16 Den_D, 17 Den_M**; groups `ARRO_*`, `ARROK_*`, `KLA_*`, `KLAD_*`,
`DEN_*`, `DMRV_*`, `Bounty_Hunter_*`, `Morton_Brother`, `Special1`. These cover
the entire Arroyo→Klamath→Den loop.

---

## 2. The roll chain — `wmRndEncounterOccurred` (worldmap.cc:3322-3522)

The walking loop (`wmWorldMapFunc`, worldmap.cc:2974): while `isWalking`, each UI
frame runs 1× `wmPartyWalkingStep()` (car = 4-9 steps, :3026-3044), then
`wmGameTimeIncrement(18000)` = **18000 ticks = 30 game-minutes / step**
(:3103), then — only if the clock actually advanced — `wmRndEncounterOccurred()`
(:3110). The gate chain, IN ORDER:

1. **Real-time throttle** (:3325): `getTicksBetween(now, wmLastRndTime) < 1500`
   → no roll. (Wall-clock 1.5 s; for a deterministic port replace with a
   per-N-steps cadence.)
2. **The Δ3 quirk** (:3331-3337): `abs(oldWorldPosX - worldPosX) < 3` → return 0;
   **then** `abs(oldWorldPosY - worldPosY) < 3` → return 0. Two SEPARATE early
   returns ⇒ effectively requires **|Δx|≥3 AND |Δy|≥3** since the last
   *encounter*. `oldWorldPos*` is reset to the current pos only after an
   encounter fires (:3501-3502), so straight axis-aligned travel (Δy≈0) NEVER
   rolls — a real engine quirk. Skippable detail for a first cut, but keep it to
   match cadence.
3. **Known-area suppression** (:3340-3343): `wmMatchWorldPosToArea(pos) != -1`
   (standing on/near a city circle) → never roll.
4. **Horrigan** (:3345-3361): if `!didMeetFrankHorrigan && gameDay > 35` → forced
   Horrigan map. **SKIP** (day-35 endgame; our loop never reaches it).
5. **sfall forced encounter** (:3367-3388): debug/script hook. **SKIP.**
6. **Subtile lookup** (:3391, `wmPartyFindCurSubTile`).
7. **Daypart** (:3393-3401): `hour = gameTimeGetHour()` (0..2359);
   `hour>=1800 || hour<600 → NIGHT`, `hour>=1200 → AFTERNOON`, else `MORNING`.
8. **Frequency + difficulty skew** (:3403-3414):
   `frequency = wmFreqValues[subtile.encounterChance[dayPart]]`; if
   `0 < frequency < 100`, `modifier = frequency/15`, Easy `-= modifier` /
   Hard `+= modifier`. (Default Normal = no skew — skip for v1.)
9. **The roll** (:3416-3419): `chance = randomBetween(0,100)`;
   `if (chance >= frequency) return 0;` → otherwise an encounter occurred.
10. **Pick** (:3421): `wmRndEncounterPick()` (§3) sets table/entry/map ids.
11. **Special-circle pin** (:3425-3443): if the picked entry is `Special`, set a
    new known city circle at the party position. **SKIP for v1** (no special
    maps in the loop's content).
12. **Outdoorsman avoid** (:3454-3519): if `frequency > chance` (a "soft" roll),
    best-party Outdoorsman (+20 motion-sensor, cap 95, + tile
    `encounter_difficulty`) rolls vs `randomBetween(1,100)`; success →
    `displayMonitor` + a **Yes/No dialog** ("you detect something") whose **No
    avoids the encounter** (returns 0, grants `100 - outdoorsman` XP). **SKIP for
    v1** (always-encounter); the prompt explicitly lists outdoorsman-avoid as
    skippable. Note `gDayPartEncounterFrequencyModifiers = {40, 30, 0}`
    (worldmap.cc:570-574) only applies in-car (:3445-3452) — skip.

Return 1 → caller (:3110-3119): `wmFadeOut(); mapLoadById(encounterMapId);
break` — leave the worldmap loop and load the encounter map (which runs
map_enter then `wmSetupRandomEncounter`, §4).

---

## 3. The weighted pick — `wmRndEncounterPick` (worldmap.cc:3557-3654)

```
table = wmEncounterTableList[subtile.encounterType];          // :3564
for each entry:
    selected = wmEvalConditional(entry.condition, NULL) != 0  // :3575  (§ below)
            && entry.counter != 0;                            // :3579
    if selected: candidates += index; totalChance += entry.chance;  // :3583-3586
chance = randomBetween(0, totalChance) + (LUCK - 5);          // :3589-3590
  + Explorer(+2)/Ranger(+1)/Scout(+1) perks;                  // :3592-3602  (SKIP — no perks)
  + difficulty: Easy chance+=5 (cap totalChance), Hard -=5 (floor 0);  // :3604-3617 (SKIP v1)
walk candidates subtracting entry.chance until chance < entry.chance;  // :3620-3631
  (overflow → last candidate)
entryId = candidates[index]; if counter>0 counter--;          // :3633-3638
map = entry.map>=0 ? entry.map                                // :3640-3651
    : table.maps ? random(table.maps)
    : random(terrain.maps);
```

So the pick is a **uniform roll over summed Chance weights of the eligible
candidates**, with a `±(Luck-5)` shift (Luck 5 = no shift) and skippable
perk/difficulty nudges. v1 = `randomBetween(0, totalChance)` straight, walk-down.
The **map** is `entry.Map:` if present, else random from the table's `maps=`,
else random from the terrain pool.

### Condition eval — `wmEvalConditional` (worldmap.cc:4096-4152)
Per sub-condition `type`:
| `If(...)` syntax | type | eval (worldmap.cc) |
|---|---|---|
| `Global(n) op v` | GLOBAL | `gameGetGlobalVar(n) op v` (:4107) |
| `Player(Level) op v` | PLAYER | `pcGetStat(PC_STAT_LEVEL) op v` (:4124) |
| `Rand(n%)` | RANDOM | `randomBetween(0,100) > n → fail` ⇒ n% pass (:4117-4121) |
| `time_of_day op v` | TIME_OF_DAY | `gameTimeGetHour()/100 op v` (0-23) (:4135-4137) |
| `days_played op v` | DAYS_PLAYED | `gametime/TICKS_PER_DAY op v` (:4129-4131) |
| `enctr(num_critters) op v` | NUMBER_OF_CRITTERS | spawned-so-far count (:4112) — used inside type_NN only |

Operators (`wmEvalSubConditional` :4155-4168): **`== != < >` ONLY** (no `<=`/
`>=`). **Quirk** (:4143-4148): the function returns the LAST sub-condition's
`matches`; it only `break`s early when an `And`-linked condition fails. With the
real data (all early tables use `If(Global(g) op v) And If(Player(Level) op v)`
pairs) this behaves as a correct AND because the second condition is the one
returned and the And-break short-circuits the first; **for an `Or` link the
engine is buggy (returns only the last)** — but NO early table uses `Or`, so a
faithful AND-only evaluator is exact for the Arroyo→Den loop. Real conditions
seen: only `Global(0)` (karma), `Global(1)` (childkiller rep), `Global(386)`
(Morton gang), `Global(605..620)` (special-seen flags), `Player(Level)`,
`time_of_day`, `Rand`.

---

## 4. From pick to spawn — what the engine does after the map loads

`mapLoadById` → map.cc:974 runs `SCRIPT_PROC_MAP_ENTER` on the map script, THEN
map.cc:978 calls `wmSetupRandomEncounter()` (worldmap.cc:3657). The encounter
maps DO have a trivial map script (LocalVariablesCount=0, no pre-placed scripted
objects — verified in p8), so map_enter is vacuous but must still run.

`wmSetupRandomEncounter` → `wmSetupCritterObjs` (worldmap.cc:3772) per sub-entry:
- group size `randomBetween(min,max)`; Easy −2 / Hard +2; party>2 → +2 (:3693-3710).
- per type_NN: count = `ratio*group/100` (USE_RATIO) or exactly 1 (SINGLE),
  clamp ≥1; skipped if `pid==-1` or its `If()` fails (`enctr` operand = group
  count, :3795).
- `objectCreateWithPid(pid)` (:3827) — proto defaults incl. base team.
- **script bind** (:3842-3848): remove proto sid, then
  `_obj_new_sid_inst(object, SCRIPT_TYPE_CRITTER, encounterEntry->scriptIdx - 1)`
  ⇒ **`Script:N` is 1-based scripts.lst; engine binds index N-1.** Verified:
  `Script:484` ↔ scripts.lst line 484 = `ECHunter.int`; `Script:617` ↔ line 617
  = `ECRat.int`. (Hexwaste convention: scripts.lst is 0-based, so our
  `CreateObject(pid, tile, elev, scriptIndex = N-1)` + `AllocateSid` is the
  identical path used for created objects / party members.)
- placement: non-Surrounding → `objectSetLocation(tile)`; Surrounding →
  `_obj_attempt_placement` (:3851-3854); then face dude
  (`tileGetRotationTo(tile, dude->tile)`, :3857). Surrounding ring distance =
  entry `Distance:` if set else `Perception + rand(-2..2)` (:3989-4005). Other
  formations anchor on a `random_start_point_N` from maps.txt
  (`map->startPoints`, parsed worldmap.cc:2724-2748).
- items (:3860-3905): roll qty, `itemAdd`, `(wielded)→_inven_wield(HAND_RIGHT)`,
  `(worn)→armor`.

### Who is hostile — confirmed script-side, not table-side
The parsed AMBUSH/FIGHTING `situation` is **never consumed** after parsing for
AMBUSH-Player (the auto-combat block at :3719-3760 only fires for the 2nd group
of an `X FIGHTING Y` pair). For `... AMBUSH Player`, hostility comes 100% from
the bound EC script's `critter_p_proc`. Verified by disassembly:
- **ECRat** (`Script:617`) `critter_p_proc` (0x988): `if obj_can_see_obj(self,
  dude) → attack(dude, …)`. `map_enter` sets team via
  `critter_add_trait(self,1,6,…)`.
- **ECScorp** (`Script:616`) `map_enter` (0xae2): `critter_add_trait(self,1,6,
  123)` = team 123. `combat_p_proc` adds `poison` on a failed `do_check`.
- **ECBHuntr** (`Script:836`) `map_enter` (0x12b8): `critter_add_trait(self,1,6,
  195)` team + `critter_add_trait(self,1,5,124)` AI-packet + `set_local_var(5,2)`
  arms aggro; `critter_p_proc`: `if LVAR5==2 && obj_can_see_obj(self,dude) →
  set LVAR5=1; attack(dude)`.

All three fire `attack(dude)`, which Hexwaste's `AttackComplex` (IntVm.cs:1000,
0x80D0) already implements. **The existing critter_p_proc heartbeat makes every
AMBUSH encounter hostile for free.** Group-vs-group `FIGHTING` autostart is the
only thing scripts don't cover — treat the two groups as different-team neutrals
to the player for v1 (their scripts will engage each other if teams differ).

### EC*.int missing-external census (the gate before any spawn work)
Ran int_analyze over all 13 early-loop EC scripts (`/tmp/p10ec/*.int`). Every
external opcode they emit, cross-referenced against `IntVm.ExecuteExternal`:

**REAL (already implemented):** `random` 0x80B4, `self_obj` 0x80BC,
`source_obj` 0x80BD, `target_obj` 0x80BE, `dude_obj` 0x80BF, `get/set_local_var`
0x80C1/C2, `get/set_map_var` 0x80C3/C4, `get/set_global_var` 0x80C5/C6,
`animate_move_obj_to_tile` 0x80CE, `attack` 0x80D0, `tile_distance_objs` 0x80D3,
`tile_num` 0x80D4, `tile_num_in_direction` 0x80D5, `add_obj_to_inven` 0x80D8,
`obj_can_see_obj` 0x80DC, `anim_busy` 0x80E7, `add_timer_event` 0x80F0,
`game_ticks` 0x80F2, `has_trait` 0x80F3, `fixed_param` 0x80F7, `obj_pid` 0x8100,
`cur_map_index` 0x8101, `critter_add_trait` 0x8102, `message_str` 0x8105,
`float_msg` 0x810A, `metarule` 0x810B, `add_mult_objs_to_inven` 0x8116,
`party_member_obj` 0x814B, `do_check` 0x80AE, `success` 0x80AF, `create_object`
0x80B7, `display_msg` 0x80B8, `script_overrides` 0x80B9.

**STUBBED (fall to the ExternalArity arity-pop default, stack stays balanced):**
| opcode | name | users | impact if left stubbed |
|---|---|---|---|
| 0x810C | `anim` | ECGecko, ECRat, ECScorp | idle fidget cosmetic — no aggro effect |
| 0x810E | `reg_anim_func` | ECGecko, ECRat, ECScorp | stop/animate-sequence helper for the idle wander — cosmetic |
| 0x8151 | `critter_is_fleeing` | ECBHuntr, ECHlyPpl, ECNomad, ECOutCst, ECSlaver | returns 0 (never fleeing) → a flee taunt float-msg is skipped; **actual min-hp flee is AI-packet-driven (data\ai.txt), which the phase-9 CombatEngine already handles** |
| 0x8122 | `poison` | ECScorp | scorpion sting applies no poison status — minor combat depth, not blocking |
| 0x8154 | `debug_msg` | 6 scripts | debug print — no-op, correct |

**Verdict: ZERO new externals are required to make the Arroyo→Klamath→Den
encounter loop spawn-and-fight.** All 5 stubs are cosmetic (fidget/taunt/debug)
or AI-packet-redundant (flee) or minor depth (poison). Every aggro path
(`obj_can_see_obj → attack`), team assignment (`critter_add_trait`), and item
equip (`add_obj_to_inven`/`add_mult_objs_to_inven` + the wielded flag) runs on
real host code today. (Optional later polish: implement `poison` for scorpion
depth — it pops 2 args, no return per ExternalArity.cs:148.)

---

## 5. What v1 can SKIP without breaking the canonical loop

Per the roll/pick chain above, all of these are confirmed-skippable:
- **Horrigan** (worldmap.cc:3345) — day-35 endgame; loop never reaches it.
- **The car** (`isInCar` branches throughout) — no vehicle in the slice.
- **Outdoorsman-avoid** dialog (worldmap.cc:3454-3519) — always-encounter for
  v1; the Yes/No avoid is a phase-2 add (we have the dialog UI).
- **Luck/Explorer/Ranger/Scout/Pathfinder perks** (pick :3589-3602, time
  :4179) — no perk system; Luck 5 = no shift, so `randomBetween(0,totalChance)`
  straight is exact-enough.
- **Difficulty skew** (occur :3404-3414, pick :3604-3617) — Normal = no skew.
- **Special-encounter circle pinning** (:3425-3443) + the `Special`/`Counter`
  one-shots — no special maps in the loop content; `Special1` spawns nothing.
  (Keep the `Counter` filter so a counter:0 entry is skipped — 2 lines — but the
  early tables only use Counter on specials we're not spawning.)
- **Scenery / Fill** fields — cosmetic/no-op.
- **The Δ3 real-time throttle wall-clock** — replace with a per-N-steps cadence
  for determinism under `--rng-seed`; KEEP the |Δ|≥3 movement gate and
  known-area suppression (we know our city-circle positions).

Required minimum (the spine): `[Data]` freqs → `[Tile]` 7×6 subtile grid (terrain
+ 3 daypart freq indices + table lookup) → `[Encounter Table]` (Chance, Counter,
Map override, Enc spec, If-conditions limited to Global/Player(Level)/Rand/
time_of_day — covers every early entry) → `[Encounter: GROUP]` (ratio-or-SINGLE,
Dead, pid, Items+wielded, Script:N→index N-1, formation). Roll loop = per step
(+30 game-min): daypart → `wmFreqValues[subtile freq]` → `randomBetween(0,100) <
freq` → `wmRndEncounterPick` (candidate filter on condition+counter, summed-
Chance weighted walk) → map (entry Map / table maps / terrain pool) → load
transient → spawn groups bound by Script:N-1 → existing critter_p_proc makes them
hostile.

---

## 6. Integration notes for Hexwaste (grounding for M-plan)

- **WorldmapScreen geometry already matches the engine:** `WorldmapScreen.cs`
  uses a 4-wide × 5-tall grid of 350×300 tiles (WorldWidth 1400, WorldHeight
  1500) = engine `WM_TILE_WIDTH=350`/`WM_TILE_HEIGHT=300`/`wmNumHorizontalTiles=4`
  exactly. The subtile lookup ports verbatim
  (`wmFindCurSubTileFromPos` worldmap.cc:3533-3543):
  `tileIndex = y/300*4 + x/350%4; column = (y%300)/50; row = (x%350)/50;
  subtiles[column][row]`.
- **MapList must learn two fields:** `src/Hexwaste.Formats/Map/MapList.cs` (73
  lines) today parses only `lookup_name`/`map_name`. Add `saved` (Yes/No) and
  `random_start_point_N` (elev+tile) — verified present on every encounter map
  (desert1: `saved=No`, 5 start points at tiles 19086/17302/21315/22699/20526).
- **No WorldmapDef/EncounterTable parser exists yet** — Track A is the new
  `WorldmapFile.cs` pure parser + the roll/pick/condition port. The
  `EncounterTableEntry`/`EncounterGroup` model from p8 (Q5) stands; the only
  correction is the subtile grid is **7×6 (42 cells)**, indexed
  `subtiles[column 0..5][row 0..6]`.
- **Transient-map persistence** (the milestone-gating risk) is Track B's
  question per the phase-10 prompt; this note confirms the engine half:
  `saved=No` ⇒ map.cc skips the .SAV write (1456) and destroys any stale .SAV on
  load (1074), `wmMapMarkVisited` returns early (2866), `_is_map_idx_same` treats
  it as never-same — i.e. regenerate pristine every visit, no delta slot.

---

## UNVERIFIED / honest flags
- **The `wmEvalConditional` Or-link bug** (returns only the last sub-condition's
  match) is real in fallout2-ce source but harmless here because NO early table
  uses `Or` — all use `And`. If a later region's table uses `Or`, a "correct" AND/
  OR evaluator would DIVERGE from the engine; the early loop is unaffected.
  UNVERIFIED whether any shipping table anywhere uses `Or` (not checked beyond
  the early loop).
- **`team_num` engine UB**: I confirmed `team_num=` appears only in the commented
  WIP block (worldmap.txt:174) and that the engine reads uninitialized team on a
  missing key; I did NOT trace what stack-leftover value results at runtime.
  Conclusion (teams come from proto+script) is sound because the EC scripts all
  set team explicitly via `critter_add_trait` in map_enter — but the precise
  "garbage team" value the engine would assign is UNVERIFIED (and irrelevant,
  since the script overwrites it).
- **The 1500 ms real-time throttle** is wall-clock; a deterministic per-step
  cadence is a documented divergence, not a port — UNVERIFIED what step count
  best matches the original feel (the engine's is FPS-dependent anyway).
- **Per-table `Counter` persistence across saves** (worldmap.cc:1117-1131, the
  save side) was NOT re-read here (it's Track F/save-format territory); this note
  only confirms the in-memory decrement (worldmap.cc:3636-3638) and the
  counter==0 filter (:3579).
