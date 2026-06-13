# Research Prompt — Hexwaste Phase 10: The Wasteland Bites Back

> Paste everything below this line into Claude Desktop (enable web search).
> Phases 2–9 were researched with the empirical method (disassembling real
> game scripts operand-by-operand, parsing real protos/maps/txt tables to the
> byte, transcribing engine tables row-by-row, running headless playthrough
> probes and golden transcripts, citing fallout2-ce file:line for every engine
> claim) and then built exactly as recommended. Same bar here. If a question can
> be answered by reading source, parsing a real table, disassembling a script,
> or attempting the interaction headless, demand that method over citation from
> memory.

---

I have a C#/MonoGame re-implementation of a Fallout 2 engine slice (**Hexwaste**,
tagged v0.9.0) whose opening hour now plays with **deep combat**: an extracted
engine-free `CombatEngine`, AI behaviour packets (close-or-flee), criticals + aimed
shots off the real crit tables, knockback + persisting knockdown, area explosions,
and throwing. The decision for Phase 10 is made: **Random Worldmap Encounters** —
the first true traveling worldmap, where Phase 9's combat depth finally gets used —
**with the cheap, broadly-useful companion-lifecycle pieces folded in as M4–M5**
(rather than spending a whole phase on a single rescued NPC). What I need
researched is the empirical detail to build it without guessing: the exact
`worldmap.txt` table semantics + roll/pick chain, the **transient-map persistence
question** (the one real unknown), the encounter-script external census, and the
small set of companion externals/dialog nodes that ride along.

## Current state (all done — 182 xUnit Formats tests, committed, v0.6.0–v0.9.0; phase-9 report fully implemented)

- **C# / .NET 10 + MonoGame DesktopGL** (3.8.4.1 pinned). `Hexwaste.Formats`
  (parsers, hex/A*/LoF, lighting, micro INT VM + script host, the engine-free
  `Combat/CombatEngine` + all combat math, GCD sheets, worldmap/city/maps tables,
  save model — headless-tested) + `Hexwaste.Viewer`. Release pipeline proven; the
  public git push/tag is the user's (docs/RELEASING.md).
- **Combat (phase 9)**: `CombatEngine` behind `ICombatHost`/`ICombatRng` — a
  no-behaviour-change extraction locked by an 11-fixture golden-transcript harness
  (`scripts/combat-golden.sh`) + fake-host CI unit tests. AI packets
  (`Formats/Combat/AiPackets`, `data\ai.txt`), criticals (generated 1080-row table,
  day≥2 gate), aimed shots (V / `--aim`), knockback + prone (+40/3-AP get-up),
  `CombatEngine.Explode` AoE, `TryThrow` (Throwing skill, recoverable land,
  grenade→Explode + the misc-10 marker + `metarule(49)`). Harness: `--fight/
  --attack/--throw/--explode/--aim/--rng-seed/--advance-days/--give/--use-item`,
  all transcripted with a clean headless exit.
- **World/scripts (phases 1–7)**: DAT2/FRM/PAL/MAP/PRO, AAF fonts, static lighting +
  day/night clock, ACM sound, the micro INT-bytecode VM + script host (~70 real
  externals, `map_enter`, `gsay` dialog, locks, `use_obj_on`, spatial traps,
  `create_object_sid` script binding, the `critter_p_proc` heartbeat, cross-script
  externals), **worldmap click-to-travel between named areas** (`maps.txt`/
  `city.txt` lookup), per-map persistent deltas keyed by load-order ordinals,
  versioned JSON save/load, a minimum party member (`party_add/remove`, followers
  travel across maps with per-map follow-script re-binding).
- **Worldmap data on hand**: `worldmap.txt` (291 KB) is in the repo and **decoded
  in phase-7 track B** (subtile grid → terrain/daypart freqs → encounter tables →
  weighted entries; cadence 1 step = 30 game-min). Its section census: **76
  `[Encounter Table N]`, 20 `[Tile N]` (6×6 subtiles), `[Random Maps: Ocean/
  Mountain/Desert/City]`, and per-group `[Encounter: GROUP]` blocks.** `MapList`
  parses `maps.txt` for travel but does **not** yet read the `saved=No` flag or
  `random_start_point_N`.
