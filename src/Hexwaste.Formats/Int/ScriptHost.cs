using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Text;

namespace Hexwaste.Formats.Int;

/// <summary>The kind of a queued reg_anim action (interpreter_extra.cc opRegAnim*).</summary>
public enum RegAnimKind
{
    MoveToTile,
    RunToTile,
    MoveToObject,
    RunToObject,
    Animate,
    AnimateReverse,
}

/// <summary>
/// One registered reg_anim action, resolved to host objects at registration time.
/// <see cref="Tile"/> is meaningful for the tile variants; <see cref="Dest"/> for the
/// object variants; <see cref="Anim"/> for the animate variants. The engine plays a
/// batch sequentially over time; the host's executor is free to simplify (P33-M1).
/// </summary>
public sealed record RegAnimAction(RegAnimKind Kind, MapObject Object, int Tile, MapObject? Dest, int Anim, int Delay);

/// <summary>
/// Runs object scripts in the micro INT VM with the engine's script-context
/// protocol (phase-4 M0): object handle table, source/target/dude context,
/// LVAR slices (lazily allocated zeroed per script like map.cc
/// _map_malloc_local_var — pristine maps store offset -1), MVARs into the
/// map's global block, and session-level GVARs. Any VM failure falls back to
/// non-scripted behavior — scripts are an enhancement, never a crash.
/// </summary>
public sealed class ScriptHost(GameFileSystem vfs, ScriptList scripts, Hexwaste.Formats.Proto.ProtoDatabase protos)
{
    private readonly Dictionary<string, IntProgram?> _programs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, MessageFile?> _dialogMessages = [];
    private readonly Dictionary<int, int> _globalVars = [];

    /// <summary>Lazily allocated LVAR slices per (map NAME, sid) — the engine
    /// appends zeroed slices to the map array on first access
    /// (scripts.cc:2805/2836). Keyed by the header map name so slices survive
    /// pristine reloads (in-session persistence) and dead MapFile instances
    /// are never pinned (the phase-5 measured leak).</summary>
    private readonly Dictionary<(string Map, int Sid), int[]> _localVarSlices = [];

    // Object handle table: scripts see objects as opaque ints; 0 = null.
    private readonly List<MapObject> _handles = [];
    private readonly Dictionary<MapObject, int> _handleByObject = [];

    public int HandleOf(MapObject? obj)
    {
        if (obj is null)
            return 0;
        if (_handleByObject.TryGetValue(obj, out int handle))
            return handle;
        _handles.Add(obj);
        handle = _handles.Count; // 1-based
        _handleByObject[obj] = handle;
        return handle;
    }

    public MapObject? ObjectOf(int handle) =>
        handle >= 1 && handle <= _handles.Count ? _handles[handle - 1] : null;

    /// <summary>
    /// critter_state's pure bitfield mapping — the single source of truth for both the VM external
    /// (ScriptContext.CritterState) and the --critter-state-probe.
    /// ported from fallout2-ce src/interpreter_extra.cc opGetCritterState() (0x80FB):
    /// DEAD(1) for null/non-critter/dead; an ACTIVE critter → NORMAL(0) | PRONE(2 if mid-fall-anim)
    /// | DAM_CRIP bits; an inactive-but-alive critter (knocked-out / lose-turn) → PRONE(2).
    /// </summary>
    public static int CritterStateOf(MapObject? c)
    {
        const int Dead = 0x01, Normal = 0x00, Prone = 0x02;
        if (c is null || Fid.PidType(c.Pid) != 1) // OBJ_TYPE_CRITTER == 1
            return Dead;
        // critterIsActive (critter.cc:942): not knocked-out / lose-turn / dead — same mask as CombatEngine.CanAct.
        bool active = (c.CombatResults & (CriticalTables.DamKnockedOut | CriticalTables.DamLoseTurn | CriticalTables.DamDead)) == 0;
        if (active)
        {
            // ANIM_FALL_BACK_SF(48)..ANIM_FALL_FRONT_SF(49) = lying prone but conscious.
            int state = Fid.AnimType(c.Fid) is >= 48 and <= 49 ? Prone : Normal;
            return state | (c.CombatResults & CriticalTables.DamHealable); // DamHealable == engine DAM_CRIP (0x7C)
        }
        return c.IsDead ? Dead : Prone; // inactive: dead → DEAD, knocked-out/lose-turn alive → PRONE
    }

    /// <summary>Resolves object names for the VM (set by the host application).</summary>
    public Func<MapObject, string>? NameResolver { get; set; }

    /// <summary>Resolves whether a door/container is currently open (set by the host).</summary>
    public Func<MapObject, bool>? IsOpenResolver { get; set; }

    /// <summary>Applies a script-driven open/close (animate, unblock — set by the host).</summary>
    public Action<MapObject, bool>? OpenStateChanged { get; set; }

    /// <summary>Diagnostic sink for arity-stubbed externals.</summary>
    public Action<string>? OnStubbedExternal { get; set; }

    /// <summary>An object was placed on the map by a script (add to draw lists/blocking).</summary>
    public Action<MapObject, MapFile>? ObjectPlaced { get; set; }

    /// <summary>An object was removed from the map by a script.</summary>
    public Action<MapObject>? ObjectRemoved { get; set; }

    /// <summary>The prototype database (item icons, fids for created objects).</summary>
    public Hexwaste.Formats.Proto.ProtoDatabase Protos => protos;

    /// <summary>P113 (item 7b): lazy proto name/description text (pro_*.msg) for the proto_data
    /// STRING members.</summary>
    public Text.ProtoMessages ProtoText => _protoText ??= new Text.ProtoMessages(vfs, protos);
    private Text.ProtoMessages? _protoText;

    /// <summary>Game clock backing the game_time externals (host-provided).</summary>
    public Func<long>? ClockTicks { get; set; }

    /// <summary>maps.txt index of the current map (cur_map_index; host-provided).</summary>
    public Func<int>? CurrentMapIndexProvider { get; set; }

    /// <summary>Sink for messages produced outside interactive runs (timer float text).</summary>
    public Action<string>? OnScriptMessage { get; set; }

    /// <summary>A script requested a walk animation (animate_move_obj_to_tile).</summary>
    public Action<MapObject, int>? MoveRequested { get; set; }

    /// <summary>set_light_level: the host sets the global ambient light (0-100%, 50=cavern).</summary>
    public Action<int>? LightLevelRequested { get; set; }

    /// <summary>P53: a dialogue reply resolved a message-list entry (messageListId, messageId) — the host
    /// looks up its audio field via <see cref="LookupAudio"/> and plays the speech file if non-empty.</summary>
    public Action<int, int>? DialogVoiceRequested { get; set; }

    /// <summary>obj_set_light_level: the host sets a per-object light pool (obj, intensity
    /// 0-100%, radius).</summary>
    public Action<MapObject, int, int>? ObjectLightRequested { get; set; }

    /// <summary>reg_anim_animate_forever: the host loops anim code on the object.</summary>
    public Action<MapObject, int>? AnimateForeverRequested { get; set; }

    /// <summary>P54-M2: elevation(obj) — the host resolves an object's map elevation (0..2).</summary>
    public Func<MapObject, int>? ElevationProvider { get; set; }

    /// <summary>P54-M2: anim(obj, code) — the host plays a one-shot animation code on the object.</summary>
    public Action<MapObject, int>? AnimRequested { get; set; }

    /// <summary>P56-M2: set_map_start(x, y, elevation, rotation) — the host repositions the dude/camera.</summary>
    public Action<int, int, int, int>? SetMapStartRequested { get; set; }

    /// <summary>P56-M2: kill_critter_type(pid, deathFrame) — the host destroys live critters of a proto.</summary>
    public Action<int, int>? KillCritterTypeRequested { get; set; }

    /// <summary>P0 (campaign port): kill_critter(object, deathFrame) — the host destroys a specific critter.</summary>
    public Action<MapObject, int>? KillCritterRequested { get; set; }

    /// <summary>P0 (campaign port): critter_rm_trait(PERK) — the host removes a perk from the dude.</summary>
    public Action<int>? PerkRemoveRequested { get; set; }

    /// <summary>P0 (campaign port): use_obj_on_obj — the host runs the use_obj_on_p_proc chain (item, target).</summary>
    public Action<MapObject, MapObject>? UseObjOnObjRequested { get; set; }

    /// <summary>P0 (campaign port): critter_mod_skill(skill, points) — the host adds skill points to the
    /// dude's skill (tagged-halved, value-capped). Dude-only, so no object is passed.</summary>
    public Action<int, int>? CritterModSkillRequested { get; set; }

    /// <summary>P0 (campaign port): scripts_request_world_map — the host leaves to the worldmap.</summary>
    public Action? WorldMapRequested { get; set; }

    /// <summary>P0 (campaign port): wm_area_set_pos(city, x, y) — the host moves a worldmap area marker.</summary>
    public Action<int, int, int>? WmAreaSetPosRequested { get; set; }

    /// <summary>P0 (campaign port): the object a dialogue_system_enter (0x80F9) call requested a dialog with,
    /// set during a use_p_proc run and consumed by the viewer right after (it opens the speaker's
    /// talk_p_proc). Null when no dialog was requested. The viewer resets it per interaction.</summary>
    public MapObject? PendingDialogSpeaker { get; set; }

    /// <summary>P0 (campaign port): load_map(mapIndex) — the host defers a transition to that map.</summary>
    public Action<int>? LoadMapRequested { get; set; }

    /// <summary>P0 (campaign port): resolve a load_map(string) map FILE name to its maps.txt index, or -1.</summary>
    public Func<string, int>? MapIndexByNameProvider { get; set; }

    /// <summary>P0 (campaign port): attack_setup(attacker, defender) — the host starts combat with the
    /// attacker as the aggressor (dude-target → script-aggro; NPC-vs-NPC → a spectated brawl).</summary>
    public Action<MapObject, MapObject>? AttackSetupRequested { get; set; }

    /// <summary>P0 (campaign port): explosion(tile, elevation, minDamage, maxDamage) — the host detonates an
    /// environmental blast (script trap, reactor meltdown, etc.) on that tile/elevation.</summary>
    public Action<int, int, int, int>? ExplosionRequested { get; set; }

    /// <summary>P100 (Point 1): endgame_slideshow — the host runs the victory-ending slideshow + credits
    /// (the win condition). Null until the viewer wires it.</summary>
    public Action? EndgameSlideshowRequested { get; set; }

    /// <summary>P100 (Point 1): endgame_movie — the host runs the endgame "movie" (credits) directly.</summary>
    public Action? EndgameMovieRequested { get; set; }

    /// <summary>P101 (bucket 1b): game_ui_disable(false)/enable(true) — lock/unlock the player interface for a
    /// scripted cutscene (New Reno prizefight rounds). Null until the viewer wires it.</summary>
    public Action<bool>? GameUiEnabledRequested { get; set; }

    /// <summary>P57: set_exit_grids(elevation, destMap, destElevation, destTile) — the host retargets
    /// the exit-grid objects on an elevation.</summary>
    public Action<int, int, int, int>? SetExitGridsRequested { get; set; }

    /// <summary>P57: wield_obj_critter(critter, item) — the host equips the item on the critter.</summary>
    public Action<MapObject, MapObject>? WieldObjCritterRequested { get; set; }

    /// <summary>P58: mark_area_known(markType, areaId, mode) — the host reveals a worldmap area.</summary>
    public Action<int, int, int>? MarkAreaKnownRequested { get; set; }

    /// <summary>P58: game_time_advance(ticks) — the host advances the clock + runs the tick catch-up.</summary>
    public Action<int>? GameTimeAdvanceRequested { get; set; }

    /// <summary>P63: tile_contains_obj_pid(tile, elevation, pid) — 1 if a matching object is at the tile.</summary>
    public Func<int, int, int, bool>? TileContainsObjPidProvider { get; set; }

    /// <summary>P63: animate_stand_reverse_obj(obj) — the host plays the object's stand anim.</summary>
    public Action<MapObject>? AnimateStandReverseRequested { get; set; }

    /// <summary>P101 (Tier A): gfade_out/in — the host fades the screen to/from black (true = fade in).</summary>
    public Action<bool>? ScreenFadeRequested { get; set; }

    /// <summary>P101 (Tier A): play_sfx / reg_anim_play_sfx — the host plays a named sound effect.</summary>
    public Action<string>? PlaySfxRequested { get; set; }

    /// <summary>P101 (Tier A): animate_stand_obj(obj) — the host plays the object's idle stand anim.</summary>
    public Action<MapObject>? AnimateStandRequested { get; set; }

    /// <summary>P101 (Tier B): tile_is_visible — the camera-centre tile (viewer camera). Null headless → 0.</summary>
    public Func<int>? CenterTileProvider { get; set; }

    /// <summary>P101 (Tier C): inven_unwield — the host holsters the critter's wielded weapon.</summary>
    public Action<MapObject>? InvenUnwieldRequested { get; set; }

    /// <summary>P101 (Tier C): use_obj(obj) — the host runs the object's use_p_proc.</summary>
    public Action<MapObject>? UseObjRequested { get; set; }

