using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>把包内的纹理参数解析为文件；Aliases 允许原版资源名映射到本地或共享素材。</summary>
public sealed class GimmickTextureSource
{
    /// <summary>提供纹理资源名的可选包参数。</summary>
    public string? Parameter { get; set; }
    /// <summary>解析纹理时尝试的资源位置列表。</summary>
    public List<GimmickResourceLocation> Candidates { get; set; } = [];
    /// <summary>将资源名映射到对应候选路径列表。</summary>
    public Dictionary<string, List<GimmickResourceLocation>> Aliases { get; set; } = [];
}

