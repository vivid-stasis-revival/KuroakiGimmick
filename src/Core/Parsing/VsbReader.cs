// 解码二进制谱面，处理扩展 mod 编号并将数据交给 Chart。


namespace KuroakiGimmick.Core;

/// <summary>
/// VSC1 二进制解码器：小端、字节对齐、单精度源数字。扩展 mod 在读到对象头时注册；截断和非法标记必须带字节位置报错。
/// </summary>
// VSC1：字节对齐的小端 GameMaker buffer；浮点字段一律 f32，读入后再升为 double。
public static class VsbReader
{
    /// <summary>从 VSB 二进制谱面读取音符、元数据及演出数据。</summary>
    /// <param name="path">VSB 文件路径。</param>
    /// <param name="definitionPath">可选的对象定义路径。</param>
    /// <param name="profile">演出配置键。</param>
    /// <returns>解析后的二进制谱面。</returns>
    public static Chart Load(string path, string? definitionPath = null, string profile = "auto")
    {
        if (RustEngineBridge.TryLoadVsb(path, definitionPath, profile, out var native) && native != null)
            return native;
        return LoadOriginal(path, definitionPath, profile);
    }

    // Rust 的 verify 模式也走这个入口；用同一对象定义解析规则给公共解码器提供扩展表。
    internal static Chart LoadOriginal(string path, string? definitionPath = null, string profile = "auto")
    {
        string? root = Path.GetDirectoryName(path);
        var parsed = KenomaGMK.Core.VsbReader.Load(path,
            name => GimmickCatalog.ReadExtraMods(name, root, definitionPath, profile),
            ChartLoadLimits.Unlimited ? long.MaxValue : 128L * 1024 * 1024,
            ChartLoadLimits.CountLimit(1_000_000));
        return EngineChartAdapter.Import(parsed);
    }
}
