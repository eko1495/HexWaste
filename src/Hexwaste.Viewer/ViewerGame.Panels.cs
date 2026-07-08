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
    private Texture2D? _charBg;
    private Texture2D? _bigNum;
    private bool _charBgTried;

    // P82-M2: the currently-selected character-sheet item, in the engine's EDITOR_* numbering
    // (0-6 SPECIAL, 7/8/9 level/exp/next, 43 max-HP, 44-50 conditions, 51-60 derived, 61-78 skills).
    // Clicking an info region sets it; the description card + the highlight read it.
    private int _charSelId;

    // stat.msg / skill.msg (the character-editor description strings). Lazy, like _editorMsg.
    private Formats.Text.MessageFile? _statMsg; private bool _statMsgTried;
    private Formats.Text.MessageFile? _skillMsg; private bool _skillMsgTried;
    private string StatMsg(int id) => id < 0 ? "" : LazyMsg(@"text\english\game\stat.msg", ref _statMsgTried, ref _statMsg)?.GetText(id) ?? "";
    private string SkillMsg(int id) => id < 0 ? "" : LazyMsg(@"text\english\game\skill.msg", ref _skillMsgTried, ref _skillMsg)?.GetText(id) ?? "";
    // trait description (trait.cc:79 — trait.msg 200+trait; the name is 100+trait, via TraitName).
    private string TraitDesc(int i) => i < 0 ? "" : LazyMsg(@"text\english\game\trait.msg", ref _traitMsgTried, ref _traitMsg)?.GetText(200 + i) ?? "";

    // The bottom-left folder (character_editor.cc region 527 = 28,363,283,105): traits + perks as
    // CLICKABLE rows. Card ids: 500+trait, 1000+perk (distinct from the EDITOR_* item numbering).
    private const int FolderStartY = 363;

    // The middle column's two panels (character_editor.cc characterEditorDrawDerivedStats): the
    // CONDITION block at y=46 (HP + status labels, items 43-50) and the DERIVED block at y=179
    // (items 51-60). Each derived row = (editor.msg label id, engine STAT_* index). The STAT_*
    // ordinals are the VERIFIED stat_defs.h values (DR=24, RadRes=31, PoisonRes=32 — the workflow
    // synthesizer had these off by one; cross-checked vs CritterStat.DamageResistance=24).
    private const int CharCondStartY = 46, CharDerivedStartY = 179;
    private static readonly int[] CharStatY = [37, 70, 103, 136, 169, 202, 235];
    // (editor.msg label id, engine STAT_* index) per render row — the label ids are the engine's
    // verbatim getmsg ids (NON-sequential: AC=302, AP=301, Carry=311, Melee=304, ...).
    private static readonly (int Label, int Stat)[] CharDerivedRows =
        [(302, 9), (301, 8), (311, 12), (304, 11), (305, 24), (306, 32), (307, 31), (308, 13), (309, 14), (310, 15)];

    /// <summary>The character sheet (C / K): the authentic FO2 character-editor backdrop
    /// (interface FID 177) with SPECIAL + derived stats + level on the left/middle and the
    /// 18 skills on the right, positioned at the engine's character_editor.cc coordinates;
    /// falls back to a plain text panel when the art is absent (the Skilldex/INVBOX
    /// text-then-art pattern). Read-only, but a banked skill point raises the selected
    /// skill in place (Right/Enter).</summary>
    private void DrawSkillAllocator()
    {
        if (!_skillAllocOpen || _fontRenderer is null || _dudeGcd is null)
            return;

        if (!_charBgTried)
        {
            _charBgTried = true;
            // EDTREDT.FRM (interface FID 177 = the in-game character-editor backdrop) + BIGNUM.FRM
            // (FID 170, the SPECIAL big-digit strip). Loaded into dedicated fields — NOT the LRU
            // FrmCache, which evicts + disposes its textures during play — the PERKWIN/OPBASE
            // text-then-art pattern. ported from fallout2-ce src/character_editor.cc:1282/307.
            _charBg = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\EDTREDT.FRM");
            _bigNum = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\BIGNUM.FRM");
        }
        if (_charBg is null) { DrawSkillAllocatorFallback(); return; }

        var green = new Color(0, 252, 0);
        var gold = new Color(252, 252, 84);
        var tan = new Color(180, 156, 96);

        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int ox = (vp.Width - 640) / 2, oy = (vp.Height - 480) / 2;
        _spriteBatch.Draw(_charBg, new Rectangle(ox, oy, 640, 480), Color.White);

        int rh = _fontRenderer.LineHeight + 1; // engine: fontGetLineHeight() + 1
        void T(int px, int py, string s, Color c) =>
            _fontRenderer!.Draw(_spriteBatch, s, new Vector2(ox + px, oy + py), c);

        int[] bb = _dudeGcd.Stats.BaseStats, bbo = _dudeGcd.Stats.BonusStats, ssk = _dudeGcd.Stats.Skills;
        int[] tg = _dudeGcd.TaggedSkills;
        Formats.Combat.CritterState? cs = _dude is not null ? GetCritterState(_dude.Dude) : null;
        int Sp(int i) => cs?.Stat(i) ?? (bb[i] + bbo[i]);

        // Name / Age / Gender — the three top buttons (characterEditorDrawName/Age/Gender), centered
        // on their baked boxes (name ~75, age ~191, gender ~270). Gender = editor.msg 107+gender
        // (107 Male / 108 Female); age defaults to 25 (the engine's creation default).
        void TC(int cx, int py, string s, Color col) =>
            _fontRenderer!.Draw(_spriteBatch, s, new Vector2(ox + cx - _fontRenderer.MeasureWidth(s) / 2f, oy + py), col);
        string nm = _dudeGcd is { Name.Length: > 0 } g && g.Name != "None" ? g.Name : "Wanderer";
        int[] bs = _dudeGcd.Stats.BaseStats;
        int age = bs.Length > 33 && bs[33] > 0 ? bs[33] : 25;
        int gender = bs.Length > 34 ? bs[34] : 0;
        TC(75, 5, nm, gold);
        TC(191, 5, age.ToString(), gold);
        TC(270, 5, EditorMsg(107 + Math.Clamp(gender, 0, 1)), gold);

        // SPECIAL values as the engine's bignum.frm digit pairs (the stat NAMES are baked into
        // the backdrop): blitted at x=58, the gCharacterEditorPrimaryStatY rows.
        // ported from fallout2-ce src/character_editor.cc characterEditorDrawBigNumber: 14x24
        // cells, white digits 0-9 in [0..167], red in [168..]; tens then ones, no leading-zero
        // suppression (so a value < 10 reads "0N"). Red signals a value > 10 (RED_NUMBERS).
        int[] statY = [37, 70, 103, 136, 169, 202, 235];
        void BigNumber(int bx, int by, int value)
        {
            if (_bigNum is null) { T(bx + 6, by + 8, value.ToString(), green); return; }
            int v = Math.Clamp(value, 0, 99), off = value > 10 ? 168 : 0;
            _spriteBatch.Draw(_bigNum, new Vector2(ox + bx, oy + by),
                new Rectangle(off + v / 10 * 14, 0, 14, 24), Color.White);
            _spriteBatch.Draw(_bigNum, new Vector2(ox + bx + 14, oy + by),
                new Rectangle(off + v % 10 * 14, 0, 14, 24), Color.White);
        }
        for (int i = 0; i < 7; i++)
            BigNumber(58, statY[i], Sp(i));

        // Level / Experience / next-level (x=32, y=280; character_editor.cc:2378-2429) — gold when selected.
        T(33, 281, $"Level {_dudeLevel}", _charSelId == 7 ? gold : green);
        T(33, 281 + rh, $"Exp {_dudeXp}", _charSelId == 8 ? gold : green);
        int nextXp = Formats.Combat.Progression.XpForLevel(_dudeLevel + 1);
        T(33, 281 + 2 * rh, nextXp > 0 ? $"Next {nextXp}" : "Next (max)", _charSelId == 9 ? gold : green);

        // Middle column, two panels (character_editor.cc characterEditorDrawDerivedStats):
        int rstep = rh + 2;
        if (cs is not null)
        {
            // CONDITION panel (region 528, y=46): HP value + the 7 status labels (green ok / red afflicted).
            bool hpSel = _charSelId == 43;
            T(194, CharCondStartY, EditorMsg(300), hpSel ? gold : green);
            T(263, CharCondStartY, $"{_dude!.Dude.CurrentHp}/{cs.MaxHp}", hpSel ? gold : green);
            int res = _dude.Dude.CombatResults;
            var red = new Color(252, 84, 84);
            bool[] bad =
            [
                _dude.Dude.Poison > 0,                                              // 312 Poisoned
                false,                                                              // 313 Radiated (not modeled)
                (res & Formats.Combat.CriticalTables.DamBlind) != 0,                // 314 Eye Damage
                (res & Formats.Combat.CriticalTables.DamCripArmRight) != 0,         // 315 Crippled R Arm
                (res & Formats.Combat.CriticalTables.DamCripArmLeft) != 0,          // 316 Crippled L Arm
                (res & Formats.Combat.CriticalTables.DamCripLegRight) != 0,         // 317 Crippled R Leg
                (res & Formats.Combat.CriticalTables.DamCripLegLeft) != 0,          // 318 Crippled L Leg
            ];
            int cyy = CharCondStartY + rstep + 4;
            for (int i = 0; i < 7; i++)
            {
                T(194, cyy, EditorMsg(312 + i), _charSelId == 44 + i ? gold : bad[i] ? red : green);
                cyy += rstep;
            }

            // DERIVED panel (region 529, y=179): 10 derived stats, label x=194 / value x=288.
            int dyy = CharDerivedStartY;
            for (int i = 0; i < CharDerivedRows.Length; i++)
            {
                Color dc = _charSelId == 51 + i ? gold : green;
                T(194, dyy, EditorMsg(CharDerivedRows[i].Label), dc);
                T(288, dyy, DerivedValue(i, cs), dc);
                dyy += rstep;
            }
        }

        // Skills (right): name x=380, value x=573, y=27 + i*(lineHeight+1) (character_editor.cc:2974).
        for (int i = 0; i < Formats.Combat.SkillSet.SkillCount; i++)
        {
            int value = Formats.Combat.SkillSet.Value(bb, bbo, ssk, tg, i);
            bool tagged = Array.IndexOf(tg, i) >= 0;
            bool selected = i == _skillAllocIndex && _unspentSkillPoints > 0;
            Color c = selected || tagged || _charSelId == 61 + i ? gold : green;
            int sy = 27 + i * rh;
            T(380, sy, (selected ? "> " : "  ") + Formats.Combat.SkillSet.Names[i], c);
            T(selected ? 540 : 573, sy,
                selected ? $"{value}% +{Formats.Combat.SkillSet.Cost(value)}" : $"{value}%", c);
        }

        // Tag-skill counter (always drawn at 522,228 — character_editor.cc:1421/2961): the number
        // of unused tag slots (NUM_TAGGED_SKILLS 4 − tagged), faithful even in the read-only view.
        BigNumber(522, 228, Math.Max(0, 4 - tg.Count(t => t >= 0)));
        // "Tag Skill(s)" caption left of the counter (editor.msg 138 at 422,233; the engine renders
        // it in creation mode only — we surface it in view too so the bare counter reads clearly).
        T(422, 233, EditorMsg(138), tan);

        // Selection cue: a gold outline on the selected recess/row (the engine leaves the view-mode
        // bignum white, so the outline + the description card are the click feedback).
        if (SheetItemRect(_charSelId) is { } sr)
            DrawRectOutline(new Rectangle(ox + sr.X, oy + sr.Y, sr.Width, sr.Height), gold);

        // Description card (the baked parchment, ~x348 y262+): the selected item's NAME + wrapped
        // DESCRIPTION in black. ported from character_editor.cc characterEditorDrawCardWithOptions
        // (title at 348,272; description at 348,315; _colorTable[0] = black on the parchment).
        (string cardTitle, string cardDesc) = SheetCard(_charSelId);
        if (cardTitle.Length > 0)
            _fontRenderer.Draw(_spriteBatch, cardTitle, new Vector2(ox + 348, oy + 270), Color.Black, shadow: false);
        int cardY = oy + 292;
        foreach (string ln in _fontRenderer.WrapText(cardDesc, 236))
        {
            if (cardY > oy + 456)
                break;
            _fontRenderer.Draw(_spriteBatch, ln, new Vector2(ox + 348, cardY), Color.Black, shadow: false);
            cardY += rh;
        }

        // Bottom-left folder (region 527, y=363): traits + perks as CLICKABLE rows (-> the card),
        // plus karma/rep/town/kills info lines. The row list is shared by the render + the hit-test.
        int fy = FolderStartY;
        foreach ((string text, int cardId, bool info) in BuildFolderRows())
        {
            if (fy > 452)
                break;
            T(34, fy, text, info ? tan : _charSelId == cardId ? gold : green);
            fy += rh;
        }

        string spHint = _unspentSkillPoints > 0 ? $"{_unspentSkillPoints} skill pts (Enter raises)   " : "";
        T(34, 462, $"{spHint}Click for info   G perk", tan);

        // Bottom buttons (character_editor.cc PRINT 363 / DONE 475 / CANCEL 571 at y=454; the red
        // round buttons are baked into the backdrop). DONE + CANCEL close the sheet; Print (a
        // character dump to a text file) is out of scope, so its label is shown but inert.
        T(383, 455, EditorMsg(103), tan);   // Print To File (inert)
        T(492, 455, EditorMsg(100), gold);  // Done
        T(585, 455, EditorMsg(102), gold);  // Cancel
    }

    /// <summary>The bottom-left folder rows: each (display text, card id, isInfo). Trait rows carry
    /// card id 500+trait, perk rows 1000+perk (clickable -> the description card); karma/rep/town/
    /// kills are info-only (card id -1). Deterministic — shared by the render + CharSheetItemAt.</summary>
    private List<(string Text, int CardId, bool Info)> BuildFolderRows()
    {
        var rows = new List<(string, int, bool)>();
        if (_dudeGcd is null)
            return rows;
        List<int> traits = _dudeGcd.Traits.Where(t => t >= 0).ToList();
        if (traits.Count == 0)
            rows.Add(("Traits: none", -1, true));
        else
        {
            rows.Add(("Traits:", -1, true));
            foreach (int t in traits)
                rows.Add(($"  {TraitName(t)}", 500 + t, false));
        }
        List<int> perks = Enumerable.Range(0, _dudePerkRanks.Length).Where(i => _dudePerkRanks[i] > 0).ToList();
        if (perks.Count == 0)
            rows.Add(("Perks: none", -1, true));
        else
        {
            rows.Add(("Perks:", -1, true));
            foreach (int p in perks)
                rows.Add(($"  {PerkName(p)}{(_dudePerkRanks[p] > 1 ? $" ({_dudePerkRanks[p]})" : "")}", 1000 + p, false));
        }
        if (AvailablePerkPicks() > 0)
            rows.Add(($"{AvailablePerkPicks()} perk(s) available - press G", -1, true));
        foreach (string kl in KarmaDisplayLines())
            rows.Add((kl, -1, true));
        List<string> kills = KillDisplayLines();
        if (kills.Count > 0)
        {
            rows.Add(("Kills:", -1, true));
            foreach (string kl in kills)
                rows.Add(($"  {kl}", -1, true));
        }
        return rows;
    }

    /// <summary>The derived-stat value string for derived row <paramref name="i"/> (the
    /// CharDerivedRows order: AC/AP/Carry/Melee/DR/PoisonRes/RadRes/Sequence/Heal/Crit).</summary>
    private static string DerivedValue(int i, Formats.Combat.CritterState cs) => i switch
    {
        0 => cs.ArmorClass.ToString(),
        1 => cs.MaxActionPoints.ToString(),
        2 => cs.CarryWeight.ToString(),
        3 => cs.MeleeDamage.ToString(),
        4 => $"{cs.DamageResistance}%",
        5 => $"{cs.Stat(32)}%",                                                  // poison resistance
        6 => $"{cs.Stat(31)}%",                                                  // radiation resistance
        7 => cs.Sequence.ToString(),
        8 => Math.Max(cs.Stat(Formats.Combat.CritterStat.Endurance) / 3, 1).ToString(),
        _ => $"{cs.Stat(Formats.Combat.CritterStat.CriticalChance)}%",
    };

    /// <summary>The description-card (title, body) for an engine EDITOR_* item id, ported from
    /// character_editor.cc characterEditorDrawCard: SPECIAL + derived via stat.msg (100+stat /
    /// 200+stat), skills via skill.msg, conditions/level via editor.msg/stat.msg.</summary>
    private (string Title, string Desc) SheetCard(int id)
    {
        if (id is >= 0 and < 7) return (StatMsg(100 + id), StatMsg(200 + id));   // SPECIAL
        if (id == 7) return (StatMsg(400), StatMsg(500));                        // Level (PC_STAT_LEVEL=0)
        if (id == 8) return (StatMsg(401), StatMsg(501));                        // Experience
        if (id == 9) return (EditorMsg(122), EditorMsg(123));                    // Next level
        if (id == 43) return (EditorMsg(300), StatMsg(207));                     // Max HP (STAT_MAX_HP=7)
        if (id is >= 44 and < 51) return (EditorMsg(312 + (id - 44)), EditorMsg(400 + (id - 44))); // conditions
        if (id is >= 51 and < 61) { int s = CharDerivedRows[id - 51].Stat; return (StatMsg(100 + s), StatMsg(200 + s)); }
        if (id is >= 61 and < 79) { int s = id - 61; return (SkillName(s), SkillMsg(200 + s)); }
        if (id is >= 500 and < 516) return (TraitName(id - 500), TraitDesc(id - 500));     // folder trait
        if (id is >= 1000 and < 1119) return (PerkName(id - 1000), PerkDescription(id - 1000)); // folder perk
        return ("", "");
    }

    /// <summary>The window-local rect of an EDITOR_* item (for the gold selection outline) — kept in
    /// lock-step with the render positions + CharSheetItemAt's hit regions.</summary>
    private Rectangle? SheetItemRect(int id)
    {
        if (_fontRenderer is null)
            return null;
        int rh = _fontRenderer.LineHeight + 1, rstep = rh + 2;
        if (id is >= 0 and < 7) return new Rectangle(56, CharStatY[id] - 1, 30, 26);            // SPECIAL recess
        if (id is >= 7 and <= 9) return new Rectangle(31, 280 + (id - 7) * rh, 120, rh);        // level/exp/next
        if (id == 43) return new Rectangle(192, CharCondStartY - 1, 122, rh + 1);               // HP
        if (id is >= 44 and < 51) return new Rectangle(192, CharCondStartY + rstep + 3 + (id - 44) * rstep, 122, rh + 1);
        if (id is >= 51 and < 61) return new Rectangle(192, CharDerivedStartY - 1 + (id - 51) * rstep, 122, rh + 1);
        if (id is >= 61 and < 79) return new Rectangle(378, 26 + (id - 61) * rh, 215, rh);      // skill row
        if (id >= 500)                                                                          // folder trait/perk row
        {
            int idx = BuildFolderRows().FindIndex(r => r.CardId == id);
            return idx >= 0 ? new Rectangle(32, FolderStartY - 1 + idx * rh, 280, rh) : null;
        }
        return null;
    }

    /// <summary>Hit-test the character sheet: map a screen click to an EDITOR_* item id, or -1.
    /// ported from character_editor.cc characterEditorRegisterInfoAreas + HandleInfoButtonPressed
    /// (the region rects; the Y-within-region maps to the item, using OUR render steps).</summary>
    private int CharSheetItemAt(int mx, int my)
    {
        if (_charBg is null || _fontRenderer is null)
            return -1;
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int ox = (vp.Width - 640) / 2, oy = (vp.Height - 480) / 2;
        int lx = mx - ox, ly = my - oy;
        int rh = _fontRenderer.LineHeight + 1, rstep = rh + 2;
        if (lx is >= 19 and < 144 && ly is >= 33 and < 266)                                     // SPECIAL (525)
            for (int i = 0; i < 7; i++)
                if (ly >= CharStatY[i] - 5 && ly <= CharStatY[i] + 27)
                    return i;
        if (lx is >= 28 and < 152 && ly is >= 280 and < 313)                                    // level/exp (526)
            return 7 + Math.Clamp((ly - 281) / rh, 0, 2);
        if (lx is >= 191 and < 314 && ly is >= 41 and < 151)                                    // condition (528)
            return ly < CharCondStartY + rstep + 3 ? 43 : 44 + Math.Clamp((ly - (CharCondStartY + rstep + 3)) / rstep, 0, 6);
        if (lx is >= 191 and < 314 && ly is >= 170 and < 312)                                   // derived (529)
            return 51 + Math.Clamp((ly - CharDerivedStartY) / rstep, 0, 9);
        if (lx is >= 370 and < 594 && ly is >= 26 and < 222)                                    // skills (531)
            return 61 + Math.Clamp((ly - 26) / rh, 0, 17);
        if (lx is >= 28 and < 312 && ly is >= FolderStartY and < 460)                            // folder (527): trait/perk rows
        {
            List<(string Text, int CardId, bool Info)> rows = BuildFolderRows();
            int row = (ly - FolderStartY) / rh;
            return row >= 0 && row < rows.Count && rows[row].CardId >= 0 ? rows[row].CardId : -1;
        }
        return -1;
    }

    /// <summary>The pre-P82 plain dark-panel character sheet, used when the FID-177 backdrop
    /// art is absent (the text-then-art fallback).</summary>
    private void DrawSkillAllocatorFallback()
    {
        if (_fontRenderer is null || _dudeGcd is null)
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
        if (_pipboyArchives) // P88: the quest-log page — Status returns, Close exits
        {
            rows.Add(("Status", () => _pipboyArchives = false));
            rows.Add(("Close", () => _pipboyOpen = false));
        }
        else if (!_pipboyRestMenu)
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

    // P82: the PIP.frm left-column tab buttons (x=53, y=341 step 27, the alarm at index 1 skipped;
    // inventory labels STATUS/AUTOMAPS/ARCHIVES/CLOSE). Returns the action for a click in the tab column,
    // or null. Hexwaste only models Status + the automap, so Archives is a no-op.
    private Action? PipboyTabAt(int mx, int my)
    {
        PipboyContentOrigin(out Point po, out _);
        if (mx < po.X + 35 || mx > po.X + 210)
            return null;
        int ry = my - po.Y;
        return ry switch
        {
            >= 336 and < 366 => () => { _pipboyRestMenu = false; _pipboyArchives = false; }, // STATUS
            >= 390 and < 419 => () => { _pipboyOpen = false; _automapOpen = true; },         // AUTOMAPS
            >= 419 and < 446 => () => { _pipboyArchives = true; _pipboyRestMenu = false; },  // ARCHIVES (P88)
            >= 446 and < 478 => () => _pipboyOpen = false,                           // CLOSE
            _ => null,
        };
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

        // P82 fix: the P20-M1 embedded mini-map is REMOVED from the Pip-Boy — PIP.frm's left column is
        // baked art (the date decoration + the STATUS/AUTOMAPS/ARCHIVES/CLOSE tab buttons), so the mini-map
        // overlaid them ("looks strange" + covered the tabs). The full automap (the Automap row / left tab)
        // is the map; DrawPipboyMiniMap stays for any future authentic-recess use.

        if (_pipboyArchives) // P88: the quest-log page
        {
            DrawPipboyArchives(cx, ty, lh, green, dim, po.Y + ph - 36);
        }
        else if (!_pipboyRestMenu)
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

    /// <summary>data\quests.txt, lazy-loaded (P88). Empty if absent.</summary>
    private IReadOnlyList<Formats.Quest> Quests()
    {
        if (!_questsTried)
        {
            _questsTried = true;
            if (_vfs.Exists(@"data\quests.txt"))
            {
                using Stream s = _vfs.OpenRead(@"data\quests.txt");
                _quests = Formats.QuestLog.Parse(s);
            }
        }
        return _quests ?? [];
    }

    /// <summary>data\holodisk.txt, lazy-loaded (P100 Point 4). Empty if absent.</summary>
    private IReadOnlyList<Formats.Holodisk> Holodisks()
    {
        if (!_holodisksTried)
        {
            _holodisksTried = true;
            if (_vfs.Exists(@"data\holodisk.txt"))
            {
                using Stream s = _vfs.OpenRead(@"data\holodisk.txt");
                _holodisks = Formats.HolodiskLog.Parse(s);
            }
        }
        return _holodisks ?? [];
    }

    /// <summary>P88: the Pip-Boy ARCHIVES quest log. Each quest shows once its global var reaches the
    /// quests.txt displayThreshold (so the displayThreshold-0 quests like "Retrieve the GECK" show from
    /// the start), grouped under its town name (map.msg), numbered per town, dimmed + "(done)" once the
    /// var reaches completedThreshold. ported from fallout2-ce src/pipboy.cc pipboyRenderQuestList().</summary>
    private void DrawPipboyArchives(int cx, int ty, int lh, Color green, Color dim, int bottomLimit)
    {
        _fontRenderer!.Draw(_spriteBatch, "ARCHIVES", new Vector2(cx, ty), green);
        ty += lh + 4;

        int lastLocation = -1, number = 1, shown = 0;
        foreach (Formats.Quest q in Quests())
        {
            int gv = _scriptHost?.GlobalVars.GetValueOrDefault(q.Gvar) ?? 0;
            if (gv < q.DisplayThreshold)
                continue;
            if (ty > bottomLimit - lh)
                break;

            if (q.Location != lastLocation) // a new town header (map.msg name)
            {
                lastLocation = q.Location;
                number = 1;
                string loc = LazyMsg(@"text\english\game\map.msg", ref _mapMsgTried, ref _mapMsg)?.GetText(q.Location)
                             ?? $"Area {q.Location}";
                ty += 4;
                _fontRenderer.Draw(_spriteBatch, loc, new Vector2(cx, ty), green);
                ty += lh;
            }

            string desc = LazyMsg(@"text\english\game\quests.msg", ref _questsMsgTried, ref _questsMsg)?.GetText(q.Description)
                          ?? $"quest {q.Description}";
            bool done = gv >= q.CompletedThreshold;
            foreach (string wrapped in _fontRenderer.WrapText($"{number}. {desc}{(done ? " (done)" : "")}", 350))
            {
                if (ty > bottomLimit - lh)
                    break;
                _fontRenderer.Draw(_spriteBatch, wrapped, new Vector2(cx + 6, ty), done ? dim : green);
                ty += lh;
            }
            number++;
            shown++;
        }

        if (shown == 0)
            _fontRenderer.Draw(_spriteBatch, "(no current quests)", new Vector2(cx, ty), dim);

        // P100 (Point 4): the HOLODISKS section — each unlocked disk (its gvar is non-zero) listed by name.
        // ported from fallout2-ce src/pipboy.cc (the Archives holodisk list, gvar != 0 gate, :894-946).
        var disks = Holodisks().Where(h => (_scriptHost?.GlobalVars.GetValueOrDefault(h.Gvar) ?? 0) != 0).ToList();
        if (disks.Count > 0 && ty <= bottomLimit - lh * 2)
        {
            ty += lh + 4;
            _fontRenderer.Draw(_spriteBatch, "HOLODISKS", new Vector2(cx, ty), green);
            ty += lh;
            foreach (Formats.Holodisk h in disks)
            {
                if (ty > bottomLimit - lh)
                    break;
                string name = LazyMsg(@"text\english\game\pipboy.msg", ref _pipboyMsgTried, ref _pipboyMsg)?.GetText(h.Name)
                              ?? $"holodisk {h.Name}";
                _fontRenderer.Draw(_spriteBatch, name, new Vector2(cx + 6, ty), green);
                ty += lh;
            }
        }
    }

    /// <summary>The automap dot colour for an object by FID type, shared by the full-window
    /// automap and the Pip-Boy mini-map (P20-M1/M2). Dead critters / untyped objects → null.
    /// Walls/scenery match the engine's IN-GAME _colorTable (automap.cc:537/541 — wall
    /// _colorTable[992] = pure green, high-detail scenery [480] = dark green). DOCUMENTED
    /// DIVERGENCE: the engine's in-game map hides critters + items (motion-sensor only) and
    /// paints the dude red; we show them (red/yellow) with a WHITE dude so enemies + loot +
    /// you are all distinguishable — a more useful PoC map.</summary>
    /// <summary>Mark the tiles around <paramref name="tile"/> as explored for the automap fog —
    /// the walked-tile accumulation of object.cc obj_set_seen()/_obj_process_seen() (P71): the
    /// dude's current tile plus its neighbor spread (the disc of radius <see cref="AutomapSeenRadius"/>)
    /// go into <see cref="_seenTiles"/>, so the fog reflects where the dude has actually BEEN.
    /// An object then shows on the automap iff its tile is in the set (DrawAutomap/DrawPipboyMiniMap).</summary>
    private void RevealAround(int tile)
    {
        if (tile < 0)
            return;
        int cx = tile % 200, cy = tile / 200;
        for (int dy = -AutomapSeenRadius - 1; dy <= AutomapSeenRadius + 1; dy++)
            for (int dx = -AutomapSeenRadius - 1; dx <= AutomapSeenRadius + 1; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || x >= 200 || y < 0 || y >= 200)
                    continue;
                int t = y * 200 + x;
                if (Formats.Hex.HexGrid.Distance(tile, t) <= AutomapSeenRadius)
                    _seenTiles.Add(t);
            }
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
            if (_seenTiles.Contains(obj.HexTile) && AutomapColor(obj) is { } col) // OBJECT_SEEN fog (P71)
                Plot(obj.HexTile, col, 2);
        if (_dude is not null)
            Plot(_dude.Dude.HexTile, new Color(255, 255, 255), 3);

        _fontRenderer.Draw(_spriteBatch, "MAP (A: full)", new Vector2(boxX + 4, boxY + 2), new Color(0, 252, 0));
    }

    /// <summary>The full-window automap (P15 M0): the authentic AUTOMAP.FRM (519x480)
    /// centred, with every object on the current elevation plotted as a colored dot
    /// (automap.cc automapRenderInMapWindow projection: ax = 449 − 2·col, ay = 2·row + 8,
    /// col = tile%200, row = tile/200). Colors by FID type; the dude is a bright marker.
    /// Fog-of-war = the walked-tile <see cref="_seenTiles"/> (P71): only objects on explored
    /// tiles plot; the embedded Pip-Boy mini-map (needs automap.db RLE) stays out.</summary>
    // The AUTOMAP.frm baked-in button screen rects (automap.cc): the SCANNER (111,454), CANCEL (277,454)
    // and the hi/lo-detail SWITCH (457,340) — shared by DrawAutomap (label hint) + the input hit-test.
    private (Rectangle Scanner, Rectangle Cancel, Rectangle Detail) AutomapButtons()
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int w = _automapBg?.Width ?? 519, h = _automapBg?.Height ?? 480;
        var o = new Point(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2));
        return (new Rectangle(o.X + 105, o.Y + 450, 24, 22),
                new Rectangle(o.X + 271, o.Y + 450, 24, 22),
                new Rectangle(o.X + 457, o.Y + 340, 42, 74));
    }

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
        {
            if (!_seenTiles.Contains(obj.HexTile) || AutomapColor(obj) is not { } col) // OBJECT_SEEN fog (P71)
                continue;
            // P82: LOW detail shows only walls (the engine's AUTOMAP_WITH_HIGH_DETAILS gate); HIGH = all.
            if (!_automapHighDetail && Fid.Type(obj.Fid) is not ObjectType.Wall)
                continue;
            Plot(obj.HexTile, col, 2);
        }
        // P116 (review H): the Motion Sensor scanner view — every LIVING critter plotted red
        // regardless of the seen-tile fog (automap.cc:524-528, AUTOMAP_WITH_SCANNER +
        // _colorTable[31744]).
        if (_automapScanner)
            foreach (MapObject critter in _solidObjects[_elevation])
                if (Fid.Type(critter.Fid) is ObjectType.Critter && !critter.IsDead && !critter.IsHidden)
                    Plot(critter.HexTile, new Color(248, 0, 0), 2);

        if (_dude is not null)
            Plot(_dude.Dude.HexTile, new Color(255, 255, 255), 3); // the dude marker

        var labelGreen = new Color(0, 252, 0);
        _fontRenderer.Draw(_spriteBatch, $"AUTOMAP — {_currentMapName} (elev {_elevation}, {(_automapHighDetail ? "hi" : "lo")} detail{(_automapScanner ? ", scanner" : "")})",
            new Vector2(o.X + 20, o.Y + 12), labelGreen);
        _fontRenderer.Draw(_spriteBatch, "SCANNER / CANCEL / hi-lo switch — or Esc/A close, H/L detail, PgUp/Dn elev",
            new Vector2(o.X + 20, o.Y + h - 24), new Color(0, 168, 0));
    }

    /// <summary>The options / pause menu (P12 M2): the authentic OPBASE.FRM (164x217)
    /// centred, with the actions the engine's showOptions offers (minus Preferences,
    /// which we have no system for). Drawn over the paused world.</summary>
    // The options/pause menu rows, top to bottom — index is the dispatch key shared by
    // DrawOptions (render), OptionsRowAt (hit-test) and the click handler.
    private static readonly string[] OptionsItems =
        ["Save Game  (S)", "Load Game  (L)", "Preferences  (P)", "Main Menu  (M)", "Quit  (Q)", "Resume  (Esc)"];

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
        // P80: capture a world-only thumbnail (deferred to the next Draw) + drop the stale cached texture.
        _pendingThumbnailPath = SlotThumbPath(slot);
        if (_slotThumbs.Remove(slot, out Texture2D? old))
            old?.Dispose();
        RefreshSlotInfos();
    }

    // P80: per-slot thumbnail (a sidecar PNG next to the slot's JSON) + a lazily-loaded texture cache.
    private string SlotThumbPath(int slot) => Path.ChangeExtension(SlotPath(slot), ".png");
    private readonly Dictionary<int, Texture2D?> _slotThumbs = [];

    private Texture2D? SlotThumbnail(int slot)
    {
        if (_slotThumbs.TryGetValue(slot, out Texture2D? cached))
            return cached;
        Texture2D? tex = null;
        string path = SlotThumbPath(slot);
        if (File.Exists(path))
        {
            try { using FileStream fs = File.OpenRead(path); tex = Texture2D.FromStream(GraphicsDevice, fs); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { tex = null; }
        }
        _slotThumbs[slot] = tex;
        return tex;
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
            int selSlot = hovered >= 0 ? hovered : 0;
            Formats.SlotInfo sel = _slotInfos[selSlot];
            int bx = p.X + 396, by = p.Y + 258;
            if (sel.Occupied && !sel.VersionMismatch)
            {
                _fontRenderer.Draw(_spriteBatch, sel.Character, new Vector2(bx, by), green);
                _fontRenderer.Draw(_spriteBatch, $"Level {sel.Level}", new Vector2(bx, by + _fontRenderer.LineHeight), green);
                _fontRenderer.Draw(_spriteBatch, sel.Map, new Vector2(bx, by + 2 * _fontRenderer.LineHeight), green);
                _fontRenderer.Draw(_spriteBatch, sel.Date, new Vector2(bx, by + 3 * _fontRenderer.LineHeight), green);
            }

            // P80: the selected slot's screenshot thumbnail in the LSGAME preview area (window-local 340,39).
            var preview = new Rectangle(p.X + 340, p.Y + 39, ThumbW, ThumbH);
            if (sel.Occupied && !sel.VersionMismatch && SlotThumbnail(selSlot) is { } thumb)
            {
                _spriteBatch.Draw(thumb, preview, Color.White);
            }
            else
            {
                _spriteBatch.Draw(_panelPixel, preview, new Color(0, 0, 0, 220)); // empty preview recess
                string none = sel.Occupied ? "(no preview)" : "";
                _fontRenderer.Draw(_spriteBatch, none, new Vector2(preview.X + 70, preview.Y + 60), gray);
            }
        }
    }

    // ====================================================================
    //  Called-shot click dialog (P49-M1)
    // ====================================================================
    //
    // Replaces the V-key aim CYCLE with the engine's called-shot window (combat.cc:5476
    // calledShotSelectHitLocation): V opens it, 1-9 / click a location picks, Esc/cancel closes.
    // P119: the authentic art — interface FRM 118 background (504×309), the target critter's
    // ANIM_CALLED_SHOT_PIC (64) portrait at (168,31), location names from combat.msg
    // (1000 + 10·alias + location, hitLocationGetName :5437), live to-hit rendered with the
    // FRM-82 digit strip (_print_tohit :5419), cancel button FRMs 8/9 at (210,268). The location
    // feeds the unchanged TryAttack(target, AimLocation) path. DIVERGENCES (documented): the
    // window sets the PERSISTENT AimLocation (Hexwaste's pre-attack aim mode) instead of arming a
    // single attack, so key 9 = "uncalled" clears the aim (no such row in the original); the text
    // list remains as the missing-art residual.

    private bool _aimDialogOpen;
    // The critter the window was opened over — fo2ce passes it into the window; the pic,
    // name set, and to-hit all key off it (null = no live %, generic names).
    private MapObject? _aimDialogTarget;

    private const int CalledShotBgFrmId = 118;      // the window background (combat.cc:5510)
    private const int CalledShotDigitsFrmId = 82;   // the 9×17 to-hit digit strip (:5422)
    private const int CalledShotCancelUpFrmId = 8;  // small red button (:5535)
    private const int CalledShotCancelDownFrmId = 9;
    private const int CalledShotW = 504, CalledShotH = 309; // CALLED_SHOT_WINDOW_* (:52)
    private static readonly int[] CalledShotRowY = [122, 188, 251, 316]; // _call_ty (:1886)

    // The dialog rows -> AimLocation values, in the engine's CALLED.frm button order
    // (head/eyes/right-arm/right-leg, then torso/groin/left-arm/left-leg — combat.cc:1894-1907),
    // then uncalled. AimNames/LocationPenalty are indexed by the AimLocation value.
    private static readonly int[] AimDialogOrder = { 0, 6, 2, 4, 3, 7, 1, 5, 8 };

    private void OpenAimDialog()
    {
        _aimDialogOpen = true;
        // Capture the target once, like the window's critter param (combat.cc:5476); the dialog
        // must not retarget as the mouse moves underneath it.
        _aimDialogTarget = _hoveredObject is { } h && h != _dude?.Dude
            && Fid.PidType(h.Pid) == (int)ObjectType.Critter ? h : null;
        // P119 probe (new prefix — golden-safe): STATE-only art status, no game text.
        Console.WriteLine($"calledshot-art: bg={InterfaceFrm(CalledShotBgFrmId) is not null}"
            + $" digits={InterfaceFrm(CalledShotDigitsFrmId) is not null}"
            + $" cancel={InterfaceFrm(CalledShotCancelUpFrmId) is not null}"
            + $" pic={(_aimDialogTarget is { } t ? CalledShotPic(t) is not null : false)}"
            + $" alias={(_aimDialogTarget is { } t2 ? _artIndex.CritterAlias(t2.Fid) : -1)}");
    }

    /// <summary>The target's called-shot portrait (ANIM_CALLED_SHOT_PIC = 64, art suffix "na"),
    /// or null when the art doesn't ship for this critter (fo2ce just skips the blit, :5525).</summary>
    private Texture2D? CalledShotPic(MapObject critter)
    {
        try { return _frmCache.GetTexture(Fid.Build(ObjectType.Critter, Fid.Index(critter.Fid), 64)); }
        catch (Exception) { return null; }
    }

    /// <summary>The hit-location display name: combat.msg 1000 + 10·alias + location
    /// (hitLocationGetName, combat.cc:5437) read from the user's game data at runtime;
    /// the Hexwaste-authored AimName is the missing-msg fallback.</summary>
    private string HitLocationName(MapObject? critter, int loc)
    {
        if (critter is not null
            && LazyMsg(@"text\english\game\combat.msg", ref _combatMsgTried, ref _combatMsg) is { } msg
            && msg.GetText(1000 + 10 * _artIndex.CritterAlias(critter.Fid) + loc) is { } name)
            return name;
        return AimName(loc);
    }

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
        _audio?.PlaySfx("ICSXXXX1"); // the pick confirmation (combat.cc:5636)
        Log($"Aiming: {AimName(AimLocation)}.");
    }

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

    /// <summary>Row 0..7 under the mouse, 8 for cancel, −1 otherwise (art layout); falls back to
    /// the text rows when the background FRM is missing.</summary>
    private int CalledShotHitAt(int mx, int my)
    {
        if (InterfaceFrm(CalledShotBgFrmId) is null)
            return AimDialogRowAt(mx, my);
        for (int i = 0; i < 8; i++)
            if (CalledShotButtonRect(i).Contains(mx, my))
                return i;
        return CalledShotCancelRect().Contains(mx, my) ? 8 : -1;
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
            && CalledShotHitAt(mouse.X, mouse.Y) is int clicked && clicked >= 0)
        {
            bool artMode = InterfaceFrm(CalledShotBgFrmId) is not null;
            if (artMode && clicked == 8)
                _aimDialogOpen = false; // the cancel button closes without changing the aim
            else
                SelectAimRow(clicked);
        }
    }

    /// <summary>Render a 0-99 to-hit with the FRM-82 digit strip (9×17 glyphs at x=9·digit;
    /// the 6-wide dash at x=108 doubled for "no value") — _print_tohit (combat.cc:5419).</summary>
    private void DrawCalledShotToHit(Texture2D digits, int x, int y, int? toHit)
    {
        if (toHit is { } pct)
        {
            int clamped = Math.Clamp(pct, 0, 99);
            _spriteBatch.Draw(digits, new Vector2(x, y), new Rectangle(9 * (clamped / 10), 0, 9, 17), Color.White);
            _spriteBatch.Draw(digits, new Vector2(x + 9, y), new Rectangle(9 * (clamped % 10), 0, 9, 17), Color.White);
        }
        else
        {
            _spriteBatch.Draw(digits, new Vector2(x, y), new Rectangle(108, 0, 6, 17), Color.White);
            _spriteBatch.Draw(digits, new Vector2(x + 9, y), new Rectangle(108, 0, 6, 17), Color.White);
        }
    }

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
        var normal = new Color(0, 252, 0);   // _colorTable[992] (green)
        var hot = new Color(252, 0, 0);      // _colorTable[31744] (red, _draw_loc_on_)
        for (int i = 0; i < 8; i++)
        {
            int loc = AimDialogOrder[i];
            int rowY = p.Y + CalledShotRowY[i % 4] - 86;
            string name = HitLocationName(_aimDialogTarget, loc);
            Color c = i == hovered || loc == AimLocation ? hot : normal;
            if (i < 4)
                _fontRenderer.Draw(_spriteBatch, name, new Vector2(p.X + 74, rowY), c);
            else
                _fontRenderer.Draw(_spriteBatch, name,
                    new Vector2(p.X + 431 - _fontRenderer.MeasureWidth(name), rowY), c);
            int? pct = _aimDialogTarget is not null ? _combat.PreviewToHit(_aimDialogTarget, loc) : null;
            DrawCalledShotToHit(digits, p.X + (i < 4 ? 33 : 453), rowY, pct);
        }

        bool cancelPressed = mouse.LeftButton == ButtonState.Pressed
            && CalledShotCancelRect().Contains(mouse.X, mouse.Y);
        if (InterfaceFrm(cancelPressed ? CalledShotCancelDownFrmId : CalledShotCancelUpFrmId) is { } cancel)
            _spriteBatch.Draw(cancel, new Vector2(p.X + 210, p.Y + 268), Color.White);
    }

    /// <summary>The pre-P119 text list, kept as the missing-art residual.</summary>
    private void DrawAimDialogFallback()
    {
        _panelPixel ??= CreatePixel();
        Rectangle p = AimDialogPanelRect();
        _spriteBatch.Draw(_panelPixel, p, new Color(8, 16, 8, 240));
        var green = new Color(0, 252, 0);
        var hot = new Color(252, 252, 84);
        _fontRenderer!.Draw(_spriteBatch, "AIM - pick a hit location (1-9 / click, Esc cancel)",
            new Vector2(p.X + 12, p.Y + 8), Color.LightGray);
        int hovered = AimDialogRowAt(Mouse.GetState().X, Mouse.GetState().Y);
        // P52-M4: the LIVE per-location to-hit % against the captured target, via the same
        // CombatEngine math the attack uses; shown alongside the static penalty.
        MapObject? aimTarget = _aimDialogTarget;
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
            panels.Add(new(InventoryPanelX(), "Inventory - click/1-9 use/equip, Shift drop, Esc close",
                _dudeInventory, ItemPanelKind.Inventory, null));
        }
        return panels;
    }

    private void DrawItemPanels()
    {
        if (_fontRenderer is null)
            return;
        DrawInventoryWindow(); // P67: the INVBOX paperdoll backdrop (behind the list); no-op if the art is absent
        DrawItemWindow();      // P86: the loot/barter/trade FRM backdrop; no-op if the art is absent
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

    private enum DragSource { None, Row, WeaponSlot, ArmorSlot, LeftWeaponSlot }
    private MapObject? _dragItem;       // the item currently being dragged, or null
    private DragSource _dragSource;     // where the drag started
    private Point _dragStart;           // the press position (to tell a tap from a drag)

    // P67: the authentic INVBOX.frm paperdoll window (interface FID 48, 499x377). Loaded lazily on
    // the first live Draw; headless it stays null so the panel falls back to the original boxes-beside-
    // the-list layout — which is what the --panel-click / --drag-equip goldens exercise (they use the
    // same CurrentItemPanels X + logical slot args), so those goldens are byte-identical.
    private Texture2D? _invBox;
    private bool _invBoxTried;
    private const int InvBoxW = 499, InvBoxH = 377;
    // Window-local layout (inventory.cc): item list at 44,38; armor slot 154,183; right-hand (our single
    // weapon) slot 245,286; left-hand slot 154,286 (decorative — single-weapon model); PC body 176,37.
    private const int InvBoxListLocalX = 44;
    private static readonly Rectangle InvBoxArmorLocal = new(154, 183, 90, 61);
    private static readonly Rectangle InvBoxWeaponLocal = new(245, 286, 90, 61);
    private static readonly Rectangle InvBoxLeftLocal = new(154, 286, 90, 61);
    private static readonly Rectangle InvBoxBodyLocal = new(176, 37, 60, 100);

    /// <summary>Top-left of the centred INVBOX window when its art is loaded; null = the fallback
    /// boxes-beside-the-list layout (headless / art absent).</summary>
    private Point? InvBoxOrigin() => _invBox is null
        ? null
        : new Point(Math.Max(0, (GraphicsDevice.Viewport.Width - InvBoxW) / 2),
                    Math.Max(0, (GraphicsDevice.Viewport.Height - InvBoxH) / 2));

    /// <summary>The dude inventory list's X: inside the INVBOX window when its art is up, else x=40
    /// (the boxes layout the harness/goldens use).</summary>
    private int InventoryPanelX() => InvBoxOrigin() is { } o ? o.X + InvBoxListLocalX : 40;

    // The two equip-slot rects: on the INVBOX paperdoll when its art is up, else free-column boxes (x=420).
    private Rectangle WeaponSlotRect() => InvBoxOrigin() is { } o
        ? InvBoxWeaponLocal with { X = o.X + InvBoxWeaponLocal.X, Y = o.Y + InvBoxWeaponLocal.Y }
        : new Rectangle(420, 96, 90, 60);
    private Rectangle ArmorSlotRect() => InvBoxOrigin() is { } o
        ? InvBoxArmorLocal with { X = o.X + InvBoxArmorLocal.X, Y = o.Y + InvBoxArmorLocal.Y }
        : new Rectangle(420, 176, 90, 60);
    // P81: the LEFT-hand (item1) slot — the engine's second ready weapon slot. Off-window fallback sits
    // below the right-hand box so it never overlaps the weapon@96 / armor@176 boxes the goldens fall back to.
    private Rectangle LeftWeaponSlotRect() => InvBoxOrigin() is { } o
        ? InvBoxLeftLocal with { X = o.X + InvBoxLeftLocal.X, Y = o.Y + InvBoxLeftLocal.Y }
        : new Rectangle(420, 256, 90, 60);

    // The INVBOX DONE button (inventory.cc: window-local 437,329 15x16; padded for an easier click).
    // Only meaningful when the INVBOX art is up — the fallback boxes layout has no DONE button.
    private Rectangle? InvBoxDoneRect() => InvBoxOrigin() is { } o ? new Rectangle(o.X + 432, o.Y + 324, 26, 24) : null;

    /// <summary>P111: the LOOT window's DONE button — fo2ce creates it window-local at (476,331) 15x16
    /// (inventory.cc:1052-1066, fires KEY_ESCAPE); padded for an easier click like InvBoxDoneRect.
    /// Null when the loot art is absent (headless fallback boxes have no DONE button).</summary>
    private Rectangle? LootDoneRect() =>
        _lootContainer is not null && ItemWindowArt() is { Strip: false } w
            ? new Rectangle(w.Origin.X + 473, w.Origin.Y + 328, 22, 22)
            : null;

    // ====================================================================
    //  P86: authentic LOOT / BARTER / TRADE window art (FID 114/111/420)
    // ====================================================================
    //
    // The dark full-panel boxes are replaced by the real interface chrome, following the proven P67
    // INVBOX pattern: lazy-load on the first live Draw; headless the texture stays null so the panels
    // fall back to the boxes layout (which is what the --panel-click / barter / loot goldens exercise),
    // so those goldens are byte-identical. Filenames + dims dumped from master.dat (not guessed):
    //   loot.frm  (FID 114) 537x376 — a centred container window (left=you/empty, right=container scroller)
    //   barter.frm(FID 111) 640x191 — a bottom strip: your list (left), merchant list (right), offer tables
    //   trade.frm (FID 420) 640x190 — the party-member trade strip, same layout
    // DOCUMENTED DIVERGENCE (the P67 one): Hexwaste is a TEXT list, not the engine's 64x48 icon grid, so a
    // long item name can extend past the narrow art slot; and the offer-table mechanic isn't modelled
    // (barter is direct click-to-buy/sell). The text-fallback path keeps the existing geometry/goldens.
    private Texture2D? _lootBox, _barterBox, _tradeBox;
    private bool _lootBoxTried, _barterBoxTried, _tradeBoxTried;
    private const int LootBoxW = 537, LootBoxH = 376;
    private const int TradeStripW = 640, TradeStripH = 191;

    /// <summary>The active loot/barter/trade FRM backdrop + its top-left screen placement (and whether it
    /// is a bottom strip), or null when no such panel is up OR the art is absent (headless) — in which case
    /// the item panels fall back to the dark text boxes and every existing golden stays byte-identical.</summary>
    private (Texture2D Tex, Point Origin, bool Strip)? ItemWindowArt()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        if (_lootContainer is not null)
        {
            if (!_lootBoxTried) { _lootBoxTried = true; _lootBox = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\loot.frm"); }
            return _lootBox is null ? null
                : (_lootBox, new Point(Math.Max(0, (vw - LootBoxW) / 2), Math.Max(0, (vh - LootBoxH) / 2)), false);
        }
        if (_barterNpc is not null)
        {
            if (!_barterBoxTried) { _barterBoxTried = true; _barterBox = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\barter.frm"); }
            return _barterBox is null ? null
                : (_barterBox, new Point((vw - TradeStripW) / 2, Math.Max(0, vh - TradeStripH)), true);
        }
        if (_tradePartner is not null)
        {
            if (!_tradeBoxTried) { _tradeBoxTried = true; _tradeBox = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\trade.frm"); }
            return _tradeBox is null ? null
                : (_tradeBox, new Point((vw - TradeStripW) / 2, Math.Max(0, vh - TradeStripH)), true);
        }
        return null;
    }

    /// <summary>Window-relative list region (row origin + visible row count) for the panel at the logical
    /// X (40 / 420) when an art window is up, or null → the dark-box fallback. Mapping: x=40 (the "them"
    /// list — container / merchant stock / follower items) → the RIGHT slot; x=420 (your list) → the LEFT
    /// slot. Both the renderer (DrawItemList) and the hit-test (ItemRowRect) go through this so they agree.</summary>
    private (int X, int Y, int Rows)? ItemPanelRegion(int logicalX)
    {
        if (ItemWindowArt() is not { } w)
            return null;
        if (w.Strip) // barter/trade bottom strip: two tall side lists flanking the centre offer tables
            return logicalX == 40
                ? (w.Origin.X + 462, w.Origin.Y + 26, 5)   // their list (merchant/follower — right tall slot)
                : (w.Origin.X + 92, w.Origin.Y + 26, 5);   // your list (left tall slot)
        return (w.Origin.X + 180, w.Origin.Y + 38, 9);     // loot: the container list across the central scrollers
    }

    /// <summary>Items shown per page in the current item panels — the FRM barter/trade STRIP only fits 5
    /// rows where loot/inventory fit 9, so paging, the number keys and the click hit-test must all stride
    /// by THIS (not the bare ItemRowsPerPage) or rows 5..8 of every page are silently skipped. Derived
    /// from ItemPanelRegion so it tracks the rendered row count; headless (no art) → ItemRowsPerPage.</summary>
    private int PanelPageRows() => ItemPanelRegion(40)?.Rows ?? ItemRowsPerPage;

    // The object-flag bit a weapon slot wields into: WeaponLeft → left hand, else the right hand.
    private static int SlotHandBit(Formats.Combat.EquipSlot slot) =>
        slot == Formats.Combat.EquipSlot.WeaponLeft ? MapObject.FlagInLeftHand : MapObject.FlagInRightHand;

    private Formats.Combat.EquipSlot? EquipSlotAt(int mx, int my) =>
        WeaponSlotRect().Contains(mx, my) ? Formats.Combat.EquipSlot.Weapon          // right hand first (unchanged)
        : LeftWeaponSlotRect().Contains(mx, my) ? Formats.Combat.EquipSlot.WeaponLeft
        : ArmorSlotRect().Contains(mx, my) ? Formats.Combat.EquipSlot.Armor
        : null;

    /// <summary>The inventory list row (0..8) under a point, or -1. Shares ItemRowRect with
    /// the renderer + TryClickItemPanel so they never disagree (panel x = 40).</summary>
    private int InventoryRowAt(int mx, int my)
    {
        for (int row = 0; row < ItemRowsPerPage; row++)
            if (ItemRowRect(InventoryPanelX(), row).Contains(mx, my))
                return row;
        return -1;
    }

    /// <summary>The dude's item currently in a slot — the wielded weapon in that HAND, or the worn armor.</summary>
    private MapObject? EquippedInSlot(Formats.Combat.EquipSlot slot) => slot switch
    {
        Formats.Combat.EquipSlot.Armor => _dudeInventory.FirstOrDefault(i => i.IsWorn),
        _ => _dudeInventory.FirstOrDefault(i => (i.Flags & SlotHandBit(slot)) != 0 && SafeProto(i.Pid)?.Weapon is not null),
    };

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
                _dragSource = slot switch
                {
                    Formats.Combat.EquipSlot.Armor => DragSource.ArmorSlot,
                    Formats.Combat.EquipSlot.WeaponLeft => DragSource.LeftWeaponSlot,
                    _ => DragSource.WeaponSlot,
                };
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
            else if (_dragSource is DragSource.LeftWeaponSlot && overSlot != Formats.Combat.EquipSlot.WeaponLeft)
                UnequipSlot(Formats.Combat.EquipSlot.WeaponLeft);
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

        if (slot is Formats.Combat.EquipSlot.Weapon or Formats.Combat.EquipSlot.WeaponLeft)
        {
            // P81: wield into THIS hand only — vacate just this hand's bit across the bag, clear both bits
            // on the item (it leaves any hand), set this hand's bit. For the right hand with no left-hand
            // weapon ever present, this reduces to the old clear-both/set-right → byte-identical.
            int bit = SlotHandBit(slot);
            foreach (MapObject other in _dudeInventory)
                other.Flags &= ~bit;
            item.Flags &= ~(MapObject.FlagInLeftHand | MapObject.FlagInRightHand);
            item.Flags |= bit;
            Log($"You ready the {ObjectName(item)} ({(slot == Formats.Combat.EquipSlot.WeaponLeft ? "left" : "right")} hand).");
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
        if (slot is Formats.Combat.EquipSlot.Weapon or Formats.Combat.EquipSlot.WeaponLeft)
        {
            int bit = SlotHandBit(slot); // P81: clear only this hand's bit
            foreach (MapObject it in _dudeInventory.Where(i => (i.Flags & bit) != 0).ToList())
            {
                it.Flags &= ~bit;
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
    /// pure-inventory view (loot/barter/trade have no equip slots). On the INVBOX paperdoll the slot
    /// art is baked into the window, so draw just the equipped icon (no box/label).</summary>
    private void DrawEquipSlots()
    {
        if (_fontRenderer is null || !_inventoryOpen || _lootContainer is not null
            || _tradePartner is not null || _barterNpc is not null)
            return;
        _panelPixel ??= CreatePixel();
        bool onWindow = InvBoxOrigin() is not null;
        bool rightActive = _activeHand == MapObject.FlagInRightHand;
        // P81: two ready weapon hands; the ACTIVE one (which fires) is marked '*'. Armor below them.
        DrawEquipSlot(WeaponSlotRect(), rightActive ? "R-HAND*" : "R-HAND", EquippedInSlot(Formats.Combat.EquipSlot.Weapon), onWindow);
        DrawEquipSlot(LeftWeaponSlotRect(), rightActive ? "L-HAND" : "L-HAND*", EquippedInSlot(Formats.Combat.EquipSlot.WeaponLeft), onWindow);
        DrawEquipSlot(ArmorSlotRect(), "ARMOR", EquippedInSlot(Formats.Combat.EquipSlot.Armor), onWindow);
        // A bright border round the active hand so it's clear which weapon fires.
        DrawRectOutline(rightActive ? WeaponSlotRect() : LeftWeaponSlotRect(), new Color(252, 252, 84));
        if (_dragItem is { } dragged) // the ghost icon follows the cursor (from the last Update mouse)
            DrawItemIcon(dragged, new Rectangle(_previousMouse.X - 14, _previousMouse.Y - 11, 28, 22));
    }

    /// <summary>P67: the authentic INVBOX.frm window + the dude paperdoll, drawn behind the item list
    /// when the inventory is open. Lazy-loads the art once; if absent, leaves the fallback layout.</summary>
    private void DrawInventoryWindow()
    {
        if (_fontRenderer is null || !_inventoryOpen || _lootContainer is not null
            || _tradePartner is not null || _barterNpc is not null)
            return;
        if (!_invBoxTried)
        {
            _invBoxTried = true;
            _invBox = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\INVBOX.frm");
        }
        if (InvBoxOrigin() is not { } o)
            return; // art absent -> the fallback boxes layout (DrawEquipSlots at x=420)
        _spriteBatch.Draw(_invBox, new Rectangle(o.X, o.Y, InvBoxW, InvBoxH), Color.White);
        // The dude paperdoll (its current art reflects worn armor), scaled into the body view (176,37,60,100).
        if (_dude?.Dude is { } dude)
        {
            try
            {
                Texture2D doll = _frmCache.GetTexture(dude.Fid, 0, 1); // frame 0, a forward-facing rotation
                var view = new Rectangle(o.X + InvBoxBodyLocal.X, o.Y + InvBoxBodyLocal.Y, InvBoxBodyLocal.Width, InvBoxBodyLocal.Height);
                float scale = Math.Min((float)view.Width / doll.Width, (float)view.Height / doll.Height);
                var size = new Point((int)(doll.Width * scale), (int)(doll.Height * scale));
                _spriteBatch.Draw(doll, new Rectangle(view.X + (view.Width - size.X) / 2, view.Y + (view.Height - size.Y) / 2, size.X, size.Y), Color.White);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
            }
        }
    }

    // A 1px rectangle border (P81 active-hand marker).
    private void DrawRectOutline(Rectangle r, Color c)
    {
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(r.X, r.Y, r.Width, 1), c);
        _spriteBatch.Draw(_panelPixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        _spriteBatch.Draw(_panelPixel, new Rectangle(r.X, r.Y, 1, r.Height), c);
        _spriteBatch.Draw(_panelPixel, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }

    private void DrawEquipSlot(Rectangle rect, string label, MapObject? item, bool onWindow = false)
    {
        if (!onWindow) // the boxes-fallback chrome; on the INVBOX paperdoll the slot art is baked in
        {
            _spriteBatch.Draw(_panelPixel, rect, new Color(8, 8, 8, 230));
            var border = new Color(0, 252, 0);
            _spriteBatch.Draw(_panelPixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), border);
            _spriteBatch.Draw(_panelPixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), border);
            _spriteBatch.Draw(_panelPixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), border);
            _spriteBatch.Draw(_panelPixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), border);
            _fontRenderer!.Draw(_spriteBatch, label, new Vector2(rect.X + 4, rect.Y - 22), Color.LightGray);
        }
        if (item is not null)
            DrawItemIcon(item, new Rectangle(rect.X + 8, rect.Y + 6, rect.Width - 16, rect.Height - 12));
        else if (!onWindow)
            _fontRenderer!.Draw(_spriteBatch, "(empty)", new Vector2(rect.X + 8, rect.Y + rect.Height / 2 - 8), Color.Gray);
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
        // P67: the inventory list inside the INVBOX window renders in the authentic left column (window-
        // relative Y, narrow — the paperdoll sits at local x=176). Only matches the inventory panel's X
        // (loot/barter/trade stay x=40/420); headless _invBox is null so this never fires -> goldens
        // byte-identical. DOCUMENTED: the readable text rows are wider than the engine's icon column, so a
        // long name can extend toward the paperdoll (Hexwaste is a text-list inventory, not an icon grid).
        if (InvBoxOrigin() is { } o && x == o.X + InvBoxListLocalX)
            return new Rectangle(x, o.Y + 40 + displayRow * lineHeight, 128, lineHeight);
        // P86: loot/barter/trade rows sit in the FRM window's slot region (render == hit-test).
        if (ItemPanelRegion(x) is { } reg)
            return new Rectangle(reg.X, reg.Y + displayRow * lineHeight, 150, lineHeight);
        int rowY = 60 + 8 + lineHeight + 6 + displayRow * lineHeight;
        return new Rectangle(x + 6, rowY - 4, 360 - 12, lineHeight);
    }

    /// <summary>Draws the panel and returns the y just below it (P24 — the weight readout sits there).</summary>
    /// <summary>P86: draw the active loot/barter/trade FRM backdrop behind the item lists. No-op when no
    /// such panel is up or the art is absent (→ the dark-box fallback, byte-identical goldens).</summary>
    private void DrawItemWindow()
    {
        if (ItemWindowArt() is not { } w)
            return;
        int wide = w.Strip ? TradeStripW : LootBoxW;
        int high = w.Strip ? TradeStripH : LootBoxH;
        _spriteBatch.Draw(w.Tex, new Rectangle(w.Origin.X, w.Origin.Y, wide, high), Color.White);

        // P111: the LOOT window's DONE button is a separate little-red-button FRM the engine overlays
        // at (476,331) next to the baked-in DONE plate (inventory.cc:1052-1066 with interface FID 8) —
        // loot.frm itself ships without the button, which is why it looked missing.
        if (!w.Strip && _lootContainer is not null)
        {
            if (!_lilRedTried)
            {
                _lilRedTried = true;
                _lilRedUp = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\lilredup.frm");
            }
            if (_lilRedUp is not null)
                _spriteBatch.Draw(_lilRedUp, new Vector2(w.Origin.X + 476, w.Origin.Y + 331), Color.White);
        }
    }

    private Texture2D? _lilRedUp;
    private bool _lilRedTried;

    private int DrawItemList(string title, List<MapObject> items, int x,
        Func<MapObject, int>? price = null)
    {
        _panelPixel ??= CreatePixel();
        int lineHeight = Math.Max(_fontRenderer!.LineHeight, 26);

        // P86: on the loot/barter/trade FRM window, render the rows in the slot region (no dark box — the
        // window art is the backdrop). Geometry via ItemPanelRegion so render == ItemRowRect hit-test.
        // Headless ItemWindowArt is null → this never fires → the dark-box path below keeps the goldens.
        if (ItemPanelRegion(x) is { } reg)
        {
            var rowCol = new Color(0, 252, 0);
            int start0 = _panelPage * reg.Rows; // P89-fix: stride by the rendered row count, not 9 (bug_001)
            if (items.Count == 0)
                _fontRenderer.Draw(_spriteBatch, "(empty)", new Vector2(reg.X, reg.Y), Color.Gray);
            for (int row = 0; row < reg.Rows; row++)
            {
                int gi = start0 + row;
                if (gi >= items.Count)
                    break;
                MapObject item = items[gi];
                Rectangle rr = ItemRowRect(x, row);
                DrawItemIcon(item, new Rectangle(rr.X, rr.Y, 18, Math.Min(rr.Height - 4, 18)));
                string c = item.StackCount > 1 ? $" x{item.StackCount}" : "";
                string tag = price is null ? "" : $" ${price(item)}";
                _fontRenderer.Draw(_spriteBatch, $"{row + 1}.{ObjectName(item)}{c}{tag}", new Vector2(rr.X + 20, rr.Y), rowCol);
            }
            if (items.Count > reg.Rows)
                _fontRenderer.Draw(_spriteBatch, "PgUp/Dn", new Vector2(reg.X, reg.Y + reg.Rows * lineHeight + 1), Color.Gray);
            return reg.Y + reg.Rows * lineHeight;
        }
        int panelWidth = 360;
        int start = _panelPage * ItemRowsPerPage;
        int shown = Math.Clamp(items.Count - start, 0, ItemRowsPerPage);
        int panelHeight = (Math.Max(shown, 1) + 2) * lineHeight + 16;
        int y = 60;

        // P67: the inventory list inside the INVBOX window — no dark box (the window art is the bg), rows
        // positioned via ItemRowRect (so render == hit-test) in the authentic narrow left column. Headless
        // _invBox is null so this never fires (the loot/barter/trade panels keep the box layout below).
        if (InvBoxOrigin() is { } wo && x == wo.X + InvBoxListLocalX)
        {
            var col = new Color(0, 252, 0);
            if (items.Count == 0)
                _fontRenderer.Draw(_spriteBatch, "(empty)", new Vector2(x, wo.Y + 40), Color.Gray);
            for (int row = 0; row < ItemRowsPerPage; row++)
            {
                int gi = start + row;
                if (gi >= items.Count)
                    break;
                MapObject item = items[gi];
                Rectangle rr = ItemRowRect(x, row);
                DrawItemIcon(item, new Rectangle(rr.X, rr.Y, 22, rr.Height - 2));
                string c = item.StackCount > 1 ? $" x{item.StackCount}" : "";
                _fontRenderer.Draw(_spriteBatch, $"{ObjectName(item)}{c}", new Vector2(rr.X + 24, rr.Y), col);
            }
            if (items.Count > ItemRowsPerPage)
                _fontRenderer.Draw(_spriteBatch, "PgUp/PgDn", new Vector2(x, wo.Y + 44 + ItemRowsPerPage * lineHeight), Color.Gray);
            return wo.Y + InvBoxH;
        }

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
        int rows = PanelPageRows(); // P89-fix: the strip pages by 5, so its last page is items.Count/5
        foreach (ItemPanel panel in CurrentItemPanels())
            max = Math.Max(max, (Math.Max(panel.Items.Count, 1) - 1) / rows);
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
        int rows = PanelPageRows(); // P89-fix: only the visible rows are clickable (5 on the strip, bug_001)
        foreach (ItemPanel panel in CurrentItemPanels())
        {
            int start = _panelPage * rows;
            for (int row = 0; row < rows; row++)
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
