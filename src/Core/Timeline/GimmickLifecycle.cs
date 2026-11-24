namespace KuroakiGimmick.Core;

/// <summary>根据创建和销毁回调求值原版绘制生命周期。</summary>
public static class GimmickLifecycle
{
    /// <summary>回放截至给定时刻的创建和销毁回调，无生命周期定义时按启用处理。</summary>
    /// <param name="definition">控制创建和销毁的生命周期规则。</param>
    /// <param name="events">按时刻排列的回调。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>给定时刻处于活动状态时为 true。</returns>
    public static bool At(GimmickLifecycleDefinition? definition, IEnumerable<Timeline.Callback> events, double time)
    {
        if (definition == null) return true;
        bool alive = definition.Initial;
        foreach (var e in events)
        {
            if (e.Time > time) break;
            if (definition.Create.Contains(e.Name, StringComparer.Ordinal)) alive = true;
            if (definition.Destroy.Contains(e.Name, StringComparer.Ordinal)) alive = false;
        }
        return alive;
    }
}
