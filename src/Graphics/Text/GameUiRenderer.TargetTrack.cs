// 绘制目标轨道的文字标签。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class GameUiRenderer
{
    /// <summary>绘制目标轨道旁的游戏文字标签。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
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
            // draw_text_o 将传入 y 减 1，不绘制轮廓。
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
