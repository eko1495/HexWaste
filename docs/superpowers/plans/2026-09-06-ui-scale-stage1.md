# UI Scale Stage 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the pure, unit-tested scale/virtual-viewport/mouse-transform math and its thin MonoGame-typed Viewer wrapper that later (separately-specced) stages will use to make Hexwaste's UI scale to fill the window like `fallout2-ce` does — with zero visible behavior change in this stage.

**Architecture:** A new pure static class `Hexwaste.Formats.Rendering.UiScale` (zero MonoGame dependency, directly unit-testable) holds the three arithmetic operations. A new partial-class file `ViewerGame.UiScale.cs` in `Hexwaste.Viewer` wraps it with real `GraphicsDevice.Viewport`/`Mouse.GetState()` calls, returning MonoGame types. Nothing existing calls the new Viewer-side methods yet.

**Tech Stack:** C# / .NET (pure math, xUnit-tested), MonoGame types (`Rectangle`, `Point`) in the thin wrapper only.

## Global Constraints

- `ComputeScale(viewportWidth, viewportHeight, baseWidth = 640, baseHeight = 480)` = `Math.Max(0.1f, Math.Min(viewportWidth / (float)baseWidth, viewportHeight / (float)baseHeight))` — a uniform (not per-axis) scale, clamped to a `0.1f` minimum against a degenerate near-zero viewport.
- `ComputeVirtualViewport(viewportWidth, viewportHeight, scale)` = `((int)(viewportWidth / scale), (int)(viewportHeight / scale))` — truncating, matching this codebase's existing tolerance for sub-pixel imprecision in similar centering math.
- `TransformMouse(rawX, rawY, scale)` = `((int)(rawX / scale), (int)(rawY / scale))` — no offset term: the virtual viewport is derived from the real one divided by scale, so scaling it back up exactly tiles the real screen with no letterbox gap to offset for.
- The pure math lives in `src/Hexwaste.Formats/Rendering/UiScale.cs` (new folder, following the existing `Hexwaste.Formats/Hex`, `Hexwaste.Formats/Light` pattern of small, focused concept folders) — zero MonoGame dependency, so it is unit-testable in `tests/Hexwaste.Formats.Tests` with no `FALLOUT2_DIR`/display guard needed.
- The Viewer wrapper lives in a new `src/Hexwaste.Viewer/ViewerGame.UiScale.cs` partial-class file, following this project's one-partial-file-per-concern convention (`ViewerGame.Hud.cs`, `ViewerGame.Panels.cs`, etc.).
- **Nothing in this plan wires the new methods into any existing `SpriteBatch.Begin` call, any existing `GraphicsDevice.Viewport` reference, or any existing `Mouse.GetState()` hit-test.** Zero visible rendering change, zero golden fixture impact. That wiring is later, separately-specced stages.

---

### Task 1: The pure scale/viewport/mouse-transform math

**Files:**
- Create: `src/Hexwaste.Formats/Rendering/UiScale.cs`
- Create: `tests/Hexwaste.Formats.Tests/UiScaleTests.cs`

**Interfaces:**
- Produces: `Hexwaste.Formats.Rendering.UiScale` — a static class with three public static methods:
  - `float ComputeScale(int viewportWidth, int viewportHeight, int baseWidth = 640, int baseHeight = 480)`
  - `(int Width, int Height) ComputeVirtualViewport(int viewportWidth, int viewportHeight, float scale)`
  - `(int X, int Y) TransformMouse(int rawX, int rawY, float scale)`

  Task 2 consumes all three by exactly these names and signatures.

- [ ] **Step 1: Write the failing tests**

Create `tests/Hexwaste.Formats.Tests/UiScaleTests.cs`:

