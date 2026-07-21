# Campaign quest-QA sweep — town notes

Per-town notes for the campaign quest-QA arc: the mechanics and exact harness recipes
for driving each original-game quest to completion through the real script logic (dialogue
VM + `set_global_var`), captured as byte-stable goldens in
[`scripts/quest-golden.sh`](../../scripts/quest-golden.sh) (fixtures under
[`tests/golden-quest/`](../../tests/golden-quest/)).

Every recipe drives the **real** path — no `--set-global` faking of a quest's gvar or its
prerequisites. State/ID only (tiles, gvars, item pids, option indices); no copyrighted
game dialogue text is reproduced.

## Status (at time of writing)

| Town | File | Landed |
| --- | --- | --- |
| Klamath (loc 1502) | [klamath.md](klamath.md) | 6/6 ✅ complete |
| The Den (loc 1501) | [den.md](den.md) | 5/7 |
| Modoc (loc 1503) | [modoc.md](modoc.md) | 1/6 |
| Vault City (loc 1504) | [vaultcity.md](vaultcity.md) | 5/10 |
| Gecko (loc 1505) | [gecko.md](gecko.md) | surveyed (no clean quick-win) |

30 quest goldens total (plus the pre-existing opening/combat/encounter suites).

## How to use these

Each note lists, per quest: the completing `gvar`, the NPC tiles (from
`ProcAnalyze --map-objects`), the item pids, and the exact `--talk-seq` / `--give` /
`--kill` / escort-sim sequence that drives it. The "REMAIN" sections scope the unlanded
quests with their known blockers, so each resumes without re-investigation.

### The tooling the recipes rely on

- `ProcAnalyze --map-objects --map <name>` — every scripted object as `elev/tile/pid/script`.
- `ProcAnalyze --quest-paths [gvar]` — the static dialog/trigger route to a quest's completion.
- `tools/int_disasm.py <script> --writes <gvar>` — which node sets a gvar to which value;
  `<script> <proc>` for operand-level gates (item pids, thresholds).
- Harness verbs: `--talk-seq`, `--give`, `--kill`, `--use-on`, `--set-hour` (night NPCs),
  `--teleport` + `--escort-pump` (escort-sim), `--critters` (runtime critter dump),
  `--pump-ms` (timed events / scripted map transitions).

### The reliable "delivery" pattern

Most quick-win quests share one shape: navigate the NPC's chat to the "I'll get it for you"
accept (`gvar:=1`) → `--give <item pids>` → re-talk; the greeting/info menu gains a "here's
your delivery" option gated on `obj_carrying` → `gvar:=complete`. See `mom-meal` (Den),
`modoc-watch`, `lydia-booze` / `valerie-tools` (Vault City).

> Note: `[[double-bracket]]` references in these notes point at the assistant's working
> memory and don't resolve here; they're left as breadcrumbs to related investigations.
