using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KuroakiGimmick.Core.Windows;

namespace KuroakiGimmick.Core.Editing;

/// <summary>
/// 编辑会话持有的是源文档（VSM 文本、cgmk JSON、VSP 文本、文本轨源串），不是渲染器对象——这里没有任何 GPU 资源，
/// 与 Session/SceneRenderer 的分层不能合并。一次提交的操作对应一次撤销单位；viewer 的显示设置不算编辑内容，不进撤销栈。
/// 保存永远写出编辑器伴生文件（*.editor.vsm / *.editor_cgmk_config.json / *.editor.vsp），绝不覆盖导入进来的原始 VSM。
/// </summary>
public sealed partial class EditorDocument
{
    /// <summary>一次撤销点的全量快照。Lines 是不可变 record 数组，未改动的行在快照间共享，不会真的复制整份文本。</summary>
    sealed record Snapshot(VsmDocument.Line[] Lines, string Windows, TimelineMarker[] Markers, string ImageText, string TextSources, string Label);
    readonly List<Snapshot> undo = [], redo = [];
    /// <summary>上次保存时各伴生文件的 SHA-256。再次保存前比对，用来发现文件在编辑器之外被改过。</summary>
    readonly Dictionary<string, string> savedHashes = new(StringComparer.Ordinal);
    string cleanText, cleanWindows, cleanImages, cleanTexts;
    TimelineMarker[] cleanMarkers;
    readonly List<TimelineMarker> markers = [];
    public IReadOnlyList<TimelineMarker> Markers => markers;
    public string[] ImportedPaths { get; }
    public VsmDocument Vsm { get; }
    public VspDocument Images { get; }
    public WindowMotionConfig Windows { get; private set; }
    public ViewerProject Project { get; private set; }
    public string? SavedProjectPath { get; private set; }
    public long Revision { get; private set; }
    /// <summary>脏标记按五份源内容与上次保存时的快照逐一比对得出，不用计数器——撤销回到保存点后应当重新变回干净。</summary>
    public bool Dirty => Vsm.Text != cleanText || Windows.Serialize() != cleanWindows || !markers.SequenceEqual(cleanMarkers) || Images.Text != cleanImages || TextSnapshot() != cleanTexts;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public string UndoLabel => undo.Count > 0 ? undo[^1].Label : "";
    public string RedoLabel => redo.Count > 0 ? redo[^1].Label : "";

