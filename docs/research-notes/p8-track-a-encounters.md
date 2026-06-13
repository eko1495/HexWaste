# Phase 8 Track A — Random Encounters (implementation-grade) + save format

## Q1. `saved=No` verified

### maps.txt census
`data\maps.txt` has **57 maps with `saved=No`** (out of ~150): all desert*/mountn*/city*/cave*/coast* encounter maps plus special-encounter maps (bhrnddst, rndtinwd, rnduvilg, rndholy1, rndforvr, rndtoxic, rndparih, rndexcow, …). Every one also carries `dead_bodies_age=No` and `can_rest_here=No,No,No`.

### Engine semantics (fallout2-ce)
- Parsing: `wmMapInit` reads `saved=` Yes/No into `MAP_SAVED` flag — worldmap.cc:2665-2671; `random_start_point_0..N` parsed at worldmap.cc:2724-2748.
- Accessors: `wmMapIdxIsSaveable` worldmap.cc:2822-2825, `wmMapIsSaveable` (current map) worldmap.cc:2828-2831.
- **Skip .SAV write**: on map exit, `map.cc:1456` — `if (a1 && !wmMapIsSaveable()) { "Not saving RANDOM encounter map."; _MapDirEraseFile_("MAPS\\", name.SAV); }` — it not only skips the save, it DELETES any stale .SAV.
- Belt-and-braces on load too: `map.cc:1074` after `mapLoadByName` — `if (!wmMapIsSaveable()) { "Destroying RANDOM encounter map."; erase .SAV; }` so a revisit always loads the pristine .MAP.
- `wmMapMarkVisited` worldmap.cc:2866 returns early for non-saved maps (no green circle), and `wmMapMarkMapEntranceState` worldmap.cc:2947 returns -1 — encounter maps never become known areas/entrances.
- `_is_map_idx_same` map.cc:529-535 treats non-saveable maps as never-same (so re-entering "the same" encounter map is always a fresh load).

### Second/third encounter maps parsed (probe: /tmp/p8a-probe, Hexwaste.Formats MapFile)
| map | objects(e0) | critters | scripted objs | header ScriptIndex | exit grids |
|---|---|---|---|---|---|
| mountn1 | 323 | 0 | 0 | 315 | 38× pid 0x5000015, dest map=-2, ring at hex x59-121/y81-100 |
| city1 | 1548 | 0 | 0 | 313 | 434× pid 0x5000010, dest map=-2, perimeter x33-173/y31-165 |
| desert1 | 921 | 0 | 0 | 313 | 56× dest map=-1 + 374× dest map=-2, perimeter |

maps.txt entries confirm structure generalizes: `mountn1` has 3 random_start_points, `city1` has 4, `cave0` has 3 (all `elev:0, tile_num:NNNNN`).
- Exit-grid destination semantics (`mapHandleTransition`, map.cc:1232-1252): **dest map == -1 → wmTownMap() (town map screen); dest map == -2 → wmWorldMap()** (return to worldmap at preserved position). Encounter map borders are ringed with map=-2 grids ⇒ "walk off edge → back to worldmap" is literally exit grids with map -2. (desert1's 56 map=-1 grids are likely editor leftovers; -2 dominates.)
- All three maps DO have a map script (ScriptIndex 313/315 ⇒ scripts.lst index 312/314) and **map_enter runs normally** — map.cc:974 executes SCRIPT_PROC_MAP_ENTER on the map script, and only **after** that calls `wmSetupRandomEncounter()` (map.cc:978). LocalVariablesCount=0 and zero pre-placed scripted objects, so LVAR import is vacuous on these maps.

### Verdict for Hexwaste
"Skip VisitedMaps for saved=No" is **necessary and sufficient for persistence**: no delta slot, no LVAR-slice export, no container snapshots, no moved/dead ordinals — regenerate pristine every entry (matches engine erase-the-.SAV behavior). But three extra behaviors ARE needed beyond skipping the delta:
1. **map_enter must still run** (engine does; encounter maps have map scripts — cheap since LVARs are 0).
2. **Don't mark the area visited / no town circle** (we have no circle for these anyway since they're not in city.txt — free).
3. Treat the map as transient in SaveState: if the player SAVES while standing on an encounter map, engine saves a .SAV for the current map regardless?? — No: engine save-game writes the current map via `_map_save_in_game(false)` path which does save the current map state into the slot (a1=false branch, map.cc:1456 takes the else). So mid-encounter saves keep the current map; our equivalent: serialize the *current runtime* map state (or simpler: forbid/ignore — see Q5).

