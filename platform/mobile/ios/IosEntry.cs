using System.Runtime.InteropServices;
using Foundation;
using UIKit;
using UniformTypeIdentifiers;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Mobile;

internal static class IosEntry
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int MainFunction(int argc, nint argv);
    static readonly MainFunction entry = Run;
    [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
    static extern int SDL_RunApp(int argc, nint argv, MainFunction main, nint reserved);
    static UITextField? nameInput;
    static UIWindow? dialogWindow;
    static UIDocumentPickerViewController? documentPicker;
    static bool importingAssets;
    static Host? host;
    static KuroakiGimmick.UI.Viewer? viewer;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void FrameCallback(nint userdata);
    static readonly FrameCallback frame = Frame;
    [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    static extern bool SDL_SetiOSAnimationCallback(nint window, int interval, FrameCallback? callback, nint userdata);
    [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
    static extern void SDL_SetiOSEventPump([MarshalAs(UnmanagedType.I1)] bool enabled);
    static int Main(string[] args) => SDL_RunApp(0, 0, entry, 0);

    [ObjCRuntime.MonoPInvokeCallback(typeof(MainFunction))]
    static int Run(int argc, nint argv)
    {
        try
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string bundled = Path.Combine(NSBundle.MainBundle.BundlePath, "Assets");
            Directory.CreateDirectory(Path.Combine(root, "UserData"));
            Console.SetError(TextWriter.Synchronized(new StreamWriter(Path.Combine(root, "UserData", "mobile.log"), append: true) { AutoFlush = true }));
            Console.Error.WriteLine($"[{DateTimeOffset.Now:O}] iOS startup");
            MobilePlatform.Configure(root, bundled, Pick);
            MobilePlatform.StartFrameLoop = StartFrames;
            string installed = MobileAssetPack.Ready(bundled) ? bundled : Path.Combine(root, "Assets");
            while (!MobileAssetPack.Ready(installed))
            {
                bool finished = false;
                string[] selected = [];
                importingAssets = true;
                Pick(false, null, false, values => { selected = values; Volatile.Write(ref finished, true); });
                // SDL_RunApp is on UIKit's main thread. Keep the scene and picker responsive
                // before the editor's frame callback has been installed.
                while (!Volatile.Read(ref finished))
                    NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.02));
                importingAssets = false;
                if (selected.Length == 0) return 0;
                try { installed = MobileAssetPack.Install(selected[0], root); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    Sdl.SDL_ShowSimpleMessageBox(0x10, "Resource pack import failed", ex.Message, 0);
                }
            }
            MobilePlatform.Configure(root, installed, Pick);
            MobilePlatform.InstallShaders(installed);
            return Program.RunMobile(MobilePlatform.LaunchArguments());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            if (!Sdl.SDL_ShowSimpleMessageBox(0x10, "Kuroaki/Gimmick", ex.Message + "\n\nSee UserData/mobile.log for details.", 0))
                Console.Error.WriteLine("Startup message box: " + Sdl.Error);
            return 1;
        }
    }

    static void StartFrames(Host h, KuroakiGimmick.UI.Viewer v)
    {
        host = h; viewer = v;
        MobilePlatform.WatchLifecycle(v.PauseMobile);
        SDL_SetiOSEventPump(false);
        Sdl.Require(SDL_SetiOSAnimationCallback(h.Window, 1, frame, 0));
    }
    [ObjCRuntime.MonoPInvokeCallback(typeof(FrameCallback))]
    static void Frame(nint userdata)
    {
        try { if (viewer?.MobileFrame() != false) return; }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
        if (host != null) SDL_SetiOSAnimationCallback(host.Window, 1, null, 0);
        MobilePlatform.StopWatchingLifecycle();
        viewer?.Dispose(); host?.Dispose();
        viewer = null; host = null;
    }
    static UIViewController DialogPresenter()
    {
        nint pointer = host == null ? 0 : Sdl.SDL_GetPointerProperty(Sdl.SDL_GetWindowProperties(host.Window), "SDL.window.uikit.window", 0);
        var scene = pointer == 0 ? null : ObjCRuntime.Runtime.GetNSObject<UIWindow>(pointer)?.WindowScene;
        scene ??= UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>()
            .FirstOrDefault(s => s.ActivationState == UISceneActivationState.ForegroundActive);
        var activeWindow = pointer == 0 ? scene?.Windows.FirstOrDefault(w => w.IsKeyWindow)
            : ObjCRuntime.Runtime.GetNSObject<UIWindow>(pointer);
        if (activeWindow?.RootViewController is { } existing)
        {
            while (existing.PresentedViewController is { } presented) existing = presented;
            return existing;
        }
        dialogWindow = scene == null ? new UIWindow(UIScreen.MainScreen.Bounds) : new UIWindow(scene);
        dialogWindow.Frame = scene?.CoordinateSpace.Bounds ?? UIScreen.MainScreen.Bounds;
        var controller = new UIViewController();
        controller.View!.BackgroundColor = UIColor.SystemBackground;
        dialogWindow.RootViewController = controller;
        dialogWindow.WindowLevel = UIWindowLevel.Normal;
        dialogWindow.MakeKeyAndVisible();
        return controller;
    }
    static void CloseDialogWindow()
    {
        // All callers run on UIKit's main thread; close before invoking a completion
        // that may immediately create another picker (startup retry).
        if (dialogWindow != null) { dialogWindow.Hidden = true; dialogWindow.Dispose(); dialogWindow = null; }
        nint pointer = host == null ? 0 : Sdl.SDL_GetPointerProperty(Sdl.SDL_GetWindowProperties(host.Window), "SDL.window.uikit.window", 0);
        if (pointer != 0) ObjCRuntime.Runtime.GetNSObject<UIWindow>(pointer)?.MakeKeyAndVisible();
    }

    static void Pick(bool save, string? suggested, bool folder, Action<string[]> completion)
    {
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            var presenter = DialogPresenter();
            if (save)
            {
                var alert = UIAlertController.Create("Save to Exports", "Files are available in Files → On My iPhone/iPad → Kuroaki/Gimmick → Exports.", UIAlertControllerStyle.Alert);
                alert.AddTextField(field => { nameInput = field; field.Text = Path.GetFileName(suggested ?? "project.sgv.json"); });
                alert.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, _ => { nameInput = null; CloseDialogWindow(); completion([]); }));
                alert.AddAction(UIAlertAction.Create("Save", UIAlertActionStyle.Default, _ =>
                {
                    try { completion([MobilePlatform.SavePath(suggested, nameInput?.Text ?? "")]); }
                    catch (Exception ex) { Console.Error.WriteLine(ex); completion([]); }
                    finally { nameInput = null; CloseDialogWindow(); }
                }));
                presenter.BeginInvokeOnMainThread(() => presenter.PresentViewController(alert, true, null));
                return;
            }
            bool assets = importingAssets;
            var picker = new UIDocumentPickerViewController(
                new[] { folder ? UTTypes.Folder : assets ? UTTypes.Zip : UTTypes.Item }, asCopy: false)
            {
                AllowsMultipleSelection = !folder && !assets,
                ModalPresentationStyle = UIModalPresentationStyle.FullScreen
            };
            documentPicker = picker; // Keep the managed event bridge alive until dismissal.
            int completed = 0;
            void Finish(string[] values, string? error = null)
            {
                if (Interlocked.Exchange(ref completed, 1) != 0) return;
                UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                {
                    picker.DismissViewController(false, () =>
                    {
                        CloseDialogWindow();
                        documentPicker = null;
                        picker.Dispose();
                        if (error != null) Sdl.SDL_ShowSimpleMessageBox(0x10, "File import failed", error, host?.Window ?? 0);
                        completion(values);
                    });
                });
            }
            picker.WasCancelled += (_, _) => Finish([]);
            picker.DidPickDocumentAtUrls += (_, e) =>
            {
                var urls = e.Urls;
                Task.Run(() =>
                {
                    var paths = new List<string>();
                    try
                    {
                        string batch = Path.Combine(MobilePlatform.Root, "Imports", Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(batch);
                        foreach (var url in urls) paths.Add(CopySelection(url, batch));
                        if (!assets)
                            for (int i = 0; i < paths.Count; i++)
                            {
                                if (Directory.Exists(paths[i])) MobilePlatform.InstallShaders(paths[i]);
                                paths[i] = MobilePlatform.ExpandImport(paths[i]);
                            }
                        Finish(paths.ToArray());
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex);
                        Finish([], ex.Message);
                    }
                });
            };
            presenter.BeginInvokeOnMainThread(() => presenter.PresentViewController(picker, true, null));
        });
    }
    static string CopySelection(NSUrl url, string batch)
    {
        bool scoped = url.StartAccessingSecurityScopedResource();
        try
        {
            using var coordinator = new NSFileCoordinator();
            string? result = null;
            Exception? failure = null;
            coordinator.CoordinateRead(url, NSFileCoordinatorReadingOptions.WithoutChanges, out var error, readable =>
            {
                try
                {
                    string source = readable.Path ?? throw new IOException("The selected document has no file path.");
                    string name = Path.GetFileName(source.TrimEnd('/'));
                    if (string.IsNullOrEmpty(name) || name is "." or "..") throw new IOException("Invalid document name.");
                    string target = Path.Combine(batch, name);
                    if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Duplicate document name: " + name);
                    try { CopyLocal(source, target); }
                    catch
                    {
                        if (Directory.Exists(target)) Directory.Delete(target, true);
                        else if (File.Exists(target)) File.Delete(target);
                        throw;
                    }
                    result = target;
                }
                catch (Exception ex) { failure = ex; }
            });
            if (failure != null) throw failure;
            if (error != null) throw new IOException(error.LocalizedDescription);
            return result ?? throw new IOException("Cannot read the selected document.");
        }
        finally { if (scoped) url.StopAccessingSecurityScopedResource(); }
    }
    static void CopyLocal(string source, string target)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Symbolic links cannot be imported: " + Path.GetFileName(source));
        if (Directory.Exists(source))
        {
            Directory.CreateDirectory(target);
            foreach (string child in Directory.EnumerateFileSystemEntries(source))
                CopyLocal(child, Path.Combine(target, Path.GetFileName(child)));
        }
        else File.Copy(source, target);
    }
}
