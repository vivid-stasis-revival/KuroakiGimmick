using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 资源候选文件的位置。Scope 只能是 pack / definition / shared，决定 Path 的根目录；
/// Path 是相对路径，必须留在该根目录内，符号链接也不能指向外面。
/// </summary>
public sealed class GimmickResourceLocation
{
    /// <summary>解析相对路径所用的根，可取 pack、definition 或 shared。</summary>
    public string Scope { get; set; } = "pack";
    /// <summary>相对于所选根目录的资源路径。</summary>
    public string Path { get; set; } = "";
}

