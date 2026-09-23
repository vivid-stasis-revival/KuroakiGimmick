using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>
/// 原生序列（剧情文本框）与音频容量回归。剧情部分用手写 fixture 跑，不需要原曲资源；
/// realChart 传入时才追加需要真实谱面和长音频的那一组检查，因此默认路径在任何机器上都能跑。
/// </summary>
public static class NativeSequenceSelfTest
{
    /// <summary>realChart 为 null 时跳过依赖真实资源的检查；这不是"测试变少了"，而是那组断言没有数据可验。</summary>
    public static int Run(string? realChart = null)
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("NATIVE SEQUENCE: " + name); checks++; Console.WriteLine("PASS " + name); }
        // 1024 MB 的 PCM 上限按 4 字节 float 算正好是 256,000,000 个样本：边界那一个必须收，多一个必须拒。
        // 关键在于判断发生在分配之前 —— 先分配再检查等于先 OOM 一次。
        Check(AudioData.MaxDecodedBytes == 1_024_000_000 && AudioData.ValidateSampleCount(256_000_000) == 256_000_000, "1024 MB PCM boundary accepts its final sample without allocation");
        bool rejected = false; try { AudioData.ValidateSampleCount(256_000_001); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "audio exceeding 1024 MB remains rejected");
        // 原生二进制事件按下标引用扩展 mod，所以这 41 个 ID 的顺序本身就是 ABI：
        // 插入或重排任何一个都会让旧谱面里的事件指向别的效果。首、中（第 20 个）、尾三点用来钉住整体位移。
        var ids = NativeGimmickProfile.ReadExtraMods("obj_extendnova_gimmick");
        Check(ids.Length == 41 && ids[0] == "glitchamp" && ids[20] == "setup_co" && ids[^1] == "fx_edge", "native binary IDs preserve source registration order");
        // 下面这段 fixture 是从原曲 story7 抄来的真实数值：780.799 秒（13:00.799）起，783.612 清字，787 销毁。
        var story = new NativeSequenceDefinition
        {
            Story = [new(780.799, "story7", "Saturday & Dawn", "This is it! Brace yourself!!!")],
            StoryWindows = [new("story7", 780.799, 783.612, 787)]
        };
        // 第三个参数是"谱面回调是否已触发"的谓词，第四个是文字宽度测量（这里用固定比例代替真实字体）。
        NativeStoryState? Sample(double time, bool triggered = true) => NativeStoryState.Sample(story, time, _ => triggered, s => s.Length * 5);
        // 时间到了但回调没触发、或回调触发了但时间没到，都不能显示：两个条件是与的关系。
        Check(Sample(780.798) == null && Sample(781, false) == null, "story waits for both source time and chart callback");
        // 打字机速度是原版的 100 字符/秒：起点后 0.01 秒正好显示 1 个字符。Y=142 来自按行数计算的入场 tween。
        Check(Sample(780.809) is { VisibleCharacters: 1 } && Math.Abs(Sample(781.8)!.Y - 142) < 1e-6, "story uses 100 characters/second and source line-count tween");
        // 清字（783.612）早于对象销毁（787）：中间这段文本框还在但没有字，顺序颠倒会看到文字残留到消失那一刻。
        Check(Sample(783.611) is { Cleared: false } && Sample(783.612) is { Cleared: true, VisibleCharacters: 0, Y: 122 }, "story7 clears text at 13:03.612, before object destruction");
        // Y=180 即退到 320×180 逻辑空间的下边缘之外；787 之后彻底返回 null，不能残留到后面的游玩段落。
        Check(Sample(784.612) is { Y: 180 } && Sample(787) == null && Sample(835.59) == null, "story exit finishes and never leaks into gameplay");
        // 采样必须是时间的纯函数：先看 781，跳到 835.59，再回到 781 必须完全一样，不能依赖"上一次问到哪了"。
        var beforeSeek = Sample(781); _ = Sample(835.59);
        Check(Sample(781)!.Y == beforeSeek!.Y && Sample(781)!.VisibleCharacters == beforeSeek.VisibleCharacters, "story sampling is independent of playback history");
        CheckChartEpisode(Check);
        if (realChart != null)
        {
            var session = Session.Load(realChart);
            // 这首曲子的音频解码后超过旧的 256 MB 上限，正是当初提高上限的原因。
            Check(session.Audio != null && session.Audio.Samples.LongLength * sizeof(float) > 256_000_000, "real long audio decodes beyond the old 256 MB limit");
            // ":mod_" 是解不出名字时的占位符：真实谱面里一个都不该出现，否则说明扩展 ID 表和二进制对不上。
            Check(session.Chart.Mods.All(m => !m.Name.Contains(":mod_")), "real binary native events have names instead of unknown ID placeholders");
            Check(session.NativeGimmick.Issues.Count == 0 && session.NativeGimmick.Data?.Sequence != null, "native resources and sequence bindings load");
            Check(session.Fx.Find("FX_glow") is { Filter: "_effect_glow", Enabled: true }, "original room Glow is present");
            Check(session.Fx.Find("glow") != null && session.Fx.Find("FX_posterize") != null && session.Fx.Find("chorusbg") != null, "background Glow, posterise and heat-haze layers load");
            // 剧情段落只是"到点显示文本框"，不得顺手往播放路线里插跳转：一旦插了，音乐时长就被悄悄改短了。
            Check(!session.Playback.HasJumps && session.Playback.Controls.Count == 0 && session.Playback.Duration(0, session.Duration) == session.Duration, "story setup never creates automatic skips; full music duration is retained");
            Check(session.NativeGimmick.Data!.Sequence!.Story.Select(c => c.Trigger).Distinct().Count() == 8, "all eight story sections have read-only timed cues");
            // 70 行台词、8 个文本框窗口都是原曲里的实际条数；setup_co_en_s7 的清字时刻要和上面手写 fixture 的 783.612 对上。
            var sequence = session.NativeGimmick.Data.Sequence;
            Check(sequence.Story.Count == 70 && sequence.StoryWindows.Count == 8 && sequence.StoryWindows.Single(w => w.Trigger == "setup_co_en_s7").Clear == 783.612,
                "all original dialogue lines and textbox lifetimes are imported");
            Check(new[] { "sp_dialogue", "sp_dialogue_grads", "sp_namebox_new" }.All(session.GameUi.Data!.Sprites.ContainsKey), "original dialogue artwork is available");
            Check(!session.Chart.Diagnostics.Any(d => d.Error || d.Message.Contains("Unsupported mod")), "real chart has no audio errors or unsupported native IDs");
            // 这一段只打印不断言：把谱面里出现过、但 ModCatalog 尚未登记的名字连同取值范围列出来供人工审阅。
            // 故意不做成失败条件 —— 发现新名字应该由人判断该怎么实现，而不是让自测直接红掉。
            foreach (var g in session.Chart.Mods.GroupBy(e => e.Name).Where(g => !ModCatalog.Supported.Contains(g.Key)))
                Console.WriteLine($"SOURCE {g.Key}: {g.Count()} events, values {g.Min(e => e.To):0.###}..{g.Max(e => e.To):0.###}, duration {g.Max(e => e.Duration):0.###} beats");
        }
        Console.WriteLine($"{checks} native sequence/audio checks passed."); return 0;
    }
    /// <summary>需要真实 GPU：用 canvas 逐个渲染再逐字节比较，只证明"画出来的东西不同/相同"，不判断画得好不好看。</summary>
    public static void CheckGpu(Canvas canvas)
    {
        string root = Path.Combine(Environment.CurrentDirectory, ".native-sequence-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string chart = Path.Combine(root, "ENCORE.vsc"); File.WriteAllText(chart, "0,3,0,b:120\n8000,0,0\n");
            File.WriteAllText(Path.Combine(root, "ENCORE.vsm"), "!obj:obj_extendnova_gimmick\n");
            var basic = Session.Load(chart); using var renderer = new SceneRenderer(canvas);
            byte[] Render(string rows, double time = .1)
            { var s = new Session(basic.Project.Copy(), editedVsm: "!obj:obj_extendnova_gimmick\n" + rows); renderer.Render(s, time); return canvas.Read(renderer.Final); }
            // 每条都是"换一个控制值重画一次，像素必须变化"的形式：只能证明该通路真的接上了 GPU，
            // 不能证明画面与原版一致，那要靠同帧图像对照。cg_xscale 先给非零值，否则 CG 宽度为 0 什么都看不出来。
            const string cg = "0,0,linear,1,1,supernova_cg_xscale,-1\n";
            var first = Render(cg + "0,0,linear,0,0,supernova_cg_frame,-1\n");
            var second = Render(cg + "0,0,linear,1,1,supernova_cg_frame,-1\n");
            if (first.SequenceEqual(second)) throw new Exception("CG frame switch had no pixel effect.");
            const string background = "0,0,linear,1,1,en_chorus_bg_alpha,-1\n";
            var normal = Render(background); var glow = Render(background + "0,0,linear,1,1,fx_glow,-1\n");
            if (normal.SequenceEqual(glow)) throw new Exception("Native FX_glow did not affect the background.");
            // 两个原生背景必须各画各的，不能因为共用一套加载代码就退化成同一张图。
            var enddrop = Render("0,0,linear,1,1,en_enddrop_bg_alpha,-1\n");
            if (normal.SequenceEqual(enddrop)) throw new Exception("Distinct native backgrounds did not render.");
            var slash = Render(background + "0,0,linear,0,1,en_slash_sat,-1\n");
            if (normal.SequenceEqual(slash)) throw new Exception("Native slash callback did not render.");
            // HP 条挂在 HUD 上，所以必须先开 uialpha；对照组只开 uialpha，差异才能归因到血条本身。
            var hp = Render("0,0,linear,1,1,uialpha,-1\n0,0,linear,1,1,en_evildawn_hpbar_alpha,-1\n0,0,linear,50,50,en_evildawn_hpbar_amount,-1\n");
            var hud = Render("0,0,linear,1,1,uialpha,-1\n");
            if (hp.SequenceEqual(hud)) throw new Exception("Native HP bar did not render.");
            // 重新构造 Session 再渲染同一组参数，必须逐字节回到最初那一帧：这同时覆盖了重新加载和回跳。
            if (!first.SequenceEqual(Render(cg + "0,0,linear,0,0,supernova_cg_frame,-1\n"))) throw new Exception("Native sequence rendering is not deterministic after reload/seek.");
            using var storyTarget = new Target(canvas.Gpu, 320, 180);
            using var ui = new GameUiRenderer(canvas);
            var storySession = new Session(basic.Project.Copy(), editedVsm: "!obj:obj_extendnova_gimmick\n0,0,linear,0,1,setup_co_en_s7,-1\n");
            byte[] StoryFrame(double time)
            {
                canvas.Begin(storyTarget, 320, 180, 320, 180, Color.Hex(0));
                ui.DrawSequenceStory(storySession, time);
                return canvas.Read(storyTarget);
            }
            // 四个时刻取自上面 CPU fixture 的同一条时间线：780 还没出现、780.85 入场中、782 已显示、783.612 正在清字。
            // 相邻两帧必须都不相同，否则说明入场 tween 或打字机在 GPU 这一侧被漏掉了。
            var absent = StoryFrame(780);
            var entering = StoryFrame(780.85);
            var shown = StoryFrame(782);
            var clearing = StoryFrame(783.612);
            if (absent.SequenceEqual(entering) || entering.SequenceEqual(shown) || shown.SequenceEqual(clearing))
                throw new Exception("Original dialogue entry, typewriter or clear did not change pixels.");
            // 787（销毁后）和 835.59（进入游玩段落）都必须回到"什么都没有"那一帧；
            // 再问一次 782 还要和第一次完全一致，用来同时验证不残留和回跳可重现。
            if (!absent.SequenceEqual(StoryFrame(787)) || !absent.SequenceEqual(StoryFrame(835.59)) || !shown.SequenceEqual(StoryFrame(782)))
                throw new Exception("Dialogue leaked after its lifetime or changed after seeking backwards.");
            Console.WriteLine("PASS original dialogue artwork, entrance, typing, clear, destruction and backward seek on GPU");
            Console.WriteLine("PASS native CG, two heat-haze backgrounds, Glow, slash, HP bar and seek determinism on GPU");
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>
    /// Custom Episodes 的谱面内剧情（custom_episode）。数值全部对照模组的 mod_cs_chart_step：
    /// 打字机每秒 100×speed 个字，停留 max(200, 显式 dwell 或 chart_dwell + 字数×40/speed)，
    /// 两者相加才是一句话占用的时间。谱面内 speed 缺省 0.5，文字框停在 132 而不是剧情房间的 122。
    /// </summary>
    static void CheckChartEpisode(Action<bool, string> Check)
    {
        string root = Path.Combine(Path.GetTempPath(), "kuroaki-episode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string file = Path.Combine(root, "story.json");
            File.WriteAllText(file, """
            {
              "chart_dwell": 1000,
              "characters": { "sat": { "display_name": "Saturday" } },
              "lines": [
                { "kind": "narration", "text": "abcde" },
                { "kind": "delay", "ms": 500 },
                { "kind": "portrait", "who": "sat" },
                { "kind": "bg", "value": "bg_wm_nightstreet" },
                { "who": "sat", "text": "fg", "speed": 1 },
                { "kind": "end" },
                { "kind": "narration", "text": "unreachable" }
              ]
            }
            """);
            var script = EpisodeScript.Read(file);
            // 谱面内不支持的指令在游戏里只写进存档目录的日志，作者还关不掉；编辑器要在试玩前就报出来。
            var skipped = script.Diagnostics.Where(d => !d.Error).Select(d => d.Message).ToArray();
            Check(skipped.Length == 2 && skipped[0].Contains("'bg'") && skipped[1].Contains("'portrait'"),
                "in-chart episode reports every step the game would silently skip");

            var sequence = script.Expand([0], s => s.Length * 5);
            // end 之后的步骤不该被展开——模组把索引直接推到末尾。
            Check(sequence.Story.Count == 2, "episode stops expanding at 'end'");
            // "abcde" 5 字 speed 0.5：打字 5/50=0.1 秒，停留 1000+5*40/0.5=1400 毫秒，合计 1.5 秒；
            // 再加 delay 的 0.5 秒，第二句正好落在 2.0 秒。portrait / bg 不占时间。
            Check(Math.Abs(sequence.Story[0].Time) < 1e-9 && sequence.Story[0].Speed == .5
                && sequence.Story[0].Speaker.Length == 0 && sequence.Story[0].Text == "abcde",
                "first episode line starts at the trigger with the in-chart default speed");
            Check(Math.Abs(sequence.Story[1].Time - 2) < 1e-9 && sequence.Story[1].Speed == 1
                && sequence.Story[1].Speaker == "Saturday",
                "episode dwell, typing time and delay accumulate onto the next line");
            // "fg" 2 字 speed 1：打字 0.02 秒，停留 1000+2*40=1080 毫秒 → 2.0+1.1=3.1 收尾，再过 1 秒销毁。
            var window = sequence.StoryWindows[0];
            Check(Math.Abs(window.Clear!.Value - 3.1) < 1e-9 && Math.Abs(window.Destroy!.Value - 4.1) < 1e-9
                && window.ExitFrom == 132 && window.ExitSeconds == .8,
                "episode exits from the in-chart rest position, not the story room's 122");

            NativeStoryState? Sample(double t) => NativeStoryState.Sample(sequence, t, _ => true, s => s.Length * 5);
            // 谱面内 speed 0.5 = 每秒 50 字：0.06 秒正好 3 个字。
            Check(Sample(.06) is { VisibleCharacters: 3 } && Sample(2.01) is { VisibleCharacters: 1, Speaker: "Saturday" },
                "episode typewriter follows 100 x speed characters per second");
            // 拖时间轴要求纯函数：先看 1.0，跳到 9.0，再回 1.0 必须逐字段一致。
            // record 的合成相等对 Lines 走引用比较，两次采样必然是不同的 list 实例，所以逐项比。
            var before = Sample(1); _ = Sample(9);
            var again = Sample(1);
            Check(again!.Y == before!.Y && again.VisibleCharacters == before.VisibleCharacters
                && again.Speaker == before.Speaker && again.Lines.SequenceEqual(before.Lines)
                && Sample(4.1) == null,
                "episode sampling is independent of playback history");

            // 编辑器在时间轴上画的区间就是 Extent 的结果，因此它必须与 Sample 的取窗规则一致：
            // 两次触发相隔 2 秒，而剧本要演到 4.1 秒，第一段于是在第二次触发到来时当场被切断。
            var cut = script.Expand([0, 2], s => s.Length * 5);
            var (firstStart, firstEnd, firstCut) = EpisodeScript.Extent(cut, 0);
            var (nextStart, nextEnd, nextCut) = EpisodeScript.Extent(cut, 1);
            Check(firstStart == 0 && Math.Abs(firstEnd - 2) < 1e-9 && firstCut
                && Math.Abs(nextStart - 2) < 1e-9 && Math.Abs(nextEnd - 4.1 - 2) < 1e-9 && !nextCut,
                "a later episode trigger cuts the playing one short instead of queueing behind it");
            // 区间右端两侧各采一次：左边仍是第一段演完的 5 个字，右边已经换成第二段的第 0 个字。
            // 画出来的长度因此不会承诺一段演不完的剧情。
            NativeStoryState? Cut(double t) => NativeStoryState.Sample(cut, t, _ => true, s => s.Length * 5);
            Check(Cut(firstEnd - 1e-6) is { VisibleCharacters: 5 } && Cut(firstEnd) is { VisibleCharacters: 0 },
                "the drawn episode span ends exactly where playback hands over to the next trigger");
            // 同一拍上的两行 custom_episode 不算互相切断——那只是重复写了一次，不是"被顶掉"。
            var same = EpisodeScript.Extent(script.Expand([0, 0], s => s.Length * 5), 0);
            Check(!same.Truncated && Math.Abs(same.End - 4.1) < 1e-9,
                "two episode triggers on the same beat do not report each other as a cut-off");
        }
        finally { Directory.Delete(root, true); }
    }
}
