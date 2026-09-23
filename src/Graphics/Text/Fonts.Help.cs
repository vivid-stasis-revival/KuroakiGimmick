namespace KuroakiGimmick.Graphics;

public sealed partial class Fonts
{
    /// <summary>
    /// 文档与帮助排版的字体信息。字体内嵌在程序集里，不可能缺失，因此这里不再有
    /// "字体资源不可用、退回旧图集"的分支——那是外部 Assets 目录时代的故障模式。
    /// </summary>
    public bool HasReadableHelpFont => true;

    /// <summary>帮助与手册的排版缓存以此为键的一部分；换字体会让缓存自然失效。</summary>
    public string HelpFontName => "Noto Sans SC";
}
