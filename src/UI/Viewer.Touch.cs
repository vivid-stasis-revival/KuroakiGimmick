// 将 SDL 单指事件映射为单指针操作，双指独占导航。触摸、实体鼠标与手柄光标分别处理，归一化触摸坐标转换为逻辑界面单位。

using System.Numerics;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    enum TouchNavigation
    {
        None, Timeline, ImageCanvas, TextCanvas
    }
    readonly record struct TouchContact(Vector2 Window, Vector2 Ui);

    readonly Dictionary<(ulong Device, ulong Finger), TouchContact> touchContacts = [];
    (ulong Device, ulong Finger)? touchPrimary, touchSecondary;
    TouchNavigation touchNavigation;
    bool touchHadMulti, touchMoved, touchStartedOnTimeline;
    bool sendingTouchMouse, mouseClickFromTouch;
    Vector2 touchDownUi, touchGestureCenter, touchImageAnchor, touchLastTapUi;
    float touchGestureDistance, touchGestureImageZoom, touchGestureTextZoom;
    double touchGestureBeat, touchGesturePixelsPerBeat;
    int touchGestureTrackScroll;
    long touchDownAt, touchLastTapAt = -1;
    readonly HashSet<Guid> touchSelectedClips = [];
    Guid? touchSelectedClip;
    int touchSelectedWindowEvent;
    double? touchSelectedNoteTime;
    // 鼠标双击在按下时编辑，触摸则在释放前必须可取消，且不增删撤销历史。
    sealed record PendingTouchAction(EditorDocument Owner, long Revision, Action Commit);
    PendingTouchAction? touchTimelineAction;

    void TimelineDoubleClick(Action action)
    {
        if (mouseClickFromTouch && touchPrimary.HasValue && touchContacts.Count == 1 && !touchHadMulti && editor != null)
            touchTimelineAction = new(editor, editor.Revision, action);
        else action();
    }

    TouchContact ReadTouchContact(Sdl.Event e)
    {
        // SDL_TouchFingerEvent 坐标相对于窗口归一化；使用与鼠标 LogicalInput 相同的 UI 变换，避免重复计入物理像素密度。
        Sdl.SDL_GetWindowSize(host.Window, out int width, out int height);
        Vector2 raw = new(e.FingerX * width, e.FingerY * height);
        var viewport = Viewport();
        return new(raw, new(raw.X * viewport.InputScaleX, raw.Y * viewport.InputScaleY));
    }

    void SendTouchMouse(uint type, TouchContact contact, byte clicks = 1)
    {
        // 合成事件使用鼠标 ID 0。Handle 入口忽略 SDL_TOUCH_MOUSEID 的重复事件，真实鼠标与 Joy-Con 仍走原有路径。
        sendingTouchMouse = true;
        try
        {
            Handle(new Sdl.Event
            {
                Type = type,
                WindowID = Sdl.SDL_GetWindowID(host.Window),
                MouseWhich = 0,
                Button = type == 0x400 ? (byte)0 : (byte)1,
                Clicks = clicks,
                X = contact.Window.X,
                Y = contact.Window.Y
            });
        }
        finally
        {
            sendingTouchMouse = false;
        }
    }

    bool HandleTouchInput(Sdl.Event e)
    {
        if (e.Type is < Sdl.FingerDown or > Sdl.FingerCanceled) return false;
        // SDL_TouchFingerEvent.windowID 在字节偏移 52，鼠标事件的 windowID 在偏移 16。
        if (e.FingerWindowID != 0 && e.FingerWindowID != Sdl.SDL_GetWindowID(host.Window))
            return true;
        if (!float.IsFinite(e.FingerX) || !float.IsFinite(e.FingerY))
        {
            if (e.Type == Sdl.FingerCanceled) CancelTouchInput();
            return true;
        }

        var id = (Device: e.TouchID, Finger: e.FingerID);
        var contact = ReadTouchContact(e);
        if (e.Type == Sdl.FingerDown)
        {
            if (touchContacts.ContainsKey(id)) return true;
            touchContacts.Add(id, contact);
            if (touchContacts.Count == 1)
            {
                touchPrimary = id;
                touchDownUi = contact.Ui;
                touchDownAt = Environment.TickCount64;
                touchMoved = touchHadMulti = false;
                touchTimelineAction = null;
                touchNavigation = TouchNavigation.None;
                touchStartedOnTimeline = editorMode && editorTimelineRect.Contains(contact.Ui.X, contact.Ui.Y);
                touchSelectedClips.Clear();
                foreach (var selected in selectedClips) touchSelectedClips.Add(selected);
                touchSelectedClip = selectedClip;
                touchSelectedWindowEvent = selectedWindowEvent;
                touchSelectedNoteTime = selectedNoteTime;
                long delta = touchDownAt - touchLastTapAt;
                byte clicks = touchLastTapAt >= 0 && delta >= 0 && delta <= 360 &&
                    Vector2.DistanceSquared(touchLastTapUi, contact.Ui) < 24 * 24 ? (byte)2 : (byte)1;
                SendTouchMouse(0x401, contact, clicks);
            }
            else if (touchContacts.Count == 2 && !touchHadMulti)
            {
                // 第二根手指加入时不得提交第一根手指开始的编辑。
                touchHadMulti = true;
                touchLastTapAt = -1;
                AbortPrimaryTouch();
                StartTwoFingerNavigation();
            }
            // 记录第三根手指仅为避免其抬起时意外重新启动鼠标拖动。
            return true;
        }

        if (!touchContacts.ContainsKey(id)) return true;
        touchContacts[id] = contact;
        if (e.Type == Sdl.FingerMotion)
        {
            if (touchHadMulti) UpdateTwoFingerNavigation();
            else if (touchPrimary == id)
            {
                touchMoved |= Vector2.DistanceSquared(touchDownUi, contact.Ui) > 6 * 6;
                SendTouchMouse(0x400, contact);
            }
            return true;
        }

        // FingerUp 或 FingerCanceled 后，只要多指手势中仍有手指未抬起，就继续忽略点击、框选与拖动提交。
        if (!touchHadMulti && touchPrimary == id)
        {
            if (e.Type == Sdl.FingerUp)
            {
                SendTouchMouse(0x402, contact);
                var pending = touchTimelineAction;
                touchTimelineAction = null;
                if (pending != null && !touchMoved && editorMode && !Busy &&
                    ReferenceEquals(editor, pending.Owner) && editor.Revision == pending.Revision)
                    pending.Commit();
                long duration = Environment.TickCount64 - touchDownAt;
                if (!touchMoved && duration >= 0 && duration <= 450)
                {
                    touchLastTapAt = Environment.TickCount64;
                    touchLastTapUi = contact.Ui;
                }
                else touchLastTapAt = -1;
            }
            else
            {
                AbortPrimaryTouch();
                touchLastTapAt = -1;
            }
        }
        touchContacts.Remove(id);
        if (touchContacts.Count == 0)
        {
            touchPrimary = touchSecondary = null;
            touchHadMulti = false;
            touchNavigation = TouchNavigation.None;
            touchSelectedClips.Clear();
        }
        return true;
    }

    void AbortPrimaryTouch()
    {
        touchTimelineAction = null;
        // 预览手势可取消。避免合成鼠标释放而触发 FinishDrag 或 FinishMarquee，误提交手势中的更改。
        if (draggedTrackKey != null) CancelTrackOrder();
        if (sceneDirectDrag != null) CancelSceneDirectGesture();
        if (textDrag != null)
        {
            textDrag = null;
            textDragValues = null;
            Sdl.SDL_CaptureMouse(false);
        }
        if (imageObjectDrag != null || imageGroupDrag != null) CancelImageGesture();
        if (prGridHudDragging)
        {
            prGridHudDragging = false;
            Sdl.SDL_CaptureMouse(false);
        }
        if (resizingLayout != 0) EndLayoutResize();
        editDrag = null;
        marqueeActive = false;
        marqueePending = null;
        panActive = false;
        editorScrub = false;
        click = held = seeking = false;
        if (touchStartedOnTimeline)
        {
            selectedClips.Clear();
            foreach (var selected in touchSelectedClips) selectedClips.Add(selected);
            selectedClip = touchSelectedClip;
            selectedWindowEvent = touchSelectedWindowEvent;
            selectedNoteTime = touchSelectedNoteTime;
            SyncPickedProxy(selectedClip);
        }
    }

    void StartTwoFingerNavigation()
    {
        touchNavigation = TouchNavigation.None;
        if (!touchPrimary.HasValue || touchContacts.Count != 2) return;
        foreach (var id in touchContacts.Keys)
            if (id != touchPrimary.Value)
            {
                touchSecondary = id;
                break;
            }
        if (!touchSecondary.HasValue) return;

        Vector2 first = touchContacts[touchPrimary.Value].Ui;
        Vector2 second = touchContacts[touchSecondary.Value].Ui;
        touchGestureCenter = (first + second) * .5f;
        touchGestureDistance = Vector2.Distance(first, second);

        if (!editorMode || Busy || settings || UiBlockingOverlayVisible) return;
        if (editorTimelineRect.Contains(first.X, first.Y) &&
            editorTimelineRect.Contains(second.X, second.Y))
        {
            touchNavigation = TouchNavigation.Timeline;
            touchGestureBeat = BeatAt(touchGestureCenter.X);
            touchGesturePixelsPerBeat = pixelsPerBeat;
            touchGestureTrackScroll = trackScroll;
            // 手指手势表示用户主动导航，不能当作 FOLLOW 播放。
            if (editorFollow && transport.Playing && !preferences.AlwaysFollow) editorFollow = false;
        }
        else if (imageCanvas && previewImage.Contains(first.X, first.Y) &&
                 previewImage.Contains(second.X, second.Y))
        {
            touchNavigation = TouchNavigation.ImageCanvas;
            touchGestureImageZoom = imageViewZoom;
            touchImageAnchor = ImageCanvasMap.Create(previewImage.X, previewImage.Y,
                previewImage.W, previewImage.H, imageViewCenter, imageViewZoom).ToWorld(touchGestureCenter);
        }
        else if (textCanvas && previewImage.Contains(first.X, first.Y) &&
                 previewImage.Contains(second.X, second.Y))
        {
            // 字幕画布固定视图中心，可双指缩放；平移仅用于图片画布和时间轴。
            touchNavigation = TouchNavigation.TextCanvas;
            touchGestureTextZoom = textZoom;
        }
    }

    void UpdateTwoFingerNavigation()
    {
        if (touchNavigation == TouchNavigation.None ||
            !touchPrimary.HasValue || !touchSecondary.HasValue ||
            !touchContacts.TryGetValue(touchPrimary.Value, out var first) ||
            !touchContacts.TryGetValue(touchSecondary.Value, out var second)) return;
        Vector2 center = (first.Ui + second.Ui) * .5f;
        float distance = Vector2.Distance(first.Ui, second.Ui);
        // 双指起点距离过小时跳过缩放，平移仍可继续。
        float ratio = touchGestureDistance < 24 ? 1 :
            Math.Clamp(distance / touchGestureDistance, .25f, 4f);
        switch (touchNavigation)
        {
            case TouchNavigation.Timeline:
                pixelsPerBeat = Math.Clamp(touchGesturePixelsPerBeat * ratio, 3, 640);
                double start = Math.Max(-64, touchGestureBeat -
                    (center.X - editorTracksRect.X) / pixelsPerBeat);
                if (Math.Abs(center.X - touchGestureCenter.X) > 3) ScrollTimeline(start);
                else beatStart = start;
                trackScroll = Math.Max(0, touchGestureTrackScroll -
                    (int)Math.Round((center.Y - touchGestureCenter.Y) / 35));
                break;
            case TouchNavigation.ImageCanvas:
                imageViewZoom = Math.Clamp(touchGestureImageZoom * ratio, .05f, 8);
                float fit = Math.Min(previewImage.W / 320, previewImage.H / 180);
                float scale = Math.Max(.001f, fit * imageViewZoom);
                Vector2 viewportCenter = new(previewImage.X + previewImage.W * .5f,
                    previewImage.Y + previewImage.H * .5f);
                imageViewCenter = touchImageAnchor - (center - viewportCenter) / scale;
                break;
            case TouchNavigation.TextCanvas:
                textZoom = Math.Clamp(touchGestureTextZoom * ratio, .25f, 8);
                break;
        }
    }

    void CancelTouchInput()
    {
        if (touchContacts.Count == 0) return;
        AbortPrimaryTouch();
        touchContacts.Clear();
        touchPrimary = touchSecondary = null;
        touchHadMulti = false;
        touchNavigation = TouchNavigation.None;
        touchLastTapAt = -1;
        touchSelectedClips.Clear();
    }
}
