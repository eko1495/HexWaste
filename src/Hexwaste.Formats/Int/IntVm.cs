namespace Hexwaste.Formats.Int;

/// <summary>
/// Host services a script may call. Only the externals needed for the
/// examine/door paths are real; every other external opcode is arity-stubbed
/// by the VM (arguments popped, 0 pushed when the builtin returns a value).
/// </summary>
public interface IVmExternals
{
    /// <summary>display_msg (fallout2-ce interpreter_extra.cc opDisplayMsg).</summary>
    void DisplayMessage(string text);

    /// <summary>message_str (opGetMessageString): text for a line of a message list.</summary>
    string GetMessage(int messageListId, int id);

    /// <summary>script_overrides (opScriptOverrides).</summary>
    void SetScriptOverrides();

    /// <summary>self_obj (opGetSelf) as an opaque int handle.</summary>
    int SelfObjectId();

    /// <summary>obj_name (opGetObjectName).</summary>
    string ObjectName(int objectHandle);

    /// <summary>get_global_var (opGetGlobalVar); returning 0 is fine.</summary>
    int GetGlobalVar(int index);

    /// <summary>get_local_var (opGetLocalVar); returning 0 is fine.</summary>
    int GetLocalVar(int index);

    /// <summary>get_map_var (opGetMapVar).</summary>
    int GetMapVar(int index);

    // ---- script-context protocol (phase-4 M0). Defaults preserve the old
    // stub behavior so simple hosts (tests) keep working unchanged.

    /// <summary>set_local_var (opSetLocalVar) — writes mapLocalVars[script.localVarsOffset + index].</summary>
    void SetLocalVar(int index, int value) { }

    /// <summary>set_global_var (opSetGlobalVar).</summary>
    void SetGlobalVar(int index, int value) { }

    /// <summary>set_map_var (opSetMapVar).</summary>
    void SetMapVar(int index, int value) { }

    /// <summary>source_obj (opGetSource) — the object that triggered the proc (usually the dude).</summary>
    int SourceObjectId() => 0;

    /// <summary>target_obj (opGetTarget) — defaults to self per scriptExecProc (scripts.cc:1316).</summary>
    int TargetObjectId() => SelfObjectId();

    /// <summary>dude_obj (opGetDude).</summary>
    int DudeObjectId() => 0;

    /// <summary>obj_being_used_with (opGetObjectBeingUsedWith).</summary>
    int ObjectBeingUsedWithId() => 0;

    /// <summary>get_critter_stat (opGetCritterStat → stat.cc critterGetStat):
    /// effective stat (base+bonus; 35=current HP, 36=poison, 37=rad); -1 unknown.</summary>
    int GetCritterStat(int objectHandle, int stat) => 0;

    /// <summary>has_skill (opHasSkill 0x80AA, interpreter_extra.cc:560 → skill.cc skillGetValue):
    /// despite the name returns the critter's EFFECTIVE skill VALUE (not a bool); 0 for null/non-critter.
    /// The only script path to a skill — get_critter_stat covers stats 0-34 only (P74-M3).</summary>
    int HasSkill(int objectHandle, int skill) => 0;

    /// <summary>set_critter_stat (opSetCritterStat): despite the name it
    /// ADJUSTS the dude's base stat by amount; 0 ok, -1 non-dude.</summary>
    int AdjustCritterBaseStat(int objectHandle, int stat, int amount) => -1;

    /// <summary>has_trait (opHasTrait): type 0=perk rank, 1=object trait
    /// (5=aiPacket, 6=team, 10=rotation, 666=visible, 669=inv weight),
    /// 2=selected character trait.</summary>
    int HasTrait(int type, int objectHandle, int param) => 0;

    /// <summary>do_check (opDoCheck → stat.cc statRoll): d10 vs SPECIAL+mod →
    /// ROLL_SUCCESS(2)/ROLL_FAILURE(1).</summary>
    int DoCheck(int objectHandle, int stat, int modifier) => 2;

    /// <summary>get_pc_stat (opGetPcStat): 0=unspent skill pts, 1=level,
    /// 2=experience, 3=reputation, 4=karma.</summary>
    int GetPcStat(int stat) => 0;

    /// <summary>metarule3 rule 103 GET_KILL_COUNT (killsGetByType): the dude's kill tally for a
    /// KILL_TYPE (P38). 0 by default → inert.</summary>
    int GetKillCount(int killType) => 0;

    /// <summary>metarule3 rule 110 (interpreter_extra.cc:2052 wmCarIsOutOfGas): the Highwayman car is empty.
    /// Default false so hosts without a car model are unaffected (P100 Point 4).</summary>
    bool CarIsOutOfGas() => false;

    /// <summary>metarule3 rule 105 METARULE3_WM_SUBTILE_STATE (interpreter_extra.cc:1995
    /// wmSubTileGetVisitedState): the worldmap subtile fog state (0 unknown / 1 known / 2 visited)
    /// at a world-pixel (x, y). 0 by default → inert (quest 529's scouting gate never passes).</summary>
    int GetSubtileState(int worldX, int worldY) => 0;

    /// <summary>critter_add_trait (opCritterAddTrait): kind 1 sets object
    /// traits (5=aiPacket, 6=team); perks (kind 0) are out of PoC scope.</summary>
    void CritterAddTrait(int objectHandle, int kind, int param, int value) { }

    /// <summary>attack/attack_complex (opAttackComplex): self attacks the
    /// target — starts combat outside it, retargets inside it.</summary>
    void AttackComplex(int targetHandle) { }

    /// <summary>anim_busy (opAnimBusy): is the object mid-animation?</summary>
    bool AnimBusy(int objectHandle) => false;

    /// <summary>give_exp_points (opGiveExpPoints → pcAddExperience).</summary>
    void GiveExpPoints(int amount) { }

    /// <summary>override_map_start (opOverrideMapStart, interpreter_extra.cc:522):
    /// tile = 200·y + x; repositions the dude during map_enter.</summary>
    void OverrideMapStart(int x, int y, int elevation, int rotation) { }

    /// <summary>fixed_param (opGetFixedParam) — map_enter: first-run flag; timed: timer param.</summary>
    int FixedParam() => 0;

    /// <summary>action_being_used (opGetActionBeingUsed) — skill id during use_skill_on (lockpick = 9).</summary>
    int ActionBeingUsed() => -1;

    /// <summary>script_action (opGetScriptAction) — the proc id being executed.</summary>
    int ScriptAction() => 0;

    /// <summary>
    /// metarule (opMetarule). The host should answer rule 14 FIRST_RUN
    /// (pristine map → 1), 22 IS_LOADGAME (0) and 30 CAR_CURRENT_TOWN (0);
    /// the default matches those for pristine-map sessions.
    /// </summary>
    int Metarule(int rule, int argument) => rule == 14 ? 1 : 0;

    /// <summary>Game clock in ticks (10/second; engine boots at 302400). Drives game_time* and month.</summary>
    int GameTime() => 302400;

    // ---- dialog protocol (phase-4 M1). The VM resolves message ids to text
    // before calling; option procs arrive as procedure indices.

    /// <summary>gsay_start (_gdialogStart): clear the reply and option list.</summary>
    void DialogStart() { }

    /// <summary>gsay_reply / the reply part of gsay_message.</summary>
    void DialogReply(string text) { }

    /// <summary>P53: a dialogue REPLY resolved a message-list entry — the host may look up the entry's
    /// audio field and play <c>sound\speech\&lt;audio&gt;.acm</c> (scripts.cc _scr_get_msg_str_speech, a3==1).
    /// Fired only for reply opcodes (not options) and only for a message-list reference, never a literal
    /// string. Default no-op (headless / --no-audio). Inert on the slice — every slice line's audio is empty.</summary>
    void PlayDialogVoice(int messageListId, int messageId) { }

    /// <summary>gsay_option / giq_option (after the IQ filter): an option bound to a procedure index.</summary>
    void DialogOption(string text, int procedureIndex, int reaction) { }

    /// <summary>gsay_end (_gdialogGo): the collected reply+options are ready to present.</summary>
    void DialogEnd() { }

    /// <summary>start_gdialog — headId is -1 for head-less NPCs. <paramref name="reaction"/> is
    /// the initial fidget family the script supplies (the head anim value: 1 good / 4 neutral /
    /// 7 bad — _gdialogInitFromScript feeds it straight to _gdSetupFidget). (P122.)</summary>
    void DialogSessionStart(int headId, int backgroundId, int reaction = 4) { }

    /// <summary>dialogue_reaction (0x80E0, interpreter_extra.cc:1958 _talk_to_critter_reacts):
    /// the script nudges the talking head's mood — value −1 good / 0 neutral / +1 bad
    /// (a1 + 50 → GAME_DIALOG_REACTION_*). (P122.)</summary>
    void DialogReaction(int value) { }

    /// <summary>end_dialogue.</summary>
    void DialogSessionEnd() { }

    /// <summary>Player IQ (+ Smooth Talker) for the giq_option filter.</summary>
    int DialogIntelligence() => 5;

    /// <summary>using_skill(object, skill): true only for the dude's SNEAK flag (P29 A-M0).</summary>
    bool IsUsingSkill(int objectHandle, int skill) => false;

    /// <summary>critter_attempt_placement (interpreter_extra.cc:2812): relocate a critter to a tile
    /// (or a free tile near it) on an elevation. Returns true on success.</summary>
    bool CritterAttemptPlacement(int critterHandle, int tile, int elevation) => false;

    /// <summary>is_in_combat (interpreter_extra.cc opCombatIsInitialized 0x8128): true while the
    /// combat state machine runs. Default false (tests / no combat).</summary>
    bool IsInCombat() => false;

    /// <summary>critter_state (interpreter_extra.cc opGetCritterState 0x80FB): the CRITTER_STATE
    /// bitfield — DEAD(1) for null/dead, else NORMAL(0)+PRONE(2)+DAM_CRIP bits, or PRONE(2) for an
    /// unconscious-but-alive critter. Default DEAD(1) matches the engine's init value.</summary>
    int CritterState(int objectHandle) => 1;

    /// <summary>poison (interpreter_extra.cc opPoison 0x8122 → critterAdjustPoison): adjust the
    /// object's poison counter by <paramref name="amount"/> (dude-only in the engine, poison-resistance
    /// reduced). Used by the scorpion's on-hit combat_p_proc (fp=2). Default no-op.</summary>
    void Poison(int objectHandle, int amount) { }

    /// <summary>terminate_combat (interpreter_extra.cc opTerminateCombat 0x8153): end the current combat
    /// (the engine's _game_user_wants_to_quit=1) and mark the script's self DISENGAGING. Used by e.g. the
    /// temple challenger's combat_p_proc (fp=4) to yield at ≤half HP. Default no-op.</summary>
    void TerminateCombat() { }

    /// <summary>float_msg — floating head-text over an object.</summary>
    void FloatMessage(int objectHandle, string text, int type) { }

    /// <summary>gdialog_barter (gameDialogBarter, game_dialog.cc:3163): sets
    /// the trade flag; its argument OVERWRITES gdialog_set_barter_mod (:3169).</summary>
    void Barter(int modifier) { }

    /// <summary>gdialog_set_barter_mod.</summary>
    void GdialogSetBarterMod(int modifier) { }

    /// <summary>move_obj_inven_to_obj (opMoveObjectInventoryToObject): move the
    /// whole inventory from source to target.</summary>
    void MoveAllInventory(int sourceHandle, int targetHandle) { }

    /// <summary>play_gmovie (opPlayGameMovie): the host shows a caption card.</summary>
    void PlayMovie(int movieId) { }

    /// <summary>endgame_slideshow (opEndgameSlideshow): the host runs the victory-ending slideshow
    /// (endgame.txt, gvar==value slides) then the endgame "movie" (credits) — the win condition.</summary>
    void EndgameSlideshow() { }

    /// <summary>endgame_movie (opEndgameMovie): the host runs the endgame "movie" (credits scroll) directly.</summary>
    void EndgameMovie() { }

