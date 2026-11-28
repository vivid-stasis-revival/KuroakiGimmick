namespace KuroakiGimmick.Core;

/// <summary>通过指定谱面时间轴读取轨道与资源包参数。</summary>
public sealed class GimmickScalar : KenomaGMK.Runtime.Definitions.ScalarExpression
{
    readonly record struct Inputs(Timeline Timeline, double Seconds) : IInputs
    {
        public double Mod(string name) => Timeline.Get(name, Seconds);
        public double Resource(string name, int component) => Timeline.Native?.ParameterNumber(name, component)
            ?? throw new InvalidDataException("No resource parameters are loaded.");
    }
    public double Value(Timeline timeline, double time)
    {
        double seconds = time + TimeOffset;
        return Sample(seconds, timeline.Bpm.Beat(seconds), new Inputs(timeline, seconds), Scale,
            DifficultyScale?.GetValueOrDefault(timeline.Difficulty, 1) ?? 1);
    }
}