## Q2. Encounter composition, exactly (Arroyo + Den decoded; parser citations)

### Table entry syntax (`[Encounter Table N]` → `enc_NN=`)
Parsed by `wmReadEncounterType` (worldmap.cc:1367, reads `maps=` via `wmParseFindMapIdxMatch`, max **6 maps** per table, max **40 entries**) and `wmParseEncounterTableIndex` (worldmap.cc:1429). Per-entry keys (defaults from `wmEncounterTypeSlotInit` worldmap.cc:1777: chance=0, counter=-1, map=-1, scenery=Normal):
- `Chance:N%` — `strParseIntWithKey "chance"` (worldmap.cc:1437). NOT normalized; pick is roll `0..totalChance` walking down the list (see Q3/prior notes).
- `Counter:N` — one-shot budget (worldmap.cc:1438); decremented on selection; entries with counter==0 are skipped.
- `Special` — flag `ENCOUNTER_ENTRY_SPECIAL` (worldmap.cc:1440-1454): special = the blinking "!" icon + pin a location, paired with explicit `Map:`.
- `Map:LookupName` — per-entry map override (worldmap.cc:1456-1462); non-special entries may use it too (e.g. Den_D enc_23/24 force "Desert Encounter 7" for the Rave Party). Fallback when map==-1: random from table `maps=`; if table has no maps, random from the terrain-type pool (wmRndEncounterOccurred → wmSetupRandomEncounter map pick, worldmap.cc:3450-3489).
- `Enc:` — composition spec, `wmParseEncounterSubEncStr` (worldmap.cc:1482): sequence of sub-entries `[(min-max)] TEAM_NAME [SITUATION]` where SITUATION ∈ `wmEncOpStrs` = {Nothing, AMBUSH, FIGHTING, AND} (enum worldmap.cc:137-142). `Player` resolves to index -1 (`wmParseFindSubEncTypeMatch` worldmap.cc:1586-1602). Grammar examples decoded:
  - `Enc:(2-4) ARRO_War_Party AMBUSH Player` — 2-4 critters from the War_Party block, ambushing the player.
  - `Enc:(2-4) ARRO_Spore_Plants AND (1-2) ARRO_Silver_Geckos FIGHTING Player` — two groups, both hostile to player.
  - `Enc:(2-4) ARRO_Hunting_Party FIGHTING (3-4) ARRO_Cannibals` — two groups fighting EACH OTHER (player is bystander).
  - `Enc:(3-5) ARRO_Nomads` — neutral group, no combat.
  - `Enc:Special1` — placeholder ([Encounter: Special1] = `type_00=ratio:0%, pid:19`, spawns nothing; the special MAP supplies content).
- `Scenery:None|Light|Normal|Heavy` — parsed worldmap.cc:1467-1473 (enum :129-135); engine only debug-prints it in wmSetupRandomEncounter (worldmap.cc:3682) — **no gameplay effect, skip**.
- `If(...) And If(...)` — `wmParseConditional` (worldmap.cc:2110), up to **3** sub-conditions + 2 logical operators ("and"/"or", `wmConditionalQualifierStrs` worldmap.cc:707).

### Conditions (`wmParseSubConditional`, worldmap.cc:2150-2330; eval `wmEvalConditional` worldmap.cc:4096-4152)
| syntax | type | eval |
|---|---|---|
| `Global(n) <op> v` | GLOBAL (:2208) | `gameGetGlobalVar(n) op v` |
| `Player(Level) <op> v` | PLAYER (:2237) | PC level op v |
| `Rand(n%)` | RANDOM (:2185) | `randomBetween(0,100) > n` → fail (i.e. n% pass chance) |
| `days_played <op> v` | DAYS_PLAYED (:2260) | gametime/ticks-per-day op v |
| `time_of_day <op> v` | TIME_OF_DAY (:2283) | `gameTimeGetHour()/100` op v (0-23; `If(time_of_day > 19)` = night raves) |
| `enctr(num_critters) <op> v` | NUMBER_OF_CRITTERS (:2306) | count spawned so far (used inside type_NN to cap groups) |
Operators `wmConditionalOpStrs` worldmap.cc:701: `_`(none) `==` `!=` `<` `>` (no <=/>=). Real data in Arroyo/Den tables uses ONLY Global, Player(Level), Rand, time_of_day.

