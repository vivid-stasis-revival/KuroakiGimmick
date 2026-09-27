using System.Numerics;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>
/// 文本对象编辑与 fx_film 的回归。核心不变量是"绝不改用户的原始文件"：所有编辑都在内存文档里，
/// 导出/另存写到独立目录，最后逐字节回查原 .vsm 和 _text_ 文件没被动过。
/// </summary>
public static class TextFilmSelfTest
{
    /// <summary>临时目录建在当前目录下而不是 /tmp：资源解析会拒绝 macOS 上 /tmp 那种符号链接父目录。</summary>
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("TEXT/FILM TEST: " + label); checks++; Console.WriteLine("PASS " + label); }
        void Reject(Action action, string label)
        {
            bool rejected = false;
            try { action(); } catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException or FormatException) { rejected = true; }
            Check(rejected, label);
        }
        string temporary = Path.Combine(Environment.CurrentDirectory, ".kuroaki-text-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string song = Path.Combine(temporary, "song"), output = Path.Combine(temporary, "output");
            Directory.CreateDirectory(song); Directory.CreateDirectory(output);
            string chart = Path.Combine(song, "ENCORE.vsc"), vsm = Path.Combine(song, "ENCORE.vsm"), txt = Path.Combine(song, "ENCORE_text_字幕.txt");
            // 原始文本刻意塞进四种"容易被顺手清理掉"的东西：行首注释、值里的逗号、{n} 换行标记、CJK，
            // 以及一行解析器认不出的内容（opaque source row）。CRLF 也必须原样保留。
            const string original = "// preserve comment\r\n0,Hello, world{n}你好\r\nopaque source row\r\n8,End\r\n";
            const string vsmSource = "!obj:obj_custom_gimmick\n!proxies:1\nmods\n0,0,linear,0,0,fx_film,-1\n4,0,linear,1,1,fx_film,-1\n8,0,linear,0,0,fx_film,-1\n";
            // 谱面在 2000 ms 处从 120 BPM 变成 240 BPM，后面用来验证动画时长取的是起始 BPM 而不是结束 BPM。
            File.WriteAllText(chart, "0,3,0,b:120\n2000,3,0,b:240\n8000,0,0\n");
            File.WriteAllText(vsm, vsmSource); File.WriteAllText(txt, original);
            var session = Session.Load(chart); var document = new EditorDocument(session); var map = session.Timeline.Bpm;
            Check(document.TextSources["字幕"] == original, "load text objects with comments and original line endings");
            // 编辑器读出的内容要和实际渲染用的是同一个 reader：{n} 已经变成真正的换行。
            Check(document.EditableTexts().Tracks.Single().At(0) == "Hello, world\n你好", "named Unicode text and multiline content use the production reader");
            document.SetTextCue("字幕", 2, "New,文字\nsecond line");
            Check(document.EditableTexts().Tracks.Single().At(2) == "New,文字\nsecond line", "edit content without manual text files");
            // 解析不了的那一行必须原样留在源文本里，不能因为"编辑器不认识"就删掉作者写的东西。
            Check(document.TextSources["字幕"].Contains("opaque source row\r\n"), "unparsed text source remains preserved");
            // 一次编辑对应一次撤销，且撤销后源文本要逐字节复原（含注释和 CRLF），Dirty 也要跟着清掉。
            document.Undo(); Check(!document.Dirty && document.TextSources["字幕"] == original, "one undo restores source text exactly");
            document.Redo(); document.MoveTextCue("字幕", 2, 3);
            Check(document.EditableTexts().Tracks.Single().At(2) == "Hello, world\n你好" && document.EditableTexts().Tracks.Single().At(3).StartsWith("New,"), "move content cue without rewriting other cue times");
            // 拍 8 上已经有一条 cue，移过去会覆盖别人，必须拒绝而不是静默合并。
            Reject(() => document.MoveTextCue("字幕", 3, 8), "moving onto another cue is rejected");
            // 新建文本对象要同时产生 VSP 侧的文本源和 VSM 侧的初始 pose，并且算作一次撤销。
            string id = document.AddText("New object", 0, true);
            Check(document.TextSources.Count == 2 && document.Vsm.Clips.Any(c => c.Name == "textX_" + id), "add text object and initial pose together");
            document.Undo(); Check(document.TextSources.Count == 1 && !document.Vsm.Clips.Any(c => c.Name == "textX_" + id), "add object is a single undo action");
            document.Redo();
            document.SetTextProperties(id, 0, new Dictionary<string, double> { ["textX"] = 80, ["textY"] = 60 }, map);
            Check(new TextValueSampler(document.Vsm, map).Get("textX", id, 0) == 80, "grouped placement updates actual VSM values");
            string before = document.Vsm.Text;
            // 分组编辑是原子的：textalp=3 越界会让整组失败，已经写进去的 textX 必须回滚，
            // 不能留下"X 动了、透明度没动"的半成品状态。
            Reject(() => document.SetTextProperties(id, 0, new Dictionary<string, double> { ["textX"] = 99, ["textalp"] = 3 }, map), "invalid property rejects the whole edit");
            Check(document.Vsm.Text == before, "failed grouped edit rolls back its earlier channel");
            // 从拍 2 到拍 2+4 做一段动画。该区间跨过 2000 ms 处的 BPM 变化，按起始 BPM（120）算时长，
            // 因此拍 4 正好是中点，textX 从 80 插值到 180 得 130。按结束 BPM 算会得到另一个值。
            document.SetTextProperties(id, 2, new Dictionary<string, double> { ["textX"] = 180, ["textY"] = 100 }, map, 4, "linear");
            Check(Math.Abs(new TextValueSampler(document.Vsm, map).Get("textX", id, 4) - 130) < 1e-6, "text animation uses start-BPM duration across BPM changes");
            // 在画布上拖动动画端点要改回同一条已存在的 clip，而不是再插一条新的把旧的盖住。
            var animation = document.Vsm.Clips.Single(c => c.Name == "textX_" + id && c.Duration > 0);
            document.SetTextAnimationEndpoint(id, animation.Id, true, new Dictionary<string, double> { ["textX"] = 200, ["textY"] = 110 });
            Check(document.Vsm.Find(animation.Id)!.To == "200", "canvas endpoint editing updates the existing animation");
            document.Undo(); Check(document.Vsm.Find(animation.Id)!.To == "180", "animation endpoint edit is undoable");
            // 拍 3 落在上面那段动画内部：在动画中间插关键帧会把动画切断，因此拒绝而不是默默改写。
            Reject(() => document.SetTextProperties(id, 3, new Dictionary<string, double> { ["textX"] = 42 }, map), "overlapping key does not destroy an existing animation");
            // 手工加一条同名的原始 clip（模拟作者自己写的动态写法）后，结构化编辑必须让位：
            // 这条通道已经不是编辑器能安全重排的形状了，保持原始文本比"帮他整理"更重要。
            document.Change("dynamic test", () => document.Vsm.Add(new(Guid.NewGuid(), 12, 1, "linear", "_", "1", "textalp_" + id, -1)));
            Reject(() => document.SetTextProperties(id, 0, new Dictionary<string, double> { ["textalp"] = .5 }, map), "dynamic text channels remain raw");
            document.Undo();
            string saved = document.SaveCopy(Path.Combine(output, "saved.sgv.json"));
            var reopened = Session.Load(saved);
            Check(reopened.Texts.Tracks.Count == 2 && reopened.Texts.Tracks.Single(t => t.Id == "字幕").At(3).StartsWith("New,"), "save/reopen resolves explicit companion text files");
            Check(reopened.Project.TextFiles!.Values.All(File.Exists), "all saved text companions exist");
            // 导出用的是内存里未保存的编辑（拍 6 那条刚加完就没存），并且要落成游戏认得的文件名 ENCORE_text_<id>.txt。
            document.SetTextCue(id, 6, "Unsaved export");
            var input = ChartExportInput.Capture(document, reopened);
            // 只导 VSM 时字幕不会跟着走，必须明确警告，而不是让作者以为导全了。
            var vsmOnly = ChartExport.Prepare(input, ChartExportKind.Vsm, Path.Combine(output, "only.vsm"));
            Check(vsmOnly.Warnings.Any(n => n.Contains("subtitle")), "VSM-only export warns about excluded edited subtitles");
            string folder = Path.Combine(output, "chart-folder"); ChartExport.Write(ChartExport.Prepare(input, ChartExportKind.ChartFolder, folder));
            Check(File.ReadAllText(Path.Combine(folder, "ENCORE_text_" + id + ".txt")).Contains("6,Unsaved export"), "Chart Folder contains unsaved text edits under game-compatible names");
            Check(Session.Load(Path.Combine(folder, "Kuroaki.sgv.json")).Texts.Tracks.Single(t => t.Id == id).At(6) == "Unsaved export", "exported project reopens with the same text");
            // 全流程收尾：上面做了编辑、撤销、另存、两种导出，用户的两个原始文件必须逐字节没变。
            Check(File.ReadAllText(txt) == original && File.ReadAllText(vsm) == vsmSource, "original text and VSM files never change");
            var film = session.Fx.Find("FX_film");
            // 胶片层在原版房间里初始隐藏，贴图随程序分发；Filter 名沿用原版的 _filter_old_film。
            Check(film is { Filter: "_filter_old_film", Visible: false } && File.Exists(film.TexturePath), "original hidden film layer and texture are available");
            // FX_film 属于基础层：判定只看 fx_film 的取值，与"是否 Custom 对象"和 ENABLE_NON_BASE_FX 都无关，
            // 所以这里第 2、3 个参数怎么传都不该改变结果。
            bool Film(double beat) => RoomFxState.IsForegroundActive(film!, true, false, false, n => session.Timeline.Get(n, map.Time(beat)));
            // vsmSource 在拍 0/4/8 分别写 0/1/0。最后再问一次拍 4 是故意的：往回跳必须重新算出"开"，
            // 不能依赖上一次查询留下的状态。
            Check(!Film(0) && Film(4) && !Film(8) && Film(4), "film switch supports enable, disable and backward seek independently of non-base FX");
            Check(!session.Chart.Diagnostics.Any(d => d.Message.Contains("Unsupported mod: fx_film")), "film is no longer reported as unsupported");
            Console.WriteLine($"{checks} text/film CPU checks passed."); return 0;
        }
        finally { Directory.Delete(temporary, true); }
    }

    /// <summary>需要真实 GPU：canvas 由 GpuSelfTest 传入。全部结论靠读回像素逐字节比较，不做视觉判断。</summary>
    public static void CheckGpu(Canvas canvas)
    {
        string temporary = Path.Combine(Environment.CurrentDirectory, ".kuroaki-text-gpu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string chart = Path.Combine(temporary, "ENCORE.vsc");
            File.WriteAllText(chart, "0,3,0,b:120\n8000,0,0\n");
            File.WriteAllText(Path.Combine(temporary, "ENCORE.vsm"), "!obj:obj_custom_gimmick\n0,0,linear,160,160,textX_test,-1\n0,0,linear,80,80,textY_test,-1\n0,0,linear,2,2,textscale_test,-1\n");
            File.WriteAllText(Path.Combine(temporary, "ENCORE_text_test.txt"), "0,Text film GPU\n");
            var off = Session.Load(chart);
            var on = new Session(off.Project.Copy(), editedVsm: File.ReadAllText(off.Project.Gimmick!) + "0,0,linear,1,1,fx_film,-1\n");
            using var renderer = new SceneRenderer(canvas);
            // 四步依次验证：开启胶片后画面变化 → 时间推进后噪点/时间 uniform 真的在动 →
            // 回到同一时刻逐字节复现 → 关掉后完全恢复原样（最后一条防止后处理状态残留到下一次渲染）。
            renderer.Render(off, 1); var baseline = canvas.Read(renderer.Final);
            renderer.Render(on, 1); var enabled = canvas.Read(renderer.Final);
            if (baseline.SequenceEqual(enabled)) throw new InvalidOperationException("fx_film did not change GPU pixels.");
            renderer.Render(on, 2); var later = canvas.Read(renderer.Final);
            if (later.SequenceEqual(enabled)) throw new InvalidOperationException("Film noise/time uniforms did not animate.");
            renderer.Render(on, 1);
            if (!enabled.SequenceEqual(canvas.Read(renderer.Final))) throw new InvalidOperationException("Film is not deterministic on backward seek.");
            renderer.Render(off, 1);
            if (!baseline.SequenceEqual(canvas.Read(renderer.Final))) throw new InvalidOperationException("Disabling film did not restore the scene.");
            // 文字画布单独验：清成 0x101820 这个非黑背景色，再判断是否存在任何一个与背景色不同的像素 ——
            // 用纯黑当底就分不清"没画"和"画了黑字"。bounds 是选中框的四角，必须是有限数才能拿去做命中测试。
            using var target = new Target(canvas.Gpu, 640, 360);
            canvas.Begin(target, 640, 360, 640, 360, Color.Hex(0x101820));
            var sampler = new TextValueSampler(VsmDocument.FromText(File.ReadAllText(off.Project.Gimmick!)), off.Timeline.Bpm);
            Vector2[]? bounds = null;
            renderer.DrawAuthoringTexts(off, 1, Matrix3x2.CreateScale(2), n => sampler.Get(n, 2), "test", (_, corners) => bounds = corners);
            var textPixels = canvas.Read(target);
            if (bounds == null || bounds.Any(v => !float.IsFinite(v.X + v.Y)) ||
                !Enumerable.Range(0, textPixels.Length / 4).Any(i => textPixels[i * 4] != 16 || textPixels[i * 4 + 1] != 24 || textPixels[i * 4 + 2] != 32))
                throw new InvalidOperationException("Text canvas failed to draw production glyphs and selection bounds.");
            Console.WriteLine("PASS text canvas glyphs/bounds and film enable/disable/animation/deterministic seek on GPU");
            CheckTextCovers(canvas, renderer, temporary, chart);
        }
        finally { Directory.Delete(temporary, true); }
    }

    static void CheckTextCovers(Canvas canvas, SceneRenderer renderer, string directory, string chart)
    {
        string text = Path.Combine(directory, "cover-text.txt");
        File.WriteAllText(text, "0,But she refused to die\n");
        using var covers = new CustomGimmickRenderer(canvas);
        using var expected = new Target(canvas.Gpu, 320, 180);
        const string header = "!obj:obj_custom_gimmick\n!proxies:1\n";
        string Mod(string name, double value, int proxy = -1) => $"0,0,linear,{value},{value},{name},{proxy}\n";
        byte[] Render(Session session, double time)
        {
            renderer.Render(session, time, notes: false, effects: false);
            return canvas.Read(renderer.Final);
        }
        foreach (bool legacy in new[] { false, true })
        {
            string id = legacy ? "" : "test", suffix = legacy ? "" : "_test";
            var project = new ViewerProject { Chart = chart, GameUiEnabled = false, RenderWidth = 320,
                TextFiles = new() { [id] = text } };
            string pose = Mod("textX" + suffix, 160) + Mod("textY" + suffix, 80)
                + Mod("textscale" + suffix, 2) + Mod("textalignh" + suffix, 1)
                + Mod("particle_alpha", 0);
            var baselineSession = new Session(project.Copy(), editedVsm: header + pose);
            var baseline = Render(baselineSession, 1);
            foreach (string cover in new[] { "cover1", "cover2", "cover3" })
            {
                // 0 -> 1 over four beats: sample fully off, halfway, on, then seek backward.
                var session = new Session(project.Copy(), editedVsm: header + pose + $"0,4,linear,0,1,{cover},-1\n");
                foreach (double time in new[] { 0, 1, 2, 1, 0 })
                {
                    Render(baselineSession, time);
                    canvas.Pass(expected, renderer.Final.Texture, canvas.Basic);
                    canvas.Begin(expected, 320, 180, 320, 180);
                    covers.DrawCovers(session, time);
                    var reference = canvas.Read(expected);
                    var actual = Render(session, time);
                    if (!reference.SequenceEqual(actual))
                        throw new InvalidOperationException($"{cover} must mask {(legacy ? "legacy" : "named")} text at time {time}.");
                    if (time > 0 && baseline.SequenceEqual(actual))
                        throw new InvalidOperationException($"{cover} fixture did not overlap visible text.");
                }
                // The covered application surface must move as a unit when a Custom proxy samples it.
                var raw = Render(session, 2);
                var moved = new Session(project.Copy(), editedVsm: header + pose + Mod(cover, 1)
                    + Mod("pra", 1, 0) + Mod("prx", 32, 0) + Mod("prcl", 0, 0)
                    + Mod("prcr", 320, 0) + Mod("prct", 0, 0));
                var shifted = Render(moved, 2);
                for (int y = 70; y < 110; y++)
                    for (int x = 120; x < 230; x++)
                        for (int channel = 0; channel < 4; channel++)
                            if (raw[(y * 320 + x - 32) * 4 + channel] != shifted[(y * 320 + x) * 4 + channel])
                                throw new InvalidOperationException($"{cover} and text did not move together with the Custom proxy.");
            }
        }
        Console.WriteLine("PASS cover1/2/3 mask named/legacy text with animated alpha, backward seek and Custom proxy movement");
    }
}
