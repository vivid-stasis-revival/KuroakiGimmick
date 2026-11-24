// 在可用时通过 Rust 轨道缓存读取基础值。

namespace KuroakiGimmick.Core;

public sealed partial class Timeline
{
    // 原时间轴、粒子、回调与逐帧绑定全部构建完成后，才挂接只读原生加速数据。
    internal RustTimelineBridge.Runtime? RustTracks { get; private set; }

    internal void PrepareFrame(double time) => RustTracks?.PrepareFrame(time);

    private bool TryNativeBase(string name, double time, int proxy, out double value)
    {
        value = 0;
        if (liveScroll && name == "scrollspeed") return false;
        if (RustTracks == null) return false;
        // 这些变换须在原轨道上进行 C# 递归求值或语义筛选；基础值与原始轨道仍可加速。
        if (proxy == -1 && Native?.Data is { } definition &&
            (definition.ModAliases.ContainsKey(name) ||
             frameBindings.ContainsKey(name) ||
             definition.CallbackFades.ContainsKey(name))) return false;
        return RustTracks.TryGet(name, proxy, time, out value);
    }
}
