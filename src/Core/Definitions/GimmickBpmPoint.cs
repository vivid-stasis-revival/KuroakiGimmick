namespace KuroakiGimmick.Core;

/// <summary>A source object's NewBPMListFromOld point. Beat is not a timestamp.</summary>
public sealed record GimmickBpmPoint(double Beat, double Bpm);