```csharp
using Hexwaste.Formats.Rendering;

namespace Hexwaste.Formats.Tests;

public class UiScaleTests
{
    [Fact]
    public void ComputeScale_ExactBaseSize_ReturnsOne()
    {
        Assert.Equal(1.0f, UiScale.ComputeScale(640, 480));
    }

    [Fact]
    public void ComputeScale_ExactDoubleSize_ReturnsTwo()
    {
        Assert.Equal(2.0f, UiScale.ComputeScale(1280, 960));
    }

    [Fact]
    public void ComputeScale_HeightLimiting_Returns1_5For1280x720()
    {
        // width ratio 1280/640=2.0, height ratio 720/480=1.5 -- height is the stricter (smaller) ratio.
        Assert.Equal(1.5f, UiScale.ComputeScale(1280, 720));
    }

    [Fact]
    public void ComputeScale_WidthLimiting_Returns1_0For640x1000()
    {
        // width ratio 640/640=1.0, height ratio 1000/480=2.0833 -- width is the stricter (smaller) ratio.
        Assert.Equal(1.0f, UiScale.ComputeScale(640, 1000));
    }

    [Fact]
    public void ComputeScale_DegenerateViewport_ClampsToMinimum()
    {
        Assert.Equal(0.1f, UiScale.ComputeScale(1, 1));
    }

    [Fact]
    public void ComputeVirtualViewport_HeightLimiting_HeightMatchesBase()
    {
        // 1280x720 -> scale 1.5 (height-limiting, see ComputeScale test above).
        (int w, int h) = UiScale.ComputeVirtualViewport(1280, 720, 1.5f);
        Assert.Equal(853, w); // 1280 / 1.5 = 853.33, truncated -- has slack past the 640 base
        Assert.Equal(480, h); // 720 / 1.5 = 480 exactly -- the limiting axis reproduces the base size
    }

    [Fact]
    public void ComputeVirtualViewport_WidthLimiting_WidthMatchesBase()
    {
        // 640x1000 -> scale 1.0 (width-limiting, see ComputeScale test above).
        (int w, int h) = UiScale.ComputeVirtualViewport(640, 1000, 1.0f);
        Assert.Equal(640, w); // the limiting axis reproduces the base size exactly
        Assert.Equal(1000, h); // has slack past the 480 base
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(640, 480)]
    [InlineData(1024, 768)]
    public void ComputeVirtualViewport_RoundTripsWithinOnePixel(int viewportWidth, int viewportHeight)
    {
        float scale = UiScale.ComputeScale(viewportWidth, viewportHeight);
        (int vw, int vh) = UiScale.ComputeVirtualViewport(viewportWidth, viewportHeight, scale);
        Assert.True(Math.Abs(vw * scale - viewportWidth) <= 1f);
        Assert.True(Math.Abs(vh * scale - viewportHeight) <= 1f);
    }

    [Fact]
    public void TransformMouse_OriginMapsToOrigin()
    {
        Assert.Equal((0, 0), UiScale.TransformMouse(0, 0, 2.0f));
    }

    [Fact]
    public void TransformMouse_ScalesDownByFactor()
    {
        Assert.Equal((320, 240), UiScale.TransformMouse(640, 480, 2.0f));
    }

    [Fact]
    public void TransformMouse_RealViewportCenterMapsToVirtualViewportCenter_HeightLimiting()
    {
        float scale = UiScale.ComputeScale(1280, 720);
        (int vw, int vh) = UiScale.ComputeVirtualViewport(1280, 720, scale);
        (int mx, int my) = UiScale.TransformMouse(1280 / 2, 720 / 2, scale);
        Assert.True(Math.Abs(mx - vw / 2) <= 1);
        Assert.True(Math.Abs(my - vh / 2) <= 1);
    }

    [Fact]
    public void TransformMouse_RealViewportCenterMapsToVirtualViewportCenter_WidthLimiting()
    {
        float scale = UiScale.ComputeScale(640, 1000);
        (int vw, int vh) = UiScale.ComputeVirtualViewport(640, 1000, scale);
        (int mx, int my) = UiScale.TransformMouse(640 / 2, 1000 / 2, scale);
        Assert.True(Math.Abs(mx - vw / 2) <= 1);
        Assert.True(Math.Abs(my - vh / 2) <= 1);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Hexwaste.Formats.Tests -c Debug --filter UiScaleTests`
