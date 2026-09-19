using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

/// <summary>
/// image 的源列表与检视面板。面板上每个字段都要先过 Can 判定：PATH 模式只放位置，
/// 起点/终点模式还要求该动画组真的覆盖了这个通道，否则按钮置灰而不是写进去再报错。
/// </summary>
public sealed partial class Viewer
{
    /// <summary>
    /// 按像素宽度截断并补省略号。逐字符退的时候遇到代理对（surrogate pair）一次退两个 code unit，
    /// 绝不把一个 UTF-16 代理对劈开，否则会截出乱码字符。
    /// </summary>
    string ImageShortText(string value, float max, int size = 12)
    {
        if (fonts.Measure(value, size, true) <= max) return value;
        while (value.Length > 0 && fonts.Measure(value + "...", size, true) > max)
        {
            int count = value.Length > 1 && char.IsLowSurrogate(value[^1]) && char.IsHighSurrogate(value[^2]) ? 2 : 1;
            value = value[..^count];
        }
        return value + "...";
    }
    /// <summary>
    /// 左侧 image 源列表，只列 VSP 里声明过的 id。每行带首帧缩略图（按 Frames 取横向切片的第一格）。
    /// HIDE / LOCK 只影响编辑期的画布显示与可选中性，不写进文档，场景渲染与导出不受影响。
    /// </summary>
    void DrawImageSources(Rect r)
    {
        EnsureImageModel();
        if (EButton("+ IMAGE / DROP IMAGE", new(r.X, r.Y, r.W, 30), primary: true, enabled: !Busy)) ChooseImageImport();
        var items = Current.Images.Items.Where(i => declaredImageIds.Contains(i.Id)).ToArray();
        Text(items.Length + " IMAGE OBJECTS", r.X + 5, r.Y + 40, 12, muted);
        float listY = r.Y + 64, bottom = r.Y + r.H - 42; int visible = Math.Max(1, (int)((bottom - listY) / 61));
        imageListScroll = Math.Clamp(imageListScroll, 0, Math.Max(0, items.Length - visible));
        Canvas.Clip(new(r.X, listY, r.W, Math.Max(1, bottom - listY)));
        for (int index = imageListScroll; index < Math.Min(items.Length, imageListScroll + visible); index++)
        {
            var item = items[index]; float y = listY + (index - imageListScroll) * 61;
            bool selected = selectedImageId == item.Id;
            var box = new Rect(r.X, y, r.W, 58); Canvas.Fill(box, selected ? Mix(Theme.PanelRaised, Theme.Accent, .3f) : Theme.PanelAlt);
            Canvas.Border(box, selected ? soft : line);
            var texture = Renderer.AuthoringImageTexture(Current, item.Asset);
            if (texture != null)
            {
                float fit = Math.Min(36f / (item.Asset.Width / (float)item.Frames), 36f / item.Asset.Height);
                Canvas.Quad(texture, new(r.X + 6, y + 10, item.Asset.Width / (float)item.Frames * fit, item.Asset.Height * fit),
                    Color.White, new(0, 0, 1f / item.Frames, 1));
            }
            Text(ImageShortText(item.Id, r.W - 58), r.X + 50, y + 9, 12, white, true);
            if (EButton(hiddenImageItems.Contains(item.Id) ? "SHOW" : "HIDE", new(r.X + 50, y + 29, 53, 22), key: "image-visible:" + item.Id))
            { if (!hiddenImageItems.Add(item.Id)) hiddenImageItems.Remove(item.Id); }
            if (EButton(lockedImageItems.Contains(item.Id) ? "UNLOCK" : "LOCK", new(r.X + 109, y + 29, 65, 22), key: "image-lock:" + item.Id))
            { if (!lockedImageItems.Add(item.Id)) lockedImageItems.Remove(item.Id); }
            if (click && !Busy && !UiOverlayVisible && box.Contains(mouseX, mouseY))
            { SelectImageObject(item.Id); FocusImageTrack(item.Id, ImageEditBeat); click = false; }
        }
        Canvas.Clip(null);
        if (items.Length == 0) Text("Drop an image to begin.", r.X + 5, listY + 20, 12, muted, max: r.W - 10);
        Text("HIDE / LOCK affect editing only.", r.X + 5, bottom + 6, 10, muted, max: r.W - 10);
        Text("Scene / exports stay unchanged.", r.X + 5, bottom + 23, 10, muted, max: r.W - 10);
    }
    /// <summary>
    /// image 检视面板。显示值做过换算：scale 与 alpha 以百分比显示（存的是倍率 / 0~1），X/Y 与旋转是逻辑单位和度。
    /// 拖拽中优先显示 imageDragPose，松手前面板和画布看到的是同一份预览姿态。
    /// FIT IMAGE 按 288x144 塞进 320x180（四周各留 10%），LOCK RATIO 开启时改单轴 scale 会按比例带动另一轴。
    /// 所有行经 Row 做视口裁剪，rows 累加完才用来夹 inspectorScroll，行数随目标模式变化也不会滚出范围。
    /// </summary>
    void DrawImageInspector(Rect r)
    {
        EnsureImageModel();
        if (editor == null || selectedImageId == null) return;
        string id = selectedImageId;
        Text(ImageShortText(id, r.W - 70, 15), r.X, r.Y + 3, 15, white, true);
        if (EButton("^", new(r.X + r.W - 62, r.Y, 27, 24))) inspectorScroll = Math.Max(0, inspectorScroll - 1);
        if (EButton("v", new(r.X + r.W - 30, r.Y, 27, 24))) inspectorScroll++;
        float third = (r.W - 12) / 3;
        if (EButton("INITIAL", new(r.X, r.Y + 32, third, 27), active: imagePoseTarget == ImagePoseTarget.Initial)) SetImageTarget(ImagePoseTarget.Initial);
        if (EButton("KEY HERE", new(r.X + third + 6, r.Y + 32, third, 27), active: imagePoseTarget == ImagePoseTarget.Key)) SetImageTarget(ImagePoseTarget.Key);
        if (EButton("PATH", new(r.X + 2 * (third + 6), r.Y + 32, third, 27), active: imagePoseTarget == ImagePoseTarget.Path)) SetImageTarget(ImagePoseTarget.Path);
        var group = ActiveImageGroup; bool simple = group?.Editable == true;
        float half = (r.W - 6) / 2;
        if (EButton("START", new(r.X, r.Y + 66, half, 27), active: imagePoseTarget == ImagePoseTarget.Start, enabled: simple)) SetImageTarget(ImagePoseTarget.Start);
        if (EButton("END", new(r.X + half + 6, r.Y + 66, half, 27), active: imagePoseTarget == ImagePoseTarget.End, enabled: simple && group!.Duration > 0)) SetImageTarget(ImagePoseTarget.End);
        string target = imagePoseTarget == ImagePoseTarget.Path ? "OFFSET ALL X/Y EVENTS" : imagePoseTarget.ToString().ToUpperInvariant() + " @ " + ImageEditBeat.ToString("0.####");
        Text(target, r.X, r.Y + 102, 11, soft, true, r.W);
        if (EButton(ImagePresets[imagePreset] + " >", new(r.X, r.Y + 125, half, 27), key: "image-preset"))
            imagePreset = (imagePreset + 1) % ImagePresets.Length;
        if (EButton("+ ANIMATION", new(r.X + half + 6, r.Y + 125, half, 27), primary: true, enabled: !Busy)) AddImageMotion();
        var pose = imageObjectDrag != null ? imageDragPose : ReadImagePose();
        string issue = ImageTransformIssue();
        int rows = 0;
        float bodyY = r.Y + 161, bodyBottom = r.Y + r.H - 45;
        int visible = Math.Max(1, (int)((bodyBottom - bodyY) / 32));
        bool Row(out float y) { y = bodyY + (rows++ - inspectorScroll) * 32; return y >= bodyY && y + 28 <= bodyBottom; }
        bool Can(ImageChannels c) => issue.Length == 0 && !transport.Playing && (imagePoseTarget != ImagePoseTarget.Path || c == ImageChannels.Position) &&
            (imagePoseTarget is not (ImagePoseTarget.Start or ImagePoseTarget.End) || simple && (group!.Channels & c) != 0);
        void Field(string label, string value, Action<string> accept, bool enabled = true)
        {
            if (!Row(out float y)) return;
            InlineField("image-field:" + id + ":" + label, label, value, r.X, y, r.W, accept, enabled: enabled, labelSize: 12);
        }
        void Pair(string left, Action first, string right, Action second, bool leftOn = false, bool rightOn = false, bool enabled = true, bool? leftEnabled = null, bool? rightEnabled = null)
        {
            if (!Row(out float y)) return;
            if (EButton(left, new(r.X, y, half, 27), active: leftOn, enabled: leftEnabled ?? enabled, key: "image-pair:" + left)) ImageAction(first);
            if (EButton(right, new(r.X + half + 6, y, half, 27), active: rightOn, enabled: rightEnabled ?? enabled, key: "image-pair:" + right)) ImageAction(second);
        }
        if (group != null && simple && (imagePoseTarget is ImagePoseTarget.Start or ImagePoseTarget.End))
        {
            Field("Start / beat", group.Beat.ToString("0.######"), v =>
            { double start = VsmDocument.Number(v); editor.TimeImageGroup(group, start, group.End(Current.Timeline.Bpm) + start - group.Beat, group.Ease, Current.Timeline.Bpm); });
            if (group.Duration > 0)
            {
                Field("End / beat", group.End(Current.Timeline.Bpm).ToString("0.######"), v => editor.TimeImageGroup(group, group.Beat, VsmDocument.Number(v), group.Ease, Current.Timeline.Bpm));
                Field("Easing", group.Ease, v => editor.TimeImageGroup(group, group.Beat, group.End(Current.Timeline.Bpm), Easings.Normalize(v), Current.Timeline.Bpm));
            }
        }
        Field("X", pose.X.ToString("0.###"), v => CommitImagePose(ReadImagePose() with { X = VsmDocument.Number(v) }, ImageChannels.Position), Can(ImageChannels.Position));
        Field("Y", pose.Y.ToString("0.###"), v => CommitImagePose(ReadImagePose() with { Y = VsmDocument.Number(v) }, ImageChannels.Position), Can(ImageChannels.Position));
        Field("Scale X %", (pose.ScaleX * 100).ToString("0.###"), v =>
        {
            var p = ReadImagePose(); double scale = VsmDocument.Number(v) / 100;
            CommitImagePose(p with { ScaleX = scale, ScaleY = imageAspectLock ? Math.Abs(p.ScaleX) > 1e-12 ? p.ScaleY * scale / p.ScaleX : scale : p.ScaleY }, ImageChannels.Scale);
        }, Can(ImageChannels.Scale));
        Field("Scale Y %", (pose.ScaleY * 100).ToString("0.###"), v =>
        {
            var p = ReadImagePose(); double scale = VsmDocument.Number(v) / 100;
            CommitImagePose(p with { ScaleY = scale, ScaleX = imageAspectLock ? Math.Abs(p.ScaleY) > 1e-12 ? p.ScaleX * scale / p.ScaleY : scale : p.ScaleX }, ImageChannels.Scale);
        }, Can(ImageChannels.Scale));
        Field("Rotation", pose.Rotation.ToString("0.###"), v => CommitImagePose(ReadImagePose() with { Rotation = VsmDocument.Number(v) }, ImageChannels.Rotation), Can(ImageChannels.Rotation));
        Field("Opacity %", (pose.Alpha * 100).ToString("0.###"), v => CommitImagePose(ReadImagePose() with { Alpha = VsmDocument.Number(v) / 100 }, ImageChannels.Alpha), Can(ImageChannels.Alpha));
        Pair("CENTER X", () => CommitImagePose(ReadImagePose() with { X = 160 }, ImageChannels.Position), "CENTER Y", () => CommitImagePose(ReadImagePose() with { Y = 90 }, ImageChannels.Position), enabled: Can(ImageChannels.Position));
        Pair("FIT IMAGE", () =>
        {
            if (ActiveImageItem is not { } item) return; double scale = Math.Min(288 / item.Width, 144 / item.Height);
            CommitImagePose(ReadImagePose() with { ScaleX = scale, ScaleY = scale }, ImageChannels.Scale);
        }, "FRAME VIEW", FrameImageObject, leftEnabled: Can(ImageChannels.Scale));
        Pair("LOCK RATIO", () => imageAspectLock = !imageAspectLock, "PATH LINES", () => imagePathVisible = !imagePathVisible, imageAspectLock, imagePathVisible);
        Pair("LINK JOINTS", () => imageLinkNeighbors = !imageLinkNeighbors, "SOLO", () => imageSolo = !imageSolo, imageLinkNeighbors, imageSolo);
        Pair("REPLACE IMAGE", ChooseImageReplacement, "RAW TRACKS", () => { expandedImageTracks.Add(id); layoutRevision = -1; imageInspector = false; FocusImageTrack(id, ImageEditBeat); });
        Pair("CANVAS", () => { imageCanvas = true; desktopPreview = false; }, "SCENE", () => imageCanvas = false, imageCanvas, !imageCanvas);
        Pair("CONTINUE", () => AddImageMotion(true), "LOOP MOTION", () =>
        {
            if (ActiveImageGroup is not { } g || g.Duration <= 0) return;
            loopIn = g.Beat; loopOut = g.End(Current.Timeline.Bpm); loopEnabled = true;
            transport.Seek(Current.Timeline.Bpm.Time(loopIn)); transport.SetPlaying(true);
        }, enabled: simple && group!.Duration > 0);
        Pair("RECORD POSE", () => CommitImagePose(ReadImagePose(), ImageChannels.All), "RESET VIEW", () => { imageViewZoom = 1; imageViewCenter = new(160, 90); },
            leftEnabled: issue.Length == 0 && !transport.Playing && (imagePoseTarget is ImagePoseTarget.Initial or ImagePoseTarget.Key));
        Pair("DUP MOTION", () =>
        {
            if (group == null) return; double beat = InsertionBeat;
            if (Math.Abs(beat - group.Beat) < 1e-9) beat = group.End(Current.Timeline.Bpm) + (group.Duration == 0 ? 1 : 0);
            Guid added = editor.DuplicateImageGroup(group, beat, Current.Timeline.Bpm); SelectImageObject(id, true, added);
        }, "DELETE MOTION", () =>
        {
            if (group == null) return; editor.DeleteImageGroup(group); selectedImageGroup = selectedClip = null; imagePoseTarget = ImagePoseTarget.Initial; layoutRevision = -1;
        }, enabled: simple);
        if (ActiveImageItem is { } resource && Row(out float infoY))
            Text($"{resource.Asset.Width}x{resource.Asset.Height} / {resource.Frames} frame(s)", r.X, infoY + 7, 12, muted, max: r.W);
        inspectorScroll = Math.Clamp(inspectorScroll, 0, Math.Max(0, rows - visible));
        string footer = imageObjectError.Length > 0 ? imageObjectError : issue.Length > 0 ? issue :
            transport.Playing ? "Playing: pause to edit." : "Drag: move / corners: scale / handle: rotate";
        Text(footer, r.X, r.Y + r.H - 36, 11, imageObjectError.Length > 0 ? soft : muted, max: r.W);
        Text(!transport.Playing && pose.Alpha <= 0 ? "0% opacity: editing ghost only." : "Alt-click path point / arrows: 1px (Shift: 10px)", r.X, r.Y + r.H - 18, 10, muted, max: r.W);
    }
}
