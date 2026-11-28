// 为 Starcrashers 音符和长条混合不同皮肤。

using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Shader starcrashNoteMix;
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

    private void StarcrashersMixedSprite(Session session, PartialSkinBlendState.State state, string suffix,
        float x, float y, double alpha, float rotation = 0, float scaleY = 1)
    {
        string allison = "sp_note_" + suffix + "_allison", normal = "sp_note_" + suffix + "_normal";
        if (current == null || !current.Sprites.TryGetValue(allison, out var target) || Image(allison) is not { } image) return;
        Shader? shader = null;
        if (!state.Direct)
        {
            if (!current.Sprites.TryGetValue(normal, out var origin) || Image(normal) is not { } source) return;
            canvas.Flush();
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

    /// <summary>在星场皮肤激活时接管指定轨道的音符，空帧同样视为已处理。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="type">来源音符类型编号。</param>
    /// <param name="lane">音符的轨道编号。</param>
    /// <param name="x">逻辑横坐标。</param>
    /// <param name="y">逻辑纵坐标。</param>
    /// <param name="alpha">绘制不透明度。</param>
    /// <param name="rotation">旋转角度。</param>
    /// <returns>该音符由此皮肤处理时为 true，包括故意留空的帧。</returns>
    public bool DrawMaskedStarFieldNativeNote(Session session, double time, int type, int lane, float x, float y, double alpha, float rotation)
    {
        if (!session.NativeGimmick.HasBehavior("masked-starfield")) return false;
        var state = PartialSkinBlendState.At(session.Timeline, time);
        if (!state.Active || !(type is 0 or 6 && lane is 0 or 1 || type is 1 or 7 or 8 && lane == 0)) return false;
        if (type is 6 or 7) return true; // 来源 lane_sprites 在这些地雷槽使用 sp_empty。
        Prepare(session);
        var anchor = KenomaGMK.Graphics.Notes.NoteGeometry.SpriteAnchor(x, y, rotation, type is 1 or 8);
        StarcrashersMixedSprite(session, state, type == 0 ? "chip" : type == 1 ? "bumper" : "bumper_timing", anchor.X, anchor.Y, alpha, rotation);
        return true;
    }

    /// <summary>在星场皮肤激活时接管左侧两轨的长条绘制。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="lane">音符的轨道编号。</param>
    /// <param name="x">逻辑横坐标。</param>
    /// <param name="startY">长条起点的逻辑纵坐标。</param>
    /// <param name="endY">长条终点的逻辑纵坐标。</param>
    /// <param name="alpha">绘制不透明度。</param>
    /// <param name="pressed">是否绘制按下状态。</param>
    /// <returns>已接管该轨长条时为 true。</returns>
    public bool DrawMaskedStarFieldNativeHold(Session session, double time, int lane, float x, float startY, float endY, double alpha, bool pressed)
    {
        if (!session.NativeGimmick.HasBehavior("masked-starfield") || lane is not (0 or 1)) return false;
        var state = PartialSkinBlendState.At(session.Timeline, time);
        if (!state.Active) return false;
        Prepare(session);
        var hold = new KenomaGMK.Graphics.Notes.HoldGeometry(startY, endY, pressed);
        if (hold.ClipBody(-8, 188, out float visibleTop, out float height, out _, out _))
            StarcrashersMixedSprite(session, state, "hold", x, visibleTop, alpha, scaleY: height);
        if (!pressed) StarcrashersMixedSprite(session, state, "chip", x, hold.Lower, alpha);
        StarcrashersMixedSprite(session, state, "chip", x, hold.Upper, alpha);
        return true;
    }
}
