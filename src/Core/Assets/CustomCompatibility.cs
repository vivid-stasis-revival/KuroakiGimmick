namespace KuroakiGimmick.Core;

/// <summary>
/// 从 obj_custom_gimmick 与 obj_base_gimmick.updateMods 核对出的对象作用域。
/// 记录原版根本不注册的参数，让预览把它们如实报成 no-op，而不是自行补一套行为。
/// </summary>
public static class CustomCompatibility
{
    /// <summary>判断名称是否属于已知侧边实例回调。</summary>
    /// <param name="name">候选回调名。</param>
    /// <returns>属于已登记侧边回调时为 true。</returns>
    public static bool IsSideCallback(string name) => KenomaGMK.Runtime.Timeline.CustomScope.IsSideCallback(name);
    /// <summary>受 ENABLE_STARPARTICLE 开关约束的星体控制名。</summary>
    public static readonly HashSet<string> Stars = KenomaGMK.Runtime.Timeline.CustomScope.Stars;
    /// <summary>原版对象专用而自定义对象未注册的控制名。</summary>
    public static readonly HashSet<string> NativeOnly = KenomaGMK.Runtime.Timeline.CustomScope.NativeOnly;
    /// <summary>给出当前对象和配置使控制无效的已知原因，无匹配时为 null。</summary>
    /// <param name="name">演出控制名。</param>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="project">当前工程的路径与呈现配置。</param>
    /// <returns>无效原因；未匹配已知情况时为 null。</returns>
    public static string? NoOpReason(string name, Chart chart, ViewerProject project)
    {
        return chart.ObjectName == "obj_custom_gimmick"
            ? KenomaGMK.Runtime.Timeline.CustomScope.NoOpReason(name,(key,fallback)=>SongFiles.Config(project,key,fallback))
            : null;
    }
}
