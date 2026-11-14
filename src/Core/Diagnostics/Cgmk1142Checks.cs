using System.Text.Json.Nodes;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;

namespace KuroakiGimmick.Core;

/// <summary>Synthetic CGMK 1.14.2 programs and skin-change authoring round trips.</summary>
internal static class Cgmk1142Checks
{
    internal static void Run()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "kg-cgmk1142-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidDataException("CGMK 1.14.2: " + label);
            checks++;
        }
        void Near(double actual, double expected, string label) => Check(Math.Abs(actual - expected) < 1e-6,
            label + " (actual " + actual + ", expected " + expected + ")");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "cmpf"));
            string vsm = Path.Combine(root, "ENCORE.vsm"), scriptPath = Path.Combine(root, "cmpf", "ENCORE.test.vsmpf");
            File.WriteAllText(vsm, "!obj:obj_custom_gimmick\n");
            string configPath = Path.Combine(root, "cgmk_config.json");
            const string originalConfig = "{\"JACKET_MANAGE_MODE\":\"normal\",\"ENABLE_SKIN_CHANGE\":false,\"unknown\":{\"keep\":7}}";
            File.WriteAllText(configPath, originalConfig);
            var project = new ViewerProject { Gimmick = vsm, Bpm = 60, Profile = "core", RoomPreset = "none", GameUiEnabled = false };
            (Timeline Time, Chart Chart) RunScript(string source)
            {
                File.WriteAllText(scriptPath, source);
                var chart = new Chart { ObjectName = "obj_custom_gimmick" };
                chart.PerFrame.Add(new(0, 1, "test")); chart.PerFrame.Add(new(2, 3, "test"));
                return (new Timeline(chart, project, 3), chart);
            }
            var structures = RunScript("""
                ::Init::
                var s = struct_create();
                var alias = s;
                struct_set(alias, "count", 4);
                struct_set(s, "items", [2]);
                var items = struct_get(s, "items");
                var pushed = array_push(items, 3);
                var nested = [s];
                struct_set(nested[0], "count", 9);
                var names = struct_get_names(s);
                struct_set(s, "later", 1);
                var empty = struct_set(s, "missingValue", struct_get(s, "absent"));
                struct_set(s, "Count", 6);
                struct_set(s, "", 8);
                struct_set(s, "self", s);
                ::Main::
                set_mod_val("count", struct_get(alias, "count"));
                set_mod_val("items", array_length(struct_get(s, "items")));
                set_mod_val("names", array_length(names));
                set_mod_val("has_count", array_contains(names, "count"));
                set_mod_val("has_absent", struct_exists(s, "absent"));
                set_mod_val("has_undefined", struct_exists(s, "missingValue"));
                set_mod_val("push_void", to_str(pushed) == "undefined");
                set_mod_val("set_void", to_str(empty) == "undefined");
                set_mod_val("case", struct_get(s, "Count"));
                set_mod_val("empty_key", struct_get(s, ""));
                set_mod_val("cycle", struct_get(struct_get(s, "self"), "count"));
                """);
            Check(!structures.Chart.Diagnostics.Any(d => d.Error), "struct whitelist and reference operations execute");
            foreach (var (name, value) in new (string, double)[] { ("count",9), ("items",2), ("names",2),
                ("has_count",1), ("has_absent",0), ("has_undefined",1), ("push_void",1), ("set_void",1),
                ("case",6), ("empty_key",8), ("cycle",9) })
                Near(structures.Time.Get(name, .5), value, name);
            Near(structures.Time.Get("count", 2.5), 9, "structs recreate on range refresh");
            Near(structures.Time.Get("count", .5), 9, "struct samples survive reverse seeks");
            foreach (string invalid in new[] { "struct_create(1)", "struct_get([], \"x\")", "struct_set(s, 0, 1)",
                "struct_exists(0, \"x\")", "struct_get_names(s, 0)", "struct_get(s, \"absent\") + 1",
                "array_push([], 1) + 1", "s == s" })
            {
                var failed = RunScript("::Init::\nvar s = struct_create();\nvar result = " + invalid + ";\n::Main::");
                Check(failed.Chart.Diagnostics.Any(d => d.Error && d.Message.Contains("TypeError")), "reject " + invalid);
            }
            var missingInstance = RunScript("#RISK_LEVEL=RISK\n::Main::\nget_game_var_instance(\"cc\", \"cpos\");");
            Check(missingInstance.Chart.Diagnostics.Any(d => d.Error && d.Message.Contains("expects 3 arguments")), "instance index is mandatory");
            var instance = RunScript("#RISK_LEVEL=RISK\n::Main::\nset_mod_val(\"clock\", get_game_var_instance(\"cc\", \"cpos\", 0));");
            Near(instance.Time.Get("clock", .5), .5, "explicit instance index works");
            var bounded = RunScript("::Init::\nvar s = struct_create(); var key = \"x\";\n"
                + "for (var i=0; i<4000; i+=1) { struct_set(s, key, i); key = str_cat(key, \"x\"); }\n::Main::");
            Check(bounded.Chart.Diagnostics.Any(d => d.Error && d.Message.Contains("MemoryLimitReached")), "struct keys share allocation budget");

            const string counter = "#REFRESH_ON_EACH_RUN=false\n::Init::\nvar count=0;\n::Main::\ncount+=1; set_mod_val(\"counter\",count);";
            var firstRun = RunScript(counter);
            var nextRun = RunScript(counter);
            Near(firstRun.Time.Get("counter", 2.5), 89, "disabled refresh retains variables between ranges");
            Near(nextRun.Time.Get("counter", .5), 30, "new session resets even when range refresh is disabled");
            File.WriteAllText(Path.Combine(root, "cmpf", "GLOBAL.added.vsmpf"), "::Main::\nset_mod_val(\"new_file\",7);");
            var discovered = new Chart { ObjectName = "obj_custom_gimmick" };
            discovered.PerFrame.Add(new(0, 1, "added"));
            Near(new Timeline(discovered, project, 1).Get("new_file", .5), 7, "new scripts discovered on rebuild");
            var changed = RunScript("::Main::\nset_mod_val(\"counter\",123);");
            Near(changed.Time.Get("counter", .5), 123, "edited script reloaded on rebuild");
            Near(firstRun.Time.Get("counter", .5), 30, "existing timeline remains an immutable snapshot");

            var initial = new Session(project.Copy());
            var editor = new EditorDocument(initial);
            var clip = new VsmDocument.Clip(Guid.NewGuid(), 0, 10, "linear", "0", "3", "changeskin", -1);
            editor.Change("Add skin", () => editor.Vsm.Add(clip));
            Check(WindowMotionConfig.Flag(editor.Windows.Root, "ENABLE_SKIN_CHANGE"), "authoring enables CGMK setting");
            Check(File.ReadAllText(configPath) == originalConfig, "authoring preserves imported config on disk");
            var preview = new Session(editor.Project.Copy(), editedVsm: editor.Vsm.Text, editedWindows: editor.CompiledWindows());
            Check(preview.SkinChangeEnabled && preview.Timeline.CustomSkinChangeEnabled, "unsaved config reaches renderer and timeline");
            Near(preview.Timeline.Get("changeskin", .5), 3, "skin callback switches immediately without tweening");
            editor.Undo();
            Check(!editor.Vsm.Clips.Any() && !WindowMotionConfig.Flag(editor.Windows.Root, "ENABLE_SKIN_CHANGE") && !editor.Dirty,
                "undo restores event and disabled setting together");
            editor.Redo();
            Check(editor.Vsm.Clips.Single().Id == clip.Id && WindowMotionConfig.Flag(editor.Windows.Root, "ENABLE_SKIN_CHANGE"), "redo restores both");
            var export = ChartExportInput.Capture(editor, preview);
            Check(JsonNode.Parse(export.ConfigText)!["ENABLE_SKIN_CHANGE"]!.GetValue<bool>(), "export captures unsaved skin setting");
            Check(JsonNode.Parse(export.ConfigText)!["unknown"]!["keep"]!.GetValue<int>() == 7, "export preserves unrelated config");
            string destination = Path.Combine(root, "out", "ENCORE.vsm");
            var plan = ChartExport.Prepare(export, ChartExportKind.VsmAndConfig, destination);
            ChartExport.Write(plan);
            Check(JsonNode.Parse(File.ReadAllText(Path.Combine(root, "out", "ENCORE_cgmk_config.json")))!["ENABLE_SKIN_CHANGE"]!.GetValue<bool>(),
                "VSM plus config writes required switch");
            string saved = editor.SaveCopy(Path.Combine(root, "renamed.sgv.json"));
            var reopened = new Session(Session.ReadProject(saved, out _));
            Check(reopened.SkinChangeEnabled && reopened.Timeline.CustomSkinChangeEnabled, "saved explicit companion overrides original disabled config");
            Check(!File.ReadAllText(saved).Contains("CgmkConfigOverride"), "runtime config snapshot is not serialized");
            editor.Change("Disable skin", () => editor.Windows.Root["ENABLE_SKIN_CHANGE"] = false);
            var disabled = new Session(editor.Project.Copy(), editedVsm: editor.Vsm.Text, editedWindows: editor.CompiledWindows());
            Check(!disabled.SkinChangeEnabled && !disabled.Timeline.Tracks.ContainsKey(("changeskin", -1))
                && disabled.Timeline.SourceNoOps.Any(n => n.Name == "changeskin"), "explicit off disables callback and reports missing setting");
            editor.Change("Unrelated change", () => editor.Vsm.Add(clip with { Id = Guid.NewGuid(), Name = "cover1" }));
            Check(!WindowMotionConfig.Flag(editor.Windows.Root, "ENABLE_SKIN_CHANGE"), "unrelated edit preserves explicit off");
            editor.ApplySource(editor.Vsm.Text + "// unrelated source edit\n", editor.Images.Text);
            Check(!WindowMotionConfig.Flag(editor.Windows.Root, "ENABLE_SKIN_CHANGE"), "unrelated source edit preserves explicit off");
            editor.ApplySource(editor.Vsm.Text + "1,0,linear,_,2,changeskin,-1\n", editor.Images.Text);
            Check(WindowMotionConfig.Flag(editor.Windows.Root, "ENABLE_SKIN_CHANGE"), "source authoring enables required switch");

            var standalone = WindowMotionConfig.Parse("{\"format\":\"ExtCustomGimmick.WindowMovement\",\"events\":[],\"ENABLE_SKIN_CHANGE\":true}");
            var windowPreview = new Session(project.Copy(), editedVsm: export.VsmText, editedWindows: standalone);
            Check(windowPreview.SkinChangeEnabled && SongFiles.ConfigString(windowPreview.Project, "JACKET_MANAGE_MODE", "") == "normal",
                "standalone window overlay preserves song config and enables skin");
            var sides = new Chart { ObjectName = "obj_custom_gimmick" };
            sides.Mods.Add(new(0, 0, "linear", 0, 0, "sides", -1, 0));
            Check(new Timeline(sides, project, 1).Callbacks.Any(c => c.Name == "sides"), "1.14.2 retains sides callback after duplicate registration removal");
            Console.WriteLine("PASS CGMK 1.14.2: " + checks + " checks");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
