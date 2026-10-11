# fo2ce trace oracle — design

Date: 2026-10-10
Status: approved in brainstorming, awaiting spec review

## Goal

Test large parts of the game for exactness against fallout2-ce, not just against Hexwaste's own
past behaviour. The existing golden suites are regression nets: they prove Hexwaste did not
*change*, not that it *matches*. The screenshot comparison pilot (`scripts/fo2ce-control.sh`,
`docs/fo2ce-comparison-playbook.md`) catches visual drift but not computed-differently-looks-same
logic drift.

The oracle runs a **trace-patched vanilla fallout2-ce (`alexbatalov e97087b`)** and Hexwaste from
the **same vanilla save**, with the **same RNG algorithm and seed**, through the **same scenario**,
and diffs their event traces. The first diverging line is the bug.

Chosen in brainstorming:
- Comparison model: **lockstep traces (A) with deterministic values (B) riding along** — the trace
  carries hit chances etc., so formula drift shows even when rolls agree.
- Shared starting state: **vanilla save files, read-only in Hexwaste.** fo2ce is the only writer.
- Scenario driving: **semantic action list** interpreted by both engines (no UI/input injection),
  **milestone 1 = passive-only** (`wait`, `end-turn`).
- fo2ce side: **source patch + offscreen build** (not gdb/LD_PRELOAD — the only prebuilt binary is
  the stripped, non-vanilla community fork).

## Grounding facts (verified in this session)

- fo2ce has **one global RNG stream**: `randomBetween()` (`src/random.cc:134`) → `getRandom()`
  (`:153`), Park-Miller minimal standard (`16807`, `127773`, `2836`) with a 32-entry shuffle table
  (`_iv[32]`, `_iy`, `_idum`), seeded via `randomSeedPrerandomInternal()` (`:192`) from
  `std::rand()`. ~80 `randomBetween` call sites (combat 15, worldmap 14, combat_ai 10, skill 9, …,
  animation 2, game_sound 1, map 1).
- **Vanilla saves do not persist RNG state**: `randomSave()`/`randomLoad()` just call
  `_roll_reset_()` (`random.cc:71-80`). The seed must come from outside the save.
- Hexwaste has **several RNG streams**: `ICombatRng`/`SystemCombatRng` (`System.Random`, combat +
  travel + encounters), the script VM's own `Random` reseeded per proc (`IntVm.cs:717`, P115), an
  ambient `Random` (`ViewerGame.cs:1966`), plus one-offs (harness, endgame). `ICombatRng.Next` is
  max-exclusive; fo2ce `randomBetween` is max-inclusive.
- fo2ce's `getTicks()` is wall-clock; idle/fidget animation and ambient sound rolls fire on
  real-time timers → their position in the RNG stream is not reproducible run to run.
- `SAVE.DAT` is **one sequential stream of 27 handler sections with no length prefixes**
  (`loadsave.cc:211-239` save list, `:242-269` load list): dude cid, gvars, map file list
  (`_GameMap2Slot`), gvars again, dude object, critter, kills, skills, random, perks, combat, ai,
  stats, items, traits, automap, preferences, character editor, worldmap, pipboy, movies,
  skills-usage, party, queue, interface. Reaching any section requires parsing all prior ones.
- Per-map `.SAV` files and `AUTOMAP.DB` are copied into the slot **gzip-compressed**
  (`fileCopyCompressed`, `loadsave.cc:2502,2514`); their payload is MAP-format with runtime
  object fields.
- Hexwaste's own save (`SaveState.cs`) is a JSON snapshot with no combat-state or queue fields —
  it is not a usable import target.

## Architecture

```
 reference/fallout2-ce (gitignored clone @ e97087b)
   + tools/fo2ce-trace/trace.patch      committed; our diff, not game data
   + tools/fo2ce-trace/build.sh         applies patch, configures, builds
   → reference/fallout2-ce/build-trace/fallout2-ce   (SDL offscreen video, dummy audio)

 tests/golden-oracle/<fixture>/
   recipe.txt      how to make the save (committed)
   scenario.txt    seed + action list (committed)

 game-data/oracle-saves/<fixture>/      generated SAVE.DAT slot (gitignored, never committed)

 scripts/oracle-run.sh <fixture>
   ├─ fo2ce-trace --load-slot <dir> --seed S --scenario scenario.txt --trace fo2ce.trace
   ├─ Hexwaste    --load-vanilla-save <dir> --rng fo2ce --seed S --scenario scenario.txt --trace hex.trace
   └─ TraceDiff   fo2ce.trace hex.trace   → IDENTICAL | first divergence + context
```

