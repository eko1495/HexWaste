# AAF Font Premultiplied-Alpha Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix Hexwaste's AAF interface-font glyph atlas so partial-opacity pixels render correctly instead of washing out toward full brightness, restoring the letterform detail (e.g. lowercase 'a' vs 'o') that's currently lost.

**Architecture:** `AafFontRenderer`'s constructor bakes every glyph into one shared atlas texture. The texture is currently built as straight (non-premultiplied) alpha, but every draw call site uses MonoGame's default premultiplied-alpha blend state — a mismatch that washes out partial-opacity pixels. The fix premultiplies the atlas's RGB channels by alpha at construction time; no draw call site changes.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`src/Hexwaste.Viewer`). No new dependencies.

## Global Constraints

- Single-file fix: only `src/Hexwaste.Viewer/AafFontRenderer.cs`'s constructor changes. `AafFont.cs` (parsing) and every `SpriteBatch.Begin()` call site across the project are untouched (`docs/superpowers/specs/2026-09-08-aaf-font-premultiplied-alpha-design.md`, Scope and Non-goals sections).
- No automated test project covers `AafFontRenderer` (MonoGame dependency) — verify via build + manual screenshot comparison, not automated tests.

---

## File Structure

- Modify: `src/Hexwaste.Viewer/AafFontRenderer.cs` — constructor's glyph-unpacking loop, one line changed.

Single 3-line change in one file — one task.

---

### Task 1: Premultiply the AAF glyph atlas RGB by alpha

**Files:**
- Modify: `src/Hexwaste.Viewer/AafFontRenderer.cs`

**Interfaces:**
- No public interface changes — `AafFontRenderer`'s constructor signature, `Draw()`, `MeasureWidth()`, `WrapText()` are all unchanged. This task only changes what bytes get written into the internal `_atlas` texture.

- [ ] **Step 1: Change the atlas RGB fill from flat white to alpha-scaled**

Find, in `src/Hexwaste.Viewer/AafFontRenderer.cs`, inside the constructor's glyph-unpacking loop:

```csharp
                    byte level = glyph.Pixels[y * glyph.Width + x];
                    if (level == 0)
                        continue;
                    int pixel = ((cellY + top + y) * atlasWidth + cellX + x) * 4;
                    byte alpha = (byte)Math.Min(level * 255 / font.MaxLevel, 255);
                    rgba[pixel] = rgba[pixel + 1] = rgba[pixel + 2] = 255;
                    rgba[pixel + 3] = alpha;
```

(currently `AafFontRenderer.cs:43-49`, inside the constructor that starts at line 22). Replace it with:

```csharp
                    byte level = glyph.Pixels[y * glyph.Width + x];
                    if (level == 0)
                        continue;
                    int pixel = ((cellY + top + y) * atlasWidth + cellX + x) * 4;
                    byte alpha = (byte)Math.Min(level * 255 / font.MaxLevel, 255);
                    rgba[pixel] = rgba[pixel + 1] = rgba[pixel + 2] = alpha;
                    rgba[pixel + 3] = alpha;
```

The only change is the RGB-fill line: `= 255;` becomes `= alpha;`. This premultiplies each
pixel's RGB by its own alpha — the standard requirement for MonoGame's default
`BlendState.AlphaBlend`, which every existing `SpriteBatch.Begin()` call site already uses
and which this task does not touch.

- [ ] **Step 2: Build to confirm no compile errors**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors (pre-existing nullable warnings, if any, are
unrelated and fine).

- [ ] **Step 3: Manual verification — inventory screen legibility repro**

This reproduces the exact bug found during today's fo2ce comparison: the resistance-list
labels in the inventory summary panel read "Loser"/"Plosmo" instead of "Laser"/"Plasma".

Launch Hexwaste windowed (simpler to screenshot than fullscreen, per this session's
established pattern) and drive it into the inventory screen using mouse clicks (main-menu
keyboard hotkeys are not wired, a known pre-existing gap — see
`docs/fo2ce-comparison-playbook.md` and prior session findings):

```bash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --windowed &
sleep 8
```

Find the window and click through NEW GAME → TAKE (Norg, the premade tribal) → open
inventory:

