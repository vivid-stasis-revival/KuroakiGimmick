namespace KuroakiGimmick.Core;

/// <summary>
/// Bounded detail for interactive previews of pathological repeated stacks.
/// Authoritative notes, scores, queries and exports never use this reduction.
/// </summary>
internal static class DensePreview
{
    internal const int Threshold = 100_000, MaxSamples = 262_144;
    internal sealed record Snapshot<T>(T[] Items, ushort[] Ordinals);

    internal static Snapshot<T>? Reduce<T, TKey>(IReadOnlyList<T> source,
        Func<T, double> time, Func<T, TKey> key, bool denseOwner = false) where TKey : notnull
    {
        if (!denseOwner && source.Count < Threshold) return null;
        var samples = new List<T>();
        var ordinals = new List<ushort>();
        var counts = new Dictionary<TKey, int>();
        int minimumSamples = 0;
        double previous = double.NaN;
        foreach (var item in source)
        {
            double at = time(item);
            if (at != previous) { counts.Clear(); previous = at; }
            var identity = key(item);
            int count = counts.GetValueOrDefault(identity);
            if (count >= ViewerSettings.MaxStackLayers) continue;
            counts[identity] = count + 1;
            // Abort early on ordinary/non-repeating charts. Do not keep a second
            // chart-sized array, or sample away sparse notes elsewhere in a song.
            if (samples.Count == MaxSamples) return null;
            samples.Add(item);
            ordinals.Add((ushort)count);
            if (count < ViewerSettings.MinStackLayers) minimumSamples++;
        }
        // Keep enough occurrences for every selectable limit. Filtering the
        // ordinal at query time avoids rescanning the full chart on each click.
        return minimumSamples * 2L < source.Count ? new(samples.ToArray(), ordinals.ToArray()) : null;
    }
}
