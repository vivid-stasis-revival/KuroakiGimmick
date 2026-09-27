using System.Text.Json;
using System.Text.Json.Nodes;

namespace KuroakiGimmick.Core;

/// <summary>
/// 音符皮肤布局契约和兼容读取。源精灵尺寸、裁切区域与显示尺寸分别记录，尤其不能裁掉完整 bumper 帧两侧标记。
/// </summary>
public sealed class NoteSkinProfile
{
    readonly Dictionary<string, NoteSkinFrame> frames;
    public string Root { get; }
    public string Format { get; }
    public bool ExactLaneFrames { get; }
    public IReadOnlyList<string> Warnings { get; }
    public IEnumerable<NoteSkinFrame> Frames => frames.Values;
    /// <summary>本包提供的皮肤名，下标就是 changeskin 的取值；只有一套时它就是 ["normal"]。</summary>
    public IReadOnlyList<string> SkinNames { get; }
    public string Description => (ExactLaneFrames? $"{Format} / exact lanes" : Format) + (SkinNames.Count > 1
        ? " / skins: " + String.Join(", ", SkinNames)
        : "");
    NoteSkinProfile(string root, string format, bool exactLaneFrames, Dictionary<string, NoteSkinFrame> frames, List<string> warnings,
        IReadOnlyList<string>? skinNames = null)
    {
        Root = root;
        Format = format;
        ExactLaneFrames = exactLaneFrames;
        this.frames = frames;
        Warnings = warnings;
        SkinNames = skinNames is { Count: > 0 } ? skinNames : ["normal"];
        Validate();
    }

    public NoteSkinFrame? Chip(int lane, int skin = 0) => Find(lane < 2 ? "chip_L" : "chip_R", skin);
    public NoteSkinFrame? ChipMine(int lane, int skin = 0) => Find(lane < 2 ? "chip_mine_L" : "chip_mine_R", skin);
    public NoteSkinFrame? HoldHead(int lane, int skin = 0) => Find(lane < 2 ? "hold_head_L" : "hold_head_R", skin);
    public NoteSkinFrame? HoldBody(int lane, int skin = 0) => Find(lane < 2 ? "hold_body_L" : "hold_body_R", skin);
    /// <summary>
    /// 长条尾帽。正常皮肤和 extendnova 的尾帽画的就是 chip 帧，原表里没有独立精灵，导出的包里也就没有
    /// hold_end 这个键；更早的单套 dump 则是一个都没有。取不到时退回头帧，和原版一致。
    /// 四套皮肤里没有任何一套把尾帽写成 sp_empty，所以这个退路不会掩盖"故意不画"的情况。
    /// </summary>
    public NoteSkinFrame? HoldEnd(int lane, int skin = 0)
        => frames.GetValueOrDefault(Scoped(lane < 2 ? "hold_end_L" : "hold_end_R", Resolve(skin))) ?? HoldHead(lane, skin);
    public NoteSkinFrame? Bumper(int lane, int skin = 0) => Find("bumper_" + Side(lane), skin);
    public NoteSkinFrame? BumperMine(int lane, int skin = 0) => Find("bumper_mine_" + Side(lane), skin);
    public NoteSkinFrame? JudgeBumper(int lane, int skin = 0) => Find("judge_bumper_" + Side(lane), skin);
    public NoteSkinFrame Get(string key) => frames.TryGetValue(key,
        out var frame) ? frame : throw new InvalidDataException("Note skin mapping is missing " + key + ".");

    /// <summary>
    /// 按皮肤下标取帧。返回 null 表示"这套皮肤不画这类音符"，调用方应当什么都不画。
    ///
    /// 只有 0 号（正常）皮肤是强制完整的，<see cref="Validate"/> 校验过它的每一个键，所以取不到就是包坏了，
    /// 直接抛。换肤目标少一个键则有两种截然不同的原因，这里必须分开：整套皮肤根本没导出来（旧的单套
    /// dump），<see cref="Resolve"/> 把它折回 0 号，总比满屏音符凭空消失强；皮肤导出了而只缺这个键，
    /// 那是原表里写的就是 sp_empty —— 比如 stopmotion 没有任何地雷、也没有中间那根 bumper ——
    /// 这种就得老老实实不画。
    /// </summary>
    NoteSkinFrame? Find(string key, int skin)
    {
        int resolved = Resolve(skin);
        return resolved == 0 ? Get(key) : frames.GetValueOrDefault(Scoped(key, resolved));
    }

