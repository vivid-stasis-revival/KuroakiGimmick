// 根据工程与对象的曲绘模式选择资源并识别动画帧条。

using System.Globalization;
using System.Text.RegularExpressions;
using StbImageSharp;

namespace KuroakiGimmick.Core;

/// <summary>
/// 封面资源索引与乘色模式。目录只提供图片，索引的 ceil/modulo 行为遵循协议；缺失索引不能静默替换成另一张封面。
/// </summary>
// 对应 o_csm_jacket 与 o_gameplay_jacketoverlay。封面始终是外部资源，不随程序分发。
public sealed class JacketAssets
{
    /// <summary>从 Plaudite 目录表读取的曲绘名称。</summary>
    public static string[] PlauditeNames => LoadCatalog("plaudite");
    static string[] LoadCatalog(string key)
    {
        var catalogs = BundledCatalog.Read<Dictionary<string, string[]>>(
            "Catalog/jackets.json", "KuroakiGimmick.JacketCatalog.json");
        return catalogs.TryGetValue(key, out var names) && names != null && names.Length <= 4096
            ? names
            : throw new InvalidDataException("Missing or invalid jacket catalog: " + key);
    }

    /// <summary>当前曲绘的选择模式，初始为 normal。</summary>
    public string Mode { get; private set; } = "normal";
    /// <summary>工程使用的默认曲绘路径。</summary>
    public string? DefaultPath { get; private set; }
    /// <summary>是否采用自定义演出对象的曲绘布局。</summary>
    public bool CustomLayout { get; private set; }
    /// <summary>曲绘是否参与辉光绘制。</summary>
    public bool Glow { get; private set; } = true;
    /// <summary>CSM 3.4.0 默认曲绘的横向方形动画帧条信息。</summary>
    public bool Animated { get; private set; }
    /// <summary>水平方形帧条中检测到的帧数，最少为 1。</summary>
    public int AnimatedFrames { get; private set; } = 1;
    /// <summary>曲绘选择器支持的条目数。</summary>
    public int Count { get; private set; } = 1;
    /// <summary>已找到的曲绘索引与文件路径。</summary>
    public Dictionary<int, string> Files { get; } = [];
    /// <summary>负值取最后一张；其余按 ceil 后对 Count 取模，这是原版协议行为，不要改成四舍五入或钳制。</summary>
    /// <param name="value">曲绘选择轨道的值。</param>
    /// <returns>负值选择末项，其余按向上取整后的编号循环。</returns>
    public int Index(double value) => value < 0 ? Count - 1 : (int)(Math.Ceiling(value) % Count);
    /// <summary>在 custom 或 plaudite 模式下采样对应曲绘轨道，其余模式取 0。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>当前模式选中的曲绘索引。</returns>
    public int IndexAt(Timeline timeline, double time) => Mode is "custom" or "plaudite" ? Index(timeline.Get(Mode + "_jacket", time)) : 0;
    /// <summary>取得当前时刻选中的曲绘路径，缺失时为 null。</summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <returns>当前曲绘路径，未找到该帧时为 null。</returns>
    public string? At(Timeline timeline, double time) => Files.GetValueOrDefault(IndexAt(timeline, time));
    /// <summary>结合工程、对象定义与图片尺寸建立曲绘资源表。</summary>
    /// <param name="project">当前工程的路径与呈现配置。</param>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="native">已解析的原版对象资源配置。</param>
    /// <returns>按当前对象及工程设置解析的曲绘集合。</returns>
    public static JacketAssets Load(ViewerProject project, Chart chart, NativeGimmickProfile? native = null)
    {
        var result = new JacketAssets
        {
            CustomLayout = chart.ObjectName == "obj_custom_gimmick",
            Glow = chart.ObjectName != "obj_custom_gimmick" || SongFiles.Config(project, "JACKET_GLOW", false)
        };
        if (native?.Data?.DisableJacketMultiply == true)
        {
            result.Mode = "none";
            return result;
        }
        string? root = SongFiles.Root(project);
        if (root == null)
        {
            AddNativeFallback(result, native, chart);
            return result;
        }
        string? Find(string dir,
            string stem) => Directory.Exists(dir) ? Directory.EnumerateFiles(dir).Order(StringComparer.Ordinal).FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(stem, StringComparison.OrdinalIgnoreCase) && Path.GetExtension(f).ToLowerInvariant() is ".gif" or ".png" or ".jpg" or ".jpeg") : null;
        result.DefaultPath = project.Jacket ?? Find(root, "jacket");
        result.Mode = result.CustomLayout ? SongFiles.ConfigString(project, "JACKET_MANAGE_MODE", "plaudite") : "normal";
        if (result.Mode is not ("normal" or "plaudite" or "custom"))
        {
            chart.Diagnostics.Add(new("jacket", 0, "Unknown JACKET_MANAGE_MODE: " + result.Mode + "; cover multiplication disabled."));
            return result;
        }
        void Add(int id, string? path)
        {
            if (path == null)
            {
                return;
            }
            try
            {
                if (ChartLoadLimits.Exceeds(new FileInfo(path).Length, 64 * 1024 * 1024))
                {
                    throw new InvalidDataException("Cover exceeds 64 MiB.");
                }
                using var stream = File.OpenRead(path);
                var info = ImageInfo.FromStream(stream) ?? throw new InvalidDataException("Unsupported or corrupt cover.");
                if (info.Width < 1 || info.Height < 1 || ChartLoadLimits.Exceeds(checked((long)info.Width * info.Height * 4), 64 * 1024 * 1024))
                {
                    throw new InvalidDataException("Cover dimensions exceed the 64 MiB decoded limit.");
                }
                if (project.JacketAnimated && result.DefaultPath != null &&
                    Path.GetFullPath(path).Equals(Path.GetFullPath(result.DefaultPath), StringComparison.OrdinalIgnoreCase))
                {
                    result.Animated = true;
                    result.AnimatedFrames = Math.Max(1, info.Width / Math.Max(1, info.Height));
                    if (result.AnimatedFrames <= 1)
                        chart.Diagnostics.Add(new(path, 0, "jacket_animated is true but the cover does not contain multiple square frames."));
                }
                result.Files[id] = path;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
            {
                chart.Diagnostics.Add(new(path, 0, "Jacket unavailable: " + ex.Message, true));
            }
        }
        string[] indexedNames = result.Mode == "plaudite" ? PlauditeNames : [];
        if (result.Mode == "plaudite")
        {
            result.Count = 12;
            Add(11, result.DefaultPath);
            for (int i = 0; i < indexedNames.Length; i++)
            {
                Add(i, Find(Path.Combine(root, "Jackets"), indexedNames[i]) ?? Find(root, indexedNames[i]));
            }
        }
        else
        {
            Add(0, result.DefaultPath);
            if (result.Mode == "custom")
            {
                foreach (var path in Directory.EnumerateFiles(root).Order(StringComparer.Ordinal))
                {
                    if (Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg"))
                    {
                        continue;
                    }
                    var match = Regex.Match(Path.GetFileName(path), @"^jacket\[(\d+)", RegexOptions.IgnoreCase);
                    if (!match.Success)
                    {
                        continue;
                    }
                    if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id > 4095)
                    {
                        chart.Diagnostics.Add(new(path, 0, "Jacket index exceeds 4095; image skipped.", true));
                        continue;
                    }
                    result.Count = Math.Max(result.Count, id + 1);
                    Add(id, path);
                }
            }
        }
        // tween 经过的中间整数索引也要预载，不能只取首尾两端。
        var needed = new HashSet<int>
        {
            result.Mode == "plaudite" ? 11 : 0
        };
        foreach (var e in chart.Mods.Where(e => e.Name == result.Mode + "_jacket" && e.Proxy == -1))
        {
            if (e.To != 573613)
            {
                needed.Add(result.Index(e.To));
            }
            if (e.Duration > 0)
            {
                if (e.From == 573613 || e.To == 573613 || e.Ease.Contains("Back", StringComparison.OrdinalIgnoreCase)
                    || e.Ease.Contains("Elastic", StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < result.Count; i++)
                    {
                        needed.Add(i);
                    }
                    continue;
                }
                needed.Add(result.Index(e.From));
                double lo = Math.Max(0, Math.Min(e.From, e.To)), hi = Math.Max(e.From, e.To);
                if (hi - lo >= result.Count)
                {
                    for (int i = 0; i < result.Count; i++)
                    {
                        needed.Add(i);
                    }
                }
                else
                {
                    for (int offset = 0; offset <= result.Count && Math.Ceiling(lo) + offset <= Math.Ceiling(hi); offset++)
                    {
                        needed.Add(result.Index(Math.Ceiling(lo) + offset));
                    }
                }
            }
        }
        AddNativeFallback(result, native, chart);
        var missing = needed.Order().Where(i => !result.Files.ContainsKey(i)).Select(i => result.Mode == "plaudite"
            && i < 11 ? indexedNames[i] : i == 0 || result.Mode == "plaudite" && i == 11 ? "jacket.jpg/png" : "jacket[" + i + "].png").ToArray();
        if (missing.Length > 0)
        {
            chart.Diagnostics.Add(new("jacket", 0, "Missing cover(s): " + string.Join(", ",
                missing.Take(16)) + ". The multiply pass is omitted for missing covers; particles will differ. Supply local covers; no song covers are bundled."));
        }
        return result;
    }

    static void AddNativeFallback(JacketAssets result, NativeGimmickProfile? native, Chart chart)
    {
        if (native?.Data == null || chart.ObjectName is "obj_base_gimmick" or "obj_custom_gimmick" ||
            result.Mode != "normal" || result.Files.ContainsKey(0)) return;
        try
        {
            string file = ResourceFiles.ContainedFile(native.AssetsRoot, "GameCommon/song_generic.png");
            new ImageResourceBudget().Check(file);
            result.Files[0] = file;
            native.Resources["song-generic-fallback"] = file;
        }
        catch (Exception ex) when (ResourceFiles.IsResourceError(ex))
        {
            native.Fail("song-generic-fallback", ex.Message);
        }
    }
}

