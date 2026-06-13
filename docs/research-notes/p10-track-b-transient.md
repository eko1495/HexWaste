# P10 Track B — THE transient-map persistence question (the gating unknown)

Scope of this note: the single milestone-gating risk for Phase-10 random
encounters — **what `saved=No` (maps.txt) + a random-encounter map load do to
the save state**, and **whether Hexwaste's per-map-name deltas + load-order
ordinals corrupt on re-entering `desert1` twice**. All engine claims cite
`reference/fallout2-ce/src/<file>.cc:LINE`; all Hexwaste claims cite
`src/.../<File>.cs:LINE`; all data numbers are quoted from the real
`data\maps.txt` (extracted from master.dat), not from memory. The companion-
fold-in, the roll/pick chain, and the EC\*.int census are OTHER tracks' notes;
this note resolves ONLY persistence and names the exact integration test.

Verdict up front: **there is NO cross-map ordinal collision to fear** — the
prompt's framing ("re-entering desert1 twice corrupt ordinals") is the wrong
shape of the risk. Ordinals in Hexwaste are a *per-load index into one map's
own object list*, not a global identity, so they structurally cannot collide
with a real map's ordinals. The **real** risk is narrower and concrete: a
transient map that gets a `_visitedMaps` slot replays a STALE delta onto a
freshly-regenerated map (whose objects/spawns differ entirely). The fix is to
skip the delta slot on BOTH exit and entry and force firstRun=1 — exactly what
the prompt proposed, and it is **necessary and sufficient**. Details + the
exact test below.

---

## (1) What `saved=No` + the random-encounter load do to the save state (engine)

### `saved=No` is parsed into a per-map MAP_SAVED flag

`wmMapInit` reads the maps.txt `saved=` key into the flag bit
(worldmap.cc:2665-2671):
```
if (configGetString(&config, section, "saved", &str)) {
    ...
    wmSetFlags(&(map->flags), MAP_SAVED, num);   // worldmap.cc:2671
}
```
and the `random_start_point_0..N` keys at worldmap.cc:2724-2748. Accessors:
`wmMapIdxIsSaveable(mapIdx)` = `(flags & MAP_SAVED) != 0` (worldmap.cc:2822-2825),
`wmMapIsSaveable()` for the current map (worldmap.cc:2828-2831).

Real data (extracted `data\maps.txt`, `[Map 000]` desert1, lines 17-29):
```
[Map 000]
lookup_name=Desert Encounter 1
map_name=desert1
saved=No  ; Random encounter maps aren't saved normally (only in savegames)
dead_bodies_age=No
can_rest_here=No,No,No  ; All 3 elevations
random_start_point_0=elev:0, tile_num:19086
random_start_point_1=elev:0, tile_num:17302
random_start_point_2=elev:0, tile_num:21315
random_start_point_3=elev:0, tile_num:22699
random_start_point_4=elev:0, tile_num:20526
```
The inline comment is the engine authors' own statement of the rule: *"Random
encounter maps aren't saved normally (only in savegames)."* Census across the
real file: **57 `saved=No`, 94 `saved=Yes`, 151 `[Map]` sections total** (quoted
`grep -c` over `/tmp/maps.txt`) — desert1 is map index **0** with **5**
random_start_points.

### The `.SAV` is skipped (and erased) for non-saveable maps — three sites

A normal town map, when you leave it, gets flushed to `MAPS\<name>.SAV` so a
revisit replays your changes. The engine gates this on `wmMapIsSaveable()`:

1. **Map exit / before loading another map** — `_map_save_in_game` (map.cc:1427).
   When you walk a transition (`mapLoad` calls `_map_save_in_game(true)` first,
   map.cc:818), the flush branch is (map.cc:1456-1483):
   ```
   if (a1 && !wmMapIsSaveable()) {
       debugPrint("\nNot saving RANDOM encounter map.");
       _MapDirEraseFile_("MAPS\\", gMapHeader.name);   // <name>.SAV  (map.cc:1461)
   } else {
       debugPrint("\n Saving \".SAV\" map.");
       _map_save();                                     // writes <name>.SAV (map.cc:1468)
   }
   ```
   So on exit it not only **skips the write, it DELETES any stale `.SAV`**.
2. **Map load** — `mapLoadSaved` (map.cc:1054) belt-and-braces: after
   `mapLoadByName`, `if (!wmMapIsSaveable()) { "Destroying RANDOM encounter map.";
   _MapDirEraseFile_(...".SAV"); }` (map.cc:1074-1085). A revisit therefore
   always loads the pristine `.MAP`, never a leftover `.SAV`.
