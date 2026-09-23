using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;
using static KuroakiGimmick.Core.Windows.WindowMotionConfig;

namespace KuroakiGimmick.UI;

/// <summary>
/// 编辑器分部的核心：编辑模式的进出、异步预览重建流水线、保存、模态输入框，以及拍 ↔ 像素 ↔ 秒的换算与吸附。
/// 时间轴按拍显示，播放/渲染时间按秒，两者之间一律经 BPM map 转换。
/// </summary>
public sealed partial class Viewer
{
    bool editorMode, desktopPreview, loopEnabled, editorFollow, noteMagnet = true;
    // Track 帮助刻意做成悬停触发：标签本身保持与源文件一致的原始写法，按住 W 时才按需揭示语义。
    bool trackHelpWHeld;
    long trackHelpWDownAt;
    EditorDocument? editor;
    Task<Session>? editorBuild;
    long compilingRevision = -1, renderedRevision = -1, layoutRevision = -1;
    EditorDocument? compilingDocument;
    double beatStart, pixelsPerBeat = 54, loopIn, loopOut = 8;
    /// <summary>FOLLOW 打开时播放头停靠在可视区宽度的这个比例处，后面那 66% 留给"接下来会发生什么"。</summary>
    const double FollowAnchor = .34;
    /// <summary>上一帧 follow 自己写出的 beatStart。null = 上一帧没在 follow（暂停、手动平移、拖拽中），恢复时必须从真实位置重新起跳。</summary>
    double? followWrote;

    /// <summary>
    /// 手动平移时间轴的唯一入口：右键拖动、滚轮横移、&lt; &gt; 按钮。播放中一旦手动把视图挪开，
    /// FOLLOW 自动关掉、按钮随之熄灭，视图就留在你拖到的地方——否则刚挪开就被滑回锚点，等于播放时没法看别处。
    /// 设置里打开"始终 FOLLOW"则保留旧行为，永远滑回播放头。缩放和跳转不走这里：前者只改比例尺，
    /// 后者会把播放头一起带过去，都不算"移动出去"。
    /// </summary>
    void ScrollTimeline(double start)
    {
        start = Math.Max(-64, start);
        // 真的动了才让位。纯纵向的右键拖动、拖到底还继续拖的滚轮都会落到这里，但它们没改视图，不该关掉 FOLLOW。
        if (start != beatStart && editorFollow && transport.Playing && !preferences.AlwaysFollow) editorFollow = false;
        beatStart = start;
    }
    int trackScroll, inspectorScroll, snapIndex = 2, templateIndex, newProxy = -1, newWindow;
    static readonly double[] SnapSteps = [0, 1, .5, .25, .125, 1.0 / 3, 1.0 / 6, 1.0 / 12, .0625];
    static readonly string[] SnapLabels = ["OFF", "1", "1/2", "1/4", "1/8", "1/3", "1/6", "1/12", "1/16"];
    static readonly string[] ModTemplates = ["prx", "pry", "prrz", "przm", "pra", "scrollspeed", "velocity", "noterot", "wave", "notealp", "uialpha", "fx_glow"];
    // 即使源 VSM 目前还没有这些 mod 的任何事件，也要把它们列出来。编辑器为此暴露一条空轨道，
    // 用户就能像在视频编辑器里添加第一个片段那样创建第一个事件。
    static readonly string[] CoreGlobalTracks = ["scrollspeed", "velocity", "noterot", "wave", "notealp", "xoffset", "yoffset", "beat", "boost_distance", "boost_time", "uialpha", "fx_film"];
    static readonly string[] CoreProxyTracks = ["prx", "pry", "prrz", "przm", "pra"];
    string customMod = "prx";
    Guid? selectedClip;
    int selectedWindowEvent = -1;
    double? selectedNoteTime;
    Rect editorTimelineRect, editorTracksRect, editorInspectorRect;
    byte mouseClicks;
    bool editorScrub;
    /// <summary>一条时间轴轨道。Label 只用于显示，Property 始终是源文件里的原始 mod identifier，两者不可互换。</summary>
    sealed record EditTrack(string Key, string Label, bool Window, int Target, string Property, string? ImageId = null, bool ImageGroup = false, string? TextId = null);
    readonly List<EditTrack> editTracks = [];
    EditTrack? hoveredEditTrack;
    /// <summary>拖拽起始快照，全部以拍为单位。MouseBeat 记录按下点，用于保持抓取处的相对偏移；Resize 为 true 时只动右端。</summary>
    sealed record DragState(VsmDocument.Clip? Clip, JsonObject? Window, int WindowIndex, double Beat, double End, double MouseBeat, bool Resize);
    DragState? editDrag;
    double dragBeat, dragDuration;
    bool modalActive, modalInput, modalSelectAll, modalTrim = true;
    string modalTitle = "", modalValue = "", modalError = "";
    Action<string>? modalAccept;
    Action? pendingDiscard;
    NativeWindowPreview? nativeWindows;
    WindowMotionTimeline? windowTimeline;
    WindowMotionConfig? windowTimelineConfig;
    int windowDesktopW = 1920, windowDesktopH = 1080;
    AudioData? waveformAudio;
    float[] waveform = [];

