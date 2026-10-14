namespace KuroakiGimmick.Core;

/// <summary>Source callback ABI shared by Distorted Fate and First Breath.</summary>
public static class NativeLorelei
{
    public static readonly uint[] Palette = [14418175, 255, 65462, 16776960, 27391, 55551, 16749568];

    // GameMaker repeat uses a rounded expression, including ties to even.
    // https://manual.gamemaker.io/monthly/en/GameMaker_Language/GML_Overview/Language_Features/repeat.htm
    public static int FirstBreathCount(double durationSeconds) => (int)Math.Clamp(
        Math.Round(1 + durationSeconds * 100, MidpointRounding.ToEven), 0, int.MaxValue);

    public static uint Recolor(int eventOrder) => Palette[Math.Min(Palette.Length - 1,
        (int)(Timeline.Hash(unchecked((uint)eventOrder * 65537 + 8171)) * Palette.Length))];
}

public sealed partial class Timeline
{
    private bool AddNativeLorelei(ModEvent e, double start, double duration, ref double color)
    {
        if (chart.ObjectName == "obj_distortedfate_gimmick")
        {
            if (e.Name == "lr_slash_color") color = NativeLorelei.Recolor(e.Order);
            else LoreleiSlashes.Add(new(start, 1, color, e.Order));
        }
        else if (chart.ObjectName == "obj_firstbreath_gimmick")
        {
            // Native callback arguments are startMs, durationMs, v1, v2, ease.
            // Both names spawn red; lr_slash_color additionally spawns black.
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
