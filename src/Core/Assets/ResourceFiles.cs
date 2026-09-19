using System.Text.Json;
using StbImageSharp;

namespace KuroakiGimmick.Core;

/// <summary>所有对象资源共用的文件边界。相对路径必须留在声明的根目录内，包括符号链接目标。</summary>
public static class ResourceFiles
{
    public const long ManifestLimit = 1024 * 1024;
    public const long ImageFileLimit = 32 * 1024 * 1024;
    public const long DecodedImageLimit = 128 * 1024 * 1024;
    /// <summary>可以按组件记录后继续加载的资源错误；不在此列的异常属于程序缺陷，必须继续向上抛。</summary>
    public static bool IsResourceError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException;
    /// <summary>
    /// 把 relative 逐段拼到 root 上并跟随符号链接，任何一段解析后离开 root 都报错。
    /// 先拒绝绝对路径、反斜杠、盘符以及 "." / ".." / 空段；返回解析后的真实文件路径。
    /// 缺文件抛 FileNotFoundException（调用方可据此试下一个候选），越界抛 InvalidDataException（不可当作缺失处理）。
    /// </summary>
    public static string ContainedFile(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':')
            || relative.Split('/').Any(part => part is ".." or "." or ""))
        {
            throw new InvalidDataException("Expected a relative resource path: " + relative);
        }
        var directory = new DirectoryInfo(root);
        string canonicalRoot = directory.ResolveLinkTarget(true)?.FullName ?? directory.FullName;
        string current = canonicalRoot;
        string[] pieces = relative.Split('/');
        for (int index = 0; index < pieces.Length; index++)
        {
            current = Path.Combine(current, pieces[index]);
            FileSystemInfo item = index == pieces.Length - 1 ? new FileInfo(current) : new DirectoryInfo(current);
            if (!item.Exists)
            {
                throw new FileNotFoundException("Missing resource: " + relative, current);
            }
            current = item.ResolveLinkTarget(true)?.FullName ?? item.FullName;
            string local = Path.GetRelativePath(canonicalRoot, current);
            if (Path.IsPathRooted(local) || local == ".." || local.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Resource symlink escapes its declared directory: " + relative);
            }
        }
        if (!File.Exists(current))
        {
            throw new FileNotFoundException("Missing resource: " + relative, current);
        }
        return current;
    }

    /// <summary>按字节上限读取文本；0 字节同样算无效，不把空文件当成缺省配置。</summary>
    public static string ReadText(string path, long limit = ManifestLimit)
    {
        long length = new FileInfo(path).Length;
        if (length < 1 || length > limit)
        {
            throw new InvalidDataException($"Resource must contain 1..{limit} bytes: {path}");
        }
        return File.ReadAllText(path);
    }
}

