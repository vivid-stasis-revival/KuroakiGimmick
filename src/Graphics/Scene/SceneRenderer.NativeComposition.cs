using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class SceneRenderer
{
    Target? nativeProxySource;
    void CaptureNativeProxySource(Texture input)
    {
        nativeProxySource??=new(canvas.Gpu,input.Width,input.Height);
        if(nativeProxySource.Texture.Width!=input.Width || nativeProxySource.Texture.Height!=input.Height)
        {canvas.Flush();nativeProxySource.Resize(input.Width,input.Height);}
        canvas.Pass(nativeProxySource,input,canvas.Basic);
    }
    /// <summary>
    /// Source regular-Draw overlays in front of all room FX still belong to the
    /// application surface. The inherited base Draw GUI then samples that whole
    /// surface; lyrics and other native GUI overlays are drawn only afterwards.
    /// </summary>
    void DrawNativeAfterEffects(Session session, double time, bool notes, Target output, bool applicationProxy, bool nativePost)
    {
        if (session.NativeGimmick.Data == null) return;
        canvas.Begin(output, output.Texture.Width, output.Texture.Height, 320, 180);
        nativeGimmick.DrawStage(session, time, GimmickStages.AfterEffects, notes);
        canvas.Flush();
        CaptureNativeApplication(session,output);
        if (!applicationProxy) return;
        canvas.Pass(field,session.NativeGimmick.Data.ApplicationProxyUsesUnfilteredSource && nativeProxySource!=null
            ?nativeProxySource.Texture:output.Texture,canvas.Basic);
        Target proxySurface=BeginNativeProxySurface(session,time);
        CompositeField(session,time,true,session.NativeGimmick.Data.ClearProxyFooter,proxySurface);
        ApplyNativeFeedback(session,time);
        Texture composed=proxySurface.Texture;
        if(nativePost)
        {
            nativeProxyPost??=new(canvas.Gpu,output.Texture.Width,output.Texture.Height);
            if(nativeProxyPost.Texture.Width!=output.Texture.Width || nativeProxyPost.Texture.Height!=output.Texture.Height)
            {canvas.Flush();nativeProxyPost.Resize(output.Texture.Width,output.Texture.Height);}
            nativeGimmick.RenderPost(nativeProxyPost,composed,session,time);
            composed=nativeProxyPost.Texture;
        }
        canvas.Pass(output,composed,canvas.Basic);
    }
}
