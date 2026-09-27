using System.Text.Json.Serialization;

namespace KuroakiGimmick.Core;

[JsonSerializable(typeof(BpmMap.Segment[]))]
internal partial class BpmJsonContext : JsonSerializerContext;
