# F32 — give the golden suite a way to catch a party-on-party proc regression (spec + plan)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

Small item, so spec and plan are one document.

## The entry's own suggestion does not work — this is why

F32 proposes authoring "a companion-vs-companion friendly-fire scenario … as a golden fixture", so the
suite can catch a regression of F27 (a party member's `damage_p_proc` must be suppressed when another
party member damages them).

**That cannot work, and the reason is structural.** Damage-proc output reaches the player through
`_host.Log(...)`, and `ViewerGame.Log` (`ViewerGame.cs:5840`) appends to `_messageLog` and queues a
floating-text entry — it **never writes to stdout**. Only `Transcript` does
(`ViewerGame.CombatHost.cs:979`, `Console.WriteLine`), and the golden scripts capture stdout. Confirmed:
none of the six `RunDamageProc` call sites emits a `Transcript` line.

So a party-on-party fixture would be **byte-identical whether the proc runs or not**. Authoring one
would produce a fixture that looks like coverage and provides none — worse than the acknowledged gap,
because it would retire the entry while leaving the hole.

## What to do instead — the shape this project already used for F21

F21 faced the same problem (behaviour with no golden-visible signal) and solved it with a headless
harness probe that was then **pinned as a combat-golden scenario**:

```
"walker-restart|--map denbus2.map --walker-restart-probe 14716 14718 14716"    # scripts/combat-golden.sh:49
```

That gives automated cover without touching a single existing fixture. Do the same here.

## Scope

1. **A probe** — e.g. `--party-proc-probe` — that stages party-on-party damage and reports, on stdout,
   whether the damage proc ran. It must print a **discriminating value**, in the way
   `walker-restart-probe` prints `started2=`: a line that changes if F27's gate is reverted.
2. **A pinned scenario** in `scripts/combat-golden.sh` exercising it, recorded as a new fixture.

**The probe must also cover the positive case**, not only the suppression: a party member damaged by
an **enemy** must still run its proc (`combat.cc:4849` is a *pair* gate — it suppresses only when both
sides are party members). A probe that reports "no proc ran" for everything would pass while the gate
was stubbed to `false`.

### Out of scope

- Adding `Transcript` output to the six production `RunDamageProc` sites. That would emit new lines
  wherever procs currently run and re-record many existing fixtures — a large, behaviour-adjacent
  change for a coverage problem, and it would bake diagnostic output into the engine's transcript.
- Any change to the gate itself. F27 is shipped and reviewed; this item is coverage only.

## Task 1: the probe and its pinned scenario

**Files:** `src/Hexwaste.Viewer/Program.cs` (flag), `src/Hexwaste.Viewer/ViewerGame.cs` (`StartupAction`),
`src/Hexwaste.Viewer/ViewerGame.Harness.cs` (handler), `scripts/combat-golden.sh` (scenario),
`tests/golden-combat/` (new fixture)

- [ ] **Step 1: Model the probe on `--walker-restart-probe`.** Read its `StartupAction` record, its
  `Program.cs` parsing case, and its handler in `ViewerGame.Harness.cs`. Follow that shape rather than
  inventing a new one.
- [ ] **Step 2: Stage both cases and print both discriminating values** on one line — party→party
  (expect suppressed) and enemy→party (expect it runs). Name a victim with a real `Sid`; a victim with
  `Sid == -1` can never run a proc and would make the probe vacuous.
- [ ] **Step 3: Run the probe against the current build** and record its exact output.
- [ ] **Step 4: Prove it discriminates.** Temporarily revert F27's gate — make `ShouldRunDamageProc`
  ignore the party pair test — rebuild, re-run the probe, and confirm the party→party value **flips**.
  Restore, rebuild, confirm the output returns. **Report that evidence.** A probe never shown to fail
  is not a regression net; this is the same standard F21's pinned scenario met.
- [ ] **Step 5: Add the scenario** to `scripts/combat-golden.sh` beside `walker-restart`, with a
  comment saying what it pins and why (the behaviour has no other golden-visible signal).
- [ ] **Step 6: Record.** `scripts/combat-golden.sh record` must produce exactly **one new** fixture
  file and **zero modified** ones. Any modified fixture is a stop condition — the probe must not
  perturb shared state.
- [ ] **Step 7:** `dotnet test`, then `scripts/combat-golden.sh check` — expect ALL PASS with the new
  scenario included.
- [ ] **Step 8: Commit.**

## Task 2: docs

- [ ] Close F32, and **record why the entry's own suggestion was wrong** — proc output goes to `Log`,
  never `Transcript`, so a fixture could not have observed it. That reasoning is the useful part for a
  future reader who wonders why a probe was used instead of a scenario.
- [ ] Note the F21 precedent explicitly, so the next "behaviour with no golden-visible signal" reaches
  for the same shape.

## Verification notes

- The controller runs `quest-golden.sh` and `encounter-golden.sh`; the implementer runs
  `combat-golden.sh` only.
- Verify every cited line number **as the code stands now** — this plan's author has had citations
  wrong eight times across this work.
