namespace KuroakiGimmick.Core;

/// <summary>MPF 1.14 regression checks; synthetic fixtures, no private charts or GPU.</summary>
internal static class MpfScriptChecks
{
    internal static int Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kg-mpf-" + Guid.NewGuid().ToString("N"));
        int count = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidDataException("MPF regression failed: " + name);
            count++;
        }
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "cmpf"));
            string chartPath = Path.Combine(dir, "ENCORE.vsc");
            string mpf = Path.Combine(dir, "cmpf", "ENCORE.wave.vsmpf");
            File.WriteAllText(chartPath, "");
            File.WriteAllText(mpf, """
                #MAX_LOOP_TIMES = 1024
                ::Init::
                var reset = 0;
                ::Main::
                reset += 1;
                set_mod_val("cover1", 0.5 + 0.5 * sin(beat * 2 * pi / 4));
                ::Finish::
                set_mod_val("cover1", 0);
                """);
            var project = new ViewerProject { Chart = chartPath, Bpm = 120, Profile = "core" };
            Chart Chart()
            {
                var chart = new Chart { ObjectName = "obj_custom_gimmick" };
                chart.PerFrame.Add(new(4, 12, "wave"));
                return chart;
            }
            Timeline NewTimeline(Chart chart) => new(chart, project, 8);

            var source = Chart();
            var time = NewTimeline(source);
            Check(Math.Abs(time.Get("cover1", time.Bpm.Time(5)) - 1) < 1e-6, "per-beat sinusoidal value");
            Check(Math.Abs(time.Get("cover1", time.Bpm.Time(6)) - .5) < 1e-6, "next phase");
            Check(Math.Abs(time.Get("cover1", time.Bpm.Time(13))) < 1e-6, "Finish cleanup");
            Check(Math.Abs(time.Get("cover1", time.Bpm.Time(5)) - 1) < 1e-6, "reverse seek");
            Check(source.Diagnostics.All(d => !d.IsError), "basic program diagnostics");

            File.WriteAllText(Path.Combine(dir, "cmpf", "GLOBAL.wave.vsmpf"), "::Main::\nset_mod_val(\"cover1\", 0.2);\n");
            File.WriteAllText(mpf, "::Main::\nset_mod_val(\"cover1\", 0.8);\n");
            var priority = NewTimeline(Chart());
            Check(Math.Abs(priority.Get("cover1", priority.Bpm.Time(5)) - .8) < 1e-6, "difficulty override");
            File.Delete(mpf);
            var fallback = NewTimeline(Chart());
            Check(Math.Abs(fallback.Get("cover1", fallback.Bpm.Time(5)) - .2) < 1e-6, "GLOBAL fallback");

            File.WriteAllText(mpf, """
                ::Init::
                var values = [1, 2, 3];
                ::Main::
                var result = 0;
                for (var i = 0; i < array_length(values); i += 1) {
                    result += values[i];
                }
                set_mod_val("cover1", result);
                """);
            var array = NewTimeline(Chart());
            Check(Math.Abs(array.Get("cover1", array.Bpm.Time(5)) - 6) < 1e-6, "arrays and for loops");

            File.WriteAllText(mpf, """
                #MAX_LOOP_TIMES = 4
                ::Main::
                while (true) { }
                """);
            var loopChart = Chart();
            _ = NewTimeline(loopChart);
            Check(loopChart.Diagnostics.Any(d => d.IsError && d.Message.Contains("MaxLoopReached", StringComparison.Ordinal)),
                "infinite loop bound");

            File.WriteAllText(mpf, """
                #RISK_LEVEL = HIGH_RISK
                ::Main::
                add_judge_cnt(0, 1);
                """);
            var riskChart = Chart();
            _ = NewTimeline(riskChart);
            Check(riskChart.Diagnostics.Any(d => d.IsError && d.Message.Contains("not simulate", StringComparison.Ordinal)),
                "high-risk gameplay mutation refused");

            Console.WriteLine("PASS CGMK 1.14 MPF: " + count + " checks");
            return 0;
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
