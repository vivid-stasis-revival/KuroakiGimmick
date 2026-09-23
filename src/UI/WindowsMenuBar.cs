using System.Runtime.InteropServices;
using KuroakiGimmick.Core;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>Windows 原生窗口菜单。WM_COMMAND 经 SDL 消息钩子入主线程队列，窗口过程仍由 SDL 拥有。</summary>
internal sealed class WindowsMenuBar : IDisposable
{
    const uint MfPopup = 0x10, MfSeparator = 0x800, MfGray = 0x1, MfChecked = 0x8, MfOwnerDraw = 0x100;
    const uint WmCommand = 0x0111;
    [DllImport("user32.dll", SetLastError = true)] static extern nint CreateMenu();
    [DllImport("user32.dll", SetLastError = true)] static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool AppendMenu(nint menu, uint flags, nuint id, string? title);
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool AppendMenuOwner(nint menu, uint flags, nuint id, nint data);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetMenu(nint window, nint menu);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DrawMenuBar(nint window);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", SetLastError = true)] static extern uint EnableMenuItem(nint menu, uint id, uint flags);
    [DllImport("user32.dll", SetLastError = true)] static extern uint CheckMenuItem(nint menu, uint id, uint flags);
    [DllImport("user32.dll", SetLastError = true)] static extern nint GetMenu(nint window);
    [DllImport("user32.dll", SetLastError = true)] static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct Message
    {
        public nint Window;
        public uint Type;
        public nint WParam, LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    readonly nint window, root;
    readonly Sdl.WindowsMessageHook hook;
    readonly Action<MenuCommand, string?> execute;
    readonly WindowsMenuTheme? theme;
    readonly Dictionary<MenuCommand, nint> commandMenus = [];
    readonly Dictionary<int, (nint Menu, string Path)> recentMenus = [];

    internal static int MenuGroupCount => MenuCatalog.Groups.Length;

    public WindowsMenuBar(nint sdlWindow, Action<MenuCommand, string?> execute, IReadOnlyList<RecentSource.Item> recent, string themeName)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        this.execute = execute;
        uint properties = Sdl.SDL_GetWindowProperties(sdlWindow);
        window = Sdl.SDL_GetPointerProperty(properties, "SDL.window.win32.hwnd", 0);
        if (window == 0) throw new InvalidOperationException("SDL did not expose the Win32 window handle.");
        root = CreateMenu();
        if (root == 0) throw new InvalidOperationException("Win32 CreateMenu failed.");
        try
        {
            try
            {
                theme = new WindowsMenuTheme(window, themeName);
                theme.SetRoot(root);
                theme.ApplyBackground(root);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
            {
                Console.Error.WriteLine("Dark Windows menu unavailable; using the system menu: " + ex.Message);
            }
            foreach (var group in MenuCatalog.Groups) AddGroup(group, recent);
            if (!SetMenu(window, root)) throw new InvalidOperationException("Win32 SetMenu failed.");
            DrawMenuBar(window);
            hook = HandleMessage;
            Sdl.SDL_SetWindowsMessageHook(hook, 0);
        }
        catch { theme?.Dispose(); DestroyMenu(root); throw; }
    }

    static string Safe(string text) => text.Replace("&", "&&", StringComparison.Ordinal);
    static string Shortcut(MenuItemSpec spec) => spec.Key.Length == 0 ? "" :
        "Ctrl+" + (spec.Shift ? "Shift+" : "") + spec.Key.ToUpperInvariant();

    bool AddItem(nint menu, uint flags, nuint id, string title, string shortcut = "", bool top = false,
        bool submenu = false, char accessKey = '\0', int menuIndex = -1)
    {
        if (theme == null) return AppendMenu(menu, flags, id, Safe(title) + (shortcut.Length > 0 ? "\t" + shortcut : ""));
        nint data = theme.Register(title, shortcut, top, submenu, accessKey, menuIndex);
        return AppendMenuOwner(menu, flags | MfOwnerDraw, id, data);
    }

    void AddGroup(MenuGroupSpec group, IReadOnlyList<RecentSource.Item> recent)
    {
        nint submenu = CreatePopupMenu();
        if (submenu == 0) throw new InvalidOperationException("Win32 CreatePopupMenu failed.");
        theme?.ApplyBackground(submenu);
        foreach (var spec in group.Items)
        {
            if (spec.Command == MenuCommand.None)
            {
                AppendMenu(submenu, MfSeparator, 0, null);
                continue;
            }
            if (spec.Command == MenuCommand.RecentMenu)
            {
                AddRecent(submenu, spec, recent);
                continue;
            }
            if (!AddItem(submenu, 0, (nuint)spec.Command, L.Get(spec.Label), Shortcut(spec)))
                throw new InvalidOperationException("Win32 AppendMenu failed: " + spec.Command);
            commandMenus[spec.Command] = submenu;
        }
        char accelerator = group.Label switch { "FILE" => 'F', "EDIT" => 'E', "PLAYBACK" => 'P', "VIEW" => 'V', _ => 'H' };
        if (!AddItem(root, MfPopup, (nuint)submenu, L.Get(group.Label) + " (" + accelerator + ")",
            top: true, submenu: true, accessKey: accelerator, menuIndex: GetMenuItemCount(root)))
            throw new InvalidOperationException("Win32 top-level menu failed: " + group.Label);
    }

    /// <summary>
    /// 最近条目的文字由调用方给（曲名 / 曲师 @ 难度 等级）。Windows 菜单项没有 tooltip，
    /// 完整路径仍接在标题后面，同一首歌的不同难度、不同目录里的同名歌都分得开。
    /// </summary>
    void AddRecent(nint parent, MenuItemSpec spec, IReadOnlyList<RecentSource.Item> items)
    {
        nint submenu = CreatePopupMenu();
        if (submenu == 0) throw new InvalidOperationException("Win32 recent menu failed.");
        theme?.ApplyBackground(submenu);
        if (items.Count == 0) AddItem(submenu, MfGray, 0, L.Get("NO RECENT FILES"));
        for (int i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            string title = entry.Title + " — " + Path.GetDirectoryName(entry.Path);
            if (title.Length > 90) title = title[..87] + "...";
            int id = MenuCatalog.RecentBaseId + i;
            if (!AddItem(submenu, File.Exists(entry.Path) || Directory.Exists(entry.Path) ? 0u : MfGray,
                (nuint)id, title)) throw new InvalidOperationException("Win32 recent item failed.");
            recentMenus[id] = (submenu, entry.Path);
        }
        if (!AddItem(parent, MfPopup, (nuint)submenu, L.Get(spec.Label), submenu: true))
            throw new InvalidOperationException("Win32 recent submenu failed.");
    }

    bool HandleMessage(nint user, nint pointer)
    {
        if (pointer == 0) return true;
        if (Marshal.ReadIntPtr(pointer) != window || Marshal.ReadInt32(pointer, nint.Size) != WmCommand) return true;
        var message = Marshal.PtrToStructure<Message>(pointer);
        if (message.LParam != 0) return true;
        int id = (int)((long)message.WParam & 0xffff);
        if (recentMenus.TryGetValue(id, out var recent))
        {
            execute(MenuCommand.OpenFile, recent.Path);
            return false;
        }
        if (Enum.IsDefined((MenuCommand)id) && commandMenus.ContainsKey((MenuCommand)id))
        {
            execute((MenuCommand)id, null);
            return false;
        }
        return true;
    }

    internal void Update(Func<MenuCommand, bool> enabled, Func<MenuCommand, bool> selected)
    {
        foreach (var (command, menu) in commandMenus)
        {
            EnableMenuItem(menu, (uint)command, enabled(command) ? 0u : MfGray);
            CheckMenuItem(menu, (uint)command, selected(command) ? MfChecked : 0u);
        }
        foreach (var (id, recent) in recentMenus)
            EnableMenuItem(recent.Menu, (uint)id,
                enabled(MenuCommand.OpenFile) && (File.Exists(recent.Path) || Directory.Exists(recent.Path)) ? 0u : MfGray);
        DrawMenuBar(window);
    }

    internal void AssertInstalled()
    {
        if (theme == null || GetMenu(window) != root || GetMenuItemCount(root) != MenuCatalog.Groups.Length ||
            commandMenus.Count != MenuCatalog.Actions.Count())
            throw new InvalidOperationException("Win32 menu groups or commands were not installed.");
    }

    internal void PostForTest(MenuCommand command, int? recentIndex = null)
    {
        int id = recentIndex is int index ? MenuCatalog.RecentBaseId + index : (int)command;
        if (!PostMessageW(window, WmCommand, (nuint)id, 0))
            throw new InvalidOperationException("Could not post a Win32 menu command.");
    }

    public void Dispose()
    {
        Sdl.SDL_SetWindowsMessageHook(null, 0);
        SetMenu(window, 0);
        DrawMenuBar(window);
        DestroyMenu(root);
        theme?.Dispose();
    }
}