### Critter blocks (`[Encounter: NAME]` → `type_NN=`)
Lazy-parsed on first reference by `wmReadEncBaseType` (worldmap.cc:1611): reads `type_00..type_NN`, then `team_num=` (one int applied to every critter pid in the block — worldmap.cc:1654-1663; appears literally ONCE in worldmap.txt at line 174 inside the commented example block, so **every real block gets team = whatever the uninitialized config lookup leaves — configGetInt fails and `team` keeps its previous stack value... in practice CE keeps default -1** since type slot init sets team=-1 and the loop only overwrites when key exists... NOTE: the loop overwrites unconditionally with local `team`, which is UNINITIALIZED if key missing — a real CE/original quirk; observed behavior: teams come out 0/garbage-stable; safe for us to default team to a fixed per-group id, see Q3), then `position=FORMATION[, spacing:N][, distance:N]` (worldmap.cc:1666-1671; defaults from `wmEncBaseTypeSlotInit` worldmap.cc:1729: position=surrounding, spacing=1, distance=-1 → computed from Perception at spawn).
Per type_NN line, `wmParseEncBaseSubTypeStr` (worldmap.cc:1681, defaults :1745-1757):
- `ratio:N%` — sets ratioMode=USE_RATIO; **no ratio key = ratioMode SINGLE** ⇒ exactly ONE spawn of this type (leaders! e.g. ARRO_Hunting_Party type_00 leader w/ Sharp Spear has no ratio).
- `Dead,` prefix — flag ENCOUNTER_SUBINFO_DEAD (corpse dressing, e.g. dead primitive w/ loot at 10% Rand).
- `pid:N` — proto id (0 → -1).
- `distance:N` / `tilenum:N` — placement offsets.
- up to **10** `Item:` specs — `wmParseEncounterItemType`/`wmParseItemType` (worldmap.cc:2005/2046): syntax `Item:[(min[-max])]PID[(wielded)|{wielded}|(worn)|{worn}]`, e.g. `Item:280(wielded)`, `Item:(3-6)320` (ammo!), `Item:(0-10)41` (money). Quantity default 1; wielded → equip in hand slot, worn → armor.
- `Script:N` — scripts.lst index to bind (e.g. 616 radscorpion, 836 bounty hunter, 628 slave).
- trailing `If(...)` — same conditional grammar; e.g. type-level `If(Rand(15%))` optional members, `If(Player(Level) > 6)` gun thugs only for higher-level players.

### Formations
`wmFormationStrs` (worldmap.cc:716-723): `surrounding, straight_line, double_line, wedge, cone, huddle` — enum worldmap.cc:109-117. Data uses: huddle (geckos/scorpion herds), wedge (armed parties), Surrounding (plants/slaves/bounty hunters — bounty hunters: `position=Surrounding, Spacing:2, distance:4` = ring AROUND the player at 4 hexes, the classic ambush ring).

### Two tables end-to-end (verified content)
- **Table 6 Arro_M** (worldmap.txt:1093): maps = Mountain Encounter 1/2/4/5 ("No Caverns here"); 27 entries: war party/cannibals/geckos/scorpions/plants/pig rats/rats ambushes (9-15%), 8 bounty-hunter tiers gated on `Global(1)>1` (childkiller rep) or `Global(0)<-500` (negative karma) × level bands (<7, 7-12, 13-18, >18), Morton brothers gated `Global(386) in 1..5`, two FIGHTING-each-other spectacles at 2%, and 2 one-shot specials (`Counter:1, Special, Map:Special Bridge Encounter`, level>9 + `Global(605)<1`).
- **Table 16 Den_D** (worldmap.txt:1436): maps = Desert Encounter 8/9/10/7; slavers/highwaymen/robbers/golden geckos/molerats; `enc_23/24` night-only Rave Party pinned to Desert Encounter 7 via `Map:` + `If(time_of_day > 19)`; same bounty-hunter/Morton/special skeleton. Note `Chance:0%` entries exist (disabled content) — a 0-chance entry can never be picked (weighted walk).

## Q3. Spawn placement, teams, hostility

