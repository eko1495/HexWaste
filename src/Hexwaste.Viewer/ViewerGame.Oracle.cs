using Hexwaste.Formats.Combat;

namespace Hexwaste.Viewer;

// Trace-oracle RNG classification (fo2ce has ONE stream; Hexwaste had many). Inventory from
//   grep -n "SystemCombatRng(\|new Random(\|ICombatRng\|Random?" src/Hexwaste.Viewer/*.cs src/Hexwaste.Formats/**/*.cs
// Rule (docs/research-notes/fo2ce-rng-callsite-audit-2026-10.md): cosmetic = wall-clock-timed AND
// visual/audio-only; everything else (ambiguous included) = logic.
//
//   field (construction site)                      class     reason
//   _combatRng        ViewerGame.cs:139/:1195      logic     to-hit/damage/crit/AI + Chemistry/rad rolls (combat.cc, combat_ai.cc, item.cc)
//   _calledShotRng    ViewerGame.cs:1196           logic     AI called-shot roll (combat_ai.cc:2641/2656 _ai_called_shot)
//   _sneakRng         ViewerGame.cs:3870           logic     sneak skill roll (skill.cc)
//   _stealRng         ViewerGame.cs:3970           logic     steal roll (skill.cc skillsPerformStealing)
//   _partyRng         ViewerGame.cs:5039           logic     party member level-up (partyMemberIncLevels)
//   _wmRng            ViewerGame.cs:5484, Worldmap.cs:89, Harness.cs:1804
//                                                  logic     worldmap encounter rolls (worldmap.cc)
//   _skillRng         ViewerGame.cs:5720/:7214/:7266
//                                                  logic     skill use / item depletion rolls (skill.cc, randomBetween(1,10))
//   _tauntRng         CombatHost.cs:942            logic     combat taunts (combat_ai.cc:3324/3367 _combatai_msg — audit: logic)
//   _addictionRng     Chemistry.cs:247             logic     addiction roll (item.cc:2822 _item_d_take_drug) — missing from the brief's list
//   _scriptHost.Rng   ViewerGame.cs:1528           logic     do_check / statRoll (roll.cc via scripts)
//   IntVm random()    IntVm.cs:629/:723            logic     interpreter_extra.cc opRandom :782 -> randomBetween (via ScriptHost.SharedVmRandom)
//   _ambientRng       ViewerGame.cs:1982           logic     per-map ambient sfx index roll + interval. fo2ce's equivalent
//                                                            (game_sound.cc:2125 ambientSoundEffectEventProcess, wmSfxRollNextIdx)
//                                                            rides the game-time event queue => audit class LOGIC, so it shares
//                                                            the stream. NOTE: Hexwaste schedules it off wall time in Update(),
//                                                            skips it in combat and without audio (always, under --no-audio), so it
//                                                            never draws in an oracle run while fo2ce does — an expected finding.
//   _fidgetRng        ViewerGame.cs:6455           cosmetic  talking-head fidget (game_dialog.cc:2505/:2865 — audit: cosmetic)
//   _ambientRandom    ViewerGame.cs:381            cosmetic  critter idle fidget = _dude_fidget (animation.cc:3071/:3109 — audit: cosmetic).
//                     (fixed seed 20260612)        out-of-scope for its WANDER half: TryStartWander is a Hexwaste-only "honest fake"
//                                                            (no fo2ce counterpart) — not a vanilla draw, so it must not consume the shared
//                                                            stream either; kept on its own stream. (Its critter moves are a known
//                                                            non-vanilla divergence; --no-ambient also stops reg_anim dispatch, so it is
//                                                            not forced off here.)
//   harness one-offs  Harness.cs:152 (travel wmRng), :950 (fpRng), :1216 (_addictionRng probe), :1449 (_sneakRng
//                     probe), :1606 (deSeed), :2033 (trunk probe)   out of scope: probe actions, never in a scenario
//   endgame slides    Endgame.cs:260               out of scope: slide picker, not reached by an oracle scenario
public sealed partial class ViewerGame
{
    /// <summary>--rng fo2ce: every LOGIC draw comes from one shared <see cref="Fo2ceRng"/>.</summary>
    public bool OracleRngMode { get; set; }
    /// <summary>--trace &lt;path&gt; (consumed by the trace sink, Task 11).</summary>
    public string? OracleTracePath { get; set; }
    /// <summary>--fixture &lt;name&gt; (the trace header's fixture name, Task 11).</summary>
    public string? OracleFixture { get; set; }
    /// <summary>The shared oracle stream; null outside oracle mode (or before the post-load seed).</summary>
    public Fo2ceRng? OracleRng { get; private set; }

    /// <summary>CombatEngine captures its two streams at construction (LoadContent, before the load),
    /// but the oracle stream is only seeded AFTER the load — so in oracle mode the engine is handed this
    /// retargetable slot, pointed at the shared stream by <see cref="InitOracle"/>.</summary>
    private OracleRngSlot? _oracleCombatSlot;

    private sealed class OracleRngSlot(ICombatRng target) : ICombatRng
    {
        public ICombatRng Target { get; set; } = target;
        public int Next(int minInclusive, int maxExclusive) => Target.Next(minInclusive, maxExclusive);
    }

    /// <summary>Called in LoadContent right before the CombatEngine is built. Pre-seed draws (during
    /// the save import) go to a deterministic non-oracle stream, untraced — fo2ce's load path likewise
    /// draws before oracleRandomSeed.</summary>
    private void PrepareOracleCombatRng()
    {
        if (!OracleRngMode)
            return;
        _oracleCombatSlot = new OracleRngSlot(new SystemCombatRng(RngSeed ?? 1));
        _combatRng = _oracleCombatSlot;
        _calledShotRng = _oracleCombatSlot;
    }

