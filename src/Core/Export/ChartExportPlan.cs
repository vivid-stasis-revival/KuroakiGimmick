namespace KuroakiGimmick.Core;

/// <summary>写出任何东西之前先完成规划与检查。源符号链接一律拒绝，而不是悄悄跟随。</summary>
public sealed class ChartExportPlan
{
    /// <summary>一个输出项；<c>Data</c> 与 <c>Source</c> 二选一：前者是内存生成的字节，后者是待复制的源文件。Sha256 在提交前用于复验。</summary>
    /// <param name="Name">输出文件名。</param>
    /// <param name="Source">可选的源文件路径。</param>
    /// <param name="Data">可选的内存文件内容。</param>
    /// <param name="Length">预期字节数。</param>
    /// <param name="Sha256">预期 SHA-256 摘要。</param>
    public sealed record Item(string Name, string? Source, byte[]? Data, long Length, string Sha256);
    /// <summary>本次导出的文件组合形式。</summary>
    public ChartExportKind Kind { get; }
    /// <summary>本计划的目标位置。</summary>
    public string Destination { get; }
    /// <summary>是否允许替换已有输出。</summary>
    public bool Overwrite { get; }
    /// <summary>审核或执行导出时使用的文件清单。</summary>
    public IReadOnlyList<Item> Files { get; }
    /// <summary>建立计划时收集的提示。</summary>
    public IReadOnlyList<string> Warnings { get; }
    /// <summary>计划内全部输出项的总字节数，用于写盘前估算空间与显示进度。</summary>
    public long TotalBytes => Files.Sum(f => f.Length);
    /// <summary>入参立即定型成数组，计划一旦生成就不再变；提交阶段只照着它执行，不会再回头重新规划。</summary>
    /// <param name="kind">导出形态。</param>
    /// <param name="destination">目标位置。</param>
    /// <param name="files">待发布文件集合。</param>
    /// <param name="warnings">生成计划时收集的警告。</param>
    /// <param name="overwrite">是否允许替换已有输出文件。</param>
    public ChartExportPlan(ChartExportKind kind, string destination, IEnumerable<Item> files, IEnumerable<string> warnings, bool overwrite = false)
    {
        Kind = kind;
        Destination = destination;
        Files = files.ToArray();
        Warnings = warnings.ToArray();
        Overwrite = overwrite;
    }
}
