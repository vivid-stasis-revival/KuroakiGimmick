// 提供 proxy 顶点投影使用的 shader 源。

namespace KuroakiGimmick.Graphics;

public sealed partial class SceneRenderer
{
    // Custom 的 MatrixTrapezoidal 将 prtrX 与 prtrY 写入 M[3]、M[7]。将齐次 W 保留到光栅化阶段，避免仅在 CPU 对四角做除法而扭曲对角线 UV 或错误裁剪跨投影平面的图元。
    internal static readonly string ProxyProjectionVertexSource = ProxyVertexSource(false);
    internal static readonly string ProxyGuideVertexSource = ProxyVertexSource(true);
    static string ProxyVertexSource(bool authoring) => """
        #version 330 core
        layout(location=0) in vec2 a_position;
        layout(location=1) in vec2 a_uv;
        layout(location=2) in vec4 a_color;
        uniform vec2 u_resolution;
        uniform vec4 proxyRowX;
        uniform vec4 proxyRowY;
        uniform vec4 proxyRowW;
        """ + (authoring ? "\nuniform vec4 guideViewport;\n" : "\n") + """
        out vec2 v_vTexcoord;
        out vec4 v_vColour;
        void main() {
            vec4 source = vec4(a_position, 0.0, 1.0);
            vec2 p = vec2(dot(source, proxyRowX), dot(source, proxyRowY));
            float w = dot(source, proxyRowW);
        """ + (authoring ? "\np = p * guideViewport.zw + guideViewport.xy * w;\n" : "\n") + """
            gl_Position = vec4(2.0 * p.x / u_resolution.x - w,
                               w - 2.0 * p.y / u_resolution.y, 0.0, w);
            v_vTexcoord = a_uv;
            v_vColour = a_color;
        }
        """;
}
