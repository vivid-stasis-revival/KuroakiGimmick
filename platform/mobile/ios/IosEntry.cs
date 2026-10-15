using System.Runtime.InteropServices;
using Foundation;
using UIKit;
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
            MobilePlatform.Configure(root, bundled, Pick);
            MobilePlatform.InstallShaders(bundled);
            MobilePlatform.StartFrameLoop = StartFrames;
            Directory.CreateDirectory(Path.Combine(root, "UserData"));
            Console.SetError(TextWriter.Synchronized(new StreamWriter(Path.Combine(root, "UserData", "mobile.log"), append: true) { AutoFlush = true }));
            string installed = MobileAssetPack.Ready(bundled) ? bundled : Path.Combine(root, "Assets");
            if (!MobileAssetPack.Ready(installed))
            {
                UIApplication.SharedApplication.BeginInvokeOnMainThread(() => RequestAssets(root));
                return 0;
            }
            MobilePlatform.Configure(root, installed, Pick);
            return Program.RunMobile(MobilePlatform.LaunchArguments());
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void RequestAssets(string root)
    {
        string installed = Path.Combine(root, "Assets");
        if (dialogWindow != null) { dialogWindow.Hidden = true; dialogWindow.Dispose(); dialogWindow = null; }
        var controller = DialogPresenter();
        var alert = UIAlertController.Create("Install resource pack",
            "In Files, copy your iOS resource pack's Assets folder (including MobileShaders) to On My iPad/iPhone → Kuroaki/Gimmick. Then tap Retry.", UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create("Retry", UIAlertActionStyle.Default, _ =>
        {
            if (!MobileAssetPack.Ready(installed)) { RequestAssets(root); return; }
            if (dialogWindow != null) { dialogWindow.Hidden = true; dialogWindow.Dispose(); dialogWindow = null; }
            MobilePlatform.Configure(root, installed, Pick);
            try { Program.RunMobile(MobilePlatform.LaunchArguments()); }
            catch (Exception ex) { Console.Error.WriteLine(ex); RequestAssets(root); }
        }));
        controller.PresentViewController(alert, true, null);
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
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            if (dialogWindow != null) { dialogWindow.Hidden = true; dialogWindow.Dispose(); dialogWindow = null; }
            nint pointer = host == null ? 0 : Sdl.SDL_GetPointerProperty(Sdl.SDL_GetWindowProperties(host.Window), "SDL.window.uikit.window", 0);
            if (pointer != 0) ObjCRuntime.Runtime.GetNSObject<UIWindow>(pointer)?.MakeKeyAndVisible();
        });
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
            ShowFiles(presenter, MobilePlatform.Root, folder, completion);
        });
    }
    static void ShowFiles(UIViewController presenter, string directory, bool folder, Action<string[]> completion)
    {
        var alert = UIAlertController.Create("Open file / song folder",
            "Copy songs or ZIPs with Files → On My iPhone/iPad → Kuroaki/Gimmick.\n" + Path.GetRelativePath(MobilePlatform.Root, directory), UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, _ => { CloseDialogWindow(); completion([]); }));
        if (directory != MobilePlatform.Root)
        {
            alert.AddAction(UIAlertAction.Create("↑ Parent folder", UIAlertActionStyle.Default, _ =>
                presenter.BeginInvokeOnMainThread(() => ShowFiles(presenter, Path.GetDirectoryName(directory)!, folder, completion))));
            alert.AddAction(UIAlertAction.Create("Open this folder", UIAlertActionStyle.Default, _ => FinishSelection(directory, completion)));
        }
        foreach (string entry in Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
        {
            string path = entry;
            alert.AddAction(UIAlertAction.Create("[Folder] " + Path.GetFileName(path), UIAlertActionStyle.Default, _ =>
                presenter.BeginInvokeOnMainThread(() => ShowFiles(presenter, path, folder, completion))));
        }
        if (!folder)
            foreach (string entry in Directory.EnumerateFiles(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                string path = entry;
                alert.AddAction(UIAlertAction.Create(Path.GetFileName(path), UIAlertActionStyle.Default, _ => FinishSelection(path, completion)));
            }
        presenter.BeginInvokeOnMainThread(() => presenter.PresentViewController(alert, true, null));
    }
    static void FinishSelection(string path, Action<string[]> completion)
    {
        CloseDialogWindow();
        Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(path)) MobilePlatform.InstallShaders(path);
                completion([MobilePlatform.ExpandImport(path)]);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); completion([]); }
        });
    }
}
