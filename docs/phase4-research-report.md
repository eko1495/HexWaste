# Phase 4 Research Report — After "The World Becomes Real"

*Researched 2026-06-12 in-repo: engine claims carry file:line citations from `reference/fallout2-ce/src`; script-VM numbers come from byte-exact disassembly + call-graph closure of 12+ real game scripts (`tools/int_analyze.py`); web findings carry sources and unverified flags.*

## TL;DR

- **Recommended path: "The world responds" — make the scripts real.** VM foundations → text dialog → locked doors/lockpick → script-stocked containers + loot UI → clock + save/load → renderer polish. Every milestone builds on the micro-VM investment, and the empirical numbers say it's all cheap-to-medium now.
- **Dialog is the steal of the phase: verdict S (~1 day for the text loop).** Options bind by *procedure index* (verified across ~120 option sites, zero exotics); the conversation reduces to a host loop with no VM re-entrancy. DarkFO's documented dialog nightmare (blocking `gsay` execution) does not apply to a bytecode VM that can simply re-run procedures.
- **The `use_p_proc`/`map_enter` cut line ("locked doors + lockpick + script-stocked containers work") costs ~1 week**: ~20 externals become real (≈12 are 1–5 lines), two skipped MAP-parser fields get kept, ~150–300 LOC of host dispatch. **Zero VM core changes** — all 39 measured core opcodes are already implemented.
- **Combat is no longer "too big" — measured at M** (~1,900 new LOC; ~1,400 with aggressive cuts), same ballpark as the lighting port. It stays out of phase 4 (scope, not feasibility) and becomes the headline phase-5 candidate.
- **One genuinely dangerous stub found**: synthesized roll results of 0 mean ROLL_CRITICAL_FAILURE — `critical(0)==1` fires jam-lock/trap-explosion branches. Pure-function rolls (half a day) are mandatory before any `use_p_proc` runs.

## Key findings per direction

### 1. VM expansion to use_p_proc + map_enter — measured, cheap, one trap

Census over 9 scripts (4 doors, 3 containers, 2 maps), true call-graph closures:
- `use_p_proc` closure union **29 externals**, `map_enter` **43**, combined **58**, whole-script safe number **70 of 181 registered**. All 39 core opcodes already implemented — no interpreter work.
- **LVAR mechanism confirmed and already half-built**: `get/set_local_var(v)` = `mapLocalVars[script->localVarsOffset + v]` (scripts.cc:2808, map.cc:437-466) — the array is our `MapFile.LocalVariables`; we only discard `localVarsOffset` in `ReadScripts` (MapFile.cs:227, inside a Skip) and need the `# local_vars=` field from scripts.lst. MVARs = our `MapFile.GlobalVariables`. GVARs need a tiny `vault13.gam` parser (deferrable).
- **World mutation** maps onto existing host structures (sorted object lists, blocking rebuild, `ToggleDoor`, parsed inventories): create_object / destroy_object / move_to / add_obj_to_inven / set_obj_visibility are all S–M each. Lock state lives in `openFlags` we currently skip (MapFile.cs:344).
- **Call protocol** (scripts.cc:1261-1342): per-exec context = self, source (user), target (defaults to self), dude, fixedParam (map_enter: first-run flag), actionBeingUsed (lockpick = SKILL 9), and a per-run scriptOverrides reset. Engine clears source after return; caller falls back to default behavior when not overridden — which matches our existing hardcoded door logic as the fallback.
- **Risks**: (1) the `critical(0)` trap — fix with pure-function rolls (`roll_vs_skill`→always 2/success for PoC); (2) null object handles must no-op like the engine (interpreter_extra.cc:853) — DenBus1's map_enter calls `move_to(party_member_obj(...))` in loops; (3) `while` loops on stubbed values: **empirically absent** in the sample (all 70 while-sites are constant-bounded counters); (4) pristine-map reloads restock containers each visit — acceptable, documented.

### 2. Dialog (text-only) — S, the best payoff/effort in the table

