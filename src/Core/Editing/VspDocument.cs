// 维护 VSP 源文本与图片声明，并在保存时重定位已暂存的图片引用。

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KuroakiGimmick.Core.Editing;

/// <summary>
/// 保源的 VSP 扩展。已有声明原封不动；导入的实例一律放进专用图层，不借用作者已有的图层名。
/// 改写只针对目标 token，其余文本逐字保留。
/// </summary>
public sealed class VspDocument
{
    /// <summary>当前编辑后的 VSP 源文本。</summary>
    public string Text { get; private set; }
    /// <summary>解析图片引用使用的根目录。</summary>
    public string ResourceRoot { get; }
    /// <summary>当前图片声明文档的来源位置。</summary>
    public string SourcePath { get; }
    /// <summary>当前 VSP 文本是否含有非空白字符。</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Text);
    readonly Dictionary<string, ImageImportBatch.Image> staged = new(StringComparer.Ordinal);
    /// <summary>文档写入新增行时使用的换行符。</summary>
    public string NewLine { get; }
    /// <summary>换行符按源文件里第一种出现的形式记下来，后续追加的行一律沿用它，不在同一份文件里混用两种换行。</summary>
    /// <param name="text">VSP 源文本。</param>
    /// <param name="root">图片资源根目录。</param>
    /// <param name="source">可选的来源文件位置。</param>
    public VspDocument(string text, string root, string source)
    {
        Text = text;
        ResourceRoot = Path.GetFullPath(root);
        SourcePath = source;
        NewLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\r') ? "\r" : "\n";
    }
    /// <summary>已挂载但文件缺失时抛错，绝不用空文档顶替；16 MiB 上限先于读取生效。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <returns>由会话持有的图片源构建的编辑文档。</returns>
    /// <exception cref="FileNotFoundException">已关联的 VSP 文件缺失。</exception>
    /// <exception cref="InvalidDataException">VSP 超过允许的读取上限。</exception>
    public static VspDocument FromSession(Session session)
    {
        string root = SongFiles.Root(session.Project) ?? Path.GetDirectoryName(session.Images.Path ?? "") ?? Paths.Output;
        string source = session.Images.Path ?? Path.Combine(root, ChartExportInput.Stem(session.Project) + ".vsp");
        // ImagePathsRelativeToVsp 决定图片路径的解析基准是 VSP 自身目录还是歌曲目录，选错会把图片指到别处。
        if (session.Project.ImagePathsRelativeToVsp && session.Images.Path != null) root = Path.GetDirectoryName(session.Images.Path)!;
        if (session.Project.Images != null && !File.Exists(source))
            throw new FileNotFoundException("Attached VSP is missing; refusing to replace it with an empty document.", source);
        if (File.Exists(source) && ChartLoadLimits.Exceeds(new FileInfo(source).Length, 16 * 1024 * 1024)) throw new InvalidDataException("VSP exceeds 16 MiB.");
        return new(File.Exists(source) ? File.ReadAllText(source) : "", root, source);
    }
    /// <summary>撤销/重做时整份文本回滚到旧快照；staged 里的暂存图片不清除，因为回滚后还可能再被重做引用。</summary>
    /// <param name="text">要恢复的 VSP 源文本。</param>
    public void Restore(string text) => Text = text;
    /// <summary>新建 ID 时按大小写无关判重，避免产生只有大小写不同的 ID。</summary>
    /// <returns>所有已声明图片的 ID 集合。</returns>
    public HashSet<string> Ids() => new(DeclaredIds(), StringComparer.OrdinalIgnoreCase);

    internal IEnumerable<string> DeclaredIds()
    {
        string section = "";
        foreach (Match row in Regex.Matches(Text, @"[^\r\n]+"))
        {
            string line = row.Value.Trim();
            if (line.StartsWith('#')) section = line;
            else if (section == "#Image" && !line.EndsWith(':') && ImageDeclarationId(line) is { } id) yield return id;
        }
    }

