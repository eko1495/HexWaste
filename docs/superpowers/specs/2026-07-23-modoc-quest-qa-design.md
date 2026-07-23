# Modoc quest QA sweep — design spec (2026-07-23)

Next campaign quest-QA increment: Modoc (loc 1503). Same rules and workflow as the VC
cross-town session (spec `2026-07-21-vc-crosstown-quest-qa-design.md`).

## Current state (verified 2026-07-23)

Modoc is **2/6 done** — `quest-modoc-watch` (106) and `quest-modoc-ghostfarm` (631, landed
via the P137 batch harvest after the town notes were written; `docs/qa-sweep/modoc.md` is
stale at "1/6"). Static census results for the remainder:

- **110** (Farrel's garden rats): display>=4, completed>=7. Activation exists — `mcFarrel`
  Node001 opt7(static) → Node994 := 4. Completers Node021/Node029 (opt5/opt6 static) write
  :=3 or :=8 state-dependent; the rat-death accounting that separates them is untraced (the
  earlier sweep killed all modgard rats with no gvar movement — likely because 110 was
  never activated to 4 first).
- **693** (Jonny → Slag caves): display>=1, completed>=2; 15 writes across `mcBaltha`,
  `mcVegeir`, `mcJonny` with several completing routes (values 2/3/4); likely chains off
  the 631 ghost-farm state; may need the escort-sim.
- **108** (tell Karl it's OK): **writes=0** — one of the three P124-pinned vanilla content
  gaps. No golden possible by design; needs a documented verdict.
- **105** (Cornelius's watch): same quest as 106 (parked verdict stands: finicky IQ-shifted
  ordinals, low marginal value).

## Goal

1. Golden `quest-modoc-rats` (110) — trace the rat-death mechanism, drive
   activate → exterminate → report. Or a documented gate-finding if it proves unreachable.
2. Golden `quest-jonny-rescue` (693) — or a documented gate-finding (the VC-89 pattern).
3. `modoc.md`: 108 recorded as the verified writes=0 vanilla gap (cross-ref P124).
4. Optional, only if 110+693 land with session time left: ONE timeboxed 105 retry using the
   driver's auto mode (`--talk-seq <tile> -`), not hand-ordinals. No-result = stays parked.
5. Docs reconciled: `modoc.md` to truth (2/6 + outcomes), README table/counts.

## Rules (unchanged)

Real script paths only (no `--set-global` faking of quest gvars or prereqs; `--give` is the
sanctioned item-acquire shortcut); no copyrighted dialogue text in committed files
(state/ID only); standard `$CREATE` + `--rng-seed 1`; one conventional commit per landed
unit; suite must end ALL PASS with only intended fixtures changed.

## Error handling

Same playbook as VC: prereq gates found mid-trace get documented (that doc IS the
deliverable for that quest); static ordinals are guides only; completers may hide behind
same-map vetting NPCs or non-dialog triggers (`mcBaltha` Node026) — trace before concluding
unreachable.

## Success criteria

110 and 693 each end as a byte-stable golden or a precise documented gate; 108 verdict on
record; docs consistent; suite green.
