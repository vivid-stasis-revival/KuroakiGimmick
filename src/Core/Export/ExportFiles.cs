using System.Text;

namespace KuroakiGimmick.Core;

/// <summary>先写同目录临时文件，成功后再发布；失败不会截断原有输出。</summary>
public static class ExportFiles
{
    /// <summary>检查目标是否为链接、目录或禁止覆盖的已有文件。</summary>
    /// <param name="path">输出文件路径。</param>
    /// <param name="overwrite">是否允许替换已有输出文件。</param>
    /// <exception cref="IOException">目标是目录、链接，或已存在且未允许覆盖。</exception>
    public static void Validate(string path, bool overwrite)
    {
        path = Path.GetFullPath(path);
        var entry = new FileInfo(path);
        if (entry.LinkTarget != null || entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked export paths are not supported: " + entry.FullName);
        if (Directory.Exists(path)) throw new IOException("Output is a directory: " + path);
        if (!overwrite && File.Exists(path)) throw new IOException("Output already exists. Enable Overwrite exports in Settings or choose a new filename: " + path);
    }

    /// <summary>复查目标后将临时文件移动到最终路径。</summary>
    /// <param name="temporary">已写好的临时文件。</param>
    /// <param name="path">最终文件路径。</param>
    /// <param name="overwrite">是否允许替换已有输出文件。</param>
    public static void Publish(string temporary, string path, bool overwrite)
    {
        Validate(path, overwrite);
        File.Move(temporary, path, overwrite);
    }

    /// <summary>在目标目录创建唯一临时文件，写入成功后发布并清理残留临时文件。</summary>
    /// <param name="path">最终文件路径。</param>
    /// <param name="overwrite">是否允许替换已有输出文件。</param>
    /// <param name="write">将内容写入临时路径的回调。</param>
    public static void Write(string path, bool overwrite, Action<string> write)
    {
        path = Path.GetFullPath(path);
        Validate(path, overwrite);
        string parent = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(parent);
        string temporary = Path.Combine(parent, ".kuroaki-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            write(temporary);
            Publish(temporary, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>通过同目录临时文件发布指定字节内容。</summary>
    /// <param name="path">输出文件路径。</param>
    /// <param name="bytes">要发布的字节。</param>
    /// <param name="overwrite">是否允许替换已有输出文件。</param>
    public static void WriteBytes(string path, byte[] bytes, bool overwrite) =>
        Write(path, overwrite, temporary => File.WriteAllBytes(temporary, bytes));
    /// <summary>将文本编码为 UTF-8 字节后发布。</summary>
    /// <param name="path">输出文件路径。</param>
    /// <param name="text">要按 UTF-8 写入的文本。</param>
    /// <param name="overwrite">是否允许替换已有输出文件。</param>
    public static void WriteText(string path, string text, bool overwrite) =>
        WriteBytes(path, Encoding.UTF8.GetBytes(text), overwrite);
}