    static string? ImageDeclarationId(string line)
    {
        string[] fields = line.Split(',', 3, StringSplitOptions.TrimEntries);
        return fields.Length >= 2 && fields[0] is ("static" or "animated") && fields[1].Length > 0 ? fields[1] : null;
    }
    /// <summary>
    /// 暂存图片的原始文件名快照。Chart Folder 导出只拿这份不可变映射恢复用户拖进来的名字，
    /// 不把 image-imports/0.png 或 .editor-assets/哈希.png 之类编辑器内部路径泄漏到成品 VSP。
    /// </summary>
    /// <returns>用于保存或导出的原始图片文件名映射。</returns>
    public IReadOnlyDictionary<string, string> OriginalResourceNames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in staged) result[Path.GetFullPath(pair.Key)] = OriginalFileName(pair.Value);
        return result;
    }
    static string OriginalFileName(ImageImportBatch.Image image)
    {
        string name = Path.GetFileName(image.OriginalPath);
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny([',', '\r', '\n']) >= 0)
            throw new IOException("Original image filename cannot be represented by VSP: " + image.OriginalPath);
        return name;
    }
    /// <summary>由文件名生成合法且唯一的 ID：非字母数字替换为下划线、截到 40 字符、数字开头加前缀，冲突时追加序号。</summary>
    /// <param name="original">来源图片名。</param>
    /// <param name="reserved">不得占用的已有 ID。</param>
    /// <returns>当前文档中可用的图片 ID。</returns>
    public string UniqueId(string original, IEnumerable<string> reserved)
    {
        string basis = Regex.Replace(Path.GetFileNameWithoutExtension(original), @"[^A-Za-z0-9_]", "_").Trim('_');
        if (basis.Length == 0) basis = "image";
        if (basis.Length > 40) basis = basis[..40];
        if (char.IsDigit(basis[0])) basis = "image_" + basis;
        var used = Ids();
        used.UnionWith(reserved);
        string id = basis;
        int i = 2;
        while (used.Contains(id)) id = basis + "_" + i++;
        return id;
    }
    /// <summary>插入一条新的图片声明：新建独占图层 + 在 #Image 段追加一行，已有文本只做插入不做改写。</summary>
    /// <param name="id">目标图片 ID。</param>
    /// <param name="image">已经校验并暂存的图片资源。</param>
    /// <exception cref="InvalidDataException">图片数量、ID 或现有图层声明结构不符合导入约束。</exception>
    public void Add(string id, ImageImportBatch.Image image)
    {
        if (ChartLoadLimits.Exceeds((long)Ids().Count + 1, 4096)) throw new InvalidDataException("VSP image limit is 4096.");
        if (Ids().Contains(id)) throw new InvalidDataException("Image ID already exists: " + id);
        string text = Text;
        // 每次导入都新分配一个图层。绝不能按名字复用作者已有的图层——同名不同优先级会悄悄改变别人图片的叠放次序。
        var layerNames = new HashSet<string>(StringComparer.Ordinal);
        string section = "";
        foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('#'))
            {
                section = line;
                continue;
            }
            if (section == "#Layer" && !line.StartsWith("//") && line.Contains(',')) layerNames.Add(line.Split(',')[0].Trim());
        }
        string layer = "kg_import_" + id;
        while (layerNames.Contains(layer)) layer += "_";
        Match layerHeader = Regex.Match(text, @"(?m)^[ \t]*#Layer[ \t]*(?:\r\n|\r|\n|$)");
        Match imageHeader = Regex.Match(text, @"(?m)^[ \t]*#Image[ \t]*(?:\r\n|\r|\n|$)");
        if (!layerHeader.Success && !imageHeader.Success)
        {
            if (text.Split('\n').Any(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("//")))
                throw new InvalidDataException("Existing VSP has no valid #Layer/#Image sections.");
            text += (text.Length > 0 && !text.EndsWith('\n') ? NewLine : "") + "#Layer" + NewLine + "#Image" + NewLine;
            layerHeader = Regex.Match(text, @"(?m)^[ \t]*#Layer[ \t]*(?:\r\n|\r|\n|$)");
            imageHeader = Regex.Match(text, @"(?m)^[ \t]*#Image[ \t]*(?:\r\n|\r|\n|$)");
        }
        if (!layerHeader.Success || !imageHeader.Success || layerHeader.Index > imageHeader.Index)
            throw new InvalidDataException("Import requires #Layer before #Image; existing text was not changed.");
        var headers = Regex.Matches(text, @"(?m)^[ \t]*#[^\r\n]+")
            .Cast<Match>().Select(m => m.Value.Trim()).ToArray();
        if (headers.Any(header => header is not ("#Layer" or "#Image")) || headers.Count(header => header == "#Layer") != 1 ||
            headers.Count(header => header == "#Image") != 1)
            throw new InvalidDataException("Import needs one #Layer and one #Image section. Existing/unknown sections were not changed.");
        // 优先级 1000 即游戏本身的前景图片层，导入的图片走的是正规图层，不引入任何只有预览器才认得的叠加层。
        int position = imageHeader.Index;
        text = text.Insert(position, layer + ",1000" + NewLine);
        if (text.Length > 0 && text[^1] is not ('\r' or '\n')) text += NewLine;
        string stagedPath = image.StagedPath.Replace('\\', '/');
        if (stagedPath.Contains(',') || stagedPath.Contains('\n') || stagedPath.Contains('\r'))
            throw new IOException("Temporary image path cannot be represented by VSP.");
        text += layer + ":" + NewLine + $"static,{id},{stagedPath},0,{image.Width},{image.Height},0" + NewLine;
        staged[Path.GetFullPath(image.StagedPath)] = image;
        Text = text;
    }
    /// <summary>
    /// 只把某个实例的资源路径换成新文件，ID、所在图层、动画帧数与逻辑宽高一律保留——换图不是重新导入，引用它的 VSM 事件必须继续有效。
    /// 必须恰好命中一条声明，命中 0 条或多条都抛错，宁可不改也不猜。
    /// </summary>
    /// <param name="item">目标图片的 VSP 条目。</param>
    /// <param name="image">已经校验并暂存的图片资源。</param>
    /// <exception cref="InvalidDataException">图片帧条不可整分，或资源路径不能写入 VSP CSV。</exception>
    /// <exception cref="InvalidOperationException">目标图片声明无法唯一定位。</exception>
    public void ReplaceSource(CustomImages.Item item, ImageImportBatch.Image image)
    {
        if (image.Width % item.Frames != 0) throw new InvalidDataException("Replacement strip does not divide into the existing frame count.");
        int matches = 0;
        string next = Regex.Replace(Text, @"[^\r\n]+", m =>
        {
            var fields = m.Value.Split(',').ToList();
            if (fields.Count < 4 || fields[0].Trim() is not ("static" or "animated") || fields[1].Trim() != item.Id) return m.Value;
            matches++;
            string path = image.StagedPath.Replace('\\', '/');
            if (path.IndexOfAny([',', '\r', '\n']) >= 0) throw new InvalidDataException("Replacement path is not valid VSP CSV.");
            string old = fields[2];
            int left = old.Length - old.TrimStart().Length, right = old.Length - old.TrimEnd().Length;
            fields[2] = old[..left] + path + (right > 0 ? old[^right..] : "");
            int optional = fields[0].Trim() == "animated" ? 5 : 4;
            while (fields.Count < optional + 2) fields.Add("-1");
            if (fields[optional].Trim() == "-1") fields[optional] = VsmDocument.N(item.Width);
            if (fields[optional + 1].Trim() == "-1") fields[optional + 1] = VsmDocument.N(item.Height);
            return string.Join(',', fields);
        });
        if (matches != 1) throw new InvalidOperationException("Replacement requires one unambiguous VSP declaration.");
        staged[Path.GetFullPath(image.StagedPath)] = image;
        Text = next;
    }

    /// <summary>替换一个 CSV 单元格的实体值，同时保留它两侧原有的对齐空白。</summary>
    static string ReplaceCell(string token, string value)
    {
        int left = token.Length - token.TrimStart().Length, right = token.Length - token.TrimEnd().Length;
        return token[..left] + value + (right > 0 ? token[^right..] : "");
    }

    /// <summary>修改图片声明自身的 priority（static/animated 的第 4 列）。这只决定同一 VSP 图层内的实例顺序。</summary>
    /// <param name="item">目标图片的 VSP 条目。</param>
    /// <param name="priority">图片在层内的新优先级。</param>
    /// <exception cref="FormatException">图片优先级非有限或绝对值超过一亿。</exception>
    /// <exception cref="InvalidOperationException">图片声明无法唯一定位。</exception>
    public void SetImagePriority(CustomImages.Item item, double priority)
    {
        if (!double.IsFinite(priority) || Math.Abs(priority) > 1e8) throw new FormatException("Image priority must be finite and within 100 million.");
        int matches = 0;
        string next = Regex.Replace(Text, @"[^\r\n]+", m =>
        {
            string[] fields = m.Value.Split(',');
            if (fields.Length < 4 || fields[0].Trim() is not ("static" or "animated") ||
                !string.Equals(fields[1].Trim(), item.Id, StringComparison.OrdinalIgnoreCase)) return m.Value;
            matches++;
            fields[3] = ReplaceCell(fields[3], VsmDocument.N(priority));
            return string.Join(',', fields);
        });
        if (matches != 1) throw new InvalidOperationException("Image priority edit requires one unambiguous VSP declaration.");
        Text = next;
    }

    /// <summary>修改图片所在 #Layer 的绘制优先级。一个 VSP layer 被多张图片共享时，它们会一起移动，这是源格式本身的语义。</summary>
    /// <param name="item">目标图片的 VSP 条目。</param>
    /// <param name="priority">所属图层的新优先级。</param>
    /// <exception cref="FormatException">图层深度超出 -15999 至 15999。</exception>
    /// <exception cref="InvalidOperationException">无法唯一定位要编辑的图层。</exception>
    public void SetLayerPriority(CustomImages.Item item, double priority)
    {
        if (!double.IsFinite(priority) || priority is < -15999 or > 15999)
            throw new FormatException("Layer depth must be between -15999 and 15999.");
        string section = "";
        int matches = 0;
        var output = new StringBuilder(Text.Length + 32);
        foreach (Match m in Regex.Matches(Text, @"([^\r\n]*)(\r\n|\r|\n|$)"))
        {
            if (m.Length == 0) continue;
            string line = m.Groups[1].Value, ending = m.Groups[2].Value, trimmed = line.Trim();
            if (trimmed.StartsWith('#')) section = trimmed;
            else if (section == "#Layer" && trimmed.Length > 0 && !trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                string[] fields = line.Split(',');
                if (fields.Length == 2 && string.Equals(fields[0].Trim(), item.Layer, StringComparison.Ordinal))
                {
                    matches++;
                    fields[1] = ReplaceCell(fields[1], VsmDocument.N(priority));
                    line = string.Join(',', fields);
                }
            }
            output.Append(line).Append(ending);
        }
        if (matches != 1) throw new InvalidOperationException("Layer depth edit requires one unambiguous #Layer declaration.");
        Text = output.ToString();
    }

    /// <summary>
    /// 删除一张图片的 VSP 声明。若它是该 layer 的最后一张图，同时移除已经空掉的 Image 标签和 #Layer 声明；
    /// 其它行逐字保留。这里故意不清 staged：撤销后同一暂存资源仍必须能再次保存。
    /// </summary>
    /// <param name="id">要删除的图片 ID。</param>
    /// <exception cref="InvalidOperationException">无法唯一定位要删除的图片声明。</exception>
    public void Remove(string id)
    {
        var rows = Regex.Matches(Text, @"([^\r\n]*)(\r\n|\r|\n|$)").Cast<Match>()
            .Where(m => m.Length > 0).Select(m => (Text: m.Groups[1].Value, Ending: m.Groups[2].Value)).ToList();
        bool[] keep = Enumerable.Repeat(true, rows.Count).ToArray();
        string section = "", imageLayer = "", targetLayer = "";
        int matches = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            string trimmed = rows[i].Text.Trim();
            if (trimmed.StartsWith('#'))
            {
                section = trimmed;
                imageLayer = "";
                continue;
            }
            if (section != "#Image" || trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
            if (trimmed.EndsWith(':'))
            {
                imageLayer = trimmed[..^1].Trim();
                continue;
            }
            if (ImageDeclarationId(trimmed) == id)
            {
                matches++;
                targetLayer = imageLayer;
                keep[i] = false;
            }
        }
        if (matches != 1) throw new InvalidOperationException("Image delete requires one unambiguous VSP declaration.");

        bool targetLayerStillUsed = targetLayer.Length == 0;
        section = imageLayer = "";
        for (int i = 0; i < rows.Count; i++)
        {
            if (!keep[i]) continue;
            string trimmed = rows[i].Text.Trim();
            if (trimmed.StartsWith('#'))
            {
                section = trimmed;
                imageLayer = "";
                continue;
            }
            if (section != "#Image" || trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
            if (trimmed.EndsWith(':'))
            {
                imageLayer = trimmed[..^1].Trim();
                continue;
            }
            if (imageLayer == targetLayer && ImageDeclarationId(trimmed) != null)
            {
                targetLayerStillUsed = true;
                break;
            }
        }
        if (!targetLayerStillUsed)
        {
            section = "";
            for (int i = 0; i < rows.Count; i++)
            {
                if (!keep[i]) continue;
                string trimmed = rows[i].Text.Trim();
                if (trimmed.StartsWith('#'))
                {
                    section = trimmed;
                    continue;
                }
                if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                if (section == "#Layer")
                {
                    string[] fields = trimmed.Split(',').Select(x => x.Trim()).ToArray();
                    if (fields.Length == 2 && fields[0] == targetLayer) keep[i] = false;
                }
                else if (section == "#Image" && trimmed.EndsWith(':') && trimmed[..^1].Trim() == targetLayer) keep[i] = false;
            }
        }
        var output = new StringBuilder(Text.Length);
        for (int i = 0; i < rows.Count; i++) if (keep[i]) output.Append(rows[i].Text).Append(rows[i].Ending);
        Text = output.ToString();
    }

    /// <summary>把每条 static/animated 声明的路径 token 交给 map 改写，其余单元格连同原有空白逐字保留。</summary>
    /// <param name="map">将来源文件映射到目标文件名的规则。</param>
    /// <returns>改写资源引用后的 VSP 文本。</returns>
    public string RewriteResources(Func<string, string> map)
    {
        return Rewrite(Text, token => map(Path.GetFullPath(token.Replace('\\', Path.DirectorySeparatorChar), ResourceRoot)));
    }
    /// <summary>非声明行原样返回；改单元格时同时提供图片 ID，导出可用它给旧版哈希资源生成可读的回退文件名。</summary>
    /// <param name="text">待改写的 VSP 文本。</param>
    /// <param name="map">资源名映射规则。</param>
    /// <returns>应用映射后的文本。</returns>
    public static string Rewrite(string text, Func<string, string> map) => Rewrite(text, (_, token) => map(token));
    /// <summary>
    /// 非声明行原样返回；改写结果含逗号或换行就抛错，因为 VSP 是无转义的 CSV，写进去等于把这行拆成两个字段。
    /// map 的第一个参数是图片 ID，第二个参数是原路径 token。
    /// </summary>
    /// <param name="text">待改写的 VSP 文本。</param>
    /// <param name="map">资源名映射规则。</param>
    /// <returns>应用映射后的文本。</returns>
    /// <exception cref="InvalidDataException">VSP 图片声明格式不合法。</exception>
    /// <exception cref="IOException">改写后的图片路径无法表示为 VSP CSV 字段。</exception>
    public static string Rewrite(string text, Func<string, string, string> map)
    {
        return Regex.Replace(text, @"[^\r\n]+", m =>
        {
            string[] cells = m.Value.Split(',');
            if (cells[0].Trim() is not ("static" or "animated")) return m.Value;
            if (cells.Length < (cells[0].Trim() == "animated" ? 5 : 4)) throw new InvalidDataException("Malformed VSP image declaration.");
            string id = cells[1].Trim(), token = cells[2], result = map(id, token.Trim()).Replace('\\', '/');
            if (result.Contains(',') || result.Contains('\n') || result.Contains('\r')) throw new IOException("Image path cannot be represented by VSP CSV.");
            int left = token.Length - token.TrimStart().Length, right = token.Length - token.TrimEnd().Length;
            cells[2] = token[..left] + result + (right > 0 ? token[^right..] : "");
            return string.Join(',', cells);
        });
    }
    /// <summary>
    /// 随工程另存的 VSP 一律使用相对自身目录的路径，因此整份文档要按新目录重算一遍相对路径。
    /// 只复制本次导入的暂存图片，不会把歌曲目录里既有的图片一并搬走；落盘前用 SHA-256 复核暂存文件未被外部改动。
    /// </summary>
    /// <param name="directory">保存目标目录。</param>
    /// <param name="stem">工程文件名主干。</param>
    /// <param name="files">接收待写文件及字节的集合。</param>
    /// <param name="relocated">已重定位的图片路径映射。</param>
    /// <param name="resolveNameConflicts">是否允许处理同名冲突。</param>
    /// <returns>用于保存的 VSP 文本。</returns>
    /// <exception cref="IOException">暂存图片在保存前发生变化。</exception>
    /// <exception cref="ImageSaveConflictException">存在未允许处理的图片名称冲突。</exception>
    public string PrepareSave(string directory, string stem, Dictionary<string, byte[]> files,
        Dictionary<string, string>? relocated = null, bool resolveNameConflicts = false)
    {
        string assets = Path.Combine(directory, stem + ".editor-assets");
        var occupied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string target in files.Keys)
            if (Path.GetDirectoryName(target) == assets) occupied.TryAdd(Path.GetFileName(target), target);
        if (Directory.Exists(assets))
            foreach (string entry in Directory.EnumerateFileSystemEntries(assets)) occupied.TryAdd(Path.GetFileName(entry), entry);

        return RewriteResources(path =>
        {
            if (relocated?.TryGetValue(path, out var moved) == true) return Path.GetRelativePath(directory, moved);
            if (!staged.TryGetValue(path, out var image)) return Path.GetRelativePath(directory, path);

            string name = OriginalFileName(image);
            byte[] data = File.ReadAllBytes(path);
            string sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            if (sha != image.Sha256) throw new IOException("Staged image changed: " + image.OriginalPath);

            string Target(string fileName) => occupied.GetValueOrDefault(fileName) ?? Path.Combine(assets, fileName);
            bool CanUse(string target, out bool needsWrite)
            {
                needsWrite = false;
                if (files.TryGetValue(target, out var planned)) return planned.AsSpan().SequenceEqual(data);
                var existing = new FileInfo(target);
                if (existing.LinkTarget != null || Directory.Exists(target)) return false;
                if (!existing.Exists)
                {
                    needsWrite = true;
                    return true;
                }
                if (existing.Length != data.LongLength) return false;
                using var input = existing.OpenRead();
                return Convert.ToHexString(SHA256.HashData(input)).Equals(sha, StringComparison.OrdinalIgnoreCase);
            }

            string target = Target(name);
            bool needsWrite;
            for (int suffix = 2; !CanUse(target, out needsWrite); suffix++)
            {
                if (!resolveNameConflicts) throw new ImageSaveConflictException(name);
                string basis = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
                string alternate = $"{basis}_{suffix}{extension}";
                if (Encoding.UTF8.GetByteCount(alternate) > 255) alternate = $"image-{sha[..12]}_{suffix}{extension}";
                target = Target(alternate);
            }
            if (needsWrite) files.Add(target, data);
            occupied[Path.GetFileName(target)] = target;
            // 成功保存后，将改名的资源同步到当前文档及撤销历史。
            if (relocated != null && Path.GetFileName(target) != name) relocated[path] = target;
            return Path.GetRelativePath(directory, target);
        });
    }
}

internal sealed class ImageSaveConflictException(string fileName) : IOException("Conflicting image filename: " + fileName)
{
    public string FileName { get; } = fileName;
}
