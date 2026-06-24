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

// The ICombatHost implementation + combat glue the CombatEngine drives: weapon/ammo/reload/unload,
// death+corpse, NPC heal, the critter heartbeat, poison + sfx, animation/projectile/throw callbacks,
// the on-hit/dodge/getup/death-fall reactions, and the destroy/damage/combat script procs. Pure move
// from ViewerGame.cs (the kills/XP/party-level/skill/rest helpers between the two clusters stay in core).
public sealed partial class ViewerGame
{
    /// <summary>The critter's in-hand weapon proto + item; the dude's bag is
    /// the separate _dudeInventory list.</summary>
    public (ProtoInfo? Proto, MapObject? Item) EquippedWeapon(MapObject critter)
    {
        List<MapObject> bag = critter == _dude?.Dude ? _dudeInventory : critter.Inventory;
        foreach (MapObject item in bag.Where(i => i.IsInHand))
        {
            try
            {
                ProtoInfo proto = _protos.Get(item.Pid);
                if (proto.Weapon is not null)
                    return (proto, item);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
            }
        }

        return (null, null);
    }

    /// <summary>The critter's carried weapon items (proto + item) for the AI inventory weapon
    /// switch (_ai_search_inven_weap). Returns ALL weapons in the bag — the CombatEngine fold skips
    /// the one being replaced; a non-weapon or unknown proto is dropped. P43.</summary>
    public IReadOnlyList<(ProtoInfo Proto, MapObject Item)> CritterInventoryWeapons(MapObject critter)
    {
        List<MapObject> bag = critter == _dude?.Dude ? _dudeInventory : critter.Inventory;
        var result = new List<(ProtoInfo, MapObject)>();
        foreach (MapObject item in bag)
        {
            try
            {
                ProtoInfo proto = _protos.Get(item.Pid);
                if (proto.Weapon is not null)
                    result.Add((proto, item));
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
            }
        }
        return result;
    }

    /// <summary>Wield a carried weapon: clear every in-hand flag in the bag, then set the new item's
    /// right hand (_inven_wield HAND_RIGHT) so <see cref="EquippedWeapon"/> returns it. P43.</summary>
    public void EquipWeapon(MapObject critter, MapObject weaponItem)
    {
        List<MapObject> bag = critter == _dude?.Dude ? _dudeInventory : critter.Inventory;
        foreach (MapObject it in bag)
            it.Flags &= ~(MapObject.FlagInLeftHand | MapObject.FlagInRightHand);
        weaponItem.Flags |= MapObject.FlagInRightHand;
    }

    /// <summary>Loaded rounds; -1 sentinel hydrates from the proto capacity
    /// (fresh items, protoItemDataDefaults).</summary>
    public int WeaponAmmo(ProtoInfo weaponProto, MapObject item)
    {
        if (item.AmmoQuantity == -1)
            item.AmmoQuantity = weaponProto.Weapon?.AmmoCapacity ?? 0;
        return item.AmmoQuantity;
    }

