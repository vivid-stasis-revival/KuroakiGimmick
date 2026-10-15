#if KG_MOBILE
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace KuroakiGimmick.Native;

internal static class MobilePlatform
{
    internal static string Root { get; private set; } = "";
    internal static string Assets { get; private set; } = "";
    internal static Action<bool, string?, bool, Action<string[]>> PickFiles { get; private set; } = null!;
#if KG_IOS
    internal static Action<Host, KuroakiGimmick.UI.Viewer> StartFrameLoop { get; set; } = null!;
#endif
    internal static bool Suspended => Volatile.Read(ref suspended);
    static bool suspended;
    static Action? pause;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    delegate bool EventWatch(nint userdata, nint ev);
    static readonly EventWatch watch = Watch;
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    static extern bool SDL_AddEventWatch(EventWatch callback, nint userdata);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)]
    static extern void SDL_RemoveEventWatch(EventWatch callback, nint userdata);
#if KG_IOS
    const string SdlLibrary = "__Internal";
#else
    const string SdlLibrary = "SDL3";
#endif

    internal static void Configure(string root, string assets,
        Action<bool, string?, bool, Action<string[]>> picker)
    {
        Root = Path.GetFullPath(root);
        Assets = Path.GetFullPath(assets);
        PickFiles = picker;
        foreach (var name in new[] { "UserData", "Exports", "Songs", "Imports", "Temp", "MobileShaders" })
            Directory.CreateDirectory(Path.Combine(Root, name));
        Sdl.ForceGpuDriver(OperatingSystem.IsIOS() ? "metal" : "vulkan");
        Sdl.SDL_SetHint("SDL_TOUCH_MOUSE_EVENTS", "1");
        Sdl.SDL_SetHint("SDL_MOUSE_TOUCH_EVENTS", "0");
        Sdl.SDL_SetHint("SDL_ORIENTATIONS", "LandscapeLeft LandscapeRight");
        Sdl.SDL_SetHint("SDL_ANDROID_TRAP_BACK_BUTTON", "1");
        Sdl.SDL_SetHint("SDL_IOS_HIDE_HOME_INDICATOR", "1");
        Directory.SetCurrentDirectory(Root);
    }

    internal static string[] LaunchArguments()
    {
        string path = Path.Combine(Root, "launch.args");
        return File.Exists(path) ? File.ReadAllLines(path).Where(x => x.Length > 0).ToArray() : [];
    }
    internal static void WatchLifecycle(Action pausePlayback)
    {
        pause = pausePlayback;
        Sdl.Require(SDL_AddEventWatch(watch, 0));
    }
    internal static void StopWatchingLifecycle()
    {
        SDL_RemoveEventWatch(watch, 0);
        pause = null;
    }
#if KG_IOS
    [ObjCRuntime.MonoPInvokeCallback(typeof(EventWatch))]
#endif
    static bool Watch(nint userdata, nint ev)
    {
        try
        {
            uint type = unchecked((uint)Marshal.ReadInt32(ev));
            if (type is 0x103 or 0x104) // WILL/DID_ENTER_BACKGROUND
            {
                Volatile.Write(ref suspended, true);
                pause?.Invoke();
            }
            else if (type == 0x106) Volatile.Write(ref suspended, false); // DID_ENTER_FOREGROUND
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
        return true;
    }

    // ZIP song packages preserve relative chart/audio/image paths across both sandboxes.
    internal static string ExpandImport(string path)
    {
        if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return path;
        string destination = Path.Combine(Root, "Songs", Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count > 20000 || archive.Entries.Sum(e => e.Length) > 2L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Song ZIP exceeds the import size limit.");
            // The framework rejects entries whose destination escapes this directory.
            archive.ExtractToDirectory(destination);
            InstallShaders(destination);
            var directories = Directory.GetDirectories(destination);
            return Directory.GetFiles(destination).Length == 0 && directories.Length == 1 ? directories[0] : destination;
        }
        catch
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            throw;
        }
    }

    internal static void InstallShaders(string directory)
    {
        uint format = OperatingSystem.IsIOS() ? (uint)Sdl.GpuShaderFormat.Msl : (uint)Sdl.GpuShaderFormat.SpirV;
        foreach (string file in Directory.EnumerateFiles(directory, "*.kgms", SearchOption.AllDirectories))
        {
            using var reader = new BinaryReader(File.OpenRead(file));
            if (reader.BaseStream.Length < 12 || reader.ReadInt32() != 0x48534D4B || reader.ReadInt32() != 1 || reader.ReadUInt32() != format) continue;
            string target = Path.Combine(Root, "MobileShaders", Path.GetFileName(file));
            // Existing entries remain until a complete replacement is available.
            string temporary = target + ".tmp";
            File.Copy(file, temporary, true);
            File.Move(temporary, target, true);
        }
    }

    internal static string SavePath(string? suggested, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name is "." or ".." || name.Contains('/') || name.Contains('\\'))
            throw new ArgumentException("Enter a file name without directories.");
        if (!Path.HasExtension(name) && suggested != null) name += Path.GetExtension(suggested);
        return Path.Combine(Root, "Exports", name);
    }
}
#endif
