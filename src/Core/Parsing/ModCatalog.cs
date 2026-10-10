using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 基础协议的 mod ID、默认值和缓动映射。对象扩展 ID 来自 manifest，数组顺序属于二进制格式契约，不能按字母排序。
/// </summary>
public static class ModCatalog
{
    static ModCatalog()
    {
        Supported.Add("lr_slash");
        Supported.Add("lr_slash_color");
        foreach (string name in "lr_sides_blue lr_sides_red lr_sides_rev_blue lr_sides_rev_red df_countdown".Split(' ')) Supported.Add(name);
        Supported.Add("fx_film");
        Supported.Add("playspeed");
        Supported.Add("prtrX");
        Supported.Add("prtrY");
        // 谱面侧 HUD 读数开关；隐藏 combo、判定信息及 combo 命中钻尘。
        Supported.Add("hide_combo");
        // Custom Episodes 模组注册的全局 mod：到点在谱面上浮出一段对白（story.json），
        // 不影响判定也不改任何演出参数。value1/value2 用不到，作者写 _ 即可。
        Supported.Add("custom_episode");
        // obj_custom_gimmick 的 InitSkinChange 用 addExtraMod 注册的，默认 0（正常皮肤），
        // 取值 0-3 是 lane_sprites 那两张表的下标，越界由 NoteSkinProfile 折回 0。
        Supported.Add("changeskin");
        // freeze 是全局 mod，带 start/end 回调，把音符滚动用的时钟钉在事件起点上；默认 0 = 不冻结
        // （gml_Object_cc_Create_0.gml:153）。展开成闭锁轨道的逻辑在 Timeline 构造函数里。
        Supported.Add("freeze");
        // drawdist 同样是全局 mod，也照常 tween 进 cc.mod_drawdist，但 6767 里只有早已停用的
        // obj_noteNormal 实例池会读它（doVisualYCheck / OptimizedNotePoolingPlaudite，两者都没有调用方）；
        // 实时绘制走 obj_note_rendering，它用的是 drawuntil。cc 的 Create 里连 mod_drawdist 都没初始化，
        // 可见原版自己也读不到。这里登记名字让它别报成未知 mod，效果则如实为零，详见 Timeline 的诊断。
        Supported.Add("drawdist");
        Supported.Add("apocalypse_sidething");
        Supported.Add("astellion_sidething");
        foreach (string name in "fx_edge static df_sideline df_sideline_alpha df_grid_alpha df_grid_top df_grid_bottom unraveling_sidething sides jumpto_beat jumpto_s".Split(' ')) Supported.Add(name);
        foreach (string name in CustomCompatibility.Stars) Supported.Add(name);
        foreach (string name in new[]
        {
            "angelstar_checker_alpha",
            "angelstar_checker_mode",
            "angelstar_checker_set"
        })
        {
            Supported.Add(name);
        }
        foreach (var name in "pburstleft pburstright prsy rotdir ditortedBG_alp ditortedBG_col_rgb BG_ditortScale BG_ditortAmount BG_blurRadius fx_chroma_distort fx_contrast fx_red fx_red_intensity recolor fx_hue_hue fx_hue_saturation fx_colorise_col_rgb fx_colorise_col_alpha fx_colorise_intensity sina sinp sino cosa coso tana tanp tano".Split(' '))
        {
            Supported.Add(name);
        }
        for (int i = 1; i <= 4; i++)
        {
            foreach (string name in new[]
            {
                "twx",
                "twy",
                "twa",
                "twr"
            })
            {
                Supported.Add(name + i);
            }
        }
        for (int i = 0; i < 7; i++)
        {
            foreach (string name in new[]
            {
                "xoffsetind",
                "yoffsetind",
                "boost_timeind",
                "boost_distanceind",
                "notealpind"
            })
            {
                Supported.Add(name + i);
            }
        }
    }

