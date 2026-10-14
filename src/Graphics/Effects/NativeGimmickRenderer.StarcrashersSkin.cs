using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Shader? starcrashNoteMix;
    const string StarcrashMixFragment = """
        #version 330 core
        in vec2 v_vTexcoord;in vec4 v_vColour;
        uniform sampler2D gm_BaseTexture,normalTexture;
        uniform vec4 normalUv;uniform float blendAmount;
        out vec4 fragColor;
        void main(){
            vec4 n=texture(normalTexture,normalUv.xy+v_vTexcoord*normalUv.zw);
            vec4 a=texture(gm_BaseTexture,v_vTexcoord);
            float amount=a.a*blendAmount;
            float alpha=amount+n.a*(1.0-amount);
            vec3 rgb=alpha>0.0?(a.rgb*amount+n.rgb*n.a*(1.0-amount))/alpha:vec3(0.0);
            rgb=floor(rgb*255.0+0.5)/255.0;
            fragColor=vec4(rgb,alpha)*v_vColour;
        }
        """;

    private void StarcrashersMixedSprite(Session session, StarcrashersNoteState.State state, string suffix,
        float x, float y, double alpha, float rotation = 0, float scaleY = 1)
    {
        string allison = "sp_note_" + suffix + "_allison", normal = "sp_note_" + suffix + "_normal";
        if (current?.Sprites.TryGetValue(allison, out var target) != true || Image(allison) is not { } image) return;
        Shader? shader = null;
        if (!state.Direct)
        {
            if (!current.Sprites.TryGetValue(normal, out var origin) || Image(normal) is not { } source) return;
            starcrashNoteMix ??= new(canvas.Gpu, Canvas.VertexSource, StarcrashMixFragment);
            canvas.Flush();
            canvas.Gpu.BindTexture(1, source);
            starcrashNoteMix.Int("normalTexture", 1);
            starcrashNoteMix.Float("blendAmount", state.Alpha);
            starcrashNoteMix.Vec4("normalUv", new((origin.OriginX - target.OriginX) / (float)origin.Width,
                (origin.OriginY - target.OriginY) / (float)origin.Height,
                target.Width / (float)origin.Width, target.Height / (float)origin.Height));
            shader = starcrashNoteMix;
        }
        var transform = Matrix3x2.CreateTranslation(-target.OriginX, -target.OriginY)
            * Matrix3x2.CreateScale(1, scaleY) * Matrix3x2.CreateRotation(-rotation * MathF.PI / 180)
            * Matrix3x2.CreateTranslation(x, y);
        canvas.Polygon(image, Vector2.Transform(Vector2.Zero, transform), Vector2.Transform(new(target.Width, 0), transform),
            Vector2.Transform(new(0, target.Height), transform), Vector2.Transform(new(target.Width, target.Height), transform),
            Color.White.Alpha(alpha), shader: shader);
        if (shader != null) canvas.Flush();
    }

    public bool DrawStarcrashersNativeNote(Session session, double time, int type, int lane, float x, float y, double alpha, float rotation)
    {
        if (session.Chart.ObjectName != "obj_starcrashers_gimmick") return false;
        var state = StarcrashersNoteState.At(session.Timeline, time);
        if (!state.Active || !(type is 0 or 6 && lane is 0 or 1 || type is 1 or 7 or 8 && lane == 0)) return false;
        if (type is 6 or 7) return true; // source lane_sprites uses sp_empty for these mine slots
        Prepare(session);
        float angle = ((rotation % 360) + 360) % 360;
        if (type is 1 or 8 && (angle + 90) % 360 >= 180) x++;
        if (angle >= 180) y++;
        StarcrashersMixedSprite(session, state, type == 0 ? "chip" : type == 1 ? "bumper" : "bumper_timing", x, y, alpha, rotation);
        return true;
    }

    public bool DrawStarcrashersNativeHold(Session session, double time, int lane, float x, float startY, float endY, double alpha, bool pressed)
    {
        if (session.Chart.ObjectName != "obj_starcrashers_gimmick" || lane is not (0 or 1)) return false;
        var state = StarcrashersNoteState.At(session.Timeline, time);
        if (!state.Active) return false;
        Prepare(session);
        endY += 6;
        float top = Math.Min(startY, endY), bottom = Math.Max(startY, endY);
        if (pressed) { top = Math.Min(top, 140); bottom = Math.Min(bottom, 140); }
        float visibleTop = Math.Max(-8, top + 4), visibleBottom = Math.Min(188, pressed ? bottom + 4 : bottom - 3);
        if (visibleBottom > visibleTop) StarcrashersMixedSprite(session, state, "hold", x, visibleTop, alpha, scaleY: visibleBottom - visibleTop);
        StarcrashersMixedSprite(session, state, "chip", x, top, alpha);
        if (!pressed) StarcrashersMixedSprite(session, state, "chip", x, bottom, alpha);
        return true;
    }
}
