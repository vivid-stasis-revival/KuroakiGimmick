using System.Runtime.InteropServices;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Core;
using StbImageSharp;

namespace KuroakiGimmick.Native;

/// <summary>进程级窗口和设备所有者。窗口先于 GPU 创建，GPU 先于窗口释放；不持有歌曲状态。</summary>
public sealed class Host : IDisposable
{
    public nint Window { get; }
    public GpuDevice Gpu { get; }
    public string Device { get; }
    bool disposed;
    /// <summary>
    /// 构造顺序即所有权顺序：SDL 初始化 → 窗口 → 图标 → GPU 设备。中途任何一步失败，
    /// catch 都要按相反顺序拆干净（销毁窗口、SDL_Quit）再把异常抛出去，不能留下半初始化的进程。
    /// </summary>
    public Host(bool hidden = false)
    {
        Sdl.SDL_SetMainReady();
        // 0x20 = SDL_INIT_VIDEO。这里只初始化视频子系统，音频由播放路径自行按需初始化。
        Sdl.Require(Sdl.SDL_Init(0x20));
        try
        {
            Window = Sdl.CreateGpuWindow("Kuroaki/Gimmick", 1440, 940,
                Sdl.SDL_WINDOW_RESIZABLE | Sdl.SDL_WINDOW_HIGH_PIXEL_DENSITY | (hidden ? Sdl.SDL_WINDOW_HIDDEN : 0));
            SetIcon();
            Sdl.SDL_SetWindowMinimumSize(Window, 1180, 860);
            Gpu = new(Window, hidden);
            Device = "SDL_GPU / " + Sdl.GetGpuDriverName(Gpu.Handle);
        }
        catch
        {
            if (Window != 0)
            {
                Sdl.SDL_DestroyWindow(Window);
            }
            Sdl.SDL_Quit();
            throw;
        }
    }
    unsafe void SetIcon()
    {
        using var input = File.OpenRead(Path.Combine(Paths.Assets, "App", "Kuroaki.png"));
        var icon = ImageResult.FromStream(input, ColorComponents.RedGreenBlueAlpha);
        // SDL 的 RGBA32 在支持的小端平台上就是 ABGR8888（0x16762004），大端取 0x16462004。
        // 这两个常量按字节序二选一，不是可以随手统一的魔数。
        // https://wiki.libsdl.org/SDL3/SDL_CreateSurfaceFrom
        fixed (byte* pixels = icon.Data)
        {
            var surface = Sdl.SDL_CreateSurfaceFrom(icon.Width, icon.Height, BitConverter.IsLittleEndian ? 0x16762004u : 0x16462004u,
                (nint)pixels, icon.Width * 4);
            if (surface == 0)
            {
                throw new InvalidOperationException("Window icon: " + Sdl.Error);
            }
            try
            {
                Sdl.SDL_SetWindowIcon(Window, surface);
            }
            // 离屏运行和部分窗口管理器不提供图标；设置失败不算错误，但 surface 一定要释放。
            finally
            {
                Sdl.SDL_DestroySurface(surface);
            }
        }
    }
    /// <summary>
    /// 释放次序是硬性不变量：GPU 设备先于窗口释放，窗口销毁之后才 SDL_Quit。
    /// try/finally 保证即使 GPU 释放抛异常，窗口和 SDL 也一定会收尾；disposed 标志保证可重入。
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            Gpu.Dispose();
        }
        finally
        {
            Sdl.SDL_DestroyWindow(Window);
            Sdl.SDL_Quit();
        }
    }
}
