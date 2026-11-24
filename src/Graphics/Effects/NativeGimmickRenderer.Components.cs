// 按合成阶段调度对象定义中的绘制组件。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>通用场景阶段，由定义组合绘制能力，无需按对象名分派。</summary>
public enum GimmickComponentStage
{
    /// <summary>生成遮罩前。</summary>
    BeforeMask,
    /// <summary>房间背景绘制后。</summary>
    AfterRoom,
    /// <summary>绘制曲绘前。</summary>
    BeforeJacket,
    /// <summary>后方粒子层。</summary>
    BackParticles,
    /// <summary>前方粒子层。</summary>
    FrontParticles,
    /// <summary>绘制曲绘后。</summary>
    AfterJacket,
    /// <summary>中间斩击层。</summary>
    NeutralSlashes,
    /// <summary>绘制游戏区域前。</summary>
    BeforePlayfield,
    /// <summary>后处理开始前。</summary>
    BeforePost,
    /// <summary>早期 GUI 阶段。</summary>
    EarlyGui,
    /// <summary>常规 GUI 阶段。</summary>
    Gui,
    /// <summary>结尾 GUI 阶段。</summary>
    EndingGui,
    /// <summary>最上方遮盖层。</summary>
    Cover
}

public sealed partial class NativeGimmickRenderer
{
    /// <summary>依次遍历已声明组件，在当前合成阶段分派绘制。</summary>
    /// <param name="stage">组件的绘制阶段。</param>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="target">接收组件绘制的目标。</param>
    /// <param name="ui">绘制游戏文字和 HUD 的渲染器。</param>
    public void DrawComponents(GimmickComponentStage stage, Session session, double time, Target target, GameUiRenderer ui)
    {
        if (session.NativeGimmick.Data is not { } definition) return;
        foreach (var component in definition.Behaviors)
        {
            switch ((component.Type, stage))
            {
                case ("masked-starfield", GimmickComponentStage.BeforeMask):
                    DrawMaskedStarFieldBehindMask(target, session, time);
                    DrawMaskedStarFieldLateDust(target, session, time);
                    break;
                case ("sprite-dialogue", GimmickComponentStage.AfterRoom):
                    DrawSpriteDialogueBackground(session, time);
                    break;
                case ("jacket-particles", GimmickComponentStage.BeforeJacket):
                    DrawLayeredParticlesSlashes(session, time, false);
                    break;
                case ("jacket-particles", GimmickComponentStage.BackParticles):
                    DrawLayeredParticlesParticles(session, time, false);
                    break;
                case ("jacket-particles", GimmickComponentStage.FrontParticles):
                    DrawLayeredParticlesParticles(session, time, true);
                    break;
                case ("side-starfield", GimmickComponentStage.AfterJacket):
                    DrawSideStarFieldBackground(session, time);
                    break;
                case ("jacket-particles", GimmickComponentStage.NeutralSlashes):
                    DrawLayeredParticlesSlashes(session, time, true);
                    break;
                case ("crt-console", GimmickComponentStage.BeforePlayfield):
                    DrawOperationConsoleScene(session, time);
                    ui.DrawOperationConsoleText(session, time);
                    break;
                case ("sprite-dialogue", GimmickComponentStage.BeforePost):
                    DrawSpriteDialogueForeground(session, time);
                    break;
                case ("price-dialogue", GimmickComponentStage.BeforePost):
                    DrawPriceDialogueDialogue(session, time, ui);
                    break;
                case ("jacket-particles", GimmickComponentStage.BeforePost):
                    DrawLayeredParticlesFinal(session, time);
                    break;
                case ("side-starfield", GimmickComponentStage.EarlyGui):
                    DrawSideStarFieldStars(session, time, true);
                    break;
                case ("instruction-panel", GimmickComponentStage.EarlyGui):
                    DrawInputInstructionsEndBlip(session, time);
                    break;
                case ("target-track", GimmickComponentStage.Gui):
                    ui.DrawTargetTrackLabels(session, time);
                    DrawTargetTrackEnding(session, time);
                    break;
                case ("instruction-panel", GimmickComponentStage.Gui):
                    ui.DrawInputInstructionsInputs(session, time, this);
                    break;
                case ("side-starfield", GimmickComponentStage.Gui):
                    DrawSideStarFieldStars(session, time, false);
                    DrawSideStarFieldEndBlip(session, time);
                    break;
                case ("price-dialogue", GimmickComponentStage.Gui):
                    DrawPriceDialogueGui(session, time, ui);
                    break;
                case ("jacket-particles", GimmickComponentStage.EndingGui):
                    DrawLayeredParticlesEndingGui(session, time);
                    break;
                case ("instruction-panel", GimmickComponentStage.Cover):
                    DrawInputInstructionsCover(session, time);
                    break;
            }
        }
    }
}
