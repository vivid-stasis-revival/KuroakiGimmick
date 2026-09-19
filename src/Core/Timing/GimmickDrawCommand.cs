namespace KuroakiGimmick.Core;

/// <summary>纯 CPU 的绘制调度结果，可在无 SDL/GPU 的测试中检查生命周期和图层顺序。</summary>
/// <param name="Age">距回调触发的秒数；常驻覆盖层直接传入当前时间。</param>
/// <param name="EventIndex">来源回调的触发序号，-1 表示常驻覆盖层，不属于任何回调，也不参与 LatestOnly 替换。</param>
public sealed record GimmickDrawCommand(GimmickDrawing Drawing, double Age, int EventIndex);

