// 在歌曲时钟与含剧情插段的呈现时钟之间转换，并按时刻采样插段。

namespace KuroakiGimmick.Core;

/// <summary>将可自动阅读的剧情停顿插入呈现时间，歌曲和谱面的秒坐标保持独立。</summary>
public sealed class NativeInterludeRoute
{
    /// <summary>歌曲时间与插段的对应关系。</summary>
    /// <param name="SongTime">插段触发处的歌曲秒数。</param>
    /// <param name="PresentationStart">插段开始的呈现秒数。</param>
    /// <param name="Schedule">插段动作时间表。</param>
    public sealed record Entry(double SongTime, double PresentationStart, NativeInterludeSchedule Schedule)
    {
        /// <summary>本插段冻结歌曲时钟的秒数，取时间表的恢复位置。</summary>
        public double Duration => Schedule.ResumeTime;
        /// <summary>恢复歌曲时钟的呈现秒数。</summary>
        public double End => PresentationStart + Duration;
    }
    /// <summary>在歌曲时刻插入的剧情及其呈现起点。</summary>
    public IReadOnlyList<Entry> Entries { get; }
    /// <summary>歌曲播放或暂停的一段呈现区间。</summary>
    /// <param name="SongStart">歌曲起始秒数。</param>
    /// <param name="SongEnd">歌曲结束秒数。</param>
    /// <param name="PresentationStart">区间起始呈现秒数。</param>
    /// <param name="Duration">呈现持续秒数。</param>
    /// <param name="Hold">是否冻结歌曲时钟。</param>
    public sealed record Leg(double SongStart, double SongEnd, double PresentationStart, double Duration, bool Hold = false);
    /// <summary>插段路线中的歌曲跳转。</summary>
    /// <param name="From">跳转前的歌曲秒数。</param>
    /// <param name="To">目标歌曲秒数。</param>
    /// <param name="PresentationTime">发生跳转的呈现秒数。</param>
    /// <param name="FadeInSeconds">跳转后的淡入时长，单位为秒。</param>
    public sealed record Seek(double From, double To, double PresentationTime, double FadeInSeconds);
    /// <summary>将呈现时间映射到播放或冻结段的路线。</summary>
    public IReadOnlyList<Leg> Legs { get; }
    /// <summary>记录跳转前后歌曲位置及呈现时刻的列表。</summary>
    public IReadOnlyList<Seek> Seeks { get; }
    /// <summary>是否存在插段或歌曲跳转。</summary>
    public bool HasChanges => Entries.Count > 0 || Seeks.Count > 0;

    /// <summary>将已触发的剧情插段和歌曲跳转展开为呈现路线。</summary>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="bpm">在歌曲秒数与拍数之间转换的 BPM 映射。</param>
    /// <param name="definitions">剧情插段定义列表。</param>
    /// <param name="songSeeks">可选的歌曲跳转控制。</param>
    /// <exception cref="InvalidDataException">跳转、插段数量或累计呈现时长超过路线预算。</exception>
    public NativeInterludeRoute(Chart chart, BpmMap bpm, IReadOnlyList<NativeInterludeDefinition> definitions,
        IReadOnlyList<NativeSequenceDefinition.SongSeek>? songSeeks = null)
    {
        var entries = new List<Entry>();
        var legs = new List<Leg>();
        var seeks = new List<Seek>();
        var scheduled = new List<(double Time, int Order, NativeInterludeDefinition? Definition, NativeSequenceDefinition.SongSeek? Seek)>();
        foreach (var e in chart.Mods.OrderBy(e => e.Beat).ThenBy(e => e.Order))
        {
            foreach (var definition in definitions.Where(d => d.Trigger == e.Name))
                scheduled.Add((Math.Max(definition.StartSongTime ?? 0, bpm.Time(e.Beat)), e.Order, definition, null));
            foreach (var seek in (songSeeks ?? []).Where(seek => seek.Trigger == e.Name))
                scheduled.Add((Math.Max(0, bpm.Time(e.Beat)), e.Order, null, seek));
        }
        double cursor = 0, presentation = 0, inserted = 0;
        foreach (var item in scheduled.OrderBy(item => item.Time).ThenBy(item => item.Order))
        {
            if (item.Time > cursor)
            {
                legs.Add(new(cursor, item.Time, presentation, item.Time - cursor));
                presentation += item.Time - cursor;
                cursor = item.Time;
            }
            if (item.Seek is { } seek)
            {
                seeks.Add(new(cursor, seek.Time, presentation, seek.FadeInSeconds));
                cursor = seek.Time;
            }
            else if (item.Definition is { } definition)
            {
                var schedule = new NativeInterludeSchedule(definition);
                if (schedule.ResumeTime <= 0) continue;
                entries.Add(new(cursor, presentation, schedule));
                legs.Add(new(cursor, cursor, presentation, schedule.ResumeTime, true));
                presentation += schedule.ResumeTime;
                inserted += schedule.ResumeTime;
            }
            if (entries.Count > 64 || seeks.Count > 64 || inserted > 3600 || presentation > 86400)
                throw new InvalidDataException("Native story route exceeds 64 controls, one hour of inserted story or 24 hours of playback.");
        }
        legs.Add(new(cursor, double.PositiveInfinity, presentation, double.PositiveInfinity));
        Entries = entries;
        Legs = legs;
        Seeks = seeks;
    }

