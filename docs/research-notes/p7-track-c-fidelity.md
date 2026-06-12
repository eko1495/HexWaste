# Phase 7 Track C — Fidelity / Perf / Test Gaps

## Q1. Performance re-bench (2026-06-12, Debug DLLs, --bench 500, --no-audio)

| map | objects | avg | p95 | max | uncapped fps | palette uploads | notes |
|---|---|---|---|---|---|---|---|
| newr1.map (P2 baseline) | 2841 | 3.6 ms | 6.2 ms | 13.6 ms | — | — | phase-2 measurement |
| newr1.map run 1 (now) | 2841 | 3.75 ms | 6.26 ms | 12.82 ms | ~267 | 94 | cycling FRMs 4 |
| newr1.map run 2 (now) | 2841 | 3.64 ms | 5.96 ms | 11.55 ms | ~275 | 94 | run-to-run noise ±0.1 ms |
| denbus1.map (now) | — | 4.16 ms | 7.07 ms | 12.00 ms | ~240 | 104 | cycling FRMs 1 |

**Verdict: NO regression.** Everything added since phase 2 (LightGrid, 10 Hz
critter_p_proc heartbeat, per-frame ProcessCombatAnimations/UpdateCombat,
stub histograms, wander) costs ~0.0–0.15 ms on the heaviest map — inside
run-to-run noise. Attribution: heartbeat is round-robin one critter per game
tick (ViewerGame.cs:2208 "_script_chk_critters" port), UpdateCombat/
ProcessCombatAnimations (ViewerGame.cs:2326/:2025) early-out when idle; the
frame is still dominated by sprite submission + CPU palette conversion.
Budget at 60 Hz: ~16.6 − 4.2 = **~12 ms headroom on the worst map measured**.
Per-vertex floor lighting (~4800 quads worst case, see Q2) and projectile
sprites (a handful of extra draws) both fit comfortably. **Performance is
NOT a phase-7 work item; no shader still justified.**

## Q2. Per-vertex floor lighting — size or kill

**Engine source (not 4 corners!):** `tileRenderFloor` (tile.cc:1598) samples
**10 vertices per floor square** from the *hex* light grid — offsets are
parity-dependent (`_verticies[10]`, tile.cc:147-158, picks `offsets[tile&1]`,
clamped to `max(lightGetTileIntensity, ambient)` at tile.cc:1675-1682) — then
Gouraud-fills **10 triangles** (5 rightside-up + 5 upside-down,
tile.cc:161-176) into a per-pixel `_intensity_map[3280]` (80x36 tile) and
intensity-blits. **Fast path** tile.cc:1684-1697: if all 10 intensities are
equal → flat darken (our current one-tint-per-square IS this path). The fast
path wins whenever ambient >= every sampled light, i.e. bright/daytime maps;
the visual delta exists only in dark interiors / low-ambient maps — which
includes torch-lit **artemple.map, the first map of the game**, so it is
user-visible from minute one (tile-edge banding around light sources).

**MonoGame cost:** faithful port = 10 `VertexPositionColorTexture` verts +
10 tris per visible square, BasicEffect (VertexColorEnabled+TextureEnabled);
GPU Gouraud replaces the triangle rasterizer for free. Needs a per-map floor
**texture atlas** (floor FRMs are all 80x36, small set per map) so the whole
elevation is one draw call; cycled floor tiles (shoreline) re-upload their
atlas region on cycle tick — same mechanism as today's per-FRM uploads.
Static buffer for a full 100x100 elevation = 10k squares x 10 verts x 24 B
≈ 2.4 MB, rebuilt only when LightGrid changes (rare: map load, day/night
re-tint, door light toggles). ~640 visible squares is GPU noise; Q1 shows
~12 ms headroom.

**Honest size: 4-5 days** (atlas 1, vertex-sampling port 1, BasicEffect
pipeline + draw-order weave 1, cycling/re-tint hooks 1, visual verify 1).

**Recommendation: keep as a real phase-7 milestone, do NOT kill.** The
fast-path argument ("delta small") only holds on bright maps; the opening
map is exactly the dark case. Perf is a non-issue (Q1).

## Q3. Combat sfx — name composition + asset verification

**Weapon sounds** — `sfxBuildWeaponName` (game_sound.cc:1374-1447), format
`W%c%c%1d%cXX1`:
- char 2: effect type from `_snd_lookup_weapon_type` (game_sound.cc:83):
  R=ready A=attack O=out-of-ammo F=firing H=hit.
- char 3: weapon `soundCode` byte from the weapon PRO
  (`weaponGetSoundId`, item.cc:1809 — `proto->item.data.weapon.soundCode`).
  e.g. 'J' = pistols, '#'/'!'/'@' = unarmed/melee variants.
- char 4: variant — 1 = primary attack/punch, 2 = secondary
  (game_sound.cc:1385-1396).
