# Charge readout (F38) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show remaining charges for the five non-gun capacity weapons, by re-basing the two readout gates on the conditions the reference actually uses — capacity for the HUD bar, caliber for the Awareness examine line.

**Architecture:** Two one-condition changes in the viewer, plus probe coverage, since neither site has golden coverage.

**Tech Stack:** C# / .NET 10, MonoGame DesktopGL. `src/Hexwaste.Viewer` only.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-22-charge-readout-design.md`. Read it — it corrects two of F38's three claims.
- **The two gates are different and must stay different.** HUD = `AmmoCapacity > 0` (`interface.cc:1357-1359`); examine = `Caliber != 0` (`proto_instance.cc:318-322`). Collapsing them into one condition is a defect, not a simplification: the Solar Scorcher (capacity 6, caliber 0) shows the HUD bar and no examine shots.
- **Verify every line number and function name as things stand now.** This project shipped a wrong reference function name once and a mis-diagnosed backlog entry once, both this week.
- Ported lines carry `// ported from fallout2-ce src/<file> <fn>()`.
- Reference is `reference/fallout2-ce` at `alexbatalov e97087b`. Read it; do not guess.
- **Do not touch anything under `tests/golden-*/`.** Expected outcome is byte-identical.
- Do NOT change the *shape* of the HUD readout. Hexwaste draws digits where vanilla draws a dithered gauge; that divergence is filed separately in Task 2 and is deliberately not fixed here.

---

### Task 1: Re-base both readout gates, with probe evidence

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Hud.cs` (the ammo counter, currently `:145-147`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs` (the Awareness examine line, currently `:5961-5963`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.Harness.cs` (extend `--awareness-probe`, around `:78-93`)
- Possibly modify: `src/Hexwaste.Viewer/Program.cs` (only if a new probe flag proves necessary)

- [ ] **Step 1: Read both sites and the reference**

Read `interface.cc` `_intface_update_ammo_lights` and `proto_instance.cc`'s examine branch, and confirm the two conditions differ as the spec states. Confirm `ammoGetCaliber` (`item.cc:1395-1412`) returns 0 when `ammoTypePid` is −1.

- [ ] **Step 2: The HUD gate**

Replace `w.IsGun(weaponProto.ExtendedFlags)` with a capacity test in the ammo-counter condition, leaving everything else — the `weaponItem is not null` check, `bar.Numbers`, the `DrawCounter` call and its coordinates — untouched. Add:

```csharp
        // ported from fallout2-ce src/interface.cc _intface_update_ammo_lights() (:1357-1359): the
        // readout is gated on ammoGetCapacity(item) > 0, NOT on weapon class, so the five non-gun
        // capacity weapons (Ripper, Cattle Prod, Power Fist, Super/Mega variants) show their charges.
        // NOTE: vanilla draws a 70px dithered gauge here (interfaceUpdateAmmoBar, :1985-2007) rather
        // than digits; that display-shape divergence predates this change and is tracked separately.
```

- [ ] **Step 3: The examine gate**

Replace the `IsGun` test with a caliber test, citing `proto_instance.cc:318-322`. Include the reason the proto's own caliber field is used rather than an ammo-proto lookup:

```csharp
                // ported from fallout2-ce src/proto_instance.cc (:318-322): message 547 ("…with %d/%d
                // shots of %s") is picked on ammoGetCaliber(item2) != 0, NOT on weapon class.
                // ammoGetCaliber (item.cc:1395-1412) resolves the AMMO proto via the weapon's
                // ammoTypePid and returns 0 when that pid is -1; the weapon proto's own caliber field
                // equals that ammo's caliber for every weapon with a real ammoTypePid, and is 0 when
                // it is -1, so the field is a faithful stand-in. A reload cannot break the
                // equivalence — weaponAttemptReload only accepts matching-caliber ammo.
```

Do not otherwise change the message text or its formatting.

- [ ] **Step 4: Probe evidence**

`--awareness-probe <hex>` (`ViewerGame.Harness.cs:78-93`) currently reports only `weaponLine=0|1`. Extend it to print the actual wielding line, so the shots text is visible. Then produce evidence for the four cases the spec's table distinguishes: a capacity melee weapon (Cattle Prod 160), a normal gun, a caliber-0 gun (Solar Scorcher 390), and a capacity-less melee weapon. **The Solar Scorcher case is the point** — it must show the HUD readout and NOT show examine shots. If reaching a case needs an existing harness flag to equip a weapon, use it; look for one before adding anything.

For the HUD site, find how to show the gate's decision without a screenshot — extending an existing probe is preferred over adding a flag. If no seam exists, say so in your report and propose one rather than declaring the change proven by inspection.

Record the exact commands and their output in your report.

- [ ] **Step 5: Build and commit**

```bash
dotnet build -v q
git add src/Hexwaste.Viewer
git commit -m "fix(hud): gate the charge readouts on capacity and caliber, not gun class"
```

---

### Task 2: Reconcile the backlog

**Files:**
- Modify: `docs/BACKLOG.md`

Wait for the controller's golden-suite result before writing the fixture outcome.

- [ ] **Step 1: F38 → shipped**, in the format its neighbours use, **stating the two corrected claims**: that the HUD and examine gates differ (capacity vs caliber) rather than both being capacity, and that vanilla's HUD readout is a dithered gauge rather than digits. Include the four-weapon evidence table from the spec and the probe output.

- [ ] **Step 2: Record F38's provenance lesson.** It was filed from a review finding without grounding, and two of its three claims did not survive contact with the reference — the same failure as F37 the day before. One or two sentences in the entry, not an essay.

- [ ] **Step 3: File the digits-vs-gauge divergence.** Vanilla paints a 70px one-pixel-wide dithered column (`interface.cc:1985-2007`, colours 14 and 196, ratio forced even, at `x = 463 + gInterfaceBarContentOffset` from `y = 26`); Hexwaste draws `NUMBERS.FRM` digits (`ViewerGame.Hud.cs`, from `1a7d27a`, P11-M1/M2, no citation). Note that closing it changes the HUD for every gun, so it needs its own decision and visual verification.

- [ ] **Step 4: File the MISC-charges branch.** `_intface_update_ammo_lights`'s `else` (`interface.cc:1363-1370`) shows the same gauge for a non-weapon MISC item in hand via `miscItemGetMaxCharges` / `miscItemGetCharges`. Hexwaste parses `MiscCharges` (`ProtoDatabase.cs:46`) but its HUD slot is weapon-only, so this needs more than a gate.

- [ ] **Step 5: Verify every citation** in the file as it now stands, then commit:

```bash
git add docs/BACKLOG.md
git commit -m "docs: F38 shipped with its claims corrected, two successors filed"
```
