using System.Text.Json;
using StbImageSharp;

namespace KuroakiGimmick.Core;

/// <summary>所有对象资源共用的文件边界。相对路径必须留在声明的根目录内，包括符号链接目标。</summary>
public static class ResourceFiles
{
    /// <summary>默认允许读取的清单字节数上限。</summary>
    public const long ManifestLimit = 1024 * 1024;
    /// <summary>默认允许的单个图像文件字节数上限。</summary>
    public const long ImageFileLimit = 32 * 1024 * 1024;
    /// <summary>默认允许的图像解码字节数上限。</summary>
    public const long DecodedImageLimit = 128 * 1024 * 1024;
    /// <summary>可以按组件记录后继续加载的资源错误；不在此列的异常属于程序缺陷，必须继续向上抛。</summary>
    /// <param name="ex">捕获的异常。</param>
    /// <returns>属于资源读取或格式错误时为 true。</returns>
    public static bool IsResourceError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException;
    /// <summary>
    /// 把 relative 逐段拼到 root 上并跟随符号链接，任何一段解析后离开 root 都报错。
    /// 先拒绝绝对路径、反斜杠、盘符以及 "." / ".." / 空段；返回解析后的真实文件路径。
    /// 缺文件抛 FileNotFoundException（调用方可据此试下一个候选），越界抛 InvalidDataException（不可当作缺失处理）。
    /// </summary>
    /// <param name="root">允许访问的根目录。</param>
    /// <param name="relative">资源的相对路径。</param>
    /// <returns>通过边界检查的完整路径。</returns>
    /// <exception cref="InvalidDataException">路径不符合相对资源约定，或符号链接逃出根目录。</exception>
    /// <exception cref="FileNotFoundException">所需资源不存在。</exception>
    public static string ContainedFile(string root, string relative) =>
        KenomaGMK.Core.VsbExtraMods.ContainedFile(root, relative);
    /// <summary>按字节上限读取文本；0 字节同样算无效，不把空文件当成缺省配置。</summary>
    /// <param name="path">文本文件路径。</param>
    /// <param name="limit">允许读取的字节上限。</param>
    /// <returns>文件的文本内容。</returns>
    /// <exception cref="InvalidDataException">文件长度不在允许范围内。</exception>
    public static string ReadText(string path, long limit = ManifestLimit)
    {
        long length = new FileInfo(path).Length;
        if (length < 1 || ChartLoadLimits.Exceeds(length, limit))
        {
            throw new InvalidDataException($"Resource must contain 1..{limit} bytes: {path}");
        }
        return File.ReadAllText(path);
    }
}