- Three real Den NPCs measured (Sheila 18 nodes, Tubby the shopkeeper 20, Smitty the quest NPC 31): dialog family is exactly `start_gdialog, gsay_start, gsay_reply, giq_option, gsay_end, end_dialogue` (+barter only for Tubby). `gsay_option`/`gsay_message`/`dialogue_reaction`: **zero occurrences** — SSL macros compile to `giq_option` exclusively.
- **Mechanics** (game_dialog.cc:1856-2089): gsay_reply sets state and clears options; giq_option appends `{msgList, msgId, procIndex, reaction}` after an IQ filter; gsay_end loops show-reply→pick→run-proc until a proc registers zero options. Host loop: run `talk_p_proc` once, then loop `RunProcedureByIndex(picked)` — node procs never call gsay_end themselves (frequency exactly 1 per script).
- Cost: ~8 externals (~150 LOC of DialogState), `RunProcedureByIndex` (the by-name path already does the work), dictionary L/GVARs, ~100 LOC choice loop + a text panel (the original renders numbered options in a plain text window — a panel with AAF text is honest; `alltlk.frm`/`di_talk.frm` are optional chrome, and Den NPCs have no talking heads anyway).
- **Must be real, not stubbed**: the `giq_option` IQ threshold (10 lines, fixed IQ 5) — otherwise smart and dumb lines show simultaneously. Riskiest unknown: mid-dialog mode switches (barter, combat) — stub to a message.
- Precedent: DarkFO had dialogs working early; its one big pain (blocking execution in transpiled JS) is structurally absent here.

### 3. Inventory & loot — list ops + a panel; equipping is nearly free

- Take/take-all/drop are inventory-list manipulation with a stubbable weight check (item.cc:313-316). Loot window fully mapped (intrface FRM 114, slots/buttons documented at inventory.cc:111-143, 1362-1383) — original art is feasible with our FRM pipeline; a minimal custom panel is less work (M).
- Item icons = proto `inventoryFid` (guard -1). Ground items already render through the normal object path; pickup = `_obj_pickup` flow we've already studied.
- **Equipping is LOW cost**: armor swaps the critter base FID via armor proto `maleFid/femaleFid` (inventory.cc:3287-3301); weapons swap the FID animation-code nibble (3313-3320) — both are recompositions our art-code port already supports. Visible armor change = big demo moment.

### 4. Game time + save/load — small, and one myth dispelled

- Clock: 10 ticks/s; time advances ONLY on worldmap travel/combat/skill use — never per frame (scripts.h:17-24, worldmap.cc:4190). `game_time_hour` returns hhmm (scripts.cc:332).
- **Day/night ambient is NOT engine-automatic** — maps load at LIGHT_INTENSITY_MAX and scripts call `set_light_level` (map.cc:927); there is no canonical hour→light curve. We define our own curve (and optionally honor `set_light_level` from map_enter, which the VM cut line gives us for free).
- Save/load: original = SAVE.DAT with 27 sequential binary handlers + per-map `.SAV` = **gzip-compressed MAP-format snapshots** (community-confirmed). PoC verdict: **JSON delta over pristine maps** (~6–8 h): position/map, opened doors, lock states, taken/moved objects, LVAR/MVAR/GVARs, clock.

### 5. Renderer polish — everything CPU-feasible, no shaders

- **Per-vertex floor lighting needs NO custom shader**: stock `BasicEffect` with `VertexColorEnabled` multiplies texture × vertex color (confirmed canonical on DesktopGL, mid-2026). Replace the floor SpriteBatch path with a quad batch sampling the 10 tile.cc light vertices — M.
- Egg transparency: S (egg.frm mask → per-frame alpha blend of wall sprites in the egg region; object.cc:2815-2844 semantics). Hover outlines: S (edge-detect the index data we keep, cache outline variants). Roof fade-instead-of-toggle: S, big polish. Scroll clamps: S. Faithful TRANS_* blend LUTs: M (alpha+tint approximation: S, ~5% off).

### 6. Combat — honestly re-measured: M, not XL

- Minimal (player + hostiles, one attack mode, vanilla to-hit/damage without perks/criticals, approach-and-attack AI): **~1,900 new LOC** reusing ~1,500 (pathfinding, animation registration, art codes, proto data). The original's 18k+ combat lines are mostly UI/perks/criticals/AI we skip. Critter stats block (344 bytes in PRO) is parseable with our existing reader.
- Biggest cost driver: sequencing the turn loop against the animation queue — engineering, not research. With cuts (fixed 60% hit, 1 AP attacks): ~1,400 LOC.
- Verdict: **feasible at M; excluded from phase 4 on scope, queued as the phase-5 headline.**

### Ecosystem / cross-cutting (web)

- fallout2-ce dormant (last commit Feb 2025); **Gecko** map editor very active (commits this week) — now the best-maintained second reference for MAP-format edge cases. vault13 (Rust) mildly alive. MonoGame 3.8.4 stable; VK/DX12 still preview; DesktopGL remains the right target.
- UI infrastructure: directions 2+3+5 share one minimal panel layer (rects + AAF text + FRM icons + numbered choices) — build it once in M1 and reuse.
- Unverified: NMA/Fandom page internals (403-blocked, from excerpts); Falltergeist dialog internals beyond README/issues.

## Comparison table

