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
                    references++; return token;
                });
                if (references != 2) throw new InvalidOperationException("An image gimmick was lost during export.");
                ChartExport.Write(plan);
                string output = Path.Combine(kind == ChartExportKind.ChartFolder ? destination : Path.GetDirectoryName(destination)!, expected);
                if (!File.ReadAllBytes(output).AsSpan().SequenceEqual(File.ReadAllBytes(original)))
                    throw new InvalidOperationException("Export changed image content.");
            }
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS shared images: repeated import, original filename/case, save deduplication, VSP/Chart Folder exports, root/Assets references and bytes");
    }
}
