using System.Reflection;
using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 应用资产和用户日志的位置策略。只发现资源根目录，不根据歌曲名称决定行为。
/// </summary>
public static class Paths
{
    /// <summary>稳定版本的应用版本号。</summary>
    public const string Version = "0.1.5";
    /// <summary>稳定版本的构建编号。</summary>
    public const string BuildNumber = "18.5";
    /// <summary>无程序集构建标识时使用的稳定版显示文本。</summary>
    public const string StableBuildRevision = "v0.1.5 / 18.5";
    /// <summary>
    /// UI/CLI 显示的构建标识来自程序集 InformationalVersion。CI 可注入 "Nightly / &lt;sha&gt;"，
    /// 正式/本地构建缺省为 StableBuildRevision；避免每次发布还要在多个源码文件里手改版本字符串。
    /// </summary>
    public static string BuildRevision { get; } = ResolveBuildRevision();
    static string ResolveBuildRevision()
    {
        string? value = typeof(Paths).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Trim();
        return string.IsNullOrWhiteSpace(value) ? StableBuildRevision : value;
    }
    /// <summary>资源根目录：优先用可执行文件旁的 Assets，找不到才回退到 bundle 外的共享副本；返回值不保证存在。</summary>
    public static string Assets
    {
        get
        {
#if KG_SWITCH
            return Path.Combine(KuroakiGimmick.Native.SwitchPlatform.Root, "Assets");
#elif KG_MOBILE
            return KuroakiGimmick.Native.MobilePlatform.Assets;
#else
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
#endif
        }
    }

#if KG_SWITCH
    /// <summary>平台可写根目录中的默认导出目录。</summary>
    public static string Output => Path.Combine(KuroakiGimmick.Native.SwitchPlatform.Root, "Exports");
    /// <summary>平台可写根目录中的 UserData，保存日志与设备设置。</summary>
    public static string LogDirectory => Path.Combine(KuroakiGimmick.Native.SwitchPlatform.Root, "UserData");
#elif KG_MOBILE
    /// <summary>平台可写根目录中的默认导出目录。</summary>
    public static string Output => Path.Combine(KuroakiGimmick.Native.MobilePlatform.Root, "Exports");
    /// <summary>平台可写根目录中的 UserData，保存日志与设备设置。</summary>
    public static string LogDirectory => Path.Combine(KuroakiGimmick.Native.MobilePlatform.Root, "UserData");
#else
    /// <summary>用户文档目录下的默认导出位置，显式选择的目录优先。</summary>
    public static string Output => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KuroakiGimmick", "Exports");
    /// <summary>日志、设置与图片导入暂存都在这里。用本地应用数据目录而非漫游目录，这些内容不应跟着用户账户同步。</summary>
    public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KuroakiGimmick");
#endif
    /// <summary>在应用资源根目录下解析指定公共资源子目录。</summary>
    /// <param name="name">共享资源子目录名。</param>
    /// <returns>位于资源根目录下的完整目录名。</returns>
    public static string SharedAssetDirectory(string name) => Path.Combine(Assets, name);
}
