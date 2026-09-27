using System.Runtime.InteropServices;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Core;

/// <summary>
/// 在选中的原生 GPU 上真跑上传、提交绘制、shader 绑定与读回；与纯 CPU 自测分开，需要可用的 GPU 设备。
/// </summary>
public static class GpuSelfTest
{
    /// <summary>
    /// 创建隐藏窗口取得 GPU 设备后按顺序执行；Host/Canvas 靠 using 保证释放晚于依附它们的 target、texture 和 shader。
    /// </summary>
    public static int Run()
    {
        using var host = new Host(hidden : true);
        using var canvas = new Canvas(host.Gpu);
        int checks = 0;
        void Check(bool ok, string description)
        {
            if (!ok)
            {
                throw new InvalidOperationException("GPU test failed: " + description);
            }
            checks++;
            Console.WriteLine("PASS " + description);
        }
        Console.WriteLine("GPU: " + host.Device);
        // 尺寸逐个对照原生 SDL3 头文件里的 sizeof(SDL_GPU*)（64 位）。托管侧结构体一旦多/少一个字段或对齐变化，
        // P/Invoke 传过去的就是垃圾内存，而且往往不会立刻崩，只表现为画面异常——所以在 ABI 层直接拦。
        var abi = new(int Actual, int Expected)[]
        {
            (Marshal.SizeOf<Sdl.GPUViewport>(), 24), (Marshal.SizeOf<Sdl.GPUTextureTransferInfo>(), 24), (Marshal.SizeOf<Sdl.GPUTransferBufferLocation>(), 16),
                (Marshal.SizeOf<Sdl.GPUTextureRegion>(), 40), (Marshal.SizeOf<Sdl.GPUBufferRegion>(), 16), (Marshal.SizeOf<Sdl.GPUSamplerCreateInfo>(), 52),
                (Marshal.SizeOf<Sdl.GPUVertexBufferDescription>(), 16), (Marshal.SizeOf<Sdl.GPUVertexAttribute>(), 16), (Marshal.SizeOf<Sdl.GPUVertexInputState>(), 32),
                (Marshal.SizeOf<Sdl.GPUStencilOpState>(), 16), (Marshal.SizeOf<Sdl.GPUColorTargetBlendState>(), 32), (Marshal.SizeOf<Sdl.GPUShaderCreateInfo>(), 56),
                (Marshal.SizeOf<Sdl.GPUTextureCreateInfo>(), 36), (Marshal.SizeOf<Sdl.GPUBufferCreateInfo>(), 12), (Marshal.SizeOf<Sdl.GPUTransferBufferCreateInfo>(), 12),
                (Marshal.SizeOf<Sdl.GPURasterizerState>(), 28), (Marshal.SizeOf<Sdl.GPUMultisampleState>(), 12), (Marshal.SizeOf<Sdl.GPUDepthStencilState>(), 44),
                (Marshal.SizeOf<Sdl.GPUColorTargetDescription>(), 36), (Marshal.SizeOf<Sdl.GPUGraphicsPipelineTargetInfo>(), 24),
                (Marshal.SizeOf<Sdl.GPUGraphicsPipelineCreateInfo>(), 168), (Marshal.SizeOf<Sdl.GPUColorTargetInfo>(), 64), (Marshal.SizeOf<Sdl.GPUBufferBinding>(), 16),
                (Marshal.SizeOf<Sdl.GPUTextureSamplerBinding>(), 16),
        };
        Check(abi.All(x => x.Actual == x.Expected),
            "SDL GPU interop structures match the native 64-bit ABI");
        // 67x9 和 3x2 都是故意取的奇数：67*4=268 字节的行宽不是 256 的倍数，能逼出 D3D12 的行距对齐路径，
        // 而紧密排列的 3x2 上传能验出把行距当成宽度用的错误。source 是两行已知颜色，用来同时检查内容与上下方向。
        using var target = new Target(host.Gpu, 67, 9);
        byte[] source = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 255, 255, 0, 255, 255, 0, 255, 255, 255];
        using var texture = new Texture(host.Gpu, 3, 2, source);
        byte[] DrawRows()
        {
            canvas.Begin(target, 67, 9, 67, 9, new(0, 0, 0, 1));
            canvas.Quad(texture, new(2, 3, 3, 2), Color.White);
            return canvas.Read(target);
        }
        // 贴图画在 (2,3)，因此读回缓冲里第 3、4 行、第 2 列起的 12 字节应当逐字节等于 source 的两行。
        var bytes = DrawRows();
        Check(bytes.AsSpan((3 * 67 + 2) * 4, 12).SequenceEqual(source.AsSpan(0, 12)) && bytes.AsSpan((4 * 67 + 2) * 4,
            12).SequenceEqual(source.AsSpan(12, 12)), "odd-width texture upload and padded readback preserve both rows and orientation");
        // 再画一遍必须逐字节相同：transfer buffer 与顶点缓冲是轮换复用的，复用错了第二帧才会看出来。
        Check(DrawRows().SequenceEqual(bytes), "successive submissions preserve upload data and cycle vertex buffers");
        canvas.Begin(target, 67, 9, 67, 9, new(0, 0, 0, 1));
        canvas.Clip(new(2, 3, 3, 2));
        canvas.Fill(new(0, 0, 67, 9), new(1, 0, 0, .5f));
        canvas.Clip(null);
        var clipped = canvas.Read(target);
        // 逐像素核对裁剪矩形：裁剪区用左上角原点（y 向下），区外必须保持清屏的黑色。
        // 区内是 alpha=.5 的红叠在黑底上，正确结果是 127/128（两种取整都放行），出现 191 就说明 alpha 被乘了两次。
        bool clipping = true;
        for (int y = 0; y < 9; y++)
        {
            for (int x = 0; x < 67; x++)
            {
                int offset = (y * 67 + x) * 4;
                bool inside = x >= 2 && x < 5 && y >= 3 && y < 5;
                clipping&= inside ? clipped[offset] is >= 127 and <= 128 : clipped[offset] == 0;
                clipping&= clipped[offset + 1] == 0 && clipped[offset + 2] == 0 && clipped[offset + 3] == 255;
            }
        }
        Check(clipping, "scissor uses top-left pixels and source alpha is applied once");
        using var shader = new Shader(host.Gpu, Canvas.VertexSource,
            "#version 450\nin vec2 v_vTexcoord;uniform sampler2D gm_BaseTexture,second;uniform vec4 tint;out vec4 fragColor;void main(){fragColor=(texture(gm_BaseTexture,v_vTexcoord)+texture(second,v_vTexcoord))*.5*tint;}");
        using var redTexture = new Texture(host.Gpu, 1, 1, [255, 0, 0, 255]);
        using var blueTexture = new Texture(host.Gpu, 1, 1, [0, 0, 255, 255]);
        // 两次绘制之间只改 uniform 和 1 号 sampler，中间 Flush 一次强制分成两个批次：
        // 入队时必须把当时的 uniform 快照下来，否则后一次的 tint 会回头把前一次也改掉。
        // 读回后第 0 个像素应是纯红、第 2 个像素（偏移 8）应是纯蓝。
        canvas.Begin(target, 67, 9, 67, 9, new(0, 0, 0, 1));
        host.Gpu.BindTexture(1, redTexture);
        shader.Int("second", 1);
        shader.Vec4("tint", new(1, 0, 0));
        canvas.Quad(canvas.White, new(0, 0, 2, 2), Color.White, shader: shader);
        canvas.Flush();
        host.Gpu.BindTexture(1, blueTexture);
        shader.Vec4("tint", new(0, 0, 1));
        canvas.Quad(canvas.White, new(2, 0, 2, 2), Color.White, shader: shader);
        bytes = canvas.Read(target);
        Check(bytes[0] == 255 && bytes[2] == 0 && bytes[8] == 0 && bytes[10] == 255,
            "queued draws snapshot distinct uniforms and bind multiple samplers");
        // 改成 131x7（同样是奇数行宽）：清屏和读回都必须覆盖整个新尺寸，不能残留按旧尺寸算的行距。
        // 清屏色 .25/.75 在 RGBA8 下是 63/64 与 191/192，两种取整都放行。
        target.Resize(131, 7);
        canvas.Begin(target, 131, 7, 131, 7, new(.25f, .5f, .75f, 1));
        bytes = canvas.Read(target);
        Check(bytes.Length == 131 * 7 * 4 && bytes[0] is >= 63 and <= 64 && bytes[ ^ 2] is >= 191 and <= 192,
            "resized render targets clear and read back their entire new extent");
        // 共用顶点 shader 还要能翻译成合法 HLSL：D3D12 后端在 Windows 上走这条路径，本机跑 Metal/Vulkan 也得先验它。
        ShaderCompiler.ValidateHlsl(Canvas.VertexSource, true);
        Check(true, "shared vertex shader translates to valid HLSL");
        // 把随程序分发的每一个 .frag 都过一遍 AdaptGml 转换 + 原生编译 + HLSL 翻译：
        // 语法错误或用了后端不支持的特性，必须在自测阶段暴露，而不是等用户切到某个 gimmick 时黑屏。
        foreach (string path in Directory.EnumerateFiles(Path.Combine(Paths.Assets, "Shaders"), "*.frag")
            .Concat(Directory.EnumerateFiles(Path.Combine(Paths.Assets, "Gimmicks"), "*.frag", SearchOption.AllDirectories)))
        {
            string sourceShader = Shader.AdaptGml(File.ReadAllText(path));
            using var effect = new Shader(host.Gpu, Canvas.VertexSource, sourceShader);
            ShaderCompiler.ValidateHlsl(sourceShader, false);
            // 编译通过还不够，要连 pipeline 一起建出来：顶点输入布局与 blend 状态的不匹配只在链接时才报错。
            effect.Pipeline(GpuDevice.Rgba8, BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
            Check(true, "native pipeline and HLSL translation: " + Path.GetFileName(path));
        }
        // 其余需要真实 GPU 的子套件复用同一个 canvas（因而也复用同一个设备和线程），不各自再建设备。
        TextFilmSelfTest.CheckGpu(canvas);
        CustomAdaptationSelfTest.CheckGpu(canvas);
        CustomProxySelfTest.CheckGpu(canvas);
        CustomFxSelfTest.CheckGpu(canvas);
        NativeSequenceSelfTest.CheckGpu(canvas);
        Console.WriteLine($"{checks} GPU checks plus text/film rendering passed.");
        return 0;
    }
}
