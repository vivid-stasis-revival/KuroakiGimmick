using System.Text.Json;
using StbImageSharp;

namespace KuroakiGimmick.Core;

/// <summary>
/// 房间滤镜配置与资源校验。滤镜类型由 manifest 明确声明，不从层名猜测；图层深度决定与对象 pass 的合成顺序。
/// </summary>
// 外部房间 FX 数值。绝不能从图层的昵称反推滤镜类型。
public sealed class GameFxProfile
{
    /// <summary>房间 FX 的一层。Filter 是原版滤镜名，必须与 Name 配对；Depth 大的先合成，Visible 为 false 时仍求值但不绘制。</summary>
    public sealed class Layer
    {
        public string Name { get; set; } = "";
        public string Filter { get; set; } = "";
        public int Depth { get; set; }
        public bool Visible { get; set; } = true;
        public bool Enabled { get; set; } = true;
        public Dictionary<string, JsonElement> Parameters { get; set; } = [];
        public string? TexturePath { get; set; }
        public string? VisibilityMod { get; set; }
        public Dictionary<string, string> ParameterMods { get; set; } = [];
        /// <summary>取标量参数；缺失或非有限值直接抛错，不替换成 0，避免把错配置渲染成看似正常的画面。</summary>
        public double Number(string name)
        {
            if (!Parameters.TryGetValue(name, out var p) || p.ValueKind != JsonValueKind.Number || !p.TryGetDouble(out double v)
                || !double.IsFinite(v))
            {
                throw new InvalidDataException("Missing/non-finite FX parameter: " + name);
            }
            return v;
        }
        public double[] Vector(string name, int length)
        {
            if (!Parameters.TryGetValue(name, out var p) || p.ValueKind != JsonValueKind.Array || p.GetArrayLength() != length)
            {
                throw new InvalidDataException($"FX parameter {name} requires {length} values.");
            }
            var v = p.EnumerateArray().Select(e => e.GetDouble()).ToArray();
            if (v.Any(x => !double.IsFinite(x)))
            {
                throw new InvalidDataException("Non-finite FX vector: " + name);
            }
            return v;
        }
    }

