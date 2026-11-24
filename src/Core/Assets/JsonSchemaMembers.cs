using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 与旧版 JsonSerializerOptions.PropertyNameCaseInsensitive 保持一致。
/// 只用于 schema 字段名，不能改变资源 ID、mod 名或 shader uniform 的大小写语义。
/// </summary>
public static class JsonSchemaMembers
{
    /// <summary>按项目的 JSON 成员名称规则查找字段并报告是否命中。</summary>
    /// <param name="value">要查询的 JSON 对象。</param>
    /// <param name="name">字段名，忽略大小写。</param>
    /// <param name="result">接收唯一匹配的字段值。</param>
    /// <returns>找到唯一匹配项时为 true，缺少该字段时为 false。</returns>
    /// <exception cref="InvalidDataException">输入并非 JSON 对象，或同一字段以忽略大小写的形式重复出现。</exception>
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

    /// <summary>读取必需的 JSON 字段，缺失时抛出 InvalidDataException。</summary>
    /// <param name="value">要查询的 JSON 对象。</param>
    /// <param name="name">必需的字段名。</param>
    /// <returns>指定字段的 JSON 值。</returns>
    /// <exception cref="InvalidDataException">根值不是对象，或必需字段缺失、有歧义。</exception>
    public static JsonElement Get(JsonElement value, string name) => TryGet(value, name,
        out var result) ? result : throw new InvalidDataException("Missing JSON field: " + name);
}
