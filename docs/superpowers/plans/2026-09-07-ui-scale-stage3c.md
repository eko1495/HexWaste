# UI Scale — Stage 3c: Preferences, Aim Dialog, Tactics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make preferences, the aim/called-shot dialog, and the tactics window render at the same
uniform scale as every screen Stage 2/3a/3b already migrated, filling non-4:3 windows like
`fallout2-ce`'s stretch, while every other screen keeps its native size. This closes out the
originally-planned Stage 3 split.

**Architecture:** Preferences and tactics get the save/load treatment (scope the whole method,
since a same-function boolean gate shares one viewport-relative formula between the art and
fallback branches). The aim dialog gets the Skilldex/perk-picker treatment (scope only the art
path; the true fallback stays unscaled) — but with one extra wrinkle no prior screen had: its
hit-test helper (`CalledShotHitAt`) itself branches between the (to-be-scaled) art rects and the
(staying-unscaled) fallback rects depending on live art presence, so its caller must decide up
front which mouse coordinate space to hand it. Preferences has its own wrinkle: its `Update()`
handler takes a whole `MouseState`, not bare ints, and is hoisted above the normal per-screen
dispatch — reusing Stage 2's synthetic-`MouseState` technique resolves it with no changes to
`UpdatePreferences`'s body.

**Tech Stack:** C# / .NET, MonoGame DesktopGL (`SpriteBatch.Begin`/`End`, already-shipped
`UiScale`/`VirtualViewport`/`UiMouse`/`UiScaleMatrix` from Stage 1/2/3a/3b).

## Global Constraints

- Scale model: `scale = min(viewportWidth/640, viewportHeight/480)` (Stage 1, shipped,
  unit-tested — do not touch).
