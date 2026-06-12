# Phase 5 Research Report — The Wasteland Bites Back, Then Ship

*Researched 2026-06-12 in-repo: four parallel tracks — combat (implementation-grade, with empirical byte-layout verification against real game files), timers+barter (script disassembly survey over 127 scripts), persistence+performance (measured with probes), and ship-it (license read from the actual fallout2-ce LICENSE.md + web verification). All engine claims carry file:line citations; unverified items flagged.*

## TL;DR

- **Recommended path: combat, bracketed by foundations and ship-prep.** M0 fixes measured bugs + lands timers (S); M1 lands multi-map persistence (M, also fixes a real memory leak); M2–M4 build minimal combat in three demoable steps (~1,450 LOC total, all hazards mapped); M5 is the ship-prep checklist. Barter (M) is the designated spillover if combat slips.
- **Combat is implementation-ready, and two presumed simplifications turned out to be engine-faithful:** (1) "apply damage when the attack animation completes" is *literally* what the engine does (`_apply_damage` runs in `_combat_anim_finished`); (2) **scriptless hostility works** — attack any critter and it fights back via pure team/`whoHitMe` arithmetic in `combat_ai`, with same-team joiners; `critter_p_proc` only adds unprovoked on-sight aggro.
- **License verdict: conditional GO for public release.** fallout2-ce is **Sustainable Use License** (fair-code): derivatives explicitly permitted, but free-of-charge/non-commercial only, SUL text + modification notice required, no MIT relicensing. Residual un-engineerable risk: upstream is a tolerated decompilation. **Hygiene catch (verified + already fixed in the working tree): `city.txt`/`worldmap.txt` were git-tracked extracted game data** — untracked now; history scrub happens at publication (fresh-history publish).
- **Three real bugs surfaced by the research:** the caps-adjust arity stub returns "success" so **pay-caps dialog branches hand over goods for free**; ScriptHost leaks ~590 KB per map transition (LVAR slices keyed by dead MapFile instances + an unbounded handle table); hover picking allocates two full list reversals per frame at 60 Hz.

## Key findings

### 1. Combat — implementation-grade (the headline)

- **Turn loop**: the engine's blocking `_combat`/`_combat_turn`/`_combat_turn_run` maps cleanly onto a `CombatController` state machine in our Update loop (RoundStart → PlayerTurn → ActionPlaying → EnemyTurn → RoundEnd → CombatOver), where ActionPlaying waits on our walker `!Moving` AND all animator states finished — the engine's `_combat_turn_running` is a *counter* over participating sequences, so wait on **all** participants (combat.cc:3121-3133, 5320-5390).
- **Damage timing**: outcome is rolled *before* animating (`attackCompute` → `_action_attack`), HP mutates only when the full sequence completes — our natural design is the engine's design.
- **Hazards mapped**: cancel the target's fidget/walk before choreographing (the engine `reg_anim_clear`s both parties); AI movement is AP-budgeted (1 AP/hex) so the walker needs a budget parameter; joiners can enter at next round (invisible deviation); check dude death after every action.
- **Stats — exact layouts, empirically verified** (parsed 4 real protos + walked 3 real maps to the last byte): critter PRO = header (+team at 0x28) then baseStats[35] @0x30, bonusStats[35] @0xBC, skills[18] @0x148 (unarmed = index 3), bodyType/exp/killType, optional damageType (two 412-byte protos lack it — default on EOF). The 11 skipped MAP ints: reaction, then the combat block (damageLastTurn, maneuver, **ap**, **results**, aiPacket, **team**, whoHitMeCid), then **current hp** (per-instance — denbus1 critters carry individual HP), radiation, poison.
- **Death**: minimal anims FALL_BACK/FALL_FRONT (20/21, behind-check flips); corpse = single-frame art at anim+28, frame 0, `OBJECT_NO_BLOCK` + flat → **our existing loot panel works on corpses unchanged** (inventories already parsed; gate on DAM_DEAD).
- **Formulas (minimal)**: toHit = unarmed skill (30 + 2×(AG+ST) + proto skill) − AC, clamp 95, d100; damage = rand(1, 2+meleeDmg) − DT, ×(1−DR/100). Player death → game-over overlay → F9 (the M4 save) closes the loop.
- **Size: ~1,450 LOC across three milestones** (stats 300, player-attacks/death/loot 600, AI-turns/game-over 550) — at the optimistic end of the phase-4 band.

### 2. Timers — S (~1 day), with two traps found

