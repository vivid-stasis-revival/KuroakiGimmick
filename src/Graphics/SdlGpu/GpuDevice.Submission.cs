using System.Runtime.InteropServices;
using KuroakiGimmick.Native;
using KuroakiGimmick.Core;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

/// <summary>SDL_GPU 命令提交：先统一上传，再按目标/清屏边界切换 render pass。</summary>
public sealed unsafe partial class GpuDevice : IDisposable
{
    /// <summary>仅扩容共享顶点/上传缓冲，最少分配 1 MiB；不逐帧分配或缩容。</summary>
    void EnsureVertices(MeshSlot slot, uint size)
    {
        if (size <= slot.Capacity)
        {
            return;
        }
        ulong grown = Math.Max(1024UL * 1024, Math.Max(size, (ulong)slot.Capacity * 2));
        uint capacity = checked((uint)Math.Min(grown, uint.MaxValue));
        nint nextVertex = Check(SDL_CreateGPUBuffer(Handle, new GPUBufferCreateInfo { usage = 1, size = capacity }), "Create vertex buffer");
        nint nextUpload;
        try { nextUpload = Transfer(capacity); }
        catch { SDL_ReleaseGPUBuffer(Handle, nextVertex); throw; }
        // This slot's fence completed before growth/reuse; old resources are no longer being read by any command.
        if (slot.Vertex != 0) SDL_ReleaseGPUBuffer(Handle, slot.Vertex);
        if (slot.Upload != 0) SDL_ReleaseGPUTransferBuffer(Handle, slot.Upload);
        slot.Vertex = nextVertex; slot.Upload = nextUpload; slot.Capacity = capacity;
    }

    /// <summary>异常退出时丢弃尚未提交的 CPU 批次，避免 Dispose 再提交并掩盖原始异常。</summary>
    public void DiscardPendingCommands()
    {
        foreach (var batch in batches) ReleaseBatchStorage(batch);
        batches.Clear();
        vertices.Clear();
    }

    /// <summary>退出失败帧后阻止 Canvas.Dispose/Flush 再次排队并覆盖原始错误。</summary>
    public void AbortPendingCommands()
    {
        aborted = true;
        DiscardPendingCommands();
    }

    /// <summary>与本 pass 内已推送的 uniform 快照逐字节比较，用于跳过重复 push；长度不同即判不等。</summary>
    static bool SameBytes(byte[]? a, int aLength, byte[]? b, int bLength)
    {
        if (aLength != bLength)
        {
            return false;
        }
        if (aLength == 0)
        {
            return true;
        }
        return a != null && b != null && a.AsSpan(0, aLength).SequenceEqual(b.AsSpan(0, bLength));
    }

    /// <summary>比较两组 sampler 绑定的 texture/sampler 句柄，用于跳过重复的 fragment sampler 绑定。</summary>
    static bool SameBindings(GPUTextureSamplerBinding[]? a, int aCount, GPUTextureSamplerBinding[]? b, int bCount)
    {
        if (aCount != bCount)
        {
            return false;
        }
        if (aCount == 0)
        {
            return true;
        }
        if (a == null || b == null)
        {
            return false;
        }
        for (int i = 0; i < aCount; i++)
        {
            if (a[i].texture != b[i].texture || a[i].sampler != b[i].sampler)
            {
                return false;
            }
        }
        return true;
    }

    bool SubmitTracked(nint command, MeshSlot? mesh, bool presentation)
    {
        if (mesh == null && !presentation) return SDL_SubmitGPUCommandBuffer(command);
        nint handle = SDL_SubmitGPUCommandBufferAndAcquireFence(command);
        if (handle == 0) { SDL_WaitForGPUIdle(Handle); return false; } // exceptional path: never reuse an untracked upload
        var fence = new SubmissionFence(handle);
        if (mesh != null) mesh.Fence = fence;
        if (presentation) presentationFences.Enqueue(fence);
        return true;
    }

