namespace Hexwaste.Formats;

/// <summary>One save slot's display metadata for the picker (P48): occupied/empty, a
/// version flag, and the fields a row shows (character, level, map, in-game date).
/// Mirrors fallout2-ce's LoadSaveSlotData display (loadsave.cc _DrawInfoBox) reduced to
/// what Hexwaste's JSON SaveState carries.</summary>
public sealed record SlotInfo(bool Occupied, bool VersionMismatch, string Character, int Level, string Map, string Date)
{
    public static readonly SlotInfo Empty = new(false, false, "", 0, "", "");
}

/// <summary>
/// Multi-slot save bookkeeping (P48), the pure half of the 10-slot load/save UI. The engine
/// keeps 10 slots (loadsave.h LOAD_SAVE_SLOT_COUNT, SAVEGAME\SLOT##\SAVE.DAT); Hexwaste keeps
/// one JSON file per slot (hexwaste-slotN.json) under a save directory the viewer composes.
/// File IO + the directory live in the viewer; this is the slot count, the per-slot file name,
/// and the SaveState→display reduction, so they can be unit-tested without a filesystem.
/// </summary>
public static class SaveSlots
{
    /// <summary>The number of save slots (LOAD_SAVE_SLOT_COUNT, loadsave.h).</summary>
    public const int Count = 10;

    /// <summary>The per-slot file name (the viewer prefixes the save directory).</summary>
    public static string SlotFileName(int slot)
    {
        if (slot < 0 || slot >= Count)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"slot must be 0..{Count - 1}");
        return $"hexwaste-slot{slot}.json";
    }

    /// <summary>Reduce a slot's loaded SaveState (or null = no file) to its picker-row metadata.
    /// A version mismatch is surfaced like the engine's "- OLD VERSION -" (loadsave.cc:2058).</summary>
    public static SlotInfo Describe(SaveState? state)
    {
        if (state is null)
            return SlotInfo.Empty;
        if (state.Version != SaveState.CurrentVersion)
            return new SlotInfo(Occupied: true, VersionMismatch: true, "", 0, "", "");
        return new SlotInfo(
            Occupied: true,
            VersionMismatch: false,
            Character: state.Character ?? "player",
            Level: state.DudeLevel,
            Map: state.Map,
            Date: GameClock.DateStringAt(state.ClockTicks));
    }
}
