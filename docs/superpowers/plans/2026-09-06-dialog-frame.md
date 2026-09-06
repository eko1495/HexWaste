# Authentic Dialog Frame Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Hexwaste's in-game dialog always draw the real Fallout 2 dialog frame art (`ALLTLK.FRM` + `DI_TALK.FRM`/`DI_TALKP.FRM`), instead of a plain dark rectangle, and let the live game world show through the head-portrait area when an NPC has no head art — matching `fallout2-ce`'s actual behavior.

**Architecture:** Both changes live entirely inside `DrawConversationPanel` (and its two call sites in `DrawDialogPanel`) in `src/Hexwaste.Viewer/ViewerGame.cs`. Two new FRM textures are loaded lazily, once, the same way `_charBg`/`_invBox` already are elsewhere in this file. No new game state, no VM/script changes.

**Tech Stack:** C# / MonoGame (`SpriteBatch.Draw`), `Hexwaste.Formats` FRM loading via the existing `InterfaceBar.LoadFrm` helper.

## Global Constraints

- Frame art: `art\intrface\ALLTLK.FRM` (interface FID 103), drawn at 640×480, centered on screen — `(viewport.Width-640)/2, (viewport.Height-480)/2` (already computed today as `frameX`/`frameY`).
- Reply/options backdrop art: `art\intrface\DI_TALK.FRM` (FID 99, regular NPC) or `art\intrface\DI_TALKP.FRM` (FID 389, party-member speaker), drawn behind the existing reply/options text at `(frameX+122, frameY+219)` (already computed today as `panelX`/`panelY` for the head-present case) — sized to Hexwaste's existing dynamically-computed `panelWidth`×`panelHeight`, not vanilla's fixed 379×58 base size (`SpriteBatch.Draw` with a destination `Rectangle` stretches automatically).
- Head portrait area: `(frameX+126, frameY+14)`, 388×200 — unchanged from the existing `DrawTalkingHead` positioning.
- When `headId < 0` (no head, e.g. `AAcklint.int`/Klint) and the frame art loaded successfully, the head-portrait rectangle must NOT be part of the screen-dim overlay — the live world must show through it undimmed, matching vanilla's transparent portrait cutout in `ALLTLK.FRM`.
- If the frame art fails to load (headless/no-game-data runs — the existing guarded path), keep today's exact plain-rectangle fallback byte-for-byte. This must not regress any golden fixture.
- No golden fixture screenshots this panel (dialog goldens are text-transcript based, verified during the fo2ce-comparison session that produced this spec) — no fixture re-recording is expected, but golden suites are still run as a regression check since `ViewerGame.cs` is shared, widely-used code.
- The `_companionHub is not null` branch in `DrawDialogPanel` is the party-member speaker case (talking to a companion); the `_dialog is not null` branch is the regular NPC case.

---

### Task 1: Draw the real dialog frame + reply backdrop, for every dialog

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs:6172-6182` (`DrawDialogPanel`), `:6433-6518` (`DrawConversationPanel`)

**Interfaces:**
- Produces: `DrawConversationPanel(string name, string reply, IReadOnlyList<string> options, IReadOnlyList<int>? reactions = null, int headId = -1, bool isPartyMember = false)` — new `isPartyMember` parameter, defaulting to `false` so the existing `_dialog is not null` call site needs no change. New private fields `_dialogFrame`, `_dialogReplyBg`, `_dialogReplyBgParty` (all `Texture2D?`), `_dialogFrameTried` (`bool`), and a new private method `EnsureDialogFrameArt()` — Task 2 reads these same fields.

- [ ] **Step 1: Read the current code to confirm line numbers match**

Run: `sed -n '6172,6182p;6433,6480p' src/Hexwaste.Viewer/ViewerGame.cs`

Expected: `DrawDialogPanel` at the top matches:
```csharp
    private void DrawDialogPanel()
    {
        if (_companionHub is not null)
        {
            DrawConversationPanel(ObjectName(_companionHub), "What do you need?",
                [.. _hubOptions.Select(o => o.Label)]);
            return;
        }
        if (_dialog is not null)
            DrawConversationPanel(_dialog.NpcName, _dialog.Reply, _dialog.Options, _dialog.OptionReactions, EffectiveHeadId());
    }
