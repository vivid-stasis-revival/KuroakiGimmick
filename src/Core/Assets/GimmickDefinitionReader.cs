using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// v1 定义的兼容读取。r15 的 perFrameFunctions 只声明函数名；通过随程序内嵌的
/// 表达式模板转换成同一套 PerFrameBindings，不再在时间轴里按曲名或函数名分支。
/// 显式的新式绑定优先，旧字段无法转换时单独报错，不删除其它有效的对象能力。
/// </summary>
public static class GimmickDefinitionReader
{
    /// <summary>
    /// migrations 记录已完成迁移的旧字段；fail 按组件上报无法迁移的旧函数，其余定义继续加载。
    /// 结构本身非法（perFrameFunctions 不是数组、超过 32 项、名字为空）仍直接抛出，不降级成部分可用。
    /// </summary>
    public static GimmickDefinition Read(string json, List<string>? migrations = null, Action<string, string>? fail = null)
    {
        var definition = AppJson.Deserialize<GimmickDefinition>(json,
            ViewerProject.Json) ?? throw new InvalidDataException("Empty object definition.");
        using var document = JsonDocument.Parse(json);
        if (!JsonSchemaMembers.TryGet(document.RootElement, "perFrameFunctions", out var functions))
        {
            return definition;
        }
        if (functions.ValueKind != JsonValueKind.Array || functions.GetArrayLength() > 32)
        {
            throw new InvalidDataException("Legacy perFrameFunctions must be an array with at most 32 names.");
        }
        definition.PerFrameBindings ??= [];
        using Stream stream = typeof(GimmickDefinitionReader).Assembly.GetManifestResourceStream("KuroakiGimmick.LegacyPerFrameBindings.json") ?? throw new InvalidDataException("Embedded legacy expression templates are missing.");
        var templates = AppJson.Deserialize<Dictionary<string, Dictionary<string, GimmickScalar>>>(stream,
            ViewerProject.Json) ?? throw new InvalidDataException("Empty legacy expression templates.");
        foreach (var function in functions.EnumerateArray())
        {
            string? name = function.ValueKind == JsonValueKind.String ? function.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDataException("Legacy per-frame function names must be nonempty strings.");
            }
            if (definition.PerFrameBindings.ContainsKey(name))
            {
                continue;
            }
            if (!templates.TryGetValue(name, out var bindings))
            {
                fail?.Invoke("legacy-per-frame/" + name, "No declarative compatibility template for legacy function: " + name);
                continue;
            }
            definition.PerFrameBindings.Add(name, bindings);
            migrations?.Add("perFrameFunctions/" + name + " -> perFrameBindings");
        }
        return definition;
    }
}