| Direction | Effort | Payoff | Risk | Fun | Verdict |
|---|---|---|---|---|---|
| VM foundations (rolls, context, LVARs) | S | Enabler for everything | Low (trap known) | Medium | **M0** |
| Text dialog | S (+panel) | Very high — talking NPCs | Low (barter stub) | Very high | **M1** |
| Doors/lockpick + map_enter | M | High — world obeys scripts | Medium | High | **M2** |
| Containers + loot/inventory UI | M | High — loot loop | Low | High | **M3** |
| Clock + JSON save/load | S–M | Medium-high — persistence | Low | Medium | **M4** |
| Renderer polish (S items) | S each | Medium — looks "finished" | Minimal | Medium | **M5** |
| Per-vertex floors / TRANS LUTs | M | Medium | Low | Medium | M5-optional |
| Minimal combat | M | Very high | Medium (sequencing) | Very high | **Phase 5** |
| Original save format | L | Low for PoC | High | Low | Dropped (JSON wins) |

## Recommended roadmap — "The world responds"

**M0 — VM foundations.** Pure functions real (`random`, `game_ticks`, `success`/`critical` 4-case maps, `roll_vs_skill`→2, `metarule` 14/22/30, constant clock externals); script-context protocol (object handle table; source/target/dude/fixed_param/action_being_used; per-run overrides reset); LVAR offset kept in `MapFile.ReadScripts` + `# local_vars=` from scripts.lst; LVAR/MVAR get/set wired to the parsed arrays. *Demo: a door's description shows its real lock-state line (LVAR-driven). Headless: `--examine` on midoor with LVAR preset.*

**M1 — Text dialog.** The 8 gsay externals + DialogState, `RunProcedureByIndex`, the giq IQ filter (fixed IQ 5), a reusable text panel (reply + numbered options, keys 1–9/mouse), dialog .msg via existing ScriptList path; barter/combat switches stub to a message. *Demo: full conversations with Sheila, Tubby, and Smitty in the Den. Headless: `--talk X,Y --pick-option N` style flags scripting a conversation transcript to stdout.*

**M2 — Doors that mean it.** Parse `openFlags`/item lock flags (currently skipped); `obj_lock/unlock/is_locked/open/close`; click→`use_p_proc` dispatch with engine-default fallback (our current hardcoded toggle); lockpick via `use_skill_on_p_proc` (action 9, hotkey); map_enter runner on LoadMap (map script first via header.ScriptIndex, then object scripts with the `sid != mapSid` filter and first-run fixedParam). *Demo: Den doors are actually locked; lockpick clicks them open; "That's locked." messages. Null-handle no-ops mandatory here.*

**M3 — Containers & loot.** Mutation externals (create_object, add/rm_obj_to_inven incl. mult variants, destroy_object); loot panel reusing the M1 UI layer (item FRM icons + names, take / take all), ground pickup + drop; script-stocked containers via map_enter. *Demo: loot Mom's box and the Den shelves stocked by their real scripts.* Optional cheap add: equip armor → visible appearance change (maleFid swap).

**M4 — Clock + persistence.** Game clock (advance on travel + a gentle idle rate), our own hour→ambient curve (engine has none — documented), real `game_time*` externals; JSON delta save/load (F5/F9): map+position, locks/doors, taken/created objects, LVAR/MVAR/GVAR, clock. *Demo: save at dusk in the Den, reload — everything persists, evening falls.*

**M5 — Polish pack.** Egg transparency (S), hover outlines (S), roof fade (S), scroll clamps (S); then per-vertex floor lighting via BasicEffect quads (M) and TRANS_* approximations. *Demo: walk behind a building — you stay visible through the egg; light pools have smooth gradients.*

**Phase-5 banner:** minimal combat is now measured at M — the next phase can make the wasteland dangerous.

## Pivot thresholds

- **M2**: if >30% of tested door/container scripts need externals beyond the censused 20, fall back to engine-default behavior per object (our hardcoded logic stays as the safety net) and re-census.
- **M3**: loot UI scope-creep → cut to take/take-all only; drop and equipping move to M5/phase 5.
- **M4**: if our hour-ambient fights map scripts' `set_light_level` calls, scripts win (honor them; curve only applies where no script set a level).
- **Anywhere**: a VM stack desync in the wild = capture the script, add to the regression set (tools/int_analyze.py), fix the arity entry — never special-case in the host.

## Caveats / unverified

- External census covers 9 scripts (doors/containers/maps) + 5 dialog scripts; town-wide map scripts beyond DenBus1/artemple may widen the union modestly.
- NMA/Fandom save-format page internals summarized from search excerpts (403-blocked); per-map `.SAV`-as-gzip-MAP cross-confirmed by two community sources.
- Dialog mid-conversation mode switches (barter/combat) are stubbed by design; untested against the full bytecode until M1 runs.
- Combat estimate is file-level analysis, not a prototype; the animation-queue sequencing risk is real and is why it stays phase 5.
