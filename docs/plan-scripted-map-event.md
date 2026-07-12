# Phase plan — scripted map transitions & the Klamath grazing-fields event

> **Status:** PLAN (not started). Scoped from the Klamath QA sweep, where the last two
> quests (391 Rescue Torr, 102 the Duntons) are gated on the grazing-fields event.
> **Goal:** complete 391 + 102 via the real script path, and unblock the broader class of
> quests that hinge on a script-driven `load_map` transition + its arrival context.
> **Ground truth:** everything below is from bytecode disassembly + live harness probes this
> session; file/line/tile/gvar refs are concrete.

## 1. The reframing (why this is bounded, not a worldmap subsystem)

The grazing-fields event was assumed to be a *worldmap special encounter*. It is NOT — it's a
**scripted `load_map` transition** from Torr's own dialogue:

- **KCTorr Node020** runs `game_time_advance; load_map(mapIndex=14, param=13)`. Map index 14 =
  `klagraz` ("Klamath Grazing Area", maps.txt). Agreeing to guard Torr's brahmin (gvar 182)
  is meant to warp you to the grazing fields.
- `param=13` is the **arrival context**: `load_map` sets `GVAR_LOAD_MAP_INDEX` (gvar **27**) to
  the param, and `klagraz` map_enter gates its setup on `global(27) == 13`. So the param IS the
  "you arrived here to guard" signal the map reads.
- `load_map` (0x80E4) is **already implemented** in Hexwaste: `ScriptHost.LoadMap`
  (ScriptHost.cs:1726) sets gvar 27 = param and fires `LoadMapRequested`; the viewer
  (ViewerGame.cs:1233) turns that into `_pendingTransition = MapDestination(mapIndex,-1,-1,-1)`,
  applied by the existing transition machinery (ViewerGame.cs:2799, Harness.cs:594).

So the engine primitive exists. What's unverified/missing is the **end-to-end path**: reaching
Node020, applying a *dialogue-triggered* deferred transition, and then the klagraz map_enter +
KCTorr/KCDunton confrontation actually running to the outcome that exposes the rescue.

## 2. The target completion chains (grounded)

- **102 (Duntons):** KCTorr `Node018` unconditionally calls `Node930`, which sets `102:=2` AND
  `71:=1`. Reached via Torr dialogue `talk =call=> Node013 =opt2=> Node018` — but Node013's
  routing only opens post-event (on klagraz, with the confrontation state).
- **391 (Rescue Torr):** gvar **71** = "Torr displaced to the canyon". `Node930` sets `71:=1`
  → the canyon Torr (KLACANYN, "no critter" until then) becomes present. Rescue = talk canyon
  Torr → `Node940` (LVAR10 follow flag) → escort → `critter_p_proc` fires `leave_player` near
  delivery tile **19450** → `391:=2` + `71:=0`. **The escort half is already solved** by the
  P-QA escort-sim (`--teleport` + `--escort-pump`, proven on Smiley 197) — it just needs a
  present, joinable Torr, which the event provides.

So both quests reduce to: **make the klagraz confrontation fire and resolve to `71:=1`.**

## 3. What already exists vs. what's needed

Exists (reuse):
- `load_map` external + `GVAR_LOAD_MAP_INDEX` (gvar 27) + `_pendingTransition` machinery.
- The escort-sim (`--teleport`/`--escort-pump`) for the 391 delivery half.
- `--map-objects`, `int_disasm.py`, the `round: opts=N` dialogue-nav aid.
- Combat engine, timed-event pump (`--pump-ms` runs timed events + critter_p_proc + map_update).

Unknown / to build (the phase):
- **W1. Reach KCTorr Node020.** Find the dialogue option sequence that triggers the load_map
  (a `--talk-seq` investigation; not code). Open Q: is Node020 gated on 182 already active from
  a prior visit, or reachable in one conversation?
