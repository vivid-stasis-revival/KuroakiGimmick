using KenomaGMK.SDL3;
using System.Text;
using KuroakiGimmick.Core;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

// 使用带版本的二进制缓存格式，使解释器无需通过反射解析 JSON。
internal static class SwitchShaderPack
{
    internal static string Key(string source, bool vertex) => ShaderPack.Key(source, vertex);

    internal static ShaderCompiler.Compiled Load(string source, bool vertex)
    {
        string path = Path.Combine(Paths.Assets, "SwitchShaders", Key(source, vertex) + ".kgsp");
        if (!File.Exists(path))
            throw new InvalidOperationException($"Missing Switch shader {Path.GetFileName(path)}. Re-bake this source on desktop.");
        using var input = File.OpenRead(path);
        var compiled = ShaderPack.Read(input, ShaderPackKind.Switch, source, vertex, (PackedShaderFormat)GpuShaderFormat.SpirV);
        return new(compiled.Code, compiled.Entry, compiled.Uniforms, compiled.Samplers, compiled.NativeSource, GpuShaderFormat.SpirV);
    }
    static void Write(string directory, string source, bool vertex)
    {
        var compiled = ShaderCompiler.Compile(source, vertex, GpuShaderFormat.SpirV);
        using var output = File.Create(Path.Combine(directory, Key(source, vertex) + ".kgsp"));
        ShaderPack.Write(output, ShaderPackKind.Switch, source, vertex,
            new(compiled.Code, compiled.Entry, compiled.Uniforms, compiled.Samplers, compiled.NativeSource, (PackedShaderFormat)compiled.Format));
    }

    internal static void Bake(string directory, IEnumerable<string> extraDirectories)
    {
#if KG_SWITCH || KG_MOBILE
        throw new PlatformNotSupportedException("Bake shaders with the desktop build.");
#else
        Directory.CreateDirectory(directory);
        var sources = new HashSet<(string Source, bool Vertex)>
        {
            (Canvas.VertexSource, true), (SceneRenderer.ProxyProjectionVertexSource, true),
            (Canvas.NoteInstanceVertexSource, true), (Canvas.NoteInstanceFragmentSource, false),
            (SceneRenderer.ProxyGuideVertexSource, true), (SceneRenderer.ProxyGuideFragmentSource, false),
            (CustomGimmickRenderer.FireworkCutoutSource, false), (CustomGimmickRenderer.FireworkCompositeSource, false),
            (NoteSkin.GreenleftStraightSource, false),
            (Canvas.BasicFragmentSource, false), (SceneRenderer.CompositeFragmentSource, false),
            (CustomEffects.CombineFragmentSource, false), (CustomEffects.PosterizeFragmentSource, false)
        };
        // 外部资源包与渲染器必须使用同一 AdaptGml 转换。
        foreach (var root in new[] { Paths.Assets }.Concat(extraDirectories))
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            foreach (var file in GameMakerShader.EnumerateFragments(root))
                sources.Add((Shader.AdaptGml(File.ReadAllText(file)), false));
        }
        foreach (var (source, vertex) in sources) Write(directory, source, vertex);
        Console.WriteLine($"Baked {sources.Count} Switch shaders to {Path.GetFullPath(directory)}");
#endif
    }
}