### Group sizing (`wmSetupRandomEncounter`, worldmap.cc:3657)
Per sub-entry: `critterCount = randomBetween(min,max)` (worldmap.cc:3693); difficulty: Easy −2 (clamped to min), Hard +2 (:3695-3705); party size > 2 ⇒ +2 more (:3707-3710). Then `wmSetupCritterObjs(encounterIndex, &firstCritter, count)` spawns the group.

### Per-critter spawn (`wmSetupCritterObjs`, worldmap.cc:3772)
For each type_NN (skipped if pid==-1 or its If() fails — `wmEvalConditional` gets the GROUP count as the `enctr(num_critters)` operand, worldmap.cc:3795):
- count for this type: `ratio*groupCount/100` if `ratio:` present, else **exactly 1** (`ENCOUNTER_RATIO_MODE_SINGLE`, :3801-3812); min clamp 1.
- `objectCreateWithPid` (:3827) — proto defaults incl. base team/AI from .pro.
- **team**: only overridden `if (encounterEntry->team != -1)` (:3841-3845). team comes from `team_num=` (worldmap.cc:1654-1663) — but real worldmap.txt has team_num ONLY inside a commented example block (line 174), and `configGetInt` does NOT write on missing key (config.cc:196-198) while the local `int team` is uninitialized and assigned unconditionally — engine UB; in practice the table-driven team is garbage/leftover. **Conclusion: teams effectively come from the critter's PROTO + its SCRIPT, not from worldmap.txt.**
- **script binding** (:3848-3855): if `Script:N` present, remove proto-default sid and `_obj_new_sid_inst(object, SCRIPT_TYPE_CRITTER, scriptIdx - 1)` — i.e. **worldmap.txt Script numbers are 1-based scripts.lst lines; engine binds index N-1** (matches our scripts.lst-is-0-based convention; Script:618 = ECWarPty.int at lst line 618).
- placement: non-surrounding formations use `objectSetLocation(object, tile, gElevation)` directly; SURROUNDING uses `_obj_attempt_placement(object, tile, 0, 0)` (:3857-3861). Then face the dude: `tileGetRotationTo(tile, gDude->tile)` (:3863-3864).
- items (:3866-3905): roll quantity (min-max), `itemAdd`, `_obj_disconnect`; money pid doubled with Fortune Finder perk; `(wielded)/(worn)` → `_inven_wield(object, item, HAND_RIGHT)`.

### Formation math (`wmSetupRndNextTileNumInit` :3911 / `wmSetupRndNextTileNum` :3973)
- **surrounding**: center = `gDude->tile`, random initial direction; per spawn: distance = entry `Distance:` if set, else `Perception + rand(-2..2)` (+3 with Cautious Nature perk) (:3989-4005); origin = tile at that distance in a rotating direction (cycles all 6), then jitter `rand(0,dist/2)` hexes in a random direction. ⇒ a loose RING AROUND THE PLAYER.
- **straight_line/double_line/wedge/cone/huddle**: anchor = a random `random_start_point_N` from maps.txt (`map->startPoints`, :3944-3953); if none, dude's tile. Two alternating "arms" (wmRndIndex flips 0/1) grow from the anchor by `spacing` hexes per spawn, oriented relative to `tileGetRotationTo(anchor, dudeTile)`; huddle spirals around one center. First critter sits ON the anchor.
- placement validity `wmEvalTileNumForPlacement` (:4078): tile must be unblocked (`_obj_blocking_at`) AND reachable from the dude (`pathfinderFindPath` with `_obj_shoot_blocking_at`); 25 retries / 25-hex drift cap, else that critter is skipped.

### Who fights whom — the real answer
The parsed AMBUSH/FIGHTING/AND `situation` field is **never read** after parsing (only writes at worldmap.cc:1562, init :1768 — no consumer). Auto-combat logic lives in `wmSetupRandomEncounter` (:3719-3760) and triggers only for `index > 0` (2nd+ sub-group) when `prevCritter != critter`:
- `subEntiesLength == 2` and both are REAL groups (`X FIGHTING Y`): set `whoHitMe` on both leaders, `_caiSetupTeamCombat(critter, prevCritter)`, `_scripts_request_combat_locked` ⇒ **combat auto-starts between the two NPC groups; the player is a bystander**.
- `X AMBUSH Player`: "Player" parses as a sub-entry with encounterIndex −1; `wmSetupCritterObjs(-1,…)` returns without writing the out-param (worldmap.cc:3774-3776), so `critter` keeps the previous iteration's stack value (== prevCritter) and the combat block is skipped — engine quirk/UB. **Net effect: ambushes do NOT reliably auto-start combat from this code; hostility comes from the spawned critters' SCRIPTS.** Verified: ECWarPty.int (Script:618) disassembly = `critter_add_trait` (team/AI) in start + `obj_can_see_obj` → `attack(dude)` in critter_p_proc + timed fidget events; ECBHuntr/ECScorp/ECGecko follow the same pattern.
- The "else ⇒ attack gDude" branch (:3742-3757) requires `index>0 && subEntiesLength==1` — unreachable dead code.

