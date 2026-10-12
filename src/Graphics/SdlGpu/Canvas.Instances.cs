using System.Runtime.InteropServices;

namespace KuroakiGimmick.Graphics;

public sealed partial class Canvas
{
    bool capturingInstances;
    internal void StartInstanceCapture(Texture texture)
    {
        Flush(); Select(texture, NoteInstances); capturingInstances = true;
    }
    internal int FinishInstanceCapture(ref float[] buffer)
    {
        capturingInstances = false;
        int count = vertices.Count;
        if (buffer.Length < count) Array.Resize(ref buffer, Math.Max(count, Math.Max(64, buffer.Length * 2)));
        CollectionsMarshal.AsSpan(vertices).CopyTo(buffer); vertices.Clear();
        return count;
    }
    internal void AppendInstances(Texture texture, ReadOnlySpan<float> data)
    {
        if (data.IsEmpty) return;
        Select(texture, NoteInstances); vertices.AddRange(data);
        if (vertices.Count >= 16 * 8192) Flush();
    }
    // One 64-byte record per sprite, rather than six 32-byte vertices.
    // All backends consume the same four vec4 instance attributes. instance_step_rate stays zero (SDL reserved).
    internal const string NoteInstanceVertexSource = """
        #version 450
        layout(location=0) in vec4 a_origin;
        layout(location=1) in vec4 a_sizeRotation;
        layout(location=2) in vec4 a_uvRect;
        layout(location=3) in vec4 a_color;
        uniform vec2 u_resolution;
        out vec2 v_vTexcoord;out vec4 v_vColour;
        void main(){
            const vec2 corners[6]=vec2[6](vec2(0,0),vec2(1,0),vec2(0,1),vec2(0,1),vec2(1,0),vec2(1,1));
            vec2 corner=corners[gl_VertexIndex];
            vec2 local=a_origin.zw+corner*a_sizeRotation.xy;
            float c=a_sizeRotation.z,s=a_sizeRotation.w;
            vec2 world=vec2(local.x*c-local.y*s,local.x*s+local.y*c)+a_origin.xy;
            vec2 p=world/u_resolution*2.-1.;p.y=-p.y;
            gl_Position=vec4(p,0,1);v_vTexcoord=a_uvRect.xy+corner*a_uvRect.zw;v_vColour=a_color;
        }
        """;
    internal const string NoteInstanceFragmentSource = "#version 450\nin vec2 v_vTexcoord;in vec4 v_vColour;uniform sampler2D gm_BaseTexture;out vec4 fragColor;void main(){fragColor=texture(gm_BaseTexture,v_vTexcoord)*v_vColour;}";

    internal void NoteSprite(Texture texture, float x, float y, Rect local, Color color, Rect uv, float cosine = 1, float sine = 0, Shader? instanceShader = null)
    {
        if (color.A <= 0 || local.W == 0 || local.H == 0) return;
        if (instanceShader == null && NoteInstances == null)
        {
            System.Numerics.Vector2 P(float px, float py) => new(x + px * cosine - py * sine, y + px * sine + py * cosine);
            Polygon(texture, P(local.X, local.Y), P(local.X + local.W, local.Y), P(local.X, local.Y + local.H), P(local.X + local.W, local.Y + local.H), color, uv);
            return;
        }
        Select(texture, instanceShader ?? NoteInstances);
        int offset = vertices.Count;
        // SetCount + one span write avoids sixteen List.Add capacity checks per instance.
        if (vertices.Capacity < offset + 16) vertices.Capacity = Math.Max(offset + 16, checked(vertices.Capacity * 2));
        CollectionsMarshal.SetCount(vertices, offset + 16);
        Span<float> data = CollectionsMarshal.AsSpan(vertices).Slice(offset, 16);
        data[0] = x; data[1] = y; data[2] = local.X; data[3] = local.Y;
        data[4] = local.W; data[5] = local.H; data[6] = cosine; data[7] = sine;
        data[8] = uv.X; data[9] = uv.Y; data[10] = uv.W; data[11] = uv.H;
        data[12] = color.R; data[13] = color.G; data[14] = color.B; data[15] = color.A * presentationOpacity;
        // Bound the temporary Canvas stream; GpuDevice preserves contiguous ordering when merging these chunks.
        if (!capturingInstances && vertices.Count >= 16 * 8192) Flush();
    }
}