    /// <summary>P101 (Tier C): drop_obj(dropper, item) — the host drops the item from the dropper's inventory.</summary>
    public Action<MapObject, MapObject>? DropObjRequested { get; set; }

    /// <summary>P101 (Tier D): radiation_inc/dec(obj, ±amount) — the host adjusts the dude's radiation.</summary>
    public Action<MapObject, int>? RadiationRequested { get; set; }

    /// <summary>True during a save/load replay — gates kill_critter_type (interpreter_extra.cc:2384).</summary>
    public Func<bool>? IsLoadingGameProvider { get; set; }
    public bool IsLoadingGame() => IsLoadingGameProvider?.Invoke() ?? false;

    /// <summary>reg_anim_func END: the host plays a flushed batch of queued reg_anim
    /// actions (moves/animations). (P33-M1.)</summary>
    public Action<IReadOnlyList<RegAnimAction>>? RegAnimRequested { get; set; }

    /// <summary>reg_anim_func CLEAR: the host cancels the object's animation/walk.</summary>
    public Action<MapObject>? RegAnimClearRequested { get; set; }

    /// <summary>Stat-block override (the dude's gcd sheet); null falls back to
    /// the critter's prototype.</summary>
    public Func<MapObject, Proto.CritterProtoStats?>? StatsResolver { get; set; }

    /// <summary>Effective skill % for has_skill (skill.cc skillGetValue) — the viewer wires it to the
    /// full CritterState.SkillValue (gcd skills + tags + perk/trait mods). Null falls back to the
    /// simplified proto skill set (no tags), like CritterStatValue. P74-M3.</summary>
    public Func<MapObject, int, int>? SkillResolver { get; set; }

    /// <summary>A script attacked: (attacker = the script's self, target).
    /// The host starts/joins combat (opAttackComplex → scriptsRequestCombat).</summary>
    public Action<MapObject, MapObject>? AttackRequested { get; set; }

    /// <summary>P113 (Stage 3): obj_can_see_obj — the host answers with isWithinPerception + a clear
    /// sight path (the viewer owns positions/facing/light). Null (headless tools) → the flat-20
    /// fallback, so DatDump/ProcAnalyze/--census stay identical.</summary>
    public Func<MapObject, MapObject, bool>? ObjCanSeeResolver { get; set; }
    /// <summary>P113 (Stage 3): obj_can_hear_obj — isWithinPerception only (no sight path).</summary>
    public Func<MapObject, MapObject, bool>? ObjCanHearResolver { get; set; }

    /// <summary>P113 (item 5): a script requested an elevator via metarule(15) — (the requesting
    /// object's tile, the script-supplied elevator type). The viewer consumes it after the pump: it
    /// scans for an elevator-stub scenery near the tile (which overrides type+level) and opens the
    /// level picker. Null when idle.</summary>
    public (int SelfTile, int RequestedType)? PendingElevator { get; set; }

    /// <summary>P113 (item 5): the party's current worldmap area id (wmGetPartyCurArea) for
    /// metarule(46). Null → 0 (the direct-map/harness default).</summary>
    public Func<int>? CurrentTownProvider { get; set; }

    /// <summary>critter_attempt_placement: relocate (obj, tile, elevation) to that tile (or a free tile
    /// near it), re-sorting the host's draw lists + blocking. Returns true on success. (P32 reg-anim/
    /// placement.)</summary>
    public Func<MapObject, int, int, bool>? PlaceObjectRequested { get; set; }

    /// <summary>anim_busy: is this object mid-animation (host animator)?</summary>
    public Func<MapObject, bool>? AnimBusyResolver { get; set; }

    /// <summary>give_exp_points: the host adds XP immediately (pcAddExperience).</summary>
    public Action<int>? ExpAwarded { get; set; }

    /// <summary>override_map_start: (tile, elevation, rotation) — the host
    /// repositions the dude + camera during map_enter.</summary>
    public Action<int, int, int>? MapStartOverridden { get; set; }

    /// <summary>play_gmovie: the host shows a caption card for the movie id.</summary>
    public Action<int>? MoviePlayed { get; set; }

    /// <summary>critter_damage: (victim, amount, bypassArmor) — the host
    /// applies HP loss and the death path.</summary>
    public Action<MapObject, int, bool>? CritterDamaged { get; set; }

    /// <summary>The party roster (engine party.cc list, minimum cut): scripts
    /// add/remove; party_member_obj answers by pid; the host carries members
    /// across maps.</summary>
    public List<MapObject> PartyMembers { get; } = [];

    /// <summary>metarule(16) PARTY_COUNT, ported from _getPartyMemberCount
    /// (party_member.cc:900): slot 0 = the dude (always +1), plus each LIVE, VISIBLE,
    /// recruited critter (dead/hidden/non-critter members don't count). Static so the
    /// roster-count logic is unit-testable without a VM.</summary>
    public static int PartyMemberCount(IReadOnlyList<MapObject> members) =>
        1 + members.Count(m => Fid.PidType(m.Pid) == (int)ObjectType.Critter && !m.IsDead && !m.IsHidden);

    /// <summary>A script recruited (true) or dismissed (false) this critter.</summary>
    public Action<MapObject, bool>? PartyChanged { get; set; }

    /// <summary>Runtime sid for a script-created object (engine scr_new): a
    /// fresh type-3 sid registered into the map's script table.</summary>
    public int AllocateSid(MapFile map, int scriptIndex)
    {
        int sid = 0x03000000 | 0x00800000; // synthetic range, clear of map sids
        while (map.ScriptsBySid.ContainsKey(sid))
            sid++;
        map.ScriptsBySid[sid] = new MapScriptRecord(scriptIndex, -1, 0);
        // P108: the MapFile (and its ScriptsBySid) is re-parsed on every map load, so this scan
        // restarts and RECYCLES synthetic sids across visits — but _localVarSlices persists per map
        // NAME. Clear any stale slice a previous visit left under this key, or the new holder
        // inherits another object's local vars (fo2ce frees LVAR blocks on scriptRemove).
        _localVarSlices.Remove((map.Header.Name, sid));
        return sid;
    }

    /// <summary>P108: party-member local vars survive map transitions, keyed by the member object.
    /// fo2ce copies each member's LVAR slice out of gMapLocalVars on leave (_partyMemberPrepLoadInstance,
    /// party_member.cc:595-608) and copies it back into the new map's array on arrival
    /// (_partyMemberRecoverLoadInstance, party_member.cc:704-708).</summary>
    private readonly Dictionary<MapObject, int[]> _partyLocalVars = [];

    /// <summary>P108 (copy-out half): snapshot every party member's local-var slice before the member
    /// is pulled off the map being left. Call from party extraction, while members still hold their
    /// old-map sids.</summary>
    public void PreservePartyLocalVars(MapFile map)
    {
        foreach (MapObject member in PartyMembers)
            if (member.Sid != -1 && map.ScriptsBySid.TryGetValue(member.Sid, out MapScriptRecord? record))
                _partyLocalVars[member] = GetLocalVarSlice(map, member.Sid, record);
    }

    /// <summary>P108 (copy-in half): bind a party member's script on the map being entered — a fresh
    /// synthetic sid whose local-var slice is the member's preserved one, so follower scripts keep
    /// their latched state (hired/mood/follow flags) across transitions like fo2ce.</summary>
    public int BindPartyScript(MapFile map, MapObject member, int scriptIndex)
    {
        int sid = AllocateSid(map, scriptIndex);
        if (_partyLocalVars.TryGetValue(member, out int[]? vars))
            _localVarSlices[(map.Header.Name, sid)] = vars;
        return sid;
    }

    /// <summary>
    /// Spatial triggers, ported from fallout2-ce scripts.cc
    /// scriptsExecSpatialProc(): exact built-tile match OR hex distance
    /// within radius, exact elevation. self = a lazily created hidden object
    /// at the trap tile; source = the mover. Disabled around first-run
    /// map_enter like _scr_SpatialsEnabled (map.cc:973).
    /// </summary>
    public bool SpatialsEnabled { get; set; } = true;

    private readonly Dictionary<(string Map, int Sid), MapObject> _spatialSelves = [];

    public void RunSpatialsAt(MapFile map, int tile, int elevation, MapObject mover)
    {
        if (!SpatialsEnabled || mover.IsHidden || mover.IsFlat || tile < 10)
            return;

        foreach (MapFile.SpatialScript spatial in map.SpatialScripts)
        {
            if (spatial.Elevation != elevation)
                continue;
            bool hit = spatial.Radius <= 0
                ? spatial.Tile == tile
                : Hex.HexGrid.Distance(spatial.Tile, tile) <= spatial.Radius;
            if (!hit)
                continue;

            MapObject self = GetSpatialSelf(map, spatial);

            ScriptRunResult? result = RunProc(spatial.ScriptListIndex, map, spatial.Sid,
                map.ScriptsBySid[spatial.Sid], self, mover, 0, -1, ["spatial_p_proc"]);
            if (result is not null)
                foreach (string line in result.Messages)
                    OnScriptMessage?.Invoke(line);
        }
    }

    /// <summary>P113 (item 7e): the lazily-synthesized hidden self object for a SPATIAL script —
    /// shared by spatial triggers and the map procs (spatial scripts get map_enter/update/exit too:
    /// scriptsExecMapUpdateScripts iterates every registered script TYPE, scripts.cc:2601-2674).</summary>
    private MapObject GetSpatialSelf(MapFile map, MapFile.SpatialScript spatial)
    {
        if (!_spatialSelves.TryGetValue((map.Header.Name, spatial.Sid), out MapObject? self))
        {
            self = new MapObject
            {
                Id = -5,
                HexTile = spatial.Tile,
                X = 0,
                Y = 0,
                Frame = 0,
                Rotation = 0,
                Fid = Fid.Build(ObjectType.Misc, 12),
                Flags = 0x01, // hidden
                Pid = 0x05000010,
                Sid = spatial.Sid,
            };
            _spatialSelves[(map.Header.Name, spatial.Sid)] = self;
        }

        if (!map.ScriptsBySid.ContainsKey(spatial.Sid))
            map.ScriptsBySid[spatial.Sid] = new MapScriptRecord(spatial.ScriptListIndex, -1, 0);
        return self;
    }

    /// <summary>P113 (item 7e): run one map proc on every SPATIAL script that defines it — the
    /// ownerless half scriptsExecMapUpdateScripts covers via the script type lists
    /// (scripts.cc:2636-2648); our object loops only reach scripts with map-object owners.</summary>
    private void RunSpatialMapProc(MapFile map, MapObject? dude, int fixedParam, string procName)
    {
        foreach (MapFile.SpatialScript spatial in map.SpatialScripts)
        {
            MapObject self = GetSpatialSelf(map, spatial);
            ScriptRunResult? result = RunProc(spatial.ScriptListIndex, map, spatial.Sid,
                map.ScriptsBySid[spatial.Sid], self, dude, fixedParam, -1, [procName]);
            if (result is not null)
                foreach (string line in result.Messages)
                    OnScriptMessage?.Invoke(line);
        }
    }

    /// <summary>Cross-script external variables (export.cc) — one per session;
    /// shop scripts pass their stock boxes through these.</summary>
    public ExternalVariables ExternalVars { get; } = new();

    /// <summary>The dude's two selected traits (gcd), -1 = none.</summary>
    public int[] DudeTraits { get; set; } = [-1, -1];

    /// <summary>get_pc_stat values (1=level, 2=experience); host-provided.</summary>
    public Func<int, int>? PcStatProvider { get; set; }

    /// <summary>The dude's kill tally for a KILL_TYPE (killsGetByType); host-provided. Drives
    /// metarule3 GET_KILL_COUNT (P38). Null → 0 (no kills tracked).</summary>
    public Func<int, int>? KillCountProvider { get; set; }

    /// <summary>The dude's rank in a perk (perkGetRank); host-provided. Drives has_trait(type 0)
    /// (P28-M2). Null → 0 (no perk system).</summary>
    public Func<int, int>? PerkRankProvider { get; set; }

    /// <summary>The dude's sneaking FLAG (dudeHasState DUDE_STATE_SNEAKING); host-provided. Drives
    /// using_skill(dude, SKILL_SNEAK) (P29 A-M0). Null → false (not sneaking).</summary>
    public Func<bool>? SneakFlagProvider { get; set; }

    /// <summary>True while the viewer's CombatEngine is non-Idle; host-provided. Drives
    /// is_in_combat(0x8128) which critter_p_proc heartbeats poll every tick (P34-M1). Null → false.</summary>
    public Func<bool>? CombatActiveProvider { get; set; }

    /// <summary>poison(obj, amount): the host adjusts the critter's poison counter (critterAdjustPoison,
    /// dude-only, poison-resistance reduced) — the scorpion's on-hit combat_p_proc fires it (P35). </summary>
    public Action<MapObject, int>? PoisonRequested { get; set; }

    /// <summary>terminate_combat: a script asked to end the current combat (P35-M5). The host ends the
    /// fight (CombatEngine); the DISENGAGING maneuver on self is set in ScriptContext.TerminateCombat.</summary>
    public Action? CombatTerminateRequested { get; set; }

