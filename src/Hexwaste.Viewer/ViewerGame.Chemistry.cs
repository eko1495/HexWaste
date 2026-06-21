using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Viewer;

// Drugs + addiction/withdrawal (P37/P38): the immediate SPECIAL boost, the timed wear-off queue,
// the addiction roll, and the onset/recovery withdrawal chain. Pure move from ViewerGame.cs; the
// _drugBonus/_withdrawalBonus/_pending* state stays central in the main file.
public sealed partial class ViewerGame
{
    private void UseDrug(MapObject item, DrugProtoStats drug)
    {
        if (!_combat.TryUseActionPoints(2))
            return;

        // ported from item.cc _item_d_take_drug (:2809): the immediate effect, then schedule the two
        // delayed kicks (the down-ramp + restore that net to zero = the wear-off). P37.
        int hpBefore = _dude?.Dude.CurrentHp ?? 0;
        bool changed = ApplyDrugEffect(drug.Stats, drug.Amounts, immediate: true);
        ScheduleDrugEvent(drug.Duration1, drug.Stats, drug.Amount1);
        ScheduleDrugEvent(drug.Duration2, drug.Stats, drug.Amount2);
        TryAddict(item, drug); // P38: the _item_d_take_drug addiction tail (item.cc:2822)

        if (changed && _dude is not null && _dude.Dude.CurrentHp != hpBefore)
            Log($"You gain {_dude.Dude.CurrentHp - hpBefore} hit points.");
        else if (!changed)
            Log("Nothing happens."); // item.cc:2714 msg-10 (no effect applied)
        Console.WriteLine($"drug: {ObjectName(item)} applied (hp {_dude?.Dude.CurrentHp ?? 0})");

        item.StackCount--;
        if (item.StackCount <= 0)
            _dudeInventory.Remove(item);
    }

    /// <summary>The active drug contribution to BonusStats[0..34] (per stat). Tracked separately so it can
    /// be RE-APPLIED on load — LoadGame rebuilds the sheet (base + worn armor) and would otherwise lose the
    /// drug bonus while the pending wear-off reversals still fire → negative stats. (P37.)</summary>
    private readonly int[] _drugBonus = new int[35];

    /// <summary>Pending delayed drug kicks (the down-ramp / wear-off), keyed by the game-tick they fire at.
    /// ported from item.cc's EVENT_TYPE_DRUG queue; driven from UpdateClock like the poison tick. (P37.)</summary>
    private readonly List<(long FireTick, int[] Stats, int[] Amounts)> _pendingDrugEvents = [];

