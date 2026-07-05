namespace Hexwaste.Formats.Item;

/// <summary>
/// Self-use MISC items whose inventory "use" runs the ITEM's own use_p_proc, ported from
/// fallout2-ce src/proto_instance.cc _obj_use_misc_item (:986) — a hardcoded six-pid set.
/// The caller consumes one on success (rc=1 → itemRemove in _obj_use_item, :1119), which is
/// how the Pip-Boy enhancers "install" and vanish. (P116, review item F.)
/// </summary>
public static class MiscItems
{
    public const int RamirezBoxClosed = 431;      // PROTO_ID_RAMIREZ_BOX_CLOSED
    public const int RaidersMap = 444;            // PROTO_ID_RAIDERS_MAP
    public const int CatsPawIssue5 = 331;         // PROTO_ID_CATS_PAW_ISSUE_5
    public const int PipBoyLingualEnhancer = 499; // PROTO_ID_PIP_BOY_LINGUAL_ENHANCER
    public const int SurveyMap = 523;             // PROTO_ID_SURVEY_MAP
    public const int PipBoyMedicalEnhancer = 516; // PROTO_ID_PIP_BOY_MEDICAL_ENHANCER

    /// <summary>Is this pid in _obj_use_misc_item's hardcoded set (use runs the item
    /// script, then one is consumed)?</summary>
    public static bool IsSelfUseScripted(int pid) => pid is RamirezBoxClosed or RaidersMap
        or CatsPawIssue5 or PipBoyLingualEnhancer or SurveyMap or PipBoyMedicalEnhancer;
}

/// <summary>
/// Charged MISC items, ported from fallout2-ce src/item.cc _item_m_use_charged_item (:2247).
/// Geiger Counter and Stealth Boy TOGGLE: turning on consumes a charge, flips the pid to the
/// "II" (on) variant and queues a trickle that drains one charge per interval until empty
/// (miscItemTurnOn :2709 / miscItemTrickleEventProcess :2298); the Stealth Boy also sets
/// OBJECT_TRANS_GLASS on its owner (stealthBoyTurnOn :2460 — translucency + halved NPC
/// perception range). The Motion Sensor consumes one charge per automap scanner view.
/// (P116, review item H.)
/// </summary>
public static class ChargedItems
{
    public const int GeigerCounterOff = 52;  // PROTO_ID_GEIGER_COUNTER_I
    public const int StealthBoyOff = 54;     // PROTO_ID_STEALTH_BOY_I
    public const int MotionSensor = 59;      // PROTO_ID_MOTION_SENSOR
    public const int GeigerCounterOn = 207;  // PROTO_ID_GEIGER_COUNTER_II
    public const int StealthBoyOn = 210;     // PROTO_ID_STEALTH_BOY_II

    /// <summary>OBJECT_TRANS_GLASS — the stealth-boy owner flag (also the render blend bit).</summary>
    public const int TransGlassFlag = 0x20000;

    public static bool IsChargedItem(int pid) => pid is GeigerCounterOff or StealthBoyOff
        or MotionSensor or GeigerCounterOn or StealthBoyOn;

    public static bool IsToggleable(int pid) => pid is GeigerCounterOff or StealthBoyOff
        or GeigerCounterOn or StealthBoyOn;

    public static bool IsOn(int pid) => pid is GeigerCounterOn or StealthBoyOn;

    public static bool IsStealthBoy(int pid) => pid is StealthBoyOff or StealthBoyOn;

    public static int TurnedOnPid(int pid) => pid switch
    {
        GeigerCounterOff => GeigerCounterOn,
        StealthBoyOff => StealthBoyOn,
        _ => pid,
    };

    public static int TurnedOffPid(int pid) => pid switch
    {
        GeigerCounterOn => GeigerCounterOff,
        StealthBoyOn => StealthBoyOff,
        _ => pid,
    };

    /// <summary>Game-ticks between trickle charge drains: 600 stealth boy / 3000 geiger
    /// (miscItemTrickleEventProcess :2303).</summary>
    public static int TrickleTicks(int pid) => IsStealthBoy(pid) ? 600 : 3000;
}

/// <summary>
/// The four medical bags: "use on a critter" is a First Aid / Doctor skill use on the target
/// plus a 1-in-10 supplies-depletion roll, ported from fallout2-ce src/proto_instance.cc
/// _protinst_use_item_on (:1249). The criticalChanceModifier (+20 bag / +40 field-medic tier)
/// feeds fo2ce's skillRoll critical channel. (P116, review item G.)
/// </summary>
public static class MedicalBags
{
    public const int FirstAidKit = 47;            // PROTO_ID_FIRST_AID_KIT — First Aid, +20
    public const int DoctorsBag = 91;             // PROTO_ID_DOCTORS_BAG — Doctor, +20
    public const int FieldMedicFirstAidKit = 408; // PROTO_ID_FIELD_MEDIC_FIRST_AID_KIT — First Aid, +40
    public const int ParamedicsBag = 409;         // PROTO_ID_PARAMEDICS_BAG — Doctor, +40

    /// <summary>Map a pid to its (skill, criticalChanceModifier); skills use the game's
    /// skill ids (6 = First Aid, 7 = Doctor). False when the pid isn't a medical bag.</summary>
    public static bool TryGet(int pid, out int skill, out int criticalChanceModifier)
    {
        (skill, criticalChanceModifier) = pid switch
        {
            FirstAidKit => (6, 20),
            DoctorsBag => (7, 20),
            FieldMedicFirstAidKit => (6, 40),
            ParamedicsBag => (7, 40),
            _ => (-1, 0),
        };
        return skill != -1;
    }
}
