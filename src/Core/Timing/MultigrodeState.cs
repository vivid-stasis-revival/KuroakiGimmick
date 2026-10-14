using System.Globalization;

namespace KuroakiGimmick.Core;

/// <summary>Source April-2024 price state and refresh-only text selection.</summary>
public sealed class MultigrodeState
{
    public IReadOnlyList<double> Fruits { get; }
    public double Banana { get; }
    readonly List<(double Time, int? Index)> refreshes = [];
    public MultigrodeState(Timeline timeline, Chart chart, int dialogueCount)
    {
        uint seed = 0xAF2024;
        double Random() => Timeline.Hash(seed++);
        Fruits = Enumerable.Range(0, 4).Select(_ => (Random() >= .5 ? 2 : 6) + Random() * 2.99).ToArray();
        Banana = (Random() >= .5 ? 5 : 9) + Random() * .99;
        foreach (var callback in timeline.Callbacks.Where(c => c.Name == "refreshdialogue"))
        {
            double selected = timeline.Get("dialogue_id", callback.Time);
            int? index = Index(selected, dialogueCount);
            refreshes.Add((callback.Time, index));
            if (index == null) chart.Diagnostics.Add(new("native-multigrode", 0, "refreshdialogue selected an out-of-range source dialogue ID.", true));
        }
    }
    public int? BodyIndexAt(double time)
    {
        int index = SortedSearch.UpperBound(refreshes, time, r => r.Time) - 1;
        return index < 0 ? null : refreshes[index].Index;
    }
    public static int? Index(double value, int count) => double.IsFinite(value) && value >= 0 && value < count ? (int)value : null;
    public static string Format(double price) => price.ToString("0.00", CultureInfo.InvariantCulture);
    public double FallingPrice(int lane) => lane == 1 ? Banana : Fruits[lane];
    public static (double Y, double Alpha) FallingPricePose(Timeline timeline, ViewerProject project, double time)
    {
        double M(string name) => timeline.Get(name, time);
        double distance = 239046 - time * 1000 + project.VisualDelayMs;
        double scroll = M("scrollspeed") * M("velocity"), bps = timeline.Bpm.BpmAtBeat(timeline.Bpm.Beat(time)) / 60;
        double y = NoteMotion.YFromScroll(distance, M("yoffset"), M("driven"), bps, scroll, M("wave"));
        double boost = M("boost_distance"), boostTime = M("boost_time");
        if (boost != 0) y += -boost + (distance < boostTime && boostTime > 0 ? boost * Math.Pow((boostTime - distance) / boostTime, 3) : 0);
        y -= 7 * project.NoteAlignment;
        double target = 144 - M("driven") * bps * scroll * 22;
        return (y - 10, y < target ? Math.Clamp((target - y) / 30, 0, 1) : 0);
    }
}
