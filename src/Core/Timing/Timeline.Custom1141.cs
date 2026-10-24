namespace KuroakiGimmick.Core;

public sealed partial class Timeline
{
    public sealed record CustomFirework(double Launch, double FlightEnd, double Burst, double X, double Y, uint Seed)
    {
        public double BurstY
        {
            get
            {
                double p = Math.Clamp((Burst - Launch) / Math.Max(.001, FlightEnd - Launch), 0, 1);
                return 190 + (Y - 190) * p * (2 - p);
            }
        }
    }
    public bool CustomSkinChangeEnabled { get; private set; }
    public bool IsCustomGimmick => chart.ObjectName == "obj_custom_gimmick";
    public List<CustomFirework> CustomFireworks { get; } = [];
    readonly List<(double Time, int Scale)> customResolutions = [];

    // Resolution 1 restores the viewer's normal target; higher values select the
    // source room's 320x180 multiplied target, never multiply the export size.
    public int CustomResolutionAt(double time, int normalWidth)
    {
        int i = SortedSearch.UpperBound(customResolutions, time, x => x.Time) - 1;
        return i < 0 || customResolutions[i].Scale <= 1 || time >= End
            ? normalWidth : 320 * customResolutions[i].Scale;
    }
    // cc stores GameMaker BGR colors, while sc_firework_color takes RGB.
    static uint SwapFireworkColor(double value)
    {
        uint color = unchecked((uint)(long)value) & 0xffffff;
        return (color & 0xff00) | (color >> 16) | ((color & 255) << 16);
    }
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
        // The source RNG is shared with the game. Use stable per-event samples
        // for offline seeking; distribution matches, sequence is intentionally local.
        uint seed = unchecked((uint)e.Order * 65537 + 1141);
        double x = 10 + Math.Min(94, Math.Floor(Hash(seed) * 95)) + 206 * Math.Min(1, Math.Floor(Hash(seed + 1) * 2));
        double y = e.To == 573613 ? 20 + Math.Min(70, Math.Floor(Hash(seed + 2) * 71)) : e.To;
        double climb = e.From == 573613 ? 1 : Math.Max(0, e.From);
        double flightEnd = Math.Max(start, Bpm.Time(e.Beat + climb));
        // Source bursts on the first Step at/after the BPM-integrated endpoint.
        double burst = Math.Ceiling(flightEnd * 60 - 1e-8) / 60;
        CustomFireworks.Add(new(start, flightEnd, burst, x, y, seed + 3));
        End = Math.Max(End, burst + 2);
        return true;
    }
}
