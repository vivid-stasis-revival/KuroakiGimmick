// 将 Lorelei 相关回调映射到侧边动画。

namespace KuroakiGimmick.Core;

/// <summary>Distorted Fate 与 First Breath 共用的原版回调约定。</summary>
public static class NativeLorelei
{
    /// <summary>Lorelei 重着色使用的原版颜色表。</summary>
    public static readonly uint[] Palette = [14418175, 255, 65462, 16776960, 27391, 55551, 16749568];

    // GameMaker 的 repeat 对表达式就近舍入，半数取偶。
    // https://manual.gamemaker.io/monthly/en/GameMaker_Language/GML_Overview/Language_Features/repeat.htm
    /// <summary>按持续秒数计算 First Breath 的 repeat 次数，取最近偶数舍入。</summary>
    /// <param name="durationSeconds">斩击持续秒数。</param>
    /// <returns>按源 repeat 规则计算的重复次数。</returns>
    public static int FirstBreathCount(double durationSeconds) => (int)Math.Clamp(
        Math.Round(1 + durationSeconds * 100, MidpointRounding.ToEven), 0, int.MaxValue);

    /// <summary>将事件来源顺序确定性地映射到原版调色板。</summary>
    /// <param name="eventOrder">源事件顺序编号。</param>
    /// <returns>稳定选中的调色板颜色。</returns>
    public static uint Recolor(int eventOrder) => Palette[Math.Min(Palette.Length - 1,
        (int)(Timeline.Hash(unchecked((uint)eventOrder * 65537 + 8171)) * Palette.Length))];
}

public sealed partial class Timeline
{
    private bool AddNativeLorelei(ModEvent e, double start, double duration, ref double color)
    {
        if (Native?.Data?.HasBehavior("palette-slash") == true)
        {
            if (e.Name == "lr_slash_color") color = NativeLorelei.Recolor(e.Order);
            else LoreleiSlashes.Add(new(start, 1, color, e.Order));
        }
        else if (Native?.Data?.HasBehavior("duration-slash") == true)
        {
            // 原生回调依次接收 startMs、durationMs、v1、v2 和 ease。两个回调都生成红色斩击，lr_slash_color 还生成黑色斩击。
            int count = NativeLorelei.FirstBreathCount(duration);
            if (count > 0)
            {
                LoreleiSlashes.Add(new(start, count, 255, unchecked(e.Order * 2)));
                if (e.Name == "lr_slash_color")
                    LoreleiSlashes.Add(new(start, count, 0, unchecked(e.Order * 2 + 1)));
            }
        }
        else return false;
        End = Math.Max(End, start + 1);
        return true;
    }
}
