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
    readonly Dictionary<(uint, BlendFactor, BlendFactor), nint> pipelines = [];
    // 两个 uniform 暂存数组会被每次 Draw 复用并覆盖；
    // GpuDevice 入队时必须复制字节快照，不能保留这里的引用。
    internal byte[] VertexUniforms { get; }
    internal byte[] FragmentUniforms { get; }
    /// <summary>下标是 fragment 中 sampler 的声明顺序，值是该 sampler 取用的纹理单元号。</summary>
    internal int[] SamplerUnits { get; }
    bool disposed;
    /// <summary>先编译再建立原生对象；第二个 stage 创建失败时释放第一个。</summary>
    public Shader(GpuDevice gpu, string vertex, string fragment)
    {
        this.gpu = gpu;
        this.vertex = ShaderCompiler.Compile(vertex, true, gpu.ShaderFormat);
        this.fragment = ShaderCompiler.Compile(fragment, false, gpu.ShaderFormat);
        VertexUniforms = new byte[this.vertex.Uniforms.Length * 16];
        FragmentUniforms = new byte[this.fragment.Uniforms.Length * 16];
        SamplerUnits = new int[this.fragment.Samplers.Length];
        vertexHandle = Create(this.vertex, true);
        try
        {
            fragmentHandle = Create(this.fragment, false);
        }
        catch
        {
            SDL_ReleaseGPUShader(gpu.Handle, vertexHandle);
            throw;
        }
    }
    nint Create(ShaderCompiler.Compiled compiled, bool vertex)
    {
        byte[] entry = Encoding.UTF8.GetBytes(compiled.Entry + "\0");
        fixed (byte* code = compiled.Code, e = entry)
        {
            return GpuDevice.Check(SDL_CreateGPUShader(gpu.Handle, new GPUShaderCreateInfo
            {
                code = (nint)code,
                code_size = (nuint)compiled.Code.Length,
                entrypoint = (nint)e,
                format = (uint)compiled.Format,
                stage = vertex ? 0u : 1u,
                num_samplers = (uint)compiled.Samplers.Length,
                num_uniform_buffers = compiled.Uniforms.Length > 0 ? 1u : 0u
            }), "Create GPU shader");
        }
    }
    /// <summary>每顶点固定 32 字节，position/UV/color 偏移为 0/8/16；与 Canvas 顶点布局一致。</summary>
    internal nint Pipeline(uint format, BlendFactor source, BlendFactor destination)
    {
        var key = (format, source, destination);
        if (pipelines.TryGetValue(key, out var result))
        {
            return result;
        }
        GPUVertexBufferDescription buffer = new()
        {
            pitch = 32
        };
        GPUVertexAttribute* attributes = stackalloc GPUVertexAttribute[3];
        attributes[0] = new()
        {
            location = 0,
            format = 10,
            offset = 0
        };
        attributes[1] = new()
        {
            location = 1,
            format = 10,
            offset = 8
        };
        attributes[2] = new()
        {
            location = 2,
            format = 12,
            offset = 16
        };
        GPUColorTargetDescription color = new()
        {
            format = format,
            blend_state = new()
            {
                enable_blend = 1,
                src_color_blendfactor = (uint)source,
                dst_color_blendfactor = (uint)destination,
                color_blend_op = 1,
                // alpha 通道固定用 One / InverseSourceAlpha，与调用方传入的颜色因子无关。
                // 配合绘制到透明目标，这让存下来的 RGB 变成预乘 alpha，而 alpha 自身保持非预乘；
                // 整个项目的预乘 alpha 约定就建立在这两行上，不要让它们跟着颜色因子一起变。
                src_alpha_blendfactor = (uint)BlendFactor.One,
                dst_alpha_blendfactor = (uint)BlendFactor.InverseSourceAlpha,
                alpha_blend_op = 1
            }
        };
        var info = new GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vertexHandle,
            fragment_shader = fragmentHandle,
            vertex_input_state = new()
            {
                vertex_buffer_descriptions = (nint)( & buffer),
                num_vertex_buffers = 1,
                vertex_attributes = (nint)attributes,
                num_vertex_attributes = 3
            },
            target_info = new()
            {
                color_target_descriptions = (nint)( & color),
                num_color_targets = 1
            }
        };
        result = GpuDevice.Check(SDL_CreateGPUGraphicsPipeline(gpu.Handle, info), "Create GPU graphics pipeline");
        pipelines[key] = result;
        return result;
    }
    /// <summary>按编译时分配的 16 字节槽位写入。int/bool 存整数位，浮点存 IEEE 754 位。</summary>
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
    public void Float(string n, double v) => Set(n, [(float)v]);
    public void Vec2(string n, double x, double y) => Set(n, [(float)x, (float)y]);
    public void Vec3(string n, double x, double y, double z) => Set(n, [(float)x, (float)y, (float)z]);
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
        foreach (var pipeline in pipelines.Values)
        {
            SDL_ReleaseGPUGraphicsPipeline(gpu.Handle, pipeline);
        }
        SDL_ReleaseGPUShader(gpu.Handle, vertexHandle);
        SDL_ReleaseGPUShader(gpu.Handle, fragmentHandle);
    }
    /// <summary>仅转换 GameMaker GLSL 接口及已存在的零除防护，不重写原滤镜公式。</summary>
    public static string AdaptGml(string source)
    {
        source = Regex.Replace(source, @"(?m)^\s*#version[^\r\n]*", "");
        source = Regex.Replace(source, @"precision\s+\w+\s+\w+\s*;", "");
        source = source.Replace("#define LOWPREC lowp", "#define LOWPREC").Replace("varying ", "in ").Replace("texture2D(",
            "texture(").Replace("gl_FragColor", "fragColor");
        // 看着像魔改，实际必须保留：把这个除数取绝对值并夹到 1e-6 以上，
        // 否则 distort 滤镜会在其中一个后端产生 NaN（另一个后端不会）。
        source = source.Replace("((g_Distort1Amount + g_Distort2Amount) * 0.5)", "max(abs((g_Distort1Amount + g_Distort2Amount) * 0.5), 0.000001)");
        return "#version 450\nout vec4 fragColor;\n" + source;
    }
}
