# Phase 10 Research Report — The Wasteland Bites Back (Random Encounters + Companion Fold-in)

*Researched 2026-06-13 in-repo: five parallel tracks — the worldmap.txt table
semantics + the `wmRndEncounterOccurred` roll/pick/condition chain (every section
parsed from the real 4047-line file, every gate cited `worldmap.cc:LINE`); the
transient-map persistence question (the one gating unknown, resolved against
`map.cc` + Hexwaste's own delta machinery, with the exact integration test); the
transient-map group spawn + formations + the headline **EC\*.int missing-external
census** (24 encounter scripts disassembled operand-by-operand); the worldmap
travel UI — the Bresenham traveling dot + the return/resume seam; and the
companion fold-in (M4–M5) — `metarule(16)`, dismiss/rejoin nodes, the flat 1:1
trade panel — with `dcVic.int`/`kcsulik.int` disassembled. Full track notes:
`docs/research-notes/p10-track-{a,b,c,d,e}-*.md`. Every engine claim carries a
`reference/fallout2-ce/src/<file>.cc:line` cite verified this session; every data
number is quoted from the real `worldmap.txt`/`maps.txt`/scripts.lst, not from
memory. Five adversarial verification passes confirmed the load-bearing claims;
one correction folded in (`metarule(16)` is genuinely unimplemented, not merely
"stubbed-safe" — see §Companions). Unverified items flagged at the end.*

## TL;DR

- **Recommended path: Random Worldmap Encounters (M0–M3), with the cheap
  companion-lifecycle pieces folded in as M4–M5 — exactly the standing decision,
  confirmed by content.** The encounter spine is the first true *traveling*
  worldmap, where phase-9's combat depth finally gets exercised; M0 hygiene + the
  three-clause transient-persistence resolution come first because spawn
  correctness depends on pristine map regeneration. The companion fold-in is
  genuinely cheap (zero new externals) and broadly reusable.
- **The single gating unknown is RESOLVED and SMALLER than feared.** The prompt
  framed the risk as "re-entering desert1 twice corrupts load-order ordinals."
  That collision is **structurally impossible** — Hexwaste's ordinals are a
  *per-load index into one map's own object list* (rebuilt every `LoadMap`,
  `ViewerGame.cs:906-919`), and deltas are keyed by **map NAME** string
  (`VisitedMaps : Dictionary<string,MapDelta>`, `SaveState.cs:57`), not ordinal.
  The *real* risk is narrower: a transient map that gets a `VisitedMaps` slot
  replays a STALE delta onto a freshly-regenerated map. The fix is three guards —
  **skip the delta slot on both exit and entry + force firstRun=1 + still run
  map_enter** — exactly what the engine does (`map.cc:1456-1462` erases the
  `.SAV`; `map.cc:519-547` treats the idx as never-same). **S, ~30-40 LoC, LOW
  risk.** This is M0/M3 work, not a refactor.
- **ZERO new combat externals are required to make the Arroyo→Klamath→Den loop
  spawn-and-fight.** The EC\*.int census (24 scripts disassembled) found every
  aggro path (`obj_can_see_obj → attack`), team assignment (`critter_add_trait`),
  and weapon equip (the in-hand item flag) already runs on real host code. Of the
  16 externals the full early loop touches that we lack, **all 16 are cosmetic
  (spawn-rotation, idle-wander, flee-bark, fade), debug, or loot-dressing — none
  on a combat path.** `OnStubbedExternal` (wired phase-6 M0, `IntVm.cs:1336`)
  already catches every one stack-balanced. Spawning is the verified
  `create_object_sid` + `AllocateSid` party-init path (`IntVm.cs:1164`).
- **ZERO new externals for the companion fold-in either.** `metarule(16)`
  PARTY_COUNT is a ~3-LoC port of `_getPartyMemberCount` (`party_member.cc:900`).
  Dismiss/rejoin/wait are pure dialog nodes using already-real externals
  (`party_add`/`party_remove`/`critter_add_trait`/`set_local_var`). The trade
  panel reuses the loot panel pointed at the follower's `Inventory` + one
  give-to-follower drop variant (~120 LoC viewer). **Vic's radio rescue is
  correctly DEFERRED** — it needs two inventory-query externals and is a 5-node
  content slice, not lifecycle.
- **Determinism is the acceptance gate, mirroring combat.** Reuse the existing
  `ICombatRng`/`SystemCombatRng(seed)` for the worldmap RNG so the whole
  travel+encounter sequence is a golden-transcript fixture under `--rng-seed` —
  the same harness pattern phase-9 combat used. The traveling dot is pure integer
  Bresenham (deterministic by construction); the only randomness is the roll.
- **Save format: additive-V2, NO V3 bump.** `SaveState.CurrentVersion = 2`
  (confirmed `SaveState.cs:20`). Transient-map persistence is achieved by *storing
  nothing* for the map; the only NEW save fields are `WorldPosX`/`WorldPosY`/
  `CurrentAreaId` + a per-table `Counter` dict — additive nullable/defaulted, one
  shared bump with the encounter-counter work. The engine itself saves these
  (`wmWorldMap_save`, worldmap.cc) and does NOT save mid-walk destination, so a
  loaded mid-travel save leaves you stopped at `worldPos` — we match that.

## Comparison / sizing table

| Area | Effort | Felt-depth payoff | Content in slice | Risk | Verdict |
|---|---|---|---|---|---|
| **M0 — hygiene + persistence pre-stage** (SCOPE.md refresh; MapList `saved`/`random_start_point_N` parse; the transient-flag plumbing + tests, no behavior change) | **S** (~60 LoC + tests) | None directly — it's the *net* | n/a | Low | **BUILD FIRST** |
| **WorldmapFile parser + roll/pick chain** (`[Data]`/`[Tile]`/`[Encounter Table]`/`[Encounter: GROUP]` + `wmRndEncounterOccurred`/`wmRndEncounterPick` port) | **M-L** (the parser is the long pole) | **High** — the spine | YES — every Arroyo/Klamath/Den tile | Low (data fully decoded) | **M1** |
| **Traveling dot + travel UI** (Bresenham port + subtile lookup + 30-min tick + per-step roll hook) | **M** (~140 LoC, integer math) | **Very high** — the world moves | n/a | Low | **M1** (with the chain) |
| **Transient-map load + group spawn** (the 3-clause persistence guards + `create_object_sid` group spawn + formations) | **M** | **High** — the fight finally happens | YES — ARRO/KLA/DEN groups | **Med** (persistence is the gate, but resolved) | **M3** |
| **Return / resume seam** (exit-grid Map==-2 → `_worldmapOpen`; ambient dot state on persistent `WorldmapScreen`) | **S** (~30 LoC) | **High** — seamless | n/a | Low | **M3/M4** (cheapest win) |
| **metarule(16) + dismiss/rejoin + follow audit** | **S** (~3 LoC port + dialog-hub wiring + msg resolve + audit fixtures) | Med — party dialog correctness | YES — Sulik/Vic gates | Low | **M4** |
| **1:1 companion trade panel** (loot panel re-point + give-to-follower variant) | **M-** (~120 LoC viewer) | Med — gear your companion | YES | Low | **M5** |
| **Vic's radio rescue** (0x810D/0x80BA + multi-node leg) | M+ | High but Vic-specific | denbus/radio | — | **DEFER** (content, not lifecycle) |
| **Outdoorsman-avoid / perks / Luck / difficulty skew / special-circle pin / Horrigan / car** | S-M each | Low for the early loop | none reachable | — | **SKIP v1** (each confirmed skippable, §3) |

