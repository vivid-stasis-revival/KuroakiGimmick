using KuroakiGimmick.Core;
using StbImageSharp;

namespace KuroakiGimmick.Graphics;

/// <summary>Lossless shelf packing of complete source images with duplicated borders. Oversized/custom UV images retain standalone textures.</summary>
internal static class NoteTextureAtlas
{
    internal sealed record Source(string File, ImageResult Image);
    internal static (Dictionary<string, Texture> Textures, Dictionary<string, Rect> Regions) Load(GpuDevice gpu, NoteSkinProfile profile)
        => Load(gpu, profile.Frames);
    internal static (Dictionary<string, Texture> Textures, Dictionary<string, Rect> Regions) Load(GpuDevice gpu, IEnumerable<NoteSkinFrame> frames, bool white = false)
    {
        var allFrames = frames.ToArray();
        const int pageSize = 4096, padding = 1;
        var textures = new Dictionary<string, Texture>(StringComparer.Ordinal);
        var regions = new Dictionary<string, Rect>(StringComparer.Ordinal);
        var sources = new List<Source>();
        try
        {
            foreach (var frame in allFrames.GroupBy(f => f.File, StringComparer.Ordinal).Select(g => g.First()))
            {
                using var input = File.OpenRead(frame.File);
                var image = ImageResult.FromStream(input, ColorComponents.RedGreenBlueAlpha);
                if (frame.ExpectedTextureWidth is int ew && image.Width != ew || frame.ExpectedTextureHeight is int eh && image.Height != eh)
                    throw new InvalidDataException($"Note texture size does not match its dump manifest: {Path.GetFileName(frame.File)} is {image.Width}x{image.Height}.");
                bool outside = allFrames.Any(f => f.File == frame.File && (f.UvX < 0 || f.UvY < 0 || f.UvX + f.UvW > 1 || f.UvY + f.UvH > 1));
                if (Environment.GetEnvironmentVariable("KG_NOTE_ATLAS") == "off" || image.Width + 2 > pageSize || image.Height + 2 > pageSize || outside)
                { textures[frame.File] = new(gpu, image.Width, image.Height, image.Data); regions[frame.File] = new(0, 0, 1, 1); }
                else sources.Add(new(frame.File, image));
            }
            if (white) sources.Add(new("@white", new ImageResult { Width = 1, Height = 1, Data = [255, 255, 255, 255] }));
            sources.Sort(static (a, b) => b.Image.Height.CompareTo(a.Image.Height));
            // Allocate only the used page extent (small default skins do not allocate a 64 MiB texture).
            while (sources.Count > 0)
            {
                var placed = new List<(Source Source, int X, int Y)>();
                int x = 0, y = 0, row = 0, usedW = 0, usedH = 0;
                for (int i = 0; i < sources.Count;)
                {
                    var s = sources[i]; int w = s.Image.Width + 2, h = s.Image.Height + 2;
                    if (x + w > pageSize) { y += row; x = 0; row = 0; }
                    if (y + h > pageSize) break;
                    placed.Add((s, x + padding, y + padding)); sources.RemoveAt(i);
                    x += w; row = Math.Max(row, h); usedW = Math.Max(usedW, x); usedH = Math.Max(usedH, y + h);
                }
                var pixels = new byte[checked(usedW * usedH * 4)];
                foreach (var (s, px, py) in placed)
                {
                    for (int yy = -1; yy <= s.Image.Height; yy++)
                    {
                        int sy = Math.Clamp(yy, 0, s.Image.Height - 1);
                        int dst = ((py + yy) * usedW + px) * 4;
                        s.Image.Data.AsSpan(sy * s.Image.Width * 4, s.Image.Width * 4).CopyTo(pixels.AsSpan(dst));
                        s.Image.Data.AsSpan(sy * s.Image.Width * 4, 4).CopyTo(pixels.AsSpan(dst - 4));
                        s.Image.Data.AsSpan((sy * s.Image.Width + s.Image.Width - 1) * 4, 4).CopyTo(pixels.AsSpan(dst + s.Image.Width * 4));
                    }
                }
                var texture = new Texture(gpu, usedW, usedH, pixels);
                foreach (var (s, px, py) in placed)
                {
                    textures[s.File] = texture;
                    regions[s.File] = new(px / (float)usedW, py / (float)usedH, s.Image.Width / (float)usedW, s.Image.Height / (float)usedH);
                }
            }
            return (textures, regions);
        }
        catch { foreach (var texture in textures.Values.Distinct()) texture.Dispose(); throw; }
    }
}
