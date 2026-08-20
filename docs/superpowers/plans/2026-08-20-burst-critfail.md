# Burst critical-failure effects (F26) + burst self-hit roll count (F15) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A burst that rolls a critical failure applies its effects (F26), and a burst self-hit rolls damage once per round spent (F15).

**Architecture:** Both changes sit in `CombatEngine`'s burst path. The crit-failure **detection** already exists and is faithful — do not touch it. Two sequenced commits with a fixture measurement between.

**Tech Stack:** C# / .NET 10 (`net10.0`), xUnit. Reference: `reference/fallout2-ce` at `e97087b`.

## Global Constraints

- **Never copy, embed, or commit game assets.** `.gitignore` excludes `*.dat`, `*.map`, `*.frm`, `*.pal`, `game-data/`.
- **Port from `reference/fallout2-ce`, never guess.** Behavioural changes carry a citation comment. If a detail cannot be confirmed, **stop and ask**.
- **`alexbatalov e97087b` is authoritative for vanilla**; `community/main` is a bug-fix candidate source cited as `(community fix #NNN)`; SFALL-marked fixes are ported by precedent, `// CE:` QoL never is.
- **VERIFY EVERY LINE NUMBER YOU CITE**, reference and Hexwaste, **on this branch, at the time you write it**. The plan author's citations have been wrong seven times across this work — six misreadings and one set that drifted as earlier commits added lines to the same file.
- **No new dependencies.** `src/Hexwaste.Formats` stays MonoGame-free.
- **Golden discipline:** scripts run the *prebuilt* binary. Never two at once, never backgrounded, never build while one runs; report exactly what they print. `combat-golden.sh` is the implementer's; **`quest-golden.sh`, `encounter-golden.sh` and `encounter-golden.sh record` are the controller's.**
- Every regression test **confirmed failing pre-change, and for the right reason**.

---

## Reference facts

- `_compute_spray` (`combat.cc:3703-3720`): sets `*roundsSpentPtr = ammoQuantity` at **`:3713`**, *then* rolls at `:3716`, and on `ROLL_CRITICAL_FAILURE` returns immediately at `:3718-3719` — rounds spent, none fired, no spray computed.
- `_compute_attack` assigns `attack->ammoQuantity = v26` (`:3888`) for `ATTACK_TYPE_RANGED`, and dispatches `ROLL_CRITICAL_FAILURE` to `attackComputeCriticalFailure` through the shared switch arm (`:3933-3934`) that every attack shape reaches.
- `attackComputeCriticalFailure` (`:4228-4230`): `int ammoQuantity = attackType == ATTACK_TYPE_RANGED ? attack->ammoQuantity : 1; attackComputeDamage(attack, ammoQuantity, 2);`
- `attackComputeDamage` loops the per-round roll `for (int index = 0; index < ammoQuantity; index++)` (`:4589`).

**Net effect:** a burst that fumbles into `DAM_HIT_SELF` rolls damage **once per round of the burst**.

## Hexwaste facts (verify on this branch)

- `RollBurst` (`CombatEngine.cs:524`) returns `(int Accuracy, int RoundsFired, int RoundsHit, int TotalDamage, List<BurstExtra> Extras)` and takes `attacker` (`CritterState`), `weaponProto`, `weaponItem`, `attackerIsDude`.
- Its crit-failure detection is at `:535-550` — **already faithful, already draws, already day-2 gated, already aborts with bullets spent.** Leave it alone.
- The abort returns `(accuracy, n, 0, 0, [])`, which is **indistinguishable from a burst that hit nothing.** That matters — see Task 1 Step 1.
- `delta` is computed **inside** `RollBurst` (`:538`) and never returned.
- Three call sites: `:495` (dude `TryBurst`), `:3756`, `:3785` (ally and enemy bursts).
- `TriggerCritFailure(attacker, attackerIsDude, weaponProto, weaponItem, delta)` returns `true` when the fumble costs the turn. The single-shot pattern is `if (!hit && TriggerCritFailure(...)) _dudeAp = 0;` (`:369-370`).
- `CritFailDamage(attacker, victimState, weaponProto, tag)` (`:1246`) is the `DAM_HIT_SELF` / `DAM_RANDOM_HIT` damage roll.

---

## Task 1: F26 — apply the effects on a burst critical failure

**Files:** `src/Hexwaste.Formats/Combat/CombatEngine.cs`; `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`

- [ ] **Step 1: Decide the call-site question, and report your reasoning before implementing**

`TriggerCritFailure` must be reached whenever a burst aborts on a fumble, from **all three** burst paths. Two shapes are available:

- **Inside `RollBurst`**, at the point it currently returns the zeroed tuple. Pro: impossible for a burst to abort without applying effects; `delta` is already in scope and is otherwise unavailable to callers. Con: a roll engine applying effects is arguably the wrong shape.
- **At each of the three call sites**, on seeing an abort. Pro: keeps effects out of the roll engine. Con: three places to miss one, and it needs `delta` plumbed out, plus a way to distinguish an abort from a clean zero-hit burst — the current tuple cannot.

**Whichever you choose, the lose-turn result must reach the caller** so the actor's AP can be zeroed, matching `:369-370`. The tuple as it stands cannot express "aborted on a fumble" — extending it is likely part of the answer either way.

