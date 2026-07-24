# B4 track: the Gecko-powerplant / VC-citizenship arc — design spec (2026-07-24)

Open the B4 campaign-state track by driving the arc that gates Vault City's remaining
quests — NOT by building a fixture-seeding mechanism. The 2026-07-24 static sweep proves
the whole chain is reachable through real script logic from a cold boot, so the campaign
state is simply a (long) recipe prefix, exactly like every landed golden.

## The arc (grounded 2026-07-24, sweep of all vc/vi/gc/gs scripts)

- **82 (Gecko powerplant)**: display>=2, completed>=8. Ladder: VCLynett Node047f/049a/b
  `:=3` → GCHarold Node048 / GCGordon Node912/936 / GCBrain Node030 `:=5` → VCMClure
  Node045/045a `:=6` → VCRandal Node024 `:=7` → plant repair: GSValve `repair_it` /
  GeckPwpl `map_exit_p_proc` `:=8`/`:=9`, GCFestus `:=9`; optimization tier: GCFestus
  Node992 `:=12`, VCMClure Node059a / VICenCom Node032 `:=13`, GsTerm `use_obj_on` `:=15`.
- **88 (the 89-gate)**: writers are ONLY VCLynett Node114 `:=5`, Node116 `:=6`, Node130
  `:=7`, and VCRandal Node026 `:=8`. The prior "stages 1-4 set elsewhere" hypothesis is
  dead — 88 jumps straight to 5 at Lynette (presumably her powerplant-outcome reaction).
- **79 (citizenship, the 529-gate)**: ladder vcskeeve Node024 `:=1` → vcgatgrd `:=2`/`:=3`
  → day-pass tier `:=4` (VCLynett Node077/076a/b, vcmclure Node046, vcgreg Node030, VCChet
  Node001c) → **`:=5` via VCLynett Node132 or VCChet Node001b** → `:=6` = hostile
  (damage_p_proc town-wide; avoid).

## Goal (in dependency order)

1. **Golden `quest-gecko-powerplant` (82)** — drive at least the repair completion (>=8);
   prefer the optimization tier (>=13 via McClure/VICenCom) if tractable, since Lynette's
   reaction likely keys off the outcome tier.
2. **Extend to the citizenship/reaction grants**: the same or a follow-on recipe drives
   Lynette 88:=5 and 79 to 5 (Node132 route preferred; Chet the fallback — his placement
   was previously unfound, now his script demonstrably writes 79, so re-probe at runtime).
3. **Golden `quest-lynette-holodisk` (89)** — with 88==5 + Bishop's holodisk (pid 447,
   --give sanctioned): Westin (ncr3 17892) getDisk → 89:=3 (+ Lynette :=4 if reachable).
4. **Golden `quest-stark-scout` (529)** — now that 79==5 is reachable, wire the ~8-line
   metarule3 rule-105 (WM_SUBTILE_STATE) IntVm hook per the P138 probe's preserved sketch
   (outcome-2 of that probe's verdict: land flag + golden together, never speculatively),
   then drive Stark's 8-subtile scout + NCR entry.
5. **85 (Dr Troy jet) — timeboxed probe only**: with the arc state present, re-check
   Troy's gate; land if it opens, document precisely if not.
6. **Docs**: vaultcity.md (82/89/529/85 outcomes; the B4-arc section), gecko.md (82 is
   Gecko's centerpiece), README rows/counts.

## Rules (unchanged)

Real script paths only; no --set-global on quest gvars or prereqs (all of 82/88/79 must
move via real dialogue/object use; --give sanctioned for items/caps; real chem use
sanctioned); no dialogue text incl. close paraphrase in committed files/reports; $CREATE +
--rng-seed 1; one conventional commit per landed unit; record-mode git-status guard.
Engine code: ONLY the rule-105 hook (Task 4), matching the preserved sketch's shape, with
the 529 golden as its test; nothing else.

## Risks

Long recipes (the 82 chain crosses VC↔Gecko repeatedly) — keep checkpoints dense;
per-NPC gates may hide IQ/stat gates (Mentats precedent) or order-dependence (Modoc-105
precedent); Lynette's audience may need the day-pass tier first (vcgatgrd :=2/3) — trace
her Node077/132 gates before driving.

## Success criteria

82 + 89 goldens landed (529 landed with its hook, or its precise blocker documented);
85 resolved either way; suite ALL PASS at the new count; docs coherent.
