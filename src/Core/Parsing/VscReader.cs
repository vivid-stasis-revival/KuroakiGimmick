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
                var parts = value.Split(',', 5);
                // CSM 3.4.0 才定义第五列 modExtra。只按源文本实际出现的第五列识别格式；
                // 没出现就保持 Legacy，绝不因为 K/G 自己支持 3.4 而替旧谱“升级”格式。
                if (parts.Length >= 5) chart.VscDialect = VscDialect.Csm340;
                if (parts.Length < 3)
                {
                    throw new FormatException("Expected time_ms,type,lane[,extra[,modExtra]].");
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
                Dictionary<string, string?>? modExtra = null;
                if (parts.Length >= 5 && parts[4].Length > 0)
                {
                    modExtra = ParseModExtra(parts[4]);
                }
                double end = time;
                if (type == 2)
                {
                    if (parts.Length < 4)
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
                    if (parts.Length < 4)
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
                chart.Notes.Add(new(time, type, lane, end, extra, modExtra));
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
        // 同上：排序稳定，同一时刻的音符保留源文件里的先后。
        chart.Notes.StableSortByTime();
        return chart;
    }

    /// <summary>
    /// CSM 3.4.0 的第五列：key:value|key2:value2。与原脚本一致，只取冒号后的第一段；
    /// undefined 保留为 null，未知键和值原样保存，K/G 不替第三方 mod 猜语义。
    /// </summary>
    static Dictionary<string, string?> ParseModExtra(string text)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string field in text.Split('|'))
        {
            var parts = field.Split(':');
            if (parts.Length < 2) continue;
            string key = parts[0].Trim();
            if (key.Length == 0) continue;
            string raw = parts[1];
            result[key] = raw == "undefined" ? null : raw.Trim();
        }
        return result;
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

