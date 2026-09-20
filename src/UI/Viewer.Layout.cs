using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;
namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    bool layoutOpen, layoutInput;
    float layoutAlpha, editorSourceScroll;
    /// <summary>正在拖动的分隔条：0 无，1 左栏宽，2 右栏宽，3 预览/时间轴分割，4 轨道标签宽。非 0 期间鼠标被捕获。</summary>
    int resizingLayout;
    double resizeStartFraction;
    /// <summary>Start 系列是拖动开始时的偏好值（Esc 回退用），Shown 系列是当时实际显示的几何（拖动增量的基准）；两者在 clamp 后可能不同。</summary>
    float resizeStartWidth, resizeStartX, resizeStartY, resizeShownWidth, resizeShownSplit;
    WorkspaceGeometry workspaceGeometry;
    Rect previewFrame, previewImage, trackLabelGrip, editorSourceRect;
    WorkspaceLayout LayoutOptions => preferences.Workspace ??= new();
    bool LayoutVisible => layoutOpen || layoutAlpha > .001f;

    /// <summary>每次调用都向 SDL 查询窗口尺寸与像素密度；逻辑尺寸、物理像素和输入缩放由 UiViewport 统一推导。</summary>
    UiViewport Viewport()
    {
        Sdl.SDL_GetWindowSize(host.Window, out int w, out int h);
        Sdl.SDL_GetWindowSizeInPixels(host.Window, out int pw, out int ph);
        return UiViewport.Create(w, h, pw, ph, Sdl.SDL_GetWindowDisplayScale(host.Window),
            Sdl.SDL_GetWindowPixelDensity(host.Window), LayoutOptions);
    }
    /// <summary>把本窗口的鼠标与拖放坐标从窗口点换算到 UI 逻辑坐标；其它窗口的事件原样放行。</summary>
    Sdl.Event LogicalInput(Sdl.Event e)
    {
        if (e.WindowID != 0 && e.WindowID != Sdl.SDL_GetWindowID(host.Window)) return e;
        var viewport = Viewport();
        // 0x400-0x402 鼠标事件，0x1000/0x1003/0x1004 拖放文件、拖放完成、拖放位置。
        if (e.Type is 0x400 or 0x401 or 0x402) { e.X *= viewport.InputScaleX; e.Y *= viewport.InputScaleY; }
        if (e.Type is 0x1000 or 0x1003 or 0x1004) { e.DropX *= viewport.InputScaleX; e.DropY *= viewport.InputScaleY; }
        return e;
    }
    /// <summary>打开布局面板时清掉所有进行中的按住 / 拖动状态，避免面板下方的控件继承一次残留的按下。</summary>
    void OpenLayout()
    {
        if (UiOverlayVisible || Busy) return;
        layoutOpen = true; held = click = seeking = editorScrub = false;
        trackHelpWHeld = false; hoveredEditTrack = null;
    }
    void CloseLayout() { layoutOpen = false; SaveSettings(); held = click = false; }
    /// <summary>UI 缩放限定在 .75–2.5；先取改动前的 viewport，改完再重投影指针。</summary>
    void SetUiScale(double value)
    {
        var before = Viewport();
        LayoutOptions.UiScale = Math.Clamp(value, .75, 2.5);
        ReprojectPointer(before); SaveSettings();
    }
    void ReprojectPointer(UiViewport previous)
    {
        // 偏好设置改变逻辑投影时，保持指针的物理位置不变。
        var current = Viewport();
        mouseX *= current.InputScaleX / previous.InputScaleX;
        mouseY *= current.InputScaleY / previous.InputScaleY;
        held = click = seeking = editorScrub = false;
    }
    /// <summary>
    /// 布局面板、分隔条拖动与缩放快捷键的输入。返回 true 表示事件已被吃掉；退出与关闭窗口请求永远放行。
    /// 拖动期间：Esc 回退到拖动前的偏好值，抬起或失去焦点结束拖动；双击分隔条恢复默认。
    /// </summary>
    bool HandleLayoutInput(Sdl.Event e)
    {
        if (e.WindowID != 0 && e.WindowID != Sdl.SDL_GetWindowID(host.Window)) return false;
        if (e.Type == 0x20F && resizingLayout != 0) EndLayoutResize(); // 失去焦点
        if (LayoutVisible)
        {
            if (e.Type is 0x400 or 0x401 or 0x402)
            {
                mouseX = e.X; mouseY = e.Y;
                if (e.Type == 0x401 && e.Button == 1 && layoutOpen) { held = true; click = true; }
                if (e.Type == 0x402 && e.Button == 1) held = false;
            }
            if (e.Type == 0x300 && e.Repeat == 0 && e.Scan == 41 && layoutOpen) CloseLayout();
            return e.Type is not (0x100 or Sdl.WindowCloseRequested);
        }
        if (resizingLayout != 0)
        {
            if (e.Type == 0x400)
            {
                // 增量以拖动开始时实际显示的几何为基准，而不是偏好值；否则被 clamp 过的面板会在按下瞬间跳动。
                mouseX = e.X; mouseY = e.Y; var d = WindowSize();
                if (resizingLayout == 1) LayoutOptions.LeftWidth = Math.Clamp(resizeShownWidth + e.X - resizeStartX, 194, 420);
                else if (resizingLayout == 2) LayoutOptions.RightWidth = Math.Clamp(resizeShownWidth - e.X + resizeStartX, 282, 480);
                else if (resizingLayout == 4) LayoutOptions.TrackLabelWidth = Math.Clamp(resizeShownWidth + e.X - resizeStartX, 180, 500);
                else if (editorMode) LayoutOptions.EditorPreviewFraction = WorkspaceGeometry.Fraction(resizeShownSplit + e.Y - resizeStartY, d.H, true);
                else LayoutOptions.ViewerPreviewFraction = WorkspaceGeometry.Fraction(resizeShownSplit + e.Y - resizeStartY, d.H, false);
                click = false; return true;
            }
            if (e.Type == 0x300 && e.Scan == 41)
            {
                if (resizingLayout == 1) LayoutOptions.LeftWidth = resizeStartWidth;
                else if (resizingLayout == 2) LayoutOptions.RightWidth = resizeStartWidth;
                else if (resizingLayout == 4) LayoutOptions.TrackLabelWidth = resizeStartWidth;
                else if (editorMode) LayoutOptions.EditorPreviewFraction = resizeStartFraction;
                else LayoutOptions.ViewerPreviewFraction = resizeStartFraction;
                EndLayoutResize(); return true;
            }
            if (e.Type == 0x402 && e.Button == 1) { EndLayoutResize(); return true; }
            return e.Type is not (0x100 or Sdl.WindowCloseRequested);
        }
        if (UiOverlayVisible || Busy || dialogOpen || editDrag != null || ImageGestureActive) return false;
        // command + 等号/小键盘加（46/87）放大，command + 减号/小键盘减（45/86）缩小，command + 0（39）复位；步长 .125。
        if (e.Type == 0x300 && e.Repeat == 0 && (e.Modifiers & 0x0CC0) != 0)
        {
            if (e.Scan is 46 or 87) { SetUiScale(LayoutOptions.UiScale + .125); return true; }
            if (e.Scan is 45 or 86) { SetUiScale(LayoutOptions.UiScale - .125); return true; }
            if (e.Scan == 39) { SetUiScale(1); return true; }
        }
        if (e.Type == 0x403 && editorMode && editorSourceRect.Contains(mouseX, mouseY))
        {
            if (textSources) textListScroll = Math.Max(0, textListScroll - (int)Math.Round(e.WheelY));
            else if (imageSources) imageListScroll = Math.Max(0, imageListScroll - (int)Math.Round(e.WheelY));
            else editorSourceScroll = Math.Clamp(editorSourceScroll - e.WheelY * 30, 0, Math.Max(0, 437 - editorSourceRect.H));
            return true;
        }
        if (e.Type != 0x401 || e.Button != 1) return false;
        int hit = workspaceGeometry.LeftGrip.Contains(e.X, e.Y) ? 1 : workspaceGeometry.RightGrip.Contains(e.X, e.Y) ? 2 :
            workspaceGeometry.HorizontalGrip.Contains(e.X, e.Y) ? 3 : editorMode && trackLabelGrip.Contains(e.X, e.Y) ? 4 : 0;
        if (hit == 0) return false;
        if (e.Clicks >= 2)
        {
            if (hit == 1) LayoutOptions.LeftWidth = 194;
            else if (hit == 2) LayoutOptions.RightWidth = 282;
            else if (hit == 4) LayoutOptions.TrackLabelWidth = 224;
            else if (editorMode) LayoutOptions.EditorPreviewFraction = .52;
            else LayoutOptions.ViewerPreviewFraction = .72;
            SaveSettings(); click = false; return true;
        }
        resizingLayout = hit;
        resizeStartX = e.X; resizeStartY = e.Y; resizeShownSplit = workspaceGeometry.SplitY;
        resizeShownWidth = hit == 1 ? workspaceGeometry.LeftWidth : hit == 2 ? workspaceGeometry.RightWidth : LayoutOptions.TrackLabelWidth;
        resizeStartWidth = hit == 1 ? LayoutOptions.LeftWidth : hit == 2 ? LayoutOptions.RightWidth : LayoutOptions.TrackLabelWidth;
        resizeStartFraction = editorMode ? LayoutOptions.EditorPreviewFraction : LayoutOptions.ViewerPreviewFraction;
        held = click = seeking = editorScrub = false; trackHelpWHeld = false; hoveredEditTrack = null;
        Sdl.SDL_CaptureMouse(true); return true;
    }
    /// <summary>结束拖动：释放鼠标捕获并落盘。任何拖动退出路径（抬起、Esc、失焦、Dispose）都必须经过这里，否则捕获会泄漏。</summary>
    void EndLayoutResize()
    {
        resizingLayout = 0; held = click = false; Sdl.SDL_CaptureMouse(false); SaveSettings();
    }
    /// <summary>分隔条只在没有阻塞覆盖层时绘制；hover 与拖动中只改配色，命中矩形不变。</summary>
    void DrawLayoutGrips()
    {
        // 轨道帮助在之后绘制，直接盖在这些分隔条上面。不要让分隔条的绘制依赖提示卡的可见性；
        // 那种交替可见正是当年闪烁的根源。
        if (UiBlockingOverlayVisible) return;
        void Grip(Rect r, int index, bool vertical)
        {
            bool hover = r.Contains(mouseX, mouseY) || resizingLayout == index;
            Canvas.Fill(r, hover ? Theme.PanelRaised : Theme.PanelAlt);
            Color c = hover ? Theme.AccentSoft : line;
            if (vertical) Canvas.Fill(new(r.X + r.W / 2 - 1, r.Y + r.H / 2 - 18, 2, 36), c);
            else Canvas.Fill(new(r.X + r.W / 2 - 22, r.Y + 3, 44, 2), c);
        }
        Grip(workspaceGeometry.LeftGrip, 1, true); Grip(workspaceGeometry.RightGrip, 2, true);
        Grip(workspaceGeometry.HorizontalGrip, 3, false);
        if (editorMode && editor != null && trackLabelGrip.H > 0) Grip(trackLabelGrip, 4, true);
    }
    /// <summary>
    /// 布局 / UI 缩放面板。面板内的按钮只在 layoutInput 为真时可用，finally 保证异常时也复位；
    /// 任何改变逻辑投影的操作（缩放、跟随 DPI、重置）都要先取 Viewport 再 ReprojectPointer。
    /// </summary>
    void DrawLayout(int w, int h)
    {
        if (!LayoutVisible) return;
        using var fade = Canvas.Opacity(layoutAlpha);
        Canvas.Fill(new(0, 0, w, h), Color.Hex(0, .88f));
        Rect r = new(w / 2f - 365, h / 2f - 260, 730, 520);
        Canvas.Fill(r, panel); Canvas.Border(r, soft);
        Text(L.Get("LAYOUT / UI SCALE"), r.X + 26, r.Y + 22, 22, white);
        layoutInput = true;
        try
        {
            Text(L.Get("UI scale"), r.X + 26, r.Y + 80, 15, white);
            if (Button("-", new(r.X + 248, r.Y + 68, 42, 34))) SetUiScale(LayoutOptions.UiScale - .125);
            Text($"{LayoutOptions.UiScale * 100:0.#}%", r.X + 307, r.Y + 79, 16, white, true);
            if (Button("+", new(r.X + 400, r.Y + 68, 42, 34))) SetUiScale(LayoutOptions.UiScale + .125);
            if (Button("100%", new(r.X + 459, r.Y + 68, 95, 34))) SetUiScale(1);
            if (Button("150%", new(r.X + 564, r.Y + 68, 130, 34))) SetUiScale(1.5);
            Text(L.Get("Follow display DPI"), r.X + 26, r.Y + 125, 15, white);
            if (Button(LayoutOptions.FollowDisplayScale ? L.Get("ON") : L.Get("OFF"), new(r.X + 248, r.Y + 114, 194, 32), active: LayoutOptions.FollowDisplayScale))
            { var before = Viewport(); LayoutOptions.FollowDisplayScale = !LayoutOptions.FollowDisplayScale; ReprojectPointer(before); SaveSettings(); }
            var vp = Viewport();
            Text(vp.FitLimited ? L.Get("Zoom limited to keep controls visible. Enlarge the window for more zoom.") : L.Get("Ctrl/Cmd +/-: scale. Ctrl/Cmd+0: reset. Mouse coordinates follow the same scale."),
                r.X + 26, r.Y + 163, 12, vp.FitLimited ? soft : muted, max: 678);
            Text(L.Get("Workspace"), r.X + 26, r.Y + 205, 15, white);
            string[] names = [L.Get("PREVIEW"), L.Get("BALANCED"), L.Get("TIMELINE")];
            for (int i = 0; i < names.Length; i++)
                if (Button(names[i], new(r.X + 248 + i * 150, r.Y + 193, 143, 34)))
                { LayoutOptions.EditorPreviewFraction = new[] { .82, .52, .18 }[i]; LayoutOptions.ViewerPreviewFraction = new[] { .82, .60, .25 }[i]; SaveSettings(); click = false; }
            Text(L.Get("Drag side dividers to resize panels; drag the bar below the preview to resize it."), r.X + 26, r.Y + 246, 13, muted, max: 678);
            Text(L.Get("Double-click a divider to reset it. Esc cancels a resize. Layout is saved per device."), r.X + 26, r.Y + 270, 13, muted, max: 678);
            Text(L.Get("Scene resolution"), r.X + 26, r.Y + 318, 15, white);
            int[] sizes = [320, 640, 1280, 1920];
            for (int i = 0; i < sizes.Length; i++)
                if (Button(sizes[i].ToString(), new(r.X + 248 + i * 112, r.Y + 305, 104, 34), active: Current.Project.RenderWidth == sizes[i]))
                    { Current.Project.RenderWidth = sizes[i]; SaveSettings(); }
            Text(L.Get("Preview panel size and UI scale do not change song geometry or MP4 dimensions."), r.X + 26, r.Y + 360, 13, muted, max: 678);
            Text(L.Get("Scene resolution is separate: higher settings use more GPU memory."), r.X + 26, r.Y + 385, 13, muted, max: 678);
            if (Button(L.Get("RESET LAYOUT"), new(r.X + 26, r.Y + 444, 230, 40))) { var before = Viewport(); preferences.Workspace = new(); ReprojectPointer(before); SaveSettings(); }
            if (Button(L.Get("DONE"), new(r.X + 478, r.Y + 444, 216, 40), primary: true)) CloseLayout();
        }
        finally { layoutInput = false; }
    }
}
