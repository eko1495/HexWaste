using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

// P100 (Point 1): the victory endgame slideshow — the win condition. A script's endgame_slideshow
// (0x8146) drives ScriptHost.EndgameSlideshowRequested → ShowEndgameSlideshow(), which parses
// data\endgame.txt, keeps every slide whose controlling GVAR currently equals its value (the karma /
// town-fate consumer — scripts already set those GVARs via set_global_var), and plays them in file
// order over black with the narrator voice-over (M6, from sound\speech\narrator\<base>.acm in the DAT)
// + subtitles. After the last slide it hands off to the endgame "movie" — the credits scroll (M7).
// ported from fallout2-ce src/endgame.cc endgamePlaySlideshow()/endgameEndingRenderStaticScene()/
// endgameEndingVoiceOverInit()/endgameEndingSubtitlesLoad()/endgameEndingRefreshSubtitles().
public sealed partial class ViewerGame
{
    private sealed class EndgameSlide
    {
        public required string FrmPath;
        public string? NarratorBase;        // sound\speech\narrator\<base>.acm + text\<lang>\cuts\<base>.txt
        public List<string> Subtitles = [];
        public int SubtitleCharCount;       // Σ subtitle length, for the speech-duration scaling (endgame.cc:695)
        public int[] TimingsMs = [];         // cumulative end-time per subtitle line
        public double SpeechDurationMs;      // >0 once the ACM is playing (endgame.cc speechGetDuration)
        public bool Panning;                 // art 327 == DP.FRM (the desert pan; dead in vanilla — see note)
        public int Direction;
    }

    private List<EndgameSlide>? _endgameSlides;
    private int _endgameIndex;
    private double _endgameSlideClock;      // ms elapsed on the current slide
    private int _endgameSubLine;            // current subtitle line index
    private readonly Dictionary<string, Texture2D?> _endgameTexCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>endgame_slideshow (0x8146): build + start the victory slideshow.</summary>
    private void ShowEndgameSlideshow()
    {
        BuildEndgameSlides();
        _endgameIndex = 0;
        _menu = MenuState.Endgame;
        if (_endgameSlides is null || _endgameSlides.Count == 0)
        {
            EndEndgameSlideshow(); // no matching slide → straight to the credits bookend
            return;
        }
        StartCurrentSlide();
    }

    /// <summary>endgame_movie (0x8148): the endgame "movie" is not a video — it's the credits scroll
    /// (endgame.cc:234 endgamePlayMovie → creditsOpen). Route it straight there.</summary>
    private void ShowEndgameMovie() => EndEndgameSlideshow();

    /// <summary>Parse data\endgame.txt and keep the slides whose controlling GVAR == value
    /// (endgame.cc:217, strict equality; slides play in file order, all matches play).</summary>
    private void BuildEndgameSlides()
    {
        _endgameSlides = [];
        const string cfg = @"data\endgame.txt";
        if (!_vfs.Exists(cfg))
            return;
        foreach (Formats.Endgame.EndgameEnding rec in Formats.Endgame.EndgameEndingFile.Parse(_vfs.ReadAllBytes(cfg)))
        {
            if (_scriptHost.GlobalVars.GetValueOrDefault(rec.Gvar, 0) != rec.Value)
                continue;
            var slide = new EndgameSlide
            {
                FrmPath = _artIndex.GetFrmPath(Formats.Fid.Build(Formats.ObjectType.Interface, rec.ArtNum)),
                NarratorBase = rec.VoiceOverBaseName,
                Panning = rec.ArtNum == 327,
                Direction = rec.Direction,
            };
            LoadEndgameSubtitles(slide, rec.VoiceOverBaseName);
            _endgameSlides.Add(slide);
        }
    }

    /// <summary>Load text\english\cuts\&lt;base&gt;.txt subtitles (endgame.cc:764: each line is
    /// "value:text", the value ignored, text after ':' kept) + seed cumulative per-line timings at
    /// 0.08 s/char (endgame.cc:686 fallback); StartCurrentSlide rescales them to the real ACM duration.</summary>
    private void LoadEndgameSubtitles(EndgameSlide slide, string baseName)
    {
        string path = $@"text\english\cuts\{baseName}.txt";
        var subs = new List<string>();
        if (_vfs.Exists(path))
        {
            foreach (string raw in Encoding.ASCII.GetString(_vfs.ReadAllBytes(path)).Replace("\r", "").Split('\n'))
            {
                int colon = raw.IndexOf(':');
                if (colon < 0)
                    continue;
                subs.Add(raw[(colon + 1)..].TrimEnd());
            }
        }
        slide.Subtitles = subs;
        slide.SubtitleCharCount = subs.Sum(s => s.Length);
        slide.TimingsMs = new int[subs.Count];
        ScaleSubtitleTimings(slide, 0.08); // 0.08 s/char fallback (endgame.cc:687)
    }

