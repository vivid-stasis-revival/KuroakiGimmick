using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;
using KuroakiGimmick.Graphics;
using static KuroakiGimmick.Core.Windows.WindowMotionConfig;
namespace KuroakiGimmick.UI;

/// <summary>
/// 编辑器工作区的整体绘制：顶栏、左侧来源面板、中间预览、右侧 inspector、底部时间轴，以及阻塞模态。
/// 预览区通过 PreviewRect 把 320×180 逻辑空间映射到 UI 像素矩形；这里的所有坐标都是 UI 像素。
/// </summary>
public sealed partial class Viewer
{
    /// <summary>
    /// 画一整帧编辑器界面。预览内容用 Unfaded 绘制，这样覆盖层的淡入淡出不会把画面本身也一起调暗，
    /// 否则用户会误以为是谱面的 alpha 变了。time 为播放位置（秒）。
    /// </summary>
    void DrawEditor(int w, int h, int pw, int ph, double time)
    {
        if (editor == null) { editorMode = false; return; }
        Canvas.Quad(logo, new(24, 20, 46, 46), Color.White);
        Text("KUROAKI", 82, 22, 23, white, true); Text(L.Get("EDITOR") + " / " + Paths.BuildRevision, 83, 50, 10, soft, true);
        Text(Current.Title + (editor.Dirty ? " *" : ""), 282, 32, 16, white, max: Math.Max(0, w - 1160));
        if (EButton(L.Get("LAYOUT"), new(w - 858, 25, 90, 31), enabled: !Busy)) OpenLayout();
        if (EButton(L.Get("EXPORT"), new(w - 758, 25, 92, 31), primary: true, enabled: !Busy)) OpenChartExport();
        if (EButton(L.Get("VSM DOCS / F1"), new(w - 656, 25, 116, 31))) OpenReference();
        if (EButton(L.Get("VIEWER UI"), new(w - 530, 25, 112, 31), active: true, enabled: !Busy)) ToggleWorkspace();
        if (EButton(L.Get("UNDO"), new(w - 409, 25, 68, 31), enabled: editor.CanUndo && !Busy)) { editor.Undo(); layoutRevision = -1; }
        if (EButton(L.Get("REDO"), new(w - 333, 25, 68, 31), enabled: editor.CanRedo && !Busy)) { editor.Redo(); layoutRevision = -1; }
        if (EButton(L.Get("SAVE AS"), new(w - 257, 25, 92, 31), enabled: !Busy)) SaveEditor(true);
        if (EButton(L.Get("SAVE"), new(w - 157, 25, 65, 31), primary: true, enabled: !Busy)) SaveEditor();
        if (EButton("?", new(w - 84, 25, 55, 31))) help = !help;
        Divider(24, 79, w - 48);
        var geometry = workspaceGeometry;
        float timelineY = geometry.SplitY, left = 24, leftW = geometry.LeftWidth,
            previewX = geometry.PreviewX, rightX = geometry.RightX;
        float previewW = geometry.PreviewWidth, previewH = timelineY - 163;
        DrawEditorSources(new(left, 96, leftW, Math.Max(1, timelineY - 110)));
        Label(textCanvas ? L.Get("TEXT CANVAS / LOCAL") : imageCanvas ? L.Get("IMAGE CANVAS / LOCAL COORDINATES") : L.Get("PREVIEW / SAME RENDERER"), previewX, 96);
        if (EButton(L.Get("IMAGE CANVAS"), new(previewX + previewW - 342, 89, 130, 25), active: imageCanvas, enabled: Current.Images.Items.Count > 0))
        { imageCanvas = !imageCanvas; textCanvas = false; if (imageCanvas) desktopPreview = false; }
        if (EButton(desktopPreview ? L.Get("DESKTOP") : L.Get("SCENE"), new(previewX + previewW - 204, 89, 94, 25), active: desktopPreview, key: "editor-desktop")) { if (textCanvas || imageCanvas) desktopPreview = false; else desktopPreview = !desktopPreview; imageCanvas = textCanvas = false; }
        if (EButton(nativeWindows != null ? L.Get("LIVE: ON") : L.Get("LIVE: OFF"), new(previewX + previewW - 104, 89, 104, 25), active: nativeWindows != null, key: "editor-live")) ToggleNativeWindows();
        var frame = new Rect(previewX, 122, previewW, previewH);
        previewFrame = frame; previewImage = Canvas.PreviewRect(frame, pw / (float)w, ph / (float)h, false);
        Canvas.Fill(frame, Color.Hex(0x030205)); Canvas.Border(frame, line);
        using (Canvas.Unfaded())
        {
            if (textCanvas) DrawTextCanvas(previewImage, time);
            else if (imageCanvas) DrawImageCanvas(previewImage, time);
            else if (desktopPreview) DrawDesktop(frame, time);
            else Canvas.Quad(Renderer.Final.Texture, previewImage, Color.White);
        }
        float controlsY = frame.Y + frame.H + 8;
        if (EButton(transport.Playing ? L.Get("PAUSE") : L.Get("PLAY"), new(previewX, controlsY, 69, 27), primary: true, enabled: !Busy, key: "editor-play")) transport.SetPlaying(!transport.Playing);
        if (EButton(L.Get("NOTES"), new(previewX + 77, controlsY, 65, 27), active: notes)) notes = !notes;
        if (EButton(L.Get("FX"), new(previewX + 150, controlsY, 39, 27), active: effects)) effects = !effects;
        if (EButton($"{transport.Speed:0.00}x", new(previewX + 197, controlsY, 61, 27), key: "editor-speed")) transport.SetSpeed(transport.Speed >= 2 ? .25 : transport.Speed + .25);
        Text("B " + Current.Timeline.Bpm.Beat(time).ToString("0.###") + " / " + Clock(time), previewX + 270, controlsY + 6, 12, white, true, previewW - 270);
        editorInspectorRect = new(rightX, 91, geometry.RightWidth, timelineY - 104);
        string inspectorKey = imageInspector ? "image:" + selectedImageId + ":" + selectedImageGroup
            : BatchSelection ? "batch:" + selectedClips.Count
            : selectedClip?.ToString() ?? "window:" + selectedWindowEvent;
        if (animatedInspector != inspectorKey)
        {
            animatedInspector = inspectorKey;
            motion.Snap("inspector", 0);
            motion.Snap("inspector-scroll", inspectorScroll);
        }
        using (Canvas.Opacity(motion.To("inspector", 1, .14))) DrawEditorInspector(editorInspectorRect, time);
        editorTimelineRect = new(24, timelineY, w - 48, h - timelineY - 61);
        DrawEditTimeline(editorTimelineRect, time, w, h);
        Divider(24, h - 51, w - 48);
        using (Canvas.Opacity(motion.To("status", 1, .15)))
            Text((editor.Dirty ? L.Get("UNSAVED | ") : L.Get("SAVED | ")) + message, 24, h - 37, 11, editor.Dirty ? soft : muted, max: w - 300);
        Text(editorBuild != null ? L.Get("REBUILDING") : L.Get("EDITOR PREVIEW"), w - 210, h - 37, 10, muted, true);
        if (help || helpAlpha > .001f)
        {
            using var fade = Canvas.Opacity(helpAlpha);
            Canvas.Fill(new(0, 0, w, h), Color.Hex(0, .84f));
            var r = new Rect(w / 2 - 330, h / 2 - 224 + (1 - helpAlpha) * 10, 660, 448); Canvas.Fill(r, panel); Canvas.Border(r, soft);
            Text(L.Get("EDITOR / CONTROLS"), r.X + 25, r.Y + 24, 20, white);
            string[] rows = [L.Get("E: mark exact time. Shift+E: delete target. CLEAR TARGET: release it."),
                L.Get("ADD GIMMICK: choose a documented or custom name and event values."),
                L.Get("F1: right-click a gimmick to add at the marked time / playhead."),
                L.Get("EXPORT: VSM / VSM + cgmk config / Chart Folder (new output paths)."),
                L.Get("Clip body: move. Right handle: duration. Double-click empty track: add."),
                L.Get("Drag empty track: box-select. Shift+click adds or removes one clip."),
                L.Get("Same mod selected: the right panel edits every one of them at once."),
                L.Get("Right-drag: pan (turns FOLLOW off). Right-click clip/track: menu."),
                L.Get("Fields edit in place: drag to select, Tab next, Enter apply, Esc cancel."),
                L.Get("Wheel: tracks. Shift+wheel: pan. Ctrl/Cmd+wheel: zoom. Alt: no snap."),
                L.Get("Hover track: short hint. Hold W: details. Ctrl/Cmd+Z: undo."),
                L.Get("Ctrl/Cmd+S: save project copy. Shift+S: save as. Tab: Viewer/Editor."),
                L.Get("Space: play. A/B: loop bounds. L: loop. Esc: close live windows."),
                L.Get("Fields accept 1/3 and 128+1/4. '_' keeps the dynamic VSM value."),
                L.Get("Double-click marker: rename. Markers are saved in the project only.")];
            // 行距 21：15 行的最后一条落在 r.Y + 360，仍在 r.Y + 385 的按钮之上。加行前先算这一笔。
            for (int i = 0; i < rows.Length; i++) Text(rows[i], r.X + 25, r.Y + 66 + i * 21, 12, muted, max: r.W - 50);
            modalInput = true;
            if (EButton(L.Get("VSM DOCS / F1"), new(r.X + 25, r.Y + 385, 297, 34))) OpenReference();
            if (EButton(L.Get("CLOSE"), new(r.X + 338, r.Y + 385, 297, 34))) help = false;
            modalInput = false;
        }

    }

