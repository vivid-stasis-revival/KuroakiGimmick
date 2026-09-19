namespace KuroakiGimmick.Core;

/// <summary>写出任何东西之前先完成规划与检查。源符号链接一律拒绝，而不是悄悄跟随。</summary>
public sealed class ChartExportPlan
{
    /// <summary>一个输出项；<c>Data</c> 与 <c>Source</c> 二选一：前者是内存生成的字节，后者是待复制的源文件。Sha256 在提交前用于复验。</summary>
    public sealed record Item(string Name, string? Source, byte[]? Data, long Length, string Sha256);
    public ChartExportKind Kind { get; }
    public string Destination { get; }
    public IReadOnlyList<Item> Files { get; }
    public IReadOnlyList<string> Warnings { get; }
    /// <summary>计划内全部输出项的总字节数，用于写盘前估算空间与显示进度。</summary>
    public long TotalBytes => Files.Sum(f => f.Length);
    /// <summary>入参立即定型成数组，计划一旦生成就不再变；提交阶段只照着它执行，不会再回头重新规划。</summary>
    public ChartExportPlan(ChartExportKind kind, string destination, IEnumerable<Item> files, IEnumerable<string> warnings)
    { Kind = kind; Destination = destination; Files = files.ToArray(); Warnings = warnings.ToArray(); }
}
