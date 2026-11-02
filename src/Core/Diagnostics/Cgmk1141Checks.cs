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
        // A scrolling image followed by ordinary text in the same VSP layer used to
        // drag/stretch that text too (Blast Processing's heartattack1..6).
        var images = new CustomImages();
        var asset = new CustomImages.Asset("unused.png", 20, 20);
        images.Items.Add(new("moving", "main", 1000, 0, 0, asset, 1, 20, 20, 0));
        images.Items.Add(new("caption", "main", 1000, 0, 1, asset, 1, 20, 20, 0));
        images.Items.Add(new("zero", "main", 1000, 0, 2, asset, 1, 20, 20, 0));
        var imageChart = new Chart { ObjectName = "obj_custom_gimmick" };
        void Set(string name, double value) => imageChart.Mods.Add(new(0, 0, "linear", value, value, name, -1, imageChart.Mods.Count));
        Set("xoffset", 11);
        Set("imgxtime_moving", 50); Set("imgytime_moving", 100); Set("imgscaleytime_moving", 200);
        Set("imgxb_caption", 5); Set("imgyb_caption", -5);
        Set("imgytime_zero", 0);
        imageChart.Mods.Add(new(1, 1, "linear", 90, 150, "imgy_caption", -1, imageChart.Mods.Count));
        var imageTimeline = new Timeline(imageChart, project, 3);
        var poses = images.At(imageTimeline, 0).ToDictionary(x => x.Image.Id, x => x.Pose.Matrix);
        Check(poses["moving"].M31 == 171 && poses["moving"].M32 == 204, "time offsets still apply to their own image");
        Check(poses["moving"].M22 == -60, "time scale divides the entire note-position difference by height");
        Check(poses["caption"].M31 == 165 && poses["caption"].M32 == 85, "static image has independent offsets plus imgxb/imgyb");
        Check(poses["caption"].M22 == 20, "static image does not inherit preceding time scale");
        Check(poses["zero"].M32 == 234, "zero time offset is active, unlike the unset sentinel");
        Check(images.At(imageTimeline, 1.5).Single(x => x.Image.Id == "caption").Pose.Matrix.M32 == 115,
            "explicit caption movement remains animated");
        Check(images.At(imageTimeline, 0).Single(x => x.Image.Id == "caption").Pose.Matrix == poses["caption"],
            "image transforms are stable on reverse seek");
        images.Items.Reverse();
        Check(images.At(imageTimeline, 0).Single(x => x.Image.Id == "caption").Pose.Matrix == poses["caption"],
            "image transforms do not depend on sibling draw order");
        Console.WriteLine("PASS CGMK 1.14.1: " + count + " checks");
    }
}
