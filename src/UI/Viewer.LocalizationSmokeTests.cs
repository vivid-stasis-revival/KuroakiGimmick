using System.Text.Json;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    /// <summary>Exercise both languages without saving preferences or changing the scene.</summary>
    public void SmokeLocalization()
    {
        string language = preferences.UiLanguage;
        bool animations = preferences.UiAnimations, wasSettings = settings, wasEditor = editorMode;
        var session = Current;
        double position = transport.Position;
        string project = JsonSerializer.Serialize(Current.Project, ViewerProject.Json);
        string? vsm = editor?.Vsm.Text;
        preferences.UiAnimations = false;
        try
        {
            foreach (string next in new[] { UiLanguage.English, UiLanguage.Chinese, UiLanguage.English })
            {
                SetUiLanguage(next, persist: false);
                if (L.Language != next || L.Get("PLAY") != (next == UiLanguage.Chinese ? "播放" : "PLAY"))
                    throw new InvalidOperationException("UI language did not update immediately.");
                if (fonts.ChineseInterface != (next == UiLanguage.Chinese))
                    throw new InvalidOperationException("Font did not follow the resolved UI language.");
                const string sample = "KUROAKI 0123456789";
                foreach (bool mono in new[] { false, true })
                {
                    foreach (float fontSize in new[] { 13f, 24f })
                    {
                        float expected;
                        if (next == UiLanguage.English)
                        {
                            var metrics = JsonSerializer.Deserialize<Dictionary<string, Graphics.Fonts.Glyph>>(
                                File.ReadAllText(Path.Combine(Paths.Assets, "Fonts", mono ? "mono.json" : "sans.json")))!;
                            expected = sample.Sum(c => metrics[c.ToString()].advance) * fontSize / 32;
                        }
                        else
                        {
                            using var stream = typeof(Viewer).Assembly.GetManifestResourceStream("KuroakiGimmick.Fonts.ui-sans.json")!;
                            var metrics = JsonSerializer.Deserialize<Graphics.Fonts.HelpAtlasMetrics>(stream)!;
                            expected = sample.Sum(c => metrics.Glyphs[char.IsAsciiDigit(c) ? "0" : c.ToString()].Advance) * fontSize / metrics.EmSize;
                        }
                        if (Math.Abs(fonts.Measure(sample, fontSize, monospaced: mono) - expected) > .001f)
                            throw new InvalidOperationException("UI text did not use original English / Medium Chinese metrics.");
                    }
                }
                foreach (var size in new[] { (1180, 860), (1440, 940) })
                {
                    settings = false;
                    Draw(size.Item1, size.Item2);
                    settings = true;
                    Draw(size.Item1, size.Item2);
                }
                if (!ReferenceEquals(session, Current) || transport.Position != position
                    || JsonSerializer.Serialize(Current.Project, ViewerProject.Json) != project || editor?.Vsm.Text != vsm)
                    throw new InvalidOperationException("Language switching changed the song, playback or editor document.");
            }
        }
        finally
        {
            SetUiLanguage(language, persist: false);
            preferences.UiAnimations = animations;
            settings = wasSettings;
            editorMode = wasEditor;
        }
    }
}
