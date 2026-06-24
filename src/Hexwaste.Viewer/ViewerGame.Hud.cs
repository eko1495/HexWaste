using Hexwaste.Formats;
using Hexwaste.Formats.Art;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Pal;
using Hexwaste.Formats.Proto;
using Hexwaste.Formats.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

// The authentic bottom HUD bar (iface.frm): the HP/AC digit roll, the bar render (weapon slot,
// AP pips, mode label, message monitor), the clickable bar buttons + monitor scroll, weapon-mode
// cycling, and the cursor-name/log text overlay. Pure move from ViewerGame.cs; fields stay central.
public sealed partial class ViewerGame
{
    /// <summary>Step the HP/AC HUD counters one unit per <c>StepMs</c> toward the real
    /// stat — the iconic Fallout digit roll (P11 M5). -1 snaps (fresh dude/load); a big
    /// swing rolls visibly over a beat. Cosmetic only; never printed.</summary>
    private void UpdateHudRoll(double elapsedMs)
    {
        if (_dude is null || GetCritterState(_dude.Dude) is not { } stats)
            return;

        if (_hudDisplayedHp < 0 || _hudDisplayedAc < 0)
        {
            _hudDisplayedHp = stats.CurrentHp;
            _hudDisplayedAc = DudeHudAc(stats);
            _hudRollAccumulatorMs = 0;
            return;
        }

        const double StepMs = 25; // ~40 digits/sec — fast enough to feel snappy, slow enough to read
        _hudRollAccumulatorMs += elapsedMs;
        while (_hudRollAccumulatorMs >= StepMs)
        {
            _hudRollAccumulatorMs -= StepMs;
            _hudDisplayedHp += Math.Sign(stats.CurrentHp - _hudDisplayedHp);
            _hudDisplayedAc += Math.Sign(DudeHudAc(stats) - _hudDisplayedAc);
        }
    }

    /// <summary>The dude's AC as the engine's interfaceRenderArmorClass shows it (P77): the static AC plus
    /// his remaining-AP dodge during combat — nonzero only when it's NOT his turn, so the readout visibly
    /// rises while enemies act and he's dodging. Out of combat = the static AC (HUD goldens are Idle).</summary>
    private int DudeHudAc(Formats.Combat.CritterState stats) =>
        stats.ArmorClass + (_dude is { } d && _combat.Phase != Formats.Combat.CombatPhase.Idle
            ? _combat.RemainingApDodge(d.Dude) : 0);

