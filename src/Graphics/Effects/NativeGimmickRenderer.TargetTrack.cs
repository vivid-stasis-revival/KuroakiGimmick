// 绘制目标轨道的结尾画面。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    /// <summary>绘制目标轨道组件的结尾图层。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    public void DrawTargetTrackEnding(Session session, double time)
    {
        if (!session.NativeGimmick.HasBehavior("target-track")) return;
        Prepare(session);
        double alpha = session.Timeline.Get("eo_endcg", time);
        if (alpha > 0 && Image("sp_cg_train") is { } texture)
            canvas.Quad(texture, new(0, 0, 320, 180), Color.White.Alpha(alpha));
        canvas.Flush();
    }
}