- Only preferences, the aim dialog, and tactics scale in this plan. No other screen changes.
- Preferences and tactics have NO separate fallback method — their whole `Draw*` method scales
  (matching Stage 3a's save/load).
- The aim dialog's fallback (`DrawAimDialogFallback`, `AimDialogPanelRect`, `AimDialogRowRect`)
  stays completely untouched and unscaled — matching the Stage 2/3a precedent for a degraded
  fallback path, even though its own math happens to also read the viewport.
- Every position read from a real device (mouse) or used to lay out scaled content must come
  from `UiMouse()`/`VirtualViewport()` inside a scaled block — never a raw
  `GraphicsDevice.Viewport`/`Mouse.GetState().X/Y` mixed into scaled content.
- `CalledShotHitAt(int mx, int my)` branches internally on art presence and tests `(mx, my)`
  against either the to-be-scaled art rects or the staying-unscaled fallback rects. Its caller,
  `HandleAimDialogInput`, must compute the art-presence check BEFORE calling it and pass
  `UiMouse()`'s point when art is live, the raw `mouse` point when it isn't — this is the one
  place in this stage where the coordinate space passed to a helper is conditional.
- `_preferencesOpen`'s `Update()` handler is hoisted above the normal per-screen dispatch
  (`ViewerGame.cs:2015-2022`) and is reachable from the Title screen's OPTIONS button, so the
  already-scaled main-menu family and Preferences already routinely render in the same frame
  today (Preferences currently unscaled) — this stage's migration makes the two agree, it does
  not introduce the overlap.
- The aim dialog and tactics can co-render in the same frame (`_aimDialogOpen` and
  `_tacticsMember is not null` have no mutual exclusion) — pre-existing, not introduced by this
  stage, not in scope to fix. Both will simply center independently in the virtual canvas instead
  of the real one, preserving today's relationship exactly.
- **Golden suite coverage differs per task** — pick the suite that actually exercises the touched
  code, per the lesson Stage 3b's final review taught: `scripts/encounter-golden.sh` has scripted
  scenarios for both the aim dialog (`aim-click`) and tactics (`companion-tactics`,
  `companion-tactics-aw`), with a `FILTER` that captures `aim-click:`/`tactics:` output lines.
  **No golden scenario exercises Preferences at all** (`--prefs` is a QA-only CLI flag, not
  wired into any golden script) — for that task, the manual visual check is the only real
  verification; `opening-golden.sh`/`encounter-golden.sh` are still worth running as a broad
  regression net, but do not mistake their pass for coverage of Preferences specifically.

## File Structure

- `src/Hexwaste.Viewer/ViewerGame.Preferences.cs` — `PrefWindowPos`, `DrawPreferences`.
- `src/Hexwaste.Viewer/ViewerGame.Panels.cs` — `CalledShotWindowPos`, `CalledShotButtonRect`,
  `CalledShotCancelRect`, `HandleAimDialogInput`, `DrawAimDialog`.
- `src/Hexwaste.Viewer/ViewerGame.Tactics.cs` — `TacticsPanelRect`, `TacticsRowRect`,
  `HandleTacticsInput`, `DrawTactics`.
- `src/Hexwaste.Viewer/ViewerGame.cs` — the `Update()` call site for Preferences (a new synthetic
  `MouseState`, following Stage 2's precedent), plus the tactics `Update()` hit-test.

## Task 1: Preferences

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Preferences.cs:45-50` (`PrefWindowPos`),
  `:137-190` (`DrawPreferences`)
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:2015-2022` (Update call site)

**Interfaces:**
- Consumes: `UiScaleMatrix()`, `VirtualViewport()`, `UiMouse()` (Stage 1/2, already shipped),
  the `Point uiMouse` local already declared in `Update()` (Stage 2 Task 1).
- Produces: nothing new consumed by a later task in this plan.

- [ ] **Step 1: Switch `PrefWindowPos()` to the virtual viewport**

Find, in `src/Hexwaste.Viewer/ViewerGame.Preferences.cs` (around line 45-50):

```csharp
    private Point PrefWindowPos()
    {
        Viewport vp = GraphicsDevice.Viewport;
        Texture2D? bg = InterfaceFrm(PrefWindowFrm);
        return new Point((vp.Width - (bg?.Width ?? 640)) / 2, (vp.Height - (bg?.Height ?? 480)) / 2);
    }
```

Replace with:

```csharp
    // Stage 3c (UI Scale): reads the virtual-canvas viewport — DrawPreferences draws inside its
    // own scoped, scaled SpriteBatch block, and UpdatePreferences (the hit-test) receives an
    // already UiMouse()-transformed synthetic MouseState from its Update() call site, so both
    // agree on the same coordinate space.
    private Point PrefWindowPos()
    {
        Viewport vp = VirtualViewport();
        Texture2D? bg = InterfaceFrm(PrefWindowFrm);
        return new Point((vp.Width - (bg?.Width ?? 640)) / 2, (vp.Height - (bg?.Height ?? 480)) / 2);
    }
```

(`VirtualViewport()` returns a `Rectangle`, and `Viewport` and `Rectangle` both expose
`Width`/`Height` — the only members `PrefWindowPos()` reads — so the type change is safe here
exactly as it was for the Stage 2 main-menu screens' equivalent `vp` local.)

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Scope `DrawPreferences` into its own scaled `SpriteBatch` block**

Find, in the same file (around line 137-144):

```csharp
    private void DrawPreferences()
    {
        if (!_preferencesOpen || _fontRenderer is null)
            return;
        Point o = PrefWindowPos();
        Texture2D? bg = InterfaceFrm(PrefWindowFrm);
        if (bg is not null)
            _spriteBatch.Draw(bg, new Vector2(o.X, o.Y), Color.White);
```

Replace with:

```csharp
    private void DrawPreferences()
    {
        if (!_preferencesOpen || _fontRenderer is null)
            return;

        // Stage 3c (UI Scale): like Pip-Boy/options/automap (Stage 3b), Preferences has no
        // separate fallback method — the art-present and art-absent branches below share this
        // function's viewport-relative math, so the whole method scales inside one scoped, scaled
        // SpriteBatch block.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        Point o = PrefWindowPos();
        Texture2D? bg = InterfaceFrm(PrefWindowFrm);
        if (bg is not null)
            _spriteBatch.Draw(bg, new Vector2(o.X, o.Y), Color.White);
```

Then find the method's closing (around line 184-190):

```csharp
        // DEFAULT / DONE / CANCEL.
        var gold = new Color(252, 252, 84);
        void Btn(int lx, int msg) => _fontRenderer.Draw(_spriteBatch, PrefMsg(msg), new Vector2(o.X + lx, o.Y + 449), gold);
        Btn(43, PrefDefaultMsg);
        Btn(169, PrefDoneMsg);
        Btn(283, PrefCancelMsg);
    }
```

Replace with:

```csharp
        // DEFAULT / DONE / CANCEL.
        var gold = new Color(252, 252, 84);
        void Btn(int lx, int msg) => _fontRenderer.Draw(_spriteBatch, PrefMsg(msg), new Vector2(o.X + lx, o.Y + 449), gold);
        Btn(43, PrefDefaultMsg);
        Btn(169, PrefDoneMsg);
        Btn(283, PrefCancelMsg);

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

(`DrawPreferences` has no `Mouse.GetState()` call to convert — confirmed during planning; it only
renders, all hit-testing lives in `UpdatePreferences`, handled in Step 5.)

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Pass a UI-scaled synthetic `MouseState` into `UpdatePreferences`**

In `src/Hexwaste.Viewer/ViewerGame.cs`, find (around line 2013-2022):

```csharp
        // P130/P139: the Preferences (PREFSCRN) modal — runs in ANY state (incl. the main menu's OPTIONS
        // button), so it too precedes the menu-state return. ClosePreferences restores the prior screen.
        if (_preferencesOpen)
        {
            UpdatePreferences(keyboard, mouse);
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }
```

Replace with:

```csharp
        // P130/P139: the Preferences (PREFSCRN) modal — runs in ANY state (incl. the main menu's OPTIONS
        // button), so it too precedes the menu-state return. ClosePreferences restores the prior screen.
        if (_preferencesOpen)
        {
            // Stage 3c (UI Scale): UpdatePreferences hit-tests against PrefWindowPos()'s
            // VirtualViewport()-derived rectangles, so it needs the same scaled position the draw
            // path uses — a synthetic MouseState carries uiMouse's position through unchanged,
            // exactly like Stage 2's main-menu-family handling (only X/Y differ from `mouse`).
            MouseState uiPrefMouse = new(uiMouse.X, uiMouse.Y, mouse.ScrollWheelValue,
                mouse.LeftButton, mouse.MiddleButton, mouse.RightButton,
                mouse.XButton1, mouse.XButton2, mouse.HorizontalScrollWheelValue);
            UpdatePreferences(keyboard, uiPrefMouse);
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }
```

(`UpdatePreferences`'s own body needs no change — every `mouse.X`/`mouse.Y` read inside it now
simply reads the already-transformed synthetic `MouseState` passed in.)

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Run the golden suites (broad regression net — Preferences itself has no scenario)**

Run: `FALLOUT2_DIR=./game-data scripts/opening-golden.sh check`
Run: `FALLOUT2_DIR=./game-data scripts/encounter-golden.sh check`
Expected: both report all scenarios passing. Neither suite has a Preferences-specific scenario
(confirmed during planning — `--prefs` is a QA-only CLI flag, not wired into any golden script),
so a pass here is a broad safety net, not evidence Preferences itself works — Step 8 is the real
verification for this task.

- [ ] **Step 8: Manual visual check — Preferences opened from the Title menu**

Run:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage3c-prefs-menu.png \
  -- --menu --prefs
```
(check `src/Hexwaste.Viewer/Program.cs`'s `--prefs` case if it doesn't combine with `--menu` as
written — the flag comment says "P130 QA: open the Preferences panel"; adjust invocation order or
arguments as needed to reach Preferences from the Title screen specifically, since that is the
same-frame-overlap case this task must prove works.)

Read the resulting PNG. Expected: BOTH the main-menu background (already scaled since Stage 2)
and the Preferences window (now also scaled by this task) render at the SAME 1.5× scale,
agreeing with each other — not one native-sized floating over the other. Then also check
Preferences reached from in-game (via Options → Preferences) at the same window size, confirming
it scales identically there too, and that a click on a slider/knob still lands on the row the
cursor visually sits over.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Preferences.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the preferences panel to fill non-4:3 windows

Wires Stage 1/2/3a/3b's UiScale infrastructure into DrawPreferences:
like Pip-Boy/options/automap, its art-present and art-absent branches
share one function's viewport-relative math, so the whole method now
draws inside one scoped, scaled SpriteBatch block. UpdatePreferences
(the hit-test) is hoisted above the normal per-screen Update()
dispatch and takes a whole MouseState rather than bare ints, so its
Update() call site now builds a UI-scaled synthetic MouseState -- the
same technique Stage 2 used for the main-menu family -- rather than
changing UpdatePreferences' own body. This also fixes a pre-existing
scale mismatch: Preferences is reachable from the Title screen's
OPTIONS button and already co-renders with the (since Stage 2, scaled)
main-menu background in the same frame.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

## Task 2: Aim dialog

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Panels.cs:1605-1621` (`CalledShotWindowPos`,
  `CalledShotButtonRect`, `CalledShotCancelRect`), `:1635-1657` (`HandleAimDialogInput`),
  `:1676-1716` (`DrawAimDialog`)

**Interfaces:**
- Consumes: same as Task 1.
- Produces: nothing new consumed by a later task.

- [ ] **Step 1: Switch the art-mode origin family to the virtual viewport**

Find, in `src/Hexwaste.Viewer/ViewerGame.Panels.cs` (around line 1603-1621):

```csharp
    /// <summary>The window's top-left in screen space (centered; fo2ce centers X and pins Y=20 at
    /// 640×480 / centers otherwise, :5492-5496 — we always center, documented).</summary>
    private Point CalledShotWindowPos() => new(
        (GraphicsDevice.Viewport.Width - CalledShotW) / 2,
        Math.Max(0, (GraphicsDevice.Viewport.Height - CalledShotH) / 2));

    /// <summary>The location button rect for dialog row 0..7 — left column rows 0-3 at window-local
    /// x=33, right column rows 4-7 at x=341, y=_call_ty−90, 128×20 (buttonCreate :5576/5583).</summary>
    private Rectangle CalledShotButtonRect(int row)
    {
        Point p = CalledShotWindowPos();
        return new Rectangle(p.X + (row < 4 ? 33 : 341), p.Y + CalledShotRowY[row % 4] - 90, 128, 20);
    }

    private Rectangle CalledShotCancelRect()
    {
        Point p = CalledShotWindowPos();
        return new Rectangle(p.X + 210, p.Y + 268, 15, 16); // :5549-5553
    }
```

Replace with:

```csharp
    /// <summary>The window's top-left in screen space (centered; fo2ce centers X and pins Y=20 at
    /// 640×480 / centers otherwise, :5492-5496 — we always center, documented).
    /// Stage 3c (UI Scale): reads the virtual-canvas viewport — DrawAimDialog's art path draws
    /// inside its own scoped, scaled SpriteBatch block. AimDialogPanelRect/AimDialogRowRect (the
    /// text fallback) deliberately do NOT read VirtualViewport() — that fallback stays unscaled.</summary>
    private Point CalledShotWindowPos() => new(
        (VirtualViewport().Width - CalledShotW) / 2,
        Math.Max(0, (VirtualViewport().Height - CalledShotH) / 2));

    /// <summary>The location button rect for dialog row 0..7 — left column rows 0-3 at window-local
    /// x=33, right column rows 4-7 at x=341, y=_call_ty−90, 128×20 (buttonCreate :5576/5583).</summary>
    private Rectangle CalledShotButtonRect(int row)
    {
        Point p = CalledShotWindowPos();
        return new Rectangle(p.X + (row < 4 ? 33 : 341), p.Y + CalledShotRowY[row % 4] - 90, 128, 20);
    }

    private Rectangle CalledShotCancelRect()
    {
        Point p = CalledShotWindowPos();
        return new Rectangle(p.X + 210, p.Y + 268, 15, 16); // :5549-5553
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Scope `DrawAimDialog`'s art path into its own scaled `SpriteBatch` block**

Find, in the same file (around line 1676-1694):

```csharp
    private void DrawAimDialog()
    {
        if (!_aimDialogOpen || _fontRenderer is null)
            return;
        Texture2D? bg = InterfaceFrm(CalledShotBgFrmId);
        Texture2D? digits = InterfaceFrm(CalledShotDigitsFrmId);
        if (bg is null || digits is null)
        {
            DrawAimDialogFallback();
            return;
        }

        Point p = CalledShotWindowPos();
        _spriteBatch.Draw(bg, new Vector2(p.X, p.Y), Color.White);
        if (_aimDialogTarget is { } target && CalledShotPic(target) is { } pic)
            _spriteBatch.Draw(pic, new Vector2(p.X + 168, p.Y + 31), Color.White); // :5530

        MouseState mouse = Mouse.GetState();
        int hovered = CalledShotHitAt(mouse.X, mouse.Y);
```

Replace with:

```csharp
    private void DrawAimDialog()
    {
        if (!_aimDialogOpen || _fontRenderer is null)
            return;
        Texture2D? bg = InterfaceFrm(CalledShotBgFrmId);
        Texture2D? digits = InterfaceFrm(CalledShotDigitsFrmId);
        if (bg is null || digits is null)
        {
            DrawAimDialogFallback();
            return;
        }

        // Stage 3c (UI Scale): the art path draws into its own scoped, scaled SpriteBatch block —
        // same technique as Stage 3a's DrawSkilldex/DrawPerkPicker — so the called-shot dialog
        // matches fo2ce's fullscreen stretch while DrawAimDialogFallback (already returned above
        // when reached) stays unscaled.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        Point p = CalledShotWindowPos();
        _spriteBatch.Draw(bg, new Vector2(p.X, p.Y), Color.White);
        if (_aimDialogTarget is { } target && CalledShotPic(target) is { } pic)
            _spriteBatch.Draw(pic, new Vector2(p.X + 168, p.Y + 31), Color.White); // :5530

        Point mouse = UiMouse();
        int hovered = CalledShotHitAt(mouse.X, mouse.Y);
```

Then find, later in the same method (around line 1712-1716):

```csharp
        bool cancelPressed = mouse.LeftButton == ButtonState.Pressed
            && CalledShotCancelRect().Contains(mouse.X, mouse.Y);
        if (InterfaceFrm(cancelPressed ? CalledShotCancelDownFrmId : CalledShotCancelUpFrmId) is { } cancel)
            _spriteBatch.Draw(cancel, new Vector2(p.X + 210, p.Y + 268), Color.White);
    }
```

Replace with:

```csharp
        bool cancelPressed = Mouse.GetState().LeftButton == ButtonState.Pressed
            && CalledShotCancelRect().Contains(mouse.X, mouse.Y);
        if (InterfaceFrm(cancelPressed ? CalledShotCancelDownFrmId : CalledShotCancelUpFrmId) is { } cancel)
            _spriteBatch.Draw(cancel, new Vector2(p.X + 210, p.Y + 268), Color.White);

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

(`mouse` is now the `Point` from `UiMouse()` declared above, which has no `.LeftButton` — button
state still needs a fresh `Mouse.GetState()` read, exactly as `cancelPressed`'s check does here;
`mouse.X`/`mouse.Y` in the `.Contains(...)` call still correctly refer to the scaled `Point`.)

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Reorder `HandleAimDialogInput` to pass the correct mouse coordinate space to `CalledShotHitAt`**

Find, in the same file (around line 1635-1657):

```csharp
    private void HandleAimDialogInput(MouseState mouse, KeyboardState keyboard)
    {
        if (IsKeyPressed(keyboard, Keys.Escape))
        {
            _aimDialogOpen = false;
            return;
        }
        for (int i = 0; i < AimDialogOrder.Length; i++)
            if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
            {
                SelectAimRow(i);
                return;
            }
        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
            && CalledShotHitAt(mouse.X, mouse.Y) is int clicked && clicked >= 0)
        {
            bool artMode = InterfaceFrm(CalledShotBgFrmId) is not null;
            if (artMode && clicked == 8)
                _aimDialogOpen = false; // the cancel button closes without changing the aim
            else
                SelectAimRow(clicked);
        }
    }
```

Replace with:

```csharp
    private void HandleAimDialogInput(MouseState mouse, KeyboardState keyboard)
    {
        if (IsKeyPressed(keyboard, Keys.Escape))
        {
            _aimDialogOpen = false;
            return;
        }
        for (int i = 0; i < AimDialogOrder.Length; i++)
            if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
            {
                SelectAimRow(i);
                return;
            }
        // Stage 3c (UI Scale): CalledShotHitAt tests its (mx,my) argument against the SCALED art
        // rects when art is live, or the UNSCALED fallback rects when it isn't — the art-presence
        // check must happen here, before the call, so the correct coordinate space is chosen up
        // front rather than left ambiguous inside CalledShotHitAt.
        bool artMode = InterfaceFrm(CalledShotBgFrmId) is not null;
        Point hitPoint = artMode ? UiMouse() : new Point(mouse.X, mouse.Y);
        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
            && CalledShotHitAt(hitPoint.X, hitPoint.Y) is int clicked && clicked >= 0)
        {
            if (artMode && clicked == 8)
                _aimDialogOpen = false; // the cancel button closes without changing the aim
            else
                SelectAimRow(clicked);
        }
    }
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Run the golden suite covering the aim dialog**

Run: `FALLOUT2_DIR=./game-data scripts/encounter-golden.sh check`
Expected: all scenarios pass, including `aim-click` — this scenario drives `SelectAimRow`
directly via the `--aim-click <row>` CLI action (not a simulated screen click), so it is
unaffected by this task's coordinate-space changes; a pass here confirms no regression in the
underlying selection logic this task's diff sits next to.

- [ ] **Step 8: Manual visual check — aim dialog art mode at a non-4:3 window**

Run:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage3c-aimdialog.png \
  -- --map arcaves.map --aim-click 0
```
(check `src/Hexwaste.Viewer/Program.cs`/`docs/fo2ce-comparison-playbook.md` for the exact combat
setup needed to have `_aimDialogOpen` still true at the screenshot — `--aim-click` may
immediately select and close the dialog rather than leaving it open for a screenshot; if so, find
or add the minimal sequence that opens the dialog and stops before a selection, similar to how
Stage 3a's Task 2 found `--perk-pick <level> -1` to leave a picker open without picking).

Read the resulting PNG. Expected: the called-shot window renders at 1.5× baseline, centered,
undistorted, hit-location names and to-hit percentages legible. Then, if a live/scripted click is
possible, click a visible hit-location row and confirm it selects that SPECIFIC location (not an
adjacent one) — this is the check that proves the `HandleAimDialogInput` coordinate-space
reordering (Step 5) is correct.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Panels.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the aim/called-shot dialog to fill non-4:3 windows

Wires Stage 1/2/3a/3b's UiScale infrastructure into DrawAimDialog's
art path: it now draws inside its own scoped, scaled SpriteBatch
block, with CalledShotWindowPos() (and everything derived from it)
reading VirtualViewport() instead of the raw device viewport.
DrawAimDialogFallback/AimDialogPanelRect/AimDialogRowRect stay
untouched and unscaled.

CalledShotHitAt tests its argument against either the (now scaled)
art rects or the (still unscaled) fallback rects depending on live
art presence -- HandleAimDialogInput now computes that same
art-presence check BEFORE calling it, and passes UiMouse()'s point or
the raw mouse point accordingly, since the callee has no way to know
which coordinate space its caller intends.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

## Task 3: Tactics

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.Tactics.cs:95-118` (`TacticsPanelRect`,
  `TacticsRowRect`), `:141-157` (`HandleTacticsInput`), `:159-193` (`DrawTactics`)

**Interfaces:**
- Consumes: same as Task 1/2.
- Produces: nothing new consumed by a later task.

- [ ] **Step 1: Switch `TacticsPanelRect()` to the virtual viewport**

Find, in `src/Hexwaste.Viewer/ViewerGame.Tactics.cs` (around line 95-106):

```csharp
    private Rectangle TacticsPanelRect()
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        if (_tacticsArt)
        {
            const int w = 640, h = 190; // CONTROL.frm dimensions
            return new Rectangle(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2), w, h);
        }
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        int tw = 440, th = (TacticsRowCount + 2) * lh + 12;
        return new Rectangle(Math.Max(0, (vp.Width - tw) / 2), Math.Max(0, (vp.Height - th) / 2), tw, th);
    }
```

Replace with:

```csharp
    // Stage 3c (UI Scale): like preferences/Pip-Boy/options/automap, tactics has no separate
    // fallback method — both the art (_tacticsArt) and text branches below share this function's
    // viewport-relative math, so DrawTactics scales the whole method, and this reads the
    // virtual-canvas viewport for both branches.
    private Rectangle TacticsPanelRect()
    {
        Rectangle vp = VirtualViewport();
        if (_tacticsArt)
        {
            const int w = 640, h = 190; // CONTROL.frm dimensions
            return new Rectangle(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2), w, h);
        }
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        int tw = 440, th = (TacticsRowCount + 2) * lh + 12;
        return new Rectangle(Math.Max(0, (vp.Width - tw) / 2), Math.Max(0, (vp.Height - th) / 2), tw, th);
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Scope `DrawTactics` into its own scaled `SpriteBatch` block**

Find, in the same file (around line 159-168):

```csharp
    private void DrawTactics()
    {
        if (_tacticsMember is not { } member || _fontRenderer is null)
            return;
        // P52-M2: render the authentic CONTROL.frm window (party combat-control art) when present;
        // fall back to the dark text panel when the asset is missing (the Skilldex text-then-art pattern).
        _controlFrm ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\CONTROL.frm");
        _tacticsArt = _controlFrm is not null;
        _panelPixel ??= CreatePixel();
        Rectangle p = TacticsPanelRect();
```

Replace with:

```csharp
    private void DrawTactics()
    {
        if (_tacticsMember is not { } member || _fontRenderer is null)
            return;
        // P52-M2: render the authentic CONTROL.frm window (party combat-control art) when present;
        // fall back to the dark text panel when the asset is missing (the Skilldex text-then-art pattern).
        _controlFrm ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\CONTROL.frm");
        _tacticsArt = _controlFrm is not null;
        _panelPixel ??= CreatePixel();

        // Stage 3c (UI Scale): no separate fallback method here (see TacticsPanelRect's doc
        // comment) — the whole method scales inside one scoped, scaled SpriteBatch block.
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());

        Rectangle p = TacticsPanelRect();
```

Then find, later in the same method (around line 186-187):

```csharp
        int hovered = TacticsRowAt(Mouse.GetState().X, Mouse.GetState().Y);
```

Replace with:

```csharp
        Point tm = UiMouse();
        int hovered = TacticsRowAt(tm.X, tm.Y);
```

Finally find the method's closing (around line 190-193):

```csharp
            Rectangle rr = TacticsRowRect(i);
            _fontRenderer.Draw(_spriteBatch, $"{i + 1}. {TacticsRowLabel(i, ai)}",
                new Vector2(rr.X + 6, rr.Y + 2), i == hovered ? hot : green);
        }
    }
```

Replace with:

```csharp
            Rectangle rr = TacticsRowRect(i);
            _fontRenderer.Draw(_spriteBatch, $"{i + 1}. {TacticsRowLabel(i, ai)}",
                new Vector2(rr.X + 6, rr.Y + 2), i == hovered ? hot : green);
        }

        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 5: Route the `Update()` hit-test through `uiMouse`**

