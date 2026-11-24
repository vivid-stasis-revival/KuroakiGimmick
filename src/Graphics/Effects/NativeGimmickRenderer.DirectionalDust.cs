// 处理 Starcrashers 后段回调以及 Allison 音符皮肤的动态混合。

using System;
using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    private Session? lateSession;
    private DirectionalDustTimeline? lateEvents;
    private DirectionalDustTimeline? Late(Session session)
    {
        if (!session.NativeGimmick.HasBehavior("masked-starfield") || current?.Data == null) return null;
        if (!ReferenceEquals(lateSession, session))
        {
            lateSession = session;
            lateEvents = new DirectionalDustTimeline(session);
        }
        return lateEvents;
    }

    /// <summary>原版 obj_starcrashers_mask.Draw 在切换到遮罩目标前，以反目标色和反源色将 o_pt_diamonddust_invmask 绘制到场景；此处的合成顺序依赖这一目标切换。</summary>
    /// <param name="scene">要绘制到的场景目标。</param>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    public void DrawMaskedStarFieldLateDust(Target scene, Session session, double time)
    {
        Prepare(session);
        var data = Late(session);
        if (data == null || !current!.Sprites.TryGetValue("pt_diamonddust", out var spec)) return;
        int tick = Math.Max(0, (int)Math.Floor(time * 60 + 1e-8));
        // 较慢的尘埃可能仍存活，需要遍历截至当前的出生记录，每会话上限 25000。
        canvas.Begin(scene, scene.Texture.Width, scene.Texture.Height, 320, 180);
        canvas.Blend(BlendFactor.InverseDestinationColor, BlendFactor.InverseSourceColor);
        foreach (var dust in data.Diamonds)
        {
            if (dust.Birth > tick) break;
            if (dust.Death <= tick) continue;
            if (Image("pt_diamonddust", dust.Frame) is not { } frame) break;
            var position = data.PositionAt(dust, tick);
            canvas.Quad(frame, new(position.X - spec.OriginX, position.Y - spec.OriginY, spec.Width, spec.Height), Color.White);
        }
        canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        canvas.Flush();
    }

    /// <summary>按 GameMaker 的 round(mod × 255) / 255 量化混合值；原版在 107×22 的 surface 上混合普通与 Allison 皮肤。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>按源规则量化的皮肤混合系数。</returns>
    public static double GreenleftAlpha(Timeline timeline, double time)
    {
        return PartialSkinBlendState.At(timeline, time).Alpha;
    }

    private void StarcrashersSprite(Session session, string spriteName, int frame, float x, float y, double alpha,
        float rotation = 0, float scaleY = 1, float scaleX = 1)
    {
        if (alpha <= 0 || current == null || !current.Sprites.TryGetValue(spriteName, out var spec)) return;
        if (Image(spriteName, frame) is not { } image) return;
        var transform = Matrix3x2.CreateTranslation(-spec.OriginX, -spec.OriginY)
            * Matrix3x2.CreateScale(scaleX, scaleY)
            * Matrix3x2.CreateRotation(-rotation * MathF.PI / 180)
            * Matrix3x2.CreateTranslation(x, y);
        canvas.Polygon(image,
            Vector2.Transform(new(0, 0), transform), Vector2.Transform(new(spec.Width, 0), transform),
            Vector2.Transform(new(0, spec.Height), transform), Vector2.Transform(new(spec.Width, spec.Height), transform),
            Color.White.Alpha(alpha));
    }

    /// <summary>根据 obj_starcrashers_gimmick.Step_1 处理左侧两个 chip 轨道及首个撞块。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="type">来源音符类型编号。</param>
    /// <param name="lane">音符的轨道编号。</param>
    /// <param name="x">逻辑横坐标。</param>
    /// <param name="y">逻辑纵坐标。</param>
    /// <param name="baseAlpha">混合前的不透明度。</param>
    /// <param name="rotation">旋转角度。</param>
    public void DrawMaskedStarFieldNoteBlend(Session session, double time, int type, int lane, float x, float y,
        double baseAlpha, float rotation)
    {
        if (!session.NativeGimmick.HasBehavior("masked-starfield")) return;
        double alpha = GreenleftAlpha(session.Timeline, time) * baseAlpha;
        if (alpha <= 0) return;
        Prepare(session);
        if (type == 0 && lane is 0 or 1)
            StarcrashersSprite(session, "sp_note_chip_allison", 0, x, y, alpha, rotation);
        else if (type == 1 && lane == 0)
            StarcrashersSprite(session, "sp_note_bumper_allison", 0, x, y, alpha, rotation);
        else if (type == 8 && lane == 0)
            StarcrashersSprite(session, "sp_note_bumper_timing_allison", 0, x, y, alpha, rotation);
    }

    /// <summary>绘制叠加皮肤与原长条帧之间的混合。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="lane">音符的轨道编号。</param>
    /// <param name="x">逻辑横坐标。</param>
    /// <param name="startY">长条起点的逻辑纵坐标。</param>
    /// <param name="endY">长条终点的逻辑纵坐标。</param>
    /// <param name="baseAlpha">混合前的不透明度。</param>
    /// <param name="pressed">是否绘制按下状态。</param>
    public void DrawMaskedStarFieldHoldBlend(Session session, double time, int lane, float x, float startY, float endY,
        double baseAlpha, bool pressed)
    {
        if (!session.NativeGimmick.HasBehavior("masked-starfield") || lane is not (0 or 1)) return;
        double alpha = GreenleftAlpha(session.Timeline, time) * baseAlpha;
        if (alpha <= 0) return;
        Prepare(session);
        endY += 6;
        float top = Math.Min(startY, endY), bottom = Math.Max(startY, endY);
        if (pressed)
        {
            top = Math.Min(top, 140);
            bottom = Math.Min(bottom, 140);
        }
        float bodyTop = top + 4, bodyBottom = pressed ? bottom + 4 : bottom - 3;
        float visibleTop = Math.Max(-8, bodyTop), visibleBottom = Math.Min(188, bodyBottom);
        if (visibleBottom > visibleTop)
            StarcrashersSprite(session, "sp_note_hold_allison", 0, x, visibleTop, alpha,
                scaleY: visibleBottom - visibleTop);
        StarcrashersSprite(session, "sp_note_chip_allison", 0, x, top, alpha);
        if (!pressed) StarcrashersSprite(session, "sp_note_chip_allison", 0, x, bottom, alpha);
    }
}
