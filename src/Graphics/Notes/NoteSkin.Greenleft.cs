using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NoteSkin
{
    internal const string GreenleftStraightSource = """
        #version 330 core
        in vec2 v_vTexcoord; in vec4 v_vColour;
        uniform sampler2D gm_BaseTexture;
        out vec4 fragColor;
        void main() { vec4 c=texture(gm_BaseTexture,v_vTexcoord); fragColor=c.a>0.0?vec4(c.rgb/c.a,c.a):vec4(0.0); }
        """;
    readonly Dictionary<string, SkinFrame> greenleftSources = [];
    GimmickDefinition? greenleftDefinition;
    SkinFrame GreenleftSource(string name)
    {
        if (greenleftSources.TryGetValue(name, out var cached)) return cached;
        string root = Path.Combine(Paths.Assets, "Gimmicks", "obj_starcrashers_gimmick");
        greenleftDefinition ??= GimmickDefinitionReader.Read(File.ReadAllText(Path.Combine(root, "manifest.json")));
        if (!greenleftDefinition.Sprites.TryGetValue(name, out var spec) || spec.Frames.Count == 0)
            throw new InvalidDataException("CGMK greenleft sprite is missing: " + name);
        string file = ResourceFiles.ContainedFile(root, spec.Frames[0]);
        new ImageResourceBudget().Check(file);
        var texture = Texture.Load(canvas.Gpu, file);
        if (texture.Width != spec.Width || texture.Height != spec.Height)
        {
            texture.Dispose(); throw new InvalidDataException("CGMK greenleft sprite dimensions differ from manifest: " + name);
        }
        var frame = new NoteSkinFrame { Key = name, File = file, Width = spec.Width, Height = spec.Height,
            LocalX = -spec.OriginX, LocalY = -spec.OriginY };
        return greenleftSources[name] = new(frame, texture, new(0,0,1,1));
    }
    void DisposeGreenleftSources()
    {
        ClearGreenleft();
        foreach (var frame in greenleftSources.Values) frame.Texture.Dispose();
        greenleftSources.Clear(); greenleftDefinition = null;
    }
    readonly SkinFrame?[] greenleftFrames = new SkinFrame?[4];
    readonly List<Target> greenleftTargets = [];
    Shader? greenleftStraight;
    bool greenleftActive;
    double greenleftAmount = -1;
    void ClearGreenleft()
    {
        canvas.Flush();
        foreach (var target in greenleftTargets) target.Dispose();
        greenleftTargets.Clear();
        Array.Clear(greenleftFrames);
        greenleftActive = false; greenleftAmount = -1;
    }
    // Bake before opening the scene/field pass. The source uses normal frames as
    // its canvas, centers Allison, and only rebuilds on round(alpha*255) changes.
    public void PrepareGreenleft(Session session, double time)
    {
        var state = session.Timeline.IsCustomGimmick ? PartialSkinBlendState.At(session.Timeline, time) : default;
        if (!state.Active)
        {
            if (greenleftActive) ClearGreenleft();
            return;
        }
        if (greenleftActive && greenleftAmount == state.Alpha) return;
        ClearGreenleft();
        string[] kinds = ["chip", "hold", "bumper", "bumper_timing"];
        var normal = kinds.Select(k => GreenleftSource("sp_note_" + k + "_normal")).ToArray();
        var overlay = kinds.Select(k => GreenleftSource("sp_note_" + k + "_allison")).ToArray();
        greenleftStraight ??= new(canvas.Gpu, Canvas.VertexSource, GreenleftStraightSource);
        for (int i = 0; i < 4; i++)
        {
            if (normal[i] is not { } n || overlay[i] is not { } a)
                throw new InvalidDataException("CGMK greenleft skin frame is missing.");
            if (state.Direct) { greenleftFrames[i] = a; continue; }
            int width = (int)n.Frame.Width, height = (int)n.Frame.Height;
            using var bake = new Target(canvas.Gpu, width, height);
            canvas.Begin(bake, width, height, width, height, new(0,0,0,0));
            canvas.Quad(n.Texture, new(0,0,width,height), Color.White, n.Uv);
            canvas.Quad(a.Texture, new((width-a.Frame.Width)/2, (height-a.Frame.Height)/2,a.Frame.Width,a.Frame.Height),
                Color.White.Alpha(state.Alpha), a.Uv);
            var result = new Target(canvas.Gpu, width, height);
            greenleftTargets.Add(result);
            canvas.Pass(result, bake.Texture, greenleftStraight, preserveAlpha: true);
            // Ensure the temporary texture survives all submitted commands.
            canvas.Gpu.Submit();
            greenleftFrames[i] = new(n.Frame, result.Texture, new(0,0,1,1));
        }
        greenleftAmount = state.Alpha; greenleftActive = true;
    }
}
