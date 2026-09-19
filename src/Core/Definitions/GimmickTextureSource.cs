using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>把包内的纹理参数解析为文件；Aliases 允许原版资源名映射到本地或共享素材。</summary>
public sealed class GimmickTextureSource
{
    public string? Parameter { get; set; }
    public List<GimmickResourceLocation> Candidates { get; set; } = [];
    public Dictionary<string, List<GimmickResourceLocation>> Aliases { get; set; } = [];
}

