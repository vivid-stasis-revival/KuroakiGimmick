using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class SceneRenderer
{
    Target? nativeCompositeInput;
    Target? nativeApplicationSnapshot;
    void CaptureNativeApplication(Session session,Target input)
    {
        if(session.NativeGimmick.Data?.CompositeSource!="application")return;
        nativeApplicationSnapshot??=new(canvas.Gpu,input.Texture.Width,input.Texture.Height);
        if(nativeApplicationSnapshot.Texture.Width!=input.Texture.Width || nativeApplicationSnapshot.Texture.Height!=input.Texture.Height)
        {canvas.Flush();nativeApplicationSnapshot.Resize(input.Texture.Width,input.Texture.Height);}
        canvas.Pass(nativeApplicationSnapshot,input.Texture,canvas.Basic);
    }
    void RenderNativeComposite(Session session,double time,Target output,bool afterGui)
    {
        if(session.NativeGimmick.Data is not {SceneComposite:not null} definition || definition.CompositeAfterGui!=afterGui)return;
        // Times' inherited GUI event clears its earlier desktop when native proxies exist.
        if(definition.SceneComposite=="desktop" && UsesNativeApplication(session))return;
        canvas.Flush();
        nativeCompositeInput??=new(canvas.Gpu,output.Texture.Width,output.Texture.Height);
        if(nativeCompositeInput.Texture.Width!=output.Texture.Width||nativeCompositeInput.Texture.Height!=output.Texture.Height)
            nativeCompositeInput.Resize(output.Texture.Width,output.Texture.Height);
        canvas.Pass(nativeCompositeInput,definition.CompositeSource=="application" && nativeApplicationSnapshot!=null
            ?nativeApplicationSnapshot.Texture:output.Texture,canvas.Basic);
        nativeGimmick.RenderSceneComposite(output,nativeCompositeInput.Texture,session,time,gameUi);
    }
}
