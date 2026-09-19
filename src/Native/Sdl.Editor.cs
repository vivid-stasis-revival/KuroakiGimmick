using System.Runtime.InteropServices;
namespace KuroakiGimmick.Native;

/// <summary>
/// 只给编辑器用的 SDL3 ABI 面。SDL_EVENT_WINDOW_CLOSE_REQUESTED 的值是 0x210；
/// 文字输入事件在受支持的 64 位 RID 上是偏移 24 处的 UTF-8 指针（见 Sdl.Input 的 Event.TextData）。
/// 这两个常量/偏移都照抄 SDL 头文件，不要凭印象改。
/// </summary>
public static partial class Sdl
{
    public const uint WindowCloseRequested = 0x210;
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint SDL_GetWindowID(nint window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_GetModState();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_StartTextInput(nint window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_StopTextInput(nint window);
    /// <summary>返回的是 SDL 自己分配的 UTF-8 缓冲，读完必须交给 SDL_free；不要用 Marshal.FreeHGlobal。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint SDL_GetClipboardText();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_SetClipboardText([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_free(nint memory);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_SetWindowPosition(nint window, int x, int y);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_SetWindowSize(nint window, int w, int h);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_ShowWindow(nint window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_HideWindow(nint window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_SetWindowBordered(nint window, [MarshalAs(UnmanagedType.I1)] bool value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_RaiseWindow(nint window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint SDL_GetDisplayForWindow(nint window);
    /// <summary>桌面坐标系下该显示器的矩形，含多显示器的负坐标；单位是逻辑点，不是物理像素。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SDL_GetDisplayBounds(uint display, out IntRect bounds);
}
