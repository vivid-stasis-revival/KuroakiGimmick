namespace KuroakiGimmick.Core;

/// <summary>模拟桌面场景所需的背景、窗口外观及可选滤镜。</summary>
public sealed class NativeDesktopDefinition
{
    /// <summary>虚拟窗口使用的标题文字表。</summary>
    public string[] WindowTitles { get; set; } = [];
    /// <summary>桌面背景精灵键。</summary>
    public string Desktop { get; set; } = "";
    /// <summary>虚拟窗口边框精灵键。</summary>
    public string WindowFrame { get; set; } = "";
    /// <summary>可选的客串画面精灵键。</summary>
    public string? Cameo { get; set; }
    /// <summary>可选的静态噪声精灵键。</summary>
    public string? Static { get; set; }
    /// <summary>用于虚拟窗口内容的可选 shader pass。</summary>
    public GimmickShaderMode? WindowShader { get; set; }
}
