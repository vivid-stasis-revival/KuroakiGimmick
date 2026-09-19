using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// 时间轴上的 image 动画组条块：绘制、选中，以及整体平移 / 拉伸右端的拖拽。
/// </summary>
public sealed partial class Viewer
{
    /// <summary>一次动画组拖拽的快照。Owner 与 Revision 用于松手时校验文档没被换过；Resize 为真表示拖的是右端把手而非整体平移。</summary>
    sealed record ImageGroupDrag(EditorDocument Owner, long Revision, ImageMotionGroup Group, double MouseBeat, bool Resize);
    ImageGroupDrag? imageGroupDrag;
    /// <summary>
    /// 算出拖拽后的起止拍。平移时只吸附起点，终点按“拍数长度不变”跟着走，所以跨 BPM 变化拖动不会改变时长；
    /// 拉伸时起点钉住、终点吸附，并强制至少比起点大 1e-6，不让区间塌成零或负。
    /// </summary>
    (double Start, double End) ImageGroupDragRange(ImageGroupDrag drag)
    {
        double delta = BeatAt(mouseX) - drag.MouseBeat;
        double start = drag.Resize ? drag.Group.Beat : Snap(drag.Group.Beat + delta);
        double end = drag.Resize ? Math.Max(start + 1e-6, Snap(drag.Group.End(Current.Timeline.Bpm) + delta)) : drag.Group.End(Current.Timeline.Bpm) + start - drag.Group.Beat;
        return (start, end);
    }
    /// <summary>
    /// 画某个 image 的全部动画组条块，并处理点击。可直接编辑的组才允许拖，否则展开该 image 的裸事件轨并提示原因，
    /// 绝不擅自把独立/动态事件当成一个可整体平移的组。
    /// 右端 7px 是拉伸把手，且只在有时长、条块宽度超过 17px 时才启用，太窄时整条只能平移，免得点不中主体。
    /// 零时长的组画成一根竖线而不是区间条，视觉上就区分出“瞬时值”和“动画”。
    /// </summary>
    void DrawImageGroupClips(string id, float y, bool interactive)
    {
        if (editor == null) return;
        foreach (var group in ImageGroups(id))
        {
            double start = group.Beat;
            double end = group.Editable ? group.End(Current.Timeline.Bpm) : group.Clips.Max(VsmVisualEnd);
            if (imageGroupDrag is { } drag && drag.Group.Id == group.Id) (start, end) = ImageGroupDragRange(drag);
            float x = BeatX(start), width = Math.Max(10, BeatX(end) - x);
            if (x + width < editorTracksRect.X || x > editorTracksRect.X + editorTracksRect.W) continue;
            var box = new Rect(x, y + 6, width, 23); bool selected = selectedImageId == id && ActiveImageGroup?.Id == group.Id;
            Canvas.Fill(box, Mix(Theme.PanelRaised, Theme.Accent, selected ? .7f : .3f));
            Canvas.Border(box, selected ? white : soft, selected ? 2 : 1);
            if (width > 40) Text(group.Label, x + 5, y + 11, 11, white, true, width - 12);
            if (group.Duration > 0) Canvas.Fill(new(x + width - 4, y + 10, 2, 14), muted);
            else Canvas.Line(x + width / 2, y + 10, x + width / 2, y + 25, 2, white);
            if (!interactive || !click || !box.Contains(mouseX, mouseY) || !editorTracksRect.Contains(mouseX, mouseY)) continue;
            SelectImageObject(id, true, group.Id); selectedNoteTime = null;
            if (!group.Editable)
            {
                imageInspector = false; expandedImageTracks.Add(id); layoutRevision = -1;
                message = "Independent/dynamic image events: raw event editor.";
            }
            else
            {
                imageGroupDrag = new(editor, editor.Revision, group, BeatAt(mouseX), group.Duration > 0 && width > 17 && mouseX >= x + width - 7);
                Sdl.SDL_CaptureMouse(true); held = true;
            }
            click = false;
        }
    }
    /// <summary>
    /// 结束拖拽并提交。位移换算成 UI 像素后不足 3px 就当成单纯的点选，不写入文档；
    /// 文档实例或 Revision 变过同样直接放弃。提交后把播放头重新对到编辑拍上。
    /// </summary>
    void FinishImageGroupDrag()
    {
        if (imageGroupDrag is not { } drag) return;
        imageGroupDrag = null;
        if (editor != drag.Owner || editor.Revision != drag.Revision || Math.Abs(BeatAt(mouseX) - drag.MouseBeat) * pixelsPerBeat < 3) return;
        var range = ImageGroupDragRange(drag);
        ImageAction(() => editor.TimeImageGroup(drag.Group, range.Start, range.End, drag.Group.Ease, Current.Timeline.Bpm));
        layoutRevision = -1;
        transport.Seek(Current.Timeline.Bpm.Time(ImageEditBeat));
    }
}
