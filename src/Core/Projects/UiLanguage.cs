using System.Globalization;

namespace KuroakiGimmick.Core;

/// <summary>设备级界面语言偏好，独立于谱面文本与数值解析的区域设置。</summary>
public static class UiLanguage
{
    /// <summary>自动选择、英文和简体中文在偏好文件中使用的语言键。</summary>
    public const string Auto = "auto", English = "en", Chinese = "zh-CN";
    // 在 Program 将数值解析切换到固定区域设置之前捕获系统语言。
    /// <summary>根据初始化时的系统界面文化选择的语言。</summary>
    public static string SystemLanguage { get; } = FromCulture(CultureInfo.CurrentUICulture.Name);

    /// <summary>以 zh 开头的文化使用简体中文，其余使用英文。</summary>
    /// <param name="name">文化名称，可为空。</param>
    /// <returns>中文文化返回简体中文键，其余返回英文键。</returns>
    public static string FromCulture(string? name) =>
        name?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true ? Chinese : English;

    /// <summary>将支持的语言别名规范化为界面语言键。</summary>
    /// <param name="value">保存的语言名称或别名。</param>
    /// <returns>规范化的语言设置键。</returns>
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "zh" or "zh-cn" or "zh-hans" => Chinese,
        "en" or "en-us" or "en-gb" => English,
        _ => Auto
    };

    /// <summary>解析保存的语言选择，自动模式使用系统语言。</summary>
    /// <param name="value">保存的语言名称或别名。</param>
    /// <returns>当前实际用于显示的语言键。</returns>
    public static string Resolve(string? value) => Normalize(value) is var language && language != Auto ? language : SystemLanguage;
}
