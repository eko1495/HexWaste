# Authentic dialog frame

## Problem

Hexwaste's in-game dialog never draws the real Fallout 2 dialog interface art. It
dims the world, floats a head sprite directly over it when the NPC has one
(`ViewerGame.cs:DrawTalkingHead`), and renders the reply/options text over a
plain flat dark rectangle (`ViewerGame.cs:DrawConversationPanel`). NPCs with no
head art (e.g. Klint, `AAcklint.int`) get no frame at all — just that flat
rectangle pinned to the bottom of the screen. This was found and reported by
the user while comparing Hexwaste against `fallout2-ce` and is a real, if
subtle, visual gap: vanilla's dialog is a full 640×480 modal window with metal
frame chrome around the head, not a bare text bar.

## Reference behavior (fallout2-ce)

Grounded directly in `reference/fallout2-ce/src/game_dialog.cc`:

- `_gdCreateHeadWindow()` (`:2380`) runs **unconditionally** whenever a dialog
  opens — the frame is drawn whether or not the NPC has head art.
- The frame background is `alltlk.frm` (`buildFid(OBJ_TYPE_INTERFACE, 103, 0, 0, 0)`,
  `gameDialogWindowRenderBackground()` at `:4470`), blitted at 640×480,
  centered on screen (`talk_to_create_background_window()` at `:4453`,
  `GAME_DIALOG_WINDOW_WIDTH`/`HEIGHT` = 640/480).
- The reply/options text sits over a second piece of art:
  `di_talk.frm` (interface FID 99) for a regular NPC, `di_talkp.frm` (FID 389)
  for a party member (`_talkToRefreshDialogWindowRect()` at `:4488`), at
  window-local `(135, 225)`, base size 379×58
  (`GAME_DIALOG_REPLY_WINDOW_X/Y/WIDTH/HEIGHT`, `:54-57`).
- The head itself renders into a window-local `(126, 14)`, 388×200 area
  (already the exact rect Hexwaste positions head sprites in —
  `DrawTalkingHead`'s own comment cites `game_dialog.cc gameDialogRenderTalkingHead()`
  at `:4590`).
- When there is no head, `_gdSetupFidget()` (`:2440`) calls
  `gameDialogRenderTalkingHead(nullptr, 0)` — it draws nothing into that area,
  so whatever is behind the frame shows through. `alltlk.frm`'s portrait region
  is transparent (palette index 0, per this project's standing FRM convention
  — CLAUDE.md "Transparent color = palette index 0") specifically so a head
  sprite *or* the live scene can show through it. The 8 small corner/edge
  rectangles vanilla separately snapshots and patches back (`_backgrndRects`,
  `:237`) are a DOS-era software-blit workaround for the frame art's rounded
  corners — an implementation detail of that renderer, not an observable
  behavior Hexwaste needs to replicate; the observable behavior is simply
  "the live world shows through the portrait hole when there's no head."

## Design

Confined to `DrawConversationPanel` (`ViewerGame.cs`) and its caller of
`DrawTalkingHead`. No new state, no script/VM changes, no golden-transcript
impact expected (dialog goldens are text-transcript based, not screenshot
based — verified during this session's comparison work).

1. **Load two new lazily-cached textures**, following the exact pattern
   already used for `_mainMenuBg`/`_charBg`/`_invBox` (load on first live
   `Draw`, stay `null` headless so goldens are unaffected):
   - `alltlk.frm` (interface FID 103) — the 640×480 frame.
   - `di_talk.frm` (FID 99) and `di_talkp.frm` (FID 389) — the reply/options
     backdrop; select by whether the speaker is a party member, matching
     vanilla's `gGameDialogSpeakerIsPartyMember` branch.
2. **Draw the frame unconditionally** whenever a dialog is open, at the same
   640×480-centered origin Hexwaste already uses for the main menu, character
   sheet, and inventory screens (`(viewport.Width-640)/2`, `(viewport.Height-480)/2`)
   — replacing the current world-dim-only backdrop for the frame's own footprint.
3. **Head portrait area** (`frameX+126, frameY+14`, 388×200 — unchanged from
   today): if a head texture resolves, draw it exactly as now. If not, that
   rectangle must show the live game world **undimmed** — the existing
   whole-viewport dim overlay needs to exclude this one rectangle (dim
   everything outside the frame, and outside this rectangle within the frame,
   but not inside it) so the transparent portrait hole in `alltlk.frm` reveals
   the bright scene behind, matching vanilla. The exact draw-order mechanism
   (e.g. dimming everything then re-drawing an undimmed crop of the world
   into that rect before the frame, vs. building the dim overlay as a mask
   that already excludes the rect) is an implementation choice, not a design
   commitment made here.
4. **Reply/options backdrop**: draw `di_talk.frm`/`di_talkp.frm` at window-local
   `(135, 225)` as the background for the existing reply/options text
   rendering (name, wrapped reply, numbered options) — replacing today's flat
   `Color(8,8,8,230)` rectangle. Vanilla's own art is a fixed 379×58 base
   size; Hexwaste's panel height is already computed dynamically from content
   length (`(replyLines.Count + optionLines.Count + 3) * lineHeight + 24`).
   Keep that dynamic sizing — stretch or extend the backdrop art to cover the
   computed height rather than clipping content to the vanilla base size,
   since Hexwaste's dialog text is not always as short as vanilla's fixed box
   assumed.
5. **Fallback preserved exactly**: if `alltlk.frm` (or the reply backdrop)
   fails to load — the existing headless/no-game-data path — keep today's
   plain text-only panel byte-for-byte. This is not a new fallback; it is the
   current behavior, and it must not regress or change output for any
   headless/golden run.

## Non-goals

- Replicating the 8-rectangle corner-patch technique itself (a DOS software-
  renderer detail; see above).
- Companion-hub conversation panel styling beyond what already shares
  `DrawConversationPanel` — no new visual treatment for that path is being
  designed here, though it will incidentally pick up the same frame since it
  calls the same function today.
- Barter window changes — out of scope; this spec is the plain-talk dialog
  screen only.

## Open items for implementation

- Exact technique for excluding the portrait rectangle from the dim overlay
  (see point 3) is left to the implementer; whichever approach is simplest
  given Hexwaste's existing SpriteBatch draw-order conventions is fine.
- Whether `di_talk.frm`/`di_talkp.frm` art tiles or stretches cleanly to a
  taller-than-379×58 box should be checked against the actual asset once
  extracted; if it looks wrong stretched, tiling the art's vertical strip is
  an acceptable alternative — the goal is "a plausible metal/tan backdrop
  behind the text," not pixel-exact reproduction of a box that vanilla never
  had to resize.
