using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;

namespace KuroakiGimmick.Core;

/// <summary>
/// 谱面导出：写出 VSM、VSM + cgmk 配置，或整个谱面文件夹。先 Prepare 出完整计划（含哈希）再 Write，
/// 全程只读源素材、不修改也不安装任何游戏文件；已存在的输出一律拒绝覆盖。
/// </summary>
public static class ChartExport
{
    static readonly StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;
    static readonly HashSet<string> ToolDirectories = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".svn", "__MACOSX", "node_modules", "bin", "obj", "dist", ".vs", ".idea" };
    /// <summary>判断 <paramref name="path"/> 是否等于 <paramref name="directory"/> 或位于其内部；用于阻止导出目标与源目录相互嵌套。</summary>
    public static bool Inside(string path, string directory) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), PathComparison) ||
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, PathComparison);
    static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    static string HashFile(string path, CancellationToken cancellation = default)
    {
        using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024]; int read;
        while ((read = stream.Read(buffer)) > 0)
        { cancellation.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    static void NoLinks(string path)
    {
        // 连父目录一起检查：链接目录下的普通子项同样会把写入引到预定导出根之外。
        FileSystemInfo? item = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path);
        for (; item != null; item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked paths are not exported: " + item.FullName);
    }
    /// <summary>
    /// 校验并归一化输出内相对路径：拒绝绝对路径、. 与 ..、控制字符、Windows 非法字符、结尾空格或点，以及 CON/PRN/LPT1 等保留名。
    /// 目的是让同一份导出结果在 Windows/macOS/Linux 上都能原样解包。
    /// </summary>
    static string Portable(string name)
    {
        name = name.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || name.Split('/').Any(p =>
            p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.') || p.Any(c => c < 32 || "<>:\"|?*".Contains(c))))
            throw new IOException("Non-portable export path: " + name);
        foreach (string part in name.Split('/'))
            if (Regex.IsMatch(part.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new IOException("Reserved portable filename: " + part);
        return name;
    }
    /// <summary>排除系统垃圾文件与编辑器自身产物（.sgv.json、.editor.*、备份、暂存），避免把工作副本混进导出的谱面文件夹。</summary>
    static bool IgnoreFile(string path)
    {
        string name = Path.GetFileName(path);
        return name is ".DS_Store" or "Thumbs.db" || name.StartsWith("._", StringComparison.Ordinal) ||
            name.EndsWith(".sgv.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".editor.vsm", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".editor.vsp", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".editor_cgmk_config.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(".save-journal.", StringComparison.Ordinal) || name.Contains(".tmp-", StringComparison.Ordinal) || name.StartsWith(".kuroaki-", StringComparison.Ordinal);
    }

    /// <summary>
    /// 只做规划：确定每个输出项的字节来源、长度和 SHA-256，并收集需要呈现给用户的警告，不写任何文件。
    /// 任何冲突（同名映射、目标已存在、要覆盖源文件、路径不可移植、含符号链接）都在这里抛出，让 Write 阶段没有意外。
    /// </summary>
    public static ChartExportPlan Prepare(ChartExportInput input, ChartExportKind kind, string destination,
        CancellationToken cancellation = default)
    {
        destination = Path.GetFullPath(destination);
        var warnings = new List<string>();
        var files = new Dictionary<string, ChartExportPlan.Item>(StringComparer.OrdinalIgnoreCase);
        void Bytes(string name, byte[] data)
        {
            name = Portable(name);
            files[name] = new(name, null, data.ToArray(), data.LongLength, Hash(data));
        }
        void Copy(string name, string path, bool replace = false)
        {
            cancellation.ThrowIfCancellationRequested(); name = Portable(name); path = Path.GetFullPath(path);
            if (!File.Exists(path)) throw new FileNotFoundException("Export dependency missing.", path);
            NoLinks(path);
            if (files.TryGetValue(name, out var prior))
            {
                // 同一源文件重复登记是幂等的；不同源落到同一输出名必须报错，否则会静默丢掉一份资源。
                if (prior.Source == path) return;
                if (!replace) throw new IOException("Two resources map to the same output name: " + name);
            }
            var info = new FileInfo(path);
            files[name] = new(name, path, null, info.Length, HashFile(path, cancellation));
            // 20000 个文件 / 20 GiB 的硬上限：防止误选家目录之类的根目录被整个复制。
            if (files.Count > 20000 || files.Values.Sum(f => f.Length) > 20L * 1024 * 1024 * 1024)
                throw new IOException("Chart Folder exceeds 20000 files or 20 GiB. Use a dedicated song folder.");
        }
        string stem = ChartExportInput.Stem(input.Project);
        byte[] configBytes = Encoding.UTF8.GetBytes(input.ConfigText);
        if (kind != ChartExportKind.ChartFolder)
        {
            if (!destination.EndsWith(".vsm", StringComparison.OrdinalIgnoreCase)) destination += ".vsm";
            Bytes(Path.GetFileName(destination), input.VsmBytes);
            if (kind == ChartExportKind.VsmAndConfig)
                Bytes(Path.GetFileNameWithoutExtension(destination) + "_cgmk_config.json", configBytes);
            else warnings.Add("VSM only: window movement and cgmk settings are NOT included.");
            warnings.Add("Images, subtitles, VSV and audio stay external. Keep the original chart resources.");
            if (input.ImageText != null) warnings.Add("Authored VSP and image files are NOT included. Use Chart Folder for imported images.");
            if (input.TextSources?.Count > 0) warnings.Add("Edited subtitle contents are NOT included. Use Chart Folder to include text files.");
        }
        else
        {
            string chart = input.Project.Chart ?? throw new InvalidOperationException("Chart Folder requires an attached VSC/VSB.");
            string root = Path.GetDirectoryName(Path.GetFullPath(chart))!;
            // 目标与源互不嵌套：否则遍历会把正在写出的文件再读回来，或直接污染源谱面目录。
            if (Inside(destination, root) || Inside(root, destination))
                throw new IOException("Choose a NEW folder outside the source chart folder and its parents.");
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Source chart folder missing: " + root);
            NoLinks(root);
            void Walk(string directory)
            {
                // 按 Ordinal 排序遍历，保证同一份源目录每次导出得到同样的文件顺序。
                foreach (string file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
                { if (!IgnoreFile(file)) Copy(Path.GetRelativePath(root, file), file); }
                foreach (string child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
                {
                    if (ToolDirectories.Contains(Path.GetFileName(child)) || Path.GetFileName(child).EndsWith(".editor-assets", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(child).EndsWith(".editor-texts", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(child).StartsWith(".kuroaki-", StringComparison.Ordinal)) continue;
                    NoLinks(child); Walk(child);
                }
            }
            Walk(root);
            var project = input.Project.Copy();
            project.Chart = Path.GetRelativePath(root, chart).Replace('\\', '/');
            project.Gimmick = stem + ".vsm"; project.WindowMotion = stem + "_cgmk_config.json";
            // 任意的歌曲元数据/资源、其它难度和回调保持原样不动。
            // 只有编辑产生的 VSM/配置和显式挂载的图片声明会在副本中被替换。
            Bytes(project.Gimmick, input.VsmBytes); Bytes(project.WindowMotion, configBytes);
            if (input.TextSources is { Count: > 0 })
            {
                project.TextFiles = new(StringComparer.Ordinal);
                if (!input.TextSources.ContainsKey("")) files.Remove(stem + "_text.txt");
                foreach (var pair in input.TextSources)
                {
                    string name = Portable(stem + "_text" + (pair.Key.Length == 0 ? "" : "_" + pair.Key) + ".txt");
                    if (project.TextFiles.Values.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new IOException("Text IDs collide on the target platform.");
                    Bytes(name, Encoding.UTF8.GetBytes(pair.Value)); project.TextFiles.Add(pair.Key, name);
                }
            }
            // 源目录内的资源保持相对路径；目录外的资源改名到 resources/ 下，前缀取完整路径哈希的前 12 位以避免同名冲突。
            string Resource(string path)
            {
                path = Path.GetFullPath(path);
                string name = Inside(path, root) ? Path.GetRelativePath(root, path).Replace('\\', '/') :
                    "resources/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..12].ToLowerInvariant() + "_" + Path.GetFileName(path);
                Copy(name, path); return name;
            }
            if (input.ImageText != null || input.Images != null)
            {
                string body;
                if (input.ImageText != null) body = input.ImageText;
                else
                {
                    string source = input.Images!;
                    if (!File.Exists(source)) throw new FileNotFoundException("VSP dependency missing.", source);
                    NoLinks(source); body = File.ReadAllText(source);
                }
                // VSP 里的图片路径基准：ImagePathsRelativeToVsp 时用 VSP 自身目录，否则用谱面目录。选错基准会把图片解析到别处。
                string imageRoot = input.ImageRoot ?? (input.Project.ImagePathsRelativeToVsp && input.Images != null
                    ? Path.GetDirectoryName(input.Images)! : root);
                // 只改写路径 token，其余源文本逐字保留；重写后路径已相对新的谱面根，因此关闭 ImagePathsRelativeToVsp。
                body = VspDocument.Rewrite(body, token => Resource(Path.GetFullPath(token.Replace('\\', Path.DirectorySeparatorChar), imageRoot)));
                project.Images = stem + ".vsp"; project.ImagePathsRelativeToVsp = false;
                Bytes(project.Images, Encoding.UTF8.GetBytes(body));
            }
            else { project.Images = null; project.ImagePathsRelativeToVsp = false; }
            string? OptionalResource(string? path) => path == null ? null : Resource(path);
            project.Audio = OptionalResource(project.Audio); project.Jacket = OptionalResource(project.Jacket);
            if (input.Project.Audio != null && !Inside(input.Project.Audio, root))
                warnings.Add("Attached external audio is included for Kuroaki; game song metadata is not rewritten.");
            // 这些 manifest 自身可能引用任意文件。对未知 schema 不能声称已经重定基准，因此要求它们本来就在歌曲目录内。
            string? InternalOnly(string? path)
            {
                if (path == null) return null;
                if (!Inside(path, root)) throw new IOException("Move explicitly attached preview manifests/assets into the song folder before Chart Folder export: " + path);
                return Path.GetRelativePath(root, path).Replace('\\', '/');
            }
            project.FxProfile = InternalOnly(project.FxProfile); project.GameUi = InternalOnly(project.GameUi);
            project.GimmickAssets = InternalOnly(project.GimmickAssets); project.GimmickDefinition = InternalOnly(project.GimmickDefinition);
            Bytes("Kuroaki.sgv.json", Encoding.UTF8.GetBytes(AppJson.Serialize(project, ViewerProject.Json)));
            warnings.Add("Copies the source song folder, excluding build/cache/editor backup files. No game/mod installation is changed.");
            warnings.Add("Kuroaki.sgv.json retains E markers. Game compatibility still depends on the target game and required mods.");
            if (chart.EndsWith(".vsb", StringComparison.OrdinalIgnoreCase))
                warnings.Add("VSB stays byte-for-byte unchanged; edited events are written to the external VSM companion.");
        }
        if (input.Notices.Length > 0) warnings.Add($"Current preview has {input.Notices.Length} diagnostic notice(s); export does not certify their game support.");
        if (JsonNode.Parse(input.ConfigText) is JsonObject config &&
            ((config[WindowMotionConfig.EventsKey] as JsonArray)?.Count > 0 || (config[WindowMotionConfig.BindingsKey] as JsonArray)?.Count > 0))
            warnings.Add("Window choreography requires the matching ExtCustomGimmick installation in the game.");
        string baseDir = kind == ChartExportKind.ChartFolder ? destination : Path.GetDirectoryName(destination)!;
        if (kind == ChartExportKind.ChartFolder && (Directory.Exists(destination) || File.Exists(destination)))
            throw new IOException("Chart Folder destination already exists. Choose a new folder name.");
        foreach (var item in files.Values)
        {
            string output = Path.GetFullPath(item.Name, baseDir);
            // ProtectedPaths 是当前会话正在使用的源文件；导出绝不能就地覆盖它们。
            if (input.ProtectedPaths.Any(p => output.Equals(p, PathComparison))) throw new IOException("Refusing to overwrite a source file: " + output);
            if (kind != ChartExportKind.ChartFolder && (File.Exists(output) || Directory.Exists(output)))
                throw new IOException("Output already exists; choose a new filename: " + output);
        }
        return new(kind, destination, files.Values.OrderBy(f => f.Name, StringComparer.Ordinal), warnings);
    }

    /// <summary>先全部写入暂存目录并逐项校验字节，再发布。已存在的文件永不覆盖。</summary>
    public static void Write(ChartExportPlan plan, CancellationToken cancellation = default, Action<string>? progress = null)
    {
        bool folder = plan.Kind == ChartExportKind.ChartFolder;
        string parent = Path.GetDirectoryName(plan.Destination)!;
        Directory.CreateDirectory(parent); NoLinks(parent);
        string stage = Path.Combine(parent, ".kuroaki-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var committed = new List<string>();
        try
        {
            for (int i = 0; i < plan.Files.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested(); var item = plan.Files[i];
                progress?.Invoke($"Export {i + 1}/{plan.Files.Count}: {item.Name}");
                string target = Path.GetFullPath(Portable(item.Name), stage);
                // 即便 Portable 已校验过，落地前再确认一次没有逃出暂存目录。
                if (!Inside(target, stage)) throw new IOException("Export path escaped staging directory.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (item.Data != null) output.Write(item.Data);
                    else
                    {
                        string source = item.Source ?? throw new InvalidDataException("Missing export source.");
                        NoLinks(source);
                        using var input = File.OpenRead(source);
                        byte[] buffer = new byte[128 * 1024]; int read;
                        while ((read = input.Read(buffer)) > 0) { cancellation.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
                    }
                    output.Flush(true);
                }
                // 与 Prepare 阶段记录的长度和哈希比对：源文件在导出途中被改动时整单作废，不提交任何输出。
                if (new FileInfo(target).Length != item.Length || HashFile(target, cancellation) != item.Sha256)
                    throw new IOException("Source changed while exporting; no output committed: " + item.Name);
            }
            cancellation.ThrowIfCancellationRequested();
            if (folder)
            {
                // 同一文件系统内的重命名：已存在的目录既不会被合并，也不会被删除。
                Directory.Move(stage, plan.Destination);
            }
            else
            {
                foreach (var item in plan.Files)
                {
                    string target = Path.Combine(parent, item.Name);
                    if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Output appeared during export: " + target);
                }
                // 两个文件无法一起原子提交。失败时只回滚本次新建的输出。
                foreach (var item in plan.Files)
                {
                    string target = Path.Combine(parent, item.Name);
                    File.Move(Path.Combine(stage, item.Name), target, false); committed.Add(target);
                }
            }
        }
        catch
        {
            foreach (string path in committed)
            {
                // 绝不删除本导出器创建后又被其它进程改动过的文件：哈希不符就原样留下。
                var item = plan.Files.First(f => Path.GetFileName(path) == f.Name);
                try { if (File.Exists(path) && HashFile(path) == item.Sha256) File.Delete(path); } catch (IOException) { }
            }
            throw;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