3. **`_is_map_idx_same`** (map.cc:519-547) returns 0 (never-same) when either map
   is non-saveable (`!wmMapIdxIsSaveable(map1/2)`, map.cc:529-535) — so "the same"
   encounter map idx is treated as a *fresh* load every time, not a re-entry.

Plus the area-marking is suppressed: `wmMapMarkVisited` returns early for
non-saved maps (no green circle, worldmap.cc:2859-2867) and
`wmMapMarkMapEntranceState` returns -1 (worldmap.cc:2940-2952). Encounter maps
never become known areas/entrances.

### map_enter STILL runs; the spawn happens AFTER it

`mapLoad` runs `scriptsExecMapEnterProc()` (map.cc:1010) normally even for a
saved=No map — these maps DO carry a map script (p8 probe: desert1 header
ScriptIndex 313, mountn1 315). Only **after** map_enter does the engine spawn
the encounter group via `wmSetupRandomEncounter` (called from the map-load path
per p8-track-a note; group sizing/placement is worldmap.cc:3657+, OTHER track).
For Hexwaste this means: **transient ≠ skip map_enter**. Run map_enter, then
spawn. (Cheap — these maps have LocalVariablesCount=0, so LVAR import is
vacuous; p8-track-a Q1.)

### What happens if you SAVE THE GAME while standing on a transient map

This is the one place the engine *does* serialize a transient map, and it is the
documented divergence we will take. The save-game path is `_GameMap2Slot`
(loadsave.cc:2435) → `_map_save_in_game(false)` (loadsave.cc:2441). With
**`a1 == false`**, the skip condition `if (a1 && !wmMapIsSaveable())`
(map.cc:1456) is **false**, so it takes the **else** branch and writes the
current map's `.SAV` into the slot — i.e. an in-game save *does* capture the
live encounter map. On the next *new game / load*, `_InitLoadSave`/`_ResetLoadSave`
erase all `MAPS\*.SAV` (loadsave.cc:343,356), and the worldmap save stream holds
`worldPosX/Y` + `currentAreaId` so travel resumes. The dude itself is saved by a
**dedicated stream** independent of any map: `_obj_save_dude` (object.cc:3607)
temporarily clears `OBJECT_NO_SAVE`, writes the dude + `gCenterTile`, then
re-sets the flag (object.cc:3611-3624) — `_obj_load_dude` restores it
(object.cc:3629+). So **the dude carries `OBJECT_NO_SAVE` and is NEVER part of
any map's `.SAV`**; it round-trips via its own stream regardless of whether the
current map is transient. (This is the answer to the prompt's `_obj_save_dude`
sub-question: the dude's persistence is decoupled from the encounter-map slot
entirely.)

**Hexwaste already matches this structurally**: our dude is an out-of-map runtime
object built by `SpawnDude` (ViewerGame.cs:927), not a pristine map record, and
is excluded from `CaptureMapDelta` (`obj != _dude?.Dude`, ViewerGame.cs:3904).
So the dude is a non-issue for transient maps; the only question is the *map's*
delta machinery, below.

---

## (2) Hexwaste's delta machinery — what would actually corrupt, and what would not

### The keying, read precisely (this is where the prompt's framing is slightly off)

Two independent keyings, do NOT conflate them:

- **Per-map deltas are keyed by MAP NAME (a string), not by ordinal.**
  `_visitedMaps` is `Dictionary<string, MapDelta>` with `OrdinalIgnoreCase`
  (ViewerGame.cs:268), and `CaptureMapDelta` writes
  `_visitedMaps[_map.Header.Name] = delta` (ViewerGame.cs:3945). SaveState mirrors
  this: `VisitedMaps` is `Dictionary<string, MapDelta>` keyed by header name
  (SaveState.cs:57, comment "keyed by header map name").
- **Ordinals are a per-LOAD index INTO one map's own object list.** In `LoadMap`,
  `_objectOrdinals`/`_ordinalObjects` are cleared and rebuilt every load by
  iterating the freshly-parsed MapFile's objects in load order
  (ViewerGame.cs:906-919): `_objectOrdinals[obj] = ordinalObjects.Count`. There is
  **no global ordinal counter** — ordinal 0 of desert1 and ordinal 0 of denbus1
  are unrelated indices into different lists. The phase-5 reason for ordinals
  (SaveState.cs:8-12, "MAP object Ids are NOT unique") is about disambiguating
  duplicate-Id objects *within one map*, not across maps.

