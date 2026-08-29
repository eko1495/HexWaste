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
        bool isDude = critter == _dude?.Dude;
        List<MapObject> bag = isDude ? _dudeInventory : critter.Inventory;
        // P81: the dude fires the ACTIVE hand only (_activeHand bit); NPCs keep the first-in-hand scan
        // (the engine forces HAND_RIGHT for NPCs). _activeHand defaults to FlagInRightHand and no slice
        // golden sets a left-hand dude weapon, so the active-hand item == the sole in-hand weapon → the
        // dude's resolution is byte-identical to the old first-in-hand scan.
        foreach (MapObject item in bag.Where(i => isDude ? (i.Flags & _activeHand) != 0 : i.IsInHand))
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

    /// <summary>aiHaveAmmo (combat_ai.cc:1765): every ammo caliber in this critter's inventory.</summary>
    public IReadOnlyList<int> CarriedAmmoCalibers(MapObject critter) =>
        [.. critter.Inventory.Select(it => SafeProto(it.Pid)?.Ammo?.Caliber ?? -1).Where(c => c >= 0).Distinct()];

    /// <summary>ported from fallout2-ce src/combat_ai.cc _ai_search_environ (:2178): item objects on the
    /// current elevation within maxDistance, nearest first. PID type 0 == OBJ_TYPE_ITEM (Fid.PidType).</summary>
    public IReadOnlyList<(ProtoInfo Proto, MapObject Item)> GroundItemsNear(MapObject critter, int maxDistance)
    {
        List<(ProtoInfo Proto, MapObject Item, int Distance)> found = [];
        foreach (MapObject o in _flatObjects[_elevation].Concat(_solidObjects[_elevation]))
        {
            if (Fid.PidType(o.Pid) != 0)
                continue; // OBJ_TYPE_ITEM only
            int d = Formats.Hex.HexGrid.Distance(critter.HexTile, o.HexTile);
            if (d > maxDistance)
                continue;
            if (SafeProto(o.Pid) is { } proto)
                found.Add((proto, o, d));
        }
        return [.. found.OrderBy(f => f.Distance).Select(f => (f.Proto, f.Item))];
    }

    /// <summary>ported from fallout2-ce src/combat_ai.cc _ai_retrieve_object (:2237): adjacent → transfer
    /// to inventory; otherwise start a walk toward it and report "not yet" so the caller remembers it for
    /// next turn. Issue-2 hardening: the reference re-checks item->owner (:2250) — someone else may already
    /// have picked it up while this critter was remembering it across turns. Hexwaste has no MapObject.Owner
    /// field, so "still available" is read directly off world-list membership instead; a stale item (no
    /// longer in the draw lists) fails outright rather than being double-added to this critter's inventory.
    /// </summary>
    public bool TryRetrieveItem(MapObject critter, MapObject item)
    {
        if (!_flatObjects[_elevation].Contains(item) && !_solidObjects[_elevation].Contains(item))
            return false; // no longer on the ground — someone else already claimed it

        if (Formats.Hex.HexGrid.Distance(critter.HexTile, item.HexTile) > 1)
        {
            StartWalk(critter, item.HexTile);
            return false;
        }
        // OnScriptObjectRemoved already strips the item from every elevation's _flatObjects/_solidObjects
        // (ViewerGame.cs:5210-5216) — mirrors PickUpItem (ViewerGame.cs:3540), which has no redundant
        // follow-up Remove calls either.
        OnScriptObjectRemoved(item);
        foreach (MapElevation? elev in _map.Elevations)
            elev?.Objects.Remove(item);
        critter.Inventory.Add(item);
        _audio?.PlaySfx("ipickup1", SfxGain(critter)); // P117 sfx (inventory.cc:2364)
        Log($"The {ObjectName(critter)} picks up: {ObjectName(item)}.");
        return true;
    }

    /// <summary>Wield a carried weapon: clear every in-hand flag in the bag, then set the new item's
    /// right hand (_inven_wield HAND_RIGHT) so <see cref="EquippedWeapon"/> returns it. P43.
    /// P118: the AI switch also updates the idle art + draw anim (_invenWieldFunc animate=true).</summary>
    public void EquipWeapon(MapObject critter, MapObject weaponItem)
    {
        List<MapObject> bag = critter == _dude?.Dude ? _dudeInventory : critter.Inventory;
        foreach (MapObject it in bag)
            it.Flags &= ~(MapObject.FlagInLeftHand | MapObject.FlagInRightHand);
        weaponItem.Flags |= MapObject.FlagInRightHand;
        SetWieldedWeaponArt(critter, SafeProto(weaponItem.Pid), animate: true);
    }

    private const int AnimTakeOut = 38;  // ANIM_TAKE_OUT (animation.h)
    private const int AnimPutAway = 39;  // ANIM_PUT_AWAY

    /// <summary>P118: stamp (or clear) a critter's idle-fid weapon nibble on wield/unwield, with
    /// the draw/holster transition — ported from fallout2-ce src/inventory.cc _invenWieldFunc
    /// (:3269, the hand==activeHand tail) / _invenUnwieldFunc (:3417). The armed STAND art is the
    /// fid's weapon code; a missing armed-art set degrades to the unarmed fid (the engine refuses
    /// the wield outright — Hexwaste's combat model is flag-driven, so refusing would desync).
    /// DOCUMENTED SIMPLIFICATION: fo2ce sequences put-away THEN take-out when switching weapon to
    /// weapon; Hexwaste's animator plays one action at a time, so the most relevant single
    /// transition plays (take-out on ready, put-away on stow), plus the put-away char sfx.</summary>
    public void SetWieldedWeaponArt(MapObject critter, ProtoInfo? weaponProto, bool animate)
    {
        if (Fid.Type(critter.Fid) is not ObjectType.Critter)
            return;

        int oldCode = Fid.WeaponCode(critter.Fid);
        int newCode = weaponProto?.Weapon?.AnimationCode ?? 0;
        int armedStand = Fid.Build(ObjectType.Critter, Fid.Index(critter.Fid), 0, newCode);
        if (newCode != 0 && !_vfs.Exists(_artIndex.GetFrmPath(armedStand)))
        {
            newCode = 0; // no armed art for this critter — keep the unarmed stand
            armedStand = Fid.Build(ObjectType.Critter, Fid.Index(critter.Fid), 0, 0);
        }
        if (newCode == oldCode)
            return;

        if (animate)
        {
            if (newCode != 0) // draw: ANIM_TAKE_OUT with the NEW weapon's code
            {
                int takeOut = Fid.Build(ObjectType.Critter, Fid.Index(critter.Fid), AnimTakeOut, newCode);
                if (_vfs.Exists(_artIndex.GetFrmPath(takeOut)))
                    _animator.PlayActionOnce(critter, takeOut);
            }
            else if (oldCode != 0) // holster: ANIM_PUT_AWAY with the OLD weapon's code
            {
                int putAway = Fid.Build(ObjectType.Critter, Fid.Index(critter.Fid), AnimPutAway, oldCode);
                if (_vfs.Exists(_artIndex.GetFrmPath(putAway)))
                    _animator.PlayActionOnce(critter, putAway);
            }
            // the holster foley (inventory.cc:3379/3443 sfxBuildCharName(ANIM_PUT_AWAY))
            if (oldCode != 0 && Formats.Sound.SfxName.CharName(
                    _artIndex.CritterBaseName(critter.Fid), AnimPutAway,
                    Formats.Sound.SfxName.CharacterSoundEffect.Unused, oldCode) is { } holsterSfx)
                _audio?.PlaySfx(holsterSfx, SfxGain(critter));
        }

        critter.Fid = armedStand;
    }

    /// <summary>Loaded rounds; -1 sentinel hydrates from the proto capacity
    /// (fresh items, protoItemDataDefaults).</summary>
    public int WeaponAmmo(ProtoInfo weaponProto, MapObject item)
    {
        if (item.AmmoQuantity == -1)
            item.AmmoQuantity = weaponProto.Weapon?.AmmoCapacity ?? 0;
        return item.AmmoQuantity;
    }

    // ported from fallout2-ce src/interface.cc _intface_update_ammo_lights() (:1357-1359): the
    // readout is gated on ammoGetCapacity(item) > 0, NOT on weapon class, so the five non-gun
    // capacity weapons (Ripper, Cattle Prod, Power Fist, Super/Mega variants) show their charges.
    // NOTE: vanilla draws a 70px dithered gauge here (interfaceUpdateAmmoBar, :1985-2007) rather
    // than digits; that display-shape divergence predates this change and is tracked separately.
    // Shared with --awareness-probe (F38) so the probe exercises the real gate, not a re-statement of it.
    private static bool ShowsAmmoReadout(ProtoInfo? weaponProto) => weaponProto?.Weapon is { } w && w.AmmoCapacity > 0;

    // ported from fallout2-ce src/proto_instance.cc _obj_examine_func() (:316-323, the caliber
    // test at :319): message 547 ("…with %d/%d shots of %s") is picked on
    // ammoGetCaliber(item2) != 0, NOT on weapon class.
    // ammoGetCaliber (item.cc:1395-1412) resolves the AMMO proto via the weapon's
    // ammoTypePid and returns 0 when that pid is -1; the weapon proto's own caliber field
    // equals that ammo's caliber for every weapon with a real ammoTypePid, and is 0 when
    // it is -1, so the field is a faithful stand-in. A reload cannot break the
    // equivalence — weaponAttemptReload only accepts matching-caliber ammo.
    // Shared with --awareness-probe (F38) so the probe exercises the real gate, not a re-statement of it.
    private static bool ShowsExamineShots(ProtoInfo? weaponProto) => weaponProto?.Weapon is { } w && w.Caliber != 0;

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

    /// <summary>The COARSE line-of-fire query. ported from fallout2-ce
    /// src/object.cc _obj_shoot_blocking_at() (:2440), tile phase: !HIDDEN &&
    /// (NO_BLOCK == 0 || SHOOT_THRU == 0), then the type test. The disjunction is deliberate —
    /// each caller decides what SHOOT_THRU means for it, via ShotFilter.Obstructs.
    /// Do NOT re-add a flag term here; that is what collapsed the two stages originally.</summary>
    public MapObject? ShootBlockerAt(int tile, MapObject shooter, MapObject target)
    {
        const int noBlock = 0x10;
        const uint shootThru = 0x80000000;
        MapObject? onTile = _solidObjects[_elevation].FirstOrDefault(o =>
            o.HexTile == tile && o != shooter && o != target && !o.IsHidden
            && ((o.Flags & noBlock) == 0 || ((uint)o.Flags & shootThru) == 0)
            && (Fid.Type(o.Fid) is ObjectType.Wall or ObjectType.Scenery
                || (Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead)));
        if (onTile is not null)
            return onTile;

        // ported from fallout2-ce src/object.cc _obj_shoot_blocking_at()'s SECOND loop (:2440):
        // with nothing on the tile itself, the six neighbours are scanned for MULTIHEX objects
        // under a STRICTER gate — !HIDDEN && NO_BLOCK == 0, with NO SHOOT_THRU disjunction. The
        // asymmetry with the tile phase above is the reference's own; do not "harmonise" it.
        const int multiHex = 0x800;
        for (int dir = 0; dir < 6; dir++)
        {
            int adj = Formats.Hex.HexGrid.TileInDirection(tile, dir, 1);
            if (!Formats.Hex.HexGrid.IsValid(adj))
                continue;
            MapObject? mh = _solidObjects[_elevation].FirstOrDefault(o =>
                o.HexTile == adj && (o.Flags & multiHex) != 0
                && o != shooter && o != target && !o.IsHidden
                && (o.Flags & noBlock) == 0
                && (Fid.Type(o.Fid) is ObjectType.Wall or ObjectType.Scenery
                    || (Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead)));
            if (mh is not null)
                return mh;
        }
        return null;
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
                _audio?.PlaySfx(Formats.Sound.SfxName.WeaponName(Formats.Sound.SfxName.WeaponSoundEffect.Ready,
                    weapon.SoundCode, primaryOrPunch: true), SfxGain(holder));
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
                // P108: fo2ce routes deathFrame==0 through objectDestroy → _obj_remove, which fires
                // destroy_p_proc BEFORE removal (object.cc:3904; no scriptSetObjects, so source is
                // null) — a victim's GVAR side effects must not be silently dropped. No XP (that is
                // the combat death path, not this one).
                if (obj.Sid != -1)
                {
                    (IReadOnlyList<string> destroyLines, _) = RunDestroyProc(obj, null);
                    foreach (string line in destroyLines)
                        Log(line);
                    obj.Sid = -1;
                }
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

    /// <summary>kill_critter (0x80ED): destroy one specific critter into a lootable corpse. ported from
    /// fallout2-ce src/critter.cc critterKill(). The specific deathFrame anim is simplified to the
    /// art-resolved PickDeathAnim (the same documented simplification as KillCrittersByType — cosmetic).
    /// The dude is guarded: a scripted dude-kill would corrupt control/HUD and no slice script does it.
    /// ConvertToCorpse already drops the animator entry and rebuilds the blocked-tile set.</summary>
    public void KillCritterObject(MapObject obj, int deathFrame)
    {
        if (obj == _dude?.Dude || obj.IsDead)
            return;
        obj.Sid = -1;
        obj.CombatResults |= 0x80; // DAM_DEAD
        obj.CurrentHp = Math.Min(obj.CurrentHp, 0);
        ConvertToCorpse(obj, PickDeathAnim(obj));
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

    /// <summary>P1-M2: the recurring map_update heartbeat — ported from fallout2-ce scripts.cc:507
    /// mapUpdateEventProcess: every 600 game ticks re-run SCRIPT_PROC_MAP_UPDATE for the map script and
    /// every scripted object, then reschedule. This is what lets quests that POLL state in map_update_p_proc
    /// (klagraz KCTorr's brahmin defense, the Den gang-war escalation) re-evaluate over time instead of only
    /// once on load. Gated to normal map play (same as PumpCritterProcs): not during combat, dialog, loot,
    /// the companion hub, the worldmap, or game-over.</summary>
    private void PumpMapUpdate(double elapsedMs)
    {
        if (_scriptHost is null || _map is null || _combat.Phase != Formats.Combat.CombatPhase.Idle
            || _combat.IsGameOver || _dialog is not null || _companionHub is not null
            || _lootContainer is not null || _worldmapOpen)
            return;

        _mapUpdateClockMs += elapsedMs;
        if (_mapUpdateClockMs < MapUpdateIntervalMs)
            return;
        _mapUpdateClockMs -= MapUpdateIntervalMs;
        _mapUpdateFires++;

        // Re-evaluate the scripted set each fire (map_enter/earlier updates may have created objects).
        IEnumerable<MapObject> scripted = _map.Elevations
            .Where(e => e is not null)
            .SelectMany(e => e!.Objects)
            .Where(o => o.Sid != -1 && o != _dude?.Dude);
        _scriptHost.RunMapUpdate(_map, scripted, _dude?.Dude);
    }

    /// <summary>P1-M2 diagnostic: how many times the recurring map_update heartbeat has fired.</summary>
    private int _mapUpdateFires;

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
    /// <see cref="GetCritterState"/> folds in (the companion-override anti-aliasing pattern), heal any HP
    /// component, consume one, then schedule the same two delayed wear-off kicks the dude gets so the
    /// buff ramps down on the game clock instead of lasting until a blanket combat-end wipe (P37, this
    /// task). ported from item.cc _item_d_take_drug (:2809).</summary>
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

        // item.cc _item_d_take_drug (:2809): the immediate effect is followed by two delayed kicks that
        // ramp it back down — the same wear-off the dude gets (P37). Before this, an NPC's buff was
        // permanent until the blanket combat-end wipe, i.e. it had no duration at all.
        ScheduleDrugEvent(drug.Duration1, drug.Stats, drug.Amount1, critter);
        ScheduleDrugEvent(drug.Duration2, drug.Stats, drug.Amount2, critter);

        item.StackCount--;
        if (item.StackCount <= 0)
            critter.Inventory.Remove(item);
        Log($"The {ObjectName(critter)} uses a chem.");
        return true;
    }

    /// <summary>P121: <paramref name="at"/> anchors the shot for positional attenuation
    /// (the attacker); null = full volume.</summary>
    private void PlayWeaponSfx(ProtoInfo? weaponProto, MapObject? at = null)
    {
        if (weaponProto?.Weapon is { SoundCode: > 0 } weapon)
            _audio?.PlaySfx(Formats.Sound.SfxName.WeaponAttack(weapon.SoundCode), SfxGain(at));
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
    /// <summary>P101 (Tier D) + P113 (item 7c): radiation_inc/dec — the dude's radiation counter, ported
    /// from fallout2-ce src/critter.cc critterAdjustRadiation: DUDE-ONLY, resistance reduces the +amount,
    /// clamp ≥0, and a positive gain sets CRITTER_RADIATED so the next midnight runs the band model.</summary>
    private void ApplyRadiation(MapObject obj, int amount)
    {
        if (_dude is null || obj != _dude.Dude)
            return; // critterAdjustRadiation: non-dude returns -1 (no-op)
        if (amount > 0)
        {
            int resist = GetCritterState(obj)?.Stat(31) ?? 0; // STAT_RADIATION_RESISTANCE
            amount -= amount * resist / 100;
            if (amount > 0)
                _dudeRadiated = true; // CRITTER_RADIATED — the midnight check will process it
        }
        obj.Radiation = Math.Max(0, obj.Radiation + amount);
    }

    // P113 (item 7c): the radiation band model, ported from fallout2-ce critter.cc:487-643. State
    // persists on the game clock like the poison model (the counter itself rides on MapObject.Radiation,
    // which the delta save already carries; the transient pending events + applied bonus are session-only,
    // a documented simplification vs a full save/restore). Goldens never irradiate ≥100 → byte-identical.
    private bool _dudeRadiated;            // CRITTER_RADIATED — a dose is pending a midnight check
    private int _lastRadCheckDay = -1;     // the last day boundary the midnight check ran for
    private readonly List<(long FireTick, int Level, bool IsHealing)> _radEvents = []; // pending damage/heal
    private readonly int[] _radBonus = new int[35]; // the currently-applied rad penalty (for reference/undo)

    /// <summary>Driven from every clock advance (like ProcessPoison): run the midnight radiation check for
    /// each crossed day boundary, then fire any due damage/heal events. Robust to clock JUMPS (rest/travel).</summary>
    private void ProcessRads()
    {
        if (_dude is null)
            return;
        int day = _clock.Day;
        if (_lastRadCheckDay < 0)
            _lastRadCheckDay = day;
        while (_lastRadCheckDay < day)
        {
            _lastRadCheckDay++;
            RadMidnightCheck(); // early-returns unless _dudeRadiated (the flag clears after one check)
        }

        // Fire due events in fire-tick order (a jump can make several due at once).
        while (true)
        {
            int idx = -1;
            long best = long.MaxValue;
            for (int i = 0; i < _radEvents.Count; i++)
                if (_radEvents[i].FireTick <= _clock.Ticks && _radEvents[i].FireTick < best)
                    (best, idx) = (_radEvents[i].FireTick, i);
            if (idx < 0)
                break;
            (long _, int level, bool isHealing) = _radEvents[idx];
            _radEvents.RemoveAt(idx);
            // A damage event schedules its own reversal 7 days out (critter.cc:634-637), then applies.
            if (!isHealing)
                _radEvents.Add((_clock.Ticks + 7L * Formats.GameClock.TicksPerDay, level, true));
            ApplyRadEffect(level, isHealing);
        }
    }

    /// <summary>_critter_check_rads (critter.cc:487): the once-per-midnight check. Reads the counter's band,
    /// an END save can bump it one harder, and if it exceeds any pending event's level a damage event is
    /// queued 4–18 game-hours out. Consumes the CRITTER_RADIATED flag.</summary>
    private void RadMidnightCheck()
    {
        if (_dude is not { } d || !_dudeRadiated)
            return;
        int oldLevel = _radEvents.Count > 0 ? _radEvents[^1].Level : 0; // _old_rad_level (last-traversed)
        int level = Formats.Combat.RadiationTables.CounterToLevel(d.Dude.Radiation);
        int end = GetCritterState(d.Dude)?.Stat(2) ?? 5; // STAT_ENDURANCE
        // statRoll <= ROLL_FAILURE ⇔ randomBetween(1,10) > END + modifier[level] (stat.cc:708).
        if (_combatRng.Next(1, 11) > end + Formats.Combat.RadiationTables.EnduranceModifiers[level])
            level++;
        if (level > oldLevel)
            _radEvents.Add((_clock.Ticks + (long)_combatRng.Next(4, 19) * Formats.GameClock.TicksPerHour,
                level, false));
        _dudeRadiated = false; // proto flag consumed (critter.cc:536)
    }

    /// <summary>_process_rads (critter.cc:566): apply (or, when healing, undo) the level's stat-penalty
    /// band to the dude's BONUS stats + current HP, then the primary-stat death check.</summary>
    private void ApplyRadEffect(int level, bool isHealing)
    {
        if (_dude is not { } d || _dudeGcd is null || level == 0)
            return;
        int idx = level - 1; // critter.cc:574
        if (idx < 0 || idx >= Formats.Combat.RadiationTables.EffectPenalties.Length)
            return;
        int modifier = isHealing ? -1 : 1;
        int[] penalties = Formats.Combat.RadiationTables.EffectPenalties[idx];
        int[] stats = Formats.Combat.RadiationTables.EffectStats;
        for (int e = 0; e < stats.Length; e++)
        {
            int delta = modifier * penalties[e];
            if (delta == 0)
                continue;
            int stat = stats[e];
            if (stat == 35) // CURRENT_HIT_POINTS
            {
                int max = GetCritterState(d.Dude)?.MaxHp ?? d.Dude.CurrentHp;
                d.Dude.CurrentHp = Math.Clamp(d.Dude.CurrentHp + delta, 0, max);
            }
            else if (stat <= 34)
            {
                _dudeGcd.Stats.BonusStats[stat] += delta;
                _radBonus[stat] += delta;
            }
        }

        // Death check (critter.cc:599): not on heal; any of the first 6 primaries base+bonus < 1.
        if (isHealing || _combat.IsGameOver)
            return;
        for (int e = 0; e < Formats.Combat.RadiationTables.PrimaryStatCount; e++)
            if ((GetCritterState(d.Dude)?.Stat(stats[e]) ?? 5) < 1)
            {
                GameOver();
                return;
            }
    }

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

    /// <summary>ICombatHost (P84): the Easy/Normal/Hard combat-difficulty damage modifier (75/100/125),
    /// honouring the same <see cref="Difficulty"/> that drives the worldmap. The engine applies it to
    /// damage dealt by off-team attackers only (the gate lives in CombatEngine); Normal = 100 = identity.</summary>
    public int CombatDifficultyDamageModifier => Formats.Combat.CombatDifficulty.DamageModifier(Difficulty);

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

    // --- P113 (Stage 0): perception/light/door/hidden-item host data ---
    public int DudeSneakSkill => DudeSkillValue(8); // SKILL_SNEAK
    public bool DudeIsActivelySneaking => _sneak.IsSneaking;
    public bool DudeHasSneakFlag => _sneak.FlagSet;
    /// <summary>objectGetLightIntensity (object.cc:1748) — max(tile, ambient) clamp lives inside
    /// LightGrid.GetTileIntensity; the darkness modifier only ever queries non-dude defenders, so the
    /// dude-self-light subtraction (dude-only in fo2ce) is not needed here.</summary>
    public int LightIntensityAt(MapObject critter) => _lightGrid.GetTileIntensity(critter.HexTile);
    /// <summary>The pathfinder canUseDoor exemption for combat movers (animation.cc:1802-1808) —
    /// same non-dude rules as NPC walkers (unlocked scenery door, biped/robotic, non-gecko).</summary>
    public bool IsPassableClosedDoor(MapObject mover, int tile) => NpcUsableClosedDoorAt(mover, tile) is not null;
    /// <summary>ITEM_HIDDEN (proto item extendedFlags 0x08000000; item.cc:1133).</summary>
    public bool ItemIsHidden(MapObject item)
    {
        if (Fid.PidType(item.Pid) != (int)ObjectType.Item)
            return false;
        try { return (_protos.Get(item.Pid).ExtendedFlags & 0x08000000) != 0; }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException) { return false; }
    }

    public bool IsAnimating(MapObject critter) => _animator.TryGetState(critter, out _);
    public bool IsFallInProgress(MapObject critter) =>
        _animator.TryGetState(critter, out AnimationState state) && !state.Finished;
    public bool IsAnyWalkerMoving() => _npcWalkers.Values.Any(w => w.Moving);
    public bool IsWalkerMoving(MapObject critter) =>
        _npcWalkers.TryGetValue(critter, out DudeController? w) && w.Moving;
    public bool StartWalk(MapObject critter, int targetTile, bool run = false) => StartNpcWalk(critter, targetTile, run);

    /// <summary>ICombatHost (P117): the critters.lst run flag (art.cc artCritterFidShouldRun) —
    /// gates the AI approach's run request.</summary>
    public bool CritterShouldRun(MapObject critter) => _artIndex.CritterShouldRun(critter.Fid);

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
        PlayWeaponSfx(weaponProto, thrower);
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
            // The scenery reads target_obj (the marker) → metarule(49) → EXPLOSION, so the marker MUST be
            // the script's TARGET (fo2ce _scr_explode_scenery), not source/dude — RunExplosionDamage sets it.
            var scripted = _scriptHost?.RunExplosionDamage(obj, _map, marker, _dude?.Dude);
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
    private Formats.Int.RegAnimSequencer? _regAnimSeq;
    private MapObject? _regAnimBlockOn; // the async action the queue is waiting to finish

    private void ExecuteRegAnim(IReadOnlyList<Formats.Int.RegAnimAction> actions)
    {
        if (_combat.Phase != Formats.Combat.CombatPhase.Idle)
            return;

        // P114: dispatch sequentially — start only the head now; AdvanceRegAnimQueue (in UpdateAmbientLife)
        // starts each following action once the prior one completes + its delay elapses. A single-action
        // batch is identical to the old all-at-once foreach (the head fires immediately).
        _regAnimSeq = new Formats.Int.RegAnimSequencer(actions);
        _regAnimBlockOn = null;
        if (_regAnimSeq.Begin() is { } head)
            StartRegAnimAction(head);
    }

    /// <summary>P114: advance the sequential reg_anim queue — dispatch the next action once the current
    /// async action (a walk) has finished and its delay counted down. Called each frame from ambient life.</summary>
    private void AdvanceRegAnimQueue(double elapsedMs)
    {
        if (_regAnimSeq is not { } seq)
            return;
        bool blockerActive = _regAnimBlockOn is { } b && _npcWalkers.ContainsKey(b);
        if (seq.Advance(blockerActive, elapsedMs) is { } next)
            StartRegAnimAction(next);
        // The batch is fully done once every action dispatched AND the last mover finished.
        if (seq.Done && (_regAnimBlockOn is not { } last || !_npcWalkers.ContainsKey(last)))
        {
            _regAnimSeq = null;
            _regAnimBlockOn = null;
        }
    }

    /// <summary>Start ONE reg_anim action (walk / animate) + emit its ordered status line. Sets the queue's
    /// block target to the walker for a move action (the queue waits for it), null for a fire-and-continue
    /// animate.</summary>
    private void StartRegAnimAction(Formats.Int.RegAnimAction a)
    {
        switch (a.Kind)
        {
            case Formats.Int.RegAnimKind.MoveToTile:
            case Formats.Int.RegAnimKind.RunToTile:
            {
                // P117: a scripted reg_anim_obj_RUN_to_tile finally runs (was funneled into walk).
                bool started = StartNpcWalk(a.Object, a.Tile, a.Kind == Formats.Int.RegAnimKind.RunToTile);
                _regAnimMoves.Add(
                    $"{ObjectName(a.Object)}@{a.Object.HexTile}->{a.Tile}:"
                    + $"{(a.Kind == Formats.Int.RegAnimKind.RunToTile ? "run" : "walk")}:{(started ? "ok" : "no")}");
                _regAnimBlockOn = started ? a.Object : null;
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
                bool started = dest >= 0
                    && StartNpcWalk(a.Object, dest, a.Kind == Formats.Int.RegAnimKind.RunToObject);
                _regAnimMoves.Add(
                    $"{ObjectName(a.Object)}@{a.Object.HexTile}->obj@{dest}:"
                    + $"{(a.Kind == Formats.Int.RegAnimKind.RunToObject ? "run" : "walk")}:{(started ? "ok" : "no")}");
                _regAnimBlockOn = started ? a.Object : null;
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
                _regAnimBlockOn = null; // looping/instant animate → don't block the queue
                break;
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
            PlayWeaponSfx(weaponProto, attacker);
        // Unarmed/melee swing grunt (actions.cc:625 sfxBuildCharName(attacker, ANIM_THROW_PUNCH, CONTACT)) —
        // a wielded weapon plays its own sfx above instead (P34-M5).
        else if (Formats.Sound.SfxName.CharName(_artIndex.CritterBaseName(attacker.Fid), 16 /*ANIM_THROW_PUNCH*/,
                     Formats.Sound.SfxName.CharacterSoundEffect.Contact, Fid.WeaponCode(attacker.Fid)) is { } swing)
            _audio?.PlaySfx(swing, SfxGain(attacker));
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
            _audio?.PlaySfx(grunt, SfxGain(target));

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
            _audio?.PlaySfx(scream, SfxGain(critter));
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

    public bool RunMapCombatOver(int knockedOutByTeam) =>
        _map is not null && (_scriptHost?.RunMapCombatOver(_map, _dude?.Dude, knockedOutByTeam)?.Overridden ?? false);

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
