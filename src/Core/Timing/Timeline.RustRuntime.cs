namespace KuroakiGimmick.Core;

public sealed partial class Timeline
{
    // Immutable native acceleration is attached only AFTER all original timeline,
    // particles, callbacks and per-frame bindings have been fully constructed.
    internal RustTimelineBridge.Runtime? RustTracks { get; private set; }

    internal void PrepareFrame(double time) => RustTracks?.PrepareFrame(time);

    private bool TryNativeBase(string name, double time, int proxy, out double value)
    {
        value = 0;
        if (liveScroll && name == "scrollspeed") return false;
        if (RustTracks == null) return false;
        // These transforms need C# recursion or semantic filters on top of raw
        // tracks. The default values and raw tracks can still be accelerated.
        if (proxy == -1 && Native?.Data is { } definition &&
            (definition.ModAliases.ContainsKey(name) ||
             frameBindings.ContainsKey(name) ||
             definition.CallbackFades.ContainsKey(name))) return false;
        return RustTracks.TryGet(name, proxy, time, out value);
    }
}
