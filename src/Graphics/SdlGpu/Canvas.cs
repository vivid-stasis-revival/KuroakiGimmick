using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Graphics;

/// <summary>立即模式二维绘制门面。坐标可为逻辑单位，提交到 GPU 前明确转换 viewport 与 scissor。</summary>
public sealed partial class Canvas : IDisposable
{
    // 整数缩放按物理像素（含 Retina）度量，而不是窗口逻辑点；
    // 原点也对齐到同一套像素网格。
    /// <summary>以物理像素决定整数缩放，避免 Retina 下把窗口点误当像素。</summary>
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

    public GpuDevice Gpu { get; }

    /// <summary>当前绘制目标的物理像素密度：一个逻辑单位对应多少物理像素。</summary>
    public float PixelScaleX => width > 0 ? pixelWidth / width : 1;
    public float PixelScaleY => height > 0 ? pixelHeight / height : 1;

    /// <summary>把一个逻辑坐标吸附到当前目标的物理像素网格；返回值仍是逻辑单位。</summary>
    public float SnapX(float x) => PixelScaleX > 0 ? MathF.Round(x * PixelScaleX) / PixelScaleX : x;
    public float SnapY(float y) => PixelScaleY > 0 ? MathF.Round(y * PixelScaleY) / PixelScaleY : y;

    public Shader Basic { get; }

    /// <summary>1×1 的不透明白纹理；纯色填充借它走与带纹理绘制完全相同的管线。</summary>
    public Texture White { get; }

    readonly List<float> vertices = new(65536);

    Texture? currentTexture;

    Target? target;

    Sdl.IntRect? clip;

    Shader? currentShader;

    float width, height;

    int pixelWidth, pixelHeight;

    BlendFactor blendSource = BlendFactor.SourceAlpha, blendDestination = BlendFactor.InverseSourceAlpha;

    /// <summary>混合因子变化前提交现有几何，防止较晚的状态污染先前图元。</summary>
    public void Blend(BlendFactor source, BlendFactor destination)
    {
        Flush();
        blendSource = source;
        blendDestination = destination;
    }

    /// <summary>共享顶点程序：逻辑坐标按 u_resolution 归一化到 NDC 并翻转 Y，以匹配左上原点。</summary>
    public static string VertexSource => "#version 330 core\nlayout(location=0) in vec2 a_position;layout(location=1) in vec2 a_uv;layout(location=2) in vec4 a_color;uniform vec2 u_resolution;out vec2 v_vTexcoord;out vec4 v_vColour;void main(){vec2 p=a_position/u_resolution*2.-1.;p.y=-p.y;gl_Position=vec4(p,0.,1.);v_vTexcoord=a_uv;v_vColour=a_color;}";

    /// <summary>创建并拥有 Basic shader 与 1×1 的 White 纹理；GpuDevice 由外部拥有，不在这里释放。</summary>
    public Canvas(GpuDevice gpu)
    {
        Gpu = gpu;
        Basic = new(gpu, VertexSource,
            "#version 450\nin vec2 v_vTexcoord;in vec4 v_vColour;uniform sampler2D gm_BaseTexture;out vec4 fragColor;void main(){fragColor=texture(gm_BaseTexture,v_vTexcoord)*v_vColour;}");
        White = new(gpu, 1, 1, [255, 255, 255, 255]);
    }

    /// <summary>切换渲染目标，清空裁剪与 shader 选择；像素尺寸和逻辑尺寸分别传入。</summary>
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
        Gpu.Begin(target, pixelWidth, pixelHeight, clear);
        currentShader = null;
    }

    // SDL_GPU 的纹理与 viewport 都以左上角为原点。
    /// <summary>逻辑矩形转换为物理像素裁剪；左上取 floor、右下取 ceil，保留边缘像素。</summary>
    public void Clip(Rect? bounds)
    {
        Flush();
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
        currentShader.Vec2("u_resolution", width, height);
        currentShader.Int("gm_BaseTexture", 0);
        Gpu.Draw(target, pixelWidth, pixelHeight, currentShader, currentTexture, clip, blendSource, blendDestination,
            CollectionsMarshal.AsSpan(vertices));
        vertices.Clear();
    }

    /// <summary>执行一次全屏离屏滤镜。输入纹理不能与输出目标为同一纹理。</summary>
    public void Pass(Target output, Texture input, Shader shader, Action<Shader>? uniforms = null)
    {
        // 输出目标先清成不透明黑：滤镜结果覆盖整个目标，不保留上一次的残留内容。
        Begin(output, output.Texture.Width, output.Texture.Height, output.Texture.Width, output.Texture.Height, new(0, 0, 0, 1));
        uniforms?.Invoke(shader);
        Quad(input, new(0, 0, output.Texture.Width, output.Texture.Height), Color.White, shader: shader);
        Flush();
    }

    /// <summary>先 Flush 再提交队列，然后释放自有的 White 与 Basic；不释放外部传入的 GpuDevice。</summary>
    public void Dispose()
    {
        Flush();
        Gpu.Submit();
        White.Dispose();
        Basic.Dispose();
    }
}