### Judgment for Hexwaste
Our existing scripted-aggro heartbeat (critter_p_proc → attack) **is exactly how the original makes ambush encounters hostile** — geckos, scorpions, war parties, bounty hunters all come for free once we bind Script:N at spawn. The only thing scripts do NOT cover is `X FIGHTING Y` group-vs-group autostart: replicate by setting whoHitMe/teams between the two groups' leaders and starting combat — or accept that the two groups' scripts will engage each other if teams differ. Skippable for v1: treat FIGHTING-each-other entries as two neutral-to-player groups with different teams.

## Q4. Worldmap flow (roll → interrupt → return → resume)

### The movement tick (`wmWorldMapFunc`, worldmap.cc:2974)
Frame loop: if `isWalking` → 1× `wmPartyWalkingStep()` (worldmap.cc:3026; car = 4-9 steps, :3028-3045). A step advances Bresenham state toward (walkDestinationX/Y) — `wmPartyInitWalking` worldmap.cc:4266 sets up the line; `wmPartyWalkingStep` :4312 divides by terrain difficulty (`_terrainCounter`/4-cycle — Mountain terrain costs more wall ticks per pixel). Each frame: `wmGameTimeIncrement(18000)` = 30 game-minutes (worldmap.cc:3104). Arrival: `walkDistance <= 0` → isWalking=false + match area (:3096-3099). Clicking empty map = `wmPartyInitWalking(x,y)` (:3181); clicking a known circle while stopped = enter town (wmTownMapFunc → mapLoadById).

### The roll (worldmap.cc:3107-3122)
Inside the loop, ONLY while `isWalking`: `wmRndEncounterOccurred()` (worldmap.cc:3322). Gates in order: <1500 ms real since last check (:3325); |Δx|<3 OR |Δy|<3 from last-encounter pos (:3330-3336, AND-quirk); on a known area circle → never (:3339-3342); Horrigan day>35 forced (:3345); sfall forced-encounter hook (:3366); then daypart (night ≥1800/<600, afternoon ≥1200, else morning :3392-3399), `frequency = wmFreqValues[subtile.encounterChance[dayPart]]` ±freq/15 by difficulty (:3403-3414), roll `randomBetween(0,100) < frequency` (:3416-3419).
On hit: `wmRndEncounterPick()` selects table entry + sets `wmGenData.encounterMapId/TableId/EntryId`; Special entries pin a new city circle at the party position (`wmAreaSetWorldPos`, :3424-3443).
- **Icon flash**: `wmBlinkRndEncounterIcon(special)` (:3446) — blinks the lightning-bolt/“!” hotspot FRMs (wmRndCursorFids 154/155/438/439, worldmap.cc:725) ~7 blinks with sound, then control returns.
- **Outdoorsman detection** (:3453-3500): best party Outdoorsman (+20 with motion sensor, cap 95, + tile `encounter_difficulty` modifier) → on success, Yes/No dialog ("You detect something up ahead", msg 2999 / 3000+50*tableId+entryId) — **choosing No AVOIDS the encounter** (resets encounter ids, returns 0, walking continues) and grants XP = 100−outdoorsman.
- Then `oldWorldPosX/Y = worldPos` (Δ3 anchor reset, :3502-3503), return 1 → caller (:3108-3119): `wmFadeOut(); mapLoadById(wmGenData.encounterMapId); break` — leaves the worldmap loop, loads the encounter map (which triggers map_enter then wmSetupRandomEncounter per Q1).

