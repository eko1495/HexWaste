# fo2ce Trace Oracle — Milestone 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run a trace-patched vanilla fallout2-ce and Hexwaste from the same vanilla save, with the same RNG algorithm and seed, through the same passive scenario, and report the first line where their event traces diverge.

**Architecture:** A source patch over fallout2-ce `e97087b` (kept as `tools/fo2ce-trace/trace.patch`, applied to a gitignored worktree, built headless with SDL offscreen + dummy audio) adds a virtual clock, a deterministic seed, trace hooks, a recipe-driven save maker and a scenario runner. Hexwaste gains a cited `Fo2ceRng` port, a read-only vanilla `SAVE.DAT` reader + importer, an opt-in oracle mode (`--rng fo2ce --trace --scenario --load-vanilla-save`) and the same trace hooks. A `TraceDiff` tool compares the two traces.

**Tech Stack:** C++17 / CMake 4.x / GNU make (fo2ce patch); C# .NET 10, xUnit (Hexwaste); bash (orchestration).

**Spec:** `docs/superpowers/specs/2026-10-10-fo2ce-trace-oracle-design.md`

## Global Constraints

- `alexbatalov e97087b` is authoritative. Never base the patch on `community/main`.
- Never commit game assets or anything derived from them: saves live under `game-data/oracle-saves/` (already covered by `game-data/` in `.gitignore`); traces under `scratch/oracle-runs/` (add to `.gitignore`).
- Default Hexwaste behaviour must be unchanged: every new behaviour is behind `--rng fo2ce`, `--trace`, `--scenario` or `--load-vanilla-save`. All golden suites (`scripts/*-golden.sh check`) stay byte-identical.
- Every ported routine carries a citation comment: `// ported from fallout2-ce src/x.cc f() (:line)`.
- No new NuGet/C++ dependencies. (`System.IO.Compression.GZipStream` is part of the BCL — not a new dependency.)
- Trace line format (both engines, byte-identical except `site=`):
  `t=<gameTicks> <KIND> <fields...>[ site=<text>]` — `KIND` ∈ `RNG PROC GVAR MVAR LVAR TURN ATTACK MOVE SPAWN`. Lines starting with `#` are headers, ignored by the diff.
- Virtual clock step: **10 ms per tick of `tickersExecute`** (fo2ce) == **10 ms per `PumpMs` iteration** (Hexwaste, already its step).
- Oracle save slot in fo2ce: **slot index 9 (`SLOT10`)** so user saves in slots 1–9 are never touched.
- Hexwaste build/test commands: `dotnet build Hexwaste.slnx`, `dotnet test tests/Hexwaste.Formats.Tests`.
- Game data dir for the C# side: `FALLOUT2_DIR=$PWD/game-data`. fo2ce runs with CWD `reference/fallout2-ce/run` (a full game-data copy; see memory note `fo2ce-runnable-baseline`).

## File Map

| Path | Responsibility |
|---|---|
| `tools/fo2ce-trace/trace.patch` | The committed diff over `e97087b` (all C++ changes below). |
| `tools/fo2ce-trace/worktree.sh` | Create/refresh the gitignored worktree `reference/fallout2-ce-oracle` at `e97087b` and apply the patch. |
| `tools/fo2ce-trace/export-patch.sh` | Regenerate `trace.patch` from the worktree. |
| `tools/fo2ce-trace/build.sh` | Configure + build the headless binary. |
| `tools/fo2ce-trace/run.sh` | Run the binary headless from the run dir with `[oracle]` config overrides. |
| *(worktree)* `src/oracle.h`, `src/oracle.cc` | Oracle config, virtual clock, trace sink, cosmetic RNG, recipe + scenario interpreters. |
| *(worktree)* `src/input.cc`, `src/delay.cc`, `src/fps_limiter.cc`, `src/svga.cc`, `src/random.cc`, `src/random.h`, `src/main.cc`, `src/loadsave.cc/.h`, `src/scripts.cc`, `src/game.cc`, `src/map.cc`, `src/combat.cc`, `src/object.cc`, `CMakeLists.txt` | Hook sites. |
| `docs/research-notes/fo2ce-rng-callsite-audit-2026-10.md` | Logic-vs-cosmetic classification of every `randomBetween`/`randomRoll` call site. |
| `src/Hexwaste.Formats/Combat/Fo2ceRng.cs` | Port of `random.cc` behind `ICombatRng`, plus `Fo2ceRandomAdapter : Random`. |
| `src/Hexwaste.Formats/Trace/OracleTrace.cs` | Process-wide trace sink (no-op when inactive). |
| `src/Hexwaste.Formats/Trace/TraceComparer.cs` | First-divergence comparison (pure). |
| `src/Hexwaste.Formats/Trace/OracleScenario.cs` | `scenario.txt` parser (pure). |
| `src/Hexwaste.Formats/Save/Vanilla/SaveDatReader.cs` | Sequential `SAVE.DAT` parser (27 handlers). |
| `src/Hexwaste.Formats/Save/Vanilla/VanillaSave.cs` | Parsed model records. |
| `src/Hexwaste.Formats/Save/Vanilla/VanillaSaveContext.cs` | Counts the reader needs from game data (gvar count, party descriptions, custom AI packets, proto subtypes). |
| `src/Hexwaste.Formats/Save/Vanilla/SlotFiles.cs` | Slot directory access + gzip-transparent open. |
| `src/Hexwaste.Formats/Map/MapFile.cs` | Expose object reading; keep runtime fields (`Elevation`, `Cid`, `ScriptIndex`, `CritterFlags`). |
| `src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs` | Apply a `VanillaSave` to live state. |
| `src/Hexwaste.Viewer/ViewerGame.Oracle.cs` | Oracle mode wiring: shared RNG, trace open/close, scenario actions. |
| `src/Hexwaste.Viewer/Program.cs` | New flags. |
| `tools/TraceDiff/` | CLI over `TraceComparer`. |
| `tests/golden-oracle/<fixture>/{recipe.txt,scenario.txt}` | Fixture definitions (committed). |
| `scripts/oracle-make-save.sh`, `scripts/oracle-run.sh`, `scripts/oracle-selfcheck.sh` | Orchestration. |
| `tests/Hexwaste.Formats.Tests/Fo2ceRngTests.cs`, `TraceComparerTests.cs`, `OracleScenarioTests.cs`, `SaveDatReaderTests.cs`, `Data/fo2ce-rng-vectors.txt` | Tests. |

---

### Task 1: Headless fo2ce build pipeline

Delivers a reproducible headless build of vanilla `e97087b` (no trace hooks yet) that boots and exits on its own.

**Files:**
- Create: `tools/fo2ce-trace/worktree.sh`, `tools/fo2ce-trace/export-patch.sh`, `tools/fo2ce-trace/build.sh`, `tools/fo2ce-trace/run.sh`, `tools/fo2ce-trace/README.md`, `tools/fo2ce-trace/trace.patch`
- Worktree: `src/oracle.h`, `src/oracle.cc` (new), `src/svga.cc`, `src/input.cc`, `src/delay.cc`, `src/fps_limiter.cc`, `CMakeLists.txt`
- Modify: `.gitignore` (add `scratch/oracle-runs/`)

**Interfaces:**
- Produces (C++, `src/oracle.h`):
  ```cpp
  bool oracleEnabled();                 // [oracle]enabled=1
  void oracleInit();                    // reads [oracle] keys from gGameConfig; call once after config load
  unsigned int oracleTicks();           // virtual ms clock
  void oracleAdvanceClock(unsigned int ms);
  void oracleTick();                    // called once per tickersExecute(): advances 10 ms, runs exit_after_ms check
  ```
- Produces (shell): `tools/fo2ce-trace/run.sh [key=value ...]` → runs `reference/fallout2-ce-oracle/build-oracle/fallout2-ce` from `reference/fallout2-ce/run` with `[oracle]enabled=1` plus each `[oracle]key=value`.

- [ ] **Step 1: Create the worktree script**

`tools/fo2ce-trace/worktree.sh`:
```bash
#!/usr/bin/env bash
# Create (or reset) the gitignored oracle worktree of the fallout2-ce reference clone at the
# authoritative vanilla commit e97087b, and apply tools/fo2ce-trace/trace.patch on top.
# Usage: tools/fo2ce-trace/worktree.sh [--reset]
set -euo pipefail
cd "$(dirname "$0")/../.."
CLONE=reference/fallout2-ce
WT=reference/fallout2-ce-oracle
BASE=e97087b
PATCH="$PWD/tools/fo2ce-trace/trace.patch"

if [ "${1:-}" = "--reset" ] && [ -d "$WT" ]; then
  git -C "$CLONE" worktree remove --force "$(cd "$WT" && pwd)"
fi
if [ ! -d "$WT" ]; then
  git -C "$CLONE" worktree add --detach "$(pwd)/$WT" "$BASE"
  if [ -s "$PATCH" ]; then
    git -C "$WT" apply --index "$PATCH"
  fi
fi
echo "$WT"
```

- [ ] **Step 2: Create the export script**

`tools/fo2ce-trace/export-patch.sh`:
```bash
#!/usr/bin/env bash
# Regenerate tools/fo2ce-trace/trace.patch from the oracle worktree (new files included).
set -euo pipefail
cd "$(dirname "$0")/../.."
WT=reference/fallout2-ce-oracle
git -C "$WT" add -A
git -C "$WT" diff --cached e97087b -- . ':!build-oracle' > tools/fo2ce-trace/trace.patch
sha1sum tools/fo2ce-trace/trace.patch
```

- [ ] **Step 3: Create the build script**

The existing `reference/fallout2-ce/build/_deps` already holds fetched SDL2/zlib sources; reuse them so the build needs no network. SDL detection already yields offscreen+dummy only (no X11/Wayland headers on this machine); force it explicitly.

`tools/fo2ce-trace/build.sh`:
```bash
#!/usr/bin/env bash
# Headless build of the trace-patched fallout2-ce. Needs: cmake 4.x (~/.local/bin), gcc, make.
set -euo pipefail
cd "$(dirname "$0")/../.."
WT=reference/fallout2-ce-oracle
DEPS="$PWD/reference/fallout2-ce/build/_deps"
PATCH_SHA=$(sha1sum tools/fo2ce-trace/trace.patch | cut -c1-12)
cmake -S "$WT" -B "$WT/build-oracle" -G "Unix Makefiles" \
  -DCMAKE_BUILD_TYPE=RelWithDebInfo \
  -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
  -DFALLOUT_VENDORED=ON \
  -DSDL_X11=OFF -DSDL_WAYLAND=OFF -DSDL_OPENGL=OFF -DSDL_OPENGLES=OFF \
  -DFETCHCONTENT_SOURCE_DIR_SDL2="$DEPS/sdl2-src" \
  -DFETCHCONTENT_FULLY_DISCONNECTED=ON \
  -DORACLE_PATCH_SHA="$PATCH_SHA"
cmake --build "$WT/build-oracle" -j"$(nproc)"
ls -l "$WT/build-oracle/fallout2-ce"
```
If `FETCHCONTENT_SOURCE_DIR_SDL2` is not honoured by `third_party/sdl2/CMakeLists.txt` (check the `FetchContent_Declare` name there — the variable is `FETCHCONTENT_SOURCE_DIR_<UPPERCASED NAME>`), drop `FETCHCONTENT_FULLY_DISCONNECTED` and let it fetch; record which worked in `tools/fo2ce-trace/README.md`. Do the same check for zlib (`$DEPS/zlib-src`) if it is fetched rather than vendored in-tree.

- [ ] **Step 4: Create the run script**

`tools/fo2ce-trace/run.sh`:
```bash
#!/usr/bin/env bash
# Run the headless oracle binary from the game run dir. Every argument is an [oracle] key=value
# override, passed through fallout2-ce's own [section]key=value argv mechanism (config.cc:79).
set -euo pipefail
cd "$(dirname "$0")/../.."
BIN="$PWD/reference/fallout2-ce-oracle/build-oracle/fallout2-ce"
RUN=reference/fallout2-ce/run
args=("[oracle]enabled=1" "[misc]SkipOpeningMovies=2")
for kv in "$@"; do args+=("[oracle]$kv"); done
cd "$RUN"
SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy timeout 1800 "$BIN" "${args[@]}"
```
`[misc]SkipOpeningMovies` is an sfall-config key; `configParseCommandLineArguments` applies argv overrides to both configs (`sfall_config.cc:83`, `game_config.cc:184`).

- [ ] **Step 5: Create the worktree and add `oracle.h/.cc` (clock + exit only)**

Run: `bash tools/fo2ce-trace/worktree.sh` (patch is empty at this point, so the worktree is pristine).

`reference/fallout2-ce-oracle/src/oracle.h`:
```cpp
#ifndef FALLOUT_ORACLE_H_
#define FALLOUT_ORACLE_H_

// Hexwaste trace oracle (tools/fo2ce-trace). Everything here is inert unless [oracle]enabled=1.
namespace fallout {

bool oracleEnabled();
void oracleInit();
unsigned int oracleTicks();
void oracleAdvanceClock(unsigned int ms);
void oracleTick();

} // namespace fallout

#endif
```

`reference/fallout2-ce-oracle/src/oracle.cc`:
```cpp
#include "oracle.h"

#include <stdio.h>
#include <stdlib.h>

#include "game_config.h"

namespace fallout {

static bool gOracleEnabled = false;
static unsigned int gOracleClock = 0;
static int gOracleExitAfterMs = -1;

static constexpr unsigned int kOracleStepMs = 10;

bool oracleEnabled()
{
    return gOracleEnabled;
}

void oracleInit()
{
    int enabled = 0;
    configGetInt(&gGameConfig, "oracle", "enabled", &enabled);
    gOracleEnabled = enabled != 0;
    if (!gOracleEnabled) {
        return;
    }
    configGetInt(&gGameConfig, "oracle", "exit_after_ms", &gOracleExitAfterMs);
}

unsigned int oracleTicks()
{
    return gOracleClock;
}

void oracleAdvanceClock(unsigned int ms)
{
    gOracleClock += ms;
}

void oracleTick()
{
    gOracleClock += kOracleStepMs;
    if (gOracleExitAfterMs >= 0 && gOracleClock >= (unsigned int)gOracleExitAfterMs) {
        fprintf(stderr, "oracle: exit_after_ms reached at %u\n", gOracleClock);
        exit(0);
    }
}

} // namespace fallout
```

Add `"src/oracle.cc"` and `"src/oracle.h"` to the `target_sources` list in `CMakeLists.txt` (alphabetically next to `"src/object.cc"`), and append after the `target_link_libraries(${EXECUTABLE_NAME} ${SDL2_LIBRARIES})` line:
```cmake
if(DEFINED ORACLE_PATCH_SHA)
    target_compile_definitions(${EXECUTABLE_NAME} PRIVATE ORACLE_PATCH_SHA="${ORACLE_PATCH_SHA}")
endif()
```

- [ ] **Step 6: Wire `oracleInit` after config load**

In `src/game.cc` `gameInitWithOptions`, immediately after the game config is loaded (the `gameConfigInit(...)` call), add `oracleInit();` and `#include "oracle.h"` at the top.

- [ ] **Step 7: Virtualise the clock**

`src/input.cc` (add `#include "oracle.h"`):
```cpp
void tickersExecute()
{
    if (gRunLoopDisabled) {
        return;
    }

    // ORACLE: one virtual 10 ms step per ticker pass — every input/bk loop goes through here.
    if (oracleEnabled()) {
        oracleTick();
    }
    gTickerLastTimestamp = getTicks();
    ...unchanged...
}

unsigned int getTicks()
{
    if (oracleEnabled()) {
        return oracleTicks();
    }
    return SDL_GetTicks();
}

unsigned int getTicksSince(unsigned int start)
{
    unsigned int end = getTicks();
    ...unchanged...
}
```
`src/delay.cc` `delay_ms`: at the top, `if (oracleEnabled()) { oracleAdvanceClock(ms); return; }`.
`src/fps_limiter.cc` `FpsLimiter::mark` and `FpsLimiter::throttle`: at the top of each, `if (oracleEnabled()) return;`.

- [ ] **Step 8: Software renderer when headless**

`src/svga.cc` around `:176-178`: wrap the `SDL_SetHint(SDL_HINT_RENDER_DRIVER, "opengl")` call and the `SDL_WINDOW_OPENGL` flag so that, when `getenv("SDL_VIDEODRIVER")` equals `"offscreen"`, the hint is `"software"` and the window flags omit `SDL_WINDOW_OPENGL`. (SDL_Init runs before the game config is parsed — `win32.cc:59` — so the switch is the environment variable, not `[oracle]`.)

- [ ] **Step 9: Export the patch, build, smoke-run**

