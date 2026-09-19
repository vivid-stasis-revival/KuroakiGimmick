using System.Buffers;
using System.Runtime.InteropServices;
using KuroakiGimmick.Native;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

// 批次一直保留到提交，让全部顶点数据在渲染前用一次 copy pass 统一上传。
// 每次 Draw 都对 uniform、纹理绑定和裁剪做快照。
/// <summary>窗口关联的 GPU 设备与 CPU 批次队列。所有调用留在创建窗口的线程；Dispose 晚于依附它的纹理和 shader。</summary>
public sealed unsafe partial class GpuDevice : IDisposable
{
    // SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM。它是原生枚举值，不是每像素字节数。
    public const uint Rgba8 = 4;

    public nint Handle { get; private set; }

    /// <summary>SDL 为本设备实际选中的后端驱动名，形如 metal / direct3d12 / vulkan；查询成功前保持 unknown，比较时忽略大小写。</summary>
    public string Driver { get; private set; } = "unknown";

    /// <summary>本设备需要生成的 shader 字节码格式，由驱动决定；不同后端的字节码不可互换。</summary>
    public GpuShaderFormat ShaderFormat { get; private set; } = GpuShaderFormat.Invalid;

    public bool Metal => Driver.Equals("metal", StringComparison.OrdinalIgnoreCase);
    public bool Direct3D12 => Driver.Equals("direct3d12", StringComparison.OrdinalIgnoreCase);
    public bool Vulkan => Driver.Equals("vulkan", StringComparison.OrdinalIgnoreCase);

    readonly nint window;
    readonly bool hidden;
    readonly List<Batch> batches = [];
    readonly List<float> vertices = [];
    readonly Dictionary<int, Texture> textures = [];

    nint vertexBuffer, uploadBuffer;

    uint vertexCapacity;

    // 批次必须持有提交时不再变化的状态快照；不能保留可继续写入的 uniform 数组引用。
    // 用 value-type batch + ArrayPool 避免 Vulkan 路径每帧制造成百上千个短命数组/对象，
    // 这些分配在老 CPU 上会放大成周期性的 GC 掉帧。
    /// <summary>提交前的绘制快照。uniform 与 sampler 数组都是 ArrayPool 租借，只在 Submit 的 finally 里归还。</summary>
    internal readonly record struct Batch(
        nint Target, int Width, int Height, Color? Clear, Shader? Shader = null,
        byte[]? VertexUniforms = null, int VertexUniformLength = 0,
        byte[]? FragmentUniforms = null, int FragmentUniformLength = 0,
        GPUTextureSamplerBinding[]? Textures = null, int TextureCount = 0,
        IntRect? Clip = null, BlendFactor Source = BlendFactor.SourceAlpha,
        BlendFactor Destination = BlendFactor.InverseSourceAlpha, uint First = 0, uint Count = 0);

    /// <summary>沿用输入版本的平台选择、双帧在途上限及隐藏窗口 present 策略。</summary>
    public GpuDevice(nint window, bool hidden)
    {
        this.window = window;
        this.hidden = hidden;
        Handle = CreatePlatformGpuDeviceForWindow(window, debugMode: Environment.GetEnvironmentVariable("KUROAKI_GPU_DEBUG") == "1");
        try
        {
            Driver = GetGpuDriverName(Handle);
            ShaderFormat = SelectShaderFormat(Driver, SDL_GetGPUShaderFormats(Handle));

            Require(SDL_SetGPUAllowedFramesInFlight(Handle, 2));
            // 隐藏窗口只用于离屏导出：后端支持时走 Immediate，不让 VSync 把导出速度压到刷新率。
            var mode = hidden && SDL_WindowSupportsGPUPresentMode(Handle, window,
                GpuPresentMode.Immediate) ? GpuPresentMode.Immediate : GpuPresentMode.VSync;
            Require(SDL_SetGPUSwapchainParameters(Handle, window, GpuSwapchainComposition.Sdr, mode));
        }
        catch
        {
            DestroyGpuDeviceForWindow(Handle, window);
            Handle = 0;
            throw;
        }
    }


