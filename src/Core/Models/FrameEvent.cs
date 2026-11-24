using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 逐帧函数的拍区间。执行时使用开区间边界，以保持源引擎的触发语义。
/// </summary>
/// <param name="StartBeat">区间起始拍，不含此边界。</param>
/// <param name="EndBeat">区间结束拍，不含此边界。</param>
/// <param name="Function">区间内调用的函数名。</param>
public record FrameEvent(double StartBeat, double EndBeat, string Function);

