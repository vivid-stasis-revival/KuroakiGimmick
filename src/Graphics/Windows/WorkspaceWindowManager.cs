// 在 SDL 线程登记工作区窗口、路由输入并共享 GPU 设备。

using System.Runtime.InteropServices;
using KuroakiGimmick.Core;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Graphics;

/// <summary>管理停靠工作区的窗口，窗口动作预览使用独立注册表与生命周期。SDL 和 GPU 操作限定于创建此管理器的线程。</summary>
public sealed partial class WorkspaceWindowManager : IDisposable
{
    /// <summary>当前平台是否提供独立停靠窗口。</summary>
    public static bool SupportsDetachedWindows
    {
        get
        {
#if KG_SWITCH || KG_MOBILE
            return false;
#else
            return true;
#endif
        }
    }
    /// <summary>记录原生窗口身份及其独立的指针、焦点和呈现状态。</summary>
    /// <param name="key">用于恢复布局的窗口键。</param>
    /// <param name="handle">SDL 窗口句柄。</param>
    /// <param name="main">是否为主窗口。</param>
    public sealed class Window(string key, nint handle, bool main)
    {
        /// <summary>用于持久化窗口布局的稳定键。</summary>
        public string Key { get; } = key;
        /// <summary>供 SDL 调用使用的原生窗口句柄。</summary>
        public nint Handle { get; } = handle;
        /// <summary>输入事件路由使用的 SDL 窗口 ID。</summary>
        public uint Id { get; } = Sdl.SDL_GetWindowID(handle);
        /// <summary>是否为宿主持有的主窗口。</summary>
        public bool Main { get; } = main;
        /// <summary>窗口当前是否获得输入焦点。</summary>
        public bool Focused { get; internal set; }
        /// <summary>是否以全屏模式呈现。</summary>
        public bool Fullscreen { get; internal set; }
        /// <summary>是否仅显示预览画面。</summary>
        public bool CleanPreview { get; internal set; }
        /// <summary>纯预览窗口是否显示播放控件。</summary>
        public bool ShowPreviewControls { get; set; }
        /// <summary>管理器记录的窗口标题。</summary>
        public string Title { get; internal set; } = "";
        /// <summary>指针在窗口逻辑空间内的横坐标。</summary>
        public float MouseX { get; internal set; }
        /// <summary>指针在窗口逻辑空间内的纵坐标。</summary>
        public float MouseY { get; internal set; }
        /// <summary>单独应用到该窗口的界面缩放倍率。</summary>
        public double UiScale { get; set; } = 1;
        internal bool Shown { get; set; }
    }

    readonly GpuDevice gpu;
    readonly int threadId = Environment.CurrentManagedThreadId;
    readonly Dictionary<uint, Window> byId = [];
    readonly Dictionary<string, Window> byKey = new(StringComparer.Ordinal);
    /// <summary>注册到管理器的宿主主窗口。</summary>
    public Window Main { get; }
    /// <summary>当前已登记的主窗口和独立窗口。</summary>
    public IReadOnlyCollection<Window> Windows => byId.Values;

