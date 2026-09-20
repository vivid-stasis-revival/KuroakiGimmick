using System.Reflection;
using System.Text;
using System.Text.Json;
using KuroakiGimmick.Core;
using StbImageSharp;

namespace KuroakiGimmick.Graphics;

public sealed partial class Fonts
{
    // IBM Plex Sans SC UI artwork is embedded separately from private game assets. An older Assets
    // folder must not turn newly translated controls into replacement characters.
    HelpAtlas? uiRegular, uiBold;
    public bool ChineseInterface { get; private set; }

    public void SetInterfaceLanguage(string language) => ChineseInterface = UiLanguage.Resolve(language) == UiLanguage.Chinese;

    void LoadUiAtlases(GpuDevice gpu)
    {
        uiRegular = Load("ui-sans");
        try { uiBold = Load("ui-sans-bold"); }
        catch { uiRegular.Dispose(); uiRegular = null; throw; }

        HelpAtlas Load(string name)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var metadata = assembly.GetManifestResourceStream($"KuroakiGimmick.Fonts.{name}.json")
                ?? throw new InvalidDataException($"Missing UI font metadata: {name}");
            using var imageStream = assembly.GetManifestResourceStream($"KuroakiGimmick.Fonts.{name}.png")
                ?? throw new InvalidDataException($"Missing UI font artwork: {name}");
            var metrics = JsonSerializer.Deserialize<HelpAtlasMetrics>(metadata)
                ?? throw new InvalidDataException($"Invalid UI font: {name}");
            var image = ImageResult.FromStream(imageStream, ColorComponents.RedGreenBlueAlpha);
            return new(new Texture(gpu, image.Width, image.Height, image.Data, linear: true, mipmaps: true), metrics);
        }
    }

    bool TryUiGlyph(Rune rune, out HelpAtlas atlas, out HelpGlyph glyph)
    {
        // Chinese UI uses Medium at every size. English follows the original
        // sans / mono / CJK path, including its original title and number faces.
        atlas = uiRegular!;
        glyph = null!;
        return ChineseInterface && atlas != null && atlas.Metrics.Glyphs.TryGetValue(rune.ToString(), out glyph!);
    }

    static float UiAdvance(HelpAtlas atlas, HelpGlyph glyph, Rune rune, float size) =>
        (rune.Value is >= '0' and <= '9' ? atlas.Metrics.Glyphs["0"].Advance : glyph.Advance) * size / atlas.Metrics.EmSize;

    void DrawUiGlyph(Canvas canvas, HelpAtlas atlas, HelpGlyph glyph, float x, float y, float size, Color color)
    {
        float scale = size / atlas.Metrics.EmSize;
        if (glyph.Width <= 0 || glyph.Height <= 0) return;
        canvas.Quad(atlas.Texture,
            new(x + glyph.OffsetX * scale, y + glyph.OffsetY * scale, glyph.Width * scale, glyph.Height * scale), color,
            new(glyph.X / atlas.Texture.Width, glyph.Y / atlas.Texture.Height,
                glyph.Width / atlas.Texture.Width, glyph.Height / atlas.Texture.Height));
    }
}
