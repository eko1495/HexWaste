# Sub-project 8: the crit-failure residuals — F15, F16, F17 — design spec (2026-08-20)

Close the three remaining crit-failure / explosion divergences left behind by the 2026-08-15 batch
(F11–F13). All three live in the same neighbourhood of `CombatEngine`, all three are re-record tier,
and all three were found *by* that batch rather than surviving it unnoticed.

Batched for the same reason F11–F13 were: they share fixtures. Recording them separately would
re-record the same transcripts three times and make each delta harder to attribute, not easier.

## Grounding — verified against `e97087b` on 2026-08-20

Every claim re-read in the reference before writing. The count of my own citation errors caught by
implementers and reviewers across this arc now stands at six, so treat all of these as falsifiable.

### F15 — a ranged self-hit rolls once per round of ammo, not once

`attackComputeCriticalFailure` (`combat.cc:4228-4230`):

```c
if ((attack->attackerFlags & DAM_HIT_SELF) != 0) {
    int ammoQuantity = attackType == ATTACK_TYPE_RANGED ? attack->ammoQuantity : 1;
    attackComputeDamage(attack, ammoQuantity, 2);
}
```

and `attackComputeDamage` wraps its per-round roll in `for (int index = 0; index < ammoQuantity; index++)`
(`combat.cc:4589`). `DAM_RANDOM_HIT` computes the same quantity the same way (`:4259-4260`).

Hexwaste's `CritFailDamage` rolls **once**, regardless of attack type. F11 fixed the *multiplier* at
this site and deliberately left the *count* alone, saying so in its own commit message.

Melee and unarmed are unaffected — `ammoQuantity` collapses to 1 off `ATTACK_TYPE_RANGED`. Only a
burst-capable ranged weapon fumbling into `DAM_HIT_SELF` or `DAM_RANDOM_HIT` with more than one round
diverges.

**This is the largest blast radius of the three, and the reason for the commit ordering below:** it
changes the RNG draw *count*, not merely a resulting figure. Everything downstream of an affected
fumble shifts.

### F16 — the other blast victims should run `damage_p_proc`, under the polarity we already adopted

This one needs care, because the backlog entry is right for a subtle reason and could easily be read
as wrong.

At `e97087b`, the extras loop passes `attack->defender == attack->oops` (`combat.cc:4751`) and
`_damage_object` gates the proc on `if (!a4)`, so for a crit-fail explode (where `defender == oops`)
the flag is **true** and no extras proc runs. On that basis the behaviour looks vanilla-faithful.

But Hexwaste does not carry `e97087b`'s polarity at these sites — it carries community fix #493,
which F13 already ported at the attacker site. Confirmed by diffing the fork:

```
-    bool v5 = attack->defender != attack->oops;
+    bool hitUnintendedTarget = attack->defender != attack->intendedTarget;
...
-        _damage_object(attacker, ..., attack->defender == attack->oops, attacker);
+        _damage_object(attacker, ..., hitUnintendedTarget, attacker);
-            _damage_object(obj, attack->extrasDamage[index], ..., attack->defender == attack->oops, ...);
+            _damage_object(obj, attack->extrasDamage[index], ..., hitUnintendedTarget, ...);
```

`#493` replaces **all three** site-specific expressions with one `hitUnintendedTarget`. For a
crit-fail explode that value is `false`, so the proc runs for the attacker **and** for every extra.
Hexwaste took the attacker half (F13) and not the extras half, which is why the engine is now
internally asymmetric on a single event: the fumbler runs its proc, the bystanders caught in his
exploding gun do not.

So F16 is a real divergence **from the polarity Hexwaste has chosen**, not from `e97087b`. That
distinction belongs in the code comment, because a future reader checking only `e97087b` will
otherwise conclude the fix is wrong.

### F17 — vanilla computes zero knockback for self-damage

`attackComputeCriticalFailure` clears `DAM_HIT` as its first statement (`combat.cc:4180`), so the
`attackComputeDamage` call it makes takes the attacker-damage branch, which sets
`knockbackDistancePtr = nullptr` unconditionally (`:4513-4517`). The reference computes **no**
knockback for the fumbler's own self-damage.

Hexwaste routes the explode branch through the generic `Explode`, whose per-victim tail calls
`Shove(centerTile, victim, damage / 10)` for every non-multihex victim — including the attacker
standing on the blast tile, where `HexGrid.RotationTo(centerTile, centerTile)` is degenerate. A
self-blast of ≥ 10 damage therefore shoves the fumbler in an arbitrary direction.

## Scope — one branch, three sequenced commits, ordered least- to most-disruptive

A fixture measurement runs between each. The order is deliberate, not arbitrary:

1. **F17** — suppress the self-shove. Narrowest: touches only `knockback:` lines, and only where a
   self-blast reaches 10 damage.
