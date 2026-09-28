using System.Text;

namespace KuroakiGimmick.Core;

/// <summary>先写同目录临时文件，成功后再发布；失败不会截断原有输出。</summary>
public static class ExportFiles
{
    public static void Validate(string path, bool overwrite)
    {
        path = Path.GetFullPath(path);
        var entry = new FileInfo(path);
        if (entry.LinkTarget != null || entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked export paths are not supported: " + entry.FullName);
        if (Directory.Exists(path)) throw new IOException("Output is a directory: " + path);
        if (!overwrite && File.Exists(path)) throw new IOException("Output already exists. Enable Overwrite exports in Settings or choose a new filename: " + path);
    }

    public static void Publish(string temporary, string path, bool overwrite)
    {
        Validate(path, overwrite);
        File.Move(temporary, path, overwrite);
    }

    public static void Write(string path, bool overwrite, Action<string> write)
    {
        path = Path.GetFullPath(path);
        Validate(path, overwrite);
        string parent = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(parent);
        string temporary = Path.Combine(parent, ".kuroaki-export-" + Guid.NewGuid().ToString("N"));
        try { write(temporary); Publish(temporary, path, overwrite); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void WriteBytes(string path, byte[] bytes, bool overwrite) =>
        Write(path, overwrite, temporary => File.WriteAllBytes(temporary, bytes));
    public static void WriteText(string path, string text, bool overwrite) =>
        WriteBytes(path, Encoding.UTF8.GetBytes(text), overwrite);
}
