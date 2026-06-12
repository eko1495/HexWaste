# Phase 6 Research Report — The Opening Hour

*Researched 2026-06-12 in-repo: four parallel tracks — barter (deferred-node flow + price math traced line-by-line, shopkeeper scripts disassembled), combat depth (weapon/armor/XP layouts parsed from real protos, corpse-persistence model), critter scripts (11 scripts disassembled with operand resolution, engine call sites cited), and a playability vertical slice (all 10 opening maps run headless, interactions attempted, stub histograms measured). Full track reports: `/tmp/p6-track-{a,b,c,d}-*.md`. All engine claims carry fallout2-ce file:line citations; unverified items flagged.*

## TL;DR

- **Recommended path: make the opening hour playable.** The empirical audit shows the skeleton already closes end-to-end (artemple → arcaves → arvillag → worldmap → Klamath/Den, verified by headless transcripts) — and the blockers concentrate in ONE family: run three more script procs (critter_p, destroy_p, damage_p) plus ~8 real externals over systems we already have. No single-system phase competes: combat-only leaves a dead world (nothing aggros, kills yield nothing), barter-only unblocks Den commerce and nothing else.
- **Aggro is script-driven, not team-driven** — dungeon critters' `critter_p_proc` is literally `if obj_can_see_obj(self, dude) then attack(dude)`; teams/AI packets are assigned by `critter_add_trait` in map_enter (currently stubbed 46–156×/map). Our phase-5 team-arithmetic joiners were the right *retaliation* model, but unprovoked hostility needs the heartbeat.
- **The dude is the biggest dialog bug**: every stat-gated conversation currently serves the low-INT branch (the Elder's "shiny bottle") because `get_critter_stat` stubs to 0. Fix = parse `premade\player.gcd` — whose layout is byte-identical to the critter proto stat block we already ported — plus ~4 real stat/trait externals. Small effort, fixes every NPC.
- **Combat is unwinnable at level 1** (both seeded `--fight` runs ended dudeHp=0). The fix stack: equipped melee weapons (hmwarr art ships a complete **spear** set — zero save-format changes), armor via equip-time bonus-stat mutation (engine-accurate), healing items, kill XP + HP-only level-up (closed formula).
- **Three latent bugs found**: kills don't survive map revisits (corpses resurrect — `DeadOrdinals` delta fix is S; the engine itself nulls dead critters' sids, combat.cc:4876); the viewer never hooks `ScriptHost.OnStubbedExternal` (the stub log we relied on doesn't exist — one-liner); `SaveState` has no Version field (add before saves circulate).
- **Barter is fully de-risked** and slots in as the closing milestone: opcode just sets a flag (game_dialog.cc:3163-3175), window opens after the proc returns, Tubby/Flick make **no post-barter checks** (cancelled trades can't break dialog), the whole buy/sell spread is one NPC-side formula (inventory.cc:4673-4701). Trap on record: shopkeeper stock moves between box objects and the critter inside talk_p_proc via export.cc cross-script variables — the dialog-end epilogue must run, and `fetch/store_external` must be real.

## Key findings

### 1. Vertical slice — measured, not guessed (track D)

- All 10 opening maps load, render, screenshot cleanly. Confirmed flow: artemple → arcaves (the real Temple, 3 elevations, the spear traps live HERE as spatial scripts — artemple/denbus have zero) → arvillag.
- WORKS TODAY: Klint/Elder/Hakunin/Mynoc/Becky/Tubby/Vic dialogs, lockpick, exit grids, worldmap exits + travel, save/load.
- BROKEN, with transcripts: no unprovoked aggro anywhere (temple = peaceful stroll); Cameron's "let's party" ends dialog without the fight; the final temple door unlocks via destroy_p_proc we never run (lockpick bypasses it — sequence break, not a fix); all stat-gated dialog serves low-INT branches; Flick is mute (barter-gated); level-1 combat unwinnable; long worldmap-bridge walks hit the Pathfinder 2000-node cap (incremental clicks fine).
- Top stub hits per map_enter (measured with a probe after fixing the unhooked logger): `critter_add_trait`, `get_critter_stat`/`has_trait`, `fetch_external`, `move_obj_inven_to_obj`, `give_exp_points`, `override_map_start`, `rotation_to_tile` (denbus1 ×395).

### 2. Critter scripts (track C, 11 scripts disassembled)

- **critter_p_proc**: ticker runs exactly ONE critter script per frame, round-robin, gated `!dialog && !combat && !movie`, no distance gate (`_script_chk_critters`, scripts.cc:705; registered :1598). Dungeon aggro = `obj_can_see_obj` → `attack` (opAttackComplex → scriptsRequestCombat, interpreter_extra.cc:1813). Town hostility gates the same call on LVAR/GVAR bits.
- **destroy_p_proc** (combat.cc:4856-4857, source=killer): reputation GVAR ladders, scripted loot (gecko pelts via perk 73), boss XP (`give_exp_points`). **Base kill XP is engine-side**: proto exp via `critterGetExp` (critter.cc:920) accrues in `_combat_exps` iff killer is dude/dude's team and the script didn't `script_overrides` (combat.cc:4860-4872), paid at combat end (:2816).
- **damage_p_proc** (combat.cc:4850-4851, fixedParam=damage): mostly retaliation backup our whoHitMe model already approximates; explosions use fixedParam=20.
- The engine **removes a dead critter's script** (sid=−1, combat.cc:4876) — and map_enter has no aliveness check, so persistence must replay "script destroyed".
- Spatials: edge-triggered on any object's tile change (animation.cc:2774 → scripts.cc:2516), elevation + exact-tile-or-radius. Only 28/~160 maps use them; **zero timed records exist in retail maps** (keep discarding). arcaves traps: PE-based notice → `critter_damage`; disarm = use_skill_on, 25 XP.

### 3. Combat depth (track B, protos parsed)

- Equip state = item obj flags `IN_LEFT_HAND 0x1000000 / IN_RIGHT_HAND 0x2000000 / WORN 0x4000000` (obj_types.h:78-87); MAP files store them verbatim and our parser already preserves them (verified: Cameron holds Spear pid 7 in right hand; denbus1 has 57 equipped items).
- Attack FID = weapon proto `animationCode` (first weapon field, proto.cc:1585) into FID bits 12-15 (art.cc:1009-1011); attack anim from `extendedFlags & 0xF` (item.cc:116/1334). Parsed: Knife (code 1, 1-6 dmg, AP3), **Spear (code 4, 3-10, AP4, range 2, thrust)**, 10mm Pistol (code 5, 5-12, AP5, range 25, cap 12). **hmwarr ships unarmed + the complete spear set and nothing else** → spear-class melee costs no art swap and no save changes; ranged adds to-hit distance terms (combat.cc:4331-4402), ammo fields we skip, reload, LoF — M-L, defer.
- Armor protos: AC, DR[7], DT[7] order (proto.cc:1556-1564). Runtime AC/DT/DR mutate **bonus stats at equip time** (`_adjust_ac`, inventory.cc:2544) — maps 1:1 onto our base+bonus CritterState. Leather Jacket AC8 DR20; Leather Armor AC15 DT2 DR25; Metal AC10 DT4 DR30.
- XP table = 1000·L·(L−1)/2 (stat.cc:662); level-up adds END/2+2 max HP and heals the delta (stat.cc:771-778). Dude stats: `premade\player.gcd` (critter.cc:1022 gcdLoad) = our already-ported protoCritterDataRead layout + skills/traits.
- **Corpse persistence fix (S)**: `DeadOrdinals` in MapDelta — apply-before-scripts sets Sid=−1 + DAM_DEAD + HP≤0 (so RunMapEnter skips them, engine-accurate), apply-after-scripts replays the deterministic corpse conversion; widen container snapshots to dead critters or looted corpse loot resurrects.

### 4. Barter (track A, scripts + protos parsed)

- Flow confirmed with two corrections: `gdialog_barter`'s own arg **overwrites** `gdialog_set_barter_mod` (game_dialog.cc:3169 — Tubby sets −30 then clobbers with 0); scripts call the opcode before building the post-barter node (equivalent — it's flag-only). CRITTER_BARTER proto flag 0x02 gates trade (refusal = proto.msg {903}).
- Price: NPC demands `(mod+100)/100 × (160+npcBarter)/(160+dudeBarter) × 2×(cost−caps) + caps` (inventory.cc:4673-4701); player goods credit at face value (:4742-4746) — the spread is all buy-side; caps always 1:1 by quantity. Worked examples vs Tubby/Flick (both barter skill 80, parsed): Stimpak buy 430 / sell 175 at dude skill 35.
- Proto `cost` = byte 48, items only (proto.cc:1680); pid→filename goes through items.lst line numbers, NOT pid (pid 8 = `00000004.pro`).
- Shopkeepers stock via box objects + `store_external`/`fetch_external` (export.cc) in talk_p_proc prologue/epilogue; restock gated on METARULE_IS_LOADGAME + LVAR timer. **No post-barter checks** — cancel-safe. UI strings: inventry.msg {27}/{28} (no barter.msg exists).
- Estimate: flat-price **M**, real formula **+S** (do the real formula — it's one function).

### 5. Cross-cutting

- `SaveState` needs `Version = 1` + refuse-newer before saves circulate (S).
- The combat machine has zero MonoGame types; extraction to a Formats `CombatEngine` with a host interface = M (S for turn/AP/joiner math only). Do the S part when wiring destroy/damage procs.
- Ecosystem: fallout2-ce still SUL, issues 428/476 unresolved; MonoGame 3.8.5 previews (3.9 = 3.x LTS); .NET 10 LTS 10.0.9. No action needed.

## Comparison table

| Direction | Effort | Payoff | Risk | Fun | Verdict |
|---|---|---|---|---|---|
| Opening-hour playability (procs + externals + dude + winnable combat) | M-L total, but every piece S/M | Very high — it becomes a *game* | Low (all call sites cited, behaviors measured) | Very high | **Phase 6, M0–M4** |
| Barter alone | M | Medium (Den commerce only) | Low (de-risked) | High | **M5 inside the phase** |
| Ranged weapons + ammo | M-L | Medium | Medium (save migration, LoF) | High | Phase 7 |
| Party members | L | High | High | High | Phase 7+ |
| Spatial traps (arcaves) | S plumbing + M behavior | Medium | Low | Medium | Slack-fill after M2 |
| Renderer fidelity backlog | M | Low-medium | Low | Low | Deferred again |

## Recommended roadmap — "The Opening Hour"

**M0 — Hygiene + corpse persistence (S).** Hook `OnStubbedExternal` into the viewer log (the one-liner that was always missing); add `SaveState.Version = 1` + refuse-newer; implement `DeadOrdinals` (kills survive revisits and save/load: sid=−1 + DAM_DEAD before scripts, corpse conversion after; container snapshots widened to corpses). *Headless: kill the Den peasant, leave, return — still dead, still looted; old saves refuse cleanly.*

**M1 — The dude becomes real (S-M).** Parse `premade\player.gcd` (gcdLoad layout = our proto stat block + skills/traits) into the dude's CritterState; real externals: `get_critter_stat` (0x8016 family), `has_trait` 0x80F3, `do_check`/`roll_vs_skill` against real stats, `get_pc_stat`. *Headless: Elder/Mynoc dialogs serve the normal-INT branches (transcript diff vs today's "shiny bottle").*

**M2 — The wasteland wakes up (M).** critter_p_proc heartbeat (one script/frame round-robin, gated !dialog && !combat, pumped like timers); real `critter_add_trait` 0x8102 (teams/AI from scripts), `attack` 0x80D0 → BeginCombat, `rotation_to_tile`, `anim_busy`, `critter_is_fleeing` minimal. *Headless: walk into arcaves — scorpions aggro on sight (`--fight`-free transcript shows scripted combat start); Den thugs stay neutral until provoked.*

**M3 — Kills matter (S-M).** destroy_p_proc on KillCritter + damage_p_proc on ResolveAttack (source plumbing); engine-side XP accrual (proto exp at combat end, script_overrides honored) + real `give_exp_points`; level-up (closed XP formula, END/2+2 HP, heal delta); HUD XP/level line. Extract turn/AP/joiner math into Formats for headless tests while touching it. *Headless: kill a gecko — XP awarded at combat end, reputation GVAR set; Cameron's death unlocks the temple door properly.*

**M4 — Winnable combat (M).** Equipped melee weapons (inventory panel equips to hand — flag bit + SavedItem.Equipped; attack uses weapon damage/AP/anim code; spear/knife verified vs hmwarr art); armor equipping (WORN bit → bonus-stat mutation, engine-accurate); healing items usable from inventory (stimpak heal roll); enemy weapon use where equipped (Cameron's spear). *Headless: seeded temple fight at level 2 with spear + leather jacket — winnable; transcript shows weapon damage ranges.*

**M5 — Barter (M).** Real `fetch/store_external` (export.cc semantics — also unblocks Becky's door + Klamath boxes); proto cost field; trade panel reusing the loot UI (offer tables, value check, inventry.msg {27}/{28}, cancel returns everything); real price formula with the modifier-overwrite semantics; dialog-end epilogue guaranteed (stock returns). *Headless: buy a stimpak from Tubby at the computed 430, sell fruit, cancel mid-trade — his dialog and stock intact.*

## Pivot thresholds

- **M2**: if the heartbeat causes combat-start re-entrancy bugs against our turn machine after 2 sessions, fall back to "aggro check on dude TileChanged within sight range" (engine-inaccurate cadence, same observable behavior in the opening maps).
- **M4**: if equipped-weapon FID choreography fights the animator, ship weapons as stat-only (damage/AP change, punch art) — ugly but winnable; art is cosmetic here.
- **M5**: barter is self-contained — if the phase runs long, it ships as phase 7's opener without loss.

## Caveats / unverified

- gcd parse: layout asserted from critter.cc:1022 reading order; verify the first three ints against a real `player.gcd` before trusting (track B did not parse one end-to-end).
- Melee skill constant (skill.cc) for spear to-hit: verify the SKILL_MELEE_WEAPONS formula terms before porting (flagged by track B; item.cc's `_attack_subtype` comment contradicts the parsed spear proto — trust the proto: thrust).
- critter_p_proc cadence is 1 script/frame at the engine's frame rate; our fixed 60 Hz Update is faster than the original ~24 — decide whether to throttle to game-ticks (recommended: pump at 10 Hz, the tick rate) — unverified which the engine uses exactly.
- Worldmap bridge long-walk Pathfinder cap: engine-faithful cap, not a bug; not addressed this phase.
