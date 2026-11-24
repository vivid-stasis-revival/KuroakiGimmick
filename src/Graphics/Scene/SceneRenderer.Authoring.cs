// 提供编辑器的图片纹理与字幕测量、绘制入口。

using KuroakiGimmick.Core;
namespace KuroakiGimmick.Graphics;

public sealed partial class SceneRenderer
{
    /// <summary>与本地图片编辑画布共用同一份有预算上限的纹理缓存；编辑器不另建一条加载路径。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="asset">需要预览的图片资源。</param>
    /// <returns>图像资源对应的纹理，不可用时为 null。</returns>
    public Texture? AuthoringImageTexture(Session session, CustomImages.Asset asset) => ImageTexture(session, asset);
    /// <summary>按编辑器给定的投影与取值回调复用场景文字渲染器，使画布预览与实际演出共用同一套排版规则。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="projection">可选的画布变换矩阵。</param>
    /// <param name="values">临时文本属性覆盖。</param>
    /// <param name="selectedId">当前选中的文本 ID。</param>
    /// <param name="bounds">接收文本可交互边界的集合。</param>
    public void DrawAuthoringTexts(Session session, double time, System.Numerics.Matrix3x2 projection,
        Func<string, double> values, string? selectedId, Action<string, System.Numerics.Vector2[]> bounds) =>
        sceneFont.Draw(canvas, session, time, projection, values, selectedId, bounds);

    /// <summary>只走与场景相同的文字布局并返回包围框，不向当前渲染目标重复画一份文字。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="projection">可选的画布变换矩阵。</param>
    /// <param name="values">临时文本属性覆盖。</param>
    /// <param name="bounds">接收文本边界的集合。</param>
    public void MeasureAuthoringTexts(Session session, double time, System.Numerics.Matrix3x2 projection,
        Func<string, double> values, Action<string, System.Numerics.Vector2[]> bounds) =>
        sceneFont.Draw(canvas, session, time, projection, values, null, bounds, render: false);
}
