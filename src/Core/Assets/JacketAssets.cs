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
    public static string[] PlauditeNames => LoadCatalog("plaudite");
    static string[] LoadCatalog(string key)
    {
        var catalogs = BundledCatalog.Read<Dictionary<string, string[]>>(
            "Catalog/jackets.json", "KuroakiGimmick.JacketCatalog.json");
        return catalogs.TryGetValue(key, out var names) && names != null && names.Length <= 4096
            ? names
            : throw new InvalidDataException("Missing or invalid jacket catalog: " + key);
    }

    public string Mode { get; private set; } = "normal";
    public string? DefaultPath { get; private set; }
    public bool CustomLayout { get; private set; }
    public int Count { get; private set; } = 1;
    public Dictionary<int, string> Files { get; } = [];
    /// <summary>负值取最后一张；其余按 ceil 后对 Count 取模，这是原版协议行为，不要改成四舍五入或钳制。</summary>
    public int Index(double value) => value < 0 ? Count - 1 : (int)(Math.Ceiling(value) % Count);
    public int IndexAt(Timeline timeline, double time) => Mode is "custom" or "plaudite" ? Index(timeline.Get(Mode + "_jacket", time)) : 0;
    public string? At(Timeline timeline, double time) => Files.GetValueOrDefault(IndexAt(timeline, time));
    public static JacketAssets Load(ViewerProject project, Chart chart, NativeGimmickProfile? native = null)
    {
        var result = new JacketAssets
        {
            CustomLayout = chart.ObjectName == "obj_custom_gimmick"
        };
        if (native?.Data?.DisableJacketMultiply == true)
        {
            result.Mode = "none";
            return result;
        }
        string? root = SongFiles.Root(project);
        if (root == null)
        {
            return result;
        }
        string? Find(string dir,
            string stem) => Directory.Exists(dir) ? Directory.EnumerateFiles(dir).Order(StringComparer.Ordinal).FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(stem, StringComparison.OrdinalIgnoreCase) && Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg") : null;
        result.DefaultPath = project.Jacket ?? Find(root, "jacket");
        result.Mode = result.CustomLayout? SongFiles.ConfigString(project, "JACKET_MANAGE_MODE", "plaudite") : "normal";
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
                if (new FileInfo(path).Length > 64 * 1024 * 1024)
                {
                    throw new InvalidDataException("Cover exceeds 64 MiB.");
                }
                using var stream = File.OpenRead(path);
                var info = ImageInfo.FromStream(stream) ?? throw new InvalidDataException("Unsupported or corrupt cover.");
                if (info.Width is < 1 or > 16384 || info.Height is < 1 or > 16384 || (long) info.Width * info.Height * 4 > 64 * 1024 * 1024)
                {
                    throw new InvalidDataException("Cover dimensions exceed the 64 MiB decoded limit.");
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
        var missing = needed.Order().Where(i => !result.Files.ContainsKey(i)).Select(i => result.Mode == "plaudite"
            && i < 11 ? indexedNames[i] : i == 0 || result.Mode == "plaudite" && i == 11 ? "jacket.jpg/png" : "jacket[" + i + "].png").ToArray();
        if (missing.Length > 0)
        {
            chart.Diagnostics.Add(new("jacket", 0, "Missing cover(s): " + string.Join(", ",
                missing.Take(16)) + ". The multiply pass is omitted for missing covers; particles will differ. Supply local covers; no song covers are bundled."));
        }
        return result;
    }
}

