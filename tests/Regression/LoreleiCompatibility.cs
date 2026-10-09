using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;

internal static class LoreleiCompatibility
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Near(double value, double expected, string message) => Check(Math.Abs(value - expected) < 1e-8, message);
    public static void Run()
    {
        var chart = new Chart();
        VsmReader.ReplaceModsText(chart, """
            !obj:obj_custom_gimmick
            !proxies:0
            1,100,not_an_easing,_,_,lr_sides_blue,63
            1,0,linear,0,0,lr_sides_rev_red,-1
            0,4,linear,4,0,df_countdown,-1
            """, "lorelei.vsm");
        var timeline = new Timeline(chart, new ViewerProject { Bpm = 60 }, 0);
        Check(timeline.Callbacks.Count == 2 && !chart.Diagnostics.Any(d => d.Error || d.Message.Contains("Unsupported mod")), "Side callbacks must ignore tween fields and be supported");
        Check(!timeline.Tracks.Keys.Any(k => LoreleiSides.IsCallback(k.Name)), "Side callbacks must not become tween tracks");
        Near(timeline.End, 4, "Ignored side duration must not extend the timeline");
        Near(timeline.Get("df_countdown", 2), 2, "Countdown tween midpoint");
        Near(timeline.Get("df_countdown", 4), 0, "Countdown must expire");
        Near(timeline.Get("df_countdown", 1), 3, "Countdown reverse seek");

        var forward = LoreleiSides.At("lr_sides_blue", .1).ToArray();
        Near(forward[0].X, 79, "Left side displacement / deceleration");
        Near(forward[1].X, 241, "Right side displacement / deceleration");
        Check(forward[0].Direction == 1 && forward[1].Direction == -1 && forward.All(s => s.Frame == 0), "Blue side mirroring");
        Near(forward[0].Alpha, .7, "Side fade");
        var reverse = LoreleiSides.At("lr_sides_rev_red", .1).ToArray();
        Near(reverse[0].X, 61, "Reverse left side");
        Near(reverse[1].X, 259, "Reverse right side");
        Check(reverse[0].Direction == -1 && reverse[1].Direction == 1 && reverse.All(s => s.Frame == 1), "Red reverse side mirroring");
        Check(!LoreleiSides.At("lr_sides_blue", -1).Any() && !LoreleiSides.At("lr_sides_blue", LoreleiSides.Lifetime).Any(), "Side lifetime boundaries");

        chart.ObjectName = "obj_firstbreath_gimmick";
        chart.Mods.Add(new(2, 0, "linear", 1, 1, "lr_sides_rev_blue", -1, 3, 6));
        var firstBreath = new Timeline(chart, new ViewerProject { Bpm = 60 }, 0);
        Check(firstBreath.Callbacks.Count == 2 && firstBreath.SourceNoOps.Any(n => n.Name == "lr_sides_rev_blue"), "First Breath source no-op");
        chart.ObjectName = "obj_distortedfate_gimmick";
        Check(new Timeline(chart, new ViewerProject { Bpm = 60 }, 0).Callbacks.Count == 3, "Native DF side callbacks");
        chart.ObjectName = "obj_base_gimmick";
        Check(new Timeline(chart, new ViewerProject { Bpm = 60 }, 0).Callbacks.Count == 0, "Side object scope");

        // A disabled DF decoration config must not disable the independent HUD control.
        string root = Path.Combine(Path.GetTempPath(), "kuroaki-lorelei-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string file = Path.Combine(root, "FINALE.vsm");
            File.WriteAllText(file, "!obj:obj_custom_gimmick\n0,4,linear,4,0,df_countdown,-1\n");
            File.WriteAllText(Path.Combine(root, "FINALE_cgmk_config.json"), "{\"ENABLE_DF_GRID_AND_SIDELINE\":false}");
            var session = new Session(new ViewerProject { Gimmick = file, Bpm = 60, RoomPreset = "none", GameUiEnabled = false });
            Check(session.Timeline.SourceNoOps.All(n => n.Name != "df_countdown"), "Countdown must be independent of DF decoration config");
            Check(GameUiRenderer.ComboText(session, 1) == " 3.00", "Countdown decimal / integer padding");
            Check(GameUiRenderer.ComboText(session, 4) != " 0.00", "Expired countdown returns to score display");
            Check(GameUiRenderer.ComboText(session, 2) == " 2.00", "Countdown HUD reverse seek");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS Lorelei: side motion, frames, lifetime, callbacks, scope and countdown HUD / seeks");
    }
}
