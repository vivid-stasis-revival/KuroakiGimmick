using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Session? stargazersOwner;
    StargazersState? stargazersState;
    public void DrawStargazersBackground(Session session, double time)
    {
        if (session.Chart.ObjectName != "obj_stargazers_gimmick") return;
        Prepare(session);
        double alpha = session.Timeline.Get("sg_bg_alpha", time) * .5;
        if (Image("sp_stargazers_bg") is { } backgroundImage)
            canvas.Quad(backgroundImage, new(0, 0, 320, 180), Color.White.Alpha(alpha));
        if (Image("sp_stargazers_checkers") is { } checks)
        {
            double offset = -60 * GimmickRoomLayerTimeline.IntegratedFrames(session.Timeline, "sg_bg_speed", time);
            double top = GimmickRoomLayerTimeline.Wrap(offset, checks.Height) - checks.Height;
            for (double y = top; y < 180; y += checks.Height)
                // Step_1 passes the layer ID (checkers), not bg_checkers, to
                // layer_background_alpha. Do not silently repair that invalid
                // element lookup into a new .5-alpha visual effect.
                canvas.Quad(checks, new(0, (float)y, checks.Width, checks.Height), Color.White);
        }
        canvas.Flush();
    }
    public void DrawStargazersStars(Session session, double time, bool fromSpawner)
    {
        if (session.Chart.ObjectName != "obj_stargazers_gimmick") return;
        // Depth 101 GUI stars are erased by inherited base Draw GUI's clear when
        // proxies exist. Callback-created stars use depth 0 and draw after it.
        if (fromSpawner && session.Chart.Proxies > 0) return;
        Prepare(session);
        if (!ReferenceEquals(stargazersOwner, session))
        { stargazersOwner = session; stargazersState = new(session.Timeline, session.Chart, session.Duration); }
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
    public void DrawStargazersEndBlip(Session session, double time)
    {
        if (session.Chart.ObjectName != "obj_stargazers_gimmick") return;
        if (StargazersState.EndBlipBirth(session.Timeline, time) is not { } birth) return;
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