    /// <summary>The authentic bottom HUD bar (P11): the iface.frm panel pinned
    /// bottom-centre at native scale, with live readouts composed on top. Sets
    /// <see cref="_hudBarHeight"/> so the message log + HUD text lift above it.</summary>
    private void DrawInterfaceBar()
    {
        if (_interfaceBar is not { Loaded: true } bar || _worldmapOpen)
        {
            _hudBarHeight = 0;
            return;
        }

        Rectangle viewport = GraphicsDevice.Viewport.Bounds;
        _hudBarHeight = InterfaceBar.Height;
        bar.Draw(_spriteBatch, viewport);

        if (_dude is null || GetCritterState(_dude.Dude) is not { } stats)
            return;
        Point o = bar.Origin(viewport); // bar-local coords (interface.cc) -> screen = o + coord

        // --- M2: equipped-weapon slot (centre, bar-local 267,26 188x67; interface.cc:505,315) ---
        (Formats.Proto.ProtoInfo? weaponProto, MapObject? weaponItem) = EquippedWeapon(_dude.Dude);
        if (weaponItem is not null)
        {
            try
            {
                int fid = _protos.Get(weaponItem.Pid).InventoryFid;
                if (fid != -1)
                {
                    Texture2D tex = _frmCache.GetTexture(fid);
                    // native size, downscaled only if larger than the slot, centred
                    float s = Math.Min(1f, Math.Min(188f / tex.Width, 67f / tex.Height));
                    int dw = (int)(tex.Width * s), dh = (int)(tex.Height * s);
                    _spriteBatch.Draw(tex, new Rectangle(o.X + 267 + (188 - dw) / 2, o.Y + 26 + (67 - dh) / 2, dw, dh), Color.White);
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
            }
        }
        // Ammo count for guns, over the baked ammo bar (NUMBERS.FRM, white band).
        if (weaponProto?.Weapon is { } w && w.IsGun(weaponProto.ExtendedFlags) && weaponItem is not null && bar.Numbers is { } numAmmo)
            DrawCounter(numAmmo, WeaponAmmo(weaponProto, weaponItem), band: 0, xRight: o.X + 458, yTop: o.Y + 76);
        // The active attack-mode label, bright, at the weapon-button top-left. For a
        // burst-capable gun it reflects the LIVE _weaponMode (P15 M1 — the slot/N cycle);
        // otherwise the proto's attack-anim nibble (SWING/THRUST/SINGLE/…).
        if (weaponProto is not null && _fontRenderer is not null)
        {
            string mode = Formats.Combat.CombatEngine.IsBurstWeapon(weaponProto)
                ? (_weaponMode == WeaponMode.Burst ? "BURST" : "SINGLE")
                : AttackModeName(weaponProto);
            _fontRenderer.Draw(_spriteBatch, mode, new Vector2(o.X + 271, o.Y + 28), new Color(252, 252, 84));
        }

        // --- M1: HP/AC via NUMBERS.FRM. The bar has baked placeholder digits ("036"/
        // "-258") in dark recessed fields; blank each field to its background colour
        // first (the engine restores the field background before re-rendering), then
        // draw the live value right-aligned over it.
        if (bar.Numbers is { } numbers)
        {
            _panelPixel ??= CreatePixel();
            var fieldBg = new Color(32, 32, 32); // the recessed digit-box interior colour
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X + 474, o.Y + 40, 33, 17), fieldBg);  // HP box
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X + 474, o.Y + 75, 33, 17), fieldBg);  // AC box
            // The counters roll toward the live stat (M5); fall back to the real value
            // until the first roll step initialises them.
            int shownHp = _hudDisplayedHp >= 0 ? _hudDisplayedHp : stats.CurrentHp;
            int shownAc = _hudDisplayedAc >= 0 ? _hudDisplayedAc : DudeHudAc(stats);
            // HP: white band normal, yellow <50%, red <25% (interface.cc:889-894) — from
            // the shown value so the colour tracks the rolling digits.
            int hpBand = shownHp * 4 <= stats.MaxHp ? 2 : shownHp * 2 <= stats.MaxHp ? 1 : 0;
            DrawCounter(numbers, shownHp, hpBand, xRight: o.X + 505, yTop: o.Y + 40);
            DrawCounter(numbers, shownAc, band: 0, xRight: o.X + 505, yTop: o.Y + 75);
        }

        // AP: light the green dot sockets along the top (interface.cc:974,1001 — 10 dots,
        // x=316 step 9, y=14). A bright-green pip per current action point.
        _panelPixel ??= CreatePixel();
        int ap = Math.Clamp(_combat.DudeAp, 0, 10);
        for (int i = 0; i < ap; i++)
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X + 316 + i * 9, o.Y + 13, 6, 6), new Color(0, 252, 0));
        // P74-M4: the Bonus Move free-move pool shows as extra (lighter-green) pips after the AP dots
        // (interface.cc interfaceRenderActionPoints renders ap + free move). Inert when the pool is 0.
        int free = Math.Clamp(_combat.DudeFreeMove, 0, 10 - ap);
        for (int i = 0; i < free; i++)
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X + 316 + (ap + i) * 9, o.Y + 13, 6, 6), new Color(132, 252, 132));

        // --- M3: the green message monitor (the left screen; bar-local 24,26 ~160x55,
        // display_monitor.cc). Reuse font1.aaf (the engine's interface font) tinted
        // green; wrap to the screen width, newest at the bottom, clipped to fit. The
        // bottom-left fallback log only shows when the bar is hidden (DrawTextOverlay).
        if (_fontRenderer is not null && _messageLog.Count > 0)
        {
            const int mx = 24, my = 26, mw = 162, mh = 56;
            int maxLines = Math.Max(1, mh / _fontRenderer.LineHeight);
            var lines = new List<string>();
            foreach (string msg in _messageLog)
                lines.AddRange(_fontRenderer.WrapText(msg, mw));
            // P52-M5: show a scroll-back window (clicking the monitor halves moves _monitorScroll).
            (int start, int end, _monitorScroll) = Formats.MonitorScrollback.Window(lines.Count, maxLines, _monitorScroll);
            int ty = o.Y + my;
            for (int i = start; i < end; i++)
            {
                _fontRenderer.Draw(_spriteBatch, lines[i], new Vector2(o.X + mx, ty), new Color(0, 252, 0), shadow: false);
                ty += _fontRenderer.LineHeight;
            }
        }

        // M5: the combat-mode buttons over the far-right hazard panel — only during a
        // fight (END TURN / END COMBAT, 38x22 @ 590,43 / 590,65; interface.cc:1893).
        bool inCombat = _combat.Phase != Formats.Combat.CombatPhase.Idle;
        if (inCombat)
        {
            if (bar.EndTurn is not null)
                _spriteBatch.Draw(bar.EndTurn, new Vector2(o.X + 590, o.Y + 43), Color.White);
            if (bar.EndCombat is not null)
                _spriteBatch.Draw(bar.EndCombat, new Vector2(o.X + 590, o.Y + 65), Color.White);
        }

        // M5: press/hover feedback. While the left mouse is held on a button, overlay
        // its DOWN-state art (invbutdn/optidn/…, the same native size as the baked UP
        // button — interface.cc buttonCreate w×h) at the button's top-left; merely
        // hovering gets a soft highlight. HEXWASTE_HUD_FORCE_PRESS=<name> forces the
        // pressed look so the art can be checked in a --screenshot (a live press is
        // otherwise only on screen mid-click). Falls back to a darken tint if the DN
        // art is missing.
        _panelPixel ??= CreatePixel();
        MouseState hoverMouse = Mouse.GetState();
        string? forcePress = Environment.GetEnvironmentVariable("HEXWASTE_HUD_FORCE_PRESS");
        foreach (HudButton b in HudButtons())
        {
            if (b.CombatOnly && !inCombat)
                continue;
            var rect = new Rectangle(o.X + b.Local.X, o.Y + b.Local.Y, b.Local.Width, b.Local.Height);
            bool over = rect.Contains(hoverMouse.X, hoverMouse.Y);
            bool pressed = (over && hoverMouse.LeftButton == ButtonState.Pressed)
                || string.Equals(forcePress, b.Name, StringComparison.OrdinalIgnoreCase);
            if (pressed && bar.Pressed.TryGetValue(b.Name, out Texture2D? dn) && dn is not null)
                _spriteBatch.Draw(dn, new Vector2(rect.X, rect.Y), Color.White);
            else if (pressed)
                _spriteBatch.Draw(_panelPixel, rect, new Color(0, 0, 0, 90));
            else if (over)
                _spriteBatch.Draw(_panelPixel, rect, new Color(255, 255, 255, 45));
        }

        // HEXWASTE_HUD_DEBUG=1: translucent overlay of the clickable button rects to
        // verify they align with the baked iface buttons.
        if (Environment.GetEnvironmentVariable("HEXWASTE_HUD_DEBUG") == "1")
            foreach (HudButton b in HudButtons())
                _spriteBatch.Draw(_panelPixel, new Rectangle(o.X + b.Local.X, o.Y + b.Local.Y, b.Local.Width, b.Local.Height), new Color(255, 0, 0, 90));
    }

    /// <summary>Weapon attack-mode label from the proto's primary attack-anim nibble
    /// (extendedFlags &amp; 0xF; item.cc _attack_anim) — SWING/THRUST/SINGLE/BURST/etc.</summary>
    private static readonly string[] AttackAnimNames =
        ["", "PUNCH", "KICK", "SWING", "THRUST", "THROW", "SINGLE", "BURST", "FLAME"];

    private static string AttackModeName(Formats.Proto.ProtoInfo proto)
    {
        int anim = proto.ExtendedFlags & 0xF;
        return anim >= 0 && anim < AttackAnimNames.Length ? AttackAnimNames[anim] : "";
    }

    /// <summary>Toggle the weapon-slot attack mode (single↔burst) for a burst-capable
    /// gun; a non-burst weapon stays single (P15 M1 — the slot click + N).</summary>
    private void CycleWeaponMode()
    {
        (Formats.Proto.ProtoInfo? weaponProto, _) = _dude is null ? (null, null) : EquippedWeapon(_dude.Dude);
        if (!Formats.Combat.CombatEngine.IsBurstWeapon(weaponProto))
        {
            _weaponMode = WeaponMode.Single;
            Log("This weapon has only a single-shot mode.");
            Console.WriteLine("weapon-mode: Single (single-only)");
            return;
        }
        _weaponMode = _weaponMode == WeaponMode.Single ? WeaponMode.Burst : WeaponMode.Single;
        Log($"Attack mode: {(_weaponMode == WeaponMode.Burst ? "burst" : "single")}.");
        Console.WriteLine($"weapon-mode: {_weaponMode}");
    }

    /// <summary>The clickable HUD buttons (bar-local rects, ported from interface.cc;
    /// measured against this iface.frm). Each wires to the same action as its keyboard
    /// shortcut — the buttons are additive, the keys still work (#15 M4).</summary>
    private readonly record struct HudButton(string Name, Rectangle Local, Action OnClick, bool CombatOnly = false);

    // Bar-local button rects, ported verbatim from interface.cc buttonCreate(x,y,w,h)
    // with gInterfaceBarContentOffset=0 (our native 640-wide bar). These match where
    // the baked iface.frm buttons sit, so the DN press-art overlays exactly.
    private HudButton[] HudButtons() =>
    [
        new("INV", new Rectangle(211, 40, 32, 21), () => { _inventoryOpen = true; _panelPage = 0; PrewarmItemTextures(_dudeInventory); }), // interface.cc:360
        new("OPT", new Rectangle(210, 61, 34, 34), () => { _optionsOpen = true; }),                                       // :380
        new("MAP", new Rectangle(526, 39, 41, 19), () => { _worldmapOpen = true; }),                                      // :433
        new("CHA", new Rectangle(526, 58, 41, 19), () => { if (_dudeGcd is not null) _skillAllocOpen = true; }),          // :475
        new("PIP", new Rectangle(526, 77, 41, 19), () => { _pipboyOpen = true; }),                                        // :454
        new("SKILLDEX", new Rectangle(523, 6, 22, 21), () => { _skilldexOpen = true; }),                                  // :406
        // The weapon slot (interface.cc:505 gSingleAttackButton): click cycles the
        // attack mode (single↔burst); F fires with the selected mode (P15 M1).
        new("WEAPON", new Rectangle(267, 26, 188, 67), CycleWeaponMode),                                                 // :505
        // Combat-mode buttons (shown + clickable only during a fight; M5).
        new("ENDTURN", new Rectangle(590, 43, 38, 22), () => _combat.EndPlayerTurn(), CombatOnly: true),                  // :1903
        new("ENDCOMBAT", new Rectangle(590, 65, 38, 22),                                                                  // :1955
            () => { if (_combat.Phase != Formats.Combat.CombatPhase.Idle) _combat.Reset(); }, CombatOnly: true),
    ];

    /// <summary>Route a left-click to a HUD button if it landed on one. Returns true
    /// when handled (the caller then skips the world-interaction click).</summary>
    private bool TryClickInterfaceBar(int mouseX, int mouseY)
    {
        if (_interfaceBar is not { Loaded: true } bar || _worldmapOpen)
            return false;
        Point o = bar.Origin(GraphicsDevice.Viewport.Bounds);
        // P52-M5: the message monitor's two invisible scroll buttons (display_monitor.cc:382/391 —
        // the top half scrolls toward older history, the bottom half toward the newest).
        var monitor = new Rectangle(o.X + 24, o.Y + 26, 162, 56);
        if (monitor.Contains(mouseX, mouseY))
        {
            _monitorScroll = Math.Max(0, _monitorScroll + (mouseY < monitor.Y + monitor.Height / 2 ? 1 : -1));
            return true;
        }
        bool inCombat = _combat.Phase != Formats.Combat.CombatPhase.Idle;
        foreach (HudButton b in HudButtons())
        {
            if (b.CombatOnly && !inCombat)
                continue;
            var screen = new Rectangle(o.X + b.Local.X, o.Y + b.Local.Y, b.Local.Width, b.Local.Height);
            if (screen.Contains(mouseX, mouseY))
            {
                b.OnClick();
                return true;
            }
        }
        return false;
    }

    /// <summary>Blit a right-aligned integer from NUMBERS.FRM (the engine digit font):
    /// 3 colour bands (band*120), digits 9px (src-x band*120+9*d), minus 6px (+108).
    /// Ported from fallout2-ce src/interface.cc interfaceRenderCounter (:2049-2088).</summary>
    private void DrawCounter(Texture2D numbers, int value, int band, int xRight, int yTop)
    {
        bool negative = value < 0;
        string digits = Math.Abs(value).ToString();
        int width = digits.Length * 9 + (negative ? 6 : 0);
        int x = xRight - width;
        int bandX = band * 120;
        if (negative)
        {
            _spriteBatch.Draw(numbers, new Rectangle(x, yTop, 6, 17), new Rectangle(bandX + 108, 0, 6, 17), Color.White);
            x += 6;
        }
        foreach (char c in digits)
        {
            int d = c - '0';
            _spriteBatch.Draw(numbers, new Rectangle(x, yTop, 9, 17), new Rectangle(bandX + 9 * d, 0, 9, 17), Color.White);
            x += 9;
        }
    }

    /// <summary>Hover name near the cursor + the message log, bottom-left, in Fallout green.</summary>
    private void DrawTextOverlay()
    {
        if (_fontRenderer is null)
            return;

        var green = new Color(0, 252, 0);

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
                hud += $"  |  round {_combat.Round}: "
                    + (_combat.Phase == Formats.Combat.CombatPhase.PlayerTurn ? "your turn (F attack, Space end turn)" : "enemy turn");
            int hudY = GraphicsDevice.Viewport.Height - _hudBarHeight - 8 - (Math.Min(_messageLog.Count, MessageLogFallbackLines) + 1) * _fontRenderer.LineHeight - 4;
            _fontRenderer.Draw(_spriteBatch, hud, new Vector2(8, hudY), new Color(252, 252, 84));
        }

        if (_combat.IsGameOver)
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel,
                new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                new Color(0, 0, 0, 170));
            var center = new Vector2(GraphicsDevice.Viewport.Width / 2f, GraphicsDevice.Viewport.Height / 2f);
            string[] lines =
            [
                "YOU HAVE DIED",
                $"Level {_dudeLevel}  -  {_dudeXp} XP  -  Day {_clock.Day}",
                "",
                "F9  Load last save",
                "N   New game",
                "Esc Quit",
            ];
            float lineY = center.Y - lines.Length * _fontRenderer.LineHeight;
            foreach (string line in lines)
            {
                Color color = line == lines[0] ? new Color(252, 0, 0) : new Color(252, 252, 84);
                _fontRenderer.Draw(_spriteBatch, line,
                    new Vector2(center.X - _fontRenderer.MeasureWidth(line) / 2f, lineY), color);
                lineY += _fontRenderer.LineHeight * 1.6f;
            }
        }

        if (_movieCard is { } card)
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel,
                new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                new Color(0, 0, 0, 235));
            float cardY = GraphicsDevice.Viewport.Height / 2f - card.Count * _fontRenderer.LineHeight;
            foreach (string line in card)
            {
                _fontRenderer.Draw(_spriteBatch, line,
                    new Vector2(GraphicsDevice.Viewport.Width / 2f - _fontRenderer.MeasureWidth(line) / 2f, cardY),
                    line == card[0] ? new Color(252, 252, 84) : new Color(0, 252, 0));
                cardY += _fontRenderer.LineHeight * 1.5f;
            }
        }

        if (_menu != MenuState.None)
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel,
                new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                new Color(0, 0, 0, 200));
            var center = new Vector2(GraphicsDevice.Viewport.Width / 2f, GraphicsDevice.Viewport.Height / 2f);
            var gold = new Color(252, 252, 84);
            var menuGreen = new Color(0, 252, 0);
            var gray = new Color(140, 140, 140);

            const string title = "H E X W A S T E";
            _fontRenderer.Draw(_spriteBatch, title,
                new Vector2(center.X - _fontRenderer.MeasureWidth(title) / 2f, center.Y - 120), gold);
            const string subtitle = "a Fallout 2 engine slice - needs your own game data";
            _fontRenderer.Draw(_spriteBatch, subtitle,
                new Vector2(center.X - _fontRenderer.MeasureWidth(subtitle) / 2f, center.Y - 120 + _fontRenderer.LineHeight * 1.4f), gray);

            if (_menu is MenuState.Title or MenuState.CharacterPick)
            {
                string[] items = _menu == MenuState.Title
                    ? ["New game", "Quit"]
                    : ["Create your own", .. _premadeGcds.Select(g => g.Label)];
                float itemY = center.Y - 20;
                for (int i = 0; i < items.Length; i++)
                {
                    string line = (i == _menuIndex ? "> " : "  ") + items[i];
                    _fontRenderer.Draw(_spriteBatch, line,
                        new Vector2(center.X - _fontRenderer.MeasureWidth(line) / 2f, itemY),
                        i == _menuIndex ? menuGreen : gray);
                    itemY += _fontRenderer.LineHeight * 1.6f;
                }
                string hint = _menu == MenuState.Title
                    ? "arrows + Enter; Esc quits"
                    : "create or pick a character - arrows + Enter; Esc back";
                _fontRenderer.Draw(_spriteBatch, hint,
                    new Vector2(center.X - _fontRenderer.MeasureWidth(hint) / 2f, itemY + _fontRenderer.LineHeight), gray);
            }
            else
            {
                DrawCreationScreen(center, gold, menuGreen, gray);
            }
        }

        // The log lives in the bar's green monitor (P11 M3); only fall back to the
        // bottom-left when the bar is hidden (no iface art / worldmap open).
        if (_hudBarHeight == 0)
        {
            // The bar-hidden fallback keeps the old recent-5 view (the scrollable history lives in the bar monitor).
            List<string> recent = _messageLog.Count > MessageLogFallbackLines
                ? _messageLog.GetRange(_messageLog.Count - MessageLogFallbackLines, MessageLogFallbackLines)
                : _messageLog;
            int y = GraphicsDevice.Viewport.Height - 8 - recent.Count * _fontRenderer.LineHeight;
            foreach (string message in recent)
            {
                _fontRenderer.Draw(_spriteBatch, message, new Vector2(8, y), green);
                y += _fontRenderer.LineHeight;
            }
        }
    }
}