### Units

| Unit | Location | Job |
|---|---|---|
| `trace.patch` + `build.sh` | `tools/fo2ce-trace/` | Trace hooks, virtual clock, cosmetic RNG split, CLI modes (`--make-save`, `--load-slot`, `--seed`, `--scenario`, `--trace`). Applied to the gitignored clone. |
| `Fo2ceRng` | `src/Hexwaste.Formats/Combat/` | Cited port of `random.cc` (seeding, `getRandom`, `randomBetween`) behind `ICombatRng` (adapting max-exclusive → inclusive). |
| `VanillaSaveReader` | `src/Hexwaste.Formats/Save/Vanilla/` | Read-only parser for `SAVE.DAT` + slot `.SAV` maps. One parser per handler section, each citing its fo2ce `*Load()`. |
| `VanillaSaveImport` | `src/Hexwaste.Viewer/` | Applies the parsed model to live Hexwaste runtime state (map, objects, dude, gvars, combat, queue, party). Not via `SaveState`. |
| `TraceWriter` | `src/Hexwaste.Formats/Trace/` | Emits the shared trace line format; no-op unless `--trace` is given. |
| Scenario runner | `src/Hexwaste.Viewer/` (harness) + patch | Parses `scenario.txt`, executes actions. M1 verbs: `wait <ticks>`, `end-turn [n]`. |
| `TraceDiff` | `tools/TraceDiff/` | Line diff ignoring the `site=` field; prints first divergence with 20 lines of context and both sites. |

Default Hexwaste behaviour is untouched: `--rng fo2ce`, `--trace`, `--scenario` and
`--load-vanilla-save` are opt-in. All existing golden suites must stay byte-identical.

## Trace format

One event per line, prefixed by **game time in ticks** (never wall-clock):

```
# oracle v1 fixture=arcaves-ambush seed=12345 scenario=<sha1> engine=fo2ce@e97087b+trace.patch@<sha1>
t=302400 RNG 1..100 -> 37 site=combat.cc:attackDetermineToHit
t=302400 PROC kladwtwn.int map_update_p_proc self=obj#1520
t=302400 GVAR 182 0->1
t=302400 MVAR 3 2->3
t=302400 LVAR klaratct 4 0->1
t=302400 TURN obj#1520 ap=8
t=302400 ATTACK obj#1520->obj#16777216 chance=66 hit=1 dmg=5 flags=0x0
t=302400 MOVE obj#1520 20529->20530
t=302400 SPAWN obj#<id> pid=<pid> tile=<tile>
```

- Objects are keyed by vanilla `obj->id` (persisted in the save; the reader preserves it).
  Objects created during the run take ids from each engine's id counter — the `SPAWN` line makes a
  counter mismatch a visible divergence.
- `RNG` ranges are written inclusive (`min..max`) on both sides.
- `site=` is **context, not compared**: fo2ce gets `__FILE__:__func__` by turning `randomBetween`
  into a macro over a traced variant; Hexwaste uses `[CallerMemberName]`/`[CallerFilePath]`.
- The header pins fixture, seed, scenario hash and engine identity so any run is reproducible from
  its header.

## Determinism rules

1. **Virtual clock (fo2ce patch):** `getTicks()` returns a counter advanced by a fixed step per
   game-loop iteration. Hexwaste runs the matching fixed step via its existing deterministic pump
   (`--pump-ms`).
2. **Single shared stream (Hexwaste, `--rng fo2ce` only):** one `Fo2ceRng` instance is handed to
   combat, travel/encounters and the script VM; the VM's per-proc reseed is disabled in this mode.
3. **Cosmetic split:** a call site is *cosmetic* if its timing is driven by wall-clock (idle/fidget
   animation, ambient sfx, talking-head fidgets). In the patch, cosmetic sites draw from a second,
   untraced generator; in Hexwaste, cosmetic sites keep their own stream. Classification is done by
   an audit of all `randomBetween` call sites (each tagged with `file:line` + reason) as the first
   plan task; **ambiguous sites default to logic** (traced).
