namespace KuroakiGimmick.Core;

/// <summary>
/// 从 obj_custom_gimmick 与 obj_base_gimmick.updateMods 核对出的对象作用域。
/// 记录原版根本不注册的参数，让预览把它们如实报成 no-op，而不是自行补一套行为。
/// </summary>
public static class CustomCompatibility
{
    public static bool IsSideCallback(string name) => name is "unraveling_sidething" or "sides"
        or "apocalypse_sidething" or "astellion_sidething";
    public static readonly HashSet<string> Stars = new("starspawner_timer starspd_multiplier starspd_low starspd_high startrans_alpha starchgcol_alpha active_startrans active_starchgcol starchgcol_up_rgb starchgcol_down_rgb".Split(' '));
    public static readonly HashSet<string> NativeOnly = new("track_alpha en_whiteoverlay eo_endsat1 eo_endsat2 eo_endsat3 eo_endsat4 eo_endcg sekaisen_arrow_point sekaisen_target_point sekaisen_jacket".Split(' '));
    public static string? NoOpReason(string name, Chart chart, ViewerProject project)
    {
        if (chart.ObjectName != "obj_custom_gimmick") return null;
        if (name == "imgalp") return "Original Custom Gimmick registers imgalp_<image ID> only; bare imgalp is unregistered and updateMods skips it.";
        if (NativeOnly.Contains(name)) return "Original Custom Gimmick does not register this native-object parameter; updateMods skips it. Source: obj_base_gimmick / obj_custom_gimmick / native-object Create.";
        if (name == "fx_edge" && !SongFiles.Config(project, "ENABLE_NON_BASE_FX")) return "ENABLE_NON_BASE_FX is false; the original object does not register this filter control.";
        if (name.StartsWith("df_", StringComparison.Ordinal) && !SongFiles.Config(project, "ENABLE_DF_GRID_AND_SIDELINE")) return "ENABLE_DF_GRID_AND_SIDELINE is false; these drawing controls are not registered.";
        if (Stars.Contains(name) && !SongFiles.Config(project, "ENABLE_STARPARTICLE", false)) return "ENABLE_STARPARTICLE is false; original Custom Gimmick does not register star controls.";
        if (name is "playspeed" or "jumpto_beat" or "jumpto_s" && !SongFiles.Config(project, "ENABLE_MUSIC_CONTROL")) return "ENABLE_MUSIC_CONTROL is false; original music-control callback does nothing.";
        return null;
    }
}
