# Full-map quest-driver harvest

Output of `scripts/quest-harvest.sh all` — the quest-driver run across **all 155 maps** in
the archive (the first census covered only 22 town hubs), with every candidate completion
**recipe-replay-verified** in a clean process (the driver's own `completed=` can be a
false positive when value-branch tie-breaking mutates gvar state mid-exploration).

## Aggregate

```
155 maps swept · 56 quest-bearing · 233 (map,quest) driver runs
batch classification: completed=20  activated=30  stuck=183
NEW recipe-verified completions (not already goldens): 4 quests
```

## New goldens harvested (recipe-replay-verified, added to scripts/quest-golden.sh)

| gvar | name | map(s) | 0-> | recipe tiles |
|------|------|--------|-----|--------------|
| 195 | GVAR_NCR_VORTIS_QUEST_STATE | ncrent | 2 | 10518 |
| 332 | GVAR_REDDING_EXCAVATOR_CHIP | redment | 3 | 15875,16306 |
| 485 | GVAR_NCR_ENLONE_LETTER_QST | sfelronb | 2 | 15469 |
| 367 | GVAR_SAN_FRAN_SPLEEN | sftanker (+dnslvrun) | 9 | 23085 |

## B3 KILL-wins (P138 — the driver's destroy_p_proc kill pass, replay-verified)

The kill pass auto-found these across all 155 maps; each `--kill` recipe replays 0→completed
(the completer's `destroy_p_proc` fires the gvar). Three goldened as unambiguous kill-completions:

| gvar | name | map | 0-> | recipe | goldened |
|------|------|-----|-----|--------|----------|
| 474 | GVAR_V15_KILL_DARION | vault15 | 2 | `--kill 23883` | ✓ quest-kill-darion |
| 486 | GVAR_NCR_KILL_ELRON_QST | ncr1 | 2 | `--kill 22886` | ✓ quest-kill-elron |
| 554 | GVAR_NAVARRO_XARN | navarro | 2 | `--kill 22900` | ✓ quest-kill-xarn |
| 100 | GVAR_QUEST_VIC_DEVICE | denbus2 | 2 | `--kill 15278` | ✓ quest-kill-metzger |
| 292 | GVAR_REDDING_WHORE_CUT | redment (+cowbomb/rndexcow) | 4 | `--kill 16324` | held — semantics |
| 217 | GVAR_NCR_MIRA_STATE | ncr1 | 5 | `--kill 14866` | held — a "state" gvar |

**292/217 held for review** (task #67): both replay-verify as real quest completions, but the
gvar names (`WHORE_CUT`, `MIRA_STATE`) suggest a possible fail/side-effect path rather than an
intended kill objective — unlike the explicit `KILL_DARION`/`KILL_ELRON`. Escort-death
completions (102 Torr, 197 Smiley) are deliberately NOT harvested (killing the escortee = failure).

## Correctly rejected by the replay guard (driver said completed, recipe did NOT reproduce)

- **380** reddown (`GVAR_REDDING_*`) — driver end=3, replay 0->0. The value-branch tie-break
  advanced the gvar while EXPLORING a terminal option; the recorded picks don't reproduce it.
  (This is the same 380 the first census flagged as a batch false-positive — still not real.)
- **481** GVAR_NCR_BRAHMN_QST (redment) & **488** GVAR_V13_GORIS_QST (vault13) — real quests
  whose completer writes via `talk_p_proc` (on dialogue OPEN, hence zero picks). **RESOLVED**
  (plan §11a): both are prerequisite-gated — 488 fires only inside the Goris party-join branch
  when `global(488)==1` (needs the V13 deathclaw storyline); 481's Node006 is reached only past
  a reputation / prior brahmin-drive gate. Neither completes from a clean start, and faking the
  prerequisite is forbidden → no golden; they belong to the future campaign-state fixture track.
  Emitter hardened to emit the `-` (zero-pick) sentinel so such recipes are well-formed CLI.

## Interpretation of the stuck tail (183)

Dominated by New Reno (newr1a/2/2a/3/st/vb = ~76 stuck of 183) and the SF/NCR hubs — the
deep multi-level negotiation + prerequisite-gvar tier the driver correctly flags but cannot
auto-drive (docs/plan-quest-driver.md §10, task #63 bit-level). `stuck` = zero driver progress,
not "broken" — these are the manual-trace / bit-level backlog, now precisely enumerated.

Regenerate: `scripts/quest-harvest.sh all` (needs display + game data). Raw logs: ./.harvest-log/.
