// 按对象配置绘制曲绘图层。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Timeline? selectionOwner;
    SpriteSelectionState? selectionState;
    /// <summary>按对象的曲绘组件配置绘制并报告是否接管该项。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>已由曲绘组件处理时为 true。</returns>
    public bool DrawConfiguredJacket(Session session, double time)
    {
        if (session.NativeGimmick.Behavior("multiply-jacket") is not { } component) return false;
        Prepare(session);
        string mod = component.Mod("frame");
        string sprite;
        int frame = 0, index = 0;
        if (component.SpriteLists.TryGetValue("variants", out var variants))
        {
            if (!ReferenceEquals(selectionOwner, session.Timeline))
            {
                selectionOwner = session.Timeline;
                selectionState = new(session.Timeline, mod, variants.Length);
            }
            index = selectionState!.At(time);
            if (index == (int)component.Number("skipIndex", -1)) return true;
            sprite = variants[index];
        }
        else
        {
            sprite = component.Sprite("atlas");
            if (current == null || !current.Sprites.TryGetValue(sprite, out var info)) return false;
            double value = session.Timeline.Get(mod, time);
            if (!double.IsFinite(value)) return false;
            frame = (int)((Math.Floor(value) % info.Frames.Count + info.Frames.Count) % info.Frames.Count);
        }
        if (Image(sprite, frame, linear: component.Flag("linear", true)) is { } texture)
        {
            canvas.Blend(BlendFactor.DestinationColor, BlendFactor.Zero);
            canvas.Quad(texture, new((float)component.Number("x", 0), (float)component.Number("y", 0),
                (float)component.Number("width", 320), (float)component.Number("height", 180)), Color.White);
            canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
            canvas.Flush();
        }
        return true;
    }
}
