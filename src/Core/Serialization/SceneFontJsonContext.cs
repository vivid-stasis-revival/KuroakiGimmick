using System.Text.Json.Serialization;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>为场景字体的字形字典生成 JSON 序列化元数据。</summary>
[JsonSerializable(typeof(Dictionary<string, SceneFont.Glyph>))]
internal partial class SceneFontJsonContext : JsonSerializerContext;
