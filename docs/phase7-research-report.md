# Phase 7 Research Report — Ship It, Then Arm the Wasteland

*Researched 2026-06-12/13 in-repo: four parallel tracks — ranged combat (to-hit/LoF/ammo traced line-by-line, real maps probed for armed critters), wider world (trap + recruitment scripts disassembled operand-by-operand, worldmap.txt decoded), fidelity (fresh `--bench` runs, sfx grammar verified against the 1362 ACMs in master.dat, trans-flag census over ~21k objects), and a ship-first audit (release pipeline exercised end-to-end, mid-2026 legal/ecosystem web check). Full track notes: `docs/research-notes/p7-track-{a,b,c,d}-*.md`. All engine claims carry fallout2-ce file:line citations; unverified items flagged.*

## TL;DR

- **Recommended path: publish v0.6 first (M0), then build "The Dangerous Wasteland" (M1–M5: guns + sounds, traps + use_obj_on, a minimum party member, per-vertex floors).** Ship-readiness is *verified*, not assumed — the release script produced clean 37 MB artifacts with licenses in and zero game assets, the README quick-start runs verbatim, and 114 tests pass. The legal window is as good as it gets (no DMCA against any Fallout re-implementation in 2025–26; upstream SUL unchanged; issues #428/#476 dormant) and waiting doesn't shrink the gray zone.
- **Guns are cheaper than feared**: the 10mm shot is **hitscan** — projectilePid = −1, the muzzle flash is baked into the fire FRM, so ranged combat needs *zero new animator features* (rotate, PlayActionOnce, sfx). The recommended rung is single-shot pistols/rifles with ammo/reload/LoF; throwing would build projectile machinery guns don't need, burst+aimed is 2–3× the code and nothing in the Den maps requires it. ~28 critters across denbus1/2 carry equipped firearms with ammo today.
- **The minimum party member is M, not L**: recruitment is just `LVAR13 = old team; critter_add_trait(team 0); party_add(self)` (Kcsulik Node800 / DCVic Node994, identical), and **follow behavior lives entirely in critter_p_proc scripts** (distance-gated walk/run toward the dude) — our 10 Hz heartbeat runs it unmodified once `party_add`/`party_member_obj`/`animate_move_obj_to_tile`/`anim_busy` are real. The one real cost is map-transition carryover (a global PartyState beside the per-map deltas): sized M (2–4 days). Level proto swaps safely cut.
- **No performance debt**: newr1.map re-benched at avg 3.64–3.75 ms (phase-2 baseline 3.6) — the light grid, heartbeat, and combat polling cost ~nothing; ~12 ms headroom remains. Per-vertex floor lighting survives its size-or-kill review: the engine renders 10-vertex fans per square, the delta is visible on dark maps **including artemple — the first map a stranger sees** — honest M, recommended.
- **Two myths corrected by disassembly**: the temple "explosive door" is NOT use_obj_on — it's `damage_p_proc` + `metarule(49) == explosion` (needs the explosives path, not the use-on path); and TRANS_* blends are a permanent footnote — a census of ~21k placed objects found exactly ONE trans-flagged object across 8 opening maps.
- **One save-format bump covers everything**: SaveState Version 2 = SavedItem ammo fields (sentinel −1 = derive from proto), `MovedOrdinals` for NPC positions (replayed BEFORE map_enter, engine-faithful), nothing for override_map_start (no save impact). V1 saves refuse cleanly as designed.

## Key findings

### 1. Ranged combat (track A)

- **To-hit** (combat.cc:4314–4498): `skill − 4×max(0, dist − 2×PE) − (AC + ammoACmod) − 20×max(0, minST−ST) − 10×crittersInLoF`, lighting tiers (dude-target only), clamp 95, no floor. Range/no-ammo/blocked gates live in `_combat_check_bad_shot` (:5643–5694), not in the to-hit. PoC subset: keep distance/PE, AC+ammo mod, min-ST, LoF count; defer lighting (small, dude-only) and all perks.
- **Line of fire** (combat.cc:5897 → animation.cc:1951 → object.cc:2440): a Bresenham walk in *screen space* between tile centers; WALL/SCENERY block, live critters don't (they're counted, feeding the −10/critter term). Our LightGrid occlusion walk is NOT reusable (radial geometry); the cheapest faithful option is a ~70-line verbatim port over our existing tile↔screen math.
- **Ammo**: weapon proto tail (rounds/caliber/ammoTypePid/ammoCapacity/soundCode byte) + ammo proto payload (AC mod → to-hit; DR mod + damage mult/div → the damage loop's ×2-default-then-÷2 wrapper, melee-identity) verified against real .pro bytes (reminder: items.lst line order ≠ file names; 10mm pistol pid 8 = `00000004.pro`). Reload = 2 AP, caliber-matched, partial fills, no mixed mags. MAP weapon records carry ammoQuantity/ammoTypePid we currently skip.
- **Art**: hmwarr (current dude) ships only unarmed+spear; **hmjmps — the engine's default vault suit — ships every weapon code**. The engine has NO weapon-art fallback (wield just fails, inventory.cc:3316): swap the dude to hmjmps and guard equips with artExists.
- **Enemy model**: combat_ai.cc:2686–2904 mapped; minimal honest AI = reload-if-empty → approach-if-out-of-range-or-blocked → stand+shoot (min_to_hit ≈ 30).

### 2. Wider world (track B)

- **Spatial traps**: trigger only on per-step tile change during walks (animation.cc:2774 → scripts.cc:2516; hidden/flat movers and tile<10 filtered; `_scr_SpatialsEnabled` is disabled around first-run map_enter — no re-entry). Match = exact built_tile (tile | elev≪29) or hex distance ≤ radius, exact elevation; self = a lazily created hidden object at the trap tile. SprTrp51 disassembled end-to-end: PE-check reveal (spawns scenery pid 951), exact-tile spring (missile + critter_damage, MVAR1 latches "fired"), TRAPS-skill disarm in use_skill_on (pid 952, +25 XP). Wiring: keep the spatial records MapFile currently discards, index per map, hook the dude's TileChanged. Trap scripts run almost entirely on already-real externals — **M-minus total**.
- **use_obj_on** (proto_instance.cc:1245): medical items bypass scripts (hardcoded); otherwise item-script then target-script with returnValue/overrides precedence. Real opening-hour consumers: crowbar pry (generic doors + ~12 boxes), grave digging, the still, **Vic's radio**, the temple key door. `obj_being_used_with` already works; missing = RunUseObjOn + a "use item on object" UI path (S–M).
- **Party minimum**: see TL;DR. PartyState carryover design: pid/inventory/stats/LVAR array travel globally; a departed-marker lands in the source map's delta; death converts the follower back into an ordinary map-delta corpse. Engine-only extras safely cut: id rewrite, NO_SAVE flags, level proto swaps, map-enter ring placement (spawn adjacent instead).
- **Random encounters**: worldmap.txt fully decodable (subtile grid → table → weighted entries with Global/Level conditions → pid/ratio compositions); roll cadence = 1 step/30 game-min, throttled 1500 ms real + 3-tile delta, suppressed near known areas; frequency table Frequent=38%…Rare=4%. Encounter maps differ from towns only by `saved=No` (must skip our delta slot!), `random_start_point_N`, runtime spawning via our existing created-object path. **M**, cleanly separable → spillover.
- **Small correctness**: NPC positions = `MovedOrdinals{tile, elevation, rotation}` replayed before map_enter (S); override_map_start (interpreter_extra.cc:522; tile = 200·y+x, sets dude pos/rotation mid-RunMapEnter) (S).

### 3. Fidelity + measurements (track C)

- **Bench**: newr1 avg 3.64–3.75 ms / p95 ~6.1 / max ~12.8 (baseline 3.6/6.2/13.6); denbus1 4.16 ms. Phases 3–6 added ~zero frame cost. Perf is not a phase-7 item.
- **Per-vertex floors**: engine = 10 verts / 10 triangles per square with a flat-tint fast path when corners agree (tile.cc:147-176, 1598-1697) — our current render IS the fast path. Port = texture atlas + VertexPositionColorTexture + BasicEffect, rebuilt on light change only. Visible on artemple. **M (4–5 days), do it.**
- **Combat sfx**: name grammar `W{R|A|O|F|H}{soundCode}{1|2}{material}XX1` (game_sound.cc:1374-1447) + `H{M|F}XXXX` death/pain with gender fallback (:1117-1158); WAJ1XXX1/WHJ1MXX1/HMXXXXZB verified present. Needs: parse the weapon soundCode byte, two SfxName builders, 3 viewer call sites. **S (~1 day), pairs with guns.**
- **TRANS_*/egg.frm**: permanent footnote (see TL;DR; egg.frm is a 129×98 feathered intensity mask — faithful rim feathering = forbidden per-frame CPU compositing).
- **play_gmovie**: one opening-hour caller — ARVILLAG map_enter plays vsuit.mve (constant 3, verified in bytecode); elder.mve is engine new-game. No stills ship, but `text\english\cuts\*.sve` subtitles do → black caption card with AAF text + movie-seen flag (**S, ~0.5–1 day**).
- **Tests**: CombatEngine extraction honest-M (ICombatPresenter sketch in notes; the tricky contract is damage-on-anim-completion); headless barter test ~0.5 day (mirror DialogTests on denbus1); top-5 regressions: perf canary (<8 ms), persistence round-trip, seeded combat trace, sfx-name facts, opening-maps stub-histogram smoke.

### 4. Ship-first audit (track D)

- Release pipeline **proven**: `scripts/release.sh` ran end-to-end (exit 0), artifacts carry LICENSE/NOTICE/README, exec bit survives tar, zero game assets (grepped). README quick-start + controls verified against code. Hygiene caught and FIXED in-tree: tracked `__pycache__/*.pyc`, hardcoded `/home/eko` path in int_analyze.py. Remaining cosmetic: `/home/eko`//tmp paths inside docs/research-notes (publish-audit item per RELEASING.md), .pdb files in artifacts.
- Missing for a stranger: README **screenshots** (common, tolerated practice — devilutionX ships them; never a takedown trigger) + a GIF, a CHANGELOG, a v0.6.0 tag, and a front door (main menu S/M, death screen S, premade-gcd picker S — GcdFile already parses everything).
- Web (mid-2026): fallout2-ce alive at v1.3/SUL unchanged, #428 closed unanswered/#476 dormant; **no DMCA/C&D against any Fallout engine re-implementation 2025–26** (MS/Bethesda demonstrably fan-friendly — FOLON hires); MonoGame 3.8.5 still preview → **keep the 3.8.4.1 pin**; .NET 10 LTS fine to Nov 2028.
- First-impression ranking: main menu > death screen > gcd picker > SPECIAL screen (M, defer) > auto-spent skill points (invisible, defer) > perks (scope creep, **skip**).

## Comparison table

| Direction | Effort | Payoff | Risk | Fun | Verdict |
|---|---|---|---|---|---|
| Ship v0.6 (screenshots, menu, death screen, tag, publish prep) | S-M | High — it exists publicly; feedback compounds | Low (verified pipeline, benign climate) | Medium | **M0** |
| Saves V2 + NPC positions + override_map_start + tests | S | Medium (correctness; unblocks the rest) | Low | Low | **M1** |
| Guns + ammo + LoF + combat sfx | M-L | Very high — the Den shoots back | Medium (LoF/AI, mapped) | Very high | **M2** |
| Spatial traps + use_obj_on + gmovie card | M | High (the temple corridors bite; crowbars/keys/radio) | Low (scripts run on real externals) | High | **M3** |
| Minimum party member (Sulik/Vic) | M | Very high — the classic companion fantasy | Medium (carryover state) | Very high | **M4** |
| Per-vertex floor lighting | M | Medium-high (artemple first impression) | Low | Medium | **M5** |
| Random encounters | M | Medium-high | Medium (saved=No quirk) | High | Spillover / phase 8 |
| Burst + aimed shots / perks / TRANS_* | M-L | Low-medium | — | — | Skip / footnote |

## Recommended roadmap — "Ship it, then arm the wasteland"

**M0 — v0.6 out the door (S-M).** README screenshots + a short GIF (game-art screenshots are accepted practice), CHANGELOG.md + v0.6.0 tag, main-menu front door (title card: New Game → premade gcd picker, Quit), a death screen worth the name (stats + Load/Quit instead of bare F9), scrub machine paths from docs/research-notes, then the fresh-history publish per docs/RELEASING.md (the actual `git push` stays with the user). *Demo: a stranger's first five minutes have a front door, a death, and a way back.*

**M1 — Version-2 saves + correctness (S).** SavedItem gains AmmoQuantity/AmmoTypePid (−1 sentinels); MapDelta gains MovedOrdinals (NPC tile/elevation/rotation, replayed before map_enter); override_map_start wired into RunMapEnter; SaveState.CurrentVersion = 2. Headless barter test + the top-5 regression tests from track C. *Headless: move an NPC via script, leave, return — he's where the script left him; V1 saves refuse politely.*

**M2 — Guns of the Den (M-L).** Finish weapon/ammo proto payloads + MAP ammo fields; to-hit with distance/PE, ammo AC/DR mods, min-ST, LoF count (combat.cc subset); the ~70-line screen-space LoF walk; reload (2 AP, caliber-matched); dude art → hmjmps + artExists equip guard; enemy AI reload→approach→stand+shoot; combat sfx (weapon WAJ1XXX1 grammar + HMXXXX deaths). *Headless: seeded firefight transcript vs Den thugs — ranged to-hit falls off with distance, walls block, reloads consume ammo; everything audible.*

**M3 — The wasteland's tricks (M).** Spatial-script records kept + indexed + TileChanged trigger with the engine's gates; the arcaves corridor springs/reveals/disarms with XP; RunUseObjOn + use-item-on-object UI (crowbar, temple key, Vic's radio); vsuit.mve caption card from the .sve subtitles. *Headless: walk the trap corridor — PE reveal or spear to the face; disarm pays 25 XP; pry a Den box with the crowbar.*

**M4 — A friend for the road (M).** Real party_add/party_remove/party_member_obj; recruitment via the existing dialog paths (Sulik Node800 / Vic Node994); follower follows via his own critter_p_proc on our heartbeat, fights on team 0 through the existing AI turns; PartyState carryover across maps + saves (departed marker, corpse handoff to map deltas). NO inventory/level/dialog management — documented cut. *Headless: recruit Sulik in Klamath, walk to the Den — he arrives, he fights, he persists through save/load.*

**M5 — The floor is light (M).** Per-vertex floor lighting: 10-vertex fans per square over a floor-texture atlas via BasicEffect, vertex colors from LightGrid, rebuilt on light-change ticks only, flat-tint fast path kept. Bench gate: stays under the 8 ms canary. *Demo: artemple at night — light pools bleed across tile seams like the original.*

## Pivot thresholds

- **M2**: if LoF/AI sequencing fights the turn machine for >2 sessions, ship guns "stat-only at range" (no LoF wall blocking, flat −10) — still a firefight; LoF returns later.
- **M4**: if PartyState carryover leaks or dupes objects, pin the follower per-map ("waits in town") — recruitment/combat still demo; carryover becomes phase 8's first item.
- **M5**: killable without ceremony if M2/M4 overrun — it's pure polish with no dependents.
- **M0** is not skippable: every later milestone benefits from public feedback, and nothing in M1–M5 changes what early feedback measures.

## Caveats / unverified

- Ammo DR/mult interactions with armor DT order: trace cited but not yet unit-tested against in-game numbers — add a fact test in M2.
- hmjmps owns all needed codes per the art listing; the *female* set (hfjmps) unchecked — fine while the premade picker ships male gcds only.
- Party carryover vs the heartbeat's per-elevation candidate list: follower on another elevation pauses following (acceptable; engine behaves similarly indoors).
- Encounter-map `saved=No` semantics rest on maps.txt flags + one parsed map; verify against a second encounter map before building (phase-8 item).
- MonoGame 3.8.5-preview APIs not evaluated; the 3.8.4.1 pin stands until a stable release.
