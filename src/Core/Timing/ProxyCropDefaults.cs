namespace KuroakiGimmick.Core;

/// <summary>Default crop sentinels occur as both decimal and exported float32 values.</summary>
public static class ProxyCropDefaults
{
    public static bool Matches(double value, double sentinel) =>
        value == sentinel || value == (double)(float)sentinel;
}
