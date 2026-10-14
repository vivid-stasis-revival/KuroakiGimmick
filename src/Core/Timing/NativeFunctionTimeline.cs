namespace KuroakiGimmick.Core;

/// <summary>
/// Source-native per-frame programs evaluated on the 60 Hz song clock. A program's
/// last write survives its range; subsequent chart tweens take ownership again.
/// Random calls use an explicit stable seed so preview, seeks and export agree.
/// </summary>
public sealed class NativeFunctionTimeline
{
    sealed record Range(string Function, double Start, double End, int Order);
    readonly Dictionary<(string Name, int Proxy), List<Range>> outputs = [];
    readonly List<Range> toggles = [];
    readonly Timeline timeline;
    readonly string objectName;

    public static IReadOnlyList<string> Inputs(string obj)=>obj switch {
        "obj_00_gimmick"=>["spinradius","spiny"],
        "obj___gimmick" or "obj_ram_gimmick"=>["spinradiusx","spinradiusz","fakezy","fakezyb","float","wiggly","prvib"],
        "obj_marenol_gimmick"=>["vibx","viby","flicker"],
        "obj_dracula_gimmick"=>["vib"],
        "obj_times_gimmick"=>["spinrad","spinang","siny","vib"],
        _=>[]};

    public static bool Supports(string obj, string function) => obj switch
    {
        "obj_00_gimmick" => function is "spinProxyZero" or "spinProxyOne" or "spinProxies",
        "obj___gimmick" => function is "quake" or "spin3D" or "wiggle" or "swirl",
        "obj_ram_gimmick" => function is "quake" or "wiggle" or "flicker",
        "obj_marenol_gimmick" => function == "spinDizzy",
        "obj_dracula_gimmick" => function == "dracular",
        "obj_times_gimmick" => function == "spinVibrate",
        _ => false
    };

    public NativeFunctionTimeline(Timeline timeline, Chart chart)
    {
        this.timeline=timeline; objectName=chart.ObjectName;
        for (int order=0;order<chart.PerFrame.Count;order++)
        {
            var f=chart.PerFrame[order];
            if (!Supports(objectName,f.Function)) continue;
            void Add(int proxy, string names, double min=double.NegativeInfinity, double max=double.PositiveInfinity)
            {
                double start=Math.Max(f.StartBeat,min), end=Math.Min(f.EndBeat,max);
                if (start>=end || proxy>=chart.Proxies) return;
                var range=new Range(f.Function,timeline.Bpm.Time(start),timeline.Bpm.Time(end),order);
                foreach (string name in names.Split(' '))
                {
                    if (!outputs.TryGetValue((name,proxy),out var list)) outputs[(name,proxy)]=list=[];
                    list.Add(range);
                }
            }
            switch (f.Function)
            {
                case "spinProxyZero": Add(0,"prx pry prrz"); break;
                case "spinProxyOne": Add(1,"prxb pryb prrzb"); break;
                case "spinProxies": for(int p=0;p<4;p++) Add(p,"prx pry"); break;
                case "quake": for(int p=0;p<8;p++) Add(p,"prxd"); break;
                case "spin3D": for(int p=0;p<4;p++) Add(p,"prx pry przm przy prrz"); break;
                case "wiggle": for(int p=0;p<4;p++) Add(p,"prxc prrx prry prrzb"); break;
                case "swirl": for(int p=0;p<8;p++) Add(p,"prxc pryc prrzb"); break;
                case "flicker":
                    for(int p=0;p<4;p++) Add(p,"prxc");
                    toggles.Add(new(f.Function,timeline.Bpm.Time(f.StartBeat),timeline.Bpm.Time(f.EndBeat),order));
                    break;
                case "dracular": Add(-1,"posx posy"); break;
                case "spinVibrate": Add(-1,"tx2 ty2 tx3 ty3"); break;
                case "spinDizzy":
                    Add(-1,"posx posy");
                    Add(-1,"twx1 twy1 twx2 twy2",80,100);
                    Add(-1,"twx1 twy1 twx2 twy2",282,332);
                    Add(-1,"sina flicker",92,99.9);
                    if (Math.Max(f.StartBeat,92)<Math.Min(f.EndBeat,99.9))
                        toggles.Add(new(f.Function,timeline.Bpm.Time(Math.Max(f.StartBeat,92)),timeline.Bpm.Time(Math.Min(f.EndBeat,99.9)),order));
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
        double radius=M("spinradius"), phase=beat*Math.PI/16;
        switch(selected.Function)
        {
            case "spinProxyZero": value=name=="prx"?90*Math.Sin(phase)*radius:(name=="pry"?60:20)*Math.Cos(beat*Math.PI/8)*radius;break;
            case "spinProxyOne": value=name=="prxb"?90*Math.Sin(phase)*radius:(name=="pryb"?30:10)*Math.Cos(beat*Math.PI/8)*radius;break;
            case "spinProxies":
                phase+=proxy*Math.PI*.5;
                value=(name=="prx"?90*Math.Sin(phase):-90*M("spiny")+90*Math.Cos(phase))*radius;break;
            case "quake":
                // Both source assignments target prxd. The second random draw wins; pryd is untouched.
                value=SignedRandom(32,proxy*2+1)*M("prvib",proxy);break;
            case "wiggle":
                double floatValue=M("float"), wig=M("wiggly")*(objectName=="obj_ram_gimmick"?.75:.55+.45*Math.Cos(beat*Math.PI*.5));
                double speed=objectName=="obj_ram_gimmick"||beat<=380?3:4;
                phase=(beat-324)*Math.PI*speed+Math.PI*.5*proxy;
                value=name switch {"prxc"=>M("spinradiusx")*(-1.5+proxy)*(.5+.5*Math.Cos(beat*Math.PI*.5))+floatValue*(-1.5+proxy),
                    "prrx"=>Math.Cos(phase)*wig,"prry"=>Math.Sin(phase)*wig,_=>Math.Cos(phase)*wig*.25};break;
            case "spin3D":
                double quadrant=Math.PI*.5*proxy+Math.PI;
                phase=((16*Math.PI+(beat-180)*Math.PI/12-quadrant)%(Math.PI*.5))+quadrant;
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
            case "flicker": value=M("spinradiusx")*ToggleSign(selectedTick,selected.Order,false);break;
            case "dracular": value=SignedRandom(32,name=="posx"?0:1)*M("vib")*.02;break;
            case "spinVibrate":
                double angle=M("spinang")*Math.PI/180;
                value=name switch {"tx2"=>M("spinrad")*Math.Cos(angle),
                    "ty2"=>M("spinrad")*.5625*Math.Sin(angle)+M("siny")*Math.Sin(beat*Math.PI*.5),
                    _=>SignedRandom(8,name=="tx3"?0:1)*M("vib")};break;
            case "spinDizzy":
                if(name is "posx" or "posy") value=SignedRandom(32,name=="posx"?0:1)*M(name=="posx"?"vibx":"viby")*.02;
                else if(name is "sina" or "flicker")
                {
                    double reset=timeline.NativeTrackWriteTime("flicker",-1,inputTime);
                    double basis=timeline.NativeRawValue("flicker",reset);
                    long after=double.IsNegativeInfinity(reset)?-1:(long)Math.Round(reset*60);
                    double toggle=basis*ToggleSign(selectedTick,selected.Order,name=="flicker",after);
                    value=name=="flicker"?toggle:.15*toggle+.15*Math.Cos(beat*1.5);
                }
                else if(beat<100) value=name switch {"twx1"=>.5+.2*Math.Sin(beat*.25),"twx2"=>.5-.2*Math.Sin(beat*.25),
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
