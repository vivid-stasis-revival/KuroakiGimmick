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
    // 每项固定占 16 字节。不要在 CPU 侧独立“压紧”布局，否则会与生成的 offset 声明不一致。
    internal sealed record Uniform(string Name, string Type, int Offset);

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
        if (format is not (GpuShaderFormat.SpirV or GpuShaderFormat.Dxbc or GpuShaderFormat.Msl))
        {
            throw new PlatformNotSupportedException($"Unsupported Kuroaki shader format: {format}");
        }

        var key = (source, vertex, format);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
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
    }
}
