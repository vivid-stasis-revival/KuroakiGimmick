using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class GameUiRenderer
{
    public void DrawTargetTrackLabels(Session session, double time)
    {
        if (!session.NativeGimmick.HasBehavior("target-track")) return;
        double alpha = session.Timeline.Get("track_alpha", time);
        if (alpha <= 0) return;
        Use(session.GameUi);
        if (current?.Data == null) return;
        string font = Font(session);
        void Label(string label, string mod)
        {
            // draw_text_o subtracts one from its y argument; it does not draw an outline.
            double y = 1 + session.Timeline.Get(mod, time) * 1.53;
            if (!double.IsFinite(y)) return;
            float x = 99 - Width(font, label);
            var white = Color.White.Alpha(alpha);
            Text(font, label, x, (float)y, white, white);
        }
        Label("YOU", "sekaisen_arrow_point");
        Label("TARGET", "sekaisen_target_point");
    }
}
