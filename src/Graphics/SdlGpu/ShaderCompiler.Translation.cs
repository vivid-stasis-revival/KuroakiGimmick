using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using KuroakiGimmick.Native;
using static KuroakiGimmick.Native.Sdl;
using static KuroakiGimmick.Native.ShaderTools;

namespace KuroakiGimmick.Graphics;

/// <summary>保留 GLSL 运算表达式，只改写 uniform、sampler 和输入输出声明。</summary>
internal static unsafe partial class ShaderCompiler
{
    // 为既有 HLSL/MSL 校验调用方保留的兼容重载。
    internal static Translation Translate(string source, bool vertex, bool metal) =>
        Translate(source, vertex, metal ? GpuShaderFormat.Msl : GpuShaderFormat.Dxbc);

    /// <summary>
    /// 先让 shaderc 展开宏，再枚举 uniform。采样器使用 set 0/2，参数块使用 set 1/3。
    /// Vulkan 直接保留最终 SPIR-V；Metal/D3D12 再经 SPIRV-Cross 转译。
    /// </summary>
    internal static Translation Translate(string source, bool vertex, GpuShaderFormat format)
    {
        if (format is not (GpuShaderFormat.SpirV or GpuShaderFormat.Dxbc or GpuShaderFormat.Msl))
        {
            throw new PlatformNotSupportedException($"Unsupported Kuroaki shader translation format: {format}");
        }

        nint compiler = shaderc_compiler_initialize();
        if (compiler == 0)
        {
            throw new InvalidOperationException("Cannot initialize shaderc.");
        }

        try
        {
            source = Regex.Replace(source, @"(?m)^\s*#version[^\r\n]*", "");
            source = Encoding.UTF8.GetString(RunShaderc(compiler, "#version 450\n" + source, vertex, true));
            source = Regex.Replace(source, @"(?m)^\s*#(?:version|line)[^\r\n]*", "");
            // 先删掉注释再枚举 uniform，被注释掉的声明不应进入参数块。
            source = Regex.Replace(source, @"/\*.*?\*/|//[^\r\n]*", "", RegexOptions.Singleline);

            var uniforms = new List<Uniform>();
            var samplers = new List<string>();
            var declarations = new StringBuilder();

            source = Regex.Replace(source, @"\buniform\s+(\w+)\s+([^;]+);", m =>
            {
                string type = m.Groups[1].Value;
                foreach (string item in m.Groups[2].Value.Split(','))
                {
                    string name = item.Trim();
                    if (!Regex.IsMatch(name, @"^\w+$"))
                    {
                        throw new InvalidOperationException("Unsupported GPU uniform declaration: " + m.Value);
                    }

                    if (type == "sampler2D")
                    {
                        samplers.Add(name);
                    }
                    else
                    {
                        if (type is not ("float" or "int" or "bool" or "vec2" or "vec3" or "vec4"))
                        {
                            throw new InvalidOperationException("Unsupported GPU uniform type: " + type);
                        }

                        uniforms.Add(new(name, type, uniforms.Count * 16));
                    }
                }

                return "";
            });

            // 显式固定 location，保证各后端都对得上共享顶点布局：v_vColour 固定 1，其余固定 0。
            source = Regex.Replace(
                source,
                @"(?<!\w)(in|out)\s+(vec[234])\s+(v_vTexcoord|v_vColour|fragColor)\s*;",
                m => $"layout(location={(m.Groups[3].Value == "v_vColour" ? 1 : 0)}) {m.Value}");

            for (int i = 0; i < samplers.Count; i++)
            {
                declarations.AppendLine(
                    $"layout(set={(vertex ? 0 : 2)}, binding={i}) uniform sampler2D {samplers[i]};");
            }

            if (uniforms.Count > 0)
            {
                declarations.AppendLine(
                    $"layout(std140, set={(vertex ? 1 : 3)}, binding=0) uniform Parameters {{");

                foreach (var u in uniforms)
                {
                    declarations.AppendLine($"layout(offset={u.Offset}) {u.Type} {u.Name};");
                }

                declarations.AppendLine("};");
            }

            string portableSource = "#version 450\n" + declarations + source;

            // Vulkan 直接使用这份 SPIR-V。shaderc 的默认设置相当于关闭优化，
            // 拿来做转译没问题，但老 Vulkan GPU 跑大块 GameMaker 片元特效时代价太高。
            // 因此只优化最终的 Vulkan 字节码；Metal/D3D12 仍走原来的路径，
            // 产物与旧版本逐位一致。
            nint spirvOptions = 0;
            try
            {
                if (format == GpuShaderFormat.SpirV)
                {
                    spirvOptions = shaderc_compile_options_initialize();
                    if (spirvOptions == 0)
                    {
                        throw new InvalidOperationException("Cannot initialize Vulkan shader optimization options.");
                    }
                    // shaderc_optimization_level_performance = 2
                    shaderc_compile_options_set_optimization_level(spirvOptions, 2);
                }

                byte[] spirv = RunShaderc(compiler, portableSource, vertex, false, spirvOptions);

                if (format == GpuShaderFormat.SpirV)
                {
                    return new Translation(
                        "main",
                        uniforms.ToArray(),
                        samplers.ToArray(),
                        portableSource,
                        spirv);
                }

                bool metal = format == GpuShaderFormat.Msl;
                var (native, entry) = CrossCompile(
                    spirv,
                    vertex,
                    metal,
                    samplers.Count,
                    uniforms.Count > 0);

                return new Translation(
                    entry,
                    uniforms.ToArray(),
                    samplers.ToArray(),
                    native,
                    spirv);
            }
            finally
            {
                if (spirvOptions != 0)
                {
                    shaderc_compile_options_release(spirvOptions);
                }
            }
        }
        finally
        {
            shaderc_compiler_release(compiler);
        }
    }

