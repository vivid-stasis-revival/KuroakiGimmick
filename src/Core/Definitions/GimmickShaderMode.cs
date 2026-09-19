using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 一个 shader pass 的绑定。Uniforms 每项 1..4 个标量分量；Samplers 的值必须是已声明的 sprite 或 texture。
/// LinearSamplers 列出改用线性过滤的 sampler；采样方式计入纹理缓存键，不能和最近邻共用同一份纹理。
/// </summary>
public sealed class GimmickShaderMode
{
    public string Shader { get; set; } = "";
    public Dictionary<string, GimmickScalar[]> Uniforms { get; set; } = [];
    public Dictionary<string, string> Samplers { get; set; } = [];
    public string[] LinearSamplers { get; set; } = [];
}

