namespace KuroakiGimmick.Core;

public sealed class NativeDesktopDefinition
{
    public string[] WindowTitles { get; set; }=[];
    public string Desktop { get; set; }="";
    public string WindowFrame { get; set; }="";
    public string? Cameo { get; set; }
    public string? Static { get; set; }
    public GimmickShaderMode? WindowShader { get; set; }
}