Run:
```bash
bash tools/fo2ce-trace/export-patch.sh
bash tools/fo2ce-trace/build.sh
bash tools/fo2ce-trace/run.sh exit_after_ms=5000; echo "exit=$?"
```
Expected: build succeeds; run prints `oracle: exit_after_ms reached at 5000` within a few seconds of wall time and `exit=0`. If it hangs at the main menu without the message, the main-menu loop is not reaching `tickersExecute` — inspect `mainmenu.cc` input loop and confirm it calls `inputGetInput()`.

- [ ] **Step 10: Document and commit**

`tools/fo2ce-trace/README.md`: purpose, the four scripts, prerequisites (cmake 4.x in `~/.local/bin`, `reference/fallout2-ce` clone + `run/` dir), the dependency-source finding from Step 3, and "the worktree is disposable — `trace.patch` is the source of truth; always run `export-patch.sh` before committing".

```bash
git add tools/fo2ce-trace .gitignore
git commit -m "feat(oracle): headless fo2ce e97087b build pipeline with a virtual clock"
```

---

### Task 2: Deterministic seeding, cosmetic split and the RNG vector dump (fo2ce)

**Files:**
- Create: `docs/research-notes/fo2ce-rng-callsite-audit-2026-10.md`, `tests/Hexwaste.Formats.Tests/Data/fo2ce-rng-vectors.txt`
- Worktree: `src/random.cc`, `src/random.h`, `src/oracle.h/.cc`, cosmetic call-site files per audit

**Interfaces:**
- Produces (C++):
  ```cpp
  void oracleRandomSeed(int seed);                          // random.cc: zero _iv/_iy, then randomSeedPrerandomInternal(seed)
  int randomBetweenAt(int min, int max, const char* file, const char* func); // traced logic draw
  int randomBetweenCosmetic(int min, int max);              // separate generator, never traced
  #define randomBetween(min, max) randomBetweenAt((min), (max), __FILE__, __func__)
  ```
- Produces (data): `fo2ce-rng-vectors.txt` — lines `seed=<s> min=<a> max=<b> -> <v>`, consumed by Task 3.

- [ ] **Step 1: Audit every RNG call site**

Run: `grep -n "randomBetween(\|randomRoll(" reference/fallout2-ce-oracle/src/*.cc > /tmp/rng-sites.txt; wc -l /tmp/rng-sites.txt`

For each line, read the surrounding function and classify:
- **cosmetic** = the draw's *timing* is driven by wall-clock timers or rendering, and its result changes no game state other than visuals/audio (idle/fidget animation choice, ambient sound selection, talking-head fidget, float-text flavour).
- **logic** = everything else. **Ambiguous ⇒ logic.**

Write `docs/research-notes/fo2ce-rng-callsite-audit-2026-10.md` as a table: `file:line | function | class (logic/cosmetic) | reason`. Include a header noting the base commit `e97087b` and that it drives `randomBetweenCosmetic` substitutions.

- [ ] **Step 2: Add the traced/cosmetic split in `random.cc`/`random.h`**

`src/random.h`: rename the declaration `int randomBetween(int min, int max);` to
```cpp
int randomBetweenAt(int min, int max, const char* file, const char* func);
int randomBetweenCosmetic(int min, int max);
void oracleRandomSeed(int seed);
#define randomBetween(min, max) randomBetweenAt((min), (max), __FILE__, __func__)
```
`src/random.cc`: rename the definition `int randomBetween(int min, int max)` to `int randomBetweenAt(int min, int max, const char* file, const char* func)` (body unchanged except: just before `return result;` add `oracleTraceRng(min, max, result, file, func);` — the function itself is added in Task 4; for now add an empty inline stub `static inline void oracleTraceRng(int, int, int, const char*, const char*) {}` at the top of `random.cc`, to be replaced in Task 4). Then add:

```cpp
// ORACLE: deterministic reseed. randomSeedPrerandomInternal() never writes _iv[0] (its loop runs
// 40..1), so a plain reseed inherits _iv[0] from the wall-clock-seeded randomInit() +
// randomValidatePrerandom(). Zero the whole table first so the stream is a pure function of seed.
void oracleRandomSeed(int seed)
{
    memset(_iv, 0, sizeof(_iv));
    _iy = 0;
    _idum = 0;
    randomSeedPrerandomInternal(seed);
}

// ORACLE: wall-clock-timed cosmetic draws use their own Park-Miller stream so they cannot shift
// the logic stream (see docs/research-notes/fo2ce-rng-callsite-audit-2026-10.md).
static int gCosmeticSeed = 1;

int randomBetweenCosmetic(int min, int max)
{
    if (min > max) {
        int t = min;
        min = max;
        max = t;
    }
    gCosmeticSeed = 16807 * (gCosmeticSeed % 127773) - 2836 * (gCosmeticSeed / 127773);
    if (gCosmeticSeed <= 0) {
        gCosmeticSeed += 0x7FFFFFFF;
    }
    return min + gCosmeticSeed % (max - min + 1);
}
```
`randomRoll` calls `randomBetween(1, 100)` inside `random.cc`; with the macro, those draws report `site=random.cc:randomRoll`. That is acceptable — the comparison ignores `site=`.

- [ ] **Step 3: Substitute cosmetic call sites**

For every row classified `cosmetic` in the audit, replace `randomBetween(` with `randomBetweenCosmetic(` at that exact line. Re-run the grep from Step 1 and confirm the remaining `randomBetween(` count equals the number of `logic` rows.

- [ ] **Step 4: Add the vector dump mode**

In `oracle.cc` `oracleInit()` read `[oracle]rng_dump` (string path, empty = off) and `[oracle]seed` (int, default 12345) into statics; expose `const char* oracleRngDumpPath(); int oracleSeed();` in `oracle.h`. In `src/main.cc` `falloutMain`, immediately after `falloutInit(argc, argv)` succeeds, add:
```cpp
    if (oracleEnabled() && oracleRngDumpPath()[0] != '\0') {
        oracleWriteRngVectors(oracleRngDumpPath(), oracleSeed());
        exit(0);
    }
```
Implement in `oracle.cc`:
```cpp
void oracleWriteRngVectors(const char* path, int seed)
{
    FILE* out = fopen(path, "wt");
    if (out == nullptr) {
        exit(2);
    }
    static const int kRanges[][2] = { { 1, 100 }, { 1, 6 }, { 0, 0 }, { 0, 1 }, { 5, 1 }, { -10, 10 }, { 1, 25 } };
    oracleRandomSeed(seed);
    for (int i = 0; i < 1000; i++) {
        const int* r = kRanges[i % 7];
        int v = randomBetweenAt(r[0], r[1], "dump", "dump");
        fprintf(out, "seed=%d min=%d max=%d -> %d\n", seed, r[0], r[1], v);
    }
    fclose(out);
}
```
Declare `void oracleWriteRngVectors(const char* path, int seed);` in `oracle.h` and `#include "random.h"` in `oracle.cc`. The `{5, 1}` row exercises the reversed-range branch and `{0, 0}` the single-value draw (both consume a draw in fo2ce).

- [ ] **Step 5: Build, dump twice, compare**

```bash
bash tools/fo2ce-trace/export-patch.sh && bash tools/fo2ce-trace/build.sh
OUT=$PWD/scratch/oracle-runs; mkdir -p "$OUT"
bash tools/fo2ce-trace/run.sh rng_dump="$OUT/v1.txt" seed=12345
bash tools/fo2ce-trace/run.sh rng_dump="$OUT/v2.txt" seed=12345
cmp "$OUT/v1.txt" "$OUT/v2.txt" && wc -l "$OUT/v1.txt"
```
Expected: `cmp` silent, `1000`. Then also dump seed 1 and seed 987654321 and concatenate all three into `tests/Hexwaste.Formats.Tests/Data/fo2ce-rng-vectors.txt` (3000 lines; plain integers — not game data).

- [ ] **Step 6: Commit**
```bash
bash tools/fo2ce-trace/export-patch.sh
git add tools/fo2ce-trace/trace.patch docs/research-notes/fo2ce-rng-callsite-audit-2026-10.md tests/Hexwaste.Formats.Tests/Data/fo2ce-rng-vectors.txt
git commit -m "feat(oracle): deterministic fo2ce seeding, cosmetic RNG split, RNG vectors"
```

---

### Task 3: `Fo2ceRng` port (C#)

**Files:**
- Create: `src/Hexwaste.Formats/Combat/Fo2ceRng.cs`, `tests/Hexwaste.Formats.Tests/Fo2ceRngTests.cs`
- Modify: `tests/Hexwaste.Formats.Tests/Hexwaste.Formats.Tests.csproj` (copy `Data/**` to output)

**Interfaces:**
- Produces:
  ```csharp
  public sealed class Fo2ceRng : ICombatRng
  {
      public Fo2ceRng(int seed);
      public int Between(int min, int max);                 // fo2ce randomBetween, inclusive
      public int Next(int minInclusive, int maxExclusive);  // ICombatRng → Between(min, max-1)
      public Action<int,int,int>? OnDraw { get; set; }      // (min, max, result) — trace hook
  }
  public sealed class Fo2ceRandomAdapter(Fo2ceRng rng) : Random  // for IntVm/ScriptHost
  ```

- [ ] **Step 1: Write the failing tests**

`tests/Hexwaste.Formats.Tests/Fo2ceRngTests.cs`:
```csharp
using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>Locks <see cref="Fo2ceRng"/> to the draws the trace-patched fallout2-ce e97087b
/// emits (Data/fo2ce-rng-vectors.txt, produced by tools/fo2ce-trace run.sh rng_dump=...).</summary>
public class Fo2ceRngTests
{
    private static IEnumerable<(int Seed, int Min, int Max, int Value)> Vectors()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "fo2ce-rng-vectors.txt");
        foreach (string line in File.ReadLines(path))
        {
            // seed=12345 min=1 max=100 -> 37
            string[] p = line.Split(' ');
            yield return (int.Parse(p[0][5..]), int.Parse(p[1][4..]), int.Parse(p[2][4..]), int.Parse(p[4]));
        }
    }

    [Fact]
    public void MatchesFo2ceVectorsForEverySeed()
    {
        Fo2ceRng? rng = null;
        int currentSeed = int.MinValue, checkedCount = 0;
        foreach (var (seed, min, max, value) in Vectors())
        {
            if (seed != currentSeed)
            {
                rng = new Fo2ceRng(seed);
                currentSeed = seed;
            }
            Assert.Equal(value, rng!.Between(min, max));
            checkedCount++;
        }
        Assert.Equal(3000, checkedCount);
    }

    [Fact]
    public void NextIsMaxExclusiveView()
    {
        var a = new Fo2ceRng(7);
        var b = new Fo2ceRng(7);
        for (int i = 0; i < 200; i++)
            Assert.Equal(a.Between(1, 100), b.Next(1, 101));
    }

    [Fact]
    public void OnDrawReportsEveryDraw()
    {
        var rng = new Fo2ceRng(1);
        var seen = new List<(int, int, int)>();
        rng.OnDraw = (min, max, v) => seen.Add((min, max, v));
        int v = rng.Between(1, 6);
        Assert.Equal([(1, 6, v)], seen);
    }

    [Fact]
    public void AdapterRoutesNextMinMaxThroughTheSameStream()
    {
        var a = new Fo2ceRng(3);
        var adapter = new Fo2ceRandomAdapter(new Fo2ceRng(3));
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.Next(1, 11), adapter.Next(1, 11));
    }
}
```
Add to the test csproj:
```xml
  <ItemGroup>
    <None Include="Data\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Run, expect compile failure**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter Fo2ceRngTests`
Expected: FAIL — `Fo2ceRng` not found.

- [ ] **Step 3: Implement**

`src/Hexwaste.Formats/Combat/Fo2ceRng.cs`:
```csharp
namespace Hexwaste.Formats.Combat;

/// <summary>
/// fallout2-ce's single game RNG, for the trace oracle (<c>--rng fo2ce</c>): a Park-Miller
/// minimal-standard generator behind a 32-entry shuffle table.
/// ported from fallout2-ce src/random.cc randomSeedPrerandomInternal() (:192), getRandom() (:153),
/// randomBetween() (:134). Seeding mirrors the oracle patch's oracleRandomSeed(): the table is
/// zeroed first (the engine's own reseed never writes _iv[0] — its loop runs 40..1 — so a vanilla
/// reseed would inherit a wall-clock-dependent _iv[0]).
/// </summary>
public sealed class Fo2ceRng : ICombatRng
{
    private readonly int[] _iv = new int[32];
    private int _iy;
    private int _idum;

    /// <summary>Trace hook: (min, max, result) after every draw, with fo2ce's inclusive bounds.</summary>
    public Action<int, int, int>? OnDraw { get; set; }

    public Fo2ceRng(int seed)
    {
        // ported from fallout2-ce src/random.cc randomSeedPrerandomInternal() (:192)
        int num = seed < 1 ? 1 : seed;
        for (int index = 40; index > 0; index--)
        {
            num = 16807 * (num % 127773) - 2836 * (num / 127773);
            if (num < 0)
                num &= int.MaxValue;
            if (index < 32)
                _iv[index] = num;
        }
        _iy = _iv[0];
        _idum = num;
    }

    // ported from fallout2-ce src/random.cc getRandom() (:153)
    private int GetRandom(int max)
    {
        int v1 = 16807 * (_idum % 127773) - 2836 * (_idum / 127773);
        if (v1 < 0)
            v1 += 0x7FFFFFFF;
        if (v1 < 0)
            v1 += 0x7FFFFFFF;
        int v2 = _iy & 0x1F;
        int v3 = _iv[v2];
        _iv[v2] = v1;
        _iy = v3;
        _idum = v1;
        return v3 % max;
    }

    /// <summary>ported from fallout2-ce src/random.cc randomBetween() (:134) — inclusive, and a
    /// reversed range is legal (draws from [max, min]).</summary>
    public int Between(int min, int max)
    {
        int result = min <= max ? min + GetRandom(max - min + 1) : max + GetRandom(min - max + 1);
        if (result < min || result > max)
            result = min;
        OnDraw?.Invoke(min, max, result);
        return result;
    }

    public int Next(int minInclusive, int maxExclusive) => Between(minInclusive, maxExclusive - 1);
}

/// <summary>Presents a <see cref="Fo2ceRng"/> as a <see cref="Random"/> so the script VM and
/// ScriptHost (which take <c>System.Random</c>) draw from the one shared oracle stream.</summary>
public sealed class Fo2ceRandomAdapter(Fo2ceRng rng) : Random
{
    public Fo2ceRng Rng { get; } = rng;
    public override int Next(int minValue, int maxValue) => Rng.Next(minValue, maxValue);
    public override int Next(int maxValue) => Rng.Next(0, maxValue);
    public override int Next() => Rng.Next(0, int.MaxValue);
    protected override double Sample() => Rng.Next(0, int.MaxValue) / (double)int.MaxValue;
}
```
Note: the reversed-range check `result < min` uses the *original* `min`, exactly as fo2ce does — so a reversed range always returns `min`'s fallback when `result < min`. Keep it verbatim; the vectors test (`{5, 1}` row) proves it.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter Fo2ceRngTests`
Expected: 4 passed. If `MatchesFo2ceVectorsForEverySeed` fails on the first line, the patch's `oracleRandomSeed` and this constructor disagree — diff them line by line before changing anything else.

- [ ] **Step 5: Commit**
```bash
git add src/Hexwaste.Formats/Combat/Fo2ceRng.cs tests/Hexwaste.Formats.Tests/Fo2ceRngTests.cs tests/Hexwaste.Formats.Tests/Hexwaste.Formats.Tests.csproj
git commit -m "feat(formats): port fallout2-ce's RNG as Fo2ceRng for the trace oracle"
```

---

### Task 4: fo2ce trace sink and hooks

**Files:**
- Worktree: `src/oracle.h/.cc`, `src/random.cc`, `src/scripts.cc`, `src/game.cc`, `src/map.cc`, `src/combat.cc`, `src/object.cc`

**Interfaces:**
- Produces (C++, `oracle.h`):
  ```cpp
  void oracleTraceOpen(const char* path, const char* header);  // no-op if path empty
  bool oracleTracing();
  void oracleTraceRng(int min, int max, int result, const char* file, const char* func);
  void oracleTraceProc(Object* self, const char* programName, const char* procName);
  void oracleTraceGvar(int index, int oldValue, int newValue);
  void oracleTraceMvar(int index, int oldValue, int newValue);
  void oracleTraceLvar(int sid, int index, int oldValue, int newValue);
  void oracleTraceTurn(Object* obj);
  void oracleTraceAttack(Object* attacker, Object* defender, int chance, bool hit, int damage, int defenderFlags);
  void oracleTraceMove(Object* obj, int fromTile, int toTile);
  void oracleTraceSpawn(Object* obj);
  ```