    /// <summary>左侧面板的三个页签。TOOLS / IMAGES / TEXT 互斥，切走时顺带关掉对应的 inspector，避免残留在不可见的选择上。</summary>
    void DrawEditorSources(Rect r)
    {
        editorSourceRect = r;
        float third = (r.W - 12) / 3;
        if (EButton(L.Get("TOOLS"), new(r.X, r.Y, third, 27), active: !imageSources && !textSources)) imageSources = textSources = false;
        if (EButton(L.Get("IMAGES"), new(r.X + third + 6, r.Y, third, 27), active: imageSources)) { imageSources = true; textSources = textInspector = false; }
        if (EButton(L.Get("TEXT"), new(r.X + 2 * (third + 6), r.Y, third, 27), active: textSources)) { textSources = true; imageSources = false; }
        var body = new Rect(r.X, r.Y + 35, r.W, Math.Max(1, r.H - 35));
        if (textSources) DrawTextSources(body); else if (imageSources) DrawImageSources(body); else DrawEditorTools(body);
    }
    /// <summary>
    /// 工具页。内容高度固定，靠 editorSourceScroll 手动滚动并裁剪。
    /// SourceButton 只在按钮完整落在可视区内才允许点击 —— 被裁掉一半的按钮如果还能点，用户会点到看不见的东西。
    /// </summary>
    void DrawEditorTools(Rect r)
    {
        const float contentHeight = 442;
        editorSourceScroll = Math.Clamp(editorSourceScroll, 0, Math.Max(0, contentHeight - r.H));
        float y = r.Y - editorSourceScroll, x = r.X, half = (r.W - 6) / 2;
        Canvas.Clip(r);
        bool SourceButton(string text, Rect box, bool primary = false, bool enabled = true)
        {
            bool visible = box.Y >= r.Y && box.Y + box.H <= r.Y + r.H;
            return EButton(text, box, primary: primary, enabled: enabled && visible);
        }
        Label(L.Get("ADD AT MARKER / PLAYHEAD"), x, y);
        if (SourceButton(L.Get("OPEN CHART"), new(x, y + 18, half, 28), enabled: !Busy)) ChooseOpen();
        if (SourceButton(L.Get("+ FILES"), new(x + half + 6, y + 18, half, 28), enabled: !Busy)) ChooseOpen(true);
        if (SourceButton(L.Get("ADD GIMMICK..."), new(x, y + 54, r.W, 28))) OpenAddGimmick();
        if (SourceButton(L.Get("+ IMAGE / DROP IMAGE"), new(x, y + 90, r.W, 30), primary: true, enabled: !Busy)) ChooseImageImport();
        if (SourceButton(L.Get("PRESET >"), new(x, y + 128, half, 28))) { templateIndex = (templateIndex + 1) % ModTemplates.Length; customMod = ModTemplates[templateIndex]; }
        if (SourceButton(newProxy < 0 ? L.Get("GLOBAL") : L.Get("PROXY ") + newProxy, new(x + half + 6, y + 128, half, 28)))
            newProxy = newProxy + 1 >= Current.Chart.Proxies ? -1 : newProxy + 1;
        if (SourceButton(L.Get("+ GIMMICK CLIP"), new(x, y + 164, r.W, 31), primary: true, enabled: !Busy)) AddMod();
        Divider(x, y + 206, r.W);
        if (SourceButton(L.Get("WINDOW ") + newWindow, new(x, y + 216, r.W, 26)))
            OpenValue(L.Get("WindowMovement index (0..63)"), newWindow.ToString(), v => { int n = int.Parse(v, CultureInfo.InvariantCulture); if (n is < 0 or > 63) throw new FormatException(L.Get("Use 0..63.")); newWindow = n; });
        string[] labels = [L.Get("MOVE / DANCE"), L.Get("RESIZE"), L.Get("SHOW / HIDE"), L.Get("Z ORDER"), L.Get("CONTENT SOURCE")];
        for (int i = 0; i < Operations.Length; i++)
        {
            int operation = i;
            if (SourceButton(labels[i], new(x + (i % 2) * (half + 6), y + 250 + i / 2 * 31, i == 4 ? r.W : half, 26), enabled: !Busy))
                AddWindowEvent(Operations[operation]);
        }
        if (SourceButton(L.Get("PROXY BINDINGS JSON"), new(x, y + 348, r.W, 27)) && editor != null)
            OpenValue(L.Get("ECG_PROXY_WINDOW_BINDINGS (JSON array)"), (editor.Windows.Bindings ?? new JsonArray()).ToJsonString(), v =>
            {
                var a = JsonNode.Parse(v) as JsonArray ?? throw new FormatException(L.Get("Expected an array."));
                editor.Change(L.Get("Edit proxy bindings"), () => editor.Windows.Root[BindingsKey] = a.DeepClone());
            });
        if (SourceButton(L.Get("SETTINGS / LANGUAGE"), new(x, y + 386, r.W, 28), enabled: !Busy)) { OpenSettings(); click = false; }
        Text(L.Get("Scroll for more controls"), x, y + 426, 10, muted);
        Canvas.Clip(null);
        if (contentHeight > r.H)
        {
            float thumb = Math.Max(20, r.H * r.H / contentHeight), maximum = contentHeight - r.H;
            Canvas.Fill(new(r.X + r.W - 2, r.Y + (r.H - thumb) * editorSourceScroll / maximum, 2, thumb), soft);
        }
    }