Expected: build FAILS (`error CS0246: The type or namespace name 'UiScale' could not be found` or similar) — `UiScale.cs` doesn't exist yet.

- [ ] **Step 3: Write the implementation**

Create `src/Hexwaste.Formats/Rendering/UiScale.cs`:

```csharp
namespace Hexwaste.Formats.Rendering;

/// <summary>
/// Pure math for scaling Hexwaste's UI to fill an arbitrary window, matching fallout2-ce's own
/// stretched-buffer presentation but WITHOUT its non-uniform (aspect-distorting) stretch: fo2ce
/// stretches a fixed 640x480 buffer independently on each axis to fill any window shape;
/// Hexwaste instead picks one UNIFORM scale (the smaller of the two axis ratios) and lets the
/// "virtual" canvas extend past 640x480 on whichever axis has slack, avoiding pixel distortion.
/// Zero MonoGame dependency (Hexwaste.Formats is a pure .NET library) so this is directly
/// unit-testable; Hexwaste.Viewer's ViewerGame.UiScale.cs wraps these with real
/// GraphicsDevice.Viewport / Mouse.GetState() values.
/// </summary>
public static class UiScale
{
    /// <summary>The uniform scale factor for a viewport of the given size, relative to a
    /// baseWidth x baseHeight (640x480 by default) reference canvas. Clamped to a 0.1 minimum
    /// so a degenerate near-zero viewport can't produce zero/negative/NaN downstream.</summary>
    public static float ComputeScale(int viewportWidth, int viewportHeight, int baseWidth = 640, int baseHeight = 480)
    {
        float scale = Math.Min(viewportWidth / (float)baseWidth, viewportHeight / (float)baseHeight);
        return Math.Max(0.1f, scale);
    }

    /// <summary>The "virtual" (pre-scale) canvas size a viewport of the given real size and
    /// scale corresponds to. The axis that was limiting in ComputeScale reproduces the base
    /// dimension exactly; the other axis comes back larger than the base, giving 640x480-sized
    /// content room to center within a taller/wider canvas via (virtualWidth-640)/2 exactly as
    /// today's un-scaled centering math already does.</summary>
    public static (int Width, int Height) ComputeVirtualViewport(int viewportWidth, int viewportHeight, float scale) =>
        ((int)(viewportWidth / scale), (int)(viewportHeight / scale));

    /// <summary>Converts a real (window-pixel) mouse position into the same virtual-canvas
    /// coordinate space as ComputeVirtualViewport, so existing hit-test code (written against
    /// un-scaled logical coordinates) keeps working once rendering is scaled. No offset term is
    /// needed: the virtual viewport is DERIVED from the real one divided by scale, so scaling it
    /// back up exactly tiles the real screen with no letterbox gap to account for.</summary>
    public static (int X, int Y) TransformMouse(int rawX, int rawY, float scale) =>
        ((int)(rawX / scale), (int)(rawY / scale));
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Hexwaste.Formats.Tests -c Debug --filter UiScaleTests`
Expected: all 13 tests pass, e.g. `Passed! - Failed: 0, Passed: 13, Skipped: 0`.

- [ ] **Step 5: Run the full Formats test suite to confirm no regression**

Run: `dotnet test tests/Hexwaste.Formats.Tests -c Debug`
Expected: all tests pass (the pre-existing suite plus the 13 new ones) — this new file adds no dependency that could affect unrelated tests, but confirming is cheap.

- [ ] **Step 6: Commit**

