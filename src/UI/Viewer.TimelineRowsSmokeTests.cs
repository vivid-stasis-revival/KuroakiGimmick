using System.Text.Json.Nodes;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void SmokeTimelineRows()
    {
        var oldSession = Current; var oldEditor = editor;
        bool grouped = preferences.GroupGimmickTracks, split = preferences.SplitOverlappingTimelineClips;
        const string source = "!obj:obj_custom_gimmick\n!proxies:2\n"
            + "0,4,linear,0,10,prx,0\n1,1,linear,2,3,prx,0\n4,2,linear,10,20,prx,0\n"
            + "8,0,linear,1,1,prx,0\n8,0,linear,2,2,prx,0\n0,4,linear,0,10,prx,1\n";
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Frame() { renderedRevision = editor!.Revision; Draw(1440, 940); }
        void Mouse(uint type, byte button, float x, float y) => Handle(new() { Type = type, Button = button, X = x, Y = y });
        void Show(EditTrack track)
        {
            int index = editTracks.FindIndex(t => t.Key == track.Key);
            trackScroll = Math.Max(0, index - 1); motion.Snap("timeline-track-scroll", trackScroll); Frame();
        }
        void CopyRow(EditTrack track)
        {
            Show(track);
            var row = rowHits.Single(h => h.Track.Key == track.Key);
            float x = editorTimelineRect.X + 50, y = row.Rect.Y + 17;
            Mouse(0x401, 3, x, y); Mouse(0x402, 3, x, y); Frame();
            Check(menuOpen && menuClip == null && menuTrack?.Key == track.Key, "Row label did not open the correct context menu");
            int item = BuildClipMenu().FindIndex(i => i.Label == L.Get("DUPLICATE ROW"));
            Check(item >= 0 && BuildClipMenu()[item].Enabled, "Row menu did not offer duplication");
            x = menuRect.X + 30; y = menuRect.Y + 6 + item * 27 + 10;
            Mouse(0x401, 1, x, y); Frame(); Mouse(0x402, 1, x, y); Frame();
        }
        try
        {
            Check(new ViewerSettings().SplitOverlappingTimelineClips && AppJson.Deserialize<ViewerSettings>("{}", ViewerProject.Json)!.SplitOverlappingTimelineClips,
                "Automatic overlap rows must default on for new and old settings");
            SetSplitOverlappingTimelineClips(true, persist: false);
            Current = new(new ViewerProject { Profile = "core", RoomPreset = "none", GameUiEnabled = false }, editedVsm: source);
            editor = new(Current); editor.ApplySource(source, "");
            mouseX = 1000; mouseY = 50; displayedHelpTrack = hoveredEditTrack = null;
            trackHelpVisualActive = trackHelpHitRectValid = false;
            SetClipSelection(null); ResetGimmickTracks(); SetGimmickGrouping(false, persist: false);
            beatStart = 0; pixelsPerBeat = 48; Frame();
            var clips = editor.Vsm.Clips.Where(c => c.Proxy == 0).ToArray();
            Check(editTracks.Count(t => t.Target == 0 && t.Property == "prx") == 2, "Overlapping channels did not split into two rows");
            Check(clipRowLanes[clips[0].Id] == clipRowLanes[clips[2].Id], "Touching clips unnecessarily split");
            Check(clipRowLanes[clips[3].Id] != clipRowLanes[clips[4].Id], "Simultaneous point events still overlap");
            Check(editor.Vsm.Text == source, "Automatic row layout changed the source");
            SetSplitOverlappingTimelineClips(false, persist: false); Frame();
            Check(editTracks.Count(t => t.Target == 0 && t.Property == "prx") == 1, "Disabling overlap rows left duplicate tracks");
            var unsplit = editTracks.Single(t => t.Target == 0 && t.Property == "prx");
            Show(unsplit);
            Check(ClipsOnTimelineRow(unsplit).Length == clips.Length && clips.All(c => clipHits.Any(h => h.Id == c.Id)),
                "Disabling overlap rows hid events or excluded them from row copy");
            var savedSettings = AppJson.Deserialize<ViewerSettings>(AppJson.Serialize(preferences, ViewerProject.Json), ViewerProject.Json)!;
            Check(!savedSettings.SplitOverlappingTimelineClips, "Disabled overlap setting did not serialize");
            SetSplitOverlappingTimelineClips(true, persist: false); Frame();
            Check(editTracks.Count(t => t.Target == 0 && t.Property == "prx") == 2 && editor.Vsm.Text == source,
                "Re-enabling overlap rows changed the source or did not restore sibling rows");
            var second = editTracks.Single(t => t.Target == 0 && t.Property == "prx" && t.Lane == 1);
            Show(second);
            var hit = clipHits.Single(h => h.Id == clips[1].Id);
            Mouse(0x400, 1, hit.Rect.X + 2, hit.Rect.Y + 10); Frame();
            Mouse(0x401, 1, hit.Rect.X + 2, hit.Rect.Y + 10); Frame();
            Mouse(0x402, 1, hit.Rect.X + 2, hit.Rect.Y + 10); Frame();
            Check(selectedClip == clips[1].Id, $"Second row selected wrong clip: selected={selectedClip}, expected={clips[1].Id}, hit={hit.Rect}, tracks={editorTracksRect}, overlay={UiOverlayVisible}, help={trackHelpVisualActive}, drag={editDrag?.Clip?.Id}, marquee={marqueeActive}");
            var first = editTracks.Single(t => t.Target == 0 && t.Property == "prx" && t.Lane == 0);
            var originals = ClipsOnTimelineRow(first); var ids = editor.Vsm.Clips.Select(c => c.Id).ToHashSet();
            CopyRow(first);
            var copies = editor.Vsm.Clips.Where(c => !ids.Contains(c.Id)).ToArray();
            Check(copies.Length == originals.Length && copies.Zip(originals).All(p => p.First with { Id = p.Second.Id } == p.Second),
                "Copy row changed timing/values or copied other rows");
            Check(editTracks.Count(t => t.Target == 0 && t.Property == "prx") >= 3, "Copied row is hidden under the original");
            string copied = editor.Vsm.Text;
            editor.Undo(); Frame(); Check(editor.Vsm.Text == source, "Row copy did not undo atomically");
            editor.Redo(); Frame(); Check(editor.Vsm.Text == copied, "Row copy did not redo");
            SetGimmickGrouping(true, persist: false); Frame();
            Check(editTracks.Count(t => t.GimmickGroup && t.Target == 0) == 1
                && editTracks.Single(t => t.GimmickGroup && t.Target == 0).Label == "Proxy 0", "Collapsed Proxy header split into multiple parents");
            var parent = editTracks.Single(t => t.GimmickGroup && t.Target == 0);
            Check(ClipsOnTimelineRow(parent).Length == editor.Vsm.Clips.Count(c => c.Proxy == 0), "Parent summary omitted child lanes");
            Check(gimmickGroupsByClip.Values.All(g => g.Select(c => c.Name).Distinct().Count() == g.Length), "Same-property duplicates merged into one group");
            expandedGimmickTracks.Add(0); layoutRevision = -1; Frame();
            Check(editTracks.Count(t => !t.GimmickGroup && t.Target == 0 && t.Property == "prx") >= 3, "Expanded groups lost split rows");
            Check(editTracks.Count(t => t.GimmickGroup && t.Target == 0) == 1, "Expanded Proxy has multiple parents");
            var children = editTracks.Where(t => !t.GimmickGroup && t.Target == 0 && t.Property == "prx").ToArray();
            Check(children.All(t => TrackRoot(t) == parent.Key), "Split property rows are not siblings under their Proxy");
            bool oldSummary = preferences.ShowExpandedGimmickSummary;
            preferences.ShowExpandedGimmickSummary = true; layoutRevision = -1; Frame();
            Check(editTracks.Count(t => t.GimmickGroup && t.Target == 0) == 1, "Showing summary split the parent");
            preferences.ShowExpandedGimmickSummary = oldSummary; layoutRevision = -1;
            editor.Undo(); Frame();
            editor.Change("Window row fixture", () => editor.Windows.Root[WindowMotionConfig.EventsKey] = JsonNode.Parse(
                "[{\"w\":0,\"op\":\"WindowResize\",\"t\":0,\"dur\":2},{\"w\":0,\"op\":\"WindowResize\",\"t\":1,\"dur\":2}]"));
            Frame();
            Check(editTracks.Count(t => t.Window && t.Target == 0) == 2, "Overlapping window events did not split");
            var window = editTracks.First(t => t.Window);
            string beforeWindows = editor.Windows.Serialize();
            CopyRow(window);
            Check(editor.Windows.Events!.Count == 3, "Window row copy did not duplicate precisely one row");
            editor.Undo(); Frame(); Check(editor.Windows.Serialize() == beforeWindows, "Window row copy did not undo");
            // A right drag remains a pan and never opens a destructive menu.
            CloseClipMenu();
            float px = editorTracksRect.X + 50, py = editorTracksRect.Y + 15;
            Mouse(0x401, 3, px, py); Mouse(0x400, 3, px + 30, py); Mouse(0x402, 3, px + 30, py); Frame();
            Check(!menuOpen, "Right drag opened a context menu");
            Console.WriteLine("PASS timeline rows: default-on setting, disable/enable without lost clips, settings serialization, overlap packing, touching/point events, independent hit targets, label context copy, preserved timing, undo/redo, single parent/sibling child rows, grouped/raw views, windows and right-drag pan");
        }
        finally
        {
            CloseClipMenu(); Current = oldSession; editor = oldEditor; SetSplitOverlappingTimelineClips(split, persist: false); SetGimmickGrouping(grouped, persist: false); layoutRevision = -1;
        }
    }
}
