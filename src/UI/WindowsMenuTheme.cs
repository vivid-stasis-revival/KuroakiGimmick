using System.Runtime.InteropServices;

namespace KuroakiGimmick.UI;

/// <summary>
/// Windows 经典 HMENU 不读取程序的深色主题。只对子类化的本窗口菜单处理 owner-draw 消息，
/// 其余消息仍交给 SDL 的窗口过程；不修改进程或系统的主题设置。
/// </summary>
internal sealed class WindowsMenuTheme : IDisposable
{
    const uint WmMeasureItem = 0x002C, WmDrawItem = 0x002B, WmMenuChar = 0x0120, WmNcPaint = 0x0085;
    const uint OdtMenu = 1, OdsSelected = 1, OdsGrayed = 2, OdsDisabled = 4, OdsChecked = 8;
    const uint MimBackground = 2, TextFlags = 0x20 | 0x4 | 0x800 | 0x8000; // SINGLELINE | VCENTER | NOPREFIX | END_ELLIPSIS
    const uint DwmDarkMode = 20;
    const nuint SubclassId = 0x4B47554D;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate nint SubclassCallback(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowSubclass(nint window, SubclassCallback callback, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool RemoveWindowSubclass(nint window, SubclassCallback callback, nuint id);
    [DllImport("comctl32.dll")] static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetMenuInfo(nint menu, ref MenuInfo info);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] static extern nint GetDC(nint window);
    [DllImport("user32.dll")] static extern nint GetWindowDC(nint window);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(nint window, out Area area);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetMenuItemRect(nint window, nint menu, uint item, out Area area);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetMenuBarInfo(nint window, int objectId, int item, ref MenuBarInfo info);
    [DllImport("gdi32.dll")] static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] static extern nint GetStockObject(int objectId);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll")] static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetTextExtentPoint32W")]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool TextSize(nint dc, string text, int length, out Size size);
    [DllImport("user32.dll")] static extern int FillRect(nint dc, ref Area area, nint brush);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DrawTextW")]
    static extern int DrawText(nint dc, string text, int length, ref Area area, uint format);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint window, uint attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)] struct Area { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Size { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] struct MenuInfo
    {
        public uint Size, Mask, Style, MaxHeight;
        public nint Background;
        public uint ContextHelp;
        public nuint Data;
    }
    [StructLayout(LayoutKind.Sequential)] struct MenuBarInfo
    {
        public uint Size;
        public Area Bar;
        public nint Menu, MenuWindow;
        public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] struct MeasureItem
    {
        public uint Type, ControlId, ItemId, Width, Height;
        public nuint Data;
    }
    [StructLayout(LayoutKind.Sequential)] struct DrawItem
    {
        public uint Type, ControlId, ItemId, Action, State;
        public nint Menu, Dc;
        public Area Bounds;
        public nuint Data;
    }
    sealed record Visual(string Label, string Shortcut, bool Top, bool Submenu);

    readonly nint window, backgroundBrush, hoverBrush;
    readonly uint foreground, muted, accent;
    readonly int dpi;
    readonly SubclassCallback callback;
    readonly Dictionary<nuint, Visual> visuals = [];
    readonly Dictionary<char, int> topKeys = [];
    nuint nextTag = 1;
    nint rootMenu;

    static uint Rgb(uint rgb) => ((rgb & 0xff) << 16) | (rgb & 0xff00) | ((rgb >> 16) & 0xff);
    int Scale(int value) => (int)Math.Round(value * dpi / 96.0);

    internal WindowsMenuTheme(nint window, string? theme)
    {
        this.window = window;
        (uint background, uint hover, uint text, uint dim, uint highlight) = (theme ?? "Nekomiya").ToLowerInvariant() switch
        {
            "scarlet" => (0x100B0Eu, 0x5B2632u, 0xF7EDEFu, 0x927D85u, 0xFF6072u),
            "kuroaki" => (0x151014u, 0x824153u, 0xFFF6F8u, 0xC7AFB7u, 0xF04461u),
            _ => (0x171E2Bu, 0x344156u, 0xF4F7FCu, 0x8998ACu, 0xFF9CAFu)
        };
        foreground = Rgb(text); muted = Rgb(dim); accent = Rgb(highlight);
        backgroundBrush = CreateSolidBrush(Rgb(background));
        hoverBrush = CreateSolidBrush(Rgb(hover));
        if (backgroundBrush == 0 || hoverBrush == 0)
        {
            if (backgroundBrush != 0) DeleteObject(backgroundBrush);
            if (hoverBrush != 0) DeleteObject(hoverBrush);
            throw new InvalidOperationException("Could not create dark menu brushes.");
        }
        dpi = (int)Math.Max(96, GetDpiForWindow(window));
        callback = HandleWindowMessage;
        if (!SetWindowSubclass(window, callback, SubclassId, 0))
        {
            DeleteObject(backgroundBrush); DeleteObject(hoverBrush);
            throw new InvalidOperationException("Could not attach dark menu drawing to the SDL window.");
        }
        // 标题栏与菜单栏分别由 DWM 和 HMENU 绘制；失败时标题栏仍用系统设置，菜单绘制照常工作。
        int dark = 1;
        try { _ = DwmSetWindowAttribute(window, DwmDarkMode, ref dark, sizeof(int)); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    internal void SetRoot(nint root) => rootMenu = root;

    internal void ApplyBackground(nint menu)
    {
        var info = new MenuInfo { Size = (uint)Marshal.SizeOf<MenuInfo>(), Mask = MimBackground, Background = backgroundBrush };
        _ = SetMenuInfo(menu, ref info);
    }

    internal nint Register(string label, string shortcut = "", bool top = false, bool submenu = false,
        char accessKey = '\0', int menuIndex = -1)
    {
        nuint tag = nextTag++;
        visuals.Add(tag, new(label, shortcut, top, submenu));
        if (top && accessKey != '\0') topKeys[char.ToUpperInvariant(accessKey)] = menuIndex;
        return (nint)tag;
    }

    nint HandleWindowMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (message == WmMeasureItem && lParam != 0 && Measure(lParam)) return 1;
            if (message == WmDrawItem && lParam != 0 && Paint(lParam)) return 1;
            if (message == WmMenuChar && lParam == rootMenu)
            {
                char key = char.ToUpperInvariant((char)((ulong)wParam & 0xffff));
                if (topKeys.TryGetValue(key, out int index)) return (nint)((3 << 16) | index); // MNC_SELECT
            }
            nint result = DefSubclassProc(hwnd, message, wParam, lParam);
            if (message == WmNcPaint) PaintMenuGaps();
            return result;
        }
        catch { return DefSubclassProc(hwnd, message, wParam, lParam); }
    }

    int MeasureText(nint dc, string text)
    {
        if (text.Length == 0) return 0;
        nint old = SelectObject(dc, GetStockObject(17)); // DEFAULT_GUI_FONT
        bool measured = TextSize(dc, text, text.Length, out Size size);
        SelectObject(dc, old);
        return measured ? size.Width : text.Length * Scale(7);
    }

    bool Measure(nint pointer)
    {
        var item = Marshal.PtrToStructure<MeasureItem>(pointer);
        if (item.Type != OdtMenu || !visuals.TryGetValue(item.Data, out var visual)) return false;
        nint dc = GetDC(window);
        try
        {
            int width = MeasureText(dc, visual.Label) + Scale(visual.Top ? 25 : visual.Submenu ? 80 : 60);
            if (visual.Shortcut.Length > 0) width += MeasureText(dc, visual.Shortcut) + Scale(35);
            item.Width = (uint)Math.Max(1, width);
            item.Height = (uint)Scale(visual.Top ? 26 : 28);
            Marshal.StructureToPtr(item, pointer, false);
            return true;
        }
        finally { if (dc != 0) ReleaseDC(window, dc); }
    }

    bool Paint(nint pointer)
    {
        var item = Marshal.PtrToStructure<DrawItem>(pointer);
        if (item.Type != OdtMenu || !visuals.TryGetValue(item.Data, out var visual)) return false;
        Area box = item.Bounds;
        FillRect(item.Dc, ref box, (item.State & OdsSelected) != 0 ? hoverBrush : backgroundBrush);
        SetBkMode(item.Dc, 1); // TRANSPARENT
        bool disabled = (item.State & (OdsGrayed | OdsDisabled)) != 0;
        SetTextColor(item.Dc, disabled ? muted : foreground);
        nint old = SelectObject(item.Dc, GetStockObject(17));
        try
        {
            Area label = box;
            label.Left += Scale(visual.Top ? 12 : 30);
            int reserve = visual.Top ? Scale(12) : visual.Shortcut.Length > 0
                ? MeasureText(item.Dc, visual.Shortcut) + Scale(25)
                : visual.Submenu ? Scale(40) : Scale(12);
            label.Right -= reserve;
            DrawText(item.Dc, visual.Label, visual.Label.Length, ref label, TextFlags);
            if (visual.Shortcut.Length > 0)
            {
                Area shortcut = box;
                shortcut.Left = label.Right + Scale(8);
                shortcut.Right -= Scale(12);
                SetTextColor(item.Dc, muted);
                DrawText(item.Dc, visual.Shortcut, visual.Shortcut.Length, ref shortcut, TextFlags | 2); // RIGHT
            }
            if (!visual.Top && visual.Submenu)
            {
                Area arrow = box;
                arrow.Left = box.Right - Scale(24);
                arrow.Right = box.Right - Scale(7);
                DrawText(item.Dc, ">", 1, ref arrow, TextFlags | 2);
            }
            if (!visual.Top && (item.State & OdsChecked) != 0)
            {
                Area check = box;
                check.Left += Scale(8);
                check.Right = check.Left + Scale(18);
                SetTextColor(item.Dc, accent);
                DrawText(item.Dc, "✓", 1, ref check, TextFlags);
            }
        }
        finally { SelectObject(item.Dc, old); }
        return true;
    }

    void PaintMenuGaps()
    {
        if (rootMenu == 0) return;
        var info = new MenuBarInfo { Size = (uint)Marshal.SizeOf<MenuBarInfo>() };
        if (!GetMenuBarInfo(window, -3, 0, ref info) || !GetWindowRect(window, out Area frame)) return; // OBJID_MENU
        int count = WindowsMenuBar.MenuGroupCount;
        if (count == 0 || !GetMenuItemRect(window, rootMenu, 0, out Area first) ||
            !GetMenuItemRect(window, rootMenu, (uint)(count - 1), out Area last)) return;
        nint dc = GetWindowDC(window);
        if (dc == 0) return;
        try
        {
            var left = new Area { Left = info.Bar.Left - frame.Left, Top = info.Bar.Top - frame.Top,
                Right = first.Left - frame.Left, Bottom = info.Bar.Bottom - frame.Top };
            var right = new Area { Left = last.Right - frame.Left, Top = info.Bar.Top - frame.Top,
                Right = info.Bar.Right - frame.Left, Bottom = info.Bar.Bottom - frame.Top };
            if (left.Right > left.Left) FillRect(dc, ref left, backgroundBrush);
            if (right.Right > right.Left) FillRect(dc, ref right, backgroundBrush);
        }
        finally { ReleaseDC(window, dc); }
    }

    public void Dispose()
    {
        RemoveWindowSubclass(window, callback, SubclassId);
        DeleteObject(backgroundBrush);
        DeleteObject(hoverBrush);
    }
}