**Consequence:** "two desert1 re-entries collide with a REAL map's ordinal" is
structurally impossible. A real map (denbus1) has its own `_visitedMaps["DENBUS1.MAP"]`
slot and its own ordinal space; nothing desert1 does can touch it. The ordinals
are reborn on every `LoadMap`. The Δ here vs the prompt's hypothesis: the risk
is NOT cross-map ordinal collision — it is **same-name stale-delta replay**.

### The REAL corruption: a transient map getting a `_visitedMaps` slot

If we let desert1 flow through the normal path, two things go wrong on a second
desert1 encounter:

1. **Stale delta replay.** Visit #1 leaves desert1 → `CaptureMapDelta` writes
   `_visitedMaps["DESERT1.MAP"]` holding TakenOrdinals/DeadOrdinals/MovedOrdinals/
   ContainerInventories/Created/Doors that index visit-#1's object list AND list
   the visit-#1 spawned group as `Created`. Visit #2 reloads pristine desert1 and
   `LoadMap` finds the slot (ViewerGame.cs:922) → `ApplyDeltaBeforeScripts` +
   `ApplyDeltaAfterScripts` (ViewerGame.cs:924,954) replay those ordinals onto a
   DIFFERENT pristine object set and re-inject visit-#1's stale `Created` corpses/
   critters alongside visit-#2's fresh spawn. Garbage. (TakenOrdinals removing the
   wrong objects, MovedOrdinals teleporting the wrong objects, dead/looted state
   bleeding across unrelated encounters.)
2. **firstRun goes to 0 on revisit.** `_visitedMaps` containing the key drives
   `firstRunOverride: delta is not null ? false : null` (ViewerGame.cs:943-944),
   and independently `_firstRunByMap["DESERT1.MAP"]` is cached in the host
   (ScriptHost.cs:455,485) and `IsFirstRun` consults it (ScriptHost.cs:487-490),
   plus the LVAR slices `_localVarSlices[(map.Header.Name, sid)]` survive in the
   host (ScriptHost.cs:557,573). So a second desert1 would run map_enter with
   `metarule(14) FIRST_RUN == 0` (ScriptHost.cs:800) and reuse cached LVARs — but
   the engine ALWAYS treats a saved=No map as first-run (`_is_map_idx_same` → 0,
   never-same; the `.SAV` is erased). Encounter scripts (ECWarPty etc.) gate their
   spawn/aggro on first-run; firstRun=0 would suppress them.

### What is already safe (no change needed)

- The dude (out-of-map; excluded from capture) — see §1.
- Party members travel OUTSIDE map deltas (SaveState.PartyMemberState,
  SaveState.cs:62-63,80-81; `ExtractPartyFromMap`/`InjectPartyMembers`,
  ViewerGame.cs:840,955) — they are not pristine map records, so transient maps
  don't disturb the roster.
- Real maps' deltas — untouched, different name keys, different ordinal spaces.

---

## (3) THE integration design (definitive)

**A map is "transient" iff its maps.txt entry has `saved=No`.** Add a `Saved`
bool to MapList parsing (currently MapList.cs does NOT read `saved=` —
MapList.cs:32-46 reads only `map_name=`/`lookup_name=`/`music=`; this is the gap
the prompt flagged). Thread `bool transient` into `LoadMap`. Then on a transient
load, do ALL of the following — skipping any one re-opens a corruption path:

1. **Do NOT read a delta on entry.** Guard ViewerGame.cs:922-924: if transient,
   skip the `_visitedMaps.TryGetValue` → `ApplyDeltaBeforeScripts` and the
   `ApplyDeltaAfterScripts` (ViewerGame.cs:953-954). `delta` stays null ⇒
   firstRun override stays `null` ⇒ falls to the header-flag default (which is
   firstRun=1 for a pristine, un-flagged MAP, RunMapEnter ScriptHost.cs:454). The
   ordinals are still built (cheap, needed for spawn/loot within the visit) but
   never matched against a stored delta.
2. **Do NOT write a delta on exit.** Guard `CaptureMapDelta`'s final
   `_visitedMaps[_map.Header.Name] = delta` (ViewerGame.cs:3945) — early-return
   for transient maps (or never call CaptureMapDelta for a transient outgoing map
   in `LoadMap`, ViewerGame.cs:838-842). No slot ⇒ nothing to replay next time ⇒
   pristine regeneration, matching the engine's erase-the-.SAV (map.cc:1461).