    public AmmoProtoStats? LoadedAmmo(ProtoInfo weaponProto, MapObject item)
    {
        int pid = item.AmmoTypePid != -1 ? item.AmmoTypePid : weaponProto.Weapon?.AmmoTypePid ?? -1;
        if (pid <= 0)
            return null;
        try
        {
            return _protos.Get(pid).Ammo;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>_obj_shoot_blocking_at subset: walls/scenery/living critters on
    /// the tile, skipping hidden, NO_BLOCK (open doors) and SHOOT_THRU.</summary>
    public MapObject? ShootBlockerAt(int tile, MapObject shooter, MapObject target)
    {
        const int noBlock = 0x10;
        const uint shootThru = 0x80000000;
        return _solidObjects[_elevation].FirstOrDefault(o =>
            o.HexTile == tile && o != shooter && o != target && !o.IsHidden
            && (o.Flags & noBlock) == 0 && ((uint)o.Flags & shootThru) == 0
            && (Fid.Type(o.Fid) is ObjectType.Wall or ObjectType.Scenery
                || (Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead)));
    }

    /// <summary>Reload from a matching-caliber ammo item: partial fills, no
    /// mixed mags (item.cc weaponCanBeReloadedWith/weaponReload). The R key / AI
    /// auto-reload path — picks any matching box (preferred pid -1).</summary>
    public bool TryReload(MapObject holder, ProtoInfo weaponProto, MapObject weaponItem) =>
        TryReloadWith(holder, weaponProto, weaponItem, -1);

    /// <summary>Reload, optionally restricting to a SPECIFIC ammo pid (P40 — the player's ammo-type
    /// selection: "reload with THIS box"). preferredAmmoPid &lt; 0 = any matching box (the default
    /// auto-reload, unchanged → byte-identical). The no-mixed-mags rule still applies, so a type swap
    /// needs an empty weapon (unload first).</summary>
    public bool TryReloadWith(MapObject holder, ProtoInfo weaponProto, MapObject weaponItem, int preferredAmmoPid)
    {
        if (weaponProto.Weapon is not { } weapon || weapon.AmmoCapacity <= 0)
            return false;
        int current = WeaponAmmo(weaponProto, weaponItem);
        if (current >= weapon.AmmoCapacity)
            return false;

        List<MapObject> bag = holder == _dude?.Dude ? _dudeInventory : holder.Inventory;
        foreach (MapObject box in bag)
        {
            if (preferredAmmoPid >= 0 && box.Pid != preferredAmmoPid)
                continue; // P40: the player chose a specific ammo type

            ProtoInfo boxProto;
            try
            {
                boxProto = _protos.Get(box.Pid);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
                continue;
            }

            if (boxProto.Ammo is not { } ammo || ammo.Caliber != weapon.Caliber)
                continue;
            if (current > 0 && weaponItem.AmmoTypePid != -1 && weaponItem.AmmoTypePid != box.Pid)
                continue; // no mixed mags

            if (box.AmmoQuantity == -1)
                box.AmmoQuantity = ammo.Quantity;
            int moved = Math.Min(weapon.AmmoCapacity - current, box.AmmoQuantity);
            if (moved <= 0)
                continue;

            weaponItem.AmmoQuantity = current + moved;
            weaponItem.AmmoTypePid = box.Pid;
            box.AmmoQuantity -= moved;
            if (box.AmmoQuantity <= 0)
            {
                box.StackCount--;
                if (box.StackCount <= 0)
                    bag.Remove(box);
                else
                    box.AmmoQuantity = ammo.Quantity; // next box in the stack
            }

            Log(holder == _dude?.Dude
                ? $"You reload the {ObjectNameByPid(weaponProto.Pid)} ({weaponItem.AmmoQuantity}/{weapon.AmmoCapacity})."
                : $"The {ObjectName(holder)} reloads.");
            Console.WriteLine($"reload: {ObjectNameByPid(weaponProto.Pid)} -> {weaponItem.AmmoQuantity}/{weapon.AmmoCapacity}");
            // Weapon-ready sfx on a successful reload (the engine rings the weapon in, combat.cc) — P34-M5.
            if (weapon.SoundCode > 0)
                _audio?.PlaySfx(Formats.Sound.SfxName.WeaponName(Formats.Sound.SfxName.WeaponSoundEffect.Ready, weapon.SoundCode, primaryOrPunch: true));
            return true;
        }

        return false;
    }

    /// <summary>Eject the dude's equipped weapon's loaded ammo into a bag box and empty the weapon
    /// (P40; ported from item.cc weaponUnload :1880 — one box of min(loaded, boxCapacity) rounds, the
    /// remainder stays in the mag). Needed to SWITCH ammo type (the no-mixed-mags rule blocks loading a
    /// different type into a non-empty weapon). The ejected box is added discretely (a partial count
    /// must not merge into a full stack). Returns true if anything was ejected.</summary>
    private bool UnloadEquippedWeapon()
    {
        if (_dude is null)
            return false;
        (ProtoInfo? wp, MapObject? wi) = EquippedWeapon(_dude.Dude);
        if (wp?.Weapon is not { } weapon || wi is null)
        {
            Log("You have no weapon to unload.");
            return false;
        }
        int loaded = WeaponAmmo(wp, wi);
        int typePid = wi.AmmoTypePid != -1 ? wi.AmmoTypePid : weapon.AmmoTypePid;
        if (loaded <= 0 || typePid <= 0)
        {
            Log($"The {ObjectNameByPid(wp.Pid)} is already empty.");
            return false;
        }
        int boxCap = SafeProto(typePid)?.Ammo?.Quantity ?? loaded;
        int ejected = Math.Min(loaded, boxCap);
        if (RebuildObject(typePid, 1) is { } box)
        {
            box.AmmoQuantity = ejected;
            _dudeInventory.Add(box); // discrete — a partial box must not merge into a full stack
        }
        wi.AmmoQuantity = loaded - ejected;
        if (wi.AmmoQuantity == 0)
            wi.AmmoTypePid = -1;
        Log($"You unload the {ObjectNameByPid(wp.Pid)}.");
        Console.WriteLine($"unload: weapon={wp.Pid} ejected={ejected} type={typePid} left={wi.AmmoQuantity}");
        return true;
    }

    /// <summary>Attack art: the weapon's animation code goes into FID bits
    /// 12-15 and the attack anim comes from extendedFlags &amp; 0xF via
    /// item.cc _attack_anim[] (THRUST=41, SWING=42; fists punch at 16).</summary>
    private void StartAttackAnimation(MapObject attacker, ProtoInfo? weaponProto)
    {
        // item.cc:116 _attack_anim, indexed by extendedFlags & 0xF
        ReadOnlySpan<int> attackAnims = [0, 16, 17, 42, 41, 18, 45, 46, 47];
        const int animThrowPunch = 16;

        int anim = animThrowPunch;
        int weaponCode = 0;
        if (weaponProto?.Weapon is { } weapon)
        {
            int index = weaponProto.ExtendedFlags & 0xF;
            anim = index < attackAnims.Length ? attackAnims[index] : animThrowPunch;
            weaponCode = weapon.AnimationCode;
        }

        int fid = Fid.Build(ObjectType.Critter, Fid.Index(attacker.Fid), anim, weaponCode);
        if (!_vfs.Exists(_artIndex.GetFrmPath(fid)))
            fid = Fid.Build(ObjectType.Critter, Fid.Index(attacker.Fid), animThrowPunch, 0);
        if (_vfs.Exists(_artIndex.GetFrmPath(fid)))
            _animator.PlayActionOnce(attacker, fid);
    }

    public string ObjectNameByPid(int pid) =>
        _protoMessages.GetName(pid) ?? $"0x{pid:X8}";

    /// <summary>Resolve the death anim against the critter's art (ported from fallout2-ce
    /// src/actions.cc _check_death): the gore anim DeathAnims.Pick chose (P26) if its art ships,
    /// else FALL_BACK, else FALL_FRONT. The engine's hit-from-front flip is out of PoC scope.</summary>
    public int PickDeathAnim(MapObject critter, int desiredAnim = Formats.Combat.DeathAnims.FallBack)
    {
        const int animFallBack = 20, animFallFront = 21;
        bool Exists(int anim) =>
            _vfs.Exists(_artIndex.GetFrmPath(Fid.Build(ObjectType.Critter, Fid.Index(critter.Fid), anim, 0)));
        if (desiredAnim != animFallBack && desiredAnim != animFallFront && Exists(desiredAnim))
            return desiredAnim;
        return Exists(animFallBack) ? animFallBack : animFallFront;
    }

    /// <summary>ported from fallout2-ce src/critter.cc critterKill(): the
    /// corpse is the single-frame art at death anim + 28, NO_BLOCK, and drawn
    /// flat — which also makes the existing loot panel reachable.</summary>
    public void ConvertToCorpse(MapObject critter, int deathAnim)
    {
        _animator.Remove(critter);

        int corpseFid = Fid.Build(ObjectType.Critter, Fid.Index(critter.Fid), deathAnim + 28, 0);
        if (_vfs.Exists(_artIndex.GetFrmPath(corpseFid)))
            critter.Fid = corpseFid;

        critter.Flags |= 0x10; // OBJECT_NO_BLOCK
        critter.Flags |= 0x08; // flat: corpses draw under standing critters

        for (int elevation = 0; elevation < MapFile.ElevationCount; elevation++)
        {
            if (_solidObjects[elevation].Remove(critter) && !_flatObjects[elevation].Contains(critter))
                InsertSorted(_flatObjects[elevation], critter);
        }
        RebuildBlockedTiles(_dude?.Dude);
    }

    /// <summary>kill_critter_type (0x80EE): destroy every live critter of a proto
    /// type on the map. deathFrame==0 silently removes the body; nonzero kills it
    /// into a corpse (the gore ftList rotation is simplified to the art-resolved
    /// fall — cosmetic, and no slice map fires this after P56-M1's branch shift, so
    /// this is faithful forward-looking infra). The count>200 guard mirrors the
    /// engine's infinite-loop bail. Ported from interpreter_extra.cc opKillCritterType.</summary>
    private void KillCrittersByType(int pid, int deathFrame)
    {
        // Snapshot first — the draw lists mutate as we remove/convert.
        List<MapObject> victims = _solidObjects
            .SelectMany(list => list)
            .Where(o => o.Pid == pid && o != _dude?.Dude
                && Fid.Type(o.Fid) is ObjectType.Critter && !o.IsHidden && !o.IsDead)
            .Distinct()
            .Take(201) // count > 200 → the engine aborts as an infinite loop
            .ToList();

        foreach (MapObject obj in victims)
        {
            if (deathFrame == 0)
            {
                _animator.Remove(obj);
                for (int elevation = 0; elevation < MapFile.ElevationCount; elevation++)
                {
                    _solidObjects[elevation].Remove(obj);
                    _flatObjects[elevation].Remove(obj);
                }
                OnCritterRemoved(obj);
            }
            else
            {
                obj.Sid = -1;
                obj.CombatResults |= 0x80; // DAM_DEAD
                obj.CurrentHp = Math.Min(obj.CurrentHp, 0);
                ConvertToCorpse(obj, PickDeathAnim(obj));
            }
        }

        if (victims.Count > 0)
            RebuildBlockedTiles(_dude?.Dude);
    }

    /// <summary>Does this object get a critter_p_proc this tick: a live, scripted,
    /// non-dude critter that isn't a "wait here" companion (phase-10 M4). The single
    /// source of truth for both the heartbeat pump and the --companion diagnostic.</summary>
    private bool IsHeartbeatEligible(MapObject o) =>
        Fid.Type(o.Fid) is ObjectType.Critter && o != _dude?.Dude
        && !o.IsDead && o.Sid != -1 && !_waitingCompanions.Contains(o);

    /// <summary>One critter_p_proc per game tick, round-robin — the flattened
    /// _script_chk_critters ticker (scripts.cc:705), gated like the engine's
    /// !dialog && !combat && !movie check.</summary>
    private void PumpCritterProcs(double elapsedMs)
    {
        if (_scriptHost is null || _combat.Phase != Formats.Combat.CombatPhase.Idle || _combat.IsGameOver
            || _dialog is not null || _companionHub is not null || _lootContainer is not null || _worldmapOpen)
            return;

        _critterProcTimerMs += elapsedMs;
        if (_critterProcTimerMs < 100)
            return;
        _critterProcTimerMs = 0;

        // P30 A-M2: the periodic sneak re-check (sneakEventProcess) on the 100 ms heartbeat — one
        // reschedule "tick" = one heartbeat. Fires only while the flag is set; uses the isolated
        // _sneakRng so it can't perturb any other stream.
        if (_sneak.FlagSet && --_sneakTicksRemaining <= 0)
            RollSneak();

        // A "wait here" companion is skipped, so its follow critter_p_proc never runs
        // and it holds position until told to follow again (phase-10 M4).
        List<MapObject> scripted = [.. _solidObjects[_elevation].Where(IsHeartbeatEligible)];
        if (scripted.Count == 0)
            return;

        _critterProcIndex %= scripted.Count;
        MapObject critter = scripted[_critterProcIndex++];
        var result = _scriptHost.RunObjectProc(critter, _map, _dude?.Dude, "critter_p_proc");
        if (result is not null)
            foreach (string line in result.Messages)
                Log($"{ObjectName(critter)}: {line}");
    }

    /// <summary>pcAddExperience: add XP, level up while thresholds pass —
    /// <summary>The dude's kill tally per KILL_TYPE (gKillsByType, critter.cc:152; 19 types). Incremented
    /// on a dude/team kill, read by metarule3 GET_KILL_COUNT + the char-sheet display (P38).</summary>
    private int[] _killsByType = new int[19];

    /// <summary>ICombatHost (P38): tally a dude/team kill by the victim's KILL_TYPE (killsIncByType,
    /// critter.cc:702). The victim's kill type is its proto field; a bad proto is skipped.</summary>
    public void RecordKill(MapObject victim)
    {
        if (GetCritterState(victim) is { } stats && stats.Proto.KillType is int kt && kt >= 0 && kt < _killsByType.Length)
            _killsByType[kt]++;
    }

    /// <summary>ICombatHost (P42): an NPC quaffs ONE healing item from its bag (the AI _ai_check_drugs
    /// heal, combat_ai.cc:999) — find a healing drug (stimpak/super-stimpak/healing-powder), roll its
    /// HP heal (the -2 random range / stat-35 amount on _combatRng, like the dude's stimpak), apply it
    /// capped at MaxHp, consume one. Returns whether it healed. Inert when the critter carries none.</summary>
    public bool TryNpcHeal(MapObject critter)
    {
        foreach (MapObject item in critter.Inventory)
        {
            if (!Formats.Combat.AiHealing.IsHealingItem(item.Pid) || SafeProto(item.Pid)?.Drug is not { } drug)
                continue;
            int healed = drug.Stats[0] == -2
                ? _combatRng.Next(drug.Amounts[0], drug.Amounts[1] + 1) // stimpak random-range heal
                : Enumerable.Range(0, 3).Where(i => drug.Stats[i] == 35).Sum(i => drug.Amounts[i]);
            if (healed <= 0)
                continue;
            int max = GetCritterState(critter)?.MaxHp ?? critter.CurrentHp;
            int before = critter.CurrentHp;
            critter.CurrentHp = Math.Min(before + healed, max);
            item.StackCount--;
            if (item.StackCount <= 0)
                critter.Inventory.Remove(item);
            Log($"The {ObjectName(critter)} uses a healing item.");
            Console.WriteLine($"ai-heal: {ObjectName(critter)}@{critter.HexTile} +{critter.CurrentHp - before} ({critter.CurrentHp}/{max})");
            return true;
        }
        return false;
    }

    /// <summary>An NPC drinks ONE non-healing combat drug to buff itself (P78-M2): pick a
    /// chem_primary_desire drug, apply its IMMEDIATE stat effect to a per-critter bonus that
    /// <see cref="GetCritterState"/> folds in (the companion-override anti-aliasing pattern; cleared on
    /// combat end), heal any HP component, consume one. DOCUMENTED SIMPLIFICATION: the timed wear-off
    /// (down-then-up ramp) isn't modelled for NPCs — the buff lasts the fight, which is shorter than any
    /// drug's onset anyway.</summary>
    public bool TryNpcUseCombatDrug(MapObject critter, int[]? primaryDesire)
    {
        var carried = critter.Inventory.Where(it => SafeProto(it.Pid)?.Drug is not null).Select(it => it.Pid).ToList();
        int pid = Formats.Combat.AiCombatDrug.Pick(carried, primaryDesire);
        if (pid < 0 || critter.Inventory.FirstOrDefault(it => it.Pid == pid) is not { } item
            || SafeProto(pid)?.Drug is not { } drug)
            return false;

        int[] bonus = _npcDrugBonus.TryGetValue(critter, out int[]? b) ? b : _npcDrugBonus[critter] = new int[35];
        int hpHeal = 0;
        for (int i = 0; i < 3; i++)
        {
            int stat = drug.Stats[i];
            if (stat == 35) hpHeal += drug.Amounts[i];          // STAT_CURRENT_HIT_POINTS
            else if (stat >= 0 && stat < 35) bonus[stat] += drug.Amounts[i]; // a SPECIAL/derived buff
        }
        if (hpHeal > 0)
        {
            int max = GetCritterState(critter)?.MaxHp ?? critter.CurrentHp;
            critter.CurrentHp = Math.Min(critter.CurrentHp + hpHeal, max);
        }
        item.StackCount--;
        if (item.StackCount <= 0)
            critter.Inventory.Remove(item);
        Log($"The {ObjectName(critter)} uses a chem.");
        return true;
    }

    private void PlayWeaponSfx(ProtoInfo? weaponProto)
    {
        if (weaponProto?.Weapon is { SoundCode: > 0 } weapon)
            _audio?.PlaySfx(Formats.Sound.SfxName.WeaponAttack(weapon.SoundCode));
    }

    /// <summary>The game-tick at which the dude's next poison damage tick fires; -1 = not poisoned.
    /// Models the engine's single EVENT_TYPE_POISON queue entry (critter.cc:351) on the game-time clock
    /// (the combat-scoped EventQueue is the wrong tool — poison must outlast combat). (P35-M3.)</summary>
    private long _dudePoisonNextTick = -1;

    /// <summary>
    /// ported from fallout2-ce src/critter.cc critterAdjustPoison() (P35): DUDE-ONLY, poison-resistance
    /// reduced; sets the poison counter + (re)schedules the next damage tick. DOCUMENTED DIVERGENCE: the
    /// engine also shows a misc.msg monitor line ("You have been poisoned!") — we apply silently to keep a
    /// copyrighted game string out of the goldens.
    /// </summary>
    private void ApplyPoison(MapObject obj, int amount)
    {
        if (_dude is null || obj != _dude.Dude)
            return; // critterAdjustPoison: non-dude returns -1 (no-op)
        if (amount > 0)
        {
            int resist = GetCritterState(obj)?.Stat(32) ?? 0; // STAT_POISON_RESISTANCE
            amount -= amount * resist / 100;
        }
        else if (obj.Poison <= 0)
        {
            return; // can't reduce poison that isn't there
        }
        obj.Poison = Math.Max(0, obj.Poison + amount);
        SchedulePoison();
    }

    /// <summary>(Re)time the single poison damage event, ported from critterAdjustPoison's
    /// _queue_clear_type(EVENT_TYPE_POISON) + queueAddEvent(10*(505-5*poison)) (critter.cc:350-351):
    /// the next tick is 10*(505-5*poison) game-ticks from now, or cleared when poison ≤ 0. (P35-M3.)</summary>
    private void SchedulePoison() => _dudePoisonNextTick =
        _dude is { Dude.Poison: > 0 } d ? _clock.Ticks + 10L * (505 - 5 * d.Dude.Poison) : -1;

    /// <summary>
    /// Fire every poison damage tick now due, ported from poisonEventProcess (critter.cc:378): each tick
    /// is DUDE-ONLY, decrements poison by 2 + deals 1 HP, then re-queues at the reduced interval until
    /// poison ≤ 0. The loop drains all ticks a clock JUMP (rest/travel) made due, each re-timed from its
    /// own fire instant (so a big jump deals the right number of ticks). Driven from UpdateClock. The
    /// engine's "You take damage from poison." misc.msg line is omitted (copyrighted; silent — P35 pattern).
    /// </summary>
    private void ProcessPoison()
    {
        if (_dude is not { } d || _dudePoisonNextTick < 0)
            return;
        while (_dudePoisonNextTick >= 0 && _clock.Ticks >= _dudePoisonNextTick && d.Dude.Poison > 0)
        {
            long firedAt = _dudePoisonNextTick;
            d.Dude.Poison = Math.Max(0, d.Dude.Poison - 2);
            d.Dude.CurrentHp -= 1; // critterAdjustHitPoints(obj, -1)
            if (d.Dude.CurrentHp <= 0 && !_combat.IsGameOver)
                GameOver(); // death by poison
            _dudePoisonNextTick = d.Dude.Poison > 0 ? firedAt + 10L * (505 - 5 * d.Dude.Poison) : -1;
        }
    }

    /// <summary>Out-of-ammo empty-click sfx (combat.cc:5745) — P34-M5.</summary>
    public void OnWeaponOutOfAmmo(ProtoInfo weaponProto)
    {
        if (weaponProto.Weapon is { SoundCode: > 0 } weapon)
            _audio?.PlaySfx(Formats.Sound.SfxName.WeaponName(Formats.Sound.SfxName.WeaponSoundEffect.OutOfAmmo, weapon.SoundCode, primaryOrPunch: true));
    }

    // ===================================================================
    //  ICombatHost — the rest of the seam to CombatEngine (phase-9 M0).
    //  The viewer keeps single ownership of the animator, walkers, draw
    //  lists and blocking; the engine reaches them through these methods.
    // ===================================================================

    public MapObject? Dude => _dude?.Dude;
    public void StopDude() => _dude?.Stop();

    /// <summary>Criticals enable after one full game-day, like the engine
    /// (random.cc: gameTime / TICKS_PER_DAY >= 1).</summary>
    public bool CriticalsEnabled => _clock.Ticks / Formats.GameClock.TicksPerDay >= 1;

    /// <summary>ICombatHost (P41): the engine suppresses the DUDE's critical-FAILURE EFFECT until day 6
    /// (combat.cc:4190); the trigger still fires from day 2. Non-dude fumbles have no such gate.</summary>
    public bool DudeCritFailuresEnabled => _clock.Ticks / Formats.GameClock.TicksPerDay >= 6;

    private Formats.Combat.AiPacketTable? _aiPackets;
    private bool _aiPacketsLoaded;

    /// <summary>Resolve a critter's ai.txt packet: instance aiPacket first, proto
    /// fallback (the engine's order); null if 0 or ai.txt is absent.</summary>
    public Formats.Combat.AiPacket? GetAiPacket(MapObject critter)
    {
        if (!_aiPacketsLoaded)
        {
            _aiPacketsLoaded = true;
            try
            {
                _aiPackets = Formats.Combat.AiPacketTable.Parse(
                    System.Text.Encoding.Latin1.GetString(_vfs.ReadAllBytes(@"data\ai.txt")));
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
                _aiPackets = null; // no ai.txt → no packets → pre-M1 behaviour
            }
        }
        if (_aiPackets is null)
            return null;

        int packet = critter.AiPacket;
        if (packet == 0)
        {
            try { packet = _protos.Get(critter.Pid).Critter?.AiPacket ?? 0; }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException) { }
        }
        return _aiPackets.Get(packet);
    }

    /// <summary>P72-M3: roll the critter's combat taunt (combat_ai.cc _combatai_msg) and, on a hit,
    /// float the resolved combatai.msg line over it in the packet's palette colour. The chance +
    /// message rolls draw from the ISOLATED <see cref="_tauntRng"/>, and the float is Draw-only, so
    /// combat goldens stay byte-identical (a taunting critter perturbs nothing the transcript sees).
    /// Skips a dead/knocked-out critter (the engine's DAM_DEAD|DAM_KNOCKED_OUT guard, :3316).</summary>
    private void TryTaunt(MapObject critter, Formats.Combat.CombatTaunt.Type type)
    {
        if (critter.IsDead || (critter.CombatResults & Formats.Combat.CriticalTables.DamKnockedOut) != 0)
            return;
        if (GetAiPacket(critter) is not { } pkt)
            return;
        _tauntRng ??= new Formats.Combat.SystemCombatRng(RngSeed ?? Environment.TickCount);
        int msgId = Formats.Combat.CombatTaunt.Pick(pkt, type, _tauntRng);
        if (msgId < 0)
            return;
        if (LazyMsg(@"text\english\game\combatai.msg", ref _combatAiMsgTried, ref _combatAiMsg)?.GetText(msgId)
            is not { Length: > 0 } text)
            return;
        (byte r, byte g, byte b) = _palette.GetRgb(pkt.TauntColor & 0xFF);
        _floatText.Add(critter.HexTile, _elevation, text, new Color(r, g, b));
    }

    public bool IsBlocked(int tile) => _blockedTiles.Contains(tile);
    public bool IsAnimating(MapObject critter) => _animator.TryGetState(critter, out _);
    public bool IsFallInProgress(MapObject critter) =>
        _animator.TryGetState(critter, out AnimationState state) && !state.Finished;
    public bool IsAnyWalkerMoving() => _npcWalkers.Values.Any(w => w.Moving);
    public bool IsWalkerMoving(MapObject critter) =>
        _npcWalkers.TryGetValue(critter, out DudeController? w) && w.Moving;
    public bool StartWalk(MapObject critter, int targetTile) => StartNpcWalk(critter, targetTile);

    public void OnThrowStarted(MapObject thrower, int targetTile, ProtoInfo weaponProto)
    {
        // P45: the throw's defender for the float-text layer (null = an empty/AoE landing tile).
        _floatDefender = CritterAt(targetTile);
        const int animThrow = 18; // ANIM_THROW_ANIM (item.cc _attack_anim[5])
        int code = weaponProto.Weapon?.AnimationCode ?? 0;
        int fid = Fid.Build(ObjectType.Critter, Fid.Index(thrower.Fid), animThrow, code);
        if (!_vfs.Exists(_artIndex.GetFrmPath(fid)))
            fid = Fid.Build(ObjectType.Critter, Fid.Index(thrower.Fid), animThrow, 0);
        if (_vfs.Exists(_artIndex.GetFrmPath(fid)))
            _animator.PlayActionOnce(thrower, fid);
        PlayWeaponSfx(weaponProto);
        LaunchProjectile(thrower.HexTile, targetTile, weaponProto); // the thrown item flies (phase-10 #11)
    }

    public void RemoveFromHand(MapObject thrower, MapObject item)
    {
        List<MapObject> bag = thrower == _dude?.Dude ? _dudeInventory : thrower.Inventory;
        item.StackCount--;
        if (item.StackCount <= 0)
            bag.Remove(item);
    }

    /// <summary>Land a thrown non-explosive weapon on the ground (a fresh Item-type
    /// object, so the existing pickup recovers it).</summary>
    public void DropThrownWeapon(MapObject item, int tile)
    {
        if (RebuildObject(item.Pid, 1) is not { } dropped)
            return;
        dropped.HexTile = tile;
        dropped.Flags |= 0x08; // flat: rests on the ground
        InsertSorted(_flatObjects[_elevation], dropped);
    }

    /// <summary>Spawn the misc-10 explosion marker and broadcast damage_p_proc to
    /// scripted objects in radius 3 with it as the source — so the temple door's
    /// script sees metarule(49) == EXPLOSION and opens (_scr_explode_scenery).</summary>
    public void SpawnExplosionMarker(int tile)
    {
        var marker = new MapObject
        {
            Id = -7, HexTile = tile, X = 0, Y = 0, Frame = 0, Rotation = 0,
            Fid = Fid.Build(ObjectType.Misc, 10, 0, 0), Flags = 0x08 | 0x10, Pid = 0x05000010, Sid = -1,
        };
        foreach (MapObject obj in _solidObjects[_elevation]
            .Where(o => o.Sid != -1 && Formats.Hex.HexGrid.Distance(o.HexTile, tile) <= 3).ToList())
        {
            var scripted = _scriptHost?.RunObjectProc(obj, _map, marker, fixedParam: 20, actionBeingUsed: -1,
                "damage_p_proc");
            if (scripted is not null)
                foreach (string line in scripted.Messages)
                    Log(line);
        }
    }

    /// <summary>Knockback relocation: move a critter to a tile with no walk
    /// animation, re-sorting the draw list + blocking (and tripping any spatial
    /// at the landing tile, like a step would).</summary>
    public void PlaceCritter(MapObject critter, int tile)
    {
        critter.HexTile = tile;
        List<MapObject> solids = _solidObjects[_elevation];
        if (solids.Remove(critter))
            InsertSorted(solids, critter);
        RebuildBlockedTiles(_dude?.Dude);
        _scriptHost?.RunSpatialsAt(_map, tile, _elevation, critter);
    }
    public void Transcript(string line) => Console.WriteLine(line);

    public IReadOnlyCollection<MapObject> PartyMembers =>
        (IReadOnlyCollection<MapObject>?)_scriptHost?.PartyMembers ?? [];

    public IEnumerable<MapObject> CombatCritters =>
        _dude is null ? [] : _solidObjects[_elevation].Where(o =>
            Fid.Type(o.Fid) is ObjectType.Critter && o != _dude.Dude);

    /// <summary>
    /// reg_anim_func END: play a flushed batch of queued reg_anim actions (P33-M1).
    /// The engine gates every reg_anim op on !isInCombat() (interpreter_extra.cc:3460) and
    /// plays the batch SEQUENTIALLY over time; we execute in parallel and ignore the delay
    /// (DOCUMENTED SIMPLIFICATIONS). run==walk (no separate run animation/speed). Animate
    /// loops the FRM rather than playing once (no one-shot primitive). SLICE NOTE: no
    /// shippable map fires the move/animate ops at map_enter (only animate_forever for
    /// scenery, P21), so this is forward-looking — it lights up when content uses it.
    /// </summary>
    private void ExecuteRegAnim(IReadOnlyList<Formats.Int.RegAnimAction> actions)
    {
        if (_combat.Phase != Formats.Combat.CombatPhase.Idle)
            return;

        foreach (Formats.Int.RegAnimAction a in actions)
        {
            switch (a.Kind)
            {
                case Formats.Int.RegAnimKind.MoveToTile:
                case Formats.Int.RegAnimKind.RunToTile:
                {
                    bool started = StartNpcWalk(a.Object, a.Tile);
                    _regAnimMoves.Add(
                        $"{ObjectName(a.Object)}@{a.Object.HexTile}->{a.Tile}:"
                        + $"{(a.Kind == Formats.Int.RegAnimKind.RunToTile ? "run" : "walk")}:{(started ? "ok" : "no")}");
                    break;
                }
                case Formats.Int.RegAnimKind.MoveToObject:
                case Formats.Int.RegAnimKind.RunToObject:
                {
                    // The engine walks to the destination object's tile; if that tile is
                    // blocked, settle on a free neighbour (the Placement port, P33-M0).
                    int dest = a.Dest is null
                        ? -1
                        : Formats.Map.Placement.FreeTileNear(a.Dest.HexTile, t => _blockedTiles.Contains(t));
                    bool started = dest >= 0 && StartNpcWalk(a.Object, dest);
                    _regAnimMoves.Add(
                        $"{ObjectName(a.Object)}@{a.Object.HexTile}->obj@{dest}:"
                        + $"{(a.Kind == Formats.Int.RegAnimKind.RunToObject ? "run" : "walk")}:{(started ? "ok" : "no")}");
                    break;
                }
                case Formats.Int.RegAnimKind.Animate:
                case Formats.Int.RegAnimKind.AnimateReverse:
                {
                    if (Fid.Type(a.Object.Fid) is ObjectType.Critter)
                        _animator.SetCritterAnimation(a.Object, Fid.Build(ObjectType.Critter,
                            Fid.Index(a.Object.Fid), a.Anim, Fid.WeaponCode(a.Object.Fid), a.Object.Rotation));
                    else
                        _animator.AddLooping(a.Object);
                    _regAnimMoves.Add(
                        $"{ObjectName(a.Object)}@{a.Object.HexTile}:anim{a.Anim}"
                        + (a.Kind == Formats.Int.RegAnimKind.AnimateReverse ? "rev" : string.Empty));
                    break;
                }
            }
        }
    }

    /// <summary>reg_anim_clear: drop a pending animation + stop/forget a walker.</summary>
    public void ClearAnimation(MapObject critter)
    {
        _animator.Remove(critter);
        if (_npcWalkers.TryGetValue(critter, out DudeController? walker))
        {
            walker.Stop();
            _npcWalkers.Remove(critter);
        }
    }

    public void OnAttackStarted(MapObject attacker, MapObject target, ProtoInfo? weaponProto)
    {
        // P45: remember THIS attack's real defender for the floating combat-text layer. The
        // outcome Log line ("...hits you for N damage.") names the defender as "you" even for
        // an NPC-vs-NPC blow (ResolveAttack keys the wording on byDude, not the real defender),
        // so the wording can't be trusted — the tracked object can. This also covers the dude
        // AS defender, which OnTargetHit/OnTargetDodge deliberately skip (the camera-anchor dude
        // doesn't visibly react — P34-M6) and which the "different shade for the dude" needs.
        _floatDefender = target;
        // P72-M3: the attacker taunts on its swing (AI_MESSAGE_TYPE_ATTACK, actions.cc:630). The dude
        // never taunts (combat_ai.cc:3314 critter == gDude → -1).
        if (attacker != _dude?.Dude)
            TryTaunt(attacker, Formats.Combat.CombatTaunt.Type.Attack);
        if (weaponProto?.Weapon is not null)
            PlayWeaponSfx(weaponProto);
        // Unarmed/melee swing grunt (actions.cc:625 sfxBuildCharName(attacker, ANIM_THROW_PUNCH, CONTACT)) —
        // a wielded weapon plays its own sfx above instead (P34-M5).
        else if (Formats.Sound.SfxName.CharName(_artIndex.CritterBaseName(attacker.Fid), 16 /*ANIM_THROW_PUNCH*/,
                     Formats.Sound.SfxName.CharacterSoundEffect.Contact, Fid.WeaponCode(attacker.Fid)) is { } swing)
            _audio?.PlaySfx(swing);
        StartAttackAnimation(attacker, weaponProto);
        LaunchProjectile(attacker, target, weaponProto);
    }

    // A projectile sprite flying attacker→target over its travel time — a purely
    // visual overlay (phase-10 #11): it doesn't gate combat or emit transcript, so
    // headless runs + the golden harnesses are unaffected.
    private sealed class Projectile
    {
        public required int Fid;
        public required int Rotation;
        public required int FromTile;
        public required int ToTile;
        public required double DurationMs;
        public double ElapsedMs;
    }
    private readonly List<Projectile> _projectiles = [];

    private void LaunchProjectile(MapObject attacker, MapObject target, ProtoInfo? weaponProto) =>
        LaunchProjectile(attacker.HexTile, target.HexTile, weaponProto);

    /// <summary>Send a projectile sprite from one tile to another for a ranged or thrown
    /// shot (melee — adjacent — gets none). Art: the weapon's ProjectilePid, else the
    /// weapon item itself (thrown). Resolves nothing → no projectile (phase-10 #11).</summary>
    private void LaunchProjectile(int fromTile, int toTile, ProtoInfo? weaponProto)
    {
        if (weaponProto?.Weapon is not { } weapon)
            return; // unarmed/melee-proto: no projectile
        int distance = Formats.Hex.HexGrid.Distance(fromTile, toTile);
        if (distance <= 1)
            return; // adjacent = melee swing, no flight

        int projectileFid;
        try
        {
            projectileFid = weapon.ProjectilePid > 0 ? _protos.Get(weapon.ProjectilePid).Fid : weaponProto.Fid;
            _ = _frmCache.GetFrm(projectileFid); // ensure the art loads, else skip
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            return;
        }

        _projectiles.Add(new Projectile
        {
            Fid = projectileFid,
            Rotation = Formats.Hex.HexGrid.RotationTo(fromTile, toTile),
            FromTile = fromTile,
            ToTile = toTile,
            DurationMs = Math.Max(120, distance * 24), // ~24 ms per hex of flight
        });
    }

    private void AdvanceProjectiles(double elapsedMs)
    {
        if (_projectiles.Count == 0)
            return;
        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            _projectiles[i].ElapsedMs += elapsedMs;
            if (_projectiles[i].ElapsedMs >= _projectiles[i].DurationMs)
                _projectiles.RemoveAt(i);
        }
    }

