using System.Text.Json.Serialization;
using KuroakiGimmick.UI;

namespace KuroakiGimmick.Core;

/// <summary>为编辑器手册数据生成 JSON 序列化元数据。</summary>
[JsonSerializable(typeof(EditorManual.Data))]
internal partial class ManualJsonContext : JsonSerializerContext;
