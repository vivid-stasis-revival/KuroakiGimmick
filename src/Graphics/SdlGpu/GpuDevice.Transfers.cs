using System.Runtime.InteropServices;
using KuroakiGimmick.Native;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

/// <summary>纹理传输与同步读回：显式区分上传行距、读回行距和 fence 生命周期。</summary>
public sealed unsafe partial class GpuDevice : IDisposable
{
    /// <summary>创建 CPU/GPU 传输缓冲。调用者负责 finally 中释放，单位为字节。</summary>
    internal nint Transfer(uint size, bool download = false) => Check(SDL_CreateGPUTransferBuffer(Handle, new GPUTransferBufferCreateInfo
    {
        usage = download ? 1u : 0u,
        size = size
    }), "Create GPU transfer buffer");


    /// <summary>返回一行像素在 transfer buffer 中占用的字节数：D3D12 必须按 256 字节对齐，Metal/Vulkan 用紧密的 width*4。</summary>
    int TextureRowPitch(int width)
    {
        int tight = checked(width * 4);
        return Direct3D12 ? checked((tight + 255) & ~255) : tight;
    }

    /// <summary>上传紧密排列的 RGBA8 输入。D3D12 使用 256-byte 行距；Metal/Vulkan 使用紧密行距。</summary>
    internal void Upload(Texture texture, byte[] pixels) => UploadLevel(texture, 0, texture.Width, texture.Height, pixels);

    /// <summary>
    /// 逐个 mip 独立上传。这是有意为之的启动期路径：每个 level 用一个 transfer buffer，
    /// 可以绕开各后端对 transfer 偏移对齐的不同要求，同时保留 D3D12 的 256-byte 行距优化。
    /// Metal 与 Vulkan 仍保持紧密排列。
    /// </summary>
    internal void UploadMipChain(Texture texture, IReadOnlyList<Texture.TextureMip> levels)
    {
        if (levels.Count != texture.MipLevels)
        {
            throw new ArgumentException("Mip chain length does not match texture allocation.", nameof(levels));
        }
        for (int level = 0; level < levels.Count; level++)
        {
            var mip = levels[level];
            UploadLevel(texture, level, mip.Width, mip.Height, mip.Pixels);
        }
    }

    /// <summary>上传单个 mip level。pixels 必须是紧密排列的 width*height*4 RGBA8，本方法自带命令缓冲并同步提交。</summary>
    void UploadLevel(Texture texture, int mipLevel, int width, int height, byte[] pixels)
    {
        if (mipLevel < 0 || mipLevel >= texture.MipLevels || width <= 0 || height <= 0 ||
            pixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException("Invalid RGBA mip level.", nameof(pixels));
        }

        // SDL 也能在内部修复未对齐的 D3D12 行距，但代价是一次额外的临时拷贝。
        // 所以 D3D12 在这里就按 256-byte 行距对齐；
        // Vulkan 和 Metal 直接使用紧密排列的行。
        int pitch = TextureRowPitch(width);
        nint transfer = Transfer(checked((uint)(pitch * height))), command = 0;
        try
        {
            nint mapped = Check(SDL_MapGPUTransferBuffer(Handle, transfer, false), "Map texture upload");
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(pixels, y * width * 4, mapped + y * pitch, width * 4);
            }
            SDL_UnmapGPUTransferBuffer(Handle, transfer);
            command = Check(SDL_AcquireGPUCommandBuffer(Handle), "Acquire texture upload command");
            nint copy = Check(SDL_BeginGPUCopyPass(command), "Begin texture upload");
            SDL_UploadToGPUTexture(copy, new GPUTextureTransferInfo
            {
                transfer_buffer = transfer,
                pixels_per_row = (uint)pitch / 4,
                rows_per_layer = (uint)height
            }, new GPUTextureRegion
            {
                texture = texture.Id,
                mip_level = (uint)mipLevel,
                w = (uint)width,
                h = (uint)height,
                d = 1
            }, false);
            SDL_EndGPUCopyPass(copy);
            var submit = command;
            command = 0;
            Require(SDL_SubmitGPUCommandBuffer(submit));
        }
        finally
        {
            if (command != 0)
            {
                SDL_CancelGPUCommandBuffer(command);
            }
            SDL_ReleaseGPUTransferBuffer(Handle, transfer);
        }
    }

    /// <summary>同步读回顶端为原点的 RGBA8 像素；先等 fence，再剥离 GPU 行填充。</summary>
    internal byte[] Read(Texture texture)
    {
        Submit();
        int pitch = TextureRowPitch(texture.Width);
        nint transfer = Transfer(checked((uint)(pitch * texture.Height)), true), command = 0, fence = 0;
        try
        {
            command = Check(SDL_AcquireGPUCommandBuffer(Handle), "Acquire readback command");
            nint copy = Check(SDL_BeginGPUCopyPass(command), "Begin readback");
            SDL_DownloadFromGPUTexture(copy, new GPUTextureRegion
            {
                texture = texture.Id,
                w = (uint)texture.Width,
                h = (uint)texture.Height,
                d = 1
            }, new GPUTextureTransferInfo
            {
                transfer_buffer = transfer,
                pixels_per_row = (uint)pitch / 4,
                rows_per_layer = (uint)texture.Height
            });
            SDL_EndGPUCopyPass(copy);
            var submit = command;
            command = 0;
            fence = Check(SDL_SubmitGPUCommandBufferAndAcquireFence(submit), "Submit readback");
            Require(SDL_WaitForGPUFences(Handle, true, fence, 1));
            nint mapped = Check(SDL_MapGPUTransferBuffer(Handle, transfer, false), "Map readback");
            try
            {
                var bytes = new byte[checked(texture.Width * texture.Height * 4)];
                for (int y = 0; y < texture.Height; y++)
                {
                    Marshal.Copy(mapped + y * pitch, bytes, y * texture.Width * 4, texture.Width * 4);
                }
                return bytes;
            }
            finally
            {
                SDL_UnmapGPUTransferBuffer(Handle, transfer);
            }
        }
        finally
        {
            if (command != 0)
            {
                SDL_CancelGPUCommandBuffer(command);
            }
            if (fence != 0)
            {
                SDL_ReleaseGPUFence(Handle, fence);
            }
            SDL_ReleaseGPUTransferBuffer(Handle, transfer);
        }
    }
}

