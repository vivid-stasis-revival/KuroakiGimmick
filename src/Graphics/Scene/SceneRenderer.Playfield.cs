using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 场景合成协调器。对象背景、资源图层、轨道、固定 HUD 与后处理按显式边界组织；预览和视频导出共用此入口。
/// </summary>
public sealed partial class SceneRenderer
{
    /// <summary>
    /// 把透明的 field 经 fieldComposite 反预乘后贴回 scene，透明度只被乘一次。
    /// Proxy 按原版范围裁剪轨道，灰色判定背景随裁剪区域一起变换；
    /// Base / Custom 不在源位置重复留下底部灰条，歌曲信息底栏仍由独立 HUD 绘制。
    /// </summary>
    void CompositeField(Session session, double time, bool proxyMode, bool clearFooter)
    {
        CaptureWindowField(session);
        var timeline = session.Timeline;
        bool separateHudFooter = session.Chart.ObjectName is "obj_base_gimmick" or "obj_custom_gimmick";
        double M(string name, int proxy = -1) => timeline.Get(name, time, proxy);
        canvas.Begin(scene, RenderWidth, RenderHeight, 320, 180);
        // 固定侧边保留谱面图片；每份轨道副本在所有深度 pass 上
        // 使用同一套裁剪、位移、形变与不透明度。
        if (!proxyMode)
        {
            canvas.Quad(field.Texture, new(0, 0, 320, 180), Color.White, shader: fieldComposite);
        }
        else
        {
            canvas.Quad(field.Texture, new(0, 0, 81, 180), Color.White.Alpha(1 - M("hom")), new(0, 0, 81f / 320, 1), shader: fieldComposite);
            canvas.Quad(field.Texture, new(239, 0, 81, 180), Color.White.Alpha(1 - M("hom")), new(239f / 320, 0, 81f / 320, 1),
                shader: fieldComposite);
            // Base / Custom 的 y=165..180 属于裁剪外区域，不在源位置再留一份灰条。
            if (!separateHudFooter)
                canvas.Quad(field.Texture, new(81, 165, 158, 15), Color.White.Alpha(1 - M("hom")), new(81f / 320, 165f / 180, 158f / 320, 15f / 180),
                    shader: fieldComposite);
            // Do not paint an opaque "clear" rectangle here. The footer is a transparent render-layer concern;
            // drawing black into scene made uialpha=0 leave a permanent strip over text/image gimmicks (#18).
            _ = clearFooter;
        }
        // 启用 proxy 的谱面用自己的 proxy 副本替换中央游玩轨道；索引从大到小，即从后往前绘制。
        for (int p = proxyMode ? session.Chart.Proxies - 1 : -1; p >= 0; p--)
        {
            bool custom = session.Chart.ObjectName == "obj_custom_gimmick";
            var alpha = M("pra", p);
            if (alpha <= 0)
            {
                continue;
            }
            double scale = M("przm", p) * M("przmb", p) * M("przmc", p), sx = scale * M("przx", p) * Math.Cos(M("prrx", p) * Math.PI / 180),
                sy = scale * M("przy", p) * Math.Cos(M("prry", p) * Math.PI / 180);
            // Custom charts magnify tiny source patches to make full-screen masks.
            // The original draw accepts these scales; rejecting >50 drops authored backgrounds.
            if (!double.IsFinite(sx) || !double.IsFinite(sy) ||
                (!custom && (Math.Abs(sx) > 50 || Math.Abs(sy) > 50)))
            {
                continue;
            }
            double x = M("prx", p) + M("prxb", p) + M("prxc", p) + M("prxd", p), y = M("pry", p) + M("pryb", p) + M("pryc", p) + M("pryd", p);
            float angle = (float)((M("prrz", p) + M("prrzb", p)) * M("rotdir"));
            float left = 113 - (float) M("shxa", p), right = 208 + (float) M("shxa", p), top = 0, bottom = TrackBottom;
            // .08/0 与 .35/.35 是原版的默认取景；只有被谱面改写过才切换到自定义裁剪边界。
            if (M("prct", p) != .08 || M("prcb", p) != 0)
            {
                top = (float)(180 * M("prcb", p) - 1);
                bottom = (float)(180 - Math.Ceiling(180 * M("prct", p)));
            }
            if (M("prcl", p) != .35 || M("prcr", p) != .35)
            {
                left = (float) M("prcl", p);
                right = (float) M("prcr", p);
            }
            // 以 (160,82) 为轴心：先平移到原点，再缩放、旋转、剪切，最后平移回去并叠加 proxy 偏移。
            // 次序照搬原版，交换任意两步都会改变旋转与剪切的复合结果。
            // Custom draws at left - (113 - shxa), then applies MToOrigin(-47,-82).
            // MatrixRotateZ uses +sin in M[1], unlike draw_sprite's angle convention.
            float originX = custom ? 160 - (float) M("shxa", p) : 160;
            var matrix = Matrix3x2.CreateTranslation(-originX, -82) * Matrix3x2.CreateScale((float) sx,
                (float) sy) * Matrix3x2.CreateRotation((custom ? angle : -angle) * MathF.PI / 180) * new Matrix3x2(1, -(float) M("prsy", p), -(float) M("prsx", p),
                1, 0, 0) * Matrix3x2.CreateTranslation(160 + (float) x, 82 + (float) y);
            canvas.Flush();
            bool projective = custom && (M("prtrX", p) != 0 || M("prtrY", p) != 0);
            var shader = projective ? projectiveProxyShader : proxyShader;
            if (projective)
            {
                shader.Vec2("proxyOrigin", originX, 82);
                shader.Vec2("proxyScale", sx, sy);
                shader.Vec2("proxyRotation", Math.Cos(angle * Math.PI / 180), Math.Sin(angle * Math.PI / 180));
                shader.Vec2("proxyTrapezoid", M("prtrX", p), M("prtrY", p));
                shader.Vec2("proxySkew", -M("prsx", p), -M("prsy", p));
                shader.Vec2("proxyTranslation", 160 + x, 82 + y);
            }
            shader.Float("Time", timeline.Bpm.Beat(time));
            shader.Vec2("Texel", 1.0 / 320, 1.0 / 180);
            foreach (var (uniform, mod) in new[]
            {
                ("xspd", "shxs"),
                ("xperiod", "shxp"),
                ("xamp", "shxa"),
                ("yspd", "shys"),
                ("yperiod", "shyp"),
                ("yamp", "shya"),
                ("ct", "shct"),
                ("ft", "shft"),
                ("cb", "shcb"),
                ("fb", "shfb"),
                ("cl", "shcl"),
                ("fl", "shfl"),
                ("cr", "shcr"),
                ("fr", "shfr")
            })
            {
                shader.Float(uniform, M(mod, p));
            }
            Vector2 Point(float px, float py) => projective ? new(px, py) : Vector2.Transform(new(px, py), matrix);
            canvas.Polygon(field.Texture, Point(left, top), Point(right, top), Point(left, bottom), Point(right, bottom), Color.White.Alpha(alpha),
                new(left / 320, top / 180, (right - left) / 320, (bottom - top) / 180), shader);
            canvas.Flush();
        }
        canvas.Flush();
    }