    /// <summary>
    /// 虚拟桌面预览。WindowPose 的 X/Y/Width/Height 是相对桌面尺寸的归一化值，这里乘预览矩形换算成 UI 像素。
    /// 尺寸按 8 倍桌面截断、非有限值直接跳过：坏数据只应当画不出来，而不该把整个预览撑爆。
    /// 按 Z 升序绘制，后画的压在上面。
    /// </summary>
    void DrawDesktop(Rect frame, double time)
    {
        var desk = Canvas.PreviewRect(frame, 1, 1, false);
        Canvas.Clip(frame); Canvas.Fill(desk, Color.Hex(0x121B26));
        for (int i = 1; i < 8; i++) Canvas.Line(desk.X + desk.W * i / 8, desk.Y, desk.X + desk.W * i / 8, desk.Y + desk.H, 1, Color.Hex(0x273343));
        Text(L.Format($"VIRTUAL DESKTOP / {windowDesktopW} x {windowDesktopH}"), desk.X + 7, desk.Y + 7, 10, muted, true);
        foreach (var p in WindowPoses(time).Where(p => p.Visible).OrderBy(p => p.Z))
        {
            if (!double.IsFinite(p.X + p.Y + p.Width + p.Height)) continue;
            var r = new Rect(desk.X + (float)(p.X - p.Width * .5) * desk.W, desk.Y + (float)(p.Y - p.Height * .5) * desk.H,
                (float)Math.Min(p.Width, 8) * desk.W, (float)Math.Min(p.Height, 8) * desk.H);
            if (r.W <= 0 || r.H <= 0) continue;
            Canvas.Fill(r, Color.Hex(0x050507));
            if (!Renderer.DrawWindowSource(Current, time, p, r)) Text(L.Get("SOURCE ") + p.Source + L.Get(" UNAVAILABLE"), r.X + 5, r.Y + 23, 10, soft, max: r.W - 10);
            Canvas.Border(r, soft);
            if (p.Border) { Canvas.Fill(new(r.X, r.Y - 16, r.W, 16), panel); Text(p.Title + " [" + p.Id + "]", r.X + 4, r.Y - 14, 10, white, max: r.W - 8); }
        }
        Canvas.Clip(null);
    }

