using System.Runtime.InteropServices;

namespace KuroakiGimmick.Native;

/// <summary>
/// 经过审查的最小 SDL3 ABI 面。
///
/// 图形后端：
///   macOS   -> SDL_GPU Metal
///   Windows -> SDL_GPU Direct3D 12，失败回退 Vulkan
///
/// 重要：
/// 本文件刻意不暴露任何 SDL_GL_*。
/// 渲染代码必须走 SDL_GPU，不允许自建 OpenGL 上下文。
/// 这里的所有声明都是 ABI 镜像：签名、参数顺序、[MarshalAs] 一律照抄 SDL 头文件，
/// 不要为了"好看"改签名，也不要在这一层夹带渲染策略。
/// </summary>
public static partial class Sdl
{
    private const string Lib = "SDL3";

    // -------------------------------------------------------------------------
    // SDL 初始化
    // -------------------------------------------------------------------------

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_SetMainReady();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_Init(uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_InitSubSystem(uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_Quit();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_GetError();

    // -------------------------------------------------------------------------
    // 窗口
    // -------------------------------------------------------------------------

    public const ulong SDL_WINDOW_FULLSCREEN         = 0x0000000000000001;

    public const ulong SDL_WINDOW_OPENGL             = 0x0000000000000002;

    public const ulong SDL_WINDOW_HIDDEN             = 0x0000000000000008;

    public const ulong SDL_WINDOW_BORDERLESS         = 0x0000000000000010;

    public const ulong SDL_WINDOW_RESIZABLE          = 0x0000000000000020;

    public const ulong SDL_WINDOW_MINIMIZED          = 0x0000000000000040;

    public const ulong SDL_WINDOW_MAXIMIZED          = 0x0000000000000080;

    public const ulong SDL_WINDOW_HIGH_PIXEL_DENSITY = 0x0000000000002000;

    public const ulong SDL_WINDOW_ALWAYS_ON_TOP      = 0x0000000000010000;

    // 这两个标志是给"自己直接管理 Vulkan/Metal 上下文"的程序用的。
    // SDL_GPU 不需要我们自己创建那些上下文，所以 CreateGpuWindow 会把它们剥掉。
    public const ulong SDL_WINDOW_VULKAN             = 0x0000000010000000;

    public const ulong SDL_WINDOW_METAL              = 0x0000000020000000;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateWindow(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        int w,
        int h,
        ulong flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint SDL_GetWindowProperties(nint window);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_GetPointerProperty(uint properties,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint fallback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public delegate bool WindowsMessageHook(nint user, nint message);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_SetWindowsMessageHook(WindowsMessageHook? callback, nint user);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroyWindow(nint window);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetWindowMinimumSize(
        nint window,
        int width,
        int height);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetWindowTitle(
        nint window,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetWindowFullscreen(
        nint window,
        [MarshalAs(UnmanagedType.I1)] bool fullscreen);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_GetWindowSize(
        nint window,
        out int width,
        out int height);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_GetWindowSizeInPixels(
        nint window,
        out int width,
        out int height);

    /// <summary>
    /// 创建适合 SDL_GPU 使用的窗口。
    ///
    /// 剥掉图形上下文相关的标志是刻意的：
    /// Metal/D3D12/Vulkan 的呈现层由 SDL_GPU 自己拥有，窗口再带这些标志会和它抢。
    /// </summary>
    public static nint CreateGpuWindow(
        string title,
        int width,
        int height,
        ulong flags = SDL_WINDOW_RESIZABLE | SDL_WINDOW_HIGH_PIXEL_DENSITY)
    {
        flags &= ~(
            SDL_WINDOW_OPENGL |
            SDL_WINDOW_VULKAN |
            SDL_WINDOW_METAL);

        nint window = SDL_CreateWindow(title, width, height, flags);

        if (window == 0)
            throw new InvalidOperationException(
                $"SDL_CreateWindow failed: {Error}");

        return window;
    }

    // -------------------------------------------------------------------------
    // Surface / 图标
    // -------------------------------------------------------------------------

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateSurfaceFrom(
        int width,
        int height,
        uint format,
        nint pixels,
        int pitch);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroySurface(nint surface);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetWindowIcon(
        nint window,
        nint surface);

    /// <summary>
    /// 当前平台期望的原生 shader 格式。
    ///
    /// 重要：
    /// 这些标志表示"本应用能提供这些格式"，不是"驱动支持哪些格式"。
    /// shader 加载器实际产不出来的格式，绝对不要在这里声明。
    /// </summary>
    public static GpuShaderFormat PlatformShaderFormats
    {
        get
        {
            // 强制后端时只声明那一个后端要的格式：候选表里也只剩它，多声明的格式不会被用到。
            // SPIR-V 在所有平台都产得出来（shaderc 是公共前端，MSL / DXBC 都是从它转译的），
            // 所以 macOS 上强制 Vulkan 不需要额外的 shader 支持，缺的只是机器上的 MoltenVK。
            if (ForcedGpuDriver is { } forced)
                return ShaderFormatFor(forced);

            if (OperatingSystem.IsMacOS())
                return GpuShaderFormat.Msl;

            if (OperatingSystem.IsWindows())
                return GpuShaderFormat.Dxbc | GpuShaderFormat.SpirV;

            throw new PlatformNotSupportedException();
        }
    }

    // -------------------------------------------------------------------------
    // 计时
    // -------------------------------------------------------------------------

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_Delay(uint ms);

    // -------------------------------------------------------------------------
    // 原生对话框
    // -------------------------------------------------------------------------

    /// <summary>
    /// 由 SDL 在主线程回调。files 是以 null 结尾的 UTF-8 字符串指针数组，只在回调期间有效；
    /// 委托实例必须由托管侧保活到对话框结束，否则会被 GC 回收造成崩溃。
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void DialogCallback(
        nint user,
        nint files,
        int filter);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ShowOpenFileDialog(
        DialogCallback callback,
        nint user,
        nint window,
        nint filters,
        int count,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? location,
        [MarshalAs(UnmanagedType.I1)] bool multiple);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ShowOpenFolderDialog(
        DialogCallback callback,
        nint user,
        nint window,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? location,
        [MarshalAs(UnmanagedType.I1)] bool multiple);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ShowSaveFileDialog(
        DialogCallback callback,
        nint user,
        nint window,
        nint filters,
        int count,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? location);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_OpenURL(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string url);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_ShowSimpleMessageBox(
        uint flags,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
        nint window);

    // -------------------------------------------------------------------------
    // 错误处理
    // -------------------------------------------------------------------------

    /// <summary>读取 SDL 的线程局部错误串。只有在某个调用已经失败之后读才有意义，SDL 不会自动清空它。</summary>
    public static string Error
    {
        get
        {
            nint ptr = SDL_GetError();

            return ptr == 0
                ? "Unknown SDL error"
                : Marshal.PtrToStringUTF8(ptr) ?? "Unknown SDL error";
        }
    }

    /// <summary>把 SDL 的 bool 返回值转成异常。调用点必须紧贴失败的那次调用，中间不能再插别的 SDL 调用，否则 Error 已被覆盖。</summary>
    public static void Require(bool result)
    {
        if (!result)
            throw new InvalidOperationException(Error);
    }
}
