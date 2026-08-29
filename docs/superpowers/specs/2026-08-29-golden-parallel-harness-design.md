# Parallel golden-test harness — design

**Date:** 2026-08-29
**Problem:** the golden suites take roughly 40 minutes, and the hermetic suite that takes 7 seconds
is not the reason.

## Where the time actually goes

Measured on this machine (16 cores) rather than assumed:

| | measured |
|---|---|
| Hermetic suite, 1016 tests | **7.15 s** wall (0.68 s of test execution) |
| `combat-golden.sh check` — 18 fixtures, 36 invocations | **112.3 s** → **3.12 s per invocation** |
| `dotnet run --no-build`, trivial scenario | 2.59 s |
| the same scenario, invoking the built binary directly | 1.43 s |
| → pure `dotnet run` overhead | **1.16 s per invocation** |
| 8 real combat fixtures run concurrently | **1.68 s** total, all 8 byte-identical |

The suites run 279 fixtures, each twice — **558 invocations**. Three independent costs stack up:

1. **`dotnet run` instead of the built binary.** 1.16 s × 558 ≈ **11 minutes** of pure process
   overhead that buys nothing; every script already runs `dotnet build` up front and then passes
   `--no-build`.
2. **Everything is serial on a 16-core machine.** Fixtures are independent processes reading a
   read-only DAT. Eight concurrent instances of a real combat fixture finished in 1.68 s and
   produced a single distinct output hash — concurrency is safe *and* deterministic here.
3. **Every scenario runs twice** (the determinism assertion), doubling all of the above.

## Goal

Cut the golden suites from ~40 minutes to ~2-4 minutes **without weakening a single assertion and
without touching a single fixture**.

## Explicit non-goal: do not drop the double run

The obvious way to halve the time is to stop running each scenario twice. This design refuses that.
The second pass is what catches non-determinism, and non-determinism is exactly the class of bug a
transcript suite exists to catch. Under a job pool the second pass costs **a core, not wall time** —
so the guarantee becomes free rather than expensive. Anyone revisiting this later should understand
the double run was kept deliberately, not overlooked.

---

## 1. Architecture

`scripts/golden-lib.sh`, sourced by six thin suite scripts.

The six scripts share an identical skeleton today — a `SCENARIOS` array of `"name|args"`, a `run()`
that invokes a tool and filters stdout, and a loop that in `check` mode runs each scenario twice,
compares the two runs to each other and the first to the committed fixture, then prints a verdict.
They differ only in the tool, the stdout `FILTER` regex, the timeout, the verdict wording, and one
extra rule in `census-sweep.sh` (empty output is `LOAD-FAIL`, not a diff).

So the parallel runner is written **once**. Its aggregation is subtle enough — in particular the
classic trap where a `while read` in a pipeline runs in a subshell and silently loses the `fail`
flag — that six copies would drift apart and one of them would be wrong.

**The library owns:** the build step, the job pool, per-job scratch directories, output ordering,
result aggregation, and the final verdict line.

**Each suite script declares:** the binary path, the build targets, `FILTER`, the per-run timeout,
the verdict label, `SCENARIOS`, and optionally a per-suite result hook (census's empty-output rule).

## 2. The job model

The unit of work is a **(fixture, pass)** pair, not a fixture. In `check` mode that is 2N jobs, and
**both passes of the same fixture may run concurrently** — that is what makes the determinism
assertion free.

Concurrency defaults to `nproc`, overridable with `GOLDEN_JOBS` so a developer can throttle it while
working. `record` mode uses the same pool; each job writes a distinct fixture file, so it is safe.

## 3. Deterministic output

Each job writes its captured stdout to its own file. Once the pool drains, the suite script walks
`SCENARIOS` **in declaration order** and emits the per-fixture result lines from those files.

Output is therefore byte-identical to today's regardless of completion order. This is a hard
requirement, not a nicety: the `ok <name>` lines are what humans and greps read, and the ordering is
what makes a run diffable against a previous one.

`fail` is computed in the main shell after the pool drains, from the result files. It is never
mutated inside a subshell.

## 4. Shared on-disk state — the hazard that would make this flaky

Four `encounter-golden.sh` scenarios write to hardcoded absolute paths:

| line | path | flag |
|---|---|---|
| 153 `automap-persist` | `/tmp/hexwaste-automap-persist.json` | `--save-path` |
| 188 `save-slot-roundtrip` | `/tmp/hexwaste-p48-rt` | `--save-dir` |
| 189 `save-slots-probe` | `/tmp/hexwaste-p48-sp` | `--save-dir` |
| 212 `vic-save-roundtrip` | `/tmp/hexwaste-m3golden.json` | `--save-path` |

Serially this is harmless. Under the job model the **two passes of the same fixture** run
concurrently against the same file or directory, which would produce intermittent, unreproducible
failures — and the natural conclusion would be "parallelism doesn't work here", which is wrong.

**Fix:** every job gets its own scratch directory, and those four scenarios use a `@SCRATCH@` token
in their args which the runner expands to that directory. A four-line change, called out here so it
is implemented on purpose rather than discovered as a flake.

## 5. Direct binary invocation

Each script already builds its tool up front and then calls `dotnet run --no-build`. Replace that
with the built binary (`bin/Debug/net10.0/Hexwaste.Viewer`, and `ProcAnalyze` for `census-sweep` and
`opening`). The build step stays; only the invocation changes.

## 6. How we know it worked

The acceptance criterion is **not** "the suites pass". It is that the new harness produces
**byte-identical stdout** to the current one.

A full clean baseline already exists from a run earlier today —
`golden-f46.log`, all six suites, 279 `ok` lines, zero differing. If that scratch file is gone by
implementation time, capture a fresh baseline from `main` **before** changing anything; a baseline
taken after the change proves nothing.

Then: run each suite under the new harness, diff against the baseline, and require an empty diff.
Also assert the wall-clock improvement, since a correct-but-still-slow result means the pool is not
actually running jobs concurrently.

## 7. Risks

- **A job that times out must be reported, not silently treated as a pass.** The existing per-run
  `timeout` moves into the job; a timed-out job has no output, which today surfaces as a diff. That
  behaviour must survive.
- **Machine load.** `nproc` jobs of a graphics-context process is heavy. `GOLDEN_JOBS` exists for
  this; the default can be lowered if it proves disruptive.
- **`record` mode is correctness-critical.** It rewrites committed fixtures. It gets the same
  ordering guarantees, and a record run should still be reviewed by diffing the fixtures it touched.
- **All six scripts change at once.** Mitigated by the byte-identical-output oracle: any behavioural
  drift in any suite shows up immediately as a non-empty diff.
