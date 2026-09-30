using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 场景合成协调器。对象背景、资源图层、轨道、固定 HUD 与后处理按显式边界组织；预览和视频导出共用此入口。
/// </summary>
public sealed partial class SceneRenderer
{
    /// <summary>会话换了图片集合才整批丢弃缓存；释放 GPU 纹理前必须先 Flush，否则仍在队列里的批次会引用已销毁的纹理。</summary>
    void PrepareImages(Session session)
    {
        if (ReferenceEquals(loadedImages, session.Images))
        {
            return;
        }
        canvas.Flush();
        foreach (var texture in customTextures.Values)
        {
            texture.Dispose();
        }
        customTextures.Clear();
        loadedImages = session.Images;
        textureLru.Clear();
        failedTextures.Clear();
        textureBytes = 0;
        jacketTexture?.Dispose();
        jacketTexture = null;
        jacketPath = null;
    }

    /// <summary>按时间取当前 jacket；解码失败只记一次诊断并进入 failedTextures，不每帧重试。切换纹理前先 Flush。</summary>
    void DrawJacket(Session session, double time)
    {
        var jackets = session.Jackets;
        string? path = jackets.At(session.Timeline, time);
        if (path != jacketPath)
        {
            canvas.Flush();
            jacketTexture?.Dispose();
            jacketTexture = null;
            jacketPath = path;
            if (path != null && !failedTextures.Contains(path))
            {
                try
                {
                    jacketTexture = Texture.Load(canvas.Gpu, path, linear: !jackets.CustomLayout);
                }
                catch (Exception ex)
                {
                    failedTextures.Add(path);
                    session.Chart.Diagnostics.Add(new(path, 0, "Jacket decode failed: " + ex.Message, true));
                }
            }
        }
        if (jacketTexture == null)
        {
            return;
        }
        // 原版的 bm_dest_color / bm_zero。用封面在每个粒子像素**当前**屏幕位置上的颜色去染色，
        // 移动中的爆发粒子也一样。画完必须恢复常规 source-alpha 混合。
        canvas.Blend(BlendFactor.DestinationColor, BlendFactor.Zero);
        var tint = session.DistortBgEnabled? Color.Hex((uint) Math.Clamp(session.Timeline.Get("ditortedBG_col_rgb", time), 0,
            16777215)) : Color.White;
        int frames = path == jackets.DefaultPath && jackets.Animated ? Math.Max(1, jackets.AnimatedFrames) : 1;
        int frame = frames <= 1 ? 0 : (int)Math.Floor(Math.Max(0, time) * 60) % frames;
        canvas.Quad(jacketTexture, jackets.CustomLayout? new(0, 0, 320, 180) : new(-50, -50, 420, 268), tint,
            new(frame / (float)frames, 0, 1f / frames, 1));
        canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
    }

    /// <summary>
    /// 按路径取图片纹理：命中即刷新 LRU 次序；未命中时先按 128 MiB 预算淘汰最久未用的纹理再加载。
    /// 解码失败的路径记入 failedTextures 并写一条诊断，之后直接返回 null，不每帧重试。分配与释放前都要 Flush。
    /// </summary>
    Texture? ImageTexture(Session session, CustomImages.Asset asset)
    {
        string path = asset.Path;
        if (customTextures.TryGetValue(path, out var cached))
        {
            textureLru.Remove(path);
            textureLru.AddLast(path);
            return cached;
        }
        if (failedTextures.Contains(path))
        {
            return null;
        }
        canvas.Flush();
        long bytes = (long) asset.Width * asset.Height * 4;
        while (textureBytes + bytes > 128 * 1024 * 1024 && textureLru.First != null)
        {
            string oldest = textureLru.First.Value;
            var old = customTextures[oldest];
            textureBytes -= (long) old.Width * old.Height * 4;
            old.Dispose();
            customTextures.Remove(oldest);
            textureLru.RemoveFirst();
        }
        try
        {
            var tex = Texture.Load(canvas.Gpu, path);
            customTextures[path] = tex;
            textureBytes += (long) tex.Width * tex.Height * 4;
            textureLru.AddLast(path);
            return tex;
        }
        catch (Exception ex)
        {
            failedTextures.Add(path);
            session.Chart.Diagnostics.Add(new(path, 0, "Image decode failed: " + ex.Message, true));
            return null;
        }
    }

    /// <summary>
    /// 绘制 [minPriority, maxPriority) 半开区间内的图片，因此相邻两次调用的区间必须首尾相接。
    /// 每次调用都会重新绑定目标，优先级窗口同时也是目标边界；不要把多个窗口合并成一次调用。
    /// </summary>
    void DrawImages(Session session, double time, double minPriority, double maxPriority, Target? target = null)
    {
        canvas.Begin(target ?? scene, RenderWidth, RenderHeight, 320, 180);
        foreach (var (item, pose) in session.Images.At(session.Timeline, time))
        {
            if (item.LayerPriority < minPriority || item.LayerPriority >= maxPriority)
            {
                continue;
            }
            var texture = ImageTexture(session, item.Asset);
            if (texture == null)
            {
                continue;
            }
            var m = pose.Matrix;
            canvas.Polygon(texture, Vector2.Transform(new(-.5f, -.5f), m), Vector2.Transform(new(.5f, -.5f), m), Vector2.Transform(new(-.5f,
                .5f), m), Vector2.Transform(new(.5f, .5f), m), Color.Hex(pose.Rgb).Alpha(pose.Alpha), new(pose.Frame / (float) item.Frames, 0,
                1f / item.Frames, 1));
        }
    }
}

