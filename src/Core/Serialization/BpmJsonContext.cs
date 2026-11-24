using System.Text.Json.Serialization;

namespace KuroakiGimmick.Core;

/// <summary>为BPM 分段数组生成 JSON 序列化元数据。</summary>
[JsonSerializable(typeof(BpmMap.Segment[]))]
internal partial class BpmJsonContext : JsonSerializerContext;
