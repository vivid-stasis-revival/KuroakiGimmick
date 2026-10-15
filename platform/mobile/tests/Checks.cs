using System.IO.Compression;
using KuroakiGimmick.Native;
using KuroakiGimmick.Graphics;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.MobileChecks;

internal static class Checks
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException) { return; }
        throw new Exception(message);
    }
    static void CheckResourcePack(string resources, string baked, string root)
    {
        string input = Path.Combine(root, "pack-input", "Assets");
        foreach (string name in new[] { "Brand", "Notes", "NoteSkinFull" })
        {
            string source = Path.Combine(resources, name);
            if (!Directory.Exists(source)) continue;
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(input, name, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
        string shaders = Path.Combine(input, "MobileShaders");
        Directory.CreateDirectory(shaders);
        Assert(!MobileAssetPack.Ready(input), "Empty shader directory accepted.");
        foreach (string file in Directory.EnumerateFiles(Path.Combine(baked, "ios", "MobileShaders"), "*.kgms"))
            File.Copy(file, Path.Combine(shaders, Path.GetFileName(file)));
        Assert(!MobileAssetPack.Ready(input), "iOS resource pack accepted on Android host check.");
        foreach (string file in Directory.EnumerateFiles(Path.Combine(baked, "android", "MobileShaders"), "*.kgms"))
            File.Copy(file, Path.Combine(shaders, Path.GetFileName(file)), true);
        Assert(MobileAssetPack.Ready(input), "Valid Android resource pack rejected.");
        string zip = Path.Combine(root, "resource-pack.zip");
        ZipFile.CreateFromDirectory(Path.GetDirectoryName(input)!, zip);
        string destination = Path.Combine(root, "install");
        Directory.CreateDirectory(Path.Combine(destination, "Assets"));
        File.WriteAllText(Path.Combine(destination, "Assets", "user-file.txt"), "preserve");
        string installed = MobileAssetPack.Install(zip, destination);
        Assert(MobileAssetPack.Ready(installed), "Resource pack installation failed.");
        Assert(Directory.GetDirectories(destination, "Assets-previous-*").Length == 1, "Previous resources not preserved.");
        Assert(Directory.GetDirectories(destination, "AssetImport-*").Length == 0, "Successful import left staging directories.");
        foreach (string entry in new[] { "../escape", "/absolute", "Assets/../escape", "Assets\\escape" })
        {
            string invalid = Path.Combine(root, "bad-" + Guid.NewGuid().ToString("N") + ".zip");
            using (var archive = ZipFile.Open(invalid, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry(entry).Open())) writer.Write("bad");
            Reject(() => MobileAssetPack.Install(invalid, destination), "Unsafe resource ZIP accepted.");
            Assert(MobileAssetPack.Ready(installed), "Failed import damaged working resources.");
            Assert(Directory.GetDirectories(destination, "AssetImport-*").Length == 0, "Failed import left staging directories.");
        }
        Console.WriteLine("Resource ZIP checks passed: valid install, empty/wrong-platform rejection, backup preservation, traversal and staging cleanup.");
    }
    static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "kg-mobile-checks-" + Guid.NewGuid().ToString("N"));
        string original = Environment.CurrentDirectory;
        try
        {
            Assert(!typeof(Checks).Assembly.GetManifestResourceNames().Any(n => n.Contains("Assets.Gimmicks") || n.EndsWith("VsmReference.json") || n.EndsWith("JacketCatalog.json")), "Private resources were embedded in external-assets build.");
            if (args.Length > 1) CheckResourcePack(args[1], args[0], root);
            string assets = Path.Combine(root, "Assets");
            Directory.CreateDirectory(assets);
            MobilePlatform.Configure(root, assets, (_, _, _, complete) => complete([]));
            Assert(MobilePlatform.SavePath("test.vsm", "chart") == Path.Combine(root, "Exports", "chart.vsm"), "Default extension or save directory changed.");
            foreach (string invalid in new[] { "../escape.json", "../..", "/tmp/escape", "a\\b.json", ".", "..", "" })
                Reject(() => MobilePlatform.SavePath(null, invalid), "Save path escaped the sandbox.");
            File.WriteAllLines(Path.Combine(root, "launch.args"), ["Songs/demo", "--render-width", "640"]);
            Assert(MobilePlatform.LaunchArguments().SequenceEqual(new[] { "Songs/demo", "--render-width", "640" }), "Launch arguments changed.");
            string zip = Path.Combine(root, "valid.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("Song/chart.vsc").Open())) writer.Write("chart");
                using (var writer = new StreamWriter(archive.CreateEntry("Song/audio/music.ogg").Open())) writer.Write("audio");
            }
            string imported = MobilePlatform.ExpandImport(zip);
            Assert(File.ReadAllText(Path.Combine(imported, "audio/music.ogg")) == "audio", "Song import lost relative paths.");
            zip = Path.Combine(root, "malicious.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("../../escape.txt").Open()); writer.Write("escape");
            }
            int before = Directory.GetDirectories(Path.Combine(root, "Songs")).Length;
            Reject(() => MobilePlatform.ExpandImport(zip), "ZIP traversal accepted.");
            Assert(!File.Exists(Path.Combine(root, "escape.txt")) && Directory.GetDirectories(Path.Combine(root, "Songs")).Length == before,
                "Failed import leaked files or staging directories.");
            int checkedPacks = 0;
            foreach (string platform in new[] { "android", "ios" })
            {
                string destination = Path.Combine(assets, "MobileShaders");
                if (Directory.Exists(destination)) Directory.Delete(destination, true);
                Directory.CreateDirectory(destination);
                var files = Directory.GetFiles(Path.Combine(args[0], platform, "MobileShaders"), "*.kgms");
                Assert(files.Length >= 6, "Built-in mobile shaders are missing.");
                foreach (string file in files)
                {
                    string copy = Path.Combine(destination, Path.GetFileName(file));
                    File.Copy(file, copy);
                    using var reader = new BinaryReader(File.OpenRead(file));
                    reader.ReadInt32(); reader.ReadInt32();
                    var format = (GpuShaderFormat)reader.ReadUInt32();
                    bool vertex = reader.ReadBoolean(); string source = reader.ReadString();
                    var compiled = MobileShaderPack.Load(source, vertex, format);
                    Assert(compiled.Code.Length > 0 && compiled.Entry.Length > 0, "Empty shader payload.");
                    Assert(compiled.Uniforms.Select((u, i) => u.Offset == i * 16).All(x => x), "Uniform ABI changed.");
                    Reject(() => MobileShaderPack.Load(source, vertex, format == GpuShaderFormat.Msl ? GpuShaderFormat.SpirV : GpuShaderFormat.Msl), "Cross-platform shader accepted.");
                    File.AppendAllText(copy, "trailing data");
                    Reject(() => MobileShaderPack.Load(source, vertex, format), "Trailing shader corruption accepted.");
                    checkedPacks++;
                }
            }
            Console.WriteLine($"Mobile checks passed: save paths, literal arguments, song ZIP import, traversal cleanup, {checkedPacks} shader packs and format/corruption rejection.");
            return 0;
        }
        finally { Directory.SetCurrentDirectory(original); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
