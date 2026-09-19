using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    /// <summary>一套 UI 配色的全部角色色。三种主题共用同一组角色，切换主题只换值不换语义。</summary>
    readonly record struct UiPalette(
        Color Background, Color Panel, Color PanelAlt, Color PanelRaised,
        Color Border, Color BorderStrong, Color Text, Color Muted,
        Color Accent, Color AccentSoft, Color AccentStrong,
        Color TimelineA, Color TimelineB, Color Grid, Color GridMajor,
        Color DocsBackground, Color DocsRail, Color HelpBackground,
        Color ScrollTrack, Color ScrollThumb);

    /// <summary>按名称选择配色，大小写不敏感；未知或空名称回退到默认的 Nekomiya，绝不抛出。</summary>
    static UiPalette PaletteFor(string? name) => (name ?? "Nekomiya").ToLowerInvariant() switch
    {
        // 最初的、提高对比度之前的编辑器配色。
        "scarlet" => new(
            Color.Hex(0x070608), Color.Hex(0x100B0E), Color.Hex(0x160E12), Color.Hex(0x1D1117),
            Color.Hex(0x3C1A23), Color.Hex(0x6A2736), Color.Hex(0xF7EDEF), Color.Hex(0x927D85),
            Color.Hex(0xFF263F), Color.Hex(0xB8374B), Color.Hex(0xFF6072),
            Color.Hex(0x100B0E), Color.Hex(0x160E12), Color.Hex(0x351820), Color.Hex(0x5B2632),
            Color.Hex(0x0D090B), Color.Hex(0x130C10), Color.Hex(0x120D10),
            Color.Hex(0x2B151C), Color.Hex(0xA94A5E)),

        // 旧的红黑视觉身份，但整体提亮到在现代显示器上仍能保持可读。
        "kuroaki" => new(
            Color.Hex(0x09080B), Color.Hex(0x151014), Color.Hex(0x1C1318), Color.Hex(0x24171D),
            Color.Hex(0x713345), Color.Hex(0xA94961), Color.Hex(0xFFF6F8), Color.Hex(0xC7AFB7),
            Color.Hex(0xD91F40), Color.Hex(0xFF8CA0), Color.Hex(0xF04461),
            Color.Hex(0x151014), Color.Hex(0x21151B), Color.Hex(0x57303A), Color.Hex(0x824153),
            Color.Hex(0x111014), Color.Hex(0x1B1318), Color.Hex(0x171217),
            Color.Hex(0x351D25), Color.Hex(0xD06179)),

        // 当前 16.1 版高对比度的蓝 / 石墨配色。默认值。
        _ => new(
            Color.Hex(0x0E1118), Color.Hex(0x171E2B), Color.Hex(0x243044), Color.Hex(0x202A3A),
            Color.Hex(0x586B84), Color.Hex(0x70859E), Color.Hex(0xF4F7FC), Color.Hex(0xB9C6D8),
            Color.Hex(0xB82046), Color.Hex(0xFF9CAF), Color.Hex(0xD64164),
            Color.Hex(0x151E2B), Color.Hex(0x202A3A), Color.Hex(0x42546B), Color.Hex(0x70859E),
            Color.Hex(0x111319), Color.Hex(0x171A22), Color.Hex(0x15161C),
            Color.Hex(0x303642), Color.Hex(0x7A879C))
    };

    UiPalette Theme => PaletteFor(preferences.UiTheme);
    // 下面这些短别名是历史命名（red / white 等），指向的是主题角色而非固定颜色；切主题后含义随之变化。
    Color bg => Theme.Background;
    Color panel => Theme.Panel;
    Color line => Theme.Border;
    Color muted => Theme.Muted;
    Color white => Theme.Text;
    Color red => Theme.Accent;
    Color soft => Theme.AccentSoft;

    /// <summary>名称先经 ValidTheme 校验（未知回退 Nekomiya）再落盘；顺带作废帮助卡片的布局缓存，下一帧整卡重建。</summary>
    void SetUiTheme(string name)
    {
        preferences.UiTheme = ViewerSettings.ValidTheme(name);
        cachedHelpKey = "";
        SaveSettings();
    }
}
