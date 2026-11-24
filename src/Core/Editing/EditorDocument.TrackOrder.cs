// 调整编辑器轨道的显示顺序。

namespace KuroakiGimmick.Core.Editing;

public sealed partial class EditorDocument
{
    /// <summary>按保存的行顺序排列当前可见轨道；新轨道仍按原始默认顺序追加。</summary>
    /// <param name="keys">当前可用的稳定轨道键。</param>
    /// <returns>应用工程显示顺序后的轨道键数组。</returns>
    public string[] OrderedTracks(IEnumerable<string> keys)
    {
        var current = keys.Distinct(StringComparer.Ordinal).ToArray();
        var available = current.ToHashSet(StringComparer.Ordinal);
        var saved = Project.EditorTrackOrder.ToHashSet(StringComparer.Ordinal);
        return Project.EditorTrackOrder.Where(available.Contains).Concat(current.Where(k => !saved.Contains(k))).ToArray();
    }

    /// <summary>移动一个显示轨道/对象组；只写工程布局，保留全部源事件的身份、顺序与内容。</summary>
    /// <param name="key">被移动的稳定键。</param>
    /// <param name="beforeKey">插入位置所参照的轨道键。</param>
    /// <param name="currentKeys">当前存在的轨道键。</param>
    public void MoveTrack(string key, string? beforeKey, IEnumerable<string> currentKeys)
    {
        var ordered = OrderedTracks(currentKeys).ToList();
        if (!ordered.Contains(key) || key == beforeKey || beforeKey != null && !ordered.Contains(beforeKey)) return;
        var original = ordered.ToArray();
        ordered.Remove(key);
        ordered.Insert(beforeKey == null ? ordered.Count : ordered.IndexOf(beforeKey), key);
        if (original.SequenceEqual(ordered)) return;
        var visible = ordered.ToHashSet(StringComparer.Ordinal);
        ordered.AddRange(Project.EditorTrackOrder.Where(k => !visible.Contains(k)));
        Change("Reorder timeline tracks", () => Project.EditorTrackOrder = ordered);
    }
}
