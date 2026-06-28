using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

// P83 "Game Shell": the authentic Fallout 2 front-door — the main menu (M1), the premade
// selector (M2), the creation editor (M3), and the credits/ending bookend (M4). Each screen
// renders the real engine FRM art with a plain-text fallback when the art is absent (the proven
// INVBOX/LSGAME text-then-art pattern), so the headless harness and a partial extraction still run.
public sealed partial class ViewerGame
{
    // ---- M1: the main-menu art (mainmenu.frm + the six menuup/menudown buttons) ----
    private Texture2D? _mainMenuBg, _menuBtnUp, _menuBtnDn;
    private bool _menuArtTried;
    private int _menuHover = -1; // mouse-hovered main-menu button (0..5), -1 = none

    // misc.msg (the button labels {9..14} + copyright {20}); lazy, like the editor/stat msg files.
    private Formats.Text.MessageFile? _miscMsg;
    private bool _miscMsgTried;
    private string MiscMsg(int id) =>
        id < 0 ? "" : LazyMsg(@"text\english\game\misc.msg", ref _miscMsgTried, ref _miscMsg)?.GetText(id) ?? "";

    // The six main-menu buttons in mainmenu.cc enum order (INTRO/NEW GAME/LOAD GAME/OPTIONS/CREDITS/EXIT).
    // Hotkeys i/n/l/o/c/e (mainmenu.cc:55-62). Mapped to Hexwaste's reality: INTRO + OPTIONS are disabled —
    // there is no intro .mve movie and no preferences screen (both documented divergences); LOAD GAME opens
    // the 10-slot picker; CREDITS scrolls credits.txt (M4); NEW GAME enters the character flow.
    private static readonly (int MsgId, char Hotkey, bool Enabled)[] MainMenuButtons =
    [
        (9, 'i', false),  // INTRO
        (10, 'n', true),  // NEW GAME
        (11, 'l', true),  // LOAD GAME
        (12, 'o', false), // OPTIONS
        (13, 'c', true),  // CREDITS
        (14, 'e', true),  // EXIT
    ];

    private const string MenuVersionString = "Hexwaste P83";

    /// <summary>The window-centred origin of the 640x480 shell backdrop (the panels' ox/oy convention).</summary>
    private (int ox, int oy) MenuOrigin()
    {
        Viewport vp = GraphicsDevice.Viewport;
        return ((vp.Width - 640) / 2, (vp.Height - 480) / 2);
    }

    // The 26x26 button at window-local x=30, y=19+index*41 (mainmenu.cc:180-200, "19 + index*42 - index").
    private static Rectangle MenuButtonRect(int ox, int oy, int i) => new(ox + 30, oy + 19 + i * 41, 26, 26);

    // The click band spans the button AND its label text (window-local), so the label is clickable too.
    private static Rectangle MenuButtonBandLocal(int i) => new(28, 17 + i * 41, 180, 30);

    private static int MenuButtonAtLocal(int lx, int ly)
    {
        for (int i = 0; i < MainMenuButtons.Length; i++)
            if (MenuButtonBandLocal(i).Contains(lx, ly))
                return i;
        return -1;
    }

    private void EnsureMenuArt()
    {
        if (_menuArtTried)
            return;
        _menuArtTried = true;
        _mainMenuBg = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\mainmenu.frm");
        _menuBtnUp = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\MENUUP.FRM");
        _menuBtnDn = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\MENUDOWN.FRM");
    }