- Queue = sorted absolute-tick list; expiry runs `timed_event_p_proc` with the stored fixedParam. **The engine drops all script timers on map exit** (`_queue_leaving_map`) — our drop-on-transition is engine-accurate, scripts re-arm from map_enter.
- Empirical survey (43/127 scripts arm timers): door auto-close (miDoor re-arms every game-second until the dude leaves), state resets, and ambient life — brahmin scripts arm three perpetual timers (wander / "moo" float / dung `create_object`). The hcprof script chains 12 timers into an out-of-dialog cutscene — works because the pump is **dialog-gated** (trap #2: don't pump while dialog/loot is open).
- Trap #1: our idle clock runs 60× real time; engine delays assume 1:1 (miDoor's 1-second close would fire in 17 ms). Timers must compare against a 1:1 tick source.
- New real externals needed: add/rm_timer_event, metarule3 rule 100 (dedupe), plus trivial hex math (`tile_num`, `tile_distance_objs` — currently stub-0 makes miDoor think the dude is adjacent forever, `cur_map_index`, `tile_num_in_direction`).

### 3. Barter — M (~2–3 days), and a live bug

- `gdialog_barter` is **deferred**: the script builds the post-barter dialog node first, then the trade window opens, then the queued node presents. No script checks a result (the opcode pushes nothing) — our stub can't break flow, but: **the `item_caps_adjust` arity stub returns 0 = success, so Tubby's pay-caps bribe branches currently take your goods for free.** Fix regardless of direction.
- Exactly 4 externals become real: `item_caps_total` (sum StackCount of pid 41), `item_caps_adjust`, `gdialog_barter` (session flag, open after proc returns), `gdialog_set_barter_mod`. Price formula documented (caps 1:1; goods ≈2× at equal skill; flat prices fine for PoC). Prereq: keep the proto `cost` field (currently skipped). UI reuses the loot panel.

### 4. Multi-map persistence — M (~1–2 days), design settled by probes

- **MAP object Ids are NOT keys** (455–626 duplicate-Id groups per map). The stable key: **load-order ordinal** per elevation (deterministic pristine loads; scripts only append).
- **Critical sequencing discovery**: Den scripts gate one-time init on **LVARs, not the saved-map flag** — setting MAP_SAVED changed nothing, while persisted LVAR slices dropped spurious re-creations 78 → 27. Therefore: pristine load → assign ordinals → **import LVAR slices + MVARs** → RunMapEnter(firstRun: 0) → apply door/taken/created/container-snapshot deltas → lighting. The residual stub-gated restock is best handled by overwriting container inventories from a snapshot.
- Folds in the **leak fix**: re-key LVAR slices by map *name* and clear the handle table per transition (measured: ~590 KB retained per visit, linear).

### 5. Performance & tests (measured)

- Map transitions cost ~15–30 ms total (load + hundreds of VM map_enter runs) — a non-issue. The leak is the real finding (above).
- Frame-allocation: **HIGH** — hover `PickObject` does `Enumerable.Reverse` over both full object lists + allocates a SpriteInfo per scanned object, every frame; fix with reverse index loops + cull-before-allocate. MEDIUM: SpriteInfo allocated before viewport cull in DrawObjects. LOW: DialogSession.Options allocates per access.
- Test gaps, top items: SaveState round-trip (move SaveState to Formats — no MonoGame deps), ScriptHost transition-hygiene regression (locks in the leak fix), world-mutation externals, DialogSession.Choose bounds, RunMapEnter firstRun semantics, Pathfinder cap, GameFileSystem layering, AcmDecoder failure modes, Fid round-trips, MapList misses.

### 6. Ship-it — conditional GO

- **License**: fallout2-ce = **Sustainable Use License v1.0** (verified in our clone + upstream). Derivative ports explicitly permitted; conditions: distribute free/non-commercial, ship the SUL text + NOTICE crediting alexbatalov/fallout2-ce as a modified derivative, keep "no game assets" stance. **Cannot relicense MIT; cannot monetize (no tip jars — GitHub Releases, not itch.io).** Precedent: two PSVita derivative ports publish under the same terms. Unresolvable residual: upstream is itself a decompilation Bethesda tolerates (issue #476); the maintainer hasn't answered redistribution questions (issue #428).
- **Hygiene**: `city.txt`/`worldmap.txt` were tracked (now untracked; gitignore extended with `*.msg/*.lst/*.gam/maps.txt`); publication should be a fresh-history repo which also handles the history scrub. Audit CLAUDE.md/reports for local paths before publishing; research reports are publishable provenance (move under `docs/`).
- **Rename**: `FalloutPoc` is a unique token → one `git grep -l | xargs sed` pass + `git mv` of dirs/slnx; "ported from fallout2-ce" comments STAY (they're the attribution); the "requires Fallout 2" README notice stays (interoperability naming is fine; branding is not).
- **Packaging**: `dotnet publish -r linux-x64|win-x64 --self-contained` as a folder (MonoGame docs recommend AGAINST single-file), tar.gz on Linux for the exec bit, no trimming (System.Text.Json reflection in saves). Onboarding: devilutionX-level is the right effort — keep `--game-dir`, add a probe list (exe dir, `C:\GOG Games\Fallout 2`, Steam paths, Linux equivalents) and a clear missing-`master.dat` message. GOG registry key value: unverified.
- Ecosystem delta: none (fallout2-ce dormant, Gecko very active, MonoGame 3.8.4.1 stable).

## Comparison table

| Direction | Effort | Payoff | Risk | Fun | Verdict |
|---|---|---|---|---|---|
| Foundations (bug fixes + timers + top tests) | S | Medium (alive world, correctness) | Minimal | Medium | **M0** |
| Multi-map persistence (+leak fix) | M | High — permanent world | Low (design probed) | Medium | **M1** |
| Combat: stats | S | Medium (HP on examine) | Low | Medium | **M2** |
| Combat: player attacks + death + loot | M | Very high | Medium (sequencing, mapped) | Very high | **M3** |
| Combat: AI turns + game over | M | Very high — it's a game | Medium | Very high | **M4** |
| Ship-prep (rename, license, packaging) | S–M | High — it exists publicly | Low (license conditions known) | Medium | **M5** |
| Barter | M | Medium-high | Low | High | Spillover / phase 6 |
| Renderer fidelity (per-vertex floors etc.) | M | Medium | Low | Medium | Phase 6 |

## Recommended roadmap — "The wasteland bites back, then ship"

**M0 — Foundations.** Fix the caps-adjust stub (pay-caps branches must actually pay — make `item_caps_total/adjust` real, they're 2 of barter's 4 anyway); fix hover-picking allocations; land **timers** (sorted tick queue, 1:1 tick source, dialog-gated pump, the 4 trivial hex-math externals); add the top-5 gap tests. *Demo: Den doors auto-close behind you; brahmin wander, moo, and leave dung on a 2–5 min cadence.*

**M1 — Persistent world.** Per-visited-map deltas (ordinal-keyed), LVAR-import-before-map_enter, container snapshots, ScriptHost leak fix, SaveState moves to Formats with round-trip tests; saves gain VisitedMaps. *Demo: loot the Footlocker, walk to the Temple and back — it stays looted; save/quit/load preserves everything.*

**M2 — Critter stats.** Extend the proto reader (team @0x28 + the stat block) and the MAP parser (the 11 skipped ints: team/results/current-hp); CritterState with effective stats; examine shows HP/AC. *Headless: `--examine-critter` stat dumps match the empirically verified values.*

**M3 — Player attacks.** CombatController (PlayerTurn/ActionPlaying), minimal to-hit/damage, punch + hit/death choreography with damage-on-completion, corpse conversion (anim+28 FID, NO_BLOCK, lootable through the existing panel), AP/HP text HUD. *Headless: `--attack <tile> --rng-seed N` combat-log transcript; loot the corpse.*

**M4 — The wasteland fights back.** Enemy turns (target = whoHitMe, AP-budgeted approach, punch), same-team joiners (the scriptless engine rules), `_combat_should_end`, dude death → game over → F9. *Headless: `--fight <tile>` deterministic full-fight transcript.*

**M5 — Ship-prep.** Rename pass (`FalloutPoc` → new name), LICENSE.md (SUL v1.0) + NOTICE, fresh-history public repo (scrubs the old tracked game data), README rewrite (screenshots, build, license, provenance docs under `docs/`), `dotnet publish` folder artifacts for linux-x64/win-x64 + a release script, game-dir probe list + friendly missing-data message. *Demo: a stranger downloads the release, points it at their GOG install, and plays.*

## Pivot thresholds

- **M3**: if animation-sequencing bugs persist after 2 sessions (out-of-order anims, deadlocked ActionPlaying), cut to instant-resolution combat (numbers + death anim only, no attack choreography) — still a game, ship-able.
- **M4**: if AI turns fight the walker architecture, cut same-team joiners first, then AP-movement (teleport-adjacent + attack as the floor).
- **M5**: if any new licensing information surfaces (issue #428 answered, upstream takedown), re-evaluate publication; the code remains privately useful regardless.
- Barter slots in wherever a milestone finishes early (its 4 externals are half-done after M0).

## Caveats / unverified

- GOG registry key for auto-detection unverified (check on a Windows machine).
- itch.io policy on data-requiring ports unverified (moot — GitHub Releases chosen).
- The SUL's "non-sublicensable/non-transferable" redistribution semantics remain unanswered upstream (issue #428); we follow the precedent of existing public derivatives.
- Combat LOC estimates are file-level analysis + the milestone split; the sequencing risk is real but bounded by the pivot threshold.
- Timer survey covered 127 of 1,894 scripts (every 12th + known names); the external closure may grow slightly with dialog-heavy timer scripts.
