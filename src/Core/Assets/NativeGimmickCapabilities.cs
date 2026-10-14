namespace KuroakiGimmick.Core;

/// <summary>
/// 原生代码中直接消费的对象 mod。声明式 drawing/room/shader 的依赖由 profile 自动收集；
/// 此表只登记无法从 manifest 推导的实际实现，不能从 ExtraMods 或 Defaults 复制生成。
/// </summary>
public static class NativeGimmickCapabilities
{
    // StarcrashersObjects, StarcrashersLateEvents, NativeGimmickRenderer.Starcrashers*,
    // GameUiRenderer.Starcrashers and Timeline's callback dispatch consume these names.
    private static readonly string[] starcrashers =
    [
        "starspd_multiplier", "starspawner_timer", "starspd_low", "starspd_high",
        "spawn_star_mask", "spawn_centerrect_red", "spawn_centerrect_black", "laser_spawn", "chorus_firework",
        "lasthours_checker_set", "lasthours_checker_chance", "lasthours_checker_alpha", "chorus_greenleft_overlay",
        "chorus_satalli_alli_particles", "chorus_satalli_sat_particles", "ending_coroutine"
    ];

    public static IReadOnlyList<string> Mods(string objectName) => objectName switch
    {
        "obj_starcrashers_gimmick" => starcrashers,
        "obj_lastwish_gimmick" => ["lwtext", "film", "glow", "filmsat", "lwflickerspd", "lwjitterintensity", "lwchroma"],
        "obj_sekaisen_gimmick" => ["track_alpha", "sekaisen_arrow_point", "sekaisen_target_point", "sekaisen_jacket",
            "starspawner_timer", "starspd_low", "starspd_high", "starspd_multiplier", "en_whiteoverlay", "eo_endcg"],
        // These three controls feed the source room FX parameter/visibility bindings.
        "obj_extendnova_gimmick" => ["en_whiteoverlay","en_posterize","en_posterize_vis","sn_bg_fxamp"],
        "obj_scarletdeath_gimmick" => ["fx_zoom"],
        "obj_angelstar_gimmick" => ["fx_zoom"],
        "obj_pictured_gimmick" => ["1_speech", "1_glitch", "2_bg_pics", "2_dialbox_size", "2_dialbox_circles",
            "2_dialbox_speech", "2_dialbox_pulsar", "3_pinkbg_alph", "c_flash"],
        "obj_plaudite_gimmick" => ["plaudite_jacket", "plaudite_slash_saturday", "plaudite_slash_dawn", "plaudite_slash_neutral",
            "plaudite_pburst", "plaudite_ending_event", "plaudite_red_particle", "plaudite_disable_jacket"],
        "obj_stargazers_gimmick" => ["sides", "starspawn", "sg_endblip", "sg_bg_alpha", "sg_bg_speed", "sg_starspawner", "starspd_low", "starspd_high", "star_pause"],
        "obj_tutorial_gimmick" => ["cover1", "sg_endblip", "sg_endblip_destroy"],
        "obj_multigrode_gimmick" => ["dialogue_size", "text_alpha", "dialogue_id", "fruitmemoryx", "refreshdialogue"],
        "obj_times_gimmick" => ["tx","tx2","tx3","txj","ty","ty2","ty3","tyj","trz","trz2","tz","tzx","tzy",
            "tspl","sepx","sepy","hid1","hid2","hid3","hid4","hid5","static","wflash"],
        "obj_distortedfate_gimmick" or "obj_firstbreath_gimmick" =>
            ["df_sideline", "df_sideline2", "df_whitebg", "df_grid_alpha", "df_grid_top", "df_grid_bottom",
             "lr_mountain_bg", "lr_slash", "lr_slash_color", "lr_sides_blue", "lr_sides_red",
             "lr_sides_rev_blue", "lr_sides_rev_red", "df_countdown"],
        "obj_stopmotion_gimmick" => ["stopmotionnoteskin", "sm_bar_show", "sm_bar_progress", "storycheck1", "storycheck2",
            "temporaryshadercreate", "temporaryshaderdestroy", "sm_leftbg_frameset", "sm_rightbg_frameset", "sm_pushcommand", "sm_killcommand"],
        "obj_self_gimmick" => ["stopmotionnoteskin", "sm_bar_show", "sm_bar_progress", "storycheck1", "storycheck2", "temporaryshadercreate",
            "self_satcolor", "self_satalpha", "self_satframe", "self_tsukicolor", "self_tsukialpha", "self_tsukiframe", "self_destsg", "self_gravefg"],
        _ => []
    };
}