    /// <summary>Draw each in-flight projectile at its lerped screen position between the
    /// from/to tile centers (phase-10 #11).</summary>
    private void DrawProjectiles()
    {
        foreach (Projectile p in _projectiles)
        {
            Formats.Frm.FrmFrame frame;
            Texture2D texture;
            try
            {
                frame = _frmCache.GetFrm(p.Fid).GetFrame(0, p.Rotation);
                texture = _frmCache.GetTexture(p.Fid, 0, p.Rotation);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
                continue;
            }
            (int fx, int fy) = _camera.HexToScreen(p.FromTile);
            (int tx, int ty) = _camera.HexToScreen(p.ToTile);
            float t = (float)Math.Clamp(p.ElapsedMs / p.DurationMs, 0, 1);
            float x = fx + (tx - fx) * t - frame.Width / 2f;
            float y = fy + (ty - fy) * t - frame.Height / 2f;
            _spriteBatch.Draw(texture, new Vector2(x, y), LightTint(p.ToTile));
        }
    }

    /// <summary>Hit-react FRM (anim 14) on a surviving, non-dude target.</summary>
    public void OnTargetHit(MapObject target, MapObject attacker, bool knockedDown)
    {
        const int animHitFromFront = 14, animHitFromBack = 15;
        // Got-hit grunt (actions.cc:431 sfxBuildCharName(defender, ANIM_HIT_FROM_FRONT, UNUSED)) —
        // audio-only, plays for any target incl. the dude; null/silent when the base is unresolvable (P34-M5).
        if (Formats.Sound.SfxName.CharName(_artIndex.CritterBaseName(target.Fid), animHitFromFront,
                Formats.Sound.SfxName.CharacterSoundEffect.Unused, Fid.WeaponCode(target.Fid)) is { } grunt)
            _audio?.PlaySfx(grunt);

        // P69-M2: the dude reacts too (the engine reacts him — actions.cc _show_damage_to_object). The
        // render path already supports it: ResolveSprite uses the animator state when the walker isn't
        // moving (Rendering.cs:130), and the dude already falls on death via PlayFall. The brief in-place
        // reaction doesn't move the dude, so the camera anchor is unaffected. (Closes the P34-M6 spillover.)
        // Already mid-fall (Once-mode = a held FALL)? Don't override it with a hit-react
        // (actions.cc:438 early-returns for a prone defender). P34-M6.
        if (_animator.TryGetState(target, out AnimationState falling) && falling.Mode == AnimationMode.Once)
            return;

        bool front = Formats.Combat.SneakAttack.IsHitFromFront(attacker.Rotation, target.Rotation);
        int weaponCode = Fid.WeaponCode(target.Fid);

        if (knockedDown) // a crit that knocks the target down plays a FALL, not a hit-react (P34-M6).
        {
            int fallFid = Fid.Build(ObjectType.Critter, Fid.Index(target.Fid),
                Formats.Combat.ReactionAnims.KnockdownFall(front), 0);
            if (_vfs.Exists(_artIndex.GetFrmPath(fallFid)))
                _animator.PlayFall(target, fallFid);
            return;
        }

        // Hit-from-front vs back (back only if the critter ships ANIM_HIT_FROM_BACK art — actions.cc:425).
        bool backArt = _vfs.Exists(_artIndex.GetFrmPath(
            Fid.Build(ObjectType.Critter, Fid.Index(target.Fid), animHitFromBack, weaponCode)));
        int anim = Formats.Combat.ReactionAnims.HitReaction(front, backArt);
        int hitFid = Fid.Build(ObjectType.Critter, Fid.Index(target.Fid), anim, weaponCode);
        if (_vfs.Exists(_artIndex.GetFrmPath(hitFid)))
            _animator.PlayActionOnce(target, hitFid);
    }

