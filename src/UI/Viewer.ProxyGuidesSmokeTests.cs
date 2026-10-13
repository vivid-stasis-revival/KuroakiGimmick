using System.Numerics;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    /// <summary>Checks guides against the former projection shader and the real game UV mask on the GPU.</summary>
    public void SmokeProxyGuides()
    {
        SmokeProxyLabelMotion();
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        void Near(float a, float b, string reason) => Check(Math.Abs(a - b) < .002, $"{reason}: {a} != {b}");
        Timeline TimelineFor(string mods, bool custom = true)
        {
            var chart = new Chart();
            VsmReader.ReplaceModsText(chart, $"!obj:{(custom ? "obj_custom_gimmick" : "obj_base_gimmick")}\n!proxies:2\n" + mods, "guides.vsm");
            return new Timeline(chart, new ViewerProject { Bpm = 120 }, 2);
        }
        string Mod(string name, double value, int proxy = 0) => $"0,0,linear,{value},{value},{name},{proxy}\n";
        var plain = ProxyGeometry.Sample(TimelineFor(""), 0, 0, true);
        Near(plain.Left, 113, "Default source left"); Near(plain.Right, 208, "Default source right");
        var half = new ProxyMask(.5f, .4f, 0, 0, .05f, 0, 0, 0);
        Near(half.AlphaAt(new(150, 80)), 0, "Mask must use full-field UV, not crop-relative UV");
        Near(half.AlphaAt(new(168, 80)), .5f, "16-pixel left fade");
        Near(half.AlphaAt(new(184, 80)), 1, "Uncovered region");
        Near(half.AlphaAt(new(198, 80)), 0, "Right coverage");
        var animated = TimelineFor("0,4,linear,0,1,shcl,0\n");
        Near(ProxyMask.Sample(animated, 1, 0).Left, .5f, "Playhead interpolation");
        Near(ProxyMask.Sample(animated, .5, 0).Left, .25f, "Reverse seek interpolation");
        foreach (bool custom in new[] { false, true })
        {
            var pose = ProxyGeometry.Sample(TimelineFor(Mod("prrz", 90) + Mod("prx", 10), custom), 0, 0, custom);
            Check(pose.TryProject(new(180, 82), out var p), "Rotated point cannot project");
            Near(p.X, 170, "Rotation/translation X"); Near(p.Y, custom ? 102 : 62, "Base/custom rotation convention");
        }
        var crossing = ProxyGeometry.Sample(TimelineFor(Mod("prtrX", .03)), 0, 0, true);
        Check(crossing.ClipSegment(new(113, 82), new(208, 82), out var ca, out var cb) &&
            ca.X >= -.002 && cb.X <= 320.002 && float.IsFinite(ca.Y), "Camera-plane clipping failed");
        var singular = ProxyGeometry.Sample(TimelineFor(Mod("przm", 0)), 0, 0, true);
        Check(!singular.TryUnproject(new(160, 82), out _), "Collapsed proxy must not produce mouse coordinates");
        Span<Vector2> clipped = stackalloc Vector2[9];
        Check(plain.ClipRegion(new(-100, -100, 520, 380), clipped) == 4, "Full-screen region with offscreen corners lost its label");
        Check(crossing.ClipRegion(crossing.Source, clipped) >= 3, "Camera-plane crossing region lost its label");
        Check(singular.ClipRegion(singular.Source, clipped) == 0, "Collapsed geometry created a misleading visible label");

        // Independent oracle: the original production vertex transform before geometry was shared.
        const string originalVertex = """
            #version 330 core
            layout(location=0) in vec2 a_position;
            layout(location=1) in vec2 a_uv;
            layout(location=2) in vec4 a_color;
            uniform vec2 u_resolution;
            uniform vec2 proxyOrigin; uniform vec2 proxyScale; uniform vec2 proxyRotation;
            uniform vec2 proxyTrapezoid; uniform vec2 proxySkew; uniform vec2 proxyTranslation;
            out vec2 v_vTexcoord; out vec4 v_vColour;
            void main() {
                vec2 p = (a_position - proxyOrigin) * proxyScale;
                p = vec2(p.x*proxyRotation.x - p.y*proxyRotation.y, p.x*proxyRotation.y + p.y*proxyRotation.x);
                float w = 1.0 + dot(p, proxyTrapezoid);
                p = vec2(p.x + p.y*proxySkew.x, p.y + p.x*proxySkew.y);
                p += proxyTranslation*w;
                gl_Position = vec4(2.0*p.x/u_resolution.x-w,w-2.0*p.y/u_resolution.y,0.0,w);
                v_vTexcoord=a_uv; v_vColour=a_color;
            }
            """;
        using var oracle = new Shader(Canvas.Gpu, originalVertex,
            Shader.AdaptGml(File.ReadAllText(Path.Combine(Paths.Assets, "Shaders", "proxy.frag"))));
        using var expected = new Target(Canvas.Gpu, 320, 180);
        using var actual = new Target(Canvas.Gpu, 320, 180);
        string maskMods = Mod("shcl", .47) + Mod("shcr", .38) + Mod("shfl", .035) + Mod("shfr", .02) +
            Mod("shct", .1) + Mod("shcb", .2) + Mod("shft", .04) + Mod("shfb", .06);
        string transform = Mod("prx", 14) + Mod("pry", -9) + Mod("przm", .9) + Mod("prrz", 23) + Mod("prsx", .15) + Mod("prsy", -.12);
        string[] fixtures = ["", transform, transform + Mod("prtrX", .003) + Mod("prtrY", .004),
            Mod("prcl", 208) + Mod("prcr", 113) + Mod("przx", -1), Mod("prtrX", .03),
            Mod("shcl", .9), Mod("prcl", 0) + Mod("prcr", 320) + Mod("prct", 0)];
        foreach (string fixture in fixtures)
        {
            var timeline = TimelineFor(maskMods + fixture);
            var pose = ProxyGeometry.Sample(timeline, 0, 0, true); var mask = ProxyMask.Sample(timeline, 0, 0);
            double M(string name) => timeline.Get(name, 0, 0);
            Canvas.Begin(expected, 320, 180, 320, 180, new Color(0, 0, 0));
            oracle.Vec2("proxyOrigin", pose.Origin.X, pose.Origin.Y);
            oracle.Vec2("proxyScale", pose.ScaleX, pose.ScaleY);
            oracle.Vec2("proxyRotation", Math.Cos(pose.Angle * Math.PI / 180), Math.Sin(pose.Angle * Math.PI / 180));
            oracle.Vec2("proxyTrapezoid", M("prtrX"), M("prtrY"));
            oracle.Vec2("proxySkew", -M("prsx"), -M("prsy")); oracle.Vec2("proxyTranslation", 160 + pose.X, 82 + pose.Y);
            oracle.Float("xperiod", 10); oracle.Float("yperiod", 10); mask.Apply(oracle);
            Canvas.Polygon(Canvas.White, new(pose.Left, pose.Top), new(pose.Right, pose.Top),
                new(pose.Left, pose.Bottom), new(pose.Right, pose.Bottom), Color.White, pose.UV, oracle);
            var reference = Canvas.Read(expected);
            Canvas.Begin(actual, 320, 180, 320, 180, new Color(0, 0, 0));
            Renderer.DrawProxyGuides(pose, mask, new(0, 0, 320, 180), Color.White, Color.White.Alpha(0));
            var pixels = Canvas.Read(actual);
            int mismatched = 0;
            for (int i = 0; i < pixels.Length; i += 4) if (Math.Abs(reference[i] - pixels[i]) > 2) mismatched++;
            Check(mismatched <= 8, $"Guide differs from original projection/mask: {mismatched} pixels; {fixture}");
            foreach (var source in new[] { new Vector2(160, 82), new Vector2(185, 110) })
                if (pose.TryProject(source, out var screen))
                {
                    Check(pose.TryUnproject(screen, out var back), "Inverse projection failed");
                    Near(back.X, source.X, "Mouse inverse X"); Near(back.Y, source.Y, "Mouse inverse Y");
                }
        }
        Console.WriteLine("PASS proxy geometry/GPU: original shader parity, UV mask and fades, signed crops, rotation, scale, shear, perspective, camera clipping and reverse seeks");

        preferences.UiAnimations = false;
        preferences.Workspace = new WorkspaceLayout { FollowDisplayScale = false, UiScale = 1 };
        string vsm = "!obj:obj_custom_gimmick\n!proxies:2\n" + Mod("pra", 1) + Mod("prx", -65) +
            Mod("pra", 1, 1) + Mod("prx", 35, 1) + Mod("prrz", 12, 1) + Mod("prtrY", .002, 1) +
            Mod("shcl", .47, 1) + Mod("shcr", .38, 1) + Mod("shfl", .035, 1) + Mod("shfr", .02, 1);
        Current = new Session(new ViewerProject { Bpm = 120, RoomPreset = "none", Profile = "core", GameUiEnabled = false }, editedVsm: vsm);
        editor = new EditorDocument(Current); editorMode = true; imageCanvas = textCanvas = desktopPreview = false;
        notes = true;
        workflow = ""; help = settings = false; renderedRevision = editor.Revision; layoutRevision = -1;
        newProxy = 0; prGridMode = 0;
        var clip = editor.Vsm.Clips.First(c => c.Name == "shcl");
        SetClipSelection(clip.Id);
        Check(newProxy == 1 && TryPrGridTarget(out int target) && target == 1, "SH clip kept a stale PR target");
        string sourceText = editor.Vsm.Text;
        void Frame() => Draw(1440, 940);
        void Click(float x, float y)
        {
            Handle(new() { Type = 0x401, Button = 1, X = x, Y = y }); Frame();
            Handle(new() { Type = 0x402, Button = 1, X = x, Y = y }); Frame();
        }
        Frame();
        EditClip(c => c with { Proxy = 0 }, "Proxy guide target check");
        Check(TryPrGridTarget(out target) && target == 0, "Inspector target edit left a stale guide");
        editor.Undo();
        Check(TryPrGridTarget(out target) && target == 1, "Undo target edit left a stale guide");
        renderedRevision = editor.Revision;
        var sceneWithoutGuides = Canvas.Read(Renderer.Final);
        prGridMode = 2; Frame();
        Check(sceneWithoutGuides.SequenceEqual(Canvas.Read(Renderer.Final)), "Editor guides leaked into scene/export");
        Check(!TryPrGridTarget(out _) && prGridHudRect.W == 0, "OFF grid still owns a hit area");
        prGridMode = 0; Frame();
        foreach (int step in new[] { 10, 5, 20 })
        { Click(prGridHudRect.X + prGridHudRect.W - 50, prGridHudRect.Y + 12); Check(prGridStep == step, "Grid step click failed"); }
        Click(prGridHudRect.X + prGridHudRect.W - 14, prGridHudRect.Y + 12);
        Check(prGridHudExpanded, "Grid details did not expand");
        Handle(new() { Type = 0x401, Button = 1, X = prGridHudRect.X + 15, Y = prGridHudRect.Y + 12 });
        Check(prGridHudDragging, "Grid header did not capture drag");
        Handle(new() { Type = 0x20F }); Check(!prGridHudDragging, "Grid drag survived focus loss");
        imageCanvas = true; Check(!TryPrGridTarget(out _), "Image canvas owns a hidden PR HUD"); imageCanvas = false;
        newProxy = -1; selectedClip = null;
        hoveredEditTrack = new("shcr:1", "shcr", false, 1, "shcr");
        Check(TryPrGridTarget(out target) && target == 1, "SH track hover did not show its proxy");
        BeginTrackOrder(hoveredEditTrack);
        Check(newProxy == 1, "SH track label did not select its proxy"); CancelTrackOrder();
        newProxy = 0; Check(TryPrGridTarget(out target) && target == 0, "Picker did not override stale clip selection");
        SetClipSelection(clip.Id);
        Check(editor.Vsm.Text == sourceText, "Guide gestures modified the VSM");
        prGridHudExpanded = false;
        mouseX = previewImage.X + previewImage.W * .61f; mouseY = previewImage.Y + previewImage.H * .59f; Frame();
        Console.WriteLine("PASS proxy guide UI: SH selection, picker priority, grid steps, details, focus loss, hidden hit areas and unchanged scene/VSM");

        Check(prGridLabels.Count == 2, "Both visible proxies need an ID tag");
        void ClickTag(int p)
        {
            var label = prGridLabels.Single(l => l.Target == p);
            Click(label.Bounds.X + 15, label.Bounds.Y + 12);
            Check(newProxy == p && TryPrGridTarget(out int picked) && picked == p, "Proxy ID tag did not select its target");
        }
        ClickTag(0);
        Check(selectedClip == null, "Selecting P0 kept the P1 inspector active");
        ClickTag(1);
        Click(prGridHudRect.X + prGridHudRect.W - 50, prGridHudRect.Y + 46);
        Check(!prGridShowIds && prGridLabels.Count == 0, "Hidden IDs still own hit areas");
        Click(prGridHudRect.X + prGridHudRect.W - 50, prGridHudRect.Y + 46);
        Check(prGridShowIds && prGridLabels.Count == 2, "IDs did not return after toggling");
        Click(prGridHudRect.X + prGridHudRect.W - 92, prGridHudRect.Y + 12);
        Check(newProxy == 0, "Next proxy did not wrap from last to first");
        Click(prGridHudRect.X + prGridHudRect.W - 122, prGridHudRect.Y + 12);
        Check(newProxy == 1, "Previous proxy did not wrap from first to last");
        Check(editor.Vsm.Text == sourceText, "Proxy identity controls changed the chart");

        void IdentityScene(string mods)
        {
            Current = new Session(Current.Project.Copy(), editedVsm: "!obj:obj_custom_gimmick\n!proxies:4\n" + mods);
            editor = new EditorDocument(Current); renderedRevision = editor.Revision; layoutRevision = -1;
            SetClipSelection(null); newProxy = 0; prGridSelection = null; mouseX = mouseY = 0; Frame();
        }
        IdentityScene(Mod("pra", 1) + Mod("pra", 1, 1) + Mod("pra", 1, 2) + Mod("pra", 1, 3));
        Check(prGridLabels.Count == 4, "Overlapping proxies dropped their ID tags");
        for (int a = 0; a < prGridLabels.Count; a++)
        {
            Check(!Overlaps(prGridLabels[a].Bounds, prGridHudRect), "ID tag overlaps the HUD");
            for (int b = a + 1; b < prGridLabels.Count; b++)
                Check(!Overlaps(prGridLabels[a].Bounds, prGridLabels[b].Bounds), "Overlapping proxies have unreadable ID tags");
        }
        // Reproduce the reported ambiguity: P0 is offscreen while P1 is still rendered.
        // P2 is hidden and P3 fully masked, so neither should pretend to be visible.
        IdentityScene(Mod("pra", 1) + Mod("pry", 180) + Mod("pra", 1, 1) + Mod("prrz", 12, 1) +
            Mod("prx", 20, 1) + Mod("pra", 0, 2) + Mod("pra", 1, 3) + Mod("shcl", 1, 3));
        Check(TryPrGridTarget(out target) && target == 0 && prGridLabels.Count == 1 && prGridLabels[0].Target == 1,
            "Offscreen selection was confused with a different visible proxy");
        var identityScene = Canvas.Read(Renderer.Final); string identityVsm = editor.Vsm.Text;
        ClickTag(1);
        Check(identityScene.SequenceEqual(Canvas.Read(Renderer.Final)) && identityVsm == editor.Vsm.Text,
            "Selecting an ID altered the scene/export or document");
        SelectProxyFromGuide(0); prGridHudExpanded = false; Frame();
        var visibleLabel = prGridLabels.Single(l => l.Target == 1);
        mouseX = visibleLabel.Bounds.X + 15; mouseY = visibleLabel.Bounds.Y + 12; Frame();
        Console.WriteLine("PASS proxy identity: clickable IDs, inspector sync, crowded labels, offscreen selection, hidden/masked proxies, toggle, cycling and unchanged scene/export");

        // A full-width source crop is not the footprint of the lane artwork (Stage4a P4).
        var wide = ProxyGeometry.Sample(TimelineFor(Mod("prcl", 320)), 0, 0, true);
        var laneRegion = ProxyTrackRegion(wide);
        Near(laneRegion.X, 113, "Wide crop lane reference left"); Near(laneRegion.W, 95, "Wide crop must not frame the full screen as a lane");
        IdentityScene(Mod("pra", 1) + Mod("prx", -60) + Mod("prcl", 320) + Mod("pra", 1, 1) + Mod("prx", 60, 1));
        byte[] complete = Canvas.Read(Renderer.Final); string unchanged = editor.Vsm.Text;
        Click(prGridHudRect.X + 30, prGridHudRect.Y + 77);
        Check(prGridSolo && prGridSoloRendered, "Solo preview button did not activate");
        Check(prGridLabels.Count == 1 && prGridLabels[0].Target == 0, "Solo preview labels other proxies");
        byte[] isolated = Canvas.Gpu.Read(Renderer.ProxyInspectionTexture!);
        Check(!isolated.SequenceEqual(complete), "Solo preview still contains the entire composite");
        Check(complete.SequenceEqual(Canvas.Read(Renderer.Final)), "Solo preview overwrote Final/export");
        Frame(); Check(complete.SequenceEqual(Canvas.Read(Renderer.Final)), "Solo preview changed the next complete frame");
        SelectProxyFromGuide(2); Frame();
        Check(prGridLabels.Count == 0, "Hidden selected proxy still has a misleading visible tag");
        byte[] hiddenSolo = Canvas.Gpu.Read(Renderer.ProxyInspectionTexture!);
        Check(hiddenSolo.Where((_, index) => index % 4 != 3).All(c => c == 0), "Hidden proxy solo must be empty");
        Click(prGridHudRect.X + prGridHudRect.W - 50, prGridHudRect.Y + 77);
        Check(prGridShowCrop && prGridLabels.Count == 0, "Crop toggle resurrected hidden proxy geometry");
        Click(prGridHudRect.X + 30, prGridHudRect.Y + 77);
        Check(!prGridSolo && !prGridSoloRendered && complete.SequenceEqual(Canvas.Read(Renderer.Final)), "Leaving solo changed the normal preview");
        Check(editor.Vsm.Text == unchanged, "Solo/crop controls rewrote chart parameters");
        SelectProxyFromGuide(0); prGridShowCrop = false; Frame();
        Console.WriteLine("PASS proxy isolation: real source render, separate output, unchanged next frame/export/VSM, hidden proxy has no ghost frame, wide crop uses lane reference");
        SmokeProxyFooter();
        SmokeProxyPicking();
        SmokeProxyLabelUiMotion();
    }
}
