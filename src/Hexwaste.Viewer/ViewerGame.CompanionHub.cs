using Hexwaste.Formats;
using Hexwaste.Formats.Map;

namespace Hexwaste.Viewer;

// Companion control hub + 1:1 trade panel (phase-10 M4/M5, #14 split out of the
// ViewerGame monolith — sibling to ViewerGame.Party.cs's roster lifecycle).
// Behaviour-preserving: same sealed class, same private fields (the hub/trade
// state — _companionHub, _hubOptions, _tradePartner, CompanionCmd — and the shared
// roster collections all live as ViewerGame fields). This file owns the player-
// facing interaction: talking to a companion opens the hub; the hub's options
// route to wait/follow/dismiss/rejoin and the flat give/trade transfer.
public sealed partial class ViewerGame
{
    /// <summary>Open the 1:1 companion trade panel (phase-10 M5): the loot panel
    /// pointed at the follower's inventory, in TRADE mode. Engine party-member trade is
    /// a flat move at barter-modifier 0 (game_dialog.cc:3757) — no caps, no price — so
    /// this deliberately bypasses the priced-barter path entirely.</summary>
    private void OpenTrade(MapObject follower)
    {
        _companionHub = null;
        _tradePartner = follower;
        _lootContainer = follower; // reuse the loot panel's take path (TakeFromContainer)
        _panelPage = 0;
        PrewarmItemTextures(follower.Inventory);
        PrewarmItemTextures(_dudeInventory);
        Log($"Trading with {ObjectName(follower)}.");
        Console.WriteLine($"trade: open with {ObjectName(follower)} (theirs={follower.Inventory.Count} yours={_dudeInventory.Count})");
    }

    /// <summary>Trade give-side: move one stack from the dude to the follower, flat (no
    /// caps) — the only transfer the loot panel didn't already have (phase-10 M5).</summary>
    private void GiveToFollower(int index)
    {
        if (_tradePartner is null || index < 0 || index >= _dudeInventory.Count)
            return;
        MapObject item = _dudeInventory[index];
        _dudeInventory.RemoveAt(index);
        UnequipForTransfer(item); // don't leave the worn-armor bonus on the dude
        // Merge same-Pid stacks like every other inbound add (AddToDudeInventory/itemAdd) —
        // ammo boxes consolidate their rounds (P75-M2).
        if (_tradePartner.Inventory.FirstOrDefault(i => i.Pid == item.Pid) is { } existing)
            MergeStackInto(existing, item);
        else
            _tradePartner.Inventory.Add(item);
        Log($"You give {ObjectName(item)}{(item.StackCount > 1 ? $" x{item.StackCount}" : "")} to {ObjectName(_tradePartner)}.");
    }

    /// <summary>Build the companion-control hub options for a member (phase-10 M4).
    /// In-party: wait/follow toggle + dismiss; dismissed: rejoin. Always a cancel.
    /// A viewer hub rather than the engine's per-script dialog nodes — robust, reusable
    /// for ANY companion (incl. encounter-spawned allies), no partymbr.msg routing.</summary>
    private void OpenCompanionHub(MapObject member)
    {
        _companionHub = member;
        _hubOptions.Clear();
        if (_scriptHost?.PartyMembers.Contains(member) ?? false)
        {
            _hubOptions.Add(("Talk to them.", CompanionCmd.Talk));
            _hubOptions.Add(("Let's trade.", CompanionCmd.Trade));
            _hubOptions.Add(_waitingCompanions.Contains(member)
                ? ("Let's go. (follow me)", CompanionCmd.Follow)
                : ("Wait here.", CompanionCmd.Wait));
            _hubOptions.Add(("Set your tactics. (combat control)", CompanionCmd.Tactics));
            _hubOptions.Add(("It's time for us to part ways. (dismiss)", CompanionCmd.Dismiss));
        }
        else // a dismissed former companion still standing on the map
        {
            _hubOptions.Add(("Join me again.", CompanionCmd.Rejoin));
        }
        _hubOptions.Add(("Never mind.", CompanionCmd.Cancel));

        Console.WriteLine($"companion-hub: {ObjectName(member)} options=[{string.Join(" | ", _hubOptions.Select(o => o.Label))}]");
    }

    private void ChooseCompanionOption(int index)
    {
        if (_companionHub is not { } member || index < 0 || index >= _hubOptions.Count)
            return;
        CompanionCmd cmd = _hubOptions[index].Cmd;
        _companionHub = null;

        switch (cmd)
        {
            case CompanionCmd.Talk:
                OpenScriptedDialog(member); // companion quest/banter dialog
                break;
            case CompanionCmd.Trade:
                OpenTrade(member);
                break;
            case CompanionCmd.Wait:
                _waitingCompanions.Add(member);
                Log($"{ObjectName(member)} will wait here.");
                Console.WriteLine($"companion: {ObjectName(member)} waiting");
                break;
            case CompanionCmd.Follow:
                _waitingCompanions.Remove(member);
                Log($"{ObjectName(member)} follows you again.");
                Console.WriteLine($"companion: {ObjectName(member)} following");
                break;
            case CompanionCmd.Dismiss:
                DismissCompanion(member);
                break;
            case CompanionCmd.Rejoin:
                RejoinCompanion(member);
                break;
            case CompanionCmd.Tactics:
                OpenTactics(member); // P50: the combat-control / AI-disposition window
                break;
            case CompanionCmd.Cancel:
                break;
        }
    }

    /// <summary>party_remove + restore the saved team + stop following (sid cleared so
    /// the follow critter_p_proc no longer runs); the body stays on the map for a
    /// same-session rejoin (phase-10 M4).</summary>
    private void DismissCompanion(MapObject member)
    {
        if (_scriptHost is null)
            return;
        int scriptIndex = _partyScriptIndex.GetValueOrDefault(member, -1);
        _scriptHost.PartyMembers.Remove(member);
        OnPartyChanged(member, joined: false); // clears _partyScriptIndex, logs "leaves"
        _waitingCompanions.Remove(member);
        member.Team = _originalTeam.GetValueOrDefault(member, member.Team); // Vic → 25, etc.
        member.Sid = -1; // halt the follow loop — an inert NPC again
        if (scriptIndex >= 0)
            _dismissedCompanions[member] = scriptIndex;
        Log($"{ObjectName(member)} parts ways.");
        Console.WriteLine($"companion: {ObjectName(member)} dismissed team={member.Team} count={Formats.Int.ScriptHost.PartyMemberCount(_scriptHost.PartyMembers)}");
    }

    /// <summary>Re-recruit a dismissed companion (alive-gated, like the engine's REJOIN
    /// node): rebind its follow script and add it back to the roster (phase-10 M4).</summary>
    private void RejoinCompanion(MapObject member)
    {
        if (_scriptHost is null)
            return;
        if (member.IsDead)
        {
            Log($"{ObjectName(member)} is in no state to travel.");
            return;
        }
        int scriptIndex = _dismissedCompanions.GetValueOrDefault(member, -1);
        _dismissedCompanions.Remove(member);
        if (scriptIndex >= 0)
            member.Sid = _scriptHost.AllocateSid(_map, scriptIndex); // rebind before OnPartyChanged reads it
        _scriptHost.PartyMembers.Add(member);
        OnPartyChanged(member, joined: true);
        Console.WriteLine($"companion: {ObjectName(member)} rejoined count={Formats.Int.ScriptHost.PartyMemberCount(_scriptHost.PartyMembers)}");
    }
}
