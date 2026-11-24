using StbImageSharp;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

/// <summary>可调整大小的离屏目标。先成功分配新纹理，才释放旧目标，避免分配失败丢失原目标。</summary>
public sealed class Target : IDisposable
{
    readonly GpuDevice gpu;
    readonly bool linear;
    /// <summary>当前离屏目标所持有的颜色纹理。</summary>
    public Texture Texture { get; private set; }
    /// <summary>为给定尺寸及过滤方式创建离屏纹理。</summary>
    /// <param name="gpu">持有渲染资源的 GPU 设备。</param>
    /// <param name="width">目标宽度。</param>
    /// <param name="height">目标高度。</param>
    /// <param name="linear">是否使用线性过滤。</param>
    public Target(GpuDevice gpu, int width, int height, bool linear = false)
    {
        this.gpu = gpu;
        this.linear = linear;
        Texture = new(gpu, width, height, linear: linear);
    }
    /// <summary>尺寸相同时不重建纹理；新纹理分配成功后才释放旧纹理，linear 设置在多次 Resize 之间保持。</summary>
    /// <param name="width">目标宽度。</param>
    /// <param name="height">目标高度。</param>
    public void Resize(int width, int height)
    {
        if (Texture.Width == width && Texture.Height == height)
        {
            return;
        }
        var next = new Texture(gpu, width, height, linear: linear);
        Texture.Dispose();
        Texture = next;
    }
    /// <inheritdoc/>
    public void Dispose() => Texture.Dispose();
}