2. **F16** — run the party-gated `damage_p_proc` for the other blast victims, matching the gate F13
   already uses for the fumbler. Adds script procs; moves fixtures only if a blast victim's script
   defines `damage_p_proc`.
3. **F15** — roll `ammoQuantity` times for a ranged self-hit. **Last, because it changes the RNG draw
   count**, so putting it earlier would pollute the measurements of the other two and make their
   deltas unattributable.

### Out of scope

- Consolidating `Explode`'s remaining simplifications (single caller-supplied radius, the
  non-`attackComputeDamage` damage formula, victim discovery by tile-occupancy). Long-standing,
  documented, and unrelated to these three.
- The `CombatRoster` width gap and `LastAttackTarget`'s inertness — separate entries.

## What carries the proof

Hermetic tests through `FakeCombatHost`, each **confirmed failing pre-change and for the right
reason**:

1. **F17** — a critter fumbling into `DAM_EXPLODE` does not move, however large the self-damage.
   Assert its tile, and assert no `knockback:` transcript line names it.
2. **F17 boundary** — other blast victims are still shoved normally. The fix must suppress the shove
   for the self-damaged attacker only, not disable knockback for everyone.
3. **F16** — an unaffiliated scripted bystander caught in a fumbler's blast runs `damage_p_proc`;
   the dude and party members do not, matching F13's gate.
4. **F15** — a ranged `DAM_HIT_SELF` fumble with N rounds loaded rolls N times. Assert the **draw
   count**, not only the damage total — a test asserting the total alone can pass for the wrong
   reason if the multiplier drifts.
5. **F15 non-regression** — a melee/unarmed fumble still rolls exactly once (`ammoQuantity` collapses
   to 1 off `ATTACK_TYPE_RANGED`).

## Fixture expectations

- **F17** — expected to move `knockback:` lines in any fixture where a fumbler self-blasts for ≥ 10.
  Possibly none; the crit-failure fixtures are narrow.
- **F16** — expected to move nothing unless a fixture's blast victim has a scripted `damage_p_proc`.
- **F15** — the one genuinely likely to move fixtures, and to move them widely, because it changes
  draw counts. Any fixture reaching a multi-round ranged fumble shifts, and everything downstream of
  that draw shifts with it.

Protocol per commit: `check` first, **enumerate** every failure, **classify** each, **justify**, then
record, then confirm `git status` matches the enumeration. If a delta cannot be explained, stop and
report rather than recording — that discipline has produced a real finding in every sub-project of
this arc, including two in the last one.

## Definition of done

Three commits with a measurement between each; five hermetic tests green and confirmed failing
pre-change; every re-recorded fixture enumerated and classified; all four suites green;
`docs/BACKLOG.md` reconciled with F15–F17 shipped and the `#493`-polarity reasoning recorded in both
the code and the entry.

---

## SCOPE CORRECTION (2026-08-20, before implementation): F15 is BLOCKED and drops out

Grounding the plan turned up a fact that removes F15 from this batch.

**`attack->ammoQuantity` is rounds FIRED, not magazine contents.** `int v26 = 1` (`combat.cc:3845`)
is the default; only `_compute_spray` raises it (`:3850`), and it is assigned at `:3888`. So a
*single-shot* ranged fumble rolls exactly once — which is already what Hexwaste does. The divergence
F15 describes needs **more than one round fired**, i.e. a **burst** fumble.

**Hexwaste's burst path cannot fumble at all.** `TriggerCritFailure` has exactly three callers —
`TryAttack` (`:369`), `TryAllyAction` (`:3617`) and `EnemyAttack` (`:3748`) — all single-attack paths.
The burst method (`CombatEngine.cs:420-540`) contains no crit-failure path whatsoever.

In the reference, `attackComputeCriticalFailure` is called from the shared `ROLL_CRITICAL_FAILURE`
case of the attack-roll switch (`combat.cc:3933-3934`), which every attack shape reaches — bursts
included.

So F15 is not a one-site fix sitting behind an easy change; it is **unreachable** until burst attacks
can fumble. Implementing the `ammoQuantity` loop now would add code no path can exercise, and its test
would have to fabricate a state the engine cannot produce — precisely the "wired but inert" shape this
project's P114 lesson warns about.

**Therefore:**
- **F15 drops out of this batch** and stays open, marked blocked.
- The blocker is filed as its own entry: **Hexwaste's burst attacks never trigger critical failure**,
  a previously untracked gap that is larger and more consequential than F15 itself — it means no
  burst can ever drop a weapon, hit itself, lose ammo, or cripple the shooter, in a game where burst
  weapons are common.
- **This batch ships F17 then F16**, in that order, with a fixture measurement between them.

The blocker is also a re-record-tier item in its own right, and a substantial one: wiring the
crit-failure upgrade into the burst path adds an RNG draw to every missed burst, which shifts every
downstream draw in every fixture that misses with a burst. It wants its own spec, not a corner of
this one.
