using System.Runtime.InteropServices;

namespace KuroakiGimmick.Native;

// SDL3 GPU 的 ABI 结构布局。字段顺序与单字节 bool 与 SDL_gpu.h 完全一致。
// https://github.com/libsdl-org/SDL/blob/release-3.4.16/include/SDL3/SDL_gpu.h
//
// 本文件的字段顺序、字段类型和所有 padding* 成员都是 ABI 的一部分，不是排版。
// 不要重排字段、不要删除看起来"没用"的 padding、不要把 byte 布尔改成 bool、
// 不要合并同类型字段 —— 任何一项都会让结构体和原生侧错位，表现为随机的渲染错误或崩溃。
// 需要新增字段时只能照 SDL 头文件在对应位置补，并同步确认 SDL 版本。
public static unsafe partial class Sdl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUViewport
    {
        public float x;
        public float y;
        public float w;
        public float h;
        public float min_depth;
        public float max_depth;
    }

    /// <summary>pixels_per_row / rows_per_layer 的单位是像素与行，不是字节；填 0 表示按 region 紧密排列。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUTextureTransferInfo
    {
        public nint transfer_buffer;
        public uint offset;
        public uint pixels_per_row;
        public uint rows_per_layer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUTransferBufferLocation
    {
        public nint transfer_buffer;
        public uint offset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUTextureRegion
    {
        public nint texture;
        public uint mip_level;
        public uint layer;
        public uint x;
        public uint y;
        public uint z;
        public uint w;
        public uint h;
        public uint d;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUBufferRegion
    {
        public nint buffer;
        public uint offset;
        public uint size;
    }

    /// <summary>
    /// enable_* 是单字节 bool；后面的 padding1/padding2 把结构补齐到 props 的 4 字节对齐位置，
    /// 两者都属于 ABI，删掉会让 props 落到错误的偏移上。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUSamplerCreateInfo
    {
        public uint min_filter;
        public uint mag_filter;
        public uint mipmap_mode;
        public uint address_mode_u;
        public uint address_mode_v;
        public uint address_mode_w;
        public float mip_lod_bias;
        public float max_anisotropy;
        public uint compare_op;
        public float min_lod;
        public float max_lod;
        public byte enable_anisotropy;
        public byte enable_compare;
        public byte padding1;
        public byte padding2;
        public uint props;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUVertexBufferDescription
    {
        public uint slot;
        public uint pitch;
        public uint input_rate;
        public uint instance_step_rate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUVertexAttribute
    {
        public uint location;
        public uint buffer_slot;
        public uint format;
        public uint offset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUVertexInputState
    {
        public nint vertex_buffer_descriptions;
        public uint num_vertex_buffers;
        public nint vertex_attributes;
        public uint num_vertex_attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUStencilOpState
    {
        public uint fail_op;
        public uint pass_op;
        public uint depth_fail_op;
        public uint compare_op;
    }

    /// <summary>
    /// 本项目按预乘 alpha 配置混合因子；color_write_mask 只有 enable_color_write_mask 为 1 时才生效。
    /// 末尾两个 padding 是 ABI 的一部分，不要删。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUColorTargetBlendState
    {
        public uint src_color_blendfactor;
        public uint dst_color_blendfactor;
        public uint color_blend_op;
        public uint src_alpha_blendfactor;
        public uint dst_alpha_blendfactor;
        public uint alpha_blend_op;
        public byte color_write_mask;
        public byte enable_blend;
        public byte enable_color_write_mask;
        public byte padding1;
        public byte padding2;
    }

    /// <summary>
    /// code/code_size 指向的 shader 字节码与 entrypoint 字符串必须在调用期间保持固定（pin 住）。
    /// num_samplers 等四个资源数量要与 shader 里实际声明的一致，多报少报都会导致绑定错位。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUShaderCreateInfo
    {
        public nuint code_size;
        public nint code;
        public nint entrypoint;
        public uint format;
        public uint stage;
        public uint num_samplers;
        public uint num_storage_textures;
        public uint num_storage_buffers;
        public uint num_uniform_buffers;
        public uint props;
    }

    /// <summary>num_levels 是 mip 层数（1 表示不带 mipmap）；layer_count_or_depth 按 type 解释为层数或深度，二者共用同一字段。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUTextureCreateInfo
    {
        public uint type;
        public uint format;
        public uint usage;
        public uint width;
        public uint height;
        public uint layer_count_or_depth;
        public uint num_levels;
        public uint sample_count;
        public uint props;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUBufferCreateInfo
    {
        public uint usage;
        public uint size;
        public uint props;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUTransferBufferCreateInfo
    {
        public uint usage;
        public uint size;
        public uint props;
    }

    /// <summary>末尾 padding1/padding2 是 ABI 占位，不要删。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPURasterizerState
    {
        public uint fill_mode;
        public uint cull_mode;
        public uint front_face;
        public float depth_bias_constant_factor;
        public float depth_bias_clamp;
        public float depth_bias_slope_factor;
        public byte enable_depth_bias;
        public byte enable_depth_clip;
        public byte padding1;
        public byte padding2;
    }

    /// <summary>
    /// 注意 padding 是 padding2/padding3 而不是从 1 开始编号 —— 这是照抄 SDL_gpu.h 的原名，
    /// 不是笔误，不要"顺手"重编号或删掉。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUMultisampleState
    {
        public uint sample_count;
        public uint sample_mask;
        public byte enable_mask;
        public byte enable_alpha_to_coverage;
        public byte padding2;
        public byte padding3;
    }

    /// <summary>back_stencil_state 在 front_stencil_state 之前，这个顺序来自 SDL_gpu.h，写反不会报错只会画错。末尾三个 padding 不要删。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUDepthStencilState
    {
        public uint compare_op;
        public GPUStencilOpState back_stencil_state;
        public GPUStencilOpState front_stencil_state;
        public byte compare_mask;
        public byte write_mask;
        public byte enable_depth_test;
        public byte enable_depth_write;
        public byte enable_stencil_test;
        public byte padding1;
        public byte padding2;
        public byte padding3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUColorTargetDescription
    {
        public uint format;
        public GPUColorTargetBlendState blend_state;
    }

    /// <summary>has_depth_stencil_target 是单字节 bool；后面三个 padding 把结构补到 4 字节对齐，属于 ABI。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUGraphicsPipelineTargetInfo
    {
        public nint color_target_descriptions;
        public uint num_color_targets;
        public uint depth_stencil_format;
        public byte has_depth_stencil_target;
        public byte padding1;
        public byte padding2;
        public byte padding3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUGraphicsPipelineCreateInfo
    {
        public nint vertex_shader;
        public nint fragment_shader;
        public GPUVertexInputState vertex_input_state;
        public uint primitive_type;
        public GPURasterizerState rasterizer_state;
        public GPUMultisampleState multisample_state;
        public GPUDepthStencilState depth_stencil_state;
        public GPUGraphicsPipelineTargetInfo target_info;
        public uint props;
    }

    /// <summary>
    /// clear_color 只在 load_op 为 Clear 时使用；cycle 让 SDL 在资源仍被占用时自动换一份内部缓冲。
    /// 末尾 padding1/padding2 是 ABI 占位，不要删。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUColorTargetInfo
    {
        public nint texture;
        public uint mip_level;
        public uint layer_or_depth_plane;
        public FColor clear_color;
        public uint load_op;
        public uint store_op;
        public nint resolve_texture;
        public uint resolve_mip_level;
        public uint resolve_layer;
        public byte cycle;
        public byte cycle_resolve_texture;
        public byte padding1;
        public byte padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUBufferBinding
    {
        public nint buffer;
        public uint offset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUTextureSamplerBinding
    {
        public nint texture;
        public nint sampler;
    }

    /// <summary>SDL 的浮点颜色，分量为线性 0-1，不是 0-255。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FColor
    {
        public float r, g, b, a;
    }

    /// <summary>对应 SDL_Rect：x/y 为左上角，w/h 为尺寸；单位是物理像素。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct IntRect
    {
        public int x, y, w, h;
    }
}

