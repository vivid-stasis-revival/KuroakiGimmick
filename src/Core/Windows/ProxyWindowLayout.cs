using System.Text.Json.Nodes;
using static KuroakiGimmick.Core.Windows.WindowMotionConfig;
namespace KuroakiGimmick.Core.Windows;

/// <summary>按输入的 ECG schema 做通用的 normal-proxy 绑定；不引入任何按曲名特化的绑定别名。</summary>
public static class ProxyWindowLayout
{
    /// <summary>
    /// 求出 <paramref name="t"/>（秒）时刻各 proxy 窗口的位姿，结果按 Z 升序返回。
    /// 输入的 <paramref name="movement"/> 位姿按 Id 覆盖合并，绑定表的表序就是 Index，不排序、不去重。
    /// </summary>
    public static IReadOnlyList<WindowPose> Compose(Session s, double t, IReadOnlyList<WindowPose> movement)
    {
        var result = movement.ToDictionary(p => p.Id);
        int index = 0;
        foreach (var b in (s.WindowMotion.Bindings ?? new JsonArray()).OfType<JsonObject>())
        {
            int i = index++, proxy = Integer(b, "proxy", i), id = Integer(b, "window", 100 + i), source = Integer(b, "source", 1000 + i);
            // 引用不存在的 proxy 直接跳过；窗口总数上限 64，超出后只允许更新已有 Id。
            if (proxy < 0 || proxy >= s.Chart.Proxies || result.Count >= 64 && !result.ContainsKey(id)) continue;
            double M(string n) => s.Timeline.Get(n, t, proxy);
            // 三档缩放相乘后再乘以绕 X/Y 轴旋转的余弦，得到原版那种"透视只压扁不真透视"的等效缩放；角度是度，需转弧度。
            double scale = M("przm") * M("przmb") * M("przmc"), sx = scale * M("przx") * Math.Cos(M("prrx") * Math.PI / 180),
                sy = scale * M("przy") * Math.Cos(M("prry") * Math.PI / 180), angle = (M("prrz") + M("prrzb")) * s.Timeline.Get("rotdir", t) * Math.PI / 180;
            // 裁剪区默认值（prct=.08 / prcb=0 / prcl=prcr=.35）表示"未被谱面改写"，此时沿用原版的固定矩形；
            // 一旦有一侧被改写，才切到按 mod 值计算——这样旧资源的像素级取值不会因为引入新公式而漂移。
            double defLeft = 113 - M("shxa"), left = defLeft, right = left + 95 + 2 * M("shxa"), top = 0, bottom = 165;
            if (M("prct") != .08 || M("prcb") != 0) { top = 180 * M("prcb") - 1; bottom = 180 - Math.Ceiling(180 * M("prct")); }
            if (M("prcl") != .35 || M("prcr") != .35) { left = M("prcl"); right = M("prcr"); }
            // 裁剪区中心相对原版锚点 (47, 82) 的偏移先按缩放拉伸，再随 Z 轴旋转，保证旋转以窗口中心而非画布原点为轴。
            double w = right - left, h = bottom - top, dx = (left - defLeft + w * .5 - 47) * sx, dy = (top + h * .5 - 82) * sy;
            double x = 160 + M("prx") + M("prxb") + M("prxc") + M("prxd") + dx * Math.Cos(angle) - dy * Math.Sin(angle);
            double y = 82 + M("pry") + M("pryb") + M("pryc") + M("pryd") + dx * Math.Sin(angle) + dy * Math.Cos(angle);
            // 1e-3 以下的 alpha、缩放或尺寸按不可见处理，避免为退化到零面积的窗口创建原生窗口。
            bool visible = M("pra") > .001 && Math.Abs(sx) > .001 && Math.Abs(sy) > .001 && w > .001 && h > .001;
            // 位置与尺寸除以 320/180 转成归一化桌面坐标；Crop* 仍留在 320×180 逻辑空间。负缩放转为翻转标记。
            result[id] = new(id, i, x / 320, y / 180, Math.Abs(w * sx) / 320, Math.Abs(h * sy) / 180, visible, source,
                Text(b, "title", $"Proxy {proxy}"), Flag(b, "border", true), i, proxy, left, top, w, h, sx < 0, sy < 0, M("pra"));
        }
        // 同 source 的多个 proxy 窗口以最后声明者为准，与"同一时间后声明覆盖先声明"的事件规则一致。
        var sources = result.Values.Where(p => p.Proxy >= 0).GroupBy(p => p.Source).ToDictionary(g => g.Key, g => g.Last());
        foreach (int id in result.Keys.ToArray())
        {
            var p = result[id];
            // 自身没绑定 proxy 的窗口从同 source 的窗口继承裁剪、翻转和 alpha，使镜像窗口显示同一份内容。
            if (p.Proxy < 0 && p.Source != 0 && sources.TryGetValue(p.Source, out var source))
                result[id] = p with { Proxy = source.Proxy, CropX = source.CropX, CropY = source.CropY, CropW = source.CropW,
                    CropH = source.CropH, FlipX = source.FlipX, FlipY = source.FlipY, Alpha = source.Alpha };
        }
        return result.Values.OrderBy(p => p.Z).ToArray();
    }
}
