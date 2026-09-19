using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 逐帧函数的拍区间。执行时使用开区间边界，以保持源引擎的触发语义。
/// </summary>
public record FrameEvent(double StartBeat, double EndBeat, string Function);

