using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 场景合成协调器。对象背景、资源图层、轨道、固定 HUD 与后处理按显式边界组织；预览和视频导出共用此入口。
/// </summary>
public sealed partial class SceneRenderer
{
    /// <summary>判定档位，定义在 ScoreState 上：命中特效与 HUD 上的判定文本必须用同一个档位。</summary>
    const int JudgementTier = ScoreState.Tier;
    /// <summary>指示框存活 0.5 秒：透明度补间延迟 1/6 秒再走 1/3 秒。这是三种特效里最长的一个，扫描窗口按它取。</summary>
    const double IndicatorLife = .5, IndicatorGrow = 1.0 / 3, IndicatorFadeDelay = 1.0 / 6;
    /// <summary>
    /// 原版粒子每帧扣 delta_s * 240 / lifetime 的透明度，所以 lifetime 不是秒，存活秒数 = lifetime / 240。
    /// 钻尘起始透明度是 0.8 而不是 1，宽键那组的 lifetime 又比常规组短。
    /// </summary>
    const double NoteParticleLife = 50, DustLife = 75, WideDustLife = 70, DustAlpha = .8;
    /// <summary>
    /// 每簇的颗粒数。原版是 for (i = 0; i &lt;= arg4 / op_bgparticles; i++)，闭区间且 op_bgparticles 默认 1，
    /// 所以 arg4=6 出 7 颗、arg4=3 出 4 颗，不是 6 和 3。
    /// </summary>
    const int NoteParticleCount = 7, DustCount = 4;
    /// <summary>
    /// o_combodisplay 每次判定从连击数两侧喷出的那四簇：lifetime 70、起始透明度 0.5、每簇 2 颗，落点固定 y = 16。
    /// 存活只有 0.5 × 70 / 240 ≈ 0.146 秒，扫描窗口按它取。
    /// </summary>
    const double ComboDustLife = 70, ComboDustAlpha = .5, ComboDustY = 16;
    const int ComboDustCount = 2;
    /// <summary>
    /// 档位配色，下标即档位，取自 obj_noteIndicatorNew 与 o_pt_diamonddust2 的 switch：
    /// A.CRITICAL 与 CRITICAL 共用 c_yellow，之后依次是 c_lime、c_aqua、c_red。
    /// 原版常量是 BGR 字节序（c_aqua = 16776960），这里已经换算成 RGB。
    /// </summary>
    static readonly Color[] judgementColors =
    [
        Color.Hex(0xFFFF00),
        Color.Hex(0xFFFF00),
        Color.Hex(0x00FF00),
        Color.Hex(0x00FFFF),
        Color.Hex(0xFF0000)
    ];

    /// <summary>
    /// 音符落到判定线时的命中特效，对应原版 handle_judgement_normal / handle_judgement_wide。
    /// 这三样在原版里都是 instance：创建的那一帧把坐标、速度、随机帧一次性定死，之后只按自身年龄演化。
    /// 所以这里全部写成 age 的闭式，不保留任何跨帧状态 —— 拖时间轴、倒放和导出才能得到同一画面。
    /// 绘制顺序照抄原版的创建顺序（指示框→音符颗粒→钻尘），同深度下后建的盖在先建的上面。
    /// 调用点必须排在音符之后：原版音符深度 -350，特效 -375，特效盖在音符上。
    /// </summary>
    void DrawJudgmentEffects(Session session, double t)
    {
        var map = session.Timeline;
        bool holdTicks = session.Project.GameUiEnabled && session.Project.GameUiHoldEffects;
        foreach (var h in session.Score.HitsBetween(t - IndicatorLife, t, holdTicks))
        {
            // 地雷在原版 autoplay 分支里只结算不出特效。长条中途的节拍点是独立开关。
            if (h.Kind == ScoreState.HitKind.Mine || (h.Kind == ScoreState.HitKind.HoldTick && !holdTicks))
            {
                continue;
            }
            double age = t - h.Time;
            bool wide = h.Kind == ScoreState.HitKind.Wide;
            int lane = h.Lane, chartLane = wide ? lane - 4 : lane;
            // 特效创建时坐标就定死了，所以横向偏移按这次判定自己的时刻求值，而不是按当前帧。
            // 用当前帧会让已经炸开的特效跟着 xoffset 一起漂，原版不会。
            // distance = 音符距判定线的毫秒数 + 视觉延迟：普通音符判定成立时前一项归零，
            // 长条的节拍点和尾判用的却是长条头的坐标，于是前一项变成负数，正好是 Hit.Lead。
            // 这里统一走 NoteMotion.X —— 它比轨道里那份内联公式只多一个 xoffsetind{lane}，
            // 而该 mod 不在 VSB 的二进制表里，非自制谱恒为 0，两者等价。
            float cx = (float)(NoteSkin.LaneX(wide ? 1 : 0, chartLane) + NoteMotion.X(map, h.Time, lane, h.Lead + session.Project.VisualDelayMs));
            float cy = (float) JudgmentLineY(map, h.Time);
            // 种子只由这次判定自身的时间与轨道导出，因此在编辑器里增删别的音符不会打乱已有音符的颗粒分布。
            uint seed = unchecked((uint)(long) Math.Round(h.Time * 1000) * 131u + (uint) lane * 17u);
            if (h.Kind == ScoreState.HitKind.HoldTick)
            {
                // 原版 obj_pressed_holds 对每个 piece 只传 arg7：没有指示框，也没有音符颗粒，只有两簇钻尘。
                DrawHitDust(cx - 11, cy, age, seed + 128, -.4f, -1.3f, DustLife, judgementColors[JudgementTier]);
                DrawHitDust(cx + 11, cy, age, seed + 192, .4f, -1.3f, DustLife, judgementColors[JudgementTier]);
                continue;
            }
            DrawHitIndicator(cx, cy, age, wide);
            if (wide)
            {
                // 宽键横跨三个常规轨道，颗粒也铺三簇。
                for (int cluster = -1; cluster <= 1; cluster++)
                {
                    DrawHitNoteParticles(cx + cluster * 22, cy, age, seed + (uint)(cluster + 1) * 64);
                }
                // 钻尘按轨道朝外侧甩：xspd 量级 5 是常规组的十几倍，yspd 只有 0.4，所以宽键的尘几乎是横着飞的。
                (float dx, float vx, float vy)[] bursts = lane switch
                {
                    4 => [(0, -5, -.4f), (22, -5, .4f)],
                    5 => [(0, -5, -.4f), (0, 5, -.4f)],
                    _ => [(0, 5, -.4f), (-22, 5, .4f)]
                };
                for (int b = 0; b < bursts.Length; b++)
                {
                    DrawHitDust(cx + bursts[b].dx, cy, age, seed + (uint)(b + 8) * 64, bursts[b].vx, bursts[b].vy, WideDustLife,
                        judgementColors[JudgementTier]);
                }
            }
            else
            {
                // ±11 就是 sp_note_chip_normal（22×7、原点居中）的左右两端。
                DrawHitNoteParticles(cx - 11, cy, age, seed);
                DrawHitNoteParticles(cx + 11, cy, age, seed + 64);
                DrawHitDust(cx - 11, cy, age, seed + 128, -.4f, -1.3f, DustLife, judgementColors[JudgementTier]);
                DrawHitDust(cx + 11, cy, age, seed + 192, .4f, -1.3f, DustLife, judgementColors[JudgementTier]);
            }
        }
    }

