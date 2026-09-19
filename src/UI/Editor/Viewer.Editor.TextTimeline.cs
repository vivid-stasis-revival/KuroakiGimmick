using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

/// <summary>
/// 时间轴上的文本事件条块。与 image 组不同，这里只做选中、不支持拖动改时间——
/// 改时间统一走检视面板的字段，避免在多条并列属性上拖出彼此不一致的区间。
/// </summary>
public sealed partial class Viewer
{
    /// <summary>
    /// 画某个文本 id 的事件条块。按 (Beat, Duration, Ease) 分组，把同一区间上的多条属性写入折叠成一块。
    /// 只要组内出现 repeat、"_"/573613 哨兵（沿用当时的当前值）或 proxy，就标记为 RAW：
    /// 这类事件的端点不是定值，点击时改为展开裸事件轨，不提供直接编辑。
    /// 零时长画成竖线并标 POSE，有时长标 MOTION。
    /// </summary>
    void DrawTextGroupClips(string id, float y, bool interactive)
    {
        if (editor == null) return;
        foreach (var group in editor.Vsm.Clips.Where(c => CustomText.TryMod(c.Name, out _, out var target) && target == id)
            .GroupBy(c => (c.Beat, c.Duration, c.Ease)))
        {
            var clip = group.First(); float x = BeatX(clip.Beat), width = Math.Max(10, BeatX(group.Max(VsmVisualEnd)) - x);
            if (x + width < editorTracksRect.X || x > editorTracksRect.X + editorTracksRect.W) continue;
            bool raw = group.Any(c => c.RepeatEnd != null || c.From is "_" or "573613" || c.To is "_" or "573613" || c.Proxy != -1);
            bool selected = selectedTextId == id && (textAnimation == clip.Id ||
                textInspector && textAnimation == null && clip.Duration == 0 && Math.Abs(textAt - clip.Beat) < 1e-9);
            var box = new Rect(x, y + 6, width, 23);
            Canvas.Fill(box, Mix(Theme.PanelRaised, Theme.Accent, selected ? .7f : .3f));
            Canvas.Border(box, selected ? white : soft, selected ? 2 : 1);
            if (width > 40) Text(raw ? "RAW" : clip.Duration > 0 ? "MOTION" : "POSE", x + 5, y + 11, 11, white, true, width - 12);
            if (clip.Duration == 0) Canvas.Line(x + width / 2, y + 10, x + width / 2, y + 25, 2, white);
            if (interactive && click && box.Contains(mouseX, mouseY) && editorTracksRect.Contains(mouseX, mouseY))
            {
                SelectText(id);
                if (raw) { textInspector = false; expandedTextTracks.Add(id); layoutRevision = -1; selectedClip = clip.Id; }
                else if (clip.Duration > 0) SelectTextAnimation(clip, true);
                else { textAt = clip.Beat; transport.Seek(Current.Timeline.Bpm.Time(textAt)); }
                click = false;
            }
        }
    }
}
