// 绘制教程提示的遮盖层及结束闪烁。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Session? tutorialOwner;
    InputInstructionState? tutorialState;
    InputInstructionState Tutorial(Session session)
    {
        if (!ReferenceEquals(tutorialOwner, session))
        {
            tutorialOwner = session;
            tutorialState = new(session.Timeline, session.Duration);
        }
        return tutorialState!;
    }
    /// <summary>采样教程提示状态并绘制结束闪烁。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    public void DrawInputInstructionsEndBlip(Session session, double time)
    {
        if (!session.NativeGimmick.HasBehavior("instruction-panel")) return;
        if (Tutorial(session).BlipBirthAt(time) is { } birth) DrawNativeEndBlip(session, time, birth);
    }
    /// <summary>在教程遮盖层仍有效时绘制其精灵。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    public void DrawInputInstructionsCover(Session session, double time)
    {
        if (!session.NativeGimmick.HasBehavior("instruction-panel") || !session.Project.NativeTutorialCover || time >= Tutorial(session).CoverDestroyedAt) return;
        DrawSequenceSprite(session, "sp_tutorial_coverhideui", 0, 0, 0, 1, 1, 1, time);
    }
}