- Line formats (exact, Hexwaste must match in Task 11):
  ```
  t=%u RNG %d..%d -> %d site=%s:%s
  t=%u PROC %s %s self=obj#%d
  t=%u GVAR %d %d->%d
  t=%u MVAR %d %d->%d
  t=%u LVAR sid=0x%08X %d %d->%d
  t=%u TURN obj#%d ap=%d
  t=%u ATTACK obj#%d->obj#%d chance=%d hit=%d dmg=%d dflags=0x%X
  t=%u MOVE obj#%d %d->%d
  t=%u SPAWN obj#%d pid=0x%08X tile=%d
  ```
  `t` = `gameTimeGetTime()`. `programName` = basename of the `.int` without directory, lowercased, with extension (e.g. `kladwtwn.int`). `self=obj#-1` when no owner. `MOVE` only for critters (`PID_TYPE(obj->pid) == OBJ_TYPE_CRITTER`) and only when `fromTile != toTile`.

- [ ] **Step 1: Implement the sink in `oracle.cc`**

```cpp
#include <ctype.h>
#include <stdarg.h>
#include <string.h>

#include "game.h"
#include "object.h"
#include "proto.h"

static FILE* gOracleTrace = nullptr;
static int gOracleSuppress = 0; // >0 while the scenario runner itself draws/acts

bool oracleTracing()
{
    return gOracleTrace != nullptr && gOracleSuppress == 0;
}

void oracleTraceOpen(const char* path, const char* header)
{
    if (path == nullptr || path[0] == '\0') {
        return;
    }
    gOracleTrace = fopen(path, "wt");
    if (gOracleTrace == nullptr) {
        fprintf(stderr, "oracle: cannot open trace %s\n", path);
        exit(2);
    }
    fprintf(gOracleTrace, "# %s\n", header);
}

static void oracleLine(const char* fmt, ...)
{
    if (!oracleTracing()) {
        return;
    }
    fprintf(gOracleTrace, "t=%u ", gameTimeGetTime());
    va_list args;
    va_start(args, fmt);
    vfprintf(gOracleTrace, fmt, args);
    va_end(args);
    fputc('\n', gOracleTrace);
}

static int oracleObjId(Object* obj)
{
    return obj != nullptr ? obj->id : -1;
}

void oracleTraceRng(int min, int max, int result, const char* file, const char* func)
{
    const char* base = strrchr(file, '/');
    oracleLine("RNG %d..%d -> %d site=%s:%s", min, max, result, base != nullptr ? base + 1 : file, func);
}

void oracleTraceProc(Object* self, const char* programName, const char* procName)
{
    char name[64];
    const char* base = programName;
    for (const char* p = programName; *p != '\0'; p++) {
        if (*p == '\\' || *p == '/') {
            base = p + 1;
        }
    }
    size_t i = 0;
    for (; base[i] != '\0' && i < sizeof(name) - 1; i++) {
        name[i] = (char)tolower((unsigned char)base[i]);
    }
    name[i] = '\0';
    oracleLine("PROC %s %s self=obj#%d", name, procName, oracleObjId(self));
}

void oracleTraceGvar(int index, int oldValue, int newValue) { oracleLine("GVAR %d %d->%d", index, oldValue, newValue); }
void oracleTraceMvar(int index, int oldValue, int newValue) { oracleLine("MVAR %d %d->%d", index, oldValue, newValue); }
void oracleTraceLvar(int sid, int index, int oldValue, int newValue) { oracleLine("LVAR sid=0x%08X %d %d->%d", sid, index, oldValue, newValue); }
void oracleTraceTurn(Object* obj) { oracleLine("TURN obj#%d ap=%d", oracleObjId(obj), obj->data.critter.combat.ap); }

void oracleTraceAttack(Object* attacker, Object* defender, int chance, bool hit, int damage, int defenderFlags)
{
    oracleLine("ATTACK obj#%d->obj#%d chance=%d hit=%d dmg=%d dflags=0x%X",
        oracleObjId(attacker), oracleObjId(defender), chance, hit ? 1 : 0, damage, defenderFlags);
}

void oracleTraceMove(Object* obj, int fromTile, int toTile)
{
    if (fromTile == toTile || PID_TYPE(obj->pid) != OBJ_TYPE_CRITTER) {
        return;
    }
    oracleLine("MOVE obj#%d %d->%d", obj->id, fromTile, toTile);
}

void oracleTraceSpawn(Object* obj)
{
    oracleLine("SPAWN obj#%d pid=0x%08X tile=%d", obj->id, obj->pid, obj->tile);
}
```
Also add `void oracleSuppress(bool on) { gOracleSuppress += on ? 1 : -1; }` to `oracle.h/.cc` and flush/close the file in an `atexit` handler registered by `oracleTraceOpen`.

- [ ] **Step 2: Replace the Task 2 stub in `random.cc`**

Delete the `static inline void oracleTraceRng(...) {}` stub and `#include "oracle.h"`. The call at the end of `randomBetweenAt` now reaches the real sink. `randomBetweenCosmetic` must not call it.

- [ ] **Step 3: Hook script procs** — `src/scripts.cc` `scriptExecProc`, immediately before `_executeProcedure(program, v9);` (`:1338`):
```cpp
    oracleTraceProc(script->owner, program->name, gScriptProcNames[proc]);
```
Also hook the dialog-reply path that bypasses `scriptExecProc`: `src/game_dialog.cc:2081` `_executeProcedure(gDialogReplyProgram, ...)` — add `oracleTraceProc(gGameDialogSpeaker, gDialogReplyProgram->name, "<reply>");` before it. (Dialog is out of M1 but the hook is free and keeps M2 honest.)

