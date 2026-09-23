using System.Text;
using System.Text.Json.Nodes;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;

namespace KuroakiGimmick.Core;

/// <summary>纯 CPU 的编辑器回归测试。用 --editor-self-test 启动；不创建 SDL 窗口，也不加载原生 ECG DLL。</summary>
public static class EditorSelfTest
{
    /// <summary>
    /// 先把排版与引用两个子套件的检查数折进来，再跑编辑器自身的检查；失败直接抛异常。
    /// 全部编辑发生在临时目录的副本上，不写用户工程。
    /// </summary>
    public static int Run()
    {
        int count = HelpTypographySelfTest.Run() + ReferenceSelfTest.Checks();
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException("EDITOR TEST FAILED: " + label);
            count++; Console.WriteLine("PASS " + label);
        }
        bool Near(double a, double b) => Math.Abs(a - b) < 1e-8;
        string dir = Path.Combine(Path.GetTempPath(), "kuroaki-editor-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // 这份 fixture 每一处"脏"都是故意的：UTF-8 BOM、CRLF 行尾、字段间多余空格、
            // 0.000 / 1e+1 这类等价但不同的写法、行尾 // KEEP 注释、认不出的 unknown:: 行、以及 mpf 段。
            // 编辑器是逐行结构化改写而非重新序列化，未触碰的部分必须逐字节原样保留。
            const string source = "!obj:obj_custom_gimmick\r\n!proxies:2\r\n!author:retained\r\nmods\r\n  0 , 1 , linear , 0.000 , 1e+1 , prx , 0 // KEEP\r\n2:4:1,0.25,outSine,_,20,pry,1\r\nunknown::source-line\r\nmpf\r\n8,12,unregistered_callback";
            string vsmPath = Path.Combine(dir, "ENCORE.vsm"), chartPath = Path.Combine(dir, "ENCORE.vsc");
            byte[] original = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(source)).ToArray();
            File.WriteAllBytes(vsmPath, original);
            File.WriteAllText(chartPath, "0,3,0,b:120\n1000,0,0\n4000,3,0,b:240\n5000,2,1,5500\n");
            var doc = VsmDocument.Load(vsmPath);
            var loopEditor = new EditorDocument(Session.Load(chartPath));
            Guid loopId = loopEditor.Vsm.Clips.Last().Id;
            Guid[] expanded = [];
            loopEditor.Change("Split loop", () => expanded = loopEditor.Vsm.SplitLoops([loopId]));
            string splitText = loopEditor.Vsm.Text;
            Check(expanded.Length == 3 && loopEditor.Dirty, "loop split is a document edit");
            loopEditor.Undo();
            Check(loopEditor.Vsm.Bytes().SequenceEqual(original) && !loopEditor.Dirty, "loop split undo restores exact source bytes and clean state");
            loopEditor.Redo();
            Check(loopEditor.Vsm.Text == splitText && expanded.All(id => loopEditor.Vsm.Find(id) != null), "loop split redo restores the same event identities");
            loopEditor.Change("Merge loop", () => loopEditor.Vsm.MergeLoop(expanded));
            Check(loopEditor.Vsm.Find(loopId)?.RepeatCount == 3, "merge records a loop as one undoable edit");
            loopEditor.Undo();
            Check(loopEditor.Vsm.Text == splitText, "merge undo restores every separate event");
            Check(doc.Bytes().SequenceEqual(original), "unmodified UTF-8 BOM / CRLF / source bytes round trip");
            // 区间行 2:4:1 在编辑器里是一个可编辑片段（重复 3 次），不是展开后的三行：
            // 展开会让作者失去原来的书写形式，保存时也回不去。
            Check(doc.Clips.Count() == 2 && doc.Clips.Last().RepeatCount == 3, "repeat source row remains one editable clip");
            var first = doc.Clips.First();
            doc.Replace(first with { Beat = .5 });
            Check(doc.Text.Contains("  0.5 , 1 , linear , 0.000 , 1e+1 , prx , 0 // KEEP"), "timing edit preserves untouched numeric spellings and comments");
            Check(doc.Text.Contains("unknown::source-line") && doc.Text.EndsWith("8,12,unregistered_callback"), "opaque rows and mpf survive a structured edit");
            // 拍值支持 "整数+分数" 与科学计数法两种写法，解析结果必须区分开；1/0 这类非有限值要拒绝而不是变成 Infinity。
            Check(Near(VsmDocument.Number("128+1/3"), 128 + 1.0 / 3) && Near(VsmDocument.Number("1e+5"), 100000), "fractional beats and scientific notation are distinct");
            bool rejected = false;
            try { VsmDocument.Number("1/0"); } catch (FormatException) { rejected = true; }
            Check(rejected, "non-finite numeric input is rejected");
            // 文件末尾已经是 mpf 段，追加效果事件必须插到 mpf 那一行之前：原版 read_mods_file 的 mode
            // 只能 mods→mpf 单向切换，写 "mods" 回去只会被当成事件行切分然后越界崩掉。
            // 哨兵起点 `_` 要原样写出，1/2 这类分数按数值写成 0.5。
            doc.Add(new(Guid.NewGuid(), 16, 1, "linear", "_", "1/2", "pra", 0));
            Check(doc.Text.Contains("\r\n16,1,linear,_,0.5,pra,0\r\nmpf\r\n") && !doc.Text.Contains("\r\nmods\r\n16"),
                "adding after mpf inserts ahead of it instead of writing an unsupported mods marker");
            // 编辑结果必须能被生产用的读取器原样消费：5 条效果事件 + 1 条逐帧绑定，旧段落一条都没丢。
            var parsed = new Chart(); VsmReader.ReplaceModsText(parsed, doc.Text, "memory.vsm");
            Check(parsed.Mods.Count == 5 && parsed.PerFrame.Count == 1, "production reader consumes edited text without discarding old sections");
            // 没有 VSM 源文件时生成的文本也得能直接喂给原版：不写 "mods" 标记、不写注释，mpf 段排在最后。
            var generated = VsmDocument.FromChart(parsed);
            Check(!generated.Text.Split('\n').Any(l => l.Trim() == "mods") && !generated.Text.Contains("//")
                && generated.Text.StartsWith("!obj:obj_custom_gimmick\n!proxies:2\n!author:retained\n0.5,"),
                "a generated VSM omits the mods marker and comments that crash read_mods_file");
            var reread = new Chart(); VsmReader.ReplaceModsText(reread, generated.Text, "generated.vsm");
            Check(reread.Mods.Count == 5 && reread.PerFrame.Count == 1 && !reread.Diagnostics.Any(),
                "the generated text round trips through the production reader without diagnostics");
            // 读的时候照旧宽容，但这两种原版会崩的写法必须报出来，否则作者只有进游戏才发现打不开。
            var crashy = new Chart();
            VsmReader.ReplaceModsText(crashy, "!obj:obj_custom_gimmick\nmods\n0,0,linear,0,1,prx,-1 // note\n", "crashy.vsm");
            Check(crashy.Mods.Count == 1 && crashy.Diagnostics.Count(d => d.Error) == 2
                && crashy.Diagnostics.Any(d => d.Line == 2 && d.Message.Contains("bare \"mods\" line"))
                && crashy.Diagnostics.Any(d => d.Line == 3 && d.Message.Contains("never strips // comments")),
                "a bare mods line and a comment are each reported as an original-parser crash");

