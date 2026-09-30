using System.Globalization;
using System.Text.RegularExpressions;

namespace KuroakiGimmick.Core;

/// <summary>
/// 演出文本解析器：展开区间、规范化缓动并保留源行号。替换 mod 不应修改音符、资源文件或作者编排顺序。
/// </summary>
public static class VsmReader
{
    /// <summary>从文件整体替换 mod 与逐帧轨道；超过 64 MiB 直接拒绝，不做部分读取。</summary>
    public static void ReplaceMods(Chart c, string path)
    {
        if (new FileInfo(path).Length > 64 * 1024 * 1024)
        {
            throw new InvalidDataException("VSM exceeds 64 MiB.");
        }
        ReplaceModsLines(c, File.ReadLines(path), path);
    }

    /// <summary>编辑器预览直接消费内存中的修订文本；不写临时源文件覆盖原文件，也不另建一套解析器。</summary>
    public static void ReplaceModsText(Chart c, string text, string source) => ReplaceModsLines(c, text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'), source);

    static void ReplaceModsLines(Chart c, IEnumerable<string> lines, string path)
    {
        c.Mods.Clear();
        c.PerFrame.Clear();
        c.Metadata.Clear();
        bool perFrame = false;
        int lineNumber = 0;
        // read_mods_file 只跳过空行，既不认 "mods" 标记也不剥 // 注释：这两种行都会走到
        // string_split(line, ",") 然后取 parts[1]，在原版里直接越界崩掉。这里照旧宽容地读下去，
        // 但必须把首次出现的位置报出来，否则作者在预览里一切正常、进游戏才发现打不开。
        int strayMods = 0, comment0 = 0;
        double Number(string value) => ParseNumber(value, message => c.Diagnostics.Add(new(path, lineNumber, message)));
        foreach (var raw in lines)
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            if (line.StartsWith("//") || line.StartsWith('#') || line.StartsWith(';'))
            {
                if (comment0 == 0) comment0 = lineNumber;
                continue;
            }
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
            {
                if (comment0 == 0) comment0 = lineNumber;
                line = line[..comment].Trim();
            }
            try
            {
                if (line == "mpf")
                {
                    perFrame = true;
                    continue;
                }
                if (line == "mods")
                {
                    if (strayMods == 0) strayMods = lineNumber;
                    perFrame = false;
                    continue;
                }
                if (line.StartsWith('!'))
                {
                    int colon = line.IndexOf(':');
                    if (colon < 0)
                    {
                        throw new FormatException("Expected !key:value.");
                    }
                    var key = line[1..colon].Trim();
                    var value = line[(colon + 1)..].Trim();
                    if (key.Length == 0)
                    {
                        throw new FormatException("Empty VSM metadata key.");
                    }
                    c.Metadata[key] = value;
                    switch (key)
                    {
                        case "obj":
                            c.ObjectName = value;
                            break;
                        case "proxies":
                            c.Proxies = int.Parse(value, CultureInfo.InvariantCulture);
                            c.ProxyCountDeclared = true;
                            if (c.Proxies is < 0 or > 64)
                            {
                                throw new FormatException("Proxy count must be 0..64.");
                            }
                            break;
                            // read_mods_file 把任意 !key:value 条目存进
                            // modsDefinition.data；未知元数据不产生任何演出效果。
                        default:
                            break;
                    }
                    continue;
                }
                var parts = line.Split(',').Select(x => x.Trim()).ToArray();
                if (perFrame)
                {
                    if (parts.Length != 3)
                    {
                        throw new FormatException("mpf rows require start,end,function.");
                    }
                    var a = Number(parts[0]);
                    var b = Number(parts[1]);
                    if (b < a)
                    {
                        throw new FormatException("mpf interval is reversed.");
                    }
                    c.PerFrame.Add(new(a, b, parts[2]));
                    continue;
                }
                if (parts.Length != 7)
                {
                    throw new FormatException("Expected beat,duration,ease,from,to,mod,proxy.");
                }
                // beat/duration 的单位是拍，秒的换算留给 BPM map，解析层不做。
                var range = VsmBeatRange.Parse(parts[0], Number);
                double start = range.Start, step = range.Step;
                int count = range.Count;
                if (count + c.Mods.Count > 1_000_000)
                {
                    throw new FormatException("Beat range expands beyond one million events.");
                }
                var duration = Number(parts[1]);
                // 与原版一致：duration 非正表示立即赋值，而不是零长度 tween。
                // "_" 写成原版哨兵 573613，表示沿用该时刻的当前值，由时间轴展开时再解析成实际数值。
                var from = parts[3] == "_" ? 573613 : Number(parts[3]);
                var to = parts[4] == "_" ? 573613 : Number(parts[4]);
                var proxy = int.Parse(parts[6], CultureInfo.InvariantCulture);
                if (proxy < -1 || proxy > 63)
                {
                    throw new FormatException("Proxy must be -1..63.");
                }
                // 区间按 step 展开成多条事件；Order 取当前计数，让同一拍的事件保留作者声明顺序。
                for (int i = 0; i < count; i++)
                {
                    c.Mods.Add(new(start + i * step, duration, Easings.Normalize(parts[2]), from, to, parts[5], proxy, c.Mods.Count, lineNumber));
                }
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                c.Diagnostics.Add(new(path, lineNumber, ex.Message, true));
            }
        }
        if (strayMods > 0)
        {
            c.Diagnostics.Add(new(path, strayMods, "A bare \"mods\" line crashes the original read_mods_file: it starts in mods mode "
                + "and only recognises \"mpf\", so this row is split as an event and parts[1] goes out of range. Delete the line.", true));
        }
        if (comment0 > 0)
        {
            c.Diagnostics.Add(new(path, comment0, "The original read_mods_file never strips // comments: a comment line is split as an event "
                + "and parts[1] goes out of range, and a trailing comment lands in the proxy field. Delete every comment; blank lines are fine.", true));
        }
    }

    // read_mods_file 把数值字段交给 GML real()。公开的 HTML5 runner 使用
    // parseFloat，只消费数字前缀（327.6.6 读成 327.6）。
    // 这里保留该事件，并把被丢弃的后缀写进报告，不静默吞掉。
    static double ParseNumber(string value, Action<string> notice)
    {
        string token = value.Trim();
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double exact))
        {
            if (!double.IsFinite(exact))
            {
                throw new FormatException("Non-finite number.");
            }
            return exact;
        }
        var hex = Regex.Match(token, @"^0x[0-9a-fA-F]+");
        var match = hex.Success? hex : Regex.Match(token, @"^[+-]?(?:[0-9]+\.?[0-9]*|\.[0-9]+)(?:[eE][+-]?[0-9]+)?");
        if (!match.Success)
        {
            throw new FormatException("No numeric prefix in '" + token + "'.");
        }
        double number = hex.Success? Convert.ToUInt64(match.Value[2..], 16) : double.Parse(match.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture);
        if (!double.IsFinite(number))
        {
            throw new FormatException("Non-finite number.");
        }
        if (match.Length != token.Length)
        {
            notice($"GML-style numeric prefix: '{token}' read as {number.ToString("R",CultureInfo.InvariantCulture)}; suffix ignored. Native runner behavior is not verified.");
        }
        return number;
    }
}
