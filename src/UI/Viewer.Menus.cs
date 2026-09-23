using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void InstallNativeMenu()
    {
        try
        {
            macMenu?.Dispose(); macMenu = null;
            windowsMenu?.Dispose(); windowsMenu = null;
            Action<MenuCommand, string?> callback = (command, path) =>
                actions.Enqueue(() => RunMenu(command, path));
            if (OperatingSystem.IsMacOS()) macMenu = new MacMenuBar(callback, preferences.RecentProjects);
            else if (OperatingSystem.IsWindows()) windowsMenu = new WindowsMenuBar(host.Window, callback, preferences.RecentProjects);
            menuStateHash = int.MinValue;
            UpdateNativeMenuState();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            macMenu = null; windowsMenu = null;
            Console.Error.WriteLine("Native menu could not be installed: " + ex.Message);
        }
    }

    bool MenuEnabled(MenuCommand command)
    {
        bool dialogFree = !Busy && !dialogOpen && pendingDiscard == null && !modalActive && !InlineActive;
        if (command == MenuCommand.Quit) return !dialogOpen;
        if (command == MenuCommand.Welcome) return dialogFree && !ImageGestureActive;
        if (command is MenuCommand.OpenFile or MenuCommand.OpenFolder)
            return dialogFree && !ImageGestureActive && (!UiBlockingOverlayVisible || StartupVisible);
        if (command is MenuCommand.Settings or MenuCommand.QuickHelp or MenuCommand.Docs)
            return dialogFree && !ImageGestureActive && !WorkflowVisible && !ReferenceVisible;
        if (!dialogFree || UiBlockingOverlayVisible || ImageGestureActive || InlineActive) return false;
        return command switch
        {
            MenuCommand.Reload or MenuCommand.Save or MenuCommand.SaveAs or
                MenuCommand.ExportVideo or MenuCommand.InfoCard or MenuCommand.PlayPause or
                MenuCommand.StepBack or MenuCommand.StepForward or MenuCommand.JumpStart or
                MenuCommand.JumpEnd or MenuCommand.ToggleEditor or MenuCommand.Notes or
                MenuCommand.Effects or MenuCommand.GameUi => !Current.IsEmpty,
            MenuCommand.AttachFiles => true,
            MenuCommand.ExportChart => !Current.IsEmpty,
            MenuCommand.Copy or MenuCommand.Paste or MenuCommand.Duplicate or
                MenuCommand.Delete or MenuCommand.AddMod or MenuCommand.Loop or MenuCommand.Follow => editorMode && editor != null,
            MenuCommand.Undo => editorMode && editor?.CanUndo == true,
            MenuCommand.Redo => editorMode && editor?.CanRedo == true,
            MenuCommand.Layout or MenuCommand.Fullscreen or MenuCommand.Quit => true,
            _ => false
        };
    }

    bool MenuChecked(MenuCommand command) => command switch
    {
        MenuCommand.ToggleEditor => editorMode,
        MenuCommand.Notes => notes,
        MenuCommand.Effects => effects,
        MenuCommand.GameUi => Current.Project.GameUiEnabled,
        MenuCommand.Fullscreen => full,
        MenuCommand.Loop => loopEnabled,
        MenuCommand.Follow => editorFollow,
        MenuCommand.PlayPause => transport.Playing,
        _ => false
    };

    void UpdateNativeMenuState()
    {
        if (macMenu == null && windowsMenu == null) return;
        var state = new HashCode();
        state.Add(Busy); state.Add(dialogOpen); state.Add(UiBlockingOverlayVisible); state.Add(ImageGestureActive);
        state.Add(InlineActive);
        state.Add(Current.IsEmpty); state.Add(editorMode); state.Add(editor?.CanUndo); state.Add(editor?.CanRedo);
        state.Add(notes); state.Add(effects); state.Add(Current.Project.GameUiEnabled); state.Add(full);
        state.Add(loopEnabled); state.Add(editorFollow); state.Add(transport.Playing);
        int hash = state.ToHashCode();
        if (hash == menuStateHash) return;
        menuStateHash = hash;
        macMenu?.Update(MenuEnabled, MenuChecked);
        windowsMenu?.Update(MenuEnabled, MenuChecked);
    }

    void RunMenu(MenuCommand command, string? recentPath = null)
    {
        if (!MenuEnabled(command)) return;
        if (recentPath != null)
        {
            if (!File.Exists(recentPath) && !Directory.Exists(recentPath))
            { message = L.Get("Recent source is missing: ") + recentPath; return; }
            if (StartupVisible) LoadFromStartup([recentPath], true);
            else LoadPaths([recentPath]);
            return;
        }
        switch (command)
        {
            case MenuCommand.OpenFile:
                if (StartupVisible) BeginStartupDialog(); else ChooseOpen();
                break;
            case MenuCommand.OpenFolder:
                if (StartupVisible) BeginStartupDialog(folder: true);
                else Dialog(false, null, paths => LoadPaths(paths), folder: true);
                break;
            case MenuCommand.Welcome:
                if (settings) { settings = false; SaveSettings(); }
                help = false; startup = true; click = held = false;
                break;
            case MenuCommand.AttachFiles: ChooseOpen(true); break;
            case MenuCommand.Reload: Reload(); break;
            case MenuCommand.Save: SaveProject(); break;
            case MenuCommand.SaveAs:
                if (editor != null) SaveEditor(true); else SaveProject();
                break;
            case MenuCommand.ExportChart: OpenChartExport(); break;
            case MenuCommand.ExportVideo: ChooseExport(); break;
            case MenuCommand.InfoCard: OpenInfoCard(); break;
            case MenuCommand.Quit:
                if (!GuardUnsaved(() => quit = true)) quit = true;
                break;
            case MenuCommand.Undo: editor!.Undo(); layoutRevision = -1; break;
            case MenuCommand.Redo: editor!.Redo(); layoutRevision = -1; break;
            case MenuCommand.Copy: CopySelection(); break;
            case MenuCommand.Paste: PasteClipboard(); break;
            case MenuCommand.Duplicate: DuplicateSelection(); break;
            case MenuCommand.Delete: DeleteSelection(); break;
            case MenuCommand.AddMod: AddMod(); break;
            case MenuCommand.PlayPause: transport.SetPlaying(!transport.Playing); break;
            case MenuCommand.StepBack:
                transport.SetPlaying(false); transport.Seek(transport.Position - 1.0 / fps); break;
            case MenuCommand.StepForward:
                transport.SetPlaying(false); transport.Seek(transport.Position + 1.0 / fps); break;
            case MenuCommand.JumpStart: transport.Seek(0); break;
            case MenuCommand.JumpEnd: transport.Seek(Current.Duration); break;
            case MenuCommand.Loop: loopEnabled = !loopEnabled; break;
            case MenuCommand.Follow: editorFollow = !editorFollow; break;
            case MenuCommand.ToggleEditor: ToggleWorkspace(); break;
            case MenuCommand.Notes: notes = !notes; break;
            case MenuCommand.Effects: effects = !effects; break;
            case MenuCommand.GameUi: Current.Project.GameUiEnabled = !Current.Project.GameUiEnabled; break;
            case MenuCommand.Fullscreen: ToggleFull(); break;
            case MenuCommand.Layout: OpenLayout(); break;
            case MenuCommand.Settings:
                startup = false; OpenSettings(); click = false; break;
            case MenuCommand.QuickHelp:
                startup = false; help = true; click = false; break;
            case MenuCommand.Docs:
                startup = false; OpenReference(); break;
        }
        UpdateNativeMenuState();
    }

    public void SmokeMacMenu()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Run this menu smoke on macOS.");
        string sample = Path.GetFullPath("Samples/EditorDemo/demo.sgv.json");
        preferences.RecentProjects = [sample];
        InstallNativeMenu(); InstallNativeMenu();
        if (macMenu == null) throw new Exception("Native macOS menu was not created.");
        macMenu.AssertInstalled();
        if (!macMenu.EnabledForTest(MenuCommand.OpenFile) || macMenu.EnabledForTest(MenuCommand.Undo))
            throw new Exception("Native menu context did not disable editor actions for an empty session.");
        startup = false;
        macMenu.InvokeForTest(MenuCommand.Welcome);
        Update();
        if (!startup) throw new Exception("Native Welcome menu action did not reach Viewer.");
        Draw(1440, 940);
        macMenu.InvokeRecentForTest(0);
        Update();
        if (loading is { } task)
        {
            if (!task.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Native recent menu load timed out.");
            Update();
        }
        if (Current.ProjectPath != sample || startup || editor == null)
            throw new Exception("Native recent menu action did not open the project in Editor.");
        preferences.UiAnimations = false;
        Draw(1440, 940);
        Update();
        bool before = notes;
        macMenu.InvokeForTest(MenuCommand.Notes);
        Update();
        if (notes == before || macMenu.CheckedForTest(MenuCommand.Notes) != notes)
            throw new Exception("Native View menu did not toggle and check notes.");
        macMenu.InvokeForTest(MenuCommand.ExportChart);
        Update();
        if (workflow != "export") throw new Exception("Native File menu did not open chart export.");
        Console.WriteLine("Native macOS menu groups, direct recent open, View and export actions passed.");
    }

    public void SmokeWindowsMenu()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Run this menu smoke on Windows.");
        string sample = Path.GetFullPath("Samples/EditorDemo/demo.sgv.json");
        preferences.RecentProjects = [sample];
        InstallNativeMenu();
        if (windowsMenu == null) throw new Exception("Native Windows menu was not created.");
        windowsMenu.AssertInstalled();
        void Pump()
        {
            while (Sdl.SDL_PollEvent(out var e)) Handle(e);
            Update();
        }
        windowsMenu.PostForTest(MenuCommand.Welcome);
        for (int i = 0; i < 20 && !startup; i++) { Pump(); Sdl.SDL_Delay(1); }
        if (!startup) throw new Exception("Win32 Welcome menu command did not reach Viewer.");
        windowsMenu.PostForTest(MenuCommand.OpenFile, 0);
        for (int i = 0; i < 20 && loading == null && Current.ProjectPath == null; i++) { Pump(); Sdl.SDL_Delay(1); }
        if (loading is { } task)
        {
            if (!task.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Win32 recent menu load timed out.");
            Pump();
        }
        if (Current.ProjectPath != sample || startup || editor == null)
            throw new Exception("Win32 recent menu did not open the project in Editor.");
        Console.WriteLine("Native Windows menu groups, Welcome and direct recent open passed.");
    }
}
