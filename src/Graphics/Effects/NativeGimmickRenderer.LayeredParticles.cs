// 按阶段绘制 Plaudite 粒子、斩击和结尾画面。

using KuroakiGimmick.Core;
using System.Numerics;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Timeline? plauditeTimeline;
    LayeredParticlesState? plauditeObjects;
    private LayeredParticlesState? PreparePlaudite(Session session)
    {
        if (!session.NativeGimmick.HasBehavior("jacket-particles")) return null;
        Prepare(session);
        if (!ReferenceEquals(plauditeTimeline, session.Timeline))
        {
            plauditeTimeline = session.Timeline;
            plauditeObjects = new(session.Timeline);
        }
        return plauditeObjects;
    }

    /// <summary>在指定的斩击阶段绘制 Plaudite 的斩线。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="neutral">是否绘制中性色斩线。</param>
    public void DrawLayeredParticlesSlashes(Session session, double time, bool neutral)
    {
        if (PreparePlaudite(session) == null) return;
        canvas.Blend(neutral ? BlendFactor.SourceAlpha : BlendFactor.InverseDestinationColor,
            neutral ? BlendFactor.InverseSourceAlpha : BlendFactor.Zero);
        foreach (var e in session.Timeline.Callbacks)
        {
            if (e.Time > time) break;
            if (e.Time <= time - 1 || (neutral ? e.Name != "plaudite_slash_neutral" : e.Name is not ("plaudite_slash_saturday" or "plaudite_slash_dawn"))) continue;
            uint color = e.Name == "plaudite_slash_saturday" ? 14418175u : e.Name == "plaudite_slash_dawn" ? 16711731u : 0;
            uint seed = unchecked((uint)e.Index * 65537 + 9127);
            float width = (float)(12 * Math.Pow(1 - (time - e.Time), 2));
            canvas.Line(-6, Timeline.Hash(seed) * 180, 326, Timeline.Hash(seed + 1) * 180, width, Color.GameMaker(color));
        }
        canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
    }

    /// <summary>依据前后层选择绘制当前可见的粒子。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="front">是否绘制前层粒子。</param>
    public void DrawLayeredParticlesParticles(Session session, double time, bool front)
    {
        if (PreparePlaudite(session) is not { } state || current == null) return;
        IEnumerable<Timeline.Dust> Active(List<Timeline.Dust> particles)
        {
            for (int i = SortedSearch.LowerBound(particles, time - 4, p => p.Time); i < particles.Count && particles[i].Time <= time; i++)
                yield return particles[i];
        }
        double inverseAlpha = session.Timeline.Get("plaudite_sect7bg", time);
        var currentOffset = session.Timeline.ParticleOffset(time);
        foreach (var particle in Active(session.Timeline.Particles).Concat(Active(state.Bursts)))
        {
            if (particle.Time > time || time >= particle.Death || !particle.Burst && particle.Time >= state.EndingTime) continue;
            var colorState = state.ParticleState(particle.Time, time);
            if (colorState.Front != front) continue;
            double age = time - particle.Time;
            var offset = currentOffset - session.Timeline.ParticleOffset(particle.Time);
            float x = particle.X + particle.Vx * (float)age * 60 + offset.X, y = particle.Y + particle.Vy * (float)age * 60 + offset.Y;
            var color = colorState.Red ? Color.GameMaker(255) : Color.Hsv(particle.Hue, particle.Saturation);
            if (Image("pt_diamonddust", particle.Frame) is { } image)
                canvas.Quad(image, new(x - 4, y - 4, 9, 9), color.Alpha(1 - age / particle.Life));
            // 源 Draw 显式传入这个 alpha，独立于 image_alpha。
            if (inverseAlpha > 0 && Image("pt_invertdiamond", particle.Frame) is { } inverse)
                canvas.Quad(inverse, new(x - 4, y - 4, 9, 9), Color.White.Alpha(inverseAlpha));
        }
    }

    /// <summary>绘制分层粒子组件的结尾场景。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    public void DrawLayeredParticlesFinal(Session session, double time)
    {
        if (PreparePlaudite(session) is not { } state || time < state.EndingTime) return;
        canvas.Fill(new(0, 0, 320, 180), Color.White);
        double age = time - state.EndingTime;
        if (age < 2) canvas.Line(-8, 109, 328, 65, (float)(12 * Math.Pow(1 - age / 2, 2)), Color.GameMaker(0));
    }

    /// <summary>在 GUI 阶段绘制分层粒子演出的结尾覆盖层。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    public void DrawLayeredParticlesEndingGui(Session session, double time)
    {
        if (PreparePlaudite(session) == null) return;
        foreach (var e in session.Timeline.Callbacks.Where(e => e.Name == "plaudite_ending_event" && e.Time <= time && time < e.Time + 7))
        {
            double age = time - e.Time, eased = Easings.Eval("outExpo", age / 4), alpha = age < 3 ? 1 : 1 - (age - 3) / 4;
            DrawSequenceSprite(session, "sp_plaudite_finalplay1", 0, 114 - 45 * eased, 6 * eased, 1, 1, alpha, time);
            DrawSequenceSprite(session, "sp_plaudite_finalplay2", 0, 114 + 37 * eased, 82 - 5 * eased, 1, 1, alpha, time);
        }
        double transition = Math.Max(session.NativeGimmick.Behavior("jacket-particles")!.Number("transitionTime", 0), session.Timeline.SequenceTriggerTime("setup_co_end")) + 1;
        if (time >= transition && Image("sp_transition_diamond") is { } diamond)
        {
            double age = Math.Clamp(time - transition, 0, 1);
            double scale = Easings.Eval("outQuad", age), rotation = Easings.Eval("outExpo", age) * -2 * Math.PI;
            var matrix = Matrix3x2.CreateTranslation(-1, -1) * Matrix3x2.CreateScale((float)(1 + 319 * scale), (float)(1 + 179 * scale))
                * Matrix3x2.CreateRotation((float)rotation) * Matrix3x2.CreateTranslation(160, 90);
            // 源 Draw 固定传入 alpha=1；TweenEasyFade 写入的 image_alpha 在此未被读取。
            canvas.Polygon(diamond, Vector2.Transform(Vector2.Zero, matrix), Vector2.Transform(new(2, 0), matrix),
                Vector2.Transform(new(0, 2), matrix), Vector2.Transform(new(2, 2), matrix), Color.White);
        }
    }
}