    /// <summary>把 changeskin 的取值折到本包真有的皮肤上：越界或负数都当正常皮肤。</summary>
    int Resolve(int skin) => skin > 0 && skin < SkinNames.Count ? skin : 0;
    /// <summary>非 0 号皮肤的键在同一张表里加前缀存放，这样纹理加载、校验和诊断都只需要遍历一份。</summary>
    internal static string Scoped(string key, int skin) => skin <= 0 ? key : skin + "/" + key;
    static string Side(int lane) => lane switch
    {
        0 => "L",
        1 => "M",
        2 => "R",
        _ => throw new ArgumentOutOfRangeException(nameof(lane))
    };
    /// <summary>Width/Height/LocalX/LocalY 是 320×180 逻辑空间中的绘制矩形；UV 是 File 内的归一化裁切，两者分别校验。</summary>
    void Validate()
    {
        string[] required = ["chip_L", "chip_R", "chip_mine_L", "chip_mine_R", "hold_head_L", "hold_head_R", "hold_body_L", "hold_body_R",
            "bumper_L", "bumper_M", "bumper_R", "bumper_mine_L", "bumper_mine_M", "bumper_mine_R", "judge_bumper_L", "judge_bumper_M",
            "judge_bumper_R"];
        foreach (string key in required)
        {
            if (!frames.ContainsKey(key))
            {
                throw new InvalidDataException("Note skin is incomplete: " + key + ".");
            }
        }
        // 换肤皮肤允许缺键（那是 sp_empty），但只要声明了某一帧，它的图和几何就得和正常皮肤一样经得起校验。
        foreach (var (key, f) in frames)
        {
            if (!File.Exists(f.File))
            {
                throw new FileNotFoundException("Note skin texture is missing: " + key, f.File);
            }
            if (!float.IsFinite(f.LocalX) || !float.IsFinite(f.LocalY) || !float.IsFinite(f.Width) || !float.IsFinite(f.Height) || f.Width <= 0
                || f.Height <= 0)
            {
                throw new InvalidDataException("Invalid note skin geometry: " + key + ".");
            }
            if (!float.IsFinite(f.UvX) || !float.IsFinite(f.UvY) || !float.IsFinite(f.UvW) || !float.IsFinite(f.UvH) || f.UvX < 0 || f.UvY < 0
                || f.UvW <= 0 || f.UvH <= 0 || f.UvX + f.UvW > 1.0001f || f.UvY + f.UvH > 1.0001f)
            {
                throw new InvalidDataException("Invalid note skin UV crop: " + key + ".");
            }
        }
    }

