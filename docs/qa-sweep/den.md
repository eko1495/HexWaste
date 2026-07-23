
# Den (loc 1501) QA sweep — 6/7 done

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

- **454** Lara's gang war — **DONE, golden `quest-lara-war`, FULL 0→2→3→4→5→9→11.** All four
  quests.txt rows (display 1/2/4/6, each completed at +1) land at val=11. See below for the
  full ladder + the key that unblocks it.

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

- dcLara Node008 := 1 (accept the recon job)
- dcLara Node016 := 2
- dcMetzge Node019 := 3 (permission)
- dcLara Node023 := 4
- dcTyler Node020 := 5
- dcLara Node027 := 6
- dcLara Node989 := 7 / Node990 := 9 (alternate branches off Node030 — the golden lands on 9)
- DenBus1/DenBus2 map_update_p_proc := 8 (auto, fires once 454 is 6 or 7 — not reached on the
  9-branch, so not exercised by this golden)
- terminal := 10 (bad outcome) / := 11 (good outcome, reached by this golden) via
  destroy_p_proc on dcTyler, dcMarc, DCG1Grd, dcG2Grd, or dcLara (any elevation), or their
  map_enter_p_proc / DenBus2 map_exit_p_proc fallbacks — all forward-only guarded the same way.
  On the 9-branch, the map_enter_p_proc fallback resolves the war off-screen (no `--kill`
  needed) once you re-enter denbus2 and let a `--pump-ms` tick run.

**Ground-truth correction:** Lara is a real, named NPC (confirmed via her own `dcLara.msg`
dialogue text — her introduction line names her directly); "Tough Guard" is just the generic
label the harness prints before she's been formally met, not a shared template as a prior pass
of this session assumed. The 454 recon job is offered by the denbus1 21514 copy of her object.
Metzger's guild-membership dialogue chain (a different, unrelated Den errand quest) does not
touch 454 despite superficially matching the "permission" framing — the real permission chain
is a separate option that only appears on Metzger's greeting once 454>=2 (see below).

**The unlock (found via `ProcAnalyze --bit-scan 445`):** dcLara's Node018 (the 454==1 greeting)
gates its report-success branch (→ Node011 → low-IQ sub-branch → Node015 → Node016 :=2) on
`GVAR445 & 0x20000000`. `--bit-scan 445` reports that mask's setter directly: **diCrate.int's
`use_p_proc`** (any denbus2 graveyard crate, e.g. tile 21731) does the classic
read-modify-write — gated on the bit being clear, it grants a one-time +500xp discovery bonus
and sets the bit. A prior pass of this session wrongly concluded diCrate "has no bitwise-or
capability" from `int_analyze.py`'s opcode-union summary alone; the full `int_disasm.py` dump
of `use_p_proc` shows the `get_global_var`/`bitwise_or`/`set_global_var` triple plainly (offsets
~0x0930-0x0946). Using a crate is exactly the in-fiction "find out what's guarded" recon step;
doing it before the first Lara conversation even short-circuits her greeting to an
already-know branch on the very first visit, skipping a return trip.

**Full driven route** (state/IDs only — see `scripts/quest-golden.sh` for the exact command):
open a denbus2 graveyard crate (21731) → talk to the denbus1 21514 guard (accept + the
already-know report chain, her Node006/Node011/Node015/Node016 nodes) → 454:=2 → Metzger
(denbus2 15278) has a new greeting option once 454>=2 (his permission chain) → 454:=3 → back to
the 21514 guard (his follow-up chain) → 454:=4 → dcTyler (denbus2 24534) has a new greeting
once 454>=4 (his own chain) → 454:=5 → back to the 21514 guard again (his report chain, then
his accept-the-plan option) → 454:=6, then immediately → 454:=9 (Node030's opt0/Node990 branch
fires with no further choice) → re-enter denbus2 and `--pump-ms` → the map_enter_p_proc
completion fallback fires → 454:=11. Verified deterministic across repeat runs with
`--rng-seed 1` (byte-identical `--get-global`/`quest-item`/`quest-probe` output both times).

**Den NPC tiles (--map-objects):** DCMom denbus2 24479, DCSmitty denbus1 22137, dcRebecc
denbus1 17662, DCFranki denbus2 14716, DCAnna denbus1 28105, dcFred denbus1 25479, dcDerek
denbus2 29694, DCMetzge (free-vic).

**Item pids:** meal=468, locket=252, book(Lavender Flower)=471.

**PATTERN CONFIRMED (2nd town):** the "easy" quests (deliveries, item-returns, single-accept)
land in ~15-30 min each with the toolset; the remainder are per-quest investigations
(bitfield-gated turn-ins, cross-NPC knowledge chains, combat questlines) at ~30-60 min. No
shortcut to bulk fixtures — but every quest IS completable via the real path with the tools.

Related: [[klamath-qa-sweep]], [[p128-quest-path-finder]], [[p124-quest-census]].
