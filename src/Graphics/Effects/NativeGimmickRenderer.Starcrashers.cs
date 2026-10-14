using System;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// The dynamic part of obj_starcrashers_mask.Draw_0 and behindmask_instances.
/// Requires the generic mask/room-layer patch from Starcrashers_KG_RuntimePatch.
/// No duplicate static MaskLayers draws; this contributes only live o_sc_* instances.
/// </summary>
public sealed partial class NativeGimmickRenderer
{
    private Session? starcrashOwner;
    private StarcrashersObjects? starcrashData;
    private Target? starcrashCheckerTarget;
    private int starcrashCheckerRevision = -1;

    private StarcrashersObjects? Starcrashers(Session session)
    {
        if (session.Chart.ObjectName != "obj_starcrashers_gimmick" || current?.Data == null)
            return null;
        if (ReferenceEquals(starcrashOwner, session)) return starcrashData;
        starcrashOwner = session;
        starcrashCheckerRevision = -1;
        // Bake once per Session: the editor's seek and video export read the same timeline.
        starcrashData = new StarcrashersObjects(session);
        return starcrashData;
    }

    /// <summary>Called after native room backgrounds and before the static+dynamic mask multiply.</summary>
    public void DrawStarcrashersBehindMask(Target scene, Session session, double time)
    {
        // GameMaker Step events update at 60Hz even when preview renders at 120/144Hz.
        time = Math.Floor(Math.Max(0, time) * 60 + 1e-8) / 60.0;
        Prepare(session);
        var data = Starcrashers(session);
        if (data == null) return;
        var checker = data.CheckerAt(time);
        if (checker != null && session.Timeline.Get("lasthours_checker_alpha", time) > 0)
        {
            starcrashCheckerTarget ??= new Target(canvas.Gpu, 40, 22);
            if (starcrashCheckerRevision != checker.Revision)
            {
                canvas.Begin(starcrashCheckerTarget, 40, 22, 40, 22, new(0, 0, 0, 0));
                for (int y = 0; y < 22; y++)
                    for (int x = 0; x < 40; x++)
                        if (checker.Pixels[x + y * 40])
                            canvas.Fill(new(x, y, 1, 1), Color.Hex(0xFF0000));
                canvas.Flush();
                starcrashCheckerRevision = checker.Revision;
            }
            canvas.Begin(scene, scene.Texture.Width, scene.Texture.Height, 320, 180);
            canvas.Quad(starcrashCheckerTarget.Texture, new(0, 0, 320, 176),
                Color.Hex(0xFF0000).Alpha(session.Timeline.Get("lasthours_checker_alpha", time)));
        }
        canvas.Begin(scene, scene.Texture.Width, scene.Texture.Height, 320, 180);
        int nowTick = Math.Max(0, (int)Math.Floor(time * 60 + 1e-8));
        int firstRect = StarcrashersObjects.FirstBornAtOrAfter(data.CenterRects, nowTick - 60, static r => r.Birth);
        for (int i = firstRect; i < data.CenterRects.Count; i++)
        {
            var rect = data.CenterRects[i];
            if (rect.Birth > nowTick) break;
            double age = time - rect.Birth / 60.0;
            if (age < 0 || age >= 1) continue;
            string name = rect.Red ? "sp_sc_centerrect_red" : "sp_sc_centerrect_black";
            if (current!.Sprites.TryGetValue(name, out var spec))
            {
                double eased = StarcrashersObjects.OutExpo(age);
                double width = 161 * eased, height = 91 * eased;
                // Create_0: line 4 -> 0, image_index round(line+1). Ends by instance_destroy.
                int frame = Math.Clamp((int)Math.Round(1 + 4 * (1 - eased), MidpointRounding.ToEven), 0, spec.Frames.Count - 1);
                if (Image(name, frame) is { } image)
                    canvas.Quad(image, new((float)(160-width), (float)(90-height),
                        (float)(width*2), (float)(height*2)), Color.White);
            }
        }
        canvas.Flush();
    }

