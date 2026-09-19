using System.Text;
using KuroakiGimmick.Core.Documentation;
using KuroakiGimmick.UI;

namespace KuroakiGimmick.Core;

/// <summary>确定性 CPU 测试：UI 缓动与帮助文章排版。由 --reference-self-test 和 --editor-self-test 调用，不碰 GPU。</summary>
internal static class UiPresentationSelfTest
{
    /// <summary>返回通过的检查数，由 ReferenceSelfTest 折算进它的总数。时间全部手工推进，不依赖真实帧率。</summary>
    internal static int Checks()
    {
        int count = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("UI PRESENTATION TEST FAILED: " + label);
            count++; Console.WriteLine("PASS " + label);
        }
        static bool Near(float a, float b) => Math.Abs(a - b) < .0001f;
        // Begin 的第二个参数是"是否允许动画"（false 对应降低动态偏好）。时间由测试直接喂进去，
        // 因此下面所有数值都是解析解，不受测试机帧率影响。
        var motion = new UiMotion();
        motion.Begin(0, true);
        Check(motion.Show("modal", true, .2) == 0, "open begins transparent");
        motion.Begin(.1, true);
        // 走完一半时长（.1/.2）的值必须是 .875 = 1-(1-.5)^3，即 ease-out 三次缓动按 UI 时钟求值；
        // 如果改成"每帧乘一个固定系数"，这里就会变成依赖帧数的另一个值。
        float middle = motion.Show("modal", true, .2);
        Check(Near(middle, .875f), "cubic half-time is based on UI clock");
        // 中途反向必须从当前显示值接着走，不能先跳回 1 再往下掉 —— 那会在快速开关面板时闪一下。
        Check(Near(motion.Show("modal", false, .2), middle), "reversal starts at current displayed value");
        motion.Begin(.3, true);
        Check(Near(motion.Show("modal", false, .2), 0), "close reaches a finite zero endpoint");
        motion.Show("modal", true, .2);
        motion.Begin(.31, false);
        // 关闭动画时两个方向都要瞬时到端点，不能只处理淡入而让淡出仍然插值。
        Check(motion.Show("modal", true, .2) == 1 && motion.Show("modal", false, .2) == 0, "reduced motion snaps both directions");
        motion.Begin(.32, true);
        // 拖拽这类直接操纵要能绕过插值，否则控件会滞后于指针。
        motion.Snap("drag", 42);
        Check(motion.To("drag", 42) == 42, "direct manipulation can bypass interpolation");
        motion.Begin(30, true);
        // 长时间不再被查询的动画 key 必须自动回收，否则每开一次面板就多留一条状态，长期运行会一直涨。
        Check(motion.Count == 0, "unused animation keys expire");
        // 同一逻辑时刻（半秒）分别按 30/60/144 fps 步进，结果必须一致：tween 位置只能由时间决定。
        // 60 fps 的 87.5 与上面的 .875 是同一条三次缓动曲线。
        float AtHalf(int fps)
        {
            var a = new UiMotion(); a.Begin(0, true); a.To("value", 100, 1, 0);
            for (int i = 1; i <= fps / 2; i++) { a.Begin(i / (double)fps, true); a.To("value", 100, 1); }
            return a.To("value", 100, 1);
        }
        Check(Near(AtHalf(30), AtHalf(144)) && Near(AtHalf(60), 87.5f), "frame rates do not change tween position at the same time");
        // NaN 时间配 NaN 目标值：结果必须仍是有限数。一个 NaN 漏进顶点缓冲会让整批 UI 直接消失，
        // 而且不报错，只表现为界面空白，所以在这一层就掐断。
        var invalid = new UiMotion(); invalid.Begin(double.NaN, true);
        Check(float.IsFinite(invalid.To("nan", float.NaN, double.NaN)), "non-finite input cannot contaminate UI vertices");

        // 用固定比例的假度量（ASCII 0.55 em、CJK 1 em）代替真实字体：排版规则才是被测对象，
        // 换字体文件不应该让这些断言变红。
        static float Measure(string s, float size, bool bold) => s.EnumerateRunes().Sum(r => r.IsAscii ? size * .55f : size);
        // 一条人造条目，四种 block 各一个，覆盖标题锚点、段落折行、代码原样保留和表格行链接。
        var entry = new VsmReference.Entry
        {
            Id = "test", Name = "Test", Blocks = [
                new() { Kind = "heading", Text = "参数" },
                new() { Kind = "paragraph", Text = "值的范围为 [0,1]，保留 [lane] 与 [tid]。" },
                new() { Kind = "code", Text = "{\n    \"ENABLE_TEXT\": false\n}" },
                new() { Kind = "table", Columns = ["参数", "作用", "说明"],
                    Rows = [["notealpind[lane]", "调整透明度", "与 notealp 累乘"]], RowLinks = ["mod.test"] }
            ]
        };
        // 三种宽度覆盖窄/中/宽布局。以下几条是排版的硬契约，任何一条破了都会在帮助页面上直接看出来。
        foreach (float width in new[] { 300f, 600f, 920f })
        {
            var layout = ReferenceArticleLayout.Build(entry, width, Measure);
            Check(layout.Anchors.Length == 1 && layout.Anchors[0].Title == "参数", "heading creates page anchor at width " + width);
            Check(layout.Items.Zip(layout.Items.Skip(1)).All(p => p.First.Top + p.First.Height <= p.Second.Top), "article blocks never overlap at width " + width);
            // 滚动范围必须盖住最后一个 block，否则末尾内容永远滚不到。
            Check(layout.Items[^1].Top + layout.Items[^1].Height <= layout.Height, "scroll range covers final block at width " + width);
            // 代码块有两份数据：复制用的原文必须逐字符等于源 block，显示用的行则要保留前导 4 空格缩进。
            var code = layout.Items.Single(item => item.Kind == "code");
            Check(code.Text == entry.Blocks[2].Text && code.Lines[0][1].StartsWith("    ", StringComparison.Ordinal), "code copy and displayed indent preserved at width " + width);
            // 表格行的跳转目标不能在折行重排时丢掉；列宽之和必须正好等于可用宽度，不留缝也不溢出。
            var row = layout.Items.Single(item => item.Kind == "table-row");
            Check(row.Link == "mod.test" && Near(row.Widths.Sum(), width), "click target and table widths retained at width " + width);
            // 单元格正文要先减掉 24 的左右内边距再判断是否放得下。
            Check(row.Lines.SelectMany((cell, i) => cell.Select(line => Measure(line, 15, false) <= row.Widths[i] - 24 + .01f)).All(ok => ok), "table lines fit their cells at width " + width);
        }
        string source = "    abc 中文 [lane] e\u0301";
        var wrapped = ReferenceArticleLayout.CodeLines(source, 40, s => Measure(s, 16, false));
        // 代码折行不做任何规范化：前导空格、CJK、方括号占位符都必须原样拼回去，组合重音也不许被切到下一行。
        Check(string.Concat(wrapped) == source, "code wrapping preserves characters and whitespace");
        Check(wrapped.All(line => !line.StartsWith('\u0301')), "grapheme wrapping does not separate combining accent");
        return count;
    }
}
