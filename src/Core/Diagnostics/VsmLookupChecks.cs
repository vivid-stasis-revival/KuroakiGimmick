using System.Diagnostics;
using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.Core;

internal static class VsmLookupChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidDataException("VSM lookup: " + name);
            checks++;
        }
        void Verify(VsmDocument document, string name)
        {
            Check(document.PreservedLineCount == document.Lines.Count(line => line.Event == null && !string.IsNullOrWhiteSpace(line.Text)), name + " preserved count");
            foreach (var clip in document.Clips)
            {
                int index = document.Lines.FindIndex(line => line.Event?.Id == clip.Id);
                Check(document.SourceLine(clip.Id) == index + 1, name + " source line");
                Check(ReferenceEquals(document.Find(clip.Id), document.Lines[index].Event), name + " first matching event");
            }
            Check(document.Find(Guid.Empty) == null && document.SourceLine(Guid.Empty) == 0, name + " missing event");
        }

        var empty = VsmDocument.FromText("");
        Verify(empty, "empty");
        empty.Lines.Add(new("// later", "\n", null));
        Verify(empty, "empty-list mutation");

        const string source = "!obj:obj_base_gimmick\r\n// preserved\r\n \t\n"
            + " 0, 1, linear, 0, 1, prx, -1 // first\r\n"
            + "1,1,linear,1,2,pry,-1\n2,1,linear,2,3,prx,-1\nmpf\n0,4,opaque_call()";
        var doc = VsmDocument.FromText(source);
        var original = doc.Lines.ToArray();
        var clips = doc.Clips.ToArray();
        Verify(doc, "initial");
        Check(doc.Text == source, "lookup does not rewrite source");
        doc.Replace(clips[0] with { Proxy = 0, To = "4" });
        Verify(doc, "replacement inserts proxy header");
        Check(doc.SourceLine(clips[0].Id) == 5 && doc.ProxyCount == 1, "header shifts cached positions");
        Check(doc.Lines[doc.SourceLine(clips[0].Id) - 1].Text.EndsWith(" // first"), "replacement preserves comment");
        doc.Replace(doc.Find(clips[0].Id)! with { To = "5" });
        Verify(doc, "replacement reuses positions");
        doc.Restore(original);
        Verify(doc, "restore original snapshot");
        Check(doc.Text == source, "restore retains exact source");

        var duplicate = doc.Lines[doc.SourceLine(clips[0].Id) - 1] with { Event = clips[0] with { To = "99" } };
        doc.Lines.Insert(0, duplicate);
        Verify(doc, "direct insert with duplicate ID");
        Check(ReferenceEquals(doc.Find(clips[0].Id), duplicate.Event), "duplicate ID preserves first-match semantics");
        doc.Lines.Reverse();
        Verify(doc, "direct reverse");
        doc.Lines.Sort((left, right) => StringComparer.Ordinal.Compare(left.Text, right.Text));
        Verify(doc, "direct sort");
        int replaced = doc.Lines.FindIndex(line => line.Event != null);
        var removedId = doc.Lines[replaced].Event!.Id;
        doc.Lines[replaced] = new("// replaced event", "\n", null);
        Verify(doc, "same-count event-to-comment replacement");
        Check(doc.SourceLine(removedId) == doc.Lines.FindIndex(line => line.Event?.Id == removedId) + 1, "removed or duplicate ID after replacement");
        doc.Lines[replaced] = new(" \t", "\n", null);
        Verify(doc, "same-count preserved-to-blank replacement");
        doc.Lines.RemoveAt(0); doc.Lines.Add(new("// appended", "\n", null));
        Verify(doc, "same-count remove and append");
        doc.Lines.Clear();
        Verify(doc, "direct clear");
        doc.Restore(original);
        doc.Delete(clips[1].Id);
        Verify(doc, "delete");
        doc.Add(clips[1] with { Id = Guid.NewGuid() });
        Verify(doc, "add before mpf");
        doc.SetHeader("obj", "obj_custom_gimmick");
        Verify(doc, "replace object header");
        doc.SetProxyCount(2);
        Verify(doc, "set proxy count");
        doc.Replace(doc.Find(clips[0].Id)! with { Proxy = 1 });
        doc.DeleteProxy(0);
        Verify(doc, "delete and compact proxy");

        var loop = VsmDocument.FromText("0:2:1,1,linear,0,1,prx,-1 // keep\n// tail");
        Verify(loop, "loop initial");
        var split = loop.SplitLoops(loop.Clips.Select(clip => clip.Id));
        Verify(loop, "split loop replaces line list");
        loop.MergeLoop(split);
        Verify(loop, "merge loop replaces line list");

        var images = VsmDocument.FromText("0,1,linear,0,1,imgx_picture,-1\n0,1,linear,0,1,prx,-1\n// tail");
        Verify(images, "images initial");
        images.DeleteImageEvents("picture");
        Verify(images, "delete image events");

        const int count = 12_000;
        var large = VsmDocument.FromText("");
        var ids = new Guid[count];
        for (int i = 0; i < count; i++)
        {
            var clip = new VsmDocument.Clip(ids[i] = Guid.NewGuid(), i, 1, "linear", "0", "1", "prx", 0);
            large.Lines.Add(new(VsmDocument.Compose(clip), "\n", clip));
        }
        // A legal late header exposes full-list scanning inside every Replace,
        // even after ID lookup itself has been indexed.
        large.Lines.Add(new("!proxies:1", "\n", null));
        Check(large.SourceLine(ids[^1]) == count && large.PreservedLineCount == 1, "large index built");
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++)
        {
            int index = i % count;
            if (large.SourceLine(ids[index]) != index + 1 || large.Find(ids[index])?.Id != ids[index] || large.PreservedLineCount != 1)
                throw new InvalidDataException("VSM lookup: repeated lookup result changed.");
        }
        Check(GC.GetAllocatedBytesForCurrentThread() - allocated == 0, "warm lookup and count allocate nothing");
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < count; i++) large.Replace(large.Find(ids[i])! with { To = "2" });
        watch.Stop();
        Check(large.Clips.All(clip => clip.To == "2") && large.SourceLine(ids[^1]) == count, "bulk replacements retain all records and positions");
        Console.WriteLine($"PASS VSM lookups: {checks} checks; {count:N0} replacements in {watch.Elapsed.TotalMilliseconds:F1} ms");
        return 0;
    }
}
