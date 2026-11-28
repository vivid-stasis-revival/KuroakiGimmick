// 将源 shader 编译为设备资源，并按顶点布局与混合模式创建管线。

using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using KuroakiGimmick.Native;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

/// <summary>顶点/片元程序、uniform 存储和管线缓存。管线键包含目标格式及混合因子。</summary>
public sealed unsafe class Shader : IDisposable
{
    readonly GpuDevice gpu;
    readonly ShaderCompiler.Compiled vertex, fragment;
    readonly nint vertexHandle, fragmentHandle;
    readonly bool ownVertex,ownFragment;
    readonly KenomaGMK.SDL3.SpritePipelineCache pipelines;
    // 两个 uniform 暂存数组会被每次 Draw 复用并覆盖；
    // GpuDevice 入队时必须复制字节快照，不能保留这里的引用。
    internal byte[] VertexUniforms { get; }
    internal byte[] FragmentUniforms { get; }
    /// <summary>下标是 fragment 中 sampler 的声明顺序，值是该 sampler 取用的纹理单元号。</summary>
    internal int[] SamplerUnits { get; }
    bool disposed;
    internal bool Instanced { get; }
    /// <summary>先编译再建立原生对象；第二个 stage 创建失败时释放第一个。</summary>
    /// <param name="gpu">持有渲染资源的 GPU 设备。</param>
    /// <param name="vertex">顶点 shader 源码。</param>
    /// <param name="fragment">片元 shader 源码。</param>
    /// <param name="instanced">是否使用实例化顶点布局。</param>
    public Shader(GpuDevice gpu, string vertex, string fragment, bool instanced = false, bool builtIn = true)
    {
        this.gpu = gpu;
        Instanced = instanced;
        (this.vertex,vertexHandle,ownVertex) = gpu.Stage(vertex,true,builtIn);
        try
        {
            (this.fragment,fragmentHandle,ownFragment) = gpu.Stage(fragment,false,builtIn);
            VertexUniforms = new byte[this.vertex.Uniforms.Length * 16];
            FragmentUniforms = new byte[this.fragment.Uniforms.Length * 16];
            SamplerUnits = new int[this.fragment.Samplers.Length];
            pipelines = new(gpu.Handle, vertexHandle, fragmentHandle, Instanced, depthClip: false,
                ShaderCompiler.SharedLibrary, builtIn: builtIn, startupComplete: () => gpu.ShaderStartupComplete);
            if (ShaderCompiler.SharedLibrary.Options.Mode == KenomaGMK.SDL3.ShaderCompilationMode.Startup &&
                ShaderCompiler.SharedLibrary.Options.PrewarmPipelines)
                Pipeline(GpuDevice.Rgba8,BlendFactor.SourceAlpha,BlendFactor.InverseSourceAlpha);
        }
        catch
        {
            pipelines?.Dispose();
            if (ownFragment && fragmentHandle != 0) SDL_ReleaseGPUShader(gpu.Handle,fragmentHandle);
            if (ownVertex) SDL_ReleaseGPUShader(gpu.Handle,vertexHandle);
            throw;
        }
    }
    internal static nint Create(GpuDevice gpu, ShaderCompiler.Compiled compiled, bool vertex, bool builtIn = true)
    {
        byte[] entry = Encoding.UTF8.GetBytes(compiled.Entry + "\0");
        // SAFETY: 创建描述按字节数组的实际长度传递 shader，入口名以 UTF-8 空字符结尾，两者在调用期间保持固定。
        fixed (byte* code = compiled.Code, e = entry)
        {
            var shader = GpuDevice.Check(SDL_CreateGPUShader(gpu.Handle, new GPUShaderCreateInfo
            {
                code = (nint)code,
                code_size = (nuint)compiled.Code.Length,
                entrypoint = (nint)e,
                format = (uint)compiled.Format,
                stage = vertex ? 0u : 1u,
                num_samplers = (uint)compiled.Samplers.Length,
                num_uniform_buffers = compiled.Uniforms.Length > 0 ? 1u : 0u
            }), "Create GPU shader");
            ShaderCompiler.SharedLibrary.RecordGpuShaderCreation(
                ShaderCompiler.SharedLibrary.Options.Mode == KenomaGMK.SDL3.ShaderCompilationMode.OnDemand,
                builtIn, unexpected: gpu.ShaderStartupComplete);
            return shader;
        }
    }
    /// <summary>每顶点固定 32 字节，position/UV/color 偏移为 0/8/16；与 Canvas 顶点布局一致。</summary>
    internal nint Pipeline(uint format, BlendFactor source, BlendFactor destination)
    {
        return pipelines.Prepare(new(format, Instanced ? 64u : 32u,
            (KenomaGMK.Graphics.BlendFactor)source, (KenomaGMK.Graphics.BlendFactor)destination));
    }
    internal void Prewarm(uint format, params (BlendFactor Source, BlendFactor Destination)[] descriptions)
    {
        if (ShaderCompiler.SharedLibrary.Options.Mode != KenomaGMK.SDL3.ShaderCompilationMode.Startup ||
            !ShaderCompiler.SharedLibrary.Options.PrewarmPipelines) return;
        foreach (var (source, destination) in descriptions) Pipeline(format, source, destination);
    }