    /// <summary>
    /// inspector 分派：文字 → 图片 → 原始事件。选中图片时 IMAGE / EVENT 两个页签并存，
    /// 因为直接编辑并不能表达所有情况，随时要能退回到原始事件编辑。
    /// </summary>
    void DrawEditorInspector(Rect r, double time)
    {
        if (editor == null) return;
        // 就地编辑的两份本帧状态在这里归零：字段顺序（Tab 用）每帧重建，可见性要等本帧真的把输入框画出来才成立。
        inlineFields.Clear(); inlineVisible = false;
        if (textInspector && selectedTextId != null && editor.TextSources.ContainsKey(selectedTextId)) { DrawTextInspector(r); return; }
        EnsureImageModel();
        if (selectedImageId != null)
        {
            float half = (r.W - 6) / 2;
            if (EButton(L.Get("IMAGE"), new(r.X, r.Y, half, 28), active: imageInspector)) { imageInspector = true; inspectorScroll = 0; }
            if (EButton(L.Get("EVENT"), new(r.X + half + 6, r.Y, half, 28), active: !imageInspector)) { imageInspector = false; inspectorScroll = 0; }
            r = new(r.X, r.Y + 38, r.W, Math.Max(1, r.H - 38));
            if (imageInspector) { DrawImageInspector(r); return; }
        }
        DrawRawEditorInspector(r, time);
    }
    /// <summary>
    /// 原始事件编辑面板。显示并修改的是源文件里写着的那个值，而不是当前时刻的运行时求值结果，
    /// 底部另外单列 NOW 作为对照。VSM 片段的 Beat / Duration 以拍为单位；WindowMovement 事件的
    /// t / dur / easeDur 在 JSON 里是秒，面板上换算成拍显示、写回时再换算回秒，字段名也标了 / sec。
    /// UI 不认识的字段按原 JSON 类型原样保留和回写，不会因为读不懂就丢弃。
    /// </summary>
    void DrawRawEditorInspector(Rect r, double time)
    {
        if (editor == null) return;
        // 选中了多个片段时换成批量面板：改一次字段就落到整组上，而不是只改 inspector 正在显示的那一个。
        if (BatchSelection) { DrawBatchClipInspector(r); return; }
        Label(L.Get("EDIT EVENT / NOT RUNTIME VALUE"), r.X, r.Y);
        if (EButton(L.Get("DUP"), new(r.X, r.Y + 22, 65, 25))) DuplicateSelection();
        if (EButton(L.Get("DELETE"), new(r.X + 71, r.Y + 22, 76, 25))) DeleteSelection();
        if (EButton("^", new(r.X + r.W - 62, r.Y + 22, 27, 25))) inspectorScroll = Math.Max(0, inspectorScroll - 1);
        if (EButton("v", new(r.X + r.W - 30, r.Y + 22, 27, 25))) inspectorScroll++;
        int fieldCount = 0;
        float shownScroll = motion.To("inspector-scroll", inspectorScroll, .13);
        void Field(string name, string value, Action<string> action)
        {
            float row = fieldCount++ - shownScroll; float y = r.Y + 57 + row * 31;
            if (row < 0 || y + 27 > r.Y + r.H - 31) return;
            ValueField(name, value, r.X, y, r.W, action);
        }
        // 只读说明行 / 可点的跳转行，与 Field 共用同一套滚动计数，因此底部的 Clamp 自动把它们算进范围。
        void Row(string label, Action? run)
        {
            int index = fieldCount++;
            float row = index - shownScroll; float y = r.Y + 57 + row * 31;
            if (row < 0 || y + 27 > r.Y + r.H - 31) return;
            if (run == null) Text(label, r.X + 2, y + 8, 11, muted, max: r.W - 4);
            else if (EButton(label, new(r.X, y, r.W, 27), key: "inspector-row:" + index)) run();
        }
        if (selectedClip is Guid id && editor.Vsm.Find(id) is { } c)
        {
            Field(L.Get("Mod"), c.Name, v => EditClip(m => m with { Name = v }, L.Get("Change mod")));
            Field(L.Get("Beat"), VsmDocument.Ui(c.Beat), v => { double b = VsmDocument.Number(v); EditClip(m => m with { Beat = b, RepeatEnd = m.RepeatEnd + b - m.Beat }, L.Get("Set beat")); });
            Field(L.Get("Duration / beat"), VsmDocument.Ui(c.Duration), v => EditClip(m => m with { Duration = VsmDocument.Number(v) }, L.Get("Set duration")));
            Field(L.Get("From / _"), VsmDocument.UiValue(c.From), v => EditClip(m => m with { From = v }, L.Get("Set from")));
            Field(L.Get("To / _"), VsmDocument.UiValue(c.To), v => EditClip(m => m with { To = v }, L.Get("Set to")));
            Field(L.Get("Ease"), c.Ease, v => EditClip(m => m with { Ease = Easings.Normalize(v) }, L.Get("Set ease")));
            Field(L.Get("Proxy / -1 global"), c.Proxy.ToString(), v => { int p = int.Parse(v, CultureInfo.InvariantCulture); if (p >= Current.Chart.Proxies) throw new FormatException(L.Get("Proxy exceeds !proxies.")); EditClip(m => m with { Proxy = p }, L.Get("Set proxy")); });
            if (c.RepeatEnd is double end)
            {
                Field(L.Get("Repeat end"), VsmDocument.Ui(end), v => EditClip(m => m with { RepeatEnd = VsmDocument.Number(v) }, L.Get("Set repeat end")));
                Field(L.Get("Repeat step"), VsmDocument.Ui(c.RepeatStep), v => EditClip(m => m with { RepeatStep = VsmDocument.Number(v) }, L.Get("Set repeat step")));
            }
            // 谱面内剧情的触发点自己不带时长信息，真正演什么写在 story.json 里，所以额外列出来。
            if (c.Name.Equals(EpisodeScript.ModName, StringComparison.OrdinalIgnoreCase)) DrawEpisodeRows(c, Row);
            Text(L.Format($"SOURCE LINE {editor.Vsm.SourceLine(id)} / NOW {Current.Timeline.Get(c.Name, time, c.Proxy):0.####}"), r.X, r.Y + r.H - 22, 10, soft, true, r.W);
        }
        else if (selectedWindowEvent >= 0 && selectedWindowEvent < (editor.Windows.Events?.Count ?? 0) && editor.Windows.Events![selectedWindowEvent] is JsonObject ev)
        {
            double t = Number(ev, "t"), start = Current.Timeline.Bpm.Beat(t), end = Current.Timeline.Bpm.Beat(t + Math.Max(0, Duration(ev)));
            Field(L.Get("Start / beat"), VsmDocument.Ui(start), v => EditWindow(e => e["t"] = Current.Timeline.Bpm.Time(VsmDocument.Number(v)), L.Get("Set window start")));
            if (WindowMotionConfig.Text(ev, "op") is "NewWindowDance" or "WindowResize")
                Field(L.Get("Length / beat"), VsmDocument.Ui(end - start), v => EditWindow(e => e[WindowMotionConfig.Text(e, "op") == "NewWindowDance" ? "easeDur" : "dur"] = Current.Timeline.Bpm.Time(start + VsmDocument.Number(v)) - t, L.Get("Set window duration")));
            foreach (var pair in ev.ToArray())
            {
                string key = pair.Key;
                if (key == "op") { Field(L.Get("Operation"), pair.Value?.ToString() ?? "", _ => throw new FormatException(L.Get("Add the desired operation instead of retyping its schema."))); continue; }
                string shown = pair.Value?.ToString() ?? "null";
                Field(key + (key is "t" or "dur" or "easeDur" ? L.Get(" / sec") : ""), shown, v => EditWindow(e =>
                {
                    if (pair.Value is JsonObject or JsonArray) e[key] = JsonNode.Parse(v);
                    else if (pair.Value is JsonValue value && value.TryGetValue<bool>(out _)) e[key] = bool.Parse(v);
                    else if (pair.Value is JsonValue sv && sv.TryGetValue<string>(out _)) e[key] = v;
                    else e[key] = VsmDocument.Number(v);
                }, L.Get("Window: ") + key));
            }
            int wi = Integer(ev, "w"); var style = editor.Windows.Style(wi);
            Field(L.Get("Window title"), style.Title, v => ChangeWindowStyle(wi, v, null));
            Field(L.Get("Window border"), style.Border.ToString(), v => ChangeWindowStyle(wi, null, bool.Parse(v)));
            Text(L.Get("WINDOW EVENT #") + (selectedWindowEvent + 1) + L.Get(" / JSON seconds"), r.X, r.Y + r.H - 22, 10, soft, true, r.W);
        }
        else
        {
            Text(L.Get("Select a timeline clip to edit."), r.X, r.Y + 70, 13, white, max: r.W);
            Text(L.Get("Notes are read-only timing references."), r.X, r.Y + 96, 11, muted, max: r.W);
            Text(L.Get("Click a note, then add a gimmick"), r.X, r.Y + 122, 11, muted, max: r.W);
            Text(L.Get("or a WindowMovement event."), r.X, r.Y + 143, 11, muted, max: r.W);
            Text(L.Format($"{editor.Vsm.Clips.Count():N0} editable source clips"), r.X, r.Y + 181, 11, muted);
            Text(L.Format($"{editor.Vsm.PreservedLineCount:N0} other lines preserved"), r.X, r.Y + 204, 11, muted);
            Text(L.Get("Unknowns remain in the source file."), r.X, r.Y + 233, 11, soft, max: r.W);
        }
        inspectorScroll = Math.Clamp(inspectorScroll, 0, Math.Max(0, fieldCount - Math.Max(1, (int)((r.H - 88) / 31))));
    }
    /// <summary>按需补齐 settings / windows 两级 JSON 结构并把数组补到 index 长度；只写 title / border，其余字段原样不动。</summary>
    void ChangeWindowStyle(int index, string? title, bool? border)
    {
        if (editor == null) return;
        editor.Change(L.Get("Set window style"), () =>
        {
            var root = editor.Windows.Root;
            if (root[SettingsKey] is not JsonObject) root[SettingsKey] = new JsonObject();
            var settingsObject = (JsonObject)root[SettingsKey]!;
            if (settingsObject["windows"] is not JsonArray) settingsObject["windows"] = new JsonArray();
            var a = (JsonArray)settingsObject["windows"]!;
            while (a.Count <= index) a.Add((JsonNode)new JsonObject());
            if (a[index] is not JsonObject) a[index] = new JsonObject();
            var o = (JsonObject)a[index]!; if (title != null) o["title"] = title; if (border != null) o["border"] = border.Value;
        });
    }

