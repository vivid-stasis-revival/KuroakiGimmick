namespace KuroakiGimmick.Core;

/// <summary>
/// Source-native per-frame programs evaluated on the 60 Hz song clock. A program's
/// last write survives its range; subsequent chart tweens take ownership again.
/// Random calls use an explicit stable seed so preview, seeks and export agree.
/// </summary>
public sealed class NativeFunctionTimeline
{
    sealed record Range(string Function, double Start, double End, int Order, GimmickFunctionProgram Program);
    readonly Dictionary<(string Name, int Proxy), List<Range>> outputs = [];
    readonly List<Range> toggles = [];
    readonly Timeline timeline;
    public static readonly string[] Kernels=["orbit-primary","orbit-secondary","orbit-quad","jitter-x","orbit-perspective","oscillate-perspective",
        "oscillate-flat","swirl","toggle-x","camera-jitter","window-orbit","camera-twist"];
    public static bool Supports(GimmickDefinition? definition,string function)=>definition?.FunctionPrograms.ContainsKey(function)==true;

    public NativeFunctionTimeline(Timeline timeline, Chart chart, GimmickDefinition? definition=null)
    {
        this.timeline=timeline;definition??=timeline.Native?.Data;
        for (int order=0;order<chart.PerFrame.Count;order++)
        {
            var f=chart.PerFrame[order];
            if(definition == null || !definition.FunctionPrograms.TryGetValue(f.Function,out var program))continue;
            void Add(int proxy, string names, double min=double.NegativeInfinity, double max=double.PositiveInfinity)
            {
                double start=Math.Max(f.StartBeat,min), end=Math.Min(f.EndBeat,max);
                if (start>=end || proxy>=chart.Proxies) return;
                var range=new Range(program.Kernel,timeline.Bpm.Time(start),timeline.Bpm.Time(end),order,program);
                foreach (string name in names.Split(' '))
                {
                    if (!outputs.TryGetValue((name,proxy),out var list)) outputs[(name,proxy)]=list=[];
                    list.Add(range);
                }
            }
            switch (program.Kernel)
            {
                case "orbit-primary": Add(0,"prx pry prrz"); break;
                case "orbit-secondary": Add(1,"prxb pryb prrzb"); break;
                case "orbit-quad": for(int p=0;p<4;p++) Add(p,"prx pry"); break;
                case "jitter-x": for(int p=0;p<8;p++) Add(p,"prxd"); break;
                case "orbit-perspective": for(int p=0;p<4;p++) Add(p,"prx pry przm przy prrz"); break;
                case "oscillate-flat":
                case "oscillate-perspective": for(int p=0;p<4;p++) Add(p,"prxc prrx prry prrzb"); break;
                case "swirl": for(int p=0;p<8;p++) Add(p,"prxc pryc prrzb"); break;
                case "toggle-x":
                    for(int p=0;p<4;p++) Add(p,"prxc");
                    toggles.Add(new(program.Kernel,timeline.Bpm.Time(f.StartBeat),timeline.Bpm.Time(f.EndBeat),order,program));
                    break;
                case "camera-jitter": Add(-1,"posx posy"); break;
                case "window-orbit": Add(-1,"tx2 ty2 tx3 ty3"); break;
                case "camera-twist":
                    Add(-1,"posx posy");
                    foreach(var window in program.Windows)
                    {
                        Add(-1,string.Join(" ",window.Outputs),window.Start,window.End);
                        if(window.Toggle && Math.Max(f.StartBeat,window.Start)<Math.Min(f.EndBeat,window.End))
                            toggles.Add(new(program.Kernel,timeline.Bpm.Time(Math.Max(f.StartBeat,window.Start)),timeline.Bpm.Time(Math.Min(f.EndBeat,window.End)),order,program));
                    }
                    break;
            }
        }
    }

    static long FirstTick(double time) => Math.Max(0,(long)Math.Floor(time*60+1e-8)+1);
    static long LastTick(double time) => (long)Math.Ceiling(time*60-1e-8)-1;