    /// <summary>基础协议的 mod 名表：下标就是二进制 ID（0..127），插入或排序都会让旧 VSB 整体错位。</summary>
    public static readonly string[] Globals = ("unknown prx prxb prxc pry pryb pryc prsx pra przm przmb przx przy prrx prry prrz prrzb shxs shxp shxa shys shyp shya scrollspeed noterot velocity spinradius spiny spinx driven beat wave hom boost_distance boost_time yoffset notealp przmc prxd pryd prct prcb prcl prcr prvib shct shft shcb shfb shcl shfl shcr shfr scrollind0 scrollind1 scrollind2 scrollind3 scrollind4 scrollind5 scrollind6 drawdist pburstleft pburstright particlexpower particleypower uialpha fx_contrast fx_chroma_distort fx_film fx_glow fx_particleglow pburstspeed freeze drawuntil").Split(' ');
    /// <summary>二进制缓动表：下标就是 VSB 里的缓动编号，同样不能排序或去重。</summary>
    public static readonly string[] BinaryEases = "unknown linear outElastic inExpo outExpo inOutExpo inQuad outQuad inOutQuad inCubic outCubic inOutCubic outBack inSine outSine inOutSine outQuart inOutCirc inCirc outCirc".Split(' ');
    public static readonly HashSet<string> Supported = new(("df_whitebg custom_jacket plaudite_jacket pburstspeed plaudite_pburst prx prxb prxc prxd pry pryb pryc pryd prsx pra przm przmb przmc przx przy prrx prry prrz prrzb prct prcb prcl prcr shxs shxp shxa shys shyp shya shct shft shcb shfb shcl shfl shcr shfr scrollspeed noterot velocity spinradius spiny spinx driven beat wave hom boost_distance boost_time yoffset xoffset notealp scrollind0 scrollind1 scrollind2 scrollind3 scrollind4 scrollind5 scrollind6 uialpha drawuntil holdoverlayalpha particle_alpha particlexpower particleypower slash_anycol set_slash_col col_convertion uhnoise vdistort bloom glitchamp glitchoffset barrelabx barrelaby posx posy fx_glow fx_particleglow fx_underwater fx_posterize fx_posterize_vis " + "gray barrel barrel2 hdistort fish vig abx aby aberamp cover1 cover2 cover3 wflash rainbow noteoverlayalp bgalph fxdist1 fxdist2 parttimer").Split(' '));
    /// <summary>
    /// 把二进制 mod ID 还原成名字。0..127 查 Globals；129 起按 id-129 查对象扩展表，表位置即编号。
    /// 无法识别时返回 global_N 或 obj:mod_N 占位，保留事件让报告能指出具体编号，不丢弃未知 mod。
    /// </summary>
    public static string Decode(int id, string obj, IReadOnlyList<string>? extraMods = null)
    {
        if (id < 128)
        {
            return id >= 0 && id < Globals.Length? Globals[id] : $"global_{id}";
        }
        if (id >= 129 && extraMods != null && id - 129 < extraMods.Count && !string.IsNullOrWhiteSpace(extraMods[id - 129]))
        {
            return extraMods[id - 129];
        }
        return $"{obj}:mod_{id}";
    }

    /// <summary>
    /// 尚无事件时该 mod 的初值。回退优先级：自定义图片 → 自定义文本 → 逐轨道特例 → 名称表；
    /// 这些数值来自原版初始化，改动会让第一条 tween 的起点漂移。
    /// </summary>
    public static double Default(string name, int proxy, double scroll = 3)
    {
        if (CustomImages.TryMod(name, out var kind, out _))
        {
            return CustomImages.Default(kind);
        }
        if (CustomText.TryMod(name, out var textKind, out var textId))
        {
            return CustomText.Default(textKind, textId == "");
        }
        if (name == "scrollspeed")
        {
            return scroll;
        }
        if (name.StartsWith("scrollind"))
        {
            return 1;
        }
        if (name.StartsWith("notealpind"))
        {
            return 1;
        }
        if (name.StartsWith("boost_timeind"))
        {
            // 300 是原版 boost_time 的毫秒初值，逐轨道分量与全局项同值。
            return 300;
        }
        if (name.StartsWith("twr") && name.Length == 4 && name[3] is >= '1' and <= '4')
        {
            return .4;
        }
        return name switch
        {
            // 16777215 是 0xFFFFFF 白色；Base/Custom proxy 的 pra 都默认为 0（Custom Create 的初始化循环执行时 proxyCount 尚为 0）。
            "df_sideline_alpha" => 1,
            "playspeed" or "starspd_multiplier" or "startrans_alpha" or "starchgcol_alpha" => 1,
            "starchgcol_up_rgb" or "starchgcol_down_rgb" => 16777215,
            "przm" or "przmb" or "przmc" or "przx" or "przy" or "velocity" or "notealp" or "uialpha" or "bgalph" or "holdoverlayalpha" or "noteoverlayalp" => 1,
            "pra" => proxy >= 0 ? 0 : 1,
            "fxdist1" or "fxdist2" => 100,
            "parttimer" => 2,
            "fx_posterize" => 32,
            "fx_particleglow" => .5,
            "fx_underwater" or "fx_chroma_distort" => .01,
            "particle_alpha" or "pburstspeed" => 1,
            "plaudite_jacket" => 11,
            "fx_contrast" or "fx_red_intensity" or "fx_hue_saturation" or "fx_colorise_col_alpha" or "sinp" or "tanp" or "rotdir" => 1,
            "ditortedBG_col_rgb" or "fx_colorise_col_rgb" => 16777215,
            "set_slash_col" or "lr_slash_color" => 16777215,
            "boost_time" => 300,
            "shxs" or "shys" => .2,
            "shxp" or "shyp" => 10,
            "prct" => .08,
            "prcl" or "prcr" => .35,
            "drawuntil" => double.MaxValue,
            _ => 0
        };
    }
}