3. **Force firstRun=1 every visit.** Two host caches must not make a re-entered
   transient map look "seen": clear (or never set) `_firstRunByMap[name]` and the
   `_localVarSlices[(name, *)]` for transient maps. Cleanest: have the transient
   `LoadMap` pass `firstRunOverride: true` to `RunMapEnter` (so it's first-run
   regardless of the header `flags & 0x01`), and skip exporting/importing LVARs
   for transient map names in `ExportAllLocalVars`/`ImportLocalVars`
   (ScriptHost.cs:591-599) — or simply never store them since desert1's
   LocalVariablesCount=0 anyway (p8-track-a Q1). The minimal robust move: a
   `RunMapEnter(..., firstRunOverride: true)` call + a transient set on the host
   that `ExportAllLocalVars` filters out.
4. **Still RUN map_enter, then spawn.** Engine order: map_enter
   (map.cc:1010) → `wmSetupRandomEncounter` (after). Mirror it: transient
   `LoadMap` runs `RunMapEnter` as today (firstRun=1), THEN does the group spawn
   (M3 work — `objectCreateWithPid` + `ScriptHost.AllocateSid`, OTHER track).
5. **Do NOT mark visited.** We have no green circle for desert1 (it's not in
   city.txt — p8-track-a Q1), so this is free. Just ensure nothing adds desert1
   to any "known areas" set on entry.

That is the WHOLE persistence design: **"skip VisitedMaps on both exit and entry
+ firstRun=1 always + still run map_enter."** It is necessary (each clause closes
a distinct corruption path: #1 stale read, #2 stale write, #3 stale firstRun/
LVAR) and sufficient (the engine itself does no more than erase the .SAV and
treat the map as never-same — map.cc:519-547,1074-1085,1456-1462). No new
ordinal scheme, no global counter, no Version bump for persistence itself (the
worldmap position + per-table Counters that DO need saving are additive-V2 JSON
fields — OTHER track / cross-cutting; persistence of the transient map is
achieved by *storing nothing*).

### Save-while-on-transient-map (the documented divergence)

The engine writes the live transient map into the slot on in-game save
(_map_save_in_game(false), §1). Replicating that means serializing a full live
transient map into our JSON — not worth it, and SaveGame already calls
`CaptureMapDelta` first (ViewerGame.cs:4436), which under design clause #2 writes
NO slot for a transient map. Recommended rule (matches the p8-track-a Q5 stance):
**saving on a transient encounter map persists worldmap coordinates + dude
state, and a reload drops you back on the worldmap at the saved position rather
than mid-encounter.** This is a stated, faithful-enough divergence; the engine's
own design erases the transient .SAV on the very next new-game/load anyway
(loadsave.cc:343,356), so "transient maps don't survive a reload" is already
half the engine's behavior. Cheapest correct implementation: when SaveGame runs
on a transient map, write `Map` = the worldmap (or a sentinel) + WorldPos, not
the desert1 name.

---

## (4) THE exact integration test (this gates M3)

The prompt asks for a definitive named test where "two desert1 re-entries must
not collide with a real map's ordinal." Because cross-map ordinal collision is
structurally impossible (§2), the test must instead prove the *real* invariant:
**a transient map gets no delta slot and re-generates pristine, while an
interleaved real map's delta is untouched.** Two layers:

### A — Formats-level unit test (no game assets; runs in CI)

Home: `tests/Hexwaste.Formats.Tests/PersistenceTests.cs` (alongside
`SaveStateRoundTripTests`, PersistenceTests.cs:7), `[Fact]` (no `[GameDataFact]`
gate — pure model):

`TransientMapTakesNoDeltaSlotAndDoesNotPerturbRealMapDelta`
1. Build a SaveState with a real-map delta: `VisitedMaps["DENBUS1.MAP"]` with
   a known TakenOrdinals=[5,7] + a ContainerInventories entry.
2. Simulate the transient flow on the model: assert that the transient capture
   path writes NOTHING to `VisitedMaps["DESERT1.MAP"]` (key absent), and that
   `VisitedMaps["DENBUS1.MAP"]` is byte-identical before/after (round-trip the
   JSON, compare). This locks design clause #2 + proves a real map's ordinal-
   keyed delta is independent of any transient activity.
