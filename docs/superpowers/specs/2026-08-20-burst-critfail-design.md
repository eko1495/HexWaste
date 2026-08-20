# Sub-project 10: burst critical-failure effects (F26) and the burst self-hit roll count (F15) — design spec (2026-08-20)

Wire the crit-failure **effects** into Hexwaste's burst path (F26), and give a burst self-hit its
per-round roll count (F15), which becomes reachable the moment F26 lands.

## Grounding — verified against `e97087b` on 2026-08-20, and it corrects F26's own entry

### F26's premise in the backlog is WRONG, and the item is much smaller than it says

F26 states that "`TryBurst` and its `RollBurst` engine have no crit-failure branch anywhere in them"
and that wiring one in "changes the RNG draw sequence for every fixture where a burst attack currently
misses cleanly". **Both halves are false.** `RollBurst` (`CombatEngine.cs:535-550`) already does this:

```csharp
int delta = accuracy - _rng.Next(1, 101);        // the inception d100, ALWAYS drawn
if (_host.CriticalsEnabled)                      // the day-2 gate
{
    if (delta < 0)
    {
        if (_rng.Next(1, 101) <= -delta / 10)
            return (accuracy, n, 0, 0, []);      // CRITICAL_FAILURE: burst aborts, bullets still spent
    }
    else if (...) { accuracy = Math.Min(accuracy + 20, 95); }   // CRITICAL_SUCCESS
}
```

That is a faithful port of `_compute_spray` (`combat.cc:3703-3720`): the inception roll comes from
`randomRoll` (`:3716`), and a `ROLL_CRITICAL_FAILURE` returns immediately (`:3718-3719`) without
computing any spray. Hexwaste even matches the subtle part — `*roundsSpentPtr = ammoQuantity` is
assigned at `:3713`, *before* the roll, so the rounds are spent even though none are fired, which is
exactly what Hexwaste's `return (accuracy, n, 0, 0, [])` encodes.

**So the detection, its RNG draws, the day-2 gate and the abort are all already correct.** What is
missing is only that `TriggerCritFailure` is never called, so none of the *effects* apply: no weapon
drop, no self-hit, no ammo loss, no crippled arm, no lost turn.

This materially shrinks the item and falsifies its re-record justification. The detection draw already
exists, so no fixture moves merely because a burst misses. A fixture moves only where a burst
**actually crit-fails**, and only because the effects now do something.

### F15 is real, not vacuous — confirmed by the same reading

It was worth checking whether `_compute_spray`'s early return skips the out-param, which would make a
burst self-hit roll once anyway and close F15 outright. It does not: `*roundsSpentPtr = ammoQuantity`
is assigned at `:3713`, before the roll at `:3716` and before the early return at `:3718-3719`. So
`attack->ammoQuantity = v26` (`:3888`) carries the burst size into
`attackComputeCriticalFailure`'s `ammoQuantity = attackType == ATTACK_TYPE_RANGED ? attack->ammoQuantity : 1`
(`:4229`), and `attackComputeDamage` loops that many times (`:4589`).

**A burst that fumbles into `DAM_HIT_SELF` therefore rolls damage once per round of the burst.**

### Why the two are inseparable

F15 has nothing to act on until a burst can reach `DAM_HIT_SELF`, which is F26. And F26 shipped
without F15 would apply a *single* self-hit roll on a burst fumble — knowingly wrong, and it would
need its own re-record to fix immediately afterwards. Doing them together records once.

## Scope — one branch, two sequenced commits

1. **F26 — call `TriggerCritFailure` on the burst's crit-failure branch.** The detection and abort
   stay exactly as they are; the effects are applied at the point `RollBurst` currently returns the
   zeroed tuple. Every burst path must reach it: `TryBurst`, `TryAllyBurst`, `TryEnemyBurst`.
2. **F15 — a ranged self-hit rolls once per round spent.** `CritFailDamage` gains the roll count,
   defaulting to 1 so every existing single-shot caller is unchanged; the burst path passes its rounds
   spent.

A fixture measurement runs between them. F26 first because F15 is meaningless without it, and
because separating them keeps each delta attributable.

### The structural question the implementer must answer first

`TriggerCritFailure` currently lives on the single-attack paths and takes `(attacker, attackerIsDude,
weaponProto, weaponItem, delta)`. `RollBurst` is a pure-ish roll engine that returns a tuple; it does
not apply effects. **Where the call belongs — inside `RollBurst`, or at each of the three burst call
sites on seeing the zeroed result — is a real design decision, not a detail.** Applying effects from
inside a roll engine may be the wrong shape; calling it from three sites risks one being missed.
Decide it explicitly, state the reasoning, and prefer whichever makes it impossible for a burst path
to abort without applying effects.

### Out of scope

- The `#493`/`!= dude` proc-gate inconsistencies (F27, F29) and the C4 shape (F28).
- Any change to the detection roll, its draws, or the day-2 gate — they are already faithful and
  touching them would move every burst fixture for no reason.

## What carries the proof

Hermetic tests through `FakeCombatHost`, each **confirmed failing pre-change and for the right
reason**:

1. **F26** — a burst that rolls a critical failure applies its effects: assert a `crit-fail:`
   transcript line with the resolved flags, on a burst attack.
2. **F26 coverage** — the same holds for the ally and enemy burst paths, not just the dude's. This is
   the test that catches "wired one of three call sites".
3. **F26 non-regression** — a burst that does *not* crit-fail is unchanged: same rounds spent, same
   damage, no extra RNG draw. The detection already existed, so this must stay byte-identical.
4. **F15** — a burst `DAM_HIT_SELF` fumble rolls damage **once per round spent**. Assert the draw
   count, not only the total: a total-only assertion can pass for the wrong reason if the multiplier
   drifts, which is exactly how F11 hid.
5. **F15 non-regression** — a single-shot ranged fumble and a melee fumble each still roll exactly
   once.

## Fixture expectations

Only fixtures where a burst **actually crit-fails** can move — the detection draws are unchanged. The
combat suite has burst fixtures (`arcaves-burst-smg`, `arcaves-burst-shotgun`,
`denbus2-burst-collateral`); whether any of them rolls a fumble is unknown until measured.

Protocol per commit: `check` first, enumerate, classify, justify, record, confirm `git status` matches
the enumeration. If a delta cannot be explained, stop and report.

## Definition of done

Both commits landed with a measurement between them; five hermetic tests green and confirmed failing
pre-change; every re-recorded fixture enumerated and classified; all four suites green;
`docs/BACKLOG.md` reconciled — F26 and F15 shipped, **and F26's original entry corrected**, since it
claimed a missing branch that was in fact present and a re-record justification that does not hold.
