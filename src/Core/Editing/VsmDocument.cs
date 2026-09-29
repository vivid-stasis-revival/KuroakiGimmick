using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KuroakiGimmick.Core.Editing;

/// <summary>
/// 一个源行对应一个可编辑片段。start:end:step 保持为一个重复片段，不把导入文件展开后重写。
/// 未知行、mpf、元数据、注释、BOM、换行均原样保留；只有七字段且严格可解析的 mods 行允许可视化编辑。
/// </summary>
public sealed partial class VsmDocument
{
    /// <summary>
    /// 一个可视化编辑单元。<c>Id</c> 是事件身份，跨编辑保持不变，决定它对应哪一源行——不要用行号或字段值当身份。
    /// Beat/Duration/RepeatStep 单位是拍；From/To 保留源文本原样（含 "_" 表示沿用当前值），不在此处转成数值。
    /// </summary>
    public sealed record Clip(Guid Id, double Beat, double Duration, string Ease, string From, string To,
        string Name, int Proxy, double? RepeatEnd = null, double RepeatStep = 1, bool ParenthesizedRepeat = false)
    {
        /// <summary>最后一次重复发生的拍。非重复片段就是 Beat 本身，不要把 Duration 算进来。</summary>
        public double LastBeat => RepeatEnd.HasValue ? Beat + (RepeatCount - 1) * RepeatStep : Beat;
        /// <summary>整个片段真正结束的拍，含最后一次重复的时长；负 Duration 按 0 处理。</summary>
        public double End => LastBeat + Math.Max(0, Duration);
        /// <summary>重复次数；1e-7 容差吸收浮点误差，上限一百万，防止病态区间展开成天文数字。</summary>
        public int RepeatCount => RepeatEnd is double end ? (int)Math.Min(1_000_000, Math.Floor((end - Beat) / RepeatStep + 1e-7) + 1) : 1;
        /// <summary>同一条轨道的判别键。proxy 必须参与其中——同名 mod 作用在不同 proxy 上是彼此独立的两条轨。</summary>
        public string TrackKey => $"{Proxy}:{Name}";
    }

    // record 是不可变的。历史快照因此可以共享未改动的行，不必复制上兆字节的文本。
    public sealed record Line(string Text, string Ending, Clip? Event);
    public List<Line> Lines { get; private set; } = [];
    /// <summary>源文件的编码，写回时照此还原。默认是不带 BOM 的 UTF-8 且对非法字节抛错，不做静默替换。</summary>
    public Encoding Encoding { get; private set; } = new UTF8Encoding(false, true);
    /// <summary>读到的 BOM 原始字节，写回时原样放在最前面；源文件没有 BOM 就不会凭空补一个。</summary>
    public byte[] Preamble { get; private set; } = [];
    public string NewLine { get; private set; } = "\n";
    /// <summary>可视化编辑的事件，按源行顺序给出。解析不了的行不在其中，但它们仍然存在于 Lines 里。</summary>
    public IEnumerable<Clip> Clips => Lines.Where(x => x.Event != null).Select(x => x.Event!);
    /// <summary>按行拼回完整源文本。逐行保留原换行符，因此未编辑的文件重新写出时与输入逐字节一致。</summary>
    public string Text => string.Concat(Lines.Select(x => x.Text + x.Ending));
    /// <summary>原样保留但无法可视化编辑的非空行数，用来在 UI 上提示“这份源里还有 N 行只能改源文本”。</summary>
    public int PreservedLineCount => Lines.Count(x => x.Event == null && !string.IsNullOrWhiteSpace(x.Text));

