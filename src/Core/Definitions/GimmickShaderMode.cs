using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 一个 shader pass 的绑定。Uniforms 每项 1..4 个标量分量；Samplers 的值必须是已声明的 sprite 或 texture。
/// LinearSamplers 列出改用线性过滤的 sampler；采样方式计入纹理缓存键，不能和最近邻共用同一份纹理。
/// </summary>
public sealed class GimmickShaderMode
{
    /// <summary>该 pass 使用的 shader 键。</summary>
    public string Shader { get; set; } = "";
    /// <summary>按 uniform 名称提供一至四个标量表达式。</summary>
    public Dictionary<string, GimmickScalar[]> Uniforms { get; set; } = [];
    /// <summary>从 sampler 名称到已声明精灵或纹理的映射。</summary>
    public Dictionary<string, string> Samplers { get; set; } = [];
    /// <summary>使用线性过滤的 sampler 名称。</summary>
    public string[] LinearSamplers { get; set; } = [];
    /// <summary>使用边缘钳制寻址的 sampler 名称。</summary>
    public string[] ClampSamplers { get; set; } = [];
}

