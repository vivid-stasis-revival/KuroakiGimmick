using System.Runtime.InteropServices;

namespace KuroakiGimmick.Native;

/// <summary>
/// SDL_GPU 原生函数声明；仅做封送，不在 ABI 层夹带渲染策略。
/// 这里只放 extern 声明：任何"什么时候提交"、"用哪个 blend"、"要不要 cycle"的判断都属于上层，
/// 不要在这个文件里包装便利方法或加默认参数。签名照抄 SDL_gpu.h，不改参数顺序与 [MarshalAs]。
/// </summary>
public static unsafe partial class Sdl
{
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUShader(nint device, in GPUShaderCreateInfo info);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUTexture(nint device, in GPUTextureCreateInfo info);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUSampler(nint device, in GPUSamplerCreateInfo info);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUBuffer(nint device, in GPUBufferCreateInfo info);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUTransferBuffer(nint device, in GPUTransferBufferCreateInfo info);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUGraphicsPipeline(nint device, in GPUGraphicsPipelineCreateInfo info);

    // color 用 in 传首元素；count 大于 1 时原生侧按数组连续读取，因此调用方必须保证这些结构体在内存中相邻。
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_BeginGPURenderPass(nint command, in GPUColorTargetInfo color, uint count, nint depth);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_EndGPURenderPass(nint pass);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_BindGPUGraphicsPipeline(nint pass, nint pipeline);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_SetGPUViewport(nint pass, in GPUViewport viewport);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_SetGPUScissor(nint pass, in IntRect rect);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_BindGPUVertexBuffers(nint pass, uint first, in GPUBufferBinding bindings, uint count);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_BindGPUFragmentSamplers(nint pass, uint first, GPUTextureSamplerBinding* bindings, uint count);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_PushGPUVertexUniformData(nint command, uint slot, nint data, uint length);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_PushGPUFragmentUniformData(nint command, uint slot, nint data, uint length);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DrawGPUPrimitives(nint pass, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_MapGPUTransferBuffer(nint device, nint buffer, [MarshalAs(UnmanagedType.I1)] bool cycle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_UnmapGPUTransferBuffer(nint device, nint buffer);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_BeginGPUCopyPass(nint command);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_EndGPUCopyPass(nint pass);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_UploadToGPUTexture(nint pass, in GPUTextureTransferInfo source, in GPUTextureRegion destination,
        [MarshalAs(UnmanagedType.I1)] bool cycle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DownloadFromGPUTexture(nint pass, in GPUTextureRegion source, in GPUTextureTransferInfo destination);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_UploadToGPUBuffer(nint pass, in GPUTransferBufferLocation source, in GPUBufferRegion destination,
        [MarshalAs(UnmanagedType.I1)] bool cycle);

    // 返回的 fence 由调用方拥有，用完必须 SDL_ReleaseGPUFence，否则每帧泄漏一个。
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_SubmitGPUCommandBufferAndAcquireFence(nint command);

    // fences 用 in 传数组首元素，count 为元素个数；all 为 false 表示任意一个完成即返回。
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_WaitForGPUFences(nint device, [MarshalAs(UnmanagedType.I1)] bool all, in nint fences, uint count);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUTexture(nint device, nint resource);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUSampler(nint device, nint resource);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUBuffer(nint device, nint resource);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUTransferBuffer(nint device, nint resource);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUShader(nint device, nint resource);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUGraphicsPipeline(nint device, nint resource);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUFence(nint device, nint resource);
}

