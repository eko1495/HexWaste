# Phase-8 Track D: Progression + Ops + Blind-spot sweep

## Q1 — Skill points, exactly

**Award (engine):** skill points are NOT granted in `pcAddExperience` — they're granted lazily by the character editor's `characterEditorUpdateLevel()` (character_editor.cc:5681-5701): for each level gained since `gCharacterEditorLastLevel`, `sp += 5 + 2*IN` (+2/Educated rank, +5 Skilled trait, −5 Gifted, floor 0), **banked pool capped at 99**. So the canonical formula at our scope (no perks/traits applied to math yet): **5 + 2×IN per level**.

**Spend (engine):** `skillAdd` (skill.cc:289-326): cost per +1 point depends on current *effective* skill value — `skillsGetCost` (skill.cc:355-371): 1 pt ≤100, 2 @101-125, 3 @126-150, 4 @151-175, 5 @176-200, 6 @201+. Hard cap **300** (skill.cc:264-266; skillAdd refuses at ≥300, skill.cc:309-311).

**Tag skills (engine):** in `skillGetValue` (skill.cc:251-256) a tagged skill gets (a) base points counted **twice** (`value += baseValue * baseValueMult` again — i.e. every spent point is worth double), and (b) a flat **+20**, except a 4th tag bought via the Tag! perk gets the double-rate but not the +20. So "tag = +20 once and 2×/point", both implemented in skillGetValue, not at spend time — spend cost is computed on the *effective* (doubled) value, which is why tagged skills hit the 101 cost ramp faster.

**Hexwaste today:** GcdFile parses `TaggedSkills[4]` (src/Hexwaste.Formats/Combat/GcdFile.cs:17,42) but nothing reads it. CritterState skill getters (CritterState.cs:53-67) and BarterMath.BarterSkill (BarterMath.cs:12-13) compute base formula + `proto.Skills[n]` only — **no tag bonus, no growth**. Level-up = HP only (ViewerGame.cs:2632-2656, Progression.cs HpPerLevel). So a premade "stealth" character is currently 20+ points weaker in their tagged skills than the engine would make them.

**Recommendation: (b) text allocator at the level-up moment — sized M (~1.5-2 days), with the tag bonus fixed first as a free S.**
- Step 0 (S, ~2h, do regardless): apply the tag bonus in skill value computation — one method `SkillValue(skill)` on CritterState that takes `TaggedSkills` for the dude; +20 and double points. This is a *correctness bug* vs the engine today, independent of any spend UI.
- Why (b) over (a) auto-spend: skills now gate 6 real systems (small-guns/melee/unarmed to-hit, lockpick, traps, barter prices) — the player choosing *which* gate to push is the entire fun of progression; auto-spend on tags silently improves numbers the player never sees, near-zero perceived value for ~60% of the same code (you still need award math, cost ramp, persistence in the JSON save). The viewer already has list-menu UI patterns (MenuState.CharacterPick, ViewerGame.cs:3957-3970, 4214-4232) — a "level up: N points, arrows+Enter to spend, Esc done" panel reuses them directly.
- Minimal model: pool = Σ per level (5+2×IN, cap 99 banked); spend UI lists the 6-8 skills we actually gate (not all 18 — honest scope); cost from skillsGetCost on effective value; cap 300; pool + per-skill spent deltas go in the JSON save (SaveState.cs). Skip skillSub (un-spend), skip Educated/Skilled/Gifted modifiers.
- Defer (c) is wrong now: with combat+lockpick+barter live, a player who levels twice and sees only +HP perceives a dead system.

## Q2 — SPECIAL creation screen (minimal)