    /// <summary>
    /// o_combodisplay 每次判定从连击数两侧向外喷的钻尘。它带 drawInGui，属于 HUD 而不是轨道，
    /// 所以画在 HUD 批次里，落点由 GameUiRenderer 量出的字宽决定（原版 160 ± (string_width / 2 + 6)）。
    /// 这几簇没有传 judgement，原版 switch 落不到任何 case，image_blend 保持白色。
    /// 这些 GUI 粒子不受 uialpha 控制；hide_combo 在命中发生时阻止新粒子生成，
    /// 已生成的粒子继续按自身寿命淡出，因此检查 hitTime 而不是当前帧。
    /// 深度 depth - 1 比连击数本身更靠前，因此必须排在 HUD 绘制之后。
    /// </summary>
    void DrawComboParticles(Session session, double t)
    {
        foreach (var hit in session.Score.HitsBetween(t - ComboDustAlpha * ComboDustLife / 240, t))
        {
            // 字宽要按这次判定当时的连击数来量，不能用当前帧的：数字位数一变，喷射点会整体外扩。
            double hitTime = hit.Time;
            if (session.Timeline.Get("hide_combo", hitTime) != 0)
            {
                continue;
            }
            if (gameUi.ComboDustOffset(session, hitTime) is not { } offset)
            {
                continue;
            }
            uint seed = unchecked((uint)(long) Math.Round(hitTime * 1000) * 977u);
            for (int burst = 0; burst < 4; burst++)
            {
                float vx = burst < 2 ? -1 : 1, vy = (burst % 2 == 0 ? -1 : 1) * .4f;
                DrawHitDust(160 + vx * offset, (float) ComboDustY, t - hitTime, seed + (uint) burst * 64, vx, vy, ComboDustLife,
                    Color.White, ComboDustCount, ComboDustAlpha);
            }
        }
    }

    /// <summary>
    /// 原版 TARGET_Y_POS()：判定线被 driven 按当前 BPM 顶离 144。特效在创建时取一次，之后不再跟随判定线。
    /// </summary>
    static double JudgmentLineY(Timeline map, double time) => 144 - map.Get("driven", time) * map.Bpm.BpmAtBeat(map.Bpm.Beat(time))
        / 60 * map.Get("scrollspeed", time) * map.Get("velocity", time) * 22;

