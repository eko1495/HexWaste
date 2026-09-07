# UI Scale — Stage 6: Shell, Endgame, Cutscene Screens, and the HUD Text Overlay

## Problem

Worldmap chrome (Stage 5) closed out every gameplay screen, but its own whole-branch review found
the claim "UI Scale is done project-wide" was false: six more surfaces still render at native
device-pixel size — death art/narration, the ending slideshow, credits, the cutscene browser, the
movie player/card, and the persistent gameplay HUD text overlay (HP/AP readout + hover-name
tooltip). One of these, `MenuOriginDevice()`, even carries its own never-executed "later stages
delete this" doc comment. This spec closes out UI Scale entirely.

## Grounding (confirmed by a direct source-tree read this session)

- **`MenuOrigin()`/`MenuOriginDevice()`** (`ViewerGame.Shell.cs:46-59`) are identical except their
  viewport source:
  ```csharp
  private (int ox, int oy) MenuOrigin()
  {
      Rectangle vp = VirtualViewport();
      return ((vp.Width - 640) / 2, (vp.Height - 480) / 2);
  }

  /// <summary>The device-pixel twin of MenuOrigin(), for the 640x480 shell screens that are NOT
  /// yet drawn inside a scaled SpriteBatch block (death art/narration, the endgame slides).
  /// Later stages delete this once those screens fold into their own scaled batch.</summary>
  private (int ox, int oy) MenuOriginDevice()
  {
      Viewport vp = GraphicsDevice.Viewport;
      return ((vp.Width - 640) / 2, (vp.Height - 480) / 2);
  }
  ```
  `MenuOriginDevice()` can simply be **deleted** once its exactly-3 callers (confirmed via
  whole-tree grep — no others exist) switch to `MenuOrigin()`: `ViewerGame.Shell.cs:848`
  (`DrawDeathArt`), `ViewerGame.Endgame.cs:159` (`DrawEndgame`), `ViewerGame.Endgame.cs:291`
  (`DrawDeathNarration`).
