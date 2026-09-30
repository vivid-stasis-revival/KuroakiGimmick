namespace KuroakiGimmick.Core;

/// <summary>
/// 导出形态：VSM、VSM + cgmk、独立 VSP + 图片资源包，或完整谱面文件夹。
/// VSP + Assets 只写图片声明及其引用资源，不偷偷夹带 VSM/字幕/音频。
/// </summary>
public enum ChartExportKind { Vsm, VsmAndConfig, VspAndAssets, ChartFolder }
