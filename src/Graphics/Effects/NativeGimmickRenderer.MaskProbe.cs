// 为诊断暴露实际的 320×180 GameMaker 遮罩目标，复用已有 GPU 资源。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    /// <summary>向诊断读取方提供当前遮罩目标的纹理。</summary>
    public Texture StarcrashersMaskProbeTexture => mask.Texture;
}
