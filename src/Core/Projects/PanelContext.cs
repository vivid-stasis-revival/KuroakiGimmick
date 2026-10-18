using System.Numerics;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>Per-window input projection and panel bounds. Shared editing state stays in Viewer/EditorDocument.</summary>
public readonly record struct PanelContext(string WindowId, string PanelId, Rect Bounds,
    UiViewport Viewport, Vector2 WindowMouse, bool Focused)
{
    public Vector2 LocalMouse => WindowMouse - new Vector2(Bounds.X, Bounds.Y);
    public bool ContainsPointer => Bounds.Contains(WindowMouse.X, WindowMouse.Y);
    public static Vector2 FromNative(float x, float y, UiViewport viewport) =>
        new(x * viewport.InputScaleX, y * viewport.InputScaleY);
    public Vector2 SceneMouse(Rect image, float sceneWidth = 320, float sceneHeight = 180) =>
        new((WindowMouse.X - image.X) * sceneWidth / Math.Max(1, image.W),
            (WindowMouse.Y - image.Y) * sceneHeight / Math.Max(1, image.H));
}

public readonly record struct PanelDefinition(string Id, float MinWidth, float MinHeight, bool CanHide);

/// <summary>Identity and minimum dimensions for every primary dock panel.</summary>
public static class PanelRegistry
{
    public static readonly IReadOnlyDictionary<string, PanelDefinition> Viewer = New(
        new("sources", 202, 350, true), new("preview", 320, 230, false),
        new("transport", 600, 280, true), new("inspector", 258, 460, true),
        new("export", 258, 350, true), new("diagnostics", 248, 240, true));
    public static readonly IReadOnlyDictionary<string, PanelDefinition> Editor = New(
        new("tools", 202, 300, true), new("images", 202, 300, true),
        new("text", 202, 300, true), new("preview", 320, 230, false),
        new("inspector", 248, 300, true), new("timeline", 900, 300, false));
    // Main-window sidebars retain the outer 24px alignment and leave breathing room
    // on the divider side. Width minima include this padding.
    public const float SidebarGutter = 8;
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