    /// <summary>game_ui_disable / game_ui_enable (opGameUiDisable/Enable): lock/unlock the player interface
    /// for a scripted cutscene (e.g. a New Reno prizefight round). Default no-op (headless has no live UI).</summary>
    void GameUiEnabled(bool enabled) { }

    /// <summary>critter_damage (opCritterDamage → actionDamage): flag 0x100 =
    /// bypass armor, 0x200 = no animation; low bits = damage type.</summary>
    void CritterDamage(int objectHandle, int amount, int damageTypeWithFlags) { }

    /// <summary>party_add / party_remove (opPartyAdd/opPartyRemove).</summary>
    void PartyAdd(int objectHandle) { }

    void PartyRemove(int objectHandle) { }

    /// <summary>party_member_obj (opGetPartyMember): handle of the party
    /// member with this pid, or 0.</summary>
    int PartyMemberByPid(int pid) => 0;

    // ---- door/container state (phase-4 M2); handle 0 must no-op like the
    // engine's scriptPredefinedError paths.

    /// <summary>obj_is_locked (objectIsLocked).</summary>
    bool ObjIsLocked(int objectHandle) => false;

    /// <summary>obj_lock / obj_unlock / jam_lock (jam treated as lock).</summary>
    void ObjSetLocked(int objectHandle, bool locked) { }

    /// <summary>obj_is_open (door frame != 0).</summary>
    bool ObjIsOpen(int objectHandle) => false;

    /// <summary>obj_open / obj_close — the host animates and re-blocks.</summary>
    void ObjSetOpen(int objectHandle, bool open) { }

    // ---- world mutation (phase-4 M3); handle 0 must no-op.

    /// <summary>create_object_sid; scriptIndex (scripts.lst, -1 none) binds a
    /// fresh script to the object. Returns the new handle or 0.</summary>
    int CreateObject(int pid, int tile, int elevation, int scriptIndex = -1) => 0;

    /// <summary>destroy_object.</summary>
    void DestroyObject(int objectHandle) { }

    /// <summary>add_obj_to_inven / add_mult_objs_to_inven.</summary>
    void AddToInventory(int targetHandle, int itemHandle, int quantity) { }

    /// <summary>rm_obj_from_inven / rm_mult_objs_from_inven; returns the removed count.</summary>
    int RemoveFromInventory(int targetHandle, int itemHandle, int quantity) => 0;

    /// <summary>move_to; returns the object's new tile (engine pushes a result).</summary>
    int MoveTo(int objectHandle, int tile, int elevation) => -1;

    /// <summary>set_obj_visibility (true = hidden).</summary>
    void SetObjectVisibility(int objectHandle, bool hidden) { }

    /// <summary>obj_pid.</summary>
    int ObjPid(int objectHandle) => -1;

    /// <summary>elevation (0x80EC, opGetObjectElevation): the object's map elevation (0..2).</summary>
    int ObjElevation(int objectHandle) => 0;

    /// <summary>critter_injure (0x8127, opCritterInjure): OR (or clear, on DAM_PERFORM_REVERSE) the
    /// DAM_CRIP crippled-limb/blind flags into the critter's combat results.</summary>
    void CritterInjure(int objectHandle, int damageFlags) { }

    // ---- P0 (campaign port) critter-state EFFECT externals: each bridges to a system Hexwaste
    // already tracks (HP / poison / the death path). Inert by default so the fake test host stays silent.

    /// <summary>critter_heal (0x80E8, opCritterHeal → critterAdjustHitPoints): adjust the critter's HP
    /// by <paramref name="amount"/>, clamped to STAT_MAXIMUM_HIT_POINTS; a drop to ≤0 kills it. Returns
    /// the engine's rc (always 0). Default no-op returning 0.</summary>
    int CritterHeal(int objectHandle, int amount) => 0;

    /// <summary>get_poison (0x8123, opGetPoison → critterGetPoison): the critter's poison counter
    /// (0 for null / non-critter, matching the engine's default). Default 0.</summary>
    int GetPoison(int objectHandle) => 0;

    /// <summary>kill_critter (0x80ED, opKillCritter → critterKill): destroy a specific critter with the
    /// given <paramref name="deathFrame"/> animation. Default no-op.</summary>
    void KillCritter(int objectHandle, int deathFrame) { }

    /// <summary>critter_rm_trait (0x8103, opCritterRemoveTrait): the engine handles ONLY
    /// CRITTER_TRAIT_PERK (kind 0) — it loops perkRemove until the rank hits 0; every other kind is a
    /// no-op error. Always returns -1. Default no-op returning -1.</summary>
    int CritterRemoveTrait(int objectHandle, int kind, int param, int value) => -1;

    /// <summary>use_obj_on_obj (0x8145, opUseObjectOnObject): run the use_obj_on_p_proc chain — the
    /// item's then the target's — for scripted "use item on object" steps. Default no-op.</summary>
    void UseObjectOnObject(int targetHandle, int itemHandle) { }

    /// <summary>critter_mod_skill (0x813C, opCritterModifySkill): add <paramref name="points"/> skill
    /// points to the dude's skill (skillAddForce/skillSubForce, halved for a tagged skill, capped at a
    /// value of 300). Dude-only in the engine. Always returns 0. Default no-op returning 0.</summary>
    int CritterModSkill(int objectHandle, int skill, int points) => 0;

    /// <summary>scripts_request_world_map (0x8108, opWorldmap → scriptsRequestWorldMap): leave the
    /// current map out to the worldmap (deferred until the script returns). Default no-op.</summary>
    void RequestWorldMap() { }

    /// <summary>wm_area_set_pos (0x80E5, opWorldmapCitySetPos → wmAreaSetWorldPos): move worldmap area
    /// <paramref name="city"/>'s marker to (<paramref name="x"/>, <paramref name="y"/>) on the world
    /// canvas. Default no-op.</summary>
    void WmAreaSetPos(int city, int x, int y) { }

    /// <summary>dialogue_system_enter (0x80F9, opGameDialogSystemEnter): request entering dialog with the
    /// script's self — the mechanism a scenery use_p_proc (terminal/well/computer) uses to open its own
    /// talk_p_proc. Suppressed in combat. Default no-op.</summary>
    void DialogueSystemEnter() { }

    /// <summary>load_map (0x80E4, opLoadMap) by map index: set GVAR_LOAD_MAP_INDEX = <paramref
    /// name="param"/> and defer a transition to that map's default start. A negative index is a no-op
    /// (engine). Default no-op.</summary>
    void LoadMap(int mapIndex, int param) { }

    /// <summary>load_map (0x80E4) by map FILE name (wmMapMatchNameToIdx): resolve the name to an index,
    /// set GVAR_LOAD_MAP_INDEX, and defer the transition. Default no-op.</summary>
    void LoadMapByName(string mapName, int param) { }

    /// <summary>attack_setup (0x8143, opAttackSetup): a script forces combat between two critters — the
    /// attacker becomes the aggressor against the defender (e.g. the New Reno kung-fu duel makes a master
    /// attack the dude). A dead/inactive/invisible attacker or defender, or a fleeing defender, aborts it.
    /// Default no-op.</summary>
    void AttackSetup(int attackerHandle, int defenderHandle) { }

    /// <summary>explosion (0x811A, opExplosion): a script detonates a blast centred on <paramref name="tile"/>
    /// at <paramref name="elevation"/> dealing up to <paramref name="maxDamage"/> explosion damage. A tile of
    /// -1 is a no-op (engine). Default no-op.</summary>
    void Explosion(int tile, int elevation, int maxDamage) { }

    /// <summary>anim (0x810C, opAnim): play animation code <paramref name="anim"/> on the object once.</summary>
    void Anim(int objectHandle, int anim, int frame) { }

    /// <summary>critter_inven_obj (0x8106, opCritterGetInventoryObject): the handle of the critter's worn
    /// (0) / right-hand (1) / left-hand (2) item, or the inventory item count (3).</summary>
    int CritterInventoryObject(int objectHandle, int type) => 0;

    /// <summary>set_map_start (0x80A8, opSetMapStart): set the map's start tile (200*y+x) / elevation /
    /// rotation and re-centre — repositions the dude on the current map.</summary>
    void SetMapStart(int x, int y, int elevation, int rotation) { }

    /// <summary>kill_critter_type (0x80EE, opKillCritterType): destroy every live critter of proto
    /// <paramref name="pid"/> (deathFrame 0 = silent remove; else a corpse anim).</summary>
    void KillCritterType(int pid, int deathFrame) { }

    /// <summary>set_exit_grids (0x80E6, opSetExitGrids): rewrite every exit-grid object on the source
    /// <paramref name="elevation"/> to point at destMap/destTile/destElevation (rotation is discarded
    /// by the engine, interpreter_extra.cc:2182).</summary>
    void SetExitGrids(int elevation, int destMap, int destElevation, int destTile) { }

    /// <summary>wield_obj_critter (0x80DA, opWieldItem): the critter equips/wields the item
    /// (weapon -> right hand; armor -> worn).</summary>
    void WieldObjCritter(int critterHandle, int itemHandle) { }

    /// <summary>obj_art_fid (0x8149, opGetObjectFid): the object's art FID (0 if null).</summary>
    int ObjArtFid(int objectHandle) => 0;

    /// <summary>critter_is_fleeing (0x8151, opCritterIsFleeing): the critter has the FLEEING maneuver bit.</summary>
    bool CritterIsFleeing(int objectHandle) => false;

    /// <summary>critter_set_flee_state (0x8152, opCritterSetFleeState): set/clear the critter's FLEEING bit.</summary>
    void CritterSetFleeState(int objectHandle, int fleeing) { }

    /// <summary>mark_area_known (0x80B2, opMarkAreaKnown): set a worldmap area's known/visited state.</summary>
    void MarkAreaKnown(int markType, int areaId, int mode) { }

    /// <summary>game_time_advance (0x80FC, opGameTimeAdvance): advance the game clock by <paramref name="ticks"/>.</summary>
    void GameTimeAdvance(int ticks) { }

    /// <summary>tile_contains_obj_pid (0x80BB, opTileContainsObjectWithPid): 1 if any object at
    /// (<paramref name="tile"/>, <paramref name="elevation"/>) has proto <paramref name="pid"/>.</summary>
    bool TileContainsObjPid(int tile, int elevation, int pid) => false;

    /// <summary>P101 (Tier B): item_subtype (0x80C9, opGetItemType) — the ITEM_TYPE of an item object
    /// (ARMOR=0..KEY=6), MISC(5) for the shiv, or -1 for null/non-item. Default -1.</summary>
    int ItemSubtype(int objectHandle) => -1;

    /// <summary>P101 (Tier B): proto_data (0x8104, opGetProtoData) — a proto's data member (int). Default 0.</summary>
    int ProtoData(int pid, int member) => 0;

    /// <summary>P113 (item 7b): proto_data STRING members — NAME (1) / DESCRIPTION (2) return a string
    /// in the engine (opGetProtoData VALUE_TYPE_STRING branch, interpreter_extra.cc:2961;
    /// protoGetDataMember, proto.cc). Null → the caller pushes the int path instead.</summary>
    string? ProtoDataString(int pid, int member) => null;

    /// <summary>P101 (Tier B): tile_is_visible (0x80F8) — crude on-screen proximity to the camera-centre tile.</summary>
    int TileIsVisible(int tile) => 0;

    /// <summary>P101 (Tier C): inven_cmds cmd==13 — the handle of the object's inventory item at index, or 0.</summary>
    int InvenPtr(int objectHandle, int index) => 0;

    /// <summary>P101 (Tier C): inven_unwield — the SELF critter holsters its wielded weapon.</summary>
    void InvenUnwield() { }

    /// <summary>P101 (Tier C): use_obj (0x80DB, opUseObject) — run the object's use_p_proc (single-object use).</summary>
    void UseObj(int objectHandle) { }

    /// <summary>P101 (Tier C): drop_obj (0x80D7, opDrop) — the SELF drops an item from its inventory to the ground.</summary>
    void DropObj(int objectHandle) { }

    /// <summary>P101 (Tier C): scr_return — store the running script's return value (consumed by the
    /// use_obj_on fallthrough gate in the engine). Default no-op.</summary>
    void ScrReturn(int value) { }

