using KuroakiGimmick.Core;

static class PerformanceRegression
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        var random = new Random(1800); var chart = new Chart();
        for (int i = 0; i < 4000; i++)
        {
            double t = Math.Floor(random.NextDouble() * 100 - 20);
            chart.Notes.Add(new(t, i % 9, i % 3, t + random.NextDouble() * 70, KuroakiGimmick.Core.Note.EmptyExtra));
        }
        var map = new BpmMap(new Chart(), 180, -150);
        var index = new NoteIndex(chart.Notes, map);
        for (int trial = 0; trial < 200; trial++)
        {
            double start = random.NextDouble() * 120 - 30, end = start + random.NextDouble() * 10;
            var expected = chart.Notes.Where(n => n.Type is not (3 or 4 or 5) && Math.Max(n.Time, n.End) >= start && n.Time <= end).OrderBy(n => n.Time).ToArray();
            var actual = index.Candidates(start, end).ToArray();
            Check(expected.SequenceEqual(actual), "Interval index changed tie ordering/crossing holds/reverse query");
            foreach (var e in index.Visible(start, end))
            {
                Check(Math.Abs(e.Value.Beat - map.Beat(e.Value.Note.Time)) < 1e-9, "Cached note beat changed");
                Check(Math.Abs(e.Value.EndBeat - map.Beat(Math.Max(e.Value.Note.Time, e.Value.Note.End))) < 1e-9, "Cached hold beat changed");
            }
        }
        chart.Notes.Add(new(-10, 2, 0, 100, KuroakiGimmick.Core.Note.EmptyExtra));
        Check(new NoteIndex(chart.Notes, map).Count == index.Count + 1, "Rebuilt index did not invalidate source snapshot");
        var bpmChart = new Chart();
        for (int i = 0; i < 10000; i++) bpmChart.Notes.Add(new(i * .01, 3, 0, i * .01, new Dictionary<int, object> { [1] = 90 + i % 300 }));
        var bpm = new BpmMap(bpmChart, 180, 250);
        for (int i = -100; i < 10000; i += 13)
        {
            double t = i * .011; Check(Math.Abs(bpm.Time(bpm.Beat(t)) - t) < 1e-9, "Binary BPM conversion is not invertible");
            var expected = bpm.Segments.LastOrDefault(s => s.Beat <= bpm.Beat(t)) ?? bpm.Segments[0];
            Check(bpm.BpmAtBeat(bpm.Beat(t)) == expected.Bpm, "BPM segment endpoint changed");
        }
        foreach (var segment in bpm.Segments) Check(bpm.BpmAtBeat(segment.Beat) == segment.Bpm, "Exact BPM endpoint changed");
        var densityChart = new Chart();
        for (int i = 0; i < 1000; i++) densityChart.Mods.Add(new(i * .03, 0, "linear", 0, 1, "velocity", -1, i));
        var density = new EventDensity(densityChart, map);
        Check(density.Bins(50, 20).ToArray().Sum() == densityChart.Mods.Count(m => map.Time(m.Beat) >= 0 && map.Time(m.Beat) < 20), "Density cache dropped a positive event");
        var bins = density.Bins(37, 20).ToArray(); Check(bins.Sum() == density.Bins(50, 20).ToArray().Sum(), "Resized density changed total");
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { foreach (var n in index.Candidates(30, 32)) _ = n.Lane; _ = density.Bins(50, 20); }
        Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Cached query allocated in steady state");
        var intervals = new IntervalIndex<int>(new[] { new IntervalIndex<int>.Entry(1, 0, 1, 0), new IntervalIndex<int>.Entry(2, 2, 3, 1) });
        Check(intervals.TryUpdate(new[] { new IntervalIndex<int>.Entry(1, 0, 5, 0), new IntervalIndex<int>.Entry(2, 2, 3, 1) }), "Incremental end replacement refused");
        Check(intervals.Overlap(4, 4).Single().Value == 1, "Incremental end maxima were stale");
        Check(!intervals.TryUpdate(new[] { new IntervalIndex<int>.Entry(1, 10, 11, 0), new IntervalIndex<int>.Entry(2, 2, 3, 1) }), "Start ordering change requires rebuild");
        Check(intervals.Overlap(4, 4).Single().Value == 1, "Rejected incremental update partially mutated the tree");
        string vsc = Path.Combine(Path.GetTempPath(), "kg-session-reuse-" + Guid.NewGuid().ToString("N") + ".vsc");
        try
        {
            File.WriteAllText(vsc, "1,0,0\n2,2,1,4\n");
            var project = new ViewerProject { Chart = vsc, Bpm = 180 };
            var first = new Session(project);
            var second = new Session(project.Copy(), editedVsm: "!obj:obj_base_gimmick\n1,0,linear,1,2,velocity,-1", reuseAudioFrom: first);
            Check(ReferenceEquals(first.Chart.Notes[0], second.Chart.Notes[0]), "Unchanged VSC reparsed note records");
            Check(!ReferenceEquals(first.Chart.Notes, second.Chart.Notes), "Sessions shared a mutable note list");
            Check(ReferenceEquals(first.NoteIndex, second.NoteIndex) && ReferenceEquals(first.Score, second.Score), "Unchanged VSC timing rebuilt note/score tables");
            Check(first.Chart.Mods.Count == 0 && second.Chart.Mods.Count == 1, "VSM rebuild mutated the previous chart");
            var shifted = project.Copy(); shifted.OffsetMs = 125;
            var third = new Session(shifted, reuseAudioFrom: second);
            Check(!ReferenceEquals(third.NoteIndex, second.NoteIndex) && !ReferenceEquals(third.Score, second.Score), "Timing settings failed to invalidate note/score tables");
            File.WriteAllText(vsc, "1,0,0\n2,2,1,4\n3,0,2\n");
            File.SetLastWriteTimeUtc(vsc, DateTime.UtcNow.AddSeconds(2));
            var changed = new Session(project.Copy(), reuseAudioFrom: third);
            Check(changed.NoteIndex.Count == 3 && !ReferenceEquals(changed.Chart.Notes[0], first.Chart.Notes[0]), "Changed VSC reused stale parser data");
            var restored = new Session(project.Copy(), reuseAudioFrom: second);
            Check(restored.Chart.Mods.Count == 0, "Source metadata/mods leaked from an edited session");
        }
        finally { File.Delete(vsc); }
        Console.WriteLine("PASS performance: stable interval queries, crossing holds, negative/reverse time, cached beats, BPM endpoints, density invalidation and zero query allocation");
    }
}