    /// <summary>按驱动名选出本项目唯一能生成的格式；驱动不提供该格式就直接抛出，不做降级回退。</summary>
    static GpuShaderFormat SelectShaderFormat(string driver, GpuShaderFormat supported)
    {
        GpuShaderFormat required = ShaderFormatFor(driver);

        if (required == GpuShaderFormat.Invalid || (supported & required) == 0)
        {
            throw new PlatformNotSupportedException(
                $"SDL_GPU backend '{driver}' does not expose a shader format Kuroaki can generate. " +
                $"Driver formats: {supported}.");
        }

        return required;
    }

    internal static nint Check(nint handle, string operation) => handle != 0 ? handle : throw new InvalidOperationException(operation + ": " + Error);

    /// <summary>登记辅助纹理单元。主纹理 unit 0 由每次 Draw 单独提供。</summary>
    public void BindTexture(int unit, Texture texture) => textures[unit] = texture;

    /// <summary>unit 0 恒为本次 Draw 传入的主纹理；unit 大于等于 1 必须先由 BindTexture 登记，否则抛出。</summary>
    GPUTextureSamplerBinding ResolveBinding(int unit, Texture primary)
    {
        var texture = unit == 0
            ? primary
            : textures.GetValueOrDefault(unit) ?? throw new InvalidOperationException($"GPU texture unit {unit} is not bound.");
        return new GPUTextureSamplerBinding
        {
            texture = texture.Id,
            sampler = texture.Sampler
        };
    }

    /// <summary>将 shader 逻辑采样器索引转换为当前纹理与 sampler 的池化句柄快照。</summary>
    GPUTextureSamplerBinding[]? RentBindings(Shader shader, Texture primary, out int count)
    {
        count = shader.SamplerUnits.Length;
        if (count == 0)
        {
            return null;
        }
        var result = ArrayPool<GPUTextureSamplerBinding>.Shared.Rent(count);
        for (int i = 0; i < count; i++)
        {
            result[i] = ResolveBinding(shader.SamplerUnits[i], primary);
        }
        return result;
    }

    /// <summary>把可写的 uniform 暂存数组复制进池化数组；长度为 0 时不租借，返回 null。</summary>
    static byte[]? RentBytes(byte[] source, out int length)
    {
        length = source.Length;
        if (length == 0)
        {
            return null;
        }
        var result = ArrayPool<byte>.Shared.Rent(length);
        source.AsSpan().CopyTo(result);
        return result;
    }

    static bool SameRect(IntRect? a, IntRect? b)
    {
        if (!a.HasValue || !b.HasValue)
        {
            return a.HasValue == b.HasValue;
        }
        var x = a.Value;
        var y = b.Value;
        return x.x == y.x && x.y == y.y && x.w == y.w && x.h == y.h;
    }

    /// <summary>比较 uniform 的字节内容而非 shader 引用：暂存数组每次 Draw 都会被重写，引用相同不代表内容相同。</summary>
    static bool SameBytes(byte[] current, byte[]? snapshot, int length) =>
        current.Length == length && (length == 0 || snapshot != null && current.AsSpan().SequenceEqual(snapshot.AsSpan(0, length)));

