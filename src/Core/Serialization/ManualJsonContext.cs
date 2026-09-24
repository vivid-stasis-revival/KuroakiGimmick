using System.Text.Json.Serialization;
using KuroakiGimmick.UI;

namespace KuroakiGimmick.Core;

[JsonSerializable(typeof(EditorManual.Data))]
internal partial class ManualJsonContext : JsonSerializerContext;
