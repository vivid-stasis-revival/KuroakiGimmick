using System.Diagnostics;
using KuroakiGimmick.Core;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void Near(double actual, double expected, string message)
    => Check(Math.Abs(actual - expected) <= 1e-8 * Math.Max(1, Math.Abs(expected)), $"{message}: {actual:R} != {expected:R}");
static Note Note(double time, int type = 0, int lane = 0, double? end = null)
    => new(time, type, lane, end ?? time, KuroakiGimmick.Core.Note.EmptyExtra);

// The previous expanded algorithm is the oracle, independent of compact streams/checkpoints.
static List<ScoreState.Hit> Expand(Chart chart, BpmMap bpm)
{
    var hits = new List<ScoreState.Hit>();
    foreach (var n in chart.Notes)
    {
        switch (n.Type)
        {
            case 0: hits.Add(new(n.Time, n.Lane, ScoreState.HitKind.Note, 0)); break;
            case 1 or 8: hits.Add(new(n.Time, n.Lane + 4, ScoreState.HitKind.Wide, 0)); break;
            case 6 or 7: hits.Add(new(n.Time + .035, n.Lane + (n.Type == 7 ? 4 : 0), ScoreState.HitKind.Mine, 0)); break;
            case 2:
                double perBeat = 60000 / bpm.BpmAtBeat(bpm.Beat(n.Time));
                int parts = (int)Math.Min(100000, Math.Floor(Math.Max(0, (n.End - n.Time) * 1000 - 150) / perBeat));
                hits.Add(new(n.Time, n.Lane, ScoreState.HitKind.Note, 0));
                for (int p = 1; p <= parts; p++) hits.Add(new(n.Time + p * perBeat / 1000, n.Lane, ScoreState.HitKind.HoldTick, -p * perBeat));
                hits.Add(new(n.End, n.Lane, ScoreState.HitKind.Note, (n.Time - n.End) * 1000)); break;
        }
    }
    hits.Sort((a, b) => a.Time.CompareTo(b.Time));
    return hits;
}
static void VerifyScore(Chart chart, double? fixedBpm = null)
{
    chart.Notes.StableSortByTime();
    var bpm = new BpmMap(chart, 137, 25, fixedBpm);
    var expected = Expand(chart, bpm);
    var score = new ScoreState(chart, bpm);
    Check(score.NoteCount == expected.Count, "Judgement total changed");
    Check(score.StoredHitCount <= 2 * chart.Notes.Count, "Future hold ticks were materialized");
    Check(score.RollingCheckpointCount <= 4097, "Unbounded rolling checkpoints");
    var acc = new double[expected.Count]; var ex = new double[expected.Count];
    double accStep = 1.01 * score.NoteValue, accRate = expected.Count == 0 ? 0 : 60 * 359800.0 / expected.Count;
    for (int k = 1; k < expected.Count; k++)
    {
        double gap = expected[k].Time - expected[k - 1].Time;
        acc[k] = Math.Min(k * accStep, acc[k - 1] + accRate * gap);
        ex[k] = Math.Min(k * 3.0, ex[k - 1] + 60 * gap);
    }
    IEnumerable<double> times = Enumerable.Range(0, 70).Select(i => i * .317 - 3)
        .Concat(expected.Where((_, i) => i % Math.Max(1, expected.Count / 60) == 0).SelectMany(h => new[] { Math.BitDecrement(h.Time), h.Time, Math.BitIncrement(h.Time), h.Time + .001 }))
        .Append(chart.Duration + 100).Reverse();
    foreach (double t in times)
    {
        int count = SortedSearch.UpperBound(expected, t, h => h.Time);
        Check(score.Judged(t) == count, $"Count at {t:R}");
        Near(score.CurrentScore(t), count * accStep, "Score");
        Check(score.GameScore(t) == 3L * count, "EX total");
        Check(score.LastHit(t) == (count == 0 ? null : expected[count - 1].Time), "Last hit");
        Near(score.AccScore(t), count == 0 ? 0 : Math.Min(count * accStep, acc[count - 1] + accRate * (t - expected[count - 1].Time)), "Rolling accuracy");
        Near(score.ExScore(t), count == 0 ? 0 : Math.Min(count * 3.0, ex[count - 1] + 60 * (t - expected[count - 1].Time)), "Rolling EX");
        var actualHits = score.HitsBetween(t - .5, t).OrderBy(h => h.Time).ThenBy(h => h.Lane).ThenBy(h => h.Kind).ThenBy(h => h.Lead).ToArray();
        var expectedHits = expected.Where(h => h.Time >= t - .5 && h.Time <= t).OrderBy(h => h.Time).ThenBy(h => h.Lane).ThenBy(h => h.Kind).ThenBy(h => h.Lead).ToArray();
        Check(actualHits.SequenceEqual(expectedHits), "Visible hit payload/order changed");
        Check(score.HitsBetween(t - .5, t, false).All(h => h.Kind != ScoreState.HitKind.HoldTick), "Disabled hold effects generated ticks");
    }
}
VerifyScore(new());
var random = new Random(27030);
for (int trial = 0; trial < 35; trial++)
{
    var chart = new Chart();
    chart.Notes.Add(new(2, 3, 0, 2, new Dictionary<int, object> { [1] = 231.0 }));
    for (int i = 0; i < 200; i++)
    {
        int type = random.Next(9); double t = random.Next(-20, 150) / 10.0;
        chart.Notes.Add(Note(t, type, random.Next(type is 1 or 7 or 8 ? 3 : 4), type == 2 ? t + random.NextDouble() * 20 : t));
    }
    VerifyScore(chart, trial % 2 == 0 ? 180 : null);
}
var longHold = new Chart(); longHold.Notes.Add(Note(-1, 2, end: 80000)); VerifyScore(longHold);
var simultaneous = new Chart();
for (int i = 0; i < 3000; i++) simultaneous.Notes.Add(Note(5, i % 2 == 0 ? 2 : 0, i % 4, i % 2 == 0 ? 7 : 5));
VerifyScore(simultaneous);
Console.WriteLine("PASS score oracle: mixed notes, variable/fixed BPM, negative times, tick boundaries, stacked hits, long holds, reverse seeks, rolling scores and effect windows");