    /// <summary>
    /// 按候选目录依次尝试并返回第一个可用皮肤。全部失败时汇总各自的失败原因抛出，
    /// 不退回内置替代图形 —— 音符尺寸错了比没有音符更难发现。
    /// </summary>
    public static NoteSkinProfile Load(string assetsRoot)
    {
        // 完整的 v2 导出若被复制到 Assets/NoteSkinFull 则优先采用，因为只有它能保留
        // 真实的逐轨道帧和 Source→Target 偏移。否则接受直接安装的 NotesExact，
        // 或传统的 Assets/Notes。
        string[] candidates = [Path.Combine(assetsRoot, "NoteSkinFull", "NotesExact"), Path.Combine(assetsRoot, "NotesExact"),
            Path.Combine(assetsRoot, "Notes")];
        var failures = new List<string>();
        foreach (string root in candidates)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            string manifest = Path.Combine(root, "manifest.json");
            if (!File.Exists(manifest))
            {
                // 非常旧的 vsnotes 目录只有那十二张 PNG。继续让它们能用，
                // 而不是把 manifest 变成硬性要求。
                if (Path.GetFileName(root).Equals("Notes", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        return LoadLegacyWithoutManifest(root);
                    }
                    catch (Exception ex)
                    {
                        failures.Add("Notes: " + ex.GetBaseException().Message);
                    }
                }
                continue;
            }
            try
            {
                return LoadDirectory(root);
            }
            catch (Exception ex)
            {
                failures.Add(Path.GetFileName(root) + ": " + ex.GetBaseException().Message);
            }
        }
        if (failures.Count > 0)
        {
            throw new InvalidDataException("No usable note skin: " + String.Join(" | ", failures));
        }
        throw new DirectoryNotFoundException("No note skin manifest found. Expected Assets/Notes/manifest.json or a v2 NoteSkinFull/NotesExact dump.");
    }

    public static NoteSkinProfile LoadDirectory(string root)
    {
        root = Path.GetFullPath(root);
        string manifestPath = Path.Combine(root, "manifest.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var json = doc.RootElement;
        string format = json.TryGetProperty("format", out var fmt) && fmt.ValueKind == JsonValueKind.String? fmt.GetString() ! : "legacy-vsnotes";
        return format switch
        {
            "vividstasis-default-notes-v1" => LoadCompactV1(root, json),
            "vividstasis-default-note-skin-exact-lanes-v2" => LoadRawV2(root, json, exact: true),
            "vividstasis-vsnotes-raw-source-v2" => LoadRawV2(root, json, exact: false),
            _ => LoadLegacy(root, json)
        };
    }

    static NoteSkinProfile LoadCompactV1(string root, JsonElement json)
    {
        var warnings = new List<string>();
        if (json.TryGetProperty("warnings", out var ws) && ws.ValueKind == JsonValueKind.Array)
        {
            warnings.AddRange(ws.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));
        }
        var geometry = new Dictionary<string, (float W, float H, float OX, float OY)>(StringComparer.Ordinal);
        if (!json.TryGetProperty("source_sprites", out var sprites) || sprites.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("v1 note dump has no source_sprites metadata.");
        }
        foreach (var sprite in sprites.EnumerateObject())
        {
            var v = sprite.Value;
            geometry[sprite.Name] = (Number(v, "width"), Number(v, "height"), Number(v, "origin_x"), Number(v, "origin_y"));
        }
        // 紧凑版 v1 是以 includePadding:true 导出的，它报告的精灵宽高和原点就是
        // GameMaker 包围盒。看起来与主体分离的 L/M/R 标记是原始帧里有意保留的像素：
        // draw_sprite_ext 会把它们和中间的 bumper 主体一起绘制。
        // 不要从 bumper_mine 推断出 45×7 的裁切，那是另一个精灵。
        var files = new Dictionary<string, NoteSkinFrame>(StringComparer.Ordinal);
        if (!json.TryGetProperty("files", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("v1 note dump has no files table.");
        }
        foreach (var row in rows.EnumerateArray())
        {
            string file = Text(row, "file"), source = Text(row, "source_sprite");
            int sourceFrame = Integer(row, "source_frame", 0);
            if (!geometry.TryGetValue(source, out var g))
            {
                throw new InvalidDataException("Missing sprite geometry for " + source + ".");
            }
            // ExportAsPNG(..., includePadding:true) 已经还原出完整的 GameMaker 逻辑帧，
            // 这里原样使用整帧。当前的普通/判定 bumper 是 107×7、原点 (53,3)；
            // 两侧的 L/M/R 标记与 draw_sprite_ext 中完全一致地保留可见。
            var frame = Frame(Path.Combine(root, file), Path.GetFileNameWithoutExtension(file), g.W, g.H, -g.OX, -g.OY, source, sourceFrame,
                (int) g.W, (int) g.H);
            AssignCompact(files, Path.GetFileNameWithoutExtension(file), frame);
        }
        // v1 有意合并了第二个 chip / hold-head 帧。保留这一兼容行为，
        // 但在诊断里写清楚，而不是假装这个包里真有右侧图片。
        if (files.TryGetValue("chip_L", out var chip) && !files.ContainsKey("chip_R"))
        {
            files["chip_R"] = Clone(chip, "chip_R");
        }
        if (files.TryGetValue("chip_mine_L", out var mine) && !files.ContainsKey("chip_mine_R"))
        {
            files["chip_mine_R"] = Clone(mine, "chip_mine_R");
        }
        if (files.TryGetValue("hold_head_L", out var head) && !files.ContainsKey("hold_head_R"))
        {
            files["hold_head_R"] = Clone(head, "hold_head_R");
        }
        if (files.TryGetValue("bumper_mine_L", out var bm))
        {
            if (!files.ContainsKey("bumper_mine_M"))
            {
                files["bumper_mine_M"] = Clone(bm, "bumper_mine_M");
            }
            if (!files.ContainsKey("bumper_mine_R"))
            {
                files["bumper_mine_R"] = Clone(bm, "bumper_mine_R");
            }
        }
        return new(root, "vividstasis-default-notes-v1", false, files, warnings);
    }

    static NoteSkinProfile LoadLegacyWithoutManifest(string root)
    {
        using var doc = JsonDocument.Parse("{}");
        return LoadLegacy(Path.GetFullPath(root), doc.RootElement);
    }

    static NoteSkinProfile LoadLegacy(string root, JsonElement json)
    {
        float chipW = 22, chipH = 7, bumperW = 45, bumperH = 7;
        if (json.TryGetProperty("logical_chip_size", out var chip) && chip.ValueKind == JsonValueKind.Array)
        {
            (chipW, chipH) = Pair(chip, chipW, chipH);
        }
        if (json.TryGetProperty("logical_bumper_size", out var bumper) && bumper.ValueKind == JsonValueKind.Array)
        {
            (bumperW, bumperH) = Pair(bumper, bumperW, bumperH);
        }
        var f = new Dictionary<string, NoteSkinFrame>(StringComparer.Ordinal);
        NoteSkinFrame Add(string key, string file, float w, float h, float ox, float oy)
        {
            var x = Frame(Path.Combine(root, file), key, w, h, -ox, -oy, null, 0, null, null);
            f[key] = x;
            return x;
        }
        var chipFrame = Add("chip_L", "chip.png", chipW, chipH, MathF.Floor(chipW / 2), MathF.Floor(chipH / 2));
        f["chip_R"] = Clone(chipFrame, "chip_R");
        var mineFrame = Add("chip_mine_L", "chip_mine.png", chipW, chipH, MathF.Floor(chipW / 2), MathF.Floor(chipH / 2));
        f["chip_mine_R"] = Clone(mineFrame, "chip_mine_R");
        var headFrame = Add("hold_head_L", "hold_head.png", chipW, chipH, MathF.Floor(chipW / 2), MathF.Floor(chipH / 2));
        f["hold_head_R"] = Clone(headFrame, "hold_head_R");
        Add("hold_body_L", "hold_body_L.png", chipW, 1, MathF.Floor(chipW / 2), 0);
        Add("hold_body_R", "hold_body_R.png", chipW, 1, MathF.Floor(chipW / 2), 0);
        foreach (string s in new[]
        {
            "L",
            "M",
            "R"
        })
        {
            Add("bumper_" + s, "bumper_" + s + ".png", bumperW, bumperH, MathF.Floor(bumperW / 2), MathF.Floor(bumperH / 2));
        }
        var bumperMine = Add("bumper_mine_L", "bumper_mine.png", bumperW, bumperH, MathF.Floor(bumperW / 2), MathF.Floor(bumperH / 2));
        f["bumper_mine_M"] = Clone(bumperMine, "bumper_mine_M");
        f["bumper_mine_R"] = Clone(bumperMine, "bumper_mine_R");
        foreach (string s in new[]
        {
            "L",
            "M",
            "R"
        })
        {
            Add("judge_bumper_" + s, "judge_bumper_" + s + ".png", bumperW, bumperH, MathF.Floor(bumperW / 2), MathF.Floor(bumperH / 2));
        }
        return new(root, "legacy-vsnotes", false, f, []);
    }

    static NoteSkinProfile LoadRawV2(string root, JsonElement json, bool exact)
    {
        var warnings = new List<string>();
        string dumpRoot = Directory.GetParent(root)?.FullName ?? root;
        var spriteGeometry = ReadSpriteGeometry(Path.Combine(dumpRoot, "metadata", "sprites.json"));
        var targetGeometry = ReadTargetGeometry(Path.Combine(dumpRoot, "metadata", "frames.json"));
        if (targetGeometry.Count == 0)
        {
            warnings.Add("v2 raw-source pack has no metadata/frames.json; TargetX/TargetY are inferred and asymmetric padding may be approximate.");
        }
        var f = new Dictionary<string, NoteSkinFrame>(StringComparer.Ordinal);
        if (!json.TryGetProperty("files", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("v2 note dump has no files table.");
        }
        foreach (var row in rows.EnumerateArray())
        {
            string file = Text(row, "file"), source = Text(row, "source_sprite");
            int sourceFrame = Integer(row, "source_frame", 0);
            float tw, th, tx, ty, ox, oy;
            if (targetGeometry.TryGetValue(source + "#" + sourceFrame, out var t) && spriteGeometry.TryGetValue(source, out var s))
            {
                tw = t.W;
                th = t.H;
                tx = t.X;
                ty = t.Y;
                ox = s.OX;
                oy = s.OY;
            }
            else
            {
                (tw, th) = row.TryGetProperty("target_size", out var target) ? Pair(target, 22, 7) : (22, 7);
                (float bw, float bh) = row.TryGetProperty("bounding_size", out var bound) ? Pair(bound, tw, th) : (tw, th);
                ox = MathF.Floor(bw / 2);
                oy = MathF.Floor(bh / 2);
                tx = (bw - tw) / 2;
                ty = (bh - th) / 2;
            }
            int? physicalW = row.TryGetProperty("width", out var pw) && pw.TryGetInt32(out int pwi) ? pwi : null;
            int? physicalH = row.TryGetProperty("height", out var ph) && ph.TryGetInt32(out int phi) ? phi : null;
            // 换肤皮肤的图放在各自的子目录里，文件名和正常皮肤重名，所以键要带皮肤下标；
            // skin 缺省为 0，旧的单套 dump 于是原样落到主表里。
            int skin = Integer(row, "skin", 0);
            string key = Scoped(Path.GetFileNameWithoutExtension(file), skin);
            var frame = Frame(Path.Combine(root, file), key, tw, th, tx - ox, ty - oy, source, sourceFrame, physicalW, physicalH);
            if (exact)
            {
                f[key] = frame;
            }
            else
            {
                AssignCompact(f, key, frame);
            }
        }
        if (!exact)
        {
            if (f.TryGetValue("chip_L", out var chip) && !f.ContainsKey("chip_R"))
            {
                f["chip_R"] = Clone(chip, "chip_R");
            }
            if (f.TryGetValue("chip_mine_L", out var mine) && !f.ContainsKey("chip_mine_R"))
            {
                f["chip_mine_R"] = Clone(mine, "chip_mine_R");
            }
            if (f.TryGetValue("hold_head_L", out var head) && !f.ContainsKey("hold_head_R"))
            {
                f["hold_head_R"] = Clone(head, "hold_head_R");
            }
            if (f.TryGetValue("bumper_mine_L", out var bm))
            {
                f["bumper_mine_M"] = Clone(bm, "bumper_mine_M");
                f["bumper_mine_R"] = Clone(bm, "bumper_mine_R");
            }
        }
        return new(root, exact ? "vividstasis-default-note-skin-exact-lanes-v2" : "vividstasis-vsnotes-raw-source-v2", exact, f, warnings,
            ReadSkinNames(json));
    }

    /// <summary>
    /// v3 的 dump 会声明它导了哪几套皮肤，下标就是 changeskin 的取值。名字只用于诊断输出，
    /// 真正起作用的是个数 —— 它决定 changeskin 的哪些取值能生效、哪些折回正常皮肤。
    /// 只有一套皮肤的旧 dump 没有这张表，返回 null 让构造函数用默认值。
    /// </summary>
    static string[]? ReadSkinNames(JsonElement json)
    {
        if (!json.TryGetProperty("skins", out var skins) || skins.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var names = new SortedDictionary<int, string>();
        foreach (var row in skins.EnumerateArray())
        {
            int index = Integer(row, "index", -1);
            if (index >= 0)
            {
                names[index] = row.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String? n.GetString() ! : index.ToString();
            }
        }
        // 下标必须从 0 起连续：中间缺一套的话，后面那些的编号就对不上 changeskin 的取值了，
        // 与其错位地画，不如只认前面连续的那一段。
        return names.TakeWhile((pair, i) => pair.Key == i).Select(pair => pair.Value).ToArray();
    }

    static void AssignCompact(Dictionary<string, NoteSkinFrame> f, string name, NoteSkinFrame frame)
    {
        switch (name)
        {
            case "chip":
                f["chip_L"] = Clone(frame, "chip_L");
                break;
            case "chip_mine":
                f["chip_mine_L"] = Clone(frame, "chip_mine_L");
                break;
            case "hold_head":
                f["hold_head_L"] = Clone(frame, "hold_head_L");
                break;
            case "hold_body_L":
                f["hold_body_L"] = Clone(frame, "hold_body_L");
                break;
            case "hold_body_R":
                f["hold_body_R"] = Clone(frame, "hold_body_R");
                break;
            case "bumper_L":
            case "bumper_M":
            case "bumper_R":
                f[name] = Clone(frame, name);
                break;
            case "bumper_mine":
                f["bumper_mine_L"] = Clone(frame, "bumper_mine_L");
                break;
            case "judge_bumper_L":
            case "judge_bumper_M":
            case "judge_bumper_R":
                f[name] = Clone(frame, name);
                break;
        }
    }

    static NoteSkinFrame Frame(string file, string key, float w, float h, float x, float y, string? source, int sourceFrame, int? physicalW,
        int? physicalH, float uvX = 0, float uvY = 0, float uvW = 1, float uvH = 1) => new()
    {
        Key = key,
        File = Path.GetFullPath(file),
        Width = w,
        Height = h,
        LocalX = x,
        LocalY = y,
        SourceSprite = source,
        SourceFrame = sourceFrame,
        ExpectedTextureWidth = physicalW,
        ExpectedTextureHeight = physicalH,
        UvX = uvX,
        UvY = uvY,
        UvW = uvW,
        UvH = uvH
    };
    static NoteSkinFrame Clone(NoteSkinFrame f, string key) => new()
    {
        Key = key,
        File = f.File,
        Width = f.Width,
        Height = f.Height,
        LocalX = f.LocalX,
        LocalY = f.LocalY,
        SourceSprite = f.SourceSprite,
        SourceFrame = f.SourceFrame,
        ExpectedTextureWidth = f.ExpectedTextureWidth,
        ExpectedTextureHeight = f.ExpectedTextureHeight,
        UvX = f.UvX,
        UvY = f.UvY,
        UvW = f.UvW,
        UvH = f.UvH
    };
    static Dictionary<string, (float OX, float OY)> ReadSpriteGeometry(string path)
    {
        var result = new Dictionary<string, (float, float)>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return result;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            if (row.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
            {
                result[n.GetString()!] = (Number(row, "origin_x"), Number(row, "origin_y"));
            }
        }
        return result;
    }

    static Dictionary<string, (float X, float Y, float W, float H)> ReadTargetGeometry(string path)
    {
        var result = new Dictionary<string, (float, float, float, float)>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return result;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            if (!row.TryGetProperty("sprite", out var s) || s.ValueKind != JsonValueKind.String || !row.TryGetProperty("target", out var t)
                || t.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            int frame = Integer(row, "frame", 0);
            result[s.GetString() ! +"#" + frame] = (Number(t, "x"), Number(t, "y"), Number(t, "width"), Number(t, "height"));
        }
        return result;
    }

    public static JsonObject Report(string assetsRoot)
    {
        try
        {
            var p = Load(assetsRoot);
            var b = p.Bumper(0) !;
            return new JsonObject
            {
                ["format"] = p.Format,
                ["root"] = p.Root,
                ["exactLaneFrames"] = p.ExactLaneFrames,
                ["skins"] = AppJson.Node(p.SkinNames.ToArray()),
                ["warnings"] = AppJson.Node(p.Warnings.ToArray()),
                ["bumper"] = new JsonObject
                {
                    ["drawWidth"] = b.Width,
                    ["drawHeight"] = b.Height,
                    ["localX"] = b.LocalX,
                    ["localY"] = b.LocalY,
                    ["uv"] = AppJson.Node(new[] { b.UvX, b.UvY, b.UvW, b.UvH })
                }
            };
        }
        catch (Exception ex)
        {
            return new JsonObject
            {
                ["format"] = "unavailable",
                ["root"] = null,
                ["exactLaneFrames"] = false,
                ["skins"] = new JsonArray(),
                ["warnings"] = AppJson.Node(new[] { ex.GetBaseException().Message })
            };
        }
    }

    static(float, float) Pair(JsonElement value, float a, float b)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return (a, b);
        }
        var items = value.EnumerateArray().Take(2).ToArray();
        return items.Length == 2 && items[0].TryGetSingle(out float x) && items[1].TryGetSingle(out float y) ? (x, y) : (a, b);
    }

    static string Text(JsonElement o, string name) => o.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String? v.GetString() ! : throw new InvalidDataException("Missing string " + name + ".");
    static float Number(JsonElement o, string name) => o.TryGetProperty(name, out var v)
        && v.TryGetSingle(out float n) ? n : throw new InvalidDataException("Missing number " + name + ".");
    static int Integer(JsonElement o, string name, int fallback) => o.TryGetProperty(name, out var v) && v.TryGetInt32(out int n) ? n : fallback;
}