- **Known gaps, on record**:
  - Random encounters: **never built.** worldmap.txt decoded but UNVERIFIED:
    `saved=No` semantics rest on one parsed map; the transient-map ↔ our
    load-order-ordinal delta keying is the single real unknown; the encounter
    scripts (EC\*.int) may reference externals we haven't implemented.
  - Companions: `--recruit` is test plumbing; the LEGITIMATE recruitments are gated
    (Sulik = $350 / Maida GVARs, Vic = radio + Metzger payment). No
    trade-with-follower, no dismiss/rejoin dialog path, no `wait`. `metarule(16)`
    PARTY_COUNT (`_getPartyMemberCount`) is stubbed to 0. The radio externals
    `0x810D`/`0x80BA` have no `IntVm` body.
  - Phase-9 loose ends (fold in opportunistically): the thrown projectile flies via
    the throw anim (no tweened sprite); recoverable thrown-weapon persistence
    unverified across save/travel; the artemple door-blast beat is wired but
    unverified; `CombatShouldEnd` is "any hostile standing" vs the engine's
    team+whoHitMe; a pre-existing **unreachable-joiner** non-termination (a hostile
    with no path the dude can't reach keeps combat from ending). Burst is DEAD
    (zero burst weapons reach the player in the slice).
  - **`SCOPE.md` is now stale** — it lists aimed/crits/throwing/explosives as "out",
    but phase 9 shipped them; only burst remains deferred. Fix in M0 hygiene.

## Constraints

- Hobby project, AI pair programming; milestones demoable + headless-testable
  (extend the harness; deterministic under `--rng-seed`).
- Legal: SUL conditions stand; no assets ever; the publish/tag is the user's.
- C#/MonoGame stays; fallout2-ce in `reference/` is the spec; **port — don't
  invent**. No custom shaders (BasicEffect only).
- **Hard scope line:** encounters spawn-and-fight using the existing CombatEngine;
  NO new combat features. Companion work is the *fold-in* — keep it to the cheap,
  reusable pieces, not a full quest VM.

## Directions to research

1. **worldmap.txt table semantics + the roll/pick chain (the spine).** Confirm the
   `[Tile N]` 6×6 subtile layout (terrain + daypart encounter frequency + the
   `encounter_type`/table pointer), `[Encounter Table N]` entries (weighted
   `Chance`, one-shot `Counter`, conditional gates — what operators?), and
   `[Encounter: GROUP]` blocks (`pid:ratio` composition, `Single`/`Dead`/`item`/
   `Script`/formation). Then the **roll order** in fallout2-ce `worldmap.cc`
   (`wmRndEncounterOccurred`/`wmEncounterTypeLookup`/`wmPartyFindCurSubTile`): the
   Δ3-quirk after a prior encounter, "special encounter" circle suppression near
   known areas, daypart frequency, difficulty skew, the weighted-entry pick, and
   condition evaluation (vs GlobalVars / player level / clock hour / `Rand%`).
   What can v1 skip (Horrigan, car, outdoorsman-avoid, perks/luck) without breaking
   the canonical Arroyo→Klamath→Den loop? Cite line ranges; name the exact
   `[Tile]`/`[Encounter Table]` the early-game tiles use.
2. **THE transient-map persistence question (the one real unknown).** A `saved=No`
   encounter map must NOT take a delta slot (it's regenerated each visit). Confirm
   in `map.cc`/`worldmap.cc` what `saved=No` + the random-encounter load actually
   do to the save state. Then the integration design for Hexwaste's **load-order
   ordinal** delta keying (phase-5 M1): does re-entering `desert1` twice corrupt
   ordinals, or does skipping `VisitedMaps` for transient maps on BOTH exit and
   entry (firstRun=1 always, but still run `map_enter`) suffice? Name the exact test
   (two `desert1` re-entries must not collide with a real map's ordinal). This is
   the milestone-gating risk — resolve it first.
3. **Transient-map load + group spawn.** The `random_start_point_N` placement
   (parse from `[Random Maps: TERRAIN]` / maps.txt), spawning a group via
   `objectCreateWithPid` + our `ScriptHost.AllocateSid` (the verified party-member
   init path), equipping wielded weapons (the MAP-NPC equip flags from phase-5/7),
   team assignment, and **formations** (`Surrounding` = ring around the dude at
   Perception±2; cluster; `Dead` = spawn as corpses). Which encounter groups does
   the early loop actually use (ARRO\_/KLA\_/DEN\_ \* — real pids), and do their
   `Script=EC*` scripts reference externals we lack? **Run the `OnStubbedExternal`
   audit over the EC\*.int scripts and produce the missing-external census** before
   M3.
4. **Worldmap travel UI + return/resume.** The traveling-dot Bresenham walk over the
   existing city-circle UI (per tick: +30 game-min via `GameClock`, subtile lookup,
   roll); arrival reuses the enter-town path; an encounter exit grid
   (`Destination.Map = -2`?) restores the saved worldmap position and auto-resumes
   the walk. Confirm the engine's worldmap-position resume + how an encounter map's
   edges let you leave (exit grids on all edges? walk-off?). What's the minimal
   deterministic version?
5. **The companion fold-in (M4–M5 — cheap + reusable only).** Empirically scope the
   *small* pieces: (a) `metarule(16)` PARTY_COUNT via `_getPartyMemberCount`
   (interpreter_extra.cc — port it, it's trivial and load-bearing for party
   dialog); (b) **dismiss/rejoin** — disassemble the Vic/Sulik dialog nodes that
   `party_remove` + re-add (team 25?), gated on `critter_state` alive, and the
   `wait`-LVAR / follow-distance-LVAR in the follow loop (**audit our follow loop
   here — it's the one companion risk**); (c) a **1:1 companion trade panel**
   reusing the loot panel + `move_obj_inven_to_obj` against the follower's own
   Inventory (flat moves, NOT priced barter — bypass `_gdCanBarter`/CRITTER_BARTER).
   Is "Vic's rescue legitimately" worth wiring the radio (`0x810D`/`0x80BA`) in this
   phase, or is the cash/dismiss/trade lifecycle the right cut (Vic's actual rescue
   = a later phase)? Recommend the M4–M5 split.
6. **Cross-cutting + loose ends.** Save format: worldmap position (WorldPosX/Y,
   CurrentAreaId) + per-table `Counter` persistence — plan ONE additive-V2 bump or
   argue it fits. Disallow quicksave on transient maps (save returns you to the
   worldmap — confirm the engine does this). Which phase-9 loose ends fold in cheaply
   (projectile tween rides M1's Bresenham/tween work; recoverable-persistence
   verified while testing M3 loot; SCOPE.md refresh in M0)? Re-run `--bench` if a
   busy encounter spawn is cheap to measure.
7. **Anything we're not seeing** — push back if a different Phase-10 cut (e.g.
   encounters-only, or Vic-as-the-whole-phase) is higher-leverage, or if the
   transient-map risk is bigger than estimated.

## Cross-cutting

- **The roll chain must be deterministic** under `--rng-seed` (reuse `ICombatRng`-
  style seeding for the worldmap RNG) so encounters get golden-transcript fixtures
  like combat did.
- **Spawned groups fight through the existing CombatEngine** — no combat changes.
  The win condition: a spawned bounty-hunter group is a coordinated threat (AI
  packets) and a wounded pack scatters (min_hp flee) for free.
- The unreachable-joiner non-termination (phase-9 spillover) will bite harder once
  encounters spawn groups that may be cornered — note whether M3 should fix it.

## Deliverable

Comparison/sizing per area (effort / payoff / risk / content-in-slice), the
resolved **transient-map persistence design** (the gating question, with the exact
test), an M0..M5 milestone plan (each demoable + headless-testable, with specific
`worldmap.cc`/`map.cc`/`interpreter_extra.cc` file:line cites and explicit
"DEFER if absent" gates), the EC\*.int missing-external census, pivot thresholds,
and explicit UNVERIFIED flags. Where a direction depends on script/table behaviour,
name the exact scripts to disassemble and `[sections]` to parse and what to look for.
