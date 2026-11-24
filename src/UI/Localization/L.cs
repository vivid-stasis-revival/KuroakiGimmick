using System.Globalization;
using System.Reflection;
using System.Text.Json;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.UI;

/// <summary>仅翻译界面文案，以源字符串为键，未知键按原文显示。谱面标识、文件路径、字幕和可编辑数据保持原值。</summary>
internal static class L
{
    static readonly IReadOnlyDictionary<string, string> English = ReadCatalog(UiLanguage.English);
    static readonly IReadOnlyDictionary<string, string> Chinese = ReadCatalog(UiLanguage.Chinese);
    public static string Language { get; private set; } = UiLanguage.English;

    public static void SetLanguage(string? language) => Language = UiLanguage.Resolve(language);

    internal static IReadOnlyDictionary<string, string> ReadCatalog(string language)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"KuroakiGimmick.Localization.{language}.json")
            ?? throw new InvalidDataException($"Missing UI language catalog: {language}");
        return AppJson.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException($"Empty UI language catalog: {language}");
    }

    public static string Get(string source)
    {
        if (Language == UiLanguage.Chinese && Chinese.TryGetValue(source, out string? translated)) return translated;
        return English.TryGetValue(source, out string? fallback) ? fallback : source;
    }

    /// <summary>先翻译模板再代入变量，保留名称和路径。</summary>
    public static string Format(FormattableString text) =>
        string.Format(CultureInfo.InvariantCulture, Get(text.Format), text.GetArguments());
}