    /// <summary>Draw the authentic FO2 main menu: mainmenu.frm (FID 140, 640x480) centred in a black
    /// letterbox, the six red-glow menuup/menudown buttons (FID 299/300, 26x26) at the engine rects, and
    /// the misc.msg labels + copyright/version. Returns false when the art is absent (headless / no game
    /// data) so the caller falls back to the plain-text title. ported from fallout2-ce src/mainmenu.cc.</summary>
    private bool DrawAuthenticMainMenu()
    {
        EnsureMenuArt();
        if (_mainMenuBg is null || _fontRenderer is null)
            return false;

        Viewport vp = GraphicsDevice.Viewport;
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
        (int ox, int oy) = MenuOrigin();
        _spriteBatch.Draw(_mainMenuBg, new Rectangle(ox, oy, 640, 480), Color.White);

        bool mouseDown = Mouse.GetState().LeftButton == ButtonState.Pressed;
        var red = new Color(165, 0, 0);
        var redLit = new Color(252, 0, 0);
        var dim = new Color(96, 12, 12);
        var tan = new Color(180, 156, 96);

        for (int i = 0; i < MainMenuButtons.Length; i++)
        {
            Rectangle r = MenuButtonRect(ox, oy, i);
            bool enabled = MainMenuButtons[i].Enabled;
            bool highlit = _menuHover == i || _menuIndex == i;
            bool pressed = _menuHover == i && mouseDown && enabled;
            Texture2D? btn = pressed ? _menuBtnDn : _menuBtnUp;
            if (btn is not null)
                _spriteBatch.Draw(btn, new Vector2(r.X, r.Y), Color.White);

            string label = MiscMsg(MainMenuButtons[i].MsgId);
            Color c = !enabled ? dim : highlit ? redLit : red;
            // Vertically centre the label on the button (the engine's font 104 is taller than ours, so we
            // centre rather than pin to its baked y=41*i+20 — a small presentation divergence).
            float ly = r.Y + (26 - _fontRenderer.LineHeight) / 2f;
            _fontRenderer.Draw(_spriteBatch, label,
                new Vector2(ox + 126 - _fontRenderer.MeasureWidth(label) / 2f, ly), c);
        }

        // Copyright (misc.msg {20}) bottom-left + version bottom-right (mainmenu.cc:141-155).
        _fontRenderer.Draw(_spriteBatch, MiscMsg(20), new Vector2(ox + 15, oy + 459), tan);
        _fontRenderer.Draw(_spriteBatch, MenuVersionString,
            new Vector2(ox + 615 - _fontRenderer.MeasureWidth(MenuVersionString), oy + 459), tan);
        return true;
    }

    /// <summary>Main-menu mouse: hover-highlight the six buttons + dispatch a click. Only the Title state
    /// has art-backed mouse nav in M1; the other shell states keep the keyboard text path until M2/M3.</summary>
    private void HandleMenuMouse(MouseState mouse)
    {
        if (_menu != MenuState.Title || _mainMenuBg is null)
        {
            _menuHover = -1;
            return;
        }
        (int ox, int oy) = MenuOrigin();
        _menuHover = MenuButtonAtLocal(mouse.X - ox, mouse.Y - oy);
        bool click = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        if (click && _menuHover >= 0)
            ActivateMainMenuButton(_menuHover);
    }

    /// <summary>Dispatch a main-menu button (shared by keyboard + mouse). INTRO/OPTIONS log "not available".</summary>
    private void ActivateMainMenuButton(int i)
    {
        if (i < 0 || i >= MainMenuButtons.Length)
            return;
        _audio?.PlaySfx("nmselec0"); // mainmenu.cc:322 click sfx
        switch (i)
        {
            case 1: _menu = MenuState.CharacterPick; _menuIndex = 0; _premadeSel = 0; break; // NEW GAME
            case 2: OpenSaveLoad(SaveLoadMode.Load); break;                 // LOAD GAME → the 10-slot picker
            case 4: _menu = MenuState.Credits; _creditsScroll = 0; break; // CREDITS
            case 5: Exit(); break;                                          // EXIT
            default: Console.WriteLine($"menu: \"{MiscMsg(MainMenuButtons[i].MsgId)}\" is not available in this slice"); break;
        }
    }

    // ---- M2: the premade selector (pickchar.frm FID 174 + per-premade portrait + .bio) ----
    private Texture2D? _pickCharBg, _lilUp, _lilDn;
    private bool _selectorArtTried;
    private int _premadeSel; // the highlighted premade in the art selector (index into _premadeGcds)
    private readonly Dictionary<string, Texture2D?> _portraitCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _bioCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Formats.Combat.GcdFile> _gcdCache = new(StringComparer.OrdinalIgnoreCase);

