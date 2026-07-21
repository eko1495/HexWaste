# Vault City cross-town quest QA — design spec (2026-07-21)

Session design for the next campaign quest-QA sweep increment: land the two Vault City
cross-town delivery quests as byte-stable goldens, probe the worldmap-recon quest for
feasibility, and reconcile the VC sweep docs.

## Context

The quest-golden suite is at 29 fixtures (`tests/golden-quest/`, driven by
`scripts/quest-golden.sh`). Vault City (loc 1504) has 4 quests landed (497 Lydia booze,
493 Valerie tools, 80 Smith plow, 459 rescue Joshua) and its easy single-map delivery
tier is exhausted. The remaining 5 split into:

- **321** — deliver Moore's briefcase to Bishop in New Reno (cross-town delivery)
- **89** — deliver Lynette's holodisk to Westin in NCR (cross-town delivery)
- **529** — scout 8 sectors around Gecko + enter NCR (worldmap recon, Stark)
- **82** — Gecko powerplant (multi-step epic; out of scope this session)
- **85** — jet sample to Dr. Troy (story-gated; parked pending B4 campaign-state fixtures)

Cross-town is harder for the *automated* driver (cross-map prereq resolution = backlog
B2), but a hand-built recipe simply chains `--goto-map` across towns — the same shape
the `quest-rescue-joshua` golden already uses across vctyctyd↔vctydwtn, and
`quest-smith-plow` uses across two NPCs with a gvar-gated cross-NPC option.

## Goal

1. Two new goldens: `quest-moore-briefcase` (gvar 321) and `quest-lynette-holodisk`
   (gvar 89) — or, for any that proves story-gated, a documented gate-finding instead.
2. A definitive feasibility verdict on 529, written into the sweep notes.
3. `docs/qa-sweep/vaultcity.md` and `docs/qa-sweep/README.md` reconciled with reality.

## Rules (unchanged from the existing sweep)

- Recipes drive the **real** script logic: dialogue VM + `set_global_var`. No
  `--set-global` faking of the quest gvar or its prerequisites. State/ID only (tiles,
  gvars, item pids, option indices); no copyrighted game dialogue text in docs or code.
- Fixed chargen via the standard `$CREATE` line + `--rng-seed 1` so option ordinals and
  outcomes are byte-stable.

## Method per quest (321, then 89)

1. **Trace.** `ProcAnalyze --quest-paths <gvar>` + `tools/int_disasm.py <script>
   --writes <gvar>` to find every writer proc and its gates on both ends (VC giver →
   destination completer). `ProcAnalyze --map-objects --map <name>` for NPC tiles on the
   VC map and the destination map (newr* / ncr*), and the briefcase/holodisk item pids.
2. **Drive.** Build the `--goto-map` chain interactively with the harness: accept at the
   VC NPC (gvar:=1; item granted by script or `--give`n), `--goto-map` to the
   destination town, `--talk-seq` the completer, `--get-global` checkpoints after each
   hop.
3. **Bank.** Add the recipe to `scripts/quest-golden.sh`, record the fixture under
   `tests/golden-quest/`, verify replay is byte-identical on a second run, update docs,
   and make one conventional commit per quest — so nothing is lost if a later quest
   stalls.

## 529 probe (timeboxed, investigate-only)

Determine how gvar 529's writers trigger — expected: worldmap sector-visit state read
by Stark's script. Then:

- If the existing travel/worldmap verbs can drive the sector visits and the NCR entry →
  land it as a third golden.
- If it needs a new harness verb or engine hook → write the exact mechanism and the
  missing verb into `vaultcity.md` and stop. **No new engine/harness code this session**
  beyond, at most, a trivial flag.

## Error handling (known failure modes from prior towns)

- **Gvar-gated cross-NPC options** (completer's option hidden until the giver's write):
  re-talk the giver to advance the gvar first, as in `quest-smith-plow`.
- **Story-prereq gate discovered mid-trace** (the 85 pattern): document the gate in
  `vaultcity.md`, mark the quest B4-tier, move on. The doc update is that quest's
  deliverable.
- **Option ordinals shifted by stats/IQ**: ordinals from `--quest-paths` are static
  guides; confirm at runtime with the fixed `$CREATE` character.
- **Destination-map ambiguity** (New Reno spans newr1–newr4; NCR similar): locate Bishop
  / Westin with `--map-objects` across the candidate maps before driving.

## Docs

- `docs/qa-sweep/vaultcity.md`: fix the stale "3/10" header (459 is landed), move landed
  quests to DONE with their recipes/tiles/pids, record the 529 verdict.
- `docs/qa-sweep/README.md`: update the status table and golden count.

## Success criteria

- 2 new byte-stable goldens committed (or documented gate-findings for any
  story-blocked), each replay-verified twice.
- A definitive 529 verdict on record.
- Docs reconciled; full golden suite still green.

## Out of scope

- 82 (Gecko powerplant) and 85 (Dr. Troy jet) — parked.
- Driver automation for cross-map chains (backlog B2) — these are hand-built recipes.
- Any new harness verbs or engine changes (except the trivial-flag allowance under the
  529 probe).
