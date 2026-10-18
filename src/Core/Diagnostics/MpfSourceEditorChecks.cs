using System.Text;
using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.Core;

internal static class MpfSourceEditorChecks
{
    internal static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "kg-mpf-edit-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidDataException("MPF source editing: " + name); checks++; }
        void Reject(Action action, string name)
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { checks++; return; }
            throw new InvalidDataException("Expected rejection: " + name);
        }
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "ENCORE.edit.vsmpf"), history = Path.Combine(root, "history");
            const string source = "// 中文\r\n::Main::\r\nset_mod_val(\"cover1\", 0.2);\r\n";
            File.WriteAllText(path, source, new UTF8Encoding(true));
            byte[] original = File.ReadAllBytes(path);
            var file = new MpfSourceFile(path);
            Check(file.Buffer.Text == source && !file.Buffer.Dirty, "source/newlines loaded unchanged");
            file.Buffer.SelectAll(); file.Buffer.Insert(source.Replace("0.2", "0.8"));
            Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(original), "draft did not write the file");
            Check(file.Save(history) && !file.Buffer.Dirty, "save marks draft clean");
            byte[] saved = File.ReadAllBytes(path);
            Check(saved.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) && File.ReadAllText(path).Contains("\r\n"), "BOM/newlines retained");
            Check(file.LastBackup != null && File.ReadAllBytes(file.LastBackup).AsSpan().SequenceEqual(original), "verified backup contains exact previous bytes");
            file.Buffer.Undo(); Check(file.Buffer.Text == source && file.Buffer.Dirty && File.ReadAllBytes(path).AsSpan().SequenceEqual(saved), "draft undo after save does not silently rewrite disk");
            file.Buffer.Redo(); Check(!file.Buffer.Dirty && !file.Save(history), "redo and unchanged save");
            file.Buffer.SelectAll(); file.Buffer.Insert("::Main::\nset_mod_val(;");
            Reject(() => file.Save(history), "syntax failure");
            Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(saved) && file.Buffer.Dirty, "syntax failure preserves file and draft");
            file.Buffer.SelectAll(); file.Buffer.Insert(source);
            File.WriteAllText(path, source + "// external\n");
            Reject(() => file.Save(history), "external modification conflict");
            Check(File.ReadAllText(path).EndsWith("// external\n"), "external changes were not overwritten");
            var removed = new MpfSourceFile(path); File.Delete(path);
            Reject(() => removed.Save(history), "external deletion conflict");
            var fresh = new MpfSourceFile(path);
            Check(!fresh.Exists && !File.Exists(path), "missing reference starts as an in-memory draft");
            Check(fresh.Save(history) && fresh.Exists && File.Exists(path), "new script saved explicitly");
            var collision = new MpfSourceFile(Path.Combine(root, "GLOBAL.new.vsmpf"));
            File.WriteAllText(collision.Path, source);
            Reject(() => collision.Save(history), "concurrent new file conflict");
            var blocked = new MpfSourceFile(path); blocked.Buffer.Insert("// edit\n");
            string blockedHistory = Path.Combine(root, "blocked"); File.WriteAllText(blockedHistory, "file");
            byte[] before = File.ReadAllBytes(path);
            Reject(() => blocked.Save(blockedHistory), "backup failure");
            Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(before) && !Directory.EnumerateFiles(root, "*.tmp").Any(), "backup failure retains original and cleans staging file");
            Console.WriteLine("PASS MPF source editor files: " + checks + " checks");
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }
}
