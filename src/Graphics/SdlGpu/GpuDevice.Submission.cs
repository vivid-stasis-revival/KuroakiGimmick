using System.Runtime.InteropServices;
using KuroakiGimmick.Native;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

/// <summary>SDL_GPU 命令提交：先统一上传，再按目标/清屏边界切换 render pass。</summary>
public sealed unsafe partial class GpuDevice : IDisposable
{
    /// <summary>仅扩容共享顶点/上传缓冲，最少分配 1 MiB；不逐帧分配或缩容。</summary>
    void EnsureVertices(uint size)
    {
        if (size <= vertexCapacity)
        {
            return;
        }
        if (vertexBuffer != 0)
        {
            SDL_ReleaseGPUBuffer(Handle, vertexBuffer);
        }
        if (uploadBuffer != 0)
        {
            SDL_ReleaseGPUTransferBuffer(Handle, uploadBuffer);
        }
        vertexBuffer = uploadBuffer = 0;
        vertexCapacity = Math.Max(size, 1024 * 1024);
        vertexBuffer = Check(SDL_CreateGPUBuffer(Handle, new GPUBufferCreateInfo
        {
            usage = 1,
            size = vertexCapacity
        }), "Create vertex buffer");
        uploadBuffer = Transfer(vertexCapacity);
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

    /// <summary>一次 copy pass 上传全部顶点，随后消费批次。即使窗口无交换链纹理，离屏导出仍继续。</summary>
    public void Submit(bool present = false, nint presentWindow = 0)
    {
        nint outputWindow = presentWindow == 0 ? window : presentWindow;
        if (batches.Count == 0)
        {
            return;
        }
        nint command = Check(SDL_AcquireGPUCommandBuffer(Handle), "Acquire render command"), pass = 0;
        bool acquired = false;
        try
        {
            nint swapchain = 0;
            uint swapWidth = 0, swapHeight = 0, swapchainFormat = 0;
            if (present && (!hidden || presentWindow != 0))
            {
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
                uint size = checked((uint)vertices.Count * 4);
                EnsureVertices(size);
                nint mapped = Check(SDL_MapGPUTransferBuffer(Handle, uploadBuffer, true), "Map vertex upload");
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
                }, true);
                SDL_EndGPUCopyPass(copy);
            }

            nint activeTarget = 0, boundPipeline = 0;
            bool vertexBufferBound = false;
            IntRect? boundScissor = null;
            Shader? uniformShader = null, samplerShader = null;
            byte[]? boundVertexUniforms = null, boundFragmentUniforms = null;
            int boundVertexUniformLength = 0, boundFragmentUniformLength = 0;
            GPUTextureSamplerBinding[]? boundTextures = null;
            int boundTextureCount = 0;

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
                    vertexBufferBound = false;
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

                if (!vertexBufferBound)
                {
                    SDL_BindGPUVertexBuffers(pass, 0, new GPUBufferBinding
                    {
                        buffer = vertexBuffer
                    }, 1);
                    vertexBufferBound = true;
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

                SDL_DrawGPUPrimitives(pass, batch.Count, 1, batch.First, 0);
            }
            if (pass != 0)
            {
                SDL_EndGPURenderPass(pass);
                pass = 0;
            }
            var submit = command;
            command = 0;
            Require(SDL_SubmitGPUCommandBuffer(submit));
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
                    SDL_SubmitGPUCommandBuffer(command);
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
