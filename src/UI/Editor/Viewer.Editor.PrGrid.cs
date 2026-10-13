using System.Numerics;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// PR family calibration for the chart's 320x180 logical coordinates.
/// Draws an overlay in editor pixels only: it is never included in video export or saved VSM.
/// Geometry and UV masks share the scene renderer's evaluated proxy pose.
/// </summary>
public sealed partial class Viewer
{
    (Guid Id, int Target)? prGridSelection;
    // Keep the overlay visibility rule shared between rendering and mouse hit testing.
    // A hidden floating panel must never steal clicks from image/text objects in the scene.
    bool TryPrGridTarget(out int target)
    {
        target = -1;
        if (editor == null || prGridMode == 2 || imageCanvas || textCanvas || desktopPreview) return false;
        var selected = selectedClip is Guid id ? EditorFindClip(id) : null;
        // Editing/undoing the target of the same clip must move its guide too. Merely
        // choosing another P in the picker does not change the clip and keeps precedence.
        if (selected != null)
        {
            if (prGridSelection is { } previous && previous.Id == selected.Id && previous.Target != selected.Proxy)
                newProxy = selected.Proxy;
            prGridSelection = (selected.Id, selected.Proxy);
        }
        else prGridSelection = null;
        bool selectedProxy = selected is { Proxy: >= 0 };
        bool hoveredProxy = hoveredEditTrack is { Window: false, Target: >= 0, ImageId: null, TextId: null };
        bool workflowProxy = workflow == "add" && addProxy >= 0;
        if (prGridMode == 0 && newProxy < 0 && !selectedProxy && !hoveredProxy && !workflowProxy) return false;
        // Explicit picker selection wins over stale hover state. Clip selection synchronizes newProxy.
        target = newProxy >= 0 ? newProxy :
            selectedProxy ? selected!.Proxy : workflowProxy ? addProxy :
            hoveredProxy ? hoveredEditTrack!.Target : 0;
        return true;
    }

    // Position is editor-only and measured relative to the actual 16:9 scene, not the outer
    // letterbox/frame. Default top-right; dragging remembers position until editor reload.
    float prGridHudOffsetX = float.NaN, prGridHudOffsetY = 8;
    float prGridHudGrabX, prGridHudGrabY;
    bool prGridHudDragging, prGridHudExpanded;
    Rect prGridHudRect;
    int prGridStep = 20;

    Rect PositionPrGridHud(Rect viewport)
    {
        float width = Math.Min(352, Math.Max(80, viewport.W - 16));
        float height = prGridHudExpanded ? 253 : PrGridHeaderHeight;
        if (!float.IsFinite(prGridHudOffsetX)) prGridHudOffsetX = Math.Max(8, viewport.W - width - 12);
        prGridHudOffsetX = Math.Clamp(prGridHudOffsetX, 0, Math.Max(0, viewport.W - width));
        prGridHudOffsetY = Math.Clamp(prGridHudOffsetY, 0, Math.Max(0, viewport.H - height));
        var hud = new Rect(viewport.X + prGridHudOffsetX, viewport.Y + prGridHudOffsetY, width, height);
        return prGridHudRect = hud;
    }

