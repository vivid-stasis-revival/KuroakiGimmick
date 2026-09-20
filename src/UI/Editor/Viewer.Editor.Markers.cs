using KuroakiGimmick.Graphics;
namespace KuroakiGimmick.UI;

/// <summary>时间轴标记（目标点）的绘制与点选。标记决定 InsertionBeat，即新事件的插入位置。</summary>
public sealed partial class Viewer
{
    /// <summary>
    /// 画出标记菱形并处理点击。点中会停止播放并把播放头跳到该拍换算出的秒数，同时清掉其他选择，
    /// 保证插入目标唯一；双击改标签。interactive 为 false 时只绘制，供只读缩略轨复用。
    /// </summary>
    void DrawTimelineMarkers(Rect rect, bool interactive)
    {
        if (editor == null) return;
        Canvas.Clip(rect);
        Canvas.Fill(rect, Color.Hex(0x18232F));
        foreach (var marker in editor.Markers.OrderBy(m => m.Beat).ToArray())
        {
            float x = BeatX(marker.Beat);
            // 超出可视范围的标记跳过；留 8 像素余量，免得菱形在边界处被整块裁掉。
            if (x < rect.X - 8 || x > rect.X + rect.W + 8) continue;
            var hit = new Rect(x - 9, rect.Y, 18, rect.H);
            var color = marker.Id == activeMarker ? Color.Hex(0xFFD27D) : Color.Hex(0x7CDBED);
            float y = rect.Y + 12;
            Canvas.Line(x, y - 7, x + 6, y, 2, color); Canvas.Line(x + 6, y, x, y + 7, 2, color);
            Canvas.Line(x, y + 7, x - 6, y, 2, color); Canvas.Line(x - 6, y, x, y - 7, 2, color);
            if (marker.Id == activeMarker) Text(marker.Label, x + 12, rect.Y + 6, 11, color, max: 110);
            if (interactive && click && rect.Contains(mouseX, mouseY) && hit.Contains(mouseX, mouseY))
            {
                activeMarker = marker.Id; selectedClip = null; selectedWindowEvent = -1; selectedNoteTime = null;
                transport.SetPlaying(false); transport.Seek(Current.Timeline.Bpm.Time(marker.Beat)); click = false;
                if (mouseClicks >= 2) OpenValue(L.Get("Marker label"), marker.Label, value => editor.RenameMarker(marker.Id, value));
                message = L.Format($"Target marker {marker.Label}: beat {marker.Beat:0.######}. Shift+E deletes; CLEAR TARGET releases insertion target.");
            }
        }
        Canvas.Clip(null);
    }
}