- **W2. Apply a dialogue-deferred transition in the harness.** `--talk-seq` closes the dialog
  (`_dialog=null`) and moves on; it likely does NOT apply the `_pendingTransition` a load_map
  node just queued. Verify, and if so, apply pending transitions at the end of `--talk-seq`
  (mirror the `--pump-ms` loop's `_pendingTransition` handling at Harness.cs:594). Small.
- **W3. Drive the klagraz confrontation.** After arriving with gvar 27==13, pump timed events +
  critter procs and see how far the KCTorr/KCDunton scene progresses. This is the real risk
  (see §5): it may involve combat, KCDunton AI, and timed steps. Determine whether it resolves
  to `71:=1` under pumping, and what (if any) player action (a Dunton dialogue choice, or
  killing the Duntons) it needs — all drivable with existing verbs (`--talk-seq`, `--kill`).
- **W4. Complete 102 + 391.** Once `71:=1`: 102 via the Torr Node018 dialogue (now reachable);
  391 via the escort-sim to the canyon Torr (present once 71==1 + its map_enter runs).
- **W5. Fixtures.** `quest-torr-duntons` (102) and `quest-torr-rescue` (391) goldens in
  scripts/quest-golden.sh, state-only (no --set-global faking of the quest gvars).

## 4. Work breakdown (reviewed steps, in order)

1. **Spike (W1+W2):** find the Node020 path; verify whether a load_map in dialogue transitions
   headlessly. Decide if W2 (apply pending transition after `--talk-seq`) is needed. → a go/no-go
   on the whole flow being harness-drivable. **Do this first; it de-risks everything.**
2. **W3 spike:** arrive at klagraz via the real load_map with 27==13; pump; instrument gvars
   71/102/27/188 + KCTorr/KCDunton talkability each step. Map exactly what the confrontation
   needs. This is the step most likely to surface a sub-gap (an unwired external, a combat hook).
3. **Implement the smallest missing piece** found in steps 1–2 (likely just W2, possibly a
   confrontation-driving nuance). Keep each change golden-verified (all 5 suites byte-identical).
4. **W4:** drive 102 then 391 to completion; confirm the lifecycle gvars.
5. **W5:** record the two goldens; run the full golden set.

## 5. Risks & unknowns (honest)

- **The confrontation may need combat.** If the Duntons must be fought (or the scorpions
  attack), the outcome that sets `71:=1` may depend on a combat result the harness drives with
  `--kill`/`--fight` but with RNG/timing nuances. Mitigation: `--rng-seed`, and if a specific
  Dunton dialogue branch (not combat) sets 71, prefer that.
- **A hidden sub-external.** The klagraz/KCDunton scripts may call an external that's stubbed
  (like load_map was thought to be). ProcAnalyze `--map klagraz` reports `stubbed=0`, which is
  encouraging, but the confrontation may lean on a timed/AI path Hexwaste under-drives.
- **Dialogue-deferred transition side-effects.** Applying `_pendingTransition` mid/post
  `--talk-seq` must not perturb existing dialogue goldens — gate it to the harness path and
  re-run the encounter/quest suites.

## 6. Non-goals (explicit scope fence)

- NOT building worldmap *random/special encounter* firing (this event is script-`load_map`
  driven; the random-encounter system is unrelated and already exists).
- NOT a general "play any scripted cutscene" engine — just the load_map-transition path + the
  one confrontation, generalized only as far as steps 1–3 naturally allow.
- If W3 shows the confrontation needs a large unmodeled subsystem (e.g. full FO2 party-NPC
  battle AI), STOP and re-scope — do not free-hand a combat-AI feature under this phase.

## 7. Estimate & payoff

- **Engine/harness delta:** S–M (the campaign-port review's own rating for `load_map` + the
  deferred transition; most of it already exists — this phase is mostly *verification + the W2
  bridge + confrontation-driving*, not new subsystems).
- **Payoff:** closes Klamath (6/6). Per docs/CAMPAIGN-PORT-REVIEW.md, the `load_map` transition
  path + arrival context also unblocks quest climaxes "across 5+ regions" and the late-game
  tanker spine — this phase is the first real exercise of that path end-to-end, so it doubles as
  validation for a much larger swath of the campaign.

## 8. First action when starting

Run the W1 spike: disassemble KCTorr's `talk_p_proc` routing to Node020 (which gvar/visit
gates it), find the `--talk-seq` option chain, and test whether the load_map transition applies
headlessly. Everything downstream depends on that go/no-go.

## 9. Spike results — W1 + W2: GO (2241-07-xx)

**W1 (reach Node020): DONE.** `--talk-seq 24291 1,1,1` (greet → "sure I can help guard" → "I'll
help guard now") IS Node020 — the accept option itself runs `gfade_out; game_time_advance;
load_map(14, 13)`. Confirmed live: gvar 27 (GVAR_LOAD_MAP_INDEX) reads **13** immediately after
the dialogue closes. No prior visit / extra gating needed; the first accept warps you.

**W2 (apply the dialogue-deferred transition): GO, and NO CODE NEEDED.** `--pump-ms` after the
talk-seq applies the queued `_pendingTransition` (Harness.cs:594) and loads klagraz. Proven by
`--map-update-probe`: `map=kladwtwn.map` before → **`map=klagraz.map`** after `--talk-seq 24291
1,1,1 --pump-ms 4000`. klagraz map_enter runs (it resets gvar 27 → 0), and
`newStubbedExternals=0[]` — no unwired external on arrival. The feared W2 bridge is unnecessary.

**So the transition half of the phase is FREE.** The remaining work is W3 only:
- On klagraz after arrival: gvar 27→0 (map_enter ran), 391/102/71 all still 0, and the static
  klagraz Torr tile 24572 reports "no critter" — i.e. the map_enter/critter setup has
  repositioned or not-yet-activated him. **W3 = drive the KCTorr/KCDunton confrontation from
  this arrived state to `71:=1`.** That's the next spike (per §4 step 2) and holds the real risk
  (§5: it may need combat / a Dunton dialogue branch). W4/W5 (complete 102+391, record goldens)
  follow once 71:=1 is reachable.

Revised estimate: **S** (W1+W2 free; only W3 + fixtures remain). Next action: the W3 spike —
dump klagraz objects post-arrival to locate/inspect Torr + the Duntons, then drive the scene.
