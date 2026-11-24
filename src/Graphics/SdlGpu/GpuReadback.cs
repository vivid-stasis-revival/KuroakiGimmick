using System.Runtime.InteropServices;
using KuroakiGimmick.Core;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

/// <summary>可复用的异步读回槽位。设备或窗口线程发起下载，只有 fence 就绪后才能映射缓冲区。</summary>
public sealed class GpuReadback : IDisposable
{
    readonly GpuDevice gpu;
    readonly int width, height, pitch;
    nint transfer, fence;
    /// <summary>是否持有尚未消费的下载 fence，已就绪时仍为 true。</summary>
    public bool Pending => fence != 0;
    /// <summary>去除行填充后的 RGBA 输出字节数。</summary>
    public int ByteLength => checked(width * height * 4);
    internal GpuReadback(GpuDevice gpu, int width, int height, int pitch)
    {
        this.gpu = gpu;
        this.width = width;
        this.height = height;
        this.pitch = pitch;
        transfer = gpu.Transfer(checked((uint)(pitch * height)), true);
    }
    /// <summary>提交源纹理下载命令并保留其 fence，用于后续非阻塞查询。</summary>
    /// <param name="texture">用于该操作的 GPU 纹理。</param>
    /// <exception cref="ObjectDisposedException">此槽位的传输缓冲区已释放。</exception>
    /// <exception cref="InvalidOperationException">槽位仍有未消费的下载，或源纹理尺寸与槽位不符。</exception>
    public void Begin(Texture texture)
    {
        if (transfer == 0) throw new ObjectDisposedException(nameof(GpuReadback));
        if (Pending || texture.Width != width || texture.Height != height) throw new InvalidOperationException("Invalid readback slot reuse.");
        // 1. 提交纹理此前的绘制，随后在单独的命令缓冲区记录下载。
        gpu.Submit();
        nint command = GpuDevice.Check(SDL_AcquireGPUCommandBuffer(gpu.Handle), "Acquire async readback");
        try
        {
            nint copy = GpuDevice.Check(SDL_BeginGPUCopyPass(command), "Begin async readback");
            SDL_DownloadFromGPUTexture(copy, new GPUTextureRegion { texture = texture.Id, w = (uint)width, h = (uint)height, d = 1 },
                new GPUTextureTransferInfo { transfer_buffer = transfer, pixels_per_row = (uint)pitch / 4, rows_per_layer = (uint)height });
            SDL_EndGPUCopyPass(copy);
            // 2. 提交后由 fence 追踪完成状态，命令句柄不再参与异常清理。
            var submit = command;
            command = 0;
            fence = GpuDevice.Check(SDL_SubmitGPUCommandBufferAndAcquireFence(submit), "Submit async readback");
        }
        finally
        {
            if (command != 0) SDL_CancelGPUCommandBuffer(command);
        }
    }
    /// <summary>当前槽位是否有已完成的 GPU 下载。</summary>
    public bool Ready => Pending && SDL_QueryGPUFence(gpu.Handle, fence);
    /// <summary>fence 就绪后逐行复制像素并释放 fence；未就绪时返回 false。</summary>
    /// <param name="output">用于接收紧密 RGBA 数据的数组。</param>
    /// <returns>完成复制并消费 fence 时为 true，未就绪时为 false。</returns>
    /// <exception cref="ArgumentException">输出数组容不下紧密排列的 RGBA8 帧。</exception>
    public bool TryCopy(byte[] output)
    {
        if (output.Length < ByteLength) throw new ArgumentException("Readback destination is too small.");
        if (!Ready) return false;
        using var timing = PerformanceStats.Measure(PerformanceStats.Stage.Readback);
        nint mapped = GpuDevice.Check(SDL_MapGPUTransferBuffer(gpu.Handle, transfer, false), "Map async readback");
        // SAFETY: 传输区按 pitch × height 分配，输出区已检查紧密排列所需长度，仅复制每行有效像素。
        try
        {
            for (int y = 0; y < height; y++) Marshal.Copy(mapped + y * pitch, output, y * width * 4, width * 4);
        }
        finally
        {
            SDL_UnmapGPUTransferBuffer(gpu.Handle, transfer);
        }
        SDL_ReleaseGPUFence(gpu.Handle, fence);
        fence = 0;
        PerformanceStats.Add(PerformanceStats.Counter.Readbacks, 1);
        return true;
    }
    /// <summary>释放仍持有的 fence 和传输缓冲区，并清零对应句柄。</summary>
    public void Dispose()
    {
        // LIFETIME: SDL 延后回收仍被已提交命令引用的传输资源；先交还 fence，再交还缓冲区。
        if (fence != 0)
        {
            SDL_ReleaseGPUFence(gpu.Handle, fence);
            fence = 0;
        }
        if (transfer != 0)
        {
            SDL_ReleaseGPUTransferBuffer(gpu.Handle, transfer);
            transfer = 0;
        }
    }
}
