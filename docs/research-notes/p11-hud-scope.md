# P11 — Authentic HUD bar (scoping)

> **PROGRESS:** M0 (bar art + anchoring + log relocation) and M1+M2 (HP/AC via
> NUMBERS.FRM over the blanked baked fields, lit AP dot row, equipped-weapon slot +
> ammo) are DONE and screenshot-verified. Discovery during build: `iface.frm` ships
> with BAKED placeholder digits ("036"/"-258"), mode labels (SINGLE/BURST) and an AP
> dot row — so HP/AC needed the real `NUMBERS.FRM` digit blitter (not AAF text) over
> a field-blank to `(32,32,32)`, which the scope had deferred; it's in now. Remaining:
> M3 green monitor, M4 clickable buttons, M5 polish (dynamic mode-label highlight,
> press art, the round combat buttons).

Scope for the iconic Fallout 2 bottom interface bar (the metal panel: green
message monitor on the left, equipped-weapon slot + AP dots + attack-mode in the
centre, HP/AC readout, and the INV/OPT/SKILLDEX/MAP/CHA/PIP buttons). Research +
plan only — no code yet. Ported from `reference/fallout2-ce/src/interface.cc`.

## Where we are today

Gameplay uses a **minimal functional HUD**: a yellow HP/AP text line + a 5-line
message log bottom-left (`ViewerGame.cs:4459`/`4559`), keyboard commands
(`F`/`B`/`V`/`I`/`C`/`Z`/`R`/`F5`/`F9`), and plain custom panels. No iface art.
The worldmap screen already loads `art\intrface\*.frm` and HitTests clicks — the
proven template to copy.

## Feasibility: green-lit (both unknowns resolved)

All art verified present in `master.dat` (loaded from the user's copy at runtime,
**never committed**; no rendered-art screenshots in the repo, per the earlier scrub):

| Asset | FRM | Size |
|---|---|---|
| Bar background | `IFACE.FRM` (NOT `INTRFACE.FRM`) | 640×99 |
| Digit font (HP/AC) | `NUMBERS.FRM` | 360×17 |
| AP dots | `LILREDUP/DN.FRM` | 15×16 |
| 6 tab buttons | `INVBUTUP/DN`, `OPTIUP/DN`, `MAPUP/DN`, `CHAUP/DN`, `PIPUP/DN`, `SKLDXON/OFF` | per-button |
| Skilldex panel | `SKLDXBOX.FRM` | 185×368 |
| Monitor font | `FONT1.AAF` | already loaded (`AafFontRenderer`) |

- **The green monitor font is `font1.aaf`** — the engine's interface font 101 = `font(N-100).aaf`, which the viewer already loads. The monitor is byte-faithful *for free*; only the 167×60 clip rect + ~5-line cap are new.
- **The HP/AC number font is sidesteppable.** `NUMBERS.FRM` is a fiddly 9px-sub-rect blitter with a 0/120/240 colour-band trick + rolling animation. Functional-authentic uses AAF text (tinted green/yellow/red) at the same spot; `NUMBERS.FRM` is a clean self-contained upgrade later.
- All live data already exists (HP/AC via `GetCritterState`, AP via `_combat.DudeAp`, weapon via `EquippedWeapon`, messages via `_messageLog`); every button targets an existing method.

## Decisions

**Fidelity: functional-authentic (recommended).** Real `IFACE.FRM` + real button
FRMs + real `LILREDUP` AP dots + real attack-mode label FRMs (single/burst/swing/
…, chosen by the weapon anim code we already derive) + the monitor at the engine's
own font. HP/AC via AAF text (not `NUMBERS.FRM`) — indistinguishable in a
screenshot, sidesteps the one fiddly new system. Pixel-perfect (`NUMBERS.FRM` +
up/down press art + digit-roll) is a contained later upgrade.

**Window: pin the bar bottom-centre at native 1:1 scale.** The world camera has no
zoom (only `PanX/PanY`); the map renders at native pixel scale at any window size.
`barX=(Viewport.Width-640)/2`, `barY=Viewport.Height-99`, every hit-rect/draw-pos
computed fresh each frame from the viewport (the pattern `DrawTextOverlay` already
uses), so resize "just works". Scaling would blur the point-clamped art and need
zoom math we don't have. Edge case: window <640px wide → clamp `barX` to 0 +
document. The bar draws in the existing `SpriteBatch` after `DrawItemPanels`, so
modal panels still correctly overlay it (like the engine, where dialog covers the bar).

## Milestones (functional-authentic)

- **M0 — bar art + anchoring + log relocation.** `InterfaceBar` class (mirror
  `WorldmapScreen`): path-load `art\intrface\iface.frm`, draw bottom-centre native.
  **Relocate the existing bottom-left log + yellow HP/AP line in the SAME commit** —
  they sit where the bar lands and would collide. One-session, screenshottable.
  Ref: `interface.cc:319-320,339-344`.
- **M1 — HP/AC/AP readouts.** AP dots (`LILREDUP`, x=316,y=14 step 9) from live AP;
  HP/AC AAF text (green/yellow/red <25%/<50%, `interface.cc:889-894`) at x=473,y=40/75.
- **M2 — weapon slot + attack-mode + change-hands.** Centre slot (x=267,y=26,
  188×67): equipped weapon FRM + ammo bar + the real attack-mode FRM (single/burst/
  swing/…, `interface.cc:1656-1703`); change-hands button → existing in-hand toggle.
- **M3 — green message monitor.** 167×60 @ (23,24), `AafFontRenderer` tinted
  `Color(0,252,0)`, newest-at-bottom, ~5-line cap, fed by `_messageLog`.
- **M4 — the 6 clickable tab buttons.** Build `_barButtonRects` per frame (like
  `_dialogOptionRects`), hit-test in the non-modal branch before map-click handling;
  INV→`_inventoryOpen`, OPT→menu hook, SKILLDEX→`_skillAllocOpen`, MAP→`_worldmapOpen`,
  CHA→character sheet, PIP→placeholder/rest. **Keyboard shortcuts stay — buttons are additive.**
- **M5 — polish.** Up/down press art; the two round END TURN / END COMBAT buttons
  (shown only in combat) → existing Space/Enter; optional `NUMBERS.FRM` HP/AC upgrade;
  optional skilldex flyout.

## Effort & risks

~**4–5 sessions** (functional-authentic); ~6–7 if every pixel-perfect upgrade is folded in.

Risks: (1) coordinate accuracy + the legacy log/HUD-text collision — mitigated by
per-frame `barX/barY + literal engine coord` and relocating the old log in M0;
screenshot per milestone via the `RenderTarget2D` path (backbuffer readback races
the GPU). (2) number font — sidestepped by the AAF choice. (3) resize — low risk
given native-pin. (4) `IFACE.FRM` colour-cycling — check on load (likely static;
route through `FrmCache.OnPaletteChanged` if it has cycling indices). (5) keeping
keyboard commands alive — hit-test only in the non-modal branch, additive.

## Recommendation

One phase (P11), M0–M5, functional-authentic. No research-gated unknowns remain.
**Smallest authentic slice = M0+M1+M2** (real bar pinned bottom-centre + AP dots +
HP/AC + weapon slot with its attack-mode label) — that screenshot already reads as
"the Fallout 2 HUD". M3 (monitor) + M4 (buttons) make it interactive; M5 is gravy.
If you want the minimum demo-able beat: **M0 alone** (bar pinned over the live map,
log relocated) is a one-session, low-risk, screenshottable proof of the anchoring.
