using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KuroakiGimmick.UI;

namespace KuroakiGimmick.Core;

internal static class LocalizationSelfTest
{
    internal static void Check(Action<bool, string> check)
    {
        string previous = L.Language;
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        string directory = Path.Combine(Path.GetTempPath(), "kuroaki-i18n-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var english = L.ReadCatalog(UiLanguage.English);
            var chinese = L.ReadCatalog(UiLanguage.Chinese);
            check(english.Count > 500 && english.Keys.Order().SequenceEqual(chinese.Keys.Order()),
                "both embedded UI catalogs contain the same complete key set");
            bool formatsValid = true;
            string[] Fields(string text) => Regex.Matches(text, @"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}")
                .Select(m => m.Value).Order().ToArray();
            foreach (string key in english.Keys)
            {
                foreach (string value in new[] { english[key], chinese[key] })
                {
                    formatsValid &= !string.IsNullOrWhiteSpace(value) && Fields(key).SequenceEqual(Fields(value));
                    if (Fields(key).Length > 0) _ = CompositeFormat.Parse(value);
                }
            }
            check(formatsValid, "translations preserve argument indexes, counts and numeric format specifiers");
            var uiRunes = english.Values.Concat(chinese.Values).Append("中文English")
                .SelectMany(text => text.EnumerateRunes()).Where(r => !Rune.IsWhiteSpace(r)).Select(r => r.ToString()).Distinct();
            foreach (string name in new[] { "ui-sans", "ui-sans-bold" })
            {
                using var stream = typeof(LocalizationSelfTest).Assembly.GetManifestResourceStream($"KuroakiGimmick.Fonts.{name}.json")!;
                var metrics = JsonSerializer.Deserialize<Graphics.Fonts.HelpAtlasMetrics>(stream)!;
                check(uiRunes.All(metrics.Glyphs.ContainsKey), $"embedded {name} covers every Chinese and English UI character");
            }
            check(UiLanguage.FromCulture("zh-TW") == UiLanguage.Chinese && UiLanguage.FromCulture("fr-FR") == UiLanguage.English
                && UiLanguage.Normalize(" EN-us ") == UiLanguage.English && UiLanguage.Normalize("zh-Hans") == UiLanguage.Chinese,
                "Chinese system locales, English fallback and supported aliases resolve correctly");

            L.SetLanguage("en");
            check(L.Get("PLAY") == "PLAY" && L.Get("关闭") == "Close", "English translates existing Chinese UI too");
            L.SetLanguage("zh-CN");
            check(L.Get("PLAY") == "播放" && L.Get("关闭") == "关闭", "Chinese UI switches without restart");
            check(L.Format($"Selected {2} clips on {"PLAY"}.") == "已选中 PLAY 上的 2 个片段。",
                "translated templates reorder arguments without translating user identifiers");
            check(L.Get("unregistered / 自定义 {n}") == "unregistered / 自定义 {n}", "missing UI keys fall back unchanged");
            check(L.Get("New text (paste multiline text; {n} also starts a new line)").Contains("{n}"),
                "literal subtitle newline markers are not format placeholders");
            L.SetLanguage("en");
            check(L.Get("PLAY") == "PLAY", "switching back does not retain cached Chinese text");
            check(ReferenceEquals(culture, CultureInfo.CurrentCulture) && ReferenceEquals(uiCulture, CultureInfo.CurrentUICulture),
                "language switching leaves numeric and thread cultures unchanged");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            check(L.Format($"Added {"prx"} at beat {1.25:0.######}.") == "Added prx at beat 1.25.",
                "UI formatted beat values remain invariant under comma-decimal locales");

            string file = Path.Combine(directory, "settings.json");
            var project = new ViewerProject();
            string before = JsonSerializer.Serialize(project, ViewerProject.Json);
            var settings = new ViewerSettings { UiLanguage = "zh-CN" };
            settings.Save(project, file);
            check(ViewerSettings.Load(file).UiLanguage == "zh-CN", "language preference survives settings save and reload");
            check(JsonSerializer.Serialize(project, ViewerProject.Json) == before && !before.Contains("uiLanguage", StringComparison.OrdinalIgnoreCase),
                "language is stored only in device settings, never in chart projects");
            File.WriteAllText(file, "{\"UiTheme\":\"Scarlet\"}");
            check(ViewerSettings.Load(file).UiLanguage == UiLanguage.Auto, "old settings without language follow the system");
            File.WriteAllText(file, "{\"UiLanguage\":\"unsupported\"}");
            check(ViewerSettings.Load(file).UiLanguage == UiLanguage.Auto, "invalid saved language falls back safely");
            File.WriteAllText(file, "{\"UiLanguage\":null}");
            check(ViewerSettings.Load(file).UiLanguage == UiLanguage.Auto, "null saved language falls back safely");
        }
        finally
        {
            L.SetLanguage(previous);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
            Directory.Delete(directory, true);
        }
    }
}
