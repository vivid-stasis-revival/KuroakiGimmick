using System.Text.Json;
using StbImageSharp;

namespace KuroakiGimmick.Core;

/// <summary>
/// HUD 精灵、字体图集与布局的 CPU 描述。保留图集尺寸和原点，避免重新缩放导致字体或按键素材截断。
/// </summary>
public sealed class GameUiAssets
{
    public sealed class Sprite
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int OriginX { get; set; }
        public int OriginY { get; set; }
        public List<string> Frames { get; set; } = [];
        /// <summary>
        /// 逐帧的裁剪矩形 [left, top, width, height]，直接取自 GameMaker 纹理页条目。
        /// 只有被 font_add_sprite 当字模用的精灵才需要它：原版比例字宽就是这个矩形的宽，
        /// 缺少它时调用方应当跳过该精灵的文字排版，而不是拿整帧宽度去猜。
        /// </summary>
        public List<int[]>? Bounds { get; set; }
    }

    public sealed class Kern
    {
        public int Character { get; set; }
        public float Shift { get; set; }
    }

    public sealed class Glyph
    {
        public int Character { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public float Advance { get; set; }
        public float Offset { get; set; }
        public List<Kern> Kerning { get; set; } = [];
    }

    public sealed class Font
    {
        public string File { get; set; } = "";
        public float Size { get; set; } = 10;
        public float LineHeight { get; set; } = 14;
        public float AscenderOffset { get; set; }
        public float ScaleX { get; set; } = 1;
        public float ScaleY { get; set; } = 1;
        public List<Glyph> Glyphs { get; set; } = [];
    }

    public sealed class Pack
    {
        public int Version { get; set; }
        public Dictionary<string, Sprite> Sprites { get; set; } = [];
        public Dictionary<string, Font> Fonts { get; set; } = [];
    }

    public string? Manifest { get; private set; }
    public Pack? Data { get; private set; }
    public bool Ready => Data != null;
    public string File(string name) => Path.GetFullPath(name, Path.GetDirectoryName(Manifest!)!);
    /// <summary>UI 包发现顺序：工程显式路径 → 曲目目录 → 随程序安装的 GameUI 目录；都没有时返回 null，不绘制替代 UI。</summary>
    static string? Discover(ViewerProject p)
    {
        if (p.GameUi != null)
        {
            return Directory.Exists(p.GameUi) ? Path.Combine(p.GameUi, "game-ui.gameui.json") : p.GameUi;
        }
        string? root = SongFiles.Root(p);
        if (root != null && System.IO.File.Exists(Path.Combine(root, "game-ui.gameui.json")))
        {
            return Path.Combine(root, "game-ui.gameui.json");
        }
        string bundled = Path.Combine(Paths.SharedAssetDirectory("GameUI"), "game-ui.gameui.json");
        return System.IO.File.Exists(bundled) ? bundled : null;
    }

    public static GameUiAssets Load(ViewerProject p, Chart chart)
    {
        var result = new GameUiAssets();
        if (p.Chart == null && p.Gimmick == null && p.Images == null)
        {
            return result;
        }
        try
        {
            result.Manifest = Discover(p);
            if (result.Manifest == null)
            {
                if (p.GameUiEnabled)
                {
                    chart.Diagnostics.Add(new("game-ui", 0,
                        "Original UI sprites/font atlas not installed. Run Dump_Game_UI_Mac.sh, then reload, or attach game-ui.gameui.json. No replacement UI graphics are drawn."));
                }
                return result;
            }
            if (new FileInfo(result.Manifest).Length > 16 * 1024 * 1024)
            {
                throw new InvalidDataException("UI manifest exceeds 16 MiB.");
            }
            var data = AppJson.Deserialize<Pack>(System.IO.File.ReadAllText(result.Manifest),
                ViewerProject.Json) ?? throw new InvalidDataException("Empty UI pack.");
            if (data.Version != 1 || data.Sprites == null || data.Fonts == null || data.Sprites.Count > 16 || data.Fonts.Count > 8)
            {
                throw new InvalidDataException("Unsupported UI pack.");
            }
            foreach (string n in new[]
            {
                "sp_gameplayoverlay2024",
                "sp_2024cc_score",
                "sp_newdifficultyindicator",
                "sp_newdifficultylevel",
                "sp_newdifficultynumbers"
            })
            {
                if (!data.Sprites.ContainsKey(n))
                {
                    throw new InvalidDataException("Missing UI sprite: " + n);
                }
            }
            foreach (string n in new[]
            {
                "fnt_monacovs",
                "fnt_credits"
            })
            {
                if (!data.Fonts.ContainsKey(n))
                {
                    throw new InvalidDataException("Missing UI font: " + n);
                }
            }
            long bytes = 0;
            // 尺寸按文件去重缓存：同一张图集被多个 sprite 或字体引用时只计一次解码预算。
            var sizes = new Dictionary<string, (int W, int H)>();
            (int W, int H) Size(string file)
            {
                if (sizes.TryGetValue(file, out var known))
                {
                    return known;
                }
                string path = result.File(file), rel = Path.GetRelativePath(Path.GetDirectoryName(result.Manifest)!, path);
                if (Path.IsPathRooted(rel) || rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar))
                {
                    throw new InvalidDataException("UI asset must stay inside its pack.");
                }
                if (new FileInfo(path).Length > 64 * 1024 * 1024)
                {
                    throw new InvalidDataException("UI image exceeds 64 MiB.");
                }
                using var stream = System.IO.File.OpenRead(path);
                var info = ImageInfo.FromStream(stream) ?? throw new InvalidDataException("Invalid UI image: " + file);
                bytes += (long) info.Width * info.Height * 4;
                if (info.Width is < 1 or > 8192 || info.Height is < 1 or > 8192 || bytes > 192 * 1024 * 1024)
                {
                    throw new InvalidDataException("UI decoded texture budget exceeded.");
                }
                return sizes[file] = (info.Width, info.Height);
            }
            foreach (var sprite in data.Sprites.Values)
            {
                if (sprite.Frames == null || sprite.Frames.Count is < 1 or > 128 || sprite.Width < 1 || sprite.Height < 1)
                {
                    throw new InvalidDataException("Invalid UI sprite frames.");
                }
                foreach (var f in sprite.Frames)
                {
                    if (Size(f) != (sprite.Width, sprite.Height))
                    {
                        throw new InvalidDataException("UI sprite padding/dimensions mismatch: " + f);
                    }
                }
                // 裁剪矩形要么整套齐全，要么整套缺失；半套会让排版在某些字符上突然算错宽度。
                if (sprite.Bounds != null && (sprite.Bounds.Count != sprite.Frames.Count
                    || sprite.Bounds.Any(b => b == null || b.Length != 4 || b[0] < 0 || b[1] < 0 || b[2] < 0 || b[3] < 0
                        || b[0] + b[2] > sprite.Width || b[1] + b[3] > sprite.Height)))
                {
                    throw new InvalidDataException("Invalid UI sprite bounds.");
                }
            }
            foreach (var font in data.Fonts.Values)
            {
                var size = Size(font.File);
                if (font.Glyphs == null || font.Glyphs.Count is < 1 or > 131072 || !float.IsFinite(font.ScaleX) || !float.IsFinite(font.ScaleY)
                    || font.ScaleX <= 0 || font.ScaleY <= 0 || font.Size <= 0 || !float.IsFinite(font.Size) || !float.IsFinite(font.AscenderOffset))
                {
                    throw new InvalidDataException("Invalid UI font metrics.");
                }
                foreach (var g in font.Glyphs)
                {
                    if (g.X < 0 || g.Y < 0 || g.Y + font.AscenderOffset < 0 || g.W < 0 || g.H < 0 || (long) g.X + g.W > size.W
                        || (long) g.Y + font.AscenderOffset + g.H > size.H || !float.IsFinite(g.Advance) || !float.IsFinite(g.Offset)
                        || g.Kerning == null || g.Kerning.Any(k => !float.IsFinite(k.Shift)))
                    {
                        throw new InvalidDataException("Invalid original glyph bounds.");
                    }
                }
            }
            result.Data = data;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            chart.Diagnostics.Add(new("game-ui", 0, "Original UI unavailable: " + ex.Message, true));
        }
        return result;
    }
}

