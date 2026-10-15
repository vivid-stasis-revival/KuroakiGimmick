using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Mobile;

[Activity(Name = "io.github.kuroakigimmick.mobile.MainActivity", Label = "Kuroaki/Gimmick", MainLauncher = true,
    Exported = true, ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout |
        ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard | ConfigChanges.UiMode | ConfigChanges.SmallestScreenSize)]
public sealed class MainActivity : Org.Libsdl.App.SDLActivity
{
    const int PickRequest = 711;
    Action<string[]>? completion;
    bool importingAssets;
    string root = "", assetsRoot = "";
    protected override string[] GetLibraries() => ["SDL3"];
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        root = Path.Combine(FilesDir!.AbsolutePath, "Documents");
        assetsRoot = Path.Combine(FilesDir.AbsolutePath, "BundledAssets");
        // Version marker is published only after every asset has been copied.
        string marker = Path.Combine(assetsRoot, ".version");
        using var versionReader = new StreamReader(Assets!.Open("mobile-assets.version"));
        string version = versionReader.ReadToEnd();
        if (!File.Exists(marker) || File.ReadAllText(marker) != version)
        {
            if (Directory.Exists(assetsRoot)) Directory.Delete(assetsRoot, true);
            CopyAssets("", assetsRoot);
            File.WriteAllText(marker, version);
        }
        base.OnCreate(savedInstanceState);
    }
    protected override void Main()
    {
        try
        {
            MobilePlatform.Configure(root, assetsRoot, Pick);
            Console.SetError(TextWriter.Synchronized(new StreamWriter(Path.Combine(root, "UserData", "mobile.log"), append: true) { AutoFlush = true }));
            MobilePlatform.InstallShaders(assetsRoot);
            string installed = MobileAssetPack.Ready(assetsRoot) ? assetsRoot : Path.Combine(root, "Assets");
            string importMessage = "This app does not include game assets. Select your Android resource ZIP (Assets and MobileShaders).";
            while (!MobileAssetPack.Ready(installed))
            {
                using var ready = new ManualResetEventSlim();
                bool retry = false;
                RunOnUiThread(() =>
                {
                    var alert = new AlertDialog.Builder(this)!.SetTitle("Install resource pack")!
                        .SetMessage(importMessage)!
                        .SetNegativeButton("Exit", (_, _) => ready.Set())!
                        .SetPositiveButton("Choose ZIP", (_, _) => { importingAssets = true; Pick(false, null, false, values =>
                        {
                            try
                            {
                                if (values.Length > 0) { MobileAssetPack.Install(values[0], root); retry = true; }
                            }
                            catch (Exception ex) { importMessage = ex.Message; retry = true; }
                            finally { importingAssets = false; ready.Set(); }
                        }); })!.Create()!;
                    alert.CancelEvent += (_, _) => ready.Set();
                    alert.Show();
                });
                ready.Wait();
                if (!retry) { RunOnUiThread(Finish); return; }
            }
            MobilePlatform.Configure(root, installed, Pick);
            Program.RunMobile(MobilePlatform.LaunchArguments());
        }
        catch (Exception ex) { Android.Util.Log.Error("Kuroaki", ex.ToString()); }
    }
    void CopyAssets(string source, string destination)
    {
        var children = Assets!.List(source) ?? [];
        Directory.CreateDirectory(destination);
        foreach (string child in children)
        {
            string entry = source.Length == 0 ? child : source + "/" + child, target = Path.Combine(destination, child);
            if ((Assets.List(entry)?.Length ?? 0) > 0) CopyAssets(entry, target);
            else
            {
                using var input = Assets.Open(entry);
                using var output = File.Create(target);
                input.CopyTo(output);
            }
        }
    }
    void Pick(bool save, string? suggested, bool folder, Action<string[]> done)
    {
        RunOnUiThread(() =>
        {
            if (save)
            {
                var input = new Android.Widget.EditText(this) { Text = Path.GetFileName(suggested ?? "project.sgv.json") };
                input.SetSingleLine(true);
                var alert = new AlertDialog.Builder(this)!.SetTitle("Save to Exports")!
                    .SetMessage("Open Kuroaki/Gimmick → Exports in the system Files app to copy or share the result.")!
                    .SetView(input)!.SetNegativeButton("Cancel", (_, _) => done([]))!
                    .SetPositiveButton("Save", (_, _) =>
                    {
                        try { done([MobilePlatform.SavePath(suggested, input.Text ?? "")]); }
                        catch (Exception ex) { Android.Util.Log.Error("Kuroaki", ex.ToString()); done([]); }
                    })!.Create()!;
                alert.CancelEvent += (_, _) => done([]);
                alert.Show();
                return;
            }
            completion = done;
            var intent = new Intent(folder ? Intent.ActionOpenDocumentTree : Intent.ActionOpenDocument);
            if (!folder)
            {
                intent.SetType("*/*");
                intent.AddCategory(Intent.CategoryOpenable!);
                intent.PutExtra(Intent.ExtraAllowMultiple, true);
            }
            try { StartActivityForResult(intent, PickRequest); }
            catch (Exception ex) { Android.Util.Log.Error("Kuroaki", ex.ToString()); completion = null; done([]); }
        });
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != PickRequest) { base.OnActivityResult(requestCode, resultCode, data); return; }
        var callback = completion;
        completion = null;
        if (callback == null) return;
        if (resultCode != Result.Ok || data == null) { callback([]); return; }
        var uris = new List<Android.Net.Uri>();
        if (data.ClipData != null)
        {
            for (int i = 0; i < data.ClipData.ItemCount; i++)
                if (data.ClipData.GetItemAt(i)?.Uri is { } uri) uris.Add(uri);
        }
        else if (data.Data != null) uris.Add(data.Data);
        Task.Run(() =>
        {
            try
            {
                string batch = Path.Combine(root, "Imports", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(batch);
                callback(uris.Select(uri => { string path = CopyDocument(uri, batch); return importingAssets ? path : MobilePlatform.ExpandImport(path); }).ToArray());
            }
            catch (Exception ex) { Android.Util.Log.Error("Kuroaki", ex.ToString()); callback([]); }
        });
    }
    string CopyDocument(Android.Net.Uri uri, string parent)
    {
        if (DocumentsContract.IsTreeUri(uri))
        {
            string id = DocumentsContract.GetTreeDocumentId(uri)!;
            string directory = Path.Combine(parent, "Song");
            CopyTree(uri, id, directory);
            MobilePlatform.InstallShaders(directory);
            return directory;
        }
        string name = "import";
        using (var cursor = ContentResolver!.Query(uri, [IOpenableColumns.DisplayName], null, null, null))
            if (cursor?.MoveToFirst() == true) name = Path.GetFileName(cursor.GetString(0) ?? name);
        string target = Path.Combine(parent, SafeName(name));
        using var input = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("Cannot read the selected document.");
        using var output = File.Create(target);
        input.CopyTo(output);
        return target;
    }
    void CopyTree(Android.Net.Uri tree, string id, string target)
    {
        Directory.CreateDirectory(target);
        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, id)!;
        using var cursor = ContentResolver!.Query(children,
            [DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType], null, null, null);
        while (cursor?.MoveToNext() == true)
        {
            string childId = cursor.GetString(0)!, name = SafeName(cursor.GetString(1)!);
            string path = Path.Combine(target, name);
            if (cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir) CopyTree(tree, childId, path);
            else
            {
                using var input = ContentResolver.OpenInputStream(DocumentsContract.BuildDocumentUriUsingTree(tree, childId)!)!;
                using var output = File.Create(path);
                input.CopyTo(output);
            }
        }
    }
    static string SafeName(string name) => name.Length > 0 && name == Path.GetFileName(name) && name is not ("." or "..") && !name.Contains('\\')
        ? name : throw new IOException("Invalid document name.");
}
