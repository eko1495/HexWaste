# Phase-7 Track D — Ship-First Case + "What We're Not Seeing"

Date: 2026-06-12. Method: read-only repo audit (release.sh dry-run executed and cleaned up),
web research current to mid-June 2026. Repo: /home/eko/dev/FPOC (untagged, main, clean tree).

## 1. Publication dry-run audit (vs docs/RELEASING.md)

### 1a. `git ls-files` — 101 tracked files. Issues found

| Severity | Item | Detail |
|---|---|---|
| MUST FIX | `tools/__pycache__/int_analyze.cpython-314.pyc` is **tracked** | A compiled Python binary in git. `.gitignore` has no `__pycache__/` entry (verified with `git check-ignore`, exit 1). It also embeds the `/home/eko/...` path. Remove + ignore. |
| SHOULD FIX | `tools/int_analyze.py:36` | `SRC = "/home/eko/dev/FPOC/reference/fallout2-ce/src"` hardcoded. Parameterize via env/arg before shipping a "tool". |
| COSMETIC | `/home/eko/dev/FPOC/...` paths in docs/research-notes/p6-track-{a,b,c,d}.md headers (+ `/tmp/p6*` probe paths) | Provenance notes; leaks a username + machine layout but nothing sensitive. RELEASING.md:39 already says to audit docs for machine-local paths — this is exactly that. Either scrub the 6 header lines or accept (low risk). |
| OK | user email | NOT in any tracked file content (`git grep` clean). It IS the author/committer identity on every commit — moot under the fresh-history plan (RELEASING.md:20-37), but the new initial commit will carry whatever `user.email` is set at publish time. Decide deliberately. |
| OK | game-derived text | No bulk extracted game text in tracked files; research notes contain only short paraphrases of engine messages ("you gain X exp"). The .gitignore (city.txt/worldmap.txt/maps.txt/*.msg/*.lst/*.gam + binaries) holds. Fresh-history plan covers the early-history leak. |
| NOTE | CLAUDE.md ships | Contains internal working-style/milestone notes (incl. the research-report filename `compass_artifact_…`). Nothing legally sensitive, but it reads as an internal doc; consider whether it belongs in the public repo or only in the dev repo. |

### 1b. scripts/release.sh — RUNS END-TO-END TODAY (exit 0)

Ran `scripts/release.sh 0.0.0-audit`, then verified and `rm -rf`'d artifacts/v0.0.0-audit:
- Both RIDs publish: 37 MB tar.gz (linux-x64) + 37 MB zip (win-x64), ~201 files/folder.
- LICENSE.md + NOTICE.md + README.md present in BOTH folders; `Hexwaste.Viewer.exe` present in win-x64.
- Exec bit preserved in tar (`-rwxr-xr-x ... Hexwaste.Viewer`).
- `tar -tzf | grep -iE '\.dat$|\.map$|\.frm$|\.pal$|game-data'` → **no game assets** in artifacts.
- Minor: `.pdb` files ship (Hexwaste.Formats.pdb, Hexwaste.Viewer.pdb) and `createdump` — harmless, slightly fatter; could strip with `-p:DebugType=none`. Build log on this machine is Polish-locale (cosmetic).
- artifacts/ is gitignored (verified; only v0.5.0 leftover sits there locally).

### 1c. README claims vs reality

- Quick-start verbatim: `dotnet run --project src/Hexwaste.Viewer -- --game-dir "$PWD/game-data"` → works, exit 0, screenshot produced (/tmp/p7d-quickstart.png path tested with `--screenshot`).
- Controls table audited against src/Hexwaste.Viewer/ViewerGame.cs key handling (lines ~809-997): F5/F9, I, M, R, T, L, F, Space, PgUp/PgDn, [ ], Esc, arrows+Shift — **all match**. No undocumented gameplay keys found.
- Test claim: `dotnet test` with FALLOUT2_DIR → **114/114 pass, 0 skipped**, ~2 s.
- README accurately describes feature set incl. phase 6 (barter, GCD stats, scripted aggro).

### 1d. Missing for a stranger

- **Screenshots: none in README — the single biggest gap.** Practice check: devilutionX README has gameplay screenshots (https://github.com/diasurgical/devilutionX — screenshots + "buy Diablo from GoG" note; latest 1.5.5, Oct 2025, no takedown ever). OpenMW's GitHub README has none (screens live on openmw.org). fallout2-ce README has none. So screenshots are common-and-tolerated (devilutionX, OpenRA, OpenTTD style) but not universal; for a project whose whole pitch is "it renders the real game," 2-3 screenshots + a short walk/dialog/combat GIF are near-free credibility. Game-art screenshots of a legally-owned copy in a README have not drawn takedowns in this scene.
- **CHANGELOG: none.** A short keep-a-changelog style file seeded from the phase milestones costs <1 h.
- **Version tags: `git tag` is empty.** Scheme suggestion: `v0.6.0` (phase = minor) matches the existing release.sh `v$VERSION` artifact naming. Tag at the fresh-history initial commit.
- **No CI** (no .github/ tracked). A build-only workflow (no game data; tests are env-gated by design, GameDataFactAttribute) is cheap and is the badge strangers look for.
- release.sh has no macOS RID (osx-x64/arm64) — fallout2-ce/devilutionX both ship mac; defer, but expect the issue.

## 2. Ship-now-vs-later

**Audience (realistic):** (1) fallout2-ce orbit — NMA forums, r/classicfallout, modders who know the formats and can falsify our DAT2/FRM/INT-VM claims — highest-value feedback (correctness bugs, weird maps, non-GOG installs); (2) C#/MonoGame engine-dev hobbyists — feedback on architecture, packaging, platform breakage (Windows paths, case sensitivity, non-US locales); (3) r/Fallout at large — screenshots travel, feedback shallow ("can I play the whole game?"). A v0.6 generates: "map X renders wrong / script Y crashes / doesn't find my Steam install" reports — exactly the long-tail coverage we can't generate alone with one GOG copy on one Linux box.

**Downside, honestly:** (a) SUL gray zone — upstream's own posture is murky: issue #428 ("Clarification on licensing needed", Dec 2024, closed with no maintainer reply — OpenBSD ports couldn't get an answer: https://github.com/alexbatalov/fallout2-ce/issues/428) and #476 ("Is this a decompilation?", Mar 2025, **open, unanswered**: https://github.com/alexbatalov/fallout2-ce/issues/476). We inherit that ambiguity; our NOTICE.md/attribution-comment trail is better than upstream's median derivative, and fallout2-ce itself has sat at 2.3k stars since 2022, packaged in nixpkgs/AUR, untouched. Risk = low, nonzero, and NOT reduced by waiting. (b) Issue churn for missing features (ranged combat, full quests, mac build) — mitigated by a blunt "what this is / isn't" scope section + issue template; (c) first impression is permanent-ish — argues for the screenshot/front-door items below, not for phase 7.

**Max first-impression value per day, pre-publication:**
1. README screenshots + 10 s GIF (≤0.5 day — `--screenshot` infra already exists).
2. Repo hygiene batch: drop .pyc, ignore `__pycache__/`, fix int_analyze.py path, scrub /home/eko from notes, CHANGELOG.md, decide tag scheme (≤0.5 day).
3. Minimal front door: main-menu (New Game / Load / Quit over art/intrface FRM) + a real death screen instead of "game over → F9" (1-2 days). This is the one *feature* that changes a stranger's first 60 seconds.

## 3. Web checks (mid-2026, sourced)

- **fallout2-ce**: latest release still v1.3 (Apr 21, 2024); repo alive (issues into 2026), 2.3k stars/181 forks, SUL license unchanged, README has no screenshots. https://github.com/alexbatalov/fallout2-ce. Issue #428 closed without maintainer answer; #476 still open/unanswered (URLs above). No license change, no takedown.
- **Takedowns/climate**: no DMCA/C&D against any Fallout engine re-implementation found 2025-2026. Vault 13 (F1-in-F4 remake) died Oct 2024 from burnout, explicitly NOT a C&D (https://www.thegamer.com/vault-13-fan-made-fallout-remake-cancelled/). Microsoft/Bethesda stance read as supportive — Techdirt Apr 2025 (https://www.techdirt.com/2025/04/25/microsoft-allows-bethesda-to-continue-to-be-cool-regarding-fan-made-remake-projects/); Bethesda even hired Fallout: London modders; the FOLON friction is about surprise Fallout 4 patches breaking the mod (Nov 2025 title update), not legal action (https://www.pcgamer.com/games/fallout/fallout-londons-project-lead-is-not-taking-the-surprise-drop-of-fallout-4s-update-well-that-has-for-a-lack-of-a-better-term-screwed-us-over/).
- **MonoGame**: 3.8.5 is STILL IN PREVIEW (preview.6, May 22, 2026; preview series since Dec 19, 2025 — https://monogame.net/blog/2025-12-19-385-preview/, https://monogame.net/blog/2026-01-02-MonoGame385.preview.2-release/; May 2026 Open Hours discussed "3.8.5 Release Final" as upcoming — https://monogame.net/blog/2026-05-19-open-hours-may-2026/). 3.9 (Vulkan/DX12) is later. We pin `MonoGame.Framework.DesktopGL 3.8.4.1` (src/Hexwaste.Viewer/Hexwaste.Viewer.csproj:11) — **keep the pin; do not bump to a preview before publishing.**
- **.NET 10**: LTS, current servicing 10.0.9 (June 9, 2026, two CVEs patched), supported through Nov 14, 2028 (https://devblogs.microsoft.com/dotnet/dotnet-and-dotnet-framework-june-2026-servicing-updates/, https://endoflife.date/dotnet). net10.0-only target is safe to ship.

## 4. Gap to "a stranger enjoys 30 minutes" (ranked by first-impression value)

1. **Front door / main menu** — today we boot straight into artemple. Smallest viable: 3-item text menu (New Game / Load Game / Quit) over the game's own mainmenu art; AAF renderer + panel code exist. **S/M, highest value** — it's the first thing anyone sees.
2. **Death/difficulty feedback** — "lose → F9" is per CLAUDE.md; a death overlay ("You have died — Enter: load last save") is hours of work, prevents the most common bad first session (die in Temple of Trials, think the game hung). **S, high value.**
3. **Premade character picker** — the game ships premade/{combat,diplomat,stealth}.gcd; GcdFile.Load (src/Hexwaste.Formats/Combat/GcdFile.cs) already parses Stats/Name/TaggedSkills/Traits/RemainingCharPoints, so a picker is pure UI. **S, good value** — instantly exercises the stat-gated dialog we already brag about.
4. **Full SPECIAL allocation screen** — gcd makes the data side trivial (RemainingCharPoints is parsed; we never need to WRITE a gcd, just build CritterProtoStats in memory). The cost is UI + validation (1-10 clamps, point pool, tag-skill picks). **M (2-4 days).** Worth it eventually; the premade picker captures 70 % of the value for 20 % of the work.
5. **Skill points on level-up** — engine grants per-level skill points (editor/stat machinery); we award HP only (src/Hexwaste.Formats/Combat/Progression.cs — XpForLevel/HpPerLevel, nothing else). A silent auto-spend into tagged skills is coherent and invisible (S); a text allocator is M. Low first-impression value — strangers won't notice in 30 min. Defer to phase 7.
6. **Perks** — pure scope creep for now: the engine's perk table mostly modifies subsystems we don't have (ranged combat, criticals, AP economy); the few stat perks (Toughness, Lifegiver) are invisible in a 30-minute session. **Skip.**

## 5. Verdict

**Ship v0.6 first.** Pre-publication items (≈2-3 days total): (1) README screenshots + demo GIF, (2) the hygiene batch (.pyc/__pycache__, int_analyze.py path, /home/eko scrub, CHANGELOG, v0.6.0 tag), (3) main menu + death screen. Land phase 7 (ranged combat, char creation) as v0.7 against real user feedback.

Three strongest facts:
1. **Ship-readiness is verified, not aspirational**: release.sh ran end-to-end today (both archives, licenses in, exec bit, zero game assets), the README quick-start works verbatim, controls table matches the code, 114/114 tests green. The remaining blockers are one tracked .pyc and missing screenshots — days, not weeks.
2. **The legal window is as good as it gets**: no takedown of any Fallout engine re-implementation through mid-2026, fallout2-ce sits untouched at 2.3k stars for 4 years, Microsoft/Bethesda are publicly fan-project-friendly (hired FOLON staff). Waiting doesn't shrink the SUL gray zone (upstream won't even answer #428/#476); our NOTICE + attribution trail already exceeds scene norms.
3. **Phase 7's headline (ranged combat) doesn't change what early feedback measures**: the audience that matters (fallout2-ce/NMA format experts) will test map coverage, installs, and VM correctness — feedback that compounds the longer the project is public, and that we cannot self-generate on one GOG copy / one Linux box. The only features that change a stranger's first 30 minutes are the front door and death feedback — both small enough to do pre-ship.
