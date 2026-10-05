using System.Text;
using System.Text.Json;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;

internal static class IssueFixes
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        // The supplied lr_extra_gimmicks callbacks ignore duration/ease/proxy,
        // clamp floor(value1) to 1..64, and take packed GM colour from value2.
        string vsm = """
            !obj:obj_custom_gimmick
            0,8,not_an_easing,_,_,lr_slash,63
            1,0,linear,_,255,lr_slash_color,-1
            1,0,linear,3.9,_,lr_slash,-1
            2,0,linear,_,_,lr_slash_color,-1
            2,0,linear,0,_,lr_slash,-1
            3,0,linear,100,0,lr_slash,-1
            4:5:0.5,0,linear,_,_,lr_slash,-1
            6,0,linear,1,_,lr_slash,-1
            6,0,linear,_,16711680,lr_slash_color,-1
            6,0,linear,1,_,lr_slash,-1
            """;
        var chart = new Chart(); VsmReader.ReplaceModsText(chart, vsm, "lr.vsm");
        var timeline = new Timeline(chart, new ViewerProject { Bpm = 60 }, 0);
        Check(!chart.Diagnostics.Any(d => d.Error), "LR callbacks should ignore tween ease/proxy");
        Check(timeline.LoreleiSlashes.Select(e => e.Count).SequenceEqual(new[] { 1, 3, 1, 64, 1, 1, 1, 1, 1 }), "LR count/range semantics");
        Check(timeline.LoreleiSlashes.Select(e => e.Color).SequenceEqual(new double[] { 16777215, 255, 255, 0, 255, 255, 255, 255, 16711680 }), "LR colour inheritance/source order");
        Check(timeline.End == 7, "LR duration must not extend slash lifetime");
        var weight = ModWeights.FromVsm(vsm);
        Check(weight.Weight == 0 && weight.Unknown.Length == 0, "LR registration weight is zero");
        Check(ModWeights.FromVsm("!obj:obj_distortedfate_gimmick\n0,0,linear,_,_,lr_slash,-1\n").Weight == 1, "Native same-name mod weight changed");
        Check(Color.GameMaker(255) == new Color(1, 0, 0), "GM 255 must be red");
        Check(Color.GameMaker(16711680) == new Color(0, 0, 1), "GM blue channel");
        Check(Color.GameMaker(-1) == Color.White, "GM signed colour must retain low bytes");
        Check(Color.GameMaker(16777216 + 255) == new Color(1, 0, 0), "GM high colour bits");
        chart.ObjectName = "obj_base_gimmick";
        Check(new Timeline(chart, new ViewerProject { Bpm = 60 }, 0).LoreleiSlashes.Count == 0, "LR requires custom object");
        Console.WriteLine("PASS #36: LR callbacks, colours, batches, brace ranges and zero mod weight");

        string temporaryRoot = Path.GetTempPath();
        // macOS aliases /var and /tmp; export deliberately rejects symlink ancestors.
        if (OperatingSystem.IsMacOS() && (temporaryRoot.StartsWith("/var/") || temporaryRoot.StartsWith("/tmp/"))) temporaryRoot = "/private" + temporaryRoot;
        string temp = Path.Combine(temporaryRoot, "kuroaki-issue40-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(temp, "song"); Directory.CreateDirectory(root);
        string FileAt(string name, string text = "resource")
        {
            string path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text); return path;
        }
        try
        {
            string sourceChart = FileAt("ENCORE.vsc", "0,0,0\n");
            FileAt("OPENING.vsc"); FileAt("unused.png"); FileAt("notes.txt"); FileAt("unused/subfolder.bin");
            FileAt(".KUROAKI/backup/secret.png"); FileAt(".kuroaki/projects/edit.sgv.json/copy.vsm");
            FileAt(".kuroaki/backup/edit.sgv.json/old/data.txt");
            if (!OperatingSystem.IsWindows()) Directory.CreateSymbolicLink(Path.Combine(root, "unused-link"), temp);
            FileAt("info.json", "{\"name\":\"Test\",\"audio_id\":\"music.ogg\",\"jacket\":\"jacket.png\",\"preview_id\":\"preview.ogg\"}");
            string music = FileAt("music.ogg"), jacket = FileAt("jacket.png"); FileAt("preview.ogg");
            string image = FileAt("pictures/needed.png"), localDependency = FileAt("fx/noise.png");
            FileAt("ENCORE.vsv"); FileAt("story.json", "{\"steps\":[]}");
            var project = new ViewerProject { Chart = sourceChart, Audio = music, Jacket = jacket };
            string events = "!obj:obj_custom_gimmick\n0,0,linear,_,_,custom_episode,-1\n";
            var input = new ChartExportInput(project, null, events, Encoding.UTF8.GetBytes(events), "{}", new[] { sourceChart }, [],
                "#Layer\nmain,0\n#Image\nmain:\nstatic,pic,pictures/needed.png,0\n", root,
                new Dictionary<string, string> { ["caption"] = "0,Hello" }, Dependencies: new[] { localDependency });
            var plan = ChartExport.Prepare(input, ChartExportKind.ChartFolder, Path.Combine(temp, "export"));
            var expected = new[] { "ENCORE.vsc", "ENCORE.vsm", "ENCORE.vsp", "ENCORE.vsv", "ENCORE_cgmk_config.json", "ENCORE_text_caption.txt", "Kuroaki.sgv.json", "fx/noise.png", "info.json", "jacket.png", "music.ogg", "needed.png", "preview.ogg", "story.json" };
            Check(plan.Files.Select(f => f.Name).Order().SequenceEqual(expected.Order()), "Chart Folder must include only necessary dependencies");
            ChartExport.Write(plan);
            Check(File.ReadAllBytes(Path.Combine(plan.Destination, "ENCORE.vsc")).SequenceEqual(File.ReadAllBytes(sourceChart)), "Chart bytes changed");
            Check(!Directory.Exists(Path.Combine(plan.Destination, ".kuroaki")), "Editor backups leaked");
            var exported = JsonSerializer.Deserialize<ViewerProject>(File.ReadAllText(Path.Combine(plan.Destination, "Kuroaki.sgv.json")), ViewerProject.Json)!;
            Check(new[] { exported.Chart, exported.Audio, exported.Images, exported.WindowMotion }.All(path => path != null && File.Exists(Path.GetFullPath(path, plan.Destination))), "Exported references do not resolve");
            var inAssets = ChartExport.Prepare(input, ChartExportKind.ChartFolder, Path.Combine(temp, "assets-export"), putImageGimmickIntoAssetsFolder: true);
            Check(inAssets.Files.Any(f => f.Name == "Assets/needed.png") && !inAssets.Files.Any(f => f.Name == "needed.png"), "Image Assets option");
            string stagedImage = FileAt(".kuroaki/projects/staging.sgv.json/staging.editor-assets/" + new string('a', 64) + ".png");
            var staged = input with
            {
                ImageText = "#Layer\nmain,0\n#Image\nmain:\nstatic,pic," + Path.GetRelativePath(root, stagedImage) + ",0\n",
                ImageOriginalNames = new Dictionary<string, string> { [stagedImage] = "author.png" }
            };
            var stagedPlan = ChartExport.Prepare(staged, ChartExportKind.ChartFolder, Path.Combine(temp, "staged-export"));
            Check(stagedPlan.Files.Any(f => f.Name == "author.png") && stagedPlan.Files.All(f => !f.Name.Contains(".kuroaki", StringComparison.OrdinalIgnoreCase)), "Referenced staged images must export without internal paths");
            bool refused = false;
            try { ChartExport.Prepare(input with { Dependencies = new[] { Path.Combine(root, ".KUROAKI/backup/secret.png") } }, ChartExportKind.ChartFolder, Path.Combine(temp, "bad")); }
            catch (IOException) { refused = true; }
            Check(refused, "Explicit cache dependency should be rejected");

            // Save twice: companions and backups must stay at their original location.
            string save = Path.Combine(root, "edit.sgv.json");
            var session = new Session(new ViewerProject { Chart = sourceChart, RoomPreset = "none", GameUiEnabled = false });
            var document = new EditorDocument(session);
            document.SaveCopy(save); document.SaveCopy(save);
            string hiddenRoot = Path.Combine(root, ".kuroaki");
            Check(Directory.Exists(Path.Combine(hiddenRoot, "projects", "edit.sgv.json")), "Companions moved");
            Check(Directory.GetDirectories(Path.Combine(hiddenRoot, "backup", "edit.sgv.json")).Count(dir => File.Exists(Path.Combine(dir, "edit.sgv.json"))) == 1, "Backup moved or missing");
            Check(!OperatingSystem.IsWindows() || (File.GetAttributes(hiddenRoot) & FileAttributes.Hidden) != 0, "Windows cache root must be hidden");
            Check(File.Exists(Path.Combine(hiddenRoot, "backup", "edit.sgv.json", "old", "data.txt")), "Existing backup was removed");
            var reopened = Session.Load(save);
            var savedInput = ChartExportInput.Capture(new EditorDocument(reopened), reopened);
            var savedPlan = ChartExport.Prepare(savedInput, ChartExportKind.ChartFolder, Path.Combine(temp, "saved-export"));
            Check(savedPlan.Files.Any(f => f.Name == "ENCORE.vsm") && savedPlan.Files.All(f => !f.Name.Contains(".kuroaki", StringComparison.OrdinalIgnoreCase)), "Save/reopen/export leaked working paths");
            Console.WriteLine("PASS #40: dependency-only export, portable references, Assets option and backup location/hidden flag");
        }
        finally { Directory.Delete(temp, true); }
    }
}