- [ ] **Step 4: Hook variable writes**
- `src/game.cc` `gameSetGlobalVar` (`:995`): just before the store at `:1020`, `oracleTraceGvar(var, gGameGlobalVars[var], value);`.
- `src/map.cc` `mapSetGlobalVar` (`:397`): before the store, `oracleTraceMvar(var, gMapGlobalVars[var], value.integerValue);`.
- `src/map.cc` `mapSetLocalVar` (`:435`): before the store at `:443-446`, `oracleTraceLvar(<sid of the calling script if available, else -1>, var, gMapLocalVars[var], value.integerValue);`. Read `scriptSetLocalVar` (`scripts.cc:2819`) — it knows the sid and computes the absolute index; prefer hooking there instead with `oracleTraceLvar(sid, variable, <old>, value.integerValue)` and leave `mapSetLocalVar` unhooked, so the trace carries the sid + script-relative index (what Hexwaste's `SetLocalVar(index)` also has).

- [ ] **Step 5: Hook combat**
- `src/combat.cc` `_combat_turn` (`:3225`): first statement after `_combat_turn_obj = obj;` → `oracleTraceTurn(obj);`.
- `src/combat.cc` `attackCompute` (`:3819`): immediately before the final `return 0;` (`:3982`):
```cpp
    oracleTraceAttack(attack->attacker, attack->defender, accuracy,
        (attack->attackerFlags & DAM_HIT) != 0, attack->defenderDamage, attack->defenderFlags);
```

- [ ] **Step 6: Hook objects**
- `src/object.cc` `objectSetLocation` (`:1354`): capture `int oldTile = obj->tile;` at entry; at the successful end, `oracleTraceMove(obj, oldTile, tile);`.
- `src/object.cc` `objectCreateWithFidPid` (`:890`) and `objectCreateWithPid` (`:981`): after `id` is assigned (`:913`, `:1024`), `oracleTraceSpawn(obj);`.

- [ ] **Step 7: Build and check the hooks compile and stay silent without a trace path**

```bash
bash tools/fo2ce-trace/export-patch.sh && bash tools/fo2ce-trace/build.sh
bash tools/fo2ce-trace/run.sh exit_after_ms=3000; echo "exit=$?"
```
Expected: `exit=0`, no trace file created (no `trace=` key).

- [ ] **Step 8: Commit**
```bash
bash tools/fo2ce-trace/export-patch.sh
git add tools/fo2ce-trace/trace.patch
git commit -m "feat(oracle): fo2ce trace sink + RNG/proc/var/combat/object hooks"
```

---

### Task 5: Headless save maker (`make_save` recipes)

**Files:**
- Create: `tests/golden-oracle/artemple-idle/recipe.txt`, `tests/golden-oracle/klamath-hour/recipe.txt`, `tests/golden-oracle/arcaves-ambush/recipe.txt`, `scripts/oracle-make-save.sh`
- Worktree: `src/oracle.cc/.h`, `src/main.cc`, `src/loadsave.cc/.h`

**Interfaces:**
- Produces (C++, `loadsave.h`):
  ```cpp
  int lsgSaveGameHeadless(int slot, const char* description);  // writes SAVEGAME\SLOT<slot+1>, no UI
  int lsgLoadGameHeadless(int slot);                           // loads SAVEGAME\SLOT<slot+1>, no UI
  ```
- Produces (recipe grammar, one command per line, `#` comments):
  ```
  premade <combat|stealth|diplomat>
  map <name.map>
  place <tile> <elevation> <rotation>
  gvar <index> <value>
  run-ms <ms>                 # let the engine run (map_enter etc.) before saving
  save <description>
  ```
- Produces (shell): `scripts/oracle-make-save.sh <fixture>` → `game-data/oracle-saves/<fixture>/` (a copy of the fo2ce `SLOT10` directory).

- [ ] **Step 1: Headless save/load wrappers in `loadsave.cc`**

`lsgSaveGame`'s quick path (`:363-436`) and `lsgLoadGame`'s quick path (`:893-909`) skip the UI when `_quick_done` is true; they use `_slot_cursor`. `lsgLoadGameInSlot` uses `_slot_cursor` for the path and `slot` for `_LSData[slot]`, so both must be equal. Add to `loadsave.cc` (and declare in `loadsave.h`):
```cpp
// ORACLE: no-UI save/load into a fixed slot (tools/fo2ce-trace).
int lsgSaveGameHeadless(int slot, const char* description)
{
    _slot_cursor = slot;
    _quick_done = true;
    strncpy(_LSData[slot].description, description, LOAD_SAVE_DESCRIPTION_LENGTH - 1);
    _LSData[slot].description[LOAD_SAVE_DESCRIPTION_LENGTH - 1] = '\0';
    return lsgPerformSaveGame();
}

int lsgLoadGameHeadless(int slot)
{
    _slot_cursor = slot;
    _quick_done = true;
    return lsgLoadGameInSlot(slot);
}
```
`lsgPerformSaveGame` calls `_QuickSnapShot` only from `lsgSaveGame`; check whether `lsgPerformSaveGame` itself needs `_snapshotBuf` (the thumbnail written into the header). If it does, call `_QuickSnapShot()` first inside `lsgSaveGameHeadless`; it reads `gIsoWindow`, which exists under the offscreen driver.

- [ ] **Step 2: Recipe interpreter in `oracle.cc`**

Read `[oracle]make_save` (path) in `oracleInit` and expose `const char* oracleMakeSavePath();`. Implement `int oracleRunRecipe(const char* path)`:
```cpp
int oracleRunRecipe(const char* path)
{
    FILE* in = fopen(path, "rt");
    if (in == nullptr) {
        return -1;
    }
    char line[256];
    while (fgets(line, sizeof(line), in) != nullptr) {
        char* hash = strchr(line, '#');
        if (hash != nullptr) *hash = '\0';
        char cmd[32] = { 0 }, a[128] = { 0 };
        int n1 = 0, n2 = 0, n3 = 0;
        if (sscanf(line, "%31s", cmd) != 1) continue;
        if (strcmp(cmd, "premade") == 0 && sscanf(line, "%*s %127s", a) == 1) {
            char gcd[160];
            snprintf(gcd, sizeof(gcd), "premade\\%s.gcd", a);
            if (_proto_dude_init(gcd) == -1) return oracleFail("premade", a);
        } else if (strcmp(cmd, "map") == 0 && sscanf(line, "%*s %127s", a) == 1) {
            oracleLoadNewMap(a);
        } else if (strcmp(cmd, "place") == 0 && sscanf(line, "%*s %d %d %d", &n1, &n2, &n3) == 3) {
            objectSetLocation(gDude, n1, n2, nullptr);
            objectSetRotation(gDude, n3, nullptr);
        } else if (strcmp(cmd, "gvar") == 0 && sscanf(line, "%*s %d %d", &n1, &n2) == 2) {
            gameSetGlobalVar(n1, n2);
        } else if (strcmp(cmd, "run-ms") == 0 && sscanf(line, "%*s %d", &n1) == 1) {
            oracleRunFor((unsigned int)n1);
        } else if (strcmp(cmd, "save") == 0) {
            const char* desc = line + 4;
            while (*desc == ' ') desc++;
            if (lsgSaveGameHeadless(9, desc) == -1) return oracleFail("save", desc);
        } else {
            return oracleFail("unknown recipe command", cmd);
        }
    }
    fclose(in);
    return 0;
}
```
with helpers:
- `oracleFail(const char* what, const char* arg)`: `fprintf(stderr, "oracle: recipe failed at %s %s\n", what, arg); return -1;`
- `oracleRunFor(ms)`: `unsigned int end = oracleTicks() + ms; while (oracleTicks() < end) { inputGetInput(); scriptsHandleRequests(); mapHandleTransition(); }` — the same body as `mainLoop` minus rendering/key handling.
- `oracleLoadNewMap(name)`: the body of `main.cc` `_main_load_new` (`:261-284`, static there): `_game_user_wants_to_quit = 0; gDude->flags &= ~OBJECT_FLAT; objectShow(gDude, nullptr); _map_init(); mapLoadByName(name);`. Because `_main_load_new` is static in `main.cc`, implement `oracleRunRecipe` call-out from `main.cc` instead, or make `_main_load_new` non-static and declare it in `main.h` — choose the latter (smaller diff).

- [ ] **Step 3: Call the recipe from `main.cc`**

In `falloutMain`, after `falloutInit` and the `rng_dump` branch:
```cpp
    if (oracleEnabled() && oracleMakeSavePath()[0] != '\0') {
        randomSeedPrerandom(1);          // fixed (vanilla new-game reseed is wall-clock, main.cc:115)
        int rc = oracleRunRecipe(oracleMakeSavePath());
        exit(rc == 0 ? 0 : 3);
    }
```
The recipe runs before any scenario seed; RNG use during save-making is irrelevant to the comparison (the oracle reseeds after loading, Task 6) but a fixed seed keeps saves reproducible.

- [ ] **Step 4: Write the three recipes**

`tests/golden-oracle/artemple-idle/recipe.txt`:
```
# Smallest end-to-end fixture: the Temple of Trials entrance, nothing hostile in view.
premade combat
map artemple.map
run-ms 2000
save oracle artemple-idle
```
`tests/golden-oracle/klamath-hour/recipe.txt`:
```
# Klamath downtown: NPC wander + timed events + map_update over one game hour.
premade combat
map kladwtwn.map
run-ms 2000
save oracle klamath-hour
```
`tests/golden-oracle/arcaves-ambush/recipe.txt` — the dude must start out of combat but inside a radscorpion's perception. The combat goldens put the dude next to the radscorpion at hex 20529 (`scripts/combat-golden.sh:25`). Pick a tile 5–7 hexes from 20529 that the scorpion can see: run
`FALLOUT2_DIR=$PWD/game-data dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --no-audio --map arcaves.map --can-see-probe 20529 <tile>` for candidate tiles (e.g. 20131, 20133, 20329) and use the first that reports visible and unblocked. Then:
```
# Radscorpions at 20529 perceive the dude and open combat on their own; the dude only ends turns.
premade combat
map arcaves.map
place <TILE-FROM-PROBE> 0 0
run-ms 500
save oracle arcaves-ambush
```
(Replace `<TILE-FROM-PROBE>` with the verified tile number before committing — the recipe must contain a literal integer.)

- [ ] **Step 5: Write `scripts/oracle-make-save.sh`**
```bash
#!/usr/bin/env bash
# Build a fixture's vanilla save with the headless oracle fo2ce, then copy the slot out to
# game-data/oracle-saves/<fixture>/ (gitignored — saves embed game data).
# Usage: scripts/oracle-make-save.sh <fixture>
set -euo pipefail
cd "$(dirname "$0")/.."
FIX="${1:?usage: $0 <fixture>}"
RECIPE="$PWD/tests/golden-oracle/$FIX/recipe.txt"
RUN=reference/fallout2-ce/run
SLOT=$(find "$RUN" -maxdepth 3 -type d -iname SAVEGAME | head -1)
[ -n "$SLOT" ] || SLOT="$RUN/data/SAVEGAME"
rm -rf "$SLOT/SLOT10"
bash tools/fo2ce-trace/run.sh make_save="$RECIPE"
[ -f "$SLOT/SLOT10/SAVE.DAT" ] || { echo "no SAVE.DAT produced in $SLOT/SLOT10" >&2; exit 1; }
DEST=game-data/oracle-saves/$FIX
rm -rf "$DEST"; mkdir -p "$DEST"
cp -r "$SLOT/SLOT10/." "$DEST/"
ls -l "$DEST"
```

- [ ] **Step 6: Build, make all three saves, verify**
```bash
bash tools/fo2ce-trace/export-patch.sh && bash tools/fo2ce-trace/build.sh
for f in artemple-idle klamath-hour arcaves-ambush; do bash scripts/oracle-make-save.sh "$f"; done
head -c 18 game-data/oracle-saves/artemple-idle/SAVE.DAT; echo
```
Expected: three slot dirs each with `SAVE.DAT`, one or more `*.SAV`, `AUTOMAP.SAV`; the header prints `FALLOUT SAVE FILE`.

If the headless save path crashes or blocks (risk in the spec): fall back to hand-played saves — play to each state in the visible community binary only after verifying `git -C reference/fallout2-ce diff e97087b..community/main -- src/loadsave.cc src/object.cc src/proto.cc src/combat.cc src/worldmap.cc src/queue.cc src/party_member.cc` changes no read/write layout; record the outcome in `tools/fo2ce-trace/README.md`.

- [ ] **Step 7: Commit**
```bash
bash tools/fo2ce-trace/export-patch.sh
git add tools/fo2ce-trace/trace.patch tests/golden-oracle scripts/oracle-make-save.sh
git commit -m "feat(oracle): headless recipe-driven vanilla save maker + 3 M1 fixture recipes"
```

---

### Task 6: fo2ce scenario runner (load slot, seed, wait/end-turn, trace)

**Files:**
- Create: `tests/golden-oracle/{artemple-idle,klamath-hour,arcaves-ambush}/scenario.txt`, `scripts/oracle-selfcheck.sh`
- Worktree: `src/oracle.cc/.h`, `src/main.cc`, `src/combat.cc`

**Interfaces:**
- Consumes: `lsgLoadGameHeadless` (Task 5), `oracleRandomSeed` (Task 2), `oracleTraceOpen` (Task 4).
- Produces (scenario grammar, shared with Hexwaste Task 10):
  ```
  seed <int>            # required, first non-comment line
  wait <ms>             # run the engine for <ms> virtual ms
  end-turn [count]      # default 1; see semantics below
  ```
  **`end-turn` semantics (both engines):** repeat `count` times: (1) run until the dude is awaiting combat input, or combat is not active (cap 600000 ms) — if combat is not active, the action ends; (2) end the dude's turn; (3) run until the dude is awaiting input again or combat has ended (same cap).
- Produces (fo2ce config keys): `[oracle]load_slot=<dir copied into SLOT10>` is NOT used — the script copies the fixture dir into `SLOT10` itself; `[oracle]scenario=<path>`, `[oracle]trace=<path>`, `[oracle]fixture=<name>`.

- [ ] **Step 1: Mark "dude awaiting combat input"**

`src/combat.cc` `_combat_input` (`:3134`): set `oracleSetDudeAwaitingInput(true)` immediately before `int keyCode = inputGetInput();` and `oracleSetDudeAwaitingInput(false)` right after it. Add the flag + accessor `bool oracleDudeAwaitingInput()` to `oracle.cc/.h`.

- [ ] **Step 2: Scenario runner**

In `oracle.cc`, `int oracleRunScenario(const char* path)`:
```cpp
int oracleRunScenario(const char* path)
{
    FILE* in = fopen(path, "rt");
    if (in == nullptr) return -1;
    char line[128];
    bool seeded = false;
    while (fgets(line, sizeof(line), in) != nullptr) {
        char* hash = strchr(line, '#');
        if (hash != nullptr) *hash = '\0';
        char cmd[32] = { 0 };
        int n = 0;
        if (sscanf(line, "%31s", cmd) != 1) continue;
        if (strcmp(cmd, "seed") == 0 && sscanf(line, "%*s %d", &n) == 1) {
            oracleRandomSeed(n);
            seeded = true;
        } else if (!seeded) {
            return oracleFail("scenario must start with seed", cmd);
        } else if (strcmp(cmd, "wait") == 0 && sscanf(line, "%*s %d", &n) == 1) {
            oracleRunFor((unsigned int)n);
        } else if (strcmp(cmd, "end-turn") == 0) {
            int count = sscanf(line, "%*s %d", &n) == 1 ? n : 1;
            for (int i = 0; i < count; i++) {
                if (!oracleRunUntilDudeInputOrNoCombat(600000)) break;
                enqueueInputEvent(KEY_SPACE);   // _combat_input: space ends the turn
                oracleRunFor(10);
                oracleRunUntilDudeInputOrNoCombat(600000);
            }
        } else {
            return oracleFail("unknown scenario command", cmd);
        }
    }
    fclose(in);
    return 0;
}

// true when the dude is awaiting combat input; false when combat is not active (or cap hit).
static bool oracleRunUntilDudeInputOrNoCombat(unsigned int capMs)
{
    unsigned int end = oracleTicks() + capMs;
    while (oracleTicks() < end) {
        if (!isInCombat()) return false;
        if (oracleDudeAwaitingInput()) return true;
        inputGetInput();
    }
    fprintf(stderr, "oracle: end-turn cap reached\n");
    return false;
}
```
**Caveat to verify while implementing:** while combat runs, control is inside `_combat()`'s own loop (`combat.cc:3378`), not in our runner — `inputGetInput()` from the runner only advances bk processes. The runner therefore cannot be a plain sequential loop once combat starts. Restructure the runner as a **step machine driven from `tickersExecute()`**: keep the parsed command list + index + per-command state in statics, and call `oracleScenarioStep()` from `oracleTick()`. Each step inspects state (`oracleDudeAwaitingInput()`, `isInCombat()`, clock) and either advances to the next command, enqueues `KEY_SPACE`, or does nothing. When the list is exhausted: close the trace and `exit(0)`. The sequential code above is the *specification* of the state machine; implement it as states `{WAIT_UNTIL(clock), ENDTURN_SEEK_INPUT, ENDTURN_PRESSED, ENDTURN_SEEK_NEXT}`. `KEY_SPACE` ends the dude's turn: `_combat_input` breaks out of its loop on it (`combat.cc:3171`); `KEY_RETURN` would instead attempt to end combat (`:3175`).

- [ ] **Step 3: Entry point in `main.cc`**

In `falloutMain`, after the `make_save` branch:
```cpp
    if (oracleEnabled() && oracleScenarioPath()[0] != '\0') {
        main_loadgame_new();                 // main.cc:289-305 prep (make non-static if needed)
        if (lsgLoadGameHeadless(9) == -1) exit(4);
        char header[256];
        snprintf(header, sizeof(header), "oracle v1 fixture=%s engine=fo2ce@e97087b+trace.patch@%s",
            oracleFixtureName(), ORACLE_PATCH_SHA);
        oracleTraceOpen(oracleTracePath(), header);
        oracleScenarioBegin(oracleScenarioPath());   // parses, seeds, arms the step machine
        mainLoop();                                  // static in main.cc; the step machine exits
        exit(5);                                     // only reached if the scenario never finished
    }
```
Define `ORACLE_PATCH_SHA` fallback `"unknown"` in `oracle.h` via `#ifndef`.

- [ ] **Step 4: Scenario files**

`tests/golden-oracle/artemple-idle/scenario.txt`:
```
seed 12345
wait 60000
```
`tests/golden-oracle/klamath-hour/scenario.txt`:
```
seed 12345
wait 3600000
```
(fo2ce advances game time by 1 tick per ≥100 ms of bk time when not in combat — `scripts.cc:766-771` — so 3 600 000 virtual ms ≈ one game hour. If a run takes over ~10 minutes of wall time, reduce to `wait 600000` and rename the fixture `klamath-10min`; note it in the README.)

`tests/golden-oracle/arcaves-ambush/scenario.txt`:
```
seed 12345
wait 5000
end-turn 5
```

- [ ] **Step 5: Self-determinism script**

`scripts/oracle-selfcheck.sh`:
```bash
#!/usr/bin/env bash
# Run a fixture's fo2ce scenario twice with the same seed; the traces must be byte-identical.
# Usage: scripts/oracle-selfcheck.sh <fixture>
set -euo pipefail
cd "$(dirname "$0")/.."
FIX="${1:?usage: $0 <fixture>}"
OUT="$PWD/scratch/oracle-runs/selfcheck-$FIX"; mkdir -p "$OUT"
RUN=reference/fallout2-ce/run
SLOT=$(find "$RUN" -maxdepth 3 -type d -iname SAVEGAME | head -1)
for pass in 1 2; do
  rm -rf "$SLOT/SLOT10"; mkdir -p "$SLOT/SLOT10"
  cp -r "game-data/oracle-saves/$FIX/." "$SLOT/SLOT10/"
  bash tools/fo2ce-trace/run.sh fixture="$FIX" \
    scenario="$PWD/tests/golden-oracle/$FIX/scenario.txt" trace="$OUT/pass$pass.trace"
done
if cmp -s "$OUT/pass1.trace" "$OUT/pass2.trace"; then
  echo "SELFCHECK OK $FIX ($(wc -l < "$OUT/pass1.trace") lines)"
else
  echo "SELFCHECK FAIL $FIX"; diff "$OUT/pass1.trace" "$OUT/pass2.trace" | head -20; exit 1
fi
```

- [ ] **Step 6: Build and run the gate**
```bash
bash tools/fo2ce-trace/export-patch.sh && bash tools/fo2ce-trace/build.sh
for f in artemple-idle klamath-hour arcaves-ambush; do bash scripts/oracle-selfcheck.sh "$f"; done
grep -c ' ATTACK ' scratch/oracle-runs/selfcheck-arcaves-ambush/pass1.trace
```
Expected: three `SELFCHECK OK` lines; the arcaves trace has ≥ 1 `ATTACK` line (if 0, the scorpions never engaged — revisit the Task 5 tile choice). A `SELFCHECK FAIL` means some nondeterminism is left: look at the first differing line's `site=` — a cosmetic draw that was classified logic (fix the audit + substitution) or a remaining real-time source (`compat_timeGetTime`, `std::rand`, `SDL_GetTicks` outside `getTicks`).

- [ ] **Step 7: Commit**
```bash
bash tools/fo2ce-trace/export-patch.sh
git add tools/fo2ce-trace/trace.patch tests/golden-oracle scripts/oracle-selfcheck.sh
git commit -m "feat(oracle): fo2ce scenario runner (seed/wait/end-turn) + self-determinism gate"
```

---

### Task 7: Trace model, comparer, scenario parser and `TraceDiff` tool (C#)

**Files:**
- Create: `src/Hexwaste.Formats/Trace/OracleTrace.cs`, `src/Hexwaste.Formats/Trace/TraceComparer.cs`, `src/Hexwaste.Formats/Trace/OracleScenario.cs`, `tools/TraceDiff/TraceDiff.csproj`, `tools/TraceDiff/Program.cs`, `tests/Hexwaste.Formats.Tests/TraceComparerTests.cs`, `tests/Hexwaste.Formats.Tests/OracleScenarioTests.cs`
- Modify: `Hexwaste.slnx` (add `tools/TraceDiff/TraceDiff.csproj` under `/tools/`)

**Interfaces:**
- Produces:
  ```csharp
  namespace Hexwaste.Formats.Trace;
  public static class OracleTrace
  {
      public static bool Active { get; }
      public static Func<uint>? Clock { get; set; }     // game ticks source
      public static void Open(TextWriter writer, string header);
      public static void Close();
      public static void Rng(int min, int max, int result, string site);
      public static void Proc(int selfId, string programName, string procName);
      public static void Gvar(int index, int oldValue, int newValue);
      public static void Mvar(int index, int oldValue, int newValue);
      public static void Lvar(int sid, int index, int oldValue, int newValue);
      public static void Turn(int objId, int ap);
      public static void Attack(int attackerId, int defenderId, int chance, bool hit, int damage, int defenderFlags);
      public static void Move(int objId, int fromTile, int toTile);
      public static void Spawn(int objId, int pid, int tile);
  }
  public sealed record TraceDivergence(int LineNumber, string? Expected, string? Actual, IReadOnlyList<string> Context);
  public static class TraceComparer
  {
      public static string StripSite(string line);
      public static TraceDivergence? FirstDivergence(IReadOnlyList<string> expected, IReadOnlyList<string> actual, int contextLines = 20);
  }
  public abstract record ScenarioAction { public sealed record Wait(int Ms); public sealed record EndTurn(int Count); }
  public sealed record OracleScenario(int Seed, IReadOnlyList<ScenarioAction> Actions)
  {
      public static OracleScenario Parse(string text);
  }
  ```

- [ ] **Step 1: Failing tests**

`tests/Hexwaste.Formats.Tests/TraceComparerTests.cs`:
```csharp
using Hexwaste.Formats.Trace;

namespace Hexwaste.Formats.Tests;

public class TraceComparerTests
{
    [Fact]
    public void IdenticalTracesHaveNoDivergence() =>
        Assert.Null(TraceComparer.FirstDivergence(["# h", "t=1 GVAR 1 0->1"], ["# other", "t=1 GVAR 1 0->1"]));

    [Fact]
    public void SiteFieldIsIgnored() =>
        Assert.Null(TraceComparer.FirstDivergence(
            ["t=5 RNG 1..100 -> 37 site=combat.cc:attackCompute"],
            ["t=5 RNG 1..100 -> 37 site=CombatEngine.TryAttack"]));

    [Fact]
    public void ReportsFirstMismatchWithContext()
    {
        string[] a = ["# h", "t=1 GVAR 1 0->1", "t=2 RNG 1..6 -> 3 site=x", "t=3 TURN obj#7 ap=8"];
        string[] b = ["# h", "t=1 GVAR 1 0->1", "t=2 RNG 1..6 -> 4 site=y", "t=3 TURN obj#7 ap=8"];
        TraceDivergence d = TraceComparer.FirstDivergence(a, b, contextLines: 1)!;
        Assert.Equal(3, d.LineNumber);
        Assert.Equal("t=2 RNG 1..6 -> 3 site=x", d.Expected);
        Assert.Equal("t=2 RNG 1..6 -> 4 site=y", d.Actual);
        Assert.Equal(["t=1 GVAR 1 0->1"], d.Context);
    }

    [Fact]
    public void ShorterTraceDivergesAtItsEnd()
    {
        TraceDivergence d = TraceComparer.FirstDivergence(["t=1 GVAR 1 0->1", "t=2 GVAR 2 0->1"], ["t=1 GVAR 1 0->1"])!;
        Assert.Equal(2, d.LineNumber);
        Assert.Null(d.Actual);
    }
}
```
`tests/Hexwaste.Formats.Tests/OracleScenarioTests.cs`:
```csharp
using Hexwaste.Formats.Trace;

namespace Hexwaste.Formats.Tests;

public class OracleScenarioTests
{
    [Fact]
    public void ParsesSeedWaitAndEndTurn()
    {
        OracleScenario s = OracleScenario.Parse("# c\nseed 12345\nwait 5000\nend-turn 5\nend-turn\n");
        Assert.Equal(12345, s.Seed);
        Assert.Equal<ScenarioAction>(
            [new ScenarioAction.Wait(5000), new ScenarioAction.EndTurn(5), new ScenarioAction.EndTurn(1)],
            s.Actions);
    }

    [Fact]
    public void RejectsMissingSeed() =>
        Assert.Throws<FormatException>(() => OracleScenario.Parse("wait 10\n"));

    [Fact]
    public void RejectsUnknownCommand() =>
        Assert.Throws<FormatException>(() => OracleScenario.Parse("seed 1\nattack 5\n"));
}
```

- [ ] **Step 2: Run, expect failure**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter "TraceComparerTests|OracleScenarioTests"` → FAIL (types missing).

- [ ] **Step 3: Implement**

`src/Hexwaste.Formats/Trace/TraceComparer.cs`:
```csharp
namespace Hexwaste.Formats.Trace;

public sealed record TraceDivergence(int LineNumber, string? Expected, string? Actual, IReadOnlyList<string> Context);

/// <summary>Lockstep first-divergence comparison for oracle traces. '#' header lines are skipped;
/// the trailing <c>site=</c> field is context only and never compared.</summary>
public static class TraceComparer
{
    public static string StripSite(string line)
    {
        int i = line.IndexOf(" site=", StringComparison.Ordinal);
        return i < 0 ? line : line[..i];
    }

    public static TraceDivergence? FirstDivergence(IReadOnlyList<string> expected, IReadOnlyList<string> actual, int contextLines = 20)
    {
        List<(int Line, string Text)> e = Body(expected), a = Body(actual);
        int n = Math.Max(e.Count, a.Count);
        for (int i = 0; i < n; i++)
        {
            string? ex = i < e.Count ? e[i].Text : null;
            string? ac = i < a.Count ? a[i].Text : null;
            if (ex is not null && ac is not null && StripSite(ex) == StripSite(ac))
                continue;
            int lineNumber = i < e.Count ? e[i].Line : (i < a.Count ? a[i].Line : i + 1);
            var context = e.Skip(Math.Max(0, i - contextLines)).Take(Math.Min(i, contextLines)).Select(x => x.Text).ToList();
            return new TraceDivergence(lineNumber, ex, ac, context);
        }
        return null;
    }

    private static List<(int, string)> Body(IReadOnlyList<string> lines)
    {
        var body = new List<(int, string)>();
        for (int i = 0; i < lines.Count; i++)
            if (!lines[i].StartsWith('#'))
                body.Add((i + 1, lines[i]));
        return body;
    }
}
```
`src/Hexwaste.Formats/Trace/OracleScenario.cs`:
```csharp
namespace Hexwaste.Formats.Trace;

public abstract record ScenarioAction
{
    public sealed record Wait(int Ms) : ScenarioAction;
    public sealed record EndTurn(int Count) : ScenarioAction;
}

/// <summary>The oracle scenario file shared with the fo2ce trace patch (tools/fo2ce-trace,
/// oracle.cc oracleRunScenario): `seed N` first, then `wait MS` / `end-turn [N]`; '#' comments.</summary>
public sealed record OracleScenario(int Seed, IReadOnlyList<ScenarioAction> Actions)
{
    public static OracleScenario Parse(string text)
    {
        int? seed = null;
        var actions = new List<ScenarioAction>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0)
                continue;
            string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (p[0])
            {
                case "seed" when p.Length == 2 && seed is null:
                    seed = int.Parse(p[1]);
                    break;
                case var _ when seed is null:
                    throw new FormatException($"scenario must start with seed, got '{line}'");
                case "wait" when p.Length == 2:
                    actions.Add(new ScenarioAction.Wait(int.Parse(p[1])));
                    break;
                case "end-turn":
                    actions.Add(new ScenarioAction.EndTurn(p.Length == 2 ? int.Parse(p[1]) : 1));
                    break;
                default:
                    throw new FormatException($"unknown scenario command '{line}'");
            }
        }
        return new OracleScenario(seed ?? throw new FormatException("scenario has no seed"), actions);
    }
}
```
`src/Hexwaste.Formats/Trace/OracleTrace.cs`:
```csharp
namespace Hexwaste.Formats.Trace;