    /// <summary>Recompute cumulative subtitle end-times at the given seconds-per-character.</summary>
    private static void ScaleSubtitleTimings(EndgameSlide slide, double secondsPerChar)
    {
        int t = 0;
        for (int i = 0; i < slide.Subtitles.Count; i++)
        {
            t += (int)(slide.Subtitles[i].Length * secondsPerChar * 1000.0); // trunc (endgame.cc:695)
            slide.TimingsMs[i] = t;
        }
    }

    /// <summary>Begin the current slide: reset clocks + play the narrator ACM (M6). When it plays, rescale
    /// the subtitle timings to speechDuration/charCount (endgame.cc:695); else keep the 0.08 s/char fallback.</summary>
    private void StartCurrentSlide()
    {
        _endgameSlideClock = 0;
        _endgameSubLine = 0;
        if (_endgameSlides is null || _endgameIndex >= _endgameSlides.Count)
            return;
        EndgameSlide slide = _endgameSlides[_endgameIndex];
        slide.SpeechDurationMs = 0;
        if (_audio is not null && slide.NarratorBase is { } b)
        {
            string acm = $@"sound\speech\narrator\{b}.acm";
            if (_vfs.Exists(acm))
            {
                double dur = _audio.PlaySpeechData(_vfs.ReadAllBytes(acm));
                if (dur > 0)
                {
                    slide.SpeechDurationMs = dur;
                    if (slide.SubtitleCharCount > 0)
                        ScaleSubtitleTimings(slide, dur / slide.SubtitleCharCount / 1000.0);
                }
            }
        }
    }

    private Texture2D? GetEndgameTexture(string frmPath)
    {
        if (!_endgameTexCache.TryGetValue(frmPath, out Texture2D? tex))
        {
            tex = LoadFrmWithSiblingPalette(frmPath); // per-slide sibling palette (endgame.cc:735)
            _endgameTexCache[frmPath] = tex;
        }
        return tex;
    }

    /// <summary>Draw the current slide over black + its active subtitle line (word-wrapped ~540 px,
    /// centred near the bottom of the 640×480 frame). ported from endgame.cc endgameEndingRefreshSubtitles.</summary>
    private void DrawEndgame()
    {
        if (_endgameSlides is null || _endgameIndex >= _endgameSlides.Count)
            return;
        EndgameSlide slide = _endgameSlides[_endgameIndex];
        Viewport vp = GraphicsDevice.Viewport;
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black);

        (int ox, int oy) = MenuOrigin();
        if (GetEndgameTexture(slide.FrmPath) is { } tex)
        {
            // Art 327 (DP.FRM) is the wide panning-desert scene, referenced only by commented endgame.txt
            // rows → dead in vanilla; we blit its left 640 px statically (a full pan is a deferred layer).
            int srcW = slide.Panning ? Math.Min(tex.Width, 640) : Math.Min(tex.Width, 640);
            _spriteBatch.Draw(tex, new Rectangle(ox, oy, 640, 480),
                new Rectangle(0, 0, srcW, tex.Height), Color.White);
        }

