using System.Text.Json.Nodes;
using static KuroakiGimmick.Core.Windows.WindowMotionConfig;

namespace KuroakiGimmick.Core.Windows;

/// <summary>
/// 给定的 ExtCustomGimmick v0.3.2 WindowMovement dancer 的 CPU 转写。
/// 所有事件时间戳单位是秒。向后跳转时从事件重建状态，回放过程中绝不移动真实窗口。
/// native 副作用属于另一个需显式启用的呈现后端，不在这里做。
/// </summary>
public sealed class WindowMotionTimeline
{
    readonly WindowMotionConfig config;
    readonly JsonObject[] events;
    Dancer[] dancers = [];
    int[] order = [];
    int next;
    double lastTime = double.NegativeInfinity;
    readonly double baseW, baseH;
    public double End { get; }
    /// <summary>桌面尺寸只影响基准窗口大小的归一化比例；事件时间与位置不随它变化。</summary>
    public WindowMotionTimeline(WindowMotionConfig config, int desktopWidth = 1920, int desktopHeight = 1080)
    {
        this.config = config;
        // 时间非有限的事件在此丢弃（Validate 已把它们记为错误诊断）；按 t 稳定排序，同一时刻保留原声明顺序。
        events = (config.Events ?? new JsonArray()).OfType<JsonObject>().Where(e => double.IsFinite(Number(e, "t")))
            .OrderBy(e => Number(e, "t")).ToArray();
        End = events.Select(e => Number(e, "t") + Math.Max(0, Duration(e))).DefaultIfEmpty(0).Max();
        // 先把桌面裁成 16:9，再按原版 352×198 窗口取整数倍缩放（至少 2 倍），得到归一化的基准窗口宽高。
        double cw = Math.Min(Math.Max(1, desktopWidth), Math.Max(1, desktopHeight) * 16.0 / 9), ch = cw * 9 / 16;
        double scale = Math.Max(2, Math.Floor(Math.Min(cw / 352 * .5, ch / 198 * .5)));
        baseW = 352 * scale / cw; baseH = 198 * scale / ch;
        Reset();
    }
    void Reset()
    {
        dancers = Enumerable.Range(0, Math.Clamp(config.Count, 1, 64)).Select(i => new Dancer(i, baseW, baseH)).ToArray();
        order = Enumerable.Range(0, dancers.Length).ToArray(); next = 0; lastTime = double.NegativeInfinity;
    }
    /// <summary>求 <paramref name="seconds"/> 秒时刻的全部窗口位姿。向后跳转会整体重放事件，因此任意跳转都给出同一结果；只读，不触碰真实窗口。</summary>
    public IReadOnlyList<WindowPose> At(double seconds)
    {
        if (!double.IsFinite(seconds)) seconds = 0;
        // 时间倒流只能从头重建：dancer 状态是增量累积的，无法反向撤销。
        if (seconds < lastTime) Reset();
        while (next < events.Length && Number(events[next], "t") <= seconds)
        {
            var e = events[next++]; double condition = Number(e, "overlap", -1);
            // overlap 是原版的分辨率分支条件：小于 0 表示无条件执行，否则只有当它与"窗口是否够大"一致时才生效。
            if (condition >= 0 && (condition >= .5) != (baseW > .4)) continue;
            int window = Integer(e, "w");
            // ReorderWindows 的 w 不指窗口，越界检查只对其余 op 生效。
            if (Text(e, "op") != "ReorderWindows" && (window < 0 || window >= dancers.Length)) continue;
            var d = dancers[Math.Clamp(window, 0, dancers.Length - 1)]; double t = Number(e, "t");
            switch (Text(e, "op"))
            {
                // 未知 preset 不套用也不报错，事件本身仍留在配置里（Validate 已提示"保留但不预览"）。
                case "NewWindowDance": if (Presets.Contains(Text(e, "preset"))) d.Dance(e, t); break;
                case "WindowResize": d.Resize(e, t); break;
                case "HideWindow": d.Visible = Flag(e, "show"); break;
                case "SetWindowContent": d.Source = Math.Max(0, Integer(e, "room")); break;
                case "ReorderWindows":
                    // 越界与重复的下标被剔除，其余保持数组给定的顺序；未列出的窗口在后面按 Index 补齐。
                    if (e["order"] is JsonArray a)
                        order = a.Select(n => int.TryParse(n?.ToString(), out int i) ? i : -1).Where(i => i >= 0 && i < dancers.Length).Distinct().ToArray();
                    break;
            }
        }
        lastTime = seconds;
        var result = new List<WindowPose>();
        foreach (var d in dancers)
        {
            var p = d.Evaluate(seconds); var style = config.Style(d.Index);
            int rank = Array.IndexOf(order, d.Index); if (rank < 0) rank = order.Length + d.Index;
            // 0 号是主窗口，其余从 100 起编号，与 ProxyWindowLayout 的绑定 id 对齐；Z 取 -rank，排在前面的后绘制（更靠上）。
            result.Add(new(d.Index == 0 ? 0 : 100 + d.Index - 1, d.Index, p.X, p.Y, Math.Abs(p.W), Math.Abs(p.H),
                d.Visible && Math.Abs(p.W) > .00001 && Math.Abs(p.H) > .00001, d.Source, style.Title, style.Border, -rank));
        }
        return result;
    }
    /// <summary>ECG 的缓动名先规范化再求值；名字不在 Eases 白名单里一律退回 linear，不猜近似的那一个。</summary>
    public static double Ease(string name, double progress) => Easings.Eval(Eases.Contains(name) ? Easings.Normalize(name) : "linear", progress);
    static double Lerp(double a, double b, double t) => a + (b - a) * t;
    static double Repeat(double x, double size) => size <= 0 ? x : x - Math.Floor(x / size) * size;
    /// <summary>沿 <paramref name="angle"/> 方向从中心到 w×h 矩形边界的距离；命中哪一条边由角度与对角线的关系决定。</summary>
    static double Diagonal(double angle, double w, double h)
    {
        bool ySide = Math.Abs(Math.PI * .5 - Repeat(angle, Math.PI)) < Math.PI * .5 - Math.Atan2(h, w);
        double sin = Math.Sin(ySide ? angle : Math.PI * .5 - angle);
        // 夹住极小的 sin，避免角度贴近轴向时除出无穷大。
        if (Math.Abs(sin) < .000001) sin = sin < 0 ? -.000001 : .000001;
        return Math.Abs((ySide ? h : w) / sin);
    }
    /// <summary>确定性伪随机：抖动必须可重复，否则同一时刻截图两次结果不同。这不是录像里的真实随机序列。</summary>
    static double Hash(double seed) { double v = Math.Abs(Math.Sin(seed * 12.9898 + 78.233) * 43758.5453); return v - Math.Floor(v); }
    static (double X, double Y) Shake(int window, double index)
    {
        double angle = Hash(index * 2 + window * 131 + 1) * Math.PI * 2, radius = Math.Sqrt(Hash(index * 2 + window * 197 + 2));
        return (Math.Cos(angle) * radius, Math.Sin(angle) * radius);
    }
    /// <summary>单个标量的缓动段；时长为 0 时直接取目标值。<c>Set</c> 从当前插值位置续接，连续下发不会跳变。</summary>
    sealed class Tween(double value)
    {
        double a = value, b = value, start, duration; string ease = "Linear";
        public double At(double t) => duration <= 0 ? b : Lerp(a, b, Ease(ease, (t - start) / duration));
        public void Set(double target, double t, double length, string curve) { a = At(t); b = target; start = t; duration = Math.Max(0, length); ease = curve; }
        public void Reset(double v) { a = b = v; duration = 0; }
    }
    /// <summary>一个窗口的运动状态机。所有坐标都是 0..1 归一化桌面坐标，时间单位秒；只有 0 号窗口默认可见。</summary>
    sealed class Dancer(int index, double baseWidth, double baseHeight)
    {
        public int Index => index;
        public bool Visible = index == 0;
        public int Source;
        string preset = "Move", subEase = "Linear", easeType = "InOut", anchorX = "None", anchorY = "None";
        bool changed = true;
        double presetX = .5, presetY = .5, presetAx, presetAy, presetAngle, speed, frequency, period, resetTime, timeSpent;
        double transitionX = .5, transitionY = .5, cachedPivotX = .5, cachedPivotY = .5;
        double anchorScaleX = 1, anchorScaleY = 1, anchorPivotX = .5, anchorPivotY = .5;
        readonly Tween posX = new(.5), posY = new(.5), angle = new(0), ampX = new(0), ampY = new(0),
            scaleX = new(1), scaleY = new(1), pivotX = new(.5), pivotY = new(.5), edgeX = new(0), edgeY = new(0), transition = new(1);
        /// <summary>取当前 pivot；锚边模式下由缩放反推 pivot，使指定的那条边在缩放过程中保持不动。</summary>
        (double X, double Y) Pivot(double t, double sx, double sy)
        {
            double px = pivotX.At(t), py = pivotY.At(t);
            if (anchorX != "None" && Math.Abs(sx) > .000001)
                px = anchorPivotX + (sx - anchorScaleX) * ((anchorX == "RightEdge" ? 1 : 0) - anchorPivotX) / sx;
            if (anchorY != "None" && Math.Abs(sy) > .000001)
                py = anchorPivotY + (sy - anchorScaleY) * ((anchorY == "TopEdge" ? 1 : 0) - anchorPivotY) / sy;
            return (px, py);
        }
        /// <summary>
        /// 求值给定时刻的窗口矩形。返回的 X/Y 是按 pivot 修正后的中心，RawX/RawY 是修正前的 preset 位置——
        /// 后者才是下一次 Dance 续接的基准，两者不能互换。
        /// </summary>
        public (double X, double Y, double W, double H, double RawX, double RawY, double Sx, double Sy) Evaluate(double t)
        {
            double sx = scaleX.At(t), sy = scaleY.At(t), w = baseWidth * sx, h = baseHeight * sy;
            // clock 是当前 preset 自身的累计时间：same=Keep 时跨事件继续累加，Reset 时归零，决定周期运动的相位。
            double clock = t - resetTime + timeSpent;
            double x = changed ? presetX : posX.At(t), y = changed ? presetY : posY.At(t);
            double ax = changed ? presetAx : ampX.At(t), ay = changed ? presetAy : ampY.At(t), an = changed ? presetAngle : angle.At(t);
            switch (preset)
            {
                case "Move": x = posX.At(t); y = posY.At(t); break;
                case "Sway":
                    double cycle = clock * frequency, part = Repeat(cycle, 1); bool odd = Math.Floor(cycle) % 2 != 0;
                    double f = easeType == "Mirror" ? Ease(subEase, odd ? 1 - part : part) : Lerp(odd ? 1 : 0, odd ? 0 : 1, Ease(subEase, part));
                    double diag = Diagonal(an, 1, 1);
                    x += ax * diag * Math.Cos(an) * f; y += ax * diag * Math.Sin(an) * f; break;
                case "Wrap":
                    double bw = 1 + w, bh = 1 + h, forward = Diagonal(an, bw, bh);
                    double sway = Math.Sin(clock * frequency * Math.PI * 2) * (ax * .5) * Diagonal(an + Math.PI * .5, bw, bh);
                    x = Repeat(x + w * .5 + forward * Math.Cos(an) * clock * speed + sway * Math.Cos(an + Math.PI * .5), bw) - w * .5;
                    y = Repeat(y + h * .5 + forward * Math.Sin(an) * clock * speed + sway * Math.Sin(an + Math.PI * .5), bh) - h * .5; break;
                case "Ellipse":
                    double ellipse = an - Math.PI * 2 * speed * clock;
                    x += ax * Math.Cos(ellipse); y += ay * Math.Sin(ellipse); break;
                case "ShakePer":
                    double i = frequency <= 0 ? 0 : Math.Floor(Math.Max(0, clock * frequency));
                    var a = Shake(index, i); var b = Shake(index, i + 1);
                    double start = frequency <= 0 ? clock : i / frequency;
                    double life = Ease(subEase, Math.Clamp((clock - start) / Math.Max(period, .001), 0, 1));
                    x += Lerp(a.X, b.X, life) * ax; y += Lerp(a.Y, b.Y, life) * ay; break;
            }
            // 切换 preset 时用 transition 从旧位置渐变到新轨迹，避免瞬移；Move 自身已由 posX/posY 缓动，无需再叠一层。
            if (changed && preset != "Move") { x = Lerp(transitionX, x, transition.At(t)); y = Lerp(transitionY, y, transition.At(t)); }
            var pivot = Pivot(t, sx, sy);
            // edgeX/edgeY 在 pivot 与当前位置之间插值，实现 reference=Edge 的贴边参考；缓存供下次 Center 续接。
            cachedPivotX = Lerp(pivot.X, x, edgeX.At(t)); cachedPivotY = Lerp(pivot.Y, y, edgeY.At(t));
            return (x + (.5 - cachedPivotX) * w, y + (.5 - cachedPivotY) * h, w, h, x, y, sx, sy);
        }
        void Center(double x, double y, bool ux, bool uy, double t, double duration, string ease)
        {
            if (ux) { anchorX = "None"; edgeX.Reset(0); pivotX.Reset(cachedPivotX); pivotX.Set(x, t, duration, ease); }
            if (uy) { anchorY = "None"; edgeY.Reset(0); pivotY.Reset(cachedPivotY); pivotY.Set(y, t, duration, ease); }
        }
        /// <summary>应用一条 NewWindowDance。带 u 前缀的开关表示"该轴本次被改写"；未开启的轴沿用当前求值结果，不回落到默认值。</summary>
        public void Dance(JsonObject e, double t)
        {
            var before = Evaluate(t); string nextPreset = Text(e, "preset", "Move");
            // 换了 preset，或显式要求 same=Reset，才重置周期相位；否则累加已消耗时间以保持连续。
            changed = nextPreset != preset || Text(e, "same", "Keep") == "Reset";
            timeSpent = changed ? 0 : timeSpent + t - resetTime; resetTime = t; preset = nextPreset;
            presetX = Flag(e, "ux") ? Number(e, "x") : before.RawX; presetY = Flag(e, "uy") ? Number(e, "y") : before.RawY;
            presetAngle = Flag(e, "uangle") ? Number(e, "angle") : angle.At(t);
            presetAx = Flag(e, "uax") ? Number(e, "ax") : ampX.At(t); presetAy = Flag(e, "uay") ? Number(e, "ay") : ampY.At(t);
            double length = Number(e, "easeDur"); string ease = Text(e, "ease", "Linear");
            if (changed)
            {
                transitionX = before.RawX; transitionY = before.RawY; transition.Reset(0); transition.Set(1, t, length, ease);
                if (preset == "Move") { posX.Reset(before.RawX); posY.Reset(before.RawY); }
                speed = Number(e, "speed"); frequency = Number(e, "freq");
            }
            if (Flag(e, "ux")) posX.Set(Number(e, "x"), t, length, ease);
            if (Flag(e, "uy")) posY.Set(Number(e, "y"), t, length, ease);
            if (Flag(e, "uangle")) angle.Set(Number(e, "angle"), t, length, ease);
            if (Flag(e, "uax")) ampX.Set(Number(e, "ax"), t, length, ease);
            if (Flag(e, "uay")) ampY.Set(Number(e, "ay"), t, length, ease);
            period = Number(e, "period"); subEase = Text(e, "subEase", "Linear"); easeType = Text(e, "easeType", "InOut");
            if (Text(e, "reference") == "Edge")
            { anchorX = anchorY = "None"; edgeX.Set(1, t, length, ease); edgeY.Set(1, t, length, ease); }
            else if (edgeX.At(t) != 0 || edgeY.At(t) != 0) Center(.5, .5, true, true, t, length, ease);
        }
        /// <summary>缩放上限固定为 2，与原版一致；AnchorEdge 模式下先记下当前缩放与 pivot，缩放过程中被锚定的那条边不动。</summary>
        public void Resize(JsonObject e, double t)
        {
            var before = Evaluate(t); var pivot = Pivot(t, before.Sx, before.Sy);
            string mode = Text(e, "pivotMode"), anchor = Text(e, "anchor"), ease = Text(e, "ease", "Linear"); double length = Number(e, "dur");
            if (Flag(e, "usx"))
            {
                if (mode == "AnchorEdge" && anchor is "LeftEdge" or "RightEdge")
                { anchorX = anchor; anchorScaleX = before.Sx; anchorPivotX = pivot.X; edgeX.Reset(0); }
                scaleX.Set(Math.Min(2, Number(e, "sx")), t, length, ease);
            }
            if (Flag(e, "usy"))
            {
                if (mode == "AnchorEdge" && anchor is "TopEdge" or "BottomEdge")
                { anchorY = anchor; anchorScaleY = before.Sy; anchorPivotY = pivot.Y; edgeY.Reset(0); }
                scaleY.Set(Math.Min(2, Number(e, "sy")), t, length, ease);
            }
            if (mode == "Default" && (Flag(e, "upx") || Flag(e, "upy")))
                Center(Number(e, "px"), Number(e, "py"), Flag(e, "upx"), Flag(e, "upy"), t, length, ease);
        }
    }
}
