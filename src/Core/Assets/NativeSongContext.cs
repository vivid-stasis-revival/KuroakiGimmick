// 汇集来自工程和歌曲元数据的场景条件，并将匹配的 JSON 补丁应用到对象定义。

using System.Text.Json;
using System.Text.Json.Nodes;

namespace KuroakiGimmick.Core;

/// <summary>提供来自工程或来源元数据的歌曲身份，song_id 不能从标题、目录或 chart_id 推导。</summary>
/// <param name="SongId">可选的游戏数值歌曲 ID。</param>
/// <param name="Source">该身份的来源说明。</param>
public sealed record NativeSongContext(int? SongId, string Source)
{
    /// <summary>是否隐藏曲目信息。</summary>
    public bool ObscureInfo { get; init; }
    /// <summary>当前原版上下文的禁用暂停标志。</summary>
    public bool DisablePause { get; init; }
    /// <summary>Plaudite 场景使用的真结局条件。</summary>
    public bool PlauditeTrueEnding { get; init; }
    /// <summary>当前原版上下文的 Betweenspace 状态。</summary>
    public bool InBetweenspace { get; init; }
    /// <summary>来源工程或歌曲元数据的名称。</summary>
    public string? SongName { get; init; }
    /// <summary>当前场景的原版房间标识。</summary>
    public string? Room { get; init; }
    /// <summary>从工程传入的 JSON 上下文变量。</summary>
    public Dictionary<string, JsonElement> Variables { get; init; } = [];

    /// <summary>优先使用工程指定的 song_id，再尝试读取歌曲元数据。</summary>
    /// <param name="project">当前工程的路径与呈现配置。</param>
    /// <param name="songRoot">可选的歌曲根目录。</param>
    /// <returns>解析出的来源歌曲身份与预览上下文。</returns>
    /// <exception cref="InvalidDataException">显式房间名为空白、歌曲 ID 为负，或输入上下文无效。</exception>
    public static NativeSongContext Resolve(ViewerProject project, string? songRoot)
    {
        project.NativeInput?.Validate();
        if (project.NativeRoom != null && string.IsNullOrWhiteSpace(project.NativeRoom)) throw new InvalidDataException("NativeRoom must be nonempty when specified.");
        string? name = project.SongName;
        if (name == null && songRoot != null)
            name = SongInfo.Read(songRoot)?.Effective(SongFiles.Difficulty(project.Chart ?? project.Gimmick ?? "")).Name;
        NativeSongContext Context(int? id, string source) => new(id, source)
        {
            ObscureInfo = project.NativeObscureInfo,
            DisablePause = project.NativeDisablePause,
            PlauditeTrueEnding = project.NativePlauditeTrueEnding,
            InBetweenspace = project.NativeInBetweenspace,
            SongName = name,
            Room = project.NativeRoom,
            Variables = project.GimmickContext
        };
        if (project.NativeSongId is { } explicitId)
        {
            if (explicitId < 0) throw new InvalidDataException("NativeSongId must be nonnegative.");
            return Context(explicitId, "project.NativeSongId");
        }
        if (songRoot != null && SongFiles.Existing(songRoot, "info.json", "song.json") is { } file)
        {
            try
            {
                using var document = JsonDocument.Parse(ResourceFiles.ReadText(file));
                JsonElement info = document.RootElement;
                // 即使选择 ENCORE，start_song 仍从 song.song_id 设置 song_id_last；song_get_info 的 enc_data 替换仅影响显示及资源查找。
                if (Id(info) is { } id) return Context(id, file + "#song_id");
            }
            catch (Exception ex) when (ResourceFiles.IsResourceError(ex))
            {
                // 曲目信息可省略。无效显示元数据由常规 SongMetadata 加载器报告，其余有效的对象配置仍可使用。
                return Context(null, "unavailable:" + file);
            }
        }
        return Context(null, "unspecified");
    }

    static int? Id(JsonElement item) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("song_id", out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int id) && id >= 0 ? id : null;

    /// <summary>隐藏曲目信息且未指定默认 uialpha 时将其设为 0。</summary>
    /// <param name="definition">当前对象的声明式演出定义。</param>
    public void ApplyDefaults(GimmickDefinition definition)
    {
        if (ObscureInfo && !definition.Defaults.ContainsKey("uialpha")) definition.Defaults["uialpha"] = 0;
    }

    /// <summary>按显式上下文选择并应用有界的 JSON 合并补丁，规则通过数据匹配执行，无需反射。</summary>
    /// <param name="definition">当前对象的声明式演出定义。</param>
    /// <returns>已应用匹配规则的对象定义。</returns>
    /// <exception cref="InvalidDataException">变体结果为空或补丁试图改变来源对象身份。</exception>
    public GimmickDefinition ApplyDefinition(GimmickDefinition definition)
    {
        ApplyDefaults(definition);
        if (definition.ContextRules.Count == 0) return definition;
        JsonObject root = (JsonObject)AppJson.Node(definition, ViewerProject.Json)!;
        string identity = definition.ObjectName;
        bool Matches(GimmickContextCondition condition)
        {
            JsonNode? actual = condition.Key switch
            {
                "songId" => JsonValue.Create(SongId),
                "songName" => JsonValue.Create(SongName),
                "room" => JsonValue.Create(Room ?? definition.SourceRoom),
                "obscureInfo" => JsonValue.Create(ObscureInfo),
                "disablePause" => JsonValue.Create(DisablePause),
                "trueEnding" => JsonValue.Create(PlauditeTrueEnding),
                "inBetweenspace" => JsonValue.Create(InBetweenspace),
                _ => Variables.TryGetValue(condition.Key, out var custom) ? JsonNode.Parse(custom.GetRawText()) : null
            };
            return JsonNode.DeepEquals(actual, JsonNode.Parse(condition.Value.GetRawText())) != condition.Not;
        }
        foreach (var rule in definition.ContextRules)
            if (rule.All.All(Matches)) Merge(root, (JsonObject)JsonNode.Parse(rule.Patch.GetRawText())!);
        var result = AppJson.Deserialize<GimmickDefinition>(root.ToJsonString(), ViewerProject.Json)
            ?? throw new InvalidDataException("Empty context variant.");
        if (result.ObjectName != identity) throw new InvalidDataException("Context variants cannot replace object identity.");
        return result;
    }

    static void Merge(JsonObject target, JsonObject patch, bool identifiers = false)
    {
        foreach (var (name, value) in patch)
        {
            string key = identifiers ? name : target.Select(p => p.Key).FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
            if (value == null)
            {
                target.Remove(key);
                continue;
            }
            if (value is JsonObject fields)
            {
                if (target[key] is not JsonObject child) target[key] = child = new JsonObject();
                bool dictionary = name is "defaults" or "Defaults" or "valueBindings" or "ValueBindings" or "postModes" or "PostModes"
                    or "sprites" or "Sprites" or "shaders" or "Shaders" or "sourceNoOps" or "SourceNoOps" or "unavailableCallbacks" or "UnavailableCallbacks"
                    or "uniforms" or "Uniforms" or "modAliases" or "ModAliases" or "automaticTriggers" or "AutomaticTriggers";
                Merge(child, fields, dictionary);
            }
            else target[key] = value.DeepClone();
        }
    }
}
