using System.Text.Json;
using System.Text.Json.Nodes;

namespace KuroakiGimmick.Core;

/// <summary>Explicit/native metadata identity; never infer song_id from title, directory or chart_id.</summary>
public sealed record NativeSongContext(int? SongId, string Source)
{
    public bool ObscureInfo { get; init; }
    public bool DisablePause { get; init; }
    public bool PlauditeTrueEnding { get; init; }
    public bool InBetweenspace { get; init; }
    public string? SongName { get; init; }
    public string? Room { get; init; }
    public Dictionary<string,JsonElement> Variables { get; init; } = [];

    public static NativeSongContext Resolve(ViewerProject project, string? songRoot)
    {
        project.NativeInput?.Validate();
        if (project.NativeRoom != null && string.IsNullOrWhiteSpace(project.NativeRoom)) throw new InvalidDataException("NativeRoom must be nonempty when specified.");
        string? name = project.SongName;
        if (name == null && songRoot != null)
            name = SongInfo.Read(songRoot)?.Effective(SongFiles.Difficulty(project.Chart ?? project.Gimmick ?? "")).Name;
        NativeSongContext Context(int? id, string source) => new(id, source)
        { ObscureInfo = project.NativeObscureInfo, DisablePause = project.NativeDisablePause, PlauditeTrueEnding = project.NativePlauditeTrueEnding,
            InBetweenspace = project.NativeInBetweenspace, SongName = name, Room = project.NativeRoom, Variables = project.GimmickContext };
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
                // start_song assigns song_id_last = song.song_id even for ENCORE;
                // song_get_info's enc_data substitutions are separate display/resource lookups.
                if (Id(info) is { } id) return Context(id, file + "#song_id");
            }
            catch (Exception ex) when (ResourceFiles.IsResourceError(ex))
            {
                // Song metadata is optional; malformed display metadata must not disable
                // an otherwise valid object profile. The normal SongMetadata loader reports it.
                return Context(null, "unavailable:" + file);
            }
        }
        return Context(null, "unspecified");
    }

    static int? Id(JsonElement item) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("song_id", out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int id) && id >= 0 ? id : null;

    public void ApplyDefaults(GimmickDefinition definition)
    {
        if (ObscureInfo && !definition.Defaults.ContainsKey("uialpha")) definition.Defaults["uialpha"] = 0;
    }

    /// <summary>Apply bounded JSON merge patches selected by explicit context. No object-name dispatch or reflection.</summary>
    public GimmickDefinition ApplyDefinition(GimmickDefinition definition)
    {
        ApplyDefaults(definition);
        if(definition.ContextRules.Count==0)return definition;
        JsonObject root=(JsonObject)AppJson.Node(definition,ViewerProject.Json)!;
        string identity=definition.ObjectName;
        bool Matches(GimmickContextCondition condition)
        {
            JsonNode? actual=condition.Key switch
            {
                "songId"=>JsonValue.Create(SongId), "songName"=>JsonValue.Create(SongName),
                "room"=>JsonValue.Create(Room??definition.SourceRoom), "obscureInfo"=>JsonValue.Create(ObscureInfo),
                "disablePause"=>JsonValue.Create(DisablePause), "trueEnding"=>JsonValue.Create(PlauditeTrueEnding),
                "inBetweenspace"=>JsonValue.Create(InBetweenspace),
                _=>Variables.TryGetValue(condition.Key,out var custom)?JsonNode.Parse(custom.GetRawText()):null
            };
            return JsonNode.DeepEquals(actual,JsonNode.Parse(condition.Value.GetRawText()))!=condition.Not;
        }
        foreach(var rule in definition.ContextRules)
            if(rule.All.All(Matches))Merge(root,(JsonObject)JsonNode.Parse(rule.Patch.GetRawText())!);
        var result=AppJson.Deserialize<GimmickDefinition>(root.ToJsonString(),ViewerProject.Json)
            ??throw new InvalidDataException("Empty context variant.");
        if(result.ObjectName!=identity)throw new InvalidDataException("Context variants cannot replace object identity.");
        return result;
    }

    static void Merge(JsonObject target,JsonObject patch,bool identifiers=false)
    {
        foreach(var (name,value) in patch)
        {
            string key=identifiers?name:target.Select(p=>p.Key).FirstOrDefault(k=>k.Equals(name,StringComparison.OrdinalIgnoreCase))??name;
            if(value==null){target.Remove(key);continue;}
            if(value is JsonObject fields)
            {
                if(target[key] is not JsonObject child)target[key]=child=new JsonObject();
                bool dictionary=name is "defaults" or "Defaults" or "valueBindings" or "ValueBindings" or "postModes" or "PostModes"
                    or "sprites" or "Sprites" or "shaders" or "Shaders" or "sourceNoOps" or "SourceNoOps" or "unavailableCallbacks" or "UnavailableCallbacks"
                    or "uniforms" or "Uniforms" or "modAliases" or "ModAliases" or "automaticTriggers" or "AutomaticTriggers";
                Merge(child,fields,dictionary);
            }
            else target[key]=value.DeepClone();
        }
    }
}
