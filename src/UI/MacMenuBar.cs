using System.Runtime.InteropServices;

namespace KuroakiGimmick.UI;

/// <summary>macOS 的原生 AppKit 菜单；Cocoa 回调只把命令送回 Viewer 主线程队列。</summary>
internal sealed class MacMenuBar : IDisposable
{
    const string Objc = "/usr/lib/libobjc.A.dylib";
    [DllImport(Objc)] static extern nint objc_getClass(string name);
    [DllImport(Objc)] static extern nint sel_registerName(string name);
    [DllImport(Objc)] static extern nint objc_allocateClassPair(nint parent, string name, nint extraBytes);
    [DllImport(Objc)] static extern void objc_registerClassPair(nint cls);
    [DllImport(Objc)] [return: MarshalAs(UnmanagedType.I1)]
    static extern bool class_addMethod(nint cls, nint selector, nint implementation, string types);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern nint Send(nint obj, nint selector);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern nint Send(nint obj, nint selector, nint arg);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern nint Send(nint obj, nint selector, nint a, nint b);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern nint Send(nint obj, nint selector, nint a, nint b, nint c);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void MenuCallback(nint self, nint selector, nint sender);
    static readonly MenuCallback Callback = Dispatch;
    static readonly nint TagSelector = sel_registerName("tag");
    static Action<MenuCommand, string?>? handler;
    static Dictionary<int, string> recentTags = [];
    readonly nint mainMenu, target;
    readonly List<nint> inserted = [];
    readonly Dictionary<MenuCommand, (nint Menu, nint Item, nint Index)> entries = [];
    readonly List<(nint Item, string Path)> recentEntries = [];

    static void Dispatch(nint self, nint selector, nint sender)
    {
        int tag = (int)Send(sender, TagSelector);
        if (recentTags.TryGetValue(tag, out string? path)) handler?.Invoke(MenuCommand.OpenFile, path);
        else if (Enum.IsDefined((MenuCommand)tag)) handler?.Invoke((MenuCommand)tag, null);
    }