    /// <summary>识别并记住 BOM 与编码，写回时原样还原；64 MiB 上限先于解码生效。</summary>
    public static VsmDocument Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("VSM exceeds 64 MiB.");
        var doc = new VsmDocument();
        // UTF-32 的 BOM 前两字节与 UTF-16 LE 相同，必须先判 4 字节形式，否则会误判编码。
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) doc.Preamble = [0xEF, 0xBB, 0xBF];
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 }))
        { doc.Encoding = new UTF32Encoding(false, false, true); doc.Preamble = [0xFF, 0xFE, 0, 0]; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF }))
        { doc.Encoding = new UTF32Encoding(true, false, true); doc.Preamble = [0, 0, 0xFE, 0xFF]; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
        { doc.Encoding = new UnicodeEncoding(false, false, true); doc.Preamble = [0xFF, 0xFE]; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
        { doc.Encoding = new UnicodeEncoding(true, false, true); doc.Preamble = [0xFE, 0xFF]; }
        doc.Parse(doc.Encoding.GetString(bytes, doc.Preamble.Length, bytes.Length - doc.Preamble.Length));
        return doc;
    }

    /// <summary>从已在内存里的文本建文档，不经过文件，因此保持默认编码与空 BOM。</summary>
    public static VsmDocument FromText(string text)
    {
        var doc = new VsmDocument(); doc.Parse(text); return doc;
    }

    /// <summary>
    /// 由已解析的 Chart 反向生成文本文档，供没有 VSM 源文件时使用；按 Order 输出以保留原声明顺序。
    /// 不写 "mods" 行：原版 read_mods_file 一开始就是 mods 模式，只认 "mpf" 一个切换标记，
    /// 写了反而会被当成事件行切分，取 parts[1] 越界直接崩。mpf 段必须排在最后，原版没有回到 mods 的路。
    /// </summary>
    public static VsmDocument FromChart(Chart chart)
    {
        var text = new StringBuilder($"!obj:{chart.ObjectName}\n!proxies:{chart.Proxies}\n");
        foreach (var pair in chart.Metadata.Where(x => x.Key is not ("obj" or "proxies")))
            text.AppendLine($"!{pair.Key}:{pair.Value}");
        foreach (var m in chart.Mods.OrderBy(m => m.Order))
            text.AppendLine($"{N(m.Beat)},{N(m.Duration)},{m.Ease},{Value(m.From)},{Value(m.To)},{m.Name},{m.Proxy}");
        if (chart.PerFrame.Count > 0)
        {
            text.AppendLine("mpf");
            foreach (var m in chart.PerFrame) text.AppendLine($"{N(m.StartBeat)},{N(m.EndBeat)},{m.Function}");
        }
        return FromText(text.ToString());
    }

    void Parse(string text)
    {
        bool perFrame = false;
        // 逐行切分并把行尾单独留存，使 CRLF/CR/LF 混排的源文件也能原样写回。
        foreach (Match m in Regex.Matches(text, @"([^\r\n]*)(\r\n|\r|\n|$)"))
        {
            if (m.Length == 0) continue;
            string line = m.Groups[1].Value, ending = m.Groups[2].Value;
            string meaningful = Body(line).Trim();
            // 原版 read_mods_file 的 mode 只能 mods→mpf 走一次，没有写 "mods" 回去的路，
            // 所以这里也不认那个标记：mpf 之后的行一律当不透明文本保留。
            if (meaningful == "mpf") perFrame = true;
            // mpf 段内的行不解析成可编辑事件，只作为不透明文本保留。每行拿到自己的 Guid，成为此后稳定的事件身份。
            Lines.Add(new(line, ending, perFrame ? null : ParseClip(line, Guid.NewGuid())));
        }
        NewLine = Lines.FirstOrDefault(l => l.Ending.Length > 0)?.Ending ?? "\n";
    }

    /// <summary>去掉 <c>//</c> 起的行尾注释，只留语义部分；注释本身仍留在原始行文本里。</summary>
    static string Body(string text)
    {
        int comment = text.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? text : text[..comment];
    }

    /// <summary>
    /// 严格解析：只有恰好七个字段且每个字段都合法时才返回可视化编辑单元。
    /// 返回 null 表示"这行保持不透明"，它仍会被原样保留和导出，绝不是"删掉"。
    /// </summary>
    static Clip? ParseClip(string text, Guid id)
    {
        string body = Body(text).Trim();
        if (body.Length == 0 || body[0] is '!' or '#' or ';') return null;
        string[] fields = body.Split(',').Select(x => x.Trim()).ToArray();
        if (fields.Length != 7) return null;
        try
        {
            var range = VsmBeatRange.Parse(fields[0], SourceNumber);
            double start = range.Start, duration = SourceNumber(fields[1]);
            double? end = range.End;
            double step = range.Step;
            // "_" 表示沿用当前值，不是数字；其余取值必须严格可解析，解析失败就整行降级为不透明行。
            if (fields[3] != "_") SourceNumber(fields[3]);
            if (fields[4] != "_") SourceNumber(fields[4]);
            if (!Easings.IsKnown(fields[2]) || !Regex.IsMatch(fields[5], @"^[\p{L}_][\p{L}\p{M}\p{N}_:.\-]*$")) return null;
            int proxy = int.Parse(fields[6], CultureInfo.InvariantCulture);
            // proxy 为 -1 表示全局，否则 0..63。
            if (proxy is < -1 or > 63 || fields[5].Length == 0) return null;
            return new(id, start, duration, fields[2], fields[3], fields[4], fields[5], proxy, end, step, range.Parenthesized);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException) { return null; }
    }

    /// <summary>源文件里的数值只接受普通/科学记数法，不接受编辑器那套分数与"拍+分数"输入。</summary>
    static double SourceNumber(string text)
    {
        double n = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(n)) throw new FormatException("Invalid source number.");
        return n;
    }

    /// <summary>
    /// VSM 是给人维护的作者格式，不是 double 的二进制状态转储。所有作者可见数值最多保留
    /// 4 位小数：编辑器显示、结构化编辑、工程保存、实时预览与正式导出统一使用同一表示，
    /// 不允许 14.1250138333 / 7.9125085600016067 这类运算尾巴重新泄漏出来。-0 也统一写成 0。
    /// </summary>
    public static string N(double n)
    {
        if (!double.IsFinite(n)) throw new FormatException("A finite number is required.");
        double rounded = Math.Round(n, 4, MidpointRounding.AwayFromZero);
        if (rounded == 0) return "0";
        return rounded.ToString("0.####", CultureInfo.InvariantCulture);
    }
    /// <summary>把一个作者数值收敛到与 VSM 最终文本完全一致的 double，避免拖拽后内存里继续带着隐藏尾巴。</summary>
    public static double Canonical(double n) => double.Parse(N(n), NumberStyles.Float, CultureInfo.InvariantCulture);
    /// <summary>作者时间/值按最终 VSM 语义比较，同时保留原先的微小 epsilon 容差；绝不能再拿 raw double 做裸 ==。</summary>
    public static bool NearlyEqual(double a, double b, double epsilon = 1e-9) =>
        Canonical(a) == Canonical(b) || Math.Abs(a - b) <= epsilon;
    /// <summary>编辑器显示与最终 VSM 使用同一套最多 4 位小数的作者数值规则；UI 与导出因此不会再各说各话。</summary>
    public static string Ui(double n) => N(n);
    /// <summary>编辑器显示 From / To 时也收掉旧文件里已有的浮点尾巴；源 token 本身不会因此被改写。</summary>
    public static string UiValue(string value)
    {
        if (value == "_") return value;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n)
            ? Ui(n) : value;
    }
    /// <summary>573613 是原版表示"沿用当前值"的哨兵值，写回源文件时还原成 "_"。</summary>
    public static string Value(double n) => n == 573613 ? "_" : N(n);
    /// <summary>接受编辑器输入语法；失败时抛 FormatException，由调用方提示用户而不是静默取 0。</summary>
    public static double Number(string text)
    {
        text = text.Trim();
        // 分数与"拍+分数"是编辑器的输入形式，不是对 VSM 语法的扩展。
        // 先按普通/科学记数法解析，再把 '+' 当作拍数相加（避免把 1e+5 拆开）。
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double literal))
        {
            if (!double.IsFinite(literal)) throw new FormatException("A finite number is required.");
            return literal;
        }
        double n;
        int plus = text.IndexOf('+', 1);
        int slash = text.IndexOf('/');
        if (plus > 0) n = Number(text[..plus]) + Number(text[(plus + 1)..]);
        else if (slash > 0) n = Number(text[..slash]) / Number(text[(slash + 1)..]);
        else n = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(n)) throw new FormatException("A finite number is required.");
        return n;
    }

    /// <summary>按事件身份查片段。找不到返回 null，通常意味着这条已被撤销或删除，调用方应当放弃本次操作而不是新建一条。</summary>
    public Clip? Find(Guid id) => Lines.FirstOrDefault(l => l.Event?.Id == id)?.Event;
    /// <summary>诊断用的 1 基源行号；事件身份始终是 Id，行号会随插入删除变化，不要拿它做标识。</summary>
    public int SourceLine(Guid id) => Lines.FindIndex(l => l.Event?.Id == id) + 1;
    /// <summary>整份行表回到快照。Line 是不可变 record，直接复用同一批对象即可，不必也不应深拷贝文本。</summary>
    public void Restore(Line[] snapshot) => Lines = snapshot.ToList();
    /// <summary>按原编码写出，并在前面补回原始 BOM；未编辑的文档由此可逐字节还原。</summary>
    public byte[] Bytes()
    {
        byte[] body = Encoding.GetBytes(Text), result = new byte[Preamble.Length + body.Length];
        Preamble.CopyTo(result, 0); body.CopyTo(result, Preamble.Length); return result;
    }
    /// <summary>
    /// 成品 VSM 文本。所有能够结构化解析的七字段事件与 mpf 时间都重新写成 canonical 数字；
    /// 注释、缩进、换行与无法识别的透明保留行完全不碰。这样旧文件里已经存在的雷霆尾巴也不会继续混进导出物。
    /// </summary>
    public string NormalizedText
    {
        get
        {
            var output = new StringBuilder();
            bool perFrame = false;
            foreach (var line in Lines)
            {
                string meaningful = Body(line.Text).Trim();
                if (meaningful == "mpf")
                {
                    perFrame = true; output.Append(line.Text).Append(line.Ending); continue;
                }
                output.Append(perFrame ? NormalizedPerFrameLine(line) : NormalizedLine(line));
            }
            return output.ToString();
        }
    }
    string NormalizedLine(Line line)
    {
        if (line.Event is not { } parsed) return line.Text + line.Ending;
        var canonical = Canonicalize(parsed, true);
        if (canonical.Name is "fx_underwater" or "fx_chroma_distort")
        {
            string Safe(string value) => value == "_" ? value : N(Math.Max(.01, Number(value)));
            canonical = canonical with { From = Safe(canonical.From), To = Safe(canonical.To) };
        }
        return RewriteTokens(line, Fields(canonical));
    }
    string NormalizedPerFrameLine(Line line)
    {
        string body = Body(line.Text), meaningful = body.Trim();
        if (meaningful.Length == 0 || meaningful[0] is '#' or ';' || meaningful.StartsWith("//", StringComparison.Ordinal))
            return line.Text + line.Ending;
        string[] raw = body.Split(',');
        if (raw.Length != 3) return line.Text + line.Ending;
        try
        {
            string[] values = [N(SourceNumber(raw[0].Trim())), N(SourceNumber(raw[1].Trim())), raw[2].Trim()];
            return RewriteTokens(line, values);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return line.Text + line.Ending;
        }
    }
    static string RewriteTokens(Line line, string[] values)
    {
        string body = Body(line.Text), suffix = line.Text[body.Length..];
        string[] parts = body.Split(',');
        if (parts.Length != values.Length) return line.Text + line.Ending;
        for (int i = 0; i < parts.Length; i++)
        {
            string token = parts[i];
            int left = token.Length - token.TrimStart().Length, right = token.Length - token.TrimEnd().Length;
            parts[i] = token[..left] + values[i] + (right > 0 ? token[^right..] : "");
        }
        return string.Join(',', parts) + suffix + line.Ending;
    }
    /// <summary>按源编码/BOM 输出 canonical VSM，供保存副本与所有正式导出统一使用。</summary>
    public byte[] NormalizedBytes()
    {
        byte[] body = Encoding.GetBytes(NormalizedText), result = new byte[Preamble.Length + body.Length];
        Preamble.CopyTo(result, 0); body.CopyTo(result, Preamble.Length); return result;
    }

    /// <summary>
    /// 结构化事件进入文档时统一收敛作者数值。From/To 只有在 normalizeValues=true 时才重写，
    /// 普通编辑仍可保留未触碰字段的原始 token；成品导出则会把所有可解析事件彻底清理。
    /// </summary>
    static Clip Canonicalize(Clip c, bool normalizeValues) => c with
    {
        Beat = Canonical(c.Beat),
        Duration = Canonical(c.Duration),
        RepeatEnd = c.RepeatEnd is double end ? Canonical(end) : null,
        RepeatStep = Canonical(c.RepeatStep),
        From = normalizeValues && c.From != "_" ? N(Number(c.From)) : c.From,
        To = normalizeValues && c.To != "_" ? N(Number(c.To)) : c.To
    };

    /// <summary>重复区间写回时保留源文件用的是括号形式还是裸 start:end:step 形式，不统一成其中一种。</summary>
    static string[] Fields(Clip c) => [c.RepeatEnd is double end ? c.ParenthesizedRepeat
        ? $"{N(c.Beat)}(:{N(end)}:{N(c.RepeatStep)})" : $"{N(c.Beat)}:{N(end)}:{N(c.RepeatStep)}" : N(c.Beat),
        N(c.Duration), c.Ease, c.From, c.To, c.Name, c.Proxy.ToString(CultureInfo.InvariantCulture)];

    /// <summary>
    /// 把片段写成一行 VSM 源文本。剪贴板与源文件共用同一套字段格式，因此贴回来的行必定能被同一个解析器读回；
    /// 这里只产出事件行本身，不含 "mods" 头也不含注释 —— 原版 read_mods_file 见到这两者会崩。
    /// </summary>
    public static string Compose(Clip c) => string.Join(',', Fields(c));

    /// <summary>提交前的取值校验；抛出的 FormatException 文案会直接显示给用户，所以写的是可操作的约束而非内部术语。</summary>
    static void Validate(Clip c)
    {
        if (!double.IsFinite(c.Beat) || !double.IsFinite(c.Duration) || Math.Abs(c.Beat) > 1e8 || Math.Abs(c.Duration) > 1e8)
            throw new FormatException("Beat/duration must be finite and within 100 million beats.");
        if (c.Proxy is < -1 or > 63) throw new FormatException("Proxy must be -1 (global) or 0..63.");
        if (!Regex.IsMatch(c.Name, @"^[\p{L}_][\p{L}\p{M}\p{N}_:.\-]*$")) throw new FormatException("Invalid mod identifier.");
        if (!Easings.IsKnown(c.Ease)) throw new FormatException("Choose a supported easing.");
        if (c.From != "_") Number(c.From);
        if (c.To != "_") Number(c.To);
        if (!double.IsFinite(c.RepeatStep) || c.RepeatStep <= 0 ||
            c.RepeatEnd is double e && (!double.IsFinite(e) || e < c.Beat || (e - c.Beat) / c.RepeatStep > 999_999))
            throw new FormatException("Invalid repeat range (up to one million occurrences).");
    }

    /// <summary>就地改写同一源行，保持事件身份与行序不变；只重写真正变化的字段，其余字段连同原有空白和注释一并保留。</summary>
    public void Replace(Clip next)
    {
        int i = Lines.FindIndex(l => l.Event?.Id == next.Id);
        if (i < 0) throw new InvalidOperationException("The source event no longer exists.");
        var line = Lines[i]; var previous = line.Event!;
        // 拍/时长是编辑器自己算出来的，提交边界必须先 canonicalize；From/To 仍只改用户真正碰过的字段。
        next = Canonicalize(next, false) with
        {
            From = next.From == previous.From || next.From == "_" ? next.From : N(Number(next.From)),
            To = next.To == previous.To || next.To == "_" ? next.To : N(Number(next.To))
        };
        Validate(next);
        Lines[i] = RewriteLine(line, next);
    }

    static Line RewriteLine(Line line, Clip next)
    {
        var previous = line.Event!;
        string body = Body(line.Text), suffix = line.Text[body.Length..];
        string[] parts = body.Split(','), oldFields = Fields(previous), values = Fields(next);
        // 非数值字段仍按原样保留；作者数值一旦经过结构化编辑就必须与 canonical Event 保持一致。
        for (int f = 0; f < 7; f++)
        {
            string token = parts[f];
            // 事件一旦被编辑，已经带着二进制尾巴的旧 token 也同步收敛到 canonical 形式；
            // 否则 Event 已经是 7.91250856，Text 却还留着 7.9125085600016067，预览重建会再次把尾巴读回来。
            if (oldFields[f] == values[f] && token.Trim() == values[f]) continue;
            // 只替换 token 的实体部分，两侧原有的对齐空白原样保留。
            int left = token.Length - token.TrimStart().Length, right = token.Length - token.TrimEnd().Length;
            parts[f] = token[..left] + values[f] + (right > 0 ? token[^right..] : "");
        }
        return line with { Text = string.Join(',', parts) + suffix, Event = next };
    }

    /// <summary>
    /// 追加效果事件，不排序、不与已有行合并；新行的 Id 即其后续身份。
    /// 文件里已有 mpf 段时插到它前面：原版 read_mods_file 的 mode 只能 mods→mpf 单向切换，
    /// 没有写 "mods" 回去的办法，那一行只会被当成事件行切分然后越界崩掉。既有段落和注释一律不动。
    /// </summary>
    public void Add(Clip clip)
    {
        clip = Canonicalize(clip, true);
        Validate(clip);
        if (Lines.Count > 0 && Lines[^1].Ending.Length == 0) Lines[^1] = Lines[^1] with { Ending = NewLine };
        int mpf = Lines.FindIndex(line => Body(line.Text).Trim() == "mpf");
        var added = new Line(string.Join(',', Fields(clip)), NewLine, clip);
        if (mpf >= 0) Lines.Insert(mpf, added);
        else Lines.Add(added);
    }

    /// <summary>仅限作者显式操作。保持文件结构与其它元数据完好：只替换值本身，原有缩进、行尾注释和行位置不变。</summary>
    public void SetHeader(string key, string value)
    {
        if (key != "obj" || string.IsNullOrWhiteSpace(value) || value.Any(c => c is '\r' or '\n' or ','))
            throw new FormatException("Invalid object header.");
        bool found = false;
        for (int i = 0; i < Lines.Count; i++)
            if (Body(Lines[i].Text).TrimStart().StartsWith("!" + key + ":", StringComparison.Ordinal))
            {
                string raw = Lines[i].Text;
                int colon = raw.IndexOf(':'), comment = raw.IndexOf("//", StringComparison.Ordinal);
                string oldValue = comment >= 0 ? raw[(colon + 1)..comment] : raw[(colon + 1)..];
                int left = oldValue.Length - oldValue.TrimStart().Length;
                int right = oldValue.Length - oldValue.TrimEnd().Length;
                string suffix = right > 0 ? oldValue[^right..] : "";
                Lines[i] = Lines[i] with { Text = raw[..(colon + 1)] + oldValue[..left] + value + suffix + (comment >= 0 ? raw[comment..] : "") };
                found = true;
            }
        if (!found) Lines.Insert(0, new("!" + key + ":" + value, NewLine, null));
    }
    /// <summary>
    /// 删除属于某张 VSP 图片的全部 mod 行。已解析事件与解析失败但仍能明确识别出第 6 列图片 mod 名的行都会删除；
    /// mpf 段之后不再按 mod 解释，因此绝不碰那里的不透明文本。
    /// </summary>
    public int DeleteImageEvents(string imageId)
    {
        int removed = 0; bool perFrame = false;
        for (int i = 0; i < Lines.Count;)
        {
            string body = Body(Lines[i].Text).Trim();
            if (body == "mpf") { perFrame = true; i++; continue; }
            bool target = false;
            if (!perFrame)
            {
                if (Lines[i].Event is { } clip && CustomImages.TryMod(clip.Name, out _, out var parsedId))
                    target = string.Equals(parsedId, imageId, StringComparison.Ordinal);
                else
                {
                    string[] fields = body.Split(',').Select(x => x.Trim()).ToArray();
                    if (fields.Length == 7 && CustomImages.TryMod(fields[5], out _, out var rawId))
                        target = string.Equals(rawId, imageId, StringComparison.Ordinal);
                }
            }
            if (target) { Lines.RemoveAt(i); removed++; } else i++;
        }
        return removed;
    }

    /// <summary>整行删除，连同它的换行一起消失；其余行的文本与顺序不受影响，不做任何重排或重新格式化。</summary>
    public void Delete(Guid id) => Lines.RemoveAll(l => l.Event?.Id == id);
}
