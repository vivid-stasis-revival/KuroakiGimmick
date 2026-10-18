using System.Text.Json.Nodes;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void SmokeProxyDelete()
    {
        if (editor == null) throw new InvalidOperationException("Proxy deletion needs an editor.");
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        const string source = "!obj:obj_custom_gimmick\r\n!proxies: 3 // count\r\n0,0,linear,1,1,pra,0\r\n0,0,linear,1,1,pra,1\r\n 2:8:2, 0, linear, 3, 4, prx, 2 // keep\r\n0,0,linear,1,1,scrollspeed,-1\r\n";
        editor.Change("Proxy delete fixture", () =>
        {
            editor.Vsm.Restore(VsmDocument.FromText(source).Lines.ToArray());
            editor.Windows.Root[WindowMotionConfig.BindingsKey] = JsonNode.Parse("[{},{},{}]");
            editor.Project.EditorTrackOrder = ["G:2", "2:prx", "G:1", "1:pra", "G:0", "-1:scrollspeed", "W:2:move"];
        });
        string windows = editor.Windows.Serialize();
        var order = editor.Project.EditorTrackOrder.ToArray();
        var remainingId = editor.Vsm.Clips.Single(c => c.Proxy == 2).Id;
        workflow = ""; imageInspector = textInspector = false; SetClipSelection(null);
        SetGimmickGrouping(true, persist: false);
        void Frame() { renderedRevision = editor.Revision; Draw(1440, 940); }
        Frame();
        int row = editTracks.FindIndex(t => t.GimmickGroup && t.Target == 1);
        trackScroll = Math.Max(0, row - 1); motion.Snap("timeline-track-scroll", trackScroll); Frame();
        float x = editorTracksRect.X - 97, y = editorTracksRect.Y + (row - trackScroll) * 35 + 17;
        Handle(new() { Type = 0x401, Button = 3, X = x, Y = y }); Frame();
        Handle(new() { Type = 0x402, Button = 3, X = x, Y = y }); Frame();
        Check(menuOpen && menuTrack?.Target == 1, "Proxy label right-click did not open its menu");
        int deleteItem = BuildClipMenu().FindIndex(item => item.Label == L.Get("DELETE PROXY") + " P1");
        Check(deleteItem >= 0, "Proxy menu has no delete action");
        float itemX = menuRect.X + 30, itemY = menuRect.Y + 6 + deleteItem * 27 + 10;
        Handle(new() { Type = 0x401, Button = 1, X = itemX, Y = itemY }); Frame();
        Handle(new() { Type = 0x402, Button = 1, X = itemX, Y = itemY }); Frame();
        Check(editor.Vsm.ProxyCount == 2 && draggedTrackKey == null, "#55 context deletion was missing or started a track drag");
        Check(editor.Vsm.Clips.Single(c => c.Id == remainingId).Proxy == 1 && editor.Vsm.Clips.Count() == 3,
            "Proxy deletion removed or retargeted the wrong events");
        Check(editor.Vsm.Text.Contains(" 2:8:2, 0, linear, 3, 4, prx, 1 // keep\r\n"), "Deletion changed repeat syntax/comments/whitespace");
        var binding = (JsonObject)editor.Windows.Bindings![1]!;
        Check(WindowMotionConfig.Integer(binding, "proxy") == 1 && WindowMotionConfig.Integer(binding, "window") == 102
            && WindowMotionConfig.Integer(binding, "source") == 1002, "Window binding identity changed");
        Check(editor.Project.EditorTrackOrder.SequenceEqual(new[] { "G:1", "1:prx", "G:0", "-1:scrollspeed", "W:2:move" }), "Track order did not follow proxy IDs");
        string deleted = editor.Vsm.Text;
        editor.Undo(); Frame();
        Check(editor.Vsm.Text == source && editor.Windows.Serialize() == windows && editor.Project.EditorTrackOrder.SequenceEqual(order), "Proxy deletion did not undo atomically");
        editor.Redo(); Frame(); Check(editor.Vsm.Text == deleted, "Proxy deletion did not redo");
        DeleteProxy(1); Frame(); DeleteProxy(0); Frame();
        Check(EditableProxyCount == 0 && editor.Vsm.Clips.All(c => c.Proxy == -1) && editor.Windows.Bindings!.Count == 0,
            "Last proxy could not be deleted");
        var chart = new Chart(); VsmReader.ReplaceModsText(chart, editor.Vsm.Text, "proxy-delete.vsm");
        Check(chart.Proxies == 0, "Zero-proxy source did not round-trip");
        CreateProxy(); Frame(); Check(EditableProxyCount == 1 && editor.Vsm.Clips.Any(c => c.Proxy == 0 && c.Name == "pra"), "Could not recreate P0");
        editor.Undo(); Frame(); Check(EditableProxyCount == 0, "Recreated proxy did not undo");
        Console.WriteLine("PASS #55: right-click label/menu deletion, source preservation, middle/last/zero proxies, binding/order remap, atomic undo/redo and recreate");
    }
}