    /// <summary>P101 (Tier D): radiation_inc(+)/radiation_dec(-) — adjust the dude's radiation counter.</summary>
    void Radiation(int objectHandle, int amount) { }

    /// <summary>P101: gfade_out(false)/gfade_in(true) — screen fade to/from black for a cutscene.</summary>
    void ScreenFade(bool fadeIn) { }

    /// <summary>P101: play_sfx / reg_anim_play_sfx — play a named sound effect (sound\sfx\&lt;name&gt;.acm).</summary>
    void PlaySfx(string name) { }

    /// <summary>P101: animate_stand_obj (0x80CC, opAnimateStand) — the object plays its idle STAND anim.</summary>
    void AnimateStand(int objectHandle) { }

    /// <summary>animate_stand_reverse_obj (0x80CD, opAnimateStandReverse): the object plays its
    /// stand animation (reversed in the engine; not in combat).</summary>
    void AnimateStandReverse(int objectHandle) { }

    /// <summary>obj_is_carrying_obj_pid (interpreter_extra.cc:1040): the quantity of
    /// <paramref name="pid"/> the critter carries (recursive into nested containers).</summary>
    int ObjIsCarryingPid(int objectHandle, int pid) => 0;

    /// <summary>obj_carrying_pid_obj (interpreter_extra.cc:3438): the handle of the
    /// FIRST carried item with <paramref name="pid"/> (depth-first), or 0 if none.</summary>
    int ObjCarryingPidObj(int objectHandle, int pid) => 0;

    /// <summary>tile_contains_pid_obj — handle of a matching object, or 0.</summary>
    int TileContainsPidObj(int tile, int elevation, int pid) => 0;

    // ---- timers + geometry + caps (phase-5 M0)

    /// <summary>add_timer_event — delay is in game ticks (10/second, 1:1 real time).</summary>
    void AddTimerEvent(int objectHandle, int delayTicks, int param) { }

    /// <summary>rm_timer_event — removes all timers owned by the object.</summary>
    void RemoveTimerEvents(int objectHandle) { }

    /// <summary>metarule3 rule 100 — removes timers matching (object, param).</summary>
    void RemoveTimerEventsWithParam(int objectHandle, int param) { }

    /// <summary>tile_num — the object's hex tile, or -1.</summary>
    int ObjTile(int objectHandle) => -1;

    /// <summary>cur_map_index.</summary>
    int CurrentMapIndex() => 0;

    /// <summary>item_caps_total — caps (pid 41) in the object's inventory.</summary>
    int CapsTotal(int objectHandle) => 0;

    /// <summary>item_caps_adjust — mutates the caps stack; 0 on success, -1 when insufficient.</summary>
    int CapsAdjust(int objectHandle, int amount) => -1;

    /// <summary>obj_can_see_obj (0x80DC) — elevation + isWithinPerception + a clear sight path
    /// (interpreter_extra.cc:1783). Default false.</summary>
    bool ObjCanSee(int objectHandle, int targetHandle) => false;
    /// <summary>obj_can_hear_obj (0x80F5) — elevation + isWithinPerception, NO line-of-sight (the CE
    /// sfall fix, interpreter_extra.cc:2620). Default routes to ObjCanSee for back-compat.</summary>
    bool ObjCanHear(int objectHandle, int targetHandle) => ObjCanSee(objectHandle, targetHandle);

    /// <summary>animate_move_obj_to_tile — the host may start a walk animation.</summary>
    void AnimateMoveToTile(int objectHandle, int tile, int speed) { }

    /// <summary>set_light_level — set the global ambient light (0-100%, 50 = cavern).</summary>
    void SetLightLevel(int level) { }

    /// <summary>obj_set_light_level — set a per-object light pool (intensity 0-100%, radius).</summary>
    void SetObjectLightLevel(int objectHandle, int intensity, int distance) { }

    /// <summary>reg_anim_animate_forever — loop animation code <paramref name="anim"/> on the
    /// object forever (the host's animator).</summary>
    void RegAnimAnimateForever(int objectHandle, int anim) { }

    /// <summary>reg_anim_func BEGIN (interpreter_extra.cc:3462 reg_anim_begin): open an
    /// animation batch; subsequent reg_anim register ops accumulate into it.</summary>
    void RegAnimBegin() { }

    /// <summary>reg_anim_func END (interpreter_extra.cc:3469 reg_anim_end): flush the batch
    /// to the host (it plays the queued moves/animations).</summary>
    void RegAnimEnd() { }

    /// <summary>reg_anim_func CLEAR (interpreter_extra.cc:3466 reg_anim_clear): cancel the
    /// object's running/queued animations.</summary>
    void RegAnimClear(int objectHandle) { }

    /// <summary>reg_anim_obj_move_to_tile / reg_anim_obj_run_to_tile (interpreter_extra.cc:
    /// 3547/3564): queue a walk (<paramref name="run"/> = run variant) to a hex tile.</summary>
    void RegAnimMoveToTile(int objectHandle, int tile, int delay, bool run) { }

    /// <summary>reg_anim_obj_move_to_obj / reg_anim_obj_run_to_obj (interpreter_extra.cc:
    /// 3513/3530): queue a walk to another object's tile.</summary>
    void RegAnimMoveToObject(int objectHandle, int destHandle, int delay, bool run) { }

    /// <summary>reg_anim_animate / reg_anim_animate_reverse (interpreter_extra.cc:3477/3496):
    /// queue an animation by code (<paramref name="reverse"/> plays it backwards).</summary>
    void RegAnimAnimate(int objectHandle, int anim, int delay, bool reverse) { }
}

/// <summary>
/// Micro interpreter for compiled Fallout 2 scripts, ported from fallout2-ce
/// src/interpreter.cc. It is a two-stack machine: a data stack holding tagged
/// values (program globals live at its bottom, below <c>basePointer</c>;
/// procedure locals are addressed off <c>framePointer</c>) and a return stack
/// for saved instruction pointers and frame pointers. Execution starts at
/// file offset 0: the 42-byte header stub jumps to the global-init prologue
/// and ends in exit_program (runScript/_interpret). Procedures are invoked
/// the way _executeProcedure() does: _setupCall() pushes the current IP and
/// the magic return address 24 on the return stack, then flags, a
/// checkWaitFunc placeholder, the window id and a 0 return-value slot on the
/// data stack; the compiled epilogue jumps to 24 where the header stub pops
/// the return value and runs pop_flags_exit, unwinding everything and
/// breaking out of the interpreter loop.
///
/// Scope per the phase-3 report (M5): the 39 core opcodes measured across six
/// real scripts plus the handful the call convention itself needs. Floats
/// never occur and are not supported. Externals not in <see cref="IVmExternals"/>
/// are arity-stubbed via <see cref="ExternalArity"/> so the stack never
/// desyncs; stubbed calls are reported through an optional callback.
/// </summary>
/// <summary>
/// Cross-script external variables (the engine's export.cc): one store per
/// host session, shared by every VM it spawns. Values are ints (numbers and
/// object handles); the rare string store is logged and dropped.
/// </summary>
public sealed class ExternalVariables
{
    internal Dictionary<string, int> Ints { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => Ints.Clear();
    public int Count => Ints.Count;

    /// <summary>True once a script has exported (declared) this variable into the shared store —
    /// i.e. a fetch_external of it resolves to a real value instead of the undefined-&gt;0 fallback.</summary>
    public bool IsDefined(string name) => Ints.ContainsKey(name);
}

public sealed class IntVm
{
    // Value tags, ported from interpreter.h VALUE_TYPE_*.
    private const ushort TypeInt = 0xC001;
    private const ushort TypeFloat = 0xA001;
    private const ushort TypeStaticString = 0x9001;
    private const ushort TypeDynamicString = 0x9801;

    // Program flags, ported from interpreter.h ProgramFlags (0x40 is the
    // "returned from an interrupt-context procedure" break flag set by
    // pop_flags_exit).
    private const int FlagExited = 0x01;
    private const int FlagStopped = 0x08;
    private const int FlagCriticalSection = 0x80;
    private const int FlagProcReturned = 0x40;

    // _interpret() break mask: EXITED | 0x04 | STOPPED | 0x20 | 0x40 | 0x0100.
    private const int BreakMask = 0x016D;

    /// <summary>Hard safety budget; real procs run a few thousand ops at most.</summary>
    // fo2ce has NO instruction cap. Some shipped scripts busy-wait synchronously — e.g. the arcaves spike
    // trap flies its missile one hex per 500 loop iterations (ATSrTrp*.Missile_Fired), which at 100k aborted
    // mid-flight and left game_ui_disable stranded (the player stuck). Raised well past any real script loop
    // while still catching a true runaway (5M instructions run in well under a second).
    private const int InstructionBudget = 5_000_000;

    private readonly struct Value(ushort tag, int raw, bool isObjectHandle = false)
    {
        public ushort Tag { get; } = tag;
        public int Raw { get; } = raw;

        /// <summary>P126: provenance — this int came from an object-returning external
        /// (self_obj, create_object_sid, …). Tag stays TypeInt so every consumer treats it
        /// as a plain int; the flag only feeds the stale-handle diagnostic at the
        /// persistent-var setters (handles are per-map-load and never serialized, so a
        /// handle stored in a GVAR/MVAR/LVAR resolves to a DIFFERENT object later).</summary>
        public bool IsObjectHandle { get; } = isObjectHandle;

        public bool IsString => Tag is TypeStaticString or TypeDynamicString;

        public static Value Int(int value) => new(TypeInt, value);

        public static Value ObjectHandle(int handle) => new(TypeInt, handle, isObjectHandle: true);
    }

    private readonly IntProgram _program;
    private IVmExternals _externals; // swapped per proc run by Rebind() on a cached VM
    private readonly Action<string>? _onStubbedExternal;

    /// <summary>The immutable bytecode this VM runs — the cache invariant check
    /// (one program per (map, sid) for the whole visit).</summary>
    public IntProgram Program => _program;

    /// <summary>True while Interpret() is on the stack — a same-sid re-entrant proc
    /// call must get a throwaway VM instead of aliasing the running one.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Swaps the per-proc script context (self/source/target/fixedParam) without
    /// rebuilding the VM — the cached-VM analog of fallout2-ce scriptSetObjects/
    /// scriptSetFixedParam (scripts.cc:624-658), which mutate the Script in place
    /// while the Program (and its module globals) persists for the map visit.
    /// </summary>
    public void Rebind(IVmExternals externals) => _externals = externals;

    // ported from fallout2-ce src/random.h ROLL_* enum
    private const int RollCriticalFailure = 0;
    private const int RollSuccess = 2;
    private const int RollCriticalSuccess = 3;

    /// <summary>Deterministic RNG (scripts only use it for stock quantities and flavor).
    /// Reseeded at each named-proc entry (TryRunProcedure) so a cached VM reproduces the
    /// per-proc fresh-VM byte stream exactly; dialog node procs (TryRunProcedureByIndex)
    /// keep advancing it within one conversation, as before.</summary>
    private Random _random = new(RandomSeed);

    private const int RandomSeed = 20260612;

    /// <summary>Dialog text: literal string, or a message-list lookup for int ids.</summary>
    private string ResolveDialogText(int messageListId, Value msg) =>
        msg.Tag == TypeInt ? _externals.GetMessage(messageListId, msg.Raw) : AsString(msg);

    /// <summary>Calendar month (1..12) for a day count since June 24, 2241 (non-leap years).</summary>
    private static int MonthFromEpochDay(int day)
    {
        ReadOnlySpan<int> daysPerMonth = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        int month = 5; // June (0-based)
        int dayOfMonth = 23 + day; // June 24 is day 0
        while (dayOfMonth >= daysPerMonth[month])
        {
            dayOfMonth -= daysPerMonth[month];
            month = (month + 1) % 12;
        }
        return month + 1;
    }

