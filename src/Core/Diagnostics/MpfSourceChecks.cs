using System.Text;
using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.Core;

/// <summary>Portable synthetic fixtures for source identity, SaveCopy and export dependency discovery.</summary>
internal static class MpfSourceChecks
{
    internal static int Run()
    {
        string temp = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "kg-mpf-sources-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidDataException("MPF source regression: " + label);
            checks++;
        }
        try
        {
            string root = Path.Combine(temp, "song"), scripts = Path.Combine(root, "cmpf");
            Directory.CreateDirectory(scripts);
            string vsm = Path.Combine(root, "ENCORE.vsm");
            const string text = "!obj:obj_custom_gimmick\nmpf\n0,4,wave\n";
            File.WriteAllText(vsm, text);
            string global = Path.Combine(scripts, "GLOBAL.wave.vsmpf");
            string local = Path.Combine(scripts, "encore.wave.vsmpf");
            File.WriteAllText(global, "::Main::\nset_mod_val(\"cover1\",0.2);\n");
            File.WriteAllText(local, "::Main::\nset_mod_val(\"cover1\",0.8);\n");
            File.WriteAllText(Path.Combine(scripts, "FINALE.wave.vsmpf"), "::Main::\n");
            var project = new ViewerProject { Gimmick = vsm, Profile = "core" };
            Check(MpfScriptSources.Resolve(project).Files["wave"] == local, "difficulty override ignores selector case");
            Check(!MpfScriptSources.Resolve(project).Files.ContainsKey("WAVE"), "script names are case sensitive");
            File.Delete(local);
            Check(MpfScriptSources.Resolve(project).Files["wave"] == global, "GLOBAL fallback");
            File.WriteAllText(local, "::Main::\nset_mod_val(\"cover1\",0.8);\n");

            var editor = new EditorDocument(new Session(project));
            string saved = editor.SaveCopy(Path.Combine(root, "DifferentProjectName.sgv.json"));
            Check(editor.Project.Gimmick!.Contains(".kuroaki"), "saved working file relocated");
            Check(MpfScriptSources.Resolve(editor.Project).Files["wave"] == local, "SaveCopy retains VSM-only identity");
            var reopened = Session.ReadProject(saved, out _);
            Check(MpfScriptSources.Resolve(reopened).Files["wave"] == local, "reopening retains source identity");
            string json = File.ReadAllText(saved);
            Check(!json.Contains(root, StringComparison.Ordinal), "saved source root is relative");
            string moved = Path.Combine(temp, "moved");
            Directory.Move(root, moved);
            reopened = Session.ReadProject(Path.Combine(moved, Path.GetFileName(saved)), out _);
            Check(MpfScriptSources.Resolve(reopened).Files["wave"] == Path.Combine(moved, "cmpf", Path.GetFileName(local)), "moving project folder preserves lookup");

            string chart = Path.Combine(moved, "ENCORE.vsc");
            File.WriteAllText(chart, "");
            reopened.Chart = chart;
            var input = new ChartExportInput(reopened, null, text, Encoding.UTF8.GetBytes(text), "{}", [], []);
            var plan = ChartExport.Prepare(input, ChartExportKind.ChartFolder, Path.Combine(temp, "export"));
            var item = plan.Files.Single(f => f.Name == "cmpf/ENCORE.wave.vsmpf");
            Check(item.Source == Path.Combine(moved, "cmpf", Path.GetFileName(local)), "export uses selected source and portable difficulty filename");
            Check(!plan.Files.Any(f => f.Name.Contains("GLOBAL.wave", StringComparison.Ordinal)), "export excludes overridden script");
            var missingInput = input with { VsmText = text.Replace("wave", "missing"), VsmBytes = Encoding.UTF8.GetBytes(text.Replace("wave", "missing")) };
            var missing = ChartExport.Prepare(missingInput, ChartExportKind.ChartFolder, Path.Combine(temp, "missing-export"));
            Check(missing.Warnings.Any(w => w.Contains("No external MPF script found for 'missing'", StringComparison.Ordinal)), "missing script is reported");

            string crowded = Path.Combine(temp, "crowded");
            Directory.CreateDirectory(Path.Combine(crowded, "cmpf"));
            for (int i = 0; i < 257; i++) File.WriteAllText(Path.Combine(crowded, "cmpf", "GLOBAL.f" + i + ".vsmpf"), "::Main::");
            var limited = MpfScriptSources.FindFiles(crowded, "ENCORE");
            Check(limited.Files.Count == 0 && limited.Errors.Count == 1, "oversized directory reports error without partial selection");
            string blocked = Path.Combine(temp, "blocked");
            Directory.CreateDirectory(blocked); File.WriteAllText(Path.Combine(blocked, "cmpf"), "not a directory");
            var unavailable = MpfScriptSources.FindFiles(blocked, "ENCORE");
            Check(unavailable.Files.Count == 0 && unavailable.Errors.Count == 1, "invalid resource directory reports an error without crashing discovery");
            Console.WriteLine("PASS CGMK MPF source/export: " + checks + " checks");
            return 0;
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
