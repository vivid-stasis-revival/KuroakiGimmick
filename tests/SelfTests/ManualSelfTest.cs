using KuroakiGimmick.UI;

namespace KuroakiGimmick.Core;

/// <summary>只跑 CPU 的说明书查表回归；不创建渲染器，也不创建 native 窗口。</summary>
public static class ManualSelfTest
{
    /// <summary>返回通过的检查数，供上层自测套件折算进自己的总数；任何一条不过就抛异常，不累计失败继续跑。</summary>
    public static int Run()
    {
        int count = 0;
        void Check(bool valid, string label)
        {
            if (!valid) throw new InvalidOperationException("MANUAL TEST FAILED: " + label);
            count++;
            Console.WriteLine("PASS " + label);
        }
        // 292 / 205 / 29 / 12 是两份原始说明文档里真实的条目数，不是"大于零就算过"的宽松阈值：
        // 对不上只可能是解析器漏读或多读了表格行，或者文档被改过而没有重新审阅，两种都必须立刻暴露。
        Check(EditorManual.LoadError.Length == 0 && EditorManual.Entries.Length == 292, "embedded source manual loads all entries");
        Check(EditorManual.Entries.Count(e => e.Kind == "mod") == 205, "all documented mod names/templates are indexed");
        Check(EditorManual.Entries.Count(e => e.Kind == "obj") == 29 &&
            EditorManual.Entries.Count(e => e.Kind == "mpf") == 12, "all obj/function associations indexed");
        // 左边是作者真会写出来的具体 mod 名，右边是它必须命中的文档模板 key；模板 key 沿用原文的中文占位符写法。
        // textX_foo_barb 与 textX_foo_bar 成对出现，专门盯住结尾 b 的歧义：不能因为某个后缀更长就无条件判它优先。
        foreach (var pair in new[] {
            ("scrollspeed", "scrollspeed"), ("noterot", "noterot"),
            ("imgx_背景_01", "imgx_[图像名]"), ("imgscaleytime_test", "imgscaleytime_[图像名]"),
            ("textX_foo_barb", "textX_[tid]b"), ("textX_foo_bar", "textX_[tid]"),
            ("textsep_12", "textsep_[tid]"), ("twx4", "twx[id]"),
            ("boost_distanceind6", "boost_distanceind[lane]") })
            Check(EditorManual.LookupMod(pair.Item1)?.Entry.Key == pair.Item2, "source help lookup: " + pair.Item1);
        Check(EditorManual.LookupMod("textX_foo_barb")?.Alternatives.Contains("textX_[tid]") == true,
            "arbitrary tid versus suffix-b ambiguity is visible");
        // 空参数（imgx_）、越界 twirl id（0 和 5，文档只认 1..4）、越界轨道（7，文档只到 6）和完全没见过的名字都必须查不到。
        // 宁可没有帮助，也不能凭"看起来差不多"给出一段无关说明，那会让作者照着错的文档写。
        foreach (string name in new[] { "imgx_", "twx0", "twx5", "scrollind7", "notealpind7", "totally_unknown" })
            Check(EditorManual.LookupMod(name) == null, "no invented template match: " + name);
        Check(EditorManual.LookupMod("PRTRX") is { CaseDiffers: true }, "case-only lookup is explicitly not an alias");
        // 原文对 hom 的说明前后自相矛盾，这里保留 document-conflict 标记交给作者判断，不替他挑一种解释当成定论。
        Check(EditorManual.LookupMod("hom")?.Entry.Scope == "document-conflict", "hom proxy inconsistency remains explicit");
        Check(EditorManual.Search("imgx_background").FirstOrDefault()?.Key == "imgx_[图像名]", "concrete image name search resolves its template");
        Check(EditorManual.Search("行间距").Any(e => e.Key == "textsep_[tid]"), "Chinese searches source wording, not the old character-spacing description");
        Check(EditorManual.Search("addVeloTween").FirstOrDefault()?.Key == "addVeloTween", "VSV syntax ranks before the full original document");
        Check(EditorManual.Search("freeze", "VSM / 基础 gimmick").Any(e => e.Key == "freeze"), "category and search work together");
        Check(EditorManual.Search("does-not-exist-405932").Length == 0, "unmatched search returns no invented results");
        Check(EditorManual.Entries.Count(e => e.Kind == "source") == 2, "both originals available inside browser");
        return count;
    }
}
