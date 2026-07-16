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

## Correctly rejected by the replay guard (driver said completed, recipe did NOT reproduce)

- **380** reddown (`GVAR_REDDING_*`) — driver end=3, replay 0->0. The value-branch tie-break
  advanced the gvar while EXPLORING a terminal option; the recorded picks don't reproduce it.
  (This is the same 380 the first census flagged as a batch false-positive — still not real.)
- **481** GVAR_NCR_BRAHMN_QST (redment) & **488** GVAR_V13_GORIS_QST (vault13) — real quests,
  but the completer writes the gvar on node-ENTRY with zero option picks, so the emitted recipe
  is a degenerate empty `--talk-seq <tile>` that does not replay. **Flagged for manual review**
  (need a valid pick sequence / first-talk trigger). Not forced into a golden.

## Interpretation of the stuck tail (183)

Dominated by New Reno (newr1a/2/2a/3/st/vb = ~76 stuck of 183) and the SF/NCR hubs — the
deep multi-level negotiation + prerequisite-gvar tier the driver correctly flags but cannot
auto-drive (docs/plan-quest-driver.md §10, task #63 bit-level). `stuck` = zero driver progress,
not "broken" — these are the manual-trace / bit-level backlog, now precisely enumerated.

Regenerate: `scripts/quest-harvest.sh all` (needs display + game data). Raw logs: ./.harvest-log/.