    static nint Sel(string name) => sel_registerName(name);
    static nint String(string value)
    {
        nint utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try { return Send(objc_getClass("NSString"), Sel("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    public MacMenuBar(Action<MenuCommand, string?> execute, IReadOnlyList<string> recent)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        handler = execute;
        recentTags = [];
        nint app = Send(objc_getClass("NSApplication"), Sel("sharedApplication"));
        mainMenu = Send(app, Sel("mainMenu"));
        if (mainMenu == 0) throw new InvalidOperationException("AppKit main menu is unavailable.");
        nint cls = objc_getClass("KuroakiGimmickMenuTarget");
        if (cls == 0)
        {
            cls = objc_allocateClassPair(objc_getClass("NSObject"), "KuroakiGimmickMenuTarget", 0);
            if (cls == 0 || !class_addMethod(cls, Sel("kgMenuAction:"), Marshal.GetFunctionPointerForDelegate(Callback), "v@:@"))
                throw new InvalidOperationException("Could not register the macOS menu action.");
            objc_registerClassPair(cls);
        }
        target = Send(Send(cls, Sel("alloc")), Sel("init"));
        int index = Math.Max(1, (int)Send(mainMenu, Sel("numberOfItems")) - 1);
        foreach (var group in MenuCatalog.Groups) AddGroup(group, recent, ref index);
    }

    static nint NewMenu(string title)
    {
        nint menu = Send(Send(objc_getClass("NSMenu"), Sel("alloc")), Sel("initWithTitle:"), String(title));
        Send(menu, Sel("setAutoenablesItems:"), 0);
        return menu;
    }

    void AddGroup(MenuGroupSpec group, IReadOnlyList<string> recent, ref int index)
    {
        string name = L.Get(group.Label);
        nint menu = NewMenu(name);
        foreach (var spec in group.Items)
        {
            if (spec.Command == MenuCommand.None)
            {
                Send(menu, Sel("addItem:"), Send(objc_getClass("NSMenuItem"), Sel("separatorItem")));
                continue;
            }
            if (spec.Command == MenuCommand.RecentMenu)
            {
                AddRecent(menu, spec, recent);
                continue;
            }
            nint itemIndex = Send(menu, Sel("numberOfItems"));
            nint item = Send(menu, Sel("addItemWithTitle:action:keyEquivalent:"),
                String(L.Get(spec.Label)), Sel("kgMenuAction:"), String(spec.Key));
            Send(item, Sel("setTarget:"), target);
            Send(item, Sel("setTag:"), (nint)spec.Command);
            if (spec.Shift) Send(item, Sel("setKeyEquivalentModifierMask:"), (nint)((1 << 20) | (1 << 17)));
            entries[spec.Command] = (menu, item, itemIndex);
        }
        nint top = Send(Send(objc_getClass("NSMenuItem"), Sel("alloc")), Sel("initWithTitle:action:keyEquivalent:"),
            String(name), 0, String(""));
        Send(top, Sel("setSubmenu:"), menu);
        Send(mainMenu, Sel("insertItem:atIndex:"), top, index++);
        inserted.Add(top);
        Send(menu, Sel("release"));
    }

    void AddRecent(nint parent, MenuItemSpec spec, IReadOnlyList<string> paths)
    {
        nint submenu = NewMenu(L.Get(spec.Label));
        if (paths.Count == 0)
        {
            nint empty = Send(submenu, Sel("addItemWithTitle:action:keyEquivalent:"), String(L.Get("NO RECENT FILES")), 0, String(""));
            Send(empty, Sel("setEnabled:"), 0);
        }
        for (int i = 0; i < paths.Count; i++)
        {
            string path = paths[i];
            string title = Path.GetFileName(path);
            if (title.Length > 52) title = title[..49] + "...";
            nint item = Send(submenu, Sel("addItemWithTitle:action:keyEquivalent:"),
                String(title), Sel("kgMenuAction:"), String(""));
            int tag = MenuCatalog.RecentBaseId + i;
            Send(item, Sel("setTarget:"), target);
            Send(item, Sel("setTag:"), tag);
            Send(item, Sel("setToolTip:"), String(path));
            recentTags[tag] = path;
            recentEntries.Add((item, path));
        }
        nint top = Send(parent, Sel("addItemWithTitle:action:keyEquivalent:"), String(L.Get(spec.Label)), 0, String(""));
        Send(top, Sel("setSubmenu:"), submenu);
        Send(submenu, Sel("release"));
    }

    internal void Update(Func<MenuCommand, bool> enabled, Func<MenuCommand, bool> selected)
    {
        foreach (var (command, entry) in entries)
        {
            Send(entry.Item, Sel("setEnabled:"), enabled(command) ? 1 : 0);
            Send(entry.Item, Sel("setState:"), selected(command) ? 1 : 0);
        }
        foreach (var (item, path) in recentEntries)
            Send(item, Sel("setEnabled:"), enabled(MenuCommand.OpenFile) && (File.Exists(path) || Directory.Exists(path)) ? 1 : 0);
    }

    internal void AssertInstalled()
    {
        if (inserted.Count != MenuCatalog.Groups.Length || entries.Count != MenuCatalog.Actions.Count() ||
            inserted.Any(item => (long)Send(mainMenu, Sel("indexOfItem:"), item) < 0 || Send(item, Sel("submenu")) == 0))
            throw new InvalidOperationException("Native menu groups or commands were not installed.");
    }

    internal void InvokeForTest(MenuCommand command)
    {
        var (menu, _, index) = entries[command];
        Send(menu, Sel("performActionForItemAtIndex:"), index);
    }

    internal void InvokeRecentForTest(int index)
    {
        var (item, _) = recentEntries[index];
        nint menu = Send(item, Sel("menu"));
        nint itemIndex = Send(menu, Sel("indexOfItem:"), item);
        Send(menu, Sel("performActionForItemAtIndex:"), itemIndex);
    }

    internal bool EnabledForTest(MenuCommand command) => Send(entries[command].Item, Sel("isEnabled")) != 0;
    internal bool CheckedForTest(MenuCommand command) => Send(entries[command].Item, Sel("state")) != 0;

    public void Dispose()
    {
        foreach (nint item in inserted)
        {
            Send(mainMenu, Sel("removeItem:"), item);
            Send(item, Sel("release"));
        }
        inserted.Clear();
        Send(target, Sel("release"));
        handler = null;
        recentTags = [];
    }
}