    /// <summary>SPIRV-Cross 保留 shader 运算；返回前复制原生字符串，因为 finally 会销毁 context。</summary>
    static (string Source, string Entry) CrossCompile(byte[] spirv, bool vertex, bool metal, int samplers, bool uniforms)
    {
        if (spvc_context_create(out nint context) != 0)
        {
            throw new InvalidOperationException("Cannot initialize SPIRV-Cross.");
        }

        void Check(int result)
        {
            if (result != 0)
            {
                throw new InvalidOperationException(
                    "SPIRV-Cross: " + Marshal.PtrToStringUTF8(spvc_context_get_last_error_string(context)));
            }
        }

        try
        {
            fixed (byte* bytes = spirv)
            {
                Check(spvc_context_parse_spirv(context, (uint*)bytes, (nuint)spirv.Length / 4, out nint ir));
                // SPIRV-Cross 后端编号：3 = MSL，2 = HLSL。
                Check(spvc_context_create_compiler(context, metal ? 3 : 2, ir, 1, out nint compiler));
                Check(spvc_compiler_create_compiler_options(compiler, out nint options));
                // 选项键与取值：MSL_VERSION 定为 2.1.0（20100），HLSL_SHADER_MODEL 定为 5.1（51）。
                Check(spvc_compiler_options_set_uint(
                    options,
                    metal ? 0x8000011u : 0x400000du,
                    metal ? 20100u : 51u));
                Check(spvc_compiler_install_compiler_options(compiler, options));

                if (metal)
                {
                    // SPIR-V 执行模型：0 = Vertex，4 = Fragment。
                    uint stage = vertex ? 0u : 4u;
                    for (uint i = 0; i < samplers; i++)
                    {
                        Check(spvc_compiler_msl_add_resource_binding(compiler, new MslBinding
                        {
                            stage = stage,
                            set = vertex ? 0u : 2u,
                            binding = i,
                            texture = i,
                            sampler = i
                        }));
                    }

                    if (uniforms)
                    {
                        Check(spvc_compiler_msl_add_resource_binding(compiler, new MslBinding
                        {
                            stage = stage,
                            set = vertex ? 1u : 3u,
                            buffer = 0
                        }));
                    }
                }

                Check(spvc_compiler_compile(compiler, out nint text));
                return (
                    Marshal.PtrToStringUTF8(text)!,
                    metal
                        ? Marshal.PtrToStringUTF8(
                            spvc_compiler_get_cleansed_entry_point_name(
                                compiler,
                                "main",
                                vertex ? 0u : 4u))!
                        : "main");
            }
        }
        finally
        {
            spvc_context_destroy(context);
        }
    }
}
