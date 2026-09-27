using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 原版 start_song 的房间关联数据；不创建专属加载器/渲染器。
/// 手动 room、外部 FX 和对象 manifest 仍由 GameFxProfile 按优先级处理。
/// </summary>
public static class RoomAssociations
{
    internal sealed class Catalog
    {
        public Catalog()
        {
        }

        public Dictionary<string, string> Presets { get; set; } = [];
        public string[] Unassociated { get; set; } = [];
    }

    private static readonly Lazy<Catalog> Data = new(() => Read());

    private static Catalog Read(string? assetsRoot = null)
    {
        var catalog = BundledCatalog.Read<Catalog>(
            "Catalog/room-associations.json", "KuroakiGimmick.RoomAssociations.json", assetsRoot);
        if (catalog.Presets == null || catalog.Unassociated == null || catalog.Presets.Count > 4096 ||
            catalog.Unassociated.Length > 4096 ||
            catalog.Presets.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
        {
            throw new InvalidDataException("Invalid room association catalog.");
        }
        return catalog;
    }

    public static string PresetFor(string songName) => Data.Value.Presets.GetValueOrDefault(songName, "gameplay");
    public static bool IsUnassociated(string songName) => Data.Value.Unassociated.Contains(songName, StringComparer.Ordinal);

    // 隔离的兼容性测试读取另一个资源根目录，不替换共享的 Lazy 实例。
    internal static string PresetFor(string songName, string assetsRoot) => Read(assetsRoot).Presets.GetValueOrDefault(songName, "gameplay");
}
