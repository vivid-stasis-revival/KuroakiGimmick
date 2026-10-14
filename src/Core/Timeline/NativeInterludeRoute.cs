namespace KuroakiGimmick.Core;

/// <summary>Insert readable source story pauses into presentation time while retaining unmodified song/chart seconds.</summary>
public sealed class NativeInterludeRoute
{
    public sealed record Entry(double SongTime, double PresentationStart, NativeInterludeSchedule Schedule)
    {
        public double Duration => Schedule.ResumeTime;
        public double End => PresentationStart + Duration;
    }
    public IReadOnlyList<Entry> Entries { get; }
    public sealed record Leg(double SongStart,double SongEnd,double PresentationStart,double Duration,bool Hold=false);
    public sealed record Seek(double From,double To,double PresentationTime,double FadeInSeconds);
    public IReadOnlyList<Leg> Legs { get; }
    public IReadOnlyList<Seek> Seeks { get; }
    public bool HasChanges=>Entries.Count>0||Seeks.Count>0;

    public NativeInterludeRoute(Chart chart, BpmMap bpm, IReadOnlyList<NativeInterludeDefinition> definitions,
        IReadOnlyList<NativeSequenceDefinition.SongSeek>? songSeeks=null)
    {
        var entries = new List<Entry>();
        var legs=new List<Leg>();var seeks=new List<Seek>();
        var scheduled = new List<(double Time,int Order,NativeInterludeDefinition? Definition,NativeSequenceDefinition.SongSeek? Seek)>();
        foreach (var e in chart.Mods.OrderBy(e => e.Beat).ThenBy(e => e.Order))
        {
            foreach (var definition in definitions.Where(d => d.Trigger == e.Name))
                scheduled.Add((Math.Max(definition.StartSongTime ?? 0,bpm.Time(e.Beat)),e.Order,definition,null));
            foreach(var seek in (songSeeks??[]).Where(seek=>seek.Trigger==e.Name))
                scheduled.Add((Math.Max(0,bpm.Time(e.Beat)),e.Order,null,seek));
        }
        double cursor=0,presentation=0,inserted=0;
        foreach(var item in scheduled.OrderBy(item=>item.Time).ThenBy(item=>item.Order))
        {
            if(item.Time>cursor)
            {legs.Add(new(cursor,item.Time,presentation,item.Time-cursor));presentation+=item.Time-cursor;cursor=item.Time;}
            if(item.Seek is { } seek)
            {seeks.Add(new(cursor,seek.Time,presentation,seek.FadeInSeconds));cursor=seek.Time;}
            else if(item.Definition is { } definition)
            {
                var schedule = new NativeInterludeSchedule(definition);
                if(schedule.ResumeTime<=0)continue;
                entries.Add(new(cursor,presentation,schedule));
                legs.Add(new(cursor,cursor,presentation,schedule.ResumeTime,true));
                presentation+=schedule.ResumeTime;
                inserted += schedule.ResumeTime;
            }
            if(entries.Count>64||seeks.Count>64||inserted>3600||presentation>86400)
                throw new InvalidDataException("Native story route exceeds 64 controls, one hour of inserted story or 24 hours of playback.");
        }
        legs.Add(new(cursor,double.PositiveInfinity,presentation,double.PositiveInfinity));
        Entries = entries;
        Legs=legs;Seeks=seeks;
    }

    public double SongAt(double presentation)
    {
        if(presentation<0)return presentation;
        var leg=Legs.LastOrDefault(leg=>leg.PresentationStart<=presentation)??Legs[0];
        return leg.Hold?leg.SongStart:leg.SongStart+presentation-leg.PresentationStart;
    }

    public double PresentationAt(double song, bool afterHolds = false)
    {
        var candidates=new List<double>();
        foreach(var leg in Legs)
            if(leg.Hold)
            {if(leg.SongStart==song)candidates.Add(leg.PresentationStart+(afterHolds?leg.Duration:0));}
            else if(song>=leg.SongStart&&song<=leg.SongEnd)candidates.Add(leg.PresentationStart+song-leg.SongStart);
        foreach(var seek in Seeks)
            if(seek.To>seek.From&&song>=seek.From&&song<seek.To)candidates.Add(seek.PresentationTime);
        return candidates.Count==0?song:afterHolds?candidates.Max():candidates.Min();
    }

    public NativeInterludeState? At(double presentation)
    {
        var entry = Entries.LastOrDefault(e => e.PresentationStart <= presentation && presentation < e.PresentationStart + e.Schedule.TotalDuration);
        return entry == null ? null : entry.Schedule.Sample(presentation - entry.PresentationStart, entry.SongTime);
    }
}