    /// <summary>Calendar day-of-month (1..31) for a day count since June 24, 2241 — the day
    /// component of the same date math as <see cref="MonthFromEpochDay"/> (opGetDay).</summary>
    private static int DayFromEpochDay(int day)
    {
        ReadOnlySpan<int> daysPerMonth = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        int month = 5;
        int dayOfMonth = 23 + day; // June 24 is day 0 (the 24th)
        while (dayOfMonth >= daysPerMonth[month])
        {
            dayOfMonth -= daysPerMonth[month];
            month = (month + 1) % 12;
        }
        return dayOfMonth + 1;
    }

    // Data stack (stackValues) and return stack (returnStackValues). Plain
    // lists because store/fetch/fetch_global index into the data stack.
    private readonly List<Value> _stack = [];
    private readonly List<Value> _returnStack = [];

    // Dynamic string heap: programPushString()'s block allocator reduced to a
    // list; a 0x9801-tagged Raw is an index into it.
    private readonly List<string> _dynamicStrings = [];

    // export_variable / fetch_external / store_external backing store. The
    // engine shares these across programs (export.cc); a per-VM dictionary is
    // enough for single-script runs, with absent imports defaulting to 0.
    private readonly Dictionary<string, Value> _externalVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly ExternalVariables? _sharedExternalVariables;

    private int _instructionPointer;
    private int _framePointer = -1;
    private int _basePointer = -1;
    private int _flags;
    private int _windowId;
    private bool _initialized;

    public IntVm(IntProgram program, IVmExternals externals, Action<string>? onStubbedExternal = null,
        ExternalVariables? externalVariables = null)
    {
        _program = program;
        _externals = externals;
        _onStubbedExternal = onStubbedExternal;
        _sharedExternalVariables = externalVariables;
    }

    /// <summary>
    /// Data stack depth — after a balanced procedure run this is back to the
    /// global count established by the init prologue (diagnostic).
    /// </summary>
    public int DataStackDepth => _stack.Count;

    /// <summary>Return stack depth — 0 between procedure runs (diagnostic).</summary>
    public int ReturnStackDepth => _returnStack.Count;

    /// <summary>Runs a procedure by name (e.g. "description_p_proc"); false when absent.</summary>
    public bool TryRunProcedure(string name)
    {
        int index = _program.FindProcedure(name);
        if (index < 0)
            return false;

        // Every named-proc entry restarts the script RNG at the fixed seed — exactly what
        // the old one-VM-per-proc lifetime gave for free. Keeps all goldens byte-identical
        // now that the VM persists per (map, sid). Dialog nodes go through
        // TryRunProcedureByIndex directly and continue the stream within a conversation.
        _random = new Random(RandomSeed);
        return TryRunProcedureByIndex(index);
    }

    /// <summary>
    /// Runs a procedure by its table index — how the dialog system binds
    /// options (game_dialog.cc _gdProcessChoice → _executeProcedure(proc)).
    /// </summary>
    public bool TryRunProcedureByIndex(int index)
    {
        if (index < 0 || index >= _program.Procedures.Count)
            return false;

        IntProcedure procedure = _program.Procedures[index];
        if (procedure.IsImported || procedure.BodyOffset <= 0)
            return false;

        EnsureInitialized();

        // ported from _executeProcedure(): _setupCall(program, address, 24)
        // followed by _interpret(program, -1).
        SetupCall(procedure.BodyOffset, returnAddress: 24);
        if (procedure.IsCritical)
            _flags |= FlagCriticalSection;
        Interpret();
        _flags &= ~FlagProcReturned;
        return true;
    }

    /// <summary>
    /// Forces the global-init prologue to run now, even when no procedure is executed.
    /// The prologue is where a script declares and assigns its exported variables
    /// (export_variable/store_external) into the shared store — combat-only scripts
    /// (only critter_p_proc/combat_p_proc) would otherwise never export at map enter,
    /// since TryRunProcedure skips EnsureInitialized when the named proc is absent.
    /// Idempotent: a no-op once the VM has initialized.
    /// </summary>
    public void RunGlobalInit() => EnsureInitialized();

    /// <summary>
    /// Runs the global-init prologue, ported from runScript(): a fresh
    /// program is interpreted from offset 0; the header stub jumps to the
    /// prologue, which pushes the program globals, runs set_global and
    /// returns to the stub's exit_program. The globals stay on the data
    /// stack below basePointer for all later procedure runs.
    /// </summary>
    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _instructionPointer = 0;
        _flags = 0;
        Interpret();
        _initialized = true;
    }

    /// <summary>ported from fallout2-ce src/interpreter.cc _setupCall()/_setupCallWithReturnVal().</summary>
    private void SetupCall(int address, int returnAddress)
    {
        _returnStack.Add(Value.Int(_instructionPointer));
        _returnStack.Add(Value.Int(returnAddress));
        _stack.Add(Value.Int(_flags & 0xFFFF));
        _stack.Add(Value.Int(0)); // checkWaitFunc placeholder
        _stack.Add(Value.Int(_windowId));
        _flags &= ~0xFFFF;
        _instructionPointer = address;
        _stack.Add(Value.Int(0)); // return value slot
    }

