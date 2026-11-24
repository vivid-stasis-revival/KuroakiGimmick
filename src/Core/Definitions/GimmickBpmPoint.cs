namespace KuroakiGimmick.Core;

/// <summary>原版对象 NewBPMListFromOld 中的变速点，位置以拍计。</summary>
/// <param name="Beat">新速度生效的拍数。</param>
/// <param name="Bpm">该点之后的每分钟拍数。</param>
public sealed record GimmickBpmPoint(double Beat, double Bpm);
