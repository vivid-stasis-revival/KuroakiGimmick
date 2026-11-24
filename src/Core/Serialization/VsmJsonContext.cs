using System.Text.Json.Serialization;
using KuroakiGimmick.Core.Documentation;

namespace KuroakiGimmick.Core;

/// <summary>为VSM 指令参考数据生成 JSON 序列化元数据。</summary>
[JsonSerializable(typeof(VsmReference.Data))]
internal partial class VsmJsonContext : JsonSerializerContext;
