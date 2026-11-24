// 将单个工程的旧历史移入应用数据目录，在校验副本后清理对应源条目。

using System.Security.Cryptography;

namespace KuroakiGimmick.Core;

internal static partial class ProjectBackups
{
    // 仅迁移指定工程的历史，工作文件与待完成的保存事务仍随工程移动。
    internal static void MigrateHistory(string path)
    {
        string hidden = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".kuroaki");
        foreach (string kind in new[] { "backup", "legacy" })
        {
            string parent = Path.Combine(hidden, kind), source = Path.Combine(parent, Path.GetFileName(path));
            if (!Directory.Exists(source)) continue;
            RejectLink(hidden);
            RejectLink(parent);
            RejectLink(source);
            string destination = Path.Combine(StorageRoot(path), kind, Path.GetFileName(path));
            foreach (string entry in Directory.GetFileSystemEntries(source))
            {
                RejectLink(entry);
                string target = Path.Combine(destination, Path.GetFileName(entry));
                if (Directory.Exists(entry)) MigrateSnapshot(entry, target);
                else
                {
                    Directory.CreateDirectory(destination);
                    if (Directory.Exists(target) || File.Exists(target) && !SameFile(entry, target))
                        target += "-migrated-" + Guid.NewGuid().ToString("N");
                    if (!File.Exists(target)) File.Copy(entry, target);
                    VerifyFile(entry, target);
                    File.Delete(entry);
                }
            }
            DeleteIfEmpty(source);
            DeleteIfEmpty(parent);
        }
    }

    static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Refusing to migrate linked history: " + path);
    }

    // 除文件哈希外还要核对空目录；枚举期间禁止沿链接进入其他目录。
    static Dictionary<string, string?> Snapshot(string root)
    {
        RejectLink(root);
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        void Visit(string directory)
        {
            foreach (string entry in Directory.GetFileSystemEntries(directory))
            {
                RejectLink(entry);
                string relative = Path.GetRelativePath(root, entry);
                if (Directory.Exists(entry))
                {
                    result.Add(relative, null);
                    Visit(entry);
                }
                else result.Add(relative, FileHash(entry));
            }
        }
        Visit(root);
        return result;
    }

    static string FileHash(string path)
    {
        RejectLink(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    static bool SameFile(string source, string target) => FileHash(source) == FileHash(target);
    static void VerifyFile(string source, string target)
    {
        if (!SameFile(source, target)) throw new IOException("History verification failed; original retained: " + source);
    }
    static bool Matches(string root, Dictionary<string, string?> expected)
    {
        var actual = Snapshot(root);
        return actual.Count == expected.Count && expected.All(pair => actual.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }

    static void MigrateSnapshot(string source, string target)
    {
        var snapshot = Snapshot(source);
        if (File.Exists(target) || Directory.Exists(target) && !Matches(target, snapshot))
            target += "-migrated-" + Guid.NewGuid().ToString("N");
        if (!Directory.Exists(target))
        {
            string staging = target + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(staging);
                foreach (var (relative, hash) in snapshot)
                {
                    string copy = Path.Combine(staging, relative);
                    if (hash == null) Directory.CreateDirectory(copy);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                        File.Copy(Path.Combine(source, relative), copy);
                    }
                }
                if (!Matches(staging, snapshot) || !Matches(source, snapshot))
                    throw new IOException("History changed or failed verification; original retained: " + source);
                // 此处在同一应用数据卷内重命名已完成的历史快照。
                Directory.Move(staging, target);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }
        if (!Matches(source, snapshot) || !Matches(target, snapshot))
            throw new IOException("History verification failed; original retained: " + source);
        // 仅删除空目录，使并发新增的文件留在原处。
        foreach (var (relative, hash) in snapshot.Where(pair => pair.Value != null))
        {
            string original = Path.Combine(source, relative);
            VerifyFile(original, Path.Combine(target, relative));
            File.Delete(original);
        }
        foreach (string directory in snapshot.Where(pair => pair.Value == null).Select(pair => pair.Key).OrderByDescending(p => p.Length))
            Directory.Delete(Path.Combine(source, directory));
        Directory.Delete(source);
    }

    static void DeleteIfEmpty(string directory)
    {
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
}
