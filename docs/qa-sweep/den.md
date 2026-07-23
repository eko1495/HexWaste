
# Den (loc 1501) QA sweep — 6/7 done (1 activation-only)

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

- **454** Lara's gang war — golden `quest-lara-war` lands the verified ACCEPT step only (0→1,
  quest goes ACTIVE; quests.txt row: display>=1, completed>=2). See below for the full ladder
  + why stage 2+ is not driven.

**REMAIN (1, deep multi-step — task #56):**
- **101** Sabotage Becky's still — FULLY REVERSE-ENGINEERED (task #56 has the full recipe), but
  driving it live is finicky. Chain: buy Rebecca's $5 drink → 445|0x20000 (persists cross-map);
  Frankie's whiskey node offers opt 173 "why costlier than Becky?" gated (445&0x20000)&&!(445&16)
  &&(101==0) → 101:=1; Rebecca still-reveal → 445|16; Frankie report → 101:=2 + $100 + "destroy
  it"; use explosive (pid 384/20/75) on Becky's still (diStill, denbus1 ELEV1 tile 17062) when
  101==2 → 101:=3 (complete, thresh>=3). LIVE SNAG: Frankie's "$20 a shot" node showed opts
  171/172/174 not 173 despite 445=0x20000 — a dialogue-graph disambiguation to resolve. Becky's
  still ≠ Bob's Klamath still (198). Item can be --give'd. ~45-60min to finish.

### 454 — Lara's gang war (session detail)

Traced the full stage ladder via `ProcAnalyze --quest-paths 454` + operand-level `int_disasm.py`
(node→value map, forward-only-guarded — a write only applies if the current value is lower):

- dcLara Node008 := 1 (accept the recon job — the REAL golden covers this step)
- dcLara Node016 := 2
- dcMetzge Node019 := 3 ("permission")
- dcLara Node023 := 4
- dcTyler Node020 := 5
- dcLara Node027 := 6
- dcLara Node989 := 7 / Node990 := 9 (alternate branches off Node030)
- DenBus1/DenBus2 map_update_p_proc := 8 (auto, fires once 454 is 6 or 7 — not a dialogue node)
- terminal := 10 (bad outcome) / := 11 (good outcome) via destroy_p_proc on dcTyler, dcMarc,
  DCG1Grd, dcG2Grd, or dcLara (any elevation), or their map_enter_p_proc / DenBus2
  map_exit_p_proc fallbacks — all forward-only guarded the same way.

**Ground-truth correction:** dcLara.int is NOT a unique "Lara" NPC script — it's a shared
"Tough Guard" template reused by several unnamed Den grunts (same script + same displayed
name at different tiles/maps: denbus1 21514, denbus2 19950). The 454 recon job lives on the
denbus1 21514 copy; talking to it (`1,1,1`) fires Node008 for real, 454 0→1, confirmed live
(no --set-global). Metzger's "join the guild" dialogue chain (option 3→2→1→1) is a DIFFERENT,
unrelated Den quest (a slave-run errand) — it does NOT touch 454 despite superficially matching
the "permission" framing; do not conflate the two.

**Stage 2 blocker (unresolved this session):** dcLara's Node018 (the 454==1 greeting) gates its
"report success" reply (which routes into Node011 → pays $200 → continues toward 454:=2) behind
`(GVAR445 & 0x20000000) != 0` (confirmed via reference/fallout2-ce `_op_giq_option`'s actual
pop order — iq threshold, then messageListId, then msg, then proc, then reaction — read
right-to-left against the push order). With that bit unset (the fresh-game default), the ONLY
option offered is the "Not yet." dead end (Node999, empty). None of the following set that bit
in this session: talking to Tyler (24534, both his 454=0 and 454=1 dialogue variants — pays XP,
says "just go in", door 23286/24335 open, but no 445 write found in his Node001-033);
talking to Marc (24538) or the DCG1Grd/dcG2Grd generic guards (21737/19544 etc.) — all give
empty dialogue (no REPLY at all, in every order tried); opening/using the graveyard crates
(diCrate — a generic reusable loot-crate script, no gvar445 capability at all); lockpicking the
church door (24335, fails at default Lockpick with the standard $CREATE stats); teleporting past
the door near dcStory2 (24885, a background/cutscene-flavor object, no 454/445 write). Whatever
sets that bit was not found by dialogue tree, NPC, object, or skill use in this pass — a future
session should grep dcTyler's 7056-instruction `map_enter_p_proc` and denbus2's
`map_update_p_proc`/`map_exit_p_proc` bodies for a `445` bitwise-or (not a `:=` — `--writes`
only catches literal assignments, not bit-sets, so a raw grep of the full disasm is needed) tied
to a `tile_distance`/`obj_can_see_obj` proximity check, or try the hostile "you'll regret that"
branch at Tyler (Node003ish) through to a resolved fight.

**Den NPC tiles (--map-objects):** DCMom denbus2 24479, DCSmitty denbus1 22137, dcRebecc
denbus1 17662, DCFranki denbus2 14716, DCAnna denbus1 28105, dcFred denbus1 25479, dcDerek
denbus2 29694, DCMetzge (free-vic).

**Item pids:** meal=468, locket=252, book(Lavender Flower)=471.

**PATTERN CONFIRMED (2nd town):** the "easy" quests (deliveries, item-returns, single-accept)
land in ~15-30 min each with the toolset; the remainder are per-quest investigations
(bitfield-gated turn-ins, cross-NPC knowledge chains, combat questlines) at ~30-60 min. No
shortcut to bulk fixtures — but every quest IS completable via the real path with the tools.

Related: [[klamath-qa-sweep]], [[p128-quest-path-finder]], [[p124-quest-census]].
