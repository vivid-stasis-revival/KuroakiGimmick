using System.Numerics;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// PR family calibration for the chart's 320x180 logical coordinates.
/// Draws an overlay in editor pixels only: it is never included in video export or saved VSM.
/// Geometry intentionally mirrors SceneRenderer.Playfield's proxy transform; changing one requires reviewing the other.
/// </summary>
public sealed partial class Viewer
{
    // Keep the overlay visibility rule shared between rendering and mouse hit testing.
    // A hidden floating panel must never steal clicks from image/text objects in the scene.
    bool TryPrGridTarget(out int target)
    {
        target = -1;
        if (editor == null || prGridMode == 2 || imageCanvas || textCanvas || desktopPreview) return false;
        var selected = selectedClip is Guid id ? editor.Vsm.Clips.FirstOrDefault(c => c.Id == id) : null;
        bool selectedPr = selected?.Name.StartsWith("pr", StringComparison.OrdinalIgnoreCase) == true;
        bool hoveredPr = hoveredEditTrack is { Window: false, Target: >= 0 } track &&
            track.Property.StartsWith("pr", StringComparison.OrdinalIgnoreCase);
        bool workflowPr = workflow == "add" && addName.StartsWith("pr", StringComparison.OrdinalIgnoreCase);
        if (prGridMode == 0 && newProxy < 0 && !selectedPr && !hoveredPr && !workflowPr) return false;
        // An explicit selection from the Proxy picker always wins over stale hover/clip state.
        // Otherwise the left panel may say P0 while the floating grid silently shows P4.
        target = newProxy >= 0 ? newProxy :
            selectedPr && selected!.Proxy >= 0 ? selected.Proxy :
            workflowPr && addProxy >= 0 ? addProxy :
            hoveredPr && hoveredEditTrack!.Target >= 0 ? hoveredEditTrack.Target : 0;
        return true;
    }

    // Position is editor-only and measured relative to the actual 16:9 scene, not the outer
    // letterbox/frame. Default top-right; dragging remembers position until editor reload.
    float prGridHudOffsetX = float.NaN, prGridHudOffsetY = 8;
    float prGridHudGrabX, prGridHudGrabY;
    bool prGridHudDragging, prGridHudExpanded;
    Rect prGridHudRect;

