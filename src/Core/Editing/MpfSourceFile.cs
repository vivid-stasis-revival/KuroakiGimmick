using System.Text;

namespace KuroakiGimmick.Core.Editing;

/// <summary>An explicit external MPF file edit, separate from the project's VSM/VSP undo history.</summary>
internal sealed class MpfSourceFile
{
    const int MaximumBytes = 1024 * 1024;
    readonly Encoding encoding;
    readonly byte[] preamble;
    byte[]? expected;
    internal string Path { get; }
    internal SourceTextBuffer Buffer { get; }
    internal bool Exists => expected != null;
    internal string? LastBackup { get; private set; }

    internal MpfSourceFile(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        if (!System.IO.Path.GetExtension(Path).Equals(".vsmpf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select a .vsmpf source file.");
        if (!File.Exists(Path))
        {
            encoding = new UTF8Encoding(false, true); preamble = [];
            Buffer = new("::Init::\n\n::Main::\n\n::Finish::\n"); return;
        }
        if (new FileInfo(Path).Length > MaximumBytes) throw new InvalidDataException("MPF source exceeds 1 MiB.");
        expected = File.ReadAllBytes(Path);
        if (expected.Length > MaximumBytes) throw new InvalidDataException("MPF source exceeds 1 MiB.");
        using var reader = new StreamReader(new MemoryStream(expected), new UTF8Encoding(false, true), true);
        _ = reader.ReadToEnd();
        encoding = (Encoding)reader.CurrentEncoding.Clone();
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        byte[] bom = encoding.GetPreamble();
        preamble = expected.AsSpan().StartsWith(bom) ? bom : [];
        encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        Buffer = new(encoding.GetString(expected, preamble.Length, expected.Length - preamble.Length));
    }

    void VerifyUnchanged()
    {
        if (File.Exists(Path))
        {
            var attributes = File.GetAttributes(Path);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0)
                throw new IOException("Cannot overwrite a linked or read-only MPF file: " + Path);
            if (expected == null || new FileInfo(Path).Length != expected.Length
                || !File.ReadAllBytes(Path).AsSpan().SequenceEqual(expected))
                throw new IOException("MPF file changed outside the editor. Reopen it before saving: " + Path);
        }
        else if (expected != null) throw new IOException("MPF file was removed outside the editor: " + Path);
    }

    internal bool Save(string? historyRoot = null)
    {
        // Validate before touching any file. Runtime-only failures still surface in the rebuilt preview.
        if (Buffer.Text.Length > MaximumBytes) throw new InvalidDataException("MPF source exceeds 1 MiB.");
        byte[] body = encoding.GetBytes(Buffer.Text);
        if ((long)body.Length + preamble.Length > MaximumBytes) throw new InvalidDataException("MPF source exceeds 1 MiB.");
        _ = MpfScriptLanguage.Parse(Buffer.Text);
        byte[] bytes = [.. preamble, .. body];
        VerifyUnchanged();
        if (expected != null && bytes.AsSpan().SequenceEqual(expected)) { Buffer.MarkSaved(); return false; }
        string directory = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(directory);
        string temporary = System.IO.Path.Combine(directory, "." + System.IO.Path.GetFileName(Path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (expected != null)
            {
                string backup = System.IO.Path.Combine(historyRoot ?? ProjectBackups.StorageRoot(Path), "mpf", System.IO.Path.GetFileName(Path),
                    DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N") + ".vsmpf");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup)!);
                File.WriteAllBytes(backup, expected);
                if (!File.ReadAllBytes(backup).AsSpan().SequenceEqual(expected)) throw new IOException("MPF backup verification failed.");
                LastBackup = backup;
            }
            VerifyUnchanged();
            File.Move(temporary, Path, expected != null);
            expected = bytes; Buffer.MarkSaved(); return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
