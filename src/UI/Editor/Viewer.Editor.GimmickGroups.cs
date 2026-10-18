using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    readonly HashSet<int> expandedGimmickTracks = [-1];
    void ResetGimmickTracks()
    {
        expandedGimmickTracks.Clear();
        expandedGimmickTracks.Add(-1);
    }
    readonly Dictionary<int, IntervalIndex<VsmDocument.Clip[]>> gimmickGroupIndices = [];
    readonly Dictionary<Guid, VsmDocument.Clip[]> gimmickGroupsByClip = [];
    long gimmickGroupRevision = -1;
    static string GimmickTrackKey(int target) => "G:" + target;
    bool IsGimmickMod(string name, int target) => !(target < 0 && name == EpisodeScript.ModName)
        && !(CustomImages.TryMod(name, out _, out var image) && declaredImageIds.Contains(image))
        && !(CustomText.TryMod(name, out _, out var text) && editor?.TextSources.ContainsKey(text) == true);
    bool IsGimmickTrack(EditTrack track) => !track.Window && track.ImageId == null && track.TextId == null
        && IsGimmickMod(track.Property, track.Target);

    void SetGimmickGrouping(bool grouped, bool persist = true)
    {
        CloseInline(); CloseClipMenu(); editDrag = null;
        preferences.GroupGimmickTracks = grouped;
        if (!grouped) SetClipSelection(selectedClip);
        layoutRevision = -1; trackScroll = 0; motion.Snap("timeline-track-scroll", 0);
        if (persist) SaveSettings();
        click = false;
    }

    // A view over the existing rows: source IDs, line order, values and repeat syntax remain untouched.
    void GroupGimmickRows()
    {
        if (!preferences.GroupGimmickTracks) return;
        var original = editTracks.ToArray();
        var groups = original.Where(IsGimmickTrack).GroupBy(t => t.Target).ToDictionary(g => g.Key, g => g.ToArray());
        var seen = new HashSet<int>();
        editTracks.Clear();
        foreach (var track in original)
        {
            if (!IsGimmickTrack(track)) { editTracks.Add(track); continue; }
            if (!seen.Add(track.Target)) continue;
            editTracks.Add(new(GimmickTrackKey(track.Target), track.Target < 0 ? L.Get("GLOBAL GIMMICKS") : "Proxy " + track.Target,
                false, track.Target, track.Target < 0 ? "scrollspeed" : "prx", GimmickGroup: true));
            if (expandedGimmickTracks.Contains(track.Target)) editTracks.AddRange(groups[track.Target]);
        }
    }

    void BuildGimmickGroups()
    {
        gimmickGroupIndices.Clear(); gimmickGroupsByClip.Clear();
        if (editor == null) return;
        gimmickGroupRevision = editor.Revision;
        foreach (var target in editor.Vsm.Clips.Where(c => IsGimmickMod(c.Name, c.Proxy)).GroupBy(c => c.Proxy))
        {
            var groups = target.GroupBy(c => (c.Beat, c.Duration, c.Ease, Repeat: c.RepeatEnd != null ? c.Id : Guid.Empty))
                .SelectMany(g =>
                {
                    // Equal timing can combine different channels, never two copies of the same channel.
                    var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
                    return g.Select(c => (Clip: c, Lane: occurrences[c.Name] = occurrences.GetValueOrDefault(c.Name) + 1))
                        .GroupBy(c => c.Lane).Select(row => row.Select(c => c.Clip).ToArray());
                }).ToArray();
            foreach (var group in groups) foreach (var clip in group) gimmickGroupsByClip[clip.Id] = group;
            gimmickGroupIndices[target.Key] = new(groups.Select((g, order) =>
                new IntervalIndex<VsmDocument.Clip[]>.Entry(g, g[0].Beat, g.Max(VsmVisualEnd), order)));
        }
    }

    void SelectGimmickGroup(VsmDocument.Clip[] group, bool additive)
    {
        bool remove = additive && group.All(c => selectedClips.Contains(c.Id));
        if (!additive && !group.All(c => selectedClips.Contains(c.Id))) selectedClips.Clear();
        foreach (var clip in group) { if (remove) selectedClips.Remove(clip.Id); else selectedClips.Add(clip.Id); }
        selectedClip = remove ? selectedClips.Count > 0 ? selectedClips.First() : null : group[0].Id;
        SyncPickedProxy(selectedClip); imageInspector = textInspector = false;
    }

    void DrawGimmickGroupClips(int target, float y, bool interactive)
    {
        EnsureTimelineClipIndex();
        if (editor != null && gimmickGroupRevision != editor.Revision) BuildGimmickGroups();
        if (!gimmickGroupIndices.TryGetValue(target, out var index)) return;
        double start = beatStart - 10 / pixelsPerBeat, end = beatStart + editorTracksRect.W / pixelsPerBeat;
        var visible = index.Overlap(start, end).OrderBy(e => e.Order).ToList();
        if (editDrag?.Clip is { } dragged && dragged.Proxy == target && gimmickGroupsByClip.TryGetValue(dragged.Id, out var moving)
            && !visible.Any(e => e.Value[0].Id == moving[0].Id))
            visible.Add(new(moving, moving[0].Beat, moving.Max(VsmVisualEnd), int.MaxValue));
        var hit = interactive && click ? visible.LastOrDefault(e => new KuroakiGimmick.Graphics.Rect(BeatX(e.Start), y + 6,
            Math.Max(9, BeatX(e.End) - BeatX(e.Start)), 23).Contains(mouseX, mouseY)).Value : null;
        foreach (var entry in visible)
        {
            var clips = entry.Value; var first = clips[0];
            string label = string.Join(" + ", clips.Select(c => c.Name).Distinct());
            DrawEventClip(y, entry.Start, entry.End, label, clips.Any(c => selectedClips.Contains(c.Id)),
                false, first, null, -1, interactive && ReferenceEquals(clips, hit), clips);
        }
    }
}
