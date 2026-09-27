using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KuroakiGimmick.Core.Documentation;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;

namespace KuroakiGimmick.Core;

/// <summary>16.0 创作与导出功能的 CPU / 文件系统测试。不创建 Host、SDL 设备、字体纹理或 GPU。</summary>
public static class AuthoringSelfTest
{
    /// <summary>
    /// 在工作目录下的临时子目录里完整走一遍"编辑 → 快照 → 导出"，finally 中整体删除；源谱面文件全程只读。
    /// </summary>
    public static int Run()
    {
        int count = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("AUTHORING TEST FAILED: " + label);
            count++; Console.WriteLine("PASS " + label);
        }
        // 只把这几类"被正常拒绝"的异常算作通过；其它异常照常冒泡，免得把真正的崩溃当成预期的拒绝。
        void Reject(Action action, string label)
        {
            bool rejected = false;
            try { action(); }
            catch (Exception e) when (e is IOException or FormatException or InvalidOperationException or OperationCanceledException)
            { rejected = true; }
            Check(rejected, label);
        }
        // 用当前源码目录而不是系统临时目录：macOS 上 /tmp 和 /var 都是符号链接别名，
        // 而导出器有意拒绝任何穿过目录符号链接的路径，放在临时目录里会被自身的安全检查挡掉。
        string temporary = Path.Combine(Environment.CurrentDirectory, ".kuroaki-authoring-test-" + Guid.NewGuid().ToString("N"));
        string song = Path.Combine(temporary, "song"), exports = Path.Combine(temporary, "exports");
        Directory.CreateDirectory(song); Directory.CreateDirectory(exports);
        try
        {
            string chart = Path.Combine(song, "ENCORE.vsc"), vsm = Path.Combine(song, "ENCORE.vsm");
            string config = Path.Combine(song, "ENCORE_cgmk_config.json");
            const string text = "!obj:obj_custom_gimmick\r\n!proxies:2\r\n!author:retained\r\nmods\r\n0,1,linear,_,1,velocity,-1\r\n// raw comment\r\nopaque::row\r\nmpf\r\n8,12,unknown_callback";
            byte[] original = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
            File.WriteAllBytes(vsm, original);
            File.WriteAllText(chart, "0,3,0,b:120\n1000,0,0\n4000,3,0,b:240\n5000,2,1,5500\n");
            File.WriteAllText(config, "{\"ENABLE_TEXT\":false,\"unrelated\":{\"keep\":42},\"ECG_WINDOW_MOVEMENT_EVENTS\":[]}");
            var project = new ViewerProject { Chart = chart, Gimmick = vsm, Profile = "core", GameUiEnabled = false };
            var session = new Session(project);
            var document = new EditorDocument(session);
            // 编辑器标记只存在于工程文件里：按同一时间戳重复打点不产生第二个标记，
            // 而且无论怎么改标记都不能回写游戏读的 VSM（那份字节必须保持导入时的原样）。
            Guid marker = document.Mark(128 + 1.0 / 3);
            Check(document.Dirty && document.Markers.Count == 1, "E marker changes editor document");
            Check(document.Mark(128 + 1.0 / 3) == marker && document.Markers.Count == 1, "same timestamp is not duplicated");
            Check(document.Vsm.Bytes().SequenceEqual(original), "marker does not write game VSM");
            // 撤销/重做必须保住标记的身份（Guid）和标签，不能重建成一个"看起来一样"的新标记，
            // 否则引用该 Guid 的界面选中状态会在一次撤销后失效。
            document.Undo(); Check(!document.Dirty && document.Markers.Count == 0, "undo marker returns clean document");
            document.Redo(); Check(document.Markers.Single().Id == marker, "redo keeps marker identity");
            document.RenameMarker(marker, "Chorus");
            document.RemoveMarker(marker); Check(document.Markers.Count == 0, "remove marked timestamp");
            document.Undo(); Check(document.Markers.Single().Label == "Chorus", "undo deletion restores label");
            Reject(() => document.Mark(double.NaN), "reject non-finite marker");
            Reject(() => document.RenameMarker(marker, "bad\nlabel"), "reject multiline label");
            // Copy() 必须深拷贝标记列表：浅拷贝会让副本上的编辑反过来污染原工程。
            var copy = project.Copy(); copy.EditorMarkers.Add(new(Guid.NewGuid(), 7, "Copy"));
            Check(project.EditorMarkers.Count == 0, "project copies do not alias marker list");

            // 新建事件的校验面：未知名字可以放行（用户可能在用新版 mod），但 `_` 哨兵要原样保留、
            // 分数按数值归一化成 0.5、非 ASCII 标识符（imgx_背景）不能被转义或截断。
            var custom = GimmickAuthoring.Create("brand_new_mod", 3.25, .5, "linear", "_", "1/2", -1, 2);
            Check(custom.To == "0.5" && custom.From == "_", "new identifier and fractional values validated");
            var image = GimmickAuthoring.Create("imgx_背景", 4, 0, "linear", "_", "128", -1, 2);
            Check(image.Name == "imgx_背景", "non-ASCII image identifiers retained");
            // 文档里的模板项写成 notealpind[lane]，创建时必须落成具体的 notealpind3；
            // 越界轨道号、没替换的 [image] 占位符都要拒绝，不能把模板字面量写进 VSM。
            var noteTemplate = VsmReference.Shared.Entries.First(e => e.Name == "notealpind[lane]");
            Check(GimmickAuthoring.Create("notealpind3", 1, 1, "linear", "_", "0", -1, 2, noteTemplate).Proxy == -1,
                "documentation lane template uses actual identifier");
            Reject(() => GimmickAuthoring.Create("notealpind9", 1, 1, "linear", "_", "0", -1, 2, noteTemplate), "reject invalid template lane");
            Reject(() => GimmickAuthoring.Create("imgx_[image]", 1, 1, "linear", "_", "0", -1, 2), "require concrete template suffix");
            // 名字里带逗号会把一行拆成错误的字段数——这是注入，必须在创建时就拒绝。
            Reject(() => GimmickAuthoring.Create("bad,name", 1, 1, "linear", "_", "0", -1, 2), "reject delimiter injection");
            // proxy 类 mod 必须绑定到已声明的 proxy 上：-1（全局）和超出 !proxies 声明数的下标都不合法。
            Reject(() => GimmickAuthoring.Create("prx", 1, 1, "linear", "_", "0", -1, 2), "proxy mod cannot target global");
            Reject(() => GimmickAuthoring.Create("prx", 1, 1, "linear", "_", "0", 2, 2), "undeclared proxy rejected");
            Reject(() => GimmickAuthoring.Create("velocity", 1, -1, "linear", "_", "0", -1, 2), "new negative duration rejected");
            Reject(() => GimmickAuthoring.Create("velocity", 1, 1, "notAnEase", "_", "0", -1, 2), "unknown easing rejected");
            // 改头部字段时只替换值本身：缩进、冒号后的空格、行尾注释这些 trivia 一律保留。
            var header = VsmDocument.FromText("  !obj: obj_base_gimmick // keep\r\n");
            header.SetHeader("obj", "obj_custom_gimmick");
            Check(header.Text == "  !obj: obj_custom_gimmick // keep\r\n", "explicit object change keeps header trivia");
            document.Change("Add new mod", () => document.Vsm.Add(custom));
            Check(document.Vsm.Find(custom.Id) != null && document.Dirty, "new track clip is authored and undoable");
            // Capture 取的是一份冻结快照：认不出的 opaque 行照样带走（同时给出提示），
            // 快照之后继续编辑（加 imgx_背景）不能倒灌进已捕获的内容——导出用的必须是点下按钮那一刻的文档。
            var input = ChartExportInput.Capture(document, session);
            Check(input.Notices.Length > 0 && input.VsmText.Contains("opaque::row"), "opaque imported rows preserved with notices");
            Check(input.Project.EditorMarkers.Single().Id == marker, "export snapshot retains markers in project metadata");
            document.Change("Later edit", () => document.Vsm.Add(image));
            Check(!input.VsmText.Contains("imgx_背景"), "export snapshot independent from later edits");

            // 导出写的是快照里的原始字节（含 BOM），并且导出不等于保存：脏标记保持、源 VSM 不被覆盖。
            // 三条拒绝分别守住：已存在的目标不覆盖、源文件不能当目标、Windows 保留设备名（CON/PRN/AUX…）不能作为文件名。
            string single = Path.Combine(exports, "single.vsm");
            var singlePlan = ChartExport.Prepare(input, ChartExportKind.Vsm, single);
            ChartExport.Write(singlePlan);
            Check(File.ReadAllBytes(single).SequenceEqual(input.VsmBytes) && singlePlan.Files.Count == 1, "VSM export uses frozen authored bytes including BOM");
            Check(document.Dirty && File.ReadAllBytes(vsm).SequenceEqual(original), "export neither marks saved nor overwrites source");
            Reject(() => ChartExport.Prepare(input, ChartExportKind.Vsm, single), "existing export not overwritten");
            Reject(() => ChartExport.Prepare(input, ChartExportKind.Vsm, vsm), "source VSM protected");
            Reject(() => ChartExport.Prepare(input, ChartExportKind.Vsm, Path.Combine(exports, "CON.vsm")), "Windows reserved output name rejected");
            // 成对导出会一起写出 cgmk 配置：里面与本工具无关的键（unrelated 及其嵌套值）必须原样带过去，
            // 那可能是游戏或其它 mod 的设置，重新序列化时丢掉等于静默改了用户配置。
            string pair = Path.Combine(exports, "pair.vsm");
            ChartExport.Write(ChartExport.Prepare(input, ChartExportKind.VsmAndConfig, pair));
            var written = JsonNode.Parse(File.ReadAllText(Path.Combine(exports, "pair_cgmk_config.json")))!;
            Check(written["ENABLE_TEXT"]?.GetValue<bool>() == false && written["unrelated"]?["keep"]?.GetValue<int>() == 42,
                "paired config retains unrelated options and nested values");
            Check(File.ReadAllBytes(pair).SequenceEqual(input.VsmBytes), "paired VSM is exactly current authored file");
            // 独立的窗口运动 JSON 要在导出时并进歌曲的 cgmk 配置里，两边的键都不能互相覆盖。
            string standalonePath = Path.Combine(song, "window_movement.json");
            File.WriteAllText(standalonePath, "{\"format\":\"ExtCustomGimmick.WindowMovement\",\"windowCount\":2,\"events\":[]}");
            var standaloneProject = project.Copy(); standaloneProject.WindowMotion = standalonePath;
            var standaloneSession = new Session(standaloneProject);
            var standaloneDoc = new EditorDocument(standaloneSession);
            var inline = JsonNode.Parse(ChartExportInput.Capture(standaloneDoc, standaloneSession).ConfigText)!;
            Check(inline["ENABLE_TEXT"]?.GetValue<bool>() == false && inline[WindowMotionConfig.CountKey]?.GetValue<int>() == 2,
                "separate window JSON merged with song cgmk settings");

            // 整曲文件夹导出的收录规则用一组真实文件钉死：
            // 收 —— 谱面（含其它难度）、字幕、SV、音频，以及 VSP 里引用到歌曲目录之外的贴图（复制进 resources/ 并把路径改写成相对引用，不留 ../）；
            // 不收 —— .DS_Store 之类的 Finder 垃圾和 bin 缓存目录。
            string external = Path.Combine(temporary, "external.png"), vsp = Path.Combine(song, "ENCORE.vsp");
            File.WriteAllBytes(external, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a4E8AAAAASUVORK5CYII="));
            File.WriteAllText(vsp, "#Layer\nBG,0\n#Image\nBG:\nstatic,background,../external.png,0,320,180\n");
            File.WriteAllText(Path.Combine(song, "FINALE.vsc"), "other difficulty\n");
            File.WriteAllBytes(Path.Combine(song, "music.ogg"), [5, 6, 7]);
            File.WriteAllText(Path.Combine(song, "ENCORE_text.txt"), "1,Subtitle\n");
            File.WriteAllText(Path.Combine(song, "ENCORE.vsv"), "addVelo(0,1)\n");
            File.WriteAllText(Path.Combine(song, ".DS_Store"), "ignored");
            Directory.CreateDirectory(Path.Combine(song, "bin")); File.WriteAllText(Path.Combine(song, "bin", "cache"), "ignored");
            string folder = Path.Combine(exports, "chart");
            var folderPlan = ChartExport.Prepare(input with { Images = vsp }, ChartExportKind.ChartFolder, folder);
            ChartExport.Write(folderPlan);
            Check(File.ReadAllBytes(Path.Combine(folder, "ENCORE.vsc")).SequenceEqual(File.ReadAllBytes(chart)), "chart file retained byte-for-byte");
            Check(File.ReadAllText(Path.Combine(folder, "FINALE.vsc")) == "other difficulty\n", "other difficulty retained");
            Check(File.Exists(Path.Combine(folder, "ENCORE_text.txt")) && File.Exists(Path.Combine(folder, "ENCORE.vsv")) && File.Exists(Path.Combine(folder, "music.ogg")), "subtitles, SV and music included");
            Check(!File.Exists(Path.Combine(folder, ".DS_Store")) && !Directory.Exists(Path.Combine(folder, "bin")), "cache and Finder files excluded");
            Check(Directory.GetFiles(Path.Combine(folder, "resources")).Single().EndsWith("_external.png"), "external VSP resource collected");
            Check(File.ReadAllText(Path.Combine(folder, "ENCORE.vsp")).Contains("resources/") && !File.ReadAllText(Path.Combine(folder, "ENCORE.vsp")).Contains("../"), "known VSP resource reference rebased");
            var reopenedProject = AppJson.Deserialize<ViewerProject>(File.ReadAllText(Path.Combine(folder, "Kuroaki.sgv.json")), ViewerProject.Json)!;
            Check(reopenedProject.Gimmick == "ENCORE.vsm" && reopenedProject.EditorMarkers.Single().Id == marker, "folder project uses relative companions and editor markers");
            // 文件夹导出永远不与已存在的目标合并（避免留下上一次的残余文件），也不许把目标放进源目录里递归自吞。
            Reject(() => ChartExport.Prepare(input, ChartExportKind.ChartFolder, folder), "folder export never merges an existing destination");
            Reject(() => ChartExport.Prepare(input, ChartExportKind.ChartFolder, Path.Combine(song, "nested")), "recursive source-folder export rejected");
            // 必需资源缺失要在 Prepare 阶段（预检）就失败，而不是写到一半才发现。
            string missing = Path.Combine(song, "missing.vsp");
            File.WriteAllText(missing, "static,bg,does-not-exist.png,0\n");
            Reject(() => ChartExport.Prepare(input with { Images = missing }, ChartExportKind.ChartFolder, Path.Combine(exports, "missing")), "missing required resource fails preflight");
            File.Delete(missing);
            // Prepare 与 Write 之间源文件被改动，Write 必须靠完整性校验失败并且不发布任何东西：
            // 导出是"暂存目录 + 原子提交"，失败时连半个目标目录都不该出现。
            var changed = ChartExport.Prepare(input with { Images = vsp }, ChartExportKind.ChartFolder, Path.Combine(exports, "changed"));
            File.AppendAllText(Path.Combine(song, "FINALE.vsc"), "changed after planning");
            Reject(() => ChartExport.Write(changed), "source change after planning fails integrity check");
            Check(!Directory.Exists(changed.Destination), "failed integrity check does not publish partial folder");
            // 取消要在提交之前停下，并把 .kuroaki-export-* 暂存目录清理干净，不留垃圾。
            var cancelled = ChartExport.Prepare(input, ChartExportKind.Vsm, Path.Combine(exports, "cancelled.vsm"));
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel(); Reject(() => ChartExport.Write(cancelled, cancel.Token), "cancelled export stops before commit");
            }
            Check(!File.Exists(cancelled.Destination) && !Directory.EnumerateDirectories(exports, ".kuroaki-export-*").Any(), "cancellation clears staging");
            // 另存后重开：标记数据要逐字段还原，且新文档必须是干净状态（不能一打开就显示未保存）。
            string saved = document.SaveCopy(Path.Combine(temporary, "saved.sgv.json"));
            var savedSession = Session.Load(saved); var savedDocument = new EditorDocument(savedSession);
            Check(savedDocument.Markers.Single().Label == "Chorus" && !savedDocument.Dirty, "save/reopen preserves exact marker data and clean state");
            Console.WriteLine($"{count} authoring/export checks passed. GPU/UI interaction and target-game playback are separate tests.");
            return 0;
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
}
