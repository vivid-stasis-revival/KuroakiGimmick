namespace KuroakiGimmick.Core.Editing;

public sealed partial class VsmDocument
{
    /// <summary>Expand in place, retaining source order, untouched tokens and the original comment once.</summary>
    public Guid[] SplitLoops(IEnumerable<Guid> selection)
    {
        var ids = selection.ToHashSet();
        var picked = Clips.Where(c => ids.Contains(c.Id)).ToArray();
        if (picked.Length != ids.Count) throw new FormatException("A selected event no longer exists.");
        if (!picked.Any(c => c.RepeatEnd != null)) throw new FormatException("Select a loop to split.");
        if (picked.Sum(c => (long)c.RepeatCount) > 1_000_000)
            throw new FormatException("Split at most one million occurrences at a time.");
        var lines = new List<Line>();
        var result = new List<Guid>();
        foreach (var line in Lines)
        {
            if (line.Event is not { } clip || !ids.Contains(clip.Id)) { lines.Add(line); continue; }
            if (clip.RepeatEnd == null) { lines.Add(line); result.Add(clip.Id); continue; }
            for (int i = 0; i < clip.RepeatCount; i++)
            {
                var single = clip with
                {
                    Id = i == 0 ? clip.Id : Guid.NewGuid(), Beat = clip.Beat + i * clip.RepeatStep,
                    RepeatEnd = null, RepeatStep = 1, ParenthesizedRepeat = false
                };
                var source = i == 0 ? line : line with { Text = Body(line.Text).TrimEnd() };
                var rewritten = RewriteLine(source, single);
                lines.Add(rewritten with { Ending = i == clip.RepeatCount - 1 ? line.Ending : line.Ending.Length > 0 ? line.Ending : NewLine });
                result.Add(single.Id);
            }
        }
        Lines = lines;
        return result.ToArray();
    }

    /// <summary>
    /// Merge an uninterrupted source block with identical payload and evenly spaced occurrences.
    /// Do not cross other events: even another track can read dynamic values or fire callbacks.
    /// </summary>
    public Guid MergeLoop(IEnumerable<Guid> selection)
    {
        var ids = selection.ToHashSet();
        var positions = Lines.Select((line, index) => (line, index))
            .Where(x => x.line.Event is { } c && ids.Contains(c.Id)).ToArray();
        if (positions.Length != ids.Count || positions.Length < 2)
            throw new FormatException("Select at least two existing events to merge.");
        var clips = positions.Select(x => x.line.Event!).ToArray();
        var first = clips[0];
        if (clips.Any(c => c.Name != first.Name || c.Proxy != first.Proxy || c.Duration != first.Duration
            || c.Ease != first.Ease || c.From != first.From || c.To != first.To))
            throw new FormatException("Merge requires the same mod, proxy, duration, easing and From/To values.");
        int start = positions[0].index, end = positions[^1].index;
        for (int i = start; i <= end; i++)
        {
            var line = Lines[i];
            if (line.Event is { } c ? !ids.Contains(c.Id) : !string.IsNullOrWhiteSpace(Body(line.Text)))
                throw new FormatException("Other source events lie between the selection. Merge adjacent source events to preserve execution order.");
        }
        long count = clips.Sum(c => (long)c.RepeatCount);
        if (count > 1_000_000) throw new FormatException("A loop supports at most one million occurrences.");
        double step = first.RepeatCount > 1 ? first.RepeatStep : clips[1].Beat - first.Beat;
        if (!double.IsFinite(step) || step <= 0)
            throw new FormatException("Events must occur in increasing source order at a positive interval.");
        int occurrence = 0;
        double tolerance = Math.Min(1e-9, step * 1e-6);
        foreach (var clip in clips)
            for (int i = 0; i < clip.RepeatCount; i++, occurrence++)
                if (Math.Abs(clip.Beat + i * clip.RepeatStep - (first.Beat + occurrence * step)) > tolerance)
                    throw new FormatException("Events must be evenly spaced, with no gaps or duplicate beats.");
        var merged = first with { RepeatEnd = first.Beat + (count - 1) * step, RepeatStep = step, ParenthesizedRepeat = true };
        Validate(merged);
        if (merged.RepeatCount != count) throw new FormatException("This interval cannot be represented as a loop without losing occurrences.");
        var result = new List<Line>(Lines.Count);
        foreach (var line in Lines)
        {
            if (line.Event is not { } c || !ids.Contains(c.Id)) { result.Add(line); continue; }
            if (c.Id == first.Id) { result.Add(RewriteLine(line, merged)); continue; }
            // Keep comments from removed rows as standalone source comments.
            string body = Body(line.Text), comment = line.Text[body.Length..];
            if (comment.Length > 0) result.Add(new(comment, line.Ending, null));
        }
        Lines = result;
        return first.Id;
    }
}
