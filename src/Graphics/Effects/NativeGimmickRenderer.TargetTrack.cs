using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
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
