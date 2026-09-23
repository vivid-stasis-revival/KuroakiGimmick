namespace KuroakiGimmick.UI;

/// <summary>macOS 与 Windows 共用的菜单结构；命令由 Viewer 执行，平台层只负责呈现与传递点击。</summary>
internal enum MenuCommand
{
    None = 0,
    OpenFile = 1, OpenFolder, RecentMenu, Welcome, AttachFiles, Reload, Save, SaveAs,
    ExportChart, ExportVideo, InfoCard, Quit,
    Undo, Redo, Copy, Paste, Duplicate, Delete, AddMod,
    PlayPause, StepBack, StepForward, JumpStart, JumpEnd, Loop, Follow,
    ToggleEditor, Notes, Effects, GameUi, Fullscreen, Layout, Settings,
    QuickHelp, Docs
}

internal readonly record struct MenuItemSpec(MenuCommand Command, string Label, string Key = "", bool Shift = false);
internal readonly record struct MenuGroupSpec(string Label, MenuItemSpec[] Items);

internal static class MenuCatalog
{
    internal const int RecentBaseId = 1000;
    internal static readonly MenuGroupSpec[] Groups =
    [
        new("FILE",
        [
            new(MenuCommand.OpenFile, "OPEN FILE...", "o"),
            new(MenuCommand.OpenFolder, "OPEN SONG FOLDER..."),
            new(MenuCommand.RecentMenu, "OPEN RECENT"),
            new(MenuCommand.Welcome, "WELCOME / RECENT"),
            new(MenuCommand.AttachFiles, "ADD FILES / RESOURCES..."),
            new(MenuCommand.None, ""),
            new(MenuCommand.Reload, "RELOAD SOURCES"),
            new(MenuCommand.Save, "SAVE PROJECT", "s"),
            new(MenuCommand.SaveAs, "SAVE AS...", "s", true),
            new(MenuCommand.None, ""),
            new(MenuCommand.ExportChart, "EXPORT CHART / VSM..."),
            new(MenuCommand.ExportVideo, "EXPORT MP4..."),
            new(MenuCommand.InfoCard, "INFO CARD / PNG..."),
            new(MenuCommand.None, ""),
            new(MenuCommand.Quit, "EXIT")
        ]),
        new("EDIT",
        [
            new(MenuCommand.Undo, "UNDO"),
            new(MenuCommand.Redo, "REDO"),
            new(MenuCommand.None, ""),
            new(MenuCommand.Copy, "COPY SELECTION"),
            new(MenuCommand.Paste, "PASTE EVENTS"),
            new(MenuCommand.Duplicate, "DUPLICATE SELECTION"),
            new(MenuCommand.Delete, "DELETE SELECTION"),
            new(MenuCommand.AddMod, "ADD GIMMICK AT PLAYHEAD"),
        ]),
        new("PLAYBACK",
        [
            new(MenuCommand.PlayPause, "PLAY / PAUSE"),
            new(MenuCommand.StepBack, "PREVIOUS FRAME"),
            new(MenuCommand.StepForward, "NEXT FRAME"),
            new(MenuCommand.JumpStart, "GO TO START"),
            new(MenuCommand.JumpEnd, "GO TO END"),
            new(MenuCommand.None, ""),
            new(MenuCommand.Loop, "LOOP"),
            new(MenuCommand.Follow, "FOLLOW")
        ]),
        new("VIEW",
        [
            new(MenuCommand.ToggleEditor, "EDITOR / VIEWER"),
            new(MenuCommand.Notes, "NOTES"),
            new(MenuCommand.Effects, "FX"),
            new(MenuCommand.GameUi, "VS UI"),
            new(MenuCommand.Fullscreen, "FULLSCREEN"),
            new(MenuCommand.None, ""),
            new(MenuCommand.Layout, "LAYOUT"),
            new(MenuCommand.Settings, "SETTINGS")
        ]),
        new("HELP",
        [
            new(MenuCommand.QuickHelp, "QUICK CONTROLS"),
            new(MenuCommand.Docs, "VSM DOCUMENTATION")
        ])
    ];

    internal static IEnumerable<MenuCommand> Actions => Groups.SelectMany(group => group.Items)
        .Select(item => item.Command).Where(command => command is not (MenuCommand.None or MenuCommand.RecentMenu));
}
