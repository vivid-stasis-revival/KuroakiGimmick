using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.Core;

internal static class MusicControlChecks
{
    internal static void Run()
    {
        int count = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidDataException("Music control: " + name);
            count++;
        }
        void Near(double value, double expected, string name) => Check(Math.Abs(value - expected) < 1e-8, name);
        const string source = "!obj:obj_custom_gimmick\n-10,0,linear,1,430,!jumpto_beat,-1\n-0.05,0,linear,0,0.5,!playspeed,-1\n";
        Chart Parse(string text)
        {
            var chart = new Chart();
            VsmReader.ReplaceModsText(chart, text, "music.vsm");
            chart.Notes.Add(new(2, 3, 0, 2, new Dictionary<int, object> { [1] = 120d }));
            return chart;
        }
        var original = Parse(source.Replace("!jumpto_beat", "jumpto_beat").Replace("!playspeed", "playspeed"));
        var chart = Parse(source);
        var bpm = new BpmMap(chart, 60, 0);
        var map = new ChartPlaybackMap(chart, bpm, true);
        var baseline = new ChartPlaybackMap(original, bpm, true);
        Check(map.Controls.SequenceEqual(baseline.Controls) && map.Segments.SequenceEqual(baseline.Segments),
            "prefixed controls use the existing callback semantics");
        Near(map.RateAt(0), .5, "negative-beat speed establishes half-speed at startup");
        var route = map.Route(0, 600);
        Near(route.ChartAt(0), 216, "startup jump converts beat 430 through the BPM change");
        Near(route.ChartAt(2), 217, "playback advances at half speed after jump");
        Near(route.Duration, 768, "export duration includes jump and half speed");
        Check(route.Legs.SequenceEqual(baseline.Route(0, 600).Legs), "preview/export route matches canonical names");
        Near(map.Route(300, 600).ChartAt(0), 300, "manual seek does not replay earlier jump");
        Check(chart.Mods[0].Name == "!jumpto_beat" && chart.Mods[1].Name == "!playspeed", "evaluation preserves source names");
        Check(VsmDocument.FromText(source).Text == source, "editor preserves source text");
        var timeline = new Timeline(chart, new ViewerProject { Bpm = 60, Profile = "core" }, 600);
        Check(!chart.Diagnostics.Any(d => d.Error || d.Message.Contains("Unsupported mod:")), "aliases do not report unsupported mods");
        Check(!ModCatalog.Supported.Contains("!cover1") && ModCatalog.MusicControlName("!cover1") == "!cover1",
            "prefix support is limited to the requested aliases");

        string root = Path.Combine(Path.GetTempPath(), "kg-music-controls-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "cgmk_config.json"), "{\"ENABLE_MUSIC_CONTROL\":false}");
            var disabledChart = Parse(source);
            var disabled = new Timeline(disabledChart, new ViewerProject { Gimmick = Path.Combine(root, "ENCORE.vsm"), Bpm = 60 }, 600);
            Check(disabled.MusicPlayback.Controls.Count == 0 && disabled.MusicPlayback.RateAt(0) == 1,
                "disabled music controls suppress both aliases");
            Check(disabled.SourceNoOps.Count == 2 && !disabledChart.Diagnostics.Any(d => d.Message.Contains("Unsupported mod:")),
                "disabled aliases retain source no-op reporting");
        }
        finally { Directory.Delete(root, true); }

        var invalidChart = Parse(source.Replace("1,430,!jumpto_beat", "1,-430,!jumpto_beat").Replace("0,0.5,!playspeed", "0,0,!playspeed"));
        var invalid = new ChartPlaybackMap(invalidChart, bpm, true);
        Check(invalidChart.Diagnostics.Count(d => d.Error) == 2 && !invalid.HasJumps,
            "aliases retain invalid speed and jump validation");
        Near(invalid.Route(0, 10).Duration, 10, "invalid aliases do not alter playback");
        Console.WriteLine("PASS music control: " + count + " checks");
    }
}
