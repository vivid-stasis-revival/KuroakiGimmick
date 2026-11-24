using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>读取欢迎页的可选本地提示，资源缺失或无效时仍可启动。</summary>
public static class FunTips
{
    /// <summary>单条提示允许的最大字符数。</summary>
    public const int MaxLength = 512;
    /// <summary>加载提示资源，无法取得资源时返回空列表。</summary>
    /// <param name="path">可选的本地提示文件路径。</param>
    /// <returns>可展示的提示数组，缺少可用来源时为空。</returns>
    public static string[] Load(string? path = null)
    {
        try
        {
            path ??= Path.Combine(Paths.Assets, "tips.json");
            if (File.Exists(path) && new FileInfo(path).Length <= 1024 * 1024)
                return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        try
        {
            using var stream = typeof(FunTips).Assembly.GetManifestResourceStream("KuroakiGimmick.Tips.json");
            if (stream != null) return Parse(new StreamReader(stream).ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }
        return [];
    }
    /// <summary>从 JSON 数组提取并筛选可展示的提示文本。</summary>
    /// <param name="json">提示列表的 JSON 文本。</param>
    /// <returns>通过长度与内容筛选的提示数组。</returns>
    /// <exception cref="JsonException">输入不是有效的提示 JSON 数组。</exception>
    public static string[] Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Tips must be a string array.");
        return document.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!.Trim()).Where(s => s.Length is > 0 and <= MaxLength)
            .Distinct(StringComparer.Ordinal).ToArray();
    }
    /// <summary>从可用提示中选择一条，空列表时返回 null。</summary>
    /// <param name="tips">可供选择的提示列表。</param>
    /// <param name="previous">上次展示的提示。</param>
    /// <returns>选中的提示，无可用项时为 null。</returns>
    public static string? Pick(IReadOnlyList<string> tips, string? previous)
    {
        if (tips.Count == 0) return null;
        int index = -1;
        for (int i = 0; i < tips.Count; i++) if (tips[i] == previous)
        {
            index = i;
            break;
        }
        int pick = Random.Shared.Next(tips.Count - (index >= 0 && tips.Count > 1 ? 1 : 0));
        if (tips.Count > 1 && index >= 0 && pick >= index) pick++;
        return tips[pick];
    }
}
