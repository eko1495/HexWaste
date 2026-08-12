using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// The seam between the engine-free <see cref="CombatEngine"/> (phase-9 M0) and
/// the MonoGame viewer. The engine owns the turn-machine STATE and DECISIONS; it
/// reaches everything else — animation, the draw lists, blocking tiles, proto/art
/// VFS, audio, scripts, the dude's progression — through this host.
///
/// Design notes (from docs/phase9-research-report.md §M0 + the adversarial audit):
/// * The viewer keeps single ownership of `_animator`, `_npcWalkers`, the
///   `_solidObjects`/`_flatObjects` draw lists and `_blockedTiles`. The engine
///   never caches them — it asks the host fresh each time (so the walker
///   `TileChanged` mutations inside <see cref="StartWalk"/> stay correct without
///   an explicit notification callback).
/// * Every method maps 1:1 to a verified `ViewerGame.cs` behaviour; the comments
///   carry the original line for the behaviour-preservation checklist.
/// * <see cref="Transcript"/> = the EXACT stdout line (golden-fixture diff);
///   <see cref="Log"/> = the in-game monitor line (player-visible, not stdout).
/// </summary>
public interface ICombatHost
{
    // --- The dude ---------------------------------------------------------
    /// <summary>The player critter, or null if no map is loaded.</summary>
    MapObject? Dude { get; }
    /// <summary>Stop the dude's current walk (ambush interrupt). ViewerGame _dude.Stop().</summary>
    void StopDude();
    /// <summary>The dude's over-encumbrance max-AP penalty for this turn (P24; stat.cc:198 — 1 AP
    /// per 40 lbs over the carry limit, +1). 0 when within capacity. Default 0 so non-viewer hosts
    /// (the combat unit tests) need no inventory model.</summary>
    int DudeEncumbranceApPenalty() => 0;
    /// <summary>The dude's rank in a perk (P28-M3 combat effects: Bonus Rate of Fire, Sniper,
    /// Slayer, Sharpshooter, …). Default 0 — a perk-less dude is inert, so goldens hold.</summary>
    int DudePerkRank(int perk) => 0;
    /// <summary>True if the dude selected this optional trait (P29-M1 combat-path effects: One Hander,
    /// Fast Shot, Finesse, Jinxed — TraitModifiers ids). Default false — a trait-less dude is inert,
    /// so the combat goldens stay byte-identical. The engine's trait checks are all <c>== gDude</c>.</summary>
    bool DudeHasTrait(int trait) => false;
    /// <summary>The dude's sneaking FLAG (dudeHasState DUDE_STATE_SNEAKING; P30 A-M1). Drives the
    /// Silent Death backstab gate (combat.cc:3872 reads the flag, NOT active _sneak_working). Default
    /// false — a non-sneaking dude is inert, so the combat goldens stay byte-identical.</summary>
    bool DudeSneakFlag => false;

    /// <summary>The Easy/Normal/Hard combat-difficulty damage modifier as a percentage (75/100/125),
    /// applied by <see cref="CombatEngine"/> to damage dealt by attackers NOT on the dude's team
    /// (combat.cc:4554). Default 100 — the fake test host has no difficulty setting and the dude/allies
    /// always deal 100%, so a 100 modifier is identity and the combat goldens stay byte-identical. P84.</summary>
    int CombatDifficultyDamageModifier => 100;

    // --- Critter / weapon data resolution --------------------------------
    CritterState? GetCritterState(MapObject critter);                    // :1410
    /// <summary>The critter's ai.txt behaviour packet (instance aiPacket, proto
    /// fallback), or null if none / ai.txt absent. Drives min_to_hit + min_hp.</summary>
    AiPacket? GetAiPacket(MapObject critter);
    /// <summary>True once a full in-game day has elapsed — the engine enables
    /// critical hits from "day 2" (random.cc randomTranslateRoll).</summary>
    bool CriticalsEnabled { get; }

