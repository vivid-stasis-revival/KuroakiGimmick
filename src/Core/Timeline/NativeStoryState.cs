// 按绝对时间重放剧情文本框、逐字显示与角色特效。

namespace KuroakiGimmick.Core;

/// <summary>可任意 seek 的 o_textbox 与 TextDrawer 重放；不接受游戏输入，也不驱动歌曲跳转。</summary>
/// <param name="Speaker">显示的说话人。</param>
/// <param name="Lines">排版后的文本行。</param>
/// <param name="Y">文本框逻辑纵坐标。</param>
/// <param name="VisibleCharacters">已显示字符数。</param>
/// <param name="Thinking">是否采用思考样式。</param>
/// <param name="Cleared">是否处于清屏状态。</param>
/// <param name="JitterCharacters">带抖动效果的字符位置。</param>
/// <param name="EffectTime">字符效果使用的时刻。</param>
/// <param name="SpeakerLeftAligned">说话人是否左对齐。</param>
/// <param name="RColorCharacters">使用循环颜色的字符位置。</param>
public sealed record NativeStoryState(string Speaker, IReadOnlyList<string> Lines, double Y, int VisibleCharacters, bool Thinking, bool Cleared,
    IReadOnlySet<int>? JitterCharacters = null, double EffectTime = 0, bool SpeakerLeftAligned = false,
    IReadOnlySet<int>? RColorCharacters = null)
{
    /// <summary>使用公共求值器重放时间点，通过同一行列表向旧的 KG API 暴露结果。</summary>
    public static NativeStoryState? Sample(NativeSequenceDefinition sequence, double time, Func<string, bool> triggered,
        Func<string, float> width, NativeStoryContext? context = null)
    {
        var s = KenomaGMK.Runtime.Sequence.NativeStoryState.Sample(sequence, time, triggered, width, context);
        return s == null ? null : new(s.Speaker, s.Lines, s.Y, s.VisibleCharacters, s.Thinking, s.Cleared,
            s.JitterCharacters, s.EffectTime, s.SpeakerLeftAligned, s.RColorCharacters);
    }
    public static string ResolveSpeaker(string name, NativeStoryContext? context, double time) =>
        KenomaGMK.Runtime.Sequence.NativeStoryState.ResolveSpeaker(name, context, time);
    public static uint RColorAt(double time, int character) =>
        KenomaGMK.Runtime.Sequence.NativeStoryState.RColorAt(time, character);
    public static IReadOnlyList<string> Wrap(string text, Func<string, float> width, double limit = 308) =>
        KenomaGMK.Runtime.Sequence.NativeStoryState.Wrap(text, width, limit);
}
