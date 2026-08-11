# fo2ce combat-AI fidelity: the byte-identical residual batch — design spec (2026-08-11)

Close the `combat_ai.cc` residuals that can be ported **without moving a single golden
fixture**. The scope is engine consistency with `reference/fallout2-ce`, chosen over the
quest-QA frontier and over rendering polish; the RNG-moving half (ring-spiral explosion
damage, `_combat_safety` weapon-switch invalidation, `_ai_danger_source`) is explicitly
deferred to a later re-record tier.

## Grounding corrections to `docs/BACKLOG.md` (verified 2026-08-11)

The backlog's A2 framing was checked line-by-line against the reference. Three fixes:

- **`attack_who` is party-member-only.** `_ai_danger_source` gates its whole attack_who
  switch on `objectIsPartyMember(a1)` (`combat_ai.cc:1544`); a non-party critter takes
  `attackWho = -1` and falls through to the whoHitMe default. Hexwaste applying
  `AttackWho` only to companions is **faithful, not a gap** — do not "fix" it.
- **`_combatai_rating` has more consumers than the backlog lists.** Besides retaliation
  (`combat.cc:4717/4745`), it is the sort key of `_compare_strength` / `_compare_weakness`
  (`combat_ai.cc:1340/1376`) and the candidate filter of
  `_cai_retargetTileFromFriendlyFire` (`:2484/2501`). Hexwaste's companion Strongest/Weakest
  ranks by **HP** (`CompanionAi.cs:125-126`) — an undocumented divergence the backlog misses.
- **Perception disengage is already ported.** `WantsToStopFighting` (`CombatEngine.cs:2094`)
  is a faithful `_combatai_want_to_stop`, perception clause included. The deferred piece is
  `PruneEscapedHostiles` (`:2185`), which uses a flat sight radius. See "Out of scope".

Three behaviors absent from the backlog entirely were also found and are in scope:
`_ai_search_environ` / `_ai_retrieve_object` (ground pickup), `_ai_search_inven_armor`
(companion armor auto-equip — note it lives in the **dialog** seam, `game_dialog.cc:3747`,
not combat), and the arm-crippled / out-of-range `_ai_switch_weapons` triggers.

## Scope — seven items, five commits

### Commit 1 — `_combatai_rating` and its two in-scope consumers

New `src/Hexwaste.Formats/Combat/AiRating.cs`: a pure static
`Score(meleeDamage, armorClass, weaponMaxDamages…)` porting `_combatai_rating`
(`combat_ai.cc:3449`) — `max(STAT_MELEE_DAMAGE, best wielded-weapon max damage) + AC`,
with the guards returning 0 for a non-critter or a `DAM_DEAD | DAM_KNOCKED_OUT` critter.
A `CombatEngine.Rating(MapObject)` wrapper resolves the stats through the existing
`ICombatHost` (`GetCritterState().Stat(…)`, `EquippedWeapon`); **no new host seam**.

Consumers:

