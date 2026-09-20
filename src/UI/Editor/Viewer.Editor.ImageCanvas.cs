using System.Numerics;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// 图像画布：在 320x180 逻辑空间里直接拖动 image 对象，以及移动/缩放/旋转手势的完整生命周期。
/// 画布只画 image 自身，不套 proxy、不过 screen FX，所以这里看到的不等于最终成片。
/// </summary>
public sealed partial class Viewer
{
    /// <summary>
    /// 一次拖拽的快照。Owner 与 Revision 在按下时就记下，松手提交前会比对：
    /// 拖拽期间文档被换掉或改过，这次拖拽就整个丢弃，不往新文档上写。
    /// Map 也一并快照，拖到一半改变缩放不会让坐标换算跳变。
    /// </summary>
    sealed record ImageObjectDrag(EditorDocument Owner, long Revision, ImageEditPose Original, Vector2 Start, Vector2 Handle,
        int Kind, ImageCanvasMap Map, double Width, double Height, float StartAngle, ImageChannels Channels);
    ImageObjectDrag? imageObjectDrag;
    ImageEditPose imageDragPose;
    bool imageDragMoved;
    float imageRotationLastAngle, imageRotationAccumulated;
    ImageCanvasMap imageCanvasMap;
    readonly List<(CustomImages.Item Item, CustomImages.Pose Pose)> imageCanvasItems = [];
    readonly List<(Vector2 Screen, Vector2 Handle, int Kind)> imageHandleHits = [];
    readonly List<(Vector2 Screen, ImageMotionGroup Group, bool End)> imagePathHits = [];