public sealed class NativeInterludeSchedule
{
    public sealed record TimedAction(double Time, NativeInterludeAction Action);
    public NativeInterludeDefinition Definition { get; }
    public NativeSequenceDefinition Sequence { get; } = new();
    public IReadOnlyList<TimedAction> Actions { get; }
    public double ResumeTime { get; }
    public double TotalDuration { get; }

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
                if(action.Kind=="clear-text")Sequence.Story.Add(new(time,"interlude",step.Speaker,"",Clear:true));
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

    public NativeInterludeState Sample(double age, double songTime, bool keepVisible = false)
    {
        var portraits = new Dictionary<string, PortraitState>(StringComparer.Ordinal);
        string? background = Definition.BackgroundSprite;
        uint tintColor = 0xffffff;
        var tint = new Tween(0,0,0,0,"linear");
        foreach (var item in Actions)
        {
            if (item.Time > age) break;
            var action = item.Action;
            double at = item.Time;
            portraits.TryGetValue(action.Id ?? "", out var portrait);
            switch (action.Kind)
            {
                case "background": background = action.Sprite; break;
                case "portrait-create":
                    portraits[action.Id!] = new(action.Sprite!, action.X ?? 0, action.Y ?? 0, action.Scale, action.Mirror) { Frame=action.Frame }; break;
                case "portrait-frame" when portrait != null: portrait.Frame=action.Frame; break;
                case "portrait-flip" when portrait != null:
                    portrait.Mirror=!portrait.Mirror;
                    if(action.Bounce)
                    {
                        portrait.X=new(portrait.X.At(at),portrait.DestinationX,at,2.0/3,"outExpo");
                        portrait.Y=new(portrait.Y.At(at)+10,portrait.DestinationY,at,2.0/3,"outExpo");
                    }
                    break;
                case "portrait-change" when portrait != null:
                    portrait.Sprite = action.Sprite!;
                    if (action.Bounce)
                    {
                        portrait.X=new(portrait.X.At(at),portrait.DestinationX,at,2.0/3,"outExpo");
                        portrait.Y = new(portrait.Y.At(at) + 10, portrait.DestinationY, at, 2.0/3, "outExpo");
                    }
                    break;
                case "portrait-move" when portrait != null:
                    double x = action.X ?? portrait.X.At(at), y = action.Y is null or 0 ? portrait.Y.At(at) : action.Y.Value;
                    portrait.X = new(portrait.X.At(at),x,at,action.Duration,"outQuint");
                    portrait.Y = new(portrait.Y.At(at),y,at,action.Duration,"outQuint");
                    portrait.DestinationX=x;portrait.DestinationY = y;
                    if (action.Mirror)
                    {
                        portrait.Mirror = !portrait.Mirror;
                        portrait.X=new(portrait.X.At(at),portrait.DestinationX,at,2.0/3,"outExpo");
                        portrait.Y=new(portrait.Y.At(at)+10,portrait.DestinationY,at,2.0/3,"outExpo");
                    }
                    break;
                case "portrait-alpha" when portrait != null:
                    portrait.Alpha = new(action.From ?? portrait.Alpha.At(at),action.To,at,action.Duration,"linear"); break;
                case "portrait-destroy": if (action.Id == null) portraits.Clear(); else portraits.Remove(action.Id); break;
                case "tint-color": tintColor = action.ColorBgr; break;
                case "tint": tint = new(action.From ?? tint.At(at),action.To,at,action.Duration,"linear"); break;
                case "background-destroy": background = null; break;
            }
        }
        return new(Definition,Sequence,age,songTime,!keepVisible && age >= ResumeTime,background,tintColor,tint.At(age),
            portraits.Select(pair => new NativeInterludePortrait(pair.Value.Sprite,pair.Value.X.At(age),pair.Value.Y.At(age),
                pair.Value.Scale,pair.Value.Mirror,pair.Value.Alpha.At(age),pair.Value.Frame)).ToArray());
    }

    sealed record Tween(double From,double To,double Start,double Duration,string Ease)
    {
        public double At(double time) => Duration <= 0 ? To : From + (To-From)*Easings.Eval(Ease,(time-Start)/Duration);
    }
    sealed class PortraitState(string sprite,double x,double y,double scale,bool mirror)
    {
        public string Sprite = sprite;
        public Tween X = new(x,x,0,0,"linear"), Y = new(y,y,0,0,"linear"), Alpha = new(1,1,0,0,"linear");
        public double Scale = scale, DestinationX=x, DestinationY = y;
        public bool Mirror = mirror;
        public int Frame;
    }
}

public sealed record NativeInterludePortrait(string Sprite,double X,double Y,double Scale,bool Mirror,double Alpha,int Frame=0);
public sealed record NativeInterludeState(NativeInterludeDefinition Definition,NativeSequenceDefinition Sequence,double Age,double SongTime,
    bool Resumed,string? BackgroundSprite,uint TintColorBgr,double TintAlpha,IReadOnlyList<NativeInterludePortrait> Portraits);
