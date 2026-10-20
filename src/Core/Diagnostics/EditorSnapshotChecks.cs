using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.Core;

internal static class EditorSnapshotChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var random = new Random(8241);
        for (int test = 0; test < 1000; test++)
        {
            var left = new List<VsmDocument.Line>(); var right = new List<VsmDocument.Line>();
            string text = string.Concat(Enumerable.Range(0, random.Next(1, 100)).Select(i => "a\r\n雪🎵"[random.Next(6)]));
            foreach (var lines in new[] { left, right })
            {
                for (int at = 0; at < text.Length;)
                {
                    int length = Math.Min(text.Length - at, random.Next(1, 7));
                    int split = random.Next(length + 1);
                    lines.Add(new(text.Substring(at, split), text.Substring(at + split, length - split), null));
                    at += length;
                    if (random.Next(4) == 0) lines.Add(new("", "", null));
                }
            }
            Check(VsmDocument.TextEquals(left, right), "Equal partitioned source text differed.");
            right.Add(new("x", "", null));
            Check(!VsmDocument.TextEquals(left, right), "Unequal partitioned source text compared equal.");
        }
        Check(VsmDocument.TextEquals([], [new("", "", null)]), "Empty line partition differs.");
        var document = new EditorDocument(Session.Empty());
        var original = document.Vsm.Lines.ToArray();
        Check(!document.Dirty, "New editor is dirty.");
        document.Change("empty partition", () => document.Vsm.Lines.Add(new("", "", null)));
        Check(!document.Dirty, "Equivalent source partition is dirty.");
        document.Undo(); Check(!document.Dirty, "Undo to clean source is dirty.");
        document.Redo(); Check(!document.Dirty, "Redo equivalent source is dirty.");
        document.Change("text change", () => document.Vsm.Lines.Add(new("# changed", "\n", null)));
        Check(document.Dirty, "Changed source is clean.");
        document.Undo(); Check(!document.Dirty, "Undo text change is dirty.");
        document.Change("restore", () => document.Vsm.Restore(original));
        Check(!document.Dirty, "Restored original source is dirty.");
        Console.WriteLine("PASS editor source snapshots: exact partitioned-text oracle and dirty/undo/redo without a full text copy");
    }
}
