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

// The modal UI panels drawn over the world: the character sheet + skill allocator, the perk picker,
// the Skilldex, the Pip-Boy + automap, the options menu, the load/save slot picker, the called-shot
// aim dialog, and the item panels (inventory/loot/barter/trade) with drag-to-equip. Pure move from
// ViewerGame.cs (Draw() dispatches into these; fields/state stay central).
public sealed partial class ViewerGame
{
    /// <summary>The character sheet (C / K): SPECIAL + derived stats + level
    /// on the left, the 18 skills on the right. Read-only, but a skill can be
    /// raised in place while banked points remain (Right/Enter).</summary>
    private void DrawSkillAllocator()
    {
        if (!_skillAllocOpen || _fontRenderer is null || _dudeGcd is null)
            return;

        _panelPixel ??= CreatePixel();
        int lh = Math.Max(_fontRenderer.LineHeight, 22);
        int x = 48, y = 28, w = 660;
        int h = (Formats.Combat.SkillSet.SkillCount + 3) * lh + 16;
        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, w, h), new Color(8, 8, 8, 238));

        var gold = new Color(252, 252, 84);
        var green = new Color(0, 252, 0);
        var gray = new Color(150, 150, 150);
        int[] b = _dudeGcd.Stats.BaseStats, bo = _dudeGcd.Stats.BonusStats, sk = _dudeGcd.Stats.Skills;
        int[] tags = _dudeGcd.TaggedSkills;
        int Stat(int i) => b[i] + bo[i];

        // ---- left column: header + SPECIAL + derived ----
        int lx = x + 14, ly = y + 10;
        void Line(string text, Color c) { _fontRenderer.Draw(_spriteBatch, text, new Vector2(lx, ly), c); ly += lh; }
        string name = _dudeGcd is { Name.Length: > 0 } g && g.Name != "None" ? g.Name : "Wanderer";
        Line($"{name}  —  Level {_dudeLevel}", gold);
        int nextXp = Formats.Combat.Progression.XpForLevel(_dudeLevel + 1);
        Line($"XP {_dudeXp}" + (nextXp > 0 ? $" / {nextXp}" : " (max)"), gray);
        Formats.Combat.CritterState? cs = _dude is not null ? GetCritterState(_dude.Dude) : null;
        if (cs is not null)
        {
            Line($"HP {_dude!.Dude.CurrentHp}/{cs.MaxHp}   AP {cs.MaxActionPoints}", green);
            ly += 4;
            string[] sp = ["ST", "PE", "EN", "CH", "IN", "AG", "LK"];
            for (int i = 0; i < 7; i++)
                _fontRenderer.Draw(_spriteBatch, $"{sp[i]} {cs.Stat(i)}", // effective (trait/perk-modified), like the derived stats
                    new Vector2(lx + (i % 2) * 130, ly + i / 2 * lh), gold);
            ly += 4 * lh + 6;
            Line($"Armor Class {cs.ArmorClass}", gray);
            Line($"Melee Damage {cs.MeleeDamage}", gray);
            Line($"Sequence {cs.Sequence}", gray);
            Line($"Critical % {Stat(Formats.Combat.CritterStat.CriticalChance)}", gray);
            Line($"Healing Rate {Math.Max(Stat(Formats.Combat.CritterStat.Endurance) / 3, 1)}", gray);
        }
        ly += 6;
        // Traits + perks (P28-M4): the character-progression payoff.
        string traitStr = string.Join(", ", _dudeGcd.Traits.Where(t => t >= 0).Select(TraitName));
        Line($"Traits: {(traitStr.Length > 0 ? traitStr : "none")}", gray);
        var takenPerks = Enumerable.Range(0, _dudePerkRanks.Length).Where(i => _dudePerkRanks[i] > 0)
            .Select(i => _dudePerkRanks[i] > 1 ? $"{PerkName(i)} ({_dudePerkRanks[i]})" : PerkName(i)).ToList();
        Line($"Perks: {(takenPerks.Count > 0 ? string.Join(", ", takenPerks) : "none")}", gray);
        if (AvailablePerkPicks() > 0)
            Line($"{AvailablePerkPicks()} perk(s) available — press G", green);
        ly += 6;
        // Karma / reputation (P31 B-M3): the karma number + generic-reputation title + any earned
        // karma titles + non-Neutral slice-town standings. Display-only (never transcript-diffed).
        foreach (string kl in KarmaDisplayLines())
            Line(kl, gray);
        // Kills by type (P38; character_editor.cc:2202 "::: Kills :::") — char sheet only.
        List<string> killLines = KillDisplayLines();
        if (killLines.Count > 0)
        {
            ly += 6;
            Line("Kills:", gray);
            foreach (string kl in killLines)
                Line($"  {kl}", gray);
        }
        ly += 6;
        if (_unspentSkillPoints > 0)
            Line($"{_unspentSkillPoints} skill points — raise →", green);
        _fontRenderer.Draw(_spriteBatch, "C / K / G perk / Esc close", new Vector2(lx, y + h - lh - 8), gray);

        // ---- right column: the 18 skills ----
        int rx = x + 330;
        int rowY = y + 10;
        for (int i = 0; i < Formats.Combat.SkillSet.SkillCount; i++)
        {
            int value = Formats.Combat.SkillSet.Value(b, bo, sk, tags, i);
            bool tagged = Array.IndexOf(tags, i) >= 0;
            bool selected = i == _skillAllocIndex && _unspentSkillPoints > 0;
            string tag = tagged ? " (T)" : "";
            string cost = selected ? $"  +1={Formats.Combat.SkillSet.Cost(value)}" : "";
            _fontRenderer.Draw(_spriteBatch, $"{(selected ? ">" : " ")} {Formats.Combat.SkillSet.Names[i]}{tag}",
                new Vector2(rx, rowY), selected ? green : (tagged ? gold : gray));
            _fontRenderer.Draw(_spriteBatch, $"{value}%{cost}", new Vector2(rx + 220, rowY),
                selected ? green : gray);
            rowY += lh;
        }
    }

    // --- Perk selection (P28-M4) -----------------------------------------

    private bool _perkPickOpen;
    private const int PerkPickRows = 12; // perks shown in the picker (the slice never offers more)

    private bool DudeHasSkilled() =>
        _dudeGcd is not null && Formats.Combat.TraitModifiers.Has(_dudeGcd.Traits, Formats.Combat.TraitModifiers.Skilled);

    /// <summary>Perk picks earned by the dude's level minus the ones already taken (one per 3
    /// levels, 4 with Skilled; PerkRules cadence).</summary>
    private int AvailablePerkPicks() => _dudeGcd is null
        ? 0
        : Math.Max(0, Formats.Perks.PerkRules.PicksEarned(_dudeLevel, DudeHasSkilled()) - _dudePerkRanks.Sum());

    /// <summary>The perk indices the dude currently qualifies for (PerkRules.CanAdd over the live
    /// stats/skills/globals), in enum order.</summary>
    private List<int> EligiblePerks()
    {
        var list = new List<int>();
        if (_dude is null)
            return list;
        int GetStat(int s) => GetCritterState(_dude.Dude)?.Stat(s) ?? 0;
        int GetSkill(int s) => GetCritterState(_dude.Dude)?.SkillValue(s) ?? 0;
        int GetGlobal(int g) => _scriptHost?.GlobalVars.GetValueOrDefault(g, 0) ?? 0;
        for (int i = 0; i < Formats.Perks.PerkTable.Count; i++)
            if (Formats.Perks.PerkRules.CanAdd(Formats.Perks.PerkTable.Get(i), _dudePerkRanks, _dudeLevel, GetStat, GetSkill, GetGlobal))
                list.Add(i);
        return list;
    }

    /// <summary>Take a rank of <paramref name="perkIndex"/> if it's eligible and a pick is
    /// available (the picker's commit). Closes the picker when no picks remain.</summary>
    private void ChoosePerk(int perkIndex)
    {
        if (AvailablePerkPicks() <= 0 || !EligiblePerks().Contains(perkIndex))
            return;
        _dudePerkRanks[perkIndex]++;
        Log($"You gain a new perk: {PerkName(perkIndex)}.");
        if (AvailablePerkPicks() <= 0)
            _perkPickOpen = false;
    }

    // PERKWIN.FRM layout (character_editor.cc:89-95): window 573x230, perk list at window-local
    // (45,43) 192x129, the perk card name at (280,27) / description at (280,70).
    private const int PerkWinW = 573, PerkWinH = 230;
    private const int PerkWinListX = 45, PerkWinListY = 43, PerkWinListW = 192, PerkWinListH = 129;
    private const int PerkWinCardX = 280;

    /// <summary>Top-left of the centred PERKWIN window + the per-row height (the list area divided so
    /// up to ~11 perks fit). One source the render + hit-test share (the SkilldexRowAt pattern).</summary>
    private Point PerkWindowOrigin(out int rowH, out int rowsShown, int eligCount)
    {
        rowH = Math.Max(_fontRenderer!.LineHeight + 1, 11);
        rowsShown = Math.Min(eligCount, PerkWinListH / rowH);
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        return new Point(Math.Max(0, (vp.Width - PerkWinW) / 2), Math.Max(0, (vp.Height - PerkWinH) / 2));
    }

    /// <summary>The eligible-perk row under (mx,my), or -1 — the list rows at
    /// (origin + 45, 43 + i*rowH), width 192.</summary>
    private int PerkPickerRowAt(int mx, int my)
    {
        if (!_perkPickOpen || _fontRenderer is null || _perkWin is null)
            return -1;
        List<int> elig = EligiblePerks();
        Point o = PerkWindowOrigin(out int rowH, out int rowsShown, elig.Count);
        for (int i = 0; i < rowsShown; i++)
        {
            var r = new Rectangle(o.X + PerkWinListX, o.Y + PerkWinListY + i * rowH, PerkWinListW, rowH);
            if (r.Contains(mx, my))
                return i;
        }
        return -1;
    }

    /// <summary>The level-up perk picker. P29-M5: the authentic PERKWIN.FRM panel (the perk list on
    /// the left, the hovered perk's name + wrapped description card on the right), falling back to the
    /// text flyout when the art is absent (the Skilldex pattern). Click a row (or 1-9) to take it.</summary>
    private void DrawPerkPicker()
    {
        if (!_perkPickOpen || _fontRenderer is null)
            return;
        if (!_perkWinTried) { _perkWinTried = true; _perkWin = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\PERKWIN.frm"); }
        if (_perkWin is null)
        {
            DrawPerkPickerTextFallback();
            return;
        }

        List<int> elig = EligiblePerks();
        Point o = PerkWindowOrigin(out int rowH, out int rowsShown, elig.Count);
        _spriteBatch.Draw(_perkWin, new Vector2(o.X, o.Y), Color.White);

        var green = new Color(0, 252, 0);
        var hi = new Color(252, 252, 84);
        var cardColor = new Color(0, 0, 0); // the card area is parchment — dark text reads on it
        int hovered = PerkPickerRowAt(Mouse.GetState().X, Mouse.GetState().Y);
        for (int i = 0; i < rowsShown; i++)
        {
            int pi = elig[i];
            string rank = _dudePerkRanks[pi] > 0 ? $" ({_dudePerkRanks[pi]}/{Formats.Perks.PerkTable.Get(pi).MaxRank})" : "";
            _fontRenderer.Draw(_spriteBatch, PerkName(pi) + rank,
                new Vector2(o.X + PerkWinListX + 4, o.Y + PerkWinListY + i * rowH), i == hovered ? hi : green);
        }

        // The perk card for the hovered (or first) eligible perk: name + wrapped description.
        int card = hovered >= 0 ? elig[hovered] : (elig.Count > 0 ? elig[0] : -1);
        if (card >= 0)
        {
            _fontRenderer.Draw(_spriteBatch, PerkName(card), new Vector2(o.X + PerkWinCardX, o.Y + 27), cardColor);
            float dy = o.Y + 70;
            foreach (string line in _fontRenderer.WrapText(PerkDescription(card), PerkWinW - PerkWinCardX - 24))
            {
                _fontRenderer.Draw(_spriteBatch, line, new Vector2(o.X + PerkWinCardX, dy), cardColor);
                dy += _fontRenderer.LineHeight;
            }
        }
        if (elig.Count == 0)
            _fontRenderer.Draw(_spriteBatch, "(none qualify)", new Vector2(o.X + PerkWinListX + 4, o.Y + PerkWinListY), green);
    }

    /// <summary>The pre-art text flyout, kept as the fallback when PERKWIN.FRM is absent.</summary>
    private void DrawPerkPickerTextFallback()
    {
        _panelPixel ??= CreatePixel();
        int lh = Math.Max(_fontRenderer!.LineHeight, 22);
        List<int> elig = EligiblePerks();
        int shown = Math.Min(elig.Count, PerkPickRows);
        int x = 360, y = 40, w = 320, h = (shown + 3) * lh + 16;
        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, w, h), new Color(8, 8, 8, 240));
        var green = new Color(0, 252, 0);
        var gray = new Color(150, 150, 150);
        _fontRenderer.Draw(_spriteBatch, $"Pick a perk ({AvailablePerkPicks()} available)", new Vector2(x + 12, y + 10), new Color(252, 252, 84));
        int rowY = y + 10 + lh + 6;
        for (int row = 0; row < shown; row++)
        {
            int pi = elig[row];
            string rank = _dudePerkRanks[pi] > 0 ? $" ({_dudePerkRanks[pi]}/{Formats.Perks.PerkTable.Get(pi).MaxRank})" : "";
            _fontRenderer.Draw(_spriteBatch, $"{row + 1}. {PerkName(pi)}{rank}", new Vector2(x + 12, rowY), green);
            rowY += lh;
        }
        if (elig.Count == 0)
            _fontRenderer.Draw(_spriteBatch, "(none qualify)", new Vector2(x + 12, rowY), gray);
        _fontRenderer.Draw(_spriteBatch, "1-9 pick / Esc close", new Vector2(x + 12, y + h - lh - 8), gray);
    }

    /// <summary>Top-left of the Skilldex box: bottom-right, just above the HUD bar
    /// (skilldex.cc:225-226 — right margin 4, bottom margin 6). btnW/btnH = the SKLDXOFF
    /// button size; row i sits at bar-local (15, 45 + i*36).</summary>
    private Point SkilldexOrigin(out int boxW, out int boxH, out int btnW, out int btnH)
    {
        boxW = _skilldexBox?.Width ?? 185;
        boxH = _skilldexBox?.Height ?? 368;
        btnW = _skilldexBtnOff?.Width ?? 88;
        btnH = _skilldexBtnOff?.Height ?? 33;
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        return new Point(Math.Max(0, vp.Width - boxW - 4), Math.Max(0, vp.Height - _hudBarHeight - boxH - 6));
    }

    /// <summary>The Skilldex row index under (mx,my), or -1 — the 8 buttons at
    /// (origin + 15, 45 + i*36), size btnW×btnH.</summary>
    private int SkilldexRowAt(int mx, int my)
    {
        Point o = SkilldexOrigin(out _, out _, out int btnW, out int btnH);
        for (int i = 0; i < SkilldexSkills.Length; i++)
        {
            var r = new Rectangle(o.X + 15, o.Y + 45 + i * 36, btnW, btnH);
            if (r.Contains(mx, my))
                return i;
        }
        return -1;
    }

    /// <summary>The Skilldex use-skill picker (P12 M0 + P13 art upgrade) — the authentic
    /// SKLDXBOX.FRM panel with SKLDXOFF/SKLDXON buttons, bottom-right above the bar
    /// (skilldex.cc). The skill name is centred on each button and the % is shown to its
    /// right; the hovered row lights with SKLDXON. Click a row (or press 1-8) to arm the
    /// skill. Falls back to a text flyout if the art is missing.</summary>
    private void DrawSkilldex()
    {
        if (!_skilldexOpen || _fontRenderer is null)
            return;

        _skilldexBox ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\SKLDXBOX.frm");
        _skilldexBtnOff ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\SKLDXOFF.frm");
        _skilldexBtnOn ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\SKLDXON.frm");
        if (_skilldexBox is null)
        {
            DrawSkilldexTextFallback();
            return;
        }

        Point o = SkilldexOrigin(out _, out _, out int btnW, out int btnH);
        var titleColor = new Color(252, 252, 84);
        var nameColor = new Color(0, 252, 0);
        var dim = new Color(0, 168, 0);

        _spriteBatch.Draw(_skilldexBox, new Vector2(o.X, o.Y), Color.White);
        _fontRenderer.Draw(_spriteBatch, "SKILLDEX", new Vector2(o.X + 55, o.Y + 14), titleColor);

        MouseState m = Mouse.GetState();
        int hovered = SkilldexRowAt(m.X, m.Y);
        for (int i = 0; i < SkilldexSkills.Length; i++)
        {
            int skill = SkilldexSkills[i];
            var btnPos = new Vector2(o.X + 15, o.Y + 45 + i * 36);
            Texture2D? btn = i == hovered ? _skilldexBtnOn : _skilldexBtnOff;
            if (btn is not null)
                _spriteBatch.Draw(btn, btnPos, Color.White);

            string name = SkillName(skill);
            int nameX = Math.Max(0, (btnW - _fontRenderer.MeasureWidth(name)) / 2);
            int nameY = Math.Max(0, (btnH - _fontRenderer.LineHeight) / 2);
            _fontRenderer.Draw(_spriteBatch, name, new Vector2(btnPos.X + nameX, btnPos.Y + nameY), nameColor);

            // The box bakes placeholder "223 %%" digits in each readout (like iface.frm);
            // field-blank them to the recess colour (32,32,32) and draw the real value
            // right-aligned (skilldex.cc blits BIG_NUMBERS here at x=110).
            _panelPixel ??= CreatePixel();
            int fieldX = o.X + 100, fieldW = 72, fieldY = o.Y + 46 + i * 36;
            _spriteBatch.Draw(_panelPixel, new Rectangle(fieldX, fieldY, fieldW, 26), new Color(32, 32, 32));
            string val = $"{DudeSkillValue(skill)}%";
            _fontRenderer.Draw(_spriteBatch, val,
                new Vector2(fieldX + fieldW - _fontRenderer.MeasureWidth(val) - 4, fieldY + (26 - _fontRenderer.LineHeight) / 2), dim);
        }
    }

    /// <summary>The pre-art text flyout, kept as the fallback when SKLDXBOX is absent.</summary>
    private void DrawSkilldexTextFallback()
    {
        _panelPixel ??= CreatePixel();
        int lh = Math.Max(_fontRenderer!.LineHeight, 18);
        int w = 220, h = (SkilldexSkills.Length + 2) * lh + 12;
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int x = vp.Width - w - 12;
        int y = vp.Height - _hudBarHeight - h - 6;
        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, w, h), new Color(8, 8, 8, 238));

        var gold = new Color(252, 252, 84);
        var green = new Color(0, 252, 0);
        var gray = new Color(150, 150, 150);
        int ty = y + 8;
        _fontRenderer.Draw(_spriteBatch, "SKILLDEX", new Vector2(x + 12, ty), gold); ty += lh;
        for (int i = 0; i < SkilldexSkills.Length; i++)
        {
            int skill = SkilldexSkills[i];
            _fontRenderer.Draw(_spriteBatch, $"{i + 1}. {SkillName(skill)}", new Vector2(x + 12, ty), green);
            _fontRenderer.Draw(_spriteBatch, $"{DudeSkillValue(skill)}%", new Vector2(x + w - 50, ty), gray);
            ty += lh;
        }
        _fontRenderer.Draw(_spriteBatch, "1-8 use, Esc/S close", new Vector2(x + 12, y + h - lh - 4), gray);
    }

    /// <summary>The Pip-Boy panel (P12 M1): the authentic PIP.FRM (640x480) centred,
    /// with the date/time, a character STATUS page, and a REST sub-page (durations).
    /// Automaps / archives / alarm are out of scope (content-gated). Reuses the AAF font
    /// (green) like the HUD monitor; the "date" is our game-day + clock — no full
    /// calendar (a documented simplification, since our GameClock tracks only ticks).</summary>
    // Pip-Boy content origin + line height — shared by DrawPipboy (render) and the
    // PipboyRow* helpers (hit-test) so a row click always lands where it's drawn.
    private void PipboyContentOrigin(out Point po, out int lh)
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int pw = _pipboyBg?.Width ?? 640, ph = _pipboyBg?.Height ?? 480;
        po = new Point(Math.Max(0, (vp.Width - pw) / 2), Math.Max(0, (vp.Height - ph) / 2));
        lh = (_fontRenderer?.LineHeight ?? 16) + 4;
    }

    // The clickable rows the current Pip-Boy page offers, paired with the action each
    // fires — the SINGLE dispatch shared by the row click and (where they overlap) the
    // keyboard. Rest rows call DoRest without closing the menu, matching the number keys.
    private List<(string Label, Action OnClick)> PipboyRows()
    {
        var rows = new List<(string, Action)>();
        if (!_pipboyRestMenu)
        {
            rows.Add(("Rest", () => _pipboyRestMenu = true));
            rows.Add(("Automap", () => { _pipboyOpen = false; _automapOpen = true; }));
            rows.Add(("Close", () => _pipboyOpen = false));
        }
        else
        {
            for (int i = 0; i < RestOptions.Length; i++)
            {
                int min = RestOptions[i].Minutes;
                rows.Add(($"{i + 1}. {RestOptions[i].Label}", () => DoRest(min)));
            }
            rows.Add(("Back", () => _pipboyRestMenu = false));
        }
        return rows;
    }

    // The clickable rows render in a fixed band below the page's info text (reserve 9
    // lines for the status block, 2 for the REST header) so the geometry is computable
    // independent of the variable status content.
    private Rectangle PipboyRowRect(int index)
    {
        PipboyContentOrigin(out Point po, out int lh);
        int baseY = po.Y + 46 + (_pipboyRestMenu ? 2 : 9) * lh + 8;
        return new Rectangle(po.X + 254 - 6, baseY + index * lh - 2, 220, lh);
    }

    private int PipboyRowAt(int mx, int my)
    {
        int n = PipboyRows().Count;
        for (int i = 0; i < n; i++)
            if (PipboyRowRect(i).Contains(mx, my))
                return i;
        return -1;
    }

    private void DrawPipboy()
    {
        if (!_pipboyOpen || _fontRenderer is null)
            return;
        _pipboyBg ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\PIP.frm");

        PipboyContentOrigin(out Point po, out int lh);
        int pw = _pipboyBg?.Width ?? 640, ph = _pipboyBg?.Height ?? 480;
        var green = new Color(0, 252, 0);
        var dim = new Color(0, 160, 0);
        var hot = new Color(252, 252, 84);

        if (_pipboyBg is not null)
            _spriteBatch.Draw(_pipboyBg, new Vector2(po.X, po.Y), Color.White);
        else
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel, new Rectangle(po.X, po.Y, pw, ph), new Color(8, 16, 8, 240));
        }

        // Date/time, top-left (pipboy.cc PIPBOY_WINDOW_DAY/TIME positions 20,17 / 155,17).
        // P20-M3: the real FO2 calendar date (scripts.cc gameTimeGetDate) — not a day count.
        _fontRenderer.Draw(_spriteBatch, _clock.DateString, new Vector2(po.X + 20, po.Y + 17), green);
        _fontRenderer.Draw(_spriteBatch, $"{_clock.Hour / 100:00}:{_clock.Hour % 100:00}",
            new Vector2(po.X + 155, po.Y + 17), green);

        // Content view (pipboy.cc CONTENT_VIEW 254,46): the info block, then the clickable rows.
        int cx = po.X + 254, ty = po.Y + 46;
        void Line(string text, Color c) { _fontRenderer!.Draw(_spriteBatch, text, new Vector2(cx, ty), c); ty += lh; }

        // The embedded mini-map fills the empty left column on the status page (P20-M1).
        if (!_pipboyRestMenu)
            DrawPipboyMiniMap(po.X + 18, po.Y + 46, 210, ph - 92);

        if (!_pipboyRestMenu)
        {
            Line("STATUS", green); ty += 4;
            string name = _dudeGcd is { Name.Length: > 0 } g && g.Name != "None" ? g.Name : "Wanderer";
            Line(name, green);
            Line($"Level {_dudeLevel}   XP {_dudeXp}", dim);
            if (_dude is not null && GetCritterState(_dude.Dude) is { } st)
            {
                Line($"Hit Points {_dude.Dude.CurrentHp}/{st.MaxHp}", dim);
                Line($"Armor Class {st.ArmorClass}", dim);
                Line($"Action Points {st.MaxActionPoints}", dim);
                int carried = DudeCarriedWeight();
                Line($"Carry Weight {carried}/{st.CarryWeight}", // red when over (P24)
                    Formats.Map.InventoryWeight.IsEncumbered(carried, st.CarryWeight) ? new Color(255, 64, 64) : dim);
            }
            ty += 4;
            foreach (string kl in KarmaDisplayLines()) // P31 B-M3
                Line(kl, dim);
        }
        else
        {
            Line("REST", green);
        }

        // The clickable action rows (click or the keyboard shortcut). The hovered row lights.
        int hovered = PipboyRowAt(Mouse.GetState().X, Mouse.GetState().Y);
        var rows = PipboyRows();
        for (int i = 0; i < rows.Count; i++)
        {
            Rectangle r = PipboyRowRect(i);
            _fontRenderer.Draw(_spriteBatch, rows[i].Label, new Vector2(r.X + 6, r.Y + 2), i == hovered ? hot : green);
        }
        _fontRenderer.Draw(_spriteBatch, _pipboyRestMenu ? "click a duration, Esc back" : "click a row, P / Esc close",
            new Vector2(cx, po.Y + ph - 30), dim);
    }

    /// <summary>The automap dot colour for an object by FID type, shared by the full-window
    /// automap and the Pip-Boy mini-map (P20-M1/M2). Dead critters / untyped objects → null.
    /// Walls/scenery match the engine's IN-GAME _colorTable (automap.cc:537/541 — wall
    /// _colorTable[992] = pure green, high-detail scenery [480] = dark green). DOCUMENTED
    /// DIVERGENCE: the engine's in-game map hides critters + items (motion-sensor only) and
    /// paints the dude red; we show them (red/yellow) with a WHITE dude so enemies + loot +
    /// you are all distinguishable — a more useful PoC map.</summary>
    /// <summary>Reveal every current-elevation object within sight of <paramref name="tile"/>
    /// for the automap fog (P20-M2) — accumulated into <see cref="_seenObjects"/> as the dude
    /// explores. Proximity, not true LoS (a documented simplification).</summary>
    private void RevealAround(int tile)
    {
        if (tile < 0)
            return;
        foreach (MapObject obj in _flatObjects[_elevation].Concat(_solidObjects[_elevation]))
            if (obj.HexTile >= 0 && Formats.Hex.HexGrid.Distance(tile, obj.HexTile) <= AutomapSightRadius)
                _seenObjects.Add(obj);
    }

    private static Color? AutomapColor(MapObject obj) => Fid.Type(obj.Fid) switch
    {
        ObjectType.Wall => new Color(0, 248, 0),     // _colorTable[992]
        ObjectType.Scenery => new Color(0, 120, 0),  // _colorTable[480]
        ObjectType.Critter => obj.IsDead ? null : new Color(248, 0, 0),
        ObjectType.Item => new Color(252, 252, 84),
        ObjectType.Misc => new Color(84, 200, 252),
        _ => null,
    };

    /// <summary>The embedded Pip-Boy mini-map (P20-M1): the current elevation's objects
    /// plotted into a small box on the status page (col→x mirrored, row→y, like the full
    /// window scaled). DIVERGENCE: the engine's embedded automap reads the explored-tile
    /// RLE from a GENERATED MAPS\AUTOMAP.DB — which our PoC never writes — so we re-plot the
    /// live objects instead (the same source as the full-window automap). A preview; press
    /// A for the full view.</summary>
    private void DrawPipboyMiniMap(int boxX, int boxY, int boxW, int boxH)
    {
        if (_fontRenderer is null)
            return;
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(boxX, boxY, boxW, boxH), new Color(0, 20, 0, 210));

        void Plot(int tile, Color c, int size)
        {
            if (tile < 0)
                return;
            int mx = boxX + boxW * (199 - tile % 200) / 199; // mirror col like the full window
            int my = boxY + boxH * (tile / 200) / 199;
            if (mx >= boxX && my >= boxY && mx + size <= boxX + boxW && my + size <= boxY + boxH)
                _spriteBatch.Draw(_panelPixel, new Rectangle(mx, my, size, size), c);
        }

        foreach (MapObject obj in _flatObjects[_elevation].Concat(_solidObjects[_elevation]))
            if (_seenObjects.Contains(obj) && AutomapColor(obj) is { } col) // OBJECT_SEEN fog (P20-M2)
                Plot(obj.HexTile, col, 2);
        if (_dude is not null)
            Plot(_dude.Dude.HexTile, new Color(255, 255, 255), 3);

        _fontRenderer.Draw(_spriteBatch, "MAP (A: full)", new Vector2(boxX + 4, boxY + 2), new Color(0, 252, 0));
    }

    /// <summary>The full-window automap (P15 M0): the authentic AUTOMAP.FRM (519x480)
    /// centred, with every object on the current elevation plotted as a colored dot
    /// (automap.cc automapRenderInMapWindow projection: ax = 449 − 2·col, ay = 2·row + 8,
    /// col = tile%200, row = tile/200). Colors by FID type; the dude is a bright marker.
    /// Fog-of-war is faked all-visible (we don't track OBJECT_SEEN) — a documented PoC
    /// simplification; the embedded Pip-Boy mini-map (needs automap.db RLE) stays out.</summary>
    private void DrawAutomap()
    {
        if (!_automapOpen || _fontRenderer is null)
            return;
        _automapBg ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\AUTOMAP.frm");

        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int w = _automapBg?.Width ?? 519, h = _automapBg?.Height ?? 480;
        var o = new Point(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2));
        _panelPixel ??= CreatePixel();
        if (_automapBg is not null)
            _spriteBatch.Draw(_automapBg, new Vector2(o.X, o.Y), Color.White);
        else
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X, o.Y, w, h), new Color(8, 16, 8, 240));

        void Plot(int tile, Color c, int size)
        {
            if (tile < 0)
                return;
            int ax = 449 - 2 * (tile % 200);   // automap.cc:550, decomposed
            int ay = 2 * (tile / 200) + 8;
            if (ax < 0 || ay < 0 || ax + size > w || ay + size > h)
                return;
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X + ax, o.Y + ay, size, size), c);
        }

        foreach (MapObject obj in _flatObjects[_elevation].Concat(_solidObjects[_elevation]))
            if (_seenObjects.Contains(obj) && AutomapColor(obj) is { } col) // OBJECT_SEEN fog (P20-M2)
                Plot(obj.HexTile, col, 2);
        if (_dude is not null)
            Plot(_dude.Dude.HexTile, new Color(255, 255, 255), 3); // the dude marker

        var labelGreen = new Color(0, 252, 0);
        _fontRenderer.Draw(_spriteBatch, $"AUTOMAP — {_currentMapName} (elev {_elevation})", new Vector2(o.X + 20, o.Y + 12), labelGreen);
        _fontRenderer.Draw(_spriteBatch, "Esc / A  close", new Vector2(o.X + 20, o.Y + h - 24), new Color(0, 168, 0));
    }

    /// <summary>The options / pause menu (P12 M2): the authentic OPBASE.FRM (164x217)
    /// centred, with the actions the engine's showOptions offers (minus Preferences,
    /// which we have no system for). Drawn over the paused world.</summary>
    // The options/pause menu rows, top to bottom — index is the dispatch key shared by
    // DrawOptions (render), OptionsRowAt (hit-test) and the click handler.
    private static readonly string[] OptionsItems =
        ["Save Game  (S)", "Load Game  (L)", "Main Menu  (M)", "Quit  (Q)", "Resume  (Esc)"];

    // The clickable rect for the index-th options row — origin + spacing mirror DrawOptions
    // exactly (the FRM-dim fallback keeps it valid before the art loads).
    private Rectangle OptionsRowRect(int index)
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int ow = _optionsBg?.Width ?? 164, oh = _optionsBg?.Height ?? 217;
        int ox = Math.Max(0, (vp.Width - ow) / 2), oy = Math.Max(0, (vp.Height - oh) / 2);
        int lh = (_fontRenderer?.LineHeight ?? 16) + 10;
        int ty0 = oy + (oh - OptionsItems.Length * lh) / 2;
        return new Rectangle(ox, ty0 + index * lh - 2, ow, lh);
    }

    private int OptionsRowAt(int mx, int my)
    {
        for (int i = 0; i < OptionsItems.Length; i++)
            if (OptionsRowRect(i).Contains(mx, my))
                return i;
        return -1;
    }

    /// <summary>The detected-encounter avoid prompt (phase-16 M1): a centred Yes/No
    /// box over the worldmap mirroring the engine's showDialogBox (worldmap.cc:3510).</summary>
    private void DrawEncounterPrompt()
    {
        if (_encounterPrompt is not { } p || _fontRenderer is null)
            return;
        _panelPixel ??= CreatePixel();
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int w = 360, h = 96;
        int x = (vp.Width - w) / 2, y = (vp.Height - h) / 2;
        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, w, h), new Color(8, 16, 8, 240));
        var green = new Color(0, 252, 0);
        string[] lines =
        [
            "You detect something up ahead.",
            p.Name ?? "An encounter.",
            "Do you wish to encounter it?  (Y / N)",
        ];
        int ty = y + 14;
        foreach (string line in lines)
        {
            int tw = _fontRenderer.MeasureWidth(line);
            _fontRenderer.Draw(_spriteBatch, line, new Vector2(x + (w - tw) / 2, ty), green);
            ty += _fontRenderer.LineHeight + 6;
        }
    }

    private void DrawOptions()
    {
        if (!_optionsOpen || _fontRenderer is null)
            return;
        _optionsBg ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\OPBASE.frm");

        int ow = _optionsBg?.Width ?? 164, oh = _optionsBg?.Height ?? 217;
        var green = new Color(0, 252, 0);
        var hot = new Color(252, 252, 84);

        // Top-left of the panel (recompute the same way OptionsRowRect does).
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int px = Math.Max(0, (vp.Width - ow) / 2), py = Math.Max(0, (vp.Height - oh) / 2);

        if (_optionsBg is not null)
            _spriteBatch.Draw(_optionsBg, new Vector2(px, py), Color.White);
        else
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel, new Rectangle(px, py, ow, oh), new Color(8, 16, 8, 240));
        }

        int hovered = OptionsRowAt(Mouse.GetState().X, Mouse.GetState().Y);
        for (int i = 0; i < OptionsItems.Length; i++)
        {
            Rectangle r = OptionsRowRect(i);
            int tw = _fontRenderer.MeasureWidth(OptionsItems[i]);
            _fontRenderer.Draw(_spriteBatch, OptionsItems[i], new Vector2(px + (ow - tw) / 2, r.Y + 2), i == hovered ? hot : green);
        }
    }

    // ====================================================================
    //  Multi-slot save/load picker (P48)
    // ====================================================================
    //
    // A 10-slot save/load modal (the engine's LSGAME screen, loadsave.cc), opened from the
    // Options Save/Load rows: each row shows a slot's metadata (character / level / map / date)
    // or "- EMPTY -", click or 0-9 to save into / load from it. One JSON file per slot
    // (hexwaste-slotN.json) under SaveDir. F5/F9 stay a separate quicksave on the default path.
    // DIVERGENCE: a dark text panel, not the authentic LSGAME.frm art (an art residual, the
    // Skilldex text-then-art pattern); no overwrite-confirm dialog (a click saves directly).

    private enum SaveLoadMode { Save, Load }
    private bool _saveLoadOpen;
    private SaveLoadMode _saveLoadMode;
    private readonly Formats.SlotInfo[] _slotInfos = new Formats.SlotInfo[Formats.SaveSlots.Count];

    /// <summary>P52-M3: the authentic LSGAME.frm load/save window art (640x480, with the slot-list
    /// frame + info box baked in), lazily loaded; null falls back to the dark text panel.</summary>
    private Texture2D? _lsgameFrm;
    private bool _saveLoadArt; // LSGAME.frm loaded — switches the picker geometry to the art window

    /// <summary>The directory holding the per-slot save files (the harness --save-dir; default cwd).</summary>
    public string SaveDir { get; set; } = "";

    private string SlotPath(int slot) => string.IsNullOrEmpty(SaveDir)
        ? Formats.SaveSlots.SlotFileName(slot)
        : Path.Combine(SaveDir, Formats.SaveSlots.SlotFileName(slot));

    private void RefreshSlotInfos()
    {
        for (int i = 0; i < Formats.SaveSlots.Count; i++)
            _slotInfos[i] = Formats.SaveSlots.Describe(SaveState.Load(SlotPath(i)));
    }

    private void OpenSaveLoad(SaveLoadMode mode)
    {
        _saveLoadMode = mode;
        RefreshSlotInfos();
        _saveLoadOpen = true;
    }

    private void SaveGameToSlot(int slot)
    {
        if (!string.IsNullOrEmpty(SaveDir))
            Directory.CreateDirectory(SaveDir);
        string prev = SavePath;
        try { SavePath = SlotPath(slot); SaveGame(); }
        finally { SavePath = prev; }
        RefreshSlotInfos();
    }

    private void LoadGameFromSlot(int slot)
    {
        string prev = SavePath;
        try { SavePath = SlotPath(slot); LoadGame(); }
        finally { SavePath = prev; }
    }

    // The centred modal + per-slot row geometry (one helper shared by render + hit-test, the
    // OptionsRowRect pattern). Row layout: a title line, then the 10 slot rows below it.
    private const int SaveLoadPanelWidth = 470;

    // LSGAME.frm slot list: window-local (55, 87) 230x353, 10 slots evenly (loadsave.cc _ShowSlotList:2032).
    private const int SaveLoadListTop = 87, SaveLoadListX = 55, SaveLoadSlotH = 35;

    private Rectangle SaveLoadPanelRect()
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        if (_saveLoadArt)
        {
            const int w = 640, h = 480; // LSGAME.frm
            return new Rectangle(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2), w, h);
        }
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        int th = (Formats.SaveSlots.Count + 2) * lh + 16;
        int x = Math.Max(0, (vp.Width - SaveLoadPanelWidth) / 2);
        int y = Math.Max(0, (vp.Height - th) / 2);
        return new Rectangle(x, y, SaveLoadPanelWidth, th);
    }

    private Rectangle SaveLoadSlotRect(int slot)
    {
        Rectangle p = SaveLoadPanelRect();
        if (_saveLoadArt)
            return new Rectangle(p.X + SaveLoadListX, p.Y + SaveLoadListTop + slot * SaveLoadSlotH, 230, SaveLoadSlotH);
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        return new Rectangle(p.X + 8, p.Y + 12 + lh + slot * lh, p.Width - 16, lh);
    }

    private int SaveLoadSlotAt(int mx, int my)
    {
        for (int i = 0; i < Formats.SaveSlots.Count; i++)
            if (SaveLoadSlotRect(i).Contains(mx, my))
                return i;
        return -1;
    }

    private void DrawSaveLoad()
    {
        if (!_saveLoadOpen || _fontRenderer is null)
            return;
        // P52-M3: render the authentic LSGAME.frm window when present; the slot-list frame + info box
        // are baked into the art (loadsave.cc). Fall back to the dark text panel when the asset is absent.
        _lsgameFrm ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\LSGAME.frm");
        _saveLoadArt = _lsgameFrm is not null;
        _panelPixel ??= CreatePixel();
        Rectangle p = SaveLoadPanelRect();
        var green = new Color(0, 252, 0);
        var hot = new Color(252, 252, 84);
        var gray = new Color(140, 140, 140);

        if (_lsgameFrm is not null)
            _spriteBatch.Draw(_lsgameFrm, new Vector2(p.X, p.Y), Color.White);
        else
            _spriteBatch.Draw(_panelPixel, p, new Color(8, 16, 8, 240));

        string title = _saveLoadMode == SaveLoadMode.Save
            ? "SAVE GAME - pick a slot (0-9 / click, Esc cancel)"
            : "LOAD GAME - pick a slot (0-9 / click, Esc cancel)";
        _fontRenderer.Draw(_spriteBatch, title, new Vector2(p.X + 12, p.Y + (_saveLoadArt ? 60 : 10)), Color.LightGray);

        int hovered = SaveLoadSlotAt(Mouse.GetState().X, Mouse.GetState().Y);
        for (int i = 0; i < Formats.SaveSlots.Count; i++)
        {
            Formats.SlotInfo info = _slotInfos[i];
            Rectangle r = SaveLoadSlotRect(i);
            Color c = i == hovered ? hot : (info.Occupied && !info.VersionMismatch ? green : gray);
            if (_saveLoadArt)
            {
                // Engine slot block: a "[ SLOT NN: ]" header line, then the description below.
                string state = !info.Occupied ? "- EMPTY -" : info.VersionMismatch ? "- OLD VERSION -"
                    : $"{info.Character} L{info.Level}";
                _fontRenderer.Draw(_spriteBatch, $"[  SLOT {i + 1:00}:  ]", new Vector2(r.X + 4, r.Y + 1), c);
                _fontRenderer.Draw(_spriteBatch, state, new Vector2(r.X + 14, r.Y + 1 + _fontRenderer.LineHeight), c);
            }
            else
            {
                string label = !info.Occupied ? "- EMPTY -" : info.VersionMismatch ? "- OLD VERSION -"
                    : $"{info.Character} L{info.Level}  {info.Map}  {info.Date}";
                _fontRenderer.Draw(_spriteBatch, $"{i}. {label}", new Vector2(r.X + 6, r.Y + 2), c);
            }
        }

        // The info box baked into LSGAME at window-local (396,254) 164x60 (loadsave.cc _DrawInfoBox):
        // the hovered (else cursor) slot's fuller metadata.
        if (_saveLoadArt)
        {
            Formats.SlotInfo sel = _slotInfos[hovered >= 0 ? hovered : 0];
            int bx = p.X + 396, by = p.Y + 258;
            if (sel.Occupied && !sel.VersionMismatch)
            {
                _fontRenderer.Draw(_spriteBatch, sel.Character, new Vector2(bx, by), green);
                _fontRenderer.Draw(_spriteBatch, $"Level {sel.Level}", new Vector2(bx, by + _fontRenderer.LineHeight), green);
                _fontRenderer.Draw(_spriteBatch, sel.Map, new Vector2(bx, by + 2 * _fontRenderer.LineHeight), green);
                _fontRenderer.Draw(_spriteBatch, sel.Date, new Vector2(bx, by + 3 * _fontRenderer.LineHeight), green);
            }
        }
    }

    // ====================================================================
    //  Called-shot click dialog (P49-M1)
    // ====================================================================
    //
    // Replaces the V-key aim CYCLE with a click dialog (the engine's CALLED.frm body-part
    // picker, combat.cc:5476 calledShotSelectHitLocation): V opens it, 1-9 / click a row picks
    // a hit location, Esc cancels. Each row shows the location's to-hit penalty (the defining
    // per-location stat, combat.cc:172 hit_location_penalty). The location feeds the unchanged
    // TryAttack(target, AimLocation) path (penalty + crit-table lookup). DIVERGENCE: a single-
    // column text list, not the authentic CALLED.frm critter-pic overlay (art residual, the
    // Skilldex text-then-art pattern); the live per-part to-hit % is a residual (penalty shown).

    private bool _aimDialogOpen;

    // The dialog rows -> AimLocation values, in the engine's CALLED.frm button order
    // (head/eyes/right-arm/right-leg, then torso/groin/left-arm/left-leg — combat.cc:1894-1907),
    // then uncalled. AimNames/LocationPenalty are indexed by the AimLocation value.
    private static readonly int[] AimDialogOrder = { 0, 6, 2, 4, 3, 7, 1, 5, 8 };

    private void OpenAimDialog() => _aimDialogOpen = true;

    private Rectangle AimDialogPanelRect()
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        int w = 320, h = (AimDialogOrder.Length + 2) * lh + 12;
        return new Rectangle(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2), w, h);
    }

    private Rectangle AimDialogRowRect(int row)
    {
        Rectangle p = AimDialogPanelRect();
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        return new Rectangle(p.X + 8, p.Y + 10 + lh + row * lh, p.Width - 16, lh);
    }

    private int AimDialogRowAt(int mx, int my)
    {
        for (int i = 0; i < AimDialogOrder.Length; i++)
            if (AimDialogRowRect(i).Contains(mx, my))
                return i;
        return -1;
    }

    /// <summary>Pick a hit location from the dialog (a row index 0..8) and close it. Shared by the
    /// live click + the --aim-click harness so they drive the identical selection path.</summary>
    private void SelectAimRow(int row)
    {
        if (row < 0 || row >= AimDialogOrder.Length)
            return;
        AimLocation = AimDialogOrder[row];
        _aimDialogOpen = false;
        Log($"Aiming: {AimName(AimLocation)}.");
    }

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
            && AimDialogRowAt(mouse.X, mouse.Y) is int clicked && clicked >= 0)
            SelectAimRow(clicked);
    }

    private void DrawAimDialog()
    {
        if (!_aimDialogOpen || _fontRenderer is null)
            return;
        _panelPixel ??= CreatePixel();
        Rectangle p = AimDialogPanelRect();
        _spriteBatch.Draw(_panelPixel, p, new Color(8, 16, 8, 240));
        var green = new Color(0, 252, 0);
        var hot = new Color(252, 252, 84);
        _fontRenderer.Draw(_spriteBatch, "AIM - pick a hit location (1-9 / click, Esc cancel)",
            new Vector2(p.X + 12, p.Y + 8), Color.LightGray);
        int hovered = AimDialogRowAt(Mouse.GetState().X, Mouse.GetState().Y);
        // P52-M4: the LIVE per-location to-hit % against the aimed-at critter (the hovered target the
        // V key opens the dialog for). Recomputed per row via the same CombatEngine math the attack
        // uses; shown alongside the static penalty when a critter is targeted.
        MapObject? aimTarget = _hoveredObject is { } h && h != _dude?.Dude ? h : null;
        for (int i = 0; i < AimDialogOrder.Length; i++)
        {
            int loc = AimDialogOrder[i];
            int penalty = Formats.Combat.CriticalTables.LocationPenalty[loc];
            int? pct = aimTarget is not null ? _combat.PreviewToHit(aimTarget, loc) : null;
            string hit = pct is { } pc ? $"  {pc}%" : "";
            string label = loc == Formats.Combat.CriticalTables.LocationUncalled
                ? $"{i + 1}. uncalled (no aim){hit}"
                : $"{i + 1}. {AimName(loc)}  ({penalty:+0;-0;+0}){hit}";
            Rectangle r = AimDialogRowRect(i);
            _fontRenderer.Draw(_spriteBatch, label, new Vector2(r.X + 6, r.Y + 2),
                i == hovered ? hot : (loc == AimLocation ? hot : green));
        }
    }

    // Phase-15 M2: the four item panels (inventory / loot / barter / trade) share one
    // layout + one set of clickable rows + one overflow-paging window. A "kind" tags
    // each panel so a row CLICK can route to the same action its number key fires.
    private enum ItemPanelKind { Inventory, Loot, BarterStock, BarterGoods, TradeTake, TradeGive }

    // x position + title + list + dispatch kind + optional price column. One per visible
    // panel; the left panel is x=40, the right (sell/give side) x=420.
    private readonly record struct ItemPanel(
        int X, string Title, List<MapObject> Items, ItemPanelKind Kind, Func<MapObject, int>? Price);

    private const int ItemRowsPerPage = 9; // the 1-9 number-key row maps to one page

    // The panels currently on screen, in draw order. SINGLE source of truth shared by
    // DrawItemPanels (render) and TryClickItemPanel (hit-test) so a click always targets
    // exactly what's drawn. Mirrors the old DrawItemPanels branch order (barter > trade >
    // loot > inventory — OpenTrade sets both _tradePartner and _lootContainer, so trade
    // must be tested first).
    private List<ItemPanel> CurrentItemPanels()
    {
        var panels = new List<ItemPanel>(2);
        if (_barterNpc is { } merchant)
        {
            panels.Add(new(40, $"{ObjectName(merchant)} sells (caps {(_barterStock is { } till ? _scriptHost?.CapsTotal(till) : 0) ?? 0}) - click/1-9 buy",
                BarterStock(), ItemPanelKind.BarterStock, BarterBuyPrice));
            panels.Add(new(420, $"You sell (caps {DudeCaps()}) - click/Shift+1-9 sell, Esc done",
                BarterGoods(), ItemPanelKind.BarterGoods, BarterSellPrice));
        }
        else if (_tradePartner is { } follower)
        {
            panels.Add(new(40, $"Trading with {ObjectName(follower)} - click/1-9 take, A take all",
                follower.Inventory, ItemPanelKind.TradeTake, null));
            panels.Add(new(420, "You carry - click/Shift+1-9 give, Esc done",
                _dudeInventory, ItemPanelKind.TradeGive, null));
        }
        else if (_lootContainer is { } container)
        {
            panels.Add(new(40, $"{ObjectName(container)} - click/1-9 take, A take all, Esc close",
                container.Inventory, ItemPanelKind.Loot, null));
        }
        else if (_inventoryOpen)
        {
            panels.Add(new(40, "Inventory - click/1-9 use/equip, Shift drop, Esc close",
                _dudeInventory, ItemPanelKind.Inventory, null));
        }
        return panels;
    }

    private void DrawItemPanels()
    {
        if (_fontRenderer is null)
            return;
        foreach (ItemPanel panel in CurrentItemPanels())
        {
            int bottom = DrawItemList(panel.Title, panel.Items, panel.X, panel.Price);
            if (ReferenceEquals(panel.Items, _dudeInventory)) // the dude's side carries the weight readout (P24)
                DrawWeightReadout(panel.X, bottom);
        }
        DrawEquipSlots(); // P47: the weapon/armor equip slots + the dragged-item ghost
    }

    // ====================================================================
    //  Inventory drag-and-drop equip (P47)
    // ====================================================================
    //
    // The inventory panel supports drag: press an item (a list row or an occupied equip
    // slot), drag it to a slot to EQUIP / out of a slot to UNEQUIP; a tap on a row (no real
    // drag) falls back to the existing click-to-use/equip. Loot/barter/trade keep click-on-
    // press (they transfer, not equip). Ported from fallout2-ce inventory.cc — the press->
    // drag->release state machine + the slot hit-test cascade (inventory.cc:2386-2537) + the
    // _switch_hand equip/swap. DIVERGENCE: Hexwaste renders the slots as boxes beside the
    // text list, not the authentic INVBOX.frm paperdoll window (a documented art residual, the
    // Skilldex text-then-art pattern); and there is no LEFT-hand slot (single-weapon model).

    private enum DragSource { None, Row, WeaponSlot, ArmorSlot }
    private MapObject? _dragItem;       // the item currently being dragged, or null
    private DragSource _dragSource;     // where the drag started
    private Point _dragStart;           // the press position (to tell a tap from a drag)

    // The two equip-slot rects (screen coords; the inventory list panel is fixed at x=40,
    // width 360, so x=420 is free — the same column the barter/trade right panel uses).
    private static readonly Rectangle WeaponSlotRect = new(420, 96, 90, 60);
    private static readonly Rectangle ArmorSlotRect = new(420, 176, 90, 60);

    private static Formats.Combat.EquipSlot? EquipSlotAt(int mx, int my) =>
        WeaponSlotRect.Contains(mx, my) ? Formats.Combat.EquipSlot.Weapon
        : ArmorSlotRect.Contains(mx, my) ? Formats.Combat.EquipSlot.Armor
        : null;

    /// <summary>The inventory list row (0..8) under a point, or -1. Shares ItemRowRect with
    /// the renderer + TryClickItemPanel so they never disagree (panel x = 40).</summary>
    private int InventoryRowAt(int mx, int my)
    {
        for (int row = 0; row < ItemRowsPerPage; row++)
            if (ItemRowRect(40, row).Contains(mx, my))
                return row;
        return -1;
    }

    /// <summary>The dude's item currently in a slot — the wielded weapon, or the worn armor.</summary>
    private MapObject? EquippedInSlot(Formats.Combat.EquipSlot slot) =>
        slot == Formats.Combat.EquipSlot.Weapon
            ? _dudeInventory.FirstOrDefault(i => i.IsInHand && SafeProto(i.Pid)?.Weapon is not null)
            : _dudeInventory.FirstOrDefault(i => i.IsWorn);

    /// <summary>The live press/drag/release handler for the inventory panel (P47). Only the pure-
    /// inventory case reaches here; loot/barter/trade keep click-on-press in the caller.</summary>
    private void HandleInventoryDrag(MouseState mouse, bool shift)
    {
        bool press = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        bool release = mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed;

        if (press)
        {
            _dragStart = new Point(mouse.X, mouse.Y);
            _dragItem = null;
            _dragSource = DragSource.None;
            if (EquipSlotAt(mouse.X, mouse.Y) is { } slot && EquippedInSlot(slot) is { } equipped)
            {
                _dragItem = equipped;
                _dragSource = slot == Formats.Combat.EquipSlot.Weapon ? DragSource.WeaponSlot : DragSource.ArmorSlot;
            }
            else if (InventoryRowAt(mouse.X, mouse.Y) is int row && row >= 0)
            {
                int gi = _panelPage * ItemRowsPerPage + row;
                if (gi < _dudeInventory.Count)
                {
                    _dragItem = _dudeInventory[gi];
                    _dragSource = DragSource.Row;
                }
            }
            return;
        }

        if (release && _dragItem is { } dragged)
        {
            Formats.Combat.EquipSlot? overSlot = EquipSlotAt(mouse.X, mouse.Y);
            if (_dragSource == DragSource.Row && overSlot is { } dropSlot)
                EquipFromDrag(dragged, dropSlot); // list -> slot: equip
            else if (_dragSource is DragSource.WeaponSlot && overSlot != Formats.Combat.EquipSlot.Weapon)
                UnequipSlot(Formats.Combat.EquipSlot.Weapon); // dragged the weapon off its slot: unequip
            else if (_dragSource is DragSource.ArmorSlot && overSlot != Formats.Combat.EquipSlot.Armor)
                UnequipSlot(Formats.Combat.EquipSlot.Armor);
            else if (_dragSource == DragSource.Row
                && Math.Abs(mouse.X - _dragStart.X) <= 4 && Math.Abs(mouse.Y - _dragStart.Y) <= 4)
                TryClickItemPanel(_dragStart.X, _dragStart.Y, shift); // a tap: the click-to-use fallback
            _dragItem = null;
            _dragSource = DragSource.None;
        }
    }

    /// <summary>Equip an item dropped onto a slot — the _switch_hand equip path (inventory.cc:2490).
    /// A wrong-type drop (armor on the weapon slot, etc.) is rejected by EquipRules. Reuses the same
    /// flag/armor-bonus mutations as the click-to-equip (UseInventoryItem).</summary>
    private void EquipFromDrag(MapObject item, Formats.Combat.EquipSlot slot)
    {
        if (_dude is null)
            return;
        Formats.Proto.ProtoInfo? proto = SafeProto(item.Pid);
        if (!Formats.Combat.EquipRules.CanEquip(proto?.Weapon is not null, proto?.Armor is not null, slot))
            return;

        if (slot == Formats.Combat.EquipSlot.Weapon)
        {
            foreach (MapObject other in _dudeInventory)
                other.Flags &= ~(MapObject.FlagInLeftHand | MapObject.FlagInRightHand);
            item.Flags |= MapObject.FlagInRightHand;
            Log($"You ready the {ObjectName(item)}.");
        }
        else // Armor
        {
            if (item.IsWorn)
                return;
            foreach (MapObject other in _dudeInventory.Where(o => o.IsWorn))
            {
                if (SafeProto(other.Pid)?.Armor is { } oldArmor)
                    ApplyArmorBonus(oldArmor, -1);
                other.Flags &= ~MapObject.FlagWorn;
            }
            item.Flags |= MapObject.FlagWorn;
            if (proto!.Armor is { } armor)
                ApplyArmorBonus(armor, +1);
            Log($"You put on the {ObjectName(item)}.");
        }
    }

    /// <summary>Unequip the item currently in a slot (dragged off it) — clears the flag + reverses
    /// the armor bonus, mirroring UnequipForTransfer without removing the item from the bag.</summary>
    private void UnequipSlot(Formats.Combat.EquipSlot slot)
    {
        if (slot == Formats.Combat.EquipSlot.Weapon)
        {
            foreach (MapObject it in _dudeInventory.Where(i => i.IsInHand).ToList())
            {
                it.Flags &= ~(MapObject.FlagInLeftHand | MapObject.FlagInRightHand);
                Log($"You put away the {ObjectName(it)}.");
            }
        }
        else
        {
            foreach (MapObject it in _dudeInventory.Where(i => i.IsWorn).ToList())
            {
                if (SafeProto(it.Pid)?.Armor is { } armor)
                    ApplyArmorBonus(armor, -1);
                it.Flags &= ~MapObject.FlagWorn;
                Log($"You take off the {ObjectName(it)}.");
            }
        }
    }

    /// <summary>Draw the weapon + armor equip slots and the dragged item's ghost icon. Only in the
    /// pure-inventory view (loot/barter/trade have no equip slots).</summary>
    private void DrawEquipSlots()
    {
        if (_fontRenderer is null || !_inventoryOpen || _lootContainer is not null
            || _tradePartner is not null || _barterNpc is not null)
            return;
        _panelPixel ??= CreatePixel();
        DrawEquipSlot(WeaponSlotRect, "WEAPON", EquippedInSlot(Formats.Combat.EquipSlot.Weapon));
        DrawEquipSlot(ArmorSlotRect, "ARMOR", EquippedInSlot(Formats.Combat.EquipSlot.Armor));
        if (_dragItem is { } dragged) // the ghost icon follows the cursor (from the last Update mouse)
            DrawItemIcon(dragged, new Rectangle(_previousMouse.X - 14, _previousMouse.Y - 11, 28, 22));
    }

    private void DrawEquipSlot(Rectangle rect, string label, MapObject? item)
    {
        _spriteBatch.Draw(_panelPixel, rect, new Color(8, 8, 8, 230));
        var border = new Color(0, 252, 0);
        _spriteBatch.Draw(_panelPixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), border);
        _spriteBatch.Draw(_panelPixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), border);
        _spriteBatch.Draw(_panelPixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), border);
        _spriteBatch.Draw(_panelPixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), border);
        _fontRenderer!.Draw(_spriteBatch, label, new Vector2(rect.X + 4, rect.Y - 22), Color.LightGray);
        if (item is not null)
            DrawItemIcon(item, new Rectangle(rect.X + 8, rect.Y + 6, rect.Width - 16, rect.Height - 12));
        else
            _fontRenderer.Draw(_spriteBatch, "(empty)", new Vector2(rect.X + 8, rect.Y + rect.Height / 2 - 8), Color.Gray);
    }

    /// <summary>The carried-weight readout, drawn just below the dude's inventory panel (P24;
    /// inventory.cc:3164 "Total Wt: N/M") — green within capacity, red when over
    /// (critterIsEncumbered). Below the panel so it never collides with the title/rows.</summary>
    private void DrawWeightReadout(int panelX, int panelBottom)
    {
        if (_fontRenderer is null || _dude is null)
            return;
        int carried = DudeCarriedWeight(), cap = DudeCarryCapacity();
        Color color = Formats.Map.InventoryWeight.IsEncumbered(carried, cap)
            ? new Color(255, 64, 64) : new Color(0, 252, 0);
        _fontRenderer.Draw(_spriteBatch, $"Total Wt: {carried}/{cap}", new Vector2(panelX + 10, panelBottom + 4), color);
    }

    // The clickable rect for the displayRow-th row (0..8) of the panel at x. Both the
    // renderer and the hit-test go through this so they can never disagree on geometry.
    private Rectangle ItemRowRect(int x, int displayRow)
    {
        int lineHeight = Math.Max(_fontRenderer?.LineHeight ?? 26, 26);
        int rowY = 60 + 8 + lineHeight + 6 + displayRow * lineHeight;
        return new Rectangle(x + 6, rowY - 4, 360 - 12, lineHeight);
    }

    /// <summary>Draws the panel and returns the y just below it (P24 — the weight readout sits there).</summary>
    private int DrawItemList(string title, List<MapObject> items, int x,
        Func<MapObject, int>? price = null)
    {
        _panelPixel ??= CreatePixel();
        int lineHeight = Math.Max(_fontRenderer!.LineHeight, 26);
        int panelWidth = 360;
        int start = _panelPage * ItemRowsPerPage;
        int shown = Math.Clamp(items.Count - start, 0, ItemRowsPerPage);
        int panelHeight = (Math.Max(shown, 1) + 2) * lineHeight + 16;
        int y = 60;

        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, panelWidth, panelHeight), new Color(8, 8, 8, 230));
        _fontRenderer.Draw(_spriteBatch, title, new Vector2(x + 10, y + 8), Color.LightGray);

        int rowY = y + 8 + lineHeight + 6;
        var green = new Color(0, 252, 0);
        if (items.Count == 0)
            _fontRenderer.Draw(_spriteBatch, "(empty)", new Vector2(x + 10, rowY), Color.Gray);

        for (int row = 0; row < ItemRowsPerPage; row++)
        {
            int gi = start + row;
            if (gi >= items.Count)
                break;
            MapObject item = items[gi];
            DrawItemIcon(item, new Rectangle(x + 28, rowY - 2, 28, 22));
            string count = item.StackCount > 1 ? $" x{item.StackCount}" : "";
            string tag = price is null ? "" : $"  ${price(item)}";
            _fontRenderer.Draw(_spriteBatch, $"{row + 1}.", new Vector2(x + 10, rowY), green);
            _fontRenderer.Draw(_spriteBatch, $"{ObjectName(item)}{count}{tag}", new Vector2(x + 62, rowY), green);
            rowY += lineHeight;
        }

        if (items.Count > ItemRowsPerPage)
        {
            int pages = (items.Count + ItemRowsPerPage - 1) / ItemRowsPerPage;
            _fontRenderer.Draw(_spriteBatch, $"(page {Math.Min(_panelPage + 1, pages)}/{pages} - PgUp/PgDn)",
                new Vector2(x + 10, rowY), Color.Gray);
        }
        return y + panelHeight;
    }

    // Highest page index across the visible panels (shared paging window).
    private int MaxPanelPage()
    {
        int max = 0;
        foreach (ItemPanel panel in CurrentItemPanels())
            max = Math.Max(max, (Math.Max(panel.Items.Count, 1) - 1) / ItemRowsPerPage);
        return max;
    }

    // Route a row CLICK to the same action its number key fires. `shift` only matters
    // for the single inventory panel (use vs drop); the other panels are physically
    // split (buy/sell, take/give), so a plain click is unambiguous.
    private void DispatchItemPanel(ItemPanelKind kind, int index, bool shift)
    {
        switch (kind)
        {
            case ItemPanelKind.BarterStock: BarterBuy(index); break;
            case ItemPanelKind.BarterGoods: BarterSell(index); break;
            case ItemPanelKind.TradeTake:
            case ItemPanelKind.Loot:        TakeFromContainer(index); break;
            case ItemPanelKind.TradeGive:   GiveToFollower(index); break;
            case ItemPanelKind.Inventory:
                if (shift) DropFromInventory(index);
                else UseInventoryItem(index);
                break;
        }
    }

    // Hit-test a click against the visible panel rows; dispatch the first match. Returns
    // false if the click missed every row (so the caller can fall through). Geometry-only
    // (no Draw dependency) so the headless --panel-click harness can drive it too.
    private bool TryClickItemPanel(int mx, int my, bool shift)
    {
        foreach (ItemPanel panel in CurrentItemPanels())
        {
            int start = _panelPage * ItemRowsPerPage;
            for (int row = 0; row < ItemRowsPerPage; row++)
            {
                int gi = start + row;
                if (gi >= panel.Items.Count)
                    break;
                if (ItemRowRect(panel.X, row).Contains(mx, my))
                {
                    DispatchItemPanel(panel.Kind, gi, shift);
                    return true;
                }
            }
        }
        return false;
    }

    // PgUp/PgDn step the shared paging window so overflow past the 9th row is reachable.
    private void HandlePanelPaging(KeyboardState keyboard)
    {
        if (IsKeyPressed(keyboard, Keys.PageDown))
            _panelPage = Math.Min(_panelPage + 1, MaxPanelPage());
        else if (IsKeyPressed(keyboard, Keys.PageUp))
            _panelPage = Math.Max(_panelPage - 1, 0);
    }

    /// <summary>
    /// Creating textures (SetData) inside an active SpriteBatch corrupts the
    /// in-flight batch — warm the icon cache before the panel ever draws.
    /// </summary>
    private void PrewarmItemTextures(IEnumerable<MapObject> items)
    {
        foreach (MapObject item in items)
        {
            try
            {
                int inventoryFid = _protos.Get(item.Pid).InventoryFid;
                if (inventoryFid != -1)
                    _frmCache.GetTexture(inventoryFid);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
            }
        }
    }

    private void DrawItemIcon(MapObject item, Rectangle destination)
    {
        try
        {
            int inventoryFid = _protos.Get(item.Pid).InventoryFid;
            if (inventoryFid == -1)
                return;
            Texture2D texture = _frmCache.GetTexture(inventoryFid);
            float scale = Math.Min((float)destination.Width / texture.Width,
                (float)destination.Height / texture.Height);
            var size = new Point((int)(texture.Width * scale), (int)(texture.Height * scale));
            _spriteBatch.Draw(texture,
                new Rectangle(destination.X, destination.Y + (destination.Height - size.Y) / 2, size.X, size.Y),
                Color.White);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
        }
    }
}
