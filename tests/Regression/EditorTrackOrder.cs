using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;

internal static class EditorTrackOrder
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        string root = Path.GetTempPath();
        if (OperatingSystem.IsMacOS() && root.StartsWith("/var/")) root = "/private" + root;
        root = Path.Combine(root, "kuroaki-track-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string file = Path.Combine(root, "FINALE.vsm");
            File.WriteAllText(file, "!obj:obj_base_gimmick\n!proxies:1\n0,0,linear,1,1,uialpha,-1\n0,0,linear,0,0,pry,0\n0,0,linear,1,1,uialpha,-1\n");
            var session = new Session(new ViewerProject { Gimmick = file, Bpm = 60, RoomPreset = "none", GameUiEnabled = false, Profile = "core" });
            var doc = new EditorDocument(session);
            string[] keys = ["-1:scrollspeed", "-1:uialpha", "0:prx", "0:pry", "I:hero", "T:caption"];
            string source = doc.Vsm.Text;
            var ids = doc.Vsm.Clips.Select(c => c.Id).ToArray();
            doc.MoveTrack("0:pry", "-1:scrollspeed", keys);
            string[] expected = ["0:pry", "-1:scrollspeed", "-1:uialpha", "0:prx", "I:hero", "T:caption"];
            Check(doc.OrderedTracks(keys).SequenceEqual(expected) && doc.Dirty, "Track order / dirty flag");
            Check(doc.Vsm.Text == source && doc.Vsm.Clips.Select(c => c.Id).SequenceEqual(ids), "Reordering changed source events or identities");
            doc.Undo(); Check(!doc.Dirty && doc.OrderedTracks(keys).SequenceEqual(keys), "Track order undo / clean baseline");
            doc.Redo(); Check(doc.Dirty && doc.OrderedTracks(keys).SequenceEqual(expected), "Track order redo");
            long revision = doc.Revision;
            doc.MoveTrack("0:pry", "-1:scrollspeed", keys);
            doc.MoveTrack("0:pry", "0:pry", keys);
            Check(doc.Revision == revision, "No-op track drag created an undo step");
            Check(doc.OrderedTracks(keys.Append("-1:wave")).Last() == "-1:wave", "New tracks must be retained");
            var copy = doc.Project.Copy(); copy.EditorTrackOrder.Clear();
            Check(doc.Project.EditorTrackOrder.Count == keys.Length, "Project copy shared track-order storage");
            string save = doc.SaveCopy(Path.Combine(root, "ordered.sgv.json"));
            Check(!doc.Dirty, "Saving track order did not clear dirty flag");
            var reopened = new EditorDocument(Session.Load(save));
            Check(reopened.OrderedTracks(keys).SequenceEqual(expected) && !reopened.Dirty, "Track order did not survive save / reopen");
            doc.Undo(); Check(doc.Dirty, "Undo after save must become dirty");
            doc.Redo(); Check(!doc.Dirty, "Redo to saved order must become clean");
            try { doc.Change("failed layout", () => { doc.Project.EditorTrackOrder.Clear(); throw new FormatException("test"); }); }
            catch (FormatException) { }
            Check(!doc.Dirty && doc.OrderedTracks(keys).SequenceEqual(expected), "Layout mutation did not roll back on failure");
            Console.WriteLine("PASS #43: layout-only track ordering, event identity, no-op drag, new tracks, undo/redo, copy isolation and save/reopen");
        }
        finally { Directory.Delete(root, true); }
    }
}
