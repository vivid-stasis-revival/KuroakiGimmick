using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace KuroakiGimmick.Core;

/// <summary>NativeAOT-safe JSON entry point for both JIT and native builds.</summary>
internal static class AppJson
{
    internal static readonly IJsonTypeInfoResolver Resolver = JsonTypeInfoResolver.Combine(
        AppJsonContext.Default, BpmJsonContext.Default, SceneFontJsonContext.Default, VsmJsonContext.Default,
        ManualJsonContext.Default);
    private static readonly JsonSerializerOptions DefaultOptions = new() { TypeInfoResolver = Resolver };

    private static JsonTypeInfo<T> Info<T>(JsonSerializerOptions? options = null) =>
        (JsonTypeInfo<T>)(options ?? DefaultOptions).GetTypeInfo(typeof(T));

    internal static string Serialize<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(value, Info<T>(options));

    internal static JsonElement SerializeToElement<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.SerializeToElement(value, Info<T>(options));

    internal static JsonNode? Node<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.SerializeToNode(value, Info<T>(options));

    internal static T? Deserialize<T>(string json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize(json, Info<T>(options));

    internal static T? Deserialize<T>(Stream json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize(json, Info<T>(options));

    internal static T? Deserialize<T>(JsonElement json, JsonSerializerOptions? options = null) =>
        json.Deserialize(Info<T>(options));
}