State your choice and why. Prefer the shape that makes it **impossible** for a burst path to abort without applying effects.

- [ ] **Step 2: Write the tests**

- **A (primary):** a dude burst that rolls a critical failure produces a `crit-fail:` transcript line with resolved flags. Drive it with a seeded RNG that lands the inception roll on a fumble; the existing detection is day-2 gated, so `CriticalsEnabled` must be true.
- **B (coverage):** the same for the **ally** and **enemy** burst paths. This is the test that catches "wired one of three call sites" — do not skip it.
- **C (non-regression):** a burst that does **not** crit-fail is unchanged — same rounds fired, same hits, same damage, and **no extra RNG draw**. The detection already existed, so this must stay byte-identical.

- [ ] **Step 3: Confirm A and B fail pre-change, and C passes both sides.** Report each failure message. C is a pin, not a regression test — label it.

- [ ] **Step 4: Implement**, per your Step 1 decision, with a comment citing `combat.cc:3718-3719` (the early return) and `:3933-3934` (the shared dispatch every attack shape reaches). Note explicitly that the *detection* was already correct and only the effects were missing — otherwise the next reader assumes this commit added the branch.

- [ ] **Step 5: `dotnet test`** — expect 0 failed.

- [ ] **Step 6: Measure.** `scripts/combat-golden.sh check`, to completion. Only a fixture where a burst **actually crit-fails** can move; the detection draws are unchanged. `arcaves-burst-smg`, `arcaves-burst-shotgun` and `denbus2-burst-collateral` are the burst fixtures — whether any rolls a fumble is unknown until measured. **Enumerate and classify every failure.** If a fixture moves that does *not* contain a burst fumble, stop and report: that would mean the detection was disturbed.

- [ ] **Step 7: Justify, record, verify** — `git status --short tests/golden-combat/` must match your enumeration; re-run `check`.

- [ ] **Step 8: Commit**, ending `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`

---

## Task 2: F15 — a ranged self-hit rolls once per round spent

**Files:** same two.

- [ ] **Step 1: Write the tests**

- **A (primary):** a burst `DAM_HIT_SELF` fumble rolls damage **once per round spent**. **Assert the draw count**, not only the damage total — a total-only assertion can pass for the wrong reason if a multiplier drifts, which is exactly how F11 hid for months. Use `RecordingRng` and count the weapon-damage draws.
- **B (non-regression):** a single-shot ranged fumble still rolls exactly once.
- **C (non-regression):** a melee/unarmed fumble still rolls exactly once — `ammoQuantity` collapses to 1 off `ATTACK_TYPE_RANGED` (`combat.cc:4229`).

- [ ] **Step 2: Confirm A fails pre-change** (it will roll once), and that B and C pass both sides.

- [ ] **Step 3: Implement.** Give `CritFailDamage` a roll count **defaulting to 1**, so every existing single-shot caller is unchanged by construction; the burst path passes its rounds spent. Cite `combat.cc:4229` (the ternary), `:4589` (the loop) and `:3713` (why rounds spent is the right number even though none were fired).

**Rounds spent, not rounds hit** — a fumbled burst fires nothing, and `*roundsSpentPtr` was assigned before the roll. Getting this wrong yields zero damage and a test that looks green if it only asserts "some damage".

- [ ] **Step 4: `dotnet test`** — expect 0 failed.

- [ ] **Step 5: Measure, classify, justify, record, verify** as in Task 1. This moves a fixture only where a burst fumble reaches `DAM_HIT_SELF` specifically.

- [ ] **Step 6: Commit.**

---

## Task 3: Backlog reconciliation

**Files:** `docs/BACKLOG.md`

- [ ] **Step 1:** F26 and F15 → shipped, with SHAs and any re-recorded fixtures.
- [ ] **Step 2: Correct F26's original entry, do not just mark it done.** It claimed `TryBurst`/`RollBurst` "have no crit-failure branch anywhere in them" and that wiring one in changes the RNG sequence for every cleanly-missing burst. Both were false: the detection, its draws, the day-2 gate and the abort were already present and faithful (`CombatEngine.cs:535-550`, porting `combat.cc:3703-3720`); only the effects were missing. Say so plainly — a wrong closed entry misleads as much as a wrong open one.
- [ ] **Step 3:** Record that F15 was confirmed **reachable and non-vacuous** by the `:3713`-before-`:3716` ordering, since a plausible reading would have closed it as unreachable.
- [ ] **Step 4:** Numbering — F1–F29 are taken. State what you chose for anything new.
- [ ] **Step 5:** Commit.

---

## Self-review notes

- **The detection is already correct and must not be touched.** The single largest risk here is an implementer "adding" a crit-failure branch that already exists, doubling the draws and moving every burst fixture. Task 1 Step 4's comment requirement and Step 6's stop condition both guard it.
- **Task 1 Step 1 is a genuine design decision**, deliberately not pre-decided: `delta` being local to `RollBurst` pulls toward calling from inside, while effects-in-a-roll-engine pulls the other way. The tuple's inability to distinguish abort from zero-hit is the fact that makes it non-obvious.
- **Ordering:** F26 before F15 because F15 has nothing to act on until a burst can reach `DAM_HIT_SELF`, and separating them keeps each delta attributable.
