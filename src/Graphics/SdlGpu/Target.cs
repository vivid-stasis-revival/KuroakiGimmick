using StbImageSharp;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

/// <summary>可调整大小的离屏目标。先成功分配新纹理，才释放旧目标，避免分配失败丢失原目标。</summary>
public sealed class Target : IDisposable
{
    readonly GpuDevice gpu;
    readonly bool linear;
    public Texture Texture { get; private set; }
    public Target(GpuDevice gpu, int width, int height, bool linear = false)
    {
        this.gpu = gpu;
        this.linear = linear;
        Texture = new(gpu, width, height, linear: linear);
    }
    /// <summary>尺寸相同时不重建纹理；新纹理分配成功后才释放旧纹理，linear 设置在多次 Resize 之间保持。</summary>
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
    public void Dispose() => Texture.Dispose();
}
