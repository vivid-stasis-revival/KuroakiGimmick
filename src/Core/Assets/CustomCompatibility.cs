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
    public static bool IsSideCallback(string name) => name is "unraveling_sidething" or "sides"
        or "apocalypse_sidething" or "astellion_sidething";
    /// <summary>受 ENABLE_STARPARTICLE 开关约束的星体控制名。</summary>
    public static readonly HashSet<string> Stars = new("starspawner_timer starspd_multiplier starspd_low starspd_high startrans_alpha starchgcol_alpha active_startrans active_starchgcol starchgcol_up_rgb starchgcol_down_rgb".Split(' '));
    /// <summary>原版对象专用而自定义对象未注册的控制名。</summary>
    public static readonly HashSet<string> NativeOnly = new("track_alpha en_whiteoverlay eo_endsat1 eo_endsat2 eo_endsat3 eo_endsat4 eo_endcg sekaisen_arrow_point sekaisen_target_point sekaisen_jacket".Split(' '));
    /// <summary>给出当前对象和配置使控制无效的已知原因，无匹配时为 null。</summary>
    /// <param name="name">演出控制名。</param>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="project">当前工程的路径与呈现配置。</param>
    /// <returns>无效原因；未匹配已知情况时为 null。</returns>
    public static string? NoOpReason(string name, Chart chart, ViewerProject project)
    {
        if (chart.ObjectName != "obj_custom_gimmick") return null;
        if (name == "changeskin" && !SongFiles.Config(project, "ENABLE_SKIN_CHANGE", false))
            return "ENABLE_SKIN_CHANGE is false; enable skin change in the event inspector to preview and export this callback.";
        if (name == "scorealph") return "Source InitMisc registers and initializes scorealph, but no active score or HUD draw reads it.";
        if (name == "ditortedBG_col") return "Source InitDistortBackground registers ditortedBG_col, but the background handler reads the separate ditortedBG_col_rgb value.";
        if (name == "fx_zoom") return "Source Step writes the misspelled FX parameter 'g_ZoomBlurIntensity ' (trailing space); the exported filter only declares 'g_ZoomBlurIntensity'.";
        if (name == "fx_twirl" && !SongFiles.Config(project, "ENABLE_NON_BASE_FX")) return "ENABLE_NON_BASE_FX is false; the original object does not register this twirl control.";
        if (name == "imgalp") return "Original Custom Gimmick registers imgalp_<image ID> only; bare imgalp is unregistered and updateMods skips it.";
        if (NativeOnly.Contains(name)) return "Original Custom Gimmick does not register this native-object parameter; updateMods skips it. Source: obj_base_gimmick / obj_custom_gimmick / native-object Create.";
        if (name == "fx_edge" && !SongFiles.Config(project, "ENABLE_NON_BASE_FX")) return "ENABLE_NON_BASE_FX is false; the original object does not register this filter control.";
        if (name is "df_sideline" or "df_sideline2" or "df_sideline_alpha" or "df_whitebg" or "df_grid_alpha" or "df_grid_top" or "df_grid_bottom"
            && !SongFiles.Config(project, "ENABLE_DF_GRID_AND_SIDELINE")) return "ENABLE_DF_GRID_AND_SIDELINE is false; these drawing controls are not registered.";
        if (Stars.Contains(name) && !SongFiles.Config(project, "ENABLE_STARPARTICLE", false)) return "ENABLE_STARPARTICLE is false; original Custom Gimmick does not register star controls.";
        if (ModCatalog.MusicControlName(name) is "playspeed" or "jumpto_beat" or "jumpto_s" && !SongFiles.Config(project, "ENABLE_MUSIC_CONTROL")) return "ENABLE_MUSIC_CONTROL is false; original music-control callback does nothing.";
        return null;
    }
}
