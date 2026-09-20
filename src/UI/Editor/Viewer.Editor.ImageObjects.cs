using System.Numerics;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

/// <summary>
/// image 对象的编辑模型：选中、编辑目标（INITIAL / KEY / PATH / 动画起点终点）、姿态读写与动画增删。
/// 直接操作只在“可直接编辑”时提供，否则一律退回裸事件编辑，判定见 ImageTransformIssue。
/// </summary>
public sealed partial class Viewer
{
    string? selectedImageId;
    Guid? selectedImageGroup;
    bool imageInspector, imageCanvas, imageSources, imageAspectLock = true, imagePathVisible = true, imageSolo, imageLinkNeighbors = true;
    ImagePoseTarget imagePoseTarget = ImagePoseTarget.Initial;
    double imageKeyBeat;
    int imageListScroll, imagePreset;
    readonly HashSet<string> expandedImageTracks = new(StringComparer.Ordinal);
    readonly HashSet<string> hiddenImageItems = new(StringComparer.Ordinal);
    readonly HashSet<string> lockedImageItems = new(StringComparer.Ordinal);
    long imageModelRevision = -1;
    EditorDocument? imageModelOwner;
    BpmMap? imageModelMap;
    ImageValueSampler? imageSampler;
    readonly Dictionary<string, ImageMotionGroup[]> imageGroups = new(StringComparer.Ordinal);
    HashSet<string> declaredImageIds = new(StringComparer.Ordinal);
    Vector2 imageViewCenter = new(160, 90);
    float imageViewZoom = 1;
    string imageObjectError = "";
    static readonly string[] ImagePresets = ["MOVE", "SCALE", "ROTATE", "FADE IN", "FADE OUT", "POP IN"];
    /// <summary>画布上是否有拖拽正在进行（图像、动画组或文本任一）。为真时禁止改选中项，免得拖到一半换了对象。</summary>
    bool ImageGestureActive => imageObjectDrag != null || imageGroupDrag != null || textDrag != null;