    /// <summary>进入编辑模式。首次进入才构造 EditorDocument；视图起点按当前播放位置换算成拍并往前留 2 拍。原谱面永不就地覆盖，保存写的是副本。</summary>
    public void OpenEditor()
    {
        if (Busy || Current.IsEmpty) return;
        try
        {
            editor ??= new EditorDocument(Current);
            editorMode = true; help = false; settings = false;
            beatStart = Math.Max(0, Current.Timeline.Bpm.Beat(transport.Position) - 2);
            transport.SetDuration(EditorDuration());
            message = L.Get("EDITOR: click a note, add a clip, drag its body or right edge. Save creates an editable copy.");
        }
        catch (Exception e) { message = L.Get("Editor unavailable: ") + e.Message; }
    }
    /// <summary>在拖拽、手势或模态进行中拒绝切换，避免留下半途的编辑状态；退出只隐藏工作区，未保存的修改仍保留在 editor 里。</summary>
    void ToggleWorkspace()
    {
        if (modalActive || editDrag != null || ImageGestureActive || Busy) return;
        if (!editorMode) OpenEditor();
        else
        {
            editorMode = false; help = false; hoveredEditTrack = null; trackHelpWHeld = false;
            message = editor?.Dirty == true ? L.Get("Viewer / unsaved editor changes are still active.") : L.Get("Viewer");
        }
    }
    /// <summary>
    /// 换谱面时清空全部编辑器状态。未完成的后台构建不 abort，而是移交 retiredImageReaders 等它自然结束，
    /// 否则会在窗口线程上阻塞等待文件读取。各 revision 归零保证下一帧必定重建一次预览。
    /// </summary>
    void ResetEditorForLoad()
    {
        ResetTextObjects();
        ResetImageObjects();
        CancelImageImport(); droppedImages.Clear(); imageDropActive = false;
        if (editorBuild is { IsCompleted: false }) retiredImageReaders.Add(editorBuild);
        activeMarker = null; workflow = ""; workflowAlpha = 0; chartExportPlan = null;
        editor = null; editorBuild = null; compilingDocument = null; renderedRevision = compilingRevision = layoutRevision = -1;
        CloseInline(); CloseClipMenu(); selectedClips.Clear(); marqueeActive = false; marqueePending = null; panActive = false;
        selectedClip = null; selectedWindowEvent = -1; selectedNoteTime = null; editDrag = null; hoveredEditTrack = null;
        trackHelpWHeld = false; trackHelpWDownAt = 0; displayedHelpTrack = null; motion.Snap("track-help-visible", 0);
        trackScroll = inspectorScroll = 0; motion.Snap("timeline-track-scroll", 0); motion.Snap("inspector-scroll", 0); beatStart = 0; windowTimelineConfig = null; nativeWindows?.Dispose(); nativeWindows = null;
        if (editorMode) OpenEditor();
    }
    /// <summary>时间轴总长（秒），取谱面时长、音符结束与所有 WindowMovement 事件结束的最大值，末尾各留 0.5 秒余量。</summary>
    double EditorDuration() => Math.Max(Current.Duration, Math.Max(Current.Timeline.End + .5,
        (editor?.Windows.Events ?? Current.WindowMotion.Events ?? new JsonArray()).OfType<JsonObject>().Select(e => Number(e, "t") + Math.Max(0, Duration(e)) + .5).DefaultIfEmpty(1).Max()));

