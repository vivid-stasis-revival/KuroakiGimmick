using System.Numerics;
using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    readonly record struct ProxyPickFrame(Session Session, Rect Viewport, double Time, int? OnlyProxy);
    ProxyPickFrame? proxyPickFrame;

    int? PickProxyInScene(Vector2 pointer, ProxyPickFrame frame)
    {
        if (!frame.Viewport.Contains(pointer.X, pointer.Y)) return null;
        var logical = new Vector2((pointer.X - frame.Viewport.X) * 320 / frame.Viewport.W,
            (pointer.Y - frame.Viewport.Y) * 180 / frame.Viewport.H);
        int count = Math.Min(frame.Session.Chart.Proxies, EditableProxyCount);
        bool custom = frame.Session.Chart.ObjectName == "obj_custom_gimmick";
        var timeline = frame.Session.Timeline;
        // 拾取按已经显示的帧采样，避免使用正在推进的音频播放头。
        return ProxyScenePicking.Pick(timeline, frame.Time, custom, count, logical, frame.OnlyProxy);
    }

    bool HandleProxySceneGesture(Sdl.Event e) => e.Type == 0x401 && e.Button == 1 &&
        // SDL 联合体中鼠标 Y 与 Event.Modifiers 有重叠，此处应通过 SDL_GetModState 查询修饰键。
        HandleProxySceneGesture(e, (Sdl.SDL_GetModState() & 3) != 0);

    bool HandleProxySceneGesture(Sdl.Event e, bool forceProxy)
    {
        if (e.Type != 0x401 || e.Button != 1 ||
            e.WindowID != 0 && e.WindowID != ActiveUiWindowId) return false;
        if (!editorMode || settings || UiOverlayVisible || Busy || sceneDirectDrag != null ||
            ImageGestureActive || editDrag != null || resizingLayout != 0 || prGridHudDragging || !TryPrGridTarget(out _)) return false;
        if (proxyPickFrame is not { } frame || !ReferenceEquals(frame.Session, Current) ||
            !frame.Viewport.Contains(e.X, e.Y) || prGridHudRect.Contains(e.X, e.Y) ||
            prGridLabels.Any(label => label.Bounds.Contains(e.X, e.Y))) return false;
        if (!forceProxy && e.Clicks < 2) return false;
        var pointer = new Vector2(e.X, e.Y);
        // 普通双击保留原图片和文字选择及拖动；按 Shift 时预留第一次按下，避免双击后方 proxy 时先拖动已选的背景图。
        var intent = ProxyScenePicking.Resolve(true, forceProxy, e.Clicks,
            frame.OnlyProxy == null && SceneDirectPick(pointer) != null);
        if (intent == ProxyScenePicking.Intent.Pass) return false;
        mouseX = e.X;
        mouseY = e.Y;
        if (intent == ProxyScenePicking.Intent.Pick && PickProxyInScene(pointer, frame) is int target)
        {
            SelectProxyFromGuide(target);
            message = L.Get("Selected proxy in scene: ") + $"P{target}";
        }
        else if (!forceProxy) return false;
        click = held = false;
        return true;
    }
}