Effort legend: S ≈ ≤½ day, M ≈ 1-2 days, L ≈ 3+ days. "Felt-depth payoff" weighed
against *what a fresh Arroyo→Klamath→Den player actually meets*, per the Track A/C
census — not engine completeness.

---

## THE transient-map persistence design (the gating question, resolved up front)

This is the one real unknown the prompt called the milestone-gating risk. The
analysis (Track B) shows the machinery already isolates maps by name and rebuilds
ordinals per load, so the fix is additive guards, not a refactor — **S, ~30-40
LoC, LOW risk.** It must land at the TOP of M3, before any spawn code.

### What the engine does (verified `map.cc`/`worldmap.cc` this session)

- `saved=No` → the `MAP_SAVED` flag (`worldmap.cc:2665-2671`); accessor
  `wmMapIsSaveable()`.
- **On exit, the engine SKIPS the `.SAV` write AND DELETES any stale one**
  (`map.cc:1456-1462`, confirmed verbatim this session: `if (a1 &&
  !wmMapIsSaveable()) { "Not saving RANDOM encounter map." ; _MapDirEraseFile_(…
  ".SAV") }`).
- **On load it belt-and-braces destroys a leftover `.SAV`** (`map.cc:1074-1085`),
  and `_is_map_idx_same` returns 0 (never-same) for a non-saveable idx
  (`map.cc:519-547`) — every visit is a *fresh* pristine load.
- **map_enter STILL runs, THEN the spawn happens.** Confirmed this session at
  `map.cc:974` (`scriptExecProc(gMapSid, SCRIPT_PROC_MAP_ENTER)`) immediately
  followed by `map.cc:978` (`wmSetupRandomEncounter()`). Transient ≠ skip
  map_enter. desert1 carries a trivial map script (LocalVariablesCount=0), so the
  LVAR import is vacuous but the proc must run.
- The dude is decoupled from any map's `.SAV` entirely — it round-trips via its
  own `_obj_save_dude` stream (`object.cc:3607`), carrying `OBJECT_NO_SAVE`.
  Hexwaste already matches this: the dude is an out-of-map runtime object
  (`SpawnDude`) excluded from `CaptureMapDelta` (`obj != _dude?.Dude`,
  `ViewerGame.cs:3904`). The dude is a non-issue for transient maps.

### Why the prompt's framing is the wrong shape (the key insight)

Two **independent** keyings — do not conflate:
- **Deltas keyed by map NAME (string).** `_visitedMaps` is `Dictionary<string,
  MapDelta>` OrdinalIgnoreCase (`ViewerGame.cs:268`); `SaveState.VisitedMaps`
  mirrors it (`SaveState.cs:57`, confirmed this session). `DESERT1.MAP` and
  `DENBUS1.MAP` are different keys with disjoint delta storage.
- **Ordinals are a per-LOAD index into one map's own object list.** `LoadMap`
  clears and rebuilds `_objectOrdinals`/`_ordinalObjects` every load
  (`ViewerGame.cs:906-919`). There is **no global ordinal counter** — ordinal 0 of
  desert1 and ordinal 0 of denbus1 are unrelated. The phase-5 reason for ordinals
  (disambiguate duplicate-Id objects *within one map*) is intra-map only.

⇒ "two desert1 re-entries collide with a real map's ordinal" is structurally
impossible. The **real** corruption is **same-name stale-delta replay**: if
desert1 gets a `_visitedMaps["DESERT1.MAP"]` slot on exit #1, then visit #2
reloads pristine desert1, finds the slot, and replays visit-#1's
TakenOrdinals/MovedOrdinals/`Created` corpses onto a DIFFERENT pristine object set
alongside visit-#2's fresh spawn — garbage. Secondarily, the slot drives
`firstRun → 0` on revisit (`ViewerGame.cs:943-944` + `_firstRunByMap` cache,
`ScriptHost.cs:455,487`), but the engine ALWAYS treats a saved=No map as first-run.

### The design (definitive — three clauses, each closes a distinct path)

**A map is transient iff its maps.txt entry has `saved=No`.** On a transient load:

1. **Do NOT read a delta on entry.** Guard the `_visitedMaps.TryGetValue` →
   `ApplyDeltaBeforeScripts`/`ApplyDeltaAfterScripts` (`ViewerGame.cs:922-924,
   953-954`). `delta` stays null ⇒ firstRun override stays null ⇒ falls to the
   pristine-MAP default (firstRun=1). Ordinals are still built (cheap, needed for
   in-visit spawn/loot) but never matched against a stored delta.
2. **Do NOT write a delta on exit.** Early-return `CaptureMapDelta`'s final
   `_visitedMaps[name] = delta` for transient maps (`ViewerGame.cs:3945`), or skip
   the call entirely in `LoadMap`'s outgoing path (`ViewerGame.cs:838-842`). No
   slot ⇒ nothing to replay ⇒ pristine regeneration, matching the engine's
   erase-the-`.SAV`.
3. **Force firstRun=1 every visit.** Pass `firstRunOverride: true` to `RunMapEnter`
   and exclude transient map names from `ExportAllLocalVars`/`ImportLocalVars`
   (`ScriptHost.cs:591-599`) — or simply never store them (desert1's
   LocalVariablesCount=0 anyway). Closes the `metarule(14)==0` / cached-LVAR path.
4. **Still RUN map_enter, then spawn.** Mirror the engine order: transient
   `LoadMap` runs `RunMapEnter` (firstRun=1), THEN the M3 group spawn.
5. **Do NOT mark visited.** desert1 is not in city.txt — no green circle — so this
   is free; just ensure nothing adds it to a known-areas set.

Necessary (each clause closes a distinct corruption path) and sufficient (the
engine does no more than erase the `.SAV` + treat the map as never-same). **No new
ordinal scheme, no global counter, no Version bump for persistence** — persistence
of the transient map is achieved by *storing nothing*.

**Save-while-on-transient (documented divergence):** the engine writes the live
transient map into the slot on in-game save (`_map_save_in_game(false)` else-branch,
`map.cc:1456`) but erases it on the next new-game (`loadsave.cc:343`). Replicating
that means serializing a full live transient runtime map — not worth it. Rule:
**saving on a transient map persists `WorldPosX/Y` + `CurrentAreaId` + dude state,
and a reload drops you back on the worldmap at the saved position, not mid-encounter.**

### The exact integration test (this gates M3)

