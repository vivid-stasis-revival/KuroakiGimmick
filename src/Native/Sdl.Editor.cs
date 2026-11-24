// 只给编辑器用的 SDL3 ABI 面。SDL_EVENT_WINDOW_CLOSE_REQUESTED 的值是 0x210；
// 文字输入事件在受支持的 64 位 RID 上是偏移 24 处的 UTF-8 指针（见 Sdl.Input 的 Event.TextData）。
// 这两个常量/偏移都照抄 SDL 头文件，不要凭印象改。

using System.Runtime.InteropServices;
namespace KuroakiGimmick.Native;

public static partial class Sdl
{
    /// <summary>限制调整窗口大小时的最小和最大宽高比。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="minimum">最小宽高比，0 表示不设置此限制。</param>
    /// <param name="maximum">最大宽高比，0 表示不设置此限制。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_SetWindowAspectRatio(nint window, float minimum, float maximum);
    /// <summary>读取窗口当前的 SDL 状态标志。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <returns>当前窗口标志位掩码。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ulong SDL_GetWindowFlags(nint window);
    /// <summary>SDL 提交的窗口关闭请求事件类型。</summary>
    public const uint WindowCloseRequested = 0x210;
    /// <summary>查询 SDL 事件使用的窗口 ID。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <returns>窗口 ID，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint SDL_GetWindowID(nint window);
    /// <summary>读取 SDL 记录的键盘修饰键状态。</summary>
    /// <returns>当前按下的修饰键位掩码。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_GetModState();
    /// <summary>覆盖 SDL 记录的键盘修饰键状态。</summary>
    /// <param name="modstate">要设置的修饰键位掩码。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_SetModState(ushort modstate);
    /// <summary>为窗口启用文本输入事件。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_StartTextInput(nint window);
    /// <summary>停止向窗口发送文本输入事件。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_StopTextInput(nint window);
    /// <summary>返回的是 SDL 自己分配的 UTF-8 缓冲，读完必须交给 SDL_free；不要用 Marshal.FreeHGlobal。</summary>
    /// <returns>UTF-8 文本地址，读取后使用 SDL_free 释放。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint SDL_GetClipboardText();
    /// <summary>将 UTF-8 文本写入系统剪贴板。</summary>
    /// <param name="text">要显示或写入的文本。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_SetClipboardText([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    /// <summary>释放由 SDL 分配并转交给调用方的内存。</summary>
    /// <param name="memory">SDL 分配的内存地址，允许为 0。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_free(nint memory);
    /// <summary>设置窗口在桌面坐标系中的位置。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="x">横坐标。</param>
    /// <param name="y">纵坐标。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_SetWindowPosition(nint window, int x, int y);
    /// <summary>读取窗口在桌面坐标系中的位置。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="x">横坐标。</param>
    /// <param name="y">纵坐标。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_GetWindowPosition(nint window, out int x, out int y);
    /// <summary>设置窗口客户区的逻辑尺寸。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="w">窗口宽度，单位为窗口坐标。</param>
    /// <param name="h">窗口高度，单位为窗口坐标。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_SetWindowSize(nint window, int w, int h);
    /// <summary>使窗口可见。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_ShowWindow(nint window);
    /// <summary>隐藏指定窗口。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_HideWindow(nint window);
    /// <summary>设置窗口是否显示边框。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <param name="value">为 true 时显示边框。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_SetWindowBordered(nint window, [MarshalAs(UnmanagedType.I1)] bool value);
    /// <summary>请求将窗口移到前台。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_RaiseWindow(nint window);
    /// <summary>查询窗口所属的显示器 ID。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <returns>显示器 ID，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint SDL_GetDisplayForWindow(nint window);
    /// <summary>枚举当前连接的显示器。</summary>
    /// <param name="count">接收显示器的数量，不含结尾的 0。</param>
    /// <returns>以 0 结尾的显示器 ID 数组，调用方用 SDL_free 释放；失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint SDL_GetDisplays(out int count);
    /// <summary>桌面坐标系下该显示器的矩形，含多显示器的负坐标；单位是逻辑点，不是物理像素。</summary>
    /// <param name="display">显示器 ID。</param>
    /// <param name="bounds">接收显示器在桌面坐标系中的范围。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_GetDisplayBounds(uint display, out IntRect bounds);
}