/// <summary>Process-wide oracle trace sink (inactive unless Open was called by --trace). Line
/// formats are byte-identical to the fo2ce patch's oracle.cc oracleTrace* functions.</summary>
public static class OracleTrace
{
    private static TextWriter? _writer;

    public static bool Active => _writer is not null;
    public static Func<uint>? Clock { get; set; }

    public static void Open(TextWriter writer, string header)
    {
        _writer = writer;
        _writer.WriteLine($"# {header}");
    }

    public static void Close()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }

    private static void Line(string body)
    {
        if (_writer is null)
            return;
        _writer.Write("t=");
        _writer.Write(Clock?.Invoke() ?? 0u);
        _writer.Write(' ');
        _writer.WriteLine(body);
    }

    public static void Rng(int min, int max, int result, string site) => Line($"RNG {min}..{max} -> {result} site={site}");
    public static void Proc(int selfId, string programName, string procName) => Line($"PROC {programName.ToLowerInvariant()} {procName} self=obj#{selfId}");
    public static void Gvar(int index, int oldValue, int newValue) => Line($"GVAR {index} {oldValue}->{newValue}");
    public static void Mvar(int index, int oldValue, int newValue) => Line($"MVAR {index} {oldValue}->{newValue}");
    public static void Lvar(int sid, int index, int oldValue, int newValue) => Line($"LVAR sid=0x{sid:X8} {index} {oldValue}->{newValue}");
    public static void Turn(int objId, int ap) => Line($"TURN obj#{objId} ap={ap}");
    public static void Attack(int attackerId, int defenderId, int chance, bool hit, int damage, int defenderFlags) =>
        Line($"ATTACK obj#{attackerId}->obj#{defenderId} chance={chance} hit={(hit ? 1 : 0)} dmg={damage} dflags=0x{defenderFlags:X}");
    public static void Move(int objId, int fromTile, int toTile)
    {
        if (fromTile != toTile)
            Line($"MOVE obj#{objId} {fromTile}->{toTile}");
    }
    public static void Spawn(int objId, int pid, int tile) => Line($"SPAWN obj#{objId} pid=0x{pid:X8} tile={tile}");
}
```
Note: `%X` in C prints uppercase without padding — `0x{defenderFlags:X}` matches; `%08X` ↔ `:X8` matches.

`tools/TraceDiff/TraceDiff.csproj`: copy `tools/ProcAnalyze/ProcAnalyze.csproj` verbatim. `tools/TraceDiff/Program.cs`:
```csharp
using Hexwaste.Formats.Trace;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: TraceDiff <fo2ce.trace> <hexwaste.trace>");
    return 2;
}
string[] expected = File.ReadAllLines(args[0]);
string[] actual = File.ReadAllLines(args[1]);
TraceDivergence? d = TraceComparer.FirstDivergence(expected, actual);
if (d is null)
{
    Console.WriteLine($"IDENTICAL ({expected.Count(l => !l.StartsWith('#'))} lines)");
    return 0;
}
Console.WriteLine($"DIVERGED at fo2ce line {d.LineNumber}");
foreach (string c in d.Context)
    Console.WriteLine($"  {c}");
Console.WriteLine($"- fo2ce:    {d.Expected ?? "<end of trace>"}");
Console.WriteLine($"+ hexwaste: {d.Actual ?? "<end of trace>"}");
return 1;
```

- [ ] **Step 4: Run tests + build the tool**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter "TraceComparerTests|OracleScenarioTests" && dotnet build tools/TraceDiff`
Expected: 7 passed; build succeeds.

- [ ] **Step 5: Commit**
```bash
git add src/Hexwaste.Formats/Trace tools/TraceDiff tests/Hexwaste.Formats.Tests/TraceComparerTests.cs tests/Hexwaste.Formats.Tests/OracleScenarioTests.cs Hexwaste.slnx
git commit -m "feat(oracle): trace sink, first-divergence comparer, scenario parser, TraceDiff tool"
```

---

### Task 8: Vanilla `SAVE.DAT` reader — fixed sections, gvars, map list

**Files:**
- Create: `src/Hexwaste.Formats/Save/Vanilla/VanillaSave.cs`, `src/Hexwaste.Formats/Save/Vanilla/VanillaSaveContext.cs`, `src/Hexwaste.Formats/Save/Vanilla/SaveDatReader.cs`, `src/Hexwaste.Formats/Save/Vanilla/SlotFiles.cs`, `tests/Hexwaste.Formats.Tests/SaveDatReaderTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace Hexwaste.Formats.Save.Vanilla;
  public sealed record VanillaSaveContext(int GlobalVarCount, int PartyDescriptionCount,
      IReadOnlyList<int> PartyDescriptionPids, Func<int, bool> HasCustomAiPacket, ProtoDatabase? Protos)
  {
      public static VanillaSaveContext FromGameData(GameFileSystem vfs, ProtoDatabase protos);
  }
  public sealed record SaveHeader(string CharacterName, string Description, int GameMonth, int GameDay, int GameYear,
      uint GameTime, int Elevation, int MapIndex, string MapFileName);
  public sealed class VanillaSave { /* properties listed in Step 3 */ }
  public static class SaveDatReader { public static VanillaSave Read(Stream saveDat, VanillaSaveContext ctx); }
  public static class SlotFiles
  {
      public static Stream OpenMaybeGzip(string path);   // gunzips when the file starts 1F 8B
  }
  ```

All values big-endian (fallout2-ce `db.cc` `fileReadInt32` :321 / `fileReadInt16` :292 / `fileReadFloat` :349). Reuse `BigEndianReader`; add `public float ReadSingle()` (`BitConverter.Int32BitsToSingle(ReadInt32())`) to it.

- [ ] **Step 1: Failing tests (hermetic, hand-built bytes)**

`tests/Hexwaste.Formats.Tests/SaveDatReaderTests.cs` — a builder writes a minimal valid stream (header + all 27 sections, combat inactive, empty queue, 1 party description, 0 custom AI packets, dude with empty inventory) and checks every parsed field round-trips:
```csharp
using System.Buffers.Binary;
using Hexwaste.Formats.Save.Vanilla;

namespace Hexwaste.Formats.Tests;

public class SaveDatReaderTests
{
    private sealed class Be
    {
        public readonly MemoryStream S = new();
        public Be I32(int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); S.Write(b); return this; }
        public Be I16(short v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteInt16BigEndian(b, v); S.Write(b); return this; }
        public Be U8(byte v) { S.WriteByte(v); return this; }
        public Be Str(string s, int len) { var b = new byte[len]; System.Text.Encoding.ASCII.GetBytes(s).CopyTo(b, 0); S.Write(b); return this; }
        public Be Zeros(int n) { S.Write(new byte[n]); return this; }
        public Be Many(int count, int v) { for (int i = 0; i < count; i++) I32(v); return this; }
    }

    private static readonly VanillaSaveContext Ctx = new(
        GlobalVarCount: 3, PartyDescriptionCount: 1, PartyDescriptionPids: [-1],
        HasCustomAiPacket: _ => false, Protos: null);

    private static Be Header(Be w) => w
        .Str("FALLOUT SAVE FILE", 24).I16(1).I16(2).U8((byte)'R')
        .Str("Narg", 32).Str("oracle test", 30)
        .I16(10).I16(10).I16(2026).I32(0)            // real date + meaningless fileTime
        .I16(7).I16(25).I16(2241).I32(302400)        // game date + game time
        .I16(0).I16(2).Str("ARTEMPLE.SAV", 16)
        .Zeros(29792 + 128);

    internal static byte[] MinimalSave()
    {
        var w = Header(new Be());
        w.I32(18000);                                // 2  dude cid
        w.I32(1).I32(0).I32(5);                      // 3  gvars
        w.I32(1).Str("ARTEMPLE.SAV", 13).I32(1234);  // 4  map list (NUL-terminated) + automap size
        w.I32(1).I32(0).I32(5);                      // 5  gvars copy
        // 6  dude object: 18 ints, then objectDataRead critter part (inv len, cap, ptr, field0, 7 combat, hp, rad, poison), then center tile
        w.I32(18000).I32(20100).I32(0).I32(0).I32(0).I32(0).I32(0).I32(3).I32(0x01000001).I32(0)
         .I32(0).I32(0x01000000).I32(-1).I32(4).I32(65536).I32(0).I32(-1).I32(-1);
        w.I32(0).I32(0).I32(0).I32(0).Many(7, 0).I32(30).I32(0).I32(0);
        w.I32(20100);
        w.I32(0).Many(35, 5).Many(35, 0).Many(18, 10).I32(0).I32(0).I32(0).I32(0); // 7 critter (376 B)
        w.Many(19, 0);                               // 8  kills
        w.I32(4).I32(0).I32(5).I32(-1);              // 9  tagged skills
        //                                             10 random: 0 bytes
        w.Many(1 * 119, 0);                          // 11 perks (1 description)
        w.I32(0);                                    // 12 combat state: inactive
        //                                             13 ai: 0 custom packets
        w.I32(0).I32(1).I32(0).I32(0).I32(0);        // 14 pc stats
        //                                             15 items: 0 bytes
        w.I32(-1).I32(-1);                           // 16 traits
        w.I32(0);                                    // 17 automap flags
        w.Many(13, 1).I32(0).Many(4, 22).I32(0).I32(0); // 18 preferences (floats as raw bits)
        w.I32(1).U8(0);                              // 19 character editor
        w.I32(0).I32(0).I32(10).I32(20).I32(0).I32(-1).I32(-1).I32(-1).I32(0).I32(-1).I32(0); // 20 worldmap fixed
        w.I32(0);                                    //    0 cities
        w.I32(0).I32(0);                             //    0 tiles, 0 horizontal
        w.I32(0);                                    //    0 counters
        //                                             21 pipboy: 0 bytes
        w.Zeros(17);                                 // 22 movies
        w.Many(18 * 3, 0);                           // 23 skill usage
        w.I32(1).I32(0);                             // 24 party: length 1 (dude only), item count; 0 ids; 0 descriptions
        w.I32(0);                                    // 25 queue: 0 events
        w.I32(1).I32(0).I32(1).I32(0);               // 26 interface
        return w.S.ToArray();
    }

    [Fact]
    public void ReadsMinimalSaveEndToEnd()
    {
        VanillaSave save = SaveDatReader.Read(new MemoryStream(MinimalSave()), Ctx);
        Assert.Equal("Narg", save.Header.CharacterName);
        Assert.Equal(302400u, save.Header.GameTime);
        Assert.Equal("ARTEMPLE.SAV", save.Header.MapFileName);
        Assert.Equal(18000, save.DudeCid);
        Assert.Equal([1, 0, 5], save.GlobalVars);
        Assert.Equal(["ARTEMPLE.SAV"], save.MapFiles);
        Assert.Equal(20100, save.Dude.HexTile);
        Assert.Equal(30, save.Dude.CurrentHp);
        Assert.Equal(20100, save.DudeCenterTile);
        Assert.Equal(5, save.Critter.BaseStats[0]);
        Assert.Equal([4, 0, 5, -1], save.TaggedSkills);
        Assert.False(save.Combat.Active);
        Assert.Equal(1, save.PcStats.Level);
        Assert.Equal([-1, -1], save.Traits);
        Assert.Equal(10, save.Worldmap.PosX);
        Assert.Empty(save.PartyMemberIds);
        Assert.Empty(save.Queue);
        Assert.Equal(save.TotalLength, MinimalSave().Length);  // consumed exactly
    }

    [Fact]
    public void RejectsBadSignature()
    {
        byte[] bytes = MinimalSave();
        bytes[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => SaveDatReader.Read(new MemoryStream(bytes), Ctx));
    }

    [Fact]
    public void RejectsDivergentGvarCopies()
    {
        byte[] bytes = MinimalSave();
        // the second gvar copy's last int sits right before the dude object; corrupt it
        int dudeObjectStart = 30051 + 4 + 12 + (4 + 13 + 4) + 12;
        bytes[dudeObjectStart - 1] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => SaveDatReader.Read(new MemoryStream(bytes), Ctx));
    }
}
```
The dude object needs proto lookups only for non-critter inventory items; with `Protos: null` and an empty inventory none happen.

- [ ] **Step 2: Run, expect failure** — `dotnet test tests/Hexwaste.Formats.Tests --filter SaveDatReaderTests` → FAIL (types missing).

- [ ] **Step 3: Expose object reading from `MapFile`**

In `src/Hexwaste.Formats/Map/MapFile.cs`:
- Add to `MapObject`: `public int Elevation { get; set; }`, `public int Cid { get; set; } = -1;`, `public int ScriptIndex { get; set; } = -1;`, `public int CritterFlags { get; set; }` (the critter `field_0` / `data->flags`, proto.cc objectDataRead).
- In `ReadObject`, stop discarding those fields: store `elevation`, `cid`, `scriptIndex` (the 11th, 13th, 18th ints of the 18-int record — read the method and map each `reader.ReadInt32()`/`Skip` to the `objectRead` order `id, tile, x, y, sx, sy, frame, rotation, fid, flags, elevation, pid, cid, lightDistance, lightIntensity, field_74, sid, scriptIndex`, `object.cc:412`). In `ReadObjectData`, replace `reader.Skip(4); // field_0` with `obj.CritterFlags = reader.ReadInt32();`.
- Change `private static MapObject ReadObject(...)` to `internal static`.
- Add `[assembly: InternalsVisibleTo("Hexwaste.Formats.Tests")]` only if a test needs it (it does not for this task).

Run `dotnet test tests/Hexwaste.Formats.Tests` — all existing map tests must still pass (pure additions).

- [ ] **Step 4: Implement the model**

