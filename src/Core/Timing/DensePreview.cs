namespace KuroakiGimmick.Core;

/// <summary>限制交互预览中重复堆叠的细节；权威音符、计分、完整查询和导出沿用原数据。</summary>
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
            if (at != previous)
            {
                counts.Clear();
                previous = at;
            }
            var identity = key(item);
            int count = counts.GetValueOrDefault(identity);
            if (count >= ViewerSettings.MaxStackLayers) continue;
            counts[identity] = count + 1;
            // 对普通或无重复的谱面尽早退出，避免保留第二份等规模数组，也避免其他位置的稀疏音符被抽掉。
            if (samples.Count == MaxSamples) return null;
            samples.Add(item);
            ordinals.Add((ushort)count);
            if (count < ViewerSettings.MinStackLayers) minimumSamples++;
        }
        // 保留所有可选层数需要的出现次数；查询时按层序号过滤，点击切换时无需重扫整谱。
        return minimumSamples * 2L < source.Count ? new(samples.ToArray(), ordinals.ToArray()) : null;
    }
}