    /// <summary>True once 6 in-game days have elapsed — the engine suppresses the DUDE's critical-
    /// FAILURE EFFECT until day 6 (combat.cc:4190; the trigger still fires from day 2). Non-dude
    /// fumbles have no such gate. Default false (P41; the fake host has no clock).</summary>
    bool DudeCritFailuresEnabled => false;
    (ProtoInfo? Proto, MapObject? Item) EquippedWeapon(MapObject critter); // :2305
    /// <summary>The critter's CARRIED weapon items (proto + item), EXCLUDING the one currently in
    /// hand — the AI inventory-weapon-switch candidate pool (_ai_search_inven_weap, combat_ai.cc:2002).
    /// Default empty so the fake test host stays inert (no inventory model) and the combat goldens
    /// hold (the golden-fight critters carry no weapons). P43.</summary>
    IReadOnlyList<(ProtoInfo Proto, MapObject Item)> CritterInventoryWeapons(MapObject critter) => [];
    /// <summary>ported from fallout2-ce src/combat_ai.cc aiHaveAmmo (:1765): the CALIBERS of every ammo
    /// item the critter carries, so a ranged weapon with an empty magazine still counts as usable when
    /// matching ammo is in the bag. Default empty — with no carried ammo the caller falls back to the
    /// loaded-round count, which is exactly the pre-port behaviour (so the fixtures stay inert).</summary>
    IReadOnlyList<int> CarriedAmmoCalibers(MapObject critter) => [];
    /// <summary>Wield a carried weapon (clear the old hand flag, set the new) — the AI weapon switch
    /// equips its best inventory weapon (_inven_wield, combat_ai.cc:2623). Default no-op (P43).</summary>
    void EquipWeapon(MapObject critter, MapObject weaponItem) { }
    int WeaponAmmo(ProtoInfo weaponProto, MapObject item);              // :2326
    AmmoProtoStats? LoadedAmmo(ProtoInfo weaponProto, MapObject item);  // :2333
    bool TryReload(MapObject holder, ProtoInfo weaponProto, MapObject item); // :2363

    // --- Spatial queries (the engine decides; the viewer owns the data) --
    /// <summary>_obj_shoot_blocking_at subset for a line-of-fire trace. :2350</summary>
    MapObject? ShootBlockerAt(int tile, MapObject shooter, MapObject target);
    /// <summary>True if a tile blocks movement (Pathfinder predicate). _blockedTiles.Contains.</summary>
    bool IsBlocked(int tile);

    // --- P113 (Stage 0): perception/light/door/hidden-item host data ---
    /// <summary>The dude's Sneak skill value (skillGetValue(gDude, SKILL_SNEAK) — the target-side term
    /// of isWithinPerception, combat_ai.cc:3499). Default 0 (fake host has no dude).</summary>
    int DudeSneakSkill => 0;
    /// <summary>dudeIsSneaking(): the sneak flag is on AND the last roll succeeded. Default false.</summary>
    bool DudeIsActivelySneaking => false;
    /// <summary>The sneak FLAG (set but possibly not working — the ×2/3 tier). Default false.</summary>
    bool DudeHasSneakFlag => false;
    /// <summary>Light intensity (0..65536) at the critter's tile — objectGetLightIntensity
    /// (object.cc:1748) for the darkness to-hit modifier (combat.cc:4446-4463). Default full
    /// brightness so the fake host and pre-P113 behavior see no darkness penalty.</summary>
    int LightIntensityAt(MapObject critter) => 65536;
    /// <summary>A closed door at the tile that THIS mover may path through and open — the pathfinder's
    /// canUseDoor exemption (animation.cc:1802-1808) for combat movement. Default false (door-blind).</summary>
    bool IsPassableClosedDoor(MapObject mover, int tile) => false;
    /// <summary>ITEM_HIDDEN (proto item extendedFlags 0x08000000, item.cc:1133) — natural-weapon
    /// items destroyed on death (itemDestroyAllHidden, combat.cc:4858). Default false.</summary>
    bool ItemIsHidden(MapObject item) => false;

    // --- Animation-as-turn-clock (combat.cc:5322/5334; damage at zero :5363) ---
    /// <summary>An attack/action animation is still playing for this critter
    /// (engine: _combat_turn_running &gt; 0 for the actor). _animator has state.</summary>
    bool IsAnimating(MapObject critter);
    /// <summary>A death-fall animation is still in progress (not yet Finished).</summary>
    bool IsFallInProgress(MapObject critter);
    /// <summary>Any NPC walker is mid-move. _npcWalkers.Values.Any(Moving).</summary>
    bool IsAnyWalkerMoving();
    /// <summary>This specific critter's walker is mid-move.</summary>
    bool IsWalkerMoving(MapObject critter);