`src/Hexwaste.Formats/Save/Vanilla/VanillaSave.cs`:
```csharp
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Save.Vanilla;

public sealed record SaveHeader(string CharacterName, string Description, int GameMonth, int GameDay, int GameYear,
    uint GameTime, int Elevation, int MapIndex, string MapFileName);

public sealed record CritterSection(int SneakWorking, int Flags, int[] BaseStats, int[] BonusStats, int[] Skills,
    int BodyType, int Experience, int KillType, int DamageType);

public sealed record CombatantSaveData(int FriendlyDeadId, int LastTargetId, int LastItemId, int LastMove);

public sealed record CombatSection(bool Active, uint State, int TurnRunning, int FreeMove, int Exps,
    int ListCom, int ListNoncom, int DudeCid, int[] CombatantCids, CombatantSaveData[] CombatantData);

public sealed record PcStatsSection(int UnspentSkillPoints, int Level, int Experience, int Reputation, int Karma);

public sealed record CitySave(int X, int Y, int State, int VisitedState, int[] EntranceStates);

public sealed record WorldmapSection(bool DidMeetFrank, int CurrentArea, int PosX, int PosY, bool EncounterIconVisible,
    int EncMapId, int EncTableId, int EncEntryId, bool IsInCar, int CarArea, int CarFuel,
    CitySave[] Cities, int NumHorizontalTiles, int[] SubtileStates, (int Table, int Entry, int Counter)[] Counters);

public sealed record PartyMemberLevel(int Level, int NumLevelUps, bool IsEarly);

public sealed record QueueEventSave(uint Time, int Type, int OwnerId, int[] Data);

public sealed class VanillaSave
{
    public required SaveHeader Header { get; init; }
    public required int DudeCid { get; init; }
    public required int[] GlobalVars { get; init; }
    public required string[] MapFiles { get; init; }
    public required int AutomapDbSize { get; init; }
    public required MapObject Dude { get; init; }
    public required int DudeCenterTile { get; init; }
    public required CritterSection Critter { get; init; }
    public required int[] Kills { get; init; }
    public required int[] TaggedSkills { get; init; }
    public required int[][] PerksByDescription { get; init; }
    public required CombatSection Combat { get; init; }
    public required int[][] CustomAiPackets { get; init; }
    public required PcStatsSection PcStats { get; init; }
    public required int[] Traits { get; init; }
    public required int AutomapFlags { get; init; }
    public required byte[] PreferencesRaw { get; init; }
    public required int CharacterEditorLastLevel { get; init; }
    public required bool CharacterEditorHasFreePerk { get; init; }
    public required WorldmapSection Worldmap { get; init; }
    public required byte[] MoviesSeen { get; init; }
    public required int[] SkillUsage { get; init; }
    public required int PartyItemCount { get; init; }
    public required int[] PartyMemberIds { get; init; }
    public required PartyMemberLevel[] PartyLevels { get; init; }
    public required QueueEventSave[] Queue { get; init; }
    public required int[] InterfaceRaw { get; init; }
    public required long TotalLength { get; init; }
}
```

- [ ] **Step 5: Implement the context**

`src/Hexwaste.Formats/Save/Vanilla/VanillaSaveContext.cs`:
```csharp
using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Save.Vanilla;

/// <summary>Game-data-derived counts SAVE.DAT does not store but its readers depend on.</summary>
public sealed record VanillaSaveContext(int GlobalVarCount, int PartyDescriptionCount,
    IReadOnlyList<int> PartyDescriptionPids, Func<int, bool> HasCustomAiPacket, ProtoDatabase? Protos)
{
    public static VanillaSaveContext FromGameData(GameFileSystem vfs, ProtoDatabase protos)
    {
        // ported from fallout2-ce src/game.cc globalVarsRead("data\\vault13.gam") (:1044)
        int gvars = GameGlobalVars.Parse(ReadText(vfs, @"data\vault13.gam")).Count;
        List<int> pids = PartyDescriptionPidsFrom(ReadText(vfs, @"data\party.txt"));
        AiPacketTable ai = AiPacketTable.Parse(ReadText(vfs, @"data\ai.txt"));
        bool Custom(int pid)
        {
            // ported from fallout2-ce src/combat_ai.cc aiLoad() (:573): critter pid whose proto AI
            // packet has disposition "custom" (index 0 after the keyword shift, :222-229/:480-482).
            if (pid == -1 || Fid.PidType(pid) != 1)
                return false;
            int packet = protos.Get(pid).Critter?.AiPacket ?? -1;
            return ai.Get(packet) is { } p && p.Disposition.Equals("custom", StringComparison.OrdinalIgnoreCase);
        }
        return new VanillaSaveContext(gvars, pids.Count, pids, Custom, protos);
    }

    /// <summary>ported from fallout2-ce src/party_member.cc partyMembersInit() (:133-143): consecutive
    /// "[Party Member N]" sections from N=0 that carry party_member_pid; index 0 is the dude.</summary>
    public static List<int> PartyDescriptionPidsFrom(string text)
    {
        var pids = new List<int>();
        var sections = new Dictionary<int, int>();
        int current = -1;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Split(';')[0].Trim();
            if (line.StartsWith("[Party Member ", StringComparison.OrdinalIgnoreCase) && line.EndsWith(']'))
                current = int.TryParse(line[14..^1].Trim(), out int n) ? n : -1;
            else if (line.StartsWith('['))
                current = -1;
            else if (current >= 0 && line.StartsWith("party_member_pid", StringComparison.OrdinalIgnoreCase)
                     && line.Split('=') is [_, var v] && int.TryParse(v.Trim(), out int pid))
                sections[current] = pid;
        }
        for (int n = 0; sections.TryGetValue(n, out int pid); n++)
            pids.Add(pid);
        return pids;
    }

    private static string ReadText(GameFileSystem vfs, string path)
    {
        using var reader = new StreamReader(vfs.OpenRead(path));
        return reader.ReadToEnd();
    }
}
```
Before trusting `PartyDescriptionPidsFrom`, read `party_member.cc:133-143` and confirm (a) the comment character in party.txt (`;` vs `#`) the config parser strips, and (b) whether the dude section `[Party Member 0]` exists in vanilla `party.txt`. Adjust and cite. Same for `GameGlobalVars.Parse(...).Count`: it must count lines exactly like `globalVarsRead`; Step 8's `GameDataFact` cross-check (`GlobalVars` copy 1 == copy 2 and stream fully consumed) is the safety net.

- [ ] **Step 6: Implement the reader**

`src/Hexwaste.Formats/Save/Vanilla/SaveDatReader.cs`:
```csharp
using System.Text;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Save.Vanilla;

/// <summary>Read-only SAVE.DAT parser: header + the 27 handlers of fallout2-ce src/loadsave.cc
/// _master_load_list (:242-269), in order. The stream has no section lengths, so every section is
/// parsed (unmodelled ones kept raw) to reach the next.</summary>
public static class SaveDatReader
{
    private const int StatSaveableCount = 35;   // stat_defs.h:70 SAVEABLE_STAT_COUNT
    private const int SkillCount = 18;          // skill_defs.h:31
    private const int KillTypeCount = 19;       // proto_types.h:107-126
    private const int TaggedSkillCount = 4;     // skill_defs.h:7
    private const int PerkCount = 119;          // perk_defs.h:126
    private const int PcStatCount = 5;          // stat_defs.h:76-82
    private const int TraitsSelected = 2;       // trait_defs.h:7
    private const int MovieCount = 17;          // game_movie.h:16-33
    private const int SkillUsesPerDay = 3;      // skill.cc:32
    private const int AiPacketInts = 45;        // combat_ai.cc aiPacketRead (:615)
    private const int SubtilesPerTile = 6 * 7;  // worldmap.cc:64-65

    public static VanillaSave Read(Stream saveDat, VanillaSaveContext ctx)
    {
        var r = new BigEndianReader(saveDat);
        SaveHeader header = ReadHeader(r);                                  // lsgLoadHeaderInSlot (:1902)
        int dudeCid = r.ReadInt32();                                        // _LoadObjDudeCid (:2923)
        int[] gvars = r.ReadInt32Array(ctx.GlobalVarCount);                 // scriptsLoadGameGlobalVars (scripts.cc:1735)
        (string[] maps, int automapSize) = ReadMapList(r);                  // _SlotMap2Game (:2548)
        int[] gvars2 = r.ReadInt32Array(ctx.GlobalVarCount);                // scriptsSkipGameGlobalVars (scripts.cc:1746)
        if (!gvars.AsSpan().SequenceEqual(gvars2))
            throw new InvalidDataException("SAVE.DAT global-variable copies differ — wrong GlobalVarCount?");
        MapObject dude = ReadDude(r, ctx);                                   // _obj_load_dude (object.cc:3629)
        int centerTile = r.ReadInt32();
        CritterSection critter = ReadCritter(r);                            // critterLoad (critter.cc:203)
        int[] kills = r.ReadInt32Array(KillTypeCount);                      // killsLoad (critter.cc:723)
        int[] tagged = r.ReadInt32Array(TaggedSkillCount);                  // skillsLoad (skill.cc:185)
        // randomLoad (random.cc:77): 0 bytes
        int[][] perks = Enumerable.Range(0, ctx.PartyDescriptionCount)      // perksLoad (perk.cc:253)
            .Select(_ => r.ReadInt32Array(PerkCount)).ToArray();
        CombatSection combat = ReadCombat(r);                               // combatLoad (combat.cc:2093)
        int[][] ai = ctx.PartyDescriptionPids.Where(ctx.HasCustomAiPacket)  // aiLoad (combat_ai.cc:573)
            .Select(_ => r.ReadInt32Array(AiPacketInts)).ToArray();
        int[] pc = r.ReadInt32Array(PcStatCount);                           // statsLoad (stat.cc:158)
        // itemsLoad (item.cc:241): 0 bytes
        int[] traits = r.ReadInt32Array(TraitsSelected);                    // traitsLoad (trait.cc:111)
        int automapFlags = r.ReadInt32();                                   // automapLoad (automap.cc:280)
        byte[] prefs = r.ReadBytes(80);                                     // preferencesLoad (preferences.cc:860)
        int lastLevel = r.ReadInt32();                                      // characterEditorLoad (character_editor.cc:5661)
        bool freePerk = r.ReadByte() != 0;
        WorldmapSection wm = ReadWorldmap(r);                               // wmWorldMap_load (worldmap.cc:1149)
        // pipboyLoad (pipboy.cc:868): 0 bytes
        byte[] movies = r.ReadBytes(MovieCount);                            // gameMoviesLoad (game_movie.cc:119)
        int[] usage = r.ReadInt32Array(SkillCount * SkillUsesPerDay);       // skillsUsageLoad (skill.cc:1196)
        (int itemCount, int[] memberIds, PartyMemberLevel[] levels) = ReadParty(r, ctx); // partyMembersLoad (party_member.cc:713)
        QueueEventSave[] queue = ReadQueue(r);                              // queueLoad (queue.cc:84)
        int[] iface = r.ReadInt32Array(4);                                  // interfaceLoad (interface.cc:702)
        // _EndLoad (:2421): 0 bytes

        return new VanillaSave
        {
            Header = header, DudeCid = dudeCid, GlobalVars = gvars, MapFiles = maps, AutomapDbSize = automapSize,
            Dude = dude, DudeCenterTile = centerTile, Critter = critter, Kills = kills, TaggedSkills = tagged,
            PerksByDescription = perks, Combat = combat, CustomAiPackets = ai,
            PcStats = new PcStatsSection(pc[0], pc[1], pc[2], pc[3], pc[4]), Traits = traits,
            AutomapFlags = automapFlags, PreferencesRaw = prefs, CharacterEditorLastLevel = lastLevel,
            CharacterEditorHasFreePerk = freePerk, Worldmap = wm, MoviesSeen = movies, SkillUsage = usage,
            PartyItemCount = itemCount, PartyMemberIds = memberIds, PartyLevels = levels, Queue = queue,
            InterfaceRaw = iface, TotalLength = saveDat.Position,
        };
    }

    // ported from fallout2-ce src/loadsave.cc lsgLoadHeaderInSlot() (:1902) / writer (:1796-1898)
    private static SaveHeader ReadHeader(BigEndianReader r)
    {
        string signature = Ascii(r.ReadBytes(24));
        if (!signature.StartsWith("FALLOUT SAVE FILE", StringComparison.Ordinal))
            throw new InvalidDataException("not a Fallout 2 SAVE.DAT (signature)");
        short v1 = r.ReadInt16(), v2 = r.ReadInt16();
        byte release = r.ReadByte();
        if (v1 != 1 || v2 != 2 || release != (byte)'R')
            throw new InvalidDataException($"unsupported SAVE.DAT version {v1}.{v2}{(char)release}");
        string name = Ascii(r.ReadBytes(32));
        string description = Ascii(r.ReadBytes(30));
        r.Skip(3 * 2 + 4);                         // real-world date + meaningless fileTime
        int month = r.ReadInt16(), day = r.ReadInt16(), year = r.ReadInt16();
        uint gameTime = r.ReadUInt32();
        int elevation = r.ReadInt16(), mapIndex = r.ReadInt16();
        string mapFile = Ascii(r.ReadBytes(16));
        r.Skip(29792 + 128);                       // thumbnail (LS_PREVIEW_SIZE) + padding
        return new SaveHeader(name, description, month, day, year, gameTime, elevation, mapIndex, mapFile);
    }

    // ported from fallout2-ce src/loadsave.cc _SlotMap2Game() (:2548) + _mygets() (:2644)
    private static (string[], int) ReadMapList(BigEndianReader r)
    {
        int count = r.ReadInt32();
        if (count <= 0)
            throw new InvalidDataException("SAVE.DAT map list is empty");
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            var sb = new StringBuilder();
            for (byte b = r.ReadByte(); b != 0; b = r.ReadByte())
                sb.Append((char)b);
            names[i] = sb.ToString();
        }
        return (names, r.ReadInt32());
    }

    private static MapObject ReadDude(BigEndianReader r, VanillaSaveContext ctx) =>
        // _obj_load_dude → _obj_load_obj (object.cc:3536) — same record as a map object, map version 20.
        MapFile.ReadObject(r, ctx.Protos!, mapVersion: 20);

    // ported from fallout2-ce src/critter.cc critterLoad() (:203) + protoCritterDataRead() (:1064)
    private static CritterSection ReadCritter(BigEndianReader r)
    {
        int sneak = r.ReadInt32();
        int flags = r.ReadInt32();
        int[] baseStats = r.ReadInt32Array(StatSaveableCount);
        int[] bonusStats = r.ReadInt32Array(StatSaveableCount);
        int[] skills = r.ReadInt32Array(SkillCount);
        return new CritterSection(sneak, flags, baseStats, bonusStats, skills,
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
    }

    // ported from fallout2-ce src/combat.cc combatLoad() (:2093) / combatSave() (:2214-2243)
    private static CombatSection ReadCombat(BigEndianReader r)
    {
        uint state = r.ReadUInt32();
        if ((state & 1) == 0)
            return new CombatSection(false, state, 0, 0, 0, 0, 0, -1, [], []);
        int turnRunning = r.ReadInt32(), freeMove = r.ReadInt32(), exps = r.ReadInt32();
        int listCom = r.ReadInt32(), listNoncom = r.ReadInt32(), listTotal = r.ReadInt32();
        int dudeCid = r.ReadInt32();
        int[] cids = r.ReadInt32Array(listTotal);
        var data = new CombatantSaveData[listTotal];
        for (int i = 0; i < listTotal; i++)
            data[i] = new CombatantSaveData(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        return new CombatSection(true, state, turnRunning, freeMove, exps, listCom, listNoncom, dudeCid, cids, data);
    }

    // ported from fallout2-ce src/worldmap.cc wmWorldMap_load() (:1149) / wmWorldMap_save() (:1074-1140)
    private static WorldmapSection ReadWorldmap(BigEndianReader r)
    {
        bool frank = r.ReadInt32() != 0;
        int area = r.ReadInt32(), x = r.ReadInt32(), y = r.ReadInt32();
        bool icon = r.ReadInt32() != 0;
        int encMap = r.ReadInt32(), encTable = r.ReadInt32(), encEntry = r.ReadInt32();
        bool inCar = r.ReadInt32() != 0;
        int carArea = r.ReadInt32(), carFuel = r.ReadInt32();
        int cityCount = r.ReadInt32();
        var cities = new CitySave[cityCount];
        for (int i = 0; i < cityCount; i++)
        {
            int cx = r.ReadInt32(), cy = r.ReadInt32(), state = r.ReadInt32(), visited = r.ReadInt32();
            int entrances = r.ReadInt32();
            cities[i] = new CitySave(cx, cy, state, visited, r.ReadInt32Array(entrances));
        }
        int tiles = r.ReadInt32(), horizontal = r.ReadInt32();
        int[] subtiles = r.ReadInt32Array(tiles * SubtilesPerTile);
        int counterCount = r.ReadInt32();
        var counters = new (int, int, int)[counterCount];
        for (int i = 0; i < counterCount; i++)
            counters[i] = (r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        return new WorldmapSection(frank, area, x, y, icon, encMap, encTable, encEntry, inCar, carArea, carFuel,
            cities, horizontal, subtiles, counters);
    }

    // ported from fallout2-ce src/party_member.cc partyMembersLoad() (:713) / writer (:520-538)
    private static (int, int[], PartyMemberLevel[]) ReadParty(BigEndianReader r, VanillaSaveContext ctx)
    {
        int length = r.ReadInt32();
        int itemCount = r.ReadInt32();
        int[] ids = length > 0 ? r.ReadInt32Array(length - 1) : [];
        var levels = new PartyMemberLevel[Math.Max(ctx.PartyDescriptionCount - 1, 0)];
        for (int i = 0; i < levels.Length; i++)
            levels[i] = new PartyMemberLevel(r.ReadInt32(), r.ReadInt32(), r.ReadInt32() != 0);
        return (itemCount, ids, levels);
    }

    // ported from fallout2-ce src/queue.cc queueLoad() (:84) + gEventTypeDescriptions readProcs (:52-67)
    private static QueueEventSave[] ReadQueue(BigEndianReader r)
    {
        int count = r.ReadInt32();
        var events = new QueueEventSave[count];
        for (int i = 0; i < count; i++)
        {
            uint time = r.ReadUInt32();
            int type = r.ReadInt32();
            int owner = r.ReadInt32();
            int dataInts = type switch
            {
                0 => 6, // drug: drugEffectEventRead (item.cc:2886)
                2 => 3, // withdrawal: withdrawalEventRead (item.cc:3005)
                3 => 2, // script: scriptEventRead (scripts.cc:836)
                6 => 2, // radiation: radiationEventRead (critter.cc:647)
                _ => 0,
            };
            events[i] = new QueueEventSave(time, type, owner, r.ReadInt32Array(dataInts));
        }
        return events;
    }

    private static string Ascii(byte[] bytes)
    {
        int nul = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, nul >= 0 ? nul : bytes.Length);
    }
}
```
`ReadDude` with `ctx.Protos == null` only works for an empty inventory; that is all the hermetic test needs. The `_mygets` 15-byte limit is irrelevant for 8.3 names; the reader is lenient (reads to NUL).