    /// <summary>将给定呈现秒数映射回歌曲秒数。</summary>
    /// <param name="presentation">呈现时刻，单位为秒。</param>
    /// <returns>对应的歌曲秒数。</returns>
    public double SongAt(double presentation)
    {
        if (presentation < 0) return presentation;
        var leg = Legs.LastOrDefault(leg => leg.PresentationStart <= presentation) ?? Legs[0];
        return leg.Hold ? leg.SongStart : leg.SongStart + presentation - leg.PresentationStart;
    }

    /// <summary>将歌曲秒数映射到呈现时刻，重叠候选默认取最早值，afterHolds 时取最晚值。</summary>
    /// <param name="song">歌曲时刻，单位为秒。</param>
    /// <param name="afterHolds">重叠候选中是否选取最晚时刻。</param>
    /// <returns>对应的呈现秒数。</returns>
    public double PresentationAt(double song, bool afterHolds = false)
    {
        var candidates = new List<double>();
        foreach (var leg in Legs)
            if (leg.Hold)
            {
                if (leg.SongStart == song) candidates.Add(leg.PresentationStart + (afterHolds ? leg.Duration : 0));
            }
            else if (song >= leg.SongStart && song <= leg.SongEnd) candidates.Add(leg.PresentationStart + song - leg.SongStart);
        foreach (var seek in Seeks)
            if (seek.To > seek.From && song >= seek.From && song < seek.To) candidates.Add(seek.PresentationTime);
        return candidates.Count == 0 ? song : afterHolds ? candidates.Max() : candidates.Min();
    }

    /// <summary>采样当前呈现时刻覆盖的最后一个插段，无匹配时返回 null。</summary>
    /// <param name="presentation">呈现时刻，单位为秒。</param>
    /// <returns>当前插段状态，未处于插段时为 null。</returns>
    public NativeInterludeState? At(double presentation)
    {
        var entry = Entries.LastOrDefault(e => e.PresentationStart <= presentation && presentation < e.PresentationStart + e.Schedule.TotalDuration);
        return entry == null ? null : entry.Schedule.Sample(presentation - entry.PresentationStart, entry.SongTime);
    }
}

/// <summary>根据步骤的显式时长或阅读速度展开的插段时间表。</summary>
public sealed class NativeInterludeSchedule
{
    /// <summary>插段时间表中的一个动作。</summary>
    /// <param name="Time">动作距插段开始的秒数。</param>
    /// <param name="Action">要执行的动作定义。</param>
    public sealed record TimedAction(double Time, NativeInterludeAction Action);
    /// <summary>用于展开本时间表的原始插段。</summary>
    public NativeInterludeDefinition Definition { get; }
    /// <summary>从步骤台词与清除动作生成的剧情序列。</summary>
    public NativeSequenceDefinition Sequence { get; } = new();
    /// <summary>按相对时间排列的动作。</summary>
    public IReadOnlyList<TimedAction> Actions { get; }
    /// <summary>首个 resume 动作的秒数，未声明时取全部步骤结束时刻。</summary>
    public double ResumeTime { get; }
    /// <summary>覆盖所有步骤与恢复时刻的呈现秒数。</summary>
    public double TotalDuration { get; }

