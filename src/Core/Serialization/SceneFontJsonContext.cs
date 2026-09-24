using System.Text.Json.Serialization;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

[JsonSerializable(typeof(Dictionary<string, SceneFont.Glyph>))]
internal partial class SceneFontJsonContext : JsonSerializerContext;
