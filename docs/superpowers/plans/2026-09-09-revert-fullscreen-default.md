# Revert Fullscreen-By-Default's Default Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A plain interactive launch of Hexwaste opens windowed at 1280x720 again (matching every launch before yesterday's fullscreen-by-default feature), while keeping the fullscreen capability itself available via an explicit `--fullscreen` flag and the existing Alt+Enter runtime toggle.

**Architecture:** Invert `Program.cs`'s opt-in/opt-out flag from `windowed` (default off, `--windowed` opts OUT of fullscreen) to `fullscreen` (default off, `--fullscreen` opts IN) — a 3-line change. No changes to `ViewerGame.cs`'s already-correct fullscreen machinery.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`src/Hexwaste.Viewer`). No new dependencies.

## Global Constraints

- Only `src/Hexwaste.Viewer/Program.cs` changes — `ViewerGame.cs`'s `StartFullscreen` property, `Initialize()`, `ToggleFullscreen()`, and the Alt+Enter check are untouched (`docs/superpowers/specs/2026-09-09-revert-fullscreen-default-design.md`, Non-goals).
- Every CLI-driven screenshot/test/benchmark path must remain unaffected either way (they already force `interactiveLaunch = false`).
- `ViewerGame`/`Program.cs`'s launch wiring has no unit test project — verify via build + manual runs, not automated tests.

---

## File Structure

- Modify: `src/Hexwaste.Viewer/Program.cs` — 3 corresponding edits: the `windowed` variable declaration, the `--windowed` flag-parsing case, and the `StartFullscreen` wiring expression.

Single tiny change, one task.

---

### Task 1: Invert the fullscreen opt-in/opt-out flag

**Files:**
- Modify: `src/Hexwaste.Viewer/Program.cs`

**Interfaces:**
- No new interfaces — this only changes a local CLI-parsing variable's name/default and the boolean expression it feeds into `ViewerGame.StartFullscreen` (an existing property, unchanged).

- [ ] **Step 1: Rename and flip the flag variable**

Find, in `src/Hexwaste.Viewer/Program.cs`:

```csharp
bool windowed = false;
```

(currently `Program.cs:38`). Replace it with:

```csharp
bool fullscreen = false;
```

- [ ] **Step 2: Flip the flag-parsing case**

Find:

```csharp
        case "--windowed":
            windowed = true;
            break;
```

(currently `Program.cs:47-49`). Replace it with:

```csharp
        case "--fullscreen":
            fullscreen = true;
            break;
```

- [ ] **Step 3: Flip the `StartFullscreen` wiring**

Find:

```csharp
    StartFullscreen = interactiveLaunch && !windowed,
```

(currently `Program.cs:899`). Replace it with:

```csharp
    StartFullscreen = interactiveLaunch && fullscreen,
```

- [ ] **Step 4: Build to confirm no compile errors**

Run: `dotnet build src/Hexwaste.Viewer/Hexwaste.Viewer.csproj`
Expected: `Build succeeded.` with 0 errors (pre-existing nullable warnings, if any, are unrelated and fine).

- [ ] **Step 5: Manual verification**

1. **Plain launch is windowed (the primary fix):**

```bash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data &
sleep 8
WIN=$(xdotool search --name "Hexwaste" | head -1)
xdotool getwindowgeometry "$WIN"
```

Expected: geometry shows `1280x720` with a non-zero window position (i.e. a normal
titled window, not a `0,0` fullscreen window covering the whole screen).

2. **`--fullscreen` still opens fullscreen:**

```bash
kill %1 2>/dev/null || true
pkill -9 -f "Hexwaste.Viewer/bin" 2>/dev/null || true
sleep 1
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --fullscreen &
sleep 8
WIN=$(xdotool search --name "Hexwaste" | head -1)
xdotool getwindowgeometry "$WIN"
```

Expected: geometry shows the desktop's native resolution at position `0,0` (fullscreen).

3. **Alt+Enter still toggles correctly from the new windowed default:**

```bash
kill %1 2>/dev/null || true
pkill -9 -f "Hexwaste.Viewer/bin" 2>/dev/null || true
sleep 1
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data &
sleep 8
WIN=$(xdotool search --name "Hexwaste" | head -1)
xdotool windowactivate "$WIN"
sleep 0.3
xdotool key alt+Return
sleep 1.5
xdotool getwindowgeometry "$WIN"
```

Expected: after the toggle, geometry shows the desktop's native resolution at `0,0`
(now fullscreen) — confirming `ToggleFullscreen()` still works correctly starting from
the new windowed default.

```bash
kill %1 2>/dev/null || true
pkill -9 -f "Hexwaste.Viewer/bin" 2>/dev/null || true
```

4. **Screenshot probe unaffected:**

```bash
dotnet run --project src/Hexwaste.Viewer -- --game-dir game-data --map artemple.map --screenshot /tmp/revert-check.png
```

Expected: this command runs to completion and exits (a one-shot headless action, no
window left running), producing `/tmp/revert-check.png` at the same fixed 1280x720
dimensions this flag has always produced — confirming `interactiveLaunch = false` for
this invocation keeps `StartFullscreen` off regardless of this change.

- [ ] **Step 6: Commit**

```bash
git add src/Hexwaste.Viewer/Program.cs
git commit -m "$(cat <<'EOF'
fix(viewer): revert fullscreen-by-default's default to windowed

A plain launch felt "less crispy" than before -- root-caused to the
FOV/apparent-size effect of native fullscreen on a large monitor
(the world renders identically either way; UI text isn't measurably
blurrier), not a defect in the fullscreen feature itself or the
broader UI Scale work. Inverts the --windowed opt-out flag to a
--fullscreen opt-in flag, so a plain launch is windowed at 1280x720
again, matching every launch before yesterday's change. The
fullscreen capability itself (StartFullscreen, Initialize()'s setup,
ToggleFullscreen(), the Alt+Enter toggle) is untouched and still
available via --fullscreen or Alt+Enter.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

## Self-Review

**Spec coverage:**
- The 3 corresponding edits (variable, flag case, `StartFullscreen` wiring) → Steps 1-3. ✅
- `ViewerGame.cs` untouched → nothing in this plan touches it. ✅
- Testing section's 4 manual checks (windowed default, `--fullscreen` opt-in, Alt+Enter, screenshot probe) → Step 5. ✅

**Placeholder scan:** No TBD/TODO. Every step has concrete, runnable commands with
stated expected output.

**Type consistency:** `fullscreen` is a `bool` throughout (declaration, case, boolean
expression) — matches the type of the variable it replaces (`windowed`, also `bool`).
No signature or public interface changes anywhere in this plan.
