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
        // Restore/split/merge replace the public List. Do not retain the old
        // source through the mutation stamp until some later UI lookup occurs.
        lookupLines = null;
        lookupVersion = default;
        sourcePositions = null;
    }

    // Lines is intentionally a public mutable List. Its fail-fast enumerator is a
    // public, allocation-free mutation stamp, including same-count indexer edits,
    // Sort and Reverse. Probe a copy so repeated reads never consume the stamp.
    // This avoids private List fields and does not require callers to notify us.
    void EnsureLookupVersion()
    {
        if (ReferenceEquals(lookupLines, Lines))
        {
            try { var probe = lookupVersion; probe.MoveNext(); return; }
            catch (InvalidOperationException) { }
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
                // Preserve FirstOrDefault/FindIndex behavior even if a caller
                // inserted duplicate IDs directly through the public list.
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
            // Merely displaying the count must not allocate a per-event ID map.
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
        // Replace resolves the ID after any inserted proxy header, so the map is
        // current here. Rewriting a parsed event keeps its ID, position and the
        // preserved-line count unchanged. Refresh the stamp instead of turning
        // a K-event edit back into K full-document index rebuilds.
        if (proxyDeclarationValid && IsProxyDeclaration(Lines[index]) != IsProxyDeclaration(replacement)) proxyDeclarationValid = false;
        Lines[index] = replacement;
        lookupVersion = Lines.GetEnumerator();
    }
}
