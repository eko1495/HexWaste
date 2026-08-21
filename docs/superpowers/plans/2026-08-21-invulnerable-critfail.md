# F30 — an INVULNERABLE critter is exempt from critical-failure effects (spec + plan)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

Small enough that spec and plan are one document. One constant, one guard, one test, one docs update.

## Grounding — verified against `e97087b` on 2026-08-21

`attackComputeCriticalFailure` (`combat.cc:4178-4184`):

```c
static int attackComputeCriticalFailure(Attack* attack)
{
    attack->attackerFlags &= ~DAM_HIT;                                    // :4180

    if (attack->attacker != nullptr
        && _critter_flag_check(attack->attacker->pid, CRITTER_INVULNERABLE)) {
        return 0;                                                         // :4182-4184
    }

    if (attack->attacker == gDude) { ... day-6 gate ... return 0; }       // :4186-4194
```

- `CRITTER_INVULNERABLE = 0x400` (`obj_types.h:99`).
- The exemption is checked **before** the dude's day-6 gate, and **before** any `_cf_table` lookup — so an invulnerable attacker draws **no severity roll** and takes **no** effects.

**Hexwaste has no invulnerability check anywhere in `CombatEngine`.** An invulnerable critter that fumbles therefore resolves the full `_cf_table` result: it can drop or destroy its weapon, hit itself, lose its ammo, be crippled or blinded, and lose its turn. Scripted invulnerable NPCs are a normal content device, so this is reachable in ordinary play.

The plumbing already exists: `Proto.CritterFlags` is parsed (`ProtoDatabase.cs:138`) and `CombatEngine` already reads it for `CRITTER_NO_KNOCKBACK = 0x4000` (`CombatEngine.cs:75`).

## Scope

`ApplyCritFailureEffects` (`CombatEngine.cs:1197`) is the single effects entry point — both `TriggerCritFailure` (single-shot) and the burst abort route through it, so one guard covers every path. It goes at the **head**, before the existing day-6 gate at `:1201`, matching the reference's order.

Out of scope: any other use of `CRITTER_INVULNERABLE` (damage application, death). This item is the crit-failure exemption only.

## Task 1: the guard

**Files:** `src/Hexwaste.Formats/Combat/CombatEngine.cs`; `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`

- [ ] **Step 1: Write the tests.**
  - **A (primary):** an invulnerable critter that fumbles applies **no** effects — no `crit-fail:` transcript line, no weapon drop, no self-damage — **and draws no severity roll**. Assert the draw count, not just the absence of effects: the reference returns before the `_cf_table` lookup, so a guard placed too late would still consume the draw and silently diverge the RNG stream.
  - **B (boundary pin):** a non-invulnerable critter still fumbles normally. Without it, "always return false" passes A.
- [ ] **Step 2: Confirm A fails pre-change** (effects currently apply) and B passes both sides — label B a pin.
- [ ] **Step 3: Implement.** Add the `CRITTER_INVULNERABLE = 0x400` constant beside the existing `CRITTER_NO_KNOCKBACK` and guard at the head of `ApplyCritFailureEffects`, citing `combat.cc:4182-4184` and `obj_types.h:99`. Note in the comment that it precedes the day-6 gate deliberately, matching `:4182` before `:4186`.
- [ ] **Step 4: `dotnet test`** — expect 0 failed.
- [ ] **Step 5: Measure.** `scripts/combat-golden.sh check` to completion. Expected: nothing moves — a fixture would have to contain an invulnerable critter that fumbles. Enumerate and classify anything that does; stop and report if unexplainable.
- [ ] **Step 6: Commit.**

## Verification notes

- The controller runs `quest-golden.sh` and `encounter-golden.sh`; the implementer runs `combat-golden.sh` only.
- Every cited line number must be verified **as the code stands now** — this plan's author has had citations wrong eight times across this work.