    /// <summary>
    /// 未保存确认与数值输入两个阻塞模态。两者都在淡出结束前继续绘制，所以判断条件是"状态为真或 alpha 未归零"。
    /// 期间 modalInput 置位，让模态自己的按钮仍可点击，而下面的工作区保持禁用。
    /// </summary>
    void DrawEditorModals(int w, int h)
    {
        // 右键菜单排在两个模态下面、时间轴上面：模态一旦出现就该盖住菜单。
        DrawClipMenu(w, h);
        if (pendingDiscard != null || discardAlpha > .001f)
        {
            using var fade = Canvas.Opacity(discardAlpha);
            Canvas.Fill(new(0, 0, w, h), Color.Hex(0, .85f));
            var r = new Rect(w / 2 - 290, h / 2 - 90 + (1 - discardAlpha) * 10, 580, 180); Canvas.Fill(r, panel); Canvas.Border(r, soft);
            Text(L.Get("UNSAVED EDITOR CHANGES"), r.X + 24, r.Y + 23, 19, white);
            Text(L.Get("Save a copy before leaving, discard edits, or cancel."), r.X + 24, r.Y + 64, 13, muted);
            modalInput = true;
            if (EButton(L.Get("SAVE FIRST"), new(r.X + 24, r.Y + 118, 162, 33), primary: true))
            {
                var after = pendingDiscard; pendingDiscard = null;
                SaveEditor(after: () => { editor = null; after?.Invoke(); });
            }
            if (EButton(L.Get("DISCARD"), new(r.X + 202, r.Y + 118, 162, 33)))
            { var action = pendingDiscard; pendingDiscard = null; editor = null; action?.Invoke(); }
            if (EButton(L.Get("CANCEL"), new(r.X + 380, r.Y + 118, 174, 33))) pendingDiscard = null;
            modalInput = false;
        }
        if (!modalActive && valueAlpha <= .001f) return;
        using var modalFade = Canvas.Opacity(valueAlpha);
        Canvas.Fill(new(0, 0, w, h), Color.Hex(0, .88f));
        var box = new Rect(w / 2 - 380, h / 2 - 130 + (1 - valueAlpha) * 10, 760, 260); Canvas.Fill(box, panel); Canvas.Border(box, soft);
        Text(modalTitle, box.X + 24, box.Y + 24, 18, white, max: box.W - 48);
        Canvas.Fill(new(box.X + 24, box.Y + 70, box.W - 48, 47), modalSelectAll ? Color.Hex(0x40263C) : Color.Hex(0x07060A));
        Text(modalValue.Length > 100 ? "..." + modalValue[^97..] : modalValue, box.X + 34, box.Y + 86, 15, white, true, box.W - 68);
        Text(modalError.Length > 0 ? modalError : L.Get("Enter: apply   Esc: cancel   Ctrl/Cmd+A: select all   Ctrl/Cmd+V: paste"), box.X + 24, box.Y + 133, 12, modalError.Length > 0 ? red : muted, max: box.W - 48);
        modalInput = true;
        if (EButton(L.Get("APPLY"), new(box.X + 24, box.Y + 191, 342, 37), primary: true)) AcceptValue();
        if (EButton(L.Get("CANCEL"), new(box.X + 384, box.Y + 191, 352, 37))) CloseValue();
        modalInput = false;
    }
}
