// 合成原版演出场景。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    /// <summary>按当前对象选择合成方式，将源纹理绘制到输出目标。</summary>
    /// <param name="output">接收绘制结果的目标。</param>
    /// <param name="input">作为输入画面的纹理。</param>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="textRenderer">绘制游戏文字和 HUD 的渲染器。</param>
    /// <returns>已处理当前场景合成时为 true。</returns>
    public bool RenderSceneComposite(Target output, Texture input, Session session, double time, GameUiRenderer? textRenderer = null)
    {
        Prepare(session);
        return current?.Data?.SceneComposite switch
        {
            "desktop" => RenderDesktopComposite(output, input, session, time, textRenderer),
            "filter-chain" => RenderFilterChain(output, input, session, time),
            _ => false
        };
    }
}
