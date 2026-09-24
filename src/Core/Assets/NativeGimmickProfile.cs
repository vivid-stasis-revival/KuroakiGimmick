using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 一次会话中解析完成的对象资源。加载与验证只发生在会话构建时，GPU 资源由渲染器持有。
/// 配置错误按组件记录；某张图损坏不应让其它有效图层一起消失。
/// </summary>
public sealed class NativeGimmickProfile
{
    public GimmickDefinition? Data { get; private set; }
    public string? Manifest { get; private set; }
    public string? DefinitionSource { get; private set; }
    public string AssetsRoot { get; private set; } = Paths.Assets;
    public List<string> DefinitionMigrations { get; } = [];
    /// <summary>
    /// 字段缺失时回退到 CompleteObject，与显式 false 不等价。
    /// 旧 v1 完整原生对象因此继续走真实 FX_red / colour-balance 链路，而不是额外叠一层红色矩形。
    /// </summary>
    public bool UseNativeColorControls => Data is { } definition && (definition.UseNativeColorControls ?? definition.CompleteObject);
    public string? ResourceManifest { get; private set; }
    public string? ResourceRoom { get; private set; }
    public bool ResourcePackLoaded { get; private set; }
    public Dictionary<string, GimmickSprite> Sprites { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> TextureFiles { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Shaders { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Resources { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Issues { get; } = new(StringComparer.Ordinal);
    private readonly HashSet<string> supported = new(StringComparer.Ordinal);
    private readonly HashSet<GimmickShaderMode> invalidModes = [];
    private readonly Dictionary<string, JsonElement> parameters = new(StringComparer.Ordinal);
    private Chart? chart;
    public bool HasStage(string stage) => Data != null && (Data.Overlays.Any(drawing => drawing.Stage == stage)
        || Data.Callbacks.Values.Any(drawings => drawings.Any(drawing => drawing.Stage == stage)));
    public bool Supports(string mod) => supported.Contains(mod);
    public bool IsModeAvailable(GimmickShaderMode mode) => !invalidModes.Contains(mode);

    /// <summary>去重相同的运行期错误，避免每帧编译失败或贴图缺失淹没报告。</summary>
    public void Fail(string component, string message)
    {
        if (Issues.TryGetValue(component, out string? previous) && previous == message)
        {
            return;
        }
        Issues[component] = message;
        chart?.Diagnostics.Add(new("native-gimmick/" + component, 0, message, true));
        Console.Error.WriteLine("Object resource error [" + component + "]: " + message);
    }

    public static string[] ReadExtraMods(string objectName) => GimmickCatalog.ReadExtraMods(objectName);
    public static NativeGimmickProfile Load(Chart chart) => Load(new ViewerProject(), chart);
    /// <summary>
    /// 解析定义、资源 manifest、图片尺寸与 shader 文本。此处不创建任何 GPU 纹理，CPU profile 不持有 GPU 指针。
    /// 除定义本身无法读取外一律不中断：每个精灵/纹理/shader/shader mode 的失败按组件记入 Issues，
    /// 其余有效资源继续加载并可预览。整个加载共用一份解码预算。
    /// </summary>
    public static NativeGimmickProfile Load(ViewerProject project, Chart chart, string? assetsRoot = null)
    {
        var result = new NativeGimmickProfile
        {
            chart = chart,
            AssetsRoot = assetsRoot ?? Paths.Assets
        };
        if (project.Profile == "core")
        {
            return result;
        }
        GimmickDefinition definition;
        string definitionRoot;
        string? songRoot = SongFiles.Root(project);
        try
        {
            var source = GimmickCatalog.Resolve(chart.ObjectName, songRoot, project.GimmickDefinition, project.Profile, result.AssetsRoot);
            if (source == null)
            {
                return result;
            }
            result.Manifest = source.Location;
            result.DefinitionSource = source.Origin;
            definitionRoot = source.ResourceRoot;
            definition = GimmickDefinitionReader.Read(source.Json, result.DefinitionMigrations, result.Fail);
            if (source.Origin == "embedded")
            {
                chart.Diagnostics.Add(new("native-gimmick/definition-source", 0,
                    "External object definition not found; using the definition embedded in this build. Assets: " + result.AssetsRoot));
            }
            GimmickValidation.Validate(definition);
            if (project.Profile == "auto" && definition.ObjectName != chart.ObjectName)
            {
                throw new InvalidDataException($"Object definition is for {definition.ObjectName}, not {chart.ObjectName}.");
            }
            result.Data = definition;
        }
        catch (Exception ex) when (ResourceFiles.IsResourceError(ex))
        {
            result.Fail("definition", ex.Message);
            return result;
        }
        string packRoot = definitionRoot;
        var spriteDefinitions = new Dictionary<string, GimmickSprite>(definition.Sprites, StringComparer.Ordinal);
        if (definition.ResourcePack != null)
        {
            try
            {
                if (songRoot == null)
                {
                    throw new InvalidDataException("The object requires a resource pack, but no song directory is available.");
                }
                result.ResourceManifest = ResourceFiles.ContainedFile(songRoot, definition.ResourcePack);
                packRoot = Path.GetDirectoryName(result.ResourceManifest)!;
                using var document = JsonDocument.Parse(ResourceFiles.ReadText(result.ResourceManifest));
                JsonElement root = document.RootElement;
                if (JsonSchemaMembers.Get(root, "version").GetInt32() != 1)
                {
                    throw new InvalidDataException("Unsupported resource pack version.");
                }
                if (JsonSchemaMembers.TryGet(root, "sprites", out var sprites))
                {
                    var external = AppJson.Deserialize<Dictionary<string,
                        GimmickSprite>>(sprites, ViewerProject.Json) ?? throw new InvalidDataException("Resource pack has no sprite dictionary.");
                    if (external.Count > 64)
                    {
                        throw new InvalidDataException("Resource pack exceeds 64 sprite groups.");
                    }
                    foreach (var (name, sprite) in external)
                    {
                        spriteDefinitions[name] = sprite;
                    }
                }
                if (JsonSchemaMembers.TryGet(root, "room", out var room) && room.ValueKind == JsonValueKind.String)
                {
                    result.ResourceRoom = room.GetString();
                }
                JsonElement source = root;
                bool hasParameters = true;
                foreach (string part in definition.ParameterObjectPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (source.ValueKind != JsonValueKind.Object || !JsonSchemaMembers.TryGet(source, part, out var next))
                    {
                        hasParameters = false;
                        break;
                    }
                    source = next;
                }
                if (hasParameters && source.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in source.EnumerateObject())
                    {
                        result.parameters[property.Name] = property.Value.Clone();
                    }
                }
                result.ResourcePackLoaded = true;
                result.Resources["resource-pack"] = result.ResourceManifest;
            }
            catch (Exception ex) when (ResourceFiles.IsResourceError(ex))
            {
                result.Fail("manifest", ex.Message);
            }
        }
        var budget = new ImageResourceBudget();
        string RootFor(string? scope) => scope switch
        {
            null or "pack" => packRoot,
            "definition" => definitionRoot,
            "shared" => result.AssetsRoot,
            _ => throw new InvalidDataException("Unknown resource scope: " + scope)
        };
        foreach (var (name, sprite) in spriteDefinitions)
        {
            try
            {
                GimmickValidation.ValidateSprite(sprite);
                if (definition.RequiredSprites.TryGetValue(name, out var expected) && (sprite.Width != expected.Width
                    || sprite.Height != expected.Height || sprite.Frames.Count != expected.Frames))
                {
                    throw new InvalidDataException($"Expected {expected.Width}x{expected.Height}, {expected.Frames} frame(s).");
                }
                var frames = new List<string>();
                foreach (string relative in sprite.Frames)
                {
                    string file = ResourceFiles.ContainedFile(RootFor(sprite.Scope), relative);
                    if (budget.Check(file) != (sprite.Width, sprite.Height))
                    {
                        // 精灵尺寸按 320×180 逻辑空间声明，且必须与解码出的实际像素尺寸逐像素一致；manifest 写错不做缩放适配。
                        throw new InvalidDataException("Decoded dimensions differ from manifest: " + relative);
                    }
                    frames.Add(file);
                }
                result.Sprites[name] = new GimmickSprite
                {
                    Width = sprite.Width,
                    Height = sprite.Height,
                    OriginX = sprite.OriginX,
                    OriginY = sprite.OriginY,
                    Frames = frames
                };
                result.Resources[name] = string.Join("; ", frames);
            }
            catch (Exception ex) when (ResourceFiles.IsResourceError(ex))
            {
                result.Fail(name, ex.Message);
            }
        }
        foreach (string name in definition.RequiredSprites.Keys)
        {
            if (!spriteDefinitions.ContainsKey(name))
            {
                result.Fail(name, "Missing required sprite entry: " + name);
            }
        }
        foreach (var (name, texture) in definition.Textures)
        {
            try
            {
                IEnumerable<GimmickResourceLocation> candidates = texture.Candidates;
                if (texture.Parameter != null)
                {
                    string parameter = result.ParameterString(texture.Parameter);
                    candidates = texture.Aliases.TryGetValue(parameter, out var aliases) ? aliases : [new GimmickResourceLocation
                    {
                        Path = parameter
                    }];
                }
                string? file = null;
                foreach (var candidate in candidates)
                {
                    try
                    {
                        file = ResourceFiles.ContainedFile(RootFor(candidate.Scope), candidate.Path);
                        break;
                    }
                    catch (FileNotFoundException)
                    {
                        // 只有候选文件缺失才继续试下一个。路径越界或不安全的符号链接是错误，不是回退理由。
                    }
                }
                if (file == null)
                {
                    throw new FileNotFoundException("No declared texture candidate is available: " + name);
                }
                budget.Check(file);
                result.TextureFiles[name] = file;
                result.Resources[name] = file;
            }
            catch (Exception ex) when (ResourceFiles.IsResourceError(ex))
            {
                result.Fail(name, ex.Message);
            }
        }
        foreach (var (name, relative) in definition.Shaders)
        {
            try
            {
                string file = ResourceFiles.ContainedFile(packRoot, relative);
                string source = ResourceFiles.ReadText(file);
                if (!source.Contains("void main", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("GLSL main function is missing.");
                }
                result.Shaders[name] = source;
                result.Resources[name] = file;
            }
            catch (Exception ex) when (ResourceFiles.IsResourceError(ex))
            {
                result.Fail(name, relative + ": " + ex.Message);
            }
        }
        foreach (var (component, mode) in GimmickValidation.ShaderModes(definition))
        {
            try
            {
                foreach (var scalar in mode.Uniforms.Values.SelectMany(values => values))
                {
                    if (scalar.Parameter != null)
                    {
                        double value = result.ParameterNumber(scalar.Parameter, scalar.ParameterIndex);
                        if (scalar.ParameterMinimumExclusive is { } minimum && value <= minimum)
                        {
                            throw new InvalidDataException("Resource parameter must be greater than " + minimum + ": " + scalar.Parameter);
                        }
                    }
                }
            }
            catch (Exception ex) when (ResourceFiles.IsResourceError(ex))
            {
                // 参数不合法只停用这一个 shader mode，其余 pass 和已加载资源照常预览。
                result.invalidModes.Add(mode);
                result.Fail(component, ex.Message);
            }
        }
        result.IndexSupportedMods(definition);
        if (definition.ParticleEmitter is { FidelityNote: { } note } emitter && result.Sprites.ContainsKey(emitter.Sprite))
        {
            chart.Diagnostics.Add(new("native-gimmick/particles", 0, note));
        }
        return result;
    }

    /// <summary>读取可选资源参数的数值；index &gt;= 0 时取数组分量。缺失或非有限值都抛错，不替换成 0。</summary>
    public double ParameterNumber(string name, int index = -1)
    {
        if (!parameters.TryGetValue(name, out var value))
        {
            throw new InvalidDataException("Missing resource parameter: " + name);
        }
        if (index >= 0)
        {
            if (value.ValueKind != JsonValueKind.Array || index >= value.GetArrayLength())
            {
                throw new InvalidDataException($"Resource parameter {name} has no component {index}.");
            }
            value = value[index];
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !double.IsFinite(number))
        {
            throw new InvalidDataException("Missing/non-finite resource parameter: " + name);
        }
        return number;
    }

    private string ParameterString(string name) => parameters.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { } text ? text : throw new InvalidDataException("Missing string resource parameter: " + name);
    /// <summary>汇总本对象声明能处理的全部 mod 名，供时间轴判断哪些 mod 有归属；只收集名字，不求值。</summary>
    private void IndexSupportedMods(GimmickDefinition definition)
    {
        if (definition.Sequence is { } sequence)
        { supported.UnionWith(sequence.Mods.Values); supported.UnionWith(sequence.Story.Select(c => c.Trigger)); }
        supported.UnionWith(definition.SourceNoOps.Keys);
        supported.UnionWith(definition.Callbacks.Keys);
        supported.UnionWith(definition.Defaults.Keys);
        supported.UnionWith(definition.ModAliases.Keys);
        supported.UnionWith(definition.ModAliases.Values);
        supported.UnionWith(definition.CallbackFades.Keys);
        foreach (var outputs in definition.PerFrameBindings.Values)
        {
            supported.UnionWith(outputs.Keys);
        }
        foreach (var drawing in definition.Overlays.Concat(definition.Callbacks.Values.SelectMany(items => items)))
        {
            if (drawing.AlphaMod != null)
            {
                supported.Add(drawing.AlphaMod);
            }
        }
        if (definition.PostModes.Count > 0)
        {
            supported.Add(definition.PostModeMod);
        }
        foreach (var scalar in GimmickValidation.ShaderModes(definition).SelectMany(pair => pair.Mode.Uniforms.Values).SelectMany(values => values).Concat(definition.PerFrameBindings.Values.SelectMany(bindings => bindings.Values)))
        {
            supported.UnionWith(scalar.Mods);
            if (scalar.MultiplyMod != null)
            {
                supported.Add(scalar.MultiplyMod);
            }
        }
        if (definition.ParticleEmitter is { } emitter)
        {
            supported.Add(emitter.IntervalMod);
            if (emitter.SaturationMod != null)
            {
                supported.Add(emitter.SaturationMod);
            }
        }
    }
}