In `src/Hexwaste.Viewer/ViewerGame.Tactics.cs`, find (around line 141-156):

```csharp
    private void HandleTacticsInput(MouseState mouse, KeyboardState keyboard)
    {
        if (IsKeyPressed(keyboard, Keys.Escape))
        {
            _tacticsMember = null;
            return;
        }
        for (int i = 0; i < TacticsRowCount; i++)
            if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
            {
                TacticsActivate(i);
                return;
            }
        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
            && TacticsRowAt(mouse.X, mouse.Y) is int r && r >= 0)
            TacticsActivate(r);
    }
```

Replace with:

```csharp
    private void HandleTacticsInput(MouseState mouse, Point uiMouse, KeyboardState keyboard)
    {
        if (IsKeyPressed(keyboard, Keys.Escape))
        {
            _tacticsMember = null;
            return;
        }
        for (int i = 0; i < TacticsRowCount; i++)
            if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
            {
                TacticsActivate(i);
                return;
            }
        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
            && TacticsRowAt(uiMouse.X, uiMouse.Y) is int r && r >= 0)
            TacticsActivate(r);
    }
```

Then, in `src/Hexwaste.Viewer/ViewerGame.cs`, find its one caller (around line 2304-2311):

```csharp
        // The companion combat-control window (P50): modal while open — cycle the tactics, Esc done.
        if (_tacticsMember is not null)
        {
            HandleTacticsInput(mouse, keyboard);
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }
```

