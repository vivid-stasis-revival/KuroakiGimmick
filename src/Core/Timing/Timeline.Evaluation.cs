namespace KuroakiGimmick.Core;

/// <summary>
/// 无显示状态的时间轴。构建时按拍数和声明顺序整理轨道；查询不依赖上一帧，因此预览、倒拖和导出可以共享结果。
/// </summary>
public sealed partial class Timeline
{
    /// <summary>
    /// 这两个原版 filter 的 0/负值会把画面打黑。K/G 作为作者工具把运行值钳到安全下限，
    /// 源文件仍保留作者输入并由构造阶段给出 warning。其它连续 mod 不在这里统一舍入。
    /// </summary>
    static double SafeModValue(string name, double value) =>
        name is "fx_underwater" or "fx_chroma_distort" ? Math.Max(.01, value) : value;

    /// <summary>读取基础轨道后应用声明式别名、逐帧覆盖与回调淡入；不修改时间轴状态。</summary>
    public double Get(string name, double t, int proxy = -1)
    {
        double initial = Initial(name, proxy);
        var key = (name, proxy);
        var v = Tracks.TryGetValue(key, out var list) ? Evaluate(list, t, initial) : initial;
        if (proxy == -1 && Native?.Data is { } definition)
        {
            // 别名与逐帧输出绑定在加载时已按 DAG 校验过，这里的递归不会成环爆栈。
            if (definition.ModAliases.TryGetValue(name, out var alias))
            {
                return SafeModValue(name, Get(alias, t));
            }
            if (frameBindings.TryGetValue(name, out var bindings))
            {
                // 逐帧函数的区间是拍，因此先把当前秒数换算回拍；边界取开区间，与源引擎的触发语义一致。
                double beat = Bpm.Beat(t);
                foreach (var binding in bindings)
                {
                    if (beat > binding.StartBeat && beat < binding.EndBeat)
                    {
                        return SafeModValue(name, binding.Value.Value(this, t));
                    }
                }
            }
            if (definition.CallbackFades.TryGetValue(name, out var fade) && (!fade.RequireResourcePack || Native.ResourcePackLoaded)
                && (fade.RequireResourceRoom == null || Native.ResourceRoom == fade.RequireResourceRoom))
            {
                if (!firstCallbacks.TryGetValue(fade.Callback, out double start) || t < start)
                {
                    return SafeModValue(name, fade.From);
                }
                // fade 的 Duration 与 t 都是秒，不是拍：这里不经过 BPM map。
                double completion = start + fade.Duration;
                if (t < completion)
                {
                    return SafeModValue(name, fade.From + (fade.To - fade.From) * Easings.Eval(fade.Ease, (t - start) / fade.Duration));
                }
                // 隐式的房间写入会盖掉更早、已经结束的轨道，但盖不掉更晚或仍在进行中的轨道。
                if (list == null || !list.Any(segment => segment.Time <= t && (segment.Time >= completion
                    || segment.Time + segment.Duration > completion)))
                {
                    return SafeModValue(name, fade.To);
                }
            }
        }
        return SafeModValue(name, v);
    }

    /// <summary>二分选择已经开始的最后一个事件；同一时间的后声明事件覆盖先声明事件。</summary>
    public static double Evaluate(List<Segment> list, double t, double initial)
    {
        int low = 0, high = list.Count;
        while (low < high)
        {
            int m = (low + high) / 2;
            if (list[m].Time <= t)
            {
                low = m + 1;
            }
            else
            {
                high = m;
            }
        }
        return low == 0 ? initial : list[low - 1].Value(t);
    }
}