```bash
WIN=$(xdotool search --name "Hexwaste" | head -1)
xdotool windowactivate "$WIN"
sleep 0.3
xdotool mousemove 275 148
sleep 0.2
xdotool mousedown 1
sleep 0.15
xdotool mouseup 1
sleep 1.5
xdotool mousemove 213 379
sleep 0.2
xdotool mousedown 1
sleep 0.15
xdotool mouseup 1
sleep 2
xdotool key i
sleep 1
spectacle -b -n -o /tmp/aaf-fix-inventory-check.png
```

(The exact click coordinates above are for a 1280x720 window with the main menu's NEW
GAME button and the character-pick screen's TAKE button at their standard positions,
matching this session's established click pattern — if the window doesn't respond as
expected, screenshot first with `spectacle -b -n -o /tmp/aaf-fix-menu-check.png` and
read it with the Read tool to find the actual button positions before clicking.)

Read `/tmp/aaf-fix-inventory-check.png` with the Read tool. Expected: the resistance
list reads "Normal / Laser / Fire / Plasma / Explode" — every letter legible, lowercase
'a' visually distinct from 'o'. If desired, crop and upscale the resistance-list region
for a closer look:

```bash
convert /tmp/aaf-fix-inventory-check.png -crop 300x120+1030+480 -resize 1200x480 /tmp/aaf-fix-zoom.png
```

(Adjust the crop offset if the panel isn't in that exact region — check the full
screenshot first.) Read `/tmp/aaf-fix-zoom.png` and confirm "Laser" and "Plasma" are
both clearly legible with correctly-shaped 'a' characters.

- [ ] **Step 4: Manual verification — general spot-check on another screen**

While still running, close the inventory (Esc or click DONE) and check another screen
with lowercase text rendered via `font1.aaf` — e.g. the Skilldex (key `S`) or Pip-Boy
(key `P`) — to confirm text elsewhere still renders correctly with no regressions (no
washed-out or overly dark glyphs, drop shadows still visible, colored/tinted text still
correctly colored).

```bash
xdotool key Escape
sleep 0.5
xdotool key s
sleep 1
spectacle -b -n -o /tmp/aaf-fix-skilldex-check.png
```

Read `/tmp/aaf-fix-skilldex-check.png` with the Read tool. Expected: skill names and
values render clearly, drop shadows are still present (not missing or doubled), no
visual artifacts introduced by the premultiplied-alpha change.

Kill the running instance when done:

```bash
kill %1 2>/dev/null || true
pkill -9 -f "Hexwaste.Viewer/bin" 2>/dev/null || true
```

- [ ] **Step 5: Commit**

```bash
git add src/Hexwaste.Viewer/AafFontRenderer.cs
git commit -m "$(cat <<'EOF'
fix(viewer): premultiply the AAF glyph atlas alpha

AafFontRenderer built its glyph atlas as straight (non-premultiplied)
alpha -- flat white RGB, opacity only in the alpha channel -- but
every SpriteBatch.Begin() call site uses MonoGame's default
premultiplied-alpha blend state. That mismatch washed out
partial-opacity glyph pixels toward full brightness, destroying the
corner-taper detail that distinguishes letters like 'a' from 'o' at
font1.aaf's tiny glyph sizes (confirmed via a real fo2ce comparison:
"Laser"/"Plasma" read as "Loser"/"Plosmo"). Fix: premultiply the
atlas RGB by alpha at construction time -- one line, no call-site
changes, since the existing default blend state is correct once the
texture data matches what it expects.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

## Self-Review

**Spec coverage:**
- The 3-line RGB-fill change → Step 1. ✅
- "No call-site changes needed" constraint → Step 1's replacement leaves every other
  file untouched; no other file appears anywhere in this plan. ✅
- Testing section's two manual checks (inventory legibility repro, general spot-check)
  → Steps 3-4. ✅
- Non-goals (no `AafFont.cs` change, no blend-state argument added anywhere) → nothing
  in this plan touches either. ✅

**Placeholder scan:** No TBD/TODO. Step 3's click coordinates carry an explicit
fallback ("if the window doesn't respond as expected, screenshot first... before
clicking") rather than assuming they'll always be exactly right — a grounded
contingency, not a vague placeholder.

**Type consistency:** N/A — this task changes byte values inside an existing loop, no
new types, methods, or signatures introduced anywhere.
