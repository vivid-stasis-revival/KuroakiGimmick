using StbImageSharp;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

/// <summary>拥有一对 GPU texture/sampler。上传完成后不保留 CPU 解码数组；重复/线性采样属于资源状态。</summary>
public sealed class Texture : IDisposable
{
    public nint Id { get; private set; }
    internal nint Sampler { get; private set; }
    public int Width { get; }
    public int Height { get; }
    public int MipLevels { get; }
    readonly GpuDevice gpu;
    /// <summary>pixels 为 null 时创建可当渲染目标的纹理，否则创建只读采样纹理；mipmaps 只对带像素的纹理生效。</summary>
    public Texture(GpuDevice gpu, int width, int height, byte[]? pixels = null, bool repeat = false, bool linear = false, bool mipmaps = false)
    {
        this.gpu = gpu;
        Width = width;
        Height = height;
        // 渲染目标用不到 mip；只有静态采样图片可以按需开启。
        MipLevels = pixels != null && mipmaps ? CalculateMipLevels(width, height) : 1;
        if (width <= 0 || height <= 0 || pixels != null && pixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException("Invalid RGBA texture dimensions/data.");
        }
        Id = GpuDevice.Check(SDL_CreateGPUTexture(gpu.Handle, new GPUTextureCreateInfo
        {
            format = GpuDevice.Rgba8,
            // usage 3 = SAMPLER|COLOR_TARGET，1 = 仅 SAMPLER：只有不带像素的纹理才允许被渲染。
            usage = pixels == null ? 3u : 1u,
            width = (uint)width,
            height = (uint)height,
            layer_count_or_depth = 1,
            num_levels = (uint)MipLevels
        }), "Create GPU texture");
        try
        {
            // SDL_GPU 原生枚举：filter 0=NEAREST、1=LINEAR；address mode 0=REPEAT、2=CLAMP_TO_EDGE。
            Sampler = GpuDevice.Check(SDL_CreateGPUSampler(gpu.Handle, new GPUSamplerCreateInfo
            {
                min_filter = linear ? 1u : 0u,
                mag_filter = linear ? 1u : 0u,
                mipmap_mode = MipLevels > 1 ? 1u : 0u,
                address_mode_u = repeat ? 0u : 2u,
                address_mode_v = repeat ? 0u : 2u,
                address_mode_w = repeat ? 0u : 2u,
                min_lod = 0,
                max_lod = MipLevels - 1
            }), "Create GPU sampler");
            if (pixels != null)
            {
                // 高分辨率位图字体在 100% DPI 的 Windows 上只占 9-12 物理像素，
                // 属于严重的纹理缩小。只开 linear 过滤仍然只采样 level 0，细笔画会走样。
                // 因此按需上传一条真正的预乘 alpha mip 链，让 D3D12/Metal 能积分整个
                // 字形的覆盖面积，而不是对它欠采样。
                if (MipLevels > 1)
                {
                    gpu.UploadMipChain(this, BuildMipChain(width, height, pixels));
                }
                else
                {
                    gpu.Upload(this, pixels);
                }
            }
        }
        catch
        {
            if (Sampler != 0)
            {
                SDL_ReleaseGPUSampler(gpu.Handle, Sampler);
            }
            SDL_ReleaseGPUTexture(gpu.Handle, Id);
            Id = Sampler = 0;
            throw;
        }
    }
    /// <summary>按 RGBA8 解码磁盘图片并立即上传；CPU 侧的解码结果不再保留。</summary>
    public static Texture Load(GpuDevice gpu, string path, bool repeat = false, bool linear = false, bool mipmaps = false)
    {
        using var input = File.OpenRead(path);
        var image = ImageResult.FromStream(input, ColorComponents.RedGreenBlueAlpha);
        return new(gpu, image.Width, image.Height, image.Data, repeat, linear, mipmaps);
    }

    static int CalculateMipLevels(int width, int height)
    {
        int levels = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            levels++;
        }
        return levels;
    }

    /// <summary>
    /// 生成非预乘 alpha 的 mip 链，但求平均时在预乘 alpha 空间进行。
    /// 独立平均 RGB 会把透明黑像素混进白色字形边缘，形成一圈暗边；
    /// 按 alpha 加权平均 RGB，才能在每一级都保住正确的覆盖颜色。
    /// </summary>
    static List<TextureMip> BuildMipChain(int width, int height, byte[] level0)
    {
        var result = new List<TextureMip>(CalculateMipLevels(width, height))
        {
            new(width, height, level0)
        };
        byte[] source = level0;
        int sourceWidth = width, sourceHeight = height;
        while (sourceWidth > 1 || sourceHeight > 1)
        {
            int nextWidth = Math.Max(1, sourceWidth / 2);
            int nextHeight = Math.Max(1, sourceHeight / 2);
            var next = new byte[checked(nextWidth * nextHeight * 4)];
            for (int y = 0; y < nextHeight; y++)
            {
                for (int x = 0; x < nextWidth; x++)
                {
                    int samples = 0, alphaSum = 0;
                    long redAlpha = 0, greenAlpha = 0, blueAlpha = 0;
                    for (int oy = 0; oy < 2; oy++)
                    {
                        int sy = y * 2 + oy;
                        if (sy >= sourceHeight) continue;
                        for (int ox = 0; ox < 2; ox++)
                        {
                            int sx = x * 2 + ox;
                            if (sx >= sourceWidth) continue;
                            int si = (sy * sourceWidth + sx) * 4;
                            int a = source[si + 3];
                            alphaSum += a;
                            redAlpha += source[si] * (long)a;
                            greenAlpha += source[si + 1] * (long)a;
                            blueAlpha += source[si + 2] * (long)a;
                            samples++;
                        }
                    }
                    int di = (y * nextWidth + x) * 4;
                    if (alphaSum > 0)
                    {
                        next[di] = (byte)Math.Clamp((redAlpha + alphaSum / 2) / alphaSum, 0, 255);
                        next[di + 1] = (byte)Math.Clamp((greenAlpha + alphaSum / 2) / alphaSum, 0, 255);
                        next[di + 2] = (byte)Math.Clamp((blueAlpha + alphaSum / 2) / alphaSum, 0, 255);
                    }
                    next[di + 3] = (byte)Math.Clamp((alphaSum + samples / 2) / Math.Max(1, samples), 0, 255);
                }
            }
            result.Add(new(nextWidth, nextHeight, next));
            source = next;
            sourceWidth = nextWidth;
            sourceHeight = nextHeight;
        }
        return result;
    }

    /// <summary>单个 mip level 的紧密排列 RGBA8 像素；长度必须等于 Width*Height*4。</summary>
    internal readonly record struct TextureMip(int Width, int Height, byte[] Pixels);
    /// <summary>先让队列结束对本资源的引用，再释放句柄；重复调用不会再次释放。</summary>
    public void Dispose()
    {
        if (Id == 0)
        {
            return;
        }
        gpu.Forget(this);
        SDL_ReleaseGPUSampler(gpu.Handle, Sampler);
        SDL_ReleaseGPUTexture(gpu.Handle, Id);
        Id = Sampler = 0;
    }
}
