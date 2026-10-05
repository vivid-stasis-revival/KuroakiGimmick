using System.Text.RegularExpressions;
using KuroakiGimmick.Core.Documentation;

namespace KuroakiGimmick.Core.Editing;

/// <summary>mod 选择器与文档右键菜单共用的一套校验与事件构造，两处入口必须走同一份规则，否则同一操作会写出不同的事件。</summary>
internal static class GimmickAuthoring
{
    static readonly VsmReference.Entry[] extraGimmicks =
    [
        new() { Id = "frollsy.lr_slash", Kind = "mod", Name = "lr_slash", Scope = "custom",
            Category = "LR Extra Gimmicks", Summary = "竖向斜线：value1 向下取整并限制为 1–64（_ = 1），value2 为 GameMaker 打包颜色（255 = 红色，_ = 当前色）；duration 不影响生成条数。" },
        new() { Id = "frollsy.lr_slash_color", Kind = "mod", Name = "lr_slash_color", Scope = "custom",
            Category = "LR Extra Gimmicks", Summary = "设置后续竖向斜线颜色：value1 忽略，value2 为 GameMaker 打包颜色（255 = 红色），_ 不改变当前色；初始颜色为白色。" }
    ];

    public static VsmReference.Entry? Entry(string name) => extraGimmicks.FirstOrDefault(e => e.Name == name)
        ?? VsmReference.Shared.MatchMod(name).FirstOrDefault()?.Entry;

    public static VsmReference.Entry[] Search(string query)
    {
        string[] words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return VsmReference.Shared.Search(query, "mod").Concat(extraGimmicks.Where(e =>
            words.All(word => (e.Name + " " + e.Category + " " + e.Summary).Contains(word, StringComparison.OrdinalIgnoreCase)))).ToArray();
    }

    /// <summary>
    /// 按 mod 的作用域推荐 proxy 目标：proxy 级钳到 0..count-1，global 与 custom 级固定 -1（GLOBAL），其余允许 -1..count-1。
    /// 这里只是给 UI 一个合理初值，真正的合法性由 <see cref="Create"/> 强制。
    /// </summary>
    public static int SuggestedProxy(VsmReference.Entry? entry, int current, int count) =>
        entry?.Scope == "proxy" ? Math.Clamp(current, 0, Math.Max(0, count - 1)) :
        entry?.Scope is "global" or "custom" ? -1 : Math.Clamp(current, -1, Math.Max(-1, count - 1));

    /// <summary>
    /// 校验并构造一条 VSM 事件。名字必须是实际 mod 名，<c>[lane]</c>/<c>[tid]</c>/<c>[image]</c> 这类占位符要先替换掉。
    /// proxy 只能是 -1（GLOBAL）或已声明的 proxy 下标，上限 63；作用域与 proxy 不匹配时直接拒绝，不做静默修正。
    /// 取值里的 "_" 原样保留（表示沿用当前值），其余数值统一规范化写法。
    /// </summary>
    public static VsmDocument.Clip Create(string name, double beat, double duration, string ease,
        string from, string to, int proxy, int proxyCount, VsmReference.Entry? template = null)
    {
        name = name.Trim();
        if (name.Length is 0 or > 256 || !Regex.IsMatch(name, @"^[\p{L}_][\p{L}\p{M}\p{N}_:.\-]*$"))
            throw new FormatException("Enter the actual mod name; replace [lane], [tid] or [image] first.");
        // beat 与 duration 单位都是拍，duration 不可为负。
        if (!double.IsFinite(beat) || Math.Abs(beat) > 1e8 || !double.IsFinite(duration) || duration < 0 || duration > 1e8)
            throw new FormatException("Beat must be finite; duration must be 0..100000000 beats.");
        if (proxy < -1 || proxy > 63 || proxy >= proxyCount)
            throw new FormatException("Proxy must be GLOBAL (-1) or an existing declared proxy.");
        // 模板自带的匹配正则带 100 ms 超时，防止文档里一条病态 pattern 把 UI 卡死。
        if (template is { MatchPattern.Length: > 0 } &&
            !new Regex(template.MatchPattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).IsMatch(name))
            throw new FormatException("The name does not match the selected template (check identifier / lane range).");
        var info = template ?? Entry(name);
        if (info?.Scope == "global" && proxy != -1) throw new FormatException("This mod requires GLOBAL (-1).");
        if (info?.Scope == "proxy" && proxy < 0) throw new FormatException("This mod requires a proxy target.");
        ease = Easings.Normalize(ease.Trim());
        if (!Easings.IsKnown(ease)) throw new FormatException("Unknown easing.");
        string Val(string s) => s.Trim() == "_" ? "_" : VsmDocument.N(VsmDocument.Number(s));
        return new(Guid.NewGuid(), beat, duration, ease, Val(from), Val(to), name, proxy);
    }
}