    public bool TryValue(string name, double time, int proxy, out double value)
    {
        value=0;
        if (!outputs.TryGetValue((name,proxy),out var ranges) || !double.IsFinite(time)) return false;
        long now=(long)Math.Floor(time*60+1e-8), selectedTick=long.MinValue;
        Range? selected=null;
        foreach (var range in ranges)
        {
            long tick=Math.Min(now,LastTick(range.End));
            if (tick<FirstTick(range.Start)) continue;
            if (tick>selectedTick || tick==selectedTick && range.Order>=(selected?.Order??-1))
            { selected=range; selectedTick=tick; }
        }
        if (selected==null) return false;
        double at=selectedTick/60.0;
        // cc.Step_1 runs updateMods/per-frame functions BEFORE applying active tweens.
        if (timeline.NativeTrackWriteTime(name,proxy,now/60.0)>=at-1e-9) return false;
        double beat=timeline.Bpm.Beat(at), inputTime=selectedTick==0?double.NegativeInfinity:(selectedTick-1)/60.0;
        double M(string mod,int p=-1)=>timeline.NativeRawValue(mod,inputTime,p);
        double SignedRandom(double rate,int draw) => Timeline.Hash(unchecked((uint)(long)Math.Floor(beat*rate)*65537u+(uint)draw*9781u+731u))*2-1;
        double N(string key,double fallback=0)=>selected.Program.Parameters.GetValueOrDefault(key,fallback);
        double radius=M("spinradius"), phase=beat*Math.PI/16;
        switch(selected.Function)
        {
            case "orbit-primary": value=name=="prx"?90*Math.Sin(phase)*radius:(name=="pry"?60:20)*Math.Cos(beat*Math.PI/8)*radius;break;
            case "orbit-secondary": value=name=="prxb"?90*Math.Sin(phase)*radius:(name=="pryb"?30:10)*Math.Cos(beat*Math.PI/8)*radius;break;
            case "orbit-quad":
                phase+=proxy*Math.PI*.5;
                value=(name=="prx"?90*Math.Sin(phase):-90*M("spiny")+90*Math.Cos(phase))*radius;break;
            case "jitter-x":
                // Both source assignments target prxd. The second random draw wins; pryd is untouched.
                value=SignedRandom(32,proxy*2+1)*M("prvib",proxy);break;
            case "oscillate-flat":
            case "oscillate-perspective":
                double floatValue=M("float"), wig=M("wiggly")*(selected.Function=="oscillate-flat"?.75:.55+.45*Math.Cos(beat*Math.PI*.5));
                double speed=selected.Function=="oscillate-flat"||beat<=N("speedChangeBeat")?3:4;
                phase=(beat-N("phaseOriginBeat"))*Math.PI*speed+Math.PI*.5*proxy;
                value=name switch {"prxc"=>M("spinradiusx")*(-1.5+proxy)*(.5+.5*Math.Cos(beat*Math.PI*.5))+floatValue*(-1.5+proxy),
                    "prrx"=>Math.Cos(phase)*wig,"prry"=>Math.Sin(phase)*wig,_=>Math.Cos(phase)*wig*.25};break;
            case "orbit-perspective":
                double quadrant=Math.PI*.5*proxy+Math.PI;
                phase=((N("phaseOffset",16)*Math.PI+(beat-N("phaseOriginBeat"))*Math.PI/N("phasePeriod",12)-quadrant)%(Math.PI*.5))+quadrant;
                double fl=M("float");
                value=name switch {"prx"=>Math.Cos(phase)*M("spinradiusx")+20*fl*Math.Sin(phase*2),
                    "pry"=>20*fl*Math.Cos(phase*2),"przm"=>1-Math.Sin(phase)*M("spinradiusz"),
                    "przy"=>(((proxy%2==0 && beat%12<6)||(proxy%2==1 && beat%12>=6))?M("fakezy"):1)*M("fakezyb"),
                    _=>10*fl*Math.Sin(phase*2)};break;
            case "swirl":
                int i=proxy%4;
                double a=2.0734511513692637*i;
                double b=1.0367255756846319*((i+(proxy>=4?.5:0))/4.0);
                value=name switch {
                    "prxc"=>M("float")*(.8*Math.Sin(a+beat*Math.PI*.25)+.4*Math.Sin(a+beat*Math.PI*.8*.2))+M("spinradiusx")*.8*Math.Sin(b+beat*Math.PI*.125),
                    "pryc"=>M("float")*(.6*Math.Cos(a+beat*Math.PI*.18)+.47*Math.Cos(a+beat*Math.PI*.8*.23))+M("spinradiusx")*.6*Math.Cos(b+beat*Math.PI*.25),
                    _=>M("float")*(.4*Math.Sin(a+beat*Math.PI*.13)+.2*Math.Cos(a+beat*Math.PI*.8*.16))};break;
            case "toggle-x": value=M("spinradiusx")*ToggleSign(selectedTick,selected.Order,false);break;
            case "camera-jitter": value=SignedRandom(32,name=="posx"?0:1)*M("vib")*.02;break;
            case "window-orbit":
                double angle=M("spinang")*Math.PI/180;
                value=name switch {"tx2"=>M("spinrad")*Math.Cos(angle),
                    "ty2"=>M("spinrad")*.5625*Math.Sin(angle)+M("siny")*Math.Sin(beat*Math.PI*.5),
                    _=>SignedRandom(8,name=="tx3"?0:1)*M("vib")};break;
            case "camera-twist":
                if(name is "posx" or "posy") value=SignedRandom(32,name=="posx"?0:1)*M(name=="posx"?"vibx":"viby")*.02;
                else if(name is "sina" or "flicker")
                {
                    double reset=timeline.NativeTrackWriteTime("flicker",-1,inputTime);
                    double basis=timeline.NativeRawValue("flicker",reset);
                    long after=double.IsNegativeInfinity(reset)?-1:(long)Math.Round(reset*60);
                    double toggle=basis*ToggleSign(selectedTick,selected.Order,name=="flicker",after);
                    value=name=="flicker"?toggle:.15*toggle+.15*Math.Cos(beat*1.5);
                }
                else if(beat<N("twistTransitionBeat")) value=name switch {"twx1"=>.5+.2*Math.Sin(beat*.25),"twx2"=>.5-.2*Math.Sin(beat*.25),
                    "twy1"=>.6+.25*Math.Cos(beat*.25),_=>.6-.25*Math.Cos(beat*.25)};
                else value=name switch {"twx1"=>.7,"twx2"=>.3,"twy1"=>.6+.3*Math.Cos(beat*.125),_=>.6-.3*Math.Cos(beat*.125)};
                break;
            default:return false;
        }
        return double.IsFinite(value);
    }

    double ToggleSign(long tick,int order,bool includeCurrent,long after=-1)
    {
        long count=0;
        foreach(var r in toggles)
        {
            long end=Math.Min(tick-1,LastTick(r.End)),start=Math.Max(after+1,FirstTick(r.Start));
            if(end>=start) count+=end-start+1;
            if(tick>=start && tick<=LastTick(r.End) && (r.Order<order || includeCurrent && r.Order==order)) count++;
        }
        return (count&1)==0?1:-1;
    }
}
