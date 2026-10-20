namespace KuroakiGimmick.Core;

internal static class DensePreviewChecks
{
    internal static void Run()
    {
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        Check(new ViewerSettings().StackLimit && AppJson.Deserialize<ViewerSettings>("{}")!.StackLimit,
            "STACK LIMIT must default on for both new and old settings.");
        Check(!AppJson.Deserialize<ViewerSettings>(AppJson.Serialize(new ViewerSettings { StackLimit = false }))!.StackLimit,
            "Disabled STACK LIMIT did not survive serialization.");
        Check(AppJson.Deserialize<ViewerSettings>("{}")!.StackLimitLayers == 256
            && AppJson.Deserialize<ViewerSettings>(AppJson.Serialize(new ViewerSettings { StackLimitLayers = 1008 }))!.StackLimitLayers == 1008,
            "STACK LIMIT layer default or persistence changed.");
        Check(new ViewerSettings { StackLimitLayers = -1 }.StackLimitLayers == 16
            && new ViewerSettings { StackLimitLayers = 9999 }.StackLimitLayers == 1024, "Layer bounds were not enforced.");
        var chart = new Chart();
        for (int i = 0; i < 120_000; i++)
            chart.Notes.Add(new(1, i % 4 == 0 ? 2 : 0, i % 4, i % 4 == 0 ? 5 : 1, Note.EmptyExtra));
        var bpm = new BpmMap(chart, 120, 0);
        var index = new NoteIndex(chart.Notes, bpm);
        var score = new ScoreState(chart, bpm);
        Check(index.HasDensePreview && score.HasDensePreview, "Stack reduction was not built.");
        Check(index.Count == chart.Notes.Count && index.Visible(0, 2).Count() == chart.Notes.Count,
            "Authoritative notes were reduced.");
        Check(index.PreviewVisible(0, 2).Count() == ViewerSettings.DefaultStackLayers * 4, "Unexpected preview multiplicity.");
        Check(index.PreviewVisible(3, 4).Count() == ViewerSettings.DefaultStackLayers, "Crossing holds disappeared.");
        Check(score.HitsBetween(1, 1, false).Count() == chart.Notes.Count, "Full hit effects were reduced.");
        Check(score.HitsBetween(1, 1, false, preview: true).Count() == ViewerSettings.DefaultStackLayers * 4,
            "Preview hit effects did not use the reduced table.");
        Check(score.HitsBetween(1.5, 1.5).Count() == 30_000
            && score.HitsBetween(1.5, 1.5, preview: true).Count() == ViewerSettings.DefaultStackLayers,
            "Hold tick effects must be bounded only in previews.");
        Check(score.Judged(1) == chart.Notes.Count && score.Combo(1) == chart.Notes.Count, "Scoring changed.");
        foreach (int layers in new[] { 16, 32, 80, 256, 1008, 1024, 16 })
        {
            Check(index.PreviewVisible(0, 2, layers).Count() == layers * 4, "Live note layer limit changed count/order.");
            Check(index.PreviewVisible(3, 4, layers).Count() == layers, "Live hold layer limit lost crossing holds.");
            Check(score.HitsBetween(1, 1, false, preview: true, layers: layers).Count() == layers * 4,
                "Live explicit effect layer limit was ignored.");
            Check(score.HitsBetween(1.5, 1.5, preview: true, layers: layers).Count() == layers,
                "Live hold-tick layer limit was ignored.");
        }
        Check(index.PreviewVisible(0, 2, 16, enabled: false).Count() == chart.Notes.Count,
            "Disabled layer limit filtered authoritative notes.");
        var query = index.PreviewVisible(0, 6).GetEnumerator();
        while (query.MoveNext())
        {
            var entry = query.Current;
            var runtime = index.PreviewRuntime![query.CurrentIndex];
            Check(runtime.Time == entry.Start && runtime.End == entry.End && runtime.Lane == entry.Value.Note.Lane,
                "Preview native motion indices refer to the full table.");
        }
        var sparse = new Chart();
        for (int i = 0; i < 110_000; i++) sparse.Notes.Add(new(i, 0, i % 4, i, Note.EmptyExtra));
        var sparseIndex = new NoteIndex(sparse.Notes, bpm);
        Check(!sparseIndex.HasDensePreview && sparseIndex.PreviewVisible(9, 12).Count() == 4,
            "Ordinary charts should retain the full path.");
        var moderate = new List<Note>();
        for (int i = 0; i < 120_000; i++) moderate.Add(new(i / 1000, 0, 0, i / 1000, Note.EmptyExtra));
        var moderateIndex = new NoteIndex(moderate, bpm);
        Check(moderateIndex.HasDensePreview && moderateIndex.PreviewVisible(0, 0, 16).Count() == 16
            && moderateIndex.PreviewVisible(0, 0, 1024).Count() == 1000,
            "Stacks below the maximum must still support smaller limits.");
        Console.WriteLine("PASS dense preview: bounded repeats, crossing holds, native indices, exact counts/scores/full queries, sparse fallback");
    }
}
