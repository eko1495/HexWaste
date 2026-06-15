using Hexwaste.Formats;
using Hexwaste.Formats.Map;

namespace Hexwaste.Viewer;

// Party / dismissed-companion lifecycle (phase-10 M4/M5, #14 split out of the
// ViewerGame monolith). Behaviour-preserving: same sealed class, same private
// fields — only the source location moved. The roster, the per-map dismissed
// bodies and the companion-hub viewer-state all live as ViewerGame fields; this
// file owns the methods that mutate them across new-game / load / map-transition.
public sealed partial class ViewerGame
{
    /// <summary>Clear the whole roster + companion viewer-state — on new game and
    /// before a load rebuilds it. These collections key on MapObject identity, so old
    /// entries would dangle after the party is rebuilt with fresh instances (phase-10 M4).</summary>
    private void ResetParty()
    {
        _scriptHost?.PartyMembers.Clear();
        _partyScriptIndex.Clear();
        _waitingCompanions.Clear();
        _dismissedCompanions.Clear();
        _dismissedByMap.Clear();
        _originalTeam.Clear();
        _companionLevelState.Clear();
        _companionStatOverride.Clear();
        _companionHub = null;
        if (_tradePartner is not null) // a trade panel pointed at a follower we're clearing
            _lootContainer = null;
        _tradePartner = null;
    }

    private void OnPartyChanged(MapObject critter, bool joined)
    {
        if (joined)
        {
            _originalTeam.TryAdd(critter, critter.Team); // remember the pre-recruit team for dismiss
            critter.Team = 0; // the dude's team (scripts also critter_add_trait it)
            critter.WhoHitMeCid = 0;
            _combat.RemoveHostile(critter);
            if (critter.Sid != -1 && _map.ScriptsBySid.TryGetValue(critter.Sid, out MapScriptRecord? record))
                _partyScriptIndex[critter] = record.ScriptListIndex;
            Log($"{ObjectName(critter)} joins you.");
            Console.WriteLine($"party: {ObjectName(critter)} joined (script {_partyScriptIndex.GetValueOrDefault(critter, -1)})");
        }
        else
        {
            _partyScriptIndex.Remove(critter);
            Log($"{ObjectName(critter)} leaves.");
            Console.WriteLine($"party: {ObjectName(critter)} left");
        }
    }

    /// <summary>Companions travel OUTSIDE the per-map deltas: pulled from the
    /// outgoing map before capture (their ordinals read as taken) and
    /// injected next to the dude after the new map's delta applies.</summary>
    private void ExtractPartyFromMap()
    {
        if (_scriptHost is null)
            return;
        foreach (MapObject member in _scriptHost.PartyMembers)
        {
            foreach (MapElevation? elev in _map.Elevations)
                elev?.Objects.Remove(member);
            foreach (List<MapObject> list in _flatObjects.Concat(_solidObjects))
                list.Remove(member);
            _npcWalkers.Remove(member);
            _homeTiles.Remove(member);
            // "Wait here" is per-map: a member that travels with you resumes following
            // on the new map rather than freezing where InjectPartyMembers drops it.
            _waitingCompanions.Remove(member);
        }
    }

    private void InjectPartyMembers()
    {
        if (_scriptHost is null || _dude is null || _map.Elevations[_elevation] is not { } elev)
            return;
        foreach (MapObject member in _scriptHost.PartyMembers)
        {
            int spawn = _dude.Dude.HexTile;
            for (int rotation = 0; rotation < 6; rotation++)
            {
                int candidate = Formats.Hex.HexGrid.TileInDirection(_dude.Dude.HexTile, rotation);
                if (!_blockedTiles.Contains(candidate))
                {
                    spawn = candidate;
                    break;
                }
            }

            member.HexTile = spawn;
            // Fresh script binding on this map so the follow critter_p_proc
            // keeps running (sids are per-map).
            if (_partyScriptIndex.TryGetValue(member, out int scriptIndex) && scriptIndex >= 0)
                member.Sid = _scriptHost.AllocateSid(_map, scriptIndex);
            elev.Objects.Add(member);
            if (!_solidObjects[_elevation].Contains(member))
                InsertSorted(_solidObjects[_elevation], member);
        }

        if (_scriptHost.PartyMembers.Count > 0)
            RebuildBlockedTiles(_dude.Dude);
    }

    private static SaveState.SavedItem ToSavedItem(MapObject i) =>
        new(i.Pid, Math.Max(i.StackCount, 1),
            i.Flags & (MapObject.FlagInLeftHand | MapObject.FlagInRightHand | MapObject.FlagWorn),
            i.AmmoQuantity, i.AmmoTypePid);

    /// <summary>Snapshot the current map's live dismissed bodies into the persisted
    /// per-map roster (P10 #3) — so a save, or leaving the map, remembers them. Replaces
    /// the map's entry (the live set IS the truth for this map right now).</summary>
    private void SyncDismissedToRoster()
    {
        if (_dismissedCompanions.Count == 0)
        {
            _dismissedByMap.Remove(_map.Header.Name);
            return;
        }
        _dismissedByMap[_map.Header.Name] = [.. _dismissedCompanions.Select(kv =>
            new SaveState.DismissedCompanion(kv.Key.Pid, kv.Value, kv.Key.HexTile, _elevation,
                kv.Key.Rotation, kv.Key.CurrentHp, kv.Key.Team, [.. kv.Key.Inventory.Select(ToSavedItem)]))];
    }

    /// <summary>On map exit: persist the dismissed bodies, then pull them off the live
    /// map so they're not captured in the delta (re-injected on return).</summary>
    private void ExtractDismissedFromMap()
    {
        SyncDismissedToRoster();
        foreach (MapObject body in _dismissedCompanions.Keys)
        {
            foreach (MapElevation? elev in _map.Elevations)
                elev?.Objects.Remove(body);
            foreach (List<MapObject> list in _flatObjects.Concat(_solidObjects))
                list.Remove(body);
        }
        _dismissedCompanions.Clear();
    }

    /// <summary>On map entry: recreate this map's dismissed companions as inert,
    /// rejoinable bodies from the persisted roster (P10 #3).</summary>
    private void InjectDismissedFromRoster()
    {
        if (_map.Elevations[_elevation] is not { } elev
            || !_dismissedByMap.TryGetValue(_map.Header.Name, out List<SaveState.DismissedCompanion>? roster))
            return;
        foreach (SaveState.DismissedCompanion d in roster)
        {
            if (RebuildObject(d.Pid, 1) is not { } body)
                continue;
            body.HexTile = d.Tile;
            body.Rotation = Math.Clamp(d.Rotation, 0, 5);
            body.CurrentHp = d.Hp;
            body.Team = d.Team;
            body.Sid = -1; // inert until rejoined
            foreach (SaveState.SavedItem it in d.Inventory)
                if (RebuildObject(it.Pid, it.Count) is { } obj)
                {
                    obj.Flags |= it.Flags;
                    obj.AmmoQuantity = it.AmmoQuantity;
                    obj.AmmoTypePid = it.AmmoTypePid;
                    body.Inventory.Add(obj);
                }
            elev.Objects.Add(body);
            InsertSorted(_solidObjects[_elevation], body);
            _dismissedCompanions[body] = d.ScriptListIndex;
        }
        if (roster.Count > 0 && _dude is not null)
            RebuildBlockedTiles(_dude.Dude); // a dismissed body blocks its tile like any NPC
    }
}
