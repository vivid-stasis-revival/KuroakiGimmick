namespace KuroakiGimmick.Core;

/// <summary>导出形态：只写 VSM、VSM 加 cgmk 配置，或复制整个谱面文件夹。前两者不包含图片、字幕和音频。</summary>
public enum ChartExportKind { Vsm, VsmAndConfig, ChartFolder }