### Return + resume
- Exit grids on encounter maps have dest map **-2** → `mapHandleTransition` (map.cc:1232) calls `wmWorldMap()` → `wmWorldMapFunc(0)` re-enters the loop. `wmGenData.worldPosX/Y` were never changed by the detour ⇒ **party reappears exactly where the encounter interrupted travel**.
- `isWalking`/walkDestination are also still set in wmGenData (in-memory) ⇒ **travel auto-resumes toward the original destination** on re-entry.
- Savegame persistence (`wmWorldMap_save`, worldmap.cc:1066): worldPosX/Y, currentAreaId, didMeetFrankHorrigan, pending encounter ids, car state, per-city x/y/state/visited/entrance states, per-subtile fog `state`, and **the remaining `Counter` values of one-shot entries** (:1117-1131). walkDestination/isWalking are NOT saved — loading a mid-travel save leaves you STOPPED at the saved position.

### Mapping onto our WorldmapScreen (currently instant teleport)
Traveling-dot mode estimate (engine-faithful enough):
1. Keep our city-circle UI; on click, instead of instant ApplyTransition, start a Bresenham walk of a party dot at ~1 px/frame (terrain divisor optional v1), +30 game-min per tick on our GameClock.
2. Every tick while walking: run the Q4 gate chain (real-time throttle can be replaced by "every 3rd subtile-pixel"; keep the Δ3 rule and known-area suppression — we know circle positions); roll vs subtile frequency from the [Tile N] grid.
3. On hit: flash icon (or skip), pick entry/map, `LoadMap(encounterMap, transient: true)` with MapDestination = chosen random_start_point; stash `_pendingWorldmapResume = (posX, posY, destX, destY)`.
4. Exit grid with Destination.Map == -2 → return to WorldmapScreen, restore dot at stashed pos, auto-resume walk; Map == -1 → treat the same for v1 (we have no town-map sub-screen).
5. Arrival at circle = existing enter-town flow.

## Q5. Milestone plan for OUR architecture (+ save format)

### Data model (new file src/Hexwaste.Formats/Map/WorldmapFile.cs, pure parser)
```csharp
sealed class WorldmapDef {
    int[] FrequencyValues;                       // [Data] Forced..None → 100/38/22/12/4/0
    List<TerrainPool> Terrains;                  // [Random Maps: X] → map lookup-names
    List<WmTile> Tiles;                          // [Tile N]
    List<EncounterTable> Tables;                 // [Encounter Table N]
    Dictionary<string, EncounterGroup> Groups;   // [Encounter: NAME] (case-insensitive)
}
sealed record WmSubtile(string Terrain, int[] FreqByDayPart /*morning,afternoon,night*/, string TableLookup, string Fill);
sealed class WmTile { int ArtIdx; WmSubtile[,] Subtiles; /*7x6... 6 cols x 6 rows*/ }
sealed class EncounterTable { string LookupName; List<string> Maps; List<EncounterTableEntry> Entries; }
sealed class EncounterTableEntry {
    int Chance; int Counter = -1; bool Special; string? MapOverride;
    List<SubEnc> SubEncounters;                  // (min,max,groupNameOrPlayer,situation)
    List<EncCondition> Conditions;               // type(Global/PlayerLevel/Rand/TimeOfDay/DaysPlayed/NumCritters), op(==,!=,<,>), param, value; And/Or links
}
sealed class EncounterGroup {
    string Name; Formation Position = Formation.Surrounding; int Spacing = 1; int Distance = -1;
    List<EncounterGroupEntry> Entries;
}
sealed class EncounterGroupEntry {
    int? RatioPercent;       // null = SINGLE (exactly one — leaders)
    bool Dead; int Pid; int Distance; int TileNum = -1;
    List<EncItem> Items;     // (MinQty, MaxQty, Pid, Wielded)
    int ScriptListIndex = -1; // worldmap.txt "Script:N" minus 1 (engine: _obj_new_sid_inst(..., scriptIdx-1), worldmap.cc:3853)
    List<EncCondition> Conditions;
}
```
Parser notes: keys are case-insensitive (`chance:`/`Chance:` both occur); `Special` is a bare token; conditions max 3 + And/Or; skip `Scenery:` (no-op) and `team_num` (engine-garbage). Unit tests guarded by FALLOUT2_DIR like the rest.

