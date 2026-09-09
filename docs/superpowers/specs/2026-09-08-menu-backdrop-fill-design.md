# Menu backdrop fill — design

> **Superseded 2026-09-09** by `2026-09-09-menu-backdrop-unstretch-design.md`: stretching
> the art moved the painted button slots away from the `MenuOrigin()`-anchored overlays
> (96 virtual px at 1280x720). Vanilla centres the 640x480 window unstretched; Hexwaste
> does again.

## Problem

The main menu (and every other "menu-family" screen: character pick, character
creation, the death screen, and the endgame slideshow) draws its authentic 640x480
backdrop art centered in the virtual viewport via `MenuOrigin()`, with the rest of the
viewport filled black — a hard letterbox. This was deliberate from the original UI
Scale work (`DrawAuthenticMainMenu`'s own doc comment: "centred in a black letterbox"),
avoiding the non-uniform stretch fo2ce itself applies to its whole buffer.

At a 4:3-ish window this was barely visible. Discovered during today's
fullscreen-by-default work (`docs/superpowers/specs/2026-09-08-fullscreen-by-default-design.md`):
at any 16:9 window the gap is proportionally ~12.5% of the width on each side
regardless of resolution (≈160px per side at 1280x720, ≈240px per side at 1920x1080
fullscreen) — and at native fullscreen, with no window chrome around it, it reads as a
clear defect rather than a subtle letterbox.

## Decision

Stretch the backdrop art itself (only) to fill the full virtual viewport, non-uniformly
if needed. Buttons, labels, portraits, stat steppers, and subtitles — everything drawn
*after* the backdrop — keep using `MenuOrigin()`'s `(ox, oy)` exactly as today, so all
interactive UI stays pixel-perfect, undistorted, and centered. This is a narrower,
scoped exception to the project's general no-non-uniform-stretch principle
(`Hexwaste.Formats.Rendering.UiScale`'s own doc comment) — applied only to decorative
backdrop art, never to UI elements — and it's actually *more* faithful to what a real
fo2ce player sees on this exact screen, since fo2ce stretches this same art (along with
everything else) to fill any window shape.

## Scope: the 5 affected call sites

All share the identical pattern — black-fill the viewport, then draw a 640x480 texture
at `MenuOrigin()`'s centered position — confirmed via direct inspection of every call to
a menu-family backdrop field:

1. **Title / main menu** — `ViewerGame.Shell.cs:90`, `_mainMenuBg` (`mainmenu.frm`), in
   `DrawAuthenticMainMenu()`.
2. **Character pick** — `ViewerGame.Shell.cs:263`, `_pickCharBg`, in the character
   selector's draw method.
3. **Character creation** — `ViewerGame.Shell.cs:434`, `_createBg`, shared by
   `CreateStats`/`CreateTraits`/`CreateTags`.
4. **Death screen** — `ViewerGame.Shell.cs:840`, `_deathBg`.
5. **Endgame slides** — `ViewerGame.Endgame.cs:165`, in `DrawEndgame()` — this one is
   slightly different: it already has a *source* rectangle
   (`new Rectangle(0, 0, srcW, tex.Height)`, `srcW = Math.Min(tex.Width, 640)`), part of
   an existing but currently-dead "wide panning texture" code path (per its own comment:
   "referenced only by commented endgame.txt rows → dead in vanilla"). Only the
   *destination* rectangle changes here — the source-rectangle logic is untouched and
   out of scope for this fix.

**Not affected**: Credits (`ViewerGame.Shell.cs`, the scrolling-text screen) has no
backdrop texture at all — its text already positions relative to `vp.Width`/`vp.Height`
directly, not `MenuOrigin()`. Confirmed by inspection: it's already correct.

## Implementation

Add one shared private helper to `ViewerGame.Shell.cs` (near `MenuOrigin()`, since it's
the same conceptual family):

```csharp
/// <summary>Draws a menu-family backdrop stretched to fill the full virtual viewport —
/// matching fo2ce's own non-uniform stretch, but scoped to this decorative art only.
/// Buttons/labels/portraits drawn afterward keep using MenuOrigin()'s centered,
/// undistorted position — only the backdrop itself is stretched.</summary>
private void DrawMenuBackdrop(Texture2D bg)
{
    Rectangle vp = VirtualViewport();
    _spriteBatch.Draw(bg, new Rectangle(0, 0, vp.Width, vp.Height), Color.White);
}
```

Each of the 4 simple call sites (Title, Character Pick, Character Creation, Death)
currently does:

```csharp
Rectangle vp = VirtualViewport();
_panelPixel ??= CreatePixel();
_spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
(int ox, int oy) = MenuOrigin();
_spriteBatch.Draw(_xxxBg, new Rectangle(ox, oy, 640, 480), Color.White);
```

Replace the black-fill-then-centered-draw pair with the helper (the black fill is no
longer necessary once the backdrop covers the full viewport itself, but every one of
these methods already null-checks its backdrop field and returns `false`/early before
reaching this point, so the backdrop is guaranteed non-null and guaranteed to cover the
full viewport — dropping the black fill is safe, not just simpler):

```csharp
(int ox, int oy) = MenuOrigin();
DrawMenuBackdrop(_xxxBg);
```

(`ox`/`oy` are still needed for every draw call that follows this one — buttons, labels,
etc. — so `MenuOrigin()` itself is unchanged and still called at each site.)

The Endgame slide site keeps its existing source-rectangle line unchanged and only
widens the destination rectangle:

```csharp
_spriteBatch.Draw(tex, new Rectangle(0, 0, vp.Width, vp.Height),
    new Rectangle(0, 0, srcW, tex.Height), Color.White);
```

(Endgame's own black-fill line, `ViewerGame.Endgame.cs:157`, stays — unlike the other 4
sites, `DrawEndgame()` doesn't `return false` before this point if `GetEndgameTexture`
returns null, i.e. `tex` can legitimately be absent for a slide with no art, so the
black fill remains the correct fallback background in that case.)

## Non-goals

- No change to `MenuOrigin()` — the coordinate system every button/label/stepper/portrait
  uses stays exactly as-is.
- No fix to Endgame's dead "wide panning texture" path — pre-existing, unrelated to this
  fix, and out of scope (per its own comment, `slide.Panning` currently changes nothing).
- No change to Credits — already correct.
- No change to any already-migrated non-menu-family screen (HUD, dialog, worldmap,
  inventory, etc.).

## Testing

No automated test project covers `ViewerGame` (same situation as the recent
scroll-border-clamp and fullscreen-by-default work) — verify manually via screenshot,
the same way the bug was originally found:

1. Launch Hexwaste fullscreen (`dotnet run --project src/Hexwaste.Viewer --
   --game-dir game-data`) and screenshot the main menu. Confirm the backdrop art fills
   edge-to-edge with no black bars, and the six buttons/labels/copyright/version text
   remain in their same centered position and size as before this fix (compare against
   `scratch/compare-runs/scroll-clamp-verify-20260908-152639/hexwaste/` or any earlier
   1280x720 main-menu screenshot from this session — button positions relative to the
   window center should be unchanged, only the backdrop should differ).
2. Click NEW GAME → confirm the character-pick screen's backdrop also fills edge-to-edge
   while the portrait, stat block, and TAKE/CREATE/BACK buttons stay centered and
   correctly clickable (click TAKE and confirm it still works — this exercises that the
   click-band coordinates, still `MenuOrigin()`-relative, weren't affected).
3. Confirm the character-creation screens (CREATE CHARACTER) and, if reachable without
   too much setup, the death screen, show the same edge-to-edge backdrop with centered,
   functional UI.
4. Confirm Credits (unaffected by this fix) still renders correctly — a quick sanity
   check that nothing in this change accidentally touched it.
