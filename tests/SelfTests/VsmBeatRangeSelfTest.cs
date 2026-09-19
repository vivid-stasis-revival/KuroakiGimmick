using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.Core;

/// <summary>
/// VSM 括号重复语法 start(:end:step) 的解析与编辑回归：展开数量、闭区间边界、非法写法的拒绝方式，
/// 以及编辑器把整行重复当成单个 clip 往返编辑。纯 CPU，不创建窗口，只在内存里构造谱面。
/// </summary>
public static class VsmBeatRangeSelfTest
{
    /// <summary>返回通过的检查数。所有期望条数都按 (end-start)/step+1 手算得出，不是跑一遍记下来的基线。</summary>
    public static int Run()
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("BEAT RANGE: " + label); checks++; Console.WriteLine("PASS " + label); }
        Chart Read(string text) { var chart = new Chart(); VsmReader.ReplaceModsText(chart, text, "range-test.vsm"); return chart; }
        // 两行只差 0.01 拍的起点，用来分辨"按行合并"和"按时间合并"。各自展开 (1.9+10)/0.02+1 = 596 条，合计 1192。
        // 源文本刻意用 CRLF 并带一条行尾注释：解析和编辑器往返都不许把它们规范化掉。
        const string first = "-10(:1.9:0.02),0,linear,1,0.08,velocity,-1";
        const string second = "-10.01(:1.9:0.02),0,linear,0.08,1,velocity,-1";
        string source = first + "\r\n" + second + " // preserve comment\r\n";
        var parsed = Read(source);
        // 负数起点带括号时不能被当成"GML 风格数字前缀"那条兼容路径吞掉，因此这里要求除注释以外没有别的诊断。
        // 那条注释诊断是必须有的：原版 read_mods_file 不剥 //，带注释的源进游戏会崩。
        Check(parsed.Mods.Count == 1192 && parsed.Diagnostics.Count == 1
            && parsed.Diagnostics[0].Message.Contains("never strips // comments"),
            "both parenthesized intro loops expand without numeric-prefix notices");
        var a = parsed.Mods.Where(e => e.SourceLine == 1).ToArray(); var b = parsed.Mods.Where(e => e.SourceLine == 2).ToArray();
        Check(a.Length == 596 && b.Length == 596 && a[0].Beat == -10 && b[0].Beat == -10.01, "fractional negative starts remain distinct");
        // 闭区间只在步进正好落到 end 时才发出：a 能踩到 1.9，b 从 -10.01 起步最多到 1.89，再走一步就越界。
        Check(Math.Abs(a[^1].Beat - 1.9) < 1e-9 && Math.Abs(b[^1].Beat - 1.89) < 1e-9, "inclusive end is emitted only when reached by the interval");
        // 展开只复制拍号，值和源行号原样带着走，方便诊断回指到作者写的那一行。
        Check(a.All(e => e.To == .08) && b.All(e => e.To == 1), "each expanded event retains the original value and source line");
        // 60 BPM 下 1 拍 = 1 秒。t=0 命中 a 的整拍事件（.08），t=.015 命中 b 在 0.01 拍的事件（1）：
        // 两行必须按时间交错生效，而不是先把 a 整行放完再放 b。
        var timeline = new Timeline(parsed, new ViewerProject { Bpm = 60 }, 3);
        Check(timeline.Get("velocity", 0) == .08 && timeline.Get("velocity", .015) == 1, "offset repeated rows alternate in chronological playback order");
        Check(Read("0(:1:0.25),0,linear,0,1,prx,-1\n").Mods.Count == 5, "fractional end boundary is included");
        Check(Read("  -1e1 (: 1.9e0 : 2e-2 ),0,linear,0,1,prx,-1\n").Mods.Count == 596, "parenthesized ranges allow whitespace and scientific notation");
        Check(Read("2(:2:.1),0,linear,0,1,prx,-1\n").Mods.Count == 1, "equal start/end produces one event");
        Check(Read("0:2:1,0,linear,0,1,prx,-1\n3:4,0,linear,0,1,prx,-1\n").Mods.Count == 5, "existing colon range syntax remains supported");
        // 逐个非法写法：步长为 0、负步长、start>end、NaN、Infinity、缺步长、括号没闭合、尾部有垃圾、
        // 非数字的 end、超过一百万条、以及小到只剩非规格化的步长。每一种都必须整行报错丢弃，
        // 且绝不能退回"GML 数字前缀"的宽容路径把 "0(:2:0)" 悄悄读成拍号 0 —— 那会凭空多出一条谁也没写的事件。
        // 第二行的 beat 9 用来证明只有出错那一行被丢掉，后续行照常解析。
        foreach (string range in new[] { "0(:2:0)", "0(:2:-1)", "3(:2:1)", "0(:2:NaN)", "0(:Infinity:1)", "0(:2)", "0(:2:1", "0(:2:1)junk", "0(:2x:1)", "0(:1000000:1)", "0(:1:1e-320)" })
        {
            var chart = Read(range + ",0,linear,0,1,prx,-1\n9,0,linear,0,1,prx,-1\n");
            Check(chart.Mods.Count == 1 && chart.Mods[0].Beat == 9 && chart.Diagnostics.Count == 1 && chart.Diagnostics[0].Error,
                "invalid range rejected without numeric-prefix recovery: " + range);
        }
        // 反过来，游戏自己写出来的畸形数字（327.6.6）仍按原版取 327.6，但那条提示必须留着，
        // 不能因为"能读出来"就静默：作者需要知道这一行的写法不规范。
        var legacy = Read("327.6.6,0,linear,0,1,prx,-1\n");
        Check(legacy.Mods.Single().Beat == 327.6 && legacy.Diagnostics.Single().Message.Contains("GML-style numeric prefix"), "legacy malformed number notices are not suppressed");
        // 一百万是上限本身而不是上限之外：0..999999 步长 1 正好一百万条，必须放行（上一段里多一条的写法才拒绝）。
        Check(VsmBeatRange.Parse("0(:999999:1)", VsmDocument.Number).Count == 1_000_000, "one-million event limit remains inclusive");
        // 以下是编辑器视角：运行时展开成 1192 条，编辑器里必须仍然只有 2 个 clip，
        // 否则作者一打开就会看到 596 个无法整体编辑的重复项。
        var document = VsmDocument.FromText(source);
        Check(document.Clips.Count() == 2 && document.Clips.All(c => c.ParenthesizedRepeat && c.RepeatCount == 596), "editor keeps each repeated source row as one editable clip");
        Check(document.Text == source, "opening the editor preserves source formatting and line endings");
        var snapshot = document.Lines.ToArray(); var clip = document.Clips.Last();
        // 只改一个值，不许顺手把括号语法展开成普通行，也不许吃掉行尾注释。
        document.Replace(clip with { To = ".5" });
        Check(document.Text.StartsWith(first + "\r\n-10.01(:1.9:0.02),") && document.Text.Contains(" // preserve comment\r\n"), "editing a value preserves the parenthesized beat token and comment");
        document.Restore(snapshot);
        Check(document.Text == source, "snapshot restore recovers exact repeat source text");
        // 整体平移：起点和终点同时 +1，-10.01 → -9.01。语法、条数和小数 offset 三者都要保住，
        // 而且改完的文本必须能被运行时解析器原样读回去（除了源里本来就带的那条注释提示，没有别的诊断）。
        document.Replace(clip with { Beat = clip.Beat + 1, RepeatEnd = clip.RepeatEnd + 1 });
        var moved = Read(document.Text); var editorMoved = VsmDocument.FromText(document.Text).Clips.Last();
        Check(editorMoved.ParenthesizedRepeat && editorMoved.RepeatCount == 596 && Math.Abs(editorMoved.Beat + 9.01) < 1e-9
            && moved.Diagnostics.Count == 1 && moved.Diagnostics[0].Message.Contains("never strips // comments"),
            "moving a repeat preserves syntax, fractional offset and runtime expansion");
        // 复制一份新的重复 clip：10..11 步长 0.25 是 5 条，1192+5=1197 说明新 clip 真的走了同一条运行时展开路径，
        // 而不是只在编辑器里显示出来。
        document.Add(editorMoved with { Id = Guid.NewGuid(), Beat = 10, RepeatEnd = 11, RepeatStep = .25 });
        Check(document.Lines.Last().Text.StartsWith("10(:11:0.25),") && Read(document.Text).Mods.Count == 1197, "duplicating a parenthesized clip roundtrips through the runtime reader");
        Console.WriteLine($"{checks} beat-range checks passed."); return checks;
    }
}
