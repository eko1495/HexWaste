# Den closeout QA — design spec (2026-07-23)

Close the Den (loc 1501) at 7/7: land the two remaining quests as byte-stable goldens (or
precise documented gates). Same rules/workflow as the VC (2026-07-21) and Modoc
(2026-07-23) sessions.

## Current state

Den is 5/7 (100 free-vic, 550 smitty-carpart, 551 anna-locket, 450 mom-meal, 371
fred-money). Remaining, both pre-investigated in `docs/qa-sweep/den.md`:

- **101 (sabotage Becky's still)** — display>=2, completed>=3. Chain fully
  reverse-engineered: Rebecca $5 drink → gvar445|=0x20000 → Frankie whiskey-price branch
  (gated `(445&0x20000) && !(445&16) && 101==0`) → 101:=1 → Rebecca still-reveal → 445|=16
  → Frankie report → 101:=2 + task → use explosive (pid 384/20/75) on diStill (denbus1
  ELEV1 tile 17062, `use_obj_on_p_proc`/`damage_p_proc` := 3 at 101==2); Frankie
  post-destruction nodes write :=4. KNOWN SNAG: on the live drive Frankie's price node
  offered opts 171/172/174 instead of the gated 173 despite 445=0x20000 — needs operand-level
  disambiguation of his option builder (which flag/state was actually missing).
- **454 (Lara's gang war)** — display>=1, completed>=2, 54 writes. Stages: church intel →
  Metzger permission (dcMetzge Node019 → 454:=3) → scout → the fight; completion :=11 fires
  non-dialog via destroy_p_proc on the gang (dcG2Grd/DCG1Grd/dcTyler) or dcLara, and
  map_enter/exit fallbacks. `--kill` fires real destroy_p_proc, so the fight is drivable
  once the stage gvar is honestly advanced through dialogue.

## Goal

1. Golden `quest-becky-still` (101) — resolve the Frankie gate snag, drive the full chain.
2. Golden `quest-lara-war` (454) — drive the stage dialogue, settle the fight with `--kill`.
3. Docs: `den.md` to 7/7 truth (+ README row/counts). Gate-findings replace goldens only if
   a stage proves genuinely unreachable (the VC-89 pattern) — not expected given the traces.

## Rules

Unchanged: real script paths only; no `--set-global` on quest gvars OR prereqs (445's bits
must come from the real Rebecca/Frankie dialogue); `--give` sanctioned for items/caps;
no dialogue text (incl. close paraphrase) in committed files or reports; `$CREATE` +
`--rng-seed 1`; one conventional commit per landed unit; record-mode git-status guard.

## Success criteria

Both quests golden (or precisely documented), suite ALL PASS at 35 (or 33+landed count),
Den row = complete, no doc contradictions.
