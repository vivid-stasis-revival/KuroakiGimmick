using System.Runtime.InteropServices;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>Windows 原生窗口菜单。WM_COMMAND 经 SDL 消息钩子入主线程队列，窗口过程仍由 SDL 拥有。</summary>
internal sealed class WindowsMenuBar : IDisposable
{
    const uint MfPopup = 0x10, MfSeparator = 0x800, MfGray = 0x1, MfChecked = 0x8;
    const uint WmCommand = 0x0111;
    [DllImport("user32.dll", SetLastError = true)] static extern nint CreateMenu();
    [DllImport("user32.dll", SetLastError = true)] static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool AppendMenu(nint menu, uint flags, nuint id, string? title);
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
    readonly Dictionary<MenuCommand, nint> commandMenus = [];
    readonly Dictionary<int, (nint Menu, string Path)> recentMenus = [];

    public WindowsMenuBar(nint sdlWindow, Action<MenuCommand, string?> execute, IReadOnlyList<string> recent)
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
            foreach (var group in MenuCatalog.Groups) AddGroup(group, recent);
            if (!SetMenu(window, root)) throw new InvalidOperationException("Win32 SetMenu failed.");
            DrawMenuBar(window);
            hook = HandleMessage;
            Sdl.SDL_SetWindowsMessageHook(hook, 0);
        }
        catch { DestroyMenu(root); throw; }
    }

    static string Safe(string text) => text.Replace("&", "&&", StringComparison.Ordinal);
    static string Shortcut(MenuItemSpec spec) => spec.Key.Length == 0 ? "" :
        "\tCtrl+" + (spec.Shift ? "Shift+" : "") + spec.Key.ToUpperInvariant();

    void AddGroup(MenuGroupSpec group, IReadOnlyList<string> recent)
    {
        nint submenu = CreatePopupMenu();
        if (submenu == 0) throw new InvalidOperationException("Win32 CreatePopupMenu failed.");
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
            if (!AppendMenu(submenu, 0, (nuint)spec.Command, Safe(L.Get(spec.Label)) + Shortcut(spec)))
                throw new InvalidOperationException("Win32 AppendMenu failed: " + spec.Command);
            commandMenus[spec.Command] = submenu;
        }
        char accelerator = group.Label switch { "FILE" => 'F', "EDIT" => 'E', "PLAYBACK" => 'P', "VIEW" => 'V', _ => 'H' };
        if (!AppendMenu(root, MfPopup, (nuint)submenu, Safe(L.Get(group.Label)) + " (&" + accelerator + ")"))
            throw new InvalidOperationException("Win32 top-level menu failed: " + group.Label);
    }

    void AddRecent(nint parent, MenuItemSpec spec, IReadOnlyList<string> paths)
    {
        nint submenu = CreatePopupMenu();
        if (submenu == 0) throw new InvalidOperationException("Win32 recent menu failed.");
        if (paths.Count == 0) AppendMenu(submenu, MfGray, 0, Safe(L.Get("NO RECENT FILES")));
        for (int i = 0; i < paths.Count; i++)
        {
            string path = paths[i];
            string title = Path.GetFileName(path) + " — " + Path.GetDirectoryName(path);
            if (title.Length > 90) title = title[..87] + "...";
            int id = MenuCatalog.RecentBaseId + i;
            if (!AppendMenu(submenu, File.Exists(path) || Directory.Exists(path) ? 0u : MfGray,
                (nuint)id, Safe(title))) throw new InvalidOperationException("Win32 recent item failed.");
            recentMenus[id] = (submenu, path);
        }
        if (!AppendMenu(parent, MfPopup, (nuint)submenu, Safe(L.Get(spec.Label))))
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
        if (GetMenu(window) != root || GetMenuItemCount(root) != MenuCatalog.Groups.Length ||
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
    }
}