            var project = new ViewerProject { Chart = chartPath, Gimmick = vsmPath, Profile = "core", GameUiEnabled = false };
            var session = new Session(project);
            var edit = new EditorDocument(session);
            var entry = edit.Vsm.Clips.First();
            edit.Change("Move one clip", () => edit.Vsm.Replace(entry with { Beat = 1 }));
            Check(edit.Dirty && edit.CanUndo, "committed operation becomes dirty and undoable");
            // 撤销要退回到逐字节相同的原文档（含 BOM 与 CRLF）并清掉脏标记，不能只是"值回去了"。
            edit.Undo(); Check(!edit.Dirty && edit.Vsm.Bytes().SequenceEqual(original), "undo returns exact original document");
            edit.Redo(); Check(edit.Dirty && Near(edit.Vsm.Clips.First().Beat, 1), "redo restores the authored change");
            edit.Change("Window movement", () => edit.AddWindow(WindowMotionConfig.NewEvent("NewWindowDance", 1, .5, 1)));
            // 另存为副本：导入的原始 VSM 必须原封不动，编辑结果写到 *.editor.vsm 与工程文件里。
            string save = edit.SaveCopy(Path.Combine(dir, "edited.sgv.json"));
            Check(!edit.Dirty && File.ReadAllBytes(vsmPath).SequenceEqual(original), "save copy does not overwrite the imported VSM");
            var reopened = Session.Load(save);
            Check(reopened.WindowMotion.Events?.Count == 1 && Near(reopened.Chart.Mods.First().Beat, 1), "saved project reopens both edited VSM and ECG config");
            // 模拟别的程序改了已保存的 VSM：再次保存必须抛 IOException 提醒冲突，不能直接覆盖掉外部改动。
            edit.SaveCopy(save);
            File.AppendAllText(Path.Combine(dir, "edited.editor.vsm"), "\n// external change");
            rejected = false;
            try { edit.SaveCopy(save); } catch (IOException) { rejected = true; }
            Check(rejected, "external modification is detected instead of silently overwritten");
            // fixture 的 VSC 在 0 ms 声明 120 BPM、4000 ms 处切到 240 BPM：
            // 第 8 拍落在 4 秒（120 BPM 走 8 拍），第 12 拍落在 5 秒（240 BPM 再走 4 拍），跨段往返也要闭合。
            var map = session.Timeline.Bpm;
            Check(Near(map.Time(8), 4) && Near(map.Time(12), 5) && Near(map.Beat(map.Time(9.25)), 9.25), "note beat mapping crosses BPM changes without changing seconds");