    // The pickchar.frm bottom-plate buttons. The LABELS (TAKE / MODIFY / CREATE CHARACTER / BACK) are baked
    // into the backdrop; we overlay the lilredup/lilreddn (FID 8/9, 15x16) button face and define a click
    // band spanning the button + its label. prev/next are the baked ◄─► arrow widget (click-band only).
    // Window-local rects (character_selector.cc layout, eyeballed off the dumped 640x480 backdrop).
    private static readonly (string Id, Rectangle Band, Point Btn)[] SelectorButtons =
    [
        ("take",   new Rectangle(70, 322, 150, 24), new Point(76, 326)),
        ("modify", new Rectangle(432, 322, 150, 24), new Point(438, 326)),
        ("prev",   new Rectangle(286, 320, 24, 22), new Point(-1, -1)),
        ("next",   new Rectangle(320, 320, 24, 22), new Point(-1, -1)),
        ("create", new Rectangle(70, 416, 300, 24), new Point(76, 420)),
        ("back",   new Rectangle(432, 416, 150, 24), new Point(438, 420)),
    ];

    private void EnsureSelectorArt()
    {
        if (_selectorArtTried)
            return;
        _selectorArtTried = true;
        _pickCharBg = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\pickchar.frm");
        _lilUp = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\lilredup.frm");
        _lilDn = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\lilreddn.frm");
    }

    // DAT paths use '\' separators; Path.GetFileNameWithoutExtension won't split on '\' on Linux, so do it
    // by hand ("premade\combat.gcd" -> "combat").
    private static string PremadeBase(string virtualPath)
    {
        string file = virtualPath.Replace('/', '\\');
        int slash = file.LastIndexOf('\\');
        if (slash >= 0) file = file[(slash + 1)..];
        int dot = file.LastIndexOf('.');
        return dot >= 0 ? file[..dot] : file;
    }

