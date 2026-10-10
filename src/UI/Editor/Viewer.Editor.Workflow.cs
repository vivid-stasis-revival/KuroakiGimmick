using System.Globalization;
using System.Text.RegularExpressions;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Documentation;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// 工作流覆盖层：ADD GIMMICK 与 EXPORT。二者都是阻塞覆盖层，显示期间下面的工作区不接受输入。
/// 准备与写出都在线程池上跑并可取消，输入分支里不做慢速文件 IO。
/// </summary>
public sealed partial class Viewer
{
    const string AddSearchKey = "workflow:add-search";
    Rect addSearchRect;
    string addShownQuery = "";
    void SetAddQuery(string value) { addQuery = value; addListScroll = 0; }
    void OpenAddSearch()
    {
        if (preferences.ModalValueEditor || addQuery.Length > InlineMax || addQuery.Contains('\n'))
            OpenValue(L.Get("Search name or description"), addQuery, SetAddQuery);
        else OpenInline(AddSearchKey, addQuery, SetAddQuery);
    }
    string workflow = "", displayedWorkflow = "", workflowError = "", addQuery = "", addName = "", addFrom = "_", addTo = "1", addEase = "linear";
    double addBeat, addDuration = 1;
    int addProxy = -1, addListScroll;
    bool workflowInput, addSwitchObject;
    float workflowAlpha;
    Rect addListRect;
    VsmReference.Entry? addTemplate;
    Guid? activeMarker;
    ChartExportKind exportKind;
    ChartExportPlan? chartExportPlan;
    Task<ChartExportPlan>? chartExportPrepare;
    Task? chartExportWrite;
    CancellationTokenSource? chartExportCancellation;
    bool ChartExportBusy => chartExportPrepare != null || chartExportWrite != null;
    bool WorkflowVisible => workflow.Length > 0 || workflowAlpha > .001f;

