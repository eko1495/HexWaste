# Sub-project 11: one model of `_damage_object`'s damage-proc gate (F27 + F29) — design spec (2026-08-20)

Replace six divergent approximations of one reference predicate with a single helper, closing **F27**
and resolving **F29** — which grounding suggests may not be a defect at all.

## Grounding — verified against `e97087b` on 2026-08-20

### The reference predicate

`_damage_object` (`combat.cc:4847-4852`) runs a damaged object's `damage_p_proc` under exactly two
conditions:

```c
if (!a4) {                                                     // :4847  "hit an unintended target" flag
    if (!objectIsPartyMember(a1) || !objectIsPartyMember(a5)) { // :4849  PAIR gate
        scriptSetFixedParam(a1->sid, damage);
        scriptExecProc(a1->sid, SCRIPT_PROC_DAMAGE);
    }
}
```

The second is a **pair** gate: the proc is skipped only when the damaged object *and* the damage
source are **both** party members. And the dude counts — `gPartyMembers->object = gDude`
(`party_member.cc:725`), so `objectIsPartyMember(gDude)` is true. Consequences:

| damaged | source | reference behaviour |
|---|---|---|
| dude | enemy | proc **runs** |
| dude | party member | skipped |
| party member | enemy | proc **runs** |
| party member | party member | skipped |

### Hexwaste has six sites and six different approximations

All six call `_host.RunDamageProc`, and not one implements the predicate above:

| Site | Gate as shipped |
|---|---|
| burst main target (`CombatEngine.cs:964`) | `!= dude && Sid != -1` |
| `ApplyBurstExtras` (`:996`) | `!= dude && Sid != -1` |
| F13 self-damage (`:1329`) | `victim == attacker && dmg > 0 && Sid != -1 && != Dude && !PartyMembers.Contains` |
| single-shot defender (`:1561`) | `!= dude && Sid != -1` |
| `Explode` self-proc (`:1755`) | `victim == selfDamageProcFor && Sid != -1 && …` |
| F16 blast victims (`:1802`) | `attackSourced && victim != killer && Sid != -1 && != Dude && !(both party)` |

**F27** is the observation that `ApplyBurstExtras` lacks the party gate. True, and it understates the
problem: four of the six lack it.

### F29 is NOT established as a defect, and this spec must not assume it is

F29 records that every site carries a `!= dude` term the reference lacks, and frames it as a
divergence to remove. Grounding casts real doubt on that. Two script-dispatch sites filter
`o.Sid != -1 && o != _dude?.Dude` (`ViewerGame.cs:1629`, `:1799`) — they exclude the dude **even when
he carries a script id**. That exclusion is redundant unless the dude can have one, which suggests a
deliberate, codebase-wide convention: **the dude's own script is never run from engine hooks.**

If that convention is real and grounded, the `!= dude` term in these gates is an expression of it, not
a misreading — and removing it would break a deliberate decision while claiming to fix a bug. That is
the precise failure this project has guarded against repeatedly, in both directions.

**So F29 is an open question, not a known defect**, and this sub-project resolves it either way rather
than presuming the answer.

## Scope

### 1. Establish the dude convention — first, before any behavioural change

Determine why the dude is excluded from engine-driven script dispatch, and whether the dude can carry
a `Sid` at all in practice (map data, fixtures, real play). Three outcomes, and the spec accepts any:

- **The convention is real and grounded** (e.g. Hexwaste deliberately never runs the player's script,
  or the dude provably never has a Sid so the term is inert). Then keep the term, **document it once**
  in the shared helper with its reasoning, and **rewrite F29** to record it as a deliberate divergence
  rather than a pending fix.
- **The convention is unfounded** — the term is cargo-culted and the dude does carry a Sid. Then
  remove it, and treat the result as a measured behavioural change: the dude's `damage_p_proc` would
  begin running when enemies damage him, which is what vanilla does.
- **Undecidable from the evidence available.** Then say so plainly, keep the term, and record the
  question in F29 rather than resolving it by assumption.

**Report the finding and the chosen branch before implementing anything behavioural.**

### 2. One helper, six call sites

A single private predicate — e.g. `ShouldRunDamageProc(MapObject target, MapObject? source)` —
implementing `combat.cc:4849`'s pair gate plus the `Sid != -1` precondition, and the dude term if and
only if step 1 justifies keeping it. Every one of the six sites routes through it.

Each site keeps its own **site-specific** conditions, which are not part of this predicate: the
`a4`/`hitUnintendedTarget` flag semantics that F12/F13/F16 established per call site, `attackSourced`,
`victim == selfDamageProcFor`, `victim == attacker`, `dmg > 0`. **Do not fold those into the helper** —
they differ legitimately between sites and flattening them would recreate the F12 bug (a collateral
victim running a proc the reference suppresses).

### 3. Out of scope

- The `#493` polarity decisions themselves (F12/F13/F16) — settled, and not what this changes.
- F30 (invulnerable exemption) and F31 (ammo-cost scaling).

## What carries the proof

Hermetic tests through `FakeCombatHost`, each **confirmed failing pre-change and for the right
reason**:

1. **The pair gate, all four quadrants** — enemy→party-member runs; party→party skipped;
   enemy→dude and party→dude per whatever step 1 concludes. This is the predicate's whole content.
2. **`ApplyBurstExtras` gains the party gate** — a party-member extra damaged by a party-member burst
   no longer runs its proc. This is F27's actual content and must fail pre-change.
3. **Site-specific conditions survive unification** — one test per site proving its own guard still
   applies: notably that a missed shot's collateral victim still runs **no** proc (F12), and that an
   environmental blast still runs none (F16's `attackSourced`). These are the tests that catch a
   flattening.

## Fixture expectations

Adding the party gate to the four sites that lack it can only *suppress* procs, and only where a party
member damages another party member — plausible in companion-heavy fixtures. Whether the dude term
changes depends entirely on step 1.

Measure, enumerate, classify, justify, record. If a delta cannot be explained, stop and report.

## Definition of done

One helper; all six sites routed through it; step 1's finding reported and F29 rewritten to match it —
whether that means "shipped", "deliberate divergence, documented" or "still open, question recorded";
site-specific conditions provably intact; all four suites green; `docs/BACKLOG.md` reconciled.

**Or:** step 1 shows the dude convention is load-bearing in a way that makes unification unsafe, and
the work stops with that recorded — a legitimate outcome, and the reason step 1 comes first.
