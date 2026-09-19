using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KuroakiGimmick.Core.Documentation;
using KuroakiGimmick.UI;

namespace KuroakiGimmick.Core;

/// <summary>只跑 CPU 的帮助文档检查，全部以原始说明文档为准。不初始化 SDL，也不注册运行时 mod。</summary>
public static class ReferenceSelfTest
{
    /// <summary>作为独立入口运行；这些检查只保证文档与原文一致，不是游戏行为一致性测试。</summary>
    public static int Run()
    {
        int count = Checks();
        Console.WriteLine($"Reference checks passed: {count}. These are not gameplay parity tests.");
        return 0;
    }

    /// <summary>返回通过的检查数，供 EditorSelfTest 等套件折算进自己的总数。</summary>
    internal static int Checks()
    {
        int count = 0;
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException("REFERENCE TEST FAILED: " + label);
            count++;
            Console.WriteLine("PASS " + label);
        }
        // 这些行数就是两份原始说明文档里的实际条目数，不是随便定的阈值：
        // 数字对不上说明解析器漏读/多读了表格行，或者文档被改过而没有重新审阅。
        var reference = VsmReference.Shared;
        Check(reference.Error.Length == 0, "embedded reference loads");
        Check(reference.Sources.Count == 2, "exactly the two supplied source documents");
        Check(reference.Entries.Count(e => e.Kind == "mod") == 205, "all 205 parameter table rows (including templates)");
        Check(reference.Entries.Count(e => e.Kind == "config") == 10, "all 10 configuration rows");
        Check(reference.Entries.Count(e => e.Kind == "object") == 29, "all 29 object rows");
        Check(reference.Entries.Count(e => e.Kind == "mpf") == 12, "12 distinct source-spelled mpf function names");
        // 出处校验：内嵌文本必须与随程序分发的原文件逐字节一致（只去掉 BOM），SHA-256 用来锁住这一点。
        // 原文只作为出处保留，不会直接当成一个 UI 页面塞给用户。
        foreach (var source in reference.Sources)
        {
            byte[] original = File.ReadAllBytes(Path.Combine(Paths.Assets, "Documentation", source.File));
            Check(Convert.ToHexString(SHA256.HashData(original)).Equals(source.Sha256, StringComparison.OrdinalIgnoreCase),
                "original source SHA-256: " + source.File);
            Check(Encoding.UTF8.GetString(original).TrimStart('\uFEFF') == source.Text, "embedded source text: " + source.File);
            Check(!reference.Entries.Any(e => e.Kind == "source"), "source retained for provenance, not a raw UI page: " + source.File);
        }
        // 结构化后的单元格必须能和原文那一行重新切分出的结果逐个对上：解析不得顺手规范化内容。
        foreach (var entry in reference.Entries.Where(e => e.Cells.Length > 0))
        {
            var line = reference.SourceExcerpt(entry);
            string[] cells = line.Trim().Trim('|').Split('|').Select(s => s.Trim()).ToArray();
            Check(cells.SequenceEqual(entry.Cells), "table cells agree with raw source: " + entry.Id);
        }
        // 文档里的默认值和符号约定按原文呈现：scrollspeed 的默认值是文档写的 1（不是工程当前的滚动速度），
        // noterot 的正负方向也照抄原文，不能按"看起来更合理"改写。
        Check(reference.MatchMod("scrollspeed").Single().Entry.Body.Any(s => s.Contains("默认值为1")),
            "source default 1 is not overwritten with project scroll speed");
        Check(reference.MatchMod("noterot").Single().Entry.Body.Any(s => s.Contains("负数为顺时针") && s.Contains("正数逆时针")),
            "source note rotation sign is preserved");
        // 模板类条目的取值范围由文档规定：轨道 0..6（6 是 Bumper）、twirl id 只有 1..4；
        // 越界写法必须匹配不上，而不是被当成"大概是这个"给出一段无关的说明。
        Check(reference.MatchMod("notealpind6").Single().Arguments["lane"] == "6", "Bumper lane 6 matches");
        Check(reference.MatchMod("notealpind7").Count == 0 && reference.MatchMod("notealpind-1").Count == 0,
            "out-of-range lane identifiers are not silently accepted by documentation matching");
        Check(reference.MatchMod("twa4").Single().Arguments["id"] == "4" && reference.MatchMod("twa0").Count == 0,
            "twirl id is limited to the documented 1..4");
        Check(reference.MatchMod("imgscaleytime_background_front").Single().Arguments["image"] == "background_front",
            "image identifiers keep underscores");
        Check(reference.MatchMod("textX_任意_字符串").Single().Arguments["tid"] == "任意_字符串",
            "arbitrary string tid matches");
        Check(reference.MatchMod("textX_nameb").Count == 2, "ambiguous trailing-b text names retain both source candidates");
        // 大小写只差一个字母时要显式标出 CaseDifference，让作者自己判断；不能当成执行层面的别名直接放行。
        Check(reference.MatchMod("prtrX").Single().CaseDifference == false && reference.MatchMod("prtrx").Single().CaseDifference,
            "case-only documentation lookup is explicit, not an execution alias");
        // 原文里的拼写错误（ditortedBG_alp、wigglr、filcker）是游戏真正认的名字，必须原样保留：
        // "顺手改对"会让文档和实际可用的标识符对不上。同理，正文里提到的比较名 fx_colorise 不是一条真实的 mod。
        Check(reference.MatchMod("ditortedBG_alp").Count == 1 && reference.MatchMod("distortedBG_alp").Count == 0,
            "unusual source spelling is not silently corrected");
        Check(reference.MatchMod("fx_colorise").Count == 0, "prose comparison name is not invented as a documented mod row");
        Check(reference.Entries.Any(e => e.Kind == "mpf" && e.Name == "wigglr") &&
            reference.Entries.Any(e => e.Kind == "mpf" && e.Name == "filcker"), "mpf source spellings remain unchanged");
        Check(reference.Search("速度", "mod").Length > 0 && reference.Search("透明度", "mod").Length > 0,
            "Chinese full text search");
        Check(reference.Search("addVeloTween", "syntax").Length == 1, "VSV tween has a searchable syntax entry");
        Check(reference.Entries.All(e => e.Kind is not ("guide" or "note" or "source") && e.Notes.Length == 0),
            "automatic editorial notes and whole-source pages removed");
        Check(reference.Sources.Single(s => s.File == "Custom Gimmick说明.md").Text.Contains("下述所有配置都会被认为是true") &&
            reference.Entries.Any(e => e.Kind == "config" && e.Cells.Contains("false")),
            "both original configuration statements remain without choosing a new default");
        var staticSyntax = reference.Entries.Single(e => e.Kind == "syntax" && e.Name == "static");
        Check(reference.SourceExcerpt(staticSyntax).Contains("{图像初始宽度},{图像初始宽度}"),
            "static declaration's duplicated source parameter name is not rewritten");
        Check(reference.Entries.Where(e => e.Kind == "section").SelectMany(e => e.Blocks).Any(b => b.Text.Contains("Extra Gimmicks")),
            "extra-object attachment reference is retained in its original section");
        // 悬浮提示直接取原文内容：不加"【…】"这类自动包装，也不附带来源文件名之类的编辑器标注。
        Check(EditorReferenceHelp.TryGet("textsep_intro", out var info) && info.Short.Contains("行间距"),
            "hover prioritizes source textsep definition over legacy character-spacing description");
        Check(EditorReferenceHelp.TryGet("scrollspeed", out info) && info.Detail.Any(s => s.Contains("默认值为1")) &&
            info.Detail.All(s => !s.Contains("【") && !s.Contains("编辑器说明") && !s.Contains("vsm格式说明.md")),
            "hold-W help retains content without automatic wrappers or source labels");
        foreach (var entry in reference.Entries)
        {
            Check(entry.Blocks.Length > 0, "entry has structured content: " + entry.Id);
            foreach (var block in entry.Blocks.Where(b => b.Kind == "table"))
                Check(block.Rows.All(row => row.Length == block.Columns.Length) && block.RowLinks.Length == block.Rows.Length &&
                    block.RowLinks.All(id => id.Length == 0 || reference.Find(id) != null), "table cells/links valid: " + entry.Id);
        }
        Check(!EditorReferenceHelp.TryGet("new_undocumented_property", out _), "unknown names do not receive invented source descriptions");

        // 内嵌的编辑器字体必须覆盖整个帮助语料（含全部中文与原文文本）里出现的每一个非空白字符，
        // 否则帮助页面会出现豆腐块。两个字重都要查，粗体常常是漏字的那一个。
        foreach (string stem in new[] { "editor-help-sans", "editor-help-sans-bold" })
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Paths.Assets, "Fonts", stem + ".json")));
            var glyphs = json.RootElement.GetProperty("Glyphs");
            var strings = reference.Sources.Select(s => s.Text).Concat(reference.Entries.SelectMany(e =>
                new[] { e.Name, e.Summary, e.Category, e.SourceLabel }.Concat(reference.Explain(e))));
            var needed = string.Concat(strings).EnumerateRunes().Where(rune => !Rune.IsWhiteSpace(rune)).Distinct();
            Check(needed.All(rune => glyphs.TryGetProperty(rune.ToString(), out _)), stem + " covers the complete reference corpus");
        }
        return count + UiPresentationSelfTest.Checks();
    }
}