    // --- Movement (StartNpcWalk + its draw-list/blocking closure stay here) ---
    /// <summary>run = request ANIM_RUNNING; the host still falls back to walk when the
    /// critter has no run art or a crippled leg (animation.cc:753-758). (P117.)</summary>
    bool StartWalk(MapObject critter, int targetTile, bool run = false); // :1616 StartNpcWalk
    /// <summary>The critters.lst run flag (art.cc artCritterFidShouldRun) — gates the AI
    /// approach's run request (combat_ai.cc:2424). (P117.)</summary>
    bool CritterShouldRun(MapObject critter);
    /// <summary>Instantly relocate a critter (knockback shove): set its tile and
    /// re-sort the draw list + blocking. No animation.</summary>
    void PlaceCritter(MapObject critter, int tile);

    // --- Throwing (M4) ----------------------------------------------------
    /// <summary>Play the thrower's throw animation + the projectile flight to the
    /// tile. IsAnimating(thrower) gates the landing, like a melee swing.</summary>
    void OnThrowStarted(MapObject thrower, int targetTile, ProtoInfo weaponProto);
    /// <summary>Remove the thrown weapon from the thrower's hand (it has left).</summary>
    void RemoveFromHand(MapObject thrower, MapObject item);
    /// <summary>Drop a non-explosive thrown weapon on the ground at a tile,
    /// recoverable (reuses the created-object delta machinery).</summary>
    void DropThrownWeapon(MapObject item, int tile);
    /// <summary>Spawn the misc-10 explosion marker at a tile so metarule(49) and
    /// nearby damage_p_proc see an EXPLOSION source (the temple-door path).</summary>
    void SpawnExplosionMarker(int tile);
    /// <summary>reg_anim_clear: drop a pending animation + stop/forget a walker. :2231-2236</summary>
    void ClearAnimation(MapObject critter);

    // --- Attack choreography ---------------------------------------------
    /// <summary>Muzzle/punch FRM + weapon sfx (PlayWeaponSfx + StartAttackAnimation),
    /// and a flying projectile attacker→target for ranged/thrown shots (phase-10 #11).</summary>
    void OnAttackStarted(MapObject attacker, MapObject target, ProtoInfo? weaponProto);
    /// <summary>Hit reaction on a surviving target (actions.cc:424 _show_damage_to_object): a hit-from-
    /// front/back FRM, or a FALL when <paramref name="knockedDown"/>. The host picks the anim by facing
    /// (attacker vs target rotation) + art existence (P34-M6). :2494-2498</summary>
    void OnTargetHit(MapObject target, MapObject attacker, bool knockedDown);

    /// <summary>Dodge reaction on a MISS for a non-prone defender (actions.cc:906 _action_ranged /
    /// _action_melee). Default no-op so the fake test host needs no override (P34-M6).</summary>
    void OnTargetDodge(MapObject target) { }

    /// <summary>Stand-up animation when a prone critter gets up (animation.cc:3182 _dude_standup) —
    /// the logical AP/prone-clear already ran in StandUpIfProne. Default no-op (P34-M6).</summary>
    void OnGetUp(MapObject critter) { }

    /// <summary>Out-of-ammo on an attack attempt (combat.cc:5745) — the host may play the empty-click
    /// sfx. Default no-op so the fake test host needs no override (P34-M5).</summary>
    void OnWeaponOutOfAmmo(ProtoInfo weaponProto) { }

    /// <summary>A critter is fleeing this turn (combat_ai.cc _ai_run_away → AI_MESSAGE_TYPE_RUN taunt,
    /// combat_ai.cc:1209). Default no-op; the viewer floats a flee taunt (P72-M3). Draw-only.</summary>
    void OnCritterFlee(MapObject critter) { }

    // --- Death + corpse ---------------------------------------------------
    /// <summary>Resolve the gory death anim the combat picked (P26 DeathAnims.Pick) against the
    /// critter's available art (actions.cc _check_death): the desired gore anim if it ships, else
    /// FALL_BACK, else FALL_FRONT.</summary>
    int PickDeathAnim(MapObject critter, int desiredAnim);
    /// <summary>Death scream + start the fall animation; returns true if a fall is
    /// playing (caller waits), false if no fall art (convert immediately). :2541-2551</summary>
    bool StartDeathFall(MapObject critter, int deathAnim);
    /// <summary>critterKill corpse conversion: anim+28 FID, NO_BLOCK+flat, draw-list
    /// move across all elevations, then rebuild blocking (FinishCorpse). :2585</summary>
    void ConvertToCorpse(MapObject critter, int deathAnim);
    /// <summary>Forget bookkeeping for a dead critter (home tile, walker). :2549-2550</summary>
    void OnCritterRemoved(MapObject critter);

