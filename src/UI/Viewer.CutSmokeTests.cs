using System.Text.Json.Nodes;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void SmokeCut()
    {
        var previousEditor = editor; var previousSession = Current; var previousMarker = activeMarker;
        const string source = "!obj:obj_custom_gimmick\n!proxies:2\n0,2,linear,0,10,prx,0\n4:8:2,1,linear,2,6,pry,0\n0,0,linear,1,1,pra,1\n";
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        string clipboard = "";
        bool Write(string text) { clipboard = text; return true; }
        try
        {
            Current = new(new ViewerProject { Profile = "core", RoomPreset = "none", GameUiEnabled = false }, editedVsm: source);
            editor = new(Current); editor.ApplySource(source, "");
            imageInspector = textInspector = false; selectedWindowEvent = -1; editDrag = null; activeMarker = null;
            var original = editor.Vsm.Clips.ToArray();
            SetClipSelection(original[0].Id); selectedClips.Add(original[1].Id);
            menuClip = original[0].Id; menuTrack = null;
            Check(BuildClipMenu().Any(item => item.Label == L.Get("CUT") + " (2)" && item.Enabled), "#56 batch context menu has no Cut");
            CloseClipMenu();
            long revision = editor.Revision;
            CopyOrCutSelection(true, _ => false);
            Check(editor.Vsm.Text == source && editor.Revision == revision && selectedClips.Count == 2, "Clipboard failure deleted events or changed history/selection");
            CopyOrCutSelection(true, Write);
            Check(editor.Vsm.Clips.Single().Id == original[2].Id && selectedClips.Count == 0, "Cut removed the wrong clips or left stale selection");
            var payload = EditorClipboard.Read(clipboard);
            Check(payload?.Events.Length == 2 && payload.Events[1].RepeatEnd == 8 && payload.Events[1].RepeatStep == 2,
                "Cut clipboard lost loop data");
            editor.Undo(); Check(editor.Vsm.Text == source, "Batch cut was not one undo");
            editor.Redo(); Check(editor.Vsm.Clips.Count() == 1, "Batch cut did not redo");
            activeMarker = editor.Mark(16);
            string beforePaste = editor.Vsm.Text;
            PasteClipboardText(clipboard);
            var pasted = editor.Vsm.Clips.Where(c => c.Proxy == 0).ToArray();
            Check(pasted.Length == 2 && pasted[0].Beat == 16 && pasted[1].Beat == 20 && pasted[1].RepeatEnd == 24
                && pasted.All(c => original.All(o => o.Id != c.Id)), "Cut/paste did not preserve spacing/repeats or allocate new IDs");
            editor.Undo(); Check(editor.Vsm.Text == beforePaste, "Paste after cut did not undo atomically");
            // Empty shortcuts must route to Cut without copying/deleting the active marker.
            SetClipSelection(null); selectedWindowEvent = -1;
            revision = editor.Revision;
            foreach (ushort modifiers in new ushort[] { 0x0040, 0x0400 })
            {
                message = ""; Handle(new() { Type = 0x300, Scan = 27, Modifiers = modifiers });
                Check(message == L.Get("Select something to cut first.") && editor.Revision == revision && activeMarker != null,
                    "Ctrl/Cmd+X did not route safely with an empty selection");
            }
            // Modal text input owns its shortcut, even while timeline clips remain selected.
            SetClipSelection(original[2].Id); OpenValue("Cut shortcut scope", "keep", _ => { });
            Handle(new() { Type = 0x300, Scan = 27, Modifiers = 0x0040 });
            Check(editor.Revision == revision && editor.Vsm.Clips.Single().Id == original[2].Id, "Text-field shortcut cut a timeline event");
            CloseValue();
            SetClipSelection(null); activeMarker = null;
            editor.Change("Cut window fixture", () => editor.AddWindow(new JsonObject { ["w"] = 0, ["op"] = "WindowResize", ["t"] = 2, ["dur"] = 1 }));
            selectedWindowEvent = 0;
            string windows = editor.Windows.Serialize();
            CopyOrCutSelection(true, Write);
            Check(editor.Windows.Events!.Count == 0 && EditorClipboard.Read(clipboard)?.Windows.Length == 1, "Window cut lost its payload or failed to remove the event");
            editor.Undo(); Check(editor.Windows.Serialize() == windows, "Window cut did not undo");
            Check(MenuCatalog.Groups.SelectMany(g => g.Items).Any(item => item.Command == MenuCommand.Cut), "Native Edit menu has no Cut");
            Console.WriteLine("PASS #56 cut: context/native menu, Ctrl/Cmd+X routing, clipboard failure retention, exact batch/window deletion, repeat/spacing preservation, paste, undo/redo and text-field isolation");
        }
        finally
        {
            CloseValue(); CloseClipMenu(); Current = previousSession; editor = previousEditor; activeMarker = previousMarker;
            SetClipSelection(null); selectedWindowEvent = -1; layoutRevision = -1;
        }
    }
}
