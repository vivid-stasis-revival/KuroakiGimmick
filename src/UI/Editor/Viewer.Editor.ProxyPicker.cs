// 直接拾取 proxy，保证编辑目标与预览校准使用同一 ID。

using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    // 选择 proxy 时保持播放时刻不变，将其首条轨道滚入视图；空 proxy 仍可选并能接收首个事件。
    void FocusProxyTrack(int target)
    {
        if (editor == null || target < 0) return;
        RebuildEditTracks();
        int index = editTracks.FindIndex(t => !t.Window && t.Target == target && t.ImageId == null && t.TextId == null);
        if (index < 0) return;
        int rows = Math.Max(1, (int)(editorTracksRect.H / 35));
        if (index < trackScroll || index >= trackScroll + rows)
            trackScroll = Math.Max(0, index - rows / 2);
        motion.Snap("timeline-track-scroll", trackScroll);
    }

}