`src/Hexwaste.Formats/Save/Vanilla/SlotFiles.cs`:
```csharp
using System.IO.Compression;

namespace Hexwaste.Formats.Save.Vanilla;

/// <summary>Slot files (*.SAV, AUTOMAP.SAV, PROTO copies) are gzip-compressed by
/// fileCopyCompressed (file_utils.cc:66-104); the engine's xfileOpen gunzips on the 1F 8B magic
/// (xfile.cc:140-155). Mirror that: decompress when the magic is present, else read plain.</summary>
public static class SlotFiles
{
    public static Stream OpenMaybeGzip(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
        {
            using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            var plain = new MemoryStream();
            gz.CopyTo(plain);
            plain.Position = 0;
            return plain;
        }
        return new MemoryStream(bytes);
    }
}
```

- [ ] **Step 7: Run tests**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter SaveDatReaderTests`
Expected: 3 passed. If `ReadsMinimalSaveEndToEnd` fails on `TotalLength`, a section's size disagrees between builder and reader — fix whichever contradicts the cited fo2ce function.

- [ ] **Step 8: Real-save check (GameDataFact)**

Append to `SaveDatReaderTests`:
```csharp
    [GameDataFact]
    public void ReadsEveryOracleFixtureSaveCompletely()
    {
        string root = Path.Combine(GameData.RequiredDir, "oracle-saves");
        if (!Directory.Exists(root))
            return; // fixtures not generated on this machine (scripts/oracle-make-save.sh)
        var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new Proto.ProtoDatabase(vfs);
        VanillaSaveContext ctx = VanillaSaveContext.FromGameData(vfs, protos);
        foreach (string dir in Directory.GetDirectories(root))
        {
            using Stream s = SlotFiles.OpenMaybeGzip(Path.Combine(dir, "SAVE.DAT"));
            VanillaSave save = SaveDatReader.Read(s, ctx);
            Assert.Equal(s.Length, save.TotalLength);
            Assert.Contains(save.Header.MapFileName, save.MapFiles, StringComparer.OrdinalIgnoreCase);
        }
    }
```
Run: `FALLOUT2_DIR=$PWD/game-data dotnet test tests/Hexwaste.Formats.Tests --filter SaveDatReaderTests`
Expected: 4 passed (with the three Task 5 saves present). A `TotalLength` mismatch on a real save is a real layout bug — find the section by dumping `saveDat.Position` after each handler.

- [ ] **Step 9: Commit**
```bash
git add src/Hexwaste.Formats/Save src/Hexwaste.Formats/Map/MapFile.cs src/Hexwaste.Formats/BigEndianReader.cs tests/Hexwaste.Formats.Tests/SaveDatReaderTests.cs
git commit -m "feat(formats): read-only vanilla SAVE.DAT reader (all 27 handler sections)"
```

---

### Task 9: Import a vanilla save into Hexwaste (`--load-vanilla-save`)

**Files:**
- Create: `src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs`
- Modify: `src/Hexwaste.Viewer/Program.cs` (flag), `src/Hexwaste.Viewer/ViewerGame.cs` (startup hook)

**Interfaces:**
- Consumes: `SaveDatReader.Read`, `VanillaSaveContext.FromGameData`, `SlotFiles.OpenMaybeGzip`, `MapFile.Load`.
- Produces: `ViewerGame.VanillaSaveDir { get; set; }` (string?, from `--load-vanilla-save <dir>`); `private void ApplyVanillaSave(VanillaSave save, string slotDir)`.

The importer must reproduce what `lsgLoadGameInSlot` leaves in memory, for the state surface M1 fixtures touch: current map (from the slot `.SAV`, **not** the pristine `.MAP`), all object ids/positions/hp/combat data as saved, map + local vars, global vars, game time, dude object + stats/skills/perks/traits/level/XP, party, timed queue, combat state.

- [ ] **Step 1: Read the existing load path**

Read `src/Hexwaste.Viewer/ViewerGame.SaveLoad.cs` end to end and list (in a scratch note, not committed) every step the JSON load performs: map load, `map_enter` with `firstRun`, LVAR import, delta application, dude placement, clock, gvars, inventory, party, worldmap. The vanilla importer reuses the same downstream calls wherever the data is equivalent.

- [ ] **Step 2: Flag**

`Program.cs`: `string? vanillaSaveDir = null;` and
```csharp
        case "--load-vanilla-save" when i + 1 < args.Length:
            vanillaSaveDir = args[++i];
            break;
```
then assign `game.VanillaSaveDir = vanillaSaveDir;` next to the other property assignments (follow how `RngSeed` is passed).

- [ ] **Step 3: Implement `ApplyVanillaSave`**

`src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs`:
```csharp
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Save.Vanilla;

namespace Hexwaste.Viewer;

/// <summary>Trace oracle (M1): start from a vanilla fallout2-ce SAVE.DAT slot instead of a pristine
/// map. Mirrors what loadsave.cc lsgLoadGameInSlot() (:1699) leaves in memory for the state the M1
/// fixtures touch. Read-only: Hexwaste never writes vanilla saves.</summary>
public sealed partial class ViewerGame
{
    public string? VanillaSaveDir { get; set; }

    private void LoadVanillaSaveAtStartup()
    {
        string dir = VanillaSaveDir!;
        var ctx = VanillaSaveContext.FromGameData(_vfs, _protos);
        VanillaSave save;
        using (Stream s = SlotFiles.OpenMaybeGzip(Path.Combine(dir, "SAVE.DAT")))
            save = SaveDatReader.Read(s, ctx);
        ApplyVanillaSave(save, dir);
    }

    private void ApplyVanillaSave(VanillaSave save, string slotDir)
    {
        // 1. Current map from the slot's .SAV (_SlotMap2Game → mapLoadSaved, map.cc:1054).
        string savPath = Directory.GetFiles(slotDir)
            .First(f => Path.GetFileName(f).Equals(save.Header.MapFileName, StringComparison.OrdinalIgnoreCase));
        MapFile map;
        using (Stream s = SlotFiles.OpenMaybeGzip(savPath))
            map = MapFile.Load(s, _protos);
        // A saved map (header flags & 1) keeps its runtime vars and does NOT re-run first-visit
        // setup: no .gam MVAR override (map.cc:922-938), no ammo/combat-data fixups (object.cc:442,
        // map.cc:905). map_enter runs with firstRun = 0 (the map has been visited).
        InstallLoadedMap(map, save.Header.Elevation, firstRun: false);

        // 2. Dude object as saved (_obj_load_dude): id, tile, rotation, fid, hp, combat data, inventory.
        ReplaceDude(save.Dude, save.Header.Elevation);

        // 3. Globals, clock, sheet.
        ImportGlobalVars(save.GlobalVars);
        SetGameTime(save.Header.GameTime);
        ImportDudeSheet(save.Critter, save.PcStats, save.TaggedSkills, save.Traits, save.PerksByDescription[0]);

        // 4. Party, timed events, combat.
        ImportParty(save.PartyMemberIds);
        ImportQueue(save.Queue);
        if (save.Combat.Active)
            ResumeCombat(save.Combat);
    }
}
```
Each `Install*/Replace*/Import*/Set*/Resume*` call above must map onto an **existing** method found in Step 1 (rename in this file to whatever the existing method is called — e.g. the JSON load's map-install routine). Where none exists:
- `ImportQueue`: only script events (type 3: `sid`, `fixedParam`) and the drug/radiation/withdrawal types already modelled by Hexwaste's timer/event queue (`EventQueueTests` shows the model) — map each `QueueEventSave` to the existing queue's enqueue call with absolute `Time`. Unsupported types: write `vanilla-import: skipped queue event type N` to stderr (the trace will show any consequence as a divergence — that is a finding, not a crash).
- `ResumeCombat`: M1 fixtures are saved out of combat; if `Combat.Active` is true, throw `NotSupportedException("vanilla import: in-combat saves are M2")`.
Write this file as a thin adapter; do not duplicate logic that already exists.

- [ ] **Step 4: Startup hook**

In `ViewerGame.cs`, where the startup decides between JSON load (`loadOnStart`) and fresh map load, add first: `if (VanillaSaveDir is not null) { LoadVanillaSaveAtStartup(); }` and skip the default map load in that case.

- [ ] **Step 5: Verify against the artemple fixture**
```bash
FALLOUT2_DIR=$PWD/game-data dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --no-audio \
  --load-vanilla-save game-data/oracle-saves/artemple-idle --screenshot scratch/oracle-runs/import-artemple.png
```
Expected: no exception; open the PNG with the Read tool — dude at the Temple entrance as in the fo2ce save (`DudeCenterTile`). Repeat for `arcaves-ambush` (dude at the recipe tile, scorpions present) and `klamath-hour`.

- [ ] **Step 6: Golden safety + commit**

Run (per the `run-golden-suite` skill): `bash scripts/combat-golden.sh check && bash scripts/opening-golden.sh check` — expected ALL PASS (the flag is opt-in; nothing else changed).
```bash
git add src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs src/Hexwaste.Viewer/Program.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "feat(viewer): --load-vanilla-save starts from a fallout2-ce SAVE.DAT slot"
```

---

### Task 10: Hexwaste oracle mode — shared RNG stream + scenario actions

**Files:**
- Create: `src/Hexwaste.Viewer/ViewerGame.Oracle.cs`
- Modify: `src/Hexwaste.Viewer/Program.cs`, `src/Hexwaste.Viewer/ViewerGame.cs` (RNG construction sites), `src/Hexwaste.Formats/Int/IntVm.cs`, `src/Hexwaste.Formats/Int/ScriptHost.cs`

**Interfaces:**
- Consumes: `Fo2ceRng`, `Fo2ceRandomAdapter` (Task 3); `OracleScenario`, `ScenarioAction` (Task 7).
- Produces: flags `--rng fo2ce`, `--scenario <path>`, `--trace <path>`, `--fixture <name>`; `ViewerGame.OracleRng` (`Fo2ceRng?`); startup actions `StartupAction.OracleEndTurn(int Count)`; `IntVm.SharedRandom` (`Random?`); `ScriptHost.SharedVmRandom` (`Random?`).

- [ ] **Step 1: Inventory every RNG in the viewer**

Run: `grep -n "SystemCombatRng(\|new Random(" src/Hexwaste.Viewer/*.cs src/Hexwaste.Formats/**/*.cs`
Classify each field with the Task 2 audit's rule (cosmetic = wall-clock-timed visuals/audio). Known so far: logic = `_combatRng`, `_calledShotRng`, `_sneakRng`, `_stealRng`, `_partyRng`, `_wmRng`, `_skillRng`, `_tauntRng`, `_scriptHost.Rng`, the IntVm `_random`; cosmetic = `_fidgetRng` (`ViewerGame.cs:6439`), `_ambientRng` (`:1966`); out of scope = harness one-offs (`ViewerGame.Harness.cs:1589`) and the endgame slide picker (`ViewerGame.Endgame.cs:260`). Record the table as a comment block at the top of `ViewerGame.Oracle.cs`.

- [ ] **Step 2: IntVm shared stream (TDD)**

IntVm needs a program to run, so add a focused unit test in the existing IntVm test file that uses the hermetic hand-assembled `.int` helper (the P115/P126 hermetic hand-assembled `.int` pattern lives in `tests/Hexwaste.Formats.Tests/IntVmTests.cs` and `StaleHandleGuardTests.cs` — copy its program-building helper). Test: with `SharedRandom = new Fo2ceRandomAdapter(new Fo2ceRng(5))`, running a proc that executes `random(1, 6)` twice across two `TryRunProcedure` calls yields the 1st and 2nd draws of `new Fo2ceRng(5).Between(1, 6)` (no reseed between procs). Then implement in `IntVm.cs`:
```csharp
    /// <summary>Trace oracle: when set, every random() draw comes from this shared stream and the
    /// per-proc reseed below is skipped (fallout2-ce has ONE RNG — interpreter_extra.cc opRandom :782
    /// calls randomBetween).</summary>
    public Random? SharedRandom { get; set; }
```
In `TryRunProcedure`: `if (SharedRandom is null) _random = new Random(RandomSeed);`. At the opcode (`IntVm.cs:1337`): replace `_random.Next(min, max + 1)` with `(SharedRandom ?? _random).Next(min, max + 1)`. Do **not** change the `min >= max ? min :` short-circuit here — if fo2ce draws for `min == max` and Hexwaste does not, the oracle should *find* it (record as an expected first finding in Task 12).

In `ScriptHost.cs`: add `public Random? SharedVmRandom { get; set; }` and, in `GetOrCreateVm`, set `vm.SharedRandom = SharedVmRandom;` on every VM it returns (new or cached).

Run: `dotnet test tests/Hexwaste.Formats.Tests` → all pass.

- [ ] **Step 3: Flags**

`Program.cs`:
```csharp
        case "--rng" when i + 1 < args.Length:
            rngMode = args[++i];                       // "fo2ce" or default
            break;
        case "--scenario" when i + 1 < args.Length:
            scenarioPath = args[++i];
            break;
        case "--trace" when i + 1 < args.Length:
            tracePath = args[++i];
            break;
        case "--fixture" when i + 1 < args.Length:
            fixtureName = args[++i];
            break;
```
After parsing, if `scenarioPath` is set: parse it with `OracleScenario.Parse(File.ReadAllText(scenarioPath))`, set `rngSeed = scenario.Seed`, and append to `actions` one `StartupAction.PumpMs(ms)` per `Wait` and one `StartupAction.OracleEndTurn(n)` per `EndTurn`. Pass `rngMode == "fo2ce"`, `tracePath`, `fixtureName` to the game as properties `OracleRngMode`, `OracleTracePath`, `OracleFixture`.

- [ ] **Step 4: Shared stream wiring**

`ViewerGame.Oracle.cs`:
```csharp
using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Trace;

namespace Hexwaste.Viewer;

// Trace-oracle RNG classification (fo2ce has ONE stream; Hexwaste had many):
//   logic (shared Fo2ceRng): _combatRng, _calledShotRng, _sneakRng, _stealRng, _partyRng, _wmRng,
//                            _skillRng, _tauntRng, _scriptHost.Rng, IntVm random()
//   cosmetic (own streams):  _fidgetRng, _ambientRng
//   out of scope:            harness one-offs, endgame slide picker
// <-- keep this table in sync with Step 1's grep -->
public sealed partial class ViewerGame
{
    public bool OracleRngMode { get; set; }
    public string? OracleTracePath { get; set; }
    public string? OracleFixture { get; set; }
    public Fo2ceRng? OracleRng { get; private set; }

