using System.Text.Json.Nodes;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;
using static KuroakiGimmick.Core.Windows.WindowMotionConfig;
namespace KuroakiGimmick.UI;

/// <summary>
/// 时间轴本体：轨道行的构建与绘制、片段拖拽、缩放与刷子定位。横轴单位是拍，播放头与音频读表用秒，
/// 二者一律经 BPM map 换算；行高、标签宽等都是 UI 像素。
/// </summary>
public sealed partial class Viewer
{
    /// <summary>
    /// 时间轴音符块的两色，直接量自玩家自己导出的音符皮肤像素：每个精灵都是 1px 亮边 + 一片暗底。
    /// chip 轨 0-1 取 sp_note_chip_normal 帧 0、2-3 取帧 1；bumper 三条依次是 sp_note_bumper_normal 的
    /// L/M/R 帧（judge bumper 同色，只多一道高光，缩到 7px 宽看不出来）；两种地雷共用 sp_note_*_mine_normal。
    /// 硬编码而不是去采样纹理：Texture.Load 上传完就丢掉了 CPU 像素，为了四个颜色再解一次 PNG 不值。
    /// </summary>
    static (Color Fill, Color Edge) NoteBlockColors(int type, int lane) => type switch
    {
        1 or 8 => lane switch
        {
            0 => (Color.Hex(0x0818AD), Color.Hex(0x041EFF)),
            1 => (Color.Hex(0x81009E), Color.Hex(0xD000FF)),
            _ => (Color.Hex(0x980819), Color.Hex(0xF20D27))
        },
        6 or 7 => (Color.Hex(0x000000), Color.Hex(0xEE1C24)),
        _ => lane < 2 ? (Color.Hex(0xCBDAF5), Color.Hex(0xDDE9FF)) : (Color.Hex(0xF5CBDE), Color.Hex(0xFFDDED))
    };

