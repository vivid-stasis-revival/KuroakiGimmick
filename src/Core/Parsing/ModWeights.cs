using System.Globalization;
using System.Text;
using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.Core;

/// <summary>
/// GIMMICK 统计项用的演出权重。原版 GetSongStats 读的是与谱面同名的 <c>.vmv</c> 里 <c>[mods] weight</c>；
/// VSM 是演出源文件，VMV 是它生成出来的权重缓存，因此 VMV 存在时以 VMV 为准。
/// 没有 VMV 时按 VSM 现算一个近似值：read_mods_file 会先把 start:end:step 展开再累加，
/// 所以一行 <c>0:7:1,...</c> 计 8 次。表里的权重取自 6.2.0.1 的 gml_GlobalScript_mod_setup.gml，
/// 表外的名字是谱面自己 addExtraMod 注册的，原版默认权重为 1。
/// </summary>
public static class ModWeights
{
    /// <summary>全局 mod 的权重表。addGlobalMod 的第二个参数是权重，不是默认值。</summary>
    public static readonly IReadOnlyDictionary<string, double> Global = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["beat"] = 1.5, ["boost_distance"] = 2.5, ["boost_time"] = 2.5, ["drawdist"] = 0, ["drawuntil"] = 0,
        ["driven"] = 4, ["freeze"] = 2, ["fx_chroma_distort"] = 0.5, ["fx_contrast"] = 0.5, ["fx_film"] = 1,
        ["fx_glow"] = 1, ["fx_particleglow"] = 0.5, ["hom"] = 2, ["notealp"] = 1.5, ["noterot"] = 2.5,
        ["particlexpower"] = 0.5, ["particleypower"] = 0.5, ["pburstleft"] = 0.5, ["pburstright"] = 0.5,
        ["pburstspeed"] = 0.5, ["pra"] = 2, ["prcb"] = 1, ["prcl"] = 1, ["prcr"] = 1, ["prct"] = 1, ["prrx"] = 2,
        ["prry"] = 2, ["prrz"] = 2, ["prrzb"] = 2, ["prsx"] = 2, ["prvib"] = 1, ["prx"] = 2, ["prxb"] = 2,
        ["prxc"] = 2, ["prxd"] = 1, ["pry"] = 2, ["pryb"] = 2, ["pryc"] = 2, ["pryd"] = 1, ["przm"] = 2,
        ["przmb"] = 2, ["przmc"] = 1, ["przx"] = 2, ["przy"] = 2, ["scrollind0"] = 1.5, ["scrollind1"] = 1.5,
        ["scrollind2"] = 1.5, ["scrollind3"] = 1.5, ["scrollind4"] = 1.5, ["scrollind5"] = 1.5, ["scrollind6"] = 1.5,
        ["scrollspeed"] = 0, ["shcb"] = 1, ["shcl"] = 1, ["shcr"] = 1, ["shct"] = 1, ["shfb"] = 1, ["shfl"] = 1,
        ["shfr"] = 1, ["shft"] = 1, ["shxa"] = 1.5, ["shxp"] = 1.5, ["shxs"] = 1.5, ["shya"] = 1.5, ["shyp"] = 1.5,
        ["shys"] = 1.5, ["spinradius"] = 2, ["spinx"] = 2, ["spiny"] = 2, ["uialpha"] = 0, ["unknown"] = 0,
        ["velocity"] = 2, ["wave"] = 2.5, ["yoffset"] = 1.5,
    };

    /// <summary>
    /// 第三方模组在运行时用 addGlobalMod 注册的全局 mod。它们不在原版 mod_setup 表里，
    /// 因此不能并进 <see cref="Global"/>——那张表是原版源码的镜像，混入模组的名字会让它不再可对照。
    /// 这里的权重同样取 addGlobalMod 的第二个参数；两张表都没有的名字仍按 addExtraMod 的默认权重 1。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> External = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        // Custom Episodes：o_mod_storyentry 的 Create 末尾 addGlobalMod("custom_episode", 0, cb, undefined)。
        // 权重 0：触发一段剧情不构成演出强度，不该计进 GIMMICK 统计。
        ["custom_episode"] = 0,
    };

    /// <summary>先查原版表，再查模组注册表；两者都没有才算未知。</summary>
    public static bool TryWeight(string name, out double weight) =>
        Global.TryGetValue(name, out weight) || External.TryGetValue(name, out weight);

    /// <summary>权重的来源，报告里如实写出，不把 VSM 推算当成 VMV 的权威值。</summary>
    public sealed record Result(double Weight, string Source, string? Vmv, string? Vsm, int ModCount, string[] UnknownMods);

    /// <summary>
    /// 解析 .vmv 的 <c>[mods] weight</c>。这是个极小的 INI：只找 [mods] 段里的 weight 键，
    /// 其余段与键一律忽略；读不出数值就返回 null 交给 VSM 兜底。
    /// </summary>
    public static double? ReadVmv(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 1024 * 1024)
            {
                return null;
            }
            bool mods = false;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    mods = line[1..^1].Trim().Equals("mods", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                int equals = line.IndexOf('=');
                if (!mods || equals < 0 || !line[..equals].Trim().Equals("weight", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                return double.TryParse(line[(equals + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double weight) && double.IsFinite(weight) ? weight : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return null;
    }

    /// <summary>
    /// 由 VSM 文本推算权重。复用 VsmDocument 的解析器，因此 mpf 段、注释、元数据行与重复区间的处理
    /// 与编辑器完全一致；只有严格七字段的事件行参与累加，原版那套更宽松的切分可能多收几行。
    /// </summary>
    public static (double Weight, int ModCount, string[] Unknown) FromVsm(string text)
    {
        double total = 0;
        int count = 0;
        var unknown = new List<string>();
        foreach (var clip in VsmDocument.FromText(text).Clips)
        {
            int repeats = clip.RepeatCount;
            if (!TryWeight(clip.Name, out double weight))
            {
                weight = 1;
                if (!unknown.Contains(clip.Name, StringComparer.OrdinalIgnoreCase))
                {
                    unknown.Add(clip.Name);
                }
            }
            total += weight * repeats;
            count += repeats;
        }
        return (total, count, [.. unknown]);
    }

    /// <summary>
    /// 为某个难度求权重。先找同名 .vmv，再找同名 .vsm，最后回落 GLOBAL.vsm —— 与 read_mods_file
    /// 的难度专属优先规则一致。三者都没有时权重为 0，GIMMICK 项不显示。
    /// </summary>
    public static Result Resolve(string? chartPath, string difficulty)
    {
        string? dir = chartPath == null ? null : Path.GetDirectoryName(chartPath);
        if (dir == null || !Directory.Exists(dir))
        {
            return new(0, "none", null, null, 0, []);
        }
        string? vmv = SongFiles.Existing(dir, difficulty + ".vmv");
        string? vsm = SongFiles.Existing(dir, difficulty + ".vsm", "GLOBAL.vsm");
        if (vmv != null && ReadVmv(vmv) is { } cached)
        {
            return new(cached, "vmv", vmv, vsm, 0, []);
        }
        if (vsm != null)
        {
            try
            {
                var (weight, count, unknown) = FromVsm(VsmDocument.Load(vsm).Text);
                return new(weight, "vsm-derived", vmv, vsm, count, unknown);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or DecoderFallbackException)
            {
                return new(0, "none", vmv, vsm, 0, []);
            }
        }
        return new(0, "none", vmv, vsm, 0, []);
    }
}
