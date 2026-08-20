# Crit-failure residuals F17 + F16 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop a fumbling attacker being knocked back by its own blast (F17), and run the
party-gated `damage_p_proc` for the *other* victims of an attack-sourced blast (F16).

**Architecture:** Two small changes inside `CombatEngine.Explode`'s per-victim tail, committed
separately with a fixture measurement between them. No new type, no new host seam.

**Tech Stack:** C# / .NET 10 (`net10.0`), xUnit. Reference: `reference/fallout2-ce` at `e97087b`.

**F15 is NOT in this plan** — see the spec's scope correction. It is unreachable until burst attacks
can fumble, which is its own item.

## Global Constraints

- **Never copy, embed, or commit game assets.** `.gitignore` excludes `*.dat`, `*.map`, `*.frm`, `*.pal`, `game-data/`.
- **Port from `reference/fallout2-ce`, never guess.** Every behavioural change carries a comment citing its source. If a detail cannot be confirmed, **stop and ask**.
- **`alexbatalov e97087b` is authoritative for vanilla**; `community/main` is a bug-fix candidate source cited as `(community fix #NNN)`. SFALL-marked fixes inside `e97087b` are ported here by precedent; `// CE:` QoL never is.
- **VERIFY EVERY LINE NUMBER YOU CITE**, in the reference *and* in Hexwaste, **on this branch**. The plan author's citations have been wrong on six occasions across this work, including one that shipped into a source comment.
- **No new dependencies.** `src/Hexwaste.Formats` stays MonoGame-free.
- **Golden discipline:** scripts run the *prebuilt* binary. Never two nets at once, never backgrounded, never build while one runs. They print no fixture count — report exactly what they print. `combat-golden.sh` is the implementer's; **`quest-golden.sh` and `encounter-golden.sh` are the controller's**, as is `encounter-golden.sh record`.
- Every regression test **confirmed failing pre-change, and for the right reason**.

---

## Reference facts

- **F17.** `attackComputeCriticalFailure` clears `DAM_HIT` as its first statement (`combat.cc:4180`), so the `attackComputeDamage` call it makes takes the attacker-damage branch, which sets `knockbackDistancePtr = nullptr` unconditionally (`:4513-4517`). **Vanilla computes no knockback for the fumbler's own self-damage.**
- **F16.** The extras loop applies each victim's damage via `_damage_object(obj, attack->extrasDamage[index], animated, <flag>, attack->attacker)` (`combat.cc:4751`) — note the damage **source is the attacker**, not the victim. `_damage_object` gates the proc as `if (!a4)` (`:4847`) and additionally skips it when **both** object and source are party members (`:4849`).
  At `e97087b` that flag is `attack->defender == attack->oops`, which is **true** for a crit-fail explode, so no extras proc runs there. **But Hexwaste carries community fix #493** at these sites (F13 ported the attacker half). Diffing the fork shows #493 replaces all three site expressions with one `hitUnintendedTarget = attack->defender != attack->intendedTarget`, which is **false** for this event — so under the polarity Hexwaste has adopted, the proc runs for the attacker *and* every extra. **Say this explicitly in the code comment**, or a future reader checking only `e97087b` will conclude the fix is wrong.

## Hexwaste facts (verify on this branch)

- `Explode(int centerTile, MapObject? killer, int minDamage, int maxDamage, int radius, MapObject? selfDamageProcFor = null)`.
- Its per-victim tail (~`:1690-1720`): damage applied, log + `explosion-hit:` transcript, then the F13 self-proc gate (~`:1705`), then `Shove(centerTile, victim, damage / 10)` (~`:1711`) for non-multihex victims, then the kill check.
- The F13 gate reads: `victim == selfDamageProcFor && victim.Sid != -1 && victim != _host.Dude && !_host.PartyMembers.Contains(victim)`.
- `ApplyBurstExtras` (~`:975`) is Hexwaste's existing extras-proc site: `if (ex.Victim != dude && ex.Victim.Sid != -1)`, source `b.Attacker`.
- `Explode` is Hexwaste's **generic** blast path: grenades, the misc-10 marker and environmental blasts all use it, and those pass `killer == null`.

---

## Task 1: F17 — the fumbler is not shoved by its own blast

**Files:** `src/Hexwaste.Formats/Combat/CombatEngine.cs` (`Explode`); `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`

- [ ] **Step 1: Write both tests**

Test A — the fix: a critter fumbling into `DAM_EXPLODE` does **not** move, regardless of self-damage size. Assert its `HexTile` is unchanged **and** that no `knockback:` transcript line names it. Asserting only the tile is weaker than it looks: `RotationTo(centre, centre)` is degenerate, so a shove could in principle resolve to the same tile and the test would pass while the bug persisted.

Test B — the boundary: **other** blast victims are still shoved normally. The fix must suppress the shove for the self-damaged attacker only. Without this, "delete the Shove call" would pass Test A.

- [ ] **Step 2: Run both, confirm A fails and B passes**

Expected: A **fails** (the fumbler currently moves / a `knockback:` line names it); B **passes** both sides — it is a boundary pin, not a regression test, and is labelled as such.

- [ ] **Step 3: Implement**