    /// <summary>Dodge reaction on a miss (P34-M6; the dude reacts too as of P69-M2).</summary>
    public void OnTargetDodge(MapObject target)
    {
        int fid = Fid.Build(ObjectType.Critter, Fid.Index(target.Fid),
            Formats.Combat.ReactionAnims.Dodge, Fid.WeaponCode(target.Fid));
        if (_vfs.Exists(_artIndex.GetFrmPath(fid)))
            _animator.PlayActionOnce(target, fid);
    }

    /// <summary>P72-M3: a fleeing critter floats its RUN taunt (AI_MESSAGE_TYPE_RUN).</summary>
    public void OnCritterFlee(MapObject critter) =>
        TryTaunt(critter, Formats.Combat.CombatTaunt.Type.Run);

    /// <summary>Stand-up sprite when a prone critter gets up (P34-M6; the dude too as of P69-M2) — the
    /// prone flag is already cleared.</summary>
    public void OnGetUp(MapObject critter)
    {
        int anim = Formats.Combat.ReactionAnims.StandUp(Fid.AnimType(critter.Fid));
        int fid = Fid.Build(ObjectType.Critter, Fid.Index(critter.Fid), anim, Fid.WeaponCode(critter.Fid));
        if (_vfs.Exists(_artIndex.GetFrmPath(fid)))
            _animator.PlayActionOnce(critter, fid);
    }

