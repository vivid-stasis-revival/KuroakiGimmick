// 声明 SDL 初始化、窗口及原生对话框接口，并提供错误处理入口。

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
#if KG_IOS
    private const string Lib = "__Internal";
#else
    private const string Lib = "SDL3";
#endif

    // -------------------------------------------------------------------------
    // SDL 初始化
    // -------------------------------------------------------------------------

    /// <summary>标记应用已完成平台入口准备。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_SetMainReady();

    /// <summary>初始化所选 SDL 子系统。</summary>
    /// <param name="flags">所选 SDL 子系统的位掩码。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_Init(uint flags);

    /// <summary>为应用初始化额外的 SDL 子系统。</summary>
    /// <param name="flags">所选 SDL 子系统的位掩码。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_InitSubSystem(uint flags);

    /// <summary>关闭已启动的 SDL 子系统。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_Quit();

    /// <summary>读取当前线程最近一次 SDL 错误的 UTF-8 描述。</summary>
    /// <returns>SDL 持有的 UTF-8 字符串地址，调用方无需释放。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_GetError();

    // -------------------------------------------------------------------------
    // 窗口
    // -------------------------------------------------------------------------

    /// <summary>请求或报告全屏窗口。</summary>
    public const ulong SDL_WINDOW_FULLSCREEN = 0x0000000000000001;

    /// <summary>创建可配合 OpenGL 上下文使用的窗口。</summary>
    public const ulong SDL_WINDOW_OPENGL = 0x0000000000000002;

    /// <summary>创建时保持隐藏的窗口。</summary>
    public const ulong SDL_WINDOW_HIDDEN = 0x0000000000000008;

    /// <summary>不带系统装饰边框的窗口。</summary>
    public const ulong SDL_WINDOW_BORDERLESS = 0x0000000000000010;

    /// <summary>用户可以调整尺寸的窗口。</summary>
    public const ulong SDL_WINDOW_RESIZABLE = 0x0000000000000020;

    /// <summary>请求或报告最小化状态。</summary>
    public const ulong SDL_WINDOW_MINIMIZED = 0x0000000000000040;

    /// <summary>请求或报告最大化状态。</summary>
    public const ulong SDL_WINDOW_MAXIMIZED = 0x0000000000000080;

    /// <summary>允许窗口使用高像素密度的绘制表面。</summary>
    public const ulong SDL_WINDOW_HIGH_PIXEL_DENSITY = 0x0000000000002000;

    /// <summary>窗口保持在普通窗口上方。</summary>
    public const ulong SDL_WINDOW_ALWAYS_ON_TOP = 0x0000000000010000;

    // 这两个标志是给"自己直接管理 Vulkan/Metal 上下文"的程序用的。
    // SDL_GPU 不需要我们自己创建那些上下文，所以 CreateGpuWindow 会把它们剥掉。
    /// <summary>创建可配合 Vulkan 使用的窗口。</summary>
    public const ulong SDL_WINDOW_VULKAN = 0x0000000010000000;

    /// <summary>创建可配合 Metal 使用的窗口。</summary>
    public const ulong SDL_WINDOW_METAL = 0x0000000020000000;

    /// <summary>按指定尺寸与标志创建 SDL 窗口。</summary>
    /// <param name="title">窗口或对话框标题。</param>
    /// <param name="w">窗口宽度，单位为窗口坐标。</param>
    /// <param name="h">窗口高度，单位为窗口坐标。</param>
    /// <param name="flags">创建窗口时使用的 SDL 窗口标志。</param>
    /// <returns>成功时返回原生句柄，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateWindow(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        int w,
        int h,
        ulong flags);

    /// <summary>获取与窗口关联的属性集合。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <returns>窗口属性集合 ID，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint SDL_GetWindowProperties(nint window);

    /// <summary>读取指针类型的 SDL 属性。</summary>
    /// <param name="properties">SDL 属性集合 ID。</param>
    /// <param name="name">要访问的属性名。</param>
    /// <param name="fallback">指针属性缺失或类型不符时使用的值。</param>
    /// <returns>属性值或传入的回退地址。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_GetPointerProperty(uint properties,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint fallback);

    /// <summary>在 SDL 处理 Windows 消息时调用的原生钩子。</summary>
    /// <param name="user">注册钩子时传入的数据地址。</param>
    /// <param name="message">传入的原生 MSG 地址。</param>
    /// <returns>为 true 时允许 SDL 继续处理消息。</returns>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public delegate bool WindowsMessageHook(nint user, nint message);

    /// <summary>注册或清除 Windows 消息钩子。</summary>
    /// <param name="callback">由 SDL 调用的回调。</param>
    /// <param name="user">原样传给回调的用户数据。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_SetWindowsMessageHook(WindowsMessageHook? callback, nint user);

    /// <summary>销毁窗口并释放其 SDL 资源。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroyWindow(nint window);

    /// <summary>限制窗口客户区的最小尺寸。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="width">客户区宽度。</param>
    /// <param name="height">客户区高度。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetWindowMinimumSize(
        nint window,
        int width,
        int height);

    /// <summary>设置窗口标题文本。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="title">窗口或对话框标题。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetWindowTitle(
        nint window,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

    /// <summary>请求进入或退出全屏模式。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="fullscreen">为 true 时请求全屏，为 false 时请求窗口模式。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetWindowFullscreen(
        nint window,
        [MarshalAs(UnmanagedType.I1)] bool fullscreen);

    /// <summary>查询窗口客户区的逻辑尺寸。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="width">客户区宽度。</param>
    /// <param name="height">客户区高度。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_GetWindowSize(
        nint window,
        out int width,
        out int height);

    /// <summary>查询窗口客户区的物理像素尺寸。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="width">客户区宽度。</param>
    /// <param name="height">客户区高度。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
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
    /// <param name="title">窗口或内容的显示标题。</param>
    /// <param name="width">目标宽度。</param>
    /// <param name="height">目标高度。</param>
    /// <param name="flags">SDL 窗口创建标志。</param>
    /// <returns>创建的 SDL 窗口句柄。</returns>
    /// <exception cref="InvalidOperationException">SDL 无法创建窗口。</exception>
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

    /// <summary>为调用方提供的像素内存创建 SDL_Surface，销毁 surface 后仍由调用方释放像素。</summary>
    /// <param name="width">图像宽度，单位为像素。</param>
    /// <param name="height">图像高度，单位为像素。</param>
    /// <param name="format">像素数据的 SDL 像素格式。</param>
    /// <param name="pixels">由调用方持有的像素数据首地址，须在 surface 使用期间有效。</param>
    /// <param name="pitch">相邻像素行起点之间的字节数。</param>
    /// <returns>成功时返回原生句柄，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateSurfaceFrom(
        int width,
        int height,
        uint format,
        nint pixels,
        int pitch);

    /// <summary>销毁 SDL_Surface。</summary>
    /// <param name="surface">SDL_Surface 句柄。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DestroySurface(nint surface);

    /// <summary>将 surface 中的图像设置为窗口图标。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="surface">SDL_Surface 句柄。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
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
                return GpuShaderFormat.Dxil | GpuShaderFormat.SpirV;

            if (OperatingSystem.IsLinux()) return GpuShaderFormat.SpirV;

            throw new PlatformNotSupportedException();
        }
    }

    // -------------------------------------------------------------------------
    // 计时
    // -------------------------------------------------------------------------

    /// <summary>暂停当前线程指定的毫秒数。</summary>
    /// <param name="ms">线程等待时长，单位为毫秒。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_Delay(uint ms);

    /// <summary>将平台待处理的输入更新到 SDL 事件队列。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_PumpEvents();

    // -------------------------------------------------------------------------
    // 原生对话框
    // -------------------------------------------------------------------------

    /// <summary>
    /// 由 SDL 在用户完成选择后回调，回调线程取决于平台。files 是以 null 结尾的 UTF-8 字符串指针数组，只在回调期间有效；
    /// 委托实例必须由托管侧保活到对话框结束，否则会被 GC 回收造成崩溃。
    /// </summary>
    /// <param name="user">用户提供的上下文地址。</param>
    /// <param name="files">以空指针结尾的 UTF-8 路径数组，出错时可为 0。</param>
    /// <param name="filter">所选过滤器的索引。</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void DialogCallback(
        nint user,
        nint files,
        int filter);

    /// <summary>异步显示文件选择器，完成时通过回调交付路径。</summary>
    /// <param name="callback">由 SDL 调用的回调。</param>
    /// <param name="user">原样传给回调的用户数据。</param>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="filters">文件过滤器数组地址，使用时须至少保持到回调触发。</param>
    /// <param name="count">连续数组中的元素数。</param>
    /// <param name="location">打开对话框时的初始位置，可省略。</param>
    /// <param name="multiple">是否允许选择多项。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ShowOpenFileDialog(
        DialogCallback callback,
        nint user,
        nint window,
        nint filters,
        int count,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? location,
        [MarshalAs(UnmanagedType.I1)] bool multiple);

    /// <summary>异步显示目录选择器，完成时通过回调交付路径。</summary>
    /// <param name="callback">由 SDL 调用的回调。</param>
    /// <param name="user">原样传给回调的用户数据。</param>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="location">打开对话框时的初始位置，可省略。</param>
    /// <param name="multiple">是否允许选择多项。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ShowOpenFolderDialog(
        DialogCallback callback,
        nint user,
        nint window,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? location,
        [MarshalAs(UnmanagedType.I1)] bool multiple);

    /// <summary>异步显示保存对话框，完成时通过回调交付所选位置。</summary>
    /// <param name="callback">由 SDL 调用的回调。</param>
    /// <param name="user">原样传给回调的用户数据。</param>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="filters">文件过滤器数组地址，使用时须至少保持到回调触发。</param>
    /// <param name="count">连续数组中的元素数。</param>
    /// <param name="location">打开对话框时的初始位置，可省略。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ShowSaveFileDialog(
        DialogCallback callback,
        nint user,
        nint window,
        nint filters,
        int count,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? location);

    /// <summary>请求系统使用默认处理程序打开 URL。</summary>
    /// <param name="url">交给系统处理的 URL。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_OpenURL(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string url);

    /// <summary>阻塞显示带单个关闭按钮的原生消息框。</summary>
    /// <param name="flags">消息框图标与显示选项标志。</param>
    /// <param name="title">窗口或对话框标题。</param>
    /// <param name="text">要显示或写入的文本。</param>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
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
    /// <param name="result">紧邻的 SDL 调用返回的成功标志。</param>
    /// <exception cref="InvalidOperationException">SDL 调用返回 false，异常消息采用当前线程的 SDL 错误。</exception>
    public static void Require(bool result)
    {
        if (!result)
            throw new InvalidOperationException(Error);
    }
}
