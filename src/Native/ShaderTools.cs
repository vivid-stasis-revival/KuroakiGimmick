using System.Runtime.InteropServices;

namespace KuroakiGimmick.Native;

// 来自 shaderc.h 与 spirv_cross_c.h 的最小 C ABI 面。原生二进制是固定版本的 NuGet runtime 资产，
// 覆盖 macOS 与 Windows 的 arm64/x64。
// 本文件只做签名镜像：参数顺序、指针类型、调用约定都照抄头文件，不要为了"好用"改签名。
// 注意 D3DCompile 用的是 StdCall，与上面 shaderc/SPIRV-Cross 的 Cdecl 不同，这是各自 ABI 的要求。
internal static unsafe class ShaderTools
{
    const string Shaderc = "shaderc_shared", Cross = "spirv-cross";
    [DllImport(Shaderc, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint shaderc_compiler_initialize();
    [DllImport(Shaderc, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint shaderc_compile_options_initialize();
    [DllImport(Shaderc, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void shaderc_compile_options_release(nint options);
    [DllImport(Shaderc, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void shaderc_compile_options_set_source_language(nint options, int language);
    [DllImport(Shaderc, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void shaderc_compile_options_set_optimization_level(nint options, int level);
    [DllImport(Shaderc, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void shaderc_compiler_release(nint compiler);
    [DllImport(Shaderc, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint shaderc_compile_into_spv(nint compiler, byte* source, nuint size, int kind, byte* file, byte* entry, nint options);
    [DllImport(Shaderc, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint shaderc_compile_into_preprocessed_text(nint compiler, byte* source, nuint size, int kind, byte* file,
        byte* entry, nint options);
    [DllImport(Shaderc, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int shaderc_result_get_compilation_status(nint result);
    [DllImport(Shaderc, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint shaderc_result_get_error_message(nint result);
    [DllImport(Shaderc, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint shaderc_result_get_length(nint result);
    [DllImport(Shaderc, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint shaderc_result_get_bytes(nint result);
    [DllImport(Shaderc, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void shaderc_result_release(nint result);
    [DllImport(Cross, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int spvc_context_create(out nint context);
    [DllImport(Cross, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void spvc_context_destroy(nint context);
    [DllImport(Cross, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint spvc_context_get_last_error_string(nint context);
    [DllImport(Cross, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int spvc_context_parse_spirv(nint context, uint* words, nuint count, out nint ir);
    [DllImport(Cross, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int spvc_context_create_compiler(nint context, int backend, nint ir, int capture, out nint compiler);
    [DllImport(Cross, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int spvc_compiler_create_compiler_options(nint compiler, out nint options);
    [DllImport(Cross, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int spvc_compiler_options_set_uint(nint options, uint option, uint value);
    [DllImport(Cross, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int spvc_compiler_install_compiler_options(nint compiler, nint options);
    [DllImport(Cross, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int spvc_compiler_compile(nint compiler, out nint source);
    [DllImport(Cross, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint spvc_compiler_get_cleansed_entry_point_name(nint compiler, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        uint stage);
    /// <summary>对应 spvc_msl_resource_binding：字段顺序属于 ABI，不要重排。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MslBinding
    {
        public uint stage, set, binding, buffer, texture, sampler;
    }
    [DllImport(Cross, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int spvc_compiler_msl_add_resource_binding(nint compiler, in MslBinding binding);
    [DllImport("d3dcompiler_47", CallingConvention = CallingConvention.StdCall)]
    internal static extern int D3DCompile(byte* data, nuint size, byte* name, nint defines, nint include, byte* entry, byte* target, uint flags,
        uint flags2, out nint code, out nint errors);
    /// <summary>
    /// 手工走 ID3DBlob 的 COM vtable，因为这里没有引入任何 ID3DBlob 互操作类型。
    /// 槽位来自 IUnknown + ID3DBlob 的固定布局：0=QueryInterface，1=AddRef，2=Release，
    /// 3=GetBufferPointer，4=GetBufferSize。这些数字不是随手写的，改动等于读错函数指针并立刻崩溃。
    /// 返回的是拷贝，blob 仍需调用方用 ReleaseBlob 释放。
    /// </summary>
    internal static byte[] BlobBytes(nint blob)
    {
        var table = *(nint**)blob;
        var pointer = ((delegate* unmanaged[Stdcall]<nint, nint>) table[3])(blob);
        var size = ((delegate* unmanaged[Stdcall]<nint, nuint>) table[4])(blob);
        return new ReadOnlySpan<byte>((void*)pointer, checked((int)size)).ToArray();
    }
    /// <summary>同上的 vtable 约定：槽位 2 是 IUnknown::Release。空句柄直接跳过，不做二次释放。</summary>
    internal static void ReleaseBlob(nint blob)
    {
        if (blob != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)blob)[2])(blob);
        }
    }
}
