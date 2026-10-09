using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    string? draggedTrackKey, trackDropBefore;
    string draggedTrackLabel = "";
    float trackDragStartY, trackShownScroll;
    bool trackOrderMoved, trackDropValid;
    double trackScrollTick;

    string TrackRoot(EditTrack track)
    {
        if (track.ImageId != null) return "I:" + track.ImageId;
        if (track.TextId != null) return "T:" + track.TextId;
        if (!track.Window && CustomText.TryMod(track.Property, out _, out var id) && editor?.TextSources.ContainsKey(id) == true)
            return "T:" + id;
        return track.Key;
    }

    void ApplyTrackOrder()
    {
        if (editor == null) return;
        var groups = editTracks.GroupBy(TrackRoot).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var ordered = editor.OrderedTracks(groups.Keys);
        editTracks.Clear();
        foreach (string key in ordered) editTracks.AddRange(groups[key]);
    }

    void BeginTrackOrder(EditTrack track)
    {
        if (editor == null || Busy || editDrag != null || ImageGestureActive) return;
        draggedTrackKey = TrackRoot(track); draggedTrackLabel = track.Label;
        trackDragStartY = mouseY; trackOrderMoved = trackDropValid = false;
        trackScrollTick = uptime.Elapsed.TotalSeconds;
        hoveredEditTrack = displayedHelpTrack = null; trackHelpVisualActive = trackHelpHitRectValid = false;
        trackHelpWHeld = false; motion.Snap("track-help-visible", 0);
        click = false; held = true; Sdl.SDL_CaptureMouse(true);
    }

    void CancelTrackOrder()
    {
        if (draggedTrackKey == null) return;
        draggedTrackKey = null; trackDropValid = false;
        click = held = false; Sdl.SDL_CaptureMouse(false);
    }

    bool HandleTrackOrderInput(Sdl.Event e)
    {
        if (draggedTrackKey == null) return false;
        if (e.WindowID != 0 && e.WindowID != Sdl.SDL_GetWindowID(host.Window)) return false;
        if (e.Type is 0x20F or 0x100 or Sdl.WindowCloseRequested || e.Type == 0x300 && e.Scan == 41)
        { CancelTrackOrder(); return e.Type is not (0x100 or Sdl.WindowCloseRequested); }
        if (e.Type is 0x400 or 0x402)
        {
            mouseX = e.X; mouseY = e.Y;
            trackOrderMoved |= Math.Abs(mouseY - trackDragStartY) >= 4;
            UpdateTrackDrop();
        }
        if (e.Type == 0x402 && e.Button == 1)
        {
            string key = draggedTrackKey;
            string? before = trackDropBefore;
            bool apply = trackOrderMoved && trackDropValid;
            var roots = editTracks.Select(TrackRoot).Distinct(StringComparer.Ordinal).ToArray();
            CancelTrackOrder();
            if (apply && editor != null)
            {
                editor.MoveTrack(key, before, roots);
                layoutRevision = -1;
                message = L.Get("Track order updated / undo available");
            }
        }
        return e.Type is >= 0x300 and <= 0x403;
    }

    void UpdateTrackDrop()
    {
        trackDropValid = editorTracksRect.H > 0 && editTracks.Count > 0 && editorTimelineRect.Contains(mouseX, mouseY);
        if (!trackDropValid) return;
        int at = Math.Clamp((int)Math.Floor((mouseY - editorTracksRect.Y) / 35 + trackShownScroll), 0, editTracks.Count - 1);
        string root = TrackRoot(editTracks[at]);
        int first = editTracks.FindIndex(t => TrackRoot(t) == root), last = editTracks.FindLastIndex(t => TrackRoot(t) == root);
        float midpoint = editorTracksRect.Y + ((first + last + 1) / 2f - trackShownScroll) * 35;
        int boundary = mouseY < midpoint ? first : last + 1;
        trackDropBefore = boundary < editTracks.Count ? TrackRoot(editTracks[boundary]) : null;
    }

    void ScrollTrackOrder(int visibleRows)
    {
        if (draggedTrackKey == null || !trackOrderMoved || uptime.Elapsed.TotalSeconds - trackScrollTick < .1) return;
        int direction = mouseY < editorTracksRect.Y + 25 ? -1 : mouseY > editorTracksRect.Y + editorTracksRect.H - 25 ? 1 : 0;
        trackScrollTick = uptime.Elapsed.TotalSeconds;
        if (direction == 0) return;
        trackScroll = Math.Clamp(trackScroll + direction, 0, Math.Max(0, editTracks.Count - visibleRows));
        motion.Snap("timeline-track-scroll", trackScroll);
    }

    void DrawTrackOrder()
    {
        if (draggedTrackKey == null || !trackOrderMoved) return;
        UpdateTrackDrop();
        Canvas.Clip(new(editorTimelineRect.X, editorTracksRect.Y, editorTimelineRect.W, editorTracksRect.H));
        if (trackDropValid)
        {
            int index = trackDropBefore == null ? editTracks.Count : editTracks.FindIndex(t => TrackRoot(t) == trackDropBefore);
            float y = editorTracksRect.Y + (index - trackShownScroll) * 35;
            Canvas.Line(editorTimelineRect.X + 2, y, editorTracksRect.X + editorTracksRect.W, y, 3, soft);
        }
        var ghost = new Rect(editorTimelineRect.X + 5, mouseY - 16, Math.Max(40, editorTracksRect.X - editorTimelineRect.X - 10), 32);
        Canvas.Fill(ghost, Theme.PanelRaised.Alpha(.95)); Canvas.Border(ghost, soft);
        Text(draggedTrackLabel, ghost.X + 10, ghost.Y + 7, 12, white, max: ghost.W - 20);
        Canvas.Clip(null);
    }
}