    /// <summary>Rolls for do_check/statRoll (seedable by the host).</summary>
    public Random Rng { get; set; } = new();

    /// <summary>Effective stat, ported from fallout2-ce src/stat.cc
    /// critterGetStat(): base + bonus; pseudostats 35/36/37 read the instance.</summary>
    public int CritterStatValue(MapObject obj, int stat)
    {
        switch (stat)
        {
            case 35: // STAT_CURRENT_HIT_POINTS
                return obj.CurrentHp;
            case 36: // STAT_CURRENT_POISON_LEVEL
                return obj.Poison;
            case 37: // STAT_CURRENT_RADIATION_LEVEL
                return obj.Radiation;
        }

        if (stat is < 0 or > 34)
            return -1;

        Proto.CritterProtoStats? stats = StatsOf(obj);
        return stats is null ? -1 : stats.BaseStats[stat] + stats.BonusStats[stat];
    }

    /// <summary>Effective skill % (skill.cc skillGetValue) for has_skill — the viewer's resolver
    /// (full CritterState.SkillValue) when wired, else the simplified proto skill set. P74-M3.</summary>
    public int CritterSkillValue(MapObject obj, int skill)
    {
        if (SkillResolver?.Invoke(obj, skill) is { } resolved)
            return resolved;
        Proto.CritterProtoStats? stats = StatsOf(obj);
        return stats is null ? 0 : Combat.SkillSet.Value(stats.BaseStats, stats.BonusStats, stats.Skills, null, skill);
    }

    internal Proto.CritterProtoStats? StatsOf(MapObject obj)
    {
        if (StatsResolver?.Invoke(obj) is { } overridden)
            return overridden;
        if (Fid.PidType(obj.Pid) != 1)
            return null;
        try
        {
            return protos.Get(obj.Pid).Critter;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return null;
        }
    }

    // ---- script timer queue, ported from fallout2-ce queue.cc/scripts.cc:
    // absolute due time, sorted, stable FIFO for equal times. Delays are game
    // ticks at the ENGINE rate (10/s real time, 100 ms per tick) — independent
    // of any accelerated day/night clock. The engine drops all script timers
    // on map exit (_queue_leaving_map) — call ClearTimers() on transitions.

    private sealed record TimerEntry(long DueTick, MapFile Map, MapObject Owner, int Param);

    private readonly List<TimerEntry> _timers = [];

    public int PendingTimerCount => _timers.Count;

    // P114: timers are keyed on GAME time (ClockTicks), not a private wall-clock. delayTicks is raw game
    // ticks (queue.cc:245 node->time = gameTimeGetTime()+delay), so a clock JUMP (game_time_advance / rest /
    // travel) fires due timers — exactly like poison/rads. 302400 = the game-start tick when no clock wired.
    public void AddTimer(MapFile map, MapObject owner, int delayTicks, int param)
    {
        long due = (ClockTicks?.Invoke() ?? 302400) + Math.Max(delayTicks, 0);
        int index = _timers.FindIndex(t => t.DueTick > due); // insert after equal times (FIFO for ties)
        var entry = new TimerEntry(due, map, owner, param);
        if (index < 0)
            _timers.Add(entry);
        else
            _timers.Insert(index, entry);
    }

    public void RemoveTimers(MapObject owner, int? param = null) =>
        _timers.RemoveAll(t => t.Owner == owner && (param is null || t.Param == param));

    public void ClearTimers() => _timers.Clear();

    public const int MoneyPid = 41; // PROTO_ID_MONEY (proto_types.h:139)

    /// <summary>Caps in an inventory (item.cc itemGetTotalCaps, sans container recursion).</summary>
    public int CapsTotal(MapObject obj) =>
        obj.Inventory.Where(i => i.Pid == MoneyPid).Sum(i => i.StackCount);

    /// <summary>ported from fallout2-ce item.cc itemCapsAdjust(): -1 when
    /// removing more than the total; adding creates a money stack.</summary>
    public int CapsAdjust(MapObject obj, int amount)
    {
        if (amount >= 0)
        {
            if (amount == 0)
                return 0;
            if (obj.Inventory.FirstOrDefault(i => i.Pid == MoneyPid) is { } stack)
            {
                stack.StackCount += amount;
                return 0;
            }

            try
            {
                var money = new MapObject
                {
                    Id = -5,
                    HexTile = -1,
                    X = 0,
                    Y = 0,
                    Frame = 0,
                    Rotation = 0,
                    Fid = Protos.Get(MoneyPid).Fid,
                    Flags = 0,
                    Pid = MoneyPid,
                    Sid = -1,
                };
                money.StackCount = amount;
                obj.Inventory.Add(money);
                return 0;
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
                Console.Error.WriteLine($"caps_adjust: {ex.Message}");
                return -1;
            }
        }

        int toRemove = -amount;
        if (CapsTotal(obj) < toRemove)
            return -1;

        foreach (MapObject stackEntry in obj.Inventory.Where(i => i.Pid == MoneyPid).ToList())
        {
            int take = Math.Min(stackEntry.StackCount, toRemove);
            stackEntry.StackCount -= take;
            toRemove -= take;
            if (stackEntry.StackCount <= 0)
                obj.Inventory.Remove(stackEntry);
            if (toRemove == 0)
                break;
        }

        return 0;
    }

    /// <summary>
    /// Advances the timer clock and runs due timed_event_p_procs. The caller
    /// gates this like the engine does (not during dialog/loot — scripts arm
    /// timers mid-conversation expecting them to fire after it closes).
    /// </summary>
    public void PumpTimers(MapObject? dude)
    {
        long now = ClockTicks?.Invoke() ?? 302400;
        while (_timers.Count > 0 && _timers[0].DueTick <= now)
        {
            TimerEntry entry = _timers[0];
            _timers.RemoveAt(0);
            ScriptRunResult? result = RunObjectProc(entry.Owner, entry.Map, dude,
                entry.Param, -1, "timed_event_p_proc");
            if (result is not null && OnScriptMessage is not null)
                foreach (string message in result.Messages)
                    OnScriptMessage(message);
        }
    }

    /// <summary>Session GVARs, exposed for save/load.</summary>
    public Dictionary<int, int> GlobalVars => _globalVars;

    /// <summary>P100 (Point 4): the Highwayman car fuel/state, driven by the metarule car externals
    /// (give_car_to_party/give_car_gas/car_current_town) + metarule3 110 (out of gas). Exposed for save/load.</summary>
    public CarState Car { get; } = new();

    /// <summary>
    /// Runs the object's description_p_proc (falling back to look_at_p_proc).
    /// Returns the display_msg lines when the script overrides the default
    /// description; null otherwise.
    /// </summary>
    public IReadOnlyList<string>? GetScriptedDescription(MapObject obj, MapFile map, MapObject? dude)
    {
        ScriptRunResult? result = RunObjectProc(obj, map, dude, "description_p_proc", "look_at_p_proc");
        return result is { Overridden: true, Messages.Count: > 0 } ? result.Messages : null;
    }

    public sealed record ScriptRunResult(bool Overridden, List<string> Messages);

    /// <summary>
    /// Runs the first procedure (by name) the object's script defines, with
    /// full context. Returns null when the object has no script / no such
    /// proc / the VM fails (soft fallback).
    /// </summary>
    public ScriptRunResult? RunObjectProc(MapObject obj, MapFile map, MapObject? dude,
        params string[] procedureNames) =>
        RunObjectProc(obj, map, dude, 0, -1, procedureNames);

    public ScriptRunResult? RunObjectProc(MapObject obj, MapFile map, MapObject? dude,
        int fixedParam, int actionBeingUsed, params string[] procedureNames)
    {
        if (obj.Sid == -1 || !map.ScriptsBySid.TryGetValue(obj.Sid, out MapScriptRecord? record))
            return null;

        return RunProc(record.ScriptListIndex, map, obj.Sid, record, obj, dude,
            fixedParam, actionBeingUsed, procedureNames);
    }

