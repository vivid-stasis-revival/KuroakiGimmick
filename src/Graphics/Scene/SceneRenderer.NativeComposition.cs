// 捕获 proxy 源画面并在效果阶段后绘制对象。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class SceneRenderer
{
    Target? nativeProxySource;
    void CaptureNativeProxySource(Texture input)
    {
        nativeProxySource ??= new(canvas.Gpu, input.Width, input.Height);
        if (nativeProxySource.Texture.Width != input.Width || nativeProxySource.Texture.Height != input.Height)
        {
            canvas.Flush();
            nativeProxySource.Resize(input.Width, input.Height);
        }
        canvas.Pass(nativeProxySource, input, canvas.Basic);
    }
    /// <summary>位于房间 FX 前方的普通 Draw 覆盖层仍在 application surface 内。基础 Draw GUI 采样整张 surface，歌词等原版 GUI 覆盖层随后绘制。</summary>
    void DrawNativeAfterEffects(Session session, double time, bool notes, Target output, bool applicationProxy, bool nativePost)
    {
        if (session.NativeGimmick.Data == null) return;
        canvas.Begin(output, output.Texture.Width, output.Texture.Height, 320, 180);
        nativeGimmick.DrawStage(session, time, GimmickStages.AfterEffects, notes);
        canvas.Flush();
        CaptureNativeApplication(session, output);
        if (!applicationProxy) return;
        canvas.Pass(field, session.NativeGimmick.Data.ApplicationProxyUsesUnfilteredSource && nativeProxySource != null
            ? nativeProxySource.Texture : output.Texture, canvas.Basic);
        Target proxySurface = BeginNativeProxySurface(session, time);
        CompositeField(session, time, true, session.NativeGimmick.Data.ClearProxyFooter, proxySurface);
        ApplyNativeFeedback(session, time);
        Texture composed = proxySurface.Texture;
        if (nativePost)
        {
            nativeProxyPost ??= new(canvas.Gpu, output.Texture.Width, output.Texture.Height);
            if (nativeProxyPost.Texture.Width != output.Texture.Width || nativeProxyPost.Texture.Height != output.Texture.Height)
            {
                canvas.Flush();
                nativeProxyPost.Resize(output.Texture.Width, output.Texture.Height);
            }
            nativeGimmick.RenderPost(nativeProxyPost, composed, session, time);
            composed = nativeProxyPost.Texture;
        }
        canvas.Pass(output, composed, canvas.Basic);
    }
}