4. **Self-determinism gate:** the patched fo2ce run twice with the same inputs must produce
   byte-identical traces before any Hexwaste comparison is trusted.

## Diff semantics

- Compare line by line, ignoring `site=`.
- On first mismatch: print the mismatching pair, the preceding 20 lines, and both sites. Stop —
  lockstep means everything after the first divergence is downstream noise.
- Re-sync past a known divergence (e.g. at the next `TURN`/`PROC`) is **out of scope for M1**.

## Fixture creation

`--make-save <recipe>` mode in the patched fo2ce executes a recipe and writes a slot:

```
new-game premade=<name>
load-map arcaves.map elev=0 tile=20131
set-gvar 182 1
advance-time 3600
save-slot oracle/arcaves-ambush
```

Recipes are committed; the resulting slot lives under `game-data/oracle-saves/` (gitignored —
saves embed game-derived data). Hand-played saves are allowed for deep states, but saves from the
community binary are accepted only after verifying its save/load handlers are format-identical to
`e97087b` (`git diff e97087b..community/main -- src/loadsave.cc` + each handler's file); otherwise
rejected.

## Milestone 1 scope

**Reader:** parses all 27 sections sequentially. Typed models for: header, gvars (both copies),
map file list + current map `.SAV`, dude object, critter, stats, skills, perks, traits, combat,
queue, party. Every other section (kills, ai, automap, preferences, character editor, worldmap,
pipboy, movies, skills-usage, interface) is length-parsed and kept as raw bytes.

**Fixtures (passive only):**
1. `artemple-idle` — `map_enter` + 600 ticks of critter heartbeats. Smallest end-to-end.
2. `klamath-hour` — advance one game hour: NPC wander (AI movement RNG), timed events,
   `map_update`.
3. `arcaves-ambush` — dude placed near radscorpions; they perceive him and start combat; dude
   `end-turn` × N. AI turns, attacks, damage rolls.

**Done when:**
- Self-determinism gate passes for all three fixtures.
- All three run end-to-end in both engines, each yielding `IDENTICAL` or a first-divergence report.
- A deliberately planted Hexwaste bug (e.g. off-by-one in hit chance) is caught by the oracle.
- Hexwaste-vs-fo2ce divergences are **findings**, recorded for follow-up — not M1 failures.

## Testing

- `Fo2ceRng`: hermetic unit test against the first 1000 draws the patched fo2ce emits for a fixed
  seed (plain integers — committed, not game data).
- `VanillaSaveReader`: hermetic per-section tests on hand-built byte streams; `FALLOUT2_DIR`-guarded
  tests reading recipe-generated saves.
- `TraceDiff`: hermetic tests (identical, first-line mismatch, site-only difference ignored).
- Existing golden suites: unchanged and byte-identical (default RNG mode untouched).

## Out of scope (M1)

- Active actions (`attack`, `walk`, `use`, `talk`/`option`) — milestone 2.
- End-of-run full-state diff (fo2ce re-saves; `StateDiff` compares reader output vs Hexwaste dump)
  — milestone 2.
- Diff re-synchronisation after a known divergence.
- Writing vanilla saves from Hexwaste.
- Function-level formula fuzzing (approach C from brainstorming) — possible separate project.

## Risks

- **Offscreen build:** previous source build was blocked on X11 dev headers (no sudo). Offscreen
  SDL + dummy audio should avoid them; needs CMake 4.x with `-DCMAKE_POLICY_VERSION_MINIMUM=3.5`.
  First plan task verifies the build before anything else is built on it.
- **Headless new-game/save path:** `lsgSaveGame` and new-game are UI-driven; the patch must reach
  the underlying save path without windows. If the UI cannot be bypassed cleanly, fall back to
  hand-played saves for M1.
- **Import fidelity:** an incomplete `VanillaSaveImport` will itself cause divergences. Mitigation:
  `artemple-idle` is chosen to exercise the smallest state surface first.
- **RNG call-order drift is pervasive:** expect early fixtures to diverge quickly. That is the
  oracle working; each divergence becomes a tracked finding.
