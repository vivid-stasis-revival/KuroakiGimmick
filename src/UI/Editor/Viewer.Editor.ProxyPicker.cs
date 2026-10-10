using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

/// <summary>Direct Proxy selection; the editor target and the preview calibration refer to the same ID.</summary>
public sealed partial class Viewer
{
    // Select a proxy without seeking playback; scroll its first PR track into view.
    // Empty proxies remain selectable and can receive their first event.
    void FocusProxyTrack(int target)
    {
        if (editor == null || target < 0) return;
        RebuildEditTracks();
        int index = editTracks.FindIndex(t => !t.Window && t.Target == target &&
            t.Property.StartsWith("pr", StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        int rows = Math.Max(1, (int)(editorTracksRect.H / 35));
        if (index < trackScroll || index >= trackScroll + rows)
            trackScroll = Math.Max(0, index - rows / 2);
        motion.Snap("timeline-track-scroll", trackScroll);
    }

}
