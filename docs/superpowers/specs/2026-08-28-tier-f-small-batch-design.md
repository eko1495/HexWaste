# Tier F small-batch: six independent fidelity fixes — design

**Date:** 2026-08-28
**Scope:** F4, F5, F6, F7, F9, F43 from `docs/BACKLOG.md`.
**Predecessor:** the F42 closeout (`docs/superpowers/specs/2026-08-28-f42-closeout-design.md`),
merged to `main` at `df36ac5`.

## Why these six

They are the residue of Tier F that is *small, cited, and independent*. Each is one site, each
already has its `e97087b` counterpart identified, and none shares state with another. That makes
them separable work items rather than a single feature: any one can be dropped without stranding
the others.

They are also the items most likely to be lost. Each has sat in the backlog long enough to acquire
a stale description — three of the six descriptions were wrong when re-derived for this spec (§2,
§4, §5). A batch that closes them also corrects the record.

## Non-goals

- **No refactoring beyond each site.** F44 (unifying `ReduceByArmor` with `RangedMath.RollDamage`)
  is explicitly out of scope even though F43 touches the same file; it has its own entry and needs
  its own measurement pass.
- **No golden re-records.** Five of the six cannot move a fixture by construction (they touch
  rendering, the HUD, and a script external no fixture drives). F43 can in principle — see §6 for
  the gate that decides.
- **No new subsystems.** F7 does not introduce a framebuffer; F9 does not introduce an animation
  registry.

---

## 1. F4 + F5 — talking-head anchoring and horizontal sway

**One change, two backlog entries.** Both are terms in the same expression.

The reference (`game_dialog.cc:4590`) computes the head's destination inside the 388x200 display
buffer as:

```c
int destOffset = destWidth * (200 - height) + a3 + (388 - width) / 2;
```

- `200 - height` **bottom-anchors** the head. Hexwaste pins `y = frameY + 14`
  (`ViewerGame.cs:6194`), i.e. top-anchors it. A probe over all 186 `art\heads\*.FRM` found 14
  frames shorter than 200 px, so those heads sit up to 7 px high *and shift between frames* as the
  frame height changes. **This is F4.**
- `a3` is `artGetRotationOffsets(...).x + _totalHotx`, where `_totalHotx` resets to 0 on frame 0
  (`:4557`) and accumulates each frame's `artGetFrameOffsets` X (`:4585`). Hexwaste applies neither.
  The rotation-offset half is provably 0 on all 186 heads (established when PR #675 hunk 20 was
  rejected), so only the accumulator is live. **This is F5** — 5 heads use it (`HRLD2BF3`,
  `HRLD2GF2`, `HRLD2NF3`, `TNDI2GF2`, `TNDI2NF3`), all X-only, within ±5 px.

**Design.** `DrawTalkingHead` (`ViewerGame.cs:6157`) needs the current frame's height and X offset.
`FrmFile` already carries `OffsetX`/`OffsetY` per frame; `FrmCache` exposes `FrameCount` and
`GetTexture` but no per-frame metadata accessor. Add one narrow accessor rather than leaking
`FrmFile` to the caller.

`_totalHotx` becomes a `ViewerGame` field reset wherever the head frame resets to 0 — note the
existing resets at `_headFrame = 0` are the reset points, not a separate lifecycle.

**Divergence to keep:** the reference's `if (destOffset + width * v8 > 0) destOffset += width * v8;`
guard rides on the rotation-offset Y, which is 0 on every shipped head. Port the expression or
document it as inert — do not silently drop it without saying which.

**Verification.** No golden covers dialog pixels. The proof is the same 186-head probe that
produced the counts above: assert bottom-anchoring changes `y` for exactly the 14 short heads and
leaves the other 172 unchanged, and that `_totalHotx` is nonzero for exactly the 5 named heads.

---

## 2. F6 — monitor bullet knob and wrap budget

**The backlog left a blocking unknown; it is resolved here.** The entry cites
`DISPLAY_MONITOR_FONT` (101) without saying what font that is, and only `font0.aaf`..`font4.aaf`
exist in `master.dat`. Resolved: `interfaceFontLoad` (`font_manager.cc:117-122`) builds its path as
`snprintf(path, sizeof(path), "font%d.aaf", font_index)` over an index of `id - 100`, so font 101
**is `font1.aaf`** — the font Hexwaste already loads (`ViewerGame.cs:1487`). No new font asset, no
new loader.

