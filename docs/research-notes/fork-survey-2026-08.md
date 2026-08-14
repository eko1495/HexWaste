# Maintained-fork survey (2026-08)

Upstream `alexbatalov/fallout2-ce` is unmaintained; issue
[#522](https://github.com/alexbatalov/fallout2-ce/issues/522) (cambragol, 2026-07-13) names two
maintained forks. This is the review of both, and the record of what we took from them.

Companion documents:

- `docs/research-notes/fork-fix-ledger-2026-08.md` — the 139-row ledger this survey summarises.
  Every classification claim below is backed by a row there; the ledger carries the evidence, this
  file carries the conclusions.
- `docs/superpowers/specs/2026-08-14-fork-fix-harvest-design.md` — the design spec (triage pipeline,
  per-candidate protocol, terminal statuses).

## 1. The two forks

**[`fallout2-ce/fallout2-ce`](https://github.com/fallout2-ce/fallout2-ce)** — a *continuation of the
same codebase* alexbatalov stopped maintaining. Its README claims "dozens upon dozens of bug fixes";
the substance is a long decompilation-correctness pass (PR #675 alone audits ~20 engine files),
pathfinding work, graphics-glitch repairs, and weapon/ammo accounting. It also deliberately carries
non-vanilla quality-of-life behaviour (party looting/bartering, expanded inventory and barter
screens, auto-open doors, music continuity, highlighting, 44.1 kHz stereo audio) and a growing
sfall-compatibility surface (script hooks, `content.cfg` knobs, alternate damage formulas). Same
Sustainable Use License as upstream, so no new licensing condition applies to us.

**[`cambragol/fission-ce`](https://github.com/cambragol/fission-ce)** — a rebrand plus a modularity
project: an `[enhancements]` config block of individually toggleable features layered over a
`StrictVanilla` baseline. Its interest to us is architectural (the toggle shape) and catalogic (what
the community considers worth adding), not fidelity.

## 2. Why one of them is a diff source

Our reference clone `reference/fallout2-ce` is pinned at **`e97087b`** (2025-02-16, "Remove pause
window"), and `e97087b` remains the sole authority for vanilla behaviour. Against
`fallout2-ce/fallout2-ce@main` that pin is **1090 commits / 300 files changed** — but it is the
*same tree*, so every one of those commits reads as a diff against our exact port source. When the
fork changes a line we ported verbatim, we can see precisely what we ported and what they think it
should have been. That is what makes this fork a candidate source at all.

`cambragol/fission-ce` is a **survey source only**. It is a feature fork, not a fidelity fork; it
contributes to §6 and to nothing else in this document.

The rule adopted in `CLAUDE.md` and applied throughout: *a fork change is ported only when it
corrects a misreading of the original game, never because the fork made it.* Where the fork offers
no disassembly, no linked issue with an in-game symptom, and no reconstructable pre-image at
`e97087b`, the change is a fork judgement call and is rejected.

## 3. Triage results

`scripts/fork-triage.sh` mechanically reduced the 1090 commits to a **252-row shortlist** (dropping
commits confined to `.github/`, `CMakeLists*`, `os/`, `.clang-format`, emscripten/Android/iOS paths
and the mapper, then keeping those touching files we ported). Two further stages fed the ledger:

- **Stage 2 (rationale read).** Classification was **deliberately bounded by a user scoping
  decision** to PR #675's 74 hunks plus the 35 shortlist commits touching `combat.cc`,
  `combat_ai.cc`, `interpreter.cc`, `interpreter_extra.cc` or `tile.cc`. Two of those commits
  (#267, #572) carry independently-classifiable halves and were split, giving 110 rows.
- **Stage 3 (completeness sweep).** The full raw diff `e97087b..community/main` was then read hunk
  by hunk for the four fidelity-critical files — `tile.cc` (1494 diff lines), `combat.cc` (5666),
  `interpreter.cc` (1928), `object.cc` (2641) — to catch behaviour deltas buried inside refactor or
  feature commits that never surfaced as a "fix" commit. That added **29 rows**. Roughly 85–90% of
  those 11729 lines is provable no-op churn (symbol renames, `int`→enum retypes,
  `_colorTable[…]`→`COLOR_*`, `const`-ness; the `0`→`DAM_NONE` / `-1`→`STAT_INVALID` retype of the
  critical-hit tables alone is ~1600 lines, verified token-for-token identical after normalising
  those two spellings).

### Final tally — 139 rows

| Status | Rows | Meaning |
| --- | --- | --- |
| `not-applicable` | 86 | The routine, field or call site does not exist in Hexwaste at all |
| `not-a-gap` | 35 | The feature exists here and already matches the fork's post-fix behaviour |
| `rejected-non-vanilla` | 14 | The fork deviates from the original on purpose, or without grounding |
| `parked-QoL` | 2 | Opt-in convenience features (see §6) |
| `ported` | 2 | Real inherited defects, fixed |

**Two ports out of 139 classified rows is the honest yield**, and it is roughly what the spec's risk
section predicted. The value of the exercise is not concentrated in the two fixes: the 121
`not-applicable` + `not-a-gap` rows are the durable output, because each one records *why* a
specific fork commit is closed and therefore stops the next session re-opening it (§5).

### The bound — 217 shortlist rows were never classified

This is a deliberately incomplete pass and must not be read as a full harvest. **217 of the 252
shortlist rows remain unclassified**, and they are deferred, not dropped. The reason is a user
scoping decision made before Task 3: rather than spread a thin pass over everything, the budget went
to PR #675 (the decompilation-correctness audit — by far the highest-probability class, since it is
exactly where upstream misread the binary and we then ported the misreading faithfully) plus the
five most fidelity-critical files.

A **second wave** would cover the subsystems that decision left out: `animation.cc`, `worldmap.cc`,
`game_dialog.cc`, `skill.cc`, `party_member.cc`, `scripts.cc`, plus the audio/platform backends. It
should skip the mapper and the fork's own EDG / sfall-extension features entirely — this pass
classified enough of those (rows for `tileSetCenter` EDG, `content.cfg`, the sfall hooks) to say with
confidence they are out of scope by construction. On this pass's base rate, a second wave over ~217
rows would be expected to yield a small handful of ports at most; it is worth doing for the
termination records as much as for the fixes.

### The highest-yield signal

Worth recording for whoever runs that second wave: **the two ports both landed at sites upstream had
flagged as uncertain in its own comments.** `e97087b`'s `combat.cc` carries alexbatalov's
`// TODO: Not sure about "attack->defender == attack->oops".` (`:4682`) and
`// TODO: Not sure about defender == oops.` (`:4750`) on exactly the two expressions PR #493
inverts. A decompiler's own admission of doubt was a better predictor of a real gap than any fork PR
title. Grepping `e97087b` for `TODO`/`Not sure`/`XXX` and intersecting with the fork's diff would be
a cheap, high-signal way to prioritise the next wave.

## 4. Ported fixes

Two, each with its own commit and regression test.

### 4.1 `DAM_HURT_SELF` fumble damage — PR #675 hunk 7 (`combat.cc attackComputeCriticalFailure`)

Commits **`c2600da`** (port) and **`77b9dd5`** (damage-model correction).

The fork's claim: a `DAM_HURT_SELF` critical failure must add a further `randomBetween(1, 5)` to the
attacker's damage, and upstream omits it. Confirmed against our `CombatEngine`, which likewise
omitted it.

Porting it exposed a deeper, older error of ours. Pass 1 added `_rng.Next(1, 6)` inside a single
lumped `CritFailDamage(..., bool hurtSelf)` call — and review caught that *the lumping itself* was
the defect: our engine treated `DAM_HIT_SELF` and `DAM_HURT_SELF` as one branch that always rolled
full weapon/unarmed damage, so a `HURT_SELF` fumble cost weapon damage **plus** 1–5 — further from
vanilla in magnitude than before the port. Pass 2 rebuilt the branch to the reference's shape
(`combat.cc:4336-4345`): `HIT_SELF` → `attackComputeDamage` (our weapon roll, unchanged); else
`EXPLODE` → the blast; and `HURT_SELF` as its **own** branch adding `randomBetween(1, 5)` to an
`attackerDamage` that starts at 0 — i.e. exactly 1–5, with no damage roll at all. (`_cf_table` never
pairs `HURT_SELF` with `HIT_SELF`, so they cannot stack.) The shared tail — HP, log, transcript,
kill/game-over — moved to `ApplyCritFailDamage`; `DAM_RANDOM_HIT` still goes through the weapon roll.

Net behaviour change: a `HURT_SELF` fumble now costs exactly 1–5 HP, matching `e97087b` + the fix,
where it previously cost weapon damage (and, briefly during pass 1, weapon damage + 1–5).

Tests: `CombatEngineTests.HurtSelfFumbleRollsTheExtraOneToFiveDamage` (asserts the exact RNG draw
stream — no damage draw precedes the `(1, 6)`) and `HitSelfFumbleStillRollsWeaponDamage`.

### 4.2 Self-damage `damage_p_proc` — PR #493 (`combat.cc _apply_damage`)

Commit **`ed0611b`**.

`_apply_damage` passes a flag meaning "this hit landed on an **unintended** target" down to
`_damage_object`, which consumes it as `if (!flag) run damage_p_proc`. Upstream spells that flag
`attack->defender == attack->oops` at two sites; the fork inverts both to `!=`.

Our review established the grounding independently of, and more strongly than, the fork's own PR —
whose only stated justification is an unshown "switched `==` to `!=` to match original asm (and
logical behaviour)":

1. Upstream flagged **exactly these two expressions** with its own `// TODO: Not sure` comments
   (above), and no others in the routine.
2. The same predicate is spelled `!=` at the three *unflagged* sites in the same routine
   (`:4678`, `:4723`, `:4954`).
3. `_combat_display` settles the semantics: `defender != oops` is the "Oops! %s was hit instead of
   you!" condition — i.e. `!=` means "hit an unintended target". Read that way, upstream's `==` at
   the self-damage site says "the attacker's own damage proc runs only when the main hit was an
   oops", which contradicts the routine's own meaning and is **unreachable on every execution path**
   — the signature of an inverted decompiled predicate.

Three call sites shared this expression here; **only one was a gap.** Burst/explosion extras
(`ApplyBurstExtras`) already ran `RunDamageProc`, i.e. already matched the post-fix behaviour.
`DAM_RANDOM_HIT` routes through the defender call site with `oops` left at the original target, so
its flag is true and no proc runs — correct already. The **attacker's self-damage** call was the
gap: `ApplyCritFailDamage` ran no `damage_p_proc` at all. Fixed with the engine's own party gate
(`!objectIsPartyMember(target) || !objectIsPartyMember(source)`; `gPartyMembers[0].object = gDude`,
so the dude and companions are skipped and only an unaffiliated critter runs its own proc).

Test: `CombatEngineTests.NpcSelfDamageFumbleRunsItsOwnDamageProc` — a scripted, non-party NPC
fumbling `HURT_SELF` on its own turn, which failed with an empty `DamageProcCalls` before the fix.

### Blast radius

**Every golden suite stayed byte-identical through both ports, and nothing was re-recorded at any
point in this effort.** Containment, not correctness — correctness rests on the unit tests above. No
fixture reaches a self-damage fumble, which is exactly why the defects survived this long.

## 5. Rejected — and why

**This section is the deliverable, not filler.** 121 rows terminated as `not-applicable` or
`not-a-gap` and 14 as `rejected-non-vanilla`. Each has a written reason in the ledger. Before
re-opening any fork commit named below, read its ledger row first — the classification rests on
evidence that was actually read, and re-deriving it costs a session.

### 5.1 The recurring rejection patterns

Almost every closed row falls into one of eight shapes. Recognising the shape is usually enough to
close a new candidate quickly.

1. **C++-only defects with no managed analogue** (the largest group). Uninitialised locals and
   pointers, use-after-free, `memcpy` of a pointer instead of its pointee, `printf` varargs
   mismatches, operator-precedence bugs on `*ptr[i]`, `char` sign-extension, release-build
   `assert(false)` followed by a read of an uninitialised result. C#'s definite-assignment rule, GC,
   spans and interpolated strings close all of these by construction. *Examples:* #675 hunks 27, 28,
   33, 34, 43; #511; #512; #569; the `interpreter.cc` comparison-opcode `programFatalError` row.
2. **Subsystem not ported.** `dbox`, `window.cc` script windows, `nevs`, `mouse_manager`, the
   `_prerandom` table, the sfx cache, dead-critter aging, proto *synthesis* (fabricating a missing
   `.pro`), proto *writing*, and the `dialog.cc` window-script stack simply do not exist in
   Hexwaste. *Examples:* #675 hunks 11–16, 39–42, 44, 51, 53–58, 62, 66, 67, 70–72, 74.
3. **We already have the post-fix behaviour.** Frequently because the managed shape makes the bug
   unexpressible: methods return values instead of writing through out-params; opcode dispatch
   pushes unconditionally; flags are *set* rather than *toggled*; wrap loops already break on `>`
   rather than `<`. *Examples:* #675 hunks 10, 25, 26, 32, 68; the `tileToScreenXY` out-param row.
4. **The fork repairing its own post-`e97087b` refactor.** The reliable test, applied throughout:
   reconstruct the pre-image with `git -C reference/fallout2-ce show e97087b:src/<file>` and confirm
   the "before" text actually exists there. When it does not, the change is fork churn and cannot be
   a vanilla gap. *Examples:* `0f3023dc9`, `ab841916f`, `d9c24e1cc`, the whole EDG `tileSetCenter`
   series.
5. **No-op C++ changes** — renames, `int`→enum retypes, formatting, comment fixes, `default: break;`
   for `-Wswitch`, unused-parameter cleanups. Classified by whether the touched routine exists here
   (`not-a-gap`) or not (`not-applicable`); either way nothing moves at runtime. *Examples:*
   `0e773d521`, `20e80d4bb`, `3082e3b2b`, `6efbe795d`, `7a08f2e03`, `86787e2c9`, `e0384a37b`,
   `0038b7f1d`, #310, #267 (1/2), #675 hunk 56.
6. **Fork-only features.** EDG (map edges) and its hi-res stencil, the `content.cfg`/`gContentConfig`
   mod layer, sfall script hooks (`HOOK_COMBATTURN`, `scriptHooks_ToHit`,
   `scriptHooks_AfterHitRoll`, the AMMO_COST hook), the Glovz/YAAM alternate damage formulas, the
   burst-disabled AI registry, and the fork's save-integrity pass over combat structures we never
   serialise. None exists at `e97087b`; several default to the vanilla value anyway.
7. **Deliberate non-vanilla deviations.** `opCritterModifySkill`'s removed return value (the
   *original* pushes it; the fork's repro is a *mod* script, `gl_addskill.int`); `interpreter.cc`
   `isEmpty` treating dynamic strings as truthy (motivated by `npc_armor.mod`'s
   `while (sect.PID)`); the `start_gdialog_fix` config knob; the `fo1HitChance` toggle; removing
   vanilla's 100-entry outlined-object cap; the multi-round AI reload loop (its author's rationale is
   an F1-vs-F2 ammo-unit theory, not a decompilation finding).
8. **Ungrounded fork judgement calls** — the class our rule exists for. PR #675's body is four bare
   bullet lines with no disassembly, no issue links and no per-hunk rationale, so any hunk of it that
   flips a comparison without an observable symptom fails Step 3. *Examples:* the `_ai_run_away`
   `<` → `<=` flee gate; `objectAttemptPlacementPartyMember`'s `ROTATION_NE` → `ROTATION_NW` seed;
   `tile.cc`'s `180.0 * 0.3183098862851122` → `180.0 / M_PI` (the odd constant is the double
   alexbatalov read out of the *shipped binary* — exact 1/π is mathematically nicer but is not what
   the game computed, and the result is truncated into a 60°-wide hex direction, so a boundary angle
   could flip); PR #588's hidden-exit-grid skip (rejected on `e97087b` fidelity — the asymmetry it
   calls a bug is the original's own shape — and corroborated by a probe finding **0** hidden exit
   grids among **19693** across all **155** VFS-deduped maps).

### 5.2 Rows that were investigated hardest

Four candidates looked live and were closed only after real work. They are the ones most likely to
be re-opened, so their verdicts are summarised here.

- **PR #267 (2/2), `ammoSetQuantity`'s negative clamp.** The fork adds `if (quantity < 0) quantity = 0;`
  and quotes the original disassembly at `0x478747`, so the clamp really is in the binary. It
  interacts *inversely* with us: our `AmmoStack` reads a negative quantity as "-1 = pristine box →
  full". Closed `not-a-gap` because **no reachable producer of a negative exists**: all five
  `CombatEngine` writers and all eight charged-item/box writers are each preceded in the same call
  path by a `<= 0` bail, a `TryReloadSwitchedGun`, or an explicit `Math.Max(0, …)`/`Math.Min` bound.
  Recorded for whoever adds a *sixth* writer: floor at 0 **at the writer** — do not "fix" it by
  deleting the `-1` sentinel, whose live consumer is ammo-**box** stacking, not magazines.
- **PR #652, `_ai_move_away`'s `actionPoints` vs `actionPointsLeft` guard.** A real upstream defect
  (a hemmed-in critter moves onto the last tile it *rejected*), but our analogue — the DISTANCE_SNIPE
  back-away — gates on `taken > 0`, a counter of steps actually taken, so the defect is structurally
  impossible. Verified with a throwaway probe (a snipe-packet enemy whose single retreat hex is
  blocked): tile unchanged after `Step()`. The test could not be made to fail, so it was deleted per
  protocol.
- **`interpreter.cc ProgramValue::isEmpty` and dynamic strings.** Rejected, but **honestly a
  judgement call between two things the fork itself said**: the commit that carries the hunk is about
  wiring sfall hooks for `npc_armor.mod`, yet its squashed history contains a step titled "restore
  isEmpty() to vanilla while fixing bug". Neither side offers disassembly, so `e97087b` wins by our
  rule — but the probe that would actually settle it (find a real *vanilla* script pushing a dynamic
  string into a condition) **was not run**. A future session should run that probe before changing
  the status, not re-argue the commit message.
- **PR #675 hunk 20, the talking-head Y offset.** Closed on real data: a probe over all **186**
  `art\heads\*.FRM` in `master.dat` found `RotationOffsetsY` all-zero on every one, so the disputed
  `width` vs `destWidth` term is multiplied by 0 and the hunk cannot move a pixel. Note the scope
  carefully — the verdict covers *that term only*, not our head Y in general, which does diverge
  (see BACKLOG H1).

### 5.3 What the rejections cost us

Ten rows recorded a genuine **Hexwaste-side** gap in passing — found while proving a fork hunk
inapplicable, and unrelated to the fork commit that surfaced them. They are the single most valuable
by-product of this pass. Because "nothing to do *about the fork commit*" is not the same as "nothing
to do", they have been promoted out of ledger prose into first-class `docs/BACKLOG.md` entries
(Tier F, plus the party-stacking bug in Tier A). If you are reading the ledger and see a `not-a-gap`
row whose notes end in a parenthesised Hexwaste-side finding, the backlog entry is where the work is
tracked.

## 6. Parked: QoL catalogue for a future phase

Recorded so nothing needs re-discovering. **Sourced from fork READMEs and not individually verified
against our code**; each would need its own confirm pass before it becomes work. Several may already
exist in Hexwaste.

### From `fallout2-ce/fallout2-ce` (QoL)

- Party members loot and barter in place of the PC; directly equip party members
- Expanded 2-column inventory and loot screens; expanded 4-row barter screen; expanded AP bar
- Ctrl-click to move items when bartering / looting / stealing, with auto-balanced caps
- Music continues between maps; auto-open doors
- Integrated HELP menu; last used save slot remembered
- Item / corpse / container / critter highlighting
- 44.1 kHz stereo audio, `.ogg` / `.wav`

### From `cambragol/fission-ce` (`[enhancements]` toggles over a `StrictVanilla` baseline)

- AutoOpenDoors, AutoPush, AutoQuickSave
- DisplayBonusDamage, DisplayKarmaChanges
- Enhanced barter; explosions emit light; gapless music; minimap
- Mass highlight; NPC armor; numbered dialogue
- Configurable game speed and inventory column count

Two further items reached the ledger as `parked-QoL` from the stage-3 sweep of `object.cc`, and
belong to the same catalogue:

- **`_obj_portal_is_walk_thru` / `settings.qol.auto_open_doors`** — out of combat, an unlocked
  scriptless door reports itself walk-thru so pathing goes straight through it. Our P109/P110 model
  (never path through a closed door; walk-to-then-open) is the vanilla one and should stay the
  default.
- **`objectWithinWalkDistance` / `settings.qol.use_walk_distance`** — an interaction target beyond a
  configurable path length is refused instead of walked to. We walk any reachable distance (P109).

**Adoption rule.** A future phase adopting any of these must follow the fission-ce shape: **vanilla
by default, opt-in toggle.** Every item above is a deliberate deviation from the original, and our
golden suites encode vanilla behaviour — an on-by-default QoL feature would move fixtures and, worse,
would silently redefine what "byte-identical" certifies. A toggle that defaults off leaves the
suites meaningful and keeps the deviation visible in one place.