    /// <summary>
    /// 从一个已装载的 Session 派生编辑会话：拷贝工程配置，再把各源文档原样读进来。
    /// 这里只读文本、不碰渲染资源；任何一份源读不动就直接抛错，宁可不让编辑，也不能把损坏的文件当成空文档交给用户改。
    /// </summary>
    public EditorDocument(Session session)
    {
        Project = session.Project.Copy();
        ImportedPaths = new[] { Project.Chart, Project.Gimmick, Project.WindowMotion, Project.Images, Project.Audio, Project.Jacket, session.ProjectPath }
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.GetFullPath(p!)).ToArray();
        // 标记要满足全部条件才收下：标签单行且不超过 128 字、拍值有限、ID 不重复、与已有标记至少差 1e-9 拍。外部改坏的条目静默丢弃，不让它污染时间轴。
        foreach (var marker in Project.EditorMarkers ?? [])
            if (marker != null && marker.Label is { Length: <= 128 } && !marker.Label.Any(char.IsControl) && double.IsFinite(marker.Beat) && Math.Abs(marker.Beat) <= 1e8 &&
                markers.All(m => m.Id != marker.Id && Math.Abs(m.Beat - marker.Beat) > 1e-9)) markers.Add(marker);
        cleanMarkers = markers.ToArray();
        // 没挂 gimmick 时从谱面现有 mod 反推一份 VSM 文本，保证编辑器始终有一份可改的源，而不是空白。
        Vsm = Project.Gimmick != null ? VsmDocument.Load(Project.Gimmick) : VsmDocument.FromChart(session.Chart);
        // 读取失败的 cgmk 绝不能变成一份“空但看起来可编辑”的配置：这里重跑一次 Parse 让错误浮出来，Session 没能装载就拒绝进入编辑。
        var configPath = WindowMotionConfig.Discover(Project);
        if (configPath != null)
        {
            WindowMotionConfig.Parse(File.ReadAllText(configPath), configPath);
            if (session.WindowMotion.Path == null)
                throw new InvalidDataException("Window config or its linked FILE could not be loaded. Repair that source before editing it.");
        }
        Windows = session.WindowMotion.Copy();
        // standalone 配置的事件直接在根上，内嵌配置则在固定的 EventsKey 下；类型不是数组说明这份 JSON 另有用途，不替换、直接报错。
        string eventsKey = Windows.Standalone ? "events" : WindowMotionConfig.EventsKey;
        if (Windows.Root[eventsKey] != null && Windows.Root[eventsKey] is not JsonArray)
            throw new InvalidDataException("Window event data is not an array; refusing to replace it.");
        Images = VspDocument.FromSession(session);
        foreach (var track in CustomText.Load(Project, new Chart(), enabled: true).Tracks) texts.Add(track.Id, track.SourceText);
        cleanTexts = TextSnapshot();
        cleanText = Vsm.Text; cleanWindows = Windows.Serialize(); cleanImages = Images.Text;
    }

    /// <summary>取一次全量快照作为撤销点。Lines 只复制数组本身，行对象是共享的不可变 record。</summary>
    Snapshot Capture(string label) => new(Vsm.Lines.ToArray(), Windows.Serialize(), markers.ToArray(), Images.Text, TextSnapshot(), label);
    /// <summary>整体回滚到某个快照，五份源必须一起换，缺一份就会出现文本与配置对不上的中间态。Revision 自增用于让上层重建缓存。</summary>
    void Restore(Snapshot s) { Vsm.Restore(s.Lines); Windows = WindowMotionConfig.Parse(s.Windows, Windows.Path); markers.Clear(); markers.AddRange(s.Markers); Images.Restore(s.ImageText); RestoreTexts(s.TextSources); Revision++; }
    /// <summary>
    /// 所有改动的唯一入口：先取快照，change 抛异常就整体回滚，保证文档不会停在改了一半的状态。
    /// 内容没有真正变化时不压栈，免得撤销栈被空操作填满；栈上限 64，超出丢最旧的一条。任何一次成功改动都会清空重做栈。
    /// </summary>
    public void Change(string label, Action change)
    {
        var before = Capture(label);
        try { change(); }
        catch { Restore(before); throw; }
        if (before.Lines.SequenceEqual(Vsm.Lines) && before.Windows == Windows.Serialize() && before.Markers.SequenceEqual(markers) && before.ImageText == Images.Text && before.TextSources == TextSnapshot()) return;
        undo.Add(before); if (undo.Count > 64) undo.RemoveAt(0);
        redo.Clear(); Revision++;
    }
    /// <summary>撤销：先把当前状态压进重做栈再回滚，标签跟着操作走，这样重做菜单显示的是同一次操作的名字。</summary>
    public void Undo()
    {
        if (!CanUndo) return;
        var last = undo[^1]; undo.RemoveAt(undo.Count - 1); redo.Add(Capture(last.Label)); Restore(last);
    }
    /// <summary>重做：与 Undo 完全对称，两边共用同一套快照，反复撤销/重做不会丢信息。</summary>
    public void Redo()
    {
        if (!CanRedo) return;
        var last = redo[^1]; redo.RemoveAt(redo.Count - 1); undo.Add(Capture(last.Label)); Restore(last);
    }
    /// <summary>在某一拍打标记，单位是拍。同一拍（容差 1e-9）已有标记就复用它的 Id，不制造重合的两个标记。</summary>
    public Guid Mark(double beat)
    {
        if (!double.IsFinite(beat) || Math.Abs(beat) > 1e8) throw new FormatException("Invalid marker beat.");
        var existing = markers.FirstOrDefault(m => Math.Abs(m.Beat - beat) <= 1e-9);
        if (existing != null) return existing.Id;
        int labelIndex = 1;
        while (markers.Any(m => m.Label == "M" + labelIndex)) labelIndex++;
        var marker = new TimelineMarker(Guid.NewGuid(), beat, "M" + labelIndex);
        Change("Mark beat " + VsmDocument.N(beat), () => markers.Add(marker));
        return marker.Id;
    }
    public void RemoveMarker(Guid id) => Change("Delete marker", () => markers.RemoveAll(m => m.Id == id));
    /// <summary>改标签不换 Id，也不动拍号。标签限单行 128 字以内，控制字符会破坏工程文件与时间轴绘制。</summary>
    public void RenameMarker(Guid id, string label)
    {
        if (label.Length > 128 || label.Any(char.IsControl)) throw new FormatException("Use a single-line label up to 128 characters.");
        Change("Rename marker", () => { int i = markers.FindIndex(m => m.Id == id); if (i >= 0) markers[i] = markers[i] with { Label = label }; });
    }
    /// <summary>就地替换第 index 条窗口事件。DeepClone 后再放入，避免调用方留着同一个 JsonObject 继续改动 DOM。</summary>
    public void ReplaceWindow(int index, JsonObject value)
    {
        var events = Windows.EnsureEvents();
        if (index < 0 || index >= events.Count) throw new InvalidOperationException("Window event no longer exists.");
        CheckWindow(value); events[index] = value.DeepClone();
        Windows.EnsureCount(WindowMotionConfig.Integer(value, "w") + 1);
    }
    /// <summary>追加一条窗口事件并返回它在数组中的下标；同时把窗口总数撑到能容纳 "w" 指定的索引。</summary>
    public int AddWindow(JsonObject value)
    {
        CheckWindow(value); Windows.EnsureCount(WindowMotionConfig.Integer(value, "w") + 1);
        var a = Windows.EnsureEvents(); a.Add(value.DeepClone()); return a.Count - 1;
    }
    /// <summary>写入前的取值校验：t 与 duration 单位是秒且必须有限，duration 不可为负；窗口索引固定 0..63，这是 ECG WindowMovement schema 的上限。</summary>
    static void CheckWindow(JsonObject e)
    {
        double t = WindowMotionConfig.Number(e, "t"), duration = WindowMotionConfig.Duration(e);
        if (!double.IsFinite(t) || !double.IsFinite(duration) || Math.Abs(t) > 1e8 || duration < 0 || duration > 1e8)
            throw new FormatException("Window timing must be finite; duration must be non-negative.");
        if (WindowMotionConfig.Integer(e, "w") is < 0 or > 63) throw new FormatException("Window index must be 0..63.");
    }

    /// <summary>
    /// 按时间给窗口事件排序后交出一份副本。ThenBy 原下标使排序稳定：同一时刻的事件保持作者写下的先后，不能重排——它们的先后决定最终生效的那一条。
    /// 未知字段随 DOM 一同保留，这里只是换顺序，不做裁剪。
    /// </summary>
    public WindowMotionConfig CompiledWindows()
    {
        var c = Windows.Copy();
        if (c.Events == null) return c;
        var ordered = c.Events.Select((node, i) => (node: node?.DeepClone(), i))
            .OrderBy(x => x.node is JsonObject e ? WindowMotionConfig.Number(e, "t") : double.PositiveInfinity)
            .ThenBy(x => x.i).ToArray();
        c.Events.Clear(); foreach (var item in ordered) c.Events.Add(item.node);
        return c;
    }

    /// <summary>
    /// 写出工程文件、VSM/cgmk 配置，以及本次编辑产生的 VSP 与图片伴生文件。全部内容先在内存里备齐，再一次性落盘；中途失败按提交的逆序回滚。
    /// 进程被强杀时 journal 与 .bak 会留在原地供恢复。这不是跨文件的原子事务，别当成事务来依赖。
    /// 保存只记录对外部素材的引用与设置，绝不复制或修改歌曲目录里的原始音频、封面与图片。
    /// </summary>
    public string SaveCopy(string path)
    {
        path = Path.GetFullPath(path);
        if (!path.EndsWith(".sgv.json", StringComparison.OrdinalIgnoreCase)) path += ".sgv.json";
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        string stem = Path.GetFileName(path)[..^9];
        string vsmPath = Path.Combine(dir, stem + ".editor.vsm");
        string windowsPath = Path.Combine(dir, stem + ".editor_cgmk_config.json");
        var outputWindows = CompiledWindows().AsInlineGameConfig();
        var p = Project.Copy(); p.EditorMarkers = markers.ToList(); p.Gimmick = vsmPath; p.WindowMotion = windowsPath;
        // 用正式的读取器把生成的 VSM 文本再解一遍，任何源文件落盘之前先确认它读得回来；解析成功不等于渲染成功，但解析失败一定不该写出去。
        var check = new Chart(); VsmReader.ReplaceModsText(check, Vsm.Text, vsmPath);
        if (check.Mods.Count == 0 && Vsm.Clips.Any()) throw new InvalidDataException("Generated VSM could not be read.");
        var files = new Dictionary<string, byte[]>();
        if (Images.HasContent)
        {
            string imageText = Images.PrepareSave(dir, stem, files);
            p.Images = Path.Combine(dir, stem + ".editor.vsp"); p.ImagePathsRelativeToVsp = true;
            files[p.Images] = Encoding.UTF8.GetBytes(imageText);
        }
        else { p.Images = null; p.ImagePathsRelativeToVsp = false; }
        p.TextFiles = new(StringComparer.Ordinal);
        int textIndex = 0;
        foreach (var pair in texts)
        {
            string target = Path.Combine(dir, stem + ".editor-texts", "text-" + textIndex++ + ".txt");
            files[target] = Encoding.UTF8.GetBytes(pair.Value); p.TextFiles.Add(pair.Key, target);
        }
        // 工程文件里一律存相对于自身目录的路径，整个工程文件夹可以随意搬动；解析基准是工程文件所在目录，不是当前工作目录。
        string? Rel(string? value) => value == null ? null : Path.GetRelativePath(dir, value);
        var jsonProject = p.Copy();
        jsonProject.Chart = Rel(p.Chart); jsonProject.Gimmick = Rel(p.Gimmick); jsonProject.WindowMotion = Rel(p.WindowMotion);
        jsonProject.Audio = Rel(p.Audio); jsonProject.Images = Rel(p.Images); jsonProject.Jacket = Rel(p.Jacket);
        jsonProject.FxProfile = Rel(p.FxProfile); jsonProject.GameUi = Rel(p.GameUi); jsonProject.GimmickAssets = Rel(p.GimmickAssets);
        jsonProject.GimmickDefinition = Rel(p.GimmickDefinition);
        jsonProject.TextFiles = p.TextFiles.ToDictionary(x => x.Key, x => Rel(x.Value)!);
        files[vsmPath] = Vsm.Bytes();
        files[windowsPath] = Encoding.UTF8.GetBytes(outputWindows.Serialize());
        files[path] = Encoding.UTF8.GetBytes(AppJson.Serialize(jsonProject, ViewerProject.Json));
        // 写任何一个文件之前把三条禁止项全查一遍：不许盖掉参考谱面；不许盖掉编辑器之外被改过的文件；不许盖掉不是本会话写出的同名伴生文件。
        foreach (string target in files.Keys)
        {
            if (Project.Chart != null && Path.GetFullPath(target) == Path.GetFullPath(Project.Chart))
                throw new IOException("Refusing to overwrite the reference chart.");
            if (File.Exists(target) && savedHashes.TryGetValue(target, out var hash) && Hash(target) != hash)
                throw new IOException("File changed outside the editor. Use Save As: " + target);
            if (File.Exists(target) && !savedHashes.ContainsKey(target))
                throw new IOException("A companion file already exists. Choose a new project name: " + target);
        }
        // 顺序不能改：先把全部临时文件写完，再写 .bak 备份，再写 journal，最后才逐个改名就位。journal 先于改名落盘，崩溃后才知道该回滚哪些目标。
        string token = Guid.NewGuid().ToString("N"), journal = path + ".save-journal.json";
        var backups = files.Keys.ToDictionary(f => f, f => File.Exists(f) ? File.ReadAllBytes(f) : null);
        var committed = new List<string>();
        try
        {
            foreach (var (target, bytes) in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target + ".tmp-" + token, bytes);
            }
            foreach (var (target, bytes) in backups)
                if (bytes != null) File.WriteAllBytes(target + ".bak", bytes);
            File.WriteAllText(journal, new System.Text.Json.Nodes.JsonObject
            {
                ["token"] = token,
                ["targets"] = new System.Text.Json.Nodes.JsonArray(files.Keys.Select(target =>
                    (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(target)).ToArray()),
                ["backupSuffix"] = ".bak"
            }.ToJsonString());
            foreach (var target in files.Keys) { File.Move(target + ".tmp-" + token, target, true); committed.Add(target); }
            File.Delete(journal);
        }
        catch
        {
            foreach (string target in committed.AsEnumerable().Reverse())
            {
                // 按提交的逆序回滚：原先存在的还原成备份内容，原先不存在的直接删掉。回滚本身再失败也不能吞掉原始异常。
                try { if (backups[target] is byte[] bytes) File.WriteAllBytes(target, bytes); else File.Delete(target); }
                catch (IOException) { /* 保留 journal 与 .bak 供事后手工恢复。 */ }
            }
            throw;
        }
        finally { foreach (string target in files.Keys) if (File.Exists(target + ".tmp-" + token)) File.Delete(target + ".tmp-" + token); }
        // 全部就位之后才更新哈希与四份 clean 基线，Dirty 随即变回 false；任何一步失败都不会走到这里，脏状态原样保留。
        foreach (string target in files.Keys) savedHashes[target] = Hash(target);
        Project = p; SavedProjectPath = path; cleanText = Vsm.Text; cleanWindows = Windows.Serialize(); cleanMarkers = markers.ToArray(); cleanImages = Images.Text; cleanTexts = TextSnapshot();
        return path;
    }
    /// <summary>整文件 SHA-256，只用来判断文件是否被编辑器之外改动过，不参与任何安全判定。</summary>
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
