using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class Fonts
{
    /// <summary>
    /// 界面语言不再影响字体选择：中文、英文共用同一份 Noto Sans SC。这里保留的语言状态
    /// 只服务于排版差异（例如中文不套用英文的字距约定），字形来源始终是同一个字体文件。
    /// </summary>
    public bool ChineseInterface { get; private set; }

    public void SetInterfaceLanguage(string language) => ChineseInterface = UiLanguage.Resolve(language) == UiLanguage.Chinese;
}
