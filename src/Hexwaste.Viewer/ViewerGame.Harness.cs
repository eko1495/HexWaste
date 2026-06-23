using Hexwaste.Formats;
using Hexwaste.Formats.Art;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Pal;
using Hexwaste.Formats.Proto;
using Hexwaste.Formats.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

// The headless test-harness StartupAction dispatch (the 100+ --probe commands), extracted from
// LoadContent so the core file holds engine logic, not test scaffolding. Pure move: RunStartupActions()
// is called at the tail of LoadContent exactly where the foreach used to run (no behaviour change).
public sealed partial class ViewerGame
{
    private void RunStartupActions()
    {
        foreach (StartupAction action in StartupActions)
        {
            switch (action)
            {
                case StartupAction.UseHex(var hex, var lockpick):
                {
                    MapObject? target = _solidObjects[_elevation].FirstOrDefault(o => o.HexTile == hex)
                        ?? _flatObjects[_elevation].FirstOrDefault(o => o.HexTile == hex);
                    if (target is null)
                    {
                        Console.Error.WriteLine($"nothing at hex {hex}");
                        break;
                    }

                    _camera.SetCenter(hex);
                    if (_dude is not null) // teleport adjacent so range checks pass (test plumbing)
                        _dude.Dude.HexTile = Formats.Hex.HexGrid.TileInDirection(hex, 3);
                    if (lockpick)
                        TryLockpick(target);
                    else
                        InteractWith(target);
                    Console.WriteLine($"{(lockpick ? "lockpick" : "use")}@{hex}: locked={target.IsLockedState} open={_openDoors.Contains(target)}");
                    if (_lootContainer is { } looted)
                    {
                        Console.WriteLine($"LOOT {ObjectName(looted)}:");
                        foreach (MapObject item in looted.Inventory)
                            Console.WriteLine($"  ITEM: {ObjectName(item)} x{item.StackCount}");
                    }

                    break;
                }
                case StartupAction.ExamineCritter(var critterHex):
                {
                    MapObject? critter = CritterAt(critterHex);
                    if (critter is null)
                    {
                        Console.Error.WriteLine($"no critter at hex {critterHex}");
                        break;
                    }
                    if (GetCritterState(critter) is not { } state)
                    {
                        Console.Error.WriteLine($"no critter proto stats for pid 0x{critter.Pid:X8}");
                        break;
                    }

                    Console.WriteLine($"CRITTER {ObjectName(critter)} @{critterHex} pid=0x{critter.Pid:X8}");
                    Console.WriteLine($"  hp={state.CurrentHp}/{state.MaxHp} ac={state.ArmorClass} ap={state.MaxActionPoints}"
                        + $" meleeDmg={state.MeleeDamage} sequence={state.Sequence} unarmedSkill={state.UnarmedSkill}");
                    Console.WriteLine($"  team={critter.Team} (proto {state.Proto.Team}) aiPacket={critter.AiPacket}"
                        + $" (proto {state.Proto.AiPacket}) results=0x{critter.CombatResults:X} dead={state.IsDead}");
                    Console.WriteLine($"  dt={state.DamageThreshold} dr={state.DamageResistance} exp={state.Proto.Experience}"
                        + $" killType={state.Proto.KillType} bodyType={state.Proto.BodyType} damageType={state.Proto.DamageType}");
                    break;
                }
                case StartupAction.AwarenessProbe(var awHex):
                {
                    // P69: drive the PLAYER examine path (Examine, not the diagnostic dump) + report state-only
                    // whether the Awareness HP/weapon lines appeared (booleans + the perk rank — never the
                    // copyrighted name/description text). --perk-probe 0 1 grants Awareness for the with-perk run.
                    if (CritterAt(awHex) is not { } awCritter)
                    {
                        Console.Error.WriteLine($"awareness-probe: no critter at {awHex}");
                        break;
                    }
                    int awBefore = _messageLog.Count;
                    Examine(awCritter);
                    var awAdded = _messageLog.Skip(awBefore).ToList();
                    int awHp = awAdded.Any(l => l.StartsWith("HP:")) ? 1 : 0;
                    int awWpn = awAdded.Any(l => l.StartsWith("Wielding")) ? 1 : 0;
                    Console.WriteLine($"awareness-probe: hex={awHex} awareness={DudePerkRank(Formats.Perks.PerkId.Awareness)}"
                        + $" hpLine={awHp} weaponLine={awWpn}");
                    break;
                }
                case StartupAction.EncounterWalk(var x0, var y0, var x1, var y1, var steps):
                {
                    // Phase-10 M1 traveling-dot demo: Bresenham from (x0,y0) toward
                    // (x1,y1), +30 game-min per pixel-step, roll an encounter each
                    // step. Deterministic under --rng-seed (golden transcript).
                    var wmRng = new Formats.Combat.SystemCombatRng(RngSeed ?? 1);
                    var encounters = new Formats.Map.WorldEncounters(Worldmap, wmRng, x0, y0);
                    int x = x0, y = y0, dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
                    int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
                    int hits = 0;
                    for (int s = 0; s < steps && (x != x1 || y != y1); s++)
                    {
                        int e2 = 2 * err;
                        if (e2 > -dy) { err -= dy; x += sx; }
                        if (e2 < dx) { err += dx; y += sy; }
                        _clock.Ticks += 18000; // 30 game-minutes per step
                        if (encounters.Roll(x, y, _clock.Hour, _ => 0, _dudeLevel, _clock.Day) is { } r)
                        {
                            hits++;
                            Console.WriteLine($"encounter @step{s + 1} @({x},{y}): "
                                + $"{r.Entry.Spawns.FirstOrDefault()?.Group ?? "?"} [{r.Table.LookupName}] {r.Entry.Situation}");
                        }
                    }
                    Console.WriteLine($"encounter-walk: ({x0},{y0})->({x1},{y1}) steps={steps} encounters={hits}");
                    break;
                }
                case StartupAction.EncounterSpawnAt(var emap, var group, var count):
                {
                    // Phase-10 M3 demo: load a transient encounter map and spawn a
                    // named worldmap.txt group on it (the live worldmap roll wires the
                    // same _pendingEncounter hook). Deterministic under --rng-seed; the
                    // census is the golden transcript.
                    var entry = new Formats.Map.EncounterEntry(100, null,
                        [new Formats.Map.EncounterSpawn(count, count, group)], "AMBUSH", []);
                    var table = new Formats.Map.EncounterTable("demo", [], [entry]);
                    _pendingEncounter = new Formats.Map.EncounterResult(table, entry);
                    LoadMap(emap, null, transient: true);

                    var spawned = _solidObjects[_elevation]
                        .Where(o => o.Id == -3 && Fid.Type(o.Fid) is ObjectType.Critter)
                        .OrderBy(o => o.HexTile)
                        .ToList();
                    // Flat spawns = the group's If()-gated ground members: scenery/items
                    // (e.g. ARRO_Rats' Xander Root at Distance:10) + Dead corpses. The
                    // pid + dist-from-dude lock the per-member If()/Distance fidelity (M4).
                    int dudeTile = _dude?.Dude.HexTile ?? 0;
                    var flat = _flatObjects[_elevation].Where(o => o.Id == -3)
                        .OrderBy(o => o.HexTile).ToList();
                    foreach (MapObject o in spawned)
                        Console.WriteLine($"  spawn pid=0x{o.Pid:X8} tile={o.HexTile} rot={o.Rotation}"
                            + $" hp={o.CurrentHp} team={o.Team} sid={(o.Sid == -1 ? "none" : "bound")} items={o.Inventory.Count}");
                    foreach (MapObject o in flat)
                        Console.WriteLine($"  flat pid=0x{o.Pid:X8} tile={o.HexTile}"
                            + $" dist={Formats.Hex.HexGrid.Distance(dudeTile, o.HexTile)} dead={o.IsDead}");
                    int deadCount = flat.Count(o => o.IsDead);
                    Console.WriteLine($"encounter: map={emap} group={group} requested={count}"
                        + $" critters={spawned.Count} items={flat.Count - deadCount} corpses={deadCount}");
                    break;
                }
                case StartupAction.EncounterFight(var fmap, var ga, var ca, var gb, var cb):
                {
                    // Phase-16 M3: a synthetic X-FIGHTING-Y entry — two groups, situation
                    // FIGHTING. SpawnEncounter puts them on teams 1 & 2 and StartBrawl
                    // opens combat. The team census + open phase is the golden.
                    var entry = new Formats.Map.EncounterEntry(100, null,
                        [new Formats.Map.EncounterSpawn(ca, ca, ga), new Formats.Map.EncounterSpawn(cb, cb, gb)],
                        "FIGHTING", []);
                    var table = new Formats.Map.EncounterTable("demo", [], [entry]);
                    _pendingEncounter = new Formats.Map.EncounterResult(table, entry);
                    LoadMap(fmap, null, transient: true); // SpawnEncounter → StartBrawl

                    var live = _solidObjects[_elevation]
                        .Where(o => o.Id == -3 && Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead).ToList();
                    int teamA = live.Count(o => o.Team == 1), teamB = live.Count(o => o.Team == 2);
                    Console.WriteLine($"encounter-fight: groups={ga}/{gb} spawned={live.Count}"
                        + $" teamA={teamA} teamB={teamB} hostiles={_combat.Hostiles.Count} phase={_combat.Phase}");
                    break;
                }
                case StartupAction.EncounterAnswer(var engage):
                    _autoEncounterAnswer = engage; // phase-16 M1: pre-answer the avoid prompt
                    break;
                case StartupAction.CombatWalk(var cwFight, var cwWalk, var cwAp, var cwCripple):
                {
                    // Phase-18 M0/M1: open combat, give the dude a clean Ap budget, then walk
                    // toward WalkHex — the AP-gated walk halts when AP runs out (1 AP/hex, or
                    // 4/hex with a crippled leg). Reports the distance covered + AP left.
                    MapObject? foe = CritterAt(cwFight, aliveOnly: true);
                    if (foe is null || _dude is null) { Console.Error.WriteLine($"combat-walk: no critter at {cwFight}"); break; }
                    _dude.Dude.HexTile = Formats.Hex.HexGrid.TileInDirection(cwFight, 3); // adjacent, like --fight
                    RebuildBlockedTiles(_dude.Dude);
                    if (cwCripple)
                        _dude.Dude.CombatResults |= Formats.Combat.CriticalTables.DamCripLegLeft;
                    _combat.TryAttack(foe); // opens combat
                    for (int g = 0; g < 3000 && _combat.IsResolving; g++) { _animator.Update(10); _combat.ProcessAnimations(); }
                    _combat.SetDudeAp(cwAp); // clean AP for the walk measurement
                    int startTile = _dude.Dude.HexTile, apBefore = _combat.DudeAp;
                    bool started = _dude.WalkTo(cwWalk);
                    for (int g = 0; g < 200_000 && _dude.Moving; g++) { _dude.Update(33); }
                    Console.WriteLine($"combat-walk: started={started} crippleLeg={cwCripple} ap={apBefore} start={startTile}"
                        + $" end={_dude.Dude.HexTile} dist={Formats.Hex.HexGrid.Distance(startTile, _dude.Dude.HexTile)}"
                        + $" apLeft={_combat.DudeAp}");
                    break;
                }
                case StartupAction.TravelStepDemo(var sx2, var sy2, var sa):
                {
                    // Phase-17 M2: drive the REAL animated path — TravelTo (animate=true)
                    // sets _activeTravel; drain StepAnimatedTravel one cadence tick at a
                    // time, counting ticks vs pixels (slow terrain steps fewer pixels/tick).
                    WorldArea? sdest = _cities.Areas.FirstOrDefault(a => a.Index == sa);
                    if (sdest is null) { Console.Error.WriteLine($"travel-step: no area {sa}"); break; }
                    _autoEncounterAnswer ??= true; // engage any encounter so the leg terminates
                    _animateTravel = true;
                    _worldPosX = sx2; _worldPosY = sy2;
                    long clockBefore = _clock.Ticks;
                    TravelTo(sdest); // sets _activeTravel + returns (no synchronous drain)
                    int ticks = 0, pixels = 0;
                    while (_activeTravel is not null && ticks < 200_000)
                    {
                        int bx = _worldPosX, by = _worldPosY;
                        StepAnimatedTravel(TravelTickMs);
                        ticks++;
                        if (_worldPosX != bx || _worldPosY != by) pixels++;
                    }
                    string outcome = _currentMapTransient ? "encounter" : "arrived";
                    Console.WriteLine($"travel-step: ({sx2},{sy2})->area{sa} ticks={ticks} pixels={pixels}"
                        + $" clockAdv={_clock.Ticks - clockBefore} {outcome} map={_currentMapName} worldPos=({_worldPosX},{_worldPosY})");
                    break;
                }
                case StartupAction.TravelSaveMid(var mx2, var my2, var ma, var mTicks):
                {
                    // Phase-17 M4: start animated travel, step partway (staying mid-leg),
                    // then save+load in-process and confirm the dot worldPos + in-flight
                    // destination round-trip (load queues an auto-resume toward the dest).
                    WorldArea? mdest = _cities.Areas.FirstOrDefault(a => a.Index == ma);
                    if (mdest is null) { Console.Error.WriteLine($"travel-save-mid: no area {ma}"); break; }
                    _autoEncounterAnswer ??= true;
                    _animateTravel = true;
                    _worldPosX = mx2; _worldPosY = my2;
                    TravelTo(mdest); // sets _activeTravel
                    for (int t = 0; t < mTicks && _activeTravel is not null; t++)
                        StepAnimatedTravel(TravelTickMs);
                    int savedX = _worldPosX, savedY = _worldPosY;
                    int savedDest = _activeTravel?.Dest.Index ?? -1;
                    bool wasTravelling = _activeTravel is not null;

                    string realPath = SavePath;
                    SavePath = Path.Combine(Path.GetTempPath(), "hexwaste-travelmid-test.json");
                    SaveGame();
                    LoadGame();
                    if (File.Exists(SavePath)) File.Delete(SavePath);
                    SavePath = realPath;

                    int resumeDest = _resumeTravelDest?.Index ?? -1;
                    Console.WriteLine($"travel-save-mid: travelling={wasTravelling}"
                        + $" savedWorldPos=({savedX},{savedY}) savedDest={savedDest}"
                        + $" -> loadedWorldPos=({_worldPosX},{_worldPosY}) resumeDest={resumeDest}"
                        + $" roundTrip={savedX == _worldPosX && savedY == _worldPosY && savedDest == resumeDest}");
                    break;
                }
                case StartupAction.TravelResume(var rx, var ry, var ra):
                {
                    // Phase-16 M2: stand at (rx,ry) mid-leg bound for area ra, "on" the
                    // encounter map; walk off the edge (Map<0) and confirm travel auto-
                    // resumes toward the destination — no worldmap re-click.
                    WorldArea? rdest = _cities.Areas.FirstOrDefault(a => a.Index == ra);
                    if (rdest is null) { Console.Error.WriteLine($"travel-resume: no area {ra}"); break; }
                    _autoEncounterAnswer ??= true; // engage any encounter on the resumed leg
                    _animateTravel = false;        // headless: synchronous drain (P17-M2)
                    _worldPosX = rx; _worldPosY = ry;
                    _travelDestination = rdest;
                    _currentMapTransient = true;                          // pretend we're on the encounter map
                    ApplyTransition(new MapDestination(-1, 0, 0, 0));     // walk off the edge → sets _resumeTravelDest
                    if (_resumeTravelDest is { } d) { _resumeTravelDest = null; TravelTo(d); }
                    int spawned = _solidObjects[_elevation].Count(o => o.Id == -3 && Fid.Type(o.Fid) is ObjectType.Critter);
                    Console.WriteLine($"travel-resume: from ({rx},{ry})->area{ra} {(_currentMapTransient ? "encounter" : "arrived")}"
                        + $" map={_currentMapName} worldPos=({_worldPosX},{_worldPosY}) spawned={spawned}");
                    break;
                }
                case StartupAction.ForceOutdoorsman(var od):
                    _forceOutdoorsman = od;
                    break;
                case StartupAction.TravelFrom(var tx, var ty, var ai):
                {
                    // Phase-10 M3 live-travel demo: stand at worldmap (tx,ty) and travel
                    // toward area ai, rolling encounters along the way (deterministic
                    // under --rng-seed). Either an encounter map loads (group spawned)
                    // or the dude arrives at the town.
                    // Headless can't answer an interactive prompt, so default a detected
                    // encounter to ENGAGE unless --encounter-answer set it (phase-16 M1).
                    _autoEncounterAnswer ??= true;
                    _animateTravel = false; // headless: drain the whole leg synchronously (P17-M2)
                    _worldPosX = tx;
                    _worldPosY = ty;
                    WorldArea? dest = _cities.Areas.FirstOrDefault(a => a.Index == ai);
                    if (dest is null)
                    {
                        Console.WriteLine($"travel-from: no area {ai} in city.txt");
                        break;
                    }
                    TravelTo(dest);
                    int spawnedCount = _solidObjects[_elevation]
                        .Count(o => o.Id == -3 && Fid.Type(o.Fid) is ObjectType.Critter);
                    string outcome = _currentMapTransient ? "encounter" : "arrived";
                    Console.WriteLine($"travel-from: ({tx},{ty})->area{ai} {outcome} map={_currentMapName}"
                        + $" worldPos=({_worldPosX},{_worldPosY}) spawned={spawnedCount}");
                    break;
                }
                case StartupAction.CenterHex(var centerHex):
                    _camera.SetCenter(centerHex); // screenshot testing (P23)
                    break;
                case StartupAction.IqProbe(var iqHex, var forceIn):
                {
                    // P25: force the dude's IN, open the NPC's dialogue, report the OPTION COUNT
                    // (an int — never the copyrighted option text) at the greeting. Different IN ->
                    // different count proves giq_option dumb/smart gating (low IN loses smart
                    // options, gains dumb ones).
                    MapObject? iqNpc = CritterAt(iqHex);
                    if (iqNpc is null) { Console.Error.WriteLine($"iq-probe: no critter at {iqHex}"); break; }
                    if (_dudeGcd is not null && forceIn >= 0)
                        _dudeGcd.Stats.BaseStats[4] = forceIn; // STAT_INTELLIGENCE (test plumbing)
                    int effIn = _dude is not null && GetCritterState(_dude.Dude) is { } cs ? cs.Stat(4) : -1;
                    TalkTo(iqNpc);
                    Console.WriteLine($"iq-probe: hex={iqHex} in={effIn} options={_dialog?.Options.Count ?? 0}");
                    _dialog = null;
                    break;
                }
                case StartupAction.SpeechProbe(var spList, var spMsg, var spForced):
                {
                    // P53: report the dialogue VO compose + gate — the audio basename (an asset id),
                    // the composed sound\speech path, and the ShouldSpeak verdict. NEVER the message
                    // text. ForcedAudio "-" uses the real MSG lookup (empty on the whole slice).
                    string? audio = spForced == "-" ? _scriptHost?.LookupAudio(spList, spMsg) : spForced;
                    bool would = Formats.Sound.SpeechName.ShouldSpeak(isReply: true, headIsValid: true, audio);
                    string path = string.IsNullOrEmpty(audio) ? "(none)" : Formats.Sound.SpeechName.Path(audio);
                    Console.WriteLine($"speech-probe: list={spList} msg={spMsg} "
                        + $"audio={(string.IsNullOrEmpty(audio) ? "(empty)" : audio)} path={path} "
                        + $"reply=1 head=1 wouldPlay={(would ? 1 : 0)}");
                    break;
                }
                case StartupAction.DeathProbe(var dpHex):
                {
                    // P26: for the critter at <hex>, report the gore death anim a solid (dmg 20)
                    // burst / explosion / laser kill picks (DeathAnims.Pick) and the art-RESOLVED
                    // anim (PickDeathAnim's _check_death) — proving whether the critter ships gore art.
                    MapObject? dpc = CritterAt(dpHex, includeFlat: true);
                    if (dpc is null) { Console.Error.WriteLine($"death-probe: no critter at {dpHex}"); break; }
                    string Probe(string label, int dmgType, int atkAnim)
                    {
                        int desired = Formats.Combat.DeathAnims.Pick(dmgType, 20, atkAnim, Formats.Combat.DeathAnims.ViolenceNormal);
                        int resolved = PickDeathAnim(dpc, desired);
                        // gore = the gore anim survived art-resolution (not a plain FALL_BACK/FRONT fallback).
                        bool gore = resolved != Formats.Combat.DeathAnims.FallBack && resolved != Formats.Combat.DeathAnims.FallFront;
                        return $"{label}(desired={desired} resolved={resolved} gore={gore})";
                    }
                    Console.WriteLine($"death-probe: pid=0x{dpc.Pid:X} "
                        + $"{Probe("burst", 0, Formats.Combat.DeathAnims.FireBurst)} "
                        + $"{Probe("laser", 1, Formats.Combat.DeathAnims.FireBurst)} "
                        + $"{Probe("explode", 6, Formats.Combat.DeathAnims.FireSingle)}");
                    break;
                }
                case StartupAction.SfxProbe(var spHex):
                {
                    // P34-M5: the composed combat-sfx names for the critter (scorpion → MASCRP* which ship;
                    // human → HMWARR* which don't, i.e. faithful-silent) + the map's first ambient entry.
                    MapObject? spc = CritterAt(spHex, includeFlat: true);
                    if (spc is null) { Console.Error.WriteLine($"sfx-probe: no critter at {spHex}"); break; }
                    string? baseName = _artIndex.CritterBaseName(spc.Fid);
                    int wc = Fid.WeaponCode(spc.Fid);
                    string Sfx(int anim, Formats.Sound.SfxName.CharacterSoundEffect ex) =>
                        Formats.Sound.SfxName.CharName(baseName, anim, ex, wc) ?? "-";
                    var amb = _mapList.GetAmbientSfx(_currentMapName);
                    string ambient = amb.Count > 0 ? $"{amb[0].Name}:{amb[0].Chance}" : "-";
                    Console.WriteLine($"sfx-probe: pid=0x{spc.Pid:X} "
                        + $"swing={Sfx(16, Formats.Sound.SfxName.CharacterSoundEffect.Contact)} "
                        + $"hit={Sfx(14, Formats.Sound.SfxName.CharacterSoundEffect.Unused)} "
                        + $"die={Sfx(20, Formats.Sound.SfxName.CharacterSoundEffect.Die)} "
                        + $"reload={Formats.Sound.SfxName.WeaponName(Formats.Sound.SfxName.WeaponSoundEffect.Ready, (byte)'1', true)} "
                        + $"ambient={ambient}");
                    break;
                }
                case StartupAction.ReactionProbe(var rpHex, var attRot):
                {
                    // P34-M6: the reaction-anim codes the critter would get from an attacker at attRot.
                    MapObject? rc = CritterAt(rpHex, includeFlat: true);
                    if (rc is null) { Console.Error.WriteLine($"reaction-probe: no critter at {rpHex}"); break; }
                    bool front = Formats.Combat.SneakAttack.IsHitFromFront(attRot, rc.Rotation);
                    bool backArt = _vfs.Exists(_artIndex.GetFrmPath(
                        Fid.Build(ObjectType.Critter, Fid.Index(rc.Fid), 15 /*HIT_FROM_BACK*/, Fid.WeaponCode(rc.Fid))));
                    Console.WriteLine($"reaction-probe: pid=0x{rc.Pid:X} att={attRot} def={rc.Rotation} front={(front ? 1 : 0)} "
                        + $"hit={Formats.Combat.ReactionAnims.HitReaction(front, backArt)} "
                        + $"dodge={Formats.Combat.ReactionAnims.Dodge} fall={Formats.Combat.ReactionAnims.KnockdownFall(front)} "
                        + $"getup={Formats.Combat.ReactionAnims.StandUp(Formats.Combat.ReactionAnims.FallBack)} backArt={(backArt ? 1 : 0)}");
                    break;
                }
                case StartupAction.FloatTextProbe(var ftHex):
                {
                    // P45: exercise the float-text layer's spawn path over a real critter and report its
                    // STATE — never the message text. Hex-int colours = the engine float_msg constants.
                    MapObject? ftc = CritterAt(ftHex, includeFlat: true);
                    if (ftc is null) { Console.Error.WriteLine($"float-text: no critter at {ftHex}"); break; }
                    _floatText.Add(ftc.HexTile, _elevation, "10", CombatFloatColors.DamageNpc); // a sample damage float
                    (int sx, int sy) = Formats.Combat.FloatText.AnchorOffset(16, 11); // a fixed sample size (camera-independent)
                    static string Hex(Microsoft.Xna.Framework.Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";
                    Console.WriteLine($"float-text: hex={ftHex} count={_floatText.Count} "
                        + $"maxCount={Formats.Combat.FloatText.MaxCount} lifetimeMs={Formats.Combat.FloatText.LifetimeMs(1)} "
                        + $"anchorOff16x11=({sx},{sy}) "
                        + $"damageNpc={Hex(CombatFloatColors.DamageNpc)} damageDude={Hex(CombatFloatColors.DamageDude)} "
                        + $"crit={Hex(CombatFloatColors.Critical)} miss={Hex(CombatFloatColors.Miss)}");
                    break;
                }
                case StartupAction.MapUpdateProbe when _map is not null && _scriptHost is not null:
                {
                    // M0 diagnostic: run map_update_p_proc (the map script + every scripted object —
                    // scripts.cc scriptsExecMapUpdateScripts) and TRACE its observable side effects.
                    // map_enter already ran on load, so _stubbedExternals + the lightgrid hold its state;
                    // we snapshot, run map_update, and diff. The lighting callbacks are wrapped to count
                    // calls (the P21 set_light_level / obj_set_light_level path). Transient diagnostic
                    // (the process exits after printing), so the in-memory side effects never persist.
                    int ambientBefore = _lightGrid.Ambient;
                    bool fixedBefore = AmbientFixed;
                    int lightCalls = 0, objLightCalls = 0;
                    var lightLevels = new List<int>();
                    Action<int>? prevLight = _scriptHost.LightLevelRequested;
                    Action<MapObject, int, int>? prevObjLight = _scriptHost.ObjectLightRequested;
                    _scriptHost.LightLevelRequested = lvl => { lightCalls++; lightLevels.Add(lvl); prevLight?.Invoke(lvl); };
                    _scriptHost.ObjectLightRequested = (o, ii, dd) => { objLightCalls++; prevObjLight?.Invoke(o, ii, dd); };
                    var stubsBefore = new HashSet<string>(_stubbedExternals.Keys);

                    IEnumerable<MapObject> muObjs = _map.Elevations
                        .Where(e => e is not null)
                        .SelectMany(e => e!.Objects)
                        .Where(o => o.Sid != -1 && o != _dude?.Dude);
                    _scriptHost.RunMapUpdate(_map, muObjs, _dude?.Dude);

                    _scriptHost.LightLevelRequested = prevLight;
                    _scriptHost.ObjectLightRequested = prevObjLight;
                    var newStubs = _stubbedExternals.Keys.Where(k => !stubsBefore.Contains(k)).OrderBy(k => k).ToList();
                    Console.WriteLine($"map-update: map={_currentMapName} ran=1 "
                        + $"lightCalls={lightCalls} levels=[{string.Join(",", lightLevels)}] objLightCalls={objLightCalls} "
                        + $"ambient={ambientBefore}->{_lightGrid.Ambient} fixed={(fixedBefore ? 1 : 0)}->{(AmbientFixed ? 1 : 0)} "
                        + $"newStubbedExternals={newStubs.Count}[{string.Join(",", newStubs)}]");
                    break;
                }
                case StartupAction.SmokeScan when _map is not null && _scriptHost is not null:
                {
                    // A per-map content-coverage smoke scan: census the map + run its map_update pass
                    // (map_enter already ran on load → its stubs are in _stubbedExternals), then report
                    // the FULL set of unwired externals the scripts fired — so adding a NEW city, you run
                    // one command and see what it needs that isn't wired. Deterministic enumeration + the
                    // script pass only (no walking / UI / RNG). State-only: counts + external NAMES.
                    int critters = 0, containers = 0, doors = 0, scripted = 0;
                    foreach (MapObject o in _map.Elevations.Where(e => e is not null).SelectMany(e => e!.Objects))
                    {
                        if (o == _dude?.Dude) continue;
                        if (o.Sid != -1) scripted++;
                        if (Fid.Type(o.Fid) is ObjectType.Critter) critters++;
                        else if (IsContainer(o)) containers++;
                        else if (IsDoor(o)) doors++;
                    }

                    IEnumerable<MapObject> smokeObjs = _map.Elevations
                        .Where(e => e is not null).SelectMany(e => e!.Objects)
                        .Where(o => o.Sid != -1 && o != _dude?.Dude);
                    _scriptHost.RunMapUpdate(_map, smokeObjs, _dude?.Dude);

                    var stubs = _stubbedExternals.Keys.OrderBy(k => k).ToList();
                    Console.WriteLine($"smoke: map={_currentMapName} critters={critters} containers={containers} "
                        + $"doors={doors} scripted={scripted} stubs={stubs.Count}[{string.Join(",", stubs)}]");
                    break;
                }
                case StartupAction.DragEquip(var deRow, var deSlot):
                {
                    // P47: drive the real drag-to-equip path headlessly + report the equipped state.
                    if (deRow < 0 || deRow >= _dudeInventory.Count)
                    {
                        Console.Error.WriteLine($"drag-equip: no item at row {deRow}");
                        break;
                    }
                    MapObject deItem = _dudeInventory[deRow];
                    if (deSlot == -1)
                        DropFromInventory(deRow);
                    else
                        EquipFromDrag(deItem, deSlot == 2 ? Formats.Combat.EquipSlot.Armor : Formats.Combat.EquipSlot.Weapon);
                    bool deEquipped = deSlot == -1 ? !_dudeInventory.Contains(deItem)
                        : deSlot == 2 ? deItem.IsWorn
                        : deItem.IsInHand;
                    Formats.Combat.CritterState? deSt = GetCritterState(_dude?.Dude);
                    Console.WriteLine($"drag-equip: row={deRow} slot={deSlot} pid=0x{deItem.Pid:X} "
                        + $"equipped={(deEquipped ? 1 : 0)} "
                        + $"AC={deSt?.ArmorClass ?? 0} DT={deSt?.DamageThreshold ?? 0} DR={deSt?.DamageResistance ?? 0}");
                    break;
                }
                case StartupAction.AimClick(var acRow):
                {
                    // P49: drive the called-shot dialog selection (the same SelectAimRow the live click uses).
                    // Row -1 just OPENS the dialog (leaves it up for a screenshot) without selecting.
                    OpenAimDialog();
                    if (acRow >= 0)
                        SelectAimRow(acRow);
                    int pen = Formats.Combat.CriticalTables.LocationPenalty[
                        Math.Clamp(AimLocation, 0, Formats.Combat.CriticalTables.LocationPenalty.Length - 1)];
                    Console.WriteLine($"aim-click: row={acRow} loc={AimLocation} name={AimName(AimLocation)} penalty={pen}");
                    break;
                }
                case StartupAction.CompanionTactics(var ctHex, var ctRow, var ctCount):
                {
                    // P50: open the combat-control window for the critter + cycle a row, via the REAL path.
                    MapObject? ctc = CritterAt(ctHex, includeFlat: true);
                    if (ctc is null) { Console.Error.WriteLine($"tactics: no critter at {ctHex}"); break; }
                    OpenTactics(ctc);
                    for (int n = 0; n < ctCount; n++)
                        TacticsActivate(ctRow);
                    if (ctRow >= 0) // row -1 = leave the window OPEN (for a screenshot)
                        _tacticsMember = null;
                    Formats.Combat.CompanionAi eff = CompanionSettings(ctc).Effective();
                    Console.WriteLine($"tactics: hex={ctHex} disposition={CompanionSettings(ctc).Disposition} "
                        + $"attackWho={eff.AttackWho} distance={eff.Distance} runAway={eff.RunAway} chemUse={eff.ChemUse} "
                        + $"areaAttack={eff.AreaAttack} bestWeapon={eff.WeaponPref}");
                    break;
                }
                case StartupAction.CombatProcProbe(var cpHex):
                {
                    // P35: run the critter's per-turn combat_p_proc (fp=4) and report whether it DEFINES
                    // the proc (RunObjectProc returns null when the proc is absent) + whether it overrides.
                    MapObject? cpc = CritterAt(cpHex, includeFlat: true);
                    if (cpc is null) { Console.Error.WriteLine($"combat-proc: no critter at {cpHex}"); break; }
                    int scriptIndex = cpc.Sid != -1 && _map.ScriptsBySid.TryGetValue(cpc.Sid, out MapScriptRecord? cpr)
                        ? cpr.ScriptListIndex : -1;
                    var r = _scriptHost?.RunObjectProc(cpc, _map, dude: null, 4, -1, "combat_p_proc");
                    Console.WriteLine($"combat-proc: pid=0x{cpc.Pid:X} script={scriptIndex} "
                        + $"hasProc={r is not null} overridden={r?.Overridden ?? false} fp=4");
                    break;
                }
                case StartupAction.CombatProcHit(var atkHex) when _dude is not null:
                {
                    // P35 fp=2: fire the attacker's on-hit combat_p_proc with target = the dude and report
                    // the dude's poison delta (the scorpion stings → poison). Deterministic under --rng-seed.
                    MapObject? atk = _solidObjects[_elevation].FirstOrDefault(o =>
                        o.HexTile == atkHex && Fid.Type(o.Fid) is ObjectType.Critter);
                    if (atk is null) { Console.Error.WriteLine($"combat-proc-hit: no critter at {atkHex}"); break; }
                    MapObject dudeObj = _dude.Dude;
                    int before = dudeObj.Poison;
                    var hr = _scriptHost?.RunCombatProc(atk, dudeObj, dudeObj, _map, 2);
                    Console.WriteLine($"combat-proc-hit: atk=0x{atk.Pid:X} hasProc={hr is not null} "
                        + $"dudePoison={before}->{dudeObj.Poison}");
                    break;
                }
                case StartupAction.PoisonTick(var initPoison, var minutes) when _dude is not null:
                {
                    // P35-M3: poison the dude, advance the clock, fire the over-time ticks, report deltas.
                    MapObject pd = _dude.Dude;
                    int hpBefore = pd.CurrentHp;
                    pd.Poison = initPoison;
                    SchedulePoison();
                    _clock.Ticks += (long)minutes * 60 * Formats.GameClock.TicksPerSecond;
                    ProcessPoison();
                    Console.WriteLine($"poison-tick: poison={initPoison}->{pd.Poison} hp={hpBefore}->{pd.CurrentHp} minutes={minutes}");
                    break;
                }
                case StartupAction.DrugProbe(var drugPid, var drugMinutes) when _dudeGcd is not null:
                {
                    // P37: advance the clock (cumulative across probes), fire the scheduled wear-off, then
                    // report the active drug contribution per stat (_drugBonus, the immediate effect minus any
                    // wear-off that has now fired) + the pending count. The immediate effect was applied by the
                    // preceding --use-item, so the bonus at minutes=0 shows the up-kick; later probes show the
                    // ramp down toward the net-zero wear-off.
                    _clock.Ticks += (long)drugMinutes * 60 * Formats.GameClock.TicksPerSecond;
                    ProcessDrugs();
                    var bonus = Enumerable.Range(0, 35)
                        .Where(s => _drugBonus[s] != 0)
                        .Select(s => $"{s}={_drugBonus[s]}");
                    Console.WriteLine($"drug-probe: pid={drugPid} minutes={drugMinutes} pending={_pendingDrugEvents.Count} "
                        + $"bonus=[{string.Join(",", bonus)}]");
                    break;
                }
                case StartupAction.AddictProbe(var adPid, var adSeed, var adMinutes) when _dude is not null && _scriptHost is not null:
                {
                    // P38: seed the addiction RNG, give+use one drug (the faithful UseDrug→TryAddict roll),
                    // advance the clock, fire onset/recovery, report the addiction GVAR + withdrawal penalty.
                    _addictionRng = new Formats.Combat.SystemCombatRng(adSeed);
                    if (RebuildObject(adPid, 1) is { } adDrug)
                    {
                        AddToDudeInventory(adDrug);
                        int adIdx = _dudeInventory.FindIndex(i => i.Pid == adPid);
                        if (adIdx >= 0)
                            UseInventoryItem(adIdx); // runs UseDrug → the addiction roll on the seeded RNG
                    }
                    _clock.Ticks += (long)adMinutes * 60 * Formats.GameClock.TicksPerSecond;
                    ProcessWithdrawals();
                    int adGvar = Formats.Item.DrugAddiction.GvarForPid(adPid);
                    int adGvarVal = adGvar >= 0 ? _scriptHost.GlobalVars.GetValueOrDefault(adGvar, 0) : -1;
                    var wd = Enumerable.Range(0, 35)
                        .Where(s => _withdrawalBonus[s] != 0)
                        .Select(s => $"{s}={_withdrawalBonus[s]}");
                    Console.WriteLine($"addict-probe: pid={adPid} seed={adSeed} minutes={adMinutes} "
                        + $"gvar{adGvar}={adGvarVal} withdrawal=[{string.Join(",", wd)}] pendingWd={_pendingWithdrawalEvents.Count}");
                    break;
                }
                case StartupAction.KillsProbe(var ktQuery):
                {
                    // P38: report the kill tally (killsGetByType). >=0 → one type; <0 → all non-zero.
                    if (ktQuery >= 0)
                        Console.WriteLine($"kills-probe: type={ktQuery} count={(ktQuery < _killsByType.Length ? _killsByType[ktQuery] : 0)}");
                    else
                    {
                        var all = Enumerable.Range(0, _killsByType.Length)
                            .Where(k => _killsByType[k] != 0).Select(k => $"{k}={_killsByType[k]}");
                        Console.WriteLine($"kills-probe: all=[{string.Join(",", all)}]");
                    }
                    break;
                }
                case StartupAction.UseBook(var bookPid):
                {
                    // P39: give one book + read it (the real UseInventoryItem→book branch prints the "book:" line).
                    if (RebuildObject(bookPid, 1) is { } bookObj)
                    {
                        AddToDudeInventory(bookObj);
                        int bookIdx = _dudeInventory.FindIndex(i => i.Pid == bookPid);
                        if (bookIdx >= 0)
                            UseInventoryItem(bookIdx);
                    }
                    break;
                }
                case StartupAction.AiHealProbe(var healHex):
                {
                    // P42: give the critter a stimpak, hurt it to 1 HP, run the real TryNpcHeal — proves
                    // the AI heal mechanic deterministically on a real slice critter (the swarm Den maps
                    // never let the dude win a clean 1-on-1 vs a stimpak NPC, so this is the live proof).
                    MapObject? hc = CritterAt(healHex);
                    if (hc is null) { Console.Error.WriteLine($"ai-heal-probe: no critter at {healHex}"); break; }
                    if (RebuildObject(40, 1) is { } stim) hc.Inventory.Add(stim); // a stimpak
                    int hmax = GetCritterState(hc)?.MaxHp ?? hc.CurrentHp;
                    hc.CurrentHp = 1;
                    bool healed = TryNpcHeal(hc);
                    Console.WriteLine($"ai-heal-probe: hex={healHex} healed={healed} hp=1->{hc.CurrentHp} max={hmax}");
                    break;
                }
                case StartupAction.AiWeaponProbe(var awHex) when _dude is not null:
                {
                    // P43: force the wielded gun dry and run the real AI weapon switch — proves the
                    // best_weapon backup-draw on a multi-weapon slice NPC (no --fight golden reaches
                    // one, so this is the live proof; the golden-fight critters carry no weapons).
                    MapObject? aw = CritterAt(awHex);
                    if (aw is null) { Console.Error.WriteLine($"ai-weapon-probe: no critter at {awHex}"); break; }
                    Formats.Combat.AiPacket? ap = GetAiPacket(aw);
                    (ProtoInfo? eqp, MapObject? eqi) = EquippedWeapon(aw);
                    if (eqi is not null) eqi.AmmoQuantity = 0; // simulate the gun going dry
                    string carried = string.Join(",", CritterInventoryWeapons(aw)
                        .Where(w => w.Item != eqi).Select(w => $"0x{w.Proto.Pid:X}"));
                    int chosen = _combat.ProbeAiWeaponSwitch(aw, _dude.Dude);
                    Console.WriteLine($"ai-weapon-probe: hex={awHex} bestWeapon={ap?.BestWeapon ?? -1} "
                        + $"equipped=0x{eqp?.Pid:X} carried=[{carried}] switchedTo="
                        + (chosen < 0 ? "fists" : $"0x{chosen:X}"));
                    break;
                }
                case StartupAction.LoadAmmo(var ammoPid) when _dude is not null:
                {
                    // P40: switch the equipped weapon to ammoPid (unload current → reload-with-pid), then
                    // report the loaded type + the combat-relevant ammo mods (proving the JHP/AP delta).
                    (ProtoInfo? wp, MapObject? wi) = EquippedWeapon(_dude.Dude);
                    if (wp?.Weapon is { } w && wi is not null)
                    {
                        UnloadEquippedWeapon(); // eject current so the type can change (no mixed mags)
                        bool ok = TryReloadWith(_dude.Dude, wp, wi, ammoPid);
                        Formats.Proto.AmmoProtoStats? la = LoadedAmmo(wp, wi);
                        Console.WriteLine($"ammo-select: weapon={wp.Pid} ok={ok} loaded={wi.AmmoTypePid} "
                            + $"qty={WeaponAmmo(wp, wi)}/{w.AmmoCapacity} ac={la?.AcModifier ?? 0} dr={la?.DrModifier ?? 0} "
                            + $"mult={la?.DamageMultiplier ?? 1} div={la?.DamageDivisor ?? 1}");
                    }
                    else
                        Console.Error.WriteLine("load-ammo: no weapon equipped");
                    break;
                }
                case StartupAction.MultihexProbe(var mhPid):
                {
                    // P36: report the proto's OBJECT_MULTIHEX (0x800) bit — verifies a slice critter is multihex.
                    try
                    {
                        Formats.Proto.ProtoInfo mhp = _protos.Get(mhPid);
                        Console.WriteLine($"multihex-probe: pid=0x{mhPid:X} multihex={((mhp.Flags & 0x800) != 0 ? 1 : 0)} flags=0x{mhp.Flags:X}");
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
                    {
                        Console.WriteLine($"multihex-probe: pid=0x{mhPid:X} (no proto)");
                    }
                    break;
                }
                case StartupAction.TerminateCombatProbe(var tcHex) when _dude is not null:
                {
                    // P35-M5: enter combat with the critter, drop it to ≤half HP, run its fp=4 — a script
                    // that yields (ACTemVil) calls terminate_combat → DISENGAGING + the fight ends.
                    MapObject? tc = _solidObjects[_elevation].FirstOrDefault(o =>
                        o.HexTile == tcHex && Fid.Type(o.Fid) is ObjectType.Critter);
                    if (tc is null) { Console.Error.WriteLine($"terminate-combat: no critter at {tcHex}"); break; }
                    _combat.BeginScriptAggro(tc, _dude.Dude); // enter combat (the critter is hostile)
                    var phaseBefore = _combat.Phase;
                    tc.CurrentHp = 1; // ≤half → a yield script fires terminate_combat
                    _scriptHost?.RunCombatProc(tc, null, _dude.Dude, _map, 4);
                    for (int i = 0; i < 4 && _combat.Phase != Formats.Combat.CombatPhase.Idle; i++)
                        _combat.Step();
                    Console.WriteLine($"terminate-combat: hex={tcHex} phaseBefore={phaseBefore} "
                        + $"maneuver={tc.Maneuver} phaseAfter={_combat.Phase}");
                    break;
                }
                case StartupAction.TraitProbe(var t1, var t2):
                {
                    // P28-M1: set the dude's traits (the array is shared with ScriptHost.DudeTraits,
                    // so has_trait stays consistent) and report the live stat/skill effects via
                    // GetCritterState — proves traitGetStatModifier/SkillModifier are wired.
                    if (_dudeGcd is null || _dude is null) { Console.Error.WriteLine("trait-probe: no dude"); break; }
                    _dudeGcd.Traits[0] = t1; _dudeGcd.Traits[1] = t2;
                    if (GetCritterState(_dude.Dude) is { } ts)
                        Console.WriteLine($"trait-probe: traits=[{t1},{t2}]"
                            + $" STR={ts.Stat(0)} AG={ts.Stat(5)} maxAP={ts.MaxActionPoints} AC={ts.ArmorClass}"
                            + $" SEQ={ts.Sequence} carry={ts.CarryWeight} crit={ts.Stat(15)} heal={ts.Stat(14)}"
                            + $" melee={ts.MeleeDamage} smallGuns={ts.SkillValue(0)} firstAid={ts.SkillValue(6)}"
                            + $" hasGifted={(_scriptHost?.DudeTraits.Contains(Formats.Combat.TraitModifiers.Gifted) == true ? 1 : 0)}");
                    break;
                }
                case StartupAction.PerkProbe(var perkIndex, var perkLevel):
                {
                    // P28-M2: at the given level, test the perk gates (PerkRules.CanAdd) and, if
                    // eligible, add a rank — reporting the live stat effect via GetCritterState.
                    if (_dude is null || perkIndex < 0 || perkIndex >= Formats.Perks.PerkTable.Count)
                    { Console.Error.WriteLine($"perk-probe: bad index {perkIndex}"); break; }
                    _dudeLevel = perkLevel;
                    Formats.Perks.PerkData pd = Formats.Perks.PerkTable.Get(perkIndex);
                    int GetStat(int s) => GetCritterState(_dude.Dude)?.Stat(s) ?? 0;
                    int GetSkill(int s) => GetCritterState(_dude.Dude)?.SkillValue(s) ?? 0;
                    int GetGlobal(int g) => _scriptHost?.GlobalVars.GetValueOrDefault(g, 0) ?? 0;
                    int before = pd.Stat >= 0 ? GetStat(pd.Stat) : -1;
                    bool canAdd = Formats.Perks.PerkRules.CanAdd(pd, _dudePerkRanks, _dudeLevel, GetStat, GetSkill, GetGlobal);
                    if (canAdd)
                        _dudePerkRanks[perkIndex]++;
                    int after = pd.Stat >= 0 ? GetStat(pd.Stat) : -1;
                    Console.WriteLine($"perk-probe: index={perkIndex} frm={pd.FrmId} level={perkLevel} minLevel={pd.MinLevel}"
                        + $" picks={Formats.Perks.PerkRules.PicksEarned(perkLevel, false)} canAdd={canAdd}"
                        + $" rank={_dudePerkRanks[perkIndex]} stat={pd.Stat} before={before} after={after}");
                    break;
                }
                case StartupAction.PerkPick(var ppLevel, var ppRow):
                {
                    // P28-M4: drive the real perk picker — set level, open it (if a pick is available),
                    // select the Row-th eligible perk, report index/count (not the name text).
                    if (_dude is null || _dudeGcd is null) { Console.Error.WriteLine("perk-pick: no dude"); break; }
                    _dudeLevel = ppLevel;
                    int avail = AvailablePerkPicks();
                    _perkPickOpen = avail > 0;
                    List<int> elig = EligiblePerks();
                    int picked = -1;
                    if (_perkPickOpen && ppRow >= 0 && ppRow < elig.Count)
                    {
                        picked = elig[ppRow];
                        ChoosePerk(picked);
                    }
                    Console.WriteLine($"perk-pick: level={ppLevel} available={avail} eligible={elig.Count}"
                        + $" pickedRow={ppRow} pickedIndex={picked} rank={(picked >= 0 ? _dudePerkRanks[picked] : 0)} open={_perkPickOpen}");
                    break;
                }
                case StartupAction.WeightProbe:
                {
                    // P24: the dude's carried weight vs capacity, plus the over-encumbrance
                    // combat AP penalty — exercises the whole InventoryWeight stack on real protos.
                    int carried = DudeCarriedWeight(), cap = DudeCarryCapacity();
                    Console.WriteLine($"weight: carried={carried} capacity={cap}"
                        + $" encumbered={Formats.Map.InventoryWeight.IsEncumbered(carried, cap)}"
                        + $" apPenalty={DudeEncumbranceApPenalty()} items={_dudeInventory.Count}");
                    break;
                }
                case StartupAction.SneakProbe(var sneakFlag):
                {
                    // P29 A-M0: set the sneaking flag and report the two-layer state. Working is wired
                    // in A-M2 (the periodic roll); A-M0 reports it as-is (0 until then).
                    _sneak.FlagSet = sneakFlag != 0;
                    Console.WriteLine($"sneak-probe: flag={(_sneak.FlagSet ? 1 : 0)} working={(_sneak.Working ? 1 : 0)}"
                        + $" sneaking={(_sneak.IsSneaking ? 1 : 0)} skill={DudeSkillValue(8)}");
                    break;
                }
                case StartupAction.BackstabProbe(var attRot, var defRot):
                {
                    // P30 A-M1: the pure facing predicate + the multiplier a qualifying non-crit backstab
                    // would apply (front → no bonus → 2; behind → 4).
                    bool front = Formats.Combat.SneakAttack.IsHitFromFront(attRot, defRot);
                    Console.WriteLine($"backstab-probe: att={attRot} def={defRot} front={(front ? 1 : 0)} mult={(front ? 2 : 4)}");
                    break;
                }
                case StartupAction.SneakRoll(var sneakSeed):
                {
                    // P30 A-M2: a deterministic periodic SKILL_SNEAK roll under a fixed seed → Working +
                    // the next reschedule tick count (the isolated _sneakRng).
                    _sneakRng = new Formats.Combat.SystemCombatRng(sneakSeed);
                    _sneak.FlagSet = true;
                    RollSneak();
                    Console.WriteLine($"sneak-roll: skill={DudeSkillValue(8)} working={(_sneak.Working ? 1 : 0)} next={_sneakTicksRemaining}");
                    break;
                }
                case StartupAction.DetectProbe(var pe, var dist, var canSee, var flag, var working):
                {
                    // P30 A-M3: the detection decision an NPC would make against the dude for a controlled
                    // perception/distance/facing + sneak state (the dude's real Sneak skill).
                    _sneak.FlagSet = flag != 0;
                    _sneak.Working = working != 0;
                    bool detected = Formats.Combat.PerceptionDetect.IsWithinPerception(dist, pe, DudeSkillValue(8),
                        canSee != 0, targetIsGlass: false, targetIsDude: true, _sneak.IsSneaking, _sneak.FlagSet, inCombat: false);
                    Console.WriteLine($"detect-probe: pe={pe} dist={dist} canSee={canSee} sneak={DudeSkillValue(8)}"
                        + $" flag={(_sneak.FlagSet ? 1 : 0)} working={(_sneak.Working ? 1 : 0)} detected={(detected ? 1 : 0)}");
                    break;
                }
                case StartupAction.KarmaProbe:
                {
                    // P31 B-M0: read the PC meta-stats through the get_pc_stat provider (proves the
                    // 0x80A6 seam: 3=reputation, 4=karma, was stubbed to 0).
                    int Pc(int s) => _scriptHost?.PcStatProvider?.Invoke(s) ?? 0;
                    Console.WriteLine($"karma-probe: karma={Pc(Formats.Int.PcStat.Karma)} rep={Pc(Formats.Int.PcStat.Reputation)}"
                        + $" level={Pc(Formats.Int.PcStat.Level)} xp={Pc(Formats.Int.PcStat.Experience)} unspent={Pc(Formats.Int.PcStat.UnspentSkillPoints)}");
                    break;
                }
                case StartupAction.GetGlobal(var ggId):
                {
                    // P32-M1: read a global var (proves vault13.gam seeding fired at new-game).
                    Console.WriteLine($"get-global: GVAR{ggId} = {_scriptHost?.GlobalVars.GetValueOrDefault(ggId, 0) ?? 0}");
                    break;
                }
                case StartupAction.PartyProbe(var ppPid):
                {
                    // Is this critter PID a data\party.txt recruitable companion? (Vic-pattern feasible
                    // vs needs custom content.) Reports membership + level_minimum — state-only, no names.
                    var desc = PartyTable()?.ForPid(ppPid);
                    Console.WriteLine($"party-probe: pid=0x{ppPid:X} member={(desc is not null ? 1 : 0)} "
                        + $"levelMin={desc?.LevelMinimum ?? -1}");
                    break;
                }
                case StartupAction.PlaceProbe(var fromHex, var toHex):
                {
                    // P32: drive the real critter_attempt_placement relocate path (PlaceObject) on a map
                    // critter, proving it moves to a different tile.
                    MapObject? obj = _solidObjects[_elevation].FirstOrDefault(o =>
                        o.HexTile == fromHex && Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead);
                    if (obj is null)
                        Console.WriteLine($"place-probe: no critter at {fromHex}");
                    else
                    {
                        bool ok = PlaceObject(obj, toHex, _elevation);
                        Console.WriteLine($"place-probe: from={fromHex} requested={toHex} now={obj.HexTile} ok={ok}");
                    }
                    break;
                }
                case StartupAction.RegAnimMove(var fromHex, var toHex):
                {
                    // P33-M1: drive the reg_anim_func batch executor on a real map critter
                    // (no shippable script fires reg_anim_obj_move_to_tile, so synthesize it).
                    MapObject? obj = _solidObjects[_elevation].FirstOrDefault(o =>
                        o.HexTile == fromHex && Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead);
                    if (obj is null)
                        Console.WriteLine($"reg-anim-move: no critter at {fromHex}");
                    else
                    {
                        ExecuteRegAnim([new Formats.Int.RegAnimAction(
                            Formats.Int.RegAnimKind.MoveToTile, obj, toHex, null, 0, 0)]);
                        Console.WriteLine($"reg-anim-move: map={_currentMapName} "
                            + $"[{string.Join(", ", _regAnimMoves)}]");
                    }
                    break;
                }
                case StartupAction.CritterStateProbe(var csHex):
                {
                    // P34-M1: prove the two heartbeat externals read real state. is_in_combat via the
                    // wired provider; critter_state via ScriptHost.CritterStateOf (the VM's source of truth).
                    int inCombat = _scriptHost?.CombatActiveProvider?.Invoke() == true ? 1 : 0;
                    MapObject? c = csHex < 0
                        ? null
                        : CritterAt(csHex);
                    int state = csHex < 0 ? -1 : Formats.Int.ScriptHost.CritterStateOf(c);
                    Console.WriteLine($"critter-state: inCombat={inCombat} hex={csHex} state={state}");
                    break;
                }
                case StartupAction.HurtTooMuchProbe(var htmHex, var htmFlags):
                {
                    // P34-M2: set a crip/blind bit on a real critter, then report whether its AI
                    // packet's hurt_too_much mask now triggers the flee gate (combat_ai.cc:3076).
                    MapObject? htmC = _solidObjects[_elevation].FirstOrDefault(o =>
                        o.HexTile == htmHex && Fid.Type(o.Fid) is ObjectType.Critter);
                    if (htmC is null)
                    {
                        Console.WriteLine($"hurt-too-much: no critter at {htmHex}");
                        break;
                    }
                    htmC.CombatResults |= htmFlags;
                    int packetHurt = GetAiPacket(htmC)?.HurtTooMuch ?? 0;
                    int wouldFlee = (htmC.CombatResults & packetHurt) != 0 ? 1 : 0;
                    Console.WriteLine($"hurt-too-much: pid=0x{htmC.Pid:X} hex={htmHex} "
                        + $"results=0x{htmC.CombatResults:X} packetHurt=0x{packetHurt:X} wouldFlee={wouldFlee}");
                    break;
                }
                case StartupAction.RunProbe:
                {
                    // P34-M3: report the dude's run-guard decision under each condition (pure, zero-RNG).
                    MapObject? rpDude = _dude?.Dude;
                    if (rpDude is null)
                    {
                        Console.WriteLine("run-probe: no dude");
                        break;
                    }
                    int runFid = Fid.Build(ObjectType.Critter, Fid.Index(rpDude.Fid),
                        Formats.Combat.RunGuard.AnimRunning, Fid.WeaponCode(rpDude.Fid));
                    bool art = _vfs.Exists(_artIndex.GetFrmPath(runFid));
                    int def = Formats.Combat.RunGuard.MovementAnimCode(0, false, false, art);
                    int crippled = Formats.Combat.RunGuard.MovementAnimCode(Formats.Combat.CriticalTables.DamCripLegLeft, false, false, art);
                    int sneaking = Formats.Combat.RunGuard.MovementAnimCode(0, true, false, art);
                    int silentRun = Formats.Combat.RunGuard.MovementAnimCode(0, true, true, art);
                    Console.WriteLine($"run-probe: default={def} crippled={crippled} sneaking={sneaking} "
                        + $"silentRun={silentRun} artExists={(art ? 1 : 0)}");
                    break;
                }
                case StartupAction.OutlineProbe(var fightHex) when _dude is not null:
                {
                    // P34-M4: position the dude adjacent to the target (zero RNG, no combat entry) and
                    // classify every living critter's combat-outline type. CombatOutlineType is phase-
                    // independent, so no need to actually enter combat.
                    if (_solidObjects[_elevation].Any(o => o.HexTile == fightHex && Fid.Type(o.Fid) is ObjectType.Critter))
                    {
                        _dude.Dude.HexTile = Formats.Hex.HexGrid.TileInDirection(fightHex, 3); // adjacent, like --fight
                        RebuildBlockedTiles(_dude.Dude);
                    }
                    foreach (MapObject oc in _solidObjects[_elevation]
                        .Where(o => Fid.Type(o.Fid) is ObjectType.Critter && o != _dude.Dude && !o.IsDead)
                        .OrderBy(o => o.HexTile))
                    {
                        string type = CombatOutlineType(oc).ToString().ToLowerInvariant();
                        Console.WriteLine($"outline: hex={oc.HexTile} team={oc.Team} type={type}");
                    }
                    break;
                }
                case StartupAction.RepTitle(var repValue):
                {
                    // P31 B-M1: the generic-reputation title MESSAGE ID for a value (never the copyrighted
                    // string); -1 = below every threshold.
                    Console.WriteLine($"rep-title: value={repValue} msg={Formats.Map.GenericReputation.TitleFor(repValue, GenrepTable())}");
                    break;
                }
                case StartupAction.TownRep(var townValue):
                {
                    // P31 B-M2: the town-reputation standing band for a value.
                    Formats.Map.TownRepLevel level = Formats.Map.TownReputation.LevelFor(townValue);
                    Console.WriteLine($"town-rep: value={townValue} level={level} msg={Formats.Map.TownReputation.MessageId(level)}");
                    break;
                }
                case StartupAction.KarmaTitlesProbe:
                {
                    // P31 B-M2: the earned karma titles — rows whose GVAR is non-zero (message IDs only).
                    int Gv(int g) => _scriptHost?.GlobalVars.GetValueOrDefault(g, 0) ?? 0;
                    var active = Formats.Map.KarmaTitles.Active(KarmavarTable(), Gv).ToList();
                    Console.WriteLine($"karma-titles: active={active.Count} ids=[{string.Join(",", active.Select(t => t.NameMessageId))}]");
                    break;
                }
                case StartupAction.SetKarma(var setK, var setR):
                {
                    // P31 B-M3: god-mode set of the karma/reputation PC-stats, with the engine's pcSetStat
                    // clamps (karma >= 0, reputation -20..20). The displayed reputation TITLE is driven by
                    // GVAR_PLAYER_REPUTATION (GlobalVars[0]) — set that via --set-global 0 <n>.
                    _dudeKarma = Math.Max(0, setK);
                    _dudeReputation = Math.Clamp(setR, -20, 20);
                    Console.WriteLine($"set-karma: karma={_dudeKarma} rep={_dudeReputation}");
                    break;
                }
                case StartupAction.FogProbe(var fx, var fy, var fa):
                {
                    // Phase-22: drive a real travel leg WITH the fog and report the reveal.
                    // Drains TravelLeg.Step() directly (not TravelTo, so no transient-map load),
                    // ignoring encounter outcomes so the WHOLE corridor to the destination is
                    // mapped — proves subtiles flip to VISITED/KNOWN along the Bresenham path and
                    // the destination subtile becomes clear. Deterministic (the fog draws no RNG;
                    // the encounter rolls still advance the same seeded stream each run).
                    WorldArea? fdest = _cities.Areas.FirstOrDefault(a => a.Index == fa);
                    if (fdest is null) { Console.Error.WriteLine($"fog-probe: no area {fa}"); break; }
                    _wmRng ??= new Formats.Combat.SystemCombatRng(RngSeed ?? Environment.TickCount);
                    int getGlobalF(int g) => _scriptHost?.GlobalVars.GetValueOrDefault(g, 0) ?? 0;
                    var fleg = new Formats.Map.TravelLeg(Worldmap, _cities.Areas, _mapList,
                        fx, fy, fdest.WorldX, fdest.WorldY, _clock.Ticks, _wmRng, getGlobalF,
                        _dudeLevel, 5, 0, Difficulty, WorldFog);
                    int startState = WorldFog.StateAt(fx, fy);
                    Formats.Map.TravelStep fs;
                    int legSteps = 0, encounters = 0;
                    do { fs = fleg.Step(); legSteps++; if (fs.Encounter is not null) encounters++; } while (!fs.Arrived && legSteps < 5000);
                    Console.WriteLine($"worldmap-fog: start=({fx},{fy}) startState={startState} steps={legSteps} encounters={encounters}"
                        + $" arrived=({fs.X},{fs.Y}) arrivedState={WorldFog.StateAt(fs.X, fs.Y)}"
                        + $" visited={WorldFog.CountState(Formats.Map.WorldmapFog.Visited)}"
                        + $" known={WorldFog.CountState(Formats.Map.WorldmapFog.Known)}");
                    break;
                }
                case StartupAction.ProjectileCheck(var projHex):
                {
                    // Phase-10 #11: ready a spear, stand 5 hexes off, throw — and report
                    // the launched projectile (deterministic; the screen-lerp is visual).
                    if (_dude is null)
                        break;
                    if (RebuildObject(7, 1) is { } spear)
                    {
                        AddToDudeInventory(spear);
                        int idx = _dudeInventory.FindIndex(i => i.Pid == 7);
                        if (idx >= 0)
                            UseInventoryItem(idx); // ready it in hand
                    }
                    _dude.Dude.HexTile = Formats.Hex.HexGrid.TileInDirection(projHex, 0, 5);
                    _projectiles.Clear();
                    _combat.TryThrow(projHex);
                    Projectile? p = _projectiles.FirstOrDefault();
                    Console.WriteLine($"projectile: launched={_projectiles.Count}"
                        + $" fid={(p is null ? "none" : $"0x{p.Fid:X8}")}"
                        + $" dist={(p is null ? 0 : Formats.Hex.HexGrid.Distance(p.FromTile, p.ToTile))} dur={p?.DurationMs ?? 0}");
                    _combat.Reset();
                    break;
                }
                case StartupAction.LoadTransient(var tmap):
                {
                    // Phase-10 M0 guard check: load a transient (saved=No) map twice
                    // and assert it stays first-run with NO delta slot (regenerated,
                    // never remembered). The full two-reentry integration test lands
                    // in M3 when real encounter maps spawn groups.
                    LoadMap(tmap, null, transient: true);
                    bool firstRun1 = _scriptHost?.IsFirstRun(_map) ?? true;
                    LoadMap(tmap, null, transient: true); // re-enter: exit-write guard must skip
                    bool firstRun2 = _scriptHost?.IsFirstRun(_map) ?? true;
                    bool deltaSlot = _visitedMaps.ContainsKey(_map.Header.Name);
                    Console.WriteLine($"transient-load: {tmap} firstRun1={firstRun1} firstRun2={firstRun2} deltaSlot={deltaSlot}");
                    break;
                }
                case StartupAction.Throw(var throwHex):
                {
                    if (_dude is not null) // teleport into throw range (test plumbing)
                        _dude.Dude.HexTile = Formats.Hex.HexGrid.TileInDirection(throwHex, 3);
                    _combat.TryThrow(throwHex);
                    for (int guard = 0; guard < 3000 && _combat.IsResolving; guard++)
                    {
                        _animator.Update(10);
                        _combat.ProcessAnimations();
                    }
                    _combat.Reset();
                    Console.WriteLine($"throw-result: hex={throwHex}");
                    break;
                }
                case StartupAction.HudClick(var hudName):
                {
                    HudButton btn = HudButtons().FirstOrDefault(b => b.Name == hudName);
                    if (btn.Name is null)
                    {
                        Console.Error.WriteLine($"hud-click: no button {hudName}");
                        break;
                    }
                    btn.OnClick();
                    Console.WriteLine($"hud-click: {hudName} -> inv={_inventoryOpen} skills={_skillAllocOpen} worldmap={_worldmapOpen} skilldex={_skilldexOpen} pipboy={_pipboyOpen} options={_optionsOpen}");
                    break;
                }
                case StartupAction.PanelClick(var pcSide, var pcRow):
                {
                    // Click the centre of a row rect via the same geometry + dispatch a live
                    // mouse uses (TryClickItemPanel) — proves a click == its number key.
                    int px = pcSide == 0 ? 40 : 420;
                    Rectangle rect = ItemRowRect(px, pcRow);
                    bool consumed = TryClickItemPanel(rect.Center.X, rect.Center.Y, shift: false);
                    MapObject? inHand = _dudeInventory.FirstOrDefault(i => i.IsInHand);
                    Console.WriteLine($"panel-click: side={(pcSide == 0 ? "L" : "R")} row={pcRow}"
                        + $" consumed={consumed} inv={_dudeInventory.Count}"
                        + $" inHand={(inHand is null ? "none" : ObjectName(inHand))}");
                    break;
                }
                case StartupAction.MenuClick(var menu, var mrow):
                {
                    // Compute the row's centre, hit-test it back (hit must == mrow), then
                    // dispatch — proves the Options/Pip-Boy row geometry + action mapping.
                    string state;
                    int hit;
                    if (menu == "options")
                    {
                        _optionsOpen = true;
                        Point c = OptionsRowRect(mrow).Center;
                        hit = OptionsRowAt(c.X, c.Y);
                        if (hit == 4) _optionsOpen = false; // Resume — the side-effect-free row
                        state = $"options={_optionsOpen}";
                    }
                    else
                    {
                        _pipboyOpen = true;
                        _pipboyRestMenu = menu == "pipboy-rest";
                        Point c = PipboyRowRect(mrow).Center;
                        hit = PipboyRowAt(c.X, c.Y);
                        if (hit >= 0)
                            PipboyRows()[hit].OnClick();
                        state = $"pipboy={_pipboyOpen} rest={_pipboyRestMenu} automap={_automapOpen}";
                    }
                    Console.WriteLine($"menu-click: menu={menu} row={mrow} hit={hit} -> {state}");
                    break;
                }
                case StartupAction.UseSkill(var useSkill, var skillHex):
                {
                    // Arm <skill> and apply it to the object at <hex> (self when hex<0):
                    // exercises the same TryUseSkillOn path the Skilldex picker drives.
                    MapObject? skillTarget = skillHex < 0
                        ? _dude?.Dude
                        : _solidObjects[_elevation].FirstOrDefault(o => o.HexTile == skillHex)
                          ?? _flatObjects[_elevation].FirstOrDefault(o => o.HexTile == skillHex);
                    if (skillTarget is null)
                    {
                        Console.Error.WriteLine($"use-skill: nothing at hex {skillHex}");
                        break;
                    }
                    if (skillHex >= 0)
                    {
                        _camera.SetCenter(skillHex);
                        if (_dude is not null) // teleport adjacent so range checks pass (test plumbing)
                            _dude.Dude.HexTile = Formats.Hex.HexGrid.TileInDirection(skillHex, 3);
                    }
                    TryUseSkillOn(useSkill, skillTarget);
                    Console.WriteLine($"use-skill: skill={useSkill} target={ObjectName(skillTarget)} "
                        + $"hp={skillTarget.CurrentHp} locked={skillTarget.IsLockedState} sneak={_sneak.FlagSet}");
                    break;
                }
                case StartupAction.RestFor(var restMinutes):
                {
                    // Drive a Pip-Boy rest option (positive minutes / -1 healed / -2,-3
                    // until morning,evening); RestForMinutes/RestToHeal print the state.
                    DoRest(restMinutes);
                    break;
                }
                case StartupAction.LightProbe:
                    // Phase-21: report the ambient AFTER map_enter ran — proves the map's
                    // scripted set_light_level took effect (artemple sets 100 -> max + pinned).
                    Console.WriteLine($"light: map={_currentMapName} ambient={_lightGrid.Ambient}"
                        + $"/{Formats.Light.LightGrid.IntensityMax} fixed={AmbientFixed}");
                    break;
                case StartupAction.RegAnimProbe:
                    // Phase-21: the reg_anim_animate_forever registrations from map_enter.
                    Console.WriteLine($"reg-anim: map={_currentMapName} forever={_regAnimForever.Count}"
                        + $" [{string.Join(", ", _regAnimForever)}]");
                    break;
                case StartupAction.RevealAt reveal:
                    RevealAround(reveal.Hex); // P71: simulate the dude exploring this tile
                    Console.WriteLine($"reveal: hex={reveal.Hex} tiles={_seenTiles.Count}");
                    break;
                case StartupAction.TauntProbe tp:
                {
                    // P72-M3: state-only — the critter's taunt config (chance/color/ranges) + the
                    // deterministic attack/run message-id picks (combatai.msg ids, NOT the text).
                    MapObject? c = CritterAt(tp.Hex);
                    Formats.Combat.AiPacket? pkt = c is not null ? GetAiPacket(c) : null;
                    if (pkt is null)
                    {
                        Console.WriteLine($"taunt: hex={tp.Hex} packet=none");
                        break;
                    }
                    var rng = new Formats.Combat.SystemCombatRng(tp.Seed);
                    int atk = Formats.Combat.CombatTaunt.Pick(pkt, Formats.Combat.CombatTaunt.Type.Attack, rng);
                    int run = Formats.Combat.CombatTaunt.Pick(pkt, Formats.Combat.CombatTaunt.Type.Run, rng);
                    Console.WriteLine($"taunt: hex={tp.Hex} packet={pkt.PacketNum} chance={pkt.Chance}"
                        + $" color={pkt.TauntColor} attack=[{pkt.AttackStart},{pkt.AttackEnd}]"
                        + $" run=[{pkt.RunStart},{pkt.RunEnd}] atkMsg={atk} runMsg={run}");
                    break;
                }
                case StartupAction.OpenAutomap:
                {
                    _automapOpen = true;
                    // Deterministic census of the dots the automap plots — now gated by the
                    // OBJECT_SEEN fog (P20-M2), so it counts what the dude has actually seen.
                    var live = _flatObjects[_elevation].Concat(_solidObjects[_elevation]).ToList();
                    int Count(ObjectType t) => live.Count(o => _seenTiles.Contains(o.HexTile)
                        && Fid.Type(o.Fid) == t && !(t == ObjectType.Critter && o.IsDead));
                    int totalPlottable = live.Count(o => AutomapColor(o) is not null);
                    int seenPlottable = live.Count(o => _seenTiles.Contains(o.HexTile) && AutomapColor(o) is not null);
                    Console.WriteLine($"automap: map={_currentMapName} elev={_elevation}"
                        + $" walls={Count(ObjectType.Wall)} scenery={Count(ObjectType.Scenery)}"
                        + $" critters={Count(ObjectType.Critter)} items={Count(ObjectType.Item)}"
                        + $" misc={Count(ObjectType.Misc)} seen={seenPlottable}/{totalPlottable}"
                        + $" tiles={_seenTiles.Count} dude={_dude?.Dude.HexTile ?? -1}");
                    break;
                }
                case StartupAction.PartyCount:
                {
                    int members = _scriptHost is null ? 0
                        : Formats.Int.ScriptHost.PartyMemberCount(_scriptHost.PartyMembers);
                    int caps = _dudeInventory.Where(i => i.Pid == 41).Sum(i => Math.Max(i.StackCount, 1));
                    // Each member as Name(curHp/maxHp) — the maxHp reflects a levelled
                    // companion's stage proto (via the stat override), so this line
                    // proves the level-up state survives a save/load round-trip (#10 M3).
                    string names = _scriptHost is null ? ""
                        : string.Join(",", _scriptHost.PartyMembers.Select(m =>
                            GetCritterState(m) is { } cs ? $"{ObjectName(m)}({cs.CurrentHp}/{cs.MaxHp})" : ObjectName(m)));
                    Console.WriteLine($"party-count: members={members} caps={caps} [{names}]");
                    break;
                }
                case StartupAction.SetGlobal(var gid, var gval):
                    if (_scriptHost is not null)
                    {
                        _scriptHost.GlobalVars[gid] = gval;
                        Console.WriteLine($"set-global: GVAR{gid} = {gval}");
                    }
                    break;
                case StartupAction.TalkChoose(var tcHex, var tcChoices):
                {
                    MapObject? npc = CritterAt(tcHex);
                    if (npc is null)
                    {
                        Console.Error.WriteLine($"talk-seq: no critter at hex {tcHex}");
                        break;
                    }
                    _camera.SetCenter(tcHex);
                    Console.WriteLine($"talk-seq: {ObjectName(npc)}@{tcHex}");
                    TalkTo(npc);
                    if (_dialog is not null)
                        PrintDialogRound();
                    foreach (int choice in tcChoices)
                    {
                        if (_dialog is null)
                            break;
                        Console.WriteLine($"CHOOSE: {choice}");
                        ChooseDialogOption(choice - 1);
                    }
                    // Close any lingering dialog so the next talk-seq starts clean.
                    _dialog = null;
                    break;
                }
                case StartupAction.BurstAt(var fromHex, var atHex):
                {
                    // Phase-20 M4: burst at <atHex> from an EXPLICIT dude tile <fromHex> so
                    // the cone can be aimed to sweep a real bystander (collateral). Reports
                    // any burst-extra: lines the spray produced.
                    MapObject? bt = CritterAt(atHex, aliveOnly: true);
                    if (bt is null || _dude is null) { Console.Error.WriteLine($"burst-at: no critter at {atHex}"); break; }
                    _dude.Dude.HexTile = fromHex;
                    RebuildBlockedTiles(_dude.Dude);
                    _camera.SetCenter(atHex);
                    _combat.TryBurst(bt);
                    for (int guard = 0; guard < 3000 && _combat.IsResolving; guard++) { _animator.Update(10); _combat.ProcessAnimations(); }
                    _combat.Reset();
                    Console.WriteLine($"burst-at: from={fromHex} target@{atHex} hp={bt.CurrentHp} dead={bt.IsDead}");
                    break;
                }
                case StartupAction.Burst(var burstHex):
                {
                    MapObject? target = CritterAt(burstHex);
                    if (target is null)
                    {
                        Console.Error.WriteLine($"no critter at hex {burstHex}");
                        break;
                    }

                    _camera.SetCenter(burstHex);
                    if (_dude is not null) // teleport adjacent (test plumbing, like --attack)
                        _dude.Dude.HexTile = Formats.Hex.HexGrid.TileInDirection(burstHex, 3);
                    _combat.TryBurst(target);

                    for (int guard = 0; guard < 3000 && _combat.IsResolving; guard++)
                    {
                        _animator.Update(10);
                        _combat.ProcessAnimations();
                    }

                    _combat.Reset();
                    Console.WriteLine($"burst-result: hp={target.CurrentHp} dead={target.IsDead}");
                    break;
                }
                case StartupAction.Explode(var explodeHex):
                {
                    // Frag-grenade payload (20-35, radius 3) at a hex — test hook for
                    // the M3 AoE; M4 throwing will trigger it from a thrown weapon.
                    _combat.Explode(explodeHex, _dude?.Dude, minDamage: 20, maxDamage: 35, radius: 3);
                    for (int guard = 0; guard < 3000 && _combat.IsResolving; guard++)
                    {
                        _animator.Update(10);
                        _combat.ProcessAnimations();
                    }
                    _combat.Reset();
                    Console.WriteLine($"explode-result: hex={explodeHex}");
                    break;
                }
                case StartupAction.Attack(var attackHex):
                {
                    MapObject? target = CritterAt(attackHex);
                    if (target is null)
                    {
                        Console.Error.WriteLine($"no critter at hex {attackHex}");
                        break;
                    }

                    _camera.SetCenter(attackHex);
                    if (_dude is not null) // teleport adjacent (test plumbing, like use-hex)
                        _dude.Dude.HexTile = Formats.Hex.HexGrid.TileInDirection(attackHex, 3);
                    _combat.TryAttack(target, AimLocation);

                    // Run the choreography to completion so transcripts and
                    // follow-up actions see the resolved world.
                    for (int guard = 0; guard < 3000 && _combat.IsResolving; guard++)
                    {
                        _animator.Update(10);
                        _combat.ProcessAnimations();
                    }

                    // --attack is a free-swing test primitive; --fight runs
                    // the real turn loop with retaliation.
                    _combat.Reset();
                    Console.WriteLine($"attack-result: hp={target.CurrentHp} dead={target.IsDead}");
                    break;
                }
                case StartupAction.Fight(var fightHex):
                {
                    MapObject? target = CritterAt(fightHex);
                    if (target is null)
                    {
                        Console.Error.WriteLine($"no critter at hex {fightHex}");
                        break;
                    }

                    _camera.SetCenter(fightHex);
                    if (_dude is not null)
                        _dude.Dude.HexTile = Formats.Hex.HexGrid.TileInDirection(fightHex, 3);

                    // Autoplay: punch any adjacent hostile while AP lasts,
                    // end turn, let the AI move — until someone wins.
                    for (int guard = 0; guard < 200_000 && !_combat.IsGameOver; guard++)
                    {
                        bool animating = _combat.IsBusy;
                        if (!animating)
                        {
                            if (_combat.Phase == Formats.Combat.CombatPhase.Idle)
                            {
                                if (target.IsDead || _dude is null)
                                    break;
                                _combat.TryAttack(target, AimLocation);
                                if (!_combat.HasPendingAttack && _combat.Phase == Formats.Combat.CombatPhase.Idle)
                                    break; // could not engage
                            }
                            else if (_combat.Phase == Formats.Combat.CombatPhase.PlayerTurn)
                            {
                                // Heal when hurt, then swing at anything in
                                // reach, then end the turn.
                                int stimpak = _dude is { } d
                                    && d.Dude.CurrentHp <= Math.Max(20, (GetCritterState(d.Dude)?.MaxHp ?? 30) * 2 / 3)
                                    ? _dudeInventory.FindIndex(i => i.Pid == 40)
                                    : -1;
                                (ProtoInfo? fightWeapon, _) = EquippedWeapon(_dude!.Dude);
                                bool fightGun = fightWeapon?.Weapon is { } fw && fw.IsGun(fightWeapon.ExtendedFlags);
                                int reach = fightGun ? fightWeapon!.Weapon!.MaxRange1
                                    : Math.Min(fightWeapon?.Weapon?.MaxRange1 ?? 1, 2);
                                int swingCost = fightWeapon?.Weapon?.ApCost ?? Formats.Combat.CombatMath.PunchApCost;
                                MapObject? victim = _combat.Hostiles.FirstOrDefault(h => !h.IsDead
                                    && Formats.Hex.HexGrid.Distance(_dude!.Dude.HexTile, h.HexTile) <= reach);
                                if (stimpak >= 0 && _combat.DudeAp >= 2)
                                    UseInventoryItem(stimpak);
                                else if (victim is not null && _combat.DudeAp >= swingCost)
                                    _combat.TryAttack(victim, AimLocation);
                                else
                                    _combat.EndPlayerTurn();
                            }
                        }

                        _animator.Update(10);
                        _combat.Step();
                        foreach (DudeController walker in _npcWalkers.Values)
                            walker.Update(10);
                    }

                    Console.WriteLine($"fight-result: rounds={_combat.Round} dudeHp={_dude?.Dude.CurrentHp}"
                        + $" gameOver={_combat.IsGameOver} targetDead={target.IsDead}"
                        + $" hostilesLeft={_combat.Hostiles.Count(h => !h.IsDead)}");
                    break;
                }
                case StartupAction.Give(var givePid, var giveCount):
                    if (RebuildObject(givePid, giveCount) is { } given)
                    {
                        AddToDudeInventory(given);
                        Console.WriteLine($"give: {ObjectName(given)} x{giveCount}");
                    }
                    break;
                case StartupAction.UseItemByPid(var usePid):
                {
                    int index = _dudeInventory.FindIndex(i => i.Pid == usePid);
                    if (index >= 0)
                        UseInventoryItem(index);
                    else
                        Console.Error.WriteLine($"use-item: pid 0x{usePid:X8} not in bag");
                    break;
                }
                case StartupAction.Recruit(var recruitHex):
                {
                    MapObject? critter = _solidObjects[_elevation].FirstOrDefault(o =>
                        o.HexTile == recruitHex && Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead);
                    if (critter is null || _scriptHost is null)
                    {
                        Console.Error.WriteLine($"recruit: no critter at {recruitHex}");
                        break;
                    }
                    _scriptHost.PartyMembers.Add(critter);
                    OnPartyChanged(critter, joined: true);
                    break;
                }
                case StartupAction.CompanionLifecycle(var compHex):
                {
                    MapObject? m = _solidObjects[_elevation].FirstOrDefault(o =>
                        o.HexTile == compHex && Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead);
                    if (m is null || _scriptHost is null)
                    {
                        Console.Error.WriteLine($"companion: no critter at {compHex}");
                        break;
                    }

                    int Count() => Formats.Int.ScriptHost.PartyMemberCount(_scriptHost.PartyMembers);
                    int HeartbeatEligible() => _solidObjects[_elevation].Count(IsHeartbeatEligible);
                    void Pick(CompanionCmd cmd)
                    {
                        OpenCompanionHub(m);
                        ChooseCompanionOption(_hubOptions.FindIndex(o => o.Cmd == cmd));
                    }

                    int originalTeam = m.Team;
                    _scriptHost.PartyMembers.Add(m);
                    OnPartyChanged(m, joined: true);
                    Console.WriteLine($"companion-lifecycle: recruited partyCount={Count()} heartbeatEligible={HeartbeatEligible()}");

                    Pick(CompanionCmd.Wait);
                    Console.WriteLine($"  wait: heartbeatEligible={HeartbeatEligible()} (member held)");
                    Pick(CompanionCmd.Follow);
                    Console.WriteLine($"  follow: heartbeatEligible={HeartbeatEligible()}");
                    Pick(CompanionCmd.Dismiss);
                    // heartbeatEligible drops because the dismissed body stays on the map but
                    // with Sid=-1 — proving its critter_p_proc no longer runs (the engine's
                    // party_remove side effect; closes the partymbr "UNVERIFIED" research flag).
                    Console.WriteLine($"  dismiss: partyCount={Count()} team={m.Team} (orig {originalTeam}) sid={(m.Sid == -1 ? "none" : "bound")} heartbeatEligible={HeartbeatEligible()}");
                    Pick(CompanionCmd.Rejoin);
                    Console.WriteLine($"  rejoin: partyCount={Count()} sid={(m.Sid == -1 ? "none" : "bound")}");
                    break;
                }
                case StartupAction.TradeWith(var tradeHex, var tradePid):
                {
                    MapObject? tp = _solidObjects[_elevation].FirstOrDefault(o =>
                        o.HexTile == tradeHex && Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead);
                    if (tp is null || _scriptHost is null || RebuildObject(tradePid, 1) is not { } gift)
                    {
                        Console.Error.WriteLine($"trade: no critter at {tradeHex} or bad pid 0x{tradePid:X8}");
                        break;
                    }
                    _scriptHost.PartyMembers.Add(tp);
                    OnPartyChanged(tp, joined: true);
                    AddToDudeInventory(gift);

                    int capsBefore = DudeCaps();
                    int dudeBefore = _dudeInventory.Count, theirsBefore = tp.Inventory.Count;
                    OpenTrade(tp);

                    GiveToFollower(_dudeInventory.FindIndex(i => i.Pid == tradePid));
                    Console.WriteLine($"trade: gave 0x{tradePid:X8} -> yours={_dudeInventory.Count} theirs={tp.Inventory.Count} (dudeHas={_dudeInventory.Any(i => i.Pid == tradePid)} theyHave={tp.Inventory.Any(i => i.Pid == tradePid)})");

                    TakeFromContainer(tp.Inventory.FindIndex(i => i.Pid == tradePid));
                    MapObject? back = _dudeInventory.FirstOrDefault(i => i.Pid == tradePid);
                    Console.WriteLine($"trade: took it back -> yours={_dudeInventory.Count} theirs={tp.Inventory.Count} (dudeHas={back is not null} stack={back?.StackCount} flags=0x{back?.Flags ?? 0:X})");
                    Console.WriteLine($"trade: caps {capsBefore}->{DudeCaps()} (flat, unchanged={capsBefore == DudeCaps()})"
                        + $" backToStart={_dudeInventory.Count == dudeBefore && tp.Inventory.Count == theirsBefore && back?.StackCount == 1 && (back?.Flags ?? 0) == 0}");
                    break;
                }
                case StartupAction.CompanionPersist(var persistHex):
                {
                    MapObject? m = _solidObjects[_elevation].FirstOrDefault(o =>
                        o.HexTile == persistHex && Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead);
                    if (m is null || _scriptHost is null)
                    {
                        Console.Error.WriteLine($"companion-persist: no critter at {persistHex}");
                        break;
                    }
                    int origTeam = m.Team;
                    _scriptHost.PartyMembers.Add(m);
                    OnPartyChanged(m, joined: true);
                    _waitingCompanions.Add(m); // "wait here"
                    Console.WriteLine($"companion-persist: pre-save waiting=true origTeam={origTeam}");

                    // Round-trip through save/load in-process via a temp slot.
                    string realPath = SavePath;
                    SavePath = Path.Combine(Path.GetTempPath(), "hexwaste-persist-test.json");
                    SaveGame();
                    LoadGame();
                    if (File.Exists(SavePath))
                        File.Delete(SavePath);
                    SavePath = realPath;

                    MapObject? r = _scriptHost.PartyMembers.FirstOrDefault(p => p.Pid == m.Pid);
                    bool waiting = r is not null && _waitingCompanions.Contains(r);
                    Console.WriteLine($"companion-persist: reloaded inParty={r is not null} waiting={waiting}");
                    if (r is not null)
                    {
                        DismissCompanion(r);
                        Console.WriteLine($"companion-persist: after-dismiss team={r.Team} (orig {origTeam}, restored={r.Team == origTeam})");
                    }
                    break;
                }
                case StartupAction.DismissPersist(var dpHex):
                {
                    MapObject? m = _solidObjects[_elevation].FirstOrDefault(o =>
                        o.HexTile == dpHex && Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead);
                    if (m is null || _scriptHost is null)
                    {
                        Console.Error.WriteLine($"dismiss-persist: no critter at {dpHex}");
                        break;
                    }
                    int pid = m.Pid;
                    int baseline = _solidObjects[_elevation].Count(o => o.Pid == pid && Fid.Type(o.Fid) is ObjectType.Critter);
                    _scriptHost.PartyMembers.Add(m);
                    OnPartyChanged(m, joined: true);
                    DismissCompanion(m); // body now stands on this map, in _dismissedCompanions
                    Console.WriteLine($"dismiss-persist: pre-save dismissedOnMap={_dismissedCompanions.Count} baselineOfPid={baseline}");

                    string realPath = SavePath;
                    SavePath = Path.Combine(Path.GetTempPath(), "hexwaste-dismiss-test.json");
                    SaveGame();
                    LoadGame();
                    if (File.Exists(SavePath))
                        File.Delete(SavePath);
                    SavePath = realPath;

                    // After reload: exactly one body on the map (pristine copy was taken),
                    // not in the party, rejoinable.
                    int onMap = _solidObjects[_elevation].Count(o => o.Pid == pid && Fid.Type(o.Fid) is ObjectType.Critter);
                    MapObject? body = _solidObjects[_elevation].FirstOrDefault(o => o.Pid == pid && _dismissedCompanions.ContainsKey(o));
                    Console.WriteLine($"dismiss-persist: reloaded noDuplicate={onMap == baseline} rejoinable={body is not null} inParty={_scriptHost.PartyMembers.Any(p => p.Pid == pid)}");
                    if (body is not null)
                    {
                        RejoinCompanion(body);
                        Console.WriteLine($"dismiss-persist: after-rejoin inParty={_scriptHost.PartyMembers.Any(p => p.Pid == pid)} dismissedOnMap={_dismissedCompanions.Count}");
                    }
                    break;
                }
                case StartupAction.UseOn(var usePid2, var useHex2):
                {
                    MapObject? item = _dudeInventory.FirstOrDefault(i => i.Pid == usePid2);
                    MapObject? target = _solidObjects[_elevation].FirstOrDefault(o => o.HexTile == useHex2)
                        ?? _flatObjects[_elevation].FirstOrDefault(o => o.HexTile == useHex2);
                    if (item is null || target is null)
                        Console.Error.WriteLine($"use-on: item 0x{usePid2:X8} or target @{useHex2} missing");
                    else
                        UseItemOn(item, target);
                    break;
                }
                case StartupAction.Buy(var buyPid):
                {
                    int index = BarterStock().FindIndex(i => i.Pid == buyPid);
                    if (index >= 0)
                        BarterBuy(index);
                    else
                        Console.Error.WriteLine($"buy: pid 0x{buyPid:X8} not in stock (barter open: {_barterNpc is not null})");
                    break;
                }
                case StartupAction.Sell(var sellPid):
                {
                    int index = BarterGoods().FindIndex(i => i.Pid == sellPid);
                    if (index >= 0)
                        BarterSell(index);
                    else
                        Console.Error.WriteLine($"sell: pid 0x{sellPid:X8} not in bag (barter open: {_barterNpc is not null})");
                    break;
                }
                case StartupAction.EndBarter:
                    CloseBarter();
                    break;
                case StartupAction.TakeAll:
                    if (_lootContainer is not null)
                    {
                        TakeAllFromContainer();
                        Console.WriteLine($"take-all: bag now {_dudeInventory.Count} stacks");
                    }
                    break;
                case StartupAction.Transit(var mapFile, var tile, var elevation):
                    LoadMap(mapFile, tile >= 0 ? new MapDestination(0, tile, elevation, 0) : null);
                    Console.WriteLine($"transit: now on {_currentMapName} (elevation {_elevation})");
                    break;
                case StartupAction.SaveNow:
                    SaveGame();
                    break;
                case StartupAction.LoadNow:
                    LoadGame();
                    break;
                case StartupAction.SaveToSlot(var ssSlot):
                    SaveGameToSlot(ssSlot);
                    Console.WriteLine($"save-slot: slot={ssSlot} saved=1");
                    break;
                case StartupAction.LoadFromSlot(var lsSlot):
                {
                    bool occupied = SaveState.Load(SlotPath(lsSlot)) is not null;
                    if (occupied)
                        LoadGameFromSlot(lsSlot);
                    Console.WriteLine($"load-slot: slot={lsSlot} occupied={(occupied ? 1 : 0)}");
                    break;
                }
                case StartupAction.SlotsProbe:
                {
                    RefreshSlotInfos();
                    var sb = new System.Text.StringBuilder("slots:");
                    for (int s = 0; s < Formats.SaveSlots.Count; s++)
                    {
                        Formats.SlotInfo info = _slotInfos[s];
                        string st = !info.Occupied ? "empty" : info.VersionMismatch ? "old" : $"L{info.Level}";
                        sb.Append($" {s}={st}");
                    }
                    Console.WriteLine(sb.ToString());
                    break;
                }
                case StartupAction.ResetSlots:
                    for (int s = 0; s < Formats.SaveSlots.Count; s++)
                        if (File.Exists(SlotPath(s)))
                            File.Delete(SlotPath(s));
                    break;
                case StartupAction.ShowSaveLoad(var slMode):
                    OpenSaveLoad(slMode == 1 ? SaveLoadMode.Load : SaveLoadMode.Save);
                    break;
                case StartupAction.GrantXp(var amount):
                    AwardXp(amount);
                    break;
                case StartupAction.SpendSkill(var skill):
                    SpendSkillPoint(skill);
                    break;
                case StartupAction.OpenSkills:
                    _skillAllocOpen = _dudeGcd is not null;
                    break;
                case StartupAction.Rest:
                    RestToHeal();
                    break;
                case StartupAction.Hurt(var dmg) when _dude is not null:
                    _dude.Dude.CurrentHp = Math.Max(1, _dude.Dude.CurrentHp - dmg);
                    Console.WriteLine($"hurt: dude HP now {_dude.Dude.CurrentHp}");
                    break;
                case StartupAction.CreateCharacter(var special, var tags, var gender, var traits):
                    _dudeGcd = Formats.Combat.GcdFile.Create(special, tags, gender, traits);
                    _activeCharacter = "custom";
                    Console.WriteLine($"create: SPECIAL {string.Join("/", special)} gender {gender} tags [{string.Join(",", tags)}]"
                        + $" traits [{string.Join(",", traits)}] HP {_dudeGcd.Stats.BaseStats[7]} AP {_dudeGcd.Stats.BaseStats[8]}");
                    StartNewGame();
                    break;
                case StartupAction.ShowCreate(var step):
                    EnterCreation();
                    _menu = step switch
                    {
                        "traits" => MenuState.CreateTraits,
                        "tags" => MenuState.CreateTags,
                        _ => MenuState.CreateStats,
                    };
                    break;
                case StartupAction.ShowInventory:
                    _inventoryOpen = true;
                    _panelPage = 0;
                    PrewarmItemTextures(_dudeInventory);
                    break;
                case StartupAction.AdvanceDays(var days):
                    _clock.AdvanceHours(days * 24);
                    Console.WriteLine($"advance: now day {_clock.Day}");
                    break;
            }
        }

        if (ExamineAt is { } examinePoint)
        {
            MapObject? target = PickObject(examinePoint.X, examinePoint.Y);
            string text = target is null ? "nothing"
                : _scriptHost?.GetScriptedDescription(target, _map, _dude?.Dude) is { } scripted
                    ? $"{ObjectName(target)} — [script] {string.Join(" / ", scripted)}"
                    : $"{ObjectName(target)} — {ObjectDescription(target)}";
            Console.WriteLine($"examine@{examinePoint.X},{examinePoint.Y}: {text}");
            if (target is not null)
                Examine(target);
        }

        if (StartInWalkMode)
            ToggleWalkMode();
        if (ToggleDoorAtTile is { } doorTile)
        {
            MapObject? door = _solidObjects[_elevation].FirstOrDefault(o => o.HexTile == doorTile && IsDoor(o));
            if (door is not null)
                ToggleDoor(door);
            else
                Console.Error.WriteLine($"no door at hex {doorTile}");
        }

        if (WalkToTile is { } walkTarget && _dude is not null && !_dude.WalkTo(walkTarget))
            Console.Error.WriteLine($"no path to hex {walkTarget}");

        // Step cycling/animations in small increments so pre-advancing N ms
        // lands on the same state as N ms of real frames (screenshot testing).
        for (double advanced = 0; advanced < AdvanceCyclingMs; advanced += 10)
        {
            _cycler.Update(10);
            _animator.Update(10);
            _combat.Step();
            _dude?.Update(10);
            UpdateAmbientLife(10);
            UpdateClock(10);
            _scriptHost?.PumpTimers(10, _dude?.Dude);
            PumpCritterProcs(10);
            if (_pendingTransition is { } transition)
            {
                _pendingTransition = null;
                ApplyTransition(transition);
            }
        }
        _frmCache.OnPaletteChanged(_palette);

        // Headless transcript runs (--fight/--attack/… with no screenshot or
        // bench, which have their own exits in Draw) settle their actions here
        // and then have nothing left to do — exit cleanly so the determinism
        // harness gets a stdout + exit code without killing a hung window.
        if (StartupActions.Count > 0 && _screenshotPath is null && BenchFrames == 0)
            _exitAfterStartupActions = true;
    }
}
