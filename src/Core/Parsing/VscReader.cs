using System.Globalization;

namespace KuroakiGimmick.Core;

/// <summary>
/// 文本音符解析器。逐行报告格式错误，保留可用音符；原文件单位转换集中在解析边界，输出统一为秒。
/// </summary>
// 自制曲加载器使用的文本谱面格式。时间与 hold 结束时刻都是毫秒，在此边界一次性转成秒。
public static class VscReader
{
    public static Chart Load(string path)
    {
        if (new FileInfo(path).Length > 128 * 1024 * 1024)
        {
            throw new InvalidDataException("VSC exceeds 128 MiB.");
        }
        var chart = new Chart
        {
            Title = Path.GetFileNameWithoutExtension(path)
        };
        int line = 0;
        foreach (var raw in File.ReadLines(path))
        {
            line++;
            string value = raw.Trim();
            if (value.Length == 0 || value.StartsWith("//") || value.StartsWith('#'))
            {
                continue;
            }
            try
            {
                var parts = value.Split(',', 4);
                if (parts.Length < 3)
                {
                    throw new FormatException("Expected time_ms,type,lane[,extra].");
                }
                // 第一列是毫秒，除以 1000 后 Note 内部统一是秒。
                double time = Number(parts[0]) / 1000;
                int type = int.Parse(parts[1], CultureInfo.InvariantCulture), lane = int.Parse(parts[2], CultureInfo.InvariantCulture);
                if (type is < 0 or > 8)
                {
                    throw new FormatException("Unknown note type: " + type);
                }
                if ((type is 0 or 2 or 6) && (lane < 0 || lane > 3) || (type is 1 or 7 or 8) && (lane < 0 || lane > 2))
                {
                    throw new FormatException("Invalid note lane.");
                }
                var extra = new Dictionary<int, object>();
                double end = time;
                if (type == 2)
                {
                    if (parts.Length != 4)
                    {
                        throw new FormatException("Hold requires absolute end time in milliseconds.");
                    }
                    end = Number(parts[3]) / 1000;
                    if (end < time)
                    {
                        throw new FormatException("Hold ends before its start.");
                    }
                    // 回写毫秒：extra[1] 沿用 VSB 的槽位含义，hold 存绝对结束毫秒，报告和导出共用同一表示。
                    extra[1] = end * 1000;
                }
                else if (type == 3)
                {
                    if (parts.Length != 4)
                    {
                        throw new FormatException("Timing row requires b:BPM metadata.");
                    }
                    foreach (var field in parts[3].Split('|'))
                    {
                        int colon = field.IndexOf(':');
                        if (colon < 0)
                        {
                            continue;
                        }
                        var key = field[..colon];
                        var data = field[(colon + 1)..];
                        if (data == "undefined" || data.Length == 0)
                        {
                            continue;
                        }
                        double n = Number(data);
                        if (key == "b")
                        {
                            if (n <= 0 || n > 10000)
                            {
                                throw new FormatException("Invalid BPM.");
                            }
                            // 变速行复用 extra[1] 存 BPM（hold 行存的是结束毫秒），由音符 type 区分。
                            extra[1] = n;
                        }
                        // 时间戳列始终是权威值；t 只是编辑器的对齐锚点，不参与换算。
                        else if (key is "v" or "s")
                        {
                            chart.Diagnostics.Add(new(path, line, $"VSC timing '{key}' is not applied."));
                        }
                    }
                }
                chart.Notes.Add(new(time, type, lane, end, extra));
                if (chart.Notes.Count > 2_000_000)
                {
                    throw new InvalidDataException("Too many VSC notes.");
                }
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                chart.Diagnostics.Add(new(path, line, ex.Message, true));
            }
        }
        // 按时间升序，后续查询才能二分；解析失败的行只留诊断，不影响其余音符。
        chart.Notes.Sort((a, b) => a.Time.CompareTo(b.Time));
        return chart;
    }

    static double Number(string text)
    {
        double n = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(n))
        {
            throw new FormatException("Non-finite value.");
        }
        return n;
    }
}

