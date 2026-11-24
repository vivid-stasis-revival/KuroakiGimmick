// 发现游戏 HUD 资源包并检查图集尺寸、字形范围及解码预算。

using System.Text.Json;
using StbImageSharp;

namespace KuroakiGimmick.Core;

/// <summary>
/// HUD 精灵、字体图集与布局的 CPU 描述。保留图集尺寸和原点，避免重新缩放导致字体或按键素材截断。
/// </summary>
public sealed class GameUiAssets
{
    /// <summary>HUD 精灵的各帧文件和绘制度量。</summary>
    public sealed class Sprite
    {
        /// <summary>各帧填充后的像素宽度。</summary>
        public int Width { get; set; }
        /// <summary>各帧填充后的像素高度。</summary>
        public int Height { get; set; }
        /// <summary>精灵绘制原点的横坐标。</summary>
        public int OriginX { get; set; }
        /// <summary>精灵绘制原点的纵坐标。</summary>
        public int OriginY { get; set; }
        /// <summary>按帧号排列的图像资源路径。</summary>
        public List<string> Frames { get; set; } = [];
        /// <summary>
        /// 逐帧的裁剪矩形 [left, top, width, height]，直接取自 GameMaker 纹理页条目。
        /// 只有被 font_add_sprite 当字模用的精灵才需要它：原版比例字宽就是这个矩形的宽，
        /// 缺少它时调用方应当跳过该精灵的文字排版，而不是拿整帧宽度去猜。
        /// </summary>
        public List<int[]>? Bounds { get; set; }
    }

    /// <summary>一对字形的字距修正。</summary>
    public sealed class Kern
    {
        /// <summary>配对字距规则匹配的字符编码。</summary>
        public int Character { get; set; }
        /// <summary>配对命中时应用的水平位移。</summary>
        public float Shift { get; set; }
    }

    /// <summary>字体图集内单个字符的区域与排版信息。</summary>
    public sealed class Glyph
    {
        /// <summary>字形映射的字符编码。</summary>
        public int Character { get; set; }
        /// <summary>字形在字体图集中的左边界。</summary>
        public int X { get; set; }
        /// <summary>字形在字体图集中的纵向位置，取样时计入 AscenderOffset。</summary>
        public int Y { get; set; }
        /// <summary>图集内该字形的像素宽度。</summary>
        public int W { get; set; }
        /// <summary>图集内该字形的像素高度。</summary>
        public int H { get; set; }
        /// <summary>绘制完本字形后笔位置的推进量。</summary>
        public float Advance { get; set; }
        /// <summary>绘制本字形时的水平偏移。</summary>
        public float Offset { get; set; }
        /// <summary>按相邻字符选择的配对字距表。</summary>
        public List<Kern> Kerning { get; set; } = [];
    }

    /// <summary>原版字体图集的尺寸和字形度量。</summary>
    public sealed class Font
    {
        /// <summary>包含字形的字体图集路径。</summary>
        public string File { get; set; } = "";
        /// <summary>原版字体的标称字号。</summary>
        public float Size { get; set; } = 10;
        /// <summary>排版连续文本行时的行距。</summary>
        public float LineHeight { get; set; } = 14;
        /// <summary>将导出的字形纵坐标换算到图集采样区域时的偏移。</summary>
        public float AscenderOffset { get; set; }
        /// <summary>字体图集的横向缩放系数。</summary>
        public float ScaleX { get; set; } = 1;
        /// <summary>字体图集的纵向缩放系数。</summary>
        public float ScaleY { get; set; } = 1;
        /// <summary>字符到图集区域和字距的映射记录。</summary>
        public List<Glyph> Glyphs { get; set; } = [];
    }

    /// <summary>从 HUD 清单解出的精灵表与字体表。</summary>
    public sealed class Pack
    {
        /// <summary>游戏 HUD 资源清单的格式版本。</summary>
        public int Version { get; set; }
        /// <summary>按名称索引的 HUD 精灵。</summary>
        public Dictionary<string, Sprite> Sprites { get; set; } = [];
        /// <summary>按资源名索引的游戏字体。</summary>
        public Dictionary<string, Font> Fonts { get; set; } = [];
    }

    /// <summary>当前 HUD 资源清单的可选位置。</summary>
    public string? Manifest { get; private set; }
    /// <summary>通过校验的资源清单，不可用时为 null。</summary>
    public Pack? Data { get; private set; }
    /// <summary>清单及其引用资源是否已通过读取和校验。</summary>
    public bool Ready => Data != null;
    /// <summary>相对于当前清单目录解析资源的完整路径。</summary>
    /// <param name="name">相对于 HUD 清单目录的资源名。</param>
    /// <returns>解析后的绝对路径。</returns>
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

    /// <summary>发现并校验游戏 HUD 资源包，将加载失败的诊断加入谱面。</summary>
    /// <param name="p">当前工程的路径与呈现配置。</param>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <returns>已校验的 HUD 资源状态，不能加载时 Ready 为 false。</returns>
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
                        "Original UI sprites/font atlas not installed. Install the original GameUI assets, then reload, or attach game-ui.gameui.json. No replacement UI graphics are drawn."));
                }
                return result;
            }
            if (ChartLoadLimits.Exceeds(new FileInfo(result.Manifest).Length, 16 * 1024 * 1024))
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
                if (ChartLoadLimits.Exceeds(new FileInfo(path).Length, 64 * 1024 * 1024))
                {
                    throw new InvalidDataException("UI image exceeds 64 MiB.");
                }
                using var stream = System.IO.File.OpenRead(path);
                var info = ImageInfo.FromStream(stream) ?? throw new InvalidDataException("Invalid UI image: " + file);
                bytes = checked(bytes + (long)info.Width * info.Height * 4);
                if (info.Width < 1 || info.Height < 1 || ChartLoadLimits.Exceeds(info.Width, 8192) || ChartLoadLimits.Exceeds(info.Height, 8192) || ChartLoadLimits.Exceeds(bytes, 192 * 1024 * 1024))
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
                    if (g.X < 0 || g.Y < 0 || g.Y + font.AscenderOffset < 0 || g.W < 0 || g.H < 0 || (long)g.X + g.W > size.W
                        || (long)g.Y + font.AscenderOffset + g.H > size.H || !float.IsFinite(g.Advance) || !float.IsFinite(g.Offset)
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