    void DrawPrGridHud(Rect viewport, int target, string status, ProxyGeometry? geometry, ProxyMask mask,
        string exitX = "", string exitY = "")
    {
        var hud = PositionPrGridHud(viewport);
        Canvas.Fill(hud, Theme.Panel.Alpha(.94f));
        Canvas.Border(hud, Theme.BorderStrong.Alpha(.85f));
        Text($"P{target}", hud.X + 8, hud.Y + 6, 22, Theme.AccentSoft, true, 56);
        Text(L.Get("CURRENT SELECTION"), hud.X + 66, hud.Y + 12, 11, Theme.Text, true, Math.Max(1, hud.W - 205));
        foreach (var (offset, label) in new[] { (135, "<"), (105, ">") })
        {
            var button = new Rect(hud.X + hud.W - offset, hud.Y + 4, 25, 24);
            Canvas.Border(button, Theme.BorderStrong);
            Text(label, button.X + 8, button.Y + 5, 12, Theme.AccentSoft, true, 18);
        }
        Text($"{prGridStep}px", hud.X + hud.W - 71, hud.Y + 12, 10, Theme.AccentSoft, true, 43);
        var toggle = new Rect(hud.X + hud.W - 27, hud.Y + 4, 26, 24);
        Text(prGridHudExpanded ? "[-]" : "[+]", toggle.X + 3, toggle.Y + 5, 10, Theme.AccentSoft, true, 24);
        Text(status, hud.X + 8, hud.Y + 40, 11, Theme.Text, true, Math.Max(1, hud.W - 90));
        var ids = new Rect(hud.X + hud.W - 77, hud.Y + 35, 70, 22);
        Canvas.Border(ids, prGridShowIds ? Theme.AccentSoft : Theme.BorderStrong);
        Text(L.Get(prGridShowIds ? "IDS: ON" : "IDS: OFF"), ids.X + 5, ids.Y + 5, 10, Theme.AccentSoft, true, ids.W - 10);
        float half = (hud.W - 21) / 2;
        var solo = new Rect(hud.X + 7, hud.Y + 65, half, 25);
        var crop = new Rect(hud.X + 14 + half, hud.Y + 65, half, 25);
        Canvas.Fill(solo, prGridSoloRendered ? Theme.AccentStrong.Alpha(.25) : Theme.PanelRaised);
        Canvas.Border(solo, prGridSoloRendered ? Theme.AccentSoft : Theme.BorderStrong);
        Text(prGridSoloRendered ? L.Get("SOLO") + $" P{target}" : L.Get("SHOW ALL PROXIES"), solo.X + 6, solo.Y + 6, 11, Theme.Text, true, solo.W - 12);
        Canvas.Border(crop, prGridShowCrop ? Theme.AccentSoft : Theme.BorderStrong);
        Text(L.Get(prGridShowCrop ? "CROP FRAME: ON" : "CROP FRAME: OFF"), crop.X + 6, crop.Y + 6, 10, Theme.Text, true, crop.W - 12);
        if (!prGridHudExpanded) return;
        Canvas.Line(hud.X + 1, hud.Y + PrGridHeaderHeight, hud.X + hud.W - 1, hud.Y + PrGridHeaderHeight, 1, Theme.BorderStrong.Alpha(.7f));
        float y = hud.Y + PrGridHeaderHeight + 6;
        void Row(string value, Color color) { Text(value, hud.X + 7, y, 10, color, true, hud.W - 14); y += 17; }
        Row(L.Get("Solid: lane reference / Dashed: source crop"), Theme.AccentSoft);
        if (geometry is not { } g) return;
        string reversed = g.Left > g.Right || g.Top > g.Bottom ? " [" + L.Get("REVERSED") + "]" : "";
        Row(L.Get("SOURCE CROP") + $" X {g.Left:0.#}..{g.Right:0.#} Y {g.Top:0.#}..{g.Bottom:0.#}" + reversed, Theme.Text);
        Row($"X {g.X:0.##}  Y {g.Y:0.##}  ROT {g.Angle:0.##}°  a={g.Alpha:0.##}", Theme.Text);
        Row($"SCALE X {g.ScaleX:0.###}  Y {g.ScaleY:0.###}" + (g.Projective ? " / " + L.Get("PERSPECTIVE") : ""), Theme.Text);
        Row($"shcl {mask.Left:0.###} ({320 * mask.Left:0.#})   shcr {mask.Right:0.###} ({320 * (1-mask.Right):0.#})", Theme.AccentStrong);
        Row($"shfl {mask.FadeLeft:0.###}   shfr {mask.FadeRight:0.###} / " + L.Get("UV fade"), Theme.AccentStrong);
        Row(exitX, Theme.Text);
        Row(exitY, Theme.Text);
        Row(L.Get("Double-click track / Shift: prefer proxy"), Theme.Muted);
    }

