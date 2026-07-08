namespace Hexwaste.Formats;

/// <summary>
/// The active localization language (fallout2.cfg <c>[system] language</c>, default
/// "english"). fo2ce builds every text path as <c>text\&lt;language&gt;\…</c>
/// (game_movie.cc:345, endgame.cc:600, etc.); Hexwaste has no config file, so the viewer
/// sets this once from its <c>--language</c> flag. A process-wide setting because the
/// Formats path builders (proto/dialog message files) are constructed in many places and
/// threading it through every ctor is disproportionate for a global. Defaults to english,
/// so the shipped english data + the goldens are unaffected. (P131.)
/// </summary>
public static class Localization
{
    public static string Language { get; set; } = "english";

    /// <summary>Rewrite a canonical <c>text\english\…</c> path to the active language
    /// (a no-op for english). Used by every message/cut path builder.</summary>
    public static string Localize(string path) =>
        Language == "english" ? path : path.Replace(@"text\english\", $@"text\{Language}\");
}
