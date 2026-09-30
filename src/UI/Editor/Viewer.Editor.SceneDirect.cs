using System.Numerics;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// GAME SCENE 直接操作层。最终预览仍由 SceneRenderer 绘制；本层只在其上建立作者坐标的命中框，
/// 双击选中 image/text，随后拖拽只提交 X/Y。选择框故意画在最终后处理之后，因此即使 uialpha/FX/proxy
/// 让对象很难看清，编辑器手柄本身也不会一起消失。一次拖拽只在松手时写一个 Undo 事务。
/// </summary>
public sealed partial class Viewer
{
    enum SceneDirectKind { Image, Text }
    sealed record SceneDirectHit(SceneDirectKind Kind, string Id, Vector2[] Corners);
    sealed record SceneDirectDrag(EditorDocument Owner, long Revision, SceneDirectKind Kind, string Id,
        Vector2 Pointer, float ScaleX, float ScaleY, double X, double Y, ImageEditPose ImagePose);

    readonly List<SceneDirectHit> sceneDirectHits = [];
    SceneDirectDrag? sceneDirectDrag;
    ImageEditPose sceneDirectImagePose;
    Dictionary<string, double>? sceneDirectTextValues;
    bool sceneDirectMoved;

    static bool SceneDirectContains(Vector2 pointer, Vector2[] corners)
    {
        if (corners.Length != 4) return false;
        float? sign = null;
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = corners[(i + 1) % 4] - corners[i], b = pointer - corners[i];
            float cross = a.X * b.Y - a.Y * b.X;
            if (Math.Abs(cross) < .001f) continue;
            float current = MathF.Sign(cross);
            if (sign == null) sign = current;
            else if (sign != current) return false;
        }
        return true;
    }

    SceneDirectHit? SceneDirectPick(Vector2 pointer) =>
        sceneDirectHits.AsEnumerable().Reverse().FirstOrDefault(hit => SceneDirectContains(pointer, hit.Corners));

    /// <summary>把 320x180 作者坐标映射到实际 PREVIEW rect；黑边不参与命中。</summary>
    static Matrix3x2 SceneDirectProjection(Rect viewport) =>
        Matrix3x2.CreateScale(viewport.W / 320f, viewport.H / 180f) * Matrix3x2.CreateTranslation(viewport.X, viewport.Y);

    /// <summary>
    /// 在最终 scene 之上建立 image/text 的 authoring overlay。Custom text 的原层级是 priority 10：
    /// priority &lt; 10 的图在它下面，priority &gt;= 10 的图在它上面；Base/其它对象的 text 在全部 VSP 图之后。
    /// 这样重叠时双击优先选择视觉上更靠上的对象，而不是按 ID 猜。
    /// </summary>
    void DrawSceneDirectManipulation(Rect viewport, double time)
    {
        sceneDirectHits.Clear();
        if (editor == null || imageCanvas || textCanvas || desktopPreview || viewport.W <= 0 || viewport.H <= 0) return;

        EnsureImageModel(); EnsureTexts();
        var projection = SceneDirectProjection(viewport);
        double beat = Current.Timeline.Bpm.Beat(time);
        var imageOverrides = new Dictionary<string, double>(StringComparer.Ordinal);
        if (sceneDirectDrag is { Kind: SceneDirectKind.Image } imageDrag)
        {
            imageOverrides["imgx_" + imageDrag.Id] = sceneDirectImagePose.X;
            imageOverrides["imgy_" + imageDrag.Id] = sceneDirectImagePose.Y;
        }
        var images = Current.Images.At(Current.Timeline, time, imageOverrides).ToArray();

        void AddImages(Func<CustomImages.Item, bool> predicate)
        {
            foreach (var (item, pose) in images)
            {
                if (!predicate(item)) continue;
                Vector2 Corner(float x, float y) => Vector2.Transform(Vector2.Transform(new(x, y), pose.Matrix), projection);
                sceneDirectHits.Add(new(SceneDirectKind.Image, item.Id,
                    [Corner(-.5f, -.5f), Corner(.5f, -.5f), Corner(.5f, .5f), Corner(-.5f, .5f)]));
            }
        }
        void AddTexts()
        {
            if (textSampler == null) return;
            double Value(string name) => sceneDirectTextValues?.GetValueOrDefault(name, textSampler.Get(name, beat)) ?? textSampler.Get(name, beat);
            Renderer.MeasureAuthoringTexts(Current, time, projection, Value,
                (id, corners) => { if (corners.All(p => float.IsFinite(p.X + p.Y))) sceneDirectHits.Add(new(SceneDirectKind.Text, id, corners)); });
        }

        if (Current.Chart.ObjectName == "obj_custom_gimmick")
        {
            AddImages(item => item.LayerPriority < 10);
            AddTexts();
            AddImages(item => item.LayerPriority >= 10);
        }
        else
        {
            AddImages(_ => true);
            AddTexts();
        }

        SceneDirectHit? selected = null;
        if (selectedImageId != null && imageInspector)
            selected = sceneDirectHits.LastOrDefault(h => h.Kind == SceneDirectKind.Image && h.Id == selectedImageId);
        else if (selectedTextId != null && textInspector)
            selected = sceneDirectHits.LastOrDefault(h => h.Kind == SceneDirectKind.Text && h.Id == selectedTextId);

        SceneDirectHit? hovered = previewImage.Contains(mouseX, mouseY) ? SceneDirectPick(new(mouseX, mouseY)) : null;
        if (selected == null && hovered == null) return;

        Canvas.Clip(viewport);
        void Outline(SceneDirectHit hit, Color color, float width, bool handles)
        {
            var points = hit.Corners;
            for (int i = 0; i < 4; i++) Canvas.Line(points[i].X, points[i].Y, points[(i + 1) % 4].X, points[(i + 1) % 4].Y, width, color);
            if (handles) foreach (var point in points) Canvas.Fill(new(point.X - 3, point.Y - 3, 6, 6), color);
        }
        if (hovered != null && hovered != selected) Outline(hovered, soft, 1, false);
        if (selected != null)
        {
            Outline(selected, Theme.AccentSoft, 2, true);
            string label = (selected.Kind == SceneDirectKind.Image ? "IMAGE / " : "TEXT / ") + (selected.Id.Length == 0 ? L.Get("LEGACY") : selected.Id);
            Text(label, viewport.X + 8, viewport.Y + viewport.H - 21, 10, white, max: viewport.W - 16);
        }
        Canvas.Clip(null);
    }

    void BeginSceneDirectDrag(SceneDirectHit hit, Vector2 pointer)
    {
        if (editor == null) return;
        double beat = Current.Timeline.Bpm.Beat(transport.Position);
        float sx = Math.Max(.0001f, previewImage.W / 320f), sy = Math.Max(.0001f, previewImage.H / 180f);
        sceneDirectMoved = false; sceneDirectTextValues = null;

        if (hit.Kind == SceneDirectKind.Image)
        {
            SelectImageObject(hit.Id, canvas: false, keyBeat: beat);
            string issue = ImageTransformIssue();
            if (issue.Length > 0) { message = L.Get("Scene drag unavailable: ") + issue; return; }
            var pose = ReadImagePose(); sceneDirectImagePose = pose;
            sceneDirectDrag = new(editor, editor.Revision, hit.Kind, hit.Id, pointer, sx, sy, pose.X, pose.Y, pose);
        }
        else
        {
            SelectText(hit.Id, canvas: false, atBeat: beat);
            EnsureTexts(); if (textSampler == null) return;
            double x = textSampler.Get("textX", hit.Id, textAt), y = textSampler.Get("textY", hit.Id, textAt);
            sceneDirectDrag = new(editor, editor.Revision, hit.Kind, hit.Id, pointer, sx, sy, x, y, default);
        }
        Sdl.SDL_CaptureMouse(true);
    }

    void UpdateSceneDirectDrag(Vector2 pointer)
    {
        if (sceneDirectDrag is not { } drag) return;
        Vector2 pixels = pointer - drag.Pointer;
        double dx = pixels.X / drag.ScaleX, dy = pixels.Y / drag.ScaleY;
        sceneDirectMoved |= pixels.Length() > 2;
        if (drag.Kind == SceneDirectKind.Image)
            sceneDirectImagePose = drag.ImagePose with { X = drag.X + dx, Y = drag.Y + dy };
        else
        {
            sceneDirectTextValues = new(StringComparer.Ordinal)
            {
                [TextValueSampler.Name("textX", drag.Id)] = drag.X + dx,
                [TextValueSampler.Name("textY", drag.Id)] = drag.Y + dy
            };
        }
    }

    /// <summary>
    /// 双击只负责选择；选中后普通左键拖拽位置。这样不会把双击的第二下误写成一个零长度 Undo。
    /// 丢焦点/Esc 取消，松手时文档 Revision 有变化也整次丢弃。
    /// </summary>
    bool HandleSceneDirectGesture(Sdl.Event e)
    {
        if (sceneDirectDrag is { } drag)
        {
            if (e.WindowID != 0 && e.WindowID != Sdl.SDL_GetWindowID(host.Window)) return false;
            if (e.Type == 0x20F || e.Type == 0x300 && e.Scan == 41)
            { CancelSceneDirectGesture(); return true; }
            if (e.Type is 0x100 or Sdl.WindowCloseRequested) { CancelSceneDirectGesture(); return false; }
            if (e.Type == 0x400)
            { mouseX = e.X; mouseY = e.Y; UpdateSceneDirectDrag(new(e.X, e.Y)); return true; }
            if (e.Type == 0x402 && e.Button == 1)
            {
                mouseX = e.X; mouseY = e.Y; UpdateSceneDirectDrag(new(e.X, e.Y));
                bool commit = sceneDirectMoved && editor == drag.Owner && editor.Revision == drag.Revision;
                var imagePose = sceneDirectImagePose;
                Dictionary<string, double>? textValues = sceneDirectTextValues;
                sceneDirectDrag = null; sceneDirectTextValues = null; held = click = false; Sdl.SDL_CaptureMouse(false);
                if (commit)
                {
                    if (drag.Kind == SceneDirectKind.Image) ImageAction(() => CommitImagePose(imagePose, ImageChannels.Position));
                    else if (textValues != null) TextAction(() => CommitTextValues(new Dictionary<string, double>
                    {
                        ["textX"] = textValues[TextValueSampler.Name("textX", drag.Id)],
                        ["textY"] = textValues[TextValueSampler.Name("textY", drag.Id)]
                    }));
                }
                return true;
            }
            return e.Type is >= 0x300 and <= 0x403;
        }

        if (!editorMode || settings || UiOverlayVisible || Busy || imageCanvas || textCanvas || desktopPreview) return false;
        if (e.Type != 0x401 || e.Button != 1) return false;
        mouseX = e.X; mouseY = e.Y;
        if (!previewImage.Contains(mouseX, mouseY)) return false;
        var pointer = new Vector2(mouseX, mouseY);
        var hit = SceneDirectPick(pointer);

        if (e.Clicks >= 2)
        {
            if (hit == null) return false;
            // 第二下本身就进入拖拽：普通双击不移动时只完成选择，不产生 Undo；
            // 按住第二下继续移动则直接拖，不要求用户再点第三次。
            BeginSceneDirectDrag(hit, pointer);
            if (sceneDirectDrag != null)
                message = L.Get("Selected in GAME SCENE. Drag to move X/Y; Undo is one step.");
            click = held = false;
            return true;
        }

        bool selected = hit != null && (hit.Kind == SceneDirectKind.Image && imageInspector && hit.Id == selectedImageId
            || hit.Kind == SceneDirectKind.Text && textInspector && hit.Id == selectedTextId);
        if (!selected) return false;
        BeginSceneDirectDrag(hit!, pointer);
        return sceneDirectDrag != null;
    }

    void CancelSceneDirectGesture()
    {
        bool active = sceneDirectDrag != null;
        sceneDirectDrag = null; sceneDirectTextValues = null; sceneDirectMoved = false;
        if (active) { held = click = false; Sdl.SDL_CaptureMouse(false); }
    }
}
