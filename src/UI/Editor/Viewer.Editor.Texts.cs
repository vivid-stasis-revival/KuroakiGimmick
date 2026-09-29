using System.Numerics;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// 文本对象的源列表与检视面板。文本分两类：id 为空的 legacy 文本（用 textcolhex，没有对齐/行距/换行宽度）
/// 和具名文本（用 textcolrgb，属性更全），两边的字段集合在这里就分好，不要合并。
/// </summary>
public sealed partial class Viewer
{
    bool textSources, textInspector, textCanvas;
    string? selectedTextId;
    int textListScroll;
    float textTimelineHeight;
    double textAt, textDuration = 4;
    Guid? textAnimation;
    bool textAnimationEnd;
    string textEase = "inOutSine";
    float textZoom = 1;
    long textRevision = -1;
    CustomText? textCache;
    TextValueSampler? textSampler;
    BpmMap? textMap;
    readonly Dictionary<string, Vector2[]> textBounds = new(StringComparer.Ordinal);
    readonly HashSet<string> expandedTextTracks = new(StringComparer.Ordinal);
    /// <summary>
    /// 一次文本拖拽的快照。Owner 与 Revision 用于松手时校验文档没被换过或改过，否则丢弃本次拖拽。
    /// CanvasScale 也要快照：提交时按它把屏幕位移还原成逻辑单位。
    /// </summary>
    sealed record TextDrag(EditorDocument Owner, long Revision, string Id, Vector2 Pointer, Vector2 Anchor,
        float CanvasScale, int Kind, double X, double Y, double Scale, double Rotation);
    TextDrag? textDrag;
    Dictionary<string, double>? textDragValues;

