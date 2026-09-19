using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>
/// Custom gimmick 的跳转/音乐控制与粒子回归。60 BPM 让 1 拍正好等于 1 秒，下面所有秒数都可以直接手算。
/// CPU 部分不创建窗口；CheckGpu 复用调用方的 canvas。
/// </summary>
public static class CustomFxSelfTest
{
    public static int Run()
    {
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("CUSTOM FX: " + label); count++; Console.WriteLine("PASS " + label); }
        Chart Chart(string body) { var c = new Chart(); VsmReader.ReplaceModsText(c, "!obj:obj_custom_gimmick\n" + body, "custom-fx-test.vsm"); return c; }
        // 把一段 mods 文本编成 0..end 的播放路线；end 是未经跳转的原始谱面秒数。
        ChartPlaybackRoute Route(string body, double end = 5, bool enabled = true)
        { var c = Chart(body); return new ChartPlaybackMap(c, new BpmMap(c, 60, 0), enabled).Route(0, end); }
        var forward = Route("1,0,linear,0,3,jumpto_beat,-1");
        // 参数依次是 (层, 是否 Custom 对象, ENABLE_NON_BASE_FX, 是否启用原生颜色控制, 取值查询)。
        // FX_edge 只有"Custom 对象 且 开了非基础 FX"时才交给 fx_edge 决定，否则一律沿用房间原本的可见性。
        var layer = new GameFxProfile.Layer { Name = "FX_edge", Visible = true };
        Check(RoomFxState.IsForegroundActive(layer, true, false, false, _ => 0) &&
            !RoomFxState.IsForegroundActive(layer, true, true, false, _ => 0) &&
            RoomFxState.IsForegroundActive(layer, true, true, false, _ => 1), "disabled Custom control retains the original room visibility; enabled control follows fx_edge");
        // 拍 1 跳到拍 3：跳过的 2 秒要从总时长里真正扣掉（5-2=3），播放到 1.25 秒时谱面已经走到 3.25 秒。
        Check(forward.Duration == 3 && Math.Abs(forward.ChartAt(1.25) - 3.25) < 1e-9, "forward jump removes skipped song time");
        // 往回跳会把 1..2 这段重播一次，所以总时长是 4+1=5；重播一次就够，不能变成死循环。
        var backward = Route("2,0,linear,0,1,jumpto_beat,-1", 4);
        Check(backward.Duration == 5 && backward.ChartAt(2.5) == 1.5, "backward jump replays a region exactly once");
        // 跳到自己所在的拍是原版谱面里真实出现过的写法，必须原地放过，否则编排路线时会卡死在这一拍上。
        Check(Route("0,0,linear,0,0,jumpto_beat,-1\n2,0,linear,0,2,jumpto_beat,-1", 4).Duration == 4, "self-target jumps never loop forever");
        // 同一拍上两条跳转按源码顺序处理，最终落点是后声明的那条（跳到拍 2，净跳过 1 秒 → 5-1=4）。
        Check(Route("1,0,linear,0,3,jumpto_beat,-1\n1,0,linear,0,2,jumpto_beat,-1").Duration == 4, "same-time jump callbacks retain source order");
        // jumpto_s 用 from 字段区分单位：from=0 时 to 是毫秒，from=1 时 to 是秒，所以 3000 与 3 必须等价。
        Check(Route("1,0,linear,0,3000,jumpto_s,-1").Duration == 3 && Route("1,0,linear,1,3,jumpto_s,-1").Duration == 3, "jumpto_s preserves millisecond/second source units");
        Check(Route("1,0,linear,0,3,jumpto_beat,-1", enabled: false).Duration == 5, "music control configuration disables jumps");
        // playspeed 是累乘的，往回跳时若把同一条速度回调再执行一遍就会变成 4 倍速；这里要求它保持 2 倍。
        var rate = Route("1,0,linear,0,2,playspeed,-1\n2,0,linear,0,0.5,jumpto_beat,-1", 4);
        Check(rate.RateAt(1.6) == 2 && Math.Abs(rate.Duration - 3.25) < 1e-9, "backward jump does not apply the same speed callback twice");
        var c = Chart("0,0,linear,0,2,jumpto_beat,-1");
        using (var transport = new Transport())
        {
            // 手动 seek 本身不触发跳转，否则作者永远停不到跳转点之前那一刻去检查演出。
            transport.Load(null, 5); transport.SetChartPlayback(new(c, new(c, 60, 0), true)); transport.Seek(0);
            Check(transport.Position == 0, "manual seek does not itself execute a jump");
            // 真正开始播放时才在播放头上执行跳转，落到拍 2 附近（上界 2.2 留给启动这一帧走过的时间）。
            transport.SetPlaying(true); Check(transport.Position >= 2 && transport.Position < 2.2, "starting playback applies a jump at the playhead");
            transport.SetPlaying(false); double paused = transport.Position; transport.SetSpeed(.5);
            Check(transport.Position == paused, "pause and manual speed retain the current route position");
        }
        // plaudite_pburst 的两种模式共用一个 mod 名，靠 from 区分：from=1 是连续模式（to 为毫秒时长），
        // from=0 是计数模式。连续模式按原版 60 Hz 逻辑 tick 倒计时发射，VSM 里写的 duration=99 拍完全不参与；
        // 118 就是这套 tick 逻辑在第 1 秒内实际发出的粒子数。
        var repeated = Chart("0,99,linear,1,1000,plaudite_pburst,-1\n2,0,linear,0,2,plaudite_pburst,-1");
        var timeline = new Timeline(repeated, new ViewerProject { Bpm = 60 }, 3);
        Check(timeline.Particles.Count(p => p.Burst && p.Time > 0 && p.Time < 1) == 118, "continuous mode uses 1000 ms and a 60 Hz countdown, ignoring VSM duration");
        // 计数模式在拍 2 一次性发完，不受上面那条连续模式影响：两条路径不能在重构时被合并成一条。
        Check(timeline.Particles.Count(p => p.Burst && p.Time == 2) == 4, "count mode remains independent from continuous mode");
        // 查询任意时刻（这里故意查到 100 秒）都不能改动已生成的粒子历史，否则拖进度条会改变演出本身。
        var before = timeline.Particles.Where(p => p.Burst).ToArray(); _ = timeline.Get("pburstspeed", 100);
        Check(before.SequenceEqual(timeline.Particles.Where(p => p.Burst)), "particle history is immutable during seeking");
        // 括号重复展开出的 1001 条同名事件只应产生一条兼容性提示，不能每条都报一次把报告刷爆。
        var many = Chart("0(:10:.01),0,linear,1,1,plaudite_pburst,-1");
        _ = new Timeline(many, new ViewerProject { Bpm = 60 }, 12);
        Check(!many.Diagnostics.Any(d => d.Message.Contains("repeated mode")), "expanded repeated bursts do not flood compatibility reports");
        Console.WriteLine($"{count} Custom FX/music-control checks passed."); return count;
    }
    /// <summary>需要真实 GPU：canvas 由 GpuSelfTest 建好传入，这里只借用，不创建设备也不负责释放。</summary>
    public static void CheckGpu(Canvas canvas)
    {
        string root = Path.Combine(Environment.CurrentDirectory, ".custom-fx-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "ENCORE.vsc"), vsm = Path.Combine(root, "ENCORE.vsm");
            File.WriteAllText(path, "0,3,0,b:120\n8000,0,0\n"); File.WriteAllText(vsm, "!obj:obj_custom_gimmick\n");
            var basic = Session.Load(path);
            Session With(string rows) => new(basic.Project.Copy(), editedVsm: "!obj:obj_custom_gimmick\n" + rows);
            using var target = new Target(canvas.Gpu, 320, 180); using var renderer = new CustomGimmickRenderer(canvas);
            byte[] Draw(Action draw) { canvas.Begin(target, 320, 180, 320, 180, Color.Hex(0)); draw(); return canvas.Read(target); }
            // 跳过 alpha 通道只看 RGB：黑底上只要有任一通道被点亮，就说明这一层真的画进去了。
            bool Bright(byte[] pixels) => pixels.Where((_, i) => i % 4 != 3).Any(v => v > 0);
            // static 是逐帧变化的噪点：既要动（0 与 .04 秒不同），又要可重现（回到 0 秒必须逐字节相同）。
            // 两条缺一不可 —— 只查"会动"的话，用 Random 也能过。
            var snow = With("0,0,linear,1,1,static,-1");
            var first = Draw(() => renderer.DrawCovers(snow, 0)); var later = Draw(() => renderer.DrawCovers(snow, .04));
            if (!Bright(first) || first.SequenceEqual(later)) throw new Exception("Static frames did not animate.");
            if (!first.SequenceEqual(Draw(() => renderer.DrawCovers(snow, 0)))) throw new Exception("Static seek is not deterministic.");
            // 113 和 206 是 DF 侧线在 320×180 逻辑空间里的原版横坐标，取第 90 行（垂直居中）采样：
            // 线画歪了或者只画了一侧，这一条就会落网。
            var lines = With("0,0,linear,1,1,df_sideline,-1"); var pixels = Draw(() => renderer.DrawDf(lines, 0));
            if (pixels[(90 * 320 + 113) * 4] == 0 || pixels[(90 * 320 + 206) * 4] == 0) throw new Exception("DF source positions were not drawn.");
            // .1 秒时侧边精灵和拖尾都在，1.1 秒时必须彻底消失：拖尾很容易被写成永不回收。
            var sides = With("0,0,linear,0,1,unraveling_sidething,-1");
            if (!Bright(Draw(() => renderer.DrawUnravelSides(sides, .1)))) throw new Exception("Unravel side sprite/trail did not draw.");
            if (Bright(Draw(() => renderer.DrawUnravelSides(sides, 1.1)))) throw new Exception("Unravel effect outlived its original sprites.");
            // 边缘检测是整屏后处理：开启后画面必须变化，关掉后必须逐字节回到原样 —— 后者防止后处理状态残留到下一帧。
            using var scene = new SceneRenderer(canvas); var edge = With("0,0,linear,1,1,fx_edge,-1");
            scene.Render(basic, 1); var normal = canvas.Read(scene.Final); scene.Render(edge, 1); var filtered = canvas.Read(scene.Final);
            if (normal.SequenceEqual(filtered)) throw new Exception("Edge detection did not change the scene.");
            scene.Render(basic, 1); if (!normal.SequenceEqual(canvas.Read(scene.Final))) throw new Exception("Edge disable did not restore the scene.");
            Console.WriteLine("PASS original static, DF sidelines, Unravel sprites/trails and edge filter on GPU");
        }
        finally { Directory.Delete(root, true); }
    }
}
