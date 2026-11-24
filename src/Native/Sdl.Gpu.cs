// 定义与 SDL_gpu.h 对应的托管结构布局，供 GPU 资源创建与命令记录使用。

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
    /// <summary>指定光栅化视口及深度范围。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUViewport
    {
        /// <summary>视口左边界。</summary>
        public float x;
        /// <summary>视口上边界。</summary>
        public float y;
        /// <summary>视口宽度。</summary>
        public float w;
        /// <summary>视口高度。</summary>
        public float h;
        /// <summary>映射后的最小深度。</summary>
        public float min_depth;
        /// <summary>映射后的最大深度。</summary>
        public float max_depth;
    }

    /// <summary>pixels_per_row / rows_per_layer 的单位是像素与行，不是字节；填 0 表示按 region 紧密排列。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUTextureTransferInfo
    {
        /// <summary>图像数据所在的传输缓冲区。</summary>
        public nint transfer_buffer;
        /// <summary>首个像素的字节偏移。</summary>
        public uint offset;
        /// <summary>相邻行起点相隔的像素数。</summary>
        public uint pixels_per_row;
        /// <summary>相邻层起点相隔的行数。</summary>
        public uint rows_per_layer;
    }

    /// <summary>指定传输缓冲区内的复制起点。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUTransferBufferLocation
    {
        /// <summary>参与复制的传输缓冲区。</summary>
        public nint transfer_buffer;
        /// <summary>数据起始字节偏移。</summary>
        public uint offset;
    }

    /// <summary>指定纹理复制涉及的子资源和三维区域。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUTextureRegion
    {
        /// <summary>参与复制的纹理。</summary>
        public nint texture;
        /// <summary>目标 mip 级别索引。</summary>
        public uint mip_level;
        /// <summary>目标层索引。</summary>
        public uint layer;
        /// <summary>区域左边界。</summary>
        public uint x;
        /// <summary>区域上边界。</summary>
        public uint y;
        /// <summary>区域前边界。</summary>
        public uint z;
        /// <summary>区域宽度。</summary>
        public uint w;
        /// <summary>区域高度。</summary>
        public uint h;
        /// <summary>区域深度。</summary>
        public uint d;
    }

    /// <summary>指定 GPU 缓冲区中的连续字节区间。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUBufferRegion
    {
        /// <summary>参与复制的缓冲区。</summary>
        public nint buffer;
        /// <summary>区域首字节偏移。</summary>
        public uint offset;
        /// <summary>区域长度，单位为字节。</summary>
        public uint size;
    }

    /// <summary>
    /// enable_* 是单字节 bool；后面的 padding1/padding2 把结构补齐到 props 的 4 字节对齐位置，
    /// 两者都属于 ABI，删掉会让 props 落到错误的偏移上。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUSamplerCreateInfo
    {
        /// <summary>缩小时使用的采样过滤方式。</summary>
        public uint min_filter;
        /// <summary>放大时使用的采样过滤方式。</summary>
        public uint mag_filter;
        /// <summary>mip 层之间的过滤方式。</summary>
        public uint mipmap_mode;
        /// <summary>U 坐标越界时的寻址模式。</summary>
        public uint address_mode_u;
        /// <summary>V 坐标越界时的寻址模式。</summary>
        public uint address_mode_v;
        /// <summary>W 坐标越界时的寻址模式。</summary>
        public uint address_mode_w;
        /// <summary>加在 mip LOD 上的偏移。</summary>
        public float mip_lod_bias;
        /// <summary>启用各向异性过滤时的最大各向异性值。</summary>
        public float max_anisotropy;
        /// <summary>过滤前对采样值使用的比较运算。</summary>
        public uint compare_op;
        /// <summary>计算所得 LOD 的下限。</summary>
        public float min_lod;
        /// <summary>计算所得 LOD 的上限。</summary>
        public float max_lod;
        /// <summary>是否启用各向异性过滤。</summary>
        public byte enable_anisotropy;
        /// <summary>是否将采样结果与参考值比较。</summary>
        public byte enable_compare;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding1;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding2;
        /// <summary>扩展属性的 SDL_PropertiesID，无扩展时为 0。</summary>
        public uint props;
    }

    /// <summary>描述顶点缓冲区的步幅和索引方式。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUVertexBufferDescription
    {
        /// <summary>顶点缓冲区绑定槽位。</summary>
        public uint slot;
        /// <summary>相邻元素起点间隔的字节数。</summary>
        public uint pitch;
        /// <summary>按顶点索引或实例索引推进。</summary>
        public uint input_rate;
        /// <summary>SDL 保留字段，须为 0。</summary>
        public uint instance_step_rate;
    }

    /// <summary>指定 shader 输入从顶点数据的何处读取。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUVertexAttribute
    {
        /// <summary>shader 输入位置索引。</summary>
        public uint location;
        /// <summary>存放本属性的顶点缓冲区槽位。</summary>
        public uint buffer_slot;
        /// <summary>属性元素的大小和数据类型。</summary>
        public uint format;
        /// <summary>属性相对于顶点元素起点的字节偏移。</summary>
        public uint offset;
    }

    /// <summary>指定管线使用的顶点缓冲区和属性描述数组。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUVertexInputState
    {
        /// <summary>顶点缓冲区描述数组的首地址。</summary>
        public nint vertex_buffer_descriptions;
        /// <summary>缓冲区描述的元素数。</summary>
        public uint num_vertex_buffers;
        /// <summary>顶点属性描述数组的首地址。</summary>
        public nint vertex_attributes;
        /// <summary>属性描述的元素数。</summary>
        public uint num_vertex_attributes;
    }

    /// <summary>定义模板测试不同结果下对样本的操作。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUStencilOpState
    {
        /// <summary>模板测试失败时执行的操作。</summary>
        public uint fail_op;
        /// <summary>模板和深度测试均通过时执行的操作。</summary>
        public uint pass_op;
        /// <summary>模板通过而深度测试失败时执行的操作。</summary>
        public uint depth_fail_op;
        /// <summary>模板测试使用的比较运算。</summary>
        public uint compare_op;
    }

    /// <summary>
    /// 本项目按预乘 alpha 配置混合因子；color_write_mask 只有 enable_color_write_mask 为 1 时才生效。
    /// 末尾两个 padding 是 ABI 的一部分，不要删。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUColorTargetBlendState
    {
        /// <summary>源 RGB 分量的混合因子。</summary>
        public uint src_color_blendfactor;
        /// <summary>目标 RGB 分量的混合因子。</summary>
        public uint dst_color_blendfactor;
        /// <summary>RGB 分量的合并运算。</summary>
        public uint color_blend_op;
        /// <summary>源 alpha 的混合因子。</summary>
        public uint src_alpha_blendfactor;
        /// <summary>目标 alpha 的混合因子。</summary>
        public uint dst_alpha_blendfactor;
        /// <summary>alpha 分量的合并运算。</summary>
        public uint alpha_blend_op;
        /// <summary>允许写入的 RGBA 通道位掩码。</summary>
        public byte color_write_mask;
        /// <summary>是否启用颜色混合。</summary>
        public byte enable_blend;
        /// <summary>是否应用通道写入掩码。</summary>
        public byte enable_color_write_mask;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding1;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding2;
    }

    /// <summary>
    /// code/code_size 指向的 shader 字节码与 entrypoint 字符串必须在调用期间保持固定（pin 住）。
    /// num_samplers 等四个资源数量要与 shader 里实际声明的一致，多报少报都会导致绑定错位。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUShaderCreateInfo
    {
        /// <summary>shader 代码的字节数。</summary>
        public nuint code_size;
        /// <summary>shader 代码首地址。</summary>
        public nint code;
        /// <summary>以空字节结尾的 UTF-8 入口函数名地址。</summary>
        public nint entrypoint;
        /// <summary>代码使用的 SDL_GPUShaderFormat。</summary>
        public uint format;
        /// <summary>程序所属的 shader 阶段。</summary>
        public uint stage;
        /// <summary>程序声明的采样器数量。</summary>
        public uint num_samplers;
        /// <summary>程序声明的存储纹理数量。</summary>
        public uint num_storage_textures;
        /// <summary>程序声明的存储缓冲区数量。</summary>
        public uint num_storage_buffers;
        /// <summary>程序声明的 uniform 缓冲区数量。</summary>
        public uint num_uniform_buffers;
        /// <summary>扩展属性的 SDL_PropertiesID，无扩展时为 0。</summary>
        public uint props;
    }

    /// <summary>num_levels 是 mip 层数（1 表示不带 mipmap）；layer_count_or_depth 按 type 解释为层数或深度，二者共用同一字段。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUTextureCreateInfo
    {
        /// <summary>纹理维度和数组类型。</summary>
        public uint type;
        /// <summary>纹理像素格式。</summary>
        public uint format;
        /// <summary>允许该纹理参与的操作标志。</summary>
        public uint usage;
        /// <summary>基础 mip 级宽度，单位为像素。</summary>
        public uint width;
        /// <summary>基础 mip 级高度，单位为像素。</summary>
        public uint height;
        /// <summary>数组层数或三维纹理深度。</summary>
        public uint layer_count_or_depth;
        /// <summary>mip 级数，1 表示仅有基础级。</summary>
        public uint num_levels;
        /// <summary>每个纹素的采样数枚举。</summary>
        public uint sample_count;
        /// <summary>扩展属性的 SDL_PropertiesID，无扩展时为 0。</summary>
        public uint props;
    }

    /// <summary>指定设备端缓冲区的容量和用途。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUBufferCreateInfo
    {
        /// <summary>创建时允许的 GPU 缓冲区用途标志。</summary>
        public uint usage;
        /// <summary>缓冲区容量，单位为字节。</summary>
        public uint size;
        /// <summary>扩展属性的 SDL_PropertiesID，无扩展时为 0。</summary>
        public uint props;
    }

    /// <summary>指定上传或下载缓冲区的容量与方向。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUTransferBufferCreateInfo
    {
        /// <summary>传输方向，用于上传或下载。</summary>
        public uint usage;
        /// <summary>缓冲区容量，单位为字节。</summary>
        public uint size;
        /// <summary>扩展属性的 SDL_PropertiesID，无扩展时为 0。</summary>
        public uint props;
    }

    /// <summary>末尾 padding1/padding2 是 ABI 占位，不要删。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPURasterizerState
    {
        /// <summary>多边形的填充或线框模式。</summary>
        public uint fill_mode;
        /// <summary>按三角形朝向选择的剔除模式。</summary>
        public uint cull_mode;
        /// <summary>用于判定正面的顶点绕序。</summary>
        public uint front_face;
        /// <summary>片元深度偏移的常量系数。</summary>
        public float depth_bias_constant_factor;
        /// <summary>片元深度偏移的限幅值。</summary>
        public float depth_bias_clamp;
        /// <summary>按斜率计算深度偏移的系数。</summary>
        public float depth_bias_slope_factor;
        /// <summary>是否启用片元深度偏移。</summary>
        public byte enable_depth_bias;
        /// <summary>启用时裁剪深度，禁用时钳制深度。</summary>
        public byte enable_depth_clip;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding1;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding2;
    }

    /// <summary>
    /// 注意 padding 是 padding2/padding3 而不是从 1 开始编号 —— 这是照抄 SDL_gpu.h 的原名，
    /// 不是笔误，不要"顺手"重编号或删掉。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUMultisampleState
    {
        /// <summary>光栅化使用的多重采样数枚举。</summary>
        public uint sample_count;
        /// <summary>SDL 保留字段，须为 0。</summary>
        public uint sample_mask;
        /// <summary>SDL 保留开关，须为 0。</summary>
        public byte enable_mask;
        /// <summary>是否根据 alpha 生成采样覆盖掩码。</summary>
        public byte enable_alpha_to_coverage;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding2;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding3;
    }

    /// <summary>back_stencil_state 在 front_stencil_state 之前，这个顺序来自 SDL_gpu.h，写反不会报错只会画错。末尾三个 padding 不要删。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUDepthStencilState
    {
        /// <summary>深度测试使用的比较运算。</summary>
        public uint compare_op;
        /// <summary>背面三角形的模板操作。</summary>
        public GPUStencilOpState back_stencil_state;
        /// <summary>正面三角形的模板操作。</summary>
        public GPUStencilOpState front_stencil_state;
        /// <summary>参与模板比较的位。</summary>
        public byte compare_mask;
        /// <summary>模板更新允许写入的位。</summary>
        public byte write_mask;
        /// <summary>是否执行深度测试。</summary>
        public byte enable_depth_test;
        /// <summary>是否在启用深度测试时写入深度。</summary>
        public byte enable_depth_write;
        /// <summary>是否执行模板测试。</summary>
        public byte enable_stencil_test;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding1;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding2;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding3;
    }

    /// <summary>指定管线颜色附件的格式与混合规则。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUColorTargetDescription
    {
        /// <summary>作为颜色附件的纹理像素格式。</summary>
        public uint format;
        /// <summary>该附件的颜色混合配置。</summary>
        public GPUColorTargetBlendState blend_state;
    }

    /// <summary>has_depth_stencil_target 是单字节 bool；后面三个 padding 把结构补到 4 字节对齐，属于 ABI。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUGraphicsPipelineTargetInfo
    {
        /// <summary>颜色附件描述数组的首地址。</summary>
        public nint color_target_descriptions;
        /// <summary>颜色附件描述的元素数。</summary>
        public uint num_color_targets;
        /// <summary>使用深度模板附件时的像素格式。</summary>
        public uint depth_stencil_format;
        /// <summary>管线是否使用深度模板附件。</summary>
        public byte has_depth_stencil_target;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding1;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding2;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding3;
    }

    /// <summary>组合 shader、顶点布局、光栅化和附件状态以创建图形管线。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUGraphicsPipelineCreateInfo
    {
        /// <summary>管线使用的顶点 shader。</summary>
        public nint vertex_shader;
        /// <summary>管线使用的片元 shader。</summary>
        public nint fragment_shader;
        /// <summary>顶点缓冲区和属性布局。</summary>
        public GPUVertexInputState vertex_input_state;
        /// <summary>顶点构成图元的拓扑类型。</summary>
        public uint primitive_type;
        /// <summary>剔除、填充和深度偏移配置。</summary>
        public GPURasterizerState rasterizer_state;
        /// <summary>多重采样配置。</summary>
        public GPUMultisampleState multisample_state;
        /// <summary>深度与模板测试配置。</summary>
        public GPUDepthStencilState depth_stencil_state;
        /// <summary>渲染附件的格式与混合规则。</summary>
        public GPUGraphicsPipelineTargetInfo target_info;
        /// <summary>扩展属性的 SDL_PropertiesID，无扩展时为 0。</summary>
        public uint props;
    }

    /// <summary>
    /// clear_color 只在 load_op 为 Clear 时使用；cycle 让 SDL 在资源仍被占用时自动换一份内部缓冲。
    /// 末尾 padding1/padding2 是 ABI 占位，不要删。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUColorTargetInfo
    {
        /// <summary>用作颜色附件的纹理。</summary>
        public nint texture;
        /// <summary>颜色附件使用的 mip 级别。</summary>
        public uint mip_level;
        /// <summary>数组层索引或三维深度切片。</summary>
        public uint layer_or_depth_plane;
        /// <summary>以 CLEAR 开始渲染时写入的颜色。</summary>
        public FColor clear_color;
        /// <summary>渲染开始时如何处理附件的原内容。</summary>
        public uint load_op;
        /// <summary>渲染结束时如何保存附件内容。</summary>
        public uint store_op;
        /// <summary>多重采样解析结果的目标纹理。</summary>
        public nint resolve_texture;
        /// <summary>接收解析结果的 mip 级别。</summary>
        public uint resolve_mip_level;
        /// <summary>接收解析结果的纹理层。</summary>
        public uint resolve_layer;
        /// <summary>启用时允许 SDL 在附件已绑定且无需加载旧内容时轮换纹理。</summary>
        public byte cycle;
        /// <summary>解析目标仍被绑定时是否允许轮换。</summary>
        public byte cycle_resolve_texture;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding1;
        /// <summary>维持原生结构布局的填充字节。</summary>
        public byte padding2;
    }

    /// <summary>将缓冲区从指定偏移处绑定到输入槽位。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUBufferBinding
    {
        /// <summary>要绑定的 GPU 缓冲区。</summary>
        public nint buffer;
        /// <summary>绑定区间首字节在缓冲区内的偏移。</summary>
        public uint offset;
    }

    /// <summary>指定采样阶段绑定的一对纹理与采样器。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUTextureSamplerBinding
    {
        /// <summary>以 SAMPLER 用途创建的纹理。</summary>
        public nint texture;
        /// <summary>与纹理配对的采样器。</summary>
        public nint sampler;
    }

    /// <summary>SDL 的浮点颜色，分量为线性 0-1，不是 0-255。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FColor
    {
        /// <summary>依次为红、绿、蓝及不透明度的浮点分量。</summary>
        public float r, g, b, a;
    }

    /// <summary>对应 SDL_Rect：x/y 为左上角，w/h 为尺寸；单位是物理像素。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct IntRect
    {
        /// <summary>依次为矩形左边界、上边界、宽和高。</summary>
        public int x, y, w, h;
    }
}

