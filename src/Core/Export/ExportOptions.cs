using System.Diagnostics;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>
/// 导出区间、帧率与画面宽度。Start/End 单位是秒。帧数在区间边界处理浮点误差，避免多导出一帧。
/// </summary>
public sealed record ExportOptions(string Path, double Start, double End, int Fps = 60, int Width = 1920, int Height = 1080, bool Notes = true,
    bool Effects = true, bool Overwrite = false)
{
    /// <summary>要编码的总帧数，至少 1 帧。减 1e-9 是为了让刚好落在帧边界的区间不多出一帧。</summary>
    public int FrameCount => Math.Max(1, (int) Math.Ceiling((End - Start) * Fps - 1e-9));
    /// <summary>区间不超过 1 小时、帧率 1..240、分辨率必须是 320×180 到 7680×4320 之间的偶数 16:9。</summary>
    public void Validate()
    {
        if (!double.IsFinite(Start) || !double.IsFinite(End) || Start < 0 || End <= Start || End - Start > 3600)
        {
            throw new ArgumentException("Export range must be positive and at most one hour.");
        }
        if (Fps is < 1 or > 240)
        {
            throw new ArgumentException("Frame rate must be 1..240.");
        }
        if (Width < 320 || Width > 7680 || Height < 180 || Height > 4320 || Width % 2 != 0 || Height % 2 != 0 || Width * 9 != Height * 16)
        {
            throw new ArgumentException("Export size must be an even 16:9 resolution, from 320x180 to 7680x4320.");
        }
        if (!System.IO.Path.GetExtension(Path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Export filename must end in .mp4.");
        }
        ExportFiles.Validate(Path, Overwrite);
    }
}