    /// <summary>
    /// Called by DrawMask after static white mask sprites, before its gpu bm_dest_color/bm_zero composite.
    /// Note: fireworks and fragments invert the already drawn mask using GameMaker's exact blend factors.
    /// </summary>
    private void DrawStarcrashersMaskObjects(Session session, double time)
    {
        time = Math.Floor(Math.Max(0, time) * 60 + 1e-8) / 60.0;
        var data = Starcrashers(session);
        if (data == null) return;
        int nowTick = Math.Max(0, (int)Math.Floor(time * 60 + 1e-8));
        if (current!.Sprites.TryGetValue("sp_sc_firework_star", out var starSpec))
        {
            int firstStar = StarcrashersObjects.FirstBornAtOrAfter(data.Stars, nowTick - 240, static s => s.Birth);
            for (int i = firstStar; i < data.Stars.Count; i++)
            {
                var star = data.Stars[i];
                if (star.Birth > nowTick) break;
                double age = time - star.Birth / 60.0;
                if (age < 0 || time * 60 >= star.Death || StarcrashersObjects.StarAlpha(star, time) <= 0) continue;
                var texture = Image("sp_sc_firework_star");
                if (texture == null) break;
                double move = star.Intro ? 0 : data.StarDisplacement(star, time);
                double x = star.X, y = star.Y + move;
                double angle = star.InitialAngle + move * 4; // spd * mod_multiplier * timediff * 240
                StarcrashersSprite(session, "sp_sc_firework_star", 0, (float)x, (float)y,
                    StarcrashersObjects.StarAlpha(star, time), rotation: (float)angle);
                if (star.Intro)
                {
                    // o_sc_star_intro.Create_0: overlayfxscale linear 1.5 -> 1 over 500 ms.
                    double scale = 1.5 - .5 * Math.Clamp(age / .5, 0, 1);
                    StarcrashersSprite(session, "sp_sc_firework_star", 0, (float)x, (float)y,
                        StarcrashersObjects.StarAlpha(star, time) * .5, rotation: (float)angle, scaleX: (float)scale, scaleY: (float)scale);
                }
            }
        }
        int firstSlash = StarcrashersObjects.FirstBornAtOrAfter(data.Slashes, nowTick - 60, static s => s.Birth);
        for (int i = firstSlash; i < data.Slashes.Count; i++)
        {
            var slash = data.Slashes[i];
            if (slash.Birth > nowTick) break;
            double age = time - slash.Birth / 60.0;
            if (age < 0 || age >= 1) continue;
            float w = (float)(12 * Math.Pow(1 - age, 2)); // EaseOutQuad: 12 -> 0
            if (w > 0) canvas.Line((float)slash.TopX, -10, (float)slash.BottomX, 190, w, Color.White);
        }
        // obj_starcrashers_mask.Draw_0 uses bm_inv_dest_color / bm_inv_src_color for firework objects.
        canvas.Blend(BlendFactor.InverseDestinationColor, BlendFactor.InverseSourceColor);
        if (current!.Sprites.TryGetValue("sp_sc_firework_star", out var fireworkSpec))
        {
            int firstFirework = StarcrashersObjects.FirstBornAtOrAfter(data.Fireworks, nowTick - 22, static f => f.Birth);
            for (int i = firstFirework; i < data.Fireworks.Count; i++)
            {
                var fw = data.Fireworks[i];
                if (fw.Birth > nowTick) break;
                double age = time - fw.Birth / 60.0;
                if (age < 0 || time * 60 >= fw.Burst) continue;
                double timer = Math.Min(1, age / .34);
                double quad = timer * (2 - timer);
                double y = 190 + (fw.EndpointY - 190) * quad;
                double scale = 2 - quad;
                if (Image("sp_sc_firework_star") is { } image)
                    StarcrashersSprite(session, "sp_sc_firework_star", 0, (float)fw.X, (float)y, 1,
                        rotation: (float)(360 * quad), scaleX: (float)scale, scaleY: (float)scale);
            }
        }
        if (current!.Sprites.TryGetValue("sp_sc_firework_particle", out var fragmentSpec))
        {
            int firstFragment = StarcrashersObjects.FirstBornAtOrAfter(data.Fragments, nowTick - 240, static p => p.Birth);
            for (int i = firstFragment; i < data.Fragments.Count; i++)
            {
                var fragment = data.Fragments[i];
                if (fragment.Birth > nowTick) break;
                double age = time - fragment.Birth / 60.0;
                if (age < 0 || time * 60 >= fragment.Death) continue;
                double t = age; // freshly spawned fragments initially draw at the firework endpoint
                double dist = StarcrashersObjects.FragmentDistance(t, fragment.Speed);
                double x = fragment.X + Math.Cos(fragment.Direction) * dist;
                double y = fragment.Y - Math.Sin(fragment.Direction) * dist + StarcrashersObjects.FragmentGravity(t);
                if (Image("sp_sc_firework_particle", fragment.Frame) is { } image)
                    canvas.Quad(image, new((float)(x-fragmentSpec.OriginX), (float)(y-fragmentSpec.OriginY),
                        fragmentSpec.Width, fragmentSpec.Height), Color.White);
            }
        }
        canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        canvas.Flush();
    }

    private void DisposeStarcrashersObjects()
    {
        starcrashCheckerTarget?.Dispose();
        starcrashCheckerTarget = null;
        starcrashData = null;
        starcrashOwner = null;
    }
}
