using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 应用资产和用户日志的位置策略。只发现资源根目录，不根据歌曲名称决定行为。
/// </summary>
public static class Paths
{
    public const string Version = "0.1.4";
    public const string BuildNumber = "17.5";
    public const string BuildRevision = "v0.1.4 / 17.5";
    /// <summary>资源根目录：优先用可执行文件旁的 Assets，找不到才回退到 bundle 外的共享副本；返回值不保证存在。</summary>
    public static string Assets
    {
        get
        {
            string local = Path.Combine(AppContext.BaseDirectory, "Assets");
            if (Directory.Exists(local))
            {
                return local;
            }
            // 已发布的 macOS 应用把 Assets 放在 .app bundle 旁边共用一份，向上找到第一层 .app 再取其父目录。
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && dir.Parent != null)
                {
                    return Path.Combine(dir.Parent.FullName, "Assets");
                }
            }
            return local;
        }
    }

    /// <summary>导出的默认落点，在“我的文档”下；只是初值，用户指定的路径优先。</summary>
    public static string Output => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KuroakiGimmick", "Exports");
    /// <summary>日志、设置与图片导入暂存都在这里。用本地应用数据目录而非漫游目录，这些内容不应跟着用户账户同步。</summary>
    public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KuroakiGimmick");
    public static string SharedAssetDirectory(string name) => Path.Combine(Assets, name);
}

