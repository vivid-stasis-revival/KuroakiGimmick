namespace KuroakiGimmick.Core;

/// <summary>在指定歌曲时刻求取模拟桌面的窗口排列与变换。</summary>
public static class NativeDesktopState
{
    /// <summary>虚拟桌面中一个窗口的当前变换。</summary>
    /// <param name="Index">窗口序号。</param>
    /// <param name="X">横坐标。</param>
    /// <param name="Y">纵坐标。</param>
    /// <param name="ScaleX">水平缩放倍率。</param>
    /// <param name="ScaleY">垂直缩放倍率。</param>
    /// <param name="Angle">旋转角。</param>
    public sealed record Window(int Index, double X, double Y, double ScaleX, double ScaleY, double Angle);
    /// <summary>按给定时间取得虚拟桌面中的窗口变换。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>该时刻各虚拟窗口的变换。</returns>
    public static IReadOnlyList<Window> Windows(Timeline timeline, double time)
    {
        double M(string n) => timeline.Get(n, time);
        double beat = timeline.Bpm.Beat(time), sx = M("tz") * M("tzx"), sy = M("tz") * M("tzy");
        if (sy > 0)
        {
            double factor = timeline.Difficulty is "OPENING" or "MIDDLE" ? .625 : 1.25;
            sx = 1 - (1 - sx) * factor;
            sy = 1 - (1 - sy) * factor;
        }
        double x = 160 + (M("tx") + M("tx2") + M("tx3") + M("txj")) * .375;
        double y = 90 + (M("ty") + M("ty2") + M("ty3") + M("tyj")) * .375;
        double angle = M("trz") + M("trz2");
        var result = new List<Window>();
        foreach (int index in new[] { 2, 1, 3, 4, 5 })
        {
            if (M("hid" + index) != 0 || index != 1 && beat <= 10) continue;
            double dx = index == 2 ? -M("tspl") : index == 3 ? M("tspl") : index == 4 ? -M("sepx") : index == 5 ? M("sepx") : 0;
            double dy = index == 2 ? -M("sepy") : index == 3 ? M("sepy") : 0;
            result.Add(new(index, x + dx * .375, y + dy * .375, sx, sy, angle));
        }
        return result;
    }
}
