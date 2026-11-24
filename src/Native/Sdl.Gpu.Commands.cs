// SDL_GPU 原生函数声明；仅做封送，不在 ABI 层夹带渲染策略。
// 这里只放 extern 声明：任何"什么时候提交"、"用哪个 blend"、"要不要 cycle"的判断都属于上层，
// 不要在这个文件里包装便利方法或加默认参数。签名照抄 SDL_gpu.h，不改参数顺序与 [MarshalAs]。

using System.Runtime.InteropServices;

namespace KuroakiGimmick.Native;

public static unsafe partial class Sdl
{
    /// <summary>按创建描述在设备上创建shader。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="info">传给 SDL 的资源创建描述。</param>
    /// <returns>成功时返回原生句柄，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUShader(nint device, in GPUShaderCreateInfo info);

    /// <summary>按创建描述在设备上创建纹理。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="info">传给 SDL 的资源创建描述。</param>
    /// <returns>成功时返回原生句柄，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUTexture(nint device, in GPUTextureCreateInfo info);

    /// <summary>按创建描述在设备上创建采样器。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="info">传给 SDL 的资源创建描述。</param>
    /// <returns>成功时返回原生句柄，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUSampler(nint device, in GPUSamplerCreateInfo info);

    /// <summary>按创建描述在设备上创建设备缓冲区。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="info">传给 SDL 的资源创建描述。</param>
    /// <returns>成功时返回原生句柄，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUBuffer(nint device, in GPUBufferCreateInfo info);

    /// <summary>按创建描述在设备上创建传输缓冲区。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="info">传给 SDL 的资源创建描述。</param>
    /// <returns>成功时返回原生句柄，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUTransferBuffer(nint device, in GPUTransferBufferCreateInfo info);

    /// <summary>按创建描述在设备上创建图形管线。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="info">传给 SDL 的资源创建描述。</param>
    /// <returns>成功时返回原生句柄，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_CreateGPUGraphicsPipeline(nint device, in GPUGraphicsPipelineCreateInfo info);

    // color 用 in 传首元素；count 大于 1 时原生侧按数组连续读取，因此调用方必须保证这些结构体在内存中相邻。
    /// <summary>在命令缓冲区中开始向所给附件绘制。</summary>
    /// <param name="command">记录本次操作的 GPU 命令缓冲区。</param>
    /// <param name="color">颜色附件描述数组的首元素。</param>
    /// <param name="count">连续数组中的元素数。</param>
    /// <param name="depth">可选的深度模板附件描述地址，0 表示省略。</param>
    /// <returns>成功时返回原生句柄，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_BeginGPURenderPass(nint command, in GPUColorTargetInfo color, uint count, nint depth);

    /// <summary>结束当前渲染 pass。</summary>
    /// <param name="pass">记录这些命令的活动 pass 句柄。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_EndGPURenderPass(nint pass);

    /// <summary>指定后续绘制使用的图形管线。</summary>
    /// <param name="pass">记录这些命令的活动 pass 句柄。</param>
    /// <param name="pipeline">后续绘制所用的图形管线。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_BindGPUGraphicsPipeline(nint pass, nint pipeline);

    /// <summary>设置当前渲染 pass 的视口与深度范围。</summary>
    /// <param name="pass">记录这些命令的活动 pass 句柄。</param>
    /// <param name="viewport">新的视口及深度范围。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_SetGPUViewport(nint pass, in GPUViewport viewport);

    /// <summary>限制当前渲染 pass 的像素写入区域。</summary>
    /// <param name="pass">记录这些命令的活动 pass 句柄。</param>
    /// <param name="rect">使用目标像素坐标的裁剪矩形。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_SetGPUScissor(nint pass, in IntRect rect);

    /// <summary>从指定槽位起绑定连续的顶点缓冲区。</summary>
    /// <param name="pass">记录这些命令的活动 pass 句柄。</param>
    /// <param name="first">首个绑定槽位的索引。</param>
    /// <param name="bindings">与槽位顺序一致的连续绑定描述。</param>
    /// <param name="count">连续数组中的元素数。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_BindGPUVertexBuffers(nint pass, uint first, in GPUBufferBinding bindings, uint count);

    /// <summary>从指定槽位起绑定片元阶段的纹理和采样器。</summary>
    /// <param name="pass">记录这些命令的活动 pass 句柄。</param>
    /// <param name="first">首个绑定槽位的索引。</param>
    /// <param name="bindings">与槽位顺序一致的连续绑定描述。</param>
    /// <param name="count">连续数组中的元素数。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_BindGPUFragmentSamplers(nint pass, uint first, GPUTextureSamplerBinding* bindings, uint count);

    /// <summary>为后续绘制写入顶点阶段的 uniform 数据。</summary>
    /// <param name="command">记录本次操作的 GPU 命令缓冲区。</param>
    /// <param name="slot">目标 uniform 槽位索引。</param>
    /// <param name="data">输入数据的首地址。</param>
    /// <param name="length">输入数据的字节数。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_PushGPUVertexUniformData(nint command, uint slot, nint data, uint length);

    /// <summary>为后续绘制写入片元阶段的 uniform 数据。</summary>
    /// <param name="command">记录本次操作的 GPU 命令缓冲区。</param>
    /// <param name="slot">目标 uniform 槽位索引。</param>
    /// <param name="data">输入数据的首地址。</param>
    /// <param name="length">输入数据的字节数。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_PushGPUFragmentUniformData(nint command, uint slot, nint data, uint length);

