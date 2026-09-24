using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KuroakiGimmick.Core;

/// <summary>
/// 定义优先级：显式覆盖 → 曲包定义 → 外置共享定义 → 同构建的内嵌定义。
/// 内嵌的是同一份 JSON，不是另一套按曲名分支的执行代码；单独更新程序时，
/// 旧 Assets 缺少新定义也不会丢失扩展 ID 表、时间轴规则和资源契约。
/// </summary>
public static class GimmickCatalog
{
    private static readonly Lazy<IReadOnlyList<(string Name, string Json, GimmickDefinition Data)>> builtIns = new(ReadBuiltIns);

    private static IReadOnlyList<(string Name, string Json, GimmickDefinition Data)> ReadBuiltIns()
    {
        Assembly assembly = typeof(GimmickCatalog).Assembly;
        var definitions = new List<(string Name, string Json, GimmickDefinition Data)>();
        foreach (string name in assembly.GetManifestResourceNames().Where(name => name.StartsWith("KuroakiGimmick.Assets.Gimmicks.",
            StringComparison.Ordinal) && name.EndsWith(".manifest.json", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using Stream stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Embedded definition is unavailable: " + name);
            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();
            var data = GimmickDefinitionReader.Read(json);
            GimmickValidation.Validate(data);
            definitions.Add((name, json, data));
        }
        // 打包失误必须暴露出来。空目录会重现 r17 的静默丢失。
        if (definitions.Count == 0)
        {
            throw new InvalidDataException("The executable contains no built-in object definitions. Rebuild with the project resource items.");
        }
        return definitions;
    }

    /// <summary>
    /// 按显式路径 → 曲包 gimmick-object.json → 共享定义 → 同构建内嵌定义的顺序返回第一个命中的来源。
    /// 文件存在即视为权威：读取或解析失败要向上抛出，不能悄悄退到下一级配置。
    /// 只有按 profile 别名扫描共享目录时才跳过损坏文件，因为那是在遍历与本次查找无关的定义。
    /// </summary>
    public static GimmickDefinitionSource? Resolve(string objectName, string? songRoot = null, string? explicitPath = null,
        string profile = "auto", string? assetsRoot = null)
    {
        assetsRoot ??= Paths.Assets;
        GimmickDefinitionSource FileSource(string path, string origin)
        {
            string full = Path.GetFullPath(path);
            return new(full, Path.GetDirectoryName(full)!, ResourceFiles.ReadText(full), origin);
        }
        if (explicitPath != null)
        {
            if (!File.Exists(explicitPath))
            {
                throw new FileNotFoundException("Object definition is missing.", explicitPath);
            }
            return FileSource(explicitPath, "explicit");
        }
        if (songRoot != null && File.Exists(Path.Combine(songRoot, "gimmick-object.json")))
        {
            return FileSource(ResourceFiles.ContainedFile(songRoot, "gimmick-object.json"), "song");
        }
        string shared = Path.Combine(assetsRoot, "Gimmicks");
        bool alias = profile is not ("auto" or "core");
        if (alias && Directory.Exists(shared))
        {
            foreach (string path in Directory.EnumerateDirectories(shared).Select(directory => Path.Combine(directory, "manifest.json"))
                .Where(File.Exists).Order(StringComparer.Ordinal))
            {
                GimmickDefinitionSource candidate;
                GimmickDefinition? data;
                try
                {
                    candidate = FileSource(ResourceFiles.ContainedFile(shared, Path.GetRelativePath(shared,
                        path).Replace(Path.DirectorySeparatorChar, '/')), "shared");
                    data = AppJson.Deserialize<GimmickDefinition>(candidate.Json, ViewerProject.Json);
                }
                catch (Exception ex) when (ResourceFiles.IsResourceError(ex))
                {
                    // 另一份无关定义损坏，不应遮蔽别的文件里有效的别名。
                    continue;
                }
                if (data?.ProfileAliases?.Contains(profile, StringComparer.OrdinalIgnoreCase) == true)
                {
                    return candidate;
                }
            }
        }
        if (!alias)
        {
            if (!Regex.IsMatch(objectName, @"^[A-Za-z0-9_]+$"))
            {
                return null;
            }
            if (File.Exists(Path.Combine(shared, objectName, "manifest.json")))
            {
                // 外置覆盖只要存在就是权威来源。文件损坏时报错，不能悄悄换成内嵌定义。
                return FileSource(ResourceFiles.ContainedFile(shared, objectName + "/manifest.json"), "shared");
            }
        }
        foreach (var item in builtIns.Value)
        {
            if (alias ? item.Data.ProfileAliases.Contains(profile, StringComparer.OrdinalIgnoreCase) : item.Data.ObjectName == objectName)
            {
                return new("embedded://" + item.Name, Path.Combine(shared, item.Data.ObjectName), item.Json, "embedded");
            }
        }
        if (alias)
        {
            throw new InvalidDataException("Unknown object profile alias: " + profile);
        }
        return null;
    }

    /// <summary>
    /// 二进制解码和会话加载共用同一发现策略。扩展表按原始顺序读取，不能排序、
    /// 去重或等到 GPU 初始化后再补，否则 ID 129 之后的事件会在加载时被误解码。
    /// assetsRoot 只用于隔离测试，不修改全局目录或用户文件。
    /// </summary>
    public static string[] ReadExtraMods(string objectName, string? songRoot = null, string? explicitPath = null, string profile = "auto",
        string? assetsRoot = null)
    {
        try
        {
            var source = Resolve(objectName, songRoot, explicitPath, profile, assetsRoot);
            if (source == null)
            {
                return [];
            }
            var definition = GimmickDefinitionReader.Read(source.Json);
            if ((profile is "auto" or "core") && definition.ObjectName != objectName || definition.ExtraMods == null
                || definition.ExtraMods.Length > 127)
            {
                return [];
            }
            return definition.ExtraMods;
        }
        catch (Exception ex) when (ResourceFiles.IsResourceError(ex))
        {
            // 完整加载器会报告具体的定义错误；这里不臆造 mod 名填补。
            return [];
        }
    }
}