- char 5: material code on HIT only: target material → M(glass/metal/
  plastic) W(wood) S(dirt/stone/cement) F(other) X(none/explosion/plasma/
  EMP) (game_sound.cc:1401-1442). Non-hit = 'X'.
**Verified in master.dat** (DatDump): WAJ1XXX1/2 (pistol attack),
WHJ1MXX1 (pistol hit metal), wH#1FXX1/wH#1WXX1 (unarmed hit flesh/wood),
wO#1XXX1 (out of ammo), WF21XXX1 (firing). 1362 ACMs under sound\SFX total.

**Critter pain/death** — `sfxBuildCharName` (game_sound.cc:1318-1352):
6-char FRM base name + 2 anim-code chars from `_art_get_code` (we already
ported this for P2-M1); for FALL_FRONT/FALL_BACK, first code char is forced
to 'Y' (pass-out) or 'Z' (die); melee CONTACT forces 'Z'. **Crucial
fallback** (game_sound.cc:1117-1158): exact name (e.g. HMJMPSZB) rarely
exists → retry `H{M|F}XXXX` + last-2-chars by gender, then HMXXXX. Verified:
HMXXXXZA/ZB (die), HMXXXXYA/YB (pass out), HMXXXXAA, HFXXXXAA exist.

**Effort: S, ~1-1.5 days.** Pieces present: AudioManager + ACM decoder +
SfxName (src/Hexwaste.Formats/Sound/SfxName.cs) + _art_get_code port +
combat completion hooks (damage applied on anim completion already).
Missing: (a) parse weapon `soundCode` — ProtoDatabase.cs weapon block stops
at actionPointCost1; soundCode is a byte further down protoItemDataRead —
extend WeaponProtoStats (+test); (b) `SfxName.Weapon(...)`/`SfxName.Char(...)`
builders + gender fallback chain (+pure unit tests, no game files needed for
name construction); (c) 3 call sites in ViewerGame combat state machine
(attack start → A/F, hit resolution → H, death anim → char Z-name).
High player-facing value per day; recommend as an early phase-7 item.

## Q4. TRANS_* blend LUTs + egg.frm — milestone or footnote?

**What they do:** palette-space translucency. Five 256x16 LUTs built at
startup by `_getColorBlendTable` (color.cc:447) from a key color each —
wall=_colorTable[25439], glass=10239, steam=32767 (white), energy=30689,
red=31744 (object.cc:3467-3471). Render dispatch object.cc:5066-5086:
`_dark_translucent_trans_buf_to_buf(src, ..., light, blendTable, grayTable)`
— for each src pixel take its gray level, then blend the *destination*
pixel toward the key color through the LUT (glass uses its own
`_glassGrayTable`). Flags come from the PROTO `flags` field (NOT map flags):
object.cc:939-957 — 0x8000 none, 0x10000 wall, 0x20000 glass, 0x40000
steam, 0x80000 energy, 0x4000 red.

**Census (probe /tmp/p7c-probe against our ProtoDatabase.Flags, MAP-placed
objects):** artemple 567 obj: 0 trans; arvillag 1836: 0; klamall 3313: 0;
klatoxcv 3658: 0; kladwtwn 3182: 0; denbus1 2839: **1 STEAM**; VCTYCTYD
2077: 0; newr1 3910: 0. One translucent object in ~21k across 8 maps —
VC force fields etc. are script-created mid-game content, out of our slice.

**egg.frm:** art\intrface\egg.frm (FID = interface id 2, intrface.lst line 3
"egg.frm — used for the translucent egg effect around player";
object.cc:352). Single frame, **129x98 feathered ellipse** — pixel values
are per-pixel intensity levels consumed by `_intensity_mask_buf_to_buf`
(object.cc ~5045): solid center fading in ~10 banded steps at the rim. Our
flat 0.45-alpha whole-sprite fade approximates the center correctly and
loses only the soft rim. A faithful no-shader version would require CPU
per-pixel compositing of roof/wall sprites against the mask each frame —
exactly the per-frame re-decode pattern CLAUDE.md forbids.

**Verdict: permanent footnote, both.** Trans LUT objects are statistically
absent from the playable slice; the egg rim feathering is a minor cosmetic
delta with a disproportionate no-shader cost. Document in README known
deviations; do not schedule.

## Q5. play_gmovie — opening-hour usage + cheapest honest treatment

**Script-side calls (scanned 104 .int files: all ac*/ar* Arroyo + dc* Den +
kc* Klamath via tools/int_analyze.py):** exactly ONE script calls
play_gmovie (0x8115) — **ARVILLAG.int map_enter_p_proc**, and the bytecode
arg is `C001 00000003` = constant **3 = MOVIE_VSUIT (vsuit.mve)**
(gMovieFileNames, game_movie.cc:35-52) — the vault-suit cinematic on first
village entry. No other opening-area script touches it.

