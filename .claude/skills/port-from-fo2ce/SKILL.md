---
name: port-from-fo2ce
description: Use when implementing or reviewing Hexwaste engine logic that must match original Fallout 2 behavior — DAT2/FRM/PAL/MAP/PRO parsing, tile/screen math, draw order, or any game mechanic — before writing code that isn't a direct, cited port from reference/fallout2-ce.
---

# Porting from fallout2-ce

## Core principle

Hexwaste is a port, not a reimplementation. Every piece of engine behavior must
trace to a specific line in `reference/fallout2-ce`, never to a guess about how
Fallout 2 "probably" worked. **If a format detail can't be confirmed from
source, stop and ask — don't guess.**

## Two remotes, one authority

- `alexbatalov e97087b` (the default clone) — **authoritative for vanilla
  behavior.** This is what you port from by default.
- `community/main` (the maintained fork, fetched into the same clone) —
  **bug-fix candidate source only.** Diff it against the baseline:
  `git diff e97087b..community/main -- src/x.cc`. Port a fork change **only**
  when it corrects a misreading of the original game's behavior — never
  because the fork improved or changed it. The fork also carries deliberate
  non-vanilla QoL, often marked `// CE:` — that's out of scope, don't port it.

## Workflow

1. Find the function in `reference/fallout2-ce` (see file map below).
2. Read the whole function, not just the part that looks relevant — edge
   cases, byte order, offset accumulation, and shared-state quirks are usually
   in the lines you'd otherwise skip.
3. Port the algorithm faithfully: same branches, same edge-case handling, same
   constants. Do not "clean up" or "improve" logic while porting it.
4. Cite the source inline:
   `// ported from fallout2-ce src/tile.cc tileToScreenXY()`
5. If it's a fork fix you're porting, cite both:
   `// ported from fallout2-ce src/x.cc f() (community fix #NNN)`
6. Can't confirm a detail from either source? Stop. Ask the user instead of
   inferring from the compiled game's observed behavior or general isometric-
   engine assumptions.

## File map

| fallout2-ce source | Subsystem |
|---|---|
| `src/dfile.cc` | DAT2 archive format |
| `src/db.cc` | VFS — loose files override DAT contents |
| `src/art.cc` | FRM sprites |
| `src/color.cc`, `src/palette.cc` | PAL format + color cycling |
| `src/map.cc` | MAP files |
| `src/proto.cc` | PRO prototypes |
| `src/tile.cc` | hex/square grid ↔ screen math, draw order (most load-bearing file for rendering — read this one closest) |

## Common mistakes

| Mistake | Why it's wrong |
|---|---|
| Guessing a format detail "close enough" | The whole point of a port is bit-for-bit fidelity; a guess that happens to render correctly for one map can break silently on another |
| Citing a source file without checking which remote/commit it came from | `alexbatalov e97087b` and `community/main` can disagree; an uncited or mis-cited fork change looks like vanilla behavior in review |
| Porting a `// CE:` fork improvement | Deliberate non-vanilla QoL is explicitly out of scope even if it looks like a nice fix |
| "Improving" the ported logic while translating it | Introduces behavior that no longer matches the original engine, defeating fidelity |
