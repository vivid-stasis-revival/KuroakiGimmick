using System.Text.Json;
using StbImageSharp;

namespace KuroakiGimmick.Core;

/// <summary>每次对象加载共用一份解码预算；同一纹理被多个 sampler 引用时只计费一次。</summary>
public sealed class ImageResourceBudget
{
    private readonly Dictionary<string, (int Width, int Height)> checkedImages = new(StringComparer.Ordinal);
    private long decodedBytes;
    public (int Width, int Height) Check(string path)
    {
        if (checkedImages.TryGetValue(path, out var known))
        {
            return known;
        }
        if (new FileInfo(path).Length > ResourceFiles.ImageFileLimit)
        {
            throw new InvalidDataException("Image exceeds 32 MiB: " + path);
        }
        using var stream = File.OpenRead(path);
        var info = ImageInfo.FromStream(stream) ?? throw new InvalidDataException("Invalid image: " + path);
        if (info.Width is < 1 or > 4096 || info.Height is < 1 or > 4096)
        {
            throw new InvalidDataException("Image dimensions must be 1..4096: " + path);
        }
        long bytes = (long) info.Width * info.Height * 4;
        if (decodedBytes + bytes > ResourceFiles.DecodedImageLimit)
        {
            throw new InvalidDataException("Object images exceed the 128 MiB decoded budget.");
        }
        decodedBytes += bytes;
        return checkedImages[path] = (info.Width, info.Height);
    }
}

