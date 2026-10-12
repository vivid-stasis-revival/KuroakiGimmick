using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class SceneRenderer
{
    Dictionary<string, Texture>? judgmentAtlas;
    readonly Dictionary<Texture, (Texture Texture, Rect Uv)> judgmentRegions = [];
    Rect judgmentWhiteUv;
    Texture? judgmentWhite;
    bool judgmentSingleTexture;
    readonly Dictionary<(double Time, int Lane, ScoreState.HitKind Kind, double Lead), (int Slot, int Count)> judgmentTemplates = [];
    readonly List<float[]> judgmentTemplateBuffers = [];

    void BuildJudgmentAtlas(GpuDevice gpu)
    {
        if (canvas.NoteInstances == null || Environment.GetEnvironmentVariable("KG_JUDGMENT_INSTANCING") == "off"
            || Environment.GetEnvironmentVariable("KG_NOTE_ATLAS") == "off") return;
        var sources = new List<NoteSkinFrame>();
        foreach (string name in Enumerable.Range(0, 4).Select(i => "pt_diamonddust_" + i).Concat(Enumerable.Range(0, 5).Select(i => "sp_noteparticle_" + i)))
            sources.Add(new() { Key = name, File = Path.Combine(Paths.Assets, "GameCommon", name + ".png"), LocalX = 0, LocalY = 0, Width = 1, Height = 1 });
        var (textures, regions) = NoteTextureAtlas.Load(gpu, sources, white: true);
        judgmentAtlas = textures; judgmentWhite = textures["@white"]; judgmentWhiteUv = regions["@white"];
        for (int i = 0; i < dustTextures.Length; i++)
        { string file = sources[i].File; judgmentRegions[dustTextures[i]] = (textures[file], regions[file]); }
        for (int i = 0; i < noteParticles.Length; i++)
        { string file = sources[dustTextures.Length + i].File; judgmentRegions[noteParticles[i]] = (textures[file], regions[file]); }
        judgmentSingleTexture = judgmentRegions.Values.All(r => ReferenceEquals(r.Texture, judgmentWhite));
    }
    void JudgmentFill(Rect r, Color color)
    {
        if (judgmentWhite == null) { canvas.Fill(r, color); return; }
        canvas.NoteSprite(judgmentWhite, r.X, r.Y, new(0, 0, r.W, r.H), color, judgmentWhiteUv);
    }
    void JudgmentQuad(Texture source, Rect r, Color color, float angle = 0)
    {
        if (!judgmentRegions.TryGetValue(source, out var region)) { canvas.Quad(source, r, color, angle: angle); return; }
        if (angle == 0) canvas.NoteSprite(region.Texture, r.X, r.Y, new(0, 0, r.W, r.H), color, region.Uv);
        else
        {
            var (sine, cosine) = MathF.SinCos(angle * MathF.PI / 180);
            canvas.NoteSprite(region.Texture, r.X + r.W / 2, r.Y + r.H / 2, new(-r.W / 2, -r.H / 2, r.W, r.H), color, region.Uv, cosine, sine);
        }
    }
}