**Engine creation mode:** stats all start at default 5 (gStatDescriptions, stat.cc:42-48; protoCritterDataResetStats stat.cc:545-551) with **5 free char points** (characterEditorReset, character_editor.cc:1907 & 5674), each stat clamped 1-10 (stat_defs.h:7-10; spend path character_editor.cc:3758). Every +/- calls `critterUpdateDerivedStats(gDude)` (character_editor.cc:3751,3766). Exit validation: char points must be 0 (character_editor.cc:843) and all 3 tag skills selected ("You must select all tag skills", character_editor.cc:861-864). Optional traits (2 max) — **skip**: they thread through trait modifiers in skill/stat/skill-point formulas (traitGetSkillModifier etc.) and we already parse-and-ignore gcd Traits[2]; consistent omission, document it.

**Derived stats that MUST recompute** (critterUpdateDerivedStats, stat.cc:554-579) — restricted to ones Hexwaste consumes (CritterState.cs:40-67, initiative ViewerGame.cs:2542):
- MaxHP = ST + 2×EN + 15 (stat.cc:567)
- MaxAP = AG/2 + 5 (stat.cc:568)
- ArmorClass = AG (stat.cc:569)
- MeleeDamage = max(ST−5, 1) (stat.cc:570)
- Sequence = 2×PE (stat.cc:572)
- CriticalChance = LK (stat.cc:574)
- (HealingRate = max(EN/3,1), stat.cc:573 — only needed if Q5's rest lands)
- Skill bases need no stored recompute: CritterState computes them live from stats. CarryWeight/rad/poison resist recompute is moot — nothing reads them.

**Sizing:** honest minimal screen = **M** (~1-1.5 days): new MenuState reusing the CharacterPick list pattern (ViewerGame.cs:3957-3970, 4214-4232) — 7 stat rows with left/right +/- and a points counter, live derived-stat readout line, then a tag-3 picker over the gated skills, build an in-memory GcdFile (no .gcd write — keep that invariant, GcdFile stays read-only). Drops to **S** (~half day) without tagging, but tagging is the choice that actually moves the gated systems, so M is the honest size. Natural pairing: Q1's allocator and this share the stat/skill row widget.

## Q3 — The female dude

**Art (DatDump vs critter.dat):** HFJMPS ships 166 FRM/FRx files vs HMJMPS 196. Suffix diff: female lacks only the exotic violent deaths — BE (charred, anim 24), BH (electrify, 27), BJ (burned-to-nothing, 29), BK (electrified-to-nothing, 30), BN (fire dance, 33), their single-frame R-set twins (RE/RH/RJ/RK), and NA (called-shot pic). Code mapping per _art_get_code: 'b'+(anim−20) for deaths, 'r'+(anim−48) for SF deaths (art.cc:544-580; animation.h:43-58). **Hexwaste only ever plays anims 20/21 (PickDeathAnim, ViewerGame.cs ~2466-2472), both present for HFJMPS — female art is complete for our scope.**

**GCD gender:** STAT_GENDER = index 34 (stat_defs.h:57, last saveable stat), already inside the `baseStats[35]` we parse (GcdFile.cs:36). Verified bytes: PLAYER/COMBAT(Narg)/STEALTH(Mingan) = 0 (male), **DIPLOMAT (Chitsa) = 1 (female)** — i.e. today picking "diplomat" renders a female character with male hmjmps art, an actual fidelity bug, not just a feature gap.

**Sfx:** female death screams ship — `sound\SFX\HFXXXXBA.ACM`/`BB` confirmed in master.dat; our SfxName.HumanDeath already takes a `female` flag (SfxName.cs:52-53) but the call site hardcodes `female: false` (ViewerGame.cs:2453).

**pro_crit.msg:** no gender split — there is one obj_dude proto; gender is a stat, the name comes from the gcd. Nothing to do.

**Sizing: S (~2-4h), and it's mostly a bug fix.** (1) SpawnDude picks "hfjmps" when `_dudeGcd.Stats.BaseStats[34] == 1` (mirrors art.cc:218-220 `_art_vault_person_nums[JUMPSUIT][GENDER_*]`), fallback hmjmps; (2) pass the dude's gender (and NPCs': art name char 2 == 'f') into HumanDeath. A creation-screen gender toggle (Q2) is then one extra menu row. No new formats, no new data.

## Q4 — Post-release ops

**CI draft (build + data-free tests).** Plain `dotnet test` already self-skips data tests (tests/Hexwaste.Formats.Tests/GameDataFactAttribute.cs:13 — Skip when FALLOUT2_DIR unset), so the workflow is trivial; net10.0 everywhere (csproj confirmed). Draft `.github/workflows/ci.yml`:

```yaml
name: ci
on:
  push: { branches: [main] }
  pull_request:
jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: "10.0.x" }
      - run: dotnet build Hexwaste.slnx -c Release
      - run: dotnet test tests/Hexwaste.Formats.Tests -c Release --no-build --logger "console;verbosity=normal"
      # Optional release dry-run on tags:
      # - run: scripts/release.sh ${GITHUB_REF_NAME#v}   # needs `zip` (present on ubuntu runners)
```
No FALLOUT2_DIR secret is possible or desirable — game data must never enter CI (legal guardrails), so CI coverage = the data-free split by construction.

**Perf canary: keep local-only.** The bench (`--bench N`, ViewerGame) needs an SDL window + GL context. On GitHub's GPU-less ubuntu runners that means xvfb + Mesa llvmpipe software rasterization — MonoGame DesktopGL does start under xvfb (standard X11 technique: xvfb-run / GabrielBB/xvfb-action, https://github.com/GabrielBB/xvfb-action, https://arbitrary-but-fixed.net/2022/01/21/headless-gui-github-actions.html), but (a) without game data the map can't load anyway, and (b) timings on a 2-core shared runner under llvmpipe are noise — a ms-threshold assertion would flap. Verdict: the canary stays a local data-gated test/checklist item in docs/RELEASING.md ("run --bench 500 on newr1.map before tagging; p95 < 16 ms"); CI gets correctness only.

**Issue templates:** add `.github/ISSUE_TEMPLATE/bug_report.yml` (fields: Hexwaste version, OS, game-data origin GOG/CD + language, map name, console log) and `config.yml` with `blank_issues_enabled: false` plus a link "Is it in scope? read SCOPE.md". **SCOPE.md doesn't exist yet** — it should be a public distillation of CLAUDE.md's in/out-of-scope lists (no combat AI depth, no script VM beyond the micro-VM, no worldmap encounters...) so "add NPC X" reports can be closed by link. Size: S for all three files.

**Web pulse (2026-06):**
- fallout2-ce: still alive but slow-burn — newest visible issue #513 (2026-02-10); the two provenance-relevant issues are unchanged: #428 "Clarification on licensing needed" (closed, Dec 2024) and #476 "Is this a decompilation?" (open since Mar 2025, no activity) — no new movement since June 2026 affecting our provenance docs. (https://github.com/alexbatalov/fallout2-ce/issues)
- MonoGame: 3.8.5 still in preview — preview.6 (2026-05-22) is latest; 3.8.5 formalizes Vulkan/DX12 preview targets ahead of 3.9; no stable 3.9 (https://github.com/MonoGame/MonoGame/releases, https://monogame.net/blog/2026-01-02-MonoGame385.preview.2-release/, https://docs.monogame.net/roadmap/). No action for us; stay on stable 3.8.x.
- .NET 10: June 2026 servicing = 10.0.9 (2 CVEs: CVE-2026-45491/-45490); LTS until Nov 2028 (https://devblogs.microsoft.com/dotnet/dotnet-and-dotnet-framework-june-2026-servicing-updates/). Self-contained published artifacts bundle the runtime — a release rebuilt against current SDK picks up the patches; worth a line in RELEASING.md.
- SUL (our license, n8n's Sustainable Use License): no v2 / no relevant 2026 changes (https://docs.n8n.io/sustainable-use-license/).

## Q5 — What we're not seeing (hour 2-3 audit)

Verified-current facts: healing exists ONLY via drugs/stimpaks (UseDrug, ViewerGame.cs:1751-1790); no rest UI and no key for it (README controls table, README.md:60-78 — no rest, no char-sheet view); GameClock.AdvanceHours exists and is already used once — worldmap travel costs 8h (ViewerGame.cs:3207); poison/radiation exist only as VM external names (ExternalArity.cs:111-112,148-149), nothing ever ticks them; kill XP + give_exp_points are wired (ScriptHost.cs:90,932; ViewerGame.cs:2438); per-map container snapshots overwrite the old "restock on re-enter" (phase-5 M1), and script timers are dialog-gated + cleared on map exit (phase-5 M0), so merchant stock and caps are effectively finite forever; level-up grants HP only (ViewerGame.cs:2632-2656).

**Top 3 by player-pain:**
1. **No rest-to-heal → permanent attrition death spiral (pain: highest; size S).** Engine: resting heals HEALING_RATE HP per 3 game-hours (pipboy.cc:2110-2114, `hoursToHeal = hpToHeal/healingRate*3`); HEALING_RATE = max(EN/3,1) (stat.cc:573). After 2-3 fights a player with no stimpaks is stuck at low HP forever — F9 is the only "heal". Minimal fix: one key ("Z rest until healed"), refuse if hostiles within sight range, AdvanceHours(ceil(need/rate*3)), heal, log. All pieces exist (clock, sight check, log). ~half day.
2. **Empty level-ups (pain: high; size M = Q1's allocator).** By hour 2-3 the player has ~1000-3000 XP (level 2-3) from kills+dialog XP and has seen "You have reached level 2!" deliver nothing visible but HP. Compounded by: no character-sheet view at all — stats/skills/level are only in the window title and console. A read-only sheet panel (reuse examine/loot panel rendering) is an S that makes both Q1 and Q2 legible.
3. **Ammo economy dead-end (pain: medium; size S-M).** Guns + reload are real (RangedMath, ViewerGame.cs:1142-1156) but supply is finite: merchants never restock (snapshot persistence overwrites the phase-4 restock; the engine's restock timers are exactly the timer class we deliberately clear on map exit). A small-guns build can permanently run dry and regress to fists. Minimal: per-merchant restock-after-N-game-days on map enter (compare GameClock day vs stored snapshot day; refresh that one container from pristine map data) — fits the existing snapshot machinery.

Not worth it now: poison/radiation ticking (no content in scope applies meaningful doses; geckos' poison is flavor), day/night ambient auto-curve already exists with manual override, XP cap concerns (curve to L99 is fine; content tops out ~L4 which the above don't change).

## Q6 — Verdict input

**Recommendation: progression-first (Q1 step-0 tag-bonus fix + level-up allocator + Q5's rest key), fold ops in as a half-day side dish (CI yaml + issue templates + SCOPE.md), defer the full creation screen behind it.**
Three strongest facts:
1. **There's a live correctness bug in the shipped build**: tagged skills get neither +20 nor double-rate (CritterState.cs:53-67 vs skill.cc:251-256), and the "diplomat" premade (Chitsa, gender byte = 1) renders with male art and male death screams (ViewerGame.cs:1288, 2453). Progression work fixes real defects, not just adds features — and the gender fix is an S.
2. **Skills now gate six systems but can never change** — the engine's loop (XP → 5+2×IN points → spend past the gates, character_editor.cc:5686-5688, skill.cc:355-371) is the single missing piece that converts our combat/lockpick/barter mechanics from a demo into a game; nothing in the ops track changes what a player feels.
3. **Ops is genuinely tiny and non-blocking**: the test split already self-skips (GameDataFactAttribute.cs:13) so the CI workflow is ~15 lines with no secrets; the perf canary cannot run meaningfully in CI anyway (no GPU, no game data — local-only per RELEASING.md); ecosystem is quiet (MonoGame 3.8.5 still preview.6, .NET 10.0.9 June servicing auto-picked-up on next publish, fallout2-ce provenance issues #428/#476 unmoved). There is no ops fire; there is a gameplay hole.
