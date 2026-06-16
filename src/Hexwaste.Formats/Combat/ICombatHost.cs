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

    // --- Critter / weapon data resolution --------------------------------
    CritterState? GetCritterState(MapObject critter);                    // :1410
    /// <summary>The critter's ai.txt behaviour packet (instance aiPacket, proto
    /// fallback), or null if none / ai.txt absent. Drives min_to_hit + min_hp.</summary>
    AiPacket? GetAiPacket(MapObject critter);
    /// <summary>True once a full in-game day has elapsed — the engine enables
    /// critical hits from "day 2" (random.cc randomTranslateRoll).</summary>
    bool CriticalsEnabled { get; }
    (ProtoInfo? Proto, MapObject? Item) EquippedWeapon(MapObject critter); // :2305
    int WeaponAmmo(ProtoInfo weaponProto, MapObject item);              // :2326
    AmmoProtoStats? LoadedAmmo(ProtoInfo weaponProto, MapObject item);  // :2333
    bool TryReload(MapObject holder, ProtoInfo weaponProto, MapObject item); // :2363

    // --- Spatial queries (the engine decides; the viewer owns the data) --
    /// <summary>_obj_shoot_blocking_at subset for a line-of-fire trace. :2350</summary>
    MapObject? ShootBlockerAt(int tile, MapObject shooter, MapObject target);
    /// <summary>True if a tile blocks movement (Pathfinder predicate). _blockedTiles.Contains.</summary>
    bool IsBlocked(int tile);

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
    bool StartWalk(MapObject critter, int targetTile);                  // :1616 StartNpcWalk
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
    /// <summary>Hit-react FRM (anim 14) on a surviving target. :2494-2498</summary>
    void OnTargetHit(MapObject target);

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
    /// <summary>Drop a fallen follower from the party (PartyMembers + script index + log). :2541</summary>
    void RemovePartyMember(MapObject critter);
    /// <summary>Living party members (for ally turns + nearest-target choice). _scriptHost.PartyMembers.</summary>
    IReadOnlyCollection<MapObject> PartyMembers { get; }
    /// <summary>All living, attackable critters on the current elevation except the
    /// dude — the join-the-fight candidate pool (AddJoiners). _solidObjects[elev].</summary>
    IEnumerable<MapObject> CombatCritters { get; }

    // --- Progression / end-of-combat -------------------------------------
    void AwardXp(int amount);                                           // :2740
    void GameOver();                                                    // :2856

    // --- Output -----------------------------------------------------------
    /// <summary>In-game monitor line (player-visible; NOT in the transcript diff).</summary>
    void Log(string line);
    /// <summary>EXACT stdout line — golden-fixture diffed (Console.WriteLine).</summary>
    void Transcript(string line);
    string ObjectName(MapObject obj);
    string ObjectNameByPid(int pid);
}
