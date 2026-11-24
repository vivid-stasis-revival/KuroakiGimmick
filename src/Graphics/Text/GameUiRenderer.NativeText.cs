// 绘制原版对象的文字层。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class GameUiRenderer
{
    /// <summary>依据离散文字表与 mod 状态绘制原版文字覆盖层。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="selected">仅绘制指定文字项，null 时采用已配置项目。</param>
    public void DrawNativeText(Session session, double time, GimmickTextOverlay? selected = null)
    {
        if (session.NativeGimmick.Data is not { TextOverlays.Count: > 0 } definition) return;
        Use(session.GameUi);
        if (current?.Data == null) return;
        string font = Font(session);
        IEnumerable<GimmickTextOverlay> overlays = selected == null ? definition.TextOverlays : [selected];
        foreach (var overlay in overlays)
        {
            double index = overlay.IndexMod == null ? 0 : session.Timeline.Get(overlay.IndexMod, time);
            if (!double.IsFinite(index) || index < 0 || index >= overlay.Frames.Length || overlay.Alpha <= 0) continue;
            string text = overlay.Frames[(int)index];
            double alpha = overlay.Alpha * (overlay.AlphaMod == null ? 1 : session.Timeline.Get(overlay.AlphaMod, time));
            if (alpha <= 0 || !double.IsFinite(alpha)) continue;
            float x = (float)overlay.X, y = (float)overlay.Y;
            float scale = (float)overlay.Scale;
            if (overlay.Center) x -= Width(font, text) * scale / 2;
            if (overlay.Outline)
            {
                var outline = Color.GameMaker(overlay.OutlineColorBgr).Alpha(alpha);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (dx != 0 || dy != 0) Text(font, text, x + dx * scale, y + dy * scale, outline, outline, scale: scale);
            }
            var color = Color.GameMaker(overlay.ColorBgr).Alpha(alpha);
            Text(font, text, x, y, color, Color.GameMaker(overlay.BottomColorBgr ?? overlay.ColorBgr).Alpha(alpha), scale: scale);
        }
    }
}
