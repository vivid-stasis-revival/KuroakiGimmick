using System.Numerics;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// 文本画布：在 320x180 逻辑空间里直接拖动文本，以及缩放/旋转手势。
/// 与 image 画布不同，这里的缩放以视口中心为锚点、没有平移视图的概念，所以只需要一个 textZoom。
/// </summary>
public sealed partial class Viewer
{
    /// <summary>
    /// 画一帧文本画布并就地做命中判定。scale = 视口宽 / 320 * textZoom，即逻辑单位到 UI 像素的换算比。
    /// 顶部 24px 是说明条不参与命中；播放中不接受拖拽。
    /// 具名文本的 X/Y 是 textX 与 textXb 两项相加（b 是附加偏移），legacy 文本没有 b 项，不要一并去取。
    /// </summary>
    void DrawTextCanvas(Rect viewport, double time)
    {
        EnsureTexts();
        if (editor == null || textSampler == null) return;
        float scale = viewport.W / 320 * textZoom;
        var origin = new Vector2(viewport.X + viewport.W / 2 - 160 * scale, viewport.Y + viewport.H / 2 - 90 * scale);
        var projection = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(origin);
        Canvas.Clip(viewport); Canvas.Fill(viewport, Theme.PanelAlt);
        for (int x = 0; x <= 320; x += 20) Canvas.Line(origin.X + x * scale, origin.Y, origin.X + x * scale, origin.Y + 180 * scale, 1, line);
        for (int y = 0; y <= 180; y += 20) Canvas.Line(origin.X, origin.Y + y * scale, origin.X + 320 * scale, origin.Y + y * scale, 1, line);
        double beat = Current.Timeline.Bpm.Beat(time);
        double Value(string name) => textDragValues?.GetValueOrDefault(name, textSampler.Get(name, beat)) ?? textSampler.Get(name, beat);
        textBounds.Clear();
        Renderer.DrawAuthoringTexts(Current, time, projection, Value, selectedTextId, (id, corners) =>
        { if (corners.All(p => float.IsFinite(p.X + p.Y))) textBounds[id] = corners; });
        Vector2 anchor = default, rotation = default;
        if (selectedTextId is { } selected && textBounds.TryGetValue(selected, out var box))
        {
            for (int i = 0; i < 4; i++) Canvas.Line(box[i].X, box[i].Y, box[(i + 1) % 4].X, box[(i + 1) % 4].Y, 1.5f, soft);
            foreach (var p in box) Canvas.Fill(new(p.X - 4, p.Y - 4, 8, 8), white);
            rotation = (box[0] + box[1]) / 2;
            var direction = rotation - (box[2] + box[3]) / 2;
            if (direction.LengthSquared() > .001f) rotation += Vector2.Normalize(direction) * 24;
            Canvas.Line(((box[0] + box[1]) / 2).X, ((box[0] + box[1]) / 2).Y, rotation.X, rotation.Y, 1.5f, soft);
            Canvas.Fill(new(rotation.X - 5, rotation.Y - 5, 10, 10), soft);
            string xName = TextValueSampler.Name("textX", selected), yName = TextValueSampler.Name("textY", selected);
            anchor = Vector2.Transform(new((float)(Value(xName) + (selected.Length == 0 ? 0 : Value(xName + "b"))),
                (float)(Value(yName) + (selected.Length == 0 ? 0 : Value(yName + "b")))), projection);
        }
        Text(L.Get("TEXT / LOCAL 320 x 180 / Ctrl+wheel zoom"), viewport.X + 8, viewport.Y + 7, 10, muted, max: viewport.W - 16);
        Canvas.Clip(null);
        if (!click || !viewport.Contains(mouseX, mouseY) || mouseY < viewport.Y + 24 || Busy || UiOverlayVisible || ImageGestureActive || transport.Playing) return;
        var pointer = new Vector2(mouseX, mouseY);
        int kind = 0;
        if (selectedTextId is { } id && textBounds.TryGetValue(id, out var selectedBox))
        {
            if (Vector2.Distance(pointer, rotation) < 12) kind = 2;
            else if (selectedBox.Any(p => Vector2.Distance(p, pointer) < 12)) kind = 1;
        }
        // 凸四边形命中：四条边的叉积同号即在内部。同时接受全为正或全为负，这样顺逆时针两种绕向都成立。
        bool Hit(Vector2[] corners)
        {
            var edges = Enumerable.Range(0, 4).Select(i =>
            { var a = corners[(i + 1) % 4] - corners[i]; var b = pointer - corners[i]; return a.X * b.Y - a.Y * b.X; }).ToArray();
            return edges.All(n => n >= 0) || edges.All(n => n <= 0);
        }
        string? pick = kind != 0 ? selectedTextId : textBounds.Reverse().Where(p => Hit(p.Value)).Select(p => p.Key).FirstOrDefault();
        if (pick == null) return;
        if (selectedTextId != pick || !textInspector) { SelectText(pick); click = false; return; }
        if (Math.Abs(beat - textAt) > 1e-6) { transport.Seek(Current.Timeline.Bpm.Time(textAt)); message = L.Get("Located text target; drag again to edit."); click = false; return; }
        textDrag = new(editor, editor.Revision, pick, pointer, anchor, scale, kind,
            textSampler.Get("textX", pick, textAt), textSampler.Get("textY", pick, textAt), textSampler.Get("textscale", pick, textAt), textSampler.Get("textrot", pick, textAt));
        textDragValues = null; click = false; Sdl.SDL_CaptureMouse(true);
    }
    /// <summary>
    /// 手势过程中只更新 textDragValues 这份预览覆盖值，不写文档；屏幕位移先除 CanvasScale 换回逻辑单位。
    /// Kind 0 移动、1 按到锚点的距离比缩放（钳在 0.001~1000，用 CopySign 保住负缩放即镜像）、2 旋转。
    /// 旋转是减角度而不是加：源里的 textrot 方向与屏幕 atan2 相反。按住 Shift（掩码 3）吸附 15°。
    /// </summary>
    void UpdateTextDrag(Vector2 pointer)
    {
        if (textDrag is not { } drag) return;
        var delta = (pointer - drag.Pointer) / drag.CanvasScale;
        textDragValues = new(StringComparer.Ordinal);
        void Put(string kind, double value) => textDragValues[TextValueSampler.Name(kind, drag.Id)] = value;
        if (drag.Kind == 0) { Put("textX", drag.X + delta.X); Put("textY", drag.Y + delta.Y); }
        else if (drag.Kind == 1)
            Put("textscale", Math.CopySign(Math.Clamp(Math.Abs(drag.Scale) * Vector2.Distance(pointer, drag.Anchor) / Math.Max(1, Vector2.Distance(drag.Pointer, drag.Anchor)), .001, 1000), drag.Scale));
        else
        {
            double angle = Math.Atan2(pointer.Y - drag.Anchor.Y, pointer.X - drag.Anchor.X) - Math.Atan2(drag.Pointer.Y - drag.Anchor.Y, drag.Pointer.X - drag.Anchor.X);
            double degrees = drag.Rotation - angle * 180 / Math.PI;
            if ((Sdl.SDL_GetModState() & 3) != 0) degrees = Math.Round(degrees / 15) * 15;
            Put("textrot", degrees);
        }
    }
    /// <summary>
    /// 文本手势的事件入口，兼管画布上的缩放与方向键微调。
    /// 拖拽中丢焦点（0x20F）或按 Esc（scancode 41）取消，退出类事件也取消但继续下传以便正常关闭；
    /// 期间返回 true 吞掉全部键鼠事件。松手时要求文档未变且位移超过 2 个 UI 像素才提交，避免点一下就写一次。
    /// Ctrl/Cmd（0x0CC0）+ 滚轮缩放钳在 0.25~8；方向键 79/80 改 textX、81/82 改 textY，
    /// 每次 1 个逻辑单位、按住 Shift 为 10，Y 轴向下为正。
    /// </summary>
    bool HandleTextGesture(Sdl.Event e)
    {
        if (textDrag is { } drag)
        {
            if (e.WindowID != 0 && e.WindowID != Sdl.SDL_GetWindowID(host.Window)) return false;
            if (e.Type is 0x20F or 0x100 or Sdl.WindowCloseRequested || e.Type == 0x300 && e.Scan == 41)
            { textDrag = null; textDragValues = null; held = click = false; Sdl.SDL_CaptureMouse(false); return e.Type is not (0x100 or Sdl.WindowCloseRequested); }
            if (e.Type == 0x400) { mouseX = e.X; mouseY = e.Y; UpdateTextDrag(new(e.X, e.Y)); return true; }
            if (e.Type == 0x402 && e.Button == 1)
            {
                UpdateTextDrag(new(e.X, e.Y)); var values = textDragValues; textDrag = null; textDragValues = null;
                if (editor == drag.Owner && editor.Revision == drag.Revision && Vector2.Distance(drag.Pointer, new(e.X, e.Y)) > 2 && values != null)
                    TextAction(() => CommitTextValues(values.ToDictionary(x => { Core.CustomText.TryMod(x.Key, out var kind, out _); return kind; }, x => x.Value)));
                held = click = false; Sdl.SDL_CaptureMouse(false); return true;
            }
            return e.Type is >= 0x300 and <= 0x403;
        }
        if (!editorMode || !textCanvas || UiOverlayVisible || Busy || !previewFrame.Contains(mouseX, mouseY)) return false;
        if (e.Type == 0x403 && (Sdl.SDL_GetModState() & 0x0CC0) != 0)
        { textZoom = Math.Clamp(textZoom * MathF.Pow(1.15f, e.WheelY), .25f, 8); return true; }
        if (e.Type == 0x300 && e.Scan is >= 79 and <= 82 && !transport.Playing && selectedTextId != null)
        {
            EnsureTexts(); if (textSampler == null) return true;
            double step = (e.Modifiers & 3) != 0 ? 10 : 1;
            string kind = e.Scan is 79 or 80 ? "textX" : "textY";
            double value = textSampler.Get(kind, selectedTextId, textAt) + (e.Scan is 80 or 82 ? -step : step);
            TextAction(() => CommitTextValues(new Dictionary<string, double> { [kind] = value })); return true;
        }
        return false;
    }
}