    /// <summary>Run a scenery/door's damage_p_proc for a nearby explosion — ported from fallout2-ce
    /// src/scripts.cc _scr_explode_scenery (:2879): fixedParam = 20, and the script's TARGET is the misc-10
    /// explosion marker. A door reads target_obj → metarule(METARULE_WEAPON_DAMAGE_TYPE) → EXPLOSION and
    /// unlocks/opens/destroys itself. RunObjectProc leaves target null (→ target_obj falls back to self),
    /// which is why the blast never reached the door before.</summary>
    public ScriptRunResult? RunExplosionDamage(MapObject scenery, MapFile map, MapObject marker, MapObject? dude)
    {
        if (scenery.Sid == -1 || !map.ScriptsBySid.TryGetValue(scenery.Sid, out MapScriptRecord? record))
            return null;
        string? path = scripts.GetScriptPath(record.ScriptListIndex);
        if (path is null)
            return null;
        try
        {
            IntProgram? program = GetProgram(path);
            if (program is null)
                return null;
            var externals = new ScriptContext(this, map, scenery.Sid, record, self: scenery, source: dude, dude: dude)
            {
                FixedParamValue = 20,     // _scr_explode_scenery: script->fixedParam = 20
                ActionBeingUsedValue = -1,
                Target = marker,          // script->target = the explosion marker (read via target_obj)
            };
            var vm = new IntVm(program, externals, OnStubbedExternal, ExternalVars);
            return vm.TryRunProcedure("damage_p_proc")
                ? new ScriptRunResult(externals.Overridden, externals.Messages)
                : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or NotSupportedException)
        {
            Console.Error.WriteLine($"script {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Run a combatant's combat_p_proc with the engine's combat context (combat.cc:3245/4730):
    /// self = the combatant, source = NULL always, target = the struck defender (fp=2) or null (fp=4),
    /// dude = the real dude (for dude_obj). This DECOUPLES source/target/dude (RunObjectProc couples
    /// source==dude), which the combat hooks need. Returns null when self has no combat_p_proc.
    /// </summary>
    public ScriptRunResult? RunCombatProc(MapObject self, MapObject? target, MapObject? dude, MapFile map, int fixedParam)
    {
        if (self.Sid == -1 || !map.ScriptsBySid.TryGetValue(self.Sid, out MapScriptRecord? record))
            return null;
        string? path = scripts.GetScriptPath(record.ScriptListIndex);
        if (path is null)
            return null;
        try
        {
            IntProgram? program = GetProgram(path);
            if (program is null)
                return null;
            var externals = new ScriptContext(this, map, self.Sid, record, self: self, source: null, dude: dude)
            {
                FixedParamValue = fixedParam,
                ActionBeingUsedValue = -1,
                Target = target,
            };
            var vm = new IntVm(program, externals, OnStubbedExternal, ExternalVars);
            return vm.TryRunProcedure("combat_p_proc")
                ? new ScriptRunResult(externals.Overridden, externals.Messages)
                : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or NotSupportedException)
        {
            Console.Error.WriteLine($"script {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// use item ON object, ported from fallout2-ce proto_instance.cc:1245
    /// _obj_use_item_on(): the ITEM's use_obj_on_p_proc runs first (self =
    /// item, usedWith = target); unless it overrides, the TARGET's proc runs
    /// (self = target, usedWith = item). Returns the merged result, or null
    /// when neither side has a script.
    /// </summary>
    public ScriptRunResult? RunUseObjOn(MapObject item, MapObject target, MapFile map, MapObject? dude)
    {
        var messages = new List<string>();
        bool overridden = false;
        bool ranAny = false;

        if (item.Sid != -1 && map.ScriptsBySid.TryGetValue(item.Sid, out MapScriptRecord? itemRecord))
        {
            ScriptRunResult? result = RunProcWith(itemRecord.ScriptListIndex, map, item.Sid, itemRecord,
                self: item, dude, usedWith: target, "use_obj_on_p_proc");
            if (result is not null)
            {
                ranAny = true;
                messages.AddRange(result.Messages);
                overridden = result.Overridden;
            }
        }

        if (!overridden && target.Sid != -1 && map.ScriptsBySid.TryGetValue(target.Sid, out MapScriptRecord? targetRecord))
        {
            ScriptRunResult? result = RunProcWith(targetRecord.ScriptListIndex, map, target.Sid, targetRecord,
                self: target, dude, usedWith: item, "use_obj_on_p_proc");
            if (result is not null)
            {
                ranAny = true;
                messages.AddRange(result.Messages);
                overridden |= result.Overridden;
            }
        }

        return ranAny ? new ScriptRunResult(overridden, messages) : null;
    }

    private ScriptRunResult? RunProcWith(int scriptListIndex, MapFile map, int sid, MapScriptRecord record,
        MapObject self, MapObject? dude, MapObject usedWith, string procedureName)
    {
        string? path = scripts.GetScriptPath(scriptListIndex);
        if (path is null)
            return null;
        try
        {
            IntProgram? program = GetProgram(path);
            if (program is null)
                return null;
            var externals = new ScriptContext(this, map, sid, record, self, source: dude, dude: dude)
            {
                UsedWith = usedWith,
            };
            var vm = new IntVm(program, externals, OnStubbedExternal, ExternalVars);
            return vm.TryRunProcedure(procedureName)
                ? new ScriptRunResult(externals.Overridden, externals.Messages)
                : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or NotSupportedException)
        {
            Console.Error.WriteLine($"script {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>The map script has no real owner object; synthesize one (shared by the
    /// start/map_enter/map_update passes).</summary>
    private static MapObject SynthMapScriptOwner() => new()
    {
        Id = -2,
        HexTile = 1,
        X = 0,
        Y = 0,
        Frame = 0,
        Rotation = 0,
        Fid = Fid.Build(ObjectType.Misc, 12),
        Flags = 0,
        Pid = 0x05000010,
        Sid = -1,
    };

    // ported from fallout2-ce src/interpreter.cc runScript() (via scriptExecProc, scripts.cc:1322-1338):
    // the FIRST time a script's program executes, its global-init prologue runs (offset 0) — that is where
    // exported variables are declared (export_variable) and any global-scope values assigned. We run only
    // that prologue, not the optional SCRIPT_PROC_START body: declaring the exports is what publishes them
    // for fetch_external, and skipping the rarely-defined start body avoids re-firing its side effects every
    // load (the PoC re-creates a program per proc run, so a start body would run more often than the engine's
    // once-per-map). Verified: this publishes denbus1/denbus2's gang_2_member_* from dcLara/dcTyler's prologue.
    private void RunStart(int scriptListIndex, MapFile map, int sid, MapScriptRecord record, MapObject self, MapObject? dude)
    {
        string? path = scripts.GetScriptPath(scriptListIndex);
        if (path is null)
            return;
        try
        {
            IntProgram? program = GetProgram(path);
            if (program is null)
                return;
            var externals = new ScriptContext(this, map, sid, record, self: self, source: dude, dude: dude)
            {
                FixedParamValue = 0,
                ActionBeingUsedValue = -1,
            };
            new IntVm(program, externals, OnStubbedExternal, ExternalVars).RunGlobalInit();
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or NotSupportedException)
        {
            Console.Error.WriteLine($"script {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs the SCRIPT_PROC_START pass like fallout2-ce src/map.cc:1006 scriptsExecStartProc():
    /// the MAP script's start then every scripted object's, BEFORE map_enter. Its real job is the
    /// side effect of a script's first execution — the global-init prologue that exports its
    /// cross-script variables — so a combat-only script (only critter_p_proc/combat_p_proc, e.g.
    /// the Den gang war's dcLara/dcTyler) still publishes gang_2_member_* for other scripts to
    /// fetch_external. Without this pass those imports resolve to 0. Call once, immediately before
    /// <see cref="RunMapEnter"/>, to preserve the engine's start -> map_enter -> map_update order.
    /// </summary>
    public void RunStartProcs(MapFile map, IEnumerable<MapObject> objects, MapObject? dude)
    {
        if (map.Header.ScriptIndex > 0)
        {
            var record = new MapScriptRecord(map.Header.ScriptIndex - 1, -1, 0);
            RunStart(record.ScriptListIndex, map, sid: -2, record, SynthMapScriptOwner(), dude);
        }

        foreach (MapObject obj in objects.ToList())
            if (obj.Sid != -1 && map.ScriptsBySid.TryGetValue(obj.Sid, out MapScriptRecord? record))
                RunStart(record.ScriptListIndex, map, obj.Sid, record, obj, dude);
    }

    /// <summary>
    /// Runs map-entry scripts like fallout2-ce map.cc:952-975 +
    /// scripts.cc scriptExecMapEnterScripts(): the MAP script first
    /// (scripts.lst index = header.ScriptIndex - 1) with fixedParam =
    /// first-run flag, then every scripted object's map_enter_p_proc.
    /// </summary>
    public void RunMapEnter(MapFile map, IEnumerable<MapObject> objects, MapObject? dude,
        bool? firstRunOverride = null)
    {
        int firstRun = firstRunOverride.HasValue
            ? (firstRunOverride.Value ? 1 : 0)
            : ((map.Header.Flags & 0x01) == 0 ? 1 : 0);
        _firstRunByMap[map.Header.Name] = firstRun == 1;

        if (map.Header.ScriptIndex > 0)
        {
            // The map script has no real owner object; synthesize one.
            var owner = new MapObject
            {
                Id = -2,
                HexTile = 1,
                X = 0,
                Y = 0,
                Frame = 0,
                Rotation = 0,
                Fid = Fid.Build(ObjectType.Misc, 12),
                Flags = 0,
                Pid = 0x05000010,
                Sid = -1,
            };
            var record = new MapScriptRecord(map.Header.ScriptIndex - 1, -1, 0);
            RunProc(record.ScriptListIndex, map, sid: -2, record, owner, dude,
                firstRun, -1, ["map_enter_p_proc"]);
        }

        // Snapshot: map_enter scripts create objects (container stocking),
        // mutating the underlying lists mid-iteration.
        foreach (MapObject obj in objects.ToList())
            RunObjectProc(obj, map, dude, firstRun, -1, "map_enter_p_proc");

        // P113 (item 7e): spatial scripts get map_enter too (scriptsExecMapUpdateScripts iterates
        // every script TYPE list, scripts.cc:2601-2674; MAP_ENTER carries firstRun, :2608-2610).
        RunSpatialMapProc(map, dude, firstRun, "map_enter_p_proc");
    }

    /// <summary>
    /// Runs map-update scripts like fallout2-ce scripts.cc scriptsExecMapUpdateScripts():
    /// the MAP script's map_update_p_proc first, then every scripted object's. The engine
    /// fires this once on map load (after map_enter) then every 600 game ticks
    /// (mapUpdateEventProcess, SCRIPT_PROC_MAP_UPDATE = 23). map_update takes no fixed param.
    /// Hexwaste runs it once on load (the engine's map.cc:1010-1011 sequence); the periodic
    /// 600-tick re-run is deferred (no time-varying map_update content on the slice).
    /// </summary>
    public void RunMapUpdate(MapFile map, IEnumerable<MapObject> objects, MapObject? dude)
    {
        if (map.Header.ScriptIndex > 0)
        {
            var owner = new MapObject
            {
                Id = -2,
                HexTile = 1,
                X = 0,
                Y = 0,
                Frame = 0,
                Rotation = 0,
                Fid = Fid.Build(ObjectType.Misc, 12),
                Flags = 0,
                Pid = 0x05000010,
                Sid = -1,
            };
            var record = new MapScriptRecord(map.Header.ScriptIndex - 1, -1, 0);
            RunProc(record.ScriptListIndex, map, sid: -2, record, owner, dude,
                fixedParam: 0, -1, ["map_update_p_proc"]);
        }

        foreach (MapObject obj in objects.ToList())
            RunObjectProc(obj, map, dude, 0, -1, "map_update_p_proc");

        RunSpatialMapProc(map, dude, 0, "map_update_p_proc"); // P113 (item 7e)
    }

    /// <summary>
    /// P105: run map-EXIT scripts like fallout2-ce scripts.cc scriptsExecMapExitProc() →
    /// scriptsExecMapUpdateScripts(SCRIPT_PROC_MAP_EXIT = 16): the MAP script's map_exit_p_proc first,
    /// then every remaining scripted object's, when the dude LEAVES the map. The caller must exclude
    /// party members — the engine removes their scripts (_partyMemberPrepLoad, map.cc:1438) before the
    /// exit procs run (map.cc:1440), so their own map_exit procs never fire. Mirrors RunMapUpdate;
    /// map_exit sets no fixed param (scripts.cc:2608-2610 does so only for MAP_ENTER). NOTE (P108
    /// review): escort quests do NOT complete here — their leave_player fires from the critter_p_proc
    /// heartbeat via fixed-tile proximity; map_exit is general engine fidelity (e.g. brahmin bookkeeping).
    /// </summary>
    public void RunMapExit(MapFile map, IEnumerable<MapObject> objects, MapObject? dude)
    {
        if (map.Header.ScriptIndex > 0)
        {
            var record = new MapScriptRecord(map.Header.ScriptIndex - 1, -1, 0);
            RunProc(record.ScriptListIndex, map, sid: -2, record, SynthMapScriptOwner(), dude,
                fixedParam: 0, -1, ["map_exit_p_proc"]);
        }

        foreach (MapObject obj in objects.ToList())
            RunObjectProc(obj, map, dude, 0, -1, "map_exit_p_proc");

        RunSpatialMapProc(map, dude, 0, "map_exit_p_proc"); // P113 (item 7e)
    }

    /// <summary>
    /// P100 (Point 3): the map-script combat_p_proc "combat over" hook, ported from fallout2-ce
    /// src/scripts.cc:2848 _scr_end_combat(): when the dude is knocked out (not killed) in combat, run the
    /// MAP script's combat_p_proc with fixedParam = the team that KO'd the dude, and report whether it
    /// script_overrides (New Reno's prizefight ring uses this to "catch" the KO + score the bout instead of
    /// leaving the dude unconscious). Returns null when there is no map script (matches RunProc's contract);
    /// Overridden mirrors the engine's <c>after-&gt;scriptOverrides != 0</c>.
    /// </summary>
    public ScriptRunResult? RunMapCombatOver(MapFile map, MapObject? dude, int team)
    {
        if (map.Header.ScriptIndex <= 0)
            return null;
        var record = new MapScriptRecord(map.Header.ScriptIndex - 1, -1, 0);
        return RunProc(record.ScriptListIndex, map, sid: -2, record, SynthMapScriptOwner(), dude,
            fixedParam: team, -1, ["combat_p_proc"]);
    }

    /// <summary>Revisit tracking: metarule 14 FIRST_RUN consults this.</summary>
    private readonly Dictionary<string, bool> _firstRunByMap = [];

    public bool IsFirstRun(MapFile map) =>
        _firstRunByMap.TryGetValue(map.Header.Name, out bool firstRun)
            ? firstRun
            : (map.Header.Flags & 0x01) == 0;

    private ScriptRunResult? RunProc(int scriptListIndex, MapFile map, int sid, MapScriptRecord record,
        MapObject self, MapObject? dude, int fixedParam, int actionBeingUsed, string[] procedureNames)
    {
        string? path = scripts.GetScriptPath(scriptListIndex);
        if (path is null)
            return null;

        try
        {
            IntProgram? program = GetProgram(path);
            if (program is null)
                return null;

            var externals = new ScriptContext(this, map, sid, record, self: self, source: dude, dude: dude)
            {
                FixedParamValue = fixedParam,
                ActionBeingUsedValue = actionBeingUsed,
            };
            var vm = new IntVm(program, externals, OnStubbedExternal, ExternalVars);
            foreach (string name in procedureNames)
            {
                if (vm.TryRunProcedure(name))
                    return new ScriptRunResult(externals.Overridden, externals.Messages);
            }

            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or NotSupportedException)
        {
            Console.Error.WriteLine($"script {path}: {ex.Message}");
            // Safety net: a proc that game_ui_disable'd then died (runaway/error) must not leave the player
            // input-locked forever — restore the UI on any aborted proc run through here (spatials/objects).
            GameUiEnabledRequested?.Invoke(true);
            return null;
        }
    }

    /// <summary>P113 (item 6): scriptHasProc — does this object's bound script DEFINE the procedure?
    /// Gates the Push action-menu verb (actionCheckPush, actions.cc:2018). Static procedure-table
    /// lookup, no execution.</summary>
    public bool ObjectHasProc(MapObject obj, MapFile map, string procName)
    {
        if (obj.Sid == -1 || !map.ScriptsBySid.TryGetValue(obj.Sid, out MapScriptRecord? record))
            return false;
        string? path = scripts.GetScriptPath(record.ScriptListIndex);
        if (path is null)
            return false;
        try
        {
            // procs[proc] > 0 in the engine = locally DEFINED — an imported forward declaration
            // (no body here) must not count (the MapDump map_update census draws the same line).
            return GetProgram(path) is { } program
                && program.FindProcedure(procName) is int idx and >= 0
                && !program.Procedures[idx].IsImported;
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or NotSupportedException)
        {
            return false;
        }
    }

    private IntProgram? GetProgram(string path)
    {
        if (_programs.TryGetValue(path, out IntProgram? cached))
            return cached;
        IntProgram? program = vfs.Exists(path) ? IntProgram.Load(vfs.ReadAllBytes(path)) : null;
        _programs[path] = program;
        return program;
    }

    private string LookupMessage(int messageListId, int messageId)
    {
        if (!_dialogMessages.TryGetValue(messageListId, out MessageFile? messages))
        {
            string? path = scripts.GetDialogMessagePath(messageListId);
            messages = path is not null && vfs.Exists(path)
                ? LoadMessages(path)
                : null;
            _dialogMessages[messageListId] = messages;
        }

        return messages?.GetText(messageId) ?? "";
    }

    /// <summary>P53: the speech-file basename for a dialogue message (the MSG audio field), or null when
    /// the line is unvoiced. Shares the same cached message files as <see cref="LookupMessage"/>.</summary>
    public string? LookupAudio(int messageListId, int messageId)
    {
        if (!_dialogMessages.TryGetValue(messageListId, out MessageFile? messages))
        {
            string? path = scripts.GetDialogMessagePath(messageListId);
            messages = path is not null && vfs.Exists(path) ? LoadMessages(path) : null;
            _dialogMessages[messageListId] = messages;
        }
        return messages?.GetAudio(messageId);
    }

    private MessageFile LoadMessages(string path)
    {
        using Stream stream = vfs.OpenRead(path);
        return MessageFile.Load(stream);
    }

    private int[] GetLocalVarSlice(MapFile map, int sid, MapScriptRecord record)
    {
        if (_localVarSlices.TryGetValue((map.Header.Name, sid), out int[]? slice))
            return slice;

        int count = record.LocalVarsCount > 0
            ? record.LocalVarsCount
            : scripts.GetLocalVarsCount(record.ScriptListIndex);
        slice = new int[Math.Max(count, 0)];

        // Saved maps (.SAV) carry real offsets into the serialized block —
        // seed the slice from it so saved state is honored when present.
        if (record.LocalVarsOffset >= 0)
        {
            for (int i = 0; i < slice.Length && record.LocalVarsOffset + i < map.LocalVariables.Length; i++)
                slice[i] = map.LocalVariables[record.LocalVarsOffset + i];
        }

        _localVarSlices[(map.Header.Name, sid)] = slice;
        return slice;
    }

    /// <summary>P105 (test aid): directly set a scripted object's local var (e.g. an escort NPC's follow
    /// flag) so a later map_exit_p_proc / other proc reads it — mirrors set_local_var without running a
    /// script. Returns false if the object has no script or the index is out of range.</summary>
    public bool SetObjectLocalVar(MapFile map, MapObject obj, int index, int value)
    {
        if (obj.Sid == -1 || !map.ScriptsBySid.TryGetValue(obj.Sid, out MapScriptRecord? record))
            return false;
        int[] slice = GetLocalVarSlice(map, obj.Sid, record);
        if (index < 0 || index >= slice.Length)
            return false;
        slice[index] = value;
        return true;
    }

    /// <summary>P114: reactionGetValue (reaction.cc) — the barterer's reaction, stored in its script's
    /// LVAR[0]; −1 (NEUTRAL) when unset. Feeds the barter reaction price modifier (inventory.cc:5093).</summary>
    public int ReactionValue(MapFile map, MapObject npc)
    {
        if (npc.Sid == -1 || !map.ScriptsBySid.TryGetValue(npc.Sid, out MapScriptRecord? record))
            return -1;
        int[] slice = GetLocalVarSlice(map, npc.Sid, record);
        return slice.Length > 0 ? slice[0] : -1;
    }

    /// <summary>Clears the object handle table — call on map transitions
    /// (handles never outlive a VM run / dialog session).</summary>
    public void ResetHandles()
    {
        _handles.Clear();
        _handleByObject.Clear();
    }

    /// <summary>LVAR slices of one map, for save serialization.</summary>
    public Dictionary<int, int[]> ExportLocalVars(string mapName) =>
        _localVarSlices.Where(kv => kv.Key.Map == mapName)
            .ToDictionary(kv => kv.Key.Sid, kv => (int[])kv.Value.Clone());

    /// <summary>All maps' LVAR slices (save serialization).</summary>
    public Dictionary<string, Dictionary<int, int[]>> ExportAllLocalVars() =>
        _localVarSlices.GroupBy(kv => kv.Key.Map)
            .ToDictionary(g => g.Key, g => g.ToDictionary(kv => kv.Key.Sid, kv => (int[])kv.Value.Clone()));

    public void ImportLocalVars(string mapName, Dictionary<int, int[]> slices)
    {
        foreach ((int sid, int[] values) in slices)
            _localVarSlices[(mapName, sid)] = (int[])values.Clone();
    }

    public void ClearAllLocalVars() => _localVarSlices.Clear();

    /// <summary>
    /// A running conversation: the same VM + context persist across option
    /// picks (LVARs/program globals keep their state), exactly like
    /// game_dialog.cc _gdProcess: show reply, pick option, run its bound
    /// procedure (which repopulates reply+options), end when a procedure
    /// leaves zero options.
    /// </summary>
    public sealed class DialogSession
    {
        private readonly IntVm _vm;
        private readonly ScriptContext _context;

        public string NpcName { get; }
        public string Reply => _context.DialogReplyText;
        public IReadOnlyList<string> Options => _context.DialogOptions.Select(o => o.Text).ToList();

        /// <summary>The raw reaction value (GAME_DIALOG_REACTION_*) per option, parallel to
        /// <see cref="Options"/> — the Empathy perk tints each option by this (game_dialog.cc:2118).</summary>
        public IReadOnlyList<int> OptionReactions => _context.DialogOptions.Select(o => o.Reaction).ToList();

        /// <summary>The procedure index each option jumps to (parallel to <see cref="Options"/>) — the
        /// dynamic census DFS's these to drive every dialog branch (P101 bucket 2).</summary>
        public IReadOnlyList<int> OptionProcedures => _context.DialogOptions.Select(o => o.ProcedureIndex).ToList();

        public bool Active { get; private set; } = true;

        /// <summary>P87: the talking-head index the script's start_gdialog supplied (heads.lst index), or
        /// -1 for a head-less dialog. The viewer renders the head FRM above the conversation panel.</summary>
        public int HeadId => _context.DialogHeadId;

        internal DialogSession(IntVm vm, ScriptContext context, string npcName)
        {
            _vm = vm;
            _context = context;
            NpcName = npcName;
        }

        /// <summary>A picked option called gdialog_barter: the host should open
        /// the trade window now; the queued reply is already in place.</summary>
        public bool TakeBarterRequest(out int modifier) => _context.TakeBarterRequest(out modifier);

        /// <summary>The shopkeeper's live stock container (see ScriptContext.StockBox).</summary>
        public MapObject? StockBox => _context.StockBox;

        /// <summary>Picks an option (0-based). Returns false when the dialog has ended.</summary>
        public bool Choose(int optionIndex)
        {
            if (!Active || optionIndex < 0 || optionIndex >= _context.DialogOptions.Count)
                return Active;

            int procedureIndex = _context.DialogOptions[optionIndex].ProcedureIndex;
            _context.ResetDialogRound();

            try
            {
                _vm.TryRunProcedureByIndex(procedureIndex);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                Console.Error.WriteLine($"dialog proc {procedureIndex}: {ex.Message}");
                Active = false;
                return false;
            }

            if (Environment.GetEnvironmentVariable("HEXWASTE_DIALOG_DEBUG") == "1")
                Console.Error.WriteLine($"[dlg] opt={optionIndex} proc={procedureIndex} -> reply='{(_context.DialogReplyText.Length > 50 ? _context.DialogReplyText[..50] : _context.DialogReplyText)}' opts={_context.DialogOptions.Count} ended={_context.SessionEnded}");
            if (_context.DialogOptions.Count == 0 || _context.SessionEnded)
                Active = false;
            return Active;
        }

        /// <summary>Out-of-band lines produced this round (float_msg, display_msg, barter notice).</summary>
        public IReadOnlyList<string> SideMessages => _context.Messages;
    }

    /// <summary>
    /// Opens a conversation with a scripted object via its talk_p_proc.
    /// Returns null when the object has no dialog (floater-only NPCs put
    /// their lines in <paramref name="floaters"/>).
    /// </summary>
    public DialogSession? StartDialog(MapObject obj, MapFile map, MapObject? dude, out IReadOnlyList<string> floaters)
    {
        floaters = [];
        if (obj.Sid == -1 || !map.ScriptsBySid.TryGetValue(obj.Sid, out MapScriptRecord? record))
            return null;

        string? path = scripts.GetScriptPath(record.ScriptListIndex);
        if (path is null)
            return null;

        try
        {
            IntProgram? program = GetProgram(path);
            if (program is null)
                return null;

            var context = new ScriptContext(this, map, obj.Sid, record, self: obj, source: dude, dude: dude);
            var vm = new IntVm(program, context, OnStubbedExternal, ExternalVars);
            if (!vm.TryRunProcedure("talk_p_proc"))
                return null;

            floaters = context.Messages;
            if (context.DialogOptions.Count == 0)
                return null; // floater-only NPC — no dialog window

            string npcName = NameResolver?.Invoke(obj) ?? "stranger";
            return new DialogSession(vm, context, npcName);
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or NotSupportedException)
        {
            Console.Error.WriteLine($"script {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Per-invocation script context, mirroring scriptExecProc's setup
    /// (scripts.cc:1261-1342): source/target/dude, fixedParam,
    /// actionBeingUsed, and a per-run overrides flag.
    /// </summary>
    internal sealed class ScriptContext : IVmExternals
    {
        private readonly ScriptHost _host;
        private readonly MapFile _map;
        private readonly int _sid;
        private readonly MapScriptRecord _record;
        private readonly MapObject _self;
        private readonly MapObject? _source;
        private readonly MapObject? _dude;

        public List<string> Messages { get; } = [];
        public bool Overridden { get; private set; }
        public int FixedParamValue { get; init; }
        public int ActionBeingUsedValue { get; init; } = -1;

        /// <summary>obj_being_used_with: the OTHER party of use_obj_on
        /// (target's proc sees the item; item's proc sees the target).</summary>
        public MapObject? UsedWith { get; init; }

        public int ObjectBeingUsedWithId() => _host.HandleOf(UsedWith);

        /// <summary>target_obj override: the on-hit combat_p_proc (fp=2) sets target = the struck
        /// defender (combat.cc:4730 scriptSetObjects(attacker,NULL,defender)). Null → self (the default).</summary>
        public MapObject? Target { get; init; }

        public ScriptContext(ScriptHost host, MapFile map, int sid, MapScriptRecord record,
            MapObject self, MapObject? source, MapObject? dude)
        {
            _host = host;
            _map = map;
            _sid = sid;
            _record = record;
            _self = self;
            _source = source;
            _dude = dude;
        }

        public void DisplayMessage(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                Messages.Add(text.Trim());
        }

        public string GetMessage(int messageListId, int id) => _host.LookupMessage(messageListId, id);

        public void PlayDialogVoice(int messageListId, int messageId) =>
            _host.DialogVoiceRequested?.Invoke(messageListId, messageId);

        public void SetScriptOverrides() => Overridden = true;

        public int SelfObjectId() => _host.HandleOf(_self);

        public int SourceObjectId() => _host.HandleOf(_source);

        public int TargetObjectId() => Target is { } t ? _host.HandleOf(t) : _host.HandleOf(_self); // null → self (scripts.cc:1316)

        public int DudeObjectId() => _host.HandleOf(_dude);

        public int FixedParam() => FixedParamValue;

        public int ActionBeingUsed() => ActionBeingUsedValue;

        public string ObjectName(int objectHandle) =>
            _host.ObjectOf(objectHandle) is { } obj
                ? _host.NameResolver?.Invoke(obj) ?? "object"
                : "object";

        public int GetGlobalVar(int index) =>
            _host._globalVars.TryGetValue(index, out int value) ? value : 0;

        public void SetGlobalVar(int index, int value) => _host._globalVars[index] = value;

        public int GetLocalVar(int index)
        {
            int[] slice = _host.GetLocalVarSlice(_map, _sid, _record);
            return index >= 0 && index < slice.Length ? slice[index] : 0;
        }

        public void SetLocalVar(int index, int value)
        {
            int[] slice = _host.GetLocalVarSlice(_map, _sid, _record);
            if (index >= 0 && index < slice.Length)
                slice[index] = value;
        }

        public int GetMapVar(int index) =>
            index >= 0 && index < _map.GlobalVariables.Length ? _map.GlobalVariables[index] : 0;

        public void SetMapVar(int index, int value)
        {
            if (index >= 0 && index < _map.GlobalVariables.Length)
                _map.GlobalVariables[index] = value;
        }

        // metarule: 14 FIRST_RUN (host tracks revisits); 16 PARTY_COUNT; 22
        // IS_LOADGAME = 0; 49 WEAPON_DAMAGE_TYPE (the misc-10 explosion marker →
        // EXPLOSION, for the temple-door damage_p_proc); everything else 0.
        public int Metarule(int rule, int argument) => rule switch
        {
            14 => _host.IsFirstRun(_map) ? 1 : 0,
            // METARULE_PARTY_COUNT (16) — _getPartyMemberCount (party_member.cc:900):
            // slot 0 is the dude (always counted), plus each live, visible, recruited
            // critter. dcVic gates the join on metarule(16)-1 >= floor(CHA/2)+trait(98).
            16 => PartyMemberCount(_host.PartyMembers),
            49 => _host.ObjectOf(argument) is { } o
                  && o.Fid == Fid.Build(ObjectType.Misc, 10, 0, 0) ? 6 /* DAMAGE_TYPE_EXPLOSION */ : 0,
            // P100 (Point 4): the Highwayman car metarules — previously silent no-ops (fell through _ => 0).
            // ported from fallout2-ce src/interpreter_extra.cc opMetarule (:3234) + src/worldmap.cc.
            30 => _host.Car.CurrentAreaId,                  // METARULE_CAR_CURRENT_TOWN (wmCarCurrentArea)
            31 => _host.Car.GiveToParty() ? 1 : -1,         // METARULE_GIVE_CAR_TO_PARTY (wmCarGiveToParty)
            32 => _host.Car.FillGas(argument),              // METARULE_GIVE_CAR_GAS (wmCarFillGas → overflow)
            // P113 (item 5): METARULE_ELEVATOR (15) — scriptsRequestElevator(self, type)
            // (interpreter_extra.cc:3215): record the request; the host services it on the next pump.
            15 => RequestElevator(argument),
            // METARULE_PARTY_COUNT is 16; METARULE_CURRENT_TOWN (46) — wmGetPartyCurArea
            // (interpreter_extra.cc:3286). The <0→0 clamp keeps direct-map/harness runs (area id −1
            // before any worldmap travel) returning today's 0.
            46 => Math.Max(0, _host.CurrentTownProvider?.Invoke() ?? 0),
            _ => 0,
        };

        private int RequestElevator(int elevatorType)
        {
            _host.PendingElevator = (_self.HexTile, elevatorType);
            return 0;
        }

        public int GameTime() => (int)(_host.ClockTicks?.Invoke() ?? 302400);

        // ---- timers + geometry + caps (phase-5 M0)

        public void AddTimerEvent(int objectHandle, int delayTicks, int param)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.AddTimer(_map, obj, delayTicks, param);
        }

        public void RemoveTimerEvents(int objectHandle)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.RemoveTimers(obj);
        }

        public void RemoveTimerEventsWithParam(int objectHandle, int param)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.RemoveTimers(obj, param);
        }

        public int ObjTile(int objectHandle) => _host.ObjectOf(objectHandle)?.HexTile ?? -1;

        // P54-M2: elevation/critter_injure/anim — VC needs these; inert on the existing slice (not fired).
        public int ObjElevation(int objectHandle) =>
            _host.ObjectOf(objectHandle) is { } obj ? _host.ElevationProvider?.Invoke(obj) ?? 0 : 0;

        public void CritterInjure(int objectHandle, int flags)
        {
            if (_host.ObjectOf(objectHandle) is not { } critter)
                return;
            int crip = flags & 0x7C; // DAM_CRIP: crippled legs/arms + blind (opCritterInjure masks to this)
            critter.CombatResults = (flags & 0x800000) != 0 // DAM_PERFORM_REVERSE → clear instead of set
                ? critter.CombatResults & ~crip
                : critter.CombatResults | crip;
        }

        public void Anim(int objectHandle, int anim, int frame)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.AnimRequested?.Invoke(obj, anim);
        }

        // P56-M1: critter_inven_obj — the handle of the worn/in-hand item or the inventory count.
        public int CritterInventoryObject(int objectHandle, int type)
        {
            if (_host.ObjectOf(objectHandle) is not { } critter)
                return 0;
            if (type == 3) // INVEN_TYPE_INV_COUNT
                return critter.Inventory.Count;
            int flag = type switch
            {
                0 => MapObject.FlagWorn,        // INVEN_TYPE_WORN
                1 => MapObject.FlagInRightHand, // INVEN_TYPE_RIGHT_HAND
                2 => MapObject.FlagInLeftHand,  // INVEN_TYPE_LEFT_HAND
                _ => 0,
            };
            return flag != 0 && critter.Inventory.FirstOrDefault(it => (it.Flags & flag) != 0) is { } item
                ? _host.HandleOf(item) : 0;
        }

        // P56-M2: set_map_start repositions the dude/camera; kill_critter_type destroys a proto type.
        public void SetMapStart(int x, int y, int elevation, int rotation) =>
            _host.SetMapStartRequested?.Invoke(x, y, elevation, rotation);

        public void KillCritterType(int pid, int deathFrame)
        {
            if (_host.IsLoadingGame()) // engine: never destroy critters during a load/save replay (:2384)
                return;
            _host.KillCritterTypeRequested?.Invoke(pid, deathFrame);
        }

        // P0 (campaign port): the critter-state EFFECT externals. ported from fallout2-ce
        // interpreter_extra.cc opCritterHeal / opGetPoison / opKillCritter.

        // critter_heal → critter.cc critterAdjustHitPoints: add amount, clamp to STAT_MAXIMUM_HIT_POINTS(7);
        // a drop to ≤0 kills (critterKill(-1)). Returns the engine's rc (always 0).
        public int CritterHeal(int objectHandle, int amount)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || Fid.PidType(obj.Pid) != 1)
                return 0;
            int maxHp = _host.CritterStatValue(obj, 7); // STAT_MAXIMUM_HIT_POINTS
            int newHp = obj.CurrentHp + amount;
            if (newHp <= maxHp)
            {
                obj.CurrentHp = newHp;
                if (newHp <= 0 && !obj.IsDead)
                    _host.KillCritterRequested?.Invoke(obj, -1);
            }
            else
            {
                obj.CurrentHp = maxHp;
            }
            return 0;
        }

        // get_poison → critter.cc critterGetPoison: the poison counter (0 for null/non-critter).
        public int GetPoison(int objectHandle) =>
            _host.ObjectOf(objectHandle) is { } obj && Fid.PidType(obj.Pid) == 1 ? obj.Poison : 0;

        // kill_critter → critter.cc critterKill(object, deathFrame, 1). Never during a load replay.
        public void KillCritter(int objectHandle, int deathFrame)
        {
            if (_host.IsLoadingGame())
                return;
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.KillCritterRequested?.Invoke(obj, deathFrame);
        }

        // critter_rm_trait → the engine ONLY removes perks (kind 0, looping perkRemove to rank 0); other
        // kinds are no-op errors. Restricted to the dude — Hexwaste's mutable perk store (_dudePerkRanks)
        // is the dude's; non-dude perk removal is a no-op (no slice script targets a non-dude). Pushes -1.
        public int CritterRemoveTrait(int objectHandle, int kind, int param, int value)
        {
            if (kind == 0 && _host.ObjectOf(objectHandle) is { } obj && obj == _dude && Fid.PidType(obj.Pid) == 1)
                _host.PerkRemoveRequested?.Invoke(param);
            return -1;
        }

        // use_obj_on_obj → run the use_obj_on_p_proc chain (item then target). ported from
        // interpreter_extra.cc opUseObjectOnObject → _obj_use_item_on.
        public void UseObjectOnObject(int targetHandle, int itemHandle)
        {
            if (_host.ObjectOf(itemHandle) is { } item && _host.ObjectOf(targetHandle) is { } target)
                _host.UseObjOnObjRequested?.Invoke(item, target);
        }

        // critter_mod_skill → opCritterModifySkill: dude-only (the engine errors for anyone else). The host
        // applies the tagged-halving + value cap against the dude's skill-points array. Always pushes 0.
        public int CritterModSkill(int objectHandle, int skill, int points)
        {
            if (points != 0 && _host.ObjectOf(objectHandle) is { } obj && obj == _dude && Fid.PidType(obj.Pid) == 1)
                _host.CritterModSkillRequested?.Invoke(skill, points);
            return 0;
        }

        // scripts_request_world_map → scriptsRequestWorldMap: leave to the worldmap (the host defers it).
        public void RequestWorldMap() => _host.WorldMapRequested?.Invoke();

        // wm_area_set_pos → wmAreaSetWorldPos: move a worldmap area marker.
        public void WmAreaSetPos(int city, int x, int y) => _host.WmAreaSetPosRequested?.Invoke(city, x, y);

        // dialogue_system_enter → opGameDialogSystemEnter: request a dialog with self (gGameDialogSpeaker).
        // The engine suppresses it in combat and, for a critter self, requires it be active. The viewer
        // picks up PendingDialogSpeaker after the use_p_proc and opens that object's talk_p_proc.
        public void DialogueSystemEnter()
        {
            if (IsInCombat())
                return;
            if (Fid.PidType(_self.Pid) == 1 && (_self.IsDead || _self.IsHidden)) // critter self must be active
                return;
            _host.PendingDialogSpeaker = _self;
        }

        // load_map → opLoadMap: set GVAR_LOAD_MAP_INDEX (=27, game_vars.h) so the target map's map_enter
        // can read the caller's param, then defer the transition (default start, tile/elev/rot = -1).
        private const int GvarLoadMapIndex = 27;

        public void LoadMap(int mapIndex, int param)
        {
            if (mapIndex < 0) // engine: a negative index sets neither the gvar nor a transition
                return;
            SetGlobalVar(GvarLoadMapIndex, param);
            _host.LoadMapRequested?.Invoke(mapIndex);
        }

        public void LoadMapByName(string mapName, int param)
        {
            SetGlobalVar(GvarLoadMapIndex, param);
            int idx = _host.MapIndexByNameProvider?.Invoke(mapName) ?? -1;
            if (idx >= 0)
                _host.LoadMapRequested?.Invoke(idx);
        }

        private const int CritterManeuverFleeing = 0x04; // CRITTER_MANUEVER_FLEEING (obj_types.h)

        // ported from fallout2-ce src/interpreter_extra.cc opAttackSetup (0x8143): a script forces combat
        // between two critters. A dead/inactive/invisible attacker or defender — or a fleeing defender — aborts
        // it (critterIsActive, critter.cc:928); otherwise the attacker engages the defender and the host opens
        // (or joins) the fight. Both must be critters; never fire mid-load (a map_enter caller must not start
        // combat before the map is live).
        public void AttackSetup(int attackerHandle, int defenderHandle)
        {
            if (_host.IsLoadingGame())
                return;
            if (_host.ObjectOf(attackerHandle) is not { } attacker || Fid.PidType(attacker.Pid) != 1)
                return;
            if (_host.ObjectOf(defenderHandle) is not { } defender || Fid.PidType(defender.Pid) != 1)
                return;
            if (attacker.IsDead || attacker.IsHidden || defender.IsDead || defender.IsHidden)
                return;
            if ((defender.Maneuver & CritterManeuverFleeing) != 0)
                return;
            _host.AttackSetupRequested?.Invoke(attacker, defender);
        }

        // ported from fallout2-ce src/interpreter_extra.cc opExplosion (0x811A): a script detonates a blast on
        // a tile/elevation. A -1 tile is rejected; minDamage is 1 unless maxDamage is 0 (then 0). The engine
        // defers it (scriptsRequestExplosion → actionExplode with a null source), so the blast is environmental
        // — no attacker is credited.
        public void Explosion(int tile, int elevation, int maxDamage)
        {
            if (tile == -1)
                return;
            int minDamage = maxDamage == 0 ? 0 : 1;
            _host.ExplosionRequested?.Invoke(tile, elevation, minDamage, maxDamage);
            // fo2ce opExplosion → actionExplode ends with gameUiEnable() (actions.cc:1793) — clears a trap's
            // game_ui_disable so an explosion-only trap (e.g. the Golgotha grave niWilGrv) can't soft-lock.
            _host.GameUiEnabledRequested?.Invoke(true);
        }

        // P57: set_exit_grids retargets exit-grid objects; wield_obj_critter equips an item on a critter.
        public void SetExitGrids(int elevation, int destMap, int destElevation, int destTile) =>
            _host.SetExitGridsRequested?.Invoke(elevation, destMap, destElevation, destTile);

        public void WieldObjCritter(int critterHandle, int itemHandle)
        {
            // engine opWieldItem: null-guard both, reject a non-critter target (interpreter_extra.cc:1694).
            if (_host.ObjectOf(critterHandle) is { } critter && _host.ObjectOf(itemHandle) is { } item)
                _host.WieldObjCritterRequested?.Invoke(critter, item);
        }

        // P58 (New Reno): the object/critter queries + the two world-state mutators.
        public int ObjArtFid(int objectHandle) => _host.ObjectOf(objectHandle)?.Fid ?? 0;

        public bool CritterIsFleeing(int objectHandle) =>
            _host.ObjectOf(objectHandle) is { } c && (c.Maneuver & 0x04) != 0; // CRITTER_MANUEVER_FLEEING

        public void CritterSetFleeState(int objectHandle, int fleeing)
        {
            if (_host.ObjectOf(objectHandle) is not { } c)
                return;
            c.Maneuver = fleeing != 0 ? c.Maneuver | 0x04 : c.Maneuver & ~0x04; // CRITTER_MANUEVER_FLEEING
        }

        public void MarkAreaKnown(int markType, int areaId, int mode) =>
            _host.MarkAreaKnownRequested?.Invoke(markType, areaId, mode);

        public void GameTimeAdvance(int ticks) => _host.GameTimeAdvanceRequested?.Invoke(ticks);

        // P63 (Sierra Army Depot): a tile-object-pid query + a cosmetic stand animation.
        public bool TileContainsObjPid(int tile, int elevation, int pid) =>
            _host.TileContainsObjPidProvider?.Invoke(tile, elevation, pid) ?? false;

        // ---- P101 Tier B (queries) ----
        public int ItemSubtype(int objectHandle)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || Fid.PidType(obj.Pid) != 0 /* OBJ_TYPE_ITEM */)
                return -1;                       // null / non-item → -1 (opGetItemType default)
            if (obj.Pid == 383) return 5;        // the shiv → MISC (item.cc:712 special-case)
            try { return _host.Protos.Get(obj.Pid).SubType; } catch { return -1; }
        }

        public int ProtoData(int pid, int member)
        {
            Proto.ProtoInfo p;
            try { p = _host.Protos.Get(pid); } catch { return 0; }
            switch (member) // universal Proto-header members (same across all object types)
            {
                case 0: return p.Pid;            // *_DATA_MEMBER_PID
                case 3: return p.Fid;            // FID
                case 6: return p.Flags;          // FLAGS
                case 7: return p.ExtendedFlags;  // EXTENDED_FLAGS
            }
            if (Fid.PidType(pid) == 0) // OBJ_TYPE_ITEM — item-specific members
                switch (member)
                {
                    case 9: return p.SubType;          // ITEM_DATA_MEMBER_TYPE
                    case 12: return p.Size;            // SIZE
                    case 13: return p.Weight;          // WEIGHT
                    case 14: return p.Cost;            // COST
                    case 15: return p.InventoryFid;    // INVENTORY_FID
                    case 555: return p.Weapon?.MaxRange1 ?? 0; // WEAPON_RANGE
                }
            // P113 (item 7b): CRITTER_DATA_MEMBER_BODY_TYPE (11) — proto.cc:1200-1202. (The item
            // member 11 = MATERIAL stays 0, documented.)
            if (Fid.PidType(pid) == 1 /* OBJ_TYPE_CRITTER */ && member == 11)
                return p.Critter?.BodyType ?? 0;
            return 0; // player-pid special case + remaining type-specific members → 0 (documented cut)
        }

        /// <summary>P113 (item 7b): the STRING proto_data members — NAME (1) / DESCRIPTION (2),
        /// proto.cc protoGetDataMember; the engine falls back to proto.msg entry 10 ("&lt;None&gt;").</summary>
        public string? ProtoDataString(int pid, int member)
        {
            if (member is not (1 or 2))
                return null;
            string? text;
            try
            {
                text = member == 1 ? _host.ProtoText.GetName(pid) : _host.ProtoText.GetDescription(pid);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
                text = null;
            }
            return text ?? "<None>";
        }

        public int TileIsVisible(int tile)
        {
            if (_host.CenterTileProvider is not { } prov) return 0; // headless / no camera → not visible
            int d = Math.Abs(prov() - tile);
            return d % 200 < 5 || d / 200 < 5 ? 1 : 0; // tileIsVisible (interpreter_extra.cc:401)
        }

        public int InvenPtr(int objectHandle, int index)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || index < 0 || index >= obj.Inventory.Count)
                return 0;
            return _host.HandleOf(obj.Inventory[index]);
        }

        // ---- P101 Tier C (object/inventory/script-return) ----
        /// <summary>scr_return's stored value (opScrReturn). Currently store-only — the engine's
        /// use_obj_on fallthrough gate change (Overridden → ReturnValue) is a documented deferred item.</summary>
        public int ReturnValue { get; private set; }
        public void ScrReturn(int value) => ReturnValue = value;

        public void InvenUnwield() => _host.InvenUnwieldRequested?.Invoke(_self);

        public void UseObj(int objectHandle)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.UseObjRequested?.Invoke(obj);
        }

        public void DropObj(int objectHandle)
        {
            if (_host.ObjectOf(objectHandle) is { } item)
                _host.DropObjRequested?.Invoke(_self, item);
        }

        // ---- P101 Tier D (radiation) ----
        public void Radiation(int objectHandle, int amount)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.RadiationRequested?.Invoke(obj, amount);
        }

        public void ScreenFade(bool fadeIn) => _host.ScreenFadeRequested?.Invoke(fadeIn);

        public void PlaySfx(string name) => _host.PlaySfxRequested?.Invoke(name);

        public void AnimateStand(int objectHandle)
        {
            if (_host.ObjectOf(objectHandle) is { } obj) // engine falls back to self on null (documented cut, like the reverse twin)
                _host.AnimateStandRequested?.Invoke(obj);
        }

        public void AnimateStandReverse(int objectHandle)
        {
            // engine: falls back to self if the handle is null (interpreter_extra.cc:1366) — the slice
            // passes an explicit object, so the self-fallback is a documented cut.
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.AnimateStandReverseRequested?.Invoke(obj);
        }

        public int CurrentMapIndex() => _host.CurrentMapIndexProvider?.Invoke() ?? 0;

        public int CapsTotal(int objectHandle) =>
            _host.ObjectOf(objectHandle) is { } obj ? _host.CapsTotal(obj) : 0;

        public int CapsAdjust(int objectHandle, int amount) =>
            _host.ObjectOf(objectHandle) is { } obj ? _host.CapsAdjust(obj, amount) : -1;

        /// <summary>PoC sight: within 20 hexes, no wall LOS (engine uses PE*2 + obstacles).</summary>
        public bool ObjCanSee(int objectHandle, int targetHandle)
        {
            MapObject? source = _host.ObjectOf(objectHandle);
            MapObject? target = _host.ObjectOf(targetHandle);
            if (source is null || target is null || source.HexTile == -1 || target.HexTile == -1)
                return false; // interpreter_extra.cc:1790-1794 (null / off-map → false)
            return _host.ObjCanSeeResolver is { } r
                ? r(source, target)
                : Hex.HexGrid.Distance(source.HexTile, target.HexTile) <= 20; // headless fallback
        }

        public bool ObjCanHear(int objectHandle, int targetHandle)
        {
            MapObject? source = _host.ObjectOf(objectHandle);
            MapObject? target = _host.ObjectOf(targetHandle);
            if (source is null || target is null || source.HexTile == -1 || target.HexTile == -1)
                return false; // interpreter_extra.cc:2630-2632
            return _host.ObjCanHearResolver is { } r
                ? r(source, target)
                : Hex.HexGrid.Distance(source.HexTile, target.HexTile) <= 20; // headless fallback
        }

        public void AnimateMoveToTile(int objectHandle, int tile, int speed)
        {
            if (_host.ObjectOf(objectHandle) is { } obj && Hex.HexGrid.IsValid(tile))
                _host.MoveRequested?.Invoke(obj, tile);
        }

        public void SetLightLevel(int level) => _host.LightLevelRequested?.Invoke(level);

        public void SetObjectLightLevel(int objectHandle, int intensity, int distance)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.ObjectLightRequested?.Invoke(obj, intensity, distance);
        }

        public void RegAnimAnimateForever(int objectHandle, int anim)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.AnimateForeverRequested?.Invoke(obj, anim);
        }

        // ---- reg_anim batch (interpreter_extra.cc opRegAnimFunc/opRegAnim*). begin opens
        // a batch, the register ops accumulate resolved actions, end flushes them to the
        // host. The list lives on the context (begin/end pair within one proc run).
        private readonly List<RegAnimAction> _regAnim = [];

        public void RegAnimBegin() => _regAnim.Clear();

        public void RegAnimEnd()
        {
            if (_regAnim.Count > 0)
                _host.RegAnimRequested?.Invoke(_regAnim.ToArray());
            _regAnim.Clear();
        }

        public void RegAnimClear(int objectHandle)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.RegAnimClearRequested?.Invoke(obj);
        }

        public void RegAnimMoveToTile(int objectHandle, int tile, int delay, bool run)
        {
            if (_host.ObjectOf(objectHandle) is { } obj && Hex.HexGrid.IsValid(tile))
                _regAnim.Add(new RegAnimAction(
                    run ? RegAnimKind.RunToTile : RegAnimKind.MoveToTile, obj, tile, null, 0, delay));
        }

        public void RegAnimMoveToObject(int objectHandle, int destHandle, int delay, bool run)
        {
            if (_host.ObjectOf(objectHandle) is { } obj && _host.ObjectOf(destHandle) is { } dest)
                _regAnim.Add(new RegAnimAction(
                    run ? RegAnimKind.RunToObject : RegAnimKind.MoveToObject, obj, -1, dest, 0, delay));
        }

        public void RegAnimAnimate(int objectHandle, int anim, int delay, bool reverse)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _regAnim.Add(new RegAnimAction(
                    reverse ? RegAnimKind.AnimateReverse : RegAnimKind.Animate, obj, -1, null, anim, delay));
        }