        if (_fontRenderer is not null && _endgameSubLine < slide.Subtitles.Count)
            DrawEndgameSubtitle(slide.Subtitles[_endgameSubLine], ox, oy);
    }

    private void DrawEndgameSubtitle(string text, int ox, int oy)
    {
        if (_fontRenderer is null || text.Length == 0)
            return;
        List<string> wrapped = WrapByWidth(text, 540);
        int lh = _fontRenderer.LineHeight;
        float y = oy + 480 - lh * wrapped.Count - 4;
        foreach (string line in wrapped)
        {
            _fontRenderer.Draw(_spriteBatch, line,
                new Vector2(ox + 320 - _fontRenderer.MeasureWidth(line) / 2f, y), new Color(224, 224, 224));
            y += lh;
        }
    }

    /// <summary>Greedy word-wrap to a pixel width (endgame.cc uses wordWrap at 540 px).</summary>
    private List<string> WrapByWidth(string text, float maxWidth)
    {
        var lines = new List<string>();
        var cur = new StringBuilder();
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length > 0 && _fontRenderer!.MeasureWidth(candidate) > maxWidth)
            {
                lines.Add(cur.ToString());
                cur.Clear();
                cur.Append(word);
            }
            else
            {
                cur.Clear();
                cur.Append(candidate);
            }
        }
        if (cur.Length > 0)
            lines.Add(cur.ToString());
        return lines;
    }

    /// <summary>Advance the slide clock + subtitle line; end the slide on any key/click, on the voice-over /
    /// subtitles finishing, or on the 3 s static timeout (endgame.cc:490-505).</summary>
    private void UpdateEndgame(double elapsedMs, KeyboardState k, MouseState mouse)
    {
        if (_endgameSlides is null || _endgameIndex >= _endgameSlides.Count)
        {
            EndEndgameSlideshow();
            return;
        }
        EndgameSlide slide = _endgameSlides[_endgameIndex];
        _endgameSlideClock += elapsedMs;
        while (_endgameSubLine < slide.TimingsMs.Length && _endgameSlideClock > slide.TimingsMs[_endgameSubLine])
            _endgameSubLine++;

        bool speechEnded = slide.SpeechDurationMs > 0 && _endgameSlideClock > slide.SpeechDurationMs;
        bool subtitlesEnded = slide.Subtitles.Count > 0 && _endgameSubLine >= slide.Subtitles.Count;
        bool timedOut = slide.SpeechDurationMs <= 0 && slide.Subtitles.Count == 0 && _endgameSlideClock > 3000;
        bool click = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        bool anyKey = k.GetPressedKeys().Any(key => !_previousKeyboard.IsKeyDown(key));

        if (click || anyKey || speechEnded || subtitlesEnded || timedOut)
        {
            _audio?.StopSpeech();
            _endgameIndex++;
            if (_endgameSlides is null || _endgameIndex >= _endgameSlides.Count)
                EndEndgameSlideshow();
            else
                StartCurrentSlide();
        }
    }

    // ---- M8: the death-ending narration on the DEATH screen -------------
    private List<string>? _deathNarrationLines;
    private bool _deathNarrationTried;

    /// <summary>Select + load the death-ending narration once, when the death screen first shows. Display-only
    /// (never logged), so the combat goldens that reach game-over stay byte-identical. ported from
    /// fallout2-ce src/endgame.cc endgameSetupDeathEnding + src/main.cc showDeath (word-wrap 560 px).</summary>
    private void EnsureDeathNarration()
    {
        if (_deathNarrationTried)
            return;
        _deathNarrationTried = true;
        const string cfg = @"data\enddeath.txt";
        if (!_vfs.Exists(cfg))
            return;
        var recs = Formats.Endgame.EndgameDeathEndingFile.Parse(_vfs.ReadAllBytes(cfg));
        var rng = new Random(_dudeXp * 31 + _dudeLevel + 1); // stable within a session; no golden captures it
        string pick = Formats.Endgame.EndgameDeathEndingFile.Select(
            recs, Formats.Endgame.EndgameDeathReason.Death,
            g => _scriptHost.GlobalVars.GetValueOrDefault(g, 0),
            _ => false, _dudeLevel, (lo, hi) => rng.Next(lo, hi + 1));
        string baseName = pick[(pick.LastIndexOfAny(['\\', '/']) + 1)..];
        string subPath = $@"text\english\cuts\{baseName}.txt";
        if (!_vfs.Exists(subPath))
            return;
        var text = new StringBuilder();
        foreach (string raw in Encoding.ASCII.GetString(_vfs.ReadAllBytes(subPath)).Replace("\r", "").Split('\n'))
        {
            int colon = raw.IndexOf(':');
            if (colon < 0)
                continue;
            if (text.Length > 0)
                text.Append(' ');
            text.Append(raw[(colon + 1)..].Trim());
        }
        if (_fontRenderer is not null && text.Length > 0)
            _deathNarrationLines = WrapByWidth(text.ToString(), 560);
        if (_audio is not null && _vfs.Exists($@"sound\speech\narrator\{baseName}.acm"))
            _audio.PlaySpeechData(_vfs.ReadAllBytes($@"sound\speech\narrator\{baseName}.acm"));
    }

    /// <summary>Draw the death narration subtitle near the bottom of the death screen.</summary>
    private void DrawDeathNarration()
    {
        EnsureDeathNarration();
        if (_fontRenderer is null || _deathNarrationLines is null)
            return;
        (int ox, int oy) = MenuOrigin();
        int lh = _fontRenderer.LineHeight;
        float y = oy + 480 - lh * _deathNarrationLines.Count - 8;
        foreach (string line in _deathNarrationLines)
        {
            _fontRenderer.Draw(_spriteBatch, line,
                new Vector2(ox + 320 - _fontRenderer.MeasureWidth(line) / 2f, y), new Color(224, 224, 224));
            y += lh;
        }
    }

    /// <summary>After the last slide, hand off to the endgame "movie" — the credits scroll bookend.</summary>
    private void EndEndgameSlideshow()
    {
        _audio?.StopSpeech();
        _endgameSlides = null;
        _menu = MenuState.Credits;
        _creditsScroll = 0;
    }
}