    /// <summary>
    /// ported from item.cc _perform_drug_effect (:2639): additively apply a drug's per-stat amounts.
    /// stats[0] == -2 → the first real stat (stats[1]) takes a random range amounts[0]..amounts[1]
    /// (immediate only; the stimpak heal). stat 35 = current HP (heal/cost, clamped, GameOver on ≤0);
    /// 0..34 = a SPECIAL/derived BonusStats bonus (mirrored into _drugBonus for save re-apply). stats ≥36
    /// (poison/rad counters) are out of scope (documented — only Mentats' minor rad bump). Returns whether
    /// anything changed (the "Nothing happens" gate). The -2 random roll is the ONLY RNG draw.
    /// </summary>
    private bool ApplyDrugEffect(int[] stats, int[] amounts, bool immediate)
    {
        if (_dude is null || _dudeGcd is null)
            return false;
        bool firstStatIsMinimum = stats[0] == -2;
        bool changed = false;
        for (int i = firstStatIsMinimum ? 1 : 0; i < 3; i++)
        {
            int stat = stats[i];
            if (stat < 0)
                continue;
            int amt = firstStatIsMinimum && i == 1
                ? (immediate ? _combatRng.Next(amounts[0], amounts[1] + 1) : amounts[i])
                : amounts[i];
            if (stat == 35) // current HP
            {
                int before = _dude.Dude.CurrentHp;
                int max = GetCritterState(_dude.Dude)?.MaxHp ?? before;
                _dude.Dude.CurrentHp = Math.Clamp(before + amt, 0, max);
                if (_dude.Dude.CurrentHp != before)
                    changed = true;
                if (_dude.Dude.CurrentHp <= 0 && !_combat.IsGameOver)
                    GameOver(); // a drug-cost HP delta can kill (Super Stimpak)
            }
            else if (stat <= 34 && amt != 0)
            {
                _dudeGcd.Stats.BonusStats[stat] += amt;
                _drugBonus[stat] += amt;
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Schedule a delayed drug kick durationMin game-minutes out (item.cc _insert_drug_effect:
    /// skip an all-zero kick; delay = 600 ticks/game-minute, the GameClock basis). (P37.)</summary>
    private void ScheduleDrugEvent(int durationMin, int[] stats, int[] amounts)
    {
        if (amounts[0] == 0 && amounts[1] == 0 && amounts[2] == 0)
            return; // item.cc:2601 — an unused kick schedules nothing
        _pendingDrugEvents.Add((_clock.Ticks + 600L * durationMin, stats, amounts));
    }

    /// <summary>Fire every due drug kick in fire-time order (a clock JUMP from rest/travel fires several);
    /// each is a wear-off delta (no RNG). ported from item.cc drugEffectEventProcess. Driven by UpdateClock.</summary>
    private void ProcessDrugs()
    {
        if (_pendingDrugEvents.Count == 0)
            return;
        while (true)
        {
            int next = -1;
            long earliest = long.MaxValue;
            for (int i = 0; i < _pendingDrugEvents.Count; i++)
                if (_pendingDrugEvents[i].FireTick <= _clock.Ticks && _pendingDrugEvents[i].FireTick < earliest)
                    (earliest, next) = (_pendingDrugEvents[i].FireTick, i);
            if (next < 0)
                return;
            (long _, int[] stats, int[] amounts) = _pendingDrugEvents[next];
            _pendingDrugEvents.RemoveAt(next);
            ApplyDrugEffect(stats, amounts, immediate: false);
        }
    }

    // ─── P38: drug addiction + withdrawal (item.cc) ──────────────────────────────────────────

    /// <summary>The active WITHDRAWAL stat penalty contribution to BonusStats[0..34] (per stat).
    /// Tracked separately so it can be RE-APPLIED after the load-time sheet rebuild — exactly the
    /// DrugBonus trap — else a pending recovery would reverse a penalty that was never re-folded.</summary>
    private readonly int[] _withdrawalBonus = new int[35];

    /// <summary>Pending withdrawal events: the absolute game-tick, IsStart (symptom onset vs recovery),
    /// the drug pid, and the addiction "perk" (the withdrawal stat-penalty perk index). ported from
    /// item.cc's EVENT_TYPE_WITHDRAWAL queue; drained from UpdateClock like the drug/poison ticks.</summary>
    private readonly List<(long FireTick, bool IsStart, int Pid, int Perk)> _pendingWithdrawalEvents = [];

    /// <summary>A dedicated seeded RNG for the addiction roll, isolated off the combat/worldmap/skill
    /// streams (the _sneakRng/_partyRng pattern) — so giving/looting/using a chem never perturbs them.</summary>
    private Formats.Combat.ICombatRng? _addictionRng;

    /// <summary>The _item_d_take_drug addiction tail (item.cc:2822-2846), dude-only: if the drug is
    /// addictive and the dude isn't already addicted to it, roll on the isolated RNG; on success set
    /// the addiction GVAR (dudeSetAddiction) and schedule the symptom-onset withdrawal event.</summary>
    private void TryAddict(MapObject item, DrugProtoStats drug)
    {
        // The dude is the only addiction subject (the engine's critter==gDude gate); UseDrug only ever
        // runs for the dude's bag.
        if (_dude is null || _scriptHost is null)
            return;
        int gvar = Formats.Item.DrugAddiction.GvarForPid(item.Pid);
        if (gvar < 0)
            return; // not an addictive drug
        if (_scriptHost.GlobalVars.GetValueOrDefault(gvar, 0) != 0)
            return; // dudeIsAddicted — no re-roll while already hooked

        bool reliant = DudeHasTrait(Formats.Combat.TraitModifiers.ChemReliant);
        bool resistant = DudeHasTrait(Formats.Combat.TraitModifiers.ChemResistant);
        bool flowerChild = DudePerkRank(Formats.Perks.PerkId.FlowerChild) > 0;
        _addictionRng ??= new Formats.Combat.SystemCombatRng(RngSeed ?? Environment.TickCount);
        int roll = _addictionRng.Next(1, 101); // randomBetween(1, 100), inclusive

        if (Formats.Item.DrugAddiction.Roll(drug.AddictionChance, reliant, resistant, flowerChild, roll))
        {
            _scriptHost.GlobalVars[gvar] = 1; // dudeSetAddiction (item.cc:3106)
            ScheduleWithdrawal(isStart: true, drug.WithdrawalOnset, drug.WithdrawalEffect, item.Pid);
        }
    }

    /// <summary>Schedule a withdrawal event durationMin game-minutes out (item.cc _insert_withdrawal:
    /// queueAddEvent(600 * duration, …); 600 ticks/game-minute = the GameClock basis).</summary>
    private void ScheduleWithdrawal(bool isStart, int durationMin, int perk, int pid) =>
        _pendingWithdrawalEvents.Add((_clock.Ticks + 600L * durationMin, isStart, pid, perk));

    /// <summary>Fire every due withdrawal event in fire-time order (a clock JUMP fires onset-then-recovery
    /// in order). ported from item.cc withdrawalEventProcess (:2974): onset → apply the perk penalty +
    /// schedule recovery 7 game-days out (halved by Chem Reliant / Flower Child); recovery → reverse the
    /// penalty + clear the addiction GVAR, EXCEPT a Jet addiction which is PERMANENT (returns early,
    /// cleared only by the Jet antidote). Driven by UpdateClock. No RNG.</summary>
    private void ProcessWithdrawals()
    {
        if (_pendingWithdrawalEvents.Count == 0)
            return;
        while (true)
        {
            int next = -1;
            long earliest = long.MaxValue;
            for (int i = 0; i < _pendingWithdrawalEvents.Count; i++)
                if (_pendingWithdrawalEvents[i].FireTick <= _clock.Ticks && _pendingWithdrawalEvents[i].FireTick < earliest)
                    (earliest, next) = (_pendingWithdrawalEvents[i].FireTick, i);
            if (next < 0)
                return;
            (long fireTick, bool isStart, int pid, int perk) = _pendingWithdrawalEvents[next];
            _pendingWithdrawalEvents.RemoveAt(next);

            if (isStart) // performWithdrawalStart (item.cc:3039)
            {
                ApplyWithdrawalPerk(perk, +1);
                int duration = 10080; // 7 game-days
                if (DudeHasTrait(Formats.Combat.TraitModifiers.ChemReliant)) duration /= 2;
                if (DudePerkRank(Formats.Perks.PerkId.FlowerChild) > 0) duration /= 2;
                // Schedule recovery from the ONSET's fire instant (the engine's queue fires events at
                // their scheduled tick), NOT the post-jump clock — else a big --addict-probe jump would
                // push recovery past where it should be (the same fire-instant rule as ProcessPoison).
                _pendingWithdrawalEvents.Add((fireTick + 600L * duration, false, pid, perk));
            }
            else // withdrawalEventProcess recovery branch (item.cc:2980)
            {
                if (perk == Formats.Perks.PerkId.JetAddiction)
                    continue; // Jet withdrawal is PERMANENT until the antidote
                ApplyWithdrawalPerk(perk, -1);
                int gvar = Formats.Item.DrugAddiction.GvarForPid(pid); // dudeClearAddiction
                if (gvar >= 0 && _scriptHost is not null)
                    _scriptHost.GlobalVars[gvar] = 0;
            }
        }
    }

    /// <summary>Apply a withdrawal perk's maxRank==-1 stat fold (perkAddEffect/perkRemoveEffect) into
    /// BonusStats + the tracked _withdrawalBonus, sign +1 on onset / -1 on recovery. NEVER touches
    /// _dudePerkRanks (the engine's perkAddEffect mutates bonus stats directly, never the rank).</summary>
    private void ApplyWithdrawalPerk(int perk, int sign)
    {
        if (_dudeGcd is null)
            return;
        foreach ((int stat, int delta) in Formats.Perks.PerkRules.MaxRankPerkEffect(perk))
        {
            if (stat < 0 || stat >= 35)
                continue;
            _dudeGcd.Stats.BonusStats[stat] += sign * delta;
            _withdrawalBonus[stat] += sign * delta;
        }
    }
}
