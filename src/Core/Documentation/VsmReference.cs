using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KuroakiGimmick.Core.Documentation;

/// <summary>
/// 只读文档。这份目录绝不能用来注册 mod、校验保存、选择对象或提供求值器默认值。
/// 文档里写了某个特性，不代表渲染器具备该能力。生成的数据完整保留了输入的两份源文件。
/// </summary>
internal sealed class VsmReference
{
    /// <summary>被收录的源文件原文与其 SHA-256。全文一并保留，文档面板据此显示原始上下文而非重新排版的摘要。</summary>
    internal sealed class SourceDocument
    {
        // 以下几个类型都是 System.Text.Json 的反序列化目标：无参构造 + init-only 属性，实例一经生成即视为只读。
        public SourceDocument() { }
        public string File { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public int LineCount { get; init; }
        public string Text { get; init; } = "";
    }

    /// <summary>文档正文的一个排版块（段落、代码、表格）。Kind 决定该看哪几个字段，其余字段为空。</summary>
    internal sealed class Block
    {
        public Block() { }
        public string Kind { get; init; } = "paragraph";
        public string Text { get; init; } = "";
        public string Language { get; init; } = "";
        public string[] Columns { get; init; } = [];
        public string[][] Rows { get; init; } = [];
        public string[] RowLinks { get; init; } = [];
    }

    /// <summary>
    /// 一个文档条目。Scope 取 global/proxy/custom，供编辑器判断该 mod 需要的 proxy 目标；MatchPattern 用来匹配带参数的 mod 名。
    /// StartLine/EndLine 是源文档里的 1 基行号，仅供定位与显示。
    /// </summary>
    internal sealed class Entry
    {
        public Entry() { }
        public string Id { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
        public string Source { get; init; } = "";
        public int StartLine { get; init; }
        public int EndLine { get; init; }
        public string Summary { get; init; } = "";
        public string[] Body { get; init; } = [];
        public string[] Context { get; init; } = [];
        public string[] Notes { get; init; } = [];
        public string ParentId { get; init; } = "";
        public string MatchPattern { get; init; } = "";
        public string Scope { get; init; } = "";
        public string[] Cells { get; init; } = [];
        public Block[] Blocks { get; init; } = [];

        public string SourceLabel => Source.Length == 0 ? "" :
            $"{Source} · L{StartLine}" + (EndLine == StartLine ? "" : $"–L{EndLine}");
    }

    /// <summary>反序列化出来的整份文档数据；SchemaVersion 与 Revision 用于确认这份资源与当前程序版本配套。</summary>
    internal sealed class Data
    {
        public Data() { }
        public int SchemaVersion { get; init; }
        public string Revision { get; init; } = "";
        public SourceDocument[] Sources { get; init; } = [];
        public Entry[] Entries { get; init; } = [];
    }

    /// <summary>一次名称匹配的结果。<c>CaseDifference</c> 表示只有大小写不同——仍算命中，但要提示用户，源格式对大小写是敏感的。</summary>
    internal sealed record Match(Entry Entry, IReadOnlyDictionary<string, string> Arguments, bool CaseDifference);
    /// <summary>预编译的匹配项：区分大小写与忽略大小写各一份正则，避免每次查询都重新编译。</summary>
    sealed record PatternEntry(Entry Entry, Regex Exact, Regex IgnoreCase);

    const string ResourceName = "KuroakiGimmick.VsmReference.json";
    static readonly Lazy<VsmReference> shared = new(LoadBundled);
    internal static VsmReference Shared => shared.Value;
    readonly Dictionary<string, Entry> byId;
    readonly Dictionary<string, SourceDocument> sources;
    readonly Dictionary<string, string[]> sourceLines;
    readonly Dictionary<string, string> searchText;
    readonly PatternEntry[] patterns;
    internal IReadOnlyList<Entry> Entries { get; }
    internal IReadOnlyCollection<SourceDocument> Sources => sources.Values;
    internal string Error { get; }

    /// <summary>构造时即校验 schema 版本、源文件行号范围和父条目引用；宁可拒绝加载，也不呈现指向错误行的文档。</summary>
    internal VsmReference(Data data, string error = "")
    {
        if (data.SchemaVersion != 1) throw new InvalidDataException("Unknown reference schema.");
        Error = error;
        Entries = data.Entries;
        sources = data.Sources.ToDictionary(s => s.File, StringComparer.Ordinal);
        // 统一换行后再切分，使行号在 CRLF/CR/LF 三种源文件上都对得上。
        sourceLines = sources.ToDictionary(s => s.Key,
            s => s.Value.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'),
            StringComparer.Ordinal);
        byId = data.Entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
        foreach (var e in data.Entries)
        {
            if (e.Source.Length > 0 && (!sources.TryGetValue(e.Source, out var source) ||
                e.StartLine < 1 || e.EndLine < e.StartLine || e.EndLine > source.LineCount))
                throw new InvalidDataException($"Reference {e.Id} has an invalid source range.");
            if (e.ParentId.Length > 0 && !byId.ContainsKey(e.ParentId))
                throw new InvalidDataException($"Reference {e.Id} has an invalid parent.");
        }
        searchText = data.Entries.ToDictionary(e => e.Id,
            e => string.Join("\n", new[] { e.Name, e.Category, e.Summary, e.Source }
                .Concat(e.Body).Concat(e.Context).Concat(e.Notes)), StringComparer.Ordinal);
        patterns = data.Entries.Where(e => e.Kind == "mod" && e.MatchPattern.Length > 0)
            // 50 ms 匹配超时：文档里的模式来自数据文件，不能让病态正则卡住编辑器 UI 线程。
            .Select(e => new PatternEntry(e,
                new Regex(e.MatchPattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)),
                new Regex(e.MatchPattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50))))
            .ToArray();
    }

