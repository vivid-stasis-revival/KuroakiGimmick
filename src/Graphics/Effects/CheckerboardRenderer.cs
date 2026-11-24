// 按时间轴更新棋盘图案并复用所需的 GPU 纹理。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 将刷新时间表绘制为持久纹理。后退拖动会从初始状态重放刷新，保留透明像素的历史合成效果。
/// </summary>
public sealed class CheckerboardRenderer : IDisposable
{
    readonly Canvas canvas;
    /// <summary>跨 generation 累积的持久棋盘面。瓦片带透明像素，所以只做增量叠加，不能每帧清空重画。</summary>
    readonly Target pattern;
    /// <summary>本类自带的一份反预乘 shader；pattern 是透明目标，贴回场景前必须还原成直通 alpha。</summary>
    readonly Shader composite;
    Checkerboard? current;
    Texture[] tiles = [];
    int generation = -1;
    /// <summary>使用指定画布管理棋盘纹理及其绘制。</summary>
    /// <param name="c">提交绘制命令的画布。</param>
    public CheckerboardRenderer(Canvas c)
    {
        canvas = c;
        pattern = new(c.Gpu, 320, 180);
        composite = new(c.Gpu, Canvas.VertexSource,
            SceneRenderer.CompositeFragmentSource);
    }

    /// <summary>
    /// 只在 checker mode 与 alpha 都匹配时绘制。generation 前进时增量补画缺的几代，
    /// 后退时必须清空并从第 0 代重放 —— 透明的瓦片像素会露出历史内容，跳着画结果就不对。
    /// </summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="mode">棋盘显示模式。</param>
    /// <param name="target">接收绘制的目标。</param>
    /// <param name="width">目标宽度。</param>
    /// <param name="height">目标高度。</param>
    public void Draw(Session session, double time, int mode, Target target, int width, int height)
    {
        var source = session.Checker;
        if (!source.Ready || (source.FixedMode ?? session.Timeline.Get("angelstar_checker_mode", time)) != mode)
        {
            return;
        }
        double alpha = session.Timeline.Get("angelstar_checker_alpha", time);
        if (alpha <= 0)
        {
            return;
        }
        int wanted = source.Generation(time);
        if (!ReferenceEquals(current, source))
        {
            canvas.Flush();
            foreach (var t in tiles)
            {
                t.Dispose();
            }
            tiles = [];
            generation = -1;
            current = source;
            tiles = source.Frames.Select(p => Texture.Load(canvas.Gpu, p)).ToArray();
        }
        if (wanted != generation)
        {
            // 原版在两次刷新之间保留这块 surface，因此透明的瓦片像素会露出下面的旧内容；
            // 首次绘制和向后跳转都要清空并从第 0 代重放，向前则只补画新增的几代。
            if (generation < 0 || wanted < generation)
            {
                canvas.Begin(pattern, 320, 180, 320, 180, new(0, 0, 0, 0));
                generation = -1;
            }
            else
            {
                canvas.Begin(pattern, 320, 180, 320, 180);
            }
            for (int step = generation + 1; step <= wanted; step++)
            {
                for (int x = 0; x < 32; x++)
                {
                    for (int y = 0; y < 18; y++)
                    {
                        canvas.Quad(tiles[Checkerboard.CellFrame(step, x * 18 + y)], new(x * 10, y * 10, 10, 10), Color.White);
                    }
                }
            }
            canvas.Flush();
            generation = wanted;
        }
        canvas.Begin(target, width, height, 320, 180);
        // 来源对象中只有原版 Angelstar 提供 curcolor。
        var tint = session.NativeGimmick.HasBehavior("checker-tint")
            ? Color.Hsv((float)session.Timeline.Get("curcolor", time) / 255, 1) : Color.Hex(0xFF0000);
        canvas.Quad(pattern.Texture, new(0, 0, 320, 180), tint.Alpha(alpha), shader: composite);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var t in tiles)
        {
            t.Dispose();
        }
        pattern.Dispose();
        composite.Dispose();
    }
}

