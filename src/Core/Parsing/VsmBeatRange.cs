using System.Globalization;
using System.Text.RegularExpressions;

namespace KuroakiGimmick.Core;

/// <summary>
/// 运行时展开与编辑器保源片段共用的拍区间语法。单位是拍，展开出的每条事件仍由 BPM map 换算成秒。
/// </summary>
public sealed record VsmBeatRange(double Start, double? End, double Step, bool Parenthesized)
{
    /// <summary>展开出的事件条数；1e-7 容差吸收浮点除法误差，避免 0:1:0.1 这类区间少算最后一拍。</summary>
    public int Count => (int)(Math.Floor(((End ?? Start) - Start) / Step + 1e-7) + 1);
    /// <summary>解析 start、start:end 或 start:end:step；step 必须为正且区间递增，展开条数上限一百万。</summary>
    public static VsmBeatRange Parse(string token, Func<string, double> number)
    {
        token = token.Trim();
        bool parenthesized = token.Contains('(') || token.Contains(')');
        string[] parts;
        if (parenthesized)
        {
            var match = Regex.Match(token, @"^([^():]+)\(\s*:\s*([^():]+)\s*:\s*([^():]+)\s*\)$", RegexOptions.CultureInvariant);
            if (!match.Success) throw new FormatException("Expected start(:end:step) beat range.");
            parts = [match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value];
            // 带括号的结构化写法绝不能被 GML 数字前缀恢复逻辑悄悄截断，因此在此改用严格解析。
            number = value => double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n)
                ? n : throw new FormatException("Invalid number in parenthesized beat range: '" + value.Trim() + "'.");
        }
        else
        {
            parts = token.Split(':');
            if (parts.Length > 3) throw new FormatException("Expected start:end:step beat range.");
        }
        double start = number(parts[0]), step = parts.Length > 2 ? number(parts[2]) : 1;
        double? end = parts.Length > 1 ? number(parts[1]) : null;
        if (!double.IsFinite(start) || !double.IsFinite(end ?? start) || !double.IsFinite(step) || step <= 0 || end < start)
            throw new FormatException("Beat range must be finite and ascend with a positive step.");
        double count = Math.Floor(((end ?? start) - start) / step + 1e-7) + 1;
        if (!double.IsFinite(count) || count > 1_000_000) throw new FormatException("Beat range expands beyond one million events.");
        return new(start, end, step, parenthesized);
    }
}