3. Assert LVAR export excludes the transient map name (clause #3): after
   "visiting" DESERT1, `ExportAllLocalVars()` contains no DESERT1.MAP key.

### B — Integration test (`[GameDataFact]`, guarded by FALLOUT2_DIR)

Home: `PersistenceTests.cs` `ScriptHostTransitionTests`
(PersistenceTests.cs:77, next to `LocalVarsSurvivePristineReloadAndHandlesReset`),
driving the same ScriptHost/MapFile path the viewer uses:

`TwoDesert1ReentriesRegeneratePristineAndLeaveRealMapDeltaIntact`
1. Load denbus1 (real, saveable), make a delta-visible change (e.g. mark an
   ordinal taken), exit → assert `_visitedMaps["DENBUS1.MAP"]` exists with the
   change.
2. Load desert1 (transient): assert `RunMapEnter` ran with **firstRun=1**
   (`IsFirstRun(desert1) == true`, ScriptHost.cs:487); mutate it (kill/move a
   spawned critter); exit → assert **`_visitedMaps` has NO "DESERT1.MAP" key**.
3. Load desert1 AGAIN: assert (a) `IsFirstRun(desert1) == true` *again* (clause
   #3 — the host did not cache it as seen), (b) the object set is the pristine
   MAP object count (no stale `Created`/`MovedOrdinals` from visit #1 leaked
   in), and (c) `_visitedMaps["DENBUS1.MAP"]` from step 1 is **still present and
   unchanged** (the interleaved real map's ordinal-keyed delta survived two
   transient round-trips untouched). Step (c) is the literal "two desert1
   re-entries must not collide with a real map's ordinal" assertion, reframed to
   the mechanism that actually exists.

Acceptance gate for M3: both tests green under `--rng-seed` determinism. If
either fails, the spawn work (M3) is built on corruptible persistence — fix
first. Given the analysis, the implementation is ~30 LoC of guards
(MapList `Saved` parse + 3 `if (transient)` branches in LoadMap/CaptureMapDelta/
RunMapEnter wiring), so this is **S, ~30-40 LoC**, and the risk is LOW —
contrary to it being "the milestone-gating risk," the analysis shows the
machinery already isolates maps by name and rebuilds ordinals per load, so the
guards are additive and local, not a refactor.

---

## Sizing + milestone fit

| Piece | Effort / LoC | Where |
|---|---|---|
| MapList `Saved` (+`random_start_point_N`) parse | **S** ~15 | MapList.cs:32-46 |
| `LoadMap` transient flag + skip-read / skip-write / firstRun=1 guards | **S** ~20 | ViewerGame.cs:838-944, 3945 |
| LVAR/firstRun host filter for transient names | **S** ~10 | ScriptHost.cs:455,591-599 |
| Save-on-transient divergence (store worldpos not map) | **S** ~10 | ViewerGame.cs:4436 (SaveGame) |
| Formats unit test (A) + integration test (B) | **S** ~60 | PersistenceTests.cs |

**Milestone fit:** this is the **M3 gate** ("transient map load + group spawn").
It must land at the TOP of M3, before the spawn code, because spawn correctness
depends on pristine regeneration. M0 can pre-stage the cheap MapList `Saved`
parse + tests with no behavior change. Maps to **P10-M3** in the proposed plan;
unblocks the worldmap return/resume (M4) which relies on transient maps being
disposable.

## Unverified / honest flags

- **Save-while-on-transient-map** exact engine bytes: I confirmed the *control
  flow* (`_map_save_in_game(false)` writes the current map's .SAV into the slot,
  loadsave.cc:2441 + map.cc:1456 else-branch; new-game erases it,
  loadsave.cc:343) but did NOT byte-trace the `.SAV` payload format — irrelevant,
  since we take the documented divergence (persist worldpos, not the live map).
- The **per-table `Counter` persistence + worldmap position save fields** are a
  separate additive-V2 concern (cross-cutting / OTHER track), NOT part of the
  transient-map persistence design, which is achieved by storing *nothing* for
  the map.
- `wmSetupRandomEncounter` group-spawn order/placement (after map_enter) is cited
  from p8-track-a Q1 (worldmap.cc:3657+); I did not re-disassemble it here — it's
  M3 spawn work, OTHER track. This note asserts only that map_enter runs first
  and the spawn happens after, which p8-track-a verified against map.cc:1010,978.
- The Hexwaste guard LoC are an estimate from reading the call sites
  (ViewerGame.cs LoadMap/CaptureMapDelta, ScriptHost RunMapEnter); not yet
  implemented/measured.
