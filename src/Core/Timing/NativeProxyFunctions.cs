namespace KuroakiGimmick.Core;

/// <summary>Source 00 proxy functions; chart ranges are open beat intervals, never render-frame counters.</summary>
public static class NativeProxyFunctions
{
    public static bool Supports(string objectName, string function) => objectName == "obj_00_gimmick"
        && function is "spinProxyZero" or "spinProxyOne" or "spinProxies";

    public static bool TryValue(Chart chart, Timeline timeline, string name, double time, int proxy, out double value)
    {
        value = 0;
        if (chart.ObjectName != "obj_00_gimmick" || proxy < 0 || proxy >= chart.Proxies
            || name is not ("prx" or "pry" or "prrz" or "prxb" or "pryb" or "prrzb")) return false;
        double beat=timeline.Bpm.Beat(time);
        bool found=false;
        foreach (var function in chart.PerFrame)
        {
            if (beat <= function.StartBeat || beat >= function.EndBeat) continue;
            double radius=timeline.Get("spinradius",time);
            if (function.Function=="spinProxyZero" && proxy==0 && name is "prx" or "pry" or "prrz")
            {
                value=name=="prx" ? 90*Math.Sin(beat*Math.PI/16)*radius
                    : (name=="pry" ? 60 : 20)*Math.Cos(beat*Math.PI/8)*radius;
                found=true;
            }
            else if (function.Function=="spinProxyOne" && proxy==1 && name is "prxb" or "pryb" or "prrzb")
            {
                value=name=="prxb" ? 90*Math.Sin(beat*Math.PI/16)*radius
                    : (name=="pryb" ? 30 : 10)*Math.Cos(beat*Math.PI/8)*radius;
                found=true;
            }
            else if (function.Function=="spinProxies" && proxy<4 && name is "prx" or "pry")
            {
                double phase=beat*Math.PI/16+proxy*Math.PI*.5;
                value=(name=="prx" ? 90*Math.Sin(phase) : -90*timeline.Get("spiny",time)+90*Math.Cos(phase))*radius;
                found=true;
            }
        }
        return found;
    }
}
