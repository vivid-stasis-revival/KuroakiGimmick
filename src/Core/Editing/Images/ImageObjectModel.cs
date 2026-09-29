namespace KuroakiGimmick.Core.Editing;

/// <summary>
/// 判定 VSM 事件能否用画布上的直接操作来改，以及把事件归并成 UI 看到的动画组。
/// 这里只做只读判断，永不改动文档；判定从严——任何读不准的情况都要求用户去改原始事件。
/// </summary>
public static class ImageObjectModel
{
    /// <summary>这条事件是否作用于给定图片实例。名字形如 <c>imgx_&lt;id&gt;</c>，前缀是属性，后缀是实例 ID。</summary>
    public static bool ForImage(VsmDocument.Clip c, string id) => CustomImages.TryMod(c.Name, out _, out var target) && target == id;
    /// <summary>
    /// 取值是否为可直接编辑的固定数值。"_" 表示沿用当前值，573613 是引擎里“取运行时当前值”的哨兵值——
    /// 两者都要在运行时才知道结果，对它们做加减或改写等于凭空篡改作者的意图。
    /// </summary>
    public static bool Fixed(string value) => value != "_" && double.TryParse(value, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) && n != 573613;
    /// <summary>
    /// 一条事件“简单”到可以被生成式改写的全部条件：不属于任何 proxy（Proxy == -1）、没有 repeat、duration 非负、
    /// 缓动是已知名字、属性能映射到某个基本通道、首尾值都是固定数值。少一条就必须走原始事件编辑器。
    /// </summary>
    public static bool Simple(VsmDocument.Clip c) => c.Proxy == -1 && c.RepeatEnd == null && c.Duration >= 0 && Easings.IsKnown(c.Ease) &&
        CustomImages.TryMod(c.Name, out string kind, out _) && ImageEditPose.Channel(kind) != ImageChannels.None && Fixed(c.From) && Fixed(c.To);
    /// <summary>
    /// 把某张图片的事件按 (拍, duration, 缓动, proxy) 四元组聚成动画组，供 UI 以“一段动画”为单位展示与操作。
    /// 排序用拍号加源行号，因此同拍的组仍按源文件的书写顺序排列。
    /// </summary>
    public static ImageMotionGroup[] Groups(VsmDocument document, string id)
    {
        var source = document.Clips.Where(c => ForImage(c, id)).ToArray();
        var result = new List<ImageMotionGroup>();
        foreach (var range in source.GroupBy(c => (c.Beat, c.Duration, c.Ease, c.Proxy)))
        {
            var rows = range.ToArray();
            // 不要把重复写入或区间不一致的事件并成一组，保留原有的事件身份。同一属性写了两遍就各自成组，让用户看见真实的源结构。
            bool merge = rows.All(Simple) && rows.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() == rows.Length;
            // 位置和缩放都是成对的轴。只写了 x 没写 y 时不给成对的手柄，否则拖一下就会凭空补出一条作者没写过的轴。
            bool CompleteAxes(VsmDocument.Clip[] clips)
            {
                var properties = clips.Select(c => c.Name[..c.Name.IndexOf('_')]).ToHashSet(StringComparer.Ordinal);
                return properties.Contains("imgx") == properties.Contains("imgy") && properties.Contains("imgscalex") == properties.Contains("imgscaley");
            }
            if (merge) result.Add(new(id, rows, CompleteAxes(rows)));
            else foreach (var c in rows) result.Add(new(id, [c], Simple(c) && CompleteAxes([c])));
        }
        return result.OrderBy(g => g.Beat).ThenBy(g => document.SourceLine(g.Id)).ToArray();
    }
    /// <summary>
    /// 图片“出场”的那一拍：优先取除透明度以外最早的姿态事件，其次取任意最早事件，都没有则为 0。
    /// 排除 imgalp_ 是因为淡入常常单独写在更早的位置，拿它当初始拍会把摆位写到画面还没出现的时候。
    /// </summary>
    public static double InitialBeat(VsmDocument document, string id)
    {
        var rows = document.Clips.Where(c => ForImage(c, id) && c.Proxy == -1).ToArray();
        var pose = rows.Where(c => !c.Name.StartsWith("imgalp_", StringComparison.Ordinal) &&
            CustomImages.TryMod(c.Name, out var p, out _) && ImageEditPose.Channel(p) != ImageChannels.None).ToArray();
        return pose.Length > 0 ? pose.Min(c => c.Beat) : rows.Length > 0 ? rows.Min(c => c.Beat) : 0;
    }
    /// <summary>
    /// 返回禁止直接操作变换的原因，空字符串表示可以。UI 拿它来决定是给手柄还是只让改原始事件。
    /// 两种排除情况都不是保守起见，而是画布上算出来的矩形与实际画面根本对不上。
    /// </summary>
    public static string DirectEditIssue(VsmDocument document, CustomImages.Item item, IEnumerable<CustomImages.Item> all)
    {
        var layerIds = all.Where(i => i.Layer == item.Layer).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var c in document.Clips)
        {
            if (!CustomImages.TryMod(c.Name, out var property, out var id)) continue;
            // 时间偏移在原渲染器里是按图层循环共享的：同图层里排在前面的实例会连带影响这一个，
            // 单看本实例的事件算不出它当前在哪，所以整个图层的变换都只能改原始事件。
            if (layerIds.Contains(id) && (property is "imgxtime" or "imgytime" or "imgscaleytime"))
                return "Time-linked layer: use raw events for transforms.";
            // 斜切不为 0 时画出来的是平行四边形，而手柄与命中测试都基于 Matrix3x2 的轴对齐矩形，拖动结果必然和画面不符。
            if (id == item.Id && (property is "imgskewx" or "imgskewy") &&
                (!Fixed(c.From) || !Fixed(c.To) || !VsmDocument.NearlyEqual(VsmDocument.Number(c.From), 0) || !VsmDocument.NearlyEqual(VsmDocument.Number(c.To), 0)))
                return "Skewed image: use raw events for transforms.";
        }
        return "";
    }
}

