// 将谱面音符转换为可见性区间索引及供批处理使用的连续数据。

using System.Collections;
using System.Runtime.InteropServices;

namespace KuroakiGimmick.Core;

/// <summary>用于原生音符计算的顺序布局记录，保存秒数、轨道和保留位。</summary>
/// <param name="time">音符起始秒数。</param>
/// <param name="end">音符结束秒数。</param>
/// <param name="lane">原生轨道编号。</param>
[StructLayout(LayoutKind.Sequential)]
public readonly struct NoteRuntimeData(double time, double end, uint lane)
{
    /// <summary>音符起始与结束时刻，单位为秒。</summary>
    public readonly double Time = time, End = end;
    /// <summary>供原生侧读取的轨道编号和置零的 ABI 保留字段。</summary>
    public readonly uint Lane = lane, Reserved = 0;
}

/// <summary>会话级音符快照，在构建时一次完成拍数换算及原生数据准备。</summary>
public sealed class NoteIndex
{
    /// <summary>为可见性查询缓存拍数的音符。</summary>
    /// <param name="Note">原始音符。</param>
    /// <param name="Beat">起始拍。</param>
    /// <param name="EndBeat">结束拍。</param>
    public readonly record struct Item(Note Note, double Beat, double EndBeat);
    readonly IntervalIndex<Item> index;
    readonly IntervalIndex<Item>? previewIndex;
    readonly ushort[]? previewOrdinals;
    internal NoteRuntimeData[] Runtime { get; }
    internal NoteRuntimeData[]? PreviewRuntime { get; }
    /// <summary>已为重复层建立独立的预览索引。</summary>
    public bool HasDensePreview => previewIndex != null;
    /// <summary>已纳入索引的可绘制音符数，不含类型 3、4、5。</summary>
    public int Count => index.Entries.Count;
    /// <summary>构建区间查询和原生绘制数据，并按需要建立精简预览。</summary>
    /// <param name="source">谱面原始音符列表。</param>
    /// <param name="map">可选的拍秒映射，省略时缓存拍数为 NaN。</param>
    public NoteIndex(IReadOnlyList<Note> source, BpmMap? map = null)
    {
        int count = 0;
        for (int i = 0; i < source.Count; i++) if (source[i].Type is not (3 or 4 or 5)) count++;
        var items = new IntervalIndex<Item>.Entry[count];
        int next = 0;
        for (int i = 0; i < source.Count; i++)
        {
            var n = source[i];
            if (n.Type is 3 or 4 or 5) continue;
            items[next++] = new(new(n, map?.Beat(n.Time) ?? double.NaN, map?.Beat(Math.Max(n.Time, n.End)) ?? double.NaN), n.Time, n.End, i);
        }
        index = new(items, owned: true);
        var preview = DensePreview.Reduce(index.Entries, static e => e.Start,
            static e => (e.Value.Note.Type, e.Value.Note.Lane, e.End));
        if (preview != null)
        {
            previewIndex = new(preview.Items, owned: true);
            previewOrdinals = preview.Ordinals;
            PreviewRuntime = new NoteRuntimeData[preview.Items.Length];
            for (int i = 0; i < preview.Items.Length; i++)
            {
                var n = preview.Items[i].Value.Note;
                PreviewRuntime[i] = new(n.Time, n.End, unchecked((uint)(n.Type is 1 or 7 or 8 ? n.Lane + 4 : n.Lane)));
            }
        }
        Runtime = new NoteRuntimeData[count];
        for (int i = 0; i < Runtime.Length; i++)
        {
            var n = index.Entries[i].Value.Note;
            Runtime[i] = new(n.Time, n.End, unchecked((uint)(n.Type is 1 or 7 or 8 ? n.Lane + 4 : n.Lane)));
        }
    }
    /// <summary>按起始时刻查找最近的音符拍数，等距时选较早音符，空索引返回 null。</summary>
    /// <param name="beat">谱面位置，单位为拍。</param>
    /// <param name="map">在歌曲秒数与拍数之间转换的 BPM 映射。</param>
    /// <returns>最近的音符起始拍，空索引时为 null。</returns>
    public double? NearestBeat(double beat, BpmMap map)
    {
        int i = SortedSearch.LowerBound(index.Entries, map.Time(beat), static n => n.Start);
        double? left = i > 0 ? map.Beat(index.Entries[i - 1].Start) : null;
        double? right = i < Count ? map.Beat(index.Entries[i].Start) : null;
        return left == null ? right : right == null || beat - left.Value <= right.Value - beat ? left : right;
    }
    /// <summary>查询与指定时间窗口重叠的完整音符记录。</summary>
    /// <param name="earliestEnd">区间允许的最早结束秒数。</param>
    /// <param name="latestStart">区间允许的最晚起始秒数。</param>
    /// <returns>包含跨界长条的可见音符区间。</returns>
    public IntervalIndex<Item>.Query Visible(double earliestEnd, double latestStart) => index.Overlap(earliestEnd, latestStart);
    /// <summary>在启用预览限制时按层数过滤重叠音符，否则使用完整索引。</summary>
    /// <param name="earliestEnd">区间允许的最早结束秒数。</param>
    /// <param name="latestStart">区间允许的最晚起始秒数。</param>
    /// <param name="layers">预览允许的重复层数。</param>
    /// <param name="enabled">是否采用预览层数限制。</param>
    /// <returns>按预览规则枚举的候选区间。</returns>
    public PreviewQuery PreviewVisible(double earliestEnd, double latestStart,
        int layers = ViewerSettings.DefaultStackLayers, bool enabled = true)
        => new((enabled ? previewIndex ?? index : index).Overlap(earliestEnd, latestStart),
            enabled ? previewOrdinals : null, ViewerSettings.ValidStackLayers(layers));
    /// <summary>包含层数上限与可选层序号表的预览查询。</summary>
    /// <param name="query">底层区间查询。</param>
    /// <param name="ordinals">各项在重复层中的序号，可为空。</param>
    /// <param name="layers">预览允许的重复层数。</param>
    public readonly struct PreviewQuery(IntervalIndex<Item>.Query query, ushort[]? ordinals, int layers)
        : IEnumerable<IntervalIndex<Item>.Entry>
    {
        /// <summary>返回遍历当前区间查询的值类型枚举器。</summary>
        /// <returns>按重复层数筛选的音符枚举器。</returns>
        public PreviewEnumerator GetEnumerator() => new(query.GetEnumerator(), ordinals, layers);
        IEnumerator<IntervalIndex<Item>.Entry> IEnumerable<IntervalIndex<Item>.Entry>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    /// <summary>按重复层序号过滤可见音符的值类型枚举器。</summary>
    /// <param name="source">底层的区间枚举器。</param>
    /// <param name="ordinals">可选的层序号数组。</param>
    /// <param name="layers">预览允许的重复层数。</param>
    public struct PreviewEnumerator(IntervalIndex<Item>.Enumerator source, ushort[]? ordinals, int layers)
        : IEnumerator<IntervalIndex<Item>.Entry>
    {
        /// <inheritdoc/>
        public IntervalIndex<Item>.Entry Current => source.Current;
        /// <summary>最近命中项在底层有序数组中的索引。</summary>
        public int CurrentIndex => source.CurrentIndex;
        object IEnumerator.Current => Current;
        /// <inheritdoc/>
        public bool MoveNext()
        {
            while (source.MoveNext())
                if (ordinals == null || ordinals[source.CurrentIndex] < layers) return true;
            return false;
        }
        /// <inheritdoc/>
        public void Dispose()
        {
        }
        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">预览枚举器不支持复位。</exception>
        public void Reset() => throw new NotSupportedException();
    }
    /// <summary>仅枚举与指定时间窗口重叠的音符对象。</summary>
    /// <param name="earliestEnd">区间允许的最早结束秒数。</param>
    /// <param name="latestStart">区间允许的最晚起始秒数。</param>
    /// <returns>枚举命中区间内原始音符的视图。</returns>
    public Query Candidates(double earliestEnd, double latestStart) => new(Visible(earliestEnd, latestStart));
    /// <summary>将区间索引结果投影为原始音符的可枚举视图。</summary>
    /// <param name="query">要投影为音符的区间查询。</param>
    public readonly struct Query(IntervalIndex<Item>.Query query) : IEnumerable<Note>
    {
        /// <summary>返回遍历当前区间查询的值类型枚举器。</summary>
        /// <returns>提供原始音符的值类型枚举器。</returns>
        public Enumerator GetEnumerator() => new(query.GetEnumerator());
        IEnumerator<Note> IEnumerable<Note>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    /// <summary>遍历候选区间并返回对应的原始音符。</summary>
    /// <param name="source">查询区间的底层枚举器。</param>
    public struct Enumerator(IntervalIndex<Item>.Enumerator source) : IEnumerator<Note>
    {
        /// <inheritdoc/>
        public Note Current => source.Current.Value.Note;
        object IEnumerator.Current => Current;
        /// <inheritdoc/>
        public bool MoveNext() => source.MoveNext();
        /// <inheritdoc/>
        public void Dispose()
        {
        }
        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">音符枚举器不支持复位。</exception>
        public void Reset() => throw new NotSupportedException();
    }
}
