using System.Runtime.InteropServices;
namespace KuroakiGimmick.Native;
/// <summary>
/// 显示缩放、像素密度与鼠标捕获的 ABI 面。两个缩放值含义不同，不能互相替代：
/// DisplayScale 是用户设定的内容缩放（界面按它排版），PixelDensity 是逻辑点到物理像素的比例（帧缓冲按它分配）。
/// </summary>
public static partial class Sdl
{
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern float SDL_GetWindowDisplayScale(nint window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern float SDL_GetWindowPixelDensity(nint window);
    /// <summary>捕获期间鼠标事件即使移出窗口也继续投递；拖拽结束必须显式关掉，否则窗口会一直吞掉全局鼠标。</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_CaptureMouse([MarshalAs(UnmanagedType.I1)] bool enabled);
}
