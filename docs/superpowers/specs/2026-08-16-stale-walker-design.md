# Sub-project 6: the stale walker — membership-vs-liveness in `_npcWalkers` (F21) — design spec (2026-08-16)

Close **F21**: a finished NPC walker is never removed from `_npcWalkers`, and `StartNpcWalk`'s guard
tests dictionary *membership* rather than *liveness*, so the critter is frozen for the rest of the
fight while the engine keeps logging flights it never makes.

This is the second live bug of the arc and, unlike F18, its blast radius is **unknown in advance and
potentially large** — it can affect any fixture where an NPC walker completes mid-run.

## The bug

Two independent defects compound:

**1. The guard asks the wrong question** (`ViewerGame.cs:3328`):

```csharp
if (npc == _dude?.Dude || _npcWalkers.ContainsKey(npc) || Fid.Type(npc.Fid) is not ObjectType.Critter)
    return false;
```

`_npcWalkers.ContainsKey(npc)` is true for a walker that finished long ago — `DudeController.Moving`
is `_rotations is not null` (`DudeController.cs:33`), so a completed walker is present but idle.
The intended meaning is "this critter is already animating", and membership is not that.

**2. The prune is nested inside an unrelated feature.** Finished walkers are removed only inside
`UpdateAmbientLife` (`ViewerGame.cs:3262-3272`), *after* its early return:

```csharp
if (DisableAmbientLife || _worldmapOpen)
    return;
```

So walker lifecycle management is a side effect of ambient fidgeting. Three ways it fails to run:

- the `--fight` autoplay harness never calls `UpdateAmbientLife` at all — it pumps
  `walker.Update(10)` directly over `_npcWalkers.Values` (`ViewerGame.Harness.cs:2037-2038`);
- the brawl-watch autoplay loop has the identical shape (`ViewerGame.Harness.cs:207-208`);
- **in the real interactive game**, `--no-ambient` takes the early return.

The last one is why this is not merely a test-harness artefact. **Correction (final review):** an open
worldmap does *not* independently defeat the prune — `Update` itself returns early whenever
`_worldmapOpen` is true, before `UpdateAmbientLife` is reached at all, so no walker advances while the
worldmap is open and none can go stale from that state. `--no-ambient` is the only real interactive arm.

### The observable

`tests/golden-combat/denbus2-fight-flee.txt` logs `flee: Healthy Slave@10270 -> 8870` in rounds 3
and 4, with the origin frozen at 10270 both times. 8870 is *not* blocked, so F18's destination check
correctly leaves this pair alone — the cause is entirely this one. `TryFlee` writes its transcript
line and zeroes the critter's AP before `StartWalk` reports failure, so the fixture records a flight
that never happened.

## Grounding

There is no direct port here — `_npcWalkers` is Hexwaste's own structure, not a reference one. But the
reference does answer *what the guard should mean*: `animationIsBusy` (`animation.cc:581`) walks only
sequences that are actually in use (`animationSequence->field_0 != -1000`) and returns busy only for a
live, non-callback, non-idle-stand animation. **The reference's busy test is liveness-based; ours is
membership-based.** That is the divergence, and it is the citation the fix carries.

## Scope — two commits, deliberately ordered

### Commit 1 — the guard tests liveness (behavioural; moves fixtures)

```csharp
if (npc == _dude?.Dude
    || (_npcWalkers.TryGetValue(npc, out DudeController? active) && active.Moving)
    || Fid.Type(npc.Fid) is not ObjectType.Critter)
    return false;
```

A stale idle entry is then replaced by the new walker at the existing
`_npcWalkers[npc] = walker` (`ViewerGame.cs:3383`) — an indexer assignment, so replacement is clean.
The discarded walker's `TileChanged` handler goes with it, and `_blockedTiles` stays consistent
because the new walker's captured `previousTile` is the critter's current tile.

This fix alone corrects every failing path, including the two non-harness ones, because it no longer
depends on pruning having happened.

### Commit 2 — the prune stops being a side effect of ambient life (must move nothing)

