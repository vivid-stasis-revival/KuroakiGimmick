namespace KuroakiGimmick.Core;

/// <summary>Synthetic source-order, lifecycle and resource-limit regressions; no private assets.</summary>
internal static class MpfRuntimeChecks
{
    internal static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "kg-mpf-runtime-" + Guid.NewGuid().ToString("N"));
        int checks = 0, fixture = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidDataException("MPF runtime regression failed: " + name);
            checks++;
        }
        void Near(double actual, double expected, string name) => Check(Math.Abs(actual - expected) < 1e-6,
            name + " (actual " + actual + ", expected " + expected + ")");
        (Timeline Time, Chart Chart) Load(string script, Action<Chart>? configure = null,
            double end = 6, Action<string>? files = null)
        {
            string dir = Path.Combine(root, (++fixture).ToString(System.Globalization.CultureInfo.InvariantCulture));
            string cmpf = Path.Combine(dir, "cmpf");
            Directory.CreateDirectory(cmpf);
            string chartPath = Path.Combine(dir, "ENCORE.vsc");
            File.WriteAllText(chartPath, "");
            File.WriteAllText(Path.Combine(cmpf, "ENCORE.test.vsmpf"), script);
            files?.Invoke(cmpf);
            var chart = new Chart { ObjectName = "obj_custom_gimmick" };
            chart.PerFrame.Add(new(0, 5, "test"));
            configure?.Invoke(chart);
            var project = new ViewerProject { Chart = chartPath, Bpm = 60, Profile = "core" };
            return (new Timeline(chart, project, end), chart);
        }
        try
        {
            var direct = Load("::Main::\nset_mod_val(\"cover1\", 0.9);", c =>
                c.Mods.Add(new(0, 4, "linear", 0, 1, "cover1", -1, 0)));
            Near(direct.Time.Get("cover1", 1), .25, "native active tween follows direct MPF write");

            var sameTick = Load("::Init::\ntween_mod_val(\"cover1\", 2, \"linear\", 0.7, 0.9);\n::Main::\n", c =>
            {
                c.PerFrame.Clear(); c.PerFrame.Add(new(2 - 1.0 / 120, 4, "test"));
                c.Mods.Add(new(2, 2, "linear", 0, 1, "cover1", -1, 0));
            });
            Near(sameTick.Time.Get("cover1", 3), .8, "new MPF tween follows same-tick native registration");

            var future = Load("::Init::\ntween_mod_val_at(\"cover1\", 3, 2, \"linear\", 0.7, 0.9);\n::Main::\n", c =>
                c.Mods.Add(new(2, 4, "linear", 0, 1, "cover1", -1, 0)));
            Near(future.Time.Get("cover1", 4), .5, "later native registration overrides previously scheduled MPF tween");

            var previous = Load("::Main::\nset_mod_val(\"cover2\", get_mod_val(\"cover1\"));", c =>
                c.Mods.Add(new(0, 4, "linear", 0, 1, "cover1", -1, 0)));
            Near(previous.Time.Get("cover2", 1), (1 - 1.0 / 60) / 4, "get_mod_val reads previous final tick");
            var readOwn = Load("::Main::\nset_mod_val(\"cover1\", 0.6);\nset_mod_val(\"cover2\", get_mod_val(\"cover1\"));", c =>
                c.Mods.Add(new(0, 4, "linear", 0, 1, "cover1", -1, 0)));
            Near(readOwn.Time.Get("cover2", 1), .6, "get_mod_val sees current script direct writes before native tween");

            var feedback = Load("::Main::\nset_mod_val(\"cover1\", get_mod_val(\"cover1\") + 1);");
            Near(feedback.Time.Get("cover1", 1), 60, "feedback reads every immediately preceding preview tick");

            var recover = Load("::Main::\nif (beatStart < 2) { var bad = 1 / 0; }\nset_mod_val(\"cover1\", 0.7);", c =>
            {
                c.PerFrame.Clear(); c.PerFrame.Add(new(0, 1, "test")); c.PerFrame.Add(new(3, 4, "test"));
            });
            Check(recover.Chart.Diagnostics.Any(d => d.Error && d.Message.Contains("ZeroDivisionError", StringComparison.Ordinal)),
                "first range reports runtime error");
            Near(recover.Time.Get("cover1", 3.5), .7, "new range revives source script after runtime error");

            var tail = Load("::Main::\n::Finish::\nset_mod_val_at(\"cover1\", 3, 0.8);\ntween_mod_val(\"cover2\", 4, \"linear\", 0, 1);", c =>
            {
                c.PerFrame.Clear(); c.PerFrame.Add(new(0, 1, "test"));
            }, end: 1);
            Check(tail.Time.End >= 5 - 1e-8, "Finish scheduled work extends timeline End");
            Near(tail.Time.Get("cover1", 3.5), .8, "Finish delayed set executes past original duration");
            Near(tail.Time.Get("cover2", 5), 1, "Finish tail tween reaches final value");

            var invalid = Load("::Main::\nset_mod_val(\"cover1\", );");
            Check(invalid.Chart.Diagnostics.Any(d => d.Error), "bad script diagnosed without aborting timeline construction");
            var excessive = Load("::Main::\n", files: dir =>
            {
                for (int i = 0; i < 256; i++) File.WriteAllText(Path.Combine(dir, "GLOBAL.extra" + i + ".vsmpf"), "::Main::\n");
            });
            Check(excessive.Chart.Diagnostics.Any(d => d.Error && d.Message.Contains("256", StringComparison.Ordinal)),
                "too many files diagnosed without aborting timeline construction");

            var arrays = Load("::Init::\nvar values = [];\nvar i = 0;\nwhile (i < 20) { array_push(values, array_create(100000)); i += 1; }\n::Main::\n");
            Check(arrays.Chart.Diagnostics.Any(d => d.Error && d.Message.Contains("MemoryLimitReached", StringComparison.Ordinal)),
                "nested array allocations bounded cumulatively");
            var strings = Load("::Init::\nvar value = \"x\";\nvar i = 0;\nwhile (i < 30) { value = str_cat(value, value); i += 1; }\n::Main::\n");
            Check(strings.Chart.Diagnostics.Any(d => d.Error && d.Message.Contains("MemoryLimitReached", StringComparison.Ordinal)),
                "exponential string allocation bounded before concatenation");
            Console.WriteLine("PASS MPF runtime: " + checks + " checks");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
