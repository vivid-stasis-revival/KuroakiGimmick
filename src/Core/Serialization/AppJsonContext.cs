using System.Text.Json.Serialization;

namespace KuroakiGimmick.Core;

// Register application JSON roots so NativeAOT never needs runtime-generated serializers.
[JsonSerializable(typeof(ViewerProject))]
[JsonSerializable(typeof(ViewerSettings))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(Dictionary<int, string>))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, GimmickScalar>>))]
[JsonSerializable(typeof(Dictionary<string, GimmickSprite>))]
[JsonSerializable(typeof(GimmickDefinition))]
[JsonSerializable(typeof(GameFxProfile.Layer))]
[JsonSerializable(typeof(GameUiAssets.Pack))]
[JsonSerializable(typeof(RoomAssociations.Catalog))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(float[]))]
[JsonSerializable(typeof(Timeline.SourceNoOp[]))]
[JsonSerializable(typeof(ChartPlaybackMap.Segment[]))]
[JsonSerializable(typeof(ChartPlaybackMap.Control[]))]
[JsonSerializable(typeof(Diagnostic[]))]
[JsonSerializable(typeof(SongMetadata))]
internal partial class AppJsonContext : JsonSerializerContext;