```bash
git add src/Hexwaste.Formats/Rendering/UiScale.cs tests/Hexwaste.Formats.Tests/UiScaleTests.cs
git commit -m "$(cat <<'EOF'
feat(formats): add UiScale, the pure math for scaling the UI to the window

First step toward matching fallout2-ce's stretched-to-fill
presentation (Hexwaste currently renders every UI screen at native
pixel size, letterboxed in the window). ComputeScale picks a single
UNIFORM scale factor (the smaller of the width/height ratios against
a 640x480 reference) rather than fo2ce's own non-uniform per-axis
stretch, deliberately avoiding the aspect distortion that comes with
matching fo2ce exactly. ComputeVirtualViewport and TransformMouse
are its inverse operations for centering content and un-scaling real
mouse input, respectively.

Zero MonoGame dependency, so it's directly unit-tested here in
Hexwaste.Formats.Tests. Nothing calls this yet -- the Viewer-side
wrapper (next commit) and the actual rendering/hit-test wiring
(separately-specced later stages) are still to come.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: The Viewer-side wrapper

**Files:**
- Create: `src/Hexwaste.Viewer/ViewerGame.UiScale.cs`

**Interfaces:**
- Consumes: `Hexwaste.Formats.Rendering.UiScale.ComputeScale`, `.ComputeVirtualViewport`, `.TransformMouse` (Task 1).
- Produces: three new private/internal instance methods on `ViewerGame` — `float UiScale()`, `Rectangle VirtualViewport()`, `Point UiMouse()` — for later, separately-specced stages to call. Nothing in this task calls them.

- [ ] **Step 1: Confirm the class/namespace context to match**

Run: `head -20 src/Hexwaste.Viewer/ViewerGame.Hud.cs`
Expected: a `namespace Hexwaste.Viewer;` declaration and `public sealed partial class ViewerGame` — confirms the partial-class pattern this new file must match. If the namespace or class declaration differs from this, stop and report rather than guessing.

- [ ] **Step 2: Write the wrapper file**

Create `src/Hexwaste.Viewer/ViewerGame.UiScale.cs`:

```csharp
using Hexwaste.Formats.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

public sealed partial class ViewerGame
{
    /// <summary>The current UI scale factor for this window size — see
    /// Hexwaste.Formats.Rendering.UiScale.ComputeScale. Not yet used by any draw call; later,
    /// separately-specced stages wire it into SpriteBatch.Begin's transformMatrix.</summary>
    private float UiScale()
    {
        Viewport vp = GraphicsDevice.Viewport;
        return Formats.Rendering.UiScale.ComputeScale(vp.Width, vp.Height);
    }

    /// <summary>The "virtual" (pre-scale) canvas for this window size — see
    /// Hexwaste.Formats.Rendering.UiScale.ComputeVirtualViewport. Not yet used by any layout
    /// code; later stages replace existing GraphicsDevice.Viewport.Bounds references with this
    /// wherever content is drawn through the scaled batch.</summary>
    private Rectangle VirtualViewport()
    {
        Viewport vp = GraphicsDevice.Viewport;
        (int w, int h) = Formats.Rendering.UiScale.ComputeVirtualViewport(vp.Width, vp.Height, UiScale());
        return new Rectangle(0, 0, w, h);
    }

