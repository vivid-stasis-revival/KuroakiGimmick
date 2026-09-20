namespace KuroakiGimmick.Graphics;

public sealed partial class SceneRenderer
{
    // Custom MatrixTrapezoidal sets M[3]=prtrX and M[7]=prtrY. Preserve homogeneous W
    // through rasterization: dividing only the four corners on CPU would distort UVs
    // along the triangle diagonal and mishandle geometry crossing the projection plane.
    internal const string ProxyProjectionVertexSource = """
        #version 330 core
        layout(location=0) in vec2 a_position;
        layout(location=1) in vec2 a_uv;
        layout(location=2) in vec4 a_color;
        uniform vec2 u_resolution;
        uniform vec2 proxyOrigin;
        uniform vec2 proxyScale;
        uniform vec2 proxyRotation;
        uniform vec2 proxyTrapezoid;
        uniform vec2 proxySkew;
        uniform vec2 proxyTranslation;
        out vec2 v_vTexcoord;
        out vec4 v_vColour;
        void main() {
            vec2 p = (a_position - proxyOrigin) * proxyScale;
            p = vec2(p.x * proxyRotation.x - p.y * proxyRotation.y,
                     p.x * proxyRotation.y + p.y * proxyRotation.x);
            float w = 1.0 + dot(p, proxyTrapezoid);
            p = vec2(p.x + p.y * proxySkew.x, p.y + p.x * proxySkew.y);
            p += proxyTranslation * w;
            gl_Position = vec4(2.0 * p.x / u_resolution.x - w,
                               w - 2.0 * p.y / u_resolution.y, 0.0, w);
            v_vTexcoord = a_uv;
            v_vColour = a_color;
        }
        """;
}
