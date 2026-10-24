namespace KuroakiGimmick.Core;

/// <summary>Shared script-owned offline state. No keyboard input or autoplay hits are injected.</summary>
internal sealed class MpfPreviewGameState(double noteValue)
{
    readonly double[] early = new double[5], late = new double[5];
    public double Combo { get; private set; }
    public double MaxCombo { get; private set; }
    public double AccuracySum { get; private set; }
    public double XAccuracySum { get; private set; }
    public double Count { get; private set; }
    public double[] Timings { get; private set; } = [35,70,110,150,200];
    public double Judge(int kind) => early[kind] + late[kind];
    public double Score => (Judge(0)*1.01 + Judge(1) + Judge(2)*.75 + Judge(3)*.5)*noteValue;
    public double MinusScore => 1010000 - (Judge(1)*.01 + Judge(2)*.26 + Judge(3)*.51 + Judge(4)*1.01)*noteValue;
    public double ExScore => Judge(0)*3 + Judge(1)*2 + Judge(2);
    public void Add(int kind, double count, bool isEarly)
    {
        (isEarly && kind != 0 ? early : late)[kind] += count;
        if (kind == 4) Combo = count > 0 ? 0 : Math.Max(0, Combo-count);
        else if (kind != 3) Combo = Math.Max(0, Combo+count);
        MaxCombo = Math.Max(MaxCombo, Combo);
        double accuracy = kind switch { 0 or 1 => 100, 2 => 75, 3 => 50, _ => 0 };
        AccuracySum += accuracy*count;
        XAccuracySum += (kind == 0 ? 100 : accuracy/1.01)*count;
        Count += count;
    }
    public void ResetTimings() => Timings = [35,70,110,150,200];
    public object Global(string name) => name switch
    {
        "count_acrit" => Judge(0), "count_crit" => Judge(1), "count_great" => Judge(2),
        "count_good" => Judge(3), "count_miss" => Judge(4),
        "count_early_crit" => early[1], "count_late_crit" => late[1],
        "count_early_great" => early[2], "count_late_great" => late[2],
        "count_early_good" => early[3], "count_late_good" => late[3],
        "count_early_miss" => early[4], "count_late_miss" => late[4],
        "earlynotes" => early[1]+early[2]+early[3], "latenotes" => late[1]+late[2]+late[3],
        "currentcombo" => Combo, "maxcombo" => MaxCombo,
        "currentscore" => Score, "minusscore" => MinusScore, "gamescore" => ExScore,
        "notevalue" => noteValue,
        _ => throw new NotSupportedException("MPF offline preview has no global game variable '"+name+"'.")
    };
}