    internal void Prewarm(params (BlendFactor Source, BlendFactor Destination)[] descriptions) =>
        Prewarm(GpuDevice.Rgba8, descriptions);

    static void WriteStage(ShaderCompiler.Compiled stage, byte[] bytes, string name, ReadOnlySpan<float> values)
    {
        var uniform = Array.Find(stage.Uniforms, u => u.Name == name);
        if (uniform == null)
        {
            return;
        }
        // 原版 shader 会故意省略用不到的控制项，所以找不到同名 uniform 时静默忽略，这是设计如此。
        for (int i = 0; i < values.Length; i++)
        {
            int bits = uniform.Type is "int" or "bool" ? (int)values[i] : BitConverter.SingleToInt32Bits(values[i]);
            BitConverter.TryWriteBytes(bytes.AsSpan(uniform.Offset + i * 4, 4), bits);
        }
    }
    /// <summary>同名 uniform 同时写入 vertex 与 fragment 两个阶段；某阶段没有该名字就跳过它。</summary>
    void Set(string name, ReadOnlySpan<float> values)
    {
        WriteStage(vertex, VertexUniforms, name, values);
        WriteStage(fragment, FragmentUniforms, name, values);
    }
    /// <summary>同名 sampler 写入纹理单元索引；其它整型 uniform 写入两阶段参数块。</summary>
    /// <param name="name">uniform 名称。</param>
    /// <param name="value">要写入的整数。</param>
    public void Int(string name, int value)
    {
        int sampler = Array.IndexOf(fragment.Samplers, name);
        if (sampler >= 0)
        {
            SamplerUnits[sampler] = value;
        }
        else
        {
            Set(name, [value]);
        }
    }
    /// <summary>将单个数值转换为 float 并写入指定 uniform。</summary>
    /// <param name="n">uniform 名称。</param>
    /// <param name="v">要写入的标量。</param>
    public void Float(string n, double v) => Set(n, [(float)v]);
    /// <summary>将两个数值分量写入指定 uniform。</summary>
    /// <param name="n">uniform 名称。</param>
    /// <param name="x">第一分量。</param>
    /// <param name="y">第二分量。</param>
    public void Vec2(string n, double x, double y) => Set(n, [(float)x, (float)y]);
    /// <summary>将三个数值分量写入指定 uniform。</summary>
    /// <param name="n">uniform 名称。</param>
    /// <param name="x">第一分量。</param>
    /// <param name="y">第二分量。</param>
    /// <param name="z">第三分量。</param>
    public void Vec3(string n, double x, double y, double z) => Set(n, [(float)x, (float)y, (float)z]);
    /// <summary>按 RGBA 顺序将颜色分量写入指定 uniform。</summary>
    /// <param name="n">uniform 名称。</param>
    /// <param name="c">RGBA 四个分量。</param>
    public void Vec4(string n, Color c) => Set(n, [c.R, c.G, c.B, c.A]);
    /// <summary>先提交仍在引用本 shader 的批次，再释放缓存的全部管线与两个 stage；重复调用不再释放。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        gpu.Submit();
        disposed = true;
        pipelines.Dispose();
        if (ownVertex) SDL_ReleaseGPUShader(gpu.Handle, vertexHandle);
        if (ownFragment) SDL_ReleaseGPUShader(gpu.Handle, fragmentHandle);
    }
    /// <summary>仅转换 GameMaker GLSL 接口及已存在的零除防护，不重写原滤镜公式。</summary>
    /// <param name="source">来源 GML shader 文本。</param>
    /// <returns>适配渲染后端的 shader 源码。</returns>
    public static string AdaptGml(string source) => KenomaGMK.SDL3.GameMakerShader.AdaptGml(source);
}
