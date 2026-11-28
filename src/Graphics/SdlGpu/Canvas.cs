// 将逻辑空间中的绘制操作组织成可提交至 SDL GPU 的批次。

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Graphics;

/// <summary>立即模式二维绘制门面。坐标可为逻辑单位，提交到 GPU 前明确转换 viewport 与 scissor。</summary>
public sealed partial class Canvas : IDisposable
{
    internal const string BasicFragmentSource = "#version 450\nin vec2 v_vTexcoord;in vec4 v_vColour;uniform sampler2D gm_BaseTexture;out vec4 fragColor;void main(){fragColor=texture(gm_BaseTexture,v_vTexcoord)*v_vColour;}";

    // 整数缩放按物理像素（含 Retina）度量，而不是窗口逻辑点；
    // 原点也对齐到同一套像素网格。
    /// <summary>以物理像素决定整数缩放，避免 Retina 下把窗口点误当像素。</summary>
    /// <param name="bounds">允许放置预览的矩形。</param>
    /// <param name="dpiX">每个横向逻辑单位对应的像素数。</param>
    /// <param name="dpiY">每个纵向逻辑单位对应的像素数。</param>
    /// <param name="integer">是否优先使用整数缩放。</param>
    /// <returns>保持预览比例并对齐输出的矩形。</returns>
    public static Rect PreviewRect(Rect bounds, float dpiX, float dpiY, bool integer)
    {
        float scale = Math.Min(bounds.W * dpiX / 320, bounds.H * dpiY / 180);
        if (integer && scale >= 1)
        {
            scale = MathF.Floor(scale);
        }
        float width = 320 * scale / dpiX, height = 180 * scale / dpiY;
        float x = bounds.X + (bounds.W - width) / 2, y = bounds.Y + (bounds.H - height) / 2;
        if (integer && scale >= 1)
        {
            x = MathF.Round(x * dpiX) / dpiX;
            y = MathF.Round(y * dpiY) / dpiY;
        }
        return new(x, y, width, height);
    }

    /// <summary>当前画布提交渲染命令所用的 SDL GPU 设备。</summary>
    public GpuDevice Gpu { get; }

    /// <summary>当前绘制目标的物理像素密度：一个逻辑单位对应多少物理像素。</summary>
    public float PixelScaleX => width > 0 ? pixelWidth / width : 1;
    /// <summary>每个逻辑纵向单位对应的目标像素数，无有效高度时取 1。</summary>
    public float PixelScaleY => height > 0 ? pixelHeight / height : 1;

    /// <summary>把一个逻辑坐标吸附到当前目标的物理像素网格；返回值仍是逻辑单位。</summary>
    /// <param name="x">待对齐的逻辑横坐标。</param>
    /// <returns>对齐到目标像素的逻辑横坐标。</returns>
    public float SnapX(float x) => PixelScaleX > 0 ? MathF.Round(x * PixelScaleX) / PixelScaleX : x;
    /// <summary>将逻辑纵坐标对齐到最近的目标像素边界。</summary>
    /// <param name="y">待对齐的逻辑纵坐标。</param>
    /// <returns>对齐到目标像素的逻辑纵坐标。</returns>
    public float SnapY(float y) => PixelScaleY > 0 ? MathF.Round(y * PixelScaleY) / PixelScaleY : y;

    /// <summary>画布基础图元使用的 shader。</summary>
    public Shader Basic { get; }
    internal Shader? NoteInstances { get; }

    /// <summary>1×1 的不透明白纹理；纯色填充借它走与带纹理绘制完全相同的管线。</summary>
    public Texture White { get; }

    readonly List<float> vertices = new(65536);

    Texture? currentTexture;

    Target? target;

    Sdl.IntRect? clip;
    Rect? clipLimit;

    Shader? currentShader;

    float width, height;

    int pixelWidth, pixelHeight;

    BlendFactor blendSource = BlendFactor.SourceAlpha, blendDestination = BlendFactor.InverseSourceAlpha;

    /// <summary>混合因子变化前提交现有几何，防止较晚的状态污染先前图元。</summary>
    /// <param name="source">源颜色混合因子。</param>
    /// <param name="destination">目标颜色混合因子。</param>
    public void Blend(BlendFactor source, BlendFactor destination)
    {
        Flush();
        blendSource = source;
        blendDestination = destination;
    }

