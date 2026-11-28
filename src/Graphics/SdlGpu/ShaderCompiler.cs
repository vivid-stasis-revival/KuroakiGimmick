using Uniform = KenomaGMK.SDL3.ShaderUniform;
using System.Text;
using KuroakiGimmick.Native;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

// 保留 GameMaker GLSL 的原始表达式，只改写资源与接口声明。
// 公共前端一律先产出 SPIR-V：Vulkan 直接使用；Metal 转译成 MSL；
// D3D12 先转译成 HLSL，再编译成 DXBC。
/// <summary>着色器编译缓存。缓存键包含源码、阶段和最终 SDL_GPU shader 格式，不跨后端复用字节码。</summary>
internal static unsafe partial class ShaderCompiler
{
    /// <summary>某个后端的最终产物：Code 依格式为 SPIR-V 字节码、带结尾 NUL 的 MSL 文本或 DXBC；Entry 在 Metal 上不一定是 main。</summary>
    internal sealed record Compiled(
        byte[] Code,
        string Entry,
        Uniform[] Uniforms,
        string[] Samplers,
        string NativeSource,
        GpuShaderFormat Format);

    /// <summary>翻译的中间结果：SpirV 始终有效，NativeSource 按目标后端为可移植 GLSL、MSL 或 HLSL。</summary>
    internal sealed record Translation(
        string Entry,
        Uniform[] Uniforms,
        string[] Samplers,
        string NativeSource,
        byte[] SpirV);

    static readonly Dictionary<(string Source, bool Vertex, GpuShaderFormat Format), Compiled> cache = [];

    // 为既有内部调用方与测试保留的兼容重载。
    internal static Compiled Compile(string source, bool vertex, bool metal) =>
        Compile(source, vertex, metal ? GpuShaderFormat.Msl : GpuShaderFormat.Dxbc);

    /// <summary>
    /// Vulkan 直接返回 shaderc 产出的 SPIR-V；Metal 返回带结尾 NUL 的 MSL 文本；D3D12 返回 DXBC。
    /// </summary>
    internal static Compiled Compile(string source, bool vertex, GpuShaderFormat format)
    {
        lock (cache) return CompileCached(source, vertex, format);
    }

    static Compiled CompileCached(string source, bool vertex, GpuShaderFormat format)
    {
        if (format is not (GpuShaderFormat.Dxil or GpuShaderFormat.SpirV or GpuShaderFormat.Dxbc or GpuShaderFormat.Msl))
        {
            throw new PlatformNotSupportedException($"Unsupported Kuroaki shader format: {format}");
        }

        var key = (source, vertex, format);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

#if KG_MOBILE
        var packed = MobileShaderPack.Load(source, vertex, format);
        cache[key] = packed;
        return packed;
#elif KG_SWITCH
        if (format != GpuShaderFormat.SpirV)
            throw new PlatformNotSupportedException("Switch requires SPIR-V.");
        var packed = SwitchShaderPack.Load(source, vertex);
        cache[key] = packed;
        return packed;
#else
        if (format == GpuShaderFormat.Dxil || format == GpuShaderFormat.Msl && !TargetIosMsl)
        {
            var portable = Translate(source,vertex,GpuShaderFormat.SpirV);
            var output = SharedLibrary.Prepare(portable.SpirV,vertex?0u:1u,(uint)format,string.Join(",",portable.Samplers)+"/"+portable.Uniforms.Length);
            bool msl = format == GpuShaderFormat.Msl;
            return cache[key]=new(output.ToArray(),msl?"main0":"main",portable.Uniforms,portable.Samplers,
                msl?Encoding.UTF8.GetString(output.Span[..^1]):"",format);
        }
        var translated = Translate(source, vertex, format);
        byte[] code = format switch
        {
            GpuShaderFormat.SpirV => translated.SpirV,
            GpuShaderFormat.Msl => Encoding.UTF8.GetBytes(translated.NativeSource + "\0"),
            GpuShaderFormat.Dxbc => CompileDxbc(translated.NativeSource, vertex),
            _ => throw new InvalidOperationException($"Unsupported Kuroaki shader format: {format}")
        };

        var result = new Compiled(
            code,
            translated.Entry,
            translated.Uniforms,
            translated.Samplers,
            translated.NativeSource,
            format);

        cache[key] = result;
        return result;
#endif
    }
}
