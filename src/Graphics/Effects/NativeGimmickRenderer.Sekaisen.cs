using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    public bool DrawSekaisenJacket(Session session, double time)
    {
        if (session.Chart.ObjectName != "obj_sekaisen_gimmick") return false;
        Prepare(session);
        double value = session.Timeline.Get("sekaisen_jacket", time);
        if (!double.IsFinite(value) || current == null || !current.Sprites.TryGetValue("sp_sekaisen_jacket", out var sprite)) return false;
        int frame = (int)((Math.Floor(value) % sprite.Frames.Count + sprite.Frames.Count) % sprite.Frames.Count);
        if (Image("sp_sekaisen_jacket", frame, linear:true) is not { } texture) return false;
        canvas.Blend(BlendFactor.DestinationColor, BlendFactor.Zero);
        canvas.Quad(texture, new(0, 0, 320, 180), Color.White);
        canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        canvas.Flush();
        return true;
    }

    public void DrawSekaisenEnding(Session session, double time)
    {
        if (session.Chart.ObjectName != "obj_sekaisen_gimmick") return;
        Prepare(session);
        double alpha = session.Timeline.Get("eo_endcg", time);
        if (alpha > 0 && Image("sp_cg_train") is { } texture)
            canvas.Quad(texture, new(0, 0, 320, 180), Color.White.Alpha(alpha));
        canvas.Flush();
    }
}