    bool HandlePrGridGesture(Sdl.Event e)
    {
        if (prGridHudDragging)
        {
            if (e.WindowID != 0 && e.WindowID != Sdl.SDL_GetWindowID(host.Window)) return false;
            if (e.Type == 0x400 || e.Type == 0x402 && e.Button == 1)
            {
                mouseX = e.X; mouseY = e.Y;
                float width = prGridHudRect.W, height = prGridHudRect.H;
                prGridHudOffsetX = Math.Clamp(e.X - prGridHudGrabX - previewImage.X, 0, Math.Max(0, previewImage.W - width));
                prGridHudOffsetY = Math.Clamp(e.Y - prGridHudGrabY - previewImage.Y, 0, Math.Max(0, previewImage.H - height));
                if (e.Type == 0x402)
                {
                    prGridHudDragging = false;
                    held = click = false;
                    Sdl.SDL_CaptureMouse(false);
                }
                return true;
            }
            if (e.Type == 0x300 && e.Scan == 41)
            { prGridHudDragging = false; held = click = false; Sdl.SDL_CaptureMouse(false); return true; }
            if (e.Type == 0x20F)
            { prGridHudDragging = false; held = click = false; Sdl.SDL_CaptureMouse(false); return false; }
            if (e.Type is 0x100 or Sdl.WindowCloseRequested)
            { prGridHudDragging = false; Sdl.SDL_CaptureMouse(false); return false; }
            return e.Type is >= 0x400 and <= 0x403;
        }
        if (e.WindowID != 0 && e.WindowID != Sdl.SDL_GetWindowID(host.Window)) return false;
        if (!editorMode || settings || UiOverlayVisible || Busy || !TryPrGridTarget(out int target) ||
            e.Type != 0x401 || e.Button != 1) return false;
        mouseX = e.X; mouseY = e.Y;
        if (!prGridHudRect.Contains(e.X, e.Y))
        {
            foreach (var label in prGridLabels)
                if (label.Bounds.Contains(e.X, e.Y))
                {
                    SelectProxyFromGuide(label.Target); click = held = false; return true;
                }
            return false;
        }
        float right = prGridHudRect.X + prGridHudRect.W;
        if (e.Y >= prGridHudRect.Y + 65 && e.Y < prGridHudRect.Y + 90)
        {
            if (e.X < prGridHudRect.X + prGridHudRect.W / 2)
            {
                if (SceneRenderer.CanInspectProxy(Current)) prGridSolo = !prGridSolo;
                else message = L.Get("Solo preview is available for Base and Custom proxies.");
            }
            else prGridShowCrop = !prGridShowCrop;
        }
        else if (e.Y >= prGridHudRect.Y + 35 && e.Y <= prGridHudRect.Y + 57 && e.X >= right - 77 && e.X <= right - 7)
        { prGridShowIds = !prGridShowIds; prGridLabels.Clear(); }
        else if (e.Y < prGridHudRect.Y + PrGridTitleHeight && e.X >= right - 27)
            prGridHudExpanded = !prGridHudExpanded;
        else if (e.Y < prGridHudRect.Y + PrGridTitleHeight && e.X >= right - 75)
            prGridStep = prGridStep == 20 ? 10 : prGridStep == 10 ? 5 : 20;
        else if (e.Y < prGridHudRect.Y + PrGridTitleHeight && e.X >= right - 105)
            StepProxyFromGuide(target, 1);
        else if (e.Y < prGridHudRect.Y + PrGridTitleHeight && e.X >= right - 135)
            StepProxyFromGuide(target, -1);
        else if (e.Y < prGridHudRect.Y + PrGridTitleHeight)
        {
            prGridHudDragging = true;
            prGridHudGrabX = e.X - prGridHudRect.X;
            prGridHudGrabY = e.Y - prGridHudRect.Y;
            Sdl.SDL_CaptureMouse(true);
        }
        click = held = false;
        return true;
    }

