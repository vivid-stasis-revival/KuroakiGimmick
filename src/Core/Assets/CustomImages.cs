using System.Globalization;
using System.Numerics;
using StbImageSharp;

namespace KuroakiGimmick.Core;

/// <summary>
/// VSP 图层/图像定义与姿态求值。图片资源和 mod 轨道分开加载；层优先级保留作者值，不强制所有图片进入同一轨道。
/// </summary>
// Custom Gimmicks v1.12.7：.vsp 图像声明 + 具名 .vsm 轨道。
public sealed class CustomImages
{
    public record Asset(string Path, int Width, int Height);
    public record Item(string Id, string Layer, double LayerPriority, double Priority, int Order, Asset Asset, int Frames, double Width,
        double Height, int Lane);
    public string? Path { get; private set; }
    public List<Item> Items { get; } = [];
    public Dictionary<string, Asset> Files { get; } = new(StringComparer.Ordinal);
    static readonly HashSet<string> Kinds = new("imgx imgy imgxtime imgytime imgrot imgscalex imgscaley imgscaleytime imgskewx imgskewy imgcolrgb imgalp imgidx".Split(' '));
    public static bool TryMod(string name, out string kind, out string id)
    {
        int split = name.IndexOf('_');
        kind = split < 0 ? name : name[..split];
        id = split < 0 ? "" : name[(split + 1)..];
        return id.Length > 0 && Kinds.Contains(kind);
    }

