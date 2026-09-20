using System.Globalization;

namespace KuroakiGimmick.Core;

/// <summary>UI language is a device preference; it never changes chart or numeric culture.</summary>
public static class UiLanguage
{
    public const string Auto = "auto", English = "en", Chinese = "zh-CN";
    // Capture before Program makes numeric parsing invariant.
    public static string SystemLanguage { get; } = FromCulture(CultureInfo.CurrentUICulture.Name);

    public static string FromCulture(string? name) =>
        name?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true ? Chinese : English;

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "zh" or "zh-cn" or "zh-hans" => Chinese,
        "en" or "en-us" or "en-gb" => English,
        _ => Auto
    };

    public static string Resolve(string? value) => Normalize(value) is var language && language != Auto ? language : SystemLanguage;
}
