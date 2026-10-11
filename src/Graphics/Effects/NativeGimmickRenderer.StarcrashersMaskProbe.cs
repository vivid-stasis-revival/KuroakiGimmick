using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// Diagnostics only: expose the actual 320x180 GameMaker mask render target.
/// No GPU resource is allocated by this partial class.
/// </summary>
public sealed partial class NativeGimmickRenderer
{
    public Texture StarcrashersMaskProbeTexture => mask.Texture;
}
