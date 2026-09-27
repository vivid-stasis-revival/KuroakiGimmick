using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.UI;

/// <summary>
/// 只读文档，不是"已支持效果"的注册表 —— 收录不等于预览器实现了它。JSON 由随附的两份 Markdown 生成；
/// 查询和搜索都不会改动编辑器文档。数据以嵌入资源为准，防止旧的外部 Assets 目录覆盖更新的帮助内容。
/// </summary>
internal static class EditorManual
{
    internal sealed class Entry
    {
        public string Id { get; set; } = "";
        public string Key { get; set; } = "";
        public string Category { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Short { get; set; } = "";
        public string[] Detail { get; set; } = [];
        public string Source { get; set; } = "";
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string Scope { get; set; } = "";
        public string Pattern { get; set; } = "";
        public string Citation => Source.Length == 0 ? "编辑器说明（非游戏语法）" :
            Source + " · L" + StartLine + (EndLine == StartLine ? "" : "–" + EndLine);
        public string SearchText => Key + "\n" + Category + "\n" + Short + "\n" + string.Join("\n", Detail);
    }

    internal sealed class Data
    {
        public int Schema { get; set; }
        public string Revision { get; set; } = "";
        public string[] Categories { get; set; } = [];
        public Entry[] Entries { get; set; } = [];
    }

    /// <summary>模板条目及其匹配正则；按 Key 去掉占位符后的长度降序排列，让更具体的模板先命中。</summary>
    sealed record Family(Entry Entry, Regex Pattern);
    /// <summary>RawName 始终是源文件里的原始拼写；CaseDiffers 表示只有大小写对得上，Alternatives 是同时命中的其他模板。</summary>
    internal sealed record Match(Entry Entry, string RawName, bool CaseDiffers, string[] Alternatives);
    static readonly Lazy<Data> Document = new(Load);
    static readonly Lazy<Family[]> Families = new(() => Entries.Where(e => e.Kind == "mod" && e.Pattern.Length > 0)
        .OrderByDescending(e => Regex.Replace(e.Key, @"\[[^\]]+\]", "").Length)
        .Select(e => new Family(e, new Regex(e.Pattern, RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100)))).ToArray());
    internal static Entry[] Entries => Document.Value.Entries;
    internal static string[] Categories => Document.Value.Categories;
    internal static string Revision => Document.Value.Revision;
    internal static string LoadError { get; private set; } = "";

    /// <summary>
    /// 首次访问时懒加载嵌入的手册。schema 不符、条目为空或 Id 重复都视为损坏并整体丢弃；
    /// 失败只记录到 LoadError，绝不让文档缺失影响播放或编辑。
    /// </summary>
    static Data Load()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("KuroakiGimmick.EditorManual.json")
                ?? throw new InvalidDataException("Missing embedded VSM manual.");
            var data = AppJson.Deserialize<Data>(stream)
                ?? throw new InvalidDataException("Empty VSM manual.");
            if (data.Schema != 1 || data.Entries.Length == 0 ||
                data.Entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != data.Entries.Length)
                throw new InvalidDataException("Invalid VSM manual schema or duplicate entry IDs.");
            Console.Error.WriteLine($"[docs] {data.Revision}: {data.Entries.Length} entries loaded from supplied documents");
            return data;
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            Console.Error.WriteLine("[docs] Manual unavailable: " + ex.Message);
            return new();
        }
    }

    /// <summary>优先精确拼写。模板帮助只是解释，绝不为运行时解析器制造别名。</summary>
    internal static Match? LookupMod(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 4096) return null;
        var exact = Entries.FirstOrDefault(e => e.Kind == "mod" && e.Pattern.Length == 0 && e.Key == name);
        if (exact != null) return new(exact, name, false, []);
        var matches = Families.Value.Where(f => f.Pattern.IsMatch(name)).Select(f => f.Entry).ToArray();
        if (matches.Length > 0)
            return new(matches[0], name, false, matches.Skip(1).Select(e => e.Key).ToArray());
        // 帮助可以指向只差大小写的近似条目，但必须显式声明拼写不一致，不能让用户以为它是别名。
        var near = Entries.FirstOrDefault(e => e.Kind == "mod" && e.Pattern.Length == 0 &&
            string.Equals(e.Key, name, StringComparison.OrdinalIgnoreCase));
        return near == null ? null : new(near, name, true, []);
    }

    /// <summary>拼出帮助卡片正文。原始名与文档名不一致、大小写不一致、模板歧义都必须显式说明，不能悄悄按文档名展示。</summary>
    internal static string[] Describe(Match match)
    {
        var lines = new List<string>();
        if (match.Entry.Key != match.RawName)
            lines.Add("文档条目：" + match.Entry.Key + "；当前原始名字：" + match.RawName + "。");
        if (match.CaseDiffers)
            lines.Add("【大小写提示】仅找到大小写不同的文档名字，未将其视为游戏别名，也不会改写源文件。");
        lines.AddRange(match.Entry.Detail);
        if (match.Alternatives.Length > 0)
            lines.Add("【模板歧义】该名字也能匹配：" + string.Join("、", match.Alternatives) +
                "。显示更具体的模板；实际目标仍由源格式和资源声明决定。");
        lines.Add("来源：" + match.Entry.Citation);
        lines.Add("【编辑器说明】文档收录不代表已实现预览。支持状态以兼容报告为准；F1 打开语法手册。");
        return lines.ToArray();
    }

    /// <summary>同时搜索原始 identifier 与源文档正文；精确命中排在整篇文档之前。</summary>
    internal static Entry[] Search(string query, string category = "")
    {
        string term = query.Trim();
        string[] tokens = term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var pool = Entries.Where(e => category.Length == 0 || e.Category == category);
        if (term.Length == 0) return pool.ToArray();
        var resolved = LookupMod(term);
        return pool.Where(e => e.Id == resolved?.Entry.Id ||
                tokens.All(t => e.SearchText.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.Id == resolved?.Entry.Id || string.Equals(e.Key, term, StringComparison.OrdinalIgnoreCase) ? 0 :
                e.Key.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 :
                e.Kind == "source" ? 4 : e.Short.Contains(term, StringComparison.OrdinalIgnoreCase) ? 2 : 3)
            .ToArray();
    }
}
