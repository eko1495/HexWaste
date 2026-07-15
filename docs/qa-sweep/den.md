
# Den (loc 1501) QA sweep — 5/7 done

The second campaign-QA town (after [[klamath-qa-sweep]] closed 6/6). Same toolset
(--map-objects, int_disasm.py, --critters, the round-nav aid, escort-sim, set-hour).

**DONE (4/7):**
- **100** Free Vic — `quest-free-vic` (pre-existing).
- **550** Get car part for Smitty — `quest-smitty-carpart` (activation, pre-existing).
- **551** Return Anna's locket — `quest-anna-locket` (--set-hour night ghost + item 252).
- **450** Deliver a meal to Smitty for Mom — `quest-mom-meal` (commit c45b084). CLEAN 0→1→3:
  accept from Mom (denbus2 24479, `2,1,1`) → deliver to Smitty (denbus1 22137, `2,1` → Node008
  → 450:=3). Two-NPC delivery, no item shortcut needed (Mom hands the meal on accept).

- **371** Collect money from Fred — **DONE golden `quest-fred-money` (23cf96f), 0→1→2.** Rebecca
  (denbus1 17662, work `2,1,1`) sets the Fred task; Fred (denbus1 25479, `1,2,2,1,1,2,3` = demand
  the FULL amount, NOT the $100 partial) pays $200 via his Node986 + sets the 446 bit; Rebecca
  turn-in `2,1,1,1` — the "Yes, I did" option appears ONLY with caps>=200 AND the 446 bit → 371:=2.
  KEY: plain --give of caps/book does NOT complete (needs the real negotiation to set the bit); the
  demand-full branch (not accept-partial) is what pays 200. Book sub-task (Derek, desc 205) still
  open — 371 is a shared gvar (two display thresholds), 205 could be a later fixture via item 471.

**REMAIN (2, both deep multi-step — tasks #56/#57):**
- **101** Sabotage Becky's still — FULLY REVERSE-ENGINEERED (task #56 has the full recipe), but
  driving it live is finicky. Chain: buy Rebecca's $5 drink → 445|0x20000 (persists cross-map);
  Frankie's whiskey node offers opt 173 "why costlier than Becky?" gated (445&0x20000)&&!(445&16)
  &&(101==0) → 101:=1; Rebecca still-reveal → 445|16; Frankie report → 101:=2 + $100 + "destroy
  it"; use explosive (pid 384/20/75) on Becky's still (diStill, denbus1 ELEV1 tile 17062) when
  101==2 → 101:=3 (complete, thresh>=3). LIVE SNAG: Frankie's "$20 a shot" node showed opts
  171/172/174 not 173 despite 445=0x20000 — a dialogue-graph disambiguation to resolve. Becky's
  still ≠ Bob's Klamath still (198). Item can be --give'd. ~45-60min to finish.
- **454** Lara's gang war (dcLara). Multi-stage (display 1/2/4/6→complete 7): church intel →
  Metzger permission (dcMetzge Node019 → 454:=3) → scout → the gang-war combat. Completion
  454:=11 via dcG2Grd/DCG1Grd/dcLara destroy_p_proc or church map_enter (combat outcome). The
  big Den combat questline — its own session; needs the multi-stage setup + the fight (--kill).

**Den NPC tiles (--map-objects):** DCMom denbus2 24479, DCSmitty denbus1 22137, dcRebecc
denbus1 17662, DCFranki denbus2 14716, DCAnna denbus1 28105, dcFred denbus1 25479, dcDerek
denbus2 29694, DCMetzge (free-vic).

**Item pids:** meal=468, locket=252, book(Lavender Flower)=471.

**PATTERN CONFIRMED (2nd town):** the "easy" quests (deliveries, item-returns, single-accept)
land in ~15-30 min each with the toolset; the remainder are per-quest investigations
(bitfield-gated turn-ins, cross-NPC knowledge chains, combat questlines) at ~30-60 min. No
shortcut to bulk fixtures — but every quest IS completable via the real path with the tools.

Related: [[klamath-qa-sweep]], [[p128-quest-path-finder]], [[p124-quest-census]].