    /// <summary>换谱面时清空全部文本编辑状态。若正拖着要先释放鼠标捕获，否则新谱面里鼠标还被 SDL 锁着。</summary>
    void ResetTextObjects()
    {
        if (textDrag != null) Sdl.SDL_CaptureMouse(false);
        textDrag = null; textDragValues = null; selectedTextId = null; textCache = null; textRevision = -1; textAnimation = null; pendingTextAdd = false;
        textSources = textInspector = textCanvas = false; textListScroll = 0; textZoom = 1;
        expandedTextTracks.Clear();
    }
    /// <summary>
    /// 按需重建文本缓存与采样器。失效条件是 Revision 变了**或** BPM map 换了——
    /// sampler 内部绑死了一份 BpmMap，只看 Revision 会在改 BPM 后继续用旧的拍↔秒换算。
    /// </summary>
    void EnsureTexts()
    {
        if (editor == null) return;
        if (textRevision == editor.Revision && textMap == Current.Timeline.Bpm) return;
        textCache = editor.EditableTexts(); textSampler = new(editor.Vsm, Current.Timeline.Bpm);
        if (textAnimation is Guid animation && editor.Vsm.Find(animation) == null) textAnimation = null;
        textMap = Current.Timeline.Bpm; textRevision = editor.Revision;
    }
    /// <summary>
    /// 执行一次文本编辑并统一善后：成功则让布局失效重排，失败只把异常转成提示文字。
    /// 编辑动作自身负责事务性，这里不吞掉错误信息也不重试。
    /// </summary>
    void TextAction(Action action)
    {
        try { action(); layoutRevision = -1; message = L.Get("Text updated / undo available"); }
        catch (Exception ex) { message = L.Get("Text edit rejected: ") + ex.Message; }
    }
    /// <summary>
    /// 选中一个文本对象，并把 image 侧的选择和桌面预览一并关掉——两套检视面板共用同一块区域，不能同时开。
    /// 同时停播并跳到该文本的初始拍，再把时间轴滚到对应轨道并 Snap 掉滚动补间，避免选中后还在慢慢滑过去。
    /// </summary>
    void SelectText(string id)
    {
        if (editor == null) return;
        EnsureTexts(); selectedTextId = id; textSources = textInspector = textCanvas = true;
        imageSources = imageInspector = imageCanvas = desktopPreview = false;
        selectedImageId = null; selectedImageGroup = null;
        selectedClip = null; textAnimation = null; selectedWindowEvent = -1; inspectorScroll = 0;
        textAt = editor.InitialTextBeat(id); transport.SetPlaying(false); transport.Seek(Current.Timeline.Bpm.Time(textAt));
        layoutRevision = -1; RebuildEditTracks();
        trackScroll = Math.Max(0, editTracks.FindIndex(t => t.TextId == id));
        motion.Snap("timeline-track-scroll", trackScroll);
    }
    /// <summary>
    /// 新建文本对象，落在插入点那一拍。输入框保留空白字符原样，{n} 作为换行的转义，粘贴多行文本也直接支持。
    /// </summary>
    void AddTextObject()
    {
        if (editor == null) return;
        double beat = InsertionBeat;
        OpenValue(L.Get("New text (paste multiline text; {n} also starts a new line)"), "Text", value =>
        {
            string id = editor.AddText(value.Replace("{n}", "\n"), beat, true); SelectText(id);
        }, preserveWhitespace: true);
    }
    /// <summary>
    /// 左侧文本源列表。文本功能要求谱面对象是 obj_custom_gimmick 且 ENABLE_TEXT 打开，
    /// 不满足时先要求输入 YES 明确确认——这一步会改谱面配置，不能点一下就静默改掉。
    /// 列表按视口高度分页，每行顺带显示当前播放拍上的内容预览。
    /// </summary>
    void DrawTextSources(Rect r)
    {
        EnsureTexts();
        if (editor == null || textCache == null) return;
        if (EButton(L.Get("+ TEXT"), new(r.X, r.Y, r.W, 30), primary: true, enabled: !Busy))
        {
            if (Current.Chart.ObjectName != "obj_custom_gimmick" || !SongFiles.Config(editor.Project, "ENABLE_TEXT"))
                OpenValue(L.Get("Adding text enables Custom + ENABLE_TEXT. Type YES to continue."), "", value =>
                { if (value != "YES") throw new FormatException(L.Get("Type YES to enable text authoring.")); pendingTextAdd = true; });
            else AddTextObject();
        }
        Text(L.Get("TEXT OBJECTS / ") + textCache.Tracks.Count, r.X, r.Y + 41, 11, muted, max: r.W);
        int visible = Math.Max(1, (int)((r.H - 105) / 58));
        textListScroll = Math.Clamp(textListScroll, 0, Math.Max(0, textCache.Tracks.Count - visible));
        for (int i = textListScroll; i < Math.Min(textCache.Tracks.Count, textListScroll + visible); i++)
        {
            var track = textCache.Tracks[i]; float y = r.Y + 64 + (i - textListScroll) * 58;
            if (EButton(ImageShortText(track.Id.Length == 0 ? L.Get("LEGACY TEXT") : track.Id, r.W - 12), new(r.X, y, r.W, 28),
                active: selectedTextId == track.Id, key: "text-source:" + track.Id)) SelectText(track.Id);
            string preview = track.At(Current.Timeline.Bpm.Beat(transport.Position)).Replace('\n', ' ');
            Text(ImageShortText(preview.Length == 0 ? L.Get("(empty at this beat)") : preview, r.W - 8, 11), r.X + 4, y + 34, 11, muted);
        }
        Text(L.Get("Select text to edit content + motion."), r.X, r.Y + r.H - 32, 10, muted, max: r.W);
        Text(L.Get("Chart Folder includes text files."), r.X, r.Y + r.H - 16, 10, muted, max: r.W);
    }
    bool pendingTextAdd;
    /// <summary>
    /// 写入文本属性。duration 为 0 且当前选中了某个动画时改的是那个动画的端点，否则是在 textAt 上打一个值；
    /// duration &gt; 0 则新建动画并立刻选中它的终点，方便接着调。
    /// </summary>
    void CommitTextValues(IReadOnlyDictionary<string, double> values, double duration = 0)
    {
        if (editor == null || selectedTextId == null) return;
        if (duration == 0 && textAnimation is Guid animation) editor.SetTextAnimationEndpoint(selectedTextId, animation, textAnimationEnd, values);
        else
        {
            editor.SetTextProperties(selectedTextId, textAt, values, Current.Timeline.Bpm, duration, textEase);
            if (duration > 0)
            {
                var clip = editor.Vsm.Clips.First(c => c.Name == TextValueSampler.Name(values.Keys.First(), selectedTextId) && VsmDocument.NearlyEqual(c.Beat, textAt));
                SelectTextAnimation(clip, true);
            }
        }
    }
    /// <summary>
    /// 选中某个动画的起点或终点并把播放头挪过去。终点拍是按起点处的 BPM 把 Duration 折成秒再转回拍，
    /// 不是 Beat + Duration——跨 BPM 变化时两者不等，必须与原渲染器保持一致。
    /// </summary>
    void SelectTextAnimation(VsmDocument.Clip clip, bool end)
    {
        textAnimation = clip.Id; textAnimationEnd = end;
        textAt = end ? Current.Timeline.Bpm.Beat(Current.Timeline.Bpm.Time(clip.Beat) + clip.Duration * 60 / Current.Timeline.Bpm.BpmAtBeat(clip.Beat)) : clip.Beat;
        transport.SetPlaying(false); transport.Seek(Current.Timeline.Bpm.Time(textAt));
    }
    /// <summary>
    /// 文本检视面板。所有行都经 Row 做视口裁剪，rows 一路累加到最后才用来夹 inspectorScroll，
    /// 所以行数随 legacy/具名、是否选中动画而变也不会滚出范围。
    /// 显示层做过换算：scale 与 alpha 按百分比显示（存的是 0~1），颜色按 6 位十六进制显示。
    /// 播放中禁止编辑，只能先暂停。
    /// </summary>
    void DrawTextInspector(Rect r)
    {
        EnsureTexts();
        if (editor == null || selectedTextId == null || textCache == null || textSampler == null) return;
        string id = selectedTextId;
        var track = textCache.Tracks.FirstOrDefault(t => t.Id == id); if (track == null) return;
        Label(L.Get("TEXT / ") + (id.Length == 0 ? L.Get("LEGACY") : ImageShortText(id, r.W - 110)), r.X, r.Y);
        if (EButton("^", new(r.X + r.W - 59, r.Y - 4, 26, 23))) inspectorScroll = Math.Max(0, inspectorScroll - 1);
        if (EButton("v", new(r.X + r.W - 27, r.Y - 4, 26, 23))) inspectorScroll++;
        float half = (r.W - 6) / 2;
        if (EButton(L.Get("INITIAL"), new(r.X, r.Y + 24, half, 27))) { textAnimation = null; textAt = editor.InitialTextBeat(id); transport.SetPlaying(false); transport.Seek(Current.Timeline.Bpm.Time(textAt)); }
        if (EButton(L.Get("KEY HERE"), new(r.X + half + 6, r.Y + 24, half, 27))) { textAnimation = null; textAt = InsertionBeat; transport.SetPlaying(false); transport.Seek(Current.Timeline.Bpm.Time(textAt)); }
        if (EButton(L.Get("TEXT CANVAS"), new(r.X, r.Y + 58, half, 27), active: textCanvas)) { textCanvas = true; imageCanvas = desktopPreview = false; }
        if (EButton(L.Get("SCENE"), new(r.X + half + 6, r.Y + 58, half, 27), active: !textCanvas)) textCanvas = false;
        int rows = 0, visible = Math.Max(1, (int)((r.H - 120) / 32));
        bool Row(out float y) { y = r.Y + 95 + (rows++ - inspectorScroll) * 32; return y >= r.Y + 95 && y + 28 <= r.Y + r.H - 25; }
        void Field(string label, string value, Action<string> apply, bool preserveWhitespace = false)
        {
            if (!Row(out float y)) return;
            // Content 天然是多行的，InlineField 会把它交回模态；其余字段就地改。
            InlineField("text-field:" + id + ":" + label, label, value, r.X, y, r.W, apply,
                preserveWhitespace: preserveWhitespace, enabled: !transport.Playing, labelSize: 11);
        }
        void Button(string label, Action action)
        { if (Row(out float y) && EButton(label, new(r.X, y, r.W, 27), enabled: !transport.Playing, key: "text-action:" + label)) TextAction(action); }
        Field(L.Get("At / beat"), VsmDocument.Ui(textAt), value => { double beat = VsmDocument.Number(value); if (Math.Abs(beat) > 1e8) throw new FormatException(L.Get("Beat out of range.")); textAnimation = null; textAt = beat; transport.Seek(Current.Timeline.Bpm.Time(beat)); });
        if (textAnimation is Guid animId && editor.Vsm.Find(animId) is { } animation)
        {
            Button(L.Get("EDIT START") + (!textAnimationEnd ? " *" : ""), () => SelectTextAnimation(animation, false));
            Button(L.Get("EDIT END") + (textAnimationEnd ? " *" : ""), () => SelectTextAnimation(animation, true));
        }
        Field(L.Get("Content"), track.At(textAt), value => editor.SetTextCue(id, textAt, value.Replace("{n}", "\n")), preserveWhitespace: true);
        Button(L.Get("ENABLE TEXT PREVIEW + EXPORT"), () => editor.Change(L.Get("Enable text"), () => editor.Windows.Root["ENABLE_TEXT"] = true));
        Button(L.Get("+ CONTENT CUE HERE"), () => editor.SetTextCue(id, textAt, track.At(textAt)));
        Button(L.Get("CLEAR TEXT HERE"), () => editor.SetTextCue(id, textAt, ""));
        foreach (string kind in new[] { "textX", "textY", "textscale", "textrot", "textalp", id.Length == 0 ? "textcolhex" : "textcolrgb" }
            .Concat(id.Length == 0 ? Array.Empty<string>() : new[] { "textalignh", "textalignv", "textsep", "textmaxwidth" }))
        {
            string key = kind; double current = textSampler.Get(key, id, textAt);
            string label = key switch { "textX" => "X", "textY" => "Y", "textscale" => L.Get("Scale %"), "textrot" => L.Get("Rotation"), "textalp" => L.Get("Opacity %"),
                "textcolrgb" => L.Get("RGB hex"), "textcolhex" => L.Get("Color hex"), "textalignh" => L.Get("Align H 0/1/2"), "textalignv" => L.Get("Align V 0/1/2"), "textsep" => L.Get("Line gap (-1)"), _ => L.Get("Wrap chars") };
            bool color = key is "textcolrgb" or "textcolhex", percent = key is "textscale" or "textalp";
            Field(label, color ? ((uint)Math.Clamp(current, 0, 16777215)).ToString("X6") : (percent ? current * 100 : current).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), value =>
            {
                double number = color ? Convert.ToUInt32(value.Trim().TrimStart('#'), 16) : VsmDocument.Number(value) / (percent ? 100 : 1);
                CommitTextValues(new Dictionary<string, double> { [key] = number });
            });
        }
        Button(L.Get("CENTER X + Y"), () => CommitTextValues(new Dictionary<string, double> { ["textX"] = 160, ["textY"] = 90 }));
        Field(L.Get("Duration / beat"), VsmDocument.Ui(textDuration), value => { double n = VsmDocument.Number(value); if (n <= 0 || n > 1e8) throw new FormatException(L.Get("Use a positive duration.")); textDuration = n; });
        Field(L.Get("Easing"), textEase, value => { if (!Easings.IsKnown(value)) throw new FormatException(L.Get("Unknown easing.")); textEase = Easings.Normalize(value); });
        Button(L.Get("+ MOVE ANIMATION"), () => { CommitTextValues(new Dictionary<string, double> { ["textX"] = textSampler.Get("textX", id, textAt) + 40,
            ["textY"] = textSampler.Get("textY", id, textAt) }, textDuration); });
        Button(L.Get("+ FADE OUT"), () => CommitTextValues(new Dictionary<string, double> { ["textalp"] = 0 }, textDuration));
        Button(L.Get("+ FADE IN"), () => editor.SetTextProperties(id, textAt, new Dictionary<string, double> { ["textalp"] = 1 },
            Current.Timeline.Bpm, textDuration, textEase, new Dictionary<string, double> { ["textalp"] = 0 }));
        Button(L.Get("RAW ANIMATION EVENTS"), () => { expandedTextTracks.Add(id); layoutRevision = -1; textInspector = false; selectedClip = editor.Vsm.Clips.FirstOrDefault(c => CustomText.TryMod(c.Name, out _, out var target) && target == id)?.Id; });
        foreach (var clip in editor.Vsm.Clips.Where(c => c.Duration > 0 && CustomText.TryMod(c.Name, out _, out var target) && target == id)
            .GroupBy(c => (c.Beat, c.Duration, c.Ease)).Select(g => g.First()))
        {
            var motionClip = clip;
            Button(L.Get("ANIMATION @ ") + VsmDocument.Ui(clip.Beat), () => SelectTextAnimation(motionClip, true));
        }
        foreach (var cue in track.Cues)
        {
            var selected = cue;
            Button(L.Get("CUE @ ") + VsmDocument.Ui(cue.Beat) + " / " + ImageShortText(cue.Text.Replace('\n', ' '), r.W * .45f, 11), () => { textAnimation = null; textAt = selected.Beat; transport.Seek(Current.Timeline.Bpm.Time(textAt)); inspectorScroll = 0; });
        }
        // Content 显示的是目标拍之前最后一条仍然生效的 cue。移动/删除也必须针对同一条，
        // 不能拿任意 textAt 去做精确浮点查找；否则面板明明显示着字幕，按钮却会 no-op 或抛 Single() 的通用异常。
        var activeCue = track.Cues.LastOrDefault(c => c.Beat <= textAt + 1e-9);
        Field(L.Get("Move cue to"), VsmDocument.Ui(textAt), value =>
        {
            if (activeCue == null) throw new InvalidOperationException(L.Get("No text cue exists at or before the target beat."));
            double beat = VsmDocument.Number(value);
            editor.MoveTextCue(id, activeCue.Beat, beat); textAt = beat; transport.Seek(Current.Timeline.Bpm.Time(beat));
        });
        Button(L.Get("DELETE CUE AT TARGET"), () =>
        {
            if (activeCue == null) throw new InvalidOperationException(L.Get("No text cue exists at or before the target beat."));
            editor.DeleteTextCue(id, activeCue.Beat);
        });
        inspectorScroll = Math.Clamp(inspectorScroll, 0, Math.Max(0, rows - visible));
        Text(transport.Playing ? L.Get("Pause to edit text.") : L.Get("Drag: move / corner: scale / top: rotate"), r.X, r.Y + r.H - 17, 10, muted, max: r.W);
    }
}