    /// <summary>按当前绑定重新解析后逐个比较 texture/sampler 句柄；任一单元换了纹理就不能并入上一批次。</summary>
    bool SameBindings(Batch batch, Shader shader, Texture primary)
    {
        if (batch.TextureCount != shader.SamplerUnits.Length)
        {
            return false;
        }
        if (batch.TextureCount == 0)
        {
            return true;
        }
        if (batch.Textures == null)
        {
            return false;
        }
        for (int i = 0; i < batch.TextureCount; i++)
        {
            var binding = ResolveBinding(shader.SamplerUnits[i], primary);
            if (batch.Textures[i].texture != binding.texture || batch.Textures[i].sampler != binding.sampler)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>归还该批次租借的池化数组；归还后数组内容随时可能被别处覆盖，只能在不再读取它之后调用。</summary>
    static void ReleaseBatchStorage(Batch batch)
    {
        if (batch.VertexUniforms != null)
        {
            ArrayPool<byte>.Shared.Return(batch.VertexUniforms);
        }
        if (batch.FragmentUniforms != null)
        {
            ArrayPool<byte>.Shared.Return(batch.FragmentUniforms);
        }
        if (batch.Textures != null)
        {
            ArrayPool<GPUTextureSamplerBinding>.Shared.Return(batch.Textures);
        }
    }

    /// <summary>记录目标/清屏边界，不立即开启原生 pass；上传必须先于所有绘制。</summary>
    internal void Begin(Target? target, int width, int height, Color? clear) => batches.Add(new(target?.Texture.Id ?? 0, width, height, clear));

    /// <summary>复制顶点、uniform 和采样器状态。相邻完全相同的状态直接合并成一个 draw。</summary>
    internal void Draw(Target? target, int width, int height, Shader shader, Texture primary, IntRect? clip, BlendFactor source,
        BlendFactor destination, ReadOnlySpan<float> data)
    {
        uint first = (uint)vertices.Count / 8;
        uint count = (uint)data.Length / 8;
        vertices.AddRange(data);
        nint targetId = target?.Texture.Id ?? 0;

        // Canvas.Flush() 既用于真正的状态边界，也有不少只是逻辑上的提交点。
        // 如果状态完全没有变化，把相邻几何重新并回同一个 draw，Vulkan 的 descriptor/command 开销会小很多。
        if (batches.Count > 0)
        {
            var last = batches[^1];
            if (last.Clear == null && ReferenceEquals(last.Shader, shader) && last.Target == targetId &&
                last.Width == width && last.Height == height && last.Source == source && last.Destination == destination &&
                last.First + last.Count == first && SameRect(last.Clip, clip) &&
                SameBytes(shader.VertexUniforms, last.VertexUniforms, last.VertexUniformLength) &&
                SameBytes(shader.FragmentUniforms, last.FragmentUniforms, last.FragmentUniformLength) &&
                SameBindings(last, shader, primary))
            {
                batches[^1] = last with { Count = last.Count + count };
                return;
            }
        }

        byte[]? vertexUniforms = null, fragmentUniforms = null;
        GPUTextureSamplerBinding[]? bindings = null;
        try
        {
            vertexUniforms = RentBytes(shader.VertexUniforms, out int vertexUniformLength);
            fragmentUniforms = RentBytes(shader.FragmentUniforms, out int fragmentUniformLength);
            bindings = RentBindings(shader, primary, out int textureCount);
            batches.Add(new Batch(
                targetId, width, height, null, shader,
                vertexUniforms, vertexUniformLength, fragmentUniforms, fragmentUniformLength,
                bindings, textureCount, clip, source, destination, first, count));
        }
        catch
        {
            if (vertexUniforms != null) ArrayPool<byte>.Shared.Return(vertexUniforms);
            if (fragmentUniforms != null) ArrayPool<byte>.Shared.Return(fragmentUniforms);
            if (bindings != null) ArrayPool<GPUTextureSamplerBinding>.Shared.Return(bindings);
            throw;
        }
    }

    /// <summary>释放纹理前先提交引用它的批次，再清除辅助单元中的绑定。</summary>
    internal void Forget(Texture texture)
    {
        Submit();
        foreach (int unit in textures.Where(x => ReferenceEquals(x.Value, texture)).Select(x => x.Key).ToArray())
        {
            textures.Remove(unit);
        }
    }

    /// <summary>幂等释放：提交未完成队列，等待 GPU 空闲，最后释放设备和交换链关联。</summary>
    public void Dispose()
    {
        if (Handle == 0)
        {
            return;
        }
        try
        {
            Submit();
        }
        finally
        {
            SDL_WaitForGPUIdle(Handle);
            if (vertexBuffer != 0)
            {
                SDL_ReleaseGPUBuffer(Handle, vertexBuffer);
            }
            if (uploadBuffer != 0)
            {
                SDL_ReleaseGPUTransferBuffer(Handle, uploadBuffer);
            }
            DestroyGpuDeviceForWindow(Handle, window);
            Handle = 0;
        }
    }
}

