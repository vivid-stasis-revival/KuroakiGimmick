using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 小型内置数据表的统一读取入口：外置文件存在时以外置为准，缺失时读取同构建的内嵌 JSON。
/// 不捕获格式错误或路径越界，避免把用户写错的配置伪装成正常默认值。
/// </summary>
internal static class BundledCatalog
{
    internal static T Read<T>(string relativePath, string resourceName, string? assetsRoot = null) where T : class
    {
        string? path;
        try
        {
            path = ResourceFiles.ContainedFile(assetsRoot ?? Paths.Assets, relativePath);
        }
        catch (FileNotFoundException)
        {
            path = null;
        }

        if (path != null)
        {
            return AppJson.Deserialize<T>(ResourceFiles.ReadText(path), ViewerProject.Json)
                ?? throw new InvalidDataException("Empty catalog: " + path);
        }

        using Stream stream = typeof(BundledCatalog).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException("Missing embedded catalog: " + resourceName);
        return AppJson.Deserialize<T>(stream, ViewerProject.Json)
            ?? throw new InvalidDataException("Empty embedded catalog: " + resourceName);
    }
}
