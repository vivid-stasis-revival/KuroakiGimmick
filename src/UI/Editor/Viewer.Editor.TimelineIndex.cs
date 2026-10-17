using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    EditorDocument? timelineIndexOwner;
    long timelineIndexRevision = -1;
    NoteIndex? timelineIndexTiming;
    readonly Dictionary<string, IntervalIndex<VsmDocument.Clip>> timelineClipIndices = [];
    readonly Dictionary<Guid, IntervalIndex<VsmDocument.Clip>.Entry> timelineClipsById = [];
    readonly List<IntervalIndex<VsmDocument.Clip>.Entry> timelineVisibleEntries = [];
    readonly List<VsmDocument.Clip> timelineVisibleClips = [];
    readonly Dictionary<string, IntervalIndex<TextClipGroup>> timelineTextIndices = [];
    readonly Dictionary<string, VsmDocument.Clip[]> timelineTextAnimations = [];
    readonly List<IntervalIndex<TextClipGroup>.Entry> timelineVisibleText = [];
    readonly Dictionary<int, int> timelineProxyClipCounts = [];
    readonly HashSet<int> timelineProxyAlpha = [];
    int timelineClipCount, timelineDeclaredProxyCount;
    readonly Dictionary<string, IntervalIndex<ImageMotionGroup>> timelineImageIndices = [];
    readonly List<IntervalIndex<ImageMotionGroup>.Entry> timelineVisibleImages = [];
    sealed record TextClipGroup(VsmDocument.Clip Clip, double End, bool Raw);

    void EnsureTimelineClipIndex()
    {
        if (editor == null || ReferenceEquals(timelineIndexOwner, editor) && timelineIndexRevision == editor.VsmRevision
            && ReferenceEquals(timelineIndexTiming, Current.NoteIndex)) return;
        var previousTracks = new Dictionary<string, IntervalIndex<VsmDocument.Clip>>(timelineClipIndices);
        timelineClipIndices.Clear(); timelineClipsById.Clear(); timelineTextIndices.Clear(); timelineTextAnimations.Clear();
        timelineImageIndices.Clear(); timelineProxyClipCounts.Clear(); timelineProxyAlpha.Clear(); timelineClipCount = 0;
        timelineDeclaredProxyCount = editor.Vsm.ProxyCount;
        var tracks = new Dictionary<string, List<IntervalIndex<VsmDocument.Clip>.Entry>>();
        var texts = new Dictionary<string, List<VsmDocument.Clip>>();
        int order = 0;
        foreach (var c in editor.Vsm.Clips)
        {
            if (c.Name.Equals("pra", StringComparison.OrdinalIgnoreCase)) timelineProxyAlpha.Add(c.Proxy);
            timelineClipCount++; timelineProxyClipCounts[c.Proxy] = timelineProxyClipCounts.GetValueOrDefault(c.Proxy) + 1;
            var e = new IntervalIndex<VsmDocument.Clip>.Entry(c, c.Beat, VsmVisualEnd(c), order++);
            if (!tracks.TryGetValue(c.TrackKey, out var list)) tracks[c.TrackKey] = list = [];
            list.Add(e); timelineClipsById[c.Id] = e;
            if (CustomText.TryMod(c.Name, out _, out var id))
            {
                if (!texts.TryGetValue(id, out var text)) texts[id] = text = [];
                text.Add(c);
            }
        }
        foreach (var (key, list) in tracks)
        {
            if (ReferenceEquals(timelineIndexTiming, Current.NoteIndex) && previousTracks.TryGetValue(key, out var old)
                && old.TryUpdate(list)) timelineClipIndices[key] = old;
            else timelineClipIndices[key] = new(list);
        }
        foreach (var (id, clips) in texts)
        {
            var groups = clips.GroupBy(c => (c.Beat, c.Duration, c.Ease)).Select((g, i) =>
            {
                var c = g.First(); double end = g.Max(VsmVisualEnd);
                bool raw = g.Any(c => c.RepeatEnd != null || c.From is "_" or "573613" || c.To is "_" or "573613" || c.Proxy != -1);
                return new IntervalIndex<TextClipGroup>.Entry(new(c, end, raw), c.Beat, end, i);
            }).ToArray();
            timelineTextIndices[id] = new(groups);
            timelineTextAnimations[id] = groups.Where(g => g.Value.Clip.Duration > 0).Select(g => g.Value.Clip).ToArray();
        }
        BuildGimmickGroups();
        timelineIndexOwner = editor; timelineIndexRevision = editor.VsmRevision; timelineIndexTiming = Current.NoteIndex;
    }

    VsmDocument.Clip? EditorFindClip(Guid id)
    {
        if (editor == null) return null;
        EnsureTimelineClipIndex();
        return timelineClipsById.TryGetValue(id, out var entry) ? entry.Value : null;
    }
    VsmDocument.Clip[] CachedSelectedClipList()
    {
        if (editor == null) return [];
        EnsureTimelineClipIndex();
        return selectedClips.Where(timelineClipsById.ContainsKey).Select(id => timelineClipsById[id]).OrderBy(e => e.Order).Select(e => e.Value).ToArray();
    }

    int EditorProxyCount() { EnsureTimelineClipIndex(); return timelineDeclaredProxyCount; }
    bool EditorHasProxyAlpha(int proxy) { EnsureTimelineClipIndex(); return timelineProxyAlpha.Contains(proxy); }

    int EditorClipCount(int? proxy = null)
    {
        EnsureTimelineClipIndex();
        return proxy.HasValue ? timelineProxyClipCounts.GetValueOrDefault(proxy.Value) : timelineClipCount;
    }
    List<IntervalIndex<ImageMotionGroup>.Entry> VisibleImageGroups(string id)
    {
        EnsureTimelineClipIndex();
        if (!timelineImageIndices.TryGetValue(id, out var index))
        {
            var groups = ImageGroups(id);
            index = new(groups.Select((g, i) => new IntervalIndex<ImageMotionGroup>.Entry(g, g.Beat,
                g.Editable ? g.End(Current.Timeline.Bpm) : g.Clips.Max(VsmVisualEnd), i)));
            timelineImageIndices[id] = index;
        }
        timelineVisibleImages.Clear();
        double start = beatStart - 10 / pixelsPerBeat, end = beatStart + editorTracksRect.W / pixelsPerBeat;
        foreach (var e in index.Overlap(start, end)) timelineVisibleImages.Add(e);
        if (imageGroupDrag is { } drag && drag.Group.ImageId == id && !timelineVisibleImages.Any(e => e.Value.Id == drag.Group.Id))
            timelineVisibleImages.Add(new(drag.Group, drag.Group.Beat, drag.Group.End(Current.Timeline.Bpm), int.MaxValue));
        timelineVisibleImages.Sort(static (a, b) => a.Order.CompareTo(b.Order));
        return timelineVisibleImages;
    }

    List<VsmDocument.Clip> VisibleTimelineClips(string key)
    {
        timelineVisibleEntries.Clear(); timelineVisibleClips.Clear();
        if (!timelineClipIndices.TryGetValue(key, out var index)) return timelineVisibleClips;
        double start = beatStart - 10 / pixelsPerBeat, end = beatStart + editorTracksRect.W / pixelsPerBeat;
        foreach (var e in index.Overlap(start, end)) timelineVisibleEntries.Add(e);
        // Drag preview may move previously invisible selected intervals into view. Keep source ordering.
        if (editDrag?.Clip is { } drag)
        {
            foreach (var id in selectedClips)
                if (timelineClipsById.TryGetValue(id, out var e) && e.Value.TrackKey == key && (e.End < start || e.Start > end)) timelineVisibleEntries.Add(e);
            if (timelineClipsById.TryGetValue(drag.Id, out var dragged) && dragged.Value.TrackKey == key && !selectedClips.Contains(drag.Id)
                && (dragged.End < start || dragged.Start > end)) timelineVisibleEntries.Add(dragged);
        }
        timelineVisibleEntries.Sort(static (a, b) => a.Order.CompareTo(b.Order));
        foreach (var e in timelineVisibleEntries) timelineVisibleClips.Add(e.Value);
        return timelineVisibleClips;
    }
}
