# One model of `_damage_object`'s damage-proc gate (F27 + F29) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace six divergent approximations of one reference predicate with a single helper — closing F27, and resolving F29 in whichever direction the evidence actually points.

**Architecture:** One private predicate in `CombatEngine`, six call sites routed through it. Site-specific conditions stay at their sites.

**Tech Stack:** C# / .NET 10 (`net10.0`), xUnit. Reference: `reference/fallout2-ce` at `e97087b`.

## Global Constraints

- **Never copy, embed, or commit game assets.** `.gitignore` excludes `*.dat`, `*.map`, `*.frm`, `*.pal`, `game-data/`.
- **Port from `reference/fallout2-ce`, never guess.** If a detail cannot be confirmed, **stop and ask**.
- **`alexbatalov e97087b` is authoritative for vanilla**; `community/main` is a bug-fix candidate source cited as `(community fix #NNN)`.
- **VERIFY EVERY LINE NUMBER YOU CITE**, reference and Hexwaste, **as the code stands when you write it**. This plan's author has had citations wrong eight times across this work — six misreadings and two sets that drifted when earlier commits shifted the same file.
- **No new dependencies.** `src/Hexwaste.Formats` stays MonoGame-free.
- **Golden discipline:** scripts run the *prebuilt* binary; never two at once, never backgrounded, never build while one runs; report exactly what they print. `combat-golden.sh` is the implementer's; **`quest-golden.sh`, `encounter-golden.sh` and `encounter-golden.sh record` are the controller's.**
- Every regression test **confirmed failing pre-change, and for the right reason**.

---

## Reference facts

`_damage_object` (`combat.cc:4847-4852`):

```c
if (!a4) {                                                     // the per-site "unintended target" flag
    if (!objectIsPartyMember(a1) || !objectIsPartyMember(a5)) { // PAIR gate  (:4849)
        scriptSetFixedParam(a1->sid, damage);
        scriptExecProc(a1->sid, SCRIPT_PROC_DAMAGE);
    }
}
```

The dude counts as a party member — `gPartyMembers->object = gDude` (`party_member.cc:725`). So:

| damaged | source | reference |
|---|---|---|
| dude | enemy | runs |
| dude | party | skipped |
| party member | enemy | runs |
| party member | party | skipped |

## Hexwaste's six sites (verify line numbers before citing)

| Site | Gate as shipped |
|---|---|
| burst main target (`CombatEngine.cs:964`) | `!= dude && Sid != -1` |
| `ApplyBurstExtras` (`:996`) | `!= dude && Sid != -1` |
| F13 self-damage (`:1329`) | `victim == attacker && dmg > 0 && Sid != -1 && != Dude && !PartyMembers.Contains` |
| single-shot defender (`:1561`) | `!= dude && Sid != -1` |
| `Explode` self-proc (`:1755`) | `victim == selfDamageProcFor && Sid != -1 && …` |
| F16 blast victims (`:1802`) | `attackSourced && victim != killer && Sid != -1 && != Dude && !(both party)` |

---

## Task 1: Settle the dude question — investigation only, no behavioural change

**This task ships no behaviour.** Its output is a finding and a recommendation.

F29 claims the `!= dude` term is a divergence to remove. **The evidence for and against is genuinely thin, and the spec's own supporting argument is weaker than it first appeared — do not inherit it uncritically:**

The spec cites `ViewerGame.cs:1629` and `:1799` as suggesting a deliberate "the dude's script never runs from engine hooks" convention. On closer reading **both are map-wide script sweeps** — `map_exit` (`:1629`, which also excludes party members) and the start/`map_enter` pass (`:1799`). They exclude the dude because the reference iterates *map objects* and the player is not one. That is a different concern from whether a damaged object's `damage_p_proc` should fire, so **those two sites do not establish a damage-hook convention.** Treat the question as open.

- [ ] **Step 1: Establish whether the dude can carry a `Sid` at all in practice.**

This is potentially decisive: if the dude's `Sid` is always `-1`, the `!= dude` term is inert everywhere and F29 is a no-op cleanup rather than a behavioural question. Check the map data path that builds the dude object, the fixtures, and a real run if a probe makes that cheap. Report what you find and how you established it — "I could not determine this" is an acceptable answer, guessing is not.

- [ ] **Step 2: Look for any real evidence of a damage-hook convention**, as opposed to the map-sweep filters above. Does anything in the codebase, comments, or `docs/` justify suppressing the dude's own `damage_p_proc` specifically? Search rather than assume in either direction.

