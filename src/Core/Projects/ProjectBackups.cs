using System.Security.Cryptography;
using System.Text;

namespace KuroakiGimmick.Core;

internal static partial class ProjectBackups
{
    static void EnsureHiddenRoot(string path)
    {
        string root = Path.Combine(Path.GetDirectoryName(path)!, ".kuroaki");
        Directory.CreateDirectory(root);
        // Unix 隐藏目录由前导点决定；Windows 还必须显式设置 Hidden。
        if (OperatingSystem.IsWindows())
            File.SetAttributes(root, File.GetAttributes(root) | FileAttributes.Hidden);
    }
    internal static string WorkingFolder(string path) => Path.Combine(Path.GetDirectoryName(path)!, ".kuroaki", "projects", Path.GetFileName(path));
    internal static string StorageRoot(string path)
    {
        string full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) full = full.ToUpperInvariant();
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..24];
        return Path.Combine(Paths.LogDirectory, "ProjectHistory", id);
    }
    internal static string Folder(string path) => Path.Combine(StorageRoot(path), "backup", Path.GetFileName(path));

    internal static IEnumerable<string> Files(string path)
    {
        if (File.Exists(path)) yield return path;
        string working = WorkingFolder(path);
        if (Directory.Exists(working))
            foreach (string file in Directory.EnumerateFiles(working, "*", SearchOption.AllDirectories)) yield return file;
        foreach (string file in LegacyFiles(path)) yield return file;
    }

    internal static IEnumerable<string> LegacyFiles(string path)
    {
        string dir = Path.GetDirectoryName(path)!, stem = Path.GetFileName(path)[..^9];
        foreach (string suffix in new[] { ".editor.vsm", ".editor.vsp", ".editor_cgmk_config.json" })
        {
            string file = Path.Combine(dir, stem + suffix);
            if (File.Exists(file)) yield return file;
        }
        foreach (string suffix in new[] { ".editor-assets", ".editor-texts" })
        {
            string folder = Path.Combine(dir, stem + suffix);
            if (Directory.Exists(folder))
                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) yield return file;
        }
    }

    internal static void ArchiveLegacy(string path)
    {
        MigrateHistory(path);
        var files = LegacyFiles(path).ToList();
        files.AddRange(files.Append(path).Select(f => f + ".bak").Where(File.Exists).ToArray());
        if (files.Count == 0) return;
        string root = Path.Combine(StorageRoot(path), "legacy", Path.GetFileName(path), Guid.NewGuid().ToString("N"));
        foreach (string file in files)
        {
            string target = Path.Combine(root, Path.GetRelativePath(Path.GetDirectoryName(path)!, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        // Cross-volume archives must be complete and verified before removing any original.
        foreach (string file in files)
            VerifyFile(file, Path.Combine(root, Path.GetRelativePath(Path.GetDirectoryName(path)!, file)));
        foreach (string file in files)
        {
            VerifyFile(file, Path.Combine(root, Path.GetRelativePath(Path.GetDirectoryName(path)!, file)));
            File.Delete(file);
        }
        string stem = Path.GetFileName(path)[..^9];
        foreach (string suffix in new[] { ".editor-assets", ".editor-texts" })
        {
            string folder = Path.Combine(Path.GetDirectoryName(path)!, stem + suffix);
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
    }

    internal static void Write(string path, IReadOnlyDictionary<string, byte[]> files)
    {
        string transaction = Path.Combine(Path.GetDirectoryName(path)!, ".kuroaki", "tmp", Guid.NewGuid().ToString("N"));
        var targets = files.Keys.ToArray();
        var originals = targets.Select(f => File.Exists(f) ? File.ReadAllBytes(f) : null).ToArray();
        int committed = 0;
        bool removable = true;
        try
        {
            EnsureHiddenRoot(path);
            Directory.CreateDirectory(transaction);
            for (int i = 0; i < targets.Length; i++)
            {
                File.WriteAllBytes(Path.Combine(transaction, i + ".new"), files[targets[i]]);
                if (originals[i] is { } bytes) File.WriteAllBytes(Path.Combine(transaction, i + ".old"), bytes);
            }
            File.WriteAllLines(Path.Combine(transaction, "targets.txt"), targets);
            Create(path);
            for (int i = 0; i < targets.Length; i++)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targets[i])!);
                File.Move(Path.Combine(transaction, i + ".new"), targets[i], true);
                committed++;
            }
        }
        catch
        {
            for (int i = committed - 1; i >= 0; i--)
            {
                try
                {
                    if (originals[i] is { } bytes) File.WriteAllBytes(targets[i], bytes);
                    else File.Delete(targets[i]);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { removable = false; }
            }
            throw;
        }
        finally
        {
            if (removable && Directory.Exists(transaction))
            {
                try { Directory.Delete(transaction, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.Error.WriteLine("[save] " + ex.Message); }
            }
        }
    }

    internal static void Create(string path)
    {
        MigrateHistory(path);
        if (!File.Exists(path)) return;
        string root = Folder(path), token = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(root, ".tmp-" + token);
        try
        {
            foreach (string file in Files(path))
            {
                string target = Path.Combine(temporary, Path.GetRelativePath(Path.GetDirectoryName(path)!, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            Directory.Move(temporary, Path.Combine(root, token));
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    internal static void Prune(string path)
    {
        string root = Folder(path);
        if (!Directory.Exists(root)) return;
        foreach (string folder in Directory.EnumerateDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, Path.GetFileName(path))))
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal).Skip(20))
        {
            try { Directory.Delete(folder, true); }
            catch (IOException ex) { Console.Error.WriteLine("[backup] " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Console.Error.WriteLine("[backup] " + ex.Message); }
        }
    }
}