    // --- Scripts (damage_p_proc / destroy_p_proc) ------------------------
    /// <summary>Run damage_p_proc; returns monitor lines. :2496</summary>
    IReadOnlyList<string> RunDamageProc(MapObject target, MapObject? source, int damage);
    /// <summary>Run destroy_p_proc; Overridden = script_overrides (no default XP). :2527</summary>
    (IReadOnlyList<string> Lines, bool Overridden) RunDestroyProc(MapObject critter, MapObject? killer);
    /// <summary>Run a combat_p_proc hook (SCRIPT_PROC_COMBAT). fixedParam=4 is the per-turn hook
    /// (combat.cc:3247, <paramref name="target"/> null); fixedParam=2 is the on-hit hook
    /// (combat.cc:4730, <paramref name="target"/> = the struck defender). Overridden = script_overrides()
    /// — on the per-turn hook that forfeits the critter's default turn. Default ([], false) so the fake
    /// test host needs no override (P35).</summary>
    (IReadOnlyList<string> Lines, bool Overridden) RunCombatProc(MapObject critter, int fixedParam, MapObject? target = null) => ([], false);
    /// <summary>P100 (Point 3): the "combat over / dude knocked out" hook, ported from fallout2-ce
    /// src/scripts.cc:2848 _scr_end_combat(): run the MAP script's combat_p_proc with fixedParam =
    /// <paramref name="knockedOutByTeam"/> and return whether it script_overrides (the ring catches the KO
    /// → end combat instead of a game-over). Default false so the fake test host is unaffected.</summary>
    bool RunMapCombatOver(int knockedOutByTeam) => false;
    /// <summary>Drop a fallen follower from the party (PartyMembers + script index + log). :2541</summary>
    void RemovePartyMember(MapObject critter);
    /// <summary>Living party members (for ally turns + nearest-target choice). _scriptHost.PartyMembers.</summary>
    IReadOnlyCollection<MapObject> PartyMembers { get; }
    /// <summary>All living, attackable critters on the current elevation except the
    /// dude — the join-the-fight candidate pool (AddJoiners). _solidObjects[elev].</summary>
    IEnumerable<MapObject> CombatCritters { get; }

    // --- Progression / end-of-combat -------------------------------------
    void AwardXp(int amount);                                           // :2740

    /// <summary>Tally a dude/team kill by the victim's KILL_TYPE (killsIncByType, critter.cc:702;
    /// combat.cc:4870 — beside the XP award, same gating). Default no-op (P38); the fake host has
    /// no kill tracker.</summary>
    void RecordKill(MapObject victim) { }

    /// <summary>An NPC quaffs ONE healing item from its inventory (the AI's _ai_check_drugs heal,
    /// combat_ai.cc:999): find a healing drug, apply its heal to the critter, consume it; return whether
    /// it healed (P42). Default false (the fake host has no inventory/proto model).</summary>
    bool TryNpcHeal(MapObject critter) => false;

    /// <summary>An NPC quaffs ONE non-healing combat drug (Jet/Psycho/Buffout) from its inventory to buff
    /// itself mid-fight (_ai_check_drugs non-heal branch, combat_ai.cc:1028): pick a chem_primary_desire
    /// drug, apply its immediate stat effect to the critter, consume it; return whether it drank one
    /// (P78-M2). Default false (the fake host has no inventory/proto model).</summary>
    bool TryNpcUseCombatDrug(MapObject critter, int[]? primaryDesire) => false;

    /// <summary>The companion's combat-control settings (P50 AI-disposition window, game_dialog.cc:3354).
    /// Default = the pre-P50 ally behaviour (Aggressive: attack the nearest hostile, never flee, no
    /// distance constraint), so an un-configured ally + the fake test host stay byte-identical.</summary>
    CompanionAi CompanionSettings(MapObject ally) => CompanionAi.Default;

    void GameOver();                                                    // :2856

    // --- Output -----------------------------------------------------------
    /// <summary>In-game monitor line (player-visible; NOT in the transcript diff).</summary>
    void Log(string line);
    /// <summary>EXACT stdout line — golden-fixture diffed (Console.WriteLine).</summary>
    void Transcript(string line);
    string ObjectName(MapObject obj);
    string ObjectNameByPid(int pid);
}