    /// <summary>The real mouse position transformed into the same virtual-canvas coordinate
    /// space as VirtualViewport — see Hexwaste.Formats.Rendering.UiScale.TransformMouse. Not yet
    /// used by any hit-test; later stages replace existing Mouse.GetState().X/Y position reads
    /// with this wherever a click is tested against scaled content. Callers that need button-
    /// press state (e.g. Mouse.GetState().LeftButton) keep calling Mouse.GetState() directly for
    /// that — only the position needs transforming.</summary>
    private Point UiMouse()
    {
        MouseState mouse = Mouse.GetState();
        (int x, int y) = Formats.Rendering.UiScale.TransformMouse(mouse.X, mouse.Y, UiScale());
        return new Point(x, y);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.` — the three new methods are currently unused (private, uncalled), so expect a compiler warning like `CS0102`/unused-member is NOT typical for C# (unused private methods don't warn by default), but do check for zero errors.

- [ ] **Step 4: Confirm zero behavior change via the golden suite**

Run:
```bash
FALLOUT2_DIR=$(pwd)/game-data DISPLAY=:0 scripts/opening-golden.sh check
```
Expected: `golden opening: ALL PASS`. This is a cheap, fast suite; since nothing calls the new methods, this run should be byte-for-byte identical to before this task — it exists purely to prove that adding this file introduced no accidental side effect (e.g. a static initializer, a namespace collision).

- [ ] **Step 5: Manually verify the app still runs and screenshots identically**

Run:
```bash
scripts/hexwaste-checkpoint.sh /tmp/ui-scale-stage1-check.png -- --menu
```
Then view `/tmp/ui-scale-stage1-check.png` with the Read tool.
Expected: the main menu renders exactly as it did before this plan (native pixel size, centered, gold button text) — no visible change of any kind, since `UiScale()`/`VirtualViewport()`/`UiMouse()` are not called anywhere yet.

- [ ] **Step 6: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.UiScale.cs
git commit -m "$(cat <<'EOF'
feat(viewer): add the UiScale/VirtualViewport/UiMouse wrapper methods

Thin MonoGame-typed wrappers around Hexwaste.Formats.Rendering.UiScale
using the real GraphicsDevice.Viewport and Mouse.GetState() -- the
Viewer-side half of the shared infrastructure for scaling the UI to
match fallout2-ce's stretched-to-fill presentation.

Nothing calls these three methods yet. Verified zero behavior change:
opening-golden.sh passes clean and a fresh main-menu screenshot is
visually identical to before this commit. Wiring them into actual
rendering (SpriteBatch.Begin's transform) and hit-testing
(Mouse.GetState() call sites) is later, separately-specced stages,
starting with the dialog and main-menu screens.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review Notes

- **Spec coverage:** the spec's three operations (`ComputeScale`, `ComputeVirtualViewport`, `TransformMouse`) are all implemented in Task 1 with the exact signatures specified, and unit-tested per the spec's required coverage list (width-limiting, height-limiting, exact-4:3, degenerate clamp, round-trip, and mouse-transform center checks). The spec's Viewer-side wrapper section is Task 2, with the exact method names (`UiScale()`, `VirtualViewport()`, `UiMouse()`) and the "button-state still reads raw `Mouse.GetState()`" note preserved in a doc comment. The spec's "Explicitly NOT in Stage 1" list (no `SpriteBatch.Begin` transform, no `GraphicsDevice.Viewport` replacement, no `Mouse.GetState()` hit-test replacement) is honored — Task 2 Step 4/5 specifically verify this by confirming zero behavior change.
- **Placeholder scan:** none — every step has literal code, exact commands, and concrete expected output.
- **Type consistency:** `ComputeScale(int, int, int, int)` returning `float`, `ComputeVirtualViewport(int, int, float)` returning `(int Width, int Height)`, and `TransformMouse(int, int, float)` returning `(int X, int Y)` are used identically in Task 1's implementation, Task 1's tests, and Task 2's wrapper calls — no renaming or signature drift between tasks.
- **Corrected from the spec's own illustrative text:** the spec's Testing section mentioned "1920×1080 → 3.0 exactly, since both axes agree" as an example — this is arithmetically wrong (1920/640=3.0 but 1080/480=2.25; they do NOT agree, since 1920x1080 is 16:9 and the 640x480 base is 4:3.0 is only exact if the window is exactly 4:3). This plan's tests use verified-correct values throughout (e.g. 1280x720 → 1.5, height-limiting) and do not repeat that inaccurate example. The spec's underlying design point it was illustrating — a uniform scale rather than fo2ce's own per-axis distorting stretch — is unaffected by that one wrong example number.
