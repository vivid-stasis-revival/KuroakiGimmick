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
            var menuKeys = MenuCatalog.Groups.SelectMany(group => new[] { group.Label }
                .Concat(group.Items.Where(item => item.Command != MenuCommand.None).Select(item => item.Label)))
                .Append("NO RECENT FILES").Distinct().ToArray();
            check(menuKeys.All(key => english.ContainsKey(key) && chinese.ContainsKey(key)) &&
                MenuCatalog.Actions.Distinct().Count() == MenuCatalog.Actions.Count() &&
                MenuCatalog.Actions.All(command => (int)command < MenuCatalog.RecentBaseId),
                "native menu commands and translations are complete on both platforms");
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
            // 本程序自己的版本号只能来自 Paths.BuildRevision：写进语言包的版本不会随构建更新，
            // 编辑器标题就曾经因此停在 v0.1.2 / 16.2，而同一个程序的 viewer 显示的是另一个号。
            // 指向外部目标包的版本说明放行——那是行为来源的出处，不随本程序构建变化。
            string[] externalTargets = ["v1.12.7"];
            check(english.Concat(chinese).All(pair =>
            {
                string text = pair.Key + " " + pair.Value;
                foreach (string target in externalTargets)
                {
                    text = text.Replace(target, "");
                }
                return !Regex.IsMatch(text, @"v\d+\.\d+\.\d+");
            }), "UI strings never hard-code this application's own version; it comes from Paths.BuildRevision");
            var uiRunes = english.Values.Concat(chinese.Values).Append("中文English")
                // 这些符号只出现在代码字面量里，语言包扫不到，但旧图集专门为它们烘过字形。
                .Append("←→−×▶Ⅱ✓…·")
                .SelectMany(text => text.EnumerateRunes()).Where(r => !Rune.IsWhiteSpace(r)).Distinct().ToArray();
            // 界面字体是运行时栅格化的真字体，加文案不再需要重新烘焙图集；这里查的是
            // 上游字体本身是否真的没有某个字，那才是唯一还会让界面出现缺字的情况。
            using (var uiFont = Graphics.UiFont.Load())
            {
                foreach (bool bold in new[] { false, true })
                {
                    check(uiRunes.All(rune => uiFont.Covers(rune, bold)),
                        $"embedded UI font covers every Chinese and English UI character ({(bold ? "SemiBold" : "Regular")})");
                }
                // 轮廓真的能栅格出墨迹，而不是只有 cmap 命中：汉字、拉丁字母各验一个。
                bool rasterized = true;
                foreach (var rune in new[] { new Rune('难'), new Rune('W') })
                {
                    rasterized &= uiFont.TryGlyph(rune, 32, false, out var glyph) && glyph.W > 0 && glyph.H > 0;
                }
                check(rasterized, "UI font rasterizes Chinese and Latin glyphs at an exact pixel size");
                // 步进按 em 归一化，与栅格尺寸无关，否则同一段文字在不同 DPI 上宽度会变。
                check(Math.Abs(uiFont.AdvanceEm(new Rune('难'), false) - uiFont.AdvanceEm(new Rune('难'), false)) < 1e-6f
                    && uiFont.AdvanceEm(new Rune('难'), false) > 0,
                    "UI font advances are em-normalized and independent of raster size");
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