    /// <summary>新事件的插入拍：有目标标记时用标记的拍，否则用播放头位置（秒）换算出的拍。</summary>
    double InsertionBeat => VsmDocument.Canonical(editor?.Markers.FirstOrDefault(m => m.Id == activeMarker)?.Beat
        ?? Current.Timeline.Bpm.Beat(transport.Position));
    /// <summary>E 键：在当前播放位置打一个标记并设为目标，把插入点钉在这里，直到 CLEAR TARGET 为止。拖拽进行中不受理。</summary>
    void MarkTimestamp()
    {
        if (editor == null || editDrag != null) return;
        double beat = Current.Timeline.Bpm.Beat(transport.Position);
        transport.SetPlaying(false); selectedNoteTime = null; selectedClip = null; selectedWindowEvent = -1;
        try { activeMarker = editor.Mark(beat); }
        catch (FormatException ex) { message = ex.Message; return; }
        message = L.Format($"Marker at beat {beat:0.######}. New gimmicks use this target until CLEAR TARGET.");
    }
    /// <summary>把新建的轨道滚动到可见区并把播放头移到该拍（换算成秒）。图片 mod 走图片轨道的专用定位路径。</summary>
    void FocusAddedTrack(string name, int proxy, double beat)
    {
        EnsureImageModel();
        if (CustomImages.TryMod(name, out _, out var imageId) && declaredImageIds.Contains(imageId))
        { FocusImageTrack(imageId, beat); transport.SetPlaying(false); transport.Seek(Current.Timeline.Bpm.Time(beat)); return; }
        layoutRevision = -1; RebuildEditTracks();
        int index = editTracks.FindIndex(t => !t.Window && t.Property == name && t.Target == proxy);
        int rows = Math.Max(1, (int)(editorTracksRect.H / 35));
        if (index >= 0 && (index < trackScroll || index >= trackScroll + rows)) trackScroll = Math.Max(0, index - rows / 2);
        motion.Snap("timeline-track-scroll", trackScroll);
        beatStart = Math.Max(-64, beat - 2);
        transport.SetPlaying(false); transport.Seek(Current.Timeline.Bpm.Time(beat));
    }
    /// <summary>创作动作的前置条件：必要时顺带进入编辑模式。返回 false 表示没有可编辑文档，调用方应直接放弃。</summary>
    bool EnsureAuthoring()
    {
        if (Busy || Current.IsEmpty) { message = L.Get("Open a chart before adding or exporting."); return false; }
        if (editor == null) OpenEditor();
        return editor != null;
    }
    void OpenAddGimmick(VsmReference.Entry? entry = null, double? at = null)
    {
        if (!EnsureAuthoring()) return;
        double beat = at ?? InsertionBeat;
        CloseReference(); referenceAlpha = 0; motion.Snap("docs", 0);
        help = settings = false; workflow = displayedWorkflow = "add"; workflowError = "";
        addBeat = beat; addDuration = 1; addFrom = "_"; addTo = "1"; addEase = "linear";
        addQuery = ""; addListScroll = 0; addSwitchObject = false;
        addTemplate = entry ?? GimmickAuthoring.Entry(customMod);
        addName = entry?.Name ?? customMod;
        addProxy = GimmickAuthoring.SuggestedProxy(addTemplate, newProxy, EditableProxyCount);
        SetExtraGimmickDefaults();
        transport.SetPlaying(false); held = click = editorScrub = false;
        motion.Snap("workflow", 0);
    }
    void PickGimmick(VsmReference.Entry entry)
    {
        addTemplate = entry; addName = entry.Name; addSwitchObject = false; workflowError = "";
        addProxy = GimmickAuthoring.SuggestedProxy(entry, newProxy, EditableProxyCount);
        SetExtraGimmickDefaults();
    }
    void SetExtraGimmickDefaults()
    {
        if (addName is not ("lr_slash" or "lr_slash_color")) return;
        addDuration = 0; addEase = "linear"; addFrom = "_";
        addTo = addName == "lr_slash" ? "_" : "16777215";
        addProxy = -1;
    }
    /// <summary>某些文档条目只在 obj_custom_gimmick 下有意义；谱面对象不匹配时必须由用户显式确认切换，而不是自动改 header。</summary>
    bool NeedsCustomObject => (GimmickAuthoring.Entry(addName) ?? addTemplate)?.Scope == "custom" &&
        Current.Chart.ObjectName != "obj_custom_gimmick";
    /// <summary>
    /// 提交新 gimmick。改 obj header 和加片段在同一个撤销步骤里，撤销不会留下"对象已换但片段没了"的中间态。
    /// 失败只写 workflowError 并保持面板打开，输入不丢。
    /// </summary>
    void CommitGimmick()
    {
        if (editor == null) return;
        try
        {
            if (NeedsCustomObject && !addSwitchObject) throw new InvalidOperationException(L.Get("This entry requires obj_custom_gimmick. Explicitly enable USE CUSTOM OBJ or cancel."));
            var clip = GimmickAuthoring.Create(addName, addBeat, addDuration, addEase, addFrom, addTo, addProxy, EditableProxyCount, addTemplate);
            editor.Change(L.Get("Add ") + clip.Name, () =>
            {
                if (NeedsCustomObject && addSwitchObject) editor.Vsm.SetHeader("obj", "obj_custom_gimmick");
                EnsureProxyInitiallyVisible(clip.Proxy, clip.Name);
                editor.Vsm.Add(clip);
            });
            selectedClip = clip.Id; selectedWindowEvent = -1; inspectorScroll = 0;
            customMod = clip.Name; newProxy = clip.Proxy; workflow = ""; click = false;
            FocusAddedTrack(clip.Name, clip.Proxy, clip.Beat);
            message = L.Format($"Added {clip.Name} at beat {clip.Beat:0.######}.");
        }
        catch (Exception ex) { workflowError = ex.Message; }
    }
    void OpenChartExport()
    {
        if (!EnsureAuthoring()) return;
        transport.SetPlaying(false); help = settings = false;
        workflow = displayedWorkflow = "export"; workflowError = ""; chartExportPlan = null;
        held = click = editorScrub = false; motion.Snap("workflow", 0);
    }
    /// <summary>选好导出类型后在后台线程准备导出计划（要扫文件、算体积），窗口线程只负责发起和显示结果。</summary>
    void ChooseChartExport(ChartExportKind kind)
    {
        if (editor == null || ChartExportBusy || dialogOpen) return;
        exportKind = kind; chartExportPlan = null; workflowError = "";
        try
        {
            var input = ChartExportInput.Capture(editor, Current);
            string name = ChartExportInput.Stem(input.Project);
            string filename = kind switch
            {
                ChartExportKind.ChartFolder => name + "_chart_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                ChartExportKind.VspAndAssets => name + ".vsp",
                _ => name + ".vsm"
            };
            string location = SuggestedExportPath(kind.ToString(), filename);
            // 保存对话框接受新路径。Chart Folder 会把选中的路径建成一个目录，绝不建成占位文件；
            // 同名输出由设置开关控制，准备时将开关值冻结进计划。
            Dialog(true, location, paths =>
            {
                RememberExportDestination(kind.ToString(), paths[0]);
                chartExportCancellation?.Dispose(); chartExportCancellation = new();
                var token = chartExportCancellation.Token;
                bool overwrite = preferences.OverwriteExports;
                bool putImagesInAssets = preferences.PutImageGimmickIntoAssetsFolder;
                chartExportPrepare = Task.Run(() => ChartExport.Prepare(input, kind, paths[0], token, overwrite, putImagesInAssets), token);
                message = L.Get("Preparing export...");
            });
        }
        catch (Exception ex) { workflowError = ex.Message; }
    }
    /// <summary>后台写出导出计划。进度文本经 actions 队列回到窗口线程再赋值给 message，不在工作线程上直接改 UI 状态。</summary>
    void WriteChartExport()
    {
        if (chartExportPlan == null || ChartExportBusy) return;
        var plan = chartExportPlan;
        chartExportCancellation?.Dispose(); chartExportCancellation = new();
        var token = chartExportCancellation.Token;
        chartExportWrite = Task.Run(() => ChartExport.Write(plan, token,
            text => actions.Enqueue(() => message = text)), token);
    }
    /// <summary>每帧回收导出任务结果，并在标记被删除后释放目标引用。取消视为正常结束，不会留下半成品目录。</summary>
    void UpdateWorkflow()
    {
        if (activeMarker != null && editor?.Markers.All(m => m.Id != activeMarker) != false) activeMarker = null;
        if (chartExportPrepare is { IsCompleted: true })
        {
            try { chartExportPlan = chartExportPrepare.GetAwaiter().GetResult(); message = L.Get("Export ready. Review the destination before writing."); }
            catch (OperationCanceledException) { message = L.Get("Export cancelled."); }
            catch (Exception ex) { workflowError = ex.Message; }
            chartExportPrepare = null;
        }
        if (chartExportWrite is { IsCompleted: true })
        {
            try
            {
                chartExportWrite.GetAwaiter().GetResult();
                message = L.Get("Export complete: ") + chartExportPlan?.Destination;
                workflow = "";
            }
            catch (OperationCanceledException) { workflowError = L.Get("Export cancelled. No incomplete folder was published."); }
            catch (Exception ex) { workflowError = ex.Message; }
            chartExportWrite = null;
        }
    }
    /// <summary>覆盖层可见时吞掉一切输入，只把鼠标位置转发给自己的控件；Esc 在导出进行中是取消，空闲时才是关闭。</summary>
    bool HandleWorkflowInput(Sdl.Event e)
    {
        if (!WorkflowVisible || modalActive) return false;
        if (valueAlpha > .001f) { click = held = false; return true; }
        if (inlineField == AddSearchKey && HandleInlineInput(e)) return true;
        if (e.Type is 0x400 or 0x401 or 0x402)
        {
            mouseX = e.X; mouseY = e.Y;
            if (workflow.Length > 0 && e.Type == 0x401 && e.Button == 1) click = true;
            held = false; return true;
        }
        if (e.Type == 0x403 && workflow == "add" && addListRect.Contains(mouseX, mouseY))
        { addListScroll = Math.Max(0, addListScroll - (int)Math.Round(e.WheelY)); return true; }
        if (e.Type == 0x300 && e.Repeat == 0 && workflow.Length > 0)
        {
            if (e.Scan == 41)
            {
                if (ChartExportBusy) chartExportCancellation?.Cancel();
                else if (!dialogOpen) workflow = "";
                click = false;
            }
            else if (workflow == "add" && (e.Modifiers & 0x0CC0) != 0 && e.Scan == 9)
                OpenAddSearch();
        }
        return true;
    }
    /// <summary>覆盖层内的按钮。workflowInput 标记让命中测试知道当前处于覆盖层，避免点击穿透到下面的工作区。</summary>
    bool WorkflowButton(string label, Rect rect, bool primary = false, bool active = false, bool enabled = true)
    {
        workflowInput = true;
        bool result = Button(label, rect, primary, active, enabled && workflow.Length > 0 && !modalActive && !dialogOpen && !ChartExportBusy,
            key: "workflow:" + label + ":" + rect.X.ToString(CultureInfo.InvariantCulture) + ":" + rect.Y.ToString(CultureInfo.InvariantCulture));
        workflowInput = false;
        if (result) click = false;
        return result;
    }
    void WorkflowField(string label, string value, float x, float y, float width, Action<string> apply)
    {
        Text(label, x, y + 8, 13, muted, max: 118);
        if (WorkflowButton(value, new(x + 125, y, width - 125, 32))) OpenValue(label, value, apply);
    }
    /// <summary>
    /// 绘制工作流覆盖层。内容按 displayedWorkflow 画而不是 workflow：关闭后 workflow 立刻清空，
    /// 但淡出期间还要继续画原来的面板，否则会在淡出的第一帧突然变空。
    /// </summary>
    void DrawWorkflow(int w, int h)
    {
        if (!WorkflowVisible) return;
        using var fade = Canvas.Opacity(workflowAlpha);
        Canvas.Clip(null); Canvas.Fill(new(0, 0, w, h), Color.Hex(0, .88f));
        float width = Math.Min(w - 48, displayedWorkflow == "add" ? 970 : 820), height = Math.Min(h - 48, 690);
        var box = new Rect((w - width) / 2, (h - height) / 2 + (1 - workflowAlpha) * 10, width, height);
        Canvas.Fill(box, panel); Canvas.Border(box, soft);
        if (displayedWorkflow == "add") DrawAddGimmick(box);
        else DrawChartExport(box);
        if (modalActive || valueAlpha > .001f) return;
        if (workflowError.Length > 0)
        {
            var lines = EditorHelpLayout.Wrap(workflowError, box.W - 48, s => fonts.Measure(s, 13)).Take(2).ToArray();
            for (int i = 0; i < lines.Length; i++) Text(lines[i], box.X + 24, box.Y + box.H - 105 + i * 20, 13, soft);
        }
    }
    void DrawAddGimmick(Rect box)
    {
        inlineFields.Clear(); inlineVisible = false;
        Text(L.Get("ADD GIMMICK"), box.X + 24, box.Y + 20, 23, white);
        float split = box.X + Math.Min(305, box.W * .34f), x = split + 20, rightWidth = box.X + box.W - 24 - x;
        var searchBox = new Rect(box.X + 24, box.Y + 65, split - box.X - 40, 34);
        addSearchRect = searchBox;
        inlineFields.Add((AddSearchKey, L.Get("Search name or description"), addQuery, SetAddQuery, false));
        if (inlineField == AddSearchKey) DrawInlineEditor(searchBox);
        else if (WorkflowButton(addQuery.Length == 0 ? L.Get("SEARCH / Ctrl+F") : addQuery, searchBox)) OpenAddSearch();
        string query = inlineField == AddSearchKey ? inlineValue : addQuery;
        if (query != addShownQuery) { addShownQuery = query; addListScroll = 0; }
        var results = GimmickAuthoring.Search(query);
        addListRect = new(box.X + 24, box.Y + 110, split - box.X - 40, box.H - 175);
        int visible = Math.Max(1, (int)(addListRect.H / 34));
        addListScroll = Math.Clamp(addListScroll, 0, Math.Max(0, results.Length - visible));
        Canvas.Clip(addListRect);
        for (int row = 0; row < visible && row + addListScroll < results.Length; row++)
        {
            var entry = results[row + addListScroll];
            if (WorkflowButton(entry.Name, new(addListRect.X, addListRect.Y + row * 34, addListRect.W, 31), active: addTemplate?.Id == entry.Id)) PickGimmick(entry);
        }
        Canvas.Clip(null);
        Text(L.Format($"{results.Length} entries / custom names allowed"), box.X + 24, box.Y + box.H - 42, 12, muted, max: split - box.X - 30);
        WorkflowField(L.Get("Name"), addName, x, box.Y + 65, rightWidth, value => { addName = value; if (addTemplate?.MatchPattern.Length == 0) addTemplate = null; });
        WorkflowField(L.Get("Target / -1 global"), addProxy.ToString(CultureInfo.InvariantCulture), x, box.Y + 108, rightWidth,
            value => addProxy = int.Parse(value, CultureInfo.InvariantCulture));
        WorkflowField(L.Get("Beat"), VsmDocument.Ui(addBeat), x, box.Y + 151, rightWidth, value => addBeat = VsmDocument.Number(value));
        WorkflowField(L.Get("Duration / beat"), VsmDocument.Ui(addDuration), x, box.Y + 194, rightWidth, value => addDuration = VsmDocument.Number(value));
        WorkflowField(L.Get("From / _"), addFrom, x, box.Y + 237, rightWidth, value => addFrom = value);
        WorkflowField(L.Get("To / _"), addTo, x, box.Y + 280, rightWidth, value => addTo = value);
        WorkflowField(L.Get("Easing"), addEase, x, box.Y + 323, rightWidth, value => addEase = value);
        if (WorkflowButton(L.Get("CUSTOM NAME"), new(x, box.Y + 368, rightWidth, 32)))
        { addTemplate = null; OpenValue(L.Get("New gimmick identifier"), "", value => { addName = value; addTemplate = null; }); }
        string note = addTemplate?.Summary ?? L.Get("A new track is created for this name. Unknown mods are preserved; preview support is unchanged.");
        var rows = EditorHelpLayout.Wrap(note, rightWidth, s => fonts.Measure(s, 14, unified: true)).Take(2).ToArray();
        for (int i = 0; i < rows.Length; i++) Text(rows[i], x, box.Y + 413 + i * 24, 14, white, unified: true);
        if (NeedsCustomObject)
        {
            Text(L.Get("Requires obj_custom_gimmick"), x, box.Y + 470, 13, soft);
            if (WorkflowButton(L.Get("USE CUSTOM OBJ"), new(x, box.Y + 495, rightWidth, 31), active: addSwitchObject)) addSwitchObject = !addSwitchObject;
        }
        if (WorkflowButton(L.Get("ADD TO CHART"), new(x, box.Y + box.H - 49, rightWidth * .59f, 33), primary: true)) CommitGimmick();
        if (WorkflowButton(L.Get("CANCEL"), new(x + rightWidth * .62f, box.Y + box.H - 49, rightWidth * .38f, 33))) workflow = "";
    }
    void DrawChartExport(Rect box)
    {
        Text(L.Get("EXPORT"), box.X + 24, box.Y + 20, 24, white);
        Text(Paths.BuildRevision, box.X + box.W - 210, box.Y + 26, 13, muted, max: 186);
        (ChartExportKind Kind, string Label)[] choices =
        [
            (ChartExportKind.Vsm, "VSM"),
            (ChartExportKind.VsmAndConfig, L.Get("VSM + cgmk config")),
            (ChartExportKind.VspAndAssets, L.Get("VSP + assets")),
            (ChartExportKind.ChartFolder, L.Get("Chart Folder"))
        ];
        float buttonWidth = (box.W - 72) / choices.Length;
        for (int i = 0; i < choices.Length; i++)
            if (WorkflowButton(choices[i].Label, new(box.X + 24 + i * (buttonWidth + 8), box.Y + 67, buttonWidth, 40),
                active: chartExportPlan != null && exportKind == choices[i].Kind))
                ChooseChartExport(choices[i].Kind);
        float y = box.Y + 128;
        if (chartExportPlan is { } plan)
        {
            Text(L.Get("DESTINATION"), box.X + 24, y, 12, muted);
            Text(plan.Destination, box.X + 24, y + 23, 14, white, max: box.W - 48);
            Text(L.Format($"{plan.Files.Count} files / {plan.TotalBytes / 1048576.0:0.00} MiB"), box.X + 24, y + 52, 14, soft);
            Text(L.Get("Overwrite exports") + ": " + L.Get(plan.Overwrite ? "ON" : "OFF"), box.X + box.W - 260, y + 52, 12, muted, max: 236);
            int shown = Math.Min(7, plan.Files.Count);
            for (int i = 0; i < shown; i++) Text(plan.Files[i].Name, box.X + 24, y + 87 + i * 22, 13, white, max: box.W - 48);
            if (plan.Files.Count > shown) Text(L.Format($"... {plan.Files.Count - shown} more files"), box.X + 24, y + 87 + shown * 22, 13, muted);
            float wy = y + 87 + (shown + 1) * 22;
            foreach (string warning in plan.Warnings)
                foreach (string text in EditorHelpLayout.Wrap(warning, box.W - 48, s => fonts.Measure(s, 12)))
                { if (wy + 19 > box.Y + box.H - 119) break; Text(text, box.X + 24, wy, 12, muted); wy += 19; }
        }
        else
        {
            Text(L.Get(preferences.OverwriteExports ? "Choose an export type, then select an output path." : "Choose an export type, then select a NEW output path."), box.X + 24, y, 16, white, max: box.W - 48);
            Text(L.Get(preferences.OverwriteExports ? "Chart Folder: matching files are replaced; other files are kept." : "Chart Folder: enter a new folder name in the save dialog."), box.X + 24, y + 38, 14, muted, max: box.W - 48);
            Text(L.Get("Source files are never overwritten. Unsaved edits are included."), box.X + 24, y + 70, 14, muted, max: box.W - 48);
        }
        if (ChartExportBusy) Text(message, box.X + 24, box.Y + box.H - 78, 13, white, max: box.W - 48);
        if (WorkflowButton(L.Get("EXPORT"), new(box.X + 24, box.Y + box.H - 49, box.W * .52f, 33), primary: true, enabled: chartExportPlan != null)) WriteChartExport();
        // 取消在工作线程忙碌期间始终可用；它不会改动编辑文档。
        var cancel = new Rect(box.X + box.W * .57f, box.Y + box.H - 49, box.W * .43f - 24, 33);
        if (ChartExportBusy)
        {
            Canvas.Fill(cancel, Color.Hex(0x384252)); Canvas.Border(cancel, line);
            Text(L.Get("CANCEL EXPORT"), cancel.X + 18, cancel.Y + 10, 12, white);
            if (click && cancel.Contains(mouseX, mouseY)) { chartExportCancellation?.Cancel(); click = false; }
        }
        else if (WorkflowButton(L.Get("CLOSE"), cancel)) workflow = "";
    }
}