    public string? Path { get; private set; }
    public string? Room { get; private set; }
    public string Source { get; private set; } = "none";
    public bool AutoFallback { get; private set; }
    public string? RoomAssociation { get; private set; }
    public static readonly string[] Presets = ["auto", "gameplay", "plaudite", "angelstar", "scarletdeath", "extendnova", "sekaisen",
        "starcrashers", "none"];
    public static string NormalizePreset(string name) => name.StartsWith("scene_gameplay_",
        StringComparison.Ordinal) ? name[15..] : name == "scene_gameplay" ? "gameplay" : name;
    public List<Layer> Layers { get; } = [];
    public static readonly string[] FilmParameters = "FlickerIntensity FlickerSpeed JitterIntensity Saturation SpeckIntensity BarScale BarSpeed BarFrequency RingScale RingSharpness RingIntensity"
        .Split(' ').Select(n => "g_OldFilm" + n).ToArray();
    public string? DynamicDefinitions { get; private set; }
    public Layer? Find(string name) => Layers.FirstOrDefault(l => l.Name == name);
    public static readonly Dictionary<string, string> ModLayers = new()
    {
        ["fx_film"] = "FX_film",
        ["fx_edge"] = "FX_edge",
        ["fx_chroma_distort"] = "FX_chroma",
        ["fx_contrast"] = "FX_contrast",
        ["BG_ditortAmount"] = "LBG"
    };
    /// <summary>
    /// FX profile 优先级：工程显式附加 → 曲目目录的 gimmick-fx.json → 按预设选中的内置房间。
    /// preset 为 auto 时才查房间关联；读取失败按文件/图层记入诊断并跳过该层，不替换成别的滤镜。
    /// 返回的 Layers 已按 Depth 从大到小排序，决定与对象 pass 的合成顺序。
    /// </summary>
    public static GameFxProfile Load(ViewerProject project, Chart chart, NativeGimmickProfile? native = null)
    {
        var result = new GameFxProfile();
        string? root = SongFiles.Root(project);
        if (root == null)
        {
            return result;
        }
        string preset = NormalizePreset(project.RoomPreset ?? "auto");
        if (!Presets.Contains(preset))
        {
            throw new InvalidDataException("Unknown room preset: " + project.RoomPreset);
        }
        result.Path = project.FxProfile;
        if (result.Path != null)
        {
            result.Source = "external";
        }
        else if (preset == "auto" && (result.Path = SongFiles.Existing(root, "gimmick-fx.json")) != null)
        {
            result.Source = "local";
        }
        else if (preset != "none")
        {
            string? selected = preset == "auto" ? native?.Data?.RoomPreset ?? "gameplay" : preset;
            if (selected == "none")
            {
                selected = null;
            }
            // start_song 依据 song_get_info(..., "name", difficulty) 选择房间，
            // 而不是封面模式、格式化后的标题或 VSM 文件名。
            if (preset == "auto" && chart.ObjectName == "obj_custom_gimmick")
            {
                string? info = SongFiles.Existing(root, "info.json", "song.json");
                if (info != null)
                {
                    try
                    {
                        using var song = JsonDocument.Parse(File.ReadAllText(info));
                        var j = song.RootElement;
                        string? name = j.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String? n.GetString() : null;
                        if (SongFiles.Difficulty(project.Chart ?? project.Gimmick ?? "").Equals("ENCORE", StringComparison.OrdinalIgnoreCase)
                            && j.TryGetProperty("enc_data", out var enc) && enc.ValueKind == JsonValueKind.Object && enc.TryGetProperty("name",
                            out n) && n.ValueKind == JsonValueKind.String)
                        {
                            name = n.GetString();
                        }
                        if (name != null)
                        {
                            selected = RoomAssociations.PresetFor(name);
                            // 其余特殊房间目前还没有对应的预览预设。
                            if (!RoomAssociations.IsUnassociated(name))
                            {
                                result.RoomAssociation = "start_song: " + name;
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
                    {
                        chart.Diagnostics.Add(new("fx", 0, "Room association metadata unavailable: " + ex.Message));
                    }
                }
            }
            if (selected != null)
            {
                result.Source = "bundled";
                result.Path = System.IO.Path.Combine(Paths.Assets, "RoomFX",
                    selected == "gameplay" ? "scene_gameplay.fx.json" : "scene_gameplay_" + selected + ".fx.json");
                string runtime = System.IO.Path.Combine(project.GimmickAssets ?? Paths.SharedAssetDirectory("GimmickExtras"),
                    "scene_gameplay.runtime.fx.json");
                if (selected == "gameplay" && File.Exists(runtime))
                {
                    result.Path = runtime;
                }
                result.AutoFallback = preset == "auto" && selected == "gameplay" && chart.ObjectName != "obj_base_gimmick"
                    && result.RoomAssociation == null;
                if (result.AutoFallback)
                {
                    chart.Diagnostics.Add(new("fx", 0,
                        "No gameplay room association was provided for this object. AUTO uses scene_gameplay; choose ROOM FX to match the actual game room. Custom jacket mode does not identify a room."));
                }
            }
        }
        if (result.Path != null)
        {
            try
            {
                if (new FileInfo(result.Path).Length > 1024 * 1024)
                {
                    throw new InvalidDataException("FX profile exceeds 1 MiB.");
                }
                using var doc = JsonDocument.Parse(File.ReadAllText(result.Path));
                if (doc.RootElement.TryGetProperty("room", out var room))
                {
                    result.Room = room.GetString();
                }
                var layers = doc.RootElement.GetProperty("layers");
                if (layers.GetArrayLength() > 64)
                {
                    throw new InvalidDataException("At most 64 FX layers are allowed.");
                }
                foreach (var entry in layers.EnumerateArray())
                {
                    AddLayer(entry, result.Path);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or OverflowException)
            {
                chart.Diagnostics.Add(new(result.Path, 0, "FX profile unavailable: " + ex.Message, true));
            }
        }
        void AddLayer(JsonElement entry, string origin)
        {
            try
            {
                var layer = entry.Deserialize<Layer>(ViewerProject.Json) ?? throw new InvalidDataException("Empty FX layer.");
                if (layer.Parameters == null)
                {
                    throw new InvalidDataException("Missing FX parameters object.");
                }
                if (layer.ParameterMods == null || layer.ParameterMods.Count > 32 || layer.ParameterMods.Any(p => string.IsNullOrWhiteSpace(p.Key) || string.IsNullOrWhiteSpace(p.Value)))
                    throw new InvalidDataException("Invalid FX parameter bindings.");
                if (layer.Name is not ("FX_edge" or "FX_film" or "FX_chroma" or "FX_contrast" or "FX_red" or "FX_hue" or "FX_underwater" or "FX_posterize" or "LBG" or "FX_glow" or "glow" or "Effect_1") &&
                    native?.Data?.Sequence?.BackgroundLayers.Values.Contains(layer.Name) != true)
                {
                    return;
                }
                if (layer.Name is "FX_glow" or "glow" && layer.Filter != "_effect_glow")
                {
                    throw new InvalidDataException("Glow layer requires the original _effect_glow filter.");
                }
                if (layer.Name == "Effect_1" && layer.Filter != "_filter_contrast")
                {
                    throw new InvalidDataException("Background Effect_1 requires its contrast filter.");
                }
                if (layer.Name == "FX_underwater" && layer.Filter != "_filter_underwater")
                {
                    throw new InvalidDataException("FX_underwater requires its source underwater filter.");
                }
                if (layer.Name == "FX_posterize" && layer.Filter != "_filter_posterise")
                {
                    throw new InvalidDataException("FX_posterize requires its source posterise filter.");
                }
                if (result.Layers.Any(l => l.Name == layer.Name))
                {
                    throw new InvalidDataException("Duplicate FX layer: " + layer.Name);
                }
                if (layer.Name == "FX_chroma" && layer.Filter is not ("_filter_heathaze" or "_filter_underwater") || layer.Name == "FX_contrast"
                    && layer.Filter != "_filter_colourise" || layer.Name == "FX_red"
                    && layer.Filter is not ("_filter_colourise" or "_filter_colour_balance") || layer.Name == "FX_hue"
                    && layer.Filter != "_filter_hue")
                {
                    throw new InvalidDataException("No matching source parameter binding for " + layer.Name + " / " + layer.Filter);
                }
                if (layer.Name == "FX_film" && layer.Filter != "_filter_old_film")
                    throw new InvalidDataException("FX_film requires the source old-film filter.");
                if (layer.Name == "FX_edge" && layer.Filter != "_filter_edgedetect")
                    throw new InvalidDataException("FX_edge requires the source edge-detection filter.");
                if (layer.Filter == "_filter_edgedetect")
                {
                    if (layer.Number("g_Threshold") is < 0 or > 1) throw new InvalidDataException("Edge threshold must be 0..1.");
                }
                else if (layer.Filter == "_filter_old_film")
                {
                    foreach (string name in FilmParameters) layer.Number(name);
                    if (layer.Number("g_OldFilmBarScale") <= 0 || layer.Number("g_OldFilmRingScale") <= 0)
                        throw new InvalidDataException("Film bar/ring scales must be positive.");
                    string sampler = layer.Parameters["g_OldFilmTexture"].GetString() ?? throw new InvalidDataException("Missing film texture.");
                    string local = System.IO.Path.GetFullPath(sampler, System.IO.Path.GetDirectoryName(origin)!);
                    string bundled = System.IO.Path.Combine(Paths.Assets, "GameFX", sampler + ".png");
                    layer.TexturePath = File.Exists(local) ? local : File.Exists(bundled) ? bundled : throw new FileNotFoundException("Missing film texture: " + sampler);
                    if (new FileInfo(layer.TexturePath).Length > 64 * 1024 * 1024) throw new InvalidDataException("Film texture exceeds 64 MiB.");
                    using var stream = File.OpenRead(layer.TexturePath);
                    var info = ImageInfo.FromStream(stream) ?? throw new InvalidDataException("Invalid film texture.");
                    if (info.Width < 1 || info.Height < 1 || (long)info.Width * info.Height * 4 > 64 * 1024 * 1024)
                        throw new InvalidDataException("Film texture decoded size exceeds limit.");
                }
                else if (layer.Filter is "_filter_heathaze" or "_filter_underwater")
                {
                    foreach (string n in new[]
                    {
                        "g_Distort1Speed",
                        "g_Distort2Speed",
                        "g_Distort1Amount",
                        "g_Distort2Amount",
                        "g_ChromaSpreadAmount",
                        "g_CamOffsetScale"
                    })
                    {
                        layer.Number(n);
                    }
                    foreach (string n in new[]
                    {
                        "g_Distort1Scale",
                        "g_Distort2Scale"
                    })
                    {
                        if (layer.Vector(n, 2).Any(x => x <= 0))
                        {
                            throw new InvalidDataException("FX noise scale must be positive.");
                        }
                    }
                    if (layer.Filter == "_filter_underwater")
                    {
                        foreach (string n in new[]
                        {
                            "g_GlintCol",
                            "g_TintCol",
                            "g_AddCol"
                        })
                        {
                            layer.Vector(n, 4);
                        }
                    }
                    string sampler = layer.Parameters["g_DistortTexture"].GetString() ?? throw new InvalidDataException("Missing noise sampler.");
                    string local = System.IO.Path.GetFullPath(sampler, System.IO.Path.GetDirectoryName(origin)!);
                    string bundled = System.IO.Path.Combine(Paths.Assets, "GameFX", sampler + ".png");
                    layer.TexturePath = File.Exists(local) ? local : File.Exists(bundled) ? bundled : throw new FileNotFoundException("Missing FX noise texture: " + sampler);
                    if (new FileInfo(layer.TexturePath).Length > 64 * 1024 * 1024)
                    {
                        throw new InvalidDataException("FX texture exceeds 64 MiB.");
                    }
                    using var f = File.OpenRead(layer.TexturePath);
                    var info = ImageInfo.FromStream(f) ?? throw new InvalidDataException("Invalid FX texture.");
                    if (info.Width is < 1 or > 16384 || info.Height is < 1 or > 16384 || (long) info.Width * info.Height * 4 > 64 * 1024 * 1024)
                    {
                        throw new InvalidDataException("FX texture decoded size exceeds limit.");
                    }
                }
                else if (layer.Filter == "_filter_colourise")
                {
                    layer.Vector("g_TintCol", 4);
                    layer.Number("g_Intensity");
                }
                else if (layer.Filter == "_filter_hue")
                {
                    layer.Number("g_HueShift");
                    layer.Number("g_HueSaturation");
                }
                else if (layer.Filter == "_filter_colour_balance")
                {
                    foreach (string n in new[]
                    {
                        "g_ColourBalanceShadows",
                        "g_ColourBalanceMidtones",
                        "g_ColourBalanceHighlights"
                    })
                    {
                        layer.Vector(n, 3);
                    }
                }
                else if (layer.Filter == "_filter_contrast")
                {
                    layer.Number("g_ContrastIntensity");
                    layer.Number("g_ContrastBrightness");
                }
                else if (layer.Filter == "_filter_posterise")
                {
                    if (layer.Number("g_ColourLevels") <= 0)
                    {
                        throw new InvalidDataException("Posterise colour levels must be positive.");
                    }
                }
                else if (layer.Filter == "_effect_glow")
                {
                    foreach (string n in new[]
                    {
                        "g_GlowRadius",
                        "g_GlowQuality",
                        "g_GlowIntensity",
                        "g_GlowGamma",
                        "g_GlowAlpha"
                    })
                    {
                        layer.Number(n);
                    }
                    double quality = layer.Number("g_GlowQuality");
                    if (quality < 1 || quality > 16 || quality != Math.Floor(quality) || layer.Number("g_GlowRadius") < 0)
                    {
                        throw new InvalidDataException("Glow requires 1..16 passes and a non-negative radius.");
                    }
                }
                else
                {
                    throw new InvalidDataException("FX filter has no source renderer: " + layer.Filter);
                }
                if (layer.Name == "LBG" && layer.Filter != "_filter_heathaze")
                {
                    throw new InvalidDataException("Custom LBG requires the source _filter_heathaze effect.");
                }
                result.Layers.Add(layer);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or OverflowException)
            {
                chart.Diagnostics.Add(new(origin, 0, "FX layer unavailable: " + ex.Message, true));
            }
        }
        if (chart.ObjectName == "obj_custom_gimmick" && SongFiles.Config(project, "ENABLE_DISTORT_BG") && result.Find("LBG") == null)
        {
            string definitions = System.IO.Path.Combine(project.GimmickAssets ?? Paths.SharedAssetDirectory("GimmickExtras"),
                "filter-definitions.json");
            if (File.Exists(definitions))
            {
                try
                {
                    if (new FileInfo(definitions).Length > 1024 * 1024)
                    {
                        throw new InvalidDataException("FX definitions exceed 1 MiB.");
                    }
                    using var strings = JsonDocument.Parse(File.ReadAllText(definitions));
                    if (strings.RootElement.ValueKind != JsonValueKind.Array || strings.RootElement.GetArrayLength() > 256)
                    {
                        throw new InvalidDataException("Invalid FX definition list.");
                    }
                    foreach (var value in strings.RootElement.EnumerateArray())
                    {
                        string? raw = value.ValueKind == JsonValueKind.String? value.GetString() : null;
                        if (raw == null || !raw.TrimStart().StartsWith('{'))
                        {
                            continue;
                        }
                        using var definition = JsonDocument.Parse(raw);
                        var d = definition.RootElement;
                        if (!d.TryGetProperty("name", out var name) || name.GetString() != "_filter_heathaze")
                        {
                            continue;
                        }
                        var parameters = new Dictionary<string, JsonElement>();
                        foreach (var param in d.GetProperty("parameters").EnumerateArray())
                        {
                            string key = param.GetProperty("name").GetString()!;
                            if (param.TryGetProperty("defaults", out var v) || param.TryGetProperty("default", out v))
                            {
                                parameters.Add(key, v.Clone());
                            }
                        }
                        // InitDistortBG 在深度 700 的 LBG 上创建这个滤镜。
                        // 保留内嵌定义里的真实默认值；谱面的 scale/amount 在绘制时才覆盖。
                        var entry = JsonSerializer.SerializeToElement(new Layer
                        {
                            Name = "LBG",
                            Filter = "_filter_heathaze",
                            Depth = 700,
                            Parameters = parameters
                        });
                        AddLayer(entry, definitions);
                        if (result.Find("LBG") != null)
                        {
                            result.DynamicDefinitions = definitions;
                        }
                        break;
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException or OverflowException)
                {
                    chart.Diagnostics.Add(new(definitions, 0, "Dynamic FX defaults unavailable: " + ex.Message, true));
                }
            }
        }
        foreach (var group in chart.Mods.Where(e => ModLayers.ContainsKey(e.Name)).GroupBy(e => ModLayers[e.Name]))
        {
            bool active = group.Any(e => e.To != ModCatalog.Default(e.Name, e.Proxy) || e.Duration > 0 && e.From != ModCatalog.Default(e.Name,
                e.Proxy));
            if (active && result.Find(group.Key) == null)
            {
                chart.Diagnostics.Add(new("fx", 0,
                    $"{group.Key}: renderer requires original room FX settings in gimmick-fx.json (or attach a .fx.json). No substitute filter is applied. See docs/FX_RESOURCES.md."));
            }
        }
        if (chart.Mods.Any(e => e.Name == "BG_ditortAmount" && e.To != 0) && !chart.Mods.Any(e => e.Name == "BG_ditortScale" && (e.To > 0
            || e.Duration > 0 && e.From > 0 && e.From != 573613)))
        {
            chart.Diagnostics.Add(new("fx", 0,
                "Background heat haze requires positive BG_ditortScale; zero scale with nonzero amount is skipped to avoid undefined shader division."));
        }
        if (chart.ObjectName == "obj_custom_gimmick" && chart.Mods.Any(e => (e.Name == "fx_red" && result.Find("FX_red") == null
            || e.Name is "fx_hue_hue" or "fx_hue_saturation" && result.Find("FX_hue") == null) && e.To != ModCatalog.Default(e.Name, e.Proxy)))
        {
            chart.Diagnostics.Add(new("fx", 0, result.Source == "none"
                || result.AutoFallback? "No verified red/hue layer for this custom object. Preview uses source colourise/hue formulas with fallback ordering; select the actual room to determine these layers." : "Selected room has no red/hue layer. The missing layer is not rendered; choose a different room only if it matches the game."));
        }
        if (result.Find("FX_chroma") is { Visible: false } && chart.Mods.Any(e => e.Name == "fx_chroma_distort" && e.To != 0))
        {
            chart.Diagnostics.Add(new("fx", 0,
                "FX_chroma is hidden in the selected original room. Its values are evaluated but the layer stays hidden; no visibility override is invented."));
        }
        result.Layers.Sort((a, b) => b.Depth.CompareTo(a.Depth));
        return result;
    }
}