var indexed = new Chart();
for (int i = 0; i < 2000; i++) indexed.Notes.Add(Note(i * .1, i % 9, i % 3, i % 9 == 2 ? i * .1 + 300 : i * .1));
var index = new NoteIndex(indexed.Notes); var map = new BpmMap(indexed, 180, 0);
for (int i = 0; i < 200; i++)
{
    double from = random.NextDouble() * 500, to = from + random.NextDouble() * 5;
    Check(index.Candidates(from, to).SequenceEqual(indexed.Notes.Where(n => n.Type is not (3 or 4 or 5) && n.End >= from && n.Time <= to)), "Interval query dropped/reordered holds");
    double beat = random.NextDouble() * 650 - 20;
    double expected = indexed.Notes.Where(n => n.Type is not (3 or 4 or 5)).Select(n => map.Beat(n.Time)).OrderBy(b => Math.Abs(b - beat)).First();
    Near(index.NearestBeat(beat, map)!.Value, expected, "Magnet");
}
var baseline = new Chart(); for (int i = 0; i < 100; i++) baseline.Notes.Add(Note(i * .2));
int before = new NoteIndex(baseline.Notes).Candidates(0, 2).Count();
for (int i = 0; i < 10000; i++) baseline.Notes.Add(Note(300));
Check(new NoteIndex(baseline.Notes).Candidates(0, 2).Count() == before, "Tail stack enters early render candidates");
for (int trial = 0; trial < 5000; trial++)
{
    double alignment = random.NextDouble() * 7 - 4, offset = random.NextDouble() * 4000 - 2000;
    double driven = random.NextDouble() * 30 - 15, bps = random.NextDouble() * 12 + .1;
    double scroll = random.NextDouble() * 30 - 15, wave = random.NextDouble() * 1000 - 500;
    double bt = random.NextDouble() * 5000 - 1000, bd = random.NextDouble() * 1000 - 500;
    bool custom = trial % 2 == 0;
    double cutoff = NoteMotion.FutureDistance(alignment, offset, driven, bps, scroll, wave, bt, bd, custom);
    for (int i = 0; i < 10; i++)
    {
        double d = cutoff + random.NextDouble() * 100000;
        double boost = custom ? -bd + (d < bt && bt > 0 ? bd * Math.Pow((bt - d) / bt, 3) : 0)
            : bt == 0 ? -bd : -bd + bd * Math.Pow(Math.Clamp((bt - d) / bt, 0, 1), 3);
        double y = alignment + NoteMotion.YFromScroll(d, offset, driven, bps, scroll, wave) + boost;
        Check(y < -25 || y > 205, "Future cutoff hides a visible note");
    }
}
Check(double.IsPositiveInfinity(NoteMotion.FutureDistance(3, 0, 0, 3, 0, 0, 0, 0, false)), "Stopped scroll must remain conservative");
Console.WriteLine("PASS note index and motion bounds: crossing holds, magnet oracle, future stack, reverse/zero scroll, wave, boost and offsets");