- `RegisterHit` (`CombatEngine.cs:1611`) becomes `_combatai_check_retaliation`
  (`combat_ai.cc:3484`): a null `WhoHitMe` is set unconditionally; an existing one is
  replaced **only when `Rating(new) > Rating(existing)`**. Hexwaste's existing team gate
  is retained (the reference's gate lives in its callers).
- `CompanionAi.PickTarget` Strongest/Weakest switch from HP to rating, **preserving
  vanilla's inverted comparators**: `_compare_strength` returns -1 when `rating1 < rating2`,
  so the ascending qsort makes "Strongest" pick the *lowest*-rated target first, and
  "Weakest" the highest. This is a vanilla quirk; port it, do not correct it. Hexwaste's
  stable distance tiebreak stays (its documented substitute for the reference's unstable
  qsort) and is noted in the code comment.

The third consumer (`_cai_retargetTileFromFriendlyFire`) is **out of scope** — it belongs
to the deferred `_combat_safety` work.

### Commit 2 — the decision-logic residuals

- **`_ai_best_weapon` weapon-perk factor** (`combat_ai.cc:1866`): the average-damage score
  doubles when `weaponGetPerk(weapon) != -1`. `WeaponProtoStats.WeaponPerk` (default -1) is
  already parsed, so this is a two-line change in `AiBestWeapon`. The explosive
  `×(extrasLength + 1)` factor stays deferred — it needs `_compute_explosion_on_extras`,
  i.e. the ring-spiral.
- **`aiHaveAmmo` bag search** (`combat_ai.cc:1765`): today ammo availability is approximated
  by the loaded/proto-default round count (`CombatEngine.cs:2378`). New host seam
  `IReadOnlyList<int> CarriedAmmoCalibers(MapObject) => []`; `AiSwitchWeapon` treats ammo as
  available when loaded rounds > 0 **or** a carried caliber matches the weapon's. The empty
  default collapses to exactly today's check.

### Commit 3 — NPC combat-drug timed wear-off

Today `TryNpcUseCombatDrug` (`ViewerGame.CombatHost.cs:524`) applies the stat bonus
immediately and the whole `_npcDrugBonus` map is wiped when combat goes idle
(`ViewerGame.cs:2807`) — so an NPC's Jet buff has no duration. The dude's pipeline already
models this correctly (`ViewerGame.Chemistry.cs:43`: immediate effect, then
`ScheduleDrugEvent(Duration1/Duration2)` down-ramps on the game clock, per
`item.cc _item_d_take_drug` / `_insert_drug_effect`).

Generalize it: `_pendingDrugEvents` entries carry an owner (`null` = dude, else the critter),
`TryNpcUseCombatDrug` schedules the same two down-ramps against that critter's
`_npcDrugBonus` array, and the blanket combat-idle `Clear()` is removed so bonuses expire on
the clock. NPC drug use does **not** roll addiction (the `_item_d_take_drug` addiction tail
is dude-gated at Hexwaste's call site and stays that way).

### Commit 4 — ground pickup (`_ai_search_environ` + `_ai_retrieve_object`)

Two new host seams, both inert by default:

- `IReadOnlyList<(ProtoInfo Proto, MapObject Item)> GroundItemsNear(MapObject critter, int maxDistance) => []`
  — viewer side: item objects on the critter's elevation, distance-sorted, cut at
  `PE + 5` (`combat_ai.cc:2193`).
- `bool TryRetrieveItem(MapObject critter, MapObject item) => false` — viewer side: move
  adjacent, transfer to inventory, charge AP (`actionPickUp` + the engine's turn-run).

Wiring, biped/robotic only (`combat_ai.cc:2004`):

- `AiSwitchWeapon`'s "nothing usable in inventory" fallback searches the ground for a weapon
  it `_ai_can_use_weapon`-qualifies for, retrieves it, and wields it (`:2606-2618`).
- The drug branch searches for `ITEM_TYPE_DRUG`, then `ITEM_TYPE_MISC` (`:1134-1137`).

Vanilla's cross-turn memory (`aiInfoSetLastItem`: an NPC that could not reach the item this
turn resumes toward it next turn) becomes a `Dictionary<MapObject, MapObject> _aiLastItem`
on `CombatEngine`, cleared on combat end and on the item leaving the ground.

### Commit 5 — companion armor auto-equip + the extra weapon-switch triggers

- **`_ai_search_inven_armor`** (`combat_ai.cc:2051`), party-member-only, called from
  `game_dialog.cc:3747` — so it lands in the dialog/party seam, not `CombatEngine`. Score a
  piece as `armorClass + Σ(damageResistance + damageThreshold)` across the 7 damage types;
  when a carried piece beats the worn one, equip it. Reuses the P129 `_armorArtDirty` re-base
  so the companion sprite follows the change.
- **Extra `_ai_switch_weapons` triggers**: the arm-crippled
  (`COMBAT_BAD_SHOT_ARM_CRIPPLED` / `BOTH_ARMS_CRIPPLED`, `combat_ai.cc:2800`) and
  out-of-range-with-no-weapon (`:2823`) branches call the existing `AiSwitchWeapon`. No new
  seam — only the two call sites the port previously left unwired.

## Out of scope (deferred to the re-record tier)

Each of these changes RNG draw order or target selection and would move committed fixtures.
They are deferred deliberately, not forgotten:

- **Ring-spiral explosion damage** (`CombatEngine.cs:1543`) — would move `arcaves-explode`
  and `denbus2-burst-collateral`. Also blocks the `_ai_best_weapon` explosive factor.
- **`_combat_safety_invalidate_weapon`** (`combat.cc:2249`) ally-in-LoF weapon invalidation
  and snipe-back, plus `_cai_retargetTileFromFriendlyFire`.
- **`_ai_danger_source` + perception-based `PruneEscapedHostiles`.** Porting the prune
  properly requires the full `_ai_danger_source` (`combat_ai.cc:1529`) — which *is* enemy
  target selection, so it can move `denbus2-fight-flee` and every multi-hostile fight. The
  P113 note at `CombatEngine.cs:2185` already records why a naive perception prune is wrong
  (a fled hostile retains its `whoHitMe` danger source).
- Everything already closed in the backlog (A1 karma, A3 whoHitMe, A4 poison/rad) and the
  out-of-scope-by-design layer (sfall opcodes, drive-travel, cosmetic Tier C).

## Verification

**Every new host seam ships with a default implementation that reproduces today's behavior**,
so the fake test host and the committed fixtures are inert *by construction*, not by luck.

1. **Unit tests** (`tests/Hexwaste.Formats.Tests`, no game data required):
   `AiRating.Score` including the non-critter and dead/KO → 0 guards; the retaliation rule
   (null → set, higher → replace, lower **and equal** → keep); the inverted Strongest/Weakest
   ordering; `AiBestWeapon` perk ×2; the ammo fallback with an empty vs matching caliber list;
   the armor score sum.
2. **Golden nets**: `scripts/combat-golden.sh check` (16 fixtures) and
   `scripts/encounter-golden.sh` (188), plus the quest, opening and census nets. The
   per-item inertness argument is a **hypothesis to falsify, not an assumption**:
   - Commit 1 needs a contested `WhoHitMe` or a non-default `AttackWho`; the fixtures are
     single-attacker fights at disposition Closest.
   - Commits 2 and 5's trigger sites sit behind the dry-gun / empty-inventory / crippled-arm
     switch paths that the melee fixtures never enter.
   - Commit 3 short-circuits for `chem_use = clean`, which every fixture enemy is.
   - Commit 4's default-empty ground query finds nothing in a fake host.

   If a fixture moves, the item is **escalated to the deferred re-record tier** — it is not
   "fixed" by weakening the port to preserve the fixture.
3. **Live behavioral proof** with real game data, because a byte-identical golden can hide
   an inert feature (the P114 lesson):
   - a new `--ai-pickup-probe` harness flag: disarm an NPC standing near a dropped weapon,
     assert it walks over, picks the weapon up and wields it;
   - an NPC drug decay check: drive a chem-using NPC, then step the game clock and assert the
     bonus ramps back down instead of vanishing at combat end;
   - a companion armor hand-over check in the dialog seam.
4. `dotnet test` in full, plus the app boot smoke (needs a display and `FALLOUT2_DIR`).

## Definition of done

Seven items landed across five conventional commits; all six golden nets byte-identical;
new unit tests green; the three live probes demonstrated on real data; `docs/BACKLOG.md`
updated so the A2 entry reflects what was ported, what was corrected (the three grounding
fixes above), and what remains in the re-record tier.