        public int GetCritterStat(int objectHandle, int stat) =>
            _host.ObjectOf(objectHandle) is { } obj ? _host.CritterStatValue(obj, stat) : -1;

        // has_skill (opHasSkill 0x80AA): the critter's effective skill value; 0 for null/non-critter
        // (the engine's result default). P74-M3.
        public int HasSkill(int objectHandle, int skill) =>
            _host.ObjectOf(objectHandle) is { } obj ? _host.CritterSkillValue(obj, skill) : 0;

        // ported from fallout2-ce interpreter_extra.cc opSetCritterStat():
        // ADJUSTS the base stat; only the dude is modifiable.
        public int AdjustCritterBaseStat(int objectHandle, int stat, int amount)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj)
                return -1;
            if (obj != _dude || stat is < 0 or > 34)
                return -1;
            if (_host.StatsOf(obj) is not { } stats)
                return -1;
            stats.BaseStats[stat] += amount;
            return 0;
        }

        // ported from fallout2-ce interpreter_extra.cc opHasTrait()
        public int HasTrait(int type, int objectHandle, int param)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj)
                return 0;
            return type switch
            {
                0 => obj == _dude ? _host.PerkRankProvider?.Invoke(param) ?? 0 : 0, // CRITTER_TRAIT_PERK (P28-M2)
                1 => param switch // CRITTER_TRAIT_OBJECT
                {
                    5 => obj.AiPacket,
                    6 => obj.Team,
                    10 => obj.Rotation,
                    666 => obj.IsHidden ? 0 : 1,
                    669 => 0, // inventory weight — unweighted PoC
                    _ => 0,
                },
                2 => _host.DudeTraits.Contains(param) ? 1 : 0, // CRITTER_TRAIT_TRAIT
                _ => 0,
            };
        }

        // ported from fallout2-ce src/stat.cc statRoll(): d10 vs SPECIAL+mod
        // (opDoCheck restricts to the seven primary stats).
        public int DoCheck(int objectHandle, int stat, int modifier)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || stat is < 0 or > 6)
                return 1; // ROLL_FAILURE
            int value = _host.CritterStatValue(obj, stat) + modifier;
            return _host.Rng.Next(1, 11) <= value ? 2 : 1; // ROLL_SUCCESS : ROLL_FAILURE
        }

        public int GetPcStat(int stat) => _host.PcStatProvider?.Invoke(stat) ?? 0;
        public int GetKillCount(int killType) => _host.KillCountProvider?.Invoke(killType) ?? 0;

        public bool CarIsOutOfGas() => _host.Car.IsOutOfGas; // P100 (Point 4): metarule3 110

        // ported from fallout2-ce interpreter_extra.cc opCritterAddTrait():
        // kind 1 = object traits; perks (kind 0) are out of PoC scope.
        public void CritterAddTrait(int objectHandle, int kind, int param, int value)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || Fid.PidType(obj.Pid) != 1)
                return;
            if (kind != 1)
                return;
            switch (param)
            {
                case 5: // CRITTER_TRAIT_OBJECT_AI_PACKET
                    obj.AiPacket = value;
                    break;
                case 6: // CRITTER_TRAIT_OBJECT_TEAM
                    obj.Team = value;
                    break;
            }
        }

        // ported from fallout2-ce interpreter_extra.cc opAttackComplex():
        // inactive/hidden parties and fleeing targets are ignored.
        public void AttackComplex(int targetHandle)
        {
            if (_host.ObjectOf(targetHandle) is not { } target)
                return;
            if (_self.IsDead || _self.IsHidden || target.IsDead || target.IsHidden)
                return;
            // ported from fallout2-ce interpreter_extra.cc _op_attack (:1860): a script attack marks the
            // attacker ENGAGING (CRITTER_MANEUVER_ENGAGING = 0x01) so its want-to-join returns true (P35-M4).
            _self.Maneuver |= 0x01;
            _host.AttackRequested?.Invoke(_self, target);
        }

        public bool AnimBusy(int objectHandle) =>
            _host.ObjectOf(objectHandle) is { } obj && (_host.AnimBusyResolver?.Invoke(obj) ?? false);

        // ported from fallout2-ce interpreter_extra.cc opCritterAttemptPlacement (0x80FF).
        public bool CritterAttemptPlacement(int critterHandle, int tile, int elevation)
        {
            if (_host.ObjectOf(critterHandle) is not { } critter)
                return false;
            return _host.PlaceObjectRequested?.Invoke(critter, tile, elevation) ?? false;
        }

        public void GiveExpPoints(int amount) => _host.ExpAwarded?.Invoke(amount);

        public void PlayMovie(int movieId) => _host.MoviePlayed?.Invoke(movieId);

        public void EndgameSlideshow() => _host.EndgameSlideshowRequested?.Invoke();

        public void EndgameMovie() => _host.EndgameMovieRequested?.Invoke();

        public void GameUiEnabled(bool enabled) => _host.GameUiEnabledRequested?.Invoke(enabled);

        public void PartyAdd(int objectHandle)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || _host.PartyMembers.Contains(obj))
                return;
            _host.PartyMembers.Add(obj);
            _host.PartyChanged?.Invoke(obj, true);
        }

        public void PartyRemove(int objectHandle)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || !_host.PartyMembers.Remove(obj))
                return;
            _host.PartyChanged?.Invoke(obj, false);
        }

        public int PartyMemberByPid(int pid) =>
            _host.HandleOf(_host.PartyMembers.FirstOrDefault(m => m.Pid == pid));

        // ported from fallout2-ce interpreter_extra.cc opCritterDamage()
        public void CritterDamage(int objectHandle, int amount, int damageTypeWithFlags)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || Fid.PidType(obj.Pid) != 1)
                return;
            _host.CritterDamaged?.Invoke(obj, amount, (damageTypeWithFlags & 0x100) != 0);
            // fo2ce opCritterDamage → actionDamage ends with gameUiEnable() (actions.cc:1955). gGameUiDisabled
            // is a bool, so this clears a trap's earlier game_ui_disable — mines/pits that damage-then-rely on
            // the action to re-enable (e.g. IIPit) would otherwise soft-lock the player.
            _host.GameUiEnabledRequested?.Invoke(true);
        }

        // ported from fallout2-ce interpreter_extra.cc opOverrideMapStart()
        public void OverrideMapStart(int x, int y, int elevation, int rotation)
        {
            int tile = 200 * y + x;
            if (Hex.HexGrid.IsValid(tile))
                _host.MapStartOverridden?.Invoke(tile, elevation, rotation);
        }

        // ---- dialog state (one "round" = one reply + its options)

        public string DialogReplyText { get; private set; } = "";
        public List<(string Text, int ProcedureIndex, int Reaction)> DialogOptions { get; } = [];
        public bool SessionEnded { get; private set; }

        public void ResetDialogRound()
        {
            DialogReplyText = "";
            DialogOptions.Clear();
            Messages.Clear();
            // Clear the sticky end-of-dialogue flag before running the next option's
            // proc. In the engine, gsay_end BLOCKS and runs the whole conversation, so
            // talk_p_proc's trailing `end_dialogue` only fires once the player is done;
            // our gsay_end is non-blocking, so that trailing end_dialogue sets
            // SessionEnded eagerly during StartDialog. Without clearing it here, the
            // first Choose would see the stale flag and kill an otherwise-valid round
            // (the real multi-round dialog blocker — #10 M0). A genuine "goodbye" node
            // re-sets it by calling end_dialogue from inside the proc we run next.
            SessionEnded = false;
        }

        /// <summary>P87: the talking-head art the script handed to start_gdialog (interpreter_extra.cc
        /// opStartGameDialog → the FID passed to gdialogInitFromScript), or -1 for a head-less dialog.
        /// Set once per session by <see cref="DialogSessionStart"/>; it persists across dialog rounds
        /// (ResetDialogRound/DialogStart leave it alone) until the next start_gdialog.</summary>
        public int DialogHeadId { get; private set; } = -1;

        public void DialogSessionStart(int headId, int backgroundId) => DialogHeadId = headId;

        public void DialogStart()
        {
            DialogReplyText = "";
            DialogOptions.Clear();
        }

        public void DialogReply(string text) => DialogReplyText = text;

        public void DialogOption(string text, int procedureIndex, int reaction) =>
            DialogOptions.Add((text, procedureIndex, reaction));

        public void DialogEnd()
        {
            // _gdialogGo: reply with no options auto-gets a "[Done]" exit.
            if (DialogReplyText.Length > 0 && DialogOptions.Count == 0)
                DialogOptions.Add(("[Done]", -1, 50));
        }

        public void DialogSessionEnd() => SessionEnded = true;

        // ported from fallout2-ce interpreter_extra.cc _op_giq_option: the dude's real
        // STAT_INTELLIGENCE gates dumb/smart dialogue options (P25). The Smooth Talker perk
        // bonus is out of scope (no perk system). Null dude → 5 (the pre-P25 neutral default).
        // P114: Smooth Talker raises the effective INT for giq/intelligence-gated dialogue options by
        // +1 per rank (interpreter_extra.cc _op_giq_option:3866-3867, before the sign test). Inert at rank 0.
        public int DialogIntelligence() => _dude is { } d
            ? _host.CritterStatValue(d, 4) + (_host.PerkRankProvider?.Invoke(Perks.PerkId.SmoothTalker) ?? 0)
            : 5;

        // ported from fallout2-ce interpreter_extra.cc opUsingSkill (0x454634): only
        // using_skill(dude, SKILL_SNEAK=8) is meaningful — it returns the SNEAKING FLAG
        // (dudeHasState, NOT the active _sneak_working). Everything else → false.
        public bool IsUsingSkill(int objectHandle, int skill) =>
            skill == 8 && _host.ObjectOf(objectHandle) == _dude && (_host.SneakFlagProvider?.Invoke() ?? false);

        // ported from fallout2-ce interpreter_extra.cc opCombatIsInitialized (0x8128).
        public bool IsInCombat() => _host.CombatActiveProvider?.Invoke() ?? false;

        // ported from fallout2-ce interpreter_extra.cc opGetCritterState (0x80FB) — see ScriptHost.CritterStateOf.
        public int CritterState(int objectHandle) => ScriptHost.CritterStateOf(_host.ObjectOf(objectHandle));

        // ported from fallout2-ce interpreter_extra.cc opPoison (0x8122 → critterAdjustPoison).
        public void Poison(int objectHandle, int amount)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.PoisonRequested?.Invoke(obj, amount);
        }

        // ported from fallout2-ce interpreter_extra.cc opTerminateCombat (0x8153): end combat + mark
        // self DISENGAGING (CRITTER_MANEUVER_DISENGAGING = 0x02). The host ends the fight.
        public void TerminateCombat()
        {
            _self.Maneuver |= 0x02;
            _host.CombatTerminateRequested?.Invoke();
        }

        public void FloatMessage(int objectHandle, string text, int type)
        {
            if (!string.IsNullOrWhiteSpace(text))
                Messages.Add(text.Trim());
        }

        // ported from fallout2-ce game_dialog.cc gameDialogBarter(): the
        // opcode only flags the session; its arg OVERWRITES the modifier.
        public bool BarterRequested { get; private set; }
        public int BarterModifier { get; private set; }

        public void Barter(int modifier)
        {
            BarterModifier = modifier;
            BarterRequested = true;
        }

        public void GdialogSetBarterMod(int modifier) => BarterModifier = modifier;

        public bool TakeBarterRequest(out int modifier)
        {
            modifier = BarterModifier;
            bool requested = BarterRequested;
            BarterRequested = false;
            return requested;
        }

        // ported from fallout2-ce interpreter_extra.cc
        // opMoveObjectInventoryToObject(): everything moves, stacks merge.
        /// <summary>Where the talk script parked its stock: shopkeepers load
        /// goods from a box in the talk_p_proc prologue and return them in the
        /// epilogue — which, in our run-to-completion dialog model, has already
        /// executed by the time the trade window opens. The last container the
        /// npc moved its inventory INTO is the live stock.</summary>
        public MapObject? StockBox { get; private set; }

        public void MoveAllInventory(int sourceHandle, int targetHandle)
        {
            if (_host.ObjectOf(sourceHandle) is not { } source
                || _host.ObjectOf(targetHandle) is not { } target || source == target)
                return;

            if (source == _self && target != _dude)
                StockBox = target;

            foreach (MapObject item in source.Inventory.ToList())
            {
                if (target.Inventory.FirstOrDefault(i => i.Pid == item.Pid) is { } existing)
                    existing.StackCount += Math.Max(item.StackCount, 1);
                else
                    target.Inventory.Add(item);
            }

            source.Inventory.Clear();
        }

        // ---- door/container state (handle 0 no-ops like the engine)

        public bool ObjIsLocked(int objectHandle) =>
            _host.ObjectOf(objectHandle)?.IsLockedState ?? false;

        public void ObjSetLocked(int objectHandle, bool locked)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                obj.IsLockedState = locked;
        }

        public bool ObjIsOpen(int objectHandle) =>
            _host.ObjectOf(objectHandle) is { } obj && (_host.IsOpenResolver?.Invoke(obj) ?? false);

        public void ObjSetOpen(int objectHandle, bool open)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.OpenStateChanged?.Invoke(obj, open);
        }

        // ---- world mutation (phase-4 M3)

        public int CreateObject(int pid, int tile, int elevation, int scriptIndex = -1)
        {
            Proto.ProtoInfo proto;
            try
            {
                proto = _host.Protos.Get(pid);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
                Console.Error.WriteLine($"create_object: bad pid 0x{pid:X8}: {ex.Message}");
                return 0;
            }

            var obj = new MapObject
            {
                Id = -3,
                HexTile = tile == -1 ? 0 : tile, // engine quirk: -1 coerced to 0
                X = 0,
                Y = 0,
                Frame = 0,
                Rotation = 0,
                Fid = proto.Fid,
                Flags = 0,
                Pid = pid,
                Sid = -1,
            };

            // Script binding (engine scr_new + scriptSetScriptIndex): allocate
            // a fresh sid so the new object's procs (use_skill_on disarm,
            // examine) actually run.
            if (scriptIndex >= 0)
            {
                int sid = _host.AllocateSid(_map, scriptIndex);
                obj.Sid = sid;
            }

            if (elevation is >= 0 and < MapFile.ElevationCount && _map.Elevations[elevation] is { } elev)
            {
                elev.Objects.Add(obj);
                _host.ObjectPlaced?.Invoke(obj, _map);
            }

            return _host.HandleOf(obj);
        }

        public void DestroyObject(int objectHandle)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj)
                return;

            foreach (MapElevation? elev in _map.Elevations)
                elev?.Objects.Remove(obj);
            foreach (MapElevation? elev in _map.Elevations)
                if (elev is not null)
                    foreach (MapObject holder in elev.Objects)
                        holder.Inventory.Remove(obj);
            _host.ObjectRemoved?.Invoke(obj);
        }

        public void AddToInventory(int targetHandle, int itemHandle, int quantity)
        {
            if (_host.ObjectOf(targetHandle) is not { } target || _host.ObjectOf(itemHandle) is not { } item)
                return;

            foreach (MapElevation? elev in _map.Elevations)
                elev?.Objects.Remove(item);
            _host.ObjectRemoved?.Invoke(item);

            // Merge stacks of the same prototype like itemAdd does.
            if (target.Inventory.FirstOrDefault(i => i.Pid == item.Pid) is { } existing)
                existing.StackCount += Math.Max(quantity, 1);
            else
            {
                item.StackCount = Math.Max(quantity, 1);
                target.Inventory.Add(item);
            }
        }

        public int RemoveFromInventory(int targetHandle, int itemHandle, int quantity)
        {
            if (_host.ObjectOf(targetHandle) is not { } target || _host.ObjectOf(itemHandle) is not { } item)
                return 0;

            MapObject? held = target.Inventory.FirstOrDefault(i => i == item || i.Pid == item.Pid);
            if (held is null)
                return 0;

            int removed = Math.Min(Math.Max(quantity, 1), held.StackCount);
            held.StackCount -= removed;
            if (held.StackCount <= 0)
                target.Inventory.Remove(held);
            return removed;
        }

        public int MoveTo(int objectHandle, int tile, int elevation)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj)
                return -1;

            foreach (MapElevation? elev in _map.Elevations)
                elev?.Objects.Remove(obj);
            obj.HexTile = tile;
            if (elevation is >= 0 and < MapFile.ElevationCount && _map.Elevations[elevation] is { } targetElev)
            {
                targetElev.Objects.Add(obj);
                _host.ObjectPlaced?.Invoke(obj, _map);
            }

            return tile;
        }

        public void SetObjectVisibility(int objectHandle, bool hidden)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || obj.IsHidden == hidden)
                return;

            obj.Flags = hidden ? obj.Flags | 0x01 : obj.Flags & ~0x01;
            if (hidden)
                _host.ObjectRemoved?.Invoke(obj);
            else
                _host.ObjectPlaced?.Invoke(obj, _map);
        }

        public int ObjPid(int objectHandle) => _host.ObjectOf(objectHandle)?.Pid ?? -1;

        public int ObjIsCarryingPid(int objectHandle, int pid) =>
            _host.ObjectOf(objectHandle) is { } owner ? InventoryScan.CountByPid(owner, pid) : 0;

        public int ObjCarryingPidObj(int objectHandle, int pid) =>
            _host.HandleOf(_host.ObjectOf(objectHandle) is { } owner ? InventoryScan.FindByPid(owner, pid) : null);

        public int TileContainsPidObj(int tile, int elevation, int pid)
        {
            if (elevation is < 0 or >= MapFile.ElevationCount || _map.Elevations[elevation] is not { } elev)
                return 0;
            MapObject? found = elev.Objects.FirstOrDefault(o => o.HexTile == tile && o.Pid == pid);
            return _host.HandleOf(found);
        }
    }
}
