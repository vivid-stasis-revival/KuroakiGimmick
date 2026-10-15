using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>Common scene hooks. Definitions compose capabilities without adding object-name branches to the host.</summary>
public enum GimmickComponentStage
{
    BeforeMask, AfterRoom, BeforeJacket, BackParticles, FrontParticles, AfterJacket, NeutralSlashes,
    BeforePlayfield, BeforePost, EarlyGui, Gui, EndingGui, Cover
}

public sealed partial class NativeGimmickRenderer
{
    public void DrawComponents(GimmickComponentStage stage,Session session,double time,Target target,GameUiRenderer ui)
    {
        if(session.NativeGimmick.Data is not { } definition)return;
        foreach(var component in definition.Behaviors)
        {
            switch((component.Type,stage))
            {
                case ("masked-starfield",GimmickComponentStage.BeforeMask):
                    DrawMaskedStarFieldBehindMask(target,session,time);DrawMaskedStarFieldLateDust(target,session,time);break;
                case ("sprite-dialogue",GimmickComponentStage.AfterRoom):DrawSpriteDialogueBackground(session,time);break;
                case ("jacket-particles",GimmickComponentStage.BeforeJacket):DrawLayeredParticlesSlashes(session,time,false);break;
                case ("jacket-particles",GimmickComponentStage.BackParticles):DrawLayeredParticlesParticles(session,time,false);break;
                case ("jacket-particles",GimmickComponentStage.FrontParticles):DrawLayeredParticlesParticles(session,time,true);break;
                case ("side-starfield",GimmickComponentStage.AfterJacket):DrawSideStarFieldBackground(session,time);break;
                case ("jacket-particles",GimmickComponentStage.NeutralSlashes):DrawLayeredParticlesSlashes(session,time,true);break;
                case ("crt-console",GimmickComponentStage.BeforePlayfield):DrawOperationConsoleScene(session,time);ui.DrawOperationConsoleText(session,time);break;
                case ("sprite-dialogue",GimmickComponentStage.BeforePost):DrawSpriteDialogueForeground(session,time);break;
                case ("price-dialogue",GimmickComponentStage.BeforePost):DrawPriceDialogueDialogue(session,time,ui);break;
                case ("jacket-particles",GimmickComponentStage.BeforePost):DrawLayeredParticlesFinal(session,time);break;
                case ("side-starfield",GimmickComponentStage.EarlyGui):DrawSideStarFieldStars(session,time,true);break;
                case ("instruction-panel",GimmickComponentStage.EarlyGui):DrawInputInstructionsEndBlip(session,time);break;
                case ("target-track",GimmickComponentStage.Gui):ui.DrawTargetTrackLabels(session,time);DrawTargetTrackEnding(session,time);break;
                case ("instruction-panel",GimmickComponentStage.Gui):ui.DrawInputInstructionsInputs(session,time,this);break;
                case ("side-starfield",GimmickComponentStage.Gui):DrawSideStarFieldStars(session,time,false);DrawSideStarFieldEndBlip(session,time);break;
                case ("price-dialogue",GimmickComponentStage.Gui):DrawPriceDialogueGui(session,time,ui);break;
                case ("jacket-particles",GimmickComponentStage.EndingGui):DrawLayeredParticlesEndingGui(session,time);break;
                case ("instruction-panel",GimmickComponentStage.Cover):DrawInputInstructionsCover(session,time);break;
            }
        }
    }
}