    /// <summary>
    /// 按游戏深度边界分段绘制到透明的 field 上。固定判定装饰会中途先合成当前轨道，再回到透明目标，避免随 proxy 一起移动。
    /// 所有逐帧 mod 取值都提前算好再进入逐音符循环：每次 M(...) 都是时间轴上的二分查找，放回闭包里会在密谱上放大上千倍开销。
    /// </summary>
    void DrawNotes(Session session, double t, bool drawNotes)
    {
        var map = session.Timeline;
        double M(string n) => map.Get(n, t);
        double beat = map.Bpm.Beat(t);
        canvas.Begin(field, RenderWidth, RenderHeight, 320, 180, new(0, 0, 0, 0));
        double ui = M("bgalph"), noteAlpha = M("notealp");
        double alignment = 3 - 7 * Math.Clamp(session.Project.NoteAlignment, 0, 1);
        // 原版共用的游戏精灵，取第 0 帧，保持原点 (0,0)。
        // 灰色的 hold/判定区有自己独立的透明度。
        if (drawNotes)
        {
            canvas.Quad(laneTexture, new(0, 0, 320, 180), Color.White.Alpha(ui));
        }
        // GameMaker 的 image depth = 图层优先级取负。轨道装饰深度 200，判定覆盖层深度 0，
        // 音符 -350，游戏 HUD -1000；下面每个 DrawImages 区间就是按这些深度切出来的。
        DrawImages(session, t, -200, -100, field);
        customGimmicks.DrawStars(session, t, false);
        if (session.NativeGimmick.Data?.ReplaceJudgmentOverlay == true || session.NativeGimmick.HasStage(GimmickStages.FixedJudgment))
        {
            // 房间里深度 100 的 hold 装饰是固定的，夹在轨道对象 (200) 与音符之间；它不能进入移动的 proxy。
            // 所以在这里中途合成一次，画完固定判定装饰后再回到透明的 field 目标继续。
            CompositeField(session, t, session.Chart.Mods.Any(e => e.Proxy >= 0), clearFooter: false);
            nativeGimmick.DrawStage(session, t, GimmickStages.FixedJudgment, drawNotes);
            canvas.Begin(field, RenderWidth, RenderHeight, 320, 180, new(0, 0, 0, 0));
        }
        DrawImages(session, t, -100, 0, field);
        if (drawNotes && session.NativeGimmick.Data?.ReplaceJudgmentOverlay != true)
        {
            // 原始轨道与灰色判定背景延伸到 y=180；只有音符裁到 y=165。
            // 固定 HUD 自行覆盖底栏，uialpha=0 时仍应能看见完整灰色区域。
            canvas.Quad(nativeSequence.HoldOverlay(session, t) ?? holdOverlay, new(0, 0, 320, 180), Color.White.Alpha(M("holdoverlayalpha")));
        }
        DrawImages(session, t, 0, 10, field);
        // Custom 的旧式/具名字幕实例都位于 depth -10：判定覆盖层之后、音符和 cover (-400) 之前。
        // 放入 application surface，稍后与遮罩一起参与 Custom proxy 采样；不受 drawNotes 开关影响。
        if (session.Chart.ObjectName == "obj_custom_gimmick")
        {
            sceneFont.Draw(canvas, session, t);
        }
        DrawImages(session, t, 10, 50, field);
        checker.Draw(session, t, 2, field, RenderWidth, RenderHeight);
        DrawImages(session, t, 50, 350, field);
        if (drawNotes)
        {
            canvas.Clip(new(0, 0, 320, TrackBottom));
            if (M("driven") != 0)
            {
                canvas.Fill(new(114, (float)(144 - M("driven") * map.Bpm.BpmAtBeat(beat) / 60 * M("scrollspeed") * M("velocity") * 22), 93, 1),
                    Color.White);
            }
            // 以下取值整帧不变。旧路径在 Y()/X() 里对每个音符重新求一次，
            // 把 Timeline.Get 的二分查找次数在密谱上放大了几千倍。这项开销在
            // 最可能从 D3D12 回退到 Vulkan 的老 CPU 上尤其明显。把它们挪回闭包内即退化成旧路径。
            double visualTime = t - session.Project.VisualDelayMs / 1000;
            // freeze 非零时原版的 obj_note_rendering 用 cc.mod_freeze 顶替 cc.currentms 去算 distance，
            // 音符就钉在事件起点那一刻的位置上。别的一切照旧走真实时间：mod 继续求值，音符也继续按
            // lane.nextNote 被判掉而消失，于是冻结期间音符是一个个原地弹走、而不是整片停住不动。
            // 所以这里只替换算距离用的时钟，剔除已过音符和 hold 按住判定仍然用 visualTime。
            double freezeMs = M("freeze");
            double scrollTime = freezeMs == 0 ? visualTime : (freezeMs - session.Project.VisualDelayMs) / 1000;
            double drawUntil = M("drawuntil");
            double scrollSpeed = M("scrollspeed"), velocity = M("velocity"), yOffset = M("yoffset");
            double driven = M("driven"), wave = M("wave"), boostTime = M("boost_time"), boostDistance = M("boost_distance");
            double xOffset = M("xoffset"), beatMotion = M("beat"), noteRotation = M("noterot");
            double bpmAtBeat = map.Bpm.BpmAtBeat(beat);
            bool custom = session.Chart.ObjectName == "obj_custom_gimmick";
            // changeskin 是离散枚举轨；Timeline 在事件起点直接写 To，不做 from->to tween。
            // 所有轨道同时换整套 lane_sprites；越界由 NoteSkin 折回正常皮肤。
            int noteSkinIndex = session.SkinChangeEnabled ? (int) M("changeskin") : 0;
            var scrollByLane = noteScrollCache;
            var alphaByLane = noteAlphaCache;
            var xOffsetByLane = noteXOffsetCache;
            var yOffsetByLane = noteYOffsetCache;
            var boostTimeByLane = noteBoostTimeCache;
            var boostDistanceByLane = noteBoostDistanceCache;
            for (int lane = 0; lane < scrollByLane.Length; lane++)
            {
                scrollByLane[lane] = NoteMotion.ScrollMultiplier(scrollSpeed, M("scrollind" + lane), velocity);
                // 这些逐轨参数由 base/custom 两个公共 note renderer 都读取。对象类型只决定 boost 的插值语义，
                // 不能把已经注册在全局时间轴上的 lane mod 再在渲染末端关掉（#32）。
                alphaByLane[lane] = noteAlpha * M("notealpind" + lane);
                xOffsetByLane[lane] = xOffset + M("xoffsetind" + lane);
                yOffsetByLane[lane] = yOffset + M("yoffsetind" + lane);
                boostTimeByLane[lane] = boostTime + M("boost_timeind" + lane);
                boostDistanceByLane[lane] = boostDistance == 0 ? 0 : boostDistance + M("boost_distanceind" + lane);
            }
            double Y(double distance, int lane)
            {
                double scroll = lane >= 0 && lane < scrollByLane.Length
                    ? scrollByLane[lane]
                    : NoteMotion.ScrollMultiplier(scrollSpeed, M("scrollind" + lane), velocity);
                double laneYOffset = lane >= 0 && lane < yOffsetByLane.Length
                    ? yOffsetByLane[lane]
                    : yOffset + M("yoffsetind" + lane);
                double laneBoostTime = lane >= 0 && lane < boostTimeByLane.Length
                    ? boostTimeByLane[lane]
                    : boostTime + M("boost_timeind" + lane);
                double laneBoostDistance = lane >= 0 && lane < boostDistanceByLane.Length
                    ? boostDistanceByLane[lane]
                    : boostDistance == 0 ? 0 : boostDistance + M("boost_distanceind" + lane);
                double y = alignment + NoteMotion.YFromScroll(distance, laneYOffset, driven, bpmAtBeat / 60, scroll, wave);
                if (custom)
                {
                    // Custom 的逐轨 boost 只在全局 boost_distance 非零时启用，保持原版门控。
                    if (boostDistance != 0)
                        y += -laneBoostDistance + (distance < laneBoostTime && laneBoostTime > 0
                            ? laneBoostDistance * Math.Pow((laneBoostTime - distance) / laneBoostTime, 3)
                            : 0);
                }
                else
                {
                    y += laneBoostTime == 0
                        ? -laneBoostDistance
                        : -laneBoostDistance + laneBoostDistance * Math.Pow(Math.Clamp((laneBoostTime - distance) / laneBoostTime, 0, 1), 3);
                }
                return y;
            }
            double X(double distance, int lane)
            {
                double laneXOffset = lane >= 0 && lane < xOffsetByLane.Length
                    ? xOffsetByLane[lane]
                    : xOffset + M("xoffsetind" + lane);
                return NoteMotion.XFromValues(beat, distance, laneXOffset, beatMotion);
            }

            double futureDistance = double.NegativeInfinity;
            for (int lane = 0; lane < scrollByLane.Length; lane++)
            {
                futureDistance = Math.Max(futureDistance, NoteMotion.FutureDistance(alignment,
                    yOffsetByLane[lane], driven, bpmAtBeat / 60, scrollByLane[lane], wave,
                    boostTimeByLane[lane], boostDistanceByLane[lane], custom));
            }
            double latestStart = Math.Min(Math.BitIncrement(drawUntil / 1000), scrollTime + futureDistance / 1000);
            // 只裁剪音符，图片与歌词仍可自由覆盖整个场景。
            // 音符的横向运动不受影响；下边界跟随 field，在被复制/旋转成 proxy 时
            // 一起变换，而不是裁在屏幕空间上。
            canvas.Clip(new(0, 0, 320, TrackBottom));
            // Query by both endpoints so long holds survive, while distant stacks never enter motion evaluation.
            foreach (var n in session.NoteIndex.Candidates(visualTime, latestStart))
            {
                if (n.Time * 1000 > drawUntil) break;
                int lane = n.Type is 1 or 7 or 8 ? 4 + n.Lane : n.Lane;
                double distance = (n.Time - scrollTime) * 1000;
                double endDistance = (n.End - scrollTime) * 1000;
                double y = Y(distance, lane), endY = Y(endDistance, lane);
                double laneAlpha = lane >= 0 && lane < alphaByLane.Length
                    ? alphaByLane[lane]
                    : noteAlpha * M("notealpind" + lane);
                if (Math.Max(y, endY) < -25 || Math.Min(y, endY) > 205)
                {
                    continue;
                }
                float x = (float)(NoteSkin.LaneX(n.Type, n.Lane) + X(distance, lane));
                if (n.Type == 2)
                {
                    noteSkin.Hold(n.Lane, x, (float) y, (float) endY, laneAlpha, n.Time <= visualTime, noteSkinIndex);
                }
                else
                {
                    noteSkin.Note(n.Type, n.Lane, x, (float) y, laneAlpha, (float) noteRotation, noteSkinIndex);
                }
            }
            // 命中特效在原版是深度 -375 的独立实例，压在音符（-350）之上，共用同一块轨道裁剪。
            DrawJudgmentEffects(session, t);
            canvas.Clip(null);
        }
        DrawImages(session, t, 350, 400, field);
        customGimmicks.DrawCovers(session, t);
        DrawImages(session, t, 400, 1000, field);
        canvas.Flush();
    }
}