    /// <summary>
    /// 按文档 revision 增量重建轨道列表；revision 没变就直接返回，避免每帧重排。
    /// 轨道顺序即源文件顺序（核心轨在前），不按字母排序去编造层级。
    /// </summary>
    void RebuildEditTracks()
    {
        if (editor == null || layoutRevision == editor.Revision) return;
        EnsureImageModel();
        editTracks.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddModTrack(string name, int proxy)
        {
            string key = $"{proxy}:{name}";
            if (seen.Add(key))
            {
                // 显示出来的标签刻意就是未经改动的源 identifier。目标作用域另用一个小标显示，
                // 这样 imgscalex_whowrit 这类名字仍然可以直接复制 / 搜索。
                editTracks.Add(new(key, name, false, proxy, name, CustomImages.TryMod(name, out _, out var id) && declaredImageIds.Contains(id) ? id : null));
            }
        }

        // 视频编辑器不会因为某条轨道暂时没有片段就把它藏起来。主要的 note / SV 控制轨始终保留，
        // 这样第一个事件可以直接在时间轴上创建。
        foreach (string name in CoreGlobalTracks) AddModTrack(name, -1);
        for (int proxy = 0; proxy < Current.Chart.Proxies; proxy++)
            foreach (string name in CoreProxyTracks) AddModTrack(name, proxy);

        // 其余轨道一律保持源文件顺序，而不是按字母排序凭空造出一套层级。
        // 图片 / 文字对象默认折叠成一行；展开后才把属于它的原始 mod 轨道逐条列出来。
        void AddImageTrack(string id)
        {
            if (!seen.Add("I:" + id)) return;
            editTracks.Add(new("I:" + id, id, false, -1, "", id, true));
            if (expandedImageTracks.Contains(id))
                foreach (var c in editor.Vsm.Clips.Where(c => ImageObjectModel.ForImage(c, id))) AddModTrack(c.Name, c.Proxy);
        }
        void AddTextTrack(string id)
        {
            if (!seen.Add("T:" + id)) return;
            editTracks.Add(new("T:" + id, id.Length == 0 ? L.Get("Legacy text") : id, false, -1, "", TextId: id));
            if (expandedTextTracks.Contains(id))
                foreach (var c in editor.Vsm.Clips.Where(c => CustomText.TryMod(c.Name, out _, out var target) && target == id)) AddModTrack(c.Name, c.Proxy);
        }
        foreach (var clip in editor.Vsm.Clips)
        {
            if (CustomText.TryMod(clip.Name, out _, out var textId) && editor.TextSources.ContainsKey(textId)) AddTextTrack(textId);
            else if (CustomImages.TryMod(clip.Name, out _, out var imageId) && declaredImageIds.Contains(imageId)) AddImageTrack(imageId);
            else AddModTrack(clip.Name, clip.Proxy);
        }
        foreach (var item in Current.Images.Items.Where(i => declaredImageIds.Contains(i.Id))) AddImageTrack(item.Id);
        foreach (var id in editor.TextSources.Keys) AddTextTrack(id);

        foreach (var group in (editor.Windows.Events ?? new JsonArray()).OfType<JsonObject>().GroupBy(e => (Window: Integer(e, "w"), Op: WindowMotionConfig.Text(e, "op"))))
        {
            string key = "W:" + group.Key.Window + ":" + group.Key.Op;
            if (seen.Add(key)) editTracks.Add(new(key, group.Key.Op, true, group.Key.Window, group.Key.Op));
        }
        layoutRevision = editor.Revision;
    }
    /// <summary>按音频对象引用缓存波形包络；只有换了音频才重算。固定 4096 个 bin，与歌曲长度无关。</summary>
    void BuildWaveform()
    {
        if (ReferenceEquals(waveformAudio, Current.Audio)) return;
        waveformAudio = Current.Audio; waveform = [];
        if (waveformAudio == null) return;
        const int count = 4096; waveform = new float[count];
        // 真实解码音频的峰值包络。大文件按 stride 限量采样（每个 bin 最多取 512 个点）；
        // 绝不拿事件密度冒充音频波形。
        var samples = waveformAudio.Samples;
        for (int b = 0; b < count; b++)
        {
            int start = (int)((long)b * samples.Length / count), end = (int)((long)(b + 1) * samples.Length / count);
            int stride = Math.Max(1, (end - start) / 512);
            float peak = 0; for (int i = start; i < end; i += stride) peak = Math.Max(peak, Math.Abs(samples[i]));
            waveform[b] = Math.Min(1, peak);
        }
    }
    /// <summary>
    /// 绘制并交互整条时间轴。time 是播放位置（秒）；内部所有横向定位都先换算成拍。
    /// 网格密度随 pixelsPerBeat 自动降级（16 / 4 / 2 / 1 拍），细分线只在每格至少 8 像素时才画，避免糊成一片。
    /// </summary>
    void DrawEditTimeline(Rect r, double time, float viewportWidth, float viewportHeight)
    {
        if (editor == null) return;
        RebuildEditTracks(); BuildWaveform();
        Canvas.Fill(r, panel); Canvas.Border(r, line);
        if (EButton(L.Get("SNAP ") + L.Get(SnapLabels[snapIndex]), new(r.X + 8, r.Y + 7, 88, 25))) snapIndex = (snapIndex + 1) % SnapSteps.Length;
        if (EButton(L.Get("N MAGNET"), new(r.X + 104, r.Y + 7, 89, 25), active: noteMagnet)) noteMagnet = !noteMagnet;
        if (EButton("-", new(r.X + 205, r.Y + 7, 31, 25))) ZoomTimeline(.8, r.X + 236);
        if (EButton("+", new(r.X + 242, r.Y + 7, 31, 25))) ZoomTimeline(1.25, r.X + 273);
        if (EButton("<", new(r.X + 281, r.Y + 7, 31, 25))) ScrollTimeline(beatStart - 4);
        if (EButton(">", new(r.X + 318, r.Y + 7, 31, 25))) ScrollTimeline(beatStart + 4);
        if (EButton(L.Get("GOTO"), new(r.X + 357, r.Y + 7, 64, 25))) OpenValue(L.Get("Jump to beat"), Current.Timeline.Bpm.Beat(time).ToString("0.###"), v =>
        { double b = VsmDocument.Number(v); beatStart = Math.Max(-64, b - 2); selectedNoteTime = null; transport.Seek(Current.Timeline.Bpm.Time(b)); });
        if (EButton("A", new(r.X + 429, r.Y + 7, 31, 25))) loopIn = Current.Timeline.Bpm.Beat(time);
        if (EButton("B", new(r.X + 466, r.Y + 7, 31, 25))) loopOut = Math.Max(loopIn + .125, Current.Timeline.Bpm.Beat(time));
        if (EButton(L.Get("LOOP"), new(r.X + 505, r.Y + 7, 63, 25), active: loopEnabled)) loopEnabled = !loopEnabled;
        if (EButton(L.Get("FOLLOW"), new(r.X + 576, r.Y + 7, 74, 25), active: editorFollow)) editorFollow = !editorFollow;
        if (EButton(L.Get("MARK / E"), new(r.X + 662, r.Y + 7, 82, 25))) MarkTimestamp();
        if (EButton(L.Get("CLEAR TARGET"), new(r.X + 752, r.Y + 7, 110, 25), enabled: activeMarker != null)) activeMarker = null;
        if (r.W > 1150) Text(activeMarker != null ? L.Format($"TARGET B {InsertionBeat:0.###}") : L.Format($"{editTracks.Count} tracks"),
            r.X + 874, r.Y + 14, 11, activeMarker != null ? soft : muted, true, r.W - 1005);
        if (EButton(L.Get("UP"), new(r.X + r.W - 116, r.Y + 7, 49, 25))) trackScroll = Math.Max(0, trackScroll - 1);
        if (EButton(L.Get("DOWN"), new(r.X + r.W - 61, r.Y + 7, 53, 25))) trackScroll++;
        float labelW = Math.Clamp(LayoutOptions.TrackLabelWidth, 180, Math.Min(500, r.W - 360)), x = r.X + labelW, rulerY = r.Y + 40, markerY = rulerY + 27, audioY = markerY + 27, noteY = audioY + 27, tracksY = noteY + 58;
        editorTracksRect = new(x, tracksY, r.W - labelW - 10, Math.Max(1, r.Y + r.H - tracksY - 8));
        trackLabelGrip = new(x - 3, rulerY, 6, r.Y + r.H - rulerY - 7);
        double endBeat = beatStart + editorTracksRect.W / pixelsPerBeat;
        var timeArea = new Rect(x, rulerY, editorTracksRect.W, r.Y + r.H - rulerY - 7);
        Canvas.Clip(timeArea);
        double gridStep = pixelsPerBeat < 10 ? 16 : pixelsPerBeat < 22 ? 4 : pixelsPerBeat < 42 ? 2 : 1;
        for (double b = Math.Floor(beatStart / gridStep) * gridStep; b <= endBeat; b += gridStep)
        {
            float bx = BeatX(b); Canvas.Line(bx, rulerY, bx, r.Y + r.H - 8, 1, Math.Abs(b % 4) < .0001 ? Theme.GridMajor : line);
            Text(b.ToString("0.##"), bx + 4, rulerY + 4, 10, muted, true);
        }
        double step = SnapSteps[snapIndex];
        if (step > 0 && step < 1 && pixelsPerBeat * step >= 8)
            for (double b = Math.Ceiling(beatStart / step) * step; b <= endBeat; b += step)
                Canvas.Line(BeatX(b), noteY, BeatX(b), r.Y + r.H - 8, 1, line.Alpha(.8));
        if (loopOut > loopIn)
        {
            var highlight = new Rect(BeatX(loopIn), rulerY, (float)((loopOut - loopIn) * pixelsPerBeat), 25);
            Canvas.Fill(highlight, soft.Alpha(motion.To("loop-highlight", loopEnabled ? .3f : .08f, .15)));
        }
        DrawTimelineMarkers(new(x, markerY, editorTracksRect.W, 26), !UiOverlayVisible && !Busy);
        Canvas.Clip(timeArea);
        for (int px = 0; px < editorTracksRect.W; px += 2)
        {
            if (waveformAudio == null || waveform.Length == 0) break;
            double sec = Current.Timeline.Bpm.Time(beatStart + px / pixelsPerBeat);
            int bin = (int)(sec / waveformAudio.Duration * waveform.Length);
            if (bin < 0 || bin >= waveform.Length) continue;
            float amp = waveform[bin] * 11; Canvas.Fill(new(x + px, audioY + 13 - amp, 1.4f, Math.Max(1, 2 * amp)), Color.Hex(0x47A9AA).Alpha(.75));
        }
        // 四条只读 chip 轨。Bumper 压在相邻两条轨的中缝上（L 盖 0-1、M 盖 1-2、R 盖 2-3），所以块高按
        // Renderer.NoteLanes 给的覆盖范围来，chip 一格、bumper 两格；Hold 尾巴保留真实结束时间，不做等长化。
        for (int lane = 0; lane < 4; lane++)
            Canvas.Line(x, noteY + lane * 13, x + editorTracksRect.W, noteY + lane * 13, 1, line);
        bool timelineInteractive = !UiOverlayVisible && !Busy && !ImageGestureActive;
        // 画两趟：先 bumper 后 chip。同一拍上 chip 落在 bumper 的色带里，后画才不会被盖掉。
        Note? picked = null; int pickedRows = 5; double pickedBeat = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var n in Current.Chart.Notes)
            {
                if ((n.Type is 1 or 7 or 8) != (pass == 0) || Renderer.NoteLanes(n.Type, n.Lane) is not { } rows) continue;
                double beat = Current.Timeline.Bpm.Beat(n.Time), tail = Current.Timeline.Bpm.Beat(Math.Max(n.Time, n.End));
                if (tail < beatStart || beat > endBeat) continue;
                float nx = BeatX(beat), ny = noteY + rows.First * 13 + 2, nh = (rows.Last - rows.First + 1) * 13 - 4;
                var (fill, edge) = NoteBlockColors(n.Type, n.Lane);
                if (tail > beat)
                    Canvas.Fill(new(nx, ny + nh / 2 - 2, Math.Max(2, BeatX(tail) - nx), 4), Color.Hex(n.Lane < 2 ? 0xB4BEFFu : 0xFFB4C7u).Alpha(.5));
                var hit = new Rect(nx - 3, ny, 7, nh);
                // 精灵本身就是 1px 亮边 + 暗底两色，这里照抄这个结构：暗底铺满，亮边描一圈。
                Canvas.Fill(hit, fill);
                var rim = selectedNoteTime == n.Time ? white : edge;
                Canvas.Fill(new(hit.X, hit.Y, hit.W, 1), rim);
                Canvas.Fill(new(hit.X, hit.Y + hit.H - 1, hit.W, 1), rim);
                Canvas.Fill(new(hit.X, hit.Y, 1, hit.H), rim);
                Canvas.Fill(new(hit.X + hit.W - 1, hit.Y, 1, hit.H), rim);
                // 命中的块里取最窄的那个：bumper 的色带盖住整列，否则它下面的 chip 永远点不中。
                int rowCount = rows.Last - rows.First + 1;
                if (timelineInteractive && click && rowCount < pickedRows && timeArea.Contains(mouseX, mouseY) && hit.Contains(mouseX, mouseY))
                {
                    picked = n; pickedRows = rowCount; pickedBeat = beat;
                }
            }
        }
        if (picked != null)
        {
            selectedNoteTime = picked.Time; transport.SetPlaying(false); transport.Seek(picked.Time); click = false;
            message = L.Format($"NOTE lane {picked.Lane} / beat {pickedBeat:0.######}. Add a gimmick here; the note is unchanged.");
        }
        Canvas.Clip(null);
        Label(L.Get("MARKERS / E"), r.X + 12, markerY + 5);
        Label(L.Get("BEATS"), r.X + 12, rulerY + 5); Label(waveformAudio == null ? L.Get("NO AUDIO") : L.Get("AUDIO / PEAKS"), r.X + 12, audioY + 7);
        Label(L.Get("NOTES / READ ONLY"), r.X + 12, noteY + 7);
        Text("1   2   3   4", r.X + 12, noteY + 28, 10, muted, true);
        int visibleRows = Math.Max(1, (int)(editorTracksRect.H / 35));
        if (textTimelineHeight != editorTracksRect.H && textInspector && selectedTextId != null)
        {
            int selectedRow = editTracks.FindIndex(t => t.TextId == selectedTextId);
            if (selectedRow >= 0)
            {
                if (selectedRow < trackScroll) trackScroll = selectedRow;
                else if (selectedRow >= trackScroll + visibleRows) trackScroll = selectedRow - visibleRows + 1;
                motion.Snap("timeline-track-scroll", trackScroll);
            }
        }
        textTimelineHeight = editorTracksRect.H;
        trackScroll = Math.Clamp(trackScroll, 0, Math.Max(0, editTracks.Count - visibleRows));
        var clips = editor.Vsm.Clips.ToArray();
        hoveredEditTrack = null;
        // 片段与轨道行的几何每帧重建。选框求交、右键命中都读这两张表，而不是各自再算一遍坐标。
        clipHits.Clear(); rowHits.Clear();
        float shownTrackScroll = Math.Clamp(motion.To("timeline-track-scroll", trackScroll, .13), 0, Math.Max(0, editTracks.Count - visibleRows));
        int firstTrack = Math.Max(0, (int)MathF.Floor(shownTrackScroll));
        for (int row = 0; row <= visibleRows + 1 && row + firstTrack < editTracks.Count; row++)
        {
            var track = editTracks[row + firstTrack];
            float y = tracksY + (row + firstTrack - shownTrackScroll) * 35;
            float clipY = Math.Max(tracksY, y), clipEnd = Math.Min(tracksY + editorTracksRect.H, y + 34);
            if (clipEnd <= clipY) continue;
            Canvas.Clip(new(r.X + 1, clipY, labelW - 1, clipEnd - clipY));
            var labelRect = new Rect(r.X + 1, y, labelW - 31, 34);
            Canvas.Fill(new(r.X + 1, y, labelW - 1, 34), (row + firstTrack) % 2 == 0 ? panel : Theme.PanelAlt);
            if (track.TextId is { } tid)
            {
                if (EButton(expandedTextTracks.Contains(tid) ? "-" : ">", new(r.X + 5, y + 5, 22, 24), key: "text-expand:" + tid))
                { if (!expandedTextTracks.Add(tid)) expandedTextTracks.Remove(tid); layoutRevision = -1; }
                Text(ImageShortText(track.Label, labelW - 100), r.X + 33, y + 10, 12, white, true, labelW - 100);
                if (timelineInteractive && click && labelRect.Contains(mouseX, mouseY) && mouseY >= clipY && mouseY < clipEnd) { SelectText(tid); click = false; }
            }
            else if (track.ImageGroup)
            {
                if (EButton(expandedImageTracks.Contains(track.ImageId!) ? "-" : ">", new(r.X + 5, y + 5, 22, 24), key: "image-expand:" + track.Key))
                { if (!expandedImageTracks.Add(track.ImageId!)) expandedImageTracks.Remove(track.ImageId!); layoutRevision = -1; }
                Text(ImageShortText(track.Label, labelW - 100), r.X + 33, y + 10, 12, white, true, labelW - 100);
                if (timelineInteractive && click && labelRect.Contains(mouseX, mouseY) && mouseY >= clipY && mouseY < clipEnd)
                { SelectImageObject(track.ImageId!); click = false; }
            }
            else Text(ImageShortText(track.Label, labelW - 72), r.X + 10, y + 10, 12, track.Window ? Color.Hex(0xDAB6FF) : white, true, labelW - 72);
            string targetBadge = track.TextId != null ? "TXT" : track.ImageGroup ? "IMG" : track.Window ? "W" + track.Target : track.Target < 0 ? "G" : "P" + track.Target;
            Text(targetBadge, x - 58, y + 11, 11, muted, true, 25);
            if (labelRect.Contains(mouseX, mouseY) && mouseY >= clipY && mouseY < clipEnd)
            {
                if (!track.ImageGroup && track.TextId == null) hoveredEditTrack = track;
                Canvas.Fill(new(labelRect.X, labelRect.Y, 2, labelRect.H), soft);
            }
            if (EButton("+", new(x - 27, y + 5, 21, 24), enabled: !Busy && y + 5 >= clipY && y + 29 <= clipEnd, key: "track-add:" + track.Key))
            {
                if (track.TextId is { } textId) { double at = InsertionBeat; SelectText(textId); textAt = at; transport.Seek(Current.Timeline.Bpm.Time(at)); }
                else if (track.ImageGroup) { double at = InsertionBeat; SelectImageObject(track.ImageId!); AddImageMotion(atBeat: at); }
                else if (track.Window) AddWindowEvent(track.Property, window: track.Target);
                else AddMod(property: track.Property, proxy: track.Target);
            }
            Canvas.Clip(new(x, clipY, editorTracksRect.W, clipEnd - clipY));
            Canvas.Fill(new(x, y, editorTracksRect.W, 34), (row + firstTrack) % 2 == 0 ? Theme.TimelineA : Theme.TimelineB);
            Canvas.Line(x, y + 34, x + editorTracksRect.W, y + 34, 1, Theme.Border);
            for (double b = Math.Floor(beatStart / gridStep) * gridStep; b <= endBeat; b += gridStep)
                Canvas.Line(BeatX(b), y, BeatX(b), y + 34, 1, Math.Abs(b % 4) < .0001 ? Theme.GridMajor : Theme.Grid);
            if (step > 0 && step < 1 && pixelsPerBeat * step >= 8)
                for (double b = Math.Ceiling(beatStart / step) * step; b <= endBeat; b += step)
                    Canvas.Line(BeatX(b), y, BeatX(b), y + 34, 1, Mix(Theme.Grid, Theme.Background, .35f));
            if (track.TextId is { } textTarget) DrawTextGroupClips(textTarget, y, timelineInteractive);
            else if (track.ImageGroup) DrawImageGroupClips(track.ImageId!, y, timelineInteractive);
            else if (track.Window)
            {
                var events = editor.Windows.Events;
                for (int i = 0; i < (events?.Count ?? 0); i++)
                {
                    if (events![i] is not JsonObject e || Integer(e, "w") != track.Target || WindowMotionConfig.Text(e, "op") != track.Property) continue;
                    double b = Current.Timeline.Bpm.Beat(Number(e, "t")), end = Current.Timeline.Bpm.Beat(Number(e, "t") + Math.Max(0, Duration(e)));
                    DrawEventClip(y, b, end, WindowMotionConfig.Text(e, "preset", track.Property), selectedWindowEvent == i, true, null, e, i, timelineInteractive);
                }
            }
            else foreach (var c in clips.Where(c => c.TrackKey == track.Key))
                DrawEventClip(y, c.Beat, VsmVisualEnd(c), c.RepeatEnd != null ? $"{c.RepeatCount}x / {c.From}>{c.To}" : $"{c.From}>{c.To}", selectedClip == c.Id, false, c, null, -1, timelineInteractive);
            Canvas.Clip(null);
            rowHits.Add((track, new(x, clipY, editorTracksRect.W, clipEnd - clipY)));
            if (timelineInteractive && click && mouseClicks >= 2 && editorTracksRect.Contains(mouseX, mouseY) && new Rect(x, y, editorTracksRect.W, 34).Contains(mouseX, mouseY))
            {
                double b = Snap(BeatAt(mouseX));
                if (track.TextId is { } textTargetId) { SelectText(textTargetId); textAt = b; transport.Seek(Current.Timeline.Bpm.Time(b)); }
                else if (track.ImageGroup)
                { SelectImageObject(track.ImageId!); imageKeyBeat = b; imagePoseTarget = ImagePoseTarget.Key; transport.Seek(Current.Timeline.Bpm.Time(b)); }
                else if (track.Window) AddWindowEvent(track.Property, b, track.Target); else AddMod(b, track.Property, track.Target);
                click = false;
            }
        }
        // 轨道空白处按下左键 = 拉选框。排在片段与行内控件之后，它们都已经有机会先吃掉这一次 click。
        if (timelineInteractive && click && mouseClicks < 2 && editorTracksRect.Contains(mouseX, mouseY))
        { BeginMarquee(); click = false; }
        if (marqueeActive)
        {
            if (held) UpdateMarquee(); else FinishMarquee();
        }
        DrawMarquee();
        if (timelineInteractive && click && new Rect(x, rulerY, editorTracksRect.W, 25).Contains(mouseX, mouseY))
        { editorScrub = true; selectedNoteTime = null; transport.SetPlaying(false); transport.Seek(Current.Timeline.Bpm.Time(Snap(BeatAt(mouseX)))); click = false; }
        if (editorScrub && held) transport.Seek(Current.Timeline.Bpm.Time(Snap(BeatAt(mouseX))));
        Canvas.Clip(timeArea);
        float playhead = BeatX(Current.Timeline.Bpm.Beat(time)); Canvas.Line(playhead, rulerY, playhead, r.Y + r.H - 8, 2, white);
        Canvas.Fill(new(playhead - 4, rulerY, 8, 5), white);
        Canvas.Clip(null);
        // Track 帮助在 FinishUiFrame 里合成，排在布局调整手柄之后。把 tooltip 挪出时间轴这一遍绘制，
        // 它就有了稳定的最顶层 Z 序，而不会让手柄的可见性反过来取决于 tooltip 的可见性
        // —— 那正是以前那个一帧显示一帧隐藏的闪烁循环的成因。
    }

    /// <summary>
    /// 画一个片段并处理选中 / 开始拖拽。拖拽中画的是预览位置，源文档要等 FinishDrag 才真正改动。
    /// 片段最小宽 9 像素，保证零时长事件也点得到；右端 7 像素内按下才算拉伸，且只有可拉伸的窗口操作才允许。
    /// </summary>
    void DrawEventClip(float y, double beat, double end, string label, bool selected, bool window,
        VsmDocument.Clip? clip, JsonObject? e, int windowIndex, bool interactive)
    {
        bool dragging = editDrag != null && (clip != null && editDrag.Clip?.Id == clip.Id || e != null && editDrag.WindowIndex == windowIndex);
        if (dragging)
        {
            CalculateDrag();
            beat = dragBeat;
            if (clip != null)
            {
                var preview = editDrag!.Resize ? clip with { Duration = VsmDurationAt(clip.LastBeat, clip.LastBeat + dragDuration) }
                    : clip with { Beat = dragBeat, RepeatEnd = clip.RepeatEnd + dragBeat - clip.Beat };
                end = VsmVisualEnd(preview);
            }
            else if (e != null)
                end = editDrag!.Resize ? beat + dragDuration
                    : Current.Timeline.Bpm.Beat(Current.Timeline.Bpm.Time(beat) + Math.Max(0, Duration(e)));
        }
        // 批量选择里的其余片段跟着被拖的那一个一起走，拖动过程中就能看到整组的落点，而不是松手才知道。
        else if (clip != null && editDrag is { Resize: false, Clip: not null } group && BatchSelection
            && selectedClips.Contains(clip.Id) && selectedClips.Contains(group.Clip.Id))
        {
            CalculateDrag();
            double follow = dragBeat - group.Beat;
            beat += follow; end += follow;
        }
        float bx = BeatX(beat), ex = BeatX(end), width = Math.Max(9, ex - bx);
        if (bx + width < editorTracksRect.X || bx > editorTracksRect.X + editorTracksRect.W) return;
        var r = new Rect(bx, y + 6, width, 23);
        if (clip != null) clipHits.Add((clip.Id, r));
        bool batched = clip != null && BatchSelection && selectedClips.Contains(clip.Id);
        selected |= batched;
        var color = window ? Mix(Theme.Accent, Color.Hex(0x7854A8), .55f) : Theme.Accent.Alpha(.58f);
        string key = window ? "window-clip:" + windowIndex : "clip:" + clip?.Id;
        float select = motion.To(key, selected ? 1 : 0, .13, selected ? 1 : 0);
        Canvas.Fill(r, Mix(color, window ? Mix(Theme.AccentSoft, Color.Hex(0x8E6ABB), .55f) : Theme.AccentStrong.Alpha(.72f), select)); Canvas.Border(r, Mix(color, white, select));
        Canvas.Fill(new(r.X + r.W - 4, r.Y + 3, 2, r.H - 6), selected ? white : muted);
        // 批量选择的成员顶边加一条亮线；主选中项（inspector 正在显示的那个）另外在左侧竖一条。
        if (batched) Canvas.Fill(new(r.X, r.Y - 2, r.W, 2), white.Alpha(.85));
        if (batched && selectedClip == clip!.Id) Canvas.Fill(new(r.X - 2, r.Y, 2, r.H), white);
        if (width > 32) Text(label, bx + 5, y + 10, 12, white, true, width - 13);
        if (clip?.RepeatEnd != null)
        {
            double step = clip.RepeatStep;
            // 重复刻度只是视觉参考。拖动改的是源区间，而不是某一个展开出来的实例。
            if (step * pixelsPerBeat > 5)
                for (double b = Math.Max(beat, beat + Math.Ceiling((beatStart - beat) / step) * step); b <= Math.Min(end, beatStart + editorTracksRect.W / pixelsPerBeat); b += step)
                    Canvas.Line(BeatX(b), y + 6, BeatX(b), y + 10, 1, white.Alpha(.6));
        }
        if (interactive && click && editorTracksRect.Contains(mouseX, mouseY) && r.Contains(mouseX, mouseY))
        {
            SelectImageFromClip(clip);
            // Shift / command 点击在批量选择里增删成员；普通点击只留下这一个。
            if (clip != null) ClipClicked(clip.Id, (Sdl.SDL_GetModState() & (3 | 0x0CC0)) != 0);
            else SetClipSelection(null);
            selectedWindowEvent = window ? windowIndex : -1; inspectorScroll = 0; selectedNoteTime = null;
            bool resize = width >= 13 && mouseX >= r.X + r.W - 7 && (clip != null || WindowMotionConfig.Text(e!, "op") is "NewWindowDance" or "WindowResize");
            editDrag = new(clip, e == null ? null : (JsonObject)e.DeepClone(), windowIndex, beat, end, BeatAt(mouseX), resize);
            transport.SetPlaying(false); click = false;
        }
    }
    /// <summary>把鼠标位移换算成拍并吸附。拉伸以片段最后一个关键拍为锚，因此只动右端，起始拍不会被带着跑。</summary>
    void CalculateDrag()
    {
        if (editDrag == null) return;
        double delta = BeatAt(mouseX) - editDrag.MouseBeat;
        if (editDrag.Resize)
        {
            dragBeat = editDrag.Beat;
            double anchor = editDrag.Clip?.LastBeat ?? editDrag.Beat;
            dragDuration = Math.Max(0, Snap(editDrag.End + delta) - anchor);
        }
        else
        {
            dragBeat = Snap(editDrag.Beat + delta); dragDuration = editDrag.End - editDrag.Beat;
        }
    }
    /// <summary>
    /// 提交拖拽。位移不足 3 像素视为误触，整次拖拽作废，不产生撤销步骤。
    /// 片段用 with 就地替换，保留原 Id 与源文件行序；窗口事件写回的 t / dur / easeDur 都换算成秒。
    /// </summary>
    void FinishDrag()
    {
        if (editor == null || editDrag == null) return;
        CalculateDrag(); var d = editDrag; editDrag = null;
        if (Math.Abs(BeatAt(mouseX) - d.MouseBeat) * pixelsPerBeat < 3) return;
        if (d.Clip is { } c)
        {
            // 批量选择里拖动其中一个 = 整组按同一个位移搬家，组内相对间距不变，撤销也只有一步。
            if (!d.Resize && BatchSelection && selectedClips.Contains(c.Id))
            {
                double delta = dragBeat - d.Beat;
                var moved = editor.Vsm.Clips.Where(x => selectedClips.Contains(x.Id))
                    .Select(x => x with { Beat = x.Beat + delta, RepeatEnd = x.RepeatEnd + delta }).ToArray();
                Edit(L.Format($"Move {moved.Length} clips"), () => { foreach (var m in moved) editor.Vsm.Replace(m); });
                return;
            }
            var next = d.Resize ? c with { Duration = VsmDurationAt(c.LastBeat, c.LastBeat + dragDuration) } : c with { Beat = dragBeat, RepeatEnd = c.RepeatEnd + dragBeat - c.Beat };
            Edit(d.Resize ? L.Get("Resize clip") : L.Get("Move clip"), () => editor.Vsm.Replace(next));
        }
        else if (d.Window is { } e)
        {
            if (d.Resize) e[WindowMotionConfig.Text(e, "op") == "NewWindowDance" ? "easeDur" : "dur"] = Current.Timeline.Bpm.Time(dragBeat + dragDuration) - Current.Timeline.Bpm.Time(dragBeat);
            else e["t"] = Current.Timeline.Bpm.Time(dragBeat);
            Edit(d.Resize ? L.Get("Resize window event") : L.Get("Move window event"), () => editor.ReplaceWindow(d.WindowIndex, e));
        }
    }
    // 正式版 VSM 时间轴用事件起点处的 BPM 把 Duration 换算成时长。当区间跨越 BPM 变化时，
    // 不要私自把它重新解释成 Time(start + duration)，那会和原渲染器算出不同的结束时刻。
    double VsmVisualEnd(VsmDocument.Clip clip) => Current.Timeline.Bpm.Beat(
        Current.Timeline.Bpm.Time(clip.LastBeat) + Math.Max(0, clip.Duration) * 60 / Current.Timeline.Bpm.BpmAtBeat(clip.LastBeat));
    /// <summary>VsmVisualEnd 的逆运算：由起止拍反求应写回源文件的 Duration，同样以 start 处的 BPM 为准。</summary>
    double VsmDurationAt(double start, double end) => Math.Max(0,
        (Current.Timeline.Bpm.Time(end) - Current.Timeline.Bpm.Time(start)) * Current.Timeline.Bpm.BpmAtBeat(start) / 60);
    /// <summary>以 anchorX（UI 像素）处的拍为不动点缩放。pixelsPerBeat 限制在 3~640，避免网格退化或溢出。</summary>
    void ZoomTimeline(double scale, float anchorX)
    {
        double anchor = BeatAt(anchorX); pixelsPerBeat = Math.Clamp(pixelsPerBeat * scale, 3, 640);
        beatStart = Math.Max(-64, anchor - (anchorX - editorTracksRect.X) / pixelsPerBeat);
    }
}
