#if KG_SWITCH
using System.Runtime.InteropServices;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Native;

internal static class SwitchPlatform
{
    internal static string Root => Environment.GetEnvironmentVariable("KG_SWITCH_ROOT")
        ?? Path.GetDirectoryName(typeof(SwitchPlatform).Assembly.Location)
        ?? "/switch/KuroakiGimmick";

    [DllImport("KGSwitch", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void kg_pump_input(nint window);
    [DllImport("KGSwitch", CallingConvention = CallingConvention.Cdecl)]
    static extern nint kg_prompt_path([MarshalAs(UnmanagedType.LPUTF8Str)] string initial,
        [MarshalAs(UnmanagedType.I1)] bool save);

    internal static string[] Initialize(string[] args)
    {
        Directory.CreateDirectory(Path.Combine(Root, "UserData"));
        Directory.CreateDirectory(Path.Combine(Root, "Exports"));
        Directory.CreateDirectory(Path.Combine(Root, "Temp"));
        Sdl.ForceGpuDriver("vulkan");
        // mono-nx reports Horizon using its Unix BCL. Select explicitly, never infer from IsLinux().
        Sdl.SDL_SetHint("SDL_TOUCH_MOUSE_EVENTS", "1");
        if (args.Length != 0) return args;
        string launch = Path.Combine(Root, "launch.args");
        // One literal argument per line; no shell expansion or quote parsing.
        return File.Exists(launch) ? File.ReadAllLines(launch).Where(l => l.Length > 0).ToArray() : [];
    }

    internal static void PumpInput(nint window) => kg_pump_input(window);
    internal static string? PromptPath(bool save, string? initial)
    {
        nint value = kg_prompt_path(initial ?? Path.Combine(Root, "Songs"), save);
        string? path = value == 0 ? null : Marshal.PtrToStringUTF8(value);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
#endif
