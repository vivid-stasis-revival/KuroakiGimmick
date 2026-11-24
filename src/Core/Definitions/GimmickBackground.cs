using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>对象背景层：先在 320×180 的离屏目标上画 Drawing，再可选地过一遍 Effect；
/// Effect 为 null 或 shader 不可用时直接输出原图，不留下上一帧的离屏残影。</summary>
public sealed class GimmickBackground
{
    /// <summary>先绘制到背景目标的内容。</summary>
    public GimmickDrawing Drawing { get; set; } = new();
    /// <summary>可选的背景后处理 pass。</summary>
    public GimmickShaderMode? Effect { get; set; }
}

