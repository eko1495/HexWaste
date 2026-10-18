# Oracle F17: the dude script's sid label in the trace

Finding F17 in `docs/research-notes/oracle-m1-findings-2026-10.md`. After F16, artemple-idle first diverges
at trace line 6 and klamath-hour at line 69. The values match; only the sid label differs:

```
fo2ce:    LVAR sid=0x04000005 0 0->302421     (klamath: 0x04000028)
hexwaste: LVAR sid=0x04800000 0 0->302421
```

## fo2ce behavior (reference: alexbatalov e97087b)

- `_obj_load_dude` (`object.cc:3629`) calls `scriptsSetDudeScript` -> `_obj_new_sid` -> `scriptAdd`. The new
  sid is `scriptGetNewId(SCRIPT_TYPE_CRITTER) | (4 << 24)` (`scripts.cc:2138`): take
  `nextScriptId++`, then step up past every id a critter-type script already uses.
- `nextScriptId` is reset only in `_scr_header_load` (`scripts.cc:1797`). A save load does not reset it,
  so its value depends on how many critter scripts fo2ce has added since startup.
- Observed in the three fixtures (critter ids from each `.SAV`'s critter section):

  | fixture | critter ids used | fo2ce dude sid |
  |---|---|---|
  | artemple-idle | 1, 18000 | 0x04000005 |
  | arcaves-ambush | 2-5, 7-24, 18000 | 0x04000006 |
  | klamath-hour | 1-39, 18000 | 0x04000028 |

  All three fit one counter value: 5. Artemple pins it to exactly 5 (ids 2-4 are free); arcaves (5 used,
  so 6) and klamath (5-39 used, so 40) agree. This is the oracle's launch path (main menu, load a slot);
  the five earlier allocations were not counted from source.
- After a map change the dude's sid is pid-derived: `(scriptType << 24) | ((pid & 0xFFFFFF) + 18000)` =
  0x04004650 (`party_member.cc:693`, `_partyMemberRecoverLoadInstance`).
- Scripts cannot observe a sid. The difference is a trace label only.

## Hexwaste today

- The dude's script uses a fixed `ScriptHost.DudeSid` (0x04800000). Its LVARs are keyed by map `""`
  (`LvarKey`) so they survive map changes; `DudeSid` is also used by `BindDudeScript`,
  `ReplaceDudeScriptAfterVanillaLoad`, the vanilla import and tests.
- The only trace line that prints a sid is LVAR (`ScriptContext.SetLocalVar`, `ScriptHost.cs:1763`).

## Design

Keep `DudeSid` as the real sid. Add a trace-only label.

1. **`ScriptHost.DudeTraceSid`** (`public int`, read-only outside the host): the sid fo2ce would print for
   the dude.
   - `BindDudeScript` sets it to 0x04004650 (the pid-derived sid after a map change).
   - `ReplaceDudeScriptAfterVanillaLoad` sets it after its `BindDudeScript` call, to
     `FirstFreeCritterSid(map.CritterSids, 5)`: the first id >= 5 not used by any sid in the `.SAV`'s
     critter list, `| 0x04000000`. A one-line comment explains the 5 (observed counter on the oracle's
     launch path, all three fixtures).
   - `FirstFreeCritterSid` is a small `internal static` helper so a test can call it.
2. **Trace emit:** `SetLocalVar` passes `_sid == DudeSid ? _host.DudeTraceSid : _sid` to `OracleTrace.Lvar`.
3. **Known limit**, as a code comment: `shortcut: assumes no critter script is created during the load-time
   passes (fo2ce's counter would move past 5); upgrade if an oracle fixture spawns one at load`.

## Testing

- **Unit (hermetic):** `FirstFreeCritterSid` on the three fixtures' id sets returns 0x04000005,
  0x04000006 and 0x04000028.
- **Unit:** after `ReplaceDudeScriptAfterVanillaLoad` (the existing `StaleDudeScriptTests` fixture), a dude
  LVAR store traces under `DudeTraceSid`, not 0x04800000; after a plain `BindDudeScript`, under 0x04004650.
  Uses `OracleTraceTests`' existing pattern for capturing trace output.
- **Oracle:** rerun all three fixtures. Success: artemple and klamath move past lines 6 and 69; arcaves
  stays at line 9.
- **Goldens:** not run. No golden suite enables the trace, and nothing else changes.
- **CI steps** (ascii-lint, unit tests, Release build) pass.

## Docs

Findings doc: F17 FIXED with the counter rule and the table above; the new first divergences; arcaves
line 9 (`sid=0x04000008` is a scorpion's combat LVAR, fo2ce starts that critter's turn first) is filed
under F4/F5, not F17.

## Out of scope

- Allocating the real dude sid like fo2ce (touches LVAR keys and every `DudeSid` user, invisible to
  scripts).
- Normalizing sids in `TraceComparer` (would hide real sid differences).
- Modeling fo2ce's `nextScriptId` counter across a session.