// #31/#32/#33: base/custom scope and callback semantics must not be inferred from renderer object gates.
var laneChart = new Chart { ObjectName = "obj_base_gimmick" };
laneChart.Mods.Add(new(0, 0, "linear", 0, 10, "xoffsetind0", -1, 0));
laneChart.Mods.Add(new(0, 0, "linear", 0, 20, "yoffsetind0", -1, 1));
laneChart.Mods.Add(new(0, 0, "linear", 1, .5, "notealpind0", -1, 2));
var laneTimeline = new Timeline(laneChart, new ViewerProject { Bpm = 120, ScrollSpeed = 3 }, 1);
Near(NoteMotion.X(laneTimeline, 0, 0, 0), 10, "Base xoffsetind must affect lane 0");
Near(NoteMotion.X(laneTimeline, 0, 1, 0), 0, "Base xoffsetind must not leak to lane 1");
Near(NoteMotion.Y(laneTimeline, 0, 0, 0), 150, "Base yoffsetind must affect lane 0");
Near(NoteMotion.Y(laneTimeline, 0, 1, 0), 144, "Base yoffsetind must not leak to lane 1");
Near(laneTimeline.Get("notealp", 0) * laneTimeline.Get("notealpind0", 0), .5, "Base notealpind must remain a live global lane mod");

var baseParticles = new Timeline(new Chart { ObjectName = "obj_base_gimmick" }, new ViewerProject { Bpm = 120 }, 1);
Check(baseParticles.Particles.Any(p => !p.Burst), "Base gimmick lost ambient background particles");
var unrelatedParticles = new Timeline(new Chart { ObjectName = "obj_unknown_gimmick" }, new ViewerProject { Bpm = 120 }, 1);
Check(unrelatedParticles.Particles.Count == 0, "Unknown native object gained ambient particles without a profile");
var sideOnlyChart = new Chart { ObjectName = "obj_unknown_gimmick" };
sideOnlyChart.Mods.Add(new(0, 0, "linear", 0, 1, "pburstleft", -1, 0));
var sideOnly = new Timeline(sideOnlyChart, new ViewerProject { Bpm = 120 }, 1);
Check(sideOnly.Particles.Count > 0 && sideOnly.Particles.All(p => p.Burst), "Side burst should build offsets without inventing ambient dust");

var slashSpanChart = new Chart();
slashSpanChart.Mods.Add(new(0, 1, "linear", 0, 1, "slash_anycol", -1, 0));
var slashSpanTimeline = new Timeline(slashSpanChart, new ViewerProject { Bpm = 60 }, 0);
Check(slashSpanTimeline.SlashSpans.Count == 1, "Non-zero slash_anycol duration must become a spawn span");
Near(slashSpanTimeline.SlashSpans[0].Start, 0, "Slash span start");
Near(slashSpanTimeline.SlashSpans[0].End, 1, "Slash span end");
Check(slashSpanTimeline.SlashSpans[0].TickCount == 60, "One-second slash span must spawn at 60 Hz");
Check(slashSpanTimeline.Callbacks.All(c => c.Name != "slash_anycol"), "Non-zero slash span must not expand into callback allocations");
Check(slashSpanTimeline.End >= 2, "Slash span tail lifetime was truncated");
var slashOnceChart = new Chart();
slashOnceChart.Mods.Add(new(0, 0, "linear", 0, 1, "slash_anycol", -1, 0));
var slashOnceTimeline = new Timeline(slashOnceChart, new ViewerProject { Bpm = 60 }, 0);
Check(slashOnceTimeline.SlashSpans.Count == 0 && slashOnceTimeline.Callbacks.Count(c => c.Name == "slash_anycol") == 1,
    "Zero-duration slash_anycol must remain a one-shot callback");
Console.WriteLine("PASS issues #31/#32/#33: base ambient particles, global lane mods and 60 Hz slash spans");

IssueFixes.Run();