    private Texture2D? PremadePortrait(string virtualPath)
    {
        string key = PremadeBase(virtualPath);
        if (!_portraitCache.TryGetValue(key, out Texture2D? tex))
            _portraitCache[key] = tex = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, $@"art\intrface\{key}.frm");
        return tex;
    }

    private Formats.Combat.GcdFile? PremadeGcd(string virtualPath)
    {
        string key = PremadeBase(virtualPath);
        if (!_gcdCache.TryGetValue(key, out Formats.Combat.GcdFile? gcd))
        {
            if (!_vfs.Exists(virtualPath))
                return null;
            using Stream s = _vfs.OpenRead(virtualPath);
            _gcdCache[key] = gcd = Formats.Combat.GcdFile.Load(s);
        }
        return gcd;
    }

    private string PremadeBio(string virtualPath)
    {
        string key = PremadeBase(virtualPath);
        if (!_bioCache.TryGetValue(key, out string? bio))
        {
            string path = $@"premade\{key}.bio";
            bio = _vfs.Exists(path)
                ? System.Text.Encoding.ASCII.GetString(_vfs.ReadAllBytes(path)).Replace("\r", "").Trim()
                : "";
            _bioCache[key] = bio;
        }
        return bio;
    }

    /// <summary>Draw the authentic premade selector: pickchar.frm (FID 174) with the highlighted premade's
    /// portrait FRM filling the display panel and its SPECIAL / tagged-skills / .bio overlaid on the dark
    /// half, plus the TAKE/MODIFY/CREATE/BACK buttons + ◄─► cycle arrows. Returns false (→ text fallback)
    /// when the art is absent. ported from fallout2-ce src/character_selector.cc.</summary>
    private bool DrawAuthenticSelector()
    {
        EnsureSelectorArt();
        if (_pickCharBg is null || _fontRenderer is null || _premadeGcds.Count == 0)
            return false;

        Viewport vp = GraphicsDevice.Viewport;
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
        (int ox, int oy) = MenuOrigin();
        _spriteBatch.Draw(_pickCharBg, new Rectangle(ox, oy, 640, 480), Color.White);

        _premadeSel = Math.Clamp(_premadeSel, 0, _premadeGcds.Count - 1);
        (string label, string path) = _premadeGcds[_premadeSel];

        // The portrait carries its own rounded panel frame → blit it into the display window.
        Texture2D? portrait = PremadePortrait(path);
        if (portrait is not null)
            _spriteBatch.Draw(portrait, new Vector2(ox + 24, oy + 20), Color.White);

        var gold = new Color(252, 252, 84);
        var green = new Color(0, 252, 0);
        void T(int x, int y, string s, Color c) => _fontRenderer!.Draw(_spriteBatch, s, new Vector2(ox + x, oy + y), c);

        Formats.Combat.GcdFile? gcd = PremadeGcd(path);
        if (gcd is not null)
        {
            int[] bs = gcd.Stats.BaseStats;
            string name = string.IsNullOrWhiteSpace(gcd.Name) || gcd.Name == "None"
                ? char.ToUpper(PremadeBase(path)[0]) + PremadeBase(path)[1..] : gcd.Name;
            T(306, 34, name, gold);
            string[] sp = ["ST", "PE", "EN", "CH", "IN", "AG", "LK"];
            for (int i = 0; i < 7; i++)
            {
                int col = i < 4 ? 0 : 1, row = i < 4 ? i : i - 4;
                T(306 + col * 92, 56 + row * 15, $"{sp[i]} {bs[i]:D2}", green);
            }
            T(398, 101, $"HP {bs[7]}", green);
            string tags = string.Join(", ", gcd.TaggedSkills.Where(t => t >= 0)
                .Select(t => Formats.Combat.SkillSet.Names[t]));
            if (tags.Length > 0)
                T(306, 128, "Tagged: " + tags, green);
        }

        // The .bio backstory, wrapped across the lower dark area.
        foreach ((string line, int li) in WrapText(PremadeBio(path), 44).Select((l, n) => (l, n)).Take(6))
            T(306, 150 + li * (_fontRenderer.LineHeight + 1), line, green);

        // The four lil-red buttons over their baked labels (down-art while pressed).
        bool mouseDown = Mouse.GetState().LeftButton == ButtonState.Pressed;
        foreach ((string id, Rectangle band, Point btn) in SelectorButtons)
        {
            if (btn.X < 0)
                continue; // prev/next use the baked arrow art
            Texture2D? face = _selectorHover == id && mouseDown ? _lilDn : _lilUp;
            if (face is not null)
                _spriteBatch.Draw(face, new Vector2(ox + btn.X, oy + btn.Y), Color.White);
        }
        // Highlight the hovered label by brightening a thin underline band (the labels are baked, so we
        // can't recolor them — a hover bar is the readable cue).
        if (_selectorHover is { } hov)
        {
            Rectangle b = Array.Find(SelectorButtons, s => s.Id == hov).Band;
            _spriteBatch.Draw(_panelPixel, new Rectangle(ox + b.X, oy + b.Y + b.Height - 2, b.Width, 2),
                new Color(252, 252, 84, 160));
        }
        return true;
    }

    private string? _selectorHover;

    /// <summary>Selector mouse: hover-highlight + dispatch a button click (TAKE/MODIFY/CREATE/BACK/prev/next).</summary>
    private void HandleSelectorMouse(MouseState mouse)
    {
        if (_menu != MenuState.CharacterPick || _pickCharBg is null)
        {
            _selectorHover = null;
            return;
        }
        (int ox, int oy) = MenuOrigin();
        int lx = mouse.X - ox, ly = mouse.Y - oy;
        _selectorHover = null;
        foreach ((string id, Rectangle band, Point _) in SelectorButtons)
            if (band.Contains(lx, ly)) { _selectorHover = id; break; }
        bool click = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        if (click && _selectorHover is { } id2)
            ActivateSelectorButton(id2);
    }

    private void ActivateSelectorButton(string id)
    {
        _audio?.PlaySfx("nmselec0");
        switch (id)
        {
            case "prev": _premadeSel = (_premadeSel + _premadeGcds.Count - 1) % _premadeGcds.Count; break;
            case "next": _premadeSel = (_premadeSel + 1) % _premadeGcds.Count; break;
            case "take": PickPremade(_premadeSel); break;
            case "create": EnterCreation(); break;
            case "modify": SeedCreationFromGcd(_premadeGcds[_premadeSel].VirtualPath); break;
            case "back": _menu = MenuState.Title; _menuIndex = 0; break;
        }
    }

    /// <summary>MODIFY: load a premade's SPECIAL / tags / traits into the creation editor for tweaking
    /// (character_selector.cc "Modify" path). Falls back to a fresh editor if the gcd can't be read.</summary>
    private void SeedCreationFromGcd(string virtualPath)
    {
        Formats.Combat.GcdFile? gcd = PremadeGcd(virtualPath);
        if (gcd is null) { EnterCreation(); return; }
        EnterCreation();
        int[] bs = gcd.Stats.BaseStats;
        for (int i = 0; i < 7; i++)
            _createSpecial[i] = Math.Clamp(bs[i], 1, 10);
        _createPoints = 0; // a premade has its points already spent
        _createGender = bs.Length > 34 ? Math.Clamp(bs[34], 0, 1) : 0;
        _createTags.Clear();
        foreach (int t in gcd.TaggedSkills.Where(t => t >= 0).Take(3)) // the engine's NUM_TAGGED_SKILLS cap
            _createTags.Add(t);
        _createTraits.Clear();
        foreach (int t in gcd.Traits.Where(t => t >= 0).Take(2)) // up to two optional traits
            _createTraits.Add(t);
    }

    // ---- M3: the creation editor (edtrcrte.frm FID 169 + bignum digits; the +/- steppers are baked in) ----
    private Texture2D? _createBg;
    private bool _createArtTried;

    // SPECIAL stat stepper layout (window-local; the bignum boxes line up with the edtrcrte.frm value
    // recesses at x=58, the same column the in-game EDTREDT sheet uses; the +/- buttons are baked into
    // the backdrop, so we only need their click bands at x≈111).
    private const int CreateBigNumX = 58, CreateStepX = 111;
    // The right-hand skills list (tag picker) + the bottom traits columns + the description card.
    private const int CreateSkillX = 384, CreateSkillValX = 573, CreateSkillY = 27;
    private const int CreateTraitLX = 24, CreateTraitRX = 161, CreateTraitY = 332;

    private void EnsureCreationArt()
    {
        if (_createArtTried)
            return;
        _createArtTried = true;
        _createBg = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\EDTRCRTE.frm");
        _bigNum ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\BIGNUM.FRM");
    }

    // The engine's bignum.frm digit-pair blit (character_editor.cc characterEditorDrawBigNumber): 14x24 cells,
    // white digits in [0..167], red (a value > 10) in [168..]; tens then ones, no leading-zero suppression.
    private void DrawBigNum(int ox, int oy, int sx, int sy, int value)
    {
        if (_bigNum is null)
        {
            _fontRenderer?.Draw(_spriteBatch, value.ToString("D2"), new Vector2(ox + sx, oy + sy + 6), new Color(0, 252, 0));
            return;
        }
        int v = Math.Clamp(value, 0, 99), off = value > 10 ? 168 : 0;
        _spriteBatch.Draw(_bigNum, new Vector2(ox + sx, oy + sy), new Rectangle(off + v / 10 * 14, 0, 14, 24), Color.White);
        _spriteBatch.Draw(_bigNum, new Vector2(ox + sx + 14, oy + sy), new Rectangle(off + v % 10 * 14, 0, 14, 24), Color.White);
    }

    // Creation buttons (window-local click bands); the labels DONE/CANCEL are drawn by us on the bottom plate.
    private static readonly (string Id, Rectangle Band)[] CreateButtons =
    [
        ("done", new Rectangle(440, 450, 90, 26)),
        ("cancel", new Rectangle(540, 450, 90, 26)),
    ];

    /// <summary>Draw the authentic FO2 creation editor: edtrcrte.frm (FID 169) with the SPECIAL point-buy
    /// (bignum digits + uparwon/dnarwon steppers + char-points counter), a live derived-stat readout, the
    /// 18-skill tag picker, the optional-trait columns, a description card, and Done/Cancel. All three create
    /// sub-states (stats/traits/tags) render this one screen; the active sub-state drives the highlight.
    /// Returns false (→ text fallback) when the art is absent. ported from fallout2-ce src/character_editor.cc.</summary>
    private bool DrawAuthenticCreation()
    {
        EnsureCreationArt();
        if (_createBg is null || _fontRenderer is null)
            return false;

        Viewport vp = GraphicsDevice.Viewport;
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
        (int ox, int oy) = MenuOrigin();
        _spriteBatch.Draw(_createBg, new Rectangle(ox, oy, 640, 480), Color.White);

        var gold = new Color(252, 252, 84);
        var green = new Color(0, 252, 0);
        var gray = new Color(140, 140, 140);
        void T(int x, int y, string s, Color c) => _fontRenderer!.Draw(_spriteBatch, s, new Vector2(ox + x, oy + y), c);

        // SPECIAL: bignum value + the up/down steppers per stat (labels ST/PE/... are baked into the backdrop).
        for (int i = 0; i < 7; i++)
        {
            int sy = CharStatY[i];
            DrawBigNum(ox, oy, CreateBigNumX, sy, _createSpecial[i]);
            // The +/- steppers are baked into edtrcrte.frm (we only need their click bands), so no arrow overlay.
            if (_menu == MenuState.CreateStats && _createCursor == i) // highlight the active stat
                _spriteBatch.Draw(_panelPixel, new Rectangle(ox + CreateBigNumX - 2, oy + sy - 1, 30, 26), new Color(252, 252, 84, 50));
        }
        // Character points remaining (bignum, below SPECIAL).
        DrawBigNum(ox, oy, 126, 273, _createPoints);

        // Live derived readout (the middle-top info panel).
        int st = _createSpecial[0], pe = _createSpecial[1], en = _createSpecial[2], ag = _createSpecial[5], lk = _createSpecial[6];
        (string, string)[] derived =
        [
            ("Hit Points", $"{15 + st + 2 * en}"), ("Action Pts", $"{5 + ag / 2}"), ("Armor Class", $"{ag}"),
            ("Melee Dmg", $"{Math.Max(st - 5, 1)}"), ("Sequence", $"{2 * pe}"), ("Heal Rate", $"{Math.Max(en / 3, 1)}"),
            ("Critical %", $"{lk}"),
        ];
        for (int i = 0; i < derived.Length; i++)
        {
            T(195, 36 + i * 15, derived[i].Item1, green);
            T(285, 36 + i * 15, derived[i].Item2, green);
        }

        // The 18-skill tag picker (right panel): tagged skills gold, the active cursor arrowed.
        for (int i = 0; i < Formats.Combat.SkillSet.SkillCount; i++)
        {
            bool tagged = _createTags.Contains(i);
            bool sel = _menu == MenuState.CreateTags && _skillAllocIndex == i;
            Color c = sel ? green : tagged ? gold : new Color(0, 200, 0);
            int sy = CreateSkillY + i * (_fontRenderer.LineHeight + 1);
            T(CreateSkillX, sy, (sel ? ">" : tagged ? "*" : " ") + Formats.Combat.SkillSet.Names[i], c);
        }
        DrawBigNum(ox, oy, 522, 228, Math.Max(0, 3 - _createTags.Count)); // tags-remaining counter

        // Optional traits (two columns on the bottom-left plate).
        for (int i = 0; i < TraitCount; i++)
        {
            bool picked = _createTraits.Contains(i);
            bool sel = _menu == MenuState.CreateTraits && _createTraitIndex == i;
            int col = i < 8 ? CreateTraitLX : CreateTraitRX, row = i < 8 ? i : i - 8;
            T(col, CreateTraitY + row * (_fontRenderer.LineHeight + 1),
                (sel ? ">" : picked ? "*" : " ") + TraitName(i), sel ? green : picked ? gold : gray);
        }

        // The description card (bottom-right tan area): the active section's selected item.
        (string title, string body) = CreationCardText();
        T(355, 276, title, gold);
        foreach ((string line, int li) in WrapText(body, 40).Select((l, n) => (l, n)).Take(11))
            T(355, 292 + li * (_fontRenderer.LineHeight + 1), line, green);

        // Done / Cancel (the bottom plate); Done is gated on 0 points + 3 tags.
        bool ready = _createPoints == 0 && _createTags.Count == 3;
        T(452, 456, "DONE", ready ? gold : gray);
        T(556, 456, "CANCEL", gold);
        return true;
    }

    /// <summary>The description-card text for the active creation sub-state's cursor (stat / trait / skill).</summary>
    private (string title, string body) CreationCardText() => _menu switch
    {
        // stat.msg / skill.msg: name at 100+i, description at 200+i (character_editor.cc card).
        MenuState.CreateTraits => (TraitName(_createTraitIndex), TraitDesc(_createTraitIndex)),
        MenuState.CreateTags => (Formats.Combat.SkillSet.Names[_skillAllocIndex], SkillMsg(200 + _skillAllocIndex)),
        _ => _createCursor < 7
            ? (StatMsg(100 + _createCursor), StatMsg(200 + _createCursor))
            : ("Gender", "Choose the character's gender."),
    };

    /// <summary>Creation mouse: the stat steppers, the skill/trait rows, and Done/Cancel.</summary>
    private void HandleCreationMouse(MouseState mouse)
    {
        if (_menu is not (MenuState.CreateStats or MenuState.CreateTraits or MenuState.CreateTags) || _createBg is null)
            return;
        (int ox, int oy) = MenuOrigin();
        int lx = mouse.X - ox, ly = mouse.Y - oy;
        bool click = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        if (!click)
            return;

        // SPECIAL steppers (+/-) and stat selection.
        for (int i = 0; i < 7; i++)
        {
            int sy = CharStatY[i];
            if (new Rectangle(CreateStepX, sy, 14, 12).Contains(lx, ly)) { _menu = MenuState.CreateStats; _createCursor = i; AdjustCreateStat(i, +1); return; }
            if (new Rectangle(CreateStepX, sy + 12, 14, 12).Contains(lx, ly)) { _menu = MenuState.CreateStats; _createCursor = i; AdjustCreateStat(i, -1); return; }
            if (new Rectangle(CreateBigNumX - 2, sy - 1, 70, 26).Contains(lx, ly)) { _menu = MenuState.CreateStats; _createCursor = i; return; }
        }
        // Skill rows → toggle a tag (and select for the card).
        for (int i = 0; i < Formats.Combat.SkillSet.SkillCount; i++)
        {
            int sy = CreateSkillY + i * (_fontRenderer!.LineHeight + 1);
            if (new Rectangle(CreateSkillX - 4, sy, 200, _fontRenderer.LineHeight + 1).Contains(lx, ly))
            { _menu = MenuState.CreateTags; _skillAllocIndex = i; ToggleCreateTag(i); return; }
        }
        // Trait rows → toggle a trait.
        for (int i = 0; i < TraitCount; i++)
        {
            int col = i < 8 ? CreateTraitLX : CreateTraitRX, row = i < 8 ? i : i - 8;
            int sy = CreateTraitY + row * (_fontRenderer!.LineHeight + 1);
            if (new Rectangle(col - 4, sy, 140, _fontRenderer.LineHeight + 1).Contains(lx, ly))
            { _menu = MenuState.CreateTraits; _createTraitIndex = i; ToggleCreateTrait(i); return; }
        }
        // Done / Cancel. DONE honours the same gate as the label colour + the keyboard path (all points
        // spent + 3 tags) so a click on a greyed DONE can't silently drop unspent SPECIAL points.
        if (CreateButtons[0].Band.Contains(lx, ly))
        {
            _audio?.PlaySfx("nmselec0");
            if (_createPoints == 0 && _createTags.Count == 3)
                FinishCreation();
            else
                Console.WriteLine($"create: spend all points + tag 3 skills first ({_createPoints} pts, {_createTags.Count}/3 tags)");
            return;
        }
        if (CreateButtons[1].Band.Contains(lx, ly)) { _audio?.PlaySfx("nmselec0"); _menu = MenuState.CharacterPick; }
    }

    // ---- M4: the credits scroll (credits.txt) + the death-screen art (death.frm) ----
    private List<(string Text, char Kind)>? _creditsLines;
    private float _creditsScroll;
    private const float CreditsPxPerSec = 32f; // credits.cc scrolls bottom-to-top, ~38 ms/line

    private void EnsureCredits()
    {
        if (_creditsLines is not null)
            return;
        _creditsLines = [];
        const string path = @"text\english\CREDITS.TXT";
        if (!_vfs.Exists(path))
            return;
        // credits.cc tags: ';' comment (skip), '#' section header, '@' role/title, plain = a name.
        foreach (string raw in System.Text.Encoding.ASCII.GetString(_vfs.ReadAllBytes(path)).Replace("\r", "").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith(';'))
                continue;
            char kind = line.StartsWith('#') ? '#' : line.StartsWith('@') ? '@' : ' ';
            _creditsLines.Add((kind == ' ' ? line : line[1..], kind));
        }
    }

    /// <summary>Advance + draw the scrolling credits over black (credits.txt). ported from
    /// fallout2-ce src/credits.cc creditsOpen — '#' section headers gold, '@' roles tan, names green.</summary>
    private void DrawCredits()
    {
        EnsureCredits();
        if (_fontRenderer is null || _creditsLines is null)
            return;
        Viewport vp = GraphicsDevice.Viewport;
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);

        var section = new Color(252, 252, 84);
        var role = new Color(180, 156, 96);
        var name = new Color(0, 252, 0);
        int lh = _fontRenderer.LineHeight + 5;
        float y = vp.Height - _creditsScroll;
        foreach ((string text, char kind) in _creditsLines)
        {
            if (text.Length > 0 && y > -lh && y < vp.Height)
            {
                Color c = kind == '#' ? section : kind == '@' ? role : name;
                _fontRenderer.Draw(_spriteBatch, text,
                    new Vector2(vp.Width / 2f - _fontRenderer.MeasureWidth(text) / 2f, y), c);
            }
            y += lh;
        }
        if (y < 0) // fully scrolled past the top → loop
            _creditsScroll = 0;
    }

    /// <summary>Advance the credits scroll + handle the exit key/click (called from the menu Update block).</summary>
    private void UpdateCredits(double elapsedMs, KeyboardState k, MouseState mouse)
    {
        _creditsScroll += (float)(elapsedMs / 1000.0 * CreditsPxPerSec);
        bool click = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        if (click || IsKeyPressed(k, Keys.Escape) || IsKeyPressed(k, Keys.Enter) || IsKeyPressed(k, Keys.Space))
        {
            _menu = MenuState.Title;
            _menuIndex = 0;
            _creditsScroll = 0;
        }
    }

    // The death screen (showDeath) — death.frm FID 310, drawn behind the game-over options.
    private Texture2D? _deathBg;
    private bool _deathArtTried;
    private bool _debugDeathScreen; // --menu death: force the game-over screen for a screenshot

    /// <summary>Load a full-screen cutscene FRM with its OWN sibling palette (death.frm → death.pal). The
    /// death + ending slides do NOT use color.pal — rendering them with the game palette garbles them
    /// (endgame.cc endgameEndingLoadPalette loads art\intrface\&lt;name&gt;.pal per scene).</summary>
    private Texture2D? LoadFrmWithSiblingPalette(string frmPath)
    {
        string palPath = frmPath[..frmPath.LastIndexOf('.')] + ".pal";
        Formats.Pal.Palette pal = _vfs.Exists(palPath)
            ? Formats.Pal.Palette.Load(_vfs.ReadAllBytes(palPath))
            : _palette;
        return InterfaceBar.LoadFrm(GraphicsDevice, _vfs, pal, frmPath);
    }

    private bool DrawDeathArt()
    {
        if (!_deathArtTried)
        {
            _deathArtTried = true;
            _deathBg = LoadFrmWithSiblingPalette(@"art\intrface\death.frm");
        }
        if (_deathBg is null)
            return false;
        Viewport vp = GraphicsDevice.Viewport;
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);
        (int ox, int oy) = MenuOrigin();
        _spriteBatch.Draw(_deathBg, new Rectangle(ox, oy, 640, 480), Color.White);
        return true;
    }

    /// <summary>Greedy word-wrap to a column width (in characters), for the .bio / credits text.</summary>
    private static List<string> WrapText(string text, int width)
    {
        var lines = new List<string>();
        foreach (string para in text.Split('\n'))
        {
            var cur = new System.Text.StringBuilder();
            foreach (string word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (cur.Length > 0 && cur.Length + 1 + word.Length > width)
                {
                    lines.Add(cur.ToString());
                    cur.Clear();
                }
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(word);
            }
            lines.Add(cur.ToString());
        }
        return lines;
    }
}