    /// <summary>The _interpret() dispatch loop, with a hard instruction budget.</summary>
    private void Interpret()
    {
        // Same-sid re-entrancy sentinel for the cached-VM cache (ScriptHost.GetOrCreateVm):
        // a nested proc call on this sid while we're mid-Interpret gets a throwaway VM.
        IsRunning = true;
        try
        {
            int budget = InstructionBudget;
            while ((_flags & BreakMask) == 0)
            {
                if (--budget < 0)
                    throw new InvalidDataException(
                        $"Script exceeded the {InstructionBudget} instruction budget (runaway loop?).");

                ushort opcode = (ushort)_program.ReadCode16(_instructionPointer);
                _instructionPointer += 2;

                if (((opcode >> 8) & 0x80) == 0)
                    throw new InvalidDataException($"Bad opcode word 0x{opcode:X4} at 0x{_instructionPointer - 2:X}.");

                Execute(opcode);
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void Execute(ushort opcode)
    {
        // Dispatch on the low 10 bits like _interpret(); the full word is
        // only meaningful for push, whose high bits carry the value type.
        switch (0x8000 | (opcode & 0x3FF))
        {
            case 0x8000: // noop
                break;
            case 0x8001: // push (the only opcode with an inline operand)
                ExecutePush(opcode);
                break;
            case 0x8002: // enter_critical_section
            case 0x804A: // start_critical
                _flags |= FlagCriticalSection;
                break;
            case 0x8003: // leave_critical_section
            case 0x804B: // end_critical
                _flags &= ~FlagCriticalSection;
                break;
            case 0x8004: // jump
                _instructionPointer = PopInt();
                break;
            case 0x8005: // call
                ExecuteCall();
                break;
            case 0x800C: // a_to_d
                Push(ReturnPop());
                break;
            case 0x800D: // d_to_a
                ReturnPush(Pop());
                break;
            case 0x8010: // exit_program
                _flags |= FlagExited;
                break;
            case 0x8011: // stop_program
                _flags |= FlagStopped;
                break;
            case 0x8012: // fetch_global
                Push(StackAt(_basePointer + PopInt()));
                break;
            case 0x8013: // store_global
            {
                int address = PopInt();
                StackSet(_basePointer + address, Pop());
                break;
            }
            case 0x8014: // fetch_external
                ExecuteFetchExternal();
                break;
            case 0x8015: // store_external
                ExecuteStoreExternal();
                break;
            case 0x8016: // export_variable
            {
                string identifier = _program.GetIdentifier(Pop().Raw);
                if (_sharedExternalVariables is { } shared)
                    shared.Ints.TryAdd(identifier, 0);
                else
                    _externalVariables.TryAdd(identifier, Value.Int(0));
                break;
            }
            case 0x8018: // swap
            {
                Value a = Pop();
                Value b = Pop();
                Push(a);
                Push(b);
                break;
            }
            case 0x8019: // swapa
            {
                Value a = ReturnPop();
                Value b = ReturnPop();
                ReturnPush(a);
                ReturnPush(b);
                break;
            }
            case 0x801A: // pop
                Pop();
                break;
            case 0x801B: // dup
            {
                Value value = Pop();
                Push(value);
                Push(value);
                break;
            }
            case 0x801C: // pop_return
                _instructionPointer = ReturnPopInt();
                break;
            case 0x801D: // pop_exit
                _instructionPointer = ReturnPopInt();
                _flags |= FlagProcReturned;
                break;
            case 0x801E: // pop_address
                ReturnPop();
                break;
            case 0x801F: // pop_flags
                ExecutePopFlags();
                break;
            case 0x8020: // pop_flags_return
                ExecutePopFlags();
                _instructionPointer = ReturnPopInt();
                break;
            case 0x8021: // pop_flags_exit
                ExecutePopFlags();
                _instructionPointer = ReturnPopInt();
                _flags |= FlagProcReturned;
                break;
            case 0x8025: // pop_flags_return_val_exit
            {
                Value value = Pop();
                ExecutePopFlags();
                _instructionPointer = ReturnPopInt();
                _flags |= FlagProcReturned;
                Push(value);
                break;
            }
            case 0x8027: // check_procedure_argument_count
            {
                int expected = PopInt();
                int procedureIndex = PopInt();
                if (ProcedureAt(procedureIndex).ArgumentCount != expected)
                    throw new InvalidDataException(
                        $"Wrong number of args to procedure {ProcedureAt(procedureIndex).Name}.");
                break;
            }
            case 0x8028: // lookup_procedure_by_name
            {
                int found = _program.FindProcedure(PopString());
                if (found < 1)
                    throw new InvalidDataException("lookup_procedure_by_name: procedure not found.");
                PushInt(found);
                break;
            }
            case 0x8029: // pop_base
                _framePointer = ReturnPopInt();
                break;
            case 0x802A: // pop_to_base
                if (_stack.Count < _framePointer)
                    throw new InvalidDataException("pop_to_base below the frame pointer (stack desync).");
                _stack.RemoveRange(_framePointer, _stack.Count - _framePointer);
                break;
            case 0x802B: // push_base
            {
                int argumentCount = PopInt();
                ReturnPush(Value.Int(_framePointer));
                _framePointer = _stack.Count - argumentCount;
                break;
            }
            case 0x802C: // set_global
                _basePointer = _stack.Count;
                break;
            case 0x802D: // fetch_procedure_address
                PushInt(ProcedureAt(PopInt()).BodyOffset);
                break;
            case 0x802E: // dump
            {
                int count = PopInt();
                for (int i = 0; i < count; i++)
                    Pop();
                break;
            }
            case 0x802F: // if
            {
                Value value = Pop();
                if (!IsEmpty(value))
                    Pop();
                else
                    _instructionPointer = PopInt();
                break;
            }
            case 0x8030: // while
                if (IsEmpty(Pop()))
                    _instructionPointer = PopInt();
                break;
            case 0x8031: // store
            {
                int address = PopInt();
                StackSet(_framePointer + address, Pop());
                break;
            }
            case 0x8032: // fetch
                Push(StackAt(_framePointer + PopInt()));
                break;
            case 0x8033: // equal
                ExecuteComparison(static c => c == 0);
                break;
            case 0x8034: // not_equal
                ExecuteComparison(static c => c != 0);
                break;
            case 0x8035: // less_than_equal
                ExecuteComparison(static c => c <= 0);
                break;
            case 0x8036: // greater_than_equal
                ExecuteComparison(static c => c >= 0);
                break;
            case 0x8037: // less_than
                ExecuteComparison(static c => c < 0);
                break;
            case 0x8038: // greater_than
                ExecuteComparison(static c => c > 0);
                break;
            case 0x8039: // add
                ExecuteAdd();
                break;
            case 0x803A: // sub
                ExecuteIntArithmetic(static (a, b) => unchecked(a - b));
                break;
            case 0x803B: // mul
                ExecuteIntArithmetic(static (a, b) => unchecked(a * b));
                break;
            case 0x803C: // div
                ExecuteIntArithmetic(static (a, b) =>
                    b != 0 ? a / b : throw new InvalidDataException("Division (DIV) by zero."));
                break;
            case 0x803D: // mod
                ExecuteIntArithmetic(static (a, b) =>
                    b != 0 ? a % b : throw new InvalidDataException("Division (MOD) by zero."));
                break;
            case 0x803E: // and
            {
                bool right = IsTruthy(Pop());
                bool left = IsTruthy(Pop());
                PushInt(left && right ? 1 : 0);
                break;
            }
            case 0x803F: // or
            {
                bool right = IsTruthy(Pop());
                bool left = IsTruthy(Pop());
                PushInt(left || right ? 1 : 0);
                break;
            }
            case 0x8040: // bitwise_and
                ExecuteIntArithmetic(static (a, b) => a & b);
                break;
            case 0x8041: // bitwise_or
                ExecuteIntArithmetic(static (a, b) => a | b);
                break;
            case 0x8042: // bitwise_xor
                ExecuteIntArithmetic(static (a, b) => a ^ b);
                break;
            case 0x8043: // bitwise_not
                PushInt(~PopInt());
                break;
            case 0x8044: // floor (no floats supported: ints pass through)
            {
                Value value = Pop();
                if (value.Tag != TypeInt)
                    throw new InvalidDataException("Invalid arg given to floor().");
                Push(value);
                break;
            }
            case 0x8045: // not (CE tests integerValue == 0 regardless of tag)
                PushInt(Pop().Raw == 0 ? 1 : 0);
                break;
            case 0x8046: // negate
            {
                Value value = Pop();
                if (value.Tag != TypeInt)
                    throw new InvalidDataException("Invalid arg given to NEG.");
                PushInt(-value.Raw);
                break;
            }
            case >= 0x8000 and <= 0x804B: // remaining core ops (timers, child programs, floats)
                throw new InvalidDataException(
                    $"Unsupported core opcode 0x{opcode:X4} at 0x{_instructionPointer - 2:X}.");
            default:
                ExecuteExternal(0x8000 | (opcode & 0x3FF));
                break;
        }
    }

    // ---------------------------------------------------------------- core ops

    /// <summary>ported from opPush(): the value type comes from the opcode word's high bits.</summary>
    private void ExecutePush(ushort opcode)
    {
        int operand = _program.ReadCode32(_instructionPointer);
        _instructionPointer += 4;

        if (opcode == TypeFloat)
            throw new NotSupportedException("Float push: floats never occur in game scripts and are not supported.");

        Push(new Value(opcode, operand));
    }

    /// <summary>ported from opCall(): jump to the procedure body; the compiled call site did the stack setup.</summary>
    private void ExecuteCall()
    {
        IntProcedure procedure = ProcedureAt(PopInt());
        if (procedure.IsImported)
            return; // faithful to fo2ce opCall (interpreter.cc:2044): the imported branch is a TODO no-op —
                    // execution simply falls through. Vanilla never emits a direct call to an imported proc
                    // (imports resolve via the export table), so this is unreachable in practice; a no-op is
                    // strictly more robust than throwing if some mod script ever did.

        _instructionPointer = procedure.BodyOffset;
        if (procedure.IsCritical)
            _flags |= FlagCriticalSection;
    }

    /// <summary>ported from opPopFlags(): windowId, checkWaitFunc, then flags off the data stack.</summary>
    private void ExecutePopFlags()
    {
        _windowId = PopInt();
        Pop(); // checkWaitFunc
        _flags = Pop().Raw & 0xFFFF;
    }

    private void ExecuteFetchExternal()
    {
        string identifier = _program.GetIdentifier(Pop().Raw);

        if (_sharedExternalVariables is { } shared)
        {
            if (!shared.Ints.TryGetValue(identifier, out int raw))
            {
                _onStubbedExternal?.Invoke($"fetch_external of undefined variable '{identifier}' -> 0");
                raw = 0;
            }
            Push(Value.Int(raw));
            return;
        }

        if (!_externalVariables.TryGetValue(identifier, out Value value))
        {
            // The engine fatals here; an import owned by another (unloaded)
            // script defaulting to 0 keeps single-program runs soft.
            _onStubbedExternal?.Invoke($"fetch_external of undefined variable '{identifier}' -> 0");
            value = Value.Int(0);
        }

        Push(value);
    }

    private void ExecuteStoreExternal()
    {
        string identifier = _program.GetIdentifier(Pop().Raw);
        Value value = Pop();

        if (_sharedExternalVariables is { } shared)
        {
            // Cross-program string offsets don't transfer; the shop scripts
            // only store ints and object handles.
            if (value.Tag == TypeInt)
                shared.Ints[identifier] = value.Raw;
            else
                _onStubbedExternal?.Invoke($"store_external of non-int '{identifier}' dropped");
            return;
        }

        _externalVariables[identifier] = value;
    }

    /// <summary>
    /// Comparison ops, ported from opConditionalOperator*(): mixed string/int
    /// operands compare as strings with the int formatted "%d"; value[1] is
    /// the left operand (popped second).
    /// </summary>
    private void ExecuteComparison(Func<int, bool> interpretation)
    {
        Value right = Pop();
        Value left = Pop();

        int comparison;
        if (left.IsString || right.IsString)
            comparison = string.CompareOrdinal(AsString(left), AsString(right));
        else
            comparison = left.Raw.CompareTo(right.Raw);

        PushInt(interpretation(comparison) ? 1 : 0);
    }

    /// <summary>ported from opAdd(): a string operand turns + into concatenation.</summary>
    private void ExecuteAdd()
    {
        Value right = Pop();
        Value left = Pop();

        if (left.IsString || right.IsString)
            PushString(AsString(left) + AsString(right));
        else
            PushInt(unchecked(left.Raw + right.Raw));
    }

    private void ExecuteIntArithmetic(Func<int, int, int> operation)
    {
        int right = PopInt();
        int left = PopInt();
        PushInt(operation(left, right));
    }

    // ------------------------------------------------------------- externals

    /// <summary>
    /// The external opcodes with an explicit <c>case</c> in <see cref="ExecuteExternal"/> — i.e. actually
    /// WIRED to behaviour rather than arity-stubbed. Kept ADJACENT to the switch so the two never drift;
    /// anything in <see cref="ExternalArity"/>.Table but NOT here is a stub (the census gap-detector uses
    /// <c>ExternalArity.Table.Keys \ WiredExternals</c> to report a map's unwired-external demand). A Formats
    /// test asserts every entry is a real external (⊆ ExternalArity.Table).
    /// </summary>
    public static readonly IReadOnlySet<int> WiredExternals = new HashSet<int>
    {
        0x80A1, 0x80A4, 0x80A6, 0x80A7, 0x80A8, 0x80A9, 0x80AA, 0x80AB, 0x80AC, 0x80AE, 0x80AF, 0x80B0,
        0x80B2, 0x80B4, 0x80B6, 0x80B7, 0x80B8, 0x80B9, 0x80BA, 0x80BB, 0x80BC, 0x80BD, 0x80BE, 0x80BF,
        0x80A2, 0x80A3, // scr_return, play_sfx (P101)
        0x80C0, 0x80C1, 0x80C2, 0x80C3, 0x80C4, 0x80C5, 0x80C6, 0x80C7, 0x80C8, 0x80C9, 0x80CA, 0x80CB, 0x80CC, 0x80CD,
        0x80CE, 0x80CF, 0x80D0, 0x80D2, 0x80D3, 0x80D4, 0x80D5, 0x80D7, 0x80D8, 0x80D9, 0x80DA, 0x80DB, 0x80DC, 0x80DD,
        0x80DE, 0x80DF, 0x80E1, 0x80E3, 0x80E4, 0x80E5, 0x80E6, 0x80E7, 0x80E8, 0x80E9, 0x80EA, 0x80EB,
        0x80EC, 0x80ED, 0x80EE, 0x80EF, 0x80F0, 0x80F1, 0x80F2, 0x80F3, 0x80F4, 0x80F5, 0x80F6, 0x80F7, 0x80F8,
        0x80F9, 0x80FA, 0x80FB, 0x80FC, 0x80FD, 0x80FE, 0x80FF, 0x8100, 0x8101, 0x8102, 0x8103, 0x8104, 0x8105, 0x8106, 0x8107,
        0x8108, 0x8109, 0x810A, 0x810B, 0x810C, 0x810D, 0x810E, 0x810F, 0x8110, 0x8111, 0x8112, 0x8113, 0x8114,
        0x8115, 0x8116, 0x8117, 0x8118, 0x8119, 0x811A, 0x811C, 0x811D, 0x811E, 0x811F, 0x8120, 0x8121,
        0x8122, 0x8123, 0x8124, 0x8125, 0x8126, 0x8127, 0x8128, 0x8129, 0x812C, 0x812D, 0x812E, 0x812F, 0x8130,
        0x8131, 0x8132, 0x8133, 0x8134, 0x8136, 0x8137, 0x8138, 0x8139, 0x813B, 0x813C, 0x813D, 0x8141, 0x8143,
        0x8145, 0x8146, 0x8147, 0x8148, 0x8149, 0x814A, 0x814B,
        0x814C, 0x814D, 0x814E, 0x8150, 0x8151, 0x8152, 0x8153, 0x8154,
    };

    /// <summary>
    /// External (engine builtin) dispatch: the few examine/door builtins call
    /// into <see cref="IVmExternals"/>; everything else known to
    /// <see cref="ExternalArity"/> is arity-stubbed (pop Args, push 0 when it
    /// returns) so the stack stays balanced.
    /// </summary>
    private void ExecuteExternal(int opcode)
    {
        switch (opcode)
        {
            case 0x80A4: // obj_name
                PushString(_externals.ObjectName(PopInt()));
                break;
            case 0x80B8: // display_msg
                _externals.DisplayMessage(PopString());
                break;
            case 0x80B9: // script_overrides
                _externals.SetScriptOverrides();
                break;
            case 0x80BC: // self_obj
                PushObject(_externals.SelfObjectId());
                break;
            case 0x80C1: // get_local_var
                PushInt(_externals.GetLocalVar(PopInt()));
                break;
            case 0x80C3: // get_map_var
                PushInt(_externals.GetMapVar(PopInt()));
                break;
            case 0x80C5: // get_global_var
                PushInt(_externals.GetGlobalVar(PopInt()));
                break;
            case 0x8105: // message_str (opGetMessageString pops index, then list)
            {
                int messageIndex = PopInt();
                int messageListIndex = PopInt();
                PushString(_externals.GetMessage(messageListIndex, messageIndex));
                break;
            }

            // ---- variable setters (opSetLocalVar pops value, then index). P126: these are
            // the PERSISTENT stores (LVAR/MVAR survive per-map deltas, GVAR the whole save),
            // while object handles are per-map-load — a handle written here resolves to a
            // different object after reload, so the pop is provenance-checked. VM module
            // globals (store_global) share the handles' lifetime and stay unchecked.
            case 0x80C2: // set_local_var
            {
                int value = PopIntCheckedForHandle("LVAR");
                _externals.SetLocalVar(PopInt(), value);
                break;
            }
            case 0x80C4: // set_map_var
            {
                int value = PopIntCheckedForHandle("MVAR");
                _externals.SetMapVar(PopInt(), value);
                break;
            }
            case 0x80C6: // set_global_var
            {
                int value = PopIntCheckedForHandle("GVAR");
                _externals.SetGlobalVar(PopInt(), value);
                break;
            }

            // ---- script context
            case 0x80BD: // source_obj
                PushObject(_externals.SourceObjectId());
                break;
            case 0x80BE: // target_obj
                PushObject(_externals.TargetObjectId());
                break;
            case 0x80BF: // dude_obj
                PushObject(_externals.DudeObjectId());
                break;
            case 0x80C0: // obj_being_used_with
                PushObject(_externals.ObjectBeingUsedWithId());
                break;
            case 0x80F7: // fixed_param
                PushInt(_externals.FixedParam());
                break;
            case 0x80FA: // action_being_used
                PushInt(_externals.ActionBeingUsed());
                break;
            case 0x80C7: // script_action
                PushInt(_externals.ScriptAction());
                break;
            case 0x810B: // metarule (opMetarule pops param, then rule)
            {
                Value param = Pop();
                int rule = PopInt();
                PushInt(_externals.Metarule(rule, param.Tag == TypeInt ? param.Raw : 0));
                break;
            }

            // ---- pure functions (phase-4 report M0: stub-0 rolls are a trap —
            // critical(0) would fire jam/explosion branches)
            case 0x80B4: // random (opRandom pops max, then min)
            {
                int max = PopInt();
                int min = PopInt();
                PushInt(min >= max ? min : _random.Next(min, max + 1));
                break;
            }
            case 0x80F2: // game_ticks: seconds * 10
                PushInt(PopInt() * 10);
                break;
            case 0x80AC: // roll_vs_skill (pops modifier, skill, obj) — PoC: plain success
                Pop();
                Pop();
                Pop();
                PushInt(RollSuccess);
                break;
            case 0x80AE: // do_check (opDoCheck pops modifier, stat, obj)
            {
                int modifier = PopInt();
                int stat = PopInt();
                PushInt(_externals.DoCheck(PopInt(), stat, modifier));
                break;
            }
            case 0x80CA: // get_critter_stat (pops stat, obj)
            {
                int stat = PopInt();
                PushInt(_externals.GetCritterStat(PopInt(), stat));
                break;
            }
            case 0x80AA: // has_skill (pops skill, obj) — opHasSkill returns skillGetValue (P74-M3)
            {
                int skill = PopInt();
                PushInt(_externals.HasSkill(PopInt(), skill));
                break;
            }
            case 0x80CB: // set_critter_stat — ADJUSTS base stat (pops amount, stat, obj)
            {
                int amount = PopInt();
                int stat = PopInt();
                PushInt(_externals.AdjustCritterBaseStat(PopInt(), stat, amount));
                break;
            }
            case 0x80F3: // has_trait (pops param, obj, type)
            {
                int traitParam = PopInt();
                int traitObj = PopInt();
                PushInt(_externals.HasTrait(PopInt(), traitObj, traitParam));
                break;
            }
            case 0x80A6: // get_pc_stat
                PushInt(_externals.GetPcStat(PopInt()));
                break;
            case 0x8102: // critter_add_trait (pops value, param, kind, obj; pushes -1)
            {
                int traitValue = PopInt();
                int traitParam = PopInt();
                int traitKind = PopInt();
                _externals.CritterAddTrait(PopInt(), traitKind, traitParam, traitValue);
                PushInt(-1);
                break;
            }
            case 0x80D0: // attack (opAttackComplex pops 7 args, then target)
            case 0x80DD: // attack_complex — same engine handler
            {
                for (int i = 0; i < 7; i++)
                    Pop();
                _externals.AttackComplex(PopInt());
                break;
            }
            case 0x80E7: // anim_busy
                PushInt(_externals.AnimBusy(PopInt()) ? 1 : 0);
                break;
            case 0x80A1: // give_exp_points
                _externals.GiveExpPoints(PopInt());
                break;
            case 0x80A9: // override_map_start (pops rotation, elevation, y, x)
            {
                int omsRotation = PopInt();
                int omsElevation = PopInt();
                int omsY = PopInt();
                _externals.OverrideMapStart(PopInt(), omsY, omsElevation, omsRotation);
                break;
            }
            case 0x814C: // rotation_to_tile (pops destTile, srcTile)
            {
                int destTile = PopInt();
                PushInt(Hex.HexGrid.RotationTo(PopInt(), destTile));
                break;
            }
            case 0x80AF: // success: ROLL_SUCCESS or ROLL_CRITICAL_SUCCESS
            {
                int roll = PopInt();
                PushInt(roll is RollSuccess or RollCriticalSuccess ? 1 : 0);
                break;
            }
            case 0x80B0: // critical: ROLL_CRITICAL_FAILURE or ROLL_CRITICAL_SUCCESS
            {
                int roll = PopInt();
                PushInt(roll is RollCriticalFailure or RollCriticalSuccess ? 1 : 0);
                break;
            }

            // ---- dialog (handlers ported from interpreter_extra.cc
            // _op_gsay_* / opStartGameDialog; text resolved here so the host
            // only sees strings + procedure indices)
            case 0x80DE: // start_gdialog (pops background, head, reaction, obj, msgList)
            {
                int backgroundId = PopInt();
                int headId = PopInt();
                int reaction = PopInt(); // the initial fidget family (1/4/7) — P122
                Pop(); // obj
                Pop(); // msgListId — discarded by the engine too
                _externals.DialogSessionStart(headId, backgroundId, reaction);
                break;
            }
            case 0x80E0: // dialogue_reaction (pops the mood nudge: −1 good / 0 / +1 bad) — P122
                _externals.DialogReaction(PopInt());
                break;
            case 0x80DF: // end_dialogue
                _externals.DialogSessionEnd();
                break;
            case 0x811C: // gsay_start
                _externals.DialogStart();
                break;
            case 0x811D: // gsay_end
                _externals.DialogEnd();
                break;
            case 0x811E: // gsay_reply (pops msg-or-string, then msgList)
            {
                Value msg = Pop();
                int listId = PopInt();
                _externals.DialogReply(ResolveDialogText(listId, msg));
                if (msg.Tag == TypeInt) // P53: a message-list reply may carry a speech file (REPLY-only)
                    _externals.PlayDialogVoice(listId, msg.Raw);
                break;
            }
            case 0x811F: // gsay_option (pops reaction, proc, msg, msgList)
            case 0x8121: // giq_option (additionally pops iq LAST — i.e. first pushed)
            {
                int reaction = PopInt();
                Value proc = Pop();
                Value msg = Pop();
                int listId = PopInt();
                if (opcode == 0x8121)
                {
                    int iq = PopInt();
                    // ported from _op_giq_option: the dude's real IN gates dumb/smart options
                    // (positive iq = min, negative = dumb-only max). P25 feeds the real INT.
                    if (!DialogGate.IqOptionVisible(iq, _externals.DialogIntelligence()))
                        break;
                }

                int procedureIndex = proc.Tag == TypeInt
                    ? proc.Raw
                    : _program.FindProcedure(AsString(proc)); // name variant: resolve (engine drops it)
                if (procedureIndex >= 0)
                    _externals.DialogOption(ResolveDialogText(listId, msg), procedureIndex, reaction);
                break;
            }
            case 0x8120: // gsay_message (pops reaction, msg, msgList): reply + auto-done + present
            {
                Pop(); // reaction
                Value msg = Pop();
                int listId = PopInt();
                _externals.DialogReply(ResolveDialogText(listId, msg));
                if (msg.Tag == TypeInt) // P53: gsay_message is a reply path too (game_dialog.cc:2239)
                    _externals.PlayDialogVoice(listId, msg.Raw);
                _externals.DialogEnd();
                break;
            }
            case 0x810A: // float_msg (pops type, msg, obj)
            {
                int type = PopInt();
                Value msg = Pop();
                int objectHandle = PopInt();
                _externals.FloatMessage(objectHandle, AsString(msg), type);
                break;
            }
            case 0x8129: // gdialog_barter — flag-only; arg OVERWRITES the modifier
                _externals.Barter(PopInt());
                break;
            case 0x814E: // gdialog_set_barter_mod
                _externals.GdialogSetBarterMod(PopInt());
                break;
            case 0x8115: // play_gmovie
                _externals.PlayMovie(PopInt());
                break;
            case 0x8133: // game_ui_disable — ported from fallout2-ce src/interpreter_extra.cc opGameUiDisable
                _externals.GameUiEnabled(false);
                break;
            case 0x8134: // game_ui_enable — ported from fallout2-ce src/interpreter_extra.cc opGameUiEnable
                _externals.GameUiEnabled(true);
                break;
            case 0x8146: // endgame_slideshow — ported from fallout2-ce src/interpreter_extra.cc opEndgameSlideshow
                _externals.EndgameSlideshow();
                break;
            case 0x8148: // endgame_movie — ported from fallout2-ce src/interpreter_extra.cc opEndgameMovie
                _externals.EndgameMovie();
                break;
            case 0x8124: // party_add
                _externals.PartyAdd(PopInt());
                break;
            case 0x8125: // party_remove
                _externals.PartyRemove(PopInt());
                break;
            case 0x814B: // party_member_obj (pops pid, pushes handle)
                PushObject(_externals.PartyMemberByPid(PopInt()));
                break;
            case 0x80EF: // critter_damage (pops typeWithFlags, amount, obj)
            {
                int damageTypeWithFlags = PopInt();
                int damageAmount = PopInt();
                _externals.CritterDamage(PopInt(), damageAmount, damageTypeWithFlags);
                break;
            }
            case 0x8147: // move_obj_inven_to_obj (pops dest, then source)
            {
                int moveDest = PopInt();
                _externals.MoveAllInventory(PopInt(), moveDest);
                break;
            }

            // ---- door/container state
            case 0x812D: // obj_is_locked
                PushInt(_externals.ObjIsLocked(PopInt()) ? 1 : 0);
                break;
            case 0x812E: // obj_lock
            case 0x814D: // jam_lock (PoC: a jammed lock is just a locked lock)
                _externals.ObjSetLocked(PopInt(), true);
                break;
            case 0x812F: // obj_unlock
                _externals.ObjSetLocked(PopInt(), false);
                break;
            case 0x8130: // obj_is_open
                PushInt(_externals.ObjIsOpen(PopInt()) ? 1 : 0);
                break;
            case 0x8131: // obj_open
                _externals.ObjSetOpen(PopInt(), true);
                break;
            case 0x8132: // obj_close
                _externals.ObjSetOpen(PopInt(), false);
                break;

            // ---- world mutation (pop orders from interpreter_extra.cc handlers)
            case 0x80B7: // create_object_sid (pops sid, elevation, tile, pid)
            {
                int scriptIndex = PopInt(); // scripts.lst index, -1 = unscripted
                int elevation = PopInt();
                int tile = PopInt();
                int pid = PopInt();
                PushObject(_externals.CreateObject(pid, tile, elevation, scriptIndex));
                break;
            }
            case 0x80F4: // destroy_object
                _externals.DestroyObject(PopInt());
                break;
            case 0x80D8: // add_obj_to_inven (pops item, target)
            {
                int item = PopInt();
                _externals.AddToInventory(PopInt(), item, 1);
                break;
            }
            case 0x8116: // add_mult_objs_to_inven (pops quantity, item, target)
            {
                int quantity = PopInt();
                int item = PopInt();
                _externals.AddToInventory(PopInt(), item, quantity);
                break;
            }
            case 0x80D9: // rm_obj_from_inven (pops item, target)
            {
                int item = PopInt();
                _externals.RemoveFromInventory(PopInt(), item, 1);
                break;
            }
            case 0x8117: // rm_mult_objs_from_inven (pops quantity, item, target) -> removed count
            {
                int quantity = PopInt();
                int item = PopInt();
                PushInt(_externals.RemoveFromInventory(PopInt(), item, quantity));
                break;
            }
            case 0x80BA: // obj_is_carrying_obj (pops pid, critter) -> quantity carried
            {                // ported from fallout2-ce src/interpreter_extra.cc:1040
                int pid = PopInt();
                PushInt(_externals.ObjIsCarryingPid(PopInt(), pid));
                break;
            }
            case 0x810D: // obj_carrying_pid_obj (pops pid, critter) -> item handle (or 0)
            {                // ported from fallout2-ce src/interpreter_extra.cc:3438
                int pid = PopInt();
                PushObject(_externals.ObjCarryingPidObj(PopInt(), pid));
                break;
            }
            case 0x80B6: // move_to (pops elevation, tile, obj) -> new tile
            {
                int elevation = PopInt();
                int tile = PopInt();
                PushInt(_externals.MoveTo(PopInt(), tile, elevation));
                break;
            }
            case 0x80E3: // set_obj_visibility (pops hidden, obj)
            {
                int hidden = PopInt();
                _externals.SetObjectVisibility(PopInt(), hidden != 0);
                break;
            }
            case 0x8100: // obj_pid
                PushInt(_externals.ObjPid(PopInt()));
                break;
            case 0x80C8: // obj_type — PID_TYPE of the object's pid
            {
                int pid = _externals.ObjPid(PopInt());
                PushInt(pid == -1 ? -1 : pid >> 24);
                break;
            }
            case 0x80A7: // tile_contains_pid_obj (pops pid, elevation, tile)
            {
                int pid = PopInt();
                int elevation = PopInt();
                PushObject(_externals.TileContainsPidObj(PopInt(), elevation, pid));
                break;
            }

            // ---- timers + geometry + caps (phase-5 M0)
            case 0x80F0: // add_timer_event (pops param, delay, obj)
            {
                int param = PopInt();
                int delay = PopInt();
                _externals.AddTimerEvent(PopInt(), delay, param);
                break;
            }
            case 0x80F1: // rm_timer_event
                _externals.RemoveTimerEvents(PopInt());
                break;
            case 0x80E1: // metarule3 (pops p3, p2, p1, rule); rule 100 clears (obj, param) timers
            {
                Value p3 = Pop();
                Value p2 = Pop();
                Value p1 = Pop();
                int rule = PopInt();
                _ = p3;
                int metaResult = 0;
                if (rule == 100 && p1.Tag == TypeInt && p2.Tag == TypeInt)
                    _externals.RemoveTimerEventsWithParam(p1.Raw, p2.Raw);
                else if (rule == 103 && p1.Tag == TypeInt) // GET_KILL_COUNT (interpreter_extra.cc:1989)
                    metaResult = _externals.GetKillCount(p1.Raw);
                else if (rule == 110) // METARULE3_110 car-out-of-gas (interpreter_extra.cc:2052 wmCarIsOutOfGas)
                    metaResult = _externals.CarIsOutOfGas() ? 1 : 0;
                else if (rule == 105 && p1.Tag == TypeInt && p2.Tag == TypeInt) // METARULE3_WM_SUBTILE_STATE
                    metaResult = _externals.GetSubtileState(p1.Raw, p2.Raw);
                PushInt(metaResult);
                break;
            }
            case 0x80D4: // tile_num
                PushInt(_externals.ObjTile(PopInt()));
                break;
            case 0x80D2: // tile_distance (pops tile2, tile1)
            {
                int tile2 = PopInt();
                PushInt(Hex.HexGrid.Distance(PopInt(), tile2));
                break;
            }
            case 0x80D3: // tile_distance_objs (pops obj2, obj1)
            {
                int tileB = _externals.ObjTile(PopInt());
                int tileA = _externals.ObjTile(PopInt());
                PushInt(Hex.HexGrid.Distance(tileA, tileB));
                break;
            }
            case 0x80D5: // tile_num_in_direction (pops distance, rotation, tile)
            {
                int distance = PopInt();
                int rotation = PopInt();
                int tile = PopInt();
                PushInt(rotation is >= 0 and < 6
                    ? Hex.HexGrid.TileInDirection(tile, rotation, Math.Max(distance, 0))
                    : tile);
                break;
            }
            case 0x8101: // cur_map_index
                PushInt(_externals.CurrentMapIndex());
                break;
            case 0x8138: // item_caps_total
                PushInt(_externals.CapsTotal(PopInt()));
                break;
            case 0x8139: // item_caps_adjust (pops amount, obj)
            {
                int amount = PopInt();
                PushInt(_externals.CapsAdjust(PopInt(), amount));
                break;
            }
            case 0x80DC: // obj_can_see_obj (pops target, source) — perception + sight path
            {
                int target = PopInt();
                PushInt(_externals.ObjCanSee(PopInt(), target) ? 1 : 0);
                break;
            }
            case 0x80F5: // obj_can_hear_obj (pops target, source) — perception only, no sight path
            {
                int target = PopInt();
                PushInt(_externals.ObjCanHear(PopInt(), target) ? 1 : 0);
                break;
            }
            case 0x80AB: // using_skill (pops skill, object) — interpreter_extra.cc:579
            {
                int skill = PopInt();
                PushInt(_externals.IsUsingSkill(PopInt(), skill) ? 1 : 0);
                break;
            }
            case 0x80FF: // critter_attempt_placement (pops elevation, tile, critter) — interpreter_extra.cc:2812
            {
                int elevation = PopInt();
                int tile = PopInt();
                PushInt(_externals.CritterAttemptPlacement(PopInt(), tile, elevation) ? 1 : 0);
                break;
            }
            case 0x8128: // is_in_combat — interpreter_extra.cc opCombatIsInitialized
                PushInt(_externals.IsInCombat() ? 1 : 0);
                break;
            case 0x80FB: // critter_state (pops critter) — interpreter_extra.cc opGetCritterState
                PushInt(_externals.CritterState(PopInt()));
                break;
            case 0x8122: // poison (pops amount, obj) — interpreter_extra.cc opPoison
            {
                int amount = PopInt();
                _externals.Poison(PopInt(), amount);
                break;
            }
            case 0x8153: // terminate_combat — interpreter_extra.cc opTerminateCombat (0 args)
                _externals.TerminateCombat();
                break;
            case 0x80CE: // animate_move_obj_to_tile (pops speed, tile, obj)
            {
                int speed = PopInt();
                int tile = PopInt();
                _externals.AnimateMoveToTile(PopInt(), tile, speed);
                break;
            }
            case 0x80E9: // set_light_level (pops level)
                _externals.SetLightLevel(PopInt());
                break;
            case 0x8107: // obj_set_light_level (pops distance, intensity, obj)
            {
                int distance = PopInt();
                int intensity = PopInt();
                _externals.SetObjectLightLevel(PopInt(), intensity, distance);
                break;
            }
            case 0x8126: // reg_anim_animate_forever (pops anim, obj)
            {
                int anim = PopInt();
                _externals.RegAnimAnimateForever(PopInt(), anim);
                break;
            }

            // ---- reg_anim batch (interpreter_extra.cc opRegAnim*). The engine gates
            // these on !isInCombat(); the args are popped first either way, so we always
            // pop here and let the host skip execution mid-combat (it keeps the stack
            // balanced). reg_anim_func pops (param, cmd); the move/animate ops pop
            // (delay, target, obj) like their opcode handlers.
            case 0x810E: // reg_anim_func
            {
                Value param = Pop();
                int cmd = PopInt();
                switch (cmd)
                {
                    case 1: _externals.RegAnimBegin(); break;          // OP_REG_ANIM_FUNC_BEGIN
                    case 2: _externals.RegAnimClear(param.Raw); break; // OP_REG_ANIM_FUNC_CLEAR (param = object)
                    case 3: _externals.RegAnimEnd(); break;            // OP_REG_ANIM_FUNC_END
                }
                break;
            }
            case 0x810F: // reg_anim_animate (pops delay, anim, obj)
            case 0x8110: // reg_anim_animate_reverse
            {
                int delay = PopInt();
                int anim = PopInt();
                _externals.RegAnimAnimate(PopInt(), anim, delay, opcode == 0x8110);
                break;
            }
            case 0x8111: // reg_anim_obj_move_to_obj (pops delay, dest, obj)
            case 0x8112: // reg_anim_obj_run_to_obj
            {
                int delay = PopInt();
                int dest = PopInt();
                _externals.RegAnimMoveToObject(PopInt(), dest, delay, opcode == 0x8112);
                break;
            }
            case 0x8113: // reg_anim_obj_move_to_tile (pops delay, tile, obj)
            case 0x8114: // reg_anim_obj_run_to_tile
            {
                int delay = PopInt();
                int tile = PopInt();
                _externals.RegAnimMoveToTile(PopInt(), tile, delay, opcode == 0x8114);
                break;
            }

            // ---- clock (ported from fallout2-ce scripts.cc gameTimeGetHour())
            case 0x80EA: // game_time (ticks)
                PushInt(_externals.GameTime());
                break;
            case 0x80EB: // game_time_in_seconds
                PushInt(_externals.GameTime() / 10);
                break;
            case 0x80F6: // game_time_hour (hhmm)
            {
                int time = _externals.GameTime();
                PushInt(100 * (time / 600 / 60 % 24) + time / 600 % 60);
                break;
            }
            case 0x8118: // month (epoch June 24, 2241; 10 ticks/s)
            {
                int day = _externals.GameTime() / 864000;
                PushInt(MonthFromEpochDay(day));
                break;
            }
            case 0x8119: // day (1..31; opGetDay — same epoch as month above)
            {
                int day = _externals.GameTime() / 864000;
                PushInt(DayFromEpochDay(day));
                break;
            }
            case 0x8154: // debug_msg (opDebugMessage): a developer no-op — pop the string and discard
                Pop();   // (faithful: the engine prints to the debug console only; nothing player-visible)
                break;
            case 0x80EC: // elevation (opGetObjectElevation)
                PushInt(_externals.ObjElevation(PopInt()));
                break;
            case 0x8127: // critter_injure (pops flags, then critter)
            {
                int flags = PopInt();
                _externals.CritterInjure(PopInt(), flags);
                break;
            }
            case 0x810C: // anim (pops frame, then anim, then who)
            {
                int frame = PopInt();
                int anim = PopInt();
                _externals.Anim(PopInt(), anim, frame);
                break;
            }
            case 0x8150: // obj_on_screen (opObjectOnScreen): no camera headless; the engine tests the
                PopInt(); // viewport — return 1 (visible), the benign map-enter default. DOCUMENTED DIVERGENCE.
                PushInt(1);
                break;
            case 0x80CF: // tile_in_tile_rect (opTileInTileRect): pops 5 (points[0..4] in pop order)
            {
                int p0 = PopInt(), p1 = PopInt(), p2 = PopInt(), p3 = PopInt(), p4 = PopInt();
                PushInt(Hex.HexGrid.TileInTileRect(p0, p1, p2, p3, p4));
                break;
            }
            case 0x8106: // critter_inven_obj (pops type, then critter)
            {
                int type = PopInt();
                PushObject(_externals.CritterInventoryObject(PopInt(), type));
                break;
            }
            case 0x80A8: // set_map_start (pops rotation, elevation, y, x)
            {
                int rotation = PopInt(), elevation = PopInt(), y = PopInt(), x = PopInt();
                _externals.SetMapStart(x, y, elevation, rotation);
                break;
            }
            case 0x80EE: // kill_critter_type (pops deathFrame, then pid)
            {
                int deathFrame = PopInt();
                _externals.KillCritterType(PopInt(), deathFrame);
                break;
            }
            case 0x80E8: // critter_heal (pops amount, then critter) — interpreter_extra.cc opCritterHeal
            {
                int amount = PopInt();
                PushInt(_externals.CritterHeal(PopInt(), amount));
                break;
            }
            case 0x8123: // get_poison (pops obj) — interpreter_extra.cc opGetPoison
                PushInt(_externals.GetPoison(PopInt()));
                break;
            case 0x80ED: // kill_critter (pops deathFrame, then object) — interpreter_extra.cc opKillCritter
            {
                int deathFrame = PopInt();
                _externals.KillCritter(PopInt(), deathFrame);
                break;
            }
            case 0x8103: // critter_rm_trait (pops value, param, kind, then object) — opCritterRemoveTrait
            {
                int value = PopInt();
                int param = PopInt();
                int kind = PopInt();
                PushInt(_externals.CritterRemoveTrait(PopInt(), kind, param, value)); // last pop = object; pushes -1
                break;
            }
            case 0x8145: // use_obj_on_obj (pops target, then item) — interpreter_extra.cc opUseObjectOnObject
            {
                int target = PopInt();
                _externals.UseObjectOnObject(target, PopInt()); // second pop = item
                break;
            }
            case 0x813C: // critter_mod_skill (pops points, skill, then critter) — opCritterModifySkill
            {
                int points = PopInt();
                int skill = PopInt();
                PushInt(_externals.CritterModSkill(PopInt(), skill, points)); // last pop = critter; pushes 0
                break;
            }
            case 0x8108: // scripts_request_world_map (0 args) — interpreter_extra.cc opWorldmap
                _externals.RequestWorldMap();
                break;
            case 0x80F9: // dialogue_system_enter (0 args) — interpreter_extra.cc opGameDialogSystemEnter
                _externals.DialogueSystemEnter();
                break;
            case 0x80E4: // load_map (pops param, then mapIndexOrName: int index or string filename) — opLoadMap
            {
                int param = PopInt();
                Value mapArg = Pop();
                if (mapArg.IsString)
                    _externals.LoadMapByName(AsString(mapArg), param);
                else
                    _externals.LoadMap(mapArg.Raw, param);
                break;
            }
            case 0x8143: // attack_setup (pops defender, then attacker) — interpreter_extra.cc opAttackSetup
            {
                int defender = PopInt();
                _externals.AttackSetup(PopInt(), defender);
                break;
            }
            case 0x811A: // explosion (pops maxDamage, elevation, tile) — interpreter_extra.cc opExplosion
            {
                int maxDamage = PopInt();
                int elevation = PopInt();
                _externals.Explosion(PopInt(), elevation, maxDamage);
                break;
            }
            case 0x80E5: // wm_area_set_pos (pops y, x, then city) — opWorldmapCitySetPos
            {
                int y = PopInt();
                int x = PopInt();
                _externals.WmAreaSetPos(PopInt(), x, y); // last pop = city
                break;
            }
            case 0x80E6: // set_exit_grids (opSetExitGrids): pops rotation, tile, destElev, map, elevation
            {
                _ = PopInt(); // destinationRotation — popped + discarded (interpreter_extra.cc:2182)
                int tile = PopInt(), destElev = PopInt(), map = PopInt();
                _externals.SetExitGrids(PopInt(), map, destElev, tile); // last pop = source elevation
                break;
            }
            case 0x80DA: // wield_obj_critter (opWieldItem): pops item, then critter
            {
                int item = PopInt();
                _externals.WieldObjCritter(PopInt(), item);
                break;
            }
            case 0x8149: // obj_art_fid (opGetObjectFid): pops object, pushes its art FID
                PushInt(_externals.ObjArtFid(PopInt()));
                break;
            case 0x8151: // critter_is_fleeing (opCritterIsFleeing): pops critter, pushes the FLEEING bit
                PushInt(_externals.CritterIsFleeing(PopInt()) ? 1 : 0);
                break;
            case 0x8152: // critter_set_flee_state (opCritterSetFleeState): pops fleeing, then critter
            {
                int fleeing = PopInt();
                _externals.CritterSetFleeState(PopInt(), fleeing);
                break;
            }
            case 0x80B2: // mark_area_known (opMarkAreaKnown): pops markType (data[0]), areaId (data[1]), mode (data[2])
            {
                int markType = PopInt(), areaId = PopInt(), mode = PopInt();
                _externals.MarkAreaKnown(markType, areaId, mode);
                break;
            }
            case 0x80FC: // game_time_advance (opGameTimeAdvance): pops ticks
                _externals.GameTimeAdvance(PopInt());
                break;
            case 0x80BB: // tile_contains_obj_pid (opTileContainsObjectWithPid): pops pid, elevation, tile
            {
                int pid = PopInt(), elevation = PopInt();
                PushInt(_externals.TileContainsObjPid(PopInt(), elevation, pid) ? 1 : 0);
                break;
            }
            case 0x80CD: // animate_stand_reverse_obj (opAnimateStandReverse): pops object
                _externals.AnimateStandReverse(PopInt());
                break;
            // ---- P101 (Tier B queries / C object-inv / D radiation): fallout2-ce interpreter_extra.cc ----
            case 0x80C9: // item_subtype (opGetItemType, :1274): pop obj, push ITEM_TYPE
                PushInt(_externals.ItemSubtype(PopInt()));
                break;
            case 0x8104: // proto_data (opGetProtoData, :2962): pops member FIRST then pid, push value
            {
                int pdMember = PopInt();
                int pdPid = PopInt();
                // P113 (item 7b): NAME (1) / DESCRIPTION (2) are STRING members (proto.cc
                // protoGetDataMember); the engine's missing-text fallback is proto.msg entry 10.
                if (_externals.ProtoDataString(pdPid, pdMember) is { } pdText)
                    PushString(pdText);
                else
                    PushInt(_externals.ProtoData(pdPid, pdMember));
                break;
            }
            case 0x80F8: // tile_is_visible (opTileIsVisible, :2671): pop tile, push 0/1
                PushInt(_externals.TileIsVisible(PopInt()));
                break;
            case 0x8109: // inven_cmds (_op_inven_cmds, :3090): pops index, cmd, obj; cmd==13 → item handle
            {
                int icIndex = PopInt();
                int icCmd = PopInt();
                int icObj = PopInt();
                PushInt(icCmd == 13 ? _externals.InvenPtr(icObj, icIndex) : 0);
                break;
            }
            case 0x812C: // inven_unwield (_op_inven_unwield, :4050): pops NOTHING, self holsters its weapon
                _externals.InvenUnwield();
                break;
            case 0x80DB: // use_obj (opUseObject, :1750): pop obj, run its use_p_proc
                _externals.UseObj(PopInt());
                break;
            case 0x80D7: // drop_obj (opDrop, :1597): pop obj, self drops it to the ground
                _externals.DropObj(PopInt());
                break;
            case 0x80A2: // scr_return (opScrReturn, :476): pop value, store the script's return value
                _externals.ScrReturn(PopInt());
                break;
            case 0x80FD: // radiation_inc (opRadiationIncrease, :2779): pops amount FIRST then object
            {
                int rAmt = PopInt();
                _externals.Radiation(PopInt(), rAmt);
                break;
            }
            case 0x80FE: // radiation_dec (opRadiationDecrease, :2794): pops amount FIRST then object; subtracts
            {
                int rAmt = PopInt();
                _externals.Radiation(PopInt(), -rAmt);
                break;
            }
            // ---- P101 (Tier A, cosmetic): ported from fallout2-ce src/interpreter_extra.cc ----
            case 0x814A: // art_anim (opGetFidAnim): the anim-code byte of a FID (pure bit-op, :4659)
                PushInt((PopInt() & 0xFF0000) >> 16);
                break;
            case 0x8136: // gfade_out (opGameFadeOut, :4172): pop 1, fade to black
                PopInt();
                _externals.ScreenFade(fadeIn: false);
                break;
            case 0x8137: // gfade_in (opGameFadeIn, :4185): pop 1, fade from black
                PopInt();
                _externals.ScreenFade(fadeIn: true);
                break;
            case 0x80A3: // play_sfx (opPlaySfx, :490): pop 1 string, play it
                _externals.PlaySfx(PopString());
                break;
            case 0x80CC: // animate_stand_obj (opAnimateStand, :1339): pop 1 object, idle stand anim
                _externals.AnimateStand(PopInt());
                break;
            case 0x813B: // reg_anim_play_sfx (opRegAnimPlaySfx, :4255): pops delay, name, obj — play now (delay/queue dropped)
            {
                PopInt();                         // delay
                string sfx = PopString();         // name
                PopInt();                         // obj
                _externals.PlaySfx(sfx);
                break;
            }
            case 0x813D: // sfx_build_char_name (opSfxBuildCharName, :4325): pops extra, anim, obj; pushes the sfx NAME
                PopInt(); PopInt(); PopInt();     // extra, anim, obj
                PushString("");                   // name feeds play_sfx only (never branched) — minimal faithful "" (silent)
                break;
            case 0x8141: // sfx_build_weapon_name (opSfxBuildWeaponName, :4376): pops target, hitMode, weapon, weaponSfxType; pushes NAME
                PopInt(); PopInt(); PopInt(); PopInt();
                PushString("");
                break;
            default:
            {
                if (!ExternalArity.Table.TryGetValue(opcode, out (string Name, int Args, bool Returns) arity))
                    throw new InvalidDataException($"Unknown external opcode 0x{opcode:X4}.");

                for (int i = 0; i < arity.Args; i++)
                    Pop();
                if (arity.Returns)
                    PushInt(0);
                _onStubbedExternal?.Invoke(
                    $"stubbed external {arity.Name} (0x{opcode:X4}): popped {arity.Args}"
                    + (arity.Returns ? ", pushed 0" : ""));
                break;
            }
        }
    }

    // ------------------------------------------------------------ stack/values

    private void Push(Value value) => _stack.Add(value);

    private void PushInt(int value) => _stack.Add(Value.Int(value));

    /// <summary>P126: push an object handle — a plain int carrying the provenance flag
    /// the persistent-var setters use for the stale-handle diagnostic.</summary>
    private void PushObject(int handle) => _stack.Add(Value.ObjectHandle(handle));

    /// <summary>programPushString() reduced to a list of dynamic strings.</summary>
    private void PushString(string? value)
    {
        _dynamicStrings.Add(value ?? "Error");
        _stack.Add(new Value(TypeDynamicString, _dynamicStrings.Count - 1));
    }

    private Value Pop()
    {
        if (_stack.Count == 0)
            throw new InvalidDataException("Data stack underflow.");
        Value value = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return value;
    }

    private int PopInt()
    {
        Value value = Pop();
        if (value.Tag != TypeInt)
            throw new InvalidDataException($"Expected an int on the stack, got tag 0x{value.Tag:X4}.");
        return value.Raw;
    }

    /// <summary>P126 stale-handle guard: PopInt for the persistent-var setters — a LIVE
    /// object handle (non-zero, straight off an object-returning external) written to a
    /// GVAR/MVAR/LVAR is a latent bug (handles are per-map-load, never serialized: the
    /// stored int resolves to a DIFFERENT object after reload). No vanilla script does
    /// this (P124 census); the diagnostic exists to catch future content/engine work.
    /// Reported once per (kind, value) to stderr — never the golden-captured stdout.</summary>
    private int PopIntCheckedForHandle(string varKind)
    {
        Value value = Pop();
        if (value.Tag != TypeInt)
            throw new InvalidDataException($"Expected an int on the stack, got tag 0x{value.Tag:X4}.");
        if (value.IsObjectHandle && value.Raw != 0 && _reportedHandleStores.Add((varKind, value.Raw)))
            Console.Error.WriteLine($"stale-handle: a script stored live object handle {value.Raw} in a"
                + $" persistent {varKind} — handles do not survive map reload/save, this WILL dangle");
        return value.Raw;
    }

    private readonly HashSet<(string, int)> _reportedHandleStores = [];

    private string PopString() => AsString(Pop());

    private Value ReturnPop()
    {
        if (_returnStack.Count == 0)
            throw new InvalidDataException("Return stack underflow.");
        Value value = _returnStack[^1];
        _returnStack.RemoveAt(_returnStack.Count - 1);
        return value;
    }

    private int ReturnPopInt()
    {
        Value value = ReturnPop();
        if (value.Tag != TypeInt)
            throw new InvalidDataException($"Expected an int on the return stack, got tag 0x{value.Tag:X4}.");
        return value.Raw;
    }

    private void ReturnPush(Value value) => _returnStack.Add(value);

    private IntProcedure ProcedureAt(int index)
    {
        if (index < 0 || index >= _program.Procedures.Count)
            throw new InvalidDataException($"Procedure index {index} is out of range.");
        return _program.Procedures[index];
    }

    private Value StackAt(int index)
    {
        if (index < 0 || index >= _stack.Count)
            throw new InvalidDataException($"Stack access at {index} is out of range (stack desync).");
        return _stack[index];
    }

    private void StackSet(int index, Value value)
    {
        if (index < 0 || index >= _stack.Count)
            throw new InvalidDataException($"Stack store at {index} is out of range (stack desync).");
        _stack[index] = value;
    }

    private string AsString(Value value) => value.Tag switch
    {
        TypeStaticString => _program.GetStaticString(value.Raw),
        TypeDynamicString when value.Raw >= 0 && value.Raw < _dynamicStrings.Count => _dynamicStrings[value.Raw],
        TypeDynamicString => throw new InvalidDataException($"Bad dynamic string handle {value.Raw}."),
        TypeInt => value.Raw.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new InvalidDataException($"Cannot read tag 0x{value.Tag:X4} as a string."),
    };

    /// <summary>ported from ProgramValue::isEmpty() (dynamic strings fall through to empty).</summary>
    private static bool IsEmpty(Value value) => value.Tag switch
    {
        TypeInt or TypeStaticString => value.Raw == 0,
        _ => true,
    };

    /// <summary>Truthiness for and/or, ported from opLogicalOperatorAnd/Or: strings are always true.</summary>
    private static bool IsTruthy(Value value) => value.IsString || value.Raw != 0;
}