### Milestones
- **M0 (S)** — `WorldmapFile` parser in Formats + tests (counts: 21 tiles, 4 terrain pools, ~50 tables, ~120 groups; spot-check Arro_M enc_04 conditions and Bounty_Hunter_Low items). Also extend MapList parsing with `saved`, `random_start_point_N` (maps.txt already parsed for lookup names — add 2 fields).
- **M1 (M)** — Traveling-dot worldmap: WorldmapScreen gets pos/dest/isWalking state, Bresenham step per fixed tick (+30 game-min via GameClock), subtile lookup (x/50, y/50 within 350×300 tiles, `wmFindCurSubTileFromPos` worldmap.cc:3539), arrival = existing enter-town path. Pure UI/feel work; --rng-seed keeps it deterministic.
- **M2 (M)** — Roll + pick: gate chain (Δ3 quirk, circle suppression, daypart, difficulty skip), weighted entry pick (uniform-roll-over-summed-chances; skip luck/perk shifts v1), condition eval against our GlobalVars/level/clock, Counter decrement, map selection (entry Map: → table maps= → terrain pool). Skip: Horrigan, car, outdoorsman-avoid dialog (v1: always encounter; v2 add Yes/No since we have dialog UI).
- **M3 (M)** — Transient map load + spawn: `LoadMap(map, transient:true)` — transient = don't touch VisitedMaps/LocalVars on exit AND on entry (no delta replay, firstRun=1 every time); still RunMapEnter. After map_enter: pick random_start_point, place dude there; spawn groups via existing created-object path — `objectCreateWithPid` equivalent + `ScriptHost.AllocateSid(map, scriptIndex)` binding (same as party members, ViewerGame.cs:4324) + start-proc run (sets team/AI via critter_add_trait — hostility then comes from our existing critter_p_proc heartbeat, verified Q3); items with wielded flag through existing inventory/equip plumbing. Formation: v1 = surrounding-ring around dude (distance = Perception±2) for `Surrounding`, cluster-with-spacing at the start point for everything else; Dead entries spawn corpse-flagged (anim+28 path exists from P5-M3).
- **M4 (S)** — Return: encounter maps' exit grids already parse with Destination.Map = -2/-1; teach CheckExitGridAt (ViewerGame.cs:3214) that Map<0 ⇒ switch to WorldmapScreen and restore stashed (pos, dest, isWalking) → auto-resume. Group-vs-group FIGHTING autostart (whoHitMe + combat request between the two groups) — S, optional v1.
- **M5 (S)** — Polish: encounter-name announcement from worldmap.msg (`getmsg 3000 + 50*tableId + entryId`, worldmap.cc:3673), icon flash, special-encounter circle pinning (defer).

### SaveState: additive **stay V2** (no breaking change needed)
Must persist:
1. **Worldmap position** `(int WorldPosX, int WorldPosY)` + `int CurrentAreaId` — engine saves exactly this (worldmap.cc:1076-1078). Mid-travel destination need NOT be saved — engine doesn't (no walkDestination in wmWorldMap_save); loading mid-travel = stopped at position. If save is triggered while ON an encounter map: simplest faithful-enough rule — disallow quicksave on transient maps OR save position + `Map=""`+worldmap mode (engine saves the live map; replicating that means serializing a full transient map — not worth it; recommend "saving on encounter maps returns you to worldmap coordinates", documented divergence).
2. **One-shot Counters**: `Dictionary<string,int> EncounterCounters` keyed `"{tableLookup}:{encIndex}"` — engine persists remaining counters (worldmap.cc:1117-1131). Without this, special encounters repeat. (Their "seen" GVARs 605-620 already persist via GlobalVars, and most specials are double-gated on a Global — so even this is semi-redundant; include it, it's 10 lines.)
3. Nothing else: VisitedMaps untouched by design (saved=No), no subtile fog (we don't render fog), no car.
All three are new JSON properties with defaults ⇒ old saves load fine ⇒ **additive V2** (bump to V3 only if we also ship a breaking shape change from another track).

### Risk notes
- DON'T allocate a load-order ordinal for transient maps (P5-M1 keys deltas by load-order ordinals — entering desert1 between two town visits must not shift ordinals; verify VisitedMaps-keying tolerates a map that never registers).
- Encounter scripts (EC*.int) use only externals we already have (verified ECWarPty: random/self_obj/dude/attack/tile_*/obj_can_see_obj/add_timer_event/critter_add_trait/float_msg/party_member_obj) — no new VM work expected. ECBHuntr spot-checked too: adds only metarule, critter_is_fleeing, debug_msg, get/set_global_var - all already implemented.
- Total honest size: **M0 S, M1 M, M2 M, M3 M, M4 S, M5 S ⇒ phase-sized, comparable to phase 5.**
