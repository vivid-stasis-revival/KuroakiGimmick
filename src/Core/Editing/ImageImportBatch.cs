using System.Security.Cryptography;
using StbImageSharp;

namespace KuroakiGimmick.Core.Editing;

/// <summary>
/// 导入图片的私有不可变副本。原文件随后被改动或删除都不影响本次编辑，SHA-256 在保存时复核暂存件未被外部篡改。
/// 撤销栈可能一直引用这些文件，所以要留到整个编辑会话释放为止才能删。
/// </summary>
public sealed class ImageImportBatch : IDisposable
{
    /// <summary>OriginalPath 只用来起名与报错，真正被读取和写入 VSP 的始终是 StagedPath。Width/Height 是解码后的实际像素尺寸。</summary>
    public sealed record Image(string OriginalPath, string StagedPath, string Sha256, int Width, int Height);
    readonly List<Image> images = [];
    public IReadOnlyList<Image> Images => images;
    public string DirectoryPath { get; }
    ImageImportBatch(string path) => DirectoryPath = path;
    /// <summary>只认这几种静态位图。判断只看扩展名，真正的格式与完整性在 <see cref="Prepare"/> 里靠解码确认。</summary>
    public static bool Accepts(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga";
    /// <summary>
    /// 把选中的图片逐一复制到日志目录下的一次性暂存文件夹，并在此处做完全部校验。
    /// 上限从严：单批 1..32 张、单文件 64 MiB、边长 16384 px、单张解码后 64 MiB、整批解码后 256 MiB。
    /// 任何一张失败都会连整批一起 Dispose，不留半截目录。
    /// </summary>
    public static ImageImportBatch Prepare(IEnumerable<string> paths)
    {
        string[] selected = paths.Take(33).ToArray();
        if (selected.Length is < 1 or > 32) throw new ArgumentException("Import between 1 and 32 static images at a time.");
        string folder = Path.Combine(Paths.LogDirectory, "image-imports", Guid.NewGuid().ToString("N"));
        // VSP 是无转义的 CSV，暂存路径里出现逗号或换行就没法写进声明行，因此在建目录之前先挡住。
        if (folder.IndexOfAny([',', '\r', '\n']) >= 0) throw new IOException("Image cache path cannot be represented in VSP CSV.");
        Directory.CreateDirectory(folder);
        var batch = new ImageImportBatch(folder);
        try
        {
            long total = 0;
            foreach (string entry in selected)
            {
                if (!Accepts(entry)) throw new ArgumentException("Use static PNG, JPEG, BMP or TGA images: " + entry);
                string path = Path.GetFullPath(entry);
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length is < 1 or > 64 * 1024 * 1024) throw new InvalidDataException("Image exceeds 64 MiB or is empty: " + path);
                string staged = Path.Combine(folder, batch.images.Count + Path.GetExtension(path).ToLowerInvariant());
                // CreateNew 而非 Create：暂存名重复说明有别的东西在写这个一次性目录，宁可报错也不覆盖。
                using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                // 后续的读取、解码与哈希一律针对暂存副本，不再回头碰原文件——原文件在这中间被改动也不会影响本批结果。
                using var stream = File.OpenRead(staged);
                var info = ImageInfo.FromStream(stream) ?? throw new InvalidDataException("Unsupported or corrupt image: " + path);
                long bytes = (long)info.Width * info.Height * 4;
                if (info.Width is < 1 or > 16384 || info.Height is < 1 or > 16384 || bytes > 64L * 1024 * 1024)
                    throw new InvalidDataException("Image exceeds 16384 px or 64 MiB decoded: " + path);
                total += bytes;
                if (total > 256L * 1024 * 1024) throw new InvalidDataException("Image batch exceeds 256 MiB decoded.");
                stream.Position = 0;
                // 落定之前必须真的解一遍像素，不能只看文件头：像素流损坏的图片一旦生成了事件，只会在渲染时才崩。
                ImageResult decoded;
                try { decoded = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { throw new InvalidDataException("Image pixel decode failed: " + path, ex); }
                if (decoded.Width != info.Width || decoded.Height != info.Height || decoded.Data.LongLength != bytes)
                    throw new InvalidDataException("Inconsistent decoded image dimensions: " + path);
                stream.Position = 0;
                string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                batch.images.Add(new(path, staged, hash, info.Width, info.Height));
            }
            return batch;
        }
        catch { batch.Dispose(); throw; }
    }
    /// <summary>删掉整个一次性暂存目录。清理失败只写一行提示，不抛异常——释放路径上再抛会盖掉真正的错误。</summary>
    public void Dispose()
    {
        try { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Console.Error.WriteLine("Image cache cleanup: " + ex.Message); }
    }
}
