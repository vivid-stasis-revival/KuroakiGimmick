// 显示缩放、像素密度与鼠标捕获的 ABI 面。两个缩放值含义不同，不能互相替代：
// DisplayScale 是用户设定的内容缩放（界面按它排版），PixelDensity 是逻辑点到物理像素的比例（帧缓冲按它分配）。

using System.Runtime.InteropServices;
namespace KuroakiGimmick.Native;

public static partial class Sdl
{
    /// <summary>查询窗口内容应采用的显示缩放倍率。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <returns>内容缩放倍率，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern float SDL_GetWindowDisplayScale(nint window);
    /// <summary>查询窗口物理像素与窗口坐标单位的比例。</summary>
    /// <param name="window">目标 SDL 窗口句柄。</param>
    /// <returns>物理像素与窗口坐标的比例，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern float SDL_GetWindowPixelDensity(nint window);
    /// <summary>捕获期间鼠标事件即使移出窗口也继续投递；拖拽结束必须显式关掉，否则窗口会一直吞掉全局鼠标。</summary>
    /// <param name="enabled">是否启用鼠标捕获。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_CaptureMouse([MarshalAs(UnmanagedType.I1)] bool enabled);
}