Hoist the finished-walker prune out of `UpdateAmbientLife`'s early return so lifecycle management runs
regardless of `DisableAmbientLife` / `_worldmapOpen`, and make the two autoplay loops prune as they
pump. **Given commit 1, this must be behaviour-neutral** — the guard no longer consults staleness — so
it is the perfect check on itself: if commit 2 moves a fixture, something is wrong with the analysis,
not with the fixture.

Ordering matters and is not cosmetic. Commit 1 alone establishes the behavioural delta and takes the
re-record; commit 2 then proves it is inert. Merging them would make it impossible to tell whether the
delta came from the semantics or the bookkeeping.

### Out of scope

- **Any redesign of `_npcWalkers`** (e.g. folding walkers into `_animator`, or making the dictionary
  self-pruning). Tempting, larger, and not needed to fix the defect.
- **The stale-entry reference retention.** With commit 2 the dictionary drains normally; whether
  entries can still outlive a critter's removal from the map is a separate question. If commit 2's
  implementer sees evidence either way, record it — do not chase it.
- **`TryFlee` writing its transcript line and zeroing AP before knowing the walk succeeded.** F18
  rejected reordering that line, and the same reasoning holds: with the walk actually starting, the
  line is truthful. Recorded as a rejected alternative again, not re-litigated.

## What carries the proof

The fixtures will move, so they are records of a consequence. The proof is hermetic and must be
**confirmed failing pre-change**:

1. **The guard's rule, directly.** A critter whose walker has finished can start a new walk; one whose
   walker is still moving cannot. This is the entire semantic change and it is the test that would
   have caught the bug.
2. **Replacement is clean.** After the second walk starts, `_npcWalkers` holds the *new* walker for
   that critter, not the stale one.
3. **The engine-level pairing, reusing F18's invariant.** A critter that logs a `flee:` line must
   actually move — driven through a sequence where the first flight completes and a second is
   attempted. This is the fixture symptom in hermetic form.

These live in the viewer's test surface rather than `Hexwaste.Formats`, since `StartNpcWalk` is a
`ViewerGame` member. **If there is no viable seam to test it hermetically, say so explicitly and
propose one** rather than declaring the change proven by fixtures alone — that would be exactly the
laundering this tier exists to prevent.

## Fixture expectations — honestly, this one is not predictable

Unlike every previous sub-project in this arc, **the failing set cannot be predicted from analysis.**
Any fixture in which an NPC walker finishes and that critter later attempts to move may change, across
both the combat and encounter suites. `denbus2-fight-flee`'s `Healthy Slave` lines are the one
certainty.

The protocol therefore matters more, not less:

1. Measure with `check` first and **enumerate every failing fixture**.
2. For each, confirm the delta is the same *class* of change: a critter that previously froze now
   moves. A delta of any other kind — changed damage, changed winner without a movement change,
   changed round count with no freed critter — is a stop condition to investigate individually.
3. Only then record, and confirm `git status` lists exactly the enumerated set.

**A large failing set is not itself alarming here** — it would mean the bug is widespread, which the
analysis predicts. A failing set containing an *unexplainable* delta is alarming.

## The justification

The commit body must name every re-recorded fixture and, for each, the critter that was frozen and now
moves. Where the set is large, state the shared mechanism once and then list per-fixture evidence —
not a bare list of filenames. **If any fixture's delta cannot be explained, do not record it**; stop
and report.

## Docs

`docs/BACKLOG.md`: F21 → shipped, with both SHAs, the full list of re-recorded fixtures, and the
consequence spelled out — that transcripts recorded through the autoplay paths before this fix may
have contained frozen-critter artefacts, and which ones did. Confirm or refute F21's suspicion about
the brawl-watch loop and record the answer. Re-check F20's numbering against the sibling branches
before adding anything new.

## Definition of done

The guard tests liveness with its `animationIsBusy` citation; the prune decoupled from ambient life
and proven inert; hermetic tests green and confirmed failing pre-change; every re-recorded fixture
enumerated and individually explained; all four suites green; `docs/BACKLOG.md` reconciled.

**Or:** a delta appeared that the analysis cannot explain, and the work stopped — which on this item
is a likelier outcome than on any previous one, and is the point of measuring first.
