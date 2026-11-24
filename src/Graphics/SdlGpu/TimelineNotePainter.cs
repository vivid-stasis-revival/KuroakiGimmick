namespace KuroakiGimmick.Graphics;

/// <summary>用一个有序实例替代编辑器的五个矩形，保留边缘和拐角处填充及边框的不透明度叠加。</summary>
public sealed class TimelineNotePainter : IDisposable
{
    readonly Canvas canvas;
    readonly Shader shader;
    float scaleX = float.NaN, scaleY;
    const string Vertex = """
        #version 450
        layout(location=0) in vec4 a_origin;layout(location=1) in vec4 a_sizeRotation;
        layout(location=2) in vec4 a_uvRect;layout(location=3) in vec4 a_color;
        uniform vec2 u_resolution;uniform vec2 u_pixelScale;
        layout(location=0) flat out vec4 bounds;layout(location=1) flat out vec4 fill;layout(location=2) flat out vec3 edge;layout(location=3) flat out vec2 border;
        void main(){
            const vec2 corners[6]=vec2[6](vec2(0,0),vec2(1,0),vec2(0,1),vec2(0,1),vec2(1,0),vec2(1,1));
            vec2 world=a_origin.xy+corners[gl_VertexIndex]*a_sizeRotation.xy;
            vec2 p=world/u_resolution*2.-1.;p.y=-p.y;gl_Position=vec4(p,0,1);
            bounds=vec4(a_origin.xy*u_pixelScale,(a_origin.xy+a_sizeRotation.xy)*u_pixelScale);
            fill=a_color;edge=a_uvRect.xyz;border=u_pixelScale;
        }
        """;
    const string Fragment = """
        #version 450
        layout(location=0) flat in vec4 bounds;layout(location=1) flat in vec4 fill;layout(location=2) flat in vec3 edge;layout(location=3) flat in vec2 border;out vec4 fragColor;
        void main(){
            vec2 p=gl_FragCoord.xy;
            float layers=(p.y<bounds.y+border.y?1.:0.)+(p.y>=bounds.w-border.y?1.:0.)
                +(p.x<bounds.x+border.x?1.:0.)+(p.x>=bounds.z-border.x?1.:0.);
            float q=1.-fill.a, rest=layers<.5?1.:layers<1.5?q:q*q;
            float alpha=1.-rest*q;
            vec3 premul=fill.rgb*fill.a*rest+edge*(1.-rest);
            fragColor=vec4(alpha>0.?premul/alpha:vec3(0.),alpha);
        }
        """;
    /// <summary>初始化时间轴音符预览所用的绘制资源。</summary>
    /// <param name="canvas">提交绘制命令的画布。</param>
    public TimelineNotePainter(Canvas canvas)
    {
        this.canvas = canvas;
        shader = new(canvas.Gpu, Vertex, Fragment, instanced: true);
    }
    static int Palette(int type, int lane) => type is 6 or 7 ? 5 : type is 1 or 8 ? 2 + lane : lane < 2 ? 0 : 1;
    static (Color Fill, Color Edge) Colors(int palette) => palette switch
    {
        0 => (Color.Hex(0xCBDAF5), Color.Hex(0xDDE9FF)),
        1 => (Color.Hex(0xF5CBDE), Color.Hex(0xFFDDED)),
        2 => (Color.Hex(0x0818AD), Color.Hex(0x041EFF)),
        3 => (Color.Hex(0x81009E), Color.Hex(0xD000FF)),
        4 => (Color.Hex(0x980819), Color.Hex(0xF20D27)),
        _ => (Color.Hex(0), Color.Hex(0xEE1C24))
    };
    /// <summary>根据类型、轨道和选中状态在给定矩形内绘制音符。</summary>
    /// <param name="type">来源音符类型编号。</param>
    /// <param name="lane">音符的轨道编号。</param>
    /// <param name="rect">音符占用的绘制矩形。</param>
    /// <param name="selected">当前项是否被选中。</param>
    public void Draw(int type, int lane, Rect rect, bool selected)
    {
        var (fill, edge) = Colors(Palette(type, lane));
        if (selected) edge = Color.White;
        // 合并半透明图层会改变中间 RGBA8 的舍入；界面淡化时仍使用原有五个图元，以保留重叠音符的合成结果。
        if (canvas.PresentationOpacity != 1)
        {
            canvas.Fill(rect, fill);
            canvas.Fill(new(rect.X, rect.Y, rect.W, 1), edge);
            canvas.Fill(new(rect.X, rect.Y + rect.H - 1, rect.W, 1), edge);
            canvas.Fill(new(rect.X, rect.Y, 1, rect.H), edge);
            canvas.Fill(new(rect.X + rect.W - 1, rect.Y, 1, rect.H), edge);
            return;
        }
        if (scaleX != canvas.PixelScaleX || scaleY != canvas.PixelScaleY)
        {
            canvas.Flush();
            scaleX = canvas.PixelScaleX;
            scaleY = canvas.PixelScaleY;
            shader.Vec2("u_pixelScale", scaleX, scaleY);
        }
        canvas.NoteSprite(canvas.White, rect.X, rect.Y, new(0, 0, rect.W, rect.H), fill,
            new(edge.R, edge.G, edge.B, 0), instanceShader: shader);
    }
    /// <inheritdoc/>
    public void Dispose() => shader.Dispose();
}
