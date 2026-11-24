// 验证共享图片的保存、删除、冲突处理及工程重开。

using System.Text;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void SmokeSharedImageExport()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "kg-shared-image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            string original = Path.Combine(source, "num2.PNG");
            File.Copy(Path.Combine(Paths.Assets, "App", "Kuroaki.png"), original);
            using var batch = ImageImportBatch.Prepare([original, original]);
            if (batch.Images.Any(image => Path.GetFileName(image.StagedPath) != "num2.PNG")
                || batch.Images[0].StagedPath == batch.Images[1].StagedPath)
                throw new InvalidOperationException("Import changed the original image filename or overwrote a staged copy.");
            string chart = Path.Combine(source, "ENCORE.vsc");
            File.WriteAllText(chart, "0,0,0,0\n");
            var document = new VspDocument("", source, Path.Combine(source, "ENCORE.vsp"));
            document.Add("first", batch.Images[0]);
            document.Add("second", batch.Images[1]);
            var saved = new Dictionary<string, byte[]>();
            string savedText = document.PrepareSave(Path.Combine(root, "saved"), "ENCORE", saved);
            if (saved.Count != 1 || Path.GetFileName(saved.Single().Key) != "num2.PNG"
                || !savedText.Contains("num2.PNG"))
                throw new InvalidOperationException("Saving shared images changed filenames or duplicated content.");
            var input = new ChartExportInput(new ViewerProject { Chart = chart }, null, "", [], "{}", [], [],
                ImageText: document.Text, ImageRoot: source, ImageOriginalNames: document.OriginalResourceNames());
            foreach (var kind in new[] { ChartExportKind.VspAndAssets, ChartExportKind.ChartFolder })
                foreach (bool assets in new[] { false, true })
                {
                    string destination = Path.Combine(root, $"{kind}-{assets}", "ENCORE.vsp");
                    var plan = ChartExport.Prepare(input, kind, destination, putImageGimmickIntoAssetsFolder: assets);
                    string expected = assets ? "Assets/num2.PNG" : "num2.PNG";
                    if (plan.Files.Count(file => file.Name.EndsWith(".PNG")) != 1)
                        throw new InvalidOperationException("Shared image was exported more than once.");
                    string text = Encoding.UTF8.GetString(plan.Files.Single(file => file.Name.EndsWith(".vsp")).Data!);
                    int references = 0;
                    VspDocument.Rewrite(text, (_, token) =>
                    {
                        if (token != expected) throw new InvalidOperationException("Shared VSP reference was renamed.");
                        references++;
                        return token;
                    });
                    if (references != 2) throw new InvalidOperationException("An image gimmick was lost during export.");
                    ChartExport.Write(plan);
                    string output = Path.Combine(kind == ChartExportKind.ChartFolder ? destination : Path.GetDirectoryName(destination)!, expected);
                    if (!File.ReadAllBytes(output).AsSpan().SequenceEqual(File.ReadAllBytes(original)))
                        throw new InvalidOperationException("Export changed image content.");
                }
            SmokeImageSaveAndDelete(root, chart, original);
        }
        finally
        {
            Directory.Delete(root, true);
        }
        Console.WriteLine("PASS shared images: repeated import, original filename/case, save deduplication, VSP/Chart Folder exports, root/Assets references and bytes");
    }

    static void SmokeImageSaveAndDelete(string root, string chart, string original)
    {
        void Check(bool ok, string problem)
        {
            if (!ok) throw new InvalidOperationException(problem);
        }

        string second = Path.Combine(root, "other", Path.GetFileName(original));
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        using (var stream = File.Create(second))
            new StbImageWriteSharp.ImageWriter().WritePng([255, 0, 0, 255], 1, 1, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        using var batch = ImageImportBatch.Prepare([original, second, second]);

        string vsm = Path.ChangeExtension(chart, ".vsm"), vsp = Path.ChangeExtension(chart, ".vsp");
        const string vsmText = "!obj:obj_custom_gimmick\n0,0,linear,0,1,imgalp_missing,-1\n0,0,linear,0,1,imgalp_shared,-1\n";
        const string vspText = "#Layer\nshared_layer,1000\n#Image\nshared_layer:\nstatic,missing,unavailable.png,0\nstatic,shared,num2.PNG,0\n";
        File.WriteAllText(vsm, vsmText);
        File.WriteAllText(vsp, vspText);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(chart)!, "cgmk_config.json"), "{\"JACKET_MANAGE_MODE\":\"normal\"}");
        var project = new ViewerProject { Chart = chart, Gimmick = vsm, Images = vsp, Profile = "core", RoomPreset = "none", GameUiEnabled = false };
        var session = new Session(project);
        var doc = new EditorDocument(session);
        Check(session.Images.Items.All(item => item.Id != "missing") && doc.Images.DeclaredIds().Contains("missing"), "Missing image declaration could not be selected.");
        doc.DeleteImage("missing");
        Check(!doc.Images.Ids().Contains("missing") && doc.Images.Ids().Contains("shared") && doc.Images.Text.Contains("shared_layer,1000")
            && !doc.Vsm.Text.Contains("imgalp_missing") && doc.Vsm.Text.Contains("imgalp_shared"), "Deleting a missing image changed another image or left its own events.");
        doc.Undo();
        Check(doc.Images.Text == vspText && doc.Vsm.Text == vsmText, "Undo did not restore both VSP and VSM.");
        doc.Redo();

        string[] ids = doc.ImportImages(batch, 4, false).ToArray();
        string destination = Path.Combine(root, "duplicate.sgv.json");
        string occupied = Path.Combine(ProjectBackups.WorkingFolder(destination), "duplicate.editor-assets", "num2_2.PNG");
        Directory.CreateDirectory(Path.GetDirectoryName(occupied)!);
        File.WriteAllText(occupied, "unrelated existing file");
        try
        {
            doc.SaveCopy(destination);
            throw new InvalidOperationException("Saving a collision did not request confirmation.");
        }
        catch (ImageSaveConflictException ex)
        {
            Check(ex.FileName == "num2.PNG", "Wrong image conflict reported.");
        }
        Check(doc.Dirty && !File.Exists(destination), "Conflicting save wrote a project or lost the dirty state.");

        doc.SaveCopy(destination, resolveImageNameConflicts: true);
        doc.SaveCopy(destination);
        var reopened = new Session(Session.ReadProject(destination, out _), destination);
        var paths = ids.Select(id => reopened.Images.Items.Single(item => item.Id == id).Asset.Path).ToArray();
        Check(!doc.Dirty && paths[0] != paths[1] && paths[1] == paths[2] && Path.GetFileName(paths[1]) == "num2_3.PNG",
            "Force save did not persist distinct paths or deduplicate the shared image.");
        Check(File.ReadAllBytes(paths[0]).AsSpan().SequenceEqual(File.ReadAllBytes(original))
            && File.ReadAllBytes(paths[1]).AsSpan().SequenceEqual(File.ReadAllBytes(second))
            && File.ReadAllText(occupied) == "unrelated existing file", "Force save changed an image or overwrote an existing file.");

        doc.DeleteImage(ids[1]);
        Check(!doc.Images.Ids().Contains(ids[1]) && doc.Images.Ids().Contains(ids[2]) && File.Exists(paths[1]),
            "Deleting one image instance broke a shared image resource.");
        doc.Undo();
        doc.SaveCopy(destination);
        Check(File.ReadAllText(doc.Project.Images!).Contains("num2_3.PNG") && File.ReadAllText(vsp) == vspText && File.ReadAllText(vsm) == vsmText,
            "Undo after force save lost the renamed path or changed source files.");
        Console.WriteLine("PASS image deletion and force save: missing/shared images, undo/redo, filename conflict, retained bytes and project reopen");
    }
}
