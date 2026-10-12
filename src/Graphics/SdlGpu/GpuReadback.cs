using System.Runtime.InteropServices;
using KuroakiGimmick.Core;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

/// <summary>One reusable asynchronous readback slot. All methods run on the device/window thread; never map an unfinished download.</summary>
public sealed class GpuReadback : IDisposable
{
    readonly GpuDevice gpu;
    readonly int width, height, pitch;
    nint transfer, fence;
    public bool Pending => fence != 0;
    public int ByteLength => checked(width * height * 4);
    internal GpuReadback(GpuDevice gpu, int width, int height, int pitch)
    {
        this.gpu = gpu; this.width = width; this.height = height; this.pitch = pitch;
        transfer = gpu.Transfer(checked((uint)(pitch * height)), true);
    }
    public void Begin(Texture texture)
    {
        if (transfer == 0) throw new ObjectDisposedException(nameof(GpuReadback));
        if (Pending || texture.Width != width || texture.Height != height) throw new InvalidOperationException("Invalid readback slot reuse.");
        gpu.Submit();
        nint command = GpuDevice.Check(SDL_AcquireGPUCommandBuffer(gpu.Handle), "Acquire async readback");
        try
        {
            nint copy = GpuDevice.Check(SDL_BeginGPUCopyPass(command), "Begin async readback");
            SDL_DownloadFromGPUTexture(copy, new GPUTextureRegion { texture = texture.Id, w = (uint)width, h = (uint)height, d = 1 },
                new GPUTextureTransferInfo { transfer_buffer = transfer, pixels_per_row = (uint)pitch / 4, rows_per_layer = (uint)height });
            SDL_EndGPUCopyPass(copy);
            var submit = command; command = 0;
            fence = GpuDevice.Check(SDL_SubmitGPUCommandBufferAndAcquireFence(submit), "Submit async readback");
        }
        finally { if (command != 0) SDL_CancelGPUCommandBuffer(command); }
    }
    public bool Ready => Pending && SDL_QueryGPUFence(gpu.Handle, fence);
    public bool TryCopy(byte[] output)
    {
        if (output.Length < ByteLength) throw new ArgumentException("Readback destination is too small.");
        if (!Ready) return false;
        using var timing = PerformanceStats.Measure(PerformanceStats.Stage.Readback);
        nint mapped = GpuDevice.Check(SDL_MapGPUTransferBuffer(gpu.Handle, transfer, false), "Map async readback");
        try { for (int y = 0; y < height; y++) Marshal.Copy(mapped + y * pitch, output, y * width * 4, width * 4); }
        finally { SDL_UnmapGPUTransferBuffer(gpu.Handle, transfer); }
        SDL_ReleaseGPUFence(gpu.Handle, fence); fence = 0;
        PerformanceStats.Add(PerformanceStats.Counter.Readbacks, 1);
        return true;
    }
    public void Dispose()
    {
        // SDL defers transfer destruction until all submitted commands referencing it have finished.
        if (fence != 0) { SDL_ReleaseGPUFence(gpu.Handle, fence); fence = 0; }
        if (transfer != 0) { SDL_ReleaseGPUTransferBuffer(gpu.Handle, transfer); transfer = 0; }
    }
}
