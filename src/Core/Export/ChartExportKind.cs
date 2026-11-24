namespace KuroakiGimmick.Core;

/// <summary>
/// 导出形态：VSM、VSM + cgmk、独立 VSP + 图片资源包，或完整谱面文件夹。
/// VSP + Assets 只写图片声明及其引用资源，不偷偷夹带 VSM/字幕/音频。
/// </summary>
public enum ChartExportKind
{
    /// <summary>仅导出演出 VSM。</summary>
    Vsm,
    /// <summary>导出 VSM 及 CGMK 配置。</summary>
    VsmAndConfig,
    /// <summary>导出 VSP 与其引用图片。</summary>
    VspAndAssets,
    /// <summary>导出完整谱面文件夹。</summary>
    ChartFolder
}