**A — Formats unit test** (`tests/Hexwaste.Formats.Tests/PersistenceTests.cs`,
`[Fact]`, no game assets):
`TransientMapTakesNoDeltaSlotAndDoesNotPerturbRealMapDelta`
1. Build a SaveState with `VisitedMaps["DENBUS1.MAP"]` = known TakenOrdinals=[5,7]
   + a ContainerInventories entry.
2. Run the transient capture path; assert NOTHING is written to
   `VisitedMaps["DESERT1.MAP"]` (key absent) and `VisitedMaps["DENBUS1.MAP"]` is
   byte-identical before/after (round-trip the JSON, compare). Locks clause #2 +
   proves a real map's ordinal-keyed delta is independent of transient activity.
3. Assert `ExportAllLocalVars()` excludes the transient map name (clause #3).

**B — Integration test** (`[GameDataFact]`, guarded by `FALLOUT2_DIR`):
`TwoDesert1ReentriesRegeneratePristineAndLeaveRealMapDeltaIntact`
1. Load denbus1 (real), mark an ordinal taken, exit → assert
   `_visitedMaps["DENBUS1.MAP"]` exists with the change.
2. Load desert1 (transient): assert `IsFirstRun(desert1) == true`
   (`ScriptHost.cs:487`); mutate it (kill/move a spawned critter); exit → assert
   **NO `"DESERT1.MAP"` key** in `_visitedMaps`.
3. Load desert1 AGAIN: assert (a) `IsFirstRun(desert1) == true` *again*, (b) the
   object set is the pristine MAP count (no stale `Created`/`MovedOrdinals` leaked
   in), (c) `_visitedMaps["DENBUS1.MAP"]` from step 1 is **still present and
   unchanged**. Step (c) is the literal "two desert1 re-entries must not collide
   with a real map's ordinal" assertion, reframed to the mechanism that exists.

Acceptance gate for M3: both green under `--rng-seed`. If either fails, the spawn
work is built on corruptible persistence — fix first.

---

## The worldmap roll/pick chain + table semantics (the spine)

### The data file — section grammar (real values, parsed from `worldmap.txt`)

- **`[Data]` frequency names → percentages** (`worldmap.txt:32-39`): `Forced=100%
  Frequent=38% Common=22% Uncommon=12% Rare=4% None=0%`. Loaded into
  `wmFreqValues[6]` (`worldmap.cc:790`); a subtile's daypart cell stores the
  *index*, the roll reads `wmFreqValues[index]`.
- **`[Tile N]` — the world grid** (`worldmap.txt:3064-4040`, 20 tiles): each is a
  **7×6 = 42-subtile grid**. CONFIRMED this session: `SUBTILE_GRID_WIDTH=7`,
  `SUBTILE_GRID_HEIGHT=6` (`worldmap.cc:64-65`), `subtiles[HEIGHT][WIDTH]`
  (`worldmap.cc:372`). **This corrects the phase-7/8 notes' "6×6".** The subtile
  line, field order EXACT (`wmParseSubTileInfo`, `worldmap.cc:1943-1967`):
  `R_C = Terrain , Fill , morningChance , afternoonChance , nightChance ,
  TableLookup`. The key is `"%d_%d"` with `R=row=x-subtile` (0..6, `x%350/50`),
  `C=column=y-subtile` (0..5, `y%300/50`) — **index `subtiles[column][row]`** or
  you read the wrong frequency. `Fill` and `Scenery` are cosmetic/no-op — skip.
  Real row: `worldmap.txt:3085` `2_4=Desert,No_Fill,Uncommon,Uncommon,Uncommon,
  Arro_D` (= 12% all dayparts).
- **`[Encounter Table N]`** (`worldmap.txt:1036+`, 76 tables, `wmReadEncounterType`
  `worldmap.cc:1367`): `lookup_name=` (the subtile pointer), `maps=` (≤6 fallback
  map names), `enc_00..NN` (≤40 entries, `candidates[41]` `worldmap.cc:3568`).
  Per-entry: `Chance:N%` (weight, NOT normalized — the pick is a roll over the SUM,
  `:1437`), `Counter:N` (one-shot budget, decremented on selection, `counter==0`
  filtered `:3579-3581`, default -1 unlimited), `Special` + `Map:` (per-entry map
  override), `Enc:` (`[(min-max)] GROUP [SITUATION]`, SITUATION ∈ `Nothing/AMBUSH/
  FIGHTING/AND`), `If(...)` (up to 3 sub-conditions + 2 links, `wmParseConditional`
  `:2110`).
- **`[Encounter: GROUP]`** (`worldmap.txt:156-1034`, `wmReadEncBaseType`
  `worldmap.cc:1611`): per `type_NN`: `ratio:N%` (omitted ratio = SINGLE, exactly
  one — the leader), `Dead` prefix (corpse dressing), `pid:N`, `Item:[(min-max)]
  PID[(wielded)|(worn)]` (≤10), `Script:N`, trailing `If(...)`. `position=
  FORMATION[, Spacing:N][, Distance:N]`, formations = `surrounding/straight_line/
  double_line/wedge/cone/huddle`. **`team_num=` is engine-UB garbage** (appears
  only in a commented WIP block, `worldmap.txt:174`) — **teams come from the proto
  + the bound SCRIPT's `critter_add_trait`, never from worldmap.txt.**

Real early-game groups: `ARRO_Rats` (95% pid 16777227 `Script:617` + pig-rats +
Xander-root `If(Rand(5%))`, huddle), `ARRO_Sm_Scorpions` (`Script:616`),
`ARRO_Silver_Geckos` (`Script:615`), `ARRO_War_Party` (hunters w/ Sharp Spear pid
280 wielded + `Item:(3-6)320` ammo + `(0-10)41` caps, `Script:618`, wedge),
`DEN_Slavers` (Desert Eagle/Springer, `Script:508/628`, wedge),
`Bounty_Hunter_Low` (`Script:836`, `position=Surrounding, Spacing:2, distance:4` —
the classic ring ambush), `Special1` (`type_00=ratio:0%` — spawns NOTHING).

### The roll chain — `wmRndEncounterOccurred` (`worldmap.cc:3322-3522`), IN ORDER

1. **Real-time throttle** (`:3325`): `getTicksBetween(now, wmLastRndTime) < 1500`
   → no roll. *For determinism, replace with a per-N-steps cadence.*
2. **The Δ3 quirk** (`:3331-3337`, VERIFIED verbatim this session — adversarial
   verdict CONFIRMED): two SEPARATE early returns —
   `if (abs(oldWorldPosX - worldPosX) < 3) return 0;` **then**
   `if (abs(oldWorldPosY - worldPosY) < 3) return 0;` ⇒ effectively requires
   **|Δx|≥3 AND |Δy|≥3** since the last *encounter*. `oldWorldPos*` resets only
   after an encounter fires (`:3501-3502`), so straight axis-aligned travel
   (Δy≈0) NEVER rolls — a real engine quirk. Keep the |Δ|≥3 movement gate.
3. **Known-area suppression** (`:3340-3343`): standing on/near a city circle
   (`wmMatchWorldPosToArea != -1`) → never roll. KEEP (we know our circle positions).
4. **Horrigan** (`:3345-3361`, day>35) — **SKIP** (endgame; loop never reaches it).
5. **sfall forced encounter** (`:3367-3388`) — **SKIP**.
6. **Subtile lookup** (`:3391`), **daypart** (`:3393-3401`, VERIFIED:
   `hour>=1800 || hour<600 → NIGHT`, `hour>=1200 → AFTERNOON`, else `MORNING`).
7. **Frequency + difficulty skew** (`:3403-3414`, VERIFIED): `frequency =
   wmFreqValues[subtile.encounterChance[dayPart]]`; if `0<freq<100`, `modifier =
   freq/15`, Easy `-=` / Hard `+=` (Normal = no skew — **SKIP v1**).
8. **The roll** (`:3416-3419`, VERIFIED): `chance = randomBetween(0,100); if
   (chance >= frequency) return 0;`.
9. **Pick** (`:3421`, `wmRndEncounterPick`) — below.
10. **Special-circle pin** (`:3425-3443`) — **SKIP v1** (no special maps in the loop).
11. **Outdoorsman-avoid** dialog (`:3454-3519`) — **SKIP v1** (always-encounter;
    the prompt explicitly lists it skippable).

Return 1 → caller (`:3110-3119`): `wmFadeOut(); mapLoadById(encounterMapId);
break` — leave the loop, load the transient map (which runs map_enter then
`wmSetupRandomEncounter`).

### The weighted pick — `wmRndEncounterPick` (`worldmap.cc:3557-3654`)

Filter candidates by `wmEvalConditional != 0 && counter != 0` (`:3575-3581`),
sum their `Chance` weights, `chance = randomBetween(0, totalChance) + (Luck-5) +
perks + difficulty` (`:3589-3617` — Luck 5 = no shift; perks/difficulty **SKIP
v1**), walk-down subtracting each `Chance` until `chance < entry.chance`. Map =
`entry.Map:` if present, else random from `table.maps=`, else random from the
terrain pool (`:3640-3651`). v1 = `randomBetween(0, totalChance)` straight, walk-down.

**Condition operators — VERIFIED this session (adversarial verdict CONFIRMED):
ONLY `== != < >`** (`wmEvalSubConditional`, `worldmap.cc:4155-4169`) — no `<=`/`>=`.
Condition types (`wmEvalConditional`, `:4096-4152`): `Global(n)`, `Player(Level)`,
`Rand(n%)`, `time_of_day` (`gameTimeGetHour()/100`), `days_played`,
`enctr(num_critters)`. The early tables use only `Global`/`Player(Level)`/`Rand`/
`time_of_day` with `And` links — a faithful AND-only evaluator is exact for the
Arroyo→Den loop. (The engine's `Or`-link bug returns only the last sub-condition's
match — harmless here because no early table uses `Or`.)

### Script binding — `Script:N` is 1-based (VERIFIED, adversarial verdict CONFIRMED)

`wmSetupCritterObjs` (`worldmap.cc:3842-3848`, confirmed verbatim this session):
`scriptRemove` then `_obj_new_sid_inst(object, SCRIPT_TYPE_CRITTER,
encounterEntry->scriptIdx - 1)` ⇒ **`Script:N` is a 1-based scripts.lst line;
engine binds index N-1.** `Script:617` → scripts.lst line 617 = `ECRat.int`.
Hexwaste's `CreateObject(pid, tile, elev, scriptIndex = N-1)` + `AllocateSid`
(`IntVm.cs:1164`, `ScriptHost.cs:114`) is the identical party-member/created-object
path.

### Which early tables/groups to parse first

Tiles **0** (Arroyo, `art_idx=339`) and **1** (Klamath→Den corridor); tables
**6 Arro_M, 7 Arro_D, 8 Arro_O, 9 Arrok_D, 10 Arrok_M, 11 Kla_D, 12 Kla_M,
13 Klad_D, 16 Den_D, 17 Den_M**; groups `ARRO_*`, `ARROK_*`, `KLA_*`, `KLAD_*`,
`DEN_*`, `DMRV_*`, `Bounty_Hunter_*`, `Morton_Brother`, `Special1`. These cover
the entire canonical loop. `Den_D` sits in Tile 1 (`world_pos=473,272 →
272/300*4 + 473/350%4 = 1`).

---

## The EC\*.int missing-external census (the gate before any spawn work)

Ran the `OnStubbedExternal` audit over all 24 early-loop EC scripts (disassembled
operand-by-operand, external opcode union cross-referenced against the real
`IntVm` `case 0x80XX:` bodies — NOT just `ExternalArity.cs`, which declares arity
for ALL of them so an unimplemented external falls to the default arity-stub: pop
`Args`, push 0 if it returns, fire `_onStubbedExternal`, `IntVm.cs:1327-1340`).

**Verdict (adversarial verdict CONFIRMED): ZERO new externals are required to make
the Arroyo→Klamath→Den loop spawn, aggro, and fight.** The load-bearing externals
are all real: aggro = `obj_can_see_obj(self,dude)` (0x80DC, `IntVm.cs:1293`) →
`attack(dude,…)` (0x80D0, `IntVm.cs:1000`); team = `critter_add_trait(self,1,6,
team)` (0x8102, `IntVm.cs:991`); wander = `animate_move_obj_to_tile` (0x80CE) +
`tile_num_in_direction` (0x80D5) + `tile_distance_objs` (0x80D3) + `anim_busy`
(0x80E7) + `party_member_obj` (0x814B); wielded equip = the in-hand item flag
`0x01000000` (`MapFile.cs:129-133`, read by `EquippedWeapon`) — "MAP NPC weapons
just work" (phase-6 M4).

### Table A — first-loop CREATURE scripts (the level-1 Arroyo loop)

ECRat, ECScorp, ECGecko, ECPlant, ECWarPty, ECCanibl, ECHunter, ECNomad. Of 41
externals used, **5 missing — ALL cosmetic/degradable:**

| opcode | name | users | proc | effect if stubbed-to-0 | verdict |
|---|---|---|---|---|---|
| 0x810C | `anim` | ECRat/Scorp/Gecko | map_enter | `anim(self,1000,rand)` = spawn rotation (`objectSetRotation`, interpreter_extra.cc:3421) | **cosmetic** — fixed instead of random facing |
| 0x810E | `reg_anim_func` | ECRat/Scorp/Gecko | map_enter+wander | `!isInCombat()`-gated idle wander bracket | **cosmetic** — no combat effect |
| 0x8122 | `poison` | ECScorp | combat_p_proc | sting poison-over-time; sting DAMAGE still applies | **degradable** — no poison system anyway |
| 0x8151 | `critter_is_fleeing` | ECNomad | combat_p_proc | flee-bark; returns 0 = "not fleeing" → bark skipped; **actual min-hp flee is AI-packet-driven (phase-9 CombatEngine)** | **cosmetic** |
| 0x8154 | `debug_msg` | most | various | console spew | **no-op** (safe) |

### Table B — all 24 EC scripts (full early loop incl. caravan/trapper/robber)

70 externals used, **16 missing** — the 5 above plus 11 in `map_enter` (or
look_at) inventory-dressing / cinematic-fade paths: `elevation` (0x80EC),
`obj_is_carrying_obj` (0x80BA), `obj_carrying_pid_obj` (0x810D), `item_subtype`
(0x80C9), `critter_inven_obj` (0x8106), `wield_obj_critter` (0x80DA — REDUNDANT,
the in-hand flag equips before the script runs), `has_skill` (0x80AA),
`using_skill` (0x80AB), `gfade_out`/`gfade_in` (0x8136/0x8137), `obj_on_screen`
(0x8150). **Structural finding: ECTrappr (728-instr map_enter) and ECRobber
(4563-instr) have NO critter_p_proc/combat_p_proc at all — their entire logic is
spawn-time inventory dressing + examine flavor.** All 11 human-group misses live
in `map_enter`/`look_at`, NOT on any combat path. The misses only reduce
loot-variety and cosmetics.

### Recommendation

Ship M3 spawn **WITHOUT** implementing any of the 16; let `OnStubbedExternal` log
them during the golden-transcript encounter fixtures and confirm none desync the
stack (they can't — the default pops `Args` + pushes 0, all 16 arities correct in
`ExternalArity.cs`). Then implement opportunistically only if a fixture proves one
load-bearing. Cheap candidates if free: **`elevation`** (0x80EC, ~1 LoC
`PushInt(objectElevation)` — all start points are elev:0 so the gate already
passes — **do it, S**); **`obj_is_carrying_obj`/`obj_carrying_pid_obj`/
`critter_inven_obj`** (the Vic radio sub-quest wants these too — **fold into M4/M5
if implementing companion loot, S-M**); `anim`/`reg_anim_func` **DEFER** (engine
gates them `!isInCombat()`, so in an ambush-into-combat they often never fire).

`Bounty_Hunter_*` and `Morton_Brother` (Arro_D enc_04-12, bind ECBHuntr
`Script:836`) are **gated OUT for a fresh player** (require `Global(1)>1`
childkiller OR `Global(0)<-500` karma; defaults childkiller=0, karma=50) — NOT
first-loop, and add no missing external beyond Table B.

---

## Transient-map group spawn + formations (M3 content)

After the persistence guards land (top of M3), the spawn is the verified
`create_object_sid` path. Per `wmSetupRandomEncounter` (`worldmap.cc:3657`) →
`wmSetupCritterObjs` (`:3771-3909`):
- group size = `randomBetween(min,max)`; Easy −2 / Hard +2; **`_getPartyMemberCount
  () > 2` → +2** (`:3708-3711`) — the SAME `_getPartyMemberCount` the companion
  `metarule(16)` needs (ties Track C to Track E — implement it once, both use it).
- per `type_NN`: count = `ratio*group/100` (USE_RATIO) or exactly 1 (SINGLE),
  clamp ≥1; skip if `pid==-1` or its `If()` fails.
- `objectCreateWithPid` + `_obj_new_sid_inst(scriptIdx-1)` = Hexwaste
  `CreateObject(pid, tile, elev, N-1)` + `AllocateSid`.
- **team**: set `critter.Team = <nonzero>` at spawn or let the bound script's
  `critter_add_trait(self,1,6,team)` do it on first map_enter/heartbeat. In
  Hexwaste team 0 = the dude's team; any nonzero = hostile-eligible
  (`CombatEngine.cs:613` dudeTeamKill = `Team==0`).
- **wielded equip**: add the proto to inventory with the in-hand flag — the
  CombatEngine picks it up unchanged. No `_inven_wield` port needed for hostility.

**Formations** (`wmSetupRndNextTileNum*`, `:3911-4070`): **Surrounding** = ring
around the dude at `randomBetween(-2,2) + Perception` hexes (or entry `Distance:`),
the bounty-hunter/spore-plant/slave ambush; **straight_line/wedge/cone/huddle** =
cluster-with-spacing anchored on a random `random_start_point_N` from maps.txt;
**Dead** = corpse (the anim+28 path from P5-M3). `Hex.HexGrid.TileInDirection`/
`RotationTo` are 1:1 ports — the geometry drops straight on. Placement validity
gate (`wmEvalTileNumForPlacement`, `:4082`): unblocked AND reachable-from-dude
(A*); 25-retry cap.

**v1 minimal**: Surrounding = ring at Perception±2; everything else =
cluster-with-spacing around a random start point; Dead = corpse. desert1's
`random_start_point_N` (19086/17302/21315/22699/20526, confirmed this session)
parse with the existing MapList once `saved` + `random_start_point_N` are read.

**Who is hostile — script-side, not table-side (VERIFIED):** the parsed
AMBUSH/FIGHTING `situation` is consumed only for the 2nd group of an `X FIGHTING Y`
pair (`:3719-3741`); `X AMBUSH Player` hostility comes 100% from the bound EC
script's `critter_p_proc` (`obj_can_see_obj → attack`). **The existing
critter_p_proc heartbeat makes every AMBUSH encounter hostile for free.** Treat
`X FIGHTING Y` groups as different-team neutrals to the player for v1 (their
scripts engage each other if teams differ).

---

## Worldmap travel UI — the traveling dot + the return/resume seam (M1 + M3/M4)

### The grid already matches the engine

`WorldmapScreen` uses a 4-wide × 5-tall grid of 350×300 tiles (WorldWidth=1400,
WorldHeight=1500, `WorldmapScreen.cs:19-24`) = engine `WM_TILE_WIDTH=350`/
`WM_TILE_HEIGHT=300`/`num_horizontal_tiles=4` exactly. The renderer is already
grid-correct. city.txt `world_pos=x,y` are absolute worldmap pixels (Arroyo
184,133 / Klamath 373,122 / Den 473,272) — the same space as the engine's
`worldPosX/Y` and our `WorldArea.WorldX/Y` (`CityList.cs:20-21`). The dot lives in
pixel space directly — no new transform.

Subtile lookup (`wmFindCurSubTileFromPos`, `worldmap.cc:3533-3543`):
`tileIndex = y/300*4 + x/350%4; column = y%300/50 (0..5); row = x%350/50 (0..6);
subtiles[column][row]`.

### The traveling dot — Bresenham over the existing UI

`wmPartyInitWalking` (`worldmap.cc:4266-4309`) is a classic integer Bresenham line
from `worldPos` to the clicked point; `wmPartyWalkingStep` (`:4312-4383`) is one
increment per call, gated by a terrain-difficulty divisor (Mountain steps less
often). **One step = one pixel along the line** (Δ3 is measured in those pixels).
Per step the loop calls `wmGameTimeIncrement(18000)` = **30 game-minutes**
(`GAME_TIME_TICKS_PER_HOUR=36000`, so 18000 = 0.5 h). Our `GameClock.TicksPerHour
= 36000` (`GameClock.cs:12`) is identical, so `_clock.Ticks += 18000` per dot-step
== +30 game-min exactly.

Minimal deterministic design (keep it in `WorldmapScreen` + a thin ViewerGame
driver): dot state (`_pos`/`_dest`/`_isWalking` + Bresenham accumulators, port
`wmPartyInitWalking` 1:1); step from the fixed update (NOT real-time — the 1500 ms
throttle is a UI-frame artifact); per-step roll **hook** (`Func<int x, int y,
EncounterResult?>` injected from ViewerGame — Track A's chain behind it); arrival
matches the dot to a circle and runs the existing enter-town path; render the dot
via the existing `_marker` texture. **Determinism:** the walk is pure integer
Bresenham; the only randomness is the roll, which seeds off the same
`ICombatRng`/`SystemCombatRng(seed)` as combat. **DEFER**: terrain-difficulty
slowdown + the `wmWorldPosInvalid` walk-mask halt (start difficulty=1, no mask —
cosmetic, since encounters come from the subtile table not the mask).

### Return / resume — nearly free (the cheapest win)

Encounter maps are ringed on every edge with exit grids whose `Destination.Map ==
-2` (audited: desert1 374×, mountn1 38×, city1 434×). `mapHandleTransition`
(`map.cc:1233-1254`): `map == -2 → wmWorldMap()` (re-enters the worldmap loop),
`map == -1 → wmTownMap()` (treat -1 as -2 for v1 — no town-map sub-screen). On
re-entry `wmWorldMapFunc` reads `isWalking` at the loop top and, if still true,
keeps stepping toward the SAME destination — **resume is automatic because nothing
cleared the ambient `wmGenData` state.**

Hexwaste already does 80%: `MapFile` reads the exit-grid `map` as raw Int32
(`-1`/`-2` flow into `Destination.Map`, `MapFile.cs:495`); `CheckExitGridAt`
queues `_pendingTransition` (`ViewerGame.cs:2959`); `ApplyTransition` branches
`Map < 0 → _worldmapOpen = true` (`ViewerGame.cs:2910-2914`). So walking off an
encounter edge ALREADY returns to the worldmap. For resume, since `WorldmapScreen`
persists across `LoadMap` (created once at startup, `ViewerGame.cs:403`), the dot
state is **already ambient across the detour — same as the engine's `wmGenData`.**
The only care: distinguish "left a town on foot" (stop at the circle) from
"returned mid-walk" (resume) — the dot's own `_isWalking` flag is that
discriminator. **S, ~30 LoC.**

### The phase-9 projectile-tween loose end — DOES NOT fit M1 (honest non-fold)

The prompt asks whether the thrown-projectile tween folds into M1's Bresenham
work. **No.** The worldmap dot tweens in **2-D worldmap-pixel space** (integer
Bresenham over a flat bitmap, no screen interpolation). The projectile is
`animationRegisterMoveToTileStraight` — a **hex-grid** straight walk + per-frame
*screen* lerp in the combat viewport (a different coordinate system, a different
host: `ObjectAnimator`, which today only plays per-frame FRM offsets, no position
lerp). Shared concept ("advance a point along a line per tick"), ~0 shared lines.
**Keep it OUT of M1.** It belongs with a combat-presentation milestone or stays
deferred. Folding it in would mean building a hex-screen interpolator M1 doesn't
otherwise need — scope creep.

---

## The companion fold-in (M4–M5 — cheap + reusable only)

### metarule(16) PARTY_COUNT — port `_getPartyMemberCount` (S, ~3 LoC)

`_getPartyMemberCount` (`party_member.cc:900-913`, confirmed verbatim this
session): `count = gPartyMembersLength; for index 1.. decrement if non-critter ||
dead || hidden; return count` — i.e. `1 + (live, visible, recruited critters)`
(slot 0 = dude). Dispatch: `case METARULE_PARTY_COUNT (16)` (`interpreter_extra.cc:
3219`).

**CORRECTION (adversarial verdict, was "PARTIAL"):** the prompt called this
"stubbed" — it is genuinely **unimplemented**, returning 0 today.
`ScriptHost.Metarule` (`ScriptHost.cs:798-804`) handles only rule 14 (FIRST_RUN)
and rule 49 (WEAPON_DAMAGE_TYPE); everything else `_ => 0`. The port (our roster
holds only recruited members; the dude is NOT in it, so add the implicit +1):
```csharp
16 => 1 + _host.PartyMembers.Count(m =>
        Fid.PidType(m.Pid) == (int)ObjectType.Critter && !m.IsDead && !m.IsHidden),
```
`MapObject` already exposes `IsDead`/`IsHidden`/`Fid.PidType` — no new plumbing.
**Load-bearing:** dcVic uses `metarule(16)-1` at four sites (0x18ba/0x190a/0x1948)
for the party-size gate (`>= floor(CHA/2) + has_trait(98)`). With our stubbed 0,
every gate computes `0-1 = -1 >= floor(CHA/2)` **never** → the "party full" refusal
NEVER fires. Default-0 doesn't *block* the join (so it's not a hard bug today), but
it's wrong and trivial to fix. The encounter spawn (+2 if party>2) reads the same
function — implement once, both tracks use it.

### Dismiss / rejoin + follow audit (S, ZERO new VM LoC)

Following is 100% script-side: Hexwaste runs one `critter_p_proc` per tick
round-robin (`ViewerGame.cs:2326`), exactly as the engine. The follow loop's every
external is already real and non-stub (operand-walk of kcsulik `[0x25a4..0x29b8]`
and dcVic `[0x1478..0x1840]`): `get/set_local_var`, `get_global_var`,
`tile_distance_objs`, `tile_num_in_direction`, `rotation_to_tile`, `anim_busy`,
`opAnimateMoveObjectToTile`, `reg_anim_func`, `party_member_obj` — all dispatched.
**LVAR map (verified): Sulik wait=LVAR[11] dist=LVAR[12]; Vic wait=LVAR[5]
dist=LVAR[6]** (default 6) — different indices per script, but our VM reads them
generically via `get/set_local_var`, so no per-script knowledge is needed.

Dismiss/rejoin are pure dialog nodes using already-real externals: Sulik REJOIN
Node800 (`critter_state` alive-gate, reset dist, clear wait, `critter_add_trait
(self,1,6,0)`, `party_add`), Sulik DISMISS Node1002 (`LVAR[11]=game_time()` wait,
`party_remove`); Vic JOIN Node994, Vic DISMISS Node1002 (`critter_add_trait
(self,1,6,25)` — **team 25 = Vic's original DEN team**, the literal "team 25" the
prompt asked about; Vic-specific hardcoded, vs Sulik restoring saved LVAR[13]).
**Zero new externals.** The only new work: bind the dismiss/wait/follow-distance
options onto the in-party gsay hub + resolve **partymbr.msg id 14** (ids
10001-10010 "wait here"/"follow at medium range"/etc.) or the option text renders
blank.

**The one real follow-loop risk is a TEST, not a feature:** verify the wait LVAR
(Sulik [11] / Vic [5]) and `GVAR[398]` halt/resume the follow loop across a
critter_p_proc tick AND a map transition (party LVAR carry, phase-7 M4). A
golden-transcript fixture under `--rng-seed`.

### 1:1 companion-inventory trade panel (M-, ~120 LoC viewer)

Engine: party-member TRADE ('d') is a **flat move, NOT priced barter**
(`game_dialog.cc:3757`): `gGameDialogBarterModifier` is reset to 0 at init
(`:726`) and only changed by the shop-barter path; party TRADE trades at modifier
0 → items move 1:1, no caps. **Bypass `_gdCanBarter`/CRITTER_BARTER entirely** (it
only matters for the priced-barter speaker we are not building).

Reuse Hexwaste's loot panel: point `_lootContainer` at the follower's
`MapObject.Inventory` — `TakeFromContainer` (`ViewerGame.cs:1748`) is already
generic, so take-from-companion works unchanged. The only genuinely new transfer
is a **give-to-follower** drop variant (`follower.Inventory.Add(item)` instead of
the map floor, ~15 LoC). Optionally route `move_obj_inven_to_obj` (0x8147, already
real `IntVm.cs:1135`) as the move primitive. Equip-best ('w'/'a') is optional
polish — **skip** (we equip via item flags). **Zero new engine externals.**

### Vic's radio rescue — DEFER (content, not lifecycle)

The cash/dismiss/trade lifecycle is the right cut. Vic's **legitimate cash rescue
needs ZERO new externals** (Metzger $1000 path; dcVic reads the bit) — but it's
*content*, not lifecycle, so it doesn't belong in this fold-in. The **radio leg**
needs two currently-stubbed inventory-query externals (`obj_is_carrying_obj`
0x80BA `ExternalArity.cs:44`, `obj_carrying_pid_obj` 0x810D `ExternalArity.cs:127`
— both hit `OnStubbedExternal` and return 0) plus a multi-node dialog leg (msg
163/174/177) that is Vic-specific content. Each external is ~10 LoC, but the
sub-quest they unlock is a 5-milestone vertical slice in its own right
(p8-track-b). **Wire the reusable pieces now (metarule 16, dismiss/rejoin, trade —
they benefit ANY companion incl. encounter-spawned allies and the `--recruit` test
plumbing); leave Vic's radio for a dedicated phase.**

---

## The M0..M5 milestone plan

Each milestone is demoable + headless-testable (extend the harness; deterministic
under `--rng-seed`). "DEFER if absent" gates are explicit.

### M0 — hygiene + persistence pre-stage (S, the net)

- **SCOPE.md refresh** (it lists aimed/crits/throwing/explosives as "out" but
  phase 9 shipped them — only burst remains deferred).
- **MapList learns `saved` + `random_start_point_N`** (`MapList.cs:32-46` reads
  only `map_name`/`lookup_name`/`music` today — the prompt-flagged gap). Add a
  `Saved` bool + the start-point list. Verified present on desert1 (`saved=No`, 5
  start points, confirmed this session).
- **Transient-flag plumbing** (no behavior change yet): thread `bool transient`
  into `LoadMap`, wire the three persistence guards behind it (skip-read /
  skip-write / firstRun=1), with NO transient map reachable yet.
- **The persistence tests A + B** (above) land here as currently-passing baselines.
- **Demo/headless:** `[Fact]` `TransientMapTakesNoDeltaSlot…` green; `--bench`
  baseline re-run (denbus2) to confirm no regression.

### M1 — the worldmap parser + roll/pick chain + the traveling dot (M-L, the spine)

- **`WorldmapFile.cs`** pure parser: `[Data]` freqs, `[Tile N]` 7×6 subtile grid
  (`subtiles[column 0..5][row 0..6]`, the load-bearing index — `worldmap.cc:
  64-65,372,1943-1967`), `[Encounter Table N]` (Chance/Counter/Map/Enc/If,
  `worldmap.cc:1367,1429`), `[Encounter: GROUP]` (ratio-or-SINGLE/Dead/pid/Items+
  wielded/`Script:N`→N-1/formation, `worldmap.cc:1611,1681`). **DEFER if absent:**
  `Fill`/`Scenery` (cosmetic), `team_num` (engine-UB — do NOT parse).
- **The roll/pick port** (`wmRndEncounterOccurred` `worldmap.cc:3322-3522`,
  `wmRndEncounterPick` `:3557-3654`, `wmEvalConditional` `:4096-4169`): Δ3 gate +
  known-area suppression + daypart freq + `randomBetween(0,100) < freq` + summed-
  Chance weighted walk + AND-only condition eval (`== != < >` only). Seed off
  `ICombatRng`. **DEFER if absent (all confirmed skippable):** Horrigan, sfall,
  outdoorsman-avoid dialog, special-circle pin, perks/Luck/difficulty skew.
  Replace the 1500 ms wall-clock throttle with a per-N-steps cadence.
- **The traveling dot** (`wmPartyInitWalking`/`wmPartyWalkingStep` `worldmap.cc:
  4266-4383`, integer Bresenham, ~50 LoC) + subtile lookup (`:3533-3543`) +
  `_clock.Ticks += 18000` per step + the per-step roll **hook**. **DEFER if
  absent:** terrain-difficulty slowdown + walk-mask halt (start difficulty=1).
- **Demo/headless:** `--rng-seed S --travel-to X Y --walk-steps N` walks N pixel-
  steps, rolls each, prints the chosen map + group on an encounter — a golden
  transcript like combat. A `[GameDataFact]` asserts a known seed → a known
  encounter sequence on Tile 0 (`Arro_D`/`Arro_M`).

### M2 — *(fold point; the spine is M1+M3 — M2 is the additive-V2 save + counters)*

- **Additive-V2 save** (NO V3 bump): `WorldPosX`/`WorldPosY`/`CurrentAreaId` +
  per-table `Counter` dict on `SaveState` (currently `CurrentVersion=2`,
  `SaveState.cs:20`; no WorldPos fields yet). The engine saves exactly these
  (`wmWorldMap_save`) and does NOT save mid-walk destination, so a loaded
  mid-travel save leaves you stopped at `worldPos` — match that. Quicksave on a
  transient map writes worldpos + returns to the worldmap on load (the documented
  divergence). **DEFER if absent:** per-subtile fog state (no fog UI).
- **Demo/headless:** save mid-travel, reload, assert the dot is stopped at the
  saved `worldPos`; assert a decremented one-shot `Counter` survives the round-trip.

### M3 — transient-map load + group spawn (M, the payoff)

- **The persistence guards go live** (the three clauses + tests B green — this
  GATES the rest of M3). The encounter roll now loads a real transient map.
- **Group spawn**: parse the picked group's `type_NN`, `CreateObject(pid, tile,
  elev, N-1)` + `AllocateSid` per critter (`worldmap.cc:3771-3909`); team via the
  bound script's `critter_add_trait`; wielded weapons via the in-hand item flag.
  **Formations**: Surrounding = ring at Perception±2 around the dude; else =
  cluster-with-spacing at a random `random_start_point_N`; Dead = corpse
  (`worldmap.cc:3911-4070`). **DEFER if absent:** the 25-retry placement-validity
  loop (use a simple unblocked+reachable check).
- **EC\*.int spawn**: implement NONE of the 16 missing externals; run the
  `OnStubbedExternal` audit during the golden fixtures to confirm none desync. Add
  `elevation` (0x80EC, ~1 LoC) only if a fixture needs it.
- **The unreachable-joiner non-termination** (phase-9 spillover) bites harder here
  — a cornered Surrounding ring is exactly the case. **M3 should bound combat-end**
  when a hostile is unreachable (the phase-9 `_combat_should_end` divergence). Fold
  this fix into M3.
- **Demo/headless:** `--rng-seed S` travel → encounter → assert an ARRO_Rats /
  DEN_Slavers group spawns at the start point, aggros via the heartbeat, and the
  CombatEngine resolves it; a wounded pack scatters (min_hp flee) for free.

### M4 — return/resume seam + the companion lifecycle (S)

- **Return/resume** (cheapest win): exit-grid `Map==-2 → _worldmapOpen` already
  fires; treat `-1` as `-2`; the dot's `_isWalking` discriminator resumes the walk
  (`worldmap.cc:2971`, `map.cc:1233-1254`; ambient `WorldmapScreen` state). ~30 LoC.
- **metarule(16)** port (`party_member.cc:900`, ~3 LoC) — implement first; shared
  with the spawn +2 gate.
- **Dismiss/rejoin/wait** dialog-hub binding (Sulik Node800/1002, Vic Node994/1002;
  team 25 for Vic) + resolve partymbr.msg id 14. Zero new externals.
- **The follow-loop audit fixture** (the one companion risk): wait LVAR + GVAR[398]
  halt/resume across a tick AND a map transition.
- **Demo/headless:** recruit → "wait here" (stops) → "follow me" (resumes) →
  dismiss (party_remove, team restored) → rejoin (alive-gated); assert
  `metarule(16)` reports N+1 and the dcVic party-size gate refuses at full party.

### M5 — companion trade panel + loose-end sweep (M-)

- **1:1 trade panel**: point `_lootContainer` at the follower's `Inventory`; add
  the give-to-follower drop variant; bypass `_gdCanBarter`/CRITTER_BARTER; "Trading
  with NAME" header. ~120 LoC viewer, zero new externals.
- **Loose-end sweep:** verify recoverable thrown-weapon persistence across
  save/travel while testing M3 loot (phase-9 loose end); confirm the dominant
  encounter-map perimeter grids are `-2` not `-1`. The projectile screen-tween is
  explicitly **NOT** folded here (different coordinate axis).
- **Demo/headless:** open companion trade, move an item each way, assert both
  inventories + NO caps changed (flat 1:1); the moved item survives save/load + a
  map transition (the follower travels outside map deltas).

---

## Pivot thresholds

- **M0:** if the MapList parse or the transient-flag plumbing destabilizes an
  existing test, isolate per-commit — the persistence tests are baselines, so any
  regression is a one-commit bisect. Persistence resolution is LOW risk; if it
  somehow isn't, it blocks ONLY M3, not M1/M2.
- **M1:** if the full `[Tile]`/`[Encounter Table]` parser runs long, ship Tiles 0+1
  + tables 6/7/16 only (the Arroyo/Den hot cells) and widen later — the parser is
  the long pole, the roll chain is small. If the dot's terrain/walk-mask details
  fight, they are already DEFERRED (difficulty=1, no mask).
- **M3 (the gate):** if either persistence test fails, the spawn is built on
  corruptible state — STOP and fix the guards before any spawn code. If the
  Surrounding-ring reachability + unreachable-joiner interaction is flaky, ship
  cluster-only formations (skip Surrounding) + the combat-end bound first.
- **M4–M5:** independent and last — cut M5 (trade) if the phase runs long; the
  dismiss/rejoin lifecycle (M4) is the higher-leverage companion piece. metarule(16)
  is non-negotiable (3 LoC, shared with spawn).
- **Vic's radio:** stays deferred; do not wire 0x810D/0x80BA until a dedicated Den
  vertical-slice phase.
- **Higher-leverage alternative cut?** Considered and REJECTED:
  *encounters-only* (drop the companion fold-in) loses two genuinely-cheap,
  broadly-reusable pieces for no risk reduction; *Vic-as-the-whole-phase* is a
  5-milestone content slice that spends the phase on one NPC — the standing
  decision (encounters + the cheap fold-in) is correct.

## UNVERIFIED / honest flags

- **The 1500 ms real-time throttle** (`worldmap.cc:3325`) is wall-clock; a
  deterministic per-step cadence is a documented divergence, not a port —
  UNVERIFIED what step count best matches the original feel (the engine's is
  FPS-dependent anyway). `StepsPerSecond` is a design choice tuned in-app.
- **The `wmEvalConditional` Or-link bug** (returns only the last sub-condition's
  match) is real but harmless — NO early table uses `Or`. UNVERIFIED whether any
  shipping table anywhere uses `Or` (not checked beyond the early loop); a "correct"
  AND/OR evaluator would diverge from the engine there.
- **Per-subtile `Counter` persistence across saves** (`worldmap.cc:1117-1131`, the
  save side) was not re-read — only the in-memory decrement (`:3636-3638`) + the
  `counter==0` filter (`:3579`) were confirmed. The additive-V2 `Counter` dict is
  the design; the exact engine save-stream layout is irrelevant (we store our own).
- **Save-while-on-transient exact engine bytes** (`_map_save_in_game(false)`
  else-branch payload) — control flow confirmed (`map.cc:1456`), byte format not
  traced; irrelevant since we take the documented worldpos divergence.
- **EC\*.int human-group classification** (ECTrappr 728-instr / ECRobber 4563-instr
  map_enter) — the 11 human-group missing externals' CLASSIFICATION is inferred
  from proc location + arity + sampled call sites, not a full operand-by-operand
  trace of those two giant map_enter bodies. The load-bearing claim (no
  combat-path miss) is solid: those scripts have no combat/critter proc at all.
- **`team_num` engine UB exact value** — confirmed it's read uninitialized on a
  missing key; the precise stack-leftover value was not traced (and is irrelevant,
  the EC scripts overwrite team via `critter_add_trait`).
- **partymbr.msg id 14 strings** (10001-10010) — the `message_str(14, 1000N)` calls
  are confirmed in the disassembly, but the strings were not extracted to the byte.
  If they render blank in-app, the cause is the unresolved message file, not the
  dialog logic.
- **Party LVAR carry for a WAITING companion across a map transition** — the
  follow externals are all real, but a save/transition probe of a waiting
  companion's wait/distance LVAR was not run. This is the M4 audit fixture and the
  single real follow-loop risk.
- **Terrain `difficulty` integers per terrain** (`wmTerrainTypeList[].difficulty`)
  — the Mountain slowdown is real (`worldmap.cc:4326-4333`) but the per-terrain
  numbers were not transcribed (DEFERRED anyway; start at 1).
- **Projectile-tween non-fold** — assessed as a different coordinate axis/host;
  UNVERIFIED that a future combat-presentation milestone wouldn't want a shared
  "advance-along-line-per-tick" helper (they share ~0 lines today).