**Engine-side (not script):** iplogo/intro/credits = main menu
(main.cc:88-108) — we have no main menu, N/A; **elder.mve at new-game start**
(main.cc:114); artimer1-4/afailed from `_scriptsCheckGameEvents`
(scripts.cc:437-485) at day >= ~90/120/150/180 or GVAR_ENEMY_ARROYO — far
outside the opening hour.

**Assets:** movies exist as art\cuts\*.mve (VSUIT.mve 6.7 MB, ELDER.mve
29 MB) — full MVE video decode is a codec port, out of scope. art\stills
does NOT exist in this game data (no caption-card images). BUT
**text\english\cuts\*.sve subtitle files exist** (elder.sve 652 B; vsuit has
none — it is dialog-free).

**Cheapest honest treatment (recommended, ~0.5-1 day):** replace the silent
stub with a "caption card": fade to black, movie title + the .sve subtitle
lines rendered with our existing AAF font stack, click/Esc to continue, set
the movie-seen flag (gameMovieIsSeen semantics — artimer logic checks it).
Needed call sites: play_gmovie external + optionally elder.mve on new game.
Zero new assets, no decoder, clearly labeled as a deviation.

## Q6. Test gaps

### (a) Formats `CombatEngine` extraction — interface sketch, sizing
Today the turn loop lives in ViewerGame.cs (UpdateCombat :2326,
StepEnemyTurn :2352, TryEnemyAction :2384, EnemyAttack :2419 + the
~1500-1900 player-attack path). The MATH is already in Formats
(CombatMath, CritterState, Pathfinder, BarterMath — 114 green tests); what
is NOT testable is the orchestration: phase/round/queue/AP, joiner pull-in,
end conditions, damage-on-animation-completion ordering. Host dependencies
observed in those methods:
```csharp
public interface ICombatPresenter {           // viewer implements
    bool IsBusy(MapObject critter);           // _pendingAttack/_fallingCritters/_npcWalkers gate
    void PlayAttack(MapObject atk, MapObject tgt, AttackResult r); // anim; calls back
    bool StartWalk(MapObject critter, int targetTile);             // StartNpcWalk
    void Log(string msg);  void OnCombatEnded(bool dudeDead);
}
// engine ctor: map objects, Func<MapObject,CritterState?> stats,
//   Func<int,bool> blockedTile, (ProtoInfo?,ProtoInfo?) EquippedWeapon
//   resolver, Random. Presenter notifies AttackAnimationComplete(r) —
//   engine applies damage there (engine rule: roll before animate,
//   damage on completion).
```
**Honest size: M (2-3 days).** The async damage-on-completion contract is
the only tricky part; the rest is a mechanical move. Payoff: the whole AI
turn loop becomes unit-testable with a fake presenter (deterministic Rng).

### (b) Headless barter test sketch (no MonoGame)
Pattern exists: tests/Hexwaste.Formats.Tests/DialogTests.cs builds
GameFileSystem + ProtoDatabase + ScriptHost and drives
ScriptHost.DialogSession on denbus1.map under [GameDataFact]. Barter
plumbing present in Formats: gdialog_barter 0x8129 sets the trade flag +
overwrites modifier (IntVm.cs:1081), gdialog_set_barter_mod 0x814E
(IntVm.cs:1084), session exposes a "picked option called gdialog_barter"
signal (ScriptHost.cs:475), BarterMath.BuyPrice/SellPrice. Test: load
denbus1, StartDialog with Tubby/Flick (Den shopkeeps), walk options until
the barter signal fires; assert (1) signal raised with the script's
modifier, (2) BuyPrice(cost, mod, npcBarter, dudeBarter) matches a
hand-computed item_w_cost case, (3) CapsAdjust round-trips a purchase
(host.CapsTotal before/after). Pure-math twin test (no game data) for the
price formula edge cases (mod negative, skill clamp). ~0.5 day.

### (c) Top-5 phase-7 regression tests
1. **Perf canary**: bench harness assert — newr1 `--bench 500` avg < 8 ms
   (2x headroom over today's 3.7) run nightly/manually; catches per-vertex
   floor or sfx work regressing the frame.
2. **Multi-map persistence round-trip** (highest past-bug density: ordinal
   keying, LVAR import, restock overwrite): enter den→klamath→den, mutate a
   container + kill a critter, save/load, assert deltas survive revisit.
3. **CombatEngine determinism** (after (a)): fixed Rng seed → identical
   round/AP/damage trace on a synthetic 3-critter map; locks the
   roll-before-animate + damage-on-completion ordering.
4. **Sfx name table** (with Q3 work): pure unit tests asserting
   WAJ1XXX1/WHJ1MXX1/wH#1FXX1/HMXXXXZB composition + gender fallback chain
   — names verified present in master.dat today, so also a [GameDataFact]
   asserting vfs.Exists for each derived name.
5. **Script externals smoke**: run map_enter + N heartbeat ticks of
   critter_p_proc across the 8 opening-hour maps headlessly, assert zero
   VM faults and stub-histogram snapshot does not grow new entries
   (catches arity drift when new externals are implemented).