            // 新建事件必须一次写全该操作在 GML 侧要求的所有字段：原生 ECG 读到缺字段不会补默认值，
            // 而是直接按未初始化处理，所以这里逐个操作对字段清单。
            foreach (string op in WindowMotionConfig.Operations)
            {
                var e = WindowMotionConfig.NewEvent(op, 1, .5, 0);
                Check(e["op"]?.ToString() == op && e.ContainsKey("t") && e.ContainsKey("overlap"), "complete event base: " + op);
                string[] fields = op switch
                {
                    "NewWindowDance" => ["preset", "same", "x", "y", "ux", "uy", "angle", "uangle", "ax", "ay", "uax", "uay", "speed", "freq", "period", "subEase", "easeType", "reference", "easeDur", "ease"],
                    "WindowResize" => ["sx", "sy", "usx", "usy", "px", "py", "upx", "upy", "pivotMode", "anchor", "dur", "ease"],
                    "HideWindow" => ["show"], "SetWindowContent" => ["room"], _ => ["order"]
                };
                Check(fields.All(e.ContainsKey), "GML event fields: " + op);
            }
            // 每个窗口舞蹈预设都跑一次"向前推进 → 跳到远处 → 跳回原时刻"：取到的位姿必须完全相等。
            // 预设里有周期运动和随机量，容易写成依赖上一帧状态的增量积分，那样拖进度条就会飘。
            foreach (string preset in WindowMotionConfig.Presets)
            {
                var motion = WindowMotionConfig.Empty(); motion.EnsureCount(2);
                var e = WindowMotionConfig.NewEvent("NewWindowDance", 1, .5, 1);
                e["preset"] = preset; e["x"] = .3; e["y"] = .6; e["ax"] = .05; e["ay"] = .07; e["freq"] = 2; e["speed"] = .1;
                motion.EnsureEvents().Add(e);
                motion.EnsureEvents().Add(WindowMotionConfig.NewEvent("HideWindow", 1, 0, 1));
                var timeline = new WindowMotionTimeline(motion);
                var forward = timeline.At(2.75).Single(p => p.Id == 100);
                timeline.At(20); timeline.At(.25);
                var replay = timeline.At(2.75).Single(p => p.Id == 100);
                Check(forward == replay && forward.Visible && double.IsFinite(forward.X + forward.Y), preset + ": deterministic backward seek and correct window id");
            }
            // 独立的 WindowMovement 文件转成内联 ECG 配置时，不认识的键（unrelated）必须整块带走：
            // 那可能是新版 ECG 的字段，丢掉等于静默降级用户的配置。
            var standalone = WindowMotionConfig.Parse("{\"format\":\"ExtCustomGimmick.WindowMovement\",\"windowCount\":2,\"events\":[],\"unrelated\":{\"keep\":42}}").AsInlineGameConfig();
            Check(standalone.Root["unrelated"]?["keep"]?.ToString() == "42" && standalone.Root[WindowMotionConfig.CountKey]?.ToString() == "2" && !standalone.Standalone, "standalone export becomes inline ECG config without losing unknown data");
            // 主窗口 id 固定为 0，附加窗口从 100 起连号——原生适配层按这套 id 寻址，不能改成 0/1/2。
            // 默认宽度是相对 1920 屏宽的归一化比例 704/1920，与 ECG 的默认窗口尺寸一致。
            var regular = WindowMotionConfig.Empty(); regular.EnsureCount(3);
            var poses = new WindowMotionTimeline(regular).At(0);
            Check(poses.Select(p => p.Id).SequenceEqual(new[] { 0, 100, 101 }) && Near(poses[0].Width, 704.0 / 1920), "default window geometry and native ids match the adapter contract");
            // 剪贴板信封：事件以 VSM 源行形式携带，与源文件共用同一套格式和解析器，往返必须逐字段一致。
            var copied = new[]
            {
                new VsmDocument.Clip(Guid.NewGuid(), 4, 2, "linear", "0", "1", "prx", -1),
                new VsmDocument.Clip(Guid.NewGuid(), 8, 0, "inOutSine", "_", "0.5", "przm", 3, 12, 2, true)
            };
            string envelope = EditorClipboard.Write(copied, []);
            var restored = EditorClipboard.Read(envelope)!;
            Check(restored.Beat == 4 && restored.Events.Length == 2
                && restored.Events.Zip(copied).All(p => p.First.Beat == p.Second.Beat && p.First.Duration == p.Second.Duration
                    && p.First.Ease == p.Second.Ease && p.First.From == p.Second.From && p.First.To == p.Second.To
                    && p.First.Name == p.Second.Name && p.First.Proxy == p.Second.Proxy
                    && p.First.RepeatEnd == p.Second.RepeatEnd && p.First.RepeatStep == p.Second.RepeatStep
                    && p.First.ParenthesizedRepeat == p.Second.ParenthesizedRepeat),
                "clipboard round-trip preserves every clip field, including the repeat form");
            Check(restored.Events.All(c => copied.All(o => o.Id != c.Id)), "pasted events never reuse the copied event identity");
            // 信封是纯文本 JSON，因此可以在两个实例之间、甚至通过聊天工具转发后粘贴。
            Check(envelope.TrimStart().StartsWith('{') && envelope.Contains("\"kuroaki\": \"clipboard/1\"")
                && envelope.Contains("4,2,linear,0,1,prx,-1"), "clipboard payload is portable text carrying VSM source rows");
            // 原版 read_mods_file 见到 "mods" 行或注释就会崩，复制方向绝不能产出这两者。
            Check(!envelope.Split('\n').Any(l => l.Trim() is "mods" or "mpf" || l.TrimStart().StartsWith("//")),
                "clipboard never emits a bare mods header or comment rows");
            // 从文本编辑器直接贴一段裸 VSM 事件行也要能收下，不必先包成信封。
            var bare = EditorClipboard.Read("16,1,linear,0,1,pry,-1\n20,1,linear,1,0,pry,-1\n")!;
            Check(bare.Events.Length == 2 && bare.Beat == 16, "bare VSM rows paste without an envelope");
            Check(EditorClipboard.Read("hello world") == null && EditorClipboard.Read("{\"kuroaki\":\"clipboard/9\"}") == null
                && EditorClipboard.Read("") == null && EditorClipboard.Read(new string('x', EditorClipboard.MaxLength + 1)) == null,
                "foreign, future-version, empty and oversized clipboard text is refused instead of guessed");
            var windowClip = EditorClipboard.Read(EditorClipboard.Write([], [WindowMotionConfig.NewEvent("HideWindow", 1, 0, 1)]))!;
            Check(windowClip.Windows.Length == 1 && windowClip.Events.Length == 0, "window events travel through the clipboard too");
            Console.WriteLine($"EDITOR SELF-TEST PASSED: {count} checks. Native GPU/compositor behavior is a separate platform test.");
            return 0;
        }
        finally { Directory.Delete(dir, true); }
    }
}