    /// <summary>将宿主窗口注册为工作区主窗口以共享 GPU 设备。</summary>
    /// <param name="gpu">持有渲染资源的 GPU 设备。</param>
    /// <param name="mainWindow">主窗口的 SDL 句柄。</param>
    public WorkspaceWindowManager(GpuDevice gpu, nint mainWindow)
    {
        this.gpu = gpu;
        Main = new(DockLayout.MainWindow, mainWindow, true) { Shown = true };
        byId.Add(Main.Id, Main);
        byKey.Add(Main.Key, Main);
    }
    void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != threadId)
            throw new InvalidOperationException("Workspace SDL windows must be accessed on their creation thread.");
    }
    /// <summary>按 SDL 事件 ID 或布局键查找已登记的窗口。</summary>
    /// <param name="windowId">SDL 事件中的窗口 ID。</param>
    /// <param name="window">输出匹配的工作区窗口。</param>
    /// <returns>找到指定 ID 时为 true。</returns>
    public bool TryGet(uint windowId, out Window? window) => byId.TryGetValue(windowId, out window);
    /// <summary>按 SDL 事件 ID 或布局键查找已登记的窗口。</summary>
    /// <param name="key">布局窗口键。</param>
    /// <param name="window">输出匹配的工作区窗口。</param>
    /// <returns>找到指定键时为 true。</returns>
    public bool TryGet(string key, out Window? window) => byKey.TryGetValue(key, out window);
    /// <summary>将窗口事件反映到对应窗口的焦点和指针状态。</summary>
    /// <param name="e">SDL 事件联合体。</param>
    /// <param name="options">设备本地的工作区布局设置。</param>
    /// <param name="window">输出该事件关联的窗口。</param>
    /// <returns>事件所属窗口已登记时为 true。</returns>
    public bool Observe(Sdl.Event e, WorkspaceLayout options, out Window? window)
    {
        CheckThread();
        if (!byId.TryGetValue(e.WindowID, out window)) return false;
        if (e.Type is 0x400 or 0x401 or 0x402)
        {
            var vp = Viewport(window, options);
            window.MouseX = e.X * vp.InputScaleX;
            window.MouseY = e.Y * vp.InputScaleY;
        }
        if (e.Type == 0x20E) window.Focused = true;
        if (e.Type == 0x20F) window.Focused = false;
        return true;
    }

    /// <summary>新的原生窗口在首个完整界面帧就绪前保持隐藏。</summary>
    /// <param name="key">新窗口的稳定键。</param>
    /// <param name="title">窗口或内容的显示标题。</param>
    /// <param name="width">目标宽度。</param>
    /// <param name="height">目标高度。</param>
    /// <param name="uiScale">独立窗口 UI 缩放倍率。</param>
    /// <param name="saved">可选的已保存窗口几何。</param>
    /// <param name="minimumWidth">允许的最小宽度。</param>
    /// <param name="minimumHeight">允许的最小高度。</param>
    /// <returns>已注册并绑定到当前设备的独立窗口。</returns>
    /// <exception cref="PlatformNotSupportedException">当前平台不支持独立停靠窗口。</exception>
    /// <exception cref="ArgumentException">窗口键无效或已经注册。</exception>
    public Window Create(string key, string title, int width, int height, double uiScale = 1,
        DockWindowFile? saved = null, int minimumWidth = 240, int minimumHeight = 160)
    {
        CheckThread();
        if (!SupportsDetachedWindows) throw new PlatformNotSupportedException("Native dock windows require a desktop platform.");
        if (string.IsNullOrWhiteSpace(key) || key == DockLayout.MainWindow || byKey.ContainsKey(key) || byKey.Count >= 33)
            throw new ArgumentException("Invalid or duplicate workspace window key.", nameof(key));
        minimumWidth = Math.Clamp(minimumWidth, 240, 2000);
        minimumHeight = Math.Clamp(minimumHeight, 160, 1500);
        nint handle = Sdl.CreateGpuWindow(title, Math.Clamp(width, minimumWidth, 10000),
            Math.Clamp(height, minimumHeight, 10000),
            Sdl.SDL_WINDOW_HIDDEN | Sdl.SDL_WINDOW_RESIZABLE | Sdl.SDL_WINDOW_HIGH_PIXEL_DENSITY);
        bool claimed = false;
        try
        {
            Sdl.Require(Sdl.SDL_SetWindowMinimumSize(handle, minimumWidth, minimumHeight));
            if (saved != null)
            {
                var recovered = DockLayout.RecoverBounds(saved, DisplayBounds());
                Sdl.SDL_SetWindowSize(handle, (int)recovered.W, (int)recovered.H);
                // Wayland 或某些 macOS 空间可能拒绝绝对窗口定位，窗口仍能正常使用。
                Sdl.SDL_SetWindowPosition(handle, (int)recovered.X, (int)recovered.Y);
                uiScale = saved.UiScale;
            }
            Sdl.Require(Sdl.SDL_ClaimWindowForGPUDevice(gpu.Handle, handle));
            claimed = true;
            var mode = Sdl.SDL_WindowSupportsGPUPresentMode(gpu.Handle, handle, Sdl.GpuPresentMode.Immediate)
                ? Sdl.GpuPresentMode.Immediate : Sdl.GpuPresentMode.VSync;
            Sdl.Require(Sdl.SDL_SetGPUSwapchainParameters(gpu.Handle, handle, Sdl.GpuSwapchainComposition.Sdr, mode));
            var window = new Window(key, handle, false) { UiScale = double.IsFinite(uiScale) ? Math.Clamp(uiScale, .75, 2.5) : 1 };
            byId.Add(window.Id, window);
            byKey.Add(key, window);
            return window;
        }
        catch
        {
            if (claimed) Sdl.SDL_ReleaseWindowFromGPUDevice(gpu.Handle, handle);
            Sdl.SDL_DestroyWindow(handle);
            throw;
        }
    }

    /// <summary>枚举显示器可用于布局恢复的桌面矩形。</summary>
    /// <returns>当前显示器在桌面坐标系中的矩形列表。</returns>
    public IReadOnlyList<Rect> DisplayBounds()
    {
        CheckThread();
        nint displays = Sdl.SDL_GetDisplays(out int count);
        var bounds = new List<Rect>();
        try
        {
            if (displays == 0 || count is < 0 or > 32) return bounds;
            for (int index = 0; index < count; index++)
            {
                uint display = unchecked((uint)Marshal.ReadInt32(displays, index * 4));
                if (Sdl.SDL_GetDisplayBounds(display, out var r) && r.w > 0 && r.h > 0)
                    bounds.Add(new(r.x, r.y, r.w, r.h));
            }
            return bounds;
        }
        finally
        {
            Sdl.SDL_free(displays);
        }
    }

    /// <summary>获取可持久化的窗口几何，查询失败时可返回 null。</summary>
    /// <param name="window">已登记的工作区窗口。</param>
    /// <param name="rootId">布局树的根标识。</param>
    /// <returns>窗口几何快照，无法取得时为 null。</returns>
    public DockWindowFile? TryCapture(Window window, string rootId)
    {
        CheckThread();
        if (!Sdl.SDL_GetWindowPosition(window.Handle, out int x, out int y) ||
            !Sdl.SDL_GetWindowSize(window.Handle, out int width, out int height)) return null;
        return new DockWindowFile
        {
            Id = rootId,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Display = Sdl.SDL_GetDisplayForWindow(window.Handle),
            UiScale = window.UiScale
        };
    }

    /// <summary>根据窗口实际尺寸与缩放偏好求取界面视口。</summary>
    /// <param name="window">已登记的工作区窗口。</param>
    /// <param name="options">设备本地的工作区布局设置。</param>
    /// <returns>根据窗口与设备偏好计算的逻辑视口。</returns>
    public UiViewport Viewport(Window window, WorkspaceLayout options)
    {
        CheckThread();
        Sdl.SDL_GetWindowSize(window.Handle, out int w, out int h);
        Sdl.SDL_GetWindowSizeInPixels(window.Handle, out int pw, out int ph);
        var scale = new WorkspaceLayout
        {
            UiScale = window.Main ? options.UiScale : window.UiScale,
            FollowDisplayScale = options.FollowDisplayScale
        };
        return window.Main
            ? UiViewport.Create(w, h, pw, ph, Sdl.SDL_GetWindowDisplayScale(window.Handle),
                Sdl.SDL_GetWindowPixelDensity(window.Handle), scale)
            : UiViewport.CreateAuxiliary(w, h, pw, ph, Sdl.SDL_GetWindowDisplayScale(window.Handle),
                Sdl.SDL_GetWindowPixelDensity(window.Handle), scale);
    }

    /// <summary>使用共享 Canvas 和 GPU 设备向辅助窗口呈现一帧界面。</summary>
    /// <param name="window">已登记的工作区窗口。</param>
    /// <param name="canvas">提交绘制命令的画布。</param>
    /// <param name="options">设备本地的工作区布局设置。</param>
    /// <param name="draw">在该窗口视口内绘制一帧界面的回调。</param>
    /// <exception cref="ArgumentException">窗口不是已登记的辅助窗口。</exception>
    public void Present(Window window, Canvas canvas, WorkspaceLayout options, Action<Window, UiViewport> draw)
    {
        CheckThread();
        if (window.Main || !byId.ContainsKey(window.Id)) throw new ArgumentException("Unknown auxiliary window.", nameof(window));
        var viewport = Viewport(window, options);
        canvas.Begin(null, viewport.PixelWidth, viewport.PixelHeight, viewport.Width, viewport.Height, Color.Hex(0x050507));
        draw(window, viewport);
        canvas.Clip(null);
        canvas.Flush();
        gpu.Submit(true, window.Handle);
        if (!window.Shown)
        {
            Sdl.Require(Sdl.SDL_ShowWindow(window.Handle));
            window.Shown = true;
        }
    }

    /// <summary>提交待处理的绘制并等待交换链，解除 GPU 绑定后销毁独立窗口。</summary>
    /// <param name="window">已登记的工作区窗口。</param>
    public void Close(Window window)
    {
        CheckThread();
        if (window.Main || !byId.Remove(window.Id)) return;
        byKey.Remove(window.Key);
        gpu.Submit();
        Sdl.Require(Sdl.SDL_WaitForGPUSwapchain(gpu.Handle, window.Handle));
        Sdl.SDL_ReleaseWindowFromGPUDevice(gpu.Handle, window.Handle);
        Sdl.SDL_DestroyWindow(window.Handle);
    }
    /// <summary>隐藏已显示的独立窗口并保留其句柄。</summary>
    /// <param name="window">已登记的工作区窗口。</param>
    public void Hide(Window window)
    {
        CheckThread();
        if (!window.Main && window.Shown)
        {
            Sdl.Require(Sdl.SDL_HideWindow(window.Handle));
            window.Shown = false;
        }
    }
    /// <summary>在窗口线程关闭管理器中的所有独立窗口。</summary>
    public void Dispose()
    {
        CheckThread();
        foreach (var window in byId.Values.Where(w => !w.Main).ToArray()) Close(window);
    }
}
