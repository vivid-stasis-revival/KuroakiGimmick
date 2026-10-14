using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    public bool RenderSceneComposite(Target output,Texture input,Session session,double time,GameUiRenderer? textRenderer=null)
    {
        Prepare(session);
        return current?.Data?.SceneComposite switch
        {
            "desktop"=>RenderDesktopComposite(output,input,session,time,textRenderer),
            "filter-chain"=>RenderFilterChain(output,input,session,time),
            _=>false
        };
    }
}