The reference's wrap loop (`display_monitor.cc:262`) is:

```c
while (fontGetStringWidth(str) < DISPLAY_MONITOR_WIDTH - _max_disp - knobWidth)
```

with `DISPLAY_MONITOR_WIDTH` = `167 + gInterfaceBarContentOffset` (`:33`, offset 0 for the vanilla
640 bar) and `_max_disp` = `DISPLAY_MONITOR_HEIGHT / fontGetLineHeight()` (`:115`), i.e. **a line
count subtracted from a pixel width**. That is an oddity of the original, not a typo to correct:
port it as written. `knobWidth` is the width of `'\x95'` for the first line and **0 for every
continuation line** (`:270`, the `knob = '\0'; knobWidth = 0;` arm).

Hexwaste wraps to a flat `mw = 162` with no knob (`ViewerGame.Hud.cs:198`).

**Design.** Replace the flat constant with the reference expression, and prefix `'\x95'` to the
first wrapped line of each message. The per-line 80-character truncation
(`DISPLAY_MONITOR_LINE_LENGTH`) is part of the same loop; port it or record it as deliberately
omitted.

**Adjacent finding, decided separately.** The monitor rect itself is off: the reference is
`X=23, Y=24, W=167, H=60` (`display_monitor.cc:31-34`), Hexwaste uses `24, 26, 162x56`. The width
is load-bearing for this fix (it *is* the budget), so it changes here. Whether Y and the height
also move is a visual decision — moving them shifts every monitor line on screen, and the height
feeds `_max_disp`, hence the budget. **Decide before implementing, do not drift into it.**

**Verification.** `WrapText` is in `Hexwaste.Formats`-adjacent viewer code; the budget arithmetic
is a pure function and gets a unit test. The knob and geometry need a screenshot check.

---

## 3. F7 — automap wall-colour priority

**The backlog description is too broad and cites the wrong path.** Both corrected here.

The guard (`automap.cc:573`) is:

```c
if (*v12 != _colorTable[992] || objectColor != _colorTable[480]) {
```

`_colorTable[992]` is the **wall** colour and `_colorTable[480]` the **high-detail scenery** colour
(`automap.cc:534-541`). So the rule is not "no later mark may hide a wall" — it is precisely
*scenery may not overpaint wall*. The dude and the scanner colour (`_colorTable[31744]`) still
overpaint walls, by design.

The guard also lives in the `AUTOMAP_IN_GAME` branch. That branch's semantics — the `OBJECT_SEEN`
gate (`:530`), the `AUTOMAP_WTH_HIGH_DETAILS` scenery gate (`:537`), the scanner critter colour
(`:526`) — are the ones Hexwaste implements in the **full** `DrawAutomap` (`ViewerGame.Panels.cs:1040`),
not in `DrawPipboyMiniMap`. The backlog cites `Panels.cs:1015`, which is the mini-map's plot call.
**The fix belongs in `DrawAutomap`'s loop at `:1066`.**

**Design.** Hexwaste draws sprites, so there is no `*v12` to read back. In `DrawAutomap` the
tile→pixel map is a bijection (`ax = 449 - 2*(tile % 200)`, `ay = 2*(tile / 200) + 8`, step 2 px,
`:1059`), so a `Dictionary<int, Color>` of already-painted tiles is an exact stand-in for reading
the pixel — not an approximation. Keep the existing `_flatObjects`-then-`_solidObjects` paint
order: the reference's guard is order-dependent (a wall painted *after* scenery still wins), so a
global priority table would be a different, unfaithful rule.

`AutomapColor` (`:980`) already returns the reference's own wall/scenery colours, so the comparison
is direct.

**Verification.** A unit-testable helper over (tile, colour) sequences: scenery-after-wall on one
tile keeps wall; wall-after-scenery takes wall; dude-after-wall takes dude.

---

## 4. F9 — the `anim` external's 1000 / 1010 values

`opAnim` (`interpreter_extra.cc:3420-3428`):

```c
} else if (anim == 1000) {
    if (frame < ROTATION_COUNT) { objectSetRotation(obj, frame, &rect); ... }
} else if (anim == 1010) {
    objectSetFrame(obj, frame, &rect); ...
}
```

