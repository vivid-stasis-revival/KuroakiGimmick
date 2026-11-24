// 处理自定义烟花回调、颜色与分辨率。

namespace KuroakiGimmick.Core;

public sealed partial class Timeline
{
    /// <summary>自定义烟花的发射、飞行与爆裂状态。</summary>
    /// <param name="Launch">发射时刻。</param>
    /// <param name="FlightEnd">飞行结束时刻。</param>
    /// <param name="Burst">爆裂时刻。</param>
    /// <param name="X">逻辑横坐标。</param>
    /// <param name="Y">逻辑纵坐标。</param>
    /// <param name="Seed">重现粒子分布的随机种子。</param>
    public sealed record CustomFirework(double Launch, double FlightEnd, double Burst, double X, double Y, uint Seed)
    {
        /// <summary>爆裂点在场景中的纵坐标。</summary>
        public double BurstY
        {
            get
            {
                double p = Math.Clamp((Burst - Launch) / Math.Max(.001, FlightEnd - Launch), 0, 1);
                return 190 + (Y - 190) * p * (2 - p);
            }
        }
    }
    /// <summary>当前自定义配置是否允许 changeskin 生效。</summary>
    public bool CustomSkinChangeEnabled { get; private set; }
    /// <summary>当前谱面是否使用 obj_custom_gimmick。</summary>
    public bool IsCustomGimmick => chart.ObjectName == "obj_custom_gimmick";
    /// <summary>依据自定义回调建立的烟花事件列表。</summary>
    public List<CustomFirework> CustomFireworks { get; } = [];
    readonly List<(double Time, int Scale)> customResolutions = [];

    // 分辨率 1 恢复常规预览目标；更大的值以原版 320×180 房间尺寸为基准进行放大，导出尺寸不参与相乘。
    /// <summary>取得指定时刻自定义分辨率轨道生效后的渲染宽度。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="normalWidth">无有效分辨率覆盖时使用的宽度。</param>
    /// <returns>当前渲染宽度。</returns>
    public int CustomResolutionAt(double time, int normalWidth)
    {
        int i = SortedSearch.UpperBound(customResolutions, time, x => x.Time) - 1;
        return i < 0 || customResolutions[i].Scale <= 1 || time >= End
            ? normalWidth : 320 * customResolutions[i].Scale;
    }
    // cc 存储 GameMaker BGR 颜色，sc_firework_color 接收 RGB。
    static uint SwapFireworkColor(double value)
    {
        uint color = unchecked((uint)(long)value) & 0xffffff;
        return (color & 0xff00) | (color >> 16) | ((color & 255) << 16);
    }
    /// <summary>取得指定时刻发射或爆裂阶段使用的烟花颜色。</summary>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="burst">为 true 时采样爆裂阶段，否则采样发射阶段。</param>
    /// <returns>当前烟花颜色值。</returns>
    public double CustomFireworkColor(double time, bool burst) =>
        SwapFireworkColor(Get(burst ? "scfw_burst_col" : "scfw_star_col", time));
    bool RegisterCustom1141(ModEvent e, double start)
    {
        if (chart.ObjectName != "obj_custom_gimmick") return false;
        if (e.Name == "set_resolution")
        {
            int scale = e.To == 573613 ? 1 : (int)Math.Clamp(Math.Round(e.To, MidpointRounding.ToEven), 1, 8);
            customResolutions.Add((start, scale));
            End = Math.Max(End, start);
            return true;
        }
        if (e.Name == "sc_firework_color")
        {
            void Write(string name, double color)
            {
                if (color == 573613) return;
                if (!Tracks.TryGetValue((name, -1), out var track)) Tracks[(name, -1)] = track = [];
                double value = SwapFireworkColor(color);
                track.Add(new(start, 0, value, value, "linear", e));
            }
            Write("scfw_star_col", e.From);
            Write("scfw_burst_col", e.To);
            End = Math.Max(End, start);
            return true;
        }
        if (e.Name != "sc_firework") return false;
        // 原版使用游戏共享随机源。离线预览以稳定的逐事件种子保留分布，使跳转可重复，随机序列仅在本地复现。
        uint seed = unchecked((uint)e.Order * 65537 + 1141);
        double x = 10 + Math.Min(94, Math.Floor(Hash(seed) * 95)) + 206 * Math.Min(1, Math.Floor(Hash(seed + 1) * 2));
        double y = e.To == 573613 ? 20 + Math.Min(70, Math.Floor(Hash(seed + 2) * 71)) : e.To;
        double climb = e.From == 573613 ? 1 : Math.Max(0, e.From);
        double flightEnd = Math.Max(start, Bpm.Time(e.Beat + climb));
        // 按 BPM 积分得到终点，烟花在到达或越过该点的第一个 Step 爆裂。
        double burst = Math.Ceiling(flightEnd * 60 - 1e-8) / 60;
        CustomFireworks.Add(new(start, flightEnd, burst, x, y, seed + 3));
        End = Math.Max(End, burst + 2);
        return true;
    }
}