    /// <summary>所有编辑的统一入口：套一层带标签的撤销步骤，并作废布局缓存。change 抛异常时编辑被整体拒绝，文档保持原样。</summary>
    void Edit(string label, Action change)
    {
        if (editor == null || Busy) return;
        try { editor.Change(label, change); message = label + L.Get(" / preview updating"); layoutRevision = -1; }
        catch (Exception e) { message = L.Get("Edit rejected: ") + e.Message; }
    }
    /// <summary>
    /// 每帧驱动编辑器：回收后台编译结果、必要时发起新的重建、处理循环试听与跟随播放头。
    /// 重建在线程池上跑（Session 构造要读文件），窗口线程只做结果接收；只有文档引用和 revision 都还对得上才采用结果，
    /// 否则说明期间又改过，这一份直接丢弃。拖拽或图片手势进行中不重建，避免每帧重编译。
    /// </summary>
    void UpdateEditor()
    {
        // 主选中项一旦被别处改写（选中文字 / 图片 / 窗口事件、新建、撤销后重选……），批量选择自动作废。
        // 这样那十几处直接写 selectedClip 的旧代码都不必知道选择集的存在，不变量在这里统一收口。
        if (selectedClips.Count > 0 && (selectedClip is not Guid picked || !selectedClips.Contains(picked))) selectedClips.Clear();
        if (pendingTextAdd && !modalActive) { pendingTextAdd = false; AddTextObject(); }
        if (editorBuild is { IsCompleted: true })
        {
            try
            {
                var s = editorBuild.GetAwaiter().GetResult();
                if (ReferenceEquals(editor, compilingDocument) && editor?.Revision == compilingRevision)
                {
                    Current = s; renderedRevision = compilingRevision; windowTimelineConfig = null;
                    transport.SetDuration(EditorDuration());
                    message = L.Get("Preview updated. ") + Current.Chart.Diagnostics.Count + L.Get(" compatibility notices (not hidden).");
                }
            }
            catch (Exception ex) { message = L.Get("Preview rebuild failed: ") + ex.Message; renderedRevision = compilingRevision; }
            editorBuild = null;
        }
        if (editor != null && editorBuild == null && editor.Revision != renderedRevision && !Busy && editDrag == null && !ImageGestureActive)
        {
            compilingDocument = editor; compilingRevision = editor.Revision;
            string text = editor.Vsm.Text, imageText = editor.Images.Text, imageRoot = editor.Images.ResourceRoot;
            var windows = editor.CompiledWindows(); var p = editor.Project.Copy();
            var textSources = editor.TextSources.ToDictionary(x => x.Key, x => x.Value);
            p.RenderWidth = Current.Project.RenderWidth; p.PreviewVolume = Current.Project.PreviewVolume;
            p.AudioDelayMs = Current.Project.AudioDelayMs; p.VisualDelayMs = Current.Project.VisualDelayMs;
            p.GameUiEnabled = Current.Project.GameUiEnabled;
            var old = Current; string? path = editor.SavedProjectPath ?? old.ProjectPath;
            editorBuild = Task.Run(() => new Session(p, path, text, windows, old, imageText, imageRoot, textSources));
        }
        if (editorMode && loopEnabled && transport.Playing)
        {
            double start = Current.Timeline.Bpm.Time(loopIn), end = Current.Timeline.Bpm.Time(loopOut);
            if (end > start && transport.Position >= end) transport.Seek(start);
        }
        // FOLLOW 不再是"播放头撞到右边 90% 就整页跳一次"，而是把它锁在可视区 34% 处连续滚动。
        // 平滑量放在"播放头到视野左沿的距离"上而不是 beatStart 本身：这个量的目标只在缩放时才变，
        // 追平之后 To 每帧返回同一个常数，beatStart = 播放头 - 常数 就是严格匀速滚动，不会有残留抖动；
        // 而播放中途 seek / 循环回跳时 beatStart 跟着播放头一起跳，播放头始终钉在同一列，不会甩屏。
        // 位移走 UiMotion（单调 UI 时钟）而不是自己按帧积分，因此与帧率无关；关掉 UI 动画就退化成瞬时锁定，截图仍然确定。
        if (editorMode && editorFollow && transport.Playing && editorTracksRect.W > 0 &&
            !panActive && !marqueeActive && !editorScrub && editDrag == null && !ImageGestureActive)
        {
            double beat = Current.Timeline.Bpm.Beat(transport.Position), span = editorTracksRect.W / pixelsPerBeat;
            float target = (float)(span * FollowAnchor), actual = (float)(beat - beatStart);
            // beatStart 被别处改过（手动平移、滚轮、跳转），或刚从暂停/拖拽恢复：从真实位置续接。
            // 但差得比一整屏还远时滑过去只会变成一次长扫屏，不如直接落位。
            if (followWrote != beatStart) motion.Snap("timeline-follow", Math.Abs(actual - target) > span ? target : actual);
            beatStart = Math.Max(0, beat - motion.To("timeline-follow", target, .3));
            followWrote = beatStart;
        }
        else followWrote = null;
    }
    /// <summary>另存为一份可编辑副本，绝不就地覆盖导入的源文件。</summary>
    void SaveEditor(bool saveAs = false, Action? after = null)
    {
        if (editor == null || Busy || ImageGestureActive) return;
        if (!saveAs && editor.SavedProjectPath != null) { SaveEditorAt(editor.SavedProjectPath, after); return; }
        string dir = Path.GetDirectoryName(Current.Project.Chart ?? Current.Project.Gimmick ?? Current.ProjectPath) ?? Paths.Output;
        string filename = "edit_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".sgv.json";
        Dialog(true, Path.Combine(dir, filename), paths => SaveEditorAt(paths[0], after));
    }
    /// <summary>Save 与 Save As 共用的成功提交点；只有 SaveCopy 真正完成才更新最近工程。</summary>
    void SaveEditorAt(string path, Action? after = null)
    {
        if (editor == null) return;
        try
        {
            string saved = editor.SaveCopy(path);
            RememberRecentSource(saved);
            // 保存之后再次挂载 / 重新加载时必须用刚写出的伴生文件，而不是导入时的 VSM 路径。
            Current.Project.EditorMarkers = editor.Markers.ToList();
            Current.Project.Gimmick = editor.Project.Gimmick;
            Current.Project.WindowMotion = editor.Project.WindowMotion;
            Current.Project.Images = editor.Project.Images;
            Current.Project.TextFiles = editor.Project.TextFiles == null ? null : new(editor.Project.TextFiles);
            Current.Project.ImagePathsRelativeToVsp = editor.Project.ImagePathsRelativeToVsp;
            renderedRevision = -1;
            message = L.Get("Saved editor project: ") + saved; after?.Invoke();
        }
        catch (Exception ex) { message = L.Get("Save failed: ") + ex.Message; }
    }
    /// <summary>返回 true 表示已拦截：有未保存修改时暂停播放并把动作挂起，等确认对话框决定是否真的丢弃。</summary>
    bool GuardUnsaved(Action discardAction)
    {
        if (editor?.Dirty != true) return false;
        pendingDiscard = discardAction; transport.SetPlaying(false); return true;
    }
    /// <summary>
    /// 打开数值 / 文本输入模态。会暂停播放并开启 SDL 文本输入。preserveWhitespace 用于文字内容一类
    /// 首尾空白有意义的场合，此时不做 Trim。同时吞掉本帧的 click，避免打开模态的那一次点击穿透到下层控件。
    /// </summary>
    void OpenValue(string title, string value, Action<string> accept, bool preserveWhitespace = false)
    {
        if (modalActive) return;
        // 模态要接管键盘，就地编辑必须先让位；否则两边都在收 0x303，输入会同时进两个缓冲。
        CloseInline();
        transport.SetPlaying(false); modalActive = true; modalTitle = title; modalValue = value;
        modalAccept = accept; modalError = ""; modalSelectAll = true; modalTrim = !preserveWhitespace;
        if (!Sdl.SDL_StartTextInput(host.Window)) { modalError = L.Get("Text input: ") + Sdl.Error; }
        click = false;
    }
    void CloseValue() { modalActive = false; modalAccept = null; Sdl.SDL_StopTextInput(host.Window); click = false; }
    /// <summary>accept 抛异常时只把错误显示在模态里并保持打开，用户可以继续改；不会静默关闭而丢掉输入。</summary>
    void AcceptValue()
    {
        try { modalAccept?.Invoke(modalTrim ? modalValue.Trim() : modalValue); CloseValue(); }
        catch (Exception e) { modalError = e.Message; }
    }
    /// <summary>
    /// 编辑器按钮：参考手册、帮助、模态、右键菜单或未保存确认覆盖在上层时一律禁用，防止点到被遮挡的工作区。
    /// 命中后清掉 click，保证同一次点击不会再被后续控件消费。
    /// </summary>
    bool EButton(string label, Rect rect, bool primary = false, bool active = false, bool enabled = true,
        string? key = null, [CallerFilePath] string caller = "", [CallerLineNumber] int callerLine = 0)
    {
        bool allowed = !ReferenceVisible && (!modalActive && pendingDiscard == null && !help || modalInput) && (!menuOpen || menuInput);
        bool hit = Button(label, rect, primary, active, enabled && allowed, key, caller, callerLine);
        if (hit) click = false;
        return hit;
    }
    /// <summary>inspector 的一行字段。默认就地编辑，焦点 key 带上当前选中对象的身份，换了选择焦点不会串到同名字段上。</summary>
    void ValueField(string name, string value, float x, float y, float width, Action<string> accept, bool preserveWhitespace = false)
        => InlineField(animatedInspector + "/" + name, name, value, x, y, width, accept, preserveWhitespace);
    /// <summary>就地替换当前选中的片段，保留原 Id，因此事件身份和源文件中的行序都不变。</summary>
    void EditClip(Func<VsmDocument.Clip, VsmDocument.Clip> apply, string name)
    {
        if (editor == null || selectedClip is not Guid id || editor.Vsm.Find(id) is not { } c) return;
        // 校验放在模态回调里做：值非法时模态保持打开，而不是静默关闭把输入丢掉。
        editor.Change(name, () => editor.Vsm.Replace(apply(c))); layoutRevision = -1; message = name;
    }
    /// <summary>改 WindowMovement 事件走深拷贝后整体替换同一下标，UI 不认识的字段原样保留在 JSON 里，不会因为没读过就被丢掉。</summary>
    void EditWindow(Action<JsonObject> apply, string name)
    {
        if (editor?.Windows.Events is not { } a || selectedWindowEvent < 0 || selectedWindowEvent >= a.Count || a[selectedWindowEvent] is not JsonObject e) return;
        var next = (JsonObject)e.DeepClone(); apply(next);
        editor.Change(name, () => editor.ReplaceWindow(selectedWindowEvent, next)); layoutRevision = -1; message = name;
    }
    /// <summary>UI 像素 X → 拍。仅对时间轴轨道区有效。</summary>
    double BeatAt(float x) => beatStart + (x - editorTracksRect.X) / pixelsPerBeat;
    /// <summary>拍 → UI 像素 X，BeatAt 的逆运算。</summary>
    float BeatX(double beat) => editorTracksRect.X + (float)((beat - beatStart) * pixelsPerBeat);
    /// <summary>
    /// 把拍值吸附到网格或最近音符。按住 Alt（0x0300 = 左右 Alt）完全绕过吸附。
    /// 音符磁吸优先于网格，但只在屏幕距离 10 像素以内才生效，所以缩放越小磁吸范围越宽容；
    /// 类型 3/4/5 的音符不参与磁吸。
    /// </summary>
    double Snap(double beat, bool magnet = true)
    {
        if ((Sdl.SDL_GetModState() & 0x0300) != 0) return beat;
        if (noteMagnet && magnet)
        {
            var map = Current.Timeline.Bpm;
            var nearest = Current.Chart.Notes.Where(n => n.Type is not (3 or 4 or 5))
                .Select(n => map.Beat(n.Time)).OrderBy(b => Math.Abs(b - beat)).Take(1).ToArray();
            if (nearest.Length > 0 && Math.Abs(nearest[0] - beat) * pixelsPerBeat <= 10) return nearest[0];
        }
        double step = SnapSteps[snapIndex]; return step <= 0 ? beat : Math.Round(beat / step) * step;
    }
    /// <summary>
    /// 新增一条 mod 片段。From 取该时刻的当前生效值，保证接上去连续；To 给一个能立刻看出效果的目标值。
    /// 目标 proxy 超出谱面 !proxies 声明范围时直接拒绝，不偷偷改绑到别的 proxy。
    /// </summary>
    void AddMod(double? at = null, string? property = null, int? proxy = null)
    {
        if (editor == null) return;
        string name = property ?? customMod; int target = proxy ?? GimmickAuthoring.SuggestedProxy(KuroakiGimmick.Core.Documentation.VsmReference.Shared.MatchMod(name).FirstOrDefault()?.Entry, newProxy, Current.Chart.Proxies);
        if (target >= Current.Chart.Proxies) { message = L.Get("Proxy is outside the chart's declared !proxies range."); return; }
        double beat = at ?? InsertionBeat;
        double from = Current.Timeline.Get(name, Current.Timeline.Bpm.Time(beat), target);
        double to = name switch
        {
            "przm" => 1.1, "prx" => 30, "pry" => -10, "prrz" => 12,
            "pra" or "notealp" or "uialpha" => from > .5 ? 0 : 1,
            "freeze" or "fx_film" or "enable_hue" => from >= 1 ? 0 : 1,
            // 新建的核心音符轨要立刻看得出变化，否则会造出一个 From == To 的空片段。
            "scrollspeed" => Math.Abs(from) < 1e-9 ? 3 : from * 1.5,
            "velocity" => Math.Abs(from) < 1e-9 ? 1.5 : from * 1.5,
            "noterot" => from + 90,
            "wave" => 5,
            _ => from
        };
        // custom_episode 是一个触发点，不是数值渐变：时长、缓动、取值都不参与判定。写成零时长的 `_` 形式
        // 与模组文档一致，也免得时间轴上出现一个看起来有长度、实则时长毫无意义的片段。
        var clip = name.Equals(EpisodeScript.ModName, StringComparison.OrdinalIgnoreCase)
            ? new VsmDocument.Clip(Guid.NewGuid(), beat, 0, "linear", "_", "_", name, target)
            : new VsmDocument.Clip(Guid.NewGuid(), beat, 1, "outSine", VsmDocument.Value(from), VsmDocument.N(to), name, target);
        Edit(L.Get("Add ") + name, () => editor.Vsm.Add(clip));
        if (editor.Vsm.Find(clip.Id) != null)
        { selectedClip = clip.Id; selectedWindowEvent = -1; inspectorScroll = 0; FocusAddedTrack(name, target, beat); }
    }
    /// <summary>新增 WindowMovement 事件。时间轴按拍操作，写进 JSON 的 t / 时长一律先经 BPM map 换算成秒。</summary>
    void AddWindowEvent(string op, double? at = null, int? window = null)
    {
        if (editor == null) return;
        double beat = at ?? InsertionBeat;
        double t = Current.Timeline.Bpm.Time(beat), duration = Current.Timeline.Bpm.Time(beat + 1) - t;
        var e = NewEvent(op, t, duration, window ?? newWindow);
        Edit(L.Get("Add ") + op, () => selectedWindowEvent = editor.AddWindow(e)); selectedClip = null; inspectorScroll = 0;
    }
    /// <summary>删除当前选中项。优先级：图片对象组 → 片段 / 窗口事件 → 标记；三者互不越界，避免误删看不见的那一层。</summary>
    void DeleteSelection()
    {
        if (editor == null) return;
        if (imageInspector && ActiveImageGroup is { } imageGroup && imageGroup.Editable)
        {
            ImageAction(() => editor.DeleteImageGroup(imageGroup)); selectedImageGroup = selectedClip = null;
            imagePoseTarget = ImagePoseTarget.Initial; layoutRevision = -1; return;
        }
        if (selectedClip == null && selectedWindowEvent < 0 && activeMarker is Guid marker)
        { editor.RemoveMarker(marker); activeMarker = null; return; }
        if (BatchSelection) { DeleteClips(); return; }
        if (selectedClip is Guid id) Edit(L.Get("Delete clip"), () => editor.Vsm.Delete(id));
        else if (selectedWindowEvent >= 0 && selectedWindowEvent < (editor.Windows.Events?.Count ?? 0))
            Edit(L.Get("Delete window event"), () => editor.Windows.Events!.RemoveAt(selectedWindowEvent));
        selectedClip = null; selectedWindowEvent = -1;
    }
    /// <summary>
    /// 复制到插入点。副本必定获得新 Id —— 事件身份不共享。若插入点就落在原件上，则顺延到原件之后，
    /// 免得两份完全重叠、在时间轴上看不出来。
    /// </summary>
    void DuplicateSelection()
    {
        if (editor == null) return;
        if (imageInspector && ActiveImageGroup is { } imageGroup && imageGroup.Editable)
        {
            ImageAction(() =>
            {
                double at = InsertionBeat;
                if (Math.Abs(at - imageGroup.Beat) < 1e-9) at = imageGroup.End(Current.Timeline.Bpm) + (imageGroup.Duration == 0 ? 1 : 0);
                Guid added = editor.DuplicateImageGroup(imageGroup, at, Current.Timeline.Bpm);
                SelectImageObject(imageGroup.ImageId, true, added);
            }); return;
        }
        if (BatchSelection) { DuplicateClips(); return; }
        double destination = InsertionBeat;
        if (selectedClip is Guid id && editor.Vsm.Find(id) is { } c)
        {
            if (Math.Abs(destination - c.Beat) < 1e-7) destination += Math.Max(.25, c.End - c.Beat);
            var copy = c with { Id = Guid.NewGuid(), Beat = destination, RepeatEnd = c.RepeatEnd + destination - c.Beat };
            Edit(L.Get("Duplicate clip"), () => editor.Vsm.Add(copy)); selectedClip = copy.Id;
        }
        else if (selectedWindowEvent >= 0 && selectedWindowEvent < (editor.Windows.Events?.Count ?? 0) && editor.Windows.Events![selectedWindowEvent] is JsonObject e)
        {
            var copy = (JsonObject)e.DeepClone(); double t = Current.Timeline.Bpm.Time(destination);
            if (Math.Abs(t - Number(e, "t")) < 1e-7) t += Math.Max(.125, Duration(e));
            copy["t"] = t; Edit(L.Get("Duplicate window event"), () => selectedWindowEvent = editor.AddWindow(copy));
        }
    }
    /// <summary>求 t 秒时的窗口位姿。WindowMotionTimeline 按配置对象的引用做缓存，配置换了才重建。</summary>
    IReadOnlyList<WindowPose> WindowPoses(double t)
    {
        if (!ReferenceEquals(windowTimelineConfig, Current.WindowMotion))
        { windowTimelineConfig = Current.WindowMotion; windowTimeline = new(Current.WindowMotion, windowDesktopW, windowDesktopH); }
        return ProxyWindowLayout.Compose(Current, t, windowTimeline!.At(t));
    }
    /// <summary>开关 LIVE 真实窗口预览。创建 SDL 窗口属于 GPU 工作，必须留在窗口线程；失败时立即 Dispose 并回到虚拟桌面预览。</summary>
    void ToggleNativeWindows()
    {
        if (nativeWindows != null) { nativeWindows.Dispose(); nativeWindows = null; message = L.Get("Live windows closed."); return; }
        if (!Current.WindowMotion.HasContent) { message = L.Get("No WindowMovement events or proxy bindings loaded."); return; }
        try
        {
            nativeWindows = new(host.Gpu, host.Window); windowDesktopW = nativeWindows.Bounds.w; windowDesktopH = nativeWindows.Bounds.h;
            windowTimelineConfig = null; message = L.Get("LIVE WINDOWS enabled. Escape or close any preview window to stop.");
        }
        catch (Exception e) { nativeWindows?.Dispose(); nativeWindows = null; message = L.Get("Live windows: ") + e.Message; }
    }
    /// <summary>向真实窗口呈现一帧；只能在窗口线程调用。任何异常都当作后端失效，直接关闭 LIVE 预览而不是逐帧重试。</summary>
    void PresentNativeWindows()
    {
        if (nativeWindows == null || Busy) return;
        try { nativeWindows.Present(Current, transport.Position, WindowPoses(transport.Position), Canvas, Renderer); }
        catch (Exception e) { nativeWindows.Dispose(); nativeWindows = null; message = L.Get("Live windows stopped: ") + e.Message; }
    }
}
