using System.Runtime.InteropServices;
using KuroakiGimmick.Core;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// Owns only dock workspace windows. Window Motion previews have a separate registry and lifecycle.
/// SDL and GPU operations are confined to the thread that created this manager.
/// </summary>
public sealed partial class WorkspaceWindowManager : IDisposable
{
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
    public sealed class Window(string key, nint handle, bool main)
    {
        public string Key { get; } = key;
        public nint Handle { get; } = handle;
        public uint Id { get; } = Sdl.SDL_GetWindowID(handle);
        public bool Main { get; } = main;
        public bool Focused { get; internal set; }
        public bool Fullscreen { get; internal set; }
        public bool CleanPreview { get; internal set; }
        public bool ShowPreviewControls { get; set; }
        public string Title { get; internal set; } = "";
        public float MouseX { get; internal set; }
        public float MouseY { get; internal set; }
        public double UiScale { get; set; } = 1;
        internal bool Shown { get; set; }
    }

    readonly GpuDevice gpu;
    readonly int threadId = Environment.CurrentManagedThreadId;
    readonly Dictionary<uint, Window> byId = [];
    readonly Dictionary<string, Window> byKey = new(StringComparer.Ordinal);
    public Window Main { get; }
    public IReadOnlyCollection<Window> Windows => byId.Values;

    public WorkspaceWindowManager(GpuDevice gpu, nint mainWindow)
    {
        this.gpu = gpu;
        Main = new(DockLayout.MainWindow, mainWindow, true) { Shown = true };
        byId.Add(Main.Id, Main); byKey.Add(Main.Key, Main);
    }
    void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != threadId)
            throw new InvalidOperationException("Workspace SDL windows must be accessed on their creation thread.");
    }
    public bool TryGet(uint windowId, out Window? window) => byId.TryGetValue(windowId, out window);
    public bool TryGet(string key, out Window? window) => byKey.TryGetValue(key, out window);
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

    /// <summary>A new native window stays hidden until its first complete UI frame is ready.</summary>
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
                // Wayland and some macOS spaces may reject absolute positioning; the window remains usable.
                Sdl.SDL_SetWindowPosition(handle, (int)recovered.X, (int)recovered.Y);
                uiScale = saved.UiScale;
            }
            Sdl.Require(Sdl.SDL_ClaimWindowForGPUDevice(gpu.Handle, handle));
            claimed = true;
            var mode = Sdl.SDL_WindowSupportsGPUPresentMode(gpu.Handle, handle, Sdl.GpuPresentMode.Immediate)
                ? Sdl.GpuPresentMode.Immediate : Sdl.GpuPresentMode.VSync;
            Sdl.Require(Sdl.SDL_SetGPUSwapchainParameters(gpu.Handle, handle, Sdl.GpuSwapchainComposition.Sdr, mode));
            var window = new Window(key, handle, false) { UiScale = double.IsFinite(uiScale) ? Math.Clamp(uiScale, .75, 2.5) : 1 };
            byId.Add(window.Id, window); byKey.Add(key, window);
            return window;
        }
        catch
        {
            if (claimed) Sdl.SDL_ReleaseWindowFromGPUDevice(gpu.Handle, handle);
            Sdl.SDL_DestroyWindow(handle);
            throw;
        }
    }

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
        finally { Sdl.SDL_free(displays); }
    }

    public DockWindowFile? TryCapture(Window window, string rootId)
    {
        CheckThread();
        if (!Sdl.SDL_GetWindowPosition(window.Handle, out int x, out int y) ||
            !Sdl.SDL_GetWindowSize(window.Handle, out int width, out int height)) return null;
        return new DockWindowFile { Id = rootId, X = x, Y = y, Width = width, Height = height,
            Display = Sdl.SDL_GetDisplayForWindow(window.Handle), UiScale = window.UiScale };
    }

    public UiViewport Viewport(Window window, WorkspaceLayout options)
    {
        CheckThread();
        Sdl.SDL_GetWindowSize(window.Handle, out int w, out int h);
        Sdl.SDL_GetWindowSizeInPixels(window.Handle, out int pw, out int ph);
        var scale = new WorkspaceLayout { UiScale = window.Main ? options.UiScale : window.UiScale,
            FollowDisplayScale = options.FollowDisplayScale };
        return window.Main
            ? UiViewport.Create(w, h, pw, ph, Sdl.SDL_GetWindowDisplayScale(window.Handle),
                Sdl.SDL_GetWindowPixelDensity(window.Handle), scale)
            : UiViewport.CreateAuxiliary(w, h, pw, ph, Sdl.SDL_GetWindowDisplayScale(window.Handle),
                Sdl.SDL_GetWindowPixelDensity(window.Handle), scale);
    }

    /// <summary>Draws one auxiliary UI frame with the shared Canvas and GPU device.</summary>
    public void Present(Window window, Canvas canvas, WorkspaceLayout options, Action<Window, UiViewport> draw)
    {
        CheckThread();
        if (window.Main || !byId.ContainsKey(window.Id)) throw new ArgumentException("Unknown auxiliary window.", nameof(window));
        var viewport = Viewport(window, options);
        canvas.Begin(null, viewport.PixelWidth, viewport.PixelHeight, viewport.Width, viewport.Height, Color.Hex(0x050507));
        draw(window, viewport);
        canvas.Clip(null); canvas.Flush(); gpu.Submit(true, window.Handle);
        if (!window.Shown) { Sdl.Require(Sdl.SDL_ShowWindow(window.Handle)); window.Shown = true; }
    }

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
    public void Hide(Window window)
    {
        CheckThread();
        if (!window.Main && window.Shown)
        { Sdl.Require(Sdl.SDL_HideWindow(window.Handle)); window.Shown = false; }
    }
    public void Dispose()
    {
        CheckThread();
        foreach (var window in byId.Values.Where(w => !w.Main).ToArray()) Close(window);
    }
}