    /// <summary>一次 copy pass 上传全部顶点，随后消费批次。即使窗口无交换链纹理，离屏导出仍继续。</summary>
    public void Submit(bool present = false, nint presentWindow = 0)
    {
        nint outputWindow = presentWindow == 0 ? window : presentWindow;
        if (aborted || batches.Count == 0)
        {
            return;
        }
        nint command, pass = 0;
        using (PerformanceStats.Measure(PerformanceStats.Stage.GpuWait)) command = Check(SDL_AcquireGPUCommandBuffer(Handle), "Acquire render command");
        PerformanceStats.Add(PerformanceStats.Counter.Batches, batches.Count);
        bool acquired = false;
        MeshSlot? mesh = null;
        nint vertexBuffer = 0, uploadBuffer = 0;
        try
        {
            nint swapchain = 0;
            uint swapWidth = 0, swapHeight = 0, swapchainFormat = 0;
            if (present && (!hidden || presentWindow != 0))
            {
                using (PerformanceStats.Measure(PerformanceStats.Stage.GpuWait))
                    Require(SDL_WaitAndAcquireGPUSwapchainTexture(command, outputWindow, out swapchain, out swapWidth, out swapHeight));
                acquired = swapchain != 0;
                if (acquired)
                {
                    // 格式属于这个呈现目标本身，而不是每个绘制批次的属性。
                    // 每个屏幕批次都查一次原生格式，在老 Vulkan 驱动上开销意外地明显。
                    swapchainFormat = SDL_GetGPUSwapchainTextureFormat(Handle, outputWindow);
                }
            }
            if (vertices.Count > 0)
            {
                using var uploadTiming = PerformanceStats.Measure(PerformanceStats.Stage.GpuUpload);
                uint size = checked((uint)vertices.Count * 4);
                mesh = meshSlots[meshCursor]; meshCursor = (meshCursor + 1) % meshSlots.Length;
                if (mesh.Fence is { } previous)
                {
                    using var backpressure = PerformanceStats.Measure(PerformanceStats.Stage.GpuWait);
                    while (!previous.Complete(Handle)) { if (!Offscreen) SDL_PumpEvents(); SDL_Delay(1); }
                    previous.Release(Handle); mesh.Fence = null;
                }
                EnsureVertices(mesh, size); vertexBuffer = mesh.Vertex; uploadBuffer = mesh.Upload;
                PerformanceStats.Add(PerformanceStats.Counter.UploadBytes, size);
                nint mapped;
                using (PerformanceStats.Measure(PerformanceStats.Stage.GpuWait)) mapped = Check(SDL_MapGPUTransferBuffer(Handle, uploadBuffer, false), "Map vertex upload");
                CollectionsMarshal.AsSpan(vertices).CopyTo(new Span<float>((void*)mapped, vertices.Count));
                SDL_UnmapGPUTransferBuffer(Handle, uploadBuffer);
                nint copy = Check(SDL_BeginGPUCopyPass(command), "Begin vertex upload");
                SDL_UploadToGPUBuffer(copy, new GPUTransferBufferLocation
                {
                    transfer_buffer = uploadBuffer
                }, new GPUBufferRegion
                {
                    buffer = vertexBuffer,
                    size = size
                }, false);
                SDL_EndGPUCopyPass(copy);
            }

            nint activeTarget = 0, boundPipeline = 0;
            uint boundVertexOffset = uint.MaxValue;
            IntRect? boundScissor = null;
            Shader? uniformShader = null, samplerShader = null;
            byte[]? boundVertexUniforms = null, boundFragmentUniforms = null;
            int boundVertexUniformLength = 0, boundFragmentUniformLength = 0;
            GPUTextureSamplerBinding[]? boundTextures = null;
            int boundTextureCount = 0;

            var recordTiming = PerformanceStats.Measure(PerformanceStats.Stage.GpuRecord);
            foreach (var batch in batches)
            {
                nint target = batch.Target == 0 ? swapchain : batch.Target;
                if (target == 0)
                {
                    continue;
                }
                // 窗口最小化或隐藏时拿不到交换链纹理，但离屏/导出目标仍然照常渲染。
                int width = batch.Target == 0 ? (int)swapWidth : batch.Width;
                int height = batch.Target == 0 ? (int)swapHeight : batch.Height;
                uint format = batch.Target == 0 ? swapchainFormat : Rgba8;
                if (pass == 0 || target != activeTarget || batch.Clear != null)
                {
                    if (pass != 0)
                    {
                        SDL_EndGPURenderPass(pass);
                    }
                    pass = 0;
                    var clear = batch.Clear ?? default;
                    pass = Check(SDL_BeginGPURenderPass(command, new GPUColorTargetInfo
                    {
                        texture = target,
                        clear_color = new()
                        {
                            r = clear.R,
                            g = clear.G,
                            b = clear.B,
                            a = clear.A
                        },
                        load_op = batch.Clear != null ? 1u : 0u,
                        store_op = 0
                    }, 1, 0), "Begin GPU render pass");
                    activeTarget = target;
                    SDL_SetGPUViewport(pass, new GPUViewport
                    {
                        w = width,
                        h = height,
                        max_depth = 1
                    });

                    // 原生 GPU 状态一律按 render pass 局部看待：每开一个 pass 就清空托管侧缓存，
                    // 避免代码依赖某个后端跨 pass 保留 pipeline、scissor 或 uniform 状态。
                    boundPipeline = 0;
                    boundVertexOffset = uint.MaxValue;
                    boundScissor = null;
                    uniformShader = null;
                    samplerShader = null;
                    boundVertexUniforms = boundFragmentUniforms = null;
                    boundVertexUniformLength = boundFragmentUniformLength = 0;
                    boundTextures = null;
                    boundTextureCount = 0;
                }
                if (batch.Shader is not { } shader)
                {
                    continue;
                }

                nint pipeline = shader.Pipeline(format, batch.Source, batch.Destination);
                if (pipeline != boundPipeline)
                {
                    SDL_BindGPUGraphicsPipeline(pass, pipeline);
                    boundPipeline = pipeline;
                }

                IntRect scissor = batch.Clip ?? new IntRect
                {
                    w = width,
                    h = height
                };
                if (!boundScissor.HasValue || !SameRect(boundScissor, scissor))
                {
                    SDL_SetGPUScissor(pass, scissor);
                    boundScissor = scissor;
                }

                if (boundVertexOffset != batch.First)
                {
                    SDL_BindGPUVertexBuffers(pass, 0, new GPUBufferBinding
                    {
                        buffer = vertexBuffer,
                        offset = batch.First
                    }, 1);
                    boundVertexOffset = batch.First;
                }

                bool sameUniformShader = ReferenceEquals(uniformShader, shader);
                fixed (byte* v = batch.VertexUniforms, f = batch.FragmentUniforms)
                {
                    if (batch.VertexUniformLength > 0 &&
                        (!sameUniformShader || !SameBytes(batch.VertexUniforms, batch.VertexUniformLength,
                            boundVertexUniforms, boundVertexUniformLength)))
                    {
                        SDL_PushGPUVertexUniformData(command, 0, (nint)v, (uint)batch.VertexUniformLength);
                    }
                    if (batch.FragmentUniformLength > 0 &&
                        (!sameUniformShader || !SameBytes(batch.FragmentUniforms, batch.FragmentUniformLength,
                            boundFragmentUniforms, boundFragmentUniformLength)))
                    {
                        SDL_PushGPUFragmentUniformData(command, 0, (nint)f, (uint)batch.FragmentUniformLength);
                    }
                }
                uniformShader = shader;
                boundVertexUniforms = batch.VertexUniforms;
                boundVertexUniformLength = batch.VertexUniformLength;
                boundFragmentUniforms = batch.FragmentUniforms;
                boundFragmentUniformLength = batch.FragmentUniformLength;

                bool sameSamplerState = ReferenceEquals(samplerShader, shader) &&
                    SameBindings(batch.Textures, batch.TextureCount, boundTextures, boundTextureCount);
                if (batch.TextureCount > 0 && !sameSamplerState)
                {
                    fixed (GPUTextureSamplerBinding* bindings = batch.Textures)
                    {
                        SDL_BindGPUFragmentSamplers(pass, 0, bindings, (uint)batch.TextureCount);
                    }
                }
                samplerShader = shader;
                boundTextures = batch.Textures;
                boundTextureCount = batch.TextureCount;

                SDL_DrawGPUPrimitives(pass, shader.Instanced ? 6u : batch.Count, shader.Instanced ? batch.Count : 1u, 0, 0);
                KuroakiGimmick.Core.PerformanceStats.Add(KuroakiGimmick.Core.PerformanceStats.Counter.DrawCalls, 1);
            }
            if (pass != 0)
            {
                SDL_EndGPURenderPass(pass);
                pass = 0;
            }
            recordTiming.Dispose();
            var submit = command;
            command = 0;
            using (PerformanceStats.Measure(PerformanceStats.Stage.GpuSubmit))
            {
                Require(SubmitTracked(submit, mesh, acquired && presentWindow == 0));
            }
        }
        finally
        {
            if (pass != 0)
            {
                SDL_EndGPURenderPass(pass);
            }
            if (command != 0)
            {
                // 已经取得交换链纹理的命令缓冲只能 Submit，SDL 禁止 Cancel。
                if (acquired)
                {
                    SubmitTracked(command, mesh, presentWindow == 0);
                }
                else
                {
                    SDL_CancelGPUCommandBuffer(command);
                }
            }
            // 池化数组必须等到这里才归还：上面的循环一直在拿它们和已绑定状态做内容比较。
            foreach (var batch in batches)
            {
                ReleaseBatchStorage(batch);
            }
            batches.Clear();
            vertices.Clear();
        }
    }
}
