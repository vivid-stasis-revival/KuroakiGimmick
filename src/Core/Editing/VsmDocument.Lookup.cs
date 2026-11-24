// 缓存源行索引并随文档版本失效。

namespace KuroakiGimmick.Core.Editing;

public sealed partial class VsmDocument
{
    List<Line>? lookupLines;
    List<Line>.Enumerator lookupVersion;
    Dictionary<Guid, int>? sourcePositions;
    bool sourcePositionsValid, preservedLineCountValid, proxyDeclarationValid, proxyDeclarationPresent;
    int preservedLineCount;

    void InvalidateLookup()
    {
        sourcePositionsValid = preservedLineCountValid = proxyDeclarationValid = false;
        // 恢复、拆分或合并后会替换公开列表，立即清除旧枚举器标记，避免到下次界面查询前仍持有旧文档。
        lookupLines = null;
        lookupVersion = default;
        sourcePositions = null;
    }

    // 公开的 Lines 列表可以直接被修改。复制其枚举器并探测失效状态，可以发现同长度替换、Sort 与 Reverse；反复读取不推进原标记，也不依赖私有字段或调用方通知。
    void EnsureLookupVersion()
    {
        if (ReferenceEquals(lookupLines, Lines))
        {
            try
            {
                var probe = lookupVersion;
                probe.MoveNext();
                return;
            }
            catch (InvalidOperationException)
            {
            }
        }
        sourcePositionsValid = preservedLineCountValid = proxyDeclarationValid = false;
        lookupLines = Lines;
        lookupVersion = Lines.GetEnumerator();
    }

    int FindLineIndex(Guid id)
    {
        EnsureLookupVersion();
        if (!sourcePositionsValid)
        {
            sourcePositions ??= [];
            sourcePositions.Clear();
            int preserved = 0;
            for (int i = 0; i < Lines.Count; i++)
            {
                var line = Lines[i];
                // 即使外部通过公开列表插入重复 ID，仍保留 FirstOrDefault 和 FindIndex 选择首项的行为。
                if (line.Event is { } clip) sourcePositions.TryAdd(clip.Id, i);
                else if (!string.IsNullOrWhiteSpace(line.Text)) preserved++;
            }
            preservedLineCount = preserved;
            sourcePositionsValid = preservedLineCountValid = true;
        }
        return sourcePositions!.TryGetValue(id, out int position) ? position : -1;
    }

    int GetPreservedLineCount()
    {
        EnsureLookupVersion();
        if (!preservedLineCountValid)
        {
            // 只显示计数时无需为每个事件分配 ID 索引。
            preservedLineCount = Lines.Count(line => line.Event == null && !string.IsNullOrWhiteSpace(line.Text));
            preservedLineCountValid = true;
        }
        return preservedLineCount;
    }

    static bool IsProxyDeclaration(Line line) => Body(line.Text).TrimStart().StartsWith("!proxies:", StringComparison.Ordinal);

    bool HasProxyDeclaration()
    {
        EnsureLookupVersion();
        if (!proxyDeclarationValid)
        {
            proxyDeclarationPresent = Lines.Any(IsProxyDeclaration);
            proxyDeclarationValid = true;
        }
        return proxyDeclarationPresent;
    }

    void ReplaceIndexedLine(int index, Line replacement)
    {
        // Replace 在插入 proxy 头之后已重新定位 ID。改写已解析事件不影响 ID、位置与保留行数，刷新修改标记即可，避免一次批量编辑反复重建索引。
        if (proxyDeclarationValid && IsProxyDeclaration(Lines[index]) != IsProxyDeclaration(replacement)) proxyDeclarationValid = false;
        Lines[index] = replacement;
        lookupVersion = Lines.GetEnumerator();
    }
}
