using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    sealed class WorkspacePointerState
    {
        public float MouseX, MouseY;
        public bool Click, Held, Seeking, EditorScrub;
        public string? ActivePanel;
        public string? MoveMenuPanel;
        public Rect MoveMenuAnchor;
        public Rect InputPanelBounds, ActivePanelRect;
        public float ViewerSourceScroll;
        public string? DockContextPanel;
        public float DockContextX, DockContextY;
        public int DockContextScroll;
        public WorkspaceGeometry Geometry;
        public Rect PreviewFrame, PreviewImage, TrackLabelGrip, EditorSourceRect;
        public Rect EditorTimelineRect, EditorTracksRect, EditorInspectorRect;
    }
    readonly Dictionary<uint, WorkspacePointerState> auxiliaryPointers = [];
    nint ActiveUiWindow => activeWorkspaceWindow.Handle;
    uint ActiveUiWindowId => activeWorkspaceWindow.Id;

    /// <summary>Bind a window's pointer state for one input or render callback, restoring it on every exit path.</summary>
    IDisposable BindPointer(WorkspaceWindowManager.Window window)
    {
        if (window.Main) return new PointerScope(this, mainPointer, window);
        if (!auxiliaryPointers.TryGetValue(window.Id, out var state))
            auxiliaryPointers[window.Id] = state = new();
        return new PointerScope(this, state, window);
    }
    sealed class PointerScope : IDisposable
    {
        readonly Viewer viewer;
        readonly WorkspacePointerState previous;
        readonly WorkspaceWindowManager.Window previousWindow;
        public PointerScope(Viewer viewer, WorkspacePointerState next, WorkspaceWindowManager.Window window)
        { this.viewer = viewer; previous = viewer.activePointer; previousWindow = viewer.activeWorkspaceWindow;
            viewer.activePointer = next; viewer.activeWorkspaceWindow = window; }
        public void Dispose() { viewer.activePointer = previous; viewer.activeWorkspaceWindow = previousWindow; }
    }
    void ForgetPointer(WorkspaceWindowManager.Window window) => auxiliaryPointers.Remove(window.Id);
    string? activePanel { get => activePointer.ActivePanel; set => activePointer.ActivePanel = value; }
    string? moveMenuPanel { get => activePointer.MoveMenuPanel; set => activePointer.MoveMenuPanel = value; }
    Rect moveMenuAnchor { get => activePointer.MoveMenuAnchor; set => activePointer.MoveMenuAnchor = value; }
    Rect inputPanelBounds { get => activePointer.InputPanelBounds; set => activePointer.InputPanelBounds = value; }
    Rect activePanelRect { get => activePointer.ActivePanelRect; set => activePointer.ActivePanelRect = value; }
    float viewerSourceScroll { get => activePointer.ViewerSourceScroll; set => activePointer.ViewerSourceScroll = value; }
    string? dockContextPanel { get => activePointer.DockContextPanel; set => activePointer.DockContextPanel = value; }
    float dockContextX { get => activePointer.DockContextX; set => activePointer.DockContextX = value; }
    float dockContextY { get => activePointer.DockContextY; set => activePointer.DockContextY = value; }
    int dockContextScroll { get => activePointer.DockContextScroll; set => activePointer.DockContextScroll = value; }
}
