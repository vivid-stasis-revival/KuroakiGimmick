using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 资源候选文件的位置。Scope 只能是 pack / definition / shared，决定 Path 的根目录；
/// Path 是相对路径，必须留在该根目录内，符号链接也不能指向外面。
/// </summary>
public sealed class GimmickResourceLocation
{
    public string Scope { get; set; } = "pack";
    public string Path { get; set; } = "";
}

