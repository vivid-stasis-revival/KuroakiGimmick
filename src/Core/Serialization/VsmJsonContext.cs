using System.Text.Json.Serialization;
using KuroakiGimmick.Core.Documentation;

namespace KuroakiGimmick.Core;

[JsonSerializable(typeof(VsmReference.Data))]
internal partial class VsmJsonContext : JsonSerializerContext;
