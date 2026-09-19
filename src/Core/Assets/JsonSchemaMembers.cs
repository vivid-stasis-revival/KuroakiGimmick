using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 与旧版 JsonSerializerOptions.PropertyNameCaseInsensitive 保持一致。
/// 只用于 schema 字段名，不能改变资源 ID、mod 名或 shader uniform 的大小写语义。
/// </summary>
public static class JsonSchemaMembers
{
    public static bool TryGet(JsonElement value, string name, out JsonElement result)
    {
        result = default;
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Expected a JSON object while reading field " + name + ".");
        }
        bool found = false;
        foreach (var property in value.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (found)
            {
                throw new InvalidDataException("Ambiguous duplicate JSON field: " + name);
            }
            found = true;
            result = property.Value;
        }
        return found;
    }

    public static JsonElement Get(JsonElement value, string name) => TryGet(value, name,
        out var result) ? result : throw new InvalidDataException("Missing JSON field: " + name);
}