    /// <summary>记录不使用索引缓冲区的实例化绘制命令。</summary>
    /// <param name="pass">记录这些命令的活动 pass 句柄。</param>
    /// <param name="vertexCount">每个实例的顶点数。</param>
    /// <param name="instanceCount">要绘制的实例数。</param>
    /// <param name="firstVertex">首个顶点索引。</param>
    /// <param name="firstInstance">首个实例索引。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DrawGPUPrimitives(nint pass, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    /// <summary>获取传输缓冲区的 CPU 可访问地址。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="buffer">传输缓冲区句柄。</param>
    /// <param name="cycle">若目标资源已被占用，是否允许 SDL 轮换底层存储。</param>
    /// <returns>CPU 可访问的数据地址，解除映射后失效；失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_MapGPUTransferBuffer(nint device, nint buffer, [MarshalAs(UnmanagedType.I1)] bool cycle);

    /// <summary>结束 CPU 映射，使缓冲区可参与 GPU 传输。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="buffer">传输缓冲区句柄。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_UnmapGPUTransferBuffer(nint device, nint buffer);

    /// <summary>在命令缓冲区中开始记录复制操作。</summary>
    /// <param name="command">记录本次操作的 GPU 命令缓冲区。</param>
    /// <returns>成功时返回原生句柄，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_BeginGPUCopyPass(nint command);

    /// <summary>结束当前复制 pass。</summary>
    /// <param name="pass">记录这些命令的活动 pass 句柄。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_EndGPUCopyPass(nint pass);

    /// <summary>从传输缓冲区向目标纹理区域上传数据。</summary>
    /// <param name="pass">记录这些命令的活动 pass 句柄。</param>
    /// <param name="source">源数据的位置和传输描述。</param>
    /// <param name="destination">接收数据的位置和传输描述。</param>
    /// <param name="cycle">若目标资源已被占用，是否允许 SDL 轮换底层存储。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_UploadToGPUTexture(nint pass, in GPUTextureTransferInfo source, in GPUTextureRegion destination,
        [MarshalAs(UnmanagedType.I1)] bool cycle);

    /// <summary>将源纹理区域复制到下载缓冲区。</summary>
    /// <param name="pass">记录这些命令的活动 pass 句柄。</param>
    /// <param name="source">源数据的位置和传输描述。</param>
    /// <param name="destination">接收数据的位置和传输描述。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_DownloadFromGPUTexture(nint pass, in GPUTextureRegion source, in GPUTextureTransferInfo destination);

    /// <summary>将传输缓冲区中的数据上传到设备缓冲区。</summary>
    /// <param name="pass">记录这些命令的活动 pass 句柄。</param>
    /// <param name="source">源数据的位置和传输描述。</param>
    /// <param name="destination">接收数据的位置和传输描述。</param>
    /// <param name="cycle">若目标资源已被占用，是否允许 SDL 轮换底层存储。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_UploadToGPUBuffer(nint pass, in GPUTransferBufferLocation source, in GPUBufferRegion destination,
        [MarshalAs(UnmanagedType.I1)] bool cycle);

    // 返回的 fence 由调用方拥有，用完必须 SDL_ReleaseGPUFence，否则每帧泄漏一个。
    /// <summary>提交命令缓冲区并获取用于追踪完成状态的 fence。</summary>
    /// <param name="command">记录本次操作的 GPU 命令缓冲区。</param>
    /// <returns>调用方须交还给 SDL_ReleaseGPUFence 的 fence，失败时为 0。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint SDL_SubmitGPUCommandBufferAndAcquireFence(nint command);

    /// <summary>查询关联的命令缓冲区是否已完成。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="fence">要查询的同步 fence。</param>
    /// <returns>关联命令执行完毕时为 true。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_QueryGPUFence(nint device, nint fence);

    // fences 用 in 传数组首元素，count 为元素个数；all 为 false 表示任意一个完成即返回。
    /// <summary>等待给定 fence 全部或任意一个完成。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="all">为 true 时等待全部完成，否则等待任意一个完成。</param>
    /// <param name="fences">连续 fence 句柄数组的首元素。</param>
    /// <param name="count">连续数组中的元素数。</param>
    /// <returns>操作成功时为 true，失败时为 false。</returns>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SDL_WaitForGPUFences(nint device, [MarshalAs(UnmanagedType.I1)] bool all, in nint fences, uint count);

    /// <summary>通知设备在安全时机释放纹理。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="resource">由该设备创建或取得的资源句柄。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUTexture(nint device, nint resource);

    /// <summary>通知设备在安全时机释放采样器。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="resource">由该设备创建或取得的资源句柄。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUSampler(nint device, nint resource);

    /// <summary>通知设备在安全时机释放设备缓冲区。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="resource">由该设备创建或取得的资源句柄。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUBuffer(nint device, nint resource);

    /// <summary>通知设备在安全时机释放传输缓冲区。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="resource">由该设备创建或取得的资源句柄。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUTransferBuffer(nint device, nint resource);

    /// <summary>通知设备在安全时机释放shader。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="resource">由该设备创建或取得的资源句柄。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUShader(nint device, nint resource);

    /// <summary>通知设备在安全时机释放图形管线。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="resource">由该设备创建或取得的资源句柄。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUGraphicsPipeline(nint device, nint resource);

    /// <summary>通知设备在安全时机释放同步 fence。</summary>
    /// <param name="device">所属的 SDL GPU 设备句柄。</param>
    /// <param name="resource">由该设备创建或取得的资源句柄。</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void SDL_ReleaseGPUFence(nint device, nint resource);
}