    /// <summary>依次展开步骤中的动作与文本，并确定恢复歌曲的位置。</summary>
    /// <param name="definition">要展开的插段定义。</param>
    public NativeInterludeSchedule(NativeInterludeDefinition definition)
    {
        Definition = definition;
        double time = 0;
        double? resume = null;
        var actions = new List<TimedAction>();
        foreach (var step in definition.Steps)
        {
            foreach (var action in step.Actions)
            {
                actions.Add(new(time, action));
                if (action.Kind == "resume") resume ??= time;
                if (action.Kind == "clear-text") Sequence.Story.Add(new(time, "interlude", step.Speaker, "", Clear: true));
            }
            if (step.Text != null) Sequence.Story.Add(new(time, "interlude", step.Speaker, step.Text));
            int length = System.Text.RegularExpressions.Regex.Replace(step.Text ?? "", "`[a-z]+\\{[^}]*\\}", "").Length;
            double duration = step.Duration ?? (step.Text == null ? step.Actions.Select(a => a.Duration).DefaultIfEmpty(0).Max()
                : Math.Max(definition.MinimumCueSeconds, length / definition.CharactersPerSecond + definition.PaddingSeconds));
            time += duration;
        }
        ResumeTime = resume ?? time;
        TotalDuration = Math.Max(time, ResumeTime);
        Sequence.StoryWindows.Add(new("interlude", 0, ResumeTime, ResumeTime, 122, 1));
        Actions = actions;
    }

    /// <summary>顺序应用截至指定年龄的动作，计算背景、立绘及着色状态。</summary>
    /// <param name="age">插段开始后经过的秒数。</param>
    /// <param name="songTime">关联的歌曲秒数。</param>
    /// <param name="keepVisible">是否要求继续显示立绘。</param>
    /// <returns>当前背景、色调与立绘的采样结果。</returns>
    public NativeInterludeState Sample(double age, double songTime, bool keepVisible = false)
    {
        var portraits = new Dictionary<string, PortraitState>(StringComparer.Ordinal);
        string? background = Definition.BackgroundSprite;
        uint tintColor = 0xffffff;
        var tint = new Tween(0, 0, 0, 0, "linear");
        foreach (var item in Actions)
        {
            if (item.Time > age) break;
            var action = item.Action;
            double at = item.Time;
            portraits.TryGetValue(action.Id ?? "", out var portrait);
            switch (action.Kind)
            {
                case "background":
                    background = action.Sprite;
                    break;
                case "portrait-create":
                    portraits[action.Id!] = new(action.Sprite!, action.X ?? 0, action.Y ?? 0, action.Scale, action.Mirror) { Frame = action.Frame };
                    break;
                case "portrait-frame" when portrait != null:
                    portrait.Frame = action.Frame;
                    break;
                case "portrait-flip" when portrait != null:
                    portrait.Mirror = !portrait.Mirror;
                    if (action.Bounce)
                    {
                        portrait.X = new(portrait.X.At(at), portrait.DestinationX, at, 2.0 / 3, "outExpo");
                        portrait.Y = new(portrait.Y.At(at) + 10, portrait.DestinationY, at, 2.0 / 3, "outExpo");
                    }
                    break;
                case "portrait-change" when portrait != null:
                    portrait.Sprite = action.Sprite!;
                    if (action.Bounce)
                    {
                        portrait.X = new(portrait.X.At(at), portrait.DestinationX, at, 2.0 / 3, "outExpo");
                        portrait.Y = new(portrait.Y.At(at) + 10, portrait.DestinationY, at, 2.0 / 3, "outExpo");
                    }
                    break;
                case "portrait-move" when portrait != null:
                    double x = action.X ?? portrait.X.At(at), y = action.Y is null or 0 ? portrait.Y.At(at) : action.Y.Value;
                    portrait.X = new(portrait.X.At(at), x, at, action.Duration, "outQuint");
                    portrait.Y = new(portrait.Y.At(at), y, at, action.Duration, "outQuint");
                    portrait.DestinationX = x;
                    portrait.DestinationY = y;
                    if (action.Mirror)
                    {
                        portrait.Mirror = !portrait.Mirror;
                        portrait.X = new(portrait.X.At(at), portrait.DestinationX, at, 2.0 / 3, "outExpo");
                        portrait.Y = new(portrait.Y.At(at) + 10, portrait.DestinationY, at, 2.0 / 3, "outExpo");
                    }
                    break;
                case "portrait-alpha" when portrait != null:
                    portrait.Alpha = new(action.From ?? portrait.Alpha.At(at), action.To, at, action.Duration, "linear");
                    break;
                case "portrait-destroy":
                    if (action.Id == null) portraits.Clear();
                    else portraits.Remove(action.Id);
                    break;
                case "tint-color":
                    tintColor = action.ColorBgr;
                    break;
                case "tint":
                    tint = new(action.From ?? tint.At(at), action.To, at, action.Duration, "linear");
                    break;
                case "background-destroy":
                    background = null;
                    break;
            }
        }
        return new(Definition, Sequence, age, songTime, !keepVisible && age >= ResumeTime, background, tintColor, tint.At(age),
            portraits.Select(pair => new NativeInterludePortrait(pair.Value.Sprite, pair.Value.X.At(age), pair.Value.Y.At(age),
                pair.Value.Scale, pair.Value.Mirror, pair.Value.Alpha.At(age), pair.Value.Frame)).ToArray());
    }

