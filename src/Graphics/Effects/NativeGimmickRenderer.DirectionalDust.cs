using System;
using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>Additional late Starcrashers callbacks and dynamic Allison note-skin blend.</summary>
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

    /// <summary>
    /// Original obj_starcrashers_mask.Draw draws o_pt_diamonddust_invmask onto the
    /// SCENE using inverse-destination/inverse-source blending BEFORE targeting its mask surface.
    /// Calling this inside DrawMask would be a different and incorrect composition.
    /// </summary>
    public void DrawMaskedStarFieldLateDust(Target scene, Session session, double time)
    {
        Prepare(session);
        var data = Late(session);
        if (data == null || !current!.Sprites.TryGetValue("pt_diamonddust", out var spec)) return;
        int tick = Math.Max(0, (int)Math.Floor(time * 60 + 1e-8));
        // Slower dust can linger. Iterate all births up to now, bounded by 25k/session.
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

    /// <summary>GameMaker round(mod * 255)/255; original mixes normal and Allison in a 107x22 surface.</summary>
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

    /// <summary>Left two chip lanes and first bumper: source obj_starcrashers_gimmick.Step_1.</summary>
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

    public void DrawMaskedStarFieldHoldBlend(Session session, double time, int lane, float x, float startY, float endY,
        double baseAlpha, bool pressed)
    {
        if (!session.NativeGimmick.HasBehavior("masked-starfield") || lane is not (0 or 1)) return;
        double alpha = GreenleftAlpha(session.Timeline, time) * baseAlpha;
        if (alpha <= 0) return;
        Prepare(session);
        endY += 6;
        float top = Math.Min(startY, endY), bottom = Math.Max(startY, endY);
        if (pressed) { top = Math.Min(top, 140); bottom = Math.Min(bottom, 140); }
        float bodyTop = top + 4, bodyBottom = pressed ? bottom + 4 : bottom - 3;
        float visibleTop = Math.Max(-8, bodyTop), visibleBottom = Math.Min(188, bodyBottom);
        if (visibleBottom > visibleTop)
            StarcrashersSprite(session, "sp_note_hold_allison", 0, x, visibleTop, alpha,
                scaleY: visibleBottom - visibleTop);
        StarcrashersSprite(session, "sp_note_chip_allison", 0, x, top, alpha);
        if (!pressed) StarcrashersSprite(session, "sp_note_chip_allison", 0, x, bottom, alpha);
    }
}