Replace with:

```csharp
        // The companion combat-control window (P50): modal while open — cycle the tactics, Esc done.
        if (_tacticsMember is not null)
        {
            HandleTacticsInput(mouse, uiMouse, keyboard);
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Run the golden suite covering tactics**

Run: `FALLOUT2_DIR=./game-data scripts/encounter-golden.sh check`
Expected: all scenarios pass, including `companion-tactics` and `companion-tactics-aw` — these
drive `TacticsActivate` directly via the `--companion-tactics` CLI action (row index, not a
simulated screen click), so they're unaffected by this task's `uiMouse` plumbing; a pass confirms
no regression in the underlying dispatch logic.

- [ ] **Step 8: Manual visual check — tactics window at a non-4:3 window**

Run:
```bash
FALLOUT2_DIR=./game-data DISPLAY=:0 scripts/hexwaste-checkpoint.sh \
  /home/eko/.claude/jobs/28039163/tmp/stage3c-tactics.png \
  -- --map arcaves.map --companion-tactics 20529 0 2
```
(this opens and immediately sets a tactic via the CLI action — check whether `_tacticsMember`
stays set afterward for the screenshot, or find/add a way to leave the window open without
completing a selection, matching the approach used for Stage 3a's perk-picker screenshot).

Read the resulting PNG. Expected: CONTROL.frm (or its dark-panel fallback) renders at 1.5×
baseline, centered, undistorted, all rows legible.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.Tactics.cs src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): scale the tactics window to fill non-4:3 windows

Wires Stage 1/2/3a/3b's UiScale infrastructure into DrawTactics: like
preferences/Pip-Boy/options/automap, its art-present and art-absent
branches share one function's viewport-relative math, so the whole
method now draws inside one scoped, scaled SpriteBatch block.
HandleTacticsInput gains a uiMouse parameter (alongside the existing
raw mouse, kept for its button-state read) so its hit-test agrees with
the scaled draw.

This closes out UI Scale Stage 3 (originally split into 3a/3b/3c after
a pre-implementation survey found ~20 origin/viewport helpers across
11 screens). Inventory remains deferred to its own follow-up plan.
Stage 4 (HUD bar/action menu/elevator picker) and Stage 5 (worldmap
chrome) are next.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```