- **All six surfaces live inside, or are called from, one method: `DrawTextOverlay()`**
  (`ViewerGame.Hud.cs:498-680`-ish), confirmed via direct read of the full method body. Unlike
  worldmap/inventory/etc. (each their own top-level `Draw*()` method with one clean call site),
  this method has several **independent sequential `if` blocks**, each currently reading
  `GraphicsDevice.Viewport` on its own — there is no single choke point. The already-scaled
  main-menu-family block inside this same method (`:608-616`, from an earlier stage) is the
  established template for fixing one block without touching its neighbors:
  ```csharp
  _spriteBatch.End();
  _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: UiScaleMatrix());
  bool handled = _menu == MenuState.Title ? DrawAuthenticMainMenu()
      : _menu == MenuState.CharacterPick ? DrawAuthenticSelector()
      : _menu is MenuState.CreateStats or MenuState.CreateTraits or MenuState.CreateTags
        && DrawAuthenticCreation();
  _spriteBatch.End();
  _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
  ```
  That block's own comment (`:602-607`) explicitly documents the gap this spec closes: *"Credits/
  Endgame above don't use MenuOrigin() and stay unscaled; the plain-text fallback below (art
  missing) also stays unscaled, matching its pre-existing, already-degraded presentation."* The
  plain-text fallback (art missing) is correctly staying unscaled by established convention
  (matching every other screen's art-absent fallback) — Credits/Endgame are the two this spec
  fixes.
- **Death screen block** (`ViewerGame.Hud.cs:526-553`) — the fallback dark overlay and "YOU HAVE
  DIED" text are inline in `DrawTextOverlay()`, not a separable method, so the whole block scopes
  as one unit (matching the main-menu-family block's shape):
  ```csharp
  if (_combat.IsGameOver || _debugDeathScreen)
  {
      _panelPixel ??= CreatePixel();
      if (!DrawDeathArt())
          _spriteBatch.Draw(_panelPixel,
              new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
              new Color(0, 0, 0, 170));
      DrawDeathNarration();
      var center = new Vector2(GraphicsDevice.Viewport.Width / 2f, GraphicsDevice.Viewport.Height / 2f);
      string[] lines = [ "YOU HAVE DIED", $"Level {_dudeLevel}  -  {_dudeXp} XP  -  Day {_clock.Day}", "",
          "F9  Load last save", "N   New game", "Esc Quit" ];
      float lineY = center.Y - lines.Length * _fontRenderer.LineHeight;
      foreach (string line in lines) { /* draws centered at center.X, lineY */ }
  }
  ```
  `DrawDeathArt()` (`ViewerGame.Shell.cs:836-851`) itself reads `GraphicsDevice.Viewport` once
  more internally (for its own dark backdrop) and calls `MenuOriginDevice()` — both convert.
  `DrawDeathNarration()` (`ViewerGame.Endgame.cs:286-300`) calls `MenuOriginDevice()` only — no
  other viewport read. Neither has a mouse hit-test; F9/N/Esc are raw key-state checks elsewhere
  (`ViewerGame.cs:2064-2076`), unaffected by this spec.
- **`DrawEndgame()`** (`ViewerGame.Endgame.cs:150-186`) and **`DrawCredits()`**
  (`ViewerGame.Shell.cs:778-804`) are each called from their own single `if`/`else if` arm inside
  `DrawTextOverlay()`'s later cascade (`ViewerGame.Hud.cs:592-599`):
  ```csharp
  else if (_menu == MenuState.Credits)
  {
      DrawCredits();
  }
  else if (_menu == MenuState.Endgame)
  {
      DrawEndgame();
  }
  ```
  Each scopes independently, matching the main-menu-family block immediately below it. Neither
  method's own `Update()` counterpart (`UpdateCredits`/`UpdateEndgame`) does position-based
  hit-testing — both read only `mouse.LeftButton` button state (click-to-advance), confirmed via
  direct read.
- **The movie player** (`MviePlayer.cs:139-149`) draws through a plain `SpriteBatch.Draw` with a
  regular `Texture2D` — architecturally identical to every FRM-art screen already migrated, not a
  special video-rendering path:
  ```csharp
  public void Draw(SpriteBatch sb, Texture2D pixel, Viewport vp)
  {
      sb.Draw(pixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
      if (_texture is null) return;
      int ox = (vp.Width - _texture.Width) / 2;
      int oy = (vp.Height - _texture.Height) / 2;
      sb.Draw(_texture, new Rectangle(ox, oy, _texture.Width, _texture.Height), Color.White);
  }
  ```
  called at `ViewerGame.Hud.cs:555-559`:
  ```csharp
  if (_moviePlayer is not null)
  {
      _panelPixel ??= CreatePixel();
      _moviePlayer.Draw(_spriteBatch, _panelPixel, GraphicsDevice.Viewport);
  }
  ```
  `Draw`'s `Viewport vp` parameter only ever reads `.Width`/`.Height` — changing it to
  `Rectangle viewport` (matching `WorldmapScreen`'s existing convention) and passing
  `VirtualViewport()` is a pure signature swap, no internal logic change. No mouse hit-test —
  `Update()` only checks button/key state to skip playback (`ViewerGame.cs:1994-2012`).
- **`_movieCard`** (`ViewerGame.Hud.cs:564-582`) is a plain text-over-black card (no art) with no
  positional hit-test (`ViewerGame.cs:2125-2130` checks button/key state only) — scopes the same
  way as credits.
- **The cutscene browser** (`ViewerGame.Hud.cs:684-713`) is the one shell screen with a real
  positional hit-test, sharing a choke point with its `Update()`-side hover math:
  ```csharp
  private void DrawCutsceneMenu()
  {
      _panelPixel ??= CreatePixel();
      int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
      _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 235));
      List<string> names = CutsceneNames();
      (float firstRowY, float rowH) = CutsceneListLayout(names.Count);
      // draws title + each row centered at vw/2f, using firstRowY + i*rowH
  }

  private (float firstRowY, float rowH) CutsceneListLayout(int count)
  {
      float rowH = _fontRenderer.LineHeight * 1.35f;
      float firstRowY = GraphicsDevice.Viewport.Height / 2f - count * rowH / 2f;
      return (firstRowY, rowH);
  }
  ```
  `Update()`'s hit-test (`ViewerGame.cs:2081-2117`, inside `if (_cutsceneMenuOpen)`):
  ```csharp
  (float firstRowY, float rowH) = CutsceneListLayout(names.Count);
  int cx = GraphicsDevice.Viewport.Width / 2;
  int hover = -1;
  if (Math.Abs(mouse.X - cx) < 220)
  {
      int row = (int)((mouse.Y - firstRowY) / rowH);
      if (row >= 0 && row < names.Count)
          hover = row;
  }
  if (hover >= 0) _cutsceneMenuIndex = hover;
  bool clickPlay = hover >= 0 && mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
  ```
  This is the same `ChromeOrigin`/`CutsceneListLayout`-style choke point shape as worldmap chrome
  — converting `CutsceneListLayout()` to `VirtualViewport()` propagates to both its caller
  (`DrawCutsceneMenu`) and the `Update()` hit-test; only `cx`/`mouse.X`/`mouse.Y` in `Update()`
  need their own conversion to `uiMouse`.
- **The persistent HUD text overlay** — approved into scope — is the top of `DrawTextOverlay()`
  itself (`ViewerGame.Hud.cs:505-524`), unconditional (runs every frame, not gated by any
  special-screen state):
  ```csharp
  if (_hoveredObject is not null && _hoveredObject != _dude?.Dude)
  {
      MouseState mouse = Mouse.GetState();
      _fontRenderer.Draw(_spriteBatch, ObjectName(_hoveredObject),
          new Vector2(mouse.X + 14, mouse.Y + 6), green);
  }

  // AP/HP text HUD above the message log.
  if (_dude is not null && GetCritterState(_dude.Dude) is { } dudeStats)
  {
      string hud = $"HP {dudeStats.CurrentHp}/{dudeStats.MaxHp}  AP {_combat.DudeAp}/{dudeStats.MaxActionPoints}"
          + $"  L{_dudeLevel} XP {_dudeXp}";
      if (AimLocation != Formats.Combat.CriticalTables.LocationUncalled)
          hud += $"  |  aim: {AimName(AimLocation)} (V)";
      if (_combat.Phase != Formats.Combat.CombatPhase.Idle)
          hud += $"  |  round {_combat.Round}: " + (...);
      int hudY = GraphicsDevice.Viewport.Height - _hudBarHeight - 8 - (Math.Min(_messageLog.Count, MessageLogFallbackLines) + 1) * _fontRenderer.LineHeight - 4;
      _fontRenderer.Draw(_spriteBatch, hud, new Vector2(8, hudY), new Color(252, 252, 84));
  }
  ```
  `_hudBarHeight` is an established **device-pixel** quantity (its own doc comment,
  `ViewerGame.Hud.cs:117-121`), with a precedent conversion this project already shipped for
  exactly this unit mismatch, at `SkilldexOrigin` (`ViewerGame.Panels.cs:634-646`):
  ```csharp
  Rectangle vp = VirtualViewport();
  int hudBarVirtual = (int)(_hudBarHeight / UiScale());
  ```
  `hudY`'s formula needs this identical `hudBarVirtual` substitution once wrapped in a scaled
  block. The hover-name tooltip's `mouse.X/Y` converts to `uiMouse.X/Y` (its local `MouseState
  mouse = Mouse.GetState();` becomes unnecessary once only the position is needed — button state
  is never read here — so it can be replaced outright by `UiMouse()`).
- **Confirmed correctly excluded (no changes needed)**: `ViewerGame.UiScale.cs`'s own wrapper
  implementations; `WorldmapScreen.cs` (already converted) and its untouched legacy fallback;
  `DrawSkilldexTextFallback`/`AimDialogPanelRect`/the elevator-picker fallback (pre-existing,
  documented art-absent fallbacks from their own stages); `DrawMapFade` (a full-bleed solid-color
  quad, nothing positioned to scale); all world-space/camera/audio code (`UpdateAmbientLife`,
  camera centering, `PickHex`, `SfxGain`); the plain-text main-menu fallback
  (`ViewerGame.Hud.cs:617-661`, already documented as deliberately unscaled, matching its
  pre-existing degraded presentation) and the HUD-bar-hidden message-log fallback
  (`:664-676`-ish) — same convention.

## Design

Two tasks, mirroring the split every prior multi-piece stage used (Stage 3→3a/3b/3c, Stage
4→4a+HUD-bar, Inventory→Piece1/2, Stage 5→Task1/2) — here split by whether the surface has real
position-based math to get right, versus pure visual scoping.

1. **Task A — static screens (no hit-test math beyond button/key state).** Delete
   `MenuOriginDevice()`; convert its 3 callers to `MenuOrigin()`. Scope the death-screen `if`
   block (`ViewerGame.Hud.cs:526-553`) as one unit, converting every internal
   `GraphicsDevice.Viewport` read (including inside `DrawDeathArt`/`DrawDeathNarration`) to
   `VirtualViewport()`. Scope the movie-player block (`:555-559`) after changing
   `MviePlayer.Draw`'s signature to take `Rectangle viewport`. Scope the `_movieCard` block
   (`:564-582`). Scope the `DrawCredits()`/`DrawEndgame()` call sites (`:592-599`) each
   independently, matching the main-menu-family block's exact shape immediately below them.
2. **Task B — real position math.** Convert `CutsceneListLayout()` to `VirtualViewport()`
   (propagates to `DrawCutsceneMenu` automatically) and convert `Update()`'s `cx`/`mouse.X/Y`
   hover math to `uiMouse`/`VirtualViewport()`. Scope `DrawTextOverlay()`'s top two unconditional
   blocks (hover-name tooltip, AP/HP HUD line) into a scaled block, converting the tooltip's mouse
   read to `UiMouse()` and `hudY`'s formula to use `_hudBarHeight / UiScale()` (the
   `SkilldexOrigin` precedent) inside a virtual-unit viewport rect.

## Non-goals

- Every surface listed above as "confirmed correctly excluded" — no changes.
- Any change to `ViewerGame.Harness.cs` — not grounded as touching any of these surfaces in this
  survey; the plan-writer should confirm with a targeted grep before finalizing, following this
  project's established practice, but no evidence surfaced of a caller there.
- The plain-text main-menu fallback and the HUD-bar-hidden message-log fallback — both explicitly
  documented as deliberately unscaled, matching every other screen's art-absent convention.

## Testing

Same as every prior stage: no new pure-math logic (this stage only applies already-proven
primitives — `VirtualViewport()`, `UiMouse()`, `UiScaleMatrix()`, and the `_hudBarHeight /
UiScale()` conversion already shipped for `SkilldexOrigin`), so verification is the existing
golden suites (headless, unaffected — none of these surfaces render in a headless run without a
specific triggering condition none of the goldens currently drive) plus manual screenshot checks
at a non-4:3 window, using CLI probes confirmed via `Program.cs`/`ViewerGame.cs:1523-1531`:
`--menu death` (sets `_debugDeathScreen`) for the death screen, `--menu credits` (also sets
`_creditsScroll = 320`, mid-scroll for the screenshot) for credits, `--menu endgame` (sets global
408 and calls `ShowEndgameSlideshow()` — the Arroyo victory slide) for the ending slideshow, and
`--cutscene-menu` for the cutscene browser. The persistent HUD text needs no special flag — it's
visible in ordinary gameplay whenever `_dude` exists, so any normal in-game screenshot exercises
it. Confirm each renders at the same scale as every other already-migrated screen. This is the
final UI Scale stage — after it, there should be zero remaining `GraphicsDevice.Viewport`/bare
`Mouse.GetState()` reads feeding rendering or hit-test math anywhere in `src/Hexwaste.Viewer/`
outside the documented, deliberately-unscaled fallbacks listed in Non-goals.
