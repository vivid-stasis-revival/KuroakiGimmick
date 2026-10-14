using System.Text.Json;

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

    public static NativeSongContext Resolve(ViewerProject project, string? songRoot)
    {
        project.NativeInput?.Validate();
        if (project.NativeRoom != null && string.IsNullOrWhiteSpace(project.NativeRoom)) throw new InvalidDataException("NativeRoom must be nonempty when specified.");
        string? name = project.SongName;
        if (name == null && songRoot != null)
            name = SongInfo.Read(songRoot)?.Effective(SongFiles.Difficulty(project.Chart ?? project.Gimmick ?? "")).Name;
        NativeSongContext Context(int? id, string source) => new(id, source)
        { ObscureInfo = project.NativeObscureInfo, DisablePause = project.NativeDisablePause, PlauditeTrueEnding = project.NativePlauditeTrueEnding,
            InBetweenspace = project.NativeInBetweenspace, SongName = name, Room = project.NativeRoom };
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
        // cc.Create initializes this before the gimmick's Create event. Preserve
        // any explicit child-object assignment rather than overriding it globally.
        if (ObscureInfo && !definition.Defaults.ContainsKey("uialpha")) definition.Defaults["uialpha"] = 0;
        ApplyStoryRoomGuards(definition);
        // obj_sekaisen_gimmick.Create_0 compares global.song_id_last with 205.
        // The unknown/default branch is the source's ordinary 0/100 initialization.
        if (definition.ObjectName == "obj_sekaisen_gimmick" && SongId == 205)
        {
            definition.Defaults["sekaisen_arrow_point"] = 100;
            definition.Defaults["sekaisen_target_point"] = 0;
        }
        if (definition.ObjectName == "obj_distortedfate_gimmick")
        {
            definition.Defaults["uialpha"] = ObscureInfo ? 0 : 1;
            definition.Defaults["df_countdown"] = SongName == "Renatus" ? 244.11 : 0;
        }
        if (definition.ObjectName == "obj_angelstar_gimmick" && definition.Sequence is { } sequence)
        {
            if (DisablePause) sequence.AutomaticTriggers["angelstar_ending"] = 179;
            else sequence.AutomaticTriggers.Remove("angelstar_ending");
        }
        if (definition.ObjectName == "obj_stargazers_gimmick") definition.HudBeforePost = !InBetweenspace;
        if (definition.ObjectName == "obj_astellion_gimmick" && (Room ?? definition.SourceRoom) != "scene_gameplay_fl_astellion")
        {
            definition.Defaults["uialpha"] = 1;
            definition.Defaults["bgalph"] = 1;
            definition.Background = null;
            definition.Overlays.Clear();
            definition.ReplaceJudgmentOverlay = false;
            definition.DisableJacketMultiply = false;
            definition.AmbientParticles = true;
            definition.RoomPreset = "gameplay";
        }
        if (definition.ObjectName == "obj_plaudite_gimmick" && definition.Sequence is { } plaudite)
        {
            if (PlauditeTrueEnding) plaudite.SongSeeks.RemoveAll(x => x.Trigger == "setup_co_sect1");
            if (!PlauditeTrueEnding)
            {
                definition.Interludes.RemoveAll(x => x.Trigger is "setup_co_sect1" or "setup_co_sect8");
                plaudite.Story.RemoveAll(x => x.Trigger == "setup_co_sect1");
                plaudite.StoryWindows.RemoveAll(x => x.Trigger == "setup_co_sect1");
                plaudite.SceneActions.RemoveAll(x => x.Trigger == "setup_co_sect1");
            }
            plaudite.ModWrites.RemoveAll(x => x.Condition == "plaudite_true_ending" && !PlauditeTrueEnding
                || x.Condition == "!plaudite_true_ending" && PlauditeTrueEnding);
        }
    }

    void ApplyStoryRoomGuards(GimmickDefinition definition)
    {
        string? room=Room??definition.SourceRoom;
        string name=definition.ObjectName;
        if(name is "obj_self_gimmick" or "obj_stopmotion_gimmick")
        {
            string nativeRoom=name=="obj_self_gimmick"?"scene_gameplay_self":"scene_gameplay_stopmotion";
            if(room==nativeRoom)return;
            // These helpers and callbacks are created only inside the native
            // room guard. The else branch writes the UI values every Step.
            definition.SceneComposite=null;
            definition.CompositePasses.Clear();
            definition.Stopmotion=null;
            definition.Interludes.Clear();
            definition.RoomLayers.Clear();
            definition.Shaders.Clear();
            definition.AmbientParticles=room=="scene_gameplay";
            definition.RoomPreset=room=="scene_gameplay"?"gameplay":"none";
            definition.DisableJacketMultiply=room!="scene_gameplay";
            foreach(string mod in new[]{"uialpha","scorealph","holdoverlayalpha","bgalph","noteoverlayalp"})
            {
                definition.Defaults[mod]=1;
                definition.ValueBindings[mod]=new(){Constant=1};
            }
            foreach(string callback in definition.ExtraMods.Where(mod=>StopmotionState.IsCallback(name,mod)))
                definition.SourceNoOps[callback]="Source room-guarded callback is undefined outside "+nativeRoom+"; its event has no effect here.";
            if(name=="obj_self_gimmick")
                definition.RoomLayers.Add(new(){Name="self_stargazing",Depth=701,Alpha=0});
            return;
        }
        if(name is "obj_supernova_gimmick" or "obj_libertia_gimmick" or "obj_convergence_gimmick")
        {
            string nativeRoom="scene_gameplay_"+name[4..^8];
            if(room==nativeRoom)return;
            definition.RoomLayers.Clear();
            definition.AmbientParticles=room=="scene_gameplay";
            definition.RoomPreset=room=="scene_gameplay"?"gameplay":"none";
            definition.DisableJacketMultiply=room!="scene_gameplay";
            if(definition.Sequence is { } sequence)
            {
                sequence.Sprites.Remove("cg");
                sequence.CgEffect=null;
                sequence.Story.Clear();sequence.StoryWindows.Clear();sequence.AudioCues.Clear();
                definition.UnavailableCallbacks["setup_co"]="The source coroutine reads o_"+name[4..^8]+"_cg.depth, but the source Create event does not create that CG outside "+nativeRoom+".";
            }
            // No CG instance is created here, so its correctly named writes
            // cannot carry into the next root pass. Preserve only the source's
            // misspelled no-op lookups in that pass.
            foreach(var mode in definition.PostModes.Values)
            {
                mode.Uniforms.Remove("aberrationx");
                mode.Uniforms.Remove("aberrationy");
            }
        }
    }
}
