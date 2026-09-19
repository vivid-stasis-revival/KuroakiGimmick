using KuroakiGimmick.Core.Documentation;

namespace KuroakiGimmick.UI;

/// <summary>
/// 把 VSM 文档条目适配成既有的悬停 / 按住 W 帮助卡片格式。纯编辑器侧的解释层：时间轴标签仍与源文件逐字一致，
/// 这里只在悬停时补充说明，不改写任何原始名称。
/// </summary>
internal static class EditorReferenceHelp
{
    /// <summary>
    /// 按原始名称查文档。一个名称可能命中多个模板（例如以 b 结尾的字幕名会与"附加位置参数"的写法重合），
    /// 此时全部列出并给出提示，而不是擅自挑一个。summary 取第一条命中。
    /// </summary>
    internal static bool TryGet(string rawName, out EditorTrackHelp.Info info)
    {
        var catalogue = VsmReference.Shared;
        var matches = catalogue.MatchMod(rawName);
        if (matches.Count == 0)
        {
            info = new("", []);
            return false;
        }
        var first = matches[0].Entry;
        var body = new List<string>();
        if (matches.Count > 1)
            body.Add("此名称匹配多个模板。以 b 结尾的字幕名可能与附加位置参数重合。");
        foreach (var match in matches)
        {
            if (matches.Count > 1 || match.Arguments.Count > 0) body.Add("名称：" + match.Entry.Name);
            if (match.Arguments.Count > 0)
                body.Add("参数：" + string.Join("；", match.Arguments.Select(p => p.Key + " = " + p.Value)));
            if (match.CaseDifference)
                body.Add("大小写与文档名称不同。");
            body.AddRange(catalogue.Explain(match.Entry));
        }
        info = new(first.Summary, body.ToArray());
        return true;
    }
}
