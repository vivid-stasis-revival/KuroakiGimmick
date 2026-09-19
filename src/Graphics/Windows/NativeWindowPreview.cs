using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Windows;
using KuroakiGimmick.Native;
namespace KuroakiGimmick.Graphics;

/// <summary>
/// 真正独立呈现的 SDL_GPU 窗口。本后端从不加载 GameMaker 的 OpenGL/D3D11 ECG DLL。
/// 编辑器宿主窗口始终可见且不移动；原生 id 0 由一个单独的预览窗口代表。
/// 所有窗口的创建、移动、呈现与销毁都在 SDL 主线程上完成。
/// 全部附加窗口共用同一个 GpuDevice，不会各自建设备。
/// </summary>
public sealed class NativeWindowPreview : IDisposable
{
    /// <summary>已创建窗口的最后一次呈现状态，用于跳过重复的 SDL 调用；X 初值 int.MinValue 表示"还没定过位置"。</summary>
    sealed class Entry(nint handle)
    {
        public nint Handle = handle;
        public int X = int.MinValue, Y, W, H;
        public bool Visible, Border = true;
        public string Title = "";
    }
    readonly GpuDevice gpu;
    readonly nint owner;
    readonly Dictionary<int, Entry> windows = [];
    string orderKey = "";
    /// 构造时记下宿主窗口所在显示器的桌面矩形，作为所有窗口坐标的参考系；不随后续换屏更新。
    public Sdl.IntRect Bounds { get; }
    public bool HasWindows => windows.Count > 0;
    public NativeWindowPreview(GpuDevice gpu, nint owner)
    {
        this.gpu = gpu; this.owner = owner;
        Sdl.Require(Sdl.SDL_GetDisplayBounds(Sdl.SDL_GetDisplayForWindow(owner), out var b)); Bounds = b;
    }
    public bool Owns(uint id) => windows.Values.Any(w => Sdl.SDL_GetWindowID(w.Handle) == id);
    /// <summary>
    /// 把这一帧的窗口姿态同步到真实窗口：多出来的窗口先关，缺的按需创建，其余只下发变化的属性。
    /// 每个窗口都 claim 到同一个 gpu.Handle 上，呈现走 gpu.Submit(true, handle)，一个窗口一次提交。
    /// 姿态坐标是 16:9 参考矩形内的归一化值（X/Y 为中心点），最多处理 64 个窗口；
    /// 非有限数值直接跳过，不去夹紧成 0。
    /// </summary>
    public void Present(Session session, double time, IReadOnlyList<WindowPose> poses, Canvas canvas, SceneRenderer renderer)
    {
        double cw = Math.Min(Bounds.w, Bounds.h * 16.0 / 9), ch = cw * 9 / 16;
        double ox = Bounds.x + (Bounds.w - cw) / 2, oy = Bounds.y + (Bounds.h - ch) / 2;
        var ids = poses.Select(p => p.Id).ToHashSet();
        foreach (int id in windows.Keys.Where(id => !ids.Contains(id)).ToArray()) Close(id);
        foreach (var p in poses.Take(64))
        {
            if (!double.IsFinite(p.X + p.Y + p.Width + p.Height)) continue;
            if (!windows.TryGetValue(p.Id, out var entry))
            {
                if (!p.Visible) continue;
                nint handle = Sdl.CreateGpuWindow(p.Title, 640, 360, Sdl.SDL_WINDOW_HIDDEN | Sdl.SDL_WINDOW_HIGH_PIXEL_DENSITY);
                if (handle == 0) throw new InvalidOperationException(Sdl.Error);
                if (!Sdl.SDL_ClaimWindowForGPUDevice(gpu.Handle, handle))
                { Sdl.SDL_DestroyWindow(handle); throw new InvalidOperationException(Sdl.Error); }
                entry = new(handle); windows.Add(p.Id, entry);
                // 帧率由编辑器宿主窗口控制。附加窗口不能各自再等一个刷新间隔，否则每多一个窗口就多掉一帧，
                // 所以支持 Immediate 就用 Immediate，不支持才退回 VSync。
                var mode = Sdl.SDL_WindowSupportsGPUPresentMode(gpu.Handle, handle, Sdl.GpuPresentMode.Immediate)
                    ? Sdl.GpuPresentMode.Immediate : Sdl.GpuPresentMode.VSync;
                Sdl.Require(Sdl.SDL_SetGPUSwapchainParameters(gpu.Handle, handle, Sdl.GpuSwapchainComposition.Sdr, mode));
            }
            if (entry.Title != p.Title) { Sdl.Require(Sdl.SDL_SetWindowTitle(entry.Handle, p.Title)); entry.Title = p.Title; }
            if (entry.Border != p.Border) { Sdl.Require(Sdl.SDL_SetWindowBordered(entry.Handle, p.Border)); entry.Border = p.Border; }
            if (!p.Visible)
            {
                if (entry.Visible) Sdl.Require(Sdl.SDL_HideWindow(entry.Handle)); entry.Visible = false; continue;
            }
            // 这里的安全夹紧只影响呈现，不回写编写的数值。极端的离屏编排仍原样保存在文档里。
            int w = (int)Math.Clamp(Math.Round(p.Width * cw), 16, Math.Min(8192, cw * 4));
            int h = (int)Math.Clamp(Math.Round(p.Height * ch), 16, Math.Min(8192, ch * 4));
            int x = (int)Math.Clamp(Math.Round(ox + p.X * cw - w / 2.0), -100000, 100000);
            int y = (int)Math.Clamp(Math.Round(oy + p.Y * ch - h / 2.0), -100000, 100000);
            if (w != entry.W || h != entry.H) { Sdl.Require(Sdl.SDL_SetWindowSize(entry.Handle, w, h)); entry.W = w; entry.H = h; }
            if (x != entry.X || y != entry.Y) { Sdl.Require(Sdl.SDL_SetWindowPosition(entry.Handle, x, y)); entry.X = x; entry.Y = y; }
            if (!entry.Visible) Sdl.Require(Sdl.SDL_ShowWindow(entry.Handle)); entry.Visible = true;
            Sdl.Require(Sdl.SDL_GetWindowSizeInPixels(entry.Handle, out int pw, out int ph));
            if (pw <= 0 || ph <= 0) continue;
            canvas.Begin(null, pw, ph, w, h, Color.Hex(0x050507));
            if (!renderer.DrawWindowSource(session, time, p, new(0, 0, w, h)))
            {
                // 源不可用时画一个一眼能认出的错误图案，绝不凭空编一个看起来正常的游玩画面。
                canvas.Fill(new(0, 0, w, 4), Color.Hex(0xFF263F));
                canvas.Line(0, 0, w, h, 2, Color.Hex(0x5A2638));
                canvas.Line(w, 0, 0, h, 2, Color.Hex(0x5A2638));
            }
            canvas.Flush(); gpu.Submit(true, entry.Handle);
        }
        string key = string.Join(",", poses.Where(p => p.Visible).OrderBy(p => p.Z).Select(p => p.Id));
        if (key != orderKey)
        {
            foreach (var p in poses.Where(p => p.Visible).OrderBy(p => p.Z))
                if (windows.TryGetValue(p.Id, out var entry)) Sdl.SDL_RaiseWindow(entry.Handle);
            // 每个预览窗口自己都处理 Escape，所以这里不要再把编辑器窗口抬到所有预览之上。
            orderKey = key;
        }
    }
    /// <summary>
    /// 释放次序不可调换：先 Submit 把仍在飞行的批次交完，再从 GPU 设备解绑窗口，最后销毁窗口。
    /// 先销毁窗口会让设备持有已失效的 swapchain。
    /// </summary>
    void Close(int id)
    {
        if (!windows.Remove(id, out var e)) return;
        gpu.Submit(); Sdl.SDL_ReleaseWindowFromGPUDevice(gpu.Handle, e.Handle); Sdl.SDL_DestroyWindow(e.Handle);
    }
    public void Dispose() { foreach (int id in windows.Keys.ToArray()) Close(id); }
}
