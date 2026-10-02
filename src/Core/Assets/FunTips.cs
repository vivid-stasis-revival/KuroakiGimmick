using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>Optional, local welcome-page copy. Invalid/missing files never prevent startup.</summary>
public static class FunTips
{
    public const int MaxLength = 512;
    public static string[] Load(string? path = null)
    {
        try
        {
            path ??= Path.Combine(Paths.Assets, "tips.json");
            if (File.Exists(path) && new FileInfo(path).Length <= 1024 * 1024)
                return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        try
        {
            using var stream = typeof(FunTips).Assembly.GetManifestResourceStream("KuroakiGimmick.Tips.json");
            if (stream != null) return Parse(new StreamReader(stream).ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        return [];
    }
    public static string[] Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Tips must be a string array.");
        return document.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!.Trim()).Where(s => s.Length is > 0 and <= MaxLength)
            .Distinct(StringComparer.Ordinal).ToArray();
    }
    public static string? Pick(IReadOnlyList<string> tips, string? previous)
    {
        if (tips.Count == 0) return null;
        int index = -1;
        for (int i = 0; i < tips.Count; i++) if (tips[i] == previous) { index = i; break; }
        int pick = Random.Shared.Next(tips.Count - (index >= 0 && tips.Count > 1 ? 1 : 0));
        if (tips.Count > 1 && index >= 0 && pick >= index) pick++;
        return tips[pick];
    }
}