    /// <summary>
    /// obj_noteIndicatorNew / obj_noteIndicatorWideNew：判定线上张开的 1px 空心矩形，四条边由 sp_pixel 拉伸而成。
    /// 半宽 0→15（宽键 27）、半高 0→7 各用 1/3 秒的 EaseOutExpo；透明度延迟 1/6 秒后再用 1/3 秒落到 0。
    /// 指数曲线自己永远到不了终点，是补间跑满后被直接 snap 过去的，Easings.Eval 在端点精确返回 0/1，语义一致。
    /// round 与右边界的 -1 照抄原版：常规键的矩形是左闭右开、比宽键窄一像素，去掉就会左右不对称。
    /// </summary>
    void DrawHitIndicator(float cx, float cy, double age, bool wide)
    {
        if (age >= IndicatorLife)
        {
            return;
        }
        double grow = Easings.Eval("outExpo", age / IndicatorGrow);
        double alpha = age < IndicatorFadeDelay ? 1 : 1 - Easings.Eval("outExpo", (age - IndicatorFadeDelay) / IndicatorGrow);
        float dx = (float)((wide ? 27 : 15) * grow), dy = (float)(7 * grow);
        float x1 = MathF.Round(cx - dx), x2 = MathF.Round(wide ? cx + dx : cx - 1 + dx);
        float y1 = MathF.Round(cy - dy), y2 = MathF.Round(cy + dy);
        var color = judgementColors[JudgementTier].Alpha(alpha);
        canvas.Fill(new(x1, y1, x2 - x1 + 1, 1), color);
        canvas.Fill(new(x1, y2, x2 - x1 + 1, 1), color);
        // 矩形刚张开的那一帧上下边重合，左右两条竖边高度为负。原版靠精灵翻转糊过去，
        // 这里直接不画：负高度会在 GPU 上变成一个反向四边形。
        if (y2 - y1 > 1)
        {
            canvas.Fill(new(x1, y1 + 1, 1, y2 - y1 - 1), color);
            canvas.Fill(new(x2, y1 + 1, 1, y2 - y1 - 1), color);
        }
    }

    /// <summary>
    /// o_pt_note：spawn_particles_area_variant 撒出的一簇 45° 小菱形，向四周匀速散开。
    /// 透明度每秒掉 240/lifetime，缩放恒等于 2×透明度，所以颗粒一边淡出一边缩小成一点。
    /// 帧号直接取判定档位：sp_noteparticle 的五帧依次是淡黄、亮黄、绿、青、红，与档位一一对应。
    /// </summary>
    void DrawHitNoteParticles(float cx, float cy, double age, uint seed)
    {
        double alpha = 1 - age * 240 / NoteParticleLife;
        if (alpha <= 0)
        {
            return;
        }
        // 精灵 4×4、原点居中，缩放 2α 之后半边长就是 4α。
        float half = (float)(4 * alpha), travel = (float)(240 * age);
        for (int i = 0; i < NoteParticleCount; i++)
        {
            // 原版是 (random(2) - 1) * 幅度，即以 0 为中心的双向散布。
            float vx = (Timeline.Hash(seed + (uint) i * 2) * 2 - 1) * .75f;
            float vy = (Timeline.Hash(seed + (uint) i * 2 + 1) * 2 - 1) * .4f;
            float x = cx + vx * travel, y = cy + vy * travel;
            canvas.Quad(noteParticles[JudgementTier], new(x - half, y - half, half * 2, half * 2), Color.White.Alpha(alpha), angle: 45);
        }
    }

    /// <summary>
    /// o_pt_diamonddust2：spawn_particles_directional 撒出的一簇钻尘，只朝给定方向的半区散开（random(0.5) 恒为正）。
    /// 速度每帧还要再乘一次当前透明度，所以位移是透明度对时间的积分，不是匀速：颗粒越淡走得越慢，最后停住。
    /// 精灵用的是项目已有的 pt_diamonddust。tint 按判定档位染色；连击数两侧那两簇没有传 judgement，
    /// 原版 switch 落不到任何 case，image_blend 保持白色，所以调用方直接传白。
    /// count 是每簇颗粒数，start 是起始透明度：命中用 (4, 0.8)，连击数两侧用 (2, 0.5)。
    /// </summary>
    void DrawHitDust(float cx, float cy, double age, uint seed, float xspd, float yspd, double life, Color tint, int count = DustCount,
        double start = DustAlpha)
    {
        double fade = 240 / life, alpha = start - age * fade;
        if (alpha <= 0)
        {
            return;
        }
        // ∫₀^age (start - fade·s) ds，乘上每秒 240 逻辑像素的基准速度。
        float travel = (float)(240 * (start * age - fade * age * age / 2));
        var color = tint.Alpha(alpha);
        for (int i = 0; i < count; i++)
        {
            float vx = Timeline.Hash(seed + (uint) i * 2) * .5f * xspd;
            float vy = Timeline.Hash(seed + (uint) i * 2 + 1) * .5f * yspd;
            // irandom(image_number) 取的是 0..4 的闭区间，而精灵只有四帧，4 会被 GameMaker 绕回 0，
            // 所以第 0 帧的出现概率是其余帧的两倍。照搬这个偏斜。
            var texture = dustTextures[(int)(Timeline.Hash(seed + (uint) i * 2 + 128) * 5) % dustTextures.Length];
            canvas.Quad(texture, new(cx + vx * travel - 4, cy + vy * travel - 4, 9, 9), color);
        }
    }
}
