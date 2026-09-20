using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Core;

/// <summary>
/// Custom gimmick 移植回归：playspeed 时钟、Transport 联动，以及 cgmk_config 控制的星尘粒子。
/// CPU 部分不创建窗口；CheckGpu 复用调用方的 canvas，不自己建设备。
/// </summary>
public static class CustomAdaptationSelfTest
{
    /// <summary>临时目录建在当前目录下而不是 /tmp：资源解析会拒绝 macOS 上 /tmp 那种符号链接父目录。</summary>
    public static int Run()
    {
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("CUSTOM ADAPTATION: " + label); count++; Console.WriteLine("PASS " + label); }
        var projectionChart = new Chart { ObjectName = "obj_custom_gimmick", Proxies = 2 };
        VsmReader.ReplaceModsText(projectionChart, "!obj:obj_custom_gimmick\n!proxies:2\n0,2,linear,0,.01,prtrX,0\n0,2,linear,0,-.01,prtrY,1\n", "projection-test.vsm");
        var projectionTimeline = new Timeline(projectionChart, new ViewerProject { Bpm = 120 }, 2);
        Check(ModCatalog.Supported.Contains("prtrX") && ModCatalog.Supported.Contains("prtrY") && !ModCatalog.Supported.Contains("prtrx"), "perspective controls are supported with their original case-sensitive names");
        Check(Math.Abs(projectionTimeline.Get("prtrX", .5, 0) - .005) < 1e-12 && Math.Abs(projectionTimeline.Get("prtrY", .5, 1) + .005) < 1e-12,
            "perspective tween values preserve each proxy and signed coefficients");
        Check(projectionTimeline.Get("prtrX", -1, 0) == 0 && projectionTimeline.Get("prtrX", .5, 1) == 0,
            "perspective defaults to zero before its event and on other proxies");
        // playspeed 按原版语义是"瞬时且累乘"：只取 to，from（这里故意写成 573613 这种垃圾值）和 duration 都不参与。
        // 负拍的那条代表开场前的 setup，必须在 t=0 之前就已经生效。
        var chart = new Chart();
        chart.Mods.Add(new(-8, 9, "linear", 573613, 1.2, "playspeed", -1, 0));
        chart.Mods.Add(new(8, 12, "linear", 0, 1.25, "playspeed", -1, 1));
        chart.Mods.Add(new(12, 0, "linear", 0, .5, "playspeed", -1, 2));
        // 120 BPM 下 1 拍 = 0.5 秒，所以拍 8/12 分别落在 4/6 秒：1.2 → 1.2×1.25=1.5 → 1.5×0.5=0.75。
        var bpm = new BpmMap(chart, 120, 0); var clock = new ChartPlaybackMap(chart, bpm, true);
        Check(clock.RateAt(0) == 1.2 && clock.RateAt(4) == 1.5 && clock.RateAt(6) == .75, "playspeed is cumulative, instantaneous and applies negative-beat setup");
        // 时长要分段积分，不能拿平均速度糊弄：0..4 秒走 1.2 倍速，4..6 秒走 1.5 倍速。
        Check(Math.Abs(clock.Duration(0, 6) - (4 / 1.2 + 2 / 1.5)) < 1e-9, "speed boundaries integrate playback duration exactly");
        // 取样点包含正好落在速度分界上的 4、分界两侧的 3.999/4.01，以及超过谱面末尾的 100。
        foreach (double t in new[] { 0, 3.999, 4, 4.01, 6, 7, 100 }) Check(Math.Abs(clock.ChartAt(clock.PlaybackAt(t)) - t) < 1e-9, "seek roundtrip " + t);
        Check(new ChartPlaybackMap(chart, bpm, false).RateAt(6) == 1, "disabled music control leaves normal speed");
        // 速度 0 会让时钟彻底停住、seek 无法求逆，因此必须忽略该条并记诊断，而不是照单全收。
        chart.Mods.Add(new(15, 0, "linear", 0, 0, "playspeed", -1, 3));
        Check(new ChartPlaybackMap(chart, bpm, true).RateAt(8) == .75 && chart.Diagnostics.Any(d => d.Error), "invalid zero rate is diagnosed without breaking the clock");
        using (var transport = new Transport())
        {
            // Seek 用的是谱面秒，不是播放器秒；手动倍速与谱面倍速相乘：5 秒处谱面是 1.5，乘手动 .5 得 .75。
            transport.Load(null, 100); transport.SetChartPlayback(clock); transport.Seek(5); transport.SetSpeed(.5);
            Check(transport.Position == 5 && transport.EffectiveSpeed == .75, "manual speed multiplies chart speed while seeking stays in chart seconds");
            // 往回跳到 2 秒只剩开场那条 1.2，乘 .5 得 .6；如果回跳时把累乘回调又执行一遍就会得到 1.2×1.2 那一档。
            transport.Seek(2); Check(transport.EffectiveSpeed == .6, "backward seek restores rate without applying callbacks twice");
        }
        string dir = Path.Combine(Environment.CurrentDirectory, ".custom-adaptation-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "ENCORE.vsc");
            File.WriteAllText(path, "0,3,0,b:120\n8000,0,0\n");
            File.WriteAllText(Path.Combine(dir, "cgmk_config.json"), "{\"ENABLE_STARPARTICLE\":true}");
            const string mods = "!obj:obj_custom_gimmick\n0,0,linear,0,1,active_startrans,-1\n0,0,linear,1,1,starspawner_timer,-1\n0,0,linear,3,3,starspd_low,-1\n0,0,linear,3,3,starspd_high,-1\n0,0,linear,1,1,starspd_multiplier,-1\n0,0,linear,1,1,startrans_alpha,-1\n2,9,linear,0,0,active_startrans,-1\n4,9,linear,0,1,active_startrans,-1\n6,0,linear,0,999,starspd_multiplier,-1\n8,0,linear,0,-1,starspd_multiplier,-1\n0,0,linear,1,1,track_alpha,-1\n";
            File.WriteAllText(Path.Combine(dir, "ENCORE.vsm"), mods);
            var session = Session.Load(path);
            Check(session.Stars.Enabled && session.Stars.Stars.Count > 0, "enabled Custom star system emits real sprite instances");
            // active_startrans 在拍 2（=1 秒）写 0，duration 写了 9 也得当场关掉：原版这类开关不做渐变。
            Check(session.Stars.At(1.5, true).Count() == 0, "activation callback ignores duration and disables immediately");
            // 先取 .9 秒、跳到 7 秒、再回到 .9 秒：粒子是按时间纯函数算出来的，回跳不能得到另一批星星。
            var before = session.Stars.At(.9, true).ToArray();
            _ = session.Stars.At(7, true).ToArray();
            Check(before.SequenceEqual(session.Stars.At(.9, true)), "backward seek reproduces identical stars");
            // 320×180 逻辑空间里，原版只在两侧带状区域生成星星，中间的游玩轨道必须留空。
            Check(session.Stars.Stars.All(s => s.X is >= 0 and <= 110 or >= 210 and <= 320), "star spawn locations stay in the source side bands");
            // 倍率被推到 999 再翻成 -1 这种极端值下，粒子必须记录一个有限的死亡时刻，
            // 不能留下 NaN/无穷导致它永远挂在场景里。
            Check(session.Stars.Stars.Where(s => s.Birth < 3).All(s => double.IsFinite(s.Death)), "extreme speed records permanent particle deaths");
            // track_alpha 在原版 Custom 里就是空操作：登记成 SourceNoOp 让作者看得见，但绝不能真的建一条 track，
            // 否则预览会显示出游戏里根本不存在的效果。
            Check(session.Timeline.SourceNoOps.Any(n => n.Name == "track_alpha") && !session.Timeline.Tracks.ContainsKey(("track_alpha", -1)), "native-only control follows the source Custom no-op rule");
            // 已实现的和已确认是空操作的控制都不该再报 Unsupported，否则真正不支持的条目会淹没在噪声里。
            Check(!session.Chart.Diagnostics.Any(d => d.Message.Contains("Unsupported mod")), "implemented and verified no-op controls have accurate diagnostics");
            // 配置关掉时要连分配都不做（Count == 0），而不是生成之后再隐藏；但空操作登记仍然保留。
            File.WriteAllText(Path.Combine(dir, "cgmk_config.json"), "{\"ENABLE_STARPARTICLE\":false}");
            var disabled = Session.Load(path);
            Check(!disabled.Stars.Enabled && disabled.Stars.Stars.Count == 0 && disabled.Timeline.SourceNoOps.Any(n => n.Name == "active_startrans"), "config-disabled stars do not allocate or spawn particles");
            Console.WriteLine($"{count} custom adaptation checks passed."); return 0;
        }
        finally { Directory.Delete(dir, true); }
    }
    /// <summary>需要真实 GPU：由 GpuSelfTest 传入已建好的 canvas，共用同一个设备和线程，不在这里创建窗口。</summary>
    public static void CheckGpu(Canvas canvas)
    {
        string dir = Path.Combine(Environment.CurrentDirectory, ".custom-adaptation-gpu-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "ENCORE.vsc"); File.WriteAllText(path, "0,3,0,b:120\n8000,0,0\n");
            File.WriteAllText(Path.Combine(dir, "ENCORE.vsm"), "!obj:obj_custom_gimmick\n0,0,linear,0,1,active_startrans,-1\n0,0,linear,1,1,starspawner_timer,-1\n0,0,linear,3,3,starspd_low,-1\n0,0,linear,3,3,starspd_high,-1\n");
            File.WriteAllText(Path.Combine(dir, "cgmk_config.json"), "{\"ENABLE_STARPARTICLE\":true}");
            var session = Session.Load(path); using var renderer = new CustomGimmickRenderer(canvas); using var target = new Target(canvas.Gpu, 320, 180);
            // 黑底上画星星，跳过 alpha 通道（i%4!=3）只看 RGB：只要有任何一个通道被点亮就说明真的画出来了。
            canvas.Begin(target, 320, 180, 320, 180, Color.Hex(0)); renderer.DrawStars(session, 1, true); var first = canvas.Read(target);
            if (!first.Where((_, i) => i % 4 != 3).Any(v => v > 0)) throw new Exception("Stars did not draw on GPU.");
            // cover2 的原版几何是遮两侧、留中间。白底对照：整幅必须有变化（两侧被遮），
            // 但正中心 (160,90) 仍是 255，遮罩画反或画成全屏就会在这两条里落网。
            canvas.Begin(target, 320, 180, 320, 180, Color.White); var white = canvas.Read(target);
            var covered = new Session(session.Project.Copy(), editedVsm: "!obj:obj_custom_gimmick\n0,0,linear,1,1,cover2,-1\n");
            canvas.Begin(target, 320, 180, 320, 180, Color.White); renderer.DrawCovers(covered, 0); var cover = canvas.Read(target);
            if (cover.SequenceEqual(white) || cover[(90 * 320 + 160) * 4] != 255) throw new Exception("cover2 should mask sides while preserving the center.");
            // hide_combo 在本程序里必须是彻底的空操作：Viewer 从不画 combo 数字，所以"隐藏"它不该有任何像素差异。
            // 这条是防止有人"顺手把 hide_combo 实现出来"，那等于凭空多画一个游戏里没有的元素。
            using var scene = new SceneRenderer(canvas);
            scene.Render(session, 1); var withoutFlag = canvas.Read(scene.Final);
            var hidden = new Session(session.Project.Copy(), editedVsm: File.ReadAllText(session.Project.Gimmick!) + "\n0,0,linear,1,1,hide_combo,-1\n");
            scene.Render(hidden, 1); var withFlag = canvas.Read(scene.Final);
            if (!withoutFlag.SequenceEqual(withFlag)) throw new Exception("hide_combo must not change rendering: Viewer never displays combo numbers.");
            Console.WriteLine("PASS Custom stars, original cover2 geometry and no combo rendering regardless of hide_combo");
        }
        finally { Directory.Delete(dir, true); }
    }
}
