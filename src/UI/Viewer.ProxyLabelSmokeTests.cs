using System.Numerics;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    const string ProxyLabelRotationVsm = """
        !obj:obj_custom_gimmick
        !proxies:1
        0,0,linear,1,1,pra,0
        24,2,inOutSine,0,25,prrz,0
        26,2,inOutSine,25,0,prrz,0
        28,2,inOutSine,0,-25,prrzb,0
        30,2,inOutSine,-25,0,prrzb,0
        """;

    // Proxy test B24..32: the top edge repeatedly crosses the viewport while rotating.
    // Single screenshots missed the label alternating between its two clipped endpoints.
    void SmokeProxyLabelMotion()
    {
        var chart = new Chart();
        VsmReader.ReplaceModsText(chart, ProxyLabelRotationVsm, "proxy-label-motion.vsm");
        var timeline = new Timeline(chart, new ViewerProject { Bpm = 120 }, 2);
        var samples = new Vector2[961];
        Span<Vector2> polygon = stackalloc Vector2[9];
        float largestStep = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double time = 12 + i / 240.0;
            var geometry = ProxyGeometry.Sample(timeline, time, 0, true);
            var source = ProxyTrackRegion(geometry);
            int count = geometry.ClipRegion(source, polygon);
            if (count == 0) throw new InvalidOperationException($"Visible rotating proxy lost its label at {time:F5}s.");
            samples[i] = ProxyIdentityAnchor(geometry, source, polygon[..count]);
            if (i == 0) continue;
            float distance = Vector2.Distance(samples[i - 1], samples[i]);
            largestStep = Math.Max(largestStep, distance);
            if (distance > 2)
                throw new InvalidOperationException($"Proxy label jumped {distance:F3} logical pixels at {time:F5}s: {samples[i - 1]} -> {samples[i]}");
        }
        for (int i = samples.Length - 1; i >= 0; i--)
        {
            var geometry = ProxyGeometry.Sample(timeline, 12 + i / 240.0, 0, true);
            var source = ProxyTrackRegion(geometry);
            int count = geometry.ClipRegion(source, polygon);
            var anchor = ProxyIdentityAnchor(geometry, source, polygon[..count]);
            if (Vector2.Distance(samples[i], anchor) > .001f)
                throw new InvalidOperationException("Reverse seek changed proxy label placement.");
        }
        Console.WriteLine($"PASS proxy label motion: 961 consecutive rotation frames + reverse seeks; maximum anchor step {largestStep:F3} logical pixels");
    }

    void SmokeProxyLabelUiMotion()
    {
        Current = new Session(new ViewerProject { Bpm = 120, Profile = "core", RoomPreset = "none", GameUiEnabled = false },
            editedVsm: ProxyLabelRotationVsm);
        editor = new EditorDocument(Current); renderedRevision = editor.Revision; layoutRevision = -1;
        SetClipSelection(null); newProxy = 0; prGridSelection = null;
        prGridMode = 1; prGridShowIds = true;
        prGridSolo = prGridShowCrop = prGridHudExpanded = false;
        prGridHudOffsetX = float.NaN; prGridHudOffsetY = 8;
        mouseX = mouseY = 0; transport.SetPlaying(false); transport.SetDuration(18);
        string original = editor.Vsm.Text;
        var positions = new Vector2[61]; float largestStep = 0;
        // Draw real UI frames across both screenshot times (13.41 and 13.45), at a wide
        // preview size so collisions with the parameter panel cannot mask anchor jitter.
        for (int i = 0; i < positions.Length; i++)
        {
            SetTime(13.2 + i / 120.0); Draw(2400, 1400);
            if (prGridLabels.Count != 1 || prGridLabels[0].Target != 0)
                throw new InvalidOperationException("P0 label flashed off during continuous rotation.");
            var rect = prGridLabels[0].Bounds; positions[i] = new(rect.X, rect.Y);
            if (i == 0) continue;
            float step = Vector2.Distance(positions[i], positions[i - 1]); largestStep = Math.Max(step, largestStep);
            if (step > 8) throw new InvalidOperationException($"P0 UI label jumped {step:F2} pixels in one frame.");
        }
        for (int i = positions.Length - 1; i >= 0; i--)
        {
            SetTime(13.2 + i / 120.0); Draw(2400, 1400);
            var rect = prGridLabels.Single(l => l.Target == 0).Bounds;
            if (Vector2.Distance(positions[i], new(rect.X, rect.Y)) > .01)
                throw new InvalidOperationException("Proxy label UI position depends on playback direction.");
        }
        if (editor.Vsm.Text != original) throw new InvalidOperationException("Label animation changed the VSM.");
        Console.WriteLine($"PASS proxy label UI: 122 rendered frames around 13.41/13.45s, no missing labels; maximum step {largestStep:F2} UI pixels; VSM unchanged");
    }
}