string temp = Path.Combine(Path.GetTempPath(), "kuroaki-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    string vsc = Path.Combine(temp, "test.vsc");
    File.WriteAllText(vsc, "0,0,0\n100,1,1\n200,2,0,900\n300,3,0,b:180\n");
    var parsed = VscReader.Load(vsc);
    Check(ReferenceEquals(parsed.Notes[0].Extra, parsed.Notes[1].Extra), "VSC empty extras not shared");
    Near(Convert.ToDouble(parsed.Notes[2].Extra[1]), 900, "Hold extra");
    string vsb = Path.Combine(temp, "test.vsb");
    using (var writer = new BinaryWriter(File.Create(vsb)))
    {
        writer.Write(new byte[] { 86, 83, 67, 1, 0, 192 });
        for (int i = 0; i < 2; i++) writer.Write(new byte[] { 160, 162, 0, 163, (byte)i, 166, 167, 161 });
        writer.Write(new byte[] { 193, 255 });
    }
    parsed = VsbReader.Load(vsb);
    Check(ReferenceEquals(parsed.Notes[0].Extra, parsed.Notes[1].Extra), "VSB empty extras not shared");
    string tips = Path.Combine(temp, "tips.json");
    using var bundledStream = typeof(FunTips).Assembly.GetManifestResourceStream("KuroakiGimmick.Tips.json")!;
    using var bundledReader = new StreamReader(bundledStream);
    var bundledTips = FunTips.Parse(bundledReader.ReadToEnd());
    Check(FunTips.Load(tips).SequenceEqual(bundledTips), "Missing tips fallback");
    File.WriteAllText(tips, "broken"); Check(FunTips.Load(tips).SequenceEqual(bundledTips), "Invalid tips fallback");
    File.WriteAllText(tips, "[]"); Check(FunTips.Load(tips).Length == 0, "Empty tips should hide");
    File.WriteAllText(tips, "[\" first \",\"first\",\"second\",null,1,\"\",\"" + new string('x', FunTips.MaxLength + 1) + "\"]");
    var loaded = FunTips.Load(tips); Check(loaded.SequenceEqual(new[] { "first", "second" }), "Tips normalization");
    for (int i = 0; i < 20; i++) Check(FunTips.Pick(loaded, "first") == "second", "Repeated tip");
    Check(FunTips.Pick(Array.Empty<string>(), null) == null && FunTips.Pick(new[] { "one" }, "one") == "one", "Tips empty/single selection");
}
finally { Directory.Delete(temp, true); }
Console.WriteLine("PASS parsers and tips loader");

if (args.Contains("--scale"))
{
    foreach (int size in new[] { 10000, 100000, 500000, 1000000 })
    {
        GC.Collect(); long memory = GC.GetTotalMemory(true), allocated = GC.GetTotalAllocatedBytes(true);
        var watch = Stopwatch.StartNew();
        var chart = new Chart(); for (int i = 0; i < size; i++) chart.Notes.Add(Note(i * .01, i % 2 == 0 ? 0 : 1, i % 3));
        chart.Notes.StableSortByTime();
        var bpm = new BpmMap(chart, 180, 0); var score = new ScoreState(chart, bpm); var ni = new NoteIndex(chart.Notes);
        double build = watch.Elapsed.TotalMilliseconds;
        watch.Restart(); for (int i = 0; i < 10000; i++) _ = ni.NearestBeat(i * .13, bpm);
        double magnetMs = watch.Elapsed.TotalMilliseconds;
        Console.WriteLine($"scale N={size:N0}: build={build:F1}ms retained={(GC.GetTotalMemory(true)-memory)/1048576.0:F1}MiB allocated={(GC.GetTotalAllocatedBytes(true)-allocated)/1048576.0:F1}MiB magnet10k={magnetMs:F1}ms storedHits={score.StoredHitCount} checkpoints={score.RollingCheckpointCount}");
        GC.KeepAlive(score); GC.KeepAlive(ni);
    }
    var huge = new Chart(); for (int i = 0; i < 100000; i++) huge.Notes.Add(Note(0, 2, i % 4, 80000));
    var hugeMap = new BpmMap(huge, 180, 0); var timer = Stopwatch.StartNew(); long allocatedHolds = GC.GetTotalAllocatedBytes(true);
    var compact = new ScoreState(huge, hugeMap);
    Check(compact.NoteCount == 10000200000L && compact.StoredHitCount == 200000, "Large logical count overflow/materialization");
    Check(compact.Combo(90000) == compact.NoteCount, "Large combo overflow");
    Console.WriteLine($"long holds N=100,000 logicalHits={compact.NoteCount:N0}: build={timer.Elapsed.TotalMilliseconds:F1}ms allocated={(GC.GetTotalAllocatedBytes(true)-allocatedHolds)/1048576.0:F1}MiB storedHits={compact.StoredHitCount} checkpoints={compact.RollingCheckpointCount}");
    GC.KeepAlive(compact);
    var staggered = new Chart();
    for (int i = 0; i < 10000; i++) staggered.Notes.Add(Note(i * .00017, 2, i % 4, 80000 + i * .00017));
    timer.Restart();
    var staggeredScore = new ScoreState(staggered, new BpmMap(staggered, 180, 0));
    Check(staggeredScore.NoteCount == 1000020000L, "Staggered hold count");
    Console.WriteLine($"staggered long holds N=10,000 logicalHits={staggeredScore.NoteCount:N0}: build={timer.Elapsed.TotalMilliseconds:F1}ms checkpoints={staggeredScore.RollingCheckpointCount}");
    timer.Restart();
    _ = staggeredScore.AccScore(30000); _ = staggeredScore.ExScore(30000);
    Console.WriteLine($"staggered seek=30,000s: {timer.Elapsed.TotalMilliseconds:F1}ms");
}
Console.WriteLine("ALL REGRESSIONS PASSED");
