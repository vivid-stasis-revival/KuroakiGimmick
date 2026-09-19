using KuroakiGimmick.Core;
namespace KuroakiGimmick.Graphics;

public sealed partial class SceneRenderer
{
    /// <summary>与本地图片编辑画布共用同一份有预算上限的纹理缓存；编辑器不另建一条加载路径。</summary>
    public Texture? AuthoringImageTexture(Session session, CustomImages.Asset asset) => ImageTexture(session, asset);
    /// <summary>按编辑器给定的投影与取值回调复用场景文字渲染器，使画布预览与实际演出共用同一套排版规则。</summary>
    public void DrawAuthoringTexts(Session session, double time, System.Numerics.Matrix3x2 projection,
        Func<string, double> values, string? selectedId, Action<string, System.Numerics.Vector2[]> bounds) =>
        sceneFont.Draw(canvas, session, time, projection, values, selectedId, bounds);
}
