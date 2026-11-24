// 绘制侧边星场、背景与结束闪烁。

using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Session? stargazersOwner;
    SideStarFieldState? stargazersState;
    /// <summary>绘制侧边星场组件的背景。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    public void DrawSideStarFieldBackground(Session session, double time)
    {
        if (!session.NativeGimmick.HasBehavior("side-starfield")) return;
        Prepare(session);
        double alpha = session.Timeline.Get("sg_bg_alpha", time) * .5;
        if (Image("sp_stargazers_bg") is { } backgroundImage)
            canvas.Quad(backgroundImage, new(0, 0, 320, 180), Color.White.Alpha(alpha));
        if (Image("sp_stargazers_checkers") is { } checks)
        {
            double offset = -60 * GimmickRoomLayerTimeline.IntegratedFrames(session.Timeline, "sg_bg_speed", time);
            double top = GimmickRoomLayerTimeline.Wrap(offset, checks.Height) - checks.Height;
            for (double y = top; y < 180; y += checks.Height)
                // Step_1 将图层 ID checkers 而非背景元素 bg_checkers 传入 layer_background_alpha；保留无效查找的表现，不额外加半透明层。
                canvas.Quad(checks, new(0, (float)y, checks.Width, checks.Height), Color.White);
        }
        canvas.Flush();
    }
    /// <summary>按生成来源筛选并绘制指定时刻的侧边星体。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="fromSpawner">是否选择由生成器自动创建的实例。</param>
    public void DrawSideStarFieldStars(Session session, double time, bool fromSpawner)
    {
        if (!session.NativeGimmick.HasBehavior("side-starfield")) return;
        // 存在 proxy 时，基础 Draw GUI 的清屏会抹去深度 101 的星体；由回调创建的深度 0 星体在清屏之后绘制。
        if (fromSpawner && session.NativeApplicationProxyEnabled) return;
        Prepare(session);
        if (!ReferenceEquals(stargazersOwner, session))
        {
            stargazersOwner = session;
            stargazersState = new(session.Timeline, session.Chart, session.Duration);
        }
        foreach (var star in stargazersState!.At(time, fromSpawner))
        {
            if (Image("sp_sg_star", star.Frame) is not { } image || !current!.Sprites.TryGetValue("sp_sg_star", out var sprite)) continue;
            var matrix = Matrix3x2.CreateTranslation(-sprite.OriginX, -sprite.OriginY)
                * Matrix3x2.CreateRotation((float)(-star.Angle * Math.PI / 180))
                * Matrix3x2.CreateTranslation((float)star.X, (float)star.Y);
            canvas.Polygon(image, Vector2.Transform(Vector2.Zero, matrix), Vector2.Transform(new(sprite.Width, 0), matrix),
                Vector2.Transform(new(0, sprite.Height), matrix), Vector2.Transform(new(sprite.Width, sprite.Height), matrix),
                Color.Hsv((float)star.Hue, 128f / 255).Alpha(star.Alpha));
        }
        canvas.Flush();
    }
    /// <summary>绘制侧边星场结尾触发的闪烁。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    public void DrawSideStarFieldEndBlip(Session session, double time)
    {
        if (!session.NativeGimmick.HasBehavior("side-starfield")) return;
        if (SideStarFieldState.EndBlipBirth(session.Timeline, time) is not { } birth) return;
        DrawNativeEndBlip(session, time, birth);
    }
    void DrawNativeEndBlip(Session session, double time, double birth)
    {
        Prepare(session);
        canvas.Fill(new(0, 0, 320, 180), Color.Hex(0));
        double progress = Math.Clamp(time - birth, 0, 1);
        double scale = progress >= 1 ? 0 : 10 * Math.Pow(2, -10 * progress);
        DrawSequenceSprite(session, "sp_sg_endblip", 0, 160, 90, 10, scale, 1, time);
        canvas.Flush();
    }
}