    void DrawPrCalibrationGrid(Rect viewport, double time)
    {
        prGridHudRect = default; prGridLabels.Clear(); proxyPickFrame = null;
        if (!TryPrGridTarget(out int target) || viewport.W < 80 || viewport.H < 80) return;
        proxyPickFrame = new(Current, viewport, time, prGridSoloRendered ? prGridSoloTarget : null);
        float scaleX = viewport.W / 320, scaleY = viewport.H / 180;
        Vector2 Pixel(Vector2 logical) => new(viewport.X + logical.X * scaleX, viewport.Y + logical.Y * scaleY);
        Vector2 Point(float x, float y) => Pixel(new(x, y));
        var grid = Theme.Grid.Alpha(.6f);
        var majorGrid = Theme.GridMajor.Alpha(.85f);
        Canvas.Clip(viewport);
        for (int x = 0; x <= 320; x += prGridStep)
        {
            var a = Point(x, 0); var b = Point(x, 180);
            Canvas.Line(a.X, a.Y, b.X, b.Y, x % 40 == 0 ? 1.2f : .6f, x % 40 == 0 ? majorGrid : grid);
        }
        for (int y = 0; y <= 180; y += prGridStep)
        {
            var a = Point(0, y); var b = Point(320, y);
            Canvas.Line(a.X, a.Y, b.X, b.Y, y % 40 == 0 ? 1.2f : .6f, y % 40 == 0 ? majorGrid : grid);
        }
        for (int x = 0; x < 320; x += 40) Text(x.ToString(), viewport.X + x * scaleX + 3, viewport.Y + 3, 10, Theme.Muted, true, 36);
        for (int y = 40; y < 180; y += 40) Text(y.ToString(), viewport.X + 3, viewport.Y + y * scaleY + 2, 10, Theme.Muted, true, 36);
        try
        {
            if (target < 0 || target >= Current.Chart.Proxies)
            {
                DrawPrGridHud(viewport, target, L.Get("PR GRID / waiting for preview rebuild"), null, default);
                return;
            }
            var g = ProxyGeometry.Sample(Current.Timeline, time, target, Current.Chart.ObjectName == "obj_custom_gimmick");
            var mask = ProxyMask.Sample(Current.Timeline, time, target);
            if (!g.Renderable)
            {
                PositionPrGridHud(viewport);
                DrawProxyIdentityTags(viewport, time, target);
                DrawPrGridHud(viewport, target, L.Get("PR geometry is not finite"), g, mask);
                return;
            }
            if (g.Alpha <= 0)
            {
                PositionPrGridHud(viewport);
                DrawProxyIdentityTags(viewport, time, target);
                DrawPrGridHud(viewport, target, L.Get("HIDDEN: pra=0"), g, mask);
                DrawProxyUnavailable(viewport, target, L.Get("This proxy is hidden and contributes no image."));
                return;
            }
            var source = ProxyTrackRegion(g, Current.Timeline.Get("shxa", time, target), Current.Timeline.Get("shya", time, target));
            if (source.W > 0 && source.H > 0)
                Renderer.DrawProxyGuides(g, mask, viewport, Theme.AccentSoft.Alpha(.025), Theme.AccentStrong.Alpha(.24), prGridShowCrop ? g.Source : source);

            void Line(Vector2 a, Vector2 b, Color color, float width, bool dashed = false)
            {
                float length = Vector2.Distance(a, b);
                if (!float.IsFinite(length) || length < .01f) return;
                if (!dashed) { Canvas.Line(a.X, a.Y, b.X, b.Y, width, color); return; }
                for (float d = 0; d < length; d += 10)
                {
                    var from = Vector2.Lerp(a, b, d / length); var to = Vector2.Lerp(a, b, Math.Min(d + 5, length) / length);
                    Canvas.Line(from.X, from.Y, to.X, to.Y, width, color);
                }
            }
            void ProjectedLine(Vector2 a, Vector2 b, Color color, float width = 1.5f, bool dashed = false)
            {
                if (g.ClipSegment(a, b, out var p, out var q)) Line(Pixel(p), Pixel(q), color, width, dashed);
            }
            // Clip the untransformed dashed frame to the field before drawing; never invent a border
            // along the viewport edge when the actual source crop extends beyond it.
            void SourceLine(Vector2 a, Vector2 b)
            {
                if (a.X == b.X)
                {
                    if (a.X < 0 || a.X > 320) return;
                    a.Y = Math.Clamp(a.Y, 0, 180); b.Y = Math.Clamp(b.Y, 0, 180);
                }
                else
                {
                    if (a.Y < 0 || a.Y > 180) return;
                    a.X = Math.Clamp(a.X, 0, 320); b.X = Math.Clamp(b.X, 0, 320);
                }
                Line(Pixel(a), Pixel(b), Theme.Muted, 1.2f, true);
            }
            Vector2[] corners = [new(g.Left, g.Top), new(g.Right, g.Top), new(g.Right, g.Bottom), new(g.Left, g.Bottom)];
            Vector2[] trackCorners = [new(source.X, source.Y), new(source.X + source.W, source.Y),
                new(source.X + source.W, source.Y + source.H), new(source.X, source.Y + source.H)];
            for (int i = 0; i < 4; i++)
            {
                if (prGridShowCrop)
                {
                    SourceLine(corners[i], corners[(i + 1) % 4]);
                    ProjectedLine(corners[i], corners[(i + 1) % 4], Theme.Muted.Alpha(.7), 1, true);
                }
                if (source.W > 0 && source.H > 0)
                    ProjectedLine(trackCorners[i], trackCorners[(i + 1) % 4], Theme.AccentSoft, 1.8f);
            }
            // Boundaries are in original full-field UV space, not percentages of the cropped width.
            void Boundary(float value, bool horizontal, string label, bool fade = false)
            {
                float low = horizontal ? source.Y : source.X, high = low + (horizontal ? source.H : source.W);
                if (!float.IsFinite(value) || value < low || value > high) return;
                Vector2 a = horizontal ? new(source.X, value) : new(value, source.Y);
                Vector2 b = horizontal ? new(source.X + source.W, value) : new(value, source.Y + source.H);
                ProjectedLine(a, b, Theme.AccentStrong.Alpha(fade ? .6 : .95), fade ? 1 : 1.7f, fade);
                if (!fade && g.ClipSegment(a, b, out var p, out var q))
                {
                    var at = Pixel(Vector2.Lerp(p, q, label is "shcr" or "shcb" ? .65f : .85f));
                    float labelX = Math.Clamp(at.X + 5, viewport.X + 3, Math.Max(viewport.X + 3, viewport.X + viewport.W - 140));
                    float labelY = Math.Clamp(at.Y + 3, viewport.Y + 16, viewport.Y + viewport.H - 18);
                    Text(label + $" {value:0.#}", labelX, labelY, 10, Theme.AccentStrong, true, 135);
                }
            }
            if (mask.Left != 0 || mask.FadeLeft > 0) Boundary(320 * mask.Left, false, "shcl");
            if (mask.Right != 0 || mask.FadeRight > 0) Boundary(320 * (1 - mask.Right), false, "shcr");
            if (mask.Top != 0 || mask.FadeTop > 0) Boundary(180 * mask.Top, true, "shct");
            if (mask.Bottom != 0 || mask.FadeBottom > 0) Boundary(180 * (1 - mask.Bottom), true, "shcb");
            if (mask.FadeLeft > 0) Boundary(320 * (mask.Left + mask.FadeLeft), false, "", true);
            if (mask.FadeRight > 0) Boundary(320 * (1 - mask.Right - mask.FadeRight), false, "", true);
            if (mask.FadeTop > 0) Boundary(180 * (mask.Top + mask.FadeTop), true, "", true);
            if (mask.FadeBottom > 0) Boundary(180 * (1 - mask.Bottom - mask.FadeBottom), true, "", true);

            // Local X/Y axes show rotation, scale and shear around the renderer's actual pivot.
            ProjectedLine(g.Origin, g.Origin + new Vector2(15, 0), Theme.AccentSoft, 2);
            ProjectedLine(g.Origin, g.Origin + new Vector2(0, 15), Theme.AccentStrong, 2);
            if (g.TryProject(g.Origin, out var pivot) && pivot.X >= 0 && pivot.X <= 320 && pivot.Y >= 0 && pivot.Y <= 180)
            {
                var p = Pixel(pivot); Canvas.Border(new(p.X - 3, p.Y - 3, 6, 6), Theme.Text);
            }
            var projected = new Vector2[4];
            bool allInFront = true;
            for (int i = 0; i < 4; i++) allInFront &= g.TryProject(corners[i], out projected[i]);
            Span<Vector2> visibleRegion = stackalloc Vector2[9];
            bool outside = g.ClipRegion(g.Source, visibleRegion) == 0;
            bool covered = Math.Max(source.X, 320 * mask.Left) >= Math.Min(source.X + source.W, 320 * (1 - mask.Right)) ||
                Math.Max(source.Y, 180 * mask.Top) >= Math.Min(source.Y + source.H, 180 * (1 - mask.Bottom));
            string status = covered ? L.Get("LANE FULLY COVERED") : outside ? L.Get("SELECTED PROXY OFFSCREEN") :
                prGridSoloRendered ? L.Get("Only this proxy is shown") : L.Get("Lane reference / all proxies shown");
            string exitX = L.Get("Projection crosses camera plane"), exitY = "";
            if (allInFront)
            {
                double x = Current.Timeline.Get("prx", time, target), y = Current.Timeline.Get("pry", time, target);
                exitX = $"prx <= {x - projected.Max(p => p.X):0.#} / >= {x + 320 - projected.Min(p => p.X):0.#}";
                exitY = $"pry <= {y - projected.Max(p => p.Y):0.#} / >= {y + 180 - projected.Min(p => p.Y):0.#}";
            }
            PositionPrGridHud(viewport);
            DrawProxyIdentityTags(viewport, time, target);
            DrawPrGridHud(viewport, target, status, g, mask, exitX, exitY);
            if (viewport.Contains(mouseX, mouseY) && !prGridHudRect.Contains(mouseX, mouseY) && !prGridHudDragging &&
                !prGridLabels.Any(label => label.Bounds.Contains(mouseX, mouseY)))
            {
                var point = new Vector2((mouseX - viewport.X) / scaleX, (mouseY - viewport.Y) / scaleY);
                string readout = $"X {point.X:0.##}  Y {point.Y:0.##}";
                if (g.TryUnproject(point, out var local))
                {
                    var delta = local - g.Origin;
                    readout += $"   SRC {local.X:0.##}, {local.Y:0.##}   Δ {delta.X:0.##}, {delta.Y:0.##}";
                }
                Canvas.Fill(new(viewport.X + 4, viewport.Y + viewport.H - 23, viewport.W - 8, 20), Theme.Panel.Alpha(.92));
                Text(readout, viewport.X + 9, viewport.Y + viewport.H - 19, 10, Theme.Text, true, viewport.W - 18);
                Canvas.Line(mouseX - 4, mouseY, mouseX + 4, mouseY, 1, Theme.Text);
                Canvas.Line(mouseX, mouseY - 4, mouseX, mouseY + 4, 1, Theme.Text);
            }
        }
        finally { Canvas.Clip(null); }
    }
}
