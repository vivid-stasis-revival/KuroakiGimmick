using System.Numerics;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>单个窗口内的指针投影与面板范围；共享编辑状态由 Viewer 和 EditorDocument 持有。</summary>
/// <param name="WindowId">窗口标识。</param>
/// <param name="PanelId">面板标识。</param>
/// <param name="Bounds">面板布局边界。</param>
/// <param name="Viewport">所属窗口的视口。</param>
/// <param name="WindowMouse">窗口坐标中的指针位置。</param>
/// <param name="Focused">是否获得焦点。</param>
public readonly record struct PanelContext(string WindowId, string PanelId, Rect Bounds,
    UiViewport Viewport, Vector2 WindowMouse, bool Focused)
{
    /// <summary>将窗口指针位置换算为相对面板左上角的坐标。</summary>
    public Vector2 LocalMouse => WindowMouse - new Vector2(Bounds.X, Bounds.Y);
    /// <summary>指针是否在面板矩形范围内。</summary>
    public bool ContainsPointer => Bounds.Contains(WindowMouse.X, WindowMouse.Y);
    /// <summary>使用视口输入倍率换算原生指针坐标。</summary>
    /// <param name="x">SDL 原生横坐标。</param>
    /// <param name="y">SDL 原生纵坐标。</param>
    /// <param name="viewport">窗口的逻辑视口与输入缩放比例。</param>
    /// <returns>对应的逻辑界面坐标。</returns>
    public static Vector2 FromNative(float x, float y, UiViewport viewport) =>
        new(x * viewport.InputScaleX, y * viewport.InputScaleY);
    /// <summary>将窗口指针按预览矩形比例映射到场景坐标。</summary>
    /// <param name="image">预览图在窗口内的矩形。</param>
    /// <param name="sceneWidth">场景坐标宽度。</param>
    /// <param name="sceneHeight">场景坐标高度。</param>
    /// <returns>场景坐标中的指针位置。</returns>
    public Vector2 SceneMouse(Rect image, float sceneWidth = 320, float sceneHeight = 180) =>
        new((WindowMouse.X - image.X) * sceneWidth / Math.Max(1, image.W),
            (WindowMouse.Y - image.Y) * sceneHeight / Math.Max(1, image.H));
}

/// <summary>面板参与工作区布局时的尺寸约束。</summary>
/// <param name="Id">稳定面板键。</param>
/// <param name="MinWidth">允许的最小宽度。</param>
/// <param name="MinHeight">允许的最小高度。</param>
/// <param name="CanHide">是否允许隐藏。</param>
public readonly record struct PanelDefinition(string Id, float MinWidth, float MinHeight, bool CanHide);

/// <summary>各主要停靠面板的身份与最小尺寸约束。</summary>
public static class PanelRegistry
{
    /// <summary>按面板标识索引的预览模式布局约束。</summary>
    public static readonly IReadOnlyDictionary<string, PanelDefinition> Viewer = New(
        new("sources", 202, 350, true), new("preview", 320, 230, false),
        new("transport", 600, 280, true), new("inspector", 258, 460, true),
        new("export", 258, 350, true), new("diagnostics", 248, 240, true));
    /// <summary>按面板标识索引的编辑模式布局约束。</summary>
    public static readonly IReadOnlyDictionary<string, PanelDefinition> Editor = New(
        new("tools", 202, 300, true), new("images", 202, 300, true),
        new("text", 202, 300, true), new("preview", 320, 230, false),
        new("inspector", 248, 300, true), new("timeline", 900, 300, false));
    // 主窗口侧栏外侧保留 24 像素对齐间距，分隔条一侧也预留空白；最小宽度包含这些边距。
    /// <summary>侧栏内容布局使用的间距。</summary>
    public const float SidebarGutter = 8;
    /// <summary>按面板类别从分配区域计算主要内容矩形。</summary>
    /// <param name="panel">停靠面板的稳定键。</param>
    /// <param name="bounds">面板的完整布局矩形。</param>
    /// <returns>面板内分配给主体的区域。</returns>
    public static Rect MainContent(string panel, Rect bounds) => panel switch
    {
        "sources" or "tools" or "images" or "text" =>
            new(bounds.X, bounds.Y, Math.Max(0, bounds.W - SidebarGutter), bounds.H),
        "inspector" or "export" or "diagnostics" =>
            new(bounds.X + SidebarGutter, bounds.Y, Math.Max(0, bounds.W - SidebarGutter), bounds.H),
        _ => bounds
    };

    static IReadOnlyDictionary<string, PanelDefinition> New(params PanelDefinition[] entries) =>
        entries.ToDictionary(p => p.Id, StringComparer.Ordinal);
}