In `Explode`'s per-victim tail, suppress the shove for the self-damaged attacker. The cleanest predicate is the one already in scope: skip when `victim == selfDamageProcFor`. Carry a comment citing `combat.cc:4180` and `:4513-4517` and stating plainly that vanilla computes *zero* knockback for self-damage, so this is a suppression of a Hexwaste-only side effect rather than a tuning choice.

**If you conclude a broader predicate is more faithful** — e.g. any victim whose damage came from its own fumble — say so with the reference behind it before implementing; do not widen it silently.

- [ ] **Step 4: `dotnet test`** — expect 0 failed.

- [ ] **Step 5: Measure** — `scripts/combat-golden.sh check`, run to completion. Expected: `knockback:` lines move only where a self-blast reached ≥ 10 damage; possibly nothing moves at all. Enumerate every failing fixture and classify each.

- [ ] **Step 6: Justify, record, verify** — only after classification. `git status --short tests/golden-combat/` must match your enumeration. Re-run `check`.

- [ ] **Step 7: Commit** with the per-fixture justification in the body, ending:
`Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`

---

## Task 2: F16 — the other blast victims run their `damage_p_proc`

**Files:** same two.

- [ ] **Step 1: Decide and state the scope question first, in your report**

`Explode` serves both attack-sourced blasts (a fumbling weapon, a thrown grenade) and environmental ones (the misc-10 marker), and the reference's extras path exists **only** for attacks — `attack->attacker` is the damage source at `combat.cc:4751`. So the proc should run for victims of an **attack-sourced** blast and not for an environmental one.

The plan's recommendation is to gate on `killer is not null`, which is exactly "this blast had an attacker". **Verify that every environmental caller really does pass `killer == null`** before relying on it — if any passes a non-null killer, say so and propose an explicit opt-in parameter instead, mirroring `selfDamageProcFor`.

- [ ] **Step 2: Write the tests**

Test A: an unaffiliated scripted bystander caught in an attack-sourced blast runs `damage_p_proc`, with the **attacker** as the source (not the victim — that is the F13 self-damage shape, and getting it backwards is the easy mistake here).

Test B: the party gate — a party-member victim of a party-member attacker does **not** run it, matching `_damage_object`'s `if (!objectIsPartyMember(a1) || !objectIsPartyMember(a5))` (`combat.cc:4849`).

Test C: an environmental blast (`killer == null`) runs no victim procs.

- [ ] **Step 3: Confirm A fails pre-change** (and for the right reason — the proc is absent, not the victim). B and C are boundary pins and may pass both sides; label them.

- [ ] **Step 4: Implement**, mirroring the gate `ApplyBurstExtras` already uses at ~`:975` plus the party check, with the source being the blast's `killer`. The comment must record the `#493`-polarity reasoning from the Reference facts above — including that at bare `e97087b` this proc would **not** run, and why Hexwaste's adopted polarity makes it correct here.

- [ ] **Step 5: `dotnet test`** — expect 0 failed.

- [ ] **Step 6: Measure** — `scripts/combat-golden.sh check`. Expected: nothing moves unless a fixture's blast victim has a scripted `damage_p_proc`. Enumerate and classify anything that does.

- [ ] **Step 7: Justify, record, verify, commit** as in Task 1.

---

## Task 3: Backlog reconciliation

**Files:** `docs/BACKLOG.md`

- [ ] **Step 1:** F17 and F16 → shipped, with SHAs and any re-recorded fixtures.
- [ ] **Step 2:** Record F16's `#493`-polarity reasoning in the entry as well as the code — that at `e97087b` this proc would not run, and Hexwaste's chosen polarity is what makes it correct.
- [ ] **Step 3:** **Rewrite F15 as BLOCKED**, not merely open: `attack->ammoQuantity` is rounds fired (`combat.cc:3845`, `:3850`, `:3888`), so only a burst fumble diverges, and Hexwaste's burst path has no crit-failure branch at all.
- [ ] **Step 4:** **New entry — Hexwaste's burst attacks never trigger critical failure.** `TriggerCritFailure`'s three callers are all single-attack paths (`CombatEngine.cs:369`, `:3617`, `:3748`); the burst method (`:420-540`) has no crit-failure path. Vanilla reaches it from the shared `ROLL_CRITICAL_FAILURE` case (`combat.cc:3933-3934`), which every attack shape hits. Consequence: **no burst can ever drop a weapon, hit itself, lose ammo or cripple the shooter.** Mark re-record tier — the upgrade draw shifts every downstream draw in any fixture that misses with a burst — and note that F15 is blocked behind it.
- [ ] **Step 5:** Numbering — F1-F25 are taken. State what you chose.
- [ ] **Step 6:** Commit.

---

## Self-review notes

- **Ordering:** F17 before F16 because F17's blast radius (`knockback:` lines on a ≥10-damage self-blast) is narrower than F16's (script procs, which can cascade through whatever the proc does). Measuring the narrow one first keeps attribution clean.
- **Both tasks carry a boundary pin** (F17's "others are still shoved", F16's party gate and environmental case) because in both cases the obvious over-broad implementation — delete the shove, run the proc for everyone — passes the primary test.
- **Known soft spot:** Task 2 Step 1's `killer is not null` gate is a recommendation, not a verified fact about every caller. The implementer is told to check it and to propose an explicit opt-in if it does not hold.