    /// <summary>Death scream + start the fall; true if a fall is playing (caller
    /// waits), false if no fall art (corpse converted immediately).</summary>
    public bool StartDeathFall(MapObject critter, int deathAnim)
    {
        // Death scream (actions.cc:321 sfxBuildCharName(defender, anim, CHARACTER_SOUND_EFFECT_DIE)).
        // NPCs use the faithful CharName (scorpions → MASCRP* which ship; humans → HMWARR* which don't,
        // i.e. engine-faithful silence). The DUDE keeps the HumanDeath HM/HFXXXX fallback (the P8 scream,
        // a documented divergence) so the player death audio isn't regressed (P34-M5).
        if (critter == _dude?.Dude)
        {
            bool female = _dudeGcd?.Stats.BaseStats[34] == 1;
            _audio?.PlaySfx(Formats.Sound.SfxName.HumanDeath(female, deathAnim));
        }
        else if (Formats.Sound.SfxName.CharName(_artIndex.CritterBaseName(critter.Fid), deathAnim,
                     Formats.Sound.SfxName.CharacterSoundEffect.Die, Fid.WeaponCode(critter.Fid)) is { } scream)
        {
            _audio?.PlaySfx(scream);
        }

        int fallFid = Fid.Build(ObjectType.Critter, Fid.Index(critter.Fid), deathAnim, 0);
        if (_vfs.Exists(_artIndex.GetFrmPath(fallFid)))
        {
            _animator.PlayFall(critter, fallFid);
            return true;
        }

        ConvertToCorpse(critter, deathAnim);
        return false;
    }

