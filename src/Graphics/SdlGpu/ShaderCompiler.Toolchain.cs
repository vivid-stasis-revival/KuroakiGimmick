using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using KuroakiGimmick.Native;
using static KuroakiGimmick.Native.ShaderTools;

namespace KuroakiGimmick.Graphics;

/// <summary>shaderc/SPIRV-Cross/D3DCompiler 的结果读取、错误传递和原生资源释放。</summary>
internal static unsafe partial class ShaderCompiler
{
    /// <summary>同步调用 shaderc；无论成功失败都释放原生 result，失败时把 shaderc 的原始错误信息带出。</summary>
    static byte[] RunShaderc(nint compiler, string source, bool vertex, bool preprocess, nint options = 0)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(source), file = "gimmick.glsl\0"u8.ToArray(), entry = "main\0"u8.ToArray();
        fixed (byte* p = bytes, f = file, e = entry)
        {
            nint result = preprocess ? shaderc_compile_into_preprocessed_text(compiler, p, (nuint)bytes.Length, vertex ? 0 : 1, f, e,
                options) : shaderc_compile_into_spv(compiler, p, (nuint)bytes.Length, vertex ? 0 : 1, f, e, options);
            if (result == 0)
            {
                throw new InvalidOperationException("shaderc returned no result.");
            }
            try
            {
                if (shaderc_result_get_compilation_status(result) != 0)
                {
                    throw new InvalidOperationException("GPU shader compilation failed: " + Marshal.PtrToStringUTF8(shaderc_result_get_error_message(result)));
                }
                return new ReadOnlySpan<byte>((void*)shaderc_result_get_bytes(result), checked((int)shaderc_result_get_length(result))).ToArray();
            }
            finally
            {
                shaderc_result_release(result);
            }
        }
    }

    // HLSL 路径的可移植语法/资源校验。真正的 DXBC 编译与 D3D12 设备校验
    // 仍然需要 Windows 和 D3DCompiler_47。
    internal static void ValidateHlsl(string source, bool vertex)
    {
        var hlsl = Translate(source, vertex, Sdl.GpuShaderFormat.Dxbc);
        nint compiler = shaderc_compiler_initialize(), options = shaderc_compile_options_initialize();
        try
        {
            if (compiler == 0 || options == 0)
            {
                throw new InvalidOperationException("Cannot initialize HLSL validation.");
            }
            shaderc_compile_options_set_source_language(options, 1);
            RunShaderc(compiler, hlsl.NativeSource, vertex, false, options);
        }
        finally
        {
            if (options != 0)
            {
                shaderc_compile_options_release(options);
            }
            if (compiler != 0)
            {
                shaderc_compiler_release(compiler);
            }
        }
    }

    /// <summary>编译到 vs_5_1 / ps_5_1；flags 为 D3DCOMPILE_OPTIMIZATION_LEVEL3（1 左移 15 位），返回前释放代码与错误两个 blob。</summary>
    static byte[] CompileDxbc(string source, bool vertex)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(source), entry = "main\0"u8.ToArray(),
            target = Encoding.ASCII.GetBytes(vertex ? "vs_5_1\0" : "ps_5_1\0");
        fixed (byte* p = bytes, e = entry, t = target)
        {
            int result = D3DCompile(p, (nuint)bytes.Length, null, 0, 0, e, t, 1u << 15, 0, out nint code, out nint errors);
            try
            {
                if (result < 0)
                {
                    throw new InvalidOperationException("D3D shader compilation failed: " + (errors == 0 ? result.ToString("X") : Encoding.UTF8.GetString(BlobBytes(errors))));
                }
                return BlobBytes(code);
            }
            finally
            {
                ReleaseBlob(code);
                ReleaseBlob(errors);
            }
        }
    }
}