    /// <summary>Seeds the shared stream and points every logic RNG at it. Called once, AFTER the
    /// startup load (LoadVanillaSaveAtStartup / LoadMap) and before any startup/scenario action —
    /// the fo2ce runner seeds right after lsgLoadGameHeadless.</summary>
    private void InitOracle()
    {
        if (!OracleRngMode)
            return;
        OracleRng = new Fo2ceRng(RngSeed ?? throw new InvalidOperationException("--rng fo2ce needs a seed (--scenario or --rng-seed)"));
        _oracleCombatSlot!.Target = OracleRng;
        _combatRng = OracleRng;
        _calledShotRng = OracleRng;
        _sneakRng = OracleRng;
        _stealRng = OracleRng;
        _partyRng = OracleRng;
        _wmRng = OracleRng;
        _skillRng = OracleRng;
        _tauntRng = OracleRng;
        _addictionRng = OracleRng;
        var adapter = new Fo2ceRandomAdapter(OracleRng);
        _ambientRng = adapter;
        if (_scriptHost is not null)
        {
            _scriptHost.Rng = adapter;
            _scriptHost.SharedVmRandom = adapter;
        }
    }

    private const int OracleEndTurnCapMs = 600000; // kOracleEndTurnCapMs (trace.patch)
    private const int OracleStepMs = 10;           // kOracleStepMs

    /// <summary>The fo2ce gOracleDudeAwaitingInput equivalent: the dude's combat slot is up and nothing
    /// is in flight (fo2ce sets it around _combat_input's inputGetInput).</summary>
    private bool IsDudeAwaitingCombatInput() =>
        _combat.Phase == CombatPhase.PlayerTurn && !_combat.IsBusy && _dude is { Moving: false };

    private bool IsCombatActive() =>
        _combat.Phase is CombatPhase.PlayerTurn or CombatPhase.EnemyTurn;

    /// <summary>Scenario `end-turn n`. Mirrors trace.patch oracleScenarioStep ENDTURN_* states.</summary>
    private void OracleEndTurns(int count)
    {
        for (int i = 0; i < count; i++)
        {
            // ENDTURN_SEEK_INPUT: combat not active -> the remaining end-turns are skipped.
            if (!OraclePumpUntilDudeTurnOrNoCombat("seeking dude input"))
                return;
            _combat.EndPlayerTurn();               // KEY_SPACE into _combat_input
            OracleFrame();                         // ENDTURN_PRESSED: run 10 ms
            OraclePumpUntilDudeTurnOrNoCombat("seeking next dude turn"); // ENDTURN_SEEK_NEXT
            Console.WriteLine($"oracle-end-turn: {i + 1}/{count} round={_combat.Round} phase={_combat.Phase}");
        }
    }

    /// <summary>Runs 10 ms frames until the dude awaits combat input (true) or combat is over (false).
    /// Hitting the 600000 ms cap is a failure: exit 7, like the fo2ce runner.</summary>
    private bool OraclePumpUntilDudeTurnOrNoCombat(string what)
    {
        for (int ms = 0; ms < OracleEndTurnCapMs; ms += OracleStepMs)
        {
            if (!IsCombatActive())
                return false;
            if (IsDudeAwaitingCombatInput())
                return true;
            OracleFrame();
        }
        OracleFail($"end-turn cap reached {what} (phase={_combat.Phase} resolving={_combat.IsResolving}"
            + $" walkersMoving={IsAnyWalkerMoving()} dudeMoving={_dude?.Moving})", 7);
        return false; // unreachable
    }

    /// <summary>One 10 ms oracle tick (scenario wait + end-turn). NOT <see cref="PumpOneFrame"/>: that
    /// --pump-ms pump never advances NPC walkers (only Update's PruneFinishedWalkers does), so the first
    /// NPC that walks in combat leaves CombatEngine.IsBusy true forever and end-turn cap-exits. This
    /// mirrors Update()'s game-logic order instead (ViewerGame.cs Update: animator, combat, dude,
    /// interaction, walkers, ambient life/sfx, critter procs, elevator, map_update, transition, clock);
    /// render-only steps (projectiles, float text, palette cycling, HUD roll) and worldmap travel are
    /// omitted. Then the fo2ce mainLoop dead/KO check (exit 5).</summary>
    private void OracleFrame()
    {
        const int ms = OracleStepMs;
        _animator.Update(ms);
        _combat.Step();
        _dude?.Update(ms);
        PumpPendingInteraction();
        PruneFinishedWalkers(ms);
        UpdateAmbientLife(ms);
        TickAmbientSfx(ms);
        PumpCritterProcs(ms);
        ConsumePendingElevator();
        PumpMapUpdate(ms);
        if (_pendingTransition is { } transition)
        {
            _pendingTransition = null;
            ApplyTransition(transition);
        }
        UpdateClock(ms);
        OracleExitIfDudeDown();
    }

    /// <summary>fo2ce leaves mainLoop (and the runner exits 5) once the dude is dead (critterKill sets
    /// _game_user_wants_to_quit) or, outside combat, DEAD|KNOCKED_OUT (main.cc mainLoop :340).</summary>
    private void OracleExitIfDudeDown()
    {
        if (_dude is null)
            return;
        bool dead = _combat.IsGameOver || _dude.Dude.IsDead;
        bool koOutsideCombat = !IsCombatActive()
            && (_dude.Dude.CombatResults & CriticalTables.DamKnockedOut) != 0;
        if (dead || koOutsideCombat)
            OracleFail("dude dead/knocked out", 5);
    }

    private static void OracleFail(string what, int code)
    {
        Console.Out.Flush();
        Console.Error.WriteLine($"oracle: {what}");
        Console.Error.Flush();
        Environment.Exit(code);
    }
}
