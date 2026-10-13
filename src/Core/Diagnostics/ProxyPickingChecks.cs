using System.Numerics;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>Window-free checks of the same picker and input policy used by the editor.</summary>
internal static class ProxyPickingChecks
{
    internal static int Run()
    {
        try
        {
            int checks = 0;
            void Check(bool condition, string reason)
            { checks++; if (!condition) throw new InvalidOperationException(reason); }
            string M(string name, double value, int proxy = 0) => $"0,0,linear,{value},{value},{name},{proxy}\n";
            Timeline Map(string mods, bool custom = true)
            {
                var chart = new Chart();
                VsmReader.ReplaceModsText(chart, $"!obj:{(custom ? "obj_custom_gimmick" : "obj_base_gimmick")}\n!proxies:3\n" + mods, "proxy-pick-test.vsm");
                return new Timeline(chart, new ViewerProject { Bpm = 120, Profile = "core" }, 2);
            }
            int? Pick(string mods, float x = 160, float y = 120, int? only = null, bool custom = true) =>
                ProxyScenePicking.Pick(Map(mods, custom), 0, custom, 3, new(x, y), only);
            string active = M("pra", 1), behind = M("pra", 1, 1);
            Check(Pick("") == null, "Default-hidden proxies accepted a hit");
            Check(Pick(active) == 0, "Plain lane missed");
            Check(Pick(active + behind) == 0, "Front-to-back order changed");
            Check(Pick(behind) == 1, "Hidden proxy blocked a visible one");
            Check(Pick(M("pra", .0001) + behind) == 1, "Near-zero alpha intercepted selection");
            Check(Pick(active + behind, only: 1) == 1, "Solo mode selected a different proxy");
            Check(Pick(active + behind, only: 2) == null, "Hidden solo target selected another proxy");
            foreach (var p in new[] { new Vector2(-1, 120), new Vector2(320, 120), new Vector2(160, -1),
                new Vector2(160, 180), new Vector2(float.NaN, 80) })
                Check(Pick(active, p.X, p.Y) == null, "Outside/non-finite point accepted");
            Check(Pick(active + M("przm", 0)) == null, "Singular transform accepted");
            Check(Pick(active + M("pry", 180)) == null, "Offscreen transform accepted");
            Check(Pick(active + M("prcl", 320), 30) == null, "Wide crop selected background as lane");
            Check(Pick(active + M("prcl", 320)) == 0, "Wide crop lost its lane");
            Check(Pick(active + M("prrz", 90), 122, 92) == 0, "Custom rotation missed");
            Check(Pick(active + M("prrz", 90), 198, 72, custom: false) == 0, "Base rotation convention changed");
            foreach (string mods in new[] { M("prrz", 27) + M("prsx", .3) + M("prsy", -.2) + M("przm", .8),
                M("prtrX", .003) + M("prtrY", .004), M("prcl", 208) + M("prcr", 113) + M("przx", -1) })
            {
                var timeline = Map(active + mods); var geometry = ProxyGeometry.Sample(timeline, 0, 0, true);
                Check(geometry.TryProject(new(170, 120), out var point), "Visible source point failed projection");
                Check(ProxyScenePicking.Pick(timeline, 0, true, 3, point) == 0, "Transformed or reversed crop missed");
            }
            Check(Pick(active + M("shcl", .5), 150) == null, "Left mask accepted");
            Check(Pick(active + M("shcr", .5), 170) == null, "Right mask accepted");
            Check(Pick(active + M("shct", .5), 160, 70) == null, "Top mask accepted");
            Check(Pick(active + M("shcb", .5), 160, 120) == null, "Bottom mask accepted");
            Check(Pick(active + M("shcl", 1)) == null, "Fully masked proxy accepted");
            Check(Pick(active + M("shcl", .6) + behind) == 1, "Masked front proxy blocked back proxy");
            Check(Pick(active + M("shcl", .45) + M("shfl", .1)) == 0, "Visible fade band missed");
            Check(Pick(active + M("shcl", .5) + M("shfl", .1)) == null, "Zero-alpha fade boundary accepted");
            var moving = Map(active + "0,4,linear,0,80,prx,0\n");
            Check(ProxyScenePicking.Pick(moving, 0, true, 3, new(125, 120)) == 0, "Displayed pose missed");
            Check(ProxyScenePicking.Pick(moving, 2, true, 3, new(125, 120)) == null, "New playhead pose ignored translation");
            Check(ProxyScenePicking.Pick(Map(behind), 0, true, 1, new(160, 120)) == null, "Deleted/out-of-range target selected");

            CheckInputPriority();
            void CheckInputPriority()
            {
                var pass = ProxyScenePicking.Intent.Pass; var reserve = ProxyScenePicking.Intent.Reserve; var pick = ProxyScenePicking.Intent.Pick;
                Check(ProxyScenePicking.Resolve(false, false, 2, false) == pass, "Disabled grid stole double-click");
                Check(ProxyScenePicking.Resolve(false, true, 2, true) == pass, "Disabled grid stole Shift double-click");
                Check(ProxyScenePicking.Resolve(true, false, 1, false) == pass, "Single click selected proxy");
                Check(ProxyScenePicking.Resolve(true, false, 2, true) == pass, "Image/text priority lost");
                Check(ProxyScenePicking.Resolve(true, false, 2, false) == pick, "Track double-click not selected");
                Check(ProxyScenePicking.Resolve(true, true, 1, true) == reserve, "Shift first press started an object drag");
                Check(ProxyScenePicking.Resolve(true, true, 2, true) == pick, "Shift did not bypass image/text");
                Check(ProxyScenePicking.Resolve(true, true, 2, false) == pick, "Shift track pick missed");
            }
            Console.WriteLine($"PASS proxy picking core: {checks} checks covering input priority, transforms, masks, layering, solo and frame time (no SDL window)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