    void DrawPrGridHud(Rect viewport, int target, double alpha, string status,
        double left, double right, double top, double bottom,
        double xLeft, double xRight, double yTop, double yBottom, bool approximate)
    {
        float width = Math.Min(320, Math.Max(80, viewport.W - 16));
        float height = prGridHudExpanded ? 79 : 26;
        if (!float.IsFinite(prGridHudOffsetX)) prGridHudOffsetX = Math.Max(8, viewport.W - width - 12);
        float maxX = Math.Max(0, viewport.W - width);
        float maxY = Math.Max(0, viewport.H - height);
        prGridHudOffsetX = Math.Clamp(prGridHudOffsetX, 0, maxX);
        prGridHudOffsetY = Math.Clamp(prGridHudOffsetY, 0, maxY);
        var hud = new Rect(viewport.X + prGridHudOffsetX, viewport.Y + prGridHudOffsetY, width, height);
        prGridHudRect = hud;
        // Keep this UI decoration small even for a white full-screen custom proxy mask.
        Canvas.Fill(hud, Theme.Panel.Alpha(.88f));
        Canvas.Border(hud, Theme.BorderStrong.Alpha(.85f));
        Canvas.Fill(new(hud.X, hud.Y, hud.W, 25), Theme.Panel.Alpha(.94f));
        string summary = $"P{target}/{Current.Chart.Proxies}   a={alpha:0.##}   {status}";
        Text(summary, hud.X + 7, hud.Y + 7, 10, Theme.Text, true, Math.Max(1, hud.W - 40));
        var toggle = new Rect(hud.X + hud.W - 27, hud.Y + 1, 26, 23);
        Canvas.Line(toggle.X, toggle.Y + 3, toggle.X, toggle.Y + 20, 1, Theme.BorderStrong);
        Text(prGridHudExpanded ? "[-]" : "[+]", toggle.X + 3, toggle.Y + 6, 9, Theme.AccentSoft, true, 24);
        if (!prGridHudExpanded) return;
        Canvas.Line(hud.X + 1, hud.Y + 25, hud.X + hud.W - 1, hud.Y + 25, 1, Theme.BorderStrong.Alpha(.7f));
        string reversed = left > right || top > bottom ? "  [" + L.Get("REVERSED") + "]" : "";
        Text(L.Get("SOURCE CROP") + $" X {left:0.#}..{right:0.#} Y {top:0.#}..{bottom:0.#}" + reversed,
            hud.X + 7, hud.Y + 29, 10, Theme.AccentSoft, true, hud.W - 14);
        Text($"prx <= {xLeft:0.#} / >= {xRight:0.#}" + (approximate ? " ~" : ""),
            hud.X + 7, hud.Y + 46, 10, Theme.Text, true, hud.W - 14);
        Text($"pry <= {yTop:0.#} / >= {yBottom:0.#}" + (approximate ? " ~" : ""),
            hud.X + 7, hud.Y + 63, 10, Theme.Text, true, hud.W - 14);
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
            if (e.Type is 0x100 or Sdl.WindowCloseRequested)
            { prGridHudDragging = false; Sdl.SDL_CaptureMouse(false); return false; }
            return e.Type is >= 0x400 and <= 0x403;
        }
        if (e.WindowID != 0 && e.WindowID != Sdl.SDL_GetWindowID(host.Window)) return false;
        if (!editorMode || settings || UiOverlayVisible || Busy || !TryPrGridTarget(out _) ||
            e.Type != 0x401 || e.Button != 1 || !prGridHudRect.Contains(e.X, e.Y)) return false;
        mouseX = e.X; mouseY = e.Y;
        if (e.X >= prGridHudRect.X + prGridHudRect.W - 27)
            prGridHudExpanded = !prGridHudExpanded;
        else
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
        prGridHudRect = default;
        if (!TryPrGridTarget(out int target) || viewport.W < 80 || viewport.H < 80) return;
        float scaleX = viewport.W / 320, scaleY = viewport.H / 180;
        Vector2 Pixel(Vector2 logical) => new(viewport.X + logical.X * scaleX, viewport.Y + logical.Y * scaleY);
        Vector2 Point(float x, float y) => Pixel(new(x, y));
        // Use the active UI palette, just like the timeline and image canvas. No fixed cyan/yellow in Kuroaki/Scarlet themes.
        var grid = Theme.Grid.Alpha(.65f);
        var majorGrid = Theme.GridMajor.Alpha(.85f);
        var boundary = Theme.BorderStrong.Alpha(.9f);
        var rail = Theme.AccentSoft;
        Canvas.Clip(viewport);
        for (int x = 0; x <= 320; x += 20)
        {
            Vector2 a = Point(x, 0), b = Point(x, 180);
            Canvas.Line(a.X, a.Y, b.X, b.Y, x % 80 == 0 ? 1.3f : .7f, x % 80 == 0 ? majorGrid : grid);
        }
        for (int y = 0; y <= 180; y += 20)
        {
            Vector2 a = Point(0, y), b = Point(320, y);
            Canvas.Line(a.X, a.Y, b.X, b.Y, y % 40 == 0 ? 1.3f : .7f, y % 40 == 0 ? majorGrid : grid);
        }
        // Original untransformed playfield rectangle (not a claim about HUD boundaries).
        var p0 = Point(113, 0); var p1 = Point(208, 165);
        Canvas.Border(new(p0.X, p0.Y, p1.X - p0.X, p1.Y - p0.Y), boundary);
        Canvas.Line(Point(160, 0).X, viewport.Y, Point(160, 0).X, viewport.Y + viewport.H, 1.6f, boundary.Alpha(.5));
        Canvas.Line(viewport.X, Point(0, 90).Y, viewport.X + viewport.W, Point(0, 90).Y, 1.6f, boundary.Alpha(.5));
        for (int x = 0; x <= 320; x += 80) Text(x.ToString(), viewport.X + x * scaleX + 3, viewport.Y + 3, 10, Theme.Muted, true, 42);
        for (int y = 40; y < 180; y += 40) Text(y.ToString(), viewport.X + 3, viewport.Y + y * scaleY + 2, 10, Theme.Muted, true, 36);