    /// <summary>未声明轨道时的原版默认值；160/90 是 320×180 逻辑空间的中心，573613 是原版表示该项未设置的哨兵值。</summary>
    public static double Default(string kind) => kind switch
    {
        "imgx" => 160,
        "imgy" => 90,
        "imgxtime" or "imgytime" or "imgscaleytime" => 573613,
        "imgscalex" or "imgscaley" or "imgalp" => 1,
        "imgcolrgb" => 16777215,
        _ => 0
    };
    public static CustomImages Load(ViewerProject p, Chart chart, string? editedText = null, string? resourceRoot = null)
    {
        var result = new CustomImages();
        if (p.Chart == null && p.Gimmick == null && p.Images == null)
        {
            return result;
        }
        string basis = p.Chart ?? p.Gimmick ?? p.Images!;
        var root = System.IO.Path.GetDirectoryName(basis)!;
        if (p.Images != null)
        {
            result.Path = p.Images;
        }
        else
        {
            var difficulty = System.IO.Path.GetFileNameWithoutExtension(basis);
            // gimmick_output 的文件名可能是 _ENCORE_song；真实谱面名始终优先。
            if (p.Chart == null && difficulty.StartsWith('_'))
            {
                difficulty = difficulty.TrimStart('_').Split('_')[0];
            }
            result.Path = new[]
            {
                difficulty + ".vsp",
                "GLOBAL.vsp"
            }.Select(x => System.IO.Path.Combine(root, x)).FirstOrDefault(File.Exists);
        }
        if (result.Path == null && editedText != null) result.Path = System.IO.Path.Combine(root, System.IO.Path.GetFileNameWithoutExtension(basis) + ".vsp");
        if (result.Path == null)
        {
            return result;
        }
        result.Path = System.IO.Path.GetFullPath(result.Path);
        // 素材路径相对谱面目录解析，即使 .vsp 是单独附加的也一样。
        if (p.Chart == null || p.ImagePathsRelativeToVsp)
        {
            root = System.IO.Path.GetDirectoryName(result.Path)!;
        }
        if (resourceRoot != null) root = System.IO.Path.GetFullPath(resourceRoot);
        var layers = new Dictionary<string, double>(StringComparer.Ordinal);
        string section = "", layer = "";
        int line = 0;
        double Number(string s)
        {
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n))
            {
                throw new InvalidDataException("Invalid number: " + s);
            }
            return n;
        }
        try
        {
            if ((editedText != null ? System.Text.Encoding.UTF8.GetByteCount(editedText) : new FileInfo(result.Path).Length) > 16 * 1024 * 1024)
            {
                throw new InvalidDataException("VSP exceeds 16 MiB.");
            }
            IEnumerable<string> lines = editedText != null ? editedText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n') : File.ReadLines(result.Path);
            foreach (var raw in lines)
            {
                line++;
                var text = raw.Trim();
                if (text.Length == 0 || text.StartsWith("//"))
                {
                    continue;
                }
                try
                {
                    if (text.StartsWith('#'))
                    {
                        section = text[1..];
                        continue;
                    }
                    var args = text.Split(',').Select(x => x.Trim()).ToArray();
                    if (section == "Layer")
                    {
                        if (args.Length != 2 || args[0].Length == 0)
                        {
                            throw new InvalidDataException("Layer needs name,priority.");
                        }
                        if (!layers.TryAdd(args[0], Math.Clamp(Number(args[1]), -15999, 15999)))
                        {
                            throw new InvalidDataException("Duplicate layer: " + args[0]);
                        }
                    }
                    else if (section == "Image")
                    {
                        if (text.EndsWith(':'))
                        {
                            layer = text[..^1].Trim();
                            continue;
                        }
                        if (!layers.TryGetValue(layer, out var layerPriority))
                        {
                            throw new InvalidDataException("Undeclared layer: " + layer);
                        }
                        if (args.Length < 4 || args[0] is not ("static" or "animated"))
                        {
                            throw new InvalidDataException("Expected static/animated image declaration.");
                        }
                        bool animated = args[0] == "animated";
                        int optional = animated ? 5 : 4;
                        if (args.Length < optional || args.Length > optional + 3)
                        {
                            throw new InvalidDataException("Wrong image column count.");
                        }
                        if (result.Items.Count >= 4096)
                        {
                            throw new InvalidDataException("Image declaration limit is 4096.");
                        }
                        string id = args[1];
                        if (id.Length == 0 || chart.ImageNames.Contains(id))
                        {
                            throw new InvalidDataException("Empty or duplicate image ID: " + id);
                        }
                        var frameNumber = animated ? Number(args[4]) : 1;
                        if (frameNumber != Math.Truncate(frameNumber) || frameNumber is < 1 or > 8192)
                        {
                            throw new InvalidDataException("Invalid frame count.");
                        }
                        int frames = (int) frameNumber;
                        double priority = Number(args[3]);
                        string path = System.IO.Path.GetFullPath(args[2].Replace('\\', System.IO.Path.DirectorySeparatorChar), root);
                        if (!result.Files.TryGetValue(path, out var asset))
                        {
                            if (!File.Exists(path))
                            {
                                throw new FileNotFoundException("Missing image: " + path);
                            }
                            if (new FileInfo(path).Length > 64 * 1024 * 1024)
                            {
                                throw new InvalidDataException("Image exceeds 64 MiB: " + path);
                            }
                            using var stream = File.OpenRead(path);
                            var info = ImageInfo.FromStream(stream) ?? throw new InvalidDataException("Unsupported or corrupt image: " + path);
                            long bytes = (long) info.Width * info.Height * 4;
                            if (info.Width is < 1 or > 16384 || info.Height is < 1 or > 16384 || bytes > 64 * 1024 * 1024)
                            {
                                throw new InvalidDataException("Image dimensions or per-image 64 MiB decoded limit exceeded: " + path);
                            }
                            asset = new(path, info.Width, info.Height);
                            result.Files.Add(path, asset);
                        }
                        if (asset.Width % frames != 0)
                        {
                            throw new InvalidDataException("Strip width must divide evenly by frame count: " + id);
                        }
                        double width = args.Length > optional ? Number(args[optional]) : -1,
                            height = args.Length > optional + 1 ? Number(args[optional + 1]) : -1;
                        if (width == -1)
                        {
                            width = asset.Width / frames;
                        }
                        if (height == -1)
                        {
                            height = asset.Height;
                        }
                        if (width <= 0 || height <= 0 || width > 100000 || height > 100000)
                        {
                            throw new InvalidDataException("Invalid initial image size: " + id);
                        }
                        double lane = args.Length > optional + 2 ? Math.Round(Number(args[optional + 2]), MidpointRounding.AwayFromZero) : 0;
                        if (lane is < 0 or > 6)
                        {
                            throw new InvalidDataException("Lane must be 0..6.");
                        }
                        result.Items.Add(new(id, layer, layerPriority, priority, line, asset, frames, width, height, (int) lane));
                        chart.ImageNames.Add(id);
                    }
                    else
                    {
                        throw new InvalidDataException("Expected #Layer or #Image section.");
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
                {
                    chart.Diagnostics.Add(new(result.Path, line, ex.Message, true));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            chart.Diagnostics.Add(new(result.Path, line, ex.Message, true));
        }
        var order = layers.Keys.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i);
        var sorted = result.Items.OrderBy(x => x.LayerPriority).ThenBy(x => order[x.Layer]).ThenBy(x => x.Priority).ThenBy(x => x.Order).ToArray();
        result.Items.Clear();
        result.Items.AddRange(sorted);
        if (chart.Mods.Any(e => TryMod(e.Name, out var k, out _) && k == "imgscaleytime"))
        {
            chart.Diagnostics.Add(new("images", 0,
                "imgscaleytime follows v1.12.7 code: posEnd - posO / height (including its original precedence)."));
        }
        if (new[]
        {
            "GLOBAL.vsv",
            System.IO.Path.GetFileNameWithoutExtension(basis) + ".vsv"
        }.Any(x => File.Exists(System.IO.Path.Combine(root, x))))
        {
            chart.Diagnostics.Add(new("images", 0, "VSV scroll warps are not implemented; image time offsets use unwarped chart time."));
        }
        return result;
    }

    public record Pose(Matrix3x2 Matrix, int Frame, uint Rgb, double Alpha);
    // 状态按层、按帧重置，对应原版绘制循环中作用域局限于循环体的时间变量。
    /// <summary>
    /// 按已排序的层顺序产出每张图的当前姿态（320×180 逻辑空间）。alpha ≤ 0 或矩阵出现非有限分量的图直接跳过，不绘制。
    /// 变换次序固定为缩放 → 斜切 → 旋转 → 平移，与原版绘制顺序一致，不能为了合并矩阵改写。
    /// </summary>
    public IEnumerable<(Item Image, Pose Pose)> At(Timeline timeline, double time, IReadOnlyDictionary<string, double>? valueOverrides = null)
    {
        string layer = "";
        double xt = 0, yt = 0, ys = 1;
        foreach (var item in Items)
        {
            if (layer != item.Layer)
            {
                layer = item.Layer;
                xt = yt = 0;
                ys = 1;
            }
            double M(string kind) => valueOverrides != null && valueOverrides.TryGetValue(kind + "_" + item.Id, out double value)
                ? value : timeline.Get(kind + "_" + item.Id, time);
            double alpha = M("imgalp");
            if (alpha <= 0)
            {
                continue;
            }
            // 573613 表示该项未设置：命中时沿用本层已累积的偏移/缩放，不重新按音符运动求值。
            if (M("imgxtime") != 573613)
            {
                xt = NoteX(timeline, time, item.Lane, M("imgxtime"));
            }
            if (M("imgytime") != 573613)
            {
                yt = NoteY(timeline, time, item.Lane, M("imgytime"));
            }
            if (M("imgscaleytime") != 573613)
            {
                ys = NoteY(timeline, time, item.Lane, M("imgscaleytime")) - NoteY(timeline, time, item.Lane, 0) / item.Height;
            }
            var matrix = Matrix3x2.CreateScale((float)(item.Width * M("imgscalex")),
                (float)(item.Height * M("imgscaley") * ys)) * new Matrix3x2(1, (float) M("imgskewy"), (float) M("imgskewx"), 1, 0,
                0) * Matrix3x2.CreateRotation((float)(M("imgrot") * Math.PI / 180)) * Matrix3x2.CreateTranslation((float)(M("imgx") + xt),
                (float)(M("imgy") + yt));
            double frame = Math.Max(0, Math.Round(M("imgidx"), MidpointRounding.AwayFromZero)) % item.Frames;
            if (!float.IsFinite(matrix.M11) || !float.IsFinite(matrix.M22) || !float.IsFinite(matrix.M31) || !float.IsFinite(matrix.M32))
            {
                continue;
            }
            yield return (item, new(matrix, (int) frame, (uint) Math.Clamp(M("imgcolrgb"), 0, 16777215), alpha));
        }
    }

    static double NoteX(Timeline t, double time, int lane, double distance) => NoteMotion.X(t, time, lane, distance);
    static double NoteY(Timeline t, double time, int lane, double distance) => NoteMotion.Y(t, time, lane, distance);
}