    sealed record Tween(double From, double To, double Start, double Duration, string Ease)
    {
        public double At(double time) => Duration <= 0 ? To : From + (To - From) * Easings.Eval(Ease, (time - Start) / Duration);
    }
    sealed class PortraitState(string sprite, double x, double y, double scale, bool mirror)
    {
        public string Sprite = sprite;
        public Tween X = new(x, x, 0, 0, "linear"), Y = new(y, y, 0, 0, "linear"), Alpha = new(1, 1, 0, 0, "linear");
        public double Scale = scale, DestinationX = x, DestinationY = y;
        public bool Mirror = mirror;
        public int Frame;
    }
}

/// <summary>当前采样时刻的剧情立绘。</summary>
/// <param name="Sprite">精灵键。</param>
/// <param name="X">逻辑横坐标。</param>
/// <param name="Y">逻辑纵坐标。</param>
/// <param name="Scale">缩放倍率。</param>
/// <param name="Mirror">是否水平镜像。</param>
/// <param name="Alpha">不透明度。</param>
/// <param name="Frame">当前精灵帧号。</param>
public sealed record NativeInterludePortrait(string Sprite, double X, double Y, double Scale, bool Mirror, double Alpha, int Frame = 0);
/// <summary>插段在当前呈现时刻的绘制状态。</summary>
/// <param name="Definition">来源插段定义。</param>
/// <param name="Sequence">用于绘制的剧情序列。</param>
/// <param name="Age">距插段开始的秒数。</param>
/// <param name="SongTime">对应的歌曲秒数。</param>
/// <param name="Resumed">歌曲是否已恢复播放。</param>
/// <param name="BackgroundSprite">可选的背景精灵键。</param>
/// <param name="TintColorBgr">覆盖色的 BGR 值。</param>
/// <param name="TintAlpha">覆盖色不透明度。</param>
/// <param name="Portraits">当前绘制的立绘集合。</param>
public sealed record NativeInterludeState(NativeInterludeDefinition Definition, NativeSequenceDefinition Sequence, double Age, double SongTime,
    bool Resumed, string? BackgroundSprite, uint TintColorBgr, double TintAlpha, IReadOnlyList<NativeInterludePortrait> Portraits);