    /// <summary>从 JSON 文本建索引。反序列化出 null 视为损坏，直接抛错，不返回一份空目录冒充可用文档。</summary>
    internal static VsmReference FromJson(string text)
    {
        var data = AppJson.Deserialize<Data>(text)
            ?? throw new InvalidDataException("Empty reference index.");
        return new(data);
    }

    static VsmReference LoadBundled()
    {
        try
        {
            // 优先使用程序集内嵌资源。外部 Assets 目录过期时，
            // 不能让新文档退化成旧的或空的帮助数据库。
            using var stream = typeof(VsmReference).Assembly.GetManifestResourceStream(ResourceName);
            string text;
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                text = reader.ReadToEnd();
            }
            else
            {
                Console.Error.WriteLine("[reference] Embedded index missing; using external Assets/Documentation.");
                text = File.ReadAllText(Path.Combine(Paths.Assets, "Documentation", "vsm-reference.json"));
            }
            var result = FromJson(text);
            Console.Error.WriteLine($"[reference] User documentation loaded: {result.Entries.Count} entries / {result.Sources.Count} sources.");
            return result;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[reference] Documentation unavailable: " + ex.Message);
            // 参考文档不可用绝不能中断播放或编辑，退回空目录并记下错误信息。
            return new(new Data { SchemaVersion = 1 }, ex.Message);
        }
    }

    /// <summary>按条目 ID 精确查找，用于交叉引用跳转；ID 不存在返回 null，调用方不应据此推断该 mod 不存在。</summary>
    internal Entry? Find(string id) => byId.GetValueOrDefault(id);

    /// <summary>先精确匹配原始名；占位符只在其文档声明的取值范围内匹配。</summary>
    internal IReadOnlyList<Match> MatchMod(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 1024) return [];
        var exact = Entries.Where(e => e.Kind == "mod" && e.MatchPattern.Length == 0 &&
            e.Name.Equals(name, StringComparison.Ordinal)).ToArray();
        if (exact.Length > 0) return exact.Select(e => new Match(e, new Dictionary<string, string>(), false)).ToArray();
        var result = new List<Match>();
        foreach (var pattern in patterns)
        {
            var m = pattern.Exact.Match(name);
            bool caseDifference = !m.Success;
            if (!m.Success) m = pattern.IgnoreCase.Match(name);
            if (!m.Success) continue;
            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string group in pattern.Exact.GetGroupNames())
                if (group != "0" && m.Groups[group].Success) args[group] = m.Groups[group].Value;
            result.Add(new(pattern.Entry, args, caseDifference));
        }
        if (result.Count > 0) return result;
        // 忽略大小写的查找只是文档检索上的方便，绝不构成别名——CaseDifference 会如实标出差异。
        return Entries.Where(e => e.Kind == "mod" && e.MatchPattern.Length == 0 &&
            e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(e => new Match(e, new Dictionary<string, string>(), true)).ToArray();
    }

    /// <summary>全部关键词都命中才算匹配；排序上完全同名优先于前缀匹配，源文件条目排在最后。</summary>
    internal Entry[] Search(string query, string kind = "")
    {
        string[] words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Entries.Where(e => kind.Length == 0 || e.Kind == kind || (kind == "section" && (e.Kind is "guide" or "section")))
            .Where(e => words.All(word => searchText[e.Id].Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => query.Length > 0 && e.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0 :
                query.Length > 0 && e.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(e => query.Length == 0 ? 0 : e.Kind == "source" ? 2 : e.Kind == "section" ? 1 : 0)
            .ToArray();
    }

    /// <summary>逐字返回源文件片段；找不到源文件时退回条目正文。行号从 1 开始，与 StartLine/EndLine 一致。</summary>
    internal string SourceExcerpt(Entry entry, bool numbered = false)
    {
        if (!sources.TryGetValue(entry.Source, out var source)) return string.Join("\n\n", entry.Body);
        if (!numbered && entry.Kind == "source") return source.Text;
        var lines = sourceLines[entry.Source];
        return string.Join("\n", Enumerable.Range(entry.StartLine, entry.EndLine - entry.StartLine + 1)
            .Select(line => numbered ? $"{line,3}  {lines[line - 1]}" : lines[line - 1]));
    }

    /// <summary>悬浮卡片用的紧凑说明，全部有源可依。不加编者按，也不塞进整章原文。</summary>
    internal string[] Explain(Entry entry) => entry.Body.Concat(entry.Context)
        .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToArray();
}