    /// <summary>换谱面时清空 image 编辑状态。先取消手势，否则鼠标捕获会留到下一个谱面。</summary>
    void ResetImageObjects()
    {
        CancelImageGesture(); selectedImageId = null; selectedImageGroup = null; imageInspector = imageCanvas = imageSources = false;
        imageModelOwner = null; imageModelRevision = -1; imageSampler = null; imageGroups.Clear();
        expandedImageTracks.Clear(); hiddenImageItems.Clear(); lockedImageItems.Clear(); imageListScroll = 0;
        imageViewCenter = new(160, 90); imageViewZoom = 1; imageObjectError = "";
    }
    /// <summary>
    /// 按需重建采样器与分组缓存。三个条件都要比：文档实例、Revision、以及 BpmMap 的引用相等——
    /// 只换了 BPM 时 Revision 可能没变，但采样器里绑的拍↔秒换算已经过时。
    /// VSP 里不再声明的 id 会顺手取消选中，避免检视面板停在一个已经不存在的对象上。
    /// </summary>
    void EnsureImageModel()
    {
        if (editor == null) return;
        if (imageModelOwner == editor && imageModelRevision == editor.Revision && ReferenceEquals(imageModelMap, Current.Timeline.Bpm)) return;
        imageModelOwner = editor; imageModelRevision = editor.Revision; imageModelMap = Current.Timeline.Bpm;
        imageSampler = new(editor.Vsm, Current.Timeline.Bpm);
        declaredImageIds = editor.Images.Ids(); imageGroups.Clear();
        if (selectedImageId != null && !declaredImageIds.Contains(selectedImageId))
        { selectedImageId = null; selectedImageGroup = null; imageInspector = false; }
    }
    /// <summary>取某个 image 的动画分组，按 id 记忆化；缓存由 EnsureImageModel 统一失效，这里不再自行判断。</summary>
    ImageMotionGroup[] ImageGroups(string id)
    {
        EnsureImageModel();
        if (editor == null) return [];
        if (!imageGroups.TryGetValue(id, out var groups)) imageGroups[id] = groups = ImageObjectModel.Groups(editor.Vsm, id);
        return groups;
    }
    /// <summary>当前选中的动画组。用 selectedImageGroup 去匹配组内任一 clip 的 Id，所以选中裸事件也能落回它所属的组。</summary>
    ImageMotionGroup? ActiveImageGroup => selectedImageId == null || selectedImageGroup == null ? null :
        ImageGroups(selectedImageId).FirstOrDefault(g => g.Clips.Any(c => c.Id == selectedImageGroup));
    /// <summary>当前选中的 image 资源项（尺寸、帧数等来自 VSP 声明）。</summary>
    CustomImages.Item? ActiveImageItem => Current.Images.Items.FirstOrDefault(i => i.Id == selectedImageId);
    /// <summary>
    /// 当前正在编辑的那一拍，由编辑目标决定：INITIAL 取该 image 最早的姿态事件拍，KEY 取插入点，
    /// 起点/终点取动画组两端；其余情况退回播放头所在拍。写入和画布预览都以它为准。
    /// </summary>
    double ImageEditBeat
    {
        get
        {
            if (editor == null || selectedImageId == null) return Current.Timeline.Bpm.Beat(transport.Position);
            var group = ActiveImageGroup;
            return imagePoseTarget switch
            {
                ImagePoseTarget.Initial => ImageObjectModel.InitialBeat(editor.Vsm, selectedImageId),
                ImagePoseTarget.Key => imageKeyBeat,
                ImagePoseTarget.Start when group != null => group.Beat,
                ImagePoseTarget.End when group != null => group.End(Current.Timeline.Bpm),
                _ => Current.Timeline.Bpm.Beat(transport.Position)
            };
        }
    }
    /// <summary>
    /// 读取当前编辑拍上的姿态。编辑动画起点/终点时，采样值会被该组 clip 的 From/To 原值覆盖，
    /// 这样面板显示的就是源文件里写的数，而不是缓动算出来的近似值。
    /// </summary>
    ImageEditPose ReadImagePose()
    {
        EnsureImageModel();
        if (imageSampler == null || selectedImageId == null) return new(160, 90, 1, 1, 0, 1);
        var pose = imageSampler.Pose(selectedImageId, ImageEditBeat);
        var group = ActiveImageGroup;
        if (group?.Editable == true && imagePoseTarget is ImagePoseTarget.Start or ImagePoseTarget.End)
        {
            var values = ImageEditPose.Properties(ImageChannels.All).ToDictionary(p => p, pose.Get);
            foreach (var c in group.Clips)
            {
                CustomImages.TryMod(c.Name, out string p, out _);
                values[p] = VsmDocument.Number(imagePoseTarget == ImagePoseTarget.End ? c.To : c.From);
            }
            pose = ImageEditPose.Read(p => values[p]);
        }
        return pose;
    }
    /// <summary>
    /// 从列表或画布选中一个 image。手势进行中直接返回，未在 VSP 声明的 id 也不受理。
    /// 会关掉文本侧的面板（共用同一块区域），停播并把播放头对到 ImageEditBeat；换了对象才重置视图缩放。
    /// </summary>
    void SelectImageObject(string id, bool canvas = true, Guid? groupId = null)
    {
        if (editor == null || ImageGestureActive) return;
        EnsureImageModel();
        if (!declaredImageIds.Contains(id)) return;
        bool changed = selectedImageId != id;
        textInspector = textSources = textCanvas = false;
        selectedImageId = id; selectedImageGroup = groupId; imageInspector = imageSources = true;
        if (canvas) { imageCanvas = true; desktopPreview = false; }
        selectedWindowEvent = -1; imageObjectError = "";
        if (groupId != null)
        {
            selectedClip = groupId;
            imagePoseTarget = ActiveImageGroup?.Duration > 0 ? ImagePoseTarget.End : ImagePoseTarget.Key;
            imageKeyBeat = ActiveImageGroup?.Beat ?? InsertionBeat;
        }
        else { selectedClip = null; imagePoseTarget = ImagePoseTarget.Initial; }
        if (changed) { inspectorScroll = 0; imageViewCenter = new(160, 90); imageViewZoom = 1; }
        transport.SetPlaying(false); transport.Seek(Current.Timeline.Bpm.Time(ImageEditBeat));
        layoutRevision = -1;
    }
    /// <summary>
    /// 在时间轴上选中一条裸事件时，同步把对应的 image 展示到检视面板。
    /// 与 SelectImageObject 不同：这里不改 selectedClip、不停播、不 Seek。
    /// </summary>
    void SelectImageFromClip(VsmDocument.Clip? clip)
    {
        textInspector = false;
        if (clip == null || !CustomImages.TryMod(clip.Name, out _, out var id)) { imageInspector = false; return; }
        EnsureImageModel();
        if (!declaredImageIds.Contains(id)) { imageInspector = false; return; }
        // 选中裸事件只是顺带把同一张 image 显示出来；不得替换这条事件自身的身份，也不得移动播放头。
        selectedImageId = id; selectedImageGroup = ImageGroups(id).FirstOrDefault(g => g.Clips.Any(c => c.Id == clip.Id))?.Id;
        imageInspector = true; imagePoseTarget = clip.Duration > 0 ? ImagePoseTarget.End : ImagePoseTarget.Key;
        imageKeyBeat = clip.Beat; imageObjectError = "";
    }
    /// <summary>
    /// 返回禁止直接编辑的原因，空串表示可以直接拖。三类原因：资源还没就绪、
    /// 同层存在 imgxtime / imgytime / imgscaleytime（在原渲染器里这些时间偏移按 loop 作用域生效，
    /// 同层靠前的对象会连带影响本对象，直接拖动算不出等价结果）、以及本对象带非零 skew。
    /// 此外被 LOCK 的对象也在这里拦下。凡是非空，面板就只剩裸事件编辑一条路。
    /// </summary>
    string ImageTransformIssue()
    {
        if (editor == null || ActiveImageItem is not { } item) return L.Get("Waiting for image resources.");
        string issue = ImageObjectModel.DirectEditIssue(editor.Vsm, item, Current.Images.Items);
        if (issue.Length > 0) return L.Get(issue);
        if (lockedImageItems.Contains(item.Id)) return L.Get("Image selection is locked. Unlock it in IMAGES.");
        return "";
    }
    /// <summary>切换编辑目标并把播放头对到新的编辑拍。切到 KEY 时当场锁定插入点那一拍，之后播放头再走也不跟着变。</summary>
    void SetImageTarget(ImagePoseTarget target)
    {
        if (selectedImageId == null) return;
        imagePoseTarget = target;
        if (target == ImagePoseTarget.Key) imageKeyBeat = InsertionBeat;
        transport.SetPlaying(false); transport.Seek(Current.Timeline.Bpm.Time(ImageEditBeat));
        imageObjectError = "";
    }
    /// <summary>
    /// 把姿态写回文档，按编辑目标分派：PATH 只允许整体平移 X/Y，起点/终点要求该动画组确实覆盖了这些通道，
    /// 其余走 INITIAL 或指定拍写值。不满足条件时抛异常而不是静默降级——写错通道比不写更难发现。
    /// </summary>
    void CommitImagePose(ImageEditPose pose, ImageChannels channels)
    {
        if (editor == null || selectedImageId == null) return;
        string issue = ImageTransformIssue(); if (issue.Length > 0) throw new InvalidOperationException(issue);
        var map = Current.Timeline.Bpm;
        if (imagePoseTarget == ImagePoseTarget.Path)
        {
            if (channels != ImageChannels.Position) throw new InvalidOperationException(L.Get("PATH mode edits positions only."));
            var old = ReadImagePose(); editor.OffsetImagePath(selectedImageId, pose.X - old.X, pose.Y - old.Y, map);
        }
        else if (imagePoseTarget is ImagePoseTarget.Start or ImagePoseTarget.End)
        {
            var group = ActiveImageGroup;
            if (group == null || !group.Editable || (group.Channels & channels) != channels)
                throw new InvalidOperationException(L.Get("Select the animation for this property, or use INITIAL / KEY HERE."));
            editor.SetImageEndpoint(group, imagePoseTarget == ImagePoseTarget.End, pose, channels, map, imageLinkNeighbors);
        }
        else if (imagePoseTarget == ImagePoseTarget.Initial) editor.SetInitialImagePose(selectedImageId, pose, channels, map);
        else editor.SetImagePose(selectedImageId, ImageEditBeat, pose, channels, map);
        layoutRevision = -1; imageObjectError = ""; message = L.Get("Updated image: ") + selectedImageId;
    }
    /// <summary>
    /// 执行一次 image 编辑并把可预期的失败转成面板提示。只捕获这几类异常，
    /// 其它异常照常上抛：那属于 bug，不该被当成“编辑被拒绝”糊过去。
    /// </summary>
    void ImageAction(Action action)
    {
        try { action(); imageObjectError = ""; }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or System.IO.IOException)
        { imageObjectError = ex.Message; message = L.Get("Image edit: ") + ex.Message; }
    }
    /// <summary>
    /// 按当前预设加一段 4 拍动画（outCubic）。continuePath 为真时从上一组的终点接着排，保证首尾相接不留缝。
    /// 预设序号对应 ImagePresets：0 移动 +40、1 放大 1.25 倍、2 旋转 +90、3 淡入、4 淡出、5 POP IN（0.6 倍 + 淡入）。
    /// </summary>
    void AddImageMotion(bool continuePath = false, double? atBeat = null)
    {
        if (editor == null || selectedImageId == null) return;
        ImageAction(() =>
        {
            EnsureImageModel(); var map = Current.Timeline.Bpm;
            double at = continuePath && ActiveImageGroup is { } last ? last.End(map) : atBeat ?? InsertionBeat;
            string issue = ImageTransformIssue(); if (issue.Length > 0) throw new InvalidOperationException(issue);
            var startPose = imageSampler!.Pose(selectedImageId, at); var endPose = startPose;
            ImageChannels channels;
            switch (imagePreset)
            {
                case 1: channels = ImageChannels.Scale; endPose = endPose with { ScaleX = startPose.ScaleX * 1.25, ScaleY = startPose.ScaleY * 1.25 }; break;
                case 2: channels = ImageChannels.Rotation; endPose = endPose with { Rotation = startPose.Rotation + 90 }; break;
                case 3: channels = ImageChannels.Alpha; startPose = startPose with { Alpha = 0 }; endPose = endPose with { Alpha = 1 }; break;
                case 4: channels = ImageChannels.Alpha; endPose = endPose with { Alpha = 0 }; break;
                case 5: channels = ImageChannels.Scale | ImageChannels.Alpha; startPose = startPose with { ScaleX = startPose.ScaleX * .6, ScaleY = startPose.ScaleY * .6, Alpha = 0 }; endPose = endPose with { Alpha = 1 }; break;
                default: channels = ImageChannels.Position; endPose = endPose with { X = startPose.X + 40 }; break;
            }
            Guid added = editor.AddImageAnimation(selectedImageId, at, at + 4, startPose, endPose, channels, "outCubic", map);
            SelectImageObject(selectedImageId, true, added); FocusImageTrack(selectedImageId, at);
        });
    }
    /// <summary>
    /// 把时间轴滚到该 image 的动画组轨道，并在目标拍跑出可视范围时才横向平移。
    /// 纵向留一行余量便于看清上下文；起始拍下限 -64，负拍区仍可见但不至于滚到无穷远。
    /// </summary>
    void FocusImageTrack(string id, double beat)
    {
        layoutRevision = -1; RebuildEditTracks();
        int i = editTracks.FindIndex(t => t.ImageId == id && t.ImageGroup);
        if (i >= 0) { trackScroll = Math.Max(0, i - 1); motion.Snap("timeline-track-scroll", trackScroll); }
        if (beat < beatStart || BeatX(beat) > editorTracksRect.X + editorTracksRect.W) beatStart = Math.Max(-64, beat - 2);
    }
    /// <summary>
    /// 把画布视图对准当前对象：中心取其姿态坐标，缩放按缩放后的实际尺寸塞进 240x130 逻辑单位（320x180 内留边）。
    /// 缩放钳在 0.05~8，尺寸取绝对值，负缩放（镜像）的对象也不会算出反向或零缩放。
    /// </summary>
    void FrameImageObject()
    {
        if (ActiveImageItem is not { } item) return;
        transport.SetPlaying(false); transport.Seek(Current.Timeline.Bpm.Time(ImageEditBeat));
        var pose = ReadImagePose(); imageViewCenter = new((float)pose.X, (float)pose.Y);
        float width = (float)Math.Abs(item.Width * pose.ScaleX), height = (float)Math.Abs(item.Height * pose.ScaleY);
        imageViewZoom = Math.Clamp(Math.Min(240 / Math.Max(1, width), 130 / Math.Max(1, height)), .05f, 8);
    }
}
