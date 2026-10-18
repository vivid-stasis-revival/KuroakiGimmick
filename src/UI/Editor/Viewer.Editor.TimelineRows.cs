using System.Text.Json.Nodes;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    readonly Dictionary<Guid, int> clipRowLanes = [];
    readonly Dictionary<int, int> windowRowLanes = [];
    readonly Dictionary<string, int> timelineLaneCounts = [];
    static string ModTrackKey(EditTrack track) => $"{track.Target}:{track.Property}";

    void SetSplitOverlappingTimelineClips(bool enabled, bool persist = true)
    {
        CloseClipMenu(); editDrag = null;
        preferences.SplitOverlappingTimelineClips = enabled;
        layoutRevision = -1;
        if (persist) SaveSettings();
        click = false;
    }

    // Stable chronological interval partition. Touching intervals share a row; simultaneous
    // zero-length events do not. The source order and playback precedence never change.
    static int AssignTimelineLanes<T>(IEnumerable<(T Id, double Start, double End)> intervals, Dictionary<T, int> lanes) where T : notnull
    {
        var active = new PriorityQueue<int, (double End, double Start, int Lane)>();
        var available = new PriorityQueue<int, int>();
        int count = 0;
        foreach (var item in intervals.OrderBy(item => item.Start))
        {
            while (active.TryPeek(out int released, out var end) && end.End <= item.Start && end.Start < item.Start)
            { active.Dequeue(); available.Enqueue(released, released); }
            int lane = available.Count > 0 ? available.Dequeue() : count++;
            active.Enqueue(lane, (item.End, item.Start, lane)); lanes[item.Id] = lane;
        }
        return Math.Max(1, count);
    }

    void BuildTimelineLanes()
    {
        if (editor == null) return;
        EnsureTimelineClipIndex();
        clipRowLanes.Clear(); windowRowLanes.Clear(); timelineLaneCounts.Clear();
        // Empty lane maps put every event back in lane zero, including hit-testing and row copy.
        if (!preferences.SplitOverlappingTimelineClips) return;
        foreach (var track in editor.Vsm.Clips.GroupBy(c => c.TrackKey))
            timelineLaneCounts[track.Key] = AssignTimelineLanes(track.Select(c => (c.Id, c.Beat, VsmVisualEnd(c))), clipRowLanes);
        if (editor.Windows.Events is { } events)
            foreach (var track in events.Select((node, i) => (Event: node as JsonObject, Id: i)).Where(e => e.Event != null)
                .GroupBy(e => $"W:{WindowMotionConfig.Integer(e.Event!, "w")}:{WindowMotionConfig.Text(e.Event!, "op")}"))
                timelineLaneCounts[track.Key] = AssignTimelineLanes(track.Select(e =>
                {
                    double time = WindowMotionConfig.Number(e.Event!, "t");
                    return (e.Id, Current.Timeline.Bpm.Beat(time), Current.Timeline.Bpm.Beat(time + Math.Max(0, WindowMotionConfig.Duration(e.Event!))));
                }), windowRowLanes);
    }

    void SplitTimelineRows()
    {
        var tracks = editTracks.ToArray(); editTracks.Clear();
        foreach (var track in tracks)
        {
            // Container headers stay unique; only their property rows split into sibling lanes.
            int count = track.GimmickGroup || track.ImageGroup || track.TextId != null ? 1 : timelineLaneCounts.GetValueOrDefault(track.Key, 1);
            for (int lane = 0; lane < count; lane++)
                editTracks.Add(track with { Lane = lane, Key = lane == 0 ? track.Key : track.Key + "/lane:" + lane,
                    Label = count == 1 ? track.Label : track.Label + $" [{lane + 1}]" });
        }
    }

    VsmDocument.Clip[] ClipsOnTimelineRow(EditTrack track)
    {
        if (editor == null || track.Window || track.ImageGroup || track.TextId != null) return [];
        return editor.Vsm.Clips.Where(c => track.GimmickGroup
            ? c.Proxy == track.Target && IsGimmickMod(c.Name, c.Proxy)
            : c.TrackKey == ModTrackKey(track) && clipRowLanes.GetValueOrDefault(c.Id) == track.Lane).ToArray();
    }

    int[] WindowEventsOnTimelineRow(EditTrack track) => editor?.Windows.Events is { } events && track.Window
        ? events.Select((node, i) => (Event: node as JsonObject, Id: i)).Where(e => e.Event != null
            && WindowMotionConfig.Integer(e.Event, "w") == track.Target && WindowMotionConfig.Text(e.Event, "op") == track.Property
            && windowRowLanes.GetValueOrDefault(e.Id) == track.Lane).Select(e => e.Id).ToArray() : [];

    bool CanDuplicateTimelineRow(EditTrack track) => track.Window ? WindowEventsOnTimelineRow(track).Length > 0 : ClipsOnTimelineRow(track).Length > 0;

    void DuplicateTimelineRow(EditTrack track)
    {
        if (editor == null || Busy) return;
        var clips = ClipsOnTimelineRow(track);
        var windows = WindowEventsOnTimelineRow(track).Select(i => (JsonObject)editor.Windows.Events![i]!.DeepClone()).ToArray();
        var copies = clips.Select(c => c with { Id = Guid.NewGuid() }).ToArray();
        Edit(L.Get("DUPLICATE ROW"), () =>
        {
            foreach (var clip in copies) editor.Vsm.Add(clip);
            foreach (var window in windows) editor.AddWindow(window);
        });
        if (copies.Length > 0)
        {
            SetClipSelection(copies[0].Id);
            foreach (var copy in copies) selectedClips.Add(copy.Id);
        }
        layoutRevision = -1;
    }
}