- [ ] **Step 3: Recommend one of three outcomes, with your evidence:**
  - **Inert** — the dude never carries a `Sid`, so the term does nothing. Drop it as dead code during Task 2, no behavioural risk, and rewrite F29 to say so.
  - **Real convention** — something genuinely justifies it. Keep the term, document it once in the helper with that justification, and rewrite F29 as a deliberate divergence rather than a pending fix.
  - **Unfounded** — the dude does carry a `Sid` and nothing justifies the exclusion. Removing it is a real behavioural change (the dude's `damage_p_proc` starts firing when enemies damage him, which is what vanilla does) and must be measured and justified like any other.
  - **Undecidable** — say so, keep the term, and record the open question in F29.

- [ ] **Step 4: Report and stop.** Do not implement Task 2 until the finding is acknowledged.

---

## Task 2: One helper, six call sites

**Files:** `src/Hexwaste.Formats/Combat/CombatEngine.cs`; `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`

- [ ] **Step 1: Write the tests first**

- **A — the pair gate, all four quadrants:** enemy→party-member runs; party→party skipped; and the two dude rows per Task 1's outcome. This is the predicate's entire content.
- **B — F27's actual content:** a party-member extra damaged by a party-member burst no longer runs its proc. **Must fail pre-change** — this is the bug being fixed.
- **C — site-specific conditions survive:** one test per site proving its own guard still applies. At minimum: a missed shot's collateral victim still runs **no** proc (F12), and an environmental blast still runs none (F16's `attackSourced`). **These are the tests that catch a flattening**, which is the main risk of this task.

- [ ] **Step 2: Confirm B fails pre-change** and that the C tests pass on both sides — they are pins guarding against regression, not new behaviour.

- [ ] **Step 3: Implement the helper.** A single private predicate, e.g. `ShouldRunDamageProc(MapObject target, MapObject? source)`, carrying the `Sid != -1` precondition and `combat.cc:4849`'s pair gate — plus the dude term if and only if Task 1 justified keeping it. Cite `:4849` and `party_member.cc:725` (why the dude counts as a party member).

**DO NOT fold site-specific conditions into it.** The `a4`/`hitUnintendedTarget` semantics, `attackSourced`, `victim == selfDamageProcFor`, `victim == attacker`, `dmg > 0` all differ legitimately per site — F12, F13 and F16 each established theirs deliberately. Flattening them would silently recreate F12, where a collateral victim runs a proc the reference suppresses.

- [ ] **Step 4: Route all six sites through it**, leaving each site's own conditions in place at the site.

- [ ] **Step 5: `dotnet test`** — expect 0 failed.

- [ ] **Step 6: Measure.** `scripts/combat-golden.sh check`, to completion. Adding the party gate can only *suppress* procs, and only where a party member damages another party member — plausible in companion fixtures. **Enumerate and classify every failure.** If a fixture moves for a reason other than a suppressed party-on-party proc (or the dude change, if Task 1 chose that branch), stop and report.

- [ ] **Step 7: Justify, record, verify.** `git status --short tests/golden-combat/` must match your enumeration; re-run `check`.

- [ ] **Step 8: Commit**, ending `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`

---

## Task 3: Backlog reconciliation

**Files:** `docs/BACKLOG.md`

- [ ] **Step 1:** F27 → shipped, with its SHA. **Correct its scope**: it attributes the missing party gate to `ApplyBurstExtras` alone, when four of six sites lacked it.
- [ ] **Step 2: Rewrite F29 to match Task 1's finding** — shipped, deliberate-divergence-documented, or still-open-with-the-question-recorded. Do **not** write it up as a fixed bug unless Task 1 established that it was one.
- [ ] **Step 3:** Record that the spec's original supporting argument for F29 (the two `ViewerGame.cs` sites) was found to be map-sweep filters, not damage-hook evidence — so the next reader does not re-derive a weak inference.
- [ ] **Step 4:** Numbering — F1–F31 are taken. State what you chose for anything new.
- [ ] **Step 5:** Commit.

---

## Self-review notes

- **Task 1 ships nothing on purpose.** F29 is the first item in this backlog where the honest answer may be "not a defect", and the sequencing exists so that outcome is reachable without having already written the code that presumes otherwise.
- **The main technical risk is flattening.** Six gates converging into one is exactly where legitimately-different site conditions get absorbed; Task 2 Step 1's C tests exist specifically to fail if that happens.
- **The spec's own argument was corrected here**, not carried forward. That correction belongs in the plan rather than being quietly dropped.