```
and `DrawConversationPanel`'s signature/opening matches:
```csharp
    private void DrawConversationPanel(string name, string reply, IReadOnlyList<string> options,
        IReadOnlyList<int>? reactions = null, int headId = -1)
    {
        if (_fontRenderer is null)
            return;

        // P52-M1: with the Empathy perk the engine tints each dialogue option by the NPC's
        // reaction to it (game_dialog.cc gameDialogOptionOnMouseEnter:2118 / onMouseExit:2162).
        bool empathy = reactions is not null && DudePerkRank(Formats.Perks.PerkId.Empathy) > 0;

        _panelPixel ??= CreatePixel();

        Rectangle viewport = GraphicsDevice.Viewport.Bounds;

        // P89: the dialogue is a screen takeover — dim the world so the head + text read as the FO2 dialog
        // screen (game_dialog.cc darkens the captured scene), instead of floating over live, lit play.
        _spriteBatch.Draw(_panelPixel, viewport, new Color(0, 0, 0, 175));

        // FO2 lays the dialog in a 640x480 frame centred on screen: the head display at window-local
        // (126,14), the reply window at (135,225). With a head we honour that frame; a head-less dialog
        // keeps the simple bottom panel over the dimmed scene.
        int frameX = Math.Max(0, (viewport.Width - 640) / 2);
        int frameY = Math.Max(0, (viewport.Height - 480) / 2);

        int panelWidth = headId >= 0 ? 397 : Math.Min(720, viewport.Width - 40);
        int textWidth = panelWidth - 32;
        int lineHeight = _fontRenderer.LineHeight;
```
If the file has drifted from this, stop and report — do not guess at a merge.

- [ ] **Step 2: Add the new fields and the art-loading helper**

Add these three private fields right before the `DrawConversationPanel` method (i.e. just above line 6433):

```csharp
    // ported from fallout2-ce src/game_dialog.cc gameDialogWindowRenderBackground() (:4470) +
    // _talkToRefreshDialogWindowRect() (:4488): alltlk.frm is the 640x480 dialog frame, drawn
    // unconditionally whether or not the speaker has head art; di_talk.frm/di_talkp.frm is the
    // reply/options backdrop, chosen by whether the speaker is a party member. Loaded lazily on
    // the first live Draw, same pattern as _charBg/_invBox — stays null headless so the existing
    // plain-rectangle fallback (golden-safe) is unchanged when there's no game data.
    private Texture2D? _dialogFrame, _dialogReplyBg, _dialogReplyBgParty;
    private bool _dialogFrameTried;

    private void EnsureDialogFrameArt()
    {
        if (_dialogFrameTried)
            return;
        _dialogFrameTried = true;
        _dialogFrame = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\ALLTLK.FRM");
        _dialogReplyBg = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\DI_TALK.FRM");
        _dialogReplyBgParty = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\DI_TALKP.FRM");
    }

```

- [ ] **Step 3: Update `DrawConversationPanel`'s signature and the frame/backdrop drawing**

Replace the method from its signature through the `if (headId >= 0) DrawTalkingHead(...)` line (i.e. replace current lines 6433-6477) with:

```csharp
    private void DrawConversationPanel(string name, string reply, IReadOnlyList<string> options,
        IReadOnlyList<int>? reactions = null, int headId = -1, bool isPartyMember = false)
    {
        if (_fontRenderer is null)
            return;

        // P52-M1: with the Empathy perk the engine tints each dialogue option by the NPC's
        // reaction to it (game_dialog.cc gameDialogOptionOnMouseEnter:2118 / onMouseExit:2162).
        bool empathy = reactions is not null && DudePerkRank(Formats.Perks.PerkId.Empathy) > 0;

        _panelPixel ??= CreatePixel();
        EnsureDialogFrameArt();

        Rectangle viewport = GraphicsDevice.Viewport.Bounds;

        // P89: the dialogue is a screen takeover — dim the world so the head + text read as the FO2 dialog
        // screen (game_dialog.cc darkens the captured scene), instead of floating over live, lit play.
        _spriteBatch.Draw(_panelPixel, viewport, new Color(0, 0, 0, 175));

        // FO2 lays the dialog in a 640x480 frame centred on screen: the head display at window-local
        // (126,14), the reply window at (135,225). ported from fallout2-ce src/game_dialog.cc
        // _gdCreateHeadWindow() (:2380): the frame draws unconditionally, head or not.
        int frameX = Math.Max(0, (viewport.Width - 640) / 2);
        int frameY = Math.Max(0, (viewport.Height - 480) / 2);

        bool framed = _dialogFrame is not null;
        int panelWidth = framed ? 397 : Math.Min(720, viewport.Width - 40);
        int textWidth = panelWidth - 32;
        int lineHeight = _fontRenderer.LineHeight;
```

Then, further down in the same method, replace this block (the original lines 6470-6480):

```csharp
        int panelHeight = (replyLines.Count + optionLines.Count + 3) * lineHeight + 24;
        // With a head, anchor the reply/options panel in the FO2 lower-frame region (the ~225 reply window)
        // so head + panel form the authentic dialog screen; otherwise pin it to the bottom of the screen.
        int panelX = headId >= 0 ? frameX + 122 : (viewport.Width - panelWidth) / 2;
        int panelY = headId >= 0 ? frameY + 219 : viewport.Height - panelHeight - 16;

        if (headId >= 0) // P89: the talking head sits in the upper frame, over the dimmed scene
            DrawTalkingHead(headId, frameX, frameY);

        _spriteBatch.Draw(_panelPixel, new Rectangle(panelX, panelY, panelWidth, panelHeight),
            new Color(8, 8, 8, 230));
```

with:

```csharp
        int panelHeight = (replyLines.Count + optionLines.Count + 3) * lineHeight + 24;
        // With the frame art, anchor the reply/options panel in the FO2 lower-frame region (the ~225
        // reply window) so head + panel form the authentic dialog screen; otherwise (art missing —
        // headless/no-game-data) pin it to the bottom of the screen, the existing fallback.
        int panelX = framed ? frameX + 122 : (viewport.Width - panelWidth) / 2;
        int panelY = framed ? frameY + 219 : viewport.Height - panelHeight - 16;

        if (framed)
            _spriteBatch.Draw(_dialogFrame, new Vector2(frameX, frameY), Color.White);

        if (headId >= 0) // P89: the talking head sits in the upper frame, over the dimmed scene
            DrawTalkingHead(headId, frameX, frameY);

        // ported from fallout2-ce src/game_dialog.cc _talkToRefreshDialogWindowRect() (:4488):
        // di_talk.frm (NPC) / di_talkp.frm (party member) behind the reply/options text, stretched
        // to Hexwaste's dynamically-computed panel size (vanilla's own box is a fixed 379x58 — ours
        // isn't, since dialogue text length varies more than vanilla's fixed layout assumed).
        Texture2D? replyBg = isPartyMember ? _dialogReplyBgParty : _dialogReplyBg;
        if (replyBg is not null)
            _spriteBatch.Draw(replyBg, new Rectangle(panelX, panelY, panelWidth, panelHeight), Color.White);
        else
            _spriteBatch.Draw(_panelPixel, new Rectangle(panelX, panelY, panelWidth, panelHeight),
                new Color(8, 8, 8, 230));
```

- [ ] **Step 4: Update `DrawDialogPanel`'s companion-hub call site**

Replace:
```csharp
    private void DrawDialogPanel()
    {
        if (_companionHub is not null)
        {
            DrawConversationPanel(ObjectName(_companionHub), "What do you need?",
                [.. _hubOptions.Select(o => o.Label)]);
            return;
        }
        if (_dialog is not null)
            DrawConversationPanel(_dialog.NpcName, _dialog.Reply, _dialog.Options, _dialog.OptionReactions, EffectiveHeadId());
    }
```
with:
```csharp
    private void DrawDialogPanel()
    {
        if (_companionHub is not null)
        {
            DrawConversationPanel(ObjectName(_companionHub), "What do you need?",
                [.. _hubOptions.Select(o => o.Label)], isPartyMember: true);
            return;
        }
        if (_dialog is not null)
            DrawConversationPanel(_dialog.NpcName, _dialog.Reply, _dialog.Options, _dialog.OptionReactions, EffectiveHeadId());
    }
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.` (pre-existing nullability warnings are fine; zero errors).

- [ ] **Step 6: Verify the head-less case (Klint) now shows the frame**

Run:
```bash
scripts/hexwaste-checkpoint.sh /tmp/dialog-frame-headless.png -- --create 5,5,5,5,5,5,5:0,4,5:0 --talk-hex 21101
```
Then view `/tmp/dialog-frame-headless.png` with the Read tool.
Expected: the ornate metal/tan `ALLTLK.FRM` frame now surrounds the dialog (not a bare bottom bar); the reply/options text sits over the `DI_TALK.FRM` backdrop instead of a flat dark rectangle; the head-portrait area (upper-middle of the frame) is present but currently shows dimmed content (the peek-through undimming is Task 2) — this is expected at this point in the plan.

- [ ] **Step 7: Verify the head-present case doesn't regress**

Run:
```bash
scripts/hexwaste-checkpoint.sh /tmp/dialog-frame-headed.png -- --create 5,5,5,5,5,5,5:0,4,5:0 --talk-hex 21101 --force-head 1
```
Then view `/tmp/dialog-frame-headed.png` with the Read tool.
Expected: the same frame as Step 6, but with a talking head sprite (head index 1 = "mrcus"/Marcus, per `art/heads/HEADS.LST`) rendered in the portrait area, positioned exactly as before this change (the head's own position math was not touched). No overlap or misalignment between the head sprite and the frame art.

- [ ] **Step 8: Run the relevant golden suites**

Run:
```bash
FALLOUT2_DIR=$(pwd)/game-data DISPLAY=:0 scripts/opening-golden.sh check
FALLOUT2_DIR=$(pwd)/game-data DISPLAY=:0 scripts/quest-golden.sh check
FALLOUT2_DIR=$(pwd)/game-data DISPLAY=:0 scripts/encounter-golden.sh check
```
Expected: `golden opening: ALL PASS`, `quest e2e: ALL PASS`, `golden encounter: ALL PASS` — this is pure rendering code, no golden fixture should change.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): draw the real dialog frame art, not a bare text panel

ported from fallout2-ce src/game_dialog.cc _gdCreateHeadWindow()
(:2380) + gameDialogWindowRenderBackground() (:4470) +
_talkToRefreshDialogWindowRect() (:4488): the 640x480 ALLTLK.FRM
frame draws unconditionally whether or not the NPC has head art, and
the reply/options text sits on DI_TALK.FRM (NPC) / DI_TALKP.FRM
(party member) instead of a flat rectangle. Previously a head-less
NPC (e.g. Klint) got no frame at all -- just a bare bottom text bar.

The head-portrait area still shows dimmed content when there's no
head; making the live world show through it undimmed, matching
vanilla's transparent portrait cutout, is a following change.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

### Task 2: Let the live world show through the portrait hole when there's no head

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs` (`DrawConversationPanel`, the dim-overlay line added/kept in Task 1)

**Interfaces:**
- Consumes: `_dialogFrame` (`Texture2D?`, Task 1) — whether the frame art loaded.
- Produces: a new private method `DimExcluding(Rectangle viewport, Rectangle hole)` — no other task depends on it, but it must exist under exactly this name/signature since Step 2 below references it.

- [ ] **Step 1: Read the current code to confirm Task 1 landed as expected**

Run: `sed -n '6433,6465p' src/Hexwaste.Viewer/ViewerGame.cs`
Expected: the dim line still reads `_spriteBatch.Draw(_panelPixel, viewport, new Color(0, 0, 0, 175));` unconditionally, and `bool framed = _dialogFrame is not null;` is present a few lines below it (from Task 1). If not, stop and report.

- [ ] **Step 2: Add the `DimExcluding` helper**

Add this method right after `EnsureDialogFrameArt()` (added in Task 1, just above `DrawConversationPanel`):

```csharp
    // Tiles the dim overlay as four strips around `hole`, leaving `hole` itself completely
    // undimmed — ported behavior, not ported code: fallout2-ce achieves the same observable
    // effect (the live scene shows through alltlk.frm's transparent portrait cutout when
    // there's no head) via a DOS-era snapshot-and-patch technique (_backgrndRects,
    // game_dialog.cc :237) that has no equivalent need in a modern immediate-mode renderer.
    private void DimExcluding(Rectangle viewport, Rectangle hole)
    {
        var dim = new Color(0, 0, 0, 175);
        if (hole.Top > viewport.Top)
            _spriteBatch.Draw(_panelPixel, new Rectangle(viewport.Left, viewport.Top, viewport.Width, hole.Top - viewport.Top), dim);
        if (hole.Bottom < viewport.Bottom)
            _spriteBatch.Draw(_panelPixel, new Rectangle(viewport.Left, hole.Bottom, viewport.Width, viewport.Bottom - hole.Bottom), dim);
        if (hole.Left > viewport.Left)
            _spriteBatch.Draw(_panelPixel, new Rectangle(viewport.Left, hole.Top, hole.Left - viewport.Left, hole.Height), dim);
        if (hole.Right < viewport.Right)
            _spriteBatch.Draw(_panelPixel, new Rectangle(hole.Right, hole.Top, viewport.Right - hole.Right, hole.Height), dim);
    }

```

- [ ] **Step 3: Use it in place of the unconditional dim, only when there's no head**

Replace this block in `DrawConversationPanel` (added by Task 1):
```csharp
        _panelPixel ??= CreatePixel();
        EnsureDialogFrameArt();

        Rectangle viewport = GraphicsDevice.Viewport.Bounds;

        // P89: the dialogue is a screen takeover — dim the world so the head + text read as the FO2 dialog
        // screen (game_dialog.cc darkens the captured scene), instead of floating over live, lit play.
        _spriteBatch.Draw(_panelPixel, viewport, new Color(0, 0, 0, 175));

        // FO2 lays the dialog in a 640x480 frame centred on screen: the head display at window-local
        // (126,14), the reply window at (135,225). ported from fallout2-ce src/game_dialog.cc
        // _gdCreateHeadWindow() (:2380): the frame draws unconditionally, head or not.
        int frameX = Math.Max(0, (viewport.Width - 640) / 2);
        int frameY = Math.Max(0, (viewport.Height - 480) / 2);

        bool framed = _dialogFrame is not null;
```
with:
```csharp
        _panelPixel ??= CreatePixel();
        EnsureDialogFrameArt();

        Rectangle viewport = GraphicsDevice.Viewport.Bounds;

        // FO2 lays the dialog in a 640x480 frame centred on screen: the head display at window-local
        // (126,14), the reply window at (135,225). ported from fallout2-ce src/game_dialog.cc
        // _gdCreateHeadWindow() (:2380): the frame draws unconditionally, head or not.
        int frameX = Math.Max(0, (viewport.Width - 640) / 2);
        int frameY = Math.Max(0, (viewport.Height - 480) / 2);

        bool framed = _dialogFrame is not null;

        // P89: the dialogue is a screen takeover — dim the world so the head + text read as the FO2 dialog
        // screen (game_dialog.cc darkens the captured scene), instead of floating over live, lit play.
        // ported from fallout2-ce src/game_dialog.cc gameDialogRenderTalkingHead(nullptr, 0): a head-less
        // NPC draws nothing into the portrait cutout, so the live (undimmed) scene shows through it —
        // the dim mask must exclude that rectangle rather than covering the whole screen uniformly.
        if (framed && headId < 0)
            DimExcluding(viewport, new Rectangle(frameX + 126, frameY + 14, 388, 200));
        else
            _spriteBatch.Draw(_panelPixel, viewport, new Color(0, 0, 0, 175));
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Hexwaste.Viewer -c Debug`
Expected: `Build succeeded.`, zero errors.

- [ ] **Step 5: Verify the peek-through effect on Klint (head-less)**

Run:
```bash
scripts/hexwaste-checkpoint.sh /tmp/dialog-frame-peekthrough.png -- --create 5,5,5,5,5,5,5:0,4,5:0 --talk-hex 21101
```
Then view `/tmp/dialog-frame-peekthrough.png` with the Read tool.
Expected: the frame from Task 1 is present, and the portrait-area rectangle (upper-middle of the frame) is now visibly BRIGHTER than the dimmed area around it — showing the live Temple-of-Trials scene undimmed, matching the `fallout2-ce` reference screenshot's "you can see the scene through the portrait window" look.

- [ ] **Step 6: Verify the head-present case is unaffected**

Run:
```bash
scripts/hexwaste-checkpoint.sh /tmp/dialog-frame-headed-2.png -- --create 5,5,5,5,5,5,5:0,4,5:0 --talk-hex 21101 --force-head 1
```
Then view `/tmp/dialog-frame-headed-2.png` with the Read tool.
Expected: identical to Task 1 Step 7's screenshot — the head sprite renders normally, the surrounding scene is uniformly dimmed (the `headId < 0` condition in Step 3 above is false here, so the plain full-viewport dim path runs, unchanged from Task 1).

- [ ] **Step 7: Run the relevant golden suites**

Run:
```bash
FALLOUT2_DIR=$(pwd)/game-data DISPLAY=:0 scripts/opening-golden.sh check
FALLOUT2_DIR=$(pwd)/game-data DISPLAY=:0 scripts/quest-golden.sh check
FALLOUT2_DIR=$(pwd)/game-data DISPLAY=:0 scripts/encounter-golden.sh check
```
Expected: `golden opening: ALL PASS`, `quest e2e: ALL PASS`, `golden encounter: ALL PASS`.

- [ ] **Step 8: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.cs
git commit -m "$(cat <<'EOF'
feat(ui): let the live scene show through a head-less dialog's portrait hole

ported behavior from fallout2-ce src/game_dialog.cc
gameDialogRenderTalkingHead(nullptr, 0): a head-less NPC draws
nothing into the portrait cutout, so the live game scene shows
through undimmed rather than the dialog's uniform screen-dim. New
DimExcluding() tiles the dim overlay as four strips around the
portrait rectangle instead of covering the whole viewport -- the
modern-renderer equivalent of vanilla's DOS-era snapshot-and-patch
technique (_backgrndRects), which this project doesn't need to
replicate since there's no software back-buffer to patch.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013X1Nsyn1sjdtu4miA36EFr
EOF
)"
```

---

## Self-Review Notes

- **Spec coverage:** point 1 (load `alltlk.frm`) → Task 1 Step 2; point 2 (draw the frame unconditionally, replacing the world-dim-only backdrop for the frame's footprint) → Task 1 Step 3; point 3 (portrait area shows the live world undimmed when there's no head) → Task 2; point 4 (reply/options backdrop art, stretched to Hexwaste's dynamic panel size) → Task 1 Step 3; point 5 (fallback preserved exactly when art missing) → both tasks gate on `framed`/`_dialogFrame is not null`, and the `else` branches are the pre-existing code paths, untouched. Non-goals (the 8-rect corner-patch technique, barter window, companion-hub styling beyond sharing the same function) are explicitly not touched by any step.
- **Placeholder scan:** none — every step has literal code and expected output.
- **Type/interface consistency:** `DrawConversationPanel`'s new `isPartyMember` parameter (Task 1) is used identically at both call sites in `DrawDialogPanel` (Task 1 Step 4). `_dialogFrame`/`_dialogReplyBg`/`_dialogReplyBgParty`/`_dialogFrameTried` (Task 1 Step 2) are read by name in Task 2 Step 3 with no renaming. `DimExcluding(Rectangle, Rectangle)` (Task 2 Step 2) matches its one call site's argument order and types (Task 2 Step 3).
- **Simplification made explicit (not a spec gap):** the peek-through-vs-dim decision in Task 2 gates on `headId < 0` (was a head ever requested), not on whether `DrawTalkingHead` actually resolved a texture for that `headId`. If `headId >= 0` but the specific head's art is missing, the existing separate fallback inside `DrawTalkingHead` (`if (head is null) return; // head art missing -> graceful text-only dialog`) already handles that edge case today and is untouched by this plan — that portrait rectangle will show as dimmed-but-empty rather than peek-through in that rare case, which is no worse than today's behavior (today it shows nothing useful there either) and matches the spec's allowance for the implementer to pick a mechanism without needing to special-case every combination.
