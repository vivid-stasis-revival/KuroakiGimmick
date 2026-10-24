namespace KuroakiGimmick.Core;

internal static class Cgmk1141Checks
{
    internal static void Run()
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidDataException("CGMK 1.14.1: " + name); count++; }
        var project = new ViewerProject { Bpm = 60, Profile = "core" };
        var chart = new Chart { ObjectName = "obj_custom_gimmick" };
        chart.Notes.Add(new(2, 3, 0, 2, new Dictionary<int,object> { [1] = 120d }));
        chart.Mods.Add(new(1, 99, "linear", 3, 50, "sc_firework", -1, 0));
        chart.Mods.Add(new(0, 0, "linear", 0xff0000, 0x00ff00, "sc_firework_color", -1, 1));
        chart.Mods.Add(new(4, 0, "linear", 573613, 0x0000ff, "sc_firework_color", -1, 2));
        chart.Mods.Add(new(0, 0, "linear", 0, 99, "set_resolution", -1, 3));
        chart.Mods.Add(new(5, 0, "linear", 0, 573613, "set_resolution", -1, 4));
        chart.Mods.Add(new(1, 0, "linear", .5, .5, "chorus_greenleft_overlay", -1, 5));
        chart.Mods.Add(new(2, 0, "linear", 1, 1, "chorus_greenleft_overlay", -1, 6));
        chart.Mods.Add(new(3, 0, "linear", 0, 0, "chorus_greenleft_overlay", -1, 7));
        var timeline = new Timeline(chart, project, 8);
        var firework = timeline.CustomFireworks.Single();
        Check(firework.Launch == 1 && firework.Burst == 3, "flight integrates changed BPM, ignores duration");
        Check(firework.Y == 50 && (firework.X is >= 10 and <= 104 or >= 216 and <= 310), "launch bands and explicit endpoint");
        Check(timeline.CustomFireworkColor(1, false) == 0xff0000, "star RGB");
        Check(timeline.CustomFireworkColor(3, true) == 0x0000ff, "burst color samples current color");
        Check(timeline.CustomFireworkColor(3, false) == 0xff0000, "underscore preserves independent color");
        Check(timeline.CustomResolutionAt(1, 640) == 2560, "resolution clamps at 8x source room");
        Check(timeline.CustomResolutionAt(4, 640) == 640, "underscore restores normal target");
        Check(timeline.CustomResolutionAt(1, 640) == 2560, "reverse seek restores earlier resolution");
        Check(timeline.CustomResolutionAt(timeline.End, 640) == 640, "chart end restores target");
        var blend = new PartialSkinBlendState(timeline);
        Check(blend.Sample(1.5).Active && blend.Sample(1.5).Alpha == .5, "partial skin blend");
        Check(blend.Sample(2.1).Direct, "full Allison selection");
        Check(!blend.Sample(2.6).Active, "custom zero restores skin");
        Check(blend.Sample(1.5).Alpha == .5, "skin reverse seek");
        chart.ObjectName = "obj_starcrashers_gimmick";
        var native = new Timeline(chart, project, 8);
        Check(native.CustomFireworks.Count == 0 && native.CustomResolutionAt(1,640) == 640, "custom callback scope");
        Check(new PartialSkinBlendState(native).Sample(2.6).Active, "native Starcrashers keeps its zero latch");
        var instant = new Timeline.CustomFirework(1, 1, 1, 50, 40, 0);
        Check(instant.BurstY == 190, "zero-climb same-tick burst uses source one-ms minimum flight");
        Check((instant with { Burst = 1.0 + 1.0/60 }).BurstY == 40, "zero-climb late tick reaches endpoint");
        var game = new MpfPreviewGameState(100);
        game.Add(0, 2, false); game.Add(1, 3, true); game.Add(3, 1, false);
        Check(game.Combo == 5 && game.MaxCombo == 5, "good preserves combo");
        Check(game.Score == 552 && Math.Abs(game.MinusScore - 1009946) < 1e-6, "source score formula");
        game.Add(4, 1, true); game.Add(4, -2, false);
        Check(game.Combo == 2 && game.Judge(4) == -1, "signed miss correction");
        Console.WriteLine("PASS CGMK 1.14.1: " + count + " checks");
    }
}