    /// <summary>Called once at startup, before any RNG field is first used.</summary>
    private void InitOracle()
    {
        if (!OracleRngMode)
            return;
        OracleRng = new Fo2ceRng(RngSeed ?? throw new InvalidOperationException("--rng fo2ce needs a seed (--scenario or --rng-seed)"));
        _combatRng = OracleRng;
        _calledShotRng = OracleRng;
        _sneakRng = OracleRng;
        _stealRng = OracleRng;
        _partyRng = OracleRng;
        _wmRng = OracleRng;
        _skillRng = OracleRng;
        _tauntRng = OracleRng;
        var adapter = new Fo2ceRandomAdapter(OracleRng);
        _scriptHost.Rng = adapter;
        _scriptHost.SharedVmRandom = adapter;
    }
}
```
The `??=` lazy initialisers elsewhere keep working: the fields are already non-null. Call `InitOracle()` at the point in `ViewerGame.cs` right after `_combat` / `_scriptHost` are constructed (around `:1192-1195` and `:1524`) — **after** both exist and **before** `ApplyVanillaSave` / the first proc runs; and make sure the `RngSeed` branches at `:1192` and `:1524` don't overwrite the shared stream afterwards (guard them with `if (!OracleRngMode)`). Note: the fo2ce side seeds **after** loading the save (scenario runner, Task 6); Hexwaste must also seed after import — so in oracle mode create `OracleRng` in `InitOracle` but call `InitOracle()` after `LoadVanillaSaveAtStartup()` returns. Any RNG draw during import is therefore drawn from a non-oracle stream and untraced on both sides (fo2ce's load path draws before `oracleRandomSeed` too).

- [ ] **Step 5: End-turn action**

Add to the `StartupAction` records in `ViewerGame.cs`: `public sealed record OracleEndTurn(int Count) : StartupAction;` and in `ViewerGame.Harness.cs` dispatch:
```csharp
                case StartupAction.OracleEndTurn(var count):
                    // Same semantics as the fo2ce patch's end-turn (tests/golden-oracle, Task 6):
                    // run until the dude awaits input or combat is over; end the turn; run until the
                    // dude awaits input again or combat is over. 10 ms steps, 600000 ms cap each.
                    for (int i = 0; i < count; i++)
                    {
                        if (!OraclePumpUntilDudeTurnOrNoCombat())
                            break;
                        _combat.EndPlayerTurn();
                        OraclePumpUntilDudeTurnOrNoCombat();
                    }
                    break;
```
with, in `ViewerGame.Oracle.cs`:
```csharp
    private bool OraclePumpUntilDudeTurnOrNoCombat()
    {
        for (int ms = 0; ms < 600000; ms += 10)
        {
            if (!IsCombatActive())
                return false;
            if (IsDudeAwaitingCombatInput())
                return true;
            PumpOneFrame(10);
        }
        Console.Error.WriteLine("oracle: end-turn cap reached");
        return false;
    }
```
`PumpOneFrame(10)` = extract the body of the `PumpMs` loop (`ViewerGame.Harness.cs:617-624` — `_animator.Update`, `_combat.Step`, `_dude?.Update`, `PumpPendingInteraction`, `UpdateAmbientLife`, `UpdateClock`, …) into a private method and call it from both places (pure refactor: `PumpMs` behaviour identical). `IsCombatActive()` / `IsDudeAwaitingCombatInput()` map to existing combat-engine state — the dude-turn phase (`CombatPhase` enum in `CombatEngine.cs`; expose a read-only `public bool IsDudeTurn` if none exists, true when the phase is the player's and `!IsBusy`).

- [ ] **Step 6: Verify byte-identical goldens + a smoke oracle run**
```bash
dotnet build Hexwaste.slnx
bash scripts/combat-golden.sh check && bash scripts/quest-golden.sh check && bash scripts/opening-golden.sh check
FALLOUT2_DIR=$PWD/game-data dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --no-audio \
  --load-vanilla-save game-data/oracle-saves/arcaves-ambush --rng fo2ce \
  --scenario tests/golden-oracle/arcaves-ambush/scenario.txt
```
Expected: all goldens ALL PASS; the oracle run exits 0 (no trace yet — Task 11).

- [ ] **Step 7: Commit**
```bash
git add src/Hexwaste.Viewer src/Hexwaste.Formats/Int tests/Hexwaste.Formats.Tests
git commit -m "feat(viewer): oracle mode — one shared fo2ce RNG stream + scenario end-turn"
```

---

### Task 11: Hexwaste trace hooks

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Oracle.cs`, `src/Hexwaste.Formats/Map/MapFile.cs` (`MapObject.HexTile`), `src/Hexwaste.Formats/Int/ScriptHost.cs`, `src/Hexwaste.Formats/Combat/CombatEngine.cs`, runtime object-creation sites

**Interfaces:**
- Consumes: `OracleTrace` (Task 7), `Fo2ceRng.OnDraw` (Task 3).
- Hook ↔ fo2ce correspondence (must emit at the *equivalent* moment):

| Kind | fo2ce site (Task 4) | Hexwaste site |
|---|---|---|
| RNG | `randomBetweenAt` end | `Fo2ceRng.OnDraw` |
| PROC | `scriptExecProc` before `_executeProcedure` | `ScriptHost` immediately before each `vm.TryRunProcedure(name)` |
| GVAR | `gameSetGlobalVar` | `ScriptContext.SetGlobalVar` (`ScriptHost.cs:1540`) |
| MVAR | `mapSetGlobalVar` | `ScriptContext.SetMapVar` (`:1558`) |
| LVAR | `scriptSetLocalVar` | `ScriptContext.SetLocalVar` (`:1548`) |
| TURN | `_combat_turn` entry | each place a combatant's turn begins in `CombatEngine` |
| ATTACK | `attackCompute` end | each `new PendingAttack(...)` (`CombatEngine.cs:440, 3956, 4094`) |
| MOVE | `objectSetLocation` | `MapObject.HexTile` setter, critters only |
| SPAWN | `objectCreateWith*` | runtime `new MapObject` sites |

- [ ] **Step 1: Open/close + clock + RNG**

In `InitOracle()` (Task 10), after creating `OracleRng`:
```csharp
        if (OracleTracePath is { } path)
        {
            OracleTrace.Clock = () => (uint)GameTimeTicks;   // the existing game-clock tick property
            OracleTrace.Open(new StreamWriter(path), $"oracle v1 fixture={OracleFixture ?? "?"} engine=hexwaste@{GitShortSha()}");
            OracleRng.OnDraw = (min, max, v) =>
                OracleTrace.Rng(min, max, v, new System.Diagnostics.StackFrame(3, false).GetMethod()?.Name ?? "?");
        }
```
`GameTimeTicks` = whatever `UpdateClock` advances (`ViewerGame.cs:6837`) — use the same value `game_time` returns to scripts. `GitShortSha()` = read `ThisAssembly`-style info if present; otherwise return `"dev"` (header lines are not compared). Call `OracleTrace.Close()` when the startup-action list finishes (wherever the harness exits after `--screenshot`/the final action) — add it unconditionally (no-op if inactive).

- [ ] **Step 2: PROC and variable hooks**

`ScriptHost.cs`: before each `vm.TryRunProcedure(<name>)` call (`:825`, `:862`, `:930`, `:1166`, `:1437` and any other `grep -n "TryRunProcedure(" src/Hexwaste.Formats/Int/ScriptHost.cs` finds), add
```csharp
            if (OracleTrace.Active)
                OracleTrace.Proc(self?.Id ?? -1, Path.GetFileName(path), procedureName);
```
using the local names available at each site (script path → `Path.GetFileName`, proc name literal or variable, owner object). In `ScriptContext`:
```csharp
        public void SetGlobalVar(int index, int value)
        {
            if (OracleTrace.Active)
                OracleTrace.Gvar(index, GetGlobalVar(index), value);
            _host._globalVars[index] = value;
        }
```
and the same pattern for `SetMapVar` (`OracleTrace.Mvar(index, GetMapVar(index), value)`, inside the bounds check) and `SetLocalVar` (`OracleTrace.Lvar(_sid, index, slice[index], value)`, inside the bounds check).

- [ ] **Step 3: MOVE via the `HexTile` setter**

`MapFile.cs` `MapObject`:
```csharp
    /// <summary>Hex grid tile number (0..39999), or -1 for inventory items. The setter reports
    /// critter moves to the trace oracle (fo2ce: object.cc objectSetLocation).</summary>
    public required int HexTile
    {
        get;
        set
        {
            if (Trace.OracleTrace.Active && Fid.PidType(Pid) == 1)
                Trace.OracleTrace.Move(Id, field, value);
            field = value;
        }
    }
```
(`field` keyword: C# 14 / .NET 10.) The `required` init during map load happens before `OracleTrace.Open`, so loading emits nothing.

- [ ] **Step 4: TURN and ATTACK**

`CombatEngine.cs`: after each of the three `_pendingAttack = new PendingAttack(attacker, target, chance, hit, damage, critFlags, ...)` assignments:
```csharp
        if (OracleTrace.Active)
            OracleTrace.Attack(<attacker>.Id, <target>.Id, chance, hit, damage, critFlags);
```
For TURN: `grep -n "_actingEnemy = \|_actingAlly = \|_phase = CombatPhase" src/Hexwaste.Formats/Combat/CombatEngine.cs` — at each assignment that starts a new combatant's turn (enemy, ally, and the dude's turn start where `_dudeAp` is refreshed), add `if (OracleTrace.Active) OracleTrace.Turn(<obj>.Id, <obj's AP after refresh>);`. fo2ce emits TURN at `_combat_turn` entry, where AP has already been refreshed by the combat loop and **before** `combat_p_proc` runs (`combat.cc:3225-3248`) — place each Hexwaste call so it precedes the turn's `combat_p_proc`.

- [ ] **Step 5: SPAWN**

`grep -n "new MapObject" src/Hexwaste.Viewer/*.cs src/Hexwaste.Formats --include=*.cs -r` — for every site that creates an object **at runtime** (not inside `MapFile.Load` / save import), add `if (OracleTrace.Active) OracleTrace.Spawn(obj.Id, obj.Pid, obj.HexTile);` after construction. If those sites do not assign fresh unique ids today, leave the id as-is: a mismatch against fo2ce's `scriptsNewObjectId()` (which increments `_cur_id` twice per allocation, `scripts.cc:531,547`) is an oracle finding for later, not M1 work.

- [ ] **Step 6: Run and inspect**
```bash
FALLOUT2_DIR=$PWD/game-data dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --no-audio \
  --load-vanilla-save game-data/oracle-saves/arcaves-ambush --rng fo2ce --fixture arcaves-ambush \
  --scenario tests/golden-oracle/arcaves-ambush/scenario.txt --trace scratch/oracle-runs/hex-arcaves.trace
head -30 scratch/oracle-runs/hex-arcaves.trace
grep -c ' ATTACK ' scratch/oracle-runs/hex-arcaves.trace
```
Expected: header line + `t=… PROC …`/`RNG …` lines in the fo2ce format; ≥ 1 ATTACK.

- [ ] **Step 7: Goldens + commit**

Run all four golden suites in `check` mode (hooks are behind `OracleTrace.Active`; must be byte-identical), then:
```bash
git add src/Hexwaste.Viewer src/Hexwaste.Formats
git commit -m "feat(oracle): Hexwaste trace hooks matching the fo2ce patch"
```

---

### Task 12: `oracle-run.sh`, the three fixtures, planted-bug check, docs

**Files:**
- Create: `scripts/oracle-run.sh`, `docs/research-notes/oracle-m1-findings-2026-10.md`
- Modify: `README.md` (progress checklist), `docs/PHASE-HISTORY.md` (maintenance entry), `tools/fo2ce-trace/README.md`

**Interfaces:**
- Consumes: everything above.
- Produces: `scripts/oracle-run.sh <fixture>` → prints `IDENTICAL (N lines)` (exit 0) or `DIVERGED at …` (exit 1); traces kept under `scratch/oracle-runs/<fixture>/`.

- [ ] **Step 1: The runner**

`scripts/oracle-run.sh`:
```bash
#!/usr/bin/env bash
# Trace oracle (M1): run one fixture in the trace-patched fallout2-ce e97087b and in Hexwaste
# from the same vanilla save + seed + scenario, then report the first divergence.
# Prereqs: tools/fo2ce-trace/build.sh; scripts/oracle-make-save.sh <fixture>.
# Usage: scripts/oracle-run.sh <fixture>
set -euo pipefail
cd "$(dirname "$0")/.."
FIX="${1:?usage: $0 <fixture>}"
SCEN="$PWD/tests/golden-oracle/$FIX/scenario.txt"
SAVE="$PWD/game-data/oracle-saves/$FIX"
OUT="$PWD/scratch/oracle-runs/$FIX"; mkdir -p "$OUT"
[ -f "$SAVE/SAVE.DAT" ] || { echo "missing $SAVE — run scripts/oracle-make-save.sh $FIX" >&2; exit 2; }

RUN=reference/fallout2-ce/run
SLOT=$(find "$RUN" -maxdepth 3 -type d -iname SAVEGAME | head -1)
rm -rf "$SLOT/SLOT10"; mkdir -p "$SLOT/SLOT10"; cp -r "$SAVE/." "$SLOT/SLOT10/"
bash tools/fo2ce-trace/run.sh fixture="$FIX" scenario="$SCEN" trace="$OUT/fo2ce.trace"

FALLOUT2_DIR="$PWD/game-data" dotnet run --project src/Hexwaste.Viewer -c Debug -- \
  --game-dir "$PWD/game-data" --no-audio --load-vanilla-save "$SAVE" --rng fo2ce \
  --fixture "$FIX" --scenario "$SCEN" --trace "$OUT/hexwaste.trace"

dotnet run --project tools/TraceDiff -- "$OUT/fo2ce.trace" "$OUT/hexwaste.trace"
```

- [ ] **Step 2: Run all three fixtures**
```bash
for f in artemple-idle klamath-hour arcaves-ambush; do echo "== $f"; bash scripts/oracle-run.sh "$f" || true; done
```
Expected: each prints `IDENTICAL` or a `DIVERGED` report with context. Divergences are findings — do **not** fix engine code in this task. For each, write an entry in `docs/research-notes/oracle-m1-findings-2026-10.md`: fixture, divergent line pair, both `site=` values, a one-paragraph hypothesis with the fo2ce `file:line` it points at, and status `open`. Candidate first finding to check for: `IntVm` `random(min, max)` with `min == max` skips the draw (`IntVm.cs:1337`) while fo2ce `opRandom` → `randomBetween` always draws (`interpreter_extra.cc:782`).

- [ ] **Step 3: Planted-bug check**

Temporarily change the hit chance in Hexwaste's to-hit computation by +1 (e.g. in the method that computes `chance` before the `new PendingAttack` at `CombatEngine.cs:4094`). Run `bash scripts/oracle-run.sh arcaves-ambush`. Expected: `DIVERGED` at (or before) the first `ATTACK` line, showing `chance=` differing by 1. Revert the change (`git checkout -- src/Hexwaste.Formats/Combat/CombatEngine.cs`) and confirm the planted divergence is gone (the report returns to whatever it was in Step 2). Record the result in the findings doc ("oracle catches a ±1 hit-chance drift: yes, at line N").

- [ ] **Step 4: Docs**
- `README.md` progress checklist: add "Trace oracle M1 (fo2ce lockstep traces from vanilla saves)" ✔.
- `docs/PHASE-HISTORY.md`: a `MAINTENANCE (2026-10-…, "trace oracle M1")` entry in the existing style: what was built, the determinism decisions (virtual clock 10 ms, zeroed `_iv`, cosmetic split), the findings count, and pointers to the spec/plan/findings doc.
- `tools/fo2ce-trace/README.md`: add the end-to-end workflow (build → make-save → selfcheck → oracle-run).

- [ ] **Step 5: Final verification + commit**

Run: `dotnet test tests/Hexwaste.Formats.Tests` (all pass), all four golden suites `check` (ALL PASS), `for f in artemple-idle klamath-hour arcaves-ambush; do bash scripts/oracle-selfcheck.sh $f; done` (all OK).
```bash
git add scripts/oracle-run.sh docs/research-notes/oracle-m1-findings-2026-10.md README.md docs/PHASE-HISTORY.md tools/fo2ce-trace/README.md
git commit -m "feat(oracle): oracle-run.sh + M1 fixtures run end-to-end; findings recorded"
```

---

## Self-review notes

- **Spec coverage:** patch + build (T1), virtual clock (T1), seed + zeroed table (T2), cosmetic audit/split (T2), Fo2ceRng + vectors test (T3), trace format + hooks fo2ce (T4) / Hexwaste (T11), recipe saves + community-binary fallback (T5), scenario runner + self-determinism gate (T6), TraceDiff (T7), reader all 27 sections + hermetic + GameDataFact (T8), importer (T9), shared stream + VM no-reseed (T10), three fixtures + planted bug + findings (T12), goldens byte-identical (T9–T12). Out-of-scope items (active actions, StateDiff, re-sync, writer) are untouched.
- **Known soft spots, flagged in-task rather than guessed:** the SDL dependency-source variable name (T1 S3), the `lsgPerformSaveGame` thumbnail dependency (T5 S1), the combat-loop control flow forcing a step-machine scenario runner (T6 S2), party.txt comment syntax (T8 S5), the exact existing method names the importer adapts (T9 S1/S3), Hexwaste's turn-start sites (T11 S4).
