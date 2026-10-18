using System.Runtime.InteropServices;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void SmokeGimmickSource()
    {
        var oldSession = Current; var oldEditor = editor;
        bool oldGrouped = preferences.GroupGimmickTracks;
        string fixture = "!obj:obj_custom_gimmick\r\n!proxies:2\r\n// retain this comment\r\n"
            + "0,2,linear,_,30,prx,0\r\n0,2,linear,0,10,pry,0\r\n"
            + "0,2,linear,0,12,prx,1\r\n0,1,linear,1,2,przm,0\r\n"
            + "4:8:2,1,linear,0,20,prrz,0\r\n";
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Frame() { layoutRevision = -1; renderedRevision = editor!.Revision; Draw(1440, 940); }
        void Type(string value)
        {
            nint ptr = Marshal.StringToCoTaskMemUTF8(value);
            try { Handle(new() { Type = 0x303, TextData = ptr }); } finally { Marshal.FreeCoTaskMem(ptr); }
        }
        try
        {
            Current = new(new ViewerProject { Profile = "core", RoomPreset = "none", GameUiEnabled = false }, editedVsm: fixture);
            editor = new(Current); editor.ApplySource(fixture, "");
            preferences.GroupGimmickTracks = true; ResetGimmickTracks();
            beatStart = 0; pixelsPerBeat = 48; trackScroll = 0; Frame();
            Check(editTracks.Count(t => t.GimmickGroup) == 3, "Global/proxy headers must remain unique per target.");
            Check(expandedGimmickTracks.SetEquals(new[] { -1 }) && editTracks.Any(t => t.Key == "-1:scrollspeed"), "Global must be expanded by default, with proxies collapsed.");
            Check(editor.Vsm.Text == fixture, "Grouping changed the original source.");
            SmokeAddedGimmickTrackFocus();
            SmokeExpandedGimmickSummaries();
            if (Environment.GetEnvironmentVariable("KG_GROUP_SCREENSHOT") is { } groupedScreenshot)
                Canvas.SavePpm(UiTarget, groupedScreenshot);
            var originalIds = editor.Vsm.Clips.Select(c => c.Id).ToArray();
            EnsureTimelineClipIndex();
            var group = gimmickGroupsByClip[originalIds[0]];
            Check(group.Length == 2 && group.All(c => c.Proxy == 0), "Grouping mixed targets or differing durations.");
            Check(gimmickGroupsByClip[originalIds[^1]].Length == 1, "Repeat event was merged into an ordinary animation.");
            SelectGimmickGroup(group, false);
            Check(selectedClips.Count == 2, "Group selection did not select all original events.");
            editDrag = new(group[0], null, -1, 0, 2, 0, false, group.Select(c => c.Id).ToArray());
            mouseX = BeatX(2); FinishDrag();
            Check(editor.Vsm.Clips.Take(2).All(c => c.Beat == 2) && editor.Vsm.Clips.ElementAt(2).Beat == 0,
                "Moving a group changed a different proxy or missed a member.");
            Check(editor.Vsm.Clips.Select(c => c.Id).SequenceEqual(originalIds), "Group move changed event identity/order.");
            editor.Undo(); Check(editor.Vsm.Text == fixture, "Group move did not undo exactly.");
            editDrag = new(group[0], null, -1, 0, 2, 2, true, group.Select(c => c.Id).ToArray());
            mouseX = BeatX(3); FinishDrag();
            Check(editor.Vsm.Clips.Take(2).All(c => c.Duration == 3), "Group resize changed only one member.");
            editor.Undo(); Check(editor.Vsm.Text == fixture, "Group resize did not undo exactly.");
            SmokeChildTrackOrder();
            Check(editor.Vsm.Text == fixture, "Child-track ordering changed the original source.");
            expandedGimmickTracks.Add(0); Frame();
            Check(editTracks.Any(t => !t.GimmickGroup && t.Target == 0 && t.Property == "pry"), "Expanding a group did not expose raw channels.");
            SetGimmickGrouping(false, persist: false); Frame();
            Check(!editTracks.Any(t => t.GimmickGroup) && editor.Vsm.Text == fixture, "Raw view changed source events.");
            SetGimmickGrouping(true, persist: false); Frame();

            OpenSourceEditor(); Frame();
            Check(sourceVsm != null, "Source editor did not open for the active document.");
            if (Environment.GetEnvironmentVariable("KG_SOURCE_SCREENSHOT") is { } sourceScreenshot)
                Canvas.SavePpm(UiTarget, sourceScreenshot);
            sourceVsm!.Select(sourceVsm.Text.Length); Type("// 中文 source");
            Handle(new() { Type = 0x300, Scan = 40 }); Type("9,0,linear,0,1,pra,1");
            Check(workflow == "source" && sourceVsm.Text.Contains("// 中文 source\r\n9,"), "Source Enter submitted instead of inserting a newline.");
            Handle(new() { Type = 0x300, Scan = 29, Modifiers = 0x0040 });
            Check(!sourceVsm.Text.EndsWith("pra,1"), "Draft undo failed.");
            Handle(new() { Type = 0x300, Scan = 28, Modifiers = 0x0040 });
            Check(sourceVsm.Text.EndsWith("pra,1") && editor.Vsm.Text == fixture, "Draft typing mutated the live document.");
            sourceImageTab = true; Type("// handwritten VSP\r\n#Layer\r\nmain,0\r\n#Image\r\nmain:\r\n");
            string vsm = sourceVsm.Text, vsp = sourceVsp!.Text;
            ApplySourceEditor();
            Check(workflow == "" && editor.Vsm.Text == vsm && editor.Images.Text == vsp, "Source application lost a tab or changed whitespace.");
            editor.Undo(); Check(editor.Vsm.Text == fixture && editor.Images.Text == "", "Source application was not one atomic undo.");
            editor.Redo(); Check(editor.Vsm.Text == vsm && editor.Images.Text == vsp, "Source redo failed.");
            Frame(); OpenSourceEditor(); sourceVsm!.SelectAll(); Type("discard me"); CloseSourceEditor();
            Check(editor.Vsm.Text == vsm, "Cancel applied draft source.");
            OpenSourceEditor(); sourceVsp!.SelectAll(); sourceVsp.Insert("static,broken"); ApplySourceEditor();
            Check(workflow == "source" && workflowError.Length > 0 && editor.Images.Text == vsp, "Malformed VSP partially changed the document.");
            CloseSourceEditor();

            editor.ApplySource(vsm.Replace("\r\n", "\n"), vsp);
            Check(editor.Vsm.NewLine == "\n", "Future visual edits ignored the source editor's new line endings.");
            editor.Undo(); Check(editor.Vsm.Text == vsm && editor.Vsm.NewLine == "\r\n", "Undo did not restore source line endings.");

            var buffer = new SourceTextBuffer("a\r\n中😀\nlast");
            buffer.Select(3); buffer.Move(1, false); buffer.Move(1, false);
            Check(buffer.Cursor == 6, "Source cursor split a Unicode surrogate pair.");
            buffer.Delete(true); buffer.Undo(); Check(buffer.Text == buffer.Original, "Unicode deletion/undo changed line endings.");
            var empty = VsmDocument.FromText(""); empty.Restore(VsmDocument.FromText("// test\r\n").Lines.ToArray());
            empty.Restore([]); Check(empty.NewLine == "\n", "Restoring an empty source retained a discarded newline style.");
            Console.WriteLine("PASS grouped GMK/source: target isolation, collapse/expand/raw, group move/resize/undo, original IDs/order, VSM/VSP drafts, Enter, local undo/redo, apply/cancel, atomic document undo and Unicode");
        }
        finally
        {
            if (workflow == "source") CloseSourceEditor();
            Current = oldSession; editor = oldEditor; preferences.GroupGimmickTracks = oldGrouped;
            ResetGimmickTracks(); SetClipSelection(null); editDrag = null; layoutRevision = -1;
            sourceOwner = null; sourceVsm = sourceVsp = null;
        }
    }

    void SmokeAddedGimmickTrackFocus()
    {
        if (editor == null) throw new InvalidOperationException("Track focus test needs an editor.");
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Frame() { renderedRevision = editor.Revision; Draw(1440, 940); }
        void Mouse(uint type, float x, float y) => Handle(new() { Type = type, Button = 1, X = x, Y = y });
        string source = editor.Vsm.Text;
        editor.Change("#54 track focus fixture", () =>
        {
            foreach (int target in new[] { -1, 1 })
                for (int i = 0; i < 24; i++)
                    editor.Vsm.Add(new(Guid.NewGuid(), 0, 1, "linear", "0", "1", "issue54_" + i, target));
        });
        expandedGimmickTracks.Add(1);
        activeMarker = null;
        foreach (bool grouped in new[] { true, false })
        {
            SetGimmickGrouping(grouped, persist: false); Frame();
            foreach (int target in new[] { -1, 1 })
            {
                string key = target + ":issue54_12";
                int index = editTracks.FindIndex(t => t.Key == key);
                trackScroll = index - 1; motion.Snap("timeline-track-scroll", trackScroll); Frame();
                int beforeScroll = trackScroll;
                var row = rowHits.Single(hit => hit.Track.Key == key).Rect;
                if (grouped)
                    Check(editTracks.FindIndex(t => t.GimmickGroup && t.Target == target) < trackScroll,
                        "#54 fixture must scroll the group header out of view.");
                transport.Seek(Current.Timeline.Bpm.Time(12));
                var ids = editor.Vsm.Clips.Select(c => c.Id).ToHashSet();
                float x = editorTracksRect.X - 16, y = row.Y + 17;
                Mouse(0x401, x, y); Frame(); Mouse(0x402, x, y); Frame();
                // Proxy insertion may also add its initial visibility event in the same undo step.
                var added = editor.Vsm.Clips.Single(c => !ids.Contains(c.Id) && c.Name == "issue54_12" && c.Proxy == target);
                Check(added.Name == "issue54_12" && added.Proxy == target && added.Beat == 12 && selectedClip == added.Id,
                    "#54 row + did not add/select an event on the requested track and beat.");
                Check(trackScroll == beforeScroll && rowHits.Single(hit => hit.Track.Key == key).Rect.Y == row.Y,
                    $"#54 row + jumped away from its child track (grouped={grouped}, target={target}).");
                editor.Undo(); Frame();
                Check(editor.Vsm.Clips.Select(c => c.Id).ToHashSet().SetEquals(ids), "#54 added events did not undo together.");
                editor.Redo(); Frame();
                Check(editor.Vsm.Clips.Any(c => c.Id == added.Id), "#54 added event did not redo.");
                editor.Undo(); Frame();

                trackScroll = 0; motion.Snap("timeline-track-scroll", 0);
                FocusAddedTrack("issue54_12", target, 12); Frame();
                Check(rowHits.Any(hit => hit.Track.Key == key), "#54 offscreen child track was not brought into view.");
            }
        }
        SetGimmickGrouping(true, persist: false);
        expandedGimmickTracks.Remove(1); layoutRevision = -1; Frame();
        FocusAddedTrack("issue54_12", 1, 12); Frame();
        Check(rowHits.Any(hit => hit.Track.GimmickGroup && hit.Track.Target == 1)
            && !expandedGimmickTracks.Contains(1), "#54 collapsed group did not retain header focus.");
        editor.Undo(); SetClipSelection(null);
        trackScroll = 0; beatStart = 0; motion.Snap("timeline-track-scroll", 0); Frame();
        Check(editor.Vsm.Text == source, "#54 focus checks changed the original source.");
        Console.WriteLine("PASS #54 track focus: row + preserves scroll in grouped/raw views, global/proxy children, offscreen focus, collapsed fallback and undo/redo");
    }

    void SmokeExpandedGimmickSummaries()
    {
        if (editor == null) throw new InvalidOperationException("Summary test needs an editor.");
        bool previous = preferences.ShowExpandedGimmickSummary, wasSettings = settings;
        string previousSection = settingsSection;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Frame() { layoutRevision = -1; renderedRevision = editor.Revision; Draw(1440, 940); }
        var clip = new VsmDocument.Clip(Guid.NewGuid(), 0, 2, "linear", "3", "4", "scrollspeed", -1);
        string source = editor.Vsm.Text;
        editor.Change("Summary fixture", () => editor.Vsm.Add(clip));
        try
        {
            bool InHeader()
            {
                return rowHits.Where(hit => hit.Track.GimmickGroup && hit.Track.Target == -1).Any(row =>
                    clipHits.Any(hit => hit.Id == clip.Id && hit.Rect.Y >= row.Rect.Y && hit.Rect.Y < row.Rect.Y + row.Rect.H));
            }
            Check(!new ViewerSettings().ShowExpandedGimmickSummary, "Expanded summaries must default to hidden.");
            preferences.ShowExpandedGimmickSummary = false; expandedGimmickTracks.Add(-1); Frame();
            Check(!InHeader() && clipHits.Any(hit => hit.Id == clip.Id), "Expanded header retained a summary or hid the raw child event.");
            preferences.ShowExpandedGimmickSummary = true; Frame();
            Check(InHeader(), "Expanded summary setting did not restore the header clip.");
            var copy = AppJson.Deserialize<ViewerSettings>(AppJson.Serialize(preferences, ViewerProject.Json), ViewerProject.Json)!;
            Check(copy.ShowExpandedGimmickSummary, "Expanded summary preference did not serialize.");
            preferences.ShowExpandedGimmickSummary = false; expandedGimmickTracks.Remove(-1); Frame();
            Check(InHeader() && !editTracks.Any(track => track.Key == clip.TrackKey), "Collapsed group lost its summary.");
            settings = true; settingsSection = "editor"; Frame();
            if (Environment.GetEnvironmentVariable("KG_EDITOR_SETTINGS_SCREENSHOT") is { } path) Canvas.SavePpm(UiTarget, path);
        }
        finally
        {
            preferences.ShowExpandedGimmickSummary = previous; expandedGimmickTracks.Add(-1);
            settings = wasSettings; settingsSection = previousSection; editor.Undo(); Frame();
        }
        Check(editor.Vsm.Text == source, "Summary visibility changed source events.");
        Console.WriteLine("PASS expanded summaries: hidden by default, child events retained, setting restores headers, collapsed summaries retained, preference serialization");
    }

    void SmokeChildTrackOrder()
    {
        if (editor == null) throw new InvalidOperationException("Track ordering needs an editor.");
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Frame() { layoutRevision = -1; renderedRevision = editor.Revision; Draw(1440, 940); }
        void Mouse(uint type, float x, float y) => Handle(new() { Type = type, Button = 1, X = x, Y = y });
        editor.Change("Track order fixture", () =>
        {
            foreach (string name in new[] { "xoffsetind3", "xoffsetind1", "xoffsetind10", "xoffsetind2" })
                editor.Vsm.Add(new(Guid.NewGuid(), 12, 1, "linear", "0", "1", name, -1));
        });
        expandedGimmickTracks.Add(-1); Frame();
        string root = GimmickTrackKey(-1);
        EditTrack[] Children() => editTracks.Where(track => TrackRoot(track) == root && track.Key != root).ToArray();
        Check(Children().Take(CoreGlobalTracks.Length).Select(track => track.Property).SequenceEqual(CoreGlobalTracks), "Core tracks lost the original default order.");
        Check(Children().Where(track => track.Property.StartsWith("xoffsetind", StringComparison.Ordinal)).Select(track => track.Property)
            .SequenceEqual(new[] { "xoffsetind3", "xoffsetind1", "xoffsetind10", "xoffsetind2" }), "Additional tracks lost their original document order.");
        var before = Children().Select(track => track.Key).ToArray();
        var roots = editTracks.Select(TrackRoot).Distinct().ToArray();
        string source = editor.Vsm.Text;
        int first = editTracks.FindIndex(track => track.Key == before[0]);
        trackScroll = first; motion.Snap("timeline-track-scroll", trackScroll); Frame();
        float x = editorTimelineRect.X + 45, y = editorTracksRect.Y;
        Mouse(0x401, x, y + 17); Frame();
        Check(draggedTrackKey == before[0] && draggedTrackParent == root, "Child label did not begin an independent child drag.");
        Mouse(0x400, x, y + 90); Frame(); Mouse(0x402, x, y + 90); Frame();
        Check(Children()[2].Key == before[0] && editor.Vsm.Text == source, "Child drop changed source events or did not reorder siblings.");
        Check(editTracks.Select(TrackRoot).Distinct().SequenceEqual(roots), "Child ordering moved its parent or another group.");
        var saved = AppJson.Deserialize<ViewerProject>(AppJson.Serialize(editor.Project, ViewerProject.Json), ViewerProject.Json)!;
        Check(saved.EditorTrackOrder.SequenceEqual(editor.Project.EditorTrackOrder), "Child order was not retained in project serialization.");
        expandedGimmickTracks.Remove(-1); Frame(); expandedGimmickTracks.Add(-1); Frame();
        Check(Children()[2].Key == before[0], "Collapse/expand lost child order.");
        editor.Undo(); Frame(); Check(Children().Select(track => track.Key).SequenceEqual(before), "Child order did not undo.");
        editor.Redo(); Frame(); Check(Children()[2].Key == before[0], "Child order did not redo.");
        editor.Undo(); Frame();
        BeginTrackOrder(Children()[0]);
        trackShownScroll = editTracks.FindIndex(track => track.GimmickGroup && track.Target == 0);
        mouseX = x; mouseY = y + 17; UpdateTrackDrop();
        Check(!trackDropValid, "Child was allowed to leave its parent group.");
        CancelTrackOrder(); editor.Undo();
        expandedGimmickTracks.Remove(-1); trackScroll = 0; motion.Snap("timeline-track-scroll", 0); Frame();
        Console.WriteLine("PASS child track order: original core/document order, label drag, parent isolation, collapse/expand, persistence, undo/redo and unchanged source");
    }
}