    /// <summary>共享顶点程序：逻辑坐标按 u_resolution 归一化到 NDC 并翻转 Y，以匹配左上原点。</summary>
    public static string VertexSource => "#version 330 core\nlayout(location=0) in vec2 a_position;layout(location=1) in vec2 a_uv;layout(location=2) in vec4 a_color;uniform vec2 u_resolution;out vec2 v_vTexcoord;out vec4 v_vColour;void main(){vec2 p=a_position/u_resolution*2.-1.;p.y=-p.y;gl_Position=vec4(p,0.,1.);v_vTexcoord=a_uv;v_vColour=a_color;}";

    /// <summary>创建并拥有 Basic shader 与 1×1 的 White 纹理；GpuDevice 由外部拥有，不在这里释放。</summary>
    /// <param name="gpu">持有渲染资源的 GPU 设备。</param>
    public Canvas(GpuDevice gpu)
    {
        Gpu = gpu;
        Basic = new(gpu, VertexSource,
            BasicFragmentSource);
        // 场景、离屏导出与 UI 都绘制在 RGBA8；最终呈现落在交换链。两种格式都要覆盖真实使用的混合组合。
        foreach (var format in PipelineFormats(gpu))
        {
            Basic.Prewarm(format,
                (BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha),
                (BlendFactor.InverseDestinationColor, BlendFactor.InverseSourceColor),
                (BlendFactor.InverseDestinationColor, BlendFactor.Zero),
                (BlendFactor.DestinationColor, BlendFactor.Zero),
                (BlendFactor.SourceAlpha, BlendFactor.Zero),
                (BlendFactor.One, BlendFactor.Zero));
        }
        if (Environment.GetEnvironmentVariable("KG_NOTE_INSTANCING") != "off")
        {
            try
            {
                NoteInstances = new(gpu, NoteInstanceVertexSource, NoteInstanceFragmentSource, instanced: true);
                NoteInstances.Prewarm(GpuDevice.Rgba8,
                    (BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Note instancing unavailable; using geometry: " + ex.Message);
            }
        }
        White = new(gpu, 1, 1, [255, 255, 255, 255]);
    }

    // 交换链格式可能与离屏目标相同；相同格式只预热一次，避免重复描述。
    static IEnumerable<uint> PipelineFormats(GpuDevice gpu)
    {
        yield return GpuDevice.Rgba8;
        if (gpu.SwapchainFormat != GpuDevice.Rgba8) yield return gpu.SwapchainFormat;
    }

    /// <summary>切换渲染目标，清空裁剪与 shader 选择；像素尺寸和逻辑尺寸分别传入。</summary>
    /// <param name="target">本次绘制的目标。</param>
    /// <param name="pixelWidth">目标的物理像素宽度。</param>
    /// <param name="pixelHeight">目标的物理像素高度。</param>
    /// <param name="logicalWidth">画布的逻辑宽度。</param>
    /// <param name="logicalHeight">画布的逻辑高度。</param>
    /// <param name="clear">可选的目标清屏色。</param>
    public void Begin(Target? target, int pixelWidth, int pixelHeight, float logicalWidth, float logicalHeight, Color? clear = null)
    {
        Flush();
        presentationOpacity = 1;
        width = logicalWidth;
        height = logicalHeight;
        this.target = target;
        this.pixelWidth = pixelWidth;
        this.pixelHeight = pixelHeight;
        clip = null;
        clipLimit = null;
        Gpu.Begin(target, pixelWidth, pixelHeight, clear);
        currentShader = null;
    }

    // SDL_GPU 的纹理与 viewport 都以左上角为原点。
    /// <summary>逻辑矩形转换为物理像素裁剪；左上取 floor、右下取 ceil，保留边缘像素。</summary>
    /// <param name="bounds">新的裁剪矩形，null 表示恢复外层边界。</param>
    public void Clip(Rect? bounds)
    {
        Flush();
        if (clipLimit is { } limit)
        {
            if (bounds is { } requested)
            {
                float clipLeft = Math.Max(limit.X, requested.X), clipTop = Math.Max(limit.Y, requested.Y);
                float clipRight = Math.Min(limit.X + limit.W, requested.X + requested.W);
                float clipBottom = Math.Min(limit.Y + limit.H, requested.Y + requested.H);
                bounds = new(clipLeft, clipTop, Math.Max(0, clipRight - clipLeft), Math.Max(0, clipBottom - clipTop));
            }
            else bounds = limit;
        }
        if (bounds is not { } r)
        {
            clip = null;
            return;
        }
        int left = Math.Clamp((int)MathF.Floor(r.X * pixelWidth / width), 0, pixelWidth);
        int right = Math.Clamp((int)MathF.Ceiling((r.X + r.W) * pixelWidth / width), left, pixelWidth);
        int top = Math.Clamp((int)MathF.Floor(r.Y * pixelHeight / height), 0, pixelHeight);
        int bottom = Math.Clamp((int)MathF.Ceiling((r.Y + r.H) * pixelHeight / height), top, pixelHeight);
        clip = new Sdl.IntRect
        {
            x = left,
            y = top,
            w = right - left,
            h = bottom - top
        };
    }

    /// <summary>设置面板的 scissor 边界，内部 Clip(null) 在边界被移除前恢复到此范围。</summary>
    /// <param name="bounds">面板的外层裁剪边界，null 时移除此限制。</param>
    public void LimitClip(Rect? bounds)
    {
        clipLimit = bounds;
        Clip(null);
    }

    /// <summary>纹理或 shader 变化时先 Flush，让已累积的几何仍按旧状态提交；shader 为 null 时回退到 Basic。</summary>
    void Select(Texture tex, Shader? shader)
    {
        shader ??= Basic;
        if (currentTexture != tex || currentShader != shader)
        {
            Flush();
            currentTexture = tex;
            currentShader = shader;
        }
    }

    /// <summary>把当前同纹理/同 shader 的几何交给设备队列，不等同于交换链呈现。</summary>
    public void Flush()
    {
        if (vertices.Count == 0 || currentShader == null || currentTexture == null)
        {
            return;
        }
        if (clip is { w: <= 0 } or { h: <= 0 })
        {
            vertices.Clear();
            return;
        }
        currentShader.Vec2("u_resolution", width, height);
        currentShader.Int("gm_BaseTexture", 0);
        Gpu.Draw(target, pixelWidth, pixelHeight, currentShader, currentTexture, clip, blendSource, blendDestination,
            CollectionsMarshal.AsSpan(vertices));
        vertices.Clear();
    }

    /// <summary>执行一次全屏离屏滤镜。输入纹理不能与输出目标为同一纹理。</summary>
    /// <param name="output">接收绘制结果的目标。</param>
    /// <param name="input">作为输入画面的纹理。</param>
    /// <param name="shader">本次绘制使用的 shader。</param>
    /// <param name="uniforms">在提交绘制前设置 shader 参数的回调。</param>
    /// <param name="preserveAlpha">是否保留输入的 alpha。</param>
    public void Pass(Target output, Texture input, Shader shader, Action<Shader>? uniforms = null, bool preserveAlpha = false)
    {
        // 全屏滤镜默认清成不透明黑；独立图层可保留透明覆盖范围。
        Begin(output, output.Texture.Width, output.Texture.Height, output.Texture.Width, output.Texture.Height, new(0, 0, 0, preserveAlpha ? 0 : 1));
        // 独立图层滤镜须保留覆盖度，直接复制预乘 RGBA，避免与不透明黑底混合。
        if (preserveAlpha) Blend(BlendFactor.One, BlendFactor.Zero);
        try
        {
            uniforms?.Invoke(shader);
            Quad(input, new(0, 0, output.Texture.Width, output.Texture.Height), Color.White, shader: shader);
            Flush();
        }
        finally
        {
            if (preserveAlpha) Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        }
    }

    /// <summary>先 Flush 再提交队列，然后释放自有的 White 与 Basic；不释放外部传入的 GpuDevice。</summary>
    public void Dispose()
    {
        Flush();
        Gpu.Submit();
        White.Dispose();
        Basic.Dispose();
        NoteInstances?.Dispose();
    }
}

