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
        // mono-nx 通过 Unix BCL 暴露 Horizon，需要显式选择平台，不能仅按 IsLinux() 判断。
        Sdl.SDL_SetHint("SDL_TOUCH_MOUSE_EVENTS", "1");
        if (args.Length != 0) return args;
        string launch = Path.Combine(Root, "launch.args");
        // 每行是一个字面参数，不执行 shell 展开或引号解析。
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
