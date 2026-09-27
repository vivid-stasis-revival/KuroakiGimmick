using System.Globalization;
using System.Reflection;
using System.Text.Json;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.UI;

/// <summary>
/// Explicit UI-only translation. Source text is the key; unknown text falls back unchanged.
/// Never call this on chart identifiers, file paths, subtitles or editable user values.
/// </summary>
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

    /// <summary>Translate the template before substitution, so names/paths are never translated.</summary>
    public static string Format(FormattableString text) =>
        string.Format(CultureInfo.InvariantCulture, Get(text.Format), text.GetArguments());
}