        // There can be a delay of one preview build between changing !proxies and receiving the new Session.
        if (target < 0 || target >= Current.Chart.Proxies)
        {
            DrawPrGridHud(viewport, target, 0, L.Get("PR GRID / waiting for preview rebuild"),
                0, 0, 0, 0, 0, 0, 0, 0, false);
            Canvas.Clip(null);
            return;
        }
        double M(string name) => Current.Timeline.Get(name, time, target);
        double horizontal = M("prx") + M("prxb") + M("prxc") + M("prxd");
        double vertical = M("pry") + M("pryb") + M("pryc") + M("pryd");
        double angle = (M("prrz") + M("prrzb")) * Current.Timeline.Get("rotdir", time);
        double scale = M("przm") * M("przmb") * M("przmc");
        double sx = scale * M("przx") * Math.Cos(M("prrx") * Math.PI / 180);
        double sy = scale * M("przy") * Math.Cos(M("prry") * Math.PI / 180);
        bool custom = Current.Chart.ObjectName == "obj_custom_gimmick";
        double left = 113 - M("shxa"), right = 208 + M("shxa"), top = 0, bottom = 165;
        if (M("prct") != .08 || M("prcb") != 0) { top = 180 * M("prcb") - 1; bottom = 180 - Math.Ceiling(180 * M("prct")); }
        if (M("prcl") != .35 || M("prcr") != .35) { left = M("prcl"); right = M("prcr"); }
        // Each PR proxy samples its OWN source rectangle from the shared field. Show that source crop as
        // a separate outline so prcl/prcr/prct/prcb-driven splits can be inspected before transformation.
        if (double.IsFinite(left) && double.IsFinite(right) && double.IsFinite(top) && double.IsFinite(bottom) &&
            Math.Abs(left) < 100000 && Math.Abs(right) < 100000 && Math.Abs(top) < 100000 && Math.Abs(bottom) < 100000)
        {
            float srcLeft = (float)Math.Min(left, right), srcRight = (float)Math.Max(left, right);
            float srcTop = (float)Math.Min(top, bottom), srcBottom = (float)Math.Max(top, bottom);
            // The renderer's final Polygon still owns signed crop bounds (mirrors are legitimate).
            var a = Point(srcLeft, srcTop); var b = Point(srcRight, srcBottom);
            if (b.X > a.X && b.Y > a.Y) Canvas.Border(new(a.X, a.Y, b.X - a.X, b.Y - a.Y), Theme.AccentSoft.Alpha(.85f));
        }
        // Exact affine path. Custom prtrX/Y uses a projective shader; in that case the outline is only an approximation.
        var matrix = Matrix3x2.CreateTranslation(custom ? (float)-(160 - M("shxa")) : -160, -82) *
            Matrix3x2.CreateScale((float)sx, (float)sy) *
            Matrix3x2.CreateRotation((float)((custom ? angle : -angle) * Math.PI / 180)) *
            new Matrix3x2(1, (float)-M("prsy"), (float)-M("prsx"), 1, 0, 0) *
            Matrix3x2.CreateTranslation(160 + (float)horizontal, 82 + (float)vertical);
        Vector2[] vertices = [Vector2.Transform(new((float)left, (float)top), matrix),
            Vector2.Transform(new((float)right, (float)top), matrix),
            Vector2.Transform(new((float)right, (float)bottom), matrix),
            Vector2.Transform(new((float)left, (float)bottom), matrix)];
        bool valid = vertices.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y));
        bool approximate = custom && (M("prtrX") != 0 || M("prtrY") != 0);
        if (valid)
        {
            float minX = vertices.Min(v => v.X), maxX = vertices.Max(v => v.X), minY = vertices.Min(v => v.Y), maxY = vertices.Max(v => v.Y);
            bool outside = maxX <= 0 || minX >= 320 || maxY <= 0 || minY >= 180;
            var outline = outside ? Theme.AccentStrong : Theme.AccentSoft;
            if (vertices.All(v => Math.Abs(v.X) < 1e6 && Math.Abs(v.Y) < 1e6))
                for (int i = 0; i < 4; i++)
                {
                    var a = Pixel(vertices[i]); var b = Pixel(vertices[(i + 1) % 4]);
                    Canvas.Line(a.X, a.Y, b.X, b.Y, 2.2f, outline);
                }
            // Translating prx/pry shifts every vertex by the same amount. These are effective complete-exit
            // thresholds at the playhead for the *other* PR parameters currently evaluated, not constants.
            double xLeft = M("prx") - maxX, xRight = M("prx") + 320 - minX;
            double yTop = M("pry") - maxY, yBottom = M("pry") + 180 - minY;
            // The canvas always displays the FULL multi-proxy composite; hidden/offscreen
            // applies to this selected proxy only, not the other visible copies.
            string visibility = M("pra") <= 0 ? L.Get("SELECTED PROXY HIDDEN") : outside ? L.Get("SELECTED PROXY OFFSCREEN") : L.Get("SELECTED PROXY OUTLINE");
            DrawPrGridHud(viewport, target, M("pra"), visibility + (approximate ? " ~" : ""),
                left, right, top, bottom, xLeft, xRight, yTop, yBottom, approximate);
        }
        else Text(L.Get("PR geometry is not finite"), viewport.X + 10, viewport.Y + viewport.H - 24, 11, rail, true);
        Canvas.Clip(null);
    }
}
