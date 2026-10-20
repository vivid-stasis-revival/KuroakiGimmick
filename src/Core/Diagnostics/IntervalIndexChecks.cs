namespace KuroakiGimmick.Core;

/// <summary>Headless oracle coverage for the compact interval tree used by notes and editor clips.</summary>
internal static class IntervalIndexChecks
{
    internal static void Run()
    {
        var random = new Random(18631);
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        foreach (int size in new[] { 0, 1, 2, 3, 7, 8, 9, 31, 32, 33, 255, 256, 257, 1000 })
        {
            var entries = new IntervalIndex<int>.Entry[size];
            for (int i = 0; i < size; i++)
            {
                double start = random.Next(-100, 100);
                double end = i % 7 == 0 ? 500 : start + random.Next(-3, 20);
                entries[i] = new(i, start, end, i);
            }
            var index = new IntervalIndex<int>(entries);
            void Compare(double start, double end)
            {
                // Preserve the existing predicate even for reversed windows and NaN bounds.
                var expected = entries.OrderBy(e => e.Start).ThenBy(e => e.Order)
                    .Where(e => !(Math.Max(e.Start, e.End) < start) && !(e.Start > end)).ToArray();
                int count = 0, previous = -1;
                var query = index.Overlap(start, end).GetEnumerator();
                while (query.MoveNext())
                {
                    Check(count < expected.Length && query.Current == expected[count++], "Interval query differs from linear oracle.");
                    Check(query.CurrentIndex > previous && index.Entries[query.CurrentIndex] == query.Current, "Interval source position changed.");
                    previous = query.CurrentIndex;
                }
                Check(count == expected.Length && !query.MoveNext(), "Interval query count/termination differs.");
            }
            for (int pass = 0; pass < 2; pass++)
            {
                for (int q = 0; q < 1000; q++) Compare(random.Next(-150, 600), random.Next(-150, 600));
                foreach (double bound in new[] { double.NegativeInfinity, -100, 0, 100, 500, double.PositiveInfinity, double.NaN })
                { Compare(bound, bound); Compare(bound, 0); Compare(0, bound); }
                for (int i = 0; i < size; i++) entries[i] = entries[i] with { End = random.Next(-200, 600), Value = i + size };
                Check(index.TryUpdate(entries), "Unchanged starts must permit incremental replacement.");
            }
            if (size > 0)
            {
                var before = index.Entries.ToArray();
                var moved = entries.ToArray(); moved[0] = moved[0] with { Start = moved[0].Start + .25 };
                Check(!index.TryUpdate(moved) && before.SequenceEqual(index.Entries), "Rejected start update mutated the index.");
                Check(!index.TryUpdate([]), "Mismatched replacement count was accepted.");
            }
        }
        // A long Hold crossing the window must survive pruning even with thousands of later chips.
        var dense = Enumerable.Range(0, 100_000).Select(i => new IntervalIndex<int>.Entry(i, i, i == 0 ? 200_000 : i, i)).ToArray();
        var tree = new IntervalIndex<int>(dense);
        long checksum = 0;
        for (int i = 0; i < 100; i++) foreach (var item in tree.Overlap(50_000, 50_010)) checksum += item.Value;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) foreach (var item in tree.Overlap(50_000, 50_010)) checksum += item.Value;
        Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Warm interval queries allocate.");
        Check(checksum > 0, "Interval query checksum was empty.");
        Console.WriteLine("PASS compact interval index: randomized linear oracle, boundaries, stable order, crossing holds, updates, zero query allocation");
    }
}
