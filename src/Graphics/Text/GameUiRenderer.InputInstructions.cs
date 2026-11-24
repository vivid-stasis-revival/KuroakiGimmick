// 根据输入映射绘制教程按键提示。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class GameUiRenderer
{
    /// <summary>根据键盘或手柄映射绘制四轨输入提示。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="native">绘制原版对象精灵的渲染器。</param>
    public void DrawInputInstructionsInputs(Session session, double time, NativeGimmickRenderer native)
    {
        if (!session.NativeGimmick.HasBehavior("instruction-panel")) return;
        var inputs = session.Project.NativeInput ?? new NativeInputContext();
        if (inputs.Device == "keyboard")
        {
            Use(session.GameUi);
            if (current?.Data == null)
            {
                session.NativeGimmick.Fail("tutorial-font", "Original input-label font is unavailable.");
                return;
            }
            string font = Font(session);
            string[] labels = inputs.KeyboardLabels ?? NativeInputContext.DefaultKeys;
            for (int lane = 0; lane < 4; lane++) Text(font, labels[lane].ToUpperInvariant(), 126 + 23 * lane, 150, Color.White, Color.White);
        }
        else
        {
            string sprite = inputs.Device == "playstation" ? "sp_ds_inputs" : "sp_sd_inputs";
            int[] frames = inputs.GamepadIcons ?? [0, 1, 2, 3];
            for (int lane = 0; lane < 4; lane++)
                native.DrawSequenceSprite(session, sprite, frames[lane], 126 + 23 * lane, 150, 1, 1, 1, time);
        }
        canvas.Flush();
    }
}