    /// <summary>
    /// 画一帧图像画布。time 是秒，画布里所有坐标是 320x180 逻辑单位，ImageCanvasMap 负责逻辑 ↔ UI 像素的来回换算。
    /// 每帧重建 imageCanvasItems / imageHandleHits / imagePathHits，末尾才处理点击，保证命中判定用的是本帧的布局。
    /// </summary>
    void DrawImageCanvas(Rect viewport, double time)
    {
        EnsureImageModel();
        Canvas.Clip(viewport); Canvas.Fill(viewport, Theme.Background);
        imageCanvasMap = ImageCanvasMap.Create(viewport.X, viewport.Y, viewport.W, viewport.H, imageViewCenter, imageViewZoom);
        var map = imageCanvasMap;
        Vector2 a = map.ToView(Vector2.Zero), b = map.ToView(new(320, 180));
        Canvas.Fill(new(a.X, a.Y, b.X - a.X, b.Y - a.Y), Theme.PanelAlt);
        for (int n = 0; n <= 320; n += 20)
        {
            var p = map.ToView(new(n, 0)); var q = map.ToView(new(n, 180));
            Canvas.Line(p.X, p.Y, q.X, q.Y, 1, n == 160 ? Theme.GridMajor : Theme.Grid);
        }
        for (int n = 0; n <= 180; n += 20)
        {
            var p = map.ToView(new(0, n)); var q = map.ToView(new(320, n));
            Canvas.Line(p.X, p.Y, q.X, q.Y, 1, Theme.Grid);
        }
        Canvas.Border(new(a.X, a.Y, b.X - a.X, b.Y - a.Y), muted);
        var overrides = new Dictionary<string, double>(StringComparer.Ordinal);
        double beat = Current.Timeline.Bpm.Beat(time);
        // 用 overrides 直接顶替选中对象的属性来预览最新姿态，不为了每次鼠标移动重建整首曲子的渲染。
        if (selectedImageId != null && imageSampler != null)
        {
            ImageEditPose pose = imageObjectDrag != null ? imageDragPose : !transport.Playing && Math.Abs(beat - ImageEditBeat) < 1e-8
                ? ReadImagePose() : imageSampler.Pose(selectedImageId, beat);
            foreach (string p in ImageEditPose.Properties(ImageChannels.All)) overrides[p + "_" + selectedImageId] = pose.Get(p);
            if (!transport.Playing && pose.Alpha <= 0) overrides["imgalp_" + selectedImageId] = .25; // 仅编辑期的幽灵显示；绝不写回 VSM，也不进导出。
        }
        imageCanvasItems.Clear();
        foreach (var (item, pose) in Current.Images.At(Current.Timeline, time, overrides))
        {
            if (hiddenImageItems.Contains(item.Id) || imageSolo && selectedImageId != item.Id) continue;
            if (!float.IsFinite(pose.Matrix.M11 + pose.Matrix.M12 + pose.Matrix.M21 + pose.Matrix.M22 + pose.Matrix.M31 + pose.Matrix.M32)) continue;
            var texture = Renderer.AuthoringImageTexture(Current, item.Asset); if (texture == null) continue;
            Vector2 Corner(float x, float y) => map.ToView(Vector2.Transform(new(x, y), pose.Matrix));
            Canvas.Polygon(texture, Corner(-.5f, -.5f), Corner(.5f, -.5f), Corner(-.5f, .5f), Corner(.5f, .5f),
                Color.Hex(pose.Rgb).Alpha(pose.Alpha), new(pose.Frame / (float)item.Frames, 0, 1f / item.Frames, 1));
            imageCanvasItems.Add((item, pose));
        }
        imageHandleHits.Clear(); imagePathHits.Clear();
        if (imagePathVisible && selectedImageId != null) DrawImagePaths(map);
        var selected = imageCanvasItems.FirstOrDefault(p => p.Item.Id == selectedImageId);
        if (selected.Item != null)
        {
            var matrix = selected.Pose.Matrix;
            Vector2 Corner(float x, float y) => map.ToView(Vector2.Transform(new(x, y), matrix));
            var corners = new[] { Corner(-.5f, -.5f), Corner(.5f, -.5f), Corner(.5f, .5f), Corner(-.5f, .5f) };
            for (int i = 0; i < 4; i++) ImageLine(corners[i], corners[(i + 1) % 4], 2, white);
            Vector2 center = Corner(0, 0); ImageLine(center - new Vector2(5, 0), center + new Vector2(5, 0), 1.5f, white);
            ImageLine(center - new Vector2(0, 5), center + new Vector2(0, 5), 1.5f, white);
            string issue = ImageTransformIssue();
            if (issue.Length == 0)
            {
                ImageChannels handles = imagePoseTarget == ImagePoseTarget.Path ? ImageChannels.Position :
                    imagePoseTarget is ImagePoseTarget.Start or ImagePoseTarget.End ? ActiveImageGroup?.Channels ?? ImageChannels.None : ImageChannels.All;
                if (handles.HasFlag(ImageChannels.Scale))
                {
                    foreach (var handle in new Vector2[] { new Vector2(-.5f, -.5f), new(.5f, -.5f), new(.5f, .5f), new(-.5f, .5f),
                        new(-.5f, 0), new(.5f, 0), new(0, -.5f), new(0, .5f) })
                    {
                        Vector2 p = Corner(handle.X, handle.Y);
                        Canvas.Fill(new(p.X - 4, p.Y - 4, 8, 8), white); Canvas.Border(new(p.X - 4, p.Y - 4, 8, 8), red);
                        imageHandleHits.Add((p, handle, 1));
                    }
                }
                if (handles.HasFlag(ImageChannels.Rotation))
                {
                    Vector2 top = Corner(0, -.5f), direction = top - center;
                    if (direction.LengthSquared() < .01f) direction = new(0, -1); else direction = Vector2.Normalize(direction);
                    var rotation = top + direction * 26; ImageLine(top, rotation, 1.5f, white);
                    ImageDiamond(rotation, 6, Theme.AccentSoft); imageHandleHits.Add((rotation, Vector2.Zero, 2));
                }
            }
            Text(selected.Item.Id, viewport.X + 10, viewport.Y + 34, 13, white, max: viewport.W - 20);
        }
        Canvas.Fill(new(viewport.X, viewport.Y, viewport.W, 27), panel);
        Text(L.Get("IMAGE CANVAS  /  LOCAL 320 x 180  /  NO PROXY OR SCREEN FX"), viewport.X + 10, viewport.Y + 7, 11, white, max: viewport.W - 20);
        Canvas.Clip(null);
        HandleImageCanvasClick(viewport);
    }
    void ImageLine(Vector2 a, Vector2 b, float width, Color color) => Canvas.Line(a.X, a.Y, b.X, b.Y, width, color);
    void ImageDiamond(Vector2 p, float size, Color color)
    {
        ImageLine(p - new Vector2(size, 0), p - new Vector2(0, size), 2, color);
        ImageLine(p - new Vector2(0, size), p + new Vector2(size, 0), 2, color);
        ImageLine(p + new Vector2(size, 0), p + new Vector2(0, size), 2, color);
        ImageLine(p + new Vector2(0, size), p - new Vector2(size, 0), 2, color);
    }
    /// <summary>
    /// 取动画组两端的姿态：先按起止拍向 sampler 采样，再用 clip 里写死的常量端点覆盖。
    /// 只覆盖 Fixed（字面量）端点，含表达式的端点保持采样值，免得把算出来的东西当成用户写死的。
    /// </summary>
    ImageEditPose GroupEndpoint(ImageMotionGroup group, bool end)
    {
        var initial = imageSampler!.Pose(group.ImageId, end ? group.End(Current.Timeline.Bpm) : group.Beat);
        var values = ImageEditPose.Properties(ImageChannels.All).ToDictionary(p => p, initial.Get);
        foreach (var c in group.Clips)
        {
            CustomImages.TryMod(c.Name, out string p, out _);
            if (ImageEditPose.Channel(p) != ImageChannels.None && ImageObjectModel.Fixed(end ? c.To : c.From))
                values[p] = VsmDocument.Number(end ? c.To : c.From);
        }
        return ImageEditPose.Read(p => values[p]);
    }
    /// <summary>
    /// 画位移动画的轨迹线。只画 Editable、有时长且带 Position 通道的组，并且最多 512 条——
    /// 大量重叠的轨迹既看不清也拖垮帧率。
    /// </summary>
    void DrawImagePaths(ImageCanvasMap map)
    {
        if (selectedImageId == null || imageSampler == null) return;
        foreach (var group in ImageGroups(selectedImageId).Where(g => g.Editable && g.Duration > 0 && g.Channels.HasFlag(ImageChannels.Position)).Take(512))
        {
            var startPose = GroupEndpoint(group, false); var endPose = GroupEndpoint(group, true);
            if (imageObjectDrag is { } pathDrag && imagePoseTarget == ImagePoseTarget.Path)
            {
                double dx = imageDragPose.X - pathDrag.Original.X, dy = imageDragPose.Y - pathDrag.Original.Y;
                startPose = startPose with { X = startPose.X + dx, Y = startPose.Y + dy };
                endPose = endPose with { X = endPose.X + dx, Y = endPose.Y + dy };
            }
            if (imageObjectDrag != null && group.Id == ActiveImageGroup?.Id)
            {
                if (imagePoseTarget == ImagePoseTarget.Start) startPose = imageDragPose;
                if (imagePoseTarget == ImagePoseTarget.End) endPose = imageDragPose;
            }
            Color color = group.Id == ActiveImageGroup?.Id ? Theme.AccentSoft : Color.Hex(0x87C9E8);
            var start = map.ToView(new((float)startPose.X, (float)startPose.Y)); var finish = map.ToView(new((float)endPose.X, (float)endPose.Y));
            ImageLine(start, finish, 1.5f, color);
            // 圆点表示这条直线段上的缓动分布，不代表存在一条贝塞尔曲线——轨迹本身始终是直线。
            for (int i = 1; i < 8; i++)
            {
                double t = Easings.Eval(group.Ease, i / 8.0);
                var p = map.ToView(new((float)(startPose.X + (endPose.X - startPose.X) * t), (float)(startPose.Y + (endPose.Y - startPose.Y) * t)));
                Canvas.Fill(new(p.X - 2, p.Y - 2, 4, 4), color);
            }
            ImageDiamond(start, 5, color); ImageDiamond(finish, 6, color);
            imagePathHits.Add((start, group, false)); imagePathHits.Add((finish, group, true));
        }
    }
    /// <summary>
    /// 画布点击派发，优先级固定为：变换手柄 → （按住 Alt 时）轨迹端点 → 画面上的 image。
    /// 命中列表都倒序遍历，取绘制顺序最靠上的那个。顶栏 27px 属于标题条不参与命中，locked 的 image 直接跳过。
    /// </summary>
    void HandleImageCanvasClick(Rect viewport)
    {
        if (!click || !viewport.Contains(mouseX, mouseY) || mouseY < viewport.Y + 27 || Busy || UiOverlayVisible || editDrag != null || ImageGestureActive) return;
        var screen = new Vector2(mouseX, mouseY); var world = imageCanvasMap.ToWorld(screen);
        if (selectedImageId != null && imageInspector && ImageTransformIssue().Length == 0)
        {
            foreach (var hit in imageHandleHits.AsEnumerable().Reverse())
                if (Vector2.DistanceSquared(screen, hit.Screen) <= 100)
                { StartImageGesture(hit.Kind, hit.Handle, world); click = false; return; }
        }
        // 按住 Alt（0x0300 = 左右 Alt）时轨迹节点优先于压在上面的图；不按 Alt 时保持“点哪拖哪”的直觉。
        // Alt 同时还负责在重叠的图之间循环切换选中。
        bool alt = (Sdl.SDL_GetModState() & 0x0300) != 0;
        if (alt)
        {
            foreach (var hit in imagePathHits.AsEnumerable().Reverse())
                if (Vector2.DistanceSquared(screen, hit.Screen) <= 121)
                {
                    SelectImageObject(hit.Group.ImageId, true, hit.Group.Id); SetImageTarget(hit.End ? ImagePoseTarget.End : ImagePoseTarget.Start);
                    StartImageGesture(0, Vector2.Zero, world); click = false; return;
                }
        }
        var picks = imageCanvasItems.Where(x => !lockedImageItems.Contains(x.Item.Id) && ImageCanvasMap.Hit(x.Pose.Matrix, world)).Reverse().ToArray();
        if (picks.Length > 0)
        {
            var item = picks[0].Item;
            if (alt && picks.Length > 1)
            {
                int index = Array.FindIndex(picks, p => p.Item.Id == selectedImageId);
                item = picks[(index + 1 + picks.Length) % picks.Length].Item;
            }
            if (selectedImageId != item.Id || !imageInspector) { SelectImageObject(item.Id); click = false; return; }
            StartImageGesture(0, Vector2.Zero, world);
        }
        click = false;
    }
    /// <summary>
    /// 开始一次手势。kind：0 移动、1 缩放、2 旋转。播放中一律不开始编辑。
    /// 播放头不在 ImageEditBeat 上时先 Seek 过去并提示再拖一次——画面上显示的姿态必须和将要写入的那一拍一致，
    /// 不能在别的时刻拖动却把结果写到编辑拍上。
    /// </summary>
    void StartImageGesture(int kind, Vector2 handle, Vector2 pointer)
    {
        if (editor == null || ActiveImageItem is not { } item || transport.Playing) { transport.SetPlaying(false); return; }
        string issue = ImageTransformIssue(); if (issue.Length > 0) { imageObjectError = issue; return; }
        if (Math.Abs(Current.Timeline.Bpm.Beat(transport.Position) - ImageEditBeat) > 1e-8)
        {
            transport.Seek(Current.Timeline.Bpm.Time(ImageEditBeat));
            message = L.Get("Positioned at ") + imagePoseTarget + L.Get(". Drag again to edit this pose."); return;
        }
        ImageChannels channel = kind == 0 ? ImageChannels.Position : kind == 1 ? ImageChannels.Scale : ImageChannels.Rotation;
        if (imagePoseTarget is ImagePoseTarget.Start or ImagePoseTarget.End && ActiveImageGroup is { } group &&
            (!group.Editable || (group.Channels & channel) == 0))
        { imageObjectError = L.Get("Select INITIAL / KEY, or the animation for this property."); return; }
        var pose = ReadImagePose(); var offset = pointer - new Vector2((float)pose.X, (float)pose.Y);
        imageDragPose = pose; imageDragMoved = false;
        imageRotationLastAngle = MathF.Atan2(offset.Y, offset.X); imageRotationAccumulated = 0;
        imageObjectDrag = new(editor, editor.Revision, pose, pointer, handle, kind, imageCanvasMap, item.Width, item.Height,
            MathF.Atan2(offset.Y, offset.X), channel);
        Sdl.SDL_CaptureMouse(true); held = true; click = false; hoveredEditTrack = null; trackHelpWHeld = false;
    }
    /// <summary>
    /// 手势过程中更新预览姿态，不落盘。位移阈值 2 是 UI 像素（先乘 Map.Scale 换算），所以缩放再小的视图也不会误判成拖动。
    /// 旋转按 atan2(sin, cos) 逐帧累加增量而不是直接取角度差，越过 ±180° 时才不会突然反向；按住 Shift（掩码 3）吸附到 15°。
    /// 缩放时 Shift 与 imageAspectLock 是异或关系：锁定开着时按 Shift 反而自由缩放。
    /// </summary>
    void UpdateImageGesture(float x, float y)
    {
        if (imageObjectDrag is not { } d) return;
        var world = d.Map.ToWorld(new(x, y)); var delta = world - d.Start;
        imageDragMoved |= delta.Length() * d.Map.Scale > 2;
        if (d.Kind == 0) imageDragPose = d.Original with { X = d.Original.X + delta.X, Y = d.Original.Y + delta.Y };
        else if (d.Kind == 1)
        {
            Vector2 exactStart = Vector2.Transform(d.Handle, d.Original.Matrix(d.Width, d.Height));
            imageDragPose = ImageCanvasMap.Resize(d.Original, d.Width, d.Height, exactStart + delta, d.Handle,
                imageAspectLock != ((Sdl.SDL_GetModState() & 3) != 0));
        }
        else
        {
            var v = world - new Vector2((float)d.Original.X, (float)d.Original.Y);
            float currentAngle = MathF.Atan2(v.Y, v.X), deltaAngle = currentAngle - imageRotationLastAngle;
            imageRotationAccumulated += MathF.Atan2(MathF.Sin(deltaAngle), MathF.Cos(deltaAngle));
            imageRotationLastAngle = currentAngle;
            double angle = d.Original.Rotation + imageRotationAccumulated * 180 / Math.PI;
            if ((Sdl.SDL_GetModState() & 3) != 0) angle = Math.Round(angle / 15) * 15;
            imageDragPose = d.Original with { Rotation = angle };
        }
    }
    /// <summary>
    /// 手势期间独占事件。丢焦点（0x20F）或 Esc（scancode 41）取消，退出类事件也取消但继续往下传好让程序正常关闭。
    /// 松手时只有真的拖动过、且文档与 Revision 都没变，才提交一次编辑。
    /// </summary>
    bool HandleActiveImageGesture(Sdl.Event e)
    {
        if (!ImageGestureActive) return false;
        if (e.WindowID != 0 && e.WindowID != Sdl.SDL_GetWindowID(host.Window)) return false;
        if (e.Type == 0x20F || e.Type == 0x300 && e.Scan == 41) { CancelImageGesture(); return true; }
        if (e.Type is 0x100 or Sdl.WindowCloseRequested) { CancelImageGesture(); return false; }
        if (e.Type == 0x400) { mouseX = e.X; mouseY = e.Y; UpdateImageGesture(e.X, e.Y); return true; }
        if (e.Type == 0x402 && e.Button == 1)
        {
            mouseX = e.X; mouseY = e.Y; UpdateImageGesture(e.X, e.Y);
            var drag = imageObjectDrag; imageObjectDrag = null;
            if (drag != null && imageDragMoved && editor == drag.Owner && editor.Revision == drag.Revision)
                ImageAction(() => CommitImagePose(imageDragPose, drag.Channels));
            FinishImageGroupDrag(); held = click = false; Sdl.SDL_CaptureMouse(false); return true;
        }
        return e.Type is >= 0x300 and <= 0x403; // 捕获手势期间吞掉全部键鼠事件：背后不能再保存、拖播放头或编辑。
    }
    /// <summary>取消手势并释放鼠标捕获。只有确实捕获过才调 SDL_CaptureMouse(false)，避免多余的解捕获调用。</summary>
    void CancelImageGesture()
    {
        bool capture = ImageGestureActive; imageObjectDrag = null; imageGroupDrag = null; held = click = false;
        if (capture) Sdl.SDL_CaptureMouse(false);
    }
    /// <summary>
    /// 画布上的键鼠快捷键。Ctrl/Cmd（0x0CC0）+ 滚轮以光标处为锚点缩放，换算保证指针下的那个逻辑坐标原地不动。
    /// 方向键（scancode 79/80/81/82 = 右/左/下/上）每次挪 1 个逻辑单位，按住 Shift 变 10；Y 轴向下为正。
    /// </summary>
    bool HandleImageObjectKeys(Sdl.Event e)
    {
        if (!imageCanvas || selectedImageId == null || !imageInspector || UiOverlayVisible || Busy || !previewFrame.Contains(mouseX, mouseY)) return false;
        if (e.Type == 0x403 && (Sdl.SDL_GetModState() & 0x0CC0) != 0)
        {
            if (imageCanvasMap.Scale <= 0) return true;
            Vector2 pointer = imageCanvasMap.ToWorld(new(mouseX, mouseY));
            float old = imageViewZoom; imageViewZoom = Math.Clamp(imageViewZoom * MathF.Pow(1.12f, e.WheelY), .05f, 8);
            imageViewCenter = pointer - (pointer - imageViewCenter) * old / imageViewZoom; return true;
        }
        if (e.Type != 0x300 || (e.Modifiers & 0x0CC0) != 0 || e.Scan is < 79 or > 82) return false;
        transport.SetPlaying(false); var p = ReadImagePose(); double amount = (e.Modifiers & 3) != 0 ? 10 : 1;
        ImageAction(() => CommitImagePose(e.Scan switch { 79 => p with { X = p.X + amount }, 80 => p with { X = p.X - amount },
            81 => p with { Y = p.Y + amount }, _ => p with { Y = p.Y - amount } }, ImageChannels.Position));
        return true;
    }
}