Note the `frame < ROTATION_COUNT` (6) guard on 1000 and its **absence** on 1010.

`ScriptHost.Anim` (`ScriptHost.cs:1610`) accepts `frame` and discards it, forwarding every `anim`
to `AnimRequested`.

**Correction to the backlog's framing:** it says scripts "get no effect at all", which is right, but
the reason matters for the fix. `AnimRequested`'s handler gates on `anim is >= 0 and < 40`
(`ViewerGame.cs:1233-1237`), so 1000/1010 already fall through as a silent no-op rather than
requesting a bogus animation. The change is therefore **purely additive** — no existing behaviour
is being replaced, which is why it cannot move a fixture.

**Obstacle, named so it is not discovered mid-implementation:** `MapObject.Frame` is `init`-only
(`MapFile.cs:47`) while `Rotation` is settable (`:48`). The renderer honours `obj.Frame`
(`ViewerGame.Rendering.cs:275`), so 1010 requires making `Frame` settable. Follow the precedent
already set on `Pid` in the same class — a doc comment saying which engine call mutates it.

**Verification.** Hermetic `ScriptHost` tests: `anim(obj, 1000, 3)` sets rotation 3;
`anim(obj, 1000, 6)` leaves rotation unchanged (the guard); `anim(obj, 1010, n)` sets frame;
`anim(obj, 5, 0)` still reaches `AnimRequested` unchanged.

---

## 5. F43 — the ammo damage-multiplier clamp

`RangedMath.RollDamage` (`CombatMath.cs:156`) computes
`raw * critMultiplier * Math.Max(ammoDamageMultiplier, 1)`. The reference multiplies by
`damageMultiplier` unconditionally (`combat.cc:4586-4587`) and guards only the divisor, with
`if (damageDivisor != 0)` (`:4594-4598`).

The divisor forms are equivalent — dividing by 1 and skipping the divide give the same result. The
multiplier clamp is not: ammo with multiplier 0 deals 0 damage in the reference *and* on Hexwaste's
own melee path (which took no such clamp when F36 landed), but full unmultiplied damage on the gun
path.

**This is the one item in the batch that can move a fixture, and it is gated.** The backlog records
as **unverified** whether any shipped ammo proto carries a multiplier of 0. That census is the
first step, not an afterthought:

- **If no shipped ammo has multiplier 0** — the change is inert on real data, every suite stays
  byte-identical, and it ships as a correctness fix with the census recorded as its evidence.
- **If some ammo does** — the gun path currently disagrees with both the reference and our own
  melee path for that ammo. The fix is still right, but it is a live damage change: stop, report
  the affected protos, and let the re-record decision be made explicitly rather than absorbed into
  a "small batch".

The census is a data question answerable offline from the proto tables; it does not need the
change to be written first.

---

## 6. Sequencing and independence

No item depends on another. Suggested order is by escalating uncertainty, so the batch banks value
early and the one item that could stop it is reached with everything else already done:

1. **F43 census** (answers the only gate in the batch).
2. **F9** — additive, hermetic tests, no rendering.
3. **F7** — pure helper, unit-testable.
4. **F4 + F5** — one expression, needs the `FrmCache` accessor.
5. **F6** — needs the geometry decision from §2 first.
6. **F43 change** — conditional on step 1.

Every step ends with the full hermetic suite green. The golden suites (279 fixtures, `DISPLAY` +
real game data required) run once at the end, and again after F43 specifically.

## 7. Risks

- **Citation drift.** This branch will change line counts in `ViewerGame.cs`, `ViewerGame.Hud.cs`,
  `ViewerGame.Panels.cs`, `ScriptHost.cs`, `MapFile.cs` and `CombatMath.cs`. Every inbound citation
  into those files — repo-wide, not just in documents this branch edits — must be re-derived from
  the tree **after** the last edit. This cost the predecessor branch three separate fix-up commits;
  budget one sweep commit at the end rather than fixing citations as they break.
- **Scope creep in F6.** The monitor geometry is adjacent and tempting. The width must change; the
  rest is a decision, not a discovery.
- **Visual items have no golden.** F4, F5, F6 and F7 are checked by probe and screenshot. State
  plainly in each commit what was actually looked at, rather than implying suite coverage that does
  not exist.
