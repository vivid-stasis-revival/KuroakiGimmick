using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// 谱面内剧情（custom_episode）的创作支持。谱面里那一行事件是零时长的触发点，真正演什么、演多久
/// 全写在 story.json 里，所以不画出来的话作者只能靠反复试听去猜一段剧情压在了哪几个音符上。
///
/// 时间轴上的区间和右侧的台词表都读 <see cref="Graphics.Text.GameUiRenderer.EpisodeSequence"/> 展开出来的那一份，
/// 与预览里演的、导出时渲的完全同源——这里刻意不复制一套时长估算，否则两边迟早对不上。
/// </summary>
public sealed partial class Viewer
{
    // 剧情自己一套色相。窗口动作已经占了紫、音符占了蓝与粉、音频包络占了青，
    // 琥珀色在三套主题里都还没有别的语义，因此一眼就能认出"这一段是剧情"。
    static readonly Color StoryBand = Color.Hex(0x6A4E1C), StoryEdge = Color.Hex(0xF2CE7A);

    /// <summary>上次探测剧本时的预览会话。探测要读盘，因此只在预览换过之后重做一次，不是每帧。</summary>
    Session? episodeProbed;
    string? episodeScriptPath;

    /// <summary>
    /// 这首歌旁边的剧本文件，没有就是 null。它与 <c>Current.Episode</c> 的区别在于不要求谱面已经写过
    /// custom_episode——第一条触发还没落下时，只有这里能回答"这首歌能不能插剧情"。
    /// </summary>
    string? EpisodeScriptPath
    {
        get
        {
            if (ReferenceEquals(episodeProbed, Current)) return episodeScriptPath;
            episodeProbed = Current;
            // 探测只是"文件在不在"，任何 IO 故障都当作没有剧本：编辑器不该因为一次读盘失败就打不开。
            try { episodeScriptPath = EpisodeScript.Find(Current.Project, Current.Chart); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { episodeScriptPath = null; }
            return episodeScriptPath;
        }
    }

    /// <summary>某条轨道是不是那条全局的 custom_episode 轨。</summary>
    static bool IsEpisodeTrack(EditTrack track) =>
        !track.Window && track.TextId == null && !track.ImageGroup &&
        track.Target < 0 && track.Property.Equals(EpisodeScript.ModName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 本帧画出来的剧情区间，以及拥有它的那条触发片段。
    /// 区间横跨整段剧情，而触发片段是零时长的、只有 9 像素宽：作者要选的其实是"这一段"，手却几乎必然
    /// 落在区间上。不记下来的话点在区间里什么都不会发生，双击还会被轨道的"空白处双击插入"接走，
    /// 在已经有剧情的地方又叠出一条触发。
    /// </summary>
    readonly List<(Rect Area, Guid Clip)> episodeBands = [];

    /// <summary>
    /// 在 custom_episode 轨上画出每次触发真正占用的时间。区间画在片段底下，拖拽仍然只能从片段上开始。
    /// 被下一次触发顶掉时，标签直接写明演了几句、几秒被切——"这段剧情演不完"是游戏自己不会告诉作者的事。
    /// </summary>
    void DrawEpisodeSpans(float y)
    {
        episodeBands.Clear();
        if (Renderer.GameUi.EpisodeSequence(Current) is not { } sequence) return;
        var map = Current.Timeline.Bpm;
        float left = editorTracksRect.X, right = editorTracksRect.X + editorTracksRect.W;
        for (int i = 0; i < sequence.StoryWindows.Count; i++)
        {
            var window = sequence.StoryWindows[i];
            var (start, end, truncated) = EpisodeScript.Extent(sequence, i);
            float bx = BeatX(map.Beat(start)), ex = BeatX(map.Beat(end));
            if (ex < left || bx > right) continue;
            // 刻意不画"本来还能演到哪里"的淡色延长段：截断只可能由下一次触发造成，而那一段的实色带子
            // 就从这里接着画下去，延长段必被盖住。真正要说的事写在标签上（演了几句 / 几秒被切）。
            var band = new Rect(bx, y + 2, Math.Max(2, ex - bx), 31);
            if (EpisodeTrigger(window.Start) is { } owner) episodeBands.Add((band, owner));
            Canvas.Fill(band, StoryBand.Alpha(.6f));
            Canvas.Fill(new(band.X, band.Y, band.W, 1), StoryEdge.Alpha(.5f));
            Canvas.Fill(new(band.X, band.Y + band.H - 1, band.W, 1), StoryEdge.Alpha(.5f));
            // 每句台词一根底部刻度。作者要把音符对到某一句上，看的就是这些位置。
            int shown = 0, total = 0;
            foreach (var cue in sequence.Story.Where(c => c.Trigger == window.Trigger))
            {
                total++;
                if (cue.Time >= end) continue;
                shown++;
                float cx = BeatX(map.Beat(cue.Time));
                if (cx >= left && cx <= right) Canvas.Line(cx, y + 27, cx, y + 33, 1, StoryEdge.Alpha(.75f));
            }
            // 标签从片段右边起排：零时长片段只有 9 像素宽，让开它就不会互相盖住。
            // 区间起点被滚到视野左边时标签贴住左沿：带子还在画，说明"这段在演"的那行字就不该跟着滚没。
            float labelX = Math.Max(left + 2, band.X + 13);
            float labelW = Math.Min(right, band.X + band.W) - labelX;
            if (labelW < 62) continue;
            string label = truncated
                ? L.Format($"STORY {shown}/{total} lines / cut at {end - start:0.#}s")
                : L.Format($"STORY {total} lines / {end - start:0.#}s");
            Text(label, labelX, y + 8, 10, StoryEdge, true, labelW);
        }
    }

    /// <summary>
    /// 在 <paramref name="start"/> 秒触发剧情的那条谱面事件。一条源行写成 start:end:step 时会展开成多次触发，
    /// 所以要逐个重复实例去比，而不是只看片段自己的拍。
    /// </summary>
    Guid? EpisodeTrigger(double start)
    {
        if (editor == null) return null;
        var map = Current.Timeline.Bpm;
        foreach (var clip in editor.Vsm.Clips.Where(c => c.Name.Equals(EpisodeScript.ModName, StringComparison.OrdinalIgnoreCase)))
            for (int repeat = 0; repeat < clip.RepeatCount; repeat++)
                if (Math.Abs(map.Time(clip.Beat + repeat * clip.RepeatStep) - start) < 1e-6) return clip.Id;
        return null;
    }

    /// <summary>
    /// 把落在剧情区间里的点击算到拥有它的触发片段上：选中之后 inspector 就列出这一段的每一句台词。
    /// 双击同样被这里吃掉，因此在已经有剧情的地方双击是选中它，而不是再插一条触发——空白处双击插入照旧。
    /// 由时间轴在片段画完之后调用，所以那 9 像素的片段仍然优先，拖拽也仍然只能从片段上开始：
    /// 区间本身没有可拖的语义，它的长度是 story.json 决定的，不是谱面里那一行。
    /// </summary>
    void ClickEpisodeSpans()
    {
        if (!click || !editorTracksRect.Contains(mouseX, mouseY)) return;
        foreach (var (area, id) in episodeBands)
        {
            if (!area.Contains(mouseX, mouseY)) continue;
            ClipClicked(id, (Sdl.SDL_GetModState() & (3 | 0x0CC0)) != 0);
            selectedWindowEvent = -1; inspectorScroll = 0; selectedNoteTime = null;
            transport.SetPlaying(false); click = false;
            return;
        }
    }

    /// <summary>
    /// 走真实输入与真实命中矩形的剧情轨自测。三条断言都只在完整画过一帧之后才成立，CPU 测试看不见：
    /// 轨道确实垫在最底下、区间本身接得住点击、在已有区间里双击不再叠出第二条触发。
    /// 前两条各自栽过一次——轨道顺序被"按源文件顺序"那一遍抢先登记后去重跳过，区间则一直只是背景。
    /// 这首歌没有剧本也没有触发时直接返回：自测跑在哪首歌上由调用方决定。
    /// </summary>
    public void SmokeEpisodeUi()
    {
        preferences.UiAnimations = false;
        preferences.Workspace = new WorkspaceLayout { FollowDisplayScale = false, UiScale = 1 };
        OpenEditor();
        if (editor == null) return;
        Draw(1440, 940);
        int row = editTracks.FindIndex(IsEpisodeTrack);
        if (row < 0) return;
        if (row != editTracks.Count - 1)
            throw new InvalidOperationException("The custom_episode track is not the last row of the timeline.");
        // 有剧本但一条触发都还没写下去时轨道是空的，到此为止。
        if (Renderer.GameUi.EpisodeSequence(Current) is not { StoryWindows.Count: > 0 } sequence) return;
        // 把视野推到第一次触发上：区间在视野外会被剔除，命中矩形自然也就不存在了。
        // 播放头和横向起点都要设：跟随打开时以播放头为准，关闭时以 beatStart 为准。
        double at = sequence.StoryWindows[0].Start;
        transport.SetPlaying(false); transport.Seek(at + .5);
        beatStart = Math.Max(-64, Current.Timeline.Bpm.Beat(at) - 2);
        // 滚到底再画：区间的命中矩形只有在这一行真的被画出来的那一帧才存在。
        trackScroll = editTracks.Count; Draw(1440, 940);
        motion.Snap("timeline-track-scroll", trackScroll); Draw(1440, 940);
        if (episodeBands.Count == 0)
            throw new InvalidOperationException("No story span was drawn on the custom_episode track.");
        var (area, owner) = episodeBands[0];
        // 取区间靠后的位置，避开最左边那 9 像素的触发片段——落在片段上就变成了在测片段自己。
        float x = Math.Clamp(area.X + area.W * .6f, editorTracksRect.X + 1, editorTracksRect.X + editorTracksRect.W - 2);
        float y = area.Y + area.H / 2;
        if (x <= area.X + 12) throw new InvalidOperationException("The story span is too narrow to click past its trigger.");
        SetClipSelection(null); Draw(1440, 940);
        Handle(new() { Type = 0x401, Button = 1, X = x, Y = y, Clicks = 1 }); Draw(1440, 940);
        Handle(new() { Type = 0x402, Button = 1, X = x, Y = y });
        if (selectedClip != owner)
            throw new InvalidOperationException("Clicking a story span did not select the trigger that owns it.");
        int clips = editor.Vsm.Clips.Count();
        Handle(new() { Type = 0x401, Button = 1, X = x, Y = y, Clicks = 2 }); Draw(1440, 940);
        Handle(new() { Type = 0x402, Button = 1, X = x, Y = y });
        if (editor.Vsm.Clips.Count() != clips)
            throw new InvalidOperationException("Double-clicking inside a story span inserted another trigger.");
    }

    /// <summary>
    /// 选中 custom_episode 片段时，在常规字段之后补上这一条触发实际会演出的内容：剧本文件、每句台词的
    /// 绝对时刻（点一下就跳过去），以及剧本自身的诊断。片段的七个字段照旧可改——它终究还是一条普通事件。
    /// </summary>
    void DrawEpisodeRows(VsmDocument.Clip clip, Action<string, Action?> row)
    {
        var script = Current.Episode;
        var sequence = Renderer.GameUi.EpisodeSequence(Current);
        if (script == null || sequence == null)
        {
            // 预览还没跟上这次编辑，或者这首歌压根没有剧本。两种情况的区别由探测回答，不要一起含糊成"没有"。
            row(EpisodeScriptPath == null
                ? L.Get("No story.json next to this chart; nothing will play.")
                : L.Get("Preview is still rebuilding this trigger."), null);
            return;
        }
        row(L.Get("STORY ") + ImageShortText(script.Path.Length == 0
            ? L.Get("(not found)") : Path.GetFileName(script.Path), editorInspectorRect.W - 70, 11), null);
        foreach (var d in script.Diagnostics)
            row((d.Error ? "! " : "- ") + d.Message, null);
        var map = Current.Timeline.Bpm;
        bool any = false;
        // 一条源行可能是 start:end:step 的重复区间，它在谱面里展开成多次触发，每次各自成窗。
        for (int repeat = 0; repeat < clip.RepeatCount; repeat++)
        {
            double at = map.Time(clip.Beat + repeat * clip.RepeatStep);
            int index = sequence.StoryWindows.FindIndex(w => Math.Abs(w.Start - at) < 1e-6);
            if (index < 0) continue;
            any = true;
            var window = sequence.StoryWindows[index];
            var (start, end, truncated) = EpisodeScript.Extent(sequence, index);
            if (clip.RepeatCount > 1) row(L.Format($"TRIGGER {repeat + 1} / beat {clip.Beat + repeat * clip.RepeatStep:0.###}"), null);
            if (truncated) row(L.Format($"Cut off after {end - start:0.#}s by the next trigger."), null);
            foreach (var cue in sequence.Story.Where(c => c.Trigger == window.Trigger).OrderBy(c => c.Time))
            {
                double time = cue.Time;
                string speaker = cue.Speaker.Length > 0 ? cue.Speaker + ": " : "";
                string text = speaker + cue.Text.Replace('\n', ' ');
                // 被顶掉的那几句也列出来，但标出来它们根本轮不到——删掉反而看不出剧本写了却没演。
                string mark = time >= end ? " " + L.Get("[skipped]") : "";
                row(L.Format($"{map.Beat(time):0.###}b  ") + ImageShortText(text, editorInspectorRect.W * .52f, 11) + mark,
                    () => { transport.SetPlaying(false); transport.Seek(time); selectedNoteTime = null; });
            }
        }
        if (!any) row(L.Get("Preview is still rebuilding this trigger."), null);
    }
}