    /// <summary>Forget bookkeeping for a dead critter (walker + home tile).</summary>
    public void OnCritterRemoved(MapObject critter)
    {
        _npcWalkers.Remove(critter);
        _homeTiles.Remove(critter);
    }

    public IReadOnlyList<string> RunDamageProc(MapObject target, MapObject? source, int damage) =>
        _scriptHost?.RunObjectProc(target, _map, source, fixedParam: damage, actionBeingUsed: -1,
            "damage_p_proc")?.Messages?.ToList() ?? [];

    public (IReadOnlyList<string> Lines, bool Overridden) RunDestroyProc(MapObject critter, MapObject? killer)
    {
        var scripted = _scriptHost?.RunObjectProc(critter, _map, killer, "destroy_p_proc");
        return scripted is null ? ([], false) : (scripted.Messages.ToList(), scripted.Overridden);
    }

    /// <summary>
    /// A combat_p_proc hook (P35). The engine sets source = NULL always (scriptSetObjects(sid, NULL, ...));
    /// the per-turn hook (fp=4) has target null, the on-hit hook (fp=2) sets target = the struck defender.
    /// Routed through ScriptHost.RunCombatProc, which decouples source/target/dude so dude_obj is the real
    /// dude (the P35 RunObjectProc coupling is gone).
    /// </summary>
    public (IReadOnlyList<string> Lines, bool Overridden) RunCombatProc(MapObject critter, int fixedParam, MapObject? target = null)
    {
        var scripted = _scriptHost?.RunCombatProc(critter, target, _dude?.Dude, _map, fixedParam);
        return scripted is null ? ([], false) : (scripted.Messages.ToList(), scripted.Overridden);
    }

    public void RemovePartyMember(MapObject critter)
    {
        if (_scriptHost?.PartyMembers.Remove(critter) == true)
        {
            _partyScriptIndex.Remove(critter);
            Log($"{ObjectName(critter)} has fallen.");
        }
    }

    /// <summary>Death-screen monitor line; the engine sets state + prints the
    /// "GAME OVER" transcript line and shows the screen via _combat.IsGameOver.</summary>
    public void GameOver() => Log("You have died. F9 loads the last save.");
}
